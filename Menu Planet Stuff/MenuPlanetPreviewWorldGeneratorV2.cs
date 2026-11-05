using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

[System.Flags]
public enum PreviewWorldRebuildScope
{
    None = 0,
    Tectonics = 1,
    Climate = 2,
    Hydrology = 4,
    Biomes = 8,
    All = 15
}

public struct MenuPlanetPreviewWorldInputs
{
    public float seed;
    public float landScale;
    public float landThreshold;
    public float elevation;
    public float elevationNoiseStrength;
    public float elevationTemperatureImpact;
    public float temperature;
    public float moisture;
    public bool enableIceCaps;

    public float climateNoiseStrength;
    public float coastWetnessStrength;
    public float continentalDrynessStrength;
    public float continentalTemperatureStrength;
    public float riparianWetnessStrength;

    public float biomeProvinceStrength;
    public float biomeCompetitionSharpness;

    public int landPresetIndex;
    public int terrainRoughnessPresetIndex;
    public int waterwaysPreset;
}

public class MenuPlanetPreviewWorldGeneratorV2 : MonoBehaviour
{
    [SerializeField] private int mapWidth = 1024;
    [SerializeField] private int mapHeight = 512;
    [SerializeField] private float regenerationDelay = 0.15f;

    [SerializeField] private int topologyWidth = 256;
    [SerializeField] private int topologyHeight = 128;
        [Header("Coastline Sculpting")]
    [Tooltip("How many topology cells inland/oceanward FastNoiseLite may significantly reshape the coastline. Higher values let noise carve broader bays, peninsulas, and coastal irregularity.")]
    [SerializeField, Range(1f, 50f)] private float coastlineDeformationWidthCells = 10f;
    [Tooltip("Large-scale FastNoiseLite authority over the coastline. Higher values let noise meaningfully push coastlines inland or outward.")]
    [SerializeField, Range(0f, 5f)] private float coastlineWarpStrength = 0.95f;
    [Tooltip("Medium-scale coastline sculpting. Controls bays, coastal shoulders, peninsulas, and major coastline irregularity between macro shape and fine shoreline detail.")]
    [SerializeField, Range(0f, 5f)] private float coastlineMidNoiseStrength = 0.65f;
    [Tooltip("Fine-scale FastNoiseLite coastal roughness. This adds smaller shoreline detail, but should stay lower than macro warp strength.")]
    [SerializeField, Range(0f, 5f)] private float coastlineEdgeNoiseStrength = 0.28f;
    [Tooltip("Soft threshold width used when turning the coastline signal into the final land mask.")]
    [SerializeField, Range(0.001f, 1f)] private float coastlineSoftness = 0.045f;
    [Tooltip("Shifts final coastline threshold. Positive values reduce land slightly; negative values expand land slightly. Leave near 0 unless final FastNoise coastline consistently adds/removes too much land.")]
    [SerializeField, Range(-0.75f, 0.75f)] private float coastlineThresholdBias = 0f;
    [SerializeField] private bool logWorldGenerationDiagnostics = true;

    [Header("Temporary Height Auto-Debug")]
    [SerializeField] private bool logHeightAutoDebugAfterDispatch = true;
    [SerializeField] private bool logHeightAttributionDebug = true;
    [SerializeField, Range(0f, 2f)] private float debugTerrainElevationStrength = 1.0f;
    [SerializeField, Range(0f, 1f)] private float debugHillStrength = 0.08f;
    [SerializeField, Range(0f, 1f)] private float debugMountainStrength = 0.18f;
    [SerializeField, Range(0f, 1f)] private float debugOceanDepth = 0.01f;


    [Header("GPU Coastline")]
    [SerializeField] private ComputeShader menuPlanetPreviewCoastlineCompute;

    [Header("GPU Height")]
    [SerializeField] private bool useGpuHeightPreview = true;
    [SerializeField] private ComputeShader menuPlanetPreviewHeightCompute;

    [Header("GPU Hydrology")]
    [SerializeField] private bool useGpuHydrologyPreview = true;
    [SerializeField] private ComputeShader menuPlanetPreviewHydrologyCompute;
    [Tooltip("Hydrology analysis grid = map resolution / divisor. 4 => 512x256 for a 2048x1024 map (baseline).")]
    [SerializeField, Range(1, 8)] private int gpuHydrologyAnalysisDivisor = 4;
    [Tooltip("Upper bound on drainage-fill passes. Relaxation stops early on the GPU once no cell changes.")]
    [SerializeField, Range(8, 4096)] private int gpuDrainageFillIterations = 768;
    [Tooltip("Upper bound on flow-accumulation passes. Relaxation stops early on the GPU once no cell changes.")]
    [SerializeField, Range(8, 4096)] private int gpuFlowAccumulationIterations = 768;
    [Tooltip("Raises the iteration caps to at least analysisWidth + analysisHeight so large continents can fully converge.")]
    [SerializeField] private bool gpuHydrologyAutoIterationBudget = true;
    [Tooltip("0..1 weight of a coherent preference field that bends rivers between strictly-downhill neighbours.")]
    [SerializeField, Range(0f, 1f)] private float gpuHydroRoutingJitter = 0.35f;
    [Tooltip("Amplitude (pixels) of the coherent multi-scale shoreline warp applied to lakes.")]
    [SerializeField, Range(0f, 6f)] private float gpuLakeShorelineWarpPixels = 3.5f;
    [Tooltip("Amplitude (pixels) of coherent bank wander applied to river ribbons at full resolution.")]
    [SerializeField, Range(0f, 4f)] private float gpuRiverMeanderPixels = 1.2f;
    [Tooltip("Minimum connected lake area (analysis cells) for a depression to render as a lake instead of wetland.")]
    [SerializeField, Range(1, 64)] private int gpuLakeMinAreaCells = 6;
    [Tooltip("Per-cell inflow loss across filled basins in fully arid climates (scaled by aridity^2). Lets enclosed basins become terminal lakes.")]
    [SerializeField, Range(0f, 0.3f)] private float gpuLakeEvaporationLoss = 0.12f;
    [Tooltip("Lowers lake surfaces below their spill level in arid climates (height units at full aridity).")]
    [SerializeField, Range(0f, 0.05f)] private float gpuTerminalLakeAridDrop = 0.012f;
    [Tooltip("Debug only: forces a GPU sync after each hydrology phase to log timings and convergence pass counts.")]
    [SerializeField] private bool logHydrologyPhaseDiagnostics = false;
    [Header("Terrain Roughness Type Elevation Multipliers")]
    [SerializeField, Range(0.2f, 2.0f)] private float flatElevationMultiplier = 0.8f;
    [SerializeField, Range(0.2f, 2.0f)] private float smoothElevationMultiplier = 0.9f;
    [SerializeField, Range(0.2f, 2.0f)] private float standardElevationMultiplier = 1.0f;
    [SerializeField, Range(0.2f, 2.0f)] private float mountainousElevationMultiplier = 1.15f;
    [SerializeField, Range(0.2f, 2.0f)] private float alpineElevationMultiplier = 1.25f;

    [Header("Debug Height")]
    [Tooltip("When enabled, the height compute writes debug values into the height RT (raw/bias/mask stages).")]
    [SerializeField] private bool debugHeightInspect = false;
    [Tooltip("When debug inspection is enabled, raw/bias values are multiplied by this remap for visibility.")]
    [SerializeField, Range(1f, 1000f)] private float debugHeightRemap = 100f;

    [Header("Experimental Signed Basin Hydrology")]
    [SerializeField] private bool useExperimentalSignedTerrain = false;
    [SerializeField] private bool debugForceSignedHeightSentinel = false;

    [System.Serializable]
    private struct ExperimentalTerrainProfile
    {
        public string name;
    public float heightBias;
    public float upwardReliefStrength;
    public float downwardBasinStrength;
    public float valleyCutStrength;
    public float broadNoiseAmplitude;
    public float midNoiseAmplitude;
    public float fineNoiseAmplitude;
    public float broadNoiseScale;
    public float midNoiseScale;
    public float fineNoiseScale;
    public float ridgeStrength;
    public float hillThreshold;
    public float mountainThreshold;
    public float basinCandidateBias;

    // --- New hydrology overrides for this terrain preset ---
    [Header("Hydrology")]
    [Range(0f, 1f)] public float lakeBasinMinDepth;
    [Range(0f, 1f)] public float lakeBasinFullDepth;
    [Range(0f, 10f)]   public float riverAccumulationThresholdSparse;
    [Range(0f, 10f)]   public float riverAccumulationThresholdStandard;
    [Range(0f, 10f)]   public float riverAccumulationThresholdAbundant;
    [Range(0f, 10f)] public float inlandBasinStrength;
    [Range(0f, 0.05f)] public float flowMinDownhillDelta;
[Range(0f, 2f)]    public float flowAccumulationCompression;
[Range(0f, 1f)]  public float macroReliefStrength;
[Range(0.25f, 4f)] public float macroReliefScale;
[Range(0.25f, 4f)] public float inlandBasinScale;
[Range(0f, 1f)]  public float watershedRidgeStrength;

[Range(1f, 8f)]  public float riverMinWidthPixels;
[Range(1f, 12f)] public float riverMaxWidthPixels;
    }

    [SerializeField] private ExperimentalTerrainProfile[] experimentalTerrainProfiles =
    {
        new ExperimentalTerrainProfile { name = "Flat", heightBias = -0.03f, upwardReliefStrength = 0.60f, downwardBasinStrength = 0.16f, valleyCutStrength = 0.05f, broadNoiseAmplitude = 0.14f, midNoiseAmplitude = 0.035f, fineNoiseAmplitude = 0.008f, broadNoiseScale = 0.55f, midNoiseScale = 1.10f, fineNoiseScale = 2.00f, ridgeStrength = 0.005f, hillThreshold = 0.04f, mountainThreshold = 0.22f, basinCandidateBias = 1.25f, lakeBasinMinDepth = 0.012f, lakeBasinFullDepth = 0.045f, riverAccumulationThresholdSparse = 0.65f, riverAccumulationThresholdStandard = 0.52f, riverAccumulationThresholdAbundant = 0.38f, inlandBasinStrength = 0.065f, flowMinDownhillDelta = 0.002f, flowAccumulationCompression = 0.08f, macroReliefStrength = 0.05f, macroReliefScale = 0.75f, inlandBasinScale = 0.75f, watershedRidgeStrength = 0.015f, riverMinWidthPixels = 1.5f, riverMaxWidthPixels = 5f },
        new ExperimentalTerrainProfile { name = "Smooth", heightBias = -0.015f, upwardReliefStrength = 0.85f, downwardBasinStrength = 0.14f, valleyCutStrength = 0.07f, broadNoiseAmplitude = 0.16f, midNoiseAmplitude = 0.055f, fineNoiseAmplitude = 0.012f, broadNoiseScale = 0.70f, midNoiseScale = 1.30f, fineNoiseScale = 2.30f, ridgeStrength = 0.015f, hillThreshold = 0.05f, mountainThreshold = 0.20f, basinCandidateBias = 1.15f, lakeBasinMinDepth = 0.015f, lakeBasinFullDepth = 0.055f, riverAccumulationThresholdSparse = 0.62f, riverAccumulationThresholdStandard = 0.48f, riverAccumulationThresholdAbundant = 0.34f, inlandBasinStrength = 0.055f, flowMinDownhillDelta = 0.003f, flowAccumulationCompression = 0.10f, macroReliefStrength = 0.06f, macroReliefScale = 0.85f, inlandBasinScale = 0.85f, watershedRidgeStrength = 0.02f, riverMinWidthPixels = 1.75f, riverMaxWidthPixels = 5.5f },
        new ExperimentalTerrainProfile { name = "Standard", heightBias = 0.00f, upwardReliefStrength = 1.10f, downwardBasinStrength = 0.13f, valleyCutStrength = 0.10f, broadNoiseAmplitude = 0.20f, midNoiseAmplitude = 0.09f, fineNoiseAmplitude = 0.02f, broadNoiseScale = 0.90f, midNoiseScale = 1.70f, fineNoiseScale = 3.00f, ridgeStrength = 0.05f, hillThreshold = 0.06f, mountainThreshold = 0.18f, basinCandidateBias = 1.00f, lakeBasinMinDepth = 0.018f, lakeBasinFullDepth = 0.065f, riverAccumulationThresholdSparse = 0.58f, riverAccumulationThresholdStandard = 0.45f, riverAccumulationThresholdAbundant = 0.32f, inlandBasinStrength = 0.045f, flowMinDownhillDelta = 0.004f, flowAccumulationCompression = 0.12f, macroReliefStrength = 0.07f, macroReliefScale = 1.25f, inlandBasinScale = 1.0f, watershedRidgeStrength = 0.025f, riverMinWidthPixels = 2f, riverMaxWidthPixels = 6f },
        new ExperimentalTerrainProfile { name = "Mountainous", heightBias = 0.03f, upwardReliefStrength = 1.45f, downwardBasinStrength = 0.15f, valleyCutStrength = 0.16f, broadNoiseAmplitude = 0.24f, midNoiseAmplitude = 0.14f, fineNoiseAmplitude = 0.035f, broadNoiseScale = 1.10f, midNoiseScale = 2.20f, fineNoiseScale = 4.00f, ridgeStrength = 0.14f, hillThreshold = 0.07f, mountainThreshold = 0.16f, basinCandidateBias = 0.90f, lakeBasinMinDepth = 0.022f, lakeBasinFullDepth = 0.085f, riverAccumulationThresholdSparse = 0.52f, riverAccumulationThresholdStandard = 0.38f, riverAccumulationThresholdAbundant = 0.24f, inlandBasinStrength = 0.035f, flowMinDownhillDelta = 0.006f, flowAccumulationCompression = 0.16f, macroReliefStrength = 0.10f, macroReliefScale = 1.75f, inlandBasinScale = 1.25f, watershedRidgeStrength = 0.035f, riverMinWidthPixels = 2.5f, riverMaxWidthPixels = 7f },
        new ExperimentalTerrainProfile { name = "Alpine", heightBias = 0.05f, upwardReliefStrength = 1.80f, downwardBasinStrength = 0.18f, valleyCutStrength = 0.22f, broadNoiseAmplitude = 0.28f, midNoiseAmplitude = 0.20f, fineNoiseAmplitude = 0.05f, broadNoiseScale = 1.25f, midNoiseScale = 2.80f, fineNoiseScale = 5.00f, ridgeStrength = 0.25f, hillThreshold = 0.08f, mountainThreshold = 0.14f, basinCandidateBias = 0.75f, lakeBasinMinDepth = 0.028f, lakeBasinFullDepth = 0.12f, riverAccumulationThresholdSparse = 0.45f, riverAccumulationThresholdStandard = 0.32f, riverAccumulationThresholdAbundant = 0.18f, inlandBasinStrength = 0.025f, flowMinDownhillDelta = 0.008f, flowAccumulationCompression = 0.20f, macroReliefStrength = 0.14f, macroReliefScale = 1.75f, inlandBasinScale = 1.5f, watershedRidgeStrength = 0.05f, riverMinWidthPixels = 3f, riverMaxWidthPixels = 8f }
    };


    public Texture TectonicSurfaceTexture => gpuTectonicSurfaceTexture;
    public Texture TectonicBoundaryTexture => gpuTectonicBoundaryTexture;
    public Texture TectonicCrustTexture => gpuTectonicCrustTexture;
    public Texture ActiveHydrologyTexture => gpuHydrologyTexture;
    public Texture ActiveHydrologyDepthTexture => gpuHydrologyDepthTexture;
    public RenderTexture GpuHeightTexture => gpuHeightTexture;
    public RenderTexture GpuDisplacementTexture => gpuDisplacementTexture;
    public RenderTexture GpuLandMaskTexture => gpuLandMaskTexture;
    public bool UseExperimentalSignedTerrain => useExperimentalSignedTerrain;
    public event Action WorldTexturesUpdated;

    private struct TopologyCell { public bool isLand; public int groupId; }
    private enum LandmassKind { MajorContinent, LargeIsland, SmallIslandCluster }
    [Serializable] private struct LandmassBand { public int minCount,maxCount; public float minAreaFraction,maxAreaFraction; public int minLobes,maxLobes; }
    [Serializable] private struct PreviewLandPresetProfileV3 {
        public string name; public LandmassBand majorLandmasses, largeIslands, smallIslandClusters;
        public float minMajorSeedSeparationDegrees, minMajorOceanGapCells;
        public float compactnessBias, irregularityBias, elongationBias;
        public bool allowMajorLandmassMerging;
    }
    private struct LandmassLobe { public Vector2 center; public float radiusX; public float radiusY; public float rotationRadians; public float weight; }
    private struct LandmassPlan { public LandmassKind kind; public int targetCellCount,lobeCount,groupId; public float estimatedRadiusCells; public Vector2Int center; public Vector2 elongationDirection; public List<LandmassLobe> lobes; }
    private struct LandmassPlanBuildDiagnostics
    {
        public int requestedMajorCount;
        public int requestedLargeIslandCount;
        public int requestedSmallClusterCount;
        public int builtMajorCount;
        public int builtLargeIslandCount;
        public int builtSmallClusterCount;
        public int failedCenterPlacements;
        public int totalPlannedTargetCells;
    }

    private struct GenDiagnostics
    {
        public string preset;
        public float targetLand;
        public float actualLand;
        public float topologyCoverage;
        public int topologyLandCells;
        public int targetTopologyLandCells;
        public int groupCount;
        public float largestGroupShare;
    }

    [SerializeField] private PreviewLandPresetProfileV3[] presets = new PreviewLandPresetProfileV3[6]
    {
        new PreviewLandPresetProfileV3{name="Archipelago",majorLandmasses=new LandmassBand{minCount=0,maxCount=0,minAreaFraction=0,maxAreaFraction=0,minLobes=0,maxLobes=0},largeIslands=new LandmassBand{minCount=10,maxCount=18,minAreaFraction=0.004f,maxAreaFraction=0.018f,minLobes=1,maxLobes=3},smallIslandClusters=new LandmassBand{minCount=12,maxCount=30,minAreaFraction=0.0005f,maxAreaFraction=0.004f,minLobes=1,maxLobes=2},minMajorSeedSeparationDegrees=0f,minMajorOceanGapCells=1f,compactnessBias=0.30f,irregularityBias=0.90f,elongationBias=0.20f,allowMajorLandmassMerging=false},
        new PreviewLandPresetProfileV3{name="Islands",majorLandmasses=new LandmassBand{minCount=0,maxCount=0,minAreaFraction=0,maxAreaFraction=0,minLobes=0,maxLobes=0},largeIslands=new LandmassBand{minCount=5,maxCount=10,minAreaFraction=0.015f,maxAreaFraction=0.045f,minLobes=1,maxLobes=3},smallIslandClusters=new LandmassBand{minCount=8,maxCount=20,minAreaFraction=0.001f,maxAreaFraction=0.006f,minLobes=1,maxLobes=2},minMajorSeedSeparationDegrees=0f,minMajorOceanGapCells=1f,compactnessBias=0.42f,irregularityBias=0.74f,elongationBias=0.30f,allowMajorLandmassMerging=false},
        new PreviewLandPresetProfileV3{name="Standard",majorLandmasses=new LandmassBand{minCount=3,maxCount=5,minAreaFraction=0.06f,maxAreaFraction=0.12f,minLobes=2,maxLobes=5},largeIslands=new LandmassBand{minCount=3,maxCount=8,minAreaFraction=0.005f,maxAreaFraction=0.020f,minLobes=1,maxLobes=3},smallIslandClusters=new LandmassBand{minCount=4,maxCount=12,minAreaFraction=0.001f,maxAreaFraction=0.004f,minLobes=1,maxLobes=2},minMajorSeedSeparationDegrees=22f,minMajorOceanGapCells=1.5f,compactnessBias=0.58f,irregularityBias=0.66f,elongationBias=0.34f,allowMajorLandmassMerging=false},
        new PreviewLandPresetProfileV3{name="Large Continents",majorLandmasses=new LandmassBand{minCount=2,maxCount=3,minAreaFraction=0.14f,maxAreaFraction=0.26f,minLobes=3,maxLobes=6},largeIslands=new LandmassBand{minCount=2,maxCount=6,minAreaFraction=0.006f,maxAreaFraction=0.024f,minLobes=1,maxLobes=3},smallIslandClusters=new LandmassBand{minCount=3,maxCount=10,minAreaFraction=0.001f,maxAreaFraction=0.004f,minLobes=1,maxLobes=2},minMajorSeedSeparationDegrees=28f,minMajorOceanGapCells=2f,compactnessBias=0.58f,irregularityBias=0.60f,elongationBias=0.50f,allowMajorLandmassMerging=false},
        new PreviewLandPresetProfileV3{name="Pangaea",majorLandmasses=new LandmassBand{minCount=1,maxCount=1,minAreaFraction=0.40f,maxAreaFraction=0.62f,minLobes=5,maxLobes=10},largeIslands=new LandmassBand{minCount=0,maxCount=3,minAreaFraction=0.005f,maxAreaFraction=0.020f,minLobes=1,maxLobes=2},smallIslandClusters=new LandmassBand{minCount=0,maxCount=6,minAreaFraction=0.001f,maxAreaFraction=0.004f,minLobes=1,maxLobes=2},minMajorSeedSeparationDegrees=0f,minMajorOceanGapCells=0f,compactnessBias=0.48f,irregularityBias=0.72f,elongationBias=0.72f,allowMajorLandmassMerging=true},
        new PreviewLandPresetProfileV3{name="Terrestrial",majorLandmasses=new LandmassBand{minCount=2,maxCount=4,minAreaFraction=0.16f,maxAreaFraction=0.28f,minLobes=3,maxLobes=6},largeIslands=new LandmassBand{minCount=2,maxCount=6,minAreaFraction=0.006f,maxAreaFraction=0.026f,minLobes=1,maxLobes=3},smallIslandClusters=new LandmassBand{minCount=2,maxCount=8,minAreaFraction=0.001f,maxAreaFraction=0.004f,minLobes=1,maxLobes=2},minMajorSeedSeparationDegrees=24f,minMajorOceanGapCells=1.5f,compactnessBias=0.56f,irregularityBias=0.52f,elongationBias=0.44f,allowMajorLandmassMerging=false}
    };

    private MenuPlanetPreviewWorldInputs inputs;
    private PreviewWorldRebuildScope pending;
    private float scheduledAt = -1f;
    private int generationVersion;
    private int runningGenerationVersion;
    private Coroutine generationCoroutine;
    public bool IsGeneratingPreview { get; private set; }
    private float generationStartedAt;

    private float[] cachedTopologyShapeNoise;

    private int cachedNoiseSeed;
    private int cachedTopologyWidth;
    private int cachedTopologyHeight;
    private bool topologyShapeNoiseValid;
    private RenderTexture gpuTectonicSurfaceTexture;
    private RenderTexture gpuLandMaskTexture;
    private RenderTexture gpuTectonicBoundaryTexture;
    private RenderTexture gpuTectonicCrustTexture;
    private ComputeBuffer topoLandCoastDistanceBuffer;
    private ComputeBuffer topoOceanCoastDistanceBuffer;
    private ComputeBuffer topoSignedCoastDistanceBuffer;
    private int gpuCoastlineKernel = -1;
    private RenderTexture gpuHeightTexture;
    private RenderTexture gpuDisplacementTexture;
    private RenderTexture gpuHydrologyTexture;
    private RenderTexture gpuHydrologyDepthTexture, gpuDrainageFillA, gpuDrainageFillB, gpuFlowDirectionTexture, gpuFlowAccumA, gpuFlowAccumB, gpuCoarseHydrologyMaskTexture, gpuCoarseHydrologyDepthTexture;
    private RenderTexture gpuHydroTerrainTexture;
    private ComputeBuffer lakeLabelBufferA, lakeLabelBufferB, lakeAreaBuffer, hydroConvergenceBuffer, hydroIndirectArgsBuffer, hydroDebugStatsBuffer;
    private int hydroAnalysisWidth, hydroAnalysisHeight;
    private static readonly uint[] ConvergenceReset = { 0u, 0u, 1u, 0u };
    private const int HydroDebugStatCount = 16;
    private int heightDebugDispatchSerial;

    

    private int gpuHeightKernel = -1;
    private bool validateCacheInitialized;
    private int lastValidatedGpuDrainageFillIterations;
    private int lastValidatedGpuFlowAccumulationIterations;
    private float lastValidatedGpuHydroRoutingJitter;
    private float lastValidatedGpuLakeShorelineWarpPixels;
    private int lastValidatedGpuHydrologyAnalysisDivisor;
    private bool lastValidatedGpuHydrologyAutoIterationBudget;
    private float lastValidatedGpuRiverMeanderPixels;
    private int lastValidatedGpuLakeMinAreaCells;
    private float lastValidatedGpuLakeEvaporationLoss;
    private float lastValidatedGpuTerminalLakeAridDrop;
    private bool lastValidatedUseExperimentalSignedTerrain;
    private int lastValidatedExperimentalTerrainProfileHash;




    [ContextMenu("Log GPU Terrain/Hydrology Coverage Stats")]
    public void LogGpuCoverageStats()
    {
        if (gpuHeightTexture == null || gpuHydrologyTexture == null || gpuDisplacementTexture == null)
        {
            Debug.LogWarning("[MenuPlanetPreviewWorldGeneratorV2] GPU textures are not ready. Generate preview first.");
            return;
        }

        AsyncGPUReadback.Request(gpuHeightTexture, 0, TextureFormat.RGBAHalf, request =>
        {
            if (request.hasError)
            {
                Debug.LogWarning("[MenuPlanetPreviewWorldGeneratorV2] Height readback failed.");
                return;
            }

            var raw = request.GetData<ushort>();
            int pixelCount = gpuHeightTexture.width * gpuHeightTexture.height;
            if (raw == null || raw.Length < pixelCount * 4)
            {
                Debug.LogWarning("[MenuPlanetPreviewWorldGeneratorV2] Height readback size mismatch.");
                return;
            }

            float minHeight = float.MaxValue;
            float maxHeight = float.MinValue;
            double sumHeight = 0.0;
            float minHill = float.MaxValue;
            float maxHill = float.MinValue;
            float minMountain = float.MaxValue;
            float maxMountain = float.MinValue;
            float minBasinPotential = float.MaxValue;
            float maxBasinPotential = float.MinValue;
            int positiveHeightCount = 0;
            int negativeBasinCount = 0;
            int hillCount = 0;
            int mountainCount = 0;
            int basinPotentialCount = 0;

            for (int i = 0; i < pixelCount; i++)
            {
                int bi = i * 4;
                float finalHeight = Mathf.HalfToFloat(raw[bi + 0]);
                float hill = Mathf.HalfToFloat(raw[bi + 1]);
                float mountain = Mathf.HalfToFloat(raw[bi + 2]);
                float basin = Mathf.HalfToFloat(raw[bi + 3]);

                minHeight = Mathf.Min(minHeight, finalHeight);
                maxHeight = Mathf.Max(maxHeight, finalHeight);
                sumHeight += finalHeight;
                minHill = Mathf.Min(minHill, hill);
                maxHill = Mathf.Max(maxHill, hill);
                minMountain = Mathf.Min(minMountain, mountain);
                maxMountain = Mathf.Max(maxMountain, mountain);
                minBasinPotential = Mathf.Min(minBasinPotential, basin);
                maxBasinPotential = Mathf.Max(maxBasinPotential, basin);
                if (finalHeight > 0.02f) positiveHeightCount++;
                if (finalHeight < -0.02f) negativeBasinCount++;
                if (hill > 0.05f) hillCount++;
                if (mountain > 0.05f) mountainCount++;
                if (basin > 0.05f) basinPotentialCount++;
            }

            AsyncGPUReadback.Request(gpuHydrologyTexture, 0, TextureFormat.RGBAHalf, hydroReq =>
            {
                if (hydroReq.hasError)
                {
                    Debug.LogWarning("[MenuPlanetPreviewWorldGeneratorV2] Hydrology readback failed.");
                    return;
                }

                var rawH = hydroReq.GetData<ushort>();
                int hydroCount = gpuHydrologyTexture.width * gpuHydrologyTexture.height;
                if (rawH == null || rawH.Length < hydroCount * 4)
                {
                    Debug.LogWarning("[MenuPlanetPreviewWorldGeneratorV2] Hydrology readback size mismatch.");
                    return;
                }

                int lakeCount = 0;
                int riverCount = 0;
                int wetlandCount = 0;

                for (int i = 0; i < hydroCount; i++)
                {
                    int bi = i * 4;
                    float rx = Mathf.HalfToFloat(rawH[bi + 0]);
                    float ry = Mathf.HalfToFloat(rawH[bi + 1]);
                    float rz = Mathf.HalfToFloat(rawH[bi + 2]);
                    if (rx > 0.05f) riverCount++;
                    if (ry > 0.05f) lakeCount++;
                    if (rz > 0.05f) wetlandCount++;
                }

                float invHeight = 1f / pixelCount;
                float invHydro = 1f / hydroCount;
                float positiveCoverage = positiveHeightCount * invHeight * 100f;
                float negativeCoverage = negativeBasinCount * invHeight * 100f;
                float hillCoverage = hillCount * invHeight * 100f;
                float mountainCoverage = mountainCount * invHeight * 100f;
                float basinPotentialCoverage = basinPotentialCount * invHeight * 100f;
                float lakeCoverage = lakeCount * invHydro * 100f;
                float riverCoverage = riverCount * invHydro * 100f;
                float wetlandCoverage = wetlandCount * invHydro * 100f;
                float meanHeight = (float)(sumHeight * invHeight);
                float balanceCoverage = 100f - positiveCoverage - negativeCoverage;

                Debug.Log($"[MenuPlanetPreviewWorldGeneratorV2] GPU stats | SignedHeight min/max/mean: {minHeight:F4}/{maxHeight:F4}/{meanHeight:F4} | Positive={positiveCoverage:F2}% | Negative={negativeCoverage:F2}% | NearZero={balanceCoverage:F2}% | Hill={hillCoverage:F2}% | Mountain={mountainCoverage:F2}% | BasinPotential={basinPotentialCoverage:F2}% | Lake: {lakeCoverage:F2}% | River: {riverCoverage:F2}% | Wetland: {wetlandCoverage:F2}%");
                Debug.Log($"[MenuPlanetPreviewWorldGeneratorV2] GPU channel ranges | Hill(y) min/max: {minHill:F4}/{maxHill:F4} | Mountain(z) min/max: {minMountain:F4}/{maxMountain:F4} | BasinPotential(w) min/max: {minBasinPotential:F4}/{maxBasinPotential:F4}");
                Debug.Log($"[MenuPlanetPreviewWorldGeneratorV2] GPU interpretation hints | AlpineSignedHeightMaxCheck={(maxHeight < 0.04f ? "WARN: <0.04 (too weak)" : "OK")} | PositiveThreshold=0.02 | NegativeThreshold=-0.02 | HillThreshold=0.05 | MountainThreshold=0.05 | BasinPotentialThreshold=0.05");

                LogHydrologyNetworkStats(rawH, hydroCount);
            });
        });
    }

    // Debug only: land-relative water coverage from the full-res hydrology texture plus coarse network
    // statistics produced by the AnalyzeHydrologyDebug kernel (outlet validity, lake regions, widths).
    private void LogHydrologyNetworkStats(Unity.Collections.NativeArray<ushort> hydroRaw, int hydroCount)
    {
        if (gpuLandMaskTexture == null || !gpuLandMaskTexture.IsCreated() || gpuLandMaskTexture.width * gpuLandMaskTexture.height != hydroCount)
        {
            Debug.LogWarning("[MenuPlanetPreviewWorldGeneratorV2] Land mask not available; skipping land-relative hydrology stats.");
            return;
        }

        // Copy the hydrology samples we need before the readback buffer is recycled.
        int riverLand = 0, lakeLand = 0, wetLand = 0, surfaceWaterLand = 0;
        var riverPx = new bool[hydroCount]; var lakePx = new bool[hydroCount]; var wetPx = new bool[hydroCount];
        for (int i = 0; i < hydroCount; i++)
        {
            int bi = i * 4;
            riverPx[i] = Mathf.HalfToFloat(hydroRaw[bi + 0]) > 0.5f;
            lakePx[i] = Mathf.HalfToFloat(hydroRaw[bi + 1]) > 0.5f;
            wetPx[i] = Mathf.HalfToFloat(hydroRaw[bi + 2]) > 0.25f;
        }

        AsyncGPUReadback.Request(gpuLandMaskTexture, 0, TextureFormat.RFloat, landReq =>
        {
            if (landReq.hasError) { Debug.LogWarning("[MenuPlanetPreviewWorldGeneratorV2] Land mask readback failed."); return; }
            var land = landReq.GetData<float>();
            if (land.Length < hydroCount) return;
            int landPixels = 0;
            for (int i = 0; i < hydroCount; i++)
            {
                if (land[i] <= 0.5f) continue;
                landPixels++;
                if (riverPx[i]) riverLand++;
                if (lakePx[i]) lakeLand++;
                if (wetPx[i]) wetLand++;
                if (riverPx[i] || lakePx[i]) surfaceWaterLand++;
            }
            float inv = landPixels > 0 ? 100f / landPixels : 0f;
            Debug.Log($"[MenuPlanetPreviewWorldGeneratorV2] Hydrology land coverage | Land={landPixels * 100f / hydroCount:F2}% of map | River={riverLand * inv:F2}% of land | Lake={lakeLand * inv:F2}% of land | SurfaceWater={surfaceWaterLand * inv:F2}% of land | Wet/riparian={wetLand * inv:F2}% of land");

            if (menuPlanetPreviewHydrologyCompute == null || hydroDebugStatsBuffer == null || !hydroDebugStatsBuffer.IsValid() || hydroAnalysisWidth <= 0 || !HasHydrologyInputTextures())
                return;

            var cs = menuPlanetPreviewHydrologyCompute;
            int kAnalyze = cs.FindKernel("AnalyzeHydrologyDebug");
            hydroDebugStatsBuffer.SetData(new uint[HydroDebugStatCount]);
            BindHydrologyResources(cs, new[] { kAnalyze });
            cs.Dispatch(kAnalyze, Mathf.CeilToInt(hydroAnalysisWidth / 8f), Mathf.CeilToInt(hydroAnalysisHeight / 8f), 1);
            AsyncGPUReadback.Request(hydroDebugStatsBuffer, statsReq =>
            {
                if (statsReq.hasError) { Debug.LogWarning("[MenuPlanetPreviewWorldGeneratorV2] Hydrology debug stats readback failed."); return; }
                var s = statsReq.GetData<uint>();
                uint riverCells = s[0], riverNoOutlet = s[1], cycleRisk = s[2], lakeRegions = s[3], lakeCandidates = s[4], lakeCells = s[5];
                uint widthSum = s[6], widthMax = s[7], landCells = s[8], mouths = s[9], brokenDownstream = s[10], unresolved = s[11], landNoOutlet = s[12];
                float avgWidth = riverCells > 0 ? widthSum / 100f / riverCells : 0f;
                float landInv = landCells > 0 ? 100f / landCells : 0f;
                Debug.Log(
                    $"[MenuPlanetPreviewWorldGeneratorV2] Hydrology network ({hydroAnalysisWidth}x{hydroAnalysisHeight}) | LandCells={landCells} " +
                    $"| RiverCells={riverCells} ({riverCells * landInv:F2}% of land) | RiverMouths={mouths} " +
                    $"| RiverCellsWithoutOutlet={riverNoOutlet} | RiverCellsWithBrokenDownstream={brokenDownstream} " +
                    $"| AvgWidthPx={avgWidth:F2} MaxWidthPx={widthMax / 100f:F2} " +
                    $"| SignificantLakes={lakeRegions} LakeCells={lakeCells} (candidates {lakeCandidates}, {lakeCells * landInv:F2}% of land) " +
                    $"| UnresolvedFillCells={unresolved} LandCellsWithoutOutlet={landNoOutlet} DownhillViolations={cycleRisk}" +
                    (unresolved > 0 ? " | WARN: drainage fill hit its iteration budget; raise gpuDrainageFillIterations or enable auto budget" : "") +
                    (cycleRisk > 0 ? " | WARN: flow graph contains non-descending edges" : ""));
            });
        });
    }

    public void SetInputs(MenuPlanetPreviewWorldInputs v) => inputs = v;
    public void RefreshGpuHeightOnly(MenuPlanetPreviewWorldInputs updatedInputs)
    {
        inputs = updatedInputs;

        if (!HasHeightInputTextures())
        {
            Debug.LogWarning(
                "[MenuPlanetPreviewWorldGeneratorV2] Height refresh requested before tectonic textures were ready. " +
                "Scheduling full tectonics rebuild instead."
            );

            RequestRebuild(PreviewWorldRebuildScope.Tectonics, false);
            return;
        }

        DispatchGpuHeight();
    }

    public void RefreshGpuHydrologyOnly(MenuPlanetPreviewWorldInputs updatedInputs)
    {
        inputs = updatedInputs;

        if (!HasHydrologyInputTextures())
        {
            Debug.LogWarning("[MenuPlanetPreviewWorldGeneratorV2] Hydrology refresh requested before tectonic/height textures were ready. Scheduling full tectonics rebuild instead.");
            RequestRebuild(PreviewWorldRebuildScope.Tectonics, false);
            return;
        }

        DispatchGpuHydrology();
    }

    public bool HasGeneratedWorldTextures()
    {
        return HasHydrologyInputTextures()
            && gpuHydrologyTexture != null && gpuHydrologyTexture.IsCreated()
            && gpuHydrologyDepthTexture != null && gpuHydrologyDepthTexture.IsCreated();
    }

    private bool HasHydrologyInputTextures()
    {
        return gpuTectonicSurfaceTexture != null && gpuTectonicSurfaceTexture.IsCreated()
            && gpuHeightTexture != null && gpuHeightTexture.IsCreated()
            && gpuDisplacementTexture != null && gpuDisplacementTexture.IsCreated();
    }

    private bool HasHeightInputTextures()
    {
        return gpuTectonicSurfaceTexture != null && gpuTectonicSurfaceTexture.IsCreated()
            && gpuLandMaskTexture != null && gpuLandMaskTexture.IsCreated()
            && gpuTectonicCrustTexture != null && gpuTectonicCrustTexture.IsCreated();
    }
    public void RequestRebuild(PreviewWorldRebuildScope scope, bool immediate = false) {
        pending |= ExpandDependencies(scope);
        generationVersion++;
        scheduledAt = immediate ? Time.time : Time.time + regenerationDelay;
    }
    public void Release()
    {
        ReleaseGpuCoastlineResources();
        ReleaseGpuHeightTexture();
        ReleaseGpuDisplacementTexture();
        ReleaseGpuHydrologyResources();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying) return;
        if (!validateCacheInitialized)
        {
            CacheValidatedGpuHeightInputs();
            return;
        }

        bool gpuHeightChanged = GpuHeightSettingsChanged();
        bool gpuHydrologyChanged = GpuHydrologySettingsChanged();

        if (gpuHeightChanged)
        {
            DispatchGpuHeight();
            DispatchGpuHydrology();
            WorldTexturesUpdated?.Invoke();
        }
        else if (gpuHydrologyChanged)
        {
            DispatchGpuHydrology();
            WorldTexturesUpdated?.Invoke();
        }

        if (gpuHeightChanged || gpuHydrologyChanged)
        {
            CacheValidatedGpuHeightInputs();
        }
    }

    private bool GpuHeightSettingsChanged()
    {
        return useExperimentalSignedTerrain != lastValidatedUseExperimentalSignedTerrain ||
               ComputeExperimentalTerrainProfileHash() != lastValidatedExperimentalTerrainProfileHash;
    }


    private bool GpuHydrologySettingsChanged()
    {
        return gpuDrainageFillIterations != lastValidatedGpuDrainageFillIterations ||
               gpuFlowAccumulationIterations != lastValidatedGpuFlowAccumulationIterations ||
               !Mathf.Approximately(gpuHydroRoutingJitter, lastValidatedGpuHydroRoutingJitter) ||
               !Mathf.Approximately(gpuLakeShorelineWarpPixels, lastValidatedGpuLakeShorelineWarpPixels) ||
               gpuHydrologyAnalysisDivisor != lastValidatedGpuHydrologyAnalysisDivisor ||
               gpuHydrologyAutoIterationBudget != lastValidatedGpuHydrologyAutoIterationBudget ||
               !Mathf.Approximately(gpuRiverMeanderPixels, lastValidatedGpuRiverMeanderPixels) ||
               gpuLakeMinAreaCells != lastValidatedGpuLakeMinAreaCells ||
               !Mathf.Approximately(gpuLakeEvaporationLoss, lastValidatedGpuLakeEvaporationLoss) ||
               !Mathf.Approximately(gpuTerminalLakeAridDrop, lastValidatedGpuTerminalLakeAridDrop) ||
               ComputeExperimentalTerrainProfileHash() != lastValidatedExperimentalTerrainProfileHash;
    }

    private void CacheValidatedGpuHeightInputs()
    {
        lastValidatedGpuDrainageFillIterations = gpuDrainageFillIterations;
        lastValidatedGpuFlowAccumulationIterations = gpuFlowAccumulationIterations;
        lastValidatedGpuHydroRoutingJitter = gpuHydroRoutingJitter;
        lastValidatedGpuLakeShorelineWarpPixels = gpuLakeShorelineWarpPixels;
        lastValidatedGpuHydrologyAnalysisDivisor = gpuHydrologyAnalysisDivisor;
        lastValidatedGpuHydrologyAutoIterationBudget = gpuHydrologyAutoIterationBudget;
        lastValidatedGpuRiverMeanderPixels = gpuRiverMeanderPixels;
        lastValidatedGpuLakeMinAreaCells = gpuLakeMinAreaCells;
        lastValidatedGpuLakeEvaporationLoss = gpuLakeEvaporationLoss;
        lastValidatedGpuTerminalLakeAridDrop = gpuTerminalLakeAridDrop;
        lastValidatedUseExperimentalSignedTerrain = useExperimentalSignedTerrain;
        lastValidatedExperimentalTerrainProfileHash = ComputeExperimentalTerrainProfileHash();
        validateCacheInitialized = true;
    }
    private void Update()
    {
        if (scheduledAt > 0f && Time.time >= scheduledAt)
        {
            scheduledAt = -1f;
            StartOrRestartGeneration();
        }
    }


    private void StartOrRestartGeneration()
    {
        if (pending == PreviewWorldRebuildScope.None) return;
        if (generationCoroutine != null) StopCoroutine(generationCoroutine);
        generationCoroutine = StartCoroutine(GenerateAsync(generationVersion));
    }

    private System.Collections.IEnumerator GenerateAsync(int version)
    {
        IsGeneratingPreview = true;
        runningGenerationVersion = version;
        generationStartedAt = Time.realtimeSinceStartup;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (logWorldGenerationDiagnostics) Debug.Log($"[WorldGenV2 Async] Started generation version={version}");
        var s = pending; pending = PreviewWorldRebuildScope.None;
        if (logWorldGenerationDiagnostics)
            Debug.Log($"[WorldGenV2 Pipeline] Scopes={s}");

        GenDiagnostics diag = default;

        if ((s & PreviewWorldRebuildScope.Tectonics) != 0)
        {
            GenerateTopologyFields(out var topoLandDist, out var topoOceanDist, out var topoSignedCoastDistance, out diag);
            DispatchGpuCoastline(topoLandDist, topoOceanDist, topoSignedCoastDistance);
            DispatchGpuHeight();
            DispatchGpuHydrology();
            yield return null;
            if (version != generationVersion) { if (logWorldGenerationDiagnostics) Debug.Log($"[WorldGenV2 Async] Cancelled stale generation version={version}"); IsGeneratingPreview = false; yield break; }
        }
        if ((s & PreviewWorldRebuildScope.Hydrology) != 0)
        {
            DispatchGpuHydrology();
            yield return null;
        }
        if (version == generationVersion)
        {
            if (logWorldGenerationDiagnostics)
            {
                Debug.Log($"[WorldGenV2 Async] Completed generation version={version}");
                Debug.Log($"[WorldGenV2 Async] Total generation wall time={sw.ElapsedMilliseconds}ms");
            }
            WorldTexturesUpdated?.Invoke();
        }
        else if (logWorldGenerationDiagnostics)
        {
            Debug.Log($"[WorldGenV2 Async] Cancelled stale generation version={version}");
        }
        IsGeneratingPreview = false;
        generationCoroutine = null;
    }

    private PreviewWorldRebuildScope ExpandDependencies(PreviewWorldRebuildScope r)
    {
        if ((r & PreviewWorldRebuildScope.Tectonics) != 0)
            return PreviewWorldRebuildScope.Tectonics;

        if ((r & PreviewWorldRebuildScope.Hydrology) != 0)
            return PreviewWorldRebuildScope.Hydrology;

        return PreviewWorldRebuildScope.None;
    }
    private void EnsureGpuCoastlineResources()
    {
        EnsureGpuLandMaskTexture();
        EnsureGpuCoastlineTexture(ref gpuTectonicSurfaceTexture, "MenuGpuTectonicSurfaceV2");
        EnsureGpuCoastlineTexture(ref gpuTectonicBoundaryTexture, "MenuGpuTectonicBoundaryV2");
        EnsureGpuCoastlineTexture(ref gpuTectonicCrustTexture, "MenuGpuTectonicCrustV2");
    }
    private void EnsureGpuLandMaskTexture()
    {
        if (gpuLandMaskTexture != null && gpuLandMaskTexture.width == mapWidth && gpuLandMaskTexture.height == mapHeight && gpuLandMaskTexture.IsCreated()) return;
        if (gpuLandMaskTexture != null) { gpuLandMaskTexture.Release(); Destroy(gpuLandMaskTexture); gpuLandMaskTexture = null; }
        gpuLandMaskTexture = new RenderTexture(mapWidth, mapHeight, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
        {
            name = "MenuGpuLandMaskV2", enableRandomWrite = true, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point, useMipMap = false, autoGenerateMips = false
        };
        gpuLandMaskTexture.Create();
    }

    private void EnsureGpuCoastlineTexture(ref RenderTexture rt, string name)
    {
        if (rt != null && rt.width == mapWidth && rt.height == mapHeight && rt.IsCreated()) return;
        if (rt != null) { rt.Release(); Destroy(rt); rt = null; }
        rt = new RenderTexture(mapWidth, mapHeight, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
        {
            name = name, enableRandomWrite = true, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, useMipMap = false, autoGenerateMips = false
        };
        rt.Create();
    }

    private void ReleaseGpuCoastlineResources()
    {
        if (gpuLandMaskTexture != null) { gpuLandMaskTexture.Release(); Destroy(gpuLandMaskTexture); gpuLandMaskTexture = null; }
        if (gpuTectonicSurfaceTexture != null) { gpuTectonicSurfaceTexture.Release(); Destroy(gpuTectonicSurfaceTexture); gpuTectonicSurfaceTexture = null; }
        if (gpuTectonicBoundaryTexture != null) { gpuTectonicBoundaryTexture.Release(); Destroy(gpuTectonicBoundaryTexture); gpuTectonicBoundaryTexture = null; }
        if (gpuTectonicCrustTexture != null) { gpuTectonicCrustTexture.Release(); Destroy(gpuTectonicCrustTexture); gpuTectonicCrustTexture = null; }
        if (topoLandCoastDistanceBuffer != null) { topoLandCoastDistanceBuffer.Release(); topoLandCoastDistanceBuffer = null; }
        if (topoOceanCoastDistanceBuffer != null) { topoOceanCoastDistanceBuffer.Release(); topoOceanCoastDistanceBuffer = null; }
        if (topoSignedCoastDistanceBuffer != null) { topoSignedCoastDistanceBuffer.Release(); topoSignedCoastDistanceBuffer = null; }
        gpuCoastlineKernel = -1;
    }

    private void DispatchGpuCoastline(float[] topoLandDist, float[] topoOceanDist, float[] topoSignedCoastDistance)
    {
        TryAssignDefaultGpuCoastlineCompute();
        if (menuPlanetPreviewCoastlineCompute == null) return;
        EnsureGpuCoastlineResources();
        int count = topologyWidth * topologyHeight;
        topoLandCoastDistanceBuffer?.Release(); topoOceanCoastDistanceBuffer?.Release(); topoSignedCoastDistanceBuffer?.Release();
        topoLandCoastDistanceBuffer = new ComputeBuffer(count, sizeof(float));
        topoOceanCoastDistanceBuffer = new ComputeBuffer(count, sizeof(float));
        topoSignedCoastDistanceBuffer = new ComputeBuffer(count, sizeof(float));
        topoLandCoastDistanceBuffer.SetData(topoLandDist);
        topoOceanCoastDistanceBuffer.SetData(topoOceanDist);
        topoSignedCoastDistanceBuffer.SetData(topoSignedCoastDistance);
        if (gpuCoastlineKernel < 0) gpuCoastlineKernel = menuPlanetPreviewCoastlineCompute.FindKernel("GenerateCoastline");
        var cs = menuPlanetPreviewCoastlineCompute;
        cs.SetInt("_MapWidth", mapWidth); cs.SetInt("_MapHeight", mapHeight);
        cs.SetInt("_TopologyWidth", topologyWidth); cs.SetInt("_TopologyHeight", topologyHeight);
        cs.SetFloat("_Seed", inputs.seed); cs.SetFloat("_CoastlineDeformationWidthCells", coastlineDeformationWidthCells); cs.SetFloat("_CoastlineWarpStrength", coastlineWarpStrength); cs.SetFloat("_CoastlineMidNoiseStrength", coastlineMidNoiseStrength); cs.SetFloat("_CoastlineEdgeNoiseStrength", coastlineEdgeNoiseStrength); cs.SetFloat("_CoastlineSoftness", coastlineSoftness); cs.SetFloat("_CoastlineThresholdBias", coastlineThresholdBias);
        cs.SetBuffer(gpuCoastlineKernel, "_TopoLandCoastDistance", topoLandCoastDistanceBuffer);
        cs.SetBuffer(gpuCoastlineKernel, "_TopoOceanCoastDistance", topoOceanCoastDistanceBuffer);
        cs.SetBuffer(gpuCoastlineKernel, "_TopoSignedCoastDistance", topoSignedCoastDistanceBuffer);
        cs.SetTexture(gpuCoastlineKernel, "_GpuTectonicSurfaceTex", gpuTectonicSurfaceTexture);
        cs.SetTexture(gpuCoastlineKernel, "_GpuLandMaskTex", gpuLandMaskTexture);
        cs.SetTexture(gpuCoastlineKernel, "_GpuTectonicBoundaryTex", gpuTectonicBoundaryTexture);
        cs.SetTexture(gpuCoastlineKernel, "_GpuTectonicCrustTex", gpuTectonicCrustTexture);
        cs.Dispatch(gpuCoastlineKernel, Mathf.CeilToInt(mapWidth / 8f), Mathf.CeilToInt(mapHeight / 8f), 1);
        ValidateGpuLandMaskAfterCoastlineDispatch();
    }

    private void ValidateGpuLandMaskAfterCoastlineDispatch()
    {
        if (!logHeightAttributionDebug || gpuLandMaskTexture == null || !gpuLandMaskTexture.IsCreated()) return;

        AsyncGPUReadback.Request(gpuLandMaskTexture, 0, req =>
        {
            if (req.hasError)
            {
                Debug.LogWarning("[WorldGenV2 LandMask Validation] GPU readback failed.");
                return;
            }

            var data = req.GetData<float>();
            int count = data.Length;
            if (count <= 0) return;

            float minV = float.MaxValue;
            float maxV = float.MinValue;
            double sum = 0.0;
            int landCount = 0;
            int midCount = 0;

            for (int i = 0; i < count; i++)
            {
                float v = data[i];
                minV = Mathf.Min(minV, v);
                maxV = Mathf.Max(maxV, v);
                sum += v;
                if (v >= 0.5f) landCount++;
                if (v > 0.001f && v < 0.999f) midCount++;
            }

            float inv = 1f / count;
            float mean = (float)(sum * inv);
            float landCoverage = landCount * inv * 100f;
            float midCoverage = midCount * inv * 100f;

            Debug.Log(
                $"[WorldGenV2 LandMask Validation] Min={minV:F4} Max={maxV:F4} Mean={mean:F4} " +
                $"Land>=0.5={landCoverage:F2}% Mid(0.001-0.999)={midCoverage:F2}% Format={gpuLandMaskTexture.format}"
            );

            if (maxV < 0.99f || minV > 0.01f)
            {
                Debug.LogError(
                    $"[WorldGenV2 LandMask Validation] Invalid binary land mask range Min={minV:F4} Max={maxV:F4}. " +
                    "Expected near 0 and near 1. Signed terrain/hydrology inputs are unreliable this frame."
                );
            }
        });
    }

private int ComputeExperimentalTerrainProfileHash()
{
    unchecked
    {
        int h = 17;
        if (experimentalTerrainProfiles != null)
        {
            for (int i = 0; i < experimentalTerrainProfiles.Length; i++)
            {
                var p = experimentalTerrainProfiles[i];
                h = h * 31 + i;
                h = h * 31 + (p.name == null ? 0 : p.name.GetHashCode());
                h = h * 31 + Mathf.RoundToInt(p.heightBias * 10000f);
                h = h * 31 + Mathf.RoundToInt(p.upwardReliefStrength * 10000f);
                h = h * 31 + Mathf.RoundToInt(p.downwardBasinStrength * 10000f);
                h = h * 31 + Mathf.RoundToInt(p.valleyCutStrength * 10000f);
                h = h * 31 + Mathf.RoundToInt(p.broadNoiseAmplitude * 10000f);
                h = h * 31 + Mathf.RoundToInt(p.midNoiseAmplitude * 10000f);
                h = h * 31 + Mathf.RoundToInt(p.fineNoiseAmplitude * 10000f);
                h = h * 31 + Mathf.RoundToInt(p.broadNoiseScale * 10000f);
                h = h * 31 + Mathf.RoundToInt(p.midNoiseScale * 10000f);
                h = h * 31 + Mathf.RoundToInt(p.fineNoiseScale * 10000f);
                h = h * 31 + Mathf.RoundToInt(p.ridgeStrength * 10000f);
                h = h * 31 + Mathf.RoundToInt(p.hillThreshold * 10000f);
                h = h * 31 + Mathf.RoundToInt(p.mountainThreshold * 10000f);
                h = h * 31 + Mathf.RoundToInt(p.basinCandidateBias * 10000f);
                // New hydrology fields
                h = h * 31 + Mathf.RoundToInt(p.lakeBasinMinDepth * 10000f);
                h = h * 31 + Mathf.RoundToInt(p.lakeBasinFullDepth * 10000f);
                h = h * 31 + Mathf.RoundToInt(p.riverAccumulationThresholdSparse * 10000f);
                h = h * 31 + Mathf.RoundToInt(p.riverAccumulationThresholdStandard * 10000f);
                h = h * 31 + Mathf.RoundToInt(p.riverAccumulationThresholdAbundant * 10000f);
                h = h * 31 + Mathf.RoundToInt(p.inlandBasinStrength * 10000f);
                h = h * 31 + Mathf.RoundToInt(p.flowMinDownhillDelta * 10000f);
                h = h * 31 + Mathf.RoundToInt(p.flowAccumulationCompression * 10000f);
                h = h * 31 + Mathf.RoundToInt(p.macroReliefStrength * 10000f);
                h = h * 31 + Mathf.RoundToInt(p.macroReliefScale * 10000f);
                h = h * 31 + Mathf.RoundToInt(p.inlandBasinStrength * 10000f);
                h = h * 31 + Mathf.RoundToInt(p.inlandBasinScale * 10000f);
                h = h * 31 + Mathf.RoundToInt(p.watershedRidgeStrength * 10000f);
                h = h * 31 + Mathf.RoundToInt(p.riverMinWidthPixels * 100f);
                h = h * 31 + Mathf.RoundToInt(p.riverMaxWidthPixels * 100f);
            }
        }
        return h;
    }
}

    private void EnsureGpuHeightTexture()
    {
        if (gpuHeightTexture != null &&
            gpuHeightTexture.width == mapWidth &&
            gpuHeightTexture.height == mapHeight &&
            gpuHeightTexture.IsCreated())
        {
            return;
        }

        ReleaseGpuHeightTexture();
        ReleaseGpuDisplacementTexture();
        ReleaseGpuHydrologyResources();
        gpuHeightTexture = new RenderTexture(mapWidth, mapHeight, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
        {
            name = "MenuGpuHeightV2",
            enableRandomWrite = true,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            useMipMap = false,
            autoGenerateMips = false
        };
        gpuHeightTexture.Create();
    }

    private void ReleaseGpuHeightTexture()
    {
        if (gpuHeightTexture == null) return;
        gpuHeightTexture.Release();
        Destroy(gpuHeightTexture);
        gpuHeightTexture = null;
        gpuHeightKernel = -1;
    }

    private void EnsureGpuDisplacementTexture()
    {
        if (gpuDisplacementTexture != null &&
            gpuDisplacementTexture.width == mapWidth &&
            gpuDisplacementTexture.height == mapHeight &&
            gpuDisplacementTexture.IsCreated())
        {
            return;
        }

        ReleaseGpuDisplacementTexture();
        gpuDisplacementTexture = new RenderTexture(mapWidth, mapHeight, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
        {
            name = "MenuGpuDisplacementV2",
            enableRandomWrite = true,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            useMipMap = false,
            autoGenerateMips = false
        };
        gpuDisplacementTexture.Create();
    }

    private void ReleaseGpuDisplacementTexture()
    {
        if (gpuDisplacementTexture == null) return;
        gpuDisplacementTexture.Release();
        Destroy(gpuDisplacementTexture);
        gpuDisplacementTexture = null;
    }

    private void DispatchGpuHeight()
    {
        if (!useGpuHeightPreview) return;
        TryAssignDefaultGpuHeightCompute();
        if (menuPlanetPreviewHeightCompute == null) return;

        EnsureGpuHeightTexture();
        EnsureGpuDisplacementTexture();
        if (gpuHeightTexture == null || gpuDisplacementTexture == null) return;

        if (gpuHeightKernel < 0)
            gpuHeightKernel = menuPlanetPreviewHeightCompute.FindKernel("GenerateHeight");

        Debug.Log(
            $"[WorldGenV2 GPU Height Asset] ComputeAsset={(menuPlanetPreviewHeightCompute != null ? menuPlanetPreviewHeightCompute.name : "NULL")} " +
            $"Kernel={gpuHeightKernel}"
        );

        menuPlanetPreviewHeightCompute.SetInt("_MapWidth", mapWidth);
        menuPlanetPreviewHeightCompute.SetInt("_MapHeight", mapHeight);
        menuPlanetPreviewHeightCompute.SetFloat("_Seed", inputs.seed);
        float presetMultiplier = GetTerrainRoughnessElevationMultiplier(Mathf.Clamp(inputs.terrainRoughnessPresetIndex, 0, 4));
        menuPlanetPreviewHeightCompute.SetFloat("_Elevation", Mathf.Clamp01(inputs.elevation * presetMultiplier));
        int terrainPreset = Mathf.Clamp(inputs.terrainRoughnessPresetIndex, 0, Mathf.Max(0, experimentalTerrainProfiles.Length - 1));
        var expProfile = experimentalTerrainProfiles[Mathf.Min(terrainPreset, experimentalTerrainProfiles.Length - 1)];
        menuPlanetPreviewHeightCompute.SetInt("_UseExperimentalSignedTerrain", useExperimentalSignedTerrain ? 1 : 0);
        menuPlanetPreviewHeightCompute.SetInt("_DebugForceSignedHeightSentinel", debugForceSignedHeightSentinel ? 1 : 0);
        // Height debug inspection: when enabled the compute writes debug vectors into the height RT
        menuPlanetPreviewHeightCompute.SetInt("_DebugHeightInspect", debugHeightInspect ? 1 : 0);
        menuPlanetPreviewHeightCompute.SetFloat("_DebugHeightRemap", debugHeightRemap);
        menuPlanetPreviewHeightCompute.SetInt("_TerrainRoughnessPresetIndex", terrainPreset);
        menuPlanetPreviewHeightCompute.SetFloat("_ExpHeightBias", expProfile.heightBias);
        menuPlanetPreviewHeightCompute.SetFloat("_ExpUpwardReliefStrength", expProfile.upwardReliefStrength);
        menuPlanetPreviewHeightCompute.SetFloat("_ExpDownwardBasinStrength", expProfile.downwardBasinStrength);
        menuPlanetPreviewHeightCompute.SetFloat("_ExpValleyCutStrength", expProfile.valleyCutStrength);
        menuPlanetPreviewHeightCompute.SetFloat("_ExpBroadNoiseAmplitude", expProfile.broadNoiseAmplitude);
        menuPlanetPreviewHeightCompute.SetFloat("_ExpMidNoiseAmplitude", expProfile.midNoiseAmplitude);
        menuPlanetPreviewHeightCompute.SetFloat("_ExpFineNoiseAmplitude", expProfile.fineNoiseAmplitude);
        menuPlanetPreviewHeightCompute.SetFloat("_ExpBroadNoiseScale", expProfile.broadNoiseScale);
        menuPlanetPreviewHeightCompute.SetFloat("_ExpMidNoiseScale", expProfile.midNoiseScale);
        menuPlanetPreviewHeightCompute.SetFloat("_ExpFineNoiseScale", expProfile.fineNoiseScale);
        menuPlanetPreviewHeightCompute.SetFloat("_ExpRidgeStrength", expProfile.ridgeStrength);
        menuPlanetPreviewHeightCompute.SetFloat("_ExpHillThreshold", expProfile.hillThreshold);
        menuPlanetPreviewHeightCompute.SetFloat("_ExpMountainThreshold", expProfile.mountainThreshold);
        menuPlanetPreviewHeightCompute.SetFloat("_ExpBasinCandidateBias", expProfile.basinCandidateBias);
        menuPlanetPreviewHeightCompute.SetFloat("_ElevationNoiseStrength", Mathf.Clamp01(inputs.elevationNoiseStrength));
        menuPlanetPreviewHeightCompute.SetFloat("_MacroReliefStrength", expProfile.macroReliefStrength);
        menuPlanetPreviewHeightCompute.SetFloat("_MacroReliefScale", expProfile.macroReliefScale);
        menuPlanetPreviewHeightCompute.SetFloat("_InlandBasinStrength", expProfile.inlandBasinStrength);
        menuPlanetPreviewHeightCompute.SetFloat("_InlandBasinScale", expProfile.inlandBasinScale);
        menuPlanetPreviewHeightCompute.SetFloat("_WatershedRidgeStrength", expProfile.watershedRidgeStrength);

        Texture tectonicSurface = TectonicSurfaceTexture;
        Texture landMaskSource = gpuLandMaskTexture != null && gpuLandMaskTexture.IsCreated()
            ? (Texture)gpuLandMaskTexture
            : tectonicSurface;
        Texture tectonicCrust = TectonicCrustTexture;

        if (tectonicSurface == null || landMaskSource == null || tectonicCrust == null)
        {
            Debug.LogWarning(
                $"[MenuPlanetPreviewWorldGeneratorV2] DispatchGpuHeight skipped because input textures are missing. " +
                $"Surface={(tectonicSurface != null ? tectonicSurface.name : "NULL")} " +
                $"LandMask={(landMaskSource != null ? landMaskSource.name : "NULL")} " +
                $"Crust={(tectonicCrust != null ? tectonicCrust.name : "NULL")}"
            );

            RequestRebuild(PreviewWorldRebuildScope.Tectonics, false);
            return;
        }

        menuPlanetPreviewHeightCompute.SetTexture(gpuHeightKernel, "_TectonicSurfaceTex", tectonicSurface);
        menuPlanetPreviewHeightCompute.SetTexture(gpuHeightKernel, "_LandMaskTex", landMaskSource);
        menuPlanetPreviewHeightCompute.SetTexture(gpuHeightKernel, "_TectonicCrustTex", tectonicCrust);
        menuPlanetPreviewHeightCompute.SetTexture(gpuHeightKernel, "_GpuHeightTex", gpuHeightTexture);
        menuPlanetPreviewHeightCompute.SetTexture(gpuHeightKernel, "_GpuDisplacementTex", gpuDisplacementTexture);

        int groupsX = Mathf.CeilToInt(mapWidth / 8f);
        int groupsY = Mathf.CeilToInt(mapHeight / 8f);
        menuPlanetPreviewHeightCompute.Dispatch(gpuHeightKernel, groupsX, groupsY, 1);
        heightDebugDispatchSerial++;

        Debug.Log(
            $"[WorldGenV2 GPU Height] ExperimentalSigned={useExperimentalSignedTerrain} " +
            $"PresetIndex={terrainPreset} PresetName={expProfile.name} " +
            $"Seed={inputs.seed:0.000} Elevation={inputs.elevation:0.000} PresetMultiplier={presetMultiplier:0.000} " +
            $"ExpBias={expProfile.heightBias:0.000} ExpUpwardReliefStrength={expProfile.upwardReliefStrength:0.000} " +
            $"ExpDownwardBasinStrength={expProfile.downwardBasinStrength:0.000} ExpValleyCutStrength={expProfile.valleyCutStrength:0.000} " +
            $"ExpBroadAmp={expProfile.broadNoiseAmplitude:0.000} ExpMidAmp={expProfile.midNoiseAmplitude:0.000} ExpFineAmp={expProfile.fineNoiseAmplitude:0.000} " +
            $"ExpBroadScale={expProfile.broadNoiseScale:0.000} ExpMidScale={expProfile.midNoiseScale:0.000} ExpFineScale={expProfile.fineNoiseScale:0.000} " +
            $"ExpRidge={expProfile.ridgeStrength:0.000} HillThreshold={expProfile.hillThreshold:0.000} MountainThreshold={expProfile.mountainThreshold:0.000} BasinBias={expProfile.basinCandidateBias:0.000} " +
            $"Map={mapWidth}x{mapHeight} Groups={groupsX}x{groupsY} Sentinel={debugForceSignedHeightSentinel}");

        LogHeightAutoDebugStats();
    }
    private void LogHeightAutoDebugStats()
    {
        if (!logHeightAutoDebugAfterDispatch || gpuHeightTexture == null) return;
        int dispatchSerialSnapshot = heightDebugDispatchSerial;
        bool sentinelSnapshot = debugForceSignedHeightSentinel;
        bool useExperimentalSignedSnapshot = useExperimentalSignedTerrain;
        int expectedWidthSnapshot = mapWidth;
        int expectedHeightSnapshot = mapHeight;
        int expectedCountSnapshot = expectedWidthSnapshot * expectedHeightSnapshot;
        int heightRtWidthSnapshot = gpuHeightTexture.width;
        int heightRtHeightSnapshot = gpuHeightTexture.height;
        int displacementRtWidthSnapshot = gpuDisplacementTexture != null ? gpuDisplacementTexture.width : -1;
        int displacementRtHeightSnapshot = gpuDisplacementTexture != null ? gpuDisplacementTexture.height : -1;

        AsyncGPUReadback.Request(gpuHeightTexture, 0, TextureFormat.RGBAHalf, req =>
        {
            if (req.hasError)
            {
                Debug.LogWarning("[WorldGenV2 Height Stats] GPU readback failed.");
                return;
            }

            var raw = req.GetData<ushort>();
            int count = raw.Length / 4;
            if (count <= 0) return;
            if (count != expectedCountSnapshot)
            {
                Debug.LogError(
                    $"[WorldGenV2 Height Stats Contract] Height readback count mismatch. " +
                    $"Expected={expectedCountSnapshot} ({expectedWidthSnapshot}x{expectedHeightSnapshot}) " +
                    $"Actual={count} HeightRT={heightRtWidthSnapshot}x{heightRtHeightSnapshot} " +
                    $"DisplacementRT={displacementRtWidthSnapshot}x{displacementRtHeightSnapshot} " +
                    $"DispatchSerial={dispatchSerialSnapshot}"
                );
                return;
            }

            Vector4[] heightSnapshot = new Vector4[count];
            for (int i = 0; i < count; i++)
            {
                int bi = i * 4;
                heightSnapshot[i] = new Vector4(
                    Mathf.HalfToFloat(raw[bi + 0]),
                    Mathf.HalfToFloat(raw[bi + 1]),
                    Mathf.HalfToFloat(raw[bi + 2]),
                    Mathf.HalfToFloat(raw[bi + 3])
                );
            }

            float signedHeightMin = float.MaxValue;
            float signedHeightMax = float.MinValue;
            double signedHeightSum = 0.0;
            int positiveCount = 0;
            int negativeCount = 0;
            int positiveAnyCount = 0;
            int positiveWeakCount = 0;
            int positiveStrongCount = 0;
            int negativeAnyCount = 0;
            int negativeWeakCount = 0;
            int negativeStrongCount = 0;
            int hillCount = 0;
            int mountainCount = 0;
            int basinPotentialCount = 0;
            float approxFinalDisplacementMin = float.MaxValue;
            float approxFinalDisplacementMax = float.MinValue;

            for (int i = 0; i < count; i++)
            {
                Vector4 h = heightSnapshot[i];
                float signedHeight = h.x;
                float hillMask = h.y;
                float mountainMask = h.z;
                float basinPotential = h.w;

                signedHeightMin = Mathf.Min(signedHeightMin, signedHeight);
                signedHeightMax = Mathf.Max(signedHeightMax, signedHeight);
                signedHeightSum += signedHeight;

                if (signedHeight > 0.02f) positiveCount++;
                if (signedHeight < -0.02f) negativeCount++;
                if (signedHeight > 0.0001f) positiveAnyCount++;
                if (signedHeight > 0.005f) positiveWeakCount++;
                if (signedHeight > 0.02f) positiveStrongCount++;
                if (signedHeight < -0.0001f) negativeAnyCount++;
                if (signedHeight < -0.005f) negativeWeakCount++;
                if (signedHeight < -0.02f) negativeStrongCount++;
                if (hillMask > 0.05f) hillCount++;
                if (mountainMask > 0.05f) mountainCount++;
                if (basinPotential > 0.05f) basinPotentialCount++;

                float approxFinalDisplacement =
                    signedHeight * debugTerrainElevationStrength +
                    hillMask * debugHillStrength +
                    mountainMask * debugMountainStrength -
                    debugOceanDepth;

                approxFinalDisplacementMin = Mathf.Min(approxFinalDisplacementMin, approxFinalDisplacement);
                approxFinalDisplacementMax = Mathf.Max(approxFinalDisplacementMax, approxFinalDisplacement);
            }

            float invCount = 1f / count;
            float signedHeightMean = (float)(signedHeightSum * invCount);
            float positiveCoverage = positiveCount * invCount * 100f;
            float negativeCoverage = negativeCount * invCount * 100f;
            float positiveAnyCoverage = positiveAnyCount * invCount * 100f;
            float positiveWeakCoverage = positiveWeakCount * invCount * 100f;
            float positiveStrongCoverage = positiveStrongCount * invCount * 100f;
            float negativeAnyCoverage = negativeAnyCount * invCount * 100f;
            float negativeWeakCoverage = negativeWeakCount * invCount * 100f;
            float negativeStrongCoverage = negativeStrongCount * invCount * 100f;
            float hillCoverage = hillCount * invCount * 100f;
            float mountainCoverage = mountainCount * invCount * 100f;
            float basinPotentialCoverage = basinPotentialCount * invCount * 100f;

            Debug.Log(
                $"[WorldGenV2 Height Stats] SignedHeightMin={signedHeightMin:F4} SignedHeightMax={signedHeightMax:F4} SignedHeightMean={signedHeightMean:F4} " +
                $"PositiveCoverage={positiveCoverage:F2}% NegativeCoverage={negativeCoverage:F2}% HillCoverage={hillCoverage:F2}% MountainCoverage={mountainCoverage:F2}% " +
                $"BasinPotentialCoverage={basinPotentialCoverage:F2}% ApproxFinalDisplacementMin={approxFinalDisplacementMin:F4} ApproxFinalDisplacementMax={approxFinalDisplacementMax:F4} " +
                $"(TerrainElevationStrength={debugTerrainElevationStrength:F3} HillStrength={debugHillStrength:F3} MountainStrength={debugMountainStrength:F3} OceanDepth={debugOceanDepth:F3}) " +
                $"DispatchSerial={dispatchSerialSnapshot} Sentinel={sentinelSnapshot} ExperimentalSigned={useExperimentalSignedSnapshot}");

            Debug.Log(
                $"[WorldGenV2 Height Stats Detail] " +
                $"PositiveAny>0.0001={positiveAnyCoverage:F2}% PositiveWeak>0.005={positiveWeakCoverage:F2}% PositiveStrong>0.02={positiveStrongCoverage:F2}% | " +
                $"NegativeAny<-0.0001={negativeAnyCoverage:F2}% NegativeWeak<-0.005={negativeWeakCoverage:F2}% NegativeStrong<-0.02={negativeStrongCoverage:F2}%"
            );

            // If the compute was asked to emit debug inspection values, interpret channels differently
            if (debugHeightInspect)
            {
                float rawMin = float.MaxValue, rawMax = float.MinValue;
                float shapingMin = float.MaxValue, shapingMax = float.MinValue;
                float scaledHeightMin = float.MaxValue, scaledHeightMax = float.MinValue;
                float landMaskMinDebug = float.MaxValue, landMaskMaxDebug = float.MinValue;
                for (int i = 0; i < count; i++)
                {
                    Vector4 d = heightSnapshot[i];
                    rawMin = Mathf.Min(rawMin, d.x); rawMax = Mathf.Max(rawMax, d.x);
                    shapingMin = Mathf.Min(shapingMin, d.y); shapingMax = Mathf.Max(shapingMax, d.y);
                    scaledHeightMin = Mathf.Min(scaledHeightMin, d.z); scaledHeightMax = Mathf.Max(scaledHeightMax, d.z);
                    landMaskMinDebug = Mathf.Min(landMaskMinDebug, d.w); landMaskMaxDebug = Mathf.Max(landMaskMaxDebug, d.w);
                }
                Debug.Log($"[WorldGenV2 Height Debug Inspect] RawNoiseMin={rawMin:F6} RawNoiseMax={rawMax:F6} | ShapingMin={shapingMin:F6} ShapingMax={shapingMax:F6} | ScaledHeightMin={scaledHeightMin:F6} ScaledHeightMax={scaledHeightMax:F6} | LandMaskMin={landMaskMinDebug:F6} LandMaskMax={landMaskMaxDebug:F6}");
            }

            RenderTexture boundSurfaceTexture = gpuLandMaskTexture;
            Texture activeSurfaceTexture = TectonicSurfaceTexture;
            RenderTexture activeRt = activeSurfaceTexture as RenderTexture;
            bool activeIsRenderTexture = activeRt != null;
            if (!logHeightAttributionDebug || boundSurfaceTexture == null || !boundSurfaceTexture.IsCreated()) return;

            string activeSurfaceInfo = activeSurfaceTexture == null
                ? "NULL"
                : $"{activeSurfaceTexture.name} Type={activeSurfaceTexture.GetType().Name}";
            string activeRenderTextureInfo = activeIsRenderTexture
                ? $" ActiveRT={activeRt.name} ActiveRTCreated={activeRt.IsCreated()} ActiveRTSize={activeRt.width}x{activeRt.height}"
                : string.Empty;
            Debug.Log($"[WorldGenV2 Height Attribution] SourceTex={boundSurfaceTexture.name} Size={boundSurfaceTexture.width}x{boundSurfaceTexture.height} Format={boundSurfaceTexture.format} Created={boundSurfaceTexture.IsCreated()} ActiveSurface={activeSurfaceInfo}{activeRenderTextureInfo}");

            AsyncGPUReadback.Request(boundSurfaceTexture, 0, surfaceReq =>
            {
                if (surfaceReq.hasError)
                {
                    Debug.LogWarning("[WorldGenV2 Height Attribution] Tectonic surface readback failed.");
                    return;
                }

                var surfaceData = surfaceReq.GetData<float>();
                if (surfaceData.Length != expectedCountSnapshot)
                {
                    Debug.LogError(
                        $"[WorldGenV2 Height Stats Contract] Land mask readback count mismatch. " +
                        $"Expected={expectedCountSnapshot} ({expectedWidthSnapshot}x{expectedHeightSnapshot}) " +
                        $"LandMaskActual={surfaceData.Length} HeightActual={count} " +
                        $"LandMaskRT={boundSurfaceTexture.width}x{boundSurfaceTexture.height} Format={boundSurfaceTexture.format} " +
                        $"HeightRT={heightRtWidthSnapshot}x{heightRtHeightSnapshot} DisplacementRT={displacementRtWidthSnapshot}x{displacementRtHeightSnapshot} " +
                        $"DispatchSerial={dispatchSerialSnapshot}"
                    );
                    return;
                }

                float landMaskMin = float.MaxValue;
                float landMaskMax = float.MinValue;
                double landMaskSum = 0.0;
                int landMaskStrongCount = 0;
                int landMaskMidCount = 0;
                int nearCoastCount = 0;
                int landWithPositiveHeightCount = 0;
                int landWithNegativeHeightCount = 0;
                float signedOverLandMaskMax = float.MinValue;
                float signedOverLandMaskMin = float.MaxValue;
                int normalizedSampleCount = 0;

                for (int i = 0; i < count; i++)
                {
                    float landMask = surfaceData[i];
                    float signedHeight = heightSnapshot[i].x;

                    landMaskMin = Mathf.Min(landMaskMin, landMask);
                    landMaskMax = Mathf.Max(landMaskMax, landMask);
                    landMaskSum += landMask;

                    if (landMask > 0.90f) landMaskStrongCount++;
                    if (landMask > 0.10f && landMask <= 0.90f) landMaskMidCount++;
                    if (landMask > 0.01f && landMask < 0.99f) nearCoastCount++;

                    if (landMask > 0.10f && signedHeight > 0.0001f) landWithPositiveHeightCount++;
                    if (landMask > 0.10f && signedHeight < -0.0001f) landWithNegativeHeightCount++;

                    if (landMask > 0.001f)
                    {
                        float normalized = signedHeight / Mathf.Max(0.001f, landMask);
                        signedOverLandMaskMax = Mathf.Max(signedOverLandMaskMax, normalized);
                        signedOverLandMaskMin = Mathf.Min(signedOverLandMaskMin, normalized);
                        normalizedSampleCount++;
                    }
                }

                float inv = 1f / count;
                string signedOverLandMaskRange = normalizedSampleCount > 0
                    ? $"SignedOverLandMaskMin={signedOverLandMaskMin:F4} SignedOverLandMaskMax={signedOverLandMaskMax:F4}"
                    : "SignedOverLandMaskMin=NA SignedOverLandMaskMax=NA";

                Debug.Log(
                    $"[WorldGenV2 Height Attribution] LandMaskMin={landMaskMin:F4} LandMaskMax={landMaskMax:F4} LandMaskMean={(float)(landMaskSum * inv):F4} " +
                    $"LandMaskStrong>0.90={(landMaskStrongCount * inv * 100f):F2}% LandMaskMid0.10-0.90={(landMaskMidCount * inv * 100f):F2}% NearCoast0.01-0.99={(nearCoastCount * inv * 100f):F2}% " +
                    $"LandPosHeight={(landWithPositiveHeightCount * inv * 100f):F2}% LandNegHeight={(landWithNegativeHeightCount * inv * 100f):F2}% NormalizedSamples={normalizedSampleCount} " +
                    $"{signedOverLandMaskRange} Sentinel={sentinelSnapshot} ExperimentalSigned={useExperimentalSignedSnapshot} DispatchSerial={dispatchSerialSnapshot}"
                );
            });
        });
    }

    private float GetTerrainRoughnessElevationMultiplier(int presetIndex)
    {
        return presetIndex switch
        {
            0 => flatElevationMultiplier,
            1 => smoothElevationMultiplier,
            2 => standardElevationMultiplier,
            3 => mountainousElevationMultiplier,
            4 => alpineElevationMultiplier,
            _ => 1f
        };
    }


    private void TryAssignDefaultGpuCoastlineCompute()
    {
#if UNITY_EDITOR
        if (menuPlanetPreviewCoastlineCompute == null)
        {
            menuPlanetPreviewCoastlineCompute = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/MenuPlanetPreviewCoastline.compute");
        }
#endif
    }

    private void TryAssignDefaultGpuHeightCompute()
    {
#if UNITY_EDITOR
        if (menuPlanetPreviewHeightCompute == null)
        {
            menuPlanetPreviewHeightCompute = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/MenuPlanetPreviewHeight.compute");
        }
#endif
    }

    private void TryAssignDefaultGpuHydrologyCompute()
    {
#if UNITY_EDITOR
        if (menuPlanetPreviewHydrologyCompute == null)
        {
            menuPlanetPreviewHydrologyCompute = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/MenuPlanetPreviewHydrology.compute");
        }
#endif
    }

    private int HydrologyAnalysisWidth => Mathf.Max(32, mapWidth / Mathf.Max(1, gpuHydrologyAnalysisDivisor));
    private int HydrologyAnalysisHeight => Mathf.Max(16, mapHeight / Mathf.Max(1, gpuHydrologyAnalysisDivisor));

    private static RenderTextureFormat AnalysisPrecisionFormat()
    {
        // Fill/accumulation need full float precision: the epsilon-fill gradient is below half-float resolution.
        return SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBFloat)
            ? RenderTextureFormat.ARGBFloat
            : RenderTextureFormat.ARGBHalf;
    }

    private static void EnsureHydrologyTexture(ref RenderTexture rt, int width, int height, RenderTextureFormat format, FilterMode filter, string name)
    {
        if (rt != null && rt.width == width && rt.height == height && rt.format == format && rt.IsCreated()) return;
        ReleaseRenderTexture(ref rt);
        rt = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear)
        {
            name = name,
            enableRandomWrite = true,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = filter,
            useMipMap = false,
            autoGenerateMips = false
        };
        rt.Create();
    }

    private static void EnsureComputeBuffer(ref ComputeBuffer buffer, int count, int stride, ComputeBufferType type = ComputeBufferType.Default)
    {
        if (buffer != null && buffer.IsValid() && buffer.count == count && buffer.stride == stride) return;
        buffer?.Release();
        buffer = new ComputeBuffer(count, stride, type);
    }

    private static void ReleaseComputeBuffer(ref ComputeBuffer buffer)
    {
        if (buffer == null) return;
        buffer.Release();
        buffer = null;
    }

    private void EnsureGpuHydrologyResources()
    {
        int aw = HydrologyAnalysisWidth;
        int ah = HydrologyAnalysisHeight;
        var precise = AnalysisPrecisionFormat();

        EnsureHydrologyTexture(ref gpuHydrologyTexture, mapWidth, mapHeight, RenderTextureFormat.ARGBHalf, FilterMode.Bilinear, "MenuGpuHydrologyV2");
        EnsureHydrologyTexture(ref gpuHydrologyDepthTexture, mapWidth, mapHeight, RenderTextureFormat.ARGBHalf, FilterMode.Bilinear, "MenuGpuHydrologyDepthV2");
        EnsureHydrologyTexture(ref gpuHydroTerrainTexture, aw, ah, precise, FilterMode.Point, "MenuGpuHydroTerrainV2");
        EnsureHydrologyTexture(ref gpuDrainageFillA, aw, ah, precise, FilterMode.Point, "MenuGpuDrainageFillA");
        EnsureHydrologyTexture(ref gpuDrainageFillB, aw, ah, precise, FilterMode.Point, "MenuGpuDrainageFillB");
        EnsureHydrologyTexture(ref gpuFlowDirectionTexture, aw, ah, precise, FilterMode.Point, "MenuGpuHydroFlow");
        EnsureHydrologyTexture(ref gpuFlowAccumA, aw, ah, precise, FilterMode.Point, "MenuGpuFlowAccumA");
        EnsureHydrologyTexture(ref gpuFlowAccumB, aw, ah, precise, FilterMode.Point, "MenuGpuFlowAccumB");
        EnsureHydrologyTexture(ref gpuCoarseHydrologyMaskTexture, aw, ah, RenderTextureFormat.ARGBHalf, FilterMode.Point, "MenuGpuCoarseHydroMask");
        EnsureHydrologyTexture(ref gpuCoarseHydrologyDepthTexture, aw, ah, RenderTextureFormat.ARGBHalf, FilterMode.Point, "MenuGpuCoarseHydroDepth");

        int cellCount = aw * ah;
        EnsureComputeBuffer(ref lakeLabelBufferA, cellCount, sizeof(int));
        EnsureComputeBuffer(ref lakeLabelBufferB, cellCount, sizeof(int));
        EnsureComputeBuffer(ref lakeAreaBuffer, cellCount, sizeof(uint));
        EnsureComputeBuffer(ref hydroConvergenceBuffer, 4, sizeof(uint));
        EnsureComputeBuffer(ref hydroIndirectArgsBuffer, 3, sizeof(uint), ComputeBufferType.IndirectArguments);
        EnsureComputeBuffer(ref hydroDebugStatsBuffer, HydroDebugStatCount, sizeof(uint));

        hydroAnalysisWidth = aw;
        hydroAnalysisHeight = ah;
    }

    private static void ReleaseRenderTexture(ref RenderTexture rt)
    {
        if (rt == null) return;
        rt.Release();
        Destroy(rt);
        rt = null;
    }

    private void ReleaseGpuHydrologyResources()
    {
        ReleaseRenderTexture(ref gpuHydrologyTexture);
        ReleaseRenderTexture(ref gpuHydrologyDepthTexture);
        ReleaseRenderTexture(ref gpuHydroTerrainTexture);
        ReleaseRenderTexture(ref gpuDrainageFillA);
        ReleaseRenderTexture(ref gpuDrainageFillB);
        ReleaseRenderTexture(ref gpuFlowDirectionTexture);
        ReleaseRenderTexture(ref gpuFlowAccumA);
        ReleaseRenderTexture(ref gpuFlowAccumB);
        ReleaseRenderTexture(ref gpuCoarseHydrologyMaskTexture);
        ReleaseRenderTexture(ref gpuCoarseHydrologyDepthTexture);
        ReleaseComputeBuffer(ref lakeLabelBufferA);
        ReleaseComputeBuffer(ref lakeLabelBufferB);
        ReleaseComputeBuffer(ref lakeAreaBuffer);
        ReleaseComputeBuffer(ref hydroConvergenceBuffer);
        ReleaseComputeBuffer(ref hydroIndirectArgsBuffer);
        ReleaseComputeBuffer(ref hydroDebugStatsBuffer);
        hydroAnalysisWidth = 0;
        hydroAnalysisHeight = 0;
    }

    private void OnDestroy()
    {
        Release();
    }

    private void ClearHydrologyTextures()
    {
        ClearRenderTexture(gpuHydrologyTexture, Color.clear);
        ClearRenderTexture(gpuHydrologyDepthTexture, Color.clear);
    }

    private static void ClearRenderTexture(RenderTexture rt, Color clearColor)
    {
        if (rt == null || !rt.IsCreated()) return;
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, clearColor);
        RenderTexture.active = previous;
    }

    private void DispatchGpuHydrology()
    {
if (!useGpuHydrologyPreview) return;
    if (!SystemInfo.supportsComputeShaders)
    {
        Debug.LogWarning("[MenuPlanetPreview GPU Hydrology] Compute shaders unsupported.");
        return;
    }
    if (!SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBHalf)) return;

    TryAssignDefaultGpuHydrologyCompute();
    if (menuPlanetPreviewHydrologyCompute == null) return;

    EnsureGpuHydrologyResources();

    if (!HasHydrologyInputTextures())
    {
        Debug.LogWarning("[MenuPlanetPreviewWorldGeneratorV2] DispatchGpuHydrology skipped because required input textures are missing.");
        return;
    }
    

    int analysisWidth = hydroAnalysisWidth;
    int analysisHeight = hydroAnalysisHeight;
    var cs = menuPlanetPreviewHydrologyCompute;
    cs.SetInt("_MapWidth", mapWidth);
    cs.SetInt("_MapHeight", mapHeight);
    cs.SetInt("_HydrologyAnalysisWidth", analysisWidth);
    cs.SetInt("_HydrologyAnalysisHeight", analysisHeight);
    cs.SetInt("_HydrologyFootprintX", Mathf.Max(1, Mathf.CeilToInt(mapWidth / (float)analysisWidth)));
    cs.SetInt("_HydrologyFootprintY", Mathf.Max(1, Mathf.CeilToInt(mapHeight / (float)analysisHeight)));
    cs.SetFloat("_Seed", inputs.seed);
    cs.SetFloat("_Elevation", Mathf.Clamp01(inputs.elevation));
    cs.SetFloat("_Moisture", Mathf.Clamp01(inputs.moisture));
    cs.SetInt("_WaterwaysPreset", inputs.waterwaysPreset);
    cs.SetFloat("_HydroRoutingJitter", gpuHydroRoutingJitter);
    cs.SetFloat("_LakeShorelineWarpPixels", gpuLakeShorelineWarpPixels);
    cs.SetFloat("_RiverMeanderPixels", gpuRiverMeanderPixels);
    cs.SetInt("_LakeMinAreaCells", gpuLakeMinAreaCells);
    cs.SetFloat("_LakeEvaporationLoss", gpuLakeEvaporationLoss);
    cs.SetFloat("_TerminalLakeAridDrop", gpuTerminalLakeAridDrop);

    // ================================================================
    // NEW: Apply preset-specific hydrology overrides
    // ================================================================
    int presetIdx = Mathf.Clamp(inputs.terrainRoughnessPresetIndex, 0, experimentalTerrainProfiles.Length - 1);
    var terrainProfile = experimentalTerrainProfiles[presetIdx];

    cs.SetFloat("_LakeBasinMinDepth", terrainProfile.lakeBasinMinDepth);
    cs.SetFloat("_LakeBasinFullDepth", terrainProfile.lakeBasinFullDepth);
    cs.SetFloat("_InlandBasinStrength", terrainProfile.inlandBasinStrength);
   cs.SetFloat("_FlowMinDownhillDelta", terrainProfile.flowMinDownhillDelta);
cs.SetFloat("_FlowAccumulationCompression", terrainProfile.flowAccumulationCompression);
cs.SetFloat("_MacroReliefStrength", terrainProfile.macroReliefStrength);
cs.SetFloat("_MacroReliefScale", terrainProfile.macroReliefScale);
cs.SetFloat("_InlandBasinScale", terrainProfile.inlandBasinScale);
cs.SetFloat("_WatershedRidgeStrength", terrainProfile.watershedRidgeStrength);
cs.SetFloat("_DrainageRiverMinWidthPixels", terrainProfile.riverMinWidthPixels);
cs.SetFloat("_DrainageRiverMaxWidthPixels", terrainProfile.riverMaxWidthPixels);

    // River accumulation threshold depends on waterways preset
    float riverThreshold = inputs.waterwaysPreset <= 0 ? terrainProfile.riverAccumulationThresholdSparse :
                           inputs.waterwaysPreset == 1 ? terrainProfile.riverAccumulationThresholdStandard :
                           terrainProfile.riverAccumulationThresholdAbundant;
    cs.SetFloat("_RiverAccumulationThreshold", riverThreshold);
    // ================================================================

    int kInitFill = cs.FindKernel("InitDrainageFill");
    int kRelaxFill = cs.FindKernel("RelaxDrainageFill");
    int kPrepare = cs.FindKernel("PrepareIndirectArgs");
    int kFlowDir = cs.FindKernel("BuildFlowDirections");
    int kInitAcc = cs.FindKernel("InitFlowAccumulation");
    int kRelaxAcc = cs.FindKernel("RelaxFlowAccumulation");
    int kInitLabels = cs.FindKernel("InitLakeLabels");
    int kRelaxLabels = cs.FindKernel("RelaxLakeLabels");
    int kClearAreas = cs.FindKernel("ClearLakeAreas");
    int kAccumAreas = cs.FindKernel("AccumulateLakeAreas");
    int kBuild = cs.FindKernel("BuildTerrainDrivenHydrologyMasks");
    int kUpsample = cs.FindKernel("UpsampleAndBeautifyHydrology");
    int kAnalyze = cs.FindKernel("AnalyzeHydrologyDebug");
    int[] allKernels = { kInitFill, kRelaxFill, kPrepare, kFlowDir, kInitAcc, kRelaxAcc, kInitLabels, kRelaxLabels, kClearAreas, kAccumAreas, kBuild, kUpsample, kAnalyze };

    int gx = Mathf.CeilToInt(analysisWidth / 8f);
    int gy = Mathf.CeilToInt(analysisHeight / 8f);
    int autoBudget = gpuHydrologyAutoIterationBudget ? analysisWidth + analysisHeight : 0;
    int fillBudget = Mathf.Max(gpuDrainageFillIterations, autoBudget);
    int accumBudget = Mathf.Max(gpuFlowAccumulationIterations, autoBudget);
    int labelBudget = Mathf.Max(64, (analysisWidth + analysisHeight) / 4);

    var phaseTimer = logHydrologyPhaseDiagnostics ? System.Diagnostics.Stopwatch.StartNew() : null;
    var phaseReport = logHydrologyPhaseDiagnostics ? new System.Text.StringBuilder() : null;

    BindHydrologyResources(cs, allKernels);

    // 1. Drainage fill (epsilon priority-flood) — converges on GPU, ping-pong A/B.
    cs.Dispatch(kInitFill, gx, gy, 1);
    RunConvergentRelaxation(cs, kRelaxFill, kPrepare, gx, gy, fillBudget, () =>
    {
        (gpuDrainageFillA, gpuDrainageFillB) = (gpuDrainageFillB, gpuDrainageFillA);
        cs.SetTexture(kRelaxFill, "_DrainageFillTexA", gpuDrainageFillA);
        cs.SetTexture(kRelaxFill, "_DrainageFillTexB", gpuDrainageFillB);
    });
    BindHydrologyResources(cs, allKernels);
    ReportHydrologyPhase(phaseReport, phaseTimer, "DrainageFill", fillBudget);

    // 2. D8 flow directions on the filled surface.
    cs.Dispatch(kFlowDir, gx, gy, 1);

    // 3. Flow accumulation over the (acyclic) D8 graph.
    cs.Dispatch(kInitAcc, gx, gy, 1);
    RunConvergentRelaxation(cs, kRelaxAcc, kPrepare, gx, gy, accumBudget, () =>
    {
        (gpuFlowAccumA, gpuFlowAccumB) = (gpuFlowAccumB, gpuFlowAccumA);
        cs.SetTexture(kRelaxAcc, "_FlowAccumTexA", gpuFlowAccumA);
        cs.SetTexture(kRelaxAcc, "_FlowAccumTexB", gpuFlowAccumB);
    });
    BindHydrologyResources(cs, allKernels);
    ReportHydrologyPhase(phaseReport, phaseTimer, "FlowAccumulation", accumBudget);

    // 4. Lake connected components + areas.
    cs.Dispatch(kInitLabels, gx, gy, 1);
    RunConvergentRelaxation(cs, kRelaxLabels, kPrepare, gx, gy, labelBudget, () =>
    {
        (lakeLabelBufferA, lakeLabelBufferB) = (lakeLabelBufferB, lakeLabelBufferA);
        cs.SetBuffer(kRelaxLabels, "_LakeLabelBufA", lakeLabelBufferA);
        cs.SetBuffer(kRelaxLabels, "_LakeLabelBufB", lakeLabelBufferB);
    });
    BindHydrologyResources(cs, allKernels);
    cs.Dispatch(kClearAreas, gx, gy, 1);
    cs.Dispatch(kAccumAreas, gx, gy, 1);
    ReportHydrologyPhase(phaseReport, phaseTimer, "LakeLabelling", labelBudget);

    // 5. Coarse masks, then full-resolution ribbons/shorelines.
    cs.Dispatch(kBuild, gx, gy, 1);
    cs.Dispatch(kUpsample, Mathf.CeilToInt(mapWidth / 8f), Mathf.CeilToInt(mapHeight / 8f), 1);
    ReportHydrologyPhase(phaseReport, phaseTimer, "Masks+Upsample", 0);

    if (phaseReport != null)
        Debug.Log($"[WorldGenV2 GPU Hydrology Timing] {phaseReport} (GPU-synchronised; debug only)");

    if (logWorldGenerationDiagnostics)
    {
        string activeThresholdType = inputs.waterwaysPreset <= 0 ? "Sparse" : (inputs.waterwaysPreset == 1 ? "Standard" : "Abundant");
        Debug.Log(
            $"[WorldGenV2 GPU Hydrology] TerrainDriven=True " +
            $"WaterwaysPreset={inputs.waterwaysPreset} ({activeThresholdType}) Moisture={inputs.moisture:0.00} " +
            $"Analysis={analysisWidth}x{analysisHeight} (divisor {gpuHydrologyAnalysisDivisor}) " +
            $"FillBudget={fillBudget} AccumBudget={accumBudget} LabelBudget={labelBudget} " +
            $"RiverThreshold={riverThreshold:0.00} (S={terrainProfile.riverAccumulationThresholdSparse:0.00} St={terrainProfile.riverAccumulationThresholdStandard:0.00} A={terrainProfile.riverAccumulationThresholdAbundant:0.00}) " +
            $"LakeDepth={terrainProfile.lakeBasinMinDepth:0.000}-{terrainProfile.lakeBasinFullDepth:0.000} MinLakeCells={gpuLakeMinAreaCells} " +
            $"RiverWidthPx={terrainProfile.riverMinWidthPixels:0.0}-{terrainProfile.riverMaxWidthPixels:0.0}");
    }
}

    private void BindHydrologyResources(ComputeShader cs, int[] kernels)
    {
        foreach (int k in kernels)
        {
            cs.SetTexture(k, "_GpuHydrologyTex", gpuHydrologyTexture);
            cs.SetTexture(k, "_GpuHydrologyDepthTex", gpuHydrologyDepthTexture);
            cs.SetTexture(k, "_HydroTerrainTex", gpuHydroTerrainTexture);
            cs.SetTexture(k, "_DrainageFillTexA", gpuDrainageFillA);
            cs.SetTexture(k, "_DrainageFillTexB", gpuDrainageFillB);
            cs.SetTexture(k, "_HydroFlowTex", gpuFlowDirectionTexture);
            cs.SetTexture(k, "_FlowAccumTexA", gpuFlowAccumA);
            cs.SetTexture(k, "_FlowAccumTexB", gpuFlowAccumB);
            cs.SetTexture(k, "_CoarseHydrologyMaskTex", gpuCoarseHydrologyMaskTexture);
            cs.SetTexture(k, "_CoarseHydrologyDepthTex", gpuCoarseHydrologyDepthTexture);
            cs.SetTexture(k, "_TectonicSurfaceTex", TectonicSurfaceTexture);
            cs.SetTexture(k, "_GpuHeightTex", gpuHeightTexture);
            cs.SetTexture(k, "_GpuSignedHeightTex", gpuDisplacementTexture);
            cs.SetBuffer(k, "_LakeLabelBufA", lakeLabelBufferA);
            cs.SetBuffer(k, "_LakeLabelBufB", lakeLabelBufferB);
            cs.SetBuffer(k, "_LakeAreaBuf", lakeAreaBuffer);
            cs.SetBuffer(k, "_ConvergenceState", hydroConvergenceBuffer);
            cs.SetBuffer(k, "_IndirectArgs", hydroIndirectArgsBuffer);
            cs.SetBuffer(k, "_DebugStats", hydroDebugStatsBuffer);
        }
    }

    // Jacobi relaxation with a GPU-side convergence flag. The first pass is a full dispatch; afterwards the
    // prepare kernel shrinks the indirect dispatch to a single group (and deactivates the relax kernel) once
    // a pass changes nothing. No CPU readback is involved, so the loop costs only command submission.
    private void RunConvergentRelaxation(ComputeShader cs, int relaxKernel, int prepareKernel, int groupsX, int groupsY, int maxIterations, Action swapBuffers)
    {
        hydroConvergenceBuffer.SetData(ConvergenceReset);
        for (int i = 0; i < maxIterations; i++)
        {
            if (i == 0) cs.Dispatch(relaxKernel, groupsX, groupsY, 1);
            else cs.DispatchIndirect(relaxKernel, hydroIndirectArgsBuffer, 0);
            cs.Dispatch(prepareKernel, 1, 1, 1);
            swapBuffers();
        }
    }

    private void ReportHydrologyPhase(System.Text.StringBuilder report, System.Diagnostics.Stopwatch timer, string phase, int budget)
    {
        if (report == null || timer == null) return;
        // Debug only: a synchronous readback of the tiny convergence buffer forces the GPU to finish the phase.
        var request = AsyncGPUReadback.Request(hydroConvergenceBuffer);
        request.WaitForCompletion();
        uint passes = request.hasError ? 0u : request.GetData<uint>()[1];
        report.Append(phase).Append('=').Append(timer.ElapsedMilliseconds).Append("ms");
        if (budget > 0) report.Append(" (").Append(passes).Append('/').Append(budget).Append(passes >= (uint)budget ? " passes, BUDGET HIT)" : " passes)");
        report.Append("; ");
        timer.Restart();
    }

    // concise but complete implementation
    private void GenerateTopologyFields(out float[] topoLandDist,out float[] topoOceanDist,out float[] topoSignedCoastDistance,out GenDiagnostics diag){
        int tw=Mathf.Max(16,topologyWidth),th=Mathf.Max(8,topologyHeight),tn=tw*th; diag=default;
        int presetIndex=Mathf.Clamp(inputs.landPresetIndex,0,presets.Length-1); var preset=presets[presetIndex]; diag.preset=preset.name;
        float sizeBias=Mathf.Clamp01(1f-inputs.landThreshold); float shapeBias=Mathf.InverseLerp(0.5f,5f,inputs.landScale); diag.targetLand=0f;
        TopologyCell[] topo=new TopologyCell[tn];
        int seedBase = Mathf.RoundToInt(inputs.seed * 1000f);
        EnsureTopologyShapeNoiseField(seedBase, tw, th);
        var r=new System.Random((seedBase*73856093) ^ 19349663);
        var plans=BuildLandmassPlan(preset,tw,th,r,sizeBias,shapeBias,out var planBuildDiag);
        RasterizeLandmassPlansToTopology(topo,plans,preset,tw,th,shapeBias);
        diag.targetTopologyLandCells = planBuildDiag.totalPlannedTargetCells;
        diag.topologyLandCells = CountTopologyLandCells(topo);
        diag.topologyCoverage = tn > 0 ? (float)diag.topologyLandCells / tn : 0f;
        int[] comp; diag.largestGroupShare=LargestLandmassShare(topo,tw,th,out diag.groupCount,out comp);
        bool[] topoLand = new bool[tn]; for (int i = 0; i < tn; i++) topoLand[i] = topo[i].isLand;
        topoLandDist = DistanceFromBoundaryTopology(topoLand, tw, th, true);
        topoOceanDist = DistanceFromBoundaryTopology(topoLand, tw, th, false);
        topoSignedCoastDistance = BuildSignedTopologyCoastDistance(topoLand, topoLandDist, topoOceanDist);
        diag.actualLand = diag.topologyCoverage;
    }

    private void EnsureTopologyShapeNoiseField(int seedBase, int topologyW, int topologyH)
    {
        int tw = Mathf.Max(8, topologyW);
        int th = Mathf.Max(4, topologyH);
        bool needsRebuild = !topologyShapeNoiseValid || cachedNoiseSeed != seedBase || cachedTopologyWidth != tw || cachedTopologyHeight != th;
        if (!needsRebuild) return;
        cachedTopologyShapeNoise = new float[tw * th];
        var topologyShapeNoise = new FastNoiseLite(seedBase + 7707);
        topologyShapeNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        topologyShapeNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        topologyShapeNoise.SetFractalOctaves(3); topologyShapeNoise.SetFractalLacunarity(2f); topologyShapeNoise.SetFractalGain(0.5f); topologyShapeNoise.SetFrequency(1.65f);
        for (int y = 0; y < th; y++) for (int x = 0; x < tw; x++)
        {
            int idx = y * tw + x;
            float u = (x + 0.5f) / tw; float v = (y + 0.5f) / th;
            float longitude = (u * Mathf.PI * 2f) - Mathf.PI; float latitude = (0.5f - v) * Mathf.PI;
            float cosLat = Mathf.Cos(latitude); float dx = cosLat * Mathf.Cos(longitude); float dy = Mathf.Sin(latitude); float dz = cosLat * Mathf.Sin(longitude);
            cachedTopologyShapeNoise[idx] = topologyShapeNoise.GetNoise(dx, dy, dz);
        }
        cachedNoiseSeed = seedBase; cachedTopologyWidth = tw; cachedTopologyHeight = th; topologyShapeNoiseValid = true;
    }

    private int CountTopologyLandCells(TopologyCell[] topo){
        int c=0; for(int i=0;i<topo.Length;i++) if(topo[i].isLand) c++; return c;
    }

    private float LargestLandmassShare(TopologyCell[] topo, int w, int h, out int groupCount, out int[] componentIds)
    {
        int n = topo.Length;
        componentIds = new int[n];
        if (n == 0 || w <= 0 || h <= 0)
        {
            groupCount = 0;
            return 0f;
        }

        int landCells = 0;
        for (int i = 0; i < n; i++) if (topo[i].isLand) landCells++;
        if (landCells == 0)
        {
            groupCount = 0;
            return 0f;
        }

        int nextComponentId = 1;
        int largest = 0;
        var queue = new Queue<int>();
        int[] ox = { -1, 0, 1, -1, 1, -1, 0, 1 };
        int[] oy = { -1, -1, -1, 0, 0, 1, 1, 1 };

        for (int i = 0; i < n; i++)
        {
            if (!topo[i].isLand || componentIds[i] != 0) continue;

            componentIds[i] = nextComponentId;
            queue.Enqueue(i);
            int componentSize = 0;

            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                componentSize++;
                int cx = c % w;
                int cy = c / w;
                for (int d = 0; d < 8; d++)
                {
                    int ny = cy + oy[d];
                    if (ny < 0 || ny >= h) continue;
                    int nx = (cx + ox[d] + w) % w;
                    int ni = ny * w + nx;
                    if (!topo[ni].isLand || componentIds[ni] != 0) continue;
                    componentIds[ni] = nextComponentId;
                    queue.Enqueue(ni);
                }
            }

            if (componentSize > largest) largest = componentSize;
            nextComponentId++;
        }

        groupCount = nextComponentId - 1;
        return landCells > 0 ? (float)largest / landCells : 0f;
    }

    private List<LandmassPlan> BuildLandmassPlan(PreviewLandPresetProfileV3 preset,int tw,int th,System.Random r,float sizeBias,float shapeBias,out LandmassPlanBuildDiagnostics diagnostics){
        var buildDiagnostics = default(LandmassPlanBuildDiagnostics);
        var plans=new List<LandmassPlan>(); int groupId=1; int tn=tw*th;
        void AddBand(LandmassKind kind, LandmassBand band){
            int count=r.Next(Mathf.Max(0,band.minCount),Mathf.Max(band.minCount,band.maxCount)+1);
            if(kind==LandmassKind.MajorContinent) buildDiagnostics.requestedMajorCount += count; else if(kind==LandmassKind.LargeIsland) buildDiagnostics.requestedLargeIslandCount += count; else buildDiagnostics.requestedSmallClusterCount += count;
            for(int i=0;i<count;i++){
                float areaFrac=Mathf.Lerp(band.minAreaFraction,band.maxAreaFraction,Mathf.Clamp01(sizeBias*0.75f+(float)r.NextDouble()*0.25f));
                int target=Mathf.Max(1,Mathf.RoundToInt(areaFrac*tn));
                float radius=Mathf.Sqrt(target/Mathf.PI);
                if(!TryPlaceLandmassCenter(plans,kind,radius,preset,tw,th,r,out var center)) { buildDiagnostics.failedCenterPlacements++; continue; }
                float ang=(float)(r.NextDouble()*Math.PI*2); var elong=new Vector2(Mathf.Cos(ang),Mathf.Sin(ang));
                int lobes=Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(band.minLobes,band.maxLobes,shapeBias)),band.minLobes,band.maxLobes);
                plans.Add(new LandmassPlan{kind=kind,targetCellCount=target,estimatedRadiusCells=radius,lobeCount=lobes,groupId=groupId++,center=center,elongationDirection=elong,lobes=BuildLandmassLobes(kind,center,radius,lobes,elong,tw,th,r)});
                if(kind==LandmassKind.MajorContinent) buildDiagnostics.builtMajorCount++; else if(kind==LandmassKind.LargeIsland) buildDiagnostics.builtLargeIslandCount++; else buildDiagnostics.builtSmallClusterCount++;
                buildDiagnostics.totalPlannedTargetCells += target;
            }
        }
        AddBand(LandmassKind.MajorContinent,preset.majorLandmasses); AddBand(LandmassKind.LargeIsland,preset.largeIslands); AddBand(LandmassKind.SmallIslandCluster,preset.smallIslandClusters);
        diagnostics = buildDiagnostics;
        return plans;
    }
    private bool TryPlaceLandmassCenter(List<LandmassPlan> existingPlans,LandmassKind newKind,float estimatedRadiusCells,PreviewLandPresetProfileV3 preset,int tw,int th,System.Random r,out Vector2Int center){
        for(int a=0;a<96;a++){
            var c=new Vector2Int(r.Next(tw),r.Next(th)); bool ok=true; Vector3 dirC=TopologyToDirection(c,tw,th);
            foreach(var e in existingPlans){ float d=TopologyCellDistance(c,e.center,tw,th);
                if(newKind==LandmassKind.MajorContinent && e.kind==LandmassKind.MajorContinent){
                    if(Vector3.Angle(dirC,TopologyToDirection(e.center,tw,th))<preset.minMajorSeedSeparationDegrees){ok=false;break;}
                    if(d<estimatedRadiusCells+e.estimatedRadiusCells+preset.minMajorOceanGapCells){ok=false;break;}
                }
                if((newKind==LandmassKind.LargeIsland||newKind==LandmassKind.SmallIslandCluster) && e.kind==LandmassKind.MajorContinent && d<e.estimatedRadiusCells*0.9f){ok=false;break;}
            }
            if(ok){center=c; return true;}
        }
        center=default; return false;
    }
    private Vector3 TopologyToDirection(Vector2Int c,int tw,int th){float u=(c.x+0.5f)/tw,v=(c.y+0.5f)/th; float lon=(u*Mathf.PI*2f)-Mathf.PI,lat=(0.5f-v)*Mathf.PI; float cl=Mathf.Cos(lat); return new Vector3(cl*Mathf.Cos(lon),Mathf.Sin(lat),cl*Mathf.Sin(lon));}
    private float TopologyCellDistance(Vector2Int a,Vector2Int b,int tw,int th){ float dx=Mathf.Min(Mathf.Abs(a.x-b.x),tw-Mathf.Abs(a.x-b.x)); float dy=Mathf.Abs(a.y-b.y); return Mathf.Sqrt(dx*dx+dy*dy);}
    private List<LandmassLobe> BuildLandmassLobes(LandmassKind kind,Vector2Int center,float estimatedRadiusCells,int lobeCount,Vector2 elongationDirection,int tw,int th,System.Random r){
        var lobes=new List<LandmassLobe>();
        float baseRadius=Mathf.Max(2f,estimatedRadiusCells);
        lobes.Add(new LandmassLobe{center=new Vector2(center.x,center.y),radiusX=baseRadius*Mathf.Lerp(0.75f,1.05f,(float)r.NextDouble()),radiusY=baseRadius*Mathf.Lerp(0.65f,1.00f,(float)r.NextDouble()),rotationRadians=(float)(r.NextDouble()*Math.PI*2),weight=1f});
        int additional=Mathf.Max(0,lobeCount-1);
        float kindTightness=kind==LandmassKind.MajorContinent?1f:kind==LandmassKind.LargeIsland?0.8f:0.62f;
        for(int i=0;i<additional;i++){
            float align=(float)r.NextDouble()*2f-1f;
            float baseAngle=Mathf.Atan2(elongationDirection.y,elongationDirection.x);
            float angle=baseAngle+align*Mathf.Lerp(0.35f,1.45f,1f-kindTightness);
            float dist=baseRadius*Mathf.Lerp(0.35f,0.90f*kindTightness+0.15f,(float)r.NextDouble());
            Vector2 lobeCenter=new Vector2((center.x+Mathf.Cos(angle)*dist+tw)%tw,Mathf.Clamp(center.y+Mathf.Sin(angle)*dist,0,th-1));
            lobes.Add(new LandmassLobe{center=lobeCenter,radiusX=baseRadius*Mathf.Lerp(0.35f,0.75f,(float)r.NextDouble()),radiusY=baseRadius*Mathf.Lerp(0.30f,0.70f,(float)r.NextDouble()),rotationRadians=(float)(r.NextDouble()*Math.PI*2),weight=Mathf.Lerp(0.65f,0.95f,(float)r.NextDouble())});
        }
        return lobes;
    }

    private void RasterizeLandmassPlansToTopology(TopologyCell[] topo,List<LandmassPlan> plans,PreviewLandPresetProfileV3 preset,int tw,int th,float shapeBias){
        for(int y=0;y<th;y++) for(int x=0;x<tw;x++){
            int idx=y*tw+x; float bestField=float.NegativeInfinity; int bestGroup=-1; bool forceOcean=false;
            float topMajor=float.NegativeInfinity, secondMajor=float.NegativeInfinity;
            foreach(var plan in plans){
                float planField=EvaluateLandmassPlanField(new Vector2(x,y),plan,tw,th,shapeBias);
                if(plan.kind==LandmassKind.MajorContinent){ if(planField>topMajor){secondMajor=topMajor; topMajor=planField;} else if(planField>secondMajor){secondMajor=planField;} }
                if(planField>bestField){bestField=planField; bestGroup=plan.groupId;}
            }
            if(!preset.allowMajorLandmassMerging && topMajor>0f && secondMajor>0f && Mathf.Abs(topMajor-secondMajor)<0.10f) forceOcean=true;
            float noise01=cachedTopologyShapeNoise[idx];
            float noiseStrength=Mathf.Lerp(0.10f,0.36f,shapeBias)*preset.irregularityBias;
            bestField += noise01*noiseStrength;
            bool isLand=!forceOcean && bestField>0f;
            topo[idx].isLand=isLand; topo[idx].groupId=isLand?bestGroup:-1;
        }
    }

    private float EvaluateLandmassPlanField(Vector2 cell,LandmassPlan plan,int tw,int th,float shapeBias){
        float field=float.NegativeInfinity;
        for(int i=0;i<plan.lobes.Count;i++){
            var lobe=plan.lobes[i];
            float dx=cell.x-lobe.center.x; if(Mathf.Abs(dx)>tw*0.5f) dx-=Mathf.Sign(dx)*tw;
            float dy=cell.y-lobe.center.y;
            float c=Mathf.Cos(-lobe.rotationRadians), s=Mathf.Sin(-lobe.rotationRadians);
            float rx=dx*c-dy*s, ry=dx*s+dy*c;
            float nx=rx/Mathf.Max(0.01f,lobe.radiusX), ny=ry/Mathf.Max(0.01f,lobe.radiusY);
            float d=Mathf.Sqrt(nx*nx+ny*ny);
            float lobeField=(1f-d)*lobe.weight;
            field=float.IsNegativeInfinity(field)?lobeField:SmoothMax(field,lobeField,0.20f);
        }
        return field;
    }

    private float SmoothMax(float a, float b, float k)
    {
        k = Mathf.Max(0.0001f, k);

        float h = Mathf.Clamp01(
            0.5f + 0.5f * (a - b) / k
        );

        return Mathf.Lerp(b, a, h) + k * h * (1f - h);
    }

    private float[] DistanceFromBoundary(float[] land,float threshold,bool forLand){
        int n=land.Length; float[] dist=new float[n]; for(int i=0;i<n;i++) dist[i]=99999f; var q=new Queue<int>();
        for(int y=0;y<mapHeight;y++) for(int x=0;x<mapWidth;x++){int i=y*mapWidth+x; bool isLand=land[i]>threshold; if(isLand!=forLand) continue; bool near=false; for(int d=0;d<4;d++){int nx=(x+(d==0?1:d==1?-1:0)+mapWidth)%mapWidth, ny=y+(d==2?1:d==3?-1:0); if(ny<0||ny>=mapHeight){near=true;break;} bool nLand=land[ny*mapWidth+nx]>threshold; if(nLand!=isLand){near=true;break;}} if(near){dist[i]=0f; q.Enqueue(i);}}
        while(q.Count>0){int c=q.Dequeue(); int x=c%mapWidth,y=c/mapWidth; float baseD=dist[c]; for(int d=0;d<4;d++){int nx=(x+(d==0?1:d==1?-1:0)+mapWidth)%mapWidth, ny=y+(d==2?1:d==3?-1:0); if(ny<0||ny>=mapHeight) continue; int ni=ny*mapWidth+nx; bool nLand=land[ni]>threshold; if(nLand!=forLand) continue; if(dist[ni]>baseD+1f){dist[ni]=baseD+1f;q.Enqueue(ni);}}}
        return dist;
    }
    private float[] DistanceFromBoundaryTopology(bool[] landTopo, int w, int h, bool forLand)
    {
        const float inf = 1e9f;
        const float diagonalCost = 1.41421356f;
        int n = w * h;
        float[] dist = new float[n];
        for (int i = 0; i < n; i++) dist[i] = landTopo[i] == forLand ? inf : 0f;
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            int i = y * w + x;
            if (landTopo[i] != forLand) continue;
            bool near = false;
            for (int oy = -1; oy <= 1 && !near; oy++) for (int ox = -1; ox <= 1; ox++)
            {
                if (ox == 0 && oy == 0) continue;
                int ny = y + oy;
                if (ny < 0 || ny >= h) { near = true; break; }
                int nx = (x + ox + w) % w;
                if (landTopo[ny * w + nx] != forLand) { near = true; break; }
            }
            if (near) dist[i] = 0f;
        }
        int[] oxs = { -1, 0, 1, -1, 1, -1, 0, 1 };
        int[] oys = { -1, -1, -1, 0, 0, 1, 1, 1 };
        for (int pass = 0; pass < 10; pass++)
        {
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) Relax(x, y);
            for (int y = h - 1; y >= 0; y--) for (int x = w - 1; x >= 0; x--) Relax(x, y);
        }
        void Relax(int x, int y)
        {
            int i = y * w + x;
            if (landTopo[i] != forLand) return;
            float best = dist[i];
            for (int d = 0; d < 8; d++)
            {
                int ny = y + oys[d];
                if (ny < 0 || ny >= h) continue;
                int nx = (x + oxs[d] + w) % w;
                int ni = ny * w + nx;
                if (landTopo[ni] != forLand) continue;
                float c = (oxs[d] == 0 || oys[d] == 0) ? 1f : diagonalCost;
                best = Mathf.Min(best, dist[ni] + c);
            }
            dist[i] = best;
        }
        return dist;
    }
    private float[] BuildSignedTopologyCoastDistance(bool[] topoLand, float[] topoLandDist, float[] topoOceanDist)
    {
        int n = topoLand.Length;
        float[] signed = new float[n];
        for (int i = 0; i < n; i++)
        {
            if (topoLand[i]) signed[i] = topoLandDist[i] + 0.5f;
            else signed[i] = -(topoOceanDist[i] + 0.5f);
        }
        return signed;
    }
}
