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

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private bool meshGenerated = false;

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

        // Don't generate mesh immediately, wait for planet to be ready
    }

    void Update()
    {
        // Only generate mesh once when planet is ready
        if (!meshGenerated && planetGenerator != null && planetGenerator.Grid != null && planetGenerator.Grid.TileCount > 0)
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

    public void GenerateAtmosphereMesh()
    {
        if (planetGenerator == null || planetGenerator.Grid == null)
        {
            Debug.LogWarning("Cannot generate atmosphere mesh: Planet generator or grid not available");
            return;
        }

        float atmosphereRadius = planetGenerator.radius * (1f + atmosphereThickness);
        Mesh atmosphereMesh = AtmosphereShellBuilder.BuildAtmosphereShell(
            planetGenerator.Grid, 
            planetGenerator.radius, 
            atmosphereRadius - planetGenerator.radius
        );

        meshFilter.mesh = atmosphereMesh;
        Debug.Log($"Generated atmosphere mesh with {atmosphereMesh.vertexCount} vertices, thickness: {atmosphereThickness}");
    }

    // Call this if planet properties change
    public void UpdateAtmosphere()
    {
        if (planetGenerator != null && meshFilter != null)
        {
            GenerateAtmosphereMesh();
            
            // Update material properties
            if (atmosphereMaterial != null)
            {
                atmosphereMaterial.SetVector("_PlanetCenterWS", planetGenerator.transform.position);
                atmosphereMaterial.SetFloat("_PlanetRadius", planetGenerator.radius);
            }
        }
    }
}
