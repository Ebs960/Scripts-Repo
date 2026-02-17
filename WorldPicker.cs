using UnityEngine;

/// <summary>
/// CPU-based tile picker for the flat map.
/// Raycasts against the flat map collider, converts the hit point to UV,
/// and looks up the tile index from the pre-built LUT array.
/// Uses the same heightmap texture as the terrain shader for accurate
/// height correction at angled camera views.
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
    public int lutHeight = 2048;
    [Tooltip("Pixel → Tile Index Lookup Table (from EquirectLUTBuilder)")]
    public int[] lut;

    [Header("Map Bounds (auto-set by HexMapChunkManager)")]
    public float mapWidth = 360f;
    public float mapHeight = 180f;

    [Header("Heightmap (auto-set by HexMapChunkManager)")]
    [Tooltip("The same heightmap texture the terrain shader uses for vertex displacement.")]
    public Texture2D heightmapTexture;
    [Tooltip("Matches the shader's _ElevationScale — multiplied with the raw heightmap value to get world-space displacement.")]
    public float elevationScale = 1f;

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
    /// Pick a tile index from screen position using raycast + CPU LUT lookup.
    /// This replaces per-tile collider picking with a single collider + LUT.
    /// </summary>
    /// <param name="screenPos">Screen position (e.g., Input.mousePosition)</param>
    /// <param name="tileIndex">Output tile index (-1 if not found)</param>
    /// <param name="hitWorldPos">Output world position of hit point</param>
    /// <returns>True if a valid tile was picked, false otherwise</returns>
    public bool TryPickTileIndex(Vector2 screenPos, out int tileIndex, out Vector3 hitWorldPos)
    {
        tileIndex = -1;
        hitWorldPos = Vector3.zero;

        // Ensure camera reference
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null) targetCamera = FindAnyObjectByType<Camera>();
        if (targetCamera == null) return false;

        if (lut == null || lut.Length == 0)
        {
            if (debugLog) Debug.LogWarning("[WorldPicker] LUT is null or empty");
            return false;
        }

        if (flatMapCollider == null)
        {
            if (debugLog) Debug.LogWarning("[WorldPicker] flatMapCollider is null");
            return false;
        }

        // Screen pixel check — skip redundant raycasts when mouse hasn't moved
        int spx = Mathf.RoundToInt(screenPos.x);
        int spy = Mathf.RoundToInt(screenPos.y);
        if (spx == lastScreenPx && spy == lastScreenPy)
        {
            tileIndex = cachedTileIndex;
            hitWorldPos = cachedHitWorldPos;
            return tileIndex >= 0;
        }

        // Raycast from camera through screen position against the flat map collider
        Ray ray = targetCamera.ScreenPointToRay(screenPos);
        if (!flatMapCollider.Raycast(ray, out RaycastHit hit, maxRaycastDistance))
        {
            // Mouse is not over the map — cache the miss result
            lastScreenPx = spx;
            lastScreenPy = spy;
            cachedTileIndex = -1;
            cachedHitWorldPos = Vector3.zero;
            return false;
        }

        hitWorldPos = hit.point;

        // Convert world hit point to UV coordinates.
        // The collider mesh spans [-mapWidth/2, mapWidth/2] x [-mapHeight/2, mapHeight/2] in local space.
        Vector3 localPos = flatMapCollider.transform.InverseTransformPoint(hit.point);
        float u = (localPos.x / mapWidth) + 0.5f;
        float v = (localPos.z / mapHeight) + 0.5f;

        // Wrap U horizontally (cylindrical map), clamp V vertically
        u = Mathf.Repeat(u, 1f);
        v = Mathf.Clamp01(v);

        // LUT pixel lookup
        int px = Mathf.Clamp(Mathf.FloorToInt(u * lutWidth), 0, lutWidth - 1);
        int py = Mathf.Clamp(Mathf.FloorToInt(v * lutHeight), 0, lutHeight - 1);
        int pixelIndex = py * lutWidth + px;

        if (pixelIndex >= 0 && pixelIndex < lut.Length)
        {
            tileIndex = lut[pixelIndex];
        }

        // --- Height correction for angled camera views ---
        // The flat picking collider doesn't account for shader-based terrain displacement,
        // so at angled views the ray hits the plane "ahead" of where the elevated terrain
        // appears visually. Sample the exact same heightmap the shader uses, multiply by
        // the same _ElevationScale, and re-intersect the ray at the corrected height.
        if (heightmapTexture != null)
        {
            float rawElevation = heightmapTexture.GetPixelBilinear(u, v).r;
            float worldDisplacement = rawElevation * elevationScale;

            if (Mathf.Abs(worldDisplacement) > 0.01f)
            {
                float correctedY = flatMapCollider.transform.position.y + worldDisplacement;
                Plane elevatedPlane = new Plane(Vector3.up, new Vector3(0f, correctedY, 0f));
                if (elevatedPlane.Raycast(ray, out float correctedDist))
                {
                    Vector3 correctedHit = ray.GetPoint(correctedDist);
                    Vector3 correctedLocal = flatMapCollider.transform.InverseTransformPoint(correctedHit);
                    float cu = (correctedLocal.x / mapWidth) + 0.5f;
                    float cv = (correctedLocal.z / mapHeight) + 0.5f;
                    cu = Mathf.Repeat(cu, 1f);
                    cv = Mathf.Clamp01(cv);
                    int cpx = Mathf.Clamp(Mathf.FloorToInt(cu * lutWidth), 0, lutWidth - 1);
                    int cpy = Mathf.Clamp(Mathf.FloorToInt(cv * lutHeight), 0, lutHeight - 1);
                    int cpixelIndex = cpy * lutWidth + cpx;
                    if (cpixelIndex >= 0 && cpixelIndex < lut.Length)
                    {
                        int correctedTileIndex = lut[cpixelIndex];
                        if (correctedTileIndex >= 0)
                        {
                            tileIndex = correctedTileIndex;
                            hitWorldPos = correctedHit;
                        }
                    }
                }
            }
        }

        // Only log when tile actually changes (no per-frame spam)
        if (debugLog && tileIndex != cachedTileIndex)
        {
            Debug.Log($"[WorldPicker] Picked tileIndex={tileIndex} uv=({u:F3},{v:F3}) lutPx=({px},{py}) worldHit={hitWorldPos}");
        }

        // Cache result
        lastScreenPx = spx;
        lastScreenPy = spy;
        cachedTileIndex = tileIndex;
        cachedHitWorldPos = hitWorldPos;

        return tileIndex >= 0;
    }
}
