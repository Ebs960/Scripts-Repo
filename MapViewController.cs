using UnityEngine;
using System;
using System.Collections.Generic;

    /// <summary>
    /// SINGLE AUTHORITY for map view mode.
    /// 
    /// This is the ONE place that decides whether we're in Flat mode.
    /// All other systems must query this controller - they do NOT decide visibility themselves.
    /// 
    /// Responsibilities:
    /// - Enable/disable FlatMapTextureRenderer
    /// - Enable/disable FlatMapTextureRenderer
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
        Flat
    }

    [Header("Current State (Authoritative)")]
    [SerializeField] private MapViewMode currentMode = MapViewMode.Flat;

    [Header("View References")]
    [Tooltip("The flat map texture renderer. Will be ENABLED in Flat mode.")]
    [SerializeField] private FlatMapTextureRenderer flatMapRenderer;
    
    [Tooltip("The planet generator (for data access).")]
    [SerializeField] private PlanetGenerator planetGenerator;

    [Header("Camera References")]
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
    /// Switch to Flat mode.
    /// </summary>
    public void SetFlatMode() => SetMode(MapViewMode.Flat);

    private void ApplyModeVisuals()
    {
        bool flatActive = currentMode == MapViewMode.Flat;

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
        Debug.Log($"[MapViewController] Applied visuals - Flat: {flatActive}");
    }

    private void ApplyModeCamera()
    {
        // Flat camera is handled by FlatMap camera controllers.
    }

    /// <summary>
    /// Enforces mode visuals periodically as a safety net.
    /// </summary>
    private void EnforceModeVisuals()
    {
        bool flatActive = currentMode == MapViewMode.Flat;

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

        ApplyModeVisuals();
    }

    private void OnDestroy()
    {
        TryUnsubscribeFromPlanetEvents();
        
        if (Instance == this)
            Instance = null;
    }
}
