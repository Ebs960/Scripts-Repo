using UnityEngine;

/// <summary>
/// Renders the planet as a globe using a single sphere mesh with a shader that samples the flat map texture.
/// This is visual-only - gameplay uses the flat map.
/// </summary>
public class GlobeRenderer : MonoBehaviour
{
    [Header("Sphere Settings")]
    [Tooltip("Radius of the globe sphere")]
    [SerializeField] private float sphereRadius = 1f;
    
    [Tooltip("Number of segments for the sphere mesh (higher = smoother)")]
    [SerializeField] private int sphereSegments = 64;
    
    [Header("Texture Reference")]
    [Tooltip("Reference to the flat map texture renderer (will use its texture)")]
    [SerializeField] private FlatMapTextureRenderer flatMapRenderer;
    
    [Header("Shader Settings")]
    [Tooltip("Custom shader for globe rendering. If null, uses standard shader with texture.")]
    [SerializeField] private Shader globeShader;
    
    private GameObject sphereObject;
    private MeshRenderer sphereRenderer;
    private Material globeMaterial;
    private Mesh sphereMesh;
    private bool isBuilt;
    private WorldPicker worldPicker;
    private PlanetGenerator planetGen; // Store planet reference for material setup
    
    public bool IsBuilt => isBuilt;
    
    private void Start()
    {
        // Try to find flat map renderer if not assigned
        if (flatMapRenderer == null)
            flatMapRenderer = FindAnyObjectByType<FlatMapTextureRenderer>();
    }
    
    public void Clear()
    {
        if (sphereObject != null)
        {
            Destroy(sphereObject);
            sphereObject = null;
        }
        
        if (globeMaterial != null)
        {
            Destroy(globeMaterial);
            globeMaterial = null;
        }
        
        if (sphereMesh != null)
        {
            Destroy(sphereMesh);
            sphereMesh = null;
        }
        
        isBuilt = false;
    }
    
    /// <summary>
    /// Rebuild the globe renderer using the flat map texture.
    /// </summary>
    public void Rebuild(PlanetGenerator planetGen, FlatMapTextureRenderer flatMap)
    {
        if (planetGen == null)
        {
            Debug.LogWarning("[GlobeRenderer] Cannot rebuild: missing planet generator.");
            return;
        }
        
        Clear();
        
        // Use provided flat map or find it
        if (flatMap == null)
            flatMap = flatMapRenderer ?? FindAnyObjectByType<FlatMapTextureRenderer>();
        
        if (flatMap == null || !flatMap.IsBuilt || flatMap.MapTexture == null)
        {
            Debug.LogWarning("[GlobeRenderer] Cannot rebuild: flat map texture not available.");
            return;
        }
        
        // Store planet reference
        this.planetGen = planetGen;
        
        // Use planet radius if available
        if (planetGen != null && planetGen.radius > 0f)
            sphereRadius = planetGen.radius;
        
        // Create sphere mesh
        CreateSphereMesh();
        
        // Create sphere GameObject
        CreateSphereObject();
        
        // Create material with shader
        var bakeResult = flatMap.GetBakeResult();
        CreateGlobeMaterial(flatMap.MapTexture, bakeResult.heightmap);
        
        // Apply material
        if (sphereRenderer != null)
            sphereRenderer.material = globeMaterial;
        
        isBuilt = true;
        
        // Update WorldPicker if it exists
        UpdateWorldPicker(flatMap);
        
        Debug.Log($"[GlobeRenderer] Built globe renderer with radius {sphereRadius:F1}");
    }
    
    private void UpdateWorldPicker(FlatMapTextureRenderer flatMap)
    {
        if (worldPicker == null)
            worldPicker = FindAnyObjectByType<WorldPicker>();
        
        if (worldPicker != null && flatMap != null && flatMap.IsBuilt)
        {
            // Use the LUT from the flat map (they share the same texture)
            var bakeResult = flatMap.GetBakeResult();
            if (bakeResult.lut != null)
            {
                worldPicker.lut = bakeResult.lut;
                worldPicker.lutWidth = bakeResult.width;
                worldPicker.lutHeight = bakeResult.height;
                worldPicker.globeCollider = sphereObject?.GetComponent<SphereCollider>();
            }
        }
    }
    
    private void CreateSphereMesh()
    {
        // Create a UV sphere mesh
        sphereMesh = new Mesh();
        sphereMesh.name = "GlobeSphere";
        
        int segments = sphereSegments;
        int rings = segments / 2;
        
        // Generate vertices
        var vertices = new Vector3[(rings + 1) * (segments + 1)];
        var uvs = new Vector2[vertices.Length];
        var normals = new Vector3[vertices.Length];
        
        for (int ring = 0; ring <= rings; ring++)
        {
            float theta = ring * Mathf.PI / rings; // 0 to PI
            float sinTheta = Mathf.Sin(theta);
            float cosTheta = Mathf.Cos(theta);
            
            for (int seg = 0; seg <= segments; seg++)
            {
                float phi = seg * 2f * Mathf.PI / segments; // 0 to 2PI
                float sinPhi = Mathf.Sin(phi);
                float cosPhi = Mathf.Cos(phi);
                
                int index = ring * (segments + 1) + seg;
                
                // Position
                vertices[index] = new Vector3(
                    sinTheta * cosPhi,
                    cosTheta,
                    sinTheta * sinPhi
                ) * sphereRadius;
                
                // Normal (same as position normalized)
                normals[index] = vertices[index].normalized;
                
                // UV mapping (equirectangular)
                // U = longitude (0 to 1), V = latitude (0 to 1, but inverted for standard UV)
                float u = (float)seg / segments;
                float v = 1f - (float)ring / rings; // Invert V for standard UV
                uvs[index] = new Vector2(u, v);
            }
        }
        
        // Generate triangles
        var triangles = new int[rings * segments * 6];
        int triIndex = 0;
        
        for (int ring = 0; ring < rings; ring++)
        {
            for (int seg = 0; seg < segments; seg++)
            {
                int current = ring * (segments + 1) + seg;
                int next = current + segments + 1;
                
                // First triangle
                triangles[triIndex++] = current;
                triangles[triIndex++] = next;
                triangles[triIndex++] = current + 1;
                
                // Second triangle
                triangles[triIndex++] = current + 1;
                triangles[triIndex++] = next;
                triangles[triIndex++] = next + 1;
            }
        }
        
        sphereMesh.vertices = vertices;
        sphereMesh.uv = uvs;
        sphereMesh.normals = normals;
        sphereMesh.triangles = triangles;
        sphereMesh.RecalculateBounds();
    }
    
    private void CreateSphereObject()
    {
        sphereObject = new GameObject("GlobeSphere");
        sphereObject.transform.SetParent(transform, false);
        sphereObject.transform.localPosition = Vector3.zero;
        sphereObject.transform.localRotation = Quaternion.identity;
        sphereObject.transform.localScale = Vector3.one;
        
        // Add MeshFilter
        var meshFilter = sphereObject.AddComponent<MeshFilter>();
        meshFilter.mesh = sphereMesh;
        
        // Add MeshRenderer
        sphereRenderer = sphereObject.AddComponent<MeshRenderer>();
        
        // Add collider for picking
        var collider = sphereObject.AddComponent<SphereCollider>();
        collider.radius = sphereRadius;
    }
    
    private void CreateGlobeMaterial(Texture2D mapTexture, Texture2D heightmap)
    {
        // Use custom shader if available, otherwise use standard
        if (globeShader != null)
        {
            globeMaterial = new Material(globeShader);
        }
        else
        {
            // Use standard shader with texture
            globeMaterial = new Material(Shader.Find("Standard"));
        }
        
        // Apply textures
        globeMaterial.mainTexture = mapTexture;
        globeMaterial.SetTexture("_MainTex", mapTexture);
        
        // Apply heightmap if available (for elevation displacement)
        if (heightmap != null)
        {
            globeMaterial.SetTexture("_Heightmap", heightmap);
        }
        
        // Set planet radius for displacement scaling
        if (planetGen != null && planetGen.radius > 0f)
        {
            globeMaterial.SetFloat("_PlanetRadius", planetGen.radius);
        }
        
        // Set displacement strength (10% of radius max)
        globeMaterial.SetFloat("_DisplacementStrength", 0.1f);
        
        // Enable texture wrapping
        if (mapTexture != null)
        {
            mapTexture.wrapMode = TextureWrapMode.Repeat;
        }
        if (heightmap != null)
        {
            heightmap.wrapMode = TextureWrapMode.Repeat;
        }
        
        // Set material properties for globe
        globeMaterial.SetFloat("_Metallic", 0f);
        globeMaterial.SetFloat("_Smoothness", 0.3f);
    }
    
    /// <summary>
    /// Enable or disable the globe renderer.
    /// </summary>
    public void SetVisible(bool visible)
    {
        if (sphereRenderer != null)
            sphereRenderer.enabled = visible;
    }
    
    /// <summary>
    /// Get the sphere radius.
    /// </summary>
    public float GetRadius()
    {
        return sphereRadius;
    }
}

