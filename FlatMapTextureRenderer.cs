using UnityEngine;

/// <summary>
/// Renders a flat equirectangular map using a texture instead of individual tile meshes.
/// This is the gameplay view - horizontal wrapping enabled.
/// </summary>
public class FlatMapTextureRenderer : MonoBehaviour
{
    [Header("Texture Settings")]
    [Tooltip("Resolution width (longitude samples)")]
    [SerializeField] private int textureWidth = 2048;
    
    [Tooltip("Resolution height (latitude samples)")]
    [SerializeField] private int textureHeight = 1024;
    
    [Header("Map Dimensions")]
    [Tooltip("When enabled, mapWidth/mapHeight are derived from the planet radius (mapWidth = 2πR, mapHeight = πR).")]
    [SerializeField] private bool autoScaleToPlanetRadius = true;
    
    [Tooltip("Full horizontal span of the map in world units (corresponds to 360° of longitude).")]
    [SerializeField] private float mapWidth = 360f;
    
    [Tooltip("Full vertical span of the map in world units (corresponds to 180° of latitude).")]
    [SerializeField] private float mapHeight = 180f;
    
    [Tooltip("Constant Y height for the flat map plane.")]
    [SerializeField] private float flatY = 0f;
    
    [Header("Color Provider")]
    [Tooltip("Optional MinimapColorProvider for custom coloring. If null, uses BiomeColorHelper.")]
    [SerializeField] private MinimapColorProvider colorProvider;
    
    [Header("Pre-Build Options")]
    [Tooltip("If true, the flat map will be pre-built when planet generation completes.")]
    [SerializeField] private bool preBuildOnPlanetReady = true;
    
    [Header("Elevation Displacement")]
    [Tooltip("Enable elevation displacement on flat map (requires subdivided mesh)")]
    [SerializeField] private bool enableElevationDisplacement = true;
    [Tooltip("Number of subdivisions for the flat map mesh (higher = smoother displacement, more vertices)")]
    [SerializeField] private int meshSubdivisions = 64; // Subdivisions per side (64x64 = 4096 quads)
    [Tooltip("Displacement strength multiplier (how much elevation affects height)")]
    [SerializeField] private float displacementStrength = 0.1f; // 10% of map height
    
    private GameObject quadObject;
    private MeshRenderer quadRenderer;
    private Material mapMaterial;
    private Texture2D mapTexture;
    private PlanetTextureBaker.BakeResult bakeResult;
    private bool isBuilt;
    private bool _subscribedToPlanetReady;
    private WorldPicker worldPicker;
    private PlanetGenerator planetGen; // Store for material setup
    
    public float MapWidth => mapWidth;
    public float MapHeight => mapHeight;
    public bool IsBuilt => isBuilt;
    public Texture2D MapTexture => mapTexture;
    
    private void OnEnable()
    {
        TrySubscribeToPlanetReady();
    }
    
    private void OnDisable()
    {
        TryUnsubscribeFromPlanetReady();
    }
    
    private void Start()
    {
        TrySubscribeToPlanetReady();
    }
    
    private void TrySubscribeToPlanetReady()
    {
        if (!preBuildOnPlanetReady) return;
        if (_subscribedToPlanetReady) return;
        if (GameManager.Instance == null) return;
        
        GameManager.Instance.OnPlanetReady += HandlePlanetReady;
        _subscribedToPlanetReady = true;
    }
    
    private void TryUnsubscribeFromPlanetReady()
    {
        if (!_subscribedToPlanetReady) return;
        if (GameManager.Instance != null)
            GameManager.Instance.OnPlanetReady -= HandlePlanetReady;
        _subscribedToPlanetReady = false;
    }
    
    private void HandlePlanetReady(int planetIndex)
    {
        if (GameManager.Instance == null) return;
        
        var gen = GameManager.Instance.GetCurrentPlanetGenerator();
        if (gen == null) return;
        
        if (GameManager.Instance.currentPlanetIndex != planetIndex) return;
        
        Debug.Log($"[FlatMapTextureRenderer] Pre-building flat map texture for planet {planetIndex}");
        Rebuild(gen);
    }
    
    public void Clear()
    {
        if (quadObject != null)
        {
            Destroy(quadObject);
            quadObject = null;
        }
        
        if (mapMaterial != null)
        {
            Destroy(mapMaterial);
            mapMaterial = null;
        }
        
        // Don't destroy texture - it might be shared with globe/minimap
        mapTexture = null;
        isBuilt = false;
    }
    
    /// <summary>
    /// Rebuild the flat map texture and quad for the provided planet generator.
    /// </summary>
    public void Rebuild(PlanetGenerator planetGen)
    {
        if (planetGen == null || planetGen.Grid == null || planetGen.Grid.TileCount <= 0)
        {
            Debug.LogWarning("[FlatMapTextureRenderer] Cannot rebuild: missing planet generator grid.");
            return;
        }
        
        Clear();
        
        // Calculate map dimensions
        if (autoScaleToPlanetRadius)
        {
            float r = Mathf.Max(0.0001f, planetGen.radius);
            mapWidth = 2f * Mathf.PI * r;
            mapHeight = Mathf.PI * r;
        }
        
        // Store planet reference
        this.planetGen = planetGen;
        
        // Bake texture using PlanetTextureBaker
        bakeResult = PlanetTextureBaker.Bake(planetGen, colorProvider, textureWidth, textureHeight);
        
        if (bakeResult.texture == null)
        {
            Debug.LogError("[FlatMapTextureRenderer] Failed to bake planet texture.");
            return;
        }
        
        mapTexture = bakeResult.texture;
        
        // Create quad (or subdivided mesh if displacement enabled)
        if (enableElevationDisplacement && bakeResult.heightmap != null)
            CreateSubdividedPlane();
        else
            CreateQuad();
        
        // Apply texture to material
        if (mapMaterial != null)
        {
            mapMaterial.mainTexture = mapTexture;
            mapMaterial.SetTexture("_MainTex", mapTexture);
        }
        
        isBuilt = true;
        
        // Update WorldPicker if it exists
        UpdateWorldPicker();
        
        Debug.Log($"[FlatMapTextureRenderer] Built flat map texture ({textureWidth}x{textureHeight}). MapWidth={mapWidth:F1}, MapHeight={mapHeight:F1}");
    }
    
    private void UpdateWorldPicker()
    {
        if (worldPicker == null)
            worldPicker = FindAnyObjectByType<WorldPicker>();
        
        if (worldPicker != null && bakeResult.lut != null)
        {
            worldPicker.lut = bakeResult.lut;
            worldPicker.lutWidth = textureWidth;
            worldPicker.lutHeight = textureHeight;
            worldPicker.flatMapCollider = quadObject?.GetComponent<Collider>();
        }
    }
    
    private void CreateQuad()
    {
        // Create quad GameObject
        quadObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quadObject.name = "FlatMapQuad";
        quadObject.transform.SetParent(transform, false);
        quadObject.transform.localPosition = new Vector3(0f, flatY, 0f);
        quadObject.transform.localRotation = Quaternion.identity;
        quadObject.transform.localScale = new Vector3(mapWidth, mapHeight, 1f);
        
        // Get or create material
        quadRenderer = quadObject.GetComponent<MeshRenderer>();
        if (quadRenderer == null)
            quadRenderer = quadObject.AddComponent<MeshRenderer>();
        
        // Create material with texture wrapping
        mapMaterial = new Material(Shader.Find("Standard"));
        mapMaterial.mainTexture = mapTexture;
        mapMaterial.SetTexture("_MainTex", mapTexture);
        
        // Apply heightmap if available
        if (bakeResult.heightmap != null)
        {
            mapMaterial.SetTexture("_Heightmap", bakeResult.heightmap);
            mapMaterial.SetFloat("_DisplacementStrength", displacementStrength);
            // Use a custom shader that supports displacement, or use vertex colors
            // For now, we'll use vertex displacement in CreateSubdividedPlane
        }
        
        // Enable horizontal wrapping
        if (mapTexture != null)
        {
            mapTexture.wrapMode = TextureWrapMode.Repeat;
        }
        if (bakeResult.heightmap != null)
        {
            bakeResult.heightmap.wrapMode = TextureWrapMode.Repeat;
        }
        
        quadRenderer.material = mapMaterial;
        
        // Ensure collider exists for raycast picking
        var collider = quadObject.GetComponent<Collider>();
        if (collider == null)
        {
            // Add BoxCollider for picking (will be updated if using subdivided mesh)
            var boxCollider = quadObject.AddComponent<BoxCollider>();
            boxCollider.size = new Vector3(mapWidth, mapHeight, 0.1f);
        }
    }
    
    /// <summary>
    /// Create a subdivided plane mesh for elevation displacement.
    /// </summary>
    private void CreateSubdividedPlane()
    {
        // Create subdivided plane GameObject
        quadObject = new GameObject("FlatMapSubdividedPlane");
        quadObject.transform.SetParent(transform, false);
        quadObject.transform.localPosition = new Vector3(0f, flatY, 0f);
        quadObject.transform.localRotation = Quaternion.identity;
        quadObject.transform.localScale = Vector3.one; // Scale is handled in mesh
        
        // Generate subdivided mesh
        Mesh mesh = new Mesh();
        mesh.name = "SubdividedPlane";
        
        int segments = meshSubdivisions;
        int vertexCount = (segments + 1) * (segments + 1);
        Vector3[] vertices = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        Vector3[] normals = new Vector3[vertexCount];
        
        // Generate vertices with elevation displacement
        for (int y = 0; y <= segments; y++)
        {
            for (int x = 0; x <= segments; x++)
            {
                int index = y * (segments + 1) + x;
                
                // UV coordinates (0-1)
                float u = (float)x / segments;
                float v = (float)y / segments;
                uvs[index] = new Vector2(u, v);
                
                // World position (centered, scaled by map dimensions)
                float worldX = (u - 0.5f) * mapWidth;
                float worldZ = (v - 0.5f) * mapHeight;
                
                // Sample heightmap for elevation displacement
                float elevation = 0f;
                if (bakeResult.heightmap != null)
                {
                    // Sample heightmap at UV coordinate
                    int texX = Mathf.Clamp(Mathf.FloorToInt(u * textureWidth), 0, textureWidth - 1);
                    int texY = Mathf.Clamp(Mathf.FloorToInt(v * textureHeight), 0, textureHeight - 1);
                    Color heightColor = bakeResult.heightmap.GetPixel(texX, texY);
                    elevation = heightColor.r; // Red channel stores elevation (0-1)
                }
                
                // Apply displacement (push vertices up based on elevation)
                float displacement = elevation * displacementStrength * mapHeight * 0.1f; // 10% of map height max
                float worldY = flatY + displacement;
                
                vertices[index] = new Vector3(worldX, worldY, worldZ);
                normals[index] = Vector3.up; // Will be recalculated
            }
        }
        
        // Generate triangles
        int[] triangles = new int[segments * segments * 6];
        int triIndex = 0;
        
        for (int y = 0; y < segments; y++)
        {
            for (int x = 0; x < segments; x++)
            {
                int current = y * (segments + 1) + x;
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
        
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        
        // Add MeshFilter and MeshRenderer
        MeshFilter meshFilter = quadObject.AddComponent<MeshFilter>();
        meshFilter.mesh = mesh;
        
        quadRenderer = quadObject.AddComponent<MeshRenderer>();
        
        // Create material with texture wrapping
        mapMaterial = new Material(Shader.Find("Standard"));
        mapMaterial.mainTexture = mapTexture;
        mapMaterial.SetTexture("_MainTex", mapTexture);
        
        // Enable horizontal wrapping
        if (mapTexture != null)
        {
            mapTexture.wrapMode = TextureWrapMode.Repeat;
        }
        if (bakeResult.heightmap != null)
        {
            bakeResult.heightmap.wrapMode = TextureWrapMode.Repeat;
        }
        
        quadRenderer.material = mapMaterial;
        
        // Add MeshCollider for picking (more accurate than BoxCollider for displaced mesh)
        var meshCollider = quadObject.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = mesh;
    }
    
    /// <summary>
    /// Get the world position for a given UV coordinate (0-1).
    /// </summary>
    public Vector3 GetWorldPositionFromUV(float u, float v)
    {
        float x = (u - 0.5f) * mapWidth;
        float z = (v - 0.5f) * mapHeight;
        return transform.TransformPoint(new Vector3(x, flatY, z));
    }
    
    /// <summary>
    /// Get UV coordinate from world position.
    /// </summary>
    public Vector2 GetUVFromWorldPosition(Vector3 worldPos)
    {
        Vector3 localPos = transform.InverseTransformPoint(worldPos);
        float u = (localPos.x / mapWidth) + 0.5f;
        float v = (localPos.z / mapHeight) + 0.5f;
        return new Vector2(u, v);
    }
    
    /// <summary>
    /// Get tile index at a given UV coordinate using the LUT.
    /// </summary>
    public int GetTileIndexAtUV(float u, float v)
    {
        if (bakeResult.lut == null || bakeResult.lut.Length == 0)
            return -1;
        
        // Clamp and wrap U (horizontal wrapping)
        u = Mathf.Repeat(u, 1f);
        v = Mathf.Clamp01(v);
        
        int x = Mathf.FloorToInt(u * textureWidth);
        int y = Mathf.FloorToInt(v * textureHeight);
        
        x = Mathf.Clamp(x, 0, textureWidth - 1);
        y = Mathf.Clamp(y, 0, textureHeight - 1);
        
        int pixelIndex = y * textureWidth + x;
        if (pixelIndex >= 0 && pixelIndex < bakeResult.lut.Length)
            return bakeResult.lut[pixelIndex];
        
        return -1;
    }
    
    /// <summary>
    /// Enable or disable the flat map renderer.
    /// </summary>
    public void SetVisible(bool visible)
    {
        if (quadRenderer != null)
            quadRenderer.enabled = visible;
    }
    
    /// <summary>
    /// Get the bake result (for WorldPicker integration).
    /// </summary>
    public PlanetTextureBaker.BakeResult GetBakeResult()
    {
        return bakeResult;
    }
    
    /// <summary>
    /// Get a downscaled version of the map texture for minimap use.
    /// </summary>
    public Texture2D GetDownscaledTexture(int targetWidth, int targetHeight)
    {
        if (mapTexture == null)
            return null;
        
        // Create downscaled texture
        RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight);
        Graphics.Blit(mapTexture, rt);
        
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        
        Texture2D downscaled = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
        downscaled.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
        downscaled.Apply();
        
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        
        downscaled.wrapMode = TextureWrapMode.Repeat;
        downscaled.filterMode = FilterMode.Bilinear;
        downscaled.name = $"{mapTexture.name}_Downscaled_{targetWidth}x{targetHeight}";
        
        return downscaled;
    }
}

