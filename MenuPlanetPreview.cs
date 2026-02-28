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
    [Tooltip("Cloud layer rotation speed (deg/s). Negative = opposite direction to planet.")]
    [SerializeField] private float cloudRotationSpeed = -3f;
    [Tooltip("Cloud altitude above planet surface.")]
    [Range(0f, 0.1f)] [SerializeField] private float cloudAltitude = 0.018f;
    [Tooltip("Cloud coverage density.")]
    [Range(0f, 1f)] [SerializeField] private float cloudDensity = 0.55f;
    [Tooltip("Cloud noise scale.")]
    [SerializeField] private float cloudScale = 3.0f;
    [Tooltip("Cloud animation speed.")]
    [SerializeField] private float cloudSpeed = 0.08f;

    [Header("Atmosphere Shell")]
    [Tooltip("Scale multiplier for the atmosphere shell mesh.")]
    [Range(1.01f, 1.15f)] [SerializeField] private float atmosphereShellScale = 1.06f;
    [Tooltip("Fresnel falloff exponent for atmospheric rim glow.")]
    [Range(1f, 8f)] [SerializeField] private float atmosphereFalloff = 3.5f;
    [Tooltip("Brightness multiplier for the atmosphere glow.")]
    [Range(0f, 3f)] [SerializeField] private float atmosphereIntensity = 1.2f;

    [Header("HDRP Post-Processing")]
    [Tooltip("Enable bloom on the preview camera for emissive glow (lava, specular).")]
    [SerializeField] private bool enableBloom = true;
    [Tooltip("Bloom threshold — only pixels brighter than this bloom.")]
    [SerializeField] private float bloomThreshold = 0.5f;
    [Tooltip("Bloom intensity.")]
    [Range(0f, 1f)] [SerializeField] private float bloomIntensity = 0.35f;

    [Header("Mesh Quality")]
    [Tooltip("Subdivisions for generated icosphere. Higher = smoother displacement. 0-6 (6 ≈ 40k tris).")]
    [Range(0,6)] [SerializeField] private int icosphereSubdivisions = 5;

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

    // -----------------------------------------------------------------
    //  Private state
    // -----------------------------------------------------------------
    private Material materialInstance;
    private Material cloudMaterialInstance;
    private Material atmosphereMaterialInstance;
    private GameObject cloudShellGO;
    private GameObject atmosphereShellGO;
    private Volume bloomVolume;

    // Cached shader property IDs — planet
    private static readonly int ID_LandScale     = Shader.PropertyToID("_LandScale");
    private static readonly int ID_LandThreshold = Shader.PropertyToID("_LandThreshold");
    private static readonly int ID_Temperature   = Shader.PropertyToID("_Temperature");
    private static readonly int ID_Moisture      = Shader.PropertyToID("_Moisture");
    private static readonly int ID_Elevation     = Shader.PropertyToID("_Elevation");
    private static readonly int ID_MapStyle     = Shader.PropertyToID("_MapStyle");
    private static readonly int ID_BiomeTint     = Shader.PropertyToID("_BiomeTint");
    private static readonly int ID_DesertFactor  = Shader.PropertyToID("_DesertFactor");
    private static readonly int ID_TropicalFactor = Shader.PropertyToID("_TropicalFactor");
    private static readonly int ID_SnowFactor    = Shader.PropertyToID("_SnowFactor");
    private static readonly int ID_DetailScale   = Shader.PropertyToID("_DetailScale");
    private static readonly int ID_DetailStrength= Shader.PropertyToID("_DetailStrength");
    private static readonly int ID_AtmosColor    = Shader.PropertyToID("_AtmosphereColor");
    private static readonly int ID_DisplacementScale = Shader.PropertyToID("_DisplacementScale");

    // Shared across planet / cloud / atmosphere shaders
    private static readonly int ID_SunDirection  = Shader.PropertyToID("_SunDirection");
    private static readonly int ID_SunColor      = Shader.PropertyToID("_SunColor");
    private static readonly int ID_SunIntensity  = Shader.PropertyToID("_SunIntensity");

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

        // Auto-find light in children
        if (previewLight == null)
        {
            previewLight = GetComponentInChildren<Light>();
        }

        SetupMaterial();
        ApplyAllParameters();
        SetupSpaceBackgroundIfNeeded();

        // Upgrade the preview mesh for better shading/detail
        TryReplacePreviewMesh();

        // Build cloud + atmosphere shells
        SetupCloudShell();
        SetupAtmosphereShell();

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

        // Cloud rotation (independent, typically opposite direction)
        if (cloudShellGO != null)
        {
            cloudShellGO.transform.Rotate(Vector3.up, cloudRotationSpeed * Time.deltaTime, Space.Self);
        }

        // Sync sun direction from preview light to all materials
        PushSunProperties();
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

        // Update biome-related visual parameters derived from temperature/moisture/elevation
        UpdateBiomeVisuals();

        // Push detail params (old atmosphere props no longer pushed — handled by shell)
        materialInstance.SetFloat(ID_DetailScale, detailScale);
        materialInstance.SetFloat(ID_DetailStrength, detailStrength);

        // Displacement
        materialInstance.SetFloat(ID_DisplacementScale, displacementScale);
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

        materialInstance.SetFloat(ID_DesertFactor, desertFactor);
        materialInstance.SetFloat(ID_TropicalFactor, tropicalFactor);
        materialInstance.SetFloat(ID_SnowFactor, snowFactor);
        
        // Mirror detail props to shader so inspector updates apply immediately
        materialInstance.SetFloat(ID_DetailScale, detailScale);
        materialInstance.SetFloat(ID_DetailStrength, detailStrength);
    }

    // -----------------------------------------------------------------
    //  Cloud Shell setup + parameter push
    // -----------------------------------------------------------------
    private void SetupCloudShell()
    {
        if (previewRenderer == null) return;

        if (cloudShader == null)
            cloudShader = Shader.Find("Custom/MenuPlanetClouds");
        if (cloudShader == null)
        {
            Debug.LogWarning("[MenuPlanetPreview] Cloud shader 'Custom/MenuPlanetClouds' not found.");
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
    }

    // -----------------------------------------------------------------
    //  Atmosphere Shell setup + parameter push
    // -----------------------------------------------------------------
    private void SetupAtmosphereShell()
    {
        if (previewRenderer == null) return;

        if (atmosphereShader == null)
            atmosphereShader = Shader.Find("Custom/MenuPlanetAtmosphere");
        if (atmosphereShader == null)
        {
            Debug.LogWarning("[MenuPlanetPreview] Atmosphere shader 'Custom/MenuPlanetAtmosphere' not found.");
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
    //  Sun light → shader property sync
    // -----------------------------------------------------------------
    private void PushSunProperties()
    {
        if (previewLight == null) return;

        Vector4 sunDir = previewLight.transform.forward; // light's forward = direction TOWARD surface
        Color lightCol = previewLight.useColorTemperature
            ? previewLight.color * Mathf.CorrelatedColorTemperatureToRGB(previewLight.colorTemperature)
            : previewLight.color;
        float intensity = previewLight.intensity / 1000f; // normalize lux to a shader-friendly range

        if (materialInstance != null)
        {
            materialInstance.SetVector(ID_SunDirection, sunDir);
            materialInstance.SetColor(ID_SunColor, lightCol);
            materialInstance.SetFloat(ID_SunIntensity, intensity);
        }
        if (cloudMaterialInstance != null)
        {
            cloudMaterialInstance.SetVector(ID_SunDirection, sunDir);
            cloudMaterialInstance.SetColor(ID_SunColor, lightCol);
            cloudMaterialInstance.SetFloat(ID_SunIntensity, intensity);
        }
        if (atmosphereMaterialInstance != null)
        {
            atmosphereMaterialInstance.SetVector(ID_SunDirection, sunDir);
            atmosphereMaterialInstance.SetColor(ID_SunColor, lightCol);
            atmosphereMaterialInstance.SetFloat(ID_SunIntensity, intensity);
        }
    }

    // -----------------------------------------------------------------
    //  HDRP Bloom Volume (programmatic, no .asset files needed)
    // -----------------------------------------------------------------
    private void SetupBloomVolume()
    {
        if (!enableBloom) return;

        // Find the preview camera (should be a sibling/child with a Camera component)
        Camera previewCam = GetComponentInChildren<Camera>(true);
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

        // Detach the quad from the rotating preview so it doesn't inherit the sphere's rotation
        backgroundQuad.transform.SetParent(this.transform, true);

        // Compute a world-space position slightly behind the preview sphere from the camera's view
        Vector3 center = previewRenderer.bounds.center;
        float maxExtent = Mathf.Max(previewRenderer.bounds.size.x, previewRenderer.bounds.size.y, previewRenderer.bounds.size.z);
        float size = maxExtent * 6f;
        Vector3 viewDir = Camera.main != null ? (center - Camera.main.transform.position).normalized : previewRenderer.transform.forward;
        float distance = maxExtent * 0.5f + 1.5f;
        backgroundQuad.transform.position = center + viewDir * (distance + maxExtent * 0.5f);
        backgroundQuad.transform.rotation = Quaternion.identity;
        backgroundQuad.transform.localScale = new Vector3(size, size, 1f);

        var quadRenderer = backgroundQuad.GetComponent<MeshRenderer>();
        quadRenderer.sharedMaterial = spaceBackgroundMaterial;

        // Disable collider
        var col = backgroundQuad.GetComponent<Collider>();
        if (col != null) Destroy(col);
    }

    private void LateUpdate()
    {
        // Keep the background quad facing the main camera if it exists
        if (backgroundQuad != null && Camera.main != null)
        {
            backgroundQuad.transform.rotation = Quaternion.LookRotation(Camera.main.transform.position - backgroundQuad.transform.position);
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
