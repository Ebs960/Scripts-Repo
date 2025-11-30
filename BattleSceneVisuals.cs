using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Sets up AAA-quality visuals for the battle scene.
/// Configures lighting, post-processing, and terrain settings for optimal visual quality.
/// Attach to a GameObject in the battle scene (e.g., the BattleMapGenerator or a dedicated "VisualsManager").
/// </summary>
public class BattleSceneVisuals : MonoBehaviour
{
    [Header("Lighting Settings")]
    [Tooltip("Intensity of the main directional light (sun)")]
    [Range(0.5f, 3f)]
    public float sunIntensity = 1.2f;
    
    [Tooltip("Sun color - warm for day battles, cool for dawn/dusk")]
    public Color sunColor = new Color(1f, 0.96f, 0.88f); // Warm white
    
    [Tooltip("Ambient light intensity")]
    [Range(0f, 2f)]
    public float ambientIntensity = 1.0f;
    
    [Tooltip("Sky color for ambient lighting")]
    public Color skyColor = new Color(0.5f, 0.7f, 1f); // Light blue
    
    [Tooltip("Ground color for ambient lighting")]
    public Color groundColor = new Color(0.3f, 0.25f, 0.2f); // Earthy brown
    
    [Header("Shadow Settings")]
    [Tooltip("Shadow distance - how far shadows render")]
    [Range(50f, 500f)]
    public float shadowDistance = 150f;
    
    [Header("Post-Processing")]
    [Tooltip("Enable bloom effect")]
    public bool enableBloom = true;
    [Range(0f, 1f)]
    public float bloomIntensity = 0.3f;
    
    [Tooltip("Enable color grading")]
    public bool enableColorGrading = true;
    [Range(-1f, 1f)]
    public float contrast = 0.1f;
    [Range(-1f, 1f)]
    public float saturation = 0.1f;
    
    [Tooltip("Enable vignette")]
    public bool enableVignette = true;
    [Range(0f, 0.5f)]
    public float vignetteIntensity = 0.2f;
    
    [Tooltip("Enable depth of field (subtle for RTS games)")]
    public bool enableDOF = false;
    
    [Header("Fog Settings")]
    [Tooltip("Enable distance fog")]
    public bool enableFog = true;
    public Color fogColor = new Color(0.7f, 0.75f, 0.85f);
    [Range(100f, 1000f)]
    public float fogStartDistance = 200f;
    [Range(200f, 2000f)]
    public float fogEndDistance = 600f;
    
    [Header("References (Auto-created if null)")]
    public Light directionalLight;
    public Volume postProcessVolume;
    
    // Cached components
    private VolumeProfile volumeProfile;
    private Bloom bloom;
    private ColorAdjustments colorAdjustments;
    private Vignette vignette;
    private DepthOfField depthOfField;
    
    void Start()
    {
        SetupVisuals();
    }
    
    /// <summary>
    /// Call this to set up or refresh all visual settings
    /// </summary>
    public void SetupVisuals()
    {
        SetupLighting();
        SetupPostProcessing();
        SetupFog();
        ConfigureQualitySettings();
        
        Debug.Log("[BattleSceneVisuals] AAA visuals configured successfully");
    }
    
    /// <summary>
    /// Set up the main directional light (sun) with proper settings
    /// </summary>
    private void SetupLighting()
    {
        // Find or create directional light
        if (directionalLight == null)
        {
            directionalLight = FindFirstObjectByType<Light>();
            if (directionalLight == null || directionalLight.type != LightType.Directional)
            {
                GameObject sunGO = new GameObject("Sun_DirectionalLight");
                directionalLight = sunGO.AddComponent<Light>();
                directionalLight.type = LightType.Directional;
                Debug.Log("[BattleSceneVisuals] Created new directional light");
            }
        }
        
        // Configure directional light for AAA quality
        directionalLight.color = sunColor;
        directionalLight.intensity = sunIntensity;
        directionalLight.shadows = LightShadows.Soft;
        directionalLight.shadowStrength = 1f;
        directionalLight.shadowBias = 0.05f;
        directionalLight.shadowNormalBias = 0.4f;
        
        // Set sun angle for nice shadows (45 degrees from horizon, slightly angled)
        directionalLight.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
        
        // Configure ambient lighting
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = skyColor * ambientIntensity;
        RenderSettings.ambientEquatorColor = Color.Lerp(skyColor, groundColor, 0.5f) * ambientIntensity;
        RenderSettings.ambientGroundColor = groundColor * ambientIntensity;
        
        // Set reflection intensity
        RenderSettings.reflectionIntensity = 0.8f;
        
        Debug.Log($"[BattleSceneVisuals] Lighting configured - Sun intensity: {sunIntensity}, Ambient: {ambientIntensity}");
    }
    
    /// <summary>
    /// Set up post-processing volume with AAA effects
    /// </summary>
    private void SetupPostProcessing()
    {
        // Find or create Volume
        if (postProcessVolume == null)
        {
            postProcessVolume = FindFirstObjectByType<Volume>();
            if (postProcessVolume == null)
            {
                GameObject volumeGO = new GameObject("BattlePostProcessVolume");
                postProcessVolume = volumeGO.AddComponent<Volume>();
                postProcessVolume.isGlobal = true;
                postProcessVolume.priority = 1;
                Debug.Log("[BattleSceneVisuals] Created new post-processing volume");
            }
        }
        
        // Create or get volume profile
        if (postProcessVolume.profile == null)
        {
            volumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            postProcessVolume.profile = volumeProfile;
        }
        else
        {
            volumeProfile = postProcessVolume.profile;
        }
        
        // Configure Bloom
        if (enableBloom)
        {
            if (!volumeProfile.TryGet(out bloom))
            {
                bloom = volumeProfile.Add<Bloom>(true);
            }
            bloom.active = true;
            bloom.threshold.Override(0.9f);
            bloom.intensity.Override(bloomIntensity);
            bloom.scatter.Override(0.7f);
            bloom.tint.Override(new Color(1f, 0.95f, 0.9f)); // Slightly warm bloom
        }
        
        // Configure Color Adjustments
        if (enableColorGrading)
        {
            if (!volumeProfile.TryGet(out colorAdjustments))
            {
                colorAdjustments = volumeProfile.Add<ColorAdjustments>(true);
            }
            colorAdjustments.active = true;
            colorAdjustments.postExposure.Override(0.1f); // Slight exposure boost
            colorAdjustments.contrast.Override(contrast * 100f); // Unity uses -100 to 100
            colorAdjustments.saturation.Override(saturation * 100f);
            colorAdjustments.colorFilter.Override(new Color(1f, 0.98f, 0.95f)); // Very slight warm tint
        }
        
        // Configure Vignette
        if (enableVignette)
        {
            if (!volumeProfile.TryGet(out vignette))
            {
                vignette = volumeProfile.Add<Vignette>(true);
            }
            vignette.active = true;
            vignette.intensity.Override(vignetteIntensity);
            vignette.smoothness.Override(0.4f);
            vignette.rounded.Override(false);
        }
        
        // Configure Depth of Field (subtle for RTS)
        if (enableDOF)
        {
            if (!volumeProfile.TryGet(out depthOfField))
            {
                depthOfField = volumeProfile.Add<DepthOfField>(true);
            }
            depthOfField.active = true;
            depthOfField.mode.Override(DepthOfFieldMode.Gaussian);
            depthOfField.gaussianStart.Override(50f);
            depthOfField.gaussianEnd.Override(200f);
            depthOfField.gaussianMaxRadius.Override(0.5f);
        }
        
        // Configure Tonemapping for HDR
        if (!volumeProfile.TryGet(out Tonemapping tonemapping))
        {
            tonemapping = volumeProfile.Add<Tonemapping>(true);
        }
        tonemapping.active = true;
        tonemapping.mode.Override(TonemappingMode.ACES); // Film-like tonemapping
        
        Debug.Log("[BattleSceneVisuals] Post-processing configured with AAA effects");
    }
    
    /// <summary>
    /// Set up fog for atmospheric depth
    /// </summary>
    private void SetupFog()
    {
        RenderSettings.fog = enableFog;
        if (enableFog)
        {
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogStartDistance;
            RenderSettings.fogEndDistance = fogEndDistance;
        }
        
        Debug.Log($"[BattleSceneVisuals] Fog configured - Enabled: {enableFog}, Start: {fogStartDistance}, End: {fogEndDistance}");
    }
    
    /// <summary>
    /// Configure quality settings for best visuals
    /// </summary>
    private void ConfigureQualitySettings()
    {
        // Shadow distance
        QualitySettings.shadowDistance = shadowDistance;
        
        // Shadow cascades (4 for best quality)
        QualitySettings.shadowCascades = 4;
        
        // LOD bias (higher = more detail at distance)
        QualitySettings.lodBias = 1.5f;
        
        // Pixel light count
        QualitySettings.pixelLightCount = 4;
        
        // Texture quality
        QualitySettings.globalTextureMipmapLimit = 0; // Full resolution
        
        // Anisotropic filtering
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
        
        Debug.Log($"[BattleSceneVisuals] Quality settings configured - Shadow distance: {shadowDistance}");
    }
    
    /// <summary>
    /// Apply biome-specific visual adjustments for ALL biomes from BiomeHelper
    /// </summary>
    public void ApplyBiomeVisuals(Biome biome)
    {
        // Reset to defaults first
        ResetToDefaults();
        
        switch (biome)
        {
            // ==================== EARTH BIOMES ====================
            
            // === DESERT/HOT DRY BIOMES ===
            case Biome.Desert:
            case Biome.Scorched:
                // Hot, harsh desert lighting
                sunColor = new Color(1f, 0.9f, 0.7f);
                sunIntensity = 1.5f;
                skyColor = new Color(0.6f, 0.7f, 0.9f);
                groundColor = new Color(0.6f, 0.5f, 0.3f);
                fogColor = new Color(0.9f, 0.85f, 0.7f);
                fogStartDistance = 150f;
                fogEndDistance = 500f;
                saturation = 0.05f;
                contrast = 0.15f;
                break;
                
            case Biome.Savannah:
                // Warm, golden savannah lighting
                sunColor = new Color(1f, 0.92f, 0.75f);
                sunIntensity = 1.4f;
                skyColor = new Color(0.55f, 0.65f, 0.85f);
                groundColor = new Color(0.5f, 0.45f, 0.3f);
                fogColor = new Color(0.85f, 0.8f, 0.65f);
                saturation = 0.1f;
                contrast = 0.12f;
                break;
                
            case Biome.Steppe:
                // Dry, windswept steppes
                sunColor = new Color(1f, 0.95f, 0.85f);
                sunIntensity = 1.3f;
                skyColor = new Color(0.5f, 0.65f, 0.9f);
                groundColor = new Color(0.45f, 0.4f, 0.3f);
                fogColor = new Color(0.8f, 0.75f, 0.65f);
                saturation = 0.0f;
                contrast = 0.1f;
                break;
                
            // === PLAINS/GRASSLAND BIOMES ===
            case Biome.Plains:
            case Biome.Grassland:
                // Pleasant, neutral lighting
                sunColor = new Color(1f, 0.96f, 0.88f);
                sunIntensity = 1.2f;
                skyColor = new Color(0.5f, 0.7f, 1f);
                groundColor = new Color(0.35f, 0.4f, 0.25f);
                fogColor = new Color(0.7f, 0.75f, 0.85f);
                saturation = 0.1f;
                contrast = 0.1f;
                break;
                
            // === FOREST/JUNGLE BIOMES ===
            case Biome.Forest:
            case Biome.PineForest:
                // Dappled forest lighting
                sunColor = new Color(1f, 0.98f, 0.9f);
                sunIntensity = 1.1f;
                skyColor = new Color(0.4f, 0.6f, 0.8f);
                groundColor = new Color(0.25f, 0.35f, 0.2f);
                fogColor = new Color(0.55f, 0.7f, 0.6f);
                fogStartDistance = 80f;
                fogEndDistance = 350f;
                saturation = 0.15f;
                contrast = 0.1f;
                break;
                
            case Biome.Jungle:
            case Biome.Rainforest:
                // Dense, humid jungle lighting
                sunColor = new Color(0.95f, 1f, 0.9f);
                sunIntensity = 1.0f;
                skyColor = new Color(0.35f, 0.55f, 0.7f);
                groundColor = new Color(0.2f, 0.35f, 0.15f);
                fogColor = new Color(0.5f, 0.65f, 0.55f);
                fogStartDistance = 50f;
                fogEndDistance = 250f;
                saturation = 0.2f;
                contrast = 0.08f;
                break;
                
            case Biome.Taiga:
                // Cold northern forest lighting
                sunColor = new Color(0.95f, 0.98f, 1f);
                sunIntensity = 1.0f;
                skyColor = new Color(0.45f, 0.6f, 0.85f);
                groundColor = new Color(0.3f, 0.35f, 0.25f);
                fogColor = new Color(0.65f, 0.75f, 0.85f);
                fogStartDistance = 100f;
                fogEndDistance = 400f;
                saturation = 0.05f;
                contrast = 0.1f;
                break;
                
            // === SWAMP/MARSH BIOMES ===
            case Biome.Swamp:
            case Biome.Marsh:
            case Biome.Floodlands:
                // Murky, humid swamp lighting
                sunColor = new Color(0.85f, 0.9f, 0.8f);
                sunIntensity = 0.9f;
                skyColor = new Color(0.4f, 0.5f, 0.45f);
                groundColor = new Color(0.25f, 0.3f, 0.2f);
                fogColor = new Color(0.45f, 0.55f, 0.45f);
                fogStartDistance = 40f;
                fogEndDistance = 200f;
                saturation = -0.1f;
                contrast = 0.05f;
                vignetteIntensity = 0.3f;
                break;
                
            // === ICE/SNOW BIOMES ===
            case Biome.Snow:
            case Biome.Glacier:
                // Bright, cold snow lighting
                sunColor = new Color(0.95f, 0.98f, 1f);
                sunIntensity = 1.3f;
                skyColor = new Color(0.7f, 0.85f, 1f);
                groundColor = new Color(0.6f, 0.65f, 0.75f);
                fogColor = new Color(0.9f, 0.95f, 1f);
                fogStartDistance = 200f;
                fogEndDistance = 600f;
                saturation = -0.15f;
                contrast = 0.05f;
                bloomIntensity = 0.4f; // Snow glare
                break;
                
            case Biome.Tundra:
            case Biome.Frozen:
                // Cold, barren tundra lighting
                sunColor = new Color(0.9f, 0.95f, 1f);
                sunIntensity = 1.0f;
                skyColor = new Color(0.6f, 0.75f, 0.95f);
                groundColor = new Color(0.4f, 0.45f, 0.5f);
                fogColor = new Color(0.8f, 0.85f, 0.95f);
                saturation = -0.1f;
                contrast = 0.08f;
                break;
                
            case Biome.Arctic:
                // Extreme cold, blue-white lighting
                sunColor = new Color(0.85f, 0.92f, 1f);
                sunIntensity = 0.9f;
                skyColor = new Color(0.6f, 0.8f, 1f);
                groundColor = new Color(0.5f, 0.55f, 0.65f);
                fogColor = new Color(0.85f, 0.92f, 1f);
                fogStartDistance = 100f;
                fogEndDistance = 400f;
                saturation = -0.2f;
                contrast = 0.03f;
                bloomIntensity = 0.35f;
                break;
                
            case Biome.IcicleField:
            case Biome.CryoForest:
                // Crystalline ice world lighting
                sunColor = new Color(0.8f, 0.9f, 1f);
                sunIntensity = 1.1f;
                skyColor = new Color(0.5f, 0.7f, 1f);
                groundColor = new Color(0.4f, 0.5f, 0.7f);
                fogColor = new Color(0.7f, 0.85f, 1f);
                fogStartDistance = 80f;
                fogEndDistance = 350f;
                saturation = -0.05f;
                contrast = 0.1f;
                bloomIntensity = 0.45f; // Ice sparkle
                break;
                
            // === OCEAN/WATER BIOMES ===
            case Biome.Ocean:
            case Biome.Seas:
                // Deep ocean blue lighting
                sunColor = new Color(1f, 0.98f, 0.95f);
                sunIntensity = 1.3f;
                skyColor = new Color(0.4f, 0.6f, 0.9f);
                groundColor = new Color(0.15f, 0.25f, 0.4f);
                fogColor = new Color(0.5f, 0.7f, 0.9f);
                saturation = 0.15f;
                contrast = 0.1f;
                break;
                
            case Biome.Coast:
            case Biome.River:
                // Bright coastal lighting
                sunColor = new Color(1f, 0.98f, 0.92f);
                sunIntensity = 1.35f;
                skyColor = new Color(0.5f, 0.7f, 0.95f);
                groundColor = new Color(0.35f, 0.4f, 0.35f);
                fogColor = new Color(0.7f, 0.8f, 0.9f);
                saturation = 0.12f;
                contrast = 0.1f;
                break;
                
            // === MOUNTAIN BIOMES ===
            case Biome.Mountain:
                // Crisp, high-altitude lighting
                sunColor = new Color(1f, 0.98f, 0.95f);
                sunIntensity = 1.4f;
                skyColor = new Color(0.4f, 0.55f, 0.9f);
                groundColor = new Color(0.35f, 0.35f, 0.35f);
                fogColor = new Color(0.75f, 0.82f, 0.95f);
                fogStartDistance = 150f;
                fogEndDistance = 600f;
                saturation = 0.05f;
                contrast = 0.15f;
                break;
                
            // === VOLCANIC/HELLISH BIOMES ===
            case Biome.Volcanic:
            case Biome.Steam:
                // Hot volcanic lighting with glow
                sunColor = new Color(1f, 0.7f, 0.5f);
                sunIntensity = 1.2f;
                skyColor = new Color(0.5f, 0.35f, 0.3f);
                groundColor = new Color(0.3f, 0.15f, 0.1f);
                fogColor = new Color(0.5f, 0.35f, 0.25f);
                fogStartDistance = 60f;
                fogEndDistance = 300f;
                saturation = 0.15f;
                contrast = 0.18f;
                bloomIntensity = 0.5f;
                break;
                
            case Biome.Hellscape:
            case Biome.Brimstone:
                // Demonic hellfire lighting
                sunColor = new Color(1f, 0.5f, 0.3f);
                sunIntensity = 1.1f;
                skyColor = new Color(0.4f, 0.2f, 0.15f);
                groundColor = new Color(0.25f, 0.1f, 0.05f);
                fogColor = new Color(0.35f, 0.15f, 0.1f);
                fogStartDistance = 30f;
                fogEndDistance = 200f;
                saturation = 0.25f;
                contrast = 0.25f;
                bloomIntensity = 0.6f;
                vignetteIntensity = 0.35f;
                break;
                
            case Biome.Ashlands:
            case Biome.CharredForest:
                // Ashen, smoky lighting
                sunColor = new Color(0.9f, 0.75f, 0.6f);
                sunIntensity = 0.9f;
                skyColor = new Color(0.4f, 0.35f, 0.35f);
                groundColor = new Color(0.2f, 0.18f, 0.15f);
                fogColor = new Color(0.45f, 0.4f, 0.38f);
                fogStartDistance = 40f;
                fogEndDistance = 250f;
                saturation = -0.15f;
                contrast = 0.1f;
                vignetteIntensity = 0.25f;
                break;
                
            // === MOON BIOMES ===
            case Biome.MoonDunes:
            case Biome.MoonCaves:
                // Stark lunar lighting
                sunColor = new Color(1f, 1f, 1f);
                sunIntensity = 1.5f;
                skyColor = new Color(0.05f, 0.05f, 0.1f); // Almost black sky
                groundColor = new Color(0.3f, 0.3f, 0.32f);
                fogColor = new Color(0.2f, 0.2f, 0.25f);
                fogStartDistance = 200f;
                fogEndDistance = 800f;
                saturation = -0.3f;
                contrast = 0.25f;
                break;
                
            // ==================== MARS BIOMES ====================
            case Biome.MartianRegolith:
            case Biome.MartianDunes:
                // Red Martian dust
                sunColor = new Color(1f, 0.85f, 0.7f);
                sunIntensity = 1.2f;
                skyColor = new Color(0.7f, 0.5f, 0.4f); // Butterscotch sky
                groundColor = new Color(0.5f, 0.3f, 0.2f);
                fogColor = new Color(0.8f, 0.6f, 0.45f);
                fogStartDistance = 100f;
                fogEndDistance = 400f;
                saturation = 0.1f;
                contrast = 0.12f;
                break;
                
            case Biome.MartianCanyon:
                // Deep Martian canyon
                sunColor = new Color(1f, 0.8f, 0.65f);
                sunIntensity = 1.0f;
                skyColor = new Color(0.6f, 0.45f, 0.35f);
                groundColor = new Color(0.4f, 0.25f, 0.15f);
                fogColor = new Color(0.7f, 0.5f, 0.4f);
                fogStartDistance = 50f;
                fogEndDistance = 300f;
                saturation = 0.15f;
                contrast = 0.15f;
                break;
                
            case Biome.MartianPolarIce:
                // Mars polar ice caps
                sunColor = new Color(1f, 0.9f, 0.85f);
                sunIntensity = 1.1f;
                skyColor = new Color(0.65f, 0.55f, 0.5f);
                groundColor = new Color(0.7f, 0.65f, 0.6f);
                fogColor = new Color(0.85f, 0.75f, 0.7f);
                saturation = -0.05f;
                contrast = 0.1f;
                break;
                
            // ==================== VENUS BIOMES ====================
            case Biome.VenusLava:
                // Hellish Venus lava plains
                sunColor = new Color(1f, 0.6f, 0.3f);
                sunIntensity = 0.8f; // Thick atmosphere blocks light
                skyColor = new Color(0.6f, 0.4f, 0.2f);
                groundColor = new Color(0.4f, 0.2f, 0.1f);
                fogColor = new Color(0.7f, 0.45f, 0.25f);
                fogStartDistance = 20f;
                fogEndDistance = 150f;
                saturation = 0.2f;
                contrast = 0.2f;
                bloomIntensity = 0.6f;
                break;
                
            case Biome.VenusianPlains:
            case Biome.VenusHighlands:
                // Venus thick atmosphere
                sunColor = new Color(1f, 0.75f, 0.5f);
                sunIntensity = 0.7f;
                skyColor = new Color(0.7f, 0.5f, 0.3f);
                groundColor = new Color(0.45f, 0.35f, 0.25f);
                fogColor = new Color(0.75f, 0.55f, 0.35f);
                fogStartDistance = 30f;
                fogEndDistance = 180f;
                saturation = 0.1f;
                contrast = 0.1f;
                break;
                
            // ==================== MERCURY BIOMES ====================
            case Biome.MercuryCraters:
            case Biome.MercuryBasalt:
            case Biome.MercuryScarp:
                // Stark Mercury dayside
                sunColor = new Color(1f, 1f, 0.95f);
                sunIntensity = 2.0f; // Very bright sun
                skyColor = new Color(0.02f, 0.02f, 0.05f); // Black sky
                groundColor = new Color(0.35f, 0.32f, 0.3f);
                fogColor = new Color(0.15f, 0.15f, 0.18f);
                fogStartDistance = 300f;
                fogEndDistance = 1000f;
                saturation = -0.2f;
                contrast = 0.3f;
                bloomIntensity = 0.5f;
                break;
                
            case Biome.MercurianIce:
                // Mercury nightside ice
                sunColor = new Color(0.6f, 0.7f, 0.9f);
                sunIntensity = 0.4f;
                skyColor = new Color(0.02f, 0.03f, 0.08f);
                groundColor = new Color(0.2f, 0.25f, 0.35f);
                fogColor = new Color(0.15f, 0.2f, 0.3f);
                saturation = -0.15f;
                contrast = 0.15f;
                break;
                
            // ==================== GAS GIANT BIOMES ====================
            case Biome.JovianClouds:
            case Biome.JovianStorm:
                // Jupiter's turbulent atmosphere
                sunColor = new Color(0.95f, 0.9f, 0.85f);
                sunIntensity = 0.8f;
                skyColor = new Color(0.6f, 0.5f, 0.4f);
                groundColor = new Color(0.5f, 0.4f, 0.35f);
                fogColor = new Color(0.7f, 0.6f, 0.5f);
                fogStartDistance = 50f;
                fogEndDistance = 300f;
                saturation = 0.15f;
                contrast = 0.12f;
                break;
                
            case Biome.SaturnRings:
            case Biome.SaturnSurface:
                // Saturn's golden haze
                sunColor = new Color(1f, 0.95f, 0.8f);
                sunIntensity = 0.6f;
                skyColor = new Color(0.7f, 0.65f, 0.5f);
                groundColor = new Color(0.55f, 0.5f, 0.4f);
                fogColor = new Color(0.8f, 0.75f, 0.6f);
                fogStartDistance = 80f;
                fogEndDistance = 400f;
                saturation = 0.1f;
                contrast = 0.08f;
                break;
                
            // ==================== ICE GIANT BIOMES ====================
            case Biome.UranusIce:
            case Biome.UranusSurface:
                // Uranus cyan-blue atmosphere
                sunColor = new Color(0.85f, 0.95f, 1f);
                sunIntensity = 0.5f;
                skyColor = new Color(0.4f, 0.7f, 0.8f);
                groundColor = new Color(0.3f, 0.5f, 0.6f);
                fogColor = new Color(0.5f, 0.75f, 0.85f);
                fogStartDistance = 60f;
                fogEndDistance = 350f;
                saturation = 0.05f;
                contrast = 0.05f;
                break;
                
            case Biome.NeptuneWinds:
            case Biome.NeptuneIce:
            case Biome.NeptuneSurface:
                // Neptune deep blue
                sunColor = new Color(0.8f, 0.9f, 1f);
                sunIntensity = 0.4f;
                skyColor = new Color(0.2f, 0.4f, 0.7f);
                groundColor = new Color(0.15f, 0.3f, 0.5f);
                fogColor = new Color(0.3f, 0.5f, 0.75f);
                fogStartDistance = 40f;
                fogEndDistance = 300f;
                saturation = 0.1f;
                contrast = 0.08f;
                break;
                
            // ==================== DWARF PLANET/MOON BIOMES ====================
            case Biome.PlutoCryo:
            case Biome.PlutoTholins:
            case Biome.PlutoMountains:
                // Pluto's frozen twilight
                sunColor = new Color(0.9f, 0.92f, 1f);
                sunIntensity = 0.3f; // Very distant sun
                skyColor = new Color(0.1f, 0.12f, 0.2f);
                groundColor = new Color(0.35f, 0.3f, 0.28f);
                fogColor = new Color(0.25f, 0.25f, 0.35f);
                fogStartDistance = 100f;
                fogEndDistance = 500f;
                saturation = -0.1f;
                contrast = 0.12f;
                break;
                
            case Biome.TitanLakes:
            case Biome.TitanDunes:
            case Biome.TitanIce:
                // Titan's orange haze
                sunColor = new Color(0.9f, 0.7f, 0.5f);
                sunIntensity = 0.5f;
                skyColor = new Color(0.6f, 0.45f, 0.3f);
                groundColor = new Color(0.4f, 0.3f, 0.2f);
                fogColor = new Color(0.7f, 0.55f, 0.4f);
                fogStartDistance = 30f;
                fogEndDistance = 200f;
                saturation = 0.1f;
                contrast = 0.08f;
                break;
                
            case Biome.EuropaIce:
            case Biome.EuropaRidges:
                // Europa's icy surface
                sunColor = new Color(0.95f, 0.98f, 1f);
                sunIntensity = 0.7f;
                skyColor = new Color(0.1f, 0.15f, 0.25f);
                groundColor = new Color(0.5f, 0.55f, 0.65f);
                fogColor = new Color(0.4f, 0.5f, 0.65f);
                fogStartDistance = 150f;
                fogEndDistance = 600f;
                saturation = -0.1f;
                contrast = 0.1f;
                bloomIntensity = 0.35f;
                break;
                
            case Biome.IoVolcanic:
            case Biome.IoSulfur:
                // Io's volcanic hellscape
                sunColor = new Color(1f, 0.8f, 0.5f);
                sunIntensity = 1.0f;
                skyColor = new Color(0.3f, 0.25f, 0.15f);
                groundColor = new Color(0.6f, 0.5f, 0.2f); // Sulfur yellow
                fogColor = new Color(0.5f, 0.4f, 0.2f);
                fogStartDistance = 40f;
                fogEndDistance = 250f;
                saturation = 0.25f;
                contrast = 0.2f;
                bloomIntensity = 0.55f;
                break;
                
            // ==================== DEFAULT ====================
            default:
                // Neutral Earth-like lighting
                sunColor = new Color(1f, 0.96f, 0.88f);
                sunIntensity = 1.2f;
                skyColor = new Color(0.5f, 0.7f, 1f);
                groundColor = new Color(0.3f, 0.25f, 0.2f);
                fogColor = new Color(0.7f, 0.75f, 0.85f);
                saturation = 0.1f;
                contrast = 0.1f;
                break;
        }
        
        // Apply the biome-specific settings
        SetupVisuals();
        
        Debug.Log($"[BattleSceneVisuals] Applied biome-specific visuals for: {biome}");
    }
    
    /// <summary>
    /// Reset all visual settings to defaults before applying biome-specific overrides
    /// </summary>
    private void ResetToDefaults()
    {
        sunIntensity = 1.2f;
        sunColor = new Color(1f, 0.96f, 0.88f);
        ambientIntensity = 1.0f;
        skyColor = new Color(0.5f, 0.7f, 1f);
        groundColor = new Color(0.3f, 0.25f, 0.2f);
        shadowDistance = 150f;
        enableBloom = true;
        bloomIntensity = 0.3f;
        enableColorGrading = true;
        contrast = 0.1f;
        saturation = 0.1f;
        enableVignette = true;
        vignetteIntensity = 0.2f;
        enableFog = true;
        fogColor = new Color(0.7f, 0.75f, 0.85f);
        fogStartDistance = 200f;
        fogEndDistance = 600f;
    }
    
    /// <summary>
    /// Set time of day (affects lighting)
    /// </summary>
    public void SetTimeOfDay(float hour)
    {
        // hour: 0-24
        float normalizedTime = hour / 24f;
        
        // Calculate sun angle based on time
        float sunAngle = (normalizedTime - 0.25f) * 360f; // 6AM = horizon
        
        if (directionalLight != null)
        {
            directionalLight.transform.rotation = Quaternion.Euler(sunAngle, -30f, 0f);
            
            // Adjust intensity and color based on time
            if (hour >= 6f && hour <= 18f)
            {
                // Daytime
                float dayProgress = (hour - 6f) / 12f;
                float intensity = Mathf.Sin(dayProgress * Mathf.PI); // Peak at noon
                sunIntensity = 0.8f + intensity * 0.7f;
                
                // Color: warm at dawn/dusk, neutral at noon
                float warmth = 1f - Mathf.Abs(dayProgress - 0.5f) * 2f;
                sunColor = Color.Lerp(new Color(1f, 0.7f, 0.5f), new Color(1f, 0.98f, 0.95f), warmth);
            }
            else
            {
                // Nighttime (dim blue light)
                sunIntensity = 0.3f;
                sunColor = new Color(0.5f, 0.6f, 0.8f);
            }
            
            directionalLight.intensity = sunIntensity;
            directionalLight.color = sunColor;
        }
        
        Debug.Log($"[BattleSceneVisuals] Time of day set to {hour}:00 - Sun angle: {sunAngle}°");
    }
    
    // Editor helper to preview changes
    void OnValidate()
    {
        if (Application.isPlaying && directionalLight != null)
        {
            SetupVisuals();
        }
    }
}

