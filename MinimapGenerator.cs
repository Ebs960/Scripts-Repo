using System.Collections.Generic;
using UnityEngine;

public enum MinimapDataSource
{
    Planet,
    Moon,
    PlanetByIndex  // For multi-planet system with specific planet index
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
    
    [Header("Performance")]
    [Tooltip("High-resolution master texture for zooming (generated once)")]
    public int masterTextureWidth = 512;  // PERFORMANCE FIX: Reduced from 2048
    [Tooltip("High-resolution master texture for zooming (generated once)")]
    public int masterTextureHeight = 256; // PERFORMANCE FIX: Reduced from 1024
    
    private Texture2D _masterTexture; // High-res version for zooming
    private bool _masterTextureReady = false;

    [Header("Data Source")]
    [Tooltip("Which generator to use for tile data")]
    public MinimapDataSource dataSource = MinimapDataSource.Planet;
    
    [Header("Multi-Planet Support")]
    [Tooltip("Planet index for multi-planet system (only used when dataSource is PlanetByIndex)")]
    public int planetIndex = 0;
    
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
    public void ConfigureDataSource(IHexasphereGenerator generator, Transform root, MinimapDataSource source, int planetIdx = 0)
    {
        _generator = generator;
        planetRoot = root;
        dataSource = source;
        planetIndex = planetIdx;
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

        // Build master texture once if not ready
        if (!_masterTextureReady)
        {
            BuildMasterTexture();
        }
        
        // Generate display texture by sampling from master texture
        GenerateDisplayTexture();
        
        IsReady = true;
    }
    
    private void BuildMasterTexture()
    {
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
            grid = GameManager.Instance?.GetCurrentPlanetGenerator()?.Grid;
        }
        else if (dataSource == MinimapDataSource.Moon)
        {
            grid = GameManager.Instance?.GetCurrentMoonGenerator()?.Grid;
        }
        else if (dataSource == MinimapDataSource.PlanetByIndex)
        {
            // Get grid from specific planet by index
            var planetGen = GameManager.Instance?.GetPlanetGenerator(planetIndex);
            grid = planetGen?.Grid;
        }
        
        if (grid == null)
        {
            Debug.LogWarning($"[MinimapGenerator] {dataSource} grid not found. Planet index: {planetIndex}");
            return;
        }

        int tileCount = grid.tileCenters.Length;
        _tileIndices = new List<int>(tileCount);
        _tileDirs = new Vector3[tileCount];
        for (int i = 0; i < tileCount; i++)
        {
            _tileIndices.Add(i);
            // Compute direction relative to the planet's own center so off-origin planets work correctly
            Vector3 dir = (grid.tileCenters[i] - planetRoot.position).normalized;
            _tileDirs[i] = dir;
        }
        
        Debug.Log($"[MinimapGenerator] Building minimap for {dataSource}, planet index {planetIndex}, {tileCount} tiles");
        
        // DEBUG: Check ColorProvider configuration
        if (colorProvider != null)
        {
            Debug.Log($"[MinimapGenerator] ColorProvider found: Render Mode = {colorProvider.renderMode}");
        }
        else
        {
            Debug.LogWarning($"[MinimapGenerator] No ColorProvider assigned! Will use default colors.");
        }

        // Allocate master texture (high resolution)
        if (_masterTexture == null || _masterTexture.width != masterTextureWidth || _masterTexture.height != masterTextureHeight)
        {
            _masterTexture = new Texture2D(masterTextureWidth, masterTextureHeight, TextureFormat.RGBA32, false);
            _masterTexture.wrapMode = TextureWrapMode.Clamp;
            _masterTexture.filterMode = FilterMode.Bilinear;
        }

        // Generate master texture at full resolution (this is expensive but done once)
        var pixels = new Color[masterTextureWidth * masterTextureHeight];
        
        for (int y = 0; y < masterTextureHeight; y++)
        {
            float v = (y + 0.5f) / masterTextureHeight;
            float lat = Mathf.PI * (v - 0.5f); // Full latitude range for master texture

            for (int x = 0; x < masterTextureWidth; x++)
            {
                float u = (x + 0.5f) / masterTextureWidth;
                float lon = 2f * Mathf.PI * (u - 0.5f); // Full longitude range for master texture

                Vector3 dir = LatLonToDir(lat, lon);

                int idx = FindNearestTile(dir);
                int tileIndex = _tileIndices[idx];
                
                // Get tile data from the correct generator based on data source
                HexTileData tileData;
                if (dataSource == MinimapDataSource.Planet)
                {
                    // Use current planet generator for backward compatibility
                    var currentPlanet = GameManager.Instance?.GetCurrentPlanetGenerator();
                    if (currentPlanet != null)
                    {
                        tileData = currentPlanet.GetHexTileData(tileIndex);
                    }
                    else
                    {
                        Debug.LogWarning($"[MinimapGenerator] Current planet generator is null for Planet data source!");
                        tileData = new HexTileData { biome = Biome.Ocean };
                    }
                }
                else if (dataSource == MinimapDataSource.Moon)
                {
                    // Use current moon generator for backward compatibility
                    var currentMoon = GameManager.Instance?.GetCurrentMoonGenerator();
                    if (currentMoon != null)
                    {
                        tileData = currentMoon.GetHexTileData(tileIndex);
                    }
                    else
                    {
                        Debug.LogWarning($"[MinimapGenerator] Current moon generator is null for Moon data source!");
                        tileData = new HexTileData { biome = Biome.Ocean };
                    }
                }
                else if (dataSource == MinimapDataSource.PlanetByIndex)
                {
                    // Get tile data from specific planet by index
                    var planetGen = GameManager.Instance?.GetPlanetGenerator(planetIndex);
                    if (planetGen != null)
                    {
                        tileData = planetGen.GetHexTileData(tileIndex);
                        
                        // DEBUG: Log first few tiles to see what we're getting (reduced logging)
                        if (tileIndex < 3 && x == 0 && y == 0)
                        {
                            Debug.Log($"[MinimapGenerator] Planet {planetIndex}, Tile {tileIndex}: Biome = {tileData.biome}");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[MinimapGenerator] Planet generator {planetIndex} is null for PlanetByIndex data source!");
                        tileData = new HexTileData { biome = Biome.Ocean };
                    }
                }
                else
                {
                    // Fallback if no generator available
                    Debug.LogWarning($"[MinimapGenerator] Unknown data source: {dataSource}");
                    tileData = new HexTileData { biome = Biome.Ocean };
                }

                // Create UV for this pixel in equirectangular space
                Vector2 uv = new Vector2(u, v);

                // Use textures with UV sampling (required for BiomeTextures)
                Color c;
                if (colorProvider != null)
                {
                    c = colorProvider.ColorFor(tileData, uv);
                    if (c == Color.magenta)
                    {
                        c = DefaultColorFor(tileData);
                    }
                }
                else
                {
                    c = DefaultColorFor(tileData);
                }
                
                // DEBUG: Log first pixel color to see what we're getting
                if (x == 0 && y == 0)
                {
                    Debug.Log($"[MinimapGenerator] First pixel: Biome={tileData.biome}, Color={c}, ColorProvider={(colorProvider != null ? "present" : "null")}");
                }
                
                pixels[y * masterTextureWidth + x] = c;
            }
        }

        _masterTexture.SetPixels(pixels);
        _masterTexture.Apply();
        _masterTextureReady = true;
        
        Debug.Log($"[MinimapGenerator] Master texture built: {masterTextureWidth}x{masterTextureHeight}");
    }
    
    private void GenerateDisplayTexture()
    {
        if (!_masterTextureReady)
        {
            Debug.LogWarning("[MinimapGenerator] Master texture not ready!");
            return;
        }
        
        // Allocate display texture
        if (minimapTexture == null || minimapTexture.width != width || minimapTexture.height != height)
        {
            minimapTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            minimapTexture.wrapMode = TextureWrapMode.Clamp;
            minimapTexture.filterMode = FilterMode.Bilinear;
        }

        // Calculate zoom center in UV space (0-1)
        Vector3 zoomRootPos = planetRoot ? planetRoot.position : Vector3.zero;
        Vector3 zoomDir = (zoomCenter - zoomRootPos).normalized;
        float centerLat = Mathf.Asin(Mathf.Clamp(zoomDir.y, -1f, 1f));
        float centerLon = Mathf.Atan2(zoomDir.z, zoomDir.x);
        
        // Convert to UV space (0-1)
        float centerU = (centerLon + Mathf.PI) / (2f * Mathf.PI);
        float centerV = (centerLat + Mathf.PI * 0.5f) / Mathf.PI;
        
        // Calculate zoom-adjusted sampling area
        float sampleWidth = 1.0f / zoomLevel;  // Smaller area = more zoomed in
        float sampleHeight = 1.0f / zoomLevel;
        
        // Calculate sampling bounds
        float minU = centerU - sampleWidth * 0.5f;
        float maxU = centerU + sampleWidth * 0.5f;
        float minV = centerV - sampleHeight * 0.5f;
        float maxV = centerV + sampleHeight * 0.5f;
        
        // Fast texture sampling from master texture
        var pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            float v = minV + (y + 0.5f) / height * sampleHeight;
            v = Mathf.Clamp01(v);
            int masterY = Mathf.Clamp(Mathf.FloorToInt(v * masterTextureHeight), 0, masterTextureHeight - 1);
            
            for (int x = 0; x < width; x++)
            {
                float u = minU + (x + 0.5f) / width * sampleWidth;
                u = Mathf.Clamp01(u);
                int masterX = Mathf.Clamp(Mathf.FloorToInt(u * masterTextureWidth), 0, masterTextureWidth - 1);
                
                // Sample from master texture (much faster than regenerating!)
                pixels[y * width + x] = _masterTexture.GetPixel(masterX, masterY);
            }
        }

        minimapTexture.SetPixels(pixels);
        minimapTexture.Apply();
    }

    public void Rebuild()
    {
        // For zoom/pan changes, just regenerate display texture (fast)
        if (_masterTextureReady)
        {
            GenerateDisplayTexture();
        }
        else
        {
            // Full rebuild if master texture not ready
            IsReady = false;
            Build();
        }
    }
    
    /// <summary>
    /// Force a complete rebuild (including master texture) - use when tile data changes
    /// </summary>
    public void ForceFullRebuild()
    {
        _masterTextureReady = false;
        IsReady = false;
        Build();
    }

    public void UpdateTileOnMinimap(int tileIndex)
    {
        if (!IsReady || !_masterTextureReady) return;
        
        // Update the master texture first
        UpdateTileInMasterTexture(tileIndex);
        
        // Then regenerate the display texture from the updated master
        GenerateDisplayTexture();
    }
    
    private void UpdateTileInMasterTexture(int tileIndex)
    {
        // Get world position from the correct generator
        Vector3 worldPos;
        if (dataSource == MinimapDataSource.Planet && GameManager.Instance?.GetCurrentPlanetGenerator() != null)
        {
            worldPos = GameManager.Instance.GetCurrentPlanetGenerator()?.Grid.tileCenters[tileIndex] ?? Vector3.zero;
        }
        else if (dataSource == MinimapDataSource.Moon && GameManager.Instance?.GetCurrentMoonGenerator() != null)
        {
            worldPos = GameManager.Instance.GetCurrentMoonGenerator().Grid.tileCenters[tileIndex];
        }
        else if (dataSource == MinimapDataSource.PlanetByIndex)
        {
            var planetGen = GameManager.Instance?.GetPlanetGenerator(planetIndex);
            if (planetGen?.Grid != null && tileIndex < planetGen.Grid.tileCenters.Length)
            {
                worldPos = planetGen.Grid.tileCenters[tileIndex];
            }
            else
            {
                return; // Can't update without proper data source
            }
        }
        else
        {
            return; // Can't update without proper data source
        }
        
        // Convert to master texture UV
        Vector2 uv = WorldPosToUV(worldPos);
        int cx = Mathf.FloorToInt(uv.x * masterTextureWidth);
        int cy = Mathf.FloorToInt(uv.y * masterTextureHeight);
        int r = Mathf.Max(1, masterTextureWidth / 512); // Scale radius based on master texture size
        
        // Get tile data from the correct generator
        HexTileData tileData;
        if (dataSource == MinimapDataSource.Planet)
        {
            // Use current planet generator for backward compatibility
            var currentPlanet = GameManager.Instance?.GetCurrentPlanetGenerator();
            if (currentPlanet != null)
            {
                tileData = currentPlanet.GetHexTileData(tileIndex);
            }
            else
            {
                return; // Can't update without proper data source
            }
        }
        else if (dataSource == MinimapDataSource.Moon)
        {
            // Use current moon generator for backward compatibility
            var currentMoon = GameManager.Instance?.GetCurrentMoonGenerator();
            if (currentMoon != null)
            {
                tileData = currentMoon.GetHexTileData(tileIndex);
            }
            else
            {
                return; // Can't update without proper data source
            }
        }
        else if (dataSource == MinimapDataSource.PlanetByIndex)
        {
            var planetGen = GameManager.Instance?.GetPlanetGenerator(planetIndex);
            if (planetGen != null)
            {
                tileData = planetGen.GetHexTileData(tileIndex);
            }
            else
            {
                return; // Can't update without proper data source
            }
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

        // Update master texture
        for (int y = cy - r; y <= cy + r; y++)
        {
            if (y < 0 || y >= masterTextureHeight) continue;
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (x < 0 || x >= masterTextureWidth) continue;
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r)
                    _masterTexture.SetPixel(x, y, c);
            }
        }
        _masterTexture.Apply();
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
        // FIXED: Don't call colorProvider here - this is the fallback function!
        // Return different colors based on biome
        return tile.biome switch
        {
            Biome.Ocean => new Color(0.2f, 0.4f, 0.8f, 1f),      // Blue
            Biome.Forest => new Color(0.2f, 0.6f, 0.2f, 1f),     // Green
            Biome.Desert => new Color(0.8f, 0.7f, 0.3f, 1f),     // Sandy
            Biome.Mountain => new Color(0.6f, 0.5f, 0.4f, 1f),   // Brown
            Biome.Plains => new Color(0.4f, 0.7f, 0.3f, 1f),     // Light green
            Biome.Snow => new Color(0.9f, 0.9f, 0.9f, 1f),       // White
            Biome.Tundra => new Color(0.6f, 0.7f, 0.8f, 1f),     // Light blue-gray
            _ => new Color(0.5f, 0.5f, 0.5f, 1f)                 // Gray fallback
        };
    }
}
