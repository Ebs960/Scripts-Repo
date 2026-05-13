using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public class MenuPlanetPreviewClimateBiomeGenerator : MonoBehaviour
{
    [SerializeField] private int climateMapWidth = 1024;
    [SerializeField] private int climateMapHeight = 512;
    [SerializeField] private float regenerationDelay = 0.15f;
    [SerializeField] private bool logClimateDiagnostics = false;
    [SerializeField] private bool logBiomeDiagnostics = false;
    [SerializeField, Range(8, 256)] private int maxContinentalDistancePixels = 96;

    private Texture2D tectonicSurfaceTexture;
    private Texture2D waterwayMaskTexture;
    private Texture2D climateTexture;
    private Texture2D biomeWeights0Texture;
    private Texture2D biomeWeights1Texture;
    private Texture2D biomeWeights2Texture;

    private bool tectonicInputsDirty = true;
    private float[] cachedContinentality;
    private bool continentalityCacheDirty = true;
    private float scheduledClimateAt = -1f;
    private float scheduledBiomeAt = -1f;

    private float seed, temperature, moisture, moistureResponseScale, temperatureHumidityInfluence, climateNoiseStrength, coastWetnessStrength, continentalDrynessStrength, continentalTemperatureStrength, rainShadowStrength, orographicWetnessStrength, orographicSampleOffset, seasonalityStrength;
    private float biomeProvinceStrength, biomeCompetitionSharpness, iceCapSize;

    public Texture2D ClimateTexture => climateTexture;
    public Texture2D BiomeWeights0Texture => biomeWeights0Texture;
    public Texture2D BiomeWeights1Texture => biomeWeights1Texture;
    public Texture2D BiomeWeights2Texture => biomeWeights2Texture;
    public event Action ClimateTextureReady;
    public event Action BiomeTexturesReady;

    public void SetTectonicSurfaceTexture(Texture2D texture) { tectonicSurfaceTexture = texture; tectonicInputsDirty = true; continentalityCacheDirty = true; }
    public void SetWaterwayMaskTexture(Texture2D texture) { waterwayMaskTexture = texture; }
    public void SetClimateInputs(float inSeed, float inTemperature, float inMoisture, float inMoistureResponseScale, float inTemperatureHumidityInfluence, float inClimateNoiseStrength, float inCoastWetnessStrength, float inContinentalDrynessStrength, float inContinentalTemperatureStrength, float inRainShadowStrength, float inOrographicWetnessStrength, float inOrographicSampleOffset, float inSeasonalityStrength)
    { seed = inSeed; temperature = inTemperature; moisture = inMoisture; moistureResponseScale = inMoistureResponseScale; temperatureHumidityInfluence = inTemperatureHumidityInfluence; climateNoiseStrength = inClimateNoiseStrength; coastWetnessStrength = inCoastWetnessStrength; continentalDrynessStrength = inContinentalDrynessStrength; continentalTemperatureStrength = inContinentalTemperatureStrength; rainShadowStrength = inRainShadowStrength; orographicWetnessStrength = inOrographicWetnessStrength; orographicSampleOffset = inOrographicSampleOffset; seasonalityStrength = inSeasonalityStrength; }
    public void SetBiomeInputs(float inBiomeProvinceStrength, float inBiomeCompetitionSharpness, float inIceCapSize) { biomeProvinceStrength = inBiomeProvinceStrength; biomeCompetitionSharpness = inBiomeCompetitionSharpness; iceCapSize = inIceCapSize; }
    public void ScheduleClimateRegeneration() => scheduledClimateAt = Time.time + regenerationDelay;
    public void ScheduleBiomeRegeneration() => scheduledBiomeAt = Time.time + regenerationDelay;

    public void GenerateClimateNow()
    {
        if (tectonicSurfaceTexture == null) return;
        EnsureTextures();
        RebuildContinentalityIfNeeded();
        var tectRaw = tectonicSurfaceTexture.GetRawTextureData<byte>();
        var climateOut = new NativeArray<byte>(climateMapWidth * climateMapHeight * 4, Allocator.TempJob);
        var tect = new NativeArray<byte>(tectRaw.ToArray(), Allocator.TempJob);
        var cont = new NativeArray<float>(cachedContinentality, Allocator.TempJob);
        var job = new GenerateClimateTextureJob { width = climateMapWidth, height = climateMapHeight, seed = seed, temperature = temperature, moisture = moisture, moistureResponseScale = moistureResponseScale, temperatureHumidityInfluence = temperatureHumidityInfluence, climateNoiseStrength = climateNoiseStrength, coastWetnessStrength = coastWetnessStrength, continentalDrynessStrength = continentalDrynessStrength, continentalTemperatureStrength = continentalTemperatureStrength, rainShadowStrength = rainShadowStrength, orographicWetnessStrength = orographicWetnessStrength, orographicSampleOffset = Mathf.Max(1f, orographicSampleOffset * climateMapWidth), seasonalityStrength = seasonalityStrength, maxDistance = maxContinentalDistancePixels, tectonicBytes = tect, continentality = cont, outputClimate = climateOut };
        job.Schedule(climateMapWidth * climateMapHeight, 128).Complete();
        climateTexture.LoadRawTextureData(climateOut);
        climateTexture.Apply(false, false);
        tect.Dispose(); cont.Dispose(); climateOut.Dispose();
        scheduledClimateAt = -1f;
        ClimateTextureReady?.Invoke();
    }

    public void GenerateBiomesNow()
    {
        if (tectonicSurfaceTexture == null || climateTexture == null || waterwayMaskTexture == null) return;
        EnsureTextures();
        var tect = new NativeArray<byte>(tectonicSurfaceTexture.GetRawTextureData<byte>().ToArray(), Allocator.TempJob);
        var climate = new NativeArray<byte>(climateTexture.GetRawTextureData<byte>().ToArray(), Allocator.TempJob);
        var hydro = new NativeArray<byte>(waterwayMaskTexture.GetRawTextureData<byte>().ToArray(), Allocator.TempJob);
        var out0 = new NativeArray<byte>(climateMapWidth * climateMapHeight * 4, Allocator.TempJob);
        var out1 = new NativeArray<byte>(climateMapWidth * climateMapHeight * 4, Allocator.TempJob);
        var out2 = new NativeArray<byte>(climateMapWidth * climateMapHeight * 4, Allocator.TempJob);
        new GenerateBiomeTexturesJob { width = climateMapWidth, height = climateMapHeight, seed = seed, biomeProvinceStrength = biomeProvinceStrength, biomeCompetitionSharpness = Mathf.Max(0.5f, biomeCompetitionSharpness), iceCapSize = iceCapSize, tectonicBytes = tect, climateBytes = climate, hydrologyBytes = hydro, outBiome0 = out0, outBiome1 = out1, outBiome2 = out2 }.Schedule(climateMapWidth * climateMapHeight, 128).Complete();
        biomeWeights0Texture.LoadRawTextureData(out0); biomeWeights0Texture.Apply(false, false);
        biomeWeights1Texture.LoadRawTextureData(out1); biomeWeights1Texture.Apply(false, false);
        biomeWeights2Texture.LoadRawTextureData(out2); biomeWeights2Texture.Apply(false, false);
        tect.Dispose(); climate.Dispose(); hydro.Dispose(); out0.Dispose(); out1.Dispose(); out2.Dispose();
        scheduledBiomeAt = -1f;
        BiomeTexturesReady?.Invoke();
    }

    private void RebuildContinentalityIfNeeded()
    {
        if (!continentalityCacheDirty && !tectonicInputsDirty && cachedContinentality != null && cachedContinentality.Length == climateMapWidth * climateMapHeight) return;
        cachedContinentality = new float[climateMapWidth * climateMapHeight];
        var raw = tectonicSurfaceTexture.GetRawTextureData<byte>();
        for (int y = 0; y < climateMapHeight; y++)
        for (int x = 0; x < climateMapWidth; x++)
        {
            int i = y * climateMapWidth + x;
            int o = i * 4;
            if (raw[o] < 128) { cachedContinentality[i] = 0f; continue; }
            int best = maxContinentalDistancePixels;
            for (int r = 1; r <= maxContinentalDistancePixels; r++)
            {
                bool found = false;
                for (int dy = -r; dy <= r && !found; dy++)
                {
                    int yy = Mathf.Clamp(y + dy, 0, climateMapHeight - 1);
                    int dx = r - Mathf.Abs(dy);
                    int x0 = (x - dx + climateMapWidth) % climateMapWidth;
                    int x1 = (x + dx) % climateMapWidth;
                    if (raw[(yy * climateMapWidth + x0) * 4] < 128 || raw[(yy * climateMapWidth + x1) * 4] < 128) found = true;
                }
                if (found) { best = r; break; }
            }
            cachedContinentality[i] = Mathf.Clamp01(best / (float)maxContinentalDistancePixels);
        }
        tectonicInputsDirty = false;
        continentalityCacheDirty = false;
    }

    private void EnsureTextures()
    {
        CreateOrResize(ref climateTexture, "MenuClimate", TextureWrapMode.Repeat);
        CreateOrResize(ref biomeWeights0Texture, "MenuBiome0", TextureWrapMode.Repeat);
        CreateOrResize(ref biomeWeights1Texture, "MenuBiome1", TextureWrapMode.Repeat);
        CreateOrResize(ref biomeWeights2Texture, "MenuBiome2", TextureWrapMode.Repeat);
    }
    private void CreateOrResize(ref Texture2D t, string n, TextureWrapMode wrap)
    {
        if (t != null && t.width == climateMapWidth && t.height == climateMapHeight) return;
        if (t != null) Destroy(t);
        t = new Texture2D(climateMapWidth, climateMapHeight, TextureFormat.RGBA32, false, true) { name = n, wrapMode = wrap, filterMode = FilterMode.Bilinear };
    }
    private void Update(){ if (scheduledClimateAt > 0f && Time.time >= scheduledClimateAt) GenerateClimateNow(); if (scheduledBiomeAt > 0f && Time.time >= scheduledBiomeAt) GenerateBiomesNow(); }

    [BurstCompile] private struct GenerateClimateTextureJob : IJobParallelFor { public int width,height; public float seed,temperature,moisture,moistureResponseScale,temperatureHumidityInfluence,climateNoiseStrength,coastWetnessStrength,continentalDrynessStrength,continentalTemperatureStrength,rainShadowStrength,orographicWetnessStrength,orographicSampleOffset,seasonalityStrength,maxDistance; [ReadOnly] public NativeArray<byte> tectonicBytes; [ReadOnly] public NativeArray<float> continentality; public NativeArray<byte> outputClimate; public void Execute(int i){int o=i*4; int x=i%width; int y=i/width; float land=tectonicBytes[o]/255f; float h=tectonicBytes[o+1]/255f; float m=tectonicBytes[o+2]/255f; float c=continentality[i]; float lat=math.abs(((y+0.5f)/height)*2f-1f); float latHeat=1f-lat; float noise=(math.sin((x+seed)*0.017f)+math.cos((y-seed)*0.021f))*0.5f; float temp=math.saturate(latHeat + (temperature-0.5f)*0.35f - h*0.4f + c*continentalTemperatureStrength*(temperature-0.5f) + noise*climateNoiseStrength*0.08f); float baseM=math.saturate(0.5f+(moisture-0.5f)*moistureResponseScale); float moist=baseM + (1f-c)*coastWetnessStrength*land - c*continentalDrynessStrength*land + (0.5f-math.abs(temp-0.5f))*temperatureHumidityInfluence; moist += m*orographicWetnessStrength - m*rainShadowStrength*0.5f + noise*climateNoiseStrength*0.08f; moist=math.saturate(moist); float season=math.saturate(lat*0.75f + c*0.45f + noise*seasonalityStrength*0.15f); outputClimate[o]=(byte)(temp*255f); outputClimate[o+1]=(byte)(moist*255f); outputClimate[o+2]=(byte)(c*255f); outputClimate[o+3]=(byte)(season*255f);} }
    [BurstCompile] private struct GenerateBiomeTexturesJob : IJobParallelFor { public int width,height; public float seed,biomeProvinceStrength,biomeCompetitionSharpness,iceCapSize; [ReadOnly] public NativeArray<byte> tectonicBytes; [ReadOnly] public NativeArray<byte> climateBytes; [ReadOnly] public NativeArray<byte> hydrologyBytes; public NativeArray<byte> outBiome0,outBiome1,outBiome2; public void Execute(int i){int o=i*4; float land=tectonicBytes[o]/255f; if(land<0.5f){outBiome0[o]=outBiome0[o+1]=outBiome0[o+2]=outBiome0[o+3]=0; outBiome1[o]=outBiome1[o+1]=outBiome1[o+2]=outBiome1[o+3]=0; outBiome2[o]=outBiome2[o+1]=outBiome2[o+2]=outBiome2[o+3]=0; return;} float h=tectonicBytes[o+1]/255f; float mtn=tectonicBytes[o+2]/255f; float t=climateBytes[o]/255f; float moist=climateBytes[o+1]/255f; float season=climateBytes[o+3]/255f; float river=hydrologyBytes[o]/255f,lake=hydrologyBytes[o+1]/255f,wet=hydrologyBytes[o+2]/255f; float em=math.saturate(moist + river*0.12f + lake*0.2f + wet*0.35f); float province=(math.sin((i+seed)*0.002f))*biomeProvinceStrength; float jungle = math.saturate((t-0.55f)*2f)*math.saturate((em-0.65f)*2f)*(1f-season*0.5f); float desert = math.saturate((1f-em)*1.6f)*math.saturate((t-0.45f)*1.8f)*(1f-wet); float savanna = math.saturate((t-0.5f)*1.8f)*(1f-math.abs(em-0.5f)*2f)*math.saturate(season+0.2f); float tempGrass=(1f-math.abs(t-0.52f)*2f)*(1f-math.abs(em-0.45f)*2f); float tempForest=(1f-math.abs(t-0.5f)*2f)*math.saturate((em-0.45f)*1.8f)*(1f-season*0.35f); float taiga=(1f-math.abs(t-0.35f)*3f)*math.saturate((em-0.35f)*1.4f); float tundra=math.saturate((0.4f-t)*3f)*math.saturate((0.75f-em)*1.3f); float snowIce=math.saturate((0.32f-t)*3f + h*0.3f + mtn*0.25f + iceCapSize*0.4f); float polar=math.saturate((0.25f-t)*4f)*math.saturate(snowIce+0.2f); float marsh=wet*math.saturate(em)*math.saturate(1f-snowIce); float alpine=math.saturate((mtn*0.7f + h*0.5f) - em*0.25f); jungle*=1f+province*0.15f; desert*=1f-province*0.1f; tempForest*=1f+province*0.1f; tempGrass*=1f-province*0.08f; float sh=biomeCompetitionSharpness; jungle=math.pow(math.max(0,jungle),sh); desert=math.pow(math.max(0,desert),sh); savanna=math.pow(math.max(0,savanna),sh); tempGrass=math.pow(math.max(0,tempGrass),sh); tempForest=math.pow(math.max(0,tempForest),sh); taiga=math.pow(math.max(0,taiga),sh); tundra=math.pow(math.max(0,tundra),sh); polar=math.pow(math.max(0,polar),sh); marsh=math.pow(math.max(0,marsh),sh); float sum=jungle+desert+savanna+tempGrass+tempForest+taiga+tundra+polar+marsh+1e-5f; outBiome0[o]=(byte)(jungle/sum*255f); outBiome0[o+1]=(byte)(desert/sum*255f); outBiome0[o+2]=(byte)(savanna/sum*255f); outBiome0[o+3]=(byte)(tempGrass/sum*255f); outBiome1[o]=(byte)(tempForest/sum*255f); outBiome1[o+1]=(byte)(taiga/sum*255f); outBiome1[o+2]=(byte)(tundra/sum*255f); outBiome1[o+3]=(byte)(polar/sum*255f); outBiome2[o]=(byte)(marsh/sum*255f); outBiome2[o+1]=(byte)(math.saturate(snowIce)*255f); outBiome2[o+2]=(byte)(math.saturate(alpine)*255f); outBiome2[o+3]=0; } }
}
