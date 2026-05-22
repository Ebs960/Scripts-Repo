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
    [SerializeField, Range(1f, 24f)] private float coastlineDeformationWidthCells = 10f;
    [Tooltip("Large-scale FastNoiseLite authority over the coastline. Higher values let noise meaningfully push coastlines inland or outward.")]
    [SerializeField, Range(0f, 2f)] private float coastlineWarpStrength = 0.95f;
    [Tooltip("Medium-scale coastline sculpting. Controls bays, coastal shoulders, peninsulas, and major coastline irregularity between macro shape and fine shoreline detail.")]
    [SerializeField, Range(0f, 1.5f)] private float coastlineMidNoiseStrength = 0.65f;
    [Tooltip("Fine-scale FastNoiseLite coastal roughness. This adds smaller shoreline detail, but should stay lower than macro warp strength.")]
    [SerializeField, Range(0f, 1f)] private float coastlineEdgeNoiseStrength = 0.28f;
    [Tooltip("Soft threshold width used when turning the coastline signal into the final land mask.")]
    [SerializeField, Range(0.001f, 0.20f)] private float coastlineSoftness = 0.045f;
    [Tooltip("Shifts final coastline threshold. Positive values reduce land slightly; negative values expand land slightly. Leave near 0 unless final FastNoise coastline consistently adds/removes too much land.")]
    [SerializeField, Range(-0.75f, 0.75f)] private float coastlineThresholdBias = 0f;
    [SerializeField] private bool logWorldGenerationDiagnostics = true;

    [Header("GPU Coastline")]
    [SerializeField] private bool useGpuCoastlinePreview = true;
    [SerializeField] private ComputeShader menuPlanetPreviewCoastlineCompute;

    [Header("GPU Height")]
    [SerializeField] private bool useGpuHeightPreview = true;
    [SerializeField] private ComputeShader menuPlanetPreviewHeightCompute;

    [Header("GPU Hydrology")]
    [SerializeField] private bool useGpuHydrologyPreview = true;
    [SerializeField] private ComputeShader menuPlanetPreviewHydrologyCompute;
    [SerializeField, Range(32, 384)] private int gpuDrainageFillIterations = 192;
    [SerializeField, Range(32, 384)] private int gpuFlowAccumulationIterations = 192;
    [SerializeField, Range(0f, 0.08f)] private float gpuHydroRoutingJitter = 0.015f;
    [SerializeField, Range(1f, 10f)] private float gpuDrainageRiverMinWidthPixels = 2.0f;
    [SerializeField, Range(2f, 18f)] private float gpuDrainageRiverMaxWidthPixels = 6.5f;
    [SerializeField, Range(0f, 0.2f)] private float gpuLakeBasinMinDepth = 0.018f;
    [SerializeField, Range(0f, 0.4f)] private float gpuLakeBasinFullDepth = 0.065f;
    [SerializeField, Range(0f, 6f)] private float gpuLakeShorelineWarpPixels = 1.5f;
    [SerializeField, Range(0f, 1f)] private float gpuRiverAccumulationThresholdSparse = 0.58f;
    [SerializeField, Range(0f, 1f)] private float gpuRiverAccumulationThresholdStandard = 0.45f;
    [SerializeField, Range(0f, 1f)] private float gpuRiverAccumulationThresholdAbundant = 0.32f;
    [SerializeField, Range(0f, 0.05f)] private float gpuFlowMinDownhillDelta = 0.004f;
    [SerializeField, Range(0.01f, 0.6f)] private float gpuFlowAccumulationCompression = 0.12f;
    [SerializeField, Range(0f, 0.25f)] private float gpuMacroReliefStrength = 0.07f;
    [SerializeField, Range(0.25f, 4f)] private float gpuMacroReliefScale = 1.25f;
    [SerializeField, Range(0f, 0.20f)] private float gpuInlandBasinStrength = 0.045f;
    [SerializeField, Range(0.25f, 4f)] private float gpuInlandBasinScale = 1.0f;
    [SerializeField, Range(0f, 0.15f)] private float gpuWatershedRidgeStrength = 0.025f;
    [Header("Terrain Roughness Type Elevation Multipliers")]
    [SerializeField, Range(0.2f, 2.0f)] private float flatElevationMultiplier = 0.8f;
    [SerializeField, Range(0.2f, 2.0f)] private float smoothElevationMultiplier = 0.9f;
    [SerializeField, Range(0.2f, 2.0f)] private float standardElevationMultiplier = 1.0f;
    [SerializeField, Range(0.2f, 2.0f)] private float mountainousElevationMultiplier = 1.15f;
    [SerializeField, Range(0.2f, 2.0f)] private float alpineElevationMultiplier = 1.25f;

[Header("Experimental Signed Basin Hydrology")]
    [SerializeField] private bool useExperimentalSignedTerrain = false;
    [SerializeField] private bool useExperimentalBasinHydrology = false;

    [SerializeField, Range(0, 16)] private int experimentalSparseLakeCount = 2;
    [SerializeField, Range(0, 16)] private int experimentalStandardLakeCount = 4;
    [SerializeField, Range(0, 16)] private int experimentalAbundantLakeCount = 7;

    [SerializeField, Range(0, 24)] private int experimentalSparseRiverAttempts = 3;
    [SerializeField, Range(0, 24)] private int experimentalStandardRiverAttempts = 7;
    [SerializeField, Range(0, 24)] private int experimentalAbundantRiverAttempts = 12;

    [SerializeField, Range(0f, 3f)] private float experimentalMoistureLakeMultiplier = 0.75f;
    [SerializeField, Range(0f, 3f)] private float experimentalMoistureRiverMultiplier = 1.0f;
    [SerializeField, Range(0f, 3f)] private float experimentalWetlandSpread = 1.0f;

    [SerializeField, Range(0f, 1f)] private float experimentalMinBasinScore = 0.20f;
    [SerializeField, Range(1, 64)] private int experimentalMinBasinPixelArea = 8;
    [SerializeField, Range(4f, 80f)] private float experimentalMinBasinSeparationPixels = 18f;

    [SerializeField, Range(0f, 1f)] private float experimentalRiverMeanderStrength = 0.25f;
    [SerializeField, Range(0f, 1f)] private float experimentalRiverValleyBias = 0.65f;
    [SerializeField, Range(0f, 1f)] private float experimentalLakeToCoastChance = 0.75f;

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
    }

    [SerializeField] private ExperimentalTerrainProfile[] experimentalTerrainProfiles =
    {
        new ExperimentalTerrainProfile { name = "Flat", heightBias = -0.03f, upwardReliefStrength = 0.06f, downwardBasinStrength = 0.16f, valleyCutStrength = 0.05f, broadNoiseAmplitude = 0.14f, midNoiseAmplitude = 0.035f, fineNoiseAmplitude = 0.008f, broadNoiseScale = 0.55f, midNoiseScale = 1.10f, fineNoiseScale = 2.00f, ridgeStrength = 0.005f, hillThreshold = 0.72f, mountainThreshold = 0.98f, basinCandidateBias = 1.25f },
        new ExperimentalTerrainProfile { name = "Smooth", heightBias = -0.015f, upwardReliefStrength = 0.10f, downwardBasinStrength = 0.14f, valleyCutStrength = 0.07f, broadNoiseAmplitude = 0.16f, midNoiseAmplitude = 0.055f, fineNoiseAmplitude = 0.012f, broadNoiseScale = 0.70f, midNoiseScale = 1.30f, fineNoiseScale = 2.30f, ridgeStrength = 0.015f, hillThreshold = 0.62f, mountainThreshold = 0.93f, basinCandidateBias = 1.15f },
        new ExperimentalTerrainProfile { name = "Standard", heightBias = 0.00f, upwardReliefStrength = 0.18f, downwardBasinStrength = 0.13f, valleyCutStrength = 0.10f, broadNoiseAmplitude = 0.20f, midNoiseAmplitude = 0.09f, fineNoiseAmplitude = 0.02f, broadNoiseScale = 0.90f, midNoiseScale = 1.70f, fineNoiseScale = 3.00f, ridgeStrength = 0.05f, hillThreshold = 0.50f, mountainThreshold = 0.82f, basinCandidateBias = 1.00f },
        new ExperimentalTerrainProfile { name = "Mountainous", heightBias = 0.03f, upwardReliefStrength = 0.32f, downwardBasinStrength = 0.15f, valleyCutStrength = 0.16f, broadNoiseAmplitude = 0.24f, midNoiseAmplitude = 0.14f, fineNoiseAmplitude = 0.035f, broadNoiseScale = 1.10f, midNoiseScale = 2.20f, fineNoiseScale = 4.00f, ridgeStrength = 0.14f, hillThreshold = 0.38f, mountainThreshold = 0.68f, basinCandidateBias = 0.90f },
        new ExperimentalTerrainProfile { name = "Alpine", heightBias = 0.05f, upwardReliefStrength = 0.48f, downwardBasinStrength = 0.18f, valleyCutStrength = 0.22f, broadNoiseAmplitude = 0.28f, midNoiseAmplitude = 0.20f, fineNoiseAmplitude = 0.05f, broadNoiseScale = 1.25f, midNoiseScale = 2.80f, fineNoiseScale = 5.00f, ridgeStrength = 0.25f, hillThreshold = 0.28f, mountainThreshold = 0.54f, basinCandidateBias = 0.75f }
    };


    Texture2D surfaceDataTexture, auxiliaryMaskTexture, worldStructureTexture;
    public Texture TectonicSurfaceTexture => useGpuCoastlinePreview ? gpuTectonicSurfaceTexture : surfaceDataTexture;
    public Texture TectonicBoundaryTexture => useGpuCoastlinePreview ? gpuTectonicBoundaryTexture : auxiliaryMaskTexture;
    public Texture TectonicCrustTexture => useGpuCoastlinePreview ? gpuTectonicCrustTexture : worldStructureTexture;
    public Texture ActiveHydrologyTexture => gpuHydrologyTexture;
    public Texture ActiveHydrologyDepthTexture => gpuHydrologyDepthTexture;
    public RenderTexture GpuHeightTexture => gpuHeightTexture;
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
    private RenderTexture gpuTectonicBoundaryTexture;
    private RenderTexture gpuTectonicCrustTexture;
    private ComputeBuffer topoLandCoastDistanceBuffer;
    private ComputeBuffer topoOceanCoastDistanceBuffer;
    private ComputeBuffer topoSignedCoastDistanceBuffer;
    private int gpuCoastlineKernel = -1;
    private RenderTexture gpuHeightTexture;
    private RenderTexture gpuHydrologyTexture;
    private RenderTexture gpuHydrologyDepthTexture, gpuDrainageFillA, gpuDrainageFillB, gpuFlowDirectionTexture, gpuFlowAccumA, gpuFlowAccumB, gpuCoarseHydrologyMaskTexture, gpuCoarseHydrologyDepthTexture;
    private RenderTexture gpuExperimentalBasinCandidateTexture, gpuExperimentalSelectedBasinTexture, gpuExperimentalRiverPathTexture;
    private int gpuHeightKernel = -1;
    private bool validateCacheInitialized;
    private float lastValidatedGpuMacroReliefStrength;
    private float lastValidatedGpuMacroReliefScale;
    private float lastValidatedGpuInlandBasinStrength;
    private float lastValidatedGpuInlandBasinScale;
    private float lastValidatedGpuWatershedRidgeStrength;
    private int lastValidatedGpuDrainageFillIterations;
    private int lastValidatedGpuFlowAccumulationIterations;
    private float lastValidatedGpuHydroRoutingJitter;
    private float lastValidatedGpuDrainageRiverMinWidthPixels;
    private float lastValidatedGpuDrainageRiverMaxWidthPixels;
    private float lastValidatedGpuLakeBasinMinDepth;
    private float lastValidatedGpuLakeBasinFullDepth;
    private float lastValidatedGpuLakeShorelineWarpPixels;
    private float lastValidatedGpuRiverAccumulationThresholdSparse;
    private float lastValidatedGpuRiverAccumulationThresholdStandard;
    private float lastValidatedGpuRiverAccumulationThresholdAbundant;
    private float lastValidatedGpuFlowMinDownhillDelta;
    private float lastValidatedGpuFlowAccumulationCompression;
    private bool lastValidatedUseExperimentalSignedTerrain;
    private bool lastValidatedUseExperimentalBasinHydrology;
    private float lastValidatedExperimentalMinBasinScore;
    private int lastValidatedExperimentalMinBasinPixelArea;
    private float lastValidatedExperimentalMinBasinSeparationPixels;
    private int lastValidatedExperimentalSparseLakeCount;
    private int lastValidatedExperimentalStandardLakeCount;
    private int lastValidatedExperimentalAbundantLakeCount;
    private int lastValidatedExperimentalSparseRiverAttempts;
    private int lastValidatedExperimentalStandardRiverAttempts;
    private int lastValidatedExperimentalAbundantRiverAttempts;
    private float lastValidatedExperimentalWetlandSpread;




    [ContextMenu("Log GPU Terrain/Hydrology Coverage Stats")]
    public void LogGpuCoverageStats()
    {
        if (gpuHeightTexture == null || gpuHydrologyTexture == null)
        {
            Debug.LogWarning("[MenuPlanetPreviewWorldGeneratorV2] GPU textures are not ready. Generate preview first.");
            return;
        }

        AsyncGPUReadback.Request(gpuHeightTexture, 0, request =>
        {
            if (request.hasError)
            {
                Debug.LogWarning("[MenuPlanetPreviewWorldGeneratorV2] Height readback failed.");
                return;
            }

            var heightData = request.GetData<Vector4>();
            int count = heightData.Length;
            if (count <= 0) return;

            float minHeight = float.MaxValue;
            float maxHeight = float.MinValue;
            double sumHeight = 0.0;
            int hillCount = 0;
            int mountainCount = 0;

            for (int i = 0; i < count; i++)
            {
                Vector4 h = heightData[i];
                float finalHeight = h.x;
                minHeight = Mathf.Min(minHeight, finalHeight);
                maxHeight = Mathf.Max(maxHeight, finalHeight);
                sumHeight += finalHeight;
                if (h.y > 0.05f) hillCount++;
                if (h.z > 0.05f) mountainCount++;
            }

            AsyncGPUReadback.Request(gpuHydrologyTexture, 0, hydroReq =>
            {
                if (hydroReq.hasError)
                {
                    Debug.LogWarning("[MenuPlanetPreviewWorldGeneratorV2] Hydrology readback failed.");
                    return;
                }

                var hydroData = hydroReq.GetData<Vector4>();
                int hydroCount = hydroData.Length;
                if (hydroCount <= 0) return;

                int lakeCount = 0;
                int riverCount = 0;
                int wetlandCount = 0;

                for (int i = 0; i < hydroCount; i++)
                {
                    Vector4 w = hydroData[i];
                    if (w.x > 0.05f) riverCount++;
                    if (w.y > 0.05f) lakeCount++;
                    if (w.z > 0.05f) wetlandCount++;
                }

                float invHeight = 1f / count;
                float invHydro = 1f / hydroCount;
                float hillCoverage = hillCount * invHeight * 100f;
                float mountainCoverage = mountainCount * invHeight * 100f;
                float lakeCoverage = lakeCount * invHydro * 100f;
                float riverCoverage = riverCount * invHydro * 100f;
                float wetlandCoverage = wetlandCount * invHydro * 100f;
                float meanHeight = (float)(sumHeight * invHeight);

                Debug.Log($"[MenuPlanetPreviewWorldGeneratorV2] GPU stats | Height min/max/mean: {minHeight:F4}/{maxHeight:F4}/{meanHeight:F4} | Hill coverage: {hillCoverage:F2}% | Mountain coverage: {mountainCoverage:F2}% | Lake: {lakeCoverage:F2}% | River: {riverCoverage:F2}% | Wetland: {wetlandCoverage:F2}%");
            });
        });
    }

    public void SetInputs(MenuPlanetPreviewWorldInputs v) => inputs = v;
    public void RefreshGpuHeightOnly(MenuPlanetPreviewWorldInputs updatedInputs)
    {
        inputs = updatedInputs;
        DispatchGpuHeight();
    }

    public void RefreshGpuHydrologyOnly(MenuPlanetPreviewWorldInputs updatedInputs)
    {
        inputs = updatedInputs;
        DispatchGpuHydrology();
    }
    public void RequestRebuild(PreviewWorldRebuildScope scope, bool immediate = false) {
        pending |= ExpandDependencies(scope);
        generationVersion++;
        scheduledAt = immediate ? Time.time : Time.time + regenerationDelay;
    }
    public void Release()
    {
        DestroyTex(ref surfaceDataTexture);
        DestroyTex(ref auxiliaryMaskTexture);
        DestroyTex(ref worldStructureTexture);
        ReleaseGpuCoastlineResources();
        ReleaseGpuHeightTexture();
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
        return !Mathf.Approximately(gpuMacroReliefStrength, lastValidatedGpuMacroReliefStrength) ||
               !Mathf.Approximately(gpuMacroReliefScale, lastValidatedGpuMacroReliefScale) ||
               !Mathf.Approximately(gpuInlandBasinStrength, lastValidatedGpuInlandBasinStrength) ||
               !Mathf.Approximately(gpuInlandBasinScale, lastValidatedGpuInlandBasinScale) ||
               !Mathf.Approximately(gpuWatershedRidgeStrength, lastValidatedGpuWatershedRidgeStrength) ||
               useExperimentalSignedTerrain != lastValidatedUseExperimentalSignedTerrain;
    }


    private bool GpuHydrologySettingsChanged()
    {
        return gpuDrainageFillIterations != lastValidatedGpuDrainageFillIterations ||
               gpuFlowAccumulationIterations != lastValidatedGpuFlowAccumulationIterations ||
               !Mathf.Approximately(gpuHydroRoutingJitter, lastValidatedGpuHydroRoutingJitter) ||
               !Mathf.Approximately(gpuDrainageRiverMinWidthPixels, lastValidatedGpuDrainageRiverMinWidthPixels) ||
               !Mathf.Approximately(gpuDrainageRiverMaxWidthPixels, lastValidatedGpuDrainageRiverMaxWidthPixels) ||
               !Mathf.Approximately(gpuLakeBasinMinDepth, lastValidatedGpuLakeBasinMinDepth) ||
               !Mathf.Approximately(gpuLakeBasinFullDepth, lastValidatedGpuLakeBasinFullDepth) ||
               !Mathf.Approximately(gpuLakeShorelineWarpPixels, lastValidatedGpuLakeShorelineWarpPixels) ||
               !Mathf.Approximately(gpuRiverAccumulationThresholdSparse, lastValidatedGpuRiverAccumulationThresholdSparse) ||
               !Mathf.Approximately(gpuRiverAccumulationThresholdStandard, lastValidatedGpuRiverAccumulationThresholdStandard) ||
               !Mathf.Approximately(gpuRiverAccumulationThresholdAbundant, lastValidatedGpuRiverAccumulationThresholdAbundant) ||
               !Mathf.Approximately(gpuFlowMinDownhillDelta, lastValidatedGpuFlowMinDownhillDelta) ||
               !Mathf.Approximately(gpuFlowAccumulationCompression, lastValidatedGpuFlowAccumulationCompression) ||
               useExperimentalBasinHydrology != lastValidatedUseExperimentalBasinHydrology ||
               !Mathf.Approximately(experimentalMinBasinScore, lastValidatedExperimentalMinBasinScore) ||
               experimentalMinBasinPixelArea != lastValidatedExperimentalMinBasinPixelArea ||
               !Mathf.Approximately(experimentalMinBasinSeparationPixels, lastValidatedExperimentalMinBasinSeparationPixels) ||
               experimentalSparseLakeCount != lastValidatedExperimentalSparseLakeCount ||
               experimentalStandardLakeCount != lastValidatedExperimentalStandardLakeCount ||
               experimentalAbundantLakeCount != lastValidatedExperimentalAbundantLakeCount ||
               experimentalSparseRiverAttempts != lastValidatedExperimentalSparseRiverAttempts ||
               experimentalStandardRiverAttempts != lastValidatedExperimentalStandardRiverAttempts ||
               experimentalAbundantRiverAttempts != lastValidatedExperimentalAbundantRiverAttempts ||
               !Mathf.Approximately(experimentalWetlandSpread, lastValidatedExperimentalWetlandSpread);
    }

    private void CacheValidatedGpuHeightInputs()
    {
        lastValidatedGpuMacroReliefStrength = gpuMacroReliefStrength;
        lastValidatedGpuMacroReliefScale = gpuMacroReliefScale;
        lastValidatedGpuInlandBasinStrength = gpuInlandBasinStrength;
        lastValidatedGpuInlandBasinScale = gpuInlandBasinScale;
        lastValidatedGpuWatershedRidgeStrength = gpuWatershedRidgeStrength;
        lastValidatedGpuDrainageFillIterations = gpuDrainageFillIterations;
        lastValidatedGpuFlowAccumulationIterations = gpuFlowAccumulationIterations;
        lastValidatedGpuHydroRoutingJitter = gpuHydroRoutingJitter;
        lastValidatedGpuDrainageRiverMinWidthPixels = gpuDrainageRiverMinWidthPixels;
        lastValidatedGpuDrainageRiverMaxWidthPixels = gpuDrainageRiverMaxWidthPixels;
        lastValidatedGpuLakeBasinMinDepth = gpuLakeBasinMinDepth;
        lastValidatedGpuLakeBasinFullDepth = gpuLakeBasinFullDepth;
        lastValidatedGpuLakeShorelineWarpPixels = gpuLakeShorelineWarpPixels;
        lastValidatedGpuRiverAccumulationThresholdSparse = gpuRiverAccumulationThresholdSparse;
        lastValidatedGpuRiverAccumulationThresholdStandard = gpuRiverAccumulationThresholdStandard;
        lastValidatedGpuRiverAccumulationThresholdAbundant = gpuRiverAccumulationThresholdAbundant;
        lastValidatedGpuFlowMinDownhillDelta = gpuFlowMinDownhillDelta;
        lastValidatedGpuFlowAccumulationCompression = gpuFlowAccumulationCompression;
        lastValidatedUseExperimentalSignedTerrain = useExperimentalSignedTerrain;
        lastValidatedUseExperimentalBasinHydrology = useExperimentalBasinHydrology;
        lastValidatedExperimentalMinBasinScore = experimentalMinBasinScore;
        lastValidatedExperimentalMinBasinPixelArea = experimentalMinBasinPixelArea;
        lastValidatedExperimentalMinBasinSeparationPixels = experimentalMinBasinSeparationPixels;
        lastValidatedExperimentalSparseLakeCount = experimentalSparseLakeCount;
        lastValidatedExperimentalStandardLakeCount = experimentalStandardLakeCount;
        lastValidatedExperimentalAbundantLakeCount = experimentalAbundantLakeCount;
        lastValidatedExperimentalSparseRiverAttempts = experimentalSparseRiverAttempts;
        lastValidatedExperimentalStandardRiverAttempts = experimentalStandardRiverAttempts;
        lastValidatedExperimentalAbundantRiverAttempts = experimentalAbundantRiverAttempts;
        lastValidatedExperimentalWetlandSpread = experimentalWetlandSpread;
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

        EnsureCoreTerrainTextures();
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

    private void EnsureCoreTerrainTextures()
    {
        Ensure(ref surfaceDataTexture, "MenuSurfaceDataV2");
        Ensure(ref auxiliaryMaskTexture, "MenuAuxMasksV2");
        Ensure(ref worldStructureTexture, "MenuStructureV2");
    }
    private void Ensure(ref Texture2D t,string n){if(t!=null&&t.width==mapWidth&&t.height==mapHeight)return;DestroyTex(ref t);t=new Texture2D(mapWidth,mapHeight,TextureFormat.RGBA32,false,true){name=n,wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Bilinear};}
    private void DestroyTex(ref Texture2D t){if(t!=null)Destroy(t);t=null;}
    private void EnsureGpuCoastlineResources()
    {
        EnsureGpuCoastlineTexture(ref gpuTectonicSurfaceTexture, "MenuGpuTectonicSurfaceV2");
        EnsureGpuCoastlineTexture(ref gpuTectonicBoundaryTexture, "MenuGpuTectonicBoundaryV2");
        EnsureGpuCoastlineTexture(ref gpuTectonicCrustTexture, "MenuGpuTectonicCrustV2");
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
        if (!useGpuCoastlinePreview) return;
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
        cs.SetTexture(gpuCoastlineKernel, "_GpuTectonicBoundaryTex", gpuTectonicBoundaryTexture);
        cs.SetTexture(gpuCoastlineKernel, "_GpuTectonicCrustTex", gpuTectonicCrustTexture);
        cs.Dispatch(gpuCoastlineKernel, Mathf.CeilToInt(mapWidth / 8f), Mathf.CeilToInt(mapHeight / 8f), 1);
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

    private void DispatchGpuHeight()
    {
        if (!useGpuHeightPreview) return;
        TryAssignDefaultGpuHeightCompute();
        if (menuPlanetPreviewHeightCompute == null) return;
        if (surfaceDataTexture == null || worldStructureTexture == null) return;

        EnsureGpuHeightTexture();
        if (gpuHeightTexture == null) return;

        if (gpuHeightKernel < 0)
            gpuHeightKernel = menuPlanetPreviewHeightCompute.FindKernel("GenerateHeight");

        menuPlanetPreviewHeightCompute.SetInt("_MapWidth", mapWidth);
        menuPlanetPreviewHeightCompute.SetInt("_MapHeight", mapHeight);
        menuPlanetPreviewHeightCompute.SetFloat("_Seed", inputs.seed);
        float presetMultiplier = GetTerrainRoughnessElevationMultiplier(Mathf.Clamp(inputs.terrainRoughnessPresetIndex, 0, 4));
        menuPlanetPreviewHeightCompute.SetFloat("_Elevation", Mathf.Clamp01(inputs.elevation * presetMultiplier));
        int terrainPreset = Mathf.Clamp(inputs.terrainRoughnessPresetIndex, 0, Mathf.Max(0, experimentalTerrainProfiles.Length - 1));
        var expProfile = experimentalTerrainProfiles[Mathf.Min(terrainPreset, experimentalTerrainProfiles.Length - 1)];
        menuPlanetPreviewHeightCompute.SetInt("_UseExperimentalSignedTerrain", useExperimentalSignedTerrain ? 1 : 0);
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
        menuPlanetPreviewHeightCompute.SetFloat("_MacroReliefStrength", gpuMacroReliefStrength);
        menuPlanetPreviewHeightCompute.SetFloat("_MacroReliefScale", gpuMacroReliefScale);
        menuPlanetPreviewHeightCompute.SetFloat("_InlandBasinStrength", gpuInlandBasinStrength);
        menuPlanetPreviewHeightCompute.SetFloat("_InlandBasinScale", gpuInlandBasinScale);
        menuPlanetPreviewHeightCompute.SetFloat("_WatershedRidgeStrength", gpuWatershedRidgeStrength);

        menuPlanetPreviewHeightCompute.SetTexture(gpuHeightKernel, "_TectonicSurfaceTex", TectonicSurfaceTexture);
        menuPlanetPreviewHeightCompute.SetTexture(gpuHeightKernel, "_TectonicCrustTex", TectonicCrustTexture);
        menuPlanetPreviewHeightCompute.SetTexture(gpuHeightKernel, "_GpuHeightTex", gpuHeightTexture);

        int groupsX = Mathf.CeilToInt(mapWidth / 8f);
        int groupsY = Mathf.CeilToInt(mapHeight / 8f);
        menuPlanetPreviewHeightCompute.Dispatch(gpuHeightKernel, groupsX, groupsY, 1);
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

    private void EnsureGpuHydrologyResources()
    {
        if (gpuHydrologyTexture == null || gpuHydrologyTexture.width != mapWidth || gpuHydrologyTexture.height != mapHeight || !gpuHydrologyTexture.IsCreated())
        {
            ReleaseGpuHydrologyResources();
            gpuHydrologyTexture = new RenderTexture(mapWidth, mapHeight, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                name = "MenuGpuHydrologyV2",
                enableRandomWrite = true,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
                autoGenerateMips = false
            };
            gpuHydrologyTexture.Create();
        }
        int aw = Mathf.Max(32, mapWidth / 4);
        int ah = Mathf.Max(16, mapHeight / 4);
        if (gpuHydrologyDepthTexture == null) { gpuHydrologyDepthTexture = new RenderTexture(mapWidth, mapHeight, 0, RenderTextureFormat.ARGBHalf) { enableRandomWrite = true }; gpuHydrologyDepthTexture.Create(); }
        if (gpuDrainageFillA == null) { gpuDrainageFillA = new RenderTexture(aw, ah, 0, RenderTextureFormat.ARGBHalf) { enableRandomWrite = true }; gpuDrainageFillA.Create(); }
        if (gpuDrainageFillB == null) { gpuDrainageFillB = new RenderTexture(aw, ah, 0, RenderTextureFormat.ARGBHalf) { enableRandomWrite = true }; gpuDrainageFillB.Create(); }
        if (gpuFlowDirectionTexture == null) { gpuFlowDirectionTexture = new RenderTexture(aw, ah, 0, RenderTextureFormat.ARGBHalf) { enableRandomWrite = true }; gpuFlowDirectionTexture.Create(); }
        if (gpuFlowAccumA == null) { gpuFlowAccumA = new RenderTexture(aw, ah, 0, RenderTextureFormat.ARGBHalf) { enableRandomWrite = true }; gpuFlowAccumA.Create(); }
        if (gpuFlowAccumB == null) { gpuFlowAccumB = new RenderTexture(aw, ah, 0, RenderTextureFormat.ARGBHalf) { enableRandomWrite = true }; gpuFlowAccumB.Create(); }
        if (gpuCoarseHydrologyMaskTexture == null) { gpuCoarseHydrologyMaskTexture = new RenderTexture(aw, ah, 0, RenderTextureFormat.ARGBHalf) { enableRandomWrite = true }; gpuCoarseHydrologyMaskTexture.Create(); }
        if (gpuCoarseHydrologyDepthTexture == null) { gpuCoarseHydrologyDepthTexture = new RenderTexture(aw, ah, 0, RenderTextureFormat.ARGBHalf) { enableRandomWrite = true }; gpuCoarseHydrologyDepthTexture.Create(); }
    }

    private void ReleaseRenderTexture(ref RenderTexture rt)
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
        ReleaseRenderTexture(ref gpuDrainageFillA);
        ReleaseRenderTexture(ref gpuDrainageFillB);
        ReleaseRenderTexture(ref gpuFlowDirectionTexture);
        ReleaseRenderTexture(ref gpuFlowAccumA);
        ReleaseRenderTexture(ref gpuFlowAccumB);
        ReleaseRenderTexture(ref gpuCoarseHydrologyMaskTexture);
        ReleaseRenderTexture(ref gpuCoarseHydrologyDepthTexture);
        ReleaseRenderTexture(ref gpuExperimentalBasinCandidateTexture);
        ReleaseRenderTexture(ref gpuExperimentalSelectedBasinTexture);
        ReleaseRenderTexture(ref gpuExperimentalRiverPathTexture);
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
            Debug.LogWarning("[MenuPlanetPreview GPU Hydrology] Compute shaders unsupported. GPU hydrology unavailable.");
            return;
        }
        if (!SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBHalf)) return;

        TryAssignDefaultGpuHydrologyCompute();
        if (menuPlanetPreviewHydrologyCompute == null) return;
        if (surfaceDataTexture == null || worldStructureTexture == null || gpuHeightTexture == null) return;

        EnsureGpuHydrologyResources();
        if (useExperimentalBasinHydrology)
        {
            DispatchExperimentalBasinHydrology();
            return;
        }
        int analysisWidth = Mathf.Max(32, mapWidth / 4);
        int analysisHeight = Mathf.Max(16, mapHeight / 4);
        var cs = menuPlanetPreviewHydrologyCompute;
        cs.SetInt("_MapWidth", mapWidth); cs.SetInt("_MapHeight", mapHeight);
        cs.SetInt("_HydrologyAnalysisWidth", analysisWidth);
        cs.SetInt("_HydrologyAnalysisHeight", analysisHeight);
        cs.SetFloat("_Seed", inputs.seed); cs.SetFloat("_Elevation", Mathf.Clamp01(inputs.elevation)); cs.SetFloat("_Moisture", Mathf.Clamp01(inputs.moisture));
        cs.SetInt("_WaterwaysPreset", inputs.waterwaysPreset);
        cs.SetFloat("_HydroRoutingJitter", gpuHydroRoutingJitter);
        cs.SetFloat("_DrainageRiverMinWidthPixels", gpuDrainageRiverMinWidthPixels);
        cs.SetFloat("_DrainageRiverMaxWidthPixels", gpuDrainageRiverMaxWidthPixels);
        cs.SetFloat("_LakeBasinMinDepth", gpuLakeBasinMinDepth);
        cs.SetFloat("_LakeBasinFullDepth", gpuLakeBasinFullDepth);
        cs.SetFloat("_LakeShorelineWarpPixels", gpuLakeShorelineWarpPixels);
        cs.SetFloat("_FlowMinDownhillDelta", gpuFlowMinDownhillDelta);
        cs.SetFloat("_FlowAccumulationCompression", gpuFlowAccumulationCompression);
        float riverAccumulationThreshold = inputs.waterwaysPreset <= 0
            ? gpuRiverAccumulationThresholdSparse
            : inputs.waterwaysPreset == 1
                ? gpuRiverAccumulationThresholdStandard
                : gpuRiverAccumulationThresholdAbundant;
        cs.SetFloat("_RiverAccumulationThreshold", riverAccumulationThreshold);

        int kInitFill = cs.FindKernel("InitDrainageFill");
        int kRelaxFill = cs.FindKernel("RelaxDrainageFill");
        int kFlowDir = cs.FindKernel("BuildFlowDirections");
        int kInitAcc = cs.FindKernel("InitFlowAccumulation");
        int kRelaxAcc = cs.FindKernel("RelaxFlowAccumulation");
        int kBuild = cs.FindKernel("BuildTerrainDrivenHydrologyMasks");
        int kUpsample = cs.FindKernel("UpsampleAndBeautifyHydrology");
        foreach (int k in new[] { kInitFill, kRelaxFill, kFlowDir, kInitAcc, kRelaxAcc, kBuild, kUpsample })
        {
            cs.SetTexture(k, "_GpuHydrologyTex", gpuHydrologyTexture);
            cs.SetTexture(k, "_GpuHydrologyDepthTex", gpuHydrologyDepthTexture);
            cs.SetTexture(k, "_DrainageFillTexA", gpuDrainageFillA);
            cs.SetTexture(k, "_DrainageFillTexB", gpuDrainageFillB);
            cs.SetTexture(k, "_HydroFlowTex", gpuFlowDirectionTexture);
            cs.SetTexture(k, "_FlowAccumTexA", gpuFlowAccumA);
            cs.SetTexture(k, "_FlowAccumTexB", gpuFlowAccumB);
            cs.SetTexture(k, "_CoarseHydrologyMaskTex", gpuCoarseHydrologyMaskTexture);
            cs.SetTexture(k, "_CoarseHydrologyDepthTex", gpuCoarseHydrologyDepthTexture);
            cs.SetTexture(k, "_TectonicSurfaceTex", TectonicSurfaceTexture);
            cs.SetTexture(k, "_GpuHeightTex", gpuHeightTexture);
        }
        cs.Dispatch(kInitFill, Mathf.CeilToInt(analysisWidth / 8f), Mathf.CeilToInt(analysisHeight / 8f), 1);
        for (int i = 0; i < gpuDrainageFillIterations; i++) { cs.Dispatch(kRelaxFill, Mathf.CeilToInt(analysisWidth / 8f), Mathf.CeilToInt(analysisHeight / 8f), 1); var t = gpuDrainageFillA; gpuDrainageFillA = gpuDrainageFillB; gpuDrainageFillB = t; cs.SetTexture(kRelaxFill, "_DrainageFillTexA", gpuDrainageFillA); cs.SetTexture(kRelaxFill, "_DrainageFillTexB", gpuDrainageFillB); }
        cs.Dispatch(kFlowDir, Mathf.CeilToInt(analysisWidth / 8f), Mathf.CeilToInt(analysisHeight / 8f), 1);
        cs.Dispatch(kInitAcc, Mathf.CeilToInt(analysisWidth / 8f), Mathf.CeilToInt(analysisHeight / 8f), 1);
        for (int i = 0; i < gpuFlowAccumulationIterations; i++) { cs.Dispatch(kRelaxAcc, Mathf.CeilToInt(analysisWidth / 8f), Mathf.CeilToInt(analysisHeight / 8f), 1); var t = gpuFlowAccumA; gpuFlowAccumA = gpuFlowAccumB; gpuFlowAccumB = t; cs.SetTexture(kRelaxAcc, "_FlowAccumTexA", gpuFlowAccumA); cs.SetTexture(kRelaxAcc, "_FlowAccumTexB", gpuFlowAccumB); }

        // Rebind ping-pong outputs so downstream kernels always read final buffers.
        RenderTexture finalFill = gpuDrainageFillA;
        RenderTexture finalAccum = gpuFlowAccumA;
        cs.SetTexture(kBuild, "_DrainageFillTexA", finalFill);
        cs.SetTexture(kBuild, "_FlowAccumTexA", finalAccum);
        cs.SetTexture(kUpsample, "_FlowAccumTexA", finalAccum);

        cs.Dispatch(kBuild, Mathf.CeilToInt(analysisWidth / 8f), Mathf.CeilToInt(analysisHeight / 8f), 1);
        cs.Dispatch(kUpsample, Mathf.CeilToInt(mapWidth / 8f), Mathf.CeilToInt(mapHeight / 8f), 1);
        Debug.Log(
            $"[WorldGenV2 GPU Hydrology] TerrainDriven=True " +
            $"WaterwaysPreset={inputs.waterwaysPreset} Moisture={inputs.moisture:0.00} " +
            $"Analysis={analysisWidth}x{analysisHeight} " +
            $"FillIter={gpuDrainageFillIterations} AccumIter={gpuFlowAccumulationIterations} " +
            $"RiverThreshold={riverAccumulationThreshold:0.00} " +
            $"LakeDepth={gpuLakeBasinMinDepth:0.000}-{gpuLakeBasinFullDepth:0.000} " +
            $"HeightRTReady={gpuHeightTexture != null}");
    }
    private void EnsureExperimentalBasinHydrologyResources()
    {
        int aw = Mathf.Max(32, mapWidth / 4);
        int ah = Mathf.Max(16, mapHeight / 4);
        EnsureExperimentalRt(ref gpuExperimentalBasinCandidateTexture, aw, ah, "MenuGpuExpBasinCandidates");
        EnsureExperimentalRt(ref gpuExperimentalSelectedBasinTexture, aw, ah, "MenuGpuExpSelectedBasins");
        EnsureExperimentalRt(ref gpuExperimentalRiverPathTexture, aw, ah, "MenuGpuExpRiverPaths");
    }

    private void EnsureExperimentalRt(ref RenderTexture rt, int w, int h, string name)
    {
        if (rt != null && rt.width == w && rt.height == h && rt.IsCreated()) return;
        if (rt != null) { rt.Release(); Destroy(rt); }
        rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
        { name = name, enableRandomWrite = true, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, useMipMap = false, autoGenerateMips = false };
        rt.Create();
    }

    private void DispatchExperimentalBasinHydrology()
    {
        if (!useExperimentalSignedTerrain)
        {
            Debug.LogWarning("[WorldGenV2 Experimental Basin Hydrology] Experimental basin hydrology requires experimental signed terrain. Aborting.");
            ClearHydrologyTextures();
            return;
        }

        EnsureExperimentalBasinHydrologyResources();
        var cs = menuPlanetPreviewHydrologyCompute;
        int kCandidate = cs.FindKernel("BuildExperimentalBasinCandidates");
        int kRasterLakes = cs.FindKernel("RasterizeExperimentalSelectedBasins");
        int kClear = cs.FindKernel("ClearExperimentalHydrology");
        int kRivers = cs.FindKernel("BuildExperimentalRiverPotential");
        int kUpsample = cs.FindKernel("UpsampleExperimentalBasinHydrology");
        int aw = Mathf.Max(32, mapWidth / 4);
        int ah = Mathf.Max(16, mapHeight / 4);
        int targetLakeCount = inputs.waterwaysPreset <= 0 ? experimentalSparseLakeCount : inputs.waterwaysPreset == 1 ? experimentalStandardLakeCount : experimentalAbundantLakeCount;
        int riverAttempts = inputs.waterwaysPreset <= 0 ? experimentalSparseRiverAttempts : inputs.waterwaysPreset == 1 ? experimentalStandardRiverAttempts : experimentalAbundantRiverAttempts;
        float moisture = Mathf.Clamp01(inputs.moisture);
        targetLakeCount = Mathf.RoundToInt(targetLakeCount * Mathf.Lerp(0.65f, 1.35f, moisture) * experimentalMoistureLakeMultiplier);
        riverAttempts = Mathf.RoundToInt(riverAttempts * Mathf.Lerp(0.65f, 1.50f, moisture) * experimentalMoistureRiverMultiplier);
        float presetScoreBias = inputs.waterwaysPreset <= 0 ? 1.15f : inputs.waterwaysPreset == 1 ? 1.0f : 0.82f;
        cs.SetInt("_MapWidth", mapWidth);
        cs.SetInt("_MapHeight", mapHeight);
        cs.SetInt("_HydrologyAnalysisWidth", aw);
        cs.SetInt("_HydrologyAnalysisHeight", ah);
        cs.SetFloat("_Seed", inputs.seed);
        cs.SetFloat("_Moisture", moisture);
        cs.SetInt("_WaterwaysPreset", inputs.waterwaysPreset);
        cs.SetInt("_UseExperimentalBasinHydrology", 1);
        cs.SetInt("_ExperimentalTargetLakeCount", targetLakeCount);
        cs.SetInt("_ExperimentalRiverAttempts", riverAttempts);
        cs.SetFloat("_ExperimentalMinBasinScore", experimentalMinBasinScore);
        cs.SetFloat("_ExperimentalEffectiveMinBasinScore", experimentalMinBasinScore * presetScoreBias);
        cs.SetInt("_ExperimentalMinBasinPixelArea", experimentalMinBasinPixelArea);
        cs.SetFloat("_ExperimentalMinBasinSeparationPixels", experimentalMinBasinSeparationPixels);
        cs.SetFloat("_ExperimentalWetlandSpread", experimentalWetlandSpread);
        cs.SetFloat("_ExperimentalRiverMeanderStrength", experimentalRiverMeanderStrength);
        cs.SetFloat("_ExperimentalRiverValleyBias", experimentalRiverValleyBias);
        cs.SetFloat("_ExperimentalLakeToCoastChance", experimentalLakeToCoastChance);
        foreach (int k in new[] { kClear, kCandidate, kRasterLakes, kRivers, kUpsample })
        {
            cs.SetTexture(k, "_TectonicSurfaceTex", TectonicSurfaceTexture);
            cs.SetTexture(k, "_GpuHeightTex", gpuHeightTexture);
            cs.SetTexture(k, "_ExperimentalBasinCandidateTex", gpuExperimentalBasinCandidateTexture);
            cs.SetTexture(k, "_ExperimentalSelectedBasinTex", gpuExperimentalSelectedBasinTexture);
            cs.SetTexture(k, "_ExperimentalRiverPathTex", gpuExperimentalRiverPathTexture);
            cs.SetTexture(k, "_GpuHydrologyTex", gpuHydrologyTexture);
            cs.SetTexture(k, "_GpuHydrologyDepthTex", gpuHydrologyDepthTexture);
            cs.SetTexture(k, "_CoarseHydrologyMaskTex", gpuCoarseHydrologyMaskTexture);
            cs.SetTexture(k, "_CoarseHydrologyDepthTex", gpuCoarseHydrologyDepthTexture);
        }
        int gx = Mathf.CeilToInt(aw / 8f), gy = Mathf.CeilToInt(ah / 8f);
        cs.Dispatch(kClear, gx, gy, 1);
        cs.Dispatch(kCandidate, gx, gy, 1);
        ApplyExperimentalBasinSelectionCpu(aw, ah, targetLakeCount, experimentalMinBasinPixelArea, experimentalMinBasinSeparationPixels, experimentalMinBasinScore * presetScoreBias);
        cs.Dispatch(kRasterLakes, gx, gy, 1);
        cs.Dispatch(kRivers, gx, gy, 1);
        cs.Dispatch(kUpsample, Mathf.CeilToInt(mapWidth / 8f), Mathf.CeilToInt(mapHeight / 8f), 1);
        Debug.Log($"[WorldGenV2 Experimental Basin Hydrology] WaterwaysPreset={inputs.waterwaysPreset} Moisture={inputs.moisture:0.00} Analysis={aw}x{ah} Lakes={targetLakeCount} Rivers={riverAttempts} SignedHeight={useExperimentalSignedTerrain} HeightRTReady={gpuHeightTexture != null}");
    }

    private void ApplyExperimentalBasinSelectionCpu(int width, int height, int targetLakeCount, int minAreaPixels, float minScore, float minSeparationPixels)
    {
        RenderTexture prev = RenderTexture.active;
        var readback = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true);
        RenderTexture.active = gpuExperimentalBasinCandidateTexture;
        readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        readback.Apply(false, false);
        var pixels = readback.GetPixels();
        Destroy(readback);
        RenderTexture.active = prev;

        bool[] visited = new bool[pixels.Length];
        var candidates = new List<(int x, int y, float score, int area)>();
        int[] nx = { 1, -1, 0, 0 };
        int[] ny = { 0, 0, 1, -1 };
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int start = y * width + x;
            if (visited[start] || pixels[start].r < minScore) continue;
            var q = new Queue<int>();
            q.Enqueue(start);
            visited[start] = true;
            int area = 0, bestIndex = start;
            float bestScore = pixels[start].r;
            while (q.Count > 0)
            {
                int i = q.Dequeue();
                area++;
                float s = pixels[i].r;
                if (s > bestScore) { bestScore = s; bestIndex = i; }
                int cx = i % width, cy = i / width;
                for (int d = 0; d < 4; d++)
                {
                    int px = (cx + nx[d] + width) % width;
                    int py = cy + ny[d];
                    if (py < 0 || py >= height) continue;
                    int ni = py * width + px;
                    if (visited[ni] || pixels[ni].r < minScore) continue;
                    visited[ni] = true;
                    q.Enqueue(ni);
                }
            }
            if (area >= minAreaPixels)
                candidates.Add((bestIndex % width, bestIndex / width, bestScore, area));
        }

        candidates.Sort((a, b) => b.score.CompareTo(a.score));
        var selected = new List<(int x, int y, float score)>();
        float minSepSq = minSeparationPixels * minSeparationPixels;
        foreach (var c in candidates)
        {
            bool tooClose = false;
            foreach (var s in selected)
            {
                float dx = Mathf.Abs(c.x - s.x);
                dx = Mathf.Min(dx, width - dx);
                float dy = c.y - s.y;
                if ((dx * dx) + (dy * dy) < minSepSq) { tooClose = true; break; }
            }
            if (tooClose) continue;
            selected.Add((c.x, c.y, c.score));
            if (selected.Count >= Mathf.Max(0, targetLakeCount)) break;
        }

        Color[] outPixels = new Color[width * height];
        foreach (var s in selected)
            outPixels[s.y * width + s.x] = new Color(1f, Mathf.Clamp01(s.score), 0f, 1f);
        var selectedTex = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true);
        selectedTex.SetPixels(outPixels);
        selectedTex.Apply(false, false);
        Graphics.Blit(selectedTex, gpuExperimentalSelectedBasinTexture);
        Destroy(selectedTex);
    }

    private static byte B(float v)=> (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(v)*255f),0,255);

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
    private void WriteSurfaceAndStructure(float[] land,float[] shelf,float[] cont){int n=mapWidth*mapHeight;var s=new Color32[n];var a=new Color32[n];var w=new Color32[n];for(int i=0;i<n;i++){s[i]=new Color32(B(land[i]),0,0,B(shelf[i]));w[i]=new Color32(B(cont[i]),0,0,0);a[i]=new Color32(B(shelf[i]),B(cont[i]),0,0);}surfaceDataTexture.SetPixelData(s,0);surfaceDataTexture.Apply(false,false);worldStructureTexture.SetPixelData(w,0);worldStructureTexture.Apply(false,false);auxiliaryMaskTexture.SetPixelData(a,0);auxiliaryMaskTexture.Apply(false,false);}
    [BurstCompile] private struct LandUpsampleAndDistanceJob : IJobFor
    {
        [ReadOnly] public NativeArray<float> topoLandCoastDistance, topoOceanCoastDistance, topoSignedCoastDistance;
        [ReadOnly] public NativeArray<float> macroCoastNoise, midCoastNoise, coastEdgeNoise;
        public int noiseFieldWidth, noiseFieldHeight;
        public int mapWidth, mapHeight, topoWidth, topoHeight; public float seed, coastlineDeformationWidthCells, coastlineWarpStrength, coastlineMidNoiseStrength, coastlineEdgeNoiseStrength, coastlineSoftness, coastlineThresholdBias;
        public NativeArray<float> land, inlandDistance, offshoreDistance, shelf, continentality;
        public void Execute(int i){int x=i%mapWidth,y=i/mapWidth; float2 uv=new float2(((float)x/mapWidth)*topoWidth,((float)y/mapHeight)*topoHeight); SampleBilinear(uv, topoLandCoastDistance, out var inD); SampleBilinear(uv, topoOceanCoastDistance, out var offD); SampleBilinear(uv, topoSignedCoastDistance, out var signedTopoDistance); float2 noiseUV=new float2(((float)x/mapWidth)*noiseFieldWidth,((float)y/mapHeight)*noiseFieldHeight); SampleNoiseBilinear(noiseUV, macroCoastNoise, out var macro); SampleNoiseBilinear(noiseUV, midCoastNoise, out var mid); SampleNoiseBilinear(noiseUV, coastEdgeNoise, out var edge); float deformationWidth=math.max(0.001f,coastlineDeformationWidthCells); float signedEnvelope=signedTopoDistance/deformationWidth; float coastInfluence=1f-math.saturate(math.abs(signedEnvelope)); coastInfluence=coastInfluence*coastInfluence*(3f-2f*coastInfluence); float macroDisplacement=macro*coastlineWarpStrength; float midDisplacement=mid*coastlineMidNoiseStrength; float fineDisplacement=edge*coastlineEdgeNoiseStrength; float coastlineNoiseDisplacement=macroDisplacement+midDisplacement+fineDisplacement; float finalCoastSignal=signedEnvelope+coastlineNoiseDisplacement*coastInfluence-coastlineThresholdBias; land[i]=math.smoothstep(-coastlineSoftness,coastlineSoftness,finalCoastSignal); inlandDistance[i]=inD; offshoreDistance[i]=offD; float finalLand=land[i]; shelf[i]=(1f-finalLand)*math.saturate(1f-offD/(0.04f*mapHeight)); continentality[i]=finalLand*math.saturate(inD/(0.24f*mapHeight));}
        void SampleBilinear(float2 uv, NativeArray<float> arr, out float value){int x0=((int)math.floor(uv.x)%topoWidth+topoWidth)%topoWidth,y0=math.clamp((int)math.floor(uv.y),0,topoHeight-1); int x1=(x0+1)%topoWidth,y1=math.min(y0+1,topoHeight-1); float fx=uv.x-math.floor(uv.x),fy=uv.y-math.floor(uv.y); float v00=arr[y0*topoWidth+x0],v10=arr[y0*topoWidth+x1],v01=arr[y1*topoWidth+x0],v11=arr[y1*topoWidth+x1]; value=math.lerp(math.lerp(v00,v10,fx),math.lerp(v01,v11,fx),fy);}
        void SampleNoiseBilinear(float2 uv, NativeArray<float> arr, out float value){int x0=((int)math.floor(uv.x)%noiseFieldWidth+noiseFieldWidth)%noiseFieldWidth,y0=math.clamp((int)math.floor(uv.y),0,noiseFieldHeight-1); int x1=(x0+1)%noiseFieldWidth,y1=math.min(y0+1,noiseFieldHeight-1); float fx=uv.x-math.floor(uv.x),fy=uv.y-math.floor(uv.y); float v00=arr[y0*noiseFieldWidth+x0],v10=arr[y0*noiseFieldWidth+x1],v01=arr[y1*noiseFieldWidth+x0],v11=arr[y1*noiseFieldWidth+x1]; value=math.lerp(math.lerp(v00,v10,fx),math.lerp(v01,v11,fx),fy);}
    }
}
