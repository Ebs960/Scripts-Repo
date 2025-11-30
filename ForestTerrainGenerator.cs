using UnityEngine;

/// <summary>
/// Terrain generator for Forest biome
/// Creates rolling hills with moderate variation (more than plains, less than mountains)
/// </summary>
public class ForestTerrainGenerator : IBiomeTerrainGenerator
{
    private BiomeNoiseProfile noiseProfile;
    
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
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (y / (float)resolution) * mapSize;
                
                float height = elevation * noiseProfile.baseHeight;
                float noiseValue = GenerateLayeredNoise(worldX, worldZ, noiseProfile);
                height += noiseValue * noiseProfile.hilliness * noiseProfile.maxHeightVariation;
                height = Mathf.Clamp01(height / noiseProfile.maxHeightVariation);
                
                heights[y, x] = height;
            }
        }
        
        if (noiseProfile.useErosion)
        {
            ApplyErosion(heights, resolution, noiseProfile.erosionStrength);
        }
        
        terrainData.SetHeights(0, 0, heights);
    }
    
    private float GenerateLayeredNoise(float x, float z, BiomeNoiseProfile profile)
    {
        float value = 0f;
        float amplitude = 1f;
        float frequency = profile.noiseScale;
        
        for (int i = 0; i < profile.octaves; i++)
        {
            value += Mathf.PerlinNoise(x * frequency, z * frequency) * amplitude;
            frequency *= profile.lacunarity;
            amplitude *= profile.persistence;
        }
        
        float maxValue = (1f - Mathf.Pow(profile.persistence, profile.octaves)) / (1f - profile.persistence);
        return (value / maxValue) * 2f - 1f;
    }
    
    private void ApplyErosion(float[,] heights, int resolution, float strength)
    {
        for (int y = 1; y < resolution - 1; y++)
        {
            for (int x = 1; x < resolution - 1; x++)
            {
                float avg = (heights[y, x] + heights[y - 1, x] + heights[y + 1, x] + 
                            heights[y, x - 1] + heights[y, x + 1]) / 5f;
                
                if (heights[y, x] < avg)
                {
                    heights[y, x] = Mathf.Lerp(heights[y, x], avg, strength * 0.5f);
                }
            }
        }
    }
    
    public BiomeNoiseProfile GetNoiseProfile()
    {
        return noiseProfile;
    }
}

