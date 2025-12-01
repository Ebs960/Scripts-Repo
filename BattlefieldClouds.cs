using UnityEngine;

/// <summary>
/// Creates atmospheric clouds for the battlefield.
/// Supports scrolling cloud layers and optional ground shadows.
/// Attach to BattleMapGenerator or a dedicated Clouds parent object.
/// </summary>
public class BattlefieldClouds : MonoBehaviour
{
    [Header("Cloud Appearance")]
    [Tooltip("Height of the cloud layer above terrain")]
    [Range(50f, 500f)]
    public float cloudHeight = 150f;
    
    [Tooltip("Size of the cloud plane (should be larger than map)")]
    public float cloudPlaneSize = 300f;
    
    [Tooltip("Base cloud color/tint")]
    public Color cloudColor = new Color(1f, 1f, 1f, 0.6f);
    
    [Tooltip("Cloud density (0 = no clouds, 1 = overcast)")]
    [Range(0f, 1f)]
    public float cloudDensity = 0.5f;
    
    [Tooltip("Cloud softness/blur amount")]
    [Range(0f, 1f)]
    public float cloudSoftness = 0.5f;
    
    [Header("Cloud Movement")]
    [Tooltip("Cloud scroll speed")]
    [Range(0f, 5f)]
    public float scrollSpeed = 0.5f;
    
    [Tooltip("Wind direction for cloud movement")]
    public Vector2 windDirection = new Vector2(1f, 0.3f);
    
    [Header("Multiple Layers")]
    [Tooltip("Enable second cloud layer for depth")]
    public bool useSecondLayer = true;
    
    [Tooltip("Height offset for second layer")]
    public float secondLayerHeightOffset = 30f;
    
    [Tooltip("Speed multiplier for second layer (creates parallax)")]
    [Range(0.5f, 2f)]
    public float secondLayerSpeedMultiplier = 0.7f;
    
    [Tooltip("Alpha multiplier for second layer")]
    [Range(0f, 1f)]
    public float secondLayerAlpha = 0.4f;
    
    [Header("Cloud Shadows")]
    [Tooltip("Enable cloud shadows on ground")]
    public bool enableCloudShadows = true;
    
    [Tooltip("Cloud shadow intensity")]
    [Range(0f, 1f)]
    public float shadowIntensity = 0.3f;
    
    [Tooltip("Height of shadow projector above ground")]
    public float shadowProjectorHeight = 100f;
    
    [Header("Biome Adaptation")]
    [Tooltip("Automatically adjust clouds based on biome")]
    public bool adaptToBiome = true;
    
    // Internal references
    private GameObject cloudLayer1;
    private GameObject cloudLayer2;
    private GameObject shadowProjector;
    private Material cloudMaterial1;
    private Material cloudMaterial2;
    private Material shadowMaterial;
    private Vector2 scrollOffset1 = Vector2.zero;
    private Vector2 scrollOffset2 = Vector2.zero;
    
    // Shader property IDs
    private static readonly int MainTexID = Shader.PropertyToID("_MainTex");
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorID = Shader.PropertyToID("_Color");
    private static readonly int AlphaID = Shader.PropertyToID("_Alpha");
    
    /// <summary>
    /// Create clouds for the battlefield
    /// </summary>
    public void CreateClouds(float mapSize, Biome biome = Biome.Plains)
    {
        // Clear any existing clouds
        ClearClouds();
        
        // Adapt settings to biome if enabled
        if (adaptToBiome)
        {
            AdaptToBiome(biome);
        }
        
        // Skip if density is too low
        if (cloudDensity < 0.05f)
        {
            Debug.Log($"[BattlefieldClouds] Skipping clouds for {biome} (density too low: {cloudDensity:F2})");
            return;
        }
        
        // Ensure cloud plane is large enough
        cloudPlaneSize = Mathf.Max(cloudPlaneSize, mapSize * 2f);
        
        // Create cloud texture
        Texture2D cloudTexture = CreateCloudTexture();
        
        // Create primary cloud layer
        cloudLayer1 = CreateCloudPlane("CloudLayer1", cloudHeight, cloudTexture, cloudColor);
        
        // Create secondary layer for depth
        if (useSecondLayer)
        {
            Color layer2Color = cloudColor;
            layer2Color.a *= secondLayerAlpha;
            cloudLayer2 = CreateCloudPlane("CloudLayer2", cloudHeight + secondLayerHeightOffset, cloudTexture, layer2Color);
        }
        
        // Create shadow projector
        if (enableCloudShadows)
        {
            CreateCloudShadowProjector(cloudTexture);
        }
        
        Debug.Log($"[BattlefieldClouds] Created clouds for {biome} - Density: {cloudDensity:F2}, Height: {cloudHeight}, Shadows: {enableCloudShadows}");
    }
    
    /// <summary>
    /// Create a single cloud plane
    /// </summary>
    private GameObject CreateCloudPlane(string name, float height, Texture2D texture, Color color)
    {
        GameObject cloudGO = new GameObject(name);
        cloudGO.transform.SetParent(transform);
        cloudGO.transform.position = new Vector3(0, height, 0);
        cloudGO.transform.rotation = Quaternion.Euler(90, 0, 0); // Face downward
        
        // Create mesh
        MeshFilter meshFilter = cloudGO.AddComponent<MeshFilter>();
        meshFilter.mesh = CreateQuadMesh(cloudPlaneSize);
        
        // Create renderer
        MeshRenderer renderer = cloudGO.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        
        // Create material
        Material mat = CreateCloudMaterial(texture, color);
        renderer.material = mat;
        
        // Store material reference for animation
        if (name == "CloudLayer1")
            cloudMaterial1 = mat;
        else
            cloudMaterial2 = mat;
        
        return cloudGO;
    }
    
    /// <summary>
    /// Create cloud material with transparency
    /// </summary>
    private Material CreateCloudMaterial(Texture2D texture, Color color)
    {
        // Try to find URP unlit transparent shader
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Transparent");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        
        Material mat = new Material(shader);
        mat.mainTexture = texture;
        
        // Set color (try both URP and standard property names)
        mat.SetColor(BaseColorID, color);
        mat.SetColor(ColorID, color);
        
        // Enable transparency
        mat.SetFloat("_Surface", 1); // Transparent
        mat.SetFloat("_Blend", 0); // Alpha blend
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000; // Transparent queue
        
        // Set texture tiling
        mat.mainTextureScale = new Vector2(2f, 2f);
        
        return mat;
    }
    
    /// <summary>
    /// Create a procedural cloud texture using Perlin noise
    /// </summary>
    private Texture2D CreateCloudTexture()
    {
        int size = 256;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Bilinear;
        
        // Generate cloud pattern using layered Perlin noise
        float[] pixels = new float[size * size];
        
        // Multiple octaves for realistic clouds
        float[] frequencies = { 2f, 4f, 8f, 16f };
        float[] amplitudes = { 1f, 0.5f, 0.25f, 0.125f };
        float totalAmplitude = 0f;
        foreach (float a in amplitudes) totalAmplitude += a;
        
        // Random offset for variety
        float offsetX = Random.Range(0f, 1000f);
        float offsetY = Random.Range(0f, 1000f);
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float value = 0f;
                
                for (int i = 0; i < frequencies.Length; i++)
                {
                    float nx = (x / (float)size) * frequencies[i] + offsetX;
                    float ny = (y / (float)size) * frequencies[i] + offsetY;
                    value += Mathf.PerlinNoise(nx, ny) * amplitudes[i];
                }
                
                value /= totalAmplitude;
                
                // Apply density threshold (creates gaps in clouds)
                float threshold = 1f - cloudDensity;
                value = Mathf.Clamp01((value - threshold) / (1f - threshold));
                
                // Apply softness (smoothstep for softer edges)
                if (cloudSoftness > 0)
                {
                    value = Mathf.SmoothStep(0, 1, value);
                }
                
                pixels[y * size + x] = value;
            }
        }
        
        // Apply to texture
        Color[] colors = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++)
        {
            float alpha = pixels[i];
            colors[i] = new Color(1f, 1f, 1f, alpha);
        }
        
        texture.SetPixels(colors);
        texture.Apply(true);
        
        return texture;
    }
    
    /// <summary>
    /// Create a quad mesh for the cloud plane
    /// </summary>
    private Mesh CreateQuadMesh(float size)
    {
        Mesh mesh = new Mesh();
        mesh.name = "CloudQuad";
        
        float halfSize = size / 2f;
        
        mesh.vertices = new Vector3[]
        {
            new Vector3(-halfSize, -halfSize, 0),
            new Vector3(halfSize, -halfSize, 0),
            new Vector3(-halfSize, halfSize, 0),
            new Vector3(halfSize, halfSize, 0)
        };
        
        mesh.uv = new Vector2[]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(0, 1),
            new Vector2(1, 1)
        };
        
        mesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        
        return mesh;
    }
    
    /// <summary>
    /// Create a projector for cloud shadows on the ground
    /// </summary>
    private void CreateCloudShadowProjector(Texture2D cloudTexture)
    {
        // Unity's Projector component is deprecated in URP
        // Instead, we'll create a shadow plane just above the terrain
        shadowProjector = new GameObject("CloudShadows");
        shadowProjector.transform.SetParent(transform);
        shadowProjector.transform.position = new Vector3(0, 0.5f, 0); // Just above ground
        shadowProjector.transform.rotation = Quaternion.Euler(90, 0, 0);
        
        MeshFilter meshFilter = shadowProjector.AddComponent<MeshFilter>();
        meshFilter.mesh = CreateQuadMesh(cloudPlaneSize);
        
        MeshRenderer renderer = shadowProjector.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        
        // Create shadow material (multiply blend for darkening)
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Transparent");
        
        shadowMaterial = new Material(shader);
        shadowMaterial.mainTexture = cloudTexture;
        
        // Dark shadow color
        Color shadowColor = new Color(0, 0, 0, shadowIntensity);
        shadowMaterial.SetColor(BaseColorID, shadowColor);
        shadowMaterial.SetColor(ColorID, shadowColor);
        
        // Transparency settings
        shadowMaterial.SetFloat("_Surface", 1);
        shadowMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        shadowMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        shadowMaterial.SetInt("_ZWrite", 0);
        shadowMaterial.renderQueue = 2450; // Just before transparent
        
        shadowMaterial.mainTextureScale = new Vector2(2f, 2f);
        
        renderer.material = shadowMaterial;
    }
    
    /// <summary>
    /// Adapt cloud settings based on biome
    /// </summary>
    private void AdaptToBiome(Biome biome)
    {
        switch (biome)
        {
            // Lush, humid biomes - more clouds
            case Biome.Jungle:
            case Biome.Rainforest:
            case Biome.Swamp:
            case Biome.Marsh:
                cloudDensity = 0.7f;
                cloudColor = new Color(0.9f, 0.9f, 0.95f, 0.7f);
                scrollSpeed = 0.3f;
                break;
                
            // Temperate biomes - moderate clouds
            case Biome.Forest:
            case Biome.Grassland:
            case Biome.Plains:
            case Biome.Taiga:
            case Biome.PineForest:
                cloudDensity = 0.5f;
                cloudColor = new Color(1f, 1f, 1f, 0.6f);
                scrollSpeed = 0.5f;
                break;
                
            // Cold biomes - heavy, grey clouds
            case Biome.Snow:
            case Biome.Tundra:
            case Biome.Frozen:
            case Biome.Arctic:
            case Biome.Glacier:
            case Biome.IcicleField:
            case Biome.CryoForest:
                cloudDensity = 0.8f;
                cloudColor = new Color(0.85f, 0.85f, 0.9f, 0.8f);
                scrollSpeed = 0.4f;
                shadowIntensity = 0.2f;
                break;
                
            // Desert biomes - sparse clouds
            case Biome.Desert:
            case Biome.Savannah:
            case Biome.Steppe:
                cloudDensity = 0.2f;
                cloudColor = new Color(1f, 0.98f, 0.95f, 0.4f);
                scrollSpeed = 0.6f;
                shadowIntensity = 0.4f; // Stronger shadows in sunny desert
                break;
                
            // Volcanic/hellish - dark, ominous clouds
            case Biome.Volcanic:
            case Biome.Hellscape:
            case Biome.Brimstone:
            case Biome.Ashlands:
            case Biome.CharredForest:
            case Biome.Scorched:
                cloudDensity = 0.6f;
                cloudColor = new Color(0.3f, 0.25f, 0.2f, 0.7f); // Dark smoke-like
                scrollSpeed = 0.8f;
                shadowIntensity = 0.5f;
                cloudHeight = 80f; // Lower, more oppressive
                break;
                
            // Steam/geothermal - wispy clouds
            case Biome.Steam:
                cloudDensity = 0.4f;
                cloudColor = new Color(0.95f, 0.95f, 1f, 0.5f);
                scrollSpeed = 1.0f;
                cloudHeight = 60f;
                break;
                
            // Coastal/water biomes - moderate with blue tint
            case Biome.Coast:
            case Biome.Ocean:
            case Biome.Seas:
            case Biome.Floodlands:
                cloudDensity = 0.5f;
                cloudColor = new Color(0.95f, 0.97f, 1f, 0.55f);
                scrollSpeed = 0.7f;
                break;
                
            // Mountain - high clouds
            case Biome.Mountain:
                cloudDensity = 0.4f;
                cloudColor = new Color(1f, 1f, 1f, 0.5f);
                cloudHeight = 200f;
                scrollSpeed = 0.8f;
                break;
                
            // Mars - thin, dusty atmosphere
            case Biome.MartianRegolith:
            case Biome.MartianCanyon:
            case Biome.MartianDunes:
            case Biome.MartianPolarIce:
                cloudDensity = 0.15f;
                cloudColor = new Color(0.8f, 0.6f, 0.5f, 0.3f); // Dusty red
                scrollSpeed = 0.3f;
                enableCloudShadows = false;
                break;
                
            // Venus - thick, yellow clouds
            case Biome.VenusLava:
            case Biome.VenusianPlains:
            case Biome.VenusHighlands:
                cloudDensity = 0.9f;
                cloudColor = new Color(0.9f, 0.8f, 0.5f, 0.8f); // Yellow-ish
                scrollSpeed = 0.2f;
                cloudHeight = 60f;
                break;
                
            // Gas giants - thick swirling clouds
            case Biome.JovianClouds:
            case Biome.JovianStorm:
            case Biome.SaturnSurface:
            case Biome.SaturnRings:
                cloudDensity = 0.95f;
                cloudColor = new Color(0.9f, 0.85f, 0.7f, 0.9f);
                scrollSpeed = 1.5f;
                useSecondLayer = true;
                secondLayerSpeedMultiplier = 1.5f;
                break;
                
            // Ice giants
            case Biome.UranusIce:
            case Biome.UranusSurface:
            case Biome.NeptuneWinds:
            case Biome.NeptuneIce:
            case Biome.NeptuneSurface:
                cloudDensity = 0.7f;
                cloudColor = new Color(0.7f, 0.8f, 0.9f, 0.7f); // Blue-ish
                scrollSpeed = 2.0f; // Fast winds
                break;
                
            // Moons - minimal or no atmosphere
            case Biome.MoonDunes:
            case Biome.MoonCaves:
            case Biome.MercuryCraters:
            case Biome.MercuryBasalt:
            case Biome.MercuryScarp:
            case Biome.MercurianIce:
            case Biome.EuropaIce:
            case Biome.EuropaRidges:
            case Biome.IoVolcanic:
            case Biome.IoSulfur:
            case Biome.PlutoCryo:
            case Biome.PlutoTholins:
            case Biome.PlutoMountains:
                cloudDensity = 0f; // No atmosphere
                break;
                
            // Titan - orange haze
            case Biome.TitanLakes:
            case Biome.TitanDunes:
            case Biome.TitanIce:
                cloudDensity = 0.6f;
                cloudColor = new Color(0.9f, 0.7f, 0.4f, 0.6f); // Orange haze
                scrollSpeed = 0.3f;
                cloudHeight = 100f;
                break;
                
            default:
                // Default moderate clouds
                cloudDensity = 0.4f;
                cloudColor = new Color(1f, 1f, 1f, 0.5f);
                scrollSpeed = 0.5f;
                break;
        }
    }
    
    void Update()
    {
        // Animate cloud scrolling
        if (cloudMaterial1 != null)
        {
            scrollOffset1 += windDirection.normalized * scrollSpeed * Time.deltaTime * 0.01f;
            cloudMaterial1.mainTextureOffset = scrollOffset1;
        }
        
        if (cloudMaterial2 != null)
        {
            scrollOffset2 += windDirection.normalized * scrollSpeed * secondLayerSpeedMultiplier * Time.deltaTime * 0.01f;
            cloudMaterial2.mainTextureOffset = scrollOffset2;
        }
        
        if (shadowMaterial != null)
        {
            // Shadow follows primary cloud layer
            shadowMaterial.mainTextureOffset = scrollOffset1;
        }
    }
    
    /// <summary>
    /// Set wind direction (can be synced with grass wind)
    /// </summary>
    public void SetWindDirection(Vector3 wind3D)
    {
        windDirection = new Vector2(wind3D.x, wind3D.z).normalized;
    }
    
    /// <summary>
    /// Clear all cloud objects
    /// </summary>
    public void ClearClouds()
    {
        if (cloudLayer1 != null)
        {
            if (Application.isPlaying)
                Destroy(cloudLayer1);
            else
                DestroyImmediate(cloudLayer1);
            cloudLayer1 = null;
        }
        
        if (cloudLayer2 != null)
        {
            if (Application.isPlaying)
                Destroy(cloudLayer2);
            else
                DestroyImmediate(cloudLayer2);
            cloudLayer2 = null;
        }
        
        if (shadowProjector != null)
        {
            if (Application.isPlaying)
                Destroy(shadowProjector);
            else
                DestroyImmediate(shadowProjector);
            shadowProjector = null;
        }
        
        cloudMaterial1 = null;
        cloudMaterial2 = null;
        shadowMaterial = null;
        scrollOffset1 = Vector2.zero;
        scrollOffset2 = Vector2.zero;
    }
    
    void OnDestroy()
    {
        ClearClouds();
    }
}

