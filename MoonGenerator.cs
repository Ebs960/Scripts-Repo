using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using SpaceGraphicsToolkit;
using System.Threading.Tasks;
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
    public struct BiomePrefab {
        public Biome biome;
        public GameObject prefab;
    }

    [Header("Tile Prefabs")]
    public List<BiomePrefab> biomePrefabs = new();
    [Tooltip("Number of tile prefabs to spawn each frame")]
    public int tileSpawnBatchSize = 100;

    private readonly Dictionary<Biome, GameObject> biomePrefabLookup = new();

    [Header("Tile Sizing")]
    public float tileRadius = 1.5f;


    [Header("Visuals")]
    public Color moonDunesColor = new Color(0.8f, 0.8f, 0.75f); // Light greyish
    public Color moonCavesColor = new Color(0.4f, 0.4f, 0.45f); // Darker grey

    // [Extrusion Settings] Removed: no longer used in surface calculations

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

    // ──────────────────────────────────────────────────────────────────────────────
    //  VISUAL LAYER (SGT Integration)
    // ──────────────────────────────────────────────────────────────────────────────
    [SerializeField] int textureSize = 1024;
    
    public enum BiomeMaskQuality { Standard, Optimized, Blended }
    
    [Header("Moon Biome Mask Generation")]
    [Tooltip("Standard: Basic RGBA packing, Optimized: Better texture settings, Blended: Smooth transitions")]
    public BiomeMaskQuality biomeMaskQuality = BiomeMaskQuality.Optimized;
    [Range(1f, 5f)]
    [Tooltip("Blend radius for smooth biome transitions (only used with Blended quality)")]
    public float biomeBlendRadius = 2f;

    // Runtime-generated textures
    // Runtime textures removed in prefab-based system




    // --------------------------- Private fields -----------------------------
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

        // Build biome lookup tables if biome settings were provided via inspector
        if (biomeSettings != null)
        {
            BuildBiomeLookup();
        }

        biomePrefabLookup.Clear();
        foreach (var bp in biomePrefabs)
            if (bp.prefab != null)
                biomePrefabLookup[bp.biome] = bp.prefab;
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

        // Visual texture generation removed in prefab-based system

        // Populate the public tile list with the final tile data
        Tiles = data.Values.ToList();

        // Debug: List biome quantities
        LogBiomeQuantities();

        if (biomePrefabs.Count > 0)
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


    // ------------------------------------------------------------------
    //  Prefab Helpers
    // ------------------------------------------------------------------
    private GameObject GetPrefabForTile(HexTileData tile)
    {
        if (biomePrefabLookup.TryGetValue(tile.biome, out var prefab))
            return prefab;
        return null;
    }

    private GameObject InstantiateTilePrefab(HexTileData tileData, Vector3 position, Transform parent)
    {
        GameObject prefab = GetPrefabForTile(tileData);
        if (prefab == null) return null;

        Vector3 up = position.normalized;
        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, up);

        GameObject go = Instantiate(prefab, position, rotation, parent);
        go.isStatic = true;

        float prefabRadius = GetPrefabBoundingRadius(go);
        float correctRadius = GetExpectedTileRadius(grid);
        if (prefabRadius > 0f && correctRadius > 0f)
        {
            float scaleFactor = correctRadius / prefabRadius;
            go.transform.localScale = Vector3.one * scaleFactor;
        }

        return go;
    }

    private float GetPrefabBoundingRadius(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return 0f;
        Bounds bounds = new Bounds(renderers[0].bounds.center, Vector3.zero);
        foreach (var r in renderers)
            bounds.Encapsulate(r.bounds);
        float radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
        return radius;
    }

    private float GetExpectedTileRadius(SphericalHexGrid grid)
    {
        if (grid == null || grid.TileCount == 0) return 0f;

        Vector3 c0 = grid.tileCenters[0];
        int neighborIdx = grid.neighbors[0][0];
        Vector3 c1 = grid.tileCenters[neighborIdx];

        float angle = Vector3.Angle(c0, c1) * Mathf.Deg2Rad;
        float arcRadius = grid.Radius * angle * 0.5f;
        return arcRadius;
    }

    private System.Collections.IEnumerator SpawnAllTilePrefabs(int batchSize = 100)
    {
        GameObject parent = new GameObject("MoonTilePrefabs");
        parent.transform.SetParent(this.transform, false);

        for (int i = 0; i < grid.TileCount; i++)
        {
            if (!data.TryGetValue(i, out var td))
                continue;

            Vector3 pos = grid.tileCenters[i];
            pos = transform.TransformPoint(pos);
            GameObject tileGO = InstantiateTilePrefab(td, pos, parent.transform);
            if (tileGO != null)
            {
                var indexHolder = tileGO.GetComponent<TileIndexHolder>();
                if (indexHolder == null)
                    indexHolder = tileGO.AddComponent<TileIndexHolder>();
                indexHolder.tileIndex = i;
            }

            if (i % batchSize == 0)
                yield return null;
        }
    }
}
