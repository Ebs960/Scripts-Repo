using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public class MenuPlanetPreviewTectonicGenerator : MonoBehaviour
{
    [SerializeField] private int tectonicMapWidth = 1024;
    [SerializeField] private int tectonicMapHeight = 512;
    [SerializeField] private float regenerationDelay = 0.15f;
    [SerializeField] private bool logGenerationTiming = false;

    [Header("Tectonic Plate Layout")]
    [SerializeField, Range(4, 32)] private int plateCount = 14;
    [SerializeField, Range(0.005f, 0.25f)] private float plateBoundaryWidth = 0.06f;
    [SerializeField, Range(0f, 1f)] private float continentalPlateFraction = 0.55f;
    [SerializeField, Range(0f, 1f)] private float plateShapeNoiseStrength = 0.28f;

    [Header("Plate Boundary Shape")]
    [SerializeField, Range(0.25f, 8f)] private float plateBoundaryWarpScale = 2.2f;
    [SerializeField, Range(0f, 0.25f)] private float plateBoundaryWarpAmplitude = 0.08f;

    [Header("Land Coverage")]
    [SerializeField, Range(0f, 1f)] private float minimumSeaLevel = 0.30f;
    [SerializeField, Range(0f, 1f)] private float maximumSeaLevel = 0.78f;
    [SerializeField] private bool logLandCoverageDiagnostics = false;

    [Header("Continental Shape")]
    [SerializeField, Range(0.25f, 8f)] private float continentalDetailScale = 1.65f;
    [SerializeField, Range(0f, 1f)] private float continentalDetailStrength = 0.22f;
    [SerializeField, Range(0f, 1f)] private float continentSeedStrength = 0.82f;
    [SerializeField, Range(0f, 0.5f)] private float plateContinentalInfluence = 0.16f;

    [Header("Elevation")]
    [SerializeField, Range(0f, 1f)] private float continentalElevationStrength = 0.42f;
    [SerializeField, Range(0f, 1f)] private float oceanBasinDepthStrength = 0.58f;
    [SerializeField, Range(0f, 1f)] private float convergentMountainStrength = 0.72f;
    [SerializeField, Range(0f, 1f)] private float divergentRidgeStrength = 0.18f;
    [SerializeField, Range(0f, 1f)] private float terrainDetailNoiseStrength = 0.14f;

    [Header("Coasts")]
    [SerializeField, Range(0f, 1f)] private float continentalShelfStrength = 0.75f;
    [SerializeField, Range(0.005f, 0.25f)] private float continentalShelfWidth = 0.08f;

    private struct PreviewTectonicPlateNative
    {
        public float3 centerDir;
        public float3 motionDir;
        public float continentalBias;
        public float baseElevationBias;
        public float ruggedness;
        public float age;
    }



    private struct PreviewContinentSeedNative
    {
        public float3 centerDir;
        public float radius;
        public float strength;
        public float elongation;
        public float3 stretchAxis;
    }
    [BurstCompile]
    private struct GenerateTectonicPreviewJob : IJobParallelFor
    {
        public int width;
        public int height;
        public float seed;
        public float landScale;
        public float landThreshold;
        public float elevation;
        public int landPreset;
        public float plateBoundaryWidth;
        public float plateShapeNoiseStrength;
        public float plateBoundaryWarpScale;
        public float plateBoundaryWarpAmplitude;
        public float minimumSeaLevel;
        public float maximumSeaLevel;
        public float continentalElevationStrength;
        public float oceanBasinDepthStrength;
        public float convergentMountainStrength;
        public float divergentRidgeStrength;
        public float terrainDetailNoiseStrength;
        public float continentalShelfStrength;
        public float continentalShelfWidth;
        public float continentalDetailScale;
        public float continentalDetailStrength;
        public float continentSeedStrength;
        public float plateContinentalInfluence;
        public float presetSeaLevelBias;
        public float islandFragmentStrength;

        [ReadOnly] public NativeArray<PreviewTectonicPlateNative> plates;
        [ReadOnly] public NativeArray<PreviewContinentSeedNative> continentSeeds;
        [NativeDisableParallelForRestriction] public NativeArray<byte> surfacePixels;
        [NativeDisableParallelForRestriction] public NativeArray<byte> boundaryPixels;
        [NativeDisableParallelForRestriction] public NativeArray<byte> crustPixels;

        public void Execute(int index)
        {
            int x = index % width;
            int y = index / width;
            float3 d = TexelToDir(x, y, width, height);

            int p0 = -1, p1 = -1;
            float best0 = -10f, best1 = -10f;
            for (int p = 0; p < plates.Length; p++)
            {
                float3 plateWarpOffset = new float3(seed * 0.013f + p * 11.71f, seed * 0.021f + p * 7.37f, seed * 0.017f + p * 19.19f);
                float smoothPlateWarp = Fbm(d * plateBoundaryWarpScale + plateWarpOffset) - 0.5f;
                float plateWarp = smoothPlateWarp * plateShapeNoiseStrength * plateBoundaryWarpAmplitude;
                float s = math.dot(d, plates[p].centerDir) + plateWarp;
                if (s > best0) { best1 = best0; p1 = p0; best0 = s; p0 = p; }
                else if (s > best1) { best1 = s; p1 = p; }
            }

            float delta = best0 - best1;
            float boundaryIntensity = 1f - math.saturate(delta / math.max(0.0001f, plateBoundaryWidth));
            PreviewTectonicPlateNative A = plates[p0];
            PreviewTectonicPlateNative B = plates[math.max(0, p1)];
            float3 bdir = math.normalizesafe(B.centerDir - A.centerDir, new float3(1, 0, 0));
            float along = math.dot(A.motionDir - B.motionDir, bdir);
            float convergent = math.saturate(-along * 0.9f) * boundaryIntensity;
            float divergent = math.saturate(along * 0.9f) * boundaryIntensity;
            float transform = math.saturate(1f - math.abs(along) * 1.3f) * boundaryIntensity;

            float plateBlend = math.saturate((best0 - best1) / math.max(0.0001f, plateBoundaryWidth * 2f));
            float plateContinentalBias = math.lerp(B.continentalBias, A.continentalBias, plateBlend);
            float continentalDetailNoise = Fbm(d * continentalDetailScale + new float3(seed + 3.1f, seed * 0.71f, seed * 1.31f));
            float continentSeedField = 0f;
            for (int i = 0; i < continentSeeds.Length; i++)
            {
                PreviewContinentSeedNative c = continentSeeds[i];
                float similarity = math.dot(d, c.centerDir);
                float axisAlign = math.dot(d, c.stretchAxis);
                float stretch = math.lerp(1f, 1f + c.elongation * math.abs(axisAlign), 0.5f);
                float distanceLike = (1f - similarity) / math.max(0.1f, stretch);
                float rawInfluence = 1f - math.saturate(distanceLike / math.max(0.0001f, c.radius));
                float influence = rawInfluence * rawInfluence * (3f - 2f * rawInfluence);
                continentSeedField = math.max(continentSeedField, influence * c.strength);
            }
            float continentalPotential = continentSeedField * continentSeedStrength + (continentalDetailNoise - 0.5f) * (continentalDetailStrength + islandFragmentStrength) + (plateContinentalBias - 0.5f) * plateContinentalInfluence;
            continentalPotential += convergent * 0.03f;
            continentalPotential -= divergent * 0.04f;
            float continental = math.saturate(continentalPotential);
            float oceanic = 1f - continental;
            float shelfProximity = math.saturate((continental - oceanic + continentalShelfWidth * 2f) / math.max(0.01f, continentalShelfWidth * 3f));
            float basinDepth = oceanic * (0.45f + 0.55f * Fbm(d * (landScale * 2.1f) + new float3(seed + 44.1f, seed + 12.2f, seed + 8.3f))) * oceanBasinDepthStrength;
            float mountainBelt = math.saturate(convergent * (0.5f + continental * 0.9f) + transform * 0.08f) * (0.6f + 0.4f * A.ruggedness);
            float baseElevation = continental * continentalElevationStrength - basinDepth + divergent * divergentRidgeStrength * oceanic + mountainBelt * convergentMountainStrength;
            baseElevation += (Fbm(d * (landScale * 3.6f) + new float3(seed + 96.2f, seed + 51.8f, seed + 14.4f)) - 0.5f) * terrainDetailNoiseStrength;
            baseElevation += (elevation - 0.5f) * 0.28f;
            float seaLevel = math.saturate(math.lerp(minimumSeaLevel, maximumSeaLevel, landThreshold) + presetSeaLevelBias);
            float landMask = SmoothThreshold(seaLevel - 0.035f, seaLevel + 0.035f, baseElevation + 0.5f);
            float shelfMask = oceanic * (1f - landMask) * shelfProximity * continentalShelfStrength * math.saturate(1f - basinDepth * 1.35f);
            float elevationHeight = math.saturate(baseElevation * 0.8f + 0.5f);

            int o = index * 4;
            surfacePixels[o + 0] = ToByte(landMask);
            surfacePixels[o + 1] = ToByte(elevationHeight);
            surfacePixels[o + 2] = ToByte(math.saturate(mountainBelt));
            surfacePixels[o + 3] = ToByte(math.saturate(shelfMask));

            boundaryPixels[o + 0] = ToByte((float)p0 / math.max(1f, plates.Length - 1f));
            boundaryPixels[o + 1] = ToByte(boundaryIntensity);
            boundaryPixels[o + 2] = ToByte(convergent);
            boundaryPixels[o + 3] = ToByte(divergent);

            crustPixels[o + 0] = ToByte(continental);
            crustPixels[o + 1] = ToByte(oceanic);
            crustPixels[o + 2] = ToByte(math.saturate(basinDepth));
            crustPixels[o + 3] = ToByte(math.saturate(continentalPotential));
        }

        private static float3 TexelToDir(int x, int y, int width, int height)
        {
            float u = (x + 0.5f) / width;
            float v = (y + 0.5f) / height;
            float lon = (u - 0.5f) * math.PI * 2f;
            float lat = (v - 0.5f) * math.PI;
            float cl = math.cos(lat);
            return new float3(math.cos(lon) * cl, math.sin(lat), math.sin(lon) * cl);
        }

        private static float SmoothThreshold(float edge0, float edge1, float x)
        {
            float t = math.saturate((x - edge0) / math.max(1e-6f, edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        private static float Fbm(float3 p)
        {
            float v = 0f, a = 0.5f, f = 1f;
            for (int i = 0; i < 4; i++)
            {
                float n = noise.snoise(new float3(p.x * f + p.y * 0.67f, p.z * f + p.y * 0.37f, p.z * 0.23f + p.x * 0.11f));
                v += a * (n * 0.5f + 0.5f);
                f *= 2f;
                a *= 0.5f;
            }
            return v;
        }

                private static byte ToByte(float v) => (byte)math.clamp(math.round(math.saturate(v) * 255f), 0f, 255f);
    }

    private Texture2D surfaceStructureTexture, plateBoundaryTexture, crustBasinTexture;
    private float scheduledAt = -1f, seed, landScale, landThreshold, elevation;
    private int landPreset;
    private JobHandle activeGenerationHandle;
    private bool generationInProgress;
    private bool uploadPending;
    private bool regenerationRequestedWhileBusy;
    private NativeArray<PreviewTectonicPlateNative> nativePlates;
    private NativeArray<PreviewContinentSeedNative> nativeContinentSeeds;
    private float pendingSeaLevel;
    private int pendingContinentSeedCount;
    private float pendingPresetSeaLevelBias;
    private NativeArray<byte> surfacePixelBytes;
    private NativeArray<byte> boundaryPixelBytes;
    private NativeArray<byte> crustPixelBytes;
    private long generationStartTicks;
    private bool pendingWasImmediate;
    private int pendingPlateCount;
    public event System.Action TectonicTexturesReady;

    public Texture2D SurfaceStructureTexture => surfaceStructureTexture;
    public Texture2D PlateBoundaryTexture => plateBoundaryTexture;
    public Texture2D CrustBasinTexture => crustBasinTexture;

    public void SetInputs(float inSeed, float inLandScale, float inLandThreshold, float inElevation, int landPresetOrEquivalentIfAvailable)
    { seed = inSeed; landScale = inLandScale; landThreshold = inLandThreshold; elevation = inElevation; landPreset = landPresetOrEquivalentIfAvailable; }

    public void ScheduleRegeneration()
    {
        if (generationInProgress) { regenerationRequestedWhileBusy = true; return; }
        scheduledAt = Time.time + regenerationDelay;
    }

    private void Update()
    {
        if (generationInProgress && activeGenerationHandle.IsCompleted)
        {
            activeGenerationHandle.Complete();
            generationInProgress = false;
            if (uploadPending)
            {
                UploadGeneratedTextures();
                uploadPending = false;
            }
            DisposeNativeBuffers();
            if (regenerationRequestedWhileBusy)
            {
                regenerationRequestedWhileBusy = false;
                scheduledAt = Time.time + regenerationDelay;
            }
        }

        if (!generationInProgress && scheduledAt > 0f && Time.time >= scheduledAt)
            StartGenerationAsync(false);
    }

    public void Release()
    {
        if (generationInProgress)
        {
            activeGenerationHandle.Complete();
            generationInProgress = false;
        }

        DisposeNativeBuffers();

        if (surfaceStructureTexture != null) Destroy(surfaceStructureTexture);
        if (plateBoundaryTexture != null) Destroy(plateBoundaryTexture);
        if (crustBasinTexture != null) Destroy(crustBasinTexture);
        surfaceStructureTexture = null;
        plateBoundaryTexture = null;
        crustBasinTexture = null;
    }

    public void GenerateNow()
    {
        if (generationInProgress) { regenerationRequestedWhileBusy = true; return; }
        StartGenerationAsync(true);
        if (generationInProgress)
        {
            activeGenerationHandle.Complete();
            generationInProgress = false;
            if (uploadPending) UploadGeneratedTextures();
            uploadPending = false;
            DisposeNativeBuffers();
        }

    }

    private void StartGenerationAsync(bool immediate)
    {
        EnsureTextures();
        DisposeNativeBuffers();
        var managedPlates = BuildPlates();
        GetLandPresetShapeParameters(landPreset, out int seedCount, out float minSeedRadius, out float maxSeedRadius, out float baseSeedStrength, out float seaLevelBias, out float islandFragment);
        var managedContinentSeeds = BuildContinentSeeds(seedCount, minSeedRadius, maxSeedRadius, baseSeedStrength);
        pendingPlateCount = managedPlates.Length;
        pendingContinentSeedCount = managedContinentSeeds.Length;
        pendingPresetSeaLevelBias = seaLevelBias;
        pendingSeaLevel = Mathf.Clamp01(Mathf.Lerp(minimumSeaLevel, maximumSeaLevel, landThreshold) + seaLevelBias);
        AllocateNativeBuffers(managedPlates, managedContinentSeeds);

        var job = new GenerateTectonicPreviewJob
        {
            width = tectonicMapWidth,
            height = tectonicMapHeight,
            seed = seed,
            landScale = landScale,
            landThreshold = landThreshold,
            elevation = elevation,
            landPreset = landPreset,
            plateBoundaryWidth = plateBoundaryWidth,
            plateShapeNoiseStrength = plateShapeNoiseStrength,
            continentalElevationStrength = continentalElevationStrength,
            oceanBasinDepthStrength = oceanBasinDepthStrength,
            convergentMountainStrength = convergentMountainStrength,
            divergentRidgeStrength = divergentRidgeStrength,
            terrainDetailNoiseStrength = terrainDetailNoiseStrength,
            plateBoundaryWarpScale = plateBoundaryWarpScale,
            plateBoundaryWarpAmplitude = plateBoundaryWarpAmplitude,
            minimumSeaLevel = minimumSeaLevel,
            maximumSeaLevel = maximumSeaLevel,
            continentalShelfStrength = continentalShelfStrength,
            continentalShelfWidth = continentalShelfWidth,
            continentalDetailScale = continentalDetailScale,
            continentalDetailStrength = continentalDetailStrength,
            continentSeedStrength = continentSeedStrength,
            plateContinentalInfluence = plateContinentalInfluence,
            presetSeaLevelBias = seaLevelBias,
            islandFragmentStrength = islandFragment,
            plates = nativePlates,
            continentSeeds = nativeContinentSeeds,
            surfacePixels = surfacePixelBytes,
            boundaryPixels = boundaryPixelBytes,
            crustPixels = crustPixelBytes
        };

        generationStartTicks = Stopwatch.GetTimestamp();
        pendingWasImmediate = immediate;
        activeGenerationHandle = job.Schedule(tectonicMapWidth * tectonicMapHeight, 64);
        generationInProgress = true;
        uploadPending = true;
        scheduledAt = -1f;
    }

    private void UploadGeneratedTextures()
    {
        long beforeUpload = Stopwatch.GetTimestamp();
        surfaceStructureTexture.SetPixelData(surfacePixelBytes, 0);
        surfaceStructureTexture.Apply(false, false);
        plateBoundaryTexture.SetPixelData(boundaryPixelBytes, 0);
        plateBoundaryTexture.Apply(false, false);
        crustBasinTexture.SetPixelData(crustPixelBytes, 0);
        crustBasinTexture.Apply(false, false);

        if (logLandCoverageDiagnostics)
        {
            int landCount = 0;
            for (int i = 0; i < surfacePixelBytes.Length; i += 4)
            {
                if (surfacePixelBytes[i] > 127) landCount++;
            }
            float landPct = (float)landCount / (tectonicMapWidth * tectonicMapHeight) * 100f;
            string[] presetNames = { "Archipelago", "Islands", "Standard", "Large Continents", "Pangaea", "Terrestrial" };
            string presetName = landPreset >= 0 && landPreset < presetNames.Length ? presetNames[landPreset] : $"Preset {landPreset}";
            UnityEngine.Debug.Log($"[Tectonic Preview] Preset={presetName} | Seeds={pendingContinentSeedCount} | SeaLevel={pendingSeaLevel:F3} | LandCoverage={landPct:F1}%");
        }

        if (logGenerationTiming)
        {
            float totalMs = TicksToMs(Stopwatch.GetTimestamp() - generationStartTicks);
            float uploadMs = TicksToMs(Stopwatch.GetTimestamp() - beforeUpload);
            string mode = pendingWasImmediate ? "immediate" : "scheduled";
            UnityEngine.Debug.Log($"[Tectonic Preview] Burst generation complete ({mode}) | {tectonicMapWidth}x{tectonicMapHeight} | plates={pendingPlateCount} | total={totalMs:F1}ms | upload={uploadMs:F1}ms");
        }

        TectonicTexturesReady?.Invoke();
    }

    private static float TicksToMs(long ticks) => ticks * 1000f / Stopwatch.Frequency;

    private void AllocateNativeBuffers(PreviewTectonicPlateNative[] managedPlates, PreviewContinentSeedNative[] managedContinentSeeds)
    {
        nativePlates = new NativeArray<PreviewTectonicPlateNative>(managedPlates.Length, Allocator.Persistent);
        for (int i = 0; i < managedPlates.Length; i++) nativePlates[i] = managedPlates[i];
        nativeContinentSeeds = new NativeArray<PreviewContinentSeedNative>(managedContinentSeeds.Length, Allocator.Persistent);
        for (int i = 0; i < managedContinentSeeds.Length; i++) nativeContinentSeeds[i] = managedContinentSeeds[i];
        int pixelBytes = tectonicMapWidth * tectonicMapHeight * 4;
        surfacePixelBytes = new NativeArray<byte>(pixelBytes, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        boundaryPixelBytes = new NativeArray<byte>(pixelBytes, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        crustPixelBytes = new NativeArray<byte>(pixelBytes, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
    }

    private void DisposeNativeBuffers()
    {
        if (nativePlates.IsCreated) nativePlates.Dispose();
        if (nativeContinentSeeds.IsCreated) nativeContinentSeeds.Dispose();
        if (surfacePixelBytes.IsCreated) surfacePixelBytes.Dispose();
        if (boundaryPixelBytes.IsCreated) boundaryPixelBytes.Dispose();
        if (crustPixelBytes.IsCreated) crustPixelBytes.Dispose();
    }

    private void EnsureTextures()
    {
        surfaceStructureTexture = EnsureTex(surfaceStructureTexture, "MenuTectonicSurface");
        plateBoundaryTexture = EnsureTex(plateBoundaryTexture, "MenuTectonicBoundary");
        crustBasinTexture = EnsureTex(crustBasinTexture, "MenuTectonicCrust");
    }

    private Texture2D EnsureTex(Texture2D t, string n)
    {
        if (t != null && t.width == tectonicMapWidth && t.height == tectonicMapHeight) return t;
        if (t != null) Destroy(t);
        return new Texture2D(tectonicMapWidth, tectonicMapHeight, TextureFormat.RGBA32, false, true)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            name = n
        };
    }

    private PreviewTectonicPlateNative[] BuildPlates()
    {
        var p = new PreviewTectonicPlateNative[Mathf.Clamp(plateCount, 4, 32)];
        int continentalTarget = Mathf.RoundToInt(p.Length * continentalPlateFraction);
        for (int i = 0; i < p.Length; i++)
        {
            Vector3 c = RandomOnSphere(i * 31 + 7);
            Vector3 tangent = Vector3.Cross(c, RandomOnSphere(i * 17 + 3)).normalized;
            if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.Cross(c, Vector3.up).normalized;
            float cont = i < continentalTarget ? Mathf.Lerp(0.58f, 0.95f, Hash01(i, 99)) : Mathf.Lerp(0.05f, 0.45f, Hash01(i, 77));
            p[i] = new PreviewTectonicPlateNative
            {
                centerDir = c,
                motionDir = tangent,
                continentalBias = cont,
                baseElevationBias = Mathf.Lerp(-0.15f, 0.25f, Hash01(i, 41)),
                ruggedness = Mathf.Lerp(0.35f, 1f, Hash01(i, 57)),
                age = Hash01(i, 13)
            };
        }
        return p;
    }



    private void GetLandPresetShapeParameters(int preset, out int continentSeedCount, out float minSeedRadius, out float maxSeedRadius, out float baseSeedStrength, out float seaLevelBias, out float islandFragmentStrength)
    {
        switch (preset)
        {
            case 0: continentSeedCount = 14; minSeedRadius = 0.08f; maxSeedRadius = 0.16f; baseSeedStrength = 0.62f; seaLevelBias = 0.10f; islandFragmentStrength = 0.10f; break;
            case 1: continentSeedCount = 8; minSeedRadius = 0.14f; maxSeedRadius = 0.24f; baseSeedStrength = 0.68f; seaLevelBias = 0.06f; islandFragmentStrength = 0.06f; break;
            case 3: continentSeedCount = 4; minSeedRadius = 0.24f; maxSeedRadius = 0.34f; baseSeedStrength = 0.82f; seaLevelBias = -0.04f; islandFragmentStrength = 0.02f; break;
            case 4: continentSeedCount = 3; minSeedRadius = 0.28f; maxSeedRadius = 0.5f; baseSeedStrength = 0.9f; seaLevelBias = -0.08f; islandFragmentStrength = 0.01f; break;
            case 5: continentSeedCount = 6; minSeedRadius = 0.26f; maxSeedRadius = 0.40f; baseSeedStrength = 0.92f; seaLevelBias = -0.14f; islandFragmentStrength = 0.0f; break;
            default: continentSeedCount = 5; minSeedRadius = 0.18f; maxSeedRadius = 0.30f; baseSeedStrength = 0.76f; seaLevelBias = 0f; islandFragmentStrength = 0.04f; break;
        }
    }

    private PreviewContinentSeedNative[] BuildContinentSeeds(int count, float minRadius, float maxRadius, float baseStrength)
    {
        var seeds = new PreviewContinentSeedNative[Mathf.Max(1, count)];
        for (int i = 0; i < seeds.Length; i++)
        {
            Vector3 c = RandomOnSphere(i * 131 + 29);
            Vector3 axis = Vector3.Cross(c, RandomOnSphere(i * 53 + 17)).normalized;
            if (axis.sqrMagnitude < 0.001f) axis = Vector3.Cross(c, Vector3.up).normalized;
            seeds[i] = new PreviewContinentSeedNative
            {
                centerDir = c,
                radius = Mathf.Lerp(minRadius, maxRadius, Hash01(i, 203)),
                strength = Mathf.Lerp(baseStrength * 0.75f, Mathf.Min(1f, baseStrength * 1.1f), Hash01(i, 211)),
                elongation = Mathf.Lerp(0.05f, 0.55f, Hash01(i, 223)),
                stretchAxis = axis
            };
        }
        return seeds;
    }
    private Vector3 RandomOnSphere(int salt)
    {
        float u = Hash01(salt, 1);
        float v = Hash01(salt, 2);
        float lon = u * Mathf.PI * 2f;
        float z = 2f * v - 1f;
        float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
        return new Vector3(r * Mathf.Cos(lon), z, r * Mathf.Sin(lon));
    }

    private float Hash01(int x, int y)
    {
        uint s = (uint)Mathf.RoundToInt(seed * 1000f) ^ (uint)(landPreset * 193);
        uint n = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ s;
        n = (n << 13) ^ n;
        return Mathf.Clamp01((1f - ((n * (n * n * 15731u + 789221u) + 1376312589u) & 0x7fffffffu) / 1073741824f) * 0.5f + 0.5f);
    }
}
