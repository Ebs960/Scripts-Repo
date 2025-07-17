using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Test script to verify the new proper hexasphere system is working correctly
/// </summary>
public class HexasphereTest : MonoBehaviour
{
    [Header("Test Settings")]
    public int testTileCount = 80; // Changed to valid count: 20 * (1+1)² = 80
    public float testRadius = 50f;
    public bool runTestOnStart = true;
    
    [Header("Debug Visualization")]
    public bool showTileCenters = true;
    public bool showNeighbors = true;
    public bool showCorners = true;
    public Color tileCenterColor = Color.white;
    public Color neighborLineColor = Color.yellow;
    public Color cornerColor = Color.red;
    
    private SphericalHexGrid testGrid;
    private List<GameObject> debugObjects = new List<GameObject>();

    void Start()
    {
        if (runTestOnStart)
        {
            RunTest();
        }
    }

    [ContextMenu("Show Valid Tile Counts")]
    public void ShowValidTileCounts()
    {
        int[] validCounts = SphericalHexGrid.GetValidTileCounts();
        Debug.Log("[HexasphereTest] Valid tile counts:");
        for (int i = 0; i < validCounts.Length; i++)
        {
            Debug.Log($"[HexasphereTest]   Subdivision {i+1}: {validCounts[i]} tiles");
        }
    }

    [ContextMenu("Run Hexasphere Test")]
    public void RunTest()
    {
        Debug.Log("[HexasphereTest] Starting hexasphere test...");
        
        // Validate tile count
        int[] validCounts = SphericalHexGrid.GetValidTileCounts();
        if (!System.Array.Exists(validCounts, count => count == testTileCount))
        {
            int closest = SphericalHexGrid.GetClosestValidTileCount(testTileCount);
            Debug.LogWarning($"[HexasphereTest] Tile count {testTileCount} is not valid. Using closest valid count: {closest}");
            testTileCount = closest;
        }
        
        // Clear previous debug objects
        ClearDebugObjects();
        
        // Create and generate test grid
        testGrid = new SphericalHexGrid();
        testGrid.Generate(testTileCount, testRadius);
        
        Debug.Log($"[HexasphereTest] Generated grid with {testGrid.TileCount} tiles");
        Debug.Log($"[HexasphereTest] Pentagons: {testGrid.pentagonIndices.Count}");
        Debug.Log($"[HexasphereTest] Hexagons: {testGrid.TileCount - testGrid.pentagonIndices.Count}");
        Debug.Log($"[HexasphereTest] Vertices: {testGrid.Vertices.Count}");
        
        // Validate the grid
        ValidateGrid();
        
        // Create debug visualization
        if (showTileCenters || showNeighbors || showCorners)
        {
            CreateDebugVisualization();
        }
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
            {
                validHexagons++;
            }
            else if (neighborCount == 5)
            {
                validPentagons++;
            }
            else
            {
                invalidTiles++;
                Debug.LogWarning($"[HexasphereTest] Tile {i} has {neighborCount} neighbors (should be 5 or 6)");
            }
            
            // Check for duplicate neighbors
            var neighbors = testGrid.neighbors[i];
            var uniqueNeighbors = new HashSet<int>(neighbors);
            if (neighbors.Count != uniqueNeighbors.Count)
            {
                Debug.LogError($"[HexasphereTest] Tile {i} has duplicate neighbors!");
            }
            
            // Check that neighbor relationships are bidirectional
            foreach (int neighbor in neighbors)
            {
                if (!testGrid.neighbors[neighbor].Contains(i))
                {
                    Debug.LogError($"[HexasphereTest] Neighbor relationship not bidirectional: {i} -> {neighbor}");
                }
            }
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
            
            // Each tile should have the same number of corners as neighbors
            if (corners.Length != testGrid.neighbors[i].Count)
            {
                Debug.LogError($"[HexasphereTest] Tile {i} has {corners.Length} corners but {testGrid.neighbors[i].Count} neighbors");
            }
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
                
                // Color based on neighbor count
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
                    // Only draw line if this tile has a lower index to avoid duplicates
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

    private void ClearDebugObjects()
    {
        foreach (GameObject obj in debugObjects)
        {
            if (obj != null)
            {
                DestroyImmediate(obj);
            }
        }
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