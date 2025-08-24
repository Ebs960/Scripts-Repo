using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Runtime minimap for multi-planet support.
/// - Pre-generates all planet minimaps during game initialization.
/// - Shows loading progress via LoadingPanelController.
/// - Switch between planets instantly via dropdown.
/// - Scroll wheel or buttons to zoom; click to move the orbital camera to that spot.
/// - TextMeshPro support for dropdown and zoom level display.
/// </summary>
public class MinimapUI : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, IScrollHandler
{
    [Header("UI References")]
    [Tooltip("RawImage used to display the generated minimap texture")]
    public RawImage minimapImage;
    [Tooltip("TMP_Dropdown used to select the active planet (supports TextMeshPro)")]
    public TMP_Dropdown planetDropdown;
    [Tooltip("Optional container for the minimap – used for scaling")]
    public RectTransform minimapContainer;
    [Tooltip("Button to zoom in on the minimap")]
    public Button zoomInButton;
    [Tooltip("Button to zoom out on the minimap")]
    public Button zoomOutButton;
    [Tooltip("Button to switch the view to the current planet's moon (assign in inspector)")]
    public Button moonButton;
    [Tooltip("Button to switch back to the main planet (assign in inspector)")]
    public Button mainPlanetButton;
    [Tooltip("Optional text display showing current zoom level")]
    public TextMeshProUGUI zoomLevelText;
    [Tooltip("Position indicator (shows where camera is looking on minimap)")]
    public RectTransform positionIndicator;

    [Header("Minimap Settings")]
    [SerializeField] private Vector2Int minimapResolution = new Vector2Int(512, 256); // Fixed resolution for better performance
    [SerializeField] private MinimapColorProvider colorProvider;
    [SerializeField] private float maxZoom = 4f;
    [SerializeField] private float minZoom = 0.5f;
    [SerializeField] private float zoomSpeed = 1f;
    [SerializeField] private float buttonZoomStep = 0.5f;

    [Header("Switching Settings")]
    [Tooltip("Index of the main planet to switch back to when pressing the main planet button (0 = Earth by default)")]
    [SerializeField] private int mainPlanetIndex = 0;

    [Header("Pre-generation Settings")]
    [SerializeField] private bool preGenerateAllMinimaps = false;
    [SerializeField] private int maxPixelsPerFrameBatch = 50000; // Process pixels in batches for better performance

    // Private fields
    // Planet and Moon minimap caches
    private readonly Dictionary<int, Texture2D> _minimapTextures = new();
    private readonly Dictionary<int, Texture2D> _moonMinimapTextures = new();
    // Fast path: per-body (planet or moon) LUT cache mapping pixel -> tile index
    // Key: P{planetIndex}_M{0|1}_W{w}_H{h}
    private static readonly Dictionary<string, int[]> _bodyIndexLUT = new();
    private float _currentZoom = 1f;
    private Vector2 _panOffset = Vector2.zero; // For panning around the zoomed minimap
    private bool _isDragging = false;
    private Vector2 _lastDragPosition;
    private GameManager _gameManager;
    private bool _minimapsPreGenerated = false;
    private LoadingPanelController _loadingPanel;
    private bool _lastIsOnMoon = false; // track camera target to auto-switch minimap
    
    // UI mirroring cache
    [SerializeField] private Camera uiCamera; // leave null for Screen Space - Overlay
    private bool _isHorizontallyMirrored;
    private bool _isVerticallyMirrored;

    // Public property for GameManager to check
    public bool MinimapsPreGenerated => _minimapsPreGenerated;
    public bool PreGenerateAll => preGenerateAllMinimaps;

    // Build or fetch a LUT mapping each minimap pixel to a tile index for a specific body
    private int[] EnsureIndexLUTForBody(int planetIndex, bool isMoon, SphericalHexGrid grid, int width, int height)
    {
        if (grid == null) return null;
        string key = $"P{planetIndex}_M{(isMoon ? 1 : 0)}_W{width}_H{height}";
        if (_bodyIndexLUT.TryGetValue(key, out var cached)) return cached;

        var lut = new int[width * height];

        // Precompute trig
        float[] sinLatArr = new float[height];
        float[] cosLatArr = new float[height];
        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            float latRad = Mathf.PI * (0.5f - v);
            sinLatArr[y] = Mathf.Sin(latRad);
            cosLatArr[y] = Mathf.Cos(latRad);
        }
        float[] sinLonArr = new float[width];
        float[] cosLonArr = new float[width];
        for (int x = 0; x < width; x++)
        {
            float u = (x + 0.5f) / width;
            float lonRad = 2f * Mathf.PI * (u - 0.5f);
            sinLonArr[x] = Mathf.Sin(lonRad);
            cosLonArr[x] = Mathf.Cos(lonRad);
        }

        for (int y = 0; y < height; y++)
        {
            float sinLat = sinLatArr[y];
            float cosLat = cosLatArr[y];
            int yBase = y * width;
            for (int x = 0; x < width; x++)
            {
                float sinLon = sinLonArr[x];
                float cosLon = cosLonArr[x];
                Vector3 dir = new Vector3(sinLon * cosLat, sinLat, cosLon * cosLat);
                int tileIndex = grid.GetTileAtPosition(dir);
                lut[yBase + x] = tileIndex;
            }
        }

        _bodyIndexLUT[key] = lut;
#if UNITY_EDITOR
        Debug.Log($"[MinimapUI] Built index LUT {key} (size={width}x{height})");
#endif
        return lut;
    }

    // Flip a Color32 pixel buffer vertically in-place (row swap) to convert from
    // bottom-left origin (Texture2D) to a top-left display orientation.
    private static void FlipPixelsVertically(Color32[] pixels, int width, int height)
    {
        if (pixels == null || pixels.Length != width * height) return;
        int half = height / 2;
        var rowBuffer = new Color32[width];
        for (int y = 0; y < half; y++)
        {
            int topRowStart = (height - 1 - y) * width;
            int bottomRowStart = y * width;

            // Swap rows using a small buffer
            System.Array.Copy(pixels, bottomRowStart, rowBuffer, 0, width);
            System.Array.Copy(pixels, topRowStart, pixels, bottomRowStart, width);
            System.Array.Copy(rowBuffer, 0, pixels, topRowStart, width);
        }
    }

    // Flip a Color32 pixel buffer horizontally in-place (column swap) to fix horizontal orientation
    private static void FlipPixelsHorizontally(Color32[] pixels, int width, int height)
    {
        if (pixels == null || pixels.Length != width * height) return;
        int half = width / 2;
        for (int y = 0; y < height; y++)
        {
            int rowStart = y * width;
            for (int x = 0; x < half; x++)
            {
                int leftIndex = rowStart + x;
                int rightIndex = rowStart + (width - 1 - x);
                
                // Swap pixels
                Color32 temp = pixels[leftIndex];
                pixels[leftIndex] = pixels[rightIndex];
                pixels[rightIndex] = temp;
            }
        }
    }

    void Awake()
    {
        _gameManager = GameManager.Instance;
        _loadingPanel = FindAnyObjectByType<LoadingPanelController>();

        if (minimapImage == null)
            Debug.LogError("[MinimapUI] No RawImage assigned.");
        if (planetDropdown != null)
            planetDropdown.onValueChanged.AddListener(OnPlanetDropdownChanged);
        
        // Set up zoom button listeners
        if (zoomInButton != null)
            zoomInButton.onClick.AddListener(ZoomIn);
        if (zoomOutButton != null)
            zoomOutButton.onClick.AddListener(ZoomOut);

        // Wire Moon button to switch to the current planet's moon
        if (moonButton != null)
        {
            moonButton.onClick.AddListener(OnMoonButtonClicked);
        }
        else
        {
            Debug.LogWarning("[MinimapUI] Moon button not assigned in inspector; moon switching will be unavailable.");
        }
        // Wire Main planet button to switch back to configured main planet
        if (mainPlanetButton != null)
        {
            mainPlanetButton.onClick.AddListener(OnMainPlanetButtonClicked);
        }
        else
        {
            Debug.LogWarning("[MinimapUI] Main planet button not assigned in inspector; main planet switching will be unavailable.");
        }
    }

    void Update()
    {
        // Update position indicator every frame for smooth tracking
        UpdatePositionIndicator();

        // Auto-switch between planet and moon minimap when camera target changes
        var camMgr = FindAnyObjectByType<PlanetaryCameraManager>();
        bool isOnMoon = camMgr != null && camMgr.IsOnMoon;
        if (isOnMoon != _lastIsOnMoon)
        {
            _lastIsOnMoon = isOnMoon;
            ShowMinimapForPlanet(_gameManager != null ? _gameManager.currentPlanetIndex : 0);
        }
    }

    private void OnEnable()
    {
        RefreshMirrorFlags();

        // Subscribe to game completion event instead of individual planet events
        if (_gameManager != null)
        {
            _gameManager.OnGameStarted += HandleGameStarted;
        }
    }

    void Start()
    {
        // Remove immediate pre-generation - wait for game completion instead
        BuildPlanetDropdown();
        ShowMinimapForPlanet(_gameManager != null ? _gameManager.currentPlanetIndex : 0);
    }

    // Keep flags fresh if layout / anchors / scaling changes
    protected void OnRectTransformDimensionsChange()
    {
        if (isActiveAndEnabled) RefreshMirrorFlags();
    }

    /// <summary>
    /// Trigger minimap pre-generation (called by GameManager when planets are ready)
    /// </summary>
    public void StartMinimapGeneration()
    {
        StartCoroutine(PreGenerateAllMinimaps());
    }

    /// <summary>
    /// Pre-generate all minimaps with performance optimizations
    /// </summary>
    public IEnumerator PreGenerateAllMinimaps()
    {
        if (_minimapsPreGenerated)
        {
            Debug.Log("[MinimapUI] Minimaps already pre-generated, skipping...");
            yield break;
        }

        Debug.Log("[MinimapUI] Starting optimized minimap pre-generation...");
        _minimapsPreGenerated = false;

        // Clear existing textures
        ClearMinimapCache();

        Debug.Log($"[MinimapUI] Using resolution: {minimapResolution.x}x{minimapResolution.y}");

        int totalPlanets;
        if (_gameManager.enableMultiPlanetSystem)
        {
            var planetData = _gameManager.GetPlanetData();
            totalPlanets = planetData?.Count ?? 0;
            
            if (totalPlanets == 0)
            {
                int generatorCount = 0;
                for (int i = 0; i < 20; i++)
                {
                    if (_gameManager.GetPlanetGenerator(i) != null)
                    {
                        generatorCount = i + 1;
                    }
                }
                totalPlanets = generatorCount;
            }
        }
        else
        {
            totalPlanets = _gameManager.planetGenerator != null ? 1 : 0;
        }

        if (totalPlanets == 0)
        {
            Debug.LogWarning("[MinimapUI] No planets found for minimap generation");
            _minimapsPreGenerated = false;
            yield break;
        }

        Debug.Log($"[MinimapUI] Generating {totalPlanets} optimized minimaps...");

        // Generate minimaps with smaller batches and more frequent yields
        for (int planetIndex = 0; planetIndex < totalPlanets; planetIndex++)
        {
            string planetName = GetPlanetName(planetIndex);
            Debug.Log($"[MinimapUI] Generating minimap for {planetName} (index {planetIndex})...");

            // Generate planet minimap with optimizations
            yield return StartCoroutine(GenerateMinimapTextureOptimized(planetIndex, minimapResolution));

            // Generate moon minimap if exists
            var moonGen = _gameManager.GetMoonGenerator(planetIndex);
            if (moonGen != null && moonGen.Grid != null && moonGen.Grid.TileCount > 0)
            {
                yield return StartCoroutine(GenerateMoonMinimapTextureOptimized(planetIndex, minimapResolution));
            }

            // Update loading progress more frequently
            if (_loadingPanel != null)
            {
                float progress = (float)(planetIndex + 1) / totalPlanets;
                _loadingPanel.SetProgress(progress);
                _loadingPanel.SetStatus($"Generated minimap for {planetName}...{(moonGen != null ? " (and moon)" : string.Empty)}");
            }

            // Yield every planet to prevent frame drops
            yield return null;
        }
        
        Debug.Log("[MinimapUI] Optimized minimap pre-generation complete!");
        _minimapsPreGenerated = true;

        BuildPlanetDropdown();
        if (totalPlanets > 0)
        {
            ShowMinimapForPlanet(0);
        }
    }

    private string GetPlanetName(int planetIndex)
    {
        if (_gameManager.enableMultiPlanetSystem)
        {
            var planetData = _gameManager.GetPlanetData();
            if (planetData != null && planetData.TryGetValue(planetIndex, out var pd))
            {
                return pd.planetName;
            }
            else
            {
                var planetGen = _gameManager.GetPlanetGenerator(planetIndex);
                if (planetGen != null)
                {
                    return planetGen.name.Replace("_Generator", "").Replace("Planet_", "");
                }
            }
        }
        return "Planet";
    }

    /// <summary>
    /// Optimized minimap generation with batching
    /// </summary>
    private IEnumerator GenerateMinimapTextureOptimized(int planetIndex, Vector2Int resolution)
    {
        var planetGen = _gameManager.enableMultiPlanetSystem
            ? _gameManager.GetPlanetGenerator(planetIndex)
            : _gameManager.planetGenerator;

        if (planetGen == null || planetGen.Grid == null)
        {
            Debug.LogWarning($"[MinimapUI] Planet generator or grid null for planet {planetIndex}");
            yield break;
        }

        int width = resolution.x;
        int height = resolution.y;
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        var grid = planetGen.Grid;
        var lut = EnsureIndexLUTForBody(planetIndex, false, grid, width, height);
        if (lut == null)
        {
            Debug.LogWarning($"[MinimapUI] Failed to build LUT for planet {planetIndex}");
            yield break;
        }

        Debug.Log($"[MinimapUI] Pre-caching tile data for planet {planetIndex}...");
        var tileDataCache = new Dictionary<int, HexTileData>();
        for (int i = 0; i < grid.TileCount; i++)
        {
            var tileData = planetGen.GetHexTileData(i);
            if (tileData == null && TileDataHelper.Instance != null)
            {
                var (helperTileData, isMoon) = TileDataHelper.Instance.GetTileDataFromPlanet(i, planetIndex);
                if (!isMoon && helperTileData != null) tileData = helperTileData;
            }
            if (tileData == null && planetGen.data != null && planetGen.data.ContainsKey(i))
                tileData = planetGen.data[i];
            if (tileData != null) tileDataCache[i] = tileData;
        }
        Debug.Log($"[MinimapUI] Cached {tileDataCache.Count} tiles for planet {planetIndex}");

        var pixels = new Color32[width * height];
        int totalPixels = width * height;
        int batchSize = Mathf.Min(maxPixelsPerFrameBatch, Mathf.Max(1, totalPixels / 20));

        for (int startIdx = 0; startIdx < totalPixels; startIdx += batchSize)
        {
            int endIdx = Mathf.Min(startIdx + batchSize, totalPixels);
            for (int pixelIdx = startIdx; pixelIdx < endIdx; pixelIdx++)
            {
                int y = pixelIdx / width;
                int x = pixelIdx % width;
                int tileIndex = lut[pixelIdx];
                Color color = Color.gray;
                if (tileIndex >= 0 && tileDataCache.TryGetValue(tileIndex, out var tileData))
                {
                    if (colorProvider != null)
                    {
                        float u = (x + 0.5f) / width;
                        float v = (y + 0.5f) / height;
                        color = colorProvider.ColorFor(tileData, new Vector2(u, v));
                    }
                    else
                    {
                        color = GetDefaultBiomeColour(tileData.biome);
                    }
                }
                pixels[pixelIdx] = color;
            }
            yield return null;
        }

        FlipPixelsVertically(pixels, width, height);
        if (_isHorizontallyMirrored) FlipPixelsHorizontally(pixels, width, height);
        tex.SetPixels32(pixels);
        tex.Apply(false);
        _minimapTextures[planetIndex] = tex;
        Debug.Log($"[MinimapUI] Generated optimized minimap for planet {planetIndex} ({width}x{height})");
    }

    /// <summary>
    /// Optimized moon minimap generation
    /// </summary>
    private IEnumerator GenerateMoonMinimapTextureOptimized(int planetIndex, Vector2Int resolution)
    {
        var moonGen = _gameManager.GetMoonGenerator(planetIndex);
        if (moonGen == null || moonGen.Grid == null) 
        {
            Debug.LogWarning($"[MinimapUI] Moon generator or grid null for planet {planetIndex}");
            yield break;
        }

        int width = resolution.x;
        int height = resolution.y;
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        var grid = moonGen.Grid;
        var lut = EnsureIndexLUTForBody(planetIndex, true, grid, width, height);
        if (lut == null) 
        {
            Debug.LogWarning($"[MinimapUI] Failed to build LUT for moon {planetIndex}");
            yield break;
        }

        Debug.Log($"[MinimapUI] Pre-caching moon tile data for planet {planetIndex}...");
        // Pre-cache moon tile data
        var tileDataCache = new Dictionary<int, HexTileData>();
        for (int i = 0; i < grid.TileCount; i++)
        {
            var tileData = moonGen.GetHexTileData(i);
            if (tileData.biome != Biome.Ocean) // Valid tile
            {
                tileDataCache[i] = tileData;
            }
        }
        Debug.Log($"[MinimapUI] Cached {tileDataCache.Count} moon tiles for planet {planetIndex}");

        var pixels = new Color32[width * height];
        int totalPixels = width * height;
        int batchSize = Mathf.Min(maxPixelsPerFrameBatch, totalPixels / 10); // Fewer yields for moon
        
        for (int startIdx = 0; startIdx < totalPixels; startIdx += batchSize)
        {
            int endIdx = Mathf.Min(startIdx + batchSize, totalPixels);
            
            for (int pixelIdx = startIdx; pixelIdx < endIdx; pixelIdx++)
            {
                int tileIndex = lut[pixelIdx];
                Color color = Color.gray;
                
                if (tileIndex >= 0 && tileDataCache.TryGetValue(tileIndex, out var tileData))
                {
                    if (colorProvider != null)
                    {
                        int y = pixelIdx / width;
                        int x = pixelIdx % width;
                        float u = (x + 0.5f) / width;
                        float v = (y + 0.5f) / height;
                        color = colorProvider.ColorFor(tileData, new Vector2(u, v));
                    }
                    else
                    {
                        color = GetDefaultBiomeColour(tileData.biome);
                    }
                }
                
                pixels[pixelIdx] = color;
            }
            
            if (startIdx % (batchSize * 3) == 0) yield return null; // Less frequent yields for moon
        }

        FlipPixelsVertically(pixels, width, height);
        if (_isHorizontallyMirrored) FlipPixelsHorizontally(pixels, width, height);
        
        tex.SetPixels32(pixels);
        tex.Apply(false);
        
        _moonMinimapTextures[planetIndex] = tex;
        Debug.Log($"[MinimapUI] Generated optimized moon minimap for planet {planetIndex} ({width}x{height})");
    }

    private IEnumerator GenerateMoonMinimapTextureCoroutine(int planetIndex)
    {
        if (_gameManager == null)
        {
            Debug.LogError("[MinimapUI] No GameManager instance.");
            yield break;
        }

        var moonGen = _gameManager.GetMoonGenerator(planetIndex);
        if (moonGen == null)
        {
            Debug.LogWarning($"[MinimapUI] Moon generator not found for planet {planetIndex}");
            yield break;
        }

        var grid = moonGen.Grid;
        if (grid == null)
        {
            Debug.LogWarning("[MinimapUI] Moon Grid missing.");
            yield break;
        }

        int width = minimapResolution.x;
        int height = minimapResolution.y;
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        // Build/fetch per-body LUT
        var lut = EnsureIndexLUTForBody(planetIndex, true, grid, width, height);
        if (lut == null) yield break;

        var tileDataCache = new Dictionary<int, HexTileData>();
        var tileOffsetCache = new Dictionary<int, Vector2>();
        var pixels = new Color32[width * height];
        // Precompute UVs for sampling
        float[] vArr = new float[height];
        for (int y = 0; y < height; y++) vArr[y] = (y + 0.5f) / height;
        float[] uArr = new float[width];
        for (int x = 0; x < width; x++) uArr[x] = (x + 0.5f) / width;

        int processed = 0;
        for (int y = 0; y < height; y++)
        {
            float v = vArr[y];

            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                int tileIndex = lut[idx];

                Color color;
                if (tileIndex < 0)
                {
                    color = Color.magenta;
                }
                else
                {
                    if (!tileDataCache.TryGetValue(tileIndex, out var tileData) || tileData == null)
                    {
                        tileData = moonGen.GetHexTileData(tileIndex);
                        tileDataCache[tileIndex] = tileData; // may be null; avoids repeated lookups
                    }

                    if (tileData == null)
                    {
                        color = new Color(0.35f, 0.35f, 0.35f);
                    }
                    else
                    {
                        if (!tileOffsetCache.TryGetValue(tileIndex, out var offset))
                        {
                            int hash = tileIndex * 9781 + 7;
                            float ox = ((hash >> 8) & 0xFF) / 255f;
                            float oy = (hash & 0xFF) / 255f;
                            offset = new Vector2(ox, oy);
                            tileOffsetCache[tileIndex] = offset;
                        }
                        float sampleU = Mathf.Repeat(uArr[x] + offset.x, 1f);
                        float sampleV = Mathf.Repeat(v + offset.y, 1f);
                        color = (colorProvider != null) ? colorProvider.ColorFor(tileData, new Vector2(sampleU, sampleV)) : GetDefaultBiomeColour(tileData.biome);
                    }
                }

                // Write directly with final orientation (flip X & Y inline)
                int dstX = width - 1 - x;
                int dstY = height - 1 - y;
                pixels[dstY * width + dstX] = color;
                processed++;
                if (processed % maxPixelsPerFrameBatch == 0)
                    yield return null;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        _moonMinimapTextures[planetIndex] = tex;

        Debug.Log($"[MinimapUI] Generated MOON minimap for planet {planetIndex} ({width}x{height})");
    }

    private IEnumerator GenerateMinimapTextureCoroutine(int planetIndex)
    {
        if (_gameManager == null)
        {
            Debug.LogError("[MinimapUI] No GameManager instance.");
            yield break;
        }

        var planetGen = _gameManager.enableMultiPlanetSystem
            ? _gameManager.GetPlanetGenerator(planetIndex)
            : _gameManager.planetGenerator;

        if (planetGen == null)
        {
            Debug.LogWarning($"[MinimapUI] Planet generator not found for index {planetIndex}");
            yield break;
        }

        var grid = planetGen.Grid;
        if (grid == null)
        {
            Debug.LogWarning("[MinimapUI] Planet Grid missing.");
            yield break;
        }

        int width = minimapResolution.x;
        int height = minimapResolution.y;
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        // Build/fetch per-body LUT (planet)
        var lut = EnsureIndexLUTForBody(planetIndex, false, grid, width, height);
        if (lut == null)
        {
            Debug.LogWarning("[MinimapUI] LUT build failed.");
            yield break;
        }

        // Cache tile data and per-tile sample offsets (no color pre-cache)
        var tileDataCache = new Dictionary<int, HexTileData>();
        var tileOffsetCache = new Dictionary<int, Vector2>();
        var pixels = new Color32[width * height];

    // Precompute sampling UV arrays
    float[] vArr = new float[height];
    for (int y = 0; y < height; y++) vArr[y] = (y + 0.5f) / height;
    float[] uArr = new float[width];
    for (int x = 0; x < width; x++) uArr[x] = (x + 0.5f) / width;

    int processed = 0;
        for (int y = 0; y < height; y++)
        {
        float v = vArr[y];

            for (int x = 0; x < width; x++)
            {
        int tileIndex = lut[y * width + x];

                Color color;
                if (tileIndex < 0)
                {
                    color = Color.magenta;
                }
                else
                {
                    if (!tileDataCache.TryGetValue(tileIndex, out var tileData) || tileData == null)
                    {
                        tileData = planetGen.GetHexTileData(tileIndex);
                        if (tileData == null && TileDataHelper.Instance != null)
                        {
                            var (helperTileData, isMoon) = TileDataHelper.Instance.GetTileDataFromPlanet(tileIndex, planetIndex);
                            if (!isMoon && helperTileData != null) tileData = helperTileData;
                        }
                        if (tileData == null && planetGen.data != null && planetGen.data.ContainsKey(tileIndex))
                            tileData = planetGen.data[tileIndex];

                        tileDataCache[tileIndex] = tileData; // may be null; avoids repeated lookups
                    }

                    if (tileData == null)
                    {
                        color = new Color(0.35f, 0.35f, 0.35f);
                    }
                    else
                    {
                        if (!tileOffsetCache.TryGetValue(tileIndex, out var offset))
                        {
                            int hash = tileIndex * 9781 + 7;
                            float ox = ((hash >> 8) & 0xFF) / 255f;
                            float oy = (hash & 0xFF) / 255f;
                            offset = new Vector2(ox, oy);
                            tileOffsetCache[tileIndex] = offset;
                        }
            float sampleU = Mathf.Repeat(uArr[x] + offset.x, 1f);
            float sampleV = Mathf.Repeat(v + offset.y, 1f);
                        color = (colorProvider != null) ? colorProvider.ColorFor(tileData, new Vector2(sampleU, sampleV)) : GetDefaultBiomeColour(tileData.biome);
                    }
                }

        int dstX = width - 1 - x;
        int dstY = height - 1 - y;
        pixels[dstY * width + dstX] = color;
                processed++;
                if (processed % maxPixelsPerFrameBatch == 0)
                    yield return null;
            }
        }
    
    tex.SetPixels32(pixels);
        tex.Apply();
        _minimapTextures[planetIndex] = tex;
        
        Debug.Log($"[MinimapUI] Generated minimap for planet {planetIndex} ({width}x{height})");
    }

    /// <summary>
    /// Clear the minimap cache (useful for debugging and forcing regeneration)
    /// </summary>
    [ContextMenu("Clear Minimap Cache")]
    public void ClearMinimapCache()
    {
        foreach (var tex in _minimapTextures.Values)
        {
            if (tex != null) DestroyImmediate(tex);
        }
        _minimapTextures.Clear();
        foreach (var tex in _moonMinimapTextures.Values)
        {
            if (tex != null) DestroyImmediate(tex);
        }
        _moonMinimapTextures.Clear();
        _minimapsPreGenerated = false;
        Debug.Log("[MinimapUI] Minimap cache cleared. Restart generation to create new textures.");
    }

    private void OnDisable()
    {
        if (_gameManager != null)
        {
            _gameManager.OnGameStarted -= HandleGameStarted;
        }
    }

    // New method to handle complete game setup
    private void HandleGameStarted()
    {
        Debug.Log("[MinimapUI] Game started - beginning minimap generation for all planets");
        StartCoroutine(PreGenerateAllMinimaps());
    }

    // Remove the old planet-specific event handlers - no longer needed
    // Minimaps will be generated when OnGameStarted fires

    /// <summary>
    /// Detect if the RawImage is mirrored by the UI transform stack.
    /// </summary>
    private void RefreshMirrorFlags()
    {
        if (minimapImage == null) return;

        // Auto-pick UI camera when not set
        if (uiCamera == null)
        {
            var canvas = minimapImage.canvas;
            if (canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                uiCamera = canvas.worldCamera;
        }

        var rt = minimapImage.rectTransform;

        // Get four world corners (bl, tl, tr, br)
        Vector3[] wc = new Vector3[4];
        rt.GetWorldCorners(wc);

        // Project to screen space (camera can be null for Overlay)
        Vector2 bl = RectTransformUtility.WorldToScreenPoint(uiCamera, wc[0]);
        Vector2 tl = RectTransformUtility.WorldToScreenPoint(uiCamera, wc[1]);
        Vector2 tr = RectTransformUtility.WorldToScreenPoint(uiCamera, wc[2]);
        Vector2 br = RectTransformUtility.WorldToScreenPoint(uiCamera, wc[3]);

        // If the right edge is to the left of the left edge, it's mirrored on X
        _isHorizontallyMirrored = (br.x < bl.x) || (tr.x < tl.x);
        // If the top edge is below the bottom edge, it's mirrored on Y
        _isVerticallyMirrored   = (tl.y < bl.y) || (tr.y < br.y);

#if UNITY_EDITOR
        Debug.Log($"[MinimapUI] Mirror flags -> H:{_isHorizontallyMirrored}  V:{_isVerticallyMirrored}");
#endif
    }

    private void BuildPlanetDropdown()
    {
        if (planetDropdown == null || _gameManager == null) return;

        planetDropdown.ClearOptions();
        var options = new List<TMP_Dropdown.OptionData>();

        // Discover actual planet indices available
        var pd = _gameManager.GetPlanetData();
        var indices = new List<int>();

        if (_gameManager.enableMultiPlanetSystem)
        {
            // Prefer planet data count if available
            if (pd != null && pd.Count > 0)
            {
                foreach (var kv in pd)
                {
                    // Only include indices that have a generator or at least data
                    if (_gameManager.GetPlanetGenerator(kv.Key) != null)
                        indices.Add(kv.Key);
                }
            }

            // Fallback: probe generators up to maxPlanets
            if (indices.Count == 0)
            {
                for (int i = 0; i < _gameManager.maxPlanets; i++)
                {
                    if (_gameManager.GetPlanetGenerator(i) != null)
                        indices.Add(i);
                }
            }
        }
        else
        {
            if (_gameManager.planetGenerator != null)
                indices.Add(0);
        }

        foreach (var i in indices)
        {
            string label = (pd != null && pd.TryGetValue(i, out var d) && !string.IsNullOrEmpty(d.planetName))
                ? d.planetName
                : $"Planet {i + 1}";
            options.Add(new TMP_Dropdown.OptionData(label));
        }

        // If still nothing, add at least one default entry so UI isn't empty
        if (options.Count == 0)
        {
            options.Add(new TMP_Dropdown.OptionData("Planet 1"));
        }

        planetDropdown.AddOptions(options);
        planetDropdown.value = Mathf.Clamp(_gameManager.currentPlanetIndex, 0, options.Count - 1);
        planetDropdown.RefreshShownValue();
    }

    private void OnPlanetDropdownChanged(int planetIndex)
    {
        if (_gameManager != null && _gameManager.enableMultiPlanetSystem)
            _gameManager.SetCurrentPlanet(planetIndex);

        ShowMinimapForPlanet(planetIndex);
    }

    private void ShowMinimapForPlanet(int planetIndex)
    {
        var camMgr = FindAnyObjectByType<PlanetaryCameraManager>();
        bool isOnMoon = camMgr != null && camMgr.IsOnMoon;

        Debug.Log($"[MinimapUI] ShowMinimapForPlanet called for planet {planetIndex}, isOnMoon: {isOnMoon}");

        if (_minimapsPreGenerated)
        {
            // Use pre-generated texture when present; otherwise, generate on-demand
            if (!isOnMoon)
            {
                if (_minimapTextures.TryGetValue(planetIndex, out var tex) && tex != null)
                {
                    if (minimapImage != null) minimapImage.texture = tex;
                    SetZoom(1f);
                    Debug.Log($"[MinimapUI] Showing pre-generated minimap for planet {planetIndex}");
                }
                else
                {
                    Debug.LogWarning($"[MinimapUI] No pre-generated minimap found for planet {planetIndex} — generating on-demand.");
                    var generated = GenerateMinimapTexture(planetIndex);
                    if (generated != null)
                    {
                        _minimapTextures[planetIndex] = generated;
                        if (minimapImage != null) minimapImage.texture = generated;
                        SetZoom(1f);
                    }
                }
            }
            else
            {
                if (_moonMinimapTextures.TryGetValue(planetIndex, out var mtex) && mtex != null)
                {
                    if (minimapImage != null) minimapImage.texture = mtex;
                    SetZoom(1f);
                    Debug.Log($"[MinimapUI] Showing pre-generated MOON minimap for planet {planetIndex}");
                }
                else
                {
                    Debug.LogWarning($"[MinimapUI] No pre-generated MOON minimap found for planet {planetIndex} — generating on-demand.");
                    var generated = GenerateMoonMinimapTexture(planetIndex);
                    if (generated != null)
                    {
                        _moonMinimapTextures[planetIndex] = generated;
                        if (minimapImage != null) minimapImage.texture = generated;
                        SetZoom(1f);
                    }
                }
            }
        }
        else
        {
            // Fallback to on-demand generation
            if (!isOnMoon)
            {
                if (!_minimapTextures.TryGetValue(planetIndex, out var tex) || tex == null)
                {
                    tex = GenerateMinimapTexture(planetIndex);
                    _minimapTextures[planetIndex] = tex;
                }

                if (minimapImage != null) minimapImage.texture = tex;
                SetZoom(1f);
            }
            else
            {
                if (!_moonMinimapTextures.TryGetValue(planetIndex, out var mtex) || mtex == null)
                {
                    mtex = GenerateMoonMinimapTexture(planetIndex);
                    _moonMinimapTextures[planetIndex] = mtex;
                }

                if (minimapImage != null) minimapImage.texture = mtex;
                SetZoom(1f);
            }
        }
    }

    private Texture2D GenerateMoonMinimapTexture(int planetIndex)
    {
        if (_gameManager == null)
        {
            Debug.LogError("[MinimapUI] No GameManager instance.");
            return null;
        }

        var moonGen = _gameManager.GetMoonGenerator(planetIndex);
        if (moonGen == null)
        {
            Debug.LogWarning($"[MinimapUI] Moon generator not found for planet {planetIndex}");
            return null;
        }

        var grid = moonGen.Grid;
        if (grid == null)
        {
            Debug.LogWarning("[MinimapUI] Moon Grid missing.");
            return null;
        }

        int width = minimapResolution.x;
        int height = minimapResolution.y;
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        var lut = EnsureIndexLUTForBody(planetIndex, true, grid, width, height);
        if (lut == null) return null;

        var tileDataCache = new Dictionary<int, HexTileData>();
        var tileOffsetCache = new Dictionary<int, Vector2>();
        var pixels = new Color32[width * height];
        float[] vArr = new float[height]; for (int y = 0; y < height; y++) vArr[y] = (y + 0.5f) / height;
        float[] uArr = new float[width]; for (int x = 0; x < width; x++) uArr[x] = (x + 0.5f) / width;

        for (int y = 0; y < height; y++)
        {
            float v = vArr[y]; int yBase = y * width;
            for (int x = 0; x < width; x++)
            {
                int tileIndex = lut[yBase + x];
                Color color;
                if (tileIndex < 0)
                {
                    color = Color.magenta;
                }
                else
                {
                    if (!tileDataCache.TryGetValue(tileIndex, out var tileData) || tileData == null)
                    {
                        tileData = moonGen.GetHexTileData(tileIndex);
                        tileDataCache[tileIndex] = tileData;
                    }
                    if (tileData == null)
                    {
                        color = new Color(0.35f, 0.35f, 0.35f);
                    }
                    else
                    {
                        if (!tileOffsetCache.TryGetValue(tileIndex, out var offset))
                        {
                            int hash = tileIndex * 9781 + 7;
                            float ox = ((hash >> 8) & 0xFF) / 255f;
                            float oy = (hash & 0xFF) / 255f;
                            offset = new Vector2(ox, oy);
                            tileOffsetCache[tileIndex] = offset;
                        }
                        float sampleU = Mathf.Repeat(uArr[x] + offset.x, 1f);
                        float sampleV = Mathf.Repeat(v + offset.y, 1f);
                        color = (colorProvider != null) ? colorProvider.ColorFor(tileData, new Vector2(sampleU, sampleV)) : GetDefaultBiomeColour(tileData.biome);
                    }
                }
                int dstX = width - 1 - x; int dstY = height - 1 - y;
                pixels[dstY * width + dstX] = color;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    private Texture2D GenerateMinimapTexture(int planetIndex)
    {
        if (_gameManager == null)
        {
            Debug.LogError("[MinimapUI] No GameManager instance.");
            return null;
        }

        var planetGen = _gameManager.enableMultiPlanetSystem
            ? _gameManager.GetPlanetGenerator(planetIndex)
            : _gameManager.planetGenerator;

        if (planetGen == null)
        {
            Debug.LogWarning($"[MinimapUI] Planet generator not found for index {planetIndex}");
            return null;
        }

        var grid = planetGen.Grid;
        if (grid == null)
        {
            Debug.LogWarning("[MinimapUI] Planet Grid missing.");
            return null;
        }

        // Debug info
        Debug.Log($"[MinimapUI] Generating minimap for planet {planetIndex}:");
        Debug.Log($"  - Grid tile count: {grid.TileCount}");
        Debug.Log($"  - Planet data dictionary count: {planetGen.data?.Count ?? 0}");
        Debug.Log($"  - Planet has generated surface: {planetGen.HasGeneratedSurface}");
        Debug.Log($"  - TileDataHelper available: {TileDataHelper.Instance != null}");

        int width = minimapResolution.x;
        int height = minimapResolution.y;
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

    var lut = EnsureIndexLUTForBody(planetIndex, false, grid, width, height);
    if (lut == null) return null;

        var tileDataCache = new Dictionary<int, HexTileData>();
        var tileOffsetCache = new Dictionary<int, Vector2>();
        var pixels = new Color32[width * height];

        float[] vArr = new float[height]; for (int y = 0; y < height; y++) vArr[y] = (y + 0.5f) / height;
        float[] uArr = new float[width]; for (int x = 0; x < width; x++) uArr[x] = (x + 0.5f) / width;

        for (int y = 0; y < height; y++)
        {
            float v = vArr[y]; int yBase = y * width;
            for (int x = 0; x < width; x++)
            {
                int tileIndex = lut[yBase + x];
                Color color;
                if (tileIndex < 0)
                {
                    color = Color.magenta;
                }
                else
                {
                    if (!tileDataCache.TryGetValue(tileIndex, out var tileData) || tileData == null)
                    {
                        tileData = planetGen.GetHexTileData(tileIndex);
                        if (tileData == null && TileDataHelper.Instance != null)
                        {
                            var (helperTileData, isMoon) = TileDataHelper.Instance.GetTileDataFromPlanet(tileIndex, planetIndex);
                            if (!isMoon && helperTileData != null) tileData = helperTileData;
                        }
                        if (tileData == null && planetGen.data != null && planetGen.data.ContainsKey(tileIndex))
                            tileData = planetGen.data[tileIndex];
                        tileDataCache[tileIndex] = tileData;
                    }
                    if (tileData == null)
                    {
                        color = new Color(0.35f, 0.35f, 0.35f);
                    }
                    else
                    {
                        if (!tileOffsetCache.TryGetValue(tileIndex, out var offset))
                        {
                            int hash = tileIndex * 9781 + 7;
                            float ox = ((hash >> 8) & 0xFF) / 255f;
                            float oy = (hash & 0xFF) / 255f;
                            offset = new Vector2(ox, oy);
                            tileOffsetCache[tileIndex] = offset;
                        }
                        float sampleU = Mathf.Repeat(uArr[x] + offset.x, 1f);
                        float sampleV = Mathf.Repeat(v + offset.y, 1f);
                        color = (colorProvider != null) ? colorProvider.ColorFor(tileData, new Vector2(sampleU, sampleV)) : GetDefaultBiomeColour(tileData.biome);
                    }
                }
                int dstX = width - 1 - x; int dstY = height - 1 - y;
                pixels[dstY * width + dstX] = color;
            }
        }

    tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    private Color GetDefaultBiomeColour(Biome biome)
    {
        return biome switch
        {
            Biome.Ocean => new Color(0.2f, 0.4f, 0.8f, 1f),
            Biome.Forest => new Color(0.2f, 0.6f, 0.2f, 1f),
            Biome.Desert => new Color(0.8f, 0.7f, 0.3f, 1f),
            Biome.Mountain => new Color(0.6f, 0.5f, 0.4f, 1f),
            Biome.Plains => new Color(0.4f, 0.7f, 0.3f, 1f),
            Biome.Snow => new Color(0.9f, 0.9f, 0.9f, 1f),
            Biome.Tundra => new Color(0.6f, 0.7f, 0.8f, 1f),
            Biome.Jungle => new Color(0.1f, 0.5f, 0.1f, 1f),
            Biome.Grassland => new Color(0.5f, 0.8f, 0.3f, 1f),
            Biome.Marsh => new Color(0.3f, 0.5f, 0.4f, 1f),
            Biome.Swamp => new Color(0.2f, 0.4f, 0.3f, 1f),
            Biome.Taiga => new Color(0.3f, 0.6f, 0.4f, 1f),
            Biome.Savannah => new Color(0.7f, 0.6f, 0.3f, 1f),
            Biome.Coast => new Color(0.4f, 0.6f, 0.8f, 1f),
            Biome.Volcanic => new Color(0.8f, 0.3f, 0.2f, 1f),
            Biome.Steam => new Color(0.7f, 0.7f, 0.8f, 1f),
            Biome.Frozen => new Color(0.8f, 0.8f, 0.9f, 1f),
            Biome.Arctic => new Color(0.9f, 0.9f, 1.0f, 1f),
            
            // Real Solar System Planet Biomes
            Biome.MartianRegolith => new Color(0.6f, 0.3f, 0.2f, 1f),
            Biome.MartianCanyon => new Color(0.5f, 0.2f, 0.1f, 1f),
            Biome.MartianPolarIce => new Color(0.8f, 0.8f, 0.9f, 1f),
            Biome.MartianDunes => new Color(0.7f, 0.4f, 0.2f, 1f),
            
            Biome.VenusianLava => new Color(1.0f, 0.4f, 0.1f, 1f),
            Biome.VenusianPlains => new Color(0.7f, 0.5f, 0.3f, 1f),
            Biome.VenusianHighlands => new Color(0.6f, 0.4f, 0.3f, 1f),
            
            Biome.MercurianCraters => new Color(0.5f, 0.5f, 0.5f, 1f),
            Biome.MercurianBasalt => new Color(0.4f, 0.4f, 0.4f, 1f),
            Biome.MercurianScarp => new Color(0.6f, 0.6f, 0.6f, 1f),
            
            Biome.JovianClouds => new Color(0.8f, 0.7f, 0.5f, 1f),
            Biome.JovianStorm => new Color(0.9f, 0.6f, 0.4f, 1f),
            Biome.SaturnianRings => new Color(0.9f, 0.8f, 0.6f, 1f),
            Biome.SaturnianClouds => new Color(0.8f, 0.7f, 0.5f, 1f),
            
            Biome.UranianIce => new Color(0.7f, 0.8f, 0.9f, 1f),
            Biome.UranianMethane => new Color(0.6f, 0.7f, 0.8f, 1f),
            Biome.NeptunianWinds => new Color(0.5f, 0.6f, 0.8f, 1f),
            Biome.NeptunianIce => new Color(0.6f, 0.7f, 0.9f, 1f),
            Biome.NeptunianSurface => new Color(0.4f, 0.5f, 0.7f, 1f),
            
            Biome.PlutoCryo => new Color(0.8f, 0.8f, 0.9f, 1f),
            Biome.PlutoTholins => new Color(0.7f, 0.6f, 0.5f, 1f),
            Biome.PlutoMountains => new Color(0.6f, 0.7f, 0.8f, 1f),
            
            Biome.TitanLakes => new Color(0.3f, 0.4f, 0.6f, 1f),
            Biome.TitanDunes => new Color(0.6f, 0.5f, 0.4f, 1f),
            Biome.TitanIce => new Color(0.8f, 0.8f, 0.9f, 1f),
            
            Biome.EuropaIce => new Color(0.9f, 0.9f, 1.0f, 1f),
            Biome.EuropaRidges => new Color(0.8f, 0.8f, 0.9f, 1f),
            
            Biome.IoVolcanic => new Color(0.9f, 0.3f, 0.1f, 1f),
            Biome.IoSulfur => new Color(0.9f, 0.8f, 0.2f, 1f),
            
            Biome.MoonDunes => new Color(0.7f, 0.7f, 0.7f, 1f),
            Biome.MoonCaves => new Color(0.5f, 0.5f, 0.5f, 1f),
            
            _ => new Color(0.5f, 0.5f, 0.5f, 1f)
        };
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (minimapImage == null || minimapImage.texture == null) return;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                minimapImage.rectTransform, eventData.position, eventData.pressEventCamera, out var local))
            return;

        // Start drag tracking
        _isDragging = true;
        _lastDragPosition = eventData.position;

        // If not zoomed in, this will be a click-to-move
        if (_currentZoom <= 1.1f)
        {
            HandleClickToMove(local);
        }
        // If zoomed in, prepare for drag-to-pan
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (_isDragging)
        {
            _isDragging = false;
            
            // If it was a short click (not much dragging), treat as click-to-move even when zoomed
            float dragDistance = Vector2.Distance(eventData.position, _lastDragPosition);
            if (dragDistance < 10f) // 10 pixels threshold
            {
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        minimapImage.rectTransform, eventData.position, eventData.pressEventCamera, out var local))
                {
                    HandleClickToMove(local);
                }
            }
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_isDragging || minimapImage == null || _currentZoom <= 1.1f) return;

        // Calculate drag delta in screen space
        Vector2 dragDelta = eventData.position - _lastDragPosition;
        _lastDragPosition = eventData.position;

        // Convert drag delta to UV space
        Vector2 size = minimapImage.rectTransform.rect.size;
        Vector2 uvDelta = new Vector2(
            -dragDelta.x / size.x * minimapImage.uvRect.width,
            -dragDelta.y / size.y * minimapImage.uvRect.height
        );

        // Apply drag to pan offset
        _panOffset += uvDelta;
        _panOffset.x = Mathf.Clamp01(_panOffset.x);
        _panOffset.y = Mathf.Clamp01(_panOffset.y);

        // Update the view
        SetZoom(_currentZoom);
    }

    private void HandleClickToMove(Vector2 localPoint)
    {
        Debug.Log("[MinimapUI] === CLICK DEBUG START ===");
        Debug.Log($"[MinimapUI] Input localPoint: ({localPoint.x:F2}, {localPoint.y:F2})");

        // RawImage pixel size in local space
        var rawSize = minimapImage.rectTransform.rect.size;
        Debug.Log($"[MinimapUI] RawImage size: ({rawSize.x:F2}, {rawSize.y:F2})");

        // Local (0,0) is rect center -> convert to 0..1
        float normX = (localPoint.x + rawSize.x * 0.5f) / rawSize.x;
        float normY = (localPoint.y + rawSize.y * 0.5f) / rawSize.y;

        Debug.Log($"[MinimapUI] Normalized (pre-mirror): normX={normX:F4}, normY={normY:F4}");

        // Un-mirror the click to match what is actually drawn
        if (_isHorizontallyMirrored) normX = 1f - normX;
        if (_isVerticallyMirrored)   normY = 1f - normY;

        normX = Mathf.Clamp01(normX);
        normY = Mathf.Clamp01(normY);
        Debug.Log($"[MinimapUI] Normalized (post-mirror): normX={normX:F4}, normY={normY:F4}");

        // Apply current zoom/pan window
        var uvRect = minimapImage.uvRect;           // (x,y,width,height) in 0..1
        Debug.Log($"[MinimapUI] UV Rect: {uvRect}");
    float worldU = uvRect.x + normX * uvRect.width;
    float worldV = uvRect.y + normY * uvRect.height;
    Debug.Log($"[MinimapUI] World UV coordinates: worldU={worldU:F4}, worldV={worldV:F4}");

    // Some pipelines rotate the minimap content by 180°. Compensate by shifting U by 0.5.
    float adjU = Mathf.Repeat(worldU + 0.5f, 1f);
    Debug.Log($"[MinimapUI] Adjusted U (rot+180): adjU={adjU:F4}");

    // Inverse of position indicator mapping
        // Indicator uses: u = 1 - (lon + PI) / (2PI)  =>  lon = 2PI * (1 - u) - PI
        //                  v = 0.5 + (lat / PI)      =>  lat = (v - 0.5) * PI
    float lonRad = 2f * Mathf.PI * (1f - adjU) - Mathf.PI;
        float latRad = (worldV - 0.5f) * Mathf.PI;
        Debug.Log($"[MinimapUI] Spherical coordinates: lonRad={lonRad:F4} ({lonRad*Mathf.Rad2Deg:F2}°), latRad={latRad:F4} ({latRad*Mathf.Rad2Deg:F2}°)");

        // Equirectangular to local direction
        Vector3 localDir = new Vector3(
            Mathf.Sin(lonRad) * Mathf.Cos(latRad),
            Mathf.Sin(latRad),
            Mathf.Cos(lonRad) * Mathf.Cos(latRad)
        ).normalized;
        Debug.Log($"[MinimapUI] Local direction vector: {localDir}");

        // Decide target body (planet or moon) and respect its transform
        var camMgr = FindAnyObjectByType<PlanetaryCameraManager>();
        bool isOnMoon = camMgr != null && camMgr.IsOnMoon;
        Vector3 worldDir;
        if (isOnMoon)
        {
            var moonGen = _gameManager?.GetCurrentMoonGenerator();
            worldDir = moonGen != null ? moonGen.transform.TransformDirection(localDir).normalized : localDir;
        }
        else
        {
            var currentPlanetGen = _gameManager?.GetCurrentPlanetGenerator();
            worldDir = currentPlanetGen != null ? currentPlanetGen.transform.TransformDirection(localDir).normalized : localDir;
        }

        Debug.Log($"[MinimapUI] World direction vector: {worldDir}");
        Debug.Log("[MinimapUI] Jumping camera to world direction.");

        if (camMgr != null)
        {
            // Pass whether we are targeting the moon so the camera orbits the right body
            camMgr.JumpToDirection(worldDir, isOnMoon);
        }

        Debug.Log("[MinimapUI] === CLICK DEBUG END ===");
    }

    public void OnScroll(PointerEventData eventData)
    {
        float delta = eventData.scrollDelta.y;
        if (Mathf.Approximately(delta, 0f)) return;
        SetZoom(Mathf.Clamp(_currentZoom + delta * zoomSpeed, minZoom, maxZoom));
    }

    private void SetZoom(float zoom)
    {
        _currentZoom = Mathf.Clamp(zoom, minZoom, maxZoom);
        
        // Use UV rect to zoom into specific areas instead of scaling the whole image
        if (minimapImage != null)
        {
            float uvSize = 1f / _currentZoom;
            
            // Center the UV rect around the pan offset, clamped to valid bounds
            float uvX = Mathf.Clamp(_panOffset.x - uvSize * 0.5f, 0f, 1f - uvSize);
            float uvY = Mathf.Clamp(_panOffset.y - uvSize * 0.5f, 0f, 1f - uvSize);
            
            minimapImage.uvRect = new Rect(uvX, uvY, uvSize, uvSize); // Fixed typo: was uvY instead of uvSize
        }
        
        UpdateZoomLevelText();
        UpdateZoomButtonStates();
        UpdatePositionIndicator();
    }
    
    /// <summary>
    /// Zoom in using button click
    /// </summary>
    public void ZoomIn()
    {
        SetZoom(_currentZoom + buttonZoomStep);
    }
    
    /// <summary>
    /// Zoom out using button click
    /// </summary>
    public void ZoomOut()
    {
        SetZoom(_currentZoom - buttonZoomStep);
    }
    
    /// <summary>
    /// Reset zoom to show entire minimap (can be called from a button)
    /// </summary>
    public void ResetZoom()
    {
        _panOffset = new Vector2(0.5f, 0.5f); // Center the view
        SetZoom(1f);
    }
    
    /// <summary>
    /// Update the zoom level text display
    /// </summary>
    private void UpdateZoomLevelText()
    {
        if (zoomLevelText != null)
        {
            zoomLevelText.text = $"{(_currentZoom * 100):F0}%";
        }
    }
    
    /// <summary>
    /// Update zoom button interactability based on current zoom level
    /// </summary>
    private void UpdateZoomButtonStates()
    {
        if (zoomInButton != null)
            zoomInButton.interactable = _currentZoom < maxZoom;
        if (zoomOutButton != null)
            zoomOutButton.interactable = _currentZoom > minZoom;
    }

    /// <summary>
    /// OnClick handler for the Moon button. Switches to the current planet's moon if available.
    /// </summary>
    private void OnMoonButtonClicked()
    {
        var camMgr = FindAnyObjectByType<PlanetaryCameraManager>();
        if (_gameManager == null || camMgr == null)
        {
            Debug.LogWarning("[MinimapUI] Cannot switch to moon: missing GameManager or CameraManager.");
            return;
        }

        int planetIndex = Mathf.Clamp(_gameManager.currentPlanetIndex, 0, Mathf.Max(0, _gameManager.maxPlanets - 1));
        var moonGen = _gameManager.GetMoonGenerator(planetIndex);
        if (moonGen == null || moonGen.Grid == null || moonGen.Grid.TileCount == 0)
        {
            Debug.LogWarning($"[MinimapUI] Planet {planetIndex} has no moon or moon grid; cannot switch.");
            return;
        }

        camMgr.SwitchToMoon(true);
        ShowMinimapForPlanet(planetIndex);
    }

    /// <summary>
    /// OnClick handler for the Main planet button. Switches to the configured main planet and ensures we're not on the moon.
    /// </summary>
    private void OnMainPlanetButtonClicked()
    {
        var camMgr = FindAnyObjectByType<PlanetaryCameraManager>();
        if (_gameManager == null || camMgr == null)
        {
            Debug.LogWarning("[MinimapUI] Cannot switch to main planet: missing GameManager or CameraManager.");
            return;
        }

        int targetIndex = Mathf.Clamp(mainPlanetIndex, 0, Mathf.Max(0, _gameManager.maxPlanets - 1));
        if (_gameManager.enableMultiPlanetSystem)
        {
            _gameManager.SetCurrentPlanet(targetIndex);
        }
        camMgr.SwitchToMoon(false);

        // Reflect in UI
        if (planetDropdown != null)
        {
            planetDropdown.value = targetIndex;
            planetDropdown.RefreshShownValue();
        }

        ShowMinimapForPlanet(targetIndex);
    }
    
    /// <summary>
    /// Update the position indicator to show where the camera is positioned on the planet
    /// </summary>
    private void UpdatePositionIndicator()
    {
        if (positionIndicator == null || minimapImage == null) return;

        var camera = Camera.main;
        if (camera == null) return;

        // Determine whether to show indicator on planet or moon based on camera target
        var camMgr = FindAnyObjectByType<PlanetaryCameraManager>();
        bool isOnMoon = camMgr != null && camMgr.IsOnMoon;

        Vector3 bodyPosition;
        if (isOnMoon)
        {
            var moonGen = _gameManager?.GetCurrentMoonGenerator();
            if (moonGen == null) return;
            bodyPosition = moonGen.transform.position;
        }
        else
        {
            var currentPlanetGen = _gameManager?.GetCurrentPlanetGenerator();
            if (currentPlanetGen == null) return;
            bodyPosition = currentPlanetGen.transform.position;
        }
        
        // Calculate camera position relative to planet center
    Vector3 relativePos = camera.transform.position - bodyPosition;
        
        // Normalize to get direction from planet center
        Vector3 direction = relativePos.normalized;
        
        // Convert to spherical coordinates (latitude/longitude)
        float lat = Mathf.Asin(direction.y); // Latitude (-π/2 to π/2)
        float lon = Mathf.Atan2(direction.x, direction.z); // Longitude (-π to π)
        
        // Convert to UV coordinates (0-1)
        // Longitude: -π to π -> 0 to 1 (0 = left edge, 1 = right edge)
        // Try horizontal flip to match minimap orientation
        float u = 1f- (lon + Mathf.PI) / (2f * Mathf.PI);
        
        // Latitude: -π/2 to π/2 -> 0 to 1 (0 = top edge, 1 = bottom edge)
        // The texture is flipped vertically after generation, so we need to invert the V coordinate
        float v = 0.5f + (lat / Mathf.PI); // Invert to match the flipped texture
        
        // Clamp to valid range
        u = Mathf.Repeat(u, 1f);
        v = Mathf.Clamp01(v);
        
        // Always show the position indicator, even if outside current view
    // Convert to local position on minimap (0-1 range)
        var rect = minimapImage.rectTransform.rect;
        Vector2 localPos = new Vector2(
            (u - 0.5f) * rect.width,
            (v - 0.5f) * rect.height
        );
        
        // Clamp to minimap bounds
        localPos.x = Mathf.Clamp(localPos.x, -rect.width * 0.5f, rect.width * 0.5f);
        localPos.y = Mathf.Clamp(localPos.y, -rect.height * 0.5f, rect.height * 0.5f);
        
        positionIndicator.anchoredPosition = localPos;
        positionIndicator.gameObject.SetActive(true);
        
        // Debug logging for position indicator (only log occasionally to avoid spam)
        if (Time.frameCount % 3000 == 0) // Log every 300 frames (once per 5 seconds at 60fps)
        {
            Debug.Log($"[MinimapUI] === POSITION INDICATOR DEBUG ===");
            Debug.Log($"[MinimapUI] Camera direction: {direction}");
            Debug.Log($"[MinimapUI] Spherical coords: lat={lat:F4} ({lat * Mathf.Rad2Deg:F2}°), lon={lon:F4} ({lon * Mathf.Rad2Deg:F2}°)");
            Debug.Log($"[MinimapUI] UV coordinates: u={u:F4}, v={v:F4}");
            Debug.Log($"[MinimapUI] Local position: {localPos}");
            Debug.Log($"[MinimapUI] === POSITION INDICATOR DEBUG END ===");
        }
    }
}
