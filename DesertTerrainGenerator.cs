using UnityEngine;

/// <summary>
/// Terrain generator for Desert biome
/// Creates sand dunes and flat areas
/// </summary>
public class DesertTerrainGenerator : IBiomeTerrainGenerator
{
    private BiomeNoiseProfile noiseProfile;
    
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
    }
    
    public void Generate(Terrain terrain, float elevation, float moisture, float temperature, float mapSize)
    {
        if (terrain == null || terrain.terrainData == null) return;
        
        TerrainData terrainData = terrain.terrainData;
        int resolution = terrainData.heightmapResolution;
        float[,] heights = new float[resolution, resolution];
        
        // Generate heightmap with gentle dunes
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (y / (float)resolution) * mapSize;
                
                // Base height from elevation
                float height = elevation * noiseProfile.baseHeight;
                
                // Add gentle dune noise
                float noiseValue = GenerateLayeredNoise(worldX, worldZ, noiseProfile);
                
                // Apply gentle hilliness for dunes
                height += noiseValue * noiseProfile.hilliness * noiseProfile.maxHeightVariation;
                
                // Normalize to 0-1 range
                height = Mathf.Clamp01(height / noiseProfile.maxHeightVariation);
                
                heights[y, x] = height;
            }
        }
        
        // Apply erosion to create flatter areas
        if (noiseProfile.useErosion)
        {
            ApplyErosion(heights, resolution, noiseProfile.erosionStrength);
        }
        
        // Set heights
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
        
        // Normalize to -1 to 1 range
        float maxValue = (1f - Mathf.Pow(profile.persistence, profile.octaves)) / (1f - profile.persistence);
        return (value / maxValue) * 2f - 1f;
    }
    
    private void ApplyErosion(float[,] heights, int resolution, float strength)
    {
        // Strong erosion for flat desert areas
        for (int y = 1; y < resolution - 1; y++)
        {
            for (int x = 1; x < resolution - 1; x++)
            {
                float avg = (heights[y, x] + heights[y - 1, x] + heights[y + 1, x] + 
                            heights[y, x - 1] + heights[y, x + 1]) / 5f;
                
                if (heights[y, x] < avg)
                {
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

