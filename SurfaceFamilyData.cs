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
}
