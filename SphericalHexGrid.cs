using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Generates a spherical hex grid directly, without icosphere subdivision.
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
    /// Generate a spherical hex grid with the specified number of tiles.
    /// </summary>
    /// <param name="targetTileCount">Approximate number of tiles to generate</param>
    /// <param name="radius">Radius of the sphere</param>
    public void Generate(int targetTileCount, float radius)
    {
        Radius = radius;
        Debug.Log($"[SphericalHexGrid] Generating grid with target tiles={targetTileCount}, radius={radius}");

        // Start with icosahedron vertices as seed points
        List<Vector3> seedPoints = GenerateIcosahedronVertices();
        
        // Calculate how many hex rings we need around each seed point
        int hexRings = CalculateHexRings(targetTileCount, seedPoints.Count);
        Debug.Log($"[SphericalHexGrid] Using {hexRings} hex rings around {seedPoints.Count} seed points");

        // Generate all tile centers using hex grid pattern around each seed
        List<Vector3> allTileCenters = new List<Vector3>();
        Dictionary<Vector3, int> centerToIndex = new Dictionary<Vector3, int>();

        // Add seed points as tile centers
        for (int i = 0; i < seedPoints.Count; i++)
        {
            Vector3 center = seedPoints[i].normalized * radius;
            allTileCenters.Add(center);
            centerToIndex[center] = allTileCenters.Count - 1;
        }

        // Generate hex rings around each seed point
        for (int seedIndex = 0; seedIndex < seedPoints.Count; seedIndex++)
        {
            Vector3 seedPoint = seedPoints[seedIndex].normalized;
            
            for (int ring = 1; ring <= hexRings; ring++)
            {
                GenerateHexRing(seedPoint, ring, radius, allTileCenters, centerToIndex);
            }
        }

        // Remove duplicates and normalize to sphere
        tileCenters = RemoveDuplicates(allTileCenters, radius);
        Debug.Log($"[SphericalHexGrid] Generated {tileCenters.Length} unique tiles");

        // Build neighbor relationships
        BuildNeighbors();

        // Build corner vertices for each tile
        BuildTileCorners();

        // Signal completion
        OnGeneration?.Invoke(this);
    }

    /// <summary>
    /// Generate the 12 vertices of a regular icosahedron
    /// </summary>
    private List<Vector3> GenerateIcosahedronVertices()
    {
        List<Vector3> vertices = new List<Vector3>();
        
        // Golden ratio for icosahedron
        float phi = (1f + Mathf.Sqrt(5f)) / 2f;
        float invPhi = 1f / phi;

        // 12 vertices of icosahedron
        vertices.Add(new Vector3(0, 1, phi));
        vertices.Add(new Vector3(0, -1, phi));
        vertices.Add(new Vector3(0, 1, -phi));
        vertices.Add(new Vector3(0, -1, -phi));
        
        vertices.Add(new Vector3(1, phi, 0));
        vertices.Add(new Vector3(-1, phi, 0));
        vertices.Add(new Vector3(1, -phi, 0));
        vertices.Add(new Vector3(-1, -phi, 0));
        
        vertices.Add(new Vector3(phi, 0, 1));
        vertices.Add(new Vector3(-phi, 0, 1));
        vertices.Add(new Vector3(phi, 0, -1));
        vertices.Add(new Vector3(-phi, 0, -1));

        return vertices;
    }

    /// <summary>
    /// Calculate how many hex rings we need to achieve the target tile count
    /// </summary>
    private int CalculateHexRings(int targetTileCount, int seedCount)
    {
        // Each ring adds approximately 6 * ring tiles around each seed
        // Total tiles ≈ seedCount * (1 + 6 + 12 + 18 + ... + 6*rings)
        // This is approximately seedCount * (1 + 3*rings*(rings+1))
        
        // Solve for rings: targetTileCount ≈ seedCount * (1 + 3*rings*(rings+1))
        // rings^2 + rings - (targetTileCount/seedCount - 1)/3 ≈ 0
        
        float targetPerSeed = (float)targetTileCount / seedCount;
        float discriminant = 1f + 4f * (targetPerSeed - 1f) / 3f;
        
        if (discriminant < 0) return 1;
        
        int rings = Mathf.RoundToInt((-1f + Mathf.Sqrt(discriminant)) / 2f);
        return Mathf.Max(1, rings);
    }

    /// <summary>
    /// Generate a ring of hex tiles around a seed point
    /// </summary>
    private void GenerateHexRing(Vector3 seedPoint, int ring, float radius, 
                                List<Vector3> allTileCenters, Dictionary<Vector3, int> centerToIndex)
    {
        // Create a local coordinate system around the seed point
        Vector3 up = seedPoint.normalized;
        Vector3 right = Vector3.Cross(up, Vector3.forward).normalized;
        if (right.magnitude < 0.1f)
            right = Vector3.Cross(up, Vector3.right).normalized;
        Vector3 forward = Vector3.Cross(right, up).normalized;

        // Hex grid parameters
        float hexSize = 1f / (ring + 1f); // Adjust hex size based on ring
        float hexSpacing = hexSize * 1.5f; // Distance between hex centers

        // Generate hex positions in local coordinates
        for (int i = 0; i < 6 * ring; i++)
        {
            float angle = i * 60f * Mathf.Deg2Rad;
            float distance = ring * hexSpacing;
            
            // Local hex position
            Vector3 localPos = new Vector3(
                Mathf.Cos(angle) * distance,
                0,
                Mathf.Sin(angle) * distance
            );

            // Transform to world space
            Vector3 worldPos = seedPoint + right * localPos.x + forward * localPos.z;
            worldPos = worldPos.normalized * radius;

            // Check if this position is already taken
            bool isDuplicate = false;
            foreach (var existing in allTileCenters)
            {
                if (Vector3.Distance(worldPos, existing) < hexSpacing * 0.5f)
                {
                    isDuplicate = true;
                    break;
                }
            }

            if (!isDuplicate)
            {
                allTileCenters.Add(worldPos);
                centerToIndex[worldPos] = allTileCenters.Count - 1;
            }
        }
    }

    /// <summary>
    /// Remove duplicate tile centers and ensure they're on the sphere
    /// </summary>
    private Vector3[] RemoveDuplicates(List<Vector3> centers, float radius)
    {
        List<Vector3> uniqueCenters = new List<Vector3>();
        float minDistance = radius * 0.1f; // Minimum distance between centers

        foreach (Vector3 center in centers)
        {
            bool isDuplicate = false;
            foreach (Vector3 existing in uniqueCenters)
            {
                if (Vector3.Distance(center, existing) < minDistance)
                {
                    isDuplicate = true;
                    break;
                }
            }

            if (!isDuplicate)
            {
                uniqueCenters.Add(center.normalized * radius);
            }
        }

        return uniqueCenters.ToArray();
    }

    /// <summary>
    /// Build neighbor relationships between tiles
    /// </summary>
    private void BuildNeighbors()
    {
        neighbors = new List<int>[tileCenters.Length];
        
        for (int i = 0; i < tileCenters.Length; i++)
        {
            neighbors[i] = new List<int>();
        }

        // Find neighbors by proximity
        float neighborThreshold = Radius * 0.2f; // Adjust based on hex spacing

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

        // Ensure each tile has at least 5-6 neighbors (hex grid property)
        for (int i = 0; i < neighbors.Length; i++)
        {
            if (neighbors[i].Count < 5)
            {
                Debug.LogWarning($"[SphericalHexGrid] Tile {i} has only {neighbors[i].Count} neighbors");
            }
        }
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