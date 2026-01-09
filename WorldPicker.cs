using UnityEngine;

/// <summary>
/// GPU-based UV/LUT picker for texture-planet mode.
/// Replaces per-tile colliders with a single collider + LUT lookup.
/// </summary>
public class WorldPicker : MonoBehaviour
{
    [Header("References")]
    public Camera targetCamera;
    public Collider flatMapCollider;

    [Header("LUT")]
    [Tooltip("Width of the LUT texture (matches texture resolution)")]
    public int lutWidth = 2048;
    [Tooltip("Height of the LUT texture (matches texture resolution)")]
    public int lutHeight = 1024;
    [Tooltip("Pixel → Tile Index Lookup Table (from EquirectLUTBuilder)")]
    public int[] lut;

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
        if (flatMapCollider == null) return false;

        // Raycast against the flat map collider
        if (!flatMapCollider.Raycast(ray, out var hit, 50000f))
            return false;

        // Get UV coordinates from hit point
        Vector2 uv = hit.textureCoord;

        // Wrap U for horizontal repeat; clamp V
        uv.x = Mathf.Repeat(uv.x, 1f);
        uv.y = Mathf.Clamp01(uv.y);

        // Convert UV to pixel coordinates
        int x = Mathf.Clamp(Mathf.FloorToInt(uv.x * lutWidth), 0, lutWidth - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(uv.y * lutHeight), 0, lutHeight - 1);
        int pixelIndex = y * lutWidth + x;

        // Bounds check
        if (pixelIndex < 0 || pixelIndex >= lut.Length) return false;

        // Lookup tile index from LUT
        tileIndex = lut[pixelIndex];
        hitWorldPos = hit.point;

        return tileIndex >= 0;
    }
}
