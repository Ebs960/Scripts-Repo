using UnityEngine;

/// <summary>
/// GPU-based UV/LUT picker for texture-planet mode.
/// Replaces per-tile colliders with a single collider + LUT lookup.
/// 
/// Picking flow:
/// - Screen → Raycast → UV → Pixel Index → LUT → Tile Index
/// 
/// Works identically in both views:
/// - Flat view: raycast against ONE plane/quad collider
/// - Globe view: raycast against ONE sphere collider  
/// - Morph view: raycast against morph mesh collider (works at any morph value)
/// 
/// IMPORTANT:
/// - CPU remains authoritative for game state
/// - This is visual-only picking (no gameplay logic changes)
/// - Uses the same pixel → tile LUT as GPU texture baking
/// </summary>
public class WorldPicker : MonoBehaviour
{
    [Header("References")]
    public Camera targetCamera;
    public Collider flatMapCollider;
    public Collider globeCollider;
    [Tooltip("Morph mesh collider (used when morphing between flat and globe)")]
    public Collider morphMeshCollider;

    [Header("LUT")]
    [Tooltip("Width of the LUT texture (matches texture resolution)")]
    public int lutWidth = 2048;
    [Tooltip("Height of the LUT texture (matches texture resolution)")]
    public int lutHeight = 1024;
    [Tooltip("Pixel → Tile Index Lookup Table (from EquirectLUTBuilder)")]
    public int[] lut;

    [Header("Mode")]
    [Range(0f, 1f)]
    [Tooltip("Morph value: 0 = flat, 1 = globe. Used to select which collider to use.")]
    public float morph; // 0 = flat, 1 = globe

    /// <summary>
    /// Pick a tile index from screen position using UV-based LUT lookup.
    /// This replaces per-tile collider picking with GPU-accelerated LUT lookup.
    /// </summary>
    /// <param name="screenPos">Screen position (e.g., Input.mousePosition)</param>
    /// <param name="tileIndex">Output tile index (-1 if not found)</param>
    /// <param name="hitWorldPos">Output world position of hit point</param>
    /// <returns>True if a tile was picked, false otherwise</returns>
    public bool TryPickTileIndex(Vector2 screenPos, out int tileIndex, out Vector3 hitWorldPos)
    {
        tileIndex = -1;
        hitWorldPos = Vector3.zero;

        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null) return false;
        if (lut == null || lut.Length != lutWidth * lutHeight) return false;

        var ray = targetCamera.ScreenPointToRay(screenPos);
        
        // Select collider based on morph state
        // If morph mesh is available, use it (works at any morph value)
        // Otherwise, use flat or globe collider based on morph threshold
        Collider col = null;
        if (morphMeshCollider != null)
        {
            // Morph mesh handles all morph values (0 to 1)
            col = morphMeshCollider;
        }
        else
        {
            // Fallback: use flat or globe collider based on morph threshold
            col = (morph < 0.5f) ? flatMapCollider : globeCollider;
        }
        
        if (col == null) return false;

        // Raycast against the selected collider
        if (!col.Raycast(ray, out var hit, 50000f))
            return false;

        // Get UV coordinates from hit point
        Vector2 uv = hit.textureCoord;
        
        // Wrap U for equirectangular horizontal repeat; clamp V
        uv.x = Mathf.Repeat(uv.x, 1f);
        uv.y = Mathf.Clamp01(uv.y);

        // Convert UV to pixel coordinates
        int x = Mathf.Clamp(Mathf.FloorToInt(uv.x * lutWidth), 0, lutWidth - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(uv.y * lutHeight), 0, lutHeight - 1);
        int pixelIndex = y * lutWidth + x;
        
        // Bounds check
        if (pixelIndex < 0 || pixelIndex >= lut.Length) return false;

        // Lookup tile index from LUT (GPU-accelerated lookup)
        tileIndex = lut[pixelIndex];
        hitWorldPos = hit.point;
        
        return tileIndex >= 0;
    }
}

