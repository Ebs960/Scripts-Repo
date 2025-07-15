using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Converts IcoSphereGrid tiles into a single mesh where each hex tile has
/// unique vertex indices (so we can assign unique UVs & textures per tile).
/// </summary>
public static class HexTileMeshBuilder
{
    public static Mesh Build(IcoSphereGrid grid, out Vector2[] perTileUV)
    {
        int tileCount = grid.TileCount;
        List<Vector3> verts = new();
        List<int> tris = new();
        perTileUV = new Vector2[tileCount];   // center-UV for lookup

        // 1. Each tile = one hex/pent. Grab its perimeter vertices.
        for (int tile = 0; tile < tileCount; tile++)
        {
            var cornerIdx = grid.GetCornersOfTile(tile); // returns int[6] or [5]
            Vector3 center = grid.tileCenters[tile];
            Vector2 uvCenter = EquirectUV(center);
            perTileUV[tile] = uvCenter;

            int vStart = verts.Count;
            // Fan triangulate center → edge pairs
            for (int c = 0; c < cornerIdx.Length; c++)
            {
                int a = cornerIdx[c];
                int b = cornerIdx[(c + 1) % cornerIdx.Length];

                verts.Add(center);
                verts.Add(grid.Vertices[a]);
                verts.Add(grid.Vertices[b]);

                tris.Add(vStart); tris.Add(vStart + 1); tris.Add(vStart + 2);
                vStart += 3;
            }
        }

        Mesh m = new();
        m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);

        // simple normals – later you’ll displace verts for height
        m.RecalculateNormals();
        return m;
    }

    static Vector2 EquirectUV(Vector3 n)
    {
        float u = (Mathf.Atan2(n.x, n.z) / Mathf.PI + 1) * 0.5f;
        float v = (Mathf.Asin(n.y) / Mathf.PI) + 0.5f;
        return new Vector2(u, v);
    }
}
