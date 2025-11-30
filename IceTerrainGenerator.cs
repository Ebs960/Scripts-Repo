using UnityEngine;

/// <summary>
/// Terrain generator for Ice/Snow/Tundra biomes
/// Creates flat to gently rolling terrain with occasional ice formations
/// </summary>
public class IceTerrainGenerator : IBiomeTerrainGenerator
{
    private BiomeNoiseProfile noiseProfile;
    
    public IceTerrainGenerator()
    {
        noiseProfile = new BiomeNoiseProfile
        {
            baseHeight = 0.25f,
            noiseScale = 0.06f,
            roughness = 0.15f,
            hilliness = 0.3f,
            mountainSharpness = 0.1f,
            octaves = 3,
            lacunarity = 2f,
            persistence = 0.35f,
            hillThreshold = 0.25f,
            mountainThreshold = 0.5f,
            maxHeightVariation = 4f,
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
        
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x / (float)resolution) * mapSize;
                float worldZ = (y / (float)resolution) * mapSize;
                
                float height = elevation * noiseProfile.baseHeight;
                float noiseValue = GenerateLayeredNoise(worldX, worldZ, noiseProfile);
                height += noiseValue * noiseProfile.hilliness * noiseProfile.maxHeightVariation;
                
                // Add occasional ice formations (small bumps)
                float iceFormation = Mathf.PerlinNoise(worldX * 0.15f, worldZ * 0.15f);
                if (iceFormation > 0.7f)
                {
                    height += (iceFormation - 0.7f) * 0.3f * noiseProfile.maxHeightVariation;
                }
                
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

