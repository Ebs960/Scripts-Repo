using UnityEngine;

public class MenuPlanetPreviewTectonicGenerator : MonoBehaviour
{
    [SerializeField] private int tectonicMapWidth = 1024;
    [SerializeField] private int tectonicMapHeight = 512;
    [SerializeField] private float regenerationDelay = 0.15f;

    [Header("Tectonic Plate Layout")]
    [SerializeField, Range(4, 32)] private int plateCount = 14;
    [SerializeField, Range(0.005f, 0.25f)] private float plateBoundaryWidth = 0.06f;
    [SerializeField, Range(0f, 1f)] private float continentalPlateFraction = 0.55f;
    [SerializeField, Range(0f, 1f)] private float plateShapeNoiseStrength = 0.28f;

    [Header("Elevation")]
    [SerializeField, Range(0f, 1f)] private float continentalElevationStrength = 0.42f;
    [SerializeField, Range(0f, 1f)] private float oceanBasinDepthStrength = 0.58f;
    [SerializeField, Range(0f, 1f)] private float convergentMountainStrength = 0.72f;
    [SerializeField, Range(0f, 1f)] private float divergentRidgeStrength = 0.18f;
    [SerializeField, Range(0f, 1f)] private float terrainDetailNoiseStrength = 0.14f;

    [Header("Coasts")]
    [SerializeField, Range(0f, 1f)] private float continentalShelfStrength = 0.75f;
    [SerializeField, Range(0.005f, 0.25f)] private float continentalShelfWidth = 0.08f;

    private struct PreviewTectonicPlate { public Vector3 centerDir, motionDir; public float continentalBias, baseElevationBias, ruggedness, age; }

    private Texture2D surfaceStructureTexture, plateBoundaryTexture, crustBasinTexture;
    private float scheduledAt = -1f, seed, landScale, landThreshold, elevation;
    private int landPreset;
    public Texture2D SurfaceStructureTexture => surfaceStructureTexture;
    public Texture2D PlateBoundaryTexture => plateBoundaryTexture;
    public Texture2D CrustBasinTexture => crustBasinTexture;

    public void SetInputs(float inSeed, float inLandScale, float inLandThreshold, float inElevation, int landPresetOrEquivalentIfAvailable)
    { seed = inSeed; landScale = inLandScale; landThreshold = inLandThreshold; elevation = inElevation; landPreset = landPresetOrEquivalentIfAvailable; }

    public void ScheduleRegeneration() => scheduledAt = Time.time + regenerationDelay;
    private void Update(){ if (scheduledAt > 0f && Time.time >= scheduledAt) GenerateNow(); }
    public void Release(){ if(surfaceStructureTexture!=null) Destroy(surfaceStructureTexture); if(plateBoundaryTexture!=null) Destroy(plateBoundaryTexture); if(crustBasinTexture!=null) Destroy(crustBasinTexture); }

    public void GenerateNow()
    {
        EnsureTextures();
        int size = tectonicMapWidth * tectonicMapHeight;
        var plates = BuildPlates();
        var surface = new Color32[size]; var boundary = new Color32[size]; var crust = new Color32[size];
        int continentalCount = 0;
        foreach (var p in plates) if (p.continentalBias > 0.5f) continentalCount++;

        for (int y = 0; y < tectonicMapHeight; y++)
        for (int x = 0; x < tectonicMapWidth; x++)
        {
            int i = y * tectonicMapWidth + x;
            Vector3 d = TexelToDir(x, y);
            int p0 = -1, p1 = -1; float best0 = -10f, best1 = -10f;
            for (int p = 0; p < plates.Length; p++)
            {
                float n = (Hash01(x + p * 37, y + p * 19) - 0.5f) * plateShapeNoiseStrength * 0.35f;
                float s = Vector3.Dot(d, plates[p].centerDir) + n;
                if (s > best0) { best1 = best0; p1 = p0; best0 = s; p0 = p; }
                else if (s > best1) { best1 = s; p1 = p; }
            }

            float delta = best0 - best1;
            float boundaryIntensity = 1f - Mathf.Clamp01(delta / Mathf.Max(0.0001f, plateBoundaryWidth));
            var A = plates[p0]; var B = plates[Mathf.Max(0,p1)];
            Vector3 bdir = Vector3.Normalize(B.centerDir - A.centerDir);
            Vector3 ma = A.motionDir; Vector3 mb = B.motionDir;
            float along = Vector3.Dot(ma - mb, bdir);
            float convergent = Mathf.Clamp01(-along * 0.9f) * boundaryIntensity;
            float divergent = Mathf.Clamp01(along * 0.9f) * boundaryIntensity;
            float transform = Mathf.Clamp01(1f - Mathf.Abs(along) * 1.3f) * boundaryIntensity;

            float interior = Mathf.Clamp01((best0 - 0.25f) / 0.75f);
            float plateNoise = Fbm(d * (landScale * 1.25f) + new Vector3(seed + p0 * 3.1f, seed * 0.71f, seed * 1.31f));
            float continental = Mathf.Clamp01(A.continentalBias * 0.72f + plateNoise * 0.28f + interior * 0.14f - divergent * 0.25f + convergent * 0.12f);
            float oceanic = 1f - continental;

            float shelfProximity = Mathf.Clamp01((continental - oceanic + continentalShelfWidth * 2f) / Mathf.Max(0.01f, continentalShelfWidth * 3f));
            float basinDepth = oceanic * (0.45f + 0.55f * Fbm(d * (landScale * 2.1f) + new Vector3(seed + 44.1f, seed + 12.2f, seed + 8.3f))) * oceanBasinDepthStrength;
            float mountainBelt = Mathf.Clamp01(convergent * (0.5f + continental * 0.9f) + transform * 0.08f) * (0.6f + 0.4f * A.ruggedness);

            float baseElevation = continental * continentalElevationStrength - basinDepth + divergent * divergentRidgeStrength * oceanic + mountainBelt * convergentMountainStrength;
            baseElevation += (Fbm(d * (landScale * 3.6f) + new Vector3(seed + 96.2f, seed + 51.8f, seed + 14.4f)) - 0.5f) * terrainDetailNoiseStrength;
            baseElevation += (elevation - 0.5f) * 0.28f;
            float seaLevel = Mathf.Lerp(0.42f, 0.62f, landThreshold);
            float landMask = Mathf.SmoothStep(seaLevel - 0.035f, seaLevel + 0.035f, baseElevation + 0.5f);
            float shelfMask = oceanic * (1f - landMask) * shelfProximity * continentalShelfStrength * Mathf.Clamp01(1f - basinDepth * 1.35f);

            float height = Mathf.Clamp01(baseElevation * 0.8f + 0.5f);
            surface[i] = new Color(landMask, height, Mathf.Clamp01(mountainBelt), Mathf.Clamp01(shelfMask));
            boundary[i] = new Color((float)p0 / Mathf.Max(1f, plates.Length - 1f), boundaryIntensity, convergent, divergent);
            crust[i] = new Color(continental, oceanic, Mathf.Clamp01(basinDepth), transform);
        }

        surfaceStructureTexture.SetPixels32(surface); surfaceStructureTexture.Apply(false, false);
        plateBoundaryTexture.SetPixels32(boundary); plateBoundaryTexture.Apply(false, false);
        crustBasinTexture.SetPixels32(crust); crustBasinTexture.Apply(false, false);
        scheduledAt = -1f;
        Debug.Log($"[Tectonic Preview] Generated {plates.Length} plates | {continentalCount} continental-biased | {plates.Length-continentalCount} oceanic-biased | surface tex {tectonicMapWidth}x{tectonicMapHeight}");
    }

    private void EnsureTextures()
    {
        surfaceStructureTexture = EnsureTex(surfaceStructureTexture, "MenuTectonicSurface");
        plateBoundaryTexture = EnsureTex(plateBoundaryTexture, "MenuTectonicBoundary");
        crustBasinTexture = EnsureTex(crustBasinTexture, "MenuTectonicCrust");
    }
    private Texture2D EnsureTex(Texture2D t, string n){ if(t!=null && t.width==tectonicMapWidth && t.height==tectonicMapHeight) return t; if(t!=null) Destroy(t); return new Texture2D(tectonicMapWidth,tectonicMapHeight,TextureFormat.RGBA32,false,true){wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Bilinear,name=n}; }
    private PreviewTectonicPlate[] BuildPlates(){ var p=new PreviewTectonicPlate[Mathf.Clamp(plateCount,4,32)]; int continentalTarget=Mathf.RoundToInt(p.Length*continentalPlateFraction); for(int i=0;i<p.Length;i++){Vector3 c=RandomOnSphere(i*31+7); Vector3 tangent = Vector3.Cross(c, RandomOnSphere(i*17+3)).normalized; if(tangent.sqrMagnitude<0.001f) tangent=Vector3.Cross(c,Vector3.up).normalized; float cont=i<continentalTarget?Mathf.Lerp(0.58f,0.95f,Hash01(i,99)):Mathf.Lerp(0.05f,0.45f,Hash01(i,77)); p[i]=new PreviewTectonicPlate{centerDir=c,motionDir=tangent,continentalBias=cont,baseElevationBias=Mathf.Lerp(-0.15f,0.25f,Hash01(i,41)),ruggedness=Mathf.Lerp(0.35f,1f,Hash01(i,57)),age=Hash01(i,13)};} return p; }
    private Vector3 TexelToDir(int x,int y){float u=(x+0.5f)/tectonicMapWidth; float v=(y+0.5f)/tectonicMapHeight; float lon=(u-0.5f)*Mathf.PI*2f; float lat=(v-0.5f)*Mathf.PI; float cl=Mathf.Cos(lat); return new Vector3(Mathf.Cos(lon)*cl,Mathf.Sin(lat),Mathf.Sin(lon)*cl);}    
    private Vector3 RandomOnSphere(int salt){ float u=Hash01(salt,1); float v=Hash01(salt,2); float lon=u*Mathf.PI*2f; float z=2f*v-1f; float r=Mathf.Sqrt(Mathf.Max(0f,1f-z*z)); return new Vector3(r*Mathf.Cos(lon), z, r*Mathf.Sin(lon)); }
    private float Fbm(Vector3 p){float v=0,a=0.5f,f=1f; for(int i=0;i<4;i++){v+=a*Mathf.PerlinNoise(p.x*f+p.y*0.67f,p.z*f+p.y*0.37f); f*=2f; a*=0.5f;} return v;}
    private float Hash01(int x,int y){uint s=(uint)Mathf.RoundToInt(seed*1000f) ^ (uint)(landPreset*193); uint n=(uint)(x*73856093)^(uint)(y*19349663)^s; n=(n<<13)^n; return Mathf.Clamp01((1f-((n*(n*n*15731u+789221u)+1376312589u)&0x7fffffffu)/1073741824f)*0.5f+0.5f);}   
}
