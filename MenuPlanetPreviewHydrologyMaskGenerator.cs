using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

public class MenuPlanetPreviewHydrologyMaskGenerator : MonoBehaviour
{
    [SerializeField] private int hydrologyMaskWidth = 1024;
    [SerializeField] private int hydrologyMaskHeight = 512;
    [SerializeField] private float regenerationDelay = 0.15f;
    [SerializeField] private bool useTectonicSurfaceInputs = true;
    [SerializeField] private bool logHydrologyDiagnostics = false;

    private Texture2D hydrologyMaskTexture;
    private Texture2D tectonicSurfaceTexture;
    private Texture2D climateTexture;
    private float scheduledAt = -1f;
    private float seed, landScale, landThreshold, elevation, moisture, temperature;
    private int waterwaysPreset;

    public Texture2D MaskTexture => hydrologyMaskTexture;
    public event System.Action HydrologyMaskReady;
    public void SetTectonicSurfaceTexture(Texture2D texture) => tectonicSurfaceTexture = texture;
    public void SetClimateTexture(Texture2D texture) => climateTexture = texture;
    public void SetInputs(float inSeed, float inLandScale, float inLandThreshold, float inElevation, float inMoisture, float inTemperature, int inWaterwaysPreset){seed=inSeed;landScale=inLandScale;landThreshold=inLandThreshold;elevation=inElevation;moisture=inMoisture;temperature=inTemperature;waterwaysPreset=inWaterwaysPreset;}
    public void ScheduleRegeneration() => scheduledAt = Time.time + regenerationDelay;

    public void GenerateNow()
    {
        EnsureTexture();
        int size = hydrologyMaskWidth * hydrologyMaskHeight;
        float[] land = new float[size], h = new float[size], mountain = new float[size], shelf = new float[size], river = new float[size], lake = new float[size], flow = new float[size], wetland = new float[size], depth = new float[size], rainfall = new float[size];
        bool usedTectonic = BuildScalarFields(land, h, mountain, shelf);
        BuildRainfall(rainfall);
        if (!usedTectonic && useTectonicSurfaceInputs) Debug.LogWarning("[Hydrology Preview] Missing tectonic surface texture; using legacy fallback scalar fields.");
        var sources = PickSources(land, h, mountain, shelf, rainfall);
        int reachedCoast = 0;
        foreach (var src in sources) if (TraceRiver(src, land, h, shelf, river, lake, flow, wetland, depth)) reachedCoast++;
        BuildWetlands(land, h, river, lake, wetland, rainfall);
        Blur(river); Blur(lake); Blur(wetland); Blur(depth);
        var px = new Color32[size]; for (int i = 0; i < size; i++) px[i] = new Color32((byte)(Mathf.Clamp01(river[i])*255f),(byte)(Mathf.Clamp01(lake[i])*255f),(byte)(Mathf.Clamp01(wetland[i])*255f),(byte)(Mathf.Clamp01(depth[i])*255f));
        hydrologyMaskTexture.SetPixels32(px); hydrologyMaskTexture.Apply(false,false); scheduledAt = -1f;
        if (logHydrologyDiagnostics) Debug.Log($"[Hydrology Preview] Built from {(usedTectonic ? "tectonic surface" : "legacy fallback")} | sources={sources.Count} | reached coast={reachedCoast}");
        HydrologyMaskReady?.Invoke();
    }

    private bool BuildScalarFields(float[] land, float[] height, float[] mountain, float[] shelf)
    {
        if (useTectonicSurfaceInputs && tectonicSurfaceTexture != null && tectonicSurfaceTexture.format == TextureFormat.RGBA32)
        {
            var raw = tectonicSurfaceTexture.GetRawTextureData<byte>();
            int tw = tectonicSurfaceTexture.width, th = tectonicSurfaceTexture.height;
            bool same = tw == hydrologyMaskWidth && th == hydrologyMaskHeight;
            for (int y = 0; y < hydrologyMaskHeight; y++) for (int x = 0; x < hydrologyMaskWidth; x++)
            {
                int i = y * hydrologyMaskWidth + x;
                int tx = same ? x : Mathf.FloorToInt((x + 0.5f) / hydrologyMaskWidth * tw) % tw;
                int ty = same ? y : Mathf.Clamp(Mathf.FloorToInt((y + 0.5f) / hydrologyMaskHeight * th), 0, th - 1);
                int o = (ty * tw + tx) * 4;
                land[i] = raw[o + 0] / 255f; height[i] = raw[o + 1] / 255f; mountain[i] = raw[o + 2] / 255f; shelf[i] = raw[o + 3] / 255f;
            }
            return true;
        }

        for (int y=0;y<hydrologyMaskHeight;y++) for (int x=0;x<hydrologyMaskWidth;x++) { int i=y*hydrologyMaskWidth+x; Vector3 d=TexelToDir(x,y); float lv=Fbm(d*landScale+new Vector3(seed,seed*.7f,seed*1.3f)); float lm=SmoothThreshold(landThreshold-0.04f,landThreshold+0.04f,lv); land[i]=lm; float hills=Fbm(d*(landScale*2.2f)+new Vector3(seed+99.1f,seed+55.3f,seed+12.7f)); height[i]=Mathf.Lerp(-0.08f,1f,lm)*(0.3f+0.7f*elevation)+hills*0.2f; mountain[i]=Mathf.Clamp01(hills); shelf[i]=Mathf.Clamp01((1f-lm)*0.5f);}        
        return false;
    }

    private List<int> PickSources(float[] land,float[] h,float[] mountain,float[] shelf,float[] rainfall){int target=waterwaysPreset==0?16:waterwaysPreset==1?34:68; float avgRain=0f; int cRain=0; for(int i=0;i<land.Length;i++){ if(land[i]>0.5f){avgRain+=rainfall[i]; cRain++;}} avgRain=cRain>0?avgRain/cRain:moisture; target=Mathf.RoundToInt(target*Mathf.Lerp(0.45f,1.2f,avgRain)); var c=new List<int>(); for(int y=16;y<hydrologyMaskHeight-16;y+=3) for(int x=0;x<hydrologyMaskWidth;x+=3){int i=y*hydrologyMaskWidth+x; if(land[i]<0.6f||h[i]<0.35f||shelf[i]>0.2f) continue; float s=h[i]*0.45f+mountain[i]*0.20f+rainfall[i]*0.35f; s*=0.9f+0.2f*Hash01(x,y); if(s>0.5f) c.Add(i);} c.Sort((a,b)=>h[b].CompareTo(h[a])); var outp=new List<int>(); foreach(var i in c){bool near=false; int x=i%hydrologyMaskWidth,y=i/hydrologyMaskWidth; foreach(var o in outp){int ox=o%hydrologyMaskWidth, oy=o/hydrologyMaskWidth; if(Mathf.Abs(ox-x)+Mathf.Abs(oy-y)<24){near=true;break;}} if(!near){outp.Add(i); if(outp.Count>=target)break;}} return outp; }
    private bool TraceRiver(int src,float[] land,float[] h,float[] shelf,float[] river,float[] lake,float[] flow,float[] wetland,float[] depth){var vis=new HashSet<int>(); int cur=src; int prev=src; for(int step=0;step<900;step++){if(!vis.Add(cur)) break; int x=cur%hydrologyMaskWidth,y=cur/hydrologyMaskWidth; float flowVal=flow[cur]+1f; float width=Mathf.Lerp(1f,4.8f,Mathf.Clamp01(flowVal/8f + step/650f)); Stamp(river,x,y,width,Mathf.Lerp(0.5f,0.95f,moisture)); Stamp(depth,x,y,Mathf.Lerp(0.8f,1.2f,width*0.16f),Mathf.Clamp01(0.25f+flowVal*0.08f+step/1200f)); flow[cur]+=1f; bool reachedOcean=land[cur]<0.5f; bool reachedCoastalShelf=shelf[cur]>0.2f; if(reachedOcean||reachedCoastalShelf){if(flowVal>2.5f) StampDelta(cur,prev,flowVal,land,river,wetland,depth); return true;} int best=cur; float bestH=h[cur]; for(int ny=-1;ny<=1;ny++)for(int nx=-1;nx<=1;nx++){if(nx==0&&ny==0)continue; int yy=Mathf.Clamp(y+ny,0,hydrologyMaskHeight-1); int xx=(x+nx+hydrologyMaskWidth)%hydrologyMaskWidth; int ni=yy*hydrologyMaskWidth+xx; float cand=h[ni]+(Hash01(xx,yy)-0.5f)*0.01f; if(cand<bestH){bestH=cand; best=ni;}} if(best==cur){float lr=Mathf.Lerp(2f,7f,Hash01(x,y)); Stamp(lake,x,y,lr,0.8f); Stamp(depth,x,y,lr*0.7f,0.95f); Stamp(wetland,x,y,lr*1.2f,0.45f); break;} if(river[best]>0.35f){flow[best]+=flow[cur]+1f; break;} prev=cur; cur=best; } return false;}
    private void StampDelta(int mouth,int prev,float flowAmt,float[] land,float[] river,float[] wetland,float[] depth){int mx=mouth%hydrologyMaskWidth,my=mouth/hydrologyMaskWidth; if(land[mouth]>0.55f) return; int px=prev%hydrologyMaskWidth,py=prev/hydrologyMaskWidth; Vector2 dir=new Vector2(mx-px,my-py); if(dir.sqrMagnitude<0.001f) dir=Vector2.right; dir.Normalize(); float baseR=Mathf.Lerp(2.5f,7.5f,Mathf.Clamp01(flowAmt/10f)); for(int i=0;i<6;i++){float t=i/5f; float spread=(Hash01(mx+i,my)-0.5f)*2.4f; Vector2 ortho=new Vector2(-dir.y,dir.x); int cx=(int)Mathf.Round(mx + dir.x*(1.5f+i*1.1f) + ortho.x*spread); int cy=(int)Mathf.Round(my + dir.y*(1.5f+i*1.1f) + ortho.y*spread); float r=baseR*(0.45f+0.65f*t); Stamp(river,cx,cy,r,Mathf.Lerp(0.35f,0.82f,t)); Stamp(wetland,cx,cy,r*1.25f,Mathf.Lerp(0.25f,0.55f,t)); Stamp(depth,cx,cy,r*0.65f,Mathf.Lerp(0.4f,0.85f,t)); }}
    private void BuildWetlands(float[] land,float[] h,float[] river,float[] lake,float[] wetland,float[] rainfall){for(int y=0;y<hydrologyMaskHeight;y++)for(int x=0;x<hydrologyMaskWidth;x++){int i=y*hydrologyMaskWidth+x; float nearby=0f; for(int ny=-2;ny<=2;ny++)for(int nx=-2;nx<=2;nx++){int yy=Mathf.Clamp(y+ny,0,hydrologyMaskHeight-1); int xx=(x+nx+hydrologyMaskWidth)%hydrologyMaskWidth; int ni=yy*hydrologyMaskWidth+xx; nearby=Mathf.Max(nearby,Mathf.Max(river[ni],lake[ni]));} float lowland=Mathf.Clamp01((0.55f-h[i])*1.8f); float moistureFactor=Mathf.Lerp(0.35f,1f,rainfall[i]); float local=nearby*lowland*moistureFactor*land[i]; wetland[i]=Mathf.Max(wetland[i],local*0.75f); }}



    private void BuildRainfall(float[] rainfall){
        if (climateTexture != null && climateTexture.format == TextureFormat.RGBA32){
            var raw = climateTexture.GetRawTextureData<byte>();
            int cw = climateTexture.width, ch = climateTexture.height;
            bool same = cw == hydrologyMaskWidth && ch == hydrologyMaskHeight;
            for (int y=0;y<hydrologyMaskHeight;y++) for (int x=0;x<hydrologyMaskWidth;x++){
                int i=y*hydrologyMaskWidth+x;
                int tx = same ? x : Mathf.FloorToInt((x + 0.5f) / hydrologyMaskWidth * cw) % cw;
                int ty = same ? y : Mathf.Clamp(Mathf.FloorToInt((y + 0.5f) / hydrologyMaskHeight * ch), 0, ch - 1);
                rainfall[i] = raw[(ty*cw+tx)*4 + 1] / 255f;
            }
            return;
        }
        for (int i=0;i<rainfall.Length;i++) rainfall[i] = moisture;
    }
    private void Update() { if (scheduledAt > 0f && Time.time >= scheduledAt) GenerateNow(); }
    public void Release() { if (hydrologyMaskTexture != null) Destroy(hydrologyMaskTexture); hydrologyMaskTexture = null; }
    private void EnsureTexture(){ if(hydrologyMaskTexture!=null && hydrologyMaskTexture.width==hydrologyMaskWidth && hydrologyMaskTexture.height==hydrologyMaskHeight) return; if(hydrologyMaskTexture!=null) Destroy(hydrologyMaskTexture); hydrologyMaskTexture=new Texture2D(hydrologyMaskWidth,hydrologyMaskHeight,TextureFormat.RGBA32,false,true){wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Bilinear,name="MenuHydrologyMask"}; }
    private void Stamp(float[] buf,int cx,int cy,float rad,float str){int r=Mathf.CeilToInt(rad); for(int y=-r;y<=r;y++){int yy=Mathf.Clamp(cy+y,0,hydrologyMaskHeight-1); for(int x=-r;x<=r;x++){float d=Mathf.Sqrt(x*x+y*y)/Mathf.Max(0.001f,rad); if(d>1f) continue; int xx=(cx+x+hydrologyMaskWidth)%hydrologyMaskWidth; int i=yy*hydrologyMaskWidth+xx; float a=(1f-d*d)*str; if(a>buf[i]) buf[i]=a; }}}
    private void Blur(float[] b){var t=new float[b.Length]; int w=hydrologyMaskWidth,h=hydrologyMaskHeight; for(int y=0;y<h;y++)for(int x=0;x<w;x++){float s=0;int c=0; for(int ny=-1;ny<=1;ny++)for(int nx=-1;nx<=1;nx++){int yy=Mathf.Clamp(y+ny,0,h-1); int xx=(x+nx+w)%w; s+=b[yy*w+xx]; c++;} t[y*w+x]=s/c;} System.Array.Copy(t,b,b.Length);}    
    private static float SmoothThreshold(float edge0, float edge1, float x){float t = Mathf.InverseLerp(edge0, edge1, x); return t * t * (3f - 2f * t);}    
    private Vector3 TexelToDir(int x,int y){float u=(x+0.5f)/hydrologyMaskWidth; float v=(y+0.5f)/hydrologyMaskHeight; float lon=(u-0.5f)*Mathf.PI*2f; float lat=(v-0.5f)*Mathf.PI; float cl=Mathf.Cos(lat); return new Vector3(Mathf.Cos(lon)*cl,Mathf.Sin(lat),Mathf.Sin(lon)*cl);}    
    private float Fbm(Vector3 p){float v=0,a=0.5f,f=1f; for(int i=0;i<4;i++){v+=a*Mathf.PerlinNoise(p.x*f + p.y*0.73f,p.z*f+p.y*0.41f); f*=2f;a*=0.5f;} return v;}    
    private float Hash01(int x,int y){uint n=(uint)(x*73856093)^(uint)(y*19349663)^(uint)(Mathf.RoundToInt(seed)*83492791); n=(n<<13)^n; return 1f-((n*(n*n*15731u+789221u)+1376312589u)&0x7fffffffu)/1073741824f*0.5f;}
}
