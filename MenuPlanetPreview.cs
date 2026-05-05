using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using System.Collections.Generic;

/// <summary>
/// Visual-only planet preview for the Main Menu / Game Setup UI.
/// Displays a slowly rotating sphere with procedural land/ocean blobs
/// that respond instantly to land type, temperature, and moisture sliders.
///
/// This system has ZERO coupling to gameplay code — no PlanetGenerator,
/// no GameManager, no seeds, no tile logic. It only sets material properties
/// on a custom HDRP shader (Custom/MenuPlanetPreview).
///
/// Hierarchy created at runtime:
///   MenuPlanetPreview (this script)
///     └── PreviewSphere   (MeshFilter + MeshRenderer — planet surface)
///     └── _CloudShell      (MeshFilter + MeshRenderer — animated clouds)
///     └── _AtmosphereShell (MeshFilter + MeshRenderer — rim glow)
/// </summary>
public class MenuPlanetPreview : MonoBehaviour
{
    // -----------------------------------------------------------------
    //  Inspector References
    // -----------------------------------------------------------------
    [Header("References")]
    [Tooltip("The MeshRenderer on the preview sphere child. " +
             "If left empty, searches children on Awake.")]
    [SerializeField] private MeshRenderer previewRenderer;

    [Tooltip("The shader to use. If empty, finds 'Custom/MenuPlanetPreview' at runtime.")]
    [SerializeField] private Shader previewShader;

    [Tooltip("Cloud layer shader. If empty, finds 'Custom/MenuPlanetClouds' at runtime.")]
    [SerializeField] private Shader cloudShader;

    [Tooltip("Atmosphere shell shader. If empty, finds 'Custom/MenuPlanetAtmosphere' at runtime.")]
    [SerializeField] private Shader atmosphereShader;

    [Tooltip("Directional light illuminating the preview. Auto-found in children if null.")]
    [SerializeField] private Light previewLight;
    [Tooltip("Preview camera used for background and post-processing. Auto-found in children if null.")]
    [SerializeField] private Camera previewCamera;

    // -----------------------------------------------------------------
    //  Rotation
    // -----------------------------------------------------------------
    [Header("Rotation")]
    [Tooltip("Degrees per second the sphere rotates around its local Y axis.")]
    [SerializeField] private float rotationSpeed = 5f;

    [Header("Detail & Atmosphere")]
    [Tooltip("Higher values add high-frequency surface detail driven in-shader.")]
    [SerializeField] private float detailScale = 18f;
    [Tooltip("Strength of normal/detail perturbation.")]
    [Range(0f,1f)] [SerializeField] private float detailStrength = 0.18f;

    [Tooltip("Atmosphere tint color for the atmosphere shell.")]
    [SerializeField] private Color atmosphereColor = new Color(0.62f, 0.78f, 0.95f, 1f);

    [Header("Displacement")]
    [Tooltip("How far land vertices protrude outward (fraction of radius). 0 = flat sphere.")]
    [Range(0f, 0.15f)] [SerializeField] private float displacementScale = 0.035f;

    [Header("Clouds")]
    [SerializeField] private bool enableCloudShell = true;
    [Tooltip("Cloud altitude above planet surface.")]
    [Range(0f, 0.1f)] [SerializeField] private float cloudAltitude = 0.018f;
    [Tooltip("Cloud coverage density.")]
    [Range(0f, 1f)] [SerializeField] private float cloudDensity = 0.55f;
    [Tooltip("Cloud noise scale.")]
    [SerializeField] private float cloudScale = 3.0f;
    [Tooltip("Cloud animation speed.")]
    [SerializeField] private float cloudSpeed = 0.08f;
    [SerializeField] private float cloudRotationMultiplier = 1.25f;

    [Header("Atmosphere Shell")]
    [SerializeField] private bool enableAtmosphereShell = true;
    [Tooltip("Scale multiplier for the atmosphere shell mesh.")]
    [Range(1.01f, 1.15f)] [SerializeField] private float atmosphereShellScale = 1.06f;
    [Tooltip("Fresnel falloff exponent for atmospheric rim glow.")]
    [Range(1f, 8f)] [SerializeField] private float atmosphereFalloff = 3.5f;
    [Tooltip("Brightness multiplier for the atmosphere glow.")]
    [Range(0f, 3f)] [SerializeField] private float atmosphereIntensity = 1.2f;
    [SerializeField] private float atmosphereRotationMultiplier = 0f;

    [Header("Surface Detail Textures")]
    [SerializeField] private Texture2D landDetailTexture;
    [SerializeField] private Texture2D mountainDetailTexture;
    [SerializeField] private Texture2D iceDetailTexture;
    [SerializeField] private Texture2D oceanDetailTexture;
    [SerializeField] private Texture2D oceanNormalTexture;
    [SerializeField] private Texture2D roughnessDetailTexture;

    [Header("Texture Detail Strengths")]
    [SerializeField, Range(0f, 1f)] private float landDetailStrength = 0.18f;
    [SerializeField, Range(0f, 1f)] private float mountainDetailStrength = 0.22f;
    [SerializeField, Range(0f, 1f)] private float iceDetailStrength = 0.12f;
    [SerializeField, Range(0f, 1f)] private float oceanDetailStrength = 0.15f;
    [SerializeField, Range(0f, 1f)] private float oceanNormalStrength = 0.35f;
    [SerializeField, Range(0.1f, 30f)] private float textureDetailScale = 8f;

    [Header("HDRP Post-Processing")]
    [Tooltip("Enable bloom on the preview camera for emissive glow (lava, specular).")]
    [SerializeField] private bool enableBloom = true;
    [Tooltip("Bloom threshold — only pixels brighter than this bloom.")]
    [SerializeField] private float bloomThreshold = 0.8f;
    [Tooltip("Bloom intensity.")]
    [Range(0f, 2f)] [SerializeField] private float bloomIntensity = 0.9f;

    [Header("Mesh Quality")]
    [Tooltip("Subdivisions for generated icosphere. Higher = smoother displacement. 0-6 (6 ≈ 40k tris).")]
    [Range(0,20)] [SerializeField] private int icosphereSubdivisions = 5;

    // -----------------------------------------------------------------
    //  Preview Parameters (exposed in inspector for quick iteration)
    // -----------------------------------------------------------------
    [Header("Land Shape")]
    [Range(0.5f, 5f)]
    [Tooltip("Controls blob frequency. Low = pangaea, High = archipelago.")]
    [SerializeField] private float landScale = 2f;

    [Range(0f, 1f)]
    [Tooltip("Controls land vs ocean ratio. Low = more land, High = more ocean.")]
    [SerializeField] private float landThreshold = 0.4f;

    [Header("Climate")]
    [Range(0f, 1f)]
    [Tooltip("0 = frozen / icy,  0.5 = temperate / green,  1 = hot / arid.")]
    [SerializeField] private float temperature = 0.5f;

    [Range(0f, 1f)]
    [Tooltip("0 = dry / brown,  1 = wet / lush green.")]
    [SerializeField] private float moisture = 0.5f;

    [Header("Terrain")]
    [Range(0f, 1f)]
    [Tooltip("0 = flat lowlands,  0.5 = hilly,  1 = extreme mountains with snow peaks.")]
    [SerializeField] private float elevation = 0.3f;

    [Header("Map Style")]
    [Range(0f, 1f)]
    [Tooltip("0 = normal world,  1 = infernal/demonic (lava oceans, charred land, volcanic glow, hellish rim).")]
    [SerializeField] private float mapStyle = 0f;

    [Header("Biome Zone Colors")]
    [Tooltip("Equatorial jungle/rainforest color.")]
    [SerializeField] private Color equatorialColor = new Color(0.01f, 0.30f, 0.04f, 1f);

    [Tooltip("Sandy desert color used for dry equatorial regions.")]
    [SerializeField] private Color desertSand = new Color(0.96f, 0.89f, 0.65f, 1f);

    [Tooltip("Subtropical savanna/golden grass color.")]
    [SerializeField] private Color subtropicalColor = new Color(0.82f, 0.70f, 0.25f, 1f);

    [Tooltip("Temperate grassland/forest color.")]
    [SerializeField] private Color temperateZoneColor = new Color(0.14f, 0.68f, 0.12f, 1f);

    [Tooltip("Boreal dark conifer color.")]
    [SerializeField] private Color borealColor = new Color(0.06f, 0.25f, 0.10f, 1f);

    [Tooltip("Tundra barren gray-brown color.")]
    [SerializeField] private Color tundraColor = new Color(0.58f, 0.50f, 0.38f, 1f);

    [Tooltip("Polar ice/snow color.")]
    [SerializeField] private Color polarColor = new Color(0.93f, 0.95f, 0.97f, 1f);

    [Header("Ocean Color")]
    [Tooltip("Ocean color.")]
    [SerializeField] private Color oceanColor = new Color(0.06f, 0.22f, 0.45f, 1f);

    [Header("Mountain Color")]
    [Tooltip("Distinct mountain color shown at high elevations (cartographic style).")]
    [SerializeField] private Color mountainColor = new Color(0.72f, 0.58f, 0.38f, 1f);

    [Header("Biome Tuning")]
    [Range(0f, 1f)]
    [Tooltip("Ice cap coverage size. 0 = no ice caps, 1 = massive polar ice.")]
    [SerializeField] private float iceCapSize = 0.5f;

    [Range(0f, 1.0f)]
    [Tooltip("Blend width at biome band edges. 0 = hard cutoff, 0.03 = subtle transition.")]
    [SerializeField] private float biomeBlend = 0.03f;

    [Range(0f, 10f)]
    [Tooltip("Scale of noise used to perturb biome latitude bands. Higher = more detail.")]
    [SerializeField] private float biomeNoiseScale = 3.0f;

    [Range(0f, 0.2f)]
    [Tooltip("Strength of noise perturbation on biome bands. 0 = straight lines.")]
    [SerializeField] private float biomeNoiseStrength = 0.08f;

    [Range(0.5f, 2f)]
    [Tooltip("Color vibrancy boost. 1 = natural, >1 = more saturated.")]
    [SerializeField] private float colorVibrancy = 1.3f;

    [Header("Surface Properties")]
    [Range(0f, 1f)]
    [Tooltip("Surface smoothness. 0 = rough/matte, 1 = mirror-like.")]
    [SerializeField] private float smoothness = 0.3f;

    [Range(0f, 1f)]
    [Tooltip("Metallic factor. 0 = dielectric, 1 = fully metallic.")]
    [SerializeField] private float metallic = 0.0f;

    [Range(0f, 1f)]
    [Tooltip("Ambient occlusion. 1 = full brightness, 0 = fully occluded.")]
    [SerializeField] private float ambientOcclusion = 1.0f;

    [Range(0f, 1f)]
    [Tooltip("Ambient light strength on the dark hemisphere. 0 = pitch black, 0.12 = default.")]
    [SerializeField] private float ambientStrength = 0.12f;

    [Range(0.5f, 3f)]
    [Tooltip("Overall brightness multiplier for the planet surface. 1 = natural, higher = brighter.")]
    [SerializeField] private float brightness = 1.4f;

    [Header("Seed")]
    [Tooltip("Planet noise seed. Randomized each play if randomizeSeed is true.")]
    [SerializeField] private float seed = 0f;

    [Tooltip("Randomize the planet seed on every Awake.")]
    [SerializeField] private bool randomizeSeed = true;

    // -----------------------------------------------------------------
    //  Private state
    // -----------------------------------------------------------------
    private Material materialInstance;
    private Material cloudMaterialInstance;
    private Material atmosphereMaterialInstance;
    private GameObject cloudShellGO;
    private GameObject atmosphereShellGO;
    private Volume bloomVolume;
    private int waterwaysPreset = 1;
    private int previewFidelity = 2;
    [SerializeField] private float basePlanetScale = 1f;
    private Vector3 baseSurfaceLocalScale = Vector3.one;
    private Vector3 baseAtmosphereLocalScale = Vector3.one;
    private bool hasWarnedMissingCloudShader;
    private bool hasWarnedMissingAtmosphereShader;


    // Cached shader property IDs — planet
    private static readonly int ID_LandScale     = Shader.PropertyToID("_LandScale");
    private static readonly int ID_LandThreshold = Shader.PropertyToID("_LandThreshold");
    private static readonly int ID_Temperature   = Shader.PropertyToID("_Temperature");
    private static readonly int ID_Moisture      = Shader.PropertyToID("_Moisture");
    private static readonly int ID_Elevation     = Shader.PropertyToID("_Elevation");
    private static readonly int ID_MapStyle     = Shader.PropertyToID("_MapStyle");
    private static readonly int ID_EquatorialColor = Shader.PropertyToID("_EquatorialColor");
    private static readonly int ID_DesertSand    = Shader.PropertyToID("_DesertSand");
    private static readonly int ID_SubtropicalColor = Shader.PropertyToID("_SubtropicalColor");
    private static readonly int ID_TemperateColor = Shader.PropertyToID("_TemperateColor");
    private static readonly int ID_BorealColor   = Shader.PropertyToID("_BorealColor");
    private static readonly int ID_TundraColor   = Shader.PropertyToID("_TundraColor");
    private static readonly int ID_PolarColor    = Shader.PropertyToID("_PolarColor");
    private static readonly int ID_OceanColor   = Shader.PropertyToID("_OceanColor");
    private static readonly int ID_MountainColor = Shader.PropertyToID("_MountainColor");
    private static readonly int ID_IceCapSize    = Shader.PropertyToID("_IceCapSize");
    private static readonly int ID_BiomeBlend    = Shader.PropertyToID("_BiomeBlend");
    private static readonly int ID_BiomeNoiseScale = Shader.PropertyToID("_BiomeNoiseScale");
    private static readonly int ID_BiomeNoiseStrength = Shader.PropertyToID("_BiomeNoiseStrength");
    private static readonly int ID_ColorVibrancy = Shader.PropertyToID("_ColorVibrancy");
    private static readonly int ID_Seed          = Shader.PropertyToID("_Seed");
    private static readonly int ID_BiomeTint     = Shader.PropertyToID("_BiomeTint");
    private static readonly int ID_DesertFactor  = Shader.PropertyToID("_DesertFactor");
    private static readonly int ID_TropicalFactor = Shader.PropertyToID("_TropicalFactor");
    private static readonly int ID_SnowFactor    = Shader.PropertyToID("_SnowFactor");
    private static readonly int ID_DetailScale   = Shader.PropertyToID("_DetailScale");
    private static readonly int ID_DetailStrength= Shader.PropertyToID("_DetailStrength");
    private static readonly int ID_AtmosColor    = Shader.PropertyToID("_AtmosphereColor");
    private static readonly int ID_DisplacementScale = Shader.PropertyToID("_DisplacementScale");
    private static readonly int ID_Smoothness    = Shader.PropertyToID("_Smoothness");
    private static readonly int ID_Metallic      = Shader.PropertyToID("_Metallic");
    private static readonly int ID_AmbientOcclusion = Shader.PropertyToID("_AmbientOcclusion");
    private static readonly int ID_AmbientStrength = Shader.PropertyToID("_AmbientStrength");
    private static readonly int ID_Brightness = Shader.PropertyToID("_Brightness");
    private static readonly int ID_LandDetailTex = Shader.PropertyToID("_LandDetailTex");
    private static readonly int ID_MountainDetailTex = Shader.PropertyToID("_MountainDetailTex");
    private static readonly int ID_IceDetailTex = Shader.PropertyToID("_IceDetailTex");
    private static readonly int ID_OceanDetailTex = Shader.PropertyToID("_OceanDetailTex");
    private static readonly int ID_OceanNormalTex = Shader.PropertyToID("_OceanNormalTex");
    private static readonly int ID_RoughnessDetailTex = Shader.PropertyToID("_RoughnessDetailTex");
    private static readonly int ID_LandDetailStrength = Shader.PropertyToID("_LandDetailStrength");
    private static readonly int ID_MountainDetailStrength = Shader.PropertyToID("_MountainDetailStrength");
    private static readonly int ID_IceDetailStrength = Shader.PropertyToID("_IceDetailStrength");
    private static readonly int ID_OceanDetailStrength = Shader.PropertyToID("_OceanDetailStrength");
    private static readonly int ID_OceanNormalStrength = Shader.PropertyToID("_OceanNormalStrength");
    private static readonly int ID_TextureDetailScale = Shader.PropertyToID("_TextureDetailScale");
    private static readonly int ID_UseDetailTextures = Shader.PropertyToID("_UseDetailTextures");



    // Cloud-specific
    private static readonly int ID_CloudDensity  = Shader.PropertyToID("_CloudDensity");
    private static readonly int ID_CloudScale    = Shader.PropertyToID("_CloudScale");
    private static readonly int ID_CloudSpeed    = Shader.PropertyToID("_CloudSpeed");
    private static readonly int ID_CloudAltitude = Shader.PropertyToID("_CloudAltitude");

    // Atmosphere shell-specific
    private static readonly int ID_AtmosFalloff  = Shader.PropertyToID("_AtmosphereFalloff");
    private static readonly int ID_AtmosIntensity = Shader.PropertyToID("_AtmosphereIntensity");



    // -----------------------------------------------------------------
    //  Lifecycle
    // -----------------------------------------------------------------
    private void Awake()
    {
        // Auto-find renderer if not assigned
        if (previewRenderer == null)
        {
            previewRenderer = GetComponentInChildren<MeshRenderer>();
        }

        // Auto-find light: children first, then any directional light in the scene
        if (previewLight == null)
            previewLight = GetComponentInChildren<Light>();
        if (previewLight == null)
        {
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { previewLight = l; break; }
        }

        SetupMaterial();

        // Randomize seed so each play session gets a unique planet
        if (randomizeSeed)
        {
            seed = Random.Range(0f, 10000f);
        }

        ApplyAllParameters();
        SetupSpaceBackgroundIfNeeded();

        // Upgrade the preview mesh for better shading/detail
        TryReplacePreviewMesh();

        SetupCloudShell();
        SetupAtmosphereShell();
        CacheBaseScales();

        // Enable bloom on the preview camera
        SetupBloomVolume();
    }

    private void Update()
    {
        // Planet rotation
        if (previewRenderer != null)
        {
            previewRenderer.transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.Self);
        }
        if (cloudShellGO != null)
        {
            cloudShellGO.transform.Rotate(Vector3.up, rotationSpeed * cloudRotationMultiplier * Time.deltaTime, Space.Self);
        }
        if (atmosphereShellGO != null && !Mathf.Approximately(atmosphereRotationMultiplier, 0f))
        {
            atmosphereShellGO.transform.Rotate(Vector3.up, rotationSpeed * atmosphereRotationMultiplier * Time.deltaTime, Space.Self);
        }

        // Sun lighting now read directly from HDRP's directional light buffer in the shaders
    }

    private void OnDestroy()
    {
        if (materialInstance != null) { Destroy(materialInstance); materialInstance = null; }
        if (cloudMaterialInstance != null) { Destroy(cloudMaterialInstance); cloudMaterialInstance = null; }
        if (atmosphereMaterialInstance != null) { Destroy(atmosphereMaterialInstance); atmosphereMaterialInstance = null; }
    }

    /// <summary>
    /// When values change in the Inspector during Play mode, push them to the material.
    /// </summary>
    private void OnValidate()
    {
        if (Application.isPlaying && materialInstance != null)
        {
            ApplyAllParameters();
            PushCloudParameters();
            PushAtmosphereParameters();

            // Update atmosphere shell scale at runtime
            if (atmosphereShellGO != null && previewRenderer != null)
                atmosphereShellGO.transform.localScale = previewRenderer.transform.localScale * atmosphereShellScale;
        }
    }

    // -----------------------------------------------------------------
    //  Setup
    // -----------------------------------------------------------------
    private void SetupMaterial()
    {
        if (previewRenderer == null)
        {
            Debug.LogWarning("[MenuPlanetPreview] No MeshRenderer found. " +
                             "Assign one in the inspector or add a child with a MeshRenderer.");
            return;
        }

        if (previewShader == null)
        {
            previewShader = Shader.Find("Custom/MenuPlanetPreview");
        }

        if (previewShader == null)
        {
            Debug.LogError("[MenuPlanetPreview] Shader 'Custom/MenuPlanetPreview' not found! " +
                           "Make sure MenuPlanetPreview.shader is in the project.");
            return;
        }

        materialInstance = new Material(previewShader);
        materialInstance.name = "MenuPlanetPreview_Instance";
        materialInstance.renderQueue = (int)RenderQueue.Geometry;
        previewRenderer.material = materialInstance;
    }

    private void ApplyAllParameters()
    {
        if (materialInstance == null) return;

        materialInstance.SetFloat(ID_LandScale,     landScale);
        materialInstance.SetFloat(ID_LandThreshold, landThreshold);
        materialInstance.SetFloat(ID_Temperature,   temperature);
        materialInstance.SetFloat(ID_Moisture,      moisture);
        materialInstance.SetFloat(ID_Elevation,     elevation);
        materialInstance.SetFloat(ID_MapStyle,     mapStyle);
        materialInstance.SetFloat(ID_Seed,         seed);

        // Push all biome zone colors
        materialInstance.SetColor(ID_EquatorialColor, equatorialColor);
        materialInstance.SetColor(ID_DesertSand,    desertSand);
        materialInstance.SetColor(ID_SubtropicalColor, subtropicalColor);
        materialInstance.SetColor(ID_TemperateColor, temperateZoneColor);
        materialInstance.SetColor(ID_BorealColor,   borealColor);
        materialInstance.SetColor(ID_TundraColor,   tundraColor);
        materialInstance.SetColor(ID_PolarColor,    polarColor);

        // Ocean color
        materialInstance.SetColor(ID_OceanColor,    oceanColor);

        // Mountain color
        materialInstance.SetColor(ID_MountainColor, mountainColor);

        // Biome tuning
        materialInstance.SetFloat(ID_IceCapSize,    iceCapSize);
        materialInstance.SetFloat(ID_BiomeBlend,    biomeBlend);
        materialInstance.SetFloat(ID_BiomeNoiseScale, biomeNoiseScale);
        materialInstance.SetFloat(ID_BiomeNoiseStrength, biomeNoiseStrength);
        materialInstance.SetFloat(ID_ColorVibrancy, colorVibrancy);

        // Update biome-related visual parameters derived from temperature/moisture/elevation
        UpdateBiomeVisuals();

        // Push detail params (old atmosphere props no longer pushed — handled by shell)
        materialInstance.SetFloat(ID_DetailScale, detailScale);
        materialInstance.SetFloat(ID_DetailStrength, detailStrength);

        // Displacement
        materialInstance.SetFloat(ID_DisplacementScale, displacementScale);

        // Surface properties
        materialInstance.SetFloat(ID_Smoothness, smoothness);
        materialInstance.SetFloat(ID_Metallic, metallic);
        materialInstance.SetFloat(ID_AmbientOcclusion, ambientOcclusion);
        materialInstance.SetFloat(ID_AmbientStrength, ambientStrength);
        materialInstance.SetFloat(ID_Brightness, brightness);
        PushDetailTextureParameters();
    }

    private void PushDetailTextureParameters()
    {
        if (materialInstance == null) return;

        materialInstance.SetTexture(ID_LandDetailTex, landDetailTexture);
        materialInstance.SetTexture(ID_MountainDetailTex, mountainDetailTexture);
        materialInstance.SetTexture(ID_IceDetailTex, iceDetailTexture);
        materialInstance.SetTexture(ID_OceanDetailTex, oceanDetailTexture);
        materialInstance.SetTexture(ID_OceanNormalTex, oceanNormalTexture);
        materialInstance.SetTexture(ID_RoughnessDetailTex, roughnessDetailTexture);
        materialInstance.SetFloat(ID_LandDetailStrength, landDetailStrength);
        materialInstance.SetFloat(ID_MountainDetailStrength, mountainDetailStrength);
        materialInstance.SetFloat(ID_IceDetailStrength, iceDetailStrength);
        materialInstance.SetFloat(ID_OceanDetailStrength, oceanDetailStrength);
        materialInstance.SetFloat(ID_OceanNormalStrength, oceanNormalStrength);
        materialInstance.SetFloat(ID_TextureDetailScale, textureDetailScale);
        bool useDetails = landDetailTexture != null || mountainDetailTexture != null || iceDetailTexture != null ||
                          oceanDetailTexture != null || oceanNormalTexture != null || roughnessDetailTexture != null;
        materialInstance.SetFloat(ID_UseDetailTextures, useDetails ? 1f : 0f);
    }

    // -----------------------------------------------------------------
    //  Public API — called by UI sliders / MainMenuManager
    // -----------------------------------------------------------------

    /// <summary>
    /// Set land shape from a preset (e.g., Archipelago, Pangaea).
    ///   scale:     blob frequency (0.5 = huge continents, 5.0 = tiny islands)
    ///   threshold: land/ocean ratio (0 = nearly all land, 1 = nearly all ocean)
    /// </summary>
    public void SetLandPreset(float scale, float threshold)
    {
        landScale     = Mathf.Clamp(scale, 0.5f, 5f);
        landThreshold = Mathf.Clamp01(threshold);

        if (materialInstance != null)
        {
            materialInstance.SetFloat(ID_LandScale,     landScale);
            materialInstance.SetFloat(ID_LandThreshold, landThreshold);
            UpdateBiomeVisuals();
        }
    }

    /// <summary>
    /// Set temperature (0 = frozen,  0.5 = temperate,  1 = scorching).
    /// Affects land color: icy blue-gray → green → sandy tan.
    /// </summary>
    public void SetTemperature(float value)
    {
        temperature = Mathf.Clamp01(value);

        if (materialInstance != null)
        {
            materialInstance.SetFloat(ID_Temperature, temperature);
            UpdateBiomeVisuals();
        }
        PushCloudParameters();
        PushAtmosphereParameters();
    }

    /// <summary>
    /// Set moisture (0 = dry / deserts,  1 = wet / lush).
    /// Affects land saturation and optional lake speckles at high values.
    /// </summary>
    public void SetMoisture(float value)
    {
        moisture = Mathf.Clamp01(value);

        if (materialInstance != null)
        {
            materialInstance.SetFloat(ID_Moisture, moisture);
            UpdateBiomeVisuals();
        }
    }

    /// <summary>
    /// Set elevation (0 = flat lowlands,  0.5 = hilly,  1 = extreme mountains).
    /// Affects terrain color banding: lowlands → highlands → rocky gray → snow peaks.
    /// Rivers fade on the highest peaks.
    /// </summary>
    public void SetElevation(float value)
    {
        elevation = Mathf.Clamp01(value);

        if (materialInstance != null)
        {
            materialInstance.SetFloat(ID_Elevation, elevation);
            UpdateBiomeVisuals();
        }
    }

    /// <summary>
    /// Set the displacement scale for terrain protrusion on the planet surface.
    /// </summary>
    public void SetDisplacementScale(float value)
    {
        displacementScale = Mathf.Clamp(value, 0f, 0.15f);
        if (materialInstance != null)
            materialInstance.SetFloat(ID_DisplacementScale, displacementScale);
    }

    /// <summary>Set the equatorial/tropical color used in jungle and monsoon bands.</summary>
    public void SetEquatorialColor(Color value)
    {
        equatorialColor = value;
        if (materialInstance != null) { materialInstance.SetColor(ID_EquatorialColor, equatorialColor); }
    }

    /// <summary>Set the desert sand color used for dry equatorial regions.</summary>
    public void SetDesertSand(Color value)
    {
        desertSand = value;
        if (materialInstance != null) { materialInstance.SetColor(ID_DesertSand, desertSand); }
    }

    /// <summary>Set the subtropical savanna color.</summary>
    public void SetSubtropicalColor(Color value)
    {
        subtropicalColor = value;
        if (materialInstance != null) { materialInstance.SetColor(ID_SubtropicalColor, subtropicalColor); }
    }

    /// <summary>Set the temperate grassland/forest color.</summary>
    public void SetTemperateZoneColor(Color value)
    {
        temperateZoneColor = value;
        if (materialInstance != null) { materialInstance.SetColor(ID_TemperateColor, temperateZoneColor); }
    }

    /// <summary>Set the boreal conifer color.</summary>
    public void SetBorealColor(Color value)
    {
        borealColor = value;
        if (materialInstance != null) { materialInstance.SetColor(ID_BorealColor, borealColor); }
    }

    /// <summary>Set the tundra barren color.</summary>
    public void SetTundraColor(Color value)
    {
        tundraColor = value;
        if (materialInstance != null) { materialInstance.SetColor(ID_TundraColor, tundraColor); }
    }

    /// <summary>Set the polar ice/snow color.</summary>
    public void SetPolarColor(Color value)
    {
        polarColor = value;
        if (materialInstance != null) { materialInstance.SetColor(ID_PolarColor, polarColor); }
    }

    /// <summary>Set the ocean color.</summary>
    public void SetOceanColor(Color value)
    {
        oceanColor = value;
        if (materialInstance != null) { materialInstance.SetColor(ID_OceanColor, oceanColor); }
    }

    /// <summary>Set ice cap coverage size. 0 = no caps, 1 = massive polar ice.</summary>
    public void SetIceCapSize(float value)
    {
        iceCapSize = Mathf.Clamp01(value);
        if (materialInstance != null) { materialInstance.SetFloat(ID_IceCapSize, iceCapSize); }
    }

    /// <summary>Set biome blend width at band edges. 0 = hard cutoff, 0.03+ = subtle transition.</summary>
    public void SetBiomeBlend(float value)
    {
        biomeBlend = Mathf.Clamp(value, 0f, 0.1f);
        if (materialInstance != null) { materialInstance.SetFloat(ID_BiomeBlend, biomeBlend); }
    }

    public void SetWorldSeed(int worldSeed, bool randomSeed)
    {
        randomizeSeed = randomSeed;
        seed = randomSeed ? Random.Range(0f, 10000f) : Mathf.Abs(worldSeed % 100000);
        if (materialInstance != null) materialInstance.SetFloat(ID_Seed, seed);
        UpdateBiomeVisuals();
    }

    public void SetWaterwaysPreset(int preset)
    {
        waterwaysPreset = Mathf.Clamp(preset, 0, 2);
        UpdateBiomeVisuals();
        PushCloudParameters();
    }

    public void SetPreviewFidelity(int fidelityLevel)
    {
        previewFidelity = Mathf.Clamp(fidelityLevel, 0, 2);
        // 0=balanced, 1=high, 2=ultra (default)
        detailScale = previewFidelity == 2 ? 28f : (previewFidelity == 1 ? 22f : 18f);
        detailStrength = previewFidelity == 2 ? 0.32f : (previewFidelity == 1 ? 0.26f : 0.2f);
        biomeNoiseScale = previewFidelity == 2 ? 6.2f : (previewFidelity == 1 ? 4.8f : 3.8f);
        biomeNoiseStrength = previewFidelity == 2 ? 0.135f : (previewFidelity == 1 ? 0.1f : 0.08f);
        displacementScale = previewFidelity == 2 ? 0.065f : (previewFidelity == 1 ? 0.05f : 0.04f);
        UpdateBiomeVisuals();
    }

    public void SetPlanetScaleMultiplier(float scaleMultiplier)
    {
        float s = Mathf.Clamp(scaleMultiplier, 0.75f, 1.35f);
        float finalScale = basePlanetScale * s;

        if (previewRenderer != null)
            previewRenderer.transform.localScale = baseSurfaceLocalScale * finalScale;
        if (atmosphereShellGO != null)
            atmosphereShellGO.transform.localScale = baseAtmosphereLocalScale * finalScale;
        if (cloudShellGO != null)
            cloudShellGO.transform.localScale = baseSurfaceLocalScale * finalScale;
    }

    private void CacheBaseScales()
    {
        if (previewRenderer != null)
            baseSurfaceLocalScale = previewRenderer.transform.localScale;
        if (atmosphereShellGO != null)
            baseAtmosphereLocalScale = atmosphereShellGO.transform.localScale;
    }

    // -----------------------------------------------------------------
    //  Biome tinting and derived visual parameters
    // -----------------------------------------------------------------
    private void UpdateBiomeVisuals()
    {
        if (materialInstance == null) return;

        // Compute a base land tint color from temperature and moisture.
        // Colder -> bluish/gray, Temperate -> green, Hot -> tan.
        Color coldColor = new Color(0.75f, 0.85f, 0.95f); // icy blue/gray
        Color temperateColor = new Color(0.33f, 0.6f, 0.26f); // green
        Color hotDryColor = new Color(0.76f, 0.66f, 0.45f); // sandy/tan

        // Interpolate temperature first (0=cold, 1=hot)
        Color tempLerp = Color.Lerp(coldColor, hotDryColor, temperature);
        // Blend in moisture (wet -> greener)
        Color finalTint = Color.Lerp(tempLerp, temperateColor, moisture * 0.9f);

        materialInstance.SetColor(ID_BiomeTint, finalTint);

        // Desert and tropical amounts should drop as things get colder.
        float desertFactor = Mathf.Clamp01(temperature * (1f - moisture) * 1.5f);
        float tropicalFactor = Mathf.Clamp01(moisture * temperature * 1.6f);

        // Snow factor is primarily temperature-driven but reinforced by elevation
        float snowFactor = Mathf.Clamp01((1f - temperature) * elevation * 1.8f);
        float waterwayWetness = waterwaysPreset == 2 ? 0.10f : (waterwaysPreset == 0 ? -0.08f : 0f);

        materialInstance.SetFloat(ID_DesertFactor, desertFactor);
        materialInstance.SetFloat(ID_TropicalFactor, tropicalFactor);
        materialInstance.SetFloat(ID_SnowFactor, snowFactor);
        
        // Push all inspector-driven biome zone colors to shader
        materialInstance.SetColor(ID_EquatorialColor, equatorialColor);
        materialInstance.SetColor(ID_DesertSand, desertSand);
        materialInstance.SetColor(ID_SubtropicalColor, subtropicalColor);
        materialInstance.SetColor(ID_TemperateColor, temperateZoneColor);
        materialInstance.SetColor(ID_BorealColor, borealColor);
        materialInstance.SetColor(ID_TundraColor, tundraColor);
        materialInstance.SetColor(ID_PolarColor, polarColor);
        materialInstance.SetColor(ID_OceanColor, oceanColor);
        materialInstance.SetFloat(ID_IceCapSize, iceCapSize);
        materialInstance.SetFloat(ID_BiomeBlend, biomeBlend);
        materialInstance.SetFloat(ID_BiomeNoiseScale, biomeNoiseScale * (waterwaysPreset == 2 ? 1.15f : 1f));
        materialInstance.SetFloat(ID_BiomeNoiseStrength, biomeNoiseStrength * (waterwaysPreset == 0 ? 0.9f : 1.05f));
        materialInstance.SetFloat(ID_ColorVibrancy, Mathf.Clamp(colorVibrancy, 0.5f, 2f));
        materialInstance.SetFloat(ID_Seed, seed);
        materialInstance.SetFloat(ID_Moisture, Mathf.Clamp01(moisture + waterwayWetness));
        
        // Mirror detail props to shader so inspector updates apply immediately
        materialInstance.SetFloat(ID_DetailScale, detailScale);
        materialInstance.SetFloat(ID_DetailStrength, detailStrength);
    }

    // -----------------------------------------------------------------
    //  Cloud Shell setup + parameter push
    // -----------------------------------------------------------------
    private void SetupCloudShell()
    {
        if (!enableCloudShell || previewRenderer == null) return;

        if (cloudShader == null)
            cloudShader = Shader.Find("Custom/MenuPlanetClouds");
        if (cloudShader == null)
        {
            if (!hasWarnedMissingCloudShader)
            {
                Debug.LogWarning("[MenuPlanetPreview] Cloud shader 'Custom/MenuPlanetClouds' not found.");
                hasWarnedMissingCloudShader = true;
            }
            return;
        }

        cloudShellGO = new GameObject("_CloudShell");
        cloudShellGO.transform.SetParent(previewRenderer.transform.parent, false);
        cloudShellGO.transform.localPosition = previewRenderer.transform.localPosition;
        cloudShellGO.transform.localScale    = previewRenderer.transform.localScale;
        cloudShellGO.layer = previewRenderer.gameObject.layer;

        var mf = cloudShellGO.AddComponent<MeshFilter>();
        mf.sharedMesh = IcoSphereGenerator.Create(Mathf.Min(icosphereSubdivisions, 4), 1f);

        var mr = cloudShellGO.AddComponent<MeshRenderer>();
        cloudMaterialInstance = new Material(cloudShader);
        cloudMaterialInstance.name = "MenuPlanetClouds_Instance";
        cloudMaterialInstance.renderQueue = (int)RenderQueue.Transparent;
        mr.material = cloudMaterialInstance;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        PushCloudParameters();
    }

    private void PushCloudParameters()
    {
        if (cloudMaterialInstance == null) return;
        cloudMaterialInstance.SetFloat(ID_CloudDensity, cloudDensity);
        cloudMaterialInstance.SetFloat(ID_CloudScale, cloudScale);
        cloudMaterialInstance.SetFloat(ID_CloudSpeed, cloudSpeed);
        cloudMaterialInstance.SetFloat(ID_CloudAltitude, cloudAltitude);
        cloudMaterialInstance.SetFloat(ID_Temperature, temperature);
        cloudMaterialInstance.SetFloat(ID_MapStyle, mapStyle);
        float waterwayCloud = waterwaysPreset == 2 ? 0.07f : (waterwaysPreset == 0 ? -0.07f : 0f);
        cloudMaterialInstance.SetFloat(ID_CloudDensity, Mathf.Clamp01(cloudDensity + waterwayCloud));
    }

    // -----------------------------------------------------------------
    //  Atmosphere Shell setup + parameter push
    // -----------------------------------------------------------------
    private void SetupAtmosphereShell()
    {
        if (!enableAtmosphereShell || previewRenderer == null) return;

        if (atmosphereShader == null)
            atmosphereShader = Shader.Find("Custom/MenuPlanetAtmosphere");
        if (atmosphereShader == null)
        {
            if (!hasWarnedMissingAtmosphereShader)
            {
                Debug.LogWarning("[MenuPlanetPreview] Atmosphere shader 'Custom/MenuPlanetAtmosphere' not found.");
                hasWarnedMissingAtmosphereShader = true;
            }
            return;
        }

        atmosphereShellGO = new GameObject("_AtmosphereShell");
        atmosphereShellGO.transform.SetParent(previewRenderer.transform.parent, false);
        atmosphereShellGO.transform.localPosition = previewRenderer.transform.localPosition;
        atmosphereShellGO.transform.localScale    = previewRenderer.transform.localScale * atmosphereShellScale;
        atmosphereShellGO.layer = previewRenderer.gameObject.layer;

        var mf = atmosphereShellGO.AddComponent<MeshFilter>();
        mf.sharedMesh = IcoSphereGenerator.Create(3, 1f); // low poly is fine for a smooth glow

        var mr = atmosphereShellGO.AddComponent<MeshRenderer>();
        atmosphereMaterialInstance = new Material(atmosphereShader);
        atmosphereMaterialInstance.name = "MenuPlanetAtmosphere_Instance";
        atmosphereMaterialInstance.renderQueue = (int)RenderQueue.Transparent + 1;
        mr.material = atmosphereMaterialInstance;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        PushAtmosphereParameters();
    }

    private void PushAtmosphereParameters()
    {
        if (atmosphereMaterialInstance == null) return;
        atmosphereMaterialInstance.SetColor(ID_AtmosColor, atmosphereColor);
        atmosphereMaterialInstance.SetFloat(ID_AtmosFalloff, atmosphereFalloff);
        atmosphereMaterialInstance.SetFloat(ID_AtmosIntensity, atmosphereIntensity);
        atmosphereMaterialInstance.SetFloat(ID_Temperature, temperature);
        atmosphereMaterialInstance.SetFloat(ID_MapStyle, mapStyle);
    }



    // -----------------------------------------------------------------
    //  HDRP Bloom Volume (programmatic, no .asset files needed)
    // -----------------------------------------------------------------
    private void SetupBloomVolume()
    {
        if (!enableBloom) return;

        // Find the preview camera (should be a sibling/child with a Camera component)
        Camera previewCam = previewCamera ?? GetComponentInChildren<Camera>(true);
        if (previewCam == null)
        {
            Debug.LogWarning("[MenuPlanetPreview] No preview camera found — bloom not configured.");
            return;
        }

        // Enable post-processing on the HD camera data
        var hdCam = previewCam.GetComponent<HDAdditionalCameraData>();
        if (hdCam != null)
        {
            // Use the non-obsolete API to enable post-processing
            hdCam.customRenderingSettings = true;
            var mask = hdCam.renderingPathCustomFrameSettingsOverrideMask;
            mask.mask[(uint)FrameSettingsField.Postprocess] = true;
            hdCam.renderingPathCustomFrameSettingsOverrideMask = mask;

            var fs = hdCam.renderingPathCustomFrameSettings;
            fs.SetEnabled(FrameSettingsField.Postprocess, true);
            hdCam.renderingPathCustomFrameSettings = fs;
        }

        // Create a local Volume on the camera for bloom
        bloomVolume = previewCam.gameObject.AddComponent<Volume>();
        bloomVolume.isGlobal = true;
        bloomVolume.priority = 100;
        bloomVolume.profile = ScriptableObject.CreateInstance<VolumeProfile>();

        // Bloom
        var bloom = bloomVolume.profile.Add<Bloom>(true);
        bloom.threshold.Override(bloomThreshold);
        bloom.intensity.Override(bloomIntensity);
        bloom.scatter.Override(0.6f);

        // Fixed exposure so bloom is consistent
        var exposure = bloomVolume.profile.Add<Exposure>(true);
        exposure.mode.Override(ExposureMode.Fixed);
        exposure.fixedExposure.Override(0f);
    }

    // Replace the attached preview sphere mesh with a generated icosphere for higher visual quality
    private void TryReplacePreviewMesh()
    {
        if (previewRenderer == null) return;
        MeshFilter mf = previewRenderer.GetComponent<MeshFilter>();
        if (mf == null) return;

        Mesh ico = IcoSphereGenerator.Create(icosphereSubdivisions, 1f);
        if (ico != null)
        {
            mf.sharedMesh = ico;
        }
    }

    // Minimal icosphere generator for preview meshes
    private static class IcoSphereGenerator
    {
        public static Mesh Create(int subdivisions, float radius)
        {
            subdivisions = Mathf.Clamp(subdivisions, 0, 6);
            Mesh mesh = new Mesh();
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            List<Vector3> verts = new List<Vector3>
            {
                new Vector3(-1,  t,  0), new Vector3( 1,  t,  0), new Vector3(-1, -t,  0), new Vector3( 1, -t,  0),
                new Vector3( 0, -1,  t), new Vector3( 0,  1,  t), new Vector3( 0, -1, -t), new Vector3( 0,  1, -t),
                new Vector3( t,  0, -1), new Vector3( t,  0,  1), new Vector3(-t,  0, -1), new Vector3(-t,  0,  1)
            };
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized * radius;

            List<int> faces = new List<int>
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
                1,5,9,5,11,4,11,10,2,10,7,6,7,1,8,
                3,9,4,3,4,2,3,2,6,3,6,8,3,8,9,
                4,9,5,2,4,11,6,2,10,8,6,7,9,8,1
            };

            Dictionary<long,int> midCache = new Dictionary<long,int>();
            for (int s = 0; s < subdivisions; s++)
            {
                List<int> newFaces = new List<int>();
                midCache.Clear();
                for (int i = 0; i < faces.Count; i += 3)
                {
                    int a = faces[i], b = faces[i+1], c = faces[i+2];
                    int ab = GetMidpointIndex(midCache, verts, a, b, radius);
                    int bc = GetMidpointIndex(midCache, verts, b, c, radius);
                    int ca = GetMidpointIndex(midCache, verts, c, a, radius);
                    newFaces.AddRange(new []{a, ab, ca});
                    newFaces.AddRange(new []{b, bc, ab});
                    newFaces.AddRange(new []{c, ca, bc});
                    newFaces.AddRange(new []{ab, bc, ca});
                }
                faces = newFaces;
            }

            mesh.SetVertices(verts);
            if (verts.Count > 65000)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetTriangles(faces, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static int GetMidpointIndex(Dictionary<long,int> cache, List<Vector3> verts, int i1, int i2, float radius)
        {
            long key = ((long)Mathf.Min(i1,i2) << 32) + Mathf.Max(i1,i2);
            if (cache.TryGetValue(key, out int idx)) return idx;
            Vector3 v1 = verts[i1]; Vector3 v2 = verts[i2];
            Vector3 mid = ((v1 + v2) * 0.5f).normalized * radius;
            idx = verts.Count;
            verts.Add(mid);
            cache[key] = idx;
            return idx;
        }
    }

    // -----------------------------------------------------------------
    //  Space background quad support
    // -----------------------------------------------------------------
    [Header("Space Background")]
    [Tooltip("Optional material to use for a space/star background behind the preview sphere.")]
    [SerializeField] private Material spaceBackgroundMaterial;

    [Tooltip("Automatically create a background quad that faces the main camera when a material is assigned.")]
    [SerializeField] private bool createBackgroundQuad = true;

    private GameObject backgroundQuad;

    private void SetupSpaceBackgroundIfNeeded()
    {
        if (!createBackgroundQuad || spaceBackgroundMaterial == null || previewRenderer == null) return;

        if (backgroundQuad != null) return;

        // Create a simple quad and place it behind the preview sphere
        backgroundQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        backgroundQuad.name = "MenuPlanetPreview_SpaceBackground";
        backgroundQuad.layer = previewRenderer.gameObject.layer;

        // Detach the quad from the rotating preview so it doesn't inherit the sphere's rotation
        backgroundQuad.transform.SetParent(this.transform, false);

        // Compute a world-space position slightly behind the preview sphere from the preview camera's view.
        // Prefer an inspector-assigned preview camera, then the scene's main camera, then fall back to any Camera on this preview.
        Camera cam = previewCamera ?? Camera.main ?? GetComponentInChildren<Camera>(true);

        Vector3 center = previewRenderer.bounds.center;
        float maxExtent = Mathf.Max(previewRenderer.bounds.size.x, previewRenderer.bounds.size.y, previewRenderer.bounds.size.z);
        float size = maxExtent * 6f;
        Vector3 viewDir;
        if (cam != null)
            viewDir = (center - cam.transform.position).normalized; // from camera toward center
        else
            viewDir = (center - previewRenderer.transform.position).normalized;

        float distance = maxExtent * 0.5f + 1.5f;
        // Place the quad between the center and the camera so it sits behind the planet relative to that camera
        backgroundQuad.transform.position = center + viewDir * (distance + maxExtent * 0.5f);
        if (cam != null)
            backgroundQuad.transform.rotation = Quaternion.LookRotation(cam.transform.position - backgroundQuad.transform.position);
        else
            backgroundQuad.transform.rotation = Quaternion.LookRotation(previewRenderer.transform.position - backgroundQuad.transform.position);
        backgroundQuad.transform.localScale = new Vector3(size, size, 1f);

        var quadRenderer = backgroundQuad.GetComponent<MeshRenderer>();
        quadRenderer.sharedMaterial = spaceBackgroundMaterial;

        // Disable collider
        var col = backgroundQuad.GetComponent<Collider>();
        if (col != null) Destroy(col);
    }

    private void LateUpdate()
    {
        // Keep the background quad facing the preview camera (prefer Camera.main, fall back to child camera)
        if (backgroundQuad == null) return;

        Camera cam = previewCamera ?? Camera.main ?? GetComponentInChildren<Camera>(true);
        if (cam != null)
        {
            backgroundQuad.transform.rotation = Quaternion.LookRotation(cam.transform.position - backgroundQuad.transform.position);
        }
    }

    /// <summary>
    /// Set map style (0 = normal world, 1 = infernal/demonic).
    /// When set to 1: oceans become lava, land becomes charred/scorched,
    /// rivers become lava flows, volcanic vents glow on the surface,
    /// ice caps become ash deposits, and a red atmosphere rim appears.
    /// </summary>
    public void SetMapStyle(float value)
    {
        mapStyle = Mathf.Clamp01(value);

        if (materialInstance != null)
        {
            materialInstance.SetFloat(ID_MapStyle, mapStyle);
        }
        PushCloudParameters();
        PushAtmosphereParameters();
    }

}
