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
    [Tooltip("Resolution of the minimap texture (width x height)")]
    public Vector2Int minimapResolution = new Vector2Int(2048, 1024); // Increased for better quality
    [Tooltip("Color/texture provider for tiles")]
    public MinimapColorProvider colorProvider;
    [Tooltip("Maximum zoom level (1 = full map, higher = zoomed in)")]
    public float maxZoom = 4f;
    [Tooltip("Minimum zoom level")]
    public float minZoom = 1f;
    [Tooltip("Zoom speed multiplier")]
    public float zoomSpeed = 1f;
    [Tooltip("Zoom step for button clicks (how much to zoom per button press)")]
    public float buttonZoomStep = 0.5f;

    [Header("Pre-generation Settings")]
    [Tooltip("Whether to pre-generate all minimaps at start (set to false to trigger from GameManager)")]
    public bool preGenerateAllMinimaps = false;
    [Tooltip("Number of minimaps to generate per frame (higher = faster but may cause frame drops)")]
    public int minimapsPerFrame = 3;
    
    [Header("Performance Settings")]
    [Tooltip("Enable performance optimizations (reduces quality but speeds up generation)")]
    public bool enablePerformanceMode = true;
    
    [Tooltip("Skip every N pixels when in performance mode (higher = faster but lower quality)")]
    [Range(1, 4)]
    public int pixelSkipRate = 2;

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
        // Update position indicator regularly
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
        if (_gameManager == null)
        {
            Debug.LogError("[MinimapUI] No GameManager instance found for pre-generation.");
            yield break;
        }

        // Clear any existing textures
        foreach (var tex in _minimapTextures.Values)
        {
            if (tex != null) DestroyImmediate(tex);
        }
        _minimapTextures.Clear();
        _minimapsPreGenerated = false;

        int totalPlanets;
        if (_gameManager.enableMultiPlanetSystem)
        {
            totalPlanets = _gameManager.GetPlanetData().Count;
        }
        else
        {
            totalPlanets = _gameManager.planetGenerator != null ? 1 : 0;
        }

        Debug.Log($"[MinimapUI] Pre-generating {totalPlanets} minimaps...");

        int generated = 0;
        var planetData = _gameManager.GetPlanetData();

        for (int planetIndex = 0; planetIndex < totalPlanets; planetIndex++)
        {
            // Get planet name for logging
            string planetName = "Unknown";
            if (planetData != null && planetData.TryGetValue(planetIndex, out var pd))
            {
                planetName = pd.planetName;
            }

            Debug.Log($"[MinimapUI] Generating minimap for {planetName} ({generated + 1}/{totalPlanets})");

            // Generate the minimap texture
            var texture = GenerateMinimapTexture(planetIndex);
            if (texture != null)
            {
                _minimapTextures[planetIndex] = texture;
                Debug.Log($"[MinimapUI] Minimap generated for {planetName} ({texture.width}x{texture.height})");
            }
            else
            {
                Debug.LogError($"[MinimapUI] Failed to generate minimap for {planetName}");
            }

            generated++;

            // Update loading panel progress
            if (_loadingPanel != null)
            {
                float progress = (float)generated / totalPlanets;
                _loadingPanel.SetProgress(progress);
                _loadingPanel.SetStatus($"Generating minimaps... ({generated}/{totalPlanets})");
            }

            // Yield every N minimaps to prevent frame drops
            if (generated % minimapsPerFrame == 0)
            {
                yield return null;
            }
        }

        _minimapsPreGenerated = true;
        
        Debug.Log($"[MinimapUI] Pre-generation complete! Generated {_minimapTextures.Count} minimaps");

        // Now build the UI
        BuildPlanetDropdown();
        ShowMinimapForPlanet(_gameManager.currentPlanetIndex);

        // Update loading panel to completion
        if (_loadingPanel != null)
        {
            _loadingPanel.SetProgress(1.0f);
            _loadingPanel.SetStatus("Minimaps ready!");
        }
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

        // Use performance mode resolution if enabled
        int width = enablePerformanceMode ? minimapResolution.x / 2 : minimapResolution.x;
        int height = enablePerformanceMode ? minimapResolution.y / 2 : minimapResolution.y;
        
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        // Pre-cache all tile data for better performance
        var tileDataCache = new Dictionary<int, HexTileData>();
        if (planetGen.data != null)
        {
            foreach (var kvp in planetGen.data)
            {
                tileDataCache[kvp.Key] = kvp.Value;
            }
        }

        var tileColourCache = new Dictionary<int, Color>();
        var biomeColorCache = new Dictionary<Biome, Color>();

        for (int y = 0; y < height; y += enablePerformanceMode ? pixelSkipRate : 1)
        {
            // Fix upside-down issue: flip the V coordinate
            float v = 1f - ((y + 0.5f) / height); // This flips the image right-side up
            float latRad = Mathf.PI * (0.5f - v);
            float sinLat = Mathf.Sin(latRad);
            float cosLat = Mathf.Cos(latRad);

            for (int x = 0; x < width; x += enablePerformanceMode ? pixelSkipRate : 1)
            {
                float u = (x + 0.5f) / width;
                float lonRad = 2f * Mathf.PI * (u - 0.5f);
                float sinLon = Mathf.Sin(lonRad);
                float cosLon = Mathf.Cos(lonRad);

                // Unit direction in planet-local space (+Z forward, +X right, +Y up)
                Vector3 dir = new Vector3(sinLon * cosLat, sinLat, cosLon * cosLat);
                int tileIndex = grid.GetTileAtPosition(dir);
                if (tileIndex < 0)
                {
                    // Fill multiple pixels if using skip rate
                    if (enablePerformanceMode && pixelSkipRate > 1)
                    {
                        for (int dy = 0; dy < pixelSkipRate && y + dy < height; dy++)
                        {
                            for (int dx = 0; dx < pixelSkipRate && x + dx < width; dx++)
                            {
                                tex.SetPixel(x + dx, y + dy, Color.magenta);
                            }
                        }
                    }
                    else
                    {
                        tex.SetPixel(x, y, Color.magenta);
                    }
                    continue;
                }

                Color color;
                if (!tileColourCache.TryGetValue(tileIndex, out color))
                {
                    // Try to get tile data from cached data first (much faster)
                    if (tileDataCache.TryGetValue(tileIndex, out var cachedTileData))
                    {
                        // Cache biome colors for better performance
                        if (!biomeColorCache.TryGetValue(cachedTileData.biome, out color))
                        {
                            color = GetDefaultBiomeColour(cachedTileData.biome);
                            biomeColorCache[cachedTileData.biome] = color;
                        }
                    }
                    else
                    {
                        // Fallback: try other sources
                        var fallbackTileData = planetGen.GetHexTileData(tileIndex);
                        if (fallbackTileData == null && TileDataHelper.Instance != null)
                        {
                            var (helperTileData, isMoon) = TileDataHelper.Instance.GetTileData(tileIndex);
                            if (!isMoon && helperTileData != null)
                                fallbackTileData = helperTileData;
                        }
                        
                        if (fallbackTileData != null)
                        {
                            if (!biomeColorCache.TryGetValue(fallbackTileData.biome, out color))
                            {
                                color = GetDefaultBiomeColour(fallbackTileData.biome);
                                biomeColorCache[fallbackTileData.biome] = color;
                            }
                        }
                        else
                        {
                            color = (tileIndex % 3 == 0) ? new Color(0.2f, 0.4f, 0.8f) : new Color(0.3f, 0.6f, 0.2f);
                        }
                    }

                    tileColourCache[tileIndex] = color;
                }

                // Fill multiple pixels if using skip rate
                if (enablePerformanceMode && pixelSkipRate > 1)
                {
                    for (int dy = 0; dy < pixelSkipRate && y + dy < height; dy++)
                    {
                        for (int dx = 0; dx < pixelSkipRate && x + dx < width; dx++)
                        {
                            tex.SetPixel(x + dx, y + dy, color);
                        }
                    }
                }
                else
                {
                    tex.SetPixel(x, y, color);
                }
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

        var camMgr = FindAnyObjectByType<PlanetaryCameraManager>();
        if (camMgr == null) return;

        // Get camera position relative to the current planet
        var camera = Camera.main;
        if (camera == null) return;

        // Get the current planet's position and radius
        var currentPlanetGen = _gameManager?.GetCurrentPlanetGenerator();
        if (currentPlanetGen == null) return;

        Vector3 planetPosition = currentPlanetGen.transform.position;
        float planetRadius = currentPlanetGen.radius;
        
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
        
        // Check if position is visible in current zoomed view
        var uvRect = minimapImage.uvRect;
        if (u >= uvRect.x && u <= uvRect.x + uvRect.width &&
            v >= uvRect.y && v <= uvRect.y + uvRect.height)
        {
            // Position is visible in current view
            float normalizedX = (u - uvRect.x) / uvRect.width;
            float normalizedY = (v - uvRect.y) / uvRect.height;
            
            // Convert to local position on minimap
            var rect = minimapImage.rectTransform.rect;
            Vector2 localPos = new Vector2(
                (normalizedX - 0.5f) * rect.width,
                (normalizedY - 0.5f) * rect.height
            );
            
            positionIndicator.anchoredPosition = localPos;
            positionIndicator.gameObject.SetActive(true);
        }
        else
        {
            // Position is outside current view
            positionIndicator.gameObject.SetActive(false);
        }
    }
}
