using UnityEngine;

/// <summary>
/// Terrain generator for Ocean/Coast/Seas biomes
/// Creates flat seabed with optional sandbars and underwater features
/// </summary>
public class OceanTerrainGenerator : IBiomeTerrainGenerator
{
    private BiomeNoiseProfile noiseProfile;
    private BattleTerrainNoiseSystem noiseSystem;
    private BiomeTerrainSettings terrainSettings;
    
    public OceanTerrainGenerator()
    {
        noiseProfile = new BiomeNoiseProfile
        {
            baseHeight = 0.1f,
            noiseScale = 0.02f,
            roughness = 0.02f,
            hilliness = 0.05f,
            mountainSharpness = 0.0f,
            octaves = 1,
            lacunarity = 2f,
            persistence = 0.2f,
            hillThreshold = 0.05f,
            mountainThreshold = 0.1f,
            maxHeightVariation = 1f,
            useErosion = true,
            erosionStrength = 0.9f
        };
        
        // Use advanced ocean terrain settings
        terrainSettings = BiomeTerrainSettings.CreateOcean();
    }
    
    public void Generate(Terrain terrain, float elevation, float moisture, float temperature, float mapSize)
    {
        if (terrain == null || terrain.terrainData == null) return;
        
        TerrainData terrainData = terrain.terrainData;
        int resolution = terrainData.heightmapResolution;
        float[,] heights = new float[resolution, resolution];
        
        // Initialize noise system
        noiseSystem = new BattleTerrainNoiseSystem(Random.Range(1, 100000));
        
        // Ocean is very flat
        terrainSettings.baseElevation = 0.15f;
        terrainSettings.heightScale = 0.2f;
        
        // Generate heightmap
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (y / (float)resolution) * mapSize;
                
                // Get base terrain (very flat)
                float height = noiseSystem.GetTerrainHeight(worldX, worldZ, terrainSettings);
                
                // Add underwater features (sandbars, gentle slopes)
                float underwaterFeature = AddUnderwaterFeatures(worldX, worldZ);
                height += underwaterFeature;
                
                heights[y, x] = Mathf.Clamp01(height);
            }
        }
        
        // Apply heavy smoothing for flat seabed
        ApplySeabedSmoothing(heights, resolution);
        
        terrainData.SetHeights(0, 0, heights);
    }
    
    /// <summary>
    /// Add underwater features like sandbars and gentle undulations
    /// </summary>
    private float AddUnderwaterFeatures(float x, float z)
    {
        float features = 0f;
        
        // Sandbars (linear features parallel to shore)
        float sandbar = Mathf.Sin(z * 0.03f) * 0.5f + 0.5f;
        sandbar = Mathf.Pow(sandbar, 4f); // Sharpen into ridges
        features += sandbar * 0.03f;
        
        // Gentle ripples
        float ripple = Mathf.PerlinNoise(x * 0.1f, z * 0.1f);
        features += (ripple - 0.5f) * 0.02f;
        
        return features;
    }
    
    /// <summary>
    /// Apply heavy smoothing for flat seabed
    /// </summary>
    private void ApplySeabedSmoothing(float[,] heights, int resolution)
    {
        // Very heavy smoothing for flat water
        for (int pass = 0; pass < 6; pass++)
        {
            for (int y = 1; y < resolution - 1; y++)
            {
                for (int x = 1; x < resolution - 1; x++)
                {
                    // Large kernel for very smooth results
                    float sum = heights[y, x] * 4f;
                    sum += heights[y - 1, x] * 2f + heights[y + 1, x] * 2f;
                    sum += heights[y, x - 1] * 2f + heights[y, x + 1] * 2f;
                    sum += heights[y - 1, x - 1] + heights[y - 1, x + 1];
                    sum += heights[y + 1, x - 1] + heights[y + 1, x + 1];
                    
                    heights[y, x] = sum / 16f;
                }
            }
        }
    }
    
    public BiomeNoiseProfile GetNoiseProfile()
    {
        return noiseProfile;
    }
}

