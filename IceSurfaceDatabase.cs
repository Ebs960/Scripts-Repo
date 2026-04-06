using UnityEngine;

/// <summary>
/// ScriptableObject that holds all visual assets used to render frozen water surfaces.
/// Assign to <see cref="ClimateManager.iceSurfaceDatabase"/>.
/// <see cref="HexMapChunkManager"/> reads from this at chunk build time to bind
/// ice textures to the terrain/water shader.
///
/// Design mirrors BiomeVisualDatabase: drop an instance in the project, fill the texture
/// slots, assign it to ClimateManager in the Inspector, and the freeze system will
/// automatically pick up the albedo/normal/mask maps for lakes and rivers.
/// </summary>
[CreateAssetMenu(menuName = "Terrain/Ice Surface Database")]
public class IceSurfaceDatabase : ScriptableObject
{
    // ─────────────────────────────────────────────────────────────
    // Lake Ice
    // ─────────────────────────────────────────────────────────────
    [Header("Lake Ice Surface")]
    [Tooltip("Texture array for frozen lake albedo variants.")]
    public Texture2DArray lakeIceAlbedoArray;

    [Tooltip("Texture array for frozen lake normal variants.")]
    public Texture2DArray lakeIceNormalArray;

    [Tooltip("Texture array for frozen lake mask variants.")]
    public Texture2DArray lakeIceMaskArray;

    [Tooltip("Texture array for frozen lake height variants.")]
    public Texture2DArray lakeIceHeightArray;

    [Tooltip("Tint colour multiplied on top of lakeIceAlbedo.")]
    public Color lakeIceTint = Color.white;

    [Tooltip("UV tiling for lake ice textures (world-space density).")]
    [Range(0.01f, 200f)]
    public float lakeIceTiling = 8f;

    // ─────────────────────────────────────────────────────────────
    // River Ice
    // ─────────────────────────────────────────────────────────────
    [Header("River Ice Surface")]
    [Tooltip("Texture array for frozen river albedo variants.")]
    public Texture2DArray riverIceAlbedoArray;

    [Tooltip("Texture array for frozen river normal variants.")]
    public Texture2DArray riverIceNormalArray;

    [Tooltip("Texture array for frozen river mask variants.")]
    public Texture2DArray riverIceMaskArray;

    [Tooltip("Texture array for frozen river height variants.")]
    public Texture2DArray riverIceHeightArray;

    [Tooltip("Tint colour multiplied on top of riverIceAlbedo.")]
    public Color riverIceTint = Color.white;

    [Tooltip("UV tiling for river ice textures.")]
    [Range(0.01f, 200f)]
    public float riverIceTiling = 12f;

    // ─────────────────────────────────────────────────────────────
    // Shared Visual Settings
    // ─────────────────────────────────────────────────────────────
    [Header("Shared Ice Visual Settings")]

    [Tooltip("Multiplier for the normal map intensity on frozen surfaces. " +
             "0 = smooth mirror ice, 1 = normal map used as-is, >1 exaggerates cracking.")]
    [Range(0f, 3f)]
    public float iceNormalStrength = 1f;

    [Tooltip("Smoothness of the ice surface. Higher values produce mirror-like reflections (glassy ice); " +
             "lower values look like matte snow-ice.")]
    [Range(0f, 1f)]
    public float iceSmoothness = 0.85f;

    [Tooltip("Metallic value for the ice surface (usually 0 – ice is dielectric).")]
    [Range(0f, 1f)]
    public float iceMetallic = 0f;

    [Tooltip("Freeze amount above which a tile is treated as visually fully-solid ice " +
             "(opaque, no water shimmer). Must match or exceed HexTileData.FreezeSolidThreshold.")]
    [Range(0.5f, 1f)]
    public float freezeOpaqueThreshold = 0.9f;
}
