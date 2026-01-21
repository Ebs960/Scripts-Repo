using System;
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
/// - Scroll wheel or buttons to zoom; click to move the camera to that spot.
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
    [Tooltip("Resolution for minimap texture. Higher = better quality but more memory. Recommended: 1024x512 to 4096x2048")]
    [SerializeField] private Vector2Int minimapResolution = new Vector2Int(1024, 512);
    
    [Tooltip("If true, reuse the flat map texture (if available) instead of regenerating. Much faster and uses less memory.")]
    [SerializeField] private bool reuseFlatMapTexture = true;
    [Tooltip("Number of rows to process per frame during LUT generation (higher = faster but more frame time)")]
    [SerializeField] private int lutRowsPerFrame = 50; // Process 50 rows per frame for smooth generation
    [SerializeField] private MinimapColorProvider colorProvider;
    [SerializeField] private float maxZoom = 4f;
    [SerializeField] private float minZoom = 0.5f;
    [SerializeField] private float zoomSpeed = 1f;
    [SerializeField] private float buttonZoomStep = 0.5f;

    [Header("Switching Settings")]
    [Tooltip("Index of the main planet to switch back to when pressing the main planet button (0 = Earth by default)")]
    [SerializeField] private int mainPlanetIndex = 0;

    // Pre-generation is always on.
    [Header("Atlas Settings")]
    [Tooltip("When enabled, build a per-tile color atlas (array) and use it as a fast lookup during minimap generation.")]
    [SerializeField] private bool useTileColorAtlas = true;
    [Tooltip("Optional compute shader to accelerate minimap generation on GPU (uses LUT + tile atlas texture).")]
    [SerializeField] private ComputeShader minimapComputeShader;
    [Tooltip("When true, require a compute shader for atlas-only generation; minimap is GPU-only.")]
    

    // Private fields
    // Planet minimap caches (moons are treated as separate planets)
    private readonly Dictionary<int, Texture> _minimapTextures = new();
    // Fast path: per-body (planet or moon) LUT cache mapping pixel -> tile index
    // Key: P{planetIndex}_M{0|1}_W{w}_H{h}
    private static readonly Dictionary<string, int[]> _bodyIndexLUT = new();
    // Optional per-body tile color atlas cache. Key format matches LUT keys but without resolution.
    private static readonly Dictionary<string, Color32[]> _tileAtlasCache = new();
    // GPU resources cache (per-resolution key)
    private static readonly Dictionary<string, ComputeBuffer> _lutComputeBufferCache = new();
    private static readonly Dictionary<string, RenderTexture> _gpuResultCache = new();
    private static readonly Dictionary<string, Texture2D> _gpuAtlasTextureCache = new();
    // Removed CPU pixel/texture caches (GPU-only path)
    private float _currentZoom = 1f;
    private Vector2 _panOffset = Vector2.zero; // For panning around the zoomed minimap
    private bool _isDragging = false;
    private Vector2 _lastDragPosition;
    private GameManager _gameManager;
    private bool _minimapsPreGenerated = false;
    private LoadingPanelController _loadingPanel;
    // Moons are separate planets now; no "onMoon" camera mode tracking.
    private PlanetaryCameraManager _cachedCameraManager; // Cached reference to avoid repeated FindAnyObjectByType calls
    
    // UI mirroring cache
    [SerializeField] private Camera uiCamera; // leave null for Screen Space - Overlay
    private bool _isHorizontallyMirrored;
    private bool _isVerticallyMirrored;

    // Public property for GameManager to check
    public bool MinimapsPreGenerated => _minimapsPreGenerated;
    // Backward compatibility: external code can still query this; always true
    public bool PreGenerateAll => true;

    // Expose LUT + atlas (planet-only accessors for overlay system)
    public int[] GetPlanetLUT(int planetIndex, out int width, out int height)
    {
        width = minimapResolution.x; height = minimapResolution.y;
        string key = $"P{planetIndex}_M0_W{width}_H{height}";
        if (_bodyIndexLUT.TryGetValue(key, out var lut)) return lut;
        // Attempt to build if planet exists
            var planetGenRef = _gameManager != null ? _gameManager.GetPlanetGenerator(planetIndex) : null;
        var grid = planetGenRef?.Grid;
        if (grid == null) return null;
        return EnsureIndexLUTForBody(planetIndex, false, grid, width, height);
    }

    public Color32[] GetTileAtlasColors(int planetIndex, bool isMoon)
    {
        string key = $"P{planetIndex}_M{(isMoon?1:0)}";
        if (_tileAtlasCache.TryGetValue(key, out var atlas)) return atlas;
        // Moons are treated as separate planets now; ignore isMoon and use the planet generator for the given index.
            PlanetGenerator planetGen = _gameManager != null ? _gameManager.GetPlanetGenerator(planetIndex) : null;
        SphericalHexGrid grid = planetGen?.Grid;
        if (grid == null) return null;
        return EnsureTileColorAtlas(planetIndex, isMoon, grid);
    }

    public Texture GetPlanetMinimapTexture(int planetIndex)
    {
        _minimapTextures.TryGetValue(planetIndex, out var tex);
        return tex;
    }

    // Removed CPU helpers (UploadPixels, GetOrCreatePixelBuffer, CreateCpuTexture)

    // Build or fetch a LUT mapping each minimap pixel to a tile index for a specific body
    // PERFORMANCE: Now uses batched coroutine generation to avoid blocking
    private int[] EnsureIndexLUTForBody(int planetIndex, bool isMoon, SphericalHexGrid grid, int width, int height)
    {
        if (grid == null) return null;
        string key = $"P{planetIndex}_M{(isMoon ? 1 : 0)}_W{width}_H{height}";
        if (_bodyIndexLUT.TryGetValue(key, out var cached)) return cached;

        // Start batched generation coroutine
        StartCoroutine(GenerateLUTBatched(planetIndex, isMoon, grid, width, height, key));
        
        // Return null for now - caller should wait for coroutine to complete
        // For immediate use, we'll need to wait for the coroutine
        return null;
    }
    
    // Batched LUT generation - processes rows per frame to avoid blocking
    private IEnumerator GenerateLUTBatched(int planetIndex, bool isMoon, SphericalHexGrid grid, int width, int height, string key)
    {
        var lut = new int[width * height];
        float mapWidth = grid.MapWidth;
        float mapHeight = grid.MapHeight;

        // Process rows in batches to avoid blocking
        int rowsProcessed = 0;
        while (rowsProcessed < height)
        {
            int rowsThisFrame = Mathf.Min(lutRowsPerFrame, height - rowsProcessed);
            
            for (int y = rowsProcessed; y < rowsProcessed + rowsThisFrame; y++)
            {
                int yBase = y * width;
                float v = (y + 0.5f) / height;
                float worldZ = (v - 0.5f) * mapHeight;
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    float worldX = (u - 0.5f) * mapWidth;
                    int tileIndex = grid.GetTileAtPosition(new Vector3(worldX, 0f, worldZ));
                    lut[yBase + x] = tileIndex;
                }
            }
            
            rowsProcessed += rowsThisFrame;
            yield return null; // Yield after processing batch
        }

        _bodyIndexLUT[key] = lut;
    }
    
    // Synchronous version for immediate use (fallback)
    private int[] EnsureIndexLUTForBodySync(int planetIndex, bool isMoon, SphericalHexGrid grid, int width, int height)
    {
        if (grid == null) return null;
        string key = $"P{planetIndex}_M{(isMoon ? 1 : 0)}_W{width}_H{height}";
        if (_bodyIndexLUT.TryGetValue(key, out var cached)) return cached;

        var lut = new int[width * height];
        float mapWidth = grid.MapWidth;
        float mapHeight = grid.MapHeight;

        // Process all rows (for immediate use cases)
        for (int y = 0; y < height; y++)
        {
            int yBase = y * width;
            float v = (y + 0.5f) / height;
            float worldZ = (v - 0.5f) * mapHeight;
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                float worldX = (u - 0.5f) * mapWidth;
                int tileIndex = grid.GetTileAtPosition(new Vector3(worldX, 0f, worldZ));
                lut[yBase + x] = tileIndex;
            }
        }

    _bodyIndexLUT[key] = lut;
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

    // Cached tile data arrays to avoid repeated lookups
    private static readonly Dictionary<string, HexTileData[]> _cachedTileDataArrays = new();

    // Build or fetch a compact per-tile color atlas for a body (planet or moon).
    // PERFORMANCE: Pre-caches all tile data to avoid per-tile lookups
    // Atlas layout: square texture array flattened to Color32[] where index -> tileIndex mapping is stored in parallel by tile order.
    private Color32[] EnsureTileColorAtlas(int planetIndex, bool isMoon, SphericalHexGrid grid)
    {
        if (!useTileColorAtlas || grid == null) return null;
        string key = $"P{planetIndex}_M{(isMoon ? 1 : 0)}";
        if (_tileAtlasCache.TryGetValue(key, out var cached)) return cached;

        int tileCount = grid.TileCount;
        var atlas = new Color32[tileCount];
        
        // PERFORMANCE: Pre-cache all tile data once to avoid repeated lookups
        string tileDataKey = $"TILEDATA_P{planetIndex}_M{(isMoon ? 1 : 0)}";
        HexTileData[] tileDataArray;
        
        if (!_cachedTileDataArrays.TryGetValue(tileDataKey, out tileDataArray))
        {
            // Pre-fetch all tile data in one pass
            tileDataArray = new HexTileData[tileCount];
            // Moons are treated as separate planets now; prefer per-index generator and fall back to legacy single generator.
                PlanetGenerator planetGen = _gameManager != null ? (_gameManager.GetPlanetGenerator(planetIndex) ?? _gameManager.planetGenerator) : null;

        for (int i = 0; i < tileCount; i++)
                {
                    tileDataArray[i] = planetGen?.GetHexTileData(i);
                
                // Fallback to TileSystem if needed
                if (tileDataArray[i] == null && TileSystem.Instance != null && TileSystem.Instance.IsReady())
            {
                    tileDataArray[i] = TileSystem.Instance.GetTileDataFromPlanet(i, planetIndex);
            }
            }
            
            _cachedTileDataArrays[tileDataKey] = tileDataArray;
        }

        // Now build atlas using pre-cached tile data (much faster)
        for (int i = 0; i < tileCount; i++)
        {
            var tileData = tileDataArray[i];
            Color c;
            
            if (tileData == null)
            {
                c = new Color(0.35f, 0.35f, 0.35f);
            }
            else
            {
                int hash = i * 9781 + 7;
                float ox = ((hash >> 8) & 0xFF) / 255f;
                float oy = (hash & 0xFF) / 255f;
                float sampleU = Mathf.Repeat(0.5f + ox, 1f);
                float sampleV = Mathf.Repeat(0.5f + oy, 1f);
                c = (colorProvider != null) ? colorProvider.ColorFor(tileData, new Vector2(sampleU, sampleV)) : GetDefaultBiomeColour(tileData.biome);
            }
            atlas[i] = (Color32)c;
        }

        _tileAtlasCache[key] = atlas;
    
        return atlas;
    }

    // Convert a Color32[] atlas into a GPU Texture2D (1-row) for the compute shader
    private Texture2D BuildAtlasTextureForGPU(Color32[] atlas, string cacheKey)
    {
        if (atlas == null || atlas.Length == 0) return null;
        int w = atlas.Length;
        if (_gpuAtlasTextureCache.TryGetValue(cacheKey, out var existing))
        {
            if (existing.width == w) return existing;
            Destroy(existing);
            _gpuAtlasTextureCache.Remove(cacheKey);
        }
        var tex = new Texture2D(w, 1, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear // Use bilinear filtering for smoother appearance
        };
    // Safe upload: 1-row atlas; SetPixels32 cost negligible vs. crash risk.
    // (Previous BlockCopy on Color32[] triggered ArgumentException on some runtimes.)
    tex.SetPixels32(atlas);
    tex.Apply(false, false);
        _gpuAtlasTextureCache[cacheKey] = tex;
        return tex;
    }

    // Create or fetch a compute buffer for a LUT array for given key
    private ComputeBuffer EnsureLUTComputeBuffer(string key, int[] lut)
    {
        if (lut == null) return null;
        if (_lutComputeBufferCache.TryGetValue(key, out var buf))
        {
            if (buf.count == lut.Length) return buf;
            buf.Release();
            _lutComputeBufferCache.Remove(key);
        }
        var newBuf = new ComputeBuffer(lut.Length, sizeof(int));
        newBuf.SetData(lut);
        _lutComputeBufferCache[key] = newBuf;
        return newBuf;
    }

    // Dispatch compute shader to fill a RenderTexture result. Returns a Texture2D (CPU) ready to be used by UI.
    // Run compute shader and return the RenderTexture result (no CPU readback).
    private RenderTexture RunComputeMinimap(int planetIndex, bool isMoon, int width, int height, int[] lut, Color32[] atlas)
    {
        if (minimapComputeShader == null || lut == null || atlas == null) return null;

        string key = $"P{planetIndex}_M{(isMoon?1:0)}_W{width}_H{height}";

        // Ensure LUT compute buffer
        var lutBuf = EnsureLUTComputeBuffer(key, lut);
        if (lutBuf == null) return null;

        // Build atlas texture (cached)
        string atlasKey = $"P{planetIndex}_M{(isMoon?1:0)}_ATLAS_{atlas.Length}";
        var atlasTex = BuildAtlasTextureForGPU(atlas, atlasKey);
        if (atlasTex == null) return null;

        // Ensure result render texture
        RenderTexture rt;
        if (!_gpuResultCache.TryGetValue(key, out rt) || rt == null || rt.width != width || rt.height != height)
        {
            if (rt != null) rt.Release();
            rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear // Use bilinear filtering for smoother appearance
            };
            rt.Create();
            _gpuResultCache[key] = rt;
        }

        int kernel = minimapComputeShader.FindKernel("CSMain");
        minimapComputeShader.SetBuffer(kernel, "_LUT", lutBuf);
        minimapComputeShader.SetTexture(kernel, "_TileAtlas", atlasTex);
        minimapComputeShader.SetTexture(kernel, "_Result", rt);
        minimapComputeShader.SetInt("_Width", width);
        minimapComputeShader.SetInt("_Height", height);
        minimapComputeShader.SetInt("_TileCount", atlas.Length);

        int tx = Mathf.CeilToInt(width / 8f);
        int ty = Mathf.CeilToInt(height / 8f);
        minimapComputeShader.Dispatch(kernel, tx, ty, 1);

        

        return rt;
    }

    void Awake()
    {
        _gameManager = GameManager.Instance;
        _loadingPanel = FindAnyObjectByType<LoadingPanelController>();
        _cachedCameraManager = FindAnyObjectByType<PlanetaryCameraManager>(); // Cache camera manager reference

    // if (minimapImage == null) { /* optional: assign via inspector */ }
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
    }
        // Wire Main planet button to switch back to configured main planet
        if (mainPlanetButton != null)
        {
            mainPlanetButton.onClick.AddListener(OnMainPlanetButtonClicked);
        }
    else
    {
    }
        
        // Hide individual UI elements during loading, but keep GameObject active for coroutines
        // Hide UI while we generate minimaps (always pre-generation now)
        if (IsLoadingActive() || !_minimapsPreGenerated) HideUIElements();

    }

    /// <summary>
    /// Hide the visual UI elements while keeping the GameObject active for coroutines
    /// </summary>
    private void HideUIElements()
    {
        // Instead of deactivating GameObjects, make them invisible but keep them active
        // This ensures the MinimapUI component remains discoverable by GameManager
        if (minimapImage != null) 
        {
            minimapImage.color = new Color(1, 1, 1, 0); // Transparent
            minimapImage.raycastTarget = false; // Disable interaction
        }
        if (planetDropdown != null) 
        {
            var canvasGroup = planetDropdown.GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = planetDropdown.gameObject.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0; // Invisible
            canvasGroup.interactable = false; // Disable interaction
            canvasGroup.blocksRaycasts = false; // Don't block clicks
        }
        if (zoomInButton != null) 
        {
            var canvasGroup = zoomInButton.GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = zoomInButton.gameObject.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        if (zoomOutButton != null) 
        {
            var canvasGroup = zoomOutButton.GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = zoomOutButton.gameObject.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        if (moonButton != null) 
        {
            var canvasGroup = moonButton.GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = moonButton.gameObject.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        if (mainPlanetButton != null) 
        {
            var canvasGroup = mainPlanetButton.GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = mainPlanetButton.gameObject.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        if (zoomLevelText != null) 
        {
            var canvasGroup = zoomLevelText.GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = zoomLevelText.gameObject.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        if (positionIndicator != null) 
        {
            var canvasGroup = positionIndicator.GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = positionIndicator.gameObject.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }

    /// <summary>
    /// Show the visual UI elements
    /// </summary>
    private void ShowUIElements()
    {
        // Restore visibility and interaction for UI elements
        if (minimapImage != null) 
        {
            minimapImage.color = Color.white; // Visible
            minimapImage.raycastTarget = true; // Enable interaction
        }
        if (planetDropdown != null) 
        {
            var canvasGroup = planetDropdown.GetComponent<CanvasGroup>();
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1; // Visible
                canvasGroup.interactable = true; // Enable interaction
                canvasGroup.blocksRaycasts = true; // Allow clicks
            }
        }
        if (zoomInButton != null) 
        {
            var canvasGroup = zoomInButton.GetComponent<CanvasGroup>();
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1;
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }
        }
        if (zoomOutButton != null) 
        {
            var canvasGroup = zoomOutButton.GetComponent<CanvasGroup>();
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1;
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }
        }
        if (moonButton != null) 
        {
            var canvasGroup = moonButton.GetComponent<CanvasGroup>();
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1;
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }
        }
        if (mainPlanetButton != null) 
        {
            var canvasGroup = mainPlanetButton.GetComponent<CanvasGroup>();
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1;
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }
        }
        if (zoomLevelText != null) 
        {
            var canvasGroup = zoomLevelText.GetComponent<CanvasGroup>();
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1;
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }
        }
        if (positionIndicator != null) 
        {
            var canvasGroup = positionIndicator.GetComponent<CanvasGroup>();
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1;
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }
        }
    }

    void Update()
    {
        // Update position indicator every frame for smooth tracking
        UpdatePositionIndicator();
    }

    private void OnEnable()
    {
        RefreshMirrorFlags();

        // Subscribe to game completion event instead of individual planet events
        _gameManager = GameManager.Instance ?? _gameManager;
        if (_gameManager != null)
        {
            _gameManager.OnGameStarted -= HandleGameStarted;
            _gameManager.OnGameStarted += HandleGameStarted;
        }
    }

    private void OnDisable()
    {
        if (_gameManager != null)
        {
            _gameManager.OnGameStarted -= HandleGameStarted;
        }
    }

    /// <summary>
    /// Called when the game has finished startup. Initializes minimap UI and starts pre-generation if needed.
    /// </summary>
    private void HandleGameStarted()
    {
        _gameManager = GameManager.Instance ?? _gameManager;
        _loadingPanel = LoadingPanelController.Instance ?? _loadingPanel;

        // If minimaps were already generated, just show UI and populate dropdown
        if (_minimapsPreGenerated)
        {
            ShowUIElements();
            BuildPlanetDropdown();
            ShowMinimapForPlanet(_gameManager != null ? _gameManager.currentPlanetIndex : 0);
            return;
        }

        // Otherwise start pre-generation
        StartCoroutine(PreGenerateAllMinimaps());
    }

    void Start()
    {
        // Don't initialize minimap display until game is ready
        // BuildPlanetDropdown() and ShowMinimapForPlanet() will be called in HandleGameStarted()
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
        // Ensure the GameObject is active before starting coroutines
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }
        
        StartCoroutine(PreGenerateAllMinimaps());
    }

    /// <summary>
    /// Pre-generate all minimaps with performance optimizations
    /// </summary>
    public IEnumerator PreGenerateAllMinimaps()
    {
        if (_minimapsPreGenerated) yield break; // guard if called twice
        _minimapsPreGenerated = false;

        // Clear existing textures
        ClearMinimapCache();

        

        int totalPlanets = 0;
        if (_gameManager != null)
        {
            var planetData = _gameManager.GetPlanetData();
            totalPlanets = planetData?.Count ?? 0;
            if (totalPlanets == 0)
            {
                int generatorCount = 0;
                for (int i = 0; i < 20; i++)
                {
                    var gen = _gameManager.GetPlanetGenerator(i);
                    if (gen != null)
                    {
                        generatorCount = i + 1;
                    }
                }
                totalPlanets = generatorCount;
            }
            if (totalPlanets == 0 && _gameManager.planetGenerator != null) totalPlanets = 1;
        }

        if (totalPlanets == 0)
        {
            _minimapsPreGenerated = false;
            yield break;
        }


        // PERFORMANCE: Unified generation loop with reduced yields
        for (int planetIndex = 0; planetIndex < totalPlanets; planetIndex++)
        {
            string planetName = GetPlanetName(planetIndex);

            // Planet
            yield return StartCoroutine(GenerateBodyMinimapCoroutine(planetIndex, false, minimapResolution));

            // Update progress (batched - only update every few planets to reduce UI overhead)
            if (_loadingPanel != null && (planetIndex % 2 == 0 || planetIndex == totalPlanets - 1))
            {
                float progress = 0.8f + (0.1f * (float)(planetIndex + 1) / totalPlanets);
                _loadingPanel.SetProgress(progress);
                _loadingPanel.SetStatus($"Generated minimap for {planetName}...");
            }

            // PERFORMANCE: Reduced yields - only yield every other planet for better throughput
            if (planetIndex % 2 == 1)
            {
                yield return null;
            }
        }
        
        _minimapsPreGenerated = true;

        // Show the UI elements now that generation is complete
    ShowUIElements();

        // Signal LoadingPanelController that minimap generation is complete
        if (LoadingPanelController.Instance != null)
        {
            LoadingPanelController.Instance.OnMinimapGenerationComplete();
        }

        BuildPlanetDropdown();
        if (totalPlanets > 0)
        {
            ShowMinimapForPlanet(0);
        }
    }

    private string GetPlanetName(int planetIndex)
    {
        var planetData = _gameManager?.GetPlanetData();
        if (planetData != null && planetData.TryGetValue(planetIndex, out var pd))
        {
            return pd.planetName;
        }
        var planetGen = _gameManager?.GetPlanetGenerator(planetIndex);
        if (planetGen != null)
        {
            return planetGen.name.Replace("_Generator", "").Replace("Planet_", "");
        }
        return "Planet";
    }

    // Unified coroutine (GPU-only) for planet or moon
    // PERFORMANCE: Batched generation with reduced yields
    private IEnumerator GenerateBodyMinimapCoroutine(int planetIndex, bool isMoon, Vector2Int resolution)
    {
        PlanetGenerator planetGenRef = null;
        // Moons are treated as separate planets now; ignore isMoon and use the planet generator for the given index.
            planetGenRef = _gameManager != null ? (_gameManager.GetPlanetGenerator(planetIndex) ?? _gameManager.planetGenerator) : null;

        var grid = planetGenRef?.Grid;
        if (grid == null || grid.TileCount == 0) yield break;

        int width = resolution.x;
        int height = resolution.y;
        
        // PERFORMANCE: Generate LUT in batched coroutine and wait for it
        string lutKey = $"P{planetIndex}_M{(isMoon ? 1 : 0)}_W{width}_H{height}";
        int[] lut = null;
        
        // Check if already cached
        if (!_bodyIndexLUT.TryGetValue(lutKey, out lut))
        {
            // Start batched generation
            yield return StartCoroutine(GenerateLUTBatched(planetIndex, isMoon, grid, width, height, lutKey));
            
            // Retrieve the generated LUT
            if (!_bodyIndexLUT.TryGetValue(lutKey, out lut) || lut == null)
            {
                Debug.LogWarning($"[MinimapUI] Failed to generate LUT for {(isMoon ? "Moon" : "Planet")} {planetIndex}");
                yield break;
            }
        }

        // PERFORMANCE: Pre-cache tile data and build atlas (batched)
        Color32[] tileAtlas = EnsureTileColorAtlas(planetIndex, isMoon, grid);
        if (tileAtlas == null)
        {
            Debug.LogWarning($"[MinimapUI] Failed to generate tile atlas for {(isMoon ? "Moon" : "Planet")} {planetIndex}");
            yield break;
        }
        
        // Yield once after atlas generation to allow frame update
        yield return null;

        // Try GPU path
        if (minimapComputeShader != null && tileAtlas != null)
        {
            var gpuRT = RunComputeMinimap(planetIndex, isMoon, width, height, lut, tileAtlas);
            if (gpuRT != null)
            {
                _minimapTextures[planetIndex] = gpuRT;
                yield break;
            }
        }

        // GPU unavailable or atlas missing: skip (no CPU fallback)
        Debug.LogWarning($"[MinimapUI] {(isMoon ? "Moon" : "Planet")} {planetIndex} minimap skipped (GPU path unavailable).");
    }

    // Immediate generation (no yielding) used only if something missing at display time
    // PERFORMANCE: Uses synchronous LUT generation for immediate use
    private Texture GenerateBodyMinimapImmediate(int planetIndex, bool isMoon)
    {
        PlanetGenerator planetGenRef = _gameManager != null ? (_gameManager.GetPlanetGenerator(planetIndex) ?? _gameManager.planetGenerator) : null;
        var grid = planetGenRef?.Grid;
        if (grid == null || grid.TileCount == 0) return null;
        int width = minimapResolution.x; int height = minimapResolution.y;
        
        // Use synchronous LUT generation for immediate use
        var lut = EnsureIndexLUTForBodySync(planetIndex, isMoon, grid, width, height);
        if (lut == null) return null;
        
        var atlas = EnsureTileColorAtlas(planetIndex, isMoon, grid);
        if (minimapComputeShader != null && atlas != null)
        {
            var gpuRT = RunComputeMinimap(planetIndex, isMoon, width, height, lut, atlas);
            if (gpuRT != null)
            {
                _minimapTextures[planetIndex] = gpuRT;
                return gpuRT;
            }
        }
        return null; // no CPU fallback
    }


    /// <summary>
    /// Clear the minimap cache (useful for debugging and forcing regeneration)
    /// </summary>
    [ContextMenu("Clear Minimap Cache")]
    public void ClearMinimapCache()
    {
        // Release and destroy CPU and GPU textures safely
        foreach (var tex in _minimapTextures.Values)
        {
            if (tex == null) continue;
            if (tex is RenderTexture rt)
            {
                rt.Release();
                Destroy(rt);
            }
            else
            {
                Destroy(tex);
            }
        }
        _minimapTextures.Clear();
        _minimapsPreGenerated = false;
    // Clear per-body tile atlas cache as it depends on tile data
    _tileAtlasCache.Clear();
        // Release GPU resources
        foreach (var kv in _lutComputeBufferCache)
        {
            kv.Value?.Release();
            kv.Value?.Dispose(); // CRITICAL: Dispose ComputeBuffers to prevent GPU memory leaks
        }
        _lutComputeBufferCache.Clear();
        foreach (var kv in _gpuResultCache)
        {
            if (kv.Value != null)
            {
                kv.Value.Release();
                Destroy(kv.Value);
            }
        }
        _gpuResultCache.Clear();
        foreach (var kv in _gpuAtlasTextureCache)
        {
            if (kv.Value != null) Destroy(kv.Value);
        }
        _gpuAtlasTextureCache.Clear();
    }

    // ...existing code...
    
    /// <summary>
    /// Check if any loading panel is currently active
    /// </summary>
    private bool IsLoadingActive()
    {
        if (LoadingPanelController.Instance != null)
        {
            return LoadingPanelController.Instance.gameObject.activeSelf;
        }
        return false;
    }
    
    /// <summary>
    /// Public method to trigger deferred initialization (called by LoadingPanelController)
    /// </summary>
    public void TriggerDeferredInitialization()
    {
        // Only proceed if loading is not active AND minimap generation is complete
        if (!IsLoadingActive() && _minimapsPreGenerated)
        {
            // Show the UI elements if they were hidden during loading
            ShowUIElements();
            HandleGameStarted();
        }
        else
        {
        }
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

    }

    private void BuildPlanetDropdown()
    {
        if (planetDropdown == null || _gameManager == null) return;

        planetDropdown.ClearOptions();
        var options = new List<TMP_Dropdown.OptionData>();

        // Discover actual planet indices available
        var pd = _gameManager.GetPlanetData();
        var indices = new List<int>();

        // Prefer planet data count if available
        if (pd != null && pd.Count > 0)
        {
            foreach (var kv in pd)
            {
                // Only include indices that have a generator
                if (_gameManager.GetPlanetGenerator(kv.Key) != null)
                    indices.Add(kv.Key);
            }
        }

        // Fallback: probe generators up to maxPlanets
        if (indices.Count == 0 && _gameManager != null)
        {
            for (int i = 0; i < _gameManager.maxPlanets; i++)
            {
                if (_gameManager.GetPlanetGenerator(i) != null)
                    indices.Add(i);
            }
        }

        // Final fallback: if there is a legacy single planet generator, include index 0
        if (indices.Count == 0 && _gameManager != null && _gameManager.planetGenerator != null)
            indices.Add(0);

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
        if (_gameManager != null)
            _gameManager.SetCurrentPlanet(planetIndex);

        ShowMinimapForPlanet(planetIndex);
    }

    private void ShowMinimapForPlanet(int planetIndex)
    {
        // OPTIMIZATION: Try to reuse flat map texture first (if enabled and available)
        if (reuseFlatMapTexture)
        {
            // Use HexMapChunkManager (chunk-based map renderer)
            var chunkManager = FindAnyObjectByType<HexMapChunkManager>();
            
            Texture mapTexture = null;
            System.Func<int, int, Texture> getDownscaled = null;
            
            if (chunkManager != null && chunkManager.IsBuilt && chunkManager.MapTexture != null)
            {
                mapTexture = chunkManager.MapTexture;
                getDownscaled = (w, h) => chunkManager.GetDownscaledTexture(w, h);
            }
            
            if (mapTexture != null)
            {
                // Check if this is the current planet (flat map shows current planet)
                var currentPlanet = _gameManager != null ? _gameManager.GetCurrentPlanetGenerator() : null;
                var targetPlanet = _gameManager != null ? (_gameManager.GetPlanetGenerator(planetIndex) ?? _gameManager.planetGenerator) : null;
                
                if (currentPlanet == targetPlanet)
                {
                    // Reuse flat map texture - downscale if needed
                    Texture minimapTex = mapTexture;
                    
                    // If minimap resolution is different, downscale
                    if (minimapResolution.x != mapTexture.width || 
                        minimapResolution.y != mapTexture.height)
                    {
                        var downscaled = getDownscaled?.Invoke(minimapResolution.x, minimapResolution.y);
                        if (downscaled != null)
                        {
                            minimapTex = downscaled;
                            // Cache it for this planet
                            _minimapTextures[planetIndex] = minimapTex;
                        }
                    }
                    
                    if (minimapImage != null && minimapTex != null)
                    {
                        minimapImage.texture = minimapTex;
                        SetZoom(1f);
                        return; // Successfully reused flat map texture
                    }
                }
            }
        }
        
        // Fallback to pre-generated or on-demand generation
        if (_minimapsPreGenerated)
        {
            // Use pre-generated texture when present; otherwise, generate on-demand
                if (_minimapTextures.TryGetValue(planetIndex, out var tex) && tex != null)
                {
                    if (minimapImage != null)
                    {
                        // If the cached texture is a GPU RenderTexture, assign it directly to the RawImage
                        if (tex is RenderTexture rt)
                            minimapImage.texture = rt;
                        else
                            minimapImage.texture = tex;
                    }
                    SetZoom(1f);
                }
                else
                {
                    var generated = GenerateBodyMinimapImmediate(planetIndex, false);
                    if (generated != null && minimapImage != null) minimapImage.texture = generated;
                    SetZoom(1f);
            }
        }
        else
        {
            // Fallback to on-demand generation
                if (!_minimapTextures.TryGetValue(planetIndex, out var tex) || tex == null)
                    tex = GenerateBodyMinimapImmediate(planetIndex, false) as Texture;
                if (minimapImage != null && tex != null) minimapImage.texture = tex;
                SetZoom(1f);
        }
    }


    private Color GetDefaultBiomeColour(Biome biome)
    {
        // Prefer the MinimapColorProvider instance if assigned, otherwise fall back
        if (colorProvider != null)
            return colorProvider.ColorForBiome(biome);

        return BiomeColorHelper.GetMinimapColor(biome);
    }
    
    // Legacy method kept for backward compatibility (now delegates to BiomeColorHelper)
    private Color GetDefaultBiomeColourLegacy(Biome biome)
    {
        return biome switch
        {
            Biome.Ocean => new Color(0.2f, 0.4f, 0.8f, 1f),
            Biome.Forest => new Color(0.2f, 0.6f, 0.2f, 1f),
            Biome.Desert => new Color(0.8f, 0.7f, 0.3f, 1f),
            Biome.Mountain => new Color(0.6f, 0.5f, 0.4f, 1f),
            Biome.Plains => new Color(0.4f, 0.7f, 0.3f, 1f),
            Biome.Arctic => new Color(0.9f, 0.9f, 0.9f, 1f),
            Biome.Tundra => new Color(0.6f, 0.7f, 0.8f, 1f),
            Biome.Jungle => new Color(0.1f, 0.5f, 0.1f, 1f),
            Biome.Grassland => new Color(0.5f, 0.8f, 0.3f, 1f),
            Biome.Marsh => new Color(0.3f, 0.5f, 0.4f, 1f),
            Biome.Swamp => new Color(0.2f, 0.4f, 0.3f, 1f),
            Biome.Taiga => new Color(0.3f, 0.6f, 0.4f, 1f),
            Biome.Savannah => new Color(0.7f, 0.6f, 0.3f, 1f),
            Biome.Coast => new Color(0.4f, 0.6f, 0.8f, 1f),
            Biome.Volcanic => new Color(0.8f, 0.3f, 0.2f, 1f),
            Biome.Steamlands => new Color(0.7f, 0.7f, 0.8f, 1f),
            
            // Real Solar System Planet Biomes
            Biome.MartianRegolith => new Color(0.6f, 0.3f, 0.2f, 1f),
            Biome.MartianCanyon => new Color(0.5f, 0.2f, 0.1f, 1f),
            Biome.MartianPolarIce => new Color(0.8f, 0.8f, 0.9f, 1f),
            Biome.MartianDunes => new Color(0.7f, 0.4f, 0.2f, 1f),
            
            Biome.VenusLava => new Color(1.0f, 0.4f, 0.1f, 1f),
            Biome.VenusianPlains => new Color(0.7f, 0.5f, 0.3f, 1f),
            Biome.VenusHighlands => new Color(0.6f, 0.4f, 0.3f, 1f),
            
            Biome.MercuryPlains => new Color(0.5f, 0.5f, 0.5f, 1f),
            Biome.MercuryBasalt => new Color(0.4f, 0.4f, 0.4f, 1f),
            Biome.MercuryScarp => new Color(0.6f, 0.6f, 0.6f, 1f),
            Biome.MercurianIce => new Color(0.7f, 0.7f, 0.8f, 1f),
            
            Biome.JovianClouds => new Color(0.8f, 0.7f, 0.5f, 1f),
            Biome.JovianStorm => new Color(0.9f, 0.6f, 0.4f, 1f),
            Biome.SaturnRings => new Color(0.9f, 0.8f, 0.6f, 1f),
            Biome.SaturnSurface => new Color(0.8f, 0.7f, 0.5f, 1f),
            
            Biome.UranusIce => new Color(0.7f, 0.8f, 0.9f, 1f),
            Biome.UranusSurface => new Color(0.6f, 0.7f, 0.8f, 1f),
            Biome.NeptuneWinds => new Color(0.5f, 0.6f, 0.8f, 1f),
            Biome.NeptuneIce => new Color(0.6f, 0.7f, 0.9f, 1f),
            Biome.NeptuneSurface => new Color(0.4f, 0.5f, 0.7f, 1f),
            
            Biome.PlutoCryo => new Color(0.8f, 0.8f, 0.9f, 1f),
            Biome.PlutoTholins => new Color(0.7f, 0.6f, 0.5f, 1f),
            
            Biome.TitanLakes => new Color(0.3f, 0.4f, 0.6f, 1f),
            Biome.TitanDunes => new Color(0.6f, 0.5f, 0.4f, 1f),
            Biome.TitanIce => new Color(0.8f, 0.8f, 0.9f, 1f),
            
            Biome.EuropaIce => new Color(0.9f, 0.9f, 1.0f, 1f),
            Biome.EuropaRidges => new Color(0.8f, 0.8f, 0.9f, 1f),
            
            Biome.IoVolcanic => new Color(0.9f, 0.3f, 0.1f, 1f),
            Biome.IoSulfur => new Color(0.9f, 0.8f, 0.2f, 1f),
            
            Biome.MoonDunes => new Color(0.7f, 0.7f, 0.7f, 1f),
            Biome.MoonCraters => new Color(0.5f, 0.5f, 0.5f, 1f),
            
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
        // RawImage pixel size in local space
        var rawSize = minimapImage.rectTransform.rect.size;

        // Local (0,0) is rect center -> convert to 0..1
        float normX = (localPoint.x + rawSize.x * 0.5f) / rawSize.x;
        float normY = (localPoint.y + rawSize.y * 0.5f) / rawSize.y;

        // Un-mirror the click to match what is actually drawn
        if (_isHorizontallyMirrored) normX = 1f - normX;
        if (_isVerticallyMirrored)   normY = 1f - normY;

        normX = Mathf.Clamp01(normX);
        normY = Mathf.Clamp01(normY);

        // Apply current zoom/pan window
        var uvRect = minimapImage.uvRect;           // (x,y,width,height) in 0..1
        float worldU = uvRect.x + normX * uvRect.width;
        float worldV = uvRect.y + normY * uvRect.height;

        // Moons are separate planets now; always target the current planet's transform.
        var currentPlanetGen = _gameManager?.GetCurrentPlanetGenerator();
        var grid = currentPlanetGen != null ? currentPlanetGen.Grid : null;
        if (grid == null) return;

        float worldX = (worldU - 0.5f) * grid.MapWidth;
        float worldZ = (worldV - 0.5f) * grid.MapHeight;
        float yPlane = currentPlanetGen != null ? currentPlanetGen.transform.position.y : 0f;
        
        // Use HexMapChunkManager Y position
        var chunkManager = FindAnyObjectByType<HexMapChunkManager>();
        if (chunkManager != null && chunkManager.IsBuilt)
            yPlane = chunkManager.transform.position.y;
        
        Vector3 worldTarget = new Vector3(worldX, yPlane, worldZ);

        // Use cached reference to avoid expensive FindAnyObjectByType call
        if (_cachedCameraManager == null)
            _cachedCameraManager = FindAnyObjectByType<PlanetaryCameraManager>();
        if (_cachedCameraManager != null)
            _cachedCameraManager.JumpToWorldPoint(worldTarget);
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
        // Use cached reference to avoid expensive FindAnyObjectByType call
        if (_cachedCameraManager == null)
            _cachedCameraManager = FindAnyObjectByType<PlanetaryCameraManager>();
        if (_gameManager == null || _cachedCameraManager == null)
        {
            return;
        }

        // Multi-planet is always enabled at runtime; proceed if GameManager present.

        // Moons are separate planets now. Switch to Luna (if present).
        _gameManager.GoToEarthMoon();
        ShowMinimapForPlanet(_gameManager.currentPlanetIndex);
    }

    /// <summary>
    /// OnClick handler for the Main planet button. Switches to the configured main planet and ensures we're not on the moon.
    /// </summary>
    private void OnMainPlanetButtonClicked()
    {
        // Use cached reference to avoid expensive FindAnyObjectByType call
        if (_cachedCameraManager == null)
            _cachedCameraManager = FindAnyObjectByType<PlanetaryCameraManager>();
        if (_gameManager == null || _cachedCameraManager == null)
        {
            return;
        }

        int targetIndex = Mathf.Clamp(mainPlanetIndex, 0, Mathf.Max(0, _gameManager.maxPlanets - 1));
        // Multi-planet always enabled; set current planet directly
        _gameManager.SetCurrentPlanet(targetIndex);
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

        // Moons are separate planets now; indicator always tracks the current planet.
            var currentPlanetGen = _gameManager?.GetCurrentPlanetGenerator();
            if (currentPlanetGen == null) return;
        Vector3 bodyPosition = currentPlanetGen.transform.position;
        var grid = currentPlanetGen.Grid;
        if (grid == null) return;
        
        // Calculate camera position relative to planet center on the flat map
        Vector3 relativePos = camera.transform.position - bodyPosition;
        
        // Convert to UV coordinates (0-1)
        float u = (relativePos.x / grid.MapWidth) + 0.5f;
        float v = (relativePos.z / grid.MapHeight) + 0.5f;
        
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
        
    // Debug logging for position indicator removed
    }
}
