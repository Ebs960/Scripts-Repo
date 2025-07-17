// Assets/Scripts/Hexasphere/HexasphereTest.cs
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Lightweight playground to visualise a hex‑sphere with procedural elevation.
/// Requires no PlanetGenerator or biome data – all elevation is local Perlin noise.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class HexasphereTest : MonoBehaviour
{
    /* ───────────────────── STATIC LOOK‑UP ───────────────────── */
    // tiles = 10 * (2^(depth‑1))^2 + 2   (depth 1 → 12, depth 2 → 42, etc.)
    private static readonly int[] TilesPerDepth;
    static HexasphereTest()
    {
        const int MAX = 20;
        TilesPerDepth = new int[MAX + 1];
        for (int d = 1; d <= MAX; d++)
        {
            int f = 1 << (d - 1);          // 2^(d‑1)
            TilesPerDepth[d] = 10 * f * f + 2;
        }
    }

    /* ───────────────────── INSPECTOR FIELDS ─────────────────── */
    [Header("Grid")]
    [Range(1, 20)] public int  subdivisions = 4;
    public float radius = 25f;

    [Header("Elevation (Perlin)")]
    public float noiseFrequency = 4f;          // higher → smaller landforms
    [Range(0f, 0.30f)]
    public float maxElevation   = 0.10f;       // fraction of radius

    [Header("Run Control")]
    public bool runOnStart = true;

    /* ───────────────────── INTERNAL STATE ───────────────────── */
    private SphericalHexGrid grid;
    private readonly List<GameObject> debugObjs = new();

    /* ───────────────────── LIFECYCLE ─────────────────────────── */
    private void Start()
    {
        if (runOnStart) RunTest();
    }

    [ContextMenu("Run Hexasphere Test")]
    public void RunTest()
    {
        Debug.Log($"[HexasphereTest] depth={subdivisions}  (~{TilesPerDepth[subdivisions]} tiles)");

        ClearDebug();
        BuildGrid();
        BuildRenderer();
        // optional: CreateDebugGeometry();
    }

    /* ───────────────────── BUILD GRID ───────────────────────── */
    private void BuildGrid()
    {
        grid = new SphericalHexGrid();
        grid.GenerateFromSubdivision(subdivisions, radius);

        Debug.Log($"Generated grid: Tiles={grid.TileCount}  Pentagons={grid.pentagonIndices.Count}");
    }

    /* ───────────────────── BUILD RENDERER ───────────────────── */
    private void BuildRenderer()
    {
        // Ensure a HexasphereRenderer component exists
        var renderer = GetComponent<HexasphereRenderer>();
        if (renderer == null) renderer = gameObject.AddComponent<HexasphereRenderer>();

        renderer.usePerTileBiomeData = false;      // we’re not dealing with biomes here
        renderer.useSeparateVertices = false;

        // Generate noise‑based elevation array
        float[] elev = new float[grid.TileCount];
        for (int i = 0; i < elev.Length; i++)
        {
            Vector3 p = grid.tileCenters[i].normalized * noiseFrequency;
            elev[i]   = Mathf.PerlinNoise(p.x + 0.5f, p.y + 0.5f) * maxElevation; // 0‑maxElev
        }

        // Spawn dummy generator & wire it up
        var dummy = gameObject.AddComponent<DummyElevationGenerator>();
        dummy.elevations = elev;
        renderer.generatorSource = dummy;

        // Build mesh & displace vertices
        renderer.BuildMesh(grid);
        renderer.ApplyHeightDisplacement(radius);
    }

    /* ───────────────────── OPTIONAL DEBUG VISUALS ───────────── */
    // Add your tile‑centre / neighbor / corner gizmo code here if desired.

    private void ClearDebug()
    {
        foreach (var go in debugObjs) if (go) DestroyImmediate(go);
        debugObjs.Clear();
    }
}

/* -------------------------------------------------------------- *
 *  Minimal elevation provider so HexasphereRenderer has something
 *  to query.  Lives in the same file for convenience, but feel
 *  free to split it out into DummyElevationGenerator.cs
 * -------------------------------------------------------------- */
public class DummyElevationGenerator : MonoBehaviour, IHexasphereGenerator
{
    public float[] elevations;                            // filled by HexasphereTest

    public float GetTileElevation(int i) =>
        elevations != null && i < elevations.Length ? elevations[i] : 0f;

    public HexTileData GetHexTileData(int i) => null;     // not needed for this preview

    public List<BiomeSettings> GetBiomeSettings() => new(); // empty list satisfies renderer
}
