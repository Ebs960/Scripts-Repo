using UnityEngine;

[CreateAssetMenu(menuName = "Terrain/Surface Family Data")]
public class SurfaceFamilyData : ScriptableObject
{
    [Header("Identification")]
    public string familyName;

    [Header("Variant Texture Arrays (slices = variants)")]
    public Texture2DArray albedoArray;
    public Texture2DArray normalArray;
    public Texture2DArray maskArray;
    // Optional per-variant height/displacement maps (single-channel, RHalf recommended)
    public Texture2DArray heightArray;

    [Header("Mountain Variant Texture Arrays (optional)")]
    [Tooltip("Optional mountain-only albedo variants for this surface family. If omitted, mountains use the base arrays.")]
    public Texture2DArray mountainAlbedoArray;
    [Tooltip("Optional mountain-only normal variants for this surface family. If omitted, mountains use the base arrays.")]
    public Texture2DArray mountainNormalArray;
    [Tooltip("Optional mountain-only mask variants for this surface family. If omitted, mountains use the base arrays.")]
    public Texture2DArray mountainMaskArray;
    [Tooltip("Optional mountain-only height/displacement variants. If omitted, mountains use the base height array.")]
    public Texture2DArray mountainHeightArray;
    [Tooltip("Optional mountain-only emissive variants. If omitted, mountains use the base emissive array.")]
    public Texture2DArray mountainEmissiveArray;

    [Header("Emissive (optional)")]
    public Texture2DArray emissiveArray;
    public bool supportsEmission = false;

    [Header("Defaults per-family")]
    public float defaultTiling = 1f;
    public Color defaultTint = Color.white;
    public float normalStrength = 1f;
    public float roughnessOffset = 0f;

    public int VariantCount
    {
        get
        {
            if (albedoArray != null) return albedoArray.depth;
            if (normalArray != null) return normalArray.depth;
            if (maskArray != null) return maskArray.depth;
            return 0;
        }
    }

    public int MountainVariantCount
    {
        get
        {
            if (mountainAlbedoArray != null) return mountainAlbedoArray.depth;
            if (mountainNormalArray != null) return mountainNormalArray.depth;
            if (mountainMaskArray != null) return mountainMaskArray.depth;
            return 0;
        }
    }

    public bool HasMountainVariants =>
        mountainAlbedoArray != null || mountainNormalArray != null || mountainMaskArray != null;
}
