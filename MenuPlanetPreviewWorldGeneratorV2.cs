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
    [SerializeField, Range(1, 12)] private int maxTopologyAttempts = 8;
    [SerializeField, Range(0f, 0.5f)] private float coastlineWarpStrength = 0.16f;
    [SerializeField, Range(0f, 0.5f)] private float coastlineEdgeNoiseStrength = 0.12f;
    [SerializeField, Range(0.001f, 0.08f)] private float coastlineSoftness = 0.018f;
    [SerializeField] private bool logWorldGenerationDiagnostics = true;

    [Header("Hydrology Targets")]
    [SerializeField] private Vector2Int sparseRiverRange = new Vector2Int(12, 20);
    [SerializeField] private Vector2Int standardRiverRange = new Vector2Int(24, 40);
    [SerializeField] private Vector2Int abundantRiverRange = new Vector2Int(45, 70);

    [SerializeField, Range(4, 64)] private int minRiverSourceSpacingPixels = 18;
    [SerializeField, Range(1, 16)] private int riverCandidateStride = 4;

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
    [Serializable] private struct PreviewLandPresetProfileV2
    {
        public string name; public float minLandCoverage, maxLandCoverage; public int minPrimaryLandGroups, maxPrimaryLandGroups; public float minSeedSeparationDegrees;
        public float compactnessBias, irregularityBias, elongationBias, islandFragmentBias; public bool requireSingleDominantLandmass; public float minLargestLandmassShare;
        public int targetSatelliteIslandClustersMin, targetSatelliteIslandClustersMax;
    }

    private readonly PreviewLandPresetProfileV2[] presets = new PreviewLandPresetProfileV2[6]
    {
        new PreviewLandPresetProfileV2{name="Archipelago",minLandCoverage=0.12f,maxLandCoverage=0.26f,minPrimaryLandGroups=10,maxPrimaryLandGroups=18,minSeedSeparationDegrees=7f,compactnessBias=0.28f,irregularityBias=0.88f,elongationBias=0.18f,islandFragmentBias=0.95f,requireSingleDominantLandmass=false,minLargestLandmassShare=0f,targetSatelliteIslandClustersMin=4,targetSatelliteIslandClustersMax=10},
        new PreviewLandPresetProfileV2{name="Islands",minLandCoverage=0.22f,maxLandCoverage=0.38f,minPrimaryLandGroups=5,maxPrimaryLandGroups=9,minSeedSeparationDegrees=12f,compactnessBias=0.42f,irregularityBias=0.72f,elongationBias=0.28f,islandFragmentBias=0.65f,requireSingleDominantLandmass=false,minLargestLandmassShare=0f,targetSatelliteIslandClustersMin=3,targetSatelliteIslandClustersMax=7},
        new PreviewLandPresetProfileV2{name="Standard",minLandCoverage=0.34f,maxLandCoverage=0.52f,minPrimaryLandGroups=3,maxPrimaryLandGroups=5,minSeedSeparationDegrees=21f,compactnessBias=0.80f,irregularityBias=0.34f,elongationBias=0.18f,islandFragmentBias=0.25f,requireSingleDominantLandmass=false,minLargestLandmassShare=0f,targetSatelliteIslandClustersMin=2,targetSatelliteIslandClustersMax=5},
        new PreviewLandPresetProfileV2{name="Large Continents",minLandCoverage=0.48f,maxLandCoverage=0.64f,minPrimaryLandGroups=2,maxPrimaryLandGroups=4,minSeedSeparationDegrees=28f,compactnessBias=0.72f,irregularityBias=0.46f,elongationBias=0.54f,islandFragmentBias=0.14f,requireSingleDominantLandmass=false,minLargestLandmassShare=0f,targetSatelliteIslandClustersMin=1,targetSatelliteIslandClustersMax=4},
        new PreviewLandPresetProfileV2{name="Pangaea",minLandCoverage=0.44f,maxLandCoverage=0.60f,minPrimaryLandGroups=1,maxPrimaryLandGroups=1,minSeedSeparationDegrees=0f,compactnessBias=0.64f,irregularityBias=0.62f,elongationBias=0.76f,islandFragmentBias=0.04f,requireSingleDominantLandmass=true,minLargestLandmassShare=0.94f,targetSatelliteIslandClustersMin=0,targetSatelliteIslandClustersMax=2},
        new PreviewLandPresetProfileV2{name="Terrestrial",minLandCoverage=0.68f,maxLandCoverage=0.82f,minPrimaryLandGroups=2,maxPrimaryLandGroups=4,minSeedSeparationDegrees=22f,compactnessBias=0.66f,irregularityBias=0.36f,elongationBias=0.40f,islandFragmentBias=0.06f,requireSingleDominantLandmass=false,minLargestLandmassShare=0f,targetSatelliteIslandClustersMin=0,targetSatelliteIslandClustersMax=3}
    };

    private MenuPlanetPreviewWorldInputs inputs;
    private PreviewWorldRebuildScope pending;
    private float scheduledAt = -1f;

    public void SetInputs(MenuPlanetPreviewWorldInputs v) => inputs = v;
    public void RequestRebuild(PreviewWorldRebuildScope scope, bool immediate = false) { pending |= ExpandDependencies(scope); if (immediate) Flush(); else scheduledAt = Time.time + regenerationDelay; }
    public void Release() { DestroyTex(ref surfaceDataTexture); DestroyTex(ref auxiliaryMaskTexture); DestroyTex(ref worldStructureTexture); DestroyTex(ref climateTexture); DestroyTex(ref hydrologyMaskTexture); DestroyTex(ref biomeWeights0Texture); DestroyTex(ref biomeWeights1Texture); DestroyTex(ref biomeWeights2Texture); }
    private void Update() { if (scheduledAt > 0f && Time.time >= scheduledAt) Flush(); }
    private PreviewWorldRebuildScope ExpandDependencies(PreviewWorldRebuildScope r) => (r & PreviewWorldRebuildScope.Tectonics) != 0 ? PreviewWorldRebuildScope.All : (r & PreviewWorldRebuildScope.Climate) != 0 ? PreviewWorldRebuildScope.Climate | PreviewWorldRebuildScope.Hydrology | PreviewWorldRebuildScope.Biomes : (r & PreviewWorldRebuildScope.Hydrology) != 0 ? PreviewWorldRebuildScope.Hydrology | PreviewWorldRebuildScope.Biomes : (r & PreviewWorldRebuildScope.Biomes) != 0 ? PreviewWorldRebuildScope.Biomes : 0;

    private struct GenDiagnostics { public string preset; public float targetLand; public float actualLand; public int groupCount; public float largestGroupShare; public int attempts; public int rivers; public int lakes; public float avgElevation; public float maxElevation; public float mountainCoverage; }

    private void Flush()
    {
        scheduledAt = -1f;
        var s = pending; pending = PreviewWorldRebuildScope.None; if (s == PreviewWorldRebuildScope.None) return;
        EnsureAllTextures();
        float[] land = null, elev = null, mtn = null, shelf = null, cont = null, moisture = null, temp = null;
        GenDiagnostics diag = default;
        if ((s & PreviewWorldRebuildScope.Tectonics) != 0)
        {
            GenerateTerrain(out land, out elev, out mtn, out shelf, out cont, out diag);
            WriteSurfaceAndStructure(land, elev, mtn, shelf, cont);
        }
        if ((s & PreviewWorldRebuildScope.Climate) != 0)
        {
            ReadSurface(out land, out elev, out mtn, out shelf);
            GenerateClimate(land, elev, mtn, cont, out temp, out moisture);
        }
        if ((s & PreviewWorldRebuildScope.Hydrology) != 0)
        {
            if (land == null) ReadSurface(out land, out elev, out mtn, out shelf);
            if (temp == null || moisture == null) ReadClimate(out temp, out moisture, out cont);
            GenerateHydrology(land, elev, moisture, ref diag);
        }
        if ((s & PreviewWorldRebuildScope.Biomes) != 0)
        {
            if (land == null) ReadSurface(out land, out elev, out mtn, out shelf);
            if (temp == null || moisture == null) ReadClimate(out temp, out moisture, out cont);
            ReadHydrology(out var river, out var lake, out var hydroWetness, out _);
            GenerateBiomes(land, elev, mtn, temp, moisture, river, lake, hydroWetness);
        }
        if (logWorldGenerationDiagnostics && (s & PreviewWorldRebuildScope.Tectonics) != 0)
        {
            Debug.Log($"[WorldGenV2] preset={diag.preset} targetLand={diag.targetLand:F3} actualLand={diag.actualLand:F3} groups={diag.groupCount} largestShare={diag.largestGroupShare:F3} attempts={diag.attempts} rivers={diag.rivers} lakes={diag.lakes} AvgElevation={diag.avgElevation:F3} MaxElevation={diag.maxElevation:F3} MountainCoverage={diag.mountainCoverage:P1}");
        }
        WorldTexturesUpdated?.Invoke();
    }

    private void EnsureAllTextures(){Ensure(ref surfaceDataTexture,"MenuSurfaceDataV2");Ensure(ref auxiliaryMaskTexture,"MenuAuxMasksV2");Ensure(ref worldStructureTexture,"MenuStructureV2");Ensure(ref climateTexture,"MenuClimateV2");Ensure(ref hydrologyMaskTexture,"MenuHydrologyV2");Ensure(ref biomeWeights0Texture,"MenuBiome0V2");Ensure(ref biomeWeights1Texture,"MenuBiome1V2");Ensure(ref biomeWeights2Texture,"MenuBiome2V2");}
    private void Ensure(ref Texture2D t,string n){if(t!=null&&t.width==mapWidth&&t.height==mapHeight)return;DestroyTex(ref t);t=new Texture2D(mapWidth,mapHeight,TextureFormat.RGBA32,false,true){name=n,wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Bilinear};}
    private void DestroyTex(ref Texture2D t){if(t!=null)Destroy(t);t=null;}
    private static byte B(float v)=> (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(v)*255f),0,255);

    // concise but complete implementation
    private void GenerateTerrain(out float[] land,out float[] elev,out float[] mtn,out float[] shelf,out float[] cont,out GenDiagnostics diag){
        int tw=Mathf.Max(16,topologyWidth),th=Mathf.Max(8,topologyHeight),tn=tw*th; diag=default;
        int presetIndex=Mathf.Clamp(inputs.landPresetIndex,0,presets.Length-1); var preset=presets[presetIndex]; diag.preset=preset.name;
        float t=Mathf.Clamp01(inputs.landThreshold); float target=Mathf.Lerp(preset.minLandCoverage,preset.maxLandCoverage,1f-t); diag.targetLand=target;
        TopologyCell[] topo=new TopologyCell[tn];
        int bestAttempt=1; float bestScore=-1f; TopologyCell[] bestTopo=null; int bestGroups=0; float bestLargest=0f;
        int seedBase = Mathf.RoundToInt(inputs.seed * 1000f);
        for(int attempt=1; attempt<=Mathf.Max(1,maxTopologyAttempts); attempt++){
            var r=new System.Random((seedBase*73856093) ^ (attempt*19349663));
            for(int i=0;i<tn;i++){topo[i].isLand=false;topo[i].groupId=-1;}
            int groups=r.Next(preset.minPrimaryLandGroups,preset.maxPrimaryLandGroups+1);
            var seeds=new List<Vector2Int>();
            for(int g=0;g<groups;g++){
                for(int k=0;k<60;k++){
                    int seedX=r.Next(tw), seedY=r.Next(th); var candidate=new Vector2Int(seedX,seedY); bool ok=true;
                    foreach(var sx in seeds){float dx=Mathf.Min(Mathf.Abs(candidate.x-sx.x), tw-Mathf.Abs(candidate.x-sx.x)); float dy=Mathf.Abs(candidate.y-sx.y); if(Mathf.Sqrt(dx*dx+dy*dy)<preset.minSeedSeparationDegrees*tw/360f){ok=false;break;}}
                    if(ok||k==59){seeds.Add(candidate); int idx=seedY*tw+seedX; topo[idx].isLand=true; topo[idx].groupId=g; break;}
                }
            }
            int targetCells=Mathf.Clamp(Mathf.RoundToInt(target*tn),groups,tn-2);
            var frontier=new List<int>[groups]; for(int g=0;g<groups;g++) frontier[g]=new List<int>{seeds[g].y*tw+seeds[g].x}; int placed=groups;
            while(placed<targetCells){ bool any=false; for(int g=0;g<groups&&placed<targetCells;g++){ if(frontier[g].Count==0) continue; int fidx=frontier[g][r.Next(frontier[g].Count)]; int fx=fidx%tw, fy=fidx/tw; for(int d=0;d<4;d++){int nx=(fx + (d==0?1:d==1?-1:0) + tw)%tw, ny=fy + (d==2?1:d==3?-1:0); if(ny<0||ny>=th) continue; int ni=ny*tw+nx; if(topo[ni].isLand) continue; topo[ni].isLand=true; topo[ni].groupId=g; frontier[g].Add(ni); placed++; any=true; break;} } if(!any) break; }
            SmoothBroadPresetTopology(topo, tw, th, preset.name); int[] comp; int compCount; float largest=LargestLandmassShare(topo,tw,th,out compCount,out comp);
            bool valid=IsTopologyValidForPreset(preset, largest, compCount, (float)placed/tn); float score=(valid?1f:0f) + (1f-Mathf.Abs(((float)placed/tn)-target));
            if(score>bestScore){bestScore=score; bestAttempt=attempt; bestTopo=(TopologyCell[])topo.Clone(); bestGroups=compCount; bestLargest=largest;} if(valid) break;
        }
        diag.attempts=bestAttempt; diag.groupCount=bestGroups; diag.largestGroupShare=bestLargest;
        bool[] topoLand = new bool[tn]; for (int i = 0; i < tn; i++) topoLand[i] = bestTopo[i].isLand;
        float[] topoLandDist = DistanceFromBoundaryTopology(topoLand, tw, th, true);
        float[] topoOceanDist = DistanceFromBoundaryTopology(topoLand, tw, th, false);
        int n=mapWidth*mapHeight; land=new float[n]; elev=new float[n]; mtn=new float[n]; shelf=new float[n]; cont=new float[n];
        var nTopoLand = new NativeArray<float>(tn, Allocator.TempJob);
        var nTopoLandDist = new NativeArray<float>(tn, Allocator.TempJob);
        var nTopoOceanDist = new NativeArray<float>(tn, Allocator.TempJob);
        var nLand = new NativeArray<float>(n, Allocator.TempJob);
        var nInland = new NativeArray<float>(n, Allocator.TempJob);
        var nOffshore = new NativeArray<float>(n, Allocator.TempJob);
        var nElev = new NativeArray<float>(n, Allocator.TempJob);
        var nMtn = new NativeArray<float>(n, Allocator.TempJob);
        var nShelf = new NativeArray<float>(n, Allocator.TempJob);
        var nCont = new NativeArray<float>(n, Allocator.TempJob);
        var nUpland = new NativeArray<float>(n, Allocator.TempJob);
        var nRawMtn = new NativeArray<float>(n, Allocator.TempJob);
        try{
            for(int i=0;i<tn;i++){nTopoLand[i]=topoLand[i]?1f:0f; nTopoLandDist[i]=topoLandDist[i]; nTopoOceanDist[i]=topoOceanDist[i];}
            var landJob=new LandUpsampleAndDistanceJob{mapWidth=mapWidth,mapHeight=mapHeight,topoWidth=tw,topoHeight=th,seed=inputs.seed,coastlineWarpStrength=coastlineWarpStrength,coastlineEdgeNoiseStrength=coastlineEdgeNoiseStrength,coastlineSoftness=coastlineSoftness,topoLand=nTopoLand,topoLandCoastDistance=nTopoLandDist,topoOceanCoastDistance=nTopoOceanDist,land=nLand,inlandDistance=nInland,offshoreDistance=nOffshore};
            var jh=landJob.ScheduleParallel(n,64,default);
            var terrJob=new TerrainPotentialJob{mapWidth=mapWidth,mapHeight=mapHeight,seed=inputs.seed,roughness=math.saturate(inputs.elevation),land=nLand,inlandDistance=nInland,rawMountainPotential=nRawMtn,uplandPotential=nUpland};
            jh=terrJob.ScheduleParallel(n,64,jh); jh.Complete();
            float rough=math.saturate(inputs.elevation); float targetMountainCoverage=math.lerp(0f,0.24f,math.pow(rough,1.15f));
            var vals=new List<float>(); for(int i=0;i<n;i++) if(nLand[i]>0.5f) vals.Add(nRawMtn[i]); vals.Sort();
            float threshold=vals.Count>0 && targetMountainCoverage>0f ? vals[Mathf.Clamp(Mathf.FloorToInt((1f-targetMountainCoverage)*(vals.Count-1)),0,vals.Count-1)] : 1f;
            var mfJob=new MountainFinalizeJob{mapHeight=mapHeight,roughness=rough,mountainThreshold=threshold,land=nLand,inlandDistance=nInland,offshoreDistance=nOffshore,uplandPotential=nUpland,rawMountainPotential=nRawMtn,mountain=nMtn,elevation=nElev,shelf=nShelf,continentality=nCont};
            mfJob.ScheduleParallel(n,64,default).Complete();
            float elevSum=0,elevMax=0,mtnPixels=0,landPixels=0,lc=0; for(int i=0;i<n;i++){land[i]=nLand[i];elev[i]=nElev[i];mtn[i]=nMtn[i];shelf[i]=nShelf[i];cont[i]=nCont[i]; if(land[i]>0.5f){lc++; landPixels++; elevSum+=elev[i]; elevMax=Mathf.Max(elevMax,elev[i]); if(mtn[i]>0.35f)mtnPixels++;}}
            diag.actualLand=lc/n; diag.avgElevation=landPixels>0?elevSum/landPixels:0; diag.maxElevation=elevMax; diag.mountainCoverage=landPixels>0?mtnPixels/landPixels:0;
        } finally {
            nRawMtn.Dispose();
            nUpland.Dispose();
            nCont.Dispose();
            nShelf.Dispose();
            nMtn.Dispose();
            nElev.Dispose();
            nOffshore.Dispose();
            nInland.Dispose();
            nLand.Dispose();
            nTopoOceanDist.Dispose();
            nTopoLandDist.Dispose();
            nTopoLand.Dispose();
        }
    }
    private void SmoothBroadPresetTopology(TopologyCell[] topo,int tw,int th,string presetName){
        if(presetName!="Standard" && presetName!="Large Continents" && presetName!="Pangaea" && presetName!="Terrestrial") return;
        bool[] next=new bool[topo.Length];
        for(int i=0;i<topo.Length;i++) next[i]=topo[i].isLand;
        for(int y=0;y<th;y++) for(int x=0;x<tw;x++){
            int i=y*tw+x; int landNeighbors=0;
            for(int oy=-1;oy<=1;oy++) for(int ox=-1;ox<=1;ox++){
                if(ox==0&&oy==0) continue; int nx=(x+ox+tw)%tw; int ny=y+oy; if(ny<0||ny>=th) continue; if(topo[ny*tw+nx].isLand) landNeighbors++;
            }
            if(!topo[i].isLand && landNeighbors>=5) next[i]=true;
            else if(topo[i].isLand && landNeighbors<=1) next[i]=false;
        }
        for(int i=0;i<topo.Length;i++) topo[i].isLand=next[i];
    }

    private float LargestLandmassShare(TopologyCell[] topo,int w,int h,out int compCount,out int[] componentSizes){
        int n=w*h; bool[] vis=new bool[n]; var sizes=new List<int>(); int total=0;
        for(int i=0;i<n;i++) if(topo[i].isLand) total++;
        for(int i=0;i<n;i++) if(topo[i].isLand&&!vis[i]){int size=0; var q=new Queue<int>(); q.Enqueue(i); vis[i]=true; while(q.Count>0){int c=q.Dequeue(); size++; int x=c%w,y=c/w; for(int d=0;d<4;d++){int nx=(x+(d==0?1:d==1?-1:0)+w)%w, ny=y+(d==2?1:d==3?-1:0); if(ny<0||ny>=h) continue; int ni=ny*w+nx; if(!vis[ni]&&topo[ni].isLand){vis[ni]=true;q.Enqueue(ni);}}} sizes.Add(size);} 
        compCount=sizes.Count; componentSizes=sizes.ToArray(); int largest=0; foreach(int s in sizes) if(s>largest) largest=s; return total<=0?0f:(float)largest/total;
    }
    private bool IsTopologyValidForPreset(PreviewLandPresetProfileV2 preset,float largestShare,int componentCount,float actualCoverage){
        switch(preset.name){
            case "Archipelago": return largestShare<=0.28f && componentCount>=8;
            case "Islands": return largestShare<=0.48f && componentCount>=4;
            case "Standard": return largestShare<=0.62f && componentCount>=3;
            case "Large Continents": return largestShare<=0.78f && componentCount>=2;
            case "Pangaea": return largestShare>=0.94f;
            case "Terrestrial": return largestShare<=0.92f || actualCoverage<=0.80f;
            default: return !preset.requireSingleDominantLandmass || largestShare>=preset.minLargestLandmassShare;
        }
    }

    private float[] DistanceFromBoundary(float[] land,float threshold,bool forLand){
        int n=land.Length; float[] dist=new float[n]; for(int i=0;i<n;i++) dist[i]=99999f; var q=new Queue<int>();
        for(int y=0;y<mapHeight;y++) for(int x=0;x<mapWidth;x++){int i=y*mapWidth+x; bool isLand=land[i]>threshold; if(isLand!=forLand) continue; bool near=false; for(int d=0;d<4;d++){int nx=(x+(d==0?1:d==1?-1:0)+mapWidth)%mapWidth, ny=y+(d==2?1:d==3?-1:0); if(ny<0||ny>=mapHeight){near=true;break;} bool nLand=land[ny*mapWidth+nx]>threshold; if(nLand!=isLand){near=true;break;}} if(near){dist[i]=0f; q.Enqueue(i);}}
        while(q.Count>0){int c=q.Dequeue(); int x=c%mapWidth,y=c/mapWidth; float baseD=dist[c]; for(int d=0;d<4;d++){int nx=(x+(d==0?1:d==1?-1:0)+mapWidth)%mapWidth, ny=y+(d==2?1:d==3?-1:0); if(ny<0||ny>=mapHeight) continue; int ni=ny*mapWidth+nx; bool nLand=land[ni]>threshold; if(nLand!=forLand) continue; if(dist[ni]>baseD+1f){dist[ni]=baseD+1f;q.Enqueue(ni);}}}
        return dist;
    }
    private float[] DistanceFromBoundaryTopology(bool[] landTopo, int w, int h, bool forLand)
    {
        int n = w * h;
        float[] dist = new float[n];
        for (int i = 0; i < n; i++) dist[i] = 99999f;
        var q = new Queue<int>();
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            int i = y * w + x; bool isLand = landTopo[i];
            if (isLand != forLand) continue;
            bool near = false;
            for (int d = 0; d < 4; d++)
            {
                int nx = (x + (d == 0 ? 1 : d == 1 ? -1 : 0) + w) % w, ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                if (ny < 0 || ny >= h) { near = true; break; }
                if (landTopo[ny * w + nx] != isLand) { near = true; break; }
            }
            if (near) { dist[i] = 0f; q.Enqueue(i); }
        }
        while (q.Count > 0)
        {
            int c = q.Dequeue(); int x = c % w, y = c / w; float baseD = dist[c];
            for (int d = 0; d < 4; d++)
            {
                int nx = (x + (d == 0 ? 1 : d == 1 ? -1 : 0) + w) % w, ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                if (ny < 0 || ny >= h) continue;
                int ni = ny * w + nx; if (landTopo[ni] != forLand) continue;
                if (dist[ni] > baseD + 1f) { dist[ni] = baseD + 1f; q.Enqueue(ni); }
            }
        }
        return dist;
    }

    private void WriteSurfaceAndStructure(float[] land,float[] elev,float[] mtn,float[] shelf,float[] cont){int n=mapWidth*mapHeight;var s=new Color32[n];var a=new Color32[n];var w=new Color32[n];for(int i=0;i<n;i++){s[i]=new Color32(B(land[i]),B(elev[i]),B(mtn[i]),B(shelf[i]));w[i]=new Color32(B(cont[i]),B(mtn[i]),B(elev[i]),0);a[i]=new Color32(B(shelf[i]),B(cont[i]),0,0);}surfaceDataTexture.SetPixelData(s,0);surfaceDataTexture.Apply(false,false);worldStructureTexture.SetPixelData(w,0);worldStructureTexture.Apply(false,false);auxiliaryMaskTexture.SetPixelData(a,0);auxiliaryMaskTexture.Apply(false,false);}    
    private void ReadSurface(out float[] land,out float[] elev,out float[] mtn,out float[] shelf){int n=mapWidth*mapHeight;land=new float[n];elev=new float[n];mtn=new float[n];shelf=new float[n];var p=surfaceDataTexture.GetPixels32();for(int i=0;i<n;i++){land[i]=p[i].r/255f;elev[i]=p[i].g/255f;mtn[i]=p[i].b/255f;shelf[i]=p[i].a/255f;}}
    private void ReadClimate(out float[] temp,out float[] moisture,out float[] cont){int n=mapWidth*mapHeight;temp=new float[n];moisture=new float[n];cont=new float[n];var p=climateTexture.GetPixels32();for(int i=0;i<n;i++){temp[i]=p[i].r/255f;moisture[i]=p[i].g/255f;cont[i]=p[i].b/255f;}}
    private void ReadHydrology(out float[] river, out float[] lake, out float[] wetness, out float[] flowOrDepth){int n=mapWidth*mapHeight;river=new float[n];lake=new float[n];wetness=new float[n];flowOrDepth=new float[n];var p=hydrologyMaskTexture.GetPixels32();for(int i=0;i<n;i++){river[i]=p[i].r/255f;lake[i]=p[i].g/255f;wetness[i]=p[i].b/255f;flowOrDepth[i]=p[i].a/255f;}}
    private void GenerateClimate(float[] land,float[] elev,float[] mtn,float[] contIn,out float[] temp,out float[] moisture){int n=mapWidth*mapHeight;temp=new float[n];moisture=new float[n];if(contIn==null){contIn=new float[n];for(int i=0;i<n;i++)contIn[i]=land[i]*Mathf.Clamp01(elev[i]);}var coast=DistanceFromBoundary(land,0.5f,true);var p=new Color32[n];for(int i=0;i<n;i++){int x=i%mapWidth;int y=i/mapWidth;float lat=Mathf.Abs(((float)y/(mapHeight-1))*2f-1f);float equatorWarmth=1f-lat;float continentality=contIn[i];float tempNoise=(Mathf.PerlinNoise((x+inputs.seed*0.31f)*0.006f,(y-inputs.seed*0.17f)*0.006f)-0.5f)*2f;float localTemperature=equatorWarmth+(inputs.temperature-0.5f)*0.65f-elev[i]*0.42f+continentality*inputs.continentalTemperatureStrength+tempNoise*inputs.climateNoiseStrength*0.35f;float coastProximity=Mathf.Clamp01(1f-coast[i]/(0.08f*mapHeight));float moistureNoise=(Mathf.PerlinNoise((x-inputs.seed*0.23f)*0.007f,(y+inputs.seed*0.29f)*0.007f)-0.5f)*2f;float localMoisture=inputs.moisture+coastProximity*inputs.coastWetnessStrength-continentality*inputs.continentalDrynessStrength+moistureNoise*inputs.climateNoiseStrength+0.06f*(1f-lat);localTemperature=Mathf.Clamp01(localTemperature);localMoisture=Mathf.Clamp01(localMoisture);temp[i]=localTemperature;moisture[i]=localMoisture;p[i]=new Color32(B(localTemperature),B(localMoisture),B(continentality),0);}climateTexture.SetPixelData(p,0);climateTexture.Apply(false,false);}
    private void GenerateHydrology(float[] land,float[] elev,float[] moisture,ref GenDiagnostics diag){
        int n=mapWidth*mapHeight;float[] river=new float[n],lake=new float[n],wet=new float[n],flow=new float[n];
        float[] coastDist=DistanceFromBoundary(land,0.5f,true);
        int hydroSeed = Mathf.RoundToInt(inputs.seed * 1000f);
        var r=new System.Random(hydroSeed*31+17);
        Vector2Int selectedRiverRange = inputs.waterwaysPreset <= 0 ? sparseRiverRange : inputs.waterwaysPreset == 1 ? standardRiverRange : abundantRiverRange;
        int targetRivers = r.Next(selectedRiverRange.x, selectedRiverRange.y + 1);
        var candidates=new List<RiverSourceCandidate>();
        int stride = Mathf.Max(1, riverCandidateStride);
        for(int y=0;y<mapHeight;y+=stride) for(int x=0;x<mapWidth;x+=stride){
            int jitter = ((x * 73856093) ^ (y * 19349663) ^ hydroSeed) & (stride - 1);
            int sx = (x + jitter) % mapWidth;
            int i = y*mapWidth+sx;
            if(land[i]<=0.5f) continue;
            if(coastDist[i] < 2f) continue;
            float inlandScore=Mathf.Clamp01(coastDist[i]/(0.18f*mapHeight));
            float sourceScore=inlandScore*0.45f + elev[i]*0.30f + moisture[i]*0.25f;
            candidates.Add(new RiverSourceCandidate{index=i,score=sourceScore});
        }
        candidates.Sort((a,b)=>b.score.CompareTo(a.score));
        var acceptedSources=new List<int>();
        foreach(var candidate in candidates){
            int cur=candidate.index; int cx=cur%mapWidth, cy=cur/mapWidth; bool tooClose=false;
            foreach(var src in acceptedSources){int sx=src%mapWidth, sy=src/mapWidth; int dx=Mathf.Abs(cx-sx); dx=Mathf.Min(dx,mapWidth-dx); int dy=Mathf.Abs(cy-sy); if(dx*dx+dy*dy<minRiverSourceSpacingPixels*minRiverSourceSpacingPixels){tooClose=true;break;}}
            if(tooClose) continue; acceptedSources.Add(cur); if(acceptedSources.Count>=targetRivers) break;
        }
        int completedRivers=0, mergedRivers=0, oceanReachedRivers=0, failedRoutes=0;
        foreach(int source in acceptedSources){
            int cur=source; int len=0; bool completed=false; var seen=new HashSet<int>();
            float pathFlow=0.28f + 0.52f*moisture[source];
            while(len<260 && !seen.Contains(cur) && land[cur]>0.5f){
                seen.Add(cur); river[cur]=Mathf.Max(river[cur],Mathf.Clamp01(0.22f + pathFlow*0.6f)); flow[cur]=Mathf.Max(flow[cur],Mathf.Clamp01(pathFlow)); wet[cur]=Mathf.Max(wet[cur],0.35f+river[cur]*0.5f); len++;
                int x=cur%mapWidth,y=cur/mapWidth; int next=-1; float bestCost=float.MaxValue; float curCoast = coastDist[cur];
                for(int oy=-1;oy<=1;oy++)for(int ox=-1;ox<=1;ox++){
                    if(ox==0&&oy==0)continue; int nx=(x+ox+mapWidth)%mapWidth, ny=y+oy; if(ny<0||ny>=mapHeight) continue; int ni=ny*mapWidth+nx;
                    if(seen.Contains(ni)) continue;
                    float coastNorm=Mathf.Clamp01(coastDist[ni]/(0.30f*mapHeight));
                    float meander=(Mathf.PerlinNoise((nx+inputs.seed*0.19f)*0.09f,(ny-inputs.seed*0.23f)*0.09f)-0.5f)*2f;
                    if (land[ni] <= 0.5f) { completed = true; oceanReachedRivers++; next = ni; break; }
                    float cost=coastNorm*0.75f + elev[ni]*0.15f + (meander*0.5f+0.5f)*0.10f;
                    if (coastDist[ni] >= curCoast) cost += 0.05f;
                    if(cost<bestCost){bestCost=cost; next=ni;}
                }
                if(next<0) break;
                if(river[next]>0.1f){completed=true; mergedRivers++; cur=next; len++; break;}
                pathFlow=Mathf.Clamp01(pathFlow+0.004f); cur=next;
            }
            if(completed && len>=10) completedRivers++; else failedRoutes++;
        }
        int lakes=0;
        Vector2Int lakeRange = inputs.waterwaysPreset <= 0 ? new Vector2Int(2,4) : inputs.waterwaysPreset == 1 ? new Vector2Int(5,9) : new Vector2Int(9,15);
        int lakeTarget=r.Next(lakeRange.x, lakeRange.y + 1) + Mathf.Clamp(Mathf.RoundToInt(inputs.moisture*2f),0,2);
        for(int attempt=0;attempt<n && lakes<lakeTarget;attempt++){
            int idx=r.Next(n); if(land[idx]<=0.5f) continue; if(coastDist[idx]<5f) continue; if(elev[idx]>0.62f) continue;
            int rad=r.Next(17,36);
            FillLocalLakePatch(idx,rad,land,elev,lake,wet); lakes++;
        }
        for(int i=0;i<n;i++) if(river[i]>0f || lake[i]>0f){int x=i%mapWidth,y=i/mapWidth; for(int oy=-1;oy<=1;oy++)for(int ox=-1;ox<=1;ox++){int nx=(x+ox+mapWidth)%mapWidth,ny=y+oy; if(ny<0||ny>=mapHeight)continue; int ni=ny*mapWidth+nx; if(land[ni]>0.5f) wet[ni]=Mathf.Max(wet[ni],0.45f + lake[i]*0.2f + river[i]*0.15f);}}
        var p=new Color32[n]; for(int i=0;i<n;i++) p[i]=new Color32(B(river[i]),B(lake[i]),B(wet[i]),B(Mathf.Max(flow[i],lake[i])));
        hydrologyMaskTexture.SetPixelData(p,0); hydrologyMaskTexture.Apply(false,false);
        diag.rivers=completedRivers; diag.lakes=lakes;
        if(logWorldGenerationDiagnostics) Debug.Log($"[WorldGenV2 Hydrology] TargetRivers={targetRivers} Candidates={candidates.Count} AcceptedSources={acceptedSources.Count} CompletedRivers={completedRivers} MergedRivers={mergedRivers} OceanReachedRivers={oceanReachedRivers} FailedRoutes={failedRoutes} Lakes={lakes}");
    }

    private struct RiverSourceCandidate { public int index; public float score; }
private void FillLocalLakePatch(int centerIndex,int radius,float[] land,float[] elev,float[] lake,float[] wet){int cx=centerIndex%mapWidth,cy=centerIndex/mapWidth;float baseElev=elev[centerIndex];for(int oy=-radius;oy<=radius;oy++)for(int ox=-radius;ox<=radius;ox++){int nx=(cx+ox+mapWidth)%mapWidth,ny=cy+oy; if(ny<0||ny>=mapHeight)continue;float d=Mathf.Sqrt(ox*ox+oy*oy);if(d>radius)continue;int ni=ny*mapWidth+nx; if(land[ni]<=0.5f)continue; if(elev[ni]>baseElev+0.22f+0.04f*d)continue; float fill=Mathf.Clamp01(1f-d/(radius+0.5f)); lake[ni]=Mathf.Max(lake[ni],fill); wet[ni]=Mathf.Max(wet[ni],0.6f+fill*0.35f);}}
    [BurstCompile] private struct LandUpsampleAndDistanceJob : IJobFor
    {
        [ReadOnly] public NativeArray<float> topoLand, topoLandCoastDistance, topoOceanCoastDistance;
        public int mapWidth, mapHeight, topoWidth, topoHeight; public float seed, coastlineWarpStrength, coastlineEdgeNoiseStrength, coastlineSoftness;
        public NativeArray<float> land, inlandDistance, offshoreDistance;
        public void Execute(int i){int x=i%mapWidth,y=i/mapWidth; float2 uv=new float2(((float)x/mapWidth)*topoWidth,((float)y/mapHeight)*topoHeight); SampleBilinear(uv, topoLand, out var l); SampleBilinear(uv, topoLandCoastDistance, out var inD); SampleBilinear(uv, topoOceanCoastDistance, out var offD); float warp=(Hash(new float2(x,y)+seed)-0.5f)*coastlineWarpStrength; float edge=(Hash(new float2(x*3.1f,y*2.7f)-seed)-0.5f)*coastlineEdgeNoiseStrength; land[i]=math.smoothstep(0.5f-coastlineSoftness,0.5f+coastlineSoftness,l+warp+edge); inlandDistance[i]=inD; offshoreDistance[i]=offD;}
        void SampleBilinear(float2 uv, NativeArray<float> arr, out float value){int x0=((int)math.floor(uv.x)%topoWidth+topoWidth)%topoWidth,y0=math.clamp((int)math.floor(uv.y),0,topoHeight-1); int x1=(x0+1)%topoWidth,y1=math.min(y0+1,topoHeight-1); float fx=uv.x-math.floor(uv.x),fy=uv.y-math.floor(uv.y); float v00=arr[y0*topoWidth+x0],v10=arr[y0*topoWidth+x1],v01=arr[y1*topoWidth+x0],v11=arr[y1*topoWidth+x1]; value=math.lerp(math.lerp(v00,v10,fx),math.lerp(v01,v11,fx),fy);}
        float Hash(float2 p)=>math.frac(math.sin(math.dot(p,new float2(12.9898f,78.233f)))*43758.5453f);
    }
    [BurstCompile] private struct TerrainPotentialJob : IJobFor
    {
        [ReadOnly] public NativeArray<float> land,inlandDistance; public int mapWidth,mapHeight; public float seed,roughness;
        public NativeArray<float> rawMountainPotential,uplandPotential;
        public void Execute(int i){int x=i%mapWidth,y=i/mapWidth; float inland=math.saturate(inlandDistance[i]/(0.24f*mapHeight)); float province=Hash(new float2(x,y)*0.0023f + seed*0.013f); float ridge=1f-math.abs(Hash(new float2(x,y)*0.007f-seed*0.021f)*2f-1f); float upl=math.smoothstep(0.35f,0.8f,province)*inland; uplandPotential[i]=upl; rawMountainPotential[i]=land[i]*inland*(province*0.55f+ridge*0.45f)*math.lerp(0.8f,1.25f,roughness);}
        float Hash(float2 p)=>math.frac(math.sin(math.dot(p,new float2(127.1f,311.7f)))*43758.5453f);
    }
    [BurstCompile] private struct MountainFinalizeJob : IJobFor
    {
        [ReadOnly] public NativeArray<float> land,inlandDistance,offshoreDistance,uplandPotential,rawMountainPotential; public int mapHeight; public float roughness,mountainThreshold;
        public NativeArray<float> mountain,elevation,shelf,continentality;
        public void Execute(int i){float l=land[i]; float inland=math.saturate(inlandDistance[i]/(0.24f*mapHeight)); float m=math.smoothstep(mountainThreshold-0.035f,mountainThreshold+0.035f,rawMountainPotential[i])*math.lerp(0.2f,1f,roughness); mountain[i]=m; float e=l*math.saturate(0.05f + inland*0.15f + uplandPotential[i]*math.lerp(0.05f,0.28f,roughness) + m*math.lerp(0.18f,0.72f,roughness)); elevation[i]=e; shelf[i]=(1f-l)*math.saturate(1f-offshoreDistance[i]/(0.04f*mapHeight)); continentality[i]=l*inland;}
    }
    private void GenerateBiomes(float[] land,float[] elev,float[] mtn,float[] temp,float[] moisture,float[] river,float[] lake,float[] hydroWetness){int n=mapWidth*mapHeight;var o0=new Color32[n];var o1=new Color32[n];var o2=new Color32[n];float sharpness=Mathf.Lerp(0.75f,2.0f,Mathf.InverseLerp(0.5f,1.25f,inputs.biomeCompetitionSharpness));for(int i=0;i<n;i++){if(land[i]<0.5f){o0[i]=o1[i]=o2[i]=new Color32();continue;}float hot=temp[i],wet=moisture[i];float provinceNoise=(Mathf.PerlinNoise(((i%mapWidth)+inputs.seed*0.41f)*0.004f,((i/mapWidth)-inputs.seed*0.37f)*0.004f)-0.5f)*2f*inputs.biomeProvinceStrength*0.18f;float jungle=Mathf.Clamp01((hot-0.6f)*2f*(wet-0.6f+provinceNoise)*2f);float desert=Mathf.Clamp01((hot-0.5f)*2f*(0.7f-wet-provinceNoise*0.5f)*2f);float sav=Mathf.Clamp01((hot-0.5f)*2f*(1f-Mathf.Abs(wet-0.5f+provinceNoise*0.3f)*2f));float grass=Mathf.Clamp01((1f-Mathf.Abs(hot-0.5f)*2f)*(1f-Mathf.Abs(wet-0.45f-provinceNoise*0.25f)*2f));float forest=Mathf.Clamp01((1f-Mathf.Abs(hot-0.5f)*2f)*(wet-0.45f+provinceNoise)*2f);float taiga=Mathf.Clamp01((0.55f-hot)*2f*(wet-0.35f+provinceNoise*0.4f)*2f);float tundra=Mathf.Clamp01((0.45f-hot)*2.5f);float polar=Mathf.Clamp01((0.3f-hot)*3f+elev[i]*0.2f+mtn[i]*0.2f);float lowlandFactor=Mathf.Clamp01((0.55f-elev[i])*2f);float marsh=Mathf.Clamp01(lowlandFactor*(hydroWetness[i]*Mathf.Lerp(0.5f,1.25f,inputs.riparianWetnessStrength)+lake[i]*0.65f+river[i]*0.30f)+wet*0.08f);jungle=Mathf.Pow(jungle,sharpness);desert=Mathf.Pow(desert,sharpness);sav=Mathf.Pow(sav,sharpness);grass=Mathf.Pow(grass,sharpness);forest=Mathf.Pow(forest,sharpness);taiga=Mathf.Pow(taiga,sharpness);tundra=Mathf.Pow(tundra,sharpness);polar=Mathf.Pow(polar,sharpness);marsh=Mathf.Pow(marsh,sharpness);float sum=jungle+desert+sav+grass+forest+taiga+tundra+polar+marsh+1e-5f;o0[i]=new Color32(B(jungle/sum),B(desert/sum),B(sav/sum),B(grass/sum));o1[i]=new Color32(B(forest/sum),B(taiga/sum),B(tundra/sum),B(polar/sum));o2[i]=new Color32(B(marsh/sum),B(polar),B(mtn[i]),0);}biomeWeights0Texture.SetPixelData(o0,0);biomeWeights0Texture.Apply(false,false);biomeWeights1Texture.SetPixelData(o1,0);biomeWeights1Texture.Apply(false,false);biomeWeights2Texture.SetPixelData(o2,0);biomeWeights2Texture.Apply(false,false);}
}
