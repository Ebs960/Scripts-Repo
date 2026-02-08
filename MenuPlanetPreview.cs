using UnityEngine;

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

    // -----------------------------------------------------------------
    //  Private state
    // -----------------------------------------------------------------
    private Material materialInstance;

    // Cached shader property IDs
    private static readonly int ID_LandScale     = Shader.PropertyToID("_LandScale");
    private static readonly int ID_LandThreshold = Shader.PropertyToID("_LandThreshold");
    private static readonly int ID_Temperature   = Shader.PropertyToID("_Temperature");
    private static readonly int ID_Moisture      = Shader.PropertyToID("_Moisture");

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
        }
    }
}
