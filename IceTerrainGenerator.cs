using UnityEngine;

/// <summary>
/// Terrain generator for Ice/Snow/Tundra biomes
/// Creates smooth ice sheets with crevasses and pressure ridges
/// </summary>
public class IceTerrainGenerator : IBiomeTerrainGenerator
{
    private BiomeNoiseProfile noiseProfile;
    private BattleTerrainNoiseSystem noiseSystem;
    private BiomeTerrainSettings terrainSettings;
    
    public IceTerrainGenerator()
    {
        noiseProfile = new BiomeNoiseProfile
        {
            baseHeight = 0.25f,
            noiseScale = 0.06f,
            roughness = 0.15f,
            hilliness = 0.3f,
            mountainSharpness = 0.1f,
            octaves = 3,
            lacunarity = 2f,
            persistence = 0.35f,
            hillThreshold = 0.25f,
            mountainThreshold = 0.5f,
            maxHeightVariation = 4f,
            useErosion = true,
            erosionStrength = 0.5f
        };
        
        // Use advanced ice terrain settings
        terrainSettings = BiomeTerrainSettings.CreateIce();
    }
    
    public void Generate(Terrain terrain, float elevation, float moisture, float temperature, float mapSize)
    {
        if (terrain == null || terrain.terrainData == null) return;
        
        TerrainData terrainData = terrain.terrainData;
        int resolution = terrainData.heightmapResolution;
        float[,] heights = new float[resolution, resolution];
        
        // Initialize noise system
        noiseSystem = new BattleTerrainNoiseSystem(Random.Range(1, 100000));
        
        // Adjust for temperature (colder = smoother ice sheets)
        terrainSettings.baseElevation = elevation * 0.3f;
        terrainSettings.detailWeight = 0.1f + temperature * 0.1f; // Less detail for colder
        terrainSettings.ridgeWeight = 0.15f + (1f - temperature) * 0.1f; // More ridges for pressure
        
        // Generate heightmap
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (y / (float)resolution) * mapSize;
                
                // Get base terrain height
                float height = noiseSystem.GetTerrainHeight(worldX, worldZ, terrainSettings);
                
                // Add ice-specific features
                float iceFeatures = AddIceFeatures(worldX, worldZ, temperature);
                height += iceFeatures;
                
                heights[y, x] = Mathf.Clamp01(height);
            }
        }
        
        // Apply ice smoothing (glacial polish)
        ApplyGlacialSmoothing(heights, resolution, temperature);
        
        terrainData.SetHeights(0, 0, heights);
    }
    
    /// <summary>
    /// Add ice-specific features: crevasses, pressure ridges, and ice formations
    /// </summary>
    private float AddIceFeatures(float x, float z, float temperature)
    {
        float features = 0f;
        
        // Pressure ridges (linear features from ice compression)
        float ridgeAngle = 0.5f; // Direction of compression
        float ridgeX = x * Mathf.Cos(ridgeAngle) + z * Mathf.Sin(ridgeAngle);
        float ridge = Mathf.Abs(Mathf.Sin(ridgeX * 0.08f));
        ridge = Mathf.Pow(ridge, 3f); // Sharpen ridges
        features += ridge * 0.08f;
        
        // Ice hummocks (random bumps)
        float hummock = noiseSystem.GetFeatureMask(x, z, 1.5f);
        if (hummock > 0.75f)
        {
            features += (hummock - 0.75f) * 0.2f;
        }
        
        // Crevasses (cracks) - only in warmer ice
        if (temperature > 0.2f)
        {
            float crevasse = noiseSystem.GetBiomeMask(x * 2f, z * 2f, 2f);
            if (crevasse < 0.15f)
            {
                features -= (0.15f - crevasse) * 0.1f;
            }
        }
        
        return features;
    }
    
    /// <summary>
    /// Apply glacial smoothing (ice polish effect)
    /// </summary>
    private void ApplyGlacialSmoothing(float[,] heights, int resolution, float temperature)
    {
        // Colder = more smoothing passes (harder ice)
        int passes = 2 + Mathf.RoundToInt((1f - temperature) * 3f);
        float strength = 0.4f + (1f - temperature) * 0.3f;
        
        for (int pass = 0; pass < passes; pass++)
        {
            for (int y = 1; y < resolution - 1; y++)
            {
                for (int x = 1; x < resolution - 1; x++)
                {
                    // Gaussian-weighted average for smooth ice
                    float sum = heights[y, x] * 4f;
                    sum += heights[y - 1, x] * 2f + heights[y + 1, x] * 2f;
                    sum += heights[y, x - 1] * 2f + heights[y, x + 1] * 2f;
                    sum += heights[y - 1, x - 1] + heights[y - 1, x + 1];
                    sum += heights[y + 1, x - 1] + heights[y + 1, x + 1];
                    
                    float avg = sum / 16f;
                    heights[y, x] = Mathf.Lerp(heights[y, x], avg, strength);
                }
            }
        }
    }
    
    public BiomeNoiseProfile GetNoiseProfile()
    {
        return noiseProfile;
    }
}

