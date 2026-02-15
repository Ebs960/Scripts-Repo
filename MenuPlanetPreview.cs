using UnityEngine;
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
/// Hierarchy expected:
///   MenuPlanetPreview (this script)
///     └── PreviewSphere (MeshFilter + MeshRenderer with sphere mesh)
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

    [Tooltip("Atmosphere tint color for rim scattering.")]
    [SerializeField] private Color atmosphereColor = new Color(0.62f, 0.78f, 0.95f, 1f);
    [Tooltip("Power/width of atmosphere rim (higher = tighter rim).")]
    [Range(0.5f,20f)] [SerializeField] private float atmospherePower = 3.5f;
    [Tooltip("Radius/scale multiplier for the atmosphere rim/shell. 1 = default sphere size, >1 = larger atmosphere.")]
    [Range(0.9f, 20f)] [SerializeField] private float atmosphereRadius = 1.05f;

    [Header("Mesh Quality")]
    [Tooltip("Subdivisions for generated icosphere used for preview. 0..4 (higher increases vertex count).")]
    [Range(0,10)] [SerializeField] private int icosphereSubdivisions = 2;

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

    // Cached shader property IDs
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
    private static readonly int ID_AtmosPower    = Shader.PropertyToID("_AtmospherePower");
    private static readonly int ID_AtmosRadius   = Shader.PropertyToID("_AtmosphereRadius");

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

        SetupMaterial();
        ApplyAllParameters();
        SetupSpaceBackgroundIfNeeded();

        // Upgrade the preview mesh for better shading/detail
        TryReplacePreviewMesh();
    }

    private void Update()
    {
        // Slow cosmetic rotation
        if (previewRenderer != null)
        {
            previewRenderer.transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.Self);
        }
    }

    private void OnDestroy()
    {
        if (materialInstance != null)
        {
            Destroy(materialInstance);
            materialInstance = null;
        }
    }

    /// <summary>
    /// When values change in the Inspector during Play mode, push them to the material.
    /// </summary>
    private void OnValidate()
    {
        if (Application.isPlaying && materialInstance != null)
        {
            ApplyAllParameters();
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

        // Push detail and atmosphere params
        materialInstance.SetFloat(ID_DetailScale, detailScale);
        materialInstance.SetFloat(ID_DetailStrength, detailStrength);
        materialInstance.SetColor(ID_AtmosColor, atmosphereColor);
        materialInstance.SetFloat(ID_AtmosPower, atmospherePower);
        materialInstance.SetFloat(ID_AtmosRadius, atmosphereRadius);
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
    /// Set the radius/scale of the atmosphere rim used by the preview shader.
    /// This mirrors the value to the material so UI can control atmospheric thickness.
    /// </summary>
    public void SetAtmosphereRadius(float radius)
    {
        atmosphereRadius = Mathf.Clamp(radius, 0.1f, 10f);
        if (materialInstance != null)
            materialInstance.SetFloat(ID_AtmosRadius, atmosphereRadius);
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
        
        // Mirror detail/atmosphere props to shader so inspector updates apply immediately
        materialInstance.SetFloat(ID_DetailScale, detailScale);
        materialInstance.SetFloat(ID_DetailStrength, detailStrength);
        materialInstance.SetColor(ID_AtmosColor, atmosphereColor);
        materialInstance.SetFloat(ID_AtmosPower, atmospherePower);
        materialInstance.SetFloat(ID_AtmosRadius, atmosphereRadius);
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
            subdivisions = Mathf.Clamp(subdivisions, 0, 4);
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
    }
}
