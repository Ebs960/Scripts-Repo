using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Converts SphericalHexGrid tiles into a single mesh with proper topology.
/// Stores biome data per-tile instead of relying on UV-based texture sampling.
/// </summary>
public static class HexTileMeshBuilder
{
    /// <summary>
    /// Build mesh with shared vertices for memory efficiency
    /// </summary>
    public static Mesh Build(SphericalHexGrid grid, out Vector2[] perTileUV, out Dictionary<int, List<int>> vertexToTiles)
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

    /// <summary>
    /// Build mesh with separate vertices for each tile to ensure clear boundaries.
    /// This trades memory for visual clarity and proper biome boundaries.
    /// </summary>
    public static Mesh BuildWithSeparateVertices(SphericalHexGrid grid, out Vector2[] perTileUV, out Dictionary<int, List<int>> vertexToTiles)
    {
        int tileCount = grid.TileCount;
        List<Vector3> verts = new();
        List<Vector2> uvs = new();
        List<int> tris = new();
        perTileUV = new Vector2[tileCount];   // center-UV for lookup
        vertexToTiles = new Dictionary<int, List<int>>();

        // Create separate vertices for each tile to ensure clear boundaries
        // This will use more memory but provide sharper tile boundaries
        
        for (int tile = 0; tile < tileCount; tile++)
        {
            var cornerIdx = grid.GetCornersOfTile(tile);
            Vector3 center = grid.tileCenters[tile];
            Vector2 uvCenter = EquirectUV(center);
            perTileUV[tile] = uvCenter;

            // Add center vertex (unique per tile)
            int centerVertexIdx = verts.Count;
            verts.Add(center);
            uvs.Add(EquirectUV(center));

            // Add corner vertices (separate for each tile)
            int[] cornerVertexIndices = new int[cornerIdx.Length];
            for (int c = 0; c < cornerIdx.Length; c++)
            {
                Vector3 cornerPos = grid.Vertices[cornerIdx[c]];
                cornerVertexIndices[c] = verts.Count;
                verts.Add(cornerPos);
                uvs.Add(EquirectUV(cornerPos));
            }

            // Create triangles from center to each edge
            for (int c = 0; c < cornerIdx.Length; c++)
            {
                int corner1Idx = cornerVertexIndices[c];
                int corner2Idx = cornerVertexIndices[(c + 1) % cornerIdx.Length];

                tris.Add(centerVertexIdx);
                tris.Add(corner1Idx);
                tris.Add(corner2Idx);

                // Track which tiles use each mesh vertex (each vertex belongs to exactly one tile)
                vertexToTiles[centerVertexIdx] = new List<int> { tile };
                vertexToTiles[corner1Idx] = new List<int> { tile };
                vertexToTiles[corner2Idx] = new List<int> { tile };
            }
        }

        Mesh m = new();
        m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(verts);
        m.SetUVs(0, uvs);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        
        Debug.Log($"[HexTileMeshBuilder] Built separate-vertex mesh with {verts.Count} vertices, {tris.Count/3} triangles for {tileCount} tiles");
        return m;
    }

    /// <summary>
    /// Build mesh with per-tile biome data stored as vertex colors instead of UV-based texture sampling.
    /// This provides more accurate biome mapping for the hexasphere.
    /// </summary>
    public static Mesh BuildWithPerTileBiomeData(SphericalHexGrid grid, Dictionary<int, int> tileBiomeIndices, out Dictionary<int, List<int>> vertexToTiles)
    {
        int tileCount = grid.TileCount;
        List<Vector3> verts = new();
        List<Vector2> uvs = new();
        List<Color> colors = new(); // Store biome indices as vertex colors
        List<int> tris = new();
        vertexToTiles = new Dictionary<int, List<int>>();

        // Create separate vertices for each tile to ensure proper biome boundaries
        for (int tile = 0; tile < tileCount; tile++)
        {
            var cornerIdx = grid.GetCornersOfTile(tile);
            Vector3 center = grid.tileCenters[tile];
            
            // Get biome index for this tile (default to 0 if not found)
            int biomeIndex = tileBiomeIndices.ContainsKey(tile) ? tileBiomeIndices[tile] : 0;
            Color biomeColor = new Color(biomeIndex / 255f, 0, 0, 1); // Store as red channel

            // Add center vertex (unique per tile)
            int centerVertexIdx = verts.Count;
            verts.Add(center);
            uvs.Add(EquirectUV(center));
            colors.Add(biomeColor);

            // Add corner vertices (separate for each tile)
            int[] cornerVertexIndices = new int[cornerIdx.Length];
            for (int c = 0; c < cornerIdx.Length; c++)
            {
                Vector3 cornerPos = grid.Vertices[cornerIdx[c]];
                cornerVertexIndices[c] = verts.Count;
                verts.Add(cornerPos);
                uvs.Add(EquirectUV(cornerPos));
                colors.Add(biomeColor); // Same biome for all vertices of this tile
            }

            // Create triangles from center to each edge
            for (int c = 0; c < cornerIdx.Length; c++)
            {
                int corner1Idx = cornerVertexIndices[c];
                int corner2Idx = cornerVertexIndices[(c + 1) % cornerIdx.Length];

                tris.Add(centerVertexIdx);
                tris.Add(corner1Idx);
                tris.Add(corner2Idx);

                // Track which tiles use each mesh vertex (each vertex belongs to exactly one tile)
                vertexToTiles[centerVertexIdx] = new List<int> { tile };
                vertexToTiles[corner1Idx] = new List<int> { tile };
                vertexToTiles[corner2Idx] = new List<int> { tile };
            }
        }

        Mesh m = new();
        m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(verts);
        m.SetUVs(0, uvs);
        m.SetColors(colors); // Set vertex colors for biome data
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        
        Debug.Log($"[HexTileMeshBuilder] Built per-tile biome mesh with {verts.Count} vertices, {tris.Count/3} triangles for {tileCount} tiles");
        return m;
    }

    /// <summary>
    /// Convert 3D direction to equirectangular UV coordinates
    /// </summary>
    static Vector2 EquirectUV(Vector3 n)
    {
        float u = (Mathf.Atan2(n.x, n.z) / Mathf.PI + 1) * 0.5f;
        float v = (Mathf.Asin(n.y) / Mathf.PI) + 0.5f;
        return new Vector2(u, v);
    }
}
