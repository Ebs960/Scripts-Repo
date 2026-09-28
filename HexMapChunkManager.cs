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
using UnityEngine.Serialization;

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
    [InspectorName("Off - Normal Terrain Rendering")] Off = 0,
    [InspectorName("1 - Raw Surface Albedo")] RawSurfaceAlbedo = 1,
    [InspectorName("2 - Substrate Albedo")] SubstrateAlbedo = 2,
    [InspectorName("3 - Final Unlit Albedo")] FinalUnlitAlbedo = 3,
    [InspectorName("4 - Simple Campaign Lit")] SimpleCampaignLit = 4,
    [InspectorName("5 - Surface Slice / Biome Index")] SliceAndBiomeIndex = 5,
    [InspectorName("6 - World Normal")] WorldNormal = 6,
    [InspectorName("7 - Mask Map RGB")] MaskMap = 7,
    [InspectorName("8 - Raw Metallic Channel")] RawMetallic = 8,
    [InspectorName("9 - Raw AO Channel")] RawAO = 9,
    [InspectorName("10 - Raw Smoothness Channel")] RawSmoothness = 10,
    [InspectorName("11 - Computed PBR Values")] ComputedPBR = 11,
    [InspectorName("12 - Macro Surface Normal")] MacroSurfaceNormal = 12
}

/// <summary>
/// Chunk-based stepped-hex campaign terrain renderer with SurfaceFamily texture-array
/// materials, world wrapping, water integration, season masks, and exact mesh picking.
/// PlanetTextureBaker remains responsible for minimap and tile lookup data only.
/// </summary>
public class HexMapChunkManager : MonoBehaviour
{


    [Header("References")]
    // Minimap/flat-map coloring is fixed to default biome colors (BiomeColorHelper).
    // Keep visuals deterministic and avoid multiple competing "color provider" assets.
    [SerializeField] private ComputeShader textureBakerComputeShader;
    [SerializeField]
    [Tooltip("Production stepped terrain shader. Must support _BiomeIndexMap, biome texture arrays, and _BiomeCount.")]
    private Shader terrainShader;
    [SerializeField] private BiomeVisualDatabase biomeVisualDatabase;
    public BiomeVisualDatabase BiomeVisuals=>biomeVisualDatabase;
    [SerializeField]
    [Tooltip("Clear cached SurfaceFamily texture arrays before rebuilding terrain materials.")]
    private bool clearSurfaceLibraryCacheBeforeBuild = true;
    [SerializeField]
    [Tooltip("Ice surface texture database (albedos, normals, tints, tiling) for lake and river freeze visuals. " +
             "Must match the IceSurfaceDatabase assigned to ClimateManager.")]
    private IceSurfaceDatabase iceSurfaceDatabase;

    [Header("Orbit Overlay")]
    [Tooltip("Shader used for the transparent orbit highlight overlay mesh (auto-found if null).")]
    [SerializeField] private Shader orbitOverlayShader;

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
    [Header("Hex Terrain Geometry")]
    [Range(0.90f, 1f)]
    [SerializeField]
    [FormerlySerializedAs("steppedHexTopScale")]
    private float hexTopScale = 1f;

    [Range(0f, 0.15f)]
    [SerializeField]
    [Tooltip("Width of the visual hex-top bevel as a fraction of the full gameplay hex radius.")]
    [FormerlySerializedAs("steppedBevelWidth")]
    private float bevelWidth = 0.04f;

    [Min(0f)]
    [SerializeField]
    [Tooltip("Vertical drop from the flat top to the outer edge of the hex-top bevel.")]
    [FormerlySerializedAs("steppedBevelDrop")]
    private float bevelDrop = 0.05f;

    [FormerlySerializedAs("steppedFlatHeightAboveSea")]
    [SerializeField] private float flatHeightAboveSea = 0.75f;
    [FormerlySerializedAs("steppedHillHeightAboveSea")]
    [SerializeField] private float hillHeightAboveSea = 3.5f;
    [FormerlySerializedAs("steppedMountainHeightAboveSea")]
    [SerializeField] private float mountainHeightAboveSea = 7.5f;

    [Header("Hill Height Variation")]
    [SerializeField] private bool enableHillHeightVariation = true;
    [SerializeField, Range(0f, 2f)] private float hillHeightVariation = 1.15f;
    [SerializeField, Min(1f)] private float hillHeightVariationWorldScale = 12f;
    [SerializeField, Range(0f, 1f)] private float hillHeightVariationSecondaryStrength = 0.25f;
    [SerializeField, Min(1f)] private float hillHeightVariationSecondaryScale = 5f;
    [SerializeField] private int hillHeightVariationSeed = 7331;
    [SerializeField, Min(0.1f)] private float minimumFlatToHillStep = 1.5f;
    [SerializeField, Min(0.1f)] private float minimumHillToMountainStep = 1.5f;

    [Header("Mountain Height Variation")]
    [SerializeField] private bool enableMountainHeightVariation = true;
    [SerializeField, Range(0f, 3f)] private float mountainHeightVariation = 1.75f;
    [SerializeField, Min(1f)] private float mountainHeightVariationWorldScale = 14f;
    [SerializeField, Range(0f, 1f)] private float mountainHeightVariationSecondaryStrength = 0.25f;
    [SerializeField, Min(1f)] private float mountainHeightVariationSecondaryScale = 6f;
    [SerializeField] private int mountainHeightVariationSeed = 19081;

    [Header("Terrain Top Shape")]
    [SerializeField] private bool enableSurfaceUndulation = true;
    [SerializeField, Range(0f, 1f)]
    [Tooltip("Small vertical relief applied to the top surface of each terrain tier. Does not change gameplay elevation tier.")]
    private float surfaceUndulationStrength = 0.12f;
    [SerializeField, Min(0.1f)]
    [Tooltip("Larger value = broader/slower rolling terrain.")]
    private float surfaceUndulationWorldScale = 10f;
    [SerializeField, Range(0f, 1f)]
    [Tooltip("Adds subtle smaller-scale macro variation.")]
    private float surfaceUndulationSecondaryStrength = 0.035f;
    [SerializeField, Min(0.1f)]
    [Tooltip("Larger value = broader/slower rolling terrain.")]
    private float surfaceUndulationSecondaryWorldScale = 3.5f;
    [SerializeField, Range(0f, 1f)]
    [Tooltip("Reduces curvature near the bevel to preserve clean stepped edges.")]
    private float surfaceEdgeFalloff = 0.25f;
    [SerializeField] private int surfaceUndulationSeed = 1337;
    [SerializeField, Range(1, 4)]
    [Tooltip("Geometry density used only for curved tile tops.")]
    private int topSubdivision = 2;

#if UNITY_EDITOR
    private void OnValidate()
    {
        hillHeightVariationWorldScale = Mathf.Max(1f, hillHeightVariationWorldScale);
        hillHeightVariationSecondaryScale = Mathf.Max(1f, hillHeightVariationSecondaryScale);
        mountainHeightVariationWorldScale = Mathf.Max(1f, mountainHeightVariationWorldScale);
        mountainHeightVariationSecondaryScale = Mathf.Max(1f, mountainHeightVariationSecondaryScale);
        minimumFlatToHillStep = Mathf.Max(0.1f, minimumFlatToHillStep);
        minimumHillToMountainStep = Mathf.Max(0.1f, minimumHillToMountainStep);
        surfaceUndulationWorldScale = Mathf.Max(0.1f, surfaceUndulationWorldScale);
        surfaceUndulationSecondaryWorldScale = Mathf.Max(0.1f, surfaceUndulationSecondaryWorldScale);
        topSubdivision = Mathf.Clamp(topSubdivision, 1, 4);
        if (!Application.isPlaying || chunks == null || grid == null || !grid.IsBuilt)
            return;

        for (int x = 0; x < chunks.GetLength(0); x++)
        for (int z = 0; z < chunks.GetLength(1); z++)
            chunks[x, z]?.ForceRefresh();
        CreatePickingCollider();
    }
#endif

    [Header("Seafloor Heights")]
    [FormerlySerializedAs("steppedOceanDepthBelowSea")]
    [Min(0f)] [SerializeField] private float oceanFloorDepthBelowSea = 2f;
    [FormerlySerializedAs("steppedAbyssalDepthBelowSea")]
    [Min(0f)] [SerializeField] private float abyssalDepthBelowSea = 3.5f;
    [FormerlySerializedAs("steppedTrenchDepthBelowSea")]
    [Min(0f)] [SerializeField] private float trenchDepthBelowSea = 5.5f;

    [Min(0f)]
    [SerializeField]
    [FormerlySerializedAs("steppedSeamDepth")]
    private float seamDepth = 0.08f;

    [Header("Terrain Placement")]
    [Tooltip("Chunk-parent Y baseline in terrain-manager space. Mesh vertices store rendered world Y minus this value, so the baseline is applied exactly once.")]
    [SerializeField] private float flatY = 0f;

    [Header("Biome Visual Modifiers")]
    [Range(0f, 1f)]
    [SerializeField] private float globalSnowAmount = 0f;
    [Min(0.01f)]
    [SerializeField] private float globalSnowTransitionDuration = 3f;

    [Header("Terrain Appearance Layers")]
    [SerializeField] private bool enableSeasonalSnow = true;
    [SerializeField] private bool enableWetnessVisuals = true;
    [SerializeField] private bool enableFreezeVisuals = true;
    [SerializeField] private bool enableSeasonalColorVariation = true;
    [SerializeField] private bool enableTerrainFogVisuals = true;
    [SerializeField] private bool enableMapModeOverlay = true;
    [SerializeField] private bool enableTerrainHighlights = true;
    [Range(0f, 0.10f)]
    [SerializeField] private float wetAlbedoDarkenMaximum = 0.10f;
    [Range(0f, 0.25f)]
    [SerializeField] private float seasonalColorStrength = 0.08f;
    [Tooltip("Development-only: display the owning SurfaceFamily albedo slice without lighting or overlays.")]
    [SerializeField] private bool forceRawTerrainAlbedo = false;
    [Header("Campaign Terrain Lighting")]
    [Range(0f, 1f)] [SerializeField] private float campaignAmbientFloor = 0.64f;
    [Range(0f, 1f)] [SerializeField] private float campaignDirectionalStrength = 0.36f;
    [Range(0f, 1f)] [SerializeField] private float campaignAOStrength = 0.30f;
    [Tooltip("Optional campaign sun. RenderSettings.sun, then the brightest enabled directional light, are used when this is unset.")]
    [SerializeField] private Light campaignDirectionalLight;
    private bool terrainLightingAuditLogged;
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
    [Tooltip("Strength multiplier for biome normal maps (surface bump detail). Higher = more visible texture bumps.")]
    [Range(0f, 5f)]
    [SerializeField] private float biomeNormalStrength = 1.0f;
    [Tooltip("Radius (in texels) used for biome blending between neighboring biome slices")]
    [Range(0f, 16f)]
    [SerializeField] private float biomeBlendRadius = 4f;

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
        "Selects a texture-authoritative production diagnostic. Raw Surface, Substrate, and Final Unlit " +
        "show successive albedo stages; Simple Campaign Lit adds the bounded production lighting. " +
        "The remaining modes inspect surface indices, normals, mask channels, and computed PBR values."
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
    [Tooltip("When enabled, logs detailed water/SDF diagnostics: pre-build tile counts, SDF seed counts, per-chunk mesh stats, post-build summary. Helps diagnose gaps or missing water.")]
    [SerializeField] private bool debugWaterVerbose = false;
    [Tooltip("Logs actual rendered center-height and visual-relief distributions after a build.")]
    [SerializeField] private bool debugTerrainRelief = false;
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
    [SerializeField] private bool enableOceanPlane = true;
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
    [SerializeField] private bool continuousWaterIncludesOcean = false;
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
    [Min(0f)]
    [Tooltip("Clearance above the authoritative stepped tile top for rivers and lakes.")]
    [SerializeField] private float steppedInlandWaterSurfaceOffset = 0.04f;

    [Header("Inland Water Volume (3D Fill)")]
    [Tooltip("When enabled, the continuous inland water surface is extruded downward into a closed 3D mesh (top + walls + bottom) so rivers/lakes look filled in 3D space.")]
    [SerializeField] private bool extrudeInlandWaterToVolume = false;
    [Header("River Terrain Channel")]
    [SerializeField, Min(0f)] private float riverChannelCarveDepth = 0.45f;
    [SerializeField, Min(1f)] private float riverBankWidthMultiplier = 1.45f;
    [SerializeField, Range(0.05f, 1f)] private float riverBankSoftness = 0.55f;
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
    private GameManager.PlanetLayerType currentViewLayer = GameManager.PlanetLayerType.Surface;
    private Transform[] columnParents;

    // Shared minimap/LUT and terrain material data.
    private PlanetTextureBaker.BakeResult bakeResult;
    private Material sharedMaterial;
    private Texture2D biomeIndexMap;
    // Cached inspector-backed runtime values for change detection
    private bool _lastUseTriplanar = true;
    private float _lastCliffTiling = -1f;
    private float _lastCliffStrength = -1f;
    private float _lastCliffSlopeThreshold = -1f;
    private float _lastCliffSlopeBlend = -1f;
    private Texture2D sliceToBiomeMap; // 1D texture: pixel[sliceIndex].r = biome index for dynamic parameters.
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
    // Base surface mapping: x=startSlice, y=variantCount, z=surfaceIndex, w=forcedVariant
    private Vector4[] biomeSurfaceMapArray;
    // Mountain override mapping: x=startSlice, y=variantCount, z=surfaceIndex, w=forcedVariant
    private Vector4[] biomeMountainSurfaceMapArray;
    private Texture2D biomeSurfaceMapTexture;
    private Texture2D biomeEmissiveMapTexture;
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
    internal int GridChunkCountX => chunksX;
    internal int GridChunkCountZ => chunksZ;
    internal float HexTopScale => hexTopScale;
    internal float BevelWidth => bevelWidth;
    internal float BevelDrop => bevelDrop;
    internal float SeamDepth => seamDepth;
    internal int TopSubdivision => Mathf.Clamp(topSubdivision, 1, 4);
    public float MapHeight => mapHeight;
    public bool IsBuilt => chunks != null;
    public Texture MapTexture => bakeResult.texture;
    public int[] LUT => bakeResult.lut;
    public int LUTWidth => bakeResult.width;
    public int LUTHeight => bakeResult.height;
    public Material SharedMaterial => sharedMaterial;
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
    /// Applies the active view's inexpensive terrain/water presentation and picking state.
    /// This never rebuilds geometry or changes map/gameplay data.
    /// </summary>
    public void ApplyViewLayer(GameManager.PlanetLayerType layer)
    {
        currentViewLayer = layer;

        bool terrainVisible = layer != GameManager.PlanetLayerType.Orbit;
        bool surfaceWaterVisible = layer == GameManager.PlanetLayerType.Surface;
        bool orbitPickingEnabled = layer == GameManager.PlanetLayerType.Orbit;

        if (chunks != null)
        {
            for (int x = 0; x < chunks.GetLength(0); x++)
            {
                for (int z = 0; z < chunks.GetLength(1); z++)
                {
                    var chunk = chunks[x, z];
                    if (chunk == null) continue;
                    chunk.SetTerrainVisible(terrainVisible);
                    chunk.SetWaterVisible(surfaceWaterVisible);
                }
            }
        }

        SetGhostColumnVisibility(ghostColumnsLeft, terrainVisible, surfaceWaterVisible);
        SetGhostColumnVisibility(ghostColumnsRight, terrainVisible, surfaceWaterVisible);

        SetRendererEnabled(_riverSurfaceObj, surfaceWaterVisible);
        SetRendererEnabled(_riverSurfaceGhostL, surfaceWaterVisible);
        SetRendererEnabled(_riverSurfaceGhostR, surfaceWaterVisible);
        SetRendererEnabled(_oceanPlaneObj, surfaceWaterVisible);
        SetRendererEnabled(_oceanPlaneGhostL, surfaceWaterVisible);
        SetRendererEnabled(_oceanPlaneGhostR, surfaceWaterVisible);
        SetRendererEnabled(waterSurfaceOverlayObj, surfaceWaterVisible);

        if (pickingCollider != null)
            pickingCollider.enabled = !orbitPickingEnabled;
        if (waterPickingCollider != null)
            waterPickingCollider.enabled = surfaceWaterVisible;
        if (orbitPickingCollider != null)
            orbitPickingCollider.enabled = orbitPickingEnabled;
    }

    private static void SetRendererEnabled(GameObject obj, bool visible)
    {
        if (obj == null) return;
        var renderer = obj.GetComponent<MeshRenderer>();
        if (renderer != null)
            renderer.enabled = visible;
    }

    private static void SetGhostColumnVisibility(Transform[] columns, bool terrainVisible, bool waterVisible)
    {
        if (columns == null) return;
        foreach (var column in columns)
        {
            if (column == null) continue;
            for (int i = 0; i < column.childCount; i++)
            {
                Transform ghostChunk = column.GetChild(i);
                var terrainRenderer = ghostChunk.GetComponent<MeshRenderer>();
                if (terrainRenderer != null)
                    terrainRenderer.enabled = terrainVisible;

                Transform water = ghostChunk.Find("Water");
                if (water == null) continue;
                var waterRenderer = water.GetComponent<MeshRenderer>();
                if (waterRenderer != null)
                    waterRenderer.enabled = waterVisible;
            }
        }
    }

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
        bool applied = false;

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

        if (_lastCliffTiling != cliffTiling || _lastCliffStrength != cliffStrength || _lastCliffSlopeThreshold != cliffSlopeThreshold || _lastCliffSlopeBlend != cliffSlopeBlend)
        {
            _lastCliffTiling = cliffTiling;
            _lastCliffStrength = cliffStrength;
            _lastCliffSlopeThreshold = cliffSlopeThreshold;
            _lastCliffSlopeBlend = cliffSlopeBlend;
            applied = true;
        }

        if (applied)
            ApplyBiomeMaterialSettings();
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
        var climate = GameManager.Instance != null ? GameManager.Instance.GetClimateManager(planetGenerator.planetIndex) : ClimateManager.Instance;
        string season = climate != null ? climate.GetSeasonForPlanet(planetGenerator.planetIndex).ToString() : "Unknown";

        float nominalTierY = GetNominalTerrainWorldY(tile);
        float renderedBaseY = GetRenderedTerrainWorldY(tileIndex);
        float surfaceY = SampleRenderedTerrainSurfaceY(tileIndex, hit.point.x, hit.point.z);
        Debug.Log(
            $"[Terrain Probe] tile={tileIndex} " +
            $"uv=({u:F5},{v:F5}) " +
            $"biome={tile.biome} " +
            $"visual={(visual != null ? visual.name : "NULL")} " +
            $"visual.biome={(visual != null ? visual.biome.ToString() : "NULL")} " +
            $"surfaceFamily={(surfaceFamily != null ? surfaceFamily.name : "NULL")} " +
            $"slice={sliceIndex} " +
            $"selection={(tile.isMountain ? "mountain" : "base")} " +
            $"tiling={(visual != null ? visual.tiling.ToString("F3") : "NULL")} " +
            $"season={season} " +
            $"renderedY={renderedBaseY:F3} " +
            $"[TerrainSurfaceProbe] tier={tile.elevationTier} nominalTierY={nominalTierY:F3} " +
            $"hillMacroOffset={(renderedBaseY - nominalTierY):+0.000;-0.000;0.000} " +
            $"renderedBaseY={renderedBaseY:F3} " +
            $"surfaceUndulationOffset={(surfaceY - renderedBaseY):+0.000;-0.000;0.000} " +
            $"finalSurfaceY={surfaceY:F3} " +
            $"worldX={hit.point.x:F3} worldZ={hit.point.z:F3} " +
            $"generatedElevation={tile.elevation:F3} elevationTier={tile.elevationTier} " +
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

        if (debugTerrainRelief || ShouldRunDiagnostics())
            LogTerrainReliefDiagnostics();

        // Create orbit highlight overlay (flat transparent mesh at orbit height)
        CreateOrbitOverlayMesh();

        // Create water surface highlight overlay (flat transparent mesh at water level)
        CreateWaterSurfaceOverlayMesh();

        // Initialize terrain overlays
        InitializeTerrainOverlays();

        // (FlatMapTextureRenderer removed — HexMapChunkManager is the sole renderer)

        if (enableBuildProfiling)
        {
            float now = Time.realtimeSinceStartup;
            Debug.Log($"[HexMapChunkManager][Profile] Total BuildChunks: {(now - buildStartTime) * 1000f:F2} ms");
        }

        // A layer switch can happen while the batched build is running. Reapply the
        // last requested state after every terrain/water/picking helper now exists.
        ApplyViewLayer(currentViewLayer);
        ReconcileTerrainBoundObjects();
        _buildCoroutine = null;
    }

    /// <summary>One-shot correction for presentation objects that may have spawned before terrain finished.</summary>
    private void ReconcileTerrainBoundObjects()
    {
        if (planetGenerator == null) return;
        int planetIndex = planetGenerator.planetIndex;
        TileSystem tiles = TileSystem.GetForPlanet(planetIndex);
        if (tiles == null) return;

        foreach (var band in FindObjectsByType<Band>(FindObjectsInactive.Include))
            if (band.PlanetIndex == planetIndex && band.CurrentTileIndex >= 0)
                band.transform.position = tiles.GetTileSurfacePosition(band.CurrentTileIndex);

        foreach (var unit in FindObjectsByType<BaseUnit>(FindObjectsInactive.Include))
            if (unit.planetIndex == planetIndex && unit.currentLayer == TileLayer.Surface && unit.currentTileIndex >= 0)
                unit.transform.position = tiles.GetTileSurfacePosition(unit.currentTileIndex);

        foreach (var herd in FindObjectsByType<Herd>(FindObjectsInactive.Include))
            if (herd.planetIndex == planetIndex && herd.currentTileIndex >= 0)
                herd.transform.position = tiles.GetTileSurfacePosition(herd.currentTileIndex);

        foreach (var resource in FindObjectsByType<ResourceInstance>(FindObjectsInactive.Include))
            if (resource.planetIndex == planetIndex && resource.tileIndex >= 0 && resource.data != null && !resource.data.isOrbitalResource)
                resource.GroundToSurface(
                    SampleRenderedTerrainSurfaceY(resource.tileIndex, resource.transform.position.x, resource.transform.position.z),
                    resource.data.visualGroundOffset);

        foreach (var improvement in FindObjectsByType<ImprovementInstance>(FindObjectsInactive.Include))
            if (improvement.PlanetIndex == planetIndex && improvement.tileIndex >= 0 && improvement.spaceTileIndex < 0)
                improvement.transform.position = tiles.GetTileSurfacePosition(improvement.tileIndex);
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
    }

    /// <summary>
    /// Coroutine version of BuildBiomeVisualMaps that uses a Burst job for BiomeIndexMap generation
    /// instead of per-pixel coroutine strips.
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

            // Populate dynamic per-biome material parameters
            biomeParamsArray = new Vector4[count];
            for (int i = 0; i < count; i++)
            {
                var entry = visuals[i];
                    if (entry != null)
                    {
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
            // This lets the shader look up per-biome dynamic parameters from each surface slice.
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

            ValidateAuthoredEarthSurfaceMappings(visuals);

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
        biomeParamsArray = null;
        biomeSurfaceMapArray = null;
        biomeMountainSurfaceMapArray = null;
        biomeSurfaceMapTexture = null;
        biomeEmissiveMapTexture = null;
        return;
    }

    /// <summary>
    /// Development validation only. It documents authored Earth mappings without ever
    /// remapping a biome or substituting a fallback family.
    /// </summary>
    private static void ValidateAuthoredEarthSurfaceMappings(IList<BiomeVisualData> visuals)
    {
        var expected = new Dictionary<string, string>
        {
            { "Temperate", "Temperate Family" },
            { "Plains", "Savannah and Plains Family" },
            { "Savannah", "Savannah and Plains Family" },
            { "Desert", "Desert Family" },
            { "Tropical", "Tropical Family" },
            { "Tundra", "Snow Family" },
        };

        foreach (var visual in visuals)
        {
            if (visual == null || !expected.TryGetValue(visual.biome.ToString(), out string expectedFamily))
                continue;

            string actualFamily = visual.surfaceFamily != null ? visual.surfaceFamily.name : "NULL";
            string message = $"[TerrainSurfaceValidation] {visual.biome} -> {actualFamily}";
            if (actualFamily == expectedFamily)
                Debug.Log(message + (visual.biome.ToString() == "Tundra" ? " (authored pale mapping; not a shader fallback)" : string.Empty));
            else
                Debug.LogWarning($"{message}; expected authored mapping '{expectedFamily}'. Mapping was not changed.");
        }
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

    private float GetTileWaterSurfaceY(int tileIndex, HexTileData tile, float additionalOffset = 0f)
    {
        if (tile.waterType == TileWaterType.Ocean)
            return GetOceanWaterSurfaceY(additionalOffset);

        if (tile.waterType == TileWaterType.River)
        {
            float sea = planetGenerator != null ? planetGenerator.SeaLevelWorldY : 0f;
            // Explicitly translate the normalized hydrology contract into campaign world Y.
            float riverY = Mathf.Lerp(sea, sea + mountainHeightAboveSea, Mathf.Clamp01(tile.renderedRiverSurface01));
            return riverY + steppedInlandWaterSurfaceOffset + additionalOffset;
        }
        return GetRenderedTerrainWorldY(tileIndex) + steppedInlandWaterSurfaceOffset + additionalOffset;
    }

    /// <summary>
    /// CAMPAIGN TERRAIN CONTRACT: HexTileData elevation is simulation data; this manager owns
    /// categorical rendered Y. Visible Surface objects use TileSystem.GetTileSurfacePosition.
    /// Oceans use SeaLevelWorldY, inland water uses its rendered owner tile, and picking uses
    /// the exact stepped mesh. Never derive visible campaign Y from simulation elevation.
    /// </summary>
    public float GetRenderedTerrainWorldY(int tileIndex)
    {
        float seaLevelWorldY = planetGenerator != null ? planetGenerator.SeaLevelWorldY : 0f;
        if (planetGenerator == null || planetGenerator.data == null ||
            !planetGenerator.data.TryGetValue(tileIndex, out HexTileData tile))
        {
            return seaLevelWorldY - oceanFloorDepthBelowSea;
        }

        if (!tile.isLand)
        {
            switch (tile.underwaterBiome)
            {
                case Biome.Trench:
                    return seaLevelWorldY - trenchDepthBelowSea;
                case Biome.AbyssalPlains:
                    return seaLevelWorldY - abyssalDepthBelowSea;
                default:
                    return seaLevelWorldY - oceanFloorDepthBelowSea;
            }
        }

        switch (tile.elevationTier)
        {
            case ElevationTier.Mountain:
                float nominalMountainY = seaLevelWorldY + mountainHeightAboveSea;
                float highestPossibleHillY = seaLevelWorldY + mountainHeightAboveSea - minimumHillToMountainStep;
                float minimumMountainY = highestPossibleHillY + 0.01f;
                float mountainSignal = Mathf.Clamp01(tile.visualRelief01 + GetMountainMacroHeightOffset(tileIndex));
                float mountainLow = Mathf.Max(minimumMountainY, nominalMountainY - mountainHeightVariation);
                float mountainHigh = nominalMountainY + mountainHeightVariation;
                return enableMountainHeightVariation ? Mathf.Lerp(mountainLow, mountainHigh, mountainSignal) : nominalMountainY;
            case ElevationTier.Hill:
                float nominalHillY = seaLevelWorldY + hillHeightAboveSea;
                float minimumHillY = seaLevelWorldY + flatHeightAboveSea + minimumFlatToHillStep;
                float maximumHillY = seaLevelWorldY + mountainHeightAboveSea - minimumHillToMountainStep;
                // Misconfigured tier distances collapse safely to their midpoint rather than
                // allowing a visual Hill to cross either categorical neighbour.
                if (minimumHillY > maximumHillY)
                    return (minimumHillY + maximumHillY) * 0.5f;
                if (!enableHillHeightVariation) return Mathf.Clamp(nominalHillY, minimumHillY, maximumHillY);
                // Shrink the requested interval before mapping into it. Unlike a final hard
                // clamp, this keeps the whole relief distribution rather than making plateaus.
                float hillLow = Mathf.Max(minimumHillY, nominalHillY - hillHeightVariation);
                float hillHigh = Mathf.Min(maximumHillY, nominalHillY + hillHeightVariation);
                float hillSignal = Mathf.Clamp01(tile.visualRelief01 + GetHillMacroHeightOffset(tileIndex));
                return Mathf.Lerp(hillLow, hillHigh, hillSignal);
            default:
                return seaLevelWorldY + flatHeightAboveSea;
        }
    }

    private float GetNominalTerrainWorldY(HexTileData tile)
    {
        float sea = planetGenerator != null ? planetGenerator.SeaLevelWorldY : 0f;
        if (!tile.isLand)
        {
            if (tile.underwaterBiome == Biome.Trench) return sea - trenchDepthBelowSea;
            if (tile.underwaterBiome == Biome.AbyssalPlains) return sea - abyssalDepthBelowSea;
            return sea - oceanFloorDepthBelowSea;
        }

        if (tile.elevationTier == ElevationTier.Mountain) return sea + mountainHeightAboveSea;
        if (tile.elevationTier == ElevationTier.Hill) return sea + hillHeightAboveSea;
        return sea + flatHeightAboveSea;
    }

    private void LogTerrainReliefDiagnostics()
    {
        if (planetGenerator == null || planetGenerator.data == null) return;
        var groups = new Dictionary<ElevationTier, List<Vector2>>();
        groups[ElevationTier.Flat] = new List<Vector2>();
        groups[ElevationTier.Hill] = new List<Vector2>();
        groups[ElevationTier.Mountain] = new List<Vector2>();
        foreach (var pair in planetGenerator.data)
        {
            HexTileData tile = pair.Value;
            if (!tile.isLand || !groups.TryGetValue(tile.elevationTier, out var samples)) continue;
            samples.Add(new Vector2(GetRenderedTerrainWorldY(pair.Key), Mathf.Clamp01(tile.visualRelief01)));
        }
        foreach (var pair in groups)
        {
            List<Vector2> values = pair.Value;
            if (values.Count == 0) { Debug.Log($"[TerrainRelief] {pair.Key} count=0"); continue; }
            float minY = float.MaxValue, maxY = float.MinValue, sumY = 0f, sumSq = 0f;
            float minR = 1f, maxR = 0f, sumR = 0f;
            foreach (Vector2 value in values)
            {
                minY = Mathf.Min(minY, value.x); maxY = Mathf.Max(maxY, value.x);
                sumY += value.x; sumSq += value.x * value.x;
                minR = Mathf.Min(minR, value.y); maxR = Mathf.Max(maxR, value.y); sumR += value.y;
            }
            float mean = sumY / values.Count;
            float stdDev = Mathf.Sqrt(Mathf.Max(0f, sumSq / values.Count - mean * mean));
            Debug.Log($"[TerrainRelief] {pair.Key} count={values.Count} renderedY={minY:F2}..{maxY:F2} mean={mean:F2} stdDev={stdDev:F2} visualRelief={minR:F2}..{maxR:F2} mean={sumR / values.Count:F2}");
        }
    }

    /// <summary>
    /// Samples one deterministic, wrap-periodic macro offset at the Hill tile center. It is
    /// deliberately tile-stable: top vertices only receive the separate surface undulation.
    /// </summary>
    private float GetHillMacroHeightOffset(int tileIndex)
    {
        if (!enableHillHeightVariation || hillHeightVariation <= 0f || grid == null ||
            tileIndex < 0 || tileIndex >= grid.TileCount || mapWidth <= 0.0001f)
            return 0f;

        Vector3 center = grid.tileCenters[tileIndex];
        int planetSeed = planetGenerator != null ? planetGenerator.Seed : 0;
        int seed = unchecked(planetSeed * 486187739 + hillHeightVariationSeed);
        float primary = PeriodicRollingNoise(center.x, center.z, hillHeightVariationWorldScale, seed);
        float secondary = PeriodicRollingNoise(center.x, center.z, hillHeightVariationSecondaryScale, seed ^ 0x6d2b79f5);
        float normalizedSignal = (primary + secondary * hillHeightVariationSecondaryStrength) /
                                 (1f + hillHeightVariationSecondaryStrength);
        return normalizedSignal * 0.10f;
    }

    /// <summary>
    /// Samples broad, deterministic and wrap-periodic visual relief for Mountain tiles.
    /// This is rendering-only macro geometry; it never changes simulation elevation or tier.
    /// </summary>
    private float GetMountainMacroHeightOffset(int tileIndex)
    {
        if (!enableMountainHeightVariation || mountainHeightVariation <= 0f || grid == null ||
            tileIndex < 0 || tileIndex >= grid.TileCount || mapWidth <= 0.0001f)
            return 0f;

        Vector3 center = grid.tileCenters[tileIndex];
        int planetSeed = planetGenerator != null ? planetGenerator.Seed : 0;
        int seed = unchecked(planetSeed * 486187739 + mountainHeightVariationSeed);
        float primary = PeriodicRollingNoise(center.x, center.z, mountainHeightVariationWorldScale, seed);
        float secondary = PeriodicRollingNoise(center.x, center.z, mountainHeightVariationSecondaryScale, seed ^ 0x27d4eb2d);
        float signal = (primary + secondary * mountainHeightVariationSecondaryStrength) /
                       (1f + mountainHeightVariationSecondaryStrength);
        return signal * 0.10f;
    }

    /// <summary>
    /// Returns the authoritative rendered surface at an XZ position. The categorical tier
    /// remains owned by GetRenderedTerrainWorldY; this only layers subtle visual relief on it.
    /// </summary>
    public float SampleRenderedTerrainSurfaceY(int tileIndex, float worldX, float worldZ)
    {
        float baseY = GetRenderedTerrainWorldY(tileIndex);
        if (grid == null || tileIndex < 0 || tileIndex >= grid.TileCount)
            return baseY;

        float radius = grid.GetLookupData().s * hexTopScale;
        float innerRadius = Mathf.Max(0.0001f, radius - grid.GetLookupData().s * bevelWidth);
        Vector3 center = grid.tileCenters[tileIndex];
        float radial01 = new Vector2(worldX - center.x, worldZ - center.z).magnitude / innerRadius;
        float edgeStart = 1f - Mathf.Clamp01(surfaceEdgeFalloff);
        float edgeMask = 1f - SmoothStep(edgeStart, 1f, radial01);
        // Blend the center toward deterministic shared corner heights. Along an edge both
        // tiles interpolate the same two corners, so same-tier land is watertight and sloped.
        float angle = Mathf.Atan2(worldZ - center.z, worldX - center.x) * Mathf.Rad2Deg + 30f;
        if (angle < 0f) angle += 360f;
        int sector = Mathf.FloorToInt(angle / 60f) % 6;
        float edgeT = (angle - sector * 60f) / 60f;
        float cornerA = GetSharedCornerWorldY(tileIndex, sector);
        float cornerB = GetSharedCornerWorldY(tileIndex, (sector + 1) % 6);
        float stitchedY = Mathf.Lerp(baseY, Mathf.Lerp(cornerA, cornerB, edgeT), Mathf.Clamp01(radial01));
        float micro = enableSurfaceUndulation ? SampleSurfaceUndulation(worldX, worldZ) * edgeMask : 0f;
        return stitchedY + micro - SampleRiverCarveDepth(tileIndex, worldX, worldZ, stitchedY);
    }

    internal bool IsSameTerrainTier(int a, int b)
    {
        return planetGenerator != null && planetGenerator.data != null &&
               planetGenerator.data.TryGetValue(a, out HexTileData ta) &&
               planetGenerator.data.TryGetValue(b, out HexTileData tb) && ta.isLand && tb.isLand &&
               ta.elevationTier == tb.elevationTier;
    }

    internal float GetSharedCornerWorldY(int tileIndex, int corner)
    {
        float sum = GetRenderedTerrainWorldY(tileIndex);
        int count = 1;
        Vector3 center = grid.tileCenters[tileIndex];
        float radius = grid.GetLookupData().s;
        for (int side = 0; side < 2; side++)
        {
            int edge = (corner - 1 + side + 6) % 6;
            float angle = Mathf.Deg2Rad * (edge * 60f);
            int neighbor = grid.GetTileAtPosition(center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius * 1.05f);
            if (neighbor >= 0 && neighbor != tileIndex && IsSameTerrainTier(tileIndex, neighbor))
            { sum += GetRenderedTerrainWorldY(neighbor); count++; }
        }
        return sum / count;
    }

    internal float GetRenderedCornerSurfaceY(int tileIndex, int corner)
    {
        float shared = GetSharedCornerWorldY(tileIndex, corner);
        Vector3 center = grid.tileCenters[tileIndex];
        float radius = grid.GetLookupData().s;
        float angle = Mathf.Deg2Rad * (corner * 60f - 30f);
        Vector3 p = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (radius * hexTopScale);
        return shared - SampleRiverCarveDepth(tileIndex, p.x, p.z, shared);
    }

    public float SampleRiverCarveInfluence(int tileIndex, float worldX, float worldZ)
    {
        if (planetGenerator == null || planetGenerator.data == null || grid == null) return 0f;
        float radius = grid.GetLookupData().s * Mathf.Max(0.01f, riverHalfWidthMultiplier) * riverBankWidthMultiplier;
        float best = float.MaxValue;
        void Consider(int index)
        {
            if (!planetGenerator.data.TryGetValue(index, out HexTileData td) || !td.isRiver) return;
            Vector3 c = grid.tileCenters[index];
            float dx = Mathf.Abs(worldX - c.x);
            if (enableWrap && mapWidth > 0f) dx = Mathf.Min(dx, mapWidth - dx);
            best = Mathf.Min(best, Mathf.Sqrt(dx * dx + (worldZ - c.z) * (worldZ - c.z)));
        }
        int owner = grid.GetTileAtPosition(new Vector3(worldX, 0f, worldZ));
        if (owner < 0) owner = tileIndex;
        Consider(owner);
        if (owner >= 0 && grid.neighbors[owner] != null)
            foreach (int neighbor in grid.neighbors[owner]) if (neighbor >= 0) Consider(neighbor);
        if (best == float.MaxValue) return 0f;
        float inner = radius * (1f - riverBankSoftness);
        return 1f - SmoothStep(inner, radius, best);
    }

    private float SampleRiverCarveDepth(int tileIndex, float worldX, float worldZ, float landY)
    {
        float influence = SampleRiverCarveInfluence(tileIndex, worldX, worldZ);
        if (influence <= 0f || !planetGenerator.data.TryGetValue(tileIndex, out HexTileData td)) return 0f;
        float waterY = landY - riverChannelCarveDepth;
        int owner = grid.GetTileAtPosition(new Vector3(worldX, 0f, worldZ));
        if (owner >= 0)
        {
            if (planetGenerator.data.TryGetValue(owner, out HexTileData ownerTile) && ownerTile.isRiver)
                waterY = GetTileWaterSurfaceY(owner, ownerTile);
            else if (grid.neighbors[owner] != null)
                foreach (int neighbor in grid.neighbors[owner])
                    if (neighbor >= 0 && planetGenerator.data.TryGetValue(neighbor, out HexTileData river) && river.isRiver)
                    { waterY = GetTileWaterSurfaceY(neighbor, river); break; }
        }
        float required = Mathf.Max(riverChannelCarveDepth, landY - waterY + 0.08f);
        return required * influence;
    }

    public Vector3 GetRenderedSurfacePosition(int tileIndex, Vector3 worldPosition, float verticalOffset = 0f)
    {
        worldPosition.y = SampleRenderedTerrainSurfaceY(tileIndex, worldPosition.x, worldPosition.z) + verticalOffset;
        return worldPosition;
    }

    // Periodic in map X by construction: only integer harmonics of the wrapped angle are used.
    // Z is deliberately not wrapped. This avoids global Random state and is stable per planet.
    private float SampleSurfaceUndulation(float worldX, float worldZ)
    {
        if (!enableSurfaceUndulation || mapWidth <= 0.0001f)
            return 0f;

        int planetSeed = planetGenerator != null ? planetGenerator.Seed : 0;
        int seed = unchecked(planetSeed * 486187739 + surfaceUndulationSeed);
        return PeriodicRollingNoise(worldX, worldZ, surfaceUndulationWorldScale, seed) * surfaceUndulationStrength
             + PeriodicRollingNoise(worldX, worldZ, surfaceUndulationSecondaryWorldScale, seed ^ 0x5bd1e995) * surfaceUndulationSecondaryStrength;
    }

    private float PeriodicRollingNoise(float x, float z, float scale, int seed)
    {
        scale = Mathf.Max(0.1f, scale);
        float angle = (x / mapWidth) * Mathf.PI * 2f;
        int baseHarmonic = Mathf.Max(1, Mathf.RoundToInt(mapWidth / (Mathf.PI * 2f * scale)));
        float sum = 0f;
        float weight = 0f;
        for (int octave = 0; octave < 3; octave++)
        {
            int h = baseHarmonic * (1 << octave);
            float amplitude = 1f / (1 << octave);
            float phaseX = HashAngle(seed + octave * 1013);
            float phaseZ = HashAngle(seed + octave * 1619);
            float zFrequency = (1 << octave) / scale;
            sum += Mathf.Sin(angle * h + phaseX + Mathf.Sin(z * zFrequency + phaseZ) * 0.65f)
                 * Mathf.Cos(z * zFrequency * 0.73f + phaseZ) * amplitude;
            weight += amplitude;
        }
        return weight > 0f ? sum / weight : 0f;
    }

    private static float HashAngle(int value)
    {
        unchecked
        {
            uint h = (uint)value;
            h ^= h >> 16; h *= 0x7feb352d; h ^= h >> 15; h *= 0x846ca68b; h ^= h >> 16;
            return (h / (float)uint.MaxValue) * Mathf.PI * 2f;
        }
    }

    private static float SmoothStep(float from, float to, float value)
    {
        float t = Mathf.Clamp01((value - from) / Mathf.Max(0.0001f, to - from));
        return t * t * (3f - 2f * t);
    }

    private int ResolveSurfaceSliceIndex(HexTileData tile, int stableSeed, int biomeIndex)
    {
        int arrayDepth = biomeAlbedoArray != null ? biomeAlbedoArray.depth : 0;
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

        if (sliceIndex < 0 || sliceIndex >= arrayDepth)
        {
            BiomeVisualData visual = ResolveRenderedVisual(tile);
            SurfaceFamilyData family = visual != null ? visual.surfaceFamily : null;
            Debug.LogError(
                $"[TerrainSurfaceError] planet={(planetGenerator != null ? planetGenerator.planetIndex : -1)} " +
                $"tile={stableSeed} biome={(tile != null ? tile.biome.ToString() : "NULL")} " +
                $"visual={(visual != null ? visual.name : "NULL")} " +
                $"surfaceFamily={(family != null ? family.name : "NULL")} " +
                $"requestedSlice={sliceIndex} arrayDepth={arrayDepth}");
            // Preserve the invalid value. The shader rejects it with diagnostic magenta;
            // silently clamping it would disguise a broken authored mapping as another surface.
        }
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

    /// <summary>Updates current biome and surface-slice pixels touched by the supplied tiles.</summary>
    public void UpdateTerrainDataTexturesForTiles(IEnumerable<int> tileIndices)
    {
        if (planetGenerator == null || grid == null || biomeIndexMap == null ||
            bakeResult.lut == null || bakeResult.lut.Length == 0)
            return;

        var biomeTiles = new HashSet<int>();
        foreach (int tileIndex in tileIndices)
            if (tileIndex >= 0 && tileIndex < grid.TileCount)
                biomeTiles.Add(tileIndex);

        if (biomeTiles.Count == 0) return;

        int width = bakeResult.width > 0 ? bakeResult.width : textureWidth;
        bool updated = false;
        for (int pixelIndex = 0; pixelIndex < bakeResult.lut.Length; pixelIndex++)
        {
            int tileIndex = bakeResult.lut[pixelIndex];
            if (!biomeTiles.Contains(tileIndex) ||
                !planetGenerator.data.TryGetValue(tileIndex, out var tile))
                continue;

            int biomeIndex = ResolveRenderedBiomeIndex(tile);
            int sliceIndex = ResolveSurfaceSliceIndex(tile, tileIndex, biomeIndex);
            biomeIndexMap.SetPixel(pixelIndex % width, pixelIndex / width,
                new Color(sliceIndex, biomeIndex, 0f, 1f));
            updated = true;
        }

        if (updated) biomeIndexMap.Apply(false, false);
    }

    public void UpdateTerrainDataTexturesForTile(int tileIndex)
    {
        UpdateTerrainDataTexturesForTiles(new[] { tileIndex });
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

    private void ApplyActiveBiomeTerrainMaterialSettings()
    {
        ApplyBiomeMaterialSettings();
    }

    public void ApplyBiomeMaterialSettings()
    {
        if (sharedMaterial == null) return;

        if (biomeIndexMap != null)
        {
            sharedMaterial.SetTexture("_BiomeIndexMap", biomeIndexMap);
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
        sharedMaterial.SetFloat("_EnableSeasonalSnow", enableSeasonalSnow ? 1f : 0f);
        sharedMaterial.SetFloat("_EnableWetnessVisuals", enableWetnessVisuals ? 1f : 0f);
        sharedMaterial.SetFloat("_EnableFreezeVisuals", enableFreezeVisuals ? 1f : 0f);
        sharedMaterial.SetFloat("_EnableSeasonalColorVariation", enableSeasonalColorVariation ? 1f : 0f);
        sharedMaterial.SetFloat("_EnableTerrainHighlights", enableTerrainHighlights ? 1f : 0f);
        sharedMaterial.SetFloat("_WetAlbedoDarkenMaximum", wetAlbedoDarkenMaximum);
        sharedMaterial.SetFloat("_SeasonalColorStrength", seasonalColorStrength);
        sharedMaterial.SetFloat("_ForceRawTerrainAlbedo", forceRawTerrainAlbedo ? 1f : 0f);
        BindCampaignLighting(sharedMaterial);
        if (!enableTerrainFogVisuals) sharedMaterial.SetFloat("_EnableFog", 0f);
        if (!enableMapModeOverlay) sharedMaterial.SetFloat("_EnableMapMode", 0f);
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
        float cliffSlices = hasValidCliffArrays ? Mathf.Max(1, cliffAlbedoArray.depth) : 0f;
        sharedMaterial.SetFloat("_CliffSliceCount", cliffSlices);

        // Normal sampling and biome blending parameters
        sharedMaterial.SetFloat("_BiomeNormalStrength", biomeNormalStrength);
        sharedMaterial.SetFloat("_BiomeBlendRadius", biomeBlendRadius);

        // Triplanar parameters
        sharedMaterial.SetFloat("_TriTiling", triplanarTiling);
        sharedMaterial.SetFloat("_TriBlend", triplanarBlend);
        sharedMaterial.SetFloat("_UseTriplanar", useTriplanar ? 1f : 0f);
        // Slice-to-biome reverse map for per-biome dynamic parameter lookup
        if (sliceToBiomeMap != null)
        {
            sharedMaterial.SetTexture("_SliceToBiomeMap", sliceToBiomeMap);
        }

        // Provide biome count and total slice count for shader UV-based lookups.
        // _BiomeCount = number of biomes (indexes into _BiomeParams[] / _BiomeEmissiveMapTex).
        // _TotalSlices = number of texture array slices (indexes into _SliceToBiomeMap).
        // These differ when multiple biomes share the same surface family.
        int biomeCount = (biomeParamsArray != null) ? biomeParamsArray.Length : 0;
        sharedMaterial.SetFloat("_BiomeCount", (float)biomeCount);
        int totalSlices = (biomeAlbedoArray != null) ? biomeAlbedoArray.depth : 1;
        sharedMaterial.SetFloat("_TotalSlices", (float)totalSlices);

        ApplyIceSurfaceSettingsToMaterial(sharedMaterial);
        ApplyIceSurfaceSettingsToMaterial(waterMaterial);
    }

    private void BindCampaignLighting(Material material)
    {
        if (material == null) return;

        Light sun = campaignDirectionalLight != null && campaignDirectionalLight.isActiveAndEnabled
            ? campaignDirectionalLight
            : RenderSettings.sun;
        if (sun == null || sun.type != LightType.Directional || !sun.isActiveAndEnabled)
        {
            float bestIntensity = -1f;
            foreach (var candidate in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            {
                if (candidate == null || candidate.type != LightType.Directional || !candidate.isActiveAndEnabled)
                    continue;
                if (candidate.intensity <= bestIntensity) continue;
                sun = candidate;
                bestIntensity = candidate.intensity;
            }
        }

        // Direction is surface-to-light. With no sun, a straight-up neutral light leaves
        // authored albedo intact and can never produce the old white-terrain failure.
        Vector3 direction = sun != null ? -sun.transform.forward : Vector3.up;
        Color color = sun != null ? sun.color : Color.white;
        material.SetVector("_CampaignLightDirectionWS", new Vector4(direction.x, direction.y, direction.z, 0f));
        material.SetColor("_CampaignLightColor", color);
        material.SetFloat("_CampaignAmbientFloor", campaignAmbientFloor);
        material.SetFloat("_CampaignDirectionalStrength", campaignDirectionalStrength);
        material.SetFloat("_CampaignAOStrength", campaignAOStrength);

        if (!terrainLightingAuditLogged)
        {
            terrainLightingAuditLogged = true;
            Debug.Log($"[TerrainLightingAudit]\nrawSurface=Debug1\nsubstrate=Debug2\n" +
                      $"finalUnlit=Debug3\nsimpleCampaignLit=Debug4\nproductionMode=SimpleCampaign\n" +
                      $"sun={(sun != null ? sun.name : "neutral-fallback")}", this);
        }
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

        if (material.HasProperty("_EnableFreezeVisuals"))
            material.SetFloat("_EnableFreezeVisuals", enableFreezeVisuals ? 1f : 0f);

        float freezeProgress = 0f;
        if (planetGenerator != null && ClimateManager.Instance != null)
            freezeProgress = ClimateManager.Instance.GetFreezeProgressForPlanet(planetGenerator.planetIndex);
        material.SetFloat("_FreezeProgress", enableFreezeVisuals ? freezeProgress : 0f);
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
                           "Expected: _BiomeIndexMap, _BiomeAlbedoArray, _BiomeNormalArray, _BiomeMaskArray, _BiomeCount. " +
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
                chunk.SetTerrainVisible(currentViewLayer != GameManager.PlanetLayerType.Orbit);

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
                chunk.SetTerrainVisible(currentViewLayer != GameManager.PlanetLayerType.Orbit);

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
            terrainOverlayGPU.OnMapModeOverlayChanged -= ApplyOverlayTexturesToMaterial;
            terrainOverlayGPU.OnMapModeOverlayChanged += ApplyOverlayTexturesToMaterial;
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
    /// Binds the separate fog mask and the single reusable campaign thematic overlay.
    /// </summary>
    private void ApplyOverlayTexturesToMaterial()
    {
        if (sharedMaterial == null || terrainOverlayGPU == null) return;

        // NOTE: These properties don't exist in the current shader graph - they're set for future compatibility
        var fogMask = terrainOverlayGPU.GetFogMaskTexture();
        if (fogMask != null)
        {
            sharedMaterial.SetTexture("_FogMask", fogMask);
            sharedMaterial.SetFloat("_EnableFog", enableTerrainFogVisuals && terrainOverlayGPU.EnableFogOverlay ? 1f : 0f);
        }

        var mapModeTex = terrainOverlayGPU.GetMapModeOverlayTexture();
        if (mapModeTex != null)
        {
            sharedMaterial.SetTexture("_MapModeOverlay", mapModeTex);
            sharedMaterial.SetFloat("_EnableMapMode", enableMapModeOverlay && terrainOverlayGPU.IsMapModeOverlayActive ? 1f : 0f);
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
    /// Creates the WorldPicker collider by combining the generated terrain chunk meshes.
    /// Picking therefore uses the exact visible stepped geometry at every camera angle.
    /// </summary>
    private void CreatePickingCollider()
    {
        if (pickingCollider != null)
            DestroyImmediate(pickingCollider.gameObject);

        GameObject colliderObj = new GameObject("ChunkMapCollider");
        colliderObj.transform.SetParent(transform, false);
        colliderObj.transform.localPosition = Vector3.zero;
        colliderObj.transform.localRotation = Quaternion.identity;

        // Reuse the generated chunk meshes so visible terrain and picking share the
        // exact same tops, bevels, walls, UVs, and categorical heights.
        var combines = new List<CombineInstance>(chunksX * chunksZ);
        for (int x = 0; x < chunksX; x++)
        for (int z = 0; z < chunksZ; z++)
        {
            HexMapChunk chunk = chunks[x, z];
            if (chunk == null || chunk.GeneratedMesh == null) continue;
            combines.Add(new CombineInstance
            {
                mesh = chunk.GeneratedMesh,
                transform = transform.worldToLocalMatrix * chunk.transform.localToWorldMatrix
            });
        }
        Mesh pickMesh = new Mesh { name = "PickingMesh_SteppedExact", indexFormat = IndexFormat.UInt32 };
        pickMesh.CombineMeshes(combines.ToArray(), true, true, false);
        const string pickingMode = "SteppedExact";

        MeshFilter mf = colliderObj.AddComponent<MeshFilter>();
        mf.sharedMesh = pickMesh;
        MeshRenderer mr = colliderObj.AddComponent<MeshRenderer>();
        mr.enabled = false;
        var meshCollider = colliderObj.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = pickMesh;
        pickingCollider = meshCollider;
        pickingCollider.enabled = currentViewLayer != GameManager.PlanetLayerType.Orbit;

        int terrainLayer = LayerMask.NameToLayer("Terrain");
        colliderObj.layer = terrainLayer >= 0 ? terrainLayer : 0;
        int curvedTopVerticesPerTile = 1 + 3 * TopSubdivision * (TopSubdivision + 1);
        Debug.Log($"[HexMapChunkManager] Created {pickingMode} picking collider: " +
                  $"oldTopVertsPerTile=7 newTopVertsPerTile={curvedTopVerticesPerTile} " +
                  $"totalMeshVertices={pickMesh.vertexCount} pickingMeshVertices={pickMesh.vertexCount}");
        LogTerrainHeightSync(pickingMode);
    }



    private void LogTerrainHeightSync(string pickingMode)
    {
        float sea = planetGenerator != null ? planetGenerator.SeaLevelWorldY : 0f;
        float water = GetOceanWaterSurfaceY();
        float flat = sea + flatHeightAboveSea;
        float hillNominal = sea + hillHeightAboveSea;
        float mountain = sea + mountainHeightAboveSea;
        float hillSafetyMinimum = flat + minimumFlatToHillStep;
        float hillSafetyMaximum = mountain - minimumHillToMountainStep;
        float configuredVariation = enableHillHeightVariation ? hillHeightVariation : 0f;
        float hillMinimum = Mathf.Max(hillNominal - configuredVariation, hillSafetyMinimum);
        float hillMaximum = Mathf.Min(hillNominal + configuredVariation, hillSafetyMaximum);
        float ocean = sea - oceanFloorDepthBelowSea;
        float abyssal = sea - abyssalDepthBelowSea;
        float trench = sea - trenchDepthBelowSea;
        Debug.Log($"[TerrainHeightSync]\nMode=SteppedHex\nSea={sea:F3}\nWater={water:F3}\nFlat={flat:F3}\nHillNominal={hillNominal:F3}\nHillAllowedRange={hillMinimum:F3}..{hillMaximum:F3}\nMountain={mountain:F3}\nOceanFloor={ocean:F3}\nAbyssal={abyssal:F3}\nTrench={trench:F3}\nPicking={pickingMode}");

        if (flat <= water || hillSafetyMinimum <= flat || hillSafetyMaximum >= mountain || hillMinimum > hillMaximum || ocean >= water || abyssal >= ocean || trench >= abyssal)
            Debug.LogWarning($"[TerrainHeightSync] Invalid stepped terrain/water ordering. Mode=SteppedHex, Picking={pickingMode}");
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
            waterPickingCollider.enabled = currentViewLayer == GameManager.PlanetLayerType.Surface;
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
            orbitPickingCollider.enabled = currentViewLayer == GameManager.PlanetLayerType.Orbit;
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
        mr.enabled = currentViewLayer == GameManager.PlanetLayerType.Surface;
    }

    /// <summary>
    /// Build a flat, densely subdivided mesh with per-vertex UVs (0..1) matching
    /// the terrain grid density.  Vertices span (-halfW..halfW, 0, -halfH..halfH).
    /// This is the same subdivision the terrain/picking mesh uses, so the GPU
    /// interpolation of UVs within each small triangle is accurate at any camera angle.
    /// </summary>
    private Mesh BuildFlatSubdividedMesh(string meshName, float halfW, float halfH)
    {
        int subX = Mathf.Min(chunksX * 32, 512);
        int subZ = Mathf.Min(chunksZ * 32, 256);
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

            float waterWorldY = GetTileWaterSurfaceY(tileIdx, td);

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
                            nbrWaterY = GetTileWaterSurfaceY(nbrIdx, nbrTd);
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
        mr.enabled = currentViewLayer == GameManager.PlanetLayerType.Surface;
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
        if (waterMaterial == null || bakeResult.lut == null || bakeResult.lut.Length == 0) { DestroyRiverSurface(); Debug.LogWarning("[HexMapChunkManager][SDF] Skipped: missing waterMaterial or LUT"); yield break; }

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

            Vector4 result = tileIndex >= 0 && planetGenerator.data.TryGetValue(tileIndex, out var tile)
                ? GetWaterFreezeVertexData(tile, tileIndex)
                : Vector4.zero;
            float distance = wType == 1 && distLake != null ? distLake[idx] : distRiver[idx];
            float halfWidth = wType == 1 ? isoLake : isoRiver;
            // W is cached water depth/shore distance: zero at the SDF bank and one at
            // the center of a sufficiently broad body. It costs nothing per frame.
            result.w = 1f - Mathf.Clamp01(distance / Mathf.Max(0.001f, halfWidth));
            return result;
        }

        // Resolve a water vertex from its owning tile and the rendered terrain contract.
        float OwnerWaterYAt(int gx, int gy, int wt)
        {
            int ci = gy * wPts + gx;
            int tIdx = (wt == 1 && ownerLake != null) ? ownerLake[ci]
                     : (ownerRiver != null) ? ownerRiver[ci] : -1;
            if (tIdx >= 0 && planetGenerator.data.TryGetValue(tIdx, out var t)
                && (t.waterType == TileWaterType.River || t.waterType == TileWaterType.Lake))
                return GetTileWaterSurfaceY(tIdx, t, riverSurfaceLift);
            // Ownership fields are propagated from valid seeds. If a boundary point
            // has no owner, search nearby valid water owners.
            for (int radius = 1; radius <= 3; radius++)
            for (int oy = -radius; oy <= radius; oy++)
            for (int ox = -radius; ox <= radius; ox++)
            {
                int nx = Mathf.Clamp(gx + ox, 0, wCells);
                int ny = Mathf.Clamp(gy + oy, 0, hCells);
                int ni = ny * wPts + nx;
                int nearTile = (wt == 1 && ownerLake != null) ? ownerLake[ni]
                    : (ownerRiver != null ? ownerRiver[ni] : -1);
                if (nearTile >= 0 && planetGenerator.data.TryGetValue(nearTile, out var nearWater) &&
                    (nearWater.waterType == TileWaterType.River || nearWater.waterType == TileWaterType.Lake))
                    return GetTileWaterSurfaceY(nearTile, nearWater, riverSurfaceLift);
            }
            return GetOceanWaterSurfaceY(riverSurfaceLift);
        }

        float SampleWaterY(float u, float v)
        {
            u = Mathf.Repeat(u, 1f);
            v = Mathf.Clamp01(v);
            int waterType = ClassifyWaterAt(u, v);
            if (waterType == 2)
                return GetOceanWaterSurfaceY(riverSurfaceLift);
            int nearestX = Mathf.Clamp(Mathf.RoundToInt(u * wCells), 0, wCells);
            int nearestY = Mathf.Clamp(Mathf.RoundToInt(v * hCells), 0, hCells);
            return OwnerWaterYAt(nearestX, nearestY, waterType);
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
            Debug.Log($"[WaterBuild] OceanMode={(enableOceanPlane ? "Plane" : (continuousWaterIncludesOcean ? "SDF" : "Tile"))} InlandMode=SDF InlandVerts={verts.Count} InlandTris={tris.Count / 3} RiverSeeds={seedRiverCount} LakeSeeds={seedLakeCount} OceanSeeds={seedOceanCount} extruded={extrudeInlandWaterToVolume} cells={wCells}x{hCells}");

        // Ensure ghosts are updated immediately after rebuild.
        if (enableWrap)
            UpdateGlobalWaterGhostPositions(_riverSurfaceObj.transform.localPosition.x);

        ApplyViewLayer(currentViewLayer);
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
            mr.enabled = currentViewLayer == GameManager.PlanetLayerType.Surface;
        }
        else
        {
            if (_riverSurfaceObj.name != objName) _riverSurfaceObj.name = objName;
            var mr = _riverSurfaceObj.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = waterMaterial;
                mr.enabled = currentViewLayer == GameManager.PlanetLayerType.Surface;
            }
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

    private GameObject EnsureGhostMeshObject(GameObject source, ref GameObject ghostObj, string ghostName, Transform parent, int layer)
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
            dstMR.enabled = currentViewLayer == GameManager.PlanetLayerType.Surface;
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
            mr.enabled = currentViewLayer == GameManager.PlanetLayerType.Surface;
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

        ApplyViewLayer(currentViewLayer);
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
        mr.enabled = currentViewLayer == GameManager.PlanetLayerType.Surface;
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
        ApplyViewLayer(currentViewLayer);

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
                ghostMR.enabled = currentViewLayer != GameManager.PlanetLayerType.Orbit;
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
        ApplyBiomeMaterialSettings();
        if (season == Season.Winter && !enableSeasonMasks)
            enableSeasonMasks = true;
        UpdateSeasonMasksBatched(season, chunksPerBatch);

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
        float visualProgress = enableFreezeVisuals ? progress : 0f;
        sharedMaterial.SetFloat("_FreezeProgress", visualProgress);
        if (waterMaterial != null)
            waterMaterial.SetFloat("_FreezeProgress", visualProgress);
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
            UpdateTerrainDataTexturesForTiles(changedTiles);

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

        if (targetChanged && newTarget > 0f && !enableSeasonMasks)
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
        UpdateTerrainDataTexturesForTile(tileIndex);

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
    /// Compatibility entry point for callers that need to refresh baked terrain after tile data changes.
    /// </summary>
    public void RebakeBakedTerrainForTile(int tileIndex)
    {
        MarkTileDirty(tileIndex);
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
        UpdateTerrainDataTexturesForTiles(tileList);

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

        ApplyActiveBiomeTerrainMaterialSettings();
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
            DestroyImmediate(sharedMaterial);
            sharedMaterial = null;
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
        if (biomeSurfaceMapTexture != null) { UnityEngine.Object.DestroyImmediate(biomeSurfaceMapTexture); biomeSurfaceMapTexture = null; }
        if (biomeEmissiveMapTexture != null) { UnityEngine.Object.DestroyImmediate(biomeEmissiveMapTexture); biomeEmissiveMapTexture = null; }
        if (lutTexture != null) { UnityEngine.Object.DestroyImmediate(lutTexture); lutTexture = null; }
        if (sliceToBiomeMap != null) { UnityEngine.Object.DestroyImmediate(sliceToBiomeMap); sliceToBiomeMap = null; }
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
