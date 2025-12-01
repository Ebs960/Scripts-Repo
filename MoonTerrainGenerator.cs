using UnityEngine;

/// <summary>
/// Terrain generator for Moon/Mercury/Pluto and gas giant moons
/// Creates cratered, airless world terrain with impact craters, mare (flat plains), and regolith
/// </summary>
public class MoonTerrainGenerator : IBiomeTerrainGenerator
{
    private BiomeNoiseProfile noiseProfile;
    private BattleTerrainNoiseSystem noiseSystem;
    private BiomeTerrainSettings terrainSettings;
    
    // Crater generation settings
    private float craterDensity = 0.5f;      // How many craters
    private float craterSizeMin = 5f;        // Minimum crater radius
    private float craterSizeMax = 25f;       // Maximum crater radius
    private float craterDepth = 0.15f;       // How deep craters are
    private float craterRimHeight = 0.08f;   // Height of crater rim
    private float mareChance = 0.3f;         // Chance of flat mare regions
    
    public MoonTerrainGenerator()
    {
        noiseProfile = new BiomeNoiseProfile
        {
            baseHeight = 0.25f,
            noiseScale = 0.04f,
            roughness = 0.3f,
            hilliness = 0.4f,
            mountainSharpness = 0.2f,
            octaves = 4,
            lacunarity = 2.2f,
            persistence = 0.45f,
            hillThreshold = 0.3f,
            mountainThreshold = 0.6f,
            maxHeightVariation = 6f,
            useErosion = false, // No erosion on airless worlds!
            erosionStrength = 0f
        };
        
        // Create moon-specific terrain settings using factory method
        terrainSettings = BiomeTerrainSettings.CreateMoon();
    }
    
    public void Generate(Terrain terrain, float elevation, float moisture, float temperature, float mapSize)
    {
        if (terrain == null || terrain.terrainData == null) return;
        
        TerrainData terrainData = terrain.terrainData;
        int resolution = terrainData.heightmapResolution;
        float[,] heights = new float[resolution, resolution];
        
        // Initialize noise system with random seed
        int seed = Random.Range(1, 100000);
        noiseSystem = new BattleTerrainNoiseSystem(seed);
        
        // Adjust settings based on elevation (higher = more mountainous highlands)
        terrainSettings.baseElevation = elevation * 0.35f;
        terrainSettings.ridgeWeight = 0.1f + elevation * 0.15f;
        
        // Temperature affects crater preservation (colder = sharper rims)
        float craterSharpness = 1f + (1f - temperature) * 0.5f;
        
        // Generate base terrain
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (y / (float)resolution) * mapSize;
                
                // Get base terrain from noise system
                float height = noiseSystem.GetTerrainHeight(worldX, worldZ, terrainSettings);
                
                heights[y, x] = height;
            }
        }
        
        // Generate impact craters
        GenerateCraters(heights, resolution, mapSize, seed, craterSharpness);
        
        // Generate mare (flat regions) if this is a body that has them
        if (mareChance > 0 && Random.value < mareChance)
        {
            GenerateMare(heights, resolution, mapSize, seed);
        }
        
        // Add regolith (dusty surface detail)
        AddRegolithDetail(heights, resolution, mapSize);
        
        // Clamp final heights
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                heights[y, x] = Mathf.Clamp01(heights[y, x]);
            }
        }
        
        terrainData.SetHeights(0, 0, heights);
    }
    
    /// <summary>
    /// Generate impact craters across the terrain
    /// </summary>
    private void GenerateCraters(float[,] heights, int resolution, float mapSize, int seed, float sharpness)
    {
        // Determine number of craters based on density
        int craterCount = Mathf.RoundToInt(craterDensity * mapSize * 0.5f);
        
        // Use seeded random for consistent crater placement
        Random.InitState(seed + 1000);
        
        for (int c = 0; c < craterCount; c++)
        {
            // Random crater position
            float craterX = Random.Range(0f, mapSize);
            float craterZ = Random.Range(0f, mapSize);
            
            // Random crater size (biased towards smaller craters - more common)
            float sizeT = Mathf.Pow(Random.value, 2f); // Squared for small bias
            float craterRadius = Mathf.Lerp(craterSizeMin, craterSizeMax, sizeT);
            
            // Larger craters are deeper
            float depth = craterDepth * (0.5f + sizeT * 0.5f);
            float rimHeight = craterRimHeight * (0.5f + sizeT * 0.5f);
            
            // Apply crater to heightmap
            ApplyCrater(heights, resolution, mapSize, craterX, craterZ, craterRadius, depth, rimHeight, sharpness);
        }
        
        // Reset random state
        Random.InitState(System.Environment.TickCount);
    }
    
    /// <summary>
    /// Apply a single impact crater to the heightmap
    /// </summary>
    private void ApplyCrater(float[,] heights, int resolution, float mapSize, 
                            float centerX, float centerZ, float radius, 
                            float depth, float rimHeight, float sharpness)
    {
        // Calculate affected area in heightmap coordinates
        int minX = Mathf.Max(0, Mathf.FloorToInt((centerX - radius * 1.5f) / mapSize * resolution));
        int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt((centerX + radius * 1.5f) / mapSize * resolution));
        int minZ = Mathf.Max(0, Mathf.FloorToInt((centerZ - radius * 1.5f) / mapSize * resolution));
        int maxZ = Mathf.Min(resolution - 1, Mathf.CeilToInt((centerZ + radius * 1.5f) / mapSize * resolution));
        
        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                // Distance from crater center
                float dist = Mathf.Sqrt((worldX - centerX) * (worldX - centerX) + 
                                       (worldZ - centerZ) * (worldZ - centerZ));
                
                if (dist > radius * 1.5f) continue;
                
                float normalizedDist = dist / radius;
                float craterEffect = 0f;
                
                if (normalizedDist < 0.8f)
                {
                    // Inside crater - depression
                    // Crater floor profile: deeper in center, rises toward rim
                    float floorProfile = Mathf.Pow(normalizedDist / 0.8f, 0.7f);
                    craterEffect = -depth * (1f - floorProfile);
                    
                    // Central peak for larger craters
                    if (radius > craterSizeMax * 0.6f && normalizedDist < 0.15f)
                    {
                        float peakProfile = 1f - (normalizedDist / 0.15f);
                        craterEffect += depth * 0.3f * peakProfile;
                    }
                }
                else if (normalizedDist < 1.0f)
                {
                    // Crater rim - raised edge
                    float rimProfile = 1f - Mathf.Abs(normalizedDist - 0.9f) / 0.1f;
                    rimProfile = Mathf.Pow(rimProfile, 1f / sharpness); // Sharper rims on cold worlds
                    craterEffect = rimHeight * rimProfile;
                }
                else if (normalizedDist < 1.3f)
                {
                    // Ejecta blanket - gradual slope away from rim
                    float ejectaProfile = 1f - (normalizedDist - 1f) / 0.3f;
                    craterEffect = rimHeight * 0.3f * ejectaProfile;
                }
                
                // Apply crater effect
                heights[z, x] += craterEffect;
            }
        }
    }
    
    /// <summary>
    /// Generate mare (flat volcanic plains) - common on Moon and Mercury
    /// </summary>
    private void GenerateMare(float[,] heights, int resolution, float mapSize, int seed)
    {
        // Use cellular noise to define mare regions
        FastNoiseLite mareNoise = new FastNoiseLite(seed + 2000);
        mareNoise.SetNoiseType(FastNoiseLite.NoiseType.Cellular);
        mareNoise.SetCellularDistanceFunction(FastNoiseLite.CellularDistanceFunction.EuclideanSq);
        mareNoise.SetCellularReturnType(FastNoiseLite.CellularReturnType.Distance);
        mareNoise.SetFrequency(0.008f);
        
        float mareLevel = 0.25f; // Height of mare floor
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                // Get mare mask
                float mareValue = mareNoise.GetNoise(worldX, worldZ);
                mareValue = (mareValue + 1f) * 0.5f;
                
                // Mare regions where cellular value is low
                if (mareValue < 0.3f)
                {
                    // Blend toward flat mare level
                    float mareStrength = 1f - (mareValue / 0.3f);
                    mareStrength = Mathf.SmoothStep(0f, 1f, mareStrength);
                    
                    // Flatten to mare level
                    heights[z, x] = Mathf.Lerp(heights[z, x], mareLevel, mareStrength * 0.8f);
                }
            }
        }
    }
    
    /// <summary>
    /// Add fine regolith (dust) detail to the surface
    /// </summary>
    private void AddRegolithDetail(float[,] heights, int resolution, float mapSize)
    {
        // High-frequency noise for dusty surface texture
        FastNoiseLite regolithNoise = new FastNoiseLite(Random.Range(1, 100000));
        regolithNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        regolithNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        regolithNoise.SetFractalOctaves(3);
        regolithNoise.SetFrequency(0.15f);
        
        float regolithAmount = 0.02f; // Very subtle surface dust
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                float dust = regolithNoise.GetNoise(worldX, worldZ);
                heights[z, x] += dust * regolithAmount;
            }
        }
    }
    
    /// <summary>
    /// Configure generator for specific moon/body type
    /// </summary>
    public void ConfigureForBody(MoonBodyType bodyType)
    {
        switch (bodyType)
        {
            case MoonBodyType.EarthMoon:
                craterDensity = 0.6f;
                craterSizeMin = 3f;
                craterSizeMax = 30f;
                craterDepth = 0.12f;
                mareChance = 0.4f; // Moon has significant mare
                break;
                
            case MoonBodyType.Mercury:
                craterDensity = 0.8f; // Very heavily cratered
                craterSizeMin = 2f;
                craterSizeMax = 35f;
                craterDepth = 0.15f;
                mareChance = 0.2f;
                break;
                
            case MoonBodyType.Pluto:
                craterDensity = 0.3f; // Fewer craters (young surface)
                craterSizeMin = 5f;
                craterSizeMax = 20f;
                craterDepth = 0.1f;
                mareChance = 0.5f; // Sputnik Planitia-like regions
                break;
                
            case MoonBodyType.Europa:
                craterDensity = 0.2f; // Very few craters (ice resurfacing)
                craterSizeMin = 5f;
                craterSizeMax = 15f;
                craterDepth = 0.08f;
                craterRimHeight = 0.05f;
                mareChance = 0f; // No mare, but has chaos terrain
                terrainSettings.ridgeWeight = 0.3f; // Ice ridges
                break;
                
            case MoonBodyType.Io:
                craterDensity = 0.05f; // Almost no craters (volcanic resurfacing)
                craterSizeMin = 3f;
                craterSizeMax = 10f;
                craterDepth = 0.05f;
                mareChance = 0f;
                terrainSettings.ridgeWeight = 0.4f; // Volcanic mountains
                terrainSettings.heightScale = 1.0f;
                break;
                
            case MoonBodyType.Titan:
                craterDensity = 0.15f; // Few craters (atmosphere + erosion)
                craterSizeMin = 8f;
                craterSizeMax = 25f;
                craterDepth = 0.08f;
                mareChance = 0.3f; // Hydrocarbon lakes
                terrainSettings.valleyWeight = 0.25f; // River valleys
                break;
                
            case MoonBodyType.Ganymede:
            case MoonBodyType.Callisto:
                craterDensity = 0.7f;
                craterSizeMin = 4f;
                craterSizeMax = 30f;
                craterDepth = 0.12f;
                mareChance = 0.1f;
                break;
                
            default:
                // Default lunar-like settings
                break;
        }
    }
    
    public BiomeNoiseProfile GetNoiseProfile()
    {
        return noiseProfile;
    }
}

/// <summary>
/// Types of moon/planetary bodies for terrain configuration
/// </summary>
public enum MoonBodyType
{
    EarthMoon,      // Earth's Moon - heavily cratered with mare
    Mercury,        // Mercury - heavily cratered, scarps
    Pluto,          // Pluto - nitrogen ice plains, few craters
    Europa,         // Europa - ice surface, ridges, few craters
    Io,             // Io - volcanic, almost no craters
    Titan,          // Titan - atmosphere, lakes, few craters
    Ganymede,       // Ganymede - cratered ice
    Callisto,       // Callisto - ancient, heavily cratered
    Generic         // Generic cratered body
}

