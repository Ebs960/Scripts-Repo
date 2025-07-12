using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using SpaceGraphicsToolkit;
using SpaceGraphicsToolkit.Landscape;
using TMPro;

public class PlanetGenerator : MonoBehaviour
{
    public static PlanetGenerator Instance { get; private set; }

    [Header("Sphere Settings")] 
    public int subdivisions = 4;
    public bool randomSeed = true;
    public int seed = 12345;

    // Public property to access the seed
    public int Seed => seed;

    // --- New Continent Parameters (Method 2: Masked Noise + Guaranteed Core) ---
    [Header("Continent Generation (Deterministic Masked Noise)")]
    [Tooltip("The target number of continents. Placement is deterministic for common counts (1-8). Higher counts might revert to random spread.")]
    [Min(1)]
    public int numberOfContinents = 6;

    [Tooltip("Maximum longitudinal extent (width) of a continent mask in degrees.")]
    [Range(10f, 180f)]
    public float maxContinentWidthDegrees = 70f; 

    [Tooltip("Maximum latitudinal extent (height) of a continent mask in degrees.")]
    [Range(10f, 180f)]
    public float maxContinentHeightDegrees = 60f; 
    
    [Tooltip("Maximum random offset applied to deterministic seed positions (0 = no offset, higher = more variance).")]
    [Range(0f, 0.8f)]
    public float seedPositionVariance = 0.1f; // Controls randomness in seed placement
    
    // --- Noise Settings --- 
    [Header("Noise Settings")] 
    public float elevationFreq = 2f, moistureFreq = 4f;
    [Range(0.2f, 0.8f)]
    [Tooltip("Noise threshold for filling land *around* the guaranteed core within masks. Lower = more land.")]
    public float landThreshold = 0.5f;

    [Tooltip("Frequency multiplier for the continent noise function.")]
    public float continentNoiseFrequency = 20f;

    [Range(-0.3f, 0.3f)]
    [Tooltip("Bias for moisture levels. Positive values make the planet wetter, negative values make it drier.")]
    public float moistureBias = 0f;

    [Range(-0.65f, 0.65f)]
    [Tooltip("Bias for temperature. Positive values make the planet hotter, negative values make it colder.")]
    public float temperatureBias = 0f;

    // --- NEW: Elevation Features ---
    [Header("Elevation Features")]
    [Range(0f, 2f)]
    [Tooltip("Elevation value (0-1) above which tiles become mountains.")]
    public float mountainThreshold = 0.8f;
    [Range(0f, 2f)]
    [Tooltip("Elevation value (0-1) above which tiles become hills (if not already mountains).")]
    public float hillThreshold = 0.6f;
    
    // --- NEW: Elevation Range Settings ---
    [Header("Elevation Range Settings")]
    [Range(0f, 1f)]
    [Tooltip("The minimum elevation level for land and glacier tiles (before noise).")]
    public float baseLandElevation = 0.15f;
    
    [Range(0f, 1f)]
    [Tooltip("The fixed elevation level for coast tiles.")]
    public float coastElevation = 0.1f;
    
    [Range(0f, 2f)] // Allow slightly higher max potential if needed
    [Tooltip("The absolute maximum elevation any tile can reach (after noise).")]
    public float maxTotalElevation = 0.3f;
    
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

    // --- Island Generation ---
    [Header("Island Generation")]
    [Tooltip("Number of islands to generate (separate from continents)")]
    public int numberOfIslands = 8;
    [Tooltip("Whether to generate islands in addition to continents")]
    public bool generateIslands = true;
    [Range(5f, 25f)]
    [Tooltip("Maximum width of an island mask in degrees (smaller than continents)")]
    public float maxIslandWidthDegrees = 15f;
    [Range(5f, 25f)]
    [Tooltip("Maximum height of an island mask in degrees (smaller than continents)")]
    public float maxIslandHeightDegrees = 15f;
    [Range(0.4f, 0.8f)]
    [Tooltip("Noise threshold for island generation (higher = smaller islands)")]
    public float islandThreshold = 0.05f;

    // ... Biome Visuals, Extrusion, Climate Settings ... (Keep these)
    [Header("Biome Visuals")] public Color[] biomeColors = new Color[]
    {
        new Color(0.1f, 0.1f, 0.6f), // Ocean - deep blue
        new Color(0.8f, 0.8f, 0.5f), // Coast - sandy beige
        new Color(0.2f, 0.4f, 0.8f), // Seas - medium blue
        new Color(0.85f, 0.75f, 0.35f), // Desert - sandy yellow
        new Color(0.8f, 0.7f, 0.2f), // Savannah - yellow-green
        new Color(0.5f, 0.8f, 0.3f), // Plains - light green
        new Color(0.0f, 0.5f, 0.0f), // Forest - forest green
        new Color(0.0f, 0.35f, 0.0f), // Jungle - dark green
        new Color(0.5f, 0.3f, 0.0f), // Mountain - brown 
        new Color(0.9f, 0.9f, 0.9f), // Snow - white
        new Color(0.8f, 0.9f, 1.0f), // Glacier - light blue
        new Color(0.7f, 0.7f, 0.6f), // Tundra - gray-green
        new Color(0.6f, 0.55f, 0.2f), // Shrubland - olive green
        new Color(0.25f, 0.4f, 0.3f), // Marsh - dark blue-green
        new Color(0.2f, 0.4f, 0.2f), // Taiga - dark conifer green
        new Color(0.1f, 0.3f, 0.15f), // Swamp - very dark green
        new Color(0.5f, 0.4f, 0.3f), // Mountain - brown (NEW)
        new Color(0.3f, 0.5f, 0.9f),  // River - lighter blue (NEW)
        new Color(0.5f, 0.1f, 0.1f),  // Volcanic - dark red (NEW)
        new Color(0.8f, 0.7f, 0.7f)   // Steam - misty white (NEW)
    };
    public List<BiomeSettings> biomeSettings = new();
    [Tooltip("Wait this many frames before initial generation so IcoSphereGrid has finished generating.")]
    public int initializationDelay = 1;
    [Header("Extrusion Settings")] public float maxExtrusionHeight = 0.04f;
    [Header("Climate Settings")]
    [Range(0.65f, 0.95f)]
    [Tooltip("Controls the size of polar regions. Lower values = larger polar regions (0.7=63° latitude, 0.8=72° latitude)")]
    public float polarLatitudeThreshold = 0.8f;
    [Range(0.45f, 0.6f)]
    [Tooltip("Latitude above which Tundra/Taiga biomes appear. Must always be lower than Polar threshold.")]
    public float subPolarLatitudeThreshold = 0.7f;
    [Range(0.0f, 0.4f)]
    [Tooltip("Latitude (absolute) below which the special Equator band applies (tropical/desert/savanna only). Must be lower than Sub-polar threshold.")]
    public float equatorLatitudeThreshold = 0.2f;

    [Header("Map Type")]
    public string currentMapTypeName = ""; // The current map type name
    public bool isRainforestMapType = false; // Whether this is a rainforest map type (determined from map name)
    public bool isScorchedMapType = false; // Whether this is a scorched map type
    public bool isInfernalMapType = false; // Whether this is an infernal map type
    public bool isDemonicMapType = false; // Add this field

    [Header("Debug/Override Map Types")]
    public bool overrideMapTypeFlags = false;
    public bool debugIsDemonicMapType = false;
    public bool debugIsInfernalMapType = false;
    public bool debugIsScorchedMapType = false;
    public bool debugIsRainforestMapType = false;

    // ──────────────────────────────────────────────────────────────────────────────
    //  VISUAL LAYER  (integrated, no extra script needed)
    // ──────────────────────────────────────────────────────────────────────────────
    [Header("💠 SGT Sphere Landscape")]
    [SerializeField] int textureSize = 2048;
    [SerializeField] SgtSphereLandscape landscape;
    
    public enum BiomeMaskQuality { Standard, Optimized, Blended }
    
    [Header("Biome Mask Generation")]
    [Tooltip("Standard: Basic RGBA packing, Optimized: Better texture settings, Blended: Smooth transitions")]
    public BiomeMaskQuality biomeMaskQuality = BiomeMaskQuality.Optimized;
    [Range(1f, 5f)]
    [Tooltip("Blend radius for smooth biome transitions (only used with Blended quality)")]
    public float biomeBlendRadius = 2f;

    // Runtime-generated textures
    Texture2D heightTex;    // R16 – elevation 0‒1
    Texture2D biomeTex;     // R8 - biome index
    Texture2D biomeColorMap; // RGBA32 – biome colors

    static readonly int HeightMapID   = Shader.PropertyToID("_HeightMap");
    static readonly int BiomeMapID    = Shader.PropertyToID("_BiomeMap");
    static readonly int WaterColID    = Shader.PropertyToID("_WaterColor");

    // --------------------------- Private fields -----------------------------
    IcoSphereGrid grid;
    public IcoSphereGrid Grid => grid;
    NoiseSampler noise;
    readonly Dictionary<int, HexTileData> data = new();
    readonly Dictionary<int, HexTileData> baseData = new();
    readonly Dictionary<Biome, BiomeSettings> lookup = new();
    private Vector3 noiseOffset;
    // Cache elevation for river generation
    readonly Dictionary<int, float> tileElevation = new Dictionary<int, float>();
    private int landTilesGenerated = 0; // Moved to class scope to be accessible by local coroutines
    private LoadingPanelController loadingPanelController;

    // Object pooling for decorations
    private Dictionary<GameObject, Queue<GameObject>> decorationPools = new();
    private List<GameObject> activeDecorations = new();

    private GameObject GetPooledObject(GameObject prefab)
    {
        if (!decorationPools.TryGetValue(prefab, out var pool))
        {
            pool = new Queue<GameObject>();
            decorationPools[prefab] = pool;
        }
        if (pool.Count > 0)
        {
            var go = pool.Dequeue();
            go.SetActive(true);
            return go;
        }
        else
        {
            return Instantiate(prefab);
        }
    }

    private void ReturnPooledObject(GameObject prefab, GameObject go)
    {
        go.SetActive(false);
        if (!decorationPools.TryGetValue(prefab, out var pool))
        {
            pool = new Queue<GameObject>();
            decorationPools[prefab] = pool;
        }
        pool.Enqueue(go);
    }

    public void ClearAllDecorations()
    {
        foreach (var go in activeDecorations)
        {
            if (go != null)
            {
                var prefab = go.name.Contains("(Clone)") ? go.name.Replace("(Clone)", "").Trim() : go.name;
                ReturnPooledObject(go, go); // Pool by instance
            }
        }
        activeDecorations.Clear();
    }

    // --------------------------- Unity lifecycle -----------------------------
    void Awake()
    {
        // Set the static instance
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }

        // Initialize the grid for this planet
        grid = new IcoSphereGrid();
        grid.Generate(subdivisions, 1f); // generate unit sphere grid
                
        // Ensure BiomeSettings list has entries for all biomes if empty
        if (biomeSettings.Count == 0) {
            foreach (Biome b in Enum.GetValues(typeof(Biome))) {
                biomeSettings.Add(new BiomeSettings { biome = b }); 
            }
        }
        foreach (var bs in biomeSettings)
            if (!lookup.ContainsKey(bs.biome))
                lookup.Add(bs.biome, bs);
            else
                lookup[bs.biome] = bs; // Allow overriding default settings
                
        if (randomSeed) seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);

        noise = new NoiseSampler(seed);

        var rand = new System.Random(seed);
        float ox = (float)(rand.NextDouble() * 2000.0 - 1000.0);
        float oy = (float)(rand.NextDouble() * 2000.0 - 1000.0);
        float oz = (float)(rand.NextDouble() * 2000.0 - 1000.0);
        noiseOffset = new Vector3(ox, oy, oz);

        // Force SGT to recognize the new textures and update the mesh
        if (landscape != null) landscape.MarkForRebuild();

#if UNITY_EDITOR
        UnityEditor.EditorUtility.ClearProgressBar();
#endif
    }
    
    void Start()
    {
        // Subscribe to season changes to update visuals
        if (ClimateManager.Instance != null)
        {
            ClimateManager.Instance.OnSeasonChanged += HandleSeasonChange;
        }
    }

    void OnDestroy()
    {
        // Unsubscribe to prevent memory leaks
        if (ClimateManager.Instance != null)
        {
            ClimateManager.Instance.OnSeasonChanged -= HandleSeasonChange;
        }
    }

    private void HandleSeasonChange(Season newSeason)
    {
        Debug.Log($"[PlanetGenerator] Season changed to {newSeason}. Rebuilding visual maps.");
        BuildVisualMaps();
    }
    


    // --------------------------- Surface Generation --------------------------
    /// <summary>
    /// Generates the planet's surface with continents, oceans, and biomes.
    /// </summary>
    public System.Collections.IEnumerator GenerateSurface()
    {
        Debug.Log($"PlanetGenerator.GenerateSurface() CALLED. Map Type: {currentMapTypeName}, IsDemonic: {isDemonicMapType}, IsInfernal: {isInfernalMapType}, IsScorched: {isScorchedMapType}, IsRainforest: {isRainforestMapType}");
        Debug.Log($"Override Active: {overrideMapTypeFlags}, Debug Demonic: {debugIsDemonicMapType}, Debug Infernal: {debugIsInfernalMapType}, Debug Scorched: {debugIsScorchedMapType}, Debug Rainforest: {debugIsRainforestMapType}");
        // Clear previous data
        data.Clear();
        baseData.Clear();
        tileElevation.Clear();
        landTilesGenerated = 0;

        // --- Validate Thresholds ---
        if (hillThreshold >= mountainThreshold) {
            hillThreshold = mountainThreshold - 0.01f;
            if (hillThreshold < 0) hillThreshold = 0;
            Debug.LogWarning("Hill threshold cannot be >= Mountain threshold. Adjusting Hill threshold to " + hillThreshold);
        }
        if (subPolarLatitudeThreshold >= polarLatitudeThreshold) {
            subPolarLatitudeThreshold = polarLatitudeThreshold - 0.05f;
            if (subPolarLatitudeThreshold < 0f) subPolarLatitudeThreshold = 0f;
            Debug.LogWarning("SubPolar Latitude threshold cannot be >= Polar threshold. Adjusting SubPolar threshold to " + subPolarLatitudeThreshold);
        }
        if (equatorLatitudeThreshold >= subPolarLatitudeThreshold) {
            equatorLatitudeThreshold = subPolarLatitudeThreshold - 0.05f;
            if (equatorLatitudeThreshold < 0f) equatorLatitudeThreshold = 0f;
            Debug.LogWarning("Equator Latitude threshold cannot be >= SubPolar threshold. Adjusting Equator threshold to " + equatorLatitudeThreshold);
        }

        // ── 1. Noise Offset Setup (as before) ──────────────────────────────
        if (noiseOffset == Vector3.zero) {
            var prng = new System.Random(seed);
            noiseOffset = new Vector3(
                 (float)(prng.NextDouble() * 2000.0 - 1000.0),
                 (float)(prng.NextDouble() * 2000.0 - 1000.0),
                 (float)(prng.NextDouble() * 2000.0 - 1000.0));
        }
        int tileCount = grid.TileCount;
        if (tileCount == 0) {
            grid.Generate(subdivisions, 1f);
            tileCount = grid.TileCount;
        }

        // ---------- 2. Generate Deterministic Continent Seeds ------------------
        List<Vector3> continentSeeds = GenerateDeterministicSeeds(numberOfContinents, seed ^ 0xD00D);
        Debug.Log($"Generated {continentSeeds.Count} deterministic continent seeds.");

        // ---------- 3. Find Noise Peak within each Mask ------------------------
        Dictionary<int, bool> isLandTile = new Dictionary<int, bool>();
        Dictionary<int, Vector2> tileLatLon = new Dictionary<int, Vector2>();
        Dictionary<Vector3, (int peakTileIndex, float peakNoiseValue)> seedPeaks =
            new Dictionary<Vector3, (int peakTileIndex, float peakNoiseValue)>();
        Dictionary<int, float> tileNoiseCache = new Dictionary<int, float>(); // Cache noise values

        // Pre-calculate LatLon for all tiles
        for (int i = 0; i < tileCount; i++) {
            Vector3 center = grid.tileCenters[i];
            float latitude = Mathf.Asin(center.normalized.y) * Mathf.Rad2Deg;
            float longitude = Mathf.Atan2(center.normalized.x, center.normalized.z) * Mathf.Rad2Deg;
            tileLatLon[i] = new Vector2(latitude, longitude);
        }

        int processedSeeds = 0;
        // Find the highest noise point within each seed's mask
        foreach (Vector3 seedPos in continentSeeds) {
            int currentPeakIndex = -1;
            float currentPeakValue = -1f; // Noise is 0-1, so -1 is safe starting point

            for (int i = 0; i < tileCount; i++) {
                Vector3 tilePos = grid.tileCenters[i].normalized;
                if (IsTileInMask(tilePos, tileLatLon[i], seedPos, maxContinentWidthDegrees, maxContinentHeightDegrees)) {
                    float noiseValue;
                    if (!tileNoiseCache.TryGetValue(i, out noiseValue)) {
                        Vector3 noisePos = tilePos + noiseOffset;
                        noiseValue = noise.GetContinent(noisePos * continentNoiseFrequency);
                        tileNoiseCache[i] = noiseValue;
                    }

                    if (noiseValue > currentPeakValue) {
                        currentPeakValue = noiseValue;
                        currentPeakIndex = i;
                    }
                }
            }
            // Store the result for this seed (only if a peak was found within the mask)
            if (currentPeakIndex != -1) {
                seedPeaks[seedPos] = (currentPeakIndex, currentPeakValue);
                // Debug.Log($"Seed at {seedPos} found peak tile {currentPeakIndex} with noise {currentPeakValue}");
            } else {
                Debug.LogWarning($"Seed at {seedPos} had no tiles within its mask or mask was empty of noise peaks.");
            }

            // YIELD after processing each seed
            processedSeeds++;
            if (loadingPanelController != null)
            {
                loadingPanelController.SetProgress(0.05f + (float)processedSeeds / numberOfContinents * 0.05f); // Progress from 5% to 10%
                loadingPanelController.SetStatus($"Analyzing continent {processedSeeds}/{numberOfContinents}...");
            }
            yield return null;
        }

        // ---------- 4. Generate Land: Guarantee Peaks + Fill Threshold ---------
        for (int i = 0; i < tileCount; i++) {
            isLandTile[i] = false; // Default to water
            Vector3 tilePos = grid.tileCenters[i].normalized;

            foreach (Vector3 seedPos in continentSeeds) {
                // Skip seeds that didn't find a valid peak/mask
                if (!seedPeaks.ContainsKey(seedPos)) continue;

                (int peakIndex, float peakValue) = seedPeaks[seedPos];

                // Check if tile is within the mask for this seed
                if (IsTileInMask(tilePos, tileLatLon[i], seedPos, maxContinentWidthDegrees, maxContinentHeightDegrees)) {

                    // A) Guarantee the peak tile is land
                    if (i == peakIndex) {
                        isLandTile[i] = true;
                        landTilesGenerated++;
                        break; // Tile is land, move to next tile
                    }

                    // B) Check noise threshold for other tiles within the mask
                    float noiseValue = tileNoiseCache.ContainsKey(i) ? tileNoiseCache[i] : -1f; // Get cached or default
                    if (noiseValue == -1f) { // Recalculate if not cached (should be rare)
                        Vector3 noisePos = tilePos + noiseOffset;
                        noiseValue = noise.GetContinent(noisePos * continentNoiseFrequency);
                    }

                    if (noiseValue > landThreshold) {
                        isLandTile[i] = true;
                        landTilesGenerated++;
                        break; // Tile is land, move to next tile
                    }
                }
            }

            // BATCH YIELD
            if (i > 0 && i % 500 == 0)
            {
                if (loadingPanelController != null)
                {
                    loadingPanelController.SetProgress(0.1f + (float)i / tileCount * 0.1f); // Progress 10% to 20%
                    loadingPanelController.SetStatus("Raising continents...");
                }
                yield return null;
            }
        }

        // ---------- 4.5. Generate Islands (NEW) ---------
        if (GameSetupData.generateIslands && GameSetupData.numberOfIslands > 0) {
            yield return GenerateIslands(isLandTile, tileLatLon, tileNoiseCache, tileCount);
        }

        // ---------- 4.6. Generate Polar Landmasses (NEW) ---------
        this.landTilesGenerated += GeneratePolarLandmasses(isLandTile, tileLatLon, tileNoiseCache, tileCount);

        Debug.Log($"Generated {landTilesGenerated} land tiles. Method: Deterministic Seeds + Mask + Guaranteed Core + Islands. ({(float)landTilesGenerated / tileCount * 100:F1}% surface coverage)");

        // ---------- 5. Calculate Biomes, Elevation, and Initial Data ---------
        for (int i = 0; i < tileCount; i++)
        {
            Vector3 n = grid.tileCenters[i].normalized;
            bool isLand = isLandTile[i];
            Vector3 noisePoint = n + noiseOffset;
            
            // Calculate raw noise elevation (0-1 range)
            float noiseElevation = noise.GetElevation(noisePoint * elevationFreq);

            // Calculate Moisture & Temperature (needed for biome determination)
            float moisture = noise.GetMoisture(noisePoint * moistureFreq);
            // Apply moisture bias - clamp to ensure it stays in 0-1 range
            moisture = Mathf.Clamp01(moisture + moistureBias);
            
            Vector2 latLon = tileLatLon[i];
            float absLatitude = Mathf.Abs(latLon.x) / 90f;
            
            // Generate temperature from noise instead of latitude for non-polar areas
            float temperature;
            if (absLatitude >= polarLatitudeThreshold) {
                // For polar areas, keep latitude-based temperature (cold)
                temperature = noise.GetTemperature(absLatitude);
            } else {
                // For non-polar areas, blend latitude-based temperature with noise-based temperature
                float latitudeTemp = 1f - Mathf.Pow(absLatitude / polarLatitudeThreshold, 0.7f); // Scaled to be 0 at polarThreshold, 1 at equator
                float noiseTemp = noise.GetTemperatureFromNoise(noisePoint);
                
                // Blend: e.g., 70% latitude influence, 30% noise influence
                temperature = (latitudeTemp * 0.7f) + (noiseTemp * 0.3f);
            }
            temperature = Mathf.Clamp01(temperature + temperatureBias);

            Biome biome;
            bool isHill = false;
            float finalElevation; // Variable to store the final elevation

            if (isLand) {
                // Calculate land elevation: base + scaled noise
                float noiseScale = Mathf.Max(0f, maxTotalElevation - baseLandElevation); // Ensure scale isn't negative
                finalElevation = baseLandElevation + (noiseElevation * noiseScale); // Scale noise contribution
                

                float coastDist = 1.0f; 


                biome = GetBiomeForTile(i, true, temperature, moisture, coastDist);

                // Override polar land areas with frozen biomes
                if (absLatitude >= polarLatitudeThreshold) {
                    // Use latitude distance from threshold to determine how "polar" it is
                    float polarIntensity = (absLatitude - polarLatitudeThreshold) / (1f - polarLatitudeThreshold);
                    
                    if (polarIntensity > 0.7f) {
                        // Most extreme polar regions: Arctic (coldest of all)
                        biome = Biome.Arctic;
                    } else if (moisture > 0.3f) {
                        // Less extreme but wet polar: Snow
                        biome = Biome.Snow;
                    } else {
                        // Less extreme and dry polar: Frozen
                        biome = Biome.Frozen;
                    }
                }

                if (finalElevation > mountainThreshold) { 
                    biome = Biome.Mountain;
                } else if (finalElevation > hillThreshold) { 
                    isHill = true; // isHill is separate from biome type, can coexist unless biome is Mountain
                }
            } else { // Water Biomes
                 finalElevation = 0f; // Water elevation is 0
                 // Let BiomeHelper determine water biomes (Ocean, Seas, Coast, Glacier)
                 // Pass coastDistance = 0f for water tiles as they are the water body itself.
                 if (absLatitude >= polarLatitudeThreshold) {
                    biome = Biome.Glacier;
                 } else {
                     biome = Biome.Ocean;
                }
            }

            // *** Override for Glaciers: Treat them like land for elevation ***
            if (biome == Biome.Glacier) {
                float noiseScale = Mathf.Max(0f, maxTotalElevation - baseLandElevation); // Ensure scale isn't negative
                finalElevation = baseLandElevation + (noiseElevation * noiseScale); // Recalculate elevation for glaciers with scaled noise
            }

            // Optional: Re-introduce cap as a safety net if base/max can be set freely
            finalElevation = Mathf.Min(finalElevation, maxTotalElevation);

            // Store the final calculated elevation
            tileElevation[i] = finalElevation; 

            // Create HexTileData
            var y = BiomeHelper.Yields(biome);
            int moveCost = BiomeHelper.GetMovementCost(biome);
            var td = new HexTileData { 
                biome = biome,
                food = y.food, production = y.prod, gold = y.gold, science = y.sci, culture = y.cult,
                occupantId = 0, 
                isLand = isLand, // Use the original isLand status (false for glaciers)
                isHill = isHill, // Assign hill status
                elevation = finalElevation, // Store calculated elevation
                temperature = temperature, // Store calculated temperature
                moisture = moisture, // Store calculated moisture
                movementCost = moveCost,
                isPassable = true, // Set to true by default, other systems can change this
                isMoonTile = false
            };
            data[i] = td;
            baseData[i] = td; // Store base state

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
            if (data.ContainsKey(i) && data[i].biome == Biome.Coast) {
                tileElevation[i] = coastElevation; // Fixed elevation for coasts 
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

        // ---------- 6.5 River Generation Pass (MOVED TO AFTER COAST/SEAS) ----
        if (enableRivers) {
            yield return GenerateRivers(isLandTile, data);
        }

        // Final Visual Update Pass - No longer setting tile colors directly
        int batchSize = 200;
        int batchCounter = 0;
        for (int i = 0; i < tileCount; i++) {
            if (!data.ContainsKey(i)) continue;
            if (lookup.TryGetValue(data[i].biome, out var bs))
            {
                // Add Decorations
                if (bs?.decorations != null && bs.decorations.Length > 0 && UnityEngine.Random.value < bs.spawnChance) {
                    GameObject prefab = bs.decorations[UnityEngine.Random.Range(0, bs.decorations.Length)];
                    if (prefab != null) {
                        var go = GetPooledObject(prefab);
                        // Adjust altitude based on tile extrusion
                        float elev = GetTileElevation(i);
                        float altitude = data[i].isLand ? elev * maxExtrusionHeight + 0.005f : 0.005f;
                        Vector3 center = grid.tileCenters[i];
                        Vector3 normal = center.normalized;
                        go.transform.SetParent(transform, true);
                        go.transform.localPosition = center + normal * altitude;
                        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.AngleAxis(UnityEngine.Random.Range(0,360), Vector3.up);
                        activeDecorations.Add(go);
                    }
                }
            }

            batchCounter++;
            if (batchCounter >= batchSize) {
                batchCounter = 0;
                if (loadingPanelController != null)
                {
                    loadingPanelController.SetProgress(0.95f + (float)i / tileCount * 0.05f); // 95% to 100%
                    loadingPanelController.SetStatus("Placing decorations...");
                }
                yield return null;
            }
        }

        // --------------------------- River Generation ----------------------------
        IEnumerator GenerateRivers(Dictionary<int, bool> isLandTile, Dictionary<int, HexTileData> tileData)
        {
            Debug.Log("Generating Rivers...");
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
            Debug.Log($"Attempting to generate up to {targetRiverCount} rivers.");

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
                List<int> path = FindPathGreedy(startTileIndex, coastTilesList, tileData, riverTiles);

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

            Debug.Log($"Successfully generated {actualRiverCount} rivers using pathfinding. (Attempts: {totalAttempts})");
        }

        // --------------------------- Helper Functions ----------------------------

        IEnumerator GenerateIslands(Dictionary<int, bool> isLandTile, Dictionary<int, Vector2> tileLatLon, 
                           Dictionary<int, float> tileNoiseCache, int tileCount) {
            
            Debug.Log($"Generating {GameSetupData.numberOfIslands} islands...");
            
            // Generate island seed positions
            List<Vector3> islandSeeds = GenerateIslandSeeds(GameSetupData.numberOfIslands, seed ^ 0xF15);
            
            // Island parameters (smaller than continents)
            float islandWidthDegrees = maxContinentWidthDegrees * 0.6f;  // INCREASED: Was 0.3f, now much bigger
            float islandHeightDegrees = maxContinentHeightDegrees * 0.6f; // INCREASED: Was 0.3f, now much bigger
            float islandNoiseFrequency = continentNoiseFrequency * 1.5f; // Higher frequency for more detail
            float islandThreshold = landThreshold - 0.08f; // LOWERED: Was -0.05f, now even easier to become land
            
            int localIslandTilesGenerated = 0;
            
            // BATCH YIELD
            int islandCheckCounter = 0;
            // Find noise peaks within each island mask
            Dictionary<Vector3, (int peakTileIndex, float peakNoiseValue)> islandPeaks = 
                new Dictionary<Vector3, (int peakTileIndex, float peakNoiseValue)>();
            
            foreach (Vector3 seedPos in islandSeeds) {
                int currentPeakIndex = -1;
                float currentPeakValue = -1f;
                
                for (int i = 0; i < tileCount; i++) {
                    // Skip tiles that are already land (from continents)
                    if (isLandTile[i]) continue;
                    
                    Vector3 tilePos = grid.tileCenters[i].normalized;
                    if (IsTileInIslandMask(tilePos, tileLatLon[i], seedPos, islandWidthDegrees, islandHeightDegrees)) {
                        float noiseValue;
                        if (!tileNoiseCache.TryGetValue(i, out noiseValue)) {
                            Vector3 noisePos = tilePos + noiseOffset;
                            noiseValue = noise.GetContinent(noisePos * islandNoiseFrequency);
                            tileNoiseCache[i] = noiseValue;
                        }
                        
                        if (noiseValue > currentPeakValue) {
                            currentPeakValue = noiseValue;
                            currentPeakIndex = i;
                        }
                    }
                }
                
                if (currentPeakIndex != -1) {
                    islandPeaks[seedPos] = (currentPeakIndex, currentPeakValue);
                }
                
                // BATCH YIELD
                islandCheckCounter++;
                if (islandCheckCounter % 5 == 0)
                {
                    if (loadingPanelController != null)
                    {
                        loadingPanelController.SetProgress(0.2f + (float)islandCheckCounter / islandSeeds.Count * 0.05f); // 20% to 25%
                        loadingPanelController.SetStatus($"Finding island locations...");
                    }
                    yield return null;
                }
            }
            
            // Generate island land tiles
            for (int i = 0; i < tileCount; i++) {
                // Skip tiles that are already land
                if (isLandTile[i]) continue;
                
                Vector3 tilePos = grid.tileCenters[i].normalized;
                
                foreach (Vector3 seedPos in islandSeeds) {
                    if (!islandPeaks.ContainsKey(seedPos)) continue;
                    
                    (int peakIndex, float peakValue) = islandPeaks[seedPos];
                    
                    if (IsTileInIslandMask(tilePos, tileLatLon[i], seedPos, islandWidthDegrees, islandHeightDegrees)) {
                        
                        // Guarantee the peak tile is land
                        if (i == peakIndex) {
                            isLandTile[i] = true;
                            localIslandTilesGenerated++;
                            break;
                        }
                        
                        // Check noise threshold for other tiles
                        float noiseValue = tileNoiseCache.ContainsKey(i) ? tileNoiseCache[i] : -1f;
                        if (noiseValue == -1f) {
                            Vector3 noisePos = tilePos + noiseOffset;
                            noiseValue = noise.GetContinent(noisePos * islandNoiseFrequency);
                        }
                        
                        if (noiseValue > islandThreshold) {
                            isLandTile[i] = true;
                            localIslandTilesGenerated++;
                            break;
                        }
                    }
                }

                // BATCH YIELD
                if (i > 0 && i % 1000 == 0)
                {
                    if (loadingPanelController != null)
                    {
                        loadingPanelController.SetProgress(0.25f + (float)i / tileCount * 0.05f); // 25% to 30%
                        loadingPanelController.SetStatus("Raising islands...");
                    }
                    yield return null;
                }
            }
            
            Debug.Log($"Generated {localIslandTilesGenerated} island tiles from {islandSeeds.Count} island seeds.");
            this.landTilesGenerated += localIslandTilesGenerated;
        }
        
        /// <summary>
        /// Generates seed positions for islands using a more random approach than continents
        /// </summary>
        List<Vector3> GenerateIslandSeeds(int count, int rndSeed) {
            List<Vector3> seeds = new List<Vector3>();
            System.Random rand = new System.Random(rndSeed);
            
            // Islands use a more scattered, random placement
            int attempts = 0;
            int maxAttempts = count * 10; // Limit attempts to prevent infinite loops
            float minDistanceBetweenIslands = 20f; // Minimum degrees between island centers
            
            while (seeds.Count < count && attempts < maxAttempts) {
                attempts++;
                
                // Generate random position on sphere
                Vector3 candidate = UnityEngine.Random.insideUnitSphere.normalized;
                if (candidate == Vector3.zero) candidate = Vector3.up;
                
                // Check distance from existing seeds (both continents and islands)
                bool tooClose = false;
                
                // Check distance from continent seeds
                foreach (var continentSeed in GenerateDeterministicSeeds(numberOfContinents, seed ^ 0xD00D)) {
                    if (Vector3.Angle(candidate, continentSeed) < minDistanceBetweenIslands * 2f) {
                        tooClose = true;
                        break;
                    }
                }
                
                // Check distance from other island seeds
                if (!tooClose) {
                    foreach (var islandSeed in seeds) {
                        if (Vector3.Angle(candidate, islandSeed) < minDistanceBetweenIslands) {
                            tooClose = true;
                            break;
                        }
                    }
                }
                
                if (!tooClose) {
                    // Apply small random offset
                    float offsetRange = seedPositionVariance * 0.5f; // Smaller offset for islands
                    float offsetX = (float)(rand.NextDouble() * offsetRange * 2 - offsetRange);
                    float offsetY = (float)(rand.NextDouble() * offsetRange * 2 - offsetRange);
                    float offsetZ = (float)(rand.NextDouble() * offsetRange * 2 - offsetRange);
                    candidate = (candidate + new Vector3(offsetX, offsetY, offsetZ)).normalized;
                    
                    seeds.Add(candidate);
                }
            }
            
            if (seeds.Count < count) {
                Debug.LogWarning($"Could only generate {seeds.Count} island seeds out of requested {count} after {attempts} attempts.");
            }
            
            return seeds;
        }
        
        /// <summary>
        /// Check if a tile is within an island mask (smaller and more circular than continent masks)
        /// </summary>
        bool IsTileInIslandMask(Vector3 tilePosNormalized, Vector2 tileLatLon, Vector3 seedPosNormalized, 
                               float maxWidthDeg, float maxHeightDeg) {
            Vector2 seedLatLon = GetLatLonFromVector(seedPosNormalized);
            float latDiff = Mathf.Abs(tileLatLon.x - seedLatLon.x);
            float lonDiff = Mathf.DeltaAngle(tileLatLon.y, seedLatLon.y);
            float widthScale = Mathf.Cos(Mathf.Deg2Rad * tileLatLon.x);
            float scaledLonDiff = Mathf.Abs(lonDiff * widthScale);
            
            float latThreshold = maxHeightDeg / 2f;
            float lonThreshold = maxWidthDeg / 2f;
            
            // Islands use a more circular/elliptical shape
            float latFactor = latDiff / latThreshold;
            float lonFactor = scaledLonDiff / lonThreshold;
            
            // Elliptical distance check
            float ellipticalDistance = latFactor * latFactor + lonFactor * lonFactor;
            
            if (ellipticalDistance > 1.0f) return false;
            
            // Add some noise to the edge for more natural island shapes
            if (ellipticalDistance > 0.6f) {
                float edgeNoise = Mathf.PerlinNoise(
                    tilePosNormalized.x * 8f + tilePosNormalized.z * 6f,
                    tilePosNormalized.y * 7f + 0.3f
                );
                
                // Make the edge more jagged
                float noiseFactor = 0.3f + 0.4f * edgeNoise;
                if (ellipticalDistance > noiseFactor) {
                    return false;
                }
            }
            
            return true;
        }

        /// <summary>
        /// Generate landmasses specifically in polar regions to ensure solid polar land
        /// </summary>
        int GeneratePolarLandmasses(Dictionary<int, bool> isLandTile, Dictionary<int, Vector2> tileLatLon, 
                                   Dictionary<int, float> tileNoiseCache, int tileCount) {
            
            Debug.Log("Generating polar landmasses to ensure solid polar land...");
            
            int polarTilesGenerated = 0;
            float polarLandProbability = 0.4f; // 40% of polar tiles become land
            System.Random rand = new System.Random(seed ^ 0x5674);
            
            for (int i = 0; i < tileCount; i++) {
                // Skip tiles that are already land
                if (isLandTile[i]) continue;
                
                Vector2 latLon = tileLatLon[i];
                float absLatitude = Mathf.Abs(latLon.x) / 90f;
                
                // Only affect polar regions
                if (absLatitude >= polarLatitudeThreshold) {
                    // Use noise + probability to determine if this polar tile becomes land
                    Vector3 tilePos = grid.tileCenters[i].normalized;
                    float noiseValue;
                    
                    if (!tileNoiseCache.TryGetValue(i, out noiseValue)) {
                        Vector3 noisePos = tilePos + noiseOffset;
                        noiseValue = noise.GetContinent(noisePos * continentNoiseFrequency);
                        tileNoiseCache[i] = noiseValue;
                    }
                    
                    // Combine noise with probability and polar distance
                    float polarStrength = (absLatitude - polarLatitudeThreshold) / (1f - polarLatitudeThreshold);
                    float landChance = polarLandProbability + (noiseValue * 0.3f) + (polarStrength * 0.2f);
                    
                    if (rand.NextDouble() < landChance) {
                        isLandTile[i] = true;
                        polarTilesGenerated++;
                    }
                }
            }
            
            Debug.Log($"Generated {polarTilesGenerated} polar land tiles to ensure solid polar regions.");
            return polarTilesGenerated;
        }

        // Build the visual maps for the high-poly sphere
        BuildVisualMaps();
        // --- NEW: Sync tile grid and maps with GameManager ---
        if (GameManager.Instance != null)
        {
            Debug.Log("[PlanetGenerator] Syncing tile grid and maps with GameManager...");
            yield return GameManager.Instance.GenerateTileGridAndMaps();
        }
        else
        {
            Debug.LogError("[PlanetGenerator] GameManager.Instance is null! Cannot sync tile grid.");
        }

        yield return StartCoroutine(BuildVisualMapsBatched());
    }

    // Call this once after the planet is generated ─ e.g. at the END of GenerateSurface()
    void BuildVisualMaps()
    {
        StartCoroutine(BuildVisualMapsBatched());
    }

    // Coroutine version with batching and progress bar
    System.Collections.IEnumerator BuildVisualMapsBatched(int batchSize = 16)
    {
        if (landscape == null)
        {
            Debug.LogError($"{name}: Landscape component not assigned!");
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

        // --- Heightmap: Output as Alpha8 (single channel), not RGB ---
        if (heightTex == null || heightTex.width != textureSize)
        {
            heightTex = new Texture2D(textureSize, textureSize / 2, TextureFormat.Alpha8, false)
            { wrapMode = TextureWrapMode.Repeat };
        }
        Color32[] hPixels = new Color32[heightTex.width * heightTex.height];
        float minH = float.MaxValue, maxH = float.MinValue;
        float planetRadius = landscape != null ? (float)landscape.Radius : 1.0f;
        float heightScale = heightFractionOfRadius * planetRadius;
        
        Debug.Log($"[PlanetGenerator] Heightmap generation: planetRadius={planetRadius}, heightScale={heightScale}");
        
        // --- MERGED: Height & Biome Mask generation in single pass ---
        int biomeCount = biomeSettings.Count;
        int texSize = 1024; // Reduced for faster generation - Unity auto-scales source textures
        
        // Create both albedo and normal texture arrays
        var albedoArray = new Texture2DArray(texSize, texSize, biomeCount, TextureFormat.RGBA32, true);
        var normalArray = new Texture2DArray(texSize, texSize, biomeCount, TextureFormat.RGBA32, true);
        
        for (int i = 0; i < biomeCount; i++) {
            // Set up albedo textures
            var albedo = biomeSettings[i].albedoTexture;
            if (albedo != null) albedoArray.SetPixels(albedo.GetPixels(), i);
            else albedoArray.SetPixels(new Color[texSize * texSize], i);
            
            // Set up normal textures (no conversion, use Unity's normal map format directly)
            var normal = biomeSettings[i].normalTexture;
            if (normal != null) {
                normalArray.SetPixels(normal.GetPixels(), i);
            } else {
                // Create flat normal map (0.5, 0.5, 1.0, 1.0 = no bump)
                Color[] flatNormal = new Color[texSize * texSize];
                for (int j = 0; j < flatNormal.Length; j++) {
                    flatNormal[j] = new Color(0.5f, 0.5f, 1f, 1f); // Flat normal in tangent space
                }
                normalArray.SetPixels(flatNormal, i);
            }
        }
        albedoArray.Apply(true); // Generate mipmaps
        normalArray.Apply(true);

        // Pre-build biome index lookup for faster access
        Dictionary<Biome, int> biomeToIndex = new Dictionary<Biome, int>();
        for (int i = 0; i < biomeSettings.Count; i++) {
            biomeToIndex[biomeSettings[i].biome] = i;
        }

        // --- Generate Biome Mask Textures (Choose Method Based on Quality Setting) ---
        List<Texture2D> biomeMaskTextures;
        List<Texture2D> packedBiomeMasks;
        Texture2D biomeIndexMap = null; // Declare at higher scope
        
        switch (biomeMaskQuality)
        {
            case BiomeMaskQuality.Optimized:
                packedBiomeMasks = GenerateOptimizedBiomeMasks(w, h);
                // Generate individual masks for compatibility
                biomeMaskTextures = GenerateBiomeMaskTextures(w, h, pixelToTileLookup);
                break;
            case BiomeMaskQuality.Blended:
                packedBiomeMasks = GenerateBlendedBiomeMasks(w, h, biomeBlendRadius);
                // Generate individual masks for compatibility
                biomeMaskTextures = GenerateBiomeMaskTextures(w, h, pixelToTileLookup);
                break;
            default: // Standard
                // Use the original method for standard quality
                biomeMaskTextures = new List<Texture2D>();
                packedBiomeMasks = new List<Texture2D>();
                List<Color[]> biomeMaskPixels = new List<Color[]>();
                List<Color[]> packedMaskPixels = new List<Color[]>();
                
                // Initialize mask textures for each biome
                for (int i = 0; i < biomeCount; i++) {
                    var maskTex = new Texture2D(w, h, TextureFormat.Alpha8, false, true);
                    maskTex.wrapMode = TextureWrapMode.Repeat;
                    biomeMaskTextures.Add(maskTex);
                    biomeMaskPixels.Add(new Color[w * h]);
                }

                // --- RGBA-packed Biome Mask Generation (up to 32 biomes in 8 textures) ---
                int packedMaskCount = Mathf.CeilToInt(biomeCount / 4f);
                for (int i = 0; i < packedMaskCount; i++) {
                    var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
                    tex.wrapMode = TextureWrapMode.Repeat;
                    packedBiomeMasks.Add(tex);
                    packedMaskPixels.Add(new Color[w * h]);
                }

                // Generate biome index map (R channel = biome index normalized 0-1) - keep for compatibility
                biomeIndexMap = new Texture2D(w, h, TextureFormat.RFloat, false, true);
                Color[] biomePixels = new Color[w * h]; // Pre-allocate for batch SetPixels

                // Single pass for height, biome index, and packed biome masks
                for (int y = 0; y < h; y++)
                {
                    float v = ((y + 0.5f) / h);
                    float lat = Mathf.Lerp(90, -90, v); // Fixed: removed -180f
                    for (int x = 0; x < w; x++)
                    {
                        int tileIdx = pixelToTileLookup[x, y]; // USE LOOKUP
                        var tile = GetHexTileData(tileIdx);
                        if (tile == null)
                        {
                            Debug.LogWarning($"[PlanetGenerator] Null tile data for tileIdx {tileIdx} at pixel ({x},{y})");
                            continue;
                        }
                        // HEIGHT MAP PROCESSING (unchanged)
                        float h01 = Mathf.InverseLerp(baseLandElevation, maxTotalElevation, tile.elevation);
                        if (h01 < minH) minH = h01;
                        if (h01 > maxH) maxH = h01;
                        int idx1d = y * w + x;
                        float scaledHeight = h01 * heightScale; // This is now in world units
                        byte heightByte = (byte)Mathf.RoundToInt(Mathf.Clamp(scaledHeight * 255f / heightScale, 0f, 255f));
                        hPixels[idx1d] = new Color32(0, 0, 0, heightByte);
                        // BIOME INDEX MAP PROCESSING (keep for compatibility)
                        int biomeIdx = biomeToIndex.ContainsKey(tile.biome) ? biomeToIndex[tile.biome] : 0;
                        float biomeNorm = biomeCount > 1 ? (float)biomeIdx / (biomeCount - 1) : 0f;
                        biomePixels[idx1d] = new Color(biomeNorm, 0, 0, 1);
                        
                        // INDIVIDUAL BIOME MASK PROCESSING (for individual mask textures)
                        for (int i = 0; i < biomeCount; i++) {
                            float maskValue = (i == biomeIdx) ? 1f : 0f;
                            biomeMaskPixels[i][idx1d] = new Color(maskValue, maskValue, maskValue, maskValue);
                        }
                        
                        // RGBA-packed BIOME MASK PROCESSING (optimized)
                        if (biomeIdx < biomeCount) {
                            int texIdx = biomeIdx / 4;
                            int channel = biomeIdx % 4;
                            
                            // Ensure we don't exceed our texture count
                            if (texIdx < packedMaskCount) {
                                Color col = packedMaskPixels[texIdx][idx1d];
                                float maskValue = 1f; // This pixel belongs to this biome
                                switch (channel)
                                {
                                    case 0: col.r = maskValue; break;
                                    case 1: col.g = maskValue; break;
                                    case 2: col.b = maskValue; break;
                                    case 3: col.a = maskValue; break;
                                }
                                packedMaskPixels[texIdx][idx1d] = col;
                            }
                        }
                    }
                    // Yield every 8 rows to keep UI responsive
                    if (y % 8 == 0)
                    {
                        float progress = (float)y / h;
                        if (loadingPanelController != null)
                        {
                            loadingPanelController.SetProgress(0.1f + progress * 0.4f); // 10% to 50%
                            loadingPanelController.SetStatus($"Building Height & Biome Maps... ({(progress*100):F0}%)");
                        }
                        yield return null;
                    }
                }
                
                // Apply individual biome mask pixels to each texture
                for (int i = 0; i < biomeCount; i++)
                {
                    biomeMaskTextures[i].SetPixels(biomeMaskPixels[i]);
                    biomeMaskTextures[i].Apply(false, true);
                }
                
                // Apply packed mask pixels to each packed mask texture
                for (int i = 0; i < packedMaskCount; i++)
                {
                    packedBiomeMasks[i].SetPixels(packedMaskPixels[i]);
                    packedBiomeMasks[i].Apply(false, true);
                }
                
                // Apply biome index map for Standard quality
                if (biomeIndexMap != null)
                {
                    biomeIndexMap.SetPixels(biomePixels);
                    biomeIndexMap.Apply(false, true);
                }
                break;
        }

        // Generate biome index map (only if not already generated in standard mode)
        Texture2D finalBiomeIndexMap = null;
        if (biomeMaskQuality != BiomeMaskQuality.Standard)
        {
            finalBiomeIndexMap = new Texture2D(w, h, TextureFormat.RFloat, false, true);
            Color[] biomePixels = new Color[w * h];
            
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int tileIdx = pixelToTileLookup[x, y]; // USE LOOKUP
                    var tile = GetHexTileData(tileIdx);
                    if (tile == null) continue;
                    
                    int biomeIdx = biomeToIndex.ContainsKey(tile.biome) ? biomeToIndex[tile.biome] : 0;
                    float biomeNorm = biomeCount > 1 ? (float)biomeIdx / (biomeCount - 1) : 0f;
                    int idx1d = y * w + x;
                    biomePixels[idx1d] = new Color(biomeNorm, 0, 0, 1);
                }
            }
            
            finalBiomeIndexMap.SetPixels(biomePixels);
            finalBiomeIndexMap.Apply(false, true);
        }

        Debug.Log($"[PlanetGenerator] Heightmap h01 min: {minH}, max: {maxH}, heightScale: {heightScale}");
        
        // Remap if all values are too close - but only once!
        if (maxH - minH < 0.1f)
        {
            Debug.LogWarning($"[PlanetGenerator] Heightmap range too small ({minH}-{maxH}). Remapping to full 0-1 range.");
            for (int i = 0; i < hPixels.Length; i++)
            {
                float orig = hPixels[i].a / 255f;
                float remapped = Mathf.InverseLerp(minH, maxH, orig * heightScale / 255f);
                hPixels[i].a = (byte)Mathf.RoundToInt(remapped * 255f);
            }
        }
        heightTex.SetPixels32(hPixels); 
        heightTex.Apply(false, false);
        
        // Create a simple color map using biome colors
        biomeColorMap = GenerateBiomeColorMap(w, h);

        // Assign textures to the landscape
        landscape.HeightTex = heightTex;
        landscape.AlbedoTex = biomeColorMap;
        landscape.HeightMidpoint = 0.5f;
        landscape.HeightRange = 10f;

        // Assign texture arrays and masks to the landscape material
        var landscapeMaterial = landscape.Material;
        if (landscapeMaterial != null)
        {
            // Set biome texture arrays
            landscapeMaterial.SetFloat("_UseBiomeBlending", 0.0f); // Disable blending for simpler debugging
            landscapeMaterial.SetTexture("_BiomeAlbedoArray", albedoArray);
            landscapeMaterial.SetTexture("_BiomeNormalArray", normalArray);
            landscapeMaterial.SetFloat("_BiomeAlbedoArray_Depth", biomeCount);
            landscapeMaterial.SetFloat("_BiomeNormalArray_Depth", biomeCount);
            // Set the biome index map for shader lookup
            landscapeMaterial.SetTexture("_BiomeIndexMap", finalBiomeIndexMap ?? biomeIndexMap);
            // Assign packed RGBA biome mask textures
            for (int i = 0; i < packedBiomeMasks.Count; i++)
            {
                landscapeMaterial.SetTexture($"_BiomeMask{i}", packedBiomeMasks[i]);
            }
            landscapeMaterial.SetFloat("_BiomeMaskCount", packedBiomeMasks.Count);
            Debug.Log($"[PlanetGenerator] Assigned {biomeCount} biomes in {packedBiomeMasks.Count} RGBA mask textures to landscape material.");
        }
        else
        {
            Debug.LogWarning("[PlanetGenerator] Could not find landscape material to assign biome textures.");
        }

        // Force SGT to recognize the new textures and update the mesh
        if (landscape != null) landscape.MarkForRebuild();

        // Create SGT biome components programmatically - DISABLED as it conflicts with SGT's fixed array sizes
        // CreateSGTBiomeComponents(biomeMaskTextures);

        // After visuals are prepared, generate the biome index texture used by the shader
        if (BiomeTextureManager.Instance != null)
        {
            BiomeTextureManager.Instance.GenerateBiomeIndexTexture(grid);
        }

        if (loadingPanelController != null)
        {
            loadingPanelController.SetProgress(1f);
            loadingPanelController.SetStatus("Finishing up...");
        }

        // --- BIOME BLENDING: Generate blend weight and index maps for 4-way blending ---
        // Reduce resolution for faster generation
        const int blendTexSize = 512;
        Texture2D biomeBlendWeightMap = new Texture2D(blendTexSize, blendTexSize, TextureFormat.RGBA32, false);
        Texture2D biomeBlendIndexMap  = new Texture2D(blendTexSize, blendTexSize, TextureFormat.RGBA32, false);

        // --- NEW: Pre-computation for blend map lookup ---
        int[,] blendPixelToTileLookup = new int[blendTexSize, blendTexSize];
        if (loadingPanelController != null) {
            loadingPanelController.SetStatus("Building blend map lookup...");
            loadingPanelController.SetProgress(0.5f); // Start blend lookup at 50%
        }
        for (int y = 0; y < blendTexSize; y++) {
            float v = (float)y / (blendTexSize - 1);
            float latitude = v * 180f - 90f;
            for (int x = 0; x < blendTexSize; x++) {
                float u = (float)x / (blendTexSize - 1);
                float longitude = u * 360f - 180f;
                Vector3 dir = SphericalToCartesian(latitude, longitude);
                int tileIdx = grid.GetTileAtPosition(dir);
                if (tileIdx < 0) tileIdx = 0;
                blendPixelToTileLookup[x, y] = tileIdx;
            }
            if (y % 32 == 0) { // Update less frequently
                if (loadingPanelController != null) {
                    loadingPanelController.SetProgress(0.5f + ((float)y / blendTexSize) * 0.1f); // 50% to 60%
                }
                yield return null;
            }
        }
        // --- END Pre-computation ---

        // Helper to gather candidate tiles: start tile + 2 neighbour rings (<= 19 tiles)
        List<int> GetCandidateTiles(int start)
        {
            HashSet<int> set = new HashSet<int>();
            List<int> frontier = new List<int> { start };
            set.Add(start);
            const int layers = 2;
            for (int l = 0; l < layers; l++)
            {
                List<int> next = new List<int>();
                foreach (int idx in frontier)
                {
                    foreach (int nb in grid.neighbors[idx])
                    {
                        if (set.Add(nb)) next.Add(nb);
                    }
                }
                frontier = next;
            }
            return new List<int>(set);
        }

        // For each pixel, find up to 4 nearest biomes from candidate tiles
        for (int y = 0; y < blendTexSize; y++)
        {
            float v = (float)y / (blendTexSize - 1);
            float latitude = v * 180f - 90f;
            for (int x = 0; x < blendTexSize; x++)
            {
                float u = (float)x / (blendTexSize - 1);
                float longitude = u * 360f - 180f;
                Vector3 dir = SphericalToCartesian(latitude, longitude);

                int baseTile = blendPixelToTileLookup[x, y]; // USE LOOKUP
                if (baseTile < 0) baseTile = 0;

                List<int> candidates = GetCandidateTiles(baseTile);

                // Compute distances only for candidates
                List<(int tileIdx, float dist)> nearest = new List<(int, float)>();
                foreach (int tIdx in candidates)
                {
                    Vector3 c   = TileDataHelper.Instance != null ? TileDataHelper.Instance.GetTileCenter(tIdx) : Vector3.zero;
                    float d      = (c - dir).sqrMagnitude;
                    nearest.Add((tIdx, d));
                }
                nearest.Sort((a, b) => a.dist.CompareTo(b.dist));

                int n = Mathf.Min(4, nearest.Count);
                float[] weights = new float[4];
                int[]   biomeIndices = new int[4];
                float totalInvDist = 0f;
                for (int i = 0; i < n; i++)
                {
                    float invDist = 1f / (nearest[i].dist + 1e-6f);
                    weights[i]    = invDist;
                    totalInvDist += invDist;
                    biomeIndices[i] = (int)data[nearest[i].tileIdx].biome;
                }
                for (int i = 0; i < n; i++) weights[i] /= totalInvDist;
                for (int i = n; i < 4; i++) { weights[i] = 0f; biomeIndices[i] = 0; }

                biomeBlendWeightMap.SetPixel(x, y, new Color(weights[0], weights[1], weights[2], weights[3]));
                biomeBlendIndexMap.SetPixel(x, y, new Color(biomeIndices[0]/255f, biomeIndices[1]/255f, biomeIndices[2]/255f, biomeIndices[3]/255f));
            }

            // Yield every 16 rows to keep UI responsive
            if (y % 16 == 0)
            {
                if (loadingPanelController != null)
                {
                    float progress = (float)y / blendTexSize;
                    loadingPanelController.SetProgress(0.6f + progress * 0.35f); // 60% to 95%
                    loadingPanelController.SetStatus($"Blending biomes... ({(progress * 100):F0}%)");
                }
                yield return null;
            }
        }

        // Removed SGT splatmap generation - using blend maps instead for better performance
        // Yield to ensure UI (loading panel) can update before finishing
        yield return null;

        // --- After all textures are generated, assign to PlanetForgeSphereInitializer and call Setup() ---
        var initializer = GetComponent<PlanetForgeSphereInitializer>();
        if (initializer != null)
        {
            initializer.heightTextures.Clear();
            initializer.heightTextures.Add(heightTex); // Add main height texture
            initializer.gradientTextures.Clear();
            initializer.gradientTextures.AddRange(GenerateGradientTextures()); // Add all generated gradients
            initializer.maskTextures.Clear();
            initializer.maskTextures.AddRange(biomeMaskTextures); // Add all biome mask textures
            initializer.Setup();
            Debug.Log("PlanetForgeSphereInitializer: Setup() called with generated textures.");
        }
    }

    // Public method to start the coroutine from outside
    public void StartBuildVisualMapsBatched(int batchSize = 16)
    {
        StartCoroutine(BuildVisualMapsBatched(batchSize));
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

    // Method to set the current map type name and update flags
    public void SetMapTypeName(string mapTypeName)
    {
        currentMapTypeName = mapTypeName;
        isRainforestMapType = mapTypeName.Contains("Rainforest");
        isScorchedMapType = mapTypeName.Contains("Scorched");
        isInfernalMapType = mapTypeName.Contains("Infernal");
        isDemonicMapType = mapTypeName.Contains("Demonic");
    }

    private Biome GetBiomeForTile(int tileIndex, bool isLand, float temperature, float moisture, float coastDistance)
    {
        // Use debug/override flags if enabled
        bool useRainforest = overrideMapTypeFlags ? debugIsRainforestMapType : isRainforestMapType;
        bool useScorched = overrideMapTypeFlags ? debugIsScorchedMapType : isScorchedMapType;
        bool useInfernal = overrideMapTypeFlags ? debugIsInfernalMapType : isInfernalMapType;
        bool useDemonic = overrideMapTypeFlags ? debugIsDemonicMapType : isDemonicMapType;
        

        return BiomeHelper.GetBiome(isLand, temperature, moisture, coastDistance, useRainforest, useScorched, useInfernal, useDemonic);
    }

    public void SetLoadingPanel(LoadingPanelController controller) { loadingPanelController = controller; }
    public LoadingPanelController GetLoadingPanel() => loadingPanelController;

    /// <summary>
    /// Generates a simple biome color map using the current biomeColors array.
    /// </summary>
    public Texture2D GenerateBiomeColorMap(int width = 1024, int height = 512)
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
                Color c = biomeColors[(int)tile.biome];
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        return tex;
    }

    [Header("SGT Heightmap Scaling")]
    [Tooltip("Maximum heightmap displacement as a fraction of planet radius (e.g. 0.02 = 2% of radius)")]
    public float heightFractionOfRadius = 0.02f;

    /// <summary>
    /// Procedurally generates a biome splatmap (area map) for SGT blending.
    /// </summary>
    public Texture2D GenerateBiomeSplatmap(int width = 512, int height = 256)
    {
        Texture2D splatmap = new Texture2D(width, height, TextureFormat.RGBA32, false);
        // Build a list of tile indices and their center positions
        var tileIndices = data.Keys.ToList();
        var tileCenters = new List<Vector3>(tileIndices.Count);
        var tileBiomes = new List<int>(tileIndices.Count);
        for (int i = 0; i < tileIndices.Count; i++)
        {
            int tileIndex = tileIndices[i];
            // Use TileDataHelper to get the center position
            Vector3 center = TileDataHelper.Instance != null ? TileDataHelper.Instance.GetTileCenter(tileIndex) : Vector3.zero;
            tileCenters.Add(center);
            tileBiomes.Add((int)data[tileIndex].biome);
        }
        int tileCount = tileCenters.Count;
        for (int y = 0; y < height; y++)
        {
            float v = (float)y / (height - 1);
            float latitude = v * 180f - 90f;
            for (int x = 0; x < width; x++)
            {
                float u = (float)x / (width - 1);
                float longitude = u * 360f - 180f;
                Vector3 dir = SphericalToCartesian(latitude, longitude);
                // Find the 4 nearest tiles (fast, brute-force for small tile counts)
                var nearest = new List<(int idx, float dist)>();
                for (int i = 0; i < tileCount; i++)
                {
                    float d = (tileCenters[i] - dir).sqrMagnitude;
                    nearest.Add((i, d));
                }
                nearest.Sort((a, b) => a.dist.CompareTo(b.dist));
                int n = Mathf.Min(4, nearest.Count);
                float[] weights = new float[4];
                int[] biomeIndices = new int[4];
                float totalInvDist = 0f;
                for (int i = 0; i < n; i++)
                {
                    float invDist = 1f / (nearest[i].dist + 1e-6f);
                    weights[i] = invDist;
                    totalInvDist += invDist;
                    biomeIndices[i] = tileBiomes[nearest[i].idx];
                }
                for (int i = 0; i < n; i++) weights[i] /= totalInvDist;
                for (int i = n; i < 4; i++) { weights[i] = 0f; biomeIndices[i] = 0; }
                // Store weights in RGBA
                splatmap.SetPixel(x, y, new Color(weights[0], weights[1], weights[2], weights[3]));
                // Optionally: store biome indices in a parallel texture if needed
            }
        }
        splatmap.Apply();
        return splatmap;
    }

    // Generate one grayscale mask texture per biome
    List<Texture2D> GenerateBiomeMaskTextures(int width, int height, int[,] pixelToTileLookup)
    {
        var list = new List<Texture2D>();
        foreach (Biome b in System.Enum.GetValues(typeof(Biome)))
        {
            var tex = new Texture2D(width, height, TextureFormat.Alpha8, false)
            { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int tile = pixelToTileLookup[x, y]; // USE LOOKUP
                    var td = GetHexTileData(tile);
                    byte val = td.biome == b ? (byte)255 : (byte)0;
                    tex.SetPixel(x, y, new Color32(val, val, val, 255));
                }
            }
            tex.Apply();
            list.Add(tex);
        }
        return list;
    }

    // Generate simple gradient textures from biomeColors
    List<Texture2D> GenerateGradientTextures()
    {
        var list = new List<Texture2D>();
        foreach (var col in biomeColors)
        {
            var tex = new Texture2D(1, 256, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 256; y++) tex.SetPixel(0, y, col);
            tex.Apply();
            list.Add(tex);
        }
        return list;
    }

    private List<int> FindPathGreedy(int startTileIndex, List<int> coastTiles, Dictionary<int, HexTileData> tileData, HashSet<int> existingRiverTiles) {
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
                if (!pathSet.Contains(neighborIndex) && !existingRiverTiles.Contains(neighborIndex) && tileData.ContainsKey(neighborIndex)) {
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
    /// Creates SGT biome components programmatically with the generated mask textures
    /// </summary>
    private void CreateSGTBiomeComponents(List<Texture2D> biomeMaskTextures)
    {
        if (landscape == null || biomeSettings.Count == 0)
        {
            Debug.LogWarning("[PlanetGenerator] Cannot create SGT biome components - landscape or biome settings missing.");
            return;
        }

        // Remove existing biome children first
        var existingBiomes = landscape.GetComponentsInChildren<SgtLandscapeBiome>();
        for (int i = 0; i < existingBiomes.Length; i++)
        {
            if (Application.isEditor)
                DestroyImmediate(existingBiomes[i].gameObject);
            else
                Destroy(existingBiomes[i].gameObject);
        }

        // Create new biome components
        for (int i = 0; i < biomeSettings.Count && i < biomeMaskTextures.Count; i++)
        {
            var biomeSetting = biomeSettings[i];
            // var maskTexture = biomeMaskTextures[i]; // Not used directly

            // Create biome GameObject
            GameObject biomeObj = new GameObject($"Biome_{biomeSetting.biome}");
            biomeObj.transform.SetParent(landscape.transform, false);

            // Add and configure SgtLandscapeBiome component
            var biomeComponent = biomeObj.AddComponent<SgtLandscapeBiome>();
            
            // Set up the biome component
            biomeComponent.Mask = true;
            biomeComponent.MaskIndex = i;
            // biomeComponent.MaskTexture = maskTexture; // REMOVE: Not supported
            // biomeComponent.MaskChannel = ...; // REMOVE: Not supported
            // biomeComponent.MaskChannelType = ...; // REMOVE: Not supported
            // If we have gradient textures, assign them by index
            biomeComponent.GradientIndex = i;
            // biomeComponent.GradientTexture = ...; // REMOVE: Not supported
            // Add a default layer
            var layer = new SgtLandscapeBiome.SgtLandscapeBiomeLayer
            {
                HeightIndex = 0,
                HeightRange = 10f,
                HeightMidpoint = 0.5f,
                GlobalSize = 100f
            };
            biomeComponent.Layers.Add(layer);
            biomeComponent.Space = SgtLandscapeBiome.SpaceType.Global;
            Debug.Log($"[PlanetGenerator] Created SGT biome component for {biomeSetting.biome}");
        }

        Debug.Log($"[PlanetGenerator] Created {biomeSettings.Count} SGT biome components with mask textures.");
    }

    /// <summary>
    /// Creates a simple gradient texture from a color
    /// </summary>
    private Texture2D CreateGradientTexture(Color color)
    {
        var tex = new Texture2D(1, 256, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp
        };
        
        for (int y = 0; y < 256; y++)
        {
            tex.SetPixel(0, y, color);
        }
        
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// Generates optimized biome mask textures with improved quality and performance
    /// </summary>
    private List<Texture2D> GenerateOptimizedBiomeMasks(int width, int height)
    {
        var biomeMasks = new List<Texture2D>();
        int biomeCount = biomeSettings.Count;
        
        // Create packed RGBA mask textures (4 biomes per texture)
        int packedMaskCount = Mathf.CeilToInt(biomeCount / 4f);
        
        for (int maskIndex = 0; maskIndex < packedMaskCount; maskIndex++)
        {
            var maskTexture = new Texture2D(width, height, TextureFormat.RGBA32, true, true) // Enable mipmaps and linear
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4 // Better quality for terrain textures
            };
            
            Color[] pixels = new Color[width * height];
            
            // Generate mask data
            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;
                float lat = Mathf.Lerp(90, -90, v);
                
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    float lon = Mathf.Lerp(-180, 180, u);
                    Vector3 dir = SphericalToCartesian(lat, lon);
                    
                    int tileIdx = grid.GetTileAtPosition(dir);
                    if (tileIdx < 0) tileIdx = 0;
                    
                    var tile = GetHexTileData(tileIdx);
                    if (tile == null) continue;
                    
                    int biomeIdx = (int)tile.biome;
                    int pixelIndex = y * width + x;
                    
                    // Determine which biomes belong to this packed texture
                    Color maskColor = Color.black;
                    for (int channel = 0; channel < 4; channel++)
                    {
                        int targetBiomeIdx = maskIndex * 4 + channel;
                        if (targetBiomeIdx < biomeCount && biomeIdx == targetBiomeIdx)
                        {
                            float maskValue = 1f;
                            switch (channel)
                            {
                                case 0: maskColor.r = maskValue; break;
                                case 1: maskColor.g = maskValue; break;
                                case 2: maskColor.b = maskValue; break;
                                case 3: maskColor.a = maskValue; break;
                            }
                        }
                    }
                    
                    pixels[pixelIndex] = maskColor;
                }
            }
            
            maskTexture.SetPixels(pixels);
            maskTexture.Apply(true, false); // Generate mipmaps but don't make read-only yet
            biomeMasks.Add(maskTexture);
        }
        
        Debug.Log($"[PlanetGenerator] Generated {packedMaskCount} optimized RGBA biome mask textures for {biomeCount} biomes");
        return biomeMasks;
    }

    /// <summary>
    /// Generates smooth biome transition masks using distance-based blending
    /// </summary>
    private List<Texture2D> GenerateBlendedBiomeMasks(int width, int height, float blendRadius = 2f)
    {
        var biomeMasks = new List<Texture2D>();
        int biomeCount = biomeSettings.Count;
        int packedMaskCount = Mathf.CeilToInt(biomeCount / 4f);
        
        for (int maskIndex = 0; maskIndex < packedMaskCount; maskIndex++)
        {
            var maskTexture = new Texture2D(width, height, TextureFormat.RGBA32, true, true)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4
            };
            
            Color[] pixels = new Color[width * height];
            
            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;
                float lat = Mathf.Lerp(90, -90, v);
                
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    float lon = Mathf.Lerp(-180, 180, u);
                    Vector3 dir = SphericalToCartesian(lat, lon);
                    
                    Color maskColor = Color.black;
                    
                    // Sample multiple nearby tiles for smooth blending
                    Vector3[] sampleOffsets = {
                        Vector3.zero,
                        new Vector3(blendRadius / width, 0, 0),
                        new Vector3(-blendRadius / width, 0, 0),
                        new Vector3(0, blendRadius / height, 0),
                        new Vector3(0, -blendRadius / height, 0)
                    };
                    
                    float[] biomeWeights = new float[4];
                    float totalWeight = 0f;
                    
                    foreach (var offset in sampleOffsets)
                    {
                        Vector3 sampleDir = (dir + offset).normalized;
                        int tileIdx = grid.GetTileAtPosition(sampleDir);
                        if (tileIdx < 0) continue;
                        
                        var tile = GetHexTileData(tileIdx);
                        if (tile == null) continue;
                        
                        int biomeIdx = (int)tile.biome;
                        float weight = 1f / (1f + offset.magnitude * 10f); // Distance-based weight
                        
                        // Add weight to the appropriate channel
                        for (int channel = 0; channel < 4; channel++)
                        {
                            int targetBiomeIdx = maskIndex * 4 + channel;
                            if (targetBiomeIdx < biomeCount && biomeIdx == targetBiomeIdx)
                            {
                                biomeWeights[channel] += weight;
                                totalWeight += weight;
                            }
                        }
                    }
                    
                    // Normalize weights
                    if (totalWeight > 0f)
                    {
                        maskColor.r = biomeWeights[0] / totalWeight;
                        maskColor.g = biomeWeights[1] / totalWeight;
                        maskColor.b = biomeWeights[2] / totalWeight;
                        maskColor.a = biomeWeights[3] / totalWeight;
                    }
                    
                    pixels[y * width + x] = maskColor;
                }
            }
            
            maskTexture.SetPixels(pixels);
            maskTexture.Apply(true, false);
            biomeMasks.Add(maskTexture);
        }
        
        Debug.Log($"[PlanetGenerator] Generated {packedMaskCount} blended RGBA biome mask textures");
        return biomeMasks;
    }

    // --- Helper methods moved to class scope ---
    private List<Vector3> GenerateDeterministicSeeds(int count, int rndSeed) {
        List<Vector3> seeds = new List<Vector3>();
        System.Random rand = new System.Random(rndSeed);
        System.Func<Vector3, Vector3> addOffset = (Vector3 v) => {
            float range = seedPositionVariance * 2f;
            float offsetX = (float)(rand.NextDouble() * range - seedPositionVariance);
            float offsetY = (float)(rand.NextDouble() * range - seedPositionVariance);
            float offsetZ = (float)(rand.NextDouble() * range - seedPositionVariance);
            return (v + new Vector3(offsetX, offsetY, offsetZ)).normalized;
        };
        if (count <= 0) return seeds;
        Vector3 northPole = Vector3.up;
        Vector3 southPole = Vector3.down;
        Vector3 equatorFwd = Vector3.forward;
        Vector3 equatorBack = Vector3.back;
        Vector3 equatorRight = Vector3.right;
        Vector3 equatorLeft = Vector3.left;
        seeds.Add(addOffset(northPole));
        if (count == 1) return seeds;
        seeds.Add(addOffset(southPole));
        if (count == 2) return seeds;
        if (count >= 3) seeds.Add(addOffset(equatorFwd));
        if (count >= 4) seeds.Add(addOffset(equatorRight));
        if (count >= 5) seeds.Add(addOffset(equatorBack));
        if (count >= 6) seeds.Add(addOffset(equatorLeft));
        if (count == 6) return seeds;
        if (count >= 7) seeds.Add(addOffset(new Vector3(1, 1, 1).normalized));
        if (count >= 8) seeds.Add(addOffset(new Vector3(-1, -1, -1).normalized));
        if (count == 8) return seeds;
        Debug.LogWarning($"Deterministic placement only defined up to 8 seeds. Adding remaining {count - seeds.Count} randomly.");
        int guard = 0;
        int maxTries = 5000;
        float minAngle = 30f;
        while (seeds.Count < count && guard < maxTries) {
            guard++;
            Vector3 candidate = UnityEngine.Random.insideUnitSphere.normalized;
            if (candidate == Vector3.zero) candidate = Vector3.up;
            bool ok = true;
            foreach (var s in seeds) {
                if (Vector3.Angle(candidate, s) < minAngle) { ok = false; break; }
            }
            if (ok) seeds.Add(candidate);
        }
        return seeds;
    }

    private bool IsTileInMask(Vector3 tilePosNormalized, Vector2 tileLatLon, Vector3 seedPosNormalized, float maxWidthDeg, float maxHeightDeg) {
        Vector2 seedLatLon = GetLatLonFromVector(seedPosNormalized);
        float latDiff = Mathf.Abs(tileLatLon.x - seedLatLon.x);
        float lonDiff = Mathf.DeltaAngle(tileLatLon.y, seedLatLon.y);
        float widthScale = Mathf.Cos(Mathf.Deg2Rad * tileLatLon.x);
        float scaledLonDiff = Mathf.Abs(lonDiff * widthScale);
        float latThreshold = maxHeightDeg / 2f;
        float lonThreshold = maxWidthDeg / 2f;
        if (latDiff > latThreshold * 1.5f || scaledLonDiff > lonThreshold * 1.5f) {
            return false;
        }
        if (latDiff > latThreshold * 0.7f || scaledLonDiff > lonThreshold * 0.7f) {
            float latFactor = Mathf.InverseLerp(latThreshold * 0.7f, latThreshold, latDiff);
            float lonFactor = Mathf.InverseLerp(lonThreshold * 0.7f, lonThreshold, scaledLonDiff);
            float edgeFactor = Mathf.Max(latFactor, lonFactor);
            float edgeNoise = Mathf.PerlinNoise(
                tilePosNormalized.x * 3.7f + tilePosNormalized.z * 2.3f,
                tilePosNormalized.y * 4.1f + 0.5f
            );
            float ellipseFactor = (latFactor * latFactor + lonFactor * lonFactor) / 2f;
            float finalFactor = edgeFactor * (0.7f + 0.3f * ellipseFactor);
            if (edgeNoise < finalFactor) {
                return false;
            }
        }
        return true;
    }

    private Vector2 GetLatLonFromVector(Vector3 v) {
        float latitude = Mathf.Asin(v.y) * Mathf.Rad2Deg;
        float longitude = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
        return new Vector2(latitude, longitude);
    }
}


