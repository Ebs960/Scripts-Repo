using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Octree node for spatial partitioning of tile centers
/// </summary>
public class OctreeNode
{
    public Vector3 center;
    public float size;
    public List<int> tileIndices = new List<int>();
    public OctreeNode[] children = null;
    public bool isLeaf => children == null;
    
    public OctreeNode(Vector3 center, float size)
    {
        this.center = center;
        this.size = size;
    }
    
    public void Insert(int tileIndex, Vector3 tilePosition, Vector3[] allTileCenters, int maxDepth = 6, int maxTilesPerNode = 8)
    {
        tileIndices.Add(tileIndex);
        
        // If we haven't exceeded limits, don't subdivide
        if (tileIndices.Count <= maxTilesPerNode || maxDepth <= 0)
            return;
            
        // Create children if needed
        if (children == null)
        {
            children = new OctreeNode[8];
            float childSize = size * 0.5f;
            float offset = childSize * 0.5f;
            
            children[0] = new OctreeNode(center + new Vector3(-offset, -offset, -offset), childSize);
            children[1] = new OctreeNode(center + new Vector3(offset, -offset, -offset), childSize);
            children[2] = new OctreeNode(center + new Vector3(-offset, offset, -offset), childSize);
            children[3] = new OctreeNode(center + new Vector3(offset, offset, -offset), childSize);
            children[4] = new OctreeNode(center + new Vector3(-offset, -offset, offset), childSize);
            children[5] = new OctreeNode(center + new Vector3(offset, -offset, offset), childSize);
            children[6] = new OctreeNode(center + new Vector3(-offset, offset, offset), childSize);
            children[7] = new OctreeNode(center + new Vector3(offset, offset, offset), childSize);
            
            // Redistribute existing tiles to children
            var tilesToRedistribute = new List<int>(tileIndices);
            tileIndices.Clear();
            
            foreach (int idx in tilesToRedistribute)
            {
                Vector3 pos = allTileCenters[idx];
                int childIndex = GetChildIndex(pos);
                children[childIndex].Insert(idx, pos, allTileCenters, maxDepth - 1, maxTilesPerNode);
            }
        }
        else
        {
            // Already subdivided, insert into appropriate child
            int childIndex = GetChildIndex(tilePosition);
            children[childIndex].Insert(tileIndex, tilePosition, allTileCenters, maxDepth - 1, maxTilesPerNode);
        }
    }
    
    private int GetChildIndex(Vector3 position)
    {
        int index = 0;
        if (position.x > center.x) index |= 1;
        if (position.y > center.y) index |= 2;
        if (position.z > center.z) index |= 4;
        return index;
    }
    
    public int FindNearestTile(Vector3 queryPosition, Vector3[] allTileCenters)
    {
        int bestIndex = -1;
        float bestDot = -1f;
        FindNearestTileRecursive(queryPosition.normalized, allTileCenters, ref bestIndex, ref bestDot);
        return bestIndex;
    }
    
    private void FindNearestTileRecursive(Vector3 queryDir, Vector3[] allTileCenters, ref int bestIndex, ref float bestDot)
    {
        if (isLeaf)
        {
            // Check all tiles in this leaf
            foreach (int idx in tileIndices)
            {
                float dot = Vector3.Dot(queryDir, allTileCenters[idx].normalized);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    bestIndex = idx;
                }
            }
        }
        else
        {
            // Sort children by distance to query point for more efficient searching
            var childDistances = new (int childIdx, float distance)[8];
            for (int i = 0; i < 8; i++)
            {
                float dist = Vector3.Distance(queryDir, children[i].center.normalized);
                childDistances[i] = (i, dist);
            }
            
            // Sort by distance (closest first)
            System.Array.Sort(childDistances, (a, b) => a.distance.CompareTo(b.distance));
            
            // Search children in order of proximity
            foreach (var (childIdx, _) in childDistances)
            {
                children[childIdx].FindNearestTileRecursive(queryDir, allTileCenters, ref bestIndex, ref bestDot);
            }
        }
    }
}

public class IcoSphereGrid
{
    public int TileCount => tileCenters.Length;
    public Vector3[] tileCenters;             // Center point of each tile (on sphere surface)
    public List<int>[] neighbors;            // Neighbor indices for each tile
    public event Action<IcoSphereGrid> OnGeneration;  // Event fired when generation is complete

    private List<Vector3> vertices;          // All vertex positions during construction
    private List<int[]> faces;              // Triangle faces as index triples
    private OctreeNode octreeRoot;           // Spatial partitioning for fast lookups

    /// <summary>Generate an icosphere-based hex tile grid.</summary>
    public void Generate(int subdivisions, float radius)
    {
        // 1. Start with a regular icosahedron (12 vertices, 20 faces)
        InitializeIcosahedron(radius);

        // 2. Subdivide each triangular face 'subdivisions-1' times to reach the target frequency
        // If subdivisions = N, each original edge will be split into N segments (frequency N).
        int nu = subdivisions;
        if (nu < 1) nu = 1;
        SubdivideFaces(nu);

        // 3. Normalize all vertices to lie on the sphere of given radius
        for (int i = 0; i < vertices.Count; i++)
        {
            vertices[i] = vertices[i].normalized * radius;
        }

        // 4. Prepare tile centers (each unique vertex is a tile center)
        tileCenters = vertices.ToArray();

        // 5. Build neighbor list by examining shared edges in the triangular faces
        neighbors = new List<int>[tileCenters.Length];
        for (int i = 0; i < tileCenters.Length; i++)
        {
            neighbors[i] = new List<int>();
        }
        foreach (int[] tri in faces)
        {
            // For each triangle, add adjacency relations (undirected)
            int a = tri[0], b = tri[1], c = tri[2];
            AddNeighbor(a, b); AddNeighbor(a, c);
            AddNeighbor(b, a); AddNeighbor(b, c);
            AddNeighbor(c, a); AddNeighbor(c, b);
        }
        // Ensure each neighbor list has unique entries
        for (int i = 0; i < neighbors.Length; i++)
        {
            // You could use HashSet to auto-unique; here we simply remove duplicates manually
            HashSet<int> uniq = new HashSet<int>(neighbors[i]);
            neighbors[i] = new List<int>(uniq);
        }

        // 6. Build octree for fast spatial queries
        BuildOctree(radius);

        // 7. Signal completion
        OnGeneration?.Invoke(this);
    }

    /// <summary>
    /// Build the octree spatial partitioning structure for fast tile lookups
    /// </summary>
    private void BuildOctree(float radius)
    {
        if (tileCenters == null || tileCenters.Length == 0)
            return;
            
        // Create root node that encompasses the entire sphere
        float octreeSize = radius * 2.5f; // Make it slightly larger than the sphere
        octreeRoot = new OctreeNode(Vector3.zero, octreeSize);
        
        // Insert all tiles into the octree
        for (int i = 0; i < tileCenters.Length; i++)
        {
            octreeRoot.Insert(i, tileCenters[i], tileCenters);
        }
        
        Debug.Log($"[IcoSphereGrid] Built octree with {tileCenters.Length} tiles for fast spatial queries.");
    }

    /// Initialize base icosahedron geometry (12 vertices, 20 faces)
    private void InitializeIcosahedron(float radius)
    {
        vertices = new List<Vector3>();
        faces = new List<int[]>();

        // Icosahedron vertices (radius-scale). The golden ratio φ for coordinates.
        float t = (1.0f + Mathf.Sqrt(5.0f)) / 2.0f;
        // Create 12 vertices of a (approximately) unit icosahedron
        vertices.Add(new Vector3(-1, t, 0).normalized * radius);
        vertices.Add(new Vector3(1, t, 0).normalized * radius);
        vertices.Add(new Vector3(-1, -t, 0).normalized * radius);
        vertices.Add(new Vector3(1, -t, 0).normalized * radius);
        vertices.Add(new Vector3(0, -1, t).normalized * radius);
        vertices.Add(new Vector3(0, 1, t).normalized * radius);
        vertices.Add(new Vector3(0, -1, -t).normalized * radius);
        vertices.Add(new Vector3(0, 1, -t).normalized * radius);
        vertices.Add(new Vector3(t, 0, -1).normalized * radius);
        vertices.Add(new Vector3(t, 0, 1).normalized * radius);
        vertices.Add(new Vector3(-t, 0, -1).normalized * radius);
        vertices.Add(new Vector3(-t, 0, 1).normalized * radius);

        // Icosahedron faces (20 triangles, each defined by three vertex indices)
        faces.Add(new int[] { 0, 11, 5 });
        faces.Add(new int[] { 0, 5, 1 });
        faces.Add(new int[] { 0, 1, 7 });
        faces.Add(new int[] { 0, 7, 10 });
        faces.Add(new int[] { 0, 10, 11 });
        faces.Add(new int[] { 1, 5, 9 });
        faces.Add(new int[] { 5, 11, 4 });
        faces.Add(new int[] { 11, 10, 2 });
        faces.Add(new int[] { 10, 7, 6 });
        faces.Add(new int[] { 7, 1, 8 });
        faces.Add(new int[] { 3, 9, 4 });
        faces.Add(new int[] { 3, 4, 2 });
        faces.Add(new int[] { 3, 2, 6 });
        faces.Add(new int[] { 3, 6, 8 });
        faces.Add(new int[] { 3, 8, 9 });
        faces.Add(new int[] { 4, 9, 5 });
        faces.Add(new int[] { 2, 4, 11 });
        faces.Add(new int[] { 6, 2, 10 });
        faces.Add(new int[] { 8, 6, 7 });
        faces.Add(new int[] { 9, 8, 1 });
    }

    /// Subdivide each triangle face into smaller triangles according to frequency nu.
    private void SubdivideFaces(int nu)
    {
        if (nu <= 1) return; // nu=1 means no subdivision (just base icosahedron)

        // For performance, precompute edge division points for each base edge
        // Use a cache to avoid duplicating vertices on shared edges
        Dictionary<long, int[]> edgeDivisionCache = new Dictionary<long, int[]>();

        List<int[]> newFaces = new List<int[]>();
        foreach (int[] tri in faces)
        {
            int v0 = tri[0], v1 = tri[1], v2 = tri[2];
            // Generate interior points for this face using barycentric subdivision
            // We create grid of points: (i,j,k) with i+j+k = nu, on this triangle.
            // Ensure edges are shared via cache:
            int[] edge01 = GetEdgeDivisions(v0, v1, nu, edgeDivisionCache);
            int[] edge12 = GetEdgeDivisions(v1, v2, nu, edgeDivisionCache);
            int[] edge20 = GetEdgeDivisions(v2, v0, nu, edgeDivisionCache);
            // edge01[0] == v0, edge01[nu] == v1
            // edge12[0] == v1, edge12[nu] == v2
            // edge20[0] == v2, edge20[nu] == v0

            // Now create faces for each small triangle in the subdivided grid
            for (int i = 0; i < nu; i++)
            {
                for (int j = 0; j < nu - i; j++)
                {
                    // Vertices of a small triangle (upper-left oriented) within this face:
                    int a = edge20[i];          // point along v2->v0
                    int b = edge20[i + 1];      // next point along v2->v0
                    int c = edge01[j + i + 1];  // point along v0->v1 one step down (i+j+1)
                    newFaces.Add(new int[] { a, c, b });
                    if (j < nu - i - 1)
                    {
                        // Second triangle (lower-right oriented) in the grid square
                        int d = edge01[j + i + 2];    // next point along v0->v1
                        int e = edge12[j + 1 + i];    // point along v1->v2 (offset by i on that edge)
                        int f = edge12[j + i];        // current point along v1->v2
                        newFaces.Add(new int[] { b, d, c });
                        newFaces.Add(new int[] { c, d, e });
                        newFaces.Add(new int[] { c, e, f });
                    }
                }
            }
        }
        faces = newFaces;
    }

    /// Divide an edge between vertices i0 and i1 into `nu` segments, returning an array of vertex indices.
    /// The returned array has length nu+1, including the end-points.
    private int[] GetEdgeDivisions(int i0, int i1, int nu, Dictionary<long, int[]> cache)
    {
        // Order the key consistently (smaller, larger) to handle undirected edge
        long key = ((long)Mathf.Min(i0, i1) << 32) | (uint)Mathf.Max(i0, i1);
        if (cache.TryGetValue(key, out int[] cached))
        {
            return cached;
        }
        // Not in cache: create the subdivision points along this edge
        Vector3 v0 = vertices[i0];
        Vector3 v1 = vertices[i1];
        int[] indices = new int[nu + 1];
        indices[0] = i0;
        indices[nu] = i1;
        for (int k = 1; k < nu; k++)
        {
            // Use spherical interpolation to place points on the great-circle path
            Vector3 newPoint = Vector3.Slerp(v0, v1, k / (float)nu);
            // Add new vertex and record index
            indices[k] = vertices.Count;
            vertices.Add(newPoint);
        }
        cache[key] = indices;
        return indices;
    }

    /// Add neighbor j to i's list (skip if already present)
    private void AddNeighbor(int i, int j)
    {
        if (!neighbors[i].Contains(j))
            neighbors[i].Add(j);
    }

    /// Get the tile index whose center is nearest to the given position/direction.
    /// Now uses fast octree search instead of brute-force iteration.
    public int GetTileAtPosition(Vector3 position, bool localSpace = false)
    {
        Vector3 dir = position;
        if (!localSpace)
        {
            // Convert world position to local direction (assuming planet centered at origin)
            // If planet is not at origin, subtract planet center position accordingly
            dir = position - Vector3.zero; // replace Vector3.zero with planet center if needed
        }
        dir.Normalize();
        
        // Use octree for fast spatial lookup
        if (octreeRoot != null)
        {
            return octreeRoot.FindNearestTile(dir, tileCenters);
        }
        
        // Fallback to brute-force if octree not built (shouldn't happen)
        Debug.LogWarning("[IcoSphereGrid] Octree not available, falling back to brute-force search!");
        int nearestIndex = -1;
        float maxDot = -1f;
        for (int i = 0; i < tileCenters.Length; i++)
        {
            float d = Vector3.Dot(dir, tileCenters[i].normalized);
            if (d > maxDot)
            {
                maxDot = d;
                nearestIndex = i;
            }
        }
        return nearestIndex;
    }

    /// Utility: Get latitude/longitude (in degrees) of the given tile index.
    public Vector2 GetTileLatLon(int tileIndex)
    {
        Vector3 p = tileCenters[tileIndex].normalized;
        float latitude = Mathf.Asin(p.y) * Mathf.Rad2Deg;
        float longitude = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg;
        return new Vector2(latitude, longitude);
    }
}
