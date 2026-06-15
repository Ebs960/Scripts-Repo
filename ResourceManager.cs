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
    // Spatial index for O(1) tile lookup: (planetIndex, tileIndex) -> ResourceInstance
    private readonly Dictionary<long, ResourceInstance> _tileLookup = new Dictionary<long, ResourceInstance>();
    private static long ResKey(int planetIndex, int tileIndex) => ((long)planetIndex << 32) | (uint)tileIndex;

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

        // Subscribe to tile resource change events so ResourceManager owns the visual instance lifecycle.
        try
        {
            ts.OnTileResourceChanged -= (t, o, n) => HandleTileResourceChanged(t, o, n, planetIndex);
            ts.OnTileResourceChanged += (t, o, n) => HandleTileResourceChanged(t, o, n, planetIndex);
        }
        catch { }

        SpawnResourcesOnPlanet(generator, planetIndex);
        spawnedPlanetIndices.Add(planetIndex);

        // One-time reconciliation to repair any mismatches between TileSystem tile data and spawned instances.
        try
        {
            ReconcilePlanetResources(planetIndex);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ResourceManager] ReconcilePlanetResources failed for planet {planetIndex}: {ex.Message}");
        }
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
        {
            TurnManager.Instance.OnRoundStarted += HandleRoundStarted;
            TurnManager.Instance.OnTurnChanged += HandleTurnChanged;
        }

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
        {
            TurnManager.Instance.OnRoundStarted -= HandleRoundStarted;
            TurnManager.Instance.OnTurnChanged -= HandleTurnChanged;
        }
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
        _tileLookup.Clear();
        
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

                // skip if biome not allowed (check surface biome AND underwater floor biome)
                bool biomeAllowed = false;
                if (rd.allowedBiomes != null)
                    foreach (var b in rd.allowedBiomes)
                        if (b == tileData.biome) { biomeAllowed = true; break; }
                // For ocean tiles, also check the underwater floor biome
                if (!biomeAllowed && rd.allowedUnderwaterBiomes != null && rd.allowedUnderwaterBiomes.Length > 0
                    && tileData.underwaterBiome != Biome.Ocean && tileData.underwaterBiome != tileData.biome)
                {
                    foreach (var ub in rd.allowedUnderwaterBiomes)
                        if (ub == tileData.underwaterBiome) { biomeAllowed = true; break; }
                }
                // Orbital resources: check surface biome below (allowedBiomes) but spawn at orbit layer
                if (!biomeAllowed && rd.isOrbitalResource && rd.allowedBiomes != null)
                {
                    foreach (var b in rd.allowedBiomes)
                        if (b == tileData.biome || b == Biome.Any) { biomeAllowed = true; break; }
                }
                if (!biomeAllowed) continue;

                float spawnChance = rd.spawnChance * Mathf.Max(0f, GameSetupData.resourceSpawnMultiplier);
                if (Random.value <= Mathf.Clamp01(spawnChance))
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

    // Per-round inventory cache: built once on first civ turn of each round
    private int _inventoryCacheRound = -1;
    private Dictionary<Civilization, Dictionary<ResourceData, int>> _inventoryCache
        = new Dictionary<Civilization, Dictionary<ResourceData, int>>();

    private void HandleRoundStarted(int round)
    {
        if (round == _inventoryCacheRound) return;

        _inventoryCacheRound = round;
        _inventoryCache.Clear();
        foreach (var inst in spawnedResources)
        {
            if (inst == null || inst.data == null) continue;
            var ts = TileSystem.GetForPlanet(inst.planetIndex) ?? TileSystem.Instance;
            var tileData = ts != null ? ts.GetTileDataFromPlanet(inst.tileIndex, inst.planetIndex) : null;
            if (tileData == null || tileData.owner == null) continue;
            var owner = tileData.owner;
            if (!_inventoryCache.TryGetValue(owner, out var dict))
            {
                dict = new Dictionary<ResourceData, int>();
                _inventoryCache[owner] = dict;
            }
            if (dict.TryGetValue(inst.data, out int cnt))
                dict[inst.data] = cnt + 1;
            else
                dict[inst.data] = 1;
        }
    }


    public bool IsResourceRevealedForCiv(ResourceData resource, Civilization civ)
    {
        if (resource == null) return true;
        bool needsTech = resource.requiredTechsToReveal != null && resource.requiredTechsToReveal.Length > 0;
        bool needsCulture = resource.requiredCulturesToReveal != null && resource.requiredCulturesToReveal.Length > 0;
        if (!needsTech && !needsCulture) return true;

        if (civ == null) return false;
        if (needsTech && civ.researchedTechs != null)
        {
            foreach (var tech in resource.requiredTechsToReveal)
                if (tech != null && civ.researchedTechs.Contains(tech))
                    return true;
        }
        if (needsCulture && civ.researchedCultures != null)
        {
            foreach (var culture in resource.requiredCulturesToReveal)
                if (culture != null && civ.researchedCultures.Contains(culture))
                    return true;
        }
        return false;
    }

    private Civilization GetLocalViewingCiv()
    {
        return FindObjectsByType<Civilization>().FirstOrDefault(c => c != null && c.isPlayerControlled);
    }

    public void RefreshResourceVisibilityForCiv(Civilization civ = null)
    {
        if (civ == null) civ = GetLocalViewingCiv();
        foreach (var inst in spawnedResources)
        {
            if (inst == null || inst.gameObject == null) continue;
            inst.gameObject.SetActive(IsResourceRevealedForCiv(inst.data, civ));
        }
    }

    /// <summary>
    /// At the start of each civ's turn, grant per-turn yields for resources within its territory.
    /// </summary>
    private void HandleTurnChanged(Civilization civ, int round)
    {
        if (round != _inventoryCacheRound)
            HandleRoundStarted(round);

        // Look up this civ's cached inventory in O(1)
        if (!_inventoryCache.TryGetValue(civ, out var inv)) return;
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
        _tileLookup.TryGetValue(ResKey(planetIndex, tileIndex), out var inst);
        return inst;
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
        // Use centralized civ helpers where available so UI events fire immediately.
        if (rd.forageFood != 0)
            civ.AddFood(rd.forageFood);
        if (rd.forageGold != 0)
            civ.AddGold(rd.forageGold);
        // Science and culture currently don't have centralized Add helpers; update fields directly.
        if (rd.forageScience != 0)
            civ.science += rd.forageScience;
        if (rd.forageCulture != 0)
            civ.culture += rd.forageCulture;
        if (rd.foragePolicyPoints != 0)
            civ.AddPolicyPoints(rd.foragePolicyPoints);
        if (rd.forageFaith != 0)
            civ.AddFaith(rd.forageFaith);

        // Request tile-level removal of the resource; TileSystem will raise event and ResourceManager will destroy the instance.
        TileSystem.SetResourceOnTile(null, inst.tileIndex, inst.planetIndex);
    }

    // Method to spawn a resource instance
    private void SpawnResourceInstance(ResourceData resource, int tileIndex, int planetIndex)
    {
        if (resource == null) return;
        // Get the TileSystem for this planet and compute a surface position for the resource
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        // GUARD: avoid spawning more than one resource on the same tile/planet
        if (GetResourceInstanceAtTile(tileIndex, planetIndex) != null)
        {
            return;
        }
        var _tileCheck = ts != null ? ts.GetTileDataFromPlanet(tileIndex, planetIndex) : null;
        if (_tileCheck != null && _tileCheck.resource != null)
        {
            return;
        }
        Vector3 surfacePos = Vector3.zero;
        if (ts != null)
        {
            // Use the surface-aware helper which takes tile elevation into account
            surfacePos = ts.GetTileSurfacePosition(tileIndex, 0f);
        }

        // Orbital resources float above the surface at the configured orbit height
        if (resource.isOrbitalResource)
        {
            surfacePos.y += PlanetGenerator.GetOrbitHeight(planetIndex);
        }

        // Instead of directly creating instances here, set the authoritative tile resource
        // via TileSystem. ResourceManager listens to OnTileResourceChanged and will
        // create/destroy visual instances in response.
        TileSystem.SetResourceOnTile(resource, tileIndex, planetIndex);
    }

    // Handles tile resource changes from TileSystem. Responsible for creating/destroying ResourceInstance GameObjects.
    private void HandleTileResourceChanged(int tileIndex, ResourceData oldResource, ResourceData newResource, int planetIndex)
    {
        // If a resource was added, ensure an instance exists.
        if (newResource != null)
        {
            if (GetResourceInstanceAtTile(tileIndex, planetIndex) != null) return;
            var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
            Vector3 surfacePos = Vector3.zero;
            if (ts != null) surfacePos = ts.GetTileSurfacePosition(tileIndex, 0f);
            if (newResource.isOrbitalResource) surfacePos.y += PlanetGenerator.GetOrbitHeight(planetIndex);

            GameObject go = SimpleObjectPool.Instance != null
                ? SimpleObjectPool.Instance.Get(newResource.prefab, surfacePos, Quaternion.identity)
                : Instantiate(newResource.prefab, surfacePos, Quaternion.identity);

            // Parent and align
            try
            {
                var planetGen = GameManager.Instance != null ? GameManager.Instance.GetPlanetGenerator(planetIndex) : null;
                var tileData = ts != null ? ts.GetTileDataFromPlanet(tileIndex, planetIndex) : null;
                Transform parent = null;
                if (planetGen != null)
                {
                    if (planetGen.resourcesRoot != null) parent = planetGen.resourcesRoot.transform;
                    else if (tileData != null && tileData.isLand && planetGen.surfaceRoot != null) parent = planetGen.surfaceRoot.transform;
                    else if (tileData != null && !tileData.isLand && planetGen.underwaterRoot != null) parent = planetGen.underwaterRoot.transform;
                    else parent = planetGen.transform;
                }
                if (parent != null) go.transform.SetParent(parent, true);

                float surfaceY = surfacePos.y;
                float lowest = float.MaxValue;
                var rends = go.GetComponentsInChildren<Renderer>(true);
                foreach (var r in rends) if (r != null) lowest = Mathf.Min(lowest, r.bounds.min.y);
                var cols = go.GetComponentsInChildren<Collider>(true);
                foreach (var c in cols) if (c != null) lowest = Mathf.Min(lowest, c.bounds.min.y);
                if (lowest != float.MaxValue)
                {
                    float delta = surfaceY - lowest;
                    if (Mathf.Abs(delta) > 0.0001f) go.transform.position = go.transform.position + new Vector3(0f, delta, 0f);
                }
            }
            catch { }

            var inst = go.GetComponent<ResourceInstance>() ?? go.AddComponent<ResourceInstance>();
            inst.data = newResource;
            inst.tileIndex = tileIndex;
            inst.planetIndex = planetIndex;
            spawnedResources.Add(inst);
            _tileLookup[ResKey(planetIndex, tileIndex)] = inst;
            go.SetActive(IsResourceRevealedForCiv(newResource, GetLocalViewingCiv()));

            // Register resource instance with HexMapChunkManager so it moves during wrap teleport
            try
            {
                var planetGen = GameManager.Instance != null ? GameManager.Instance.GetPlanetGenerator(planetIndex) : null;
                var mgr = FindObjectsByType<HexMapChunkManager>().FirstOrDefault(m => m.PlanetGenerator == planetGen);
                if (mgr != null)
                {
                    mgr.RegisterObjectForWrapAtTile(tileIndex, go);
                }
            }
            catch { }

            // NOTE: Resources no longer register as tile occupants. Occupancy is reserved for units and cities only.
            // This avoids blocking unit movement and selection when resources are present on a tile.
        }
        else
        {
            // Resource removed: destroy instance if present
            var inst = GetResourceInstanceAtTile(tileIndex, planetIndex);
            if (inst != null)
            {
                spawnedResources.Remove(inst);
                _tileLookup.Remove(ResKey(planetIndex, tileIndex));
                try { Destroy(inst.gameObject); } catch { }
            }
        }
    }

    // One-time reconciliation that repairs mismatches between tile data and spawned instances.
    private void ReconcilePlanetResources(int planetIndex)
    {
        var ts = TileSystem.GetForPlanet(planetIndex);
        PlanetGenerator gen = GameManager.Instance != null ? GameManager.Instance.GetPlanetGenerator(planetIndex) : null;
        int fixes = 0;

        if (gen != null && gen.Grid != null && ts != null)
        {
            int tileCount = gen.Grid.TileCount;
            for (int i = 0; i < tileCount; i++)
            {
                var td = ts.GetTileDataFromPlanet(i, planetIndex);
                var inst = GetResourceInstanceAtTile(i, planetIndex);
                bool tileHas = td != null && td.resource != null;
                if (tileHas && inst == null)
                {
                    // Create missing instance to match tile data
                    HandleTileResourceChanged(i, null, td.resource, planetIndex);
                    if (ShouldLogForPlanet(planetIndex)) Debug.Log($"[ResourceManager][Reconcile] Created ResourceInstance for tile {i} resource '{td.resource.resourceName}' on planet {planetIndex}");
                    fixes++;
                }
                else if (!tileHas && inst != null)
                {
                    // Remove stray instance
                    HandleTileResourceChanged(i, inst.data, null, planetIndex);
                    if (ShouldLogForPlanet(planetIndex)) Debug.Log($"[ResourceManager][Reconcile] Destroyed stray ResourceInstance at tile {i} on planet {planetIndex}");
                    fixes++;
                }
            }
        }
        else if (ts != null)
        {
            // No generator available; at least verify spawned instances have matching tile data
            var instances = spawnedResources.ToArray();
            foreach (var inst in instances)
            {
                if (inst == null) continue;
                if (inst.planetIndex != planetIndex) continue;
                var td = ts.GetTileDataFromPlanet(inst.tileIndex, planetIndex);
                bool tileHas = td != null && td.resource != null;
                if (!tileHas || td.resource != inst.data)
                {
                    HandleTileResourceChanged(inst.tileIndex, inst.data, null, planetIndex);
                    if (ShouldLogForPlanet(planetIndex)) Debug.Log($"[ResourceManager][Reconcile] Destroyed mismatched ResourceInstance at tile {inst.tileIndex} on planet {planetIndex}");
                    fixes++;
                }
            }
        }

        if (ShouldLogForPlanet(planetIndex))
        {
            if (fixes > 0)
                Debug.Log($"[ResourceManager][Reconcile] Fixed {fixes} resource mismatches on planet {planetIndex}");
            else
                Debug.Log($"[ResourceManager][Reconcile] No mismatches found on planet {planetIndex}");
        }
    }

    private bool ShouldLogForPlanet(int planetIndex)
    {
        if (GameManager.Instance == null) return true;
        if (!GameManager.Instance.restrictDiagnosticsToFirstPlanet) return true;
        return GameManager.Instance.currentPlanetIndex == planetIndex;
    }
}
