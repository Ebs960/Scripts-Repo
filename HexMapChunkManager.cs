using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;

/// <summary>
/// Burst job that fills a BiomeIndexMap texture (RGFloat) from pre-computed tile-to-slice
/// and tile-to-biome lookups. R = surface slice index, G = biome index.
/// Storing the biome index directly avoids the lossy SliceToBiomeMap reverse lookup
/// which fails when multiple biomes share the same surface family/slice.
/// </summary>
[BurstCompile]
struct FillBiomeIndexMapJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<int> lut;
    [ReadOnly] public NativeArray<int> tileSliceIndex;
    [ReadOnly] public NativeArray<int> tileBiomeIndex;
    public NativeArray<float2> pixels;

    public void Execute(int i)
    {
        int tileIndex = lut[i];
        if (tileIndex >= 0 && tileIndex < tileSliceIndex.Length)
            pixels[i] = new float2((float)tileSliceIndex[tileIndex], (float)tileBiomeIndex[tileIndex]);
        else
            pixels[i] = new float2(0f, 0f);
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

public enum TerrainDebugMode
{
    [InspectorName("Off - Normal Terrain Rendering")]
    Off = 0,

    [InspectorName("1 - Raw Albedo Before HDRP Lighting")]
    RawAlbedo = 1,

    [InspectorName("2 - Surface Slice / Biome Index")]
    SliceAndBiomeIndex = 2,

    [InspectorName("3 - World Normal")]
    WorldNormal = 3,

    [InspectorName("4 - Mask Map RGB")]
    MaskMap = 4,

    [InspectorName("5 - HDRP Lit With Exposure")]
    HdrpLitWithExposure = 5,

    [InspectorName("6 - Fallback Lit")]
    FallbackLit = 6,

    [InspectorName("7 - HDRP Lit Without Exposure")]
    HdrpLitNoExposure = 7,

    [InspectorName("8 - Raw Metallic Channel")]
    RawMetallic = 8,

    [InspectorName("9 - Raw AO Channel")]
    RawAO = 9,

    [InspectorName("10 - Raw Smoothness Channel")]
    RawSmoothness = 10,

    [InspectorName("11 - Computed PBR Values")]
    ComputedPBR = 11,

    [InspectorName("12 - HDRP Diffuse Only")]
    HdrpDiffuseOnly = 12,

    [InspectorName("13 - HDRP Specular Only")]
    HdrpSpecularOnly = 13,

    [InspectorName("14 - Baked Diffuse / SH")]
    BakedDiffuseSH = 14,

    [InspectorName("15 - Exposure Multiplier")]
    ExposureMultiplier = 15
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
public enum TerrainRenderPath
{
    CustomBiomeShader,
    BakedHdrpLit
}

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
    [SerializeField]
    [Tooltip("Ice surface texture database (albedos, normals, tints, tiling) for lake and river freeze visuals. " +
             "Must match the IceSurfaceDatabase assigned to ClimateManager.")]
    private IceSurfaceDatabase iceSurfaceDatabase;

    [Header("Terrain Render Path")]
    [SerializeField] private TerrainRenderPath terrainRenderPath = TerrainRenderPath.BakedHdrpLit;

    [Header("Baked HDRP Lit Terrain")]
    [SerializeField] private Material bakedLitTerrainMaterialTemplate;

    [SerializeField]
    [Tooltip("Resolution width for baked HDRP/Lit terrain maps. Use 2048 initially; can be reduced for testing.")]
    private int bakedTerrainTextureWidth = 2048;

    [SerializeField]
    [Tooltip("Resolution height for baked HDRP/Lit terrain maps. Use 1024 or 2048 initially.")]
    private int bakedTerrainTextureHeight = 1024;

    [SerializeField]
    [Tooltip("When true, bakes simple biome/minimap colors. Disable to bake BaseColorMap from resolved surface-family albedo texture slices.")]
    private bool bakedTerrainUseSimpleBiomeColors = true;

    [SerializeField]
    [Tooltip("If true, keeps baked textures CPU-readable for debugging. Disable for production to save memory.")]
    private bool keepBakedTerrainTexturesReadable = false;

    [SerializeField] private bool forceRebakeBakedLitTerrain;

    [Header("Baked HDRP Lit Terrain Diagnostics")]
    [SerializeField] private bool debugBakedTerrainResolution = false;
    [SerializeField] private bool debugBakedTerrainOnlyProblemBiomes = true;
    [SerializeField] private int debugBakedTerrainSamplesPerBiome = 10;
    [SerializeField] private bool exportBakedTerrainDebugPng = false;
    [SerializeField] private string bakedTerrainDebugExportFolder = "Assets/TerrainDebug";
    [SerializeField] private bool exportProblemBiomeSlices = false;
    [SerializeField] private bool clearSurfaceLibraryCacheBeforeBuild = true;

    private Material bakedLitTerrainMaterial;
    private Texture2D bakedTerrainBaseColor;
    private Texture2D bakedTerrainMaskMap;

    [Header("Orbit Overlay")]
    [Tooltip("Shader used for the transparent orbit highlight overlay mesh (auto-found if null).")]
    [SerializeField] private Shader orbitOverlayShader;
    
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
    [Min(0.01f)]
    [SerializeField] private float globalSnowTransitionDuration = 3f;
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
    [Tooltip("Strength multiplier for heightmap-derived displaced normals")]
    [Range(0.01f, 5f)]
    [SerializeField] private float normalStrength = 1.0f;
    [Tooltip("Strength multiplier for biome normal maps (surface bump detail). Higher = more visible texture bumps.")]
    [Range(0f, 5f)]
    [SerializeField] private float biomeNormalStrength = 1.0f;
    [Tooltip("Radius (in texels) used when sampling normals/heightmap for normal computation")]
    [Range(1f, 12f)]
    [SerializeField] private float normalSampleRadius = 4f;
    [Tooltip("Radius (in texels) used for biome blending between neighboring biome slices")]
    [Range(0f, 16f)]
    [SerializeField] private float biomeBlendRadius = 4f;
    [Tooltip("Blend sharpness used when blending biome surfaces by height")]
    [Range(0.01f, 10f)]
    [SerializeField] private float biomeBlendSharpness = 3f;

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

    [Header("Terrain Shader Debug")]
    [SerializeField]
    [Tooltip(
        "Controls _TerrainDebugMode on the runtime terrain material.\n\n" +
        "Off: Normal terrain rendering.\n" +
        "Raw Albedo: Shows terrain texture color before HDRP lighting/exposure.\n" +
        "Slice/Biome Index: Shows debug colors for texture slice and biome index.\n" +
        "World Normal: Shows world-space normals as colors.\n" +
        "Mask Map: Shows mask map RGB channels.\n" +
        "HDRP Lit With Exposure: Shows HDRP lighting multiplied by exposure.\n" +
        "Fallback Lit: Shows the shader fallback lighting path.\n" +
        "HDRP Lit Without Exposure: Shows HDRP lighting before exposure.\n" +
        "Raw Metallic/AO/Smoothness: Shows individual mask-map PBR channels.\n" +
        "Computed PBR Values: R=metallic, G=AO/spec occlusion, B=smoothness.\n" +
        "HDRP Diffuse/Specular/Baked/Exposure: Splits HDRP lighting contributions."
    )]
    private TerrainDebugMode terrainDebugMode = TerrainDebugMode.Off;

    [Header("Terrain Surface Probe")]
    [SerializeField]
    [Tooltip("When enabled, pressing the probe key logs the terrain tile under the cursor, its resolved surface family/slice, material multipliers, computed PBR values, and an AsyncGPUReadback sample of the runtime mask array slice at the exact rendered UV.")]
    private bool enableTerrainSurfaceProbe = false;
    [SerializeField]
    [Tooltip("Key used when Terrain Surface Probe is enabled to log the currently hovered terrain tile.")]
    private Key terrainSurfaceProbeKey = Key.P;

    private TerrainDebugMode _lastTerrainDebugMode = (TerrainDebugMode)(-999);

    private float _targetGlobalSnowAmount = 0f;
    private float _currentGlobalSnowAmount = 0f;
    private static readonly int _GlobalSnowAmountID = Shader.PropertyToID("_GlobalSnowAmount");

    [Header("Diagnostics")]
    [Tooltip("Logs the full transform parent chain when building chunks (helps find unexpected rotation/offset).")]
    [SerializeField] private bool logTransformChainOnBuild = true;
    [Tooltip("Logs whenever this manager's transform changes at runtime (position/rotation/scale).")]
    [SerializeField] private bool debugTransformChanges = false;
    [Tooltip("When enabled, dump per-biome tint values and slice->biome map samples to the Console for debugging.")]
    [SerializeField] private bool debugBiomeDetails = false;
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
    [Tooltip("Additional offset applied only to ocean/coast/seas water. Use a small negative value to keep shoreline water slightly below the coast mesh.")]
    [SerializeField] private float shorelineWaterOffset = 0.15f;
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
    private readonly HashSet<int> _solidFrozenWaterTiles = new HashSet<int>();

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
    // Base surface mapping: x=startSlice, y=variantCount, z=surfaceIndex, w=forcedVariant
    private Vector4[] biomeSurfaceMapArray;
    // Mountain override mapping: x=startSlice, y=variantCount, z=surfaceIndex, w=forcedVariant
    private Vector4[] biomeMountainSurfaceMapArray;
    private Texture2D biomeSurfaceMapTexture;
    private Texture2D biomeEmissiveMapTexture;
    private Vector4[] biomeTintArray;
    private Vector4[] biomeParamsArray;
    private Vector4[] biomeRoughnessOffsetsArray;
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

    // Wrap registry: columnIndex -> GameObjects that need teleport when a column moves
    private Dictionary<int, HashSet<GameObject>> _wrapRegistryByColumn = new Dictionary<int, HashSet<GameObject>>();
    // Reverse lookup: GameObject -> columnIndex
    private Dictionary<GameObject, int> _objectToColumn = new Dictionary<GameObject, int>();

    // Ghost object system: lightweight renderer-only clones of registered objects at wrap edges
    private struct GhostObjectEntry
    {
        public GameObject ghost;
        public bool isRightGhost;
    }
    private readonly Dictionary<GameObject, List<GhostObjectEntry>> _ghostObjects = new Dictionary<GameObject, List<GhostObjectEntry>>();
    private readonly HashSet<int> _ghostLeftSourceCols = new HashSet<int>();
    private readonly HashSet<int> _ghostRightSourceCols = new HashSet<int>();
    private Transform _ghostObjectContainer;

    // Seasonal mask sizing
    private int seasonMaskWidth;
    private int seasonMaskHeight;
    
    // Event subscriptions
    private PlanetGenerator _surfaceEventSource;
    private bool _subscribedToPlanetReady;
    
    // Coroutine tracking for async chunk building
    private Coroutine _buildCoroutine;
    
    // Public accessors (API compatible with FlatMapTextureRenderer)
        public float MapWidth => mapWidth;
    public HexGrid Grid => grid;
    public PlanetGenerator PlanetGenerator => planetGenerator;
    public int MeshSubdivisionsPerChunk => meshSubdivisionsPerChunk;
    /// <summary>
    /// The actual displacement strength used by the terrain shader (_ElevationScale).
    /// Water surfaces must use this value to match terrain vertex displacement.
    /// </summary>
    public float DisplacementStrength => displacementStrength;
    public float MapHeight => mapHeight;
    public bool IsBuilt => chunks != null;
    public Texture MapTexture => bakeResult.texture;
    public int[] LUT => bakeResult.lut;
    public int LUTWidth => bakeResult.width;
    public int LUTHeight => bakeResult.height;
    public Material SharedMaterial => sharedMaterial;
    public TerrainRenderPath RenderPath => terrainRenderPath;
    public bool UseBakedHdrpLit => terrainRenderPath == TerrainRenderPath.BakedHdrpLit;
    public float FlatY => flatY;
    public bool WrapEnabled => enableWrap;
    
    // Collider for WorldPicker (uses MeshCollider for proper UV support)
    private Collider pickingCollider;
    public Collider PickingCollider => pickingCollider;
    
    // Per-layer picking colliders (flat meshes at the correct Y for parallax-free picking)
    private Collider waterPickingCollider;
    public Collider WaterPickingCollider => waterPickingCollider;
    private Collider orbitPickingCollider;
    public Collider OrbitPickingCollider => orbitPickingCollider;

    // Orbit highlight overlay (flat transparent mesh at orbit height)
    private GameObject orbitOverlayObj;
    private Material orbitOverlayMaterial;
    public Material OrbitOverlayMaterial => orbitOverlayMaterial;

    // Water surface highlight overlay (flat transparent mesh at water level)
    private GameObject waterSurfaceOverlayObj;
    private Material waterSurfaceOverlayMaterial;
    public Material WaterSurfaceOverlayMaterial => waterSurfaceOverlayMaterial;
    
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
        
        ClimateManager.OnPlanetSeasonChanged         += HandlePlanetSeasonChanged;
        ClimateManager.OnPlanetFreezeTargetsReady     += HandleFreezeTargetsReady;
        ClimateManager.OnPlanetFreezeProgressChanged  += HandleFreezeProgressChanged;
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
        
        ClimateManager.OnPlanetSeasonChanged         -= HandlePlanetSeasonChanged;
        ClimateManager.OnPlanetFreezeTargetsReady     -= HandleFreezeTargetsReady;
        ClimateManager.OnPlanetFreezeProgressChanged  -= HandleFreezeProgressChanged;
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

        // Only update column wrapping when the camera has actually moved
        if (enableWrap && cameraTransform != null && chunks != null)
        {
            float camX = cameraTransform.position.x;
            if (Mathf.Abs(camX - _lastWrapCamX) > 0.05f)
            {
                _lastWrapCamX = camX;
                UpdateColumnWrapping();
            }
        }

        // Sync ghost object positions every frame (sources may move via teleport or gameplay)
        if (enableWrap && ghostColumnsCreated && _ghostObjects.Count > 0)
        {
            UpdateGhostObjects();
        }

        UpdateSnow();
        UpdateTerrainSurfaceProbe();

        if (forceRebakeBakedLitTerrain)
        {
            forceRebakeBakedLitTerrain = false;
            if (terrainRenderPath == TerrainRenderPath.BakedHdrpLit)
            {
                BuildBakedHdrpLitTerrainMaps();
                BuildNeutralBakedMaskMap();
            }
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

        if (_lastTerrainDebugMode != terrainDebugMode)
        {
            _lastTerrainDebugMode = terrainDebugMode;
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

        if (applied && terrainRenderPath == TerrainRenderPath.CustomBiomeShader)
        {
            ApplyBiomeMaterialSettings();
        }
    }
    
    private void UpdateTerrainSurfaceProbe()
    {
        if (!enableTerrainSurfaceProbe) return;
        if (!Application.isPlaying) return;
        if (Keyboard.current == null || !Keyboard.current[terrainSurfaceProbeKey].wasPressedThisFrame) return;

        LogTerrainSurfaceProbeUnderCursor();
    }

    private void LogTerrainSurfaceProbeUnderCursor()
    {
        if (Camera.main == null)
        {
            Debug.LogWarning("[Terrain Probe] Cannot probe terrain: Camera.main is NULL.");
            return;
        }

        if (pickingCollider == null)
        {
            Debug.LogWarning("[Terrain Probe] Cannot probe terrain: picking collider is NULL.");
            return;
        }

        Vector2 pointerPosition = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        Ray ray = Camera.main.ScreenPointToRay(pointerPosition);
        if (!pickingCollider.Raycast(ray, out RaycastHit hit, 10000f))
        {
            Debug.LogWarning($"[Terrain Probe] No terrain hit under cursor at screen={pointerPosition}.");
            return;
        }

        float u = Mathf.Repeat(hit.textureCoord.x, 1f);
        float v = Mathf.Clamp01(hit.textureCoord.y);
        int tileIndex = GetTileIndexAtUV(u, v);
        if (tileIndex < 0 || planetGenerator == null || planetGenerator.data == null || !planetGenerator.data.TryGetValue(tileIndex, out var tile))
        {
            Debug.LogWarning($"[Terrain Probe] No tile data for uv=({u:F5},{v:F5}) tile={tileIndex}.");
            return;
        }

        BiomeVisualData visual = ResolveRenderedVisual(tile);
        int biomeIndex = ResolveRenderedBiomeIndex(tile);
        int sliceIndex = ResolveSurfaceSliceIndex(tile, tileIndex, biomeIndex);
        SurfaceFamilyData surfaceFamily = visual != null ? visual.surfaceFamily : null;
        float roughnessOffset = surfaceFamily != null ? surfaceFamily.roughnessOffset : 0f;
        float metallicMultiplierValue = GetMaterialFloat(sharedMaterial, "_MetallicMultiplier", metallicMultiplier);
        float aoIntensityValue = GetMaterialFloat(sharedMaterial, "_AOIntensity", aoIntensity);
        float smoothnessMultiplierValue = GetMaterialFloat(sharedMaterial, "_SmoothnessMultiplier", smoothnessMultiplier);

        Debug.Log(
            $"[Terrain Probe] tile={tileIndex} " +
            $"uv=({u:F5},{v:F5}) " +
            $"biome={tile.biome} " +
            $"visual={(visual != null ? visual.name : "NULL")} " +
            $"visual.biome={(visual != null ? visual.biome.ToString() : "NULL")} " +
            $"surfaceFamily={(surfaceFamily != null ? surfaceFamily.name : "NULL")} " +
            $"slice={sliceIndex} " +
            $"isMountain={tile.isMountain} " +
            $"isRiver={tile.isRiver} isLake={tile.isLake} " +
            $"waterType={tile.waterType} " +
            $"globalSnow={GetMaterialFloat(sharedMaterial, "_GlobalSnowAmount", globalSnowAmount):F3} " +
            $"AOIntensity={aoIntensityValue:F3} " +
            $"MetallicMult={metallicMultiplierValue:F3} " +
            $"SmoothnessMult={smoothnessMultiplierValue:F3} " +
            $"roughnessOffset={roughnessOffset:F3}"
        );

        RequestTerrainMaskProbe(u, v, tileIndex, sliceIndex, metallicMultiplierValue, aoIntensityValue, smoothnessMultiplierValue, roughnessOffset);
    }

    private static float GetMaterialFloat(Material material, string propertyName, float fallback)
    {
        if (material == null) return fallback;
        if (!material.HasProperty(propertyName)) return fallback;
        return material.GetFloat(propertyName);
    }

    private void RequestTerrainMaskProbe(
        float u,
        float v,
        int tileIndex,
        int sliceIndex,
        float metallicMultiplierValue,
        float aoIntensityValue,
        float smoothnessMultiplierValue,
        float roughnessOffset)
    {
        if (biomeMaskArray == null)
        {
            Debug.LogWarning($"[Terrain Probe] Cannot sample mask array: biomeMaskArray is NULL for tile={tileIndex} slice={sliceIndex}.");
            return;
        }

        if (sliceIndex < 0 || sliceIndex >= biomeMaskArray.depth)
        {
            Debug.LogWarning($"[Terrain Probe] Cannot sample mask array: slice={sliceIndex} outside depth={biomeMaskArray.depth} for tile={tileIndex}.");
            return;
        }

        int maskWidth = Mathf.Max(1, biomeMaskArray.width);
        int maskHeight = Mathf.Max(1, biomeMaskArray.height);
        int x = Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(u, 1f) * maskWidth), 0, maskWidth - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(v) * maskHeight), 0, maskHeight - 1);

        AsyncGPUReadback.Request(
            biomeMaskArray,
            0,
            x,
            1,
            y,
            1,
            sliceIndex,
            1,
            TextureFormat.RGBAFloat,
            request =>
            {
                if (request.hasError)
                {
                    Debug.LogWarning($"[Terrain Probe] AsyncGPUReadback failed for tile={tileIndex} slice={sliceIndex} pixel=({x},{y}).");
                    return;
                }

                var data = request.GetData<Color>();
                if (data.Length <= 0)
                {
                    Debug.LogWarning($"[Terrain Probe] AsyncGPUReadback returned no data for tile={tileIndex} slice={sliceIndex} pixel=({x},{y}).");
                    return;
                }

                Color maskSample = data[0];
                float metallicValue = Mathf.Clamp01(maskSample.r * metallicMultiplierValue);
                float aoValue = Mathf.Clamp01(maskSample.g * aoIntensityValue);
                float smoothnessValue = Mathf.Clamp01(maskSample.a * smoothnessMultiplierValue - roughnessOffset);

                Debug.Log(
                    $"[Terrain Probe] maskSample tile={tileIndex} slice={sliceIndex} " +
                    $"uv=({u:F5},{v:F5}) pixel=({x},{y}) " +
                    $"rawRGBA=({maskSample.r:F4},{maskSample.g:F4},{maskSample.b:F4},{maskSample.a:F4}) " +
                    $"computedPBR=(metallic={metallicValue:F4}, ao={aoValue:F4}, smoothness={smoothnessValue:F4})"
                );
            });
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
            // Debug.Log — Map dimensions (disabled to reduce console noise)
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
        
        // Create per-layer picking colliders for water surface and orbit
        CreateLayerPickingColliders();
        
        // Update WorldPicker with our LUT and collider
        UpdateWorldPicker();
        
        // Create orbit highlight overlay (flat transparent mesh at orbit height)
        CreateOrbitOverlayMesh();
        
        // Create water surface highlight overlay (flat transparent mesh at water level)
        CreateWaterSurfaceOverlayMesh();
        
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

        if (debugBiomeDetails)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.Append($"[HexMapChunkManager] Building textures for {count} visual entries:\n");
                for (int i = 0; i < count; i++)
                {
                    var e = visuals[i];
                    if (e == null) sb.AppendLine($"  [{i}] <null>");
                    else sb.AppendLine($"  [{i}] {e.name} (biome={e.biome}) tint={e.tint}");
                }
                Debug.Log(sb.ToString());
            }
            catch { }
        }

        if (clearSurfaceLibraryCacheBeforeBuild)
            BiomeVisualDatabase.ClearAllCachedSurfaceLibraries();

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

            // Build per-biome mapping vectors for base and optional mountain overrides.
            biomeSurfaceMapArray = new Vector4[count];
            biomeMountainSurfaceMapArray = new Vector4[count];
            for (int i = 0; i < count; i++)
            {
                int surfaceIndex = (lib.biomeToSurfaceIndex != null && i < lib.biomeToSurfaceIndex.Length) ? lib.biomeToSurfaceIndex[i] : -1;
                if (surfaceIndex >= 0 && surfaceIndex < lib.surfaceStartSlice.Length)
                {
                    int start = lib.surfaceStartSlice[surfaceIndex];
                    int variants = lib.surfaceVariantCounts[surfaceIndex];
                    int mountainStart = (lib.surfaceMountainStartSlice != null && surfaceIndex < lib.surfaceMountainStartSlice.Length) ? lib.surfaceMountainStartSlice[surfaceIndex] : start;
                    int mountainVariants = (lib.surfaceMountainVariantCounts != null && surfaceIndex < lib.surfaceMountainVariantCounts.Length) ? lib.surfaceMountainVariantCounts[surfaceIndex] : 0;
                    int forced = (lib.biomeForcedVariant != null && i < lib.biomeForcedVariant.Length) ? lib.biomeForcedVariant[i] : -1;
                    biomeSurfaceMapArray[i] = new Vector4(start, variants, surfaceIndex, forced);
                    biomeMountainSurfaceMapArray[i] = new Vector4(mountainStart, mountainVariants, surfaceIndex, forced);
                }
                else
                {
                    biomeSurfaceMapArray[i] = new Vector4(0, 1, 0, -1);
                    biomeMountainSurfaceMapArray[i] = new Vector4(0, 0, 0, -1);
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
                        // Consolidation: use the seasonal winter response's snow value as the
                        // per-biome shader parameter. This makes the seasonal response the
                        // authoritative source for biome snow intensity.
                        float retentionFromSeason = entry.winterResponse.snow;
                        biomeParamsArray[i] = new Vector4(tiling, retentionFromSeason, entry.inherentWetness, entry.isWaterBiome ? 1f : 0f);
                    }
                else
                {
                    biomeTintArray[i] = Color.white;
                    biomeParamsArray[i] = new Vector4(1f, 0f, 0f, 0f);
                }
            }

            // Build per-biome roughness offset array (packed: 4 biomes per Vector4, 16 Vector4s = 64 biomes max)
            biomeRoughnessOffsetsArray = new Vector4[16];
            for (int i = 0; i < count; i++)
            {
                var entry = visuals[i];
                float ro = (entry != null && entry.surfaceFamily != null) ? entry.surfaceFamily.roughnessOffset : 0f;
                int vecIdx = i / 4;
                int comp = i % 4;
                var v = biomeRoughnessOffsetsArray[vecIdx];
                v[comp] = ro;
                biomeRoughnessOffsetsArray[vecIdx] = v;
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

                if (biomeMountainSurfaceMapArray == null || bi >= biomeMountainSurfaceMapArray.Length) continue;
                var mountainMap = biomeMountainSurfaceMapArray[bi];
                int mountainStart = Mathf.Max(0, Mathf.RoundToInt(mountainMap.x));
                int mountainVariantCount = Mathf.Max(0, Mathf.RoundToInt(mountainMap.y));
                for (int v = 0; v < mountainVariantCount; v++)
                {
                    int si = mountainStart + v;
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
        biomeMountainSurfaceMapArray = null;
        biomeSurfaceMapTexture = null;
        biomeEmissiveMapTexture = null;
        return;
    }

    private static int ChooseSurfaceVariant(int stableSeed, int variantCount, int forcedVariant)
    {
        if (variantCount <= 1) return 0;
        if (forcedVariant >= 0 && forcedVariant < variantCount)
            return forcedVariant;

        unchecked
        {
            int h = stableSeed * 1103515245 + 12345;
            return Mathf.Abs(h) % variantCount;
        }
    }

    private float GetOceanWaterSurfaceY(float additionalOffset = 0f)
    {
        float baseOceanY = useManualOceanWaterY
            ? manualOceanWaterY
            : (planetGenerator != null ? planetGenerator.SeaLevelWorldY : 0f);
        return baseOceanY + waterYOffset + shorelineWaterOffset + additionalOffset;
    }

    private float GetTileWaterSurfaceY(HexTileData tile, float additionalOffset = 0f)
    {
        if (tile.waterType == TileWaterType.Ocean)
            return GetOceanWaterSurfaceY(additionalOffset);

        return flatY + tile.waterElevation * displacementStrength + waterYOffset + additionalOffset;
    }

    private int ResolveSurfaceSliceIndex(HexTileData tile, int stableSeed, int biomeIndex)
    {
        int maxSlice = (biomeAlbedoArray != null) ? Mathf.Max(0, biomeAlbedoArray.depth - 1) : -1;
        int sliceIndex = 0;

        Vector4[] sourceMapArray = biomeSurfaceMapArray;
        if (tile.isMountain && biomeMountainSurfaceMapArray != null && biomeIndex >= 0 && biomeIndex < biomeMountainSurfaceMapArray.Length)
        {
            var mountainMap = biomeMountainSurfaceMapArray[biomeIndex];
            if (Mathf.RoundToInt(mountainMap.y) > 0)
                sourceMapArray = biomeMountainSurfaceMapArray;
        }

        if (sourceMapArray != null && biomeIndex >= 0 && biomeIndex < sourceMapArray.Length)
        {
            var map = sourceMapArray[biomeIndex];
            int startSlice = Mathf.Max(0, Mathf.RoundToInt(map.x));
            int variantCount = Mathf.Max(1, Mathf.RoundToInt(map.y));
            int forcedVariant = Mathf.RoundToInt(map.w);
            int chosenVariant = ChooseSurfaceVariant(stableSeed, variantCount, forcedVariant);
            sliceIndex = startSlice + chosenVariant;
        }

        if (maxSlice >= 0 && sliceIndex > maxSlice) sliceIndex = maxSlice;
        if (sliceIndex < 0) sliceIndex = 0;
        return sliceIndex;
    }

    private float GetSolidIceThreshold()
    {
        return iceSurfaceDatabase != null
            ? Mathf.Clamp01(iceSurfaceDatabase.freezeOpaqueThreshold)
            : HexTileData.FreezeSolidThreshold;
    }

    private bool IsFreezableWater(HexTileData tile)
    {
        return tile != null
               && tile.waterType != TileWaterType.None
               && tile.waterType != TileWaterType.Ocean
               && tile.biome != Biome.Lava;
    }

    private bool IsSolidFrozenWater(HexTileData tile)
    {
        return IsFreezableWater(tile) && tile.freezeAmount >= GetSolidIceThreshold();
    }

    private bool HasWaterFreezeVisuals(HexTileData tile)
    {
        if (!IsFreezableWater(tile) || iceSurfaceDatabase == null)
            return false;

        return iceSurfaceDatabase.iceAlbedoArray != null;
    }

    private static float HashToUnitFloat(int value)
    {
        unchecked
        {
            uint hash = (uint)value;
            hash ^= hash >> 16;
            hash *= 0x7feb352dU;
            hash ^= hash >> 15;
            hash *= 0x846ca68bU;
            hash ^= hash >> 16;
            return (hash & 0x00FFFFFFu) / 16777215f;
        }
    }

    private Vector4 GetWaterFreezeVertexData(HexTileData tile, int tileIndex)
    {
        if (!IsFreezableWater(tile))
            return Vector4.zero;

        float freezeTarget = Mathf.Clamp01(Mathf.Max(tile.freezeTarget, tile.freezeAmount));
        float freezeAmount = Mathf.Clamp01(tile.freezeAmount);
        float variantSeed = HashToUnitFloat(tileIndex + 1);
        return new Vector4(freezeTarget, freezeAmount, variantSeed, 0f);
    }

    private Biome ResolveFrozenWaterSurfaceBiome()
    {
        if (planetGenerator == null)
            return Biome.Glacier;

        Biome preferred = planetGenerator.planetType switch
        {
            PlanetType.Mars => Biome.MartianPolarIce,
            PlanetType.Mercury => Biome.MercurianIce,
            PlanetType.Titan => Biome.TitanIce,
            PlanetType.Europa => Biome.EuropaIce,
            PlanetType.Pluto => Biome.PlutoCryo,
            _ => planetGenerator.mapType == MapType.IceWorld ? Biome.IcicleField : Biome.Glacier,
        };

        return biomeVisualDatabase != null && biomeVisualDatabase.Get(preferred) != null
            ? preferred
            : Biome.Glacier;
    }

    private BiomeVisualData ResolveRenderedVisual(HexTileData tile)
    {
        if (tile == null || biomeVisualDatabase == null)
            return null;

        if (IsSolidFrozenWater(tile))
        {
            var frozenVisual = biomeVisualDatabase.Get(ResolveFrozenWaterSurfaceBiome());
            if (frozenVisual != null && frozenVisual.surfaceFamily != null)
                return frozenVisual;
        }

        var visual = biomeVisualDatabase.Get(tile.biome);

        if (tile.underwaterBiome != Biome.Ocean && tile.underwaterBiome != tile.biome)
        {
            var underwaterVisual = biomeVisualDatabase.Get(tile.underwaterBiome);
            if (underwaterVisual != null && underwaterVisual.surfaceFamily != null)
                visual = underwaterVisual;
        }

        return visual;
    }

    private int ResolveRenderedBiomeIndex(HexTileData tile)
    {
        var visual = ResolveRenderedVisual(tile);
        return visual != null && biomeIndexLookup.TryGetValue(visual.biome, out var idx) ? idx : 0;
    }

    private void BuildBiomeIndexMap(int width, int height)
    {
        if (bakeResult.lut == null || bakeResult.lut.Length == 0) return;

        if (biomeIndexMap == null || biomeIndexMap.width != width || biomeIndexMap.height != height)
        {
            // RGFloat: R = surface slice index, G = biome index.
            // Storing biome index directly avoids the lossy SliceToBiomeMap reverse lookup
            // which fails when multiple biomes share the same surface family/slice.
            biomeIndexMap = new Texture2D(width, height, TextureFormat.RGFloat, false, true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                name = "BiomeIndexMap"
            };
        }

        int minVal = int.MaxValue;
        int maxVal = 0;

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

                int biomeIndex = ResolveRenderedBiomeIndex(tile);
                int sliceIndex = ResolveSurfaceSliceIndex(tile, tileIndex, biomeIndex);

                if (sliceIndex < minVal) minVal = sliceIndex;
                if (sliceIndex > maxVal) maxVal = sliceIndex;

                stripPixels[localIdx] = new Color(sliceIndex, biomeIndex, 0f, 1f);
            }

            biomeIndexMap.SetPixels(0, startRow, width, rowsThisStrip, stripPixels);
        }

        biomeIndexMap.Apply(false, false);

        if (ShouldRunDiagnostics())
        {
            if (minVal == int.MaxValue) minVal = 0;
            Debug.Log($"[HexMapChunkManager][Diag] BiomeIndexMap(slice) range: {minVal}..{maxVal} (RGFloat).");
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
            biomeIndexMap = new Texture2D(width, height, TextureFormat.RGFloat, false, true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                name = "BiomeIndexMap"
            };
        }

        int minVal = int.MaxValue;
        int maxVal = 0;

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

                int biomeIndex = ResolveRenderedBiomeIndex(tile);
                int sliceIndex = ResolveSurfaceSliceIndex(tile, tileIndex, biomeIndex);

                if (sliceIndex < minVal) minVal = sliceIndex;
                if (sliceIndex > maxVal) maxVal = sliceIndex;

                stripPixels[localIdx] = new Color(sliceIndex, biomeIndex, 0f, 1f);
            }

            biomeIndexMap.SetPixels(0, startRow, width, rowsThisStrip, stripPixels);
            
            yield return null;
        }

        biomeIndexMap.Apply(false, false);

        if (ShouldRunDiagnostics())
        {
            if (minVal == int.MaxValue) minVal = 0;
            Debug.Log($"[HexMapChunkManager][Diag] BiomeIndexMap(slice) range: {minVal}..{maxVal} (RGFloat) [batched].");
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
    private void PrecomputeTileSliceAndBiomeIndices(out int[] sliceIndices, out int[] biomeIndices)
    {
        int tileCount = grid.TileCount;
        sliceIndices = ArrayPoolUtils.RentInt(tileCount);
        biomeIndices = ArrayPoolUtils.RentInt(tileCount);

        for (int ti = 0; ti < tileCount; ti++)
        {
            if (!planetGenerator.data.TryGetValue(ti, out var tile))
            {
                sliceIndices[ti] = 0;
                biomeIndices[ti] = 0;
                continue;
            }

            int biomeIndex = ResolveRenderedBiomeIndex(tile);
            biomeIndices[ti] = biomeIndex;
            sliceIndices[ti] = ResolveSurfaceSliceIndex(tile, ti, biomeIndex);
        }
    }

    /// <summary>
    /// Rebuild only the baked terrain pixels touched by the specified tiles.
    /// Updates the runtime BiomeIndexMap and Heightmap in place instead of rebaking
    /// the whole planet texture.
    /// </summary>
    public void RebakeBakedTerrainForTiles(IEnumerable<int> tileIndices)
    {
        if (planetGenerator == null || grid == null || bakeResult.lut == null || bakeResult.lut.Length == 0)
            return;
        if (biomeIndexMap == null || heightmapTexture == null)
            return;

        var biomeTiles = new HashSet<int>();
        var heightTiles = new HashSet<int>();

        foreach (int tileIndex in tileIndices)
        {
            if (tileIndex < 0 || tileIndex >= grid.TileCount)
                continue;

            biomeTiles.Add(tileIndex);
            heightTiles.Add(tileIndex);

            var neighbors = grid.neighbors != null && tileIndex < grid.neighbors.Length
                ? grid.neighbors[tileIndex]
                : null;
            if (neighbors == null)
                continue;

            foreach (int neighbor in neighbors)
            {
                if (neighbor >= 0 && neighbor < grid.TileCount)
                    heightTiles.Add(neighbor);
            }
        }

        if (biomeTiles.Count == 0 && heightTiles.Count == 0)
            return;

        int width = bakeResult.width > 0 ? bakeResult.width : textureWidth;
        bool biomeUpdated = false;
        bool heightUpdated = false;

        for (int pixelIndex = 0; pixelIndex < bakeResult.lut.Length; pixelIndex++)
        {
            int tileIndex = bakeResult.lut[pixelIndex];
            if (tileIndex < 0)
                continue;

            bool updateBiome = biomeTiles.Contains(tileIndex);
            bool updateHeight = heightTiles.Contains(tileIndex);
            if (!updateBiome && !updateHeight)
                continue;

            if (!planetGenerator.data.TryGetValue(tileIndex, out var tile))
                continue;

            int x = pixelIndex % width;
            int y = pixelIndex / width;

            if (updateBiome)
            {
                int biomeIndex = ResolveRenderedBiomeIndex(tile);
                int sliceIndex = ResolveSurfaceSliceIndex(tile, tileIndex, biomeIndex);
                biomeIndexMap.SetPixel(x, y, new Color(sliceIndex, biomeIndex, 0f, 1f));
                biomeUpdated = true;
            }

            if (updateHeight)
            {
                float elevation = GetRenderedElevation(tileIndex);
                heightmapTexture.SetPixel(x, y, new Color(elevation, 0f, 0f, 1f));
                heightUpdated = true;
            }
        }

        if (biomeUpdated)
            biomeIndexMap.Apply(false, false);
        if (heightUpdated)
            heightmapTexture.Apply(true, false);
    }

    public void RebakeBakedTerrainForTile(int tileIndex)
    {
        RebakeBakedTerrainForTiles(new[] { tileIndex });
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
            biomeIndexMap = new Texture2D(width, height, TextureFormat.RGFloat, false, true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                name = "BiomeIndexMap"
            };
        }

        int pixelCount = width * height;
        PrecomputeTileSliceAndBiomeIndices(out var tileSlice, out var tileBiome);

        var lutNative = new NativeArray<int>(bakeResult.lut, Allocator.TempJob);
        var sliceNative = new NativeArray<int>(tileSlice, Allocator.TempJob);
        var biomeNative = new NativeArray<int>(tileBiome, Allocator.TempJob);
        ArrayPoolUtils.ReturnInt(tileSlice);
        ArrayPoolUtils.ReturnInt(tileBiome);
        var pixelsNative = new NativeArray<float2>(pixelCount, Allocator.TempJob);

        new FillBiomeIndexMapJob
        {
            lut = lutNative,
            tileSliceIndex = sliceNative,
            tileBiomeIndex = biomeNative,
            pixels = pixelsNative,
        }.Schedule(pixelCount, 4096).Complete();

        biomeIndexMap.SetPixelData(pixelsNative, 0);
        biomeIndexMap.Apply(false, false);

        pixelsNative.Dispose();
        biomeNative.Dispose();
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

    public void ApplyBiomeMaterialSettings()
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
        bool hasValidCliffArrays = cliffAlbedoArray != null && cliffNormalArray != null;

        if (hasValidCliffArrays)
        {
            sharedMaterial.SetTexture("_CliffAlbedoArray", cliffAlbedoArray);
            sharedMaterial.SetTexture("_CliffNormalArray", cliffNormalArray);
        }
        else
        {
            sharedMaterial.SetFloat("_CliffSliceCount", 0f);
        }
        
        if (biomeTintArray != null)
        {
            sharedMaterial.SetVectorArray("_BiomeTints", biomeTintArray);
            if (debugTransformChanges)
            {
                try
                {
                    Debug.Log($"[HexMapChunkManager] Pushed _BiomeTints count={biomeTintArray.Length} first={biomeTintArray[0]}");
                }
                catch { }
            }
        }

        if (biomeParamsArray != null)
        {
            sharedMaterial.SetVectorArray("_BiomeParams", biomeParamsArray);
            if (debugTransformChanges)
            {
                try { Debug.Log($"[HexMapChunkManager] Pushed _BiomeParams count={biomeParamsArray.Length} first={biomeParamsArray[0]}"); } catch { }
            }
        }

        if (biomeRoughnessOffsetsArray != null)
        {
            sharedMaterial.SetVectorArray("_BiomeRoughnessOffsets", biomeRoughnessOffsetsArray);
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
        sharedMaterial.SetFloat("_TerrainDebugMode", (float)terrainDebugMode);
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
        float cliffSlices = hasValidCliffArrays ? Mathf.Max(1, cliffAlbedoArray.depth) : 0f;
        sharedMaterial.SetFloat("_CliffSliceCount", cliffSlices);

        // Normal sampling and biome blending parameters
        sharedMaterial.SetFloat("_NormalStrength", normalStrength);
        sharedMaterial.SetFloat("_BiomeNormalStrength", biomeNormalStrength);
        sharedMaterial.SetFloat("_NormalSampleRadius", normalSampleRadius);
        sharedMaterial.SetFloat("_BiomeBlendRadius", biomeBlendRadius);
        sharedMaterial.SetFloat("_BiomeBlendSharpness", biomeBlendSharpness);

        // Triplanar parameters
        sharedMaterial.SetFloat("_TriTiling", triplanarTiling);
        sharedMaterial.SetFloat("_TriBlend", triplanarBlend);
        sharedMaterial.SetFloat("_UseTriplanar", useTriplanar ? 1f : 0f);
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

        if (debugBiomeDetails)
        {
            try
            {
                Debug.Log($"[HexMapChunkManager] TerrainDebugMode={terrainDebugMode} ({(float)terrainDebugMode})");

                // Dump first several tint entries to help diagnose why tinting appears as white
                int dumpN = Mathf.Min(16, biomeCount);
                var parts = new System.Text.StringBuilder();
                parts.Append($"[HexMapChunkManager] BiomeCount={biomeCount} TotalSlices={totalSlices} Tints[:{dumpN}]=");
                for (int i = 0; i < dumpN; i++)
                {
                    parts.Append(biomeTintArray[i].ToString());
                    if (i < dumpN - 1) parts.Append(",");
                }
                Debug.Log(parts.ToString());

                // Sample a few slice->biome mappings (first 32 slices or totalSlices)
                int sampleSlices = Mathf.Min(32, totalSlices);
                var sp = new System.Text.StringBuilder();
                sp.Append($"[HexMapChunkManager] SliceToBiomeMap samples[:{sampleSlices}]=");
                if (sliceToBiomeMap != null)
                {
                    for (int si = 0; si < sampleSlices; si++)
                    {
                        // Read pixel from texture (may be valid only in main thread during Apply)
                        try
                        {
                            var c = sliceToBiomeMap.GetPixel(si, 0);
                            sp.Append(((int)c.r).ToString());
                        }
                        catch { sp.Append("?"); }
                        if (si < sampleSlices - 1) sp.Append(",");
                    }
                }
                else sp.Append("<null>");
                Debug.Log(sp.ToString());
                // Also attempt to read back what was written to the material instance (one-shot)
                try
                {
                    if (sharedMaterial != null && sharedMaterial.HasProperty("_BiomeTints"))
                    {
                        // GetVectorArray exists in supported Unity versions where SetVectorArray is available
                        var matVecs = sharedMaterial.GetVectorArray("_BiomeTints");
                        if (matVecs != null)
                        {
                            var mb = new System.Text.StringBuilder();
                            mb.Append("[HexMapChunkManager] Material._BiomeTints[:" + Mathf.Min(16, matVecs.Length) + "]=");
                            for (int i = 0; i < Mathf.Min(16, matVecs.Length); i++)
                            {
                                mb.Append(matVecs[i].ToString());
                                if (i < Mathf.Min(16, matVecs.Length) - 1) mb.Append(",");
                            }
                            Debug.Log(mb.ToString());
                        }
                        else Debug.Log("[HexMapChunkManager] Material.GetVectorArray returned null");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[HexMapChunkManager] Failed to GetVectorArray from material: {ex.Message}");
                }
            }
            catch (System.Exception ex) { Debug.LogWarning($"[HexMapChunkManager] debugBiomeDetails error: {ex.Message}"); }
        }

        ApplyIceSurfaceSettingsToMaterial(sharedMaterial);
        ApplyIceSurfaceSettingsToMaterial(waterMaterial);
    }

    private void ApplyIceSurfaceSettingsToMaterial(Material material)
    {
        if (material == null)
            return;

        if (iceSurfaceDatabase != null)
        {
            if (material == sharedMaterial)
            {
                Debug.Log($"[HexMapChunkManager] Binding shared ice textures. Albedo={iceSurfaceDatabase.iceAlbedoArray != null}");
            }

            if (iceSurfaceDatabase.iceAlbedoArray != null) material.SetTexture("_IceAlbedoArray", iceSurfaceDatabase.iceAlbedoArray);
            if (iceSurfaceDatabase.iceNormalArray != null) material.SetTexture("_IceNormalArray", iceSurfaceDatabase.iceNormalArray);
            if (iceSurfaceDatabase.iceMaskArray != null) material.SetTexture("_IceMaskArray", iceSurfaceDatabase.iceMaskArray);
            if (iceSurfaceDatabase.iceHeightArray != null) material.SetTexture("_IceHeightArray", iceSurfaceDatabase.iceHeightArray);
            material.SetFloat("_IceSliceCount", iceSurfaceDatabase.iceAlbedoArray != null ? iceSurfaceDatabase.iceAlbedoArray.depth : 0f);
            material.SetColor("_LakeIceTint",    iceSurfaceDatabase.lakeIceTint);
            material.SetFloat("_LakeIceTiling",  iceSurfaceDatabase.lakeIceTiling);
            material.SetColor("_RiverIceTint",   iceSurfaceDatabase.riverIceTint);
            material.SetFloat("_RiverIceTiling", iceSurfaceDatabase.riverIceTiling);
            material.SetFloat("_IceNormalStrength", iceSurfaceDatabase.iceNormalStrength);
            material.SetFloat("_IceSmoothness",     iceSurfaceDatabase.iceSmoothness);
            material.SetFloat("_IceMetallic",       iceSurfaceDatabase.iceMetallic);
            material.SetFloat("_FreezeOpaqueThreshold", iceSurfaceDatabase.freezeOpaqueThreshold);
        }
        else
        {
            if (material == sharedMaterial)
                Debug.LogWarning("[HexMapChunkManager] iceSurfaceDatabase is NULL — no ice textures bound! Assign it in the Inspector.");

            material.SetFloat("_IceSliceCount", 0f);
        }

        float freezeProgress = 0f;
        if (planetGenerator != null && ClimateManager.Instance != null)
            freezeProgress = ClimateManager.Instance.GetFreezeProgressForPlanet(planetGenerator.planetIndex);
        material.SetFloat("_FreezeProgress", freezeProgress);
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
    
    // Follow-up: make TileSystem.GetTileSurfacePosition(tileIndex) consume this same rendered
    // elevation source (or a shared terrain height provider) so units and path preview markers
    // remain aligned with the CPU-displaced BakedHdrpLit terrain.
    public float SampleTerrainSurfaceYAtUV(Vector2 uv)
    {
        if (bakeResult.lut == null || bakeResult.lut.Length == 0)
            return flatY;

        int width = bakeResult.width > 0 ? bakeResult.width : textureWidth;
        int height = bakeResult.height > 0 ? bakeResult.height : textureHeight;

        int x = Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(uv.x, 1f) * width), 0, width - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(uv.y) * height), 0, height - 1);
        int lutIndex = y * width + x;

        if (lutIndex < 0 || lutIndex >= bakeResult.lut.Length)
            return flatY;

        int tileIndex = bakeResult.lut[lutIndex];
        if (tileIndex < 0)
            return flatY;

        return flatY + GetRenderedElevation(tileIndex) * displacementStrength;
    }

    private void CreateBakedLitMaterial()
    {
        if (bakedLitTerrainMaterial != null)
            DestroyImmediate(bakedLitTerrainMaterial);

        if (bakedLitTerrainMaterialTemplate != null)
        {
            bakedLitTerrainMaterial = new Material(bakedLitTerrainMaterialTemplate);
        }
        else
        {
            Shader litShader = Shader.Find("HDRP/Lit");
            if (litShader == null)
            {
                Debug.LogError("[HexMapChunkManager] Could not find HDRP/Lit shader. Assign bakedLitTerrainMaterialTemplate.");
                return;
            }

            bakedLitTerrainMaterial = new Material(litShader);
        }

        bakedLitTerrainMaterial.name = "BakedTerrain_HDRP_Lit";

        bool hasBaseColorMap = bakedLitTerrainMaterial.HasProperty("_BaseColorMap");
        bool hasMaskMap = bakedLitTerrainMaterial.HasProperty("_MaskMap");
        bool hasNormalMap = bakedLitTerrainMaterial.HasProperty("_NormalMap");
        bool hasBaseColor = bakedLitTerrainMaterial.HasProperty("_BaseColor");
        bool hasMetallic = bakedLitTerrainMaterial.HasProperty("_Metallic");
        bool hasSmoothness = bakedLitTerrainMaterial.HasProperty("_Smoothness");

        if (hasBaseColor)
            bakedLitTerrainMaterial.SetColor("_BaseColor", Color.white);

        if (hasMetallic)
            bakedLitTerrainMaterial.SetFloat("_Metallic", 0f);

        if (hasSmoothness)
            bakedLitTerrainMaterial.SetFloat("_Smoothness", 0.35f);

        Debug.Log($"[HexMapChunkManager] Created baked HDRP/Lit terrain material. shader={bakedLitTerrainMaterial.shader.name}");
        Debug.Log($"[HexMapChunkManager] BakedLit Has _BaseColorMap={hasBaseColorMap}");
        Debug.Log($"[HexMapChunkManager] BakedLit Has _MaskMap={hasMaskMap}");
        Debug.Log($"[HexMapChunkManager] BakedLit Has _NormalMap={hasNormalMap}");
        Debug.Log($"[HexMapChunkManager] BakedLit Has _BaseColor={hasBaseColor}");
        Debug.Log($"[HexMapChunkManager] BakedLit Has _Metallic={hasMetallic}");
        Debug.Log($"[HexMapChunkManager] BakedLit Has _Smoothness={hasSmoothness}");
    }

    [ContextMenu("Validate Baked Terrain Inputs")]
    private void ValidateBakedTerrainInputs()
    {
        int errors = 0;
        int warnings = 0;

        void Error(string message)
        {
            errors++;
            Debug.LogError($"[BakedTerrainValidation] {message}");
        }

        void Warning(string message)
        {
            warnings++;
            Debug.LogWarning($"[BakedTerrainValidation] {message}");
        }

        if (biomeVisualDatabase == null)
        {
            Error("BiomeVisualDatabase is not assigned.");
            Debug.LogError("[BakedTerrainValidation] Complete: errors=1 warnings=0");
            return;
        }

        if (biomeVisualDatabase.biomes == null || biomeVisualDatabase.biomes.Count == 0)
        {
            Error("BiomeVisualDatabase has no BiomeVisualData entries.");
            Debug.LogError($"[BakedTerrainValidation] Complete: errors={errors} warnings={warnings}");
            return;
        }

        var seen = new Dictionary<Biome, BiomeVisualData>();
        foreach (var visual in biomeVisualDatabase.biomes)
        {
            if (visual == null)
            {
                Error("BiomeVisualDatabase contains a null BiomeVisualData entry.");
                continue;
            }

            if (seen.TryGetValue(visual.biome, out var previous))
                Error($"Duplicate BiomeVisualData.biome entry for {visual.biome}: '{previous.name}' and '{visual.name}'.");
            else
                seen[visual.biome] = visual;

            SurfaceFamilyData family = visual.surfaceFamily;
            if (family == null)
            {
                Error($"BiomeVisualData '{visual.name}' ({visual.biome}) has a null surfaceFamily.");
                continue;
            }

            if (family.albedoArray == null)
                Error($"SurfaceFamily '{family.name}' for visual '{visual.name}' ({visual.biome}) has a null albedoArray.");
            else if (family.albedoArray.depth <= 0)
                Error($"SurfaceFamily '{family.name}' for visual '{visual.name}' ({visual.biome}) has albedoArray depth <= 0.");

            int variantCount = Mathf.Max(0, family.VariantCount);
            if (visual.forcedVariant >= variantCount && visual.forcedVariant >= 0)
                Error($"BiomeVisualData '{visual.name}' ({visual.biome}) forcedVariant={visual.forcedVariant} is outside variant count {variantCount} for family '{family.name}'.");

            if (!visual.name.ToLowerInvariant().Contains(visual.biome.ToString().ToLowerInvariant()))
                Warning($"BiomeVisualData asset name '{visual.name}' does not contain internal biome name '{visual.biome}'.");

            if (!string.IsNullOrWhiteSpace(family.familyName) && !family.name.ToLowerInvariant().Contains(family.familyName.ToLowerInvariant()) && !family.familyName.ToLowerInvariant().Contains(family.name.ToLowerInvariant()))
                Warning($"SurfaceFamily asset name '{family.name}' does not closely match familyName '{family.familyName}'.");
        }

        Biome[] normalEarthBiomes =
        {
            Biome.Ocean, Biome.Coast, Biome.Desert, Biome.Savannah, Biome.Plains,
            Biome.Temperate, Biome.Tropical, Biome.Glacier, Biome.Tundra,
            Biome.Swamp, Biome.Seas, Biome.River, Biome.Lake
        };

        foreach (Biome biome in normalEarthBiomes)
        {
            BiomeVisualData visual = biomeVisualDatabase.Get(biome);
            if (visual == null)
            {
                Error($"Normal Earth biome {biome} resolves to a null visual.");
                continue;
            }

            if (visual.surfaceFamily == null)
                Error($"Normal Earth biome {biome} resolves to visual '{visual.name}' with a null surfaceFamily.");
        }

        void ExpectFamilyContains(Biome biome, params string[] expectedTerms)
        {
            BiomeVisualData visual = biomeVisualDatabase.Get(biome);
            SurfaceFamilyData family = visual != null ? visual.surfaceFamily : null;
            if (visual == null || family == null)
                return;

            string combined = $"{family.name} {family.familyName}".ToLowerInvariant();
            bool matched = expectedTerms.Any(term => combined.Contains(term.ToLowerInvariant()));
            if (!matched)
                Warning($"Biome {biome} resolves to suspicious family asset='{family.name}' familyName='{family.familyName}', expected one of: {string.Join(", ", expectedTerms)}.");
        }

        ExpectFamilyContains(Biome.Desert, "Desert");
        ExpectFamilyContains(Biome.Savannah, "Savannah", "Plains");
        ExpectFamilyContains(Biome.Plains, "Savannah", "Plains");
        ExpectFamilyContains(Biome.Temperate, "Temperate");
        ExpectFamilyContains(Biome.Tropical, "Tropical");

        string result = $"[BakedTerrainValidation] Complete: errors={errors} warnings={warnings}";
        if (errors > 0) Debug.LogError(result);
        else if (warnings > 0) Debug.LogWarning(result);
        else Debug.Log(result);
    }

    private struct ResolvedTerrainSurfaceSample
    {
        public int tileIndex;
        public Biome tileBiome;
        public Biome renderedBiome;
        public Biome underwaterBiome;
        public TileWaterType waterType;
        public bool isMountain;
        public bool isRiver;
        public bool isLake;
        public bool isSolidFrozenWater;

        public BiomeVisualData visual;
        public SurfaceFamilyData surfaceFamily;
        public int biomeIndex;
        public int surfaceIndex;
        public int sliceIndex;
        public int forcedVariant;

        public Vector2 mapUV;
        public Vector2 surfaceUV;
        public Color sampledAlbedo;
    }

    private static readonly HashSet<Biome> BakedTerrainWatchedBiomes = new HashSet<Biome>
    {
        Biome.Desert,
        Biome.Savannah,
        Biome.Plains,
        Biome.Temperate,
        Biome.Coast,
        Biome.Glacier
    };

    private static readonly HashSet<Biome> BakedTerrainSliceExportBiomes = new HashSet<Biome>
    {
        Biome.Desert,
        Biome.Savannah,
        Biome.Plains,
        Biome.Coast,
        Biome.Glacier
    };

    private const int BakedTerrainReadableSliceCacheLimit = 8;
    private Dictionary<int, Color[]> bakedTerrainReadableSliceCache;
    private Dictionary<int, string> bakedTerrainFailedSliceSampleReasons;
    private bool lastResolvedTerrainAlbedoSampleSucceeded;
    private string lastResolvedTerrainAlbedoSampleFailureReason;

    private class BakedTerrainBiomeStats
    {
        public int pixelCount;
        public int fallbackCount;
        public double sumR;
        public double sumG;
        public double sumB;
        public double sumLum;
        public float minLum = float.PositiveInfinity;
        public float maxLum = float.NegativeInfinity;
        public readonly Dictionary<int, int> sliceCounts = new Dictionary<int, int>();

        public void Add(ResolvedTerrainSurfaceSample sample, bool fallback)
        {
            pixelCount++;
            if (fallback) fallbackCount++;

            Color c = sample.sampledAlbedo;
            sumR += c.r;
            sumG += c.g;
            sumB += c.b;
            float lum = c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
            sumLum += lum;
            if (lum < minLum) minLum = lum;
            if (lum > maxLum) maxLum = lum;

            if (!sliceCounts.ContainsKey(sample.sliceIndex))
                sliceCounts[sample.sliceIndex] = 0;
            sliceCounts[sample.sliceIndex]++;
        }
    }

    private bool TryResolveTerrainSurfaceSample(int tileIndex, float u, float v, out ResolvedTerrainSurfaceSample sample)
    {
        sample = new ResolvedTerrainSurfaceSample
        {
            tileIndex = tileIndex,
            biomeIndex = 0,
            surfaceIndex = -1,
            sliceIndex = 0,
            forcedVariant = -1,
            mapUV = new Vector2(u, v),
            surfaceUV = new Vector2(Mathf.Repeat(u * 8f, 1f), Mathf.Repeat(v * 8f, 1f)),
            sampledAlbedo = Color.magenta
        };

        lastResolvedTerrainAlbedoSampleSucceeded = false;
        lastResolvedTerrainAlbedoSampleFailureReason = null;

        if (tileIndex < 0 || planetGenerator == null || planetGenerator.data == null || !planetGenerator.data.TryGetValue(tileIndex, out var tile))
        {
            lastResolvedTerrainAlbedoSampleFailureReason = "missing tile data";
            return false;
        }

        BiomeVisualData visual = ResolveRenderedVisual(tile);
        int biomeIndex = ResolveRenderedBiomeIndex(tile);
        int sliceIndex = ResolveSurfaceSliceIndex(tile, tileIndex, biomeIndex);
        int surfaceIndex = ResolveSurfaceIndex(biomeIndex, tile.isMountain);
        SurfaceFamilyData surfaceFamily = visual != null ? visual.surfaceFamily : null;
        int forcedVariant = GetForcedVariant(biomeIndex, tile.isMountain);

        sample.tileBiome = tile.biome;
        sample.renderedBiome = visual != null ? visual.biome : tile.biome;
        sample.underwaterBiome = tile.underwaterBiome;
        sample.waterType = tile.waterType;
        sample.isMountain = tile.isMountain;
        sample.isRiver = tile.isRiver;
        sample.isLake = tile.isLake;
        sample.isSolidFrozenWater = IsSolidFrozenWater(tile);
        sample.visual = visual;
        sample.surfaceFamily = surfaceFamily;
        sample.biomeIndex = biomeIndex;
        sample.surfaceIndex = surfaceIndex;
        sample.sliceIndex = sliceIndex;
        sample.forcedVariant = forcedVariant;

        if (!bakedTerrainUseSimpleBiomeColors)
        {
            if (TrySampleRuntimeAlbedoSlice(sliceIndex, sample.surfaceUV, out var color, out var failureReason))
            {
                sample.sampledAlbedo = color;
                lastResolvedTerrainAlbedoSampleSucceeded = true;
            }
            else
            {
                sample.sampledAlbedo = BiomeColorHelper.GetMinimapColor(tile.biome);
                lastResolvedTerrainAlbedoSampleFailureReason = failureReason;
            }
        }
        else
        {
            sample.sampledAlbedo = BiomeColorHelper.GetMinimapColor(tile.biome);
            lastResolvedTerrainAlbedoSampleSucceeded = true;
        }

        return true;
    }

    private bool TrySampleRuntimeAlbedoSlice(int sliceIndex, Vector2 surfaceUV, out Color sampled, out string failureReason)
    {
        sampled = Color.magenta;
        failureReason = null;

        if (biomeAlbedoArray == null)
        {
            failureReason = "missing biomeAlbedoArray";
            return false;
        }

        if (sliceIndex < 0 || sliceIndex >= biomeAlbedoArray.depth)
        {
            failureReason = $"slice {sliceIndex} outside albedo depth {biomeAlbedoArray.depth}";
            return false;
        }

        if (bakedTerrainFailedSliceSampleReasons == null)
            bakedTerrainFailedSliceSampleReasons = new Dictionary<int, string>();
        if (bakedTerrainFailedSliceSampleReasons.TryGetValue(sliceIndex, out failureReason))
            return false;

        if (bakedTerrainReadableSliceCache == null)
            bakedTerrainReadableSliceCache = new Dictionary<int, Color[]>();
        if (!bakedTerrainReadableSliceCache.TryGetValue(sliceIndex, out var slicePixels))
        {
            try
            {
                slicePixels = biomeAlbedoArray.GetPixels(sliceIndex, 0);
            }
            catch (System.Exception ex)
            {
                failureReason = $"slice {sliceIndex} is not CPU-readable ({ex.GetType().Name}: {ex.Message})";
                bakedTerrainFailedSliceSampleReasons[sliceIndex] = failureReason;
                return false;
            }

            if (slicePixels == null || slicePixels.Length == 0)
            {
                failureReason = $"slice {sliceIndex} returned no pixels";
                bakedTerrainFailedSliceSampleReasons[sliceIndex] = failureReason;
                return false;
            }

            if (bakedTerrainReadableSliceCache.Count >= BakedTerrainReadableSliceCacheLimit)
                bakedTerrainReadableSliceCache.Clear();

            bakedTerrainReadableSliceCache[sliceIndex] = slicePixels;
        }

        int sourceWidth = Mathf.Max(1, biomeAlbedoArray.width);
        int sourceHeight = Mathf.Max(1, biomeAlbedoArray.height);
        float sampleX = Mathf.Repeat(surfaceUV.x, 1f) * (sourceWidth - 1);
        float sampleY = Mathf.Repeat(surfaceUV.y, 1f) * (sourceHeight - 1);
        int x0 = Mathf.Clamp(Mathf.FloorToInt(sampleX), 0, sourceWidth - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(sampleY), 0, sourceHeight - 1);
        int x1 = Mathf.Min(x0 + 1, sourceWidth - 1);
        int y1 = Mathf.Min(y0 + 1, sourceHeight - 1);
        float tx = sampleX - x0;
        float ty = sampleY - y0;

        int maxIndex = slicePixels.Length - 1;
        Color c00 = slicePixels[Mathf.Min(y0 * sourceWidth + x0, maxIndex)];
        Color c10 = slicePixels[Mathf.Min(y0 * sourceWidth + x1, maxIndex)];
        Color c01 = slicePixels[Mathf.Min(y1 * sourceWidth + x0, maxIndex)];
        Color c11 = slicePixels[Mathf.Min(y1 * sourceWidth + x1, maxIndex)];
        sampled = Color.Lerp(Color.Lerp(c00, c10, tx), Color.Lerp(c01, c11, tx), ty);
        sampled.a = 1f;
        return true;
    }

    private int ResolveSurfaceIndex(int biomeIndex, bool isMountain)
    {
        Vector4[] sourceMapArray = biomeSurfaceMapArray;
        if (isMountain && biomeMountainSurfaceMapArray != null && biomeIndex >= 0 && biomeIndex < biomeMountainSurfaceMapArray.Length)
        {
            var mountainMap = biomeMountainSurfaceMapArray[biomeIndex];
            if (Mathf.RoundToInt(mountainMap.y) > 0)
                sourceMapArray = biomeMountainSurfaceMapArray;
        }

        if (sourceMapArray != null && biomeIndex >= 0 && biomeIndex < sourceMapArray.Length)
            return Mathf.RoundToInt(sourceMapArray[biomeIndex].z);

        return -1;
    }

    private int GetForcedVariant(int biomeIndex, bool isMountain)
    {
        Vector4[] sourceMapArray = biomeSurfaceMapArray;
        if (isMountain && biomeMountainSurfaceMapArray != null && biomeIndex >= 0 && biomeIndex < biomeMountainSurfaceMapArray.Length)
        {
            var mountainMap = biomeMountainSurfaceMapArray[biomeIndex];
            if (Mathf.RoundToInt(mountainMap.y) > 0)
                sourceMapArray = biomeMountainSurfaceMapArray;
        }

        if (sourceMapArray != null && biomeIndex >= 0 && biomeIndex < sourceMapArray.Length)
            return Mathf.RoundToInt(sourceMapArray[biomeIndex].w);

        return -1;
    }

    private static string FormatColorRgb(Color c)
    {
        return $"({c.r:F3},{c.g:F3},{c.b:F3})";
    }

    private static string FormatVector2(Vector2 v)
    {
        return $"({v.x:F4},{v.y:F4})";
    }

    private static string FormatResolvedTerrainSurfaceSample(ResolvedTerrainSurfaceSample sample)
    {
        string visualName = sample.visual != null ? sample.visual.name : "<null>";
        string visualBiome = sample.visual != null ? sample.visual.biome.ToString() : "<null>";
        string familyAssetName = sample.surfaceFamily != null ? sample.surfaceFamily.name : "<null>";
        string familyName = sample.surfaceFamily != null ? sample.surfaceFamily.familyName : "<null>";
        return $"tileIndex={sample.tileIndex} tile.biome={sample.tileBiome} renderedBiome={sample.renderedBiome} underwaterBiome={sample.underwaterBiome} " +
               $"waterType={sample.waterType} isMountain={sample.isMountain} isRiver={sample.isRiver} isLake={sample.isLake} isSolidFrozenWater={sample.isSolidFrozenWater} " +
               $"visual.name={visualName} visual.biome={visualBiome} surfaceFamily.asset={familyAssetName} surfaceFamily.familyName={familyName} " +
               $"biomeIndex={sample.biomeIndex} surfaceIndex={sample.surfaceIndex} sliceIndex={sample.sliceIndex} forcedVariant={sample.forcedVariant} " +
               $"mapUV={FormatVector2(sample.mapUV)} surfaceUV={FormatVector2(sample.surfaceUV)} sampledAlbedo={FormatColorRgb(sample.sampledAlbedo)}";
    }

    private void ExportBakedTerrainBaseColorDebugPng()
    {
        if (bakedTerrainBaseColor == null) return;

        try
        {
            Directory.CreateDirectory(bakedTerrainDebugExportFolder);
            string path = Path.Combine(bakedTerrainDebugExportFolder, "BakedTerrain_BaseColor.png");
            File.WriteAllBytes(path, bakedTerrainBaseColor.EncodeToPNG());
            Debug.Log($"[HexMapChunkManager] Exported baked terrain BaseColor debug PNG: {path}");
#if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
#endif
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[HexMapChunkManager] Failed to export baked terrain BaseColor debug PNG: {ex.Message}");
        }
    }

    private void ExportRuntimeAlbedoSliceDebug(int sliceIndex, string label)
    {
        if (biomeAlbedoArray == null)
        {
            Debug.LogWarning($"[HexMapChunkManager] Cannot export albedo slice {sliceIndex} ({label}): biomeAlbedoArray is null.");
            return;
        }

        if (sliceIndex < 0 || sliceIndex >= biomeAlbedoArray.depth)
        {
            Debug.LogWarning($"[HexMapChunkManager] Cannot export albedo slice {sliceIndex} ({label}): outside depth {biomeAlbedoArray.depth}.");
            return;
        }

        try
        {
            Color[] slicePixels;
            if (bakedTerrainReadableSliceCache != null && bakedTerrainReadableSliceCache.TryGetValue(sliceIndex, out var cachedPixels))
                slicePixels = cachedPixels;
            else
                slicePixels = biomeAlbedoArray.GetPixels(sliceIndex, 0);

            var tex = new Texture2D(biomeAlbedoArray.width, biomeAlbedoArray.height, TextureFormat.RGBA32, false, false)
            {
                name = $"AlbedoSlice_{sliceIndex}_{label}"
            };
            tex.SetPixels(slicePixels);
            tex.Apply(false, false);

            Directory.CreateDirectory(bakedTerrainDebugExportFolder);
            string safeLabel = new string((label ?? "Slice").Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray());
            string path = Path.Combine(bakedTerrainDebugExportFolder, $"AlbedoSlice_{sliceIndex}_{safeLabel}.png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            DestroyImmediate(tex);
            Debug.Log($"[HexMapChunkManager] Exported runtime albedo slice debug PNG: {path}");
#if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
#endif
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[HexMapChunkManager] Failed to export albedo slice {sliceIndex} ({label}): {ex.Message}");
        }
    }

    private void BuildBakedHdrpLitTerrainMaps()
    {
        int width = Mathf.Max(1, bakedTerrainTextureWidth);
        int height = Mathf.Max(1, bakedTerrainTextureHeight);

        if (bakeResult.lut == null || bakeResult.lut.Length == 0)
        {
            Debug.LogError("[HexMapChunkManager] Cannot bake HDRP/Lit terrain maps: bakeResult.lut is missing.");
            return;
        }

        if (bakedTerrainBaseColor != null)
            DestroyImmediate(bakedTerrainBaseColor);

        bakedTerrainBaseColor = new Texture2D(width, height, TextureFormat.RGBA32, true, false);
        bakedTerrainBaseColor.name = "BakedTerrain_BaseColor";
        bakedTerrainBaseColor.wrapMode = TextureWrapMode.Repeat;
        bakedTerrainBaseColor.filterMode = FilterMode.Bilinear;
        bakedTerrainBaseColor.anisoLevel = 4;

        Color[] pixels = new Color[width * height];

        int lutWidth = bakeResult.width > 0 ? bakeResult.width : textureWidth;
        int lutHeight = bakeResult.height > 0 ? bakeResult.height : textureHeight;
        int[] lut = bakeResult.lut;
        bool useSimpleColors = bakedTerrainUseSimpleBiomeColors;

        int successfulAlbedoSamplePixelCount = 0;
        int fallbackPixelCount = 0;
        var warnedFallbackKeys = new HashSet<string>();
        var sampleLogs = new List<string>(10);
        var targetedLogs = new Dictionary<Biome, List<string>>();
        var exportedProblemBiomeSlices = new HashSet<Biome>();
        var statsByBiome = new Dictionary<Biome, BakedTerrainBiomeStats>();
        bakedTerrainReadableSliceCache = new Dictionary<int, Color[]>();
        bakedTerrainFailedSliceSampleReasons = new Dictionary<int, string>();

        for (int y = 0; y < height; y++)
        {
            float v = (height <= 1) ? 0f : (float)y / (height - 1);
            int lutY = Mathf.Clamp(Mathf.FloorToInt(v * lutHeight), 0, lutHeight - 1);

            for (int x = 0; x < width; x++)
            {
                float u = (width <= 1) ? 0f : (float)x / (width - 1);
                int lutX = Mathf.Clamp(Mathf.FloorToInt(u * lutWidth), 0, lutWidth - 1);

                int lutIndex = lutY * lutWidth + lutX;
                int tileIndex = (lutIndex >= 0 && lutIndex < lut.Length) ? lut[lutIndex] : -1;
                Color color = Color.magenta;

                if (TryResolveTerrainSurfaceSample(tileIndex, u, v, out var sample))
                {
                    bool sampleFallback = !useSimpleColors && !lastResolvedTerrainAlbedoSampleSucceeded;
                    color = sample.sampledAlbedo;

                    if (useSimpleColors)
                    {
                        color = BiomeColorHelper.GetMinimapColor(sample.tileBiome);
                    }
                    else if (lastResolvedTerrainAlbedoSampleSucceeded)
                    {
                        successfulAlbedoSamplePixelCount++;
                    }
                    else
                    {
                        fallbackPixelCount++;
                        string fallbackKey = $"surface={sample.surfaceIndex}|slice={sample.sliceIndex}|reason={lastResolvedTerrainAlbedoSampleFailureReason}";
                        if (warnedFallbackKeys.Add(fallbackKey))
                        {
                            Debug.LogWarning($"[HexMapChunkManager] Baked HDRP/Lit albedo sampling failed for surface={sample.surfaceIndex}, slice={sample.sliceIndex}, biome={sample.tileBiome}; using minimap fallback. Reason: {lastResolvedTerrainAlbedoSampleFailureReason}");
                        }
                    }

                    if (sampleLogs.Count < 10)
                        sampleLogs.Add($"pixel=({x},{y}) {FormatResolvedTerrainSurfaceSample(sample)}");

                    if (!statsByBiome.TryGetValue(sample.renderedBiome, out var stats))
                    {
                        stats = new BakedTerrainBiomeStats();
                        statsByBiome[sample.renderedBiome] = stats;
                    }
                    stats.Add(sample, sampleFallback);

                    if (debugBakedTerrainResolution)
                    {
                        bool shouldLogBiome = debugBakedTerrainOnlyProblemBiomes
                            ? BakedTerrainWatchedBiomes.Contains(sample.renderedBiome) || BakedTerrainWatchedBiomes.Contains(sample.tileBiome)
                            : true;

                        if (shouldLogBiome)
                        {
                            Biome key = BakedTerrainWatchedBiomes.Contains(sample.renderedBiome) ? sample.renderedBiome : sample.tileBiome;
                            if (!targetedLogs.TryGetValue(key, out var logs))
                            {
                                logs = new List<string>();
                                targetedLogs[key] = logs;
                            }

                            if (logs.Count < Mathf.Max(1, debugBakedTerrainSamplesPerBiome))
                                logs.Add($"pixel=({x},{y}) {FormatResolvedTerrainSurfaceSample(sample)}");
                        }
                    }

                    if (exportProblemBiomeSlices && !useSimpleColors && BakedTerrainSliceExportBiomes.Contains(sample.renderedBiome) && exportedProblemBiomeSlices.Add(sample.renderedBiome))
                        ExportRuntimeAlbedoSliceDebug(sample.sliceIndex, sample.renderedBiome.ToString());
                }

                pixels[y * width + x] = color;
            }
        }

        bakedTerrainBaseColor.SetPixels(pixels);
        bool keepReadableForDebugExport = keepBakedTerrainTexturesReadable || exportBakedTerrainDebugPng;
        bakedTerrainBaseColor.Apply(true, !keepReadableForDebugExport);

        if (exportBakedTerrainDebugPng)
            ExportBakedTerrainBaseColorDebugPng();

        if (bakedLitTerrainMaterial != null)
        {
            if (bakedLitTerrainMaterial.HasProperty("_BaseColorMap"))
                bakedLitTerrainMaterial.SetTexture("_BaseColorMap", bakedTerrainBaseColor);
            else
                Debug.LogWarning("[HexMapChunkManager] Baked HDRP/Lit material has no _BaseColorMap property.");
        }

        string bakeMode = useSimpleColors ? "simple biome colors" : "surface albedo textures";
        Debug.Log($"[HexMapChunkManager] Built baked HDRP/Lit BaseColor map {width}x{height}, mode={bakeMode}, bakedTerrainUseSimpleBiomeColors={useSimpleColors}, readable={keepReadableForDebugExport}");
        Debug.Log($"[HexMapChunkManager] Baked HDRP/Lit BaseColor bake sampledRealAlbedoPixels={successfulAlbedoSamplePixelCount}, fallbackBiomeColorPixels={fallbackPixelCount}.");
        if (sampleLogs.Count > 0)
            Debug.Log($"[HexMapChunkManager] First resolved baked HDRP/Lit terrain samples:\n  {string.Join("\n  ", sampleLogs)}");

        if (targetedLogs.Count > 0)
        {
            foreach (var kvp in targetedLogs.OrderBy(k => k.Key.ToString()))
                Debug.Log($"[BakedTerrainResolution] {kvp.Key} examples (max {Mathf.Max(1, debugBakedTerrainSamplesPerBiome)}):\n  {string.Join("\n  ", kvp.Value)}");
        }

        foreach (var kvp in statsByBiome.OrderBy(k => k.Key.ToString()))
        {
            var stats = kvp.Value;
            if (stats.pixelCount <= 0) continue;

            string slices = string.Join(",", stats.sliceCounts
                .OrderByDescending(s => s.Value)
                .ThenBy(s => s.Key)
                .Take(8)
                .Select(s => $"{s.Key}:{s.Value}"));

            Debug.Log($"[BakedTerrainStats] {kvp.Key} pixels={stats.pixelCount} avgRGB=({stats.sumR / stats.pixelCount:F3},{stats.sumG / stats.pixelCount:F3},{stats.sumB / stats.pixelCount:F3}) " +
                      $"avgLum={stats.sumLum / stats.pixelCount:F3} minLum={stats.minLum:F3} maxLum={stats.maxLum:F3} slices={slices} fallbackCount={stats.fallbackCount}");
        }
    }

    private void BuildNeutralBakedMaskMap()
    {
        // Keep this neutral mask tiny for the first HDRP/Lit proof path. Real per-pixel mask baking
        // should be added only after the BaseColor-only path is validated.
        int width = 1;
        int height = 1;

        if (bakedTerrainMaskMap != null)
            DestroyImmediate(bakedTerrainMaskMap);

        bakedTerrainMaskMap = new Texture2D(width, height, TextureFormat.RGBA32, true, true);
        bakedTerrainMaskMap.name = "BakedTerrain_NeutralMask";
        bakedTerrainMaskMap.wrapMode = TextureWrapMode.Repeat;
        bakedTerrainMaskMap.filterMode = FilterMode.Bilinear;

        Color neutral = new Color(0f, 1f, 0f, 0.35f);
        Color[] pixels = new Color[width * height];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = neutral;

        bakedTerrainMaskMap.SetPixels(pixels);
        bakedTerrainMaskMap.Apply(true, !keepBakedTerrainTexturesReadable);

        if (bakedLitTerrainMaterial != null)
        {
            if (bakedLitTerrainMaterial.HasProperty("_MaskMap"))
                bakedLitTerrainMaterial.SetTexture("_MaskMap", bakedTerrainMaskMap);
            else
                Debug.LogWarning("[HexMapChunkManager] Baked HDRP/Lit material has no _MaskMap property.");
        }

        Debug.Log($"[HexMapChunkManager] Built neutral baked HDRP/Lit MaskMap {width}x{height}, readable={keepBakedTerrainTexturesReadable}");
    }

    private void CreateSharedMaterial()
    {
        Debug.Log($"[HexMapChunkManager] TerrainRenderPath={terrainRenderPath}");

        if (terrainRenderPath == TerrainRenderPath.BakedHdrpLit)
        {
            CreateBakedLitMaterial();
            BuildBakedHdrpLitTerrainMaps();
            BuildNeutralBakedMaskMap();
            sharedMaterial = bakedLitTerrainMaterial;
            Debug.Log($"[HexMapChunkManager] Shared material shader={sharedMaterial?.shader?.name}");
            Debug.Log($"[HexMapChunkManager] BakedBaseColor={bakedTerrainBaseColor != null} size={bakedTerrainTextureWidth}x{bakedTerrainTextureHeight}");
            if (bakedLitTerrainMaterial != null)
            {
                Debug.Log($"[HexMapChunkManager] BakedLit Has _BaseColorMap={bakedLitTerrainMaterial.HasProperty("_BaseColorMap")}");
                Debug.Log($"[HexMapChunkManager] BakedLit Has _MaskMap={bakedLitTerrainMaterial.HasProperty("_MaskMap")}");
            }
            return;
        }

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
        ApplyBiomeMaterialSettings();
        
        // Create and apply LUT texture for tile highlighting
        CreateAndApplyLUTTexture();

        Debug.Log($"[HexMapChunkManager] Shared material shader={sharedMaterial?.shader?.name}");
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
        
        // Baked HDRP/Lit visible chunks use SampleTerrainSurfaceYAtUV; use the same source here so picking matches.
        bool useBakedHeightSampler = terrainRenderPath == TerrainRenderPath.BakedHdrpLit;

        // Check if the heightmap is available for CPU-side displacement in the custom shader path.
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
                
                // Sample the same surface height as the visible terrain. The collider object already sits at flatY,
                // so convert the world-space sampled terrain Y back into collider-local Y.
                float posY = 0f;
                if (useBakedHeightSampler)
                {
                    posY = SampleTerrainSurfaceYAtUV(new Vector2(u, v)) - flatY;
                }
                else if (hasHeightmap)
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
    /// Create flat picking colliders at the water-surface and orbit heights.
    /// These use the same dense subdivision + UV mapping as the terrain collider
    /// so that hit.textureCoord→LUT lookup is accurate, but at the correct Y
    /// for each layer — eliminating parallax at oblique camera angles.
    /// </summary>
    private void CreateLayerPickingColliders()
    {
        float halfW = mapWidth * 0.5f;
        float halfH = mapHeight * 0.5f;
        int terrainLayer = LayerMask.NameToLayer("Terrain");
        int unityLayer = terrainLayer >= 0 ? terrainLayer : 0;

        // --- Water surface picking collider ---
        if (waterPickingCollider != null)
            DestroyImmediate(waterPickingCollider.gameObject);

        {
            Mesh mesh = BuildFlatSubdividedMesh("WaterPickingMesh", halfW, halfH);
            var obj = new GameObject("WaterPickingCollider");
            obj.transform.SetParent(transform, false);
            float waterY = GetOceanWaterSurfaceY();
            obj.transform.localPosition = new Vector3(0f, waterY, 0f);
            obj.transform.localRotation = Quaternion.identity;
            obj.layer = unityLayer;

            var mf = obj.AddComponent<MeshFilter>();
            mf.mesh = mesh;
            var mr = obj.AddComponent<MeshRenderer>();
            mr.enabled = false;
            var mc = obj.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            waterPickingCollider = mc;
        }

        // --- Orbit picking collider ---
        if (orbitPickingCollider != null)
            DestroyImmediate(orbitPickingCollider.gameObject);

        if (planetGenerator != null && planetGenerator.orbitRoot != null)
        {
            // Orbit overlay mesh uses normalised verts (-0.5..0.5) because orbitRoot
            // has localScale = (mapWidth, 1, mapHeight).  The picking collider must
            // match, so we use the same normalised half-extents.
            Mesh mesh = BuildFlatSubdividedMesh("OrbitPickingMesh", 0.5f, 0.5f);
            var obj = new GameObject("OrbitPickingCollider");
            obj.transform.SetParent(planetGenerator.orbitRoot.transform, false);
            float localY = planetGenerator.orbitHeight + flatY - planetGenerator.orbitYOffset;
            obj.transform.localPosition = new Vector3(0f, localY, 0f);
            obj.transform.localRotation = Quaternion.identity;
            obj.transform.localScale = Vector3.one;
            obj.layer = unityLayer;

            var mf = obj.AddComponent<MeshFilter>();
            mf.mesh = mesh;
            var mr = obj.AddComponent<MeshRenderer>();
            mr.enabled = false;
            var mc = obj.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            orbitPickingCollider = mc;
        }
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
    
    /// <summary>
    /// Create a flat transparent mesh at orbit height for tile highlighting in orbit view.
    /// Parents to PlanetGenerator.orbitRoot so it auto-hides with the orbit layer.
    /// Uses OrbitHighlightOverlay shader which is fully transparent except for the highlighted tile.
    /// </summary>
    private void CreateOrbitOverlayMesh()
    {
        if (planetGenerator == null) return;
        var orbitRoot = planetGenerator.orbitRoot;
        if (orbitRoot == null) return;

        // Clean up previous overlay
        if (orbitOverlayObj != null)
            DestroyImmediate(orbitOverlayObj);

        // Resolve shader
        if (orbitOverlayShader == null)
            orbitOverlayShader = Shader.Find("Custom/OrbitHighlightOverlay");
        if (orbitOverlayShader == null)
        {
            Debug.LogWarning("[HexMapChunkManager] OrbitHighlightOverlay shader not found; orbit highlight disabled.");
            return;
        }

        // Build a densely subdivided flat mesh with correct per-vertex UVs —
        // identical grid to the terrain mesh so LUT sampling is pixel-accurate.
        // Uses normalised coordinates (-0.5 to 0.5) because orbitRoot.localScale
        // is set to (mapWidth, 1, mapHeight) by LayerManager.
        Mesh mesh = BuildFlatSubdividedMesh("OrbitHighlightOverlay", 0.5f, 0.5f);

        orbitOverlayObj = new GameObject("OrbitHighlightOverlay");
        orbitOverlayObj.transform.SetParent(orbitRoot.transform, false);
        // Position at orbitHeight relative to orbit root (orbit root is at orbitYOffset)
        float localY = planetGenerator.orbitHeight + flatY - planetGenerator.orbitYOffset;
        orbitOverlayObj.transform.localPosition = new Vector3(0f, localY, 0f);
        orbitOverlayObj.transform.localRotation = Quaternion.identity;
        orbitOverlayObj.transform.localScale = Vector3.one;

        var mf = orbitOverlayObj.AddComponent<MeshFilter>();
        mf.mesh = mesh;

        orbitOverlayMaterial = new Material(orbitOverlayShader);
        orbitOverlayMaterial.name = "OrbitHighlightOverlay_Mat";
        // Assign the same LUT texture used by the terrain shader
        if (lutTexture != null)
            orbitOverlayMaterial.SetTexture("_LUT", lutTexture);
        orbitOverlayMaterial.SetFloat("_HighlightTileIndex", -1f);

        var mr = orbitOverlayObj.AddComponent<MeshRenderer>();
        mr.sharedMaterial = orbitOverlayMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }
    
    /// <summary>
    /// Create a flat transparent mesh at the water surface level for tile highlighting
    /// when hovering over water tiles in surface view.
    /// Always active under the HexMapChunkManager transform (visible whenever terrain is).
    /// The material highlight index defaults to -1 (fully transparent / no highlight).
    /// </summary>
    private void CreateWaterSurfaceOverlayMesh()
    {
        // Clean up previous overlay
        if (waterSurfaceOverlayObj != null)
            DestroyImmediate(waterSurfaceOverlayObj);

        // Reuse the same shader as the orbit overlay
        var shader = orbitOverlayShader;
        if (shader == null)
            shader = Shader.Find("Custom/OrbitHighlightOverlay");
        if (shader == null)
        {
            Debug.LogWarning("[HexMapChunkManager] OrbitHighlightOverlay shader not found; water surface highlight disabled.");
            return;
        }

        float halfW = mapWidth * 0.5f;
        float halfH = mapHeight * 0.5f;

        // Build a densely subdivided flat mesh — same grid as the terrain for
        // pixel-accurate LUT sampling.  Uses map-space half-extents directly
        // because this overlay's parent transform has scale = 1.
        Mesh mesh = BuildFlatSubdividedMesh("WaterSurfaceHighlightOverlay", halfW, halfH);

        waterSurfaceOverlayObj = new GameObject("WaterSurfaceHighlightOverlay");
        waterSurfaceOverlayObj.transform.SetParent(transform, false);
        // Position at the ocean water surface Y (slightly above to avoid z-fighting with water plane)
        float waterY = GetOceanWaterSurfaceY(0.02f);
        waterSurfaceOverlayObj.transform.localPosition = new Vector3(0f, waterY, 0f);
        waterSurfaceOverlayObj.transform.localRotation = Quaternion.identity;
        waterSurfaceOverlayObj.transform.localScale = Vector3.one;

        var mf = waterSurfaceOverlayObj.AddComponent<MeshFilter>();
        mf.mesh = mesh;

        waterSurfaceOverlayMaterial = new Material(shader);
        waterSurfaceOverlayMaterial.name = "WaterSurfaceHighlightOverlay_Mat";
        if (lutTexture != null)
            waterSurfaceOverlayMaterial.SetTexture("_LUT", lutTexture);
        waterSurfaceOverlayMaterial.SetFloat("_HighlightTileIndex", -1f);

        var mr = waterSurfaceOverlayObj.AddComponent<MeshRenderer>();
        mr.sharedMaterial = waterSurfaceOverlayMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    /// <summary>
    /// Build a flat, densely subdivided mesh with per-vertex UVs (0..1) matching
    /// the terrain grid density.  Vertices span (-halfW..halfW, 0, -halfH..halfH).
    /// This is the same subdivision the terrain/picking mesh uses, so the GPU
    /// interpolation of UVs within each small triangle is accurate at any camera angle.
    /// </summary>
    private Mesh BuildFlatSubdividedMesh(string meshName, float halfW, float halfH)
    {
        int subX = Mathf.Min(chunksX * meshSubdivisionsPerChunk, 512);
        int subZ = Mathf.Min(chunksZ * meshSubdivisionsPerChunk, 256);
        int vX = subX + 1;
        int vZ = subZ + 1;
        int vertCount = vX * vZ;

        var vertices = new Vector3[vertCount];
        var uvs = new Vector2[vertCount];

        for (int z = 0; z < vZ; z++)
        {
            for (int x = 0; x < vX; x++)
            {
                int idx = z * vX + x;
                float u = (float)x / subX;
                float v = (float)z / subZ;

                vertices[idx] = new Vector3(
                    -halfW + u * (halfW * 2f),
                    0f,
                    -halfH + v * (halfH * 2f));
                uvs[idx] = new Vector2(u, v);
            }
        }

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

        var mesh = new Mesh();
        mesh.name = meshName;
        if (vertCount > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
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
            if (ShouldHideLiquidWater(td)) continue;
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
        var freezeData = new List<Vector4>(waterTiles.Count * 12);
        var normals = new List<Vector3>(waterTiles.Count * 12);
        var triangles = new List<int>(waterTiles.Count * 24);

        // Chunk transform places the mesh; vertices are in chunk-local space.
        Vector3 chunkWorldPos = chunk.transform.position;

        // Cache per-tile top vertex base index + water height so we can build walls in a second pass.
        var baseVertByTile = new Dictionary<int, int>(waterTiles.Count);
        var waterYByTile = new Dictionary<int, float>(waterTiles.Count);

        int AddVert(Vector3 v, Vector2 uv, Color c, Vector4 freeze)
        {
            int idx = vertices.Count;
            vertices.Add(v);
            uvs.Add(uv);
            colors.Add(c);
            freezeData.Add(freeze);
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

            float waterWorldY = GetTileWaterSurfaceY(td);

            // Convert to chunk-local
            Vector3 localCenter = new Vector3(
                tileCenter.x - chunkWorldPos.x,
                waterWorldY - chunkWorldPos.y,
                tileCenter.z - chunkWorldPos.z
            );

            // Encode flow into vertex color
            // Encode flow into vertex color. Still water uses a tint hint so lava lakes
            // and demonic oceans can render differently without a separate water system.
            Color flowColor;
            if (td.waterType == TileWaterType.River)
            {
                flowColor = new Color(
                    td.riverFlowDirXZ.x * 0.5f + 0.5f,
                    td.riverFlowDirXZ.y * 0.5f + 0.5f,
                    0f,
                    1f
                );
            }
            else if (td.biome == Biome.Lava)
            {
                flowColor = new Color(0.92f, 0.24f, 0.04f, 2f / 3f);
            }
            else if (td.waterType == TileWaterType.Ocean && planetGenerator != null && planetGenerator.mapType == MapType.Demonic)
            {
                flowColor = new Color(0.38f, 0.43f, 0.47f, 1f / 3f);
            }
            else
            {
                flowColor = td.waterType == TileWaterType.Ocean
                    ? new Color(0.10f, 0.40f, 0.72f, 1f / 3f)
                    : new Color(0.20f, 0.56f, 0.86f, 2f / 3f);
            }

            int baseVert = vertices.Count;
            baseVertByTile[tileIdx] = baseVert;
            waterYByTile[tileIdx] = waterWorldY;
            Vector4 tileFreezeData = GetWaterFreezeVertexData(td, tileIdx);

            // Center vertex
            AddVert(localCenter, new Vector2(0.5f, 0.5f), flowColor, tileFreezeData);

            // 6 corner vertices
            for (int k = 0; k < 6; k++)
            {
                AddVert(
                    localCenter + new Vector3(s * HexCornerCos[k], 0f, s * HexCornerSin[k]),
                    new Vector2(HexCornerCos[k] * 0.5f + 0.5f, HexCornerSin[k] * 0.5f + 0.5f),
                    flowColor,
                    tileFreezeData
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
                            nbrWaterY = GetTileWaterSurfaceY(nbrTd);
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
                    Vector4 freeze = freezeData[topA];
                    int botA = AddVert(vBotA, new Vector2(0f, 0f), c, freeze);
                    int botB = AddVert(vBotB, new Vector2(1f, 0f), c, freeze);

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
        waterMesh.SetUVs(1, freezeData);
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
        void MarkSeed(bool[] seed, int[] owner, float u, float v, int tileIndex, float isoRadius)
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
                radius = Mathf.CeilToInt(Mathf.Max(0.001f, isoRadius) / minCell);

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

            if (ShouldHideLiquidWater(td))
            {
                continue;
            }

            if (td.waterType == TileWaterType.River)
            {
                MarkSeed(seedRiver, ownerRiver, u0, v0, ti, isoRiver);

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
                            MarkSeed(seedRiver, ownerRiver, uu, vv, ti, isoRiver);
                        }
                    }
                }
            }
            else if (continuousWaterIncludesLakes && td.waterType == TileWaterType.Lake)
            {
                // Lakes: seed center + corners so the lake area fills the whole hex reliably.
                MarkSeed(seedLake, ownerLake, u0, v0, ti, isoLake);
                for (int k = 0; k < 6; k++)
                {
                    Vector3 p = c + new Vector3(hexSize * HexCornerCos[k], 0f, hexSize * HexCornerSin[k]);
                    float uu = (p.x - minX) / worldW;
                    float vv = (p.z - minZ) / worldH;
                    MarkSeed(seedLake, ownerLake, uu, vv, ti, isoLake);
                }
            }
            else if (continuousWaterIncludesOcean && td.waterType == TileWaterType.Ocean)
            {
                // Ocean: seed center + corners so the SDF fully covers each ocean hex (prevents holes between tile centers).
                MarkSeed(seedOcean, ownerOcean, u0, v0, ti, isoOcean);
                for (int k = 0; k < 6; k++)
                {
                    Vector3 p = c + new Vector3(hexSize * HexCornerCos[k], 0f, hexSize * HexCornerSin[k]);
                    float uu = (p.x - minX) / worldW;
                    float vv = (p.z - minZ) / worldH;
                    MarkSeed(seedOcean, ownerOcean, uu, vv, ti, isoOcean);
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

        int lutW = bakeResult.width > 0 ? bakeResult.width : textureWidth;
        int lutH = bakeResult.height > 0 ? bakeResult.height : textureHeight;

        // Scalar field: f = min(distRiver - isoRiver, distLake - isoLake, distOcean - isoOcean). Inside when f <= 0.
        float FAt(int ix, int iy)
        {
            int idx = iy * wPts + ix;
            float f = distRiver[idx] - isoRiver;
            if (distLake != null) f = Mathf.Min(f, distLake[idx] - isoLake);
            if (distOcean != null) f = Mathf.Min(f, distOcean[idx] - isoOcean);

            // Hard-clip the continuous water mesh to tiles that are actually marked as water.
            // Check a 2x2 LUT neighborhood to tolerate rounding mismatches between
            // the SDF grid and the LUT pixel grid at tile boundaries.
            float u = (float)ix / wCells;
            float v = (float)iy / hCells;
            u = Mathf.Repeat(u, 1f);
            v = Mathf.Clamp01(v);
            int px0 = Mathf.Clamp(Mathf.FloorToInt(u * lutW), 0, lutW - 1);
            int py0 = Mathf.Clamp(Mathf.FloorToInt(v * lutH), 0, lutH - 1);
            int px1 = Mathf.Min(px0 + 1, lutW - 1);
            int py1 = Mathf.Min(py0 + 1, lutH - 1);

            bool anyWater = false;
            for (int py = py0; py <= py1 && !anyWater; py++)
            {
                for (int px = px0; px <= px1 && !anyWater; px++)
                {
                    int pixelIndex = py * lutW + px;
                    if (pixelIndex >= 0 && pixelIndex < bakeResult.lut.Length)
                    {
                        int tileIndex = bakeResult.lut[pixelIndex];
                        if (tileIndex >= 0 && planetGenerator.data.TryGetValue(tileIndex, out var tileAtUv) && tileAtUv.waterType != TileWaterType.None)
                            anyWater = true;
                    }
                }
            }

            if (!anyWater)
                return Mathf.Max(f, 0.001f);

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
        var freezeVerts = new System.Collections.Generic.List<Vector4>(65536);
        var norms = new System.Collections.Generic.List<Vector3>(65536);
        var tris = new System.Collections.Generic.List<int>(131072);

        int[] cornerVert = ArrayPoolUtils.RentInt(wPts * hPts);
        for (int i = 0; i < wPts * hPts; i++) cornerVert[i] = -1;

        int[] horizEdge = ArrayPoolUtils.RentInt(wCells * (hCells + 1));        // edge between (x,y) and (x+1,y)
        int[] vertEdge = ArrayPoolUtils.RentInt((wCells + 1) * hCells);         // edge between (x,y) and (x,y+1)
        for (int i = 0; i < wCells * (hCells + 1); i++) horizEdge[i] = -1;
        for (int i = 0; i < (wCells + 1) * hCells; i++) vertEdge[i] = -1;

        

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

            u = Mathf.Repeat(u, 1f);
            v = Mathf.Clamp01(v);
            int ix = Mathf.Clamp(Mathf.RoundToInt(u * wCells), 0, wCells);
            int iy = Mathf.Clamp(Mathf.RoundToInt(v * hCells), 0, hCells);
            int idx = iy * wPts + ix;

            if (wType == 1) // lake
            {
                int lakeTileIndex = ownerLake != null ? ownerLake[idx] : -1;
                if (lakeTileIndex >= 0 && planetGenerator.data.TryGetValue(lakeTileIndex, out var lakeTile) && lakeTile.biome == Biome.Lava)
                    return new Color(0.92f, 0.24f, 0.04f, 2f / 3f);

                return new Color(0.20f, 0.56f, 0.86f, 2f / 3f);
            }

            if (wType == 2) // ocean — encode as ocean alpha (1/3)
            {
                if (planetGenerator != null && planetGenerator.mapType == MapType.Demonic)
                    return new Color(0.38f, 0.43f, 0.47f, 1f / 3f);

                return new Color(0.10f, 0.40f, 0.72f, 1f / 3f);
            }

            // River: pick flow direction from nearest propagated river seed tile.
            int tIndex = (ownerRiver != null) ? ownerRiver[idx] : -1;
            if (tIndex >= 0 && planetGenerator.data.TryGetValue(tIndex, out var td) && td.waterType == TileWaterType.River)
                return new Color(td.riverFlowDirXZ.x * 0.5f + 0.5f, td.riverFlowDirXZ.y * 0.5f + 0.5f, 0f, 1f);

            return new Color(0.5f, 0.5f, 0f, 1f);
        }

        Vector4 SampleWaterFreezeData(float u, float v)
        {
            int wType = ClassifyWaterAt(u, v);
            if (wType == 2)
                return Vector4.zero;

            u = Mathf.Repeat(u, 1f);
            v = Mathf.Clamp01(v);
            int ix = Mathf.Clamp(Mathf.RoundToInt(u * wCells), 0, wCells);
            int iy = Mathf.Clamp(Mathf.RoundToInt(v * hCells), 0, hCells);
            int idx = iy * wPts + ix;

            int tileIndex = wType == 1 && ownerLake != null
                ? ownerLake[idx]
                : ownerRiver != null ? ownerRiver[idx] : -1;

            return tileIndex >= 0 && planetGenerator.data.TryGetValue(tileIndex, out var tile)
                ? GetWaterFreezeVertexData(tile, tileIndex)
                : Vector4.zero;
        }

        // Helper: compute water Y for a specific SDF grid point from its owner tile's waterElevation.
        float OwnerWaterYAt(int gx, int gy, int wt)
        {
            int ci = gy * wPts + gx;
            int tIdx = (wt == 1 && ownerLake != null) ? ownerLake[ci]
                     : (ownerRiver != null) ? ownerRiver[ci] : -1;
            if (tIdx >= 0 && planetGenerator.data.TryGetValue(tIdx, out var t)
                && (t.waterType == TileWaterType.River || t.waterType == TileWaterType.Lake))
                return flatY + t.waterElevation * displacementStrength + waterYOffset + riverSurfaceLift;
            float eu = Mathf.Repeat((float)gx / wCells, 1f);
            float ev = Mathf.Clamp01((float)gy / hCells);
            float el = heightmapTexture != null ? heightmapTexture.GetPixelBilinear(eu, ev).r : 0f;
            return flatY + el * displacementStrength + waterYOffset + riverSurfaceLift;
        }

        float SampleWaterY(float u, float v)
        {
            u = Mathf.Repeat(u, 1f);
            v = Mathf.Clamp01(v);
            int wType = ClassifyWaterAt(u, v);

            if (wType == 2) // ocean — flat at sea level
                return GetOceanWaterSurfaceY(riverSurfaceLift);

            // Bilinear blend of water elevation from 4 nearest SDF grid corners.
            // Smooths the Y staircase that occurs at tile-ownership boundaries
            // where adjacent owner tiles have different waterElevation values.
            float fx = u * wCells;
            float fy = v * hCells;
            int x0 = Mathf.Clamp((int)fx, 0, wCells - 1);
            int y0 = Mathf.Clamp((int)fy, 0, hCells - 1);
            int x1 = Mathf.Min(x0 + 1, wCells);
            int y1 = Mathf.Min(y0 + 1, hCells);
            float tx = fx - x0;
            float ty = fy - y0;

            float y00 = OwnerWaterYAt(x0, y0, wType);
            float y10 = OwnerWaterYAt(x1, y0, wType);
            float y01 = OwnerWaterYAt(x0, y1, wType);
            float y11 = OwnerWaterYAt(x1, y1, wType);

            float blendedY = Mathf.Lerp(
                Mathf.Lerp(y00, y10, tx),
                Mathf.Lerp(y01, y11, tx),
                ty);

            return blendedY;
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
            freezeVerts.Add(SampleWaterFreezeData(u, v));
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
            freezeVerts.Add(SampleWaterFreezeData(u, v));
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
            freezeVerts.Add(SampleWaterFreezeData(u, v));
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
            freezeVerts.Add(SampleWaterFreezeData(u, v));
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
            var f2 = new System.Collections.Generic.List<Vector4>(nTop * 2);
            var t2 = new System.Collections.Generic.List<int>(tris.Count * 2 + 65536);

            v2.AddRange(verts);
            c2.AddRange(cols);
            f2.AddRange(freezeVerts);

            for (int i = 0; i < nTop; i++)
            {
                Vector3 p = verts[i];
                v2.Add(new Vector3(p.x, p.y - depth, p.z));
                c2.Add(cols[i]);
                f2.Add(freezeVerts[i]);
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
            freezeVerts = f2;
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
        _riverSurfaceMesh.SetUVs(1, freezeVerts);
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

        float y = GetOceanWaterSurfaceY();
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
        Color cOcean = planetGenerator != null && planetGenerator.mapType == MapType.Demonic
            ? new Color(0.38f, 0.43f, 0.47f, 1f / 3f)
            : new Color(0.10f, 0.40f, 0.72f, 1f / 3f);
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
        
        InitializeGhostSourceColumns();
        ghostColumnsCreated = true;
        CreateGhostObjectsForAllRegistered();

        UpdateGhostSeasonMasks();

        if (debugWrap)
        {
            Debug.Log($"[HexMapChunkManager][WRAP] Created ghost columns: mirror={columnsToMirror}, mapWidth={mapWidth:F3}, chunksX={chunksX}, columnWidth={columnWidth:F3}, ghostObjects={_ghostObjects.Count}");
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
                // compute world-space delta for registered objects
                Vector3 oldLocal = new Vector3(oldX, col.localPosition.y, col.localPosition.z);
                Vector3 newLocal = new Vector3(newX, col.localPosition.y, col.localPosition.z);
                Vector3 oldWorld = transform.TransformPoint(oldLocal);
                Vector3 newWorld = transform.TransformPoint(newLocal);
                Vector3 deltaWorld = newWorld - oldWorld;

                col.localPosition = new Vector3(newX, col.localPosition.y, col.localPosition.z);
                // Move registered objects with this column
                TeleportRegisteredObjectsForColumn(i, deltaWorld);
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
                Vector3 oldLocal = new Vector3(oldX, col.localPosition.y, col.localPosition.z);
                Vector3 newLocal = new Vector3(newX, col.localPosition.y, col.localPosition.z);
                Vector3 oldWorld = transform.TransformPoint(oldLocal);
                Vector3 newWorld = transform.TransformPoint(newLocal);
                Vector3 deltaWorld = newWorld - oldWorld;

                col.localPosition = new Vector3(newX, col.localPosition.y, col.localPosition.z);
                TeleportRegisteredObjectsForColumn(i, deltaWorld);
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

    // ------------------------- Wrap registry API -------------------------
    /// <summary>
    /// Register a GameObject for wrap teleportation using a tile index (manager will find which column it belongs to).
    /// Safe to call multiple times for same object.
    /// </summary>
    public void RegisterObjectForWrapAtTile(int tileIndex, GameObject go)
    {
        if (go == null) return;

        if (tileToChunk.TryGetValue(tileIndex, out var chunk) && chunk != null)
        {
            RegisterObjectForWrapColumn(chunk.ColumnIndex, go);
            return;
        }

        // Fallback when tileToChunk not yet populated (e.g. registration during/right after OnPlanetReady
        // before AssignTilesToChunksCoroutine has run). Derive column from tile index and grid layout
        // so units/resources/improvements don't silently fail to wrap and "disappear" at the boundary.
        if (grid != null && grid.Width > 0 && chunksX > 0 && tileIndex >= 0 && tileIndex < grid.TileCount)
        {
            int tileCol = tileIndex % grid.Width;
            float normalizedX = (tileCol + 0.5f) / (float)grid.Width;
            int columnIndex = Mathf.Clamp(Mathf.FloorToInt(normalizedX * chunksX), 0, chunksX - 1);
            RegisterObjectForWrapColumn(columnIndex, go);
        }
    }

    /// <summary>
    /// Register a GameObject to be teleported whenever the given column index is teleported.
    /// </summary>
    public void RegisterObjectForWrapColumn(int columnIndex, GameObject go)
    {
        if (go == null) return;
        if (columnIndex < 0 || columnIndex >= chunksX) return;
        if (_objectToColumn.TryGetValue(go, out var previousColumn))
        {
            if (previousColumn == columnIndex)
            {
                if (_wrapRegistryByColumn.TryGetValue(previousColumn, out var existingSet) && !existingSet.Contains(go))
                    existingSet.Add(go);
            }
            else
            {
                if (_wrapRegistryByColumn.TryGetValue(previousColumn, out var previousSet))
                    previousSet.Remove(go);
                DestroyGhostObjectsFor(go);
            }
        }

        if (!_wrapRegistryByColumn.TryGetValue(columnIndex, out var set))
        {
            set = new HashSet<GameObject>();
            _wrapRegistryByColumn[columnIndex] = set;
        }

        bool addedToSet = set.Add(go);
        _objectToColumn[go] = columnIndex;

        if (addedToSet || previousColumn != columnIndex)
        {
            if (ghostColumnsCreated) CreateGhostObjectsFor(go, columnIndex);
            // Prevent dynamic occlusion culling from hiding registered objects near wrap seams.
            // Many decoration/instance prefabs are dynamic and can be incorrectly occlusion-culled
            // when columns teleport. Force their renderers to stay visible when dynamic.
            try
            {
                var rends = go.GetComponentsInChildren<Renderer>(true);
                foreach (var r in rends)
                {
                    if (r == null) continue;
                    r.allowOcclusionWhenDynamic = false;
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Unregister a previously registered GameObject.
    /// </summary>
    public void UnregisterObjectForWrap(GameObject go)
    {
        if (go == null) return;
        if (_objectToColumn.TryGetValue(go, out var col))
        {
            if (_wrapRegistryByColumn.TryGetValue(col, out var set)) set.Remove(go);
            _objectToColumn.Remove(go);
        }
        DestroyGhostObjectsFor(go);
    }

    private void TeleportRegisteredObjectsForColumn(int columnIndex, Vector3 deltaWorld)
    {
        if (deltaWorld == Vector3.zero) return;
        if (!_wrapRegistryByColumn.TryGetValue(columnIndex, out var set) || set == null) return;

        var toRemove = new List<GameObject>();
        foreach (var go in set)
        {
            if (go == null) { toRemove.Add(go); continue; }

            try
            {
                if (debugWrapVerbose)
                {
                    try { LogObjectDiagnostics(go, "BeforeTeleport"); } catch { }
                }
                // NavMeshAgent: use Warp to preserve agent state
                if (go.TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var agent))
                {
                    Vector3 target = go.transform.position + deltaWorld;
                    agent.Warp(target);
                    if (debugWrapVerbose) try { LogObjectDiagnostics(go, "AfterTeleport"); } catch { }
                    continue;
                }

                // Rigidbody: move with physics-aware positioning
                if (go.TryGetComponent<Rigidbody>(out var rb))
                {
                    // Use MovePosition for kinematic Rigidbodies, otherwise set position directly
                    if (rb.isKinematic)
                        rb.MovePosition(rb.position + deltaWorld);
                    else
                        rb.position = rb.position + deltaWorld;
                    continue;
                }

                // Default: adjust transform position
                go.transform.position = go.transform.position + deltaWorld;
                // Refresh renderer/animator state to avoid disappearing due to occlusion/animation culling.
                try { SanitizeRenderers(go); } catch { }
                if (debugWrapVerbose)
                {
                    try { LogObjectDiagnostics(go, "AfterTeleport"); } catch { }
                }
            }
            catch { }
        }

        // Clean up any collected null entries
        foreach (var r in toRemove) set.Remove(r);
    }

    // -------------------- Ghost object system --------------------

    private Transform GetGhostObjectContainer()
    {
        if (_ghostObjectContainer == null)
        {
            var go = new GameObject("_GhostObjects");
            go.transform.SetParent(transform, false);
            _ghostObjectContainer = go.transform;
        }
        return _ghostObjectContainer;
    }

    private void InitializeGhostSourceColumns()
    {
        _ghostLeftSourceCols.Clear();
        _ghostRightSourceCols.Clear();

        int columnsToMirror = Mathf.Max(2, Mathf.CeilToInt(chunksX * 0.25f));
        for (int i = 0; i < columnsToMirror; i++)
        {
            _ghostRightSourceCols.Add(i);
            _ghostLeftSourceCols.Add(chunksX - 1 - i);
        }
    }

    private void CreateGhostObjectsForAllRegistered()
    {
        foreach (var kvp in _wrapRegistryByColumn)
        {
            int col = kvp.Key;
            if (!_ghostLeftSourceCols.Contains(col) && !_ghostRightSourceCols.Contains(col)) continue;

            foreach (var go in kvp.Value)
            {
                if (go == null) continue;
                CreateGhostObjectsFor(go, col);
            }
        }
    }

    private void CreateGhostObjectsFor(GameObject source, int columnIndex)
    {
        if (source == null || !ghostColumnsCreated) return;
        if (_ghostObjects.ContainsKey(source)) return;

        bool needsLeft = _ghostLeftSourceCols.Contains(columnIndex);
        bool needsRight = _ghostRightSourceCols.Contains(columnIndex);
        if (!needsLeft && !needsRight) return;

        var entries = new List<GhostObjectEntry>();

        if (needsRight)
        {
            var ghost = CreateGhostClone(source);
            if (ghost != null)
                entries.Add(new GhostObjectEntry { ghost = ghost, isRightGhost = true });
        }
        if (needsLeft)
        {
            var ghost = CreateGhostClone(source);
            if (ghost != null)
                entries.Add(new GhostObjectEntry { ghost = ghost, isRightGhost = false });
        }

        if (entries.Count > 0)
            _ghostObjects[source] = entries;
    }

    /// <summary>
    /// Create a lightweight renderer-only clone of a source GameObject.
    /// Copies only MeshFilter+MeshRenderer pairs, preserving the transform hierarchy.
    /// </summary>
    private GameObject CreateGhostClone(GameObject source)
    {
        if (source == null) return null;

        var ghost = new GameObject(source.name + "_WrapGhost");
        ghost.transform.SetParent(GetGhostObjectContainer(), false);
        ghost.transform.position = source.transform.position;
        ghost.transform.rotation = source.transform.rotation;
        // Match layer so camera culling masks remain consistent with the source object.
        try { ghost.layer = source.layer; } catch { }

        BuildGhostHierarchy(source.transform, ghost.transform);

        return ghost;
    }

    private void BuildGhostHierarchy(Transform sourceNode, Transform ghostNode)
    {
        var sourceMF = sourceNode.GetComponent<MeshFilter>();
        var sourceMR = sourceNode.GetComponent<MeshRenderer>();
        if (sourceMF != null && sourceMR != null && sourceMF.sharedMesh != null)
        {
            var mf = ghostNode.gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = sourceMF.sharedMesh;

            var mr = ghostNode.gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterials = sourceMR.sharedMaterials;
            mr.shadowCastingMode = sourceMR.shadowCastingMode;
            mr.receiveShadows = sourceMR.receiveShadows;

            // Ensure ghosts are not occlusion-culled. Also copy any property block so appearance matches.
            try { mr.allowOcclusionWhenDynamic = false; } catch { }
            var block = new MaterialPropertyBlock();
            sourceMR.GetPropertyBlock(block);
            if (!block.isEmpty) mr.SetPropertyBlock(block);
            // Match the layer so the ghost appears under the same camera culling rules as the source.
            try { ghostNode.gameObject.layer = sourceNode.gameObject.layer; } catch { }
        }

        for (int i = 0; i < sourceNode.childCount; i++)
        {
            var sourceChild = sourceNode.GetChild(i);
            var ghostChild = new GameObject(sourceChild.name);
            ghostChild.transform.SetParent(ghostNode, false);
            ghostChild.transform.localPosition = sourceChild.localPosition;
            ghostChild.transform.localRotation = sourceChild.localRotation;
            ghostChild.transform.localScale = sourceChild.localScale;
            try { ghostChild.layer = sourceChild.gameObject.layer; } catch { }
            BuildGhostHierarchy(sourceChild, ghostChild.transform);
        }
    }

    private void UpdateGhostObjects()
    {
        if (_ghostObjects.Count == 0) return;

        Vector3 rightOffset = transform.TransformDirection(new Vector3(mapWidth, 0f, 0f));
        Vector3 leftOffset = -rightOffset;

        List<GameObject> toRemove = null;

        foreach (var kvp in _ghostObjects)
        {
            var source = kvp.Key;
            if (source == null)
            {
                if (toRemove == null) toRemove = new List<GameObject>();
                toRemove.Add(source);
                continue;
            }

            foreach (var entry in kvp.Value)
            {
                if (entry.ghost == null) continue;
                Vector3 offset = entry.isRightGhost ? rightOffset : leftOffset;
                entry.ghost.transform.position = source.transform.position + offset;
                entry.ghost.transform.rotation = source.transform.rotation;
            }
        }

        if (toRemove != null)
        {
            foreach (var key in toRemove)
            {
                if (_ghostObjects.TryGetValue(key, out var entries))
                {
                    foreach (var e in entries)
                    {
                        if (e.ghost != null) Destroy(e.ghost);
                    }
                }
                _ghostObjects.Remove(key);
            }
        }
    }

    /// <summary>
    /// Ensure renderers and animators on a teleported object won't be culled or stopped
    /// by offscreen/occlusion heuristics. This sets conservative flags for runtime
    /// objects that move with wrap teleports.
    /// </summary>
    private void SanitizeRenderers(GameObject go)
    {
        if (go == null) return;

        // Disable occlusion-based culling on all renderers and ensure they are enabled
        var rends = go.GetComponentsInChildren<Renderer>(true);
        foreach (var r in rends)
        {
            if (r == null) continue;
            try
            {
                r.allowOcclusionWhenDynamic = false;
                if (!r.enabled) r.enabled = true;
            }
            catch { }

            // Special-case skinned meshes: make sure they update even offscreen
            if (r is SkinnedMeshRenderer smr)
            {
                try { smr.updateWhenOffscreen = true; } catch { }
            }
        }

        // Ensure animators keep animating so skinned meshes don't collapse
        var animators = go.GetComponentsInChildren<Animator>(true);
        foreach (var a in animators)
        {
            if (a == null) continue;
            try { a.cullingMode = AnimatorCullingMode.AlwaysAnimate; } catch { }
        }
    }

    private void LogObjectDiagnostics(GameObject go, string stage)
    {
        if (go == null)
        {
            Debug.Log($"[HexMapChunkManager][WRAP][{stage}] GameObject is null");
            return;
        }

        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendFormat("[HexMapChunkManager][WRAP][{0}] ", stage);
            sb.AppendFormat("name={0} ", go.name);
            sb.AppendFormat("active={0} ", go.activeInHierarchy);
            sb.AppendFormat("layer={0} ", go.layer);
            sb.AppendFormat("pos={0} ", go.transform.position.ToString("F3"));
            sb.AppendFormat("parent={0} ", GetTransformPath(go.transform.parent));

            var rends = go.GetComponentsInChildren<Renderer>(true);
            sb.AppendFormat("renderers={0} ", rends.Length);
            Bounds? combined = null;
            foreach (var r in rends)
            {
                if (r == null) continue;
                try
                {
                    var b = r.bounds;
                    if (combined == null) combined = b;
                    else { var c = combined.Value; c.Encapsulate(b); combined = c; }
                }
                catch { }
            }
            if (combined != null) sb.AppendFormat("boundsCenter={0} boundsSize={1} ", combined.Value.center.ToString("F3"), combined.Value.size.ToString("F3"));

            int skinned = go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;
            int anims = go.GetComponentsInChildren<Animator>(true).Length;
            int agents = go.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true).Length;
            int rbs = go.GetComponentsInChildren<Rigidbody>(true).Length;
            int lods = go.GetComponentsInChildren<LODGroup>(true).Length;

            sb.AppendFormat("skinned={0} animators={1} navAgents={2} rigidbodies={3} lods={4}", skinned, anims, agents, rbs, lods);

            Debug.Log(sb.ToString());
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[HexMapChunkManager][WRAP] Failed to log diagnostics for {go?.name}: {ex.Message}");
        }
    }

    private void DestroyGhostObjectsFor(GameObject source)
    {
        if (_ghostObjects.TryGetValue(source, out var entries))
        {
            foreach (var e in entries)
            {
                if (e.ghost != null) Destroy(e.ghost);
            }
            _ghostObjects.Remove(source);
        }
    }

    private void DestroyAllGhostObjects()
    {
        foreach (var kvp in _ghostObjects)
        {
            foreach (var e in kvp.Value)
            {
                if (e.ghost != null) DestroyImmediate(e.ghost);
            }
        }
        _ghostObjects.Clear();

        if (_ghostObjectContainer != null)
        {
            DestroyImmediate(_ghostObjectContainer.gameObject);
            _ghostObjectContainer = null;
        }
    }

    // -------------------- End ghost object system --------------------

    private float _lastWrapCamX = float.NegativeInfinity;
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
        DestroyAllGhostObjects();
        _ghostLeftSourceCols.Clear();
        _ghostRightSourceCols.Clear();

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
        if (terrainRenderPath == TerrainRenderPath.BakedHdrpLit)
        {
            BuildBakedHdrpLitTerrainMaps();
            BuildNeutralBakedMaskMap();
        }
        else
        {
            ApplyBiomeMaterialSettings();
        }
        // Ensure season masks are enabled when winter begins so the per-tile
        // snow/wet/dry masks are applied by the terrain shader. This covers
        // cases where global snow amount is already 1 and enableSeasonMasks
        // remained false (eg. forced season change without a prior toggle).
        if (terrainRenderPath == TerrainRenderPath.CustomBiomeShader)
        {
            if (season == Season.Winter && !enableSeasonMasks)
            {
                enableSeasonMasks = true;
            }
            UpdateSeasonMasksBatched(season, chunksPerBatch);
        }

        SyncFrozenWaterTerrainOverrides();

        if (season != Season.Winter)
            RebuildSeasonalWaterVisuals();
    }

    // ─────────────────────────────────────────────────────────────
    // Freeze mask event handlers
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called once per freeze season start after ClimateManager has written
    /// <c>tile.freezeTarget</c>.  Triggers a batched bake of the per-chunk
    /// _FreezeMaskTex.  Only acts on the planet this manager is tracking.
    /// </summary>
    private void HandleFreezeTargetsReady(int planetIndex)
    {
        if (planetGenerator == null || planetGenerator.planetIndex != planetIndex) return;
        Debug.Log($"[HexMapChunkManager] HandleFreezeTargetsReady received for planet {planetIndex}. Chunks exist={chunks != null}. SharedMaterial={sharedMaterial != null}");
        UpdateFreezeTargetMasksBatched();
        SyncFrozenWaterTerrainOverrides();
        RebuildSeasonalWaterVisuals();
    }

    /// <summary>
    /// Called every frame during a freeze or thaw animation.
    /// Updates the _FreezeProgress property on the shared material so the shader
    /// blends between water and ice in real time — no per-chunk texture updates needed.
    /// </summary>
    private void HandleFreezeProgressChanged(int planetIndex, float progress, bool isFreeze)
    {
        if (planetGenerator == null || planetGenerator.planetIndex != planetIndex) return;
        if (sharedMaterial == null)
        {
            Debug.LogWarning($"[HexMapChunkManager] HandleFreezeProgressChanged: sharedMaterial is NULL! progress={progress:F3}");
            return;
        }
        sharedMaterial.SetFloat("_FreezeProgress", progress);
        if (waterMaterial != null)
            waterMaterial.SetFloat("_FreezeProgress", progress);
        SyncFrozenWaterTerrainOverrides();
        // Log periodically (every ~0.25 progress increment) to avoid spam
        if (Mathf.Abs(progress % 0.25f) < Time.deltaTime / Mathf.Max(0.01f, 1f))
            Debug.Log($"[HexMapChunkManager] _FreezeProgress set to {progress:F3} (isFreeze={isFreeze})");
    }

    // Coroutine handle so we can cancel a mid-flight bake if a new season starts
    private Coroutine _freezeMaskCoroutine = null;

    /// <summary>
    /// Kick off a batched coroutine to bake the per-chunk freeze target mask textures.
    /// Safe to call from event handlers.
    /// </summary>
    private void UpdateFreezeTargetMasksBatched()
    {
        if (planetGenerator == null || chunks == null || bakeResult.lut == null) return;

        if (_freezeMaskCoroutine != null)
        {
            StopCoroutine(_freezeMaskCoroutine);
            _freezeMaskCoroutine = null;
        }
        _freezeMaskCoroutine = StartCoroutine(UpdateFreezeTargetMasksCoroutine());
    }

    private bool ShouldHideLiquidWater(HexTileData tile)
    {
        if (tile == null || tile.waterType == TileWaterType.None) return false;
        if (!IsFreezableWater(tile)) return false;
        if ((tile.freezeTarget <= 0.001f && tile.freezeAmount <= 0.001f) || HasWaterFreezeVisuals(tile)) return false;
        if (ClimateManager.Instance == null || planetGenerator == null) return false;
        if (ClimateManager.Instance.GetSeasonForPlanet(planetGenerator.planetIndex) != Season.Winter) return false;
        return true;
    }

    private void SyncFrozenWaterTerrainOverrides()
    {
        if (planetGenerator == null || planetGenerator.data == null)
            return;

        var solidNow = new HashSet<int>();
        var changedTiles = new List<int>();

        foreach (var kvp in planetGenerator.data)
        {
            if (!IsSolidFrozenWater(kvp.Value))
                continue;

            solidNow.Add(kvp.Key);
            if (!_solidFrozenWaterTiles.Contains(kvp.Key))
                changedTiles.Add(kvp.Key);
        }

        foreach (int tileIndex in _solidFrozenWaterTiles)
        {
            if (!solidNow.Contains(tileIndex))
                changedTiles.Add(tileIndex);
        }

        if (changedTiles.Count > 0)
            RebakeBakedTerrainForTiles(changedTiles);

        _solidFrozenWaterTiles.Clear();
        foreach (int tileIndex in solidNow)
            _solidFrozenWaterTiles.Add(tileIndex);
    }

    private void RebuildSeasonalWaterVisuals()
    {
        if (chunks != null)
            StartCoroutine(BuildAllWaterMeshesCoroutine());

        if (enableContinuousRiverSurface)
            StartCoroutine(BuildContinuousRiverSurfaceMeshCoroutine());
    }

    private System.Collections.IEnumerator UpdateFreezeTargetMasksCoroutine()
    {
        if (chunks == null) yield break;

        int lutWidth  = bakeResult.width  > 0 ? bakeResult.width  : textureWidth;
        int lutHeight = bakeResult.height > 0 ? bakeResult.height : textureHeight;

        Debug.Log($"[HexMapChunkManager] UpdateFreezeTargetMasksCoroutine started. LUT={lutWidth}x{lutHeight}, chunks={chunksX}x{chunksZ}, seasonMask={seasonMaskWidth}x{seasonMaskHeight}");

        int processed = 0;
        int chunksUpdated = 0;
        for (int x = 0; x < chunksX; x++)
        {
            for (int z = 0; z < chunksZ; z++)
            {
                var chunk = chunks[x, z];
                if (chunk == null) continue;

                chunk.UpdateFreezeTargetMask(
                    lutWidth,
                    lutHeight,
                    seasonMaskWidth,
                    seasonMaskHeight,
                    bakeResult.lut,
                    planetGenerator);

                chunksUpdated++;
                processed++;
                if (processed >= chunksPerBatch)
                {
                    processed = 0;
                    yield return null;
                }
            }
        }

        Debug.Log($"[HexMapChunkManager] UpdateFreezeTargetMasksCoroutine finished. {chunksUpdated} chunks updated.");

        // Ghost column property blocks inherit the freeze mask automatically because
        // UpdateFreezeTargetMask writes to the same MaterialPropertyBlock that
        // CopySeasonMaskToGhostColumn (called from UpdateGhostSeasonMasks) copies.
        UpdateGhostSeasonMasks();
        _freezeMaskCoroutine = null;
    }


    private void UpdateSnow()
    {
        Season season = Season.Spring;
        var cm = ClimateManager.Instance;
        if (cm != null)
        {
            int pIndex = planetGenerator != null ? planetGenerator.planetIndex
                : (GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0);
            season = cm.GetSeasonForPlanet(pIndex);
        }

        float newTarget = season == Season.Winter ? 1f : 0f;
        bool targetChanged = !Mathf.Approximately(newTarget, _targetGlobalSnowAmount);
        _targetGlobalSnowAmount = newTarget;

        if (targetChanged && newTarget > 0f && !enableSeasonMasks && terrainRenderPath == TerrainRenderPath.CustomBiomeShader)
        {
            enableSeasonMasks = true;
            UpdateSeasonMasksBatched(season, chunksPerBatch);
        }

        if (!targetChanged && Mathf.Approximately(_currentGlobalSnowAmount, _targetGlobalSnowAmount))
            return;

        _currentGlobalSnowAmount = Mathf.MoveTowards(
            _currentGlobalSnowAmount,
            _targetGlobalSnowAmount,
            Time.deltaTime / Mathf.Max(globalSnowTransitionDuration, 0.01f));

        globalSnowAmount = _currentGlobalSnowAmount;

        if (sharedMaterial != null)
        {
            sharedMaterial.SetFloat(_GlobalSnowAmountID, _currentGlobalSnowAmount);
        }

        Shader.SetGlobalFloat(_GlobalSnowAmountID, _currentGlobalSnowAmount);
    }

    private void UpdateSeasonMasksForCurrentSeason()
    {
        if (terrainRenderPath == TerrainRenderPath.BakedHdrpLit) return;
        if (!enableSeasonMasks) return;
        if (planetGenerator == null) return;
        var climateManager = GameManager.Instance != null
            ? GameManager.Instance.GetClimateManager(planetGenerator.planetIndex)
            : ClimateManager.Instance;
        if (climateManager == null) return;

        UpdateSeasonMasksBatched(climateManager.GetSeasonForPlanet(planetGenerator.planetIndex), chunksPerBatch);
    }

    private void UpdateSeasonMasksForSeason(Season season)
    {
        if (terrainRenderPath == TerrainRenderPath.BakedHdrpLit) return;
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

    // Batched coroutine for updating season masks to avoid frame spikes.
    private Coroutine _seasonMaskCoroutine = null;

    public void UpdateSeasonMasksBatched(Season season, int chunksPerFrame = 2)
    {
        if (terrainRenderPath == TerrainRenderPath.BakedHdrpLit) return;
        if (!enableSeasonMasks) return;
        if (planetGenerator == null || chunks == null || bakeResult.lut == null) return;

        if (_seasonMaskCoroutine != null)
        {
            StopCoroutine(_seasonMaskCoroutine);
            _seasonMaskCoroutine = null;
        }
        _seasonMaskCoroutine = StartCoroutine(UpdateSeasonMasksForSeasonCoroutine(season, chunksPerFrame));
    }

    private System.Collections.IEnumerator UpdateSeasonMasksForSeasonCoroutine(Season season, int chunksPerFrame)
    {
        if (chunks == null) yield break;
        if (chunksPerFrame <= 0) chunksPerFrame = 1;

        int lutWidth = bakeResult.width > 0 ? bakeResult.width : textureWidth;
        int lutHeight = bakeResult.height > 0 ? bakeResult.height : textureHeight;

        var climateManager = GameManager.Instance != null
            ? GameManager.Instance.GetClimateManager(planetGenerator.planetIndex)
            : ClimateManager.Instance;
        if (climateManager == null) yield break;

        int processed = 0;
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

                processed++;
                if (processed >= chunksPerFrame)
                {
                    processed = 0;
                    yield return null;
                }
            }
        }

        UpdateGhostSeasonMasks();
        _seasonMaskCoroutine = null;
    }
    
    /// <summary>
    /// Mark a specific tile as changed and refresh its chunk.
    /// Call this when tile data changes (biome, elevation, etc.)
    /// </summary>
    public void MarkTileDirty(int tileIndex)
    {
        RebakeBakedTerrainForTile(tileIndex);

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
        var tileList = tileIndices as IList<int> ?? tileIndices.ToList();
        RebakeBakedTerrainForTiles(tileList);

        HashSet<HexMapChunk> affectedChunks = new HashSet<HexMapChunk>();
        
        foreach (int idx in tileList)
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
        
        if (waterPickingCollider != null)
        {
            DestroyImmediate(waterPickingCollider.gameObject);
            waterPickingCollider = null;
        }
        
        if (orbitPickingCollider != null)
        {
            DestroyImmediate(orbitPickingCollider.gameObject);
            orbitPickingCollider = null;
        }
        
        if (sharedMaterial != null)
        {
            bool sharedWasBakedMaterial = ReferenceEquals(bakedLitTerrainMaterial, sharedMaterial);
            DestroyImmediate(sharedMaterial);
            sharedMaterial = null;
            if (sharedWasBakedMaterial)
                bakedLitTerrainMaterial = null;
        }
        
        tileToChunk.Clear();
        // Clear wrap registry
        _wrapRegistryByColumn?.Clear();
        _objectToColumn?.Clear();
        _ghostObjects?.Clear();
        _ghostLeftSourceCols?.Clear();
        _ghostRightSourceCols?.Clear();
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
            sharedMaterial.SetFloat("_CliffSliceCount", 0f);
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
        if (bakedTerrainBaseColor != null) { UnityEngine.Object.DestroyImmediate(bakedTerrainBaseColor); bakedTerrainBaseColor = null; }
        if (bakedTerrainMaskMap != null) { UnityEngine.Object.DestroyImmediate(bakedTerrainMaskMap); bakedTerrainMaskMap = null; }
        if (orbitOverlayMaterial != null) { UnityEngine.Object.DestroyImmediate(orbitOverlayMaterial); orbitOverlayMaterial = null; }
        if (orbitOverlayObj != null) { UnityEngine.Object.DestroyImmediate(orbitOverlayObj); orbitOverlayObj = null; }
        if (waterSurfaceOverlayMaterial != null) { UnityEngine.Object.DestroyImmediate(waterSurfaceOverlayMaterial); waterSurfaceOverlayMaterial = null; }
        if (waterSurfaceOverlayObj != null) { UnityEngine.Object.DestroyImmediate(waterSurfaceOverlayObj); waterSurfaceOverlayObj = null; }

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
