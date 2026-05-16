using System;
using System.Collections.Generic;
using UnityEngine;

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

    [Header("V2 Debug")]
    [SerializeField] private bool showLandMaskOnly;
    [SerializeField] private bool showElevationOnly;
    [SerializeField] private bool showMountainMaskOnly;
    [SerializeField] private bool showClimateTemperatureOnly;
    [SerializeField] private bool showClimateMoistureOnly;
    [SerializeField] private bool showContinentalityOnly;
    [SerializeField] private bool showRiverMaskOnly;
    [SerializeField] private bool showLakeMaskOnly;
    [SerializeField] private bool showWetlandMaskOnly;
    [SerializeField] private bool showDominantBiomeOnly;

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
        new PreviewLandPresetProfileV2{name="Standard",minLandCoverage=0.34f,maxLandCoverage=0.52f,minPrimaryLandGroups=3,maxPrimaryLandGroups=5,minSeedSeparationDegrees=21f,compactnessBias=0.62f,irregularityBias=0.58f,elongationBias=0.42f,islandFragmentBias=0.25f,requireSingleDominantLandmass=false,minLargestLandmassShare=0f,targetSatelliteIslandClustersMin=2,targetSatelliteIslandClustersMax=5},
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

    private struct GenDiagnostics { public string preset; public float targetLand; public float actualLand; public int groupCount; public float largestGroupShare; public int attempts; public int rivers; public int lakes; }

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
            GenerateBiomes(land, elev, mtn, temp, moisture);
        }
        if (logWorldGenerationDiagnostics && (s & PreviewWorldRebuildScope.Tectonics) != 0)
        {
            Debug.Log($"[WorldGenV2] preset={diag.preset} targetLand={diag.targetLand:F3} actualLand={diag.actualLand:F3} groups={diag.groupCount} largestShare={diag.largestGroupShare:F3} attempts={diag.attempts} rivers={diag.rivers} lakes={diag.lakes}");
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
        for(int attempt=1; attempt<=Mathf.Max(1,maxTopologyAttempts); attempt++){
            var r=new System.Random(inputs.seed*73856093 ^ attempt*19349663);
            for(int i=0;i<tn;i++){topo[i].isLand=false;topo[i].groupId=-1;}
            int groups=r.Next(preset.minPrimaryLandGroups,preset.maxPrimaryLandGroups+1);
            var seeds=new List<Vector2Int>();
            for(int g=0;g<groups;g++){
                for(int k=0;k<60;k++){
                    int sx=r.Next(tw), sy=r.Next(th); var candidate=new Vector2Int(sx,sy); bool ok=true;
                    foreach(var s in seeds){float dx=Mathf.Min(Mathf.Abs(candidate.x-s.x), tw-Mathf.Abs(candidate.x-s.x)); float dy=Mathf.Abs(candidate.y-s.y); if(Mathf.Sqrt(dx*dx+dy*dy)<preset.minSeedSeparationDegrees*tw/360f){ok=false;break;}}
                    if(ok||k==59){seeds.Add(candidate); int idx=sy*tw+sx; topo[idx].isLand=true; topo[idx].groupId=g; break;}
                }
            }
            int targetCells=Mathf.Clamp(Mathf.RoundToInt(target*tn),groups,tn-2);
            var counts=new int[groups]; for(int g=0;g<groups;g++)counts[g]=1;
            var centers=new Vector2[groups]; for(int g=0;g<groups;g++) centers[g]=seeds[g];
            var frontier=new List<int>[groups]; for(int g=0;g<groups;g++) frontier[g]=new List<int>{seeds[g].y*tw+seeds[g].x};
            int placed=groups; int guard=tn*40;
            while(placed<targetCells && guard-->0){
                for(int g=0;g<groups&&placed<targetCells;g++){
                    if(frontier[g].Count==0) continue;
                    int best=-1; float bestLocal=-999f;
                    foreach(int fidx in frontier[g]){
                        int fx=fidx%tw, fy=fidx/tw;
                        for(int d=0;d<4;d++){
                            int nx=(fx + (d==0?1:d==1?-1:0) + tw)%tw, ny=fy + (d==2?1:d==3?-1:0);
                            if(ny<0||ny>=th) continue; int ni=ny*tw+nx; if(topo[ni].isLand) continue;
                            float compact=-Vector2.Distance(new Vector2(nx,ny), centers[g])/Mathf.Max(tw,th);
                            float irr=(Mathf.PerlinNoise((nx+inputs.seed)*0.17f,(ny-inputs.seed)*0.17f)-0.5f)*2f;
                            var elong=(nx-seeds[g].x)*(nx-seeds[g].x) > (ny-seeds[g].y)*(ny-seeds[g].y) ? 1f : 0f;
                            int nearbyOther=0, own=0;
                            for(int oy=-1;oy<=1;oy++)for(int ox=-1;ox<=1;ox++){if(ox==0&&oy==0)continue;int xx=(nx+ox+tw)%tw,yy=ny+oy; if(yy<0||yy>=th)continue; var c=topo[yy*tw+xx]; if(c.isLand){if(c.groupId==g)own++; else nearbyOther++;}}
                            float tendrilPenalty=own<=1?0.4f:0f;
                            float score=preset.compactnessBias*compact + preset.irregularityBias*irr + preset.elongationBias*elong - nearbyOther*0.12f - tendrilPenalty;
                            if(score>bestLocal){bestLocal=score; best=ni;}
                        }
                    }
                    if(best>=0){topo[best].isLand=true; topo[best].groupId=g; placed++; counts[g]++; frontier[g].Add(best);} else frontier[g].Clear();
                }
            }
            int sat=r.Next(preset.targetSatelliteIslandClustersMin,preset.targetSatelliteIslandClustersMax+1);
            for(int s=0;s<sat;s++){int cx=r.Next(tw),cy=r.Next(th),rad=r.Next(1,3); for(int yy=-rad;yy<=rad;yy++)for(int xx=-rad;xx<=rad;xx++){int x=(cx+xx+tw)%tw,y=cy+yy; if(y<0||y>=th)continue; if(xx*xx+yy*yy<=rad*rad&&r.NextDouble()>0.25){int i=y*tw+x; if(!topo[i].isLand){topo[i].isLand=true; topo[i].groupId=r.Next(groups); placed++;}}}}
            int[] comp; int compCount; float largest=LargestLandmassShare(topo,tw,th,out compCount,out comp);
            bool valid=true;
            if(preset.requireSingleDominantLandmass && largest<preset.minLargestLandmassShare) valid=false;
            if((preset.name=="Archipelago"||preset.name=="Islands"||preset.name=="Standard") && largest>0.70f) valid=false;
            float score=(valid?1f:0f) + (1f-Mathf.Abs(((float)placed/tn)-target)) - Mathf.Abs(largest-(preset.requireSingleDominantLandmass?preset.minLargestLandmassShare:0.35f));
            if(score>bestScore){bestScore=score; bestAttempt=attempt; bestTopo=(TopologyCell[])topo.Clone(); bestGroups=compCount; bestLargest=largest;}
            if(valid){bestAttempt=attempt; bestTopo=(TopologyCell[])topo.Clone(); bestGroups=compCount; bestLargest=largest; break;}
        }
        diag.attempts=bestAttempt; diag.groupCount=bestGroups; diag.largestGroupShare=bestLargest;
        land=new float[mapWidth*mapHeight]; elev=new float[land.Length]; mtn=new float[land.Length]; shelf=new float[land.Length]; cont=new float[land.Length];
        float sx=(float)tw/mapWidth, sy=(float)th/mapHeight;
        for(int y=0;y<mapHeight;y++) for(int x=0;x<mapWidth;x++){
            int i=y*mapWidth+x; float tx=x*sx, ty=y*sy; int x0=Mathf.FloorToInt(tx)%tw, y0=Mathf.Clamp(Mathf.FloorToInt(ty),0,th-1);
            int x1=(x0+1)%tw, y1=Mathf.Min(y0+1,th-1); float fx=tx-Mathf.Floor(tx), fy=ty-Mathf.Floor(ty);
            float v00=bestTopo[y0*tw+x0].isLand?1f:0f,v10=bestTopo[y0*tw+x1].isLand?1f:0f,v01=bestTopo[y1*tw+x0].isLand?1f:0f,v11=bestTopo[y1*tw+x1].isLand?1f:0f;
            float baseLand=Mathf.Lerp(Mathf.Lerp(v00,v10,fx),Mathf.Lerp(v01,v11,fx),fy);
            float warp=(Mathf.PerlinNoise((x+inputs.seed)*0.006f,(y-inputs.seed)*0.009f)-0.5f)*coastlineWarpStrength;
            float edge=(Mathf.PerlinNoise((x-inputs.seed)*0.035f,(y+inputs.seed)*0.033f)-0.5f)*coastlineEdgeNoiseStrength;
            float smooth=Mathf.SmoothStep(0.5f-coastlineSoftness,0.5f+coastlineSoftness,baseLand+warp+edge);
            land[i]=smooth;
        }
        float[] coastDist=DistanceFromBoundary(land,0.5f,true); float[] oceanDist=DistanceFromBoundary(land,0.5f,false);
        for(int i=0;i<land.Length;i++){
            float inland=Mathf.Clamp01(coastDist[i]/(0.24f*mapHeight));
            float broad=Mathf.PerlinNoise(((i%mapWidth)+inputs.seed)*0.0045f,((i/mapWidth)-inputs.seed)*0.0045f);
            float hill=Mathf.PerlinNoise(((i%mapWidth)-inputs.seed)*0.016f,((i/mapWidth)+inputs.seed)*0.016f);
            float ridge=1f-Mathf.Abs(Mathf.PerlinNoise((i%mapWidth)*0.024f+inputs.seed*0.01f,(i/mapWidth)*0.024f-inputs.seed*0.01f)*2f-1f);
            mtn[i]=land[i]*Mathf.Pow(Mathf.Clamp01(ridge*inputs.elevation),1.8f);
            elev[i]=land[i]*Mathf.Clamp01(0.08f+0.58f*inland+0.2f*broad+0.2f*hill+0.3f*mtn[i]);
            shelf[i]=(1f-land[i])*Mathf.Clamp01(1f-oceanDist[i]/(0.04f*mapHeight));
            cont[i]=land[i]*inland;
        }
        float lc=0f; for(int i=0;i<land.Length;i++) if(land[i]>0.5f) lc++; diag.actualLand=lc/land.Length;
    }

    private float LargestLandmassShare(TopologyCell[] topo,int w,int h,out int compCount,out int[] componentSizes){
        int n=w*h; bool[] vis=new bool[n]; var sizes=new List<int>(); int total=0;
        for(int i=0;i<n;i++) if(topo[i].isLand) total++;
        for(int i=0;i<n;i++) if(topo[i].isLand&&!vis[i]){int size=0; var q=new Queue<int>(); q.Enqueue(i); vis[i]=true; while(q.Count>0){int c=q.Dequeue(); size++; int x=c%w,y=c/w; for(int d=0;d<4;d++){int nx=(x+(d==0?1:d==1?-1:0)+w)%w, ny=y+(d==2?1:d==3?-1:0); if(ny<0||ny>=h) continue; int ni=ny*w+nx; if(!vis[ni]&&topo[ni].isLand){vis[ni]=true;q.Enqueue(ni);}}} sizes.Add(size);} 
        compCount=sizes.Count; componentSizes=sizes.ToArray(); int largest=0; foreach(int s in sizes) if(s>largest) largest=s; return total<=0?0f:(float)largest/total;
    }

    private float[] DistanceFromBoundary(float[] land,float threshold,bool forLand){
        int n=land.Length; float[] dist=new float[n]; for(int i=0;i<n;i++) dist[i]=99999f; var q=new Queue<int>();
        for(int y=0;y<mapHeight;y++) for(int x=0;x<mapWidth;x++){int i=y*mapWidth+x; bool isLand=land[i]>threshold; if(isLand!=forLand) continue; bool near=false; for(int d=0;d<4;d++){int nx=(x+(d==0?1:d==1?-1:0)+mapWidth)%mapWidth, ny=y+(d==2?1:d==3?-1:0); if(ny<0||ny>=mapHeight){near=true;break;} bool nLand=land[ny*mapWidth+nx]>threshold; if(nLand!=isLand){near=true;break;}} if(near){dist[i]=0f; q.Enqueue(i);}}
        while(q.Count>0){int c=q.Dequeue(); int x=c%mapWidth,y=c/mapWidth; float baseD=dist[c]; for(int d=0;d<4;d++){int nx=(x+(d==0?1:d==1?-1:0)+mapWidth)%mapWidth, ny=y+(d==2?1:d==3?-1:0); if(ny<0||ny>=mapHeight) continue; int ni=ny*mapWidth+nx; bool nLand=land[ni]>threshold; if(nLand!=forLand) continue; if(dist[ni]>baseD+1f){dist[ni]=baseD+1f;q.Enqueue(ni);}}}
        return dist;
    }

    private void WriteSurfaceAndStructure(float[] land,float[] elev,float[] mtn,float[] shelf,float[] cont){int n=mapWidth*mapHeight;var s=new Color32[n];var a=new Color32[n];var w=new Color32[n];for(int i=0;i<n;i++){s[i]=new Color32(B(land[i]),B(elev[i]),B(mtn[i]),B(shelf[i]));w[i]=new Color32(B(cont[i]),B(mtn[i]),B(elev[i]),0);a[i]=new Color32(B(shelf[i]),B(cont[i]),0,0);}surfaceDataTexture.SetPixelData(s,0);surfaceDataTexture.Apply(false,false);worldStructureTexture.SetPixelData(w,0);worldStructureTexture.Apply(false,false);auxiliaryMaskTexture.SetPixelData(a,0);auxiliaryMaskTexture.Apply(false,false);}    
    private void ReadSurface(out float[] land,out float[] elev,out float[] mtn,out float[] shelf){int n=mapWidth*mapHeight;land=new float[n];elev=new float[n];mtn=new float[n];shelf=new float[n];var p=surfaceDataTexture.GetPixels32();for(int i=0;i<n;i++){land[i]=p[i].r/255f;elev[i]=p[i].g/255f;mtn[i]=p[i].b/255f;shelf[i]=p[i].a/255f;}}
    private void ReadClimate(out float[] temp,out float[] moisture,out float[] cont){int n=mapWidth*mapHeight;temp=new float[n];moisture=new float[n];cont=new float[n];var p=climateTexture.GetPixels32();for(int i=0;i<n;i++){temp[i]=p[i].r/255f;moisture[i]=p[i].g/255f;cont[i]=p[i].b/255f;}}
    private void GenerateClimate(float[] land,float[] elev,float[] mtn,float[] contIn,out float[] temp,out float[] moisture){int n=mapWidth*mapHeight;temp=new float[n];moisture=new float[n];if(contIn==null){contIn=new float[n];for(int i=0;i<n;i++)contIn[i]=land[i]*Mathf.Clamp01(elev[i]);}var coast=DistanceFromBoundary(land,0.5f,true);var p=new Color32[n];for(int i=0;i<n;i++){int y=i/mapWidth;float lat=Mathf.Abs(((float)y/(mapHeight-1))*2f-1f);float tempLat=1f-lat;float elevCool=elev[i]*0.42f;float t=Mathf.Clamp01(tempLat+(inputs.temperature-0.5f)*0.65f-elevCool);float coastalWet=Mathf.Clamp01(1f-coast[i]/(0.08f*mapHeight));float continentalDry=Mathf.Clamp01(contIn[i]);float rainShadow=Mathf.Clamp01(mtn[i]*0.3f*continentalDry);float m=Mathf.Clamp01(inputs.moisture*0.7f+coastalWet*0.5f-continentalDry*0.35f-rainShadow+0.1f*(1f-lat));temp[i]=t;moisture[i]=m;p[i]=new Color32(B(t),B(m),B(contIn[i]),B(inputs.seasonalityStrength));}climateTexture.SetPixelData(p,0);climateTexture.Apply(false,false);}    
    private void GenerateHydrology(float[] land,float[] elev,float[] moisture,ref GenDiagnostics diag){int n=mapWidth*mapHeight;float[] river=new float[n],lake=new float[n],wet=new float[n];
        var sources=new List<int>(); for(int i=0;i<n;i++) if(land[i]>0.5f&&elev[i]>0.45f&&moisture[i]>0.45f) sources.Add(i);
        var r=new System.Random(inputs.seed*31+17); int targetRivers=Mathf.RoundToInt(Mathf.Lerp(sparseRiverRange.x,abundantRiverRange.y,inputs.moisture)); targetRivers=Mathf.Clamp(targetRivers, sparseRiverRange.x, abundantRiverRange.y);
        int rivers=0,lakes=0; int tries=Mathf.Min(sources.Count,targetRivers*5);
        for(int t=0;t<tries&&rivers<targetRivers;t++){
            if(sources.Count==0) break; int pick=r.Next(sources.Count); int cur=sources[pick]; sources[pick]=sources[sources.Count-1]; sources.RemoveAt(sources.Count-1);
            var seen=new HashSet<int>(); int len=0; bool reachedWater=false;
            while(len<220 && land[cur]>0.5f && !seen.Contains(cur)){
                seen.Add(cur); river[cur]=Mathf.Max(river[cur],1f); wet[cur]=Mathf.Max(wet[cur],0.6f); len++;
                int x=cur%mapWidth,y=cur/mapWidth; int next=cur; float best=elev[cur]-moisture[cur]*0.03f;
                for(int oy=-1;oy<=1;oy++)for(int ox=-1;ox<=1;ox++){if(ox==0&&oy==0)continue;int nx=(x+ox+mapWidth)%mapWidth,ny=y+oy; if(ny<0||ny>=mapHeight) continue; int ni=ny*mapWidth+nx; float e=elev[ni]-moisture[ni]*0.02f; if(e<best){best=e; next=ni;}}
                if(next==cur){lake[cur]=Mathf.Max(lake[cur],1f); wet[cur]=1f; lakes++; break;}
                if(land[next]<=0.5f||river[next]>0.6f){reachedWater=true; break;} cur=next;
            }
            if(len>18&&reachedWater) rivers++;
        }
        for(int i=0;i<n;i++){if(lake[i]>0.2f){int x=i%mapWidth,y=i/mapWidth; for(int oy=-1;oy<=1;oy++)for(int ox=-1;ox<=1;ox++){int nx=(x+ox+mapWidth)%mapWidth,ny=y+oy; if(ny<0||ny>=mapHeight)continue; int ni=ny*mapWidth+nx; if(land[ni]>0.5f) wet[ni]=Mathf.Max(wet[ni],0.75f);}} if(river[i]>0f){river[i]=Mathf.Clamp01(river[i]); wet[i]=Mathf.Max(wet[i],0.4f*moisture[i]);}}
        var p=new Color32[n]; for(int i=0;i<n;i++) p[i]=new Color32(B(river[i]),B(lake[i]),B(wet[i]),B(Mathf.Max(river[i],lake[i])));
        hydrologyMaskTexture.SetPixelData(p,0); hydrologyMaskTexture.Apply(false,false); diag.rivers=rivers; diag.lakes=lakes;}    
    private void GenerateBiomes(float[] land,float[] elev,float[] mtn,float[] temp,float[] moisture){int n=mapWidth*mapHeight;var o0=new Color32[n];var o1=new Color32[n];var o2=new Color32[n];for(int i=0;i<n;i++){if(land[i]<0.5f){o0[i]=o1[i]=o2[i]=new Color32();continue;}float hot=temp[i],wet=moisture[i];float jungle=Mathf.Clamp01((hot-0.6f)*2f*(wet-0.6f)*2f);float desert=Mathf.Clamp01((hot-0.5f)*2f*(0.7f-wet)*2f);float sav=Mathf.Clamp01((hot-0.5f)*2f*(1f-Mathf.Abs(wet-0.5f)*2f));float grass=Mathf.Clamp01((1f-Mathf.Abs(hot-0.5f)*2f)*(1f-Mathf.Abs(wet-0.45f)*2f));float forest=Mathf.Clamp01((1f-Mathf.Abs(hot-0.5f)*2f)*(wet-0.45f)*2f);float taiga=Mathf.Clamp01((0.55f-hot)*2f*(wet-0.35f)*2f);float tundra=Mathf.Clamp01((0.45f-hot)*2.5f);float polar=Mathf.Clamp01((0.3f-hot)*3f+elev[i]*0.2f+mtn[i]*0.2f);float marsh=Mathf.Clamp01(wet*0.8f*(0.45f-elev[i])*2f);float sum=jungle+desert+sav+grass+forest+taiga+tundra+polar+marsh+1e-5f;o0[i]=new Color32(B(jungle/sum),B(desert/sum),B(sav/sum),B(grass/sum));o1[i]=new Color32(B(forest/sum),B(taiga/sum),B(tundra/sum),B(polar/sum));o2[i]=new Color32(B(marsh/sum),B(polar),B(mtn[i]),0);}biomeWeights0Texture.SetPixelData(o0,0);biomeWeights0Texture.Apply(false,false);biomeWeights1Texture.SetPixelData(o1,0);biomeWeights1Texture.Apply(false,false);biomeWeights2Texture.SetPixelData(o2,0);biomeWeights2Texture.Apply(false,false);}    
}
