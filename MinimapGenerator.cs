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

    // Final minimap texture is declared in the UV atlas section below

    [Header("Performance")]
    [Tooltip("High-resolution master texture for zooming (generated once)")]
    public int masterTextureWidth = 512;  // PERFORMANCE FIX: Reduced from 2048
    [Tooltip("High-resolution master texture for zooming (generated once)")]
    public int masterTextureHeight = 256; // PERFORMANCE FIX: Reduced from 1024
    
    private Texture2D _masterTexture; // High-res version for zooming
    private bool _masterTextureReady = false;

    // --- New persistent UV atlas data ---
    [Header("UV Atlas")]
    [Tooltip("Width of the precomputed UV atlas used for fast tile lookup")] public int widthUV = 512; // Changed from 1024
    [Tooltip("Height of the precomputed UV atlas used for fast tile lookup")] public int heightUV = 256; // Changed from 512
    private Vector2[] tileUVs;                    // Per-tile UV (0..1, equirectangular)
    private int[] uvToTileIndex;                  // For each UV pixel, the nearest tile index
    public Texture2D minimapTexture;              // Final 512x256 minimap texture (kept persistent)
    public bool isBuilt = false;                  // Whether atlas + minimapTexture have been created

    [Header("Data Source")]
    [Tooltip("Which generator to use for tile data")]
    public MinimapDataSource dataSource = MinimapDataSource.Planet;
    
    [Header("Multi-Planet Support")]
    [Tooltip("Planet index for multi-planet system (only used when dataSource is PlanetByIndex)")]
    public int planetIndex = 0;
    
    [Header("Zoom")]
    [Tooltip("Current zoom level - higher values show more detail of a smaller area")]
    public float zoomLevel = 1.0f;
    
    [Tooltip("Direction from planet center for zoom focus (local coordinates)")]
    public Vector3 zoomCenter = Vector3.right;

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
        // Start centered on 0° longitude/latitude in the planet's local frame
        zoomCenter = Vector3.right;
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
            // Expect zoom center as a direction relative to the planet's origin
            zoomCenter = newZoomCenter.Value.normalized;
        }
    }

    public void Build()
    {
        if (isBuilt) { IsReady = true; return; }

        if (_generator == null || planetRoot == null)
        {
            Debug.LogWarning("[MinimapGenerator] Data source not configured. Call ConfigureDataSource() first.");
            return;
        }

        if (!PrepareGridAndTiles()) return;
        BuildTileUVs();
        BuildUVLookup();
        RasterizeMinimap();

        isBuilt = true;
        IsReady = true;
    }

    // Legacy path retained for compatibility (unused by new Build())
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
            Vector3 localPos = planetRoot ? planetRoot.InverseTransformPoint(grid.tileCenters[i]) : grid.tileCenters[i];
            _tileDirs[i] = localPos.normalized;
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

        // Calculate zoom center in UV space (0-1) using local direction
        Vector3 zoomDir = zoomCenter.normalized;
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

    // === New pipeline ===
    private SphericalHexGrid cachedGrid;
    private bool PrepareGridAndTiles()
    {
        cachedGrid = null;
        if (dataSource == MinimapDataSource.Planet)
            cachedGrid = GameManager.Instance?.GetCurrentPlanetGenerator()?.Grid;
        else if (dataSource == MinimapDataSource.Moon)
            cachedGrid = GameManager.Instance?.GetCurrentMoonGenerator()?.Grid;
        else if (dataSource == MinimapDataSource.PlanetByIndex)
            cachedGrid = GameManager.Instance?.GetPlanetGenerator(planetIndex)?.Grid;

        if (cachedGrid == null)
        {
            Debug.LogWarning($"[MinimapGenerator] Grid missing for {dataSource}, planet {planetIndex}");
            return false;
        }

        int tileCount = cachedGrid.tileCenters.Length;
        _tileIndices = new List<int>(tileCount);
        _tileDirs = new Vector3[tileCount];
        tileUVs = new Vector2[tileCount];
        for (int i = 0; i < tileCount; i++)
        {
            _tileIndices.Add(i);
            // IMPORTANT: grid.tileCenters are already in planet-local space
            // Do NOT transform by planetRoot; this caused all tiles to collapse
            Vector3 localPos = cachedGrid.tileCenters[i];
            _tileDirs[i] = localPos.normalized;
        }
        return true;
    }

    private void BuildTileUVs()
    {
        for (int i = 0; i < _tileDirs.Length; i++)
        {
            Vector3 d = _tileDirs[i];
            float u = (Mathf.Atan2(d.z, d.x) + Mathf.PI) / (2f * Mathf.PI);
            float v = 0.5f - (Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) / Mathf.PI); // v=0 at north pole
            tileUVs[i] = new Vector2(u, Mathf.Clamp01(v));
        }
    }

    private void BuildUVLookup()
    {
        uvToTileIndex = new int[widthUV * heightUV];
        // Simple bucket grid
        int bucketsX = 128, bucketsY = 64;
        List<int>[,] buckets = new List<int>[bucketsX, bucketsY];
        for (int bx = 0; bx < bucketsX; bx++)
            for (int by = 0; by < bucketsY; by++)
                buckets[bx, by] = new List<int>(8);

        // Assign tiles to buckets (and nearby buckets)
        for (int i = 0; i < tileUVs.Length; i++)
        {
            Vector2 uv = tileUVs[i];
            int bx = Mathf.Clamp(Mathf.FloorToInt(uv.x * bucketsX), 0, bucketsX - 1);
            int by = Mathf.Clamp(Mathf.FloorToInt(uv.y * bucketsY), 0, bucketsY - 1);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    int px = (bx + dx + bucketsX) % bucketsX;
                    int py = Mathf.Clamp(by + dy, 0, bucketsY - 1);
                    buckets[px, py].Add(i);
                }
            }
        }

        // For each UV pixel, choose nearest tile from its bucket neighborhood
        for (int y = 0; y < heightUV; y++)
        {
            float v = (y + 0.5f) / heightUV;
            int by = Mathf.Clamp(Mathf.FloorToInt(v * bucketsY), 0, bucketsY - 1);
            
            // POLAR FIX: Detect polar regions and use wider search
            bool isPolarRegion = (v < 0.15f || v > 0.85f);
            int searchRadius = isPolarRegion ? 8 : 1; // Wider search for poles (was 3, now 8)
            
            for (int x = 0; x < widthUV; x++)
            {
                float u = (x + 0.5f) / widthUV;
                int bx = Mathf.Clamp(Mathf.FloorToInt(u * bucketsX), 0, bucketsX - 1);

                // PERFORMANCE FIX: Use bucket search for ALL regions (no expensive O(n) search)
                int bestIdx = 0;
                float bestDist2 = float.MaxValue;
                
                // Normal bucket search for non-polar regions (now also applies to polar regions with wider searchRadius)
                for (int dx = -searchRadius; dx <= searchRadius; dx++)
                {
                    for (int dy = -searchRadius; dy <= searchRadius; dy++)
                    {
                        int px = (bx + dx + bucketsX) % bucketsX;
                        int py = Mathf.Clamp(by + dy, 0, bucketsY - 1);
                        var list = buckets[px, py];
                        for (int k = 0; k < list.Count; k++)
                        {
                            int ti = list[k];
                            Vector2 tuv = tileUVs[ti];
                            // Handle wraparound in u
                            float du = Mathf.Abs(u - tuv.x);
                            du = Mathf.Min(du, 1f - du);
                            float dv = (v - tuv.y);
                            float d2 = du * du + dv * dv;
                            if (d2 < bestDist2)
                            {
                                bestDist2 = d2;
                                bestIdx = ti;
                            }
                        }
                    }
                }

                uvToTileIndex[y * widthUV + x] = bestIdx;
                // Also mirror seam for last column
                if (x == 0)
                    uvToTileIndex[y * widthUV + (widthUV - 1)] = bestIdx;
            }
        }
    }

    private void RasterizeMinimap()
    {
        if (minimapTexture == null || minimapTexture.width != width || minimapTexture.height != height)
        {
            minimapTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            minimapTexture.wrapMode = TextureWrapMode.Clamp;
            minimapTexture.filterMode = FilterMode.Bilinear;
        }

        // ZOOM SUPPORT: Calculate zoom center in UV space (0-1) using local direction
        Vector3 zoomDir = zoomCenter.normalized;
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

        Color[] outPixels = new Color[width * height];
        for (int Y = 0; Y < height; Y++)
        {
            // ZOOM: Map Y coordinate to the zoomed sampling area  
            // NOTE: Y=0 is top of texture, Y=height-1 is bottom of texture
            // We want bottom of texture (Y=height-1) to map to minV (south)
            // We want top of texture (Y=0) to map to maxV (north)
            float sampleV = maxV - (Y + 0.5f) / height * sampleHeight;
            sampleV = Mathf.Clamp01(sampleV);
            int iy = Mathf.Clamp(Mathf.FloorToInt(sampleV * heightUV), 0, heightUV - 1);
            
            for (int X = 0; X < width; X++)
            {
                // ZOOM: Map X coordinate to the zoomed sampling area
                float sampleU = minU + (X + 0.5f) / width * sampleWidth;
                sampleU = Mathf.Clamp01(sampleU);
                float u = sampleU;
                float v = sampleV;
                int ix = Mathf.Clamp(Mathf.FloorToInt(u * widthUV), 0, widthUV - 1);
                int tileIndex = uvToTileIndex[iy * widthUV + ix];

                // Fetch tile data from the configured source
                HexTileData tileData;
                if (dataSource == MinimapDataSource.PlanetByIndex)
                {
                    var planetGen = GameManager.Instance?.GetPlanetGenerator(planetIndex);
                    tileData = planetGen != null ? planetGen.GetHexTileData(tileIndex) : null;
                }
                else if (dataSource == MinimapDataSource.Planet)
                {
                    var currentPlanet = GameManager.Instance?.GetCurrentPlanetGenerator();
                    tileData = currentPlanet != null ? currentPlanet.GetHexTileData(tileIndex) : null;
                }
                else
                {
                    var currentMoon = GameManager.Instance?.GetCurrentMoonGenerator();
                    tileData = currentMoon != null ? currentMoon.GetHexTileData(tileIndex) : null;
                }

                Color c = (tileData != null && colorProvider != null) ? colorProvider.ColorFor(tileData, new Vector2(u, v)) : Color.gray;
                outPixels[Y * width + X] = c;
            }
        }
        minimapTexture.SetPixels(outPixels);
        minimapTexture.Apply();
    }

    public int GetTileAtUV(float u, float v)
    {
        // ZOOM SUPPORT: Convert minimap UV to world UV accounting for zoom
        // Calculate zoom center in UV space
        Vector3 zoomDir = zoomCenter.normalized;
        float centerLat = Mathf.Asin(Mathf.Clamp(zoomDir.y, -1f, 1f));
        float centerLon = Mathf.Atan2(zoomDir.z, zoomDir.x);
        float centerU = (centerLon + Mathf.PI) / (2f * Mathf.PI);
        float centerV = (centerLat + Mathf.PI * 0.5f) / Mathf.PI;
        
        // Calculate sampling area
        float sampleWidth = 1.0f / zoomLevel;
        float sampleHeight = 1.0f / zoomLevel;
        float minU = centerU - sampleWidth * 0.5f;
        float minV = centerV - sampleHeight * 0.5f;
        
        // Convert minimap UV to world UV
        float worldU = minU + u * sampleWidth;
        float worldV = minV + v * sampleHeight; // NO FLIP - v already correct
        
        worldU = Mathf.Repeat(worldU, 1f);
        worldV = Mathf.Clamp01(worldV);
        
        int ix = Mathf.Clamp(Mathf.FloorToInt(worldU * widthUV), 0, widthUV - 1);
        int iy = Mathf.Clamp(Mathf.FloorToInt(worldV * heightUV), 0, heightUV - 1);
        
        if (uvToTileIndex == null || uvToTileIndex.Length == 0) 
        {
            Debug.LogError("[MinimapGenerator] uvToTileIndex is null or empty!");
            return 0;
        }
        
        return uvToTileIndex[iy * widthUV + ix];
    }

    public Texture2D GetMinimapTexture() => minimapTexture;

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
        Vector3 localPos = planetRoot ? planetRoot.InverseTransformPoint(worldPos) : worldPos;
        Vector3 dir = localPos.normalized;
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
