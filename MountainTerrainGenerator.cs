using UnityEngine;

/// <summary>
/// Terrain generator for Mountain biome
/// Creates sharp peaks and ridges
/// </summary>
public class MountainTerrainGenerator : IBiomeTerrainGenerator
{
    private BiomeNoiseProfile noiseProfile;
    
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
    }
    
    public void Generate(Terrain terrain, float elevation, float moisture, float temperature, float mapSize)
    {
        if (terrain == null || terrain.terrainData == null) return;
        
        TerrainData terrainData = terrain.terrainData;
        int resolution = terrainData.heightmapResolution;
        float[,] heights = new float[resolution, resolution];
        
        // Generate heightmap with sharp peaks
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (y / (float)resolution) * mapSize;
                
                // Base height from elevation
                float height = elevation * noiseProfile.baseHeight;
                
                // Add layered noise for mountains
                float noiseValue = GenerateLayeredNoise(worldX, worldZ, noiseProfile);
                
                // Apply sharpness using power function
                float sharpNoise = Mathf.Pow(Mathf.Abs(noiseValue), 1f - noiseProfile.mountainSharpness * 0.5f);
                if (noiseValue < 0) sharpNoise = -sharpNoise;
                
                // Apply hilliness with sharp peaks
                height += sharpNoise * noiseProfile.hilliness * noiseProfile.maxHeightVariation;
                
                // Normalize to 0-1 range
                height = Mathf.Clamp01(height / noiseProfile.maxHeightVariation);
                
                heights[y, x] = height;
            }
        }
        
        // No erosion for mountains (keep them sharp)
        
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
    
    public BiomeNoiseProfile GetNoiseProfile()
    {
        return noiseProfile;
    }
}

