// Assets/Scripts/Managers/ResourceManager.cs
using System.Collections;
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
    private HexGrid grid;
    
    // Prevent multiple initialization
    private bool _isInitialized = false;
    private bool _subscribedToPlanetReady = false;
    private readonly HashSet<int> spawnedPlanetIndices = new HashSet<int>();

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
    }

    private void TrySubscribeToPlanetReady()
    {
        if (_subscribedToPlanetReady) return;
        
        GameManager.OnPlanetFullyGenerated += HandlePlanetFullyGenerated;
        _subscribedToPlanetReady = true;
    }

    private void TryUnsubscribeFromPlanetReady()
    {
        if (!_subscribedToPlanetReady) return;
        GameManager.OnPlanetFullyGenerated -= HandlePlanetFullyGenerated;
        _subscribedToPlanetReady = false;
    }

    private void HandlePlanetFullyGenerated(PlanetGenerator generator)
    {
        if (generator == null) return;
        int planetIndex = generator.planetIndex;
        if (spawnedPlanetIndices.Contains(planetIndex)) return;

        var ts = TileSystem.GetForPlanet(planetIndex);
        if (ts == null || !ts.IsReady())
        {
            Debug.LogWarning("[ResourceManager] TileSystem is not ready; deferring resource spawn.");
            StartCoroutine(WaitForTileSystemAndSpawn(generator));
            return;
        }

        if (!_isInitialized)
        {
            InitializeResourceManager();
        }

        SpawnResourcesOnPlanet(generator, planetIndex);
        spawnedPlanetIndices.Add(planetIndex);
    }

    private IEnumerator WaitForTileSystemAndSpawn(PlanetGenerator generator)
    {
        int planetIndex = generator != null ? generator.planetIndex : 0;
        var ts = TileSystem.GetForPlanet(planetIndex);
        while (ts == null || !ts.IsReady())
        {
            ts = TileSystem.GetForPlanet(planetIndex);
            yield return null;
        }

        if (generator == null || spawnedPlanetIndices.Contains(planetIndex)) yield break;

        if (!_isInitialized)
        {
            InitializeResourceManager();
        }

        SpawnResourcesOnPlanet(generator, planetIndex);
        spawnedPlanetIndices.Add(planetIndex);
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

        // Spawn batching settings (tweak in inspector)
        resourceSpawnBatchSize = Mathf.Max(1, resourceSpawnBatchSize);
        resourceSpawnFramesBetweenBatches = Mathf.Max(0, resourceSpawnFramesBetweenBatches);

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
        spawnedPlanetIndices.Clear();
    }

    /// <summary>
    /// Scatter resource nodes across the map based on each ResourceData's rules.
    /// MULTI-PLANET COMPATIBLE: Spawns resources on all planets
    /// </summary>
    private void SpawnResources()
    {
        // Planet-aware: spawn per planet once its TileSystem is ready (TileSystem.GetTileDataFromPlanet can still fall back to generator).

        // Spawn resources on all known planets (multi-planet is the default)
        if (GameManager.Instance != null)
        {
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
                if (spawnedPlanetIndices.Contains(planetIndex))
                {
                    continue;
                }
                StartCoroutine(SpawnResourcesOnPlanetCoroutine(planetGen, planetIndex));
                spawnedPlanetIndices.Add(planetIndex);
            }
            return;
        }

        // Fallback: single/legacy planet
        if (grid == null || planetGenerator == null)
        {
            Debug.LogWarning("[ResourceManager] Missing grid or planetGenerator, cannot spawn resources");
            return;
        }
        if (!spawnedPlanetIndices.Contains(0))
        {
            StartCoroutine(SpawnResourcesOnPlanetCoroutine(planetGenerator, 0));
            spawnedPlanetIndices.Add(0);
        }
        
    }
    
    // Backwards-compatible wrapper: start the per-planet spawn coroutine.
    private void SpawnResourcesOnPlanet(PlanetGenerator planetGen, int planetIndex)
    {
        if (planetGen == null) return;
        StartCoroutine(SpawnResourcesOnPlanetCoroutine(planetGen, planetIndex));
    }
    
    /// <summary>
    /// Spawn resources on a specific planet
    /// </summary>
    private int resourceSpawnBatchSize = 200;
    private int resourceSpawnFramesBetweenBatches = 0;

    private IEnumerator SpawnResourcesOnPlanetCoroutine(PlanetGenerator planetGen, int planetIndex)
    {
        if (planetGen == null || planetGen.Grid == null) yield break;
        var planetGrid = planetGen.Grid;
        int tileCount = planetGrid.TileCount;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;

        int processed = 0;
        for (int idx = 0; idx < tileCount; idx++)
        {
            // Get tile data specifically from this planet
            var tileData = ts != null ? ts.GetTileDataFromPlanet(idx, planetIndex) : null;
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

                processed++;
                if (processed >= resourceSpawnBatchSize)
                {
                    processed = 0;
                    if (resourceSpawnFramesBetweenBatches > 0)
                        for (int f = 0; f < resourceSpawnFramesBetweenBatches; f++)
                            yield return null;
                    else
                        yield return null;
                }
            }
        }

        yield break;
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
            
            var ts = TileSystem.GetForPlanet(inst.planetIndex) ?? TileSystem.Instance;
            var tileData = ts != null ? ts.GetTileDataFromPlanet(inst.tileIndex, inst.planetIndex) : null;
            if (tileData != null && tileData.owner == civ) ownsTile = true;
            
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
            currentPlanet = GameManager.Instance.currentPlanetIndex;
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
        var ts = TileSystem.GetForPlanet(inst.planetIndex) ?? TileSystem.Instance;
        if (ts != null)
        {
            var tileData = ts.GetTileDataFromPlanet(inst.tileIndex, inst.planetIndex);
            if (tileData != null)
            {
                tileData.resource = null;
                ts.SetTileDataOnPlanet(inst.tileIndex, tileData, inst.planetIndex);
            }
        }

        spawnedResources.Remove(inst);
        Destroy(inst.gameObject);
    }

    // Method to spawn a resource instance
    private void SpawnResourceInstance(ResourceData resource, int tileIndex, int planetIndex)
    {
        if (resource == null) return;

        // Get the TileSystem for this planet and compute a surface position for the resource
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        Vector3 surfacePos = Vector3.zero;
        if (ts != null)
        {
            // Use the surface-aware helper which takes tile elevation into account
            surfacePos = ts.GetTileSurfacePosition(tileIndex, 0f);
        }

        // Retrieve tile data early so we can choose an appropriate parent before instantiation
        var tileData = ts != null ? ts.GetTileDataFromPlanet(tileIndex, planetIndex) : null;

        // Use object pooling if available
        GameObject go = SimpleObjectPool.Instance != null
            ? SimpleObjectPool.Instance.Get(resource.prefab, surfacePos, Quaternion.identity)
            : Instantiate(resource.prefab, surfacePos, Quaternion.identity);

        // Keep hierarchy organized: parent spawned world objects under their planet generator.
        // (Do not change gameplay logic; this is purely scene organization.)
        try
        {
            var planetGen = GameManager.Instance != null ? GameManager.Instance.GetPlanetGenerator(planetIndex) : null;
            if (planetGen != null)
            {
                // Parent resources under the planet's visual layer for better scene organization.
                // Prefer a dedicated resourcesRoot when present. Fallback to per-layer roots,
                // then fall back to the planet GameObject itself.
                Transform parent = null;
                if (planetGen.resourcesRoot != null)
                    parent = planetGen.resourcesRoot.transform;
                else if (tileData != null && tileData.isLand && planetGen.surfaceRoot != null)
                    parent = planetGen.surfaceRoot.transform;
                else if (tileData != null && !tileData.isLand && planetGen.underwaterRoot != null)
                    parent = planetGen.underwaterRoot.transform;
                else
                    parent = planetGen.transform;

                if (parent != null)
                    go.transform.SetParent(parent, true);
            }

            // Ensure the spawned object rests on the terrain surface regardless of prefab pivot.
            // Align by renderer/collider bounds: move object up so its lowest visual/collider point equals the surface Y.
            try
            {
                float surfaceY = surfacePos.y;
                float lowest = float.MaxValue;
                var rends = go.GetComponentsInChildren<Renderer>(true);
                foreach (var r in rends)
                {
                    if (r == null) continue;
                    lowest = Mathf.Min(lowest, r.bounds.min.y);
                }
                var cols = go.GetComponentsInChildren<Collider>(true);
                foreach (var c in cols)
                {
                    if (c == null) continue;
                    lowest = Mathf.Min(lowest, c.bounds.min.y);
                }
                if (lowest != float.MaxValue)
                {
                    float delta = surfaceY - lowest;
                    if (Mathf.Abs(delta) > 0.0001f)
                        go.transform.position = go.transform.position + new Vector3(0f, delta, 0f);
                }
            }
            catch { }
            }
        
        catch { 

        var inst = go.GetComponent<ResourceInstance>() ?? go.AddComponent<ResourceInstance>();
        inst.data = resource;
        inst.tileIndex = tileIndex;
        inst.planetIndex = planetIndex;
        spawnedResources.Add(inst);

        // Update the tile data to reflect the new resource
        if (tileData != null)
        {
            tileData.resource = resource;
            ts?.SetTileDataOnPlanet(tileIndex, tileData, planetIndex);
            // Register resource occupancy: surface or underwater
            try
            {
                var layer = tileData.isLand ? TileLayer.Surface : TileLayer.Underwater;
                (TileOccupancyManager.GetForPlanet(inst.planetIndex) ?? TileOccupancyManager.Instance)?.SetOccupant(tileIndex, go, layer);
            }
            catch {}
        }
    }
}
}
