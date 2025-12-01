using UnityEngine;

/// <summary>
/// Terrain generator for Desert biome
/// Creates sand dunes and flat areas using advanced FastNoiseLite with domain warping
/// </summary>
public class DesertTerrainGenerator : IBiomeTerrainGenerator
{
    private BiomeNoiseProfile noiseProfile;
    private BattleTerrainNoiseSystem noiseSystem;
    private BiomeTerrainSettings terrainSettings;
    
    public DesertTerrainGenerator()
    {
        noiseProfile = new BiomeNoiseProfile
        {
            baseHeight = 0.2f,
            noiseScale = 0.05f,
            roughness = 0.1f,
            hilliness = 0.2f,
            mountainSharpness = 0.05f,
            octaves = 2,
            lacunarity = 2.5f,
            persistence = 0.3f,
            hillThreshold = 0.2f,
            mountainThreshold = 0.5f,
            maxHeightVariation = 3f,
            useErosion = true,
            erosionStrength = 0.5f
        };
        
        // Use advanced desert terrain settings
        terrainSettings = BiomeTerrainSettings.CreateDesert();
    }
    
    public void Generate(Terrain terrain, float elevation, float moisture, float temperature, float mapSize)
    {
        if (terrain == null || terrain.terrainData == null) return;
        
        TerrainData terrainData = terrain.terrainData;
        int resolution = terrainData.heightmapResolution;
        float[,] heights = new float[resolution, resolution];
        
        // Initialize noise system
        noiseSystem = new BattleTerrainNoiseSystem(Random.Range(1, 100000));
        
        // Adjust for temperature (hotter = more wind-shaped dunes)
        terrainSettings.domainWarpStrength = 30f + temperature * 20f;
        terrainSettings.detailWeight = 0.3f + temperature * 0.2f;
        terrainSettings.baseElevation = elevation * 0.25f;
        
        // Generate heightmap with wind-shaped dunes
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (y / (float)resolution) * mapSize;
                
                // Get base terrain height
                float height = noiseSystem.GetTerrainHeight(worldX, worldZ, terrainSettings);
                
                // Add extra dune-specific noise (directional for wind effect)
                float duneNoise = AddDuneNoise(worldX, worldZ);
                height += duneNoise * 0.15f;
                
                heights[y, x] = Mathf.Clamp01(height);
            }
        }
        
        // Apply wind erosion
        ApplyWindErosion(heights, resolution);
        
        // Set heights
        terrainData.SetHeights(0, 0, heights);
    }
    
    /// <summary>
    /// Add directional dune noise for wind-shaped sand dunes
    /// </summary>
    private float AddDuneNoise(float x, float z)
    {
        // Wind direction bias (stretches dunes along wind direction)
        float windAngle = 0.7f; // Radians
        float stretchedX = x * Mathf.Cos(windAngle) - z * Mathf.Sin(windAngle);
        float stretchedZ = (x * Mathf.Sin(windAngle) + z * Mathf.Cos(windAngle)) * 0.3f;
        
        // Layered dune noise
        float dune = Mathf.PerlinNoise(stretchedX * 0.05f, stretchedZ * 0.15f);
        dune += Mathf.PerlinNoise(stretchedX * 0.1f, stretchedZ * 0.3f) * 0.5f;
        
        return dune - 0.5f; // Center around 0
    }
    
    private void ApplyWindErosion(float[,] heights, int resolution)
    {
        // Wind erosion: smooth along wind direction, sharpen perpendicular
        for (int pass = 0; pass < 2; pass++)
        {
            for (int y = 1; y < resolution - 1; y++)
            {
                for (int x = 1; x < resolution - 1; x++)
                {
                    // Wind direction weighted average
                    float windAvg = heights[y, x - 1] * 0.3f + heights[y, x] * 0.4f + heights[y, x + 1] * 0.3f;
                    
                    // Blend with wind-smoothed value
                    heights[y, x] = Mathf.Lerp(heights[y, x], windAvg, 0.3f);
                }
            }
        }
    }
    
    public BiomeNoiseProfile GetNoiseProfile()
    {
        return noiseProfile;
    }
}

