using UnityEngine;

/// <summary>
/// Terrain generator for Ocean/Coast/Seas biomes
/// Creates very flat terrain (water level)
/// </summary>
public class OceanTerrainGenerator : IBiomeTerrainGenerator
{
    private BiomeNoiseProfile noiseProfile;
    
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
            erosionStrength = 0.9f // Very strong erosion for flat water
        };
    }
    
    public void Generate(Terrain terrain, float elevation, float moisture, float temperature, float mapSize)
    {
        if (terrain == null || terrain.terrainData == null) return;
        
        TerrainData terrainData = terrain.terrainData;
        int resolution = terrainData.heightmapResolution;
        float[,] heights = new float[resolution, resolution];
        
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                // Very flat, near sea level
                float height = elevation * noiseProfile.baseHeight;
                
                // Minimal variation (gentle waves)
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (y / (float)resolution) * mapSize;
                float waveNoise = Mathf.PerlinNoise(worldX * 0.05f, worldZ * 0.05f);
                height += (waveNoise - 0.5f) * 0.1f * noiseProfile.maxHeightVariation;
                
                height = Mathf.Clamp01(height / noiseProfile.maxHeightVariation);
                heights[y, x] = height;
            }
        }
        
        // Strong erosion for flat water
        if (noiseProfile.useErosion)
        {
            ApplyErosion(heights, resolution, noiseProfile.erosionStrength);
        }
        
        terrainData.SetHeights(0, 0, heights);
    }
    
    private float GenerateLayeredNoise(float x, float z, BiomeNoiseProfile profile)
    {
        return Mathf.PerlinNoise(x * profile.noiseScale, z * profile.noiseScale) * 2f - 1f;
    }
    
    private void ApplyErosion(float[,] heights, int resolution, float strength)
    {
        // Multiple passes for very flat water
        for (int pass = 0; pass < 5; pass++)
        {
            for (int y = 1; y < resolution - 1; y++)
            {
                for (int x = 1; x < resolution - 1; x++)
                {
                    float avg = (heights[y, x] + heights[y - 1, x] + heights[y + 1, x] + 
                                heights[y, x - 1] + heights[y, x + 1]) / 5f;
                    
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

