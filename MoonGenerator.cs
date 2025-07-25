using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

public class MoonGenerator : MonoBehaviour, IHexasphereGenerator
{
    [Header("Sphere Settings")]
    public int subdivisions = 6;
    public bool randomSeed = true;
    public int seed = 98765;

    [Header("Moon Surface Settings")]
    [Tooltip("Noise frequency for the main moon surface elevation.")]
    public float moonElevationFreq = 2.5f;
    [Range(0f, 1f)]
    [Tooltip("Base elevation for Moon Dunes.")]
    public float baseDuneElevation = 0.4f;
    [Range(0f, 1f)] // User mentioned 4.5, assuming 0.45 is intended for consistency. Adjust range if 4.5 is really needed.
    [Tooltip("Maximum elevation Moon Dunes can reach with noise.")]
    public float maxDuneElevation = 0.45f;

    [Header("Moon Cave Settings")]
    [Tooltip("Noise frequency for determining cave locations.")]
    public float cavePlacementFreq = 1.2f;
    [Range(0f, 1f)]
    [Tooltip("Noise threshold (0-1). Values above this potentially become caves. Higher = rarer caves.")]
    public float caveThreshold = 0.85f;
     [Range(0f, 1f)]
    [Tooltip("Fixed elevation level for Moon Caves.")]
    public float fixedCaveElevation = 0.2f;
    [Range(1, 10)]
    [Tooltip("Minimum size of a cave cluster.")]
    public int minCaveClusterSize = 3; // Slightly smaller than requested 4 to allow smaller patches
    [Range(1, 10)]
    [Tooltip("Maximum size of a cave cluster.")]
    public int maxCaveClusterSize = 6;

    // --- Terrain Prefab Support ---
    [System.Serializable]
    public struct BiomePrefabEntry {
        public Biome biome;
        public GameObject[] flatPrefabs; // Prefabs for flat tiles
        public GameObject[] hillPrefabs; // Prefabs for hill tiles
        public GameObject[] pentagonFlatPrefabs; // Prefabs for flat pentagon tiles
        public GameObject[] pentagonHillPrefabs; // Prefabs for hill pentagon tiles
    }

    [Header("Tile Prefabs")]
    public List<BiomePrefabEntry> biomePrefabList = new();
    [Tooltip("Number of tile prefabs to spawn each frame")]
    public int tileSpawnBatchSize = 100;

    // Dictionaries for flat and hill prefabs
    private Dictionary<Biome, GameObject[]> flatBiomePrefabs = new();
    private Dictionary<Biome, GameObject[]> hillBiomePrefabs = new();

    [Range(0.5f, 1.2f)]
    [Tooltip("Multiplier to fine-tune tile prefab scaling for perfect fit (1.0 = auto, <1 = tighter, >1 = looser)")]
    public float tileRadiusMultiplier = 1.0f;

    [Header("Initialization")]
    [Tooltip("Wait this many frames before initial generation so Hexasphere has finished.")]
    public int initializationDelay = 1;

    private List<BiomeSettings> biomeSettings;
    public List<BiomeSettings> GetBiomeSettings() => biomeSettings;
    private readonly Dictionary<Biome, int> biomeToIndex = new Dictionary<Biome, int>();

    public void SetBiomeSettings(List<BiomeSettings> sharedBiomeSettings)
    {
        biomeSettings = sharedBiomeSettings;
        BuildBiomeLookup();
    }

    /// <summary>
    /// Configure the moon generator with the correct radius and build the mesh
    /// </summary>
    public void ConfigureMoon(int subdivisions, float radius)
    {
        // Generate the grid with the correct radius and subdivisions
        grid.GenerateFromSubdivision(subdivisions, radius);
        // Mesh build moved after biomes are assigned
        Debug.Log($"[MoonGenerator] Configured with subdivisions: {subdivisions}, radius: {radius}");
    }

    /// <summary>
    /// Get moon map size parameters - same as planets but scaled down to 1/5th

    private void BuildBiomeLookup()
    {
        lookup.Clear();
        biomeToIndex.Clear();

        if (biomeSettings == null)
        {
            Debug.LogError("[MoonGenerator] Biome settings list is null!");
            return;
        }

        for (int i = 0; i < biomeSettings.Count; i++)
        {
            var bs = biomeSettings[i];
            lookup[bs.biome] = bs;
            biomeToIndex[bs.biome] = i;
        }
    }


    SphericalHexGrid grid;
    public SphericalHexGrid Grid => grid;
    NoiseSampler noise; // Use the same NoiseSampler
    NoiseSampler cavePlacementNoise; // Separate noise for cave placement
    readonly Dictionary<int, HexTileData> data = new Dictionary<int, HexTileData>();
    readonly Dictionary<int, float> tileElevation = new Dictionary<int, float>(); // Store final elevation
    readonly Dictionary<Biome, BiomeSettings> lookup = new Dictionary<Biome, BiomeSettings>(); // Lookup for settings
    private LoadingPanelController loadingPanelController;
    /// <summary>
    /// List holding the final HexTileData objects for all moon tiles.
    /// Populated after surface generation completes.
    /// </summary>
    public List<HexTileData> Tiles { get; private set; } = new List<HexTileData>();
    public void SetLoadingPanel(LoadingPanelController controller)
    {
        loadingPanelController = controller;
    }

    public LoadingPanelController GetLoadingPanel() => loadingPanelController;

    // Decorations are now managed by SGT components. Old pooling logic has been
    // removed to keep this generator focused solely on terrain data.

    // --------------------------- Unity lifecycle -----------------------------
    void Awake()
    {
        // Initialize the grid for this moon (will be configured by GameManager)
        grid = new SphericalHexGrid();
        
        if (randomSeed) seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);

        noise = new NoiseSampler(seed); // For elevation
        cavePlacementNoise = new NoiseSampler(seed + 1); // Different seed for cave placement

        // No need for dynamic tile sizing - we're using fixed scale 0.3

        // Build biome lookup tables if biome settings were provided via inspector
        if (biomeSettings != null)
        {
            BuildBiomeLookup();
        }

        flatBiomePrefabs.Clear();
        hillBiomePrefabs.Clear();
        // Build dictionaries for all prefab types
        foreach (var entry in biomePrefabList)
        {
            if (entry.flatPrefabs != null && entry.flatPrefabs.Length > 0)
                flatBiomePrefabs[entry.biome] = entry.flatPrefabs;
            if (entry.hillPrefabs != null && entry.hillPrefabs.Length > 0)
                hillBiomePrefabs[entry.biome] = entry.hillPrefabs;
            // Pentagon support
            if (entry.pentagonFlatPrefabs != null && entry.pentagonFlatPrefabs.Length > 0)
                flatBiomePrefabs[(Biome)((int)entry.biome + 1000)] = entry.pentagonFlatPrefabs; // Key offset for pentagon
            if (entry.pentagonHillPrefabs != null && entry.pentagonHillPrefabs.Length > 0)
                hillBiomePrefabs[(Biome)((int)entry.biome + 1000)] = entry.pentagonHillPrefabs;
        }
    }

    void Start()
    {
        // if (!generateOnAwake && generateOnStart) // GameManager will control initialization
        // {
        // StartCoroutine(Initialise());
        // }
    }

    System.Collections.IEnumerator Initialise() // This can be called if manual generation outside GameManager is needed
    {
        yield return null;
        GenerateSurface();
    }

    // --------------------------- Surface Generation --------------------------
    /// <summary>
    /// Generates the moon's surface
    /// </summary>
    public System.Collections.IEnumerator GenerateSurface()
    {
        data.Clear();
        tileElevation.Clear();
        Tiles.Clear();
        int tileCount = grid.TileCount;

        // --- 1. Initial Dune Elevation and Biome Assignment ---
        for (int i = 0; i < tileCount; i++)
        {
            Vector3 tileCenter = grid.tileCenters[i]; // No noise offset needed for simple generation
            float rawNoise = noise.GetElevation(tileCenter * moonElevationFreq); // Noise 0-1
            float noiseScale = Mathf.Max(0f, maxDuneElevation - baseDuneElevation);
            float elevation = baseDuneElevation + (rawNoise * noiseScale);
            elevation = Mathf.Min(elevation, maxDuneElevation); // Ensure cap

            tileElevation[i] = elevation;

            // Initialize all tiles as Moon Dunes first
            var yields = BiomeHelper.Yields(Biome.MoonDunes); // Get default yields
            var td = new HexTileData {
                biome = Biome.MoonDunes,
                food = yields.food, production = yields.prod, gold = yields.gold, science = yields.sci, culture = yields.cult,
                occupantId = 0,
                isLand = true, // All moon tiles are land conceptually
                isHill = false, // No hills initially
                elevation = elevation,
                elevationTier = ElevationTier.Flat,
                isMoonTile = true // Mark as moon tile
            };
            data[i] = td;

            // BATCH YIELD
            if (i > 0 && i % 500 == 0)
            {
                if (loadingPanelController != null)
                {
                    loadingPanelController.SetProgress((float)i / tileCount * 0.2f); // Progress 0% to 20%
                    loadingPanelController.SetStatus("Sculpting moon dunes...");
                }
        yield return null;
            }
        }

        // --- 2. Generate Moon Caves ---
        yield return StartCoroutine(GenerateCaves(tileCount));


        // --- 3. Final Visual Update Pass ---
        int batchSize = 200;
        int batchCounter = 0;
        for (int i = 0; i < tileCount; i++)
        {
            if (!data.ContainsKey(i)) continue; // Should not happen

            HexTileData td = data[i];
            Biome biome = td.biome;
            float finalElevation = tileElevation[i]; // Use the stored final elevation

            // Decorations are spawned by SGT systems; old code removed.

            // Assign the final elevation back to the data struct (especially important for caves)
            td.elevation = finalElevation;
            data[i] = td;

            batchCounter++;
            if (batchCounter >= batchSize) {
                batchCounter = 0;
                if (loadingPanelController != null)
                {
                    loadingPanelController.SetProgress(0.4f + (float)i / tileCount * 0.1f); // Progress 40% to 50%
                    loadingPanelController.SetStatus("Finalizing moon terrain...");
                }
                yield return null;
            }
        }
        Debug.Log($"Generated Moon Surface with {tileCount} tiles.");


        // If you want to trigger a visual update, do it here (e.g., update textures, notify UI, etc.)
        // Example: Notify loading panel that surface generation is done
        if (loadingPanelController != null)
        {
            loadingPanelController.SetProgress(1f);
            loadingPanelController.SetStatus("Moon surface generation complete.");
        }

        // Visual textures are no longer generated; tiles rely on prefabs

        // Populate the public tile list with the final tile data
        Tiles = data.Values.ToList();

        // Debug: List biome quantities
        LogBiomeQuantities();

        // Only spawn prefabs if any flat or hill prefabs are present
        if (flatBiomePrefabs.Count > 0 || hillBiomePrefabs.Count > 0)
            StartCoroutine(SpawnAllTilePrefabs(tileSpawnBatchSize));
    }

    /// <summary>
    /// Debug method to log the quantity of each biome on the moon
    /// </summary>
    private void LogBiomeQuantities()
    {
        Dictionary<Biome, int> biomeCounts = new Dictionary<Biome, int>();
        
        // Count tiles for each biome
        for (int i = 0; i < grid.TileCount; i++)
        {
            if (data.ContainsKey(i))
            {
                Biome biome = data[i].biome;
                if (!biomeCounts.ContainsKey(biome))
                    biomeCounts[biome] = 0;
                biomeCounts[biome]++;
            }
        }
        
        // Log the results
        Debug.Log("=== MOON BIOME QUANTITY SUMMARY ===");
        Debug.Log($"Total moon tiles: {grid.TileCount}");
        Debug.Log($"Moon tiles with data: {data.Count}");
        
        foreach (var kvp in biomeCounts.OrderByDescending(x => x.Value))
        {
            float percentage = (float)kvp.Value / grid.TileCount * 100f;
            Debug.Log($"Moon Biome {kvp.Key}: {kvp.Value} tiles ({percentage:F1}%)");
        }
        
        // Check for any tiles without data
        int tilesWithoutData = grid.TileCount - data.Count;
        if (tilesWithoutData > 0)
        {
            Debug.LogWarning($"Warning: {tilesWithoutData} moon tiles have no biome data assigned!");
        }
        
        Debug.Log("=== END MOON BIOME QUANTITY SUMMARY ===");
    }

    // Helper: lat/long (deg) → unit vector
    static Vector3 SphericalToCartesian(float latDeg, float lonDeg)
    {
        float lat = latDeg * Mathf.Deg2Rad;
        float lon = lonDeg * Mathf.Deg2Rad;
        float x = Mathf.Cos(lat) * Mathf.Cos(lon);
        float y = Mathf.Sin(lat);
        float z = Mathf.Cos(lat) * Mathf.Sin(lon);
        return new Vector3(x, y, z);
    }

    // Helper: 3D direction vector -> 2D equirectangular texture coordinates
    private static Vector2 WorldToEquirectangular(Vector3 direction, int textureWidth, int textureHeight)
    {
        direction.Normalize();
        float longitude = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        float latitude = Mathf.Asin(direction.y) * Mathf.Rad2Deg;

        float u = (longitude + 180f) / 360f;
        float v = (180f - (latitude + 90f)) / 180f; // Invert V for texture space

        return new Vector2(u * textureWidth, v * textureHeight);
    }




    // --- 2.1 Cave Generation Helper ---
    System.Collections.IEnumerator GenerateCaves(int tileCount)
    {
        HashSet<int> processedForCaves = new HashSet<int>(); // Track tiles already part of a cave cluster
        List<int> clusterTiles = new List<int>(); // To hold tiles in the current potential cluster
        Queue<int> floodQueue = new Queue<int>(); // For BFS/flood fill

        for (int i = 0; i < tileCount; i++)
        {
            if (processedForCaves.Contains(i)) continue; // Skip if already processed

            Vector3 tileCenter = grid.tileCenters[i];
            float caveNoiseValue = cavePlacementNoise.GetElevation(tileCenter * cavePlacementFreq); // Use a noise function (0-1)

            if (caveNoiseValue > caveThreshold)
            {
                // Potential Cave Seed Found - Start BFS for cluster
                clusterTiles.Clear();
                floodQueue.Clear();

                floodQueue.Enqueue(i);
                processedForCaves.Add(i); // Mark seed as processed immediately
                clusterTiles.Add(i);

                while (floodQueue.Count > 0 && clusterTiles.Count < maxCaveClusterSize)
                {
                    int currentTile = floodQueue.Dequeue();

                    foreach (int neighborIndex in grid.neighbors[currentTile])
                    {
                        if (!processedForCaves.Contains(neighborIndex) && clusterTiles.Count < maxCaveClusterSize)
                        {
                            // Check if neighbor *also* meets threshold (optional, makes caves sparser)
                             Vector3 neighborCenter = grid.tileCenters[neighborIndex];
                             float neighborCaveNoise = cavePlacementNoise.GetElevation(neighborCenter * cavePlacementFreq);
                             if (neighborCaveNoise > caveThreshold) // Only cluster with other potential caves
                             {
                                processedForCaves.Add(neighborIndex);
                                floodQueue.Enqueue(neighborIndex);
                                clusterTiles.Add(neighborIndex);
                             }
                        }
                    }
                }

                // Check if cluster size meets minimum requirement
                if (clusterTiles.Count >= minCaveClusterSize)
                {
                    // Valid cluster - convert tiles to caves
                    foreach (int caveTileIndex in clusterTiles)
                    {
                        if (data.ContainsKey(caveTileIndex)) // Safety check
                        {
                            var td = data[caveTileIndex];
                            td.biome = Biome.MoonCaves;
                            data[caveTileIndex] = td;
                            tileElevation[caveTileIndex] = fixedCaveElevation; // Set fixed elevation
                        }
                    }
                    // Debug.Log($"Created cave cluster of size {clusterTiles.Count} starting near tile {i}");
                } else {
                    // Cluster too small, revert processed status for potential inclusion in other clusters
                    // Note: This part makes it less likely small invalid clusters prevent valid ones nearby.
                    // However, could potentially re-process tiles unnecessarily.
                    // Consider removing this revert if performance is critical and small failed clusters are acceptable.
                     foreach(int revertedTile in clusterTiles) {
                         processedForCaves.Remove(revertedTile);
                     }
                }
            }
             // Ensure even tiles that didn't start a cluster are marked processed if checked
             processedForCaves.Add(i);

            // BATCH YIELD
            if (i > 0 && i % 500 == 0)
            {
                if (loadingPanelController != null)
                {
                    loadingPanelController.SetProgress(0.2f + (float)i / tileCount * 0.2f); // Progress 20% to 40%
                    loadingPanelController.SetStatus("Carving moon caves...");
                }
        yield return null;
            }
        }
    }

    // --------------------------- API for other systems -----------------------

    /// <summary>
    /// Gets hex tile data for the specified tile index.
    /// </summary>
    public HexTileData GetHexTileData(int tileIndex)
    {
        data.TryGetValue(tileIndex, out HexTileData td);
        return td; // null if not found
    }

    /// <summary>
    /// Sets or updates hex tile data for the specified tile index.
    /// </summary>
    public void SetHexTileData(int tileIndex, HexTileData td)
    {
        data[tileIndex] = td;
    }

    /// <summary>
    /// Checks if a tile is part of the moon.
    /// </summary>
    public bool IsTileMoon(int tileIndex)
    {
        if (data.TryGetValue(tileIndex, out HexTileData td))
        {
            return td.isMoonTile;
        }
        return false;
    }

    /// <summary>
    /// Gets the elevation value for a tile.
    /// </summary>
    public float GetTileElevation(int tileIndex)
    {
        if (tileElevation.TryGetValue(tileIndex, out float elev))
        {
            return elev;
        }
        return 0f;
    }

    /// <summary>
    /// Sets an occupant for a tile.
    /// </summary>
    public void SetTileOccupant(int tileIndex, GameObject occupant)
    {
        if (!data.ContainsKey(tileIndex)) return;
        
        HexTileData td = data[tileIndex];
        td.occupantId = occupant ? occupant.GetInstanceID() : 0;
        data[tileIndex] = td;
    }

    /// <summary>
    /// Gets the GameObject that is currently occupying a tile.
    /// </summary>
    public GameObject GetTileOccupant(int tileIndex)
    {
        if (!data.TryGetValue(tileIndex, out HexTileData td) || td.occupantId == 0)
            return null;
            
        // Find the GameObject with this instance ID
        var objects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        foreach (var obj in objects)
        {
            if (obj.GetInstanceID() == td.occupantId)
                return obj;
        }
        
        return null;
    }

    /// <summary>
    /// Changes a tile's biome and updates its yields and visuals.
    /// </summary>
    public void SetTileBiome(int tileIndex, Biome newBiome)
    {
        if (!data.ContainsKey(tileIndex)) return;
        
        HexTileData td = data[tileIndex];
        td.biome = newBiome;
        
        // Update yields based on new biome
        var yields = BiomeHelper.Yields(newBiome);
        td.food = yields.food;
        td.production = yields.prod;
        td.gold = yields.gold;
        td.science = yields.sci;
        td.culture = yields.cult;
        
        data[tileIndex] = td;
        
        // Update visuals - moon will use SGT landscape system like planet
        // Color updates would be handled by regenerating moon surface maps if needed
    }
    
    /// <summary>
    /// Sets the owner of a tile.
    /// </summary>
    public void SetTileOwner(int tileIndex, Civilization owner)
    {
        if (!data.ContainsKey(tileIndex)) return;
        
        HexTileData td = data[tileIndex];
        td.owner = owner;
        data[tileIndex] = td;
    }
    
    /// <summary>
    /// Sets a tile's controlling city.
    /// </summary>
    public void SetTileCity(int tileIndex, City city)
    {
        if (!data.ContainsKey(tileIndex)) return;
        
        HexTileData td = data[tileIndex];
        td.controllingCity = city;
        data[tileIndex] = td;
    }
    
    /// <summary>
    /// Gets all tiles within a certain range of a center tile.
    /// </summary>
    public List<int> GetTilesInRange(int centerTileIndex, int range)
    {
        var result = new List<int>();
        if (!data.ContainsKey(centerTileIndex)) return result;
        
        // Use a queue for breadth-first search
        var queue = new Queue<(int tileIndex, int distance)>();
        var visited = new HashSet<int>();
        
        queue.Enqueue((centerTileIndex, 0));
        visited.Add(centerTileIndex);
        
        while (queue.Count > 0)
        {
            var (tileIndex, distance) = queue.Dequeue();
            result.Add(tileIndex);
            
            if (distance < range)
            {
                foreach (int neighborIndex in grid.neighbors[tileIndex])
                {
                    if (!visited.Contains(neighborIndex) && data.ContainsKey(neighborIndex))
                    {
                        visited.Add(neighborIndex);
                        queue.Enqueue((neighborIndex, distance + 1));
                    }
                }
            }
        }
        
        return result;
    }


    // ------------------------------------------------------------------
    //  Prefab Helpers
    // ------------------------------------------------------------------
    private GameObject GetPrefabForTile(int tileIndex, HexTileData tile)
    {
        // Determine if tile is a pentagon
        bool isPentagon = grid.neighbors != null && grid.neighbors[tileIndex].Count == 5;
        BiomePrefabEntry entry = biomePrefabList.Find(e => e.biome == tile.biome);
        if (entry.biome == Biome.Any) entry = biomePrefabList.Find(e => e.biome == Biome.Any);

        // Hill logic
        if (tile.isHill) {
            if (isPentagon && entry.pentagonHillPrefabs != null && entry.pentagonHillPrefabs.Length > 0)
                return entry.pentagonHillPrefabs[UnityEngine.Random.Range(0, entry.pentagonHillPrefabs.Length)];
            if (entry.hillPrefabs != null && entry.hillPrefabs.Length > 0)
                return entry.hillPrefabs[UnityEngine.Random.Range(0, entry.hillPrefabs.Length)];
        }
        // Flat logic
        if (isPentagon && entry.pentagonFlatPrefabs != null && entry.pentagonFlatPrefabs.Length > 0)
            return entry.pentagonFlatPrefabs[UnityEngine.Random.Range(0, entry.pentagonFlatPrefabs.Length)];
        if (entry.flatPrefabs != null && entry.flatPrefabs.Length > 0)
            return entry.flatPrefabs[UnityEngine.Random.Range(0, entry.flatPrefabs.Length)];

        // Fallback: Any biome
        BiomePrefabEntry anyEntry = biomePrefabList.Find(e => e.biome == Biome.Any);
        if (anyEntry.biome != Biome.Any) return null;
        
        if (tile.isHill) {
            if (isPentagon && anyEntry.pentagonHillPrefabs != null && anyEntry.pentagonHillPrefabs.Length > 0)
                return anyEntry.pentagonHillPrefabs[UnityEngine.Random.Range(0, anyEntry.pentagonHillPrefabs.Length)];
            if (anyEntry.hillPrefabs != null && anyEntry.hillPrefabs.Length > 0)
                return anyEntry.hillPrefabs[UnityEngine.Random.Range(0, anyEntry.hillPrefabs.Length)];
        }
        
        if (isPentagon && anyEntry.pentagonFlatPrefabs != null && anyEntry.pentagonFlatPrefabs.Length > 0)
            return anyEntry.pentagonFlatPrefabs[UnityEngine.Random.Range(0, anyEntry.pentagonFlatPrefabs.Length)];
        if (anyEntry.flatPrefabs != null && anyEntry.flatPrefabs.Length > 0)
            return anyEntry.flatPrefabs[UnityEngine.Random.Range(0, anyEntry.flatPrefabs.Length)];

        Debug.LogWarning($"No prefab found for biome={tile.biome}. Assign a prefab for this biome (including water biomes) in BiomePrefabEntry.");
        return null;
    }

    // Single tile instantiation method that handles all cases
    // Using hard-coded scale (0.3) for all tiles
    
    private GameObject InstantiateTilePrefab(HexTileData tileData, Vector3 position, Quaternion rotation, Transform parent)
    {
        // Get the appropriate prefab
        int tileIndex = Tiles.IndexOf(tileData);
        GameObject prefab = GetPrefabForTile(tileIndex, tileData);
        if (prefab == null) return null;

        // Determine if tile is a pentagon
        bool isPentagon = grid.neighbors != null && grid.neighbors[tileIndex].Count == 5;
        
        // Instantiate with correct position, rotation and parent
        GameObject go = Instantiate(prefab, position, rotation, parent);

        // Different scale factors for hexagons and pentagons
        const float hexagonScale = 0.042f;
        const float pentagonScale = 0.157f; // Special scale for pentagons
        float hardCodedScale = isPentagon ? pentagonScale : hexagonScale;
        
        go.transform.localScale = new Vector3(hardCodedScale, hardCodedScale, hardCodedScale);

        return go;
    }


    // All tile sizing is now hard-coded to 0.3 so we don't need these methods anymore
    // No need for dynamic tile sizing calculations

    private System.Collections.IEnumerator SpawnAllTilePrefabs(int batchSize = 100)
    {
        GameObject parent = new GameObject("MoonTilePrefabs");
        parent.transform.SetParent(this.transform, false);

        int tileCount = grid.TileCount;
        for (int i = 0; i < tileCount; i++)
        {
            // Draw outline with LineRenderer using world-space corners
            int[] cornerIndices = grid.GetCornersOfTile(i);
            var localCorners = grid.CornerVertices;
            Vector3[] worldCorners = new Vector3[cornerIndices.Length];
            for (int j = 0; j < cornerIndices.Length; j++)
                worldCorners[j] = transform.TransformPoint(localCorners[cornerIndices[j]]);

            // Calculate center and orientation ONCE for both line mesh and prefab
            Vector3 worldCenter = Vector3.zero;
            foreach (var wc in worldCorners)
                worldCenter += wc;
            worldCenter /= worldCorners.Length;

            // Calculate hex normal (up) using the first three corners
            Vector3 edge1 = worldCorners[1] - worldCorners[0];
            Vector3 edge2 = worldCorners[2] - worldCorners[0];
            Vector3 hexNormal = Vector3.Cross(edge1, edge2).normalized;

            // Forward: direction from center to first corner
            Vector3 forward = (worldCorners[0] - worldCenter).normalized;
            // If forward is degenerate, use next edge
            if (forward == Vector3.zero && worldCorners.Length > 1)
                forward = (worldCorners[1] - worldCenter).normalized;

            Quaternion rotation = Quaternion.LookRotation(forward, hexNormal);

            // LineRenderer uses the calculated rotation for orientation
            var lrObj = new GameObject($"HexOutline_{i}");
            var lr = lrObj.AddComponent<LineRenderer>();
            lr.positionCount = worldCorners.Length;
            lr.useWorldSpace = true;
            lr.loop = true;
            lr.widthMultiplier = 0.02f;
            lr.SetPositions(worldCorners);
            lr.transform.SetParent(parent.transform, false);
            lr.transform.position = worldCenter;
            lr.transform.rotation = rotation;

            // Instantiate tile prefab at center with EXACT same rotation as line mesh
            if (!data.TryGetValue(i, out var td))
                continue;

            GameObject tileGO = InstantiateTilePrefab(td, worldCenter, rotation, parent.transform);
            if (tileGO != null)
            {
                var indexHolder = tileGO.GetComponent<TileIndexHolder>();
                if (indexHolder == null)
                    indexHolder = tileGO.AddComponent<TileIndexHolder>();
                indexHolder.tileIndex = i;
            }

            if (batchSize > 0 && i % batchSize == 0)
                yield return null;
        }
    }
}
