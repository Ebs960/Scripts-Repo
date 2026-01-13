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
    
    [Header("Map Bounds (auto-set by HexMapChunkManager)")]
    public float mapWidth = 360f;
    public float mapHeight = 180f;
    
    [Header("Debug")]
    public bool debugLog = false;

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
        if (lut == null || lut.Length == 0)
        {
            if (debugLog) Debug.LogWarning("[WorldPicker] LUT is null or empty");
            return false;
        }

        var ray = targetCamera.ScreenPointToRay(screenPos);
        if (flatMapCollider == null) 
        {
            if (debugLog) Debug.LogWarning("[WorldPicker] flatMapCollider is null");
            return false;
        }

        // Raycast against the flat map collider
        if (!flatMapCollider.Raycast(ray, out var hit, 50000f))
            return false;

        hitWorldPos = hit.point;
        
        // Get UV coordinates - try textureCoord first, fall back to world position calculation
        Vector2 uv = hit.textureCoord;
        
        // If textureCoord returns (0,0), it might not be working - use world position fallback
        if (uv.sqrMagnitude < 0.0001f && mapWidth > 0 && mapHeight > 0)
        {
            // Calculate UV from world position (assuming map centered at origin)
            // Transform hit point to collider's local space
            Vector3 localHit = flatMapCollider.transform.InverseTransformPoint(hit.point);
            uv.x = (localHit.x / mapWidth) + 0.5f;
            uv.y = (localHit.z / mapHeight) + 0.5f;
            
            if (debugLog) Debug.Log($"[WorldPicker] Using world position fallback: localHit={localHit}, uv={uv}");
        }

        // Wrap U for horizontal repeat; clamp V
        uv.x = Mathf.Repeat(uv.x, 1f);
        uv.y = Mathf.Clamp01(uv.y);

        // Convert UV to pixel coordinates
        int x = Mathf.Clamp(Mathf.FloorToInt(uv.x * lutWidth), 0, lutWidth - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(uv.y * lutHeight), 0, lutHeight - 1);
        int pixelIndex = y * lutWidth + x;

        // Bounds check
        if (pixelIndex < 0 || pixelIndex >= lut.Length)
        {
            if (debugLog) Debug.LogWarning($"[WorldPicker] Pixel index {pixelIndex} out of bounds (lut.Length={lut.Length})");
            return false;
        }

        // Lookup tile index from LUT
        tileIndex = lut[pixelIndex];
        
        if (debugLog && tileIndex >= 0) Debug.Log($"[WorldPicker] Picked tile {tileIndex} at uv={uv}, pixel=({x},{y})");

        return tileIndex >= 0;
    }
}
