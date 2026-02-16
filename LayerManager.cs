using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using static GameManager;

/// <summary>
/// Centralized authority for planet gameplay/visual layers (Surface / Underwater / Mantle / Atmosphere / Orbit).
/// Responsibilities:
/// - Determine whether a planet supports a layer (PlanetConfig or GameManager PlanetData)
/// - Track current view visibility per layer (view state)
/// - Apply visibility to planet layer roots and gas giant renderer/volumetrics rules
///
/// Attach to the same GameObject as PlanetGenerator (recommended).
/// </summary>
[DisallowMultipleComponent]
public class LayerManager : MonoBehaviour
{
    [Header("References (optional overrides)")]
    [Tooltip("If not assigned, will auto-resolve from the same GameObject.")]
    [SerializeField] private PlanetGenerator planetGenerator;

    [Tooltip("If not assigned, will use PlanetGenerator.surfaceRoot.")]
    [FormerlySerializedAs("surfaceRoot")]
    [SerializeField] private GameObject surfaceRootOverride;

    [Tooltip("If not assigned, will use PlanetGenerator.underwaterRoot.")]
    [FormerlySerializedAs("underwaterRoot")]
    [SerializeField] private GameObject underwaterRootOverride;

    [Header("Camera Switching")]
    [Tooltip("When enabled, LayerManager will switch between the surface and underwater camera when the active layer changes.")]
    [SerializeField] private bool switchCameraOnLayerChange = true;
    [Tooltip("Optional camera used for Surface view. If not assigned, Camera.main will be used when switching back.")]
    [SerializeField] private Camera surfaceCameraOverride;
    [Tooltip("Optional camera used for Underwater view. Assign a separate camera with underwater post-processing/settings.")]
    [SerializeField] private Camera underwaterCameraOverride;
    [Header("Post-Processing Volumes")]
    [Tooltip("Optional global Volume used for Surface view. Assign a Volume with your surface post-processing profile.")]
    [SerializeField] private Volume surfaceVolumeOverride;
    [Tooltip("Optional global Volume used for Underwater view. Assign a Volume with underwater color/fog/caustics profile.")]
    [SerializeField] private Volume underwaterVolumeOverride;

    [Tooltip("If not assigned, will use PlanetGenerator.atmosphereRoot.")]
    [FormerlySerializedAs("atmosphereRoot")]
    [SerializeField] private GameObject atmosphereRootOverride;

    [Tooltip("If not assigned, will use PlanetGenerator.gasGiantRenderer.")]
    [SerializeField] private GasGiantRenderer gasGiantRendererOverride;

    [Tooltip("If not assigned, will attempt to resolve from planet hierarchy (GasGiantVolumeSpawner).")]
    [SerializeField] private GasGiantVolumeSpawner gasGiantVolumeSpawnerOverride;

    [Tooltip("If not assigned, will use PlanetGenerator.terrainRenderer.")]
    [SerializeField] private HexMapChunkManager terrainRendererOverride;

    [Header("Diagnostics")]
    [Tooltip("Enable minimal logs for layer state changes (avoid spam).")]
    [SerializeField] private bool logLayerChanges = false;

    private readonly HashSet<PlanetLayerType> _supported = new HashSet<PlanetLayerType>();
    private readonly Dictionary<PlanetLayerType, bool> _visible = new Dictionary<PlanetLayerType, bool>();

    private bool _initialized = false;
    private bool _lastGasGiantEnabled = false;
    private bool _warnedMissingPlanetGenerator = false;
    private readonly HashSet<PlanetLayerType> _warnedUnsupportedLayer = new HashSet<PlanetLayerType>();

    public event Action<PlanetLayerType, bool> OnLayerVisibilityChanged;

    private void Awake()
    {
        ResolveReferencesIfNeeded();
    }

    /// <summary>
    /// Initialize supported layers and apply initial default visibility.
    /// Called by PlanetGenerator during generation.
    /// </summary>
    public void InitializeForPlanet(PlanetGenerator gen, PlanetData data)
    {
        if (gen != null) planetGenerator = gen;
        ResolveReferencesIfNeeded();

        _supported.Clear();

        // Authoritative: PlanetConfig supportedLayers if present.
        if (planetGenerator != null && planetGenerator.planetConfig != null && planetGenerator.planetConfig.supportedLayers != null)
        {
            foreach (var layer in planetGenerator.planetConfig.supportedLayers)
                _supported.Add(layer);
        }
        // Fallback: runtime PlanetData.supportedLayers
        else if (data != null && data.supportedLayers != null)
        {
            foreach (var plc in data.supportedLayers)
            {
                if (plc == null) continue;
                _supported.Add(plc.layerType);
            }
        }

        // Default visibility MUST match legacy behavior:
        // - Surface: enabled when supported
        // - Underwater: enabled when supported
        // - Atmosphere: disabled by default (user toggles via UI)
        // - Mantle/Orbit: not currently visualized by roots (default hidden)
        _visible[PlanetLayerType.Surface] = IsLayerSupported(PlanetLayerType.Surface);
        _visible[PlanetLayerType.Underwater] = IsLayerSupported(PlanetLayerType.Underwater);
        _visible[PlanetLayerType.Atmosphere] = false;
        _visible[PlanetLayerType.Mantle] = false;
        _visible[PlanetLayerType.Orbit] = false;

        _initialized = true;

        ApplyVisualState(force: true);
    }

    public bool IsLayerSupported(PlanetLayerType layer)
    {
        if (!_initialized)
        {
            // Support queries are still valid before InitializeForPlanet when PlanetConfig is assigned on prefab.
            ResolveReferencesIfNeeded();
            if (planetGenerator != null && planetGenerator.planetConfig != null && planetGenerator.planetConfig.supportedLayers != null)
            {
                return planetGenerator.planetConfig.supportedLayers.Contains(layer);
            }
        }
        return _supported.Contains(layer);
    }

    public bool IsLayerVisible(PlanetLayerType layer)
    {
        if (_visible.TryGetValue(layer, out var v)) return v;
        return false;
    }

    public void SetLayerVisible(PlanetLayerType layer, bool visible)
    {
        ResolveReferencesIfNeeded();

        // Allow hiding any layer regardless of support (safe), but do not allow enabling unsupported layers.
        if (visible && !IsLayerSupported(layer))
        {
            if (!_warnedUnsupportedLayer.Contains(layer))
            {
                Debug.LogWarning($"[LayerManager] Cannot enable unsupported layer '{layer}' on planet '{GetPlanetNameForLogs()}'.");
                _warnedUnsupportedLayer.Add(layer);
            }
            return;
        }

        bool old = IsLayerVisible(layer);
        if (old == visible) return;

        _visible[layer] = visible;

        ApplyVisualState(force: false);

        OnLayerVisibilityChanged?.Invoke(layer, visible);
        if (logLayerChanges)
        {
            Debug.Log($"[LayerManager] Layer visibility changed planet='{GetPlanetNameForLogs()}' layer={layer} visible={visible}");
        }
    }

    public void SetOnlyLayerVisible(PlanetLayerType layer)
    {
        // Optional UI mode helper: show only one layer (if supported) and hide others.
        // Keep behavior minimal: Surface/Underwater/Atmosphere only. Mantle/Orbit are left as-is unless requested.
        SetLayerVisible(PlanetLayerType.Surface, layer == PlanetLayerType.Surface);
        SetLayerVisible(PlanetLayerType.Underwater, layer == PlanetLayerType.Underwater);
        SetLayerVisible(PlanetLayerType.Atmosphere, layer == PlanetLayerType.Atmosphere);
    }

    private void ApplyVisualState(bool force)
    {
        // Must be robust even if references are missing.
        var gen = planetGenerator;
        if (gen == null)
        {
            if (!_warnedMissingPlanetGenerator)
            {
                Debug.LogWarning("[LayerManager] Missing PlanetGenerator reference; cannot apply layer visuals.");
                _warnedMissingPlanetGenerator = true;
            }
            return;
        }

        var surfaceRoot = surfaceRootOverride != null ? surfaceRootOverride : gen.surfaceRoot;
        var underwaterRoot = underwaterRootOverride != null ? underwaterRootOverride : gen.underwaterRoot;
        var atmosphereRoot = atmosphereRootOverride != null ? atmosphereRootOverride : gen.atmosphereRoot;
        var gasGiantRenderer = gasGiantRendererOverride != null ? gasGiantRendererOverride : gen.gasGiantRenderer;
        var terrainRenderer = terrainRendererOverride != null ? terrainRendererOverride : gen.terrainRenderer;
        var volumeSpawner = gasGiantVolumeSpawnerOverride != null ? gasGiantVolumeSpawnerOverride : gen.GetComponentInChildren<GasGiantVolumeSpawner>(true);

        bool surfaceVisible = IsLayerVisible(PlanetLayerType.Surface) && IsLayerSupported(PlanetLayerType.Surface);
        bool underwaterVisible = IsLayerVisible(PlanetLayerType.Underwater) && IsLayerSupported(PlanetLayerType.Underwater);
        bool atmosphereVisible = IsLayerVisible(PlanetLayerType.Atmosphere) && IsLayerSupported(PlanetLayerType.Atmosphere);

        // Apply roots (with legacy Y offsets).
        if (surfaceRoot != null)
        {
            surfaceRoot.SetActive(surfaceVisible);
            var lp = surfaceRoot.transform.localPosition;
            surfaceRoot.transform.localPosition = new Vector3(lp.x, gen.surfaceYOffset, lp.z);
        }

        if (underwaterRoot != null)
        {
            underwaterRoot.SetActive(underwaterVisible);
            var lp = underwaterRoot.transform.localPosition;
            underwaterRoot.transform.localPosition = new Vector3(lp.x, gen.underwaterYOffset, lp.z);
        }

        if (atmosphereRoot != null)
        {
            atmosphereRoot.SetActive(atmosphereVisible);
            var lp = atmosphereRoot.transform.localPosition;
            atmosphereRoot.transform.localPosition = new Vector3(lp.x, gen.atmosphereYOffset, lp.z);
        }

        // Gas giant visuals are ONLY valid on planets that have Atmosphere and do not have Surface.
        bool enableGasGiantVisuals = IsLayerSupported(PlanetLayerType.Atmosphere) && !IsLayerSupported(PlanetLayerType.Surface);

        // Legacy behavior: terrain renderer disabled for gas giant planets based on support (not current visibility).
        if (terrainRenderer != null)
        {
            terrainRenderer.enabled = !enableGasGiantVisuals;
        }

        // Visibility rule: show gas giant when Atmosphere is visible and Surface is NOT visible (Surface not supported on true gas giants).
        bool shouldShowGasGiant = enableGasGiantVisuals && atmosphereVisible && !surfaceVisible;

        if (gasGiantRenderer != null)
        {
            if (force || _lastGasGiantEnabled != shouldShowGasGiant)
            {
                gasGiantRenderer.SetEnabledForPlanet(shouldShowGasGiant);
                _lastGasGiantEnabled = shouldShowGasGiant;
            }
        }

        if (volumeSpawner != null)
        {
            // Only toggle when needed; the spawner itself manages pooling and fade.
            bool isEnabled = volumeSpawner.IsVolumetricsEnabled;
            if (force || isEnabled != shouldShowGasGiant)
            {
                volumeSpawner.SetVolumetricsEnabled(shouldShowGasGiant);
            }
        }

        // Optional camera switching: enable the underwater camera when underwater view is active
        if (switchCameraOnLayerChange)
        {
            Camera surfCam = surfaceCameraOverride != null ? surfaceCameraOverride : Camera.main;

            // If we have an explicit underwater camera, prefer toggling cameras.
            if (underwaterCameraOverride != null)
            {
                // Avoid disabling the same camera if the user mistakenly assigned the same reference
                if (surfCam != null && surfCam != underwaterCameraOverride)
                {
                    surfCam.enabled = !underwaterVisible;
                }
                underwaterCameraOverride.enabled = underwaterVisible;
            }
            else if (surfCam != null)
            {
                // No underwater camera provided: optionally tweak main camera settings here in future.
                surfCam.enabled = true; // always keep surface camera enabled if no underwater camera exists
            }
        }

        // Toggle post-processing volumes if provided (use weight + enabled for smooth blending if desired)
        if (surfaceVolumeOverride != null || underwaterVolumeOverride != null)
        {
            // Simple hard switch: enable the active volume and disable the other
            if (underwaterVolumeOverride != null)
            {
                underwaterVolumeOverride.enabled = underwaterVisible;
                underwaterVolumeOverride.weight = underwaterVisible ? 1f : 0f;
            }
            if (surfaceVolumeOverride != null)
            {
                bool surfActive = !underwaterVisible;
                surfaceVolumeOverride.enabled = surfActive;
                surfaceVolumeOverride.weight = surfActive ? 1f : 0f;
            }
        }
    }

    private void ResolveReferencesIfNeeded()
    {
        if (planetGenerator == null) planetGenerator = GetComponent<PlanetGenerator>();
        if (terrainRendererOverride == null && planetGenerator != null && planetGenerator.terrainRenderer != null)
            terrainRendererOverride = planetGenerator.terrainRenderer;
        // Auto-assign surface camera from Camera.main when available (editor-friendly)
        if (surfaceCameraOverride == null)
        {
            try { surfaceCameraOverride = Camera.main; } catch { }
        }

        // Auto-find an underwater camera by name or by containing the word "Underwater"
        if (underwaterCameraOverride == null)
        {
            GameObject go = GameObject.Find("UnderwaterCamera");
            if (go != null) underwaterCameraOverride = go.GetComponent<Camera>();
            else
            {
                foreach (var cam in Camera.allCameras)
                {
                    if (cam == null) continue;
                    if (cam.name.IndexOf("Underwater", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        underwaterCameraOverride = cam;
                        break;
                    }
                }
            }
        }
    }

    private void OnValidate()
    {
        // Keep inspector friendly: try to auto-resolve common references while editing.
        ResolveReferencesIfNeeded();
    }

    private string GetPlanetNameForLogs()
    {
        if (planetGenerator == null) return "UnknownPlanet";
        if (planetGenerator.planetConfig != null && !string.IsNullOrEmpty(planetGenerator.planetConfig.planetName))
            return planetGenerator.planetConfig.planetName;
        return planetGenerator.name;
    }
}

