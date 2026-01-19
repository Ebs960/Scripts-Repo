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
    public bool useSecondLayer = false;
    
    [Tooltip("Height offset for second layer")]
    public float secondLayerHeightOffset = 30f;
    
    [Tooltip("Speed multiplier for second layer (creates parallax)")]
    [Range(0.5f, 2f)]
    public float secondLayerSpeedMultiplier = 0.7f;
    
    [Tooltip("Alpha multiplier for second layer")]
    [Range(0f, 1f)]
    public float secondLayerAlpha = 0.4f;
    
    [Header("Venus Thick Atmosphere")]
    [Tooltip("Enable Venus-style super thick atmosphere (overrides other settings)")]
    public bool venusAtmosphere = false;
    
    [Tooltip("Number of cloud layers for thick atmosphere")]
    [Range(3, 8)]
    public int thickAtmosphereLayers = 5;
    
    [Tooltip("Venus atmosphere visibility range (how far you can see)")]
    [Range(20f, 200f)]
    public float venusVisibility = 80f;
    
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
    
    // Venus thick atmosphere layers
    private GameObject[] venusCloudLayers;
    private Material[] venusCloudMaterials;
    private Vector2[] venusScrollOffsets;
    private GameObject venusGroundFog;
    
    // Shared texture cache (memory optimization - reuse textures across layers)
    private static Texture2D sharedCloudTexture;
    private static Texture2D sharedVenusTexture;
    private static int sharedTextureRefCount = 0;
    
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
        
        // Check for Venus thick atmosphere mode
        if (venusAtmosphere)
        {
            CreateVenusAtmosphere(mapSize, biome);
            return;
        }
        
        // Skip if density is too low
        if (cloudDensity < 0.05f)
        {
            // Skipping clouds - density too low for this biome
            return;
        }
        
        // Ensure cloud plane is large enough
        cloudPlaneSize = Mathf.Max(cloudPlaneSize, mapSize * 2f);
        
        // Create or reuse shared cloud texture (memory optimization)
        Texture2D cloudTexture = GetOrCreateSharedCloudTexture();
        sharedTextureRefCount++;
        
        // Create primary cloud layer
        cloudLayer1 = CreateCloudPlane("CloudLayer1", cloudHeight, cloudTexture, cloudColor);
        
        // Create secondary layer for depth (reuses same texture)
        if (useSecondLayer)
        {
            Color layer2Color = cloudColor;
            layer2Color.a *= secondLayerAlpha;
            cloudLayer2 = CreateCloudPlane("CloudLayer2", cloudHeight + secondLayerHeightOffset, cloudTexture, layer2Color);
        }
        
        // Create shadow projector (reuses same texture)
        if (enableCloudShadows)
        {
            CreateCloudShadowProjector(cloudTexture);
        }
        
        // Cloud creation complete - density: {cloudDensity}, height: {cloudHeight}
    }
    
    /// <summary>
    /// Create Venus-style super thick atmosphere with multiple cloud layers
    /// Venus has 20km thick sulfuric acid clouds that completely obscure the surface
    /// </summary>
    private void CreateVenusAtmosphere(float mapSize, Biome biome)
    {
        cloudPlaneSize = Mathf.Max(cloudPlaneSize, mapSize * 2.5f);
        
        // Initialize Venus cloud arrays
        venusCloudLayers = new GameObject[thickAtmosphereLayers];
        venusCloudMaterials = new Material[thickAtmosphereLayers];
        venusScrollOffsets = new Vector2[thickAtmosphereLayers];
        
        // Venus cloud colors - sulfuric acid creates yellow/orange haze
        Color[] venusColors = new Color[]
        {
            new Color(0.95f, 0.85f, 0.55f, 0.95f),  // Top layer - bright yellow
            new Color(0.9f, 0.75f, 0.45f, 0.85f),   // Upper mid
            new Color(0.85f, 0.65f, 0.35f, 0.8f),   // Mid
            new Color(0.75f, 0.55f, 0.3f, 0.75f),   // Lower mid
            new Color(0.65f, 0.45f, 0.25f, 0.7f),   // Low
            new Color(0.55f, 0.4f, 0.2f, 0.65f),    // Very low
            new Color(0.5f, 0.35f, 0.18f, 0.6f),    // Near ground
            new Color(0.45f, 0.32f, 0.15f, 0.55f),  // Ground fog
        };
        
        // Create or reuse shared Venus texture (memory optimization - all layers share one texture)
        Texture2D sharedTexture = GetOrCreateSharedVenusTexture();
        sharedTextureRefCount++;
        
        // Create multiple cloud layers at different heights
        float baseHeight = 30f;  // Start low for oppressive atmosphere
        float heightSpacing = 25f;
        
        for (int i = 0; i < thickAtmosphereLayers; i++)
        {
            float layerHeight = baseHeight + (i * heightSpacing);
            
            // Get color for this layer (varied by index for depth)
            Color layerColor = venusColors[Mathf.Min(i, venusColors.Length - 1)];
            
            // Create cloud plane (all layers share the same texture)
            string layerName = $"VenusCloudLayer_{i}";
            venusCloudLayers[i] = CreateCloudPlane(layerName, layerHeight, sharedTexture, layerColor);
            venusCloudMaterials[i] = venusCloudLayers[i].GetComponent<MeshRenderer>().material;
            venusScrollOffsets[i] = new Vector2(i * 0.1f, i * 0.05f); // Offset each layer for variety
            
            // Vary the wind direction slightly per layer (creates realistic swirling)
            float windAngle = (i * 15f) * Mathf.Deg2Rad;
            Vector2 layerWind = new Vector2(
                Mathf.Cos(windAngle) + windDirection.x,
                Mathf.Sin(windAngle) + windDirection.y
            ).normalized;
            
            // Store wind direction in material's secondary offset (we'll use it in Update)
            venusCloudMaterials[i].SetVector("_WindDir", new Vector4(layerWind.x, layerWind.y, 0, 0));
            
            // Vary texture scale per layer for more variation with shared texture
            venusCloudMaterials[i].mainTextureScale = new Vector2(1.5f + i * 0.3f, 1.5f + i * 0.3f);
        }
        
        // Create thick ground-level fog for Venus (reuses shared texture)
        CreateVenusGroundFog(mapSize);
        
        // Create very dark shadows (Venus surface is very dark) - reuses shared texture
        if (enableCloudShadows)
        {
            CreateCloudShadowProjector(sharedTexture);
            if (shadowMaterial != null)
            {
                shadowMaterial.SetColor(BaseColorID, new Color(0, 0, 0, 0.6f));
                shadowMaterial.SetColor(ColorID, new Color(0, 0, 0, 0.6f));
            }
        }
        
        // Venus thick atmosphere created with {thickAtmosphereLayers} layers
    }
    
    /// <summary>
    /// Create a Venus-specific cloud texture with sulfuric swirls
    /// Enhanced with better noise patterns and color gradients
    /// </summary>
    private Texture2D CreateVenusCloudTexture(float density, int layerIndex)
    {
        int size = 256; // Higher resolution for better quality
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Trilinear; // Better filtering
        texture.anisoLevel = 4;
        
        // Multiple octaves with different offsets per layer
        float[] frequencies = { 1.5f, 3f, 6f, 12f };
        float[] amplitudes = { 1f, 0.6f, 0.35f, 0.15f };
        float totalAmplitude = 0f;
        foreach (float a in amplitudes) totalAmplitude += a;
        
        // Per-layer random offset for variety
        float offsetX = Random.Range(0f, 1000f) + layerIndex * 100f;
        float offsetY = Random.Range(0f, 1000f) + layerIndex * 100f;
        
        // Domain warping for swirly Venus clouds
        float warpStrength = 0.3f + layerIndex * 0.05f;
        
        Color[] colors = new Color[size * size];
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = x / (float)size;
                float ny = y / (float)size;
                
                // Domain warping for swirly effect
                float warpX = Mathf.PerlinNoise((nx + offsetX) * 2f, (ny + offsetY) * 2f) * warpStrength;
                float warpY = Mathf.PerlinNoise((nx + offsetX + 100f) * 2f, (ny + offsetY + 100f) * 2f) * warpStrength;
                
                float value = 0f;
                for (int i = 0; i < frequencies.Length; i++)
                {
                    float fx = (nx + warpX) * frequencies[i] + offsetX;
                    float fy = (ny + warpY) * frequencies[i] + offsetY;
                    value += Mathf.PerlinNoise(fx, fy) * amplitudes[i];
                }
                
                value /= totalAmplitude;
                
                // Venus clouds are very dense - high base opacity
                float threshold = 1f - density;
                value = Mathf.Clamp01((value - threshold * 0.3f) / (1f - threshold * 0.3f));
                
                // Smooth edges
                value = Mathf.SmoothStep(0, 1, value);
                
                // Add realistic yellow-orange sulfuric acid tint with variation
                float tintVar = Mathf.PerlinNoise(nx * 4f + offsetX, ny * 4f + offsetY) * 0.15f;
                float thicknessVar = Mathf.PerlinNoise(nx * 2f + offsetX + 50f, ny * 2f + offsetY + 50f);
                
                // Thicker areas are more orange, thinner areas are more yellow
                float orangeAmount = Mathf.Lerp(0.1f, 0.3f, thicknessVar);
                float red = Mathf.Lerp(0.95f, 0.9f, thicknessVar) - tintVar * 0.1f;
                float green = Mathf.Lerp(0.85f, 0.7f, thicknessVar) - tintVar * 0.15f - orangeAmount;
                float blue = Mathf.Lerp(0.7f, 0.5f, thicknessVar) - tintVar * 0.2f - orangeAmount * 0.5f;
                
                colors[y * size + x] = new Color(
                    Mathf.Clamp01(red),
                    Mathf.Clamp01(green),
                    Mathf.Clamp01(blue),
                    value
                );
            }
        }
        
        texture.SetPixels(colors);
        texture.Apply(true);
        
        return texture;
    }
    
    /// <summary>
    /// Create thick ground-level fog for Venus atmosphere
    /// Uses shared Venus texture for memory optimization
    /// </summary>
    private void CreateVenusGroundFog(float mapSize)
    {
        venusGroundFog = new GameObject("VenusGroundFog");
        venusGroundFog.transform.SetParent(transform);
        venusGroundFog.transform.position = new Vector3(0, 5f, 0); // Just above ground
        venusGroundFog.transform.rotation = Quaternion.Euler(90, 0, 0);
        
        MeshFilter meshFilter = venusGroundFog.AddComponent<MeshFilter>();
        meshFilter.mesh = CreateQuadMesh(cloudPlaneSize);
        
        MeshRenderer renderer = venusGroundFog.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        
        // Dense orange-brown ground fog (reuses shared Venus texture)
        Texture2D fogTexture = GetOrCreateSharedVenusTexture();
        Material fogMat = CreateCloudMaterial(fogTexture, new Color(0.6f, 0.4f, 0.2f, 0.5f));
        fogMat.mainTextureScale = new Vector2(4f, 4f); // Smaller scale for ground fog
        
        renderer.material = fogMat;
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
        if (mat == null)
        {
            Debug.LogError($"[BattlefieldClouds] Failed to create cloud material for {name}. Cloud layer will not render.");
            return null; // Cannot create cloud plane without material
        }
        renderer.material = mat;
        
        // Store material reference for animation
        if (name == "CloudLayer1")
            cloudMaterial1 = mat;
        else
            cloudMaterial2 = mat;
        
        return cloudGO;
    }
    
    /// <summary>
    /// Create cloud material with enhanced transparency and lighting
    /// Uses URP Lit shader for better visual quality with lighting interaction
    /// </summary>
    private Material CreateCloudMaterial(Texture2D texture, Color color)
    {
        // Try URP Lit shader first for better lighting interaction
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Transparent");
        }
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }
        if (shader == null)
        {
            Debug.LogError("[BattlefieldClouds] No shader found! Clouds will not render. Make sure URP or Standard shaders are available.");
            return null; // Cannot create material without shader
        }
        
        Material mat = new Material(shader);
        mat.mainTexture = texture;
        
        // Set color (try both URP and standard property names)
        mat.SetColor(BaseColorID, color);
        mat.SetColor(ColorID, color);
        
        // Enhanced transparency settings
        if (shader.name.Contains("Universal Render Pipeline"))
        {
            // URP shader settings
            mat.SetFloat("_Surface", 1); // Transparent
            mat.SetFloat("_Blend", 0); // Alpha blend
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            
            // Enhanced lighting properties for clouds
            mat.SetFloat("_Smoothness", 0.1f); // Slight specularity for realistic cloud shine
            mat.SetFloat("_Metallic", 0f);
            
            // Enable emission for self-illumination (clouds glow slightly)
            if (mat.HasProperty("_EmissionColor"))
            {
                Color emission = color * 0.1f; // Subtle glow
                emission.a = 1f;
                mat.SetColor("_EmissionColor", emission);
                mat.EnableKeyword("_EMISSION");
            }
        }
        else
        {
            // Standard shader fallback
            mat.SetFloat("_Mode", 3); // Transparent mode
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }
        
        mat.renderQueue = 3000; // Transparent queue
        
        // Set texture tiling with better filtering
        mat.mainTextureScale = new Vector2(2f, 2f);
        mat.mainTextureOffset = Vector2.zero;
        
        // Enable double-sided rendering for clouds
        mat.SetFloat("_Cull", 0); // Off (double-sided)
        
        return mat;
    }
    
    /// <summary>
    /// Get or create a shared cloud texture (memory optimization)
    /// </summary>
    private Texture2D GetOrCreateSharedCloudTexture()
    {
        if (sharedCloudTexture == null)
        {
            sharedCloudTexture = CreateCloudTexture();
            // Shared cloud texture created
        }
        return sharedCloudTexture;
    }
    
    /// <summary>
    /// Get or create a shared Venus texture (memory optimization)
    /// </summary>
    private Texture2D GetOrCreateSharedVenusTexture()
    {
        if (sharedVenusTexture == null)
        {
            sharedVenusTexture = CreateVenusCloudTexture(0.8f, 0);
            // Shared Venus texture created
        }
        return sharedVenusTexture;
    }
    
    /// <summary>
    /// Create a procedural cloud texture using advanced noise techniques
    /// Uses Worley/Voronoi noise for cloud cell structure + Perlin for detail
    /// Enhanced quality with better filtering and color gradients
    /// </summary>
    private Texture2D CreateCloudTexture()
    {
        int size = 256; // Increased for better quality (can be reduced if memory is tight)
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Trilinear; // Better filtering for smoother clouds
        texture.anisoLevel = 4; // Anisotropic filtering for better quality at angles
        
        // Generate cloud pattern using hybrid noise approach
        // Base: Worley/Voronoi noise for cloud cell structure
        // Detail: Perlin noise for fine detail and variation
        
        // Multiple octaves for realistic clouds
        float[] frequencies = { 1.5f, 3f, 6f, 12f, 24f };
        float[] amplitudes = { 1f, 0.6f, 0.35f, 0.2f, 0.1f };
        float totalAmplitude = 0f;
        foreach (float a in amplitudes) totalAmplitude += a;
        
        // Random offset for variety
        float offsetX = Random.Range(0f, 1000f);
        float offsetY = Random.Range(0f, 1000f);
        
        // Voronoi cell centers for cloud structure (Worley noise approximation)
        int cellCount = 8;
        Vector2[] cellCenters = new Vector2[cellCount];
        for (int i = 0; i < cellCount; i++)
        {
            cellCenters[i] = new Vector2(
                Random.Range(0f, 1f) + offsetX * 0.001f,
                Random.Range(0f, 1f) + offsetY * 0.001f
            );
        }
        
        Color[] colors = new Color[size * size];
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = x / (float)size;
                float ny = y / (float)size;
                
                // === STEP 1: Voronoi/Worley noise for cloud cell structure ===
                float minDist = float.MaxValue;
                float secondMinDist = float.MaxValue;
                
                for (int i = 0; i < cellCount; i++)
                {
                    float dx = nx - cellCenters[i].x;
                    float dy = ny - cellCenters[i].y;
                    // Wrap around for seamless tiling
                    dx = dx - Mathf.Round(dx);
                    dy = dy - Mathf.Round(dy);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    
                    if (dist < minDist)
                    {
                        secondMinDist = minDist;
                        minDist = dist;
                    }
                    else if (dist < secondMinDist)
                    {
                        secondMinDist = dist;
                    }
                }
                
                // Worley noise value (distance to second closest point)
                float worleyValue = Mathf.Clamp01(secondMinDist * 3f);
                
                // === STEP 2: Perlin noise for detail and variation ===
                float perlinValue = 0f;
                for (int i = 0; i < frequencies.Length; i++)
                {
                    float fx = nx * frequencies[i] + offsetX;
                    float fy = ny * frequencies[i] + offsetY;
                    perlinValue += Mathf.PerlinNoise(fx, fy) * amplitudes[i];
                }
                perlinValue /= totalAmplitude;
                
                // === STEP 3: Combine Worley (structure) + Perlin (detail) ===
                float combinedValue = worleyValue * 0.6f + perlinValue * 0.4f;
                
                // === STEP 4: Apply density threshold with smooth falloff ===
                float threshold = 1f - cloudDensity;
                float densityValue = Mathf.Clamp01((combinedValue - threshold * 0.7f) / (1f - threshold * 0.7f));
                
                // === STEP 5: Apply softness with multiple smoothstep passes ===
                if (cloudSoftness > 0)
                {
                    densityValue = Mathf.SmoothStep(0, 1, densityValue);
                    // Second pass for extra softness
                    if (cloudSoftness > 0.5f)
                    {
                        densityValue = Mathf.SmoothStep(0, 1, densityValue);
                    }
                }
                
                // === STEP 6: Add subtle color variation based on thickness ===
                // Thicker clouds are slightly darker/blue-tinted (more realistic)
                float thickness = densityValue;
                float blueTint = Mathf.Lerp(0f, 0.05f, thickness);
                float brightness = Mathf.Lerp(1f, 0.95f, thickness * 0.3f);
                
                // === STEP 7: Create final color with alpha ===
                float alpha = densityValue;
                colors[y * size + x] = new Color(
                    brightness,
                    brightness - blueTint * 0.5f,
                    brightness - blueTint,
                    alpha
                );
            }
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
        if (shader == null)
        {
            Debug.LogError("[BattlefieldClouds] No shader found for cloud shadows! Shadows will not render.");
            return; // Cannot create shadow material without shader
        }
        
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
                cloudDensity = 0.5f;
                cloudColor = new Color(1f, 1f, 1f, 0.6f);
                scrollSpeed = 0.5f;
                break;
                
            // Cold biomes - heavy, grey clouds
            case Biome.Arctic:
            case Biome.Tundra:
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
                cloudDensity = 0.2f;
                cloudColor = new Color(1f, 0.98f, 0.95f, 0.4f);
                scrollSpeed = 0.6f;
                shadowIntensity = 0.4f; // Stronger shadows in sunny desert
                break;
                
            // Volcanic/hellish - dark, ominous clouds
            case Biome.Volcanic:
            case Biome.Hellscape:
            case Biome.Ashlands:
            case Biome.CharredForest:
            case Biome.Scorched:
                cloudDensity = 0.6f;
                cloudColor = new Color(0.3f, 0.25f, 0.2f, 0.7f); // Dark smoke-like
                scrollSpeed = 0.8f;
                shadowIntensity = 0.5f;
                cloudHeight = 80f; // Lower, more oppressive
                break;
                
            // Steamlands/geothermal - wispy clouds
            case Biome.Steamlands:
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
                
            // Venus - SUPER THICK sulfuric acid clouds that cover entire map
            case Biome.VenusLava:
            case Biome.VenusianPlains:
            case Biome.VenusHighlands:
                venusAtmosphere = true; // Enable Venus thick atmosphere mode
                thickAtmosphereLayers = 6;
                venusVisibility = 60f;
                cloudDensity = 0.95f;
                cloudColor = new Color(0.9f, 0.75f, 0.45f, 0.9f); // Yellow-orange sulfuric
                scrollSpeed = 0.15f; // Slow, oppressive movement
                cloudHeight = 40f;
                useSecondLayer = true;
                secondLayerSpeedMultiplier = 0.8f;
                enableCloudShadows = true;
                shadowIntensity = 0.7f; // Dark surface
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
            case Biome.MoonCraters:
            case Biome.MercuryPlains:
            case Biome.MercuryBasalt:
            case Biome.MercuryScarp:
            case Biome.MercurianIce:
            case Biome.EuropaIce:
            case Biome.EuropaRidges:
            case Biome.IoVolcanic:
            case Biome.IoSulfur:
            case Biome.PlutoCryo:
            case Biome.PlutoTholins:
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
        // Animate Venus thick atmosphere layers
        if (venusCloudMaterials != null && venusCloudMaterials.Length > 0)
        {
            for (int i = 0; i < venusCloudMaterials.Length; i++)
            {
                if (venusCloudMaterials[i] != null)
                {
                    // Each layer moves at slightly different speed and direction
                    float layerSpeed = scrollSpeed * (0.5f + i * 0.15f);
                    float angle = i * 12f * Mathf.Deg2Rad;
                    Vector2 layerWind = new Vector2(
                        windDirection.x + Mathf.Sin(angle) * 0.3f,
                        windDirection.y + Mathf.Cos(angle) * 0.3f
                    ).normalized;
                    
                    venusScrollOffsets[i] += layerWind * layerSpeed * Time.deltaTime * 0.01f;
                    venusCloudMaterials[i].mainTextureOffset = venusScrollOffsets[i];
                }
            }
            
            // Shadow follows lowest layer
            if (shadowMaterial != null && venusScrollOffsets.Length > 0)
            {
                shadowMaterial.mainTextureOffset = venusScrollOffsets[0];
            }
            
            return; // Skip normal cloud animation if using Venus mode
        }
        
        // Animate normal cloud scrolling
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
        
        // Clear Venus thick atmosphere layers
        if (venusCloudLayers != null)
        {
            for (int i = 0; i < venusCloudLayers.Length; i++)
            {
                if (venusCloudLayers[i] != null)
                {
                    if (Application.isPlaying)
                        Destroy(venusCloudLayers[i]);
                    else
                        DestroyImmediate(venusCloudLayers[i]);
                }
            }
            venusCloudLayers = null;
        }
        
        if (venusGroundFog != null)
        {
            if (Application.isPlaying)
                Destroy(venusGroundFog);
            else
                DestroyImmediate(venusGroundFog);
            venusGroundFog = null;
        }
        
        cloudMaterial1 = null;
        cloudMaterial2 = null;
        shadowMaterial = null;
        venusCloudMaterials = null;
        venusScrollOffsets = null;
        scrollOffset1 = Vector2.zero;
        scrollOffset2 = Vector2.zero;
        
        // Reset Venus mode flag
        venusAtmosphere = false;
        
        // Decrement shared texture reference count and cleanup if no longer needed
        if (sharedTextureRefCount > 0)
        {
            sharedTextureRefCount--;
            if (sharedTextureRefCount <= 0)
            {
                if (sharedCloudTexture != null)
                {
                    if (Application.isPlaying)
                        Destroy(sharedCloudTexture);
                    else
                        DestroyImmediate(sharedCloudTexture);
                    sharedCloudTexture = null;
                }
                if (sharedVenusTexture != null)
                {
                    if (Application.isPlaying)
                        Destroy(sharedVenusTexture);
                    else
                        DestroyImmediate(sharedVenusTexture);
                    sharedVenusTexture = null;
                }
                sharedTextureRefCount = 0;
            }
        }
    }
    
    void OnDestroy()
    {
        ClearClouds();
    }
}

