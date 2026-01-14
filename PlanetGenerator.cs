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


    [Header("Map Settings")] 
    public bool randomSeed = true;
    public int seed = 12345;
    // Spherical radius removed in flat-only refactor

    // Public property to access the seed
    public int Seed => seed;

    // --- New Continent Parameters (Method 2: Masked Noise + Guaranteed Core) ---
    [Header("Continent Generation (Deterministic Masked Noise)")]
    [Tooltip("The target number of continents. Placement is deterministic for common counts (1-8). Higher counts might revert to random spread.")]
    [Min(1)]
    public int numberOfContinents = 6;


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
    [Range(0.2f, 0.8f)]
    [Tooltip("Noise threshold for filling land *around* the guaranteed core within masks. Lower = more land.")]
    public float landThreshold = 0.4f;

    [Tooltip("Frequency multiplier for the continent noise function.")]
    public float continentNoiseFrequency = 20f;

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
    
    // --- NEW: Continent Shape Parameters ---
    [Header("Continent Shape (Fractal)")]
    [Range(0.1f, 0.5f)]
    [Tooltip("Inner radius (normalized) where land is guaranteed.")]
    public float continentInnerRadius = 0.35f;
    [Range(0.6f, 1.2f)]
    [Tooltip("Outer radius (normalized) where land fades to ocean.")]
    public float continentOuterRadius = 1.0f;
    [Range(0f, 0.6f)]
    [Tooltip("Amplitude of macro noise that shapes continent edges.")]
    public float continentMacroAmplitude = 0.35f;
    [Range(0f, 0.3f)]
    [Tooltip("Amplitude of coastline warp for fractal edges.")]
    public float coastlineWarpAmplitude = 0.12f;
    [Range(0.3f, 0.7f)]
    [Tooltip("Land cutoff threshold after combining falloff and noise.")]
    public float landCutoff = 0.5f;
    
    // --- NEW: Advanced Fractal Settings ---
    [Header("Advanced Fractal Settings")]
    [Range(0f, 0.5f)]
    [Tooltip("Domain warp amplitude for organic continent shapes. Higher = more flowing, less blobby.")]
    public float continentDomainWarp = 0.25f;
    
    [Range(0f, 0.4f)]
    [Tooltip("Fine-scale coastline warp amplitude. Adds small inlets and detail.")]
    public float coastlineFineWarp = 0.08f;
    
    [Range(0f, 0.5f)]
    [Tooltip("Voronoi influence on continents. Creates natural clustering/separation.")]
    public float voronoiContinentInfluence = 0.0f; // disabled by default - Voronoi removed per request
    
    [Range(0f, 0.5f)]
    [Tooltip("Voronoi influence on elevation. Adds natural cellular variation.")]
    public float voronoiElevationInfluence = 0.12f;

    [Header("Experimental / Advanced")]
    [Tooltip("Enable Voronoi-based continent/elevation features. Controlled per-prefab.")]
    public bool useVoronoiContinents = false;
    
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
    [Range(0.3f, 0.7f)]
    [Tooltip("Land cutoff for islands (similar to landCutoff but for islands)")]
    public float islandThreshold = 0.45f;
    [Range(0.1f, 1.0f)]
    [Tooltip("Noise frequency multiplier for islands (relative to continent frequency)")]
    public float islandNoiseFrequency = 1.8f;
    [Range(0.1f, 0.5f)]
    [Tooltip("Inner radius for island falloff (guaranteed land core).")]
    public float islandInnerRadius = 0.25f;
    [Range(0.5f, 1.2f)]
    [Tooltip("Outer radius for island falloff.")]
    public float islandOuterRadius = 0.9f;
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
    /// Generates the planet's surface with continents, oceans, and biomes.
    /// Uses continuous land field with fractal coastlines (not threshold noise).
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
        float mapWidth = grid.MapWidth;
        float mapHeight = grid.MapHeight;
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
            Debug.Log($"[PrefabTuning] mapSize={GameSetupData.mapSize} contTiles(WxH) min={cMinW}x{cMinH} max={cMaxW}x{cMaxH} islandTiles(WxH) min={iMinW}x{iMinH} max={iMaxW}x{iMaxH}");
            Debug.Log($"[PrefabTuning] landCutoff={landCutoff} continentNoiseFreq={continentNoiseFrequency} continentMacroAmplitude={continentMacroAmplitude} continentDomainWarp={continentDomainWarp} coastlineWarpAmplitude={coastlineWarpAmplitude} coastlineFineWarp={coastlineFineWarp} voronoiContinentInfluence={voronoiContinentInfluence} voronoiElevationInfluence={voronoiElevationInfluence}");
            Debug.Log($"[PrefabTuning] islandNoiseFrequency={islandNoiseFrequency} islandInnerRadius={islandInnerRadius} islandOuterRadius={islandOuterRadius} islandThreshold={islandThreshold}");
            Debug.Log($"[Setup] mapSize={GameSetupData.mapSize} contCount={GameSetupData.numberOfContinents} islandCount={GameSetupData.numberOfIslands} generateIslands={GameSetupData.generateIslands} landThreshold={GameSetupData.landThreshold} seedVariance={GameSetupData.seedPositionVariance}");
            Debug.Log($"[PlanetGenerator][Diag] numberOfContinents={numberOfContinents} continentTiles(WxH) min={cMinW}x{cMinH} max={cMaxW}x{cMaxH} islandTiles(WxH) min={iMinW}x{iMinH} max={iMaxW}x{iMaxH} seedPositionVariance={seedPositionVariance}");
            Debug.Log($"[PlanetGenerator][Diag] landCutoff={landCutoff} continentMacroAmplitude={continentMacroAmplitude} continentDomainWarp={continentDomainWarp} voronoiContinentInfluence={voronoiContinentInfluence}");
            Debug.Log($"[PlanetGenerator][Diag] generateIslands={generateIslands} numberOfIslands={numberOfIslands} islandNoiseFrequency={islandNoiseFrequency} islandInnerRadius={islandInnerRadius} islandOuterRadius={islandOuterRadius}");
            Debug.Log($"[PlanetGenerator][Diag] latitudeInfluence={latitudeInfluence} latitudeExponent={latitudeExponent} temperatureBias={temperatureBias} moistureBias={moistureBias}");

            // Tile center Z bounds (latitude mapping)
            if (grid != null && grid.tileCenters != null && grid.tileCenters.Length > 0)
            {
                float minZ = float.MaxValue, maxZ = float.MinValue;
                for (int ti = 0; ti < grid.tileCenters.Length; ti++)
                {
                    float z = grid.tileCenters[ti].z;
                    if (z < minZ) minZ = z;
                    if (z > maxZ) maxZ = z;
                }
                Debug.Log($"[PlanetGenerator][Diag] tileCenters z range: minZ={minZ:F3} maxZ={maxZ:F3}");
            }
        }
        
        // Calculate noise frequencies based on map size
        float continentMacroFreq = 1f / (mapWidth * 0.6f);
        float coastlineDetailFreq = continentMacroFreq * 4f;
        float elevationBroadFreq = continentMacroFreq * 1.2f;
        float elevationRidgedFreq = continentMacroFreq * 2.0f;
        
        // ---------- 2. Generate Deterministic Continent Seeds with Per-Continent Sizes ------------------
        List<ContinentData> continentDataList = GenerateContinentData(numberOfContinents, seed ^ 0xD00D);
        
        // Log continent generation info
        Debug.Log($"[PlanetGenerator] Generated {continentDataList.Count} continents with varied sizes");

        // Additional diagnostics: compare GameSetupData => generator fields and inspect pole tile indices
        if (enableDiagnostics)
        {
            try {
                Debug.Log($"[PlanetGenerator][Diag] GameSetupData.numberOfContinents={GameSetupData.numberOfContinents}, GameSetupData.numberOfIslands={GameSetupData.numberOfIslands}, GameSetupData.generateIslands={GameSetupData.generateIslands}");
                Debug.Log($"[PlanetGenerator][Diag] Generator fields: numberOfContinents={numberOfContinents}, numberOfIslands={numberOfIslands}, generateIslands={generateIslands}, landCutoff={landCutoff}, landThreshold={landThreshold}");
                Debug.Log($"[PlanetGenerator][Diag] Continent seeds count={continentDataList.Count}");

                // print continent seed summary
                for (int ci = 0; ci < continentDataList.Count; ci++) {
                    var c = continentDataList[ci];
                    Debug.Log($"[PlanetGenerator][Diag] Continent[{ci}] pos={c.position} width={c.widthWorld:F1} height={c.heightWorld:F1} rotDeg={(c.rotation*Mathf.Rad2Deg):F0} sizeScale={c.sizeScale:F2}");
                }

                // Additional per-continent noise/warp sampling to diagnose how noise affects effective size
                for (int ci = 0; ci < continentDataList.Count; ci++) {
                    var c = continentDataList[ci];
                    float halfW = c.widthWorld * 0.5f;
                    float halfH = c.heightWorld * 0.5f;
                    Vector2 centerPos = c.position;
                    // sample macro noise at center
                    float centerMacro = (continentDomainWarp > 0.01f)
                        ? noise.GetWarpedContinentPeriodic(centerPos, mapWidth, mapHeight, continentMacroFreq, continentDomainWarp * 0.5f)
                        : noise.GetContinentPeriodic(centerPos, mapWidth, mapHeight, continentMacroFreq);
                    // sample macro noise at edge along +X axis
                    Vector2 edgePos = centerPos + new Vector2(halfW, 0f);
                    if (edgePos.x > mapWidth * 0.5f) edgePos.x -= mapWidth;
                    float edgeMacro = noise.GetContinentPeriodic(edgePos, mapWidth, mapHeight, continentMacroFreq);
                    // sample a warped macro at the edge using coast warp
                    float edgeWarpAmp = coastlineWarpAmplitude;
                    Vector2 coastWarp = noise.GetCoastWarpPeriodic(edgePos, mapWidth, mapHeight, coastlineDetailFreq, edgeWarpAmp);
                    Vector2 warpedSamplePos = edgePos + coastWarp * halfW;
                    float warpedEdgeMacro = noise.GetContinentPeriodic(warpedSamplePos, mapWidth, mapHeight, continentMacroFreq);

                    Debug.Log($"[PlanetGenerator][DiagSample] Continent[{ci}] centerMacro={centerMacro:F3} edgeMacro={edgeMacro:F3} warpedEdgeMacro={warpedEdgeMacro:F3} halfW={halfW:F1} halfH={halfH:F1}");
                }

                // find min/max Z tile indices for pole checking
                if (grid != null && grid.tileCenters != null && grid.tileCenters.Length > 0)
                {
                    int minIdx = 0, maxIdx = 0;
                    float minZ = grid.tileCenters[0].z, maxZ = grid.tileCenters[0].z;
                    for (int ti = 1; ti < grid.tileCenters.Length; ti++) {
                        float z = grid.tileCenters[ti].z;
                        if (z < minZ) { minZ = z; minIdx = ti; }
                        if (z > maxZ) { maxZ = z; maxIdx = ti; }
                    }
                    float mapH = Mathf.Max(0.001f, grid.MapHeight);
                    float northSouthMin = Mathf.Clamp(grid.tileCenters[minIdx].z / (mapH * 0.5f), -1f, 1f);
                    float northSouthMax = Mathf.Clamp(grid.tileCenters[maxIdx].z / (mapH * 0.5f), -1f, 1f);
                    Debug.Log($"[PlanetGenerator][Diag] minZIndex={minIdx} z={minZ:F3} northSouth={northSouthMin:F3}");
                    Debug.Log($"[PlanetGenerator][Diag] maxZIndex={maxIdx} z={maxZ:F3} northSouth={northSouthMax:F3}");
                }
            } catch (System.Exception ex) {
                Debug.LogError($"[PlanetGenerator][Diag] diagnostics failed: {ex.Message}");
            }
        }

        // ---------- 3. Pre-calculate tile positions and continent masks --------
        Dictionary<int, bool> isLandTile = new Dictionary<int, bool>();
        Dictionary<int, Vector2> tilePositions = new Dictionary<int, Vector2>();
        Dictionary<int, float> tileLandValues = new Dictionary<int, float>(); // Store continuous land values

        // Pre-calculate tile positions for all tiles (flat-only)
        for (int i = 0; i < tileCount; i++) {
            Vector3 center = (grid != null && grid.tileCenters != null && i < grid.tileCenters.Length)
                ? grid.tileCenters[i]
                : Vector3.zero;
            tilePositions[i] = new Vector2(center.x, center.z);
        }

        if (loadingPanelController != null)
        {
            loadingPanelController.SetProgress(0.05f);
            loadingPanelController.SetStatus("Analyzing continent positions...");
        }
        yield return null;

        // ---------- 4. Generate Land using Advanced Fractal Land Field ---------------
        // Enhanced with:
        // - Domain warping for organic continent shapes
        // - Multi-octave cascaded coastline warping
        // - Voronoi influence for natural clustering
        // - Elliptical falloff with fractal edges
        
        // Pre-calculate Voronoi influence field (for continent clustering)
        float voronoiFreq = 0f;
        if (useVoronoiContinents)
            voronoiFreq = 1f / (mapWidth * 0.25f);  // Low frequency for large cells
        
        // Diagnostic counters for contribution stages
        int diag_baseCount = 0;
        int diag_macroAdds = 0;
        int diag_warpAdds = 0;
        int diag_voronoiAdds = 0;
        int continentCount = continentDataList.Count;
        int[] continentAllowedCounts = new int[continentCount];
        int[] continentLandInMaskCounts = new int[continentCount];
        int[] continentLandOutsideMaskCounts = new int[continentCount];
        int continentLandCount = 0;
        int continentLandOutsideMaskCount = 0;

        for (int i = 0; i < tileCount; i++) {
            isLandTile[i] = false;
            tileLandValues[i] = 0f;
            Vector2 tilePos = tilePositions[i];

            // DEBUG: Log pre-noise/warp position for first tile of each continent (for clarity)
            if (i < numberOfContinents) {
                Debug.Log($"[ContinentGen][PreNoise] tile {i} pos={tilePos}");
            }

            // Apply domain warping to the tile position for organic shapes
            Vector2 warpedTilePos = tilePos;
            if (continentDomainWarp > 0.01f) {
                warpedTilePos = noise.GetDomainWarpedPosition(tilePos, mapWidth, mapHeight,
                    continentDomainWarp * mapWidth * 0.15f,  // Large-scale warp
                    continentDomainWarp * mapWidth * 0.05f); // Fine-scale warp
            }

            // DEBUG: Log post-warp position for first tile of each continent
            if (i < numberOfContinents) {
                Debug.Log($"[ContinentGen][PostWarp] tile {i} warpedPos={warpedTilePos}");
            }

            // Get Voronoi influence for this tile (creates natural continent clustering)
            float voronoiValue = 0f;
            if (useVoronoiContinents && voronoiContinentInfluence > 0.01f) {
                voronoiValue = noise.GetVoronoiPeriodic(tilePos, mapWidth, mapHeight, voronoiFreq);
            }

            // Track max land values for each stage so we can compute per-stage contribution diagnostics
            float maxBaseFalloff = 0f;              // falloff only
            float maxMacroUnwarped = 0f;            // falloff + (macroNoise - 0.5)*amp (unwarped sample)
            float maxFinalMacro = 0f;               // falloff + (finalMacro - 0.5)*amp (after warp blend)
            float maxVoronoiApplied = 0f;           // final value after voronoi modulation
            float maxLandValue = 0f;
            int winningContinentIndex = -1;
            bool winningInsideMask = false;
            bool tileInAnyContinentMask = false;

            for (int ci = 0; ci < continentDataList.Count; ci++) {
                ContinentData continent = continentDataList[ci];
                bool insideMask = IsInsideContinentMask(tilePos, continent, mapWidth, continentOuterRadius);
                if (insideMask) {
                    tileInAnyContinentMask = true;
                    continentAllowedCounts[ci]++;
                } else {
                    continue;
                }
                // Calculate wrapped distance to seed (using warped position)
                float dx = warpedTilePos.x - continent.position.x;
                // Wrap X distance
                if (Mathf.Abs(dx) > mapWidth * 0.5f) {
                    dx = dx > 0 ? dx - mapWidth : dx + mapWidth;
                }
                float dz = warpedTilePos.y - continent.position.y;

                // Apply per-continent rotation to create varied orientations
                float cosR = Mathf.Cos(continent.rotation);
                float sinR = Mathf.Sin(continent.rotation);
                float rotatedDx = dx * cosR - dz * sinR;
                float rotatedDz = dx * sinR + dz * cosR;

                // Use per-continent size (randomized width/height)
                float halfWidth = continent.widthWorld * 0.5f;
                float halfHeight = continent.heightWorld * 0.5f;
                float xNorm = rotatedDx / Mathf.Max(0.001f, halfWidth);
                float zNorm = rotatedDz / Mathf.Max(0.001f, halfHeight);
                float normDist = Mathf.Sqrt(xNorm * xNorm + zNorm * zNorm);

                // (a) Continent falloff - guarantees solid core
                float falloff = 1f - NoiseSampler.SmoothStep(continentInnerRadius, continentOuterRadius, normDist);

                // (b) Multi-scale domain-warped macro noise
                float macroNoise;
                if (continentDomainWarp > 0.01f) {
                    macroNoise = noise.GetWarpedContinentPeriodic(tilePos, mapWidth, mapHeight, 
                        continentMacroFreq, continentDomainWarp * 0.5f);
                } else {
                    macroNoise = noise.GetContinentPeriodic(tilePos, mapWidth, mapHeight, continentMacroFreq);
                }

                // (c) Multi-octave cascaded coastline warping for fractal edges
                float edgeFactor = NoiseSampler.SmoothStep(0.3f, 0.8f, normDist); // Only warp near edges
                Vector2 warp;
                if (coastlineFineWarp > 0.01f) {
                    // Use cascaded multi-scale warp
                    warp = noise.GetCascadedCoastWarp(tilePos, mapWidth, mapHeight,
                        coastlineDetailFreq, coastlineDetailFreq * 3f,  // Coarse and fine frequencies
                        coastlineWarpAmplitude * edgeFactor,            // Coarse amplitude
                        coastlineFineWarp * edgeFactor);                // Fine amplitude
                } else {
                    // Fallback to simple warp
                    warp = noise.GetCoastWarpPeriodic(tilePos, mapWidth, mapHeight, 
                        coastlineDetailFreq, coastlineWarpAmplitude * edgeFactor);
                }


                // Apply warp to sample position for the macro noise
                Vector2 warpedPos = tilePos + warp * halfWidth;
                float warpedMacro = noise.GetContinentPeriodic(warpedPos, mapWidth, mapHeight, continentMacroFreq);

                // Blend macro noise based on edge proximity
                float finalMacro = Mathf.Lerp(macroNoise, warpedMacro, edgeFactor * 0.7f);

                // Combine: falloff + noise contribution
                float landValue_unwarpedMacro = falloff + (macroNoise - 0.5f) * continentMacroAmplitude;
                float landValue_warpedMacro = falloff + (warpedMacro - 0.5f) * continentMacroAmplitude;
                float landValue = falloff + (finalMacro - 0.5f) * continentMacroAmplitude;

                // Apply Voronoi modulation (creates natural gaps and clustering)
                float landValue_beforeVoronoi = landValue;
                if (useVoronoiContinents && voronoiContinentInfluence > 0.01f) {
                    // Voronoi creates natural breaks between continents
                    float voronoiMod = Mathf.Lerp(1f, 0.5f + voronoiValue * 0.5f, voronoiContinentInfluence);
                    landValue = landValue * voronoiMod;
                }

                // Track stage maxes (we want the maximum influence any continent seed gives for each stage)
                if (falloff > maxBaseFalloff) maxBaseFalloff = falloff;
                if (landValue_unwarpedMacro > maxMacroUnwarped) maxMacroUnwarped = landValue_unwarpedMacro;
                if (landValue_warpedMacro > maxFinalMacro) maxFinalMacro = landValue_warpedMacro;
                if (landValue_beforeVoronoi > maxFinalMacro) maxFinalMacro = landValue_beforeVoronoi; // ensure blended value considered
                if (landValue > maxVoronoiApplied) maxVoronoiApplied = landValue;

                // Track the highest land value from any seed (for overlapping continents)
                if (landValue > maxLandValue) {
                    maxLandValue = landValue;
                    winningContinentIndex = ci;
                    winningInsideMask = insideMask;
                }
            }

            // Store continuous land value
            tileLandValues[i] = tileInAnyContinentMask ? maxLandValue : 0f;

            // --- Diagnostic: evaluate per-stage land decisions for contribution counts
            bool baseIsLand = tileInAnyContinentMask && maxBaseFalloff > landCutoff;
            bool macroIsLand = tileInAnyContinentMask && maxMacroUnwarped > landCutoff;
            bool finalMacroIsLand = tileInAnyContinentMask && maxFinalMacro > landCutoff;
            bool voronoiIsLand = tileInAnyContinentMask && maxVoronoiApplied > landCutoff;

            if (baseIsLand) diag_baseCount++;
            if (macroIsLand && !baseIsLand) diag_macroAdds++;
            if (finalMacroIsLand && !macroIsLand) diag_warpAdds++;
            if (voronoiIsLand && !finalMacroIsLand) diag_voronoiAdds++;

            if (voronoiIsLand) {
                isLandTile[i] = true;
                landTilesGenerated++;
                continentLandCount++;
                if (winningContinentIndex >= 0) {
                    if (winningInsideMask) {
                        continentLandInMaskCounts[winningContinentIndex]++;
                    } else {
                        continentLandOutsideMaskCounts[winningContinentIndex]++;
                        continentLandOutsideMaskCount++;
                    }
                } else {
                    continentLandOutsideMaskCount++;
                }
            } else {
                isLandTile[i] = false;
            }

            // BATCH YIELD
            if (i > 0 && i % 500 == 0)
            {
                if (loadingPanelController != null)
                {
                    loadingPanelController.SetProgress(0.1f + (float)i / tileCount * 0.1f);
                    loadingPanelController.SetStatus("Raising continents...");
                }
                yield return null;
            }
        }

        // ---------- Diagnostic: per-stage contribution summary ----------
        Debug.Log($"[PlanetGenerator][DiagContrib] baseline={diag_baseCount} macroAdded={diag_macroAdds} warpAdded={diag_warpAdds} voronoiAdded={diag_voronoiAdds} totalLandAfterAllStages={landTilesGenerated}");
        if (enableDiagnostics) {
            for (int ci = 0; ci < continentDataList.Count; ci++) {
                var c = continentDataList[ci];
                float halfW = c.widthWorld * 0.5f;
                float halfH = c.heightWorld * 0.5f;
                Debug.Log($"[ContinentMask] c={ci} tiles={c.widthTiles}x{c.heightTiles} halfW={halfW:F1} halfH={halfH:F1} allowed={continentAllowedCounts[ci]} landInMask={continentLandInMaskCounts[ci]} landOutMask={continentLandOutsideMaskCounts[ci]}");
            }
            Debug.Log($"[ContinentMaskSummary] landOutsideMask={continentLandOutsideMaskCount}");
        }

        // ---------- 4.5. Generate Islands (NEW) ---------
        if (allowIslands)
            yield return StartCoroutine(GenerateIslands(isLandTile, tilePositions, tileLandValues, tileCount));

        int totalLand = 0;
        for (int i = 0; i < tileCount; i++) {
            if (isLandTile[i]) totalLand++;
        }
        int islandLand = Mathf.Max(0, totalLand - continentLandCount);
        int totalOcean = tileCount - totalLand;
        Debug.Log($"[LandSummary] totalLand={totalLand} landFromContinents={continentLandCount} landFromIslands={islandLand} totalOcean={totalOcean}");

        // ---------- 5. Calculate Biomes, Elevation, and Initial Data ---------
        if (!allowOceans)
        {
            for (int i = 0; i < tileCount; i++) isLandTile[i] = true;
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
        float elevVoronoiFreq = 1f / (mapWidth * 0.35f);

        for (int i = 0; i < tileCount; i++)
        {
            Vector3 c = (grid != null && grid.tileCenters != null && i < grid.tileCenters.Length)
                ? grid.tileCenters[i]
                : Vector3.zero;
            bool isLand = isLandTile[i];
            Vector2 tilePos = tilePositions[i];
            Vector3 noisePoint = new Vector3(c.x, 0f, c.z) + noiseOffset;
            
            // Calculate elevation using advanced multi-noise blending (seamless wrap)
            // Combines: FBm (base), Ridged (mountains), Billow (hills), Voronoi (variation)
            float noiseElevation;
            // Only enable the Voronoi path if the prefab flag is set. Billow hills may still trigger advanced elevation.
            if (billowHillWeight > 0.01f || (useVoronoiContinents && voronoiElevationInfluence > 0.01f)) {
                // Use advanced elevation with appropriate Voronoi contribution (zeroed if flag disabled)
                float voronoiParam = (useVoronoiContinents ? voronoiElevationInfluence : 0f);
                noiseElevation = noise.GetAdvancedElevationPeriodic(tilePos, mapWidth, mapHeight,
                    elevBroadFreq, elevRidgedFreq, elevBillowFreq, elevVoronoiFreq,
                    ridgedMountainWeight, billowHillWeight, voronoiParam);
            } else {
                // Fallback to simple elevation
                noiseElevation = noise.GetElevationPeriodic(tilePos, mapWidth, mapHeight, 
                    elevBroadFreq, elevRidgedFreq, ridgedMountainWeight);
            }

            // Calculate Moisture & Temperature (needed for biome determination)
            float moisture = noise.GetMoisture(noisePoint * moistureFreq);
            // Apply moisture bias - clamp to ensure it stays in 0-1 range
            moisture = Mathf.Clamp01(moisture + moistureBias);
            
            float northness = Mathf.Clamp(c.z / (mapHeight * 0.5f), -1f, 1f);
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

            if (isLand) {
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

            // Consider tiles that are NOT land as initial water bodies (Glaciers are now treated as land)
            if (!data[i].isLand) {
                 waterTiles.Add(i);
                 continue; 
            }

            bool hasWaterNeighbor = false;
            foreach (int nIdx in grid.neighbors[i]) {
                // A neighbor is water if it's in the waterTiles set OR it's an ocean/sea/glacier
                if (waterTiles.Contains(nIdx) || (data.ContainsKey(nIdx) && !data[nIdx].isLand)) {
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

        int landNow = data.Values.Count(d => d.isLand && d.biome != Biome.Glacier);

        // ---------- 6.5 River Generation Pass (MOVED TO AFTER COAST/SEAS) ----
        if (enableRivers)
            yield return StartCoroutine(GenerateRivers(isLandTile, data));

        // ---------- 6.6 Lake Generation Pass (Earth only) ----
        // Lakes only generate on Earth-like planets, not on other celestial bodies
        bool isEarthPlanet = !isMarsWorldType && !isVenusWorldType && !isMercuryWorldType && !isJupiterWorldType &&
                            !isSaturnWorldType && !isUranusWorldType && !isNeptuneWorldType && !isPlutoWorldType &&
                            !isTitanWorldType && !isEuropaWorldType && !isIoWorldType && !isGanymedeWorldType &&
                            !isCallistoWorldType && !isLunaWorldType;
        
        if (enableLakes && isEarthPlanet)
            yield return StartCoroutine(GenerateLakes(isLandTile, data));

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
        IEnumerator GenerateRivers(Dictionary<int, bool> isLandTile, Dictionary<int, HexTileData> tileData)
        {
            System.Random riverRand = new System.Random(unchecked((int)(seed ^ 0xBADF00D)));
            HashSet<int> riverTiles = new HashSet<int>();
            int actualRiverCount = 0;
            int totalAttempts = 0;
            const int MAX_TOTAL_ATTEMPTS = 500; // Limit attempts to prevent infinite loops

            // Build set of protected tiles (Snow/Glacier)
            HashSet<int> protectedBiomeTiles = new HashSet<int>();
            List<int> validLandTiles = new List<int>();
            List<int> coastTilesList = new List<int>();

            foreach (var kvp in tileData) {
                int idx = kvp.Key;
                HexTileData td = kvp.Value;
                if (td.biome == Biome.Snow || td.biome == Biome.Glacier) {
                    protectedBiomeTiles.Add(idx);
                    continue; // Skip protected tiles
                }
                if (td.biome == Biome.Coast) {
                    coastTilesList.Add(idx);
                }
                // Valid land tiles for starting rivers: land, not coast, not protected
                if (td.isLand && td.biome != Biome.Coast && td.biome != Biome.River) {
                    // Further check: ensure it's not directly adjacent to water/coast
                    bool adjacentToWater = false;
                    foreach (int neighbor in grid.neighbors[idx]) {
                        if (tileData.ContainsKey(neighbor) &&
                            (!tileData[neighbor].isLand || tileData[neighbor].biome == Biome.Coast)) {
                            adjacentToWater = true;
                            break;
                        }
                    }
                    if (!adjacentToWater) {
                        validLandTiles.Add(idx);
                    }
                }
            }

            if (validLandTiles.Count == 0) {
                Debug.LogWarning("No valid interior land tiles found to start rivers.");
                yield break;
            }
            if (coastTilesList.Count == 0) {
                Debug.LogWarning("No coast tiles found to end rivers.");
                yield break;
            }

            int targetRiverCount = riverRand.Next(minRiversPerContinent, maxRiversPerContinent + 1) * 7; // Estimate based on avg continents
            // Double river count for map types with 'Rivers' in the name (case-insensitive)
            if (!string.IsNullOrEmpty(currentMapTypeName) && currentMapTypeName.IndexOf("Rivers", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                targetRiverCount *= 2;
            }
            targetRiverCount = Mathf.Clamp(targetRiverCount, 0, 50); // Cap total rivers
            

            while (actualRiverCount < targetRiverCount && totalAttempts < MAX_TOTAL_ATTEMPTS)
            {
                totalAttempts++;
                if (validLandTiles.Count == 0) break; // No more potential start points

                // 1. Pick random valid starting land tile
                int startTileIndex = validLandTiles[riverRand.Next(validLandTiles.Count)];

                // Double check it hasn't become river/coast/protected somehow or adjacent to river
                                        bool startIsAdjacentToRiver = false;
                        foreach (int neighbor in grid.neighbors[startTileIndex]) {
                    if (riverTiles.Contains(neighbor)) {
                        startIsAdjacentToRiver = true;
                        break;
                    }
                }
                if (riverTiles.Contains(startTileIndex) || protectedBiomeTiles.Contains(startTileIndex) ||
                    tileData[startTileIndex].biome == Biome.Coast || startIsAdjacentToRiver)
                {
                    validLandTiles.Remove(startTileIndex); // Remove invalid start point
                    continue;
                }

                // 2. Find path using greedy descent
                List<int> path = FindPathGreedy(startTileIndex, coastTilesList, tileData, riverTiles, protectedBiomeTiles);

                // 3. Validate the path
                if (path == null || path.Count < minRiverLength || path.Count > maxRiverPathLength) {
                    continue; // Path invalid (null, too short, or too long)
                }

                // 4. Path is valid - commit the river
                actualRiverCount++;

                // Process all tiles in the path except the last one (coast tile)
                for (int i = 0; i < path.Count; i++) {
                    int tileIdx = path[i];
                    riverTiles.Add(tileIdx); // Add to river set

                    var td = tileData[tileIdx];

                    // Keep the coast tile as a coast for the river mouth
                    if (i == path.Count - 1 && td.biome == Biome.Coast) {
                        continue;
                    }

                    td.biome = Biome.River;
                    td.isLand = true; // Rivers are land
                    td.isHill = false; // Rivers can't be hills
                    tileData[tileIdx] = td;
                    baseData[tileIdx] = td; // Update base state
                }

                // Remove start tile from valid list as it's now a river
                validLandTiles.Remove(startTileIndex);

                // BATCH YIELD
                if (totalAttempts % 20 == 0)
                {
                    if (loadingPanelController != null)
                    {
                        loadingPanelController.SetProgress(0.75f + (float)actualRiverCount / Mathf.Max(1, targetRiverCount) * 0.2f); // 75% to 95%
                        loadingPanelController.SetStatus($"Carving rivers... ({actualRiverCount}/{targetRiverCount})");
                    }
                    yield return null;
                }
            }

        }

        // --------------------------- Lake Generation ----------------------------
        IEnumerator GenerateLakes(Dictionary<int, bool> isLandTile, Dictionary<int, HexTileData> tileData)
        {
            System.Random lakeRand = new System.Random(unchecked((int)(seed ^ 0x1A4E)));
            HashSet<int> lakeTiles = new HashSet<int>();
            int actualLakeCount = 0;
            
            // Build list of potential lake seed tiles (low elevation inland areas)
            List<int> potentialLakeSeeds = new List<int>();
            HashSet<int> protectedBiomeTiles = new HashSet<int>();
            
            // Find the elevation range for land tiles
            float minLandElev = float.MaxValue;
            float maxLandElev = float.MinValue;
            
            foreach (var kvp in tileData) {
                int idx = kvp.Key;
                HexTileData td = kvp.Value;
                
                // Skip protected biomes
                if (td.biome == Biome.Snow || td.biome == Biome.Glacier || 
                    td.biome == Biome.Coast || td.biome == Biome.Ocean || 
                    td.biome == Biome.Seas || td.biome == Biome.River ||
                    td.biome == Biome.Mountain) {
                    protectedBiomeTiles.Add(idx);
                    continue;
                }
                
                if (td.isLand && tileElevation.ContainsKey(idx)) {
                    float elev = tileElevation[idx];
                    if (elev < minLandElev) minLandElev = elev;
                    if (elev > maxLandElev) maxLandElev = elev;
                }
            }
            
            // Normalize threshold relative to land elevation range
            float elevRange = maxLandElev - minLandElev;
            float absThreshold = minLandElev + elevRange * lakeElevationThreshold;
            
            // Find low-elevation inland tiles suitable for lakes
            foreach (var kvp in tileData) {
                int idx = kvp.Key;
                HexTileData td = kvp.Value;
                
                if (protectedBiomeTiles.Contains(idx)) continue;
                if (!td.isLand) continue;
                if (!tileElevation.ContainsKey(idx)) continue;
                
                float elev = tileElevation[idx];
                
                // Check if this is a low-elevation tile (valley/depression)
                if (elev > absThreshold) continue;
                
                // Ensure it's inland (not adjacent to coast/ocean/seas)
                bool isInland = true;
                foreach (int neighbor in grid.neighbors[idx]) {
                    if (tileData.ContainsKey(neighbor)) {
                        Biome nb = tileData[neighbor].biome;
                        if (nb == Biome.Coast || nb == Biome.Ocean || nb == Biome.Seas) {
                            isInland = false;
                            break;
                        }
                    }
                }
                
                if (isInland) {
                    potentialLakeSeeds.Add(idx);
                }
            }
            
            if (potentialLakeSeeds.Count == 0) {
                Debug.Log("[PlanetGenerator] No suitable low-elevation inland areas found for lakes.");
                yield break;
            }
            
            Debug.Log($"[PlanetGenerator] Found {potentialLakeSeeds.Count} potential lake seed locations");
            
            // Generate lakes
            int targetLakes = numberOfLakes;
            int attempts = 0;
            const int MAX_ATTEMPTS = 200;
            
            while (actualLakeCount < targetLakes && attempts < MAX_ATTEMPTS && potentialLakeSeeds.Count > 0)
            {
                attempts++;
                
                // Pick a random seed tile
                int seedIndex = lakeRand.Next(potentialLakeSeeds.Count);
                int seedTile = potentialLakeSeeds[seedIndex];
                potentialLakeSeeds.RemoveAt(seedIndex);
                
                // Skip if already part of a lake or river
                if (lakeTiles.Contains(seedTile) || tileData[seedTile].biome == Biome.River) continue;
                
                // Grow the lake from this seed using flood fill
                int targetSize = lakeRand.Next(minLakeSize, maxLakeSize + 1);
                List<int> lakeBody = GrowLake(seedTile, targetSize, tileData, lakeTiles, protectedBiomeTiles, lakeRand);
                
                // Only accept lakes that meet minimum size
                if (lakeBody.Count >= minLakeSize) {
                    actualLakeCount++;
                    
                    foreach (int tileIdx in lakeBody) {
                        lakeTiles.Add(tileIdx);
                        
                        var td = tileData[tileIdx];
                        td.biome = Biome.Lake;
                        td.isLand = false; // Lakes are water (for naval units)
                        td.isHill = false;
                        td.elevation = 0.02f; // Slightly above sea level
                        td.renderElevation = 0.05f;
                        tileData[tileIdx] = td;
                        baseData[tileIdx] = td;
                        
                        // Remove from potential seeds
                        potentialLakeSeeds.Remove(tileIdx);
                    }
                    
                    Debug.Log($"[PlanetGenerator] Created lake #{actualLakeCount} with {lakeBody.Count} tiles");
                }
                
                // BATCH YIELD
                if (attempts % 10 == 0)
                {
                    if (loadingPanelController != null)
                    {
                        loadingPanelController.SetProgress(0.92f + (float)actualLakeCount / Mathf.Max(1, targetLakes) * 0.03f);
                        loadingPanelController.SetStatus($"Forming lakes... ({actualLakeCount}/{targetLakes})");
                    }
                    yield return null;
                }
            }
            
            // Connect rivers to lakes if enabled
            if (connectRiversToLakes && lakeTiles.Count > 0)
            {
                yield return StartCoroutine(ConnectRiversToLakes(tileData, lakeTiles));
            }
            
            Debug.Log($"[PlanetGenerator] Lake generation complete: {actualLakeCount} lakes created");
        }
        
        /// <summary>
        /// Grows a lake from a seed tile using flood fill, preferring low elevation neighbors
        /// </summary>
        List<int> GrowLake(int seedTile, int targetSize, Dictionary<int, HexTileData> tileData, 
                                   HashSet<int> existingLakes, HashSet<int> protectedTiles, System.Random rand)
        {
            List<int> lake = new List<int> { seedTile };
            HashSet<int> lakeSet = new HashSet<int> { seedTile };
            List<int> frontier = new List<int>();
            
            // Add seed's neighbors to frontier
            foreach (int n in grid.neighbors[seedTile]) {
                if (!lakeSet.Contains(n) && !existingLakes.Contains(n) && !protectedTiles.Contains(n)) {
                    if (tileData.ContainsKey(n) && tileData[n].isLand && tileData[n].biome != Biome.River) {
                        frontier.Add(n);
                    }
                }
            }
            
            while (lake.Count < targetSize && frontier.Count > 0)
            {
                // Sort frontier by elevation (prefer lower tiles for natural lake shape)
                frontier.Sort((a, b) => {
                    float elevA = tileElevation.ContainsKey(a) ? tileElevation[a] : 1f;
                    float elevB = tileElevation.ContainsKey(b) ? tileElevation[b] : 1f;
                    return elevA.CompareTo(elevB);
                });
                
                // Pick from the lowest elevation candidates (with some randomness)
                int pickRange = Mathf.Min(3, frontier.Count);
                int pickIndex = rand.Next(pickRange);
                int nextTile = frontier[pickIndex];
                frontier.RemoveAt(pickIndex);
                
                // Skip if already in lake or protected
                if (lakeSet.Contains(nextTile) || existingLakes.Contains(nextTile) || protectedTiles.Contains(nextTile)) continue;
                if (!tileData.ContainsKey(nextTile) || !tileData[nextTile].isLand) continue;
                if (tileData[nextTile].biome == Biome.River) continue;
                
                // Add to lake
                lake.Add(nextTile);
                lakeSet.Add(nextTile);
                
                // Add new neighbors to frontier
                foreach (int n in grid.neighbors[nextTile]) {
                    if (!lakeSet.Contains(n) && !existingLakes.Contains(n) && !protectedTiles.Contains(n) && !frontier.Contains(n)) {
                        if (tileData.ContainsKey(n) && tileData[n].isLand && tileData[n].biome != Biome.River) {
                            frontier.Add(n);
                        }
                    }
                }
            }
            
            return lake;
        }
        
        /// <summary>
        /// Connects existing rivers to nearby lakes by extending rivers to lake shores
        /// </summary>
        IEnumerator ConnectRiversToLakes(Dictionary<int, HexTileData> tileData, HashSet<int> lakeTiles)
        {
            // Find river endpoints (river tiles adjacent to non-river land)
            List<int> riverEndpoints = new List<int>();
            HashSet<int> riverTiles = new HashSet<int>();
            
            foreach (var kvp in tileData) {
                if (kvp.Value.biome == Biome.River) {
                    riverTiles.Add(kvp.Key);
                }
            }
            
            foreach (int riverTile in riverTiles) {
                // Check if this river tile is adjacent to non-river land (potential endpoint)
                bool hasNonRiverLandNeighbor = false;
                int riverNeighborCount = 0;
                
                foreach (int n in grid.neighbors[riverTile]) {
                    if (tileData.ContainsKey(n)) {
                        if (tileData[n].biome == Biome.River || riverTiles.Contains(n)) {
                            riverNeighborCount++;
                        } else if (tileData[n].isLand && tileData[n].biome != Biome.Coast && 
                                   tileData[n].biome != Biome.Ocean && tileData[n].biome != Biome.Lake) {
                            hasNonRiverLandNeighbor = true;
                        }
                    }
                }
                
                // If river tile has fewer than 2 river neighbors, it's likely an endpoint
                if (riverNeighborCount <= 1 && hasNonRiverLandNeighbor) {
                    riverEndpoints.Add(riverTile);
                }
            }
            
            Debug.Log($"[PlanetGenerator] Found {riverEndpoints.Count} river endpoints to potentially connect to lakes");
            
            // Try to connect river endpoints to nearby lakes
            int connectionsCreated = 0;
            System.Random connectRand = new System.Random(unchecked((int)(seed ^ 0xC0EC7)));
            
            foreach (int endpoint in riverEndpoints) {
                // Find nearest lake tile within reasonable distance
                int nearestLake = -1;
                int nearestDist = int.MaxValue;
                const int MAX_CONNECTION_DIST = 8;
                
                // BFS to find nearest lake
                Queue<(int tile, int dist)> queue = new Queue<(int, int)>();
                HashSet<int> visited = new HashSet<int>();
                queue.Enqueue((endpoint, 0));
                visited.Add(endpoint);
                
                while (queue.Count > 0 && nearestLake < 0) {
                    var (current, dist) = queue.Dequeue();
                    if (dist > MAX_CONNECTION_DIST) break;
                    
                    foreach (int n in grid.neighbors[current]) {
                        if (visited.Contains(n)) continue;
                        visited.Add(n);
                        
                        if (lakeTiles.Contains(n)) {
                            nearestLake = n;
                            nearestDist = dist + 1;
                            break;
                        }
                        
                        if (tileData.ContainsKey(n) && tileData[n].isLand && 
                            tileData[n].biome != Biome.Mountain && tileData[n].biome != Biome.Coast) {
                            queue.Enqueue((n, dist + 1));
                        }
                    }
                }
                
                // If found a nearby lake, create river connection
                if (nearestLake >= 0 && nearestDist <= MAX_CONNECTION_DIST && connectRand.NextDouble() < 0.7) {
                    // Use simple pathfinding to create river path
                    List<int> connectionPath = FindPathToLake(endpoint, nearestLake, tileData, lakeTiles, riverTiles);
                    
                    if (connectionPath != null && connectionPath.Count > 0) {
                        foreach (int pathTile in connectionPath) {
                            if (!lakeTiles.Contains(pathTile) && !riverTiles.Contains(pathTile)) {
                                var td = tileData[pathTile];
                                td.biome = Biome.River;
                                td.isLand = true;
                                td.isHill = false;
                                tileData[pathTile] = td;
                                baseData[pathTile] = td;
                                riverTiles.Add(pathTile);
                            }
                        }
                        connectionsCreated++;
                    }
                }
            }
            
            Debug.Log($"[PlanetGenerator] Created {connectionsCreated} river-to-lake connections");
            yield return null;
        }
        
        /// <summary>
        /// Finds a path from a river endpoint to a lake
        /// </summary>
        List<int> FindPathToLake(int start, int lakeTile, Dictionary<int, HexTileData> tileData, 
                                         HashSet<int> lakeTiles, HashSet<int> riverTiles)
        {
            // A* pathfinding with elevation preference (rivers flow downhill)
            var openSet = new SortedDictionary<float, List<int>>();
            var cameFrom = new Dictionary<int, int>();
            var gScore = new Dictionary<int, float> { [start] = 0 };
            
            float startH = HeuristicToLake(start, lakeTile);
            if (!openSet.ContainsKey(startH)) openSet[startH] = new List<int>();
            openSet[startH].Add(start);
            
            HashSet<int> closedSet = new HashSet<int>();
            
            while (openSet.Count > 0) {
                // Get tile with lowest f-score
                var firstKey = openSet.Keys.First();
                int current = openSet[firstKey][0];
                openSet[firstKey].RemoveAt(0);
                if (openSet[firstKey].Count == 0) openSet.Remove(firstKey);
                
                // Found the lake!
                if (lakeTiles.Contains(current)) {
                    // Reconstruct path
                    List<int> path = new List<int>();
                    int temp = current;
                    while (cameFrom.ContainsKey(temp)) {
                        if (!lakeTiles.Contains(temp) && !riverTiles.Contains(temp)) {
                            path.Add(temp);
                        }
                        temp = cameFrom[temp];
                    }
                    path.Reverse();
                    return path;
                }
                
                closedSet.Add(current);
                
                foreach (int neighbor in grid.neighbors[current]) {
                    if (closedSet.Contains(neighbor)) continue;
                    if (!tileData.ContainsKey(neighbor)) continue;
                    
                    var td = tileData[neighbor];
                    
                    // Allow traversing to lake tiles
                    if (lakeTiles.Contains(neighbor)) {
                        float tentativeG = gScore[current] + 1;
                        if (!gScore.ContainsKey(neighbor) || tentativeG < gScore[neighbor]) {
                            cameFrom[neighbor] = current;
                            gScore[neighbor] = tentativeG;
                            float f = tentativeG + HeuristicToLake(neighbor, lakeTile);
                            if (!openSet.ContainsKey(f)) openSet[f] = new List<int>();
                            openSet[f].Add(neighbor);
                        }
                        continue;
                    }
                    
                    // Skip non-traversable tiles
                    if (!td.isLand) continue;
                    if (td.biome == Biome.Mountain || td.biome == Biome.Coast || 
                        td.biome == Biome.Ocean || td.biome == Biome.Glacier) continue;
                    
                    float moveCost = 1f;
                    // Prefer lower elevation (rivers flow downhill)
                    if (tileElevation.ContainsKey(neighbor) && tileElevation.ContainsKey(current)) {
                        float elevDiff = tileElevation[neighbor] - tileElevation[current];
                        if (elevDiff < 0) moveCost *= 0.5f; // Cheaper to go downhill
                        else if (elevDiff > 0) moveCost *= 2f; // More expensive uphill
                    }
                    
                    float tentativeG2 = gScore[current] + moveCost;
                    if (!gScore.ContainsKey(neighbor) || tentativeG2 < gScore[neighbor]) {
                        cameFrom[neighbor] = current;
                        gScore[neighbor] = tentativeG2;
                        float f = tentativeG2 + HeuristicToLake(neighbor, lakeTile);
                        if (!openSet.ContainsKey(f)) openSet[f] = new List<int>();
                        openSet[f].Add(neighbor);
                    }
                }
            }
            
            return null; // No path found
        }
        
        float HeuristicToLake(int from, int to)
        {
            // Simple distance heuristic based on tile positions
            if (from >= 0 && from < grid.tileCenters.Length && to >= 0 && to < grid.tileCenters.Length) {
                return Vector3.Distance(grid.tileCenters[from], grid.tileCenters[to]);
            }
            return 100f; // Fallback
        }

        // --------------------------- Helper Functions ----------------------------

        IEnumerator GenerateIslands(Dictionary<int, bool> isLandTile, Dictionary<int, Vector2> tilePositions, 
                           Dictionary<int, float> tileLandValues, int tileCount) {
            
            // Use tile-based island parameters
            GameManager.GetFlatTileResolution(GameSetupData.mapSize, out int tilesX, out int tilesZ);
            int iminW = minIslandWidthTilesStandard, imaxW = maxIslandWidthTilesStandard;
            int iminH = minIslandHeightTilesStandard, imaxH = maxIslandHeightTilesStandard;
            switch (GameSetupData.mapSize)
            {
                case GameManager.MapSize.Small:
                    iminW = minIslandWidthTilesSmall; imaxW = maxIslandWidthTilesSmall;
                    iminH = minIslandHeightTilesSmall; imaxH = maxIslandHeightTilesSmall;
                    break;
                case GameManager.MapSize.Large:
                    iminW = minIslandWidthTilesLarge; imaxW = maxIslandWidthTilesLarge;
                    iminH = minIslandHeightTilesLarge; imaxH = maxIslandHeightTilesLarge;
                    break;
            }
            // Clamp island tile maxima to tile resolution
            int clampedIminW = Mathf.Clamp(iminW, 1, tilesX);
            int clampedImaxW = Mathf.Clamp(imaxW, clampedIminW, tilesX);
            int clampedIminH = Mathf.Clamp(iminH, 1, tilesZ);
            int clampedImaxH = Mathf.Clamp(imaxH, clampedIminH, tilesZ);
            if (enableDiagnostics && (clampedImaxW != imaxW || clampedImaxH != imaxH || clampedIminW != iminW || clampedIminH != iminH)) {
                Debug.Log($"[IslandSize] Clamped island tiles from W={iminW}-{imaxW},H={iminH}-{imaxH} to W={clampedIminW}-{clampedImaxW},H={clampedIminH}-{clampedImaxH} based on tilesX={tilesX},tilesZ={tilesZ}");
            }
            float islandMacroFreq = 1f / (mapWidth * 0.6f) * islandNoiseFrequency; // Higher freq than continents
            float islandCoastFreq = islandMacroFreq * 3f;
            
            int localIslandTilesGenerated = 0;
            
            // Generate island seeds - either as chains or scattered
            List<Vector2> islandSeeds;
            if (generateIslandChains)
            {
                islandSeeds = GenerateIslandChainSeeds(numberOfIslands, seed ^ 0xF15, islandsPerChain);
            }
            else
            {
                islandSeeds = GenerateIslandSeeds(numberOfIslands, seed ^ 0xF15);
            }
            
            if (numberOfIslands > 0 && islandSeeds.Count == 0) {
                // Fallback: guarantee at least one seed unless explicitly disabled
                islandSeeds.Add(new Vector2(0f, 0f));
                Debug.LogWarning("[PlanetGenerator] Island seed placement failed; falling back to a central island seed.");
            }
            Debug.Log($"[PlanetGenerator] Generating {islandSeeds.Count} island seeds (chains={generateIslandChains})");

            // Assign per-island sizes based on prefab tile ranges for active map size
            var islandDataList = new List<IslandData>(islandSeeds.Count);
            System.Random islandSizeRand = new System.Random(seed ^ 0x1A51);
            foreach (var seedPos in islandSeeds) {
                int chosenWidthTiles = islandSizeRand.Next(clampedIminW, clampedImaxW + 1);
                int chosenHeightTiles = islandSizeRand.Next(clampedIminH, clampedImaxH + 1);
                float widthWorld = (chosenWidthTiles / (float)Mathf.Max(1, tilesX)) * mapWidth;
                float heightWorld = (chosenHeightTiles / (float)Mathf.Max(1, tilesZ)) * mapHeight;
                islandDataList.Add(new IslandData {
                    position = seedPos,
                    widthWorld = widthWorld,
                    heightWorld = heightWorld,
                    widthTiles = chosenWidthTiles,
                    heightTiles = chosenHeightTiles
                });
                if (enableDiagnostics) {
                    Debug.Log($"[IslandSize] seed={seedPos} tiles={chosenWidthTiles}x{chosenHeightTiles} widthWorld={widthWorld:F1} heightWorld={heightWorld:F1}");
                }
            }
            
            if (loadingPanelController != null)
            {
                loadingPanelController.SetProgress(0.2f);
                loadingPanelController.SetStatus("Finding island locations...");
            }
            yield return null;
            
            // Generate island land tiles using radial falloff + noise (like continents but smaller)
            for (int i = 0; i < tileCount; i++) {
                // Skip tiles that are already land (from continents)
                if (isLandTile[i]) continue;
                
                Vector2 tilePos = tilePositions[i];
                float maxIslandValue = 0f;
                bool tileInAnyIslandMask = false;
                
                foreach (var island in islandDataList) {
                    // Calculate wrapped distance to island seed
                    float normDist = GetNormalizedIslandDistance(tilePos, island, mapWidth);
                    bool insideMask = normDist <= islandOuterRadius;
                    if (!insideMask) {
                        continue;
                    }
                    tileInAnyIslandMask = true;
                    
                    // Island falloff - same approach as continents but with island-specific radii
                    float falloff = 1f - NoiseSampler.SmoothStep(islandInnerRadius, islandOuterRadius, normDist);
                    
                    // Island noise (higher frequency than continents)
                    float islandNoise = noise.GetIslandNoisePeriodic(tilePos, mapWidth, mapHeight, islandMacroFreq);
                    
                    // Coastline detail for the island
                    float edgeFactor = NoiseSampler.SmoothStep(0.2f, 0.7f, normDist);
                    float coastNoise = noise.GetCoastPeriodic(tilePos, mapWidth, mapHeight, islandCoastFreq);
                    
                    // Combine: falloff + noise (slightly more noise influence for varied island shapes)
                    float islandValue = falloff + (islandNoise - 0.5f) * 0.4f + (coastNoise - 0.5f) * 0.15f * edgeFactor;
                    
                    if (islandValue > maxIslandValue) {
                        maxIslandValue = islandValue;
                    }
                }
                
                // Decision rule: becomes island land if value exceeds island threshold
                if (tileInAnyIslandMask && maxIslandValue > islandThreshold) {
                    isLandTile[i] = true;
                    tileLandValues[i] = maxIslandValue; // Store for potential use
                    localIslandTilesGenerated++;
                }

                // BATCH YIELD
                if (i > 0 && i % 1000 == 0)
                {
                    if (loadingPanelController != null)
                    {
                        loadingPanelController.SetProgress(0.2f + (float)i / tileCount * 0.1f);
                        loadingPanelController.SetStatus("Raising islands...");
                    }
                    yield return null;
                }
            }
            
            Debug.Log($"[PlanetGenerator] Generated {localIslandTilesGenerated} island tiles");
            this.landTilesGenerated += localIslandTilesGenerated;
        }
    
        /// <summary>
        /// Generate island seeds as chains/clusters for more natural-looking archipelagos.
        /// </summary>
        List<Vector2> GenerateIslandChainSeeds(int totalCount, int rndSeed, int islandsPerChain) {
            List<Vector2> seeds = new List<Vector2>();
            System.Random rand = new System.Random(rndSeed);
            float mapWidth = grid.MapWidth;
            float mapHeight = grid.MapHeight;
            
            int numChains = Mathf.Max(1, totalCount / Mathf.Max(1, islandsPerChain));
            // Use tile-based spacing heuristics
            GameManager.GetFlatTileResolution(GameSetupData.mapSize, out int tilesX2, out int tilesZ2);
            int chainIslandMaxW = maxIslandWidthTilesStandard;
            switch (GameSetupData.mapSize)
            {
                case GameManager.MapSize.Small: chainIslandMaxW = maxIslandWidthTilesSmall; break;
                case GameManager.MapSize.Large: chainIslandMaxW = maxIslandWidthTilesLarge; break;
            }
            float minDistanceBetweenChains = (40f / 360f) * mapWidth; // keep a modest chain spacing
            float islandSpacing = (chainIslandMaxW / (float)Mathf.Max(1, tilesX2) * 1.2f) * mapWidth;
            
            List<Vector2> chainStarts = new List<Vector2>();
            int attempts = 0;
            int maxAttempts = numChains * 20;
            
            // First, place chain starting points away from continents
            List<Vector2> continentSeeds = GenerateDeterministicSeeds(numberOfContinents, seed ^ 0xD00D);
            // Compute a conservative continent width world estimate using configured continent tile maxima
            GameManager.GetFlatTileResolution(GameSetupData.mapSize, out int tilesXC, out int tilesZC);
            int cMaxW = maxContinentWidthTilesStandard;
            switch (GameSetupData.mapSize)
            {
                case GameManager.MapSize.Small: cMaxW = maxContinentWidthTilesSmall; break;
                case GameManager.MapSize.Large: cMaxW = maxContinentWidthTilesLarge; break;
            }
            // Clamp the configured continent tile max to the tile resolution to avoid absurd world sizes
            int clampedCMaxW = Mathf.Clamp(cMaxW, 1, tilesXC);
            if (enableDiagnostics && clampedCMaxW != cMaxW) Debug.Log($"[IslandSeeds] Clamped cMaxW from {cMaxW} to {clampedCMaxW} based on tilesXC={tilesXC}");
            float maxContinentWidthWorld = (clampedCMaxW / (float)Mathf.Max(1, tilesXC)) * mapWidth;
            if (enableDiagnostics) Debug.Log($"[IslandSeeds] maxContinentWidthWorld={maxContinentWidthWorld:F1} (clampedCMaxW={clampedCMaxW})");
            
            while (chainStarts.Count < numChains && attempts < maxAttempts) {
                attempts++;
                float x = (float)(rand.NextDouble() * mapWidth - mapWidth * 0.5f);
                float z = (float)(rand.NextDouble() * mapHeight * 0.8f - mapHeight * 0.4f); // Avoid polar regions
                Vector2 candidate = new Vector2(x, z);
                
                bool tooClose = false;
                
                // Check distance from continents
                foreach (var cSeed in continentSeeds) {
                    if (WrappedDistance(candidate, cSeed, mapWidth) < maxContinentWidthWorld * 0.6f) {
                        tooClose = true;
                        break;
                    }
                }
                
                // Check distance from other chain starts
                if (!tooClose) {
                    foreach (var cs in chainStarts) {
                        if (WrappedDistance(candidate, cs, mapWidth) < minDistanceBetweenChains) {
                            tooClose = true;
                            break;
                        }
                    }
                }
                
                if (!tooClose) {
                    chainStarts.Add(candidate);
                }
            }
            
            // Now generate islands along each chain
            foreach (var chainStart in chainStarts) {
                // Random chain direction
                float angle = (float)(rand.NextDouble() * Mathf.PI * 2f);
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                
                for (int i = 0; i < islandsPerChain && seeds.Count < totalCount; i++) {
                    // Position along chain with some jitter
                    float dist = i * islandSpacing;
                    float jitterX = (float)(rand.NextDouble() - 0.5f) * islandSpacing * 0.5f;
                    float jitterZ = (float)(rand.NextDouble() - 0.5f) * islandSpacing * 0.5f;
                    
                    Vector2 islandPos = chainStart + direction * dist + new Vector2(jitterX, jitterZ);
                    
                    // Wrap X position
                    if (islandPos.x > mapWidth * 0.5f) islandPos.x -= mapWidth;
                    if (islandPos.x < -mapWidth * 0.5f) islandPos.x += mapWidth;
                    
                    // Clamp Z to stay on map
                    islandPos.y = Mathf.Clamp(islandPos.y, -mapHeight * 0.45f, mapHeight * 0.45f);
                    
                    seeds.Add(islandPos);
                }
            }
            
            return seeds;
        }
        
        /// <summary>
        /// Generates seed positions for islands using a more random approach than continents
        /// </summary>
        List<Vector2> GenerateIslandSeeds(int count, int rndSeed) {
            List<Vector2> seeds = new List<Vector2>();
            System.Random rand = new System.Random(rndSeed);
            float mapWidth = grid.MapWidth;
            float mapHeight = grid.MapHeight;
            
            // Islands use a more scattered, random placement
            int attempts = 0;
            int maxAttempts = count * 10; // Limit attempts to prevent infinite loops
            float minDistanceBetweenIslands = (20f / 360f) * mapWidth;
            
            while (seeds.Count < count && attempts < maxAttempts) {
                attempts++;
                
                // Generate random position on the flat map
                float x = (float)(rand.NextDouble() * mapWidth - mapWidth * 0.5f);
                float z = (float)(rand.NextDouble() * mapHeight - mapHeight * 0.5f);
                Vector2 candidate = new Vector2(x, z);
                
                // Check distance from existing seeds (both continents and islands)
                bool tooClose = false;
                
                // Check distance from continent seeds
                foreach (var continentSeed in GenerateDeterministicSeeds(numberOfContinents, seed ^ 0xD00D)) {
                    if (WrappedDistance(candidate, continentSeed, mapWidth) < minDistanceBetweenIslands * 2f) {
                        tooClose = true;
                        break;
                    }
                }
                
                // Check distance from other island seeds
                if (!tooClose) {
                    foreach (var islandSeed in seeds) {
                        if (WrappedDistance(candidate, islandSeed, mapWidth) < minDistanceBetweenIslands) {
                            tooClose = true;
                            break;
                        }
                    }
                }
                
                if (!tooClose) {
                    // Apply small random offset
                    float offsetRangeX = (seedPositionVariance * 0.5f / 360f) * mapWidth;
                    float offsetRangeZ = (seedPositionVariance * 0.5f / 180f) * mapHeight;
                    float offsetX = (float)(rand.NextDouble() * offsetRangeX * 2 - offsetRangeX);
                    float offsetZ = (float)(rand.NextDouble() * offsetRangeZ * 2 - offsetRangeZ);
                    candidate = new Vector2(candidate.x + offsetX, candidate.y + offsetZ);
                    
                    seeds.Add(candidate);
                }
            }
            
            if (seeds.Count < count) {
                Debug.LogWarning($"Could only generate {seeds.Count} island seeds out of requested {count} after {attempts} attempts.");
            }
            
            return seeds;
        }
        
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
        // Update land status based on biome (e.g., setting to River makes it technically not 'land' for some checks)
        td.isLand = (newBiome != Biome.Ocean && newBiome != Biome.Seas && newBiome != Biome.Coast && newBiome != Biome.River && newBiome != Biome.Glacier);
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
            Vector3 tileCenter = grid.tileCenters[tileIndex];
            float mapW = Mathf.Max(0.001f, grid.MapWidth);
            float mapH = Mathf.Max(0.001f, grid.MapHeight);
            northSouth = Mathf.Clamp(tileCenter.z / (mapH * 0.5f), -1f, 1f);
            eastWest = Mathf.Clamp(tileCenter.x / (mapW * 0.5f), -1f, 1f);
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
    /// Finds a path from start tile to coast using greedy descent algorithm
    /// </summary>
    private List<int> FindPathGreedy(int startTileIndex, List<int> coastTiles, Dictionary<int, HexTileData> tileData, HashSet<int> existingRiverTiles, HashSet<int> protectedTiles)
    {

        List<int> path = new List<int>();
        path.Add(startTileIndex);
        HashSet<int> pathSet = new HashSet<int> { startTileIndex };
        
        int currentTileIndex = startTileIndex;

        for(int i = 0; i < maxRiverPathLength; i++) {
            var neighbors = grid.neighbors[currentTileIndex];
            int bestNeighbor = -1;
            float minElevation = float.MaxValue;

            // Find the lowest neighbor that isn't already in the path or another river
            foreach(int neighborIndex in neighbors) {
                if (!pathSet.Contains(neighborIndex) && !existingRiverTiles.Contains(neighborIndex) && !protectedTiles.Contains(neighborIndex) && tileData.ContainsKey(neighborIndex)) {
                    float neighborElevation = GetTileElevation(neighborIndex);
                    if (neighborElevation < minElevation) {
                        minElevation = neighborElevation;
                        bestNeighbor = neighborIndex;
                    }
                }
            }

            if (bestNeighbor != -1) {
                path.Add(bestNeighbor);
                pathSet.Add(bestNeighbor);
                currentTileIndex = bestNeighbor;

                // If we hit a coast tile, we're done
                if (coastTiles.Contains(currentTileIndex)) {
                    return path;
                }
            } else {
                // No valid downward path found
                return null;
            }
        }
        return null; // Path too long
    }

    /// <summary>
    /// Generates optimized biome mask textures with improved quality and performance
    /// </summary>

    // --- Per-Continent Data Structure ---
    /// <summary>
    /// Holds per-continent data for varied size/rotation per continent
    /// </summary>
    private struct ContinentData {
        public Vector2 position;       // Seed position
        public float widthWorld;       // Width in world units (randomized)
        public float heightWorld;      // Height in world units (randomized)
        public int widthTiles;         // Width in tiles (randomized)
        public int heightTiles;        // Height in tiles (randomized)
        public float rotation;         // Rotation angle in radians
        public float sizeScale;        // Overall size scale (0.4 to 1.0)
    }

    private struct IslandData {
        public Vector2 position;
        public float widthWorld;
        public float heightWorld;
        public int widthTiles;
        public int heightTiles;
    }

    // --- Helper methods moved to class scope ---
    /// <summary>
    /// Generate continent seeds with per-continent randomized sizes and rotations.
    /// Returns both positions and per-continent size data.
    /// </summary>
    private List<ContinentData> GenerateContinentData(int count, int rndSeed) {
        List<ContinentData> continents = new List<ContinentData>();
        System.Random rand = new System.Random(rndSeed);
        float mapWidthLocal = grid.MapWidth;
        float mapHeightLocal = grid.MapHeight;
        float offsetRangeX = (seedPositionVariance / 360f) * mapWidthLocal;
        float offsetRangeZ = (seedPositionVariance / 180f) * mapHeightLocal;
        
        // Determine tile resolution for the selected map size
        GameManager.GetFlatTileResolution(GameSetupData.mapSize, out int tilesX, out int tilesZ);

        int minW = minContinentWidthTilesStandard, maxW = maxContinentWidthTilesStandard;
        int minH = minContinentHeightTilesStandard, maxH = maxContinentHeightTilesStandard;
        switch (GameSetupData.mapSize)
        {
            case GameManager.MapSize.Small:
                minW = minContinentWidthTilesSmall; maxW = maxContinentWidthTilesSmall;
                minH = minContinentHeightTilesSmall; maxH = maxContinentHeightTilesSmall;
                break;
            case GameManager.MapSize.Large:
                minW = minContinentWidthTilesLarge; maxW = maxContinentWidthTilesLarge;
                minH = minContinentHeightTilesLarge; maxH = maxContinentHeightTilesLarge;
                break;
            case GameManager.MapSize.Standard:
            default:
                minW = minContinentWidthTilesStandard; maxW = maxContinentWidthTilesStandard;
                minH = minContinentHeightTilesStandard; maxH = maxContinentHeightTilesStandard;
                break;
        }

        System.Func<Vector2, ContinentData> createContinent = (Vector2 basePos) => {
            float offsetX = (float)(rand.NextDouble() * offsetRangeX * 2 - offsetRangeX);
            float offsetZ = (float)(rand.NextDouble() * offsetRangeZ * 2 - offsetRangeZ);
            
            // Random rotation: 0 to 2π (creates differently oriented continents)
            float rotation = (float)(rand.NextDouble() * Mathf.PI * 2f);

            // Select tile counts per-continent so each continent varies independently
            int chosenWidthTiles = rand.Next(Mathf.Max(1, minW), Mathf.Max(1, maxW) + 1);
            int chosenHeightTiles = rand.Next(Mathf.Max(1, minH), Mathf.Max(1, maxH) + 1);

            // DEBUG: report chosen tile counts before clamping
            if (enableDiagnostics) {
                Debug.Log($"[ContinentSize] chosenTiles pre-clamp W={chosenWidthTiles} H={chosenHeightTiles} (minW={minW} maxW={maxW} minH={minH} maxH={maxH}) tilesX={tilesX} tilesZ={tilesZ}");
            }

            // Clamp chosen sizes to the available tile resolution to avoid producing world sizes > map dimensions
            chosenWidthTiles = Mathf.Clamp(chosenWidthTiles, 1, tilesX);
            chosenHeightTiles = Mathf.Clamp(chosenHeightTiles, 1, tilesZ);

            // DEBUG: report chosen tile counts after clamping
            if (enableDiagnostics) {
                Debug.Log($"[ContinentSize] chosenTiles post-clamp W={chosenWidthTiles} H={chosenHeightTiles}");
            }

            float tilesXF = Mathf.Max(1, tilesX);
            float tilesZF = Mathf.Max(1, tilesZ);
            float widthWorld = (chosenWidthTiles / tilesXF) * mapWidthLocal;
            float heightWorld = (chosenHeightTiles / tilesZF) * mapHeightLocal;

            if (enableDiagnostics) {
                Debug.Log($"[ContinentSize] widthWorld={widthWorld:F1} heightWorld={heightWorld:F1} (tiles W={chosenWidthTiles}, H={chosenHeightTiles})");
            }

            return new ContinentData {
                position = new Vector2(basePos.x + offsetX, basePos.y + offsetZ),
                widthWorld = widthWorld,
                heightWorld = heightWorld,
                widthTiles = chosenWidthTiles,
                heightTiles = chosenHeightTiles,
                rotation = rotation,
                sizeScale = 1f
            };
        };

        if (count <= 0) return continents;

        float halfWidth = mapWidthLocal * 0.5f;
        float halfHeight = mapHeightLocal * 0.5f;
        Vector2 center = Vector2.zero;
        Vector2 north = new Vector2(0f, halfHeight * 0.6f);
        Vector2 south = new Vector2(0f, -halfHeight * 0.6f);
        Vector2 east = new Vector2(halfWidth * 0.6f, 0f);
        Vector2 west = new Vector2(-halfWidth * 0.6f, 0f);
        Vector2 northeast = new Vector2(halfWidth * 0.4f, halfHeight * 0.4f);
        Vector2 northwest = new Vector2(-halfWidth * 0.4f, halfHeight * 0.4f);
        Vector2 southeast = new Vector2(halfWidth * 0.4f, -halfHeight * 0.4f);
        Vector2 southwest = new Vector2(-halfWidth * 0.4f, -halfHeight * 0.4f);

        continents.Add(createContinent(center));
        if (count == 1) return continents;
        continents.Add(createContinent(north));
        if (count == 2) return continents;
        continents.Add(createContinent(south));
        if (count == 3) return continents;
        continents.Add(createContinent(east));
        if (count == 4) return continents;
        continents.Add(createContinent(west));
        if (count == 5) return continents;
        continents.Add(createContinent(northeast));
        if (count == 6) return continents;
        continents.Add(createContinent(northwest));
        if (count == 7) return continents;
        continents.Add(createContinent(southeast));
        if (count == 8) return continents;
        continents.Add(createContinent(southwest));
        if (count == 9) return continents;

        int guard = 0;
        int maxTries = 5000;
        float minDistance = (30f / 360f) * mapWidthLocal;
        while (continents.Count < count && guard < maxTries) {
            guard++;
            Vector2 candidate = new Vector2(
                (float)(rand.NextDouble() * mapWidthLocal - halfWidth),
                (float)(rand.NextDouble() * mapHeightLocal - halfHeight)
            );
            bool ok = true;
            foreach (var c in continents) {
                if (WrappedDistance(candidate, c.position, mapWidthLocal) < minDistance) { ok = false; break; }
            }
            if (ok) continents.Add(createContinent(candidate));
        }
        
        // Log continent sizes for debugging
        for (int i = 0; i < continents.Count; i++) {
            var c = continents[i];
            Debug.Log($"[PlanetGenerator] Continent {i}: pos={c.position}, tiles={c.widthTiles}x{c.heightTiles}, size={c.widthWorld:F1}x{c.heightWorld:F1}, rot={c.rotation * Mathf.Rad2Deg:F0}°, scale={c.sizeScale:F2}");
        }
        
        return continents;
    }
    
    /// <summary>
    /// Legacy method for backward compatibility - extracts just positions
    /// </summary>
    private List<Vector2> GenerateDeterministicSeeds(int count, int rndSeed) {
        var continentData = GenerateContinentData(count, rndSeed);
        var seeds = new List<Vector2>();
        foreach (var c in continentData) {
            seeds.Add(c.position);
        }
        return seeds;
    }

    private float GetNormalizedContinentDistance(Vector2 tilePos, ContinentData continent, float mapWidth) {
        float dx = tilePos.x - continent.position.x;
        if (Mathf.Abs(dx) > mapWidth * 0.5f) {
            dx = dx > 0 ? dx - mapWidth : dx + mapWidth;
        }
        float dz = tilePos.y - continent.position.y;

        float cosR = Mathf.Cos(continent.rotation);
        float sinR = Mathf.Sin(continent.rotation);
        float rotatedDx = dx * cosR - dz * sinR;
        float rotatedDz = dx * sinR + dz * cosR;

        float halfWidth = continent.widthWorld * 0.5f;
        float halfHeight = continent.heightWorld * 0.5f;
        float xNorm = rotatedDx / Mathf.Max(0.001f, halfWidth);
        float zNorm = rotatedDz / Mathf.Max(0.001f, halfHeight);
        return Mathf.Sqrt(xNorm * xNorm + zNorm * zNorm);
    }

    private bool IsInsideContinentMask(Vector2 tilePos, ContinentData continent, float mapWidth, float maskRadius) {
        return GetNormalizedContinentDistance(tilePos, continent, mapWidth) <= maskRadius;
    }

    private float GetNormalizedIslandDistance(Vector2 tilePos, IslandData island, float mapWidth) {
        float dx = tilePos.x - island.position.x;
        if (Mathf.Abs(dx) > mapWidth * 0.5f) {
            dx = dx > 0 ? dx - mapWidth : dx + mapWidth;
        }
        float dz = tilePos.y - island.position.y;
        float halfWidth = island.widthWorld * 0.5f;
        float halfHeight = island.heightWorld * 0.5f;
        float xNorm = dx / Mathf.Max(0.001f, halfWidth);
        float zNorm = dz / Mathf.Max(0.001f, halfHeight);
        return Mathf.Sqrt(xNorm * xNorm + zNorm * zNorm);
    }

    private bool IsInsideIslandMask(Vector2 tilePos, IslandData island, float mapWidth, float maskRadius) {
        return GetNormalizedIslandDistance(tilePos, island, mapWidth) <= maskRadius;
    }

    private bool IsTileInMask(Vector2 tilePos, Vector2 seedPos, float maxWidth, float maxHeight, float mapWidth) {
        float dx = Mathf.Abs(tilePos.x - seedPos.x);
        dx = Mathf.Min(dx, mapWidth - dx); // Handle wrap
        float dz = Mathf.Abs(tilePos.y - seedPos.y);
        float halfWidth = maxWidth * 0.5f;
        float halfHeight = maxHeight * 0.5f;
        
        // Quick reject if clearly outside
        if (dx > halfWidth * 1.5f || dz > halfHeight * 1.5f) {
            return false;
        }
        
        // Use elliptical distance check (wrap-safe, no noise needed here)
        // The continuous land field algorithm handles the fractal coastlines
        float xNorm = dx / Mathf.Max(0.001f, halfWidth);
        float zNorm = dz / Mathf.Max(0.001f, halfHeight);
        float ellipticalDist = Mathf.Sqrt(xNorm * xNorm + zNorm * zNorm);
        
        // Include tiles within 1.3x the ellipse for processing
        // The actual land determination happens via falloff + noise in the main loop
        return ellipticalDist <= 1.3f;
    }

    private float WrappedDistance(Vector2 a, Vector2 b, float mapWidth) {
        float dx = Mathf.Abs(a.x - b.x);
        dx = Mathf.Min(dx, mapWidth - dx);
        float dz = Mathf.Abs(a.y - b.y);
        return Mathf.Sqrt(dx * dx + dz * dz);
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
}
