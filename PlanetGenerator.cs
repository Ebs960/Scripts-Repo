using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.Buffers;
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
            Biome.Temperate or Biome.Tropical => 0.9f,
            Biome.Plains or Biome.Savannah => 0.8f,
            
            // Moderate decoration biomes
            Biome.Swamp => 0.6f,
            
            // Sparse decoration biomes
            Biome.Desert or Biome.Tundra => 0.4f,
            Biome.Arctic => 0.3f,
            
            // Hostile biomes - minimal decorations
            Biome.Volcanic or Biome.Steamlands => 0.2f,
            Biome.Hellscape => 0.1f,
            
            // Moon biomes
            Biome.MoonDunes => 0.5f,
            
            // Default
            _ => 0.5f
        };
    }
    
    private static int GetDefaultMinDecorations(Biome biome)
    {
        return biome switch
        {
            // Lush biomes
            Biome.Temperate or Biome.Tropical => 2,
            Biome.Plains => 1,
            
            // Sparse biomes
            Biome.Desert or Biome.Tundra => 1,
            
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
            Biome.Temperate or Biome.Tropical => 5,
            Biome.Plains or Biome.Savannah => 4,
            
            // Moderate biomes
            Biome.Swamp => 3,
            
            // Sparse biomes
            Biome.Desert or Biome.Tundra => 2,
            
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
    [Tooltip("Warn when code changes HexTileData.owner/controllingCity via PlanetGenerator.SetHexTileData instead of TileSystem.SetTileOwner.")]
    [SerializeField] private bool debugOwnershipGuard = false;
    [Tooltip("Includes stack traces for unsafe ownership writes (can be noisy/slow).")]
    [SerializeField] private bool debugOwnershipGuardVerbose = false;
    internal bool suppressOwnershipGuards = false;

    [Header("Layer Integration")]
    [Tooltip("Optional GasGiantRenderer for planetoid visuals (enabled/disabled via layer config)")]
    public GasGiantRenderer gasGiantRenderer;

    [Tooltip("Optional reference to the HexMapChunkManager terrain renderer on this planet prefab.")]
    public HexMapChunkManager terrainRenderer;
    
    [Header("Layer Roots (assign on prefab)")]
    [Tooltip("Root GameObject containing all Surface visuals (terrain meshes, chunk manager children)")]
    public GameObject surfaceRoot;
    [Tooltip("Root GameObject containing underwater/water visuals (water meshes, shore decals)")]
    public GameObject underwaterRoot;
    [Tooltip("Root GameObject containing atmosphere visuals (clouds, banded gas giant renderer)")]
    public GameObject atmosphereRoot;
    [Tooltip("Optional root GameObject to parent planet-specific runtime objects such as spawned resources")]
    public GameObject resourcesRoot;

    [Header("Per-layer vertical offsets")]
    [Tooltip("Local Y offset applied to the Surface root when it is enabled (meters). Useful for small visual tweaks).")]
    public float surfaceYOffset = 0f;
    [Tooltip("Local Y offset applied to the Underwater root when it is enabled. Use negative values to sink the underwater root below sea level.")]
    public float underwaterYOffset = 0f;
    [Tooltip("Local Y offset applied to the Atmosphere root when it is enabled. Use positive values to expand atmosphere shells.")]
    public float atmosphereYOffset = 0f;
    [Header("Atmosphere")]
    [Tooltip("Local scale multiplier applied to the atmosphere root. Use this to control atmosphere radius/thickness.")]
    public float atmosphereRadius = 1.0f;
    
    [Tooltip("Optional authoritative PlanetConfig ScriptableObject for this planet.")]
    public PlanetConfig planetConfig;
    
    /// <summary>
    /// Query whether this planet supports a specific gameplay/visual layer.
    /// This checks the assigned `planetConfig` first (authoritative). If no config
    /// is assigned, it falls back to runtime `GameManager` planet data when available.
    /// </summary>
    public bool HasLayer(GameManager.PlanetLayerType layer)
    {
        if (planetConfig != null && planetConfig.supportedLayers != null)
        {
            return planetConfig.supportedLayers.Contains(layer);
        }

        // Fallback: if GameManager has planet data for this index, check that
        if (GameManager.Instance != null)
        {
            int idx = Mathf.Clamp(planetIndex, 0, int.MaxValue);
            var allPd = GameManager.Instance.GetPlanetData();
            if (allPd != null && allPd.ContainsKey(idx))
            {
                var pd = allPd[idx];
                if (pd != null && pd.supportedLayers != null)
                {
                    return pd.supportedLayers.Exists(p => p.layerType == layer);
                }
            }
        }

        // Sandbox / legacy scenes: if there's no planetConfig and no GameManager planet data,
        // we have no layer authority. Preserve legacy behavior by treating layers as supported.
        // This keeps tools like PlanetSandbox functional without requiring full GameManager setup.
        if (planetConfig == null && GameManager.Instance == null)
            return true;

        return false;
    }

    /// <summary>
    /// Apply planet layer configuration in a data-driven way.
    /// Enables the GasGiantRenderer only when the planet has an Atmosphere layer and no Surface layer.
    /// This method does not rely on planet type conditionals and does not modify tile generation.
    /// </summary>
    public void ApplyPlanetLayers(GameManager.PlanetData data)
    {
        if (data == null) return;

        if (terrainRenderer == null)
        {
            terrainRenderer = UnityEngine.Object.FindAnyObjectByType<HexMapChunkManager>(FindObjectsInactive.Include);
            if (terrainRenderer == null)
            {
                Debug.Log("[PlanetGenerator] ApplyPlanetLayers: no HexMapChunkManager assigned or found in scene.");
            }
            else if (enableDiagnostics)
            {
                Debug.Log($"[PlanetGenerator] ApplyPlanetLayers: auto-found HexMapChunkManager ({terrainRenderer.name}).");
            }
        }

        // Centralized layer authority: LayerManager owns layer support/visibility and toggling of roots + gas giant rules.
        var layerManager = GetComponent<LayerManager>();
        if (layerManager == null)
        {
            layerManager = gameObject.AddComponent<LayerManager>();
        }

        layerManager.InitializeForPlanet(this, data);

        // Apply atmosphere transform (scale/offset) to reflect the configured radius/offset
        UpdateAtmosphereTransform();
    }

    /// <summary>
    /// Set atmosphere radius (scale multiplier for the atmosphere root).
    /// Call this at runtime to update the preview/main-menu planet atmosphere size.
    /// </summary>
    public void SetAtmosphereRadius(float radius)
    {
        atmosphereRadius = Mathf.Max(0.01f, radius);
        UpdateAtmosphereTransform();
    }

    private void UpdateAtmosphereTransform()
    {
        if (atmosphereRoot == null) return;
        try
        {
            atmosphereRoot.transform.localScale = Vector3.one * atmosphereRadius;
            var lp = atmosphereRoot.transform.localPosition;
            lp.y = atmosphereYOffset;
            atmosphereRoot.transform.localPosition = lp;
        }
        catch { }
    }

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

    [Header("Continent Noise (Fractal Coastlines)")]
    [Tooltip("Enable low-frequency fractal noise to perturb continent edges for more realistic coastlines.")]
    [SerializeField] private bool continentNoiseEnabled = true;
    [Tooltip("Base frequency for continent-edge noise (lower = larger features).")]
    [SerializeField, Range(0.0005f, 0.05f)] private float continentNoiseFrequency = 0.005f;
    [Tooltip("Amplitude of continent-edge perturbation (0 = perfect ellipses, 1 = strong fractal carving).")]
    [SerializeField, Range(0f, 1f)] private float continentNoiseAmplitude = 0.35f;


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
    
    [Range(0.5f, 4f)]
    [Tooltip("Power curve exponent for elevation distribution. Higher = mostly flat with rare peaks. Lower = more uniform elevation. 1.8 is a balanced default.")]
    public float elevationExponent = 1.8f;
    
    [Range(0f, 1f)]
    [Tooltip("Ridged noise strength for mountains. Blends sharp ridgeline character into high-elevation tiles. 0 = smooth hills everywhere, 0.4 = dramatic mountain ridges.")]
    public float ridgeStrength = 0.35f;

    [Header("Tier-Based Elevation")]
    [Range(0f, 1f)]
    [Tooltip("Normalized noise value (0-1) above which a land tile becomes a Hill. Lower = more hills.")]
    public float hillNoiseCutoff = 0.5f;
    [Range(0f, 1f)]
    [Tooltip("Normalized noise value (0-1) above which a land tile becomes a Mountain. Lower = more mountains.")]
    public float mountainNoiseCutoff = 0.7f;
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

    [Header("Coastal Moisture")]
    [Tooltip("Maximum moisture boost applied to tiles near the coast. Falls off linearly with distance from ocean.")]
    [Range(0f, 0.4f)]
    public float coastalMoistureBoost = 0.15f;
    [Tooltip("How many tiles inland the coastal moisture boost reaches.")]
    [Range(1, 20)]
    public int coastalMoistureRange = 8;

    // --- Elevation Tier Ranges ---
    // All elevation values are in WORLD-SPACE UNITS (height offset from the flat plane).
    // The tier system assigns elevation based on classification:
    //   Flat tiles  = flatElevationMin .. flatElevationMax (interpolated)
    //   Hill tiles  = hillElevationMin .. hillElevationMax (interpolated)
    //   Mountain tiles = mountainElevationMin .. mountainElevationMax (interpolated)
    [Header("Tier Elevation Ranges")]
    [Range(0f, 25f)]
    [Tooltip("Lowest flat land elevation (world units). The lowest flat tiles sit here.")]
    public float flatElevationMin = 5.0f;
    [Range(0f, 25f)]
    [Tooltip("Highest flat land elevation (world units). The highest flat tiles reach here.")]
    public float flatElevationMax = 6.5f;

    [Range(0f, 25f)]
    [Tooltip("Lowest hill elevation (world units). The shortest hill starts here.")]
    public float hillElevationMin = 7.0f;
    [Range(0f, 25f)]
    [Tooltip("Highest hill elevation (world units). The tallest hill reaches here.")]
    public float hillElevationMax = 10.0f;

    [Range(0f, 25f)]
    [Tooltip("Lowest mountain elevation (world units). The shortest mountain starts here.")]
    public float mountainElevationMin = 10.0f;
    [Range(0f, 25f)]
    [Tooltip("Highest mountain elevation (world units). The tallest peak reaches here.")]
    public float mountainElevationMax = 15.0f;

    [Header("Water Elevation")]
    [Range(-5f, 30f)]
    [Tooltip("Ocean floor elevation in world units (typically 0 = flat plane level).")]
    public float oceanElevation = 0f;
    [Range(-5f, 30f)]
    [Tooltip("Shallow seas elevation in world units.")]
    public float seasElevation = 0.15f;
    [Range(-5f, 30f)]
    [Tooltip("Coast elevation in world units. Sea level is typically at or near this value.")]
    public float coastElevation = 0.3f;
    
    // --- River Generation (Placeholder) ---
    [Header("River Generation")]
    public bool enableRivers = true;
    [Range(0, 20)]
    [Tooltip("Minimum rivers per continent")]
    public int minRiversPerContinent = 1;
    [Range(1, 20)]
    [Tooltip("Maximum rivers per continent")]
    public int maxRiversPerContinent = 2;
    [Range(0.01f, 3.0f)]
    [Tooltip("Elevation drop applied along river tiles (world units subtracted from terrain height).")]
    public float riverDepth = 0.15f;

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
    public int minLandTilesForCoastStamps = 24;

    [Tooltip("How many tiles out from Coast should be considered shallow Seas (rings). Default 3.")]
    [Range(1, 6)]
    public int shallowSeasRings = 3;

    [Header("Lake Depth")]
    [Range(0f, 5f)]
    [Tooltip("World units subtracted from surrounding land elevation to form the lake bed depression.")]
    public float lakeDepth = 0.3f;

    [Header("Sea Level")]
    [Tooltip("When true, SeaLevelWorldY is computed from coastElevation (sea level = flatY + coastElevation). When false, use Manual Sea Level World Y.")]
    public bool seaLevelMatchCoast = true;

    [Tooltip("Manual world-space Y for sea level when seaLevelMatchCoast is false.")]
    public float manualSeaLevelWorldY = 0.3f;

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

    [Header("Stamping Performance")]
    [Tooltip("Number of tile iterations to process before yielding during stamping passes (higher = faster but larger spikes).")]
    [SerializeField]
    private int stampingBatchSize = 4096;

    [Header("Planet & Map Type")]
    [Tooltip("Which celestial body this planet represents. Controls biome assignment rules.")]
    public PlanetType planetType = PlanetType.Earth;
    [Tooltip("Earth map variant (only applies when planetType == Earth). Controls special biome rules.")]
    public MapType mapType = MapType.Standard;
    public string currentMapTypeName = "";

    [Header("Feature Toggles")]
    public bool allowOceans = true;
    public bool allowIslands = true;


    // --------------------------- Private fields -----------------------------
    HexGrid grid;
    public HexGrid Grid => grid;
    NoiseSampler noise;
    public Dictionary<int, HexTileData> data = new();
    public Dictionary<int, HexTileData> baseData = new();
    private Vector3 noiseOffset;
    // tileElevation dictionary removed — use data[i].elevation directly (world-space)
    public int landTilesGenerated = 0; // Moved to class scope to be accessible by local coroutines
    /// <summary>
    /// Public list containing the final HexTileData for every tile on the planet.
    /// This is rebuilt after surface generation completes.
    /// </summary>
    public List<HexTileData> Tiles { get; private set; } = new List<HexTileData>();
    public bool HasGeneratedSurface { get; private set; } = false;
    /// <summary>
    /// World-space Y of the flat sea plane for this planet. This is the single
    /// source-of-truth for sea/lake surface placement — water generators must
    /// use this value and must NOT hardcode or compute their own Y offsets.
    /// Assigned during render elevation / finalization pass.
    /// </summary>
    public float SeaLevelWorldY { get; private set; } = 0f;
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
        grid = new HexGrid();
        

                
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

    // Ensure there's a dedicated resources root to parent runtime-spawned objects.
    if (resourcesRoot == null)
    {
        resourcesRoot = new GameObject("ResourcesRoot");
        resourcesRoot.transform.SetParent(this.transform, false);
    }
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

        // Ensure GPU resources are released when this generator is destroyed (planet unload)
        try
        {
            ReleaseGpuResources();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[PlanetGenerator] Exception while releasing GPU resources on destroy: {ex.Message}");
        }
    }

    /// <summary>
    /// Release GPU/native resources related to this planet (textures, buffers, baker caches).
    /// Call when unloading or switching planets to reduce VRAM and native memory usage.
    /// </summary>
    public void ReleaseGpuResources()
    {
        try
        {
            if (terrainRenderer != null)
            {
                terrainRenderer.ReleaseGpuResources();
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[PlanetGenerator] Failed releasing HexMapChunkManager GPU resources: {ex.Message}");
        }

        try
        {
            PlanetTextureBaker.ClearAllCaches();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[PlanetGenerator] Failed releasing PlanetTextureBaker caches: {ex.Message}");
        }
    }

    /// <summary>
    /// Inject an externally-created `HexGrid` instance (for example from `HexGridComponent`).
    /// Use this when the grid is prepared in-scene and should be used by this generator
    /// before baking/chunk building.
    /// </summary>
    public void SetGrid(HexGrid newGrid)
    {
        if (newGrid == null)
        {
            Debug.LogWarning("[PlanetGenerator] SetGrid called with null grid; ignoring.");
            return;
        }

        grid = newGrid;
        Debug.Log($"[PlanetGenerator] Grid injected: {grid.Width}x{grid.Height} ({grid.TileCount} tiles)");
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
        Debug.Log($"[PlanetGenerator] GenerateSurface START for '{gameObject.name}' seed={seed} tileCount={grid?.TileCount}");
        // Clear previous data
        data.Clear();
        baseData.Clear();
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
        
        // Water (oceans/lakes/rivers) MUST be gated by planet layer support.
        // Root cause: the previous gating relied on world-type booleans (isMarsWorldType, etc.),
        // which can be wrong in multi-planet runs and allowed non-underwater planets to stamp lakes.
        bool hasLayerAuthority = (planetConfig != null && planetConfig.supportedLayers != null && planetConfig.supportedLayers.Count > 0);
        if (!hasLayerAuthority && GameManager.Instance != null)
        {
            try
            {
                var allPd = GameManager.Instance.GetPlanetData();
                int idx = Mathf.Clamp(planetIndex, 0, int.MaxValue);
                if (allPd != null && allPd.ContainsKey(idx))
                {
                    var pd = allPd[idx];
                    if (pd != null && pd.supportedLayers != null && pd.supportedLayers.Count > 0)
                        hasLayerAuthority = true;
                }
            }
            catch { /* ignore */ }
        }

        // If we cannot determine layers (legacy/sandbox scenes), preserve legacy behavior (water allowed).
        bool supportsUnderwaterLayer = hasLayerAuthority ? HasLayer(GameManager.PlanetLayerType.Underwater) : true;
        bool allowOceansThisRun = allowOceans && supportsUnderwaterLayer;

        // Configure noise sampler for this map's dimensions
        float mapWidth = grid.Width;
        float mapHeight = grid.Height;
        // Single elevation frequency — scales with map width for consistent terrain scale
        float elevFreqPeriodic = 1f / (mapWidth * 0.45f);

        // DIAGNOSTICS: report key settings and grid stats
        if (enableDiagnostics)
        {
            Debug.Log($"[PlanetGenerator][Diag] mapWidth={mapWidth:F1} mapHeight={mapHeight:F1} tiles={tileCount}");
            Debug.Log($"[PlanetGenerator][Diag] latitudeInfluence={latitudeInfluence} latitudeExponent={latitudeExponent} temperatureBias={temperatureBias} moistureBias={moistureBias}");

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

        // Rent large per-tile arrays from pool to reduce GC pressure
        Vector2Int[] tileCoords = ArrayPoolUtils.Rent<Vector2Int>(tileCount, true);
        for (int i = 0; i < tileCount; i++) {
            tileCoords[i] = new Vector2Int(i % tilesX, i / tilesX);
        }

        bool[] isLandTile = ArrayPoolUtils.RentBool(tileCount);
        bool[] isLakeTile = ArrayPoolUtils.RentBool(tileCount);
        bool[] isRiverTile = ArrayPoolUtils.RentBool(tileCount);

        int WrappedDelta(int a, int b, int width) {
            int delta = a - b;
            if (Mathf.Abs(delta) > width / 2) {
                delta = delta > 0 ? delta - width : delta + width;
            }
            return delta;
        }

        System.Collections.IEnumerator StampEllipseBatched(ContinentData continent)
        {
            float halfW = Mathf.Max(0.5f, continent.widthTiles * 0.5f);
            float halfH = Mathf.Max(0.5f, continent.heightTiles * 0.5f);
            int counter = 0;
            int batch = Mathf.Max(1, stampingBatchSize);
            for (int i = 0; i < tileCount; i++)
            {
                Vector2Int coord = tileCoords[i];
                float dx = WrappedDelta(coord.x, continent.center.x, tilesX) / halfW;
                float dy = (coord.y - continent.center.y) / halfH;
                float distSq = dx * dx + dy * dy;
                if (continentNoiseEnabled && noise != null)
                {
                    Vector2 tilePos = new Vector2(coord.x, coord.y);
                    float n = noise.GetElevationPeriodic(tilePos, mapWidth, mapHeight, continentNoiseFrequency);
                    float perturb = (n * 2f - 1f) * continentNoiseAmplitude;
                    float radiusScale = 1f + perturb;
                    radiusScale = Mathf.Max(0.2f, radiusScale);
                    if (distSq <= radiusScale * radiusScale)
                    {
                        isLandTile[i] = true;
                    }
                }
                else
                {
                    if (distSq <= 1f)
                    {
                        isLandTile[i] = true;
                    }
                }

                counter++;
                if (counter >= batch) { counter = 0; yield return null; }
            }
        }

        System.Collections.IEnumerator StampCircleBatched(Vector2Int center, int radius, bool makeLand, bool makeLake)
        {
            int maxRadius = Mathf.Max(0, radius);
            int counter = 0;
            int batch = Mathf.Max(1, stampingBatchSize);
            for (int i = 0; i < tileCount; i++)
            {
                int dist = HexDistanceWrapped(tileCoords[i], center, tilesX);
                if (dist <= maxRadius)
                {
                    if (makeLand)
                    {
                        isLandTile[i] = true;
                    }
                    if (makeLake)
                    {
                        isLandTile[i] = false;
                        isLakeTile[i] = true;
                    }
                }

                counter++;
                if (counter >= batch) { counter = 0; yield return null; }
            }
        }

        foreach (var continent in continentDataList)
        {
            yield return StartCoroutine(StampEllipseBatched(continent));
        }

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
                yield return StartCoroutine(StampCircleBatched(tileCoords[idx], radius, true, false));
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
                    yield return StartCoroutine(StampCircleBatched(tileCoords[idx], radius, true, false));
                    islandsStamped++;
                }
            }
        }

        // (Stamping debug logs removed)

        // ---------- 2.75. Coastal irregularity passes (bays & peninsulas) ----------
        // compute current land count (islands/stamps applied so far)
        int _currentLandCount = 0;
        int _countYieldCounter = 0;
        for (int _i = 0; _i < tileCount; _i++)
        {
            if (isLandTile[_i]) _currentLandCount++;
            _countYieldCounter++;
            if (_countYieldCounter >= stampingBatchSize) { _countYieldCounter = 0; yield return null; }
        }
        if (_currentLandCount >= minLandTilesForCoastStamps)
        {
            System.Random coastRand = new System.Random(unchecked((int)(seed ^ 0xBEEF)));

            // Build coast candidate lists
            List<int> coastLandCandidates = new List<int>();
            List<int> coastWaterCandidates = new List<int>();
            int coastYieldCounter = 0;
            for (int i = 0; i < tileCount; i++) {
                bool anyWaterNeighbor = false;
                foreach (int n in grid.neighbors[i]) {
                    if (n < 0 || n >= tileCount) continue;
                    if (!isLandTile[n]) { anyWaterNeighbor = true; break; }
                }
                if (isLandTile[i] && anyWaterNeighbor) coastLandCandidates.Add(i);
                if (!isLandTile[i] && anyWaterNeighbor) coastWaterCandidates.Add(i);
                coastYieldCounter++;
                if (coastYieldCounter >= stampingBatchSize) { coastYieldCounter = 0; yield return null; }
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
                    if ((t & 2047) == 0) yield return null; // yield periodically during heavy per-tile checks
                }
                if (removed == 0) continue;
                if ((float)removed / Mathf.Max(1, _currentLandCount) > 0.15f) continue; // don't remove >15% of land

                yield return StartCoroutine(StampCircleBatched(tileCoords[centerIdx], r, false, false));
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
                    if ((t & 2047) == 0) yield return null;
                }
                if (added.Count == 0) continue;
                bool connects = false;
                foreach (int at in added) {
                    foreach (int n in grid.neighbors[at]) { if (n >= 0 && n < tileCount && isLandTile[n]) { connects = true; break; } }
                    if (connects) break;
                }
                if (!connects) continue;

                yield return StartCoroutine(StampCircleBatched(tileCoords[centerIdx], r, true, false));
            }
        }

        // ---------- 3. Generate Lakes (Stamping) ----------
        int lakesStamped = 0;
        List<Vector2Int> lakeCenters = new List<Vector2Int>();
        if (enableLakes && allowOceansThisRun && numberOfLakes > 0)
        {
            int lakeMinRadius = Mathf.Max(1, lakeMinRadiusTiles);
            int lakeMaxRadius = Mathf.Max(lakeMinRadius, lakeMaxRadiusTiles);
            int lakeMinDistance = Mathf.Max(0, lakeMinDistanceFromCoast);

            List<int> lakeCoastTiles = new List<int>();
            int lakeCoastYield = 0;
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
                lakeCoastYield++;
                if (lakeCoastYield >= stampingBatchSize) { lakeCoastYield = 0; yield return null; }
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

                // Add 1-2 random land neighbors on the lake perimeter so each lake
                // has a slightly different shape instead of a uniform circle.
                int extraTiles = lakeRand.Next(1, 3); // 1 or 2 extra tiles
                var perimeterLand = new List<int>();
                foreach (int lt in lakeTiles)
                {
                    foreach (int nb in grid.neighbors[lt])
                    {
                        if (nb >= 0 && nb < tileCount && isLandTile[nb] && !isLakeTile[nb])
                            perimeterLand.Add(nb);
                    }
                }
                // Shuffle and pick distinct tiles
                for (int ei = perimeterLand.Count - 1; ei > 0; ei--)
                {
                    int ej = lakeRand.Next(ei + 1);
                    int tmp = perimeterLand[ei]; perimeterLand[ei] = perimeterLand[ej]; perimeterLand[ej] = tmp;
                }
                HashSet<int> addedExtra = new HashSet<int>();
                foreach (int extra in perimeterLand)
                {
                    if (addedExtra.Count >= extraTiles) break;
                    if (addedExtra.Contains(extra)) continue;
                    isLandTile[extra] = false;
                    isLakeTile[extra] = true;
                    addedExtra.Add(extra);
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

                    // Add 1-2 random land neighbors for shape variety (same as main path)
                    int fbExtraTiles = lakeRand.Next(1, 3);
                    var fbPerimeter = new List<int>();
                    foreach (int lt in lakeTiles)
                    {
                        foreach (int nb in grid.neighbors[lt])
                        {
                            if (nb >= 0 && nb < tileCount && isLandTile[nb] && !isLakeTile[nb])
                                fbPerimeter.Add(nb);
                        }
                    }
                    for (int ei = fbPerimeter.Count - 1; ei > 0; ei--)
                    {
                        int ej = lakeRand.Next(ei + 1);
                        int tmp = fbPerimeter[ei]; fbPerimeter[ei] = fbPerimeter[ej]; fbPerimeter[ej] = tmp;
                    }
                    HashSet<int> fbAdded = new HashSet<int>();
                    foreach (int extra in fbPerimeter)
                    {
                        if (fbAdded.Count >= fbExtraTiles) break;
                        if (fbAdded.Contains(extra)) continue;
                        isLandTile[extra] = false;
                        isLakeTile[extra] = true;
                        fbAdded.Add(extra);
                    }

                    lakeCenters.Add(tileCoords[centerIdx]);
                    lakesStamped++;
                }
            }
        }

        if (enableRivers && allowOceansThisRun && GameSetupData.riverCount > 0 && enableLakes && lakeCenters.Count == 0)
        {
            int fallbackRadius = Mathf.Max(1, lakeMinRadiusTiles);
            for (int i = 0; i < tileCount; i++)
            {
                if (!isLandTile[i]) continue;
                yield return StartCoroutine(StampCircleBatched(tileCoords[i], fallbackRadius, false, true));
                lakeCenters.Add(tileCoords[i]);
                lakesStamped++;
                break;
            }
        }
        // (Stamping debug logs removed)
        // Shape raw 0-1 noise with power curve + optional ridged character.
        // Returns a shaped 0-1 value (NOT yet an elevation — tier assignment happens later).
        float ShapeNoise(float rawNoise)
        {
            float shaped = Mathf.Pow(rawNoise, elevationExponent);
            if (ridgeStrength > 0.001f)
            {
                // Ridged transform: creates sharp V-shaped ridgelines from smooth noise
                float ridged = 2f * (0.5f - Mathf.Abs(0.5f - rawNoise));
                ridged = Mathf.Pow(ridged, elevationExponent);
                // Blend ridged character only into higher elevations (mountains get sharp, lowlands stay smooth)
                float heightBlend = Mathf.Clamp01(shaped * 2f - 0.5f);
                shaped = Mathf.Lerp(shaped, ridged, ridgeStrength * heightBlend);
            }
            return shaped;
        }

        // ---------- PRE-PASS: Compute shaped noise for every tile, then normalize ----------
        // This guarantees the full 0-1 range is used regardless of FBm output limits,
        // so hillNoiseCutoff / mountainNoiseCutoff work as intended.
        float[] shapedNoisePerTile = ArrayPoolUtils.RentFloat(tileCount);
        float noiseMin = float.MaxValue;
        float noiseMax = float.MinValue;

        for (int i = 0; i < tileCount; i++)
        {
            if (!isLandTile[i] && !isLakeTile[i]) continue; // ocean tiles stay 0
            Vector2Int coord = tileCoords[i];
            Vector2 tilePos = new Vector2(coord.x, coord.y);
            float rawNoise = noise.GetElevationPeriodic(tilePos, mapWidth, mapHeight, elevFreqPeriodic);
            float shaped = ShapeNoise(rawNoise);
            shapedNoisePerTile[i] = shaped;
            if (shaped < noiseMin) noiseMin = shaped;
            if (shaped > noiseMax) noiseMax = shaped;
        }

        // Normalize all land/lake noise to 0-1 so cutoffs are reliable
        float noiseSpan = noiseMax - noiseMin;
        if (noiseSpan < 0.001f) noiseSpan = 1f; // safety: avoid division by zero
        for (int i = 0; i < tileCount; i++)
        {
            if (!isLandTile[i] && !isLakeTile[i]) continue;
            shapedNoisePerTile[i] = (shapedNoisePerTile[i] - noiseMin) / noiseSpan;
        }

        Debug.Log($"[PlanetGenerator] Noise pre-pass: raw shaped range [{noiseMin:F4}..{noiseMax:F4}], normalized to [0..1]");

        // Convert a normalized 0-1 noise value into a world-space elevation using
        // the discrete tier system: Flat / Hill / Mountain.
        float TierElevation(float normalizedNoise)
        {
            if (normalizedNoise >= mountainNoiseCutoff)
            {
                // Mountain tier: interpolate mountainElevationMin .. mountainElevationMax
                float denom = 1f - mountainNoiseCutoff;
                float t = denom > 0.001f ? (normalizedNoise - mountainNoiseCutoff) / denom : 0f;
                return Mathf.Lerp(mountainElevationMin, mountainElevationMax, t);
            }
            if (normalizedNoise >= hillNoiseCutoff)
            {
                // Hill tier: interpolate hillElevationMin .. hillElevationMax
                float hillDenom = mountainNoiseCutoff - hillNoiseCutoff;
                float hillT = hillDenom > 0.001f ? (normalizedNoise - hillNoiseCutoff) / hillDenom : 0f;
                return Mathf.Lerp(hillElevationMin, hillElevationMax, hillT);
            }
            // Flat tier: interpolate flatElevationMin .. flatElevationMax
            float flatDenom = hillNoiseCutoff;
            float flatT = flatDenom > 0.001f ? normalizedNoise / flatDenom : 0f;
            return Mathf.Lerp(flatElevationMin, flatElevationMax, flatT);
        }

        float ComputeLandElevationForIndex(int index)
        {
            return TierElevation(shapedNoisePerTile[index]);
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

            // (Stamping debug logs removed)
        }

        landTilesGenerated = 0;
        for (int i = 0; i < tileCount; i++) {
            if (isLandTile[i]) landTilesGenerated++;
        }
        // (Stamping debug logs removed)

        // ---------- 5. Calculate Biomes, Elevation, and Initial Data ---------
        if (!allowOceansThisRun)
        {
            for (int i = 0; i < tileCount; i++) {
                isLandTile[i] = true;
                isLakeTile[i] = false;
                isRiverTile[i] = false;
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

        // northPoleY was unused previously; remove to avoid warning
        int southPoleY = Mathf.Max(0, tilesZ - 1);
        int equatorY = Mathf.Clamp(tilesZ / 2, 0, southPoleY);
        float? northPoleTemp = null;
        float? southPoleTemp = null;
        float? equatorTemp = null;
        
        // Two-pass climate/elevation processing:
        // 1) Sample elevation, temperature, moisture into arrays (periodic sampling available)
        // 2) Smooth climate arrays (passes)
        // 3) Assign biomes using smoothed climate values and already-computed elevation

        float[] sampledTemp = ArrayPoolUtils.RentFloat(tileCount);
        float[] sampledMoist = ArrayPoolUtils.RentFloat(tileCount);
        float[] sampledElev = ArrayPoolUtils.RentFloat(tileCount);

        // Ensure rented arrays are returned when generation finishes / coroutine disposed
        try
        {

        for (int i = 0; i < tileCount; i++)
        {
            Vector2Int coord = tileCoords[i];
            bool isLand = isLandTile[i];
            Vector2 tilePos = new Vector2(coord.x, coord.y);
            Vector3 noisePoint = new Vector3(coord.x, 0f, coord.y) + noiseOffset;

            // Normalized shaped noise was pre-computed above; retrieve it for tier elevation.
            float normalizedNoise = shapedNoisePerTile[i];

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

            // Compute final elevation using discrete tier system
            float finalElevation;
            if (isLakeTile[i])
            {
                // Lakes: compute what the land elevation WOULD be, then subtract lakeDepth
                // so the lake bed sits below the surrounding terrain surface.
                float landElev = TierElevation(normalizedNoise);
                finalElevation = landElev - lakeDepth;
                finalElevation = Mathf.Max(0f, finalElevation); // Don't go negative
            }
            else if (isLand)
            {
                finalElevation = TierElevation(normalizedNoise);
            }
            else
            {
                finalElevation = 0f;
            }

            finalElevation = Mathf.Min(finalElevation, mountainElevationMax);

            sampledTemp[i] = temperature;
            sampledMoist[i] = moisture;
            sampledElev[i] = finalElevation;

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

        // --- Coastal moisture boost: BFS from ocean tiles, boost moisture near coasts ---
        if (coastalMoistureBoost > 0.001f && coastalMoistureRange > 0)
        {
            int[] distFromCoast = new int[tileCount];
            for (int i = 0; i < tileCount; i++) distFromCoast[i] = int.MaxValue;

            var coastQueue = new Queue<int>();
            for (int i = 0; i < tileCount; i++)
            {
                if (!isLandTile[i] && !isLakeTile[i]) // Ocean/seas tiles
                {
                    distFromCoast[i] = 0;
                    coastQueue.Enqueue(i);
                }
            }

            // BFS flood fill from all ocean tiles simultaneously
            while (coastQueue.Count > 0)
            {
                int cur = coastQueue.Dequeue();
                int nextDist = distFromCoast[cur] + 1;
                if (nextDist > coastalMoistureRange) continue;
                foreach (int n in grid.neighbors[cur])
                {
                    if (n < 0 || n >= tileCount) continue;
                    if (distFromCoast[n] <= nextDist) continue; // Already closer
                    distFromCoast[n] = nextDist;
                    coastQueue.Enqueue(n);
                }
            }

            // Apply moisture boost — linear falloff from coast
            for (int i = 0; i < tileCount; i++)
            {
                if (distFromCoast[i] > 0 && distFromCoast[i] <= coastalMoistureRange)
                {
                    float t = 1f - (float)distFromCoast[i] / coastalMoistureRange;
                    sampledMoist[i] = Mathf.Clamp01(sampledMoist[i] + coastalMoistureBoost * t);
                }
            }

            if (enableDiagnostics)
                Debug.Log($"[PlanetGenerator] Coastal moisture boost applied: boost={coastalMoistureBoost} range={coastalMoistureRange} tiles");
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
            bool isMountain = false;

            if (isLake)
            {
                biome = Biome.Lake;
            }
            else if (isLand)
            {
                biome = GetBiomeForTile(i, true, temperature, moisture);

                if (finalElevation >= mountainElevationMin)
                {
                    if (biome != Biome.Glacier && biome != Biome.Arctic)
                    {
                        isMountain = true;
                    }
                }
                else if (finalElevation >= hillElevationMin)
                {
                    bool biomeIsWater = (biome == Biome.Coast || biome == Biome.Seas || biome == Biome.Ocean || biome == Biome.Lake || biome == Biome.River);
                    if (!biomeIsWater)
                    {
                        isHill = true;
                    }
                }
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
                // Glaciers use the same tier-based elevation as land
                finalElevation = TierElevation(shapedNoisePerTile[i]);
                if (finalElevation < landElevMin) landElevMin = finalElevation;
                if (finalElevation > landElevMax) landElevMax = finalElevation;
                if (!landTileIndices.Contains(i)) landTileIndices.Add(i);
            }

            // Track climate min/max for diagnostics
            if (temperature < temperatureMin) temperatureMin = temperature;
            if (temperature > temperatureMax) temperatureMax = temperature;
            if (moisture < moistureMin) moistureMin = moisture;
            if (moisture > moistureMax) moistureMax = moisture;

            // Create HexTileData
            var y = BiomeHelper.Yields(biome);
            int moveCost = BiomeHelper.GetMovementCost(biome);
            ElevationTier elevTier = ElevationTier.Flat;
            if (finalElevation >= mountainElevationMin) elevTier = ElevationTier.Mountain;
            else if (finalElevation >= hillElevationMin) elevTier = ElevationTier.Hill;

            #pragma warning disable 612, 618  // Suppress obsolete warning for occupantId initialization
            var td = new HexTileData
            {
                biome = biome,
                food = y.food, production = y.prod, gold = y.gold, science = y.sci, culture = y.cult,
                occupantId = 0,
                isLand = isLand,
                isLake = isLake,
                isRiver = isRiverTile[i],
                isHill = isHill,
                isMountain = isMountain,
                elevation = finalElevation,
                originalElevation = finalElevation,
                elevationTier = elevTier,
                temperature = temperature,
                moisture = moisture,
                movementCost = moveCost,
                isPassable = true,
                isMoonTile = false
            };
            #pragma warning restore 612, 618
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

        // (Stamping debug asserts removed)

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

        Debug.Log($"[PlanetGenerator] Land elevation range (initial, pre-coast/river): {landElevMin:F4} to {landElevMax:F4}");

        // ---------- 5.5. Compute Render Elevation — MOVED to section 6.6 ----------
        // Render elevation normalization now runs AFTER coast/seas/river post-processing
        // (section 6.6) so that converted coast tiles get correct render elevation
        // instead of retaining their former land values.

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
            // Convert land tile to Coast if adjacent to Ocean/Seas (but NEVER Arctic/Glacier)
            // Mountains and hills adjacent to water are demoted — coastline always forms.
            if (hasWaterNeighbor && !postProcessProtectedTiles.Contains(i)) {
                var td = data[i];
                td.biome = Biome.Coast;
                td.isLand = true;
                td.isHill = false;
                td.isMountain = false;
                td.elevationTier = ElevationTier.Flat;
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

        // Convert Ocean tiles near Coast into Seas — 3 rings deep.
        // Pass 1: Ocean adjacent to Coast → Seas
        // Pass 2-3: Ocean adjacent to existing Seas → Seas (extends 2 more tiles out)
        HashSet<int> seasTiles = new HashSet<int>();
        int seasRings = Mathf.Max(1, shallowSeasRings);
        for (int ring = 0; ring < seasRings; ring++)
        {
            List<int> newSeas = new List<int>();
            for (int i = 0; i < tileCount; i++)
            {
                if (!data.ContainsKey(i)) continue;
                if (postProcessProtectedTiles.Contains(i)) continue;
                if (data[i].biome != Biome.Ocean) continue;

                bool nearShallow = false;
                foreach (int nIdx in grid.neighbors[i])
                {
                    // First ring: adjacent to coast. Subsequent rings: adjacent to seas.
                    if (ring == 0 && coastTiles.Contains(nIdx)) { nearShallow = true; break; }
                    if (ring > 0 && seasTiles.Contains(nIdx)) { nearShallow = true; break; }
                }

                if (nearShallow)
                {
                    newSeas.Add(i);
                }
            }

            foreach (int idx in newSeas)
            {
                var td = data[idx];
                td.biome = Biome.Seas;
                data[idx] = td;
                baseData[idx] = td;
                seasTiles.Add(idx);
            }

            // Yield between rings
            if (loadingPanelController != null)
            {
                loadingPanelController.SetProgress(0.65f + (float)(ring + 1) / seasRings * 0.05f);
                loadingPanelController.SetStatus($"Defining shallow seas (ring {ring + 1}/{seasRings})...");
            }
            yield return null;
        }

        // ---------- 6.1 Set Fixed Coast Elevation (AFTER Coasts/Seas are determined) ----------
        // Also synchronize elevationTier and hill/mountain flags for tiles whose biome changed
        // during post-processing. Without this, coast tiles converted from land retain their
        // original Hill/Mountain tier, causing mismatches with gameplay systems.
        for (int i = 0; i < tileCount; i++) {
            if (data.ContainsKey(i)) {
                Biome b = data[i].biome;
                if (b == Biome.Ocean) {
                    var td = data[i]; td.elevation = oceanElevation; td.elevationTier = ElevationTier.Flat; td.isHill = false; td.isMountain = false; data[i] = td; baseData[i] = td;
                } else if (b == Biome.Seas) {
                    var td = data[i]; td.elevation = seasElevation; td.elevationTier = ElevationTier.Flat; td.isHill = false; td.isMountain = false; data[i] = td; baseData[i] = td;
                } else if (b == Biome.Coast) {
                    var td = data[i]; td.elevation = coastElevation; td.elevationTier = ElevationTier.Flat; td.isHill = false; td.isMountain = false; data[i] = td; baseData[i] = td;
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

        // ---------- 6.2 Flatten land tiles within 2 tiles of coastal water ----------
        // BFS from ocean/seas/coast tiles outward into land (NOT lakes or rivers).
        // Any land tile within 2 hops of coastal water gets flattened to flatElevationMin.
        // This creates a natural shoreline buffer — no hills or mountains right at the waterline.
        {
            int flattenRadius = 2;
            int[] waterDist = new int[tileCount];
            for (int i = 0; i < tileCount; i++) waterDist[i] = -1;

            var bfsQueue = new Queue<int>();
            // Seed BFS from ocean/seas/coast tiles only (exclude lakes and rivers)
            for (int i = 0; i < tileCount; i++)
            {
                if (!data.ContainsKey(i)) continue;
                Biome b = data[i].biome;
                if (b == Biome.Ocean || b == Biome.Seas || b == Biome.Coast)
                {
                    waterDist[i] = 0;
                    bfsQueue.Enqueue(i);
                }
            }

            while (bfsQueue.Count > 0)
            {
                int cur = bfsQueue.Dequeue();
                int nextDist = waterDist[cur] + 1;
                if (nextDist > flattenRadius) continue;
                foreach (int n in grid.neighbors[cur])
                {
                    if (n < 0 || n >= tileCount) continue;
                    if (waterDist[n] >= 0) continue; // already visited
                    waterDist[n] = nextDist;
                    bfsQueue.Enqueue(n);
                }
            }

            // Flatten any land tile within the radius
            for (int i = 0; i < tileCount; i++)
            {
                if (waterDist[i] <= 0 || waterDist[i] > flattenRadius) continue;
                if (!data.ContainsKey(i)) continue;
                var td = data[i];
                if (!td.isLand || td.isLake || td.isRiver) continue;
                if (td.biome == Biome.Coast || td.biome == Biome.Ocean || td.biome == Biome.Seas) continue;

                td.elevation = flatElevationMin;
                td.elevationTier = ElevationTier.Flat;
                td.isHill = false;
                td.isMountain = false;
                data[i] = td;
                baseData[i] = td;
            }
        }

        // ---------- 6.5 River Generation Pass (after coasts are defined) ----
        if (enableRivers && allowOceansThisRun && GameSetupData.riverCount > 0)
            yield return StartCoroutine(GenerateRivers(isLandTile, data, lakeCenters));

        // ---------- 6.55 Compute Water Metadata for chunk-based water mesh system ----------
        ComputeWaterMetadata(data, grid, tileCount);

        // ---------- 6.6. Elevation is already in world-space units ----------
        // No normalization needed. The elevation field on each tile IS the world-space
        // height offset from the flat plane. The heightmap texture stores these values
        // directly (RHalf supports the full float range including negatives).
        Debug.Log($"[PlanetGenerator] Elevation is world-space. ocean={oceanElevation:F2}, seas={seasElevation:F2}, coast={coastElevation:F2}, flat={flatElevationMin:F2}-{flatElevationMax:F2}, hills={hillElevationMin:F2}-{hillElevationMax:F2}, mountains={mountainElevationMin:F2}-{mountainElevationMax:F2}");

        // Set authoritative sea level world Y for this planet.
        // With world-space elevation, sea level is simply flatY + coastElevation.
        float flatY = GameManager.Instance != null ? GameManager.Instance.GetFlatPlaneY() : transform.position.y;
        if (seaLevelMatchCoast)
        {
            SeaLevelWorldY = flatY + coastElevation;
        }
        else
        {
            SeaLevelWorldY = manualSeaLevelWorldY;
        }
        Debug.Log($"[PlanetGenerator] SeaLevelWorldY={SeaLevelWorldY:F3} (flatY={flatY:F3} coastElev={coastElevation:F3} matchCoast={seaLevelMatchCoast})");

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

        Debug.Log($"[PlanetGenerator] GenerateSurface COMPLETE for '{gameObject.name}' HasGeneratedSurface={HasGeneratedSurface} totalTiles={Tiles.Count} SeaLevelWorldY={SeaLevelWorldY}");

        // DIAGNOSTIC: Log elevation statistics (gated behind enableDiagnostics to avoid
        // iterating all tiles + heavy Debug.LogError calls for every planet during loading)
        if (enableDiagnostics)
            LogElevationDiagnostics(data);

        // Notify listeners that surface is ready for rendering
        try { OnSurfaceGenerated?.Invoke(); } catch (System.Exception ex) { Debug.LogError($"[PlanetGenerator] OnSurfaceGenerated invocation error: {ex.Message}"); }
        

        }
        finally
        {
            // Return pooled arrays to avoid GC pressure and reduce peak memory
            try { ArrayPoolUtils.Return<Vector2Int>(tileCoords); } catch { }
            try { ArrayPoolUtils.ReturnBool(isLandTile); } catch { }
            try { ArrayPoolUtils.ReturnBool(isLakeTile); } catch { }
            try { ArrayPoolUtils.ReturnBool(isRiverTile); } catch { }
            try { ArrayPoolUtils.ReturnFloat(shapedNoisePerTile); } catch { }
            try { ArrayPoolUtils.ReturnFloat(sampledTemp); } catch { }
            try { ArrayPoolUtils.ReturnFloat(sampledMoist); } catch { }
            try { ArrayPoolUtils.ReturnFloat(sampledElev); } catch { }
        }

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
                yield break;
            }

            // NOTE: previously we clamped targetRiverCount to lake count (one river per lake).
            // For stricter coast-reaching behavior we do NOT clamp here; targetRiverCount stays as requested.

            int riversGenerated = 0;
            int attempts = 0;

            // Determine target river count strictly from lakes (one river per lake maximum)
            // (Stamping debug logs removed)

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

            // Coast tiles cache used for reachability + continent coast targeting.
            // NOTE: This is local to GenerateRivers (the earlier coastline pass has its own local coastTiles).
            var coastTiles = new HashSet<int>();
            foreach (var kvp in tileData)
            {
                if (kvp.Value.biome == Biome.Coast)
                    coastTiles.Add(kvp.Key);
            }

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
            // (Stamping debug logs removed)

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
                        float elevCur = curTile.elevation;
                        float elevN = nt.elevation;
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

                // (Stamping per-river debug logs removed)

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
                    td.elevation = td.elevation - riverDepth; // world-space: river carves into terrain
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

            // (Stamping debug logs removed)
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
        data.TryGetValue(tileIndex, out HexTileData td) ? td.elevation : 0f;

    // --- NEW: Getter for full HexTileData ---
    public HexTileData GetHexTileData(int tileIndex) {
        data.TryGetValue(tileIndex, out HexTileData td);
        return td; // Will be null if tile not found
    }
    
    // --- NEW: Setter for HexTileData ---
    public void SetHexTileData(int tileIndex, HexTileData td) {
        if (!data.ContainsKey(tileIndex)) return;
        if (!suppressOwnershipGuards && (debugOwnershipGuard || debugOwnershipGuardVerbose))
        {
            var prev = data[tileIndex];
            if (prev != null && td != null)
            {
                bool ownerChanged = !ReferenceEquals(prev.owner, td.owner);
                bool controllingCityChanged = !ReferenceEquals(prev.controllingCity, td.controllingCity);
                if (ownerChanged || controllingCityChanged)
                {
                    string prevOwnerName = prev.owner != null ? prev.owner.name : "null";
                    string newOwnerName = td.owner != null ? td.owner.name : "null";
                    string prevCityName = prev.controllingCity != null ? prev.controllingCity.name : "null";
                    string newCityName = td.controllingCity != null ? td.controllingCity.name : "null";
                    Debug.LogWarning(
                        $"[PlanetGenerator][OwnershipGuard] Direct SetHexTileData changed ownership fields. " +
                        $"planet={planetIndex} tile={tileIndex} owner {prevOwnerName}->{newOwnerName} controllingCity {prevCityName}->{newCityName}. " +
                        $"Use TileSystem.SetTileOwner(...) instead.");
                    if (debugOwnershipGuardVerbose)
                    {
                        Debug.LogWarning($"[PlanetGenerator][OwnershipGuard] StackTrace:\n{Environment.StackTrace}");
                    }
                }
            }
        }
        data[tileIndex] = td;
        // baseData may also want updating if you allow undoing.
        baseData[tileIndex] = td;
    }
    // ----------------------------------------

    /// <summary>
    /// Set the occupant for a tile. Delegates to TileOccupancyManager which is the single source of truth.
    /// </summary>
    public void SetTileOccupant(int tileIndex, GameObject occupant, TileLayer layer = TileLayer.Surface) {
        if (!data.ContainsKey(tileIndex)) return;
        
        // TileOccupancyManager is the single source of truth for all layer occupancy
        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        if (occ != null)
        {
            occ.SetOccupant(tileIndex, occupant, layer);
        }
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
        // Restore original rule: Coast is treated as water (not land) for gameplay layer logic.
        td.isLand = (newBiome != Biome.Ocean && newBiome != Biome.Seas && newBiome != Biome.Coast && newBiome != Biome.Lake && newBiome != Biome.Glacier);
        if (td.isRiver) td.isLand = true;
        td.isHill = false; // Setting biome usually overrides hill status unless specifically handled

        // Keep water metadata coherent for the chunk-based water mesh system.
        // Root-cause fix for: tiles that become Ocean/Seas late not spawning a water surface.
        if (newBiome == Biome.Ocean || newBiome == Biome.Seas || newBiome == Biome.Coast)
        {
            td.waterType = TileWaterType.Ocean;
            td.lakeId = -1;
            td.waterElevation = coastElevation;
            td.riverFlowDirXZ = Vector2.zero;
        }
        else if (newBiome == Biome.River)
        {
            td.waterType = TileWaterType.River;
            td.lakeId = -1;
            td.waterElevation = td.elevation; // will be refined by ComputeWaterMetadata during generation
            // riverFlowDirXZ will be computed during generation; default to zero here
        }
        else if (newBiome == Biome.Lake)
        {
            td.waterType = TileWaterType.Lake;
            td.lakeId = -1;
            td.waterElevation = td.elevation; // will be refined by ComputeWaterMetadata during generation
            td.riverFlowDirXZ = Vector2.zero;
        }
        else
        {
            // Land biomes have no water surface mesh.
            td.waterType = TileWaterType.None;
            td.lakeId = -1;
            td.waterElevation = 0f;
            td.riverFlowDirXZ = Vector2.zero;
        }
        data[tileIndex] = td;

        // If visuals are already built, update the chunk-based water meshes when the water-ness changes.
        // This fixes the case where tiles become Coast/Seas/Ocean after the initial water mesh build.
        if (terrainRenderer != null)
        {
            // For safety, always rebuild when switching to/from a water surface biome.
            bool isWaterBiome = (newBiome == Biome.Ocean || newBiome == Biome.Seas || newBiome == Biome.Coast || newBiome == Biome.Lake || newBiome == Biome.River);
            if (isWaterBiome || td.waterType == TileWaterType.None)
            {
                terrainRenderer.RebuildWaterForTile(tileIndex);
            }
        }
        
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

    // Method to set the current map type from a name string
    public void SetMapTypeName(string mapTypeName)
    {
        currentMapTypeName = mapTypeName;
        if (mapTypeName.Contains("Infernal")) mapType = MapType.Infernal;
        else if (mapTypeName.Contains("Demonic")) mapType = MapType.Demonic;
        else if (mapTypeName.Contains("Frozen") || mapTypeName.Contains("Arctic") || mapTypeName.Contains("Glacial") || mapTypeName.Contains("Ice")) mapType = MapType.IceWorld;
        else mapType = MapType.Standard;
    }

    /// <summary>
    /// Apply a terrain preset to configure elevation parameters.
    /// Preset indices match the MainMenuManager dropdown:
    ///   0=Flat, 1=Smooth, 2=Standard, 3=Mountainous, 4=Alpine
    /// </summary>
    public void ApplyTerrainPreset(int presetIndex)
    {
        // Tier system: noise cutoffs control what % of land tiles become hills/mountains.
        // Elevation ranges: Flat=flatElevationMin..Max, Hills=hillElevationMin..Max,
        // Mountains=mountainElevationMin..Max.
        // elevationExponent further shapes the noise distribution (higher = more flat).
        switch (presetIndex)
        {
            case 0: // Flat — vast plains, very rare hills, almost no mountains
                elevationExponent = 1.8f;
                hillNoiseCutoff = 0.75f;
                mountainNoiseCutoff = 0.95f;
                flatElevationMin = 5.3f;
                flatElevationMax = 5.7f;
                hillElevationMin = 6.5f;
                hillElevationMax = 7.0f;
                mountainElevationMin = 9.5f;
                mountainElevationMax = 10.0f;
                ridgeStrength = 0.0f;
                break;
            case 1: // Smooth — gentle rolling terrain, some hills, rare mountains
                elevationExponent = 1.5f;
                hillNoiseCutoff = 0.55f;
                mountainNoiseCutoff = 0.85f;
                flatElevationMin = 5.0f;
                flatElevationMax = 6.5f;
                hillElevationMin = 7.0f;
                hillElevationMax = 10.0f;
                mountainElevationMin = 10.0f;
                mountainElevationMax = 13.0f;
                ridgeStrength = 0.15f;
                break;
            case 2: // Standard — balanced mix
                elevationExponent = 1.0f;
                hillNoiseCutoff = 0.52f;
                mountainNoiseCutoff = 0.90f;
                flatElevationMin = 6.35f;
                flatElevationMax = 7.25f;
                hillElevationMin = 8.5f;
                hillElevationMax = 11.0f;
                mountainElevationMin = 11.5f;
                mountainElevationMax = 12.5f;
                ridgeStrength = 0.05f;
                break;
            case 3: // Mountainous — lots of hills, frequent mountains
                elevationExponent = 1.0f;
                hillNoiseCutoff = 0.25f;
                mountainNoiseCutoff = 0.50f;
                flatElevationMin = 5.0f;
                flatElevationMax = 6.0f;
                hillElevationMin = 7.5f;
                hillElevationMax = 10.0f;
                mountainElevationMin = 10.0f;
                mountainElevationMax = 15.0f;
                ridgeStrength = 0.50f;
                break;
            case 4: // Alpine — extremely mountainous, dramatic peaks
                elevationExponent = 1.0f;
                hillNoiseCutoff = 0.15f;
                mountainNoiseCutoff = 0.35f;
                flatElevationMin = 5.0f;
                flatElevationMax = 6.5f;
                hillElevationMin = 7.0f;
                hillElevationMax = 10.0f;
                mountainElevationMin = 10.0f;
                mountainElevationMax = 15.0f;
                ridgeStrength = 0.65f;
                break;
            default: // Fallback to Standard
                elevationExponent = 1.2f;
                hillNoiseCutoff = 0.40f;
                mountainNoiseCutoff = 0.70f;
                flatElevationMin = 5.0f;
                flatElevationMax = 6.5f;
                hillElevationMin = 7.0f;
                hillElevationMax = 10.0f;
                mountainElevationMin = 10.0f;
                mountainElevationMax = 15.0f;
                ridgeStrength = 0.35f;
                break;
        }
        Debug.Log($"[PlanetGenerator] Applied terrain preset {presetIndex}: exponent={elevationExponent} hillCutoff={hillNoiseCutoff} mtnCutoff={mountainNoiseCutoff} flat={flatElevationMin}-{flatElevationMax} hills={hillElevationMin}-{hillElevationMax} mtns={mountainElevationMin}-{mountainElevationMax} ridge={ridgeStrength}");
    }

    private Biome GetBiomeForTile(int tileIndex, bool isLand, float temperature, float moisture)
    {
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

        Biome assignedBiome = BiomeHelper.GetBiome(
            isLand, temperature, moisture,
            mapType, planetType,
            northSouth, eastWest
        );
        
        return BiomeHelper.ValidateAndLogBiome(assignedBiome, planetType);
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
            // Pick size first so we can keep the continent away from the top/bottom edges.
            // If we pick the center first, large continents near the poles get clipped by the map boundary,
            // creating the "north band rectangle" artifact.
            int chosenWidthTiles = rand.Next(minW, maxW + 1);
            int chosenHeightTiles = rand.Next(minH, maxH + 1);
            chosenWidthTiles = Mathf.Clamp(chosenWidthTiles, 1, mapWidthTiles);
            chosenHeightTiles = Mathf.Clamp(chosenHeightTiles, 1, mapHeightTiles);

            // Compute a vertical safety margin so the ellipse stamp doesn't get cut off.
            // Horizontal wrap is supported, so we only need to protect Y.
            int halfH = Mathf.Max(0, (int)Mathf.Ceil(chosenHeightTiles * 0.5f));
            int yMin = Mathf.Clamp(halfH, 0, Mathf.Max(0, mapHeightTiles - 1));
            int yMax = Mathf.Clamp((mapHeightTiles - 1) - halfH, 0, Mathf.Max(0, mapHeightTiles - 1));
            if (yMax < yMin)
            {
                // Extremely small maps vs. huge continents: fall back to full range.
                yMin = 0;
                yMax = Mathf.Max(0, mapHeightTiles - 1);
            }

            Vector2Int center = Vector2Int.zero;
            bool accepted = false;

            for (int attempt = 0; attempt < maxAttemptsPerContinent; attempt++) {
                var candidate = new Vector2Int(rand.Next(0, mapWidthTiles), rand.Next(yMin, yMax + 1));
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
                center = new Vector2Int(rand.Next(0, mapWidthTiles), rand.Next(yMin, yMax + 1));
            }

            // (Stamping debug asserts removed)

            continents.Add(new ContinentData {
                name = $"Continent {continentIndex++}",
                center = center,
                widthTiles = chosenWidthTiles,
                heightTiles = chosenHeightTiles
            });
        }

        // (Stamping debug logs removed)

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
        int[] distances = ArrayPoolUtils.RentInt(tileCount);
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
    
    // =====================================================================================
    //  WATER METADATA — populates TileWaterType, lakeId, waterElevation, riverFlowDirXZ
    //  Called once after rivers/coasts are finalized, before chunk mesh build.
    // =====================================================================================

    /// <summary>
    /// Compute water metadata for every tile so the chunk-based water mesh builder can
    /// create per-chunk water surfaces without any per-tile GameObjects.
    /// </summary>
    private void ComputeWaterMetadata(Dictionary<int, HexTileData> tileData, HexGrid hexGrid, int tileCount)
    {
        if (tileData == null || hexGrid == null || tileCount <= 0) return;

        // --- Pass 1: Ocean tiles ---
        float flatY = GameManager.Instance != null ? GameManager.Instance.GetFlatPlaneY() : transform.position.y;
        // Ocean water elevation = coastElevation (shared sea level for all ocean/seas/coast tiles)
        float oceanWaterElev = coastElevation;

        for (int i = 0; i < tileCount; i++)
        {
            if (!tileData.TryGetValue(i, out var td)) continue;
            Biome b = td.biome;
            if (b == Biome.Ocean || b == Biome.Seas || b == Biome.Coast)
            {
                td.waterType = TileWaterType.Ocean;
                td.lakeId = -1;
                td.waterElevation = oceanWaterElev;
                td.riverFlowDirXZ = Vector2.zero;
                tileData[i] = td;
            }
        }

        // --- Pass 2: Lake connected components + spill-rim water height ---
        bool[] visitedLake = new bool[tileCount];
        int nextLakeId = 0;

        for (int i = 0; i < tileCount; i++)
        {
            if (visitedLake[i]) continue;
            if (!tileData.TryGetValue(i, out var seed)) continue;
            if (!seed.isLake) continue;

            // Flood-fill this connected lake body
            var lakeBody = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(i);
            visitedLake[i] = true;

            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                lakeBody.Add(cur);
                foreach (int n in hexGrid.neighbors[cur])
                {
                    if (n < 0 || n >= tileCount || visitedLake[n]) continue;
                    if (!tileData.TryGetValue(n, out var ntd)) continue;
                    if (!ntd.isLake) continue;
                    visitedLake[n] = true;
                    queue.Enqueue(n);
                }
            }

            // Find the lowest adjacent land elevation (spill rim)
            float spillElevation = float.MaxValue;
            foreach (int lakeIdx in lakeBody)
            {
                foreach (int n in hexGrid.neighbors[lakeIdx])
                {
                    if (n < 0 || n >= tileCount) continue;
                    if (!tileData.TryGetValue(n, out var ntd)) continue;
                    if (ntd.isLake) continue; // skip other lake tiles
                    if (ntd.isLand && ntd.elevation < spillElevation)
                    {
                        spillElevation = ntd.elevation;
                    }
                }
            }

            // Also check non-land, non-lake neighbors (e.g. ocean/coast) for the spill rim,
            // since lakes near the coast would otherwise find no rim at all.
            float waterElev;
            float maxBed = float.MinValue;
            float minBed = float.MaxValue;
            foreach (int lakeIdx in lakeBody)
            {
                float e = tileData[lakeIdx].elevation;
                if (e > maxBed) maxBed = e;
                if (e < minBed) minBed = e;
            }
            if (spillElevation < float.MaxValue * 0.5f)
            {
                // Water sits just barely below the spill rim for a full-looking lake
                waterElev = spillElevation - 0.005f;

                // CRITICAL: ensure the carved lake bed actually sits BELOW the water surface.
                // If lakeDepth is too small relative to local terrain variation, it's possible for a "lake"
                // to end up higher than its spill rim. That would make water appear missing (below terrain).
                // Fix at the cause by carving this entire lake body down so maxBed <= waterElev - margin.
                float margin = Mathf.Max(0.02f, lakeDepth * 0.25f);
                if (maxBed > waterElev - margin)
                {
                    float carveDelta = maxBed - (waterElev - margin);
                    for (int j = 0; j < lakeBody.Count; j++)
                    {
                        int lakeIdx = lakeBody[j];
                        var tdAdj = tileData[lakeIdx];
                        tdAdj.elevation = Mathf.Max(0f, tdAdj.elevation - carveDelta);
                        tileData[lakeIdx] = tdAdj;
                    }
                    // Recompute bed range for diagnostics / downstream logic
                    maxBed = float.MinValue;
                    minBed = float.MaxValue;
                    foreach (int lakeIdx in lakeBody)
                    {
                        float e = tileData[lakeIdx].elevation;
                        if (e > maxBed) maxBed = e;
                        if (e < minBed) minBed = e;
                    }
                }
            }
            else
            {
                // Fallback: no land rim found (lake may border only ocean/coast).
                // Use the highest lake-bed elevation plus a visible offset so the water
                // surface is clearly above the carved lake bed.
                waterElev = maxBed + lakeDepth * 0.9f;
            }

            // Stamp metadata on every tile in this lake body
            int lid = nextLakeId++;
            foreach (int lakeIdx in lakeBody)
            {
                var td2 = tileData[lakeIdx];
                td2.waterType = TileWaterType.Lake;
                td2.lakeId = lid;
                td2.waterElevation = waterElev;
                td2.riverFlowDirXZ = Vector2.zero;
                tileData[lakeIdx] = td2;
            }
            
            Debug.Log($"[PlanetGenerator] Lake {lid}: tiles={lakeBody.Count} spillRim={spillElevation:F3} waterElev={waterElev:F3} bedRange=[{minBed:F3}..{maxBed:F3}]");
        }

        // --- Pass 3: River tiles — water height + flow direction ---
        // Phase A: Set initial water elevation and flow direction for each river tile.
        // Phase B: Propagate water levels downstream so the surface is continuous —
        //          each tile's water is at least as high as its downstream neighbor,
        //          eliminating gaps between tiles at different terrain elevations.
        
        // Phase A: Initial per-tile water level + flow direction
        var riverTileIndices = new List<int>();
        for (int i = 0; i < tileCount; i++)
        {
            if (!tileData.TryGetValue(i, out var td)) continue;
            if (!td.isRiver) continue;

            td.waterType = TileWaterType.River;
            td.lakeId = -1;
            // River surface sits above the carved river bed
            td.waterElevation = td.elevation + (riverDepth * 0.75f);

            // Flow direction: toward the neighboring river tile with the lowest elevation.
            Vector3 myCenter = hexGrid.tileCenters[i];
            float lowestElev = float.MaxValue;
            int lowestNeighbor = -1;

            foreach (int n in hexGrid.neighbors[i])
            {
                if (n < 0 || n >= tileCount) continue;
                if (!tileData.TryGetValue(n, out var ntd)) continue;
                if (ntd.isRiver && ntd.elevation < lowestElev)
                {
                    lowestElev = ntd.elevation;
                    lowestNeighbor = n;
                }
            }

            // Fallback: any lowest neighbor (river flows toward ocean/lake)
            if (lowestNeighbor < 0)
            {
                foreach (int n in hexGrid.neighbors[i])
                {
                    if (n < 0 || n >= tileCount) continue;
                    if (!tileData.TryGetValue(n, out var ntd)) continue;
                    if (ntd.elevation < lowestElev)
                    {
                        lowestElev = ntd.elevation;
                        lowestNeighbor = n;
                    }
                }
            }

            if (lowestNeighbor >= 0)
            {
                Vector3 nbrCenter = hexGrid.tileCenters[lowestNeighbor];
                Vector3 dir3 = (nbrCenter - myCenter);
                dir3.y = 0f;
                if (dir3.sqrMagnitude > 0.0001f)
                {
                    dir3.Normalize();
                    td.riverFlowDirXZ = new Vector2(dir3.x, dir3.z);
                }
                else
                {
                    td.riverFlowDirXZ = Vector2.right;
                }
            }
            else
            {
                td.riverFlowDirXZ = Vector2.right;
            }

            tileData[i] = td;
            riverTileIndices.Add(i);
        }
        
        // Phase B: Propagate water levels upstream so the surface is continuous.
        // Sort river tiles from lowest to highest water elevation (downstream first).
        // Then propagate upward: each river tile's water must be >= its lowest river neighbor's water,
        // ensuring the surface connects smoothly between tiles at different terrain heights.
        // Multiple passes handle long chains where propagation needs to ripple upstream.
        if (riverTileIndices.Count > 0)
        {
            // Sort by water elevation ascending (downstream/lowest tiles first)
            riverTileIndices.Sort((a, b) => tileData[a].waterElevation.CompareTo(tileData[b].waterElevation));
            
            // Propagate: for each river tile, ensure it's at least as high as its
            // lowest downstream river neighbor's water level. This fills in the gaps
            // where a high tile's water would float above a low neighbor's water.
            bool changed = true;
            int maxPasses = 20; // safety limit
            int pass = 0;
            while (changed && pass < maxPasses)
            {
                changed = false;
                pass++;
                foreach (int ri in riverTileIndices)
                {
                    var td = tileData[ri];
                    
                    // Find the highest water elevation among adjacent river neighbors
                    // that are lower in terrain (upstream neighbors whose water we should match)
                    float maxNeighborWater = td.waterElevation;
                    foreach (int n in hexGrid.neighbors[ri])
                    {
                        if (n < 0 || n >= tileCount) continue;
                        if (!tileData.TryGetValue(n, out var ntd)) continue;
                        if (!ntd.isRiver) continue;
                        
                        // If a neighbor's water is higher than ours, we need to rise to meet it
                        // (water flows downhill — the upstream tile pushes water down to us)
                        if (ntd.waterElevation > maxNeighborWater)
                        {
                            maxNeighborWater = ntd.waterElevation;
                        }
                    }
                    
                    // Only raise water level, never lower it — ensures continuity
                    // Cap the raise so water doesn't go above the river bed + full riverDepth
                    float maxAllowed = td.elevation + riverDepth;
                    float newWaterElev = Mathf.Min(maxNeighborWater, maxAllowed);
                    
                    if (newWaterElev > td.waterElevation + 0.001f)
                    {
                        td.waterElevation = newWaterElev;
                        tileData[ri] = td;
                        changed = true;
                    }
                }
            }
        }

        Debug.Log($"[PlanetGenerator] ComputeWaterMetadata: {nextLakeId} lake bodies labeled, river/ocean tiles tagged.");
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
        int landCount = 0;
        int hillCount = 0;
        int mountainCount = 0;
        int flatCount = 0;
        int zeroElevCount = 0;
        
        foreach (var kvp in tileData)
        {
            var td = kvp.Value;
            float elev = td.elevation;
            
            if (elev < minElev) minElev = elev;
            if (elev > maxElev) maxElev = elev;
            avgElev += elev;
            
            if (td.isLand) landCount++;
            if (td.elevationTier == ElevationTier.Hill) hillCount++;
            else if (td.elevationTier == ElevationTier.Mountain) mountainCount++;
            else flatCount++;
            
            if (elev <= 0.001f) zeroElevCount++;
        }
        
        avgElev /= tileData.Count;
        
        Debug.LogError($"[ELEVATION DIAGNOSTIC] ========================================");
        Debug.LogError($"[ELEVATION DIAGNOSTIC] Total Tiles: {tileData.Count}, Land: {landCount}");
        Debug.LogError($"[ELEVATION DIAGNOSTIC] Elevation Range (world units): {minElev:F3} to {maxElev:F3} (avg: {avgElev:F3})");
        Debug.LogError($"[ELEVATION DIAGNOSTIC] Elevation Tiers - Flat: {flatCount}, Hills: {hillCount}, Mountains: {mountainCount}");
        Debug.LogError($"[ELEVATION DIAGNOSTIC] Zero/Near-Zero Elevation Tiles: {zeroElevCount}");
        Debug.LogError($"[ELEVATION DIAGNOSTIC] Settings - flat: {flatElevationMin}-{flatElevationMax}, hills: {hillElevationMin}-{hillElevationMax}, mountains: {mountainElevationMin}-{mountainElevationMax}");
        Debug.LogError($"[ELEVATION DIAGNOSTIC] Settings - hillNoiseCutoff: {hillNoiseCutoff}, mountainNoiseCutoff: {mountainNoiseCutoff}, exponent: {elevationExponent}");
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

    /// <summary>
    /// Get the displacement scale from HexMapChunkManager (artistic multiplier, default 1.0).
    /// With world-space elevation, this is typically 1.0 unless terrain is artistically exaggerated.
    /// </summary>
    private float GetActualDisplacementStrength()
    {
        if (terrainRenderer != null)
            return terrainRenderer.DisplacementStrength;

        var chunkManager = FindAnyObjectByType<HexMapChunkManager>(FindObjectsInactive.Include);
        if (chunkManager != null)
            return chunkManager.DisplacementStrength;

        return 1f; // World-space elevation: default scale is 1.0
    }
}
