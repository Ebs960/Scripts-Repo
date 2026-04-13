using UnityEngine;

[System.Serializable]
public struct BiomeSeasonVisualResponse
{
    [Range(0f, 1f)] public float snow;
    [Range(0f, 1f)] public float dry;
    public Color tint;
}

[CreateAssetMenu(menuName = "Terrain/Biome Visual Data")]
public class BiomeVisualData : ScriptableObject
{
    [Header("Seasonal Visual Responses")]
    public BiomeSeasonVisualResponse springResponse;
    public BiomeSeasonVisualResponse summerResponse;
    public BiomeSeasonVisualResponse autumnResponse;
    public BiomeSeasonVisualResponse winterResponse;
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

    [Header("Visual Tuning")]
    public float tiling = 1f;
    public Color tint = Color.white;

    [Header("Emission")]
    [Tooltip("Per-biome emissive tint (multiply by intensity).")]
    public Color emissiveTint = Color.white;
    [Tooltip("Per-biome emissive intensity (HDR multiplier). 0 = no emission.")]
    public float emissiveIntensity = 0f;

    [Header("Climate Response")]
    [Tooltip("Inherent wetness of this biome (0 = dry, 1 = fully wet). Swamps/marshes should be high. Drives per-biome glossiness and albedo darkening.")]
    [Range(0f, 1f)]
    public float inherentWetness = 0f;

    [Header("Flags")]
    public bool isWaterBiome;
}
