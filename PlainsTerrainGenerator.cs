using UnityEngine;

/// <summary>
/// Terrain generator for Plains biome
/// Creates rolling hills with gentle slopes using advanced FastNoiseLite fBm
/// </summary>
public class PlainsTerrainGenerator : IBiomeTerrainGenerator
{
    private BiomeNoiseProfile noiseProfile;
    private BattleTerrainNoiseSystem noiseSystem;
    private BiomeTerrainSettings terrainSettings;
    
    public PlainsTerrainGenerator()
    {
        noiseProfile = new BiomeNoiseProfile
        {
            baseHeight = 0.3f,
            noiseScale = 0.08f,
            roughness = 0.2f,
            hilliness = 0.4f,
            mountainSharpness = 0.1f,
            octaves = 3,
            lacunarity = 2f,
            persistence = 0.4f,
            hillThreshold = 0.3f,
            mountainThreshold = 0.6f,
            maxHeightVariation = 5f,
            useErosion = true,
            erosionStrength = 0.4f
        };
        
        // Use the new advanced terrain settings
        terrainSettings = BiomeTerrainSettings.CreatePlains();
    }
    
    public void Generate(Terrain terrain, float elevation, float moisture, float temperature, float mapSize)
    {
        if (terrain == null || terrain.terrainData == null) return;
        
        TerrainData terrainData = terrain.terrainData;
        int resolution = terrainData.heightmapResolution;
        float[,] heights = new float[resolution, resolution];
        
        // Initialize noise system with random seed for variety
        noiseSystem = new BattleTerrainNoiseSystem(Random.Range(1, 100000));
        
        // Adjust settings based on elevation/moisture
        terrainSettings.baseElevation = elevation * 0.3f;
        terrainSettings.heightScale = 0.5f + elevation * 0.3f;
        
        // More moisture = more valleys (water erosion)
        terrainSettings.valleyWeight = 0.1f + moisture * 0.2f;
        
        // Generate heightmap using advanced noise system
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (y / (float)resolution) * mapSize;
                
                // Get height from advanced noise system
                float height = noiseSystem.GetTerrainHeight(worldX, worldZ, terrainSettings);
                
                heights[y, x] = height;
            }
        }
        
        // Apply erosion pass
        ApplyErosion(heights, resolution, moisture);
        
        // Set heights
        terrainData.SetHeights(0, 0, heights);
    }
    
    private void ApplyErosion(float[,] heights, int resolution, float moisture)
    {
        // Multi-pass erosion for smoother terrain
        int passes = 2;
        float strength = noiseProfile.erosionStrength * (1f + moisture * 0.5f);
        
        for (int pass = 0; pass < passes; pass++)
        {
            for (int y = 1; y < resolution - 1; y++)
            {
                for (int x = 1; x < resolution - 1; x++)
                {
                    // Calculate slope
                    float slope = noiseSystem.CalculateSlope(heights, x, y, resolution);
                    
                    // Average with neighbors
                    float avg = (heights[y, x] + heights[y - 1, x] + heights[y + 1, x] + 
                                heights[y, x - 1] + heights[y, x + 1]) / 5f;
                    
                    // Flatten low areas and steep slopes
                    if (heights[y, x] < avg || slope > 0.3f)
                    {
                        float erosionAmount = strength * (1f - Mathf.Clamp01(slope * 2f));
                        heights[y, x] = Mathf.Lerp(heights[y, x], avg, erosionAmount * 0.3f);
                    }
                }
            }
        }
    }
    
    public BiomeNoiseProfile GetNoiseProfile()
    {
        return noiseProfile;
    }
}

