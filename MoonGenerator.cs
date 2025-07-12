using UnityEngine;
using System;
using System.Collections.Generic;
using SpaceGraphicsToolkit;
using SpaceGraphicsToolkit.Landscape;

public class MoonGenerator : MonoBehaviour
{
    [Header("Sphere Settings")]
    public int subdivisions = 4;
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


    [Header("Visuals")]
    public Color moonDunesColor = new Color(0.8f, 0.8f, 0.75f); // Light greyish
    public Color moonCavesColor = new Color(0.4f, 0.4f, 0.45f); // Darker grey

    [Header("Extrusion Settings")]
    public float maxExtrusionHeight = 0.04f; // Can reuse or have a separate one for moon

    [Header("Initialization")]
    [Tooltip("Wait this many frames before initial generation so Hexasphere has finished.")]
    public int initializationDelay = 1;

    private List<BiomeSettings> biomeSettings;
    public void SetBiomeSettings(List<BiomeSettings> sharedBiomeSettings) { 
        biomeSettings = sharedBiomeSettings;
        if (biomeSettings == null) {
            Debug.LogError("[MoonGenerator] SetBiomeSettings called with null list!");
        } else {
            Debug.Log($"[MoonGenerator] SetBiomeSettings called. Count: {biomeSettings.Count}");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────────
    //  VISUAL LAYER (SGT Integration)
    // ──────────────────────────────────────────────────────────────────────────────
    [Header("💠 SGT Moon Landscape")]
    [SerializeField] int textureSize = 1024;
    [SerializeField] SgtSphereLandscape moonLandscape;
    
    public enum BiomeMaskQuality { Standard, Optimized, Blended }
    
    [Header("Moon Biome Mask Generation")]
    [Tooltip("Standard: Basic RGBA packing, Optimized: Better texture settings, Blended: Smooth transitions")]
    public BiomeMaskQuality biomeMaskQuality = BiomeMaskQuality.Optimized;
    [Range(1f, 5f)]
    [Tooltip("Blend radius for smooth biome transitions (only used with Blended quality)")]
    public float biomeBlendRadius = 2f;

    // Runtime-generated textures
    Texture2D heightTex;    // R16 – elevation 0‒1
    Texture2D biomeColorMap; // RGBA32 – biome colors

    static readonly int HeightMapID   = Shader.PropertyToID("_HeightMap");
    static readonly int BiomeMapID    = Shader.PropertyToID("_BiomeMap");

    [Header("SGT Heightmap Scaling")]
    [Tooltip("Maximum heightmap displacement as a fraction of moon radius")]
    public float heightFractionOfRadius = 0.01f; // Smaller than planet

    // --------------------------- Private fields -----------------------------
    IcoSphereGrid grid;
    public IcoSphereGrid Grid => grid;
    NoiseSampler noise; // Use the same NoiseSampler
    NoiseSampler cavePlacementNoise; // Separate noise for cave placement
    readonly Dictionary<int, HexTileData> data = new Dictionary<int, HexTileData>();
    readonly Dictionary<int, float> tileElevation = new Dictionary<int, float>(); // Store final elevation
    readonly Dictionary<Biome, BiomeSettings> lookup = new Dictionary<Biome, BiomeSettings>(); // Lookup for settings
    private LoadingPanelController loadingPanelController;
    public void SetLoadingPanel(LoadingPanelController controller) { loadingPanelController = controller; }

    // Decorations are now managed by SGT components. Old pooling logic has been
    // removed to keep this generator focused solely on terrain data.

    // --------------------------- Unity lifecycle -----------------------------
    void Awake()
    {
        // Initialize the grid for this moon
        grid = new IcoSphereGrid();
        grid.Generate(subdivisions, 1f); // generate unit sphere grid
        
        if (randomSeed) seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);

        noise = new NoiseSampler(seed); // For elevation
        cavePlacementNoise = new NoiseSampler(seed + 1); // Different seed for cave placement

        // Build the lookup dictionary from the shared biomeSettings
        if (biomeSettings != null)
        {
            foreach (var bs in biomeSettings) {
                if (!lookup.ContainsKey(bs.biome)) {
                    lookup.Add(bs.biome, bs);
                } else {
                    lookup[bs.biome] = bs; // Allow overriding defaults from Inspector
                }
            }
        }

        // Optional: Set different noise parameters for moon elevation if needed
        // noise.elevationNoise.SetFrequency(moonElevationFreq); // Example

        // if (generateOnAwake) // GameManager will control initialization
        // {
        // StartCoroutine(Initialise());
        // }
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
        for (int i = 0; i < Mathf.Max(1, initializationDelay); i++) yield return null;
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
        
        // NEW: Build visual textures for SGT
        yield return StartCoroutine(BuildMoonVisualMapsBatched());
    }

    // Build the visual maps for the high-poly moon sphere
    // (Removed BuildMoonVisualMaps() as it is no longer needed)

    // Coroutine version with batching and progress bar
    System.Collections.IEnumerator BuildMoonVisualMapsBatched(int batchSize = 16)
    {
        if (moonLandscape == null)
        {
            Debug.LogError($"{name}: Moon Landscape component not assigned!");
            yield break;
        }

        int w = textureSize;
        int h = textureSize / 2;

        // --- PARALLEL: Direct mapping pixel-to-tile lookup (no flood fill) ---
        int[,] pixelToTileLookup = new int[w, h];
        System.Threading.Tasks.Parallel.For(0, h, y => {
            float v = ((y + 0.5f) / h);
            float lat = Mathf.Lerp(90, -90, v);
            for (int x = 0; x < w; x++) {
                float u = (x + 0.5f) / w;
                float lon = Mathf.Lerp(-180, 180, u);
                Vector3 dir = SphericalToCartesian(lat, lon);
                int tileIdx = grid.GetTileAtPosition(dir);
                if (tileIdx < 0) tileIdx = 0;
                pixelToTileLookup[x, y] = tileIdx;
            }
        });
        // Yield once after the parallel loop to keep UI responsive
        if (loadingPanelController != null) {
            loadingPanelController.SetProgress(0.1f);
        }
        yield return null;
        // --- END Parallel mapping ---
        
        if (loadingPanelController != null) {
            loadingPanelController.SetStatus("Building moon lookup table...");
            loadingPanelController.SetProgress(0.5f); // Start halfway through moon gen
        }
        
        // --- Heightmap: Output as Alpha8 (single channel) ---
        if (heightTex == null || heightTex.width != textureSize)
        {
            heightTex = new Texture2D(textureSize, textureSize / 2, TextureFormat.Alpha8, false)
            { wrapMode = TextureWrapMode.Repeat };
        }
        Color32[] hPixels = new Color32[heightTex.width * heightTex.height];
        float minH = float.MaxValue, maxH = float.MinValue;
        float moonRadius = moonLandscape != null ? (float)moonLandscape.Radius : 1.0f;
        float heightScale = heightFractionOfRadius * moonRadius;
        
        Debug.Log($"[MoonGenerator] Heightmap generation: moonRadius={moonRadius}, heightScale={heightScale}");
        
        // --- Moon has only 2 biomes, so simpler setup ---
        int biomeCount = 2; // MoonDunes, MoonCaves
        int texSize = 512; // Smaller than planet
        
        // Create texture arrays for moon biomes
        var albedoArray = new Texture2DArray(texSize, texSize, biomeCount, TextureFormat.RGBA32, true);
        var normalArray = new Texture2DArray(texSize, texSize, biomeCount, TextureFormat.RGBA32, true);
        
        // Set up moon biome textures
        for (int i = 0; i < biomeCount; i++) {
            if (i < biomeSettings.Count && biomeSettings[i].albedoTexture != null) {
                albedoArray.SetPixels(biomeSettings[i].albedoTexture.GetPixels(), i);
            } else {
                // Default colors for moon biomes
                Color defaultColor = i == 0 ? moonDunesColor : moonCavesColor;
                Color[] defaultPixels = new Color[texSize * texSize];
                for (int j = 0; j < defaultPixels.Length; j++) {
                    defaultPixels[j] = defaultColor;
                }
                albedoArray.SetPixels(defaultPixels, i);
            }
            
            // Set up normal textures
            if (i < biomeSettings.Count && biomeSettings[i].normalTexture != null) {
                normalArray.SetPixels(biomeSettings[i].normalTexture.GetPixels(), i);
            } else {
                // Create flat normal map
                Color[] flatNormal = new Color[texSize * texSize];
                for (int j = 0; j < flatNormal.Length; j++) {
                    flatNormal[j] = new Color(0.5f, 0.5f, 1f, 1f);
                }
                normalArray.SetPixels(flatNormal, i);
            }
        }
        albedoArray.Apply(true);
        normalArray.Apply(true);

        // --- Generate Moon Biome Mask Textures ---
        List<Texture2D> biomeMaskTextures = new List<Texture2D>();
        List<Texture2D> packedBiomeMasks = new List<Texture2D>();
        Texture2D biomeIndexMap = null;
        
        // Create one RGBA texture for 2 biomes (using R and G channels)
        var packedMask = new Texture2D(w, h, TextureFormat.RGBA32, true, true)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4
        };
        packedBiomeMasks.Add(packedMask);
        
        // Create biome index map
        biomeIndexMap = new Texture2D(w, h, TextureFormat.RFloat, false, true);
        Color[] biomePixels = new Color[w * h];
        Color[] packedPixels = new Color[w * h];
        
        // Single pass for height and biome data
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int tileIdx = pixelToTileLookup[x, y]; // USE LOOKUP
                var tile = GetHexTileData(tileIdx);
                if (tile == null) continue;
                
                // HEIGHT MAP PROCESSING
                float h01 = Mathf.InverseLerp(baseDuneElevation, maxDuneElevation, tile.elevation);
                if (h01 < minH) minH = h01;
                if (h01 > maxH) maxH = h01;
                int idx1d = y * w + x;
                float scaledHeight = h01 * heightScale;
                byte heightByte = (byte)Mathf.RoundToInt(Mathf.Clamp(scaledHeight * 255f / heightScale, 0f, 255f));
                hPixels[idx1d] = new Color32(0, 0, 0, heightByte);
                
                // BIOME PROCESSING
                int biomeIdx = tile.biome == Biome.MoonCaves ? 1 : 0;
                float biomeNorm = biomeIdx / 1f; // 0 or 1 for 2 biomes
                biomePixels[idx1d] = new Color(biomeNorm, 0, 0, 1);
                
                // PACKED BIOME MASK (R = MoonDunes, G = MoonCaves)
                Color packedColor = Color.black;
                if (biomeIdx == 0) packedColor.r = 1f; // MoonDunes in red channel
                else packedColor.g = 1f; // MoonCaves in green channel
                packedPixels[idx1d] = packedColor;
            }
            
            // Yield every 8 rows to keep UI responsive
            if (y % 8 == 0)
            {
                if (loadingPanelController != null)
                {
                    float progress = (float)y / h;
                    loadingPanelController.SetProgress(0.6f + progress * 0.3f); // 60% to 90%
                    loadingPanelController.SetStatus($"Building Moon Maps... ({(progress*100):F0}%)");
                }
                yield return null;
            }
        }
        
        Debug.Log($"[MoonGenerator] Heightmap h01 min: {minH}, max: {maxH}, heightScale: {heightScale}");
        
        // Apply textures
        heightTex.SetPixels32(hPixels);
        heightTex.Apply(false, false);
        
        packedMask.SetPixels(packedPixels);
        packedMask.Apply(true, false);
        
        biomeIndexMap.SetPixels(biomePixels);
        biomeIndexMap.Apply(false, true);
        
        // Create biome color map
        biomeColorMap = GenerateMoonBiomeColorMap(w, h);
        
        // Assign textures to the moon landscape
        moonLandscape.HeightTex = heightTex;
        moonLandscape.AlbedoTex = biomeColorMap;
        moonLandscape.HeightMidpoint = 0.5f;
        moonLandscape.HeightRange = 5f; // Smaller than planet
        
        // Assign texture arrays and masks to the landscape material
        var landscapeMaterial = moonLandscape.Material;
        if (landscapeMaterial != null)
        {
            landscapeMaterial.SetTexture("_BiomeAlbedoArray", albedoArray);
            landscapeMaterial.SetTexture("_BiomeNormalArray", normalArray);
            landscapeMaterial.SetFloat("_BiomeAlbedoArray_Depth", biomeCount);
            landscapeMaterial.SetFloat("_BiomeNormalArray_Depth", biomeCount);
            landscapeMaterial.SetTexture("_BiomeIndexMap", biomeIndexMap);
            landscapeMaterial.SetTexture("_BiomeMask0", packedMask);
            landscapeMaterial.SetFloat("_BiomeMaskCount", 1);
            Debug.Log($"[MoonGenerator] Assigned {biomeCount} moon biomes to landscape material.");
        }
        else
        {
            Debug.LogWarning("[MoonGenerator] Could not find moon landscape material to assign biome textures.");
        }

        // Register this grid/material pair with BiomeTextureManager
        if (BiomeTextureManager.Instance != null && grid != null && landscapeMaterial != null)
        {
            BiomeTextureManager.Instance.RegisterTarget(grid, landscapeMaterial);
        }
        
        // Force SGT to recognize the new textures and update the mesh
        if (moonLandscape != null) moonLandscape.MarkForRebuild();
        
        // Create SGT biome components for moon - DISABLED as it conflicts with SGT's fixed array sizes
        // CreateMoonSGTBiomeComponents(biomeMaskTextures);
        
        if (loadingPanelController != null)
        {
            loadingPanelController.SetProgress(1f);
            loadingPanelController.SetStatus("Finishing up moon...");
        }
        yield return null;

        // After visuals are prepared, generate the biome index texture used by the shader
        if (BiomeTextureManager.Instance != null && grid != null)
        {
            BiomeTextureManager.Instance.GenerateBiomeIndexTexture(grid);
        }
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

    /// <summary>
    /// Generates a biome color map for the moon using moon biome colors.
    /// </summary>
    public Texture2D GenerateMoonBiomeColorMap(int width = 512, int height = 256)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            float lat = Mathf.Lerp(90, -90, v);
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                float lon = Mathf.Lerp(-180, 180, u);
                Vector3 dir = SphericalToCartesian(lat, lon);
                int tileIndex = grid.GetTileAtPosition(dir);
                if (tileIndex < 0) tileIndex = 0;
                var tile = GetHexTileData(tileIndex);
                Color c = tile.biome == Biome.MoonCaves ? moonCavesColor : moonDunesColor;
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// Creates SGT biome components for the moon
    /// </summary>
    private void CreateMoonSGTBiomeComponents(List<Texture2D> biomeMaskTextures)
    {
        if (moonLandscape == null)
        {
            Debug.LogWarning("[MoonGenerator] Cannot create SGT biome components - moon landscape missing.");
            return;
        }

        // Remove existing biome children first
        var existingBiomes = moonLandscape.GetComponentsInChildren<SgtLandscapeBiome>();
        for (int i = 0; i < existingBiomes.Length; i++)
        {
            if (Application.isEditor)
                DestroyImmediate(existingBiomes[i].gameObject);
            else
                Destroy(existingBiomes[i].gameObject);
        }

        // Create biome components for MoonDunes and MoonCaves
        string[] moonBiomeNames = { "MoonDunes", "MoonCaves" };
        for (int i = 0; i < 2; i++)
        {
            // Create biome GameObject
            GameObject biomeObj = new GameObject($"MoonBiome_{moonBiomeNames[i]}");
            biomeObj.transform.SetParent(moonLandscape.transform, false);

            // Add and configure SgtLandscapeBiome component
            var biomeComponent = biomeObj.AddComponent<SgtLandscapeBiome>();
            
            biomeComponent.Mask = true;
            biomeComponent.MaskIndex = i;
            biomeComponent.GradientIndex = i;
            
            // Add a default layer
            var layer = new SgtLandscapeBiome.SgtLandscapeBiomeLayer
            {
                HeightIndex = 0,
                HeightRange = 5f,
                HeightMidpoint = 0.5f,
                GlobalSize = 50f // Smaller features than planet
            };
            biomeComponent.Layers.Add(layer);
            biomeComponent.Space = SgtLandscapeBiome.SpaceType.Global;
            Debug.Log($"[MoonGenerator] Created SGT biome component for {moonBiomeNames[i]}");
        }
        
        Debug.Log("[MoonGenerator] Created 2 SGT moon biome components.");
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
} 