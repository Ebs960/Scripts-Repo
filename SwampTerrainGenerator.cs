using UnityEngine;

/// <summary>
/// Terrain generator for Swamp/Marsh biomes
/// Creates very flat terrain with water pockets using cellular noise
/// </summary>
public class SwampTerrainGenerator : IBiomeTerrainGenerator
{
    private BiomeNoiseProfile noiseProfile;
    private BattleTerrainNoiseSystem noiseSystem;
    private BiomeTerrainSettings terrainSettings;
    
    public SwampTerrainGenerator()
    {
        noiseProfile = new BiomeNoiseProfile
        {
            baseHeight = 0.15f,
            noiseScale = 0.03f,
            roughness = 0.05f,
            hilliness = 0.1f,
            mountainSharpness = 0.0f,
            octaves = 2,
            lacunarity = 2f,
            persistence = 0.3f,
            hillThreshold = 0.1f,
            mountainThreshold = 0.3f,
            maxHeightVariation = 2f,
            useErosion = true,
            erosionStrength = 0.7f
        };
        
        // Use advanced swamp terrain settings
        terrainSettings = BiomeTerrainSettings.CreateSwamp();
    }
    
    public void Generate(Terrain terrain, float elevation, float moisture, float temperature, float mapSize)
    {
        if (terrain == null || terrain.terrainData == null) return;
        
        TerrainData terrainData = terrain.terrainData;
        int resolution = terrainData.heightmapResolution;
        float[,] heights = new float[resolution, resolution];
        
        // Initialize noise system
        noiseSystem = new BattleTerrainNoiseSystem(Random.Range(1, 100000));
        
        // Adjust for moisture (more water pockets)
        terrainSettings.baseElevation = elevation * 0.2f;
        terrainSettings.valleyWeight = 0.4f + moisture * 0.3f;
        terrainSettings.heightScale = 0.3f; // Very flat
        
        // Generate heightmap
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (y / (float)resolution) * mapSize;
                
                // Get base terrain height
                float height = noiseSystem.GetTerrainHeight(worldX, worldZ, terrainSettings);
                
                // Add water pockets using cellular noise (biome mask)
                float waterPocket = AddWaterPockets(worldX, worldZ, moisture);
                height -= waterPocket;
                
                // Add mud mounds and islets
                float moundNoise = AddMudMounds(worldX, worldZ);
                height += moundNoise * 0.1f;
                
                heights[y, x] = Mathf.Clamp01(height);
            }
        }
        
        // Apply heavy smoothing for flat swamp
        ApplySwampSmoothing(heights, resolution);
        
        terrainData.SetHeights(0, 0, heights);
    }
    
    /// <summary>
    /// Add water pocket depressions using cellular noise
    /// </summary>
    private float AddWaterPockets(float x, float z, float moisture)
    {
        // Use cellular noise for natural-looking pools
        float cellNoise = noiseSystem.GetBiomeMask(x, z, 1.5f);
        
        // Create pools where cellular value is low
        float poolThreshold = 0.3f + (1f - moisture) * 0.2f;
        
        if (cellNoise < poolThreshold)
        {
            // Deeper pools in center, shallower at edges
            float poolDepth = (poolThreshold - cellNoise) / poolThreshold;
            poolDepth = poolDepth * poolDepth * 0.15f; // Max 15% depth
            return poolDepth;
        }
        
        return 0f;
    }
    
    /// <summary>
    /// Add small mud mounds and islets
    /// </summary>
    private float AddMudMounds(float x, float z)
    {
        // Small-scale cellular noise for mounds
        float featureNoise = noiseSystem.GetFeatureMask(x, z, 2f);
        
        // Only add mounds at peaks
        if (featureNoise > 0.7f)
        {
            return (featureNoise - 0.7f) / 0.3f;
        }
        
        return 0f;
    }
    
    /// <summary>
    /// Apply heavy smoothing for flat swamp terrain
    /// </summary>
    private void ApplySwampSmoothing(float[,] heights, int resolution)
    {
        // Multiple passes of heavy smoothing
        for (int pass = 0; pass < 4; pass++)
        {
            for (int y = 1; y < resolution - 1; y++)
            {
                for (int x = 1; x < resolution - 1; x++)
                {
                    // 9-point average for smoother results
                    float sum = heights[y, x];
                    sum += heights[y - 1, x] + heights[y + 1, x];
                    sum += heights[y, x - 1] + heights[y, x + 1];
                    sum += heights[y - 1, x - 1] + heights[y - 1, x + 1];
                    sum += heights[y + 1, x - 1] + heights[y + 1, x + 1];
                    
                    heights[y, x] = Mathf.Lerp(heights[y, x], sum / 9f, 0.6f);
                }
            }
        }
    }
    
    public BiomeNoiseProfile GetNoiseProfile()
    {
        return noiseProfile;
    }
}

