using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

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
    public float temperature;
    public float moisture;

    public float climateNoiseStrength;
    public float coastWetnessStrength;
    public float continentalDrynessStrength;
    public float continentalTemperatureStrength;
    public float riparianWetnessStrength;

    public float biomeProvinceStrength;
    public float biomeCompetitionSharpness;

    public int landPresetIndex;
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
    [SerializeField] private bool logLandmassGenerationDiagnostics = true;

    [Header("FastNoiseLite Sphere Fields")]
    [SerializeField] private bool useFastNoiseLiteFields = true;
    [SerializeField, Range(128, 1024)] private int noiseFieldWidth = 512;
    [SerializeField, Range(64, 512)] private int noiseFieldHeight = 256;

    [Header("Hydrology Targets")]
    [SerializeField] private Vector2Int sparseRiverRange = new Vector2Int(12, 20);
    [SerializeField] private Vector2Int standardRiverRange = new Vector2Int(24, 40);
    [SerializeField] private Vector2Int abundantRiverRange = new Vector2Int(45, 70);

    [SerializeField, Range(4, 64)] private int minRiverSourceSpacingPixels = 18;
    [SerializeField, Range(1, 16)] private int riverCandidateStride = 4;

    [Header("Decorative River Shape")]
    [SerializeField, Range(4f, 80f)] private float decorativeRiverMinSourceCoastDistance = 22f;
    [SerializeField, Range(0f, 1f)] private float decorativeRiverMeanderStrength = 0.55f;
    [SerializeField, Range(0.02f, 0.40f)] private float decorativeRiverLateralWanderFraction = 0.14f;
    [SerializeField, Range(3, 10)] private int decorativeRiverControlPointCountMin = 5;
    [SerializeField, Range(4, 14)] private int decorativeRiverControlPointCountMax = 8;
    [SerializeField, Range(1, 6)] private int decorativeRiverUpperRadius = 2;
    [SerializeField, Range(2, 10)] private int decorativeRiverLowerRadius = 5;
    [SerializeField, Range(1, 16)] private int decorativeRiverLandSnapRadius = 6;

    Texture2D surfaceDataTexture, auxiliaryMaskTexture, worldStructureTexture, climateTexture, hydrologyMaskTexture, biomeWeights0Texture, biomeWeights1Texture, biomeWeights2Texture;
    public Texture2D TectonicSurfaceTexture => surfaceDataTexture;
    public Texture2D TectonicBoundaryTexture => auxiliaryMaskTexture;
    public Texture2D TectonicCrustTexture => worldStructureTexture;
    public Texture2D ClimateTexture => climateTexture;
    public Texture2D HydrologyMaskTexture => hydrologyMaskTexture;
    public Texture2D BiomeWeights0Texture => biomeWeights0Texture;
    public Texture2D BiomeWeights1Texture => biomeWeights1Texture;
    public Texture2D BiomeWeights2Texture => biomeWeights2Texture;
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

    private float[] cachedMacroCoastNoise;
    private float[] cachedMidCoastNoise;
    private float[] cachedCoastEdgeNoise;
    private float[] cachedUplandProvinceNoise;
    private float[] cachedMountainProvinceNoise;
    private float[] cachedMountainRangeNoise;
    private float[] cachedTopologyShapeNoise;

    private int cachedNoiseSeed;
    private int cachedNoiseWidth;
    private int cachedNoiseHeight;
    private int cachedTopologyWidth;
    private int cachedTopologyHeight;
    private bool fastNoiseFieldsValid;

    public void SetInputs(MenuPlanetPreviewWorldInputs v) => inputs = v;
    public void RequestRebuild(PreviewWorldRebuildScope scope, bool immediate = false) {
        pending |= ExpandDependencies(scope);
        generationVersion++;
        scheduledAt = immediate ? Time.time : Time.time + regenerationDelay;
    }
    public void Release() { DestroyTex(ref surfaceDataTexture); DestroyTex(ref auxiliaryMaskTexture); DestroyTex(ref worldStructureTexture); DestroyTex(ref climateTexture); DestroyTex(ref hydrologyMaskTexture); DestroyTex(ref biomeWeights0Texture); DestroyTex(ref biomeWeights1Texture); DestroyTex(ref biomeWeights2Texture); }
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
        EnsureAllTextures();
        float[] land = null, elev = null, mtn = null, shelf = null, cont = null, moisture = null, temp = null;
        GenDiagnostics diag = default;

        if ((s & PreviewWorldRebuildScope.Tectonics) != 0)
        {
            GenerateTerrain(out land, out elev, out mtn, out shelf, out cont, out diag);
            WriteSurfaceAndStructure(land, elev, mtn, shelf, cont);
            yield return null;
            if (version != generationVersion) { if (logWorldGenerationDiagnostics) Debug.Log($"[WorldGenV2 Async] Cancelled stale generation version={version}"); IsGeneratingPreview = false; yield break; }
        }
        if ((s & PreviewWorldRebuildScope.Climate) != 0)
        {
            if (land == null) ReadSurface(out land, out elev, out mtn, out shelf);
            GenerateClimate(land, elev, mtn, cont, out temp, out moisture);
            yield return null;
        }
        if ((s & PreviewWorldRebuildScope.Hydrology) != 0)
        {
            if (land == null) ReadSurface(out land, out elev, out mtn, out shelf);
            if (temp == null || moisture == null) ReadClimate(out temp, out moisture, out cont);
            GenerateHydrology(land, elev, moisture, ref diag);
            yield return null;
        }
        if ((s & PreviewWorldRebuildScope.Biomes) != 0)
        {
            if (land == null) ReadSurface(out land, out elev, out mtn, out shelf);
            if (temp == null || moisture == null) ReadClimate(out temp, out moisture, out cont);
            ReadHydrology(out var river, out var lake, out var hydroWetness, out _);
            GenerateBiomes(land, elev, mtn, temp, moisture, river, lake, hydroWetness);
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

    private PreviewWorldRebuildScope ExpandDependencies(PreviewWorldRebuildScope r) => (r & PreviewWorldRebuildScope.Tectonics) != 0 ? PreviewWorldRebuildScope.All : (r & PreviewWorldRebuildScope.Climate) != 0 ? PreviewWorldRebuildScope.Climate | PreviewWorldRebuildScope.Hydrology | PreviewWorldRebuildScope.Biomes : (r & PreviewWorldRebuildScope.Hydrology) != 0 ? PreviewWorldRebuildScope.Hydrology | PreviewWorldRebuildScope.Biomes : (r & PreviewWorldRebuildScope.Biomes) != 0 ? PreviewWorldRebuildScope.Biomes : 0;

    private struct GenDiagnostics { public string preset; public float targetLand; public float actualLand; public float topologyCoverage; public int topologyLandCells; public int targetTopologyLandCells; public int groupCount; public float largestGroupShare; public int attempts; public int rivers; public int lakes; public float avgElevation; public float maxElevation; public float mountainCoverage; }

    private void Flush()
    {
        StartOrRestartGeneration();
    }

    private void EnsureAllTextures(){Ensure(ref surfaceDataTexture,"MenuSurfaceDataV2");Ensure(ref auxiliaryMaskTexture,"MenuAuxMasksV2");Ensure(ref worldStructureTexture,"MenuStructureV2");Ensure(ref climateTexture,"MenuClimateV2");Ensure(ref hydrologyMaskTexture,"MenuHydrologyV2");Ensure(ref biomeWeights0Texture,"MenuBiome0V2");Ensure(ref biomeWeights1Texture,"MenuBiome1V2");Ensure(ref biomeWeights2Texture,"MenuBiome2V2");}
    private void Ensure(ref Texture2D t,string n){if(t!=null&&t.width==mapWidth&&t.height==mapHeight)return;DestroyTex(ref t);t=new Texture2D(mapWidth,mapHeight,TextureFormat.RGBA32,false,true){name=n,wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Bilinear};}
    private void DestroyTex(ref Texture2D t){if(t!=null)Destroy(t);t=null;}
    private static byte B(float v)=> (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(v)*255f),0,255);

    // concise but complete implementation
    private void GenerateTerrain(out float[] land,out float[] elev,out float[] mtn,out float[] shelf,out float[] cont,out GenDiagnostics diag){
        int tw=Mathf.Max(16,topologyWidth),th=Mathf.Max(8,topologyHeight),tn=tw*th; diag=default;
        int presetIndex=Mathf.Clamp(inputs.landPresetIndex,0,presets.Length-1); var preset=presets[presetIndex]; diag.preset=preset.name;
        float sizeBias=Mathf.Clamp01(1f-inputs.landThreshold); float shapeBias=Mathf.InverseLerp(0.5f,5f,inputs.landScale); diag.targetLand=0f;
        TopologyCell[] topo=new TopologyCell[tn];
        int seedBase = Mathf.RoundToInt(inputs.seed * 1000f);
        EnsureFastNoiseFields(seedBase, tw, th);
        var r=new System.Random((seedBase*73856093) ^ 19349663);
        var plans=BuildLandmassPlan(preset,tw,th,r,sizeBias,shapeBias,out var planBuildDiag);
        if (logWorldGenerationDiagnostics && logLandmassGenerationDiagnostics)
            Debug.Log($"[WorldGenV2 Landmass Plan]\nPreset={preset.name}\nTopologySize={tw}x{th} ({tn} cells)\nRequestedMajor={planBuildDiag.requestedMajorCount}\nBuiltMajor={planBuildDiag.builtMajorCount}\nRequestedLargeIslands={planBuildDiag.requestedLargeIslandCount}\nBuiltLargeIslands={planBuildDiag.builtLargeIslandCount}\nRequestedSmallClusters={planBuildDiag.requestedSmallClusterCount}\nBuiltSmallClusters={planBuildDiag.builtSmallClusterCount}\nFailedCenterPlacements={planBuildDiag.failedCenterPlacements}\nTotalPlannedTargetCells={planBuildDiag.totalPlannedTargetCells}\nPlannedCoverage={(float)planBuildDiag.totalPlannedTargetCells / tn:F4}");
        RasterizeLandmassPlansToTopology(topo,plans,preset,tw,th,shapeBias);
        diag.attempts=1;
        diag.targetTopologyLandCells = planBuildDiag.totalPlannedTargetCells;
        diag.topologyLandCells = CountTopologyLandCells(topo);
        diag.topologyCoverage = tn > 0 ? (float)diag.topologyLandCells / tn : 0f;
        int[] comp; diag.largestGroupShare=LargestLandmassShare(topo,tw,th,out diag.groupCount,out comp);
        if (logWorldGenerationDiagnostics)
            Debug.Log($"[WorldGenV2 Landmass Field]\nPreset={preset.name}\nPlannedMajor={planBuildDiag.builtMajorCount}\nPlannedLargeIslands={planBuildDiag.builtLargeIslandCount}\nPlannedSmallClusters={planBuildDiag.builtSmallClusterCount}\nPlannedTargetCoverage={(float)planBuildDiag.totalPlannedTargetCells / tn:F4}\nRasterizedTopologyCoverage={diag.topologyCoverage:F4}");
        if (logWorldGenerationDiagnostics)
            Debug.Log($"[WorldGenV2 Landmasses]\nPreset={preset.name}\nWorldCoverage={diag.topologyCoverage:F2}");
        bool[] topoLand = new bool[tn]; for (int i = 0; i < tn; i++) topoLand[i] = topo[i].isLand;
        float[] topoLandDist = DistanceFromBoundaryTopology(topoLand, tw, th, true);
        float[] topoOceanDist = DistanceFromBoundaryTopology(topoLand, tw, th, false);
        float[] topoSignedCoastDistance = BuildSignedTopologyCoastDistance(topoLand, topoLandDist, topoOceanDist);
        int noiseW = Mathf.Max(1, cachedNoiseWidth);
        int noiseH = Mathf.Max(1, cachedNoiseHeight);
        int n=mapWidth*mapHeight; land=new float[n]; elev=new float[n]; mtn=new float[n]; shelf=new float[n]; cont=new float[n];
        var nTopoLandDist = new NativeArray<float>(tn, Allocator.TempJob);
        var nTopoOceanDist = new NativeArray<float>(tn, Allocator.TempJob);
        var nTopoSignedCoastDistance = new NativeArray<float>(tn, Allocator.TempJob);
        var nLand = new NativeArray<float>(n, Allocator.TempJob);
        var nInland = new NativeArray<float>(n, Allocator.TempJob);
        var nOffshore = new NativeArray<float>(n, Allocator.TempJob);
        var nElev = new NativeArray<float>(n, Allocator.TempJob);
        var nMtn = new NativeArray<float>(n, Allocator.TempJob);
        var nShelf = new NativeArray<float>(n, Allocator.TempJob);
        var nCont = new NativeArray<float>(n, Allocator.TempJob);
        var nUpland = new NativeArray<float>(n, Allocator.TempJob);
        var nRawMtn = new NativeArray<float>(n, Allocator.TempJob);
        int nn = noiseW * noiseH;
        var nMacroCoastNoise = new NativeArray<float>(nn, Allocator.TempJob);
        var nMidCoastNoise = new NativeArray<float>(nn, Allocator.TempJob);
        var nCoastEdgeNoise = new NativeArray<float>(nn, Allocator.TempJob);
        var nUplandProvinceNoise = new NativeArray<float>(nn, Allocator.TempJob);
        var nMountainProvinceNoise = new NativeArray<float>(nn, Allocator.TempJob);
        var nMountainRangeNoise = new NativeArray<float>(nn, Allocator.TempJob);
        try{
            for(int i=0;i<tn;i++){nTopoLandDist[i]=topoLandDist[i]; nTopoOceanDist[i]=topoOceanDist[i]; nTopoSignedCoastDistance[i]=topoSignedCoastDistance[i];}
            if (fastNoiseFieldsValid && useFastNoiseLiteFields)
            {
                nMacroCoastNoise.CopyFrom(cachedMacroCoastNoise);
                nMidCoastNoise.CopyFrom(cachedMidCoastNoise);
                nCoastEdgeNoise.CopyFrom(cachedCoastEdgeNoise);
                nUplandProvinceNoise.CopyFrom(cachedUplandProvinceNoise);
                nMountainProvinceNoise.CopyFrom(cachedMountainProvinceNoise);
                nMountainRangeNoise.CopyFrom(cachedMountainRangeNoise);
            }
            var landJob=new LandUpsampleAndDistanceJob{mapWidth=mapWidth,mapHeight=mapHeight,topoWidth=tw,topoHeight=th,seed=inputs.seed,coastlineDeformationWidthCells=coastlineDeformationWidthCells,coastlineWarpStrength=coastlineWarpStrength,coastlineMidNoiseStrength=coastlineMidNoiseStrength,coastlineEdgeNoiseStrength=coastlineEdgeNoiseStrength,coastlineSoftness=coastlineSoftness,coastlineThresholdBias=coastlineThresholdBias,topoLandCoastDistance=nTopoLandDist,topoOceanCoastDistance=nTopoOceanDist,topoSignedCoastDistance=nTopoSignedCoastDistance,macroCoastNoise=nMacroCoastNoise,midCoastNoise=nMidCoastNoise,coastEdgeNoise=nCoastEdgeNoise,noiseFieldWidth=noiseW,noiseFieldHeight=noiseH,land=nLand,inlandDistance=nInland,offshoreDistance=nOffshore};
            var jh=landJob.ScheduleParallel(n,64,default);
            var terrJob=new TerrainPotentialJob{mapWidth=mapWidth,mapHeight=mapHeight,seed=inputs.seed,land=nLand,inlandDistance=nInland,uplandProvinceNoise=nUplandProvinceNoise,mountainProvinceNoise=nMountainProvinceNoise,mountainRangeNoise=nMountainRangeNoise,noiseFieldWidth=noiseW,noiseFieldHeight=noiseH,rawMountainPotential=nRawMtn,uplandPotential=nUpland};
            jh=terrJob.ScheduleParallel(n,64,jh); jh.Complete();
            float[] mountainRank = BuildMountainRankField(nLand, nRawMtn);
            var nMountainRank = new NativeArray<float>(mountainRank, Allocator.TempJob);
            var mfJob=new MountainFinalizeJob{mapHeight=mapHeight,land=nLand,inlandDistance=nInland,offshoreDistance=nOffshore,uplandPotential=nUpland,mountainRank=nMountainRank,rawMountainPotential=nRawMtn,mountain=nMtn,elevation=nElev,shelf=nShelf,continentality=nCont};
            mfJob.ScheduleParallel(n,64,default).Complete();
            nMountainRank.Dispose();
            float elevSum=0,elevMax=0,mtnPixels=0,landPixels=0,lc=0; for(int i=0;i<n;i++){land[i]=nLand[i];elev[i]=nElev[i];mtn[i]=nMtn[i];shelf[i]=nShelf[i];cont[i]=nCont[i]; if(land[i]>0.5f){lc++; landPixels++; elevSum+=elev[i]; elevMax=Mathf.Max(elevMax,elev[i]); if(mtn[i]>0.35f)mtnPixels++;}}
            float finalCoastLandCoverage = n > 0 ? lc / n : 0f;
            if (logWorldGenerationDiagnostics)
            {
                Debug.Log($"[WorldGenV2 Coast Sculpt]\nTopologyCoverage={diag.topologyCoverage:F4}\nFinalLandCoverage={finalCoastLandCoverage:F4}\nCoastlineDeformationWidthCells={coastlineDeformationWidthCells:F2}\nCoastlineWarpStrength={coastlineWarpStrength:F3}\nCoastlineMidNoiseStrength={coastlineMidNoiseStrength:F3}\nCoastlineEdgeNoiseStrength={coastlineEdgeNoiseStrength:F3}\nCoastlineThresholdBias={coastlineThresholdBias:F3}");
                if (Mathf.Abs(finalCoastLandCoverage - diag.topologyCoverage) > 0.18f)
                {
                    Debug.LogWarning("[WorldGenV2 Coast Sculpt WARNING]\nFinal FastNoise coastline changed land coverage substantially.\nThis may be fine visually, but tune coastlineThresholdBias or deformation strength if presets drift too far.");
                }
            }
            diag.actualLand=lc/n; diag.avgElevation=landPixels>0?elevSum/landPixels:0; diag.maxElevation=elevMax; diag.mountainCoverage=landPixels>0?mtnPixels/landPixels:0;
        } finally {
            nMountainRangeNoise.Dispose();
            nMountainProvinceNoise.Dispose();
            nUplandProvinceNoise.Dispose();
            nCoastEdgeNoise.Dispose();
            nMidCoastNoise.Dispose();
            nMacroCoastNoise.Dispose();
            nRawMtn.Dispose();
            nUpland.Dispose();
            nCont.Dispose();
            nShelf.Dispose();
            nMtn.Dispose();
            nElev.Dispose();
            nOffshore.Dispose();
            nInland.Dispose();
            nLand.Dispose();
            nTopoSignedCoastDistance.Dispose();
            nTopoOceanDist.Dispose();
            nTopoLandDist.Dispose();
        }
    }



    private void EnsureFastNoiseFields(int seedBase, int topologyW, int topologyH)
    {
        if (!useFastNoiseLiteFields)
        {
            fastNoiseFieldsValid = false;
            return;
        }

        int nw = Mathf.Max(128, noiseFieldWidth);
        int nh = Mathf.Max(64, noiseFieldHeight);
        int tw = Mathf.Max(8, topologyW);
        int th = Mathf.Max(4, topologyH);
        bool needsRebuild = !fastNoiseFieldsValid
            || cachedNoiseSeed != seedBase
            || cachedNoiseWidth != nw
            || cachedNoiseHeight != nh
            || cachedTopologyWidth != tw
            || cachedTopologyHeight != th;

        if (!needsRebuild) return;

        int n = nw * nh;
        cachedMacroCoastNoise = new float[n];
        cachedMidCoastNoise = new float[n];
        cachedCoastEdgeNoise = new float[n];
        cachedUplandProvinceNoise = new float[n];
        cachedMountainProvinceNoise = new float[n];
        cachedMountainRangeNoise = new float[n];
        cachedTopologyShapeNoise = new float[tw * th];

        var macroCoastNoise = new FastNoiseLite(seedBase + 1101);
        macroCoastNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        macroCoastNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        macroCoastNoise.SetFractalOctaves(3); macroCoastNoise.SetFractalLacunarity(2f); macroCoastNoise.SetFractalGain(0.5f); macroCoastNoise.SetFrequency(1.35f);

        var coastWarp = new FastNoiseLite(seedBase + 2202);
        coastWarp.SetDomainWarpType(FastNoiseLite.DomainWarpType.OpenSimplex2);
        coastWarp.SetFractalType(FastNoiseLite.FractalType.DomainWarpProgressive);
        coastWarp.SetFractalOctaves(2); coastWarp.SetFractalLacunarity(2f); coastWarp.SetFractalGain(0.5f); coastWarp.SetFrequency(1.10f); coastWarp.SetDomainWarpAmp(0.45f);

        var midCoastNoise = new FastNoiseLite(seedBase + 2752);
        midCoastNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        midCoastNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        midCoastNoise.SetFractalOctaves(3); midCoastNoise.SetFractalLacunarity(2.0f); midCoastNoise.SetFractalGain(0.52f); midCoastNoise.SetFrequency(2.55f);

        var coastEdgeNoise = new FastNoiseLite(seedBase + 3303);
        coastEdgeNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        coastEdgeNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        coastEdgeNoise.SetFractalOctaves(2); coastEdgeNoise.SetFractalLacunarity(2.1f); coastEdgeNoise.SetFractalGain(0.45f); coastEdgeNoise.SetFrequency(4.75f);

        var uplandNoise = new FastNoiseLite(seedBase + 4404);
        uplandNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        uplandNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        uplandNoise.SetFractalOctaves(3); uplandNoise.SetFractalLacunarity(2f); uplandNoise.SetFractalGain(0.5f); uplandNoise.SetFrequency(1.25f);

        var mountainProvinceNoise = new FastNoiseLite(seedBase + 5505);
        mountainProvinceNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        mountainProvinceNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        mountainProvinceNoise.SetFractalOctaves(3); mountainProvinceNoise.SetFractalLacunarity(2f); mountainProvinceNoise.SetFractalGain(0.5f); mountainProvinceNoise.SetFrequency(1.8f);

        var mountainRangeNoise = new FastNoiseLite(seedBase + 6606);
        mountainRangeNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        mountainRangeNoise.SetFractalType(FastNoiseLite.FractalType.Ridged);
        mountainRangeNoise.SetFractalOctaves(4); mountainRangeNoise.SetFractalLacunarity(2f); mountainRangeNoise.SetFractalGain(0.5f); mountainRangeNoise.SetFrequency(3.6f);

        var topologyShapeNoise = new FastNoiseLite(seedBase + 7707);
        topologyShapeNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        topologyShapeNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        topologyShapeNoise.SetFractalOctaves(3); topologyShapeNoise.SetFractalLacunarity(2f); topologyShapeNoise.SetFractalGain(0.5f); topologyShapeNoise.SetFrequency(1.65f);

        for (int y = 0; y < nh; y++) for (int x = 0; x < nw; x++)
        {
            int idx = y * nw + x;
            float u = (x + 0.5f) / nw;
            float v = (y + 0.5f) / nh;
            float longitude = (u * Mathf.PI * 2f) - Mathf.PI;
            float latitude = (0.5f - v) * Mathf.PI;
            float cosLat = Mathf.Cos(latitude);
            float dx = cosLat * Mathf.Cos(longitude);
            float dy = Mathf.Sin(latitude);
            float dz = cosLat * Mathf.Sin(longitude);
            float wx = dx, wy = dy, wz = dz;
            coastWarp.DomainWarp(ref wx, ref wy, ref wz);
            cachedMacroCoastNoise[idx] = macroCoastNoise.GetNoise(wx, wy, wz);
            cachedMidCoastNoise[idx] = midCoastNoise.GetNoise(wx, wy, wz);
            cachedCoastEdgeNoise[idx] = coastEdgeNoise.GetNoise(dx, dy, dz);
            cachedUplandProvinceNoise[idx] = uplandNoise.GetNoise(dx, dy, dz);
            cachedMountainProvinceNoise[idx] = mountainProvinceNoise.GetNoise(dx, dy, dz);
            cachedMountainRangeNoise[idx] = mountainRangeNoise.GetNoise(dx, dy, dz);
        }

        for (int y = 0; y < th; y++) for (int x = 0; x < tw; x++)
        {
            int idx = y * tw + x;
            float u = (x + 0.5f) / tw;
            float v = (y + 0.5f) / th;
            float longitude = (u * Mathf.PI * 2f) - Mathf.PI;
            float latitude = (0.5f - v) * Mathf.PI;
            float cosLat = Mathf.Cos(latitude);
            float dx = cosLat * Mathf.Cos(longitude);
            float dy = Mathf.Sin(latitude);
            float dz = cosLat * Mathf.Sin(longitude);
            cachedTopologyShapeNoise[idx] = topologyShapeNoise.GetNoise(dx, dy, dz);
        }

        cachedNoiseSeed = seedBase; cachedNoiseWidth = nw; cachedNoiseHeight = nh; cachedTopologyWidth = tw; cachedTopologyHeight = th;
        fastNoiseFieldsValid = true;
        if (logWorldGenerationDiagnostics)
            Debug.Log($"[WorldGenV2 FastNoise] Rebuilt cached sphere-space noise fields. Seed={seedBase} Size={nw}x{nh}");
    }

    private int CountTopologyLandCells(TopologyCell[] topo){
        int c=0; for(int i=0;i<topo.Length;i++) if(topo[i].isLand) c++; return c;
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

    private float SmoothMax(float a,float b,float k){ float h=Mathf.Clamp01(0.5f+0.5f*(b-a)/Mathf.Max(0.0001f,k)); return Mathf.Lerp(b,a,h)+k*h*(1f-h); }

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
    private float[] BuildMountainRankField(NativeArray<float> land, NativeArray<float> rawMountainPotential)
    {
        const int bins = 256;
        int[] hist = new int[bins];
        int landCount = 0;
        for (int i = 0; i < land.Length; i++)
        {
            if (land[i] <= 0.5f) continue;
            landCount++;
            int bin = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(rawMountainPotential[i]) * (bins - 1)), 0, bins - 1);
            hist[bin]++;
        }
        int[] cdf = new int[bins];
        int run = 0;
        for (int i = 0; i < bins; i++) { run += hist[i]; cdf[i] = run; }
        float[] rank = new float[land.Length];
        for (int i = 0; i < land.Length; i++)
        {
            if (land[i] <= 0.5f) { rank[i] = 0f; continue; }
            int bin = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(rawMountainPotential[i]) * (bins - 1)), 0, bins - 1);
            rank[i] = landCount > 0
                ? Mathf.Clamp01((cdf[bin] - hist[bin] * 0.5f) / (float)landCount)
                : 0f;
        }
        return rank;
    }

    private void WriteSurfaceAndStructure(float[] land,float[] elev,float[] mtn,float[] shelf,float[] cont){int n=mapWidth*mapHeight;var s=new Color32[n];var a=new Color32[n];var w=new Color32[n];for(int i=0;i<n;i++){s[i]=new Color32(B(land[i]),B(elev[i]),B(mtn[i]),B(shelf[i]));w[i]=new Color32(B(cont[i]),B(mtn[i]),B(elev[i]),0);a[i]=new Color32(B(shelf[i]),B(cont[i]),0,0);}surfaceDataTexture.SetPixelData(s,0);surfaceDataTexture.Apply(false,false);worldStructureTexture.SetPixelData(w,0);worldStructureTexture.Apply(false,false);auxiliaryMaskTexture.SetPixelData(a,0);auxiliaryMaskTexture.Apply(false,false);}    
    private void ReadSurface(out float[] land,out float[] elev,out float[] mtn,out float[] shelf){int n=mapWidth*mapHeight;land=new float[n];elev=new float[n];mtn=new float[n];shelf=new float[n];var p=surfaceDataTexture.GetPixels32();for(int i=0;i<n;i++){land[i]=p[i].r/255f;elev[i]=p[i].g/255f;mtn[i]=p[i].b/255f;shelf[i]=p[i].a/255f;}}
    private void ReadClimate(out float[] temp,out float[] moisture,out float[] cont){int n=mapWidth*mapHeight;temp=new float[n];moisture=new float[n];cont=new float[n];var p=climateTexture.GetPixels32();for(int i=0;i<n;i++){temp[i]=p[i].r/255f;moisture[i]=p[i].g/255f;cont[i]=p[i].b/255f;}}
    private void ReadHydrology(out float[] river, out float[] lake, out float[] wetness, out float[] flowOrDepth){int n=mapWidth*mapHeight;river=new float[n];lake=new float[n];wetness=new float[n];flowOrDepth=new float[n];var p=hydrologyMaskTexture.GetPixels32();for(int i=0;i<n;i++){river[i]=p[i].r/255f;lake[i]=p[i].g/255f;wetness[i]=p[i].b/255f;flowOrDepth[i]=p[i].a/255f;}}
    private void GenerateClimate(float[] land,float[] elev,float[] mtn,float[] contIn,out float[] temp,out float[] moisture){int n=mapWidth*mapHeight;temp=new float[n];moisture=new float[n];if(contIn==null){contIn=new float[n];for(int i=0;i<n;i++)contIn[i]=land[i]*Mathf.Clamp01(elev[i]);}var coast=DistanceFromBoundary(land,0.5f,true);var p=new Color32[n];for(int i=0;i<n;i++){int x=i%mapWidth;int y=i/mapWidth;float lat=Mathf.Abs(((float)y/(mapHeight-1))*2f-1f);float equatorWarmth=1f-lat;float continentality=contIn[i];float tempNoise=(Mathf.PerlinNoise((x+inputs.seed*0.31f)*0.006f,(y-inputs.seed*0.17f)*0.006f)-0.5f)*2f;float localTemperature=equatorWarmth-elev[i]*0.42f+continentality*inputs.continentalTemperatureStrength+tempNoise*inputs.climateNoiseStrength*0.35f;float coastProximity=Mathf.Clamp01(1f-coast[i]/(0.08f*mapHeight));float moistureNoise=(Mathf.PerlinNoise((x-inputs.seed*0.23f)*0.007f,(y+inputs.seed*0.29f)*0.007f)-0.5f)*2f;float localMoisture=0.5f+coastProximity*inputs.coastWetnessStrength-continentality*inputs.continentalDrynessStrength+moistureNoise*inputs.climateNoiseStrength+0.06f*(1f-lat);localTemperature=Mathf.Clamp01(localTemperature);localMoisture=Mathf.Clamp01(localMoisture);temp[i]=localTemperature;moisture[i]=localMoisture;p[i]=new Color32(B(localTemperature),B(localMoisture),B(continentality),0);}climateTexture.SetPixelData(p,0);climateTexture.Apply(false,false);}
    private void GenerateHydrology(float[] land,float[] elev,float[] moisture,ref GenDiagnostics diag){
        int n=mapWidth*mapHeight;float[] river=new float[n],lake=new float[n],wet=new float[n],priority=new float[n];
        float[] coastDist=DistanceFromBoundary(land,0.5f,true);
        int hydroSeed = Mathf.RoundToInt(inputs.seed * 1000f);
        var rng=new System.Random(hydroSeed*31+17);

        var inlandCandidates=new List<int>(n/4);
        float maxCoastDist=0.0001f;
        for(int i=0;i<n;i++) if(land[i]>0.5f) maxCoastDist=Mathf.Max(maxCoastDist,coastDist[i]);
        for(int i=0;i<n;i++) if(land[i]>0.5f && coastDist[i]>=decorativeRiverMinSourceCoastDistance) inlandCandidates.Add(i);

        int riverTarget=Mathf.Clamp(rng.Next(12,23)+(inlandCandidates.Count>n*0.22f?rng.Next(0,5):0),12,28), riversPainted=0, riversReachedCoast=0;
        int minRiverSteps=35, minStampedPixels=120, totalRiverLength=0, minRiverLength=int.MaxValue, maxRiverLength=0;
        var chosenSources=new List<int>();

        for(int ri=0;ri<riverTarget;ri++){
            if(inlandCandidates.Count==0) break;
            int source=PickGuidedRiverSource(inlandCandidates,chosenSources,land,elev,moisture,coastDist,maxCoastDist,rng);
            if(source<0) break;
            chosenSources.Add(source);
            float t=(ri+1f)/Mathf.Max(1f,riverTarget);
            float riverPriority=t<0.20f?Mathf.Lerp(0.8f,1f,(float)rng.NextDouble()):t<0.65f?Mathf.Lerp(0.55f,0.85f,(float)rng.NextDouble()):Mathf.Lerp(0.35f,0.65f,(float)rng.NextDouble());
            bool reachedCoast=PaintSplineMeanderingRiver(source,land,elev,moisture,coastDist,river,wet,priority,rng,riverPriority,out int stamped,out int steps);
            if(reachedCoast) riversReachedCoast++;
            if(stamped>=minStampedPixels && steps>=minRiverSteps){ riversPainted++; totalRiverLength+=steps; minRiverLength=Mathf.Min(minRiverLength,steps); maxRiverLength=Mathf.Max(maxRiverLength,steps); }
        }

        int lakeRequested=rng.Next(6,11), lakesPainted=0;
        for(int li=0;li<lakeRequested;li++){
            if(inlandCandidates.Count==0) break;
            int center=inlandCandidates[rng.Next(inlandCandidates.Count)];
            if(coastDist[center]<10f) continue;
            int baseRadius=rng.Next(20,46); float m=Mathf.Clamp01(moisture[center]); if(rng.NextDouble()<0.20+0.28*m) baseRadius=rng.Next(45,76);
            float lakePriority=baseRadius>=50?Mathf.Lerp(0.75f,1f,(float)rng.NextDouble()):baseRadius>=30?Mathf.Lerp(0.55f,0.80f,(float)rng.NextDouble()):Mathf.Lerp(0.40f,0.60f,(float)rng.NextDouble());
            if(PaintOrganicLakeBasin(center,baseRadius,land,lake,wet,priority,lakePriority,rng,li,coastDist)) lakesPainted++;
        }

        ApplyHydrologyWetnessHalo(land,river,lake,wet);
        int riverPixels=0,lakePixels=0,hydroPixels=0; var p=new Color32[n];
        for(int i=0;i<n;i++){ if(river[i]>0.04f) riverPixels++; if(lake[i]>0.04f) lakePixels++; if(river[i]>0.02f||lake[i]>0.02f) hydroPixels++; p[i]=new Color32(B(river[i]),B(lake[i]),B(wet[i]),B(priority[i])); }
        hydrologyMaskTexture.SetPixelData(p,0); hydrologyMaskTexture.Apply(false,false);
        diag.rivers=riversPainted; diag.lakes=lakesPainted;

        float avgRiverLength=riversPainted>0?(float)totalRiverLength/riversPainted:0f;
        if(minRiverLength==int.MaxValue) minRiverLength=0;
        if(logWorldGenerationDiagnostics) Debug.Log($"[WorldGenV2 Decorative Hydrology] RiversRequested={riverTarget} RiversPainted={riversPainted} RiversReachedCoast={riversReachedCoast} AvgRiverLength={avgRiverLength:F1} MinRiverLength={minRiverLength} MaxRiverLength={maxRiverLength} LakesRequested={lakeRequested} LakesPainted={lakesPainted} RiverPixels={riverPixels} LakePixels={lakePixels} HydrologyCoverage={(float)hydroPixels/Mathf.Max(1,n):F3}");
    }

    private int PickGuidedRiverSource(List<int> candidates,List<int> chosenSources,float[] land,float[] elevation,float[] moisture,float[] coastDistance,float maxCoastDist,System.Random r){
        int sampleCount=Mathf.Min(96,candidates.Count); int best=-1; float bestScore=float.NegativeInfinity;
        for(int s=0;s<sampleCount;s++){
            int idx=candidates[r.Next(candidates.Count)]; if(land[idx]<=0.5f||coastDistance[idx]<decorativeRiverMinSourceCoastDistance) continue;
            float coastN=Mathf.Clamp01(coastDistance[idx]/Mathf.Max(1f,maxCoastDist));
            float spacingPenalty=0f;
            for(int i=0;i<chosenSources.Count;i++){ Vector2 d=DeltaWrap(IndexToXY(chosenSources[i]),IndexToXY(idx)); float dist=d.magnitude; if(dist<minRiverSourceSpacingPixels*2.4f) spacingPenalty+=Mathf.Clamp01(1f-dist/(minRiverSourceSpacingPixels*2.4f)); }
            float score=coastN*0.65f + Mathf.Clamp01(elevation[idx])*0.20f + Mathf.Clamp01(moisture[idx])*0.15f - spacingPenalty*0.40f + ((float)r.NextDouble()-0.5f)*0.06f;
            if(score>bestScore){bestScore=score; best=idx;}
        }
        return best;
    }

    private bool PaintSplineMeanderingRiver(int sourceIndex,float[] land,float[] elevation,float[] moisture,float[] coastDistance,float[] river,float[] wet,float[] priority,System.Random r,float riverPriority,out int stampedPixelCount,out int stepCount){
        stampedPixelCount=0; stepCount=0; if(land[sourceIndex]<=0.5f) return false;
        int coastTarget=PickRiverCoastTarget(sourceIndex,land,coastDistance,r);
        if(coastTarget<0) return false;
        Vector2 source=IndexToXY(sourceIndex); Vector2 target=IndexToXY(coastTarget);
        var control=BuildRiverControlPoints(source,target,r,sourceIndex);
        if(control.Count<4) return false;
        float routeDistance=DeltaWrap(source,target).magnitude;
        int samples=Mathf.Clamp(Mathf.RoundToInt(routeDistance*1.25f),90,520);
        bool reachedCoast=false; int consecutiveFail=0;
        for(int i=0;i<samples;i++){
            float t=i/(float)Mathf.Max(1,samples-1);
            Vector2 p=SampleCatmullSpline(control,t);
            int idx=XYToWrappedIndex(p);
            if(land[idx]<=0.5f){ int snap=FindNearestLand((int)Mathf.Round(p.x),(int)Mathf.Round(p.y),land,decorativeRiverLandSnapRadius); if(snap>=0) idx=snap; else { consecutiveFail++; if(consecutiveFail>14) break; continue; }}
            consecutiveFail=0;
            float widthT=Mathf.SmoothStep(0f,1f,t); float radius=Mathf.Lerp(decorativeRiverUpperRadius,decorativeRiverLowerRadius,widthT);
            if(t>0.87f) radius+=Mathf.Lerp(0f,1.5f,(t-0.87f)/0.13f);
            stampedPixelCount+=StampRiverAtIndex(idx,radius,riverPriority,land,river,wet,priority,sourceIndex+i*17);
            stepCount++;
            if(coastDistance[idx]<=2.5f) reachedCoast=true;
        }
        return reachedCoast;
    }

    private int PickRiverCoastTarget(int sourceIndex,float[] land,float[] coastDistance,System.Random r){
        var candidates=new List<(int idx,float dist)>(); Vector2 source=IndexToXY(sourceIndex);
        for(int i=0;i<land.Length;i+=Mathf.Max(1,riverCandidateStride)){
            if(land[i]<=0.5f || coastDistance[i]>2.5f) continue;
            float d=DeltaWrap(source,IndexToXY(i)).magnitude; if(d<decorativeRiverMinSourceCoastDistance*0.45f) continue;
            candidates.Add((i,d));
        }
        if(candidates.Count==0) return -1;
        candidates.Sort((a,b)=>a.dist.CompareTo(b.dist));
        int nearCount=Mathf.Max(1,Mathf.Min(candidates.Count,Mathf.CeilToInt(candidates.Count*0.25f)));
        if(r.NextDouble()<0.70) return candidates[r.Next(nearCount)].idx;
        int fartherStart=Mathf.Min(candidates.Count-1,nearCount);
        return candidates[r.Next(fartherStart,candidates.Count)].idx;
    }

    private List<Vector2> BuildRiverControlPoints(Vector2 source,Vector2 target,System.Random r,int riverSeed){
        int controlCount=Mathf.Clamp(r.Next(decorativeRiverControlPointCountMin,decorativeRiverControlPointCountMax+1),4,32);
        var pts=new List<Vector2>(controlCount); Vector2 delta=DeltaWrap(source,target); float dist=Mathf.Max(1f,delta.magnitude); Vector2 dir=delta/dist; Vector2 perp=new Vector2(-dir.y,dir.x);
        for(int k=0;k<controlCount;k++){
            float t=controlCount<=1?0f:k/(float)(controlCount-1); Vector2 basePoint=source+delta*t; float envelope=Mathf.Sin(Mathf.PI*t);
            float sign=(k%2==0?1f:-1f) * (((riverSeed+k)&1)==0?1f:-1f);
            float lateral=dist*decorativeRiverLateralWanderFraction*decorativeRiverMeanderStrength*envelope*sign*Mathf.Lerp(0.65f,1.25f,(float)r.NextDouble());
            Vector2 cp=basePoint+perp*lateral; cp.x=(cp.x%mapWidth+mapWidth)%mapWidth; cp.y=Mathf.Clamp(cp.y,0,mapHeight-1); pts.Add(cp);
        }
        return pts;
    }

    private Vector2 SampleCatmullSpline(List<Vector2> pts,float t){
        int segCount=pts.Count-1; float ft=t*segCount; int i=Mathf.Clamp(Mathf.FloorToInt(ft),0,segCount-1); float lt=ft-i;
        Vector2 p0=pts[Mathf.Max(0,i-1)],p1=pts[i],p2=pts[i+1],p3=pts[Mathf.Min(pts.Count-1,i+2)];
        return CatmullRom(p0,p1,p2,p3,lt);
    }

    private Vector2 CatmullRom(Vector2 p0,Vector2 p1,Vector2 p2,Vector2 p3,float t){
        float t2=t*t,t3=t2*t;
        return 0.5f*((2f*p1)+(-p0+p2)*t+(2f*p0-5f*p1+4f*p2-p3)*t2+(-p0+3f*p1-3f*p2+p3)*t3);
    }

    private int XYToWrappedIndex(Vector2 p){ int x=((Mathf.RoundToInt(p.x)%mapWidth)+mapWidth)%mapWidth; int y=Mathf.Clamp(Mathf.RoundToInt(p.y),0,mapHeight-1); return y*mapWidth+x; }

    private int StampRiverAtIndex(int centerIndex,float radius,float riverPriority,float[] land,float[] river,float[] wet,float[] priority,int riverId){int cx=centerIndex%mapWidth,cy=centerIndex/mapWidth; int ir=Mathf.CeilToInt(radius+1f); int stamped=0; for(int oy=-ir;oy<=ir;oy++)for(int ox=-ir;ox<=ir;ox++){int nx=(cx+ox+mapWidth)%mapWidth,ny=cy+oy; if(ny<0||ny>=mapHeight) continue; int ni=ny*mapWidth+nx; if(land[ni]<=0.5f) continue; float d=Mathf.Sqrt(ox*ox+oy*oy); float edgeN=(Mathf.PerlinNoise((nx+riverId*13.1f+inputs.seed)*0.19f,(ny-riverId*7.3f-inputs.seed)*0.19f)-0.5f)*0.45f; float allowed=radius+edgeN; if(d>allowed) continue; float core=Mathf.Clamp01(1f-d/Mathf.Max(0.01f,allowed)); float channel=Mathf.Lerp(0.42f,1f,core); river[ni]=Mathf.Max(river[ni],channel); wet[ni]=Mathf.Max(wet[ni],Mathf.Lerp(0.35f,0.9f,core)); priority[ni]=Mathf.Max(priority[ni],riverPriority); stamped++;} return stamped; }

    private bool PaintOrganicLakeBasin(int centerIndex,int baseRadius,float[] land,float[] lake,float[] wet,float[] priority,float lakePriority,System.Random rng,int lakeId,float[] coastDistance){int cx=centerIndex%mapWidth,cy=centerIndex/mapWidth; if(coastDistance[centerIndex]<8f) return false; float stretch=Mathf.Lerp(0.75f,1.35f,(float)rng.NextDouble()); float angle=(float)rng.NextDouble()*Mathf.PI*2f; Vector2 axisX=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle)); Vector2 axisY=new Vector2(-axisX.y,axisX.x); int lobeCount=rng.Next(2,5); var lobeOffsets=new Vector2[lobeCount]; var lobeRadii=new float[lobeCount]; for(int i=0;i<lobeCount;i++){float a=(float)rng.NextDouble()*Mathf.PI*2f; float dist=baseRadius*Mathf.Lerp(0.25f,0.95f,(float)rng.NextDouble()); lobeOffsets[i]=new Vector2(Mathf.Cos(a),Mathf.Sin(a))*dist; lobeRadii[i]=baseRadius*Mathf.Lerp(0.35f,0.85f,(float)rng.NextDouble()); }
        float threshold=Mathf.Lerp(0.57f,0.72f,(float)rng.NextDouble()); int reach=Mathf.CeilToInt(baseRadius*1.9f); int validLand=0, candidate=0, stamped=0;
        for(int oy=-reach;oy<=reach;oy++)for(int ox=-reach;ox<=reach;ox++){int nx=(cx+ox+mapWidth)%mapWidth,ny=cy+oy; if(ny<0||ny>=mapHeight) continue; int ni=ny*mapWidth+nx; candidate++; if(land[ni]>0.5f) validLand++; Vector2 d=new Vector2(ox,oy); float lx=Vector2.Dot(d,axisX), ly=Vector2.Dot(d,axisY); float rx=baseRadius*stretch, ry=baseRadius/Mathf.Max(0.2f,stretch); float e=Mathf.Sqrt((lx*lx)/(rx*rx)+(ly*ly)/(ry*ry)); float baseEllipse=Mathf.Clamp01(1f-e);
            float strongestLobe=0f; for(int li=0;li<lobeCount;li++){Vector2 dl=d-lobeOffsets[li]; float nd=dl.magnitude/Mathf.Max(1f,lobeRadii[li]); strongestLobe=Mathf.Max(strongestLobe,Mathf.Clamp01(1f-nd));}
            float shoreNoise=Mathf.PerlinNoise((nx+inputs.seed*0.17f+lakeId*1.93f)*0.065f,(ny-inputs.seed*0.21f-lakeId*2.31f)*0.065f);
            float shape=baseEllipse*0.65f + strongestLobe*0.30f + shoreNoise*0.18f;
            if(shape<=threshold) continue; if(land[ni]<=0.5f) continue;
            float fill=Mathf.Clamp01((shape-threshold)/Mathf.Max(0.05f,1f-threshold)); fill=Mathf.SmoothStep(0f,1f,fill);
            lake[ni]=Mathf.Max(lake[ni],fill); wet[ni]=Mathf.Max(wet[ni],Mathf.Lerp(0.45f,1f,fill)); priority[ni]=Mathf.Max(priority[ni],lakePriority); stamped++; }
        float landRatio=candidate>0?(float)validLand/candidate:0f;
        return stamped>260 && landRatio>=0.58f;
    }

    private void ApplyHydrologyWetnessHalo(float[] land,float[] river,float[] lake,float[] wet){for(int y=0;y<mapHeight;y++)for(int x=0;x<mapWidth;x++){int i=y*mapWidth+x; if(land[i]<=0.5f) continue; float src=Mathf.Max(river[i],lake[i]); if(src<=0.01f) continue; int r=lake[i]>river[i]?10:7; for(int oy=-r;oy<=r;oy++)for(int ox=-r;ox<=r;ox++){int nx=(x+ox+mapWidth)%mapWidth,ny=y+oy; if(ny<0||ny>=mapHeight) continue; int ni=ny*mapWidth+nx; if(land[ni]<=0.5f) continue; float d=Mathf.Sqrt(ox*ox+oy*oy); if(d>r) continue; float falloff=1f-d/r; float baseV=lake[i]>river[i]?Mathf.Lerp(0.40f,0.75f,falloff):Mathf.Lerp(0.35f,0.70f,falloff); wet[ni]=Mathf.Max(wet[ni],baseV*src); }}}
    private int FindNearestLand(int x,int y,float[] land,int radius){for(int r=0;r<=radius;r++)for(int oy=-r;oy<=r;oy++)for(int ox=-r;ox<=r;ox++){int nx=(x+ox+mapWidth)%mapWidth,ny=y+oy; if(ny<0||ny>=mapHeight) continue; int ni=ny*mapWidth+nx; if(land[ni]>0.5f) return ni;} return -1;}
    private Vector2 IndexToXY(int index)=>new Vector2(index%mapWidth,index/mapWidth);
    private Vector2 DeltaWrap(Vector2 from,Vector2 to){float dx=to.x-from.x; if(Mathf.Abs(dx)>mapWidth*0.5f) dx-=Mathf.Sign(dx)*mapWidth; return new Vector2(dx,to.y-from.y);}
    private Vector2 Bezier(Vector2 p0,Vector2 p1,Vector2 p2,Vector2 p3,float t){float u=1f-t; return u*u*u*p0+3f*u*u*t*p1+3f*u*t*t*p2+t*t*t*p3;}
    [BurstCompile] private struct LandUpsampleAndDistanceJob : IJobFor
    {
        [ReadOnly] public NativeArray<float> topoLandCoastDistance, topoOceanCoastDistance, topoSignedCoastDistance;
        [ReadOnly] public NativeArray<float> macroCoastNoise, midCoastNoise, coastEdgeNoise;
        public int noiseFieldWidth, noiseFieldHeight;
        public int mapWidth, mapHeight, topoWidth, topoHeight; public float seed, coastlineDeformationWidthCells, coastlineWarpStrength, coastlineMidNoiseStrength, coastlineEdgeNoiseStrength, coastlineSoftness, coastlineThresholdBias;
        public NativeArray<float> land, inlandDistance, offshoreDistance;
        public void Execute(int i){int x=i%mapWidth,y=i/mapWidth; float2 uv=new float2(((float)x/mapWidth)*topoWidth,((float)y/mapHeight)*topoHeight); SampleBilinear(uv, topoLandCoastDistance, out var inD); SampleBilinear(uv, topoOceanCoastDistance, out var offD); SampleBilinear(uv, topoSignedCoastDistance, out var signedTopoDistance); float2 noiseUV=new float2(((float)x/mapWidth)*noiseFieldWidth,((float)y/mapHeight)*noiseFieldHeight); SampleNoiseBilinear(noiseUV, macroCoastNoise, out var macro); SampleNoiseBilinear(noiseUV, midCoastNoise, out var mid); SampleNoiseBilinear(noiseUV, coastEdgeNoise, out var edge); float deformationWidth=math.max(0.001f,coastlineDeformationWidthCells); float signedEnvelope=signedTopoDistance/deformationWidth; float coastInfluence=1f-math.saturate(math.abs(signedEnvelope)); coastInfluence=coastInfluence*coastInfluence*(3f-2f*coastInfluence); float macroDisplacement=macro*coastlineWarpStrength; float midDisplacement=mid*coastlineMidNoiseStrength; float fineDisplacement=edge*coastlineEdgeNoiseStrength; float coastlineNoiseDisplacement=macroDisplacement+midDisplacement+fineDisplacement; float finalCoastSignal=signedEnvelope+coastlineNoiseDisplacement*coastInfluence-coastlineThresholdBias; land[i]=math.smoothstep(-coastlineSoftness,coastlineSoftness,finalCoastSignal); inlandDistance[i]=inD; offshoreDistance[i]=offD;}
        void SampleBilinear(float2 uv, NativeArray<float> arr, out float value){int x0=((int)math.floor(uv.x)%topoWidth+topoWidth)%topoWidth,y0=math.clamp((int)math.floor(uv.y),0,topoHeight-1); int x1=(x0+1)%topoWidth,y1=math.min(y0+1,topoHeight-1); float fx=uv.x-math.floor(uv.x),fy=uv.y-math.floor(uv.y); float v00=arr[y0*topoWidth+x0],v10=arr[y0*topoWidth+x1],v01=arr[y1*topoWidth+x0],v11=arr[y1*topoWidth+x1]; value=math.lerp(math.lerp(v00,v10,fx),math.lerp(v01,v11,fx),fy);}
        void SampleNoiseBilinear(float2 uv, NativeArray<float> arr, out float value){int x0=((int)math.floor(uv.x)%noiseFieldWidth+noiseFieldWidth)%noiseFieldWidth,y0=math.clamp((int)math.floor(uv.y),0,noiseFieldHeight-1); int x1=(x0+1)%noiseFieldWidth,y1=math.min(y0+1,noiseFieldHeight-1); float fx=uv.x-math.floor(uv.x),fy=uv.y-math.floor(uv.y); float v00=arr[y0*noiseFieldWidth+x0],v10=arr[y0*noiseFieldWidth+x1],v01=arr[y1*noiseFieldWidth+x0],v11=arr[y1*noiseFieldWidth+x1]; value=math.lerp(math.lerp(v00,v10,fx),math.lerp(v01,v11,fx),fy);}
    }
    [BurstCompile] private struct TerrainPotentialJob : IJobFor
    {
        [ReadOnly] public NativeArray<float> uplandProvinceNoise,mountainProvinceNoise,mountainRangeNoise;
        public int noiseFieldWidth,noiseFieldHeight;
        [ReadOnly] public NativeArray<float> land,inlandDistance; public int mapWidth,mapHeight; public float seed;
        public NativeArray<float> rawMountainPotential,uplandPotential;
        public void Execute(int i){int x=i%mapWidth,y=i/mapWidth; float inland=math.saturate(inlandDistance[i]/(0.24f*mapHeight)); float2 uv=new float2(((float)x/mapWidth)*noiseFieldWidth,((float)y/mapHeight)*noiseFieldHeight); SampleNoiseBilinear(uv,uplandProvinceNoise,out var uplandRaw); SampleNoiseBilinear(uv,mountainProvinceNoise,out var mountainProvinceRaw); SampleNoiseBilinear(uv,mountainRangeNoise,out var mountainRangeRaw); float upland01=uplandRaw*0.5f+0.5f; float mountainProvince01=mountainProvinceRaw*0.5f+0.5f; float mountainRange01=mountainRangeRaw*0.5f+0.5f; float upland=math.smoothstep(0.42f,0.76f,upland01)*inland; uplandPotential[i]=upland; float mountainProvince=math.smoothstep(0.45f,0.78f,mountainProvince01); float rangeBelts=math.smoothstep(0.40f,0.82f,mountainRange01); rawMountainPotential[i]=land[i]*inland*(mountainProvince*0.55f+rangeBelts*0.45f);}
        void SampleNoiseBilinear(float2 uv, NativeArray<float> arr, out float value){int x0=((int)math.floor(uv.x)%noiseFieldWidth+noiseFieldWidth)%noiseFieldWidth,y0=math.clamp((int)math.floor(uv.y),0,noiseFieldHeight-1); int x1=(x0+1)%noiseFieldWidth,y1=math.min(y0+1,noiseFieldHeight-1); float fx=uv.x-math.floor(uv.x),fy=uv.y-math.floor(uv.y); float v00=arr[y0*noiseFieldWidth+x0],v10=arr[y0*noiseFieldWidth+x1],v01=arr[y1*noiseFieldWidth+x0],v11=arr[y1*noiseFieldWidth+x1]; value=math.lerp(math.lerp(v00,v10,fx),math.lerp(v01,v11,fx),fy);}
    }
    [BurstCompile] private struct MountainFinalizeJob : IJobFor
    {
        [ReadOnly] public NativeArray<float> land,inlandDistance,offshoreDistance,uplandPotential,rawMountainPotential,mountainRank; public int mapHeight;
        public NativeArray<float> mountain,elevation,shelf,continentality;
        public void Execute(int i){float l=land[i]; float inland=math.saturate(inlandDistance[i]/(0.24f*mapHeight)); float m=l*math.saturate(mountainRank[i]); mountain[i]=m; float e=l*math.saturate(0.20f*inland + 0.55f*uplandPotential[i] + 0.25f*rawMountainPotential[i]); elevation[i]=e; shelf[i]=(1f-l)*math.saturate(1f-offshoreDistance[i]/(0.04f*mapHeight)); continentality[i]=l*inland;}
    }
    private void GenerateBiomes(float[] land,float[] elev,float[] mtn,float[] temp,float[] moisture,float[] river,float[] lake,float[] hydroWetness){int n=mapWidth*mapHeight;var o0=new Color32[n];var o1=new Color32[n];var o2=new Color32[n];float sharpness=Mathf.Lerp(0.75f,2.0f,Mathf.InverseLerp(0.5f,1.25f,inputs.biomeCompetitionSharpness));for(int i=0;i<n;i++){if(land[i]<0.5f){o0[i]=o1[i]=o2[i]=new Color32();continue;}float hot=temp[i],wet=moisture[i];float provinceNoise=(Mathf.PerlinNoise(((i%mapWidth)+inputs.seed*0.41f)*0.004f,((i/mapWidth)-inputs.seed*0.37f)*0.004f)-0.5f)*2f*inputs.biomeProvinceStrength*0.18f;float jungle=Mathf.Clamp01((hot-0.6f)*2f*(wet-0.6f+provinceNoise)*2f);float desert=Mathf.Clamp01((hot-0.5f)*2f*(0.7f-wet-provinceNoise*0.5f)*2f);float sav=Mathf.Clamp01((hot-0.5f)*2f*(1f-Mathf.Abs(wet-0.5f+provinceNoise*0.3f)*2f));float grass=Mathf.Clamp01((1f-Mathf.Abs(hot-0.5f)*2f)*(1f-Mathf.Abs(wet-0.45f-provinceNoise*0.25f)*2f));float forest=Mathf.Clamp01((1f-Mathf.Abs(hot-0.5f)*2f)*(wet-0.45f+provinceNoise)*2f);float taiga=Mathf.Clamp01((0.55f-hot)*2f*(wet-0.35f+provinceNoise*0.4f)*2f);float tundra=Mathf.Clamp01((0.45f-hot)*2.5f);float polar=Mathf.Clamp01((0.3f-hot)*3f+elev[i]*0.2f+mtn[i]*0.2f);float lowlandFactor=Mathf.Clamp01((0.55f-elev[i])*2f);float marsh=Mathf.Clamp01(lowlandFactor*(hydroWetness[i]*Mathf.Lerp(0.5f,1.25f,inputs.riparianWetnessStrength)+lake[i]*0.65f+river[i]*0.30f)+wet*0.08f);jungle=Mathf.Pow(jungle,sharpness);desert=Mathf.Pow(desert,sharpness);sav=Mathf.Pow(sav,sharpness);grass=Mathf.Pow(grass,sharpness);forest=Mathf.Pow(forest,sharpness);taiga=Mathf.Pow(taiga,sharpness);tundra=Mathf.Pow(tundra,sharpness);polar=Mathf.Pow(polar,sharpness);marsh=Mathf.Pow(marsh,sharpness);float sum=jungle+desert+sav+grass+forest+taiga+tundra+polar+marsh+1e-5f;o0[i]=new Color32(B(jungle/sum),B(desert/sum),B(sav/sum),B(grass/sum));o1[i]=new Color32(B(forest/sum),B(taiga/sum),B(tundra/sum),B(polar/sum));o2[i]=new Color32(B(marsh/sum),B(polar),B(mtn[i]),0);}biomeWeights0Texture.SetPixelData(o0,0);biomeWeights0Texture.Apply(false,false);biomeWeights1Texture.SetPixelData(o1,0);biomeWeights1Texture.Apply(false,false);biomeWeights2Texture.SetPixelData(o2,0);biomeWeights2Texture.Apply(false,false);}
}
