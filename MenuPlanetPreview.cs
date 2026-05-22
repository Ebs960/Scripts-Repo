using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using System.Collections.Generic;
using UnityEngine.Serialization;

/// <summary>
/// Visual-only generated planet preview for the Main Menu / Game Setup UI.
///
/// Uses MenuPlanetPreviewWorldGeneratorV2 to build a preview-specific
/// procedural landmass/climate/hydrology/biome field pipeline, then renders those
/// generated maps through the Custom/MenuPlanetPreview HDRP shader.
///
/// This system has no coupling to gameplay PlanetGenerator or tile logic.
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
    [SerializeField] private MenuPlanetPreviewWorldGeneratorV2 worldGenerator;

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
    [Range(0f, 0.15f)] [SerializeField] private float displacementScale = 0.004f;
    [Tooltip("Uses displaced geometry-derived normals. Disable to force stable sphere normals.")]
    [SerializeField] private bool useDisplacedNormals = false;

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
    [SerializeField, Range(0f, 0.35f)] private float cloudSurfaceShadowStrength = 0.12f;

    [Header("Atmosphere Shell")]
    [SerializeField] private bool enableAtmosphereShell = true;
    [Tooltip("Scale multiplier for the atmosphere shell mesh.")]
    [Range(1.01f, 1.15f)] [SerializeField] private float atmosphereShellScale = 1.06f;
    [Tooltip("Fresnel falloff exponent for atmospheric rim glow.")]
    [Range(1f, 8f)] [SerializeField] private float atmosphereFalloff = 3.5f;
    [Tooltip("Brightness multiplier for the atmosphere glow.")]
    [Range(0f, 3f)] [SerializeField] private float atmosphereIntensity = 1.2f;
    [SerializeField] private float atmosphereRotationMultiplier = 0f;
    [SerializeField, Range(0f, 2f)] private float atmosphereDayRimBoost = 1.15f;
    [SerializeField, Range(0f, 1f)] private float atmosphereNightRimStrength = 0.42f;
    [SerializeField, Range(0f, 1f)] private float atmosphereInnerScatterStrength = 0.12f;

    [Header("Shared Surface Overlays")]
    [Header("Mountain Overlay")]
    [SerializeField] private Texture2D mountainDetailTexture;
    [SerializeField] private Texture2D mountainNormalTexture;

    [Header("Ice / Snow Overlay")]
    [SerializeField] private Texture2D iceDetailTexture;
    [SerializeField] private Texture2D iceAlbedoTexture;
    [SerializeField] private Texture2D iceNormalTexture;
    [SerializeField] private Texture2D iceSmoothnessTexture;

    [Header("Ocean Material")]
    [SerializeField] private Texture2D oceanAlbedoTexture;
    [SerializeField] private Texture2D oceanDetailTexture;
    [SerializeField] private Texture2D waterwayDetailTexture;
    [SerializeField] private Texture2D oceanNormalTexture;
    [SerializeField] private Texture2D oceanSmoothnessTexture;

    [Header("Infernal / Lava")]
    [SerializeField] private Texture2D volcanicSurfaceSmoothnessTexture;

        [Header("Infernal / Lava Textures")]
    [SerializeField] private Texture2D volcanicRockTexture;
    [SerializeField] private Texture2D lavaCrackTexture;
    [SerializeField] private Texture2D lavaEmissiveTexture;
    [SerializeField] private Texture2D ashDetailTexture;

    [Header("Clouds")]
    [SerializeField] private Texture2D cloudNoiseTexture;

    [Header("Infernal / Lava Tuning")]
    [SerializeField, Range(0f, 1f)] private float volcanicRockStrength = 0.35f;
    [SerializeField, Range(0f, 1f)] private float lavaCrackStrength = 0.65f;
    [SerializeField, Range(0f, 5f)] private float lavaEmissionStrength = 2.2f;
    [SerializeField, Range(0.1f, 30f)] private float lavaTextureScale = 10f;
    [SerializeField, Range(0f, 1f)] private float ashDetailStrength = 0.25f;

    [Header("Texture Detail Strengths")]
    [SerializeField, Range(0f, 1f)] private float mountainDetailStrength = 0.14f;
    [SerializeField, Range(0f, 1f)] private float iceDetailStrength = 0.12f;
    [SerializeField, Range(0f, 1f)] private float oceanDetailStrength = 0.15f;
    [SerializeField, Range(0f, 1f)] private float oceanNormalStrength = 0.1f;
    [SerializeField, Range(0f, 1f)] private float mountainNormalStrength = 0.18f;
    [SerializeField, Range(0f, 1f)] private float iceNormalStrength = 0.22f;
    [SerializeField, Range(0.1f, 30f)] private float textureDetailScale = 8f;
    [Header("Texture-Driven Surface Biomes")]
    [Header("Biome Albedo Textures")]
    [SerializeField] private Texture2D jungleAlbedoTexture;
    [SerializeField] private Texture2D desertAlbedoTexture;
    [SerializeField] private Texture2D savannaAlbedoTexture;
    [SerializeField] private Texture2D temperateGrassAlbedoTexture;
    [SerializeField] private Texture2D temperateForestAlbedoTexture;
    [SerializeField] private Texture2D taigaAlbedoTexture;
    [SerializeField] private Texture2D tundraAlbedoTexture;
    [SerializeField] private Texture2D polarAlbedoTexture;
    [SerializeField] private Texture2D marshAlbedoTexture;
    [Header("Biome Normal Textures")]
    [SerializeField] private Texture2D jungleNormalTexture;
    [SerializeField] private Texture2D desertNormalTexture;
    [SerializeField] private Texture2D savannaNormalTexture;
    [SerializeField] private Texture2D temperateGrassNormalTexture;
    [SerializeField] private Texture2D temperateForestNormalTexture;
    [SerializeField] private Texture2D taigaNormalTexture;
    [SerializeField] private Texture2D tundraNormalTexture;
    [SerializeField] private Texture2D polarNormalTexture;
    [SerializeField] private Texture2D marshNormalTexture;
    [Header("Biome Smoothness / Data Textures")]
    [SerializeField] private Texture2D jungleSmoothnessTexture;
    [SerializeField] private Texture2D desertSmoothnessTexture;
    [SerializeField] private Texture2D savannaSmoothnessTexture;
    [SerializeField] private Texture2D temperateGrassSmoothnessTexture;
    [SerializeField] private Texture2D temperateForestSmoothnessTexture;
    [SerializeField] private Texture2D taigaSmoothnessTexture;
    [SerializeField] private Texture2D tundraSmoothnessTexture;
    [SerializeField] private Texture2D polarSmoothnessTexture;
    [SerializeField] private Texture2D marshSmoothnessTexture;
    [Header("Texture-Driven Biome Tuning")]
    [SerializeField, Range(0f, 1f)] private float biomeTextureStrength = 0.8f;
    [SerializeField, Range(0f, 0.2f)] private float climateGradeStrength = 0.06f;
    [SerializeField, Range(0f, 1f)] private float biomeNormalStrength = 0.15f;
    [SerializeField, Range(0.1f, 30f)] private float biomeTextureScale = 6.0f;
    [SerializeField, Range(0f, 1f)] private float biomeTextureContrast = 0.18f;
    
    [Header("Elevation Displacement")]
    [SerializeField, Range(0f, 1f)] private float landUpliftStrength = 0.06f;
    [SerializeField, Range(0f, 1f)] private float hillDisplacementStrength = 0.08f;
    [SerializeField, Range(0f, 1f)] private float mountainDisplacementStrength = 0.18f;
    [SerializeField, Range(0f, 2f)] private float terrainElevationDisplacementStrength = 1.0f;
    [SerializeField, Range(0f, 1f)] private float iceDisplacementStrength = 0.04f;
    [SerializeField, Range(0f, 1f)] private float volcanicDisplacementStrength = 0.12f;
    [SerializeField, Range(0f, 1f)] private float oceanDepthStrength = 0.01f;

    [Header("Elevation Debug")]
    [SerializeField] private bool showElevationOnly;
    [SerializeField] private bool showMountainMaskOnly;
    [SerializeField] private bool showDisplacementHeightOnly;

    [Header("Debug")]
    [SerializeField] private bool showLandMaskOnly = false;
    [SerializeField] private bool showDetailTexturesOnly = false;
    [SerializeField] private bool showNormalsOnly = false;
    [SerializeField] private bool showBiomeWeightsOnly = false;
    [SerializeField] private bool showBiomeTextureOnly = false;
    [SerializeField] private bool showSmoothnessOnly = false;
    [SerializeField] private bool showLocalMoistureOnly = false;
    [SerializeField] private bool showLocalTemperatureOnly = false;
    [SerializeField] private bool showContinentalityOnly = false;
    [SerializeField] private bool showSeasonalityOnly = false;
    [SerializeField] private bool showRainShadowOnly = false;
    [SerializeField] private bool showRiparianWetnessOnly = false;
    [SerializeField] private bool showDominantBiomeOnly = false;
    [SerializeField] private bool showWaterwaysOnly = false;
    [SerializeField] private bool showWaterwayAmountOnly = false;
    [SerializeField] private bool showRiverMaskOnly = false;
    [SerializeField] private bool showLakeMaskOnly = false;
    [SerializeField] private bool showSurfaceWaterMaskOnly = false;
    [SerializeField] private bool showCloudShadowMaskOnly = false;
    [SerializeField] private bool showCoastShelfMaskOnly = false;
    [SerializeField] private bool showShorelineMaskOnly = false;
    [SerializeField] private bool showWetlandMaskOnly = false;
    [SerializeField] private bool showWaterDepthMaskOnly = false;
    [SerializeField, Range(0f, 0.5f)] private float riverChannelCarveStrength = 0.05f;
    [SerializeField, Range(0f, 0.5f)] private float lakeBasinCarveStrength = 0.08f;
    [SerializeField] private bool disableCloudsForDebug = false;
    [SerializeField] private bool showTectonicLandMaskOnly = false;
    [SerializeField] private bool showTectonicHeightOnly = false;
    [SerializeField] private bool showPlateBoundariesOnly = false;
    [SerializeField] private bool showConvergentBoundariesOnly = false;
    [SerializeField] private bool showDivergentBoundariesOnly = false;
    [SerializeField] private bool showMountainUpliftOnly = false;
    [SerializeField] private bool showGeneratedHillReliefOnly = false;
    [SerializeField] private bool showSignedHeightOnly = false;
    [SerializeField] private bool showBasinPotentialOnly = false;
    [SerializeField] private bool showSelectedBasinMaskOnly = false;
    [SerializeField] private bool showExperimentalRiverPathOnly = false;
    [SerializeField] private bool showContinentalShelfOnly = false;
    [SerializeField] private bool showCrustTypeOnly = false;
    [SerializeField] private bool showContinentalPotentialOnly = false;

    [Header("HDRP Post-Processing")]
    [Tooltip("Enable bloom on the preview camera for emissive glow (lava, specular).")]
    [SerializeField] private bool enableBloom = true;
    [Tooltip("Bloom threshold — only pixels brighter than this bloom.")]
    [SerializeField] private float bloomThreshold = 0.8f;
    [Tooltip("Bloom intensity.")]
    [Range(0f, 2f)] [SerializeField] private float bloomIntensity = 0.9f;

    [Header("Mesh Quality")]
    [Tooltip("Subdivisions for generated icosphere. Higher = smoother displacement. 0-6 (6 ≈ 40k tris).")]
    [Range(0,6)] [SerializeField] private int icosphereSubdivisions = 5;

    // -----------------------------------------------------------------
    //  Preview Parameters (exposed in inspector for quick iteration)
    // -----------------------------------------------------------------
    [Header("Land Shape")]
    [Range(0.5f, 5f)]
    [Tooltip("Generator-side landmass shape complexity. Lower values favor simpler, broader continent structures; higher values allow more lobe-rich, irregular planned landmasses.")]
    [SerializeField] private float landScale = 2f;

    [Range(0f, 1f)]
    [Tooltip("Generator-side land amount bias within the chosen land preset. Lower values favor larger landmasses; higher values favor less total land.")]
    [SerializeField] private float landThreshold = 0.4f;

    [Header("Climate")]
    [Range(0f, 2f)]
    [Tooltip("0 = frozen / icy,  0.5 = temperate / green,  1 = hot / arid, values above 1 push extreme heat.")]
    [SerializeField] private float temperature = 0.5f;

    [Range(0f, 2f)]
    [Tooltip("0 = dry / brown,  1 = wet / lush green, values above 1 push extreme humidity.")]
    [SerializeField] private float moisture = 0.5f;

    
    [Header("Advanced Climate Tuning")]
    [SerializeField, Range(0f, 2f)] private float climateNoiseStrength = 0.12f;
    [SerializeField, Range(0f, 2f)] private float coastWetnessStrength = 0.08f;
    [SerializeField, Range(0f, 2f)] private float continentalDrynessStrength = 0.08f;
    [SerializeField, Range(0f, 2f)] private float continentalTemperatureStrength = 0.06f;
    [SerializeField, Range(0f, 2f)] private float riparianWetnessStrength = 0.12f;

    [Header("Advanced Biome Tuning")]
    [SerializeField, Range(0f, 2f)] private float biomeProvinceStrength = 0.20f;
    [SerializeField, Range(0.5f, 3f)] private float biomeCompetitionSharpness = 0.85f;

    [Header("Terrain")]
    [Range(0f, 2f)]
    [Tooltip("0 = flat lowlands,  0.5 = hilly,  1 = extreme mountains with snow peaks, values above 1 exaggerate relief.")]
    [SerializeField] private float elevation = 0.3f;
    [SerializeField, Range(0f, 2f)] private float elevationNoiseStrength = 0.45f;
    [SerializeField, Range(0f, 2f)] private float elevationTemperatureImpact = 0.18f;

    [Header("Map Style")]
    [Range(0f, 1f)]
    [Tooltip("0 = normal world,  1 = infernal/demonic (lava oceans, charred land, volcanic glow, hellish rim).")]
    [SerializeField] private float mapStyle = 0f;

    [Header("Ocean Color")]
    [Tooltip("Ocean color.")]
    [SerializeField] private Color oceanColor = new Color(0.06f, 0.22f, 0.45f, 1f);

    [Header("Biome Tuning")]
    [SerializeField] private bool enableIceCaps = true;
    [Range(0f, 1f)]
    [Tooltip("Ice cap coverage size. 0 = no ice caps, 1 = massive polar ice.")]
    [FormerlySerializedAs("iceCapSize")][SerializeField, HideInInspector] private float legacyIceCapSize = 0.5f;

    [Range(0f, 1.0f)]
    [Tooltip("Blend width at biome band edges. 0 = hard cutoff, 0.03 = subtle transition.")]
    [FormerlySerializedAs("biomeBlend")][SerializeField, HideInInspector] private float legacyBiomeBlend = 0.03f;


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
    [SerializeField] private float brightness = 1.12f;

    [Header("Seed")]
    [Tooltip("Planet noise seed. Randomized each play if randomizeSeed is true.")]
    [SerializeField] private float seed = 0f;

    [Tooltip("Randomize the planet seed on every Awake.")]
    [SerializeField] private bool randomizeSeed = true;
    [SerializeField] private bool autoConfigurePreviewRig = true;
    [SerializeField] private float cameraDistance = 4.0f;
    [SerializeField] private float cameraFov = 28f;
    [SerializeField] private Vector3 cameraLocalEuler = new Vector3(8f, 0f, 0f);
    [SerializeField] private Vector3 keyLightEuler = new Vector3(25f, -35f, 0f);
    [SerializeField] private float keyLightIntensity = 3.0f;
    [SerializeField] private Color keyLightColor = new Color(1.0f, 0.95f, 0.88f, 1f);
    private bool hasStoredRandomSeed;


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

    private bool validateCacheInitialized;
    private float lastValidatedSeed;
    private float lastValidatedLandScale;
    private float lastValidatedLandThreshold;
    private float lastValidatedElevation;
    private float lastValidatedElevationNoiseStrength;
    private float lastValidatedMoisture;
    private float lastValidatedTemperature;
    private int lastValidatedLandPresetIndex;
    private int lastValidatedTerrainPresetIndex;
    private float lastValidatedClimateNoiseStrength;
    private float lastValidatedCoastWetnessStrength;
    private float lastValidatedContinentalDrynessStrength;
    private float lastValidatedContinentalTemperatureStrength;
    private int lastValidatedWaterwaysPreset;
    private float lastValidatedRiparianWetnessStrength;
    private float lastValidatedBiomeProvinceStrength;
    private float lastValidatedBiomeCompetitionSharpness;
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
    private static readonly int ID_WaterwayAmount = Shader.PropertyToID("_WaterwayAmount");
    private static readonly int ID_Elevation     = Shader.PropertyToID("_Elevation");
    private static readonly int ID_MapStyle     = Shader.PropertyToID("_MapStyle");
    private static readonly int ID_OceanColor   = Shader.PropertyToID("_OceanColor");
    private static readonly int ID_IceCapSize    = Shader.PropertyToID("_IceCapSize");
    private static readonly int ID_Seed          = Shader.PropertyToID("_Seed");
    private static readonly int ID_SnowFactor    = Shader.PropertyToID("_SnowFactor");
    private static readonly int ID_DetailScale   = Shader.PropertyToID("_DetailScale");
    private static readonly int ID_DetailStrength= Shader.PropertyToID("_DetailStrength");
    private static readonly int ID_AtmosColor    = Shader.PropertyToID("_AtmosphereColor");
    private static readonly int ID_DisplacementScale = Shader.PropertyToID("_DisplacementScale");
    private static readonly int ID_TerrainElevationDisplacementStrength = Shader.PropertyToID("_TerrainElevationDisplacementStrength");
    private static readonly int ID_LandUpliftStrength = Shader.PropertyToID("_LandUpliftStrength");
    private static readonly int ID_HillDisplacementStrength = Shader.PropertyToID("_HillDisplacementStrength");
    private static readonly int ID_MountainDisplacementStrength = Shader.PropertyToID("_MountainDisplacementStrength");
    private static readonly int ID_IceDisplacementStrength = Shader.PropertyToID("_IceDisplacementStrength");
    private static readonly int ID_VolcanicDisplacementStrength = Shader.PropertyToID("_VolcanicDisplacementStrength");
    private static readonly int ID_OceanDepthStrength = Shader.PropertyToID("_OceanDepthStrength");
    private static readonly int ID_ShowElevationOnly = Shader.PropertyToID("_ShowElevationOnly");
    private static readonly int ID_ShowMountainMaskOnly = Shader.PropertyToID("_ShowMountainMaskOnly");
    private static readonly int ID_ShowDisplacementHeightOnly = Shader.PropertyToID("_ShowDisplacementHeightOnly");
    private static readonly int ID_UseDisplacedNormals = Shader.PropertyToID("_UseDisplacedNormals");

    private static readonly int ID_Smoothness    = Shader.PropertyToID("_Smoothness");
    private static readonly int ID_Metallic      = Shader.PropertyToID("_Metallic");
    private static readonly int ID_AmbientOcclusion = Shader.PropertyToID("_AmbientOcclusion");
    private static readonly int ID_AmbientStrength = Shader.PropertyToID("_AmbientStrength");
    private static readonly int ID_Brightness = Shader.PropertyToID("_Brightness");
    private static readonly int ID_MountainDetailTex = Shader.PropertyToID("_MountainDetailTex");
    private static readonly int ID_IceDetailTex = Shader.PropertyToID("_IceDetailTex");
    private static readonly int ID_IceAlbedoTex = Shader.PropertyToID("_IceAlbedoTex");
    private static readonly int ID_OceanAlbedoTex = Shader.PropertyToID("_OceanAlbedoTex");
    private static readonly int ID_OceanDetailTex = Shader.PropertyToID("_OceanDetailTex");
    private static readonly int ID_WaterwayDetailTex = Shader.PropertyToID("_WaterwayDetailTex");
    private static readonly int ID_WaterwayMaskTex = Shader.PropertyToID("_WaterwayMaskTex");
    private static readonly int ID_WaterwayDepthTex = Shader.PropertyToID("_WaterwayDepthTex");
    private static readonly int ID_RiverChannelCarveStrength = Shader.PropertyToID("_RiverChannelCarveStrength");
    private static readonly int ID_LakeBasinCarveStrength = Shader.PropertyToID("_LakeBasinCarveStrength");
    private static readonly int ID_OceanNormalTex = Shader.PropertyToID("_OceanNormalTex");
    private static readonly int ID_MountainNormalTex = Shader.PropertyToID("_MountainNormalTex");
    private static readonly int ID_IceNormalTex = Shader.PropertyToID("_IceNormalTex");
    private static readonly int ID_OceanSmoothnessTex = Shader.PropertyToID("_OceanSmoothnessTex");
    private static readonly int ID_IceSmoothnessTex = Shader.PropertyToID("_IceSmoothnessTex");
    private static readonly int ID_VolcanicSurfaceSmoothnessTex = Shader.PropertyToID("_VolcanicSmoothnessTex");
    private static readonly int ID_MountainDetailStrength = Shader.PropertyToID("_MountainDetailStrength");
    private static readonly int ID_IceDetailStrength = Shader.PropertyToID("_IceDetailStrength");
    private static readonly int ID_OceanDetailStrength = Shader.PropertyToID("_OceanDetailStrength");
    private static readonly int ID_OceanNormalStrength = Shader.PropertyToID("_OceanNormalStrength");
    private static readonly int ID_MountainNormalStrength = Shader.PropertyToID("_MountainNormalStrength");
    private static readonly int ID_IceNormalStrength = Shader.PropertyToID("_IceNormalStrength");
    private static readonly int ID_TextureDetailScale = Shader.PropertyToID("_TextureDetailScale");
    private static readonly int ID_UseDetailTextures = Shader.PropertyToID("_UseDetailTextures");
    private static readonly int ID_UseTextureDrivenBiomes = Shader.PropertyToID("_UseTextureDrivenBiomes");
    private static readonly int ID_JungleAlbedoTex = Shader.PropertyToID("_JungleAlbedoTex");
    private static readonly int ID_DesertAlbedoTex = Shader.PropertyToID("_DesertAlbedoTex");
    private static readonly int ID_SavannaAlbedoTex = Shader.PropertyToID("_SavannaAlbedoTex");
    private static readonly int ID_TemperateGrassAlbedoTex = Shader.PropertyToID("_TemperateGrassAlbedoTex");
    private static readonly int ID_TemperateForestAlbedoTex = Shader.PropertyToID("_TemperateForestAlbedoTex");
    private static readonly int ID_TaigaAlbedoTex = Shader.PropertyToID("_TaigaAlbedoTex");
    private static readonly int ID_TundraAlbedoTex = Shader.PropertyToID("_TundraAlbedoTex");
    private static readonly int ID_PolarAlbedoTex = Shader.PropertyToID("_PolarAlbedoTex");
    private static readonly int ID_MarshAlbedoTex = Shader.PropertyToID("_MarshAlbedoTex");
    private static readonly int ID_JungleNormalTex = Shader.PropertyToID("_JungleNormalTex");
    private static readonly int ID_DesertNormalTex = Shader.PropertyToID("_DesertNormalTex");
    private static readonly int ID_SavannaNormalTex = Shader.PropertyToID("_SavannaNormalTex");
    private static readonly int ID_TemperateGrassNormalTex = Shader.PropertyToID("_TemperateGrassNormalTex");
    private static readonly int ID_TemperateForestNormalTex = Shader.PropertyToID("_TemperateForestNormalTex");
    private static readonly int ID_TaigaNormalTex = Shader.PropertyToID("_TaigaNormalTex");
    private static readonly int ID_TundraNormalTex = Shader.PropertyToID("_TundraNormalTex");
    private static readonly int ID_PolarNormalTex = Shader.PropertyToID("_PolarNormalTex");
    private static readonly int ID_MarshNormalTex = Shader.PropertyToID("_MarshNormalTex");
    private static readonly int ID_JungleSmoothnessTex = Shader.PropertyToID("_JungleSmoothnessTex");
    private static readonly int ID_DesertSmoothnessTex = Shader.PropertyToID("_DesertSmoothnessTex");
    private static readonly int ID_SavannaSmoothnessTex = Shader.PropertyToID("_SavannaSmoothnessTex");
    private static readonly int ID_TemperateGrassSmoothnessTex = Shader.PropertyToID("_TemperateGrassSmoothnessTex");
    private static readonly int ID_TemperateForestSmoothnessTex = Shader.PropertyToID("_TemperateForestSmoothnessTex");
    private static readonly int ID_TaigaSmoothnessTex = Shader.PropertyToID("_TaigaSmoothnessTex");
    private static readonly int ID_TundraSmoothnessTex = Shader.PropertyToID("_TundraSmoothnessTex");
    private static readonly int ID_PolarSmoothnessTex = Shader.PropertyToID("_PolarSmoothnessTex");
    private static readonly int ID_MarshSmoothnessTex = Shader.PropertyToID("_MarshSmoothnessTex");
    private static readonly int ID_BiomeTextureStrength = Shader.PropertyToID("_BiomeTextureStrength");
    private static readonly int ID_BiomeTintStrength = Shader.PropertyToID("_BiomeTintStrength");
    private static readonly int ID_BiomeNormalStrength = Shader.PropertyToID("_BiomeNormalStrength");
    private static readonly int ID_BiomeTextureScale = Shader.PropertyToID("_BiomeTextureScale");
    private static readonly int ID_BiomeTextureContrast = Shader.PropertyToID("_BiomeTextureContrast");
    private static readonly int ID_ShowLandMaskOnly = Shader.PropertyToID("_ShowLandMaskOnly");
    private static readonly int ID_ShowDetailTexturesOnly = Shader.PropertyToID("_ShowDetailTexturesOnly");
    private static readonly int ID_ShowNormalsOnly = Shader.PropertyToID("_ShowNormalsOnly");
    private static readonly int ID_ShowBiomeWeightsOnly = Shader.PropertyToID("_ShowBiomeWeightsOnly");
    private static readonly int ID_ShowBiomeTextureOnly = Shader.PropertyToID("_ShowBiomeTextureOnly");
    private static readonly int ID_ShowSmoothnessOnly = Shader.PropertyToID("_ShowSmoothnessOnly");
    private static readonly int ID_ShowLocalMoistureOnly = Shader.PropertyToID("_ShowLocalMoistureOnly");
    private static readonly int ID_ShowWaterwaysOnly = Shader.PropertyToID("_ShowWaterwaysOnly");
    private static readonly int ID_ShowWaterwayAmountOnly = Shader.PropertyToID("_ShowWaterwayAmountOnly");
    private static readonly int ID_ShowRiverMaskOnly = Shader.PropertyToID("_ShowRiverMaskOnly");
    private static readonly int ID_ShowLakeMaskOnly = Shader.PropertyToID("_ShowLakeMaskOnly");
    private static readonly int ID_ShowSurfaceWaterMaskOnly = Shader.PropertyToID("_ShowSurfaceWaterMaskOnly");
    private static readonly int ID_ShowLocalTemperatureOnly = Shader.PropertyToID("_ShowLocalTemperatureOnly");
    private static readonly int ID_ShowContinentalityOnly = Shader.PropertyToID("_ShowContinentalityOnly");
    private static readonly int ID_ShowSeasonalityOnly = Shader.PropertyToID("_ShowSeasonalityOnly");
    private static readonly int ID_ShowRainShadowOnly = Shader.PropertyToID("_ShowRainShadowOnly");
    private static readonly int ID_ShowRiparianWetnessOnly = Shader.PropertyToID("_ShowRiparianWetnessOnly");
    private static readonly int ID_ShowDominantBiomeOnly = Shader.PropertyToID("_ShowDominantBiomeOnly");
    private static readonly int ID_ClimateNoiseStrength = Shader.PropertyToID("_ClimateNoiseStrength");
    private static readonly int ID_CoastWetnessStrength = Shader.PropertyToID("_CoastWetnessStrength");
    private static readonly int ID_ContinentalDrynessStrength = Shader.PropertyToID("_ContinentalDrynessStrength");
    private static readonly int ID_ContinentalTemperatureStrength = Shader.PropertyToID("_ContinentalTemperatureStrength");
    private static readonly int ID_RiparianWetnessStrength = Shader.PropertyToID("_RiparianWetnessStrength");
    private static readonly int ID_BiomeProvinceStrength = Shader.PropertyToID("_BiomeProvinceStrength");
    private static readonly int ID_BiomeCompetitionSharpness = Shader.PropertyToID("_BiomeCompetitionSharpness");

    private static readonly int ID_VolcanicRockTex = Shader.PropertyToID("_VolcanicRockTex");
    private static readonly int ID_LavaCrackTex = Shader.PropertyToID("_LavaCrackTex");
    private static readonly int ID_LavaEmissiveTex = Shader.PropertyToID("_LavaEmissiveTex");
    private static readonly int ID_AshDetailTex = Shader.PropertyToID("_AshDetailTex");
    private static readonly int ID_VolcanicRockStrength = Shader.PropertyToID("_VolcanicRockStrength");
    private static readonly int ID_LavaCrackStrength = Shader.PropertyToID("_LavaCrackStrength");
    private static readonly int ID_LavaEmissionStrength = Shader.PropertyToID("_LavaEmissionStrength");
    private static readonly int ID_LavaTextureScale = Shader.PropertyToID("_LavaTextureScale");
    private static readonly int ID_AshDetailStrength = Shader.PropertyToID("_AshDetailStrength");
     private static readonly int ID_OceanShallowColor = Shader.PropertyToID("_OceanShallowColor");
    private static readonly int ID_LandSmoothness = Shader.PropertyToID("_LandSmoothness");
    private static readonly int ID_OceanSmoothness = Shader.PropertyToID("_OceanSmoothness");
    private static readonly int ID_OceanSpecularStrength = Shader.PropertyToID("_OceanSpecularStrength");
    private static readonly int ID_OceanFresnelStrength = Shader.PropertyToID("_OceanFresnelStrength");
    private static readonly int ID_OceanFresnelColor = Shader.PropertyToID("_OceanFresnelColor");
    private static readonly int ID_TerminatorSoftness = Shader.PropertyToID("_TerminatorSoftness");
    private static readonly int ID_KeyLightDirectionWS = Shader.PropertyToID("_KeyLightDirectionWS");
    private static readonly int ID_KeyLightColor = Shader.PropertyToID("_KeyLightColor");
    private static readonly int ID_KeyLightIntensity = Shader.PropertyToID("_KeyLightIntensity");
    private static readonly int ID_FillLightColor = Shader.PropertyToID("_FillLightColor");
    private static readonly int ID_FillLightIntensity = Shader.PropertyToID("_FillLightIntensity");
    private static readonly int ID_RimLightColor = Shader.PropertyToID("_RimLightColor");
    private static readonly int ID_RimLightIntensity = Shader.PropertyToID("_RimLightIntensity");
    private static readonly int ID_CloudColor = Shader.PropertyToID("_CloudColor");
    private static readonly int ID_CloudNoiseTex = Shader.PropertyToID("_CloudNoiseTex");
    private static readonly int ID_CloudSoftness = Shader.PropertyToID("_CloudSoftness");
    private static readonly int ID_CloudShadowStrength = Shader.PropertyToID("_CloudShadowStrength");



    // Cloud-specific
    private static readonly int ID_CloudDensity  = Shader.PropertyToID("_CloudDensity");
    private static readonly int ID_CloudScale    = Shader.PropertyToID("_CloudScale");
    private static readonly int ID_CloudSpeed    = Shader.PropertyToID("_CloudSpeed");
    private static readonly int ID_CloudAltitude = Shader.PropertyToID("_CloudAltitude");
    private static readonly int ID_CloudShadowDensity = Shader.PropertyToID("_CloudShadowDensity");
    private static readonly int ID_CloudShadowScale = Shader.PropertyToID("_CloudShadowScale");
    private static readonly int ID_CloudShadowSpeed = Shader.PropertyToID("_CloudShadowSpeed");
    private static readonly int ID_CloudSurfaceShadowStrength = Shader.PropertyToID("_CloudSurfaceShadowStrength");

    // Atmosphere shell-specific
    private static readonly int ID_AtmosFalloff  = Shader.PropertyToID("_AtmosphereFalloff");
    private static readonly int ID_AtmosIntensity = Shader.PropertyToID("_AtmosphereIntensity");
    private static readonly int ID_ShowCloudShadowMaskOnly = Shader.PropertyToID("_ShowCloudShadowMaskOnly");
    private static readonly int ID_ShowCoastShelfMaskOnly = Shader.PropertyToID("_ShowCoastShelfMaskOnly");
    private static readonly int ID_ShowShorelineMaskOnly = Shader.PropertyToID("_ShowShorelineMaskOnly");
    private static readonly int ID_ShowWetlandMaskOnly = Shader.PropertyToID("_ShowWetlandMaskOnly");
    private static readonly int ID_ShowWaterDepthMaskOnly = Shader.PropertyToID("_ShowWaterDepthMaskOnly");
    private static readonly int ID_AtmosLightDirectionWS = Shader.PropertyToID("_AtmosLightDirectionWS");
    private static readonly int ID_AtmosDayRimBoost = Shader.PropertyToID("_AtmosphereDayRimBoost");
    private static readonly int ID_AtmosNightRimStrength = Shader.PropertyToID("_AtmosphereNightRimStrength");
    private static readonly int ID_AtmosInnerScatterStrength = Shader.PropertyToID("_AtmosphereInnerScatterStrength");
    private static readonly int ID_TectonicSurfaceTex = Shader.PropertyToID("_TectonicSurfaceTex");
    private static readonly int ID_TectonicBoundaryTex = Shader.PropertyToID("_TectonicBoundaryTex");
    private static readonly int ID_TectonicCrustTex = Shader.PropertyToID("_TectonicCrustTex");
    private static readonly int ID_GpuHeightTex = Shader.PropertyToID("_GpuHeightTex");
    private static readonly int ID_UseTectonicPreview = Shader.PropertyToID("_UseTectonicPreview");
    private static readonly int ID_UseExperimentalSignedTerrain = Shader.PropertyToID("_UseExperimentalSignedTerrain");
    private static readonly int ID_ShowSignedHeightOnly = Shader.PropertyToID("_ShowSignedHeightOnly");
    private static readonly int ID_ShowBasinPotentialOnly = Shader.PropertyToID("_ShowBasinPotentialOnly");
    private static readonly int ID_ShowSelectedBasinMaskOnly = Shader.PropertyToID("_ShowSelectedBasinMaskOnly");
    private static readonly int ID_ShowExperimentalRiverPathOnly = Shader.PropertyToID("_ShowExperimentalRiverPathOnly");
    private static readonly int ID_ShowTectonicLandMaskOnly = Shader.PropertyToID("_ShowTectonicLandMaskOnly");
    private static readonly int ID_ShowTectonicHeightOnly = Shader.PropertyToID("_ShowTectonicHeightOnly");
    private static readonly int ID_ShowPlateBoundariesOnly = Shader.PropertyToID("_ShowPlateBoundariesOnly");
    private static readonly int ID_ShowConvergentBoundariesOnly = Shader.PropertyToID("_ShowConvergentBoundariesOnly");
    private static readonly int ID_ShowDivergentBoundariesOnly = Shader.PropertyToID("_ShowDivergentBoundariesOnly");
    private static readonly int ID_ShowMountainUpliftOnly = Shader.PropertyToID("_ShowMountainUpliftOnly");
    private static readonly int ID_ShowGeneratedHillReliefOnly = Shader.PropertyToID("_ShowGeneratedHillReliefOnly");
    private static readonly int ID_ShowContinentalShelfOnly = Shader.PropertyToID("_ShowContinentalShelfOnly");
    private static readonly int ID_ShowCrustTypeOnly = Shader.PropertyToID("_ShowCrustTypeOnly");
    private static readonly int ID_ShowContinentalPotentialOnly = Shader.PropertyToID("_ShowContinentalPotentialOnly");


    private int currentLandPresetIndex = 2;
    private int currentTerrainRoughnessPresetIndex = 2;

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
        SetupWorldGenerator();

        // Randomize seed so each play session gets a unique planet
        if (randomizeSeed)
        {
            seed = Random.Range(0f, 10000f);
        }

        ApplyAllParameters();

        worldGenerator.WorldTexturesUpdated -= BindGeneratedWorldTextures;
        worldGenerator.WorldTexturesUpdated += BindGeneratedWorldTextures;
        RequestWorldRebuild(PreviewWorldRebuildScope.Tectonics, false);
        CacheValidatedGeneratorInputs();

        SetupSpaceBackgroundIfNeeded();

        // Upgrade the preview mesh for better shading/detail
        TryReplacePreviewMesh();
        ConfigurePreviewRig();

        SetupCloudShell();
        SetupAtmosphereShell();
        CacheBaseScales();

        // Enable bloom on the preview camera
        SetupBloomVolume();
    }


    private void SetupWorldGenerator()
    {
        if (worldGenerator == null)
            worldGenerator = GetComponent<MenuPlanetPreviewWorldGeneratorV2>();

        if (worldGenerator == null)
            worldGenerator = gameObject.AddComponent<MenuPlanetPreviewWorldGeneratorV2>();

        if (worldGenerator != null)
            worldGenerator.SetInputs(BuildWorldInputs());
    }

    private MenuPlanetPreviewWorldInputs BuildWorldInputs()
    {
        return new MenuPlanetPreviewWorldInputs
        {
            seed = seed, landScale = landScale, landThreshold = landThreshold, landPresetIndex = currentLandPresetIndex, elevation = elevation,
            terrainRoughnessPresetIndex = currentTerrainRoughnessPresetIndex,
            elevationNoiseStrength = elevationNoiseStrength, elevationTemperatureImpact = elevationTemperatureImpact, enableIceCaps = enableIceCaps,
            temperature = temperature, moisture = moisture, waterwaysPreset = waterwaysPreset,
            climateNoiseStrength = climateNoiseStrength,
            coastWetnessStrength = coastWetnessStrength, continentalDrynessStrength = continentalDrynessStrength, continentalTemperatureStrength = continentalTemperatureStrength,
            riparianWetnessStrength = riparianWetnessStrength,

            biomeProvinceStrength = biomeProvinceStrength, biomeCompetitionSharpness = biomeCompetitionSharpness
        };
    }

    private void RequestWorldRebuild(PreviewWorldRebuildScope scope, bool immediate = false)
    {
        if (worldGenerator == null) return;
        worldGenerator.SetInputs(BuildWorldInputs());
        worldGenerator.RequestRebuild(scope, immediate);
    }

    private void BindGeneratedWorldTextures()
    {
        if (materialInstance == null || worldGenerator == null) return;
        materialInstance.SetTexture(ID_TectonicSurfaceTex, worldGenerator.TectonicSurfaceTexture);
        materialInstance.SetTexture(ID_TectonicBoundaryTex, worldGenerator.TectonicBoundaryTexture);
        materialInstance.SetTexture(ID_TectonicCrustTex, worldGenerator.TectonicCrustTexture);
        materialInstance.SetTexture(ID_GpuHeightTex, worldGenerator.GpuHeightTexture);
        materialInstance.SetTexture(ID_WaterwayMaskTex, worldGenerator.ActiveHydrologyTexture);
        materialInstance.SetTexture(ID_WaterwayDepthTex, worldGenerator.ActiveHydrologyDepthTexture);
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
        if (worldGenerator != null) worldGenerator.WorldTexturesUpdated -= BindGeneratedWorldTextures;
        if (materialInstance != null) { Destroy(materialInstance); materialInstance = null; }
        if (worldGenerator != null) { worldGenerator.Release(); }
        if (cloudMaterialInstance != null) { Destroy(cloudMaterialInstance); cloudMaterialInstance = null; }
        if (atmosphereMaterialInstance != null) { Destroy(atmosphereMaterialInstance); atmosphereMaterialInstance = null; }
    }

    /// <summary>
    /// When values change in the Inspector during Play mode, push them to the material.
    /// </summary>
    private void OnValidate()
    {
        if (!Application.isPlaying || materialInstance == null)
            return;

        bool tectonicChanged = TectonicInputsChanged();
        bool gpuHeightChanged = GpuHeightInputsChanged();
        bool gpuHydrologyChanged = GpuHydrologyInputsChanged();

        ApplyAllParameters();
        PushCloudParameters();
        PushAtmosphereParameters();
        PushInfernalTextureParameters();

        if (atmosphereShellGO != null && previewRenderer != null)
            atmosphereShellGO.transform.localScale = previewRenderer.transform.localScale * atmosphereShellScale;

        if (tectonicChanged)
            RequestWorldRebuild(PreviewWorldRebuildScope.Tectonics, false);
        else
        {
            bool rebound = false;
            if (gpuHeightChanged && worldGenerator != null)
            {
                worldGenerator.RefreshGpuHeightOnly(BuildWorldInputs());
                worldGenerator.RefreshGpuHydrologyOnly(BuildWorldInputs());
                rebound = true;
            }
            else if (gpuHydrologyChanged && worldGenerator != null)
            {
                worldGenerator.RefreshGpuHydrologyOnly(BuildWorldInputs());
                rebound = true;
            }
            if (rebound) BindGeneratedWorldTextures();
        }

        CacheValidatedGeneratorInputs();
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
        PushBiomeTextureParameters();
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
        // Ocean color
        materialInstance.SetColor(ID_OceanColor,    oceanColor);

        // Biome tuning
        materialInstance.SetFloat(ID_IceCapSize,    enableIceCaps ? legacyIceCapSize : 0f);

        // Update biome-related visual parameters derived from temperature/moisture/elevation
        RecalculateDerivedVisuals();

        // Push detail params (old atmosphere props no longer pushed — handled by shell)
        materialInstance.SetFloat(ID_DetailScale, detailScale);
        materialInstance.SetFloat(ID_DetailStrength, detailStrength);

        // Displacement
        materialInstance.SetFloat(ID_DisplacementScale, displacementScale);
        PushDisplacementParameters();

        // Surface properties
        materialInstance.SetFloat(ID_Smoothness, smoothness);
        materialInstance.SetFloat(ID_Metallic, metallic);
        materialInstance.SetFloat(ID_AmbientOcclusion, ambientOcclusion);
        materialInstance.SetFloat(ID_AmbientStrength, ambientStrength);
        materialInstance.SetFloat(ID_Brightness, brightness);
        PushDetailTextureParameters();
        PushBiomeTextureParameters();
        PushInfernalTextureParameters();
        PushAdvancedClimateParameters();
        PushSurfaceCloudShadowParameters();
    }

    private void PushDisplacementParameters()
    {
        if (materialInstance == null) return;
        materialInstance.SetFloat(ID_LandUpliftStrength, landUpliftStrength);
        materialInstance.SetFloat(ID_HillDisplacementStrength, hillDisplacementStrength);
        materialInstance.SetFloat(ID_MountainDisplacementStrength, mountainDisplacementStrength);
        materialInstance.SetFloat(ID_TerrainElevationDisplacementStrength, terrainElevationDisplacementStrength);
        materialInstance.SetFloat(ID_IceDisplacementStrength, iceDisplacementStrength);
        materialInstance.SetFloat(ID_VolcanicDisplacementStrength, volcanicDisplacementStrength);
        materialInstance.SetFloat(ID_OceanDepthStrength, oceanDepthStrength);
        materialInstance.SetFloat(ID_RiverChannelCarveStrength, riverChannelCarveStrength);
        materialInstance.SetFloat(ID_LakeBasinCarveStrength, lakeBasinCarveStrength);
        materialInstance.SetFloat(ID_ShowElevationOnly, showElevationOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowMountainMaskOnly, showMountainMaskOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowDisplacementHeightOnly, showDisplacementHeightOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_UseDisplacedNormals, useDisplacedNormals ? 1f : 0f);
    }

    private void PushDetailTextureParameters()
    {
        if (materialInstance == null) return;

        materialInstance.SetTexture(ID_MountainDetailTex, mountainDetailTexture);
        materialInstance.SetTexture(ID_IceDetailTex, iceDetailTexture);
        materialInstance.SetTexture(ID_IceAlbedoTex, iceAlbedoTexture);
        materialInstance.SetTexture(ID_OceanAlbedoTex, oceanAlbedoTexture);
        materialInstance.SetTexture(ID_OceanDetailTex, oceanDetailTexture);
        materialInstance.SetTexture(ID_WaterwayDetailTex, waterwayDetailTexture != null ? waterwayDetailTexture : oceanDetailTexture);
        if (worldGenerator != null && worldGenerator.ActiveHydrologyTexture != null) materialInstance.SetTexture(ID_WaterwayMaskTex, worldGenerator.ActiveHydrologyTexture);
        materialInstance.SetTexture(ID_OceanNormalTex, oceanNormalTexture);
        materialInstance.SetTexture(ID_MountainNormalTex, mountainNormalTexture);
        materialInstance.SetTexture(ID_IceNormalTex, iceNormalTexture);
        materialInstance.SetTexture(ID_OceanSmoothnessTex, oceanSmoothnessTexture);
        materialInstance.SetTexture(ID_IceSmoothnessTex, iceSmoothnessTexture);
        materialInstance.SetTexture(ID_VolcanicSurfaceSmoothnessTex, volcanicSurfaceSmoothnessTexture);
        materialInstance.SetFloat(ID_MountainDetailStrength, mountainDetailStrength);
        materialInstance.SetFloat(ID_IceDetailStrength, iceDetailStrength);
        materialInstance.SetFloat(ID_OceanDetailStrength, oceanDetailStrength);
        materialInstance.SetFloat(ID_OceanNormalStrength, oceanNormalStrength);
        materialInstance.SetFloat(ID_MountainNormalStrength, mountainNormalStrength);
        materialInstance.SetFloat(ID_IceNormalStrength, iceNormalStrength);
        materialInstance.SetFloat(ID_TextureDetailScale, textureDetailScale);
        materialInstance.SetFloat(ID_ShowLandMaskOnly, showLandMaskOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowDetailTexturesOnly, showDetailTexturesOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowNormalsOnly, showNormalsOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowBiomeWeightsOnly, showBiomeWeightsOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowBiomeTextureOnly, showBiomeTextureOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowSmoothnessOnly, showSmoothnessOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowLocalMoistureOnly, showLocalMoistureOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowLocalTemperatureOnly, showLocalTemperatureOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowContinentalityOnly, showContinentalityOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowSeasonalityOnly, showSeasonalityOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowRainShadowOnly, showRainShadowOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowRiparianWetnessOnly, showRiparianWetnessOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowDominantBiomeOnly, showDominantBiomeOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowWaterwaysOnly, showWaterwaysOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowWaterwayAmountOnly, showWaterwayAmountOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowRiverMaskOnly, showRiverMaskOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowLakeMaskOnly, showLakeMaskOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowSurfaceWaterMaskOnly, showSurfaceWaterMaskOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowCloudShadowMaskOnly, showCloudShadowMaskOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowCoastShelfMaskOnly, showCoastShelfMaskOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowShorelineMaskOnly, showShorelineMaskOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowWetlandMaskOnly, showWetlandMaskOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowWaterDepthMaskOnly, showWaterDepthMaskOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_UseTectonicPreview, 1f);
        materialInstance.SetFloat(ID_UseExperimentalSignedTerrain, worldGenerator != null && worldGenerator.UseExperimentalSignedTerrain ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowSignedHeightOnly, showSignedHeightOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowBasinPotentialOnly, showBasinPotentialOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowSelectedBasinMaskOnly, showSelectedBasinMaskOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowExperimentalRiverPathOnly, showExperimentalRiverPathOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowTectonicLandMaskOnly, showTectonicLandMaskOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowTectonicHeightOnly, showTectonicHeightOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowPlateBoundariesOnly, showPlateBoundariesOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowConvergentBoundariesOnly, showConvergentBoundariesOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowDivergentBoundariesOnly, showDivergentBoundariesOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowMountainUpliftOnly, showMountainUpliftOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowGeneratedHillReliefOnly, showGeneratedHillReliefOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowContinentalShelfOnly, showContinentalShelfOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowCrustTypeOnly, showCrustTypeOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowContinentalPotentialOnly, showContinentalPotentialOnly ? 1f : 0f);
        bool useDetails = mountainDetailTexture != null || iceDetailTexture != null || iceAlbedoTexture != null ||
                          oceanDetailTexture != null || waterwayDetailTexture != null || oceanNormalTexture != null ||
                          mountainNormalTexture != null || iceNormalTexture != null ||
                          oceanSmoothnessTexture != null || iceSmoothnessTexture != null ||
                          volcanicSurfaceSmoothnessTexture != null ||
                          jungleAlbedoTexture != null || desertAlbedoTexture != null || savannaAlbedoTexture != null ||
                          temperateGrassAlbedoTexture != null || temperateForestAlbedoTexture != null ||
                          taigaAlbedoTexture != null ||
                          tundraAlbedoTexture != null || polarAlbedoTexture != null || marshAlbedoTexture != null ||
                          jungleNormalTexture != null || desertNormalTexture != null || savannaNormalTexture != null ||
                          temperateGrassNormalTexture != null || temperateForestNormalTexture != null ||
                          taigaNormalTexture != null ||
                          tundraNormalTexture != null || polarNormalTexture != null || marshNormalTexture != null;
        materialInstance.SetFloat(ID_UseDetailTextures, useDetails ? 1f : 0f);
    }

    private void PushBiomeTextureParameters()
    {
        if (materialInstance == null) return;
        materialInstance.SetTexture(ID_JungleAlbedoTex, jungleAlbedoTexture);
        materialInstance.SetTexture(ID_DesertAlbedoTex, desertAlbedoTexture);
        materialInstance.SetTexture(ID_SavannaAlbedoTex, savannaAlbedoTexture);
        materialInstance.SetTexture(ID_TemperateGrassAlbedoTex, temperateGrassAlbedoTexture);
        materialInstance.SetTexture(ID_TemperateForestAlbedoTex, temperateForestAlbedoTexture);
        materialInstance.SetTexture(ID_TaigaAlbedoTex, taigaAlbedoTexture);
        materialInstance.SetTexture(ID_TundraAlbedoTex, tundraAlbedoTexture);
        materialInstance.SetTexture(ID_PolarAlbedoTex, polarAlbedoTexture);
        materialInstance.SetTexture(ID_MarshAlbedoTex, marshAlbedoTexture);
        materialInstance.SetTexture(ID_JungleNormalTex, jungleNormalTexture);
        materialInstance.SetTexture(ID_DesertNormalTex, desertNormalTexture);
        materialInstance.SetTexture(ID_SavannaNormalTex, savannaNormalTexture);
        materialInstance.SetTexture(ID_TemperateGrassNormalTex, temperateGrassNormalTexture);
        materialInstance.SetTexture(ID_TemperateForestNormalTex, temperateForestNormalTexture);
        materialInstance.SetTexture(ID_TaigaNormalTex, taigaNormalTexture);
        materialInstance.SetTexture(ID_TundraNormalTex, tundraNormalTexture);
        materialInstance.SetTexture(ID_PolarNormalTex, polarNormalTexture);
        materialInstance.SetTexture(ID_MarshNormalTex, marshNormalTexture);
        materialInstance.SetTexture(ID_JungleSmoothnessTex, jungleSmoothnessTexture);
        materialInstance.SetTexture(ID_DesertSmoothnessTex, desertSmoothnessTexture);
        materialInstance.SetTexture(ID_SavannaSmoothnessTex, savannaSmoothnessTexture);
        materialInstance.SetTexture(ID_TemperateGrassSmoothnessTex, temperateGrassSmoothnessTexture);
        materialInstance.SetTexture(ID_TemperateForestSmoothnessTex, temperateForestSmoothnessTexture);
        materialInstance.SetTexture(ID_TaigaSmoothnessTex, taigaSmoothnessTexture);
        materialInstance.SetTexture(ID_TundraSmoothnessTex, tundraSmoothnessTexture);
        materialInstance.SetTexture(ID_PolarSmoothnessTex, polarSmoothnessTexture);
        materialInstance.SetTexture(ID_MarshSmoothnessTex, marshSmoothnessTexture);
        materialInstance.SetFloat(ID_BiomeTextureStrength, biomeTextureStrength);
        materialInstance.SetFloat(ID_BiomeTintStrength, climateGradeStrength);
        materialInstance.SetFloat(ID_BiomeNormalStrength, biomeNormalStrength);
        materialInstance.SetFloat(ID_BiomeTextureScale, biomeTextureScale);
        materialInstance.SetFloat(ID_BiomeTextureContrast, biomeTextureContrast);
        materialInstance.SetFloat(ID_ShowBiomeWeightsOnly, showBiomeWeightsOnly ? 1f : 0f);
        materialInstance.SetFloat(ID_ShowBiomeTextureOnly, showBiomeTextureOnly ? 1f : 0f);
    }

    private void PushInfernalTextureParameters()
    {
        if (materialInstance == null) return;

        materialInstance.SetTexture(ID_VolcanicRockTex, volcanicRockTexture);
        materialInstance.SetTexture(ID_LavaCrackTex, lavaCrackTexture);
        materialInstance.SetTexture(ID_LavaEmissiveTex, lavaEmissiveTexture);
        materialInstance.SetTexture(ID_AshDetailTex, ashDetailTexture);
        materialInstance.SetFloat(ID_VolcanicRockStrength, volcanicRockStrength);
        materialInstance.SetFloat(ID_LavaCrackStrength, lavaCrackStrength);
        materialInstance.SetFloat(ID_LavaEmissionStrength, lavaEmissionStrength);
        materialInstance.SetFloat(ID_LavaTextureScale, lavaTextureScale);
        materialInstance.SetFloat(ID_AshDetailStrength, ashDetailStrength);
    }
    private void PushAdvancedClimateParameters()
    {
        if (materialInstance == null) return;
        materialInstance.SetFloat(ID_ClimateNoiseStrength, climateNoiseStrength);
        materialInstance.SetFloat(ID_CoastWetnessStrength, coastWetnessStrength);
        materialInstance.SetFloat(ID_ContinentalDrynessStrength, continentalDrynessStrength);
        materialInstance.SetFloat(ID_ContinentalTemperatureStrength, continentalTemperatureStrength);
        materialInstance.SetFloat(ID_RiparianWetnessStrength, riparianWetnessStrength);
        materialInstance.SetFloat(ID_BiomeProvinceStrength, biomeProvinceStrength);
        materialInstance.SetFloat(ID_BiomeCompetitionSharpness, biomeCompetitionSharpness);
    }


    private void CacheValidatedGeneratorInputs()
    {
        lastValidatedSeed = seed;
        lastValidatedLandScale = landScale;
        lastValidatedLandThreshold = landThreshold;
        lastValidatedLandPresetIndex = currentLandPresetIndex;
        lastValidatedTerrainPresetIndex = currentTerrainRoughnessPresetIndex;
        lastValidatedElevation = elevation;
        lastValidatedElevationNoiseStrength = elevationNoiseStrength;

        lastValidatedMoisture = moisture;
        lastValidatedTemperature = temperature;

        lastValidatedClimateNoiseStrength = climateNoiseStrength;
        lastValidatedCoastWetnessStrength = coastWetnessStrength;
        lastValidatedContinentalDrynessStrength = continentalDrynessStrength;
        lastValidatedContinentalTemperatureStrength = continentalTemperatureStrength;
        lastValidatedRiparianWetnessStrength = riparianWetnessStrength;

        lastValidatedWaterwaysPreset = waterwaysPreset;

        lastValidatedBiomeProvinceStrength = biomeProvinceStrength;
        lastValidatedBiomeCompetitionSharpness = biomeCompetitionSharpness;

        validateCacheInitialized = true;
    }

    private bool TectonicInputsChanged()
    {
        if (!validateCacheInitialized) return true;

        return
            !Mathf.Approximately(seed, lastValidatedSeed) ||
            !Mathf.Approximately(landScale, lastValidatedLandScale) ||
            !Mathf.Approximately(landThreshold, lastValidatedLandThreshold) ||
            currentLandPresetIndex != lastValidatedLandPresetIndex;
    }

    private bool GpuHydrologyInputsChanged()
    {
        if (!validateCacheInitialized) return true;

        return !Mathf.Approximately(moisture, lastValidatedMoisture) || waterwaysPreset != lastValidatedWaterwaysPreset;
    }

    private bool GpuHeightInputsChanged()
    {
        if (!validateCacheInitialized) return true;

        return !Mathf.Approximately(elevation, lastValidatedElevation) ||
               !Mathf.Approximately(elevationNoiseStrength, lastValidatedElevationNoiseStrength) ||
               currentTerrainRoughnessPresetIndex != lastValidatedTerrainPresetIndex;
    }

    public void SetTerrainRoughnessPreset(int terrainPresetIndex)
    {
        int nextPreset = Mathf.Clamp(terrainPresetIndex, 0, 4);
        if (currentTerrainRoughnessPresetIndex == nextPreset)
            return;

        currentTerrainRoughnessPresetIndex = nextPreset;

        if (worldGenerator != null)
        {
            worldGenerator.RefreshGpuHeightOnly(BuildWorldInputs());
            worldGenerator.RefreshGpuHydrologyOnly(BuildWorldInputs());
            BindGeneratedWorldTextures();
        }
    }

    // -----------------------------------------------------------------
    //  Public API — called by UI sliders / MainMenuManager
    // -----------------------------------------------------------------

    /// <summary>
    /// Set land shape from a preset (e.g., Archipelago, Pangaea).
    ///   scale:     blob frequency (0.5 = huge continents, 5.0 = tiny islands)
    ///   threshold: land/ocean ratio (0 = nearly all land, 1 = nearly all ocean)
    /// </summary>
    public void SetLandPreset(float scale, float threshold, int landPresetIndex)
    {
        float nextScale = Mathf.Clamp(scale, 0.5f, 5f);
        float nextThreshold = Mathf.Clamp01(threshold);
        int nextPreset = Mathf.Clamp(landPresetIndex, 0, 5);

        bool changed =
            !Mathf.Approximately(landScale, nextScale) ||
            !Mathf.Approximately(landThreshold, nextThreshold) ||
            currentLandPresetIndex != nextPreset;

        if (!changed)
            return;

        landScale = nextScale;
        landThreshold = nextThreshold;
        currentLandPresetIndex = nextPreset;

        if (materialInstance != null)
        {
            materialInstance.SetFloat(ID_LandScale, landScale);
            materialInstance.SetFloat(ID_LandThreshold, landThreshold);
            RecalculateDerivedVisuals();
        }

        RequestWorldRebuild(PreviewWorldRebuildScope.Tectonics);
    }

    public void SetLandPreset(float scale, float threshold)
    {
        SetLandPreset(scale, threshold, currentLandPresetIndex);
    }

    /// <summary>
    /// Set temperature (0 = frozen,  0.5 = temperate,  1 = scorching).
    /// Affects land color: icy blue-gray → green → sandy tan.
    /// </summary>
    public void SetTemperature(float value)
    {
        float next = Mathf.Clamp01(value);
        if (Mathf.Approximately(temperature, next))
            return;

        temperature = next;

        if (materialInstance != null)
        {
            materialInstance.SetFloat(ID_Temperature, temperature);
             RecalculateDerivedVisuals();
        }
        PushCloudParameters();
        PushSurfaceCloudShadowParameters();
        PushAtmosphereParameters();
    }

    private void PushSurfaceCloudShadowParameters()
    {
        if (materialInstance == null) return;
        materialInstance.SetFloat(ID_CloudShadowDensity, cloudDensity);
        materialInstance.SetFloat(ID_CloudShadowScale, cloudScale);
        materialInstance.SetFloat(ID_CloudShadowSpeed, cloudSpeed);
        materialInstance.SetFloat(ID_CloudSurfaceShadowStrength, cloudSurfaceShadowStrength);
        materialInstance.SetTexture(ID_CloudNoiseTex, cloudNoiseTexture);
    }

    /// <summary>
    /// Set moisture (0 = dry / deserts,  1 = wet / lush).
    /// Affects biome wetness/climate and cloudiness. Does not directly control visible river/lake count.
    /// </summary>
    public void SetMoisture(float value)
    {
        float next = Mathf.Clamp01(value);
        if (Mathf.Approximately(moisture, next))
            return;

        moisture = next;

        if (materialInstance != null)
        {
            materialInstance.SetFloat(ID_Moisture, moisture);
            RecalculateDerivedVisuals();
        }

        if (worldGenerator != null)
        {
            worldGenerator.RefreshGpuHydrologyOnly(BuildWorldInputs());
            BindGeneratedWorldTextures();
        }
    }

    /// <summary>
    /// Set elevation (0 = flat lowlands,  0.5 = hilly,  1 = extreme mountains).
    /// Affects terrain color banding: lowlands → highlands → rocky gray → snow peaks.
    /// Rivers fade on the highest peaks.
    /// </summary>
    public void SetElevation(float value)
    {
        float next = Mathf.Clamp01(value);
        if (Mathf.Approximately(elevation, next))
            return;

        elevation = next;

        if (materialInstance != null)
        {
            materialInstance.SetFloat(ID_Elevation, elevation);
            PushDisplacementParameters();
            RecalculateDerivedVisuals();
        }

        if (worldGenerator != null)
        {
            worldGenerator.RefreshGpuHeightOnly(BuildWorldInputs());
            worldGenerator.RefreshGpuHydrologyOnly(BuildWorldInputs());
            BindGeneratedWorldTextures();
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
        PushDisplacementParameters();
    }

    [System.Obsolete("Legacy color-driven biome setters are no-ops. Texture-driven biomes are authoritative.")]
    public void SetEquatorialColor(Color value) { }

    /// <summary>Set the desert sand color used for dry equatorial regions.</summary>
    public void SetDesertSand(Color value) { }

    /// <summary>Set the subtropical savanna color.</summary>
    public void SetSubtropicalColor(Color value) { }

    /// <summary>Set the temperate grassland/forest color.</summary>
    public void SetTemperateZoneColor(Color value) { }

    /// <summary>Set the tundra barren color.</summary>
    public void SetTundraColor(Color value) { }

    /// <summary>Set the polar ice/snow color.</summary>
    public void SetPolarColor(Color value) { }

    /// <summary>Set the ocean color.</summary>
    public void SetOceanColor(Color value)
    {
        oceanColor = value;
        if (materialInstance != null) { materialInstance.SetColor(ID_OceanColor, oceanColor); }
    }

    [System.Obsolete("Ice Cap Size no longer affects the Option B generated preview. Snow/ice is climate-driven.")]
    public void SetIceCapSize(float value)
    {
        legacyIceCapSize = Mathf.Clamp01(value);
        if (materialInstance != null) { materialInstance.SetFloat(ID_IceCapSize, legacyIceCapSize); }
    }

    [System.Obsolete("Biome Blend is a legacy fallback control and no longer affects the Option B generated preview pipeline.")]
    public void SetBiomeBlend(float value)
    {
        legacyBiomeBlend = Mathf.Clamp(value, 0f, 0.1f);
    }

public void SetWorldSeed(int worldSeed, bool randomSeed, bool forceReroll = false)
    {
        bool previousRandomizeSeed = randomizeSeed;
        float previousSeed = seed;

        randomizeSeed = randomSeed;
        if (!randomSeed)
        {
            seed = Mathf.Abs(worldSeed % 100000);
            hasStoredRandomSeed = false;
        }
        else if (!hasStoredRandomSeed || forceReroll)
        {
            seed = Random.Range(0f, 10000f);
            hasStoredRandomSeed = true;
        }

        bool changed =
            previousRandomizeSeed != randomizeSeed ||
            !Mathf.Approximately(previousSeed, seed) ||
            forceReroll;

        if (!changed)
            return;

        if (materialInstance != null)
            materialInstance.SetFloat(ID_Seed, seed);

        RecalculateDerivedVisuals();
        RequestWorldRebuild(PreviewWorldRebuildScope.Tectonics);
    }

        public void RandomizePreviewSeed() => SetWorldSeed(Mathf.RoundToInt(seed), true, true);

    public void SetWaterwaysPreset(int preset)
    {
        int next = Mathf.Clamp(preset, 0, 2);
        if (waterwaysPreset == next)
            return;

        waterwaysPreset = next;
        RecalculateDerivedVisuals();

        if (worldGenerator != null)
        {
            worldGenerator.RefreshGpuHydrologyOnly(BuildWorldInputs());
            BindGeneratedWorldTextures();
        }

        PushCloudParameters();
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
    private void RecalculateDerivedVisuals()
    {
        if (materialInstance == null) return;

        float effectiveIceCapSize = enableIceCaps ? legacyIceCapSize : 0f;

        float coldness = 1f - temperature;
        float snowFactor = enableIceCaps
            ? Mathf.Clamp01(coldness * Mathf.Lerp(0.08f, 0.28f, elevationTemperatureImpact))
            : 0f;

        materialInstance.SetFloat(ID_SnowFactor, snowFactor);
        
        materialInstance.SetColor(ID_OceanColor, oceanColor);
        materialInstance.SetFloat(ID_IceCapSize, effectiveIceCapSize);
        materialInstance.SetFloat(ID_Seed, seed);
        materialInstance.SetFloat(ID_Moisture, moisture);
        float baseWaterwayAmount = waterwaysPreset == 0 ? 0.22f : (waterwaysPreset == 1 ? 0.70f : 1.0f);
        // Menu preview only: scale visible waterways by plausible climate capacity.
        // Very cold or very arid worlds should show fewer persistent open channels.
        float climateFlowCapacity = Mathf.Clamp01(
            Mathf.Lerp(0.35f, 1.0f, moisture) *
            Mathf.Lerp(0.45f, 1.0f, Mathf.Clamp01(1f - Mathf.Abs(temperature - 0.58f) * 1.1f))
        );
        // Hydrology generation already responds to rainfall.
        // The shader should only modestly modulate visibility, not nearly erase waterways.
        float visualClimateScale = Mathf.Lerp(0.78f, 1.0f, climateFlowCapacity);
        float waterwayAmount = Mathf.Clamp01(baseWaterwayAmount * visualClimateScale);
        materialInstance.SetFloat(ID_WaterwayAmount, waterwayAmount);
        PushInfernalTextureParameters();
        
        // Mirror detail props to shader so inspector updates apply immediately
        materialInstance.SetFloat(ID_DetailScale, detailScale);
        materialInstance.SetFloat(ID_DetailStrength, detailStrength);
        Color oceanShallow = Color.Lerp(oceanColor, new Color(0.24f, 0.52f, 0.62f, 1f), Mathf.Clamp01(moisture * 0.7f + (1f - temperature) * 0.25f));
        float infernal = Mathf.Clamp01((mapStyle - 0.35f) / 0.35f);
        float demonic = Mathf.Clamp01((mapStyle - 0.75f) / 0.25f);
        if (infernal > 0f) oceanShallow = Color.Lerp(oceanShallow, new Color(0.62f, 0.22f, 0.06f, 1f), infernal);
        materialInstance.SetColor(ID_OceanShallowColor, oceanShallow);
        materialInstance.SetFloat(ID_LandSmoothness, Mathf.Lerp(0.16f, 0.34f, moisture));
        materialInstance.SetFloat(ID_OceanSmoothness, Mathf.Lerp(0.55f, 0.88f, 1f - infernal));
        materialInstance.SetFloat(ID_OceanSpecularStrength, Mathf.Lerp(1.15f, 0.7f, infernal));
        materialInstance.SetFloat(ID_OceanFresnelStrength, 0.6f);
        materialInstance.SetColor(ID_OceanFresnelColor, Color.Lerp(new Color(0.5f,0.7f,0.9f,1f), new Color(1f,0.35f,0.2f,1f), infernal));
        materialInstance.SetFloat(ID_TerminatorSoftness, 0.2f);
        Vector3 ld = previewLight != null ? -previewLight.transform.forward : new Vector3(-0.5f,-0.7f,0.3f);
        materialInstance.SetVector(ID_KeyLightDirectionWS, ld.normalized);
        materialInstance.SetColor(ID_KeyLightColor, keyLightColor);
        materialInstance.SetFloat(ID_KeyLightIntensity, keyLightIntensity);
        materialInstance.SetColor(ID_FillLightColor, new Color(0.3f,0.36f,0.48f,1f));
        materialInstance.SetFloat(ID_FillLightIntensity, 0.33f);
        materialInstance.SetColor(ID_RimLightColor, Color.Lerp(new Color(0.32f,0.45f,0.64f,1f), new Color(0.85f,0.28f,0.22f,1f), infernal));
        materialInstance.SetFloat(ID_RimLightIntensity, Mathf.Lerp(0.35f, 0.72f, demonic));
        cloudDensity = Mathf.Clamp(0.25f + moisture * 0.3f + (waterwaysPreset==2?0.04f:waterwaysPreset==0?-0.03f:0f) + (temperature>0.8f&&moisture<0.3f?-0.12f:0f) - infernal*0.12f - demonic*0.2f,0f,0.65f);
        Color coolAtmos = new Color(0.66f, 0.82f, 0.97f, 1f);
        Color temperateAtmos = new Color(0.56f, 0.74f, 0.95f, 1f);
        Color warmAtmos = new Color(0.68f, 0.72f, 0.78f, 1f);
        atmosphereColor = Color.Lerp(coolAtmos, warmAtmos, Mathf.Clamp01((temperature - 0.25f) / 0.6f));
        atmosphereColor = Color.Lerp(atmosphereColor, temperateAtmos, Mathf.Clamp01(moisture * 0.5f));
        atmosphereColor = Color.Lerp(atmosphereColor, new Color(0.9f,0.26f,0.12f,1f), infernal);
        atmosphereColor = Color.Lerp(atmosphereColor, new Color(0.58f,0.12f,0.25f,1f), demonic);
        atmosphereIntensity = Mathf.Clamp(Mathf.Lerp(0.9f,1.5f,moisture) + infernal*0.3f + demonic*0.35f,0.6f,1.9f);
        PushCloudParameters();
        PushAtmosphereParameters();
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
        mf.sharedMesh = IcoSphereGenerator.Create(icosphereSubdivisions, 1f);

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
        if (disableCloudsForDebug)
        {
            cloudMaterialInstance.SetFloat(ID_CloudDensity, 0f);
            return;
        }
        cloudMaterialInstance.SetFloat(ID_CloudDensity, cloudDensity);
        cloudMaterialInstance.SetFloat(ID_CloudScale, cloudScale);
        cloudMaterialInstance.SetFloat(ID_CloudSpeed, cloudSpeed);
        cloudMaterialInstance.SetFloat(ID_CloudAltitude, cloudAltitude);
        cloudMaterialInstance.SetFloat(ID_Temperature, temperature);
        cloudMaterialInstance.SetFloat(ID_MapStyle, mapStyle);
        float waterwayCloud = waterwaysPreset == 2 ? 0.07f : (waterwaysPreset == 0 ? -0.07f : 0f);
        cloudMaterialInstance.SetFloat(ID_CloudDensity, Mathf.Clamp01(cloudDensity + waterwayCloud));
                cloudMaterialInstance.SetTexture(ID_CloudNoiseTex, cloudNoiseTexture);
        cloudMaterialInstance.SetColor(ID_CloudColor, temperature < 0.2f ? new Color(0.86f,0.92f,0.96f,1f) : (mapStyle > 0.75f ? new Color(0.32f,0.18f,0.18f,1f) : (mapStyle > 0.35f ? new Color(0.44f,0.36f,0.32f,1f) : new Color(0.9f,0.93f,0.96f,1f))));
        cloudMaterialInstance.SetFloat(ID_CloudSoftness, 0.45f);
        cloudMaterialInstance.SetFloat(ID_CloudShadowStrength, 0.5f);
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
        Vector3 ld = previewLight != null ? -previewLight.transform.forward : new Vector3(-0.5f,-0.7f,0.3f);
        atmosphereMaterialInstance.SetVector(ID_AtmosLightDirectionWS, ld.normalized);
        atmosphereMaterialInstance.SetFloat(ID_AtmosDayRimBoost, atmosphereDayRimBoost);
        atmosphereMaterialInstance.SetFloat(ID_AtmosNightRimStrength, atmosphereNightRimStrength);
        atmosphereMaterialInstance.SetFloat(ID_AtmosInnerScatterStrength, atmosphereInnerScatterStrength);
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
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 3f);
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

    private void ConfigurePreviewRig()
    {
        if (!autoConfigurePreviewRig) return;
        if (previewCamera != null)
        {
            previewCamera.fieldOfView = cameraFov;
            previewCamera.transform.localRotation = Quaternion.Euler(cameraLocalEuler);
            previewCamera.transform.localPosition = -previewCamera.transform.forward * cameraDistance;
            if (previewRenderer != null)
                previewCamera.transform.LookAt(previewRenderer.bounds.center);
        }
        if (previewLight != null)
        {
            previewLight.type = LightType.Directional;
            previewLight.transform.rotation = Quaternion.Euler(keyLightEuler);
            previewLight.intensity = keyLightIntensity;
            previewLight.color = keyLightColor;
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


private bool ClimateInputsChanged()
{
    if (!validateCacheInitialized) return true;

    return
        !Mathf.Approximately(temperature, lastValidatedTemperature) ||
        !Mathf.Approximately(moisture, lastValidatedMoisture) ||
        !Mathf.Approximately(climateNoiseStrength, lastValidatedClimateNoiseStrength) ||
        !Mathf.Approximately(coastWetnessStrength, lastValidatedCoastWetnessStrength) ||
        !Mathf.Approximately(continentalDrynessStrength, lastValidatedContinentalDrynessStrength) ||
        !Mathf.Approximately(continentalTemperatureStrength, lastValidatedContinentalTemperatureStrength);
}

private bool BiomeOnlyInputsChanged()
{
    if (!validateCacheInitialized) return true;

    return
        !Mathf.Approximately(riparianWetnessStrength, lastValidatedRiparianWetnessStrength) ||
        !Mathf.Approximately(biomeProvinceStrength, lastValidatedBiomeProvinceStrength) ||
        !Mathf.Approximately(biomeCompetitionSharpness, lastValidatedBiomeCompetitionSharpness);
}

}
