using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public enum MinimapTarget
{
    Planet,
    Moon
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

    [Header("UI Controls")]
    public Button planetButton;   // Button to switch to planet minimap
    public Button moonButton;     // Button to switch to moon minimap
    public Button zoomInButton;   // Button to zoom in
    public Button zoomOutButton;  // Button to zoom out
    public TextMeshProUGUI currentTargetLabel; // Optional label showing current target

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
    private float _minimapRefreshCooldown = 0.5f; // Seconds between minimap refreshes
    private float _lastRefreshTime;

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
    }

    void Start()
    {
        // Don't build minimaps here - they'll be built by GameManager after world generation
        // Just set initial target
        SwitchToTarget(currentTarget);
    }

    void Update()
    {
        var currentGenerator = GetCurrentGenerator();
        if (currentGenerator && currentGenerator.IsReady && minimapImage.texture == null)
            minimapImage.texture = currentGenerator.minimapTexture;

        UpdateMarker();
        
        // Update zoom center when camera moves significantly (with throttling)
        if (currentGenerator != null && _currentZoomLevel != 1.0f && mainCamera != null)
        {
            float distanceMoved = Vector3.Distance(mainCamera.transform.position, _lastCameraPosition);
            bool shouldRefresh = distanceMoved > 0.1f && (Time.time - _lastRefreshTime) > _minimapRefreshCooldown;
            
            if (shouldRefresh)
            {
                UpdateZoomCenter();
                currentGenerator.Rebuild();
                _lastCameraPosition = mainCamera.transform.position;
                _lastRefreshTime = Time.time;
            }
            else
            {
                // Just update center without rebuilding for smooth marker movement
                UpdateZoomCenterOnly();
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
    }
    
    public void OnZoomOutButton() 
    { 
        _currentZoomLevel = Mathf.Clamp(_currentZoomLevel - zoomStep, minZoomLevel, maxZoomLevel);
        RefreshMinimapTexture();
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
        
        if (generator && generator.IsReady)
        {
            minimapImage.texture = generator.minimapTexture;
        }
        
        // Update UI
        if (currentTargetLabel)
        {
            currentTargetLabel.text = target == MinimapTarget.Planet ? "Planet" : "Moon";
        }
        
        // Update button states (optional visual feedback)
        if (planetButton) planetButton.interactable = (target != MinimapTarget.Planet);
        if (moonButton) moonButton.interactable = (target != MinimapTarget.Moon);
    }

    public void SwitchToPlanet() => SwitchToTarget(MinimapTarget.Planet);
    public void SwitchToMoon() => SwitchToTarget(MinimapTarget.Moon);

    private MinimapGenerator GetCurrentGenerator()
    {
        return currentTarget == MinimapTarget.Planet ? planetGenerator : moonGenerator;
    }

    private Transform GetCurrentRoot()
    {
        return currentTarget == MinimapTarget.Planet ? planetRoot : moonRoot;
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
        
        // Account for zoom level and zoom center
        float zoomLevel = generator.zoomLevel;
        Vector3 zoomCenter = generator.zoomCenter;
        
        // Calculate zoom-adjusted angular coverage
        float baseCoverage = 2f * Mathf.PI;
        float latCoverage = Mathf.PI;
        float currentLonCoverage = baseCoverage / zoomLevel;
        float currentLatCoverage = latCoverage / zoomLevel;
        
        // Convert zoom center to lat/lon
        Vector3 zoomRootPos = currentRoot.position;
        Vector3 zoomDir = (zoomCenter - zoomRootPos).normalized;
        float centerLat = Mathf.Asin(Mathf.Clamp(zoomDir.y, -1f, 1f));
        float centerLon = Mathf.Atan2(zoomDir.z, zoomDir.x);
        
        // Map UV to lat/lon in zoomed space
        float lat = centerLat + (uv.y - 0.5f) * currentLatCoverage;
        float lon = centerLon + (uv.x - 0.5f) * currentLonCoverage;
        
        // Clamp latitude and normalize longitude
        lat = Mathf.Clamp(lat, -Mathf.PI * 0.5f, Mathf.PI * 0.5f);
        while (lon > Mathf.PI) lon -= 2f * Mathf.PI;
        while (lon < -Mathf.PI) lon += 2f * Mathf.PI;
        
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
        
        // Account for zoom level and zoom center
        float zoomLevel = generator.zoomLevel;
        Vector3 zoomCenter = generator.zoomCenter;
        
        // Calculate zoom-adjusted angular coverage
        float baseCoverage = 2f * Mathf.PI;
        float latCoverage = Mathf.PI;
        float currentLonCoverage = baseCoverage / zoomLevel;
        float currentLatCoverage = latCoverage / zoomLevel;
        
        // Convert zoom center to lat/lon
        Vector3 zoomRootPos = currentRoot.position;
        Vector3 zoomDir = (zoomCenter - zoomRootPos).normalized;
        float centerLat = Mathf.Asin(Mathf.Clamp(zoomDir.y, -1f, 1f));
        float centerLon = Mathf.Atan2(zoomDir.z, zoomDir.x);
        
        // Convert direction to lat/lon
        float dirLat = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f));
        float dirLon = Mathf.Atan2(dir.z, dir.x);
        
        // Map to UV space relative to zoom center
        float u = 0.5f + (dirLon - centerLon) / currentLonCoverage;
        float v = 0.5f + (dirLat - centerLat) / currentLatCoverage;
        
        return new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v));
    }
}
