using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public enum MinimapTarget
{
    Planet,
    Moon,
    PlanetByIndex  // For multi-planet system
}

[RequireComponent(typeof(RawImage))]
public class MinimapController : MonoBehaviour, IPointerClickHandler
{
    [Header("References")]
    public MinimapGenerator planetGenerator;
    public MinimapGenerator moonGenerator;
    public RawImage minimapImage; // auto-filled in Awake
    public Image marker;  // Camera marker image that moves around on the minimap
    public Transform planetRoot;  // center of the planet
    public Transform moonRoot;    // center of the moon
    public Camera mainCamera;
    public PlanetaryCameraManager cameraManager; // your existing manager; hook via Inspector

    [Header("Multi-Planet Support")]
    [Tooltip("Dictionary of minimap generators for each planet (for multi-planet system)")]
    public Dictionary<int, MinimapGenerator> planetGenerators = new Dictionary<int, MinimapGenerator>();
    [Tooltip("Dictionary of planet root transforms (for multi-planet system)")]
    public Dictionary<int, Transform> planetRoots = new Dictionary<int, Transform>();
    [Tooltip("Current planet index for multi-planet system")]
    public int currentPlanetIndex = 0;
    
    [Header("UI Controls")]
    public Button planetButton;   // Button to switch to planet minimap
    public Button moonButton;     // Button to switch to moon minimap
    public Button zoomInButton;   // Button to zoom in
    public Button zoomOutButton;  // Button to zoom out
    public TMP_Dropdown planetDropdown; // Dropdown to select planet directly
    public TextMeshProUGUI currentTargetLabel; // Optional label showing current target
    public TextMeshProUGUI planetNameLabel; // Label showing current planet name
    
    [Header("Options")]
    public bool buildOnStart = true;
    public float zoomStep = 0.5f; // minimap zoom step (how much to zoom in/out)
    public float clickLerpSeconds = 0.35f; // smooth camera retarget
    public float minZoomLevel = 0.25f; // minimum zoom level (zoomed out - shows more area)
    public float maxZoomLevel = 4.0f; // maximum zoom level (zoomed in - shows less area, more detail)
    
    [Header("Current Target")]
    public MinimapTarget currentTarget = MinimapTarget.Planet;
    
    private float _currentZoomLevel = 1.0f; // 1.0 = normal view, higher = zoomed in, lower = zoomed out
    private Vector3 _lastCameraPosition;
    private float _minimapRefreshCooldown = 0.038f; // Much faster refresh now that it's optimized!
    private float _lastRefreshTime;
    private float _lastTextureCheckTime; // Performance: Only check texture assignment periodically

    private RectTransform _rt;

    void Awake()
    {
        if (!minimapImage) minimapImage = GetComponent<RawImage>();
        _rt = minimapImage.rectTransform;
        
        // Wire up button events
        if (planetButton) planetButton.onClick.AddListener(() => SwitchToTarget(MinimapTarget.Planet));
        if (moonButton) moonButton.onClick.AddListener(() => SwitchToTarget(MinimapTarget.Moon));
        if (zoomInButton) zoomInButton.onClick.AddListener(OnZoomInButton);
        if (zoomOutButton) zoomOutButton.onClick.AddListener(OnZoomOutButton);
        if (planetDropdown) planetDropdown.onValueChanged.AddListener(OnPlanetDropdownChanged);
    }

    void Start()
    {
        // Don't build minimaps here - they'll be built by GameManager after world generation
        // Just set initial target
        SwitchToTarget(currentTarget);
        
        // Update initial button states
        UpdateZoomButtonStates();
    }

    void Update()
    {
        // PERFORMANCE FIX: Only check texture assignment once per second
        if (Time.time - _lastTextureCheckTime > 0.0001f)
        {
            var currentGenerator = GetCurrentGenerator();
            if (currentGenerator && currentGenerator.IsReady && minimapImage.texture == null)
                minimapImage.texture = currentGenerator.minimapTexture;
            _lastTextureCheckTime = Time.time;
        }

        UpdateMarker();
        
        // PERFORMANCE FIX: Improved camera movement detection and throttling
        if (_currentZoomLevel != 1.0f && mainCamera != null)
        {
            var currentGenerator = GetCurrentGenerator();
            if (currentGenerator != null)
            {
                float distanceMoved = Vector3.Distance(mainCamera.transform.position, _lastCameraPosition);
                bool shouldRefresh = distanceMoved > 0.05f && (Time.time - _lastRefreshTime) > _minimapRefreshCooldown; // Increased threshold for better performance
                
                if (shouldRefresh)
                {
                    UpdateZoomCenter();
                    currentGenerator.Rebuild(); // Now much faster - just samples from master texture!
                    _lastCameraPosition = mainCamera.transform.position;
                    _lastRefreshTime = Time.time;
                }
                else if (distanceMoved > 0.01f)
                {
                    // Just update center without rebuilding for smooth marker movement
                    UpdateZoomCenterOnly();
                }
            }
        }
    }
    
    private void UpdateZoomCenterOnly()
    {
        var generator = GetCurrentGenerator();
        var currentRoot = GetCurrentRoot();
        if (generator != null && mainCamera != null && currentRoot != null)
        {
            // Set zoom center based on where the camera is looking (without rebuilding)
            Vector3 center = currentRoot.position;
            Vector3 camDirFromCenter = (mainCamera.transform.position - center).normalized;
            Vector3 surfacePoint = center + camDirFromCenter;
            
            // Only update the zoom center, don't rebuild yet
            generator.zoomCenter = surfacePoint;
        }
    }

    // --- UI Hooks (wire to your buttons) ---
    public void OnZoomInButton()  
    { 
        _currentZoomLevel = Mathf.Clamp(_currentZoomLevel + zoomStep, minZoomLevel, maxZoomLevel);
        RefreshMinimapTexture();
        UpdateZoomButtonStates();
    }
    
    public void OnZoomOutButton() 
    { 
        _currentZoomLevel = Mathf.Clamp(_currentZoomLevel - zoomStep, minZoomLevel, maxZoomLevel);
        RefreshMinimapTexture();
        UpdateZoomButtonStates();
    }
    

    
    public void OnPlanetDropdownChanged(int selectedIndex)
    {
        if (GameManager.Instance != null && GameManager.Instance.enableMultiPlanetSystem && planetDropdown != null)
        {
            var availablePlanets = planetGenerators.Keys.OrderBy(x => x).ToList();
            if (selectedIndex >= 0 && selectedIndex < availablePlanets.Count)
            {
                int planetIndex = availablePlanets[selectedIndex];
                SwitchToPlanet(planetIndex);
            }
        }
    }
    
    /// <summary>
    /// Update the planet dropdown with available planets
    /// </summary>
    private void UpdatePlanetDropdown()
    {
        if (planetDropdown == null || GameManager.Instance == null) return;
        
        // Clear existing options
        planetDropdown.ClearOptions();
        
        if (GameManager.Instance.enableMultiPlanetSystem)
        {
            var availablePlanets = planetGenerators.Keys.OrderBy(x => x).ToList();
            var options = new List<TMP_Dropdown.OptionData>();
            
            foreach (int planetIndex in availablePlanets)
            {
                var planetData = GameManager.Instance.GetPlanetData();
                string planetName = planetData.ContainsKey(planetIndex) ? planetData[planetIndex].planetName : $"Planet {planetIndex}";
                options.Add(new TMP_Dropdown.OptionData(planetName));
            }
            
            planetDropdown.AddOptions(options);
            
            // Set current selection
            int currentIndex = availablePlanets.IndexOf(currentPlanetIndex);
            if (currentIndex >= 0)
            {
                planetDropdown.SetValueWithoutNotify(currentIndex);
            }
        }
        else
        {
            // Single planet mode
            planetDropdown.AddOptions(new List<string> { "Earth" });
            planetDropdown.SetValueWithoutNotify(0);
        }
    }
    
    private void UpdateZoomButtonStates()
    {
        // Disable zoom out button if we're at minimum zoom (showing whole texture)
        if (zoomOutButton != null)
        {
            zoomOutButton.interactable = _currentZoomLevel > minZoomLevel;
        }
        
        // Disable zoom in button if we're at maximum zoom
        if (zoomInButton != null)
        {
            zoomInButton.interactable = _currentZoomLevel < maxZoomLevel;
        }
    }

    
    private void RefreshMinimapTexture()
    {
        var generator = GetCurrentGenerator();
        if (generator != null)
        {
            // Update zoom center to current camera position
            UpdateZoomCenter();
            generator.SetZoomLevel(_currentZoomLevel);
            generator.Rebuild();
        }
    }
    
    private void UpdateZoomCenter()
    {
        var generator = GetCurrentGenerator();
        var currentRoot = GetCurrentRoot();
        if (generator != null && mainCamera != null && currentRoot != null)
        {
            // Set zoom center based on where the camera is looking
            Vector3 center = currentRoot.position;
            Vector3 camDirFromCenter = (mainCamera.transform.position - center).normalized;
            Vector3 surfacePoint = center + camDirFromCenter;
            generator.SetZoomLevel(_currentZoomLevel, surfacePoint);
        }
    }

    // --- Target Switching ---
    public void SwitchToTarget(MinimapTarget target)
    {
        currentTarget = target;
        var generator = GetCurrentGenerator();
        
        if (generator != null)
        {
            // Build the minimap on-demand if not ready
            if (!generator.IsReady)
            {
                Debug.Log($"[MinimapController] Building minimap on-demand for target: {target}");
                generator.Build();
            }
            
            if (generator.IsReady)
            {
                minimapImage.texture = generator.minimapTexture;
            }
        }
        
        // Clear tile data caches when switching planets/moons
        if (TileDataHelper.Instance != null)
        {
            TileDataHelper.Instance.OnPlanetSwitch();
        }
        
        // Update UI labels
        if (currentTargetLabel)
        {
            string targetText = target switch
            {
                MinimapTarget.Planet => "Planet",
                MinimapTarget.Moon => "Moon", 
                MinimapTarget.PlanetByIndex => $"Planet {currentPlanetIndex}",
                _ => "Unknown"
            };
            currentTargetLabel.text = targetText;
        }
        
        if (planetNameLabel && GameManager.Instance != null && GameManager.Instance.enableMultiPlanetSystem)
        {
            if (target == MinimapTarget.PlanetByIndex)
            {
                var allPlanetData = GameManager.Instance.GetPlanetData();
                if (allPlanetData != null && allPlanetData.ContainsKey(currentPlanetIndex))
                {
                    var planetData = allPlanetData[currentPlanetIndex];
                    planetNameLabel.text = planetData.planetName ?? $"Planet {currentPlanetIndex}";
                }
                else
                {
                    planetNameLabel.text = $"Planet {currentPlanetIndex}";
                }
            }
            else
            {
                planetNameLabel.text = target.ToString();
            }
        }
        
        // Update button states (optional visual feedback)
        if (planetButton) planetButton.interactable = (target != MinimapTarget.Planet && target != MinimapTarget.PlanetByIndex);
        if (moonButton) moonButton.interactable = (target != MinimapTarget.Moon);
        
        // Navigation buttons removed - using dropdown instead
        
        // Update zoom button states for the new target
        UpdateZoomButtonStates();
    }

    public void SwitchToPlanet() => SwitchToTarget(MinimapTarget.Planet);
    public void SwitchToMoon() => SwitchToTarget(MinimapTarget.Moon);

    private MinimapGenerator GetCurrentGenerator()
    {
        // Multi-planet support
        if (GameManager.Instance != null && GameManager.Instance.enableMultiPlanetSystem)
        {
            if (currentTarget == MinimapTarget.PlanetByIndex)
            {
                return planetGenerators.TryGetValue(currentPlanetIndex, out var generator) ? generator : null;
            }
        }
        
        // Original behavior
        return currentTarget == MinimapTarget.Planet ? planetGenerator : moonGenerator;
    }

    private Transform GetCurrentRoot()
    {
        // Multi-planet support
        if (GameManager.Instance != null && GameManager.Instance.enableMultiPlanetSystem)
        {
            if (currentTarget == MinimapTarget.PlanetByIndex)
            {
                return planetRoots.TryGetValue(currentPlanetIndex, out var root) ? root : null;
            }
        }
        
        // Original behavior
        return currentTarget == MinimapTarget.Planet ? planetRoot : moonRoot;
    }
    
    /// <summary>
    /// Add a planet to the multi-planet minimap system
    /// </summary>
    public void AddPlanet(int planetIndex, MinimapGenerator generator, Transform root)
    {
        planetGenerators[planetIndex] = generator;
        planetRoots[planetIndex] = root;
        Debug.Log($"[MinimapController] Added planet {planetIndex} to minimap system");
        
        // Update dropdown options
        UpdatePlanetDropdown();
    }
    
    /// <summary>
    /// Switch to a specific planet by index (for multi-planet system)
    /// </summary>
    public void SwitchToPlanet(int planetIndex)
    {
        currentPlanetIndex = planetIndex;
        SwitchToTarget(MinimapTarget.PlanetByIndex);
        
        // Update dropdown selection without triggering event
        if (planetDropdown != null && GameManager.Instance != null && GameManager.Instance.enableMultiPlanetSystem)
        {
            var availablePlanets = planetGenerators.Keys.OrderBy(x => x).ToList();
            int dropdownIndex = availablePlanets.IndexOf(planetIndex);
            if (dropdownIndex >= 0)
            {
                planetDropdown.SetValueWithoutNotify(dropdownIndex);
            }
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        var generator = GetCurrentGenerator();
        if (!generator || !generator.IsReady) return;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, eventData.position, eventData.pressEventCamera, out var local))
        {
            var uv = LocalToUV(local);
            Vector3 dir = UVToDirection(uv);
            FocusCamera(dir);
        }
    }

    // --- Marker shows camera sub-observer point ---
    private void UpdateMarker()
    {
        var generator = GetCurrentGenerator();
        var currentRoot = GetCurrentRoot();
        if (!marker || !mainCamera || !currentRoot || !generator || !generator.IsReady) return;

        // For an orbital camera looking at planet/moon center: the sub-observer point is the direction from center toward camera
        Vector3 center = currentRoot.position;
        Vector3 camDirFromCenter = (mainCamera.transform.position - center).normalized;
        // The point on the surface facing the camera (nadir under the camera):
        Vector3 surfacePoint = center + camDirFromCenter; // unit sphere assumption for UV calc

        Vector2 uv = WorldDirToUV(camDirFromCenter);
        Vector2 anchored = UVToLocal(uv);
        
        marker.rectTransform.anchoredPosition = anchored;
    }

    // --- Camera focus ---
    private void FocusCamera(Vector3 dir)
    {
        if (cameraManager != null) {
            cameraManager.FocusOnDirection(dir, clickLerpSeconds);
        }
        else {
            // Fallback: rotate main camera to look at point on sphere
            var currentRoot = GetCurrentRoot();
            if (!currentRoot || !mainCamera) return;
            Vector3 center = currentRoot.position;
            Vector3 newPos = center - dir * (mainCamera.transform.position - center).magnitude;
            StartCoroutine(LerpCamera(mainCamera.transform.position, newPos, clickLerpSeconds));
        }
    }

    private System.Collections.IEnumerator LerpCamera(Vector3 from, Vector3 to, float seconds)
    {
        float t = 0;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.0001f, seconds);
            mainCamera.transform.position = Vector3.Slerp(from, to, t);
            var currentRoot = GetCurrentRoot();
            mainCamera.transform.LookAt(currentRoot ? currentRoot.position : Vector3.zero);
            yield return null;
        }
    }

    // --- Coordinate conversions ---

    private Vector2 LocalToUV(Vector2 local)
    {
        var size = _rt.rect.size;
        float u = Mathf.Clamp01((local.x / size.x) + 0.5f);
        float v = Mathf.Clamp01((local.y / size.y) + 0.5f);
        return new Vector2(u, v);
    }

    private Vector2 UVToLocal(Vector2 uv)
    {
        var size = _rt.rect.size;
        float x = (uv.x - 0.5f) * size.x;
        float y = (uv.y - 0.5f) * size.y;
        return new Vector2(x, y);
    }

    private Vector3 UVToDirection(Vector2 uv)
    {
        var generator = GetCurrentGenerator();
        var currentRoot = GetCurrentRoot();
        if (generator == null || currentRoot == null) 
        {
            // Fallback to simple conversion
            float fallbackLon = (uv.x * 2f - 1f) * Mathf.PI;
            float fallbackLat = (uv.y - 0.5f) * Mathf.PI;
            float fallbackClat = Mathf.Cos(fallbackLat);
            return new Vector3(fallbackClat * Mathf.Cos(fallbackLon), Mathf.Sin(fallbackLat), fallbackClat * Mathf.Sin(fallbackLon)).normalized;
        }
        
        // Calculate zoom center in UV space
        Vector3 zoomRootPos = currentRoot.position;
        Vector3 zoomDir = (generator.zoomCenter - zoomRootPos).normalized;
        float centerLat = Mathf.Asin(Mathf.Clamp(zoomDir.y, -1f, 1f));
        float centerLon = Mathf.Atan2(zoomDir.z, zoomDir.x);
        
        // Convert zoom center to UV space (0-1)
        float centerU = (centerLon + Mathf.PI) / (2f * Mathf.PI);
        float centerV = (centerLat + Mathf.PI * 0.5f) / Mathf.PI;
        
        // Calculate zoom-adjusted sampling area
        float sampleWidth = 1.0f / generator.zoomLevel;
        float sampleHeight = 1.0f / generator.zoomLevel;
        
        // Map UV to world space considering zoom
        float worldU = centerU - sampleWidth * 0.5f + uv.x * sampleWidth;
        float worldV = centerV - sampleHeight * 0.5f + uv.y * sampleHeight;
        
        // Convert back to lat/lon
        float lon = (worldU * 2f - 1f) * Mathf.PI;
        float lat = (worldV - 0.5f) * Mathf.PI;
        
        float clat = Mathf.Cos(lat);
        return new Vector3(clat * Mathf.Cos(lon), Mathf.Sin(lat), clat * Mathf.Sin(lon)).normalized;
    }

    private Vector2 WorldDirToUV(Vector3 dir)
    {
        var generator = GetCurrentGenerator();
        var currentRoot = GetCurrentRoot();
        if (generator == null || currentRoot == null)
        {
            // Fallback to simple conversion
            float fallbackLat = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f));
            float fallbackLon = Mathf.Atan2(dir.z, dir.x);
            float fallbackU = (fallbackLon + Mathf.PI) / (2f * Mathf.PI);
            float fallbackV = (fallbackLat + Mathf.PI * 0.5f) / Mathf.PI;
            return new Vector2(fallbackU, fallbackV);
        }
        
        // Convert direction to lat/lon
        float dirLat = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f));
        float dirLon = Mathf.Atan2(dir.z, dir.x);
        
        // Convert to world UV space (0-1)
        float worldU = (dirLon + Mathf.PI) / (2f * Mathf.PI);
        float worldV = (dirLat + Mathf.PI * 0.5f) / Mathf.PI;
        
        // Calculate zoom center in UV space
        Vector3 zoomRootPos = currentRoot.position;
        Vector3 zoomDir = (generator.zoomCenter - zoomRootPos).normalized;
        float centerLat = Mathf.Asin(Mathf.Clamp(zoomDir.y, -1f, 1f));
        float centerLon = Mathf.Atan2(zoomDir.z, zoomDir.x);
        
        float centerU = (centerLon + Mathf.PI) / (2f * Mathf.PI);
        float centerV = (centerLat + Mathf.PI * 0.5f) / Mathf.PI;
        
        // Calculate zoom-adjusted sampling area
        float sampleWidth = 1.0f / generator.zoomLevel;
        float sampleHeight = 1.0f / generator.zoomLevel;
        
        // Map world UV to local minimap UV
        float localU = (worldU - (centerU - sampleWidth * 0.5f)) / sampleWidth;
        float localV = (worldV - (centerV - sampleHeight * 0.5f)) / sampleHeight;
        
        return new Vector2(Mathf.Clamp01(localU), Mathf.Clamp01(localV));
    }
}
