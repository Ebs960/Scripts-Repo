using UnityEngine;

[CreateAssetMenu(menuName = "Terrain/Biome Visual Data")]
public class BiomeVisualData : ScriptableObject
{
    public Biome biome;

    // Classify water behavior per-biome. This drives whether HDRP Water Surfaces
    // should be created for areas of this biome. Rivers are intentionally
    // distinguished so they can be rendered with decals/meshes later instead
    // of full Water Surface objects.
    public enum WaterType { None, Ocean, Lake, River }
    [Header("Water Behavior")]
    public WaterType waterType = WaterType.None;

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
