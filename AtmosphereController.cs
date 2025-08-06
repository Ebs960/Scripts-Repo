using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class AtmosphereController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Reference to the planet generator")]
    public PlanetGenerator planetGenerator;

    [Header("Atmosphere Settings")]
    [Range(0.01f, 0.2f)]
    [Tooltip("Thickness of atmosphere relative to planet radius")]
    public float atmosphereThickness = 0.05f;

    [Tooltip("Material to use for the atmosphere")]
    public Material atmosphereMaterial;

    [Header("Planet-Specific Controls")]
    [Tooltip("Should this planet check for atmosphere compatibility based on planet type?")]
    public bool checkPlanetTypeForAtmosphere = true;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private bool meshGenerated = false;
    private bool atmosphereEnabled = true;

    void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();

        // Ensure we have a material
        if (atmosphereMaterial != null)
            meshRenderer.material = atmosphereMaterial;
    }

    void Start()
    {
        // Try to find planet generator if not assigned
        if (planetGenerator == null)
            planetGenerator = GetComponentInParent<PlanetGenerator>();

        if (planetGenerator == null)
            planetGenerator = GameManager.Instance?.GetCurrentPlanetGenerator();

        // Check if this planet should have an atmosphere
        if (checkPlanetTypeForAtmosphere)
        {
            CheckAtmosphereCompatibility();
        }

        // Don't generate mesh immediately, wait for planet to be ready
    }

    void Update()
    {
        var grid = planetGenerator != null ? planetGenerator.Grid : null;
        if (grid == null || grid.TileCount == 0)
            return;

        // OPTION A: Skip procedural generation if prefab atmosphere already exists (CHECK ONLY ONCE)
        // Check if this AtmosphereController already has a MeshRenderer with material assigned
        // (indicating it's a prefab atmosphere, not a procedural one)
        if (meshRenderer != null && meshRenderer.material != null && meshRenderer.material != atmosphereMaterial)
        {
            // This is a prefab atmosphere - disable procedural generation
            atmosphereEnabled = false;
            Debug.Log($"[AtmosphereController] Detected prefab atmosphere on {gameObject.name} - skipping procedural generation");
            enabled = false; // STOP UPDATE() FROM RUNNING
            return;
        }

        // Only generate procedural mesh once when planet is ready and atmosphere is enabled
        if (!meshGenerated && atmosphereEnabled)
        {
            GenerateAtmosphereMesh();
            meshGenerated = true;

            // Update shader properties if we have a material
            if (atmosphereMaterial != null)
            {
                atmosphereMaterial.SetVector("_PlanetCenterWS", planetGenerator.transform.position);
                atmosphereMaterial.SetFloat("_PlanetRadius", planetGenerator.radius);
            }
        }
    }

    /// <summary>
    /// Check if this planet type should have an atmosphere based on planet type
    /// </summary>
    private void CheckAtmosphereCompatibility()
    {
        // Get the current planet type from GameManager
        if (GameManager.Instance != null && GameManager.Instance.enableMultiPlanetSystem)
        {
            var planetData = GameManager.Instance.GetPlanetData();
            if (planetData != null && planetData.ContainsKey(GameManager.Instance.currentPlanetIndex))
            {
                var currentPlanet = planetData[GameManager.Instance.currentPlanetIndex];
                atmosphereEnabled = ShouldPlanetHaveAtmosphere(currentPlanet.planetType);
                
                if (!atmosphereEnabled)
                {
                    Debug.Log($"[AtmosphereController] {currentPlanet.planetType} does not generate atmosphere - disabling atmosphere rendering");
                    DisableAtmosphere();
                }
                else
                {
                    Debug.Log($"[AtmosphereController] {currentPlanet.planetType} generates atmosphere normally");
                }
            }
        }
        else
        {
            // Default behavior for single planet mode - assume Terran type
            atmosphereEnabled = ShouldPlanetHaveAtmosphere(GameManager.PlanetType.Terran);
            Debug.Log("[AtmosphereController] Single planet mode - assuming Terran atmosphere");
        }
    }

    /// <summary>
    /// Determine if a planet type should have a visible atmosphere
    /// </summary>
    private bool ShouldPlanetHaveAtmosphere(GameManager.PlanetType planetType)
    {
        switch (planetType)
        {
            // Planets with NO atmosphere (airless worlds)
            case GameManager.PlanetType.Barren:         // Barren - likely no atmosphere
                return false;

            // Planets with THICK/VISIBLE atmospheres
            case GameManager.PlanetType.Terran:         // Earth-like - perfect atmosphere
            case GameManager.PlanetType.Gas_Giant:      // Gas giant - thick atmosphere
            case GameManager.PlanetType.Volcanic:       // Hot, likely atmospheric
            case GameManager.PlanetType.Jungle:         // Humid, thick atmosphere
            case GameManager.PlanetType.Ocean:          // Usually has atmosphere
            case GameManager.PlanetType.Ice:            // Cold but could have atmosphere
            case GameManager.PlanetType.Tundra:         // Cold world with atmosphere
            case GameManager.PlanetType.Desert:         // Usually has thin atmosphere
                return true;

            default:
                return true; // Default to having atmosphere
        }
    }

    /// <summary>
    /// Disable atmosphere rendering completely
    /// </summary>
    private void DisableAtmosphere()
    {
        atmosphereEnabled = false;
        
        // Hide the renderer
        if (meshRenderer != null)
            meshRenderer.enabled = false;
            
        // Clear the mesh
        if (meshFilter != null)
            meshFilter.mesh = null;
            
        // Optionally disable the entire GameObject
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Enable atmosphere rendering
    /// </summary>
    public void EnableAtmosphere()
    {
        atmosphereEnabled = true;
        gameObject.SetActive(true);
        
        if (meshRenderer != null)
            meshRenderer.enabled = true;
            
        // Force regeneration
        meshGenerated = false;
    }

    public void GenerateAtmosphereMesh()
    {
        if (!atmosphereEnabled || planetGenerator == null || planetGenerator.Grid == null)
        {
            Debug.LogWarning("Cannot generate atmosphere mesh: Atmosphere disabled or planet generator/grid not available");
            return;
        }

        float atmosphereRadius = planetGenerator.radius * (1f + atmosphereThickness);
        Mesh atmosphereMesh = AtmosphereShellBuilder.BuildAtmosphereShell(
            planetGenerator.Grid, 
            planetGenerator.radius, 
            atmosphereRadius - planetGenerator.radius
        );

        meshFilter.mesh = atmosphereMesh;
        
        // Assign the atmosphere layer to this GameObject
        gameObject.layer = LayerMask.NameToLayer("Atmosphere");
        
        Debug.Log($"Generated atmosphere mesh with {atmosphereMesh.vertexCount} vertices, thickness: {atmosphereThickness}, assigned to Atmosphere layer");
    }

    // Call this if planet properties change
    public void UpdateAtmosphere()
    {
        if (!atmosphereEnabled) return;
        
        if (planetGenerator != null && meshFilter != null)
        {
            GenerateAtmosphereMesh();
            
            // Ensure the atmosphere layer is set
            gameObject.layer = LayerMask.NameToLayer("Atmosphere");
            
            // Update material properties
            if (atmosphereMaterial != null)
            {
                atmosphereMaterial.SetVector("_PlanetCenterWS", planetGenerator.transform.position);
                atmosphereMaterial.SetFloat("_PlanetRadius", planetGenerator.radius);
            }
        }
    }

    /// <summary>
    /// Public method to manually override atmosphere state
    /// </summary>
    public void SetAtmosphereEnabled(bool enabled)
    {
        if (enabled && !atmosphereEnabled)
        {
            EnableAtmosphere();
        }
        else if (!enabled && atmosphereEnabled)
        {
            DisableAtmosphere();
        }
    }
}
