using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Manages all hex map chunks and handles seamless world wrapping.
/// Replaces FlatMapTextureRenderer with a chunk-based approach that enables:
/// - Seamless horizontal wrap via column teleportation (Civ 5 style)
/// - Per-chunk dirty marking for dynamic tile updates
/// - Same visual quality using PlanetTextureBaker for minimap and HDRP terrain shading
/// 
/// This integrates with the existing pipeline:
/// - Uses PlanetTextureBaker.Bake() or BakeGPU() for minimap textures
/// - Uses BiomeVisualDatabase for terrain textures
/// - Uses HDRP terrain shader for heightmap displacement and overlays
/// - Subscribes to OnSurfaceGenerated to wait for proper map generation
/// </summary>
public class HexMapChunkManager : MonoBehaviour
{
    [Header("References")]
    // Minimap/flat-map coloring is fixed to default biome colors (BiomeColorHelper).
    // Keep visuals deterministic and avoid multiple competing "color provider" assets.
    [SerializeField] private ComputeShader textureBakerComputeShader;
    [SerializeField]
    [Tooltip("Terrain shader used to render biome chunks (assign exactly one). Must support the runtime-bound properties: _BiomeIndexMap, _Heightmap, _BiomeAlbedoArray, _BiomeNormalArray, _BiomeMaskArray, _BiomeCount.")]
    private Shader terrainShader;
    [SerializeField] private BiomeVisualDatabase biomeVisualDatabase;
    
    [Header("Texture Settings")]
    [Tooltip("Width of biome texture arrays (used for shader arrays and baking).")]
    [SerializeField] private int textureWidth = 2048;
    [Tooltip("Height of biome texture arrays (used for shader arrays and baking). Use 2048 for 2048x2048 RGBA32 arrays.")]
    [SerializeField] private int textureHeight = 2048;
    
    [Header("Chunk Settings")]
    [Tooltip("Number of chunk columns (X axis). More columns = finer wrap granularity.")]
    [SerializeField] private int chunksX = 8;
    [Tooltip("Number of chunk rows (Z axis).")]
    [SerializeField] private int chunksZ = 4;
    [Tooltip("Mesh subdivisions per chunk for smooth heightmap displacement.")]
    [SerializeField] private int meshSubdivisionsPerChunk = 32;
    
    [Header("Displacement Settings")]
    [Tooltip("Multiplier for terrain elevation. With world-space elevation, 1.0 means elevation values are used directly. Values >1 exaggerate terrain height for artistic effect.")]
    [Range(0.1f, 10f)]
    [SerializeField] private float displacementStrength = 1.0f;
    [SerializeField] private float flatY = 0f;

    [Header("Biome Visual Modifiers")]
    [Range(0f, 1f)]
    [SerializeField] private float globalSnowAmount = 0f;
    [Range(0f, 1f)]
    [SerializeField] private float globalWetness = 0f;
    
    [Header("Triplanar Settings")]
    [Tooltip("Triplanar tiling scale — controls how large biome textures appear on terrain. Lower = larger textures.")]
    [Range(0.01f, 5f)]
    [SerializeField] private float triplanarTiling = 2f;
    [Tooltip("Triplanar blend sharpness — higher values make the blend between projection axes sharper. Lower = smoother.")]
    [Range(1f, 20f)]
    [SerializeField] private float triplanarBlend = 6f;

    [Header("Biome Blending (Shader)")]
    [Tooltip("Biome transition sampling radius in texels of the biome index map. Higher = smoother/softer biome edges, but more texture reads.")]
    [Range(0f, 16f)]
    [SerializeField] private float biomeBlendRadius = 4f;
    [Tooltip("Height-based blend sharpness when transitioning between biomes. Higher = crisper transitions, lower = smoother/muddier blends.")]
    [Range(0.01f, 10f)]
    [SerializeField] private float biomeBlendSharpness = 3f;

    [Header("Cliff Overlay (Shader)")]
    [Tooltip("Albedo texture for cliff/rock surfaces on steep slopes. Assign the Mountain texture here.")]
    [SerializeField] private Texture2D cliffAlbedoTexture;
    [Tooltip("Normal map for cliff surfaces (optional). Leave null for flat normals.")]
    [SerializeField] private Texture2D cliffNormalTexture;
    [Tooltip("Triplanar tiling scale for the cliff texture.")]
    [Range(0.1f, 20f)]
    [SerializeField] private float cliffTiling = 2.0f;
    [Tooltip("Slope angle (in terms of normal.y) where cliff begins appearing. 1 = flat, 0 = vertical. Lower = steeper before cliff starts.")]
    [Range(0f, 1f)]
    [SerializeField] private float cliffSlopeStart = 0.6f;
    [Tooltip("Slope angle where cliff is fully visible. Must be less than Slope Start.")]
    [Range(0f, 1f)]
    [SerializeField] private float cliffSlopeEnd = 0.3f;
    [Tooltip("PBR smoothness for cliff surfaces.")]
    [Range(0f, 1f)]
    [SerializeField] private float cliffSmoothness = 0.3f;
    [Tooltip("PBR metallic for cliff surfaces.")]
    [Range(0f, 1f)]
    [SerializeField] private float cliffMetallic = 0.0f;

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
    
    // NOTE: Hex grid overlay was removed - shader graph doesn't support it.
    // To add hex grid, create a separate HexGridOverlay script using line renderers or decals.
    
    [Header("Water Mesh System")]
    [Tooltip("Material for chunk-based water tiles (lakes, ocean, rivers). Assign SG_WaterTile material.")]
    [SerializeField] private Material waterMaterial;
    [Tooltip("Material for foam edge strips along water-to-land boundaries. Assign SG_FoamEdge material.")]
    [SerializeField] private Material foamMaterial;
    [Tooltip("Small Y offset above the computed water surface to prevent z-fighting with terrain.")]
    [SerializeField] private float waterYOffset = 0.01f;
    [Tooltip("Manual world-space Y position for ocean water surface. Set this to sit just below your coastline terrain. Overrides the computed SeaLevelWorldY.")]
    [SerializeField] private float manualOceanWaterY = 4.5f;
    [Tooltip("When true, use manualOceanWaterY for ocean water height instead of PlanetGenerator.SeaLevelWorldY.")]
    [SerializeField] private bool useManualOceanWaterY = true;

    [Header("Water Volume Columns (Minecraft-like)")]
    [Tooltip("When enabled, chunk water meshes include vertical side walls so water occupies visible 3D volume (like Minecraft columns).")]
    [SerializeField] private bool enableWaterVolumeColumns = true;
    [Tooltip("How far downward (world units) to extend water walls when bordering land (or missing neighbor).")]
    [SerializeField] private float waterVolumeDepth = 10f;
    [Tooltip("When false, only inland water (rivers/lakes) gets volume walls. Ocean remains a surface only (cheaper).")]
    [SerializeField] private bool waterVolumeIncludeOcean = false;
    [Tooltip("Minimum water height difference before we build a step wall between two water tiles.")]
    [SerializeField] private float waterVolumeStepEpsilon = 0.02f;

    [Header("Continuous River Surface (SDF / Marching Squares)")]
    [Tooltip("When enabled, rivers are rendered as a continuous surface mesh (one mesh) built from an SDF and marching squares.\nThis disables per-tile river fans in the chunk water mesh to avoid double-rendering.")]
    [SerializeField] private bool enableContinuousRiverSurface = true;
    [Tooltip("When enabled, lakes are included in the same SDF/marching-squares surface so rivers can flow seamlessly into lakes.\nThis disables per-tile LAKE fans in the chunk water mesh to avoid double-rendering.")]
    [SerializeField] private bool continuousWaterIncludesLakes = true;
    [Tooltip("Resolution of the SDF field (higher = smoother rivers, more CPU time).")]
    [SerializeField] private int riverSdfWidth = 512;
    [Tooltip("Resolution of the SDF field (higher = smoother rivers, more CPU time).")]
    [SerializeField] private int riverSdfHeight = 256;
    [Tooltip("River half-width multiplier relative to hex size (computed from map).")]
    [SerializeField] private float riverHalfWidthMultiplier = 0.55f;
    [Tooltip("Lake half-width multiplier relative to hex size (computed from map). Usually larger than rivers.")]
    [SerializeField] private float lakeHalfWidthMultiplier = 1.25f;
    [Tooltip("Extra Y lift above sampled terrain height to avoid z-fighting.")]
    [SerializeField] private float riverSurfaceLift = 0.02f;

    [Header("Inland Water Volume (3D Fill)")]
    [Tooltip("When enabled, the continuous inland water surface is extruded downward into a closed 3D mesh (top + walls + bottom) so rivers/lakes look filled in 3D space.")]
    [SerializeField] private bool extrudeInlandWaterToVolume = true;
    [Tooltip("How far downward (world units) to extrude the inland water mesh to create a filled volume.")]
    [SerializeField] private float inlandWaterVolumeDepth = 12f;

    [Header("Cliff Walls (Mesh)")]
    [Tooltip("When enabled, builds vertical wall quads along edges where neighboring tiles have a large elevation step (Minecraft-like cliffs).\n\nNOTE: Cliff walls are built on CPU using tile elevation data, but terrain is displaced by the GPU shader via heightmap. This can cause cliffs to appear misaligned with the actual terrain surface. Consider disabling until a shader-aware cliff system is implemented.")]
    [SerializeField] private bool enableCliffWalls = false;
    [Tooltip("Material used for cliff wall meshes. If null, cliffs will not be rendered.")]
    [SerializeField] private Material cliffWallMaterial;
    [Tooltip("Minimum height difference (world units after displacementStrength) required to create a cliff wall.")]
    [SerializeField] private float cliffMinHeightDelta = 1.25f;
    [Tooltip("Extra amount to extend the cliff wall downward beyond the lower tile height to hide cracks.")]
    [SerializeField] private float cliffBottomExtension = 0.25f;
    [Tooltip("Small horizontal inset (world units) toward the higher tile to reduce z-fighting at the seam.")]
    [SerializeField] private float cliffInset = 0.02f;

    // Continuous river mesh instance (lives under this manager)
    private GameObject _riverSurfaceObj;
    private Mesh _riverSurfaceMesh;

    [Header("Auto-Build")]
    [SerializeField] private bool preBuildOnPlanetReady = true;

    [Header("Season Masks")]
    [SerializeField] private bool enableSeasonMasks = false;
    
    // Chunk storage
    private HexMapChunk[,] chunks;
    private Transform[] columnParents;
    
    // Baked texture data (shared across all chunks)
    private PlanetTextureBaker.BakeResult bakeResult;
    private Material sharedMaterial;
    private Texture2D biomeIndexMap;
    private Texture2D heightmapTexture;
    // Heightmap diagnostics (computed during BuildHeightmap)
    private float _heightmapMin = 0f;
    private float _heightmapMax = 0f;
    private int _heightmapNonZero = 0;
    private int _heightmapInvalidLut = 0;
    private int _heightmapMissingTileData = 0;
    private Texture2D sliceToBiomeMap; // 1D texture: pixel[sliceIndex].r = biomeIndex (for shader tint/params lookup)
    private Texture2DArray biomeAlbedoArray;
    private Texture2DArray biomeNormalArray;
    private Texture2DArray biomeMaskArray;
    private Texture2DArray biomeEmissiveArray;
    // Mapping from biome -> surface start/variant counts: x=startSlice, y=variantCount, z=surfaceIndex, w=forcedVariant
    private Vector4[] biomeSurfaceMapArray;
    private Texture2D biomeSurfaceMapTexture;
    private Texture2D biomeEmissiveMapTexture;
    private Vector4[] biomeTintArray;
    private Vector4[] biomeParamsArray;
    private Dictionary<Biome, int> biomeIndexLookup;
    
    // Map dimensions
    private float mapWidth;
    private float mapHeight;
    private float columnWidth;
    
    // References
    private HexGrid grid;
    private PlanetGenerator planetGenerator;
    private Transform cameraTransform;
    private TerrainOverlayGPU terrainOverlayGPU;
    private TileSystem overlayTileSystem;

    private bool ShouldRunDiagnostics()
    {
        if (GameManager.Instance == null) return true;
        if (!GameManager.Instance.restrictDiagnosticsToFirstPlanet) return true;
        return planetGenerator != null && planetGenerator.planetIndex == 0;
    }
    
    // Tile to chunk mapping
    private Dictionary<int, HexMapChunk> tileToChunk = new Dictionary<int, HexMapChunk>();

    // Seasonal mask sizing
    private int seasonMaskWidth;
    private int seasonMaskHeight;
    
    // Event subscriptions
    private PlanetGenerator _surfaceEventSource;
    private bool _subscribedToPlanetReady;
    
    // Coroutine tracking for async chunk building
    private Coroutine _buildCoroutine;
    
    // Public accessors (API compatible with FlatMapTextureRenderer)
    public HexGrid Grid => grid;
    public PlanetGenerator PlanetGenerator => planetGenerator;
    public int MeshSubdivisionsPerChunk => meshSubdivisionsPerChunk;
    /// <summary>
    /// The actual displacement strength used by the terrain shader (_ElevationScale).
    /// Water surfaces must use this value to match terrain vertex displacement.
    /// </summary>
    public float DisplacementStrength => displacementStrength;
    public float MapWidth => mapWidth;
    public float MapHeight => mapHeight;
    public bool IsBuilt => chunks != null;
    public Texture MapTexture => bakeResult.texture;
    public int[] LUT => bakeResult.lut;
    public int LUTWidth => bakeResult.width;
    public int LUTHeight => bakeResult.height;
    public Material SharedMaterial => sharedMaterial;
    public float FlatY => flatY;
    
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
        _subscribedToPlanetReady = false;
        if (preBuildOnPlanetReady && GameManager.Instance != null)
        {
            GameManager.Instance.OnPlanetReady += HandlePlanetReady;
            _subscribedToPlanetReady = true;
        }
        
        ClimateManager.OnPlanetSeasonChanged += HandlePlanetSeasonChanged;
    }
    
    private void OnDisable()
    {
        _subscribedToPlanetReady = false;
        if (GameManager.Instance != null)
            GameManager.Instance.OnPlanetReady -= HandlePlanetReady;
        
        if (_surfaceEventSource != null)
        {
            _surfaceEventSource.OnSurfaceGenerated -= HandleSurfaceGenerated;
            _surfaceEventSource = null;
        }
        
        ClimateManager.OnPlanetSeasonChanged -= HandlePlanetSeasonChanged;
    }
    
    private void Start()
    {
        if (cameraTransform == null)
        {
            var cam = Camera.main;
            if (cam != null) cameraTransform = cam.transform;
        }
        
        // If GameManager wasn't available during OnEnable, try subscribing now.
        // Guard: only subscribe if OnEnable didn't already (avoids double-fire of BuildChunks).
        if (preBuildOnPlanetReady && GameManager.Instance != null && !_subscribedToPlanetReady)
            GameManager.Instance.OnPlanetReady += HandlePlanetReady;

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
                if (ShouldRunDiagnostics())
                {
                    Debug.LogWarning($"[HexMapChunkManager][TRANSFORM] Changed: path={GetTransformPath(transform)} pos={transform.position.ToString("F3")} rot={transform.rotation.eulerAngles.ToString("F1")} scale={transform.lossyScale.ToString("F3")}");
                }
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
    
    #region Event Handlers
    
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
            // Subscribe to surface generation completion
            if (_surfaceEventSource != null)
                _surfaceEventSource.OnSurfaceGenerated -= HandleSurfaceGenerated;
            
            _surfaceEventSource = gen;
            gen.OnSurfaceGenerated += HandleSurfaceGenerated;
        }
    }
    
    private void HandleSurfaceGenerated()
    {
        var gen = _surfaceEventSource ?? GameManager.Instance?.GetCurrentPlanetGenerator();
        
        // Unsubscribe from surface event
        if (_surfaceEventSource != null)
        {
            _surfaceEventSource.OnSurfaceGenerated -= HandleSurfaceGenerated;
            _surfaceEventSource = null;
        }
        
        if (gen == null) return;
        BuildChunks(gen);
    }
    
    #endregion
    
    #region Chunk Building
    
    /// <summary>
    /// Build all chunks for the given planet generator.
    /// Uses the same texture baking pipeline as FlatMapTextureRenderer.
    /// Heavy work (LUT building, biome index map) is spread across multiple frames via coroutine.
    /// </summary>
    public void BuildChunks(PlanetGenerator planetGen)
    {
        if (planetGen == null || planetGen.Grid == null || planetGen.Grid.TileCount <= 0)
        {
            Debug.LogWarning("[HexMapChunkManager] Cannot build: missing planet generator or grid.");
            return;
        }

        // Enforce layer gate: do not build chunks if planet has no Surface layer
        if (!planetGen.HasLayer(GameManager.PlanetLayerType.Surface))
        {
            Debug.Log("[HexMapChunkManager] Surface layer not present on planet; skipping chunk build.");
            return;
        }
        
        // Stop any in-progress build coroutine to avoid overlapping builds
        if (_buildCoroutine != null)
        {
            StopCoroutine(_buildCoroutine);
            _buildCoroutine = null;
        }
        
        _buildCoroutine = StartCoroutine(BuildChunksCoroutine(planetGen));
    }
    
    /// <summary>
    /// Coroutine version of BuildChunks that spreads heavy work (LUT building, biome index map)
    /// across multiple frames to avoid blocking the main thread during planet generation.
    /// </summary>
    private System.Collections.IEnumerator BuildChunksCoroutine(PlanetGenerator planetGen)
    {
        // Clean up existing chunks
        DestroyAllChunks();
        
        this.planetGenerator = planetGen;
        this.grid = planetGen.Grid;
        
        // Get map dimensions — prefer the grid's own dimensions (authoritative source)
        // since the grid knows exactly how large it was built. GameManager preset values
        // can be stale/mismatched if the grid was built with different dimensions.
        float gridW = grid.MapWidth;
        float gridH = grid.MapHeight;
        
        if (gridW > 0.001f && gridH > 0.001f)
        {
            mapWidth = gridW;
            mapHeight = gridH;
        }
        else if (GameManager.Instance != null)
        {
            // Fallback to GameManager if grid dimensions aren't set yet
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
            Debug.LogError($"[HexMapChunkManager] Map dimensions are invalid! gridW={gridW}, gridH={gridH}, mapWidth={mapWidth}, mapHeight={mapHeight}");
        }
        else
        {
            Debug.Log($"[HexMapChunkManager] Map dimensions: {mapWidth}x{mapHeight} (grid={gridW}x{gridH}, GameManager={GameManager.Instance?.GetFlatMapWidth() ?? 0}x{GameManager.Instance?.GetFlatMapHeight() ?? 0})");
        }
        
        columnWidth = mapWidth / chunksX;
        
        // --- BATCHED: Build LUT across multiple frames (avoids 4M+ synchronous GetTileAtPosition calls) ---
        int[] preBuiltLUT = null;
        yield return StartCoroutine(EquirectLUTBuilder.BuildLUTBatched(
            grid, textureWidth, textureHeight, 64,
            lut => preBuiltLUT = lut));
        
        if (preBuiltLUT == null)
        {
            Debug.LogError("[HexMapChunkManager] Failed to build LUT in batched mode!");
            _buildCoroutine = null;
            yield break;
        }
        
        // Bake texture using PlanetTextureBaker with pre-built LUT (GPU bake is fast; LUT was the bottleneck)
        BakeTexture(preBuiltLUT);
        
        // --- BATCHED: Build biome visual maps with yielding for heavy texture operations ---
        yield return StartCoroutine(BuildBiomeVisualMapsCoroutine());

        int lutWidth = bakeResult.width > 0 ? bakeResult.width : textureWidth;
        int lutHeight = bakeResult.height > 0 ? bakeResult.height : textureHeight;
        seasonMaskWidth = Mathf.Max(1, lutWidth / chunksX);
        seasonMaskHeight = Mathf.Max(1, lutHeight / chunksZ);
        
        if (bakeResult.texture == null)
        {
            Debug.LogError("[HexMapChunkManager] Failed to bake texture!");
            _buildCoroutine = null;
            yield break;
        }
        
        // Create shared material
        CreateSharedMaterial();

        if (logTransformChainOnBuild && ShouldRunDiagnostics())
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

        // Build cliff wall meshes (after terrain meshes exist)
        BuildAllCliffWalls();

        // Build chunk-based water and foam meshes (after terrain meshes exist)
        BuildAllWaterMeshes();

        // Build continuous river surface mesh (after heightmap + tile data exist)
        BuildContinuousRiverSurfaceMesh();
        
        // Create picking collider for WorldPicker
        CreatePickingCollider();
        
        // Update WorldPicker with our LUT and collider
        UpdateWorldPicker();
        
        // Initialize terrain overlays
        InitializeTerrainOverlays();
        
        // (FlatMapTextureRenderer removed — HexMapChunkManager is the sole renderer)
        
        // DIAGNOSTIC: Log heightmap and displacement settings (respect GameManager toggle)
        if (ShouldRunDiagnostics())
        {
            Debug.LogError($"[HEIGHTMAP DIAGNOSTIC] ========================================");
            Debug.LogError($"[HEIGHTMAP DIAGNOSTIC] Heightmap Generated: {heightmapTexture != null}");
            Debug.LogError($"[HEIGHTMAP DIAGNOSTIC] PlanetGen.HasGeneratedSurface: {(planetGenerator != null ? planetGenerator.HasGeneratedSurface : false)}  data.Count={(planetGenerator != null && planetGenerator.data != null ? planetGenerator.data.Count : 0)}");
            Debug.LogError($"[HEIGHTMAP DIAGNOSTIC] Height range: {_heightmapMin:F4} .. {_heightmapMax:F4} (nonZeroPixels={_heightmapNonZero}, invalidLut={_heightmapInvalidLut}, missingTileData={_heightmapMissingTileData})");
            Debug.LogError($"[HEIGHTMAP DIAGNOSTIC] Displacement Strength: {displacementStrength} (Inspector value)");
            Debug.LogError($"[HEIGHTMAP DIAGNOSTIC] Material _ElevationScale: {(sharedMaterial != null && sharedMaterial.HasProperty("_ElevationScale") ? sharedMaterial.GetFloat("_ElevationScale").ToString("F4") : "N/A")} ");
            if (sharedMaterial != null && !sharedMaterial.HasProperty("_ElevationScale"))
            {
                Debug.LogWarning("Material is missing _ElevationScale property.");
            }
            Debug.LogError($"[HEIGHTMAP DIAGNOSTIC] ========================================");
        }
        
        _buildCoroutine = null;
    }
    
    private void BakeTexture(int[] preBuiltLUT = null)
    {
        // GPU-only baking (CPU path removed). Requires a compute shader.
        if (textureBakerComputeShader == null)
        {
            Debug.LogError("[HexMapChunkManager] textureBakerComputeShader is NULL. PlanetTextureBaker is GPU-only now, so baking cannot proceed.");
            bakeResult = new PlanetTextureBaker.BakeResult { width = textureWidth, height = textureHeight };
            return;
        }

        // Note: GPU baker uses per-tile colors; for non-BiomeColors render modes this is an approximation.
        // Pass pre-built LUT when available to avoid redundant synchronous LUT rebuild.
        bakeResult = PlanetTextureBaker.BakeGPU(planetGenerator, null, textureBakerComputeShader, textureWidth, textureHeight, false, preBuiltLUT);
    }

    private void BuildBiomeVisualMaps()
    {
        if (planetGenerator == null || grid == null || !grid.IsBuilt)
        {
            Debug.LogWarning("[HexMapChunkManager] Cannot build biome visuals: missing grid.");
            return;
        }

        if (biomeVisualDatabase == null || biomeVisualDatabase.biomes == null || biomeVisualDatabase.biomes.Count == 0)
        {
            Debug.LogWarning("[HexMapChunkManager] Missing biome visual database. Terrain visuals will be incomplete.");
            return;
        }

        int width = textureWidth;
        int height = textureHeight;

        // Reuse the LUT already built by BakeTexture() / PlanetTextureBaker.BakeGPU() —
        // don't rebuild it here (previously allocated another 16 MB duplicate).
        if (bakeResult.lut == null || bakeResult.lut.Length != width * height)
        {
            // Fallback: only build if BakeTexture didn't produce one (shouldn't happen)
            bakeResult.lut = EquirectLUTBuilder.BuildLUT(grid, width, height);
            bakeResult.width = width;
            bakeResult.height = height;
        }

        BuildBiomeLookup();
        BuildBiomeTextureArrays();
        BuildBiomeIndexMap(width, height);
        BuildHeightmap(width, height);
    }

    /// <summary>
    /// Coroutine version of BuildBiomeVisualMaps that yields during heavy texture generation
    /// (BuildBiomeIndexMap) to avoid blocking the main thread.
    /// The synchronous BuildBiomeVisualMaps() is kept for RebakeTexture() and other immediate-use paths.
    /// </summary>
    private System.Collections.IEnumerator BuildBiomeVisualMapsCoroutine()
    {
        if (planetGenerator == null || grid == null || !grid.IsBuilt)
        {
            Debug.LogWarning("[HexMapChunkManager] Cannot build biome visuals: missing grid.");
            yield break;
        }

        if (biomeVisualDatabase == null || biomeVisualDatabase.biomes == null || biomeVisualDatabase.biomes.Count == 0)
        {
            Debug.LogWarning("[HexMapChunkManager] Missing biome visual database. Terrain visuals will be incomplete.");
            yield break;
        }

        int width = textureWidth;
        int height = textureHeight;

        // Reuse the LUT already built by BakeTexture() / PlanetTextureBaker.BakeGPU() —
        // don't rebuild it here (previously allocated another 16 MB duplicate).
        if (bakeResult.lut == null || bakeResult.lut.Length != width * height)
        {
            // Fallback: only build if BakeTexture didn't produce one (shouldn't happen)
            bakeResult.lut = EquirectLUTBuilder.BuildLUT(grid, width, height);
            bakeResult.width = width;
            bakeResult.height = height;
        }

        BuildBiomeLookup();
        BuildBiomeTextureArrays();
        yield return null; // Yield after texture array building (can be heavy)

        // BATCHED: Build biome index map with yields between strips
        yield return StartCoroutine(BuildBiomeIndexMapCoroutine(width, height));
        
        // BATCHED: Build heightmap with yields between strips
        yield return StartCoroutine(BuildHeightmapCoroutine(width, height));
    }

    private void BuildBiomeLookup()
    {
        biomeIndexLookup = new Dictionary<Biome, int>();
        int index = 0;
        foreach (var entry in biomeVisualDatabase.biomes)
        {
            if (entry == null) continue;
            biomeIndexLookup[entry.biome] = index++;
        }
    }

    private void BuildBiomeTextureArrays()
    {
        // Build flattened surface library (families + variants) via BiomeVisualDatabase
        var visuals = biomeVisualDatabase.biomes;
        int count = visuals.Count;
        if (count == 0) return;

        // IMPORTANT:
        // `textureWidth/textureHeight` are used for the equirect LUT + baked planet textures (often 2:1 like 2048x1024).
        // Terrain surface Texture2DArrays should be square (e.g., 2048x2048). Do NOT tie them to the LUT height.
        int surfaceSize = textureWidth;
        var lib = biomeVisualDatabase.BuildSurfaceLibrary(surfaceSize, surfaceSize);
        if (lib != null)
        {
            // Use flattened arrays as the texture sources
            biomeAlbedoArray = lib.albedoArray;
            biomeNormalArray = lib.normalArray;
            biomeMaskArray = lib.maskArray;
                biomeEmissiveArray = lib.emissiveArray;

            // Build per-biome mapping vector: x = startSlice, y = variantCount, z = surfaceIndex, w = forcedVariant
            biomeSurfaceMapArray = new Vector4[count];
            for (int i = 0; i < count; i++)
            {
                int surfaceIndex = (lib.biomeToSurfaceIndex != null && i < lib.biomeToSurfaceIndex.Length) ? lib.biomeToSurfaceIndex[i] : -1;
                if (surfaceIndex >= 0 && surfaceIndex < lib.surfaceStartSlice.Length)
                {
                    int start = lib.surfaceStartSlice[surfaceIndex];
                    int variants = lib.surfaceVariantCounts[surfaceIndex];
                    int forced = (lib.biomeForcedVariant != null && i < lib.biomeForcedVariant.Length) ? lib.biomeForcedVariant[i] : -1;
                    biomeSurfaceMapArray[i] = new Vector4(start, variants, surfaceIndex, forced);
                }
                else
                {
                    biomeSurfaceMapArray[i] = new Vector4(0, 1, 0, -1);
                }
            }

            // Build a 1D RGBAFloat texture for shader lookup (width = biome count)
            try
            {
                biomeSurfaceMapTexture = new Texture2D(count, 1, TextureFormat.RGBAFloat, false, true);
                biomeSurfaceMapTexture.wrapMode = TextureWrapMode.Repeat;
                biomeSurfaceMapTexture.filterMode = FilterMode.Point;
                var cols = new Color[count];
                for (int i = 0; i < count; i++)
                {
                    var v = biomeSurfaceMapArray[i];
                    cols[i] = new Color(v.x, v.y, v.z, v.w);
                }
                biomeSurfaceMapTexture.SetPixels(cols);
                biomeSurfaceMapTexture.Apply(false, false);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[HexMapChunkManager] Failed to create biome surface map texture: {ex.Message}");
                biomeSurfaceMapTexture = null;
            }

            // Build per-biome emissive param texture (RGB = tint, A = intensity)
            try
            {
                biomeEmissiveMapTexture = new Texture2D(count, 1, TextureFormat.RGBAFloat, false, true);
                biomeEmissiveMapTexture.wrapMode = TextureWrapMode.Repeat;
                biomeEmissiveMapTexture.filterMode = FilterMode.Point;
                var ecols = new Color[count];
                for (int i = 0; i < count; i++)
                {
                    var entry = visuals[i];
                    if (entry != null)
                    {
                        ecols[i] = new Color(entry.emissiveTint.r, entry.emissiveTint.g, entry.emissiveTint.b, entry.emissiveIntensity);
                    }
                    else
                    {
                        ecols[i] = new Color(0f, 0f, 0f, 0f);
                    }
                }
                biomeEmissiveMapTexture.SetPixels(ecols);
                biomeEmissiveMapTexture.Apply(false, false);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[HexMapChunkManager] Failed to create biome emissive map texture: {ex.Message}");
                biomeEmissiveMapTexture = null;
            }

            // Populate tint and params arrays from biome visuals
            biomeTintArray = new Vector4[count];
            biomeParamsArray = new Vector4[count];
            for (int i = 0; i < count; i++)
            {
                var entry = visuals[i];
                if (entry != null)
                {
                    biomeTintArray[i] = entry.tint;
                    biomeParamsArray[i] = new Vector4(entry.tiling, entry.snowRetention, entry.wetnessResponse, entry.isWaterBiome ? 1f : 0f);
                }
                else
                {
                    biomeTintArray[i] = Color.white;
                    biomeParamsArray[i] = new Vector4(1f, 0f, 0f, 0f);
                }
            }

            biomeAlbedoArray.wrapMode = TextureWrapMode.Repeat;
            biomeNormalArray.wrapMode = TextureWrapMode.Repeat;
            biomeMaskArray.wrapMode = TextureWrapMode.Repeat;
            
            // Build slice-to-biome reverse map: for each texture array slice, store which biome index owns it.
            // This lets the shader look up per-biome tints/params from the slice index in _BiomeIndexMap.
            int totalSlices = biomeAlbedoArray != null ? biomeAlbedoArray.depth : 1;
            if (sliceToBiomeMap != null) DestroyImmediate(sliceToBiomeMap);
            sliceToBiomeMap = new Texture2D(totalSlices, 1, TextureFormat.RFloat, false, true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "SliceToBiomeMap"
            };
            var slicePixels = new Color[totalSlices];
            for (int bi = 0; bi < count; bi++)
            {
                if (biomeSurfaceMapArray == null || bi >= biomeSurfaceMapArray.Length) continue;
                var map = biomeSurfaceMapArray[bi];
                int startSlice = Mathf.Max(0, Mathf.RoundToInt(map.x));
                int variantCount = Mathf.Max(1, Mathf.RoundToInt(map.y));
                for (int v = 0; v < variantCount; v++)
                {
                    int si = startSlice + v;
                    if (si >= 0 && si < totalSlices)
                        slicePixels[si] = new Color(bi, 0, 0, 1);
                }
            }
            sliceToBiomeMap.SetPixels(slicePixels);
            sliceToBiomeMap.Apply(false, false);
            
            return;
        }

        // STRICT MODE:
        // No per-biome RGBA32 fallback arrays. If the surface library failed to build, fix the SurfaceFamilyData assets
        // (size/format/mip consistency) rather than silently allocating uncompressed arrays at runtime.
        Debug.LogError("[HexMapChunkManager] BuildSurfaceLibrary failed (strict). Terrain biome Texture2DArrays were NOT built. " +
                       "Fix your SurfaceFamilyData arrays so they are consistent (e.g., BC7 2048x2048 with matching mips).");
        biomeAlbedoArray = null;
        biomeNormalArray = null;
        biomeMaskArray = null;
        biomeEmissiveArray = null;
        biomeTintArray = null;
        biomeParamsArray = null;
        biomeSurfaceMapArray = null;
        biomeSurfaceMapTexture = null;
        biomeEmissiveMapTexture = null;
        return;
    }

    private void BuildBiomeIndexMap(int width, int height)
    {
        if (bakeResult.lut == null || bakeResult.lut.Length == 0) return;

        if (biomeIndexMap == null || biomeIndexMap.width != width || biomeIndexMap.height != height)
        {
            // Store slice index directly as float (RFloat) so Shader Graph sampling does NOT require *255 decoding.
            // This is a stability/UX win: fewer fragile packing/unpacking assumptions.
            biomeIndexMap = new Texture2D(width, height, TextureFormat.RFloat, false, true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                name = "BiomeIndexMap"
            };
        }

        // NOTE:
        // Despite the name, this map is now used as a *surface slice index map* (families/variants),
        // because the Shader Graph version looks better and is simpler when it samples arrays by a single index.
        // Values are written as float slice indices (RFloat), and shader code can use them directly (round if needed).
        bool warnedSliceOutOfRange = false;
        int minVal = int.MaxValue;
        int maxVal = 0;

        int maxSlice = (biomeAlbedoArray != null) ? Mathf.Max(0, biomeAlbedoArray.depth - 1) : -1;

        // MEMORY OPT: Process in row strips instead of one huge Color[width*height] (~67 MB for 2048x2048).
        // Each strip is only width × rowsPerStrip Colors, keeping peak allocation under ~1 MB.
        int rowsPerStrip = 64;
        var stripPixels = new Color[width * rowsPerStrip];

        for (int startRow = 0; startRow < height; startRow += rowsPerStrip)
        {
            int rowsThisStrip = Mathf.Min(rowsPerStrip, height - startRow);
            int stripLen = width * rowsThisStrip;

            for (int localIdx = 0; localIdx < stripLen; localIdx++)
            {
                int globalIdx = startRow * width + localIdx;
                int tileIndex = bakeResult.lut[globalIdx];
                if (tileIndex < 0)
                {
                    stripPixels[localIdx] = new Color(0f, 0f, 0f, 1f);
                    continue;
                }

                if (!planetGenerator.data.TryGetValue(tileIndex, out var tile))
                {
                    stripPixels[localIdx] = new Color(0f, 0f, 0f, 1f);
                    continue;
                }

                var visual = biomeVisualDatabase.Get(tile.biome);
                int biomeIndex = visual != null && biomeIndexLookup.TryGetValue(visual.biome, out var idx) ? idx : 0;

                // Convert biomeIndex -> surface slice index (startSlice + chosenVariant)
                int sliceIndex = 0;
                if (biomeSurfaceMapArray != null && biomeIndex >= 0 && biomeIndex < biomeSurfaceMapArray.Length)
                {
                    var map = biomeSurfaceMapArray[biomeIndex];
                    int startSlice = Mathf.Max(0, Mathf.RoundToInt(map.x));
                    int variantCount = Mathf.Max(1, Mathf.RoundToInt(map.y));
                    int forcedVariant = Mathf.RoundToInt(map.w);

                    int chosenVariant = 0;
                    if (forcedVariant >= 0 && forcedVariant < variantCount)
                    {
                        chosenVariant = forcedVariant;
                    }
                    else
                    {
                        // Deterministic per-tile variant selection (stable across runs)
                        unchecked
                        {
                            int h = tileIndex * 1103515245 + 12345;
                            chosenVariant = Mathf.Abs(h) % variantCount;
                        }
                    }

                    sliceIndex = startSlice + chosenVariant;
                }

                if (maxSlice >= 0 && sliceIndex > maxSlice)
                {
                    if (!warnedSliceOutOfRange)
                    {
                        Debug.LogWarning($"[HexMapChunkManager] Surface slice index out of range for texture arrays (slice={sliceIndex}, maxSlice={maxSlice}). Clamping to avoid invalid sampling.");
                        warnedSliceOutOfRange = true;
                    }
                    sliceIndex = maxSlice;
                }
                if (sliceIndex < 0) sliceIndex = 0;

                if (sliceIndex < minVal) minVal = sliceIndex;
                if (sliceIndex > maxVal) maxVal = sliceIndex;

                stripPixels[localIdx] = new Color(sliceIndex, 0f, 0f, 1f);
            }

            biomeIndexMap.SetPixels(0, startRow, width, rowsThisStrip, stripPixels);
        }

        biomeIndexMap.Apply(false, false);

        if (ShouldRunDiagnostics())
        {
            if (minVal == int.MaxValue) minVal = 0;
            Debug.Log($"[HexMapChunkManager][Diag] BiomeIndexMap(slice) range: {minVal}..{maxVal} (RFloat).");
        }
    }

    /// <summary>
    /// Coroutine version of BuildBiomeIndexMap that yields between row strips to avoid blocking.
    /// Each strip processes 64 rows then yields a frame, spreading ~4M pixel iterations across ~32 frames.
    /// The synchronous BuildBiomeIndexMap() is kept for RebakeTexture() and other immediate-use paths.
    /// </summary>
    private System.Collections.IEnumerator BuildBiomeIndexMapCoroutine(int width, int height)
    {
        if (bakeResult.lut == null || bakeResult.lut.Length == 0) yield break;

        if (biomeIndexMap == null || biomeIndexMap.width != width || biomeIndexMap.height != height)
        {
            biomeIndexMap = new Texture2D(width, height, TextureFormat.RFloat, false, true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                name = "BiomeIndexMap"
            };
        }

        bool warnedSliceOutOfRange = false;
        int minVal = int.MaxValue;
        int maxVal = 0;

        int maxSlice = (biomeAlbedoArray != null) ? Mathf.Max(0, biomeAlbedoArray.depth - 1) : -1;

        int rowsPerStrip = 64;
        var stripPixels = new Color[width * rowsPerStrip];

        for (int startRow = 0; startRow < height; startRow += rowsPerStrip)
        {
            int rowsThisStrip = Mathf.Min(rowsPerStrip, height - startRow);
            int stripLen = width * rowsThisStrip;

            for (int localIdx = 0; localIdx < stripLen; localIdx++)
            {
                int globalIdx = startRow * width + localIdx;
                int tileIndex = bakeResult.lut[globalIdx];
                if (tileIndex < 0)
                {
                    stripPixels[localIdx] = new Color(0f, 0f, 0f, 1f);
                    continue;
                }

                if (!planetGenerator.data.TryGetValue(tileIndex, out var tile))
                {
                    stripPixels[localIdx] = new Color(0f, 0f, 0f, 1f);
                    continue;
                }

                var visual = biomeVisualDatabase.Get(tile.biome);
                int biomeIndex = visual != null && biomeIndexLookup.TryGetValue(visual.biome, out var idx) ? idx : 0;

                int sliceIndex = 0;
                if (biomeSurfaceMapArray != null && biomeIndex >= 0 && biomeIndex < biomeSurfaceMapArray.Length)
                {
                    var map = biomeSurfaceMapArray[biomeIndex];
                    int startSlice = Mathf.Max(0, Mathf.RoundToInt(map.x));
                    int variantCount = Mathf.Max(1, Mathf.RoundToInt(map.y));
                    int forcedVariant = Mathf.RoundToInt(map.w);

                    int chosenVariant = 0;
                    if (forcedVariant >= 0 && forcedVariant < variantCount)
                    {
                        chosenVariant = forcedVariant;
                    }
                    else
                    {
                        unchecked
                        {
                            int h = tileIndex * 1103515245 + 12345;
                            chosenVariant = Mathf.Abs(h) % variantCount;
                        }
                    }

                    sliceIndex = startSlice + chosenVariant;
                }

                if (maxSlice >= 0 && sliceIndex > maxSlice)
                {
                    if (!warnedSliceOutOfRange)
                    {
                        Debug.LogWarning($"[HexMapChunkManager] Surface slice index out of range for texture arrays (slice={sliceIndex}, maxSlice={maxSlice}). Clamping to avoid invalid sampling.");
                        warnedSliceOutOfRange = true;
                    }
                    sliceIndex = maxSlice;
                }
                if (sliceIndex < 0) sliceIndex = 0;

                if (sliceIndex < minVal) minVal = sliceIndex;
                if (sliceIndex > maxVal) maxVal = sliceIndex;

                stripPixels[localIdx] = new Color(sliceIndex, 0f, 0f, 1f);
            }

            biomeIndexMap.SetPixels(0, startRow, width, rowsThisStrip, stripPixels);
            
            // Yield after each strip to spread work across frames
            yield return null;
        }

        biomeIndexMap.Apply(false, false);

        if (ShouldRunDiagnostics())
        {
            if (minVal == int.MaxValue) minVal = 0;
            Debug.Log($"[HexMapChunkManager][Diag] BiomeIndexMap(slice) range: {minVal}..{maxVal} (RFloat) [batched].");
        }
    }

    private void BuildHeightmap(int width, int height)
    {
        if (bakeResult.lut == null || bakeResult.lut.Length == 0) return;

        // Diagnostics: track min/max range as we write.
        _heightmapMin = float.MaxValue;
        _heightmapMax = float.MinValue;
        _heightmapNonZero = 0;
        _heightmapInvalidLut = 0;
        _heightmapMissingTileData = 0;

        // Use RHalf (16-bit float) instead of R8 (8-bit) for much better elevation precision.
        // R8 only provides 256 discrete height levels which causes visible stepping/terracing
        // on terrain slopes. RHalf provides 65536 levels, eliminating banding artifacts.
        if (heightmapTexture == null || heightmapTexture.width != width || heightmapTexture.height != height)
        {
            heightmapTexture = new Texture2D(width, height, TextureFormat.RHalf, true, true)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat,
                name = "TerrainHeightmap"
            };
        }

        // MEMORY OPT: Process in row strips instead of one huge Color[width*height] (~67 MB for 2048x2048).
        int rowsPerStrip = 64;
        var stripPixels = new Color[width * rowsPerStrip];

        for (int startRow = 0; startRow < height; startRow += rowsPerStrip)
        {
            int rowsThisStrip = Mathf.Min(rowsPerStrip, height - startRow);
            int stripLen = width * rowsThisStrip;

            for (int localIdx = 0; localIdx < stripLen; localIdx++)
            {
                int globalIdx = startRow * width + localIdx;
                int tileIndex = bakeResult.lut[globalIdx];
                float elevation = 0f;
                if (tileIndex < 0)
                {
                    _heightmapInvalidLut++;
                }
                else if (planetGenerator.data.TryGetValue(tileIndex, out var tile))
                {
                    elevation = tile.elevation; // World-space height offset — stored directly in RHalf (no clamping needed)
                }
                else
                {
                    _heightmapMissingTileData++;
                }

                if (elevation != 0f) _heightmapNonZero++;
                if (elevation < _heightmapMin) _heightmapMin = elevation;
                if (elevation > _heightmapMax) _heightmapMax = elevation;
                stripPixels[localIdx] = new Color(elevation, 0f, 0f, 1f);
            }

            heightmapTexture.SetPixels(0, startRow, width, rowsThisStrip, stripPixels);
        }

        heightmapTexture.Apply(true, false);

        if (_heightmapMin == float.MaxValue) _heightmapMin = 0f;
        if (_heightmapMax == float.MinValue) _heightmapMax = 0f;
    }

    /// <summary>
    /// Coroutine version of BuildHeightmap that yields between row strips to avoid blocking.
    /// Each strip processes 64 rows then yields a frame, spreading ~4M pixel iterations across ~32 frames.
    /// The synchronous BuildHeightmap() is kept for RebakeTexture() and other immediate-use paths.
    /// </summary>
    private System.Collections.IEnumerator BuildHeightmapCoroutine(int width, int height)
    {
        if (bakeResult.lut == null || bakeResult.lut.Length == 0) yield break;

        // Diagnostics: track min/max range as we write.
        _heightmapMin = float.MaxValue;
        _heightmapMax = float.MinValue;
        _heightmapNonZero = 0;
        _heightmapInvalidLut = 0;
        _heightmapMissingTileData = 0;

        if (heightmapTexture == null || heightmapTexture.width != width || heightmapTexture.height != height)
        {
            heightmapTexture = new Texture2D(width, height, TextureFormat.RHalf, true, true)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat,
                name = "TerrainHeightmap"
            };
        }

        int rowsPerStrip = 64;
        var stripPixels = new Color[width * rowsPerStrip];

        for (int startRow = 0; startRow < height; startRow += rowsPerStrip)
        {
            int rowsThisStrip = Mathf.Min(rowsPerStrip, height - startRow);
            int stripLen = width * rowsThisStrip;

            for (int localIdx = 0; localIdx < stripLen; localIdx++)
            {
                int globalIdx = startRow * width + localIdx;
                int tileIndex = bakeResult.lut[globalIdx];
                float elevation = 0f;
                if (tileIndex < 0)
                {
                    _heightmapInvalidLut++;
                }
                else if (planetGenerator.data.TryGetValue(tileIndex, out var tile))
                {
                    elevation = tile.elevation; // World-space height offset — stored directly in RHalf (no clamping needed)
                }
                else
                {
                    _heightmapMissingTileData++;
                }

                if (elevation != 0f) _heightmapNonZero++;
                if (elevation < _heightmapMin) _heightmapMin = elevation;
                if (elevation > _heightmapMax) _heightmapMax = elevation;
                stripPixels[localIdx] = new Color(elevation, 0f, 0f, 1f);
            }

            heightmapTexture.SetPixels(0, startRow, width, rowsThisStrip, stripPixels);
            
            // Yield after each strip to spread work across frames
            yield return null;
        }

        heightmapTexture.Apply(true, false);

        if (_heightmapMin == float.MaxValue) _heightmapMin = 0f;
        if (_heightmapMax == float.MinValue) _heightmapMax = 0f;
    }

    private static Texture2D CreateFlatNormal()
    {
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
        tex.SetPixel(0, 0, new Color(0.5f, 0.5f, 1f, 1f));
        tex.Apply();
        return tex;
    }

    private static Texture2D CreateDefaultMask()
    {
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
        tex.SetPixel(0, 0, new Color(0f, 1f, 0f, 0.5f));
        tex.Apply();
        return tex;
    }

    private void ApplyBiomeMaterialSettings()
    {
        if (sharedMaterial == null) return;

        if (biomeIndexMap != null)
        {
            sharedMaterial.SetTexture("_BiomeIndexMap", biomeIndexMap);
        }

        if (heightmapTexture != null)
        {
            sharedMaterial.SetTexture("_Heightmap", heightmapTexture);
            sharedMaterial.SetFloat("_ElevationScale", displacementStrength);
        }

        if (biomeAlbedoArray != null)
        {
            sharedMaterial.SetTexture("_BiomeAlbedoArray", biomeAlbedoArray);
        }
        if (biomeNormalArray != null)
        {
            sharedMaterial.SetTexture("_BiomeNormalArray", biomeNormalArray);
        }
        if (biomeMaskArray != null)
        {
            sharedMaterial.SetTexture("_BiomeMaskArray", biomeMaskArray);
        }

        if (biomeTintArray != null)
        {
            sharedMaterial.SetVectorArray("_BiomeTints", biomeTintArray);
        }

        if (biomeParamsArray != null)
        {
            sharedMaterial.SetVectorArray("_BiomeParams", biomeParamsArray);
        }

        if (biomeSurfaceMapArray != null)
        {
            sharedMaterial.SetVectorArray("_BiomeSurfaceMap", biomeSurfaceMapArray);
        }
        
        if (biomeSurfaceMapTexture != null)
        {
            sharedMaterial.SetTexture("_BiomeSurfaceMapTex", biomeSurfaceMapTexture);
        }

        if (biomeEmissiveArray != null)
        {
            sharedMaterial.SetTexture("_SurfaceEmissiveArray", biomeEmissiveArray);
        }

        if (biomeEmissiveMapTexture != null)
        {
            sharedMaterial.SetTexture("_BiomeEmissiveMapTex", biomeEmissiveMapTexture);
        }

        sharedMaterial.SetFloat("_GlobalSnowAmount", globalSnowAmount);
        sharedMaterial.SetFloat("_GlobalWetness", globalWetness);
        sharedMaterial.SetFloat("_MapWidth", mapWidth);
        sharedMaterial.SetFloat("_MapHeight", mapHeight);

        // Triplanar parameters
        sharedMaterial.SetFloat("_TriTiling", triplanarTiling);
        sharedMaterial.SetFloat("_TriBlend", triplanarBlend);

        // Biome transition blending parameters (optional shader props; guard to avoid warnings if a different shader is assigned)
        if (sharedMaterial.HasProperty("_BiomeBlendRadius"))
            sharedMaterial.SetFloat("_BiomeBlendRadius", biomeBlendRadius);
        if (sharedMaterial.HasProperty("_BiomeBlendSharpness"))
            sharedMaterial.SetFloat("_BiomeBlendSharpness", biomeBlendSharpness);

        // Cliff overlay parameters
        if (sharedMaterial.HasProperty("_CliffTiling"))
        {
            if (cliffAlbedoTexture != null)
                sharedMaterial.SetTexture("_CliffAlbedoMap", cliffAlbedoTexture);
            if (cliffNormalTexture != null)
                sharedMaterial.SetTexture("_CliffNormalMap", cliffNormalTexture);
            sharedMaterial.SetFloat("_CliffTiling", cliffTiling);
            sharedMaterial.SetFloat("_CliffSlopeStart", cliffSlopeStart);
            sharedMaterial.SetFloat("_CliffSlopeEnd", cliffSlopeEnd);
            sharedMaterial.SetFloat("_CliffSmoothness", cliffSmoothness);
            sharedMaterial.SetFloat("_CliffMetallic", cliffMetallic);
        }

        // Slice-to-biome reverse map (for per-biome tint/params lookup in shader)
        if (sliceToBiomeMap != null)
        {
            sharedMaterial.SetTexture("_SliceToBiomeMap", sliceToBiomeMap);
        }

        // Provide biome count for shader UV-based lookups
        int biomeCount = (biomeTintArray != null) ? biomeTintArray.Length : 0;
        sharedMaterial.SetFloat("_BiomeCount", (float)biomeCount);
    }

    private void OnValidate()
    {
        // Allow tuning in the Inspector even though the terrain material is created at runtime.
        // (Unity calls OnValidate when serialized fields change in the Inspector, including in Play Mode.)
        if (sharedMaterial == null) return;

        if (sharedMaterial.HasProperty("_TriTiling"))
            sharedMaterial.SetFloat("_TriTiling", triplanarTiling);
        if (sharedMaterial.HasProperty("_TriBlend"))
            sharedMaterial.SetFloat("_TriBlend", triplanarBlend);

        if (sharedMaterial.HasProperty("_BiomeBlendRadius"))
            sharedMaterial.SetFloat("_BiomeBlendRadius", biomeBlendRadius);
        if (sharedMaterial.HasProperty("_BiomeBlendSharpness"))
            sharedMaterial.SetFloat("_BiomeBlendSharpness", biomeBlendSharpness);

        if (sharedMaterial.HasProperty("_CliffTiling"))
        {
            if (cliffAlbedoTexture != null)
                sharedMaterial.SetTexture("_CliffAlbedoMap", cliffAlbedoTexture);
            if (cliffNormalTexture != null)
                sharedMaterial.SetTexture("_CliffNormalMap", cliffNormalTexture);
            sharedMaterial.SetFloat("_CliffTiling", cliffTiling);
            sharedMaterial.SetFloat("_CliffSlopeStart", cliffSlopeStart);
            sharedMaterial.SetFloat("_CliffSlopeEnd", cliffSlopeEnd);
            sharedMaterial.SetFloat("_CliffSmoothness", cliffSmoothness);
            sharedMaterial.SetFloat("_CliffMetallic", cliffMetallic);
        }
    }
    
    private void CreateSharedMaterial()
    {
        bool ShaderSupportsBiomeTerrain(Shader s)
        {
            if (s == null) return false;
            // We require these to be present; missing any usually means the assigned shader graph
            // doesn't match our runtime binding and will render with default values (often "all blue").
            // Note: Shader.HasProperty does not exist; check via a temporary Material instead.
            var tmp = new Material(s);
            bool ok =
                tmp.HasProperty("_BiomeIndexMap") &&
                tmp.HasProperty("_Heightmap") &&
                tmp.HasProperty("_BiomeAlbedoArray") &&
                tmp.HasProperty("_BiomeNormalArray") &&
                tmp.HasProperty("_BiomeMaskArray") &&
                // Shader Graph can either sample surface slices directly from _BiomeIndexMap (slice map mode),
                // or it can use _BiomeSurfaceMapTex (biome->slice mapping mode). We still set _BiomeSurfaceMapTex,
                // but do not require it for shader compatibility checks.
                tmp.HasProperty("_BiomeCount");
            Destroy(tmp);
            return ok;
        }

        // Single inspector-assigned shader (no fallbacks).
        Shader shader = terrainShader;
        if (shader == null)
        {
            Debug.LogError("[HexMapChunkManager] Terrain shader is not assigned. Assign exactly one terrain shader on HexMapChunkManager.");
            return;
        }

        // Final guard: if we still don't support the required properties, log loudly so we can fix the assignment.
        if (!ShaderSupportsBiomeTerrain(shader))
        {
            Debug.LogError($"[HexMapChunkManager] Selected terrain shader '{shader.name}' is missing required properties. " +
                           "Expected: _BiomeIndexMap, _Heightmap, _BiomeAlbedoArray, _BiomeNormalArray, _BiomeMaskArray, _BiomeCount. " +
                           "This will render incorrectly (often solid blue).");
            return;
        }

        sharedMaterial = new Material(shader);
        sharedMaterial.name = "ChunkTerrainMaterial";

        // One-time diagnostic: confirms which shader we actually bound at runtime.
        if (ShouldRunDiagnostics())
        {
            Debug.Log($"[HexMapChunkManager][Diag] Using terrain shader: {shader.name}");
        }

        ApplyBiomeMaterialSettings();
        
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
        
        // Create texture to encode tile indices.
        // IMPORTANT: Use linear color space (sRGB OFF), otherwise GPU sampling will gamma-transform
        // the values and DecodeTileIndex() in the shader will never match the hovered tile index.
        lutTexture = new Texture2D(width, height, TextureFormat.RGB24, false, true);
        lutTexture.filterMode = FilterMode.Point; // No interpolation!
        lutTexture.wrapMode = TextureWrapMode.Repeat;
        lutTexture.name = "TileIndexLUT";
        lutTexture.anisoLevel = 0;
        
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
    
    // Hex grid methods removed - shader graph doesn't support these properties.
    // To implement hex grid, create a separate HexGridOverlay component.
    
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
            int pIndex = planetGenerator != null ? planetGenerator.planetIndex : (GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0);
            overlayTileSystem = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;
            if (overlayTileSystem != null)
            {
                overlayTileSystem.OnTileOwnerChanged += HandleTileOwnerChanged;
                overlayTileSystem.OnFogChanged += HandleFogChanged;
            }
            
            // Apply overlay textures to material
            ApplyOverlayTexturesToMaterial();
        }
    }
    
    /// <summary>
    /// Apply fog and ownership overlay textures to the shared material.
    /// NOTE: The current shader graph does NOT have _FogMask, _EnableFog, _OwnershipOverlay, _EnableOwnership properties.
    /// These are set here for future compatibility when the shader is updated, or for alternative rendering approaches.
    /// Consider using a separate overlay pass or decal system for fog/ownership until the shader graph is extended.
    /// </summary>
    private void ApplyOverlayTexturesToMaterial()
    {
        if (sharedMaterial == null || terrainOverlayGPU == null) return;
        
        // NOTE: These properties don't exist in the current shader graph - they're set for future compatibility
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
            // Ensure a camera is assigned for picking. If the scene doesn't tag MainCamera (common in HDRP setups),
            // WorldPicker will still fall back to any available camera, but assigning here reduces ambiguity.
            if (worldPicker.targetCamera == null) worldPicker.targetCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
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
        // IMPORTANT:
        // This texture is displayed in UI as a minimap. We do NOT want vertical wrapping bleed at the top/bottom edges,
        // which can appear as a distorted band (especially after bilinear downscaling).
        // Clamp the destination and temporarily clamp the source during the blit so edge samples don't wrap.
        rt.wrapMode = TextureWrapMode.Clamp;
        var prevWrap = bakeResult.texture.wrapMode;
        bakeResult.texture.wrapMode = TextureWrapMode.Clamp;
        Graphics.Blit(bakeResult.texture, rt);
        bakeResult.texture.wrapMode = prevWrap;
        
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
        
        downscaled.wrapMode = TextureWrapMode.Clamp;
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

    #region Water Mesh System

    /// <summary>
    /// Compute hex circumradius (center-to-corner) matching HexGrid.GenerateFlatGrid().
    /// </summary>
    private float ComputeHexSize()
    {
        if (grid == null) return 1f;
        float sX = grid.MapWidth / (grid.Width * Mathf.Sqrt(3f));
        float sZ = grid.MapHeight / (1.5f * (grid.Height + 0.5f));
        return Mathf.Max(0.001f, Mathf.Min(sX, sZ));
    }

    // Pre-computed hex corner unit offsets (pointy-top, angles -30 + 60k degrees)
    private static readonly float[] HexCornerCos = new float[6];
    private static readonly float[] HexCornerSin = new float[6];
    private static bool _hexCornersInitialized = false;

    private static void EnsureHexCorners()
    {
        if (_hexCornersInitialized) return;
        for (int k = 0; k < 6; k++)
        {
            float angle = Mathf.Deg2Rad * (60f * k - 30f);
            HexCornerCos[k] = Mathf.Cos(angle);
            HexCornerSin[k] = Mathf.Sin(angle);
        }
        _hexCornersInitialized = true;
    }

    /// <summary>
    /// Build a single combined water mesh for all water tiles in a chunk.
    /// Creates a child GameObject "Water" under the chunk with MeshFilter + MeshRenderer.
    /// Vertex colors encode flow direction (rg) and water type (a).
    /// </summary>
    public void BuildWaterMeshForChunk(HexMapChunk chunk)
    {
        if (chunk == null || planetGenerator == null || grid == null) return;
        if (waterMaterial == null) return;

        // Destroy existing water child if present
        Transform existingWater = chunk.transform.Find("Water");
        if (existingWater != null) DestroyImmediate(existingWater.gameObject);

        EnsureHexCorners();
        float s = ComputeHexSize();

        var tileIndices = chunk.TileIndices;
        if (tileIndices == null || tileIndices.Count == 0) return;

        // Collect water tiles in this chunk
        var waterTiles = new List<int>();
        foreach (int ti in tileIndices)
        {
            if (!planetGenerator.data.TryGetValue(ti, out var td)) continue;
            // If continuous river surface is enabled, we normally skip per-tile river/lake fans.
            // BUT in Minecraft-like volume mode we want per-tile columns, so we keep them.
            if (!enableWaterVolumeColumns)
            {
                if (enableContinuousRiverSurface && td.waterType == TileWaterType.River) continue; // rivers rendered by continuous mesh
                if (enableContinuousRiverSurface && continuousWaterIncludesLakes && td.waterType == TileWaterType.Lake) continue; // lakes rendered by continuous mesh
            }
            if (td.waterType != TileWaterType.None) waterTiles.Add(ti);
        }
        if (waterTiles.Count == 0) return;

        // Build hex-fan top surface + optional volume side walls.
        // Use Lists because wall verts/indices depend on neighbor relationships.
        var vertices = new List<Vector3>(waterTiles.Count * 12);
        var uvs = new List<Vector2>(waterTiles.Count * 12);
        var colors = new List<Color>(waterTiles.Count * 12);
        var normals = new List<Vector3>(waterTiles.Count * 12);
        var triangles = new List<int>(waterTiles.Count * 24);

        // Chunk transform places the mesh; vertices are in chunk-local space.
        Vector3 chunkWorldPos = chunk.transform.position;

        // Cache per-tile top vertex base index + water height so we can build walls in a second pass.
        var baseVertByTile = new Dictionary<int, int>(waterTiles.Count);
        var waterYByTile = new Dictionary<int, float>(waterTiles.Count);

        int AddVert(Vector3 v, Vector2 uv, Color c)
        {
            int idx = vertices.Count;
            vertices.Add(v);
            uvs.Add(uv);
            colors.Add(c);
            normals.Add(Vector3.up); // will be recalculated; placeholder keeps array lengths consistent
            return idx;
        }

        void AddTri(int a, int b, int c)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        foreach (int tileIdx in waterTiles)
        {
            var td = planetGenerator.data[tileIdx];
            Vector3 tileCenter = grid.tileCenters[tileIdx];

            // Water world Y: use manual ocean water Y when enabled, otherwise computed sea level.
            // Lakes/rivers fall back to per-tile waterElevation (scaled).
            float waterWorldY;
            if (td.waterType == TileWaterType.Ocean)
            {
                waterWorldY = (useManualOceanWaterY ? manualOceanWaterY : planetGenerator.SeaLevelWorldY) + waterYOffset;
            }
            else
            {
                waterWorldY = flatY + td.waterElevation * displacementStrength + waterYOffset;
            }

            // Convert to chunk-local
            Vector3 localCenter = new Vector3(
                tileCenter.x - chunkWorldPos.x,
                waterWorldY - chunkWorldPos.y,
                tileCenter.z - chunkWorldPos.z
            );

            // Encode flow into vertex color
            Color flowColor = new Color(
                td.riverFlowDirXZ.x * 0.5f + 0.5f,
                td.riverFlowDirXZ.y * 0.5f + 0.5f,
                0f,
                (float)td.waterType / 3f // None=0, Ocean=0.33, Lake=0.67, River=1.0
            );

            int baseVert = vertices.Count;
            baseVertByTile[tileIdx] = baseVert;
            waterYByTile[tileIdx] = waterWorldY;

            // Center vertex
            AddVert(localCenter, new Vector2(0.5f, 0.5f), flowColor);

            // 6 corner vertices
            for (int k = 0; k < 6; k++)
            {
                AddVert(
                    localCenter + new Vector3(s * HexCornerCos[k], 0f, s * HexCornerSin[k]),
                    new Vector2(HexCornerCos[k] * 0.5f + 0.5f, HexCornerSin[k] * 0.5f + 0.5f),
                    flowColor
                );
            }

            // 6 triangles (fan from center) — clockwise winding so faces point UP (toward camera)
            for (int k = 0; k < 6; k++)
            {
                AddTri(
                    baseVert,                    // center
                    baseVert + 1 + (k + 1) % 6,  // corner k+1
                    baseVert + 1 + k             // corner k
                );
            }
        }

        // Optional: build vertical side walls for a voxel-like filled look.
        if (enableWaterVolumeColumns)
        {
            float depth = Mathf.Max(0.01f, waterVolumeDepth);

            foreach (int tileIdx in waterTiles)
            {
                var td = planetGenerator.data[tileIdx];
                if (!waterVolumeIncludeOcean && td.waterType == TileWaterType.Ocean) continue;

                float waterWorldY = waterYByTile[tileIdx];
                int baseVert = baseVertByTile[tileIdx];
                var neighbors = grid.neighbors[tileIdx];

                for (int edge = 0; edge < 6; edge++)
                {
                    int nbrIdx = -1;
                    if (neighbors != null && edge < neighbors.Count) nbrIdx = neighbors[edge];

                    bool nbrIsWater = false;
                    float nbrWaterY = waterWorldY;

                    if (nbrIdx >= 0 && nbrIdx < grid.TileCount && planetGenerator.data.TryGetValue(nbrIdx, out var nbrTd))
                    {
                        nbrIsWater = nbrTd.waterType != TileWaterType.None;
                        if (nbrIsWater)
                        {
                            // Note: neighbor might not be in this chunk; compute its water height on the fly.
                            if (nbrTd.waterType == TileWaterType.Ocean)
                                nbrWaterY = (useManualOceanWaterY ? manualOceanWaterY : planetGenerator.SeaLevelWorldY) + waterYOffset;
                            else
                                nbrWaterY = flatY + nbrTd.waterElevation * displacementStrength + waterYOffset;
                        }
                    }

                    // Build wall if bordering land/empty, or if neighbor water is significantly lower (step).
                    bool needWall = !nbrIsWater || (nbrWaterY < waterWorldY - waterVolumeStepEpsilon);
                    if (!needWall) continue;

                    float bottomWorldY = nbrIsWater ? nbrWaterY : (waterWorldY - depth);

                    // Edge endpoints are corner edge and (edge+1)%6.
                    int topA = baseVert + 1 + edge;
                    int topB = baseVert + 1 + ((edge + 1) % 6);

                    Vector3 vTopA = vertices[topA];
                    Vector3 vTopB = vertices[topB];

                    // Bottom verts (same XZ as top; lower Y)
                    Vector3 vBotA = new Vector3(vTopA.x, bottomWorldY - chunkWorldPos.y, vTopA.z);
                    Vector3 vBotB = new Vector3(vTopB.x, bottomWorldY - chunkWorldPos.y, vTopB.z);

                    Color c = colors[topA];
                    int botA = AddVert(vBotA, new Vector2(0f, 0f), c);
                    int botB = AddVert(vBotB, new Vector2(1f, 0f), c);

                    // Two triangles for the quad. Winding isn't critical with Cull Off, but keep consistent.
                    AddTri(topA, topB, botB);
                    AddTri(topA, botB, botA);
                }
            }
        }

        // Build mesh
        var waterMesh = new Mesh();
        waterMesh.name = $"Water_{chunk.ChunkX}_{chunk.ChunkZ}";
        waterMesh.SetVertices(vertices);
        waterMesh.SetUVs(0, uvs);
        waterMesh.SetColors(colors);
        // We'll recalc normals after triangles to ensure correctness even under mirrored parents
        // (and because volume walls need proper normals).
        // If this chunk's parent transform has a negative scale (mirroring),
        // reverse triangle winding so faces remain front-facing after transform.
        var triArr = triangles.ToArray();
        float det = chunk.transform.lossyScale.x * chunk.transform.lossyScale.y * chunk.transform.lossyScale.z;
        if (det < 0f)
        {
            for (int i = 0; i < triArr.Length; i += 3)
            {
                int tmp = triArr[i + 1];
                triArr[i + 1] = triArr[i + 2];
                triArr[i + 2] = tmp;
            }
        }

        waterMesh.SetTriangles(triArr, 0);
        waterMesh.RecalculateNormals();
        waterMesh.RecalculateBounds();

        // Expand bounds vertically for safety
        var b = waterMesh.bounds;
        b.Expand(new Vector3(0f, 10f, 0f));
        waterMesh.bounds = b;

        // Create child GameObject
        GameObject waterObj = new GameObject("Water");
        waterObj.transform.SetParent(chunk.transform, false);
        waterObj.transform.localPosition = Vector3.zero;
        waterObj.transform.localRotation = Quaternion.identity;
        waterObj.transform.localScale = Vector3.one;
        waterObj.layer = chunk.gameObject.layer;

        var mf = waterObj.AddComponent<MeshFilter>();
        mf.sharedMesh = waterMesh;

        var mr = waterObj.AddComponent<MeshRenderer>();
        mr.sharedMaterial = waterMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.allowOcclusionWhenDynamic = false;
    }

    /// <summary>
    /// Build foam edge quads along water-to-land boundaries in a chunk.
    /// Creates thin quad strips that straddle each shared edge between a water tile
    /// and a non-water neighbor, with UV.v = 0 on water side, UV.v = 1 on land side.
    /// </summary>
    public void BuildFoamEdgeMeshForChunk(HexMapChunk chunk)
    {
        if (chunk == null || planetGenerator == null || grid == null) return;
        if (foamMaterial == null) return;

        // Destroy existing foam child if present
        Transform existingFoam = chunk.transform.Find("Foam");
        if (existingFoam != null) DestroyImmediate(existingFoam.gameObject);

        EnsureHexCorners();
        float s = ComputeHexSize();
        float foamWidth = s * 0.15f; // foam strip extends 15% of hex size outward
        float foamYLift = 0.01f;     // slight Y above water

        var tileIndices = chunk.TileIndices;
        if (tileIndices == null || tileIndices.Count == 0) return;

        Vector3 chunkWorldPos = chunk.transform.position;

        // Collect edge quads
        var verts = new List<Vector3>();
        var foamUVs = new List<Vector2>();
        var foamNormals = new List<Vector3>();
        var tris = new List<int>();

        foreach (int tileIdx in tileIndices)
        {
            if (!planetGenerator.data.TryGetValue(tileIdx, out var td)) continue;
            if (td.waterType == TileWaterType.None) continue;

            Vector3 tileCenter = grid.tileCenters[tileIdx];
                // Use manual ocean water Y for ocean foam edges as well.
                float waterWorldY;
                if (td.waterType == TileWaterType.Ocean)
                {
                    waterWorldY = (useManualOceanWaterY ? manualOceanWaterY : planetGenerator.SeaLevelWorldY) + waterYOffset + foamYLift;
                }
                else
                {
                    waterWorldY = flatY + td.waterElevation * displacementStrength + waterYOffset + foamYLift;
                }

            var neighbors = grid.neighbors[tileIdx];
            for (int edge = 0; edge < 6; edge++)
            {
                // Check if this edge's neighbor is NOT water
                int nbrIdx = -1;
                if (edge < neighbors.Count) nbrIdx = neighbors[edge];
                if (nbrIdx < 0 || nbrIdx >= grid.TileCount) continue;

                bool nbrIsWater = false;
                if (planetGenerator.data.TryGetValue(nbrIdx, out var nbrTd))
                {
                    nbrIsWater = nbrTd.waterType != TileWaterType.None;
                }
                if (nbrIsWater) continue; // skip water-water edges

                // Build quad along this edge
                // Edge k is between corner k and corner (k+1)%6
                Vector3 cornerA = tileCenter + new Vector3(s * HexCornerCos[edge], 0f, s * HexCornerSin[edge]);
                Vector3 cornerB = tileCenter + new Vector3(s * HexCornerCos[(edge + 1) % 6], 0f, s * HexCornerSin[(edge + 1) % 6]);

                // Edge midpoint direction (outward from center)
                Vector3 edgeMid = (cornerA + cornerB) * 0.5f;
                Vector3 outDir = (edgeMid - tileCenter);
                outDir.y = 0f;
                outDir.Normalize();

                // Inner edge (water side)
                Vector3 innerA = new Vector3(cornerA.x - chunkWorldPos.x, waterWorldY - chunkWorldPos.y, cornerA.z - chunkWorldPos.z);
                Vector3 innerB = new Vector3(cornerB.x - chunkWorldPos.x, waterWorldY - chunkWorldPos.y, cornerB.z - chunkWorldPos.z);

                // Outer edge (land side, pushed outward)
                Vector3 outerA = innerA + new Vector3(outDir.x * foamWidth, 0f, outDir.z * foamWidth);
                Vector3 outerB = innerB + new Vector3(outDir.x * foamWidth, 0f, outDir.z * foamWidth);

                int baseIdx = verts.Count;
                verts.Add(innerA); // 0 - water side A
                verts.Add(innerB); // 1 - water side B
                verts.Add(outerA); // 2 - land side A
                verts.Add(outerB); // 3 - land side B

                foamUVs.Add(new Vector2(0f, 0f)); // inner A
                foamUVs.Add(new Vector2(1f, 0f)); // inner B
                foamUVs.Add(new Vector2(0f, 1f)); // outer A
                foamUVs.Add(new Vector2(1f, 1f)); // outer B

                foamNormals.Add(Vector3.up);
                foamNormals.Add(Vector3.up);
                foamNormals.Add(Vector3.up);
                foamNormals.Add(Vector3.up);

                // Two triangles for the quad — clockwise winding so faces point UP (toward camera)
                tris.Add(baseIdx);
                tris.Add(baseIdx + 1);
                tris.Add(baseIdx + 2);

                tris.Add(baseIdx + 1);
                tris.Add(baseIdx + 3);
                tris.Add(baseIdx + 2);
            }
        }

        if (verts.Count == 0) return;

        var foamMesh = new Mesh();
        foamMesh.name = $"Foam_{chunk.ChunkX}_{chunk.ChunkZ}";
        foamMesh.SetVertices(verts);
        foamMesh.SetUVs(0, foamUVs);
        // We'll recalc normals after triangles to ensure correctness even under mirrored parents
        var triArr = tris.ToArray();
        float detFoam = chunk.transform.lossyScale.x * chunk.transform.lossyScale.y * chunk.transform.lossyScale.z;
        if (detFoam < 0f)
        {
            for (int i = 0; i < triArr.Length; i += 3)
            {
                int tmp = triArr[i + 1];
                triArr[i + 1] = triArr[i + 2];
                triArr[i + 2] = tmp;
            }
        }
        foamMesh.SetTriangles(triArr, 0);
        foamMesh.RecalculateNormals();
        foamMesh.RecalculateBounds();

        var b = foamMesh.bounds;
        b.Expand(new Vector3(0f, 10f, 0f));
        foamMesh.bounds = b;

        GameObject foamObj = new GameObject("Foam");
        foamObj.transform.SetParent(chunk.transform, false);
        foamObj.transform.localPosition = Vector3.zero;
        foamObj.transform.localRotation = Quaternion.identity;
        foamObj.transform.localScale = Vector3.one;
        foamObj.layer = chunk.gameObject.layer;

        var mf = foamObj.AddComponent<MeshFilter>();
        mf.sharedMesh = foamMesh;

        var mr = foamObj.AddComponent<MeshRenderer>();
        mr.sharedMaterial = foamMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.allowOcclusionWhenDynamic = false;
    }

    /// <summary>
    /// Build water and foam meshes for ALL chunks.
    /// Called once during BuildChunks after terrain is ready.
    /// </summary>
    private void BuildAllWaterMeshes()
    {
        if (chunks == null || planetGenerator == null) return;

        // Root-cause safeguard:
        // Ensure waterType is coherent with tile biome/flags before building meshes.
        // If a downstream system ever desyncs biome vs. waterType, chunk meshes will skip water tiles entirely.
        RepairWaterMetadataFromBiome();

        for (int x = 0; x < chunksX; x++)
        {
            for (int z = 0; z < chunksZ; z++)
            {
                if (chunks[x, z] != null)
                {
                    BuildWaterMeshForChunk(chunks[x, z]);
                    BuildFoamEdgeMeshForChunk(chunks[x, z]);
                }
            }
        }

        // Diagnostic: detect coast/seas/ocean tiles missing waterType (common cause of missing coast water)
        if (ShouldRunDiagnostics() && planetGenerator != null && planetGenerator.data != null)
        {
            int coastBiome = 0, coastMissingWaterType = 0;
            int riverTiles = 0, riverMissingWaterType = 0;
            int lakeTiles = 0, lakeMissingWaterType = 0;
            foreach (var kvp in planetGenerator.data)
            {
                var td = kvp.Value;
                if (td.biome == Biome.Coast)
                {
                    coastBiome++;
                    if (td.waterType == TileWaterType.None) coastMissingWaterType++;
                }
                if (td.biome == Biome.River || td.isRiver)
                {
                    riverTiles++;
                    if (td.waterType == TileWaterType.None) riverMissingWaterType++;
                }
                if (td.biome == Biome.Lake || td.isLake)
                {
                    lakeTiles++;
                    if (td.waterType == TileWaterType.None) lakeMissingWaterType++;
                }
            }
            if (coastMissingWaterType > 0)
            {
                Debug.LogWarning($"[HexMapChunkManager][WaterDiag] Coast tiles missing waterType: {coastMissingWaterType}/{coastBiome}. These will not get coast water meshes.");
            }
            if (riverMissingWaterType > 0)
            {
                Debug.LogWarning($"[HexMapChunkManager][WaterDiag] River tiles missing waterType: {riverMissingWaterType}/{riverTiles}. These will not get river water meshes.");
            }
            if (lakeMissingWaterType > 0)
            {
                Debug.LogWarning($"[HexMapChunkManager][WaterDiag] Lake tiles missing waterType: {lakeMissingWaterType}/{lakeTiles}. These will not get lake water meshes.");
            }
        }
    }

    /// <summary>
    /// Ensure <see cref="HexTileData.waterType"/> is coherent with tile biome/flags.
    /// This prevents water tiles from being skipped by the chunk water mesh builder.
    /// 
    /// IMPORTANT: HexTileData is a reference type (class). Modifying td.waterType directly
    /// modifies the object in the dictionary without invalidating the enumerator. We must NOT
    /// call planetGenerator.data[idx] = td (dictionary indexer set) during foreach iteration
    /// because that increments the dictionary version and throws InvalidOperationException.
    /// </summary>
    private void RepairWaterMetadataFromBiome()
    {
        if (planetGenerator == null || planetGenerator.data == null) return;

        int fixedOcean = 0, fixedRiver = 0, fixedLake = 0, clearedNonWater = 0;
        int waterBiomeButNone = 0;
        int totalWaterTiles = 0;

        // HexTileData is a class — td IS the same object stored in the dictionary.
        // Modifying td.waterType modifies the dictionary value directly (no need to re-assign).
        foreach (var kvp in planetGenerator.data)
        {
            var td = kvp.Value;
            if (td == null) continue;

            bool biomeOcean = (td.biome == Biome.Ocean || td.biome == Biome.Seas || td.biome == Biome.Coast);
            bool biomeLake = (td.biome == Biome.Lake);
            bool biomeRiver = (td.biome == Biome.River);
            bool flagLake = td.isLake;
            bool flagRiver = td.isRiver;

            bool isWaterBiome = biomeOcean || biomeLake || biomeRiver || flagLake || flagRiver;
            if (isWaterBiome) totalWaterTiles++;
            if (isWaterBiome && td.waterType == TileWaterType.None) waterBiomeButNone++;

            if (biomeOcean)
            {
                if (td.waterType != TileWaterType.Ocean)
                {
                    td.waterType = TileWaterType.Ocean;
                    td.isLake = false;
                    td.isRiver = false;
                    td.lakeId = -1;
                    td.waterElevation = planetGenerator.coastElevation;
                    td.riverFlowDirXZ = Vector2.zero;
                    fixedOcean++;
                }
                continue;
            }

            if (biomeLake || flagLake)
            {
                if (td.waterType != TileWaterType.Lake)
                {
                    td.waterType = TileWaterType.Lake;
                    td.isLake = true;
                    td.isRiver = false;
                    if (td.lakeId < 0) td.lakeId = -1;
                    if (td.waterElevation == 0f) td.waterElevation = td.elevation;
                    td.riverFlowDirXZ = Vector2.zero;
                    fixedLake++;
                }
                continue;
            }

            if (biomeRiver || flagRiver)
            {
                if (td.waterType != TileWaterType.River)
                {
                    td.waterType = TileWaterType.River;
                    td.isRiver = true;
                    td.isLake = false;
                    td.lakeId = -1;
                    if (td.waterElevation == 0f) td.waterElevation = td.elevation;
                    fixedRiver++;
                }
                continue;
            }

            // Non-water biomes should not carry a water surface classification.
            if (td.waterType != TileWaterType.None)
            {
                td.waterType = TileWaterType.None;
                td.lakeId = -1;
                td.waterElevation = 0f;
                td.riverFlowDirXZ = Vector2.zero;
                clearedNonWater++;
            }
        }

        int totalRepaired = fixedOcean + fixedLake + fixedRiver + clearedNonWater;
        // Always log this — it's the most important diagnostic for missing water.
        Debug.Log($"[HexMapChunkManager][WaterRepair] totalWaterTiles={totalWaterTiles}, waterBiomeButWaterTypeNone(pre)={waterBiomeButNone}, repaired: ocean={fixedOcean} lake={fixedLake} river={fixedRiver} clearedNonWater={clearedNonWater}");
    }

    /// <summary>
    /// Build cliff wall meshes for ALL chunks.
    /// Cliff walls are vertical quads along edges where two neighboring tiles have a large elevation step.
    /// </summary>
    private void BuildAllCliffWalls()
    {
        if (!enableCliffWalls) { DestroyAllCliffWalls(); return; }
        if (chunks == null || planetGenerator == null || grid == null) return;
        if (cliffWallMaterial == null) return;

        for (int x = 0; x < chunksX; x++)
        {
            for (int z = 0; z < chunksZ; z++)
            {
                if (chunks[x, z] != null)
                    BuildCliffWallsForChunk(chunks[x, z]);
            }
        }
    }

    private void DestroyAllCliffWalls()
    {
        if (chunks == null) return;
        for (int x = 0; x < chunksX; x++)
        {
            for (int z = 0; z < chunksZ; z++)
            {
                var c = chunks[x, z];
                if (c == null) continue;
                Transform existing = c.transform.Find("CliffWalls");
                if (existing != null) DestroyImmediate(existing.gameObject);
            }
        }
    }

    /// <summary>
    /// Build a single combined cliff wall mesh for a chunk.
    /// Walls are created only from the higher tile toward the lower tile, so edges are not duplicated.
    /// </summary>
    private void BuildCliffWallsForChunk(HexMapChunk chunk)
    {
        if (chunk == null || planetGenerator == null || grid == null) return;
        if (!enableCliffWalls) return;
        if (cliffWallMaterial == null) return;

        // Destroy existing cliff wall child if present
        Transform existing = chunk.transform.Find("CliffWalls");
        if (existing != null) DestroyImmediate(existing.gameObject);

        EnsureHexCorners();
        float s = ComputeHexSize();
        float inset = Mathf.Max(0f, cliffInset);

        var tileIndices = chunk.TileIndices;
        if (tileIndices == null || tileIndices.Count == 0) return;

        Vector3 chunkWorldPos = chunk.transform.position;
        float minDelta = Mathf.Max(0.001f, cliffMinHeightDelta);
        float bottomExt = Mathf.Max(0f, cliffBottomExtension);

        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        void AddQuad(Vector3 topA, Vector3 topB, Vector3 botA, Vector3 botB)
        {
            int baseIdx = verts.Count;
            verts.Add(topA);
            verts.Add(topB);
            verts.Add(botA);
            verts.Add(botB);

            uvs.Add(new Vector2(0f, 1f));
            uvs.Add(new Vector2(1f, 1f));
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(1f, 0f));

            // Two triangles (Cull mode depends on material; keep consistent winding).
            tris.Add(baseIdx);
            tris.Add(baseIdx + 1);
            tris.Add(baseIdx + 2);
            tris.Add(baseIdx + 1);
            tris.Add(baseIdx + 3);
            tris.Add(baseIdx + 2);
        }

        foreach (int tileIdx in tileIndices)
        {
            if (!planetGenerator.data.TryGetValue(tileIdx, out var td)) continue;
            Vector3 tileCenter = grid.tileCenters[tileIdx];
            float tileTopWorldY = flatY + td.elevation * displacementStrength;

            var neighbors = grid.neighbors[tileIdx];
            if (neighbors == null) continue;

            for (int edge = 0; edge < 6; edge++)
            {
                int nbrIdx = (edge < neighbors.Count) ? neighbors[edge] : -1;
                if (nbrIdx < 0 || nbrIdx >= grid.TileCount) continue;
                if (!planetGenerator.data.TryGetValue(nbrIdx, out var nd)) continue;

                float nbrTopWorldY = flatY + nd.elevation * displacementStrength;
                float delta = tileTopWorldY - nbrTopWorldY;
                if (delta < minDelta) continue; // only build from higher tile

                // Edge endpoints in world space (XZ from hex corners, Y from each tile top)
                Vector3 cornerAWorld = tileCenter + new Vector3(s * HexCornerCos[edge], 0f, s * HexCornerSin[edge]);
                Vector3 cornerBWorld = tileCenter + new Vector3(s * HexCornerCos[(edge + 1) % 6], 0f, s * HexCornerSin[(edge + 1) % 6]);

                // Inset toward tile center to avoid z-fighting with top surface at the seam
                if (inset > 0f)
                {
                    Vector3 dirA = (tileCenter - cornerAWorld); dirA.y = 0f; float lenA = dirA.magnitude; if (lenA > 1e-5f) cornerAWorld += (dirA / lenA) * inset;
                    Vector3 dirB = (tileCenter - cornerBWorld); dirB.y = 0f; float lenB = dirB.magnitude; if (lenB > 1e-5f) cornerBWorld += (dirB / lenB) * inset;
                }

                float bottomWorldY = nbrTopWorldY - bottomExt;

                // Convert to chunk-local
                Vector3 topALocal = new Vector3(cornerAWorld.x - chunkWorldPos.x, tileTopWorldY - chunkWorldPos.y, cornerAWorld.z - chunkWorldPos.z);
                Vector3 topBLocal = new Vector3(cornerBWorld.x - chunkWorldPos.x, tileTopWorldY - chunkWorldPos.y, cornerBWorld.z - chunkWorldPos.z);
                Vector3 botALocal = new Vector3(cornerAWorld.x - chunkWorldPos.x, bottomWorldY - chunkWorldPos.y, cornerAWorld.z - chunkWorldPos.z);
                Vector3 botBLocal = new Vector3(cornerBWorld.x - chunkWorldPos.x, bottomWorldY - chunkWorldPos.y, cornerBWorld.z - chunkWorldPos.z);

                AddQuad(topALocal, topBLocal, botALocal, botBLocal);
            }
        }

        if (tris.Count < 3) return;

        var m = new Mesh();
        m.name = $"CliffWalls_{chunk.ChunkX}_{chunk.ChunkZ}";
        m.SetVertices(verts);
        m.SetUVs(0, uvs);

        // Mirror-safe winding
        var triArr = tris.ToArray();
        float det = chunk.transform.lossyScale.x * chunk.transform.lossyScale.y * chunk.transform.lossyScale.z;
        if (det < 0f)
        {
            for (int i = 0; i < triArr.Length; i += 3)
            {
                int tmp = triArr[i + 1];
                triArr[i + 1] = triArr[i + 2];
                triArr[i + 2] = tmp;
            }
        }
        m.SetTriangles(triArr, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();

        // Expand bounds vertically a bit
        var b = m.bounds;
        b.Expand(new Vector3(0f, 50f, 0f));
        m.bounds = b;

        GameObject obj = new GameObject("CliffWalls");
        obj.transform.SetParent(chunk.transform, false);
        obj.transform.localPosition = Vector3.zero;
        obj.transform.localRotation = Quaternion.identity;
        obj.transform.localScale = Vector3.one;
        obj.layer = chunk.gameObject.layer;

        var mf = obj.AddComponent<MeshFilter>();
        mf.sharedMesh = m;
        var mr = obj.AddComponent<MeshRenderer>();
        mr.sharedMaterial = cliffWallMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        mr.receiveShadows = true;
        mr.allowOcclusionWhenDynamic = false;
    }

    // =====================================================================================
    //  Continuous River Surface Mesh (SDF + Marching Squares)
    // =====================================================================================
    private void BuildContinuousRiverSurfaceMesh()
    {
        // If we're using Minecraft-like per-tile volume columns, don't also build the continuous SDF surface.
        if (enableWaterVolumeColumns) { DestroyRiverSurface(); return; }
        if (!enableContinuousRiverSurface) { DestroyRiverSurface(); return; }
        if (planetGenerator == null || grid == null || !grid.IsBuilt) { DestroyRiverSurface(); return; }
        if (waterMaterial == null || heightmapTexture == null || bakeResult.lut == null || bakeResult.lut.Length == 0) { DestroyRiverSurface(); return; }

        int wCells = Mathf.Clamp(riverSdfWidth, 64, 4096);
        int hCells = Mathf.Clamp(riverSdfHeight, 32, 4096);
        int wPts = wCells + 1;
        int hPts = hCells + 1;

        float dx = mapWidth / wCells;
        float dz = mapHeight / hCells;
        float diag = Mathf.Sqrt(dx * dx + dz * dz);

        EnsureHexCorners();
        float hexSize = ComputeHexSize();
        float isoRiver = Mathf.Max(0.05f, hexSize * Mathf.Max(0.01f, riverHalfWidthMultiplier));
        float isoLake = Mathf.Max(0.05f, hexSize * Mathf.Max(0.01f, lakeHalfWidthMultiplier));
        // Prevent sub-cell widths which alias into hairline strands at a given SDF resolution.
        float minIso = Mathf.Max(dx, dz) * 1.5f;
        isoRiver = Mathf.Max(isoRiver, minIso);
        isoLake = Mathf.Max(isoLake, minIso);

        // --- Build seed grids for rivers + lakes (plus some midpoints for rivers so they don't look dotted) ---
        var seedRiver = new bool[wPts * hPts];
        var seedLake = continuousWaterIncludesLakes ? new bool[wPts * hPts] : null;
        var ownerRiver = new int[wPts * hPts];
        for (int i = 0; i < ownerRiver.Length; i++) ownerRiver[i] = -1;
        int[] ownerLake = null;
        if (seedLake != null)
        {
            ownerLake = new int[wPts * hPts];
            for (int i = 0; i < ownerLake.Length; i++) ownerLake[i] = -1;
        }

        // Helper: mark a seed at UV (0..1)
        void MarkSeed(bool[] seed, int[] owner, float u, float v, int tileIndex)
        {
            u = Mathf.Repeat(u, 1f);
            v = Mathf.Clamp01(v);
            int px = Mathf.Clamp(Mathf.RoundToInt(u * wCells), 0, wCells);
            int py = Mathf.Clamp(Mathf.RoundToInt(v * hCells), 0, hCells);
            int idx = py * wPts + px;
            seed[idx] = true;
            if (owner != null && owner[idx] < 0) owner[idx] = tileIndex; // keep first owner
        }

        // Mark tile centers and some segment samples so rivers don't appear "dotted"
        for (int ti = 0; ti < grid.TileCount; ti++)
        {
            if (!planetGenerator.data.TryGetValue(ti, out var td)) continue;
            Vector3 c = grid.tileCenters[ti];
            float u0 = (c.x / mapWidth) + 0.5f;
            float v0 = (c.z / mapHeight) + 0.5f;

            if (td.waterType == TileWaterType.River)
            {
                MarkSeed(seedRiver, ownerRiver, u0, v0, ti);

                // Sample toward river neighbors for continuity
                var nbrs = grid.neighbors[ti];
                if (nbrs != null)
                {
                    foreach (int n in nbrs)
                    {
                        if (n < 0 || n >= grid.TileCount) continue;
                        if (!planetGenerator.data.TryGetValue(n, out var nd) || nd.waterType != TileWaterType.River) continue;
                        Vector3 nc = grid.tileCenters[n];
                        // 2 samples along the segment
                        for (int s = 1; s <= 2; s++)
                        {
                            float t = s / 3f;
                            Vector3 p = Vector3.Lerp(c, nc, t);
                            float uu = (p.x / mapWidth) + 0.5f;
                            float vv = (p.z / mapHeight) + 0.5f;
                            MarkSeed(seedRiver, ownerRiver, uu, vv, ti);
                        }
                    }
                }
            }
            else if (continuousWaterIncludesLakes && td.waterType == TileWaterType.Lake)
            {
                MarkSeed(seedLake, ownerLake, u0, v0, ti);
            }
        }

        // If no inland-water seeds, remove mesh
        bool anySeed = false;
        for (int i = 0; i < seedRiver.Length; i++) { if (seedRiver[i]) { anySeed = true; break; } }
        if (!anySeed && seedLake != null)
            for (int i = 0; i < seedLake.Length; i++) { if (seedLake[i]) { anySeed = true; break; } }
        if (!anySeed) { DestroyRiverSurface(); return; }

        // --- Approximate Euclidean distance transform (2-pass chamfer) in WORLD units ---
        float INF = 1e20f;
        var distRiver = new float[wPts * hPts];
        for (int i = 0; i < distRiver.Length; i++) distRiver[i] = seedRiver[i] ? 0f : INF;
        float[] distLake = null;
        if (seedLake != null)
        {
            distLake = new float[wPts * hPts];
            for (int i = 0; i < distLake.Length; i++) distLake[i] = seedLake[i] ? 0f : INF;
        }

        void DistanceTransformInPlace(float[] distArr, int[] ownerArr)
        {
            // Forward pass
            for (int y = 0; y < hPts; y++)
            {
                int row = y * wPts;
                for (int x = 0; x < wPts; x++)
                {
                    int idx = row + x;
                    float d = distArr[idx];
                    int bestOwner = ownerArr != null ? ownerArr[idx] : -1;

                    if (x > 0)
                    {
                        int n = idx - 1;
                        float nd = distArr[n] + dx;
                        if (nd < d && (ownerArr == null || ownerArr[n] >= 0)) { d = nd; if (ownerArr != null) bestOwner = ownerArr[n]; }
                    }
                    if (y > 0)
                    {
                        int n = idx - wPts;
                        float nd = distArr[n] + dz;
                        if (nd < d && (ownerArr == null || ownerArr[n] >= 0)) { d = nd; if (ownerArr != null) bestOwner = ownerArr[n]; }
                    }
                    if (x > 0 && y > 0)
                    {
                        int n = idx - wPts - 1;
                        float nd = distArr[n] + diag;
                        if (nd < d && (ownerArr == null || ownerArr[n] >= 0)) { d = nd; if (ownerArr != null) bestOwner = ownerArr[n]; }
                    }
                    if (x < wPts - 1 && y > 0)
                    {
                        int n = idx - wPts + 1;
                        float nd = distArr[n] + diag;
                        if (nd < d && (ownerArr == null || ownerArr[n] >= 0)) { d = nd; if (ownerArr != null) bestOwner = ownerArr[n]; }
                    }
                    distArr[idx] = d;
                    if (ownerArr != null) ownerArr[idx] = bestOwner;
                }
            }
            // Backward pass
            for (int y = hPts - 1; y >= 0; y--)
            {
                int row = y * wPts;
                for (int x = wPts - 1; x >= 0; x--)
                {
                    int idx = row + x;
                    float d = distArr[idx];
                    int bestOwner = ownerArr != null ? ownerArr[idx] : -1;

                    if (x < wPts - 1)
                    {
                        int n = idx + 1;
                        float nd = distArr[n] + dx;
                        if (nd < d && (ownerArr == null || ownerArr[n] >= 0)) { d = nd; if (ownerArr != null) bestOwner = ownerArr[n]; }
                    }
                    if (y < hPts - 1)
                    {
                        int n = idx + wPts;
                        float nd = distArr[n] + dz;
                        if (nd < d && (ownerArr == null || ownerArr[n] >= 0)) { d = nd; if (ownerArr != null) bestOwner = ownerArr[n]; }
                    }
                    if (x < wPts - 1 && y < hPts - 1)
                    {
                        int n = idx + wPts + 1;
                        float nd = distArr[n] + diag;
                        if (nd < d && (ownerArr == null || ownerArr[n] >= 0)) { d = nd; if (ownerArr != null) bestOwner = ownerArr[n]; }
                    }
                    if (x > 0 && y < hPts - 1)
                    {
                        int n = idx + wPts - 1;
                        float nd = distArr[n] + diag;
                        if (nd < d && (ownerArr == null || ownerArr[n] >= 0)) { d = nd; if (ownerArr != null) bestOwner = ownerArr[n]; }
                    }
                    distArr[idx] = d;
                    if (ownerArr != null) ownerArr[idx] = bestOwner;
                }
            }
        }

        DistanceTransformInPlace(distRiver, ownerRiver);
        if (distLake != null) DistanceTransformInPlace(distLake, ownerLake);

        // Scalar field: f = min(distRiver - isoRiver, distLake - isoLake). Inside when f <= 0.
        float FAt(int ix, int iy)
        {
            int idx = iy * wPts + ix;
            float fR = distRiver[idx] - isoRiver;
            if (distLake == null) return fR;
            float fL = distLake[idx] - isoLake;
            return Mathf.Min(fR, fL);
        }

        // --- Marching squares filled mesh for inside region (dist <= iso) ---
        var verts = new System.Collections.Generic.List<Vector3>(65536);
        var cols = new System.Collections.Generic.List<Color>(65536);
        var norms = new System.Collections.Generic.List<Vector3>(65536);
        var tris = new System.Collections.Generic.List<int>(131072);

        int[] cornerVert = new int[wPts * hPts];
        for (int i = 0; i < cornerVert.Length; i++) cornerVert[i] = -1;

        int[] horizEdge = new int[wCells * (hCells + 1)];        // edge between (x,y) and (x+1,y)
        int[] vertEdge = new int[(wCells + 1) * hCells];         // edge between (x,y) and (x,y+1)
        for (int i = 0; i < horizEdge.Length; i++) horizEdge[i] = -1;
        for (int i = 0; i < vertEdge.Length; i++) vertEdge[i] = -1;

        int lutW = bakeResult.width > 0 ? bakeResult.width : textureWidth;
        int lutH = bakeResult.height > 0 ? bakeResult.height : textureHeight;

        bool IsLakeAt(float u, float v)
        {
            if (distLake == null) return false;
            u = Mathf.Repeat(u, 1f);
            v = Mathf.Clamp01(v);
            int ix = Mathf.Clamp(Mathf.RoundToInt(u * wCells), 0, wCells);
            int iy = Mathf.Clamp(Mathf.RoundToInt(v * hCells), 0, hCells);
            int idx = iy * wPts + ix;
            float fR = distRiver[idx] - isoRiver;
            float fL = distLake[idx] - isoLake;
            return fL <= fR;
        }

        Color SampleInlandWaterColor(float u, float v)
        {
            // IMPORTANT: Don't rely on the LUT for water-vs-land classification here.
            // The inland mesh covers sub-tile points; the LUT can map those points to a nearby LAND tile,
            // which makes lakes/rivers look "unfilled" or misaligned. Use the SDF to classify lake vs river.
            if (IsLakeAt(u, v))
                return new Color(0.5f, 0.5f, 0f, 2f / 3f); // lake alpha matches existing per-tile encoding

            // River: pick flow direction from nearest propagated river seed tile.
            u = Mathf.Repeat(u, 1f);
            v = Mathf.Clamp01(v);
            int ix = Mathf.Clamp(Mathf.RoundToInt(u * wCells), 0, wCells);
            int iy = Mathf.Clamp(Mathf.RoundToInt(v * hCells), 0, hCells);
            int idx = iy * wPts + ix;
            int tIndex = (ownerRiver != null) ? ownerRiver[idx] : -1;
            if (tIndex >= 0 && planetGenerator.data.TryGetValue(tIndex, out var td) && td.waterType == TileWaterType.River)
                return new Color(td.riverFlowDirXZ.x * 0.5f + 0.5f, td.riverFlowDirXZ.y * 0.5f + 0.5f, 0f, 1f);

            return new Color(0.5f, 0.5f, 0f, 1f);
        }

        float SampleInlandWaterY(float u, float v)
        {
            // Prefer tile waterElevation so lakes stay flat and rivers stay coherent.
            u = Mathf.Repeat(u, 1f);
            v = Mathf.Clamp01(v);
            bool wantLake = IsLakeAt(u, v);
            int ix = Mathf.Clamp(Mathf.RoundToInt(u * wCells), 0, wCells);
            int iy = Mathf.Clamp(Mathf.RoundToInt(v * hCells), 0, hCells);
            int idx = iy * wPts + ix;
            int tIndex = -1;
            if (wantLake && ownerLake != null) tIndex = ownerLake[idx];
            else if (ownerRiver != null) tIndex = ownerRiver[idx];

            float waterY;
            if (tIndex >= 0 && planetGenerator.data.TryGetValue(tIndex, out var td) && (td.waterType == TileWaterType.River || td.waterType == TileWaterType.Lake))
                waterY = flatY + td.waterElevation * displacementStrength + waterYOffset + riverSurfaceLift;
            else
            {
                // Fallback to hugging terrain
                float elev = heightmapTexture != null ? heightmapTexture.GetPixelBilinear(u, v).r : 0f;
                waterY = flatY + elev * displacementStrength + waterYOffset + riverSurfaceLift;
            }

            // Ensure water is never below terrain: at boundaries the SDF extends water into
            // cells where terrain (from heightmap bilinear) can be higher than tile water.
            float terrainY = heightmapTexture != null
                ? flatY + heightmapTexture.GetPixelBilinear(u, v).r * displacementStrength
                : float.MinValue;
            return terrainY > float.MinValue ? Mathf.Max(waterY, terrainY + 0.01f) : waterY;
        }

        int GetCorner(int x, int y)
        {
            int idx = y * wPts + x;
            int vi = cornerVert[idx];
            if (vi >= 0) return vi;

            float u = (float)x / wCells;
            float v = (float)y / hCells;
            float wx = (u - 0.5f) * mapWidth;
            float wz = (v - 0.5f) * mapHeight;
            float wy = SampleInlandWaterY(u, v);

            vi = verts.Count;
            verts.Add(new Vector3(wx, wy, wz));
            cols.Add(SampleInlandWaterColor(u, v));
            norms.Add(Vector3.up);
            cornerVert[idx] = vi;
            return vi;
        }

        int GetHoriz(int x, int y) // between (x,y) and (x+1,y), x in [0..wCells-1], y in [0..hCells]
        {
            int ei = y * wCells + x;
            int vi = horizEdge[ei];
            if (vi >= 0) return vi;

            float f0 = FAt(x, y);
            float f1 = FAt(x + 1, y);
            float t = (Mathf.Abs(f1 - f0) < 1e-6f) ? 0.5f : Mathf.Clamp01((0f - f0) / (f1 - f0));

            float u = (x + t) / wCells;
            float v = (float)y / hCells;
            float wx = (u - 0.5f) * mapWidth;
            float wz = (v - 0.5f) * mapHeight;
            float wy = SampleInlandWaterY(u, v);

            vi = verts.Count;
            verts.Add(new Vector3(wx, wy, wz));
            cols.Add(SampleInlandWaterColor(u, v));
            norms.Add(Vector3.up);
            horizEdge[ei] = vi;
            return vi;
        }

        int GetVert(int x, int y) // between (x,y) and (x,y+1), x in [0..wCells], y in [0..hCells-1]
        {
            int ei = y * (wCells + 1) + x;
            int vi = vertEdge[ei];
            if (vi >= 0) return vi;

            float f0 = FAt(x, y);
            float f1 = FAt(x, y + 1);
            float t = (Mathf.Abs(f1 - f0) < 1e-6f) ? 0.5f : Mathf.Clamp01((0f - f0) / (f1 - f0));

            float u = (float)x / wCells;
            float v = (y + t) / hCells;
            float wx = (u - 0.5f) * mapWidth;
            float wz = (v - 0.5f) * mapHeight;
            float wy = SampleInlandWaterY(u, v);

            vi = verts.Count;
            verts.Add(new Vector3(wx, wy, wz));
            cols.Add(SampleInlandWaterColor(u, v));
            norms.Add(Vector3.up);
            vertEdge[ei] = vi;
            return vi;
        }

        void AddTri(int a, int b, int c3)
        {
            tris.Add(a);
            tris.Add(b);
            tris.Add(c3);
        }

        void AddPoly(params int[] poly)
        {
            if (poly == null || poly.Length < 3) return;
            int a = poly[0];
            for (int i = 1; i < poly.Length - 1; i++)
            {
                AddTri(a, poly[i], poly[i + 1]);
            }
        }

        // Only needed for ambiguous cases (5/10) to avoid concave fan triangles.
        int GetCenter(int x, int y)
        {
            float u = (x + 0.5f) / wCells;
            float v = (y + 0.5f) / hCells;
            float wx = (u - 0.5f) * mapWidth;
            float wz = (v - 0.5f) * mapHeight;
            float wy = SampleInlandWaterY(u, v);
            int vi = verts.Count;
            verts.Add(new Vector3(wx, wy, wz));
            cols.Add(SampleInlandWaterColor(u, v));
            norms.Add(Vector3.up);
            return vi;
        }

        for (int y = 0; y < hCells; y++)
        {
            for (int x = 0; x < wCells; x++)
            {
                // Corners: BL, BR, TR, TL
                float fBL = FAt(x, y);
                float fBR = FAt(x + 1, y);
                float fTR = FAt(x + 1, y + 1);
                float fTL = FAt(x, y + 1);

                bool inBL = fBL <= 0f;
                bool inBR = fBR <= 0f;
                bool inTR = fTR <= 0f;
                bool inTL = fTL <= 0f;

                int c = (inBL ? 1 : 0) | (inBR ? 2 : 0) | (inTR ? 4 : 0) | (inTL ? 8 : 0);
                if (c == 0) continue;

                int vBL = -1, vBR = -1, vTR = -1, vTL = -1;
                if (inBL) vBL = GetCorner(x, y);
                if (inBR) vBR = GetCorner(x + 1, y);
                if (inTR) vTR = GetCorner(x + 1, y + 1);
                if (inTL) vTL = GetCorner(x, y + 1);

                int eB = -1, eR = -1, eT = -1, eL = -1;
                if (inBL != inBR) eB = GetHoriz(x, y);
                if (inBR != inTR) eR = GetVert(x + 1, y);
                if (inTL != inTR) eT = GetHoriz(x, y + 1);
                if (inBL != inTL) eL = GetVert(x, y);

                // Saddle disambiguation (cases 5 and 10)
                bool centerInside = false;
                if (c == 5 || c == 10)
                {
                    float fC = (fBL + fBR + fTR + fTL) * 0.25f;
                    centerInside = fC <= 0f;
                }

                switch (c)
                {
                    case 1:  AddPoly(vBL, eB, eL); break;
                    case 2:  AddPoly(vBR, eR, eB); break;
                    case 3:  AddPoly(vBL, vBR, eR, eL); break;
                    case 4:  AddPoly(vTR, eT, eR); break;
                    case 5:
                        if (centerInside)
                        {
                            // Fill the connected hourglass using a center vertex to avoid concave fan triangles.
                            int vC = GetCenter(x, y);
                            AddTri(vBL, eB, vC);
                            AddTri(vBL, vC, eL);
                            AddTri(vC, eB, eR);
                            AddTri(vC, eR, vTR);
                            AddTri(vC, vTR, eT);
                            AddTri(vC, eT, eL);
                        }
                        else { AddPoly(vBL, eB, eL); AddPoly(vTR, eT, eR); }
                        break;
                    case 6:  AddPoly(vBR, vTR, eT, eB); break;
                    case 7:  AddPoly(vBL, vBR, vTR, eT, eL); break;
                    case 8:  AddPoly(vTL, eL, eT); break;
                    case 9:  AddPoly(vBL, eB, eT, vTL); break;
                    case 10:
                        if (centerInside)
                        {
                            int vC = GetCenter(x, y);
                            AddTri(vBR, eR, vC);
                            AddTri(vBR, vC, eB);
                            AddTri(vC, eR, eT);
                            AddTri(vC, eT, vTL);
                            AddTri(vC, vTL, eL);
                            AddTri(vC, eL, eB);
                        }
                        else { AddPoly(vBR, eR, eB); AddPoly(vTL, eL, eT); }
                        break;
                    case 11: AddPoly(vBL, vBR, eR, eT, vTL); break;
                    case 12: AddPoly(vTL, vTR, eR, eL); break;
                    case 13: AddPoly(vBL, eB, eR, vTR, vTL); break;
                    case 14: AddPoly(vBR, vTR, vTL, eL, eB); break;
                    case 15: AddPoly(GetCorner(x, y), GetCorner(x + 1, y), GetCorner(x + 1, y + 1), GetCorner(x, y + 1)); break;
                }
            }
        }

        if (tris.Count < 3)
        {
            DestroyRiverSurface();
            return;
        }

        // Optionally extrude the top surface into a closed 3D volume (walls + bottom).
        if (extrudeInlandWaterToVolume)
        {
            float depth = Mathf.Max(0.01f, inlandWaterVolumeDepth);

            int nTop = verts.Count;
            var v2 = new System.Collections.Generic.List<Vector3>(nTop * 2);
            var c2 = new System.Collections.Generic.List<Color>(nTop * 2);
            var t2 = new System.Collections.Generic.List<int>(tris.Count * 2 + 65536);

            v2.AddRange(verts);
            c2.AddRange(cols);

            for (int i = 0; i < nTop; i++)
            {
                Vector3 p = verts[i];
                v2.Add(new Vector3(p.x, p.y - depth, p.z));
                c2.Add(cols[i]);
            }

            // Top faces
            t2.AddRange(tris);

            // Bottom faces (reverse winding)
            for (int i = 0; i < tris.Count; i += 3)
            {
                int a = tris[i] + nTop;
                int b = tris[i + 1] + nTop;
                int c = tris[i + 2] + nTop;
                t2.Add(a);
                t2.Add(c);
                t2.Add(b);
            }

            // Boundary edges -> side walls
            ulong Key(int a, int b)
            {
                uint aa = (uint)Mathf.Min(a, b);
                uint bb = (uint)Mathf.Max(a, b);
                return ((ulong)aa << 32) | (ulong)bb;
            }

            var edgeCount = new System.Collections.Generic.Dictionary<ulong, int>(tris.Count);
            var edgeDir = new System.Collections.Generic.Dictionary<ulong, Vector2Int>(tris.Count);

            void AccEdge(int a, int b)
            {
                ulong k = Key(a, b);
                if (edgeCount.TryGetValue(k, out int cnt)) edgeCount[k] = cnt + 1;
                else edgeCount[k] = 1;
                if (!edgeDir.ContainsKey(k)) edgeDir[k] = new Vector2Int(a, b); // keep one direction for wall build
            }

            for (int i = 0; i < tris.Count; i += 3)
            {
                int a = tris[i];
                int b = tris[i + 1];
                int c = tris[i + 2];
                AccEdge(a, b);
                AccEdge(b, c);
                AccEdge(c, a);
            }

            foreach (var kvp in edgeCount)
            {
                if (kvp.Value != 1) continue; // interior edge
                Vector2Int e = edgeDir[kvp.Key];
                int a = e.x;
                int b = e.y;
                int a2 = a + nTop;
                int b2 = b + nTop;

                // Quad as two triangles. Cull is off on the water shader, so exact winding isn't critical,
                // but we keep a consistent ordering for normal calculation.
                t2.Add(a);
                t2.Add(b);
                t2.Add(b2);
                t2.Add(a);
                t2.Add(b2);
                t2.Add(a2);
            }

            verts = v2;
            cols = c2;
            tris = t2;
            norms = null; // we'll recalc for volume
        }

        EnsureRiverSurfaceObject();
        if (_riverSurfaceMesh == null) _riverSurfaceMesh = new Mesh();
        _riverSurfaceMesh.Clear();
        _riverSurfaceMesh.name = extrudeInlandWaterToVolume ? "InlandWaterVolume" : "ContinuousRiverSurface";
        _riverSurfaceMesh.SetVertices(verts);
        _riverSurfaceMesh.SetColors(cols);
        _riverSurfaceMesh.SetTriangles(tris, 0);
        if (norms != null && norms.Count == verts.Count) _riverSurfaceMesh.SetNormals(norms);
        else _riverSurfaceMesh.RecalculateNormals();
        _riverSurfaceMesh.RecalculateBounds();

        var mf = _riverSurfaceObj.GetComponent<MeshFilter>();
        mf.sharedMesh = _riverSurfaceMesh;
    }

    private void EnsureRiverSurfaceObject()
    {
        if (_riverSurfaceObj == null)
        {
            string objName =
                extrudeInlandWaterToVolume
                    ? (continuousWaterIncludesLakes ? "InlandWaterVolume" : "RiverVolume")
                    : (continuousWaterIncludesLakes ? "InlandWaterSurface" : "RiverSurface");

            _riverSurfaceObj = new GameObject(objName);
            _riverSurfaceObj.transform.SetParent(transform, false);
            _riverSurfaceObj.transform.localPosition = Vector3.zero;
            _riverSurfaceObj.transform.localRotation = Quaternion.identity;
            _riverSurfaceObj.transform.localScale = Vector3.one;
            _riverSurfaceObj.layer = gameObject.layer;

            var mf = _riverSurfaceObj.AddComponent<MeshFilter>();
            var mr = _riverSurfaceObj.AddComponent<MeshRenderer>();
            mr.sharedMaterial = waterMaterial; // same water shader; river-ness comes from vertexColor.a
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.allowOcclusionWhenDynamic = false;
        }
        else
        {
            string objName =
                extrudeInlandWaterToVolume
                    ? (continuousWaterIncludesLakes ? "InlandWaterVolume" : "RiverVolume")
                    : (continuousWaterIncludesLakes ? "InlandWaterSurface" : "RiverSurface");

            if (_riverSurfaceObj.name != objName) _riverSurfaceObj.name = objName;
            var mr = _riverSurfaceObj.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = waterMaterial;
        }
    }

    private void DestroyRiverSurface()
    {
        if (_riverSurfaceObj != null)
        {
            DestroyImmediate(_riverSurfaceObj);
            _riverSurfaceObj = null;
        }
        if (_riverSurfaceMesh != null)
        {
            DestroyImmediate(_riverSurfaceMesh);
            _riverSurfaceMesh = null;
        }
    }

    /// <summary>
    /// Copy a named child mesh (Water or Foam) from a source chunk to a ghost chunk.
    /// Used by CreateGhostColumn to duplicate water surfaces for seamless wrap.
    /// </summary>
    private void CopyChildMeshToGhost(Transform sourceChunk, Transform ghostChunk, string childName, Material mat)
    {
        if (mat == null) return;
        Transform sourceChild = sourceChunk.Find(childName);
        if (sourceChild == null) return;

        MeshFilter srcMF = sourceChild.GetComponent<MeshFilter>();
        if (srcMF == null || srcMF.sharedMesh == null) return;

        GameObject ghostChild = new GameObject(childName);
        ghostChild.transform.SetParent(ghostChunk, false);
        ghostChild.transform.localPosition = sourceChild.localPosition;
        ghostChild.transform.localRotation = sourceChild.localRotation;
        ghostChild.transform.localScale = sourceChild.localScale;
        ghostChild.layer = sourceChild.gameObject.layer;

        var mf = ghostChild.AddComponent<MeshFilter>();
        mf.sharedMesh = srcMF.sharedMesh;

        var mr = ghostChild.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.allowOcclusionWhenDynamic = false;
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

            // Copy Water and Foam child meshes for seamless water wrap
            CopyChildMeshToGhost(sourceChunk.transform, ghostChunk.transform, "Water", waterMaterial);
            CopyChildMeshToGhost(sourceChunk.transform, ghostChunk.transform, "Foam", foamMaterial);
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
        if (!enableSeasonMasks) return;
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
        if (!enableSeasonMasks) return;
        if (planetGenerator == null) return;
        var climateManager = GameManager.Instance != null
            ? GameManager.Instance.GetClimateManager(planetGenerator.planetIndex)
            : ClimateManager.Instance;
        if (climateManager == null) return;

        UpdateSeasonMasksForSeason(climateManager.GetSeasonForPlanet(planetGenerator.planetIndex));
    }

    private void UpdateSeasonMasksForSeason(Season season)
    {
        if (!enableSeasonMasks) return;
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
    /// Rebuild water + foam meshes for the chunk containing <paramref name="tileIndex"/>.
    /// Use this when a tile's water-ness changes (e.g. becomes Coast/Seas/Ocean/Lake/River) after the initial build.
    /// </summary>
    public void RebuildWaterForTile(int tileIndex)
    {
        if (chunks == null || planetGenerator == null) return;
        if (!tileToChunk.TryGetValue(tileIndex, out HexMapChunk chunk) || chunk == null) return;

        BuildWaterMeshForChunk(chunk);
        BuildFoamEdgeMeshForChunk(chunk);
        // Rivers are rendered as a single continuous mesh when enabled.
        // Rebuild the whole river surface if a river tile changed (cheap at low SDF resolution).
        if (enableContinuousRiverSurface)
        {
            try
            {
                if (planetGenerator.data.TryGetValue(tileIndex, out var td) &&
                    (td.waterType == TileWaterType.River || (continuousWaterIncludesLakes && td.waterType == TileWaterType.Lake)))
                    BuildContinuousRiverSurfaceMesh();
            }
            catch { /* ignore */ }
        }
        // NOTE: Ghost columns copy Water/Foam at creation time; if you dynamically change coast/water at runtime
        // near map edges, we may also need to refresh ghost meshes.
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

        if (enableSeasonMasks)
        {
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
    }
    
    /// <summary>
    /// Rebuild the baked texture (e.g., after terrain changes).
    /// </summary>
    public void RebakeTexture()
    {
        if (planetGenerator == null) return;
        
        BakeTexture();
        BuildBiomeVisualMaps();
        
        ApplyBiomeMaterialSettings();
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
        // Release GPU resources allocated by this manager (textures, arrays, RTs, buffers)
        ReleaseGpuResources();

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

    /// <summary>
    /// Explicitly release GPU/native resources held by this manager.
    /// Call this before unloading or switching planets to free VRAM and native memory.
    /// </summary>
    public void ReleaseGpuResources()
    {
        // Clear textures from material so shader doesn't hold native refs
        if (sharedMaterial != null)
        {
            sharedMaterial.SetTexture("_BiomeAlbedoArray", null);
            sharedMaterial.SetTexture("_BiomeNormalArray", null);
            sharedMaterial.SetTexture("_BiomeMaskArray", null);
            sharedMaterial.SetTexture("_SurfaceEmissiveArray", null);
            sharedMaterial.SetTexture("_BiomeIndexMap", null);
            sharedMaterial.SetTexture("_Heightmap", null);
            sharedMaterial.SetTexture("_BiomeSurfaceMapTex", null);
            sharedMaterial.SetTexture("_BiomeEmissiveMapTex", null);
            sharedMaterial.SetTexture("_LUT", null);
            sharedMaterial.SetTexture("_SliceToBiomeMap", null);
        }

        // Destroy Texture2DArray / Texture2D resources
        if (biomeAlbedoArray != null) { UnityEngine.Object.DestroyImmediate(biomeAlbedoArray); biomeAlbedoArray = null; }
        if (biomeNormalArray != null) { UnityEngine.Object.DestroyImmediate(biomeNormalArray); biomeNormalArray = null; }
        if (biomeMaskArray != null) { UnityEngine.Object.DestroyImmediate(biomeMaskArray); biomeMaskArray = null; }
        if (biomeEmissiveArray != null) { UnityEngine.Object.DestroyImmediate(biomeEmissiveArray); biomeEmissiveArray = null; }

        if (biomeIndexMap != null) { UnityEngine.Object.DestroyImmediate(biomeIndexMap); biomeIndexMap = null; }
        if (heightmapTexture != null) { UnityEngine.Object.DestroyImmediate(heightmapTexture); heightmapTexture = null; }
        if (biomeSurfaceMapTexture != null) { UnityEngine.Object.DestroyImmediate(biomeSurfaceMapTexture); biomeSurfaceMapTexture = null; }
        if (biomeEmissiveMapTexture != null) { UnityEngine.Object.DestroyImmediate(biomeEmissiveMapTexture); biomeEmissiveMapTexture = null; }
        if (lutTexture != null) { UnityEngine.Object.DestroyImmediate(lutTexture); lutTexture = null; }
        if (sliceToBiomeMap != null) { UnityEngine.Object.DestroyImmediate(sliceToBiomeMap); sliceToBiomeMap = null; }

        // Release RenderTextures from bakeResult (if present)
        try
        {
            if (bakeResult.texture != null) { bakeResult.texture.Release(); UnityEngine.Object.DestroyImmediate(bakeResult.texture); bakeResult.texture = null; }
        }
        catch { }
        try
        {
            if (bakeResult.heightmap != null) { bakeResult.heightmap.Release(); UnityEngine.Object.DestroyImmediate(bakeResult.heightmap); bakeResult.heightmap = null; }
        }
        catch { }
        try
        {
            if (bakeResult.normalmap != null) { bakeResult.normalmap.Release(); UnityEngine.Object.DestroyImmediate(bakeResult.normalmap); bakeResult.normalmap = null; }
        }
        catch { }

        // Clear cached GPU resources in the baker (compute buffers, cached arrays)
        try { PlanetTextureBaker.ClearAllCaches(); } catch { }
    }
    
    #endregion
    
    private void OnDestroy()
    {
        // OnDisable handles event unsubscription; clean up overlay and chunks here
        if (overlayTileSystem != null)
        {
            overlayTileSystem.OnTileOwnerChanged -= HandleTileOwnerChanged;
            overlayTileSystem.OnFogChanged -= HandleFogChanged;
            overlayTileSystem = null;
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
