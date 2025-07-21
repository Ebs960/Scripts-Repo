// Assets/Scripts/Hexasphere/HexasphereTest.cs
using UnityEngine;
using System.Collections;
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
        StartCoroutine(BuildRenderer());
        // optional: CreateDebugGeometry();
    }

    /* ───────────────────── BUILD GRID ───────────────────────── */
    private void BuildGrid()
    {
        grid = new SphericalHexGrid();
        grid.GenerateFromSubdivision(subdivisions, radius);

        Debug.Log($"Generated grid: Tiles={grid.TileCount}  Pentagons={grid.pentagonIndices.Count}");
    }

    /* ───────────────────── BUILD PREFABS ───────────────────── */
[Header("Prefab Settings")]
public GameObject tilePrefab; // Assign a default prefab in inspector
public int batchSize = 100;

    private System.Collections.IEnumerator BuildRenderer()
    {
        if (tilePrefab == null)
        {
            Debug.LogError("HexasphereTest: No tilePrefab assigned!");
            yield break;
        }

        for (int i = 0; i < grid.TileCount; i++)
        {
            Vector3 tileDir = grid.tileCenters[i].normalized;
            Vector3 worldPos = transform.TransformPoint(tileDir * radius);
            var go = Instantiate(tilePrefab, worldPos, Quaternion.identity, transform);
            go.name = $"Tile_{i}";
            debugObjs.Add(go);

            if (i % batchSize == 0)
                yield return null;
        }
    }

    /* ───────────────────── OPTIONAL DEBUG VISUALS ───────────── */
    // Add your tile‑centre / neighbor / corner gizmo code here if desired.

    private void ClearDebug()
    {
        foreach (var go in debugObjs) if (go) DestroyImmediate(go);
        debugObjs.Clear();
    }
}

// DummyElevationGenerator removed: not needed for prefab-based visualization
