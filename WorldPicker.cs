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
    private Vector3 lastCameraPosition = new Vector3(float.NaN, float.NaN, float.NaN);
    private Quaternion lastCameraRotation = Quaternion.identity;
    private HexMapChunkManager cachedChunkManager;
    private LayerManager cachedLayerManager;
    private GameManager.PlanetLayerType lastActiveLayer = GameManager.PlanetLayerType.Surface;

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
        lastCameraPosition = new Vector3(float.NaN, float.NaN, float.NaN);
        lastCameraRotation = Quaternion.identity;
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
        if (targetCamera == null) return false;
        if (lut == null || lut.Length == 0) return false;

        int spx = Mathf.RoundToInt(screenPos.x);
        int spy = Mathf.RoundToInt(screenPos.y);
        Vector3 cameraPosition = targetCamera.transform.position;
        Quaternion cameraRotation = targetCamera.transform.rotation;
        bool cameraUnchanged =
            !float.IsNaN(lastCameraPosition.x) &&
            (cameraPosition - lastCameraPosition).sqrMagnitude <= 0.000001f &&
            Quaternion.Angle(cameraRotation, lastCameraRotation) <= 0.001f;

        if (spx == lastScreenPx && spy == lastScreenPy && cameraUnchanged)
        {
            tileIndex = cachedTileIndex;
            hitWorldPos = cachedHitWorldPos;
            return tileIndex >= 0;
        }

        if (cachedChunkManager == null)
            cachedChunkManager = FindAnyObjectByType<HexMapChunkManager>();

        // Invalidate cache when active view layer changes (different collider = different hits)
        if (cachedLayerManager == null)
            cachedLayerManager = FindAnyObjectByType<LayerManager>();
        if (cachedLayerManager != null)
        {
            var currentLayer = cachedLayerManager.ActiveViewLayer;
            if (currentLayer != lastActiveLayer)
            {
                lastActiveLayer = currentLayer;
                lastScreenPx = -1; // force re-raycast
            }
        }

        Ray ray = targetCamera.ScreenPointToRay(screenPos);
        if (!TryRaycastTerrain(ray, out RaycastHit hit, out hitWorldPos))
        {
            lastScreenPx = spx;
            lastScreenPy = spy;
            cachedTileIndex = -1;
            cachedHitWorldPos = Vector3.zero;
            lastCameraPosition = cameraPosition;
            lastCameraRotation = cameraRotation;
            return false;
        }

        float u = Mathf.Repeat(hit.textureCoord.x, 1f);
        float v = Mathf.Clamp01(hit.textureCoord.y);
        int px = Mathf.Clamp(Mathf.FloorToInt(u * lutWidth), 0, lutWidth - 1);
        int py = Mathf.Clamp(Mathf.FloorToInt(v * lutHeight), 0, lutHeight - 1);
        int pixelIndex = py * lutWidth + px;
        if (pixelIndex >= 0 && pixelIndex < lut.Length)
            tileIndex = lut[pixelIndex];

        lastScreenPx = spx;
        lastScreenPy = spy;
        cachedTileIndex = tileIndex;
        cachedHitWorldPos = hitWorldPos;
        lastCameraPosition = cameraPosition;
        lastCameraRotation = cameraRotation;

        if (debugLog && tileIndex >= 0)
            Debug.Log($"[WorldPicker] Picked tileIndex={tileIndex} uv=({u:F3},{v:F3}) worldHit={hitWorldPos}");

        return tileIndex >= 0;
    }

    private bool TryRaycastTerrain(Ray screenRay, out RaycastHit bestHit, out Vector3 bestWorldPoint)
    {
        RaycastHit localBestHit = default;
        Vector3 localBestWorldPoint = Vector3.zero;

        Vector3 direction = screenRay.direction.normalized;
        float bestDistanceAlongRay = float.PositiveInfinity;
        float bestPerpDistanceSq = float.PositiveInfinity;
        bool found = false;

        void ConsiderCandidate(Ray candidateRay, Vector3 worldOffset)
        {
            if (!TryRaycastSingle(candidateRay, out RaycastHit hit))
                return;

            Vector3 candidateWorldPoint = hit.point + worldOffset;
            float alongRay = Vector3.Dot(candidateWorldPoint - screenRay.origin, direction);
            if (alongRay < 0f || alongRay > maxRaycastDistance)
                return;

            Vector3 projectedPoint = screenRay.origin + direction * alongRay;
            float perpDistanceSq = (candidateWorldPoint - projectedPoint).sqrMagnitude;

            if (!found || perpDistanceSq < bestPerpDistanceSq - 0.0001f ||
                (Mathf.Abs(perpDistanceSq - bestPerpDistanceSq) <= 0.0001f && alongRay < bestDistanceAlongRay))
            {
                found = true;
                bestPerpDistanceSq = perpDistanceSq;
                bestDistanceAlongRay = alongRay;
                localBestHit = hit;
                localBestWorldPoint = candidateWorldPoint;
            }
        }

        if (cachedChunkManager == null || !cachedChunkManager.WrapEnabled || cachedChunkManager.MapWidth <= 0.001f)
        {
            ConsiderCandidate(screenRay, Vector3.zero);
            bestHit = localBestHit;
            bestWorldPoint = localBestWorldPoint;
            return found;
        }

        float mapWidth = cachedChunkManager.MapWidth;
        Vector3 wrapOffset = cachedChunkManager.transform.TransformVector(new Vector3(mapWidth, 0f, 0f));
        Vector3 localRayOrigin = cachedChunkManager.transform.InverseTransformPoint(screenRay.origin);
        int nearestWrapMultiple = Mathf.RoundToInt(localRayOrigin.x / mapWidth);

        // Test the canonical wrapped sheet first, then adjacent sheets around the seam.
        for (int delta = -1; delta <= 1; delta++)
        {
            int wrapMultiple = nearestWrapMultiple + delta;
            Vector3 rayShift = -wrapOffset * wrapMultiple;
            ConsiderCandidate(new Ray(screenRay.origin + rayShift, screenRay.direction), -rayShift);
        }

        bestHit = localBestHit;
        bestWorldPoint = localBestWorldPoint;
        return found;
    }

    private bool TryRaycastSingle(Ray ray, out RaycastHit hit)
    {
        hit = default;
        if (cachedChunkManager == null)
            return Physics.Raycast(ray, out hit, maxRaycastDistance, pickingLayerMask);

        if (cachedLayerManager == null)
            cachedLayerManager = Object.FindAnyObjectByType<LayerManager>();

        var layer = cachedLayerManager != null
            ? cachedLayerManager.ActiveViewLayer
            : GameManager.PlanetLayerType.Surface;

        // Orbit: single collider at orbit height
        if (layer == GameManager.PlanetLayerType.Orbit)
        {
            var oc = cachedChunkManager.OrbitPickingCollider;
            if (oc != null)
                return oc.Raycast(ray, out hit, maxRaycastDistance);
            return cachedChunkManager.PickingCollider != null
                && cachedChunkManager.PickingCollider.Raycast(ray, out hit, maxRaycastDistance);
        }

        // Surface / Underwater: try BOTH terrain and water colliders, keep nearest.
        // Land tiles: terrain collider is above water → closer hit wins.
        // Water tiles: water collider is above seafloor → closer hit wins.
        Collider terrainCol = cachedChunkManager.PickingCollider;
        Collider waterCol = cachedChunkManager.WaterPickingCollider;

        RaycastHit terrainHit = default;
        RaycastHit waterHit = default;

        bool hitTerrain = terrainCol != null && terrainCol.Raycast(ray, out terrainHit, maxRaycastDistance);
        bool hitWater = waterCol != null && waterCol.Raycast(ray, out waterHit, maxRaycastDistance);

        if (hitTerrain && hitWater)
        {
            // Pick whichever is closer to the camera (smaller distance along ray)
            if (waterHit.distance < terrainHit.distance)
                hit = waterHit;
            else
                hit = terrainHit;
            return true;
        }
        if (hitTerrain) { hit = terrainHit; return true; }
        if (hitWater) { hit = waterHit; return true; }

        return Physics.Raycast(ray, out hit, maxRaycastDistance, pickingLayerMask);
    }
}
