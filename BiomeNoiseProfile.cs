using UnityEngine;

/// <summary>
/// Noise parameters for biome-specific terrain generation
/// Controls how terrain looks (hills, mountains, flat plains, etc.)
/// </summary>
[System.Serializable]
public class BiomeNoiseProfile
{
    [Header("Base Terrain")]
    [Tooltip("Base height multiplier (0-1)")]
    public float baseHeight = 0.5f;
    
    [Header("Noise Parameters")]
    [Tooltip("Scale of the noise (lower = larger features)")]
    public float noiseScale = 0.1f;
    
    [Tooltip("Roughness of terrain (0 = smooth, 1 = very rough)")]
    [Range(0f, 1f)]
    public float roughness = 0.3f;
    
    [Tooltip("Hilliness (0 = flat, 1 = very hilly)")]
    [Range(0f, 1f)]
    public float hilliness = 0.5f;
    
    [Tooltip("Mountain sharpness (0 = rounded, 1 = sharp peaks)")]
    [Range(0f, 1f)]
    public float mountainSharpness = 0.3f;
    
    [Header("Layered Noise")]
    [Tooltip("Number of noise octaves (more = more detail)")]
    [Range(1, 8)]
    public int octaves = 4;
    
    [Tooltip("Lacunarity (frequency multiplier per octave)")]
    public float lacunarity = 2f;
    
    [Tooltip("Persistence (amplitude multiplier per octave)")]
    [Range(0f, 1f)]
    public float persistence = 0.5f;
    
    [Header("Terrain Features")]
    [Tooltip("Threshold for hills (0-1)")]
    public float hillThreshold = 0.4f;
    
    [Tooltip("Threshold for mountains (0-1)")]
    public float mountainThreshold = 0.7f;
    
    [Tooltip("Maximum height variation")]
    public float maxHeightVariation = 10f;
    
    [Header("Erosion/Blending")]
    [Tooltip("Apply slope-based erosion (flatten valleys)")]
    public bool useErosion = true;
    
    [Tooltip("Erosion strength (0-1)")]
    [Range(0f, 1f)]
    public float erosionStrength = 0.3f;
}

