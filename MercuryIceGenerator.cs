using UnityEngine;

/// <summary>
/// Terrain generator for Mercury's polar ice deposits
/// Creates terrain with permanently shadowed crater floors where water ice persists
/// Despite being closest to the Sun, Mercury has ice in polar crater shadows
/// </summary>
public class MercuryIceGenerator : IBiomeTerrainGenerator
{
    private BiomeNoiseProfile noiseProfile;
    private BattleTerrainNoiseSystem noiseSystem;
    private BiomeTerrainSettings terrainSettings;
    
    // Ice crater characteristics
    private float deepCraterDensity = 0.5f;    // Deep craters that hold ice
    private float deepCraterDepth = 0.25f;     // Very deep to stay in shadow
    private float deepCraterMinSize = 10f;
    private float deepCraterMaxSize = 35f;
    private float iceFloorFlatness = 0.8f;     // Ice fills and flattens crater floors
    
    public MercuryIceGenerator()
    {
        noiseProfile = new BiomeNoiseProfile
        {
            baseHeight = 0.3f,
            noiseScale = 0.04f,
            roughness = 0.25f,
            hilliness = 0.35f,
            mountainSharpness = 0.2f,
            octaves = 4,
            lacunarity = 2.1f,
            persistence = 0.45f,
            hillThreshold = 0.35f,
            mountainThreshold = 0.65f,
            maxHeightVariation = 6f,
            useErosion = false,
            erosionStrength = 0f
        };
        
        terrainSettings = new BiomeTerrainSettings
        {
            baseElevation = 0.35f,
            heightScale = 0.65f,
            baseFrequency = 0.025f,
            detailFrequency = 0.07f,
            ridgeFrequency = 0.035f,
            valleyFrequency = 0.025f,
            baseWeight = 0.45f,
            detailWeight = 0.2f,
            ridgeWeight = 0.2f,
            valleyWeight = 0.15f,
            ridgeSharpness = 1.5f,
            useDomainWarping = true,
            domainWarpStrength = 20f
        };
    }
    
    public void Generate(Terrain terrain, float elevation, float moisture, float temperature, float mapSize)
    {
        if (terrain == null || terrain.terrainData == null) return;
        
        TerrainData terrainData = terrain.terrainData;
        int resolution = terrainData.heightmapResolution;
        float[,] heights = new float[resolution, resolution];
        
        int seed = Random.Range(1, 100000);
        noiseSystem = new BattleTerrainNoiseSystem(seed);
        
        // Generate base terrain
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (y / (float)resolution) * mapSize;
                
                float height = noiseSystem.GetTerrainHeight(worldX, worldZ, terrainSettings);
                heights[y, x] = height;
            }
        }
        
        // Generate deep craters that can hold ice
        GenerateDeepIceCraters(heights, resolution, mapSize, seed);
        
        // Add smaller surrounding craters
        GenerateSurroundingCraters(heights, resolution, mapSize, seed);
        
        // Add ice texture variations (sublimation patterns)
        AddIceTexture(heights, resolution, mapSize, seed);
        
        // Add regolith/dust detail on non-ice surfaces
        AddRegolithDetail(heights, resolution, mapSize);
        
        // Clamp heights
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
    /// Generate deep craters where ice accumulates in permanently shadowed floors
    /// </summary>
    private void GenerateDeepIceCraters(float[,] heights, int resolution, float mapSize, int seed)
    {
        int craterCount = Mathf.RoundToInt(deepCraterDensity * mapSize * 0.2f);
        
        Random.InitState(seed + 10000);
        
        for (int c = 0; c < craterCount; c++)
        {
            float craterX = Random.Range(mapSize * 0.1f, mapSize * 0.9f);
            float craterZ = Random.Range(mapSize * 0.1f, mapSize * 0.9f);
            
            float radius = Random.Range(deepCraterMinSize, deepCraterMaxSize);
            
            ApplyDeepIceCrater(heights, resolution, mapSize, craterX, craterZ, radius, seed + c);
        }
        
        Random.InitState(System.Environment.TickCount);
    }
    
    /// <summary>
    /// Apply a deep crater with ice-filled floor
    /// </summary>
    private void ApplyDeepIceCrater(float[,] heights, int resolution, float mapSize,
                                    float centerX, float centerZ, float radius, int localSeed)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt((centerX - radius * 1.5f) / mapSize * resolution));
        int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt((centerX + radius * 1.5f) / mapSize * resolution));
        int minZ = Mathf.Max(0, Mathf.FloorToInt((centerZ - radius * 1.5f) / mapSize * resolution));
        int maxZ = Mathf.Min(resolution - 1, Mathf.CeilToInt((centerZ + radius * 1.5f) / mapSize * resolution));
        
        // Ice noise for subtle ice surface variation
        FastNoiseLite iceNoise = new FastNoiseLite(localSeed);
        iceNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        iceNoise.SetFrequency(0.08f);
        
        float depth = deepCraterDepth;
        float rimHeight = 0.08f;
        float iceLevel = -depth * 0.6f; // Ice fills about 40% of crater depth
        
        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                float dist = Mathf.Sqrt((worldX - centerX) * (worldX - centerX) +
                                       (worldZ - centerZ) * (worldZ - centerZ));
                
                if (dist > radius * 1.4f) continue;
                
                float normalizedDist = dist / radius;
                float craterEffect = 0f;
                
                if (normalizedDist < 0.75f)
                {
                    // Deep crater bowl
                    float bowlProfile = Mathf.Pow(normalizedDist / 0.75f, 0.5f); // Steeper walls
                    float naturalFloor = -depth * (1f - bowlProfile);
                    
                    // Ice fills the deepest parts
                    if (normalizedDist < 0.5f)
                    {
                        // Flat ice floor with subtle variation
                        float iceVariation = iceNoise.GetNoise(worldX, worldZ) * 0.01f;
                        float iceFloor = iceLevel + iceVariation;
                        
                        // Blend between natural bowl and flat ice
                        float iceCoverage = 1f - (normalizedDist / 0.5f);
                        iceCoverage = Mathf.Pow(iceCoverage, 0.5f) * iceFloorFlatness;
                        
                        craterEffect = Mathf.Lerp(naturalFloor, iceFloor, iceCoverage);
                    }
                    else
                    {
                        // Crater walls (above ice level)
                        craterEffect = naturalFloor;
                    }
                }
                else if (normalizedDist < 1.0f)
                {
                    // Sharp crater rim (well-preserved in cold)
                    float rimProfile = 1f - Mathf.Abs(normalizedDist - 0.88f) / 0.12f;
                    rimProfile = Mathf.Pow(Mathf.Max(0, rimProfile), 0.5f);
                    craterEffect = rimHeight * rimProfile;
                }
                else if (normalizedDist < 1.3f)
                {
                    // Ejecta blanket
                    float ejectaProfile = 1f - (normalizedDist - 1f) / 0.3f;
                    craterEffect = rimHeight * 0.3f * ejectaProfile;
                }
                
                heights[z, x] += craterEffect;
            }
        }
    }
    
    /// <summary>
    /// Generate smaller surrounding craters (typical of polar regions)
    /// </summary>
    private void GenerateSurroundingCraters(float[,] heights, int resolution, float mapSize, int seed)
    {
        int craterCount = Mathf.RoundToInt(mapSize * 0.4f);
        
        Random.InitState(seed + 10100);
        
        for (int c = 0; c < craterCount; c++)
        {
            float craterX = Random.Range(0f, mapSize);
            float craterZ = Random.Range(0f, mapSize);
            
            float sizeT = Mathf.Pow(Random.value, 2f);
            float radius = Mathf.Lerp(1.5f, 10f, sizeT);
            float depth = 0.06f * (0.5f + sizeT * 0.5f);
            float rimHeight = 0.03f * (0.5f + sizeT * 0.5f);
            
            ApplySmallCrater(heights, resolution, mapSize, craterX, craterZ, radius, depth, rimHeight);
        }
        
        Random.InitState(System.Environment.TickCount);
    }
    
    private void ApplySmallCrater(float[,] heights, int resolution, float mapSize,
                                  float centerX, float centerZ, float radius, float depth, float rimHeight)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt((centerX - radius * 1.2f) / mapSize * resolution));
        int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt((centerX + radius * 1.2f) / mapSize * resolution));
        int minZ = Mathf.Max(0, Mathf.FloorToInt((centerZ - radius * 1.2f) / mapSize * resolution));
        int maxZ = Mathf.Min(resolution - 1, Mathf.CeilToInt((centerZ + radius * 1.2f) / mapSize * resolution));
        
        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                float dist = Mathf.Sqrt((worldX - centerX) * (worldX - centerX) +
                                       (worldZ - centerZ) * (worldZ - centerZ));
                
                if (dist > radius * 1.1f) continue;
                
                float normalizedDist = dist / radius;
                float craterEffect = 0f;
                
                if (normalizedDist < 0.85f)
                {
                    float bowlProfile = Mathf.Pow(normalizedDist / 0.85f, 0.7f);
                    craterEffect = -depth * (1f - bowlProfile);
                }
                else if (normalizedDist < 1.05f)
                {
                    float rimProfile = 1f - Mathf.Abs(normalizedDist - 0.92f) / 0.13f;
                    craterEffect = rimHeight * Mathf.Max(0, rimProfile);
                }
                
                heights[z, x] += craterEffect;
            }
        }
    }
    
    /// <summary>
    /// Add ice texture - sublimation patterns and ice fractures
    /// </summary>
    private void AddIceTexture(float[,] heights, int resolution, float mapSize, int seed)
    {
        FastNoiseLite icePatternNoise = new FastNoiseLite(seed + 10200);
        icePatternNoise.SetNoiseType(FastNoiseLite.NoiseType.Cellular);
        icePatternNoise.SetCellularDistanceFunction(FastNoiseLite.CellularDistanceFunction.EuclideanSq);
        icePatternNoise.SetCellularReturnType(FastNoiseLite.CellularReturnType.Distance2Add);
        icePatternNoise.SetFrequency(0.05f);
        
        FastNoiseLite crackNoise = new FastNoiseLite(seed + 10300);
        crackNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        crackNoise.SetFractalType(FastNoiseLite.FractalType.Ridged);
        crackNoise.SetFractalOctaves(2);
        crackNoise.SetFrequency(0.08f);
        
        float iceTextureAmount = 0.015f;
        float crackDepth = 0.008f;
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                // Ice polygon patterns (sublimation)
                float icePattern = icePatternNoise.GetNoise(worldX, worldZ);
                heights[z, x] += icePattern * iceTextureAmount * 0.5f;
                
                // Ice cracks
                float cracks = crackNoise.GetNoise(worldX, worldZ);
                if (cracks > 0.7f)
                {
                    float crackStrength = (cracks - 0.7f) / 0.3f;
                    heights[z, x] -= crackDepth * crackStrength;
                }
            }
        }
    }
    
    /// <summary>
    /// Add regolith detail for non-ice surfaces
    /// </summary>
    private void AddRegolithDetail(float[,] heights, int resolution, float mapSize)
    {
        FastNoiseLite regolithNoise = new FastNoiseLite(Random.Range(1, 100000));
        regolithNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        regolithNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        regolithNoise.SetFractalOctaves(3);
        regolithNoise.SetFrequency(0.15f);
        
        float regolithAmount = 0.01f;
        
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
    
    public BiomeNoiseProfile GetNoiseProfile()
    {
        return noiseProfile;
    }
}

