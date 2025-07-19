using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Converts SphericalHexGrid tiles into meshes.  When using
/// BuildWithPerTileBiomeData (…) each tile’s biome index is stored in the
/// vertex‑color red channel, normalised by (biomeCount‑1) so the shader can
/// recover the exact slice.
/// </summary>
public static class HexTileMeshBuilder
{
    /* ───────────────────────── Shared‑vertex build ───────────────────────── */
    public static Mesh Build(SphericalHexGrid grid,
                             out Vector2[]           perTileUV,
                             out Dictionary<int,List<int>> vertexToTiles)
    {
        int tileCount = grid.TileCount;
        List<Vector3> verts = new();
        List<Vector2> uvs   = new();
        List<Vector2> uvs1  = new();
        List<int>     tris  = new();
        perTileUV     = new Vector2[tileCount];
        vertexToTiles = new();

        Dictionary<Vector3,int> vLookup = new();

        for (int tile = 0; tile < tileCount; tile++)
        {
            int[] corners = grid.GetCornersOfTile(tile);
            Vector3 center = grid.tileCenters[tile];

            int cIdx = GetOrAdd(center,   vLookup, verts, uvs, uvs1);
            perTileUV[tile] = EquirectUV(center);

            int[] cornerV = new int[corners.Length];
            for (int i = 0; i < corners.Length; i++)
                cornerV[i] = GetOrAdd(grid.CornerVertices[corners[i]], vLookup, verts, uvs, uvs1);

            // ── planar UVs for this tile ──
            Vector3 centre = grid.tileCenters[tile];
            Vector3 xAxis  = Vector3.Normalize(Vector3.Cross(Vector3.up, centre));
            Vector3 yAxis  = Vector3.Cross(centre, xAxis);
            uvs1[cIdx] = new Vector2(0.5f, 0.5f);
            for (int c = 0; c < cornerV.Length; c++)
            {
                Vector3 corner = grid.CornerVertices[corners[c]];
                Vector3 local  = corner - centre;
                float u = Vector3.Dot(local, xAxis) * 0.5f + 0.5f;
                float v = Vector3.Dot(local, yAxis) * 0.5f + 0.5f;
                uvs1[cornerV[c]] = new Vector2(u, v);
            }

            for (int i = 0; i < corners.Length; i++)
            {
                int v1 = cornerV[(i+1)%corners.Length];
                int v2 = cornerV[i];

                tris.Add(cIdx); tris.Add(v1); tris.Add(v2);

                AddRef(vertexToTiles, cIdx, tile);
                AddRef(vertexToTiles, v1,  tile);
                AddRef(vertexToTiles, v2,  tile);
            }
        }
        return ToMesh("Shared‑Vertex", verts, uvs, uvs1, null, tris, tileCount);
    }

    /* ────────────── Separate‑vertex build (sharp edges) ────────────── */
    public static Mesh BuildWithSeparateVertices(SphericalHexGrid grid,
                                                 out Vector2[]           perTileUV,
                                                 out Dictionary<int,List<int>> vertexToTiles)
    {
        int tileCount = grid.TileCount;
        List<Vector3> verts = new();
        List<Vector2> uvs   = new();
        List<Vector2> uvs1  = new();
        List<int>     tris  = new();
        perTileUV     = new Vector2[tileCount];
        vertexToTiles = new();

        for (int tile = 0; tile < tileCount; tile++)
        {
            int[] corners = grid.GetCornersOfTile(tile);
            Vector3 center = grid.tileCenters[tile];

            int cIdx = Add(center, verts, uvs, uvs1);
            perTileUV[tile] = EquirectUV(center);

            int[] cornerV = new int[corners.Length];
            for (int i = 0; i < corners.Length; i++)
                cornerV[i] = Add(grid.CornerVertices[corners[i]], verts, uvs, uvs1);

            Vector3 centre = grid.tileCenters[tile];
            Vector3 xAxis  = Vector3.Normalize(Vector3.Cross(Vector3.up, centre));
            Vector3 yAxis  = Vector3.Cross(centre, xAxis);
            uvs1[cIdx] = new Vector2(0.5f, 0.5f);
            for (int c = 0; c < cornerV.Length; c++)
            {
                Vector3 corner = grid.CornerVertices[corners[c]];
                Vector3 local  = corner - centre;
                float u = Vector3.Dot(local, xAxis) * 0.5f + 0.5f;
                float v = Vector3.Dot(local, yAxis) * 0.5f + 0.5f;
                uvs1[cornerV[c]] = new Vector2(u, v);
            }

            for (int i = 0; i < corners.Length; i++)
            {
                int v1 = cornerV[(i+1)%corners.Length];
                int v2 = cornerV[i];
                tris.Add(cIdx); tris.Add(v1); tris.Add(v2);

                vertexToTiles[cIdx] = new() { tile };
                vertexToTiles[v1]   = new() { tile };
                vertexToTiles[v2]   = new() { tile };
            }
        }
        return ToMesh("Separate‑Vertex", verts, uvs, uvs1, null, tris, tileCount);
    }

    /* ─────── Per‑tile‑biome build (vertex colours store biome id) ─────── */
    public static Mesh BuildWithPerTileBiomeData(SphericalHexGrid grid,
                                                 Dictionary<int,int>   tileBiome,
                                                 Dictionary<int,float> tileElevation,
                                                 int                   biomeCount,
                                                 out Dictionary<int,List<int>> vertexToTiles)
    {
        int tileCount = grid.TileCount;
        List<Vector3> verts  = new();
        List<Vector2> uvs    = new();
        List<Vector2> uvs1   = new();
        List<Color>   colors = new();
        List<int>     tris   = new();
        vertexToTiles        = new();

        float normaliser = 1f / Mathf.Max(1, biomeCount - 1);

        for (int tile = 0; tile < tileCount; tile++)
        {
            int[] corners = grid.GetCornersOfTile(tile);
            Vector3 center = grid.tileCenters[tile];

            int biomeIdx = tileBiome.TryGetValue(tile, out int b) ? b : 0;
            // g = normalised elevation, b = edge weight
            float elevNorm = tileElevation.TryGetValue(tile, out var h) ? h : 0f;

            Color centreCol = new(biomeIdx * normaliser, elevNorm, 1f, 1f); // centre: weight=1
            Color edgeCol   = new(biomeIdx * normaliser, elevNorm, 0f, 1f); // edges: weight=0

            int cIdx = Add(center, verts, uvs, uvs1, colors, centreCol);

            int[] cornerV = new int[corners.Length];
            for (int i = 0; i < corners.Length; i++)
                cornerV[i] = Add(grid.CornerVertices[corners[i]], verts, uvs, uvs1, colors, edgeCol);

            Vector3 centre = grid.tileCenters[tile];
            Vector3 xAxis  = Vector3.Normalize(Vector3.Cross(Vector3.up, centre));
            Vector3 yAxis  = Vector3.Cross(centre, xAxis);
            uvs1[cIdx] = new Vector2(0.5f, 0.5f);
            for (int c = 0; c < cornerV.Length; c++)
            {
                Vector3 corner = grid.CornerVertices[corners[c]];
                Vector3 local  = corner - centre;
                float u = Vector3.Dot(local, xAxis) * 0.5f + 0.5f;
                float v = Vector3.Dot(local, yAxis) * 0.5f + 0.5f;
                uvs1[cornerV[c]] = new Vector2(u, v);
            }

            for (int i = 0; i < corners.Length; i++)
            {
                int v1 = cornerV[(i+1)%corners.Length];
                int v2 = cornerV[i];
                tris.Add(cIdx); tris.Add(v1); tris.Add(v2);

                vertexToTiles[cIdx] = new() { tile };
                vertexToTiles[v1]   = new() { tile };
                vertexToTiles[v2]   = new() { tile };
            }
        }
        return ToMesh("Per‑Tile‑Biome", verts, uvs, uvs1, colors, tris, tileCount);
    }

    /* ─────────────────────────── Helpers ─────────────────────────── */
    static int GetOrAdd(Vector3 pos, Dictionary<Vector3,int> lut,
                        List<Vector3> v, List<Vector2> u0, List<Vector2> u1)
    {
        if (lut.TryGetValue(pos, out int idx)) return idx;
        idx = v.Count;
        v.Add(pos);
        u0.Add(EquirectUV(pos));
        u1.Add(Vector2.zero);
        lut[pos] = idx;
        return idx;
    }

    static int Add(Vector3 pos, List<Vector3> v, List<Vector2> u0, List<Vector2> u1) =>
        Add(pos, v, u0, u1, null, Color.white);

    static int Add(Vector3 pos, List<Vector3> v, List<Vector2> u0, List<Vector2> u1,
                   List<Color> c, Color col)
    {
        int idx = v.Count;
        v.Add(pos);
        u0.Add(EquirectUV(pos));
        u1.Add(Vector2.zero);
        c?.Add(col);
        return idx;
    }

    static void AddRef(Dictionary<int,List<int>> map, int vert, int tile)
    {
        if (!map.TryGetValue(vert, out var list)) map[vert] = list = new();
        list.Add(tile);
    }

    static Mesh ToMesh(string label, List<Vector3> v, List<Vector2> u0, List<Vector2> u1,
                       List<Color> c, List<int> t, int tileCount)
    {
        Mesh m = new() { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(v);
        m.SetUVs(0, u0);
        if (u1 != null && u1.Count == v.Count)
            m.SetUVs(1, u1);
        if (c != null) m.SetColors(c);
        m.SetTriangles(t, 0);
        m.RecalculateNormals();
        Debug.Log($"[HexTileMeshBuilder] {label} mesh – verts:{v.Count} tris:{t.Count/3} tiles:{tileCount}");
        return m;
    }

    // ----------------------------------------------------------------------
    // Atmosphere shell builder
    public static Mesh BuildAtmosphereShell(SphericalHexGrid grid,
                                            float radius,
                                            float thickness = 0.02f)
    {
        var verts = new List<Vector3>();
        var tris  = new List<int>();

        verts.AddRange(grid.Vertices);     // deduped icosphere vertices
        tris.AddRange(grid.Triangles);     // original topology

        for (int i = 0; i < verts.Count; i++)
            verts[i] = verts[i].normalized * (radius + thickness);

        Mesh m = new Mesh { name = "AtmosphereShell",
                            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        return m;
    }

    static Vector2 EquirectUV(Vector3 n)
    {
        float u = (Mathf.Atan2(n.x, n.z) / Mathf.PI + 1f) * 0.5f;
        float v = Mathf.Asin(n.y) / Mathf.PI + 0.5f;
        return new(u, v);
    }
}
