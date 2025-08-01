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
            planetGenerator = Object.FindFirstObjectByType<PlanetGenerator>();

        // Check if this planet should have an atmosphere
        if (checkPlanetTypeForAtmosphere)
        {
            CheckAtmosphereCompatibility();
        }

        // Don't generate mesh immediately, wait for planet to be ready
    }

    void Update()
    {
        // Only generate mesh once when planet is ready and atmosphere is enabled
        if (!meshGenerated && atmosphereEnabled && planetGenerator != null && 
            planetGenerator.Grid != null && planetGenerator.Grid.TileCount > 0)
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
    /// Check if this planet type should have an atmosphere based on real solar system data
    /// </summary>
    private void CheckAtmosphereCompatibility()
    {
        // Get the current planet type from SolarSystemManager
        var solarSystem = SolarSystemManager.Instance;
        if (solarSystem != null)
        {
            var currentPlanet = solarSystem.GetCurrentPlanet();
            if (currentPlanet != null)
            {
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
    }

    /// <summary>
    /// Determine if a planet type should have a visible atmosphere
    /// </summary>
    private bool ShouldPlanetHaveAtmosphere(PlanetType planetType)
    {
        switch (planetType)
        {
            // Planets with NO atmosphere (airless worlds)
            case PlanetType.Mercury:        // No atmosphere
            case PlanetType.Luna:           // Earth's moon - no atmosphere
            case PlanetType.Io:             // Volcanic moon - very thin atmosphere
            case PlanetType.Ganymede:       // Ice moon - no significant atmosphere
            case PlanetType.Callisto:       // Ice moon - no atmosphere
            case PlanetType.Enceladus:      // Small ice moon - minimal atmosphere
                return false;

            // Planets with THIN atmosphere (minimal visual effect)
            case PlanetType.Mars:           // Very thin CO2 atmosphere
            case PlanetType.Pluto:          // Extremely thin nitrogen atmosphere
                return false; // You could return true here for very subtle atmosphere

            // Planets with THICK/VISIBLE atmospheres
            case PlanetType.Terran:         // Earth - perfect atmosphere
            case PlanetType.Venus:          // Thick, dense atmosphere
            case PlanetType.Jupiter:        // Massive gas giant atmosphere
            case PlanetType.Saturn:         // Dense gas atmosphere
            case PlanetType.Uranus:         // Ice giant atmosphere
            case PlanetType.Neptune:        // Dense ice giant atmosphere
            case PlanetType.Titan:          // Thick nitrogen atmosphere
            case PlanetType.Europa:         // Thin oxygen atmosphere (could be subtle)
                return true;

            // Procedural planet types
            case PlanetType.Desert:         // Depends on design - usually thin
            case PlanetType.Ocean:          // Usually has atmosphere
            case PlanetType.Ice:            // Cold but could have atmosphere
            case PlanetType.Volcanic:       // Hot, likely atmospheric
            case PlanetType.Jungle:         // Humid, thick atmosphere
            case PlanetType.Gas:            // Gas giant - thick atmosphere
            case PlanetType.Demonic:        // Hellish world - thick sulfurous atmosphere
                return true;

            case PlanetType.Rocky:          // Barren - likely no atmosphere
                return false;

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
