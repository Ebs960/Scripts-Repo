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
            Biome.Taiga or Biome.Taiga => 0.7f,
            Biome.Marsh or Biome.Swamp => 0.6f,
            
            // Sparse decoration biomes
            Biome.Desert or Biome.Tundra => 0.4f,
            Biome.Mountain or Biome.Arctic => 0.3f,
            
            // Hostile biomes - minimal decorations
            Biome.Volcanic or Biome.Steam => 0.2f,
            Biome.Hellscape or Biome.Brimstone => 0.1f,
            
            // Moon biomes
            Biome.MoonDunes => 0.5f,
            Biome.MoonCraters => 0.3f,
            
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
            Biome.Taiga or Biome.Taiga => 3,
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
    
    [Range(0f, 2.5f)]
    [Tooltip("Additional elevation boost for mountain tiles (added to their base elevation). Similar to `hillElevationBoost`.")]
    public float mountainElevationBoost = 0.25f;
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
    public float latitudeInfluence = 0.45f;

    [Range(0.2f, 2f)]
    [Tooltip("Exponent applied to absolute latitude when computing latitude temperature. >1 makes poles colder (steeper gradient).")]
    public float latitudeExponent = 2.0f;

    [Header("Temperature Noise")]
    [Tooltip("Base frequency for low-frequency, region-scale temperature noise (periodic/wrap-safe)")]
    [SerializeField] public float temperatureNoiseFrequency = 0.012f;
    [Tooltip("Multiplier applied to create a detail octave for temperature (blended) - higher = more local variation")]
    [SerializeField] public float temperatureDetailMultiplier = 4f;
    [Tooltip("Blend factor between base and detail temperature noise (0 = base only, 1 = detail only)")]
    [Range(0f,1f)]
    [SerializeField] public float temperatureDetailStrength = 0.15f;

    [Header("Climate Noise Options")]
    [Tooltip("When enabled, use periodic/wrap-safe climate noise sampling (recommended)")]
    [SerializeField] public bool usePeriodicClimateNoise = true;

    [Header("Climate Smoothing")]
    [Tooltip("Number of smoothing passes to run over temperature and moisture to reduce speckling")]
    [SerializeField] public int climateSmoothingPasses = 2;
    [Tooltip("Strength of each smoothing pass (0=no smoothing, 1=replace with neighbor average)")]
    [Range(0f,1f)]
    [SerializeField] public float climateSmoothingStrength = 0.45f;

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
    
    [Range(0f, 2.5f)]
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
    [Range(0, 20)]
    [Tooltip("Minimum rivers per continent")]
    public int minRiversPerContinent = 1;
    [Range(1, 20)]
    [Tooltip("Maximum rivers per continent")]
    public int maxRiversPerContinent = 2;
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
    [Tooltip("Minimum lake radius (tiles)")]
    public int lakeMinRadiusTiles = 3;
    [Range(3, 30)]
    [Tooltip("Maximum lake radius (tiles)")]
    public int lakeMaxRadiusTiles = 12;
    [Tooltip("Minimum distance (tiles) a lake center must be from coast")]
    public int lakeMinDistanceFromCoast = 3;
    [Header("Coast Irregularity")]
    [Tooltip("Number of random coastal 'bites' (water) to stamp per map)")]
    public int coastBiteCount = 3;
    [Tooltip("Min radius (tiles) for coastal bite stamps")]
    public int coastBiteRadiusMin = 2;
    [Tooltip("Max radius (tiles) for coastal bite stamps")]
    public int coastBiteRadiusMax = 5;

    [Tooltip("Number of random coastal 'spurs' (land peninsulas) to stamp per map)")]
    public int coastSpurCount = 2;
    [Tooltip("Min radius (tiles) for coastal spur stamps")]
    public int coastSpurRadiusMin = 1;
    [Tooltip("Max radius (tiles) for coastal spur stamps")]
    public int coastSpurRadiusMax = 3;

    [Tooltip("Minimum total land tiles required to allow coast stamping")]
    public int minLandTilesForCoastStamps = 120;
    [Range(0f, 0.2f)]
    [Tooltip("Fixed elevation for lake tiles")]
    public float lakeElevation = 0.02f;
    [Range(0f, 0.2f)]
    [Tooltip("Render elevation for lake tiles (baseline). This value is reduced by `lakeDepth` to produce the final render elevation.")]
    public float lakeRenderElevation = 0.05f;
    [Header("Lake/River Render Depth")]
    [Range(0f, 0.2f)]
    [Tooltip("Depth reduction applied to lake render elevation (subtracts from lakeRenderElevation). Similar to how `riverDepth` lowers river tiles.")]
    public float lakeDepth = 0.05f;

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
        ClimateManager.OnPlanetSeasonChanged += HandlePlanetSeasonChanged;

        var mgr = GameManager.Instance != null
            ? GameManager.Instance.GetClimateManager(planetIndex)
            : ClimateManager.Instance;
        if (mgr != null)
        {
            ApplySeasonToTiles(mgr.GetSeasonForPlanet(planetIndex));
        }
    }

    void OnDestroy()
    {
        ClimateManager.OnPlanetSeasonChanged -= HandlePlanetSeasonChanged;

    }

    private void HandlePlanetSeasonChanged(int planet, Season newSeason)
    {
        if (planet != planetIndex) return;

        ApplySeasonToTiles(newSeason);
    }

    private void ApplySeasonToTiles(Season newSeason)
    {
        if (data == null || data.Count == 0) return;

        var mgr = GameManager.Instance != null
            ? GameManager.Instance.GetClimateManager(planetIndex)
            : ClimateManager.Instance;
        if (mgr == null) return;

        foreach (var tile in data.Values)
        {
            var response = mgr.GetSeasonResponse(tile.biome, newSeason);
            tile.season = newSeason;
            tile.seasonalYieldModifier = response.yieldMultiplier;
            tile.hasSnow = response.snow > 0f && tile.isLand;
            tile.isFrozen = tile.hasSnow && (tile.isLake || tile.isRiver);
        }
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
        // Calculate noise frequencies for elevation
        float elevBroadFreq = 1f / (mapWidth * 0.5f);
        float elevRidgedFreq = 1f / (mapWidth * 0.3f);
        float elevBillowFreq = 1f / (mapWidth * 0.4f);

        // DIAGNOSTICS: report key settings and grid stats
        if (enableDiagnostics)
        {
            Debug.Log($"[PlanetGenerator][Diag] mapWidth={mapWidth:F1} mapHeight={mapHeight:F1} tiles={tileCount}");
            // Log continent/island sizing (use GameSetupData as authoritative source)
            Debug.Log($"[StampGen][Diag] mapSize={GameSetupData.mapSize} contTiles(WxH) min={GameSetupData.continentMinWidthTiles}x{GameSetupData.continentMinHeightTiles} max={GameSetupData.continentMaxWidthTiles}x{GameSetupData.continentMaxHeightTiles} minDistance={GameSetupData.continentMinDistanceTiles}");
            Debug.Log($"[StampGen][Diag] islandRadius (effective) min={GameSetupData.islandMinRadiusTiles} max={GameSetupData.islandMaxRadiusTiles} minDistanceFromContinents={GameSetupData.islandMinDistanceFromContinents}");
            Debug.Log($"[StampGen][Diag] lakeRadius (effective) min={GameSetupData.lakeMinRadiusTiles} max={GameSetupData.lakeMaxRadiusTiles} minDistanceFromCoast={GameSetupData.lakeMinDistanceFromCoast}");
            Debug.Log($"[Setup] mapSize={GameSetupData.mapSize} contCount={GameSetupData.numberOfContinents} islandCount={GameSetupData.numberOfIslands} generateIslands={GameSetupData.generateIslands}");
            Debug.Log($"[PlanetGenerator][Diag] numberOfContinents (effective)={GameSetupData.numberOfContinents} continentTiles(WxH) min={GameSetupData.continentMinWidthTiles}x{GameSetupData.continentMinHeightTiles} max={GameSetupData.continentMaxWidthTiles}x{GameSetupData.continentMaxHeightTiles}");
            Debug.Log($"[PlanetGenerator][Diag] generateIslands (effective)={GameSetupData.generateIslands} numberOfIslands (effective)={GameSetupData.numberOfIslands}");
            Debug.Log($"[PlanetGenerator][Diag] latitudeInfluence={latitudeInfluence} latitudeExponent={latitudeExponent} temperatureBias={temperatureBias} moistureBias={moistureBias}");

            // Additional comprehensive diagnostic summary (include GameSetupData vs generator-resolved values)
            int[] riverPresetMap = { 0, 1, 4, 6, 8, 10 };
            int expectedRiverFromMoisture = riverPresetMap[Mathf.Clamp(GameSetupData.selectedMoisturePreset, 0, riverPresetMap.Length - 1)];
            Debug.Log($"[StampGen][Summary] seed={seed} randomSeed={randomSeed} mapSize={GameSetupData.mapSize} climatePreset={GameSetupData.selectedClimatePreset} moisturePreset={GameSetupData.selectedMoisturePreset} landPreset={GameSetupData.selectedLandPreset} terrainPreset={GameSetupData.selectedTerrainPreset}");
            Debug.Log($"[StampGen][Summary] GameSetupData -> riversEnabled={GameSetupData.enableRivers} riverCount={GameSetupData.riverCount} (expected from moisture preset={expectedRiverFromMoisture}) lakesEnabled={GameSetupData.enableLakes} lakeCount={GameSetupData.numberOfLakes} lakeMinRadius={GameSetupData.lakeMinRadiusTiles} lakeMaxRadius={GameSetupData.lakeMaxRadiusTiles} lakeMinDistanceFromCoast={GameSetupData.lakeMinDistanceFromCoast}");
            Debug.Log($"[StampGen][Summary] GameSetupData -> enableRivers={GameSetupData.enableRivers} minRiversPerContinent={GameSetupData.minRiversPerContinent} maxRiversPerContinent={GameSetupData.maxRiversPerContinent} lakeMinRadiusTiles={GameSetupData.lakeMinRadiusTiles} lakeMaxRadiusTiles={GameSetupData.lakeMaxRadiusTiles} lakeMinDistanceFromCoast={GameSetupData.lakeMinDistanceFromCoast}");
            Debug.Log($"[StampGen][Summary] continents={numberOfContinents} islands={numberOfIslands} generateIslands={generateIslands} continentMinDistanceTiles={GameSetupData.continentMinDistanceTiles}");
            Debug.Log($"[StampGen][Summary] temperatureBias={temperatureBias} moistureBias={moistureBias} latitudeInfluence={latitudeInfluence} latitudeExponent={latitudeExponent} temperatureNoiseFreq={temperatureNoiseFrequency} tempDetailMultiplier={temperatureDetailMultiplier} tempDetailStrength={temperatureDetailStrength}");
        }
        
        int tilesX = grid.Width;
        int tilesZ = grid.Height;
        // ---------- 2. Generate Deterministic Continent Seeds with Per-Continent Sizes ------------------
        List<ContinentData> continentDataList = GenerateContinentData(
            numberOfContinents,
            seed ^ 0xD00D,
            tilesX,
            tilesZ,
            GameSetupData.continentMinWidthTiles,
            GameSetupData.continentMaxWidthTiles,
            GameSetupData.continentMinHeightTiles,
            GameSetupData.continentMaxHeightTiles,
            GameSetupData.continentMinDistanceTiles
        );
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

        Debug.Log(
            $"[StampGen][Diag] continentMinDistanceTiles = " +
            $"{GameSetupData.continentMinDistanceTiles}"
        );

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

        // ---------- 2.75. Coastal irregularity passes (bays & peninsulas) ----------
        // compute current land count (islands/stamps applied so far)
        int _currentLandCount = 0;
        for (int _i = 0; _i < tileCount; _i++) if (isLandTile[_i]) _currentLandCount++;
        if (_currentLandCount >= minLandTilesForCoastStamps)
        {
            System.Random coastRand = new System.Random(unchecked((int)(seed ^ 0xBEEF)));

            // Build coast candidate lists
            List<int> coastLandCandidates = new List<int>();
            List<int> coastWaterCandidates = new List<int>();
            for (int i = 0; i < tileCount; i++) {
                bool anyWaterNeighbor = false;
                foreach (int n in grid.neighbors[i]) {
                    if (n < 0 || n >= tileCount) continue;
                    if (!isLandTile[n]) { anyWaterNeighbor = true; break; }
                }
                if (isLandTile[i] && anyWaterNeighbor) coastLandCandidates.Add(i);
                if (!isLandTile[i] && anyWaterNeighbor) coastWaterCandidates.Add(i);
            }

            int ApplyWalkInland(int startIdx, int steps) {
                int cur = startIdx;
                for (int s = 0; s < steps; s++) {
                    int best = -1; int bestCount = -1;
                    foreach (int n in grid.neighbors[cur]) {
                        if (n < 0 || n >= tileCount) continue;
                        if (!isLandTile[n]) continue;
                        int cnt = 0;
                        foreach (int nn in grid.neighbors[n]) { if (nn >= 0 && nn < tileCount && isLandTile[nn]) cnt++; }
                        if (cnt > bestCount) { bestCount = cnt; best = n; }
                    }
                    if (best == -1) break;
                    cur = best;
                }
                return cur;
            }

            int ApplyWalkOffshore(int startIdx, int steps) {
                int cur = startIdx;
                for (int s = 0; s < steps; s++) {
                    int best = -1; int bestCount = -1;
                    foreach (int n in grid.neighbors[cur]) {
                        if (n < 0 || n >= tileCount) continue;
                        if (isLandTile[n]) continue;
                        int cnt = 0;
                        foreach (int nn in grid.neighbors[n]) { if (nn >= 0 && nn < tileCount && !isLandTile[nn]) cnt++; }
                        if (cnt > bestCount) { bestCount = cnt; best = n; }
                    }
                    if (best == -1) break;
                    cur = best;
                }
                return cur;
            }

            // Apply bites (carve water into land)
            for (int b = 0; b < coastBiteCount && coastLandCandidates.Count > 0; b++) {
                int pick = coastRand.Next(coastLandCandidates.Count);
                int startIdx = coastLandCandidates[pick];
                int r = coastRand.Next(Mathf.Max(1, coastBiteRadiusMin), Mathf.Max(coastBiteRadiusMin, coastBiteRadiusMax) + 1);
                int walkSteps = Mathf.Max(1, r / 2);
                int centerIdx = ApplyWalkInland(startIdx, walkSteps);

                // Simulate removal size and skip if too aggressive
                int removed = 0;
                for (int t = 0; t < tileCount; t++) {
                    int dist = HexDistanceWrapped(tileCoords[t], tileCoords[centerIdx], tilesX);
                    if (dist <= r && isLandTile[t]) removed++;
                }
                if (removed == 0) continue;
                if ((float)removed / Mathf.Max(1, _currentLandCount) > 0.15f) continue; // don't remove >15% of land

                StampCircle(tileCoords[centerIdx], r, false, false);
            }

            // Apply spurs (add small land peninsulas)
            for (int s = 0; s < coastSpurCount && coastWaterCandidates.Count > 0; s++) {
                int pick = coastRand.Next(coastWaterCandidates.Count);
                int startIdx = coastWaterCandidates[pick];
                int r = coastRand.Next(Mathf.Max(1, coastSpurRadiusMin), Mathf.Max(coastSpurRadiusMin, coastSpurRadiusMax) + 1);
                int walkSteps = Mathf.Max(1, r / 3);
                int centerIdx = ApplyWalkOffshore(startIdx, walkSteps);

                // Simulate added tiles and ensure at least one connects to existing land
                List<int> added = new List<int>();
                for (int t = 0; t < tileCount; t++) {
                    int dist = HexDistanceWrapped(tileCoords[t], tileCoords[centerIdx], tilesX);
                    if (dist <= r && !isLandTile[t]) added.Add(t);
                }
                if (added.Count == 0) continue;
                bool connects = false;
                foreach (int at in added) {
                    foreach (int n in grid.neighbors[at]) { if (n >= 0 && n < tileCount && isLandTile[n]) { connects = true; break; } }
                    if (connects) break;
                }
                if (!connects) continue;

                StampCircle(tileCoords[centerIdx], r, true, false);
            }
        }

        // ---------- 3. Generate Lakes (Stamping) ----------
        bool isEarthPlanet = !isMarsWorldType && !isVenusWorldType && !isMercuryWorldType && !isJupiterWorldType &&
                            !isSaturnWorldType && !isUranusWorldType && !isNeptuneWorldType && !isPlutoWorldType &&
                            !isTitanWorldType && !isEuropaWorldType && !isIoWorldType && !isGanymedeWorldType &&
                            !isCallistoWorldType && !isLunaWorldType;

        int lakesStamped = 0;
        List<Vector2Int> lakeCenters = new List<Vector2Int>();
        if (enableLakes && isEarthPlanet && numberOfLakes > 0)
        {
            int lakeMinRadius = Mathf.Max(1, lakeMinRadiusTiles);
            int lakeMaxRadius = Mathf.Max(lakeMinRadius, lakeMaxRadiusTiles);
            int lakeMinDistance = Mathf.Max(0, lakeMinDistanceFromCoast);

            List<int> lakeCoastTiles = new List<int>();
            for (int i = 0; i < tileCount; i++) {
                if (!isLandTile[i]) continue;
                bool adjacentToOcean = false;
                foreach (int neighbor in grid.neighbors[i]) {
                    if (!isLandTile[neighbor]) {
                        adjacentToOcean = true;
                        break;
                    }
                }
                if (adjacentToOcean) lakeCoastTiles.Add(i);
            }

            List<int> candidateCenters = new List<int>();
            if (lakeCoastTiles.Count == 0) {
                for (int i = 0; i < tileCount; i++) {
                    if (isLandTile[i]) candidateCenters.Add(i);
                }
            } else {
                int[] distanceFromCoast = BuildDistanceMap(lakeCoastTiles);
                for (int i = 0; i < tileCount; i++) {
                    if (!isLandTile[i]) continue;
                    if (distanceFromCoast[i] >= lakeMinDistance) {
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
            int fallbackRadius = Mathf.Max(1, lakeMinRadiusTiles);
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
        float ComputeLandElevationForIndex(int index)
        {
            Vector2Int coord = tileCoords[index];
            Vector2 tilePos = new Vector2(coord.x, coord.y);
            Vector3 noisePoint = new Vector3(coord.x, 0f, coord.y) + noiseOffset;
            float noiseElevation;
            if (billowHillWeight > 0.01f) {
                noiseElevation = noise.GetAdvancedElevationPeriodic(tilePos, mapWidth, mapHeight,
                    elevBroadFreq, elevRidgedFreq, elevBillowFreq,
                    ridgedMountainWeight, billowHillWeight);
            } else {
                noiseElevation = noise.GetElevationPeriodic(tilePos, mapWidth, mapHeight,
                    elevBroadFreq, elevRidgedFreq, ridgedMountainWeight);
            }
            float elevationRange = maxTotalElevation - baseLandElevation;
            return baseLandElevation + (noiseElevation * elevationRange);
        }

        for (int lakeId = 0; lakeId < lakeCenters.Count; lakeId++)
        {
            Vector2Int center = lakeCenters[lakeId];
            int centerIdx = center.y * tilesX + center.x;
            if (centerIdx < 0 || centerIdx >= tileCount) continue;
            // Flood-fill to collect the full connected lake tile set, then examine perimeter
            var lakeQueue = new Queue<int>();
            var lakeSet = new HashSet<int>();
            if (isLakeTile[centerIdx]) lakeQueue.Enqueue(centerIdx);
            else
            {
                // if center isn't a lake tile, try to find any adjacent lake tile
                foreach (int n in grid.neighbors[centerIdx]) if (n >= 0 && n < tileCount && isLakeTile[n]) { lakeQueue.Enqueue(n); break; }
            }

            while (lakeQueue.Count > 0)
            {
                int idx = lakeQueue.Dequeue();
                if (lakeSet.Contains(idx)) continue;
                if (!isLakeTile[idx]) continue;
                lakeSet.Add(idx);
                foreach (int n in grid.neighbors[idx]) if (n >= 0 && n < tileCount && isLakeTile[n] && !lakeSet.Contains(n)) lakeQueue.Enqueue(n);
            }

            float minNeighborElevation = float.MaxValue;
            int validNeighborCount = 0;
            var perimeterNeighbors = new HashSet<int>();

            foreach (int lakeTile in lakeSet)
            {
                foreach (int neighbor in grid.neighbors[lakeTile])
                {
                    if (neighbor < 0 || neighbor >= tileCount) continue;
                    if (!isLandTile[neighbor]) continue; // only consider land neighbors as potential outlets
                    if (isLakeTile[neighbor]) continue; // skip other lake tiles
                    perimeterNeighbors.Add(neighbor);
                }
            }

            foreach (int neighbor in perimeterNeighbors)
            {
                validNeighborCount++;
                float neighborElevation = ComputeLandElevationForIndex(neighbor);
                if (neighborElevation < minNeighborElevation) minNeighborElevation = neighborElevation;
            }

            if (validNeighborCount == 0)
            {
                Debug.Log($"[StampGen][LakeOutlet] lakeId={lakeId} lakeElev={lakeElevation:F4} minNeighbor=none delta=NA validNeighbors=0 perimeterSize={lakeSet.Count}");
            }
            else
            {
                float delta = minNeighborElevation - lakeElevation;
                Debug.Log($"[StampGen][LakeOutlet] lakeId={lakeId} lakeElev={lakeElevation:F4} minNeighbor={minNeighborElevation:F4} delta={delta:F4} validNeighbors={validNeighborCount} perimeterSize={lakeSet.Count}");
            }
        }

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

        int northPoleY = 0;
        int southPoleY = Mathf.Max(0, tilesZ - 1);
        int equatorY = Mathf.Clamp(tilesZ / 2, 0, southPoleY);
        float? northPoleTemp = null;
        float? southPoleTemp = null;
        float? equatorTemp = null;
        
        // Two-pass climate/elevation processing:
        // 1) Sample elevation, temperature, moisture into arrays (periodic sampling available)
        // 2) Smooth climate arrays (passes)
        // 3) Assign biomes using smoothed climate values and already-computed elevation

        float[] sampledTemp = new float[tileCount];
        float[] sampledMoist = new float[tileCount];
        float[] sampledElev = new float[tileCount];

        for (int i = 0; i < tileCount; i++)
        {
            Vector2Int coord = tileCoords[i];
            bool isLand = isLandTile[i];
            Vector2 tilePos = new Vector2(coord.x, coord.y);
            Vector3 noisePoint = new Vector3(coord.x, 0f, coord.y) + noiseOffset;

            float noiseElevation;
            if (billowHillWeight > 0.01f)
            {
                noiseElevation = noise.GetAdvancedElevationPeriodic(tilePos, mapWidth, mapHeight,
                    elevBroadFreq, elevRidgedFreq, elevBillowFreq,
                    ridgedMountainWeight, billowHillWeight);
            }
            else
            {
                noiseElevation = noise.GetElevationPeriodic(tilePos, mapWidth, mapHeight,
                    elevBroadFreq, elevRidgedFreq, ridgedMountainWeight);
            }

            // Sample climate
            float moisture;
            float noiseTemp;
            if (usePeriodicClimateNoise)
            {
                moisture = noise.GetMoisturePeriodic(tilePos, mapWidth, mapHeight, moistureFreq);
                float baseTemp = noise.GetTemperaturePeriodic(tilePos, mapWidth, mapHeight, temperatureNoiseFrequency);
                float detailTemp = noise.GetTemperaturePeriodic(tilePos, mapWidth, mapHeight, temperatureNoiseFrequency * temperatureDetailMultiplier);
                noiseTemp = Mathf.Lerp(baseTemp, detailTemp, temperatureDetailStrength);
            }
            else
            {
                moisture = noise.GetMoisture(noisePoint * moistureFreq);
                noiseTemp = noise.GetTemperatureFromNoise(noisePoint);
            }

            moisture = Mathf.Clamp01(moisture + moistureBias);
            float normalizedY = mapHeight > 1f ? coord.y / Mathf.Max(1f, mapHeight - 1f) : 0f;
            // Step 1: latitude as distance from equator (0 at equator, 1 at poles)
            float lat = Mathf.Abs(normalizedY - 0.5f) * 2f;
            // Step 2: convert to heat curve: equator => +1, poles => -1
            float latCurve = 1f - lat;
            latCurve = latCurve * 2f - 1f;
            // Step 3: apply exponent symmetrically and scale by influence
            float latEffect = Mathf.Sign(latCurve) * Mathf.Pow(Mathf.Abs(latCurve), latitudeExponent) * latitudeInfluence;
            // Step 4: combine with base temperature
            float temperature = noiseTemp + latEffect + temperatureBias;
            temperature = Mathf.Clamp01(temperature);

            // Compute final elevation for this tile now (independent of biome assignment)
            float finalElevation;
            if (isLakeTile[i])
            {
                finalElevation = lakeElevation;
            }
            else if (isLand)
            {
                float elevationRange = maxTotalElevation - baseLandElevation;
                finalElevation = baseLandElevation + (noiseElevation * elevationRange);
            }
            else
            {
                finalElevation = 0f;
            }

            finalElevation = Mathf.Min(finalElevation, maxTotalElevation);

            sampledTemp[i] = temperature;
            sampledMoist[i] = moisture;
            sampledElev[i] = finalElevation;
            tileElevation[i] = finalElevation;

            if (i > 0 && i % 500 == 0)
            {
                if (loadingPanelController != null)
                {
                    loadingPanelController.SetProgress(0.3f + (float)i / tileCount * 0.05f);
                    loadingPanelController.SetStatus("Sampling climate and elevation...");
                }
                yield return null;
            }
        }

        // Smooth climate arrays to reduce speckling
        for (int pass = 0; pass < Mathf.Max(0, climateSmoothingPasses); pass++)
        {
            float[] newTemp = new float[tileCount];
            float[] newMoist = new float[tileCount];
            for (int i = 0; i < tileCount; i++)
            {
                float sumT = 0f; int cntT = 0;
                float sumM = 0f; int cntM = 0;
                foreach (int n in grid.neighbors[i])
                {
                    if (n < 0 || n >= tileCount) continue;
                    sumT += sampledTemp[n]; cntT++;
                    sumM += sampledMoist[n]; cntM++;
                }
                if (cntT > 0)
                {
                    float avgT = sumT / cntT;
                    newTemp[i] = Mathf.Lerp(sampledTemp[i], avgT, climateSmoothingStrength);
                }
                else newTemp[i] = sampledTemp[i];

                if (cntM > 0)
                {
                    float avgM = sumM / cntM;
                    newMoist[i] = Mathf.Lerp(sampledMoist[i], avgM, climateSmoothingStrength);
                }
                else newMoist[i] = sampledMoist[i];
            }

            sampledTemp = newTemp;
            sampledMoist = newMoist;

            if (loadingPanelController != null)
            {
                loadingPanelController.SetProgress(0.35f + (float)pass / Mathf.Max(1, climateSmoothingPasses) * 0.05f);
                loadingPanelController.SetStatus($"Smoothing climate (pass {pass+1}/{climateSmoothingPasses})...");
            }
            yield return null;
        }

        // Second pass: assign biomes and build HexTileData using smoothed climate
        for (int i = 0; i < tileCount; i++)
        {
            Vector2Int coord = tileCoords[i];
            bool isLand = isLandTile[i];
            bool isLake = isLakeTile[i];
            float temperature = sampledTemp[i];
            float moisture = sampledMoist[i];
            float finalElevation = sampledElev[i];

            Biome biome;
            bool isHill = false;

            if (isLake)
            {
                biome = Biome.Lake;
            }
            else if (isLand)
            {
                biome = GetBiomeForTile(i, true, temperature, moisture);

                if (finalElevation > mountainThreshold)
                {
                    if (biome != Biome.Glacier && biome != Biome.Arctic)
                    {
                        biome = Biome.Mountain;
                        // Apply mountain boost so mountains sit noticeably above surrounding land
                        finalElevation += mountainElevationBoost;
                    }
                }
                else if (finalElevation > hillThreshold)
                {
                    // Prevent water/coast tiles from being hills. Only allow hills on true land biomes.
                    bool biomeIsWater = (biome == Biome.Coast || biome == Biome.Seas || biome == Biome.Ocean || biome == Biome.Lake || biome == Biome.River);
                    if (!biomeIsWater)
                    {
                        isHill = true;
                        finalElevation += hillElevationBoost;
                    }
                }

                // Ensure elevation stays within configured maximum after applying boosts
                finalElevation = Mathf.Min(finalElevation, maxTotalElevation);
                // Track land elevation range for later normalization
                if (finalElevation < landElevMin) landElevMin = finalElevation;
                if (finalElevation > landElevMax) landElevMax = finalElevation;
                landTileIndices.Add(i);
            }
            else
            {
                biome = GetBiomeForTile(i, false, temperature, moisture);
            }

            if (biome == Biome.Glacier)
            {
                float elevationRange = maxTotalElevation - baseLandElevation;
                finalElevation = baseLandElevation + (noise.GetElevationPeriodic(new Vector2(coord.x, coord.y), mapWidth, mapHeight, elevBroadFreq, elevRidgedFreq, ridgedMountainWeight) * elevationRange);
                if (finalElevation < landElevMin) landElevMin = finalElevation;
                if (finalElevation > landElevMax) landElevMax = finalElevation;
                if (!landTileIndices.Contains(i)) landTileIndices.Add(i);
            }

            tileElevation[i] = finalElevation;

            // Track climate min/max for diagnostics
            if (temperature < temperatureMin) temperatureMin = temperature;
            if (temperature > temperatureMax) temperatureMax = temperature;
            if (moisture < moistureMin) moistureMin = moisture;
            if (moisture > moistureMax) moistureMax = moisture;

            // Create HexTileData
            var y = BiomeHelper.Yields(biome);
            int moveCost = BiomeHelper.GetMovementCost(biome);
            ElevationTier elevTier = ElevationTier.Flat;
            if (finalElevation > mountainThreshold) elevTier = ElevationTier.Mountain;
            else if (finalElevation > hillThreshold) elevTier = ElevationTier.Hill;

            var td = new HexTileData
            {
                biome = biome,
                food = y.food, production = y.prod, gold = y.gold, science = y.sci, culture = y.cult,
                occupantId = 0,
                isLand = isLand,
                isLake = isLake,
                isRiver = isRiverTile[i],
                isHill = isHill,
                elevation = finalElevation,
                renderElevation = 0f,
                elevationTier = elevTier,
                temperature = temperature,
                moisture = moisture,
                movementCost = moveCost,
                isPassable = true,
                isMoonTile = false
            };
            data[i] = td;
            baseData[i] = td;

            if (i > 0 && i % 250 == 0)
            {
                if (loadingPanelController != null)
                {
                    loadingPanelController.SetProgress(0.3f + (float)i / tileCount * 0.2f);
                    loadingPanelController.SetStatus("Defining biomes and elevation...");
                }
                yield return null;
            }
        }

        if (enableDiagnostics)
        {
            string northLabel = northPoleTemp.HasValue ? northPoleTemp.Value.ToString("F3") : "n/a";
            string southLabel = southPoleTemp.HasValue ? southPoleTemp.Value.ToString("F3") : "n/a";
            string equatorLabel = equatorTemp.HasValue ? equatorTemp.Value.ToString("F3") : "n/a";
            Debug.Log($"[PlanetGenerator][Diag] temperature y=0: {northLabel} y=max: {southLabel} y=mid: {equatorLabel}");
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
                // Compute lake render elevation relative to surrounding land so lakes sit below neighboring terrain.
                // Find neighboring land tiles' normalized elevation and average them to decide lake level, then
                // subtract `lakeDepth` so the lake sits lower than surrounding land (more realistic).
                float sumNorm = 0f;
                int normCount = 0;
                foreach (int n in grid.neighbors[i])
                {
                    if (n < 0 || n >= tileCount) continue;
                    if (!data.ContainsKey(n)) continue;
                    var nd = data[n];
                    if (nd.isLand && nd.biome != Biome.Lake)
                    {
                        float neighborNorm = Mathf.InverseLerp(landElevMin, landElevMax, nd.elevation);
                        sumNorm += neighborNorm;
                        normCount++;
                    }
                }

                if (normCount > 0)
                {
                    float avgNorm = sumNorm / normCount;
                    float baseRender = 0.1f + avgNorm * 0.85f; // same normalization used for land
                    td.renderElevation = Mathf.Clamp01(Mathf.Max(0f, baseRender - lakeDepth));
                }
                else
                {
                    // No neighboring land found (edge case): fallback to the inspector baseline reduced by lakeDepth
                    td.renderElevation = Mathf.Max(0f, lakeRenderElevation - lakeDepth);
                }
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

            // Protect Arctic and Glacier tiles from ever becoming a coast or sea
            if (data[i].biome == Biome.Arctic || data[i].biome == Biome.Glacier) {
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
            int targetRiverCount = 0; // will be set based on discovered lakes (one river per lake)
            System.Random riverRand = new System.Random(unchecked((int)(seed ^ 0xBADF00D)));
            HashSet<int> riverTiles = new HashSet<int>();
            // Timing counters for diagnostics
            int aStarCalls = 0;
            double aStarMs = 0.0;
            int bfsCalls = 0;
            double bfsMs = 0.0;
            float riverGenerationStart = Time.realtimeSinceStartup;

            HashSet<int> lakeEdgeSources = new HashSet<int>();
            // Map each lake-edge source tile to its lake id (index into lakeCenters)
            Dictionary<int, int> sourceToLakeId = new Dictionary<int, int>();
            if (lakeCenters != null && lakeCenters.Count > 0)
            {
                // For each stamped lake, flood-fill the connected lake tiles and collect perimeter land neighbors
                for (int i = 0; i < lakeCenters.Count; i++)
                {
                    var center = lakeCenters[i];
                    int centerIdx = center.y * grid.Width + center.x;
                    if (!tileData.ContainsKey(centerIdx)) continue;

                    var queue = new Queue<int>();
                    var lakeSet = new HashSet<int>();
                    if (tileData[centerIdx].isLake) queue.Enqueue(centerIdx);
                    else
                    {
                        foreach (int n in grid.neighbors[centerIdx]) if (n >= 0 && n < tileCount && tileData.ContainsKey(n) && tileData[n].isLake) { queue.Enqueue(n); break; }
                    }

                    while (queue.Count > 0)
                    {
                        int idx = queue.Dequeue();
                        if (lakeSet.Contains(idx)) continue;
                        if (!tileData.ContainsKey(idx) || !tileData[idx].isLake) continue;
                        lakeSet.Add(idx);
                        foreach (int n in grid.neighbors[idx]) if (n >= 0 && n < tileCount && !lakeSet.Contains(n) && tileData.ContainsKey(n) && tileData[n].isLake) queue.Enqueue(n);
                    }

                    foreach (int lakeTile in lakeSet)
                    {
                        foreach (int neighbor in grid.neighbors[lakeTile])
                        {
                            if (neighbor < 0 || neighbor >= tileCount) continue;
                            if (!tileData.TryGetValue(neighbor, out var nTile)) continue;
                            if (!nTile.isLand || nTile.isLake || nTile.isRiver) continue;
                            if (nTile.biome == Biome.Coast || nTile.biome == Biome.Ocean || nTile.biome == Biome.Seas) continue;
                            lakeEdgeSources.Add(neighbor);
                            if (!sourceToLakeId.ContainsKey(neighbor)) sourceToLakeId[neighbor] = i;
                        }
                    }
                }
            }

            List<int> riverSources = lakeEdgeSources.ToList();
            HashSet<int> usedLakeIds = new HashSet<int>();
            if (riverSources.Count == 0)
            {
                Debug.LogWarning("[StampGen] No lake-edge river sources found; falling back to inland land tiles.");
                foreach (var kvp in tileData)
                {
                    var td = kvp.Value;
                    if (!td.isLand || td.isLake || td.isRiver) continue;
                    if (td.biome == Biome.Coast || td.biome == Biome.Ocean || td.biome == Biome.Seas) continue;

                    bool adjacentToOcean = false;
                    foreach (int neighbor in grid.neighbors[kvp.Key])
                    {
                        if (!tileData.TryGetValue(neighbor, out var nTile)) continue;
                        if (nTile.biome == Biome.Ocean || nTile.biome == Biome.Seas || nTile.biome == Biome.Coast)
                        {
                            adjacentToOcean = true;
                            break;
                        }
                    }

                    if (!adjacentToOcean)
                    {
                        riverSources.Add(kvp.Key);
                    }
                }
            }

            if (riverSources.Count == 0)
            {
                Debug.LogWarning("[StampGen] No valid river sources found.");
                // Dump a few samples from original source set (if any lake sources existed)
                int sampleCount = Math.Min(10, sourceToLakeId.Count);
                int i = 0;
                foreach (var kvp in sourceToLakeId)
                {
                    if (i++ >= sampleCount) break;
                    if (!tileData.TryGetValue(kvp.Key, out var sTile)) continue;
                    Debug.Log($"[StampGen][Debug] lakeSource sample idx={kvp.Key} lakeId={kvp.Value} isLand={sTile.isLand} isLake={sTile.isLake} isRiver={sTile.isRiver} biome={sTile.biome} elev={sTile.elevation}");
                }
                // Also log a few random inland samples
                i = 0;
                foreach (var kvp in tileData)
                {
                    if (i++ >= 10) break;
                    var td = kvp.Value;
                    Debug.Log($"[StampGen][Debug] tile sample idx={kvp.Key} isLand={td.isLand} isLake={td.isLake} isRiver={td.isRiver} biome={td.biome} elev={td.elevation}");
                }
                yield break;
            }

            // NOTE: previously we clamped targetRiverCount to lake count (one river per lake).
            // For stricter coast-reaching behavior we do NOT clamp here; targetRiverCount stays as requested.

            int riversGenerated = 0;
            int attempts = 0;

            // Determine target river count strictly from lakes (one river per lake maximum)
            // Debug: report sources and lake-edge counts; targetRiverCount will be derived from lake groups below
            Debug.Log($"[StampGen][River] sources={riverSources.Count} lakeSources={sourceToLakeId.Count}");

            // Group lake-edge sources by lake id to enforce one river per lake
            Dictionary<int, List<int>> lakeSourcesDict = new Dictionary<int, List<int>>();
            foreach (var kvp in sourceToLakeId)
            {
                if (!lakeSourcesDict.TryGetValue(kvp.Value, out var list)) { list = new List<int>(); lakeSourcesDict[kvp.Value] = list; }
                list.Add(kvp.Key);
            }
            List<int> unusedLakeIds = lakeSourcesDict.Keys.ToList();

            // Caches for performance (declared before use)
            int[] tileContinent = null;
            bool[] reachesCoast = null;

            // Precompute caches: continent index per tile, coast lists per continent, and reachability to any coast
            tileContinent = new int[tileCount];
            for (int ti = 0; ti < tileCount; ti++) tileContinent[ti] = GetContinentIndexForTile(ti);

            var coastByContinent = new Dictionary<int, List<int>>();
            foreach (int ct in coastTiles)
            {
                int cidx = (ct >= 0 && ct < tileCount) ? tileContinent[ct] : -1;
                if (!coastByContinent.TryGetValue(cidx, out var lst)) { lst = new List<int>(); coastByContinent[cidx] = lst; }
                lst.Add(ct);
            }

            // Multi-source BFS from all coast tiles to mark tiles that can reach a coast via land (not through lakes)
            reachesCoast = new bool[tileCount];
            var q2 = new Queue<int>();
            var seen2 = new bool[tileCount];
            foreach (int ct in coastTiles)
            {
                if (ct < 0 || ct >= tileCount) continue;
                if (!tileData.TryGetValue(ct, out var ctTile)) continue;
                q2.Enqueue(ct);
                seen2[ct] = true;
                reachesCoast[ct] = true;
            }
            while (q2.Count > 0)
            {
                int idx = q2.Dequeue();
                if (!tileData.TryGetValue(idx, out var t)) continue;
                foreach (int n in grid.neighbors[idx])
                {
                    if (n < 0 || n >= tileCount) continue;
                    if (seen2[n]) continue;
                    if (!tileData.TryGetValue(n, out var nt)) continue;
                    if (!nt.isLand) continue;
                    if (nt.isLake) continue;
                    seen2[n] = true;
                    reachesCoast[n] = true;
                    q2.Enqueue(n);
                }
            }

            // Determine target river count: one river per lake-group when lakes exist, otherwise fall back to preset
            targetRiverCount = (lakeSourcesDict.Count > 0) ? lakeSourcesDict.Count : Mathf.Clamp(GameSetupData.riverCount, 0, 200);
            Debug.Log($"[StampGen][River] targetRiverCount={targetRiverCount} lakeGroups={lakeSourcesDict.Count} coastTiles={coastTiles.Count}");

            // Helper: quick reachability check. If `reachesCoast` is precomputed, use it; otherwise fallback to BFS.

            // Helper: quick reachability check. If `reachesCoast` is precomputed, use it; otherwise fallback to BFS.
            bool HasCoastPath(int startIdx)
            {
                if (reachesCoast != null)
                {
                    if (startIdx >= 0 && startIdx < reachesCoast.Length) return reachesCoast[startIdx];
                    return false;
                }

                var swb = System.Diagnostics.Stopwatch.StartNew();
                try {
                var q = new Queue<int>();
                var seen = new HashSet<int>();
                q.Enqueue(startIdx);
                seen.Add(startIdx);
                while (q.Count > 0)
                {
                    int idx = q.Dequeue();
                    if (!tileData.TryGetValue(idx, out var t)) continue;
                    foreach (int n in grid.neighbors[idx])
                    {
                        if (n < 0 || n >= tileCount) continue;
                        if (!tileData.TryGetValue(n, out var nt)) continue;
                        if (nt.biome == Biome.Coast) return true;
                        if (seen.Contains(n)) continue;
                        // Walkable for reachability: any land tile that is not a lake
                        if (!nt.isLand) continue;
                        if (nt.isLake) continue;
                        seen.Add(n);
                        q.Enqueue(n);
                    }
                }
                return false;
                } finally { swb.Stop(); bfsCalls++; bfsMs += swb.Elapsed.TotalMilliseconds; }
            }

            // Helper: determine which continent a tile belongs to (returns -1 when none)
            int GetContinentIndexForTile(int idx)
            {
                if (continents == null) return -1;
                Vector2Int coord = tileCoords[idx];
                int width = tilesX;
                int height = tilesZ;
                for (int ci = 0; ci < continents.Count; ci++)
                {
                    var c = continents[ci];
                    float halfW = Mathf.Max(0.5f, c.widthTiles * 0.5f);
                    float halfH = Mathf.Max(0.5f, c.heightTiles * 0.5f);
                    float dx = WrappedDelta(coord.x, c.center.x, width) / halfW;
                    float dy = (coord.y - c.center.y) / halfH;
                    if ((dx * dx + dy * dy) <= 1f) return ci;
                }
                return -1;
            }

            // A* pathfinder between land tiles (allows Coast as goal). Returns null on failure.
            List<int> FindPathAStar(int startIdx, int goalIdx, System.Random rand, int maxSteps = 2000)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try {
                if (!tileData.ContainsKey(startIdx) || !tileData.ContainsKey(goalIdx)) return null;

                var openSet = new HashSet<int>();
                var gScore = new Dictionary<int, float>();
                var fScore = new Dictionary<int, float>();
                var cameFrom = new Dictionary<int, int>();

                float Heuristic(int a, int b)
                {
                    return HexDistanceWrapped(tileCoords[a], tileCoords[b], tilesX);
                }

                openSet.Add(startIdx);
                gScore[startIdx] = 0f;
                fScore[startIdx] = Heuristic(startIdx, goalIdx);

                int steps = 0;
                while (openSet.Count > 0 && steps++ < maxSteps)
                {
                    // pick node with lowest fScore
                    int current = -1; float bestF = float.MaxValue;
                    foreach (var n in openSet)
                    {
                        float v = fScore.ContainsKey(n) ? fScore[n] : float.MaxValue;
                        if (v < bestF) { bestF = v; current = n; }
                    }
                    if (current == -1) break;

                    if (current == goalIdx)
                    {
                        var path = new List<int>();
                        int cur = current;
                        while (cameFrom.ContainsKey(cur))
                        {
                            path.Add(cur);
                            cur = cameFrom[cur];
                        }
                        path.Add(startIdx);
                        path.Reverse();
                        return path;
                    }

                    openSet.Remove(current);

                    if (!tileData.TryGetValue(current, out var curTile)) continue;
                    foreach (int n in grid.neighbors[current])
                    {
                        if (n < 0 || n >= tileCount) continue;
                        if (!tileData.TryGetValue(n, out var nt)) continue;
                        // Allow stepping into coast (goal) but otherwise require land and not lake/ocean
                        if (n != goalIdx)
                        {
                            if (!nt.isLand) continue;
                            if (nt.isLake) continue;
                            if (nt.biome == Biome.Ocean || nt.biome == Biome.Seas) continue;
                        }

                        float tentativeG = gScore.ContainsKey(current) ? gScore[current] + 1f : float.MaxValue;
                        float elevCur = tileElevation.ContainsKey(current) ? tileElevation[current] : curTile.elevation;
                        float elevN = tileElevation.ContainsKey(n) ? tileElevation[n] : nt.elevation;
                        float uphill = Mathf.Clamp((elevN - elevCur) * 6f, 0f, 5f);
                        tentativeG += uphill;
                        tentativeG += (float)(rand.NextDouble() * 0.2 - 0.1);

                        if (!gScore.ContainsKey(n) || tentativeG < gScore[n])
                        {
                            cameFrom[n] = current;
                            gScore[n] = tentativeG;
                            float f = tentativeG + Heuristic(n, goalIdx);
                            fScore[n] = f;
                            if (!openSet.Contains(n)) openSet.Add(n);
                        }
                    }
                }

                return null;
                } finally {
                    sw.Stop(); aStarCalls++; aStarMs += sw.Elapsed.TotalMilliseconds;
                }
            }

            while (riversGenerated < targetRiverCount)
            {
                if (attempts++ > 70000)
                {
                    Debug.LogWarning("[StampGen][River] excessive attempts (70000) without reaching target; aborting to avoid hang.");
                    break;
                }

                int sourceIndex = -1;
                bool sourceFromLake = false;

                // Prefer one river per lake when lake sources exist
                if (lakeSourcesDict.Count > 0 && unusedLakeIds.Count > 0)
                {
                    // Pick a random unused lake
                    int pickLakeIdx = riverRand.Next(unusedLakeIds.Count);
                    int lakeId = unusedLakeIds[pickLakeIdx];
                    var sourcesForLake = lakeSourcesDict[lakeId];

                    // Try a few random sources from this lake before giving up on the lake
                    bool foundValidSource = false;
                    int triesForLake = 0;
                    int maxTriesForLake = Math.Max(3, sourcesForLake.Count * 2);
                    while (triesForLake++ < maxTriesForLake)
                    {
                        int candidate = sourcesForLake[riverRand.Next(sourcesForLake.Count)];
                        if (!tileData.TryGetValue(candidate, out var candTile)) continue;
                        if (!candTile.isLand || candTile.isLake || candTile.isRiver) continue;
                        sourceIndex = candidate;
                        sourceFromLake = true;
                        foundValidSource = true;
                        break;
                    }

                    if (!foundValidSource)
                    {
                        // Give up on this lake for now
                        unusedLakeIds.RemoveAt(pickLakeIdx);
                        continue;
                    }
                }
                else
                {
                    // No lakes - pick a random inland source
                    if (riverSources.Count == 0)
                    {
                        Debug.LogWarning("[StampGen] No valid river sources found (after filtering).");
                        break;
                    }
                    int pick = riverRand.Next(riverSources.Count);
                    sourceIndex = riverSources[pick];
                    // Remove it to avoid trying the same failing source repeatedly
                    riverSources.RemoveAt(pick);
                }

                if (sourceIndex == -1) continue;

                // Skip sources that cannot reach a coast tile to reduce dead-ends
                if (!HasCoastPath(sourceIndex))
                {
                    // If this was a lake-sourced attempt, mark this lake as unusable to avoid repeated attempts
                    if (sourceFromLake && sourceToLakeId.TryGetValue(sourceIndex, out var badLake))
                    {
                        unusedLakeIds.Remove(badLake);
                    }
                    continue;
                }
                // STEP 2: choose a coast target on the same continent (exclude closest ~30%)
                List<int> chosenPath = null;
                int targetAttempts = 0; // record how many A* target attempts were made for diagnostics
                int sourceContinent = (tileContinent != null && sourceIndex >= 0 && sourceIndex < tileContinent.Length) ? tileContinent[sourceIndex] : GetContinentIndexForTile(sourceIndex);
                List<int> coastCandidates;
                if (tileContinent != null && coastByContinent != null)
                {
                    if (!coastByContinent.TryGetValue(sourceContinent, out coastCandidates)) coastCandidates = new List<int>();
                    else coastCandidates = new List<int>(coastCandidates);
                }
                else
                {
                    coastCandidates = new List<int>();
                    foreach (int ct in coastTiles)
                    {
                        if (sourceContinent >= 0)
                        {
                            int cidx = GetContinentIndexForTile(ct);
                            if (cidx != sourceContinent) continue;
                        }
                        coastCandidates.Add(ct);
                    }
                }

                if (coastCandidates.Count == 0)
                {
                    // No coast on same continent — mark lake unusable and skip
                    if (sourceFromLake && sourceToLakeId.TryGetValue(sourceIndex, out var badLake)) unusedLakeIds.Remove(badLake);
                    continue;
                }
                else
                {
                    var dlist = new List<(int idx, int dist)>();
                    foreach (int ct in coastCandidates) dlist.Add((ct, HexDistanceWrapped(tileCoords[sourceIndex], tileCoords[ct], tilesX)));
                    dlist.Sort((a,b) => a.dist.CompareTo(b.dist));
                    int discard = Mathf.FloorToInt(dlist.Count * 0.30f);
                    int startIdx = Mathf.Clamp(discard, 0, dlist.Count - 1);
                    var selectable = dlist.Skip(startIdx).ToList();

                    float totalW = 0f;
                    var weights = new List<float>();
                    foreach (var p in selectable)
                    {
                        float w = Mathf.Pow(Mathf.Max(1, p.dist), 1.5f);
                        weights.Add(w); totalW += w;
                    }

                    int triesTarget = 0;
                    int maxTargetTries = Mathf.Max(3, selectable.Count);
                    while (triesTarget++ < maxTargetTries && chosenPath == null && weights.Count > 0)
                    {
                        float roll = (float)(riverRand.NextDouble() * totalW);
                        int pickIdx = 0;
                        for (int i = 0; i < weights.Count; i++) { roll -= weights[i]; if (roll <= 0f) { pickIdx = i; break; } }
                        int targetIdx = selectable[pickIdx].idx;

                        var pathFound = FindPathAStar(sourceIndex, targetIdx, riverRand);
                        if (pathFound != null && pathFound.Count >= 3)
                        {
                            chosenPath = pathFound;
                            targetAttempts = triesTarget;
                            break;
                        }
                        else
                        {
                            totalW -= weights[pickIdx];
                            weights.RemoveAt(pickIdx);
                            selectable.RemoveAt(pickIdx);
                            if (weights.Count == 0) break;
                        }
                    }

                    if (chosenPath == null)
                    {
                        // A* failed for all targets — give up on this source (do not fallback to greedy)
                        if (sourceFromLake && sourceToLakeId.TryGetValue(sourceIndex, out var badLake)) unusedLakeIds.Remove(badLake);
                        continue;
                    }
                }

                List<int> path = chosenPath;
                if (path == null || path.Count <= 1)
                {
                    // Failed to build a usable path; if from lake, try other sources from same lake next loop
                    continue;
                }

                // Per-river diagnostics: source, lakeId (if any), target, hexDistance, path length, A* attempts
                int pathTargetIdx = path[path.Count - 1];
                int chosenLakeId = sourceToLakeId.TryGetValue(sourceIndex, out var lid) ? lid : -1;
                int hexDist = HexDistanceWrapped(tileCoords[sourceIndex], tileCoords[pathTargetIdx], tilesX);
                Debug.Log($"[StampGen][River] src={sourceIndex} lake={chosenLakeId} tgt={pathTargetIdx} hexDist={hexDist} pathLen={path.Count} aStarAttempts={targetAttempts} result=SUCCESS");

                // Apply river tiles (do NOT include termination tiles)
                riversGenerated++;
                foreach (int tileIdx in path)
                {
                    if (!tileData.TryGetValue(tileIdx, out var td)) continue;
                    if (td.isLake || td.isRiver) continue;
                    if (td.biome == Biome.Coast || td.biome == Biome.Ocean || td.biome == Biome.Seas) continue;

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

                if (sourceFromLake && sourceToLakeId.TryGetValue(sourceIndex, out var usedLake))
                {
                    // Mark lake used and remove from unused list
                    usedLakeIds.Add(usedLake);
                    unusedLakeIds.Remove(usedLake);
                }

                if (loadingPanelController != null && riversGenerated % 5 == 0)
                {
                    loadingPanelController.SetProgress(0.75f + (float)riversGenerated / Mathf.Max(1, targetRiverCount) * 0.2f);
                    loadingPanelController.SetStatus($"Carving rivers... ({riversGenerated}/{targetRiverCount})");
                }
                yield return null;
            }

            Debug.Log($"[StampGen] Rivers generated: {riversGenerated}");
            float riverGenerationElapsed = Time.realtimeSinceStartup - riverGenerationStart;
            Debug.Log($"[StampGen][Timing] rivers={riversGenerated} totalMs={riverGenerationElapsed * 1000f:F1} aStarCalls={aStarCalls} aStarTotalMs={aStarMs:F1} aStarAvgMs={(aStarCalls>0? aStarMs / aStarCalls:0):F2} bfsCalls={bfsCalls} bfsTotalMs={bfsMs:F1}");
        }

        // Old greedy river walk removed — A* pathfinder is now authoritative. Do not use BuildRiverWalk.

        // PickWeightedNeighbor removed — A* is now the only river routing method.

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
    private List<ContinentData> GenerateContinentData(
        int count,
        int rndSeed,
        int mapWidthTiles,
        int mapHeightTiles,
        int minContinentWidth,
        int maxContinentWidth,
        int minContinentHeight,
        int maxContinentHeight,
        int minDistanceTiles
    ) {
        var continents = new List<ContinentData>();
        if (count <= 0) return continents;

        System.Random rand = new System.Random(rndSeed);
        int minW = Mathf.Max(1, minContinentWidth);
        int maxW = Mathf.Max(minW, maxContinentWidth);
        int minH = Mathf.Max(1, minContinentHeight);
        int maxH = Mathf.Max(minH, maxContinentHeight);
        int minDistance = Mathf.Max(0, minDistanceTiles);
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

            Debug.Assert(
                chosenWidthTiles < mapWidthTiles && chosenHeightTiles < mapHeightTiles,
                $"[StampGen][ERROR] Continent size {chosenWidthTiles}x{chosenHeightTiles} >= map size {mapWidthTiles}x{mapHeightTiles}"
            );

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
