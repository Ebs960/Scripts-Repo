// Assets/Scripts/Managers/ResourceManager.cs
using System.Collections.Generic;
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
            Debug.Log("[ResourceManager] Instance set in Awake");
        }
        else 
        {
            Debug.LogWarning("[ResourceManager] Duplicate ResourceManager detected - destroying this instance");
            Destroy(gameObject);
        }
    }

    void Start()
    {
        // CRITICAL: Wait for TileDataHelper before proceeding
        if (TileDataHelper.Instance == null)
        {
            Debug.LogWarning("[ResourceManager] TileDataHelper not ready yet, will retry in Start");
            StartCoroutine(WaitForTileDataHelper());
            return;
        }
        
        InitializeResourceManager();
    }
    
    private System.Collections.IEnumerator WaitForTileDataHelper()
    {
        while (TileDataHelper.Instance == null)
        {
            Debug.Log("[ResourceManager] Waiting for TileDataHelper...");
            yield return new WaitForSeconds(0.1f);
        }
        
        Debug.Log("[ResourceManager] TileDataHelper found, initializing...");
        InitializeResourceManager();
    }
    
    private void InitializeResourceManager()
    {
        // GUARD: Prevent multiple initialization
        if (_isInitialized)
        {
            Debug.Log("[ResourceManager] Already initialized, skipping...");
            return;
        }

        Debug.Log("[ResourceManager] Starting initialization...");
        
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
        Debug.Log("[ResourceManager] Initialization complete");
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
        
        Debug.Log("[ResourceManager] Spawning resources on ready planets...");
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
        Debug.Log("[ResourceManager] Resetting for new game");
        
        // Clear existing resources
        foreach (var resource in spawnedResources)
        {
            if (resource != null && resource.gameObject != null)
                Destroy(resource.gameObject);
        }
        spawnedResources.Clear();
        
        // Reset initialization flag
        _isInitialized = false;
        
        Debug.Log("[ResourceManager] Reset complete");
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
            Debug.Log($"[ResourceManager] Resources already spawned ({spawnedResources.Count} resources), skipping");
            return;
        }
        
        if (TileDataHelper.Instance == null)
        {
            Debug.LogError("[ResourceManager] TileDataHelper.Instance is null! Cannot spawn resources.");
            return;
        }

        Debug.Log("[ResourceManager] Starting resource spawn...");
        
        // MULTI-PLANET FIX: Spawn resources on all planets, not just current one
        if (GameManager.Instance != null && GameManager.Instance.enableMultiPlanetSystem)
        {
            Debug.Log("[ResourceManager] Multi-planet mode: spawning resources on all planets");
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
            Debug.Log("[ResourceManager] Single-planet mode: spawning resources on current planet");
            // Single planet mode: use current planet
            if (grid == null || planetGenerator == null)
            {
                Debug.LogWarning("[ResourceManager] Missing grid or planetGenerator, cannot spawn resources");
                return;
            }
            
            SpawnResourcesOnPlanet(planetGenerator, 0);
        }
        
        Debug.Log($"[ResourceManager] Resource spawning completed. Total spawned: {spawnedResources.Count}");
    }
    
    /// <summary>
    /// Spawn resources on a specific planet
    /// </summary>
    private void SpawnResourcesOnPlanet(PlanetGenerator planetGen, int planetIndex)
    {
        var planetGrid = planetGen.Grid;
        int tileCount = planetGrid.TileCount;
        int resourcesSpawned = 0;
        
        Debug.Log($"[ResourceManager] Spawning resources on planet {planetIndex} with {tileCount} tiles");

        for (int idx = 0; idx < tileCount; idx++)
        {
            // Get tile data specifically from this planet
            var (tileData, isMoonTile) = TileDataHelper.Instance.GetTileDataFromPlanet(idx, planetIndex);
            if (tileData == null || isMoonTile) continue; // No resources on the moon for now

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
                    Vector3 pos = TileDataHelper.Instance.GetTileCenterFromPlanet(idx, planetIndex);
                    
                    // Use object pooling if available
                    GameObject go = SimpleObjectPool.Instance != null 
                        ? SimpleObjectPool.Instance.Get(rd.prefab, pos, Quaternion.identity)
                        : Instantiate(rd.prefab, pos, Quaternion.identity);
                        
                    var inst = go.AddComponent<ResourceInstance>();
                    inst.data = rd;
                    inst.tileIndex = idx;
                    spawnedResources.Add(inst);

                    // Mirror into the tile data
                    tileData.resource = rd;
                    TileDataHelper.Instance.SetTileDataOnPlanet(idx, tileData, planetIndex);
                    
                    resourcesSpawned++;
                }
            }
        }
        
        Debug.Log($"[ResourceManager] Spawned {resourcesSpawned} resources on planet {planetIndex}");
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
        var (tileData, isMoonTile) = TileDataHelper.Instance.GetTileData(inst.tileIndex);
        if (tileData != null) {
            tileData.resource = null;
            TileDataHelper.Instance.SetTileData(inst.tileIndex, tileData);
        }

        spawnedResources.Remove(inst);
        Destroy(inst.gameObject);
    }

    // Method to spawn a resource instance
   private void SpawnResourceInstance(ResourceData resource, int tileIndex)
    {
        if (resource == null) return;

        // Ensure we have a grid reference
        if (grid == null && planetGenerator != null)
            grid = planetGenerator.Grid;

        if (grid == null) return;

        // Get the position for the resource
        Vector3 position = TileDataHelper.Instance.GetTileCenter(tileIndex);

        // Instantiate the resource prefab at the tile position
        var go = Instantiate(resource.prefab, position, Quaternion.identity);
        var inst = go.AddComponent<ResourceInstance>();
        inst.data = resource;
        inst.tileIndex = tileIndex;
        spawnedResources.Add(inst);

        // Update the tile data to reflect the new resource
        var (tileData, _) = TileDataHelper.Instance.GetTileData(tileIndex);
        if (tileData != null)
        {
            tileData.resource = resource;
            TileDataHelper.Instance.SetTileData(tileIndex, tileData);
        }
    }
}
