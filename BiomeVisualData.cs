using UnityEngine;

[CreateAssetMenu(menuName = "Terrain/Biome Visual Data")]
public class BiomeVisualData : ScriptableObject
{
    public Biome biome;

    [Header("Surface Family (new)")]
    public SurfaceFamilyData surfaceFamily;
    [Tooltip("Optional: force a specific variant index for this biome (0-based). -1 = automatic selection")]
    public int forcedVariant = -1;

    [Header("Legacy Textures (deprecated)")]
    [HideInInspector]
    public Texture2D albedo;
    [HideInInspector]
    public Texture2D normal;
    [HideInInspector]
    public Texture2D maskMap;

    [Header("Visual Tuning")]
    public float tiling = 1f;
    public Color tint = Color.white;

    [Header("Emission")]
    [Tooltip("Per-biome emissive tint (multiply by intensity).")]
    public Color emissiveTint = Color.white;
    [Tooltip("Per-biome emissive intensity (HDR multiplier). 0 = no emission.")]
    public float emissiveIntensity = 0f;

    [Header("Climate Response")]
    [Range(0f, 1f)] public float snowRetention;
    [Range(0f, 1f)] public float wetnessResponse;

    [Header("Flags")]
    public bool isWaterBiome;
}
