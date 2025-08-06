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
    [Tooltip("Number of tile decorations to spawn each frame (smaller batches for performance)")]
    public int decorationSpawnBatchSize = 10;

    // Dictionaries for flat and hill prefabs
    private Dictionary<Biome, GameObject[]> flatBiomePrefabs = new();
    private Dictionary<Biome, GameObject[]> hillBiomePrefabs = new();

    [Range(0.5f, 1.2f)]
    [Tooltip("Multiplier to fine-tune tile prefab scaling for perfect fit (1.0 = auto, <1 = tighter, >1 = looser)")]
    public float tileRadiusMultiplier = 1.0f;

    [Range(0.0f, 2.0f)]
    [Tooltip("How strongly to blend mesh vertices toward tile corners for better alignment (0 = no blending, 1 = full blending, >1 = stronger pull)")]
    public float meshVertexBlendFactor = 1.0f;

    [Header("Decoration System")]
    [Tooltip("Modern decoration system for spawning biome-specific decorations")]
    public BiomeDecorationManager decorationManager = new BiomeDecorationManager();

    [Header("Performance Settings")]
    [Tooltip("Enable expensive post-processing (normalization, mesh deformation) - disable for better performance")]
    public bool enableExpensivePostProcessing = false;
    [Tooltip("Enable mesh vertex deformation for gap filling (can be slow on large maps)")]
    public bool enableMeshDeformation = false;
    [Tooltip("Normalize tile distances for uniform spacing (slower but better visuals)")]
    public bool enableTileNormalization = false;

    [Header("Initialization")]
    [Tooltip("Wait this many frames before initial generation so Hexasphere has finished.")]
    public int initializationDelay = 1;

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


    SphericalHexGrid grid;
    public SphericalHexGrid Grid => grid;
    NoiseSampler noise; // Use the same NoiseSampler
    NoiseSampler cavePlacementNoise; // Separate noise for cave placement
    readonly Dictionary<int, HexTileData> data = new Dictionary<int, HexTileData>();
    readonly Dictionary<int, float> tileElevation = new Dictionary<int, float>(); // Store final elevation

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
        {
            // Safety check to prevent MissingReferenceException
            if (this == null || gameObject == null)
            {
                Debug.LogError($"[MoonGenerator] GameObject became invalid during generation. Stopping prefab spawning.");
                yield break;
            }
            
            // Can't use yield inside try-catch, so check validity and call directly
            bool spawnSuccessful = true;
            if (this != null && gameObject != null)
            {
                yield return StartCoroutine(SpawnAllTilePrefabs(tileSpawnBatchSize));
                
                // Check again before spawning decorations
                if (this != null && gameObject != null && spawnSuccessful)
                {
                    // Spawn decorations in batches after all tile prefabs are created
                    if (decorationManager != null && decorationManager.enableDecorations)
                        yield return StartCoroutine(SpawnAllTileDecorations(decorationSpawnBatchSize));
                }
                else
                {
                    Debug.LogError($"[MoonGenerator] GameObject became invalid during tile spawning. Stopping decoration spawning.");
                }
            }
            else
            {
                Debug.LogError($"[MoonGenerator] GameObject is invalid, cannot spawn prefabs.");
            }
        }

        // Normalize tile distances for uniform spacing (after everything else is done)
        if (enableExpensivePostProcessing && enableTileNormalization)
        {
            // Safety check before starting normalization
            if (this != null && gameObject != null)
            {
                StartCoroutine(NormalizeTileDistances());
            }
            else
            {
                Debug.LogWarning($"[MoonGenerator] Cannot start tile normalization - GameObject is invalid");
            }
        }
        else
        {
            Debug.Log("Skipping moon tile normalization for better performance");
        }
    }

    /// <summary>
    /// Normalizes distances between neighboring tile centers using Lloyd's relaxation algorithm.
    /// This creates uniform spacing between tiles with minimal sphere deformation.
    /// </summary>
    private System.Collections.IEnumerator NormalizeTileDistances()
    {
        if (loadingPanelController != null)
        {
            loadingPanelController.SetProgress(0.95f);
            loadingPanelController.SetStatus("Normalizing moon tile spacing...");
        }
        
        const int maxIterations = 5; // Number of relaxation iterations
        const float relaxationFactor = 0.1f; // How much to move each iteration (0.1 = 10%)
        const int batchSize = 200; // Process tiles in batches
        
        // Calculate target distance (average of all neighbor distances) with batching
        float totalDistance = 0f;
        int distanceCount = 0;
        
        for (int i = 0; i < grid.TileCount; i++)
        {
            Vector3 tileCenter = grid.tileCenters[i];
            foreach (int neighborIndex in grid.neighbors[i])
            {
                float distance = Vector3.Distance(tileCenter, grid.tileCenters[neighborIndex]);
                totalDistance += distance;
                distanceCount++;
            }
            
            // Yield periodically during distance calculation
            if (i % batchSize == 0)
                yield return null;
        }
        
        float targetDistance = totalDistance / distanceCount;
        Debug.Log($"Target moon tile distance: {targetDistance:F4}");
        
        // Perform relaxation iterations with batching
        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            Vector3[] newPositions = new Vector3[grid.TileCount];
            
            // Copy current positions
            for (int i = 0; i < grid.TileCount; i++)
            {
                newPositions[i] = grid.tileCenters[i];
            }
            
            // Calculate new positions based on target distances in batches
            for (int i = 0; i < grid.TileCount; i++)
            {
                Vector3 currentPos = grid.tileCenters[i];
                Vector3 adjustment = Vector3.zero;
                int neighborCount = 0;
                
                foreach (int neighborIndex in grid.neighbors[i])
                {
                    Vector3 neighborPos = grid.tileCenters[neighborIndex];
                    Vector3 direction = (neighborPos - currentPos).normalized;
                    float currentDistance = Vector3.Distance(currentPos, neighborPos);
                    
                    if (currentDistance > 0.001f) // Avoid division by zero
                    {
                        float distanceError = targetDistance - currentDistance;
                        adjustment += direction * distanceError * 0.5f; // Move half the error distance
                        neighborCount++;
                    }
                }
                
                if (neighborCount > 0)
                {
                    adjustment /= neighborCount; // Average adjustment
                    newPositions[i] = currentPos + adjustment * relaxationFactor;
                    
                    // Project back onto sphere to maintain spherical shape (with slight deformation allowed)
                    float originalRadius = currentPos.magnitude;
                    newPositions[i] = newPositions[i].normalized * originalRadius;
                }
                
                // Yield every batch to prevent frame drops
                if (i % batchSize == 0)
                {
                    if (loadingPanelController != null)
                    {
                        float iterationProgress = (float)iteration / maxIterations;
                        float batchProgress = (float)i / grid.TileCount;
                        float totalProgress = 0.95f + (iterationProgress + batchProgress / maxIterations) * 0.05f;
                        loadingPanelController.SetProgress(totalProgress);
                        loadingPanelController.SetStatus($"Normalizing moon spacing... ({iteration + 1}/{maxIterations})");
                    }
                    yield return null;
                }
            }
            
            // Apply new positions
            for (int i = 0; i < grid.TileCount; i++)
            {
                grid.tileCenters[i] = newPositions[i];
            }
        }
        
        Debug.Log("Moon tile distance normalization complete.");
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

    // Single tile instantiation method that handles all cases with mesh deformation
    
    private GameObject InstantiateTilePrefab(HexTileData tileData, int tileIndex, Vector3 position, Transform parent, Quaternion? targetRotation = null)
    {
        // Get the appropriate prefab
        GameObject prefab = GetPrefabForTile(tileIndex, tileData);
        if (prefab == null) return null;

        // Determine if tile is a pentagon
        bool isPentagon = grid.neighbors != null && tileIndex >= 0 && tileIndex < grid.neighbors.Length && grid.neighbors[tileIndex].Count == 5;
        
        // Instantiate with correct position
        GameObject go = Instantiate(prefab, position, Quaternion.identity, parent);

        // Apply rotation - use the provided target rotation if available, otherwise default to radial orientation
        if (targetRotation.HasValue)
        {
            // Use the exact same rotation as the LineRenderer for perfect alignment
            go.transform.rotation = targetRotation.Value;
        }
        else
        {
            // Fallback orientation. Orient so the tile's DOWN points away from the moon's center (up points toward center)
            Vector3 radial = (position - transform.position).normalized;
            go.transform.up = radial; // C
            Debug.LogWarning($"No target rotation provided for tile {tileIndex}. Using radial orientation.");
        }

        // Different scale factors for hexagons and pentagons
        const float hexagonScale = 0.047f;
        const float pentagonScale = 0.047f; // Fixed pentagon scale (matching PlanetGenerator)
        float baseScale = isPentagon ? pentagonScale : hexagonScale;
        
        // Apply tileRadiusMultiplier (matching PlanetGenerator)
        float finalScale = baseScale * tileRadiusMultiplier;
        
        go.transform.localScale = new Vector3(finalScale, finalScale, finalScale);

        // Apply mesh deformation to fill gaps only if enabled
        if (enableExpensivePostProcessing && enableMeshDeformation)
        {
            StartCoroutine(DeformTileMeshToFillGaps(go, tileIndex, isPentagon));
        }

        // Note: Decorations are now spawned in a separate batched process for performance

        return go;
    }

    /// <summary>
    /// Deforms the tile's mesh vertices to stretch toward gaps and create seamless coverage
    /// </summary>
    private System.Collections.IEnumerator DeformTileMeshToFillGaps(GameObject tileObject, int tileIndex, bool isPentagon)
    {
        // Wait a frame to ensure all tiles are instantiated first
        yield return new WaitForEndOfFrame();

        // Look for MeshFilter in the tile object or its children
        MeshFilter meshFilter = tileObject.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            meshFilter = tileObject.GetComponentInChildren<MeshFilter>();
        }
        
        if (meshFilter == null || meshFilter.mesh == null)
        {
            Debug.LogWarning($"Moon tile {tileIndex} has no MeshFilter or mesh for deformation (checked parent and children)");
            yield break;
        }

        // Get the current mesh and make it editable
        Mesh originalMesh = meshFilter.mesh;
        Mesh deformedMesh = Instantiate(originalMesh);
        Vector3[] vertices = deformedMesh.vertices;
        
        // Calculate gap information for this tile
        var gapInfo = AnalyzeTileGaps(tileIndex);
        
        if (gapInfo.hasSignificantGaps)
        {
            // Apply vertex deformation based on gap analysis
            DeformVerticesForGaps(vertices, gapInfo, tileIndex, isPentagon);
            
            // Update the mesh
            deformedMesh.vertices = vertices;
            deformedMesh.RecalculateNormals();
            deformedMesh.RecalculateBounds();
            
            // Apply the deformed mesh
            meshFilter.mesh = deformedMesh;
            
            Debug.Log($"Deformed moon tile {tileIndex} mesh to fill {gapInfo.gapDirections.Count} gaps");
        }
    }

    /// <summary>
    /// Data structure for gap analysis results
    /// </summary>
    private struct TileGapInfo
    {
        public bool hasSignificantGaps;
        public List<Vector3> gapDirections;     // Directions toward gaps (world space)
        public List<float> gapMagnitudes;       // How severe each gap is (0-1)
        public float averageGapSeverity;        // Overall gap severity
        public Vector3 primaryGapDirection;     // Most significant gap direction
    }

    /// <summary>
    /// Analyzes gaps around a tile by comparing expected vs actual neighbor distances
    /// </summary>
    private TileGapInfo AnalyzeTileGaps(int tileIndex)
    {
        var gapInfo = new TileGapInfo
        {
            gapDirections = new List<Vector3>(),
            gapMagnitudes = new List<float>()
        };

        if (grid.neighbors == null || tileIndex < 0 || tileIndex >= grid.neighbors.Length)
        {
            return gapInfo;
        }

        Vector3 tileCenter = grid.tileCenters[tileIndex];
        var neighbors = grid.neighbors[tileIndex];
        
        // Calculate expected distance (global average)
        float expectedDistance = CalculateExpectedTileDistance();
        
        const float gapThreshold = 1.15f; // 15% larger than expected = gap
        float totalGapSeverity = 0f;
        Vector3 weightedGapDirection = Vector3.zero;

        foreach (int neighborIndex in neighbors)
        {
            if (neighborIndex >= 0 && neighborIndex < grid.tileCenters.Length)
            {
                Vector3 neighborCenter = grid.tileCenters[neighborIndex];
                Vector3 directionToNeighbor = (neighborCenter - tileCenter).normalized;
                float actualDistance = Vector3.Distance(tileCenter, neighborCenter);
                float gapRatio = actualDistance / expectedDistance;

                if (gapRatio > gapThreshold)
                {
                    float gapSeverity = (gapRatio - 1.0f); // How much bigger than expected
                    gapInfo.gapDirections.Add(directionToNeighbor);
                    gapInfo.gapMagnitudes.Add(gapSeverity);
                    
                    totalGapSeverity += gapSeverity;
                    weightedGapDirection += directionToNeighbor * gapSeverity;
                }
            }
        }

        gapInfo.hasSignificantGaps = gapInfo.gapDirections.Count > 0;
        gapInfo.averageGapSeverity = gapInfo.gapDirections.Count > 0 ? totalGapSeverity / gapInfo.gapDirections.Count : 0f;
        gapInfo.primaryGapDirection = weightedGapDirection.normalized;

        return gapInfo;
    }

    /// <summary>
    /// Deforms mesh vertices to stretch toward identified gaps
    /// </summary>
    private void DeformVerticesForGaps(Vector3[] vertices, TileGapInfo gapInfo, int tileIndex, bool isPentagon)
    {
        if (!gapInfo.hasSignificantGaps) return;

        // Convert tile center to local space of the mesh
        Vector3 tileCenter = Vector3.zero; // Mesh is centered at origin
        
        // Maximum stretch amount (as a fraction of original vertex distance from center)
        const float maxStretchFactor = 0.4f; // 40% maximum stretch
        
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 originalVertex = vertices[i];
            Vector3 vertexDirection = originalVertex.normalized;
            float distanceFromCenter = originalVertex.magnitude;
            
            // Calculate how much to stretch this vertex based on nearby gaps
            float totalStretch = 0f;
            
            for (int gapIndex = 0; gapIndex < gapInfo.gapDirections.Count; gapIndex++)
            {
                Vector3 gapDirection = gapInfo.gapDirections[gapIndex];
                float gapMagnitude = gapInfo.gapMagnitudes[gapIndex];
                
                // Calculate alignment between vertex direction and gap direction
                float alignment = Vector3.Dot(vertexDirection, gapDirection);
                
                // Only stretch vertices that are reasonably aligned with the gap direction
                if (alignment > 0.3f) // 30-degree cone around gap direction
                {
                    // Stronger stretch for better alignment and more severe gaps
                    float stretchAmount = alignment * gapMagnitude * maxStretchFactor;
                    totalStretch += stretchAmount;
                }
            }
            
            // Clamp total stretch to prevent extreme deformation
            totalStretch = Mathf.Clamp(totalStretch, 0f, maxStretchFactor);
            
            // Apply the stretch with blend factor control
            if (totalStretch > 0.01f) // Only apply significant stretches
            {
                Vector3 stretchedVertex = originalVertex * (1f + totalStretch);
                // Use meshVertexBlendFactor to control the strength of the deformation
                vertices[i] = Vector3.Lerp(originalVertex, stretchedVertex, meshVertexBlendFactor);
            }
        }
    }

    /// <summary>
    /// Calculate the expected distance between neighboring tiles (cached)
    /// </summary>
    private float CalculateExpectedTileDistance()
    {
        // Cache the result to avoid recalculating
        if (_cachedExpectedDistance > 0f) return _cachedExpectedDistance;

        float totalDistance = 0f;
        int distanceCount = 0;

        for (int i = 0; i < grid.TileCount; i++)
        {
            if (grid.neighbors != null && i < grid.neighbors.Length && grid.neighbors[i] != null)
            {
                Vector3 tileCenter = grid.tileCenters[i];
                foreach (int neighborIndex in grid.neighbors[i])
                {
                    if (neighborIndex >= 0 && neighborIndex < grid.tileCenters.Length)
                    {
                        float distance = Vector3.Distance(tileCenter, grid.tileCenters[neighborIndex]);
                        totalDistance += distance;
                        distanceCount++;
                    }
                }
            }
        }

        _cachedExpectedDistance = distanceCount > 0 ? totalDistance / distanceCount : 1.0f;
        Debug.Log($"Calculated expected moon tile distance: {_cachedExpectedDistance:F4}");
        return _cachedExpectedDistance;
    }

    private float _cachedExpectedDistance = 0f;


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

            Quaternion rotation = Quaternion.LookRotation(forward, -hexNormal); // Use -hexNormal like PlanetGenerator

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

            // Instantiate tile prefab at center using the same rotation as LineRenderer
            if (!data.TryGetValue(i, out var td))
                continue;

            GameObject tileGO = InstantiateTilePrefab(td, i, worldCenter, parent.transform, rotation);
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

    /// <summary>
    /// Spawns decorations on all tiles in batches for performance
    /// </summary>
    private System.Collections.IEnumerator SpawnAllTileDecorations(int batchSize = 25)
    {
        if (decorationManager == null || !decorationManager.enableDecorations)
        {
            Debug.LogWarning("DecorationManager is null or disabled, skipping decoration spawning");
            yield break;
        }

        decorationManager.Initialize();
        
        if (loadingPanelController != null)
        {
            loadingPanelController.SetStatus("Spawning moon tile decorations...");
            loadingPanelController.SetProgress(0.9f);
        }

        // Find the tile prefabs parent
        Transform tilePrefabsParent = transform.Find("MoonTilePrefabs");
        if (tilePrefabsParent == null)
        {
            Debug.LogWarning("Could not find MoonTilePrefabs parent object for decoration spawning");
            yield break;
        }

        // Pre-cache tile transforms by index for fast lookup
        var tileTransformCache = new Dictionary<int, Transform>();
        int childCount = tilePrefabsParent.childCount;
        
        for (int i = 0; i < childCount; i++)
        {
            Transform child = tilePrefabsParent.GetChild(i);
            var indexHolder = child.GetComponent<TileIndexHolder>();
            if (indexHolder != null)
            {
                tileTransformCache[indexHolder.tileIndex] = child;
            }
            
            // Yield periodically during caching
            if (i % 100 == 0)
                yield return null;
        }

        // Pre-filter tiles that need decorations using the cache
        var tilesNeedingDecorations = new List<(int index, HexTileData data, Transform transform)>();
        
        for (int i = 0; i < grid.TileCount; i++)
        {
            if (!data.TryGetValue(i, out HexTileData tileData)) continue;
            
            if (decorationManager.ShouldSpawnDecorations(tileData.biome))
            {
                if (tileTransformCache.TryGetValue(i, out Transform tileTransform))
                {
                    tilesNeedingDecorations.Add((i, tileData, tileTransform));
                }
            }
            
            // Yield during pre-filtering
            if (i % 250 == 0)
                yield return null;
        }

        // Now spawn decorations only on tiles that need them
        for (int i = 0; i < tilesNeedingDecorations.Count; i++)
        {
            var (tileIndex, tileData, tileTransform) = tilesNeedingDecorations[i];
            SpawnTileDecorations(tileTransform.gameObject, tileData, tileIndex, tileTransform.position);
            
            if (i % batchSize == 0 || i % 5 == 0) // Yield every 5 decorations for smoother loading
            {
                if (loadingPanelController != null)
                {
                    float progress = 0.9f + (0.05f * (float)i / tilesNeedingDecorations.Count);
                    loadingPanelController.SetProgress(progress);
                    loadingPanelController.SetStatus($"Adding moon decorations... ({i}/{tilesNeedingDecorations.Count})");
                }
                yield return null;
            }
        }

        if (loadingPanelController != null)
        {
            loadingPanelController.SetProgress(0.95f);
            loadingPanelController.SetStatus("Moon decoration spawning complete.");
        }

        Debug.Log($"Spawned decorations for {tilesNeedingDecorations.Count} moon tiles");
    }

    /// <summary>
    /// Spawns decorations on a moon tile based on the biome decoration settings
    /// </summary>
    private void SpawnTileDecorations(GameObject tileObject, HexTileData tileData, int tileIndex, Vector3 tilePosition)
    {
        if (decorationManager == null)
            return;

        decorationManager.Initialize();
        var decorationEntry = decorationManager.GetDecorationSettings(tileData.biome);
        if (decorationEntry.decorationPrefabs == null || decorationEntry.decorationPrefabs.Length == 0)
            return;

        if (UnityEngine.Random.value > decorationEntry.spawnChance)
            return;

        float tileRadius = CalculateTileRadius();
        if (tileRadius < 0.1f)
            return;

        int decorationCount = UnityEngine.Random.Range(decorationEntry.minDecorations, decorationEntry.maxDecorations + 1);

        GameObject decorationParent = new GameObject($"MoonDecorations_Tile_{tileIndex}");
        decorationParent.transform.SetParent(tileObject.transform, false);
        decorationParent.transform.localPosition = Vector3.zero;
        decorationParent.transform.localRotation = Quaternion.identity;

        for (int i = 0; i < decorationCount; i++)
        {
            // Select a random decoration from the biome
            GameObject decorationPrefab = decorationEntry.decorationPrefabs[UnityEngine.Random.Range(0, decorationEntry.decorationPrefabs.Length)];
            if (decorationPrefab == null) continue;

            // Generate a random position within the tile bounds
            Vector3 decorationPosition = GenerateRandomDecorationPosition(tilePosition, tileRadius, decorationEntry);
            
            // Calculate the "up" direction for this position on the moon
            Vector3 upDirection = (decorationPosition - transform.position).normalized;
            
            // Create rotation to orient the decoration away from moon center
            Quaternion decorationRotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(UnityEngine.Random.insideUnitSphere, upDirection).normalized, upDirection);
            
            // Instantiate the decoration
            GameObject decoration = Instantiate(decorationPrefab, decorationPosition, decorationRotation, decorationParent.transform);
            
            // Apply decoration scale with variation

            // Apply decoration scaling with variation
            float finalScale = decorationEntry.scaleMultiplier * decorationManager.globalScaleMultiplier;
            if (decorationEntry.scaleVariation > 0f)
            {
                float variation = UnityEngine.Random.Range(-decorationEntry.scaleVariation, decorationEntry.scaleVariation);
                finalScale *= (1f + variation);
            }
            decoration.transform.localScale = Vector3.one * finalScale;
            
            // Add a small random rotation around the up axis for variety
            decoration.transform.Rotate(upDirection, UnityEngine.Random.Range(0f, 360f), Space.World);
        }
    }

    /// <summary>
    /// Generates a random position for decoration placement within moon tile bounds
    /// </summary>
    private Vector3 GenerateRandomDecorationPosition(Vector3 tileCenter, float tileRadius, BiomeDecorationEntry decorationEntry)
    {
        // Generate a random direction from tile center
        Vector2 randomCircle = UnityEngine.Random.insideUnitCircle;
        
        // Scale to be within the decoration placement range
        float distance = UnityEngine.Random.Range(
            decorationEntry.minDistanceFromCenter * tileRadius,
            decorationEntry.maxDistanceFromCenter * tileRadius
        );
        
        // Calculate the moon's surface normal at tile center
        Vector3 surfaceNormal = (tileCenter - transform.position).normalized;
        
        // Create a random direction on the sphere surface around the tile
        Vector3 randomDirection = Vector3.Cross(surfaceNormal, Vector3.up);
        if (randomDirection.magnitude < 0.1f) // Handle case where surface normal is parallel to Vector3.up
            randomDirection = Vector3.Cross(surfaceNormal, Vector3.forward);
        randomDirection = randomDirection.normalized;
        
        Vector3 tangent = Vector3.Cross(surfaceNormal, randomDirection).normalized;
        
        // Combine the tangent vectors to get a random direction on the sphere surface
        Vector3 localOffset = (randomDirection * randomCircle.x + tangent * randomCircle.y) * distance;
        
        // Calculate final position and project back to sphere surface
        Vector3 decorationPos = tileCenter + localOffset;
        float moonRadius = tileCenter.magnitude; // Distance from moon center
        decorationPos = decorationPos.normalized * moonRadius;
        
        return decorationPos;
    }

    /// <summary>
    /// Calculates the approximate radius of a moon tile for decoration placement
    /// </summary>
    private float CalculateTileRadius()
    {
        // Use the expected tile distance calculation
        float expectedDistance = CalculateExpectedTileDistance();
        return expectedDistance * 0.5f; // Radius is roughly half the distance to neighbors
    }
}
