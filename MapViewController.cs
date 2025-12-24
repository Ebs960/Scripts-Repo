using UnityEngine;
using System;
using System.Collections.Generic;

    /// <summary>
    /// SINGLE AUTHORITY for map view mode.
    /// 
    /// This is the ONE place that decides whether we're in Globe or Flat mode.
    /// All other systems must query this controller - they do NOT decide visibility themselves.
    /// 
    /// Responsibilities:
    /// - Enable/disable FlatMapTextureRenderer
    /// - Enable/disable GlobeRenderer
    /// - Switch camera behavior
    /// - Block input to inactive mode
    /// 
    /// CRITICAL: We do NOT call SetActive(false) on PlanetGenerator because it holds game DATA.
    /// We only disable the visual components (Renderers and Colliders).
    /// </summary>
public class MapViewController : MonoBehaviour
{
    public static MapViewController Instance { get; private set; }

    public enum MapViewMode
    {
        Globe,
        Flat
    }

    [Header("Current State (Authoritative)")]
    [SerializeField] private MapViewMode currentMode = MapViewMode.Flat;

    [Header("View References")]
    [Tooltip("The flat map texture renderer. Will be ENABLED in Flat mode, DISABLED in Globe mode.")]
    [SerializeField] private FlatMapTextureRenderer flatMapRenderer;
    
    [Tooltip("The globe renderer. Will be ENABLED in Globe mode, DISABLED in Flat mode.")]
    [SerializeField] private GlobeRenderer globeRenderer;

    [Tooltip("The planet generator (for data access).")]
    [SerializeField] private PlanetGenerator planetGenerator;

    [Header("Camera References")]
    [SerializeField] private PlanetaryCameraManager globeCamera;
    [SerializeField] private Camera mainCamera;

    [Header("Startup")]
    [SerializeField] private MapViewMode startMode = MapViewMode.Flat;

    [Header("Performance")]
    [Tooltip("How often to enforce mode visuals (in seconds). 0 = every frame, which is expensive.")]
    [SerializeField] private float enforceInterval = 0.5f;

    /// <summary>
    /// Event fired when mode changes. Subscribe to react to mode switches.
    /// </summary>
    public event Action<MapViewMode> OnModeChanged;

    /// <summary>
    /// Current authoritative map view mode. Read-only from outside.
    /// </summary>
    public MapViewMode CurrentMode => currentMode;

    /// <summary>
    /// Is the flat map currently the active view?
    /// </summary>
    public bool IsFlatMode => currentMode == MapViewMode.Flat;

    /// <summary>
    /// Is the globe currently the active view?
    /// </summary>
    public bool IsGlobeMode => currentMode == MapViewMode.Globe;

    private bool _initialized;
    private float _lastEnforceTime;
    private bool _subscribedToPlanetEvents;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[MapViewController] Duplicate instance detected, destroying this one.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (mainCamera == null)
            mainCamera = Camera.main;
    }
    
    private void OnEnable()
    {
        TrySubscribeToPlanetEvents();
    }
    
    private void OnDisable()
    {
        TryUnsubscribeFromPlanetEvents();
    }
    
    private void TrySubscribeToPlanetEvents()
    {
        if (_subscribedToPlanetEvents) return;
        if (GameManager.Instance == null) return;
        
        GameManager.Instance.OnPlanetReady += HandlePlanetReady;
        _subscribedToPlanetEvents = true;
    }
    
    private void TryUnsubscribeFromPlanetEvents()
    {
        if (!_subscribedToPlanetEvents) return;
        if (GameManager.Instance != null)
            GameManager.Instance.OnPlanetReady -= HandlePlanetReady;
        _subscribedToPlanetEvents = false;
    }
    
    private void HandlePlanetReady(int planetIndex)
    {
        // Auto-update planet reference when a planet becomes ready
        var newPlanet = GetCurrentPlanetGenerator();
        if (newPlanet != null && newPlanet != planetGenerator)
        {
            Debug.Log($"[MapViewController] Planet {planetIndex} ready, updating references");
            OnPlanetChanged();
        }
    }

    private void Start()
    {
        // Subscribe to planet events
        TrySubscribeToPlanetEvents();
        
        // Try to find components if not assigned
        if (flatMapRenderer == null)
            flatMapRenderer = FindAnyObjectByType<FlatMapTextureRenderer>();
        
        if (globeRenderer == null)
            globeRenderer = FindAnyObjectByType<GlobeRenderer>();
        
        // Auto-assign current planet (will be null if planets not created yet, that's OK)
        planetGenerator = GetCurrentPlanetGenerator();

        // Apply startup mode (only if we have a planet)
        if (planetGenerator != null)
        {
            SetMode(startMode, force: true);
        }
        else
        {
            // Wait for planet to be ready - will be triggered by HandlePlanetReady
            Debug.Log("[MapViewController] No planet available yet, will initialize when planet is ready");
        }
        _initialized = true;
    }

    private void Update()
    {
        // Late subscription if GameManager was created after our Start()
        TrySubscribeToPlanetEvents();
        
        // Try to find components if still missing
        if (flatMapRenderer == null)
            flatMapRenderer = FindAnyObjectByType<FlatMapTextureRenderer>();
        
        if (globeRenderer == null)
            globeRenderer = FindAnyObjectByType<GlobeRenderer>();
        
        // Auto-update planet reference if it changed (multi-planet support)
        var currentPlanet = GetCurrentPlanetGenerator();
        if (currentPlanet != null && currentPlanet != planetGenerator)
        {
            Debug.Log("[MapViewController] Detected planet change, updating references");
            OnPlanetChanged();
        }
        else if (planetGenerator == null && currentPlanet != null)
        {
            // First time planet becomes available
            planetGenerator = currentPlanet;
            if (!_initialized)
            {
                SetMode(startMode, force: true);
                _initialized = true;
            }
        }

        // SAFETY: Periodically enforce mode visuals (not every frame)
        // This catches any external code that might re-enable/disable things incorrectly
        if (enforceInterval <= 0f)
        {
            EnforceModeVisuals();
        }
        else if (Time.time - _lastEnforceTime >= enforceInterval)
        {
            _lastEnforceTime = Time.time;
            EnforceModeVisuals();
        }
    }

    /// <summary>
    /// Set the map view mode. This is THE authoritative way to switch views.
    /// </summary>
    public void SetMode(MapViewMode newMode, bool force = false)
    {
        if (!force && currentMode == newMode)
            return;

        MapViewMode oldMode = currentMode;
        currentMode = newMode;

        Debug.Log($"[MapViewController] Mode changed: {oldMode} -> {newMode}");

        ApplyModeVisuals();
        ApplyModeCamera();

        OnModeChanged?.Invoke(newMode);
    }

    /// <summary>
    /// Toggle between Globe and Flat modes.
    /// </summary>
    public void ToggleMode()
    {
        SetMode(currentMode == MapViewMode.Flat ? MapViewMode.Globe : MapViewMode.Flat);
    }

    /// <summary>
    /// Switch to Flat mode.
    /// </summary>
    public void SetFlatMode() => SetMode(MapViewMode.Flat);

    /// <summary>
    /// Switch to Globe mode.
    /// </summary>
    public void SetGlobeMode() => SetMode(MapViewMode.Globe);

    private void ApplyModeVisuals()
    {
        bool flatActive = currentMode == MapViewMode.Flat;
        bool globeActive = currentMode == MapViewMode.Globe;

        // === FLAT MAP ===
        if (flatMapRenderer != null)
        {
            flatMapRenderer.SetVisible(flatActive);

            // Rebuild flat map when switching to flat mode if needed
            if (flatActive && !flatMapRenderer.IsBuilt)
            {
                var gen = GetCurrentPlanetGenerator();
                if (gen != null)
                    flatMapRenderer.Rebuild(gen);
            }
        }

        // === GLOBE ===
        if (globeRenderer != null)
        {
            globeRenderer.SetVisible(globeActive);

            // Rebuild globe when switching to globe mode if needed
            if (globeActive && !globeRenderer.IsBuilt)
            {
                var gen = GetCurrentPlanetGenerator();
                if (gen != null && flatMapRenderer != null && flatMapRenderer.IsBuilt)
                    globeRenderer.Rebuild(gen, flatMapRenderer);
            }
        }

        Debug.Log($"[MapViewController] Applied visuals - Flat: {flatActive}, Globe: {globeActive}");
    }

    private void ApplyModeCamera()
    {
        bool flatActive = currentMode == MapViewMode.Flat;

        // Globe camera (orbital) - only active in globe mode
        if (globeCamera != null)
        {
            globeCamera.enabled = !flatActive;
        }

        // Flat camera is handled by FlatGlobeZoomViewController or similar
    }

    /// <summary>
    /// Enforces mode visuals periodically as a safety net.
    /// </summary>
    private void EnforceModeVisuals()
    {
        bool flatActive = currentMode == MapViewMode.Flat;
        bool globeActive = currentMode == MapViewMode.Globe;

        // Ensure flat map state matches mode
        if (flatMapRenderer != null)
        {
            // Check if renderer is enabled (we use SetVisible which controls the renderer)
            var renderer = flatMapRenderer.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.enabled != flatActive)
            {
                flatMapRenderer.SetVisible(flatActive);
                Debug.Log($"[MapViewController] Enforced flat map visibility to {flatActive}");
            }
        }

        // Ensure globe renderer matches mode
        if (globeRenderer != null)
        {
            var renderer = globeRenderer.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.enabled != globeActive)
            {
                globeRenderer.SetVisible(globeActive);
                Debug.Log($"[MapViewController] Enforced globe visibility to {globeActive}");
            }
        }
    }

    private PlanetGenerator GetCurrentPlanetGenerator()
    {
        if (GameManager.Instance != null)
            return GameManager.Instance.GetCurrentPlanetGenerator();
        return null;
    }

    /// <summary>
    /// Call this when planet changes (e.g., switching planets in multi-planet mode).
    /// </summary>
    public void OnPlanetChanged()
    {
        planetGenerator = GetCurrentPlanetGenerator();

        // Rebuild flat map for new planet
        if (currentMode == MapViewMode.Flat && flatMapRenderer != null)
        {
            flatMapRenderer.Clear();
            if (planetGenerator != null)
                flatMapRenderer.Rebuild(planetGenerator);
        }

        // Rebuild globe for new planet
        if (currentMode == MapViewMode.Globe && globeRenderer != null)
        {
            globeRenderer.Clear();
            if (planetGenerator != null && flatMapRenderer != null && flatMapRenderer.IsBuilt)
                globeRenderer.Rebuild(planetGenerator, flatMapRenderer);
        }

        ApplyModeVisuals();
    }

    private void OnDestroy()
    {
        TryUnsubscribeFromPlanetEvents();
        
        if (Instance == this)
            Instance = null;
    }
}
