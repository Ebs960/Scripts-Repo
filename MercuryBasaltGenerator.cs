using UnityEngine;

/// <summary>
/// Terrain generator for Mercury's basaltic plains (smooth maria)
/// Creates flat volcanic plains with scattered small craters and subtle volcanic features
/// Similar to lunar mare but older and more weathered by solar wind
/// </summary>
public class MercuryBasaltGenerator : IBiomeTerrainGenerator
{
    private BiomeNoiseProfile noiseProfile;
    private BattleTerrainNoiseSystem noiseSystem;
    private BiomeTerrainSettings terrainSettings;
    
    // Basalt plain characteristics
    private float baseLevel = 0.25f;           // Relatively flat
    private float smallCraterDensity = 0.3f;   // Some small craters
    private float smallCraterMaxSize = 8f;     // Small craters only
    private float wrinkleRidgeDensity = 0.4f;  // Volcanic wrinkle ridges
    
    public MercuryBasaltGenerator()
    {
        noiseProfile = new BiomeNoiseProfile
        {
            baseHeight = 0.2f,
            noiseScale = 0.03f,
            roughness = 0.2f,
            hilliness = 0.15f,
            mountainSharpness = 0.1f,
            octaves = 3,
            lacunarity = 2.0f,
            persistence = 0.4f,
            hillThreshold = 0.4f,
            mountainThreshold = 0.7f,
            maxHeightVariation = 4f,
            useErosion = false,
            erosionStrength = 0f
        };
        
        terrainSettings = new BiomeTerrainSettings
        {
            baseElevation = 0.25f,
            heightScale = 0.4f,
            baseFrequency = 0.015f,
            detailFrequency = 0.05f,
            ridgeFrequency = 0.03f,
            valleyFrequency = 0.02f,
            baseWeight = 0.6f,
            detailWeight = 0.2f,
            ridgeWeight = 0.1f,
            valleyWeight = 0.1f,
            ridgeSharpness = 1.2f,
            useDomainWarping = false,
            domainWarpStrength = 10f
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
        
        // Generate base flat plain with gentle undulations
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (y / (float)resolution) * mapSize;
                
                float height = noiseSystem.GetTerrainHeight(worldX, worldZ, terrainSettings);
                
                // Flatten toward base level (basalt plains are relatively flat)
                height = Mathf.Lerp(baseLevel, height, 0.4f);
                
                heights[y, x] = height;
            }
        }
        
        // Add wrinkle ridges (volcanic compression features)
        if (Random.value < wrinkleRidgeDensity)
        {
            AddWrinkleRidges(heights, resolution, mapSize, seed);
        }
        
        // Add scattered small craters (not as dense as highlands)
        AddSmallCraters(heights, resolution, mapSize, seed);
        
        // Add subtle surface texture
        AddSurfaceTexture(heights, resolution, mapSize);
        
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
    /// Add volcanic wrinkle ridges - gentle, linear ridges from crustal compression
    /// </summary>
    private void AddWrinkleRidges(float[,] heights, int resolution, float mapSize, int seed)
    {
        FastNoiseLite ridgeNoise = new FastNoiseLite(seed + 7000);
        ridgeNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        ridgeNoise.SetFractalType(FastNoiseLite.FractalType.Ridged);
        ridgeNoise.SetFractalOctaves(2);
        ridgeNoise.SetFrequency(0.015f);
        
        // Direction of ridges (roughly parallel)
        float ridgeAngle = Random.Range(0f, Mathf.PI);
        
        float ridgeHeight = 0.04f;
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                // Rotate coordinates for directional ridges
                float rotX = worldX * Mathf.Cos(ridgeAngle) - worldZ * Mathf.Sin(ridgeAngle);
                float rotZ = worldX * Mathf.Sin(ridgeAngle) + worldZ * Mathf.Cos(ridgeAngle);
                
                float ridgeValue = ridgeNoise.GetNoise(rotX * 0.5f, rotZ);
                ridgeValue = (ridgeValue + 1f) * 0.5f;
                
                // Only add ridges where value is high (creates linear features)
                if (ridgeValue > 0.6f)
                {
                    float ridgeStrength = (ridgeValue - 0.6f) / 0.4f;
                    heights[z, x] += ridgeHeight * ridgeStrength;
                }
            }
        }
    }
    
    /// <summary>
    /// Add scattered small craters (basalt plains have fewer, smaller craters)
    /// </summary>
    private void AddSmallCraters(float[,] heights, int resolution, float mapSize, int seed)
    {
        int craterCount = Mathf.RoundToInt(smallCraterDensity * mapSize * 0.2f);
        
        Random.InitState(seed + 7100);
        
        for (int c = 0; c < craterCount; c++)
        {
            float craterX = Random.Range(0f, mapSize);
            float craterZ = Random.Range(0f, mapSize);
            
            // Small craters only
            float radius = Random.Range(2f, smallCraterMaxSize);
            float depth = 0.03f * (radius / smallCraterMaxSize);
            float rimHeight = 0.015f * (radius / smallCraterMaxSize);
            
            ApplySmallCrater(heights, resolution, mapSize, craterX, craterZ, radius, depth, rimHeight);
        }
        
        Random.InitState(System.Environment.TickCount);
    }
    
    private void ApplySmallCrater(float[,] heights, int resolution, float mapSize,
                                  float centerX, float centerZ, float radius, float depth, float rimHeight)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt((centerX - radius * 1.3f) / mapSize * resolution));
        int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt((centerX + radius * 1.3f) / mapSize * resolution));
        int minZ = Mathf.Max(0, Mathf.FloorToInt((centerZ - radius * 1.3f) / mapSize * resolution));
        int maxZ = Mathf.Min(resolution - 1, Mathf.CeilToInt((centerZ + radius * 1.3f) / mapSize * resolution));
        
        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                float dist = Mathf.Sqrt((worldX - centerX) * (worldX - centerX) +
                                       (worldZ - centerZ) * (worldZ - centerZ));
                
                if (dist > radius * 1.2f) continue;
                
                float normalizedDist = dist / radius;
                float craterEffect = 0f;
                
                if (normalizedDist < 0.85f)
                {
                    // Simple bowl shape
                    float bowlProfile = Mathf.Pow(normalizedDist / 0.85f, 0.8f);
                    craterEffect = -depth * (1f - bowlProfile);
                }
                else if (normalizedDist < 1.1f)
                {
                    // Subtle rim
                    float rimProfile = 1f - Mathf.Abs(normalizedDist - 0.95f) / 0.15f;
                    craterEffect = rimHeight * Mathf.Max(0, rimProfile);
                }
                
                heights[z, x] += craterEffect;
            }
        }
    }
    
    /// <summary>
    /// Add subtle surface texture (space weathering)
    /// </summary>
    private void AddSurfaceTexture(float[,] heights, int resolution, float mapSize)
    {
        FastNoiseLite textureNoise = new FastNoiseLite(Random.Range(1, 100000));
        textureNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        textureNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        textureNoise.SetFractalOctaves(3);
        textureNoise.SetFrequency(0.12f);
        
        float textureAmount = 0.01f;
        
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (z / (float)resolution) * mapSize;
                
                float texture = textureNoise.GetNoise(worldX, worldZ);
                heights[z, x] += texture * textureAmount;
            }
        }
    }
    
    public BiomeNoiseProfile GetNoiseProfile()
    {
        return noiseProfile;
    }
}

