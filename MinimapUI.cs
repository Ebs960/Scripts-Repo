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
    [Tooltip("Optional text display showing current zoom level")]
    public TextMeshProUGUI zoomLevelText;
    [Tooltip("Position indicator (shows where camera is looking on minimap)")]
    public RectTransform positionIndicator;

    [Header("Minimap Settings")]
    [SerializeField] private Vector2Int minimapResolution = new Vector2Int(2048, 1024); // Reduced from 2048x1024 for performance
    [SerializeField] private MinimapColorProvider colorProvider;
    [SerializeField] private float maxZoom = 4f;
    [SerializeField] private float minZoom = 0.5f;
    [SerializeField] private float zoomSpeed = 1f;
    [SerializeField] private float buttonZoomStep = 0.5f;

    [Header("Pre-generation Settings")]
    [SerializeField] private bool preGenerateAllMinimaps = false;
    [SerializeField] private int minimapsPerFrame = 1;
    [SerializeField] private int pixelsPerFrame = 50000; // Process pixels in batches for better performance

    // Private fields
    private readonly Dictionary<int, Texture2D> _minimapTextures = new();
    private float _currentZoom = 1f;
    private Vector2 _panOffset = Vector2.zero; // For panning around the zoomed minimap
    private bool _isDragging = false;
    private Vector2 _lastDragPosition;
    private GameManager _gameManager;
    private bool _minimapsPreGenerated = false;
    private LoadingPanelController _loadingPanel;

    // Public property for GameManager to check
    public bool MinimapsPreGenerated => _minimapsPreGenerated;

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
    }

    void Update()
    {
        // Update position indicator every frame for smooth tracking
        UpdatePositionIndicator();
    }

    void Start()
    {
        if (preGenerateAllMinimaps)
        {
            StartCoroutine(PreGenerateAllMinimaps());
        }
        else
        {
            BuildPlanetDropdown();
            ShowMinimapForPlanet(_gameManager != null ? _gameManager.currentPlanetIndex : 0);
        }
    }

    /// <summary>
    /// Trigger minimap pre-generation (called by GameManager when planets are ready)
    /// </summary>
    public void StartMinimapGeneration()
    {
        StartCoroutine(PreGenerateAllMinimaps());
    }

    /// <summary>
    /// Pre-generate all minimaps during loading with progress updates
    /// </summary>
    public IEnumerator PreGenerateAllMinimaps()
    {
        if (_minimapsPreGenerated)
        {
            Debug.Log("[MinimapUI] Minimaps already pre-generated, skipping...");
            yield break;
        }

        Debug.Log("[MinimapUI] Starting minimap pre-generation...");
        _minimapsPreGenerated = false;

        // Clear existing textures
        ClearMinimapCache();

        int totalPlanets;
        if (_gameManager.enableMultiPlanetSystem)
        {
            totalPlanets = _gameManager.GetPlanetData().Count;
        }
        else
        {
            totalPlanets = _gameManager.planetGenerator != null ? 1 : 0;
        }

        if (totalPlanets == 0)
        {
            Debug.LogWarning("[MinimapUI] No planets found for minimap generation");
            _minimapsPreGenerated = true;
            yield break;
        }

        Debug.Log($"[MinimapUI] Generating {totalPlanets} minimaps...");

        for (int planetIndex = 0; planetIndex < totalPlanets; planetIndex++)
        {
            string planetName = _gameManager.enableMultiPlanetSystem
                ? _gameManager.GetPlanetData()[planetIndex].planetName
                : "Planet";

            Debug.Log($"[MinimapUI] Generating minimap for {planetName} (index {planetIndex})...");

            // Use coroutine-based generation for better performance
            yield return StartCoroutine(GenerateMinimapTextureCoroutine(planetIndex));

            // Update loading progress
            if (_loadingPanel != null)
            {
                float progress = (float)planetIndex / totalPlanets;
                _loadingPanel.SetProgress(progress);
                _loadingPanel.SetStatus($"Generated minimap for {planetName}...");
            }

            // Yield to prevent frame drops
            if (planetIndex % minimapsPerFrame == 0)
            {
                yield return null;
            }
        }

        Debug.Log("[MinimapUI] Minimap pre-generation complete!");
        _minimapsPreGenerated = true;

        // Build dropdown after all minimaps are ready
        BuildPlanetDropdown();

        // Show first planet's minimap
        if (totalPlanets > 0)
        {
            ShowMinimapForPlanet(0);
        }
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

        // Pre-calculate all tile colors to avoid repeated lookups
        var tileColourCache = new Dictionary<int, Color>();
        
        // Pre-populate tile color cache for all tiles
        for (int tileIndex = 0; tileIndex < grid.TileCount; tileIndex++)
        {
            var tileData = planetGen.GetHexTileData(tileIndex);
            
            // If that fails, try TileDataHelper
            if (tileData == null && TileDataHelper.Instance != null)
            {
                var (helperTileData, isMoon) = TileDataHelper.Instance.GetTileData(tileIndex);
                if (!isMoon && helperTileData != null)
                    tileData = helperTileData;
            }
            
            // If still null, try getting from the planet generator's data dictionary
            if (tileData == null && planetGen.data != null && planetGen.data.ContainsKey(tileIndex))
            {
                tileData = planetGen.data[tileIndex];
            }
            
            Color colour;
            if (tileData == null)
            {
                // Last resort: create basic land/ocean tile based on grid position
                colour = (tileIndex % 3 == 0) ? new Color(0.2f, 0.4f, 0.8f) : new Color(0.3f, 0.6f, 0.2f);
            }
            else
            {
                colour = (colorProvider != null)
                    ? colorProvider.ColorFor(tileData, Vector2.zero)
                    : GetDefaultBiomeColour(tileData.biome);
            }
            
            tileColourCache[tileIndex] = colour;
        }

        // Generate texture pixels in batches
        int pixelsProcessed = 0;
        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            float latRad = Mathf.PI * (0.5f - v);
            float sinLat = Mathf.Sin(latRad);
            float cosLat = Mathf.Cos(latRad);

            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                float lonRad = 2f * Mathf.PI * (u - 0.5f);
                float sinLon = Mathf.Sin(lonRad);
                float cosLon = Mathf.Cos(lonRad);

                // Unit direction in planet-local space (+Z forward, +X right, +Y up)
                Vector3 dir = new Vector3(sinLon * cosLat, sinLat, cosLon * cosLat);
                int tileIndex = grid.GetTileAtPosition(dir);
                
                Color colour;
                if (tileIndex < 0 || !tileColourCache.ContainsKey(tileIndex))
                {
                    colour = Color.magenta;
                }
                else
                {
                    colour = tileColourCache[tileIndex];
                }

                tex.SetPixel(x, y, colour);
                pixelsProcessed++;

                // Yield every batch of pixels to prevent frame drops
                if (pixelsProcessed % pixelsPerFrame == 0)
                {
                    yield return null;
                }
            }
        }

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
        _minimapsPreGenerated = false;
        Debug.Log("[MinimapUI] Minimap cache cleared. Restart generation to create new textures.");
    }

    private void BuildPlanetDropdown()
    {
        if (planetDropdown == null || _gameManager == null) return;

        planetDropdown.ClearOptions();
        var options = new List<TMP_Dropdown.OptionData>();

        int max = _gameManager.enableMultiPlanetSystem ? _gameManager.maxPlanets : 1;
        var pd = _gameManager.GetPlanetData();
        for (int i = 0; i < max; i++)
        {
            // Only add planets that have minimaps if pre-generation is enabled
            if (_minimapsPreGenerated && !_minimapTextures.ContainsKey(i))
                continue;

            string label = (pd != null && pd.TryGetValue(i, out var d) && !string.IsNullOrEmpty(d.planetName))
                ? d.planetName
                : $"Planet {i + 1}";
            options.Add(new TMP_Dropdown.OptionData(label));
        }

        planetDropdown.AddOptions(options);
        planetDropdown.value = _gameManager.currentPlanetIndex;
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
        if (_minimapsPreGenerated)
        {
            // Use pre-generated texture
            if (_minimapTextures.TryGetValue(planetIndex, out var tex) && tex != null)
            {
                if (minimapImage != null) minimapImage.texture = tex;
                SetZoom(1f);
                Debug.Log($"[MinimapUI] Showing pre-generated minimap for planet {planetIndex}");
            }
            else
            {
                Debug.LogWarning($"[MinimapUI] No pre-generated minimap found for planet {planetIndex}");
            }
        }
        else
        {
            // Fallback to on-demand generation
            if (!_minimapTextures.TryGetValue(planetIndex, out var tex) || tex == null)
            {
                tex = GenerateMinimapTexture(planetIndex);
                _minimapTextures[planetIndex] = tex;
            }

            if (minimapImage != null) minimapImage.texture = tex;
            SetZoom(1f);
        }
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

        // Pre-calculate all tile colors to avoid repeated lookups
        var tileColourCache = new Dictionary<int, Color>();
        var tileOffsetCache = new Dictionary<int, Vector2>();
        
        // Pre-populate tile color cache for all tiles
        for (int tileIndex = 0; tileIndex < grid.TileCount; tileIndex++)
        {
            var tileData = planetGen.GetHexTileData(tileIndex);
            
            // If that fails, try TileDataHelper
            if (tileData == null && TileDataHelper.Instance != null)
            {
                var (helperTileData, isMoon) = TileDataHelper.Instance.GetTileData(tileIndex);
                if (!isMoon && helperTileData != null)
                    tileData = helperTileData;
            }
            
            // If still null, try getting from the planet generator's data dictionary
            if (tileData == null && planetGen.data != null && planetGen.data.ContainsKey(tileIndex))
            {
                tileData = planetGen.data[tileIndex];
            }
            
            Color colour;
            if (tileData == null)
            {
                // Last resort: create basic land/ocean tile based on grid position
                colour = (tileIndex % 3 == 0) ? new Color(0.2f, 0.4f, 0.8f) : new Color(0.3f, 0.6f, 0.2f);
            }
            else
            {
                // Calculate offset for texture sampling
                int hash = tileIndex * 9781 + 7;
                float ox = ((hash >> 8) & 0xFF) / 255f;
                float oy = (hash & 0xFF) / 255f;
                var offset = new Vector2(ox, oy);
                tileOffsetCache[tileIndex] = offset;
                
                // Sample color with offset
                float sampleU = Mathf.Repeat(0.5f + offset.x, 1f);
                float sampleV = Mathf.Repeat(0.5f + offset.y, 1f);
                colour = (colorProvider != null)
                    ? colorProvider.ColorFor(tileData, new Vector2(sampleU, sampleV))
                    : GetDefaultBiomeColour(tileData.biome);
            }
            
            tileColourCache[tileIndex] = colour;
        }

        // Generate texture pixels in batches
        int pixelsProcessed = 0;
        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            float latRad = Mathf.PI * (0.5f - v);
            float sinLat = Mathf.Sin(latRad);
            float cosLat = Mathf.Cos(latRad);

            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                float lonRad = 2f * Mathf.PI * (u - 0.5f);
                float sinLon = Mathf.Sin(lonRad);
                float cosLon = Mathf.Cos(lonRad);

                // Unit direction in planet-local space (+Z forward, +X right, +Y up)
                Vector3 dir = new Vector3(sinLon * cosLat, sinLat, cosLon * cosLat);
                int tileIndex = grid.GetTileAtPosition(dir);
                
                Color colour;
                if (tileIndex < 0 || !tileColourCache.ContainsKey(tileIndex))
                {
                    colour = Color.magenta;
                }
                else
                {
                    colour = tileColourCache[tileIndex];
                }

                tex.SetPixel(x, y, colour);
                pixelsProcessed++;
            }
        }

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
        // Convert to normalized coordinates (0-1) relative to the RawImage
        Vector2 size = minimapImage.rectTransform.rect.size;
        float normX = (localPoint.x / size.x) + 0.5f;
        float normY = (localPoint.y / size.y) + 0.5f;
        
        // Clamp to valid range
        normX = Mathf.Clamp01(normX);
        normY = Mathf.Clamp01(normY);

        // Convert to UV coordinates considering the current zoom/pan
        var uvRect = minimapImage.uvRect;
        float worldU = uvRect.x + normX * uvRect.width;
        float worldV = uvRect.y + normY * uvRect.height;

        // Move camera to this location
        float lonRad = 2f * Mathf.PI * (worldU - 0.5f);
        float latRad = Mathf.PI * (0.5f - worldV);

        Vector3 dir = new Vector3(
            Mathf.Sin(lonRad) * Mathf.Cos(latRad),
            Mathf.Sin(latRad),
            Mathf.Cos(lonRad) * Mathf.Cos(latRad)).normalized;

        var camMgr = FindAnyObjectByType<PlanetaryCameraManager>();
        if (camMgr != null)
        {
            camMgr.JumpToDirection(dir, false);
            Debug.Log($"[MinimapUI] Jumping camera to direction: {dir}");
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
    /// Update the position indicator to show where the camera is positioned on the planet
    /// </summary>
    private void UpdatePositionIndicator()
    {
        if (positionIndicator == null || minimapImage == null) return;

        var camera = Camera.main;
        if (camera == null) return;

        // Get the current planet's position
        var currentPlanetGen = _gameManager?.GetCurrentPlanetGenerator();
        if (currentPlanetGen == null) return;

        Vector3 planetPosition = currentPlanetGen.transform.position;
        
        // Calculate camera position relative to planet center
        Vector3 relativePos = camera.transform.position - planetPosition;
        
        // Normalize to get direction from planet center
        Vector3 direction = relativePos.normalized;
        
        // Convert to spherical coordinates (latitude/longitude)
        float lat = Mathf.Asin(direction.y); // Latitude (-π/2 to π/2)
        float lon = Mathf.Atan2(direction.x, direction.z); // Longitude (-π to π)
        
        // Convert to UV coordinates (0-1)
        // Longitude: -π to π -> 0 to 1 (0 = left edge, 1 = right edge)
        float u = (lon + Mathf.PI) / (2f * Mathf.PI);
        
        // Latitude: -π/2 to π/2 -> 1 to 0 (1 = top edge, 0 = bottom edge)
        float v = 1f - ((lat + Mathf.PI * 0.5f) / Mathf.PI);
        
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
    }
}
