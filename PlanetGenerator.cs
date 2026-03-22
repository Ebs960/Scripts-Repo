using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.Buffers;
using TMPro;
using System.Threading.Tasks;

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
    [Tooltip("Root GameObject containing orbit-layer visuals (satellites, space stations, orbital units)")]
    public GameObject orbitRoot;
    [Tooltip("Optional root GameObject to parent planet-specific runtime objects such as spawned resources")]
    public GameObject resourcesRoot;

    [Header("Per-layer vertical offsets")]
    [Tooltip("Local Y offset applied to the Surface root when it is enabled (meters). Useful for small visual tweaks).")]
    public float surfaceYOffset = 0f;
    [Tooltip("Local Y offset applied to the Underwater root when it is enabled. Use negative values to sink the underwater root below sea level.")]
    public float underwaterYOffset = 0f;
    [Tooltip("Local Y offset applied to the Atmosphere root when it is enabled. Use positive values to expand atmosphere shells.")]
    public float atmosphereYOffset = 0f;
    [Tooltip("Local Y offset applied to the Orbit root when it is enabled.")]
    public float orbitYOffset = 0f;

    [Header("Orbit")]
    [Tooltip("World-space height above the surface at which orbit-layer units are positioned.")]
    public float orbitHeight = 5f;

    /// <summary>
    /// Returns the orbit height for the given planet. Falls back to Instance or a default value.
    /// </summary>
    public static float GetOrbitHeight(int planetIndex = 0)
    {
        PlanetGenerator gen = null;
        if (GameManager.Instance != null)
            gen = GameManager.Instance.GetPlanetGenerator(planetIndex);
        if (gen == null)
            gen = Instance;
        return gen != null ? gen.orbitHeight : 5f;
    }
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

    private bool ShouldLogDiagnostics()
    {
        if (!enableDiagnostics) return false;
        if (GameManager.Instance == null) return true;
        if (!GameManager.Instance.restrictDiagnosticsToFirstPlanet) return true;
        return GameManager.Instance.currentPlanetIndex == planetIndex;
    }


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

    [Header("Stamping Batching")]
    [Tooltip("How many tiles/islands/lakes to stamp per batch before yielding. Lower = more responsive UI, higher = faster generation.")]
    [SerializeField] private int stampingBatchSize = 128;

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

    [Header("Advanced Terrain Variety")]
    [Range(0f, 0.5f)]
    [Tooltip("How strongly low-frequency warp noise bends the main land elevation sampling coordinates.")]
    public float terrainWarpStrength = 0.18f;
    [Range(0.1f, 2f)]
    [Tooltip("Frequency multiplier for the domain warp field. Lower values create broader bent landforms.")]
    public float terrainWarpFrequencyMultiplier = 0.7f;
    [Range(0.05f, 1.5f)]
    [Tooltip("Frequency multiplier for large terrain provinces that bias land toward plains, uplands, or rough interiors.")]
    public float terrainProvinceFrequencyMultiplier = 0.3f;
    [Range(0f, 0.25f)]
    [Tooltip("How strongly terrain provinces bias the main land signal.")]
    public float terrainProvinceStrength = 0.1f;
    [Range(0.5f, 6f)]
    [Tooltip("Frequency multiplier for the separate hill signal.")]
    public float hillNoiseFrequencyMultiplier = 2.6f;
    [Range(0f, 0.3f)]
    [Tooltip("Contribution of the hill signal to overall terrain roughness.")]
    public float hillNoiseStrength = 0.11f;
    [Range(0.5f, 6f)]
    [Tooltip("Frequency multiplier for the separate mountain-core signal.")]
    public float mountainNoiseFrequencyMultiplier = 1.55f;
    [Range(0f, 0.35f)]
    [Tooltip("Contribution of the mountain-core signal to overall terrain roughness.")]
    public float mountainNoiseStrength = 0.16f;
    [Range(0.5f, 4f)]
    [Tooltip("Frequency multiplier for broad interior basins.")]
    public float basinFrequencyMultiplier = 0.85f;
    [Range(0f, 0.25f)]
    [Tooltip("Depth of interior basin carving as a fraction of terrain tier ranges.")]
    public float basinCarvingStrength = 0.12f;
    [Range(1f, 8f)]
    [Tooltip("Frequency multiplier for narrow valley carving.")]
    public float valleyFrequencyMultiplier = 3.1f;
    [Range(0f, 0.2f)]
    [Tooltip("Strength of narrow valley carving.")]
    public float valleyCarvingStrength = 0.08f;
    [Range(0f, 0.25f)]
    [Tooltip("How strongly some inland highlands are flattened into mesas or plateaus.")]
    public float mesaStrength = 0.1f;
    [Range(0f, 0.2f)]
    [Tooltip("Strength of broken escarpment and step-terrain shaping in rough provinces.")]
    public float escarpmentStrength = 0.09f;
    [Range(0f, 0.6f)]
    [Tooltip("Strength of erosion-like redistribution that softens isolated spikes while preserving larger forms.")]
    public float erosionStrength = 0.22f;

    [Header("Advanced Geology Framework")]
    [Tooltip("Enables tectonic provinces, crust age, margin types, drainage basins, sedimentation, glaciation, and trench/uplift coupling. When off, the legacy terrain pipeline is preserved.")]
    public bool enableAdvancedGeologyFramework = false;
    [Range(0f, 1f)]
    [Tooltip("How strongly the tectonic framework influences terrain uplift, basin placement, and coastal margin behavior.")]
    public float geologyFrameworkStrength = 0.42f;
    [Range(0.05f, 1f)]
    [Tooltip("Frequency multiplier for tectonic provinces and crustal structure.")]
    public float tectonicProvinceFrequencyMultiplier = 0.3f;
    [Range(0f, 1f)]
    [Tooltip("How strongly precomputed drainage basins influence valleys, river corridors, and depositional lowlands.")]
    public float drainageBasinStrength = 0.38f;
    [Range(0f, 1f)]
    [Tooltip("How strongly crust age affects terrain smoothing, shield flattening, and erosion resistance.")]
    public float crustAgeStrength = 0.45f;
    [Range(0f, 1f)]
    [Tooltip("How strongly depositional smoothing builds deltas, foreland plains, and basin fills.")]
    public float sedimentationStrength = 0.4f;
    [Range(0f, 1f)]
    [Tooltip("How strongly cold, wet highlands are reshaped into glaciated valleys and fjord-prone coasts.")]
    public float glaciationStrength = 0.35f;

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
    [Range(0f, 15f)]
    [Tooltip("Lowest flat land elevation (world units). The lowest flat tiles sit here.")]
    public float flatElevationMin = 5.0f;
    [Range(0f, 15f)]
    [Tooltip("Highest flat land elevation (world units). The highest flat tiles reach here.")]
    public float flatElevationMax = 6.5f;

    [Range(0f, 20f)]
    [Tooltip("Lowest hill elevation (world units). The shortest hill starts here.")]
    public float hillElevationMin = 7.0f;
    [Range(0f, 20f)]
    [Tooltip("Highest hill elevation (world units). The tallest hill reaches here.")]
    public float hillElevationMax = 10.0f;

    [Range(0f, 25f)]
    [Tooltip("Lowest mountain elevation (world units). The shortest mountain starts here.")]
    public float mountainElevationMin = 10.0f;
    [Range(0f, 30f)]
    [Tooltip("Highest mountain elevation (world units). The tallest peak reaches here.")]
    public float mountainElevationMax = 15.0f;

    [Header("Water Elevation")]
    [Range(-5f, 40f)]
    [Tooltip("Ocean floor elevation in world units (typically 0 = flat plane level).")]
    public float oceanElevation = 0f;
    [Range(-5f, 40f)]
    [Tooltip("Shallow seas elevation in world units.")]
    public float seasElevation = 0.15f;
    [Range(-5f, 40f)]
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
    [Range(0f, 0.4f)]
    [Tooltip("How far rivers are allowed to meander sideways away from the direct source-to-coast line.")]
    public float riverMeanderAmplitude = 0.18f;
    [Range(0.5f, 3f)]
    [Tooltip("How many broad bends a river prefers along its route. Higher values create more wiggles.")]
    public float riverMeanderFrequency = 1.15f;
    [Range(0f, 3f)]
    [Tooltip("Penalty against abrupt river turns. Higher values produce smoother sweeping bends instead of zig-zags.")]
    public float riverTurnResistance = 0.8f;
    [Header("Floodplains")]
    [Tooltip("When enabled, low river-adjacent land is flattened and enriched into fertile floodplain belts.")]
    public bool enableFloodplains = true;
    [Range(1, 4)]
    [Tooltip("How many land tiles outward from a river can receive floodplain effects.")]
    public int floodplainRange = 2;
    [Range(0f, 1f)]
    [Tooltip("How strongly river-adjacent land is flattened toward broad alluvial lowlands.")]
    public float floodplainFlattenStrength = 0.38f;
    [Range(0f, 0.5f)]
    [Tooltip("Moisture added to floodplain-eligible tiles near rivers.")]
    public float floodplainMoistureBoost = 0.16f;
    [Range(0f, 1f)]
    [Tooltip("How strongly floodplains bias dry banks into plains/temperate/swamp style fertile land.")]
    public float floodplainFertilityBias = 0.5f;

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
    [Header("Lake Shape")]
    [Range(0f, 1f)]
    [Tooltip("How irregular lake shorelines become. Higher values create more coves, peninsulas, and broken shore edges.")]
    public float lakeShoreIrregularity = 0.65f;
    [Range(0f, 1f)]
    [Tooltip("How strongly lakes stretch into long basins instead of round bowls.")]
    public float lakeElongationStrength = 0.55f;
    [Range(0f, 1f)]
    [Tooltip("How strongly shoreline growth favors coves and embayments instead of smooth outlines.")]
    public float lakeCoveStrength = 0.45f;
    [Header("Smart Coast Shaping")]
    [Tooltip("How many embayment candidates to carve per map. Higher values create more bays and inlets.")]
    public int smartEmbaymentCount = 3;
    [Tooltip("Minimum inland length for a smart embayment.")]
    public int smartEmbaymentMinLength = 2;
    [Tooltip("Maximum inland length for a smart embayment.")]
    public int smartEmbaymentMaxLength = 5;

    [Tooltip("How many peninsula candidates to grow per map. Higher values create more coherent coastal land fingers.")]
    public int smartPeninsulaCount = 2;
    [Tooltip("Minimum offshore length for a smart peninsula.")]
    public int smartPeninsulaMinLength = 2;
    [Tooltip("Maximum offshore length for a smart peninsula.")]
    public int smartPeninsulaMaxLength = 4;
    [Tooltip("Minimum wrapped hex distance between accepted smart coast features.")]
    public int smartCoastFeatureSpacing = 5;

    [Tooltip("Minimum total land tiles required to allow coast stamping")]
    public int minLandTilesForCoastStamps = 24;

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
    private enum TectonicProvinceType
    {
        StableShield = 0,
        FoldBelt = 1,
        RiftZone = 2,
        ForelandBasin = 3,
        VolcanicArc = 4,
        PassiveMargin = 5
    }

    private enum CoastalMarginType
    {
        None = 0,
        Passive = 1,
        Active = 2,
        Rifted = 3,
        Deltaic = 4,
        Glaciated = 5
    }

    HexGrid grid;
    public HexGrid Grid => grid;
    NoiseSampler noise;
    public Dictionary<int, HexTileData> data = new();
    public Dictionary<int, HexTileData> baseData = new();
    private int[] geologyProvinceMap;
    private int[] geologyMarginTypeMap;
    private float[] geologyStressMap;
    private float[] geologyAgeMap;
    private float[] geologyDrainageMap;
    private float[] geologySedimentMap;
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
        ReleaseGeologyCaches();

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
            var response = mgr.GetSeasonResponse(tile.biome, newSeason, planetIndex);
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
        float elevFreqPeriodic = 1f / (mapWidth * 0.38f);
        var terrainMotifRand = new System.Random(unchecked(seed ^ 0x71A9C5));
        float motifFoldedRanges = Mathf.Lerp(0.85f, 1.25f, (float)terrainMotifRand.NextDouble());
        float motifBasins = Mathf.Lerp(0.8f, 1.25f, (float)terrainMotifRand.NextDouble());
        float motifPlateaus = Mathf.Lerp(0.75f, 1.3f, (float)terrainMotifRand.NextDouble());
        float motifEscarpments = Mathf.Lerp(0.8f, 1.25f, (float)terrainMotifRand.NextDouble());
        float motifRuggedness = Mathf.Lerp(0.9f, 1.2f, (float)terrainMotifRand.NextDouble());

        // DIAGNOSTICS: report key settings and grid stats
        if (enableDiagnostics)
        {
            Debug.Log($"[PlanetGenerator][Diag] mapWidth={mapWidth:F1} mapHeight={mapHeight:F1} tiles={tileCount}");
            Debug.Log($"[PlanetGenerator][Diag] latitudeInfluence={latitudeInfluence} latitudeExponent={latitudeExponent} temperatureBias={temperatureBias} moistureBias={moistureBias}");
            Debug.Log($"[PlanetGenerator][Diag] terrain motifs folded={motifFoldedRanges:F2} basins={motifBasins:F2} plateaus={motifPlateaus:F2} escarpments={motifEscarpments:F2} rugged={motifRuggedness:F2}");

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

        List<int> BuildOrganicLakeTiles(int centerIdx, int radius, System.Random lakeRand, int minLakeTiles, int maxLakeTiles)
        {
            if (centerIdx < 0 || centerIdx >= tileCount || !isLandTile[centerIdx])
                return null;

            Vector2Int centerCoord = tileCoords[centerIdx];
            float angle = (float)(lakeRand.NextDouble() * Mathf.PI * 2f);
            Vector2 majorAxis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 minorAxis = new Vector2(-majorAxis.y, majorAxis.x);
            float elongation = Mathf.Lerp(1f, 1.55f, lakeElongationStrength * Mathf.Lerp(0.5f, 1f, (float)lakeRand.NextDouble()));
            float majorRadius = Mathf.Max(1.2f, radius * elongation);
            float minorRadius = Mathf.Max(1.0f, radius * Mathf.Lerp(0.72f, 1.05f, 1f - lakeElongationStrength * 0.75f));
            float lobeOffset = majorRadius * Mathf.Lerp(0.12f, 0.38f, lakeElongationStrength);
            float phase = (float)(lakeRand.NextDouble() * Mathf.PI * 2f);
            int targetLakeTiles = Mathf.Clamp(
                Mathf.RoundToInt((1 + 3 * radius * (radius + 1)) * Mathf.Lerp(0.7f, 1.02f, (float)lakeRand.NextDouble())),
                minLakeTiles,
                maxLakeTiles);
            int hardMaxTiles = Mathf.Max(targetLakeTiles + 2, Mathf.RoundToInt(targetLakeTiles * 1.2f));

            var lakeSet = new HashSet<int> { centerIdx };
            var frontier = new HashSet<int>();
            foreach (int neighbor in grid.neighbors[centerIdx])
            {
                if (neighbor >= 0 && neighbor < tileCount && isLandTile[neighbor] && !isLakeTile[neighbor])
                    frontier.Add(neighbor);
            }

            float minAcceptance = -0.12f;
            while (frontier.Count > 0 && lakeSet.Count < hardMaxTiles)
            {
                int bestIdx = -1;
                float bestScore = float.NegativeInfinity;

                foreach (int candidate in frontier)
                {
                    if (candidate < 0 || candidate >= tileCount || !isLandTile[candidate] || isLakeTile[candidate])
                        continue;

                    Vector2Int coord = tileCoords[candidate];
                    Vector2 local = new Vector2(WrappedDelta(coord.x, centerCoord.x, tilesX), coord.y - centerCoord.y);
                    float major = Vector2.Dot(local, majorAxis);
                    float minor = Vector2.Dot(local, minorAxis);

                    float primaryDist = Mathf.Sqrt((major * major) / Mathf.Max(1f, majorRadius * majorRadius) + (minor * minor) / Mathf.Max(1f, minorRadius * minorRadius));
                    Vector2 forwardLobeOffset = majorAxis * lobeOffset;
                    Vector2 backwardLobeOffset = -majorAxis * lobeOffset * 0.6f;
                    Vector2 toForwardLobe = local - forwardLobeOffset;
                    Vector2 toBackwardLobe = local - backwardLobeOffset;
                    float forwardLobeDist = Mathf.Sqrt((Vector2.Dot(toForwardLobe, majorAxis) * Vector2.Dot(toForwardLobe, majorAxis)) / Mathf.Max(1f, majorRadius * majorRadius * 0.9f)
                        + (Vector2.Dot(toForwardLobe, minorAxis) * Vector2.Dot(toForwardLobe, minorAxis)) / Mathf.Max(1f, minorRadius * minorRadius * 0.72f));
                    float backwardLobeDist = Mathf.Sqrt((Vector2.Dot(toBackwardLobe, majorAxis) * Vector2.Dot(toBackwardLobe, majorAxis)) / Mathf.Max(1f, majorRadius * majorRadius * 0.65f)
                        + (Vector2.Dot(toBackwardLobe, minorAxis) * Vector2.Dot(toBackwardLobe, minorAxis)) / Mathf.Max(1f, minorRadius * minorRadius * 0.82f));
                    float combinedDist = Mathf.Min(primaryDist, Mathf.Min(forwardLobeDist * 1.04f, backwardLobeDist * 1.1f));
                    if (combinedDist > 1.35f)
                        continue;

                    float shoreNoiseA = noise.GetElevationPeriodic(new Vector2(coord.x + 500f, coord.y + 500f), mapWidth, mapHeight, elevFreqPeriodic * 5.5f);
                    float shoreNoiseB = noise.GetElevationPeriodic(new Vector2(coord.x + 1740f, coord.y + 320f), mapWidth, mapHeight, elevFreqPeriodic * 8.75f);
                    float localAngle = Mathf.Atan2(minor, major);
                    float embaymentMask = Mathf.Sin(localAngle * (2.25f + lakeCoveStrength * 2.75f) + phase) * 0.5f + 0.5f;
                    int touchingLakeNeighbors = 0;
                    foreach (int neighbor in grid.neighbors[candidate])
                    {
                        if (neighbor >= 0 && neighbor < tileCount && lakeSet.Contains(neighbor))
                            touchingLakeNeighbors++;
                    }

                    float radialCore = 1f - combinedDist;
                    float shorelineBias = (shoreNoiseA - 0.5f) * (0.55f + lakeShoreIrregularity * 0.75f)
                        + (shoreNoiseB - 0.5f) * 0.28f * lakeShoreIrregularity;
                    float coveBias = (embaymentMask - 0.5f) * lakeCoveStrength * 0.4f;
                    float cohesionBias = touchingLakeNeighbors * 0.16f;
                    float fillBias = lakeSet.Count < targetLakeTiles ? 0.08f : -0.06f;
                    float score = radialCore * 1.6f + shorelineBias + coveBias + cohesionBias + fillBias;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestIdx = candidate;
                    }
                }

                if (bestIdx < 0 || bestScore < minAcceptance)
                    break;

                frontier.Remove(bestIdx);
                lakeSet.Add(bestIdx);

                foreach (int neighbor in grid.neighbors[bestIdx])
                {
                    if (neighbor >= 0 && neighbor < tileCount && isLandTile[neighbor] && !isLakeTile[neighbor] && !lakeSet.Contains(neighbor))
                        frontier.Add(neighbor);
                }

                if (lakeSet.Count >= targetLakeTiles && bestScore < 0.18f)
                    break;
            }

            if (lakeSet.Count < minLakeTiles || lakeSet.Count > maxLakeTiles)
                return null;

            return lakeSet.ToList();
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
                if (counter >= batch)
                {
                    counter = 0;
                    yield return null; // Yield after each batch to keep UI responsive
                }
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
                if (counter >= batch)
                {
                    counter = 0;
                    yield return null; // Yield after each batch to keep UI responsive
                }
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

            if (generateIslandChains && islandsPerChain >= 2)
            {
                // --- Arc-based island chains ---
                // Group islands into chains that follow curved arcs (like volcanic arcs).
                int chainsNeeded = Mathf.Max(1, numberOfIslands / islandsPerChain);
                int islandsRemaining = numberOfIslands;

                for (int chain = 0; chain < chainsNeeded && islandsRemaining > 0; chain++)
                {
                    // Find a suitable ocean seed far from land
                    int chainSeed = -1;
                    for (int att = 0; att < 200; att++)
                    {
                        attempts++;
                        int idx = islandRand.Next(0, tileCount);
                        if (isLandTile[idx]) continue;
                        if (HasLandWithinDistance(idx, islandMinDistance, isLandTile)) continue;
                        chainSeed = idx;
                        break;
                    }
                    if (chainSeed < 0) continue;

                    // Pick an arc direction using noise for natural curve
                    Vector2Int seedCoord = tileCoords[chainSeed];
                    float arcAngle = (float)(islandRand.NextDouble() * Mathf.PI * 2f);
                    float arcCurve = 0.15f + (float)(islandRand.NextDouble() * 0.2f); // gentle curve per step
                    int chainLength = Mathf.Min(islandsPerChain, islandsRemaining);
                    int spacing = Mathf.Max(islandMaxRadius * 2 + 2, 4);

                    Vector2 pos = new Vector2(seedCoord.x, seedCoord.y);
                    float angle = arcAngle;

                    for (int ci = 0; ci < chainLength; ci++)
                    {
                        // Wrap X coordinate for cylindrical map
                        int px = ((int)Mathf.Round(pos.x) % tilesX + tilesX) % tilesX;
                        int py = Mathf.Clamp((int)Mathf.Round(pos.y), 0, tilesZ - 1);
                        int idx = py * tilesX + px;

                        if (idx >= 0 && idx < tileCount && !isLandTile[idx])
                        {
                            int radius = islandRand.Next(islandMinRadius, islandMaxRadius + 1);
                            var stamp = StampCircleBatched(tileCoords[idx], radius, true, false);
                            while (stamp.MoveNext()) yield return null;
                            islandsStamped++;
                            islandsRemaining--;
                        }

                        // Advance along arc
                        angle += arcCurve;
                        pos.x += Mathf.Cos(angle) * spacing;
                        pos.y += Mathf.Sin(angle) * spacing;
                    }
                }

                // Fill remaining with scattered islands
                while (islandsStamped < numberOfIslands && attempts < maxAttempts)
                {
                    attempts++;
                    int idx = islandRand.Next(0, tileCount);
                    if (isLandTile[idx]) continue;
                    if (HasLandWithinDistance(idx, islandMinDistance, isLandTile)) continue;
                    int radius = islandRand.Next(islandMinRadius, islandMaxRadius + 1);
                    var stamp = StampCircleBatched(tileCoords[idx], radius, true, false);
                    while (stamp.MoveNext()) yield return null;
                    islandsStamped++;
                }
            }
            else
            {
                // --- Original scattered placement ---
                while (islandsStamped < numberOfIslands && attempts < maxAttempts)
                {
                    attempts++;
                    int idx = islandRand.Next(0, tileCount);
                    if (isLandTile[idx]) continue;
                    if (HasLandWithinDistance(idx, islandMinDistance, isLandTile)) continue;

                    int radius = islandRand.Next(islandMinRadius, islandMaxRadius + 1);
                    var stamp = StampCircleBatched(tileCoords[idx], radius, true, false);
                    while (stamp.MoveNext())
                    {
                        yield return null;
                    }
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
                        var stamp = StampCircleBatched(tileCoords[idx], radius, true, false);
                        while (stamp.MoveNext())
                        {
                            yield return null;
                        }
                        islandsStamped++;
                    }
                }
            }
        }

        // (Stamping debug logs removed)

        BuildAdvancedGeologyFramework(tileCoords, isLandTile, isLakeTile, tilesX, mapWidth, mapHeight, elevFreqPeriodic);

        // ---------- 2.75. Smart coastal shaping ----------
        // Replace random bite/spur stamps with scored embayment and peninsula roots
        // so coastlines respond to nearby land depth and offshore exposure.
        int _currentLandCount = 0;
        for (int _i = 0; _i < tileCount; _i++) if (isLandTile[_i]) _currentLandCount++;
        if (_currentLandCount >= minLandTilesForCoastStamps)
        {
            System.Random coastRand = new System.Random(unchecked((int)(seed ^ 0xBEEF)));
            float coastShapeFreq = 1f / Mathf.Max(1f, mapWidth * 0.35f);
            int minFeatureSpacing = Mathf.Max(2, smartCoastFeatureSpacing);

            int CountLandNeighbors(int idx)
            {
                int count = 0;
                foreach (int n in grid.neighbors[idx])
                    if (n >= 0 && n < tileCount && isLandTile[n]) count++;
                return count;
            }

            int CountWaterNeighbors(int idx)
            {
                int count = 0;
                foreach (int n in grid.neighbors[idx])
                    if (n >= 0 && n < tileCount && !isLandTile[n]) count++;
                return count;
            }

            Vector2 AverageDirectionToState(int idx, bool towardLand)
            {
                Vector2 dir = Vector2.zero;
                foreach (int n in grid.neighbors[idx])
                {
                    if (n < 0 || n >= tileCount) continue;
                    bool matches = towardLand ? isLandTile[n] : !isLandTile[n];
                    if (!matches) continue;
                    dir += new Vector2(tileCoords[n].x - tileCoords[idx].x, tileCoords[n].y - tileCoords[idx].y);
                }
                return dir.sqrMagnitude > 0.001f ? dir.normalized : Vector2.zero;
            }

            bool IsFarEnoughFromChosen(int idx, List<int> chosenRoots)
            {
                foreach (int root in chosenRoots)
                {
                    if (HexDistanceWrapped(tileCoords[idx], tileCoords[root], tilesX) < minFeatureSpacing)
                        return false;
                }
                return true;
            }

            int WalkTowardContext(int startIdx, int steps, bool seekLand, Vector2 preferredDir)
            {
                int current = startIdx;
                Vector2 currentDir = preferredDir;
                for (int step = 0; step < steps; step++)
                {
                    int bestNext = -1;
                    float bestScore = float.NegativeInfinity;
                    foreach (int n in grid.neighbors[current])
                    {
                        if (n < 0 || n >= tileCount) continue;
                        if (seekLand != isLandTile[n]) continue;

                        Vector2 stepDir = new Vector2(tileCoords[n].x - tileCoords[current].x, tileCoords[n].y - tileCoords[current].y);
                        if (stepDir.sqrMagnitude > 0.001f) stepDir.Normalize();
                        float align = currentDir.sqrMagnitude > 0.001f ? Vector2.Dot(currentDir, stepDir) : 0f;
                        int support = seekLand ? CountLandNeighbors(n) : CountWaterNeighbors(n);
                        float noiseBias = noise != null
                            ? noise.GetElevationPeriodic(new Vector2(tileCoords[n].x + 530f, tileCoords[n].y + 890f), mapWidth, mapHeight, coastShapeFreq * 1.4f) - 0.5f
                            : 0f;
                        float score = support + align * 2f + noiseBias;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestNext = n;
                        }
                    }

                    if (bestNext < 0) break;
                    Vector2 chosenDir = new Vector2(tileCoords[bestNext].x - tileCoords[current].x, tileCoords[bestNext].y - tileCoords[current].y);
                    if (chosenDir.sqrMagnitude > 0.001f) chosenDir.Normalize();
                    if (currentDir.sqrMagnitude > 0.001f)
                        currentDir = (currentDir * 0.65f + chosenDir * 0.35f).normalized;
                    else
                        currentDir = chosenDir;
                    current = bestNext;
                }
                return current;
            }

            bool TryCarveEmbayment(int rootIdx)
            {
                if (!isLandTile[rootIdx]) return false;
                int length = coastRand.Next(Mathf.Max(1, smartEmbaymentMinLength), Mathf.Max(smartEmbaymentMinLength, smartEmbaymentMaxLength) + 1);
                Vector2 inlandDir = -AverageDirectionToState(rootIdx, false);
                int current = rootIdx;
                var carved = new HashSet<int> { rootIdx };
                var carvedList = new List<int> { rootIdx };

                for (int step = 0; step < length; step++)
                {
                    int bestNext = -1;
                    float bestScore = float.NegativeInfinity;
                    foreach (int n in grid.neighbors[current])
                    {
                        if (n < 0 || n >= tileCount) continue;
                        if (!isLandTile[n] || carved.Contains(n)) continue;

                        Vector2 stepDir = new Vector2(tileCoords[n].x - tileCoords[current].x, tileCoords[n].y - tileCoords[current].y);
                        if (stepDir.sqrMagnitude > 0.001f) stepDir.Normalize();
                        float align = inlandDir.sqrMagnitude > 0.001f ? Vector2.Dot(inlandDir, stepDir) : 0f;
                        int landSupport = CountLandNeighbors(n);
                        int waterSupport = CountWaterNeighbors(n);
                        float weakness = Mathf.Clamp01((4f - landSupport) / 4f);
                        float geologyBias = 0f;
                        if (enableAdvancedGeologyFramework && geologyProvinceMap != null && geologyMarginTypeMap != null)
                        {
                            var province = (TectonicProvinceType)geologyProvinceMap[n];
                            var margin = (CoastalMarginType)geologyMarginTypeMap[n];
                            if (province == TectonicProvinceType.ForelandBasin || province == TectonicProvinceType.RiftZone) geologyBias += 0.8f;
                            if (margin == CoastalMarginType.Deltaic || margin == CoastalMarginType.Passive) geologyBias += 0.55f;
                            if (margin == CoastalMarginType.Active) geologyBias -= 0.35f;
                        }
                        float score = align * 2.2f + waterSupport * 0.7f + weakness + geologyBias;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestNext = n;
                        }
                    }

                    if (bestNext < 0) break;
                    carved.Add(bestNext);
                    carvedList.Add(bestNext);

                    if (step < length - 1)
                    {
                        int flank = -1;
                        int flankWater = -1;
                        foreach (int n in grid.neighbors[bestNext])
                        {
                            if (n < 0 || n >= tileCount) continue;
                            if (!isLandTile[n] || carved.Contains(n)) continue;
                            int waterSupport = CountWaterNeighbors(n);
                            if (waterSupport > flankWater)
                            {
                                flankWater = waterSupport;
                                flank = n;
                            }
                        }
                        if (flank >= 0 && flankWater >= 2 && coastRand.NextDouble() < 0.35)
                        {
                            carved.Add(flank);
                            carvedList.Add(flank);
                        }
                    }

                    Vector2 chosenDir = new Vector2(tileCoords[bestNext].x - tileCoords[current].x, tileCoords[bestNext].y - tileCoords[current].y);
                    if (chosenDir.sqrMagnitude > 0.001f) chosenDir.Normalize();
                    if (inlandDir.sqrMagnitude > 0.001f)
                        inlandDir = (inlandDir * 0.7f + chosenDir * 0.3f).normalized;
                    else
                        inlandDir = chosenDir;
                    current = bestNext;
                }

                if (carvedList.Count < 2) return false;
                foreach (int idx in carvedList)
                    isLandTile[idx] = false;
                return true;
            }

            bool TryGrowPeninsula(int rootIdx)
            {
                if (isLandTile[rootIdx]) return false;
                int length = coastRand.Next(Mathf.Max(1, smartPeninsulaMinLength), Mathf.Max(smartPeninsulaMinLength, smartPeninsulaMaxLength) + 1);
                Vector2 outwardDir = AverageDirectionToState(rootIdx, false);
                int current = rootIdx;
                var added = new HashSet<int> { rootIdx };
                var addedList = new List<int> { rootIdx };

                for (int step = 0; step < length; step++)
                {
                    int bestNext = -1;
                    float bestScore = float.NegativeInfinity;
                    foreach (int n in grid.neighbors[current])
                    {
                        if (n < 0 || n >= tileCount) continue;
                        if (isLandTile[n] || added.Contains(n)) continue;

                        Vector2 stepDir = new Vector2(tileCoords[n].x - tileCoords[current].x, tileCoords[n].y - tileCoords[current].y);
                        if (stepDir.sqrMagnitude > 0.001f) stepDir.Normalize();
                        float align = outwardDir.sqrMagnitude > 0.001f ? Vector2.Dot(outwardDir, stepDir) : 0f;
                        int waterSupport = CountWaterNeighbors(n);
                        int landSupport = CountLandNeighbors(n);
                        float geologyBias = 0f;
                        if (enableAdvancedGeologyFramework && geologyProvinceMap != null && geologyMarginTypeMap != null)
                        {
                            var province = (TectonicProvinceType)geologyProvinceMap[n];
                            var margin = (CoastalMarginType)geologyMarginTypeMap[n];
                            if (province == TectonicProvinceType.VolcanicArc || province == TectonicProvinceType.FoldBelt || province == TectonicProvinceType.RiftZone) geologyBias += 0.75f;
                            if (margin == CoastalMarginType.Active || margin == CoastalMarginType.Rifted || margin == CoastalMarginType.Glaciated) geologyBias += 0.55f;
                            if (margin == CoastalMarginType.Deltaic) geologyBias -= 0.4f;
                        }
                        float score = align * 2.1f + waterSupport * 0.6f - landSupport * 0.35f + geologyBias;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestNext = n;
                        }
                    }

                    if (bestNext < 0) break;
                    added.Add(bestNext);
                    addedList.Add(bestNext);

                    if (step == 0)
                    {
                        int shoulder = -1;
                        int shoulderWater = -1;
                        foreach (int n in grid.neighbors[bestNext])
                        {
                            if (n < 0 || n >= tileCount) continue;
                            if (isLandTile[n] || added.Contains(n)) continue;
                            int waterSupport = CountWaterNeighbors(n);
                            if (waterSupport > shoulderWater)
                            {
                                shoulderWater = waterSupport;
                                shoulder = n;
                            }
                        }
                        if (shoulder >= 0 && shoulderWater >= 3 && coastRand.NextDouble() < 0.45)
                        {
                            added.Add(shoulder);
                            addedList.Add(shoulder);
                        }
                    }

                    Vector2 chosenDir = new Vector2(tileCoords[bestNext].x - tileCoords[current].x, tileCoords[bestNext].y - tileCoords[current].y);
                    if (chosenDir.sqrMagnitude > 0.001f) chosenDir.Normalize();
                    if (outwardDir.sqrMagnitude > 0.001f)
                        outwardDir = (outwardDir * 0.65f + chosenDir * 0.35f).normalized;
                    else
                        outwardDir = chosenDir;
                    current = bestNext;
                }

                if (addedList.Count < 2) return false;
                bool connectsToLand = false;
                foreach (int idx in addedList)
                {
                    foreach (int n in grid.neighbors[idx])
                    {
                        if (n >= 0 && n < tileCount && isLandTile[n])
                        {
                            connectsToLand = true;
                            break;
                        }
                    }
                    if (connectsToLand) break;
                }
                if (!connectsToLand) return false;

                foreach (int idx in addedList)
                    isLandTile[idx] = true;
                return true;
            }

            var embaymentCandidates = new List<(int idx, float score)>();
            var peninsulaCandidates = new List<(int idx, float score)>();
            for (int i = 0; i < tileCount; i++)
            {
                int landNeighbors = CountLandNeighbors(i);
                int waterNeighbors = CountWaterNeighbors(i);
                if (isLandTile[i] && waterNeighbors > 0)
                {
                    Vector2 inlandDir = -AverageDirectionToState(i, false);
                    int inlandProbe = WalkTowardContext(i, 2, true, inlandDir);
                    int inlandSupport = CountLandNeighbors(inlandProbe);
                    if (inlandSupport >= 3)
                    {
                        float noiseBias = noise != null
                            ? noise.GetElevationPeriodic(new Vector2(tileCoords[i].x + 1500f, tileCoords[i].y + 500f), mapWidth, mapHeight, coastShapeFreq) - 0.5f
                            : 0f;
                        float geologyBias = 0f;
                        if (enableAdvancedGeologyFramework && geologyProvinceMap != null && geologyMarginTypeMap != null)
                        {
                            var province = (TectonicProvinceType)geologyProvinceMap[i];
                            var margin = (CoastalMarginType)geologyMarginTypeMap[i];
                            if (province == TectonicProvinceType.ForelandBasin || province == TectonicProvinceType.RiftZone) geologyBias += 1.0f;
                            if (margin == CoastalMarginType.Passive || margin == CoastalMarginType.Deltaic) geologyBias += 0.65f;
                            if (margin == CoastalMarginType.Active) geologyBias -= 0.45f;
                        }
                        float score = waterNeighbors * 2.0f + inlandSupport * 0.8f + noiseBias + geologyBias;
                        embaymentCandidates.Add((i, score));
                    }
                }
                else if (!isLandTile[i] && landNeighbors > 0)
                {
                    Vector2 outwardDir = AverageDirectionToState(i, false);
                    int offshoreProbe = WalkTowardContext(i, 2, false, outwardDir);
                    int offshoreSupport = CountWaterNeighbors(offshoreProbe);
                    if (offshoreSupport >= 3)
                    {
                        float noiseBias = noise != null
                            ? noise.GetElevationPeriodic(new Vector2(tileCoords[i].x + 2300f, tileCoords[i].y + 1200f), mapWidth, mapHeight, coastShapeFreq) - 0.5f
                            : 0f;
                        float geologyBias = 0f;
                        if (enableAdvancedGeologyFramework && geologyProvinceMap != null && geologyMarginTypeMap != null)
                        {
                            var province = (TectonicProvinceType)geologyProvinceMap[i];
                            var margin = (CoastalMarginType)geologyMarginTypeMap[i];
                            if (province == TectonicProvinceType.VolcanicArc || province == TectonicProvinceType.FoldBelt || province == TectonicProvinceType.RiftZone) geologyBias += 0.9f;
                            if (margin == CoastalMarginType.Active || margin == CoastalMarginType.Rifted || margin == CoastalMarginType.Glaciated) geologyBias += 0.6f;
                            if (margin == CoastalMarginType.Deltaic) geologyBias -= 0.4f;
                        }
                        float score = landNeighbors * 1.8f + offshoreSupport * 0.75f + noiseBias + geologyBias;
                        peninsulaCandidates.Add((i, score));
                    }
                }
            }

            embaymentCandidates.Sort((a, b) => b.score.CompareTo(a.score));
            peninsulaCandidates.Sort((a, b) => b.score.CompareTo(a.score));

            var chosenEmbaymentRoots = new List<int>();
            int embaymentsApplied = 0;
            foreach (var candidate in embaymentCandidates)
            {
                if (embaymentsApplied >= Mathf.Max(0, smartEmbaymentCount)) break;
                if (!IsFarEnoughFromChosen(candidate.idx, chosenEmbaymentRoots)) continue;
                if (!TryCarveEmbayment(candidate.idx)) continue;
                chosenEmbaymentRoots.Add(candidate.idx);
                embaymentsApplied++;
                yield return null;
            }

            var chosenPeninsulaRoots = new List<int>();
            int peninsulasApplied = 0;
            foreach (var candidate in peninsulaCandidates)
            {
                if (peninsulasApplied >= Mathf.Max(0, smartPeninsulaCount)) break;
                if (!IsFarEnoughFromChosen(candidate.idx, chosenPeninsulaRoots)) continue;
                if (!TryGrowPeninsula(candidate.idx)) continue;
                chosenPeninsulaRoots.Add(candidate.idx);
                peninsulasApplied++;
                yield return null;
            }

            if (ShouldLogDiagnostics())
                Debug.Log($"[PlanetGenerator] Smart coast shaping: {embaymentsApplied} embayments, {peninsulasApplied} peninsulas applied.");
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
                List<int> lakeTiles = BuildOrganicLakeTiles(centerIdx, radius, lakeRand, minLakeTiles, maxLakeTiles);

                if (lakeTiles == null || lakeTiles.Count < minLakeTiles || lakeTiles.Count > maxLakeTiles) continue;

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
                    List<int> lakeTiles = BuildOrganicLakeTiles(centerIdx, radius, lakeRand, minLakeTiles, maxLakeTiles);

                    if (lakeTiles == null || lakeTiles.Count < minLakeTiles || lakeTiles.Count > maxLakeTiles) continue;
                    foreach (int tileIdx in lakeTiles) {
                        isLandTile[tileIdx] = false;
                        isLakeTile[tileIdx] = true;
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
                var stamp = StampCircleBatched(tileCoords[i], fallbackRadius, false, true);
                while (stamp.MoveNext())
                {
                    yield return null;
                }
                lakeCenters.Add(tileCoords[i]);
                lakesStamped++;
                break;
            }
        }
        // (Stamping debug logs removed)

        BuildAdvancedGeologyFramework(tileCoords, isLandTile, isLakeTile, tilesX, mapWidth, mapHeight, elevFreqPeriodic);

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

        Vector2 WrapTerrainTilePos(Vector2 tilePos)
        {
            return new Vector2(
                Mathf.Repeat(tilePos.x + mapWidth * 0.5f, mapWidth) - mapWidth * 0.5f,
                Mathf.Clamp(tilePos.y, 0f, Mathf.Max(0f, mapHeight - 1f))
            );
        }

        Vector2 ApplyTerrainWarp(Vector2 tilePos)
        {
            if (terrainWarpStrength <= 0.0001f)
                return tilePos;

            float warpFreq = elevFreqPeriodic * Mathf.Max(0.1f, terrainWarpFrequencyMultiplier);
            float warpScale = Mathf.Max(1f, mapWidth * 0.06f) * terrainWarpStrength;
            float warpX = noise.GetElevationPeriodic(tilePos + new Vector2(913.7f, 271.3f), mapWidth, mapHeight, warpFreq) - 0.5f;
            float warpY = noise.GetElevationPeriodic(tilePos + new Vector2(149.2f, 631.8f), mapWidth, mapHeight, warpFreq) - 0.5f;
            return WrapTerrainTilePos(tilePos + new Vector2(warpX, warpY) * (warpScale * 2f));
        }

        // ---------- PRE-PASS: Compute shaped noise for every tile, then normalize ----------
        // This guarantees the full 0-1 range is used regardless of FBm output limits,
        // so hillNoiseCutoff / mountainNoiseCutoff work as intended.
        float[] shapedNoisePerTile = ArrayPoolUtils.RentFloat(tileCount);
        float[] provinceNoisePerTile = ArrayPoolUtils.RentFloat(tileCount);
        float[] hillSignalPerTile = ArrayPoolUtils.RentFloat(tileCount);
        float[] mountainSignalPerTile = ArrayPoolUtils.RentFloat(tileCount);
        float noiseMin = float.MaxValue;
        float noiseMax = float.MinValue;

        for (int i = 0; i < tileCount; i++)
        {
            if (!isLandTile[i] && !isLakeTile[i]) continue; // ocean tiles stay 0
            Vector2Int coord = tileCoords[i];
            Vector2 tilePos = new Vector2(coord.x, coord.y);
            Vector2 warpedPos = ApplyTerrainWarp(tilePos);
            float rawNoise = noise.GetElevationPeriodic(warpedPos, mapWidth, mapHeight, elevFreqPeriodic);
            float provinceNoise = noise.GetElevationPeriodic(tilePos + new Vector2(611f, 197f), mapWidth, mapHeight, elevFreqPeriodic * Mathf.Max(0.05f, terrainProvinceFrequencyMultiplier));
            float hillSignal = noise.GetElevationPeriodic(warpedPos + new Vector2(1240f, 480f), mapWidth, mapHeight, elevFreqPeriodic * Mathf.Max(0.5f, hillNoiseFrequencyMultiplier));
            float mountainSignal = noise.GetElevationPeriodic(warpedPos + new Vector2(2080f, 1320f), mapWidth, mapHeight, elevFreqPeriodic * Mathf.Max(0.5f, mountainNoiseFrequencyMultiplier));
            float shaped = ShapeNoise(rawNoise);
            float provinceBias = (provinceNoise - 0.5f) * 2f * terrainProvinceStrength;
            float hillContribution = Mathf.Clamp01((hillSignal - 0.5f) / 0.5f) * hillNoiseStrength * motifRuggedness;
            float mountainContribution = Mathf.Clamp01((mountainSignal - 0.6f) / 0.4f) * mountainNoiseStrength * motifFoldedRanges;
                float geologyBias = 0f;
                if (enableAdvancedGeologyFramework && geologyProvinceMap != null && geologyMarginTypeMap != null && geologyStressMap != null && geologyAgeMap != null && geologyDrainageMap != null && geologySedimentMap != null)
                {
                    var province = (TectonicProvinceType)geologyProvinceMap[i];
                    var margin = (CoastalMarginType)geologyMarginTypeMap[i];
                    float stress = geologyStressMap[i];
                    float age = geologyAgeMap[i];
                    float drainage = geologyDrainageMap[i];
                    float sediment = geologySedimentMap[i];

                    switch (province)
                    {
                        case TectonicProvinceType.FoldBelt:
                            geologyBias += geologyFrameworkStrength * (0.08f + stress * 0.11f);
                            break;
                        case TectonicProvinceType.VolcanicArc:
                            geologyBias += geologyFrameworkStrength * (0.1f + stress * 0.12f);
                            break;
                        case TectonicProvinceType.RiftZone:
                            geologyBias -= geologyFrameworkStrength * (0.02f + drainage * 0.08f * drainageBasinStrength);
                            geologyBias += geologyFrameworkStrength * (1f - age) * 0.03f;
                            break;
                        case TectonicProvinceType.ForelandBasin:
                            geologyBias -= geologyFrameworkStrength * (0.04f + drainage * 0.1f * drainageBasinStrength + sediment * 0.05f * sedimentationStrength);
                            break;
                        case TectonicProvinceType.PassiveMargin:
                            geologyBias -= geologyFrameworkStrength * (0.015f + sediment * 0.04f * sedimentationStrength);
                            break;
                        case TectonicProvinceType.StableShield:
                            geologyBias += geologyFrameworkStrength * ((0.5f - age) * 0.04f * crustAgeStrength);
                            break;
                    }

                    if (margin == CoastalMarginType.Active)
                        geologyBias += geologyFrameworkStrength * 0.04f;
                    else if (margin == CoastalMarginType.Rifted)
                        geologyBias -= geologyFrameworkStrength * 0.03f;
                    else if (margin == CoastalMarginType.Passive || margin == CoastalMarginType.Deltaic)
                        geologyBias -= geologyFrameworkStrength * 0.025f;
                    else if (margin == CoastalMarginType.Glaciated)
                        geologyBias += geologyFrameworkStrength * 0.02f;
                }

                shaped = Mathf.Clamp01(shaped + provinceBias + hillContribution + mountainContribution + geologyBias);
            shapedNoisePerTile[i] = shaped;
            provinceNoisePerTile[i] = provinceNoise;
            hillSignalPerTile[i] = hillSignal;
            mountainSignalPerTile[i] = mountainSignal;
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

        if (ShouldLogDiagnostics())
            Debug.Log($"[PlanetGenerator] Noise pre-pass: raw shaped range [{noiseMin:F4}..{noiseMax:F4}], normalized to [0..1]");

        // --- Mountain Range Ridgeline Generation ---
        // Trace spline-based fault lines across continents and boost noise along them
        // to form coherent mountain chains with foothills instead of scattered peaks.
        {
            // BFS distance from coast for this pass (reused concept from continental bias, scoped here)
            int[] ridgeDist = new int[tileCount];
            for (int i = 0; i < tileCount; i++) ridgeDist[i] = -1;
            var ridgeBfsQueue = new Queue<int>();
            for (int i = 0; i < tileCount; i++)
            {
                if (!isLandTile[i] && !isLakeTile[i])
                {
                    ridgeDist[i] = 0;
                    ridgeBfsQueue.Enqueue(i);
                }
            }
            while (ridgeBfsQueue.Count > 0)
            {
                int cur = ridgeBfsQueue.Dequeue();
                int nd = ridgeDist[cur] + 1;
                foreach (int n in grid.neighbors[cur])
                {
                    if (n < 0 || n >= tileCount) continue;
                    if (ridgeDist[n] >= 0) continue;
                    ridgeDist[n] = nd;
                    ridgeBfsQueue.Enqueue(n);
                }
            }

            int ridgeMaxDist = 0;
            for (int i = 0; i < tileCount; i++)
                if (ridgeDist[i] > ridgeMaxDist) ridgeMaxDist = ridgeDist[i];

            // Only generate ridges if there's enough inland depth
            if (ridgeMaxDist >= 4)
            {
                var ridgeRand = new System.Random(unchecked((int)(seed ^ 0xD15EA5E)));
                int ridgeCount = Mathf.Max(1, continentDataList.Count); // ~1 range per continent

                for (int r = 0; r < ridgeCount; r++)
                {
                    // Pick a random deep-inland seed tile (inner 40-80% of max distance)
                    int minDist = Mathf.Max(3, (int)(ridgeMaxDist * 0.4f));
                    int maxDistForSeed = Mathf.Max(minDist + 1, (int)(ridgeMaxDist * 0.8f));
                    var deepCandidates = new List<int>();
                    for (int i = 0; i < tileCount; i++)
                    {
                        if (isLandTile[i] && !isLakeTile[i] && ridgeDist[i] >= minDist && ridgeDist[i] <= maxDistForSeed)
                            deepCandidates.Add(i);
                    }
                    if (deepCandidates.Count == 0) continue;

                    // Random walk to create a ridgeline path
                    int pathSeed = deepCandidates[ridgeRand.Next(deepCandidates.Count)];
                    int ridgeLength = Mathf.Clamp(ridgeRand.Next(8, 20), 8, tileCount / 100);
                    var ridgePath = new List<int> { pathSeed };
                    var ridgeVisited = new HashSet<int> { pathSeed };

                    // Pick an initial walk direction using noise for consistency
                    Vector2Int seedCoord = tileCoords[pathSeed];
                    float dirNoise = noise.GetElevationPeriodic(
                        new Vector2(seedCoord.x + 2000f, seedCoord.y + 2000f),
                        mapWidth, mapHeight, elevFreqPeriodic * 0.5f);
                    float walkAngle = dirNoise * Mathf.PI * 2f;
                    Vector2 walkDir = new Vector2(Mathf.Cos(walkAngle), Mathf.Sin(walkAngle));

                    int current = pathSeed;
                    for (int step = 0; step < ridgeLength; step++)
                    {
                        int bestNext = -1;
                        float bestScore = float.NegativeInfinity;
                        foreach (int n in grid.neighbors[current])
                        {
                            if (n < 0 || n >= tileCount) continue;
                            if (!isLandTile[n] || isLakeTile[n] || ridgeVisited.Contains(n)) continue;
                            if (ridgeDist[n] < 2) continue; // stay inland

                            Vector2Int nc = tileCoords[n];
                            Vector2 stepDir = new Vector2(nc.x - tileCoords[current].x, nc.y - tileCoords[current].y);
                            if (stepDir.sqrMagnitude > 0.001f) stepDir.Normalize();
                            float forward = Vector2.Dot(walkDir, stepDir); // prefer continuing direction
                            float inlandBias = ridgeDist[n] * 0.15f;
                            float noiseBias = noise.GetElevationPeriodic(
                                new Vector2(nc.x + 3000f, nc.y + 3000f),
                                mapWidth, mapHeight, elevFreqPeriodic * 2f) * 0.5f;
                            float score = forward * 2f + inlandBias + noiseBias;
                            if (score > bestScore) { bestScore = score; bestNext = n; }
                        }

                        if (bestNext < 0) break;
                        ridgePath.Add(bestNext);
                        ridgeVisited.Add(bestNext);
                        // Gently curve the walk direction (smooth turns)
                        Vector2Int bc = tileCoords[bestNext];
                        Vector2 newDir = new Vector2(bc.x - tileCoords[current].x, bc.y - tileCoords[current].y);
                        if (newDir.sqrMagnitude > 0.001f) newDir.Normalize();
                        walkDir = (walkDir * 0.7f + newDir * 0.3f).normalized;
                        current = bestNext;
                    }

                    if (ridgePath.Count < 4) continue;

                    // BFS outward from ridgeline to create foothills falloff
                    int foothillRadius = 3;
                    int[] ridgeProximity = new int[tileCount];
                    for (int i = 0; i < tileCount; i++) ridgeProximity[i] = int.MaxValue;

                    var foothillQueue = new Queue<int>();
                    foreach (int rt in ridgePath)
                    {
                        ridgeProximity[rt] = 0;
                        foothillQueue.Enqueue(rt);
                    }
                    while (foothillQueue.Count > 0)
                    {
                        int cur = foothillQueue.Dequeue();
                        int nd2 = ridgeProximity[cur] + 1;
                        if (nd2 > foothillRadius) continue;
                        foreach (int n in grid.neighbors[cur])
                        {
                            if (n < 0 || n >= tileCount) continue;
                            if (ridgeProximity[n] <= nd2) continue;
                            if (!isLandTile[n] || isLakeTile[n]) continue;
                            ridgeProximity[n] = nd2;
                            foothillQueue.Enqueue(n);
                        }
                    }

                    // Boost shapedNoisePerTile along the ridgeline and foothills
                    float ridgePeakBoost = 0.35f; // center of ridge gets this much noise boost
                    for (int i = 0; i < tileCount; i++)
                    {
                        if (ridgeProximity[i] >= int.MaxValue) continue;
                        float falloff = 1f - (float)ridgeProximity[i] / (foothillRadius + 1);
                        falloff = falloff * falloff; // quadratic falloff for natural profile
                        float boost = ridgePeakBoost * falloff;
                        shapedNoisePerTile[i] = Mathf.Clamp01(shapedNoisePerTile[i] + boost);
                    }
                }

                if (ShouldLogDiagnostics())
                    Debug.Log($"[PlanetGenerator] Mountain ridgelines: {ridgeCount} ranges traced, foothills radius=3");
            }
        }

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
            // Strengthened: use a minimum influence of 0.55 to guarantee visible climate bands
            float effectiveLatInfluence = Mathf.Max(latitudeInfluence, 0.55f);
            float latEffect = Mathf.Sign(latCurve) * Mathf.Pow(Mathf.Abs(latCurve), latitudeExponent) * effectiveLatInfluence;
            // Step 4: combine with base temperature
            float temperature = noiseTemp + latEffect + temperatureBias;
            temperature = Mathf.Clamp01(temperature);

            // Step 5: Latitude-based moisture modulation (Hadley cell pattern)
            // Equator (~lat 0.0): wet (ITCZ convergence zone)
            // Subtropics (~lat 0.3): dry (descending air → deserts at ~30°N/S)
            // Mid-latitudes (~lat 0.6): moderate moisture (frontal systems)
            // Poles (~lat 1.0): dry (cold air holds little moisture)
            {
                float subtropLat = 0.3f;   // deserts form here
                float midLat = 0.6f;       // temperate rain belt
                float latMoistureShift;
                if (lat < subtropLat)
                {
                    // Equator to subtropics: wet → dry
                    float t = lat / subtropLat;
                    latMoistureShift = Mathf.Lerp(0.12f, -0.15f, t);
                }
                else if (lat < midLat)
                {
                    // Subtropics to mid-latitudes: dry → moderate
                    float t = (lat - subtropLat) / (midLat - subtropLat);
                    latMoistureShift = Mathf.Lerp(-0.15f, 0.05f, t);
                }
                else
                {
                    // Mid-latitudes to poles: moderate → dry
                    float t = (lat - midLat) / (1f - midLat);
                    latMoistureShift = Mathf.Lerp(0.05f, -0.10f, t);
                }
                moisture = Mathf.Clamp01(moisture + latMoistureShift);
            }

            // Compute final elevation using discrete tier system
            float finalElevation;
            if (isLakeTile[i])
            {
                // Lakes: compute what the land elevation WOULD be, then subtract lakeDepth
                // so the lake bed sits below the surrounding terrain surface.
                // Floor: lake beds must stay above coast elevation to prevent visual sinking.
                float landElev = TierElevation(normalizedNoise);
                finalElevation = landElev - lakeDepth;
                finalElevation = Mathf.Max(coastElevation + 0.1f, finalElevation);
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

        // --- Continental Distance Elevation Bias ---
        // Tiles deeper inside a continent get a gentle elevation boost,
        // creating natural mountain interiors and flatter coastlines.
        int[] distFromEdge = new int[tileCount];
        {
            for (int i = 0; i < tileCount; i++) distFromEdge[i] = int.MaxValue;

            var edgeQueue = new Queue<int>();
            for (int i = 0; i < tileCount; i++)
            {
                if (!isLandTile[i] && !isLakeTile[i]) // ocean/coast seeds
                {
                    distFromEdge[i] = 0;
                    edgeQueue.Enqueue(i);
                }
            }

            while (edgeQueue.Count > 0)
            {
                int cur = edgeQueue.Dequeue();
                int nextDist = distFromEdge[cur] + 1;
                foreach (int n in grid.neighbors[cur])
                {
                    if (n < 0 || n >= tileCount) continue;
                    if (distFromEdge[n] <= nextDist) continue;
                    distFromEdge[n] = nextDist;
                    edgeQueue.Enqueue(n);
                }
            }

            // Find max inland distance for normalization
            int maxDist = 0;
            for (int i = 0; i < tileCount; i++)
                if (distFromEdge[i] < int.MaxValue && distFromEdge[i] > maxDist)
                    maxDist = distFromEdge[i];

            if (maxDist > 0)
            {
                float maxBoost = (flatElevationMax - flatElevationMin) * 0.24f; // keep interior uplift selective instead of swelling whole continents
                for (int i = 0; i < tileCount; i++)
                {
                    if (!isLandTile[i] || isLakeTile[i]) continue;
                    if (distFromEdge[i] <= 0 || distFromEdge[i] >= int.MaxValue) continue;

                    float t = (float)distFromEdge[i] / maxDist;
                    t = Mathf.Pow(t, 1.35f); // keep uplift concentrated deeper inland rather than inflating most interiors
                    sampledElev[i] += maxBoost * t;
                    sampledElev[i] = Mathf.Min(sampledElev[i], mountainElevationMax);
                }
            }

            if (ShouldLogDiagnostics())
                Debug.Log($"[PlanetGenerator] Continental elevation bias: maxDist={maxDist} tiles, boost up to {(flatElevationMax - flatElevationMin) * 0.5f:F2} units");
        }

        // --- Basin / Valley / Plateau / Escarpment Shaping ---
        {
            float[] terrainAdjust = ArrayPoolUtils.RentFloat(tileCount);
            try
            {
                float basinFreq = elevFreqPeriodic * Mathf.Max(0.5f, basinFrequencyMultiplier);
                float valleyFreq = elevFreqPeriodic * Mathf.Max(1f, valleyFrequencyMultiplier);
                float reliefRange = Mathf.Max(0.5f, mountainElevationMax - flatElevationMin);

                for (int i = 0; i < tileCount; i++)
                {
                    if (!isLandTile[i] || isLakeTile[i]) continue;

                    Vector2 tilePos = new Vector2(tileCoords[i].x, tileCoords[i].y);
                    Vector2 warpedPos = ApplyTerrainWarp(tilePos);
                    float province = provinceNoisePerTile[i];
                    float inland01 = distFromEdge[i] >= int.MaxValue ? 0f : Mathf.Clamp01(distFromEdge[i] / 10f);

                    float basinNoise = noise.GetElevationPeriodic(warpedPos + new Vector2(3510f, 920f), mapWidth, mapHeight, basinFreq);
                    float valleyNoise = noise.GetElevationPeriodic(warpedPos + new Vector2(2870f, 1770f), mapWidth, mapHeight, valleyFreq);
                    float plateauNoise = noise.GetElevationPeriodic(warpedPos + new Vector2(4190f, 260f), mapWidth, mapHeight, basinFreq * 1.4f);
                    float escarpNoise = noise.GetElevationPeriodic(warpedPos + new Vector2(5200f, 810f), mapWidth, mapHeight, basinFreq * 1.8f);

                    float basinMask = Mathf.Clamp01((0.42f - basinNoise) / 0.42f) * inland01 * motifBasins;
                    float valleyMask = Mathf.Clamp01(1f - Mathf.Abs(valleyNoise - 0.5f) * 4f) * inland01;
                    float plateauMask = Mathf.Clamp01((plateauNoise - 0.58f) / 0.42f) * Mathf.Clamp01((province - 0.45f) / 0.55f) * motifPlateaus;
                    float escarpMask = Mathf.Clamp01((escarpNoise - 0.55f) / 0.45f) * Mathf.Clamp01((province - 0.4f) / 0.6f) * motifEscarpments;

                    float delta = 0f;
                    delta -= basinMask * basinCarvingStrength * reliefRange;
                    delta -= valleyMask * valleyCarvingStrength * reliefRange;

                    if (mesaStrength > 0.001f && plateauMask > 0.001f)
                    {
                        float targetMesa = Mathf.Lerp(hillElevationMin, hillElevationMax, 0.75f);
                        sampledElev[i] = Mathf.Lerp(sampledElev[i], targetMesa, plateauMask * mesaStrength);
                    }

                    if (escarpmentStrength > 0.001f && escarpMask > 0.001f)
                    {
                        float normalizedStep = Mathf.InverseLerp(flatElevationMin, mountainElevationMax, sampledElev[i]);
                        float stepped = Mathf.Round(normalizedStep * 5f) / 5f;
                        float targetEscarp = Mathf.Lerp(flatElevationMin, mountainElevationMax, stepped);
                        sampledElev[i] = Mathf.Lerp(sampledElev[i], targetEscarp, escarpMask * escarpmentStrength);
                        delta += escarpMask * escarpmentStrength * reliefRange * 0.08f;
                    }

                    terrainAdjust[i] = delta;
                }

                for (int i = 0; i < tileCount; i++)
                {
                    if (!isLandTile[i] || isLakeTile[i]) continue;
                    sampledElev[i] = Mathf.Clamp(sampledElev[i] + terrainAdjust[i], flatElevationMin - 0.25f, mountainElevationMax);
                }
            }
            finally
            {
                ArrayPoolUtils.ReturnFloat(terrainAdjust);
            }
        }

        // --- Rain Shadow ---
        // Mountains block moisture from the prevailing wind direction.
        // Windward slopes get a moisture boost; leeward slopes get dried out.
        {
            // Prevailing wind: seeded noise picks a per-map direction (mostly west-to-east with variation)
            var windRand = new System.Random(unchecked((int)(seed ^ 0xC1052)));
            float windAngle = (float)(windRand.NextDouble() * 0.6 - 0.3); // -0.3..+0.3 radians off east
            Vector2 windDir = new Vector2(Mathf.Cos(windAngle), Mathf.Sin(windAngle)); // mostly +X (west to east)

            float shadowStrength = 0.25f; // max moisture reduction behind mountains
            float boostStrength = 0.10f;  // max moisture boost on windward side
            int shadowRange = 4;          // how many tiles the shadow extends downwind

            // Identify mountain/high-hill tiles as blockers
            bool[] isBlocker = new bool[tileCount];
            for (int i = 0; i < tileCount; i++)
            {
                if (!isLandTile[i] || isLakeTile[i]) continue;
                if (sampledElev[i] >= hillElevationMin + (hillElevationMax - hillElevationMin) * 0.5f)
                    isBlocker[i] = true;
            }

            // For each blocker, cast a shadow downwind
            float[] moistureOffset = new float[tileCount];
            for (int i = 0; i < tileCount; i++)
            {
                if (!isBlocker[i]) continue;

                // Boost windward neighbors (upwind side)
                foreach (int n in grid.neighbors[i])
                {
                    if (n < 0 || n >= tileCount) continue;
                    if (!isLandTile[n] || isLakeTile[n]) continue;
                    Vector2 toNeighbor = new Vector2(tileCoords[n].x - tileCoords[i].x, tileCoords[n].y - tileCoords[i].y);
                    if (toNeighbor.sqrMagnitude > 0.001f) toNeighbor.Normalize();
                    float dot = Vector2.Dot(toNeighbor, windDir);
                    if (dot < -0.3f) // upwind neighbor
                        moistureOffset[n] = Mathf.Max(moistureOffset[n], boostStrength * Mathf.Abs(dot));
                }

                // BFS downwind to cast shadow
                var shadowQueue = new Queue<(int idx, int depth)>();
                var shadowVisited = new HashSet<int> { i };
                foreach (int n in grid.neighbors[i])
                {
                    if (n < 0 || n >= tileCount) continue;
                    if (!isLandTile[n] && !isLakeTile[n]) continue;
                    Vector2 toN = new Vector2(tileCoords[n].x - tileCoords[i].x, tileCoords[n].y - tileCoords[i].y);
                    if (toN.sqrMagnitude > 0.001f) toN.Normalize();
                    if (Vector2.Dot(toN, windDir) > 0.3f) // downwind
                    {
                        shadowQueue.Enqueue((n, 1));
                        shadowVisited.Add(n);
                    }
                }

                while (shadowQueue.Count > 0)
                {
                    var (cur, depth) = shadowQueue.Dequeue();
                    float falloff = 1f - (float)depth / (shadowRange + 1);
                    float reduction = -shadowStrength * falloff;
                    if (reduction < moistureOffset[cur])
                        moistureOffset[cur] = reduction;

                    if (depth < shadowRange)
                    {
                        foreach (int n in grid.neighbors[cur])
                        {
                            if (n < 0 || n >= tileCount) continue;
                            if (shadowVisited.Contains(n)) continue;
                            if (!isLandTile[n] && !isLakeTile[n]) continue;
                            if (isBlocker[n]) continue; // another mountain blocks the shadow
                            Vector2 toN = new Vector2(tileCoords[n].x - tileCoords[cur].x, tileCoords[n].y - tileCoords[cur].y);
                            if (toN.sqrMagnitude > 0.001f) toN.Normalize();
                            if (Vector2.Dot(toN, windDir) > 0.1f)
                            {
                                shadowQueue.Enqueue((n, depth + 1));
                                shadowVisited.Add(n);
                            }
                        }
                    }
                }
            }

            // Apply moisture offsets
            for (int i = 0; i < tileCount; i++)
            {
                if (Mathf.Abs(moistureOffset[i]) > 0.001f)
                    sampledMoist[i] = Mathf.Clamp01(sampledMoist[i] + moistureOffset[i]);
            }

            if (ShouldLogDiagnostics())
                Debug.Log($"[PlanetGenerator] Rain shadow: wind angle={windAngle:F2}rad, shadow range={shadowRange}, strength={shadowStrength}");
        }

        // --- Elevation Smoothing Pass ---
        // Average each land tile's elevation with its neighbors to eliminate harsh
        // tier boundary cliffs and produce natural-looking gradients.
        {
            int elevSmoothPasses = 3;
            float elevSmoothStrength = 0.35f;
            for (int pass = 0; pass < elevSmoothPasses; pass++)
            {
                float[] smoothed = new float[tileCount];
                for (int i = 0; i < tileCount; i++)
                {
                    if (!isLandTile[i] && !isLakeTile[i])
                    {
                        smoothed[i] = sampledElev[i];
                        continue;
                    }

                    float sum = 0f;
                    int cnt = 0;
                    foreach (int n in grid.neighbors[i])
                    {
                        if (n < 0 || n >= tileCount) continue;
                        if (!isLandTile[n] && !isLakeTile[n]) continue; // don't blend with ocean
                        sum += sampledElev[n];
                        cnt++;
                    }

                    if (cnt > 0)
                    {
                        float avg = sum / cnt;
                        smoothed[i] = Mathf.Lerp(sampledElev[i], avg, elevSmoothStrength);
                    }
                    else
                    {
                        smoothed[i] = sampledElev[i];
                    }
                }
                sampledElev = smoothed;

                if (loadingPanelController != null)
                {
                    loadingPanelController.SetProgress(0.38f + (float)pass / elevSmoothPasses * 0.02f);
                    loadingPanelController.SetStatus($"Smoothing elevation (pass {pass + 1}/{elevSmoothPasses})...");
                }
                yield return null;
            }

            if (ShouldLogDiagnostics())
                Debug.Log($"[PlanetGenerator] Elevation smoothing: {elevSmoothPasses} passes at {elevSmoothStrength} strength");
        }

        // --- Erosion-Style Redistribution ---
        if (erosionStrength > 0.001f)
        {
            float[] eroded = ArrayPoolUtils.RentFloat(tileCount);
            try
            {
                for (int i = 0; i < tileCount; i++)
                {
                    if (!isLandTile[i] || isLakeTile[i])
                    {
                        eroded[i] = sampledElev[i];
                        continue;
                    }

                    float center = sampledElev[i];
                    float lowerSum = 0f;
                    float higherSum = 0f;
                    int lowerCount = 0;
                    int higherCount = 0;
                    foreach (int n in grid.neighbors[i])
                    {
                        if (n < 0 || n >= tileCount) continue;
                        if (!isLandTile[n] || isLakeTile[n]) continue;
                        float neighbor = sampledElev[n];
                        if (neighbor < center)
                        {
                            lowerSum += neighbor;
                            lowerCount++;
                        }
                        else
                        {
                            higherSum += neighbor;
                            higherCount++;
                        }
                    }

                    float downhillAvg = lowerCount > 0 ? lowerSum / lowerCount : center;
                    float uphillAvg = higherCount > 0 ? higherSum / higherCount : center;
                    float spikeFactor = Mathf.Clamp01((center - downhillAvg) / Mathf.Max(0.5f, mountainElevationMax - flatElevationMin));
                    float depositTarget = Mathf.Lerp(center, (downhillAvg + uphillAvg) * 0.5f, 0.7f);
                    eroded[i] = Mathf.Lerp(center, depositTarget, erosionStrength * spikeFactor * motifRuggedness);
                }

                sampledElev = eroded;
                eroded = null;
            }
            finally
            {
                if (eroded != null) ArrayPoolUtils.ReturnFloat(eroded);
            }
        }

        ApplyAdvancedGeologyClimateAdjustments(sampledTemp, sampledMoist, sampledElev, isLandTile, isLakeTile, tileCount);

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

                float hillSignal = hillSignalPerTile[i];
                float mountainSignal = mountainSignalPerTile[i];
                float province = provinceNoisePerTile[i];
                float hillThreshold = Mathf.Clamp01(hillNoiseCutoff + 0.14f - (province - 0.5f) * 0.08f);
                float mountainThreshold = Mathf.Clamp01(mountainNoiseCutoff + 0.05f - (province - 0.5f) * 0.08f);
                float hillSignalFloor = Mathf.Lerp(flatElevationMax, hillElevationMin, 0.55f);
                bool separateMountainSignal = mountainSignal > mountainThreshold && finalElevation >= hillElevationMin + (hillElevationMax - hillElevationMin) * 0.8f;
                bool separateHillSignal = hillSignal > hillThreshold && finalElevation >= hillSignalFloor;

                if (finalElevation >= mountainElevationMin || separateMountainSignal)
                {
                    if (biome != Biome.Glacier && biome != Biome.Arctic)
                    {
                        isMountain = true;
                        if (finalElevation < mountainElevationMin)
                            finalElevation = Mathf.Lerp(finalElevation, mountainElevationMin, 0.55f);
                    }
                }
                else if (finalElevation >= hillElevationMin || separateHillSignal)
                {
                    bool biomeIsWater = (biome == Biome.Coast || biome == Biome.Seas || biome == Biome.Ocean || biome == Biome.Lake || biome == Biome.River);
                    if (!biomeIsWater)
                    {
                        isHill = true;
                        if (finalElevation < hillElevationMin)
                            finalElevation = Mathf.Lerp(finalElevation, hillElevationMin, 0.5f);
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
            if (isMountain || finalElevation >= mountainElevationMin) elevTier = ElevationTier.Mountain;
            else if (isHill || finalElevation >= hillElevationMin) elevTier = ElevationTier.Hill;

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

        if (ShouldLogDiagnostics())
            Debug.Log($"[PlanetGenerator] Land elevation range (initial, pre-coast/river): {landElevMin:F4} to {landElevMax:F4}");

        // ---------- 5.5. Compute Render Elevation — MOVED to section 6.6 ----------
        // Render elevation normalization now runs AFTER coast/seas/river post-processing
        // (section 6.6) so that converted coast tiles get correct render elevation
        // instead of retaining their former land values.

        // ---------- 6. Post-processing (Coasts, Seas, Visuals) --------------
        // Create coast tiles from a frozen land/water snapshot so conversion cannot
        // recursively spread through the continent during the same pass.
        HashSet<int> postProcessProtectedTiles = new HashSet<int>();
        bool[] isOceanWaterSnapshot = new bool[tileCount];
        bool[] shouldBecomeCoast = new bool[tileCount];

        for (int i = 0; i < tileCount; i++) {
            if (!data.ContainsKey(i)) continue;

            var td = data[i];

            // Protect Arctic and Glacier tiles from ever becoming a coast or sea
            if (td.biome == Biome.Arctic || td.biome == Biome.Glacier) {
                postProcessProtectedTiles.Add(i);
                continue;
            }

            // Snapshot only true pre-coast ocean water. Lakes are excluded.
            if (!td.isLand && !td.isLake) {
                isOceanWaterSnapshot[i] = true;
            }

            // BATCH YIELD
            if (i > 0 && i % 1000 == 0)
            {
                if (loadingPanelController != null)
                {
                    loadingPanelController.SetProgress(0.5f + (float)i / tileCount * 0.04f);
                    loadingPanelController.SetStatus("Scanning shoreline...");
                }
                yield return null;
            }
        }

        for (int i = 0; i < tileCount; i++) {
            if (!data.ContainsKey(i)) continue;
            if (postProcessProtectedTiles.Contains(i)) continue;

            var td = data[i];
            if (!td.isLand || td.isLake || td.isRiver) continue;

            bool hasOceanNeighbor = false;
            foreach (int nIdx in grid.neighbors[i]) {
                if (nIdx < 0 || nIdx >= tileCount) continue;
                if (isOceanWaterSnapshot[nIdx]) {
                    hasOceanNeighbor = true;
                    break;
                }
            }

            shouldBecomeCoast[i] = hasOceanNeighbor;

            // BATCH YIELD
            if (i > 0 && i % 1000 == 0)
            {
                if (loadingPanelController != null)
                {
                    loadingPanelController.SetProgress(0.54f + (float)i / tileCount * 0.03f);
                    loadingPanelController.SetStatus("Marking coastline...");
                }
                yield return null;
            }
        }

        for (int i = 0; i < tileCount; i++) {
            if (!shouldBecomeCoast[i]) continue;

            var td = data[i];
            td.biome = Biome.Coast;
            td.isLand = false;
            td.isHill = false;
            td.isMountain = false;
            td.elevationTier = ElevationTier.Flat;
            data[i] = td;
            baseData[i] = td;

            if (i > 0 && i % 1000 == 0)
            {
                if (loadingPanelController != null)
                {
                    loadingPanelController.SetProgress(0.57f + (float)i / tileCount * 0.03f);
                    loadingPanelController.SetStatus("Applying coastline...");
                }
                yield return null;
            }
        }

        // Identify all coast tiles after the first pass
        HashSet<int> coastTiles = new HashSet<int>();

        // --- Fjord & Peninsula Carving ---
        // Cut narrow water channels into coastlines (fjords) and extend thin
        // land fingers outward (peninsulas) for more interesting shorelines.
        {
            var fjordRand = new System.Random(unchecked((int)(seed ^ 0xF10AD)));
            int fjordCount = Mathf.Max(1, tileCount / 4000); // ~1 per 4000 tiles
            int peninsulaCount = Mathf.Max(1, tileCount / 5000);

            // Collect coast tiles for seeding
            var coastSeedList = new List<int>();
            for (int i = 0; i < tileCount; i++)
            {
                if (data.ContainsKey(i) && data[i].biome == Biome.Coast)
                    coastSeedList.Add(i);
            }

            if (coastSeedList.Count > 0)
            {
                // --- Fjords: narrow water channels cutting into land ---
                for (int f = 0; f < fjordCount && coastSeedList.Count > 0; f++)
                {
                    int startIdx = coastSeedList[fjordRand.Next(coastSeedList.Count)];
                    // Walk inland: prefer tiles that continue roughly straight + noise variation
                    int fjordLength = fjordRand.Next(3, 6);
                    int current = startIdx;
                    var fjordVisited = new HashSet<int> { current };
                    var fjordTiles = new List<int>();

                    // Initial direction: from ocean toward land
                    Vector2 inlandDir = Vector2.zero;
                    foreach (int n in grid.neighbors[startIdx])
                    {
                        if (n < 0 || n >= tileCount) continue;
                        if (data.ContainsKey(n) && data[n].isLand && !data[n].isLake && !data[n].isRiver)
                        {
                            inlandDir += new Vector2(tileCoords[n].x - tileCoords[startIdx].x, tileCoords[n].y - tileCoords[startIdx].y);
                        }
                    }
                    if (inlandDir.sqrMagnitude < 0.001f) continue;
                    inlandDir.Normalize();

                    for (int step = 0; step < fjordLength; step++)
                    {
                        int bestNext = -1;
                        float bestScore = float.NegativeInfinity;
                        foreach (int n in grid.neighbors[current])
                        {
                            if (n < 0 || n >= tileCount) continue;
                            if (fjordVisited.Contains(n)) continue;
                            if (!data.ContainsKey(n)) continue;
                            var ntd = data[n];
                            if (!ntd.isLand || ntd.isLake || ntd.isRiver || ntd.isMountain) continue;

                            Vector2 toN = new Vector2(tileCoords[n].x - tileCoords[current].x, tileCoords[n].y - tileCoords[current].y);
                            if (toN.sqrMagnitude > 0.001f) toN.Normalize();
                            float forward = Vector2.Dot(inlandDir, toN);
                            float noiseBias = noise.GetElevationPeriodic(
                                new Vector2(tileCoords[n].x + 4000f, tileCoords[n].y + 4000f),
                                mapWidth, mapHeight, elevFreqPeriodic * 6f) - 0.5f;
                            float score = forward * 1.5f + noiseBias;
                            if (score > bestScore) { bestScore = score; bestNext = n; }
                        }
                        if (bestNext < 0) break;
                        fjordTiles.Add(bestNext);
                        fjordVisited.Add(bestNext);
                        // Gently curve
                        Vector2 stepDir = new Vector2(tileCoords[bestNext].x - tileCoords[current].x, tileCoords[bestNext].y - tileCoords[current].y);
                        if (stepDir.sqrMagnitude > 0.001f) stepDir.Normalize();
                        inlandDir = (inlandDir * 0.65f + stepDir * 0.35f).normalized;
                        current = bestNext;
                    }

                    // Convert fjord tiles to coast (water channel)
                    foreach (int ft in fjordTiles)
                    {
                        if (!data.ContainsKey(ft)) continue;
                        var td = data[ft];
                        td.biome = Biome.Coast;
                        td.isLand = false;
                        td.isHill = false;
                        td.isMountain = false;
                        td.elevationTier = ElevationTier.Flat;
                        td.elevation = coastElevation;
                        data[ft] = td;
                        baseData[ft] = td;
                        isLandTile[ft] = false;
                    }
                }

                // --- Peninsulas: thin land fingers extending into water ---
                // Collect ocean tiles adjacent to coast for peninsula roots
                var oceanNearCoast = new List<int>();
                for (int i = 0; i < tileCount; i++)
                {
                    if (!data.ContainsKey(i)) continue;
                    if (data[i].biome != Biome.Ocean && data[i].biome != Biome.Seas) continue;
                    foreach (int n in grid.neighbors[i])
                    {
                        if (n >= 0 && n < tileCount && data.ContainsKey(n) && data[n].biome == Biome.Coast)
                        {
                            oceanNearCoast.Add(i);
                            break;
                        }
                    }
                }

                for (int p = 0; p < peninsulaCount && oceanNearCoast.Count > 0; p++)
                {
                    int startIdx = oceanNearCoast[fjordRand.Next(oceanNearCoast.Count)];
                    int penLength = fjordRand.Next(2, 5);
                    int current = startIdx;
                    var penVisited = new HashSet<int> { current };
                    var penTiles = new List<int> { current };

                    // Direction: away from coast, into ocean
                    Vector2 outDir = Vector2.zero;
                    foreach (int n in grid.neighbors[startIdx])
                    {
                        if (n < 0 || n >= tileCount) continue;
                        if (data.ContainsKey(n) && (data[n].biome == Biome.Ocean || data[n].biome == Biome.Seas))
                        {
                            outDir += new Vector2(tileCoords[n].x - tileCoords[startIdx].x, tileCoords[n].y - tileCoords[startIdx].y);
                        }
                    }
                    if (outDir.sqrMagnitude < 0.001f) continue;
                    outDir.Normalize();

                    for (int step = 0; step < penLength; step++)
                    {
                        int bestNext = -1;
                        float bestScore = float.NegativeInfinity;
                        foreach (int n in grid.neighbors[current])
                        {
                            if (n < 0 || n >= tileCount) continue;
                            if (penVisited.Contains(n)) continue;
                            if (!data.ContainsKey(n)) continue;
                            if (data[n].biome != Biome.Ocean && data[n].biome != Biome.Seas) continue;

                            Vector2 toN = new Vector2(tileCoords[n].x - tileCoords[current].x, tileCoords[n].y - tileCoords[current].y);
                            if (toN.sqrMagnitude > 0.001f) toN.Normalize();
                            float forward = Vector2.Dot(outDir, toN);
                            float score = forward * 1.5f + (float)(fjordRand.NextDouble() * 0.4);
                            if (score > bestScore) { bestScore = score; bestNext = n; }
                        }
                        if (bestNext < 0) break;
                        penTiles.Add(bestNext);
                        penVisited.Add(bestNext);
                        Vector2 stepDir = new Vector2(tileCoords[bestNext].x - tileCoords[current].x, tileCoords[bestNext].y - tileCoords[current].y);
                        if (stepDir.sqrMagnitude > 0.001f) stepDir.Normalize();
                        outDir = (outDir * 0.6f + stepDir * 0.4f).normalized;
                        current = bestNext;
                    }

                    // Convert peninsula tiles to coast (land in water)
                    foreach (int pt in penTiles)
                    {
                        if (!data.ContainsKey(pt)) continue;
                        var td = data[pt];
                        td.biome = Biome.Coast;
                        td.isLand = false; // coast tiles are not "land" in this system
                        td.isHill = false;
                        td.isMountain = false;
                        td.elevationTier = ElevationTier.Flat;
                        td.elevation = coastElevation;
                        data[pt] = td;
                        baseData[pt] = td;
                    }
                }

                if (ShouldLogDiagnostics())
                    Debug.Log($"[PlanetGenerator] Fjord/peninsula carving: {fjordCount} fjords, {peninsulaCount} peninsulas attempted");
            }
        }

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
        int seasRings = 3;
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

        // (6.2 Forced coastal flattening removed — continental distance bias and
        //  elevation smoothing now create natural coastal-to-inland gradients.)

        // Snapshot the finalized land elevation BEFORE freshwater metadata/river surfaces are built.
        // Heightmap rendering uses this on land adjacent to rivers/lakes so banks stay aligned with
        // the intended pre-freshwater terrain instead of defaulting to an uninitialized value.
        for (int i = 0; i < tileCount; i++)
        {
            if (!data.TryGetValue(i, out var td)) continue;
            if (!td.isLand || td.isLake || td.isRiver) continue;
            if (td.biome == Biome.Coast || td.biome == Biome.Ocean || td.biome == Biome.Seas) continue;

            td.originalElevation = td.elevation;
            data[i] = td;
            baseData[i] = td;
        }

        void ApplyFloodplainBehavior()
        {
            if (!enableFloodplains || floodplainRange <= 0)
                return;

            bool isSpecialHeatWorld = mapType == MapType.Infernal || mapType == MapType.Demonic;
            int range = Mathf.Max(1, floodplainRange);
            float[] floodplainInfluence = new float[tileCount];
            var queue = new Queue<(int idx, int depth)>();

            for (int i = 0; i < tileCount; i++)
            {
                if (!data.TryGetValue(i, out var td) || !td.isRiver)
                    continue;

                queue.Enqueue((i, 0));
                floodplainInfluence[i] = 1f;
            }

            while (queue.Count > 0)
            {
                var (current, depth) = queue.Dequeue();
                if (depth >= range)
                    continue;

                foreach (int neighbor in grid.neighbors[current])
                {
                    if (neighbor < 0 || neighbor >= tileCount)
                        continue;
                    if (!data.TryGetValue(neighbor, out var ntd))
                        continue;
                    if (!ntd.isLand || ntd.isLake || ntd.isRiver)
                        continue;
                    if (ntd.biome == Biome.Coast || ntd.biome == Biome.Ocean || ntd.biome == Biome.Seas)
                        continue;

                    float influence = 1f - (float)(depth + 1) / (range + 1f);
                    if (influence <= floodplainInfluence[neighbor])
                        continue;

                    floodplainInfluence[neighbor] = influence;
                    queue.Enqueue((neighbor, depth + 1));
                }
            }

            float floodplainCeiling = Mathf.Lerp(flatElevationMax, hillElevationMin, 0.18f);
            float hillSoftCeiling = hillElevationMin + Mathf.Max(0.35f, (hillElevationMax - hillElevationMin) * 0.12f);
            int floodplainTiles = 0;

            for (int i = 0; i < tileCount; i++)
            {
                float influence = floodplainInfluence[i];
                if (influence <= 0f)
                    continue;
                if (!data.TryGetValue(i, out var td))
                    continue;
                if (!td.isLand || td.isLake || td.isRiver || td.biome == Biome.Coast || td.biome == Biome.Ocean || td.biome == Biome.Seas)
                    continue;
                if (td.isMountain || td.elevation >= mountainElevationMin)
                    continue;

                bool isEligibleLowland = td.elevation <= hillSoftCeiling;
                bool canSoftenHill = td.isHill && td.elevation <= hillSoftCeiling;
                if (!isEligibleLowland && !canSoftenHill)
                    continue;

                float newElevation = td.elevation;
                if (newElevation > floodplainCeiling)
                {
                    float flattenT = influence * floodplainFlattenStrength;
                    newElevation = Mathf.Lerp(newElevation, floodplainCeiling, flattenT);
                }

                td.elevation = Mathf.Max(flatElevationMin, newElevation);
                td.originalElevation = td.elevation;
                td.moisture = Mathf.Clamp01(td.moisture + floodplainMoistureBoost * influence);

                if (td.elevation < hillElevationMin)
                {
                    td.isHill = false;
                    td.isMountain = false;
                    td.elevationTier = ElevationTier.Flat;
                }
                else if (td.elevation < mountainElevationMin)
                {
                    td.isMountain = false;
                    td.isHill = true;
                    td.elevationTier = ElevationTier.Hill;
                }

                if (!isSpecialHeatWorld && td.biome != Biome.Arctic && td.biome != Biome.Glacier && td.biome != Biome.Tundra)
                {
                    Biome newBiome = td.biome;
                    float fertility = td.moisture + influence * floodplainFertilityBias * 0.2f;

                    if (td.temperature > 0.78f)
                    {
                        if (fertility > 0.46f) newBiome = Biome.Plains;
                        else if (fertility > 0.2f) newBiome = Biome.Savannah;
                    }
                    else if (td.temperature > 0.38f)
                    {
                        if (fertility > 0.72f) newBiome = Biome.Swamp;
                        else if (fertility > 0.42f) newBiome = Biome.Temperate;
                        else newBiome = Biome.Plains;
                    }
                    else if (td.temperature > 0.22f && fertility > 0.35f)
                    {
                        newBiome = Biome.Plains;
                    }

                    if (newBiome != td.biome)
                    {
                        td.biome = newBiome;
                        var y = BiomeHelper.Yields(newBiome);
                        td.food = y.food;
                        td.production = y.prod;
                        td.gold = y.gold;
                        td.science = y.sci;
                        td.culture = y.cult;
                        td.movementCost = BiomeHelper.GetMovementCost(newBiome);
                    }
                }

                data[i] = td;
                baseData[i] = td;
                floodplainTiles++;
            }

            if (ShouldLogDiagnostics())
                Debug.Log($"[PlanetGenerator] Floodplains applied to {floodplainTiles} river-adjacent tiles (range={range}, moistureBoost={floodplainMoistureBoost:F2}).");
        }

        // ---------- 6.5 River Generation Pass (after coasts are defined) ----
        if (enableRivers && allowOceansThisRun && GameSetupData.riverCount > 0)
            yield return StartCoroutine(GenerateRivers(isLandTile, data, lakeCenters));

        ApplyFloodplainBehavior();

        // ---------- 6.55 Compute Water Metadata for chunk-based water mesh system ----------
        ComputeWaterMetadata(data, grid, tileCount);

        // ---------- 6.6. Elevation is already in world-space units ----------
        // No normalization needed. The elevation field on each tile IS the world-space
        // height offset from the flat plane. The heightmap texture stores these values
        // directly (RHalf supports the full float range including negatives).
        if (ShouldLogDiagnostics())
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
        if (ShouldLogDiagnostics())
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
            try { ArrayPoolUtils.ReturnFloat(provinceNoisePerTile); } catch { }
            try { ArrayPoolUtils.ReturnFloat(hillSignalPerTile); } catch { }
            try { ArrayPoolUtils.ReturnFloat(mountainSignalPerTile); } catch { }
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

                Vector2Int sourceCoord = tileCoords[startIdx];
                Vector2Int goalCoord = tileCoords[goalIdx];
                Vector2 sourceToGoal = new Vector2(WrappedDelta(goalCoord.x, sourceCoord.x, tilesX), goalCoord.y - sourceCoord.y);
                float pathLength = Mathf.Max(1f, sourceToGoal.magnitude);
                Vector2 pathDir = sourceToGoal / pathLength;
                Vector2 pathNormal = new Vector2(-pathDir.y, pathDir.x);
                var riverShapeRand = new System.Random(unchecked(seed ^ (startIdx * 73856093) ^ (goalIdx * 19349663)));
                float meanderPhase = (float)(riverShapeRand.NextDouble() * Mathf.PI * 2.0);
                float meanderAmplitudeTiles = Mathf.Clamp(pathLength * riverMeanderAmplitude, 1f, 6.5f);
                float meanderCycles = Mathf.Max(0.4f, Mathf.Lerp(0.8f, 2.4f, Mathf.Clamp01(pathLength / 60f)) * riverMeanderFrequency);
                float corridorStrength = 0.18f + riverMeanderAmplitude * 1.8f;

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
                        Vector2Int nCoord = tileCoords[n];
                        Vector2 sourceToCandidate = new Vector2(WrappedDelta(nCoord.x, sourceCoord.x, tilesX), nCoord.y - sourceCoord.y);
                        float along01 = Mathf.Clamp01(Vector2.Dot(sourceToCandidate, pathDir) / pathLength);
                        float lateral = Vector2.Dot(sourceToCandidate, pathNormal);
                        float meanderNoise = noise.GetElevationPeriodic(
                            new Vector2(nCoord.x + 1000f, nCoord.y + 1000f),
                            mapWidth, mapHeight, elevFreqPeriodic * 1.85f);
                        float desiredLateral = Mathf.Sin(along01 * Mathf.PI * 2f * meanderCycles + meanderPhase) * meanderAmplitudeTiles;
                        desiredLateral += (meanderNoise - 0.5f) * meanderAmplitudeTiles * 0.7f;
                        tentativeG += Mathf.Abs(lateral - desiredLateral) * corridorStrength;

                        if (cameFrom.TryGetValue(current, out int previous))
                        {
                            Vector2Int prevCoord = tileCoords[previous];
                            Vector2 incoming = new Vector2(WrappedDelta(tileCoords[current].x, prevCoord.x, tilesX), tileCoords[current].y - prevCoord.y);
                            Vector2 outgoing = new Vector2(WrappedDelta(nCoord.x, tileCoords[current].x, tilesX), nCoord.y - tileCoords[current].y);
                            if (incoming.sqrMagnitude > 0.001f && outgoing.sqrMagnitude > 0.001f)
                            {
                                incoming.Normalize();
                                outgoing.Normalize();
                                float turnPenalty = 1f - Mathf.Clamp01((Vector2.Dot(incoming, outgoing) + 1f) * 0.5f);
                                tentativeG += turnPenalty * riverTurnResistance;
                            }
                        }

                        tentativeG += (float)(rand.NextDouble() * 0.05);

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
                var stampedPath = new List<int>(); // ordered list of tiles actually stamped as river
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
                    // Don't carve yet — erosion pass will set final elevations
                    tileData[tileIdx] = td;
                    baseData[tileIdx] = td;
                    riverTiles.Add(tileIdx);
                    isLandTile[tileIdx] = true;
                    isRiverTile[tileIdx] = true;
                    stampedPath.Add(tileIdx);
                }

                // --- Oxbow Lake Detection ---
                // Scan the river path for sharp bends (tight U-turns). Where the river
                // curves sharply, convert a land tile on the inside of the bend into a
                // small lake — simulating meander cutoff / oxbow lake formation.
                if (stampedPath.Count >= 6)
                {
                    int oxbowsCreated = 0;
                    int maxOxbows = Mathf.Max(1, stampedPath.Count / 12); // ~1 per 12 tiles
                    for (int si = 2; si < stampedPath.Count - 2 && oxbowsCreated < maxOxbows; si++)
                    {
                        // Measure bend angle at tile si using tiles si-2 and si+2
                        Vector2Int prev2 = tileCoords[stampedPath[si - 2]];
                        Vector2Int curr = tileCoords[stampedPath[si]];
                        Vector2Int next2 = tileCoords[stampedPath[si + 2]];
                        Vector2 dirIn = new Vector2(curr.x - prev2.x, curr.y - prev2.y);
                        Vector2 dirOut = new Vector2(next2.x - curr.x, next2.y - curr.y);
                        if (dirIn.sqrMagnitude < 0.001f || dirOut.sqrMagnitude < 0.001f) continue;
                        dirIn.Normalize();
                        dirOut.Normalize();
                        float dot = Vector2.Dot(dirIn, dirOut);
                        // Sharp bend: dot < 0.1 means >~84 degree turn
                        if (dot > 0.1f) continue;

                        // Find the inside of the bend: perpendicular to the average direction, on the turning side
                        Vector2 avgDir = (dirIn + dirOut);
                        if (avgDir.sqrMagnitude < 0.001f) avgDir = dirIn;
                        avgDir.Normalize();
                        // Cross product sign tells us turning direction (2D: perp = (-y, x) or (y, -x))
                        float cross = dirIn.x * dirOut.y - dirIn.y * dirOut.x;
                        Vector2 insideDir = cross > 0f ? new Vector2(-avgDir.y, avgDir.x) : new Vector2(avgDir.y, -avgDir.x);

                        // Find a neighbor in that direction that's land (not river/lake)
                        int bendTile = stampedPath[si];
                        int bestOxbow = -1;
                        float bestOxbowScore = float.NegativeInfinity;
                        foreach (int n in grid.neighbors[bendTile])
                        {
                            if (n < 0 || n >= tileCount) continue;
                            if (!tileData.TryGetValue(n, out var ntd)) continue;
                            if (ntd.isRiver || ntd.isLake || !ntd.isLand) continue;
                            if (ntd.biome == Biome.Coast || ntd.biome == Biome.Ocean) continue;

                            Vector2 toN = new Vector2(tileCoords[n].x - tileCoords[bendTile].x, tileCoords[n].y - tileCoords[bendTile].y);
                            if (toN.sqrMagnitude > 0.001f) toN.Normalize();
                            float score = Vector2.Dot(toN, insideDir);
                            if (score > bestOxbowScore) { bestOxbowScore = score; bestOxbow = n; }
                        }

                        if (bestOxbow >= 0 && bestOxbowScore > 0.2f)
                        {
                            var otd = tileData[bestOxbow];
                            otd.biome = Biome.Lake;
                            otd.isLand = false;
                            otd.isLake = true;
                            otd.isRiver = false;
                            otd.isHill = false;
                            otd.isMountain = false;
                            tileData[bestOxbow] = otd;
                            baseData[bestOxbow] = otd;
                            isLandTile[bestOxbow] = false;
                            isLakeTile[bestOxbow] = true;
                            oxbowsCreated++;
                            si += 4; // skip ahead to avoid overlapping oxbows
                        }
                    }
                }

                // --- Erosion Simulation ---
                // Instead of subtracting a fixed depth and hoping for the best,
                // simulate water eroding a smooth channel from source to mouth.
                if (stampedPath.Count > 1)
                {
                    // Elevation floor: river beds must never go below coast level
                    // This prevents rivers from sinking the terrain below sea level
                    float riverBedFloor = coastElevation + 0.1f;

                    // Step 1: Determine anchor elevations
                    // Source anchor: if from a lake, match the lake bed elevation; otherwise use terrain
                    float sourceAnchor = tileData[stampedPath[0]].elevation;
                    if (sourceFromLake && sourceToLakeId.TryGetValue(sourceIndex, out int srcLakeId))
                    {
                        // Find the lowest adjacent lake tile elevation to pin the river start
                        float lowestLakeBed = float.MaxValue;
                        foreach (int n in grid.neighbors[stampedPath[0]])
                        {
                            if (n < 0 || n >= tileCount) continue;
                            if (!tileData.TryGetValue(n, out var ntd)) continue;
                            if (ntd.isLake && ntd.elevation < lowestLakeBed)
                                lowestLakeBed = ntd.elevation;
                        }
                        if (lowestLakeBed < float.MaxValue * 0.5f)
                            sourceAnchor = lowestLakeBed;
                    }
                    // Apply initial carve to source anchor, but respect floor
                    sourceAnchor = Mathf.Max(sourceAnchor - riverDepth, riverBedFloor);

                    // Mouth anchor: slightly above coast elevation (river flows INTO coast, not below it)
                    float mouthAnchor = Mathf.Max(coastElevation + 0.05f, riverBedFloor);

                    // Step 2: Initialize river bed elevations with terrain-aware baseline
                    // Each tile starts at its natural terrain minus riverDepth, 
                    // then erosion smooths everything. Respect the floor.
                    float[] bedElev = new float[stampedPath.Count];
                    for (int si = 0; si < stampedPath.Count; si++)
                    {
                        bedElev[si] = Mathf.Max(tileData[stampedPath[si]].elevation - riverDepth, riverBedFloor);
                    }
                    // Pin anchors
                    bedElev[0] = sourceAnchor;
                    bedElev[stampedPath.Count - 1] = mouthAnchor;

                    // Step 3: Iterative erosion
                    // Water flows downstream, eroding tiles that are too high relative to their
                    // downstream neighbor. Multiple passes converge to a smooth channel.
                    int erosionPasses = 30;
                    float erosionRate = 0.5f; // how quickly tiles erode toward target (0-1)
                    for (int ep = 0; ep < erosionPasses; ep++)
                    {
                        bool anyChange = false;

                        // Forward pass (source → mouth): enforce downhill flow
                        // Each tile should be at most slightly higher than the next
                        float idealDropPerTile = Mathf.Max(0.001f, (sourceAnchor - mouthAnchor) / stampedPath.Count);
                        for (int si = 1; si < stampedPath.Count; si++)
                        {
                            float targetElev = bedElev[si - 1] - idealDropPerTile;
                            // If terrain is naturally lower, follow the valley
                            float terrainBed = tileData[stampedPath[si]].elevation - riverDepth;
                            // Use whichever is lower: the smooth ramp or the natural valley
                            float desired = Mathf.Min(targetElev, terrainBed);
                            // But never go above the upstream tile (monotonic descent)
                            desired = Mathf.Min(desired, bedElev[si - 1] - 0.001f);

                            if (bedElev[si] > desired)
                            {
                                bedElev[si] = Mathf.Lerp(bedElev[si], desired, erosionRate);
                                anyChange = true;
                            }
                        }

                        // Backward pass (mouth → source): smooth out any steep drops
                        // If a downstream tile is much lower, gently pull it toward a smooth gradient
                        for (int si = stampedPath.Count - 2; si >= 1; si--)
                        {
                            float avgNeighbor = (bedElev[si - 1] + bedElev[si + 1]) * 0.5f;
                            // If this tile is much higher than its average neighbors, erode it
                            if (bedElev[si] > avgNeighbor + idealDropPerTile)
                            {
                                bedElev[si] = Mathf.Lerp(bedElev[si], avgNeighbor, erosionRate * 0.5f);
                                anyChange = true;
                            }
                        }

                        // Re-pin anchors and enforce floor each pass
                        bedElev[0] = sourceAnchor;
                        bedElev[stampedPath.Count - 1] = mouthAnchor;
                        for (int fi = 0; fi < stampedPath.Count; fi++)
                            bedElev[fi] = Mathf.Max(bedElev[fi], riverBedFloor);

                        if (!anyChange) break;
                    }

                    // Step 4: Final monotonic enforcement — absolute guarantee of no uphill
                    // But never drop below the floor
                    for (int si = 1; si < stampedPath.Count; si++)
                    {
                        if (bedElev[si] > bedElev[si - 1] - 0.001f)
                            bedElev[si] = Mathf.Max(bedElev[si - 1] - 0.001f, riverBedFloor);
                    }

                    // Step 5: Apply eroded elevations to tile data
                    for (int si = 0; si < stampedPath.Count; si++)
                    {
                        var td = tileData[stampedPath[si]];
                        td.elevation = bedElev[si];
                        tileData[stampedPath[si]] = td;
                        baseData[stampedPath[si]] = td;
                    }

                    // Step 6: Corridor erosion — erode neighbor tiles toward the river bed
                    // to create natural valley banks instead of cliff walls
                    for (int si = 0; si < stampedPath.Count; si++)
                    {
                        int riverIdx = stampedPath[si];
                        float riverElev = tileData[riverIdx].elevation;

                        foreach (int n in grid.neighbors[riverIdx])
                        {
                            if (n < 0 || n >= tileCount) continue;
                            if (!tileData.TryGetValue(n, out var ntd)) continue;
                            if (ntd.isRiver || ntd.isLake) continue; // don't touch other water
                            if (!ntd.isLand) continue;

                            // Blend neighbor 40% toward the river bed — creates a gentle bank
                            // But never drop land below flatElevationMin (flat terrain floor)
                            float bankTarget = riverElev + riverDepth * 1.5f; // bank sits above water
                            float landFloor = flatElevationMin;
                            if (ntd.elevation > bankTarget)
                            {
                                ntd.elevation = Mathf.Max(Mathf.Lerp(ntd.elevation, bankTarget, 0.4f), landFloor);
                                tileData[n] = ntd;
                                baseData[n] = ntd;
                            }
                        }
                    }
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
                elevationExponent = 2.0f;
                hillNoiseCutoff = 0.87f;
                mountainNoiseCutoff = 0.975f;
                flatElevationMin = 32.0f;
                flatElevationMax = 34.0f;
                hillElevationMin = 33.5f;
                hillElevationMax = 36.0f;
                mountainElevationMin = 35.5f;
                mountainElevationMax = 41.0f;
                ridgeStrength = 0.06f;
                terrainWarpStrength = 0.02f;
                terrainWarpFrequencyMultiplier = 0.45f;
                terrainProvinceFrequencyMultiplier = 0.18f;
                terrainProvinceStrength = 0.03f;
                hillNoiseFrequencyMultiplier = 1.8f;
                hillNoiseStrength = 0.025f;
                mountainNoiseFrequencyMultiplier = 1.1f;
                mountainNoiseStrength = 0.04f;
                basinFrequencyMultiplier = 0.65f;
                basinCarvingStrength = 0.03f;
                valleyFrequencyMultiplier = 2.0f;
                valleyCarvingStrength = 0.02f;
                mesaStrength = 0.02f;
                escarpmentStrength = 0.015f;
                erosionStrength = 0.12f;
                break;
            case 1: // Smooth — gentle rolling terrain, some hills, rare mountains
                elevationExponent = 1.55f;
                hillNoiseCutoff = 0.8f;
                mountainNoiseCutoff = 0.95f;
                flatElevationMin = 32.0f;
                flatElevationMax = 34.0f;
                hillElevationMin = 33.5f;
                hillElevationMax = 36.0f;
                mountainElevationMin = 35.5f;
                mountainElevationMax = 41.0f;
                ridgeStrength = 0.07f;
                terrainWarpStrength = 0.06f;
                terrainWarpFrequencyMultiplier = 0.55f;
                terrainProvinceFrequencyMultiplier = 0.22f;
                terrainProvinceStrength = 0.04f;
                hillNoiseFrequencyMultiplier = 2.1f;
                hillNoiseStrength = 0.04f;
                mountainNoiseFrequencyMultiplier = 1.25f;
                mountainNoiseStrength = 0.07f;
                basinFrequencyMultiplier = 0.72f;
                basinCarvingStrength = 0.06f;
                valleyFrequencyMultiplier = 2.4f;
                valleyCarvingStrength = 0.045f;
                mesaStrength = 0.035f;
                escarpmentStrength = 0.03f;
                erosionStrength = 0.16f;
                break;
            case 2: // Standard — balanced mix
                elevationExponent = 1.00f;
                hillNoiseCutoff = 0.75f;
                mountainNoiseCutoff = 0.90f;
                flatElevationMin = 32.0f;
                flatElevationMax = 34.0f;
                hillElevationMin = 34.5f;
                hillElevationMax = 37.0f;
                mountainElevationMin = 37.01f;
                mountainElevationMax = 43.0f;
                ridgeStrength = 0.06f;
                terrainWarpStrength = 0.15f;
                terrainWarpFrequencyMultiplier = 0.7f;
                terrainProvinceFrequencyMultiplier = 0.3f;
                terrainProvinceStrength = 0.2f;
                hillNoiseFrequencyMultiplier = 2.6f;
                hillNoiseStrength = 0.06f;
                mountainNoiseFrequencyMultiplier = 1.5f;
                mountainNoiseStrength = 0.14f;
                basinFrequencyMultiplier = 0.85f;
                basinCarvingStrength = 0.11f;
                valleyFrequencyMultiplier = 3.0f;
                valleyCarvingStrength = 0.08f;
                mesaStrength = 0.1f;
                escarpmentStrength = 0.07f;
                erosionStrength = 0.22f;
                break;
            case 3: // Mountainous — lots of hills, frequent mountains
                elevationExponent = 1.1f;
                hillNoiseCutoff = 0.66f;
                mountainNoiseCutoff = 0.84f;
                flatElevationMin = 32.0f;
                flatElevationMax = 34.0f;
                hillElevationMin = 33.5f;
                hillElevationMax = 36.0f;
                mountainElevationMin = 35.5f;
                mountainElevationMax = 41.5f;
                ridgeStrength = 0.14f;
                terrainWarpStrength = 0.14f;
                terrainWarpFrequencyMultiplier = 0.85f;
                terrainProvinceFrequencyMultiplier = 0.38f;
                terrainProvinceStrength = 0.08f;
                hillNoiseFrequencyMultiplier = 3.1f;
                hillNoiseStrength = 0.09f;
                mountainNoiseFrequencyMultiplier = 1.75f;
                mountainNoiseStrength = 0.18f;
                basinFrequencyMultiplier = 1.0f;
                basinCarvingStrength = 0.13f;
                valleyFrequencyMultiplier = 3.5f;
                valleyCarvingStrength = 0.1f;
                mesaStrength = 0.09f;
                escarpmentStrength = 0.1f;
                erosionStrength = 0.28f;
                break;
            case 4: // Alpine — extremely mountainous, dramatic peaks
                elevationExponent = 1.0f;
                hillNoiseCutoff = 0.55f;
                mountainNoiseCutoff = 0.75f;
                flatElevationMin = 32.0f;
                flatElevationMax = 34.0f;
                hillElevationMin = 33.5f;
                hillElevationMax = 36.0f;
                mountainElevationMin = 35.5f;
                mountainElevationMax = 42.5f;
                ridgeStrength = 0.18f;
                terrainWarpStrength = 0.18f;
                terrainWarpFrequencyMultiplier = 1.0f;
                terrainProvinceFrequencyMultiplier = 0.45f;
                terrainProvinceStrength = 0.12f;
                hillNoiseFrequencyMultiplier = 3.6f;
                hillNoiseStrength = 0.12f;
                mountainNoiseFrequencyMultiplier = 2.0f;
                mountainNoiseStrength = 0.24f;
                basinFrequencyMultiplier = 1.1f;
                basinCarvingStrength = 0.15f;
                valleyFrequencyMultiplier = 4.0f;
                valleyCarvingStrength = 0.12f;
                mesaStrength = 0.11f;
                escarpmentStrength = 0.13f;
                erosionStrength = 0.32f;
                break;
            default: // Fallback to Standard
                elevationExponent = 1.4f;
                hillNoiseCutoff = 0.8f;
                mountainNoiseCutoff = 0.91f;
                flatElevationMin = 32.0f;
                flatElevationMax = 34.0f;
                hillElevationMin = 33.5f;
                hillElevationMax = 36.0f;
                mountainElevationMin = 35.5f;
                mountainElevationMax = 41.0f;
                ridgeStrength = 0.09f;
                terrainWarpStrength = 0.08f;
                terrainWarpFrequencyMultiplier = 0.7f;
                terrainProvinceFrequencyMultiplier = 0.3f;
                terrainProvinceStrength = 0.05f;
                hillNoiseFrequencyMultiplier = 2.6f;
                hillNoiseStrength = 0.06f;
                mountainNoiseFrequencyMultiplier = 1.5f;
                mountainNoiseStrength = 0.14f;
                basinFrequencyMultiplier = 0.85f;
                basinCarvingStrength = 0.1f;
                valleyFrequencyMultiplier = 3.0f;
                valleyCarvingStrength = 0.075f;
                mesaStrength = 0.07f;
                escarpmentStrength = 0.07f;
                erosionStrength = 0.22f;
                break;
        }
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

        bool isPangaeaPreset = GameSetupData.selectedLandPreset == 4;
        bool isMostlyLandPreset = GameSetupData.selectedLandPreset == 5;

        if (isPangaeaPreset)
        {
            int widthTiles = Mathf.Clamp(Mathf.RoundToInt(mapWidthTiles * 0.9f), Mathf.Max(1, minContinentWidth), mapWidthTiles);
            int heightTiles = Mathf.Clamp(Mathf.RoundToInt(mapHeightTiles * 0.68f), Mathf.Max(1, minContinentHeight), mapHeightTiles);
            int centerYJitter = Mathf.Max(1, Mathf.RoundToInt(mapHeightTiles * 0.04f));
            var pangaeaRand = new System.Random(rndSeed ^ 0x51A9);

            continents.Add(new ContinentData {
                name = "Pangaea",
                center = new Vector2Int(
                    pangaeaRand.Next(0, Mathf.Max(1, mapWidthTiles)),
                    Mathf.Clamp(mapHeightTiles / 2 + pangaeaRand.Next(-centerYJitter, centerYJitter + 1), 0, Mathf.Max(0, mapHeightTiles - 1))
                ),
                widthTiles = widthTiles,
                heightTiles = heightTiles
            });

            return continents;
        }

        if (isMostlyLandPreset)
        {
            var landHeavyRand = new System.Random(rndSeed ^ 0x7B31);
            int continentCount = Mathf.Max(2, count);
            int baseWidth = Mathf.Clamp(Mathf.RoundToInt(mapWidthTiles * 0.62f), Mathf.Max(1, minContinentWidth), mapWidthTiles);
            int altWidth = Mathf.Clamp(Mathf.RoundToInt(mapWidthTiles * 0.48f), Mathf.Max(1, minContinentWidth), mapWidthTiles);
            int baseHeight = Mathf.Clamp(Mathf.RoundToInt(mapHeightTiles * 0.56f), Mathf.Max(1, minContinentHeight), mapHeightTiles);
            int altHeight = Mathf.Clamp(Mathf.RoundToInt(mapHeightTiles * 0.44f), Mathf.Max(1, minContinentHeight), mapHeightTiles);
            int yJitter = Mathf.Max(1, Mathf.RoundToInt(mapHeightTiles * 0.06f));

            for (int i = 0; i < continentCount; i++)
            {
                float t = continentCount == 1 ? 0.5f : (float)i / continentCount;
                int centerX = Mathf.RoundToInt(t * mapWidthTiles + mapWidthTiles * 0.12f) % Mathf.Max(1, mapWidthTiles);
                int targetY = (i % 2 == 0)
                    ? Mathf.RoundToInt(mapHeightTiles * 0.42f)
                    : Mathf.RoundToInt(mapHeightTiles * 0.58f);

                continents.Add(new ContinentData {
                    name = $"Mainland {i + 1}",
                    center = new Vector2Int(
                        centerX,
                        Mathf.Clamp(targetY + landHeavyRand.Next(-yJitter, yJitter + 1), 0, Mathf.Max(0, mapHeightTiles - 1))
                    ),
                    widthTiles = i == 0 ? baseWidth : altWidth,
                    heightTiles = i == 0 ? baseHeight : altHeight
                });
            }

            return continents;
        }

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
        int q = col - ((row - (row & 1)) / 2);
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

    private void ReleaseGeologyCaches()
    {
        if (geologyProvinceMap != null) { ArrayPoolUtils.ReturnInt(geologyProvinceMap); geologyProvinceMap = null; }
        if (geologyMarginTypeMap != null) { ArrayPoolUtils.ReturnInt(geologyMarginTypeMap); geologyMarginTypeMap = null; }
        if (geologyStressMap != null) { ArrayPoolUtils.ReturnFloat(geologyStressMap); geologyStressMap = null; }
        if (geologyAgeMap != null) { ArrayPoolUtils.ReturnFloat(geologyAgeMap); geologyAgeMap = null; }
        if (geologyDrainageMap != null) { ArrayPoolUtils.ReturnFloat(geologyDrainageMap); geologyDrainageMap = null; }
        if (geologySedimentMap != null) { ArrayPoolUtils.ReturnFloat(geologySedimentMap); geologySedimentMap = null; }
    }

    private void EnsureGeologyCaches(int tileCount)
    {
        if (geologyProvinceMap != null && geologyProvinceMap.Length >= tileCount &&
            geologyMarginTypeMap != null && geologyMarginTypeMap.Length >= tileCount &&
            geologyStressMap != null && geologyStressMap.Length >= tileCount &&
            geologyAgeMap != null && geologyAgeMap.Length >= tileCount &&
            geologyDrainageMap != null && geologyDrainageMap.Length >= tileCount &&
            geologySedimentMap != null && geologySedimentMap.Length >= tileCount)
            return;

        ReleaseGeologyCaches();
        geologyProvinceMap = ArrayPoolUtils.RentInt(tileCount);
        geologyMarginTypeMap = ArrayPoolUtils.RentInt(tileCount);
        geologyStressMap = ArrayPoolUtils.RentFloat(tileCount);
        geologyAgeMap = ArrayPoolUtils.RentFloat(tileCount);
        geologyDrainageMap = ArrayPoolUtils.RentFloat(tileCount);
        geologySedimentMap = ArrayPoolUtils.RentFloat(tileCount);
    }

    private void BuildAdvancedGeologyFramework(Vector2Int[] tileCoords, bool[] isLandTile, bool[] isLakeTile, int tilesX, float mapWidth, float mapHeight, float elevFreqPeriodic)
    {
        if (!enableAdvancedGeologyFramework || grid == null || noise == null)
        {
            ReleaseGeologyCaches();
            return;
        }

        int tileCount = grid.TileCount;
        EnsureGeologyCaches(tileCount);

        var landSources = new List<int>();
        var waterSources = new List<int>();
        for (int i = 0; i < tileCount; i++)
        {
            if (isLandTile[i] || isLakeTile[i]) landSources.Add(i);
            else waterSources.Add(i);
        }

        int[] distToWater = BuildDistanceMap(waterSources.Count > 0 ? waterSources : landSources);
        int[] distToLand = BuildDistanceMap(landSources.Count > 0 ? landSources : waterSources);

        try
        {
            float provinceFreq = elevFreqPeriodic * Mathf.Max(0.03f, tectonicProvinceFrequencyMultiplier);
            float stressFreq = provinceFreq * 1.6f;
            float riftFreq = provinceFreq * 1.25f;
            float basinFreq = provinceFreq * 2.2f;
            float ageFreq = provinceFreq * 0.8f;

            for (int i = 0; i < tileCount; i++)
            {
                Vector2 tilePos = new Vector2(tileCoords[i].x, tileCoords[i].y);
                bool isLand = isLandTile[i] || isLakeTile[i];
                float provinceNoise = noise.GetElevationPeriodic(tilePos + new Vector2(700f, 150f), mapWidth, mapHeight, provinceFreq);
                float stressNoise = noise.GetElevationPeriodic(tilePos + new Vector2(1500f, 450f), mapWidth, mapHeight, stressFreq);
                float divergenceNoise = noise.GetElevationPeriodic(tilePos + new Vector2(2200f, 980f), mapWidth, mapHeight, riftFreq);
                float basinNoise = noise.GetElevationPeriodic(tilePos + new Vector2(2900f, 1280f), mapWidth, mapHeight, basinFreq);
                float ageNoise = noise.GetElevationPeriodic(tilePos + new Vector2(3600f, 1720f), mapWidth, mapHeight, ageFreq);

                float coastal01 = 0f;
                if (isLand)
                {
                    int d = distToWater[i];
                    if (d >= 0) coastal01 = 1f - Mathf.Clamp01((d - 1f) / 8f);
                }
                else
                {
                    int d = distToLand[i];
                    if (d >= 0) coastal01 = 1f - Mathf.Clamp01((d - 1f) / 6f);
                }

                float stress = Mathf.Clamp01(Mathf.Lerp(stressNoise, 1f - divergenceNoise, 0.25f));
                float age = Mathf.Clamp01(ageNoise + (provinceNoise < 0.35f ? 0.18f : 0f) - stress * 0.25f);
                float drainage = Mathf.Clamp01(
                    Mathf.Clamp01((0.55f - basinNoise) / 0.55f) * 0.5f +
                    coastal01 * 0.18f +
                    (1f - age) * 0.18f +
                    Mathf.Clamp01((stress - 0.55f) / 0.45f) * 0.12f);

                TectonicProvinceType province = TectonicProvinceType.StableShield;
                if (isLand)
                {
                    if (divergenceNoise > 0.74f && stress < 0.6f)
                        province = TectonicProvinceType.RiftZone;
                    else if (coastal01 > 0.45f && stress > 0.7f)
                        province = TectonicProvinceType.VolcanicArc;
                    else if (stress > 0.72f)
                        province = TectonicProvinceType.FoldBelt;
                    else if (basinNoise < 0.34f || (provinceNoise < 0.4f && stress > 0.55f))
                        province = TectonicProvinceType.ForelandBasin;
                    else if (coastal01 > 0.5f && age > 0.55f)
                        province = TectonicProvinceType.PassiveMargin;
                }

                CoastalMarginType margin = CoastalMarginType.None;
                if (coastal01 > 0.3f)
                {
                    switch (province)
                    {
                        case TectonicProvinceType.VolcanicArc:
                        case TectonicProvinceType.FoldBelt:
                            margin = CoastalMarginType.Active;
                            break;
                        case TectonicProvinceType.RiftZone:
                            margin = CoastalMarginType.Rifted;
                            break;
                        case TectonicProvinceType.PassiveMargin:
                        case TectonicProvinceType.StableShield:
                            margin = CoastalMarginType.Passive;
                            break;
                        case TectonicProvinceType.ForelandBasin:
                            margin = CoastalMarginType.Deltaic;
                            break;
                    }

                    if (drainage > 0.72f && margin == CoastalMarginType.Passive)
                        margin = CoastalMarginType.Deltaic;
                }

                geologyProvinceMap[i] = (int)province;
                geologyMarginTypeMap[i] = (int)margin;
                geologyStressMap[i] = stress;
                geologyAgeMap[i] = age;
                geologyDrainageMap[i] = drainage;
                geologySedimentMap[i] = Mathf.Clamp01(
                    ((margin == CoastalMarginType.Passive || margin == CoastalMarginType.Deltaic || province == TectonicProvinceType.ForelandBasin) ? 0.42f : 0.08f) +
                    drainage * 0.35f +
                    age * 0.08f -
                    stress * 0.2f);
            }
        }
        finally
        {
            ArrayPoolUtils.ReturnInt(distToWater);
            ArrayPoolUtils.ReturnInt(distToLand);
        }
    }

    private void ApplyAdvancedGeologyClimateAdjustments(float[] sampledTemp, float[] sampledMoist, float[] sampledElev, bool[] isLandTile, bool[] isLakeTile, int tileCount)
    {
        if (!enableAdvancedGeologyFramework || geologyProvinceMap == null || geologyStressMap == null)
            return;

        float reliefRange = Mathf.Max(0.5f, mountainElevationMax - flatElevationMin);
        for (int i = 0; i < tileCount; i++)
        {
            if (!isLandTile[i] || isLakeTile[i])
                continue;

            var province = (TectonicProvinceType)geologyProvinceMap[i];
            var margin = (CoastalMarginType)geologyMarginTypeMap[i];
            float stress = geologyStressMap[i];
            float age = geologyAgeMap[i];
            float drainage = geologyDrainageMap[i];
            float sediment = geologySedimentMap[i];

            if (province == TectonicProvinceType.FoldBelt || province == TectonicProvinceType.VolcanicArc)
                sampledElev[i] += reliefRange * geologyFrameworkStrength * (0.06f + stress * 0.08f);
            else if (province == TectonicProvinceType.RiftZone)
                sampledElev[i] -= reliefRange * geologyFrameworkStrength * 0.05f;
            else if (province == TectonicProvinceType.ForelandBasin)
                sampledElev[i] -= reliefRange * geologyFrameworkStrength * 0.045f;
            else if (province == TectonicProvinceType.StableShield)
                sampledElev[i] = Mathf.Lerp(sampledElev[i], Mathf.Lerp(flatElevationMin, hillElevationMin, 0.35f), crustAgeStrength * age * 0.18f);

            float glacialMask = sampledTemp[i] < 0.22f && sampledMoist[i] > 0.42f && sampledElev[i] >= hillElevationMin
                ? Mathf.Clamp01((0.22f - sampledTemp[i]) / 0.22f)
                : 0f;
            if (glacialMask > 0.001f)
            {
                float valleyFloor = Mathf.Lerp(hillElevationMin, mountainElevationMin, 0.25f);
                sampledElev[i] = Mathf.Lerp(sampledElev[i], Mathf.Max(valleyFloor, sampledElev[i] - reliefRange * 0.08f), glaciationStrength * glacialMask);
                if (margin == CoastalMarginType.Active || margin == CoastalMarginType.Passive)
                    geologyMarginTypeMap[i] = (int)CoastalMarginType.Glaciated;
            }

            float sedimentMask = Mathf.Clamp01(sediment * sedimentationStrength + drainage * drainageBasinStrength * 0.4f);
            if (sedimentMask > 0.001f && sampledElev[i] <= hillElevationMin + reliefRange * 0.12f)
            {
                float depositionalTarget = Mathf.Lerp(flatElevationMin, hillElevationMin, 0.12f);
                sampledElev[i] = Mathf.Lerp(sampledElev[i], depositionalTarget, sedimentMask * 0.22f);
            }

            sampledElev[i] = Mathf.Clamp(sampledElev[i], flatElevationMin - 0.25f, mountainElevationMax);
        }
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
        // Ocean water elevation = coastElevation (shared sea level for all ocean/seas/coast tiles)
        float oceanWaterElev = coastElevation;
        int[] waterDistanceFromCoast = new int[tileCount];
        for (int i = 0; i < tileCount; i++) waterDistanceFromCoast[i] = -1;
        var coastalWaterQueue = new Queue<int>();

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
                td.trenchDepth = 0f;

                if (b == Biome.Ocean)
                {
                    td.underwaterBiome = Biome.AbyssalPlains;
                    td.elevation = Mathf.Min(td.elevation, oceanElevation - 0.35f);
                }
                else
                {
                    td.underwaterBiome = Biome.Ocean;
                }

                tileData[i] = td;
                baseData[i] = td;

                if (b == Biome.Coast)
                {
                    waterDistanceFromCoast[i] = 0;
                    coastalWaterQueue.Enqueue(i);
                }
            }
        }

        while (coastalWaterQueue.Count > 0)
        {
            int current = coastalWaterQueue.Dequeue();
            int nextDistance = waterDistanceFromCoast[current] + 1;

            foreach (int n in hexGrid.neighbors[current])
            {
                if (n < 0 || n >= tileCount || waterDistanceFromCoast[n] >= 0) continue;
                if (!tileData.TryGetValue(n, out var ntd)) continue;
                if (ntd.biome != Biome.Ocean && ntd.biome != Biome.Seas) continue;

                waterDistanceFromCoast[n] = nextDistance;
                coastalWaterQueue.Enqueue(n);
            }
        }

        float Hash01(int value, int salt)
        {
            uint h = unchecked((uint)(value * 374761393)) ^ unchecked((uint)(salt * 668265263));
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0x00FFFFFFu) / 16777215f;
        }

            bool useAdvancedGeology =
                enableAdvancedGeologyFramework &&
                geologyProvinceMap != null &&
                geologyMarginTypeMap != null &&
                geologyStressMap != null &&
                geologyAgeMap != null &&
                geologySedimentMap != null;

        bool IsDeepOceanCandidate(int idx)
        {
            if (idx < 0 || idx >= tileCount) return false;
            if (!tileData.TryGetValue(idx, out var td)) return false;
            return td.biome == Biome.Ocean && waterDistanceFromCoast[idx] >= 4;
        }

        void StampTrenchTile(int idx, float targetDepth)
        {
            if (!tileData.TryGetValue(idx, out var td)) return;
            if (td.biome != Biome.Ocean) return;

            td.underwaterBiome = Biome.Trench;
            td.trenchDepth = Mathf.Min(td.trenchDepth, targetDepth);
            td.elevation = Mathf.Min(td.elevation, oceanElevation + td.trenchDepth);
            tileData[idx] = td;
            baseData[idx] = td;
        }

        var deepOceanCandidates = new List<int>();
        for (int i = 0; i < tileCount; i++)
        {
            if (IsDeepOceanCandidate(i))
                deepOceanCandidates.Add(i);
        }

        if (deepOceanCandidates.Count > 0)
        {
            var trenchRand = new System.Random(unchecked(seed ^ 0x54A9B31));
            var usedCenterlineTiles = new HashSet<int>();
            int trenchTargetCount = Mathf.Clamp(tileCount / 3500, 1, 4);

            List<int> BuildTrenchPath(int startTile, int desiredLength)
            {
                var path = new List<int>();
                var visited = new HashSet<int>();
                int current = startTile;
                Vector2 previousDirection = Vector2.zero;

                while (path.Count < desiredLength)
                {
                    path.Add(current);
                    visited.Add(current);

                    int bestNext = -1;
                    float bestScore = float.NegativeInfinity;
                    Vector3 currentCenter = hexGrid.tileCenters[current];

                    foreach (int n in hexGrid.neighbors[current])
                    {
                        if (!IsDeepOceanCandidate(n)) continue;
                        if (visited.Contains(n) || usedCenterlineTiles.Contains(n)) continue;

                        Vector3 dir3 = hexGrid.tileCenters[n] - currentCenter;
                        dir3.y = 0f;
                        if (dir3.sqrMagnitude <= 0.0001f) continue;

                        Vector2 dir = new Vector2(dir3.x, dir3.z).normalized;
                        float forwardBias = previousDirection == Vector2.zero ? 0f : Vector2.Dot(previousDirection, dir);
                        float score =
                            waterDistanceFromCoast[n] * 0.9f +
                            Hash01(n, seed ^ path.Count) * 1.25f +
                            forwardBias * 1.5f;

                        if (useAdvancedGeology)
                        {
                            var province = (TectonicProvinceType)geologyProvinceMap[n];
                            var margin = (CoastalMarginType)geologyMarginTypeMap[n];
                            score += geologyStressMap[n] * 0.9f + (1f - geologyAgeMap[n]) * 0.45f;
                            if (province == TectonicProvinceType.RiftZone) score += 0.6f;
                            if (margin == CoastalMarginType.Active || margin == CoastalMarginType.Rifted) score += 0.7f;
                        }

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestNext = n;
                        }
                    }

                    if (bestNext < 0)
                        break;

                    Vector3 step3 = hexGrid.tileCenters[bestNext] - currentCenter;
                    step3.y = 0f;
                    previousDirection = new Vector2(step3.x, step3.z).normalized;
                    current = bestNext;
                }

                return path.Count >= 6 ? path : null;
            }

            for (int trenchIndex = 0; trenchIndex < trenchTargetCount; trenchIndex++)
            {
                if (deepOceanCandidates.Count == 0) break;

                int bestSeedIdx = -1;
                float bestSeedScore = float.NegativeInfinity;
                int attempts = Mathf.Min(18, deepOceanCandidates.Count);
                for (int attempt = 0; attempt < attempts; attempt++)
                {
                    int candidate = deepOceanCandidates[trenchRand.Next(deepOceanCandidates.Count)];
                    if (usedCenterlineTiles.Contains(candidate)) continue;

                    float candidateScore = waterDistanceFromCoast[candidate] + Hash01(candidate, seed ^ 0x2D1);
                    if (useAdvancedGeology)
                    {
                        var province = (TectonicProvinceType)geologyProvinceMap[candidate];
                        var margin = (CoastalMarginType)geologyMarginTypeMap[candidate];
                        float stress = geologyStressMap[candidate];
                        float age = geologyAgeMap[candidate];

                        if (province == TectonicProvinceType.RiftZone) candidateScore += 0.9f;
                        if (margin == CoastalMarginType.Active) candidateScore += 1.15f;
                        else if (margin == CoastalMarginType.Rifted) candidateScore += 0.8f;
                        candidateScore += stress * 1.2f + (1f - age) * 0.6f;
                    }
                    if (candidateScore > bestSeedScore)
                    {
                        bestSeedScore = candidateScore;
                        bestSeedIdx = candidate;
                    }
                }

                if (bestSeedIdx < 0) continue;

                int desiredLength = Mathf.Clamp(8 + trenchRand.Next(6, 14), 8, Mathf.Max(10, tileCount / 180));
                var trenchPath = BuildTrenchPath(bestSeedIdx, desiredLength);
                if (trenchPath == null) continue;

                for (int i = 0; i < trenchPath.Count; i++)
                {
                    int centerTile = trenchPath[i];
                    usedCenterlineTiles.Add(centerTile);

                    float centerDepth = -1.25f - Hash01(centerTile, seed ^ 0x731) * 0.55f;
                    StampTrenchTile(centerTile, centerDepth);

                    int prev = i > 0 ? trenchPath[i - 1] : -1;
                    int next = i < trenchPath.Count - 1 ? trenchPath[i + 1] : -1;
                    Vector2 forward = Vector2.zero;
                    Vector3 centerWorld = hexGrid.tileCenters[centerTile];

                    if (prev >= 0)
                    {
                        Vector3 prevDir3 = centerWorld - hexGrid.tileCenters[prev];
                        prevDir3.y = 0f;
                        if (prevDir3.sqrMagnitude > 0.0001f)
                            forward += new Vector2(prevDir3.x, prevDir3.z).normalized;
                    }
                    if (next >= 0)
                    {
                        Vector3 nextDir3 = hexGrid.tileCenters[next] - centerWorld;
                        nextDir3.y = 0f;
                        if (nextDir3.sqrMagnitude > 0.0001f)
                            forward += new Vector2(nextDir3.x, nextDir3.z).normalized;
                    }
                    if (forward.sqrMagnitude <= 0.0001f)
                        forward = Vector2.right;
                    else
                        forward.Normalize();

                    int bestFlank = -1;
                    float bestFlankScore = float.NegativeInfinity;
                    int secondFlank = -1;
                    float secondFlankScore = float.NegativeInfinity;

                    foreach (int n in hexGrid.neighbors[centerTile])
                    {
                        if (!IsDeepOceanCandidate(n)) continue;
                        if (n == prev || n == next) continue;

                        Vector3 sideDir3 = hexGrid.tileCenters[n] - centerWorld;
                        sideDir3.y = 0f;
                        if (sideDir3.sqrMagnitude <= 0.0001f) continue;

                        Vector2 sideDir = new Vector2(sideDir3.x, sideDir3.z).normalized;
                        float perpendicularity = 1f - Mathf.Abs(Vector2.Dot(forward, sideDir));
                        float score = perpendicularity * 2f + waterDistanceFromCoast[n] * 0.5f + Hash01(n, seed ^ 0x1337);

                        if (score > bestFlankScore)
                        {
                            secondFlank = bestFlank;
                            secondFlankScore = bestFlankScore;
                            bestFlank = n;
                            bestFlankScore = score;
                        }
                        else if (score > secondFlankScore)
                        {
                            secondFlank = n;
                            secondFlankScore = score;
                        }
                    }

                    if (bestFlank >= 0)
                        StampTrenchTile(bestFlank, centerDepth * 0.7f);

                    if (secondFlank >= 0 && Hash01(centerTile, seed ^ 0x6A09) > 0.6f)
                        StampTrenchTile(secondFlank, centerDepth * 0.55f);
                }
            }
        }

        // --- Pass 1b: Underwater terrain variety (elevation only) ---
        // Continental shelves: gradual shallow slope near coastlines (distance 1-3)
        for (int i = 0; i < tileCount; i++)
        {
            if (!tileData.TryGetValue(i, out var td)) continue;
            if (td.biome != Biome.Ocean && td.biome != Biome.Seas) continue;
            int dist = waterDistanceFromCoast[i];
            if (dist < 1 || dist > 3) continue;
            if (td.underwaterBiome == Biome.Trench) continue; // don't overwrite trenches

            // Gradual depth: shelf is shallower near coast
            float shelfFactor = 1f - (dist - 1) / 3f; // 1.0 at dist=1, 0.33 at dist=3
            float shelfTarget = oceanWaterElev - 0.3f;
            float shelfBlend = shelfFactor * 0.6f;
            if (useAdvancedGeology)
            {
                var margin = (CoastalMarginType)geologyMarginTypeMap[i];
                float sediment = geologySedimentMap[i];

                if (margin == CoastalMarginType.Passive)
                {
                    shelfTarget = oceanWaterElev - 0.22f;
                    shelfBlend += 0.08f;
                }
                else if (margin == CoastalMarginType.Deltaic)
                {
                    shelfTarget = oceanWaterElev - 0.16f;
                    shelfBlend += 0.14f;
                }
                else if (margin == CoastalMarginType.Glaciated)
                {
                    shelfTarget = oceanWaterElev - 0.2f;
                    shelfBlend += 0.06f;
                }
                else if (margin == CoastalMarginType.Active)
                {
                    shelfTarget = oceanWaterElev - 0.42f;
                    shelfBlend *= 0.78f;
                }
                else if (margin == CoastalMarginType.Rifted)
                {
                    shelfTarget = oceanWaterElev - 0.47f;
                    shelfBlend *= 0.72f;
                }

                shelfBlend = Mathf.Clamp01(shelfBlend + sediment * 0.12f * sedimentationStrength);
            }

            td.elevation = Mathf.Lerp(td.elevation, shelfTarget, shelfBlend);
            tileData[i] = td;
            baseData[i] = td;
        }

        // Mid-ocean ridges: elevated paths through deep ocean
        if (deepOceanCandidates.Count > 20)
        {
            var ridgeRand = new System.Random(unchecked(seed ^ 0xA7B3C1));
            var usedRidgeTiles = new HashSet<int>();
            int ridgeCount = Mathf.Clamp(tileCount / 5000, 1, 3);

            for (int ri = 0; ri < ridgeCount; ri++)
            {
                // Pick a seed tile far from coast
                int bestSeed = -1;
                float bestScore = float.NegativeInfinity;
                int attempts = Mathf.Min(20, deepOceanCandidates.Count);
                for (int a = 0; a < attempts; a++)
                {
                    int cand = deepOceanCandidates[ridgeRand.Next(deepOceanCandidates.Count)];
                    if (usedRidgeTiles.Contains(cand)) continue;
                    float sc = waterDistanceFromCoast[cand] * 0.8f + Hash01(cand, seed ^ 0xD2E5) * 1.5f;
                    if (useAdvancedGeology)
                    {
                        var province = (TectonicProvinceType)geologyProvinceMap[cand];
                        var margin = (CoastalMarginType)geologyMarginTypeMap[cand];
                        sc += (1f - geologyAgeMap[cand]) * 1.25f + geologyStressMap[cand] * 0.35f;
                        if (province == TectonicProvinceType.RiftZone) sc += 0.9f;
                        if (margin == CoastalMarginType.Rifted) sc += 0.55f;
                    }
                    if (sc > bestScore) { bestScore = sc; bestSeed = cand; }
                }
                if (bestSeed < 0) continue;

                // Walk a ridge path
                int ridgeLen = 6 + ridgeRand.Next(4, 12);
                var ridgePath = new List<int>();
                var ridgeVisited = new HashSet<int>();
                int cur = bestSeed;
                Vector2 prevDir = Vector2.zero;

                while (ridgePath.Count < ridgeLen)
                {
                    ridgePath.Add(cur);
                    ridgeVisited.Add(cur);
                    usedRidgeTiles.Add(cur);

                    int bestNext = -1;
                    float bestNextScore = float.NegativeInfinity;
                    Vector3 curCenter = hexGrid.tileCenters[cur];

                    foreach (int n in hexGrid.neighbors[cur])
                    {
                        if (!IsDeepOceanCandidate(n)) continue;
                        if (ridgeVisited.Contains(n) || usedRidgeTiles.Contains(n)) continue;

                        Vector3 d3 = hexGrid.tileCenters[n] - curCenter;
                        d3.y = 0f;
                        if (d3.sqrMagnitude <= 0.0001f) continue;
                        Vector2 dir = new Vector2(d3.x, d3.z).normalized;

                        float fwd = prevDir == Vector2.zero ? 0f : Vector2.Dot(prevDir, dir);
                        float ns = Hash01(n, seed ^ 0xE3F7 ^ ridgePath.Count) * 1.2f + fwd * 1.8f;
                        if (useAdvancedGeology)
                        {
                            ns += (1f - geologyAgeMap[n]) * 0.85f + geologyStressMap[n] * 0.3f;
                            if ((TectonicProvinceType)geologyProvinceMap[n] == TectonicProvinceType.RiftZone) ns += 0.6f;
                        }
                        if (ns > bestNextScore) { bestNextScore = ns; bestNext = n; }
                    }

                    if (bestNext < 0) break;

                    Vector3 step = hexGrid.tileCenters[bestNext] - curCenter;
                    step.y = 0f;
                    prevDir = new Vector2(step.x, step.z).normalized;
                    cur = bestNext;
                }

                if (ridgePath.Count < 4) continue;

                // Stamp ridge tiles with elevation bump
                foreach (int ridgeTile in ridgePath)
                {
                    if (!tileData.TryGetValue(ridgeTile, out var rtd)) continue;
                    if (rtd.underwaterBiome == Biome.Trench) continue;
                    float ridgeLift = 0.25f + Hash01(ridgeTile, seed ^ 0xF1A2) * 0.15f;
                    if (useAdvancedGeology)
                        ridgeLift += (1f - geologyAgeMap[ridgeTile]) * 0.12f + geologyStressMap[ridgeTile] * 0.04f;
                    rtd.elevation += ridgeLift;
                    tileData[ridgeTile] = rtd;
                    baseData[ridgeTile] = rtd;
                }
            }
        }

        // Seamounts: isolated underwater peaks scattered in deep ocean
        if (deepOceanCandidates.Count > 10)
        {
            var seamountRand = new System.Random(unchecked(seed ^ 0xBE47D9));
            int seamountCount = Mathf.Clamp(tileCount / 2500, 2, useAdvancedGeology ? 10 : 8);

            for (int si = 0; si < seamountCount; si++)
            {
                int idx = deepOceanCandidates[seamountRand.Next(deepOceanCandidates.Count)];
                if (!tileData.TryGetValue(idx, out var std)) continue;
                if (std.underwaterBiome == Biome.Trench) continue;

                float seamountLift = 0.35f + Hash01(idx, seed ^ 0x4C8A) * 0.25f;
                if (useAdvancedGeology)
                {
                    seamountLift += geologyStressMap[idx] * 0.08f;
                    if ((TectonicProvinceType)geologyProvinceMap[idx] == TectonicProvinceType.RiftZone)
                        seamountLift += 0.12f;
                    if ((CoastalMarginType)geologyMarginTypeMap[idx] == CoastalMarginType.Active)
                        seamountLift += 0.08f;
                }
                std.elevation += seamountLift;
                tileData[idx] = std;
                baseData[idx] = std;

                // Optionally bump one neighbor for a broader seamount base
                if (Hash01(idx, seed ^ 0x5D9B) > 0.5f)
                {
                    foreach (int n in hexGrid.neighbors[idx])
                    {
                        if (!IsDeepOceanCandidate(n)) continue;
                        if (!tileData.TryGetValue(n, out var ntd)) continue;
                        if (ntd.underwaterBiome == Biome.Trench) continue;

                        ntd.elevation += 0.15f + Hash01(n, seed ^ 0x6EA3) * 0.1f;
                        tileData[n] = ntd;
                        baseData[n] = ntd;
                        break; // only one neighbor
                    }
                }
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
            
            if (ShouldLogDiagnostics())
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
        
        // Phase B: Set water elevation from eroded bed.
        // Erosion already guarantees smooth downhill beds, so water simply sits a fixed
        // height above each tile's bed. Also sync river tiles adjacent to lakes.
        if (riverTileIndices.Count > 0)
        {
            // First pass: set baseline water from bed
            foreach (int ri in riverTileIndices)
            {
                var td = tileData[ri];
                td.waterElevation = td.elevation + (riverDepth * 0.75f);
                tileData[ri] = td;
            }

            // Second pass: pin river tiles adjacent to lakes to the lake's water level
            // so there's no cliff at the junction
            foreach (int ri in riverTileIndices)
            {
                var td = tileData[ri];
                foreach (int n in hexGrid.neighbors[ri])
                {
                    if (n < 0 || n >= tileCount) continue;
                    if (!tileData.TryGetValue(n, out var ntd)) continue;
                    if (ntd.isLake && ntd.waterElevation > 0f)
                    {
                        // Pin this river tile's water to the lake's water level
                        if (Mathf.Abs(td.waterElevation - ntd.waterElevation) > 0.01f)
                        {
                            td.waterElevation = ntd.waterElevation;
                            // Also ensure bed sits below lake water
                            if (td.elevation > ntd.waterElevation - riverDepth * 0.5f)
                                td.elevation = ntd.waterElevation - riverDepth * 0.5f;
                            tileData[ri] = td;
                        }
                        break; // only need one lake neighbor
                    }
                }
            }

            // Third pass: smooth water elevation along connected river tiles
            // so the junction pinning blends naturally into the rest of the river
            bool changed = true;
            int maxPasses = 15;
            int pass = 0;
            while (changed && pass < maxPasses)
            {
                changed = false;
                pass++;
                foreach (int ri in riverTileIndices)
                {
                    var td = tileData[ri];
                    float sumWater = td.waterElevation;
                    int count = 1;
                    foreach (int n in hexGrid.neighbors[ri])
                    {
                        if (n < 0 || n >= tileCount) continue;
                        if (!tileData.TryGetValue(n, out var ntd)) continue;
                        if (!ntd.isRiver) continue;
                        sumWater += ntd.waterElevation;
                        count++;
                    }
                    float avg = sumWater / count;
                    // Only lower water to smooth, never raise it (water flows downhill)
                    if (avg < td.waterElevation - 0.005f)
                    {
                        td.waterElevation = Mathf.Lerp(td.waterElevation, avg, 0.3f);
                        tileData[ri] = td;
                        changed = true;
                    }
                }
            }

            // Final safety: ensure bed is always below water
            foreach (int ri in riverTileIndices)
            {
                var td = tileData[ri];
                float margin = Mathf.Max(0.02f, riverDepth * 0.25f);
                if (td.elevation > td.waterElevation - margin)
                {
                    td.elevation = td.waterElevation - margin;
                    tileData[ri] = td;
                }
            }
        }

        // --- Pass 4: Terrain Skirt — smooth land toward water edges ---
        // BFS outward from water-edge land tiles, gradually blending elevation toward water level
        // to eliminate cliff faces at shorelines and riverbanks.
        {
            int skirtRings = 3;
            float[] ringBlend = { 0.7f, 0.4f, 0.15f }; // blend strength per ring distance
            float skirtFloor = flatElevationMin; // never drop land below the flat tier minimum

            // Find all land tiles adjacent to water (lake or river)
            var waterEdgeLand = new Dictionary<int, float>(); // tileIndex -> nearest water elevation
            for (int i = 0; i < tileCount; i++)
            {
                if (!tileData.TryGetValue(i, out var td)) continue;
                if (!td.isLand || td.isLake || td.isRiver) continue;

                float nearestWaterElev = float.MaxValue;
                foreach (int n in hexGrid.neighbors[i])
                {
                    if (n < 0 || n >= tileCount) continue;
                    if (!tileData.TryGetValue(n, out var ntd)) continue;
                    if ((ntd.isLake || ntd.isRiver) && ntd.waterElevation < nearestWaterElev)
                    {
                        nearestWaterElev = ntd.waterElevation;
                    }
                }
                if (nearestWaterElev < float.MaxValue * 0.5f)
                {
                    waterEdgeLand[i] = nearestWaterElev;
                }
            }

            // BFS ring expansion
            var currentRing = new Dictionary<int, float>(waterEdgeLand);
            var visited = new HashSet<int>(waterEdgeLand.Keys);

            for (int ring = 0; ring < skirtRings; ring++)
            {
                float blend = ringBlend[ring];
                var nextRing = new Dictionary<int, float>();

                foreach (var kvp in currentRing)
                {
                    int tileIdx = kvp.Key;
                    float targetWaterElev = kvp.Value;

                    if (!tileData.TryGetValue(tileIdx, out var td)) continue;
                    if (!td.isLand || td.isLake || td.isRiver) continue;

                    // Only lower land, never raise it, and respect the land floor
                    float blended = Mathf.Lerp(td.elevation, targetWaterElev, blend);
                    if (blended < td.elevation)
                    {
                        td.elevation = Mathf.Max(blended, skirtFloor);
                        tileData[tileIdx] = td;
                    }

                    // Expand to next ring neighbors
                    foreach (int n in hexGrid.neighbors[tileIdx])
                    {
                        if (n < 0 || n >= tileCount) continue;
                        if (visited.Contains(n)) continue;
                        if (!tileData.TryGetValue(n, out var ntd)) continue;
                        if (!ntd.isLand || ntd.isLake || ntd.isRiver) continue;

                        visited.Add(n);
                        nextRing[n] = targetWaterElev;
                    }
                }

                currentRing = nextRing;
            }

            if (ShouldLogDiagnostics())
                Debug.Log($"[PlanetGenerator] Terrain Skirt: smoothed {visited.Count} land tiles near water edges ({skirtRings} rings).");
        }

        // --- Pass 5: Elevation Floor Enforcement ---
        // Final safety pass: ensure no land tile has been eroded below its tier minimum.
        // Rivers and lakes are water features and can sit lower, but non-water land must
        // maintain minimum elevation for its classification.
        {
            int floorFixCount = 0;
            for (int i = 0; i < tileCount; i++)
            {
                if (!tileData.TryGetValue(i, out var td)) continue;
                if (!td.isLand) continue;
                if (td.isRiver || td.isLake) continue; // water features are exempt

                float floor;
                if (td.isMountain) floor = mountainElevationMin;
                else if (td.isHill) floor = hillElevationMin;
                else floor = flatElevationMin;

                if (td.elevation < floor)
                {
                    td.elevation = floor;
                    tileData[i] = td;
                    floorFixCount++;
                }
            }
            if (ShouldLogDiagnostics() && floorFixCount > 0)
                Debug.Log($"[PlanetGenerator] Elevation Floor Enforcement: raised {floorFixCount} land tiles to their tier minimum.");
        }

        int abyssalCount = 0;
        int trenchCount = 0;
        for (int i = 0; i < tileCount; i++)
        {
            if (!tileData.TryGetValue(i, out var td)) continue;
            if (td.underwaterBiome == Biome.AbyssalPlains) abyssalCount++;
            else if (td.underwaterBiome == Biome.Trench) trenchCount++;
        }

        if (ShouldLogDiagnostics())
            Debug.Log($"[PlanetGenerator] ComputeWaterMetadata: {nextLakeId} lake bodies labeled, river/ocean tiles tagged, abyssal={abyssalCount}, trench={trenchCount}.");
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
