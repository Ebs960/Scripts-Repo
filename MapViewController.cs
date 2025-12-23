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
/// - Enable/disable FlatMapRenderer
/// - Enable/disable Globe VISUALS ONLY (renderers/colliders) - NOT the whole GameObject
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
    [Tooltip("The flat map renderer. Will be ENABLED in Flat mode, DISABLED in Globe mode.")]
    [SerializeField] private FlatMapRenderer flatMapRenderer;

    [Tooltip("The globe visual root (PlanetGenerator GameObject). Renderers/Colliders will be toggled, but GameObject stays active.")]
    [SerializeField] private GameObject globeVisualRoot;

    [Tooltip("Auto-bind globeVisualRoot from GameManager if not assigned.")]
    [SerializeField] private bool autoBindGlobeRoot = true;

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

    // Cached globe components for efficient toggling
    private List<Renderer> _globeRenderers = new List<Renderer>();
    private List<Collider> _globeColliders = new List<Collider>();
    private bool _globeComponentsCached;

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

    private void Start()
    {
        // Bind globe root if needed
        TryBindGlobeRoot();

        // Apply startup mode
        SetMode(startMode, force: true);
        _initialized = true;
    }

    private void Update()
    {
        // Continuously try to bind globe root until we have it
        if (autoBindGlobeRoot && globeVisualRoot == null)
        {
            TryBindGlobeRoot();
            if (globeVisualRoot != null)
            {
                // Cache components and re-apply mode now that we have the globe reference
                CacheGlobeComponents();
                ApplyModeVisuals();
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
    /// Cache all Renderers and Colliders under the globe root for efficient toggling.
    /// This must be called after the globe is generated.
    /// </summary>
    private void CacheGlobeComponents()
    {
        _globeRenderers.Clear();
        _globeColliders.Clear();

        if (globeVisualRoot == null) return;

        // Find the TilePrefabs container - this is where the visual tiles live
        var tilePrefabsParent = globeVisualRoot.transform.Find("TilePrefabs");
        if (tilePrefabsParent != null)
        {
            // Cache renderers and colliders from tile prefabs
            _globeRenderers.AddRange(tilePrefabsParent.GetComponentsInChildren<Renderer>(true));
            _globeColliders.AddRange(tilePrefabsParent.GetComponentsInChildren<Collider>(true));
        }

        // Also get any renderers/colliders on the root itself (like atmosphere, etc.)
        var rootRenderer = globeVisualRoot.GetComponent<Renderer>();
        if (rootRenderer != null) _globeRenderers.Add(rootRenderer);
        
        var rootCollider = globeVisualRoot.GetComponent<Collider>();
        if (rootCollider != null) _globeColliders.Add(rootCollider);

        _globeComponentsCached = true;
        Debug.Log($"[MapViewController] Cached {_globeRenderers.Count} renderers and {_globeColliders.Count} colliders from globe");
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

        // === GLOBE VISUALS (disable FIRST before flat map rebuild) ===
        // CRITICAL: We only disable renderers/colliders, NOT the entire GameObject.
        // This keeps game systems (TileSystem, etc.) working.
        SetGlobeVisualsEnabled(globeActive);

        // === FLAT MAP ===
        if (flatMapRenderer != null)
        {
            flatMapRenderer.gameObject.SetActive(flatActive);

            // Rebuild flat map when switching to flat mode
            // Note: Globe is already visually hidden at this point, so cloning is safe
            if (flatActive && !flatMapRenderer.IsBuilt)
            {
                var gen = GetCurrentPlanetGenerator();
                if (gen != null)
                    flatMapRenderer.Rebuild(gen);
            }
        }

        Debug.Log($"[MapViewController] Applied visuals - Flat: {flatActive}, Globe renderers: {globeActive}");
    }

    /// <summary>
    /// Enable or disable globe visual components (Renderers and Colliders) without disabling the GameObject.
    /// This allows game systems to keep running while hiding the globe visually.
    /// </summary>
    private void SetGlobeVisualsEnabled(bool enabled)
    {
        // If we haven't cached components yet, try now
        if (!_globeComponentsCached && globeVisualRoot != null)
        {
            CacheGlobeComponents();
        }

        // Toggle all cached renderers
        foreach (var rend in _globeRenderers)
        {
            if (rend != null)
                rend.enabled = enabled;
        }

        // Toggle all cached colliders
        foreach (var col in _globeColliders)
        {
            if (col != null)
                col.enabled = enabled;
        }
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
        if (flatMapRenderer != null && flatMapRenderer.gameObject.activeSelf != flatActive)
        {
            flatMapRenderer.gameObject.SetActive(flatActive);
            Debug.Log($"[MapViewController] Enforced flat map active state to {flatActive}");
        }

        // Ensure globe renderers/colliders match mode
        // (We no longer call SetActive on the whole globe)
        if (_globeComponentsCached)
        {
            // Check a sample renderer to see if enforcement is needed
            if (_globeRenderers.Count > 0 && _globeRenderers[0] != null)
            {
                if (_globeRenderers[0].enabled != globeActive)
                {
                    SetGlobeVisualsEnabled(globeActive);
                    Debug.Log($"[MapViewController] Enforced globe visuals enabled state to {globeActive}");
                }
            }
        }
    }

    private void TryBindGlobeRoot()
    {
        if (!autoBindGlobeRoot) return;
        if (globeVisualRoot != null) return;

        var gen = GetCurrentPlanetGenerator();
        if (gen != null)
        {
            globeVisualRoot = gen.gameObject;
            Debug.Log($"[MapViewController] Bound globe root: {globeVisualRoot.name}");
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
        // Clear cached components from old planet
        _globeComponentsCached = false;
        _globeRenderers.Clear();
        _globeColliders.Clear();

        globeVisualRoot = null; // Force rebind
        TryBindGlobeRoot();

        // Recache components for new planet
        if (globeVisualRoot != null)
        {
            CacheGlobeComponents();
        }

        // Rebuild flat map for new planet
        if (currentMode == MapViewMode.Flat && flatMapRenderer != null)
        {
            flatMapRenderer.Clear(); // Clear old flat tiles
            var gen = GetCurrentPlanetGenerator();
            if (gen != null)
                flatMapRenderer.Rebuild(gen);
        }

        ApplyModeVisuals();
    }

    /// <summary>
    /// Force recache of globe visual components.
    /// Call this if globe visuals have been regenerated.
    /// </summary>
    public void RefreshGlobeComponentCache()
    {
        _globeComponentsCached = false;
        CacheGlobeComponents();
        ApplyModeVisuals();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
