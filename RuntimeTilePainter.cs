using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public class TilePaintEvent : UnityEvent<int> { }

// Runtime tool to paint tiles by clicking in Play mode. It attempts to call common
// setters on the `PlanetGenerator` via reflection. If no setter is found, it
// emits the `onTilePainted` UnityEvent so you can hook your own handler in the inspector.
public class RuntimeTilePainter : MonoBehaviour
{
    [Header("Required")]
    public HexGrid hexGrid;
    public HexGridComponent hexGridComponent;

    [Header("Optional")]
    public PlanetGenerator planetGenerator;
    public HexMapChunkManager hexMapChunkManager;

    [Header("Paint")]
    public Biome paintBiome = Biome.Temperate;
    public bool rebuildOnPaint = true;

    public TilePaintEvent onTilePainted = new TilePaintEvent();

    private Camera _cam;

    void Start()
    {
        _cam = Camera.main;
        if (_cam == null) _cam = Camera.current;
        if (hexGrid == null) Debug.LogWarning("RuntimeTilePainter: hexGrid not assigned.");
    }

    void Update()
    {
        if (hexGrid == null && hexGridComponent == null) return;
        if (Input.GetMouseButtonDown(0))
        {
            if (_cam == null) return;
            var ray = _cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit, 10000f))
            {
                TryPaintAtPosition(hit.point);
            }
            else
            {
                // Raycast to XZ plane as fallback
                if (Mathf.Abs(_cam.transform.forward.y) > 0.001f)
                {
                    Plane p = new Plane(Vector3.up, Vector3.zero);
                    if (p.Raycast(ray, out var enter))
                    {
                        var pt = ray.GetPoint(enter);
                        TryPaintAtPosition(pt);
                    }
                }
            }
        }
    }

    private void TryPaintAtPosition(Vector3 worldPos)
    {
        int tile = -1;
        if (hexGridComponent != null) tile = hexGridComponent.GetTileAtPosition(worldPos);
        else if (hexGrid != null) tile = hexGrid.GetTileAtPosition(worldPos);
        if (tile < 0) return;

        // Try to call common setter signatures on PlanetGenerator
        bool handled = false;
        if (planetGenerator != null)
        {
            var t = planetGenerator.GetType();
            var m1 = t.GetMethod("SetTileBiome", new System.Type[] { typeof(int), typeof(Biome) });
            if (m1 != null)
            {
                try { m1.Invoke(planetGenerator, new object[] { tile, paintBiome }); handled = true; }
                catch { handled = false; }
            }

            if (!handled)
            {
                // try integer overload
                var m2 = t.GetMethod("SetTileBiome", new System.Type[] { typeof(int), typeof(int) });
                if (m2 != null)
                {
                    try { m2.Invoke(planetGenerator, new object[] { tile, (int)paintBiome }); handled = true; }
                    catch { handled = false; }
                }
            }
        }

        if (!handled)
        {
            // fallback event so user scripts can subscribe
            onTilePainted?.Invoke(tile);
        }

        if (rebuildOnPaint && hexMapChunkManager != null)
        {
            // Try to call a chunk rebuild method; fall back to full rebuild
            bool rebuilt = false;
            var t = hexMapChunkManager.GetType();
            var methodNames = new string[] { "RebuildChunkForTile", "RebuildChunkContainingTile", "Rebuild" };
            foreach (var name in methodNames)
            {
                var mi = t.GetMethod(name, new System.Type[] { typeof(int) });
                if (mi != null)
                {
                    try { mi.Invoke(hexMapChunkManager, new object[] { tile }); rebuilt = true; break; }
                    catch { }
                }
            }

            if (!rebuilt)
            {
                // try Rebuild(PlanetGenerator)
                var mi2 = t.GetMethod("Rebuild", new System.Type[] { typeof(PlanetGenerator) });
                if (mi2 != null)
                {
                    try { mi2.Invoke(hexMapChunkManager, new object[] { planetGenerator }); }
                    catch { }
                }
            }
        }

        Debug.Log($"RuntimeTilePainter: Painted tile {tile} -> {paintBiome} (handled={planetGenerator != null && handled})");
    }
}
