using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Generates a spherical hex grid using a geodesic approach with fixed hex sizes.
/// Each tile is a proper hexagon (except for 12 pentagons at the original icosahedron vertices).
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

    /// <summary>
    /// Generate a spherical hex grid with fixed hex tile area.
    /// </summary>
    /// <param name="targetTileCount">Target number of tiles to generate</param>
    /// <param name="radius">Radius of the sphere</param>
    public void Generate(int targetTileCount, float radius)
    {
        Radius = radius;
        Debug.Log($"[SphericalHexGrid] Generating spherical hex grid, target={targetTileCount}, radius={radius}");

        // Calculate the number of icosahedron subdivisions needed
        int subdivisions = CalculateSubdivisions(targetTileCount);
        Debug.Log($"[SphericalHexGrid] Using {subdivisions} icosahedron subdivisions");

        // Generate tiles using geodesic approach
        List<Vector3> allTileCenters = new List<Vector3>();
        
        // Start with icosahedron vertices
        Vector3[] icosahedronVertices = GenerateIcosahedronVertices();
        
        // Subdivide icosahedron faces and generate hex centers
        for (int face = 0; face < 20; face++)
        {
            Vector3[] faceVertices = GetIcosahedronFace(face, icosahedronVertices);
            List<Vector3> faceHexCenters = SubdivideFaceToHexes(faceVertices, subdivisions);
            allTileCenters.AddRange(faceHexCenters);
        }

        // Remove duplicates and normalize to sphere surface
        allTileCenters = RemoveDuplicates(allTileCenters);
        allTileCenters = allTileCenters.Select(v => v.normalized * radius).ToList();

        // Convert to array
        tileCenters = allTileCenters.ToArray();
        Debug.Log($"[SphericalHexGrid] Generated {tileCenters.Length} tiles");

        // Build neighbor relationships using spherical distance
        BuildNeighborsFromSphericalDistance();

        // Build corner vertices for each tile
        BuildTileCorners();

        // Signal completion
        OnGeneration?.Invoke(this);
    }

    /// <summary>
    /// Calculate the number of icosahedron subdivisions needed for target tile count
    /// </summary>
    private int CalculateSubdivisions(int targetTileCount)
    {
        // Each icosahedron face generates approximately (subdivisions + 1)² hexes
        // Total tiles ≈ 20 * (subdivisions + 1)²
        // So subdivisions ≈ sqrt(targetTileCount / 20) - 1
        int subdivisions = Mathf.RoundToInt(Mathf.Sqrt(targetTileCount / 20f) - 1);
        return Mathf.Max(1, subdivisions); // Minimum 1 subdivision
    }

    /// <summary>
    /// Generate icosahedron vertices
    /// </summary>
    private Vector3[] GenerateIcosahedronVertices()
    {
        float phi = (1f + Mathf.Sqrt(5f)) / 2f; // Golden ratio
        float a = 1f;
        float b = 1f / phi;

        Vector3[] vertices = new Vector3[12];
        vertices[0] = new Vector3(0, a, b);
        vertices[1] = new Vector3(0, a, -b);
        vertices[2] = new Vector3(0, -a, b);
        vertices[3] = new Vector3(0, -a, -b);
        vertices[4] = new Vector3(a, b, 0);
        vertices[5] = new Vector3(a, -b, 0);
        vertices[6] = new Vector3(-a, b, 0);
        vertices[7] = new Vector3(-a, -b, 0);
        vertices[8] = new Vector3(b, 0, a);
        vertices[9] = new Vector3(-b, 0, a);
        vertices[10] = new Vector3(b, 0, -a);
        vertices[11] = new Vector3(-b, 0, -a);

        return vertices;
    }

    /// <summary>
    /// Get the three vertices of an icosahedron face
    /// </summary>
    private Vector3[] GetIcosahedronFace(int faceIndex, Vector3[] vertices)
    {
        // Icosahedron face indices (20 faces, 3 vertices each)
        int[,] faceIndices = {
            {0, 8, 4}, {0, 4, 1}, {0, 1, 9}, {0, 9, 2}, {0, 2, 8},
            {1, 4, 10}, {1, 10, 3}, {1, 3, 9}, {2, 9, 3}, {2, 3, 11},
            {2, 11, 8}, {3, 10, 11}, {4, 8, 5}, {4, 5, 10}, {5, 8, 11},
            {5, 11, 7}, {5, 7, 6}, {5, 6, 4}, {6, 7, 9}, {6, 9, 1}
        };

        return new Vector3[] {
            vertices[faceIndices[faceIndex, 0]],
            vertices[faceIndices[faceIndex, 1]],
            vertices[faceIndices[faceIndex, 2]]
        };
    }

    /// <summary>
    /// Subdivide a triangular face into hexagons
    /// </summary>
    private List<Vector3> SubdivideFaceToHexes(Vector3[] faceVertices, int subdivisions)
    {
        List<Vector3> hexCenters = new List<Vector3>();
        
        // Generate barycentric coordinates for hex centers
        for (int i = 0; i <= subdivisions; i++)
        {
            for (int j = 0; j <= subdivisions - i; j++)
            {
                int k = subdivisions - i - j;
                
                // Calculate barycentric coordinates
                float u = (float)i / subdivisions;
                float v = (float)j / subdivisions;
                float w = (float)k / subdivisions;
                
                // Convert to 3D position
                Vector3 center = u * faceVertices[0] + v * faceVertices[1] + w * faceVertices[2];
                center = center.normalized;
                
                hexCenters.Add(center);
            }
        }
        
        return hexCenters;
    }

    /// <summary>
    /// Remove duplicate vertices (within tolerance)
    /// </summary>
    private List<Vector3> RemoveDuplicates(List<Vector3> vertices)
    {
        List<Vector3> unique = new List<Vector3>();
        float tolerance = 0.01f;
        
        foreach (Vector3 v in vertices)
        {
            bool isDuplicate = false;
            foreach (Vector3 existing in unique)
            {
                if (Vector3.Distance(v, existing) < tolerance)
                {
                    isDuplicate = true;
                    break;
                }
            }
            
            if (!isDuplicate)
            {
                unique.Add(v);
            }
        }
        
        return unique;
    }

    /// <summary>
    /// Build neighbor relationships using spherical distance
    /// </summary>
    private void BuildNeighborsFromSphericalDistance()
    {
        neighbors = new List<int>[tileCenters.Length];
        
        for (int i = 0; i < tileCenters.Length; i++)
        {
            neighbors[i] = new List<int>();
        }

        // Find neighbors based on spherical distance
        float neighborThreshold = Radius * 0.2f; // Adjust this value to control neighbor distance
        
        for (int i = 0; i < tileCenters.Length; i++)
        {
            for (int j = i + 1; j < tileCenters.Length; j++)
            {
                float distance = Vector3.Distance(tileCenters[i], tileCenters[j]);
                if (distance < neighborThreshold)
                {
                    neighbors[i].Add(j);
                    neighbors[j].Add(i);
                }
            }
        }

        Debug.Log($"[SphericalHexGrid] Built neighbor relationships for {tileCenters.Length} tiles");
    }

    /// <summary>
    /// Build corner vertices for each tile
    /// </summary>
    private void BuildTileCorners()
    {
        Vertices = new List<Vector3>();
        tileCorners = new List<int>[tileCenters.Length];

        for (int tileIndex = 0; tileIndex < tileCenters.Length; tileIndex++)
        {
            Vector3 center = tileCenters[tileIndex];
            List<int> cornerIndices = new List<int>();

            // Get neighboring tile centers
            List<Vector3> neighborCenters = new List<Vector3>();
            foreach (int neighborIndex in neighbors[tileIndex])
            {
                neighborCenters.Add(tileCenters[neighborIndex]);
            }

            // Generate corner vertices at the midpoint between this tile and each neighbor
            for (int i = 0; i < neighborCenters.Count; i++)
            {
                Vector3 neighbor = neighborCenters[i];
                Vector3 corner = (center + neighbor).normalized * Radius;
                
                // Check if this corner already exists
                int cornerIndex = -1;
                for (int j = 0; j < Vertices.Count; j++)
                {
                    if (Vector3.Distance(Vertices[j], corner) < Radius * 0.01f)
                    {
                        cornerIndex = j;
                        break;
                    }
                }

                if (cornerIndex == -1)
                {
                    cornerIndex = Vertices.Count;
                    Vertices.Add(corner);
                }

                cornerIndices.Add(cornerIndex);
            }

            tileCorners[tileIndex] = cornerIndices;
        }

        Debug.Log($"[SphericalHexGrid] Built {Vertices.Count} corner vertices for {tileCenters.Length} tiles");
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