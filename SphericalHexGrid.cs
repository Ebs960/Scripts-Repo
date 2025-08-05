using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Generates a true geodesic hexasphere (dual of subdivided icosahedron).
/// Each tile is centered at a vertex of the subdivided sphere mesh.
/// Tiles are pentagons (12, at icosahedron vertices) or hexagons (everywhere else).
/// </summary>
public class SphericalHexGrid
{
    public int TileCount => tileCenters != null ? tileCenters.Length : 0;
    public bool IsBuilt => tileCenters != null && tileCenters.Length > 0;
    public Vector3[] tileCenters;            // Center point of each tile (on sphere surface)
    public List<int>[] neighbors;            // Neighbor indices for each tile
    public List<int>[] tileCorners;          // For each tile: list of indices (into CornerVertices) for corners (polygon, sorted)
    public List<Vector3> Vertices { get; private set; }        // Deduped icosphere vertices
    public List<int>     Triangles { get; private set; }       // Icosphere topology
    public List<Vector3> CornerVertices { get; private set; }  // List of all corner positions
    public float Radius { get; private set; }
    public HashSet<int> pentagonIndices { get; private set; } // Indices of 12 pentagon tiles

    /// <summary>
    /// Main generation method: create the hexasphere by subdivision level.
    /// </summary>
    public void GenerateFromSubdivision(int subdivision, float radius)
    {
        Radius = radius;

        // Step 1: Build subdivided icosahedron mesh (returns vertices and faces)
        List<Vector3> meshVertices;
        List<int[]> meshFaces;
        BuildSubdividedIcosahedron(subdivision, out meshVertices, out meshFaces);

        // Step 2: Build dual graph - each unique vertex becomes a tile
        // Build: - For each vertex, list of incident faces
        Dictionary<int, List<int>> vertexToFaces = new Dictionary<int, List<int>>();
        for (int f = 0; f < meshFaces.Count; f++)
        {
            foreach (int v in meshFaces[f])
            {
                if (!vertexToFaces.ContainsKey(v)) vertexToFaces[v] = new List<int>();
                vertexToFaces[v].Add(f);
            }
        }

        int tileCount = meshVertices.Count;
        tileCenters     = new Vector3[tileCount];
        neighbors       = new List<int>[tileCount];
        tileCorners     = new List<int>[tileCount];
        Vertices        = meshVertices;              // icosphere verts
        Triangles       = meshFaces.SelectMany(f => f).ToList();
        CornerVertices  = new List<Vector3>();
        pentagonIndices = new HashSet<int>();

        // Step 3: For each tile (vertex), assign center, corners, neighbors
        Dictionary<Vector3, int> cornerLookup = new Dictionary<Vector3, int>(new Vector3EqualityComparer(1e-5f));

        for (int t = 0; t < tileCount; t++)
        {
            Vector3 center = meshVertices[t].normalized * Radius;
            tileCenters[t] = center;

            // -- Corners: for each face incident to this vertex, use face center as a corner
            var incidentFaces = vertexToFaces[t];
            var corners = new List<int>();
            var faceCenters = incidentFaces.Select(fIdx =>
            {
                var f = meshFaces[fIdx];
                Vector3 fc = (meshVertices[f[0]] + meshVertices[f[1]] + meshVertices[f[2]]) / 3f;
                // Ensure exact normalization to radius - prevents subtle distortions
                return fc.normalized * Radius;
            }).ToList();

            // Sort corners clockwise around tile center for mesh consistency
            faceCenters = SortCornersAroundCenter(faceCenters, center);

            foreach (var fc in faceCenters)
            {
                int cornerIdx;
                if (!cornerLookup.TryGetValue(fc, out cornerIdx))
                {
                    cornerIdx = CornerVertices.Count;
                    // Double ensure normalization before adding to corner vertices
                    CornerVertices.Add(fc.normalized * Radius);
                    cornerLookup[fc] = cornerIdx;
                }
                corners.Add(cornerIdx);
            }
            tileCorners[t] = corners;

            // -- Neighbors: for each edge connected to this vertex, find the other vertex and create neighbor relationship
            neighbors[t] = new List<int>();
            HashSet<int> neighborSet = new HashSet<int>();
            foreach (int fIdx in incidentFaces)
            {
                var f = meshFaces[fIdx];
                for (int i = 0; i < 3; i++)
                {
                    if (f[i] == t)
                    {
                        int prev = f[(i + 2) % 3];
                        int next = f[(i + 1) % 3];
                        if (prev != t) neighborSet.Add(prev);
                        if (next != t) neighborSet.Add(next);
                    }
                }
            }
            neighbors[t] = neighborSet.ToList();

            // -- Identify pentagons: if this vertex is an original icosahedron vertex, it's a pentagon
            if (incidentFaces.Count == 5)
                pentagonIndices.Add(t);
        }

        Debug.Log($"[SphericalHexGrid] Tiles: {tileCount} | Hexagons: {tileCount - pentagonIndices.Count} | Pentagons: {pentagonIndices.Count}");
    }

    /// <summary>
    /// Helper for approximate Vector3 key matching in dictionaries
    /// </summary>
    private class Vector3EqualityComparer : IEqualityComparer<Vector3>
    {
        private readonly float epsilon;
        public Vector3EqualityComparer(float epsilon) { this.epsilon = epsilon; }
        public bool Equals(Vector3 a, Vector3 b) => Vector3.SqrMagnitude(a - b) < epsilon * epsilon;
        public int GetHashCode(Vector3 obj) => obj.GetHashCode();
    }

    // Subdivision mesh construction
    private void BuildSubdividedIcosahedron(int n, out List<Vector3> outVerts, out List<int[]> outFaces)
    {
        // Adapted from https://github.com/daniman/Hexasphere-Unity (MIT) and RedBlobGames
        outVerts = new List<Vector3>();
        outFaces = new List<int[]>();
        // Build icosahedron
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        var rawVerts = new Vector3[]
        {
            new Vector3(-1,  t, 0), new Vector3(1,  t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1,  t), new Vector3(0,  1,  t), new Vector3(0, -1, -t), new Vector3(0,  1, -t),
            new Vector3( t, 0, -1), new Vector3( t, 0,  1), new Vector3(-t, 0, -1), new Vector3(-t, 0,  1)
        };
        for (int i = 0; i < rawVerts.Length; i++) rawVerts[i] = rawVerts[i].normalized;

        int[][] rawFaces = new int[][]
        {
            new []{0,11,5}, new []{0,5,1}, new []{0,1,7}, new []{0,7,10}, new []{0,10,11},
            new []{1,5,9}, new []{5,11,4}, new []{11,10,2}, new []{10,7,6}, new []{7,1,8},
            new []{3,9,4}, new []{3,4,2}, new []{3,2,6}, new []{3,6,8}, new []{3,8,9},
            new []{4,9,5}, new []{2,4,11}, new []{6,2,10}, new []{8,6,7}, new []{9,8,1}
        };

        // Subdivide each face
        Dictionary<(int,int), int> midpointCache = new();
        List<Vector3> verts = rawVerts.ToList();
        List<int[]> faces = new List<int[]>();
        foreach (var f in rawFaces)
            SubdivideFace(f[0], f[1], f[2], n, verts, faces, midpointCache);

        // Remove duplicate vertices and build index map
        List<Vector3> finalVerts = new List<Vector3>();
        Dictionary<Vector3, int> vertMap = new Dictionary<Vector3, int>(new Vector3EqualityComparer(1e-6f));
        int[] oldToNew = new int[verts.Count];
        for (int i = 0; i < verts.Count; i++)
        {
            Vector3 v = verts[i].normalized;
            if (!vertMap.TryGetValue(v, out int idx))
            {
                idx = finalVerts.Count;
                finalVerts.Add(v);
                vertMap[v] = idx;
            }
            oldToNew[i] = idx;
        }
        List<int[]> finalFaces = new List<int[]>();
        foreach (var f in faces)
            finalFaces.Add(new[] { oldToNew[f[0]], oldToNew[f[1]], oldToNew[f[2]] });

        outVerts = finalVerts;
        outFaces = finalFaces;
    }

    private void SubdivideFace(int v0, int v1, int v2, int depth, List<Vector3> verts, List<int[]> faces, Dictionary<(int,int), int> midpointCache)
    {
        if (depth == 1)
        {
            faces.Add(new int[] { v0, v1, v2 });
            return;
        }
        int a = GetMidpoint(v0, v1, verts, midpointCache);
        int b = GetMidpoint(v1, v2, verts, midpointCache);
        int c = GetMidpoint(v2, v0, verts, midpointCache);
        SubdivideFace(v0, a, c, depth - 1, verts, faces, midpointCache);
        SubdivideFace(a, v1, b, depth - 1, verts, faces, midpointCache);
        SubdivideFace(c, b, v2, depth - 1, verts, faces, midpointCache);
        SubdivideFace(a, b, c, depth - 1, verts, faces, midpointCache);
    }

    private int GetMidpoint(int i0, int i1, List<Vector3> verts, Dictionary<(int,int), int> cache)
    {
        var key = i0 < i1 ? (i0, i1) : (i1, i0);
        if (cache.TryGetValue(key, out int idx)) return idx;
        // Calculate midpoint and ensure perfect normalization
        Vector3 mid = ((verts[i0] + verts[i1]) * 0.5f).normalized;
        idx = verts.Count;
        verts.Add(mid);
        cache[key] = idx;
        return idx;
    }

    // Utility: Sort corners in order around the center, for correct polygon winding
    private List<Vector3> SortCornersAroundCenter(List<Vector3> corners, Vector3 center)
    {
        // Project onto tangent plane, then sort by angle
        Vector3 north = center.normalized;
        Vector3 refRight = Vector3.Cross(north, Vector3.up).normalized;
        if (refRight == Vector3.zero)
            refRight = Vector3.Cross(north, Vector3.forward).normalized;
        Vector3 refForward = Vector3.Cross(refRight, north).normalized;

        return corners.OrderBy(corner =>
        {
            Vector3 rel = (corner - center).normalized;
            float x = Vector3.Dot(rel, refRight);
            float y = Vector3.Dot(rel, refForward);
            return Mathf.Atan2(y, x);
        }).ToList();
    }

    // ----- API for mesh builder -----

    public int[] GetCornersOfTile(int tileIndex)
    {
        if (tileIndex < 0 || tileIndex >= tileCorners.Length)
            return new int[0];
        return tileCorners[tileIndex].ToArray();
    }

    public int GetTileAtPosition(Vector3 position)
    {
        Vector3 dir = position.normalized;
        float maxDot = -2f;
        int bestIdx = -1;
        for (int i = 0; i < tileCenters.Length; i++)
        {
            float d = Vector3.Dot(dir, tileCenters[i].normalized);
            if (d > maxDot)
            {
                maxDot = d;
                bestIdx = i;
            }
        }
        return bestIdx;
    }

    /// <summary>
    /// Get the world-space corner positions for a tile in the correct ring order.
    /// This ensures corner positions match between LineRenderer and tile prefab mesh.
    /// </summary>
    /// <param name="tileIndex">Index of the tile</param>
    /// <param name="transform">Transform to convert from local to world space</param>
    /// <returns>Array of world-space corner positions in ring order</returns>
    public Vector3[] GetTileWorldCorners(int tileIndex, Transform transform)
    {
        if (tileIndex < 0 || tileIndex >= tileCorners.Length)
            return new Vector3[0];

        int[] cornerIndices = tileCorners[tileIndex].ToArray();
        Vector3[] worldCorners = new Vector3[cornerIndices.Length];
        
        for (int i = 0; i < cornerIndices.Length; i++)
        {
            worldCorners[i] = transform.TransformPoint(CornerVertices[cornerIndices[i]]);
        }
        
        return worldCorners;
    }

    /// <summary>
    /// Get the local-space corner positions for a tile in the correct ring order.
    /// </summary>
    /// <param name="tileIndex">Index of the tile</param>
    /// <returns>Array of local-space corner positions in ring order</returns>
    public Vector3[] GetTileLocalCorners(int tileIndex)
    {
        if (tileIndex < 0 || tileIndex >= tileCorners.Length)
            return new Vector3[0];

        int[] cornerIndices = tileCorners[tileIndex].ToArray();
        Vector3[] localCorners = new Vector3[cornerIndices.Length];
        
        for (int i = 0; i < cornerIndices.Length; i++)
        {
            localCorners[i] = CornerVertices[cornerIndices[i]];
        }
        
        return localCorners;
    }
}
