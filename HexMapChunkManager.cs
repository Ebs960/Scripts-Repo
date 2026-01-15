using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Manages all hex map chunks and handles seamless world wrapping.
/// Replaces FlatMapTextureRenderer with a chunk-based approach that enables:
/// - Seamless horizontal wrap via column teleportation (Civ 5 style)
/// - Per-chunk dirty marking for dynamic tile updates
/// - Same visual quality using PlanetTextureBaker and FlatMapDisplacement_URP shader
/// 
/// This integrates with the existing pipeline:
/// - Uses PlanetTextureBaker.Bake() or BakeGPU() for texture generation
/// - Uses MinimapColorProvider for biome textures/colors
/// - Uses FlatMapDisplacement_URP shader for heightmap displacement and overlays
/// - Subscribes to OnSurfaceGenerated to wait for proper map generation
/// </summary>
public class HexMapChunkManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MinimapColorProvider colorProvider;
    [SerializeField] private ComputeShader textureBakerComputeShader;
    [SerializeField] private Shader flatMapDisplacementShader;
    
    [Header("Texture Settings")]
    [SerializeField] private int textureWidth = 2048;
    [SerializeField] private int textureHeight = 1024;
    
    [Header("Chunk Settings")]
    [Tooltip("Number of chunk columns (X axis). More columns = finer wrap granularity.")]
    [SerializeField] private int chunksX = 8;
    [Tooltip("Number of chunk rows (Z axis).")]
    [SerializeField] private int chunksZ = 4;
    [Tooltip("Mesh subdivisions per chunk for smooth heightmap displacement.")]
    [SerializeField] private int meshSubdivisionsPerChunk = 32;
    
    [Header("Displacement Settings")]
    [Tooltip("World units of vertical relief. Higher values = more visible terrain height.")]
    [SerializeField] private float displacementStrength = 5.0f;
    [SerializeField] private float flatY = 0f;
    
    [Header("Wrap Settings")]
    [SerializeField] private bool enableWrap = true;
    [Tooltip("Buffer zone before wrap triggers (fraction of column width).")]
    [SerializeField] private float wrapBuffer = 0.5f;

    [Header("Debug")]
    [Tooltip("Logs wrap teleport events and key positions. Enable only when diagnosing wrap issues.")]
    [SerializeField] private bool debugWrap = false;
    [Tooltip("Logs additional per-column state when wrapping. Can be noisy.")]
    [SerializeField] private bool debugWrapVerbose = false;
    [Tooltip("Minimum seconds between non-teleport debug logs.")]
    [SerializeField] private float debugLogCooldownSeconds = 0.5f;
    private float _lastDebugLogTime = -999f;
    private int _wrapTeleportEvents = 0;

    [Header("Diagnostics")]
    [Tooltip("Logs the full transform parent chain when building chunks (helps find unexpected rotation/offset).")]
    [SerializeField] private bool logTransformChainOnBuild = true;
    [Tooltip("Logs whenever this manager's transform changes at runtime (position/rotation/scale).")]
    [SerializeField] private bool debugTransformChanges = false;
    private Vector3 _lastTransformPos;
    private Quaternion _lastTransformRot;
    private Vector3 _lastTransformScale;
    
    [Header("Hex Grid Outline")]
    [SerializeField] private bool showHexGrid = false;
    [SerializeField] private Color hexGridColor = new Color(0.1f, 0.1f, 0.1f, 0.5f);
    [SerializeField, Range(0.01f, 0.1f)] private float hexGridWidth = 0.03f;
    [Tooltip("Number of hex tiles across the map width (adjust to match your tile count).")]
    [SerializeField] private float hexScale = 80f;
    
    [Header("Auto-Build")]
    [SerializeField] private bool preBuildOnPlanetReady = true;
    [SerializeField] private bool disableOldRenderer = true;
    
    // Chunk storage
    private HexMapChunk[,] chunks;
    private Transform[] columnParents;
    
    // Baked texture data (shared across all chunks)
    private PlanetTextureBaker.BakeResult bakeResult;
    private Material sharedMaterial;
    
    // Map dimensions
    private float mapWidth;
    private float mapHeight;
    private float columnWidth;
    
    // References
    private SphericalHexGrid grid;
    private PlanetGenerator planetGenerator;
    private Transform cameraTransform;
    private TerrainOverlayGPU terrainOverlayGPU;
    
    // Tile to chunk mapping
    private Dictionary<int, HexMapChunk> tileToChunk = new Dictionary<int, HexMapChunk>();

    // Seasonal mask sizing
    private int seasonMaskWidth;
    private int seasonMaskHeight;
    
    // Event subscriptions
    private bool _subscribedToPlanetReady;
    private bool _subscribedToSurfaceReady;
    private PlanetGenerator _surfaceEventSource;
    private bool _subscribedToSeasonChanges;
    
    // Public accessors (API compatible with FlatMapTextureRenderer)
    public SphericalHexGrid Grid => grid;
    public PlanetGenerator PlanetGenerator => planetGenerator;
    public int MeshSubdivisionsPerChunk => meshSubdivisionsPerChunk;
    public float MapWidth => mapWidth;
    public float MapHeight => mapHeight;
    public bool IsBuilt => chunks != null;
    public Texture MapTexture => bakeResult.texture;
    public int[] LUT => bakeResult.lut;
    public int LUTWidth => bakeResult.width;
    public int LUTHeight => bakeResult.height;
    public Material SharedMaterial => sharedMaterial;
    
    // Collider for WorldPicker (uses MeshCollider for proper UV support)
    private Collider pickingCollider;
    public Collider PickingCollider => pickingCollider;
    
    /// <summary>
    /// API-compatible method matching FlatMapTextureRenderer.Rebuild().
    /// </summary>
    public void Rebuild(PlanetGenerator planetGen)
    {
        BuildChunks(planetGen);
    }
    
    private void OnEnable()
    {
        TrySubscribeToPlanetReady();
        TrySubscribeToSeasonChanges();
    }
    
    private void OnDisable()
    {
        TryUnsubscribeFromPlanetReady();
        TryUnsubscribeFromSurfaceReady();
        TryUnsubscribeFromSeasonChanges();
    }
    
    private void Start()
    {
        if (cameraTransform == null)
        {
            var cam = Camera.main;
            if (cam != null) cameraTransform = cam.transform;
        }
        
        TrySubscribeToPlanetReady();

        _lastTransformPos = transform.position;
        _lastTransformRot = transform.rotation;
        _lastTransformScale = transform.lossyScale;
    }
    
    private void LateUpdate()
    {
        if (debugTransformChanges)
        {
            if (transform.position != _lastTransformPos || transform.rotation != _lastTransformRot || transform.lossyScale != _lastTransformScale)
            {
                Debug.LogWarning($"[HexMapChunkManager][TRANSFORM] Changed: path={GetTransformPath(transform)} pos={transform.position.ToString("F3")} rot={transform.rotation.eulerAngles.ToString("F1")} scale={transform.lossyScale.ToString("F3")}");
                _lastTransformPos = transform.position;
                _lastTransformRot = transform.rotation;
                _lastTransformScale = transform.lossyScale;
            }
        }

        if (enableWrap && cameraTransform != null && chunks != null)
        {
            UpdateColumnWrapping();
        }
    }
    
    #region Event Subscription (mirrors FlatMapTextureRenderer)
    
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
BuildChunks(gen);
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
        var gen = _surfaceEventSource ?? GameManager.Instance?.GetCurrentPlanetGenerator();
        TryUnsubscribeFromSurfaceReady();
        if (gen == null) return;
        
        BuildChunks(gen);
    }

    private void TrySubscribeToSeasonChanges()
    {
        if (_subscribedToSeasonChanges) return;
        ClimateManager.OnPlanetSeasonChanged += HandlePlanetSeasonChanged;
        _subscribedToSeasonChanges = true;
    }

    private void TryUnsubscribeFromSeasonChanges()
    {
        if (!_subscribedToSeasonChanges) return;
        ClimateManager.OnPlanetSeasonChanged -= HandlePlanetSeasonChanged;
        _subscribedToSeasonChanges = false;
    }
    
    #endregion
    
    #region Chunk Building
    
    /// <summary>
    /// Build all chunks for the given planet generator.
    /// Uses the same texture baking pipeline as FlatMapTextureRenderer.
    /// </summary>
    public void BuildChunks(PlanetGenerator planetGen)
    {
        if (planetGen == null || planetGen.Grid == null || planetGen.Grid.TileCount <= 0)
        {
            Debug.LogWarning("[HexMapChunkManager] Cannot build: missing planet generator or grid.");
            return;
        }
        
        // Clean up existing chunks
        DestroyAllChunks();
        
        this.planetGenerator = planetGen;
        this.grid = planetGen.Grid;
        
        // Get map dimensions from GameManager
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
        if (mapWidth <= 0.001f || mapHeight <= 0.001f)
        {
            mapWidth = Mathf.Max(mapWidth, grid.MapWidth);
            mapHeight = Mathf.Max(mapHeight, grid.MapHeight);
        }
        
        columnWidth = mapWidth / chunksX;
        
        // Bake texture using PlanetTextureBaker (same as FlatMapTextureRenderer)
        BakeTexture();

        int lutWidth = bakeResult.width > 0 ? bakeResult.width : textureWidth;
        int lutHeight = bakeResult.height > 0 ? bakeResult.height : textureHeight;
        seasonMaskWidth = Mathf.Max(1, lutWidth / chunksX);
        seasonMaskHeight = Mathf.Max(1, lutHeight / chunksZ);
        
        if (bakeResult.texture == null)
        {
            Debug.LogError("[HexMapChunkManager] Failed to bake texture!");
            return;
        }
        
        // Create shared material
        CreateSharedMaterial();

        if (logTransformChainOnBuild)
        {
            LogTransformDiagnostics();
        }
        
        // Create column parents for wrap teleportation
        CreateColumnParents();
        
        // Create chunks
        CreateChunks();
        
        // Assign tiles to chunks
        AssignTilesToChunks();

        // Initialize per-chunk season masks
        UpdateSeasonMasksForCurrentSeason();
        
        // Build all chunk meshes
        RefreshAllChunks();
        
        // Create picking collider for WorldPicker
        CreatePickingCollider();
        
        // Update WorldPicker with our LUT and collider
        UpdateWorldPicker();
        
        // Initialize terrain overlays
        InitializeTerrainOverlays();
        
        // Disable old renderer if present
        if (disableOldRenderer)
        {
            DisableOldRenderer();
        }
        
        // DIAGNOSTIC: Log heightmap and displacement settings
        Debug.LogError($"[HEIGHTMAP DIAGNOSTIC] ========================================");
        Debug.LogError($"[HEIGHTMAP DIAGNOSTIC] Heightmap Generated: {bakeResult.heightmap != null}");
        Debug.LogError($"[HEIGHTMAP DIAGNOSTIC] Displacement Strength: {displacementStrength} (Inspector value)");
        Debug.LogError($"[HEIGHTMAP DIAGNOSTIC] Shader Assigned: {flatMapDisplacementShader != null}");
        Debug.LogError($"[HEIGHTMAP DIAGNOSTIC] Material _FlatHeightScale: {(sharedMaterial != null ? sharedMaterial.GetFloat("_FlatHeightScale").ToString("F4") : "N/A")}");
        Debug.LogError($"[HEIGHTMAP DIAGNOSTIC] ========================================");
    }
    
    private void BakeTexture()
    {
        // Use GPU baking when possible (BiomeColors mode only)
        bool gpuAllowed = (textureBakerComputeShader != null) &&
                          (colorProvider == null || colorProvider.renderMode == MinimapRenderMode.BiomeColors);
        
        if (gpuAllowed)
        {
            bakeResult = PlanetTextureBaker.BakeGPU(planetGenerator, colorProvider, textureBakerComputeShader, textureWidth, textureHeight);
}
        else
        {
            bakeResult = PlanetTextureBaker.Bake(planetGenerator, colorProvider, textureWidth, textureHeight);
}
    }
    
    private void CreateSharedMaterial()
    {
        // Use same shader as FlatMapTextureRenderer
        Shader shader = flatMapDisplacementShader;
        if (shader == null)
        {
            shader = Shader.Find("Custom/FlatMapDisplacement_URP");
        }
        if (shader == null)
        {
            Debug.LogError("[HexMapChunkManager] FlatMapDisplacement_URP shader not found!");
            shader = Shader.Find("Universal Render Pipeline/Lit");
        }
        
        sharedMaterial = new Material(shader);
        sharedMaterial.name = "ChunkTerrainMaterial";
        
        // Apply baked texture
        sharedMaterial.mainTexture = bakeResult.texture;
        sharedMaterial.SetTexture("_MainTex", bakeResult.texture);
        
        // Apply heightmap
        if (bakeResult.heightmap != null)
        {
            sharedMaterial.SetTexture("_Heightmap", bakeResult.heightmap);
            sharedMaterial.SetFloat("_FlatHeightScale", displacementStrength);
            sharedMaterial.SetFloat("_MapHeight", mapHeight);
        }
        
        // Set texture wrapping for seamless wrap
        if (bakeResult.texture != null)
        {
            bakeResult.texture.wrapMode = TextureWrapMode.Repeat;
        }
        if (bakeResult.heightmap != null)
        {
            bakeResult.heightmap.wrapMode = TextureWrapMode.Repeat;
        }
        
        sharedMaterial.SetFloat("_Metallic", 0.0f);
        sharedMaterial.SetFloat("_Smoothness", 0.3f);
        
        // Apply hex grid settings
        ApplyHexGridSettings();
        
        // Create and apply LUT texture for tile highlighting
        CreateAndApplyLUTTexture();
    }
    
    /// <summary>
    /// Create a texture from the LUT array for shader-based tile highlighting.
    /// </summary>
    private Texture2D lutTexture;
    private void CreateAndApplyLUTTexture()
    {
        if (bakeResult.lut == null || bakeResult.lut.Length == 0) return;
        
        int width = bakeResult.width > 0 ? bakeResult.width : textureWidth;
        int height = bakeResult.height > 0 ? bakeResult.height : textureHeight;
        
        // Create texture to encode tile indices
        lutTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
        lutTexture.filterMode = FilterMode.Point; // No interpolation!
        lutTexture.wrapMode = TextureWrapMode.Repeat;
        lutTexture.name = "TileIndexLUT";
        
        Color[] pixels = new Color[width * height];
        for (int i = 0; i < bakeResult.lut.Length && i < pixels.Length; i++)
        {
            int tileIndex = bakeResult.lut[i];
            // Encode tile index in RGB: R + G*256 + B*65536
            float r = (tileIndex % 256) / 255f;
            float g = ((tileIndex / 256) % 256) / 255f;
            float b = ((tileIndex / 65536) % 256) / 255f;
            pixels[i] = new Color(r, g, b, 1f);
        }
        
        lutTexture.SetPixels(pixels);
        lutTexture.Apply();
        
        // Apply to material
        if (sharedMaterial != null)
        {
            sharedMaterial.SetTexture("_LUT", lutTexture);
        }
}
    
    /// <summary>
    /// Apply hex grid settings to the shared material.
    /// </summary>
    private void ApplyHexGridSettings()
    {
        if (sharedMaterial == null) return;
        
        sharedMaterial.SetFloat("_EnableHexGrid", showHexGrid ? 1f : 0f);
        sharedMaterial.SetColor("_HexGridColor", hexGridColor);
        sharedMaterial.SetFloat("_HexGridWidth", hexGridWidth);
        sharedMaterial.SetFloat("_HexScale", hexScale);
    }
    
    /// <summary>
    /// Toggle hex grid visibility at runtime.
    /// </summary>
    public void SetHexGridVisible(bool visible)
    {
        showHexGrid = visible;
        ApplyHexGridSettings();
    }
    
    /// <summary>
    /// Configure hex grid appearance at runtime.
    /// </summary>
    public void ConfigureHexGrid(bool visible, Color? color = null, float? lineWidth = null, float? scale = null)
    {
        showHexGrid = visible;
        if (color.HasValue) hexGridColor = color.Value;
        if (lineWidth.HasValue) hexGridWidth = Mathf.Clamp(lineWidth.Value, 0.01f, 0.1f);
        if (scale.HasValue) hexScale = scale.Value;
        ApplyHexGridSettings();
    }
    
    /// <summary>
    /// Auto-calculate hex scale based on the actual tile count.
    /// Call this after building to match grid lines to actual hex tiles.
    /// </summary>
    public void AutoCalculateHexScale()
    {
        if (grid != null)
        {
            // Calculate based on tiles across the map width
            // For pointy-top hexes, width = sqrt(3) * radius, so tiles across ≈ mapWidth / (sqrt(3) * hexRadius)
            // This is approximate - adjust based on your hex grid setup
            hexScale = grid.Width;
            ApplyHexGridSettings();
}
    }
    
    private void CreateColumnParents()
    {
        columnParents = new Transform[chunksX];

        // Columns are positioned across the map width in LOCAL SPACE.
        // This ensures columnParents[x].localPosition.x truly represents the column's location,
        // which makes wrapping/ghosting stable and debuggable.
        for (int x = 0; x < chunksX; x++)
        {
            GameObject columnObj = new GameObject($"Column_{x}");
            columnObj.transform.SetParent(transform, false);
            columnObj.transform.localRotation = Quaternion.identity;
            columnObj.transform.localScale = Vector3.one;

            float colLocalX = (-mapWidth * 0.5f) + (x * columnWidth);
            columnObj.transform.localPosition = new Vector3(colLocalX, flatY, 0f);
            columnParents[x] = columnObj.transform;
        }
    }
    
    private void CreateChunks()
    {
        chunks = new HexMapChunk[chunksX, chunksZ];
        
        float chunkWidth = mapWidth / chunksX;
        float chunkHeight = mapHeight / chunksZ;
        
        for (int x = 0; x < chunksX; x++)
        {
            for (int z = 0; z < chunksZ; z++)
            {
                // Calculate chunk bounds in world space
                float minX = -mapWidth * 0.5f + x * chunkWidth;
                float maxX = minX + chunkWidth;
                float minZ = -mapHeight * 0.5f + z * chunkHeight;
                float maxZ = minZ + chunkHeight;
                
                // Calculate UV region for this chunk
                float uMin = (float)x / chunksX;
                float uMax = (float)(x + 1) / chunksX;
                float vMin = (float)z / chunksZ;
                float vMax = (float)(z + 1) / chunksZ;
                
                // Create chunk
                GameObject chunkObj = new GameObject($"Chunk_{x}_{z}");
                chunkObj.transform.SetParent(columnParents[x]);
                chunkObj.transform.localPosition = new Vector3(0f, 0f, (-mapHeight * 0.5f) + (z * chunkHeight));
                chunkObj.transform.localRotation = Quaternion.identity;
                chunkObj.transform.localScale = Vector3.one;
                
                HexMapChunk chunk = chunkObj.AddComponent<HexMapChunk>();
                chunk.Initialize(this, x, z, x);

                // Bounds are in the CHUNK'S LOCAL MESH SPACE.
                // The chunk transform handles placement in the map.
                chunk.SetBounds(0f, chunkWidth, 0f, chunkHeight);
                chunk.SetUVRegion(new Vector2(uMin, vMin), new Vector2(uMax, vMax));
                chunk.SetMaterial(sharedMaterial);
                
                chunks[x, z] = chunk;
            }
        }
    }
    
    private void AssignTilesToChunks()
    {
        tileToChunk.Clear();
        
        if (grid == null) return;
        
        float chunkWidth = mapWidth / chunksX;
        float chunkHeight = mapHeight / chunksZ;
        
        // Group tiles by chunk
        var chunkTiles = new Dictionary<(int, int), List<int>>();
        
        for (int i = 0; i < grid.TileCount; i++)
        {
            Vector3 tilePos = grid.tileCenters[i];
            
            // Calculate which chunk this tile belongs to
            float normalizedX = (tilePos.x + mapWidth * 0.5f) / mapWidth;
            float normalizedZ = (tilePos.z + mapHeight * 0.5f) / mapHeight;
            
            int chunkX = Mathf.Clamp(Mathf.FloorToInt(normalizedX * chunksX), 0, chunksX - 1);
            int chunkZ = Mathf.Clamp(Mathf.FloorToInt(normalizedZ * chunksZ), 0, chunksZ - 1);
            
            var key = (chunkX, chunkZ);
            if (!chunkTiles.ContainsKey(key))
            {
                chunkTiles[key] = new List<int>();
            }
            chunkTiles[key].Add(i);
            
            tileToChunk[i] = chunks[chunkX, chunkZ];
        }
        
        // Assign to chunks
        foreach (var kvp in chunkTiles)
        {
            chunks[kvp.Key.Item1, kvp.Key.Item2].SetTileIndices(kvp.Value);
        }
    }
    
    private void InitializeTerrainOverlays()
    {
        terrainOverlayGPU = FindAnyObjectByType<TerrainOverlayGPU>();
        if (terrainOverlayGPU != null && bakeResult.lut != null)
        {
            terrainOverlayGPU.Initialize(bakeResult.lut, bakeResult.width, bakeResult.height, textureWidth, textureHeight);
            
            // Subscribe to TileSystem events
            if (TileSystem.Instance != null)
            {
                TileSystem.Instance.OnTileOwnerChanged += HandleTileOwnerChanged;
                TileSystem.Instance.OnFogChanged += HandleFogChanged;
            }
            
            // Apply overlay textures to material
            ApplyOverlayTexturesToMaterial();
        }
    }
    
    private void ApplyOverlayTexturesToMaterial()
    {
        if (sharedMaterial == null || terrainOverlayGPU == null) return;
        
        var fogMask = terrainOverlayGPU.GetFogMaskTexture();
        if (fogMask != null)
        {
            sharedMaterial.SetTexture("_FogMask", fogMask);
            sharedMaterial.SetFloat("_EnableFog", terrainOverlayGPU.EnableFogOverlay ? 1f : 0f);
        }
        
        var ownershipTex = terrainOverlayGPU.GetOwnershipTexture();
        if (ownershipTex != null)
        {
            sharedMaterial.SetTexture("_OwnershipOverlay", ownershipTex);
            sharedMaterial.SetFloat("_EnableOwnership", terrainOverlayGPU.EnableOwnershipOverlay ? 1f : 0f);
        }
    }
    
    private void HandleTileOwnerChanged(int tile, int oldOwner, int newOwner)
    {
        if (terrainOverlayGPU != null)
        {
            terrainOverlayGPU.MarkTilesDirty(new[] { tile });
            terrainOverlayGPU.UpdateOverlays();
        }
    }
    
    private void HandleFogChanged(int civId, List<int> changedTiles)
    {
        if (terrainOverlayGPU != null)
        {
            terrainOverlayGPU.MarkTilesDirty(changedTiles);
            terrainOverlayGPU.UpdateOverlays();
        }
    }
    
    private void DisableOldRenderer()
    {
        // FlatMapTextureRenderer removed - this is now a no-op
        // HexMapChunkManager is the sole map renderer
    }
    
    /// <summary>
    /// Create a MeshCollider covering the entire map for WorldPicker raycasts with proper UV support.
    /// </summary>
    private void CreatePickingCollider()
    {
        // Destroy old collider if exists
        if (pickingCollider != null)
        {
            DestroyImmediate(pickingCollider.gameObject);
        }
        
        // Create a dedicated GameObject for the picking collider
        GameObject colliderObj = new GameObject("ChunkMapCollider");
        colliderObj.transform.SetParent(transform);
        colliderObj.transform.localPosition = new Vector3(0f, flatY, 0f);
        colliderObj.transform.localRotation = Quaternion.identity; // No rotation - mesh is on XZ plane
        
        // Create simple quad mesh with proper UVs on XZ plane (Y=0)
        // This ensures hit.textureCoord works correctly for raycasting from above
        Mesh quadMesh = new Mesh();
        quadMesh.name = "PickingQuad";
        
        float halfW = mapWidth * 0.5f;
        float halfH = mapHeight * 0.5f;
        
        // Vertices on XZ plane (Y=0), facing up
        quadMesh.vertices = new Vector3[]
        {
            new Vector3(-halfW, 0f, -halfH), // bottom-left
            new Vector3(halfW, 0f, -halfH),  // bottom-right
            new Vector3(-halfW, 0f, halfH),  // top-left
            new Vector3(halfW, 0f, halfH)    // top-right
        };
        // UVs match the vertex layout
        quadMesh.uv = new Vector2[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f)
        };
        // Triangles wound counter-clockwise when viewed from above (Y+)
        quadMesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
        quadMesh.RecalculateNormals();
        
        // Add mesh filter (required for MeshCollider UV support)
        MeshFilter mf = colliderObj.AddComponent<MeshFilter>();
        mf.mesh = quadMesh;
        
        // Add invisible renderer (required for hit.textureCoord to work)
        MeshRenderer mr = colliderObj.AddComponent<MeshRenderer>();
        mr.enabled = false;
        
        // Use MeshCollider for proper UV support (hit.textureCoord)
        var meshCollider = colliderObj.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = quadMesh;
        pickingCollider = meshCollider;
        
        // Set layer for raycasting
        int terrainLayer = LayerMask.NameToLayer("Terrain");
        colliderObj.layer = terrainLayer >= 0 ? terrainLayer : 0;
}
    
    /// <summary>
    /// Update WorldPicker with our LUT and collider.
    /// </summary>
    private void UpdateWorldPicker()
    {
        var worldPicker = FindAnyObjectByType<WorldPicker>();
        if (worldPicker != null && bakeResult.lut != null)
        {
            worldPicker.lut = bakeResult.lut;
            worldPicker.lutWidth = bakeResult.width > 0 ? bakeResult.width : textureWidth;
            worldPicker.lutHeight = bakeResult.height > 0 ? bakeResult.height : textureHeight;
            worldPicker.flatMapCollider = pickingCollider;
            worldPicker.mapWidth = mapWidth;
            worldPicker.mapHeight = mapHeight;
            Debug.Log($"[HexMapChunkManager] Updated WorldPicker: LUT={bakeResult.lut.Length}, collider={(pickingCollider != null ? "assigned" : "null")}, mapSize={mapWidth}x{mapHeight}");
        }
        else
        {
            Debug.LogWarning($"[HexMapChunkManager] Could not update WorldPicker: picker={(worldPicker != null ? "found" : "null")}, lut={(bakeResult.lut != null ? "exists" : "null")}");
        }
    }
    
    #endregion
    
    #region Utility Methods (API compatible with FlatMapTextureRenderer)
    
    /// <summary>
    /// Get world position from UV coordinates.
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
    /// Get a downscaled version of the map texture for minimap use (GPU-accelerated).
    /// </summary>
    public Texture GetDownscaledTexture(int targetWidth, int targetHeight, bool returnTexture2D = false)
    {
        if (bakeResult.texture == null)
            return null;
        
        // GPU-accelerated downscaling using Graphics.Blit
        RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
        rt.filterMode = FilterMode.Bilinear;
        rt.wrapMode = TextureWrapMode.Repeat;
        Graphics.Blit(bakeResult.texture, rt);
        
        if (!returnTexture2D)
            return rt;
        
        // Convert to Texture2D if explicitly requested
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        
        Texture2D downscaled = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
        downscaled.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
        downscaled.Apply();
        
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        
        downscaled.wrapMode = TextureWrapMode.Repeat;
        downscaled.filterMode = FilterMode.Bilinear;
        
        return downscaled;
    }
    
    /// <summary>
    /// Get the bake result for external systems.
    /// </summary>
    public PlanetTextureBaker.BakeResult GetBakeResult()
    {
        return bakeResult;
    }
    
    #endregion
    
    #region Column Wrapping
    
    // Ghost columns for seamless edge rendering
    private Transform[] ghostColumnsLeft;
    private Transform[] ghostColumnsRight;
    private int[] ghostColumnsLeftSourceIndices;
    private int[] ghostColumnsRightSourceIndices;
    private bool ghostColumnsCreated = false;
    
    /// <summary>
    /// Create ghost columns that mirror the edges for seamless wrapping.
    /// This ensures there's always visible terrain at the map edges.
    /// </summary>
    private void CreateGhostColumns()
    {
        if (ghostColumnsCreated || chunks == null || columnParents == null) return;
        
        // Calculate how many columns we need to duplicate based on camera view
        // We'll duplicate enough columns to cover the maximum view distance
        int columnsToMirror = Mathf.Max(2, Mathf.CeilToInt(chunksX * 0.25f)); // Mirror 25% of columns on each side
        
        ghostColumnsLeft = new Transform[columnsToMirror];
        ghostColumnsRight = new Transform[columnsToMirror];
        ghostColumnsLeftSourceIndices = new int[columnsToMirror];
        ghostColumnsRightSourceIndices = new int[columnsToMirror];
        
        for (int i = 0; i < columnsToMirror; i++)
        {
            // Left ghost: mirror rightmost columns, place them to the left
            int sourceColRight = chunksX - 1 - i;
            ghostColumnsLeft[i] = CreateGhostColumn(sourceColRight, -mapWidth, $"GhostLeft_{i}");
            ghostColumnsLeftSourceIndices[i] = sourceColRight;
            
            // Right ghost: mirror leftmost columns, place them to the right
            int sourceColLeft = i;
            ghostColumnsRight[i] = CreateGhostColumn(sourceColLeft, mapWidth, $"GhostRight_{i}");
            ghostColumnsRightSourceIndices[i] = sourceColLeft;
        }
        
        ghostColumnsCreated = true;

        UpdateGhostSeasonMasks();

        if (debugWrap)
        {
            Debug.Log($"[HexMapChunkManager][WRAP] Created ghost columns: mirror={columnsToMirror}, mapWidth={mapWidth:F3}, chunksX={chunksX}, columnWidth={columnWidth:F3}");
        }
}
    
    private Transform CreateGhostColumn(int sourceColumnIndex, float xOffset, string name)
    {
        GameObject ghostObj = new GameObject(name);
        ghostObj.transform.SetParent(transform, false);
        ghostObj.transform.localRotation = Quaternion.identity;
        ghostObj.transform.localScale = Vector3.one;

        // Position the entire ghost column relative to its source column.
        // Use LOCAL SPACE so it remains correct even if the map hierarchy is rotated.
        if (columnParents != null && sourceColumnIndex >= 0 && sourceColumnIndex < columnParents.Length)
        {
            ghostObj.transform.localPosition = columnParents[sourceColumnIndex].localPosition + new Vector3(xOffset, 0f, 0f);
        }
        
        // Copy all chunks from source column
        for (int z = 0; z < chunksZ; z++)
        {
            HexMapChunk sourceChunk = chunks[sourceColumnIndex, z];
            if (sourceChunk == null) continue;
            
            // Create ghost chunk as simple mesh copy
            GameObject ghostChunk = new GameObject($"{name}_Chunk_{z}");
            ghostChunk.transform.SetParent(ghostObj.transform, false);
            
            // Copy mesh filter
            MeshFilter sourceMF = sourceChunk.GetComponent<MeshFilter>();
            if (sourceMF != null && sourceMF.sharedMesh != null)
            {
                MeshFilter ghostMF = ghostChunk.AddComponent<MeshFilter>();
                ghostMF.sharedMesh = sourceMF.sharedMesh;
            }
            
            // Copy mesh renderer with shared material
            MeshRenderer sourceMR = sourceChunk.GetComponent<MeshRenderer>();
            if (sourceMR != null)
            {
                MeshRenderer ghostMR = ghostChunk.AddComponent<MeshRenderer>();
                ghostMR.sharedMaterial = sharedMaterial;
                ghostMR.shadowCastingMode = sourceMR.shadowCastingMode;
                ghostMR.receiveShadows = sourceMR.receiveShadows;
            }

            // Match the source chunk's LOCAL offset within the column (typically Z placement)
            ghostChunk.transform.localPosition = sourceChunk.transform.localPosition;
            ghostChunk.transform.localRotation = Quaternion.identity;
            ghostChunk.transform.localScale = Vector3.one;
            ghostChunk.layer = sourceChunk.gameObject.layer;
        }

        if (debugWrapVerbose)
        {
            Debug.Log($"[HexMapChunkManager][WRAP] Created ghost column '{name}' from sourceCol={sourceColumnIndex} xOffset={xOffset:F3} ghostPos={ghostObj.transform.position}");
        }
        
        return ghostObj.transform;
    }
    
    /// <summary>
    /// Update ghost column positions to always stay at the edges relative to camera.
    /// </summary>
    private void UpdateGhostColumns()
    {
        if (!ghostColumnsCreated || ghostColumnsLeft == null || ghostColumnsRight == null) return;
        
        // Ghost columns track the main column positions
        for (int i = 0; i < ghostColumnsLeft.Length; i++)
        {
            int sourceColRight = chunksX - 1 - i;
            if (sourceColRight >= 0 && sourceColRight < columnParents.Length)
            {
                // Position ghost left columns relative to their source
                Vector3 sourceLocal = columnParents[sourceColRight].localPosition;
                ghostColumnsLeft[i].localPosition = sourceLocal + new Vector3(-mapWidth, 0f, 0f);
            }
        }
        
        for (int i = 0; i < ghostColumnsRight.Length; i++)
        {
            int sourceColLeft = i;
            if (sourceColLeft >= 0 && sourceColLeft < columnParents.Length)
            {
                // Position ghost right columns relative to their source
                Vector3 sourceLocal = columnParents[sourceColLeft].localPosition;
                ghostColumnsRight[i].localPosition = sourceLocal + new Vector3(mapWidth, 0f, 0f);
            }
        }

        if (debugWrapVerbose && Time.unscaledTime - _lastDebugLogTime >= debugLogCooldownSeconds)
        {
            _lastDebugLogTime = Time.unscaledTime;
            string left0 = ghostColumnsLeft.Length > 0 && ghostColumnsLeft[0] != null ? ghostColumnsLeft[0].position.ToString("F3") : "(none)";
            string right0 = ghostColumnsRight.Length > 0 && ghostColumnsRight[0] != null ? ghostColumnsRight[0].position.ToString("F3") : "(none)";
            Debug.Log($"[HexMapChunkManager][WRAP] Ghost update: left0={left0}, right0={right0}");
        }
    }
    
    /// <summary>
    /// Update column positions for seamless world wrapping.
    /// Teleports columns when camera crosses threshold.
    /// </summary>
    private void UpdateColumnWrapping()
    {
        if (columnParents == null || cameraTransform == null) return;
        
        // Create ghost columns on first update if not yet created
        if (!ghostColumnsCreated)
        {
            CreateGhostColumns();
        }
        
        // Work in MAP-LOCAL space for stability even if the map is rotated/offset in the scene.
        float cameraX = transform.InverseTransformPoint(cameraTransform.position).x;
        float halfMap = mapWidth * 0.5f;
        float leftEdge = cameraX - halfMap;
        float rightEdge = cameraX + halfMap;
        float buffer = columnWidth * wrapBuffer;

        int teleportsThisFrame = 0;
        
        for (int i = 0; i < columnParents.Length; i++)
        {
            Transform col = columnParents[i];
            float colX = col.localPosition.x;
            
            // Column is too far left - teleport to right
            if (colX < leftEdge - buffer)
            {
                float oldX = colX;
                float newX = colX + mapWidth;
                col.localPosition = new Vector3(newX, col.localPosition.y, col.localPosition.z);
                teleportsThisFrame++;
                _wrapTeleportEvents++;
                if (debugWrap)
                {
                    Debug.Log($"[HexMapChunkManager][WRAP] Teleport col[{i}] LEFT->RIGHT oldX={oldX:F3} newX={newX:F3} camX={cameraX:F3} leftEdge={leftEdge:F3} rightEdge={rightEdge:F3} buffer={buffer:F3} mapW={mapWidth:F3} events={_wrapTeleportEvents}");
                }
            }
            // Column is too far right - teleport to left
            else if (colX > rightEdge + buffer)
            {
                float oldX = colX;
                float newX = colX - mapWidth;
                col.localPosition = new Vector3(newX, col.localPosition.y, col.localPosition.z);
                teleportsThisFrame++;
                _wrapTeleportEvents++;
                if (debugWrap)
                {
                    Debug.Log($"[HexMapChunkManager][WRAP] Teleport col[{i}] RIGHT->LEFT oldX={oldX:F3} newX={newX:F3} camX={cameraX:F3} leftEdge={leftEdge:F3} rightEdge={rightEdge:F3} buffer={buffer:F3} mapW={mapWidth:F3} events={_wrapTeleportEvents}");
                }
            }

            if (debugWrapVerbose && Time.unscaledTime - _lastDebugLogTime >= debugLogCooldownSeconds)
            {
                _lastDebugLogTime = Time.unscaledTime;
                Debug.Log($"[HexMapChunkManager][WRAP] State col[{i}] x={col.localPosition.x:F3} camX={cameraX:F3} edges=[{leftEdge:F3},{rightEdge:F3}] buffer={buffer:F3} mapW={mapWidth:F3} mgrPos={transform.position.ToString("F3")} mgrRot={transform.rotation.eulerAngles.ToString("F1")}");
            }
        }

        if (debugWrap && teleportsThisFrame > 0)
        {
            Debug.Log($"[HexMapChunkManager][WRAP] Teleports this frame={teleportsThisFrame} camX={cameraX:F3} mapW={mapWidth:F3}");
        }
        
        // Update ghost columns to match
        UpdateGhostColumns();
    }

    private void LogTransformDiagnostics()
    {
        var t = transform;
        Debug.LogWarning($"[HexMapChunkManager][TRANSFORM] BuildChunks: selfPath={GetTransformPath(t)} localPos={t.localPosition.ToString("F3")} localRot={t.localRotation.eulerAngles.ToString("F1")} localScale={t.localScale.ToString("F3")} worldPos={t.position.ToString("F3")} worldRot={t.rotation.eulerAngles.ToString("F1")} worldScale={t.lossyScale.ToString("F3")}");

        Transform p = t.parent;
        int depth = 0;
        while (p != null && depth < 12)
        {
            Debug.LogWarning($"[HexMapChunkManager][TRANSFORM] Parent[{depth}]: path={GetTransformPath(p)} localPos={p.localPosition.ToString("F3")} localRot={p.localRotation.eulerAngles.ToString("F1")} localScale={p.localScale.ToString("F3")} worldPos={p.position.ToString("F3")} worldRot={p.rotation.eulerAngles.ToString("F1")} worldScale={p.lossyScale.ToString("F3")}");
            p = p.parent;
            depth++;
        }
    }

    private static string GetTransformPath(Transform t)
    {
        if (t == null) return "(null)";
        var names = new List<string>(16);
        Transform cur = t;
        int guard = 0;
        while (cur != null && guard++ < 64)
        {
            names.Add(cur.name);
            cur = cur.parent;
        }
        names.Reverse();
        return string.Join("/", names);
    }
    
    /// <summary>
    /// Destroy ghost columns during cleanup.
    /// </summary>
    private void DestroyGhostColumns()
    {
        if (ghostColumnsLeft != null)
        {
            foreach (var col in ghostColumnsLeft)
            {
                if (col != null) DestroyImmediate(col.gameObject);
            }
            ghostColumnsLeft = null;
        }
        
        if (ghostColumnsRight != null)
        {
            foreach (var col in ghostColumnsRight)
            {
                if (col != null) DestroyImmediate(col.gameObject);
            }
            ghostColumnsRight = null;
        }
        
        ghostColumnsCreated = false;
    }

    private void UpdateGhostSeasonMasks()
    {
        if (!ghostColumnsCreated || chunks == null) return;

        if (ghostColumnsLeft != null)
        {
            for (int i = 0; i < ghostColumnsLeft.Length; i++)
            {
                int sourceCol = ghostColumnsLeftSourceIndices != null && i < ghostColumnsLeftSourceIndices.Length
                    ? ghostColumnsLeftSourceIndices[i]
                    : -1;
                CopySeasonMaskToGhostColumn(ghostColumnsLeft[i], sourceCol);
            }
        }

        if (ghostColumnsRight != null)
        {
            for (int i = 0; i < ghostColumnsRight.Length; i++)
            {
                int sourceCol = ghostColumnsRightSourceIndices != null && i < ghostColumnsRightSourceIndices.Length
                    ? ghostColumnsRightSourceIndices[i]
                    : -1;
                CopySeasonMaskToGhostColumn(ghostColumnsRight[i], sourceCol);
            }
        }
    }

    private void CopySeasonMaskToGhostColumn(Transform ghostColumn, int sourceColumnIndex)
    {
        if (ghostColumn == null || sourceColumnIndex < 0 || sourceColumnIndex >= chunksX) return;

        for (int z = 0; z < chunksZ; z++)
        {
            var sourceChunk = chunks[sourceColumnIndex, z];
            if (sourceChunk == null) continue;

            var sourceRenderer = sourceChunk.GetComponent<MeshRenderer>();
            if (sourceRenderer == null) continue;

            if (z >= ghostColumn.childCount) continue;
            var ghostChunk = ghostColumn.GetChild(z);
            var ghostRenderer = ghostChunk.GetComponent<MeshRenderer>();
            if (ghostRenderer == null) continue;

            var block = new MaterialPropertyBlock();
            sourceRenderer.GetPropertyBlock(block);
            ghostRenderer.SetPropertyBlock(block);
        }
    }
    
    #endregion
    
    #region Public API
    
    /// <summary>
    /// Refresh all chunks that have been marked dirty.
    /// </summary>
    public void RefreshDirtyChunks()
    {
        if (chunks == null) return;
        
        for (int x = 0; x < chunksX; x++)
        {
            for (int z = 0; z < chunksZ; z++)
            {
                if (chunks[x, z] != null && chunks[x, z].IsDirty)
                {
                    chunks[x, z].Refresh();
                }
            }
        }
    }
    
    /// <summary>
    /// Force refresh all chunks immediately.
    /// </summary>
    public void RefreshAllChunks()
    {
        if (chunks == null) return;
        
        for (int x = 0; x < chunksX; x++)
        {
            for (int z = 0; z < chunksZ; z++)
            {
                if (chunks[x, z] != null)
                {
                    chunks[x, z].ForceRefresh();
                }
            }
        }
    }

    private void HandlePlanetSeasonChanged(int planetIndex, Season season)
    {
        if (planetGenerator == null || planetGenerator.planetIndex != planetIndex) return;
        UpdateSeasonMasksForSeason(season);
    }

    private void UpdateSeasonMasksForCurrentSeason()
    {
        if (planetGenerator == null) return;
        var climateManager = GameManager.Instance != null
            ? GameManager.Instance.GetClimateManager(planetGenerator.planetIndex)
            : ClimateManager.Instance;
        if (climateManager == null) return;

        UpdateSeasonMasksForSeason(climateManager.GetSeasonForPlanet(planetGenerator.planetIndex));
    }

    private void UpdateSeasonMasksForSeason(Season season)
    {
        if (planetGenerator == null || chunks == null || bakeResult.lut == null) return;
        if (seasonMaskWidth <= 0 || seasonMaskHeight <= 0) return;

        int lutWidth = bakeResult.width > 0 ? bakeResult.width : textureWidth;
        int lutHeight = bakeResult.height > 0 ? bakeResult.height : textureHeight;

        var climateManager = GameManager.Instance != null
            ? GameManager.Instance.GetClimateManager(planetGenerator.planetIndex)
            : ClimateManager.Instance;
        if (climateManager == null) return;

        for (int x = 0; x < chunksX; x++)
        {
            for (int z = 0; z < chunksZ; z++)
            {
                var chunk = chunks[x, z];
                if (chunk == null) continue;

                chunk.UpdateSeasonMask(
                    lutWidth,
                    lutHeight,
                    seasonMaskWidth,
                    seasonMaskHeight,
                    bakeResult.lut,
                    planetGenerator,
                    climateManager,
                    season);
            }
        }

        UpdateGhostSeasonMasks();
    }
    
    /// <summary>
    /// Mark a specific tile as changed and refresh its chunk.
    /// Call this when tile data changes (biome, elevation, etc.)
    /// </summary>
    public void MarkTileDirty(int tileIndex)
    {
        if (tileToChunk.TryGetValue(tileIndex, out HexMapChunk chunk))
        {
            chunk.MarkTileDirty(tileIndex);
            // Also update the chunk's season mask so seasonal visuals stay in sync
            try
            {
                if (planetGenerator != null && bakeResult.lut != null && seasonMaskWidth > 0 && seasonMaskHeight > 0)
                {
                    var climateManager = GameManager.Instance != null
                        ? GameManager.Instance.GetClimateManager(planetGenerator.planetIndex)
                        : ClimateManager.Instance;
                    if (climateManager != null)
                    {
                        Season s = climateManager.GetSeasonForPlanet(planetGenerator.planetIndex);
                        int lutWidth = bakeResult.width > 0 ? bakeResult.width : textureWidth;
                        int lutHeight = bakeResult.height > 0 ? bakeResult.height : textureHeight;
                        chunk.UpdateSeasonMask(lutWidth, lutHeight, seasonMaskWidth, seasonMaskHeight, bakeResult.lut, planetGenerator, climateManager, s);
                        UpdateGhostSeasonMasks();
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[HexMapChunkManager] Failed to update season mask for chunk after tile dirty: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// Mark multiple tiles as changed.
    /// </summary>
    public void MarkTilesDirty(IEnumerable<int> tileIndices)
    {
        HashSet<HexMapChunk> affectedChunks = new HashSet<HexMapChunk>();
        
        foreach (int idx in tileIndices)
        {
            if (tileToChunk.TryGetValue(idx, out HexMapChunk chunk))
            {
                affectedChunks.Add(chunk);
            }
        }
        
        foreach (var chunk in affectedChunks)
        {
            chunk.MarkDirty();
        }

        // Update season masks for affected chunks so seasonal overlays reflect tile changes
        try
        {
            if (planetGenerator != null && bakeResult.lut != null && seasonMaskWidth > 0 && seasonMaskHeight > 0)
            {
                var climateManager = GameManager.Instance != null
                    ? GameManager.Instance.GetClimateManager(planetGenerator.planetIndex)
                    : ClimateManager.Instance;
                if (climateManager != null)
                {
                    Season s = climateManager.GetSeasonForPlanet(planetGenerator.planetIndex);
                    int lutWidth = bakeResult.width > 0 ? bakeResult.width : textureWidth;
                    int lutHeight = bakeResult.height > 0 ? bakeResult.height : textureHeight;
                    foreach (var chunk in affectedChunks)
                    {
                        if (chunk == null) continue;
                        chunk.UpdateSeasonMask(lutWidth, lutHeight, seasonMaskWidth, seasonMaskHeight, bakeResult.lut, planetGenerator, climateManager, s);
                    }
                    UpdateGhostSeasonMasks();
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[HexMapChunkManager] Failed to update season masks for affected chunks: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Rebuild the baked texture (e.g., after terrain changes).
    /// </summary>
    public void RebakeTexture()
    {
        if (planetGenerator == null) return;
        
        BakeTexture();
        
        if (sharedMaterial != null && bakeResult.texture != null)
        {
            sharedMaterial.SetTexture("_MainTex", bakeResult.texture);
            if (bakeResult.heightmap != null)
            {
                sharedMaterial.SetTexture("_Heightmap", bakeResult.heightmap);
            }
        }
    }
    
    /// <summary>
    /// Get the chunk containing a specific tile.
    /// </summary>
    public HexMapChunk GetChunkForTile(int tileIndex)
    {
        tileToChunk.TryGetValue(tileIndex, out HexMapChunk chunk);
        return chunk;
    }
    
    /// <summary>
    /// Clean up all chunks.
    /// </summary>
    public void DestroyAllChunks()
    {
        // Destroy ghost columns first
        DestroyGhostColumns();
        
        if (chunks != null)
        {
            for (int x = 0; x < chunks.GetLength(0); x++)
            {
                for (int z = 0; z < chunks.GetLength(1); z++)
                {
                    if (chunks[x, z] != null)
                    {
                        DestroyImmediate(chunks[x, z].gameObject);
                    }
                }
            }
            chunks = null;
        }
        
        if (columnParents != null)
        {
            foreach (var col in columnParents)
            {
                if (col != null) DestroyImmediate(col.gameObject);
            }
            columnParents = null;
        }
        
        if (pickingCollider != null)
        {
            DestroyImmediate(pickingCollider.gameObject);
            pickingCollider = null;
        }
        
        if (sharedMaterial != null)
        {
            DestroyImmediate(sharedMaterial);
            sharedMaterial = null;
        }
        
        tileToChunk.Clear();
    }
    
    #endregion
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        TryUnsubscribeFromPlanetReady();
        TryUnsubscribeFromSurfaceReady();
        
        if (TileSystem.Instance != null)
        {
            TileSystem.Instance.OnTileOwnerChanged -= HandleTileOwnerChanged;
            TileSystem.Instance.OnFogChanged -= HandleFogChanged;
        }
        
        DestroyAllChunks();
    }
    
#if UNITY_EDITOR
    [ContextMenu("Force Rebuild Chunks")]
    private void ForceRebuild()
    {
        var gen = GameManager.Instance?.GetCurrentPlanetGenerator();
        if (gen != null)
        {
            BuildChunks(gen);
        }
    }
#endif
}
