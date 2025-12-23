using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Presentation-only flat equirectangular map renderer backed by the spherical tile grid.
/// 
/// This creates a DUPLICATE set of tile visuals laid out flat - the globe tiles are NOT touched.
/// Both views remain independent and can be shown/hidden separately.
/// 
/// Non-negotiable rules satisfied:
/// - Does NOT compute adjacency / neighbors.
/// - Visual tiles preserve spherical tileIndex identity.
/// - Positions tiles deterministically using equirectangular projection from spherical tile centers.
/// </summary>
public class FlatMapRenderer : MonoBehaviour
{
    [Header("Tile Source")]
    [Tooltip("When true, clones the planet's existing tile visuals (including decorations/meshes/textures). When false, uses tilePrefab for all tiles.")]
    [SerializeField] private bool clonePlanetTileVisuals = true;

    [Tooltip("Fallback prefab if clonePlanetTileVisuals is false or planet has no tile objects.")]
    [SerializeField] private GameObject tilePrefab;

    [Tooltip("Optional explicit root for spawned tiles (defaults to this transform).")]
    [SerializeField] private Transform tilesRoot;

    [Header("Equirectangular Projection (World Units)")]
    [Tooltip("When enabled, mapWidth/mapHeight are derived from the planet radius (mapWidth = 2πR, mapHeight = πR).")]
    [SerializeField] private bool autoScaleToPlanetRadius = true;

    [Tooltip("Full horizontal span of the map in world units (corresponds to 360° of longitude).")]
    [SerializeField] private float mapWidth = 360f;

    [Tooltip("Full vertical span of the map in world units (corresponds to 180° of latitude).")]
    [SerializeField] private float mapHeight = 180f;

    [Tooltip("Constant Y height for the flat map plane.")]
    [SerializeField] private float flatY = 0f;

    [Header("Tile Layout")]
    [Tooltip("Scale multiplier applied to each cloned tile. Adjust if tiles appear too small/large on the flat map.")]
    [SerializeField] private float tileScaleMultiplier = 1f;

    [Header("Wrapping Visuals")]
    [Tooltip("Duplicate tiles near U=0 and U=1 to create a seamless wrap strip. Set to 0 to disable.")]
    [SerializeField, Range(0f, 0.25f)] private float wrapDuplicateUThreshold = 0.05f;

    [Header("Raycast / Layer")]
    [Tooltip("If >= 0, force all spawned objects (and children) to this layer. If < 0, keep prefab layers.")]
    [SerializeField] private int overrideLayer = -1;

    [Header("Runtime (Read-only)")]
    [SerializeField] private bool isBuilt;

    private readonly Dictionary<int, GameObject> _primaryByTileIndex = new();

    public float MapWidth => mapWidth;
    public float MapHeight => mapHeight;
    public bool IsBuilt => isBuilt;
    public Transform TilesRoot => tilesRoot != null ? tilesRoot : transform;

    public bool TryGetPrimaryTileGO(int tileIndex, out GameObject go) => _primaryByTileIndex.TryGetValue(tileIndex, out go);

    public bool TryGetFlatWorldPosition(int tileIndex, out Vector3 pos)
    {
        if (_primaryByTileIndex.TryGetValue(tileIndex, out var go) && go != null)
        {
            pos = go.transform.position;
            return true;
        }
        pos = default;
        return false;
    }

    public void Clear()
    {
        _primaryByTileIndex.Clear();

        var root = TilesRoot;
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            var child = root.GetChild(i);
            if (child != null) Destroy(child.gameObject);
        }
        isBuilt = false;
    }

    /// <summary>
    /// Rebuild the flat map for the provided planet generator (spherical authority).
    /// Creates duplicate tile visuals - does NOT touch the globe's tiles.
    /// </summary>
    public void Rebuild(PlanetGenerator planetGen)
    {
        if (planetGen == null || planetGen.Grid == null || planetGen.Grid.TileCount <= 0)
        {
            Debug.LogWarning("[FlatMapRenderer] Cannot rebuild: missing planet generator grid.");
            return;
        }

        Clear();

        var root = TilesRoot;
        var grid = planetGen.Grid;
        int tileCount = grid.TileCount;

        if (autoScaleToPlanetRadius)
        {
            float r = Mathf.Max(0.0001f, planetGen.radius);
            mapWidth = 2f * Mathf.PI * r;
            mapHeight = Mathf.PI * r;
        }

        // Build a lookup of planet tile objects by tileIndex for cloning
        Dictionary<int, GameObject> planetTileObjects = null;
        if (clonePlanetTileVisuals)
        {
            planetTileObjects = new Dictionary<int, GameObject>();
            var tilePrefabsParent = planetGen.transform.Find("TilePrefabs");
            if (tilePrefabsParent != null)
            {
                var holders = tilePrefabsParent.GetComponentsInChildren<TileIndexHolder>(true);
                foreach (var h in holders)
                {
                    if (h != null && h.tileIndex >= 0 && h.tileIndex < tileCount)
                    {
                        if (!planetTileObjects.ContainsKey(h.tileIndex))
                            planetTileObjects[h.tileIndex] = h.gameObject;
                    }
                }
            }
        }

        for (int tileIndex = 0; tileIndex < tileCount; tileIndex++)
        {
            // Canonical spherical direction (unit vector)
            Vector3 centerDir = grid.tileCenters[tileIndex].normalized;

            // Canonical lat/lon (degrees)
            float latitude = Mathf.Asin(centerDir.y) * Mathf.Rad2Deg;
            float longitude = Mathf.Atan2(centerDir.x, centerDir.z) * Mathf.Rad2Deg;

            // Canonical equirectangular UV (0..1)
            float u = (longitude + 180f) / 360f;
            float v = (latitude + 90f) / 180f;

            Vector3 flatPos = FlatPositionFromUV(u, v);

            // Get source object for cloning
            GameObject sourceObj = null;
            if (planetTileObjects != null && planetTileObjects.TryGetValue(tileIndex, out var planetTile))
                sourceObj = planetTile;

            var primary = SpawnFlatTile(tileIndex, flatPos, root, sourceObj, isClone: false);
            if (primary != null)
                _primaryByTileIndex[tileIndex] = primary;

            // Wrap strip clones for seamless horizontal wrapping
            if (wrapDuplicateUThreshold > 0f && primary != null)
            {
                if (u < wrapDuplicateUThreshold)
                {
                    var clonePos = flatPos + new Vector3(mapWidth, 0f, 0f);
                    SpawnFlatTile(tileIndex, clonePos, root, sourceObj, isClone: true);
                }
                else if (u > 1f - wrapDuplicateUThreshold)
                {
                    var clonePos = flatPos - new Vector3(mapWidth, 0f, 0f);
                    SpawnFlatTile(tileIndex, clonePos, root, sourceObj, isClone: true);
                }
            }
        }

        isBuilt = true;
        Debug.Log($"[FlatMapRenderer] Built flat map with {_primaryByTileIndex.Count} tiles. MapWidth={mapWidth:F1}, MapHeight={mapHeight:F1}");
    }

    /// <summary>
    /// Deterministic equirectangular mapping:
    /// X = longitude, Z = latitude, Y = constant.
    /// </summary>
    public Vector3 FlatPositionFromUV(float u, float v)
    {
        float x = (u - 0.5f) * mapWidth;
        float z = (v - 0.5f) * mapHeight;
        return new Vector3(x, flatY, z);
    }

    private GameObject SpawnFlatTile(int tileIndex, Vector3 worldPos, Transform parent, GameObject sourceObj, bool isClone)
    {
        GameObject go;

        if (sourceObj != null)
        {
            // Clone the planet's tile object (includes meshes, materials, decorations)
            go = Instantiate(sourceObj, worldPos, Quaternion.identity, parent);
        }
        else if (tilePrefab != null)
        {
            // Fallback to generic prefab
            go = Instantiate(tilePrefab, worldPos, Quaternion.identity, parent);
        }
        else
        {
            return null;
        }

        go.name = isClone ? $"FlatTile_{tileIndex}_Clone" : $"FlatTile_{tileIndex}";

        // Ensure TileIndexHolder for picking
        var holder = go.GetComponent<TileIndexHolder>();
        if (holder == null) holder = go.AddComponent<TileIndexHolder>();
        holder.tileIndex = tileIndex;

        // Remove Rigidbody (presentation only)
        var rb = go.GetComponent<Rigidbody>();
        if (rb != null) Destroy(rb);

        // Lay flat: tiles on globe are oriented radially, on flat map they should face up
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * tileScaleMultiplier;

        // Ensure collider for picking
        EnsureColliderForPicking(go);

        if (overrideLayer >= 0)
            SetLayerRecursively(go, overrideLayer);

        return go;
    }

    private static void EnsureColliderForPicking(GameObject go)
    {
        if (go == null) return;

        // If any collider exists, assume it supports picking
        if (go.GetComponentInChildren<Collider>() != null) return;

        // Try MeshCollider
        var mf = go.GetComponentInChildren<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = false;
            return;
        }

        // Fallback BoxCollider
        var rend = go.GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            var bc = rend.gameObject.AddComponent<BoxCollider>();
            bc.center = rend.gameObject.transform.InverseTransformPoint(rend.bounds.center);
            bc.size = rend.bounds.size;
        }
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        if (go == null) return;
        go.layer = layer;
        var t = go.transform;
        for (int i = 0; i < t.childCount; i++)
        {
            var child = t.GetChild(i);
            if (child != null) SetLayerRecursively(child.gameObject, layer);
        }
    }
}
