using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Generates a flat, rectangular grid with horizontal wrap.
/// Tile centers live on the XZ plane, with optional corner data for UI/mesh helpers.
/// </summary>
public class SphericalHexGrid
{
    public int TileCount => tileCenters != null ? tileCenters.Length : 0;
    public bool IsBuilt => tileCenters != null && tileCenters.Length > 0;
    public Vector3[] tileCenters;            // Center point of each tile (XZ plane)
    public List<int>[] neighbors;            // Neighbor indices for each tile
    public List<int>[] tileCorners;          // For each tile: list of indices (into CornerVertices) for corners (polygon, sorted)
    public List<Vector3> Vertices { get; private set; }        // Unused for flat maps (kept for compatibility)
    public List<int>     Triangles { get; private set; }       // Unused for flat maps (kept for compatibility)
    public List<Vector3> CornerVertices { get; private set; }  // List of all corner positions
    public float Radius { get; private set; }
    public HashSet<int> pentagonIndices { get; private set; } // Unused for flat maps (kept for compatibility)
    public int Width { get; private set; }
    public int Height { get; private set; }
    public float MapWidth { get; private set; }
    public float MapHeight { get; private set; }

    /// <summary>
    /// Main generation method: create a flat grid using subdivision to scale size.
    /// </summary>
    public void GenerateFromSubdivision(int subdivision, float radius)
    {
        Radius = radius;
        MapWidth = 2f * Mathf.PI * Mathf.Max(0.001f, radius);
        MapHeight = Mathf.PI * Mathf.Max(0.001f, radius);

        int targetTileCount = Mathf.Max(1, Mathf.RoundToInt(10f * Mathf.Pow(4f, Mathf.Max(0, subdivision - 2)) + 2f));
        Height = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(targetTileCount / 2f)));
        Width = Mathf.Max(1, Mathf.CeilToInt((float)targetTileCount / Height));

        int tileCount = Width * Height;
        tileCenters     = new Vector3[tileCount];
        neighbors       = new List<int>[tileCount];
        tileCorners     = new List<int>[tileCount];
        Vertices        = new List<Vector3>();
        Triangles       = new List<int>();
        CornerVertices  = new List<Vector3>();
        pentagonIndices = new HashSet<int>();

        float tileWidth = MapWidth / Width;
        float tileHeight = MapHeight / Height;
        float minX = -MapWidth * 0.5f;
        float minZ = -MapHeight * 0.5f;

        for (int z = 0; z < Height; z++)
        {
            for (int x = 0; x < Width; x++)
            {
                int index = z * Width + x;
                float worldX = minX + (x + 0.5f) * tileWidth;
                float worldZ = minZ + (z + 0.5f) * tileHeight;
                tileCenters[index] = new Vector3(worldX, 0f, worldZ);

                neighbors[index] = new List<int>();
                int left = (x - 1 + Width) % Width;
                int right = (x + 1) % Width;
                neighbors[index].Add(z * Width + left);
                neighbors[index].Add(z * Width + right);
                if (z > 0) neighbors[index].Add((z - 1) * Width + x);
                if (z < Height - 1) neighbors[index].Add((z + 1) * Width + x);

                var corners = new List<int>(4);
                Vector3 bottomLeft = new Vector3(minX + x * tileWidth, 0f, minZ + z * tileHeight);
                Vector3 bottomRight = new Vector3(minX + (x + 1) * tileWidth, 0f, minZ + z * tileHeight);
                Vector3 topRight = new Vector3(minX + (x + 1) * tileWidth, 0f, minZ + (z + 1) * tileHeight);
                Vector3 topLeft = new Vector3(minX + x * tileWidth, 0f, minZ + (z + 1) * tileHeight);

                corners.Add(AddCorner(bottomLeft));
                corners.Add(AddCorner(bottomRight));
                corners.Add(AddCorner(topRight));
                corners.Add(AddCorner(topLeft));
                tileCorners[index] = corners;
            }
        }

        Debug.Log($"[FlatGrid] Tiles: {tileCount} (Width: {Width}, Height: {Height})");
    }

    private int AddCorner(Vector3 corner)
    {
        CornerVertices.Add(corner);
        return CornerVertices.Count - 1;
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
        if (Width <= 0 || Height <= 0 || tileCenters == null) return -1;

        float u = (position.x + MapWidth * 0.5f) / MapWidth;
        float v = (position.z + MapHeight * 0.5f) / MapHeight;
        u = Mathf.Repeat(u, 1f);
        v = Mathf.Clamp01(v);

        int x = Mathf.Clamp(Mathf.FloorToInt(u * Width), 0, Width - 1);
        int z = Mathf.Clamp(Mathf.FloorToInt(v * Height), 0, Height - 1);
        return z * Width + x;
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
