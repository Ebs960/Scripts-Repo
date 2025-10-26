using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class AtmosphereController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Reference to the planet generator")]
    public PlanetGenerator planetGenerator;

    [Header("Atmosphere Settings")]
    [Range(0.01f, 0.3f)]
    [Tooltip("Thickness of atmosphere relative to planet radius (multiplier)")]
    public float atmosphereThickness = 0.08f;

    [Tooltip("Material to use for the atmosphere")]
    public Material atmosphereMaterial;

    [Header("Planet-Specific Controls")]
    [Tooltip("Should this planet check for atmosphere compatibility based on planet type?")]
    public bool checkPlanetTypeForAtmosphere = true;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private bool atmosphereGenerated = false;
    private bool atmosphereEnabled = true;
    private MaterialPropertyBlock _mpb;
    private Mesh _atmosphereMesh;

    void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();

        // Ensure we have a material
        if (atmosphereMaterial != null)
            meshRenderer.sharedMaterial = atmosphereMaterial; // avoid instantiating a unique material

        // Prepare per-instance property block
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
    }

    void Start()
    {
        // Try to find planet generator if not assigned
        if (planetGenerator == null)
            planetGenerator = GetComponentInParent<PlanetGenerator>();

        if (planetGenerator == null)
            planetGenerator = GameManager.Instance?.GetCurrentPlanetGenerator();

        // Wait for game to be ready before checking atmosphere compatibility
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnGameStarted += HandleGameStarted;
        }

        // Don't generate mesh immediately, wait for planet to be ready
    }
    
    private void HandleGameStarted()
    {
        // Check if this planet should have an atmosphere (now that game is ready)
        if (checkPlanetTypeForAtmosphere)
        {
            CheckAtmosphereCompatibility();
        }
    }
    
    void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnGameStarted -= HandleGameStarted;
        }
    }

    void Update()
    {
        // Wait for planet generator to be ready
        if (planetGenerator == null || planetGenerator.radius <= 0)
            return;

        // Check if prefab atmosphere exists (different material) - skip procedural generation
        if (meshRenderer != null && meshRenderer.sharedMaterial != null && 
            atmosphereMaterial != null && meshRenderer.sharedMaterial != atmosphereMaterial)
        {
            atmosphereEnabled = false;
            Debug.Log($"[AtmosphereController] Detected prefab atmosphere on {gameObject.name} - skipping procedural generation");
            enabled = false;
            return;
        }

        // Generate atmosphere sphere once when planet is ready
        if (!atmosphereGenerated && atmosphereEnabled)
        {
            GenerateAtmosphereSphere();
            atmosphereGenerated = true;
        }

        // Update shader properties each frame for proper rendering
        if (meshRenderer != null && atmosphereEnabled && _mpb != null)
        {
            meshRenderer.GetPropertyBlock(_mpb);
            _mpb.SetVector("_PlanetCenterWS", planetGenerator.transform.position);
            _mpb.SetFloat("_PlanetRadius", planetGenerator.radius);
            meshRenderer.SetPropertyBlock(_mpb);
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
            int owningIndex = ResolveOwningPlanetIndex();
            if (owningIndex >= 0 && planetData != null && planetData.ContainsKey(owningIndex))
            {
                var currentPlanet = planetData[owningIndex];
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

    private int ResolveOwningPlanetIndex()
    {
        if (GameManager.Instance == null) return -1;
        // Try to find the index by comparing generator instances
        for (int i = 0; i < GameManager.Instance.maxPlanets; i++)
        {
            var gen = GameManager.Instance.GetPlanetGenerator(i);
            if (gen != null && gen == planetGenerator)
                return i;
        }
        return -1;
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
        atmosphereGenerated = false;
    }

    /// <summary>
    /// Generates atmosphere using Unity's built-in sphere primitive.
    /// Simplest possible approach - just scale Unity's default sphere!
    /// </summary>
    public void GenerateAtmosphereSphere()
    {
        if (!atmosphereEnabled || planetGenerator == null)
        {
            Debug.LogWarning("Cannot generate atmosphere sphere: Atmosphere disabled or planet generator not available");
            return;
        }

        float planetRadius = planetGenerator.radius;
        
        // Calculate atmosphere radius accounting for terrain elevation
        float elevationScale = planetRadius * 0.1f;
        float maxSurfaceOffset = elevationScale * Mathf.Max(0f, planetGenerator.maxTotalElevation + planetGenerator.hillElevationBoost);
        float atmosphereRadiusOffset = Mathf.Max(planetRadius * atmosphereThickness, maxSurfaceOffset + planetRadius * 0.02f);
        float atmosphereRadius = planetRadius + atmosphereRadiusOffset;

        // Use Unity's built-in sphere mesh (most efficient!)
        GameObject tempSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Mesh sphereMesh = tempSphere.GetComponent<MeshFilter>().sharedMesh;
        Destroy(tempSphere); // Clean up the temporary GameObject
        
        // Clone the mesh and scale it to our atmosphere size
        _atmosphereMesh = Instantiate(sphereMesh);
        _atmosphereMesh.name = "AtmosphereSphere";
        
        // Scale vertices to atmosphere radius
        Vector3[] vertices = _atmosphereMesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] *= atmosphereRadius * 2f; // Unity sphere has radius 0.5, so multiply by 2
        }
        _atmosphereMesh.vertices = vertices;
        _atmosphereMesh.RecalculateBounds();
        
        meshFilter.mesh = _atmosphereMesh;

        // Assign atmosphere layer
        int layerIdx = LayerMask.NameToLayer("Atmosphere");
        if (layerIdx >= 0)
        {
            gameObject.layer = layerIdx;
        }
        else
        {
            Debug.LogWarning("[AtmosphereController] Layer 'Atmosphere' not found; using default layer.");
        }

        Debug.Log($"[AtmosphereController] Generated atmosphere using Unity primitive: radius={atmosphereRadius:F2}, vertices={_atmosphereMesh.vertexCount}");
    }

    /// <summary>
    /// Call this if planet properties change and atmosphere needs to be regenerated.
    /// </summary>
    public void UpdateAtmosphere()
    {
        if (!atmosphereEnabled) return;
        
        if (planetGenerator != null && meshFilter != null)
        {
            GenerateAtmosphereSphere();
            
            // Ensure the atmosphere layer is set
            int layerIdx = LayerMask.NameToLayer("Atmosphere");
            if (layerIdx >= 0) gameObject.layer = layerIdx;
            
            // Update material properties
            if (meshRenderer != null && _mpb != null)
            {
                meshRenderer.GetPropertyBlock(_mpb);
                _mpb.SetVector("_PlanetCenterWS", planetGenerator.transform.position);
                _mpb.SetFloat("_PlanetRadius", planetGenerator.radius);
                meshRenderer.SetPropertyBlock(_mpb);
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

    void OnDisable()
    {
        // Ensure we don't leak meshes when component is disabled
        if (_atmosphereMesh != null && meshFilter != null && meshFilter.mesh == _atmosphereMesh)
        {
            meshFilter.mesh = null;
        }
    }

    void OnApplicationQuit()
    {
        // Destroy runtime meshes on app quit to silence editor leaks
        if (_atmosphereMesh != null)
        {
            Destroy(_atmosphereMesh);
            _atmosphereMesh = null;
        }
    }
}
