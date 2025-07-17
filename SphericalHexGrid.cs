using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Generates a proper geodesic hexasphere using RedBlobGames' approach.
/// Creates a true hex grid on a sphere with proper topology and corner generation.
/// </summary>
public class SphericalHexGrid
{
    public int TileCount => tileCenters.Length;
    public Vector3[] tileCenters;             // Center point of each tile (on sphere surface)
    public List<int>[] neighbors;            // Neighbor indices for each tile
    public event System.Action<SphericalHexGrid> OnGeneration;  // Event fired when generation is complete

    // Expose generated geometry for mesh building
    public List<Vector3> Vertices { get; private set; }
    public List<int>[] tileCorners;          // Corner vertex indices for each tile
    
    // Store the radius for later access
    public float Radius { get; private set; }
    
    // Track pentagon tiles (icosahedron vertices)
    public HashSet<int> pentagonIndices { get; private set; }

    // Internal data structures for proper geodesic generation
    private List<Vector3> icosahedronVertices;
    private List<int[]> icosahedronFaces;
    private List<Vector3> subdividedVertices;
    private List<int[]> subdividedFaces;
    private Dictionary<Vector3, int> vertexToIndex;

    /// <summary>
    /// Generate a proper geodesic hexasphere with correct topology
    /// </summary>
    /// <param name="targetTileCount">Target number of tiles to generate</param>
    /// <param name="radius">Radius of the sphere</param>
    public void Generate(int targetTileCount, float radius)
    {
        Radius = radius;
        Debug.Log($"[SphericalHexGrid] Generating proper geodesic hexasphere, target={targetTileCount}, radius={radius}");

        // Calculate the number of icosahedron subdivisions needed
        int subdivisions = CalculateSubdivisions(targetTileCount);
        Debug.Log($"[SphericalHexGrid] Using {subdivisions} icosahedron subdivisions");

        // Step 1: Generate icosahedron
        GenerateIcosahedron();

        // Step 2: Subdivide icosahedron faces
        SubdivideIcosahedron(subdivisions);

        // Step 3: Generate hex centers from face centers (RedBlobGames method)
        GenerateHexCentersFromFaces();

        // Step 4: Build proper neighbor relationships using dual graph
        BuildNeighborsFromDualGraph();

        // Step 5: Detect the 12 pentagon tiles (original icosahedron vertices)
        DetectPentagons();

        // Step 6: Build proper corner vertices
        BuildTileCorners();

        Debug.Log($"[SphericalHexGrid] Generated {tileCenters.Length} tiles with proper topology");
        Debug.Log($"[SphericalHexGrid] Pentagons: {pentagonIndices.Count}, Hexagons: {tileCenters.Length - pentagonIndices.Count}");

        // Signal completion
        OnGeneration?.Invoke(this);
    }

    /// <summary>
    /// Get valid tile counts for different icosahedron subdivision levels
    /// </summary>
    public static int[] GetValidTileCounts()
    {
        List<int> validCounts = new List<int>();
        for (int subdivisions = 1; subdivisions <= 10; subdivisions++)
        {
            int faces = 20 * (subdivisions + 1) * (subdivisions + 1);
            validCounts.Add(faces);
        }
        return validCounts.ToArray();
    }

    /// <summary>
    /// Get the closest valid tile count to the target
    /// </summary>
    public static int GetClosestValidTileCount(int targetTileCount)
    {
        int[] validCounts = GetValidTileCounts();
        int closest = validCounts[0];
        int minDiff = Mathf.Abs(targetTileCount - closest);
        
        foreach (int count in validCounts)
        {
            int diff = Mathf.Abs(targetTileCount - count);
            if (diff < minDiff)
            {
                minDiff = diff;
                closest = count;
            }
        }
        
        return closest;
    }

    /// <summary>
    /// Calculate the number of icosahedron subdivisions needed for target tile count
    /// </summary>
    private int CalculateSubdivisions(int targetTileCount)
    {
        // Each icosahedron face generates (subdivisions + 1)^2 faces
        int subdivisions = Mathf.RoundToInt(Mathf.Sqrt(targetTileCount / 20f) - 1);
        subdivisions = Mathf.Max(1, subdivisions);

        int actualFaces = 20 * (subdivisions + 1) * (subdivisions + 1);
        Debug.Log($"[SphericalHexGrid] Target: {targetTileCount}, Subdivisions: {subdivisions}, Actual faces: {actualFaces}");
        return subdivisions;
    }

    /// <summary>
    /// Generate icosahedron vertices and faces
    /// </summary>
    private void GenerateIcosahedron()
    {
        icosahedronVertices = new List<Vector3>();
        icosahedronFaces = new List<int[]>();

        float phi = (1f + Mathf.Sqrt(5f)) / 2f; // Golden ratio
        float a = 1f;
        float b = 1f / phi;

        // Generate 12 icosahedron vertices
        icosahedronVertices.Add(new Vector3(0, a, b));
        icosahedronVertices.Add(new Vector3(0, a, -b));
        icosahedronVertices.Add(new Vector3(0, -a, b));
        icosahedronVertices.Add(new Vector3(0, -a, -b));
        icosahedronVertices.Add(new Vector3(a, b, 0));
        icosahedronVertices.Add(new Vector3(a, -b, 0));
        icosahedronVertices.Add(new Vector3(-a, b, 0));
        icosahedronVertices.Add(new Vector3(-a, -b, 0));
        icosahedronVertices.Add(new Vector3(b, 0, a));
        icosahedronVertices.Add(new Vector3(-b, 0, a));
        icosahedronVertices.Add(new Vector3(b, 0, -a));
        icosahedronVertices.Add(new Vector3(-b, 0, -a));

        // Normalize all vertices to unit sphere
        for (int i = 0; i < icosahedronVertices.Count; i++)
        {
            icosahedronVertices[i] = icosahedronVertices[i].normalized;
        }

        // Define 20 icosahedron faces (triangles)
        int[,] faceIndices = {
            {0, 8, 4}, {0, 4, 1}, {0, 1, 9}, {0, 9, 2}, {0, 2, 8},
            {1, 4, 10}, {1, 10, 3}, {1, 3, 9}, {2, 9, 3}, {2, 3, 11},
            {2, 11, 8}, {3, 10, 11}, {4, 8, 5}, {4, 5, 10}, {5, 8, 11},
            {5, 11, 7}, {5, 7, 6}, {5, 6, 4}, {6, 7, 9}, {6, 9, 1}
        };

        for (int i = 0; i < 20; i++)
        {
            icosahedronFaces.Add(new int[] { faceIndices[i, 0], faceIndices[i, 1], faceIndices[i, 2] });
        }
    }

    /// <summary>
    /// Subdivide icosahedron faces using geodesic subdivision
    /// </summary>
    private void SubdivideIcosahedron(int subdivisions)
    {
        subdividedVertices = new List<Vector3>(icosahedronVertices);
        subdividedFaces = new List<int[]>();
        vertexToIndex = new Dictionary<Vector3, int>();

        // Initialize vertex lookup for original icosahedron vertices
        for (int i = 0; i < icosahedronVertices.Count; i++)
        {
            vertexToIndex[icosahedronVertices[i]] = i;
        }

        // Subdivide each icosahedron face
        foreach (int[] face in icosahedronFaces)
        {
            SubdivideFace(face, subdivisions);
        }

        // Normalize all subdivided vertices to unit sphere
        for (int i = 0; i < subdividedVertices.Count; i++)
        {
            subdividedVertices[i] = subdividedVertices[i].normalized;
        }
    }

    /// <summary>
    /// Subdivide a single triangular face
    /// </summary>
    private void SubdivideFace(int[] face, int subdivisions)
    {
        Vector3 v0 = icosahedronVertices[face[0]];
        Vector3 v1 = icosahedronVertices[face[1]];
        Vector3 v2 = icosahedronVertices[face[2]];

        // Create vertices using proper triangular barycentric coordinates
        // For each subdivision level, we create vertices at barycentric coordinates
        // where i + j + k = subdivisions (i, j, k are non-negative integers)
        
        // Generate all vertices first
        Dictionary<string, int> barycentricToVertex = new Dictionary<string, int>();
        
        for (int i = 0; i <= subdivisions; i++)
        {
            for (int j = 0; j <= subdivisions - i; j++)
            {
                int k = subdivisions - i - j;
                
                // Calculate barycentric coordinates (normalized)
                float u = (float)i / subdivisions;
                float v = (float)j / subdivisions;
                float w = (float)k / subdivisions;
                
                // Ensure barycentric coordinates sum to 1
                float sum = u + v + w;
                u /= sum;
                v /= sum;
                w /= sum;
                
                // Convert to 3D position
                Vector3 point = u * v0 + v * v1 + w * v2;
                point = point.normalized;
                
                // Create unique key for this barycentric coordinate
                string key = $"{i},{j},{k}";
                barycentricToVertex[key] = GetOrCreateVertex(point);
            }
        }
        
        // Create triangles from the barycentric grid
        for (int i = 0; i < subdivisions; i++)
        {
            for (int j = 0; j < subdivisions - i; j++)
            {
                int k = subdivisions - i - j;
                
                // Get the four vertices of this subdivision cell
                string v00_key = $"{i},{j},{k}";
                string v10_key = $"{i+1},{j},{k-1}";
                string v01_key = $"{i},{j+1},{k-1}";
                string v11_key = $"{i+1},{j+1},{k-2}";
                
                // Create first triangle
                subdividedFaces.Add(new int[] { 
                    barycentricToVertex[v00_key], 
                    barycentricToVertex[v10_key], 
                    barycentricToVertex[v01_key] 
                });
                
                // Create second triangle (if k-2 >= 0, meaning we're not at the edge)
                if (k > 1)
                {
                    subdividedFaces.Add(new int[] { 
                        barycentricToVertex[v10_key], 
                        barycentricToVertex[v11_key], 
                        barycentricToVertex[v01_key] 
                    });
                }
            }
        }
    }

    /// <summary>
    /// Get or create a vertex index for the given position
    /// </summary>
    private int GetOrCreateVertex(Vector3 position)
    {
        Vector3 normalized = position.normalized;
        
        // Check if vertex already exists (with tolerance)
        foreach (var kvp in vertexToIndex)
        {
            if (Vector3.Distance(kvp.Key, normalized) < 0.001f)
            {
                return kvp.Value;
            }
        }
        
        // Create new vertex
        int index = subdividedVertices.Count;
        subdividedVertices.Add(normalized);
        vertexToIndex[normalized] = index;
        return index;
    }

    /// <summary>
    /// Generate hex centers from the centers of subdivided faces
    /// </summary>
    private void GenerateHexCentersFromFaces()
    {
        List<Vector3> centers = new List<Vector3>();

        // Each subdivided face becomes a hex tile center
        foreach (int[] face in subdividedFaces)
        {
            Vector3 v0 = subdividedVertices[face[0]];
            Vector3 v1 = subdividedVertices[face[1]];
            Vector3 v2 = subdividedVertices[face[2]];

            Vector3 faceCenter = (v0 + v1 + v2).normalized;
            centers.Add(faceCenter);
        }

        tileCenters = centers.Select(v => v * Radius).ToArray();

        Debug.Log($"[SphericalHexGrid] Generated {tileCenters.Length} hex centers from face centers");
    }

    /// <summary>
    /// Build neighbor relationships using the dual graph (faces sharing edges)
    /// </summary>
    private void BuildNeighborsFromDualGraph()
    {
        neighbors = new List<int>[tileCenters.Length];

        for (int i = 0; i < tileCenters.Length; i++)
        {
            neighbors[i] = new List<int>();
        }

        Dictionary<string, List<int>> edgeToFaces = new Dictionary<string, List<int>>();

        for (int faceIndex = 0; faceIndex < subdividedFaces.Count; faceIndex++)
        {
            int[] face = subdividedFaces[faceIndex];

            for (int edge = 0; edge < 3; edge++)
            {
                int v1 = face[edge];
                int v2 = face[(edge + 1) % 3];

                string edgeKey = v1 < v2 ? $"{v1}-{v2}" : $"{v2}-{v1}";

                if (!edgeToFaces.ContainsKey(edgeKey))
                {
                    edgeToFaces[edgeKey] = new List<int>();
                }
                edgeToFaces[edgeKey].Add(faceIndex);
            }
        }

        for (int faceIndex = 0; faceIndex < subdividedFaces.Count; faceIndex++)
        {
            int[] face = subdividedFaces[faceIndex];

            for (int edge = 0; edge < 3; edge++)
            {
                int v1 = face[edge];
                int v2 = face[(edge + 1) % 3];

                string edgeKey = v1 < v2 ? $"{v1}-{v2}" : $"{v2}-{v1}";

                if (edgeToFaces.ContainsKey(edgeKey))
                {
                    foreach (int neighborFace in edgeToFaces[edgeKey])
                    {
                        if (neighborFace != faceIndex)
                        {
                            neighbors[faceIndex].Add(neighborFace);
                        }
                    }
                }
            }
        }

        for (int i = 0; i < neighbors.Length; i++)
        {
            neighbors[i] = neighbors[i].Distinct().OrderBy(j => Vector3.Distance(tileCenters[i], tileCenters[j])).ToList();
        }

        Debug.Log($"[SphericalHexGrid] Built neighbor relationships for {tileCenters.Length} tiles using dual graph");
    }

    /// <summary>
    /// Detect pentagon tiles (faces that include original icosahedron vertices)
    /// </summary>
    private void DetectPentagons()
    {
        pentagonIndices = new HashSet<int>();

        // Find faces that contain original icosahedron vertices
        for (int faceIndex = 0; faceIndex < subdividedFaces.Count; faceIndex++)
        {
            int[] face = subdividedFaces[faceIndex];

            bool containsOriginalVertex = false;
            for (int v = 0; v < 3; v++)
            {
                if (face[v] < icosahedronVertices.Count)
                {
                    containsOriginalVertex = true;
                    break;
                }
            }

            if (containsOriginalVertex)
            {
                pentagonIndices.Add(faceIndex);
            }
        }

        Debug.Log($"[SphericalHexGrid] Detected {pentagonIndices.Count} pentagon tiles");
    }

    /// <summary>
    /// Build proper corner vertices for each tile
    /// </summary>
    private void BuildTileCorners()
    {
        Vertices = new List<Vector3>();
        tileCorners = new List<int>[tileCenters.Length];

        for (int i = 0; i < tileCenters.Length; i++)
        {
            Vector3 center = tileCenters[i].normalized;
            var neighborCenters = neighbors[i].Select(idx => tileCenters[idx].normalized).ToList();
            
            // Sort neighbors clockwise around the center
            neighborCenters.Sort((a, b) =>
            {
                Vector3 ca = (a - center).normalized;
                Vector3 cb = (b - center).normalized;
                return Vector3.SignedAngle(ca, cb, center) > 0 ? 1 : -1;
            });

            var corners = new List<int>();
            int neighborCount = neighborCenters.Count;

            // Generate corners between consecutive neighbors
            for (int n = 0; n < neighborCount; n++)
            {
                Vector3 neighbor1 = neighborCenters[n];
                Vector3 neighbor2 = neighborCenters[(n + 1) % neighborCount];
                
                // Calculate corner position as the intersection of the three great circles
                Vector3 cornerPos = CalculateCornerPosition(center, neighbor1, neighbor2);
                cornerPos = cornerPos.normalized * Radius;

                // Find or create vertex
                int vertexIndex = FindOrCreateVertex(cornerPos);
                corners.Add(vertexIndex);
            }

            tileCorners[i] = corners;
        }
    }

    /// <summary>
    /// Calculate proper corner position using spherical geometry
    /// </summary>
    private Vector3 CalculateCornerPosition(Vector3 center, Vector3 neighbor1, Vector3 neighbor2)
    {
        // Use spherical trigonometry to find the corner position
        // The corner is the intersection of the great circles defined by center-neighbor1 and center-neighbor2
        
        // Calculate the great circle intersection using spherical trigonometry
        // The corner should be equidistant from all three points
        
        // Method 1: Use the circumcenter of the spherical triangle
        // This is the point equidistant from all three vertices
        Vector3 corner = (center + neighbor1 + neighbor2).normalized;
        
        // Method 2: Use the intersection of the perpendicular bisectors
        // Calculate the midpoint of the arc between neighbor1 and neighbor2
        Vector3 midPoint = (neighbor1 + neighbor2).normalized;
        
        // The corner should be on the great circle perpendicular to the center-midpoint line
        Vector3 perpendicular = Vector3.Cross(center, midPoint).normalized;
        
        // The corner is the intersection of the great circle through center and perpendicular
        // with the great circle through neighbor1 and neighbor2
        Vector3 greatCircle1 = Vector3.Cross(center, perpendicular).normalized;
        Vector3 greatCircle2 = Vector3.Cross(neighbor1, neighbor2).normalized;
        
        // The intersection is the cross product of the two great circle normals
        Vector3 intersection = Vector3.Cross(greatCircle1, greatCircle2).normalized;
        
        // Choose the intersection point that's closest to our center
        if (Vector3.Dot(intersection, center) < 0)
        {
            intersection = -intersection;
        }
        
        // Use a weighted average of the two methods for better results
        corner = (corner + intersection).normalized;
        
        return corner;
    }

    /// <summary>
    /// Find or create a vertex for the given position
    /// </summary>
    private int FindOrCreateVertex(Vector3 position)
    {
        float tolerance = Radius * 0.001f; // Very small tolerance for vertex matching
        
        // Check if vertex already exists
        for (int i = 0; i < Vertices.Count; i++)
        {
            if (Vector3.Distance(Vertices[i], position) < tolerance)
            {
                return i;
            }
        }
        
        // Create new vertex
        int index = Vertices.Count;
        Vertices.Add(position);
        return index;
    }

    /// <summary>
    /// Get the corner vertex indices for a specific tile
    /// </summary>
    public int[] GetCornersOfTile(int tileIndex)
    {
        if (tileIndex >= 0 && tileIndex < tileCorners.Length)
        {
            return tileCorners[tileIndex].ToArray();
        }
        return new int[0];
    }

    /// <summary>
    /// Get the tile index whose center is nearest to the given position
    /// </summary>
    public int GetTileAtPosition(Vector3 position, bool localSpace = false)
    {
        Vector3 dir = position;
        if (!localSpace)
        {
            dir = (position - Vector3.zero).normalized;
        }

        int nearestIndex = -1;
        float maxDot = -1f;

        for (int i = 0; i < tileCenters.Length; i++)
        {
            float dot = Vector3.Dot(dir, tileCenters[i].normalized);
            if (dot > maxDot)
            {
                maxDot = dot;
                nearestIndex = i;
            }
        }

        return nearestIndex;
    }

    /// <summary>
    /// Get latitude/longitude (in degrees) of the given tile index
    /// </summary>
    public Vector2 GetTileLatLon(int tileIndex)
    {
        Vector3 p = tileCenters[tileIndex].normalized;
        float latitude = Mathf.Asin(p.y) * Mathf.Rad2Deg;
        float longitude = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg;
        return new Vector2(latitude, longitude);
    }
} 