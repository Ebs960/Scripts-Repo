using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Renders a flat equirectangular map using a texture instead of individual tile meshes.
/// This is the gameplay view - horizontal wrapping enabled.
/// </summary>
public class FlatMapTextureRenderer : MonoBehaviour
{
    [Header("Texture Settings")]
    [Tooltip("Resolution width (horizontal samples)")]
    [SerializeField] private int textureWidth = 2048;
    
    [Tooltip("Resolution height (vertical samples)")]
    [SerializeField] private int textureHeight = 1024;
    
    [Header("Map Dimensions")]
    [Tooltip("Full horizontal span of the map in world units (wraps east-west).")]
    [SerializeField] private float mapWidth = 360f;
    
    [Tooltip("Full vertical span of the map in world units (no vertical wrap).")]
    [SerializeField] private float mapHeight = 180f;
    
    [Tooltip("Constant Y height for the flat map plane.")]
    [SerializeField] private float flatY = 0f;
    
    [Header("Color Provider")]
    [Tooltip("Optional MinimapColorProvider for custom coloring. If null, uses BiomeColorHelper.")]
    [SerializeField] private MinimapColorProvider colorProvider;
    
    [Header("GPU Acceleration")]
    [Tooltip("Optional compute shader for GPU-accelerated texture baking. If null, uses CPU path.")]
    [SerializeField] private ComputeShader textureBakerComputeShader;
    
    [Header("Pre-Build Options")]
    [Tooltip("If true, the flat map will be pre-built when planet generation completes.")]
    [SerializeField] private bool preBuildOnPlanetReady = true;
    
    [Header("Elevation Displacement")]
    [Tooltip("Enable elevation displacement on flat map (requires subdivided mesh)")]
    [SerializeField] private bool enableElevationDisplacement = true;
    [Tooltip("Number of mesh segments for the flat map (higher = smoother displacement, more vertices)")]
    [SerializeField] private int meshSubdivisions = 256; // Subdivisions per side (256x128 recommended for good detail)
    [Tooltip("Displacement strength multiplier (how much elevation affects height)")]
    [SerializeField] private float displacementStrength = 0.1f; // 10% of map height
    [Tooltip("Custom shader for GPU-based vertex displacement. If null, uses Standard shader.")]
    [SerializeField] private Shader flatMapDisplacementShader;
    
    private GameObject quadObject;
    private MeshRenderer quadRenderer;
    private Material mapMaterial;
    private RenderTexture mapTexture;  // Changed from Texture2D to RenderTexture - use GPU texture directly
    private PlanetTextureBaker.BakeResult bakeResult;
    private bool isBuilt;
    private bool _subscribedToPlanetReady;
    private bool _subscribedToSurfaceReady;
    private WorldPicker worldPicker;
    private PlanetGenerator planetGen; // Store for material setup
    private PlanetGenerator _surfaceEventSource; // Current generator subscribed for surface ready
    
    public float MapWidth => mapWidth;
    public float MapHeight => mapHeight;
    public bool IsBuilt => isBuilt;
    public Texture MapTexture => mapTexture;  // RenderTexture implements Texture interface
    
    private void OnEnable()
    {
        TrySubscribeToPlanetReady();
    }
    
    private void OnDisable()
    {
        TryUnsubscribeFromPlanetReady();
        TryUnsubscribeFromSurfaceReady();
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
        
        if (gen.HasGeneratedSurface)
        {
Rebuild(gen);
        }
        else
        {
TrySubscribeToSurfaceReady(gen);
        }
    }

    private void TrySubscribeToSurfaceReady(PlanetGenerator gen)
    {
        if (gen == null) return;
        if (_subscribedToSurfaceReady && _surfaceEventSource == gen) return;
        
        // Ensure we don't have stale subscriptions
        TryUnsubscribeFromSurfaceReady();
        
        gen.OnSurfaceGenerated += HandleSurfaceGenerated;
        _surfaceEventSource = gen;
        _subscribedToSurfaceReady = true;
    }
    
    private void TryUnsubscribeFromSurfaceReady()
    {
        if (!_subscribedToSurfaceReady) return;
        if (_surfaceEventSource != null)
        {
            _surfaceEventSource.OnSurfaceGenerated -= HandleSurfaceGenerated;
        }
        _surfaceEventSource = null;
        _subscribedToSurfaceReady = false;
    }
    
    private void HandleSurfaceGenerated()
    {
        // Called when the subscribed PlanetGenerator completes surface generation
        var gen = _surfaceEventSource ?? GameManager.Instance?.GetCurrentPlanetGenerator();
        TryUnsubscribeFromSurfaceReady();
        if (gen == null) return;
        Debug.Log("[FlatMapTextureRenderer] Surface generated; building flat map texture.");
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
        
        // Don't destroy texture - it might be shared with the minimap
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
        
        // Calculate map dimensions from GameManager
        if (GameManager.Instance != null)
        {
            float gmW = GameManager.Instance.GetFlatMapWidth();
            float gmH = GameManager.Instance.GetFlatMapHeight();
            if (gmW > 0.001f && gmH > 0.001f)
            {
                mapWidth = gmW;
                mapHeight = gmH;
            }
        }
        // Fallback: if dimensions are invalid or extremely small, use sensible defaults
        if (mapWidth <= 0.001f || mapHeight <= 0.001f)
        {
            mapWidth = Mathf.Max(mapWidth, 360f);
            mapHeight = Mathf.Max(mapHeight, 180f);
        }
        
        // Store planet reference
        this.planetGen = planetGen;
        
        // Bake texture using PlanetTextureBaker
        // Use GPU only when rendering solid biome colors; textures/custom require per-pixel UV sampling (CPU)
        bool gpuAllowed = (textureBakerComputeShader != null) &&
                          (colorProvider == null || colorProvider.renderMode == MinimapRenderMode.BiomeColors);
        if (gpuAllowed)
        {
            bakeResult = PlanetTextureBaker.BakeGPU(planetGen, colorProvider, textureBakerComputeShader, textureWidth, textureHeight);
}
        else
        {
            bakeResult = PlanetTextureBaker.Bake(planetGen, colorProvider, textureWidth, textureHeight);
}
        
        if (bakeResult.texture == null)
        {
            Debug.LogError("[FlatMapTextureRenderer] Failed to bake planet texture. bakeResult.texture is NULL!");
            Debug.LogError($"[FlatMapTextureRenderer] colorProvider is {(colorProvider == null ? "NULL" : "ASSIGNED")}");
            Debug.LogError($"[FlatMapTextureRenderer] bakeResult.lut length: {(bakeResult.lut != null ? bakeResult.lut.Length : 0)}");
            return;
        }
        
        mapTexture = bakeResult.texture;
Debug.Log($"[FlatMapTextureRenderer] RenderTexture: Dimensions {(mapTexture != null ? mapTexture.width + "x" + mapTexture.height : "NULL")}, Format ARGB32");
        
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
Debug.Log($"[FlatMapTextureRenderer] Material._MainTex: {(mapMaterial.GetTexture("_MainTex") != null ? "ASSIGNED" : "NULL")}");
        }
        else
        {
            Debug.LogError("[FlatMapTextureRenderer] mapMaterial is NULL, cannot apply texture!");
        }
        
        // Initialize TerrainOverlayGPU (Phase 6: GPU overlays)
        InitializeTerrainOverlays();
        
        isBuilt = true;
        
        // Update WorldPicker if it exists
        UpdateWorldPicker();
        
        // FINAL VALIDATION: Check that texture is actually applied and visible
        if (quadRenderer != null && mapMaterial != null)
        {
            Texture appliedTex = mapMaterial.GetTexture("_MainTex");
            if (appliedTex == null)
            {
                Debug.LogError("[FlatMapTextureRenderer] CRITICAL: mapMaterial._MainTex is NULL even though we set it!");
                Debug.LogError($"[FlatMapTextureRenderer] Material: {mapMaterial.name}");
                Debug.LogError($"[FlatMapTextureRenderer] Shader: {mapMaterial.shader.name}");
            }
            else
            {
}
        }
}
    
    /// <summary>
    /// Initialize TerrainOverlayGPU system for fog and ownership overlays.
    /// </summary>
    private void InitializeTerrainOverlays()
    {
        var overlayGPU = FindAnyObjectByType<TerrainOverlayGPU>();
        if (overlayGPU != null && bakeResult.lut != null)
        {
            overlayGPU.Initialize(bakeResult.lut, bakeResult.width, bakeResult.height, textureWidth, textureHeight);
            // Subscribe to TileSystem events for overlay updates
            SubscribeToTileSystemEvents(overlayGPU);
            // Apply overlay textures to material
            ApplyOverlayTexturesToMaterial(overlayGPU);
        }
    }
    
    private TerrainOverlayGPU _cachedOverlayGPU;
    
    /// <summary>
    /// Subscribe to TileSystem events to update overlays when fog/ownership changes.
    /// </summary>
    private void SubscribeToTileSystemEvents(TerrainOverlayGPU overlayGPU)
    {
        if (TileSystem.Instance == null) return;
        
        _cachedOverlayGPU = overlayGPU;
        
        // Unsubscribe first to avoid duplicates
        TileSystem.Instance.OnTileOwnerChanged -= HandleTileOwnerChanged;
        TileSystem.Instance.OnFogChanged -= HandleFogChanged;
        
        // Subscribe to events
        TileSystem.Instance.OnTileOwnerChanged += HandleTileOwnerChanged;
        TileSystem.Instance.OnFogChanged += HandleFogChanged;
    }
    
    private void HandleTileOwnerChanged(int tile, int oldOwner, int newOwner)
    {
        if (_cachedOverlayGPU != null)
        {
            _cachedOverlayGPU.MarkTilesDirty(new[] { tile });
            _cachedOverlayGPU.UpdateOverlays();
        }
    }
    
    private void HandleFogChanged(int civId, List<int> changedTiles)
    {
        if (_cachedOverlayGPU != null)
        {
            _cachedOverlayGPU.MarkTilesDirty(changedTiles);
            _cachedOverlayGPU.UpdateOverlays();
        }
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        if (TileSystem.Instance != null)
        {
            TileSystem.Instance.OnTileOwnerChanged -= HandleTileOwnerChanged;
            TileSystem.Instance.OnFogChanged -= HandleFogChanged;
        }
    }
    
    /// <summary>
    /// Apply overlay textures (fog mask and ownership) to the material.
    /// </summary>
    private void ApplyOverlayTexturesToMaterial(TerrainOverlayGPU overlayGPU)
    {
        if (mapMaterial == null) return;
        
        // Apply fog mask texture
        var fogMask = overlayGPU.GetFogMaskTexture();
        if (fogMask != null)
        {
            mapMaterial.SetTexture("_FogMask", fogMask);
            mapMaterial.SetFloat("_EnableFog", overlayGPU.EnableFogOverlay ? 1f : 0f);
        }
        
        // Apply ownership overlay texture
        var ownershipTex = overlayGPU.GetOwnershipTexture();
        if (ownershipTex != null)
        {
            mapMaterial.SetTexture("_OwnershipOverlay", ownershipTex);
            mapMaterial.SetFloat("_EnableOwnership", overlayGPU.EnableOwnershipOverlay ? 1f : 0f);
        }
    }
    
    private void UpdateWorldPicker()
    {
        if (worldPicker == null)
            worldPicker = FindAnyObjectByType<WorldPicker>();
        
        if (worldPicker != null && bakeResult.lut != null)
        {
            worldPicker.lut = bakeResult.lut;
            worldPicker.lutWidth = bakeResult.width > 0 ? bakeResult.width : textureWidth;
            worldPicker.lutHeight = bakeResult.height > 0 ? bakeResult.height : textureHeight;
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
        
        // Create material with the required URP custom shader (no fallbacks)
        Shader shaderToUse = flatMapDisplacementShader != null ? flatMapDisplacementShader : Shader.Find("Custom/FlatMapDisplacement_URP");
        if (shaderToUse == null)
        {
            Debug.LogError("[FlatMapTextureRenderer] CreateQuad: Custom/FlatMapDisplacement_URP not found. Assign 'flatMapDisplacementShader' in inspector.");
            return;
        }
        mapMaterial = new Material(shaderToUse);
if (mapMaterial != null && mapTexture != null)
        {
            mapMaterial.mainTexture = mapTexture;
            mapMaterial.SetTexture("_MainTex", mapTexture);
}
        else
        {
            Debug.LogError($"[FlatMapTextureRenderer] CreateQuad: FAILED - mapMaterial is {(mapMaterial == null ? "NULL" : "VALID")}, mapTexture is {(mapTexture == null ? "NULL" : "VALID")}");
        }
        
        // Apply heightmap and parameters
        if (bakeResult.heightmap != null)
        {
            mapMaterial.SetTexture("_Heightmap", bakeResult.heightmap);
            mapMaterial.SetFloat("_FlatHeightScale", displacementStrength);
            mapMaterial.SetFloat("_MapHeight", mapHeight);
        }
        mapMaterial.SetFloat("_Metallic", 0.0f);
        mapMaterial.SetFloat("_Smoothness", 0.3f);
        
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
    /// GPU-accelerated: Uses vertex shader displacement instead of CPU heightmap sampling.
    /// This is dramatically faster than the old CPU GetPixel() approach.
    /// </summary>
    private void CreateSubdividedPlane()
    {
        // Create subdivided plane GameObject
        quadObject = new GameObject("FlatMapSubdividedPlane");
        quadObject.transform.SetParent(transform, false);
        quadObject.transform.localPosition = new Vector3(0f, flatY, 0f);
        quadObject.transform.localRotation = Quaternion.identity;
        quadObject.transform.localScale = Vector3.one; // Scale is handled in mesh
        
        // Generate subdivided mesh (flat plane - displacement happens in GPU vertex shader)
        Mesh mesh = new Mesh();
        mesh.name = "SubdividedPlane";
        
        int segmentsX = meshSubdivisions;
        int segmentsY = Mathf.RoundToInt(meshSubdivisions * (mapHeight / mapWidth)); // Maintain aspect ratio
        int vertexCount = (segmentsX + 1) * (segmentsY + 1);
        Vector3[] vertices = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        Vector3[] normals = new Vector3[vertexCount];
        
        // Generate flat vertices (displacement will be done in GPU vertex shader)
        for (int y = 0; y <= segmentsY; y++)
        {
            for (int x = 0; x <= segmentsX; x++)
            {
                int index = y * (segmentsX + 1) + x;
                
                // UV coordinates (0-1) with horizontal wrapping
                float u = (float)x / segmentsX;
                float v = (float)y / segmentsY;
                uvs[index] = new Vector2(u, v);
                
                // World position (centered, scaled by map dimensions)
                // Y will be displaced by vertex shader based on heightmap
                float worldX = (u - 0.5f) * mapWidth;
                float worldZ = (v - 0.5f) * mapHeight;
                
                // Start with flat plane - GPU shader will displace based on heightmap
                vertices[index] = new Vector3(worldX, flatY, worldZ);
                normals[index] = Vector3.up; // Will be recalculated after displacement
            }
        }
        
        // Generate triangles
        int[] triangles = new int[segmentsX * segmentsY * 6];
        int triIndex = 0;
        
        for (int y = 0; y < segmentsY; y++)
        {
            for (int x = 0; x < segmentsX; x++)
            {
                int current = y * (segmentsX + 1) + x;
                int next = current + segmentsX + 1;
                
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
        mesh.normals = normals; // Will be recalculated by GPU after displacement
        mesh.RecalculateBounds();
        
        // Add MeshFilter and MeshRenderer
        MeshFilter meshFilter = quadObject.AddComponent<MeshFilter>();
        meshFilter.mesh = mesh;
        
        quadRenderer = quadObject.AddComponent<MeshRenderer>();
        
        // Create material with GPU displacement shader
        Shader shaderToUse = flatMapDisplacementShader;
        if (shaderToUse == null)
        {
            // Require the URP custom shader; no fallbacks
            shaderToUse = Shader.Find("Custom/FlatMapDisplacement_URP");
            if (shaderToUse == null)
            {
                Debug.LogError("[FlatMapTextureRenderer] Custom/FlatMapDisplacement_URP not found. Assign 'flatMapDisplacementShader' in the inspector or ensure the URP shader asset exists.");
                return; // Abort setup to avoid magenta from invalid materials
            }
        }
        
        mapMaterial = new Material(shaderToUse);
mapMaterial.mainTexture = mapTexture;
        mapMaterial.SetTexture("_MainTex", mapTexture);
        // Ensure material scalars are applied
        mapMaterial.SetFloat("_Metallic", 0.0f);
        mapMaterial.SetFloat("_Smoothness", 0.3f);
// Apply heightmap for GPU vertex displacement
        if (bakeResult.heightmap != null)
        {
            mapMaterial.SetTexture("_Heightmap", bakeResult.heightmap);
            mapMaterial.SetFloat("_FlatHeightScale", displacementStrength);
            mapMaterial.SetFloat("_MapHeight", mapHeight);
}
        
        // RenderTextures handle wrapping at the shader level; material wrap mode is set via shader
        // No need to set wrapMode on RenderTexture - it's configured during bake
quadRenderer.material = mapMaterial;
        
        // Add MeshCollider for picking
        // Note: MeshCollider will use the original flat mesh, but picking will still work
        // The visual displacement is GPU-only and doesn't affect collision
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
    /// Get a downscaled version of the map texture for minimap use (GPU-accelerated).
    /// Phase 5: Uses Graphics.Blit for GPU downscaling - essentially free!
    /// Returns RenderTexture for best performance (no CPU readback).
    /// </summary>
    /// <param name="targetWidth">Target width for downscaled texture</param>
    /// <param name="targetHeight">Target height for downscaled texture</param>
    /// <param name="returnTexture2D">If true, converts to Texture2D (slow CPU readback). If false, returns RenderTexture (fast, GPU-only).</param>
    /// <returns>Downscaled texture (RenderTexture if returnTexture2D=false, Texture2D if true)</returns>
    public Texture GetDownscaledTexture(int targetWidth, int targetHeight, bool returnTexture2D = false)
    {
        if (mapTexture == null)
            return null;
        
        // GPU-accelerated downscaling using Graphics.Blit (essentially free!)
        RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
        rt.filterMode = FilterMode.Bilinear;
        rt.wrapMode = TextureWrapMode.Repeat;
        Graphics.Blit(mapTexture, rt);
        
        // Return RenderTexture directly for best performance (no CPU readback)
        if (!returnTexture2D)
        {
            // Note: Caller must release this RenderTexture when done using RenderTexture.ReleaseTemporary(rt)
            // For cached usage, consider storing the RenderTexture and releasing on cleanup
            return rt;
        }
        
        // Fallback: Convert to Texture2D if explicitly requested (slow CPU readback)
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
