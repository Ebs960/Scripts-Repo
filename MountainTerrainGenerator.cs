using UnityEngine;

/// <summary>
/// Terrain generator for Mountain biome
/// Creates sharp peaks and ridges using FastNoiseLite Ridged noise
/// </summary>
public class MountainTerrainGenerator : IBiomeTerrainGenerator
{
    private BiomeNoiseProfile noiseProfile;
    private BattleTerrainNoiseSystem noiseSystem;
    private BiomeTerrainSettings terrainSettings;
    
    public MountainTerrainGenerator()
    {
        noiseProfile = new BiomeNoiseProfile
        {
            baseHeight = 0.6f,
            noiseScale = 0.12f,
            roughness = 0.8f,
            hilliness = 0.9f,
            mountainSharpness = 0.9f,
            octaves = 6,
            lacunarity = 2.2f,
            persistence = 0.6f,
            hillThreshold = 0.5f,
            mountainThreshold = 0.7f,
            maxHeightVariation = 15f,
            useErosion = false,
            erosionStrength = 0.1f
        };
        
        // Use advanced mountain terrain settings
        terrainSettings = BiomeTerrainSettings.CreateMountain();
    }
    
    public void Generate(Terrain terrain, float elevation, float moisture, float temperature, float mapSize)
    {
        if (terrain == null || terrain.terrainData == null) return;
        
        TerrainData terrainData = terrain.terrainData;
        int resolution = terrainData.heightmapResolution;
        float[,] heights = new float[resolution, resolution];
        
        // Initialize noise system
        noiseSystem = new BattleTerrainNoiseSystem(Random.Range(1, 100000));
        
        // Adjust for elevation (higher base = more dramatic mountains)
        terrainSettings.baseElevation = 0.3f + elevation * 0.2f;
        terrainSettings.heightScale = 1.2f + elevation * 0.5f;
        terrainSettings.ridgeWeight = 0.35f + elevation * 0.15f;
        terrainSettings.ridgeSharpness = 2.0f + elevation * 1.0f;
        
        // Colder = sharper peaks (less weathering)
        if (temperature < 0.3f)
        {
            terrainSettings.ridgeSharpness += 0.5f;
        }
        
        // Generate heightmap with ridged noise for sharp peaks
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (y / (float)resolution) * mapSize;
                
                // Get base terrain height with strong ridge component
                float height = noiseSystem.GetTerrainHeight(worldX, worldZ, terrainSettings);
                
                // Add extra cliff/ledge features
                float cliffNoise = AddCliffFeatures(worldX, worldZ);
                height = Mathf.Max(height, height + cliffNoise * 0.2f);
                
                heights[y, x] = Mathf.Clamp01(height);
            }
        }
        
        // Apply minimal thermal erosion (crumbling cliffs)
        if (temperature > 0.5f)
        {
            ApplyThermalErosion(heights, resolution, 0.2f);
        }
        
        // Set heights
        terrainData.SetHeights(0, 0, heights);
    }
    
    /// <summary>
    /// Add cliff and ledge features to mountains
    /// </summary>
    private float AddCliffFeatures(float x, float z)
    {
        // Use cellular noise for cliff edges
        float cliff1 = Mathf.PerlinNoise(x * 0.08f, z * 0.08f);
        float cliff2 = Mathf.PerlinNoise(x * 0.15f + 100f, z * 0.15f + 100f);
        
        // Create terraced effect
        cliff1 = Mathf.Floor(cliff1 * 4f) / 4f;
        
        return (cliff1 + cliff2 * 0.3f) - 0.5f;
    }
    
    /// <summary>
    /// Apply thermal erosion (rocks crumbling from cliffs)
    /// </summary>
    private void ApplyThermalErosion(float[,] heights, int resolution, float strength)
    {
        float talusAngle = 0.4f; // Maximum stable slope
        
        for (int y = 1; y < resolution - 1; y++)
        {
            for (int x = 1; x < resolution - 1; x++)
            {
                float h = heights[y, x];
                
                // Check all neighbors
                float[] neighbors = {
                    heights[y - 1, x], heights[y + 1, x],
                    heights[y, x - 1], heights[y, x + 1]
                };
                
                foreach (float nh in neighbors)
                {
                    float diff = h - nh;
                    if (diff > talusAngle)
                    {
                        // Move material downhill
                        float transfer = (diff - talusAngle) * strength * 0.5f;
                        heights[y, x] -= transfer;
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

