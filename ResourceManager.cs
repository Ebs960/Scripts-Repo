// Assets/Scripts/Managers/ResourceManager.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ResourceManager : MonoBehaviour
{
    public static ResourceManager Instance { get; private set; }

    [Header("All resource types")]
    public ResourceData[] resourceTypes;

    // all spawned nodes in the world
    private readonly List<ResourceInstance> spawnedResources = new List<ResourceInstance>();

    private PlanetGenerator planetGenerator;
    private SphericalHexGrid grid;
    
    // Prevent multiple initialization
    private bool _isInitialized = false;
    private bool _subscribedToPlanetReady = false;

    void Awake()
    {
        if (Instance == null) 
        {
            Instance = this;
        }
        else 
        {
            Debug.LogWarning("[ResourceManager] Duplicate ResourceManager detected - destroying this instance");
            Destroy(gameObject);
        }
    }

    void OnEnable()
    {
        TrySubscribeToPlanetReady();
    }

    void OnDisable()
    {
        TryUnsubscribeFromPlanetReady();
    }

    void Start()
    {
        TrySubscribeToPlanetReady();
        
        // If TileSystem is already ready, initialize immediately
        if (TileSystem.Instance != null && TileSystem.Instance.IsReady())
        {
            InitializeResourceManager();
        }
    }

    private void TrySubscribeToPlanetReady()
    {
        if (_subscribedToPlanetReady) return;
        if (GameManager.Instance == null) return;
        
        GameManager.Instance.OnPlanetReady += HandlePlanetReady;
        _subscribedToPlanetReady = true;
    }

    private void TryUnsubscribeFromPlanetReady()
    {
        if (!_subscribedToPlanetReady) return;
        if (GameManager.Instance != null)
            GameManager.Instance.OnPlanetReady -= HandlePlanetReady;
        _subscribedToPlanetReady = false;
    }

    private void HandlePlanetReady(int planetIndex)
    {
        // Initialize when planet is ready (TileSystem will be ready by then)
        if (TileSystem.Instance != null && TileSystem.Instance.IsReady())
        {
            InitializeResourceManager();
        }
    }
    
    private void InitializeResourceManager()
    {
        // GUARD: Prevent multiple initialization
        if (_isInitialized)
        {
            return;
        }
        
        // Find references to key components
        planetGenerator = GameManager.Instance?.GetCurrentPlanetGenerator();
        if (planetGenerator != null)
            grid = planetGenerator.Grid;
        
        // Load resources from asset folder if needed
        if (resourceTypes == null || resourceTypes.Length == 0)
        {
            LoadResources();
        }
        
        // CRITICAL FIX: Do NOT spawn resources immediately
        // Wait for explicit call from GameManager when planets are ready
        
        // Start listening to events
        if (TurnManager.Instance != null)
            TurnManager.Instance.OnTurnChanged += HandleTurnChanged;

        _isInitialized = true;
    }
    
    /// <summary>
    /// Called by GameManager when planets are ready for resource spawning
    /// </summary>
    public void SpawnResourcesWhenReady()
    {
        if (!_isInitialized)
        {
            Debug.LogWarning("[ResourceManager] Cannot spawn resources - not initialized yet");
            return;
        }

        SpawnResources();
    }

    void OnDestroy()
    {
        if (TurnManager.Instance != null)
            TurnManager.Instance.OnTurnChanged -= HandleTurnChanged;
    }
    
    /// <summary>
    /// Reset ResourceManager state for a new game
    /// </summary>
    public void ResetForNewGame()
    {
        // Clear existing resources
        foreach (var resource in spawnedResources)
        {
            if (resource != null && resource.gameObject != null)
                Destroy(resource.gameObject);
        }
        spawnedResources.Clear();
        
        // Reset initialization flag
        _isInitialized = false;
    }

    /// <summary>
    /// Scatter resource nodes across the map based on each ResourceData's rules.
    /// MULTI-PLANET COMPATIBLE: Spawns resources on all planets
    /// </summary>
    private void SpawnResources()
    {
        // SAFETY: Don't spawn if already spawned (prevents memory leak)
        if (spawnedResources.Count > 0)
        {
            return;
        }
        
        if (TileSystem.Instance == null || !TileSystem.Instance.IsReady())
        {
            Debug.LogError("[ResourceManager] TileSystem.Instance is not ready! Cannot spawn resources.");
            return;
        }

        // MULTI-PLANET FIX: Spawn resources on all planets, not just current one
        if (GameManager.Instance != null && GameManager.Instance.enableMultiPlanetSystem)
        {
            // Multi-planet mode: iterate through all planets
            var planetData = GameManager.Instance.GetPlanetData();
            foreach (var kvp in planetData)
            {
                int planetIndex = kvp.Key;
                var planetGen = GameManager.Instance.GetPlanetGenerator(planetIndex);
                
                if (planetGen == null || planetGen.Grid == null) 
                {
                    Debug.LogWarning($"[ResourceManager] Planet {planetIndex} generator or grid is null, skipping");
                    continue;
                }
                
                SpawnResourcesOnPlanet(planetGen, planetIndex);
            }
        }
        else
        {
            // Single planet mode: use current planet
            if (grid == null || planetGenerator == null)
            {
                Debug.LogWarning("[ResourceManager] Missing grid or planetGenerator, cannot spawn resources");
                return;
            }
            
            SpawnResourcesOnPlanet(planetGenerator, 0);
        }
        
    }
    
    /// <summary>
    /// Spawn resources on a specific planet
    /// </summary>
    private void SpawnResourcesOnPlanet(PlanetGenerator planetGen, int planetIndex)
    {
        var planetGrid = planetGen.Grid;
        int tileCount = planetGrid.TileCount;

        for (int idx = 0; idx < tileCount; idx++)
        {
            // Get tile data specifically from this planet
            var tileData = TileSystem.Instance.GetTileDataFromPlanet(idx, planetIndex);
            if (tileData == null) continue; // No resources on the moon for now (not supported)

            foreach (var rd in resourceTypes)
            {
                if (rd == null) continue; // Safety check
                
                // skip if biome not allowed
                bool biomeAllowed = false;
                foreach (var b in rd.allowedBiomes)
                    if (b == tileData.biome) { biomeAllowed = true; break; }
                if (!biomeAllowed) continue;

                if (Random.value <= rd.spawnChance)
                {
                    SpawnResourceInstance(rd, idx, planetIndex);
                }
            }
        }
    }

    // Load resources from Resources folder if not set in inspector
    private void LoadResources()
    {
        resourceTypes = ResourceCache.GetAllResourceData();
        if (resourceTypes == null || resourceTypes.Length == 0)
        {
            Debug.LogWarning("No resource types found in Resources/Data/Resources folder. Please assign them in the inspector.");
        }
    }

    /// <summary>
    /// At the start of each civ's turn, grant per-turn yields for resources within its territory.
    /// </summary>
    private void HandleTurnChanged(Civilization civ, int round)
    {
        // Only grant at the start of a civ's own turn
        // (TurnManager invokes OnTurnChanged before civ.BeginTurn)
        var inv = GetInventory(civ);
        foreach (var kv in inv)
        {
            var rd = kv.Key;
            int count = kv.Value;
            civ.food          += rd.foodPerTurn * count;
            civ.gold          += rd.goldPerTurn * count;
            civ.science       += rd.sciencePerTurn * count;
            civ.culture       += rd.culturePerTurn * count;
            civ.policyPoints  += rd.policyPointsPerTurn * count;
            civ.faith         += rd.faithPerTurn * count;
        }
    }

    /// <summary>
    /// Returns how many nodes of each resource a civ controls across all planets (by tile ownership).
    /// Uses tile data owner field for accurate per-planet ownership verification.
    /// </summary>
    public Dictionary<ResourceData,int> GetInventory(Civilization civ)
    {
        var dict = new Dictionary<ResourceData,int>();
        
        if (civ == null) return dict;
        
        foreach (var inst in spawnedResources)
        {
            if (inst == null || inst.data == null) continue;
            
            // Check ownership by querying the tile data from the specific planet
            // This is the authoritative source for ownership (tileData.owner is now always set)
            bool ownsTile = false;
            
            if (TileSystem.Instance != null)
            {
                var tileData = TileSystem.Instance.GetTileDataFromPlanet(inst.tileIndex, inst.planetIndex);
                if (tileData != null && tileData.owner == civ)
                {
                    ownsTile = true;
                }
            }
            
            if (ownsTile)
            {
                if (dict.TryGetValue(inst.data, out int count))
                    dict[inst.data] = count + 1;
                else
                    dict[inst.data] = 1;
            }
        }
        return dict;
    }

    /// <summary>
    /// Returns the spawned ResourceInstance at the given tile index on the specified planet, or null if none.
    /// </summary>
    public ResourceInstance GetResourceInstanceAtTile(int tileIndex, int planetIndex)
    {
        if (spawnedResources.Count == 0) return null;
        return spawnedResources.FirstOrDefault(r => r != null && r.tileIndex == tileIndex && r.planetIndex == planetIndex);
    }

    /// <summary>
    /// Returns the spawned ResourceInstance at the given tile index on the current planet, or null if none.
    /// Convenience overload that uses GameManager.currentPlanetIndex.
    /// </summary>
    public ResourceInstance GetResourceInstanceAtTile(int tileIndex)
    {
        int currentPlanet = 0;
        if (GameManager.Instance != null)
        {
            currentPlanet = GameManager.Instance.enableMultiPlanetSystem 
                ? GameManager.Instance.currentPlanetIndex 
                : 0;
        }
        return GetResourceInstanceAtTile(tileIndex, currentPlanet);
    }

    /// <summary>
    /// Called by a worker's forage action. Grants one-off yields and removes the node.
    /// </summary>
    public void ForageResource(ResourceInstance inst, Civilization civ)
    {
        if (inst == null || inst.data == null) return;
        
        var rd = inst.data;
        civ.food         += rd.forageFood;
        civ.gold         += rd.forageGold;
        civ.science      += rd.forageScience;
        civ.culture      += rd.forageCulture;
        civ.policyPoints += rd.foragePolicyPoints;
        civ.faith        += rd.forageFaith;

        // Clear the tile's resource in the hex data using planet-aware method
        if (TileSystem.Instance != null)
        {
            var tileData = TileSystem.Instance.GetTileDataFromPlanet(inst.tileIndex, inst.planetIndex);
            if (tileData != null)
            {
                tileData.resource = null;
                TileSystem.Instance.SetTileDataOnPlanet(inst.tileIndex, tileData, inst.planetIndex);
            }
        }

        spawnedResources.Remove(inst);
        Destroy(inst.gameObject);
    }

    // Method to spawn a resource instance
    private void SpawnResourceInstance(ResourceData resource, int tileIndex, int planetIndex)
    {
        if (resource == null) return;

        // Get the position for the resource on the specified planet
        Vector3 position = TileSystem.Instance.GetTileCenterFromPlanet(tileIndex, planetIndex);

        // Use object pooling if available
        GameObject go = SimpleObjectPool.Instance != null
            ? SimpleObjectPool.Instance.Get(resource.prefab, position, Quaternion.identity)
            : Instantiate(resource.prefab, position, Quaternion.identity);

        var inst = go.GetComponent<ResourceInstance>() ?? go.AddComponent<ResourceInstance>();
        inst.data = resource;
        inst.tileIndex = tileIndex;
        inst.planetIndex = planetIndex;
        spawnedResources.Add(inst);

        // Update the tile data to reflect the new resource
        var tileData = TileSystem.Instance.GetTileDataFromPlanet(tileIndex, planetIndex);
        if (tileData != null)
        {
            tileData.resource = resource;
            TileSystem.Instance.SetTileDataOnPlanet(tileIndex, tileData, planetIndex);
        }
    }
}
