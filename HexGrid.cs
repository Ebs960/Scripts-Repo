using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Blittable snapshot of the hex-grid math constants needed by Burst jobs.
/// Created once via <see cref="HexGrid.GetLookupData"/> and passed by value to jobs.
/// </summary>
public struct HexGridLookupData
{
    public int gridWidth;
    public int gridHeight;
    public int tileCount;
    public float s;
    public float offsetX;
    public float offsetZ;
    public float minZCorner;
    public float maxZCorner;
    public float sqrt3over3_div_s;
    public float inv3_div_s;
    public float twothirds_div_s;
}

/// <summary>
/// Generates a flat, rectangular hex grid with horizontal wrap.
/// Tile centers live on the XZ plane.
/// </summary>
public class HexGrid
{
    public int TileCount => tileCenters != null ? tileCenters.Length : 0;
    public bool IsBuilt => tileCenters != null && tileCenters.Length > 0;
    public Vector3[] tileCenters;            // Center point of each tile (XZ plane)
    public List<int>[] neighbors;            // Neighbor indices for each tile
    public List<int>[] tileCorners;          // For each tile: list of indices (into CornerVertices) for corners (polygon, sorted)
    public List<Vector3> CornerVertices { get; private set; }  // List of all corner positions
    private Dictionary<long, int> cornerLookup; // quantized XZ -> index
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
        _lookupValid = false;

        Width = Mathf.Max(1, tilesX);
        Height = Mathf.Max(1, tilesZ);
        MapWidth = Mathf.Max(0.001f, mapWidth);
        MapHeight = Mathf.Max(0.001f, mapHeight);

        int tileCount = Width * Height;
        tileCenters     = new Vector3[tileCount];
        neighbors       = new List<int>[tileCount];
        tileCorners     = new List<int>[tileCount];
        CornerVertices  = new List<Vector3>();
        cornerLookup = new Dictionary<long, int>();

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
}

    private int AddCorner(Vector3 corner)
    {
        // Quantize X,Z to avoid floating point duplicates. Keep 1e-4 precision (~0.1mm at world scale).
        int qx = Mathf.RoundToInt(corner.x * 10000f);
        int qz = Mathf.RoundToInt(corner.z * 10000f);
        long key = ((long)qx << 32) ^ (uint)qz;
        if (cornerLookup.TryGetValue(key, out int existing))
            return existing;

        CornerVertices.Add(corner);
        int idx = CornerVertices.Count - 1;
        cornerLookup[key] = idx;
        return idx;
    }

    // Cached lookup constants — populated once in GenerateFlatGrid(), used by GetTileAtPosition().
    private HexGridLookupData _lookup;
    private bool _lookupValid;

    public int GetTileAtPosition(Vector3 position)
    {
        if (Width <= 0 || Height <= 0 || tileCenters == null)
        {
            Debug.LogWarning("[HexGrid] GetTileAtPosition called but grid is not built.");
            return -1;
        }

        if (!_lookupValid)
        {
            _lookup = GetLookupData();
            _lookupValid = true;
        }

        if (position.z < _lookup.minZCorner || position.z > _lookup.maxZCorner)
            return -1;

        float lx = position.x - _lookup.offsetX;
        float lz = position.z - _lookup.offsetZ;

        float qf = _lookup.sqrt3over3_div_s * lx - _lookup.inv3_div_s * lz;
        float rf = _lookup.twothirds_div_s * lz;

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
            xi = -yi - zi;
        else if (yDiff > zDiff)
            yi = -xi - zi;
        else
            zi = -xi - yi;

        int row = zi;
        int col = xi + ((row & 1) == 0 ? (row / 2) : ((row + 1) / 2));
        col = ((col % Width) + Width) % Width;
        if (row < 0 || row >= Height)
            return -1;
        int idx = row * Width + col;
        if (idx >= 0 && idx < tileCenters.Length) return idx;
        Debug.LogWarning($"[HexGrid] Computed tile out of range. q={xi} r={zi} col={col} row={row} idx={idx} Width={Width} Height={Height} TileCount={tileCenters.Length}");
        return -1;
    }

    /// <summary>
    /// Returns a blittable struct containing all pre-computed constants needed by Burst jobs
    /// to replicate GetTileAtPosition without any managed references.
    /// </summary>
    public HexGridLookupData GetLookupData()
    {
        float sX = MapWidth / (Width * Mathf.Sqrt(3f));
        float sZ = MapHeight / (1.5f * (Height + 0.5f));
        float s = Mathf.Max(0.001f, Mathf.Min(sX, sZ));
        float w = Mathf.Sqrt(3f) * s;
        float h = 1.5f * s;
        float minX = -MapWidth * 0.5f;
        float minZ = -MapHeight * 0.5f;

        return new HexGridLookupData
        {
            gridWidth = Width,
            gridHeight = Height,
            tileCount = Width * Height,
            s = s,
            offsetX = minX + w * 0.5f,
            offsetZ = minZ + s,
            minZCorner = minZ,
            maxZCorner = minZ + ((Height - 1) * h + 2f * s),
            sqrt3over3_div_s = 1.7320508f / (3f * s),
            inv3_div_s = 1f / (3f * s),
            twothirds_div_s = 2f / (3f * s),
        };
    }

    // Corner helper APIs were removed as part of spherical-era cleanup; reintroduce if needed by mesh/UI systems.
}
