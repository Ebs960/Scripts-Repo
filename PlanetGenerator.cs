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
    [Tooltip("Minimum distance from tile center (as fraction of tile radius)")]
    public float minDistanceFromCenter;
    
    [Range(0.1f, 0.95f)]
    [Tooltip("Maximum distance from tile center (as fraction of tile radius)")]
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


    [Header("Sphere Settings")] 
    public int subdivisions = 8;
    public bool randomSeed = true;
    public int seed = 12345;
    public float radius = 21f; // Default radius, will be overridden by GameManager

    // Public property to access the seed
    public int Seed => seed;

    // --- New Continent Parameters (Method 2: Masked Noise + Guaranteed Core) ---
    [Header("Continent Generation (Deterministic Masked Noise)")]
    [Tooltip("The target number of continents. Placement is deterministic for common counts (1-8). Higher counts might revert to random spread.")]
    [Min(1)]
    public int numberOfContinents = 6;


    [Tooltip("Maximum longitudinal extent (width) of a continent mask in degrees.")]
    [Range(10f, 237.6f)]
    public float maxContinentWidthDegrees = 70f; 

    [Tooltip("Maximum latitudinal extent (height) of a continent mask in degrees.")]
    [Range(10f, 237.6f)]
    public float maxContinentHeightDegrees = 60f; 
    
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
    
    [Range(-2f, 1f)]
    [Tooltip("Elevation for ocean tiles.")]
    public float oceanElevation = 0f;
    [Range(-2f, 1f)]
    [Tooltip("Elevation for sea tiles.")]
    public float seasElevation = 0.05f;
    [Range(-2f, 1f)]
    [Tooltip("Elevation for coast tiles.")]
    public float coastElevation = 0.1f;
    
    [Range(0f, 0.5f)]
    [Tooltip("Additional elevation boost for hill tiles (added to their base elevation).")]
    public float hillElevationBoost = 0.05f;
    
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

    [Tooltip("Wait this many frames before initial generation so SphericalHexGrid has finished generating.")]
    public int initializationDelay = 1;

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
    private LoadingPanelController loadingPanelController;

    // OBSOLETE: Prefab loading removed - new system uses texture-based rendering


    // --------------------------- Unity lifecycle -----------------------------
    void Awake()
    {
        
        
        // Check if we're in multi-planet mode
        bool isMultiPlanet = GameManager.Instance?.enableMultiPlanetSystem == true;
        
        if (!isMultiPlanet)
        {
            // Single planet mode: Use traditional singleton pattern
            if (Instance == null)
            {
                Instance = this;
                
            }
            else if (Instance != this)
            {
                Debug.LogWarning($"[PlanetGenerator] Duplicate PlanetGenerator in single planet mode! Destroying {gameObject.name}");
                Destroy(gameObject);
                return;
            }
        }
        else
        {
            // Multi-planet mode: Only set instance if it's null, but don't destroy others
            if (Instance == null)
            {
                Instance = this;
                
            }
            else
            {
                
            }
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
            if (GameManager.Instance.enableMultiPlanetSystem)
                mgr = GameManager.Instance.GetClimateManager(planetIndex);
            else
                mgr = GameManager.Instance.GetClimateManager(0);
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
            if (GameManager.Instance.enableMultiPlanetSystem)
                mgr = GameManager.Instance.GetClimateManager(planetIndex);
            else
                mgr = GameManager.Instance.GetClimateManager(0);
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

        
        // ---------- 2. Generate Deterministic Continent Seeds ------------------
        List<Vector3> continentSeeds = GenerateDeterministicSeeds(numberOfContinents, seed ^ 0xD00D);

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
        if (allowIslands)
            yield return StartCoroutine(GenerateIslands(isLandTile, tileLatLon, tileNoiseCache, tileCount));

        // ---------- 5. Calculate Biomes, Elevation, and Initial Data ---------
        if (!allowOceans)
        {
            for (int i = 0; i < tileCount; i++) isLandTile[i] = true;
        }

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
            
            // Generate temperature from noise and latitude
            float latitudeTemp = 1f - Mathf.Pow(absLatitude, 0.7f); 
            float noiseTemp = noise.GetTemperatureFromNoise(noisePoint);
            
            // Blend: e.g., 70% latitude influence, 30% noise influence
            float temperature = (latitudeTemp * 0.7f) + (noiseTemp * 0.3f);
            temperature = Mathf.Clamp01(temperature + temperatureBias);

            Biome biome;
            bool isHill = false;
            float finalElevation; // Variable to store the final elevation

            if (isLand) {
                // Calculate land elevation: base + scaled noise
                float noiseScale = Mathf.Max(0f, maxTotalElevation - baseLandElevation); // Ensure scale isn't negative
                finalElevation = baseLandElevation + (noiseElevation * noiseScale); // Scale noise contribution
                

                biome = GetBiomeForTile(i, true, temperature, moisture);

                // REMOVED: Earth-specific polar override that was overriding planet-specific biome logic
                // BiomeHelper.GetBiome() now handles all planet-specific biome assignments correctly
                // This polar override was forcing Earth biomes on all planets regardless of type

                // Mountain/Hill check, but protect polar biomes from being overridden
                if (finalElevation > mountainThreshold) {
                    if (biome != Biome.Glacier && biome != Biome.Arctic && biome != Biome.Frozen && biome != Biome.Tundra && biome != Biome.Snow)
                    {
                        biome = Biome.Mountain;
                    }
                } else if (finalElevation > hillThreshold) { 
                    isHill = true; // isHill is separate from biome type, can coexist unless biome is Mountain
                }
            } else { // Water Biomes
                 finalElevation = 0f; // Water elevation is 0
                 // FIXED: Use BiomeHelper for water biomes too - no hardcoded polar glaciers
                 biome = GetBiomeForTile(i, false, temperature, moisture);
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
            ElevationTier elevTier = ElevationTier.Flat;
            if (finalElevation > mountainThreshold) elevTier = ElevationTier.Mountain;
            else if (finalElevation > hillThreshold) elevTier = ElevationTier.Hill;

            var td = new HexTileData {
                biome = biome,
                food = y.food, production = y.prod, gold = y.gold, science = y.sci, culture = y.cult,
                occupantId = 0,
                isLand = isLand, // Use the original isLand status (false for glaciers)
                isHill = isHill, // Assign hill status
                elevation = finalElevation, // Store calculated elevation
                elevationTier = elevTier,
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

        if (loadingPanelController != null)
        {
            loadingPanelController.SetProgress(0.95f);
            loadingPanelController.SetStatus("Finalizing terrain...");
        }
        yield return null;

        // --- Visual Generation ---
        // NOTE: Tile prefab spawning is disabled. The new system uses texture-based rendering.
        // FlatMapTextureRenderer and GlobeRenderer handle visualization.
        // Tile data is still generated and stored - only visualization changed.

        // Finalize
        HasGeneratedSurface = true;
        Tiles = data.Values.ToList();
        

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

        // --------------------------- Helper Functions ----------------------------

        IEnumerator GenerateIslands(Dictionary<int, bool> isLandTile, Dictionary<int, Vector2> tileLatLon, 
                           Dictionary<int, float> tileNoiseCache, int tileCount) {
            
            
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
            
            this.landTilesGenerated += localIslandTilesGenerated;
        }
    }
        
        /// <summary>
        /// Generates seed positions for islands using a more random approach than continents
        /// </summary>
        private List<Vector3> GenerateIslandSeeds(int count, int rndSeed) {
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
        // Calculate latitude and longitude for special biome rules
        float latitude = 0f;
        float longitude = 0f;
        if (grid != null && grid.IsBuilt && tileIndex < grid.tileCenters.Length)
        {
            Vector3 tileCenter = grid.tileCenters[tileIndex];
            // Convert to normalized latitude (-1 to +1, where ±1 are poles)
            latitude = tileCenter.y / radius;
            // Convert to longitude (-180 to +180 degrees)
            longitude = Mathf.Atan2(tileCenter.z, tileCenter.x) * Mathf.Rad2Deg;
        }
        
        Biome assignedBiome = BiomeHelper.GetBiome(
            isLand, temperature, moisture,
            isRainforestMapType, isScorchedMapType, isInfernalMapType, isDemonicMapType,
            isIceWorldMapType, isMonsoonMapType,
            isMarsWorldType, isVenusWorldType, isMercuryWorldType, isJupiterWorldType,
            isSaturnWorldType, isUranusWorldType, isNeptuneWorldType, isPlutoWorldType,
            isTitanWorldType, isEuropaWorldType, isIoWorldType, isGanymedeWorldType,
            isCallistoWorldType, isLunaWorldType, latitude, longitude
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
                    // Debug.LogWarning($"Deterministic placement only defined up to 8 seeds. Adding remaining {count - seeds.Count} randomly.");
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


