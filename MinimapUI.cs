using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;

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
    [BurstCompile]
    private struct LUTJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Vector3> tileCenters;
        [ReadOnly] public NativeArray<float> sinLatArr;
        [ReadOnly] public NativeArray<float> cosLatArr;
        [ReadOnly] public NativeArray<float> sinLonArr;
        [ReadOnly] public NativeArray<float> cosLonArr;
        [ReadOnly] public int width;
        [ReadOnly] public int height;
        [WriteOnly] public NativeArray<int> lut;

        public void Execute(int index)
        {
            int y = index / width;
            int x = index % width;
            float sinLat = sinLatArr[y];
            float cosLat = cosLatArr[y];
            float sinLon = sinLonArr[x];
            float cosLon = cosLonArr[x];
            Vector3 dir = new Vector3(sinLon * cosLat, sinLat, cosLon * cosLat).normalized;
            float maxDot = -2f;
            int bestIdx = -1;
            for (int i = 0; i < tileCenters.Length; i++)
            {
                float d = Vector3.Dot(dir, tileCenters[i].normalized);
                if (d > maxDot)
                {
                    maxDot = d;
                    bestIdx = i;
                }
            }
            lut[index] = bestIdx;
        }
    }
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
    [SerializeField] private bool preGenerateAllMinimaps = true;
    [SerializeField] private int maxPixelsPerFrameBatch = 50000; // Process pixels in batches for better performance
    [Header("Atlas Settings")]
    [Tooltip("When enabled, build a per-tile color atlas (array) and use it as a fast lookup during minimap generation.")]
    [SerializeField] private bool useTileColorAtlas = true;
    [Tooltip("Optional compute shader to accelerate minimap generation on GPU (uses LUT + tile atlas texture).")]
    [SerializeField] private ComputeShader minimapComputeShader;
    [Tooltip("When true, require a compute shader for atlas-only generation; do not run CPU per-pixel fallbacks.")]
    [SerializeField] private bool requireGPUForAtlas = true;

    // Private fields
    // Planet and Moon minimap caches
    private readonly Dictionary<int, Texture> _minimapTextures = new();
    private readonly Dictionary<int, Texture> _moonMinimapTextures = new();
    // Fast path: per-body (planet or moon) LUT cache mapping pixel -> tile index
    // Key: P{planetIndex}_M{0|1}_W{w}_H{h}
    private static readonly Dictionary<string, int[]> _bodyIndexLUT = new();
    // Optional per-body tile color atlas cache. Key format matches LUT keys but without resolution.
    private static readonly Dictionary<string, Color32[]> _tileAtlasCache = new();
    // GPU resources cache (per-resolution key)
    private static readonly Dictionary<string, ComputeBuffer> _lutComputeBufferCache = new();
    private static readonly Dictionary<string, RenderTexture> _gpuResultCache = new();
    private static readonly Dictionary<string, Texture2D> _gpuAtlasTextureCache = new();
    private static readonly Dictionary<string, byte[]> _cpuRawBufferCache = new();
    // NOTE: CPU per-pixel fallbacks were removed - this class now requires the compute shader + atlas LUT path
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

    // Reuse a raw byte[] sized for RGBA32 uploads (used by GPU atlas uploads)
    private byte[] GetOrCreateRawBuffer(int width, int height)
    {
        string key = $"{width}x{height}-raw";
        if (!_cpuRawBufferCache.TryGetValue(key, out var buf))
        {
            buf = new byte[width * height * 4];
            _cpuRawBufferCache[key] = buf;
        }
        return buf;
    }
    // Build or fetch a LUT mapping each minimap pixel to a tile index for a specific body
    private int[] EnsureIndexLUTForBody(int planetIndex, bool isMoon, SphericalHexGrid grid, int width, int height)
    {
        if (grid == null) return null;
        string key = $"P{planetIndex}_M{(isMoon ? 1 : 0)}_W{width}_H{height}";
        if (_bodyIndexLUT.TryGetValue(key, out var cached)) return cached;

        var lut = new int[width * height];

        // Precompute trig
        var sinLatArr = new NativeArray<float>(height, Allocator.TempJob);
        var cosLatArr = new NativeArray<float>(height, Allocator.TempJob);
        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            float latRad = Mathf.PI * (0.5f - v);
            sinLatArr[y] = Mathf.Sin(latRad);
            cosLatArr[y] = Mathf.Cos(latRad);
        }
        var sinLonArr = new NativeArray<float>(width, Allocator.TempJob);
        var cosLonArr = new NativeArray<float>(width, Allocator.TempJob);
        for (int x = 0; x < width; x++)
        {
            float u = (x + 0.5f) / width;
            float lonRad = 2f * Mathf.PI * (u - 0.5f);
            sinLonArr[x] = Mathf.Sin(lonRad);
            cosLonArr[x] = Mathf.Cos(lonRad);
        }

        var tileCenters = new NativeArray<Vector3>(grid.tileCenters.Length, Allocator.TempJob);
        for (int i = 0; i < grid.tileCenters.Length; i++)
        {
            tileCenters[i] = grid.tileCenters[i];
        }
        var lutNative = new NativeArray<int>(lut.Length, Allocator.TempJob);

        var job = new LUTJob
        {
            tileCenters = tileCenters,
            sinLatArr = sinLatArr,
            cosLatArr = cosLatArr,
            sinLonArr = sinLonArr,
            cosLonArr = cosLonArr,
            width = width,
            height = height,
            lut = lutNative
        };

        JobHandle handle = job.Schedule(lut.Length, 64);
        handle.Complete();

        lutNative.CopyTo(lut);

        sinLatArr.Dispose();
        cosLatArr.Dispose();
        sinLonArr.Dispose();
        cosLonArr.Dispose();
        tileCenters.Dispose();
        lutNative.Dispose();

        _bodyIndexLUT[key] = lut;
        return lut;
    }
        // Build or fetch a compact per-tile color atlas for a body (planet or moon).
    // Atlas layout: square texture array flattened to Color32[] where index -> tileIndex mapping is stored in parallel by tile order.
    private Color32[] EnsureTileColorAtlas(int planetIndex, bool isMoon, SphericalHexGrid grid)
    {
        if (!useTileColorAtlas || grid == null) return null;
        string key = $"P{planetIndex}_M{(isMoon ? 1 : 0)}";
        if (_tileAtlasCache.TryGetValue(key, out var cached)) return cached;

        int tileCount = grid.TileCount;
        var atlas = new Color32[tileCount];

        for (int i = 0; i < tileCount; i++)
        {
                var tileData = isMoon ? _gameManager.GetMoonGenerator(planetIndex)?.GetHexTileData(i) : _gameManager.GetPlanetGenerator(planetIndex)?.GetHexTileData(i);
            if (tileData == null && TileDataHelper.Instance != null)
            {
                var (helperTileData, helperIsMoon) = TileDataHelper.Instance.GetTileDataFromPlanet(i, planetIndex);
                if (!helperIsMoon && helperTileData != null) tileData = helperTileData;
            }

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
    Debug.Log($"[MinimapUI] Built tile atlas for {key} ({tileCount} tiles)");
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
            UnityEngine.Object.DestroyImmediate(existing);
            _gpuAtlasTextureCache.Remove(cacheKey);
        }
        var tex = new Texture2D(w, 1, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point
        };
    // Fast upload using raw buffer to avoid temporary GC from SetPixels32
    var raw = GetOrCreateRawBuffer(w, 1);
    Buffer.BlockCopy(atlas, 0, raw, 0, w * 4);
    tex.LoadRawTextureData(raw);
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
                filterMode = FilterMode.Point
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

        Debug.Log($"[MinimapUI] GPU minimap generated for {key} (atlas {atlas.Length})");

        return rt;
    }

    void Awake()
    {
        _gameManager = GameManager.Instance;
        _loadingPanel = FindAnyObjectByType<LoadingPanelController>();

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
        if (IsLoadingActive() || !_minimapsPreGenerated)
        {
            HideUIElements();
        }

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
        if (_minimapsPreGenerated)
        {
            yield break;
        }
        _minimapsPreGenerated = false;

        // Clear existing textures
        ClearMinimapCache();

        

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
                    var gen = _gameManager.GetPlanetGenerator(i);
                    if (gen != null)
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
            _minimapsPreGenerated = false;
            yield break;
        }


        // Generate minimaps with smaller batches and more frequent yields
        for (int planetIndex = 0; planetIndex < totalPlanets; planetIndex++)
        {
            string planetName = GetPlanetName(planetIndex);

            // Generate planet minimap with optimizations (GPU-only path)
            yield return StartCoroutine(GenerateMinimapTextureOptimized(planetIndex, minimapResolution));

                // Generate moon minimap if exists (use current moon generator API)
                var moonGen = _gameManager.GetMoonGenerator(planetIndex);
                if (moonGen != null && moonGen.Grid != null && moonGen.Grid.TileCount > 0)
                {
                    // Moon minimap generation is handled by the same optimized GPU path
                    yield return StartCoroutine(GenerateMinimapTextureOptimized(planetIndex, minimapResolution, true));
                }

            // Update loading progress more frequently
            if (_loadingPanel != null)
            {
                float progress = 0.8f + (0.1f * (float)(planetIndex + 1) / totalPlanets); // Use 0.8-0.9 range for minimap generation
                _loadingPanel.SetProgress(progress);
                _loadingPanel.SetStatus($"Generated minimap for {planetName}...{(moonGen != null ? " (and moon)" : string.Empty)}");
            }


            // Minimap generation now uses the optimized LUT + Atlas + ComputeShader GPU path only.
        }

        // Mark completed and finish UI setup
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

    /// <summary>
    /// Optimized minimap generation with batching using LUT + atlas + compute shader.
    /// </summary>
    private IEnumerator GenerateMinimapTextureOptimized(int planetIndex, Vector2Int resolution, bool isMoon = false)
    {
        var planetGen = _gameManager.enableMultiPlanetSystem
            ? _gameManager.GetPlanetGenerator(planetIndex)
            : _gameManager.planetGenerator;

        if (planetGen == null) yield break;

        // Decide target generator (moon if requested)
        var targetGen = isMoon ? (_gameManager.GetMoonGenerator(planetIndex) ?? planetGen) : planetGen;
        var grid = targetGen?.Grid;
        if (grid == null) yield break;

        int width = resolution.x;
        int height = resolution.y;

        var lut = EnsureIndexLUTForBody(planetIndex, isMoon, grid, width, height);
        if (lut == null) yield break;

        var tileAtlas = EnsureTileColorAtlas(planetIndex, isMoon, grid);
        if (minimapComputeShader == null || tileAtlas == null)
        {
            Debug.LogWarning($"[MinimapUI] GPU path unavailable for planet {planetIndex} (compute shader or atlas missing). Skipping minimap generation.");
            yield break;
        }

        var gpuRT = RunComputeMinimap(planetIndex, isMoon, width, height, lut, tileAtlas);
        if (gpuRT != null)
        {
            Debug.Log($"[MinimapUI] Using GPU path for {(isMoon?"moon":"planet")} {planetIndex}");
            if (isMoon) _moonMinimapTextures[planetIndex] = gpuRT;
            else _minimapTextures[planetIndex] = gpuRT;
        }
        yield break;
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
                UnityEngine.Object.DestroyImmediate(rt);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }
        _minimapTextures.Clear();
        foreach (var tex in _moonMinimapTextures.Values)
        {
            if (tex == null) continue;
            if (tex is RenderTexture rt)
            {
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }
        _moonMinimapTextures.Clear();
        _minimapsPreGenerated = false;
    // Clear per-body tile atlas cache as it depends on tile data
    _tileAtlasCache.Clear();
        // Release GPU resources
        foreach (var kv in _lutComputeBufferCache)
        {
            kv.Value?.Release();
        }
        _lutComputeBufferCache.Clear();
        foreach (var kv in _gpuResultCache)
        {
            if (kv.Value != null)
            {
                kv.Value.Release();
                UnityEngine.Object.DestroyImmediate(kv.Value);
            }
        }
        _gpuResultCache.Clear();
        foreach (var kv in _gpuAtlasTextureCache)
        {
            if (kv.Value != null) UnityEngine.Object.DestroyImmediate(kv.Value);
        }
        _gpuAtlasTextureCache.Clear();
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
        // Check if minimaps are ready and UI setup is needed
        if (_minimapsPreGenerated)
        {
            // Initialize UI elements that were deferred from Start()
            BuildPlanetDropdown();
            ShowMinimapForPlanet(_gameManager != null ? _gameManager.currentPlanetIndex : 0);
        }
        else
        {
            // UI setup will happen automatically when PreGenerateAllMinimaps() completes
        }
    }
    
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

    private string GetPlanetName(int planetIndex)
    {
        if (_gameManager == null) return "Planet";
        if (_gameManager.enableMultiPlanetSystem)
        {
            var planetData = _gameManager.GetPlanetData();
            if (planetData != null && planetData.TryGetValue(planetIndex, out var pd))
            {
                return pd.planetName ?? "Planet";
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

        if (_minimapsPreGenerated)
        {
            // Use pre-generated texture when present; otherwise, generate on-demand
            if (!isOnMoon)
            {
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
                    if (minimapImage != null)
                    {
                        if (mtex is RenderTexture mrt)
                            minimapImage.texture = mrt;
                        else
                            minimapImage.texture = mtex;
                    }
                    SetZoom(1f);
                }
                else
                {
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

                if (minimapImage != null)
                {
                    if (tex is RenderTexture rt) minimapImage.texture = rt;
                    else minimapImage.texture = tex;
                }
                SetZoom(1f);
            }
            else
            {
                if (!_moonMinimapTextures.TryGetValue(planetIndex, out var mtex) || mtex == null)
                {
                    mtex = GenerateMoonMinimapTexture(planetIndex);
                    _moonMinimapTextures[planetIndex] = mtex;
                }

                if (minimapImage != null)
                {
                    if (mtex is RenderTexture mrt) minimapImage.texture = mrt;
                    else minimapImage.texture = mtex;
                }
                SetZoom(1f);
            }
        }
    }

    private Texture2D GenerateMoonMinimapTexture(int planetIndex)
    {
    Debug.Log($"[MinimapUI] CPU GenerateMoonMinimapTexture called for planet {planetIndex}");
        if (_gameManager == null)
        {
            return null;
        }

        var moonGen = _gameManager.GetMoonGenerator(planetIndex);
        if (moonGen == null)
        {
            return null;
        }

        var grid = moonGen.Grid;
        if (grid == null)
        {
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

    var raw = GetOrCreateRawBuffer(width, height);
    Buffer.BlockCopy(pixels, 0, raw, 0, width * height * 4);
    tex.LoadRawTextureData(raw);
    tex.Apply();
    return tex;
    }

    private Texture2D GenerateMinimapTexture(int planetIndex)
    {
    Debug.Log($"[MinimapUI] CPU GenerateMinimapTexture called for planet {planetIndex}");
        if (_gameManager == null)
        {
            return null;
        }

        var planetGen = _gameManager.enableMultiPlanetSystem
            ? _gameManager.GetPlanetGenerator(planetIndex)
            : _gameManager.planetGenerator;

        if (planetGen == null)
        {
            return null;
        }

        var grid = planetGen.Grid;
        if (grid == null)
        {
            return null;
        }

    // Debug info removed

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

    var raw = GetOrCreateRawBuffer(width, height);
        Buffer.BlockCopy(pixels, 0, raw, 0, width * height * 4);
        tex.LoadRawTextureData(raw);
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
            
            Biome.VenusLava => new Color(1.0f, 0.4f, 0.1f, 1f),
            Biome.VenusianPlains => new Color(0.7f, 0.5f, 0.3f, 1f),
            Biome.VenusHighlands => new Color(0.6f, 0.4f, 0.3f, 1f),
            
            Biome.MercuryCraters => new Color(0.5f, 0.5f, 0.5f, 1f),
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

    // Some pipelines rotate the minimap content by 180°. Compensate by shifting U by 0.5.
    float adjU = Mathf.Repeat(worldU + 0.5f, 1f);

    // Inverse of position indicator mapping
        // Indicator uses: u = 1 - (lon + PI) / (2PI)  =>  lon = 2PI * (1 - u) - PI
        //                  v = 0.5 + (lat / PI)      =>  lat = (v - 0.5) * PI
    float lonRad = 2f * Mathf.PI * (1f - adjU) - Mathf.PI;
        float latRad = (worldV - 0.5f) * Mathf.PI;

        // Equirectangular to local direction
        Vector3 localDir = new Vector3(
            Mathf.Sin(lonRad) * Mathf.Cos(latRad),
            Mathf.Sin(latRad),
            Mathf.Cos(lonRad) * Mathf.Cos(latRad)
        ).normalized;

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

        if (camMgr != null)
        {
            // Pass whether we are targeting the moon so the camera orbits the right body
            camMgr.JumpToDirection(worldDir, isOnMoon);
        }
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
            return;
        }

        int planetIndex = Mathf.Clamp(_gameManager.currentPlanetIndex, 0, Mathf.Max(0, _gameManager.maxPlanets - 1));
        var moonGen = _gameManager.GetMoonGenerator(planetIndex);
        if (moonGen == null || moonGen.Grid == null || moonGen.Grid.TileCount == 0)
        {
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
        
    // Debug logging for position indicator removed
    }
}
