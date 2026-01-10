using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Generates a flat, rectangular grid with horizontal wrap.
/// Tile centers live on the XZ plane.
/// </summary>
public class SphericalHexGrid
{
    public int TileCount => tileCenters != null ? tileCenters.Length : 0;
    public bool IsBuilt => tileCenters != null && tileCenters.Length > 0;
    public Vector3[] tileCenters;            // Center point of each tile (XZ plane)
    public List<int>[] neighbors;            // Neighbor indices for each tile
    public List<int>[] tileCorners;          // For each tile: list of indices (into CornerVertices) for corners (polygon, sorted)
    public List<Vector3> CornerVertices { get; private set; }  // List of all corner positions
    public int Width { get; private set; }
    public int Height { get; private set; }
    public float MapWidth { get; private set; }
    public float MapHeight { get; private set; }

    // Subdivision-based generator removed in flat-only refactor.

    /// <summary>
    /// Generate a flat rectangular grid using explicit map dimensions and tile resolution.
    /// </summary>
    /// <param name="tilesX">Number of tiles along X (width)</param>
    /// <param name="tilesZ">Number of tiles along Z (height)</param>
    /// <param name="mapWidth">World-space width (X extent)</param>
    /// <param name="mapHeight">World-space height (Z extent)</param>
    public void GenerateFlatGrid(int tilesX, int tilesZ, float mapWidth, float mapHeight)
    {
        Width = Mathf.Max(1, tilesX);
        Height = Mathf.Max(1, tilesZ);
        MapWidth = Mathf.Max(0.001f, mapWidth);
        MapHeight = Mathf.Max(0.001f, mapHeight);

        int tileCount = Width * Height;
        tileCenters     = new Vector3[tileCount];
        neighbors       = new List<int>[tileCount];
        tileCorners     = new List<int>[tileCount];
        CornerVertices  = new List<Vector3>();

        // Pointy-top hex sizing
        float sX = MapWidth / (Width * Mathf.Sqrt(3f));
        float sZ = MapHeight / (1.5f * (Height + 0.5f));
        float s = Mathf.Max(0.001f, Mathf.Min(sX, sZ));
        float w = Mathf.Sqrt(3f) * s; // horizontal spacing
        float h = 1.5f * s;           // vertical spacing

        float minX = -MapWidth * 0.5f;
        float minZ = -MapHeight * 0.5f;
        float offsetX = minX + w * 0.5f;
        float offsetZ = minZ + s; // top apex margin

        for (int r = 0; r < Height; r++)
        {
            for (int c = 0; c < Width; c++)
            {
                int index = r * Width + c;
                float worldX = offsetX + c * w + ((r & 1) == 1 ? w * 0.5f : 0f);
                float worldZ = offsetZ + r * h;
                Vector3 center = new Vector3(worldX, 0f, worldZ);
                tileCenters[index] = center;

                // 6-neighbor even-r offset with horizontal wrap
                var nbrs = new List<int>(6);
                int rUp = r - 1;
                int rDn = r + 1;
                int cL = (c - 1 + Width) % Width;
                int cR = (c + 1) % Width;
                nbrs.Add(r * Width + cL); // left
                nbrs.Add(r * Width + cR); // right
                if (rUp >= 0)
                {
                    if ((r & 1) == 0)
                    {
                        nbrs.Add(rUp * Width + c);     // up-left
                        nbrs.Add(rUp * Width + cR);     // up-right
                    }
                    else
                    {
                        nbrs.Add(rUp * Width + cL);    // up-left
                        nbrs.Add(rUp * Width + c);      // up-right
                    }
                }
                if (rDn < Height)
                {
                    if ((r & 1) == 0)
                    {
                        nbrs.Add(rDn * Width + c);     // down-left
                        nbrs.Add(rDn * Width + cR);     // down-right
                    }
                    else
                    {
                        nbrs.Add(rDn * Width + cL);    // down-left
                        nbrs.Add(rDn * Width + c);      // down-right
                    }
                }
                neighbors[index] = nbrs;

                // Hex corners (pointy-top), angles -30 + 60k degrees
                var corners = new List<int>(6);
                for (int k = 0; k < 6; k++)
                {
                    float angle = Mathf.Deg2Rad * (60f * k - 30f);
                    Vector3 corner = center + new Vector3(s * Mathf.Cos(angle), 0f, s * Mathf.Sin(angle));
                    corners.Add(AddCorner(corner));
                }
                tileCorners[index] = corners;
            }
        }

        Debug.Log($"[FlatHexGrid] Tiles: {tileCount} (Width: {Width}, Height: {Height})");
    }

    private int AddCorner(Vector3 corner)
    {
        CornerVertices.Add(corner);
        return CornerVertices.Count - 1;
    }

    public int GetTileAtPosition(Vector3 position)
    {
        if (Width <= 0 || Height <= 0 || tileCenters == null) return -1;

        // Use axial conversion for pointy-top hexes
        float sX = MapWidth / (Width * Mathf.Sqrt(3f));
        float sZ = MapHeight / (1.5f * (Height + 0.5f));
        float s = Mathf.Max(0.001f, Mathf.Min(sX, sZ));
        float w = Mathf.Sqrt(3f) * s;
        float h = 1.5f * s;

        float minX = -MapWidth * 0.5f;
        float minZ = -MapHeight * 0.5f;
        float offsetX = minX + w * 0.5f;
        float offsetZ = minZ + s;

        float lx = position.x - offsetX;
        float lz = position.z - offsetZ;

        // axial q,r
        float qf = (Mathf.Sqrt(3f) / 3f * lx - 1f / 3f * lz) / s;
        float rf = (2f / 3f * lz) / s;

        // cube rounding
        float xf = qf;
        float zf = rf;
        float yf = -xf - zf;
        int xi = Mathf.RoundToInt(xf);
        int yi = Mathf.RoundToInt(yf);
        int zi = Mathf.RoundToInt(zf);

        float xDiff = Mathf.Abs(xi - xf);
        float yDiff = Mathf.Abs(yi - yf);
        float zDiff = Mathf.Abs(zi - zf);
        if (xDiff > yDiff && xDiff > zDiff)
        {
            xi = -yi - zi;
        }
        else if (yDiff > zDiff)
        {
            yi = -xi - zi;
        }
        else
        {
            zi = -xi - yi;
        }

        // axial from cube
        int q = xi;
        int r = zi;

        // even-r offset conversion
        int row = r;
        int col = q + ((row & 1) == 0 ? (row / 2) : ((row + 1) / 2));
        // wrap horizontally
        col = ((col % Width) + Width) % Width;
        row = Mathf.Clamp(row, 0, Height - 1);
        int idx = row * Width + col;
        return (idx >= 0 && idx < tileCenters.Length) ? idx : -1;
    }

    // Corner helper APIs were removed as part of spherical-era cleanup; reintroduce if needed by mesh/UI systems.
}
