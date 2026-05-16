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

    private void Flush()
    {
        scheduledAt = -1f;
        var s = pending; pending = PreviewWorldRebuildScope.None; if (s == PreviewWorldRebuildScope.None) return;
        EnsureAllTextures();
        float[] land = null, elev = null, mtn = null, shelf = null, cont = null, moisture = null, temp = null;
        if ((s & PreviewWorldRebuildScope.Tectonics) != 0)
        {
            GenerateTerrain(out land, out elev, out mtn, out shelf, out cont);
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
            GenerateHydrology(land, elev, moisture);
        }
        if ((s & PreviewWorldRebuildScope.Biomes) != 0)
        {
            if (land == null) ReadSurface(out land, out elev, out mtn, out shelf);
            if (temp == null || moisture == null) ReadClimate(out temp, out moisture, out cont);
            GenerateBiomes(land, elev, mtn, temp, moisture);
        }
        WorldTexturesUpdated?.Invoke();
    }

    // helper implementations omitted for brevity in this scaffold
    // (compact but fully functional enough for shader feeding)
    private void EnsureAllTextures(){Ensure(ref surfaceDataTexture,"MenuSurfaceDataV2");Ensure(ref auxiliaryMaskTexture,"MenuAuxMasksV2");Ensure(ref worldStructureTexture,"MenuStructureV2");Ensure(ref climateTexture,"MenuClimateV2");Ensure(ref hydrologyMaskTexture,"MenuHydrologyV2");Ensure(ref biomeWeights0Texture,"MenuBiome0V2");Ensure(ref biomeWeights1Texture,"MenuBiome1V2");Ensure(ref biomeWeights2Texture,"MenuBiome2V2");}
    private void Ensure(ref Texture2D t,string n){if(t!=null&&t.width==mapWidth&&t.height==mapHeight)return;DestroyTex(ref t);t=new Texture2D(mapWidth,mapHeight,TextureFormat.RGBA32,false,true){name=n,wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Bilinear};}
    private void DestroyTex(ref Texture2D t){if(t!=null)Destroy(t);t=null;}
    private static byte B(float v)=> (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(v)*255f),0,255);
    private void GenerateTerrain(out float[] land,out float[] elev,out float[] mtn,out float[] shelf,out float[] cont){int n=mapWidth*mapHeight;land=new float[n];elev=new float[n];mtn=new float[n];shelf=new float[n];cont=new float[n];for(int i=0;i<n;i++){int x=i%mapWidth,y=i/mapWidth;float u=(float)x/mapWidth,v=(float)y/mapHeight;float lat=Mathf.Abs(v*2f-1f);float l=Mathf.PerlinNoise((u+inputs.seed*0.01f)*3f,(v-inputs.seed*0.02f)*2f)>inputs.landThreshold?1f:0f;land[i]=l;float inland=l*(1f-lat*0.6f);elev[i]=Mathf.Clamp01(l*(0.2f+inland*0.5f+Mathf.PerlinNoise(u*8f,v*8f)*0.25f*inputs.elevation));mtn[i]=l*Mathf.Clamp01((Mathf.PerlinNoise(u*16f+71.2f,v*16f+13.9f)-0.45f)*(0.8f+inputs.elevation));shelf[i]=(1f-l)*Mathf.Clamp01(1f-Mathf.PerlinNoise(u*5f,v*5f)*1.2f);cont[i]=l*inland;}var px=new Color32[n];for(int i=0;i<n;i++)px[i]=new Color32(B(land[i]),B(elev[i]),B(mtn[i]),B(shelf[i]));surfaceDataTexture.SetPixelData(px,0);surfaceDataTexture.Apply(false,false);var b=new Color32[n];for(int i=0;i<n;i++)b[i]=new Color32(B(cont[i]),0,0,0);worldStructureTexture.SetPixelData(b,0);worldStructureTexture.Apply(false,false);auxiliaryMaskTexture.SetPixelData(b,0);auxiliaryMaskTexture.Apply(false,false);}    
    private void WriteSurfaceAndStructure(float[] land,float[] elev,float[] mtn,float[] shelf,float[] cont){}
    private void ReadSurface(out float[] land,out float[] elev,out float[] mtn,out float[] shelf){int n=mapWidth*mapHeight;land=new float[n];elev=new float[n];mtn=new float[n];shelf=new float[n];var p=surfaceDataTexture.GetPixels32();for(int i=0;i<n;i++){land[i]=p[i].r/255f;elev[i]=p[i].g/255f;mtn[i]=p[i].b/255f;shelf[i]=p[i].a/255f;}}
    private void ReadClimate(out float[] temp,out float[] moisture,out float[] cont){int n=mapWidth*mapHeight;temp=new float[n];moisture=new float[n];cont=new float[n];var p=climateTexture.GetPixels32();for(int i=0;i<n;i++){temp[i]=p[i].r/255f;moisture[i]=p[i].g/255f;cont[i]=p[i].b/255f;}}
    private void GenerateClimate(float[] land,float[] elev,float[] mtn,float[] contIn,out float[] temp,out float[] moisture){int n=mapWidth*mapHeight;temp=new float[n];moisture=new float[n];var p=new Color32[n];for(int i=0;i<n;i++){int y=i/mapWidth;float lat=Mathf.Abs(((float)y/mapHeight)*2f-1f);float t=Mathf.Clamp01((1f-lat)+(inputs.temperature-0.5f)*0.6f-elev[i]*0.35f);float m=Mathf.Clamp01(inputs.moisture+land[i]*(0.35f-contIn[i]*0.3f)+mtn[i]*0.08f);temp[i]=t;moisture[i]=m;p[i]=new Color32(B(t),B(m),B(contIn[i]),B(inputs.seasonalityStrength));}climateTexture.SetPixelData(p,0);climateTexture.Apply(false,false);}    
    private void GenerateHydrology(float[] land,float[] elev,float[] moisture){int n=mapWidth*mapHeight;var p=new Color32[n];for(int i=0;i<n;i++){float river=land[i]*Mathf.Clamp01((moisture[i]-0.55f)*2f+elev[i]*0.4f);float lake=land[i]*Mathf.Clamp01((0.35f-elev[i])*2f*moisture[i]);float wet=land[i]*Mathf.Max(river*0.7f,lake*0.8f);p[i]=new Color32(B(river),B(lake),B(wet),B(Mathf.Max(river,lake)));}hydrologyMaskTexture.SetPixelData(p,0);hydrologyMaskTexture.Apply(false,false);}    
    private void GenerateBiomes(float[] land,float[] elev,float[] mtn,float[] temp,float[] moisture){int n=mapWidth*mapHeight;var o0=new Color32[n];var o1=new Color32[n];var o2=new Color32[n];for(int i=0;i<n;i++){if(land[i]<0.5f){o0[i]=o1[i]=o2[i]=new Color32();continue;}float hot=temp[i],wet=moisture[i];float jungle=Mathf.Clamp01((hot-0.6f)*2f*(wet-0.6f)*2f);float desert=Mathf.Clamp01((hot-0.5f)*2f*(0.7f-wet)*2f);float sav=Mathf.Clamp01((hot-0.5f)*2f*(1f-Mathf.Abs(wet-0.5f)*2f));float grass=Mathf.Clamp01((1f-Mathf.Abs(hot-0.5f)*2f)*(1f-Mathf.Abs(wet-0.45f)*2f));float forest=Mathf.Clamp01((1f-Mathf.Abs(hot-0.5f)*2f)*(wet-0.45f)*2f);float taiga=Mathf.Clamp01((0.55f-hot)*2f*(wet-0.35f)*2f);float tundra=Mathf.Clamp01((0.45f-hot)*2.5f);float polar=Mathf.Clamp01((0.3f-hot)*3f+elev[i]*0.2f+mtn[i]*0.2f);float marsh=Mathf.Clamp01(wet*0.8f*(0.45f-elev[i])*2f);float sum=jungle+desert+sav+grass+forest+taiga+tundra+polar+marsh+1e-5f;o0[i]=new Color32(B(jungle/sum),B(desert/sum),B(sav/sum),B(grass/sum));o1[i]=new Color32(B(forest/sum),B(taiga/sum),B(tundra/sum),B(polar/sum));o2[i]=new Color32(B(marsh/sum),B(polar),B(mtn[i]),0);}biomeWeights0Texture.SetPixelData(o0,0);biomeWeights0Texture.Apply(false,false);biomeWeights1Texture.SetPixelData(o1,0);biomeWeights1Texture.Apply(false,false);biomeWeights2Texture.SetPixelData(o2,0);biomeWeights2Texture.Apply(false,false);}    
}
