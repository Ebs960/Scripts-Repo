using System.Collections.Generic;
using UnityEngine;

public enum MinimapDataSource
{
    Planet,
    Moon
}

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

    [Header("Data Source")]
    [Tooltip("Which generator to use for tile data")]
    public MinimapDataSource dataSource = MinimapDataSource.Planet;
    
    [Header("Zoom")]
    [Tooltip("Current zoom level - higher values show more detail of a smaller area")]
    public float zoomLevel = 1.0f;
    
    [Tooltip("Camera position for zoom focus (world coordinates)")]
    public Vector3 zoomCenter = Vector3.zero;

    // Cached tile indices and their world positions
    private List<int> _tileIndices;
    private Vector3[] _tileDirs;
    private IHexasphereGenerator _generator;

    public bool IsReady { get; private set; }

    /// <summary>
    /// Configure this minimap generator to use a specific planet/moon generator and root transform
    /// </summary>
    public void ConfigureDataSource(IHexasphereGenerator generator, Transform root, MinimapDataSource source)
    {
        _generator = generator;
        planetRoot = root;
        dataSource = source;
        zoomCenter = root ? root.position : Vector3.zero; // Set initial zoom center to planet/moon center
        IsReady = false; // Reset ready state when data source changes
    }
    
    /// <summary>
    /// Set the zoom level and optionally the zoom center
    /// </summary>
    public void SetZoomLevel(float newZoomLevel, Vector3? newZoomCenter = null)
    {
        zoomLevel = newZoomLevel;
        if (newZoomCenter.HasValue)
        {
            zoomCenter = newZoomCenter.Value;
        }
    }

    public void Build()
    {
        if (IsReady) return;
        
        // Ensure we have a configured data source
        if (_generator == null || planetRoot == null)
        {
            Debug.LogWarning($"[MinimapGenerator] Data source not configured. Call ConfigureDataSource() first.");
            return;
        }

        // Get number of tiles from TileDataHelper
        var helper = TileDataHelper.Instance;
        if (helper == null)
        {
            Debug.LogWarning("[MinimapGenerator] TileDataHelper.Instance is null.");
            return;
        }

        // --- Gather all tiles from the configured generator ---
        SphericalHexGrid grid = null;
        
        if (dataSource == MinimapDataSource.Planet)
        {
            grid = GameManager.Instance?.planetGenerator?.Grid;
        }
        else if (dataSource == MinimapDataSource.Moon)
        {
            grid = GameManager.Instance?.moonGenerator?.Grid;
        }
        
        if (grid == null)
        {
            Debug.LogWarning($"[MinimapGenerator] {dataSource} grid not found.");
            return;
        }

        int tileCount = grid.tileCenters.Length;
        _tileIndices = new List<int>(tileCount);
        _tileDirs = new Vector3[tileCount];
        for (int i = 0; i < tileCount; i++)
        {
            _tileIndices.Add(i);
            // Use the correct root position based on data source
            Vector3 rootPos = planetRoot ? planetRoot.position : Vector3.zero;
            Vector3 dir = (grid.tileCenters[i] - rootPos).normalized;
            _tileDirs[i] = dir;
        }

        // Allocate texture
        if (minimapTexture == null || minimapTexture.width != width || minimapTexture.height != height)
        {
            minimapTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            minimapTexture.wrapMode = TextureWrapMode.Clamp;
            minimapTexture.filterMode = FilterMode.Bilinear;
        }

        // Generate minimap with zoom support
        var pixels = new Color[width * height];
        
        // Calculate zoom-adjusted angular coverage
        float baseCoverage = 2f * Mathf.PI; // Full 360° longitude coverage at zoom level 1
        float latCoverage = Mathf.PI; // Full 180° latitude coverage at zoom level 1
        
        float currentLonCoverage = baseCoverage / zoomLevel;
        float currentLatCoverage = latCoverage / zoomLevel;
        
        // Convert zoom center to lat/lon for centering the zoomed view
        Vector3 zoomRootPos = planetRoot ? planetRoot.position : Vector3.zero;
        Vector3 zoomDir = (zoomCenter - zoomRootPos).normalized;
        float centerLat = Mathf.Asin(Mathf.Clamp(zoomDir.y, -1f, 1f));
        float centerLon = Mathf.Atan2(zoomDir.z, zoomDir.x);
        
        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            // Map v to latitude range centered on zoom center
            float lat = centerLat + (v - 0.5f) * currentLatCoverage;
            lat = Mathf.Clamp(lat, -Mathf.PI * 0.5f, Mathf.PI * 0.5f); // Clamp to valid latitude range

            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                // Map u to longitude range centered on zoom center
                float lon = centerLon + (u - 0.5f) * currentLonCoverage;
                // Normalize longitude to -π to π range
                while (lon > Mathf.PI) lon -= 2f * Mathf.PI;
                while (lon < -Mathf.PI) lon += 2f * Mathf.PI;

                Vector3 dir = LatLonToDir(lat, lon);

                int idx = FindNearestTile(dir);
                int tileIndex = _tileIndices[idx];
                
                // Get tile data from the correct generator based on data source
                HexTileData tileData;
                if (dataSource == MinimapDataSource.Planet && GameManager.Instance?.planetGenerator != null)
                {
                    tileData = GameManager.Instance.planetGenerator.GetHexTileData(tileIndex);
                }
                else if (dataSource == MinimapDataSource.Moon && GameManager.Instance?.moonGenerator != null)
                {
                    tileData = GameManager.Instance.moonGenerator.GetHexTileData(tileIndex);
                }
                else
                {
                    // Fallback if no generator available
                    tileData = new HexTileData { biome = Biome.Ocean };
                }

                // Create UV coordinates for this pixel
                Vector2 uv = new Vector2(u, v);

                // Use enhanced ColorProvider with UV support
                Color c;
                if (colorProvider != null)
                {
                    // Always pass UV - ColorProvider will decide whether to use texture or biome colors
                    c = colorProvider.ColorFor(tileData, uv);
                }
                else
                {
                    c = DefaultColorFor(tileData);
                }
                
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
        
        // Get world position from the correct generator
        Vector3 worldPos;
        if (dataSource == MinimapDataSource.Planet && GameManager.Instance?.planetGenerator != null)
        {
            worldPos = GameManager.Instance.planetGenerator.Grid.tileCenters[tileIndex];
        }
        else if (dataSource == MinimapDataSource.Moon && GameManager.Instance?.moonGenerator != null)
        {
            worldPos = GameManager.Instance.moonGenerator.Grid.tileCenters[tileIndex];
        }
        else
        {
            return; // Can't update without proper data source
        }
        
        Vector2 uv = WorldPosToUV(worldPos);
        int cx = Mathf.FloorToInt(uv.x * width);
        int cy = Mathf.FloorToInt(uv.y * height);
        int r = Mathf.Max(1, width / 256);
        
        // Get tile data from the correct generator
        HexTileData tileData;
        if (dataSource == MinimapDataSource.Planet && GameManager.Instance?.planetGenerator != null)
        {
            tileData = GameManager.Instance.planetGenerator.GetHexTileData(tileIndex);
        }
        else if (dataSource == MinimapDataSource.Moon && GameManager.Instance?.moonGenerator != null)
        {
            tileData = GameManager.Instance.moonGenerator.GetHexTileData(tileIndex);
        }
        else
        {
            return; // Can't update without proper data source
        }
        
        // Use enhanced ColorProvider with UV support
        Color c;
        if (colorProvider != null)
        {
            c = colorProvider.ColorFor(tileData, uv);
        }
        else
        {
            c = DefaultColorFor(tileData);
        }

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
