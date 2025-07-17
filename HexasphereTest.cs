using UnityEngine;
using System.Collections.Generic;

/// <summary>Quick test harness for the geodesic grid.</summary>
public class HexasphereTest : MonoBehaviour
{
    /* ─────────────────────────  STATIC DATA  ──────────────────────────
       "Depth" = how many recursive edge‑splits you ask the generator for.
       The tile totals follow  tiles = 10*(2^(depth‑1))² + 2 .
       We pre‑compute once so nobody ever hand‑types a number.             */
    private static readonly int[] TilesPerDepth;

    static HexasphereTest()
    {
        const int MAX_DEPTH = 20;                 // slider upper‑bound
        TilesPerDepth       = new int[MAX_DEPTH + 1];
        for (int d = 1; d <= MAX_DEPTH; d++)
        {
            int f = 1 << (d - 1);                 // 2^(d‑1)
            TilesPerDepth[d] = 10 * f * f + 2;
        }
    }

    /* ─────────────────────────  INSPECTOR FIELDS  ────────────────────*/
    [Header("Test Settings")]
    [Range(1, 20)] public int  subdivisions = 3;
    public float  testRadius  = 50f;
    public bool   runTestOnStart = true;

    [Header("Debug Visualisation")]
    public bool showTileCenters = true, showNeighbors = true, showCorners = true;
    public Color tileCenterColor   = Color.white;
    public Color neighborLineColor = Color.yellow;
    public Color cornerColor       = Color.red;

    [Header("Elevation test")]
    public float noiseFrequency = 3f;
    public float maxTestElevation = 0.12f;   // 12 % of radius
    private FastNoiseLite noise = new FastNoiseLite(12345);

    /* ─────────────────────────  INTERNAL STATE  ──────────────────────*/
    private SphericalHexGrid   testGrid;
    private readonly List<GameObject> debugObjects = new();

    /* ─────────────────────────  LIFECYCLE  ───────────────────────────*/
    private void Start()
    {
        if (runTestOnStart) RunTest();
    }

    /* ─────────────────────────  MAIN ENTRY  ──────────────────────────*/
    [ContextMenu("Run Hexasphere Test")]
    public void RunTest()
    {
        int estimated = TilesPerDepth[subdivisions];
        Debug.Log($"[HexasphereTest] Starting hexasphere test " +
                  $"with subdivisions={subdivisions} (≈{estimated} tiles)…");

        ClearDebugObjects();

        testGrid = new SphericalHexGrid();
        testGrid.GenerateFromSubdivision(subdivisions, testRadius);

        Debug.Log($"[HexasphereTest] Generated grid with {testGrid.TileCount} tiles");
        Debug.Log($"[HexasphereTest] Pentagons: {testGrid.pentagonIndices.Count}");
        Debug.Log($"[HexasphereTest] Hexagons:  {testGrid.TileCount - testGrid.pentagonIndices.Count}");
        Debug.Log($"[HexasphereTest] Vertices:  {testGrid.Vertices.Count}");

        // Generate test elevation data using noise
        float[] testElevation = new float[testGrid.TileCount];
        for (int i = 0; i < testGrid.TileCount; i++)
        {
            Vector3 p = testGrid.tileCenters[i].normalized * noiseFrequency;
            testElevation[i] = noise.GetNoise(p.x, p.y, p.z) * maxTestElevation; // 0‑max
        }

        // Build a HexasphereRenderer next to the grid and feed it the elevation data
        var renderer = gameObject.AddComponent<HexasphereRenderer>();
        renderer.BuildMesh(testGrid);
        renderer.SetCustomElevations(testElevation);   // Use our custom elevation data
        renderer.ApplyHeightDisplacement(testRadius);

        ValidateGrid();
        CreateDebugVisualization();

        // Build the visual mesh using HexasphereRenderer
        BuildRenderer();
    }

    /* ─────────────────────────  UTILITIES  ───────────────────────────*/
    [ContextMenu("Show Valid Subdivisions")]
    public void ShowValidSubdivisions()
    {
        for (int d = 1; d < TilesPerDepth.Length; d++)
            Debug.Log($"Depth {d} → {TilesPerDepth[d]} tiles");
    }

    private void ValidateGrid()
    {
        Debug.Log("[HexasphereTest] Validating grid...");

        int validHexagons = 0;
        int validPentagons = 0;
        int invalidTiles = 0;

        for (int i = 0; i < testGrid.TileCount; i++)
        {
            int neighborCount = testGrid.neighbors[i].Count;

            if (neighborCount == 6)
                validHexagons++;
            else if (neighborCount == 5)
                validPentagons++;
            else
            {
                invalidTiles++;
                Debug.LogWarning($"[HexasphereTest] Tile {i} has {neighborCount} neighbors (should be 5 or 6)");
            }

            // Check for duplicate neighbors
            var neighbors = testGrid.neighbors[i];
            var uniqueNeighbors = new HashSet<int>(neighbors);
            if (neighbors.Count != uniqueNeighbors.Count)
                Debug.LogError($"[HexasphereTest] Tile {i} has duplicate neighbors!");

            // Check that neighbor relationships are bidirectional
            foreach (int neighbor in neighbors)
                if (!testGrid.neighbors[neighbor].Contains(i))
                    Debug.LogError($"[HexasphereTest] Neighbor relationship not bidirectional: {i} -> {neighbor}");
        }

        Debug.Log($"[HexasphereTest] Validation complete:");
        Debug.Log($"[HexasphereTest]   Valid hexagons: {validHexagons}");
        Debug.Log($"[HexasphereTest]   Valid pentagons: {validPentagons}");
        Debug.Log($"[HexasphereTest]   Invalid tiles: {invalidTiles}");

        // Check corner consistency
        int totalCorners = 0;
        for (int i = 0; i < testGrid.TileCount; i++)
        {
            var corners = testGrid.GetCornersOfTile(i);
            totalCorners += corners.Length;

            if (corners.Length != testGrid.neighbors[i].Count)
                Debug.LogError($"[HexasphereTest] Tile {i} has {corners.Length} corners but {testGrid.neighbors[i].Count} neighbors");
        }

        Debug.Log($"[HexasphereTest] Total corners: {totalCorners}");
    }

    private void CreateDebugVisualization()
    {
        Debug.Log("[HexasphereTest] Creating debug visualization...");

        // Create tile centers
        if (showTileCenters)
        {
            for (int i = 0; i < testGrid.TileCount; i++)
            {
                GameObject centerObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                centerObj.transform.position = testGrid.tileCenters[i];
                centerObj.transform.localScale = Vector3.one * 0.5f;
                Color color = testGrid.neighbors[i].Count == 6 ? Color.green : Color.blue;
                centerObj.GetComponent<Renderer>().material.color = color;
                centerObj.name = $"TileCenter_{i}";
                debugObjects.Add(centerObj);
            }
        }

        // Create neighbor lines
        if (showNeighbors)
        {
            for (int i = 0; i < testGrid.TileCount; i++)
            {
                Vector3 center = testGrid.tileCenters[i];
                foreach (int neighbor in testGrid.neighbors[i])
                {
                    if (i < neighbor)
                    {
                        Vector3 neighborPos = testGrid.tileCenters[neighbor];
                        GameObject lineObj = new GameObject($"NeighborLine_{i}_{neighbor}");
                        LineRenderer lr = lineObj.AddComponent<LineRenderer>();
                        lr.material = new Material(Shader.Find("Sprites/Default"));
                        lr.material.color = neighborLineColor;
                        lr.startWidth = 0.1f;
                        lr.endWidth = 0.1f;
                        lr.positionCount = 2;
                        lr.SetPosition(0, center);
                        lr.SetPosition(1, neighborPos);
                        debugObjects.Add(lineObj);
                    }
                }
            }
        }

        // Create corner vertices
        if (showCorners)
        {
            for (int i = 0; i < testGrid.Vertices.Count; i++)
            {
                GameObject cornerObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                cornerObj.transform.position = testGrid.Vertices[i];
                cornerObj.transform.localScale = Vector3.one * 0.3f;
                cornerObj.GetComponent<Renderer>().material.color = cornerColor;
                cornerObj.name = $"Corner_{i}";
                debugObjects.Add(cornerObj);
            }
        }
    }

    // ------------------------------------------------------------------
    // Hexasphere Rendering
    // ------------------------------------------------------------------
    private void BuildRenderer()
    {
        HexasphereRenderer renderer = GetComponent<HexasphereRenderer>();
        if (renderer == null)
            renderer = gameObject.AddComponent<HexasphereRenderer>();

        float radius = testRadius;

        // Generate simple perlin-based elevations
        float[] elev = new float[testGrid.TileCount];
        for (int i = 0; i < testGrid.TileCount; i++)
        {
            Vector3 p = testGrid.tileCenters[i].normalized * 4f;
            float noise = Mathf.PerlinNoise(p.x + 0.5f, p.y + 0.5f);
            elev[i] = noise * 0.1f;
        }

        // 1) Spawn the dummy generator and give it the elevation array
        var dummy = gameObject.AddComponent<DummyElevationGenerator>();
        dummy.elevations = elev;

        // 2) Point the renderer at it
        renderer.generatorSource = dummy;

        // 3) Build mesh and apply displacement
        renderer.BuildMesh(testGrid);
        renderer.ApplyHeightDisplacement(radius);

        Debug.Log("[HexasphereTest] DummyElevationGenerator attached, mesh built, displacement applied.");
    }

    private void ClearDebugObjects()
    {
        foreach (GameObject obj in debugObjects)
            if (obj != null) DestroyImmediate(obj);
        debugObjects.Clear();
    }

    void OnDestroy()
    {
        ClearDebugObjects();
    }

    [ContextMenu("Clear Debug Objects")]
    public void ClearDebug()
    {
        ClearDebugObjects();
    }
}
