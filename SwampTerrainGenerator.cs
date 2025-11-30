using UnityEngine;

/// <summary>
/// Terrain generator for Swamp/Marsh biomes
/// Creates very flat terrain with occasional depressions (water areas)
/// </summary>
public class SwampTerrainGenerator : IBiomeTerrainGenerator
{
    private BiomeNoiseProfile noiseProfile;
    
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
            erosionStrength = 0.7f // Strong erosion for flat swamps
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
                
                // Very flat base
                float height = elevation * noiseProfile.baseHeight;
                
                // Add very subtle variation
                float noiseValue = GenerateLayeredNoise(worldX, worldZ, noiseProfile);
                height += noiseValue * noiseProfile.hilliness * noiseProfile.maxHeightVariation;
                
                // Create occasional depressions (water areas) based on moisture
                if (moisture > 0.6f)
                {
                    float depressionNoise = Mathf.PerlinNoise(worldX * 0.02f, worldZ * 0.02f);
                    if (depressionNoise < 0.3f) // 30% chance of depression
                    {
                        height -= (0.3f - depressionNoise) * 0.5f; // Create shallow depressions
                    }
                }
                
                height = Mathf.Clamp01(height / noiseProfile.maxHeightVariation);
                heights[y, x] = height;
            }
        }
        
        // Strong erosion for flat swamps
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
        // Multiple passes for very flat terrain
        for (int pass = 0; pass < 3; pass++)
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

