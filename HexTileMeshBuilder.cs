using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Converts IcoSphereGrid tiles into a single mesh where each hex tile has
/// unique vertex indices (so we can assign unique UVs & textures per tile).
/// </summary>
public static class HexTileMeshBuilder
{
    public static Mesh Build(IcoSphereGrid grid, out Vector2[] perTileUV, out Dictionary<int, List<int>> vertexToTiles)
    {
        int tileCount = grid.TileCount;
        List<Vector3> verts = new();
        List<Vector2> uvs = new();
        List<int> tris = new();
        perTileUV = new Vector2[tileCount];   // center-UV for lookup
        vertexToTiles = new Dictionary<int, List<int>>();

        // Create a proper mesh where tiles share vertices at boundaries
        // This prevents the "bunched up" appearance from overlapping geometry
        
        // First, create a vertex lookup to avoid duplicates
        Dictionary<Vector3, int> vertexLookup = new Dictionary<Vector3, int>();
        
        for (int tile = 0; tile < tileCount; tile++)
        {
            var cornerIdx = grid.GetCornersOfTile(tile);
            Vector3 center = grid.tileCenters[tile];
            Vector2 uvCenter = EquirectUV(center);
            perTileUV[tile] = uvCenter;

            // Add center vertex (unique per tile)
            int centerVertexIdx;
            if (!vertexLookup.ContainsKey(center))
            {
                centerVertexIdx = verts.Count;
                verts.Add(center);
                uvs.Add(EquirectUV(center));
                vertexLookup[center] = centerVertexIdx;
            }
            else
            {
                centerVertexIdx = vertexLookup[center];
            }

            // Add corner vertices (shared between tiles)
            int[] cornerVertexIndices = new int[cornerIdx.Length];
            for (int c = 0; c < cornerIdx.Length; c++)
            {
                Vector3 cornerPos = grid.Vertices[cornerIdx[c]];
                if (!vertexLookup.ContainsKey(cornerPos))
                {
                    cornerVertexIndices[c] = verts.Count;
                    verts.Add(cornerPos);
                    uvs.Add(EquirectUV(cornerPos));
                    vertexLookup[cornerPos] = cornerVertexIndices[c];
                }
                else
                {
                    cornerVertexIndices[c] = vertexLookup[cornerPos];
                }
            }

            // Create triangles from center to each edge
            for (int c = 0; c < cornerIdx.Length; c++)
            {
                int corner1Idx = cornerVertexIndices[c];
                int corner2Idx = cornerVertexIndices[(c + 1) % cornerIdx.Length];

                tris.Add(centerVertexIdx);
                tris.Add(corner1Idx);
                tris.Add(corner2Idx);

                // Track which tiles use each mesh vertex
                if (!vertexToTiles.ContainsKey(centerVertexIdx)) vertexToTiles[centerVertexIdx] = new List<int>();
                if (!vertexToTiles.ContainsKey(corner1Idx)) vertexToTiles[corner1Idx] = new List<int>();
                if (!vertexToTiles.ContainsKey(corner2Idx)) vertexToTiles[corner2Idx] = new List<int>();
                
                vertexToTiles[centerVertexIdx].Add(tile);
                vertexToTiles[corner1Idx].Add(tile);
                vertexToTiles[corner2Idx].Add(tile);
            }
        }

        Mesh m = new();
        m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(verts);
        m.SetUVs(0, uvs);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        
        Debug.Log($"[HexTileMeshBuilder] Built mesh with {verts.Count} vertices, {tris.Count/3} triangles for {tileCount} tiles");
        return m;
    }

    static Vector2 EquirectUV(Vector3 n)
    {
        float u = (Mathf.Atan2(n.x, n.z) / Mathf.PI + 1) * 0.5f;
        float v = (Mathf.Asin(n.y) / Mathf.PI) + 0.5f;
        return new Vector2(u, v);
    }
}
