using UnityEngine;

/// <summary>
/// CPU-based tile picker for the flat map.
/// Raycasts against the displaced terrain MeshCollider(s), converts the
/// hit UV to a LUT pixel, and returns the tile index.
/// Works at any camera angle (top-down, oblique, or ground-level).
/// </summary>
public class WorldPicker : MonoBehaviour
{
    [Header("References")]
    public Camera targetCamera;

    [Header("Picking Layer")]
    [Tooltip("Layer mask used for terrain picking raycasts. Set automatically by HexMapChunkManager.")]
    public LayerMask pickingLayerMask = ~0; // default: everything

    [Header("LUT")]
    [Tooltip("Width of the LUT texture (matches texture resolution)")]
    public int lutWidth = 2048;
    [Tooltip("Height of the LUT texture (matches texture resolution)")]
    public int lutHeight = 2048;
    [Tooltip("Pixel → Tile Index Lookup Table (from EquirectLUTBuilder)")]
    public int[] lut;

    [Header("Debug")]
    public bool debugLog = false;

    [Header("Raycast")]
    [Tooltip("Maximum raycast distance for picking")]
    public float maxRaycastDistance = 10000f;

    // Cache to avoid redundant raycasts when mouse hasn't moved
    private int lastScreenPx = -1;
    private int lastScreenPy = -1;
    private int cachedTileIndex = -1;
    private Vector3 cachedHitWorldPos = Vector3.zero;

    /// <summary>
    /// Invalidate the screen-pixel cache. Call when switching planets, changing LUT,
    /// or any event that makes the cached tile index stale.
    /// </summary>
    public void InvalidateCache()
    {
        lastScreenPx = -1;
        lastScreenPy = -1;
        cachedTileIndex = -1;
        cachedHitWorldPos = Vector3.zero;
    }

    /// <summary>
    /// Pick a tile index from screen position using raycast + CPU LUT lookup.
    /// Uses Physics.Raycast against the displaced terrain MeshCollider so
    /// picking is accurate at every camera angle, including ground level.
    /// </summary>
    public bool TryPickTileIndex(Vector2 screenPos, out int tileIndex, out Vector3 hitWorldPos)
    {
        tileIndex = -1;
        hitWorldPos = Vector3.zero;

        // Ensure camera reference
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null) targetCamera = FindAnyObjectByType<Camera>();
        if (targetCamera == null)
        {
            Debug.LogWarning("[WorldPicker] No camera found for picking!");
            return false;
        }

        if (lut == null || lut.Length == 0)
        {
            Debug.LogWarning("[WorldPicker] LUT is null or empty!");
            return false;
        }

        // Screen pixel check — skip redundant raycasts when mouse hasn't moved
        int spx = Mathf.RoundToInt(screenPos.x);
        int spy = Mathf.RoundToInt(screenPos.y);
        if (spx == lastScreenPx && spy == lastScreenPy)
        {
            tileIndex = cachedTileIndex;
            hitWorldPos = cachedHitWorldPos;
            if (debugLog) Debug.Log($"[WorldPicker] Used cached tileIndex={tileIndex} at screen ({spx},{spy})");
            return tileIndex >= 0;
        }

        // Raycast against the displaced terrain collider(s) using layer mask
        Ray ray = targetCamera.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hit, maxRaycastDistance, pickingLayerMask))
        {
            lastScreenPx = spx;
            lastScreenPy = spy;
            cachedTileIndex = -1;
            cachedHitWorldPos = Vector3.zero;
            Debug.LogWarning($"[WorldPicker] Raycast miss at screen ({spx},{spy})");
            return false;
        }

        hitWorldPos = hit.point;

        // Convert hit point to UV coordinates via mesh UVs — authoritative.
        // The MeshCollider's mesh has UVs mapped 0-1 across the map, matching the LUT.
        float u = Mathf.Repeat(hit.textureCoord.x, 1f);
        float v = Mathf.Clamp01(hit.textureCoord.y);

        // LUT pixel lookup
        int px = Mathf.Clamp(Mathf.FloorToInt(u * lutWidth), 0, lutWidth - 1);
        int py = Mathf.Clamp(Mathf.FloorToInt(v * lutHeight), 0, lutHeight - 1);
        int pixelIndex = py * lutWidth + px;

        if (debugLog)
        {
            Debug.Log($"[WorldPicker] Raycast hit {hit.collider?.name} at world {hit.point}, uv=({u:F3},{v:F3}), px={px}, py={py}, pixelIndex={pixelIndex}");
        }

        if (pixelIndex >= 0 && pixelIndex < lut.Length)
        {
            tileIndex = lut[pixelIndex];
        }
        else
        {
            Debug.LogWarning($"[WorldPicker] Pixel index {pixelIndex} out of LUT bounds (lut.Length={lut.Length})");
        }

        // Only log when tile actually changes
        if (debugLog && tileIndex != cachedTileIndex)
            Debug.Log($"[WorldPicker] Picked tileIndex={tileIndex} uv=({u:F3},{v:F3}) worldHit={hitWorldPos}");

        // Cache result
        lastScreenPx = spx;
        lastScreenPy = spy;
        cachedTileIndex = tileIndex;
        cachedHitWorldPos = hitWorldPos;

        if (tileIndex < 0)
        {
            Debug.LogWarning($"[WorldPicker] LUT lookup returned invalid tileIndex at px={px}, py={py}, uv=({u:F3},{v:F3})");
        }

        return tileIndex >= 0;
    }
}
