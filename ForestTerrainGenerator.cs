using UnityEngine;

/// <summary>
/// Terrain generator for Forest biome
/// Creates rolling hills with moderate variation using FastNoiseLite fBm
/// </summary>
public class ForestTerrainGenerator : IBiomeTerrainGenerator
{
    private BiomeNoiseProfile noiseProfile;
    private BattleTerrainNoiseSystem noiseSystem;
    private BiomeTerrainSettings terrainSettings;
    
    public ForestTerrainGenerator()
    {
        noiseProfile = new BiomeNoiseProfile
        {
            baseHeight = 0.35f,
            noiseScale = 0.09f,
            roughness = 0.4f,
            hilliness = 0.6f,
            mountainSharpness = 0.2f,
            octaves = 4,
            lacunarity = 2.1f,
            persistence = 0.5f,
            hillThreshold = 0.35f,
            mountainThreshold = 0.65f,
            maxHeightVariation = 7f,
            useErosion = true,
            erosionStrength = 0.3f
        };
        
        // Use advanced forest terrain settings
        terrainSettings = BiomeTerrainSettings.CreateForest();
    }
    
    public void Generate(Terrain terrain, float elevation, float moisture, float temperature, float mapSize)
    {
        if (terrain == null || terrain.terrainData == null) return;
        
        TerrainData terrainData = terrain.terrainData;
        int resolution = terrainData.heightmapResolution;
        float[,] heights = new float[resolution, resolution];
        
        // Initialize noise system
        noiseSystem = new BattleTerrainNoiseSystem(Random.Range(1, 100000));
        
        // Adjust for moisture (wetter = more valleys from water erosion)
        terrainSettings.baseElevation = elevation * 0.35f;
        terrainSettings.valleyWeight = 0.15f + moisture * 0.15f;
        terrainSettings.domainWarpStrength = 30f + moisture * 10f;
        
        // Generate heightmap
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (y / (float)resolution) * mapSize;
                
                // Get base terrain height
                float height = noiseSystem.GetTerrainHeight(worldX, worldZ, terrainSettings);
                
                // Add forest floor detail (root bumps, fallen logs, etc.)
                float forestDetail = AddForestFloorDetail(worldX, worldZ);
                height += forestDetail * 0.05f;
                
                heights[y, x] = Mathf.Clamp01(height);
            }
        }
        
        // Apply erosion (water erosion in forests)
        ApplyHydraulicErosion(heights, resolution, moisture);
        
        terrainData.SetHeights(0, 0, heights);
    }
    
    /// <summary>
    /// Add small-scale forest floor detail
    /// </summary>
    private float AddForestFloorDetail(float x, float z)
    {
        // High frequency noise for ground detail
        float detail = Mathf.PerlinNoise(x * 0.3f, z * 0.3f);
        detail += Mathf.PerlinNoise(x * 0.5f + 50f, z * 0.5f + 50f) * 0.5f;
        return detail - 0.75f;
    }
    
    /// <summary>
    /// Apply hydraulic erosion (water flow)
    /// </summary>
    private void ApplyHydraulicErosion(float[,] heights, int resolution, float moisture)
    {
        int passes = 2 + Mathf.RoundToInt(moisture * 2f);
        float strength = 0.2f + moisture * 0.2f;
        
        for (int pass = 0; pass < passes; pass++)
        {
            for (int y = 1; y < resolution - 1; y++)
            {
                for (int x = 1; x < resolution - 1; x++)
                {
                    float h = heights[y, x];
                    
                    // Find lowest neighbor
                    float minNeighbor = Mathf.Min(
                        heights[y - 1, x], heights[y + 1, x],
                        heights[y, x - 1], heights[y, x + 1]
                    );
                    
                    // Water flows downhill, carving valleys
                    if (h > minNeighbor)
                    {
                        float diff = h - minNeighbor;
                        heights[y, x] -= diff * strength * 0.3f;
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

