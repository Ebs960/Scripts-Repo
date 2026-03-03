using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// Burst job that fills a BiomeIndexMap texture (RFloat) from a pre-computed tile-to-slice lookup.
/// Each pixel reads the LUT for its tile index, then looks up the slice in a flat array.
/// </summary>
[BurstCompile]
struct FillBiomeIndexMapJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<int> lut;
    [ReadOnly] public NativeArray<int> tileSliceIndex;
    public NativeArray<float> pixels;

    public void Execute(int i)
    {
        int tileIndex = lut[i];
        if (tileIndex >= 0 && tileIndex < tileSliceIndex.Length)
            pixels[i] = (float)tileSliceIndex[tileIndex];
        else
            pixels[i] = 0f;
    }
}

/// <summary>
/// Burst job that fills a Heightmap texture (RHalf) from a pre-computed tile-to-elevation lookup.
/// Writes raw half-float bits so the output can go directly to SetPixelData.
/// </summary>
[BurstCompile]
struct FillHeightmapJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<int> lut;
    [ReadOnly] public NativeArray<float> tileElevation;
    public NativeArray<ushort> pixels;

    public void Execute(int i)
    {
        int tileIndex = lut[i];
        float elevation = 0f;
        if (tileIndex >= 0 && tileIndex < tileElevation.Length)
            elevation = tileElevation[tileIndex];
        pixels[i] = (ushort)math.f32tof16(elevation);
    }
}

/// <summary>
/// Burst job that encodes a tile-index LUT into an RGB24 texture (3 bytes per pixel).
/// </summary>
[BurstCompile]
struct EncodeLUTTextureJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<int> lut;
    [NativeDisableParallelForRestriction]
    public NativeArray<byte> pixels;

    public void Execute(int i)
    {
        int tileIndex = lut[i];
        int offset = i * 3;
        pixels[offset]     = (byte)(tileIndex & 0xFF);
        pixels[offset + 1] = (byte)((tileIndex >> 8) & 0xFF);
        pixels[offset + 2] = (byte)((tileIndex >> 16) & 0xFF);
    }
}

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
    [Tooltip("Anisotropic level to apply to the runtime generated heightmap texture.")]
    [SerializeField]
    [Range(0,16)]
    private int heightmapAnisoLevel = 4;
    
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
    
    [Header("Rendering Options")]
    [Tooltip("When true, preserve land tile elevations adjacent to lakes/rivers by using the original pre-water elevation for rendering.")]
    [SerializeField] private bool preserveLandElevationNearFreshwater = true;

    [Header("Biome Visual Modifiers")]
    [Range(0f, 1f)]
    [SerializeField] private float globalSnowAmount = 0f;
    [Range(0f, 1f)]
    [SerializeField] private float globalWetness = 0f;
    [Header("Material Channel Multipliers")]
    [Range(0f, 2f)]
    [SerializeField]
    [Tooltip("Multiplier applied to metallic channel from biome mask")]
    private float metallicMultiplier = 1.0f;
    [Range(0f, 2f)]
    [SerializeField]
    [Tooltip("Multiplier applied to AO channel from biome mask")]
    private float aoIntensity = 1.0f;
    [Range(0f, 2f)]
    [SerializeField]
    [Tooltip("Multiplier applied to smoothness channel from biome mask")]
    private float smoothnessMultiplier = 1.0f;
    
    [Header("Triplanar Settings")]
    [Tooltip("Triplanar tiling scale — controls how large biome textures appear on terrain. Lower = larger textures.")]
    [Range(0.01f, 5f)]
    [SerializeField] private float triplanarTiling = 2f;
    [Tooltip("Triplanar blend sharpness — higher values make the blend between projection axes sharper. Lower = smoother.")]
    [Range(1f, 20f)]
    [SerializeField] private float triplanarBlend = 6f;
    [SerializeField]
    [Tooltip("When true, use triplanar/hex-tiled sampling; when false, use simple Y-planar sampling.")]
    private bool useTriplanar = true;
    
    [Header("Normals & Biome Blending")]
    [Tooltip("Strength multiplier for sampled normals from biome normal array")]
    [Range(0.01f, 5f)]
    [SerializeField] private float normalStrength = 1.0f;
    [Tooltip("Radius (in texels) used when sampling normals/heightmap for normal computation")]
    [Range(1f, 12f)]
    [SerializeField] private float normalSampleRadius = 4f;
    [Tooltip("Radius (in texels) used for biome blending between neighboring biome slices")]
    [Range(0f, 16f)]
    [SerializeField] private float biomeBlendRadius = 4f;
    [Tooltip("Blend sharpness used when blending biome surfaces by height")]
    [Range(0.01f, 10f)]
    [SerializeField] private float biomeBlendSharpness = 3f;

    [Header("Micro Detail (Detail Maps)")]
    [Tooltip("Detail albedo (tileable) sampled on top of biome albedo")]
    [SerializeField] private Texture2D detailAlbedoMap = null;
    [Tooltip("Detail normal map (tangent-space). Mark as Normal Map in importer")]
    [SerializeField] private Texture2D detailNormalMap = null;
    [Tooltip("Tiling for the detail textures (independent of triplanar tiling)")]
    [Range(1f, 100f)]
    [SerializeField] private float detailTiling = 20f;
    [Tooltip("Strength of detail albedo modulation")]
    [Range(0f, 10f)]
    [SerializeField] private float detailStrength = 0.3f;
    [Tooltip("Strength of detail normal perturbation")]
    [Range(0f, 10f)]
    [SerializeField] private float detailNormalStrength = 0.5f;
    [Tooltip("Camera distance where detail begins to fade (meters)")]
    [SerializeField] private float detailFadeStart = 5f;
    [Tooltip("Camera distance where detail is fully faded out (meters)")]
    [SerializeField] private float detailFadeEnd = 50f;
    
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
    [Tooltip("When enabled, logs detailed water/SDF diagnostics: pre-build tile counts, SDF seed counts, per-chunk mesh stats, post-build summary. Helps diagnose gaps or missing water.")]
    [SerializeField] private bool debugWaterVerbose = false;
    private Vector3 _lastTransformPos;
    private Quaternion _lastTransformRot;
    private Vector3 _lastTransformScale;
    
    // NOTE: Hex grid overlay was removed - shader graph doesn't support it.
    // To add hex grid, create a separate HexGridOverlay script using line renderers or decals.
    
    [Header("Water Mesh System")]
    [Tooltip("Material for chunk-based water tiles (lakes, ocean, rivers). Assign SG_WaterTile material.")]
    [SerializeField] private Material waterMaterial;
    [Tooltip("Small Y offset above the computed water surface to prevent z-fighting with terrain.")]
    [SerializeField] private float waterYOffset = 0.01f;
    [Tooltip("Manual world-space Y position for ocean water surface. Set this to sit just below your coastline terrain. Overrides the computed SeaLevelWorldY.")]
    [SerializeField] private float manualOceanWaterY = 4.5f;
    [Tooltip("When true, use manualOceanWaterY for ocean water height instead of PlanetGenerator.SeaLevelWorldY.")]
    [SerializeField] private bool useManualOceanWaterY = true;

    [Header("Ocean Plane (Fast, Water Everywhere)")]
    [Tooltip("When enabled, renders the ocean as one cheap plane mesh at sea level (low memory). Disable this if you want SDF-only water.")]
    [SerializeField] private bool enableOceanPlane = false;
    [Tooltip("Extra padding (in hex radii) beyond the grid extents for the ocean plane.")]
    [SerializeField] private float oceanPlanePaddingHex = 2f;

    [Header("Water Volume Columns (Minecraft-like)")]
    [Tooltip("When enabled, chunk water meshes include vertical side walls so water occupies visible 3D volume (like Minecraft columns).")]
    [SerializeField] private bool enableWaterVolumeColumns = true;
    [Tooltip("How far downward (world units) to extend water walls when bordering land (or missing neighbor).")]
    [SerializeField] private float waterVolumeDepth = 10f;
    [Tooltip("When false, only inland water (rivers/lakes) gets volume walls. Ocean remains a surface only (cheaper).")]
    [SerializeField] private bool waterVolumeIncludeOcean = false;
    [Tooltip("Minimum water height difference before we build a step wall between two water tiles.")]
    [SerializeField] private float waterVolumeStepEpsilon = 0.02f;

    [Header("Unified SDF Water Surface (All Water Types)")]
    [Tooltip("When enabled, ALL water (ocean, rivers, lakes) is rendered as one gap-free SDF/marching-squares mesh.\nThis replaces per-tile hex fan water entirely.")]
    [SerializeField] private bool enableContinuousRiverSurface = true;
    [Tooltip("Legacy toggle — kept for compatibility. When false, lakes fall back to per-tile hex fans.")]
    [SerializeField] private bool continuousWaterIncludesLakes = true;
    [Tooltip("When enabled, ocean tiles are also included in the unified SDF water mesh (gap-free ocean).\nWARNING: This can create a massive mesh (and memory spikes) on big maps. Prefer OceanPlane unless you explicitly want SDF-only water.")]
    [SerializeField] private bool continuousWaterIncludesOcean = true;
    [Tooltip("Resolution of the SDF field (higher = smoother edges, more CPU time).")]
    [SerializeField] private int riverSdfWidth = 512;
    [Tooltip("Resolution of the SDF field (higher = smoother edges, more CPU time).")]
    [SerializeField] private int riverSdfHeight = 256;
    [Tooltip("River half-width multiplier relative to hex size (computed from map).")]
    [SerializeField] private float riverHalfWidthMultiplier = 0.55f;
    [Tooltip("Lake half-width multiplier relative to hex size (computed from map). Usually larger than rivers.")]
    [SerializeField] private float lakeHalfWidthMultiplier = 1.25f;
    [Tooltip("Ocean half-width multiplier relative to hex size. Should be >= 1 to fully cover hex tiles.")]
    [SerializeField] private float oceanHalfWidthMultiplier = 1.25f;
    [Tooltip("Extra Y lift above sampled terrain height to avoid z-fighting.")]
    [SerializeField] private float riverSurfaceLift = 0.02f;

    [Header("Inland Water Volume (3D Fill)")]
    [Tooltip("When enabled, the continuous inland water surface is extruded downward into a closed 3D mesh (top + walls + bottom) so rivers/lakes look filled in 3D space.")]
    [SerializeField] private bool extrudeInlandWaterToVolume = true;
    [Tooltip("How far downward (world units) to extrude the inland water mesh to create a filled volume.")]
    [SerializeField] private float inlandWaterVolumeDepth = 12f;

    // Continuous river mesh instance (lives under this manager)
    private GameObject _riverSurfaceObj;
    private Mesh _riverSurfaceMesh;

    [Header("Auto-Build")]
    [SerializeField] private bool preBuildOnPlanetReady = true;
    [Tooltip("Chunks processed per frame during batched build (higher = faster total time but more frame spikes).")]
    [SerializeField] private int chunksPerBatch = 4;
    [Tooltip("Tiles processed per frame when assigning tiles to chunks (higher = faster but more frame spikes).")]
    [SerializeField] private int tilesPerBatch = 2048;
    [Header("Profiling")]
    [Tooltip("When true, logs timing breakdowns for chunk build phases (useful for identifying hotspots).")]
    [SerializeField] private bool enableBuildProfiling = false;

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
    // Cached inspector-backed runtime values for change detection
    private int _lastHeightmapAnisoLevel = -1;
    private bool _lastUseTriplanar = true;
    private float _lastCliffTiling = -1f;
    private float _lastCliffStrength = -1f;
    private float _lastCliffSlopeThreshold = -1f;
    private float _lastCliffSlopeBlend = -1f;
    private float _lastCliffStepThreshold = -1f;
    private float _lastCliffStepBlend = -1f;
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
    private Texture2DArray biomeHeightArray;
    [SerializeField]
    [Tooltip("Optional texture array used for cliff/alpine surfaces. Assign a Texture2DArray with multiple cliff variants.")]
    private Texture2DArray cliffAlbedoArray;
    [SerializeField]
    [Tooltip("Optional detail normal array for cliffs. Assign a Texture2DArray matching `cliffAlbedoArray` depth.")]
    private Texture2DArray cliffNormalArray;
    
    [Header("Cliff Settings")]
    [SerializeField]
    [Range(0.1f, 200f)]
    private float cliffTiling = 12f;
    [SerializeField]
    [Range(0f, 1f)]
    private float cliffStrength = 1f;
    [SerializeField]
    [Range(0f, 1f)]
    private float cliffSlopeThreshold = 0.5f;
    [SerializeField]
    [Range(0f, 1f)]
    private float cliffSlopeBlend = 0.2f;
    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("Tile-step threshold (in normalized heightmap units). If center - neighbor > this, it is considered a step.")]
    private float cliffStepThreshold = 0.15f;
    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("How quickly the step mask falls off (in normalized heightmap units).")]
    private float cliffStepBlend = 0.08f;
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

        // Detect changes made in the inspector at runtime and apply them immediately.
        bool applied = false;

        if (_lastHeightmapAnisoLevel != heightmapAnisoLevel)
        {
            _lastHeightmapAnisoLevel = heightmapAnisoLevel;
            if (heightmapTexture != null)
            {
                heightmapTexture.anisoLevel = heightmapAnisoLevel;
            }
            applied = true;
        }

        if (_lastUseTriplanar != useTriplanar)
        {
            _lastUseTriplanar = useTriplanar;
            applied = true;
        }

        if (_lastCliffTiling != cliffTiling || _lastCliffStrength != cliffStrength || _lastCliffSlopeThreshold != cliffSlopeThreshold || _lastCliffSlopeBlend != cliffSlopeBlend || _lastCliffStepThreshold != cliffStepThreshold || _lastCliffStepBlend != cliffStepBlend)
        {
            _lastCliffTiling = cliffTiling;
            _lastCliffStrength = cliffStrength;
            _lastCliffSlopeThreshold = cliffSlopeThreshold;
            _lastCliffSlopeBlend = cliffSlopeBlend;
            _lastCliffStepThreshold = cliffStepThreshold;
            _lastCliffStepBlend = cliffStepBlend;
            applied = true;
        }

        if (applied)
        {
            ApplyBiomeMaterialSettings();
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
        
        // --- BURST: Build LUT using Burst-compiled parallel job (all CPU cores) ---
        float buildStartTime = enableBuildProfiling ? Time.realtimeSinceStartup : 0f;
        float lastPhaseTime = buildStartTime;
        int[] preBuiltLUT = EquirectLUTBuilder.BuildLUTBurst(grid, textureWidth, textureHeight);
        yield return null;
        
        if (preBuiltLUT == null)
        {
            Debug.LogError("[HexMapChunkManager] Failed to build LUT via Burst!");
            _buildCoroutine = null;
            yield break;
        }

        if (enableBuildProfiling)
        {
            float now = Time.realtimeSinceStartup;
            Debug.Log($"[HexMapChunkManager][Profile] LUT build (Burst): {(now - lastPhaseTime) * 1000f:F2} ms");
            lastPhaseTime = now;
        }

        // Bake texture using PlanetTextureBaker with pre-built LUT (GPU bake is fast; LUT was the bottleneck)
        BakeTexture(preBuiltLUT);
        
        // --- BATCHED: Build biome visual maps with yielding for heavy texture operations ---
        yield return StartCoroutine(BuildBiomeVisualMapsCoroutine());

        if (enableBuildProfiling)
        {
            float now = Time.realtimeSinceStartup;
            Debug.Log($"[HexMapChunkManager][Profile] Biome visuals: {(now - lastPhaseTime) * 1000f:F2} ms");
            lastPhaseTime = now;
        }

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
        
        // Create chunks (batched)
        yield return StartCoroutine(CreateChunksCoroutine());

        // Assign tiles to chunks (batched)
        yield return StartCoroutine(AssignTilesToChunksCoroutine());

        if (enableBuildProfiling)
        {
            float now = Time.realtimeSinceStartup;
            Debug.Log($"[HexMapChunkManager][Profile] Create+Assign chunks: {(now - lastPhaseTime) * 1000f:F2} ms");
            lastPhaseTime = now;
        }

        // Initialize per-chunk season masks
        UpdateSeasonMasksForCurrentSeason();
        
        // Build all chunk meshes (batched)
        yield return StartCoroutine(RefreshAllChunksCoroutine());

        // Build chunk-based water and foam meshes (batched)
        yield return StartCoroutine(BuildAllWaterMeshesCoroutine());

        // Build continuous SDF water mesh (batched)
        yield return StartCoroutine(BuildContinuousRiverSurfaceMeshCoroutine());

        // Build cheap ocean plane last (ensures "water everywhere" even if SDF is inland-only)
        BuildOceanPlane();
        
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
        
        if (enableBuildProfiling)
        {
            float now = Time.realtimeSinceStartup;
            Debug.Log($"[HexMapChunkManager][Profile] Total BuildChunks: {(now - buildStartTime) * 1000f:F2} ms");
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
    /// Coroutine version of BuildBiomeVisualMaps that uses Burst jobs for heavy texture generation
    /// (BiomeIndexMap and Heightmap) instead of per-pixel coroutine strips.
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

        if (bakeResult.lut == null || bakeResult.lut.Length != width * height)
        {
            bakeResult.lut = EquirectLUTBuilder.BuildLUT(grid, width, height);
            bakeResult.width = width;
            bakeResult.height = height;
        }

        BuildBiomeLookup();
        BuildBiomeTextureArrays();
        yield return null;

        // BURST: Build biome index map via parallel job
        BuildBiomeIndexMapBurst(width, height);
        yield return null;

        // BURST: Build heightmap via parallel job
        BuildHeightmapBurst(width, height);
        yield return null;
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
            biomeHeightArray = lib.heightArray;

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
                    // Tiling fallback: if biome.tiling is <= 0, use the SurfaceFamily defaultTiling (explicit fallback).
                    float tiling = entry.tiling;
                    if (tiling <= 0f && entry.surfaceFamily != null)
                        tiling = entry.surfaceFamily.defaultTiling;
                    biomeParamsArray[i] = new Vector4(tiling, entry.snowRetention, entry.wetnessResponse, entry.isWaterBiome ? 1f : 0f);
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
                filterMode = FilterMode.Trilinear,
                anisoLevel = heightmapAnisoLevel,
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
                    elevation = GetRenderedElevation(tileIndex); // Use rendered elevation (may preserve pre-water elevation)
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
                filterMode = FilterMode.Trilinear,
                anisoLevel = heightmapAnisoLevel,
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
                    elevation = GetRenderedElevation(tileIndex); // World-space height offset — may preserve original elevation near freshwater
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

    // =====================================================================================
    //  Burst-accelerated BiomeIndexMap and Heightmap builders
    // =====================================================================================

    /// <summary>
    /// Pre-compute a flat array mapping tileIndex -> surface slice index.
    /// Doing this once over ~tens of thousands of tiles eliminates millions of
    /// Dictionary.TryGetValue + biome resolution calls in the per-pixel loop.
    /// </summary>
    private int[] PrecomputeTileSliceIndices()
    {
        int tileCount = grid.TileCount;
        var result = ArrayPoolUtils.RentInt(tileCount);
        int maxSlice = (biomeAlbedoArray != null) ? Mathf.Max(0, biomeAlbedoArray.depth - 1) : -1;

        for (int ti = 0; ti < tileCount; ti++)
        {
            if (!planetGenerator.data.TryGetValue(ti, out var tile))
            {
                result[ti] = 0;
                continue;
            }

            var visual = biomeVisualDatabase.Get(tile.biome);

            // Underwater biome texture swap: if this tile has a non-default underwater biome
            // (AbyssalPlains, Trench, etc.), render that biome's texture on the ocean floor instead
            // of the surface biome (Ocean) texture. The surface biome stays Ocean for gameplay.
            if (tile.underwaterBiome != Biome.Ocean && tile.underwaterBiome != tile.biome)
            {
                var underwaterVisual = biomeVisualDatabase.Get(tile.underwaterBiome);
                if (underwaterVisual != null && underwaterVisual.surfaceFamily != null)
                    visual = underwaterVisual;
            }

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
                        int h = ti * 1103515245 + 12345;
                        chosenVariant = Mathf.Abs(h) % variantCount;
                    }
                }
                sliceIndex = startSlice + chosenVariant;
            }

            if (maxSlice >= 0 && sliceIndex > maxSlice) sliceIndex = maxSlice;
            if (sliceIndex < 0) sliceIndex = 0;
            result[ti] = sliceIndex;
        }

        return result;
    }

    /// <summary>
    /// Pre-compute a flat array mapping tileIndex -> rendered elevation.
    /// Eliminates per-pixel dictionary lookups and neighbor iteration in the heightmap loop.
    /// </summary>
    private float[] PrecomputeTileElevations()
    {
        int tileCount = grid.TileCount;
        var result = ArrayPoolUtils.RentFloat(tileCount);
        for (int ti = 0; ti < tileCount; ti++)
            result[ti] = GetRenderedElevation(ti);
        return result;
    }

    /// <summary>
    /// Build the BiomeIndexMap texture using a Burst-compiled parallel job.
    /// Replaces the strip-based coroutine with a single parallel pass over all pixels.
    /// </summary>
    private void BuildBiomeIndexMapBurst(int width, int height)
    {
        if (bakeResult.lut == null || bakeResult.lut.Length == 0) return;

        if (biomeIndexMap == null || biomeIndexMap.width != width || biomeIndexMap.height != height)
        {
            biomeIndexMap = new Texture2D(width, height, TextureFormat.RFloat, false, true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                name = "BiomeIndexMap"
            };
        }

        int pixelCount = width * height;
        var tileSlice = PrecomputeTileSliceIndices();

        var lutNative = new NativeArray<int>(bakeResult.lut, Allocator.TempJob);
        var sliceNative = new NativeArray<int>(tileSlice, Allocator.TempJob);
        ArrayPoolUtils.ReturnInt(tileSlice); // return to pool after NativeArray copy
        var pixelsNative = new NativeArray<float>(pixelCount, Allocator.TempJob);

        new FillBiomeIndexMapJob
        {
            lut = lutNative,
            tileSliceIndex = sliceNative,
            pixels = pixelsNative,
        }.Schedule(pixelCount, 4096).Complete();

        biomeIndexMap.SetPixelData(pixelsNative, 0);
        biomeIndexMap.Apply(false, false);

        pixelsNative.Dispose();
        sliceNative.Dispose();
        lutNative.Dispose();
    }

    /// <summary>
    /// Build the Heightmap texture using a Burst-compiled parallel job.
    /// Writes half-float data directly — no intermediate Color[] allocation.
    /// </summary>
    private void BuildHeightmapBurst(int width, int height)
    {
        if (bakeResult.lut == null || bakeResult.lut.Length == 0) return;

        _heightmapMin = float.MaxValue;
        _heightmapMax = float.MinValue;
        _heightmapNonZero = 0;
        _heightmapInvalidLut = 0;
        _heightmapMissingTileData = 0;

        if (heightmapTexture == null || heightmapTexture.width != width || heightmapTexture.height != height)
        {
            heightmapTexture = new Texture2D(width, height, TextureFormat.RHalf, true, true)
            {
                filterMode = FilterMode.Trilinear,
                anisoLevel = heightmapAnisoLevel,
                wrapMode = TextureWrapMode.Repeat,
                name = "TerrainHeightmap"
            };
        }

        int pixelCount = width * height;
        var tileElev = PrecomputeTileElevations();

        // Track min/max from the pre-computed array (avoids doing it in the Burst job)
        int tileCount = grid.TileCount;
        for (int ti = 0; ti < tileCount; ti++)
        {
            float e = tileElev[ti];
            if (e != 0f) _heightmapNonZero++;
            if (e < _heightmapMin) _heightmapMin = e;
            if (e > _heightmapMax) _heightmapMax = e;
        }
        if (_heightmapMin == float.MaxValue) _heightmapMin = 0f;
        if (_heightmapMax == float.MinValue) _heightmapMax = 0f;

        var lutNative = new NativeArray<int>(bakeResult.lut, Allocator.TempJob);
        var elevNative = new NativeArray<float>(tileElev, Allocator.TempJob);
        ArrayPoolUtils.ReturnFloat(tileElev); // return to pool after NativeArray copy
        var pixelsNative = new NativeArray<ushort>(pixelCount, Allocator.TempJob);

        new FillHeightmapJob
        {
            lut = lutNative,
            tileElevation = elevNative,
            pixels = pixelsNative,
        }.Schedule(pixelCount, 4096).Complete();

        heightmapTexture.SetPixelData(pixelsNative, 0);
        heightmapTexture.Apply(true, false);

        pixelsNative.Dispose();
        elevNative.Dispose();
        lutNative.Dispose();
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
        if (cliffAlbedoArray != null)
        {
            sharedMaterial.SetTexture("_CliffAlbedoArray", cliffAlbedoArray);
        }
        if (cliffNormalArray != null)
        {
            sharedMaterial.SetTexture("_CliffNormalArray", cliffNormalArray);
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

        if (biomeHeightArray != null)
        {
            sharedMaterial.SetTexture("_BiomeHeightArray", biomeHeightArray);
        }

        if (biomeEmissiveMapTexture != null)
        {
            sharedMaterial.SetTexture("_BiomeEmissiveMapTex", biomeEmissiveMapTexture);
        }

        sharedMaterial.SetFloat("_GlobalSnowAmount", globalSnowAmount);
        sharedMaterial.SetFloat("_GlobalWetness", globalWetness);
        sharedMaterial.SetFloat("_MetallicMultiplier", metallicMultiplier);
        sharedMaterial.SetFloat("_AOIntensity", aoIntensity);
        sharedMaterial.SetFloat("_SmoothnessMultiplier", smoothnessMultiplier);
        sharedMaterial.SetFloat("_MapWidth", mapWidth);
        sharedMaterial.SetFloat("_MapHeight", mapHeight);

        // Cliff params
        sharedMaterial.SetFloat("_CliffTiling", cliffTiling);
        sharedMaterial.SetFloat("_CliffStrength", cliffStrength);
        sharedMaterial.SetFloat("_CliffSlopeThreshold", cliffSlopeThreshold);
        sharedMaterial.SetFloat("_CliffSlopeBlend", cliffSlopeBlend);
        sharedMaterial.SetFloat("_CliffStepThreshold", cliffStepThreshold);
        sharedMaterial.SetFloat("_CliffStepBlend", cliffStepBlend);
        float cliffSlices = (cliffAlbedoArray != null) ? Mathf.Max(1, cliffAlbedoArray.depth) : 1;
        sharedMaterial.SetFloat("_CliffSliceCount", cliffSlices);

        // Normal sampling and biome blending parameters
        sharedMaterial.SetFloat("_NormalStrength", normalStrength);
        sharedMaterial.SetFloat("_NormalSampleRadius", normalSampleRadius);
        sharedMaterial.SetFloat("_BiomeBlendRadius", biomeBlendRadius);
        sharedMaterial.SetFloat("_BiomeBlendSharpness", biomeBlendSharpness);

        // Triplanar parameters
        sharedMaterial.SetFloat("_TriTiling", triplanarTiling);
        sharedMaterial.SetFloat("_TriBlend", triplanarBlend);
        sharedMaterial.SetFloat("_UseTriplanar", useTriplanar ? 1f : 0f);
        // Detail maps (micro-detail)
        if (detailAlbedoMap != null)
            sharedMaterial.SetTexture("_DetailAlbedoMap", detailAlbedoMap);
        if (detailNormalMap != null)
            sharedMaterial.SetTexture("_DetailNormalMap", detailNormalMap);
        sharedMaterial.SetFloat("_DetailTiling", detailTiling);
        sharedMaterial.SetFloat("_DetailStrength", detailStrength);
        sharedMaterial.SetFloat("_DetailNormalStrength", detailNormalStrength);
        sharedMaterial.SetFloat("_DetailFadeStart", detailFadeStart);
        sharedMaterial.SetFloat("_DetailFadeEnd", detailFadeEnd);
        
        // Slice-to-biome reverse map (for per-biome tint/params lookup in shader)
        if (sliceToBiomeMap != null)
        {
            sharedMaterial.SetTexture("_SliceToBiomeMap", sliceToBiomeMap);
        }

        // Provide biome count and total slice count for shader UV-based lookups.
        // _BiomeCount = number of biomes (indexes into _BiomeTints[] / _BiomeParams[] / _BiomeEmissiveMapTex).
        // _TotalSlices = number of texture array slices (indexes into _SliceToBiomeMap).
        // These differ when multiple biomes share the same surface family.
        int biomeCount = (biomeTintArray != null) ? biomeTintArray.Length : 0;
        sharedMaterial.SetFloat("_BiomeCount", (float)biomeCount);
        int totalSlices = (biomeAlbedoArray != null) ? biomeAlbedoArray.depth : 1;
        sharedMaterial.SetFloat("_TotalSlices", (float)totalSlices);
    }
    
    /// <summary>
    /// Returns the elevation that should be used for rendering for a given tile.
    /// When `preserveLandElevationNearFreshwater` is enabled, land tiles that are
    /// adjacent to lakes or rivers will use their `originalElevation` instead of
    /// the possibly-carved `elevation` value. Otherwise returns the current elevation.
    /// </summary>
    private float GetRenderedElevation(int tileIndex)
    {
        if (planetGenerator == null) return 0f;
        if (!planetGenerator.data.TryGetValue(tileIndex, out var td)) return 0f;
        if (!preserveLandElevationNearFreshwater) return td.elevation;

        if (td.isLand)
        {
            var nbrs = grid.neighbors[tileIndex];
            if (nbrs != null)
            {
                foreach (int n in nbrs)
                {
                    if (n < 0 || n >= grid.TileCount) continue;
                    if (planetGenerator.data.TryGetValue(n, out var nt))
                    {
                        if (nt.isLake || nt.isRiver)
                        {
                            return td.originalElevation;
                        }
                    }
                }
            }
        }

        return td.elevation;
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
            try
            {
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
                return ok;
            }
            finally
            {
                Destroy(tmp);
            }
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
    /// Uses a Burst job to encode tile indices as RGB24 bytes, then SetPixelData.
    /// </summary>
    private Texture2D lutTexture;
    private void CreateAndApplyLUTTexture()
    {
        if (bakeResult.lut == null || bakeResult.lut.Length == 0) return;

        int width = bakeResult.width > 0 ? bakeResult.width : textureWidth;
        int height = bakeResult.height > 0 ? bakeResult.height : textureHeight;

        lutTexture = new Texture2D(width, height, TextureFormat.RGB24, false, true);
        lutTexture.filterMode = FilterMode.Point;
        lutTexture.wrapMode = TextureWrapMode.Repeat;
        lutTexture.name = "TileIndexLUT";
        lutTexture.anisoLevel = 0;

        int pixelCount = width * height;
        var lutNative = new NativeArray<int>(bakeResult.lut, Allocator.TempJob);
        var pixelsNative = new NativeArray<byte>(pixelCount * 3, Allocator.TempJob);

        new EncodeLUTTextureJob
        {
            lut = lutNative,
            pixels = pixelsNative,
        }.Schedule(pixelCount, 4096).Complete();

        lutTexture.SetPixelData(pixelsNative, 0);
        lutTexture.Apply(false, false);

        pixelsNative.Dispose();
        lutNative.Dispose();

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

    /// <summary>
    /// Batched version of CreateChunks to spread GameObject/Component creation across frames.
    /// </summary>
    private System.Collections.IEnumerator CreateChunksCoroutine()
    {
        chunks = new HexMapChunk[chunksX, chunksZ];
        
        float chunkWidth = mapWidth / chunksX;
        float chunkHeight = mapHeight / chunksZ;
        int batchSize = Mathf.Max(1, chunksPerBatch);
        int count = 0;
        
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

                count++;
                if (count >= batchSize) { count = 0; yield return null; }
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

    /// <summary>
    /// Batched version of AssignTilesToChunks that yields every N tiles to avoid frame hiccups on large maps.
    /// </summary>
    private System.Collections.IEnumerator AssignTilesToChunksCoroutine()
    {
        tileToChunk.Clear();
        
        if (grid == null) yield break;
        
        float chunkWidth = mapWidth / chunksX;
        float chunkHeight = mapHeight / chunksZ;
        
        // Group tiles by chunk
        var chunkTiles = new Dictionary<(int, int), List<int>>();
        int batchSize = Mathf.Max(1, tilesPerBatch);
        int count = 0;

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

            count++;
            if (count >= batchSize) { count = 0; yield return null; }
        }

        // Assign to chunks (small number of chunks, do synchronously)
        foreach (var kvp in chunkTiles)
        {
            chunks[kvp.Key.Item1, kvp.Key.Item2].SetTileIndices(kvp.Value);
            yield return null; // yield between chunk assignments to be safe
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
    /// Create a MeshCollider covering the entire map for WorldPicker raycasts.
    /// The mesh is subdivided and CPU-displaced using the heightmap so that
    /// raycasts land on the actual visible terrain surface at any camera angle
    /// (including ground-level views).
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
        colliderObj.transform.localRotation = Quaternion.identity;
        
        // Subdivision resolution — match chunk mesh density, capped for performance
        int subX = Mathf.Min(chunksX * meshSubdivisionsPerChunk, 512);
        int subZ = Mathf.Min(chunksZ * meshSubdivisionsPerChunk, 256);
        int vX = subX + 1;
        int vZ = subZ + 1;
        int vertCount = vX * vZ;
        
        float halfW = mapWidth * 0.5f;
        float halfH = mapHeight * 0.5f;
        
        // Check if the heightmap is available for CPU-side displacement
        bool hasHeightmap = heightmapTexture != null && heightmapTexture.isReadable;
        int hmW = hasHeightmap ? heightmapTexture.width : 0;
        int hmH = hasHeightmap ? heightmapTexture.height : 0;
        
        var vertices = new Vector3[vertCount];
        var uvs = new Vector2[vertCount];
        
        for (int z = 0; z < vZ; z++)
        {
            for (int x = 0; x < vX; x++)
            {
                int idx = z * vX + x;
                float u = (float)x / subX;
                float v = (float)z / subZ;
                
                float posX = -halfW + u * mapWidth;
                float posZ = -halfH + v * mapHeight;
                
                // Sample the heightmap and apply the same displacement the GPU shader uses
                float posY = 0f;
                if (hasHeightmap)
                {
                    int px = Mathf.Clamp(Mathf.FloorToInt(u * hmW), 0, hmW - 1);
                    int py = Mathf.Clamp(Mathf.FloorToInt(v * hmH), 0, hmH - 1);
                    posY = heightmapTexture.GetPixel(px, py).r * displacementStrength;
                }
                
                vertices[idx] = new Vector3(posX, posY, posZ);
                uvs[idx] = new Vector2(u, v);
            }
        }
        
        // Build triangle indices
        int triCount = subX * subZ * 6;
        var triangles = new int[triCount];
        int triIdx = 0;
        for (int z = 0; z < subZ; z++)
        {
            for (int x = 0; x < subX; x++)
            {
                int bl = z * vX + x;
                int br = bl + 1;
                int tl = bl + vX;
                int tr = tl + 1;
                
                triangles[triIdx++] = bl;
                triangles[triIdx++] = tl;
                triangles[triIdx++] = tr;
                
                triangles[triIdx++] = bl;
                triangles[triIdx++] = tr;
                triangles[triIdx++] = br;
            }
        }
        
        Mesh pickMesh = new Mesh();
        pickMesh.name = "PickingMesh_Displaced";
        if (vertCount > 65535)
            pickMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        pickMesh.vertices = vertices;
        pickMesh.uv = uvs;
        pickMesh.triangles = triangles;
        pickMesh.RecalculateNormals();
        pickMesh.RecalculateBounds();
        
        // MeshFilter is required for hit.textureCoord to work with MeshCollider
        MeshFilter mf = colliderObj.AddComponent<MeshFilter>();
        mf.mesh = pickMesh;
        
        // Invisible renderer (required for hit.textureCoord on some Unity versions)
        MeshRenderer mr = colliderObj.AddComponent<MeshRenderer>();
        mr.enabled = false;
        
        // MeshCollider for physics raycasts with proper UV interpolation
        var meshCollider = colliderObj.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = pickMesh;
        pickingCollider = meshCollider;
        
        // Set layer for filtered raycasting (WorldPicker uses this layer mask)
        int terrainLayer = LayerMask.NameToLayer("Terrain");
        colliderObj.layer = terrainLayer >= 0 ? terrainLayer : 0;
        
        Debug.Log($"[HexMapChunkManager] Created displaced picking collider: {subX}x{subZ} subdivisions, {vertCount} verts, heightmap={hasHeightmap}, displacement={displacementStrength}");
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
            
            // Set the picking layer mask to match the picking collider's actual layer
            int layer = pickingCollider != null ? pickingCollider.gameObject.layer : 0;
            worldPicker.pickingLayerMask = 1 << layer;
            
            // Ensure a camera is assigned for picking. If the scene doesn't tag MainCamera (common in HDRP setups),
            // WorldPicker will still fall back to any available camera, but assigning here reduces ambiguity.
            if (worldPicker.targetCamera == null) worldPicker.targetCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            Debug.Log($"[HexMapChunkManager] Updated WorldPicker: LUT={bakeResult.lut.Length}, pickingLayer={layer}, displaced collider={(pickingCollider != null ? "assigned" : "null")}");
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
    public void BuildWaterMeshForChunk(HexMapChunk chunk, out int lakes, out int rivers, out int oceans)
    {
        lakes = rivers = oceans = 0;
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
            if (td.waterType == TileWaterType.None) continue;
            // When the unified SDF water mesh handles a water type, skip it here to avoid double-rendering.
            if (enableContinuousRiverSurface && td.waterType == TileWaterType.River) continue;
            if (enableContinuousRiverSurface && continuousWaterIncludesLakes && td.waterType == TileWaterType.Lake) continue;
            // When we render ocean via the cheap ocean plane, skip per-tile ocean to avoid double-rendering.
            if (enableOceanPlane && td.waterType == TileWaterType.Ocean) continue;
            if (enableContinuousRiverSurface && continuousWaterIncludesOcean && td.waterType == TileWaterType.Ocean) continue;
            waterTiles.Add(ti);
        }
        if (waterTiles.Count == 0) return;

        foreach (int ti in waterTiles)
        {
            var wt = planetGenerator.data[ti].waterType;
            if (wt == TileWaterType.Lake) lakes++;
            else if (wt == TileWaterType.River) rivers++;
            else if (wt == TileWaterType.Ocean) oceans++;
        }

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
    /// Build water and foam meshes for ALL chunks (batched).
    /// Called once during BuildChunks after terrain is ready.
    /// </summary>
    private System.Collections.IEnumerator BuildAllWaterMeshesCoroutine()
    {
        if (chunks == null || planetGenerator == null) yield break;

        // Pre-build: count water tiles by type (helps diagnose mismatches)
        int lakeTiles = 0, riverTiles = 0, oceanTiles = 0, totalWater = 0;
        if (planetGenerator.data != null)
        {
            foreach (var kvp in planetGenerator.data)
            {
                var wt = kvp.Value.waterType;
                if (wt == TileWaterType.None) continue;
                totalWater++;
                if (wt == TileWaterType.Lake) lakeTiles++;
                else if (wt == TileWaterType.River) riverTiles++;
                else if (wt == TileWaterType.Ocean) oceanTiles++;
            }
        }
        if (ShouldRunDiagnostics() || debugWaterVerbose)
            Debug.Log($"[HexMapChunkManager][WaterDiag] Pre-build totals: ocean={oceanTiles}, lake={lakeTiles}, river={riverTiles}, totalWater={totalWater}");

        int batchSize = Mathf.Max(1, chunksPerBatch);
        int count = 0;
        for (int x = 0; x < chunksX; x++)
        {
            for (int z = 0; z < chunksZ; z++)
            {
                if (chunks[x, z] != null)
                {
                    BuildWaterMeshForChunk(chunks[x, z], out int lakes, out int rivers, out int oceans);
                    if (debugWaterVerbose && (lakes + rivers + oceans) > 0)
                        Debug.Log($"[HexMapChunkManager][WaterDiag] Chunk({x},{z}) per-tile mesh: lakes={lakes}, rivers={rivers}, oceans={oceans}");
                    count++;
                    if (count >= batchSize) { count = 0; yield return null; }
                }
            }
        }

        if (ShouldRunDiagnostics() || debugWaterVerbose)
            Debug.Log($"[HexMapChunkManager][WaterDiag] Post-build: per-tile water meshes done (SDF unified mesh handles ocean/river/lake when enabled)");

        // Diagnostic: detect coast/seas/ocean tiles missing waterType (common cause of missing coast water)
        if (ShouldRunDiagnostics() && planetGenerator != null && planetGenerator.data != null)
        {
            int coastBiome = 0, coastMissingWaterType = 0;
            foreach (var kvp in planetGenerator.data)
            {
                var td = kvp.Value;
                if (td.biome == Biome.Coast)
                {
                    coastBiome++;
                    if (td.waterType == TileWaterType.None) coastMissingWaterType++;
                }
            }
            if (coastMissingWaterType > 0)
            {
                Debug.LogWarning($"[HexMapChunkManager][WaterDiag] Coast tiles missing waterType: {coastMissingWaterType}/{coastBiome}. These will not get coast water meshes.");
            }
        }
    }

    
    // =====================================================================================
    //  Continuous River Surface Mesh (SDF + Marching Squares) — batched coroutine
    // =====================================================================================
    private System.Collections.IEnumerator BuildContinuousRiverSurfaceMeshCoroutine()
    {
        if (!enableContinuousRiverSurface) { DestroyRiverSurface(); if (debugWaterVerbose) Debug.Log("[HexMapChunkManager][SDF] Skipped: enableContinuousRiverSurface=false"); yield break; }
        if (planetGenerator == null || grid == null || !grid.IsBuilt) { DestroyRiverSurface(); Debug.LogWarning("[HexMapChunkManager][SDF] Skipped: missing planetGenerator, grid, or grid not built"); yield break; }
        if (waterMaterial == null || heightmapTexture == null || bakeResult.lut == null || bakeResult.lut.Length == 0) { DestroyRiverSurface(); Debug.LogWarning("[HexMapChunkManager][SDF] Skipped: missing waterMaterial, heightmapTexture, or LUT"); yield break; }

        int wCells = Mathf.Clamp(riverSdfWidth, 64, 4096);
        int hCells = Mathf.Clamp(riverSdfHeight, 32, 4096);
        int wPts = wCells + 1;
        int hPts = hCells + 1;

        // Use actual grid extents (world space) instead of assuming the map is centered at origin.
        // This prevents the unified mesh from collapsing into a strip when the grid/manager is offset.
        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
        for (int i = 0; i < grid.TileCount; i++)
        {
            Vector3 c = grid.tileCenters[i];
            if (c.x < minX) minX = c.x;
            if (c.x > maxX) maxX = c.x;
            if (c.z < minZ) minZ = c.z;
            if (c.z > maxZ) maxZ = c.z;
        }

        EnsureHexCorners();
        float hexSize = ComputeHexSize();
        minX -= hexSize; maxX += hexSize;
        minZ -= hexSize; maxZ += hexSize;
        float worldW = Mathf.Max(0.001f, maxX - minX);
        float worldH = Mathf.Max(0.001f, maxZ - minZ);
        Vector3 mgrPos = transform.position;

        float dx = worldW / wCells;
        float dz = worldH / hCells;
        float diag = Mathf.Sqrt(dx * dx + dz * dz);

        // EnsureHexCorners + hexSize already computed above
        float isoRiver = Mathf.Max(0.05f, hexSize * Mathf.Max(0.01f, riverHalfWidthMultiplier));
        float isoLake = Mathf.Max(0.05f, hexSize * Mathf.Max(0.01f, lakeHalfWidthMultiplier));
        float isoOcean = Mathf.Max(0.05f, hexSize * Mathf.Max(0.01f, oceanHalfWidthMultiplier));
        // Prevent sub-cell widths which alias into hairline strands at a given SDF resolution.
        // Use a smaller multiplier so coarse-resolution inflation is reduced, and
        // also rely on seeding multiple grid cells around each tile center so
        // the iso behaves consistently when resolution changes.
        float minIso = Mathf.Max(dx, dz) * 0.5f;
        isoRiver = Mathf.Max(isoRiver, minIso);
        isoLake = Mathf.Max(isoLake, minIso);
        isoOcean = Mathf.Max(isoOcean, minIso);

        if (debugWaterVerbose)
            Debug.Log($"[HexMapChunkManager][SDF] Iso values: river={isoRiver:F3}, lake={isoLake:F3}, ocean={isoOcean:F3}, hexSize={hexSize:F3}, grid={wCells}x{hCells}");

        // --- Build seed grids for rivers, lakes, and ocean ---
        var seedRiver = ArrayPoolUtils.RentBool(wPts * hPts);
        var seedLake = continuousWaterIncludesLakes ? ArrayPoolUtils.RentBool(wPts * hPts) : null;
        var seedOcean = continuousWaterIncludesOcean ? ArrayPoolUtils.RentBool(wPts * hPts) : null;
        var ownerRiver = ArrayPoolUtils.RentInt(wPts * hPts);
        for (int i = 0; i < wPts * hPts; i++) ownerRiver[i] = -1;
        int[] ownerLake = null;
        if (seedLake != null)
        {
            ownerLake = ArrayPoolUtils.RentInt(wPts * hPts);
            for (int i = 0; i < wPts * hPts; i++) ownerLake[i] = -1;
        }
        int[] ownerOcean = null;
        if (seedOcean != null)
        {
            ownerOcean = ArrayPoolUtils.RentInt(wPts * hPts);
            for (int i = 0; i < wPts * hPts; i++) ownerOcean[i] = -1;
        }

        // Helper: mark a seed at UV (0..1)
        void MarkSeed(bool[] seed, int[] owner, float u, float v, int tileIndex)
        {
            u = Mathf.Repeat(u, 1f);
            v = Mathf.Clamp01(v);
            int px = Mathf.Clamp(Mathf.RoundToInt(u * wCells), 0, wCells);
            int py = Mathf.Clamp(Mathf.RoundToInt(v * hCells), 0, hCells);

            // Compute a radius in grid cells that covers the world-space iso radius.
            // This makes seeding resolution-independent: increasing SDF resolution
            // simply increases the number of seeded cells rather than changing
            // whether anything is seeded at all.
            int radius = 0;
            float minCell = Mathf.Min(dx, dz);
            if (minCell > 0f)
                radius = Mathf.CeilToInt(isoRiver / minCell);

            for (int oy = py - radius; oy <= py + radius; oy++)
            {
                if (oy < 0 || oy > hPts - 1) continue;
                for (int ox = px - radius; ox <= px + radius; ox++)
                {
                    if (ox < 0 || ox > wPts - 1) continue;
                    int idx = oy * wPts + ox;
                    seed[idx] = true;
                    if (owner != null && owner[idx] < 0) owner[idx] = tileIndex; // keep first owner
                }
            }
        }

        // Mark tile centers and some segment samples so rivers don't appear "dotted"
        for (int ti = 0; ti < grid.TileCount; ti++)
        {
            if (!planetGenerator.data.TryGetValue(ti, out var td)) continue;
            Vector3 c = grid.tileCenters[ti];
            float u0 = (c.x - minX) / worldW;
            float v0 = (c.z - minZ) / worldH;

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
                            float uu = (p.x - minX) / worldW;
                            float vv = (p.z - minZ) / worldH;
                            MarkSeed(seedRiver, ownerRiver, uu, vv, ti);
                        }
                    }
                }
            }
            else if (continuousWaterIncludesLakes && td.waterType == TileWaterType.Lake)
            {
                // Lakes: seed center + corners so the lake area fills the whole hex reliably.
                MarkSeed(seedLake, ownerLake, u0, v0, ti);
                for (int k = 0; k < 6; k++)
                {
                    Vector3 p = c + new Vector3(hexSize * HexCornerCos[k], 0f, hexSize * HexCornerSin[k]);
                    float uu = (p.x - minX) / worldW;
                    float vv = (p.z - minZ) / worldH;
                    MarkSeed(seedLake, ownerLake, uu, vv, ti);
                }
            }
            else if (continuousWaterIncludesOcean && td.waterType == TileWaterType.Ocean)
            {
                // Ocean: seed center + corners so the SDF fully covers each ocean hex (prevents holes between tile centers).
                MarkSeed(seedOcean, ownerOcean, u0, v0, ti);
                for (int k = 0; k < 6; k++)
                {
                    Vector3 p = c + new Vector3(hexSize * HexCornerCos[k], 0f, hexSize * HexCornerSin[k]);
                    float uu = (p.x - minX) / worldW;
                    float vv = (p.z - minZ) / worldH;
                    MarkSeed(seedOcean, ownerOcean, uu, vv, ti);
                }
            }
        }

        // If no water seeds at all, remove mesh
        int sdfLen = wPts * hPts;
        int seedRiverCount = 0, seedLakeCount = 0, seedOceanCount = 0;
        for (int i = 0; i < sdfLen; i++) if (seedRiver[i]) seedRiverCount++;
        if (seedLake != null) for (int i = 0; i < sdfLen; i++) if (seedLake[i]) seedLakeCount++;
        if (seedOcean != null) for (int i = 0; i < sdfLen; i++) if (seedOcean[i]) seedOceanCount++;
        bool anySeed = seedRiverCount > 0 || seedLakeCount > 0 || seedOceanCount > 0;

        if (ShouldRunDiagnostics() || debugWaterVerbose)
            Debug.Log($"[HexMapChunkManager][SDF] Seed counts: river={seedRiverCount}, lake={seedLakeCount}, ocean={seedOceanCount}");

        if (!anySeed)
        {
            // Return pooled arrays before early exit
            ArrayPoolUtils.ReturnBool(seedRiver);
            if (seedLake != null) ArrayPoolUtils.ReturnBool(seedLake);
            if (seedOcean != null) ArrayPoolUtils.ReturnBool(seedOcean);
            ArrayPoolUtils.ReturnInt(ownerRiver);
            if (ownerLake != null) ArrayPoolUtils.ReturnInt(ownerLake);
            if (ownerOcean != null) ArrayPoolUtils.ReturnInt(ownerOcean);
            DestroyRiverSurface();
            Debug.LogWarning("[HexMapChunkManager][SDF] No water seeds — unified water mesh not built. Check waterType on tiles.");
            yield break;
        }

        // --- Approximate Euclidean distance transform (2-pass chamfer) in WORLD units ---
        float INF = 1e20f;
        var distRiver = ArrayPoolUtils.RentFloat(wPts * hPts);
        for (int i = 0; i < wPts * hPts; i++) distRiver[i] = seedRiver[i] ? 0f : INF;
        float[] distLake = null;
        if (seedLake != null)
        {
            distLake = ArrayPoolUtils.RentFloat(wPts * hPts);
            for (int i = 0; i < wPts * hPts; i++) distLake[i] = seedLake[i] ? 0f : INF;
        }
        float[] distOcean = null;
        if (seedOcean != null)
        {
            distOcean = ArrayPoolUtils.RentFloat(wPts * hPts);
            for (int i = 0; i < wPts * hPts; i++) distOcean[i] = seedOcean[i] ? 0f : INF;
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
        if (distOcean != null) DistanceTransformInPlace(distOcean, ownerOcean);
        yield return null; // Yield after distance transform (heavy)

        // Scalar field: f = min(distRiver - isoRiver, distLake - isoLake, distOcean - isoOcean). Inside when f <= 0.
        float FAt(int ix, int iy)
        {
            int idx = iy * wPts + ix;
            float f = distRiver[idx] - isoRiver;
            if (distLake != null) f = Mathf.Min(f, distLake[idx] - isoLake);
            if (distOcean != null) f = Mathf.Min(f, distOcean[idx] - isoOcean);
            return f;
        }

        // Helper: classify which water type "wins" at a grid point (closest SDF).
        // 0 = river, 1 = lake, 2 = ocean
        int WaterTypeAt(int ix, int iy)
        {
            int idx = iy * wPts + ix;
            float fR = distRiver[idx] - isoRiver;
            float best = fR;
            int type = 0;
            if (distLake != null) { float fL = distLake[idx] - isoLake; if (fL < best) { best = fL; type = 1; } }
            if (distOcean != null) { float fO = distOcean[idx] - isoOcean; if (fO < best) { best = fO; type = 2; } }
            return type;
        }

        // --- Marching squares filled mesh for inside region (dist <= iso) ---
        var verts = new System.Collections.Generic.List<Vector3>(65536);
        var cols = new System.Collections.Generic.List<Color>(65536);
        var norms = new System.Collections.Generic.List<Vector3>(65536);
        var tris = new System.Collections.Generic.List<int>(131072);

        int[] cornerVert = ArrayPoolUtils.RentInt(wPts * hPts);
        for (int i = 0; i < wPts * hPts; i++) cornerVert[i] = -1;

        int[] horizEdge = ArrayPoolUtils.RentInt(wCells * (hCells + 1));        // edge between (x,y) and (x+1,y)
        int[] vertEdge = ArrayPoolUtils.RentInt((wCells + 1) * hCells);         // edge between (x,y) and (x,y+1)
        for (int i = 0; i < wCells * (hCells + 1); i++) horizEdge[i] = -1;
        for (int i = 0; i < (wCells + 1) * hCells; i++) vertEdge[i] = -1;

        int lutW = bakeResult.width > 0 ? bakeResult.width : textureWidth;
        int lutH = bakeResult.height > 0 ? bakeResult.height : textureHeight;

        // Classify the water type at a UV using the SDF (not the LUT).
        // Returns: 0=river, 1=lake, 2=ocean
        int ClassifyWaterAt(float u, float v)
        {
            u = Mathf.Repeat(u, 1f);
            v = Mathf.Clamp01(v);
            int ix = Mathf.Clamp(Mathf.RoundToInt(u * wCells), 0, wCells);
            int iy = Mathf.Clamp(Mathf.RoundToInt(v * hCells), 0, hCells);
            return WaterTypeAt(ix, iy);
        }

        Color SampleWaterColor(float u, float v)
        {
            int wType = ClassifyWaterAt(u, v);

            if (wType == 1) // lake
                return new Color(0.5f, 0.5f, 0f, 2f / 3f);

            if (wType == 2) // ocean — encode as ocean alpha (1/3)
                return new Color(0.5f, 0.5f, 0f, 1f / 3f);

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

        float SampleWaterY(float u, float v)
        {
            u = Mathf.Repeat(u, 1f);
            v = Mathf.Clamp01(v);
            int wType = ClassifyWaterAt(u, v);
            int ix = Mathf.Clamp(Mathf.RoundToInt(u * wCells), 0, wCells);
            int iy = Mathf.Clamp(Mathf.RoundToInt(v * hCells), 0, hCells);
            int idx = iy * wPts + ix;

            if (wType == 2) // ocean — flat at sea level
                return (useManualOceanWaterY ? manualOceanWaterY : planetGenerator.SeaLevelWorldY) + waterYOffset + riverSurfaceLift;

            // Lake or river: use propagated owner tile for waterElevation
            int tIndex = -1;
            if (wType == 1 && ownerLake != null) tIndex = ownerLake[idx];
            else if (ownerRiver != null) tIndex = ownerRiver[idx];

            if (tIndex >= 0 && planetGenerator.data.TryGetValue(tIndex, out var td) && (td.waterType == TileWaterType.River || td.waterType == TileWaterType.Lake))
                return flatY + td.waterElevation * displacementStrength + waterYOffset + riverSurfaceLift;

            // Fallback to hugging terrain
            float elev = heightmapTexture != null ? heightmapTexture.GetPixelBilinear(u, v).r : 0f;
            return flatY + elev * displacementStrength + waterYOffset + riverSurfaceLift;
        }

        int GetCorner(int x, int y)
        {
            int idx = y * wPts + x;
            int vi = cornerVert[idx];
            if (vi >= 0) return vi;

            float u = (float)x / wCells;
            float v = (float)y / hCells;
            float wx = minX + u * worldW;
            float wz = minZ + v * worldH;
            float wy = SampleWaterY(u, v);

            vi = verts.Count;
            verts.Add(new Vector3(wx - mgrPos.x, wy - mgrPos.y, wz - mgrPos.z));
            cols.Add(SampleWaterColor(u, v));
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
            float wx = minX + u * worldW;
            float wz = minZ + v * worldH;
            float wy = SampleWaterY(u, v);

            vi = verts.Count;
            verts.Add(new Vector3(wx - mgrPos.x, wy - mgrPos.y, wz - mgrPos.z));
            cols.Add(SampleWaterColor(u, v));
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
            float wx = minX + u * worldW;
            float wz = minZ + v * worldH;
            float wy = SampleWaterY(u, v);

            vi = verts.Count;
            verts.Add(new Vector3(wx - mgrPos.x, wy - mgrPos.y, wz - mgrPos.z));
            cols.Add(SampleWaterColor(u, v));
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
            float wx = minX + u * worldW;
            float wz = minZ + v * worldH;
            float wy = SampleWaterY(u, v);
            int vi = verts.Count;
            verts.Add(new Vector3(wx - mgrPos.x, wy - mgrPos.y, wz - mgrPos.z));
            cols.Add(SampleWaterColor(u, v));
            norms.Add(Vector3.up);
            return vi;
        }

        const int marchingSquaresRowsPerBatch = 32;
        for (int y = 0; y < hCells; y++)
        {
            if (y > 0 && y % marchingSquaresRowsPerBatch == 0)
                yield return null; // Batch marching squares to avoid frame freeze

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
            if (ShouldRunDiagnostics() || debugWaterVerbose)
            {
                Debug.LogWarning($"[HexMapChunkManager][SDF] Marching squares produced < 3 triangles (tris={tris.Count}). iso: river={isoRiver:F3}, lake={isoLake:F3}, ocean={isoOcean:F3}, minIso={minIso:F4}, dx={dx:F4}, dz={dz:F4}, cells={wCells}x{hCells}, seeds: river={seedRiverCount}, lake={seedLakeCount}, ocean={seedOceanCount}");
            }
            // Return all pooled arrays before early exit
            ArrayPoolUtils.ReturnBool(seedRiver);
            if (seedLake != null) ArrayPoolUtils.ReturnBool(seedLake);
            if (seedOcean != null) ArrayPoolUtils.ReturnBool(seedOcean);
            ArrayPoolUtils.ReturnFloat(distRiver);
            if (distLake != null) ArrayPoolUtils.ReturnFloat(distLake);
            if (distOcean != null) ArrayPoolUtils.ReturnFloat(distOcean);
            ArrayPoolUtils.ReturnInt(ownerRiver);
            if (ownerLake != null) ArrayPoolUtils.ReturnInt(ownerLake);
            if (ownerOcean != null) ArrayPoolUtils.ReturnInt(ownerOcean);
            ArrayPoolUtils.ReturnInt(cornerVert);
            ArrayPoolUtils.ReturnInt(horizEdge);
            ArrayPoolUtils.ReturnInt(vertEdge);
            DestroyRiverSurface();
            Debug.LogWarning($"[HexMapChunkManager][SDF] Marching squares produced < 3 triangles (tris={tris.Count}) — unified water mesh not built. Check iso values or seed distribution.");
            yield break;
        }
        // Release SDF grid arrays — marching squares is done, only the vert/tri lists matter now.
        ArrayPoolUtils.ReturnBool(seedRiver); seedRiver = null;
        if (seedLake != null) { ArrayPoolUtils.ReturnBool(seedLake); seedLake = null; }
        if (seedOcean != null) { ArrayPoolUtils.ReturnBool(seedOcean); seedOcean = null; }
        ArrayPoolUtils.ReturnFloat(distRiver); distRiver = null;
        if (distLake != null) { ArrayPoolUtils.ReturnFloat(distLake); distLake = null; }
        if (distOcean != null) { ArrayPoolUtils.ReturnFloat(distOcean); distOcean = null; }
        ArrayPoolUtils.ReturnInt(ownerRiver); ownerRiver = null;
        if (ownerLake != null) { ArrayPoolUtils.ReturnInt(ownerLake); ownerLake = null; }
        if (ownerOcean != null) { ArrayPoolUtils.ReturnInt(ownerOcean); ownerOcean = null; }
        ArrayPoolUtils.ReturnInt(cornerVert); cornerVert = null;
        ArrayPoolUtils.ReturnInt(horizEdge); horizEdge = null;
        ArrayPoolUtils.ReturnInt(vertEdge); vertEdge = null;

        yield return null;

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
        _riverSurfaceMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // SDF grid can exceed 65k verts
        _riverSurfaceMesh.name = extrudeInlandWaterToVolume ? "UnifiedWaterVolume" : "UnifiedWaterSurface";
        _riverSurfaceMesh.SetVertices(verts);
        _riverSurfaceMesh.SetColors(cols);
        _riverSurfaceMesh.SetTriangles(tris, 0);
        if (norms != null && norms.Count == verts.Count) _riverSurfaceMesh.SetNormals(norms);
        else _riverSurfaceMesh.RecalculateNormals();
        _riverSurfaceMesh.RecalculateBounds();

        var mf = _riverSurfaceObj.GetComponent<MeshFilter>();
        mf.sharedMesh = _riverSurfaceMesh;

        if (ShouldRunDiagnostics() || debugWaterVerbose)
            Debug.Log($"[HexMapChunkManager][SDF] Unified water mesh built: verts={verts.Count}, tris={tris.Count / 3}, extruded={extrudeInlandWaterToVolume}, iso: river={isoRiver:F3}, lake={isoLake:F3}, ocean={isoOcean:F3}, cells={wCells}x{hCells}, seeds: river={seedRiverCount}, lake={seedLakeCount}, ocean={seedOceanCount}");

        // Ensure ghosts are updated immediately after rebuild.
        if (enableWrap)
            UpdateGlobalWaterGhostPositions(_riverSurfaceObj.transform.localPosition.x);
    }

    private void EnsureRiverSurfaceObject()
    {
        string objName = extrudeInlandWaterToVolume ? "UnifiedWaterVolume" : "UnifiedWaterSurface";

        if (_riverSurfaceObj == null)
        {
            _riverSurfaceObj = new GameObject(objName);
            _riverSurfaceObj.transform.SetParent(transform, false);
            _riverSurfaceObj.transform.localPosition = Vector3.zero;
            _riverSurfaceObj.transform.localRotation = Quaternion.identity;
            _riverSurfaceObj.transform.localScale = Vector3.one;
            _riverSurfaceObj.layer = gameObject.layer;

            var mf = _riverSurfaceObj.AddComponent<MeshFilter>();
            var mr = _riverSurfaceObj.AddComponent<MeshRenderer>();
            mr.sharedMaterial = waterMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.allowOcclusionWhenDynamic = false;
        }
        else
        {
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
        if (_riverSurfaceGhostL != null) { DestroyImmediate(_riverSurfaceGhostL); _riverSurfaceGhostL = null; }
        if (_riverSurfaceGhostR != null) { DestroyImmediate(_riverSurfaceGhostR); _riverSurfaceGhostR = null; }
        if (_riverSurfaceMesh != null)
        {
            DestroyImmediate(_riverSurfaceMesh);
            _riverSurfaceMesh = null;
        }
    }

    // =====================================================================================
    //  Ocean Plane (fast, low-memory, always present)
    // =====================================================================================
    private GameObject _oceanPlaneObj;
    private Mesh _oceanPlaneMesh;
    private GameObject _oceanPlaneGhostL;
    private GameObject _oceanPlaneGhostR;

    private GameObject _riverSurfaceGhostL;
    private GameObject _riverSurfaceGhostR;

    private static GameObject EnsureGhostMeshObject(GameObject source, ref GameObject ghostObj, string ghostName, Transform parent, int layer)
    {
        if (source == null) return null;
        if (ghostObj == null)
        {
            ghostObj = new GameObject(ghostName);
            ghostObj.transform.SetParent(parent, false);
            ghostObj.transform.localPosition = Vector3.zero;
            ghostObj.transform.localRotation = Quaternion.identity;
            ghostObj.transform.localScale = Vector3.one;
            ghostObj.layer = layer;

            ghostObj.AddComponent<MeshFilter>();
            ghostObj.AddComponent<MeshRenderer>();
        }

        var srcMF = source.GetComponent<MeshFilter>();
        var srcMR = source.GetComponent<MeshRenderer>();
        var dstMF = ghostObj.GetComponent<MeshFilter>();
        var dstMR = ghostObj.GetComponent<MeshRenderer>();
        if (srcMF != null && dstMF != null) dstMF.sharedMesh = srcMF.sharedMesh;
        if (srcMR != null && dstMR != null)
        {
            dstMR.sharedMaterial = srcMR.sharedMaterial;
            dstMR.shadowCastingMode = srcMR.shadowCastingMode;
            dstMR.receiveShadows = srcMR.receiveShadows;
            dstMR.allowOcclusionWhenDynamic = srcMR.allowOcclusionWhenDynamic;
        }

        return ghostObj;
    }

    private void UpdateGlobalWaterGhostPositions(float baseOffsetX)
    {
        if (!enableWrap) return;
        if (mapWidth <= 0.001f) return;

        float leftX = baseOffsetX - mapWidth;
        float rightX = baseOffsetX + mapWidth;

        if (_oceanPlaneObj != null)
        {
            EnsureGhostMeshObject(_oceanPlaneObj, ref _oceanPlaneGhostL, "OceanPlane_GhostL", transform, gameObject.layer);
            EnsureGhostMeshObject(_oceanPlaneObj, ref _oceanPlaneGhostR, "OceanPlane_GhostR", transform, gameObject.layer);
            if (_oceanPlaneGhostL != null)
            {
                var lp = _oceanPlaneGhostL.transform.localPosition;
                _oceanPlaneGhostL.transform.localPosition = new Vector3(leftX, lp.y, lp.z);
            }
            if (_oceanPlaneGhostR != null)
            {
                var lp = _oceanPlaneGhostR.transform.localPosition;
                _oceanPlaneGhostR.transform.localPosition = new Vector3(rightX, lp.y, lp.z);
            }
        }

        if (_riverSurfaceObj != null)
        {
            EnsureGhostMeshObject(_riverSurfaceObj, ref _riverSurfaceGhostL, _riverSurfaceObj.name + "_GhostL", transform, gameObject.layer);
            EnsureGhostMeshObject(_riverSurfaceObj, ref _riverSurfaceGhostR, _riverSurfaceObj.name + "_GhostR", transform, gameObject.layer);
            if (_riverSurfaceGhostL != null)
            {
                var lp = _riverSurfaceGhostL.transform.localPosition;
                _riverSurfaceGhostL.transform.localPosition = new Vector3(leftX, lp.y, lp.z);
            }
            if (_riverSurfaceGhostR != null)
            {
                var lp = _riverSurfaceGhostR.transform.localPosition;
                _riverSurfaceGhostR.transform.localPosition = new Vector3(rightX, lp.y, lp.z);
            }
        }
    }

    private void BuildOceanPlane()
    {
        if (!enableOceanPlane || waterMaterial == null || grid == null || !grid.IsBuilt)
        {
            DestroyOceanPlane();
            return;
        }

        // Compute extents from tile centers
        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
        for (int i = 0; i < grid.TileCount; i++)
        {
            Vector3 c = grid.tileCenters[i];
            if (c.x < minX) minX = c.x;
            if (c.x > maxX) maxX = c.x;
            if (c.z < minZ) minZ = c.z;
            if (c.z > maxZ) maxZ = c.z;
        }

        float hexSize = ComputeHexSize();
        float pad = Mathf.Max(0f, oceanPlanePaddingHex) * hexSize;
        minX -= pad; maxX += pad;
        minZ -= pad; maxZ += pad;

        float y = (useManualOceanWaterY ? manualOceanWaterY : (planetGenerator != null ? planetGenerator.SeaLevelWorldY : 0f)) + waterYOffset;
        Vector3 mgrPos = transform.position;

        // Ensure object
        if (_oceanPlaneObj == null)
        {
            _oceanPlaneObj = new GameObject("OceanPlane");
            _oceanPlaneObj.transform.SetParent(transform, false);
            _oceanPlaneObj.transform.localPosition = Vector3.zero;
            _oceanPlaneObj.transform.localRotation = Quaternion.identity;
            _oceanPlaneObj.transform.localScale = Vector3.one;
            _oceanPlaneObj.layer = gameObject.layer;

            _oceanPlaneObj.AddComponent<MeshFilter>();
            var mr = _oceanPlaneObj.AddComponent<MeshRenderer>();
            mr.sharedMaterial = waterMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.allowOcclusionWhenDynamic = false;
        }

        if (_oceanPlaneMesh == null) _oceanPlaneMesh = new Mesh();
        _oceanPlaneMesh.name = "OceanPlane";
        _oceanPlaneMesh.Clear();

        // 4 verts, 2 tris
        Vector3 v0 = new Vector3(minX - mgrPos.x, y - mgrPos.y, minZ - mgrPos.z);
        Vector3 v1 = new Vector3(maxX - mgrPos.x, y - mgrPos.y, minZ - mgrPos.z);
        Vector3 v2 = new Vector3(maxX - mgrPos.x, y - mgrPos.y, maxZ - mgrPos.z);
        Vector3 v3 = new Vector3(minX - mgrPos.x, y - mgrPos.y, maxZ - mgrPos.z);

        _oceanPlaneMesh.SetVertices(new System.Collections.Generic.List<Vector3> { v0, v1, v2, v3 });
        _oceanPlaneMesh.SetUVs(0, new System.Collections.Generic.List<Vector2> {
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f)
        });
        // Encode as Ocean in vertexColor.a (1/3)
        Color cOcean = new Color(0.5f, 0.5f, 0f, 1f / 3f);
        _oceanPlaneMesh.SetColors(new System.Collections.Generic.List<Color> { cOcean, cOcean, cOcean, cOcean });
        _oceanPlaneMesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
        _oceanPlaneMesh.RecalculateNormals();
        _oceanPlaneMesh.RecalculateBounds();

        var mf = _oceanPlaneObj.GetComponent<MeshFilter>();
        mf.sharedMesh = _oceanPlaneMesh;

        // Ensure ghosts are updated immediately after build.
        if (enableWrap)
            UpdateGlobalWaterGhostPositions(_oceanPlaneObj.transform.localPosition.x);
    }

    private void DestroyOceanPlane()
    {
        if (_oceanPlaneObj != null)
        {
            DestroyImmediate(_oceanPlaneObj);
            _oceanPlaneObj = null;
        }
        if (_oceanPlaneGhostL != null) { DestroyImmediate(_oceanPlaneGhostL); _oceanPlaneGhostL = null; }
        if (_oceanPlaneGhostR != null) { DestroyImmediate(_oceanPlaneGhostR); _oceanPlaneGhostR = null; }
        if (_oceanPlaneMesh != null)
        {
            DestroyImmediate(_oceanPlaneMesh);
            _oceanPlaneMesh = null;
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

            // Copy Water child mesh for seamless water wrap
            CopyChildMeshToGhost(sourceChunk.transform, ghostChunk.transform, "Water", waterMaterial);
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

        // Keep global (non-chunk) water meshes aligned with wrap period
        UpdateGlobalWaterWrap(cameraX);
    }

    // Global water meshes (UnifiedWaterVolume/Surface, OceanPlane) are not parented to columns,
    // so they must be shifted by whole map widths to stay aligned with the teleported columns.
    private int _globalWaterWrapOffset = int.MinValue;
    private void UpdateGlobalWaterWrap(float cameraXLocal)
    {
        if (!enableWrap) return;
        if (mapWidth <= 0.001f) return;

        float halfMap = mapWidth * 0.5f;
        int desired = Mathf.FloorToInt((cameraXLocal + halfMap) / mapWidth);
        if (desired == _globalWaterWrapOffset) return;
        _globalWaterWrapOffset = desired;

        float offsetX = desired * mapWidth;

        if (_oceanPlaneObj != null)
        {
            var lp = _oceanPlaneObj.transform.localPosition;
            _oceanPlaneObj.transform.localPosition = new Vector3(offsetX, lp.y, lp.z);
        }

        if (_riverSurfaceObj != null)
        {
            var lp = _riverSurfaceObj.transform.localPosition;
            _riverSurfaceObj.transform.localPosition = new Vector3(offsetX, lp.y, lp.z);
        }

        // Maintain ±mapWidth ghost copies so water stays visible across seam.
        UpdateGlobalWaterGhostPositions(offsetX);

        if (debugWrap)
            Debug.Log($"[HexMapChunkManager][WRAP] GlobalWater offset={offsetX:F3} (period={desired}) camX={cameraXLocal:F3} mapW={mapWidth:F3}");
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
                    chunks[x, z].ForceRefresh();
            }
        }
    }

    /// <summary>
    /// Batched version: refresh chunks with yield every N chunks to avoid frame freeze.
    /// </summary>
    private System.Collections.IEnumerator RefreshAllChunksCoroutine()
    {
        if (chunks == null) yield break;
        int batchSize = Mathf.Max(1, chunksPerBatch);
        int count = 0;
        for (int x = 0; x < chunksX; x++)
        {
            for (int z = 0; z < chunksZ; z++)
            {
                if (chunks[x, z] != null)
                {
                    chunks[x, z].ForceRefresh();
                    count++;
                    if (count >= batchSize) { count = 0; yield return null; }
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

        BuildWaterMeshForChunk(chunk, out _, out _, out _);
        // Foam removed
        // Rivers are rendered as a single continuous mesh when enabled.
        // Rebuild the whole river surface if a river tile changed (cheap at low SDF resolution).
        if (enableContinuousRiverSurface)
        {
            try
            {
                if (planetGenerator.data.TryGetValue(tileIndex, out var td) &&
                    (td.waterType == TileWaterType.River
                     || (continuousWaterIncludesLakes && td.waterType == TileWaterType.Lake)
                     || (continuousWaterIncludesOcean && td.waterType == TileWaterType.Ocean)))
                    StartCoroutine(BuildContinuousRiverSurfaceMeshCoroutine());
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
            sharedMaterial.SetTexture("_BiomeHeightArray", null);
            sharedMaterial.SetTexture("_BiomeIndexMap", null);
            sharedMaterial.SetTexture("_Heightmap", null);
            sharedMaterial.SetTexture("_BiomeSurfaceMapTex", null);
            sharedMaterial.SetTexture("_BiomeEmissiveMapTex", null);
            sharedMaterial.SetTexture("_LUT", null);
            sharedMaterial.SetTexture("_SliceToBiomeMap", null);
            sharedMaterial.SetTexture("_CliffAlbedoArray", null);
            sharedMaterial.SetTexture("_CliffNormalArray", null);
        }

        // Destroy Texture2DArray / Texture2D resources
        if (biomeAlbedoArray != null) { UnityEngine.Object.DestroyImmediate(biomeAlbedoArray); biomeAlbedoArray = null; }
        if (biomeNormalArray != null) { UnityEngine.Object.DestroyImmediate(biomeNormalArray); biomeNormalArray = null; }
        if (biomeMaskArray != null) { UnityEngine.Object.DestroyImmediate(biomeMaskArray); biomeMaskArray = null; }
        if (biomeEmissiveArray != null) { UnityEngine.Object.DestroyImmediate(biomeEmissiveArray); biomeEmissiveArray = null; }

        // IMPORTANT:
        // Cliff arrays are typically assigned in the inspector as project assets (serialized fields).
        // Destroying them here breaks cliffs at runtime after any rebuild/unload cycle.
        // We only clear the material bindings above; we do NOT destroy or null the serialized refs.

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
