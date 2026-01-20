using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Manages post-processing effects for the hex map view.
/// Creates and configures URP Volume with bloom, color grading, vignette, and more.
/// Attach to any GameObject or let HexMapChunkManager reference it.
/// </summary>
[ExecuteAlways]
public class HexMapPostProcessing : MonoBehaviour
{
    public static HexMapPostProcessing Instance { get; private set; }
    
    [Header("Lighting")]
    [Tooltip("Directional sun light for the campaign map. If null, one will be created.")]
    public Light directionalLight;
    [Tooltip("Intensity of the main directional light (sun)")]
    public float sunIntensity = 1.2f;
    [Tooltip("Sun color - warm for day maps")]
    public Color sunColor = new Color(1f, 0.96f, 0.88f);
    [Tooltip("Ambient light intensity")]
    [Range(0f, 2f)] public float ambientIntensity = 1.0f;
    [Tooltip("Sky color for ambient lighting")]
    public Color skyColor = new Color(0.5f, 0.7f, 1f);
    [Tooltip("Ground color for ambient lighting")]
    public Color groundColor = new Color(0.3f, 0.25f, 0.2f);
    
    [Header("Volume Settings")]
    [SerializeField] private bool createVolumeOnStart = true;
    [SerializeField] private float blendDistance = 0f; // 0 = global
    
    [Header("Bloom")]
    [SerializeField] private bool enableBloom = true;
    [SerializeField, Range(0f, 10f)] private float bloomIntensity = 0.8f;
    [SerializeField, Range(0f, 1f)] private float bloomThreshold = 0.9f;
    [SerializeField, Range(0f, 1f)] private float bloomScatter = 0.7f;
    [SerializeField] private Color bloomTint = Color.white;
    
    [Header("Color Adjustments")]
    [SerializeField] private bool enableColorAdjustments = true;
    [SerializeField, Range(-100f, 100f)] private float postExposure = 0.2f;
    [SerializeField, Range(-100f, 100f)] private float contrast = 10f;
    [SerializeField, Range(-100f, 100f)] private float saturation = 10f;
    [SerializeField] private Color colorFilter = Color.white;
    
    [Header("Vignette")]
    [SerializeField] private bool enableVignette = true;
    [SerializeField, Range(0f, 1f)] private float vignetteIntensity = 0.25f;
    [SerializeField, Range(0f, 1f)] private float vignetteSmoothness = 0.4f;
    [SerializeField] private Color vignetteColor = Color.black;
    
    [Header("Film Grain (subtle)")]
    [SerializeField] private bool enableFilmGrain = false;
    [SerializeField, Range(0f, 1f)] private float filmGrainIntensity = 0.1f;
    
    [Header("Depth of Field")]
    [SerializeField] private bool enableDOF = false;
    [SerializeField] private float focusDistance = 50f;
    [SerializeField, Range(1f, 32f)] private float aperture = 5.6f;
    [SerializeField, Range(1f, 300f)] private float focalLength = 50f;
    
    [Header("Chromatic Aberration (subtle)")]
    [SerializeField] private bool enableChromaticAberration = false;
    [SerializeField, Range(0f, 1f)] private float chromaticIntensity = 0.05f;
    
    [Header("Tonemapping")]
    [SerializeField] private bool enableTonemapping = true;
    [SerializeField] private TonemappingMode tonemappingMode = TonemappingMode.ACES;

    [Header("Cinematic Extras")]
    [SerializeField] private bool enableSplitToning = true;
    [SerializeField] private Color splitShadowsTint = new Color(0.2f, 0.3f, 0.4f);
    [SerializeField] private Color splitHighlightsTint = new Color(1f, 0.95f, 0.85f);
    [SerializeField, Range(-100f, 100f)] private float splitToningBalance = 0f;
    [SerializeField] private bool enableMotionBlur = false;
    [SerializeField, Range(0f, 1f)] private float motionBlurIntensity = 0.3f;
    [SerializeField] private bool enableLensDistortion = false;
    [SerializeField, Range(-1f, 1f)] private float lensDistortionIntensity = -0.1f;
    
    [Header("White Balance")]
    [SerializeField] private bool enableWhiteBalance = false;
    [SerializeField, Range(-100f, 100f)] private float temperature = 0f;
    [SerializeField, Range(-100f, 100f)] private float tint = 0f;
    
    // Internal
    private Volume volume;
    private VolumeProfile profile;
    private Bloom bloom;
    private ColorAdjustments colorAdjustments;
    private Vignette vignette;
    private FilmGrain filmGrain;
    private DepthOfField depthOfField;
    private ChromaticAberration chromaticAberration;
    private Tonemapping tonemapping;
    private WhiteBalance whiteBalance;
    private SplitToning splitToning;
    private MotionBlur motionBlur;
    private LensDistortion lensDistortion;
    
    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }
    
    private void Start()
    {
        if (createVolumeOnStart)
        {
            SetupVolume();
            ApplyAllSettings();
            SetupLighting();
        }
    }
    
    private void OnValidate()
    {
        // Apply changes in editor
        if (profile != null)
        {
            ApplyAllSettings();
            SetupLighting();
        }
    }

    /// <summary>
    /// Configure directional light and ambient settings to match battlefield visuals.
    /// Creates a directional light if none is present and applies ambient colors.
    /// </summary>
    public void SetupLighting()
    {
        // Find or create a directional light
        if (directionalLight == null)
        {
            var existing = UnityEngine.Object.FindAnyObjectByType<Light>();
            if (existing != null && existing.type == LightType.Directional)
            {
                directionalLight = existing;
            }
            else
            {
                GameObject sunGO = new GameObject("HexMap_Sun_DirectionalLight");
                directionalLight = sunGO.AddComponent<Light>();
                directionalLight.type = LightType.Directional;
            }
        }

        if (directionalLight != null)
        {
            directionalLight.color = sunColor;
            directionalLight.intensity = sunIntensity;
            directionalLight.shadows = LightShadows.Soft;
            directionalLight.shadowStrength = 1f;
            directionalLight.shadowBias = 0.05f;
            directionalLight.shadowNormalBias = 0.4f;
            directionalLight.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
        }

        // Ambient lighting (Trilight for nicer results)
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = skyColor * ambientIntensity;
        RenderSettings.ambientEquatorColor = Color.Lerp(skyColor, groundColor, 0.5f) * ambientIntensity;
        RenderSettings.ambientGroundColor = groundColor * ambientIntensity;
        RenderSettings.reflectionIntensity = 0.8f;

        // Also attempt to tint the map and atmosphere materials so post-processing/toning
        // visually affects the flat map and the atmosphere shader as well.
        ApplyMapAndAtmosphereToning();
    }

    private void ApplyMapAndAtmosphereToning()
    {
        // Tint the chunk manager shared material (map texture shader uses _Color)
        var chunkManager = UnityEngine.Object.FindAnyObjectByType<HexMapChunkManager>();
        if (chunkManager != null)
        {
            var mat = chunkManager.SharedMaterial;
            if (mat != null)
            {
                // Apply a tint derived from our color filter and slight exposure
                float exposureMul = 1f + postExposure * 0.15f;
                Color tint = colorFilter * exposureMul;
                mat.SetColor("_Color", tint);
            }
        }

        // Find any atmosphere materials (shader: Custom/PlanetAtmosphereURP) and tint them
        var allMats = Resources.FindObjectsOfTypeAll<Material>();
        foreach (var m in allMats)
        {
            if (m == null || m.shader == null) continue;
            if (m.shader.name == "Custom/PlanetAtmosphereURP")
            {
                // Use skyColor as base atmosphere tint, influenced by exposure
                float exposureMul = 1f + postExposure * 0.15f;
                Color atm = skyColor * exposureMul;
                m.SetColor("_AtmosphereColor", atm);
            }
        }
    }
    
    /// <summary>
    /// Create or find the Volume component and set up all effects.
    /// </summary>
    public void SetupVolume()
    {
        // Try to find existing volume on this object
        volume = GetComponent<Volume>();
        if (volume == null)
        {
            volume = gameObject.AddComponent<Volume>();
        }
        
        // Create new profile if needed
        if (volume.profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "HexMapPostProcessProfile";
            volume.profile = profile;
        }
        else
        {
            profile = volume.profile;
        }
        
        // Make it global
        volume.isGlobal = true;
        volume.weight = 1f;
        volume.priority = 1f;
        volume.blendDistance = blendDistance;
        
        // Add/get all effect overrides
        bloom = GetOrAddOverride<Bloom>();
        colorAdjustments = GetOrAddOverride<ColorAdjustments>();
        vignette = GetOrAddOverride<Vignette>();
        filmGrain = GetOrAddOverride<FilmGrain>();
        depthOfField = GetOrAddOverride<DepthOfField>();
        chromaticAberration = GetOrAddOverride<ChromaticAberration>();
        splitToning = GetOrAddOverride<SplitToning>();
        motionBlur = GetOrAddOverride<MotionBlur>();
        lensDistortion = GetOrAddOverride<LensDistortion>();
        tonemapping = GetOrAddOverride<Tonemapping>();
        whiteBalance = GetOrAddOverride<WhiteBalance>();
}
    
    private T GetOrAddOverride<T>() where T : VolumeComponent
    {
        if (!profile.TryGet(out T component))
        {
            component = profile.Add<T>(true);
        }
        return component;
    }
    
    /// <summary>
    /// Apply all current settings to the volume profile.
    /// </summary>
    public void ApplyAllSettings()
    {
        ApplyBloom();
        ApplyColorAdjustments();
        ApplyVignette();
        ApplyFilmGrain();
        ApplyDepthOfField();
        ApplyChromaticAberration();
        ApplySplitToning();
        ApplyMotionBlur();
        ApplyLensDistortion();
        ApplyTonemapping();
        ApplyWhiteBalance();
    }

    private void ApplySplitToning()
    {
        if (splitToning == null) return;
        splitToning.active = enableSplitToning;
        splitToning.shadows.overrideState = true;
        splitToning.shadows.value = splitShadowsTint;
        splitToning.highlights.overrideState = true;
        splitToning.highlights.value = splitHighlightsTint;
        splitToning.balance.overrideState = true;
        splitToning.balance.value = splitToningBalance;
    }

    private void ApplyMotionBlur()
    {
        if (motionBlur == null) return;
        motionBlur.active = enableMotionBlur;
        motionBlur.intensity.overrideState = true;
        motionBlur.intensity.value = motionBlurIntensity;
        // Quality may not exist on all MotionBlur variants; guard if present
        try { motionBlur.quality.overrideState = true; motionBlur.quality.value = MotionBlurQuality.High; } catch { }
    }

    private void ApplyLensDistortion()
    {
        if (lensDistortion == null) return;
        lensDistortion.active = enableLensDistortion;
        lensDistortion.intensity.overrideState = true;
        lensDistortion.intensity.value = lensDistortionIntensity;
        lensDistortion.scale.overrideState = true;
        lensDistortion.scale.value = 1f;
    }
    
    private void ApplyBloom()
    {
        if (bloom == null) return;
        
        bloom.active = enableBloom;
        bloom.intensity.overrideState = true;
        bloom.intensity.value = bloomIntensity;
        bloom.threshold.overrideState = true;
        bloom.threshold.value = bloomThreshold;
        bloom.scatter.overrideState = true;
        bloom.scatter.value = bloomScatter;
        bloom.tint.overrideState = true;
        bloom.tint.value = bloomTint;
    }
    
    private void ApplyColorAdjustments()
    {
        if (colorAdjustments == null) return;
        
        colorAdjustments.active = enableColorAdjustments;
        colorAdjustments.postExposure.overrideState = true;
        colorAdjustments.postExposure.value = postExposure;
        colorAdjustments.contrast.overrideState = true;
        colorAdjustments.contrast.value = contrast;
        colorAdjustments.saturation.overrideState = true;
        colorAdjustments.saturation.value = saturation;
        colorAdjustments.colorFilter.overrideState = true;
        colorAdjustments.colorFilter.value = colorFilter;
    }
    
    private void ApplyVignette()
    {
        if (vignette == null) return;
        
        vignette.active = enableVignette;
        vignette.intensity.overrideState = true;
        vignette.intensity.value = vignetteIntensity;
        vignette.smoothness.overrideState = true;
        vignette.smoothness.value = vignetteSmoothness;
        vignette.color.overrideState = true;
        vignette.color.value = vignetteColor;
    }
    
    private void ApplyFilmGrain()
    {
        if (filmGrain == null) return;
        
        filmGrain.active = enableFilmGrain;
        filmGrain.intensity.overrideState = true;
        filmGrain.intensity.value = filmGrainIntensity;
        filmGrain.type.overrideState = true;
        filmGrain.type.value = FilmGrainLookup.Medium1;
    }
    
    private void ApplyDepthOfField()
    {
        if (depthOfField == null) return;
        
        depthOfField.active = enableDOF;
        depthOfField.mode.overrideState = true;
        depthOfField.mode.value = DepthOfFieldMode.Bokeh;
        depthOfField.focusDistance.overrideState = true;
        depthOfField.focusDistance.value = focusDistance;
        depthOfField.aperture.overrideState = true;
        depthOfField.aperture.value = aperture;
        depthOfField.focalLength.overrideState = true;
        depthOfField.focalLength.value = focalLength;
    }
    
    private void ApplyChromaticAberration()
    {
        if (chromaticAberration == null) return;
        
        chromaticAberration.active = enableChromaticAberration;
        chromaticAberration.intensity.overrideState = true;
        chromaticAberration.intensity.value = chromaticIntensity;
    }
    
    private void ApplyTonemapping()
    {
        if (tonemapping == null) return;
        
        tonemapping.active = enableTonemapping;
        tonemapping.mode.overrideState = true;
        tonemapping.mode.value = tonemappingMode;
    }
    
    private void ApplyWhiteBalance()
    {
        if (whiteBalance == null) return;
        
        whiteBalance.active = enableWhiteBalance;
        whiteBalance.temperature.overrideState = true;
        whiteBalance.temperature.value = temperature;
        whiteBalance.tint.overrideState = true;
        whiteBalance.tint.value = tint;
    }
    
    #region Runtime API
    
    /// <summary>
    /// Set bloom intensity at runtime.
    /// </summary>
    public void SetBloom(bool enabled, float intensity = 0.8f, float threshold = 0.9f)
    {
        enableBloom = enabled;
        bloomIntensity = intensity;
        bloomThreshold = threshold;
        ApplyBloom();
    }
    
    /// <summary>
    /// Set color grading at runtime.
    /// </summary>
    public void SetColorGrading(float exposure = 0f, float contrast = 0f, float saturation = 0f)
    {
        enableColorAdjustments = true;
        postExposure = exposure;
        this.contrast = contrast;
        this.saturation = saturation;
        ApplyColorAdjustments();
    }
    
    /// <summary>
    /// Set vignette at runtime.
    /// </summary>
    public void SetVignette(bool enabled, float intensity = 0.25f)
    {
        enableVignette = enabled;
        vignetteIntensity = intensity;
        ApplyVignette();
    }
    
    /// <summary>
    /// Set depth of field at runtime.
    /// </summary>
    public void SetDepthOfField(bool enabled, float focus = 50f)
    {
        enableDOF = enabled;
        focusDistance = focus;
        ApplyDepthOfField();
    }
    
    /// <summary>
    /// Apply a warm/golden hour look.
    /// </summary>
    public void ApplyGoldenHourPreset()
    {
        SetBloom(true, 1.2f, 0.8f);
        SetColorGrading(0.3f, 15f, 20f);
        enableWhiteBalance = true;
        temperature = 30f;
        tint = 10f;
        ApplyWhiteBalance();
        SetVignette(true, 0.3f);
}
    
    /// <summary>
    /// Apply a cool/night look.
    /// </summary>
    public void ApplyNightPreset()
    {
        SetBloom(true, 0.5f, 0.95f);
        SetColorGrading(-0.5f, 20f, -20f);
        enableWhiteBalance = true;
        temperature = -30f;
        tint = 0f;
        ApplyWhiteBalance();
        SetVignette(true, 0.4f);
}
    
    /// <summary>
    /// Apply a vibrant/colorful look.
    /// </summary>
    public void ApplyVibrantPreset()
    {
        SetBloom(true, 1.0f, 0.85f);
        SetColorGrading(0.2f, 20f, 30f);
        enableWhiteBalance = false;
        ApplyWhiteBalance();
        SetVignette(true, 0.2f);
}
    
    /// <summary>
    /// Apply a cinematic look.
    /// </summary>
    public void ApplyCinematicPreset()
    {
        SetBloom(true, 0.6f, 0.9f);
        SetColorGrading(0.1f, 25f, -5f);
        enableWhiteBalance = true;
        temperature = -10f;
        tint = 5f;
        ApplyWhiteBalance();
        SetVignette(true, 0.35f);
        enableFilmGrain = true;
        filmGrainIntensity = 0.15f;
        ApplyFilmGrain();
}
    
    /// <summary>
    /// Reset to default balanced look.
    /// </summary>
    public void ApplyDefaultPreset()
    {
        SetBloom(true, 0.8f, 0.9f);
        SetColorGrading(0.2f, 10f, 10f);
        enableWhiteBalance = false;
        ApplyWhiteBalance();
        SetVignette(true, 0.25f);
        enableFilmGrain = false;
        ApplyFilmGrain();
}
    
    #endregion
    
    #region Editor Helpers
    
#if UNITY_EDITOR
    [ContextMenu("Setup Volume Now")]
    private void EditorSetupVolume()
    {
        SetupVolume();
        ApplyAllSettings();
    }
    
    [ContextMenu("Apply Golden Hour Preset")]
    private void EditorGoldenHour() => ApplyGoldenHourPreset();
    
    [ContextMenu("Apply Night Preset")]
    private void EditorNight() => ApplyNightPreset();
    
    [ContextMenu("Apply Vibrant Preset")]
    private void EditorVibrant() => ApplyVibrantPreset();
    
    [ContextMenu("Apply Cinematic Preset")]
    private void EditorCinematic() => ApplyCinematicPreset();
    
    [ContextMenu("Apply Default Preset")]
    private void EditorDefault() => ApplyDefaultPreset();
#endif
    
    #endregion
}
