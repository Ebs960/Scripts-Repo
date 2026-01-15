using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using TMPro;
using System.Threading.Tasks;

/// <summary>
/// This handles decoration prefabs for each biome type independently
/// </summary>
[System.Serializable]
public struct BiomeDecorationEntry
{
    [Header("Biome Configuration")]
    public Biome biome;
    
    [Header("Decoration Prefabs")]
    [Tooltip("Decoration prefabs for this biome (trees, bushes, rocks, etc.)")]
    public GameObject[] decorationPrefabs;
    
    [Header("Spawn Settings")]
    [Range(0f, 1f)]
    [Tooltip("Chance this biome will get decorations (0 = never, 1 = always)")]
    public float spawnChance;
    
    [Range(1, 8)]
    [Tooltip("Minimum decorations to spawn on tiles of this biome")]
    public int minDecorations;
    
    [Range(1, 12)]
    [Tooltip("Maximum decorations to spawn on tiles of this biome")]
    public int maxDecorations;
    
    [Header("Positioning")]
    [Range(0.1f, 0.9f)]
    [Tooltip("Minimum distance from tile center (as fraction of tile size)")]
    public float minDistanceFromCenter;
    
    [Range(0.1f, 0.95f)]
    [Tooltip("Maximum distance from tile center (as fraction of tile size)")]
    public float maxDistanceFromCenter;
    
    [Header("Scale and Rotation")]
    [Range(0.1f, 15.0f)]
    [Tooltip("Scale multiplier for decorations in this biome")]
    public float scaleMultiplier;
    
    [Range(0f, 1f)]
    [Tooltip("Random scale variation (0 = no variation, 1 = ±100% variation)")]
    public float scaleVariation;
    
    [Tooltip("Should decorations randomly rotate around their up axis?")]
    public bool randomRotation;
    
    /// <summary>
    /// Get default settings for a biome
    /// </summary>
    public static BiomeDecorationEntry GetDefault(Biome biome)
    {
        return new BiomeDecorationEntry
        {
            biome = biome,
            decorationPrefabs = new GameObject[0],
            spawnChance = GetDefaultSpawnChance(biome),
            minDecorations = GetDefaultMinDecorations(biome),
            maxDecorations = GetDefaultMaxDecorations(biome),
            minDistanceFromCenter = 0.4f,
            maxDistanceFromCenter = 0.85f,
            scaleMultiplier = 1.0f,
            scaleVariation = 0.2f,
            randomRotation = true
        };
    }
    
    private static float GetDefaultSpawnChance(Biome biome)
    {
        return biome switch
        {
            // Water biomes - no decorations
            Biome.Ocean or Biome.Coast or Biome.Seas => 0f,
            
            // Lush biomes - high decoration chance
            Biome.Forest or Biome.Jungle or Biome.Rainforest => 0.9f,
            Biome.Grassland or Biome.Plains or Biome.Savannah => 0.8f,
            
            // Moderate decoration biomes
            Biome.Taiga or Biome.PineForest => 0.7f,
            Biome.Marsh or Biome.Swamp => 0.6f,
            
            // Sparse decoration biomes
            Biome.Desert or Biome.Tundra => 0.4f,
            Biome.Mountain or Biome.Snow => 0.3f,
            
            // Hostile biomes - minimal decorations
            Biome.Volcanic or Biome.Steam => 0.2f,
            Biome.Hellscape or Biome.Brimstone => 0.1f,
            
            // Moon biomes
            Biome.MoonDunes => 0.5f,
            Biome.MoonCaves => 0.3f,
            
            // Default
            _ => 0.5f
        };
    }
    
    private static int GetDefaultMinDecorations(Biome biome)
    {
        return biome switch
        {
            // Lush biomes
            Biome.Forest or Biome.Jungle or Biome.Rainforest => 2,
            Biome.Grassland or Biome.Plains => 1,
            
            // Sparse biomes
            Biome.Desert or Biome.Tundra or Biome.Mountain => 1,
            
            // Very sparse biomes
            Biome.Volcanic or Biome.Hellscape => 1,
            
            // Default
            _ => 1
        };
    }
    
    private static int GetDefaultMaxDecorations(Biome biome)
    {
        return biome switch
        {
            // Lush biomes - lots of decorations
            Biome.Forest or Biome.Jungle or Biome.Rainforest => 5,
            Biome.Grassland or Biome.Plains or Biome.Savannah => 4,
            
            // Moderate biomes
            Biome.Taiga or Biome.PineForest => 3,
            Biome.Marsh or Biome.Swamp => 3,
            
            // Sparse biomes
            Biome.Desert or Biome.Tundra or Biome.Mountain => 2,
            
            // Very sparse biomes
            Biome.Volcanic or Biome.Hellscape => 1,
            
            // Default
            _ => 3
        };
    }
}

/// <summary>
/// Component that manages decoration spawning for both planet and moon generators
/// </summary>
[System.Serializable]
public class BiomeDecorationManager
{
    [Header("Biome Decoration Configuration")]
    [Tooltip("Decoration settings for each biome type")]
    public BiomeDecorationEntry[] biomeDecorations = new BiomeDecorationEntry[0];
    
    [Header("Global Decoration Settings")]
    [Tooltip("Enable decoration spawning")]
    public bool enableDecorations = true;
    
    [Range(0.5f, 3.0f)]
    [Tooltip("Global scale multiplier applied to all decorations")]
    public float globalScaleMultiplier = 1.0f;
    
    private Dictionary<Biome, BiomeDecorationEntry> decorationLookup;
    
    /// <summary>
    /// Initialize the decoration lookup dictionary
    /// </summary>
    public void Initialize()
    {
        decorationLookup = new Dictionary<Biome, BiomeDecorationEntry>();
        
        foreach (var entry in biomeDecorations)
        {
            decorationLookup[entry.biome] = entry;
        }
    }
    
    /// <summary>
    /// Get decoration settings for a specific biome
    /// </summary>
    public BiomeDecorationEntry GetDecorationSettings(Biome biome)
    {
        if (decorationLookup == null)
            Initialize();
            
        if (decorationLookup.TryGetValue(biome, out var settings))
            return settings;
            
        // Return default settings if not found
        return BiomeDecorationEntry.GetDefault(biome);
    }
    
    /// <summary>
    /// Check if a biome should have decorations spawned
    /// </summary>
    public bool ShouldSpawnDecorations(Biome biome)
    {
        if (!enableDecorations)
            return false;
            
        var settings = GetDecorationSettings(biome);
        return settings.decorationPrefabs.Length > 0 && UnityEngine.Random.value < settings.spawnChance;
    }
    
    /// <summary>
    /// Get a random decoration prefab for a biome
    /// </summary>
    public GameObject GetRandomDecorationPrefab(Biome biome)
    {
        var settings = GetDecorationSettings(biome);
        if (settings.decorationPrefabs.Length == 0)
            return null;
            
        return settings.decorationPrefabs[UnityEngine.Random.Range(0, settings.decorationPrefabs.Length)];
    }
    
    /// <summary>
    /// Get the number of decorations to spawn for a tile
    /// </summary>
    public int GetDecorationCount(Biome biome)
    {
        var settings = GetDecorationSettings(biome);
        return UnityEngine.Random.Range(settings.minDecorations, settings.maxDecorations + 1);
    }
}

public class PlanetGenerator : MonoBehaviour, IHexasphereGenerator
{
    public static PlanetGenerator Instance { get; private set; }

    [Header("Diagnostics")]
    [Tooltip("Enable verbose diagnostic logs for generation steps.")]
    public bool enableDiagnostics = false;
    [SerializeField] private bool debugDrawContinents = true;


    [Header("Map Settings")] 
    public bool randomSeed = true;
    public int seed = 12345;
    // Spherical radius removed in flat-only refactor

    // Public property to access the seed
    public int Seed => seed;

    // --- Continent Parameters (Stamping) ---
    [Header("Continent Generation (Stamping)")]
    [Tooltip("The target number of continents. Placement is deterministic for common counts (1-8). Higher counts might revert to random spread.")]
    [Min(1)]
    public int numberOfContinents = 6;

    private List<ContinentData> continents;


    // Continent sizing now uses raw tile counts (configured per-map-size)

    [Tooltip("Small map: min continent width (tiles)")]
    public int minContinentWidthTilesSmall = 80;
    [Tooltip("Small map: max continent width (tiles)")]
    public int maxContinentWidthTilesSmall = 200;
    [Tooltip("Small map: min continent height (tiles)")]
    public int minContinentHeightTilesSmall = 40;
    [Tooltip("Small map: max continent height (tiles)")]
    public int maxContinentHeightTilesSmall = 100;

    [Tooltip("Standard map: min continent width (tiles)")]
    public int minContinentWidthTilesStandard = 200;
    [Tooltip("Standard map: max continent width (tiles)")]
    public int maxContinentWidthTilesStandard = 400;
    [Tooltip("Standard map: min continent height (tiles)")]
    public int minContinentHeightTilesStandard = 100;
    [Tooltip("Standard map: max continent height (tiles)")]
    public int maxContinentHeightTilesStandard = 200;

    [Tooltip("Large map: min continent width (tiles)")]
    public int minContinentWidthTilesLarge = 400;
    [Tooltip("Large map: max continent width (tiles)")]
    public int maxContinentWidthTilesLarge = 800;
    [Tooltip("Large map: min continent height (tiles)")]
    public int minContinentHeightTilesLarge = 200;
    [Tooltip("Large map: max continent height (tiles)")]
    public int maxContinentHeightTilesLarge = 400;
    
    [Tooltip("Maximum random offset applied to deterministic seed positions (0 = no offset, higher = more variance).")]
    [Range(0f, 0.8f)]
    public float seedPositionVariance = 0.1f; // Controls randomness in seed placement
    
    // --- Noise Settings --- 
    [Header("Noise Settings")] 
    public float elevationFreq = 2f, moistureFreq = 4f;

    [Range(-0.3f, 0.3f)]
    [Tooltip("Bias for moisture levels. Positive values make the planet wetter, negative values make it drier.")]
    public float moistureBias = 0f;

    [Range(-0.65f, 0.65f)]
    [Tooltip("Bias for temperature. Positive values make the planet hotter, negative values make it colder.")]
    public float temperatureBias = 0f;
    
    [Header("Latitude / Temperature Blending")]
    [Range(0f, 1f)]
    [Tooltip("Weight of latitude (north/south) influence when computing temperature. Higher = poles/equator dominate over noise.")]
    public float latitudeInfluence = 0.85f;

    [Range(0.2f, 2f)]
    [Tooltip("Exponent applied to absolute latitude when computing latitude temperature. >1 makes poles colder (steeper gradient).")]
    public float latitudeExponent = 1.15f;

    // --- NEW: Elevation Features ---
    [Header("Elevation Features")]
    [Range(0f, 2f)]
    [Tooltip("Elevation value (0-1) above which tiles become mountains.")]
    public float mountainThreshold = 0.75f;
    [Range(0f, 2f)]
    [Tooltip("Elevation value (0-1) above which tiles become hills (if not already mountains).")]
    public float hillThreshold = 0.55f;
    
    // --- NEW: Elevation Range Settings ---
    [Header("Elevation Range Settings")]
    [Range(0f, 1f)]
    [Tooltip("The minimum elevation level for land and glacier tiles (before noise).")]
    public float baseLandElevation = 0.15f;
    
    [Range(-2f, 1f)]
    [Tooltip("Elevation for ocean tiles.")]
    public float oceanElevation = 0f;
    [Range(-2f, 1f)]
    [Tooltip("Elevation for sea tiles.")]
    public float seasElevation = 0.02f;
    [Range(-2f, 1f)]
    [Tooltip("Elevation for coast tiles.")]
    public float coastElevation = 0.05f;
    
    [Range(0f, 0.5f)]
    [Tooltip("Additional elevation boost for hill tiles (added to their base elevation).")]
    public float hillElevationBoost = 0.1f;
    
    [Range(0f, 1.5f)]
    [Tooltip("The absolute maximum elevation any tile can reach (after noise). Set near 1.0 for full range.")]
    public float maxTotalElevation = 1.0f;
    
    [Header("Experimental / Advanced")]
    [Range(0f, 0.5f)]
    [Tooltip("Billow noise weight for rolling hills. Higher = more rounded terrain.")]
    public float billowHillWeight = 0.2f;
    
    [Range(0f, 0.6f)]
    [Tooltip("Ridged noise weight for mountain spines. Higher = sharper peaks.")]
    public float ridgedMountainWeight = 0.35f;
    
    // --- River Generation (Placeholder) ---
    [Header("River Generation")]
    public bool enableRivers = true;
    [Range(5, 100)]
    [Tooltip("Minimum length a river must be to be included")]
    public int minRiverLength = 8;
    [Range(0, 20)]
    [Tooltip("Minimum rivers per continent")]
    public int minRiversPerContinent = 1;
    [Range(1, 20)]
    [Tooltip("Maximum rivers per continent")]
    public int maxRiversPerContinent = 2;
    [Range(5, 50)]
    [Tooltip("Maximum length (in tiles) for a single river path")]
    public int maxRiverPathLength = 15;
    [Range(0.01f, 0.2f)]
    [Tooltip("Elevation drop applied along river tiles")]
    public float riverDepth = 0.05f;

    // --- Lake Generation ---
    [Header("Lake Generation")]
    public bool enableLakes = true;
    [Range(1, 30)]
    [Tooltip("Target number of lakes to generate")]
    public int numberOfLakes = 8;
    [Range(1, 15)]
    [Tooltip("Minimum size of a lake in tiles")]
    public int minLakeSize = 3;
    [Range(3, 30)]
    [Tooltip("Maximum size of a lake in tiles")]
    public int maxLakeSize = 12;
    [Range(0f, 0.5f)]
    [Tooltip("Elevation threshold - lakes form in depressions below this relative elevation")]
    public float lakeElevationThreshold = 0.25f;
    [Range(0f, 0.2f)]
    [Tooltip("Fixed elevation for lake tiles")]
    public float lakeElevation = 0.02f;
    [Range(0f, 0.2f)]
    [Tooltip("Render elevation for lake tiles")]
    public float lakeRenderElevation = 0.05f;
    [Tooltip("Whether rivers should connect to lakes (flow into or out of them)")]
    public bool connectRiversToLakes = true;

    // --- Island Generation ---
    [Header("Island Generation")]
    [Tooltip("Number of islands to generate (separate from continents)")]
    public int numberOfIslands = 8;
    [Tooltip("Whether to generate islands in addition to continents")]
    public bool generateIslands = true;
    [Range(1, 4000)]
    [Tooltip("Small map: min island width (tiles)")]
    public int minIslandWidthTilesSmall = 1;
    [Tooltip("Small map: max island width (tiles)")]
    public int maxIslandWidthTilesSmall = 24;
    [Tooltip("Small map: min island height (tiles)")]
    public int minIslandHeightTilesSmall = 4;
    [Tooltip("Small map: max island height (tiles)")]
    public int maxIslandHeightTilesSmall = 12;

    [Tooltip("Standard map: min island width (tiles)")]
    public int minIslandWidthTilesStandard = 20;
    [Tooltip("Standard map: max island width (tiles)")]
    public int maxIslandWidthTilesStandard = 60;
    [Tooltip("Standard map: min island height (tiles)")]
    public int minIslandHeightTilesStandard = 10;
    [Tooltip("Standard map: max island height (tiles)")]
    public int maxIslandHeightTilesStandard = 30;

    [Tooltip("Large map: min island width (tiles)")]
    public int minIslandWidthTilesLarge = 40;
    [Tooltip("Large map: max island width (tiles)")]
    public int maxIslandWidthTilesLarge = 120;
    [Tooltip("Large map: min island height (tiles)")]
    public int minIslandHeightTilesLarge = 20;
    [Tooltip("Large map: max island height (tiles)")]
    public int maxIslandHeightTilesLarge = 60;
    [Tooltip("Generate islands as chains/clusters instead of random scatter.")]
    public bool generateIslandChains = true;
    [Range(2, 6)]
    [Tooltip("Number of islands per chain.")]
    public int islandsPerChain = 3;


    [Header("Decoration System")]
    [Tooltip("Modern decoration system for spawning biome-specific decorations")]
    public BiomeDecorationManager decorationManager = new BiomeDecorationManager();

    [Header("Map Type")]
    public string currentMapTypeName = ""; // The current map type name
public bool isRainforestMapType = false; // Whether this is a rainforest map type (determined from map name)
public bool isScorchedMapType = false; // Whether this is a scorched map type
public bool isInfernalMapType = false; // Whether this is an infernal map type
public bool isDemonicMapType = false; // Add this field
public bool isIceWorldMapType = false; // Whether this is an ice world map type
public bool isMonsoonMapType = false; // Whether this is a monsoon map type


    [Header("Real Planet Flags")]
    public bool isMarsWorldType, isVenusWorldType, isMercuryWorldType,
        isJupiterWorldType, isSaturnWorldType, isUranusWorldType,
        isNeptuneWorldType, isPlutoWorldType,
        isTitanWorldType, isEuropaWorldType, isIoWorldType,
        isGanymedeWorldType, isCallistoWorldType, isLunaWorldType;

    [Header("Feature Toggles")]
    public bool allowOceans = true;
    public bool allowIslands = true;

    public void ClearRealPlanetFlags()
    {
        isMarsWorldType = isVenusWorldType = isMercuryWorldType =
        isJupiterWorldType = isSaturnWorldType = isUranusWorldType =
        isNeptuneWorldType = isPlutoWorldType =
        isTitanWorldType = isEuropaWorldType = isIoWorldType =
        isGanymedeWorldType = isCallistoWorldType = isLunaWorldType = false;
    }


    // --------------------------- Private fields -----------------------------
    SphericalHexGrid grid;
    public SphericalHexGrid Grid => grid;
    NoiseSampler noise;
    public Dictionary<int, HexTileData> data = new();
    public Dictionary<int, HexTileData> baseData = new();
    private Vector3 noiseOffset;
    // Cache elevation for river generation
    public Dictionary<int, float> tileElevation = new Dictionary<int, float>();
    public int landTilesGenerated = 0; // Moved to class scope to be accessible by local coroutines
    /// <summary>
    /// Public list containing the final HexTileData for every tile on the planet.
    /// This is rebuilt after surface generation completes.
    /// </summary>
    public List<HexTileData> Tiles { get; private set; } = new List<HexTileData>();
    public bool HasGeneratedSurface { get; private set; } = false;
    // Raised when surface generation fully completes
    public event System.Action OnSurfaceGenerated;
    private LoadingPanelController loadingPanelController;

    // OBSOLETE: Prefab loading removed - new system uses texture-based rendering


    // --------------------------- Unity lifecycle -----------------------------
    void Awake()
    {
        
        
        // Multi-planet-first: set the static Instance if unset, but do not destroy duplicates.
        if (Instance == null)
        {
            Instance = this;
        }
        

        // OBSOLETE: Prefab loading code removed - new system uses texture-based rendering
        // Initialize the grid for this planet (will be configured by GameManager)
        grid = new SphericalHexGrid();
        

                
        if (randomSeed) seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);

        noise = new NoiseSampler(seed);

        var rand = new System.Random(seed);
        float ox = (float)(rand.NextDouble() * 2000.0 - 1000.0);
        float oy = (float)(rand.NextDouble() * 2000.0 - 1000.0);
        float oz = (float)(rand.NextDouble() * 2000.0 - 1000.0);
        noiseOffset = new Vector3(ox, oy, oz);

        // OBSOLETE: Biome prefab lookup removed - new system uses texture-based rendering

#if UNITY_EDITOR
        UnityEditor.EditorUtility.ClearProgressBar();
#endif
    }
    
    public int planetIndex = 0;

    void Start()
    {
        ClimateManager mgr = null;
        if (GameManager.Instance != null)
        {
            mgr = GameManager.Instance.GetClimateManager(planetIndex);
        }
        else
        {
            mgr = ClimateManager.Instance;
        }

        if (mgr != null)
        {
            mgr.OnSeasonChanged += HandleSeasonChange;
        }
    }

    void OnDestroy()
    {
        ClimateManager mgr = null;
        if (GameManager.Instance != null)
        {
            mgr = GameManager.Instance.GetClimateManager(planetIndex);
        }
        else
        {
            mgr = ClimateManager.Instance;
        }

        if (mgr != null)
        {
            mgr.OnSeasonChanged -= HandleSeasonChange;
        }

    }

    private void HandleSeasonChange(Season newSeason)
    {
        
    }
    


    // --------------------------- Surface Generation --------------------------
    /// <summary>
    /// Generates the planet's surface with stamped continents, oceans, and biomes.
    /// Landmask is stamp-only; noise is used solely for elevation and climate variation.
    /// </summary>
    public System.Collections.IEnumerator GenerateSurface()
    {
        // Clear previous data
        data.Clear();
        baseData.Clear();
        tileElevation.Clear();
        Tiles.Clear();
        landTilesGenerated = 0;

        // ── 1. Noise Offset Setup (as before) ──────────────────────────────
        if (noiseOffset == Vector3.zero) {
            var prng = new System.Random(seed);
            noiseOffset = new Vector3(
                 (float)(prng.NextDouble() * 2000.0 - 1000.0),
                 (float)(prng.NextDouble() * 2000.0 - 1000.0),
                 (float)(prng.NextDouble() * 2000.0 - 1000.0));
        }
        int tileCount = grid.TileCount;

        // Configure noise sampler for this map's dimensions
        float mapWidth = grid.Width;
        float mapHeight = grid.Height;
        noise.ConfigureForMapSize(mapWidth, mapHeight);

        // DIAGNOSTICS: report key settings and grid stats
        if (enableDiagnostics)
        {
            Debug.Log($"[PlanetGenerator][Diag] mapWidth={mapWidth:F1} mapHeight={mapHeight:F1} tiles={tileCount}");
            // Log continent/island sizing (tile-based system)
            int cMinW = minContinentWidthTilesStandard, cMaxW = maxContinentWidthTilesStandard, cMinH = minContinentHeightTilesStandard, cMaxH = maxContinentHeightTilesStandard;
            int iMinW = minIslandWidthTilesStandard, iMaxW = maxIslandWidthTilesStandard, iMinH = minIslandHeightTilesStandard, iMaxH = maxIslandHeightTilesStandard;
            switch (GameSetupData.mapSize)
            {
                case GameManager.MapSize.Small:
                    cMinW = minContinentWidthTilesSmall; cMaxW = maxContinentWidthTilesSmall; cMinH = minContinentHeightTilesSmall; cMaxH = maxContinentHeightTilesSmall;
                    iMinW = minIslandWidthTilesSmall; iMaxW = maxIslandWidthTilesSmall; iMinH = minIslandHeightTilesSmall; iMaxH = maxIslandHeightTilesSmall;
                    break;
                case GameManager.MapSize.Large:
                    cMinW = minContinentWidthTilesLarge; cMaxW = maxContinentWidthTilesLarge; cMinH = minContinentHeightTilesLarge; cMaxH = maxContinentHeightTilesLarge;
                    iMinW = minIslandWidthTilesLarge; iMaxW = maxIslandWidthTilesLarge; iMinH = minIslandHeightTilesLarge; iMaxH = maxIslandHeightTilesLarge;
                    break;
            }
            Debug.Log($"[StampGen][Diag] mapSize={GameSetupData.mapSize} contTiles(WxH) min={cMinW}x{cMinH} max={cMaxW}x{cMaxH} minDistance={GameSetupData.continentMinDistanceTiles}");
            Debug.Log($"[StampGen][Diag] islandRadius min={GameSetupData.islandMinRadiusTiles} max={GameSetupData.islandMaxRadiusTiles} minDistanceFromContinents={GameSetupData.islandMinDistanceFromContinents}");
            Debug.Log($"[StampGen][Diag] lakeRadius min={GameSetupData.lakeMinRadiusTiles} max={GameSetupData.lakeMaxRadiusTiles} minDistanceFromCoast={GameSetupData.lakeMinDistanceFromCoast}");
            Debug.Log($"[Setup] mapSize={GameSetupData.mapSize} contCount={GameSetupData.numberOfContinents} islandCount={GameSetupData.numberOfIslands} generateIslands={GameSetupData.generateIslands}");
            Debug.Log($"[PlanetGenerator][Diag] numberOfContinents={numberOfContinents} continentTiles(WxH) min={cMinW}x{cMinH} max={cMaxW}x{cMaxH}");
            Debug.Log($"[PlanetGenerator][Diag] generateIslands={generateIslands} numberOfIslands={numberOfIslands}");
            Debug.Log($"[PlanetGenerator][Diag] latitudeInfluence={latitudeInfluence} latitudeExponent={latitudeExponent} temperatureBias={temperatureBias} moistureBias={moistureBias}");
        }
        
        int tilesX = grid.Width;
        int tilesZ = grid.Height;
        // ---------- 2. Generate Deterministic Continent Seeds with Per-Continent Sizes ------------------
        List<ContinentData> continentDataList = GenerateContinentData(numberOfContinents, seed ^ 0xD00D, tilesX, tilesZ);
        continents = continentDataList;

        if (loadingPanelController != null)
        {
            loadingPanelController.SetProgress(0.05f);
            loadingPanelController.SetStatus("Stamping continents...");
        }
        yield return null;

        Vector2Int[] tileCoords = new Vector2Int[tileCount];
        for (int i = 0; i < tileCount; i++) {
            tileCoords[i] = new Vector2Int(i % tilesX, i / tilesX);
        }

        bool[] isLandTile = new bool[tileCount];
        bool[] isLakeTile = new bool[tileCount];
        bool[] isRiverTile = new bool[tileCount];

        int WrappedDelta(int a, int b, int width) {
            int delta = a - b;
            if (Mathf.Abs(delta) > width / 2) {
                delta = delta > 0 ? delta - width : delta + width;
            }
            return delta;
        }

        void StampEllipse(ContinentData continent) {
            float halfW = Mathf.Max(0.5f, continent.widthTiles * 0.5f);
            float halfH = Mathf.Max(0.5f, continent.heightTiles * 0.5f);
            for (int i = 0; i < tileCount; i++) {
                Vector2Int coord = tileCoords[i];
                float dx = WrappedDelta(coord.x, continent.center.x, tilesX) / halfW;
                float dy = (coord.y - continent.center.y) / halfH;
                if ((dx * dx + dy * dy) <= 1f) {
                    isLandTile[i] = true;
                }
            }
        }

        void StampCircle(Vector2Int center, int radius, bool makeLand, bool makeLake) {
            int maxRadius = Mathf.Max(0, radius);
            for (int i = 0; i < tileCount; i++) {
                int dist = HexDistanceWrapped(tileCoords[i], center, tilesX);
                if (dist <= maxRadius) {
                    if (makeLand) {
                        isLandTile[i] = true;
                    }
                    if (makeLake) {
                        isLandTile[i] = false;
                        isLakeTile[i] = true;
                    }
                }
            }
        }

        foreach (var continent in continentDataList) {
            StampEllipse(continent);
        }

        Debug.Log($"[StampGen] Continents stamped: {continentDataList.Count}");

        // ---------- 2.5. Generate Islands (Stamping) ---------
        int islandsStamped = 0;
        if (allowIslands && generateIslands && numberOfIslands > 0)
        {
            int islandMinRadius = Mathf.Max(1, GameSetupData.islandMinRadiusTiles);
            int islandMaxRadius = Mathf.Max(islandMinRadius, GameSetupData.islandMaxRadiusTiles);
            int islandMinDistance = Mathf.Max(0, GameSetupData.islandMinDistanceFromContinents);
            System.Random islandRand = new System.Random(seed ^ 0xF15);
            int attempts = 0;
            int maxAttempts = numberOfIslands * 50;

            while (islandsStamped < numberOfIslands && attempts < maxAttempts)
            {
                attempts++;
                int idx = islandRand.Next(0, tileCount);
                if (isLandTile[idx]) continue;
                if (HasLandWithinDistance(idx, islandMinDistance, isLandTile)) continue;

                int radius = islandRand.Next(islandMinRadius, islandMaxRadius + 1);
                StampCircle(tileCoords[idx], radius, true, false);
                islandsStamped++;
            }

            if (islandsStamped < numberOfIslands)
            {
                while (islandsStamped < numberOfIslands && attempts < maxAttempts * 2)
                {
                    attempts++;
                    int idx = islandRand.Next(0, tileCount);
                    if (isLandTile[idx]) continue;
                    int radius = islandRand.Next(islandMinRadius, islandMaxRadius + 1);
                    StampCircle(tileCoords[idx], radius, true, false);
                    islandsStamped++;
                }
            }
        }

        Debug.Log($"[StampGen] Islands stamped: {islandsStamped}");

        // ---------- 3. Generate Lakes (Stamping) ----------
        bool isEarthPlanet = !isMarsWorldType && !isVenusWorldType && !isMercuryWorldType && !isJupiterWorldType &&
                            !isSaturnWorldType && !isUranusWorldType && !isNeptuneWorldType && !isPlutoWorldType &&
                            !isTitanWorldType && !isEuropaWorldType && !isIoWorldType && !isGanymedeWorldType &&
                            !isCallistoWorldType && !isLunaWorldType;

        int lakesStamped = 0;
        List<Vector2Int> lakeCenters = new List<Vector2Int>();
        if (enableLakes && isEarthPlanet && numberOfLakes > 0)
        {
            int lakeMinRadius = Mathf.Max(1, GameSetupData.lakeMinRadiusTiles);
            int lakeMaxRadius = Mathf.Max(lakeMinRadius, GameSetupData.lakeMaxRadiusTiles);
            int lakeMinDistanceFromCoast = Mathf.Max(0, GameSetupData.lakeMinDistanceFromCoast);

            List<int> coastTiles = new List<int>();
            for (int i = 0; i < tileCount; i++) {
                if (!isLandTile[i]) continue;
                bool adjacentToOcean = false;
                foreach (int neighbor in grid.neighbors[i]) {
                    if (!isLandTile[neighbor]) {
                        adjacentToOcean = true;
                        break;
                    }
                }
                if (adjacentToOcean) coastTiles.Add(i);
            }

            List<int> candidateCenters = new List<int>();
            if (coastTiles.Count == 0) {
                for (int i = 0; i < tileCount; i++) {
                    if (isLandTile[i]) candidateCenters.Add(i);
                }
            } else {
                int[] distanceFromCoast = BuildDistanceMap(coastTiles);
                for (int i = 0; i < tileCount; i++) {
                    if (!isLandTile[i]) continue;
                    if (distanceFromCoast[i] >= lakeMinDistanceFromCoast) {
                        candidateCenters.Add(i);
                    }
                }
            }

            System.Random lakeRand = new System.Random(unchecked((int)(seed ^ 0x1A4E)));
            int attempts = 0;
            int maxAttempts = numberOfLakes * 50;
            int minLakeTiles = 1 + 3 * lakeMinRadius * (lakeMinRadius + 1);
            int maxLakeTiles = 1 + 3 * lakeMaxRadius * (lakeMaxRadius + 1);

            while (lakesStamped < numberOfLakes && attempts < maxAttempts)
            {
                attempts++;
                if (candidateCenters.Count == 0) break;

                int pickIndex = lakeRand.Next(candidateCenters.Count);
                int centerIdx = candidateCenters[pickIndex];
                candidateCenters.RemoveAt(pickIndex);
                if (!isLandTile[centerIdx]) continue;

                int radius = lakeRand.Next(lakeMinRadius, lakeMaxRadius + 1);
                List<int> lakeTiles = new List<int>();
                for (int i = 0; i < tileCount; i++) {
                    int dist = HexDistanceWrapped(tileCoords[i], tileCoords[centerIdx], tilesX);
                    if (dist <= radius && isLandTile[i]) {
                        lakeTiles.Add(i);
                    }
                }

                if (lakeTiles.Count < minLakeTiles || lakeTiles.Count > maxLakeTiles) continue;

                foreach (int tileIdx in lakeTiles) {
                    isLandTile[tileIdx] = false;
                    isLakeTile[tileIdx] = true;
                }
                lakeCenters.Add(tileCoords[centerIdx]);
                lakesStamped++;
            }

            if (lakesStamped < numberOfLakes)
            {
                List<int> fallbackCenters = new List<int>();
                for (int i = 0; i < tileCount; i++) {
                    if (isLandTile[i]) fallbackCenters.Add(i);
                }
                while (lakesStamped < numberOfLakes && fallbackCenters.Count > 0)
                {
                    int pickIndex = lakeRand.Next(fallbackCenters.Count);
                    int centerIdx = fallbackCenters[pickIndex];
                    fallbackCenters.RemoveAt(pickIndex);
                    if (!isLandTile[centerIdx]) continue;

                    int radius = lakeRand.Next(lakeMinRadius, lakeMaxRadius + 1);
                    List<int> lakeTiles = new List<int>();
                    for (int i = 0; i < tileCount; i++) {
                        int dist = HexDistanceWrapped(tileCoords[i], tileCoords[centerIdx], tilesX);
                        if (dist <= radius && isLandTile[i]) {
                            lakeTiles.Add(i);
                        }
                    }

                    if (lakeTiles.Count < minLakeTiles) continue;
                    foreach (int tileIdx in lakeTiles) {
                        isLandTile[tileIdx] = false;
                        isLakeTile[tileIdx] = true;
                    }
                    lakeCenters.Add(tileCoords[centerIdx]);
                    lakesStamped++;
                }
            }
        }

        if (enableRivers && GameSetupData.riverCount > 0 && enableLakes && isEarthPlanet && lakeCenters.Count == 0)
        {
            int fallbackRadius = Mathf.Max(1, GameSetupData.lakeMinRadiusTiles);
            for (int i = 0; i < tileCount; i++)
            {
                if (!isLandTile[i]) continue;
                StampCircle(tileCoords[i], fallbackRadius, false, true);
                lakeCenters.Add(tileCoords[i]);
                lakesStamped++;
                Debug.LogWarning("[StampGen] Forced a fallback lake to seed rivers.");
                break;
            }
        }

        Debug.Log($"[StampGen] Lakes stamped: {lakesStamped}");

        landTilesGenerated = 0;
        for (int i = 0; i < tileCount; i++) {
            if (isLandTile[i]) landTilesGenerated++;
        }
        Debug.Log($"[StampGen] Total land tiles: {landTilesGenerated}");

        // ---------- 5. Calculate Biomes, Elevation, and Initial Data ---------
        if (!allowOceans)
        {
            for (int i = 0; i < tileCount; i++) {
                isLandTile[i] = true;
                isLakeTile[i] = false;
            }
        }

        // Track climate ranges for diagnostics
        float temperatureMin = 1f, temperatureMax = 0f;
        float moistureMin = 1f, moistureMax = 0f;
        
        // Track land elevation ranges for render normalization
        float landElevMin = float.MaxValue;
        float landElevMax = float.MinValue;
        List<int> landTileIndices = new List<int>();

        // Sample a few representative tiles for detailed climate logs (avoid spam)
        List<int> climateSampleIndices = new List<int>();
        if (tileCount > 0) climateSampleIndices.Add(0);
        if (tileCount > 4) climateSampleIndices.Add(tileCount / 4);
        if (tileCount > 2) climateSampleIndices.Add(tileCount / 2);
        if (tileCount > 4) climateSampleIndices.Add((3 * tileCount) / 4);
        if (tileCount > 1) climateSampleIndices.Add(tileCount - 1);
        
        // Calculate noise frequencies for elevation
        float elevBroadFreq = 1f / (mapWidth * 0.5f);
        float elevRidgedFreq = 1f / (mapWidth * 0.3f);
        float elevBillowFreq = 1f / (mapWidth * 0.4f);

        for (int i = 0; i < tileCount; i++)
        {
            Vector2Int coord = tileCoords[i];
            bool isLand = isLandTile[i];
            Vector2 tilePos = new Vector2(coord.x, coord.y);
            Vector3 noisePoint = new Vector3(coord.x, 0f, coord.y) + noiseOffset;
            
            // Calculate elevation using advanced multi-noise blending (seamless wrap)
            // Combines: FBm (base), Ridged (mountains), Billow (hills), Voronoi (variation)
            float noiseElevation;
            // Only enable the Voronoi path if the prefab flag is set. Billow hills may still trigger advanced elevation.
            if (billowHillWeight > 0.01f) {
                noiseElevation = noise.GetAdvancedElevationPeriodic(tilePos, mapWidth, mapHeight,
                    elevBroadFreq, elevRidgedFreq, elevBillowFreq,
                    ridgedMountainWeight, billowHillWeight);
            } else {
                noiseElevation = noise.GetElevationPeriodic(tilePos, mapWidth, mapHeight,
                    elevBroadFreq, elevRidgedFreq, ridgedMountainWeight);
            }

            // Calculate Moisture & Temperature (needed for biome determination)
            float moisture = noise.GetMoisture(noisePoint * moistureFreq);
            // Apply moisture bias - clamp to ensure it stays in 0-1 range
            moisture = Mathf.Clamp01(moisture + moistureBias);
            
            float northness = mapHeight > 1f
                ? Mathf.Lerp(-1f, 1f, coord.y / Mathf.Max(1f, mapHeight - 1f))
                : 0f;
            float absNorthness = Mathf.Abs(northness);
            
            // Generate temperature from noise and north/south position
            // Latitude-derived temperature: use exponent to control steepness and a blend weight
            float northTemp = 1f - Mathf.Pow(absNorthness, latitudeExponent);
            float noiseTemp = noise.GetTemperatureFromNoise(noisePoint);

            // Blend latitude vs noise using configurable weight
            float temperature = (northTemp * latitudeInfluence) + (noiseTemp * (1f - latitudeInfluence));
            temperature = Mathf.Clamp01(temperature + temperatureBias);
            // Track ranges
            if (temperature < temperatureMin) temperatureMin = temperature;
            if (temperature > temperatureMax) temperatureMax = temperature;
            if (moisture < moistureMin) moistureMin = moisture;
            if (moisture > moistureMax) moistureMax = moisture;

            // Detailed sample logs for a handful of tiles
            if (climateSampleIndices.Contains(i))
            {
}

            Biome biome;
            bool isHill = false;
            float finalElevation; // Variable to store the final elevation
            bool isLake = isLakeTile[i];

            if (isLake) {
                finalElevation = lakeElevation;
                biome = Biome.Lake;
            } else if (isLand) {
                // Calculate land elevation: base + scaled noise
                // Now uses full range since maxTotalElevation defaults to 1.0
                float elevationRange = maxTotalElevation - baseLandElevation;
                finalElevation = baseLandElevation + (noiseElevation * elevationRange);
                
                // Track land elevation range for later normalization
                if (finalElevation < landElevMin) landElevMin = finalElevation;
                if (finalElevation > landElevMax) landElevMax = finalElevation;
                landTileIndices.Add(i);

                biome = GetBiomeForTile(i, true, temperature, moisture);

                // Mountain/Hill check, but protect polar biomes from being overridden
                if (finalElevation > mountainThreshold) {
                    if (biome != Biome.Glacier && biome != Biome.Arctic && biome != Biome.Frozen)
                    {
                        biome = Biome.Mountain;
                    }
                } else if (finalElevation > hillThreshold) { 
                    isHill = true;
                    // Add hill elevation boost
                    finalElevation += hillElevationBoost;
                }
            } else { // Water Biomes
                 finalElevation = 0f; // Water elevation is 0
                 biome = GetBiomeForTile(i, false, temperature, moisture);
            }

            // *** Override for Glaciers: Treat them like land for elevation ***
            if (biome == Biome.Glacier) {
                float elevationRange = maxTotalElevation - baseLandElevation;
                finalElevation = baseLandElevation + (noiseElevation * elevationRange);
                // Track glacier elevation too
                if (finalElevation < landElevMin) landElevMin = finalElevation;
                if (finalElevation > landElevMax) landElevMax = finalElevation;
                if (!landTileIndices.Contains(i)) landTileIndices.Add(i);
            }

            // Cap as safety net
            finalElevation = Mathf.Min(finalElevation, maxTotalElevation);

            // Store the final calculated elevation
            tileElevation[i] = finalElevation; 

            // Create HexTileData
            var y = BiomeHelper.Yields(biome);
            int moveCost = BiomeHelper.GetMovementCost(biome);
            ElevationTier elevTier = ElevationTier.Flat;
            if (finalElevation > mountainThreshold) elevTier = ElevationTier.Mountain;
            else if (finalElevation > hillThreshold) elevTier = ElevationTier.Hill;

            var td = new HexTileData {
                biome = biome,
                food = y.food, production = y.prod, gold = y.gold, science = y.sci, culture = y.cult,
                occupantId = 0,
                isLand = isLand,
                isLake = isLake,
                isRiver = isRiverTile[i],
                isHill = isHill,
                elevation = finalElevation,
                renderElevation = 0f, // Will be computed after all tiles are processed
                elevationTier = elevTier,
                temperature = temperature,
                moisture = moisture,
                movementCost = moveCost,
                isPassable = true,
                isMoonTile = false
            };
            data[i] = td;
            baseData[i] = td;

            if (climateSampleIndices.Contains(i))
            {
}

            // BATCH YIELD
            if (i > 0 && i % 250 == 0)
            {
                if (loadingPanelController != null)
                {
                    loadingPanelController.SetProgress(0.3f + (float)i / tileCount * 0.2f); // Progress 30% to 50%
                    loadingPanelController.SetStatus("Defining biomes and elevation...");
                }
                yield return null;
            }
        }

        bool noiseDidNotChangeLand = true;
        for (int i = 0; i < tileCount; i++)
        {
            if (!data.ContainsKey(i)) continue;
            if (data[i].isLand != isLandTile[i])
            {
                noiseDidNotChangeLand = false;
                break;
            }
        }
        Debug.Assert(noiseDidNotChangeLand, "[StampGen] noiseDidNotChangeLand assertion failed.");

        // Log climate variability after biome assignment loop
// Log top biome counts as a quick distribution check
        var biomeCounts = new Dictionary<Biome, int>();
        for (int i = 0; i < tileCount; i++)
        {
            if (!data.ContainsKey(i)) continue;
            Biome b = data[i].biome;
            if (!biomeCounts.ContainsKey(b)) biomeCounts[b] = 0;
            biomeCounts[b]++;
        }
        var ordered = biomeCounts.OrderByDescending(kv => kv.Value).Take(8).ToList();
        foreach (var kv in ordered)
        {
}

        // ---------- 5.5. Compute Render Elevation (Normalized for Heightmap) ----------
        // This normalizes land elevation to use the full 0-1 range for heightmap/displacement
        // Water stays at their fixed values, land spans most of 0-1
        Debug.Log($"[PlanetGenerator] Land elevation range before normalization: {landElevMin:F4} to {landElevMax:F4}");
        
        float landElevRange = landElevMax - landElevMin;
        if (landElevRange < 0.001f) landElevRange = 1f; // Prevent division by zero
        
        for (int i = 0; i < tileCount; i++)
        {
            if (!data.ContainsKey(i)) continue;
            var td = data[i];
            
            if (td.isLand || td.biome == Biome.Glacier)
            {
                // Normalize land elevation to use range ~0.1 to 0.95 (leave room for water)
                float normalizedElev = Mathf.InverseLerp(landElevMin, landElevMax, td.elevation);
                td.renderElevation = 0.1f + normalizedElev * 0.85f;
            }
            else if (td.biome == Biome.Coast)
            {
                td.renderElevation = 0.08f; // Just above water
            }
            else if (td.biome == Biome.Lake)
            {
                td.renderElevation = lakeRenderElevation;
            }
            else if (td.biome == Biome.Seas)
            {
                td.renderElevation = 0.03f;
            }
            else // Ocean
            {
                td.renderElevation = 0f;
            }
            
            data[i] = td;
            baseData[i] = td;
        }
        
        Debug.Log($"[PlanetGenerator] Render elevation range: ocean=0, seas=0.03, coast=0.08, land=0.1-0.95");

        // ---------- 6. Post-processing (Coasts, Seas, Visuals) --------------
        // Create coast tiles first where land meets water (excluding glaciers and rivers)
        HashSet<int> waterTiles = new HashSet<int>();
        // Make a set of protected biomes that can't be modified by coastline/seas processing
        HashSet<int> postProcessProtectedTiles = new HashSet<int>();

        for (int i = 0; i < tileCount; i++) {
            if (!data.ContainsKey(i)) continue;

            // Protect Snow and Glacier tiles from ever becoming a coast or sea
            if (data[i].biome == Biome.Snow || data[i].biome == Biome.Glacier) {
                postProcessProtectedTiles.Add(i);
                continue;
            }

            if (data[i].isLake) {
                continue;
            }

            // Consider tiles that are NOT land and NOT lakes as ocean water bodies (Glaciers are now treated as land)
            if (!data[i].isLand && !data[i].isLake) {
                 waterTiles.Add(i);
                 continue; 
            }

            bool hasWaterNeighbor = false;
            foreach (int nIdx in grid.neighbors[i]) {
                // A neighbor is water if it's in the waterTiles set OR it's an ocean/sea/glacier
                if (waterTiles.Contains(nIdx) || (data.ContainsKey(nIdx) && !data[nIdx].isLand && !data[nIdx].isLake)) {
                    hasWaterNeighbor = true; break;
                }
            }
            // Convert land tile to Coast if adjacent to Ocean/Seas (but NEVER Snow or Mountains)
            if (hasWaterNeighbor && !postProcessProtectedTiles.Contains(i) && data[i].biome != Biome.Mountain) {
                var td = data[i];
                td.biome = Biome.Coast;
                td.isLand = true; // Coast is technically land
                data[i] = td;
                baseData[i] = td;
            }

            // BATCH YIELD
            if (i > 0 && i % 500 == 0)
            {
                if (loadingPanelController != null)
                {
                    loadingPanelController.SetProgress(0.5f + (float)i / tileCount * 0.1f); // Progress 50% to 60%
                    loadingPanelController.SetStatus("Forming coastlines...");
                }
                yield return null;
            }
        }

        // Identify all coast tiles after the first pass
        HashSet<int> coastTiles = new HashSet<int>();
        for (int i = 0; i < tileCount; i++) {
            if (data.ContainsKey(i) && data[i].biome == Biome.Coast) coastTiles.Add(i);
            
            // BATCH YIELD
            if (i > 0 && i % 1000 == 0) // Larger batch size for this simple operation
            {
                if (loadingPanelController != null)
                {
                    loadingPanelController.SetProgress(0.6f + (float)i / tileCount * 0.05f); // Progress 60% to 65%
                    loadingPanelController.SetStatus("Identifying coastlines...");
                }
                yield return null;
            }
        }

        // Convert Ocean tiles near Coast tiles into Seas
        for (int i = 0; i < tileCount; i++) {
            if (!data.ContainsKey(i)) continue;

            // BIOME PROTECTION: Never modify Snow or Glacier
            if (postProcessProtectedTiles.Contains(i)) continue;

            Biome currentBiome = data[i].biome;

            if (currentBiome == Biome.Ocean) { // Only process Ocean tiles
                bool nearCoast = false;
                foreach (int nIdx in grid.neighbors[i]) {
                    if (coastTiles.Contains(nIdx)) {
                        nearCoast = true;
                        break;
                    }
                }

                if (nearCoast) {
                    var td = data[i];
                    td.biome = Biome.Seas;
                    data[i] = td;
                    baseData[i] = td;
                }
            }

            // BATCH YIELD
            if (i > 0 && i % 500 == 0)
            {
                if (loadingPanelController != null)
                {
                    loadingPanelController.SetProgress(0.65f + (float)i / tileCount * 0.05f); // Progress 65% to 70%
                    loadingPanelController.SetStatus("Defining shallow seas...");
                }
                yield return null;
            }
        }

        // ---------- 6.1 Set Fixed Coast Elevation (AFTER Coasts/Seas are determined) ----------
        for (int i = 0; i < tileCount; i++) {
            if (data.ContainsKey(i)) {
                Biome b = data[i].biome;
                if (b == Biome.Ocean) {
                    tileElevation[i] = oceanElevation;
                    var td = data[i]; td.elevation = oceanElevation; data[i] = td; baseData[i] = td;
                } else if (b == Biome.Seas) {
                    tileElevation[i] = seasElevation;
                    var td = data[i]; td.elevation = seasElevation; data[i] = td; baseData[i] = td;
                } else if (b == Biome.Coast) {
                    tileElevation[i] = coastElevation;
                    var td = data[i]; td.elevation = coastElevation; data[i] = td; baseData[i] = td;
                }
            }

            // BATCH YIELD
            if (i > 0 && i % 1000 == 0)
            {
                if (loadingPanelController != null)
                {
                    loadingPanelController.SetProgress(0.70f + (float)i / tileCount * 0.05f); // Progress 70% to 75%
                    loadingPanelController.SetStatus("Setting coastline elevation...");
                }
                yield return null;
            }
        }

        // ---------- 6.5 River Generation Pass (after coasts are defined) ----
        if (enableRivers && GameSetupData.riverCount > 0)
            yield return StartCoroutine(GenerateRivers(isLandTile, data, lakeCenters));

        if (loadingPanelController != null)
        {
            loadingPanelController.SetProgress(0.95f);
            loadingPanelController.SetStatus("Finalizing terrain...");
        }
        yield return null;

        // --- Visual Generation ---
        // NOTE: Tile prefab spawning is disabled. The new system uses texture-based rendering.
        // FlatMapTextureRenderer handles visualization.
        // Tile data is still generated and stored - only visualization changed.

        // Finalize
        HasGeneratedSurface = true;
        Tiles = data.Values.ToList();
        
        // DIAGNOSTIC: Log elevation statistics
        LogElevationDiagnostics(data);
        
        // Notify listeners that surface is ready for rendering
        try { OnSurfaceGenerated?.Invoke(); } catch (System.Exception ex) { Debug.LogError($"[PlanetGenerator] OnSurfaceGenerated invocation error: {ex.Message}"); }
        

        // --------------------------- River Generation ----------------------------
        IEnumerator GenerateRivers(bool[] isLandTile, Dictionary<int, HexTileData> tileData, List<Vector2Int> lakeCenters)
        {
            if (lakeCenters == null || lakeCenters.Count == 0) {
                Debug.LogWarning("[StampGen] No lakes available for river sources.");
                yield break;
            }

            HashSet<int> coastTiles = new HashSet<int>();
            foreach (var kvp in tileData) {
                if (kvp.Value.biome == Biome.Coast) {
                    coastTiles.Add(kvp.Key);
                }
            }

            if (coastTiles.Count == 0) {
                Debug.LogWarning("[StampGen] No coast tiles found for river endpoints.");
                yield break;
            }

            int targetRiverCount = Mathf.Clamp(GameSetupData.riverCount, 0, 200);
            System.Random riverRand = new System.Random(unchecked((int)(seed ^ 0xBADF00D)));
            HashSet<int> riverTiles = new HashSet<int>();

            List<int> lakeIndices = new List<int>();
            foreach (var center in lakeCenters) {
                int idx = center.y * grid.Width + center.x;
                if (idx >= 0 && idx < tileData.Count) lakeIndices.Add(idx);
            }

            if (lakeIndices.Count == 0) {
                Debug.LogWarning("[StampGen] Lake centers were invalid for river generation.");
                yield break;
            }

            lakeIndices = lakeIndices.OrderBy(_ => riverRand.Next()).ToList();

            int riversGenerated = 0;
            int attempts = 0;
            int maxAttempts = Mathf.Max(targetRiverCount * 5, 20);

            while (riversGenerated < targetRiverCount && attempts < maxAttempts)
            {
                int lakeIndex = lakeIndices[attempts % lakeIndices.Count];
                attempts++;

                List<int> path = FindRiverPath(lakeIndex, coastTiles, tileData, riverRand);
                if (path == null || path.Count < minRiverLength || path.Count > maxRiverPathLength) {
                    continue;
                }

                riversGenerated++;
                foreach (int tileIdx in path) {
                    if (!tileData.ContainsKey(tileIdx)) continue;
                    var td = tileData[tileIdx];

                    if (td.isLake) continue;
                    if (coastTiles.Contains(tileIdx)) continue;

                    td.biome = Biome.River;
                    td.isLand = true;
                    td.isLake = false;
                    td.isRiver = true;
                    td.isHill = false;
                    td.elevation = Mathf.Max(0f, td.elevation - riverDepth);
                    td.renderElevation = Mathf.Max(0f, td.renderElevation - riverDepth);
                    tileElevation[tileIdx] = td.elevation;
                    tileData[tileIdx] = td;
                    baseData[tileIdx] = td;
                    riverTiles.Add(tileIdx);
                    isLandTile[tileIdx] = true;
                    isRiverTile[tileIdx] = true;
                }

                if (loadingPanelController != null && riversGenerated % 5 == 0)
                {
                    loadingPanelController.SetProgress(0.75f + (float)riversGenerated / Mathf.Max(1, targetRiverCount) * 0.2f);
                    loadingPanelController.SetStatus($"Carving rivers... ({riversGenerated}/{targetRiverCount})");
                }
                yield return null;
            }

            Debug.Log($"[StampGen] Rivers generated: {riversGenerated}");
        }

        List<int> FindRiverPath(int start, HashSet<int> coastTiles, Dictionary<int, HexTileData> tileData, System.Random rand)
        {
            var openSet = new SortedDictionary<float, Queue<int>>();
            var cameFrom = new Dictionary<int, int>();
            var costSoFar = new Dictionary<int, float>();
            var steps = new Dictionary<int, int>();

            costSoFar[start] = 0f;
            steps[start] = 0;
            AddToOpenSet(openSet, 0f, start);

            while (openSet.Count > 0)
            {
                int current = PopFromOpenSet(openSet);
                if (coastTiles.Contains(current))
                {
                    return ReconstructPath(cameFrom, current);
                }

                int currentSteps = steps[current];
                if (currentSteps >= maxRiverPathLength) continue;

                foreach (int neighbor in grid.neighbors[current])
                {
                    if (!tileData.ContainsKey(neighbor)) continue;
                    var td = tileData[neighbor];

                    if (td.biome == Biome.Ocean || td.biome == Biome.Seas || td.biome == Biome.Lake) continue;
                    if (td.biome == Biome.Mountain || td.biome == Biome.Glacier || td.biome == Biome.Snow) continue;
                    if (!td.isLand && td.biome != Biome.Coast) continue;

                    float stepCost = 1f + (float)rand.NextDouble() * 0.35f;
                    if (td.isRiver) stepCost *= 0.7f;

                    float newCost = costSoFar[current] + stepCost;
                    if (!costSoFar.ContainsKey(neighbor) || newCost < costSoFar[neighbor])
                    {
                        costSoFar[neighbor] = newCost;
                        cameFrom[neighbor] = current;
                        steps[neighbor] = currentSteps + 1;
                        AddToOpenSet(openSet, newCost, neighbor);
                    }
                }
            }
            return null;
        }

        void AddToOpenSet(SortedDictionary<float, Queue<int>> openSet, float priority, int node)
        {
            if (!openSet.TryGetValue(priority, out var queue))
            {
                queue = new Queue<int>();
                openSet[priority] = queue;
            }
            queue.Enqueue(node);
        }

        int PopFromOpenSet(SortedDictionary<float, Queue<int>> openSet)
        {
            var first = openSet.First();
            int node = first.Value.Dequeue();
            if (first.Value.Count == 0) openSet.Remove(first.Key);
            return node;
        }

        List<int> ReconstructPath(Dictionary<int, int> cameFrom, int current)
        {
            List<int> path = new List<int>();
            while (cameFrom.TryGetValue(current, out int prev))
            {
                path.Add(current);
                current = prev;
            }
            path.Reverse();
            return path;
        }

        // --------------------------- Helper Functions ----------------------------

        // GenerateSurface continues...
    } // End of GenerateSurface()

    // --------------------------- API for other systems -----------------------
    public Biome GetBaseBiome(int tileIndex) =>
        baseData.TryGetValue(tileIndex, out HexTileData td) ? td.biome : Biome.Ocean;
        
    public bool IsTileHill(int tileIndex) =>
        data.TryGetValue(tileIndex, out HexTileData td) ? td.isHill : false;
        
    public float GetTileElevation(int tileIndex) =>
        tileElevation.TryGetValue(tileIndex, out float elev) ? elev : 0f;

    // --- NEW: Getter for full HexTileData ---
    public HexTileData GetHexTileData(int tileIndex) {
        data.TryGetValue(tileIndex, out HexTileData td);
        return td; // Will be null if tile not found
    }
    
    // --- NEW: Setter for HexTileData ---
    public void SetHexTileData(int tileIndex, HexTileData td) {
        if (!data.ContainsKey(tileIndex)) return;
        data[tileIndex] = td;
        // baseData may also want updating if you allow undoing.
        baseData[tileIndex] = td;
    }
    // ----------------------------------------

    public void SetTileOccupant(int tileIndex, GameObject occupant) {
        if (!data.ContainsKey(tileIndex)) return;
        HexTileData td = data[tileIndex]; td.occupantId = occupant ? occupant.GetInstanceID() : 0; data[tileIndex] = td;
        // baseData may also want updating if you allow undoing.
        baseData[tileIndex] = td;
    }
    public void SetTileBiome(int tileIndex, Biome newBiome) {
        if (!data.ContainsKey(tileIndex)) return;
        HexTileData td = data[tileIndex]; 
        td.biome = newBiome;
        // Update yields based on new biome
        var y = BiomeHelper.Yields(newBiome);
        td.food = y.food; td.production = y.prod; td.gold = y.gold; td.science = y.sci; td.culture = y.cult;
        // Update land status based on biome (rivers are treated as land, lakes as water)
        td.isLake = newBiome == Biome.Lake;
        td.isRiver = newBiome == Biome.River;
        td.isLand = (newBiome != Biome.Ocean && newBiome != Biome.Seas && newBiome != Biome.Coast && newBiome != Biome.Lake && newBiome != Biome.Glacier);
        if (td.isRiver) td.isLand = true;
        td.isHill = false; // Setting biome usually overrides hill status unless specifically handled
        data[tileIndex] = td;
        
        // Visuals are updated via SGT textures, no direct color/texture setting needed here.
    }
    public void RestoreTileToBase(int tileIndex) {
        if (!baseData.ContainsKey(tileIndex)) return;
        // Restore biome and hill status from base data
        HexTileData baseTd = baseData[tileIndex];
        SetTileBiome(tileIndex, baseTd.biome);
        // Explicitly set hill status after setting biome
        if (data.ContainsKey(tileIndex)) {
            HexTileData currentTd = data[tileIndex];
            currentTd.isHill = baseTd.isHill;
            data[tileIndex] = currentTd;
        }
    }

    // Method to set the current map type and update flags
    public void SetMapTypeName(string mapTypeName)
    {
        currentMapTypeName = mapTypeName;
        isRainforestMapType = mapTypeName.Contains("Rainforest");
        isScorchedMapType = mapTypeName.Contains("Scorched");
        isInfernalMapType = mapTypeName.Contains("Infernal");
        isDemonicMapType = mapTypeName.Contains("Demonic");
        isIceWorldMapType = mapTypeName.Contains("Frozen") || mapTypeName.Contains("Arctic") || mapTypeName.Contains("Glacial")|| mapTypeName.Contains("Ice");
        isMonsoonMapType = mapTypeName.Contains("Monsoon") || mapTypeName.Contains("Floodlands");
    }

    private Biome GetBiomeForTile(int tileIndex, bool isLand, float temperature, float moisture)
    {
        // Calculate north/south and east/west normalized positions using the generator's grid
        float northSouth = 0f;
        float eastWest = 0f;
        if (grid != null && grid.IsBuilt && tileIndex >= 0 && tileIndex < grid.TileCount)
        {
            int row = tileIndex / Mathf.Max(1, grid.Width);
            int col = tileIndex % Mathf.Max(1, grid.Width);
            float mapW = Mathf.Max(1f, grid.Width - 1f);
            float mapH = Mathf.Max(1f, grid.Height - 1f);
            northSouth = Mathf.Lerp(-1f, 1f, row / mapH);
            eastWest = Mathf.Lerp(-1f, 1f, col / mapW);
        }
        
        // NOTE: Removed Earth-only polar override so polar biomes are determined by temperature and
        // latitude influence settings in `PlanetGenerator`. This avoids forced asymmetries and
        // lets `BiomeHelper` decide biomes based on passed temperature/moisture values.

        Biome assignedBiome = BiomeHelper.GetBiome(
            isLand, temperature, moisture,
            isRainforestMapType, isScorchedMapType, isInfernalMapType, isDemonicMapType,
            isIceWorldMapType, isMonsoonMapType,
            isMarsWorldType, isVenusWorldType, isMercuryWorldType, isJupiterWorldType,
            isSaturnWorldType, isUranusWorldType, isNeptuneWorldType, isPlutoWorldType,
            isTitanWorldType, isEuropaWorldType, isIoWorldType, isGanymedeWorldType,
            isCallistoWorldType, isLunaWorldType, northSouth, eastWest
        );
        
        // Validate and log inappropriate biome assignments
        return BiomeHelper.ValidateAndLogBiome(assignedBiome, 
            isMarsWorldType, isVenusWorldType, isMercuryWorldType, isJupiterWorldType,
            isSaturnWorldType, isUranusWorldType, isNeptuneWorldType, isPlutoWorldType,
            isTitanWorldType, isEuropaWorldType, isIoWorldType, isGanymedeWorldType,
            isCallistoWorldType, isLunaWorldType);
    }

    public void SetLoadingPanel(LoadingPanelController controller)
    {
        loadingPanelController = controller;
    }
    public LoadingPanelController GetLoadingPanel() => loadingPanelController;



    /// <summary>
    /// Generates optimized biome mask textures with improved quality and performance
    /// </summary>

    // --- Per-Continent Data Structure ---
    /// <summary>
    /// Holds per-continent data for varied size/rotation per continent
    /// </summary>
    private struct ContinentData {
        public string name;           // Debug name
        public Vector2Int center;     // Tile-space center (col,row)
        public int widthTiles;        // Width in tiles
        public int heightTiles;       // Height in tiles
    }

    // --- Helper methods moved to class scope ---
    /// <summary>
    /// Generate continent seeds with per-continent randomized sizes and rotations.
    /// Returns both positions and per-continent size data.
    /// </summary>
    private List<ContinentData> GenerateContinentData(int count, int rndSeed, int mapWidthTiles, int mapHeightTiles) {
        var continents = new List<ContinentData>();
        if (count <= 0) return continents;

        System.Random rand = new System.Random(rndSeed);
        int minW = Mathf.Max(1, GameSetupData.continentMinWidthTiles);
        int maxW = Mathf.Max(minW, GameSetupData.continentMaxWidthTiles);
        int minH = Mathf.Max(1, GameSetupData.continentMinHeightTiles);
        int maxH = Mathf.Max(minH, GameSetupData.continentMaxHeightTiles);
        int minDistance = Mathf.Max(0, GameSetupData.continentMinDistanceTiles);
        float connectionChance = Mathf.Clamp01(GameSetupData.continentConnectionChance);
        int maxAttemptsPerContinent = 50;

        int continentIndex = 1;
        for (int i = 0; i < count; i++) {
            Vector2Int center = Vector2Int.zero;
            bool accepted = false;

            for (int attempt = 0; attempt < maxAttemptsPerContinent; attempt++) {
                var candidate = new Vector2Int(rand.Next(0, mapWidthTiles), rand.Next(0, mapHeightTiles));
                bool farEnough = true;
                foreach (var c in continents) {
                    int dist = HexDistanceWrapped(candidate, c.center, mapWidthTiles);
                    if (dist < minDistance) {
                        farEnough = false;
                        if (rand.NextDouble() < connectionChance) {
                            farEnough = true;
                        }
                        break;
                    }
                }
                if (farEnough) {
                    center = candidate;
                    accepted = true;
                    break;
                }
            }

            if (!accepted) {
                center = new Vector2Int(rand.Next(0, mapWidthTiles), rand.Next(0, mapHeightTiles));
            }

            int chosenWidthTiles = rand.Next(minW, maxW + 1);
            int chosenHeightTiles = rand.Next(minH, maxH + 1);

            continents.Add(new ContinentData {
                name = $"Continent {continentIndex++}",
                center = center,
                widthTiles = Mathf.Clamp(chosenWidthTiles, 1, mapWidthTiles),
                heightTiles = Mathf.Clamp(chosenHeightTiles, 1, mapHeightTiles)
            });
        }

        if (enableDiagnostics) {
            for (int i = 0; i < continents.Count; i++) {
                var c = continents[i];
                Debug.Log($"[StampGen][Continent] {c.name} center={c.center} tiles={c.widthTiles}x{c.heightTiles}");
            }
        }

        return continents;
    }
    
    private Vector2Int OffsetToAxial(Vector2Int offset) {
        int row = offset.y;
        int col = offset.x;
        int q = col - ((row & 1) == 0 ? (row / 2) : ((row + 1) / 2));
        int r = row;
        return new Vector2Int(q, r);
    }

    private int HexDistance(Vector2Int a, Vector2Int b) {
        int dx = a.x - b.x;
        int dy = a.y - b.y;
        int dz = -dx - dy;
        return (Mathf.Abs(dx) + Mathf.Abs(dy) + Mathf.Abs(dz)) / 2;
    }

    private int HexDistanceWrapped(Vector2Int aOffset, Vector2Int bOffset, int width) {
        var a = OffsetToAxial(aOffset);
        int best = int.MaxValue;
        int[] offsets = { 0, -width, width };
        foreach (int colOffset in offsets) {
            var bWrapped = new Vector2Int(bOffset.x + colOffset, bOffset.y);
            var b = OffsetToAxial(bWrapped);
            int dist = HexDistance(a, b);
            if (dist < best) best = dist;
        }
        return best;
    }

    private bool HasLandWithinDistance(int startIndex, int maxDistance, bool[] isLandTile) {
        if (maxDistance <= 0) return false;
        Queue<(int idx, int dist)> queue = new Queue<(int, int)>();
        HashSet<int> visited = new HashSet<int>();
        queue.Enqueue((startIndex, 0));
        visited.Add(startIndex);

        while (queue.Count > 0) {
            var (current, dist) = queue.Dequeue();
            if (dist >= maxDistance) continue;
            foreach (int neighbor in grid.neighbors[current]) {
                if (visited.Contains(neighbor)) continue;
                if (isLandTile[neighbor]) return true;
                visited.Add(neighbor);
                queue.Enqueue((neighbor, dist + 1));
            }
        }
        return false;
    }

    private int[] BuildDistanceMap(List<int> sources) {
        int tileCount = grid.TileCount;
        int[] distances = new int[tileCount];
        for (int i = 0; i < tileCount; i++) distances[i] = -1;
        Queue<int> queue = new Queue<int>();
        foreach (int src in sources) {
            distances[src] = 0;
            queue.Enqueue(src);
        }

        while (queue.Count > 0) {
            int current = queue.Dequeue();
            int nextDistance = distances[current] + 1;
            foreach (int neighbor in grid.neighbors[current]) {
                if (distances[neighbor] >= 0) continue;
                distances[neighbor] = nextDistance;
                queue.Enqueue(neighbor);
            }
        }
        return distances;
    }
    
    /// <summary>
    /// DIAGNOSTIC: Log elevation statistics after map generation.
    /// This helps identify why terrain might appear flat.
    /// </summary>
    private void LogElevationDiagnostics(Dictionary<int, HexTileData> tileData)
    {
        if (tileData == null || tileData.Count == 0)
        {
            Debug.LogError("[PlanetGenerator] ELEVATION DIAGNOSTIC: No tile data available!");
            return;
        }
        
        float minElev = float.MaxValue;
        float maxElev = float.MinValue;
        float avgElev = 0f;
        float minRenderElev = float.MaxValue;
        float maxRenderElev = float.MinValue;
        float avgRenderElev = 0f;
        int landCount = 0;
        int hillCount = 0;
        int mountainCount = 0;
        int flatCount = 0;
        int zeroElevCount = 0;
        
        foreach (var kvp in tileData)
        {
            var td = kvp.Value;
            float elev = td.elevation;
            float renderElev = td.renderElevation;
            
            if (elev < minElev) minElev = elev;
            if (elev > maxElev) maxElev = elev;
            avgElev += elev;
            
            if (renderElev < minRenderElev) minRenderElev = renderElev;
            if (renderElev > maxRenderElev) maxRenderElev = renderElev;
            avgRenderElev += renderElev;
            
            if (td.isLand) landCount++;
            if (td.elevationTier == ElevationTier.Hill) hillCount++;
            else if (td.elevationTier == ElevationTier.Mountain) mountainCount++;
            else flatCount++;
            
            if (elev <= 0.001f) zeroElevCount++;
        }
        
        avgElev /= tileData.Count;
        avgRenderElev /= tileData.Count;
        
        Debug.LogError($"[ELEVATION DIAGNOSTIC] ========================================");
        Debug.LogError($"[ELEVATION DIAGNOSTIC] Total Tiles: {tileData.Count}, Land: {landCount}");
        Debug.LogError($"[ELEVATION DIAGNOSTIC] Gameplay Elevation Range: {minElev:F4} to {maxElev:F4} (avg: {avgElev:F4})");
        Debug.LogError($"[ELEVATION DIAGNOSTIC] Render Elevation Range: {minRenderElev:F4} to {maxRenderElev:F4} (avg: {avgRenderElev:F4})");
        Debug.LogError($"[ELEVATION DIAGNOSTIC] Elevation Tiers - Flat: {flatCount}, Hills: {hillCount}, Mountains: {mountainCount}");
        Debug.LogError($"[ELEVATION DIAGNOSTIC] Zero/Near-Zero Elevation Tiles: {zeroElevCount}");
        Debug.LogError($"[ELEVATION DIAGNOSTIC] Settings - baseLandElevation: {baseLandElevation}, maxTotalElevation: {maxTotalElevation}");
        Debug.LogError($"[ELEVATION DIAGNOSTIC] Settings - hillThreshold: {hillThreshold}, mountainThreshold: {mountainThreshold}");
        Debug.LogError($"[ELEVATION DIAGNOSTIC] ========================================");
    }

    private void OnDrawGizmosSelected()
    {
        if (!debugDrawContinents || continents == null)
        {
            return;
        }

        Gizmos.color = Color.magenta;
        foreach (var c in continents)
        {
            int idx = c.center.y * grid.Width + c.center.x;
            if (grid.tileCenters == null || idx < 0 || idx >= grid.tileCenters.Length) continue;
            Vector3 center = new Vector3(grid.tileCenters[idx].x, 0f, grid.tileCenters[idx].z);
            float tileWorldWidth = grid.MapWidth / Mathf.Max(1, grid.Width);
            float tileWorldHeight = grid.MapHeight / Mathf.Max(1, grid.Height);
            Vector3 size = new Vector3(c.widthTiles * tileWorldWidth, 1f, c.heightTiles * tileWorldHeight);
            Gizmos.DrawWireCube(center, size);
        }
    }
}
