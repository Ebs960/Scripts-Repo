using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class MinimapGenerator : MonoBehaviour
{
    [Header("Inputs")]
    public Transform planetRoot;
    public int width = 512;
    public int height = 256;

    [Header("Colors")]
    public MinimapColorProvider colorProvider;

    [Header("Output")]
    public Texture2D minimapTexture;

    // Cached tile indices and their world positions
    private List<int> _tileIndices;
    private Vector3[] _tileDirs;

    public bool IsReady { get; private set; }

    public void Build()
    {
        if (IsReady) return;
        // Get number of tiles from TileDataHelper
        var helper = TileDataHelper.Instance;
        if (helper == null)
        {
            Debug.LogWarning("[MinimapGenerator] TileDataHelper.Instance is null.");
            return;
        }

        // --- Gather all planet tiles ---
        var grid = GameManager.Instance?.planetGenerator?.Grid;
        if (grid == null)
        {
            Debug.LogWarning("[MinimapGenerator] Planet grid not found.");
            return;
        }

        int tileCount = grid.tileCenters.Length;
        _tileIndices = new List<int>(tileCount);
        _tileDirs = new Vector3[tileCount];
        for (int i = 0; i < tileCount; i++)
        {
            _tileIndices.Add(i);
            Vector3 dir = (helper.GetTileCenter(i) - planetRoot.position).normalized;
            _tileDirs[i] = dir;
        }

        // Allocate texture
        if (minimapTexture == null || minimapTexture.width != width || minimapTexture.height != height)
        {
            minimapTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            minimapTexture.wrapMode = TextureWrapMode.Clamp;
            minimapTexture.filterMode = FilterMode.Bilinear;
        }

        // Generate minimap
        var pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            float lat = Mathf.PI * (v - 0.5f); // -pi/2..pi/2

            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                float lon = 2f * Mathf.PI * (u - 0.5f); // -pi..pi

                Vector3 dir = LatLonToDir(lat, lon);

                int idx = FindNearestTile(dir);
                int tileIndex = _tileIndices[idx];
                var (tileData, _) = TileDataHelper.Instance.GetTileData(tileIndex);

                Color c = colorProvider ? colorProvider.ColorFor(tileData) : DefaultColorFor(tileData);
                pixels[y * width + x] = c;
            }
        }

        minimapTexture.SetPixels(pixels);
        minimapTexture.Apply();
        IsReady = true;
    }

    public void Rebuild()
    {
        IsReady = false;
        Build();
    }

    public void UpdateTileOnMinimap(int tileIndex)
    {
        if (!IsReady) return;
        var worldPos = TileDataHelper.Instance.GetTileCenter(tileIndex);
        Vector2 uv = WorldPosToUV(worldPos);
        int cx = Mathf.FloorToInt(uv.x * width);
        int cy = Mathf.FloorToInt(uv.y * height);
        int r = Mathf.Max(1, width / 256);
        var (tileData, _) = TileDataHelper.Instance.GetTileData(tileIndex);
        Color c = colorProvider ? colorProvider.ColorFor(tileData) : DefaultColorFor(tileData);

        for (int y = cy - r; y <= cy + r; y++)
        {
            if (y < 0 || y >= height) continue;
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (x < 0 || x >= width) continue;
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r)
                    minimapTexture.SetPixel(x, y, c);
            }
        }
        minimapTexture.Apply();
    }

    private static Vector3 LatLonToDir(float lat, float lon)
    {
        float clat = Mathf.Cos(lat);
        return new Vector3(
            clat * Mathf.Cos(lon),
            Mathf.Sin(lat),
            clat * Mathf.Sin(lon)
        ).normalized;
    }

    private Vector2 WorldPosToUV(Vector3 worldPos)
    {
        Vector3 center = planetRoot ? planetRoot.position : Vector3.zero;
        Vector3 dir = (worldPos - center).normalized;
        float lat = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f));
        float lon = Mathf.Atan2(dir.z, dir.x);
        float u = (lon + Mathf.PI) / (2f * Mathf.PI);
        float v = (lat + Mathf.PI * 0.5f) / Mathf.PI;
        return new Vector2(u, v);
    }

    private int FindNearestTile(Vector3 dir)
    {
        int best = 0;
        float bestDot = -2f;
        for (int i = 0; i < _tileDirs.Length; i++)
        {
            float d = Vector3.Dot(dir, _tileDirs[i]);
            if (d > bestDot) { bestDot = d; best = i; }
        }
        return best;
    }

    private Color DefaultColorFor(HexTileData tile)
    {
        if (colorProvider != null) return colorProvider.ColorFor(tile);
        return new Color(0.3f, 0.5f, 0.8f, 1f);
    }
}
