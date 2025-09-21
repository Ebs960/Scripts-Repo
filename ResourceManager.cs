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

    void Start()
    {
        // Wait for TileSystem before proceeding
        if (TileSystem.Instance == null || !TileSystem.Instance.IsReady())
        {
            Debug.LogWarning("[ResourceManager] TileSystem not ready yet, will retry in Start");
            StartCoroutine(WaitForTileSystem());
            return;
        }
        
        InitializeResourceManager();
    }

    private System.Collections.IEnumerator WaitForTileSystem()
    {
        while (TileSystem.Instance == null || !TileSystem.Instance.IsReady())
        {
            yield return new WaitForSeconds(0.1f);
        }
        
        InitializeResourceManager();
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
        int resourcesSpawned = 0;
        
        

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
                    resourcesSpawned++;
                }
            }
        }
        
        
    }

    // Load resources from Resources folder if not set in inspector
    private void LoadResources()
    {
        resourceTypes = Resources.LoadAll<ResourceData>("Data/Resources");
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
    /// Returns how many nodes of each resource a civ controls (by tile ownership).
    /// </summary>
    public Dictionary<ResourceData,int> GetInventory(Civilization civ)
    {
        var dict = new Dictionary<ResourceData,int>();
        foreach (var inst in spawnedResources)
        {
            if (civ.ownedTileIndices.Contains(inst.tileIndex))
            {
                if (!dict.ContainsKey(inst.data)) dict[inst.data] = 0;
                dict[inst.data]++;
            }
        }
        return dict;
    }

    /// <summary>
    /// Returns the spawned ResourceInstance at the given tile index, or null if none.
    /// </summary>
    public ResourceInstance GetResourceInstanceAtTile(int tileIndex)
    {
        if (spawnedResources == null || spawnedResources.Count == 0) return null;
        return spawnedResources.FirstOrDefault(r => r != null && r.tileIndex == tileIndex);
    }

    /// <summary>
    /// Called by a worker's forage action. Grants one-off yields and removes the node.
    /// </summary>
    public void ForageResource(ResourceInstance inst, Civilization civ)
    {
        var rd = inst.data;
        civ.food         += rd.forageFood;
        civ.gold         += rd.forageGold;
        civ.science      += rd.forageScience;
        civ.culture      += rd.forageCulture;
        civ.policyPoints += rd.foragePolicyPoints;

        // Clear the tile's resource in the hex data
        var tileData = TileSystem.Instance != null ? TileSystem.Instance.GetTileData(inst.tileIndex) : null;
        if (tileData != null) {
            tileData.resource = null;
            if (TileSystem.Instance != null) TileSystem.Instance.SetTileData(inst.tileIndex, tileData);
        }

        spawnedResources.Remove(inst);
        Destroy(inst.gameObject);
    }

    // Method to spawn a resource instance
    private void SpawnResourceInstance(ResourceData resource, int tileIndex, int planetIndex)
    {
        if (resource == null) return;

        // Ensure we have a grid reference
        if (grid == null && planetGenerator != null)
            grid = planetGenerator.Grid;

        if (grid == null) return;

        // Get the position for the resource on the specified planet
    Vector3 position = TileSystem.Instance.GetTileCenterFromPlanet(tileIndex, planetIndex);

        // Use object pooling if available
        GameObject go = SimpleObjectPool.Instance != null
            ? SimpleObjectPool.Instance.Get(resource.prefab, position, Quaternion.identity)
            : Instantiate(resource.prefab, position, Quaternion.identity);

        var inst = go.GetComponent<ResourceInstance>() ?? go.AddComponent<ResourceInstance>();
        inst.data = resource;
        inst.tileIndex = tileIndex;
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
