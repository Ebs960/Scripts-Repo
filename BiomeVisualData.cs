using UnityEngine;

[CreateAssetMenu(menuName = "Terrain/Biome Visual Data")]
public class BiomeVisualData : ScriptableObject
{
    public Biome biome;

    [Header("Core PBR Textures (HDRP compatible)")]
    public Texture2D albedo;
    public Texture2D normal;
    public Texture2D maskMap;

    [Header("Visual Tuning")]
    public float tiling = 1f;
    public Color tint = Color.white;

    [Header("Climate Response")]
    [Range(0f, 1f)] public float snowRetention;
    [Range(0f, 1f)] public float wetnessResponse;

    [Header("Flags")]
    public bool isWaterBiome;
}
