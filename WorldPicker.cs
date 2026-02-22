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

    [Header("Displacement-Aware Picking")]
    [Tooltip("Iterations used to solve ray vs displaced heightfield. Higher = more accurate, slightly more CPU.")]
    [Range(0, 12)]
    public int displacementSolveIterations = 6;
    [Tooltip("Stop iterating early once UV stabilizes within this threshold.")]
    [Range(0.000001f, 0.01f)]
    public float displacementUvEpsilon = 0.0005f;

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

        // Convert hit point to UV coordinates.
        // Prefer hit.textureCoord (mesh UVs) to avoid mismatches due to transform scaling.
        float u = hit.textureCoord.x;
        float v = hit.textureCoord.y;

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

        // --- Displacement-aware correction (matches shader vertex displacement) ---
        // The map is a heightfield: y = baseY + height(u,v)*elevationScale, with x/z mapped linearly to u/v.
        // To pick the exact visible tile at angled views, solve for the ray parameter t such that:
        //   ray(t).y == baseY + height(uv(ray(t))).r * elevationScale
        // We do a short fixed-point iteration: infer Y from height(u,v), intersect ray with that Y-plane,
        // recompute u/v from the new x/z, repeat until stable.
        if (heightmapTexture != null && displacementSolveIterations > 0)
        {
            float baseY = flatMapCollider.transform.position.y;
            float dirY = ray.direction.y;
            if (Mathf.Abs(dirY) > 1e-6f)
            {
                float prevU = u;
                float prevV = v;
                Vector3 correctedHit = hitWorldPos;

                int iters = Mathf.Clamp(displacementSolveIterations, 1, 12);
                for (int i = 0; i < iters; i++)
                {
                    float rawElevation = heightmapTexture.GetPixelBilinear(u, v).r;
                    float correctedY = baseY + rawElevation * elevationScale;

                    float correctedDist = (correctedY - ray.origin.y) / dirY;
                    if (correctedDist <= 0f || float.IsNaN(correctedDist) || float.IsInfinity(correctedDist))
                        break;

                    correctedHit = ray.GetPoint(correctedDist);

                    // Convert corrected hit point back to UVs on the underlying flat quad mapping.
                    // Collider local X/Z span [-mapWidth/2, +mapWidth/2] and [-mapHeight/2, +mapHeight/2].
                    Vector3 correctedLocal = flatMapCollider.transform.InverseTransformPoint(correctedHit);
                    u = Mathf.Repeat((correctedLocal.x / mapWidth) + 0.5f, 1f);
                    v = Mathf.Clamp01((correctedLocal.z / mapHeight) + 0.5f);

                    if (Mathf.Abs(u - prevU) <= displacementUvEpsilon && Mathf.Abs(v - prevV) <= displacementUvEpsilon)
                        break;

                    prevU = u;
                    prevV = v;
                }

                // Recompute LUT lookup from corrected UVs
                int cpx = Mathf.Clamp(Mathf.FloorToInt(u * lutWidth), 0, lutWidth - 1);
                int cpy = Mathf.Clamp(Mathf.FloorToInt(v * lutHeight), 0, lutHeight - 1);
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
