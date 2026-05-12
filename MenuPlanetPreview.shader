Shader "Custom/MenuPlanetPreview"
{
    Properties
    {
        _LandScale("Land Scale", Range(0.5, 5.0)) = 2.0
        _LandThreshold("Land Threshold", Range(0, 1)) = 0.4
        _Temperature("Temperature", Range(0, 1)) = 0.5
        _Moisture("Moisture", Range(0, 1)) = 0.5
        _WaterwayAmount("Waterway Amount", Range(0,1)) = 0.55
        _Elevation("Elevation", Range(0, 1)) = 0.3
            _DesertFactor("Desert Factor", Range(0,1)) = 0.0
            _TropicalFactor("Tropical Factor", Range(0,1)) = 0.0
            _SnowFactor("Snow Factor", Range(0,1)) = 0.0
        [Header(Ocean Color)]
            _OceanColor("Ocean Base Color", Color) = (0.06, 0.22, 0.45, 1)
        [Header(Biome Tuning)]
            _IceCapSize("Ice Cap Size", Range(0, 1)) = 0.5
            _BiomeBlend("Biome Blend", Range(0, 1.0)) = 0.03
            _BiomeNoiseScale("Biome Noise Scale", Range(0, 10)) = 3.0
            _BiomeNoiseStrength("Biome Noise Strength", Range(0, 0.2)) = 0.08
        [Header(Seed)]
            _Seed("Planet Seed", Float) = 0.0
            _DetailScale("Detail Scale", Float) = 18.0
            _DetailStrength("Detail Strength", Range(0,1)) = 0.18
            // _AtmosphereColor, _AtmospherePower, _AtmosphereRadius kept in CBUFFER
            // for SRP Batcher layout but no longer exposed — atmosphere is handled by
            // the separate MenuPlanetAtmosphere shell shader.
            [HideInInspector] _AtmosphereColor("Atmosphere Color", Color) = (0.62,0.78,0.95,1)
            [HideInInspector] _AtmospherePower("Atmosphere Power", Range(0.5,6)) = 3.5
            [HideInInspector] _AtmosphereRadius("Atmosphere Radius", Float) = 1.0
            _MapStyle("Map Style", Range(0, 1)) = 0.0
        [Header(Displacement)]
            _DisplacementScale("Displacement Scale", Range(0, 0.15)) = 0.004
            _LandUpliftStrength("Land Uplift Strength", Range(0,1)) = 0.06
            _HillDisplacementStrength("Hill Displacement Strength", Range(0,1)) = 0.08
            _MountainDisplacementStrength("Mountain Displacement Strength", Range(0,1)) = 0.18
            _IceDisplacementStrength("Ice Displacement Strength", Range(0,1)) = 0.04
            _VolcanicDisplacementStrength("Volcanic Displacement Strength", Range(0,1)) = 0.12
            _OceanDepthStrength("Ocean Depth Strength", Range(0,1)) = 0.01
            _ShowElevationOnly("Show Elevation Only", Float) = 0
            _ShowMountainMaskOnly("Show Mountain Mask Only", Float) = 0
            _ShowDisplacementHeightOnly("Show Displacement Height Only", Float) = 0
            _UseDisplacedNormals("Use Displaced Normals", Float) = 0
        [Header(Surface)]
            _Smoothness("Smoothness", Range(0, 1)) = 0.3
            _Metallic("Metallic", Range(0, 1)) = 0.0
            _AmbientOcclusion("Ambient Occlusion", Range(0, 1)) = 1.0
            _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.12
            _Brightness("Brightness", Range(0.5, 3.0)) = 1.12
            _MountainDetailTex("Mountain Detail", 2D) = "gray" {}
            _IceDetailTex("Ice Detail", 2D) = "gray" {}
            _OceanDetailTex("Ocean Detail", 2D) = "gray" {}
            _OceanNormalTex("Ocean Normal", 2D) = "bump" {}
            _MountainNormalTex("Mountain Normal", 2D) = "bump" {}
            _IceNormalTex("Ice Normal", 2D) = "bump" {}
            _OceanSmoothnessTex("Ocean Smoothness", 2D) = "gray" {}
            _IceSmoothnessTex("Ice Smoothness", 2D) = "gray" {}
            _MarshSmoothnessTex("Marsh/Wetland Smoothness", 2D) = "gray" {}
            _VolcanicSmoothnessTex("Volcanic/Lava Smoothness", 2D) = "gray" {}
            _VolcanicRockTex("Volcanic Rock", 2D) = "gray" {}
            _LavaCrackTex("Lava Crack", 2D) = "gray" {}
            _LavaEmissiveTex("Lava Emissive", 2D) = "white" {}
            _AshDetailTex("Ash Detail", 2D) = "gray" {}
            _MountainDetailStrength("Mountain Detail Strength", Range(0,1)) = 0.14
            _IceDetailStrength("Ice Detail Strength", Range(0,1)) = 0.12
            _OceanDetailStrength("Ocean Detail Strength", Range(0,1)) = 0.15
            _OceanNormalStrength("Ocean Normal Strength", Range(0,1)) = 0.1
            _MountainNormalStrength("Mountain Normal Strength", Range(0,1)) = 0.18
            _IceNormalStrength("Ice Normal Strength", Range(0,1)) = 0.22
            _TextureDetailScale("Texture Detail Scale", Range(0.1,30)) = 8
            _UseDetailTextures("Use Detail Textures", Float) = 0
            _UseTextureDrivenBiomes("Use Texture Driven Biomes", Float) = 1
            _JungleAlbedoTex("Jungle Albedo", 2D) = "gray" {}
            _DesertAlbedoTex("Desert Albedo", 2D) = "gray" {}
            _SavannaAlbedoTex("Savanna Albedo", 2D) = "gray" {}
            _TemperateGrassAlbedoTex("Temperate Grass Albedo", 2D) = "gray" {}
            _TemperateForestAlbedoTex("Temperate Forest Albedo", 2D) = "gray" {}
            _TundraAlbedoTex("Tundra Albedo", 2D) = "gray" {}
            _PolarAlbedoTex("Polar Albedo", 2D) = "gray" {}
            _MarshAlbedoTex("Marsh Albedo", 2D) = "gray" {}
            _JungleNormalTex("Jungle Normal", 2D) = "bump" {}
            _DesertNormalTex("Desert Normal", 2D) = "bump" {}
            _SavannaNormalTex("Savanna Normal", 2D) = "bump" {}
            _TemperateGrassNormalTex("Temperate Grass Normal", 2D) = "bump" {}
            _TemperateForestNormalTex("Temperate Forest Normal", 2D) = "bump" {}
            _TundraNormalTex("Tundra Normal", 2D) = "bump" {}
            _PolarNormalTex("Polar Normal", 2D) = "bump" {}
            _MarshNormalTex("Marsh Normal", 2D) = "bump" {}
            _BiomeTextureStrength("Biome Texture Strength", Range(0,1)) = 0.75
            _BiomeTintStrength("Biome Tint Strength", Range(0,1)) = 0.12
            _BiomeNormalStrength("Biome Normal Strength", Range(0,1)) = 0.18
            _BiomeTextureScale("Biome Texture Scale", Range(0.1,30)) = 6
            _BiomeTextureContrast("Biome Texture Contrast", Range(0,1)) = 0.18
            _ShowBiomeWeightsOnly("Show Biome Weights Only", Float) = 0
            _ShowBiomeTextureOnly("Show Biome Texture Only", Float) = 0
            _ShowSmoothnessOnly("Show Smoothness Only", Float) = 0
            _ShowLocalMoistureOnly("Show Local Moisture Only", Float) = 0
            _ShowWaterwaysOnly("Show Waterways Only", Float) = 0
            _ShowWaterwayAmountOnly("Show Waterway Amount Only", Float) = 0
            _TerminatorSoftness("Terminator Softness", Range(0.05,1)) = 0.45
            _ShowLandMaskOnly("Show Land Mask Only", Float) = 0
            _ShowDetailTexturesOnly("Show Detail Textures Only", Float) = 0
            _ShowNormalsOnly("Show Normals Only", Float) = 0
            _VolcanicRockStrength("Volcanic Rock Strength", Range(0,1)) = 0.35
            _LavaCrackStrength("Lava Crack Strength", Range(0,1)) = 0.65
            _LavaEmissionStrength("Lava Emission Strength", Range(0,5)) = 2.2
            _LavaTextureScale("Lava Texture Scale", Range(0.1,30)) = 10
            _AshDetailStrength("Ash Detail Strength", Range(0,1)) = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        // =====================================================================
        //  Forward rendering pass — main color output
        // =====================================================================
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ UNITY_DOTS_INSTANCING_ENABLED

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            // SRP Batcher compatible CBUFFER
            CBUFFER_START(UnityPerMaterial)
                float _LandScale;
                float _LandThreshold;
                float _Temperature;
                float _Moisture;
                float _WaterwayAmount;
                float _Elevation;
                float _DesertFactor;
                float _TropicalFactor;
                float _SnowFactor;
                float4 _OceanColor;
                float _IceCapSize;
                float _BiomeBlend;
                float _BiomeNoiseScale;
                float _BiomeNoiseStrength;
                float _Seed;
                float _MapStyle;
                    float _DetailScale;
                    float _DetailStrength;
                    float4 _AtmosphereColor;
                    float _AtmospherePower;
                    float _AtmosphereRadius;
                float _DisplacementScale;
                float _LandUpliftStrength;
                float _HillDisplacementStrength;
                float _MountainDisplacementStrength;
                float _IceDisplacementStrength;
                float _VolcanicDisplacementStrength;
                float _OceanDepthStrength;
                float _ShowElevationOnly;
                float _ShowMountainMaskOnly;
                float _ShowDisplacementHeightOnly;
                float _UseDisplacedNormals;
                float _Smoothness;
                float _Metallic;
                float _AmbientOcclusion;
                float _AmbientStrength;
                float _Brightness;
                float _MountainDetailStrength;
                float _IceDetailStrength;
                float _OceanDetailStrength;
                float _OceanNormalStrength;
                float _MountainNormalStrength;
                float _IceNormalStrength;
                float _TextureDetailScale;
                float _UseDetailTextures;
                float _UseTextureDrivenBiomes;
                float _BiomeTextureStrength;
                float _BiomeTintStrength;
                float _BiomeNormalStrength;
                float _BiomeTextureScale;
                float _BiomeTextureContrast;
                float _ShowBiomeWeightsOnly;
                float _ShowBiomeTextureOnly;
                float _ShowSmoothnessOnly;
                float _ShowLocalMoistureOnly;
                float _ShowWaterwaysOnly;
                float _ShowWaterwayAmountOnly;
                float _TerminatorSoftness;
                float _ShowLandMaskOnly;
                float _ShowDetailTexturesOnly;
                float _ShowNormalsOnly;
                float _VolcanicRockStrength;
                float _LavaCrackStrength;
                float _LavaEmissionStrength;
                float _LavaTextureScale;
                float _AshDetailStrength;
            CBUFFER_END
            TEXTURE2D(_MountainDetailTex); SAMPLER(sampler_MountainDetailTex);
            TEXTURE2D(_IceDetailTex);
            TEXTURE2D(_OceanDetailTex);
            TEXTURE2D(_OceanNormalTex);
            TEXTURE2D(_MountainNormalTex);
            TEXTURE2D(_IceNormalTex);
            TEXTURE2D(_OceanSmoothnessTex);
            TEXTURE2D(_IceSmoothnessTex);
            TEXTURE2D(_MarshSmoothnessTex);
            TEXTURE2D(_VolcanicSmoothnessTex);
            TEXTURE2D(_JungleAlbedoTex);
            TEXTURE2D(_DesertAlbedoTex);
            TEXTURE2D(_SavannaAlbedoTex);
            TEXTURE2D(_TemperateGrassAlbedoTex);
            TEXTURE2D(_TemperateForestAlbedoTex);
            TEXTURE2D(_TundraAlbedoTex);
            TEXTURE2D(_PolarAlbedoTex);
            TEXTURE2D(_MarshAlbedoTex);
            TEXTURE2D(_JungleNormalTex);
            TEXTURE2D(_DesertNormalTex);
            TEXTURE2D(_SavannaNormalTex);
            TEXTURE2D(_TemperateGrassNormalTex);
            TEXTURE2D(_TemperateForestNormalTex);
            TEXTURE2D(_TundraNormalTex);
            TEXTURE2D(_PolarNormalTex);
            TEXTURE2D(_MarshNormalTex);
            TEXTURE2D(_VolcanicRockTex);
            TEXTURE2D(_LavaCrackTex);
            TEXTURE2D(_LavaEmissiveTex);
            TEXTURE2D(_AshDetailTex);

            float3 SampleTriplanar(TEXTURE2D_PARAM(tex, samp), float3 positionOS, float3 objNorm)
            {
                float3 n = abs(normalize(objNorm));
                n = pow(n, 4.0);
                n /= (n.x + n.y + n.z + 1e-5);
                float2 uvX = positionOS.yz * _TextureDetailScale;
                float2 uvY = positionOS.xz * _TextureDetailScale;
                float2 uvZ = positionOS.xy * _TextureDetailScale;
                float3 sx = SAMPLE_TEXTURE2D(tex, samp, uvX).rgb;
                float3 sy = SAMPLE_TEXTURE2D(tex, samp, uvY).rgb;
                float3 sz = SAMPLE_TEXTURE2D(tex, samp, uvZ).rgb;
                return sx * n.x + sy * n.y + sz * n.z;
            }

                        float3 SampleTriplanarScaled(TEXTURE2D_PARAM(tex, samp), float3 positionOS, float3 objNorm, float scale)
            {
                float3 n = abs(normalize(objNorm));
                n = pow(n, 4.0);
                n /= (n.x + n.y + n.z + 1e-5);
                float2 uvX = positionOS.yz * scale;
                float2 uvY = positionOS.xz * scale;
                float2 uvZ = positionOS.xy * scale;
                float3 sx = SAMPLE_TEXTURE2D(tex, samp, uvX).rgb;
                float3 sy = SAMPLE_TEXTURE2D(tex, samp, uvY).rgb;
                float3 sz = SAMPLE_TEXTURE2D(tex, samp, uvZ).rgb;
                return sx * n.x + sy * n.y + sz * n.z;
            }

            // -----------------------------------------------------------------
            //  Structs
            // -----------------------------------------------------------------
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // -----------------------------------------------------------------
            //  Noise — value noise with smooth interpolation (3D)
            // -----------------------------------------------------------------
            float hash31(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float noise3D(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f); // smoothstep

                float n000 = hash31(i + float3(0, 0, 0));
                float n100 = hash31(i + float3(1, 0, 0));
                float n010 = hash31(i + float3(0, 1, 0));
                float n110 = hash31(i + float3(1, 1, 0));
                float n001 = hash31(i + float3(0, 0, 1));
                float n101 = hash31(i + float3(1, 0, 1));
                float n011 = hash31(i + float3(0, 1, 1));
                float n111 = hash31(i + float3(1, 1, 1));

                return lerp(
                    lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                    lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y),
                    f.z
                );
            }

            // Fractal Brownian Motion — 4 octaves gives nice organic blobs
            float fbm(float3 p)
            {
                float value = 0.0;
                float amp   = 0.5;
                float freq  = 1.0;
                for (int i = 0; i < 4; i++)
                {
                    value += amp * noise3D(p * freq);
                    freq  *= 2.0;
                    amp   *= 0.5;
                }
                return value;
            }

            float GetWarpedLandValue(float3 objNorm, float3 seedOff)
            {
                float3 broadP = objNorm * _LandScale;
                float3 warpP = objNorm * max(0.4, _LandScale * 0.55);
                float3 warp = float3(
                    fbm(warpP + seedOff + float3(15.1, 42.2, 73.3)),
                    fbm(warpP + seedOff + float3(66.4, 24.8, 11.5)),
                    fbm(warpP + seedOff + float3(93.7, 57.9, 31.2))
                ) - 0.5;
                float3 warped = broadP + warp * lerp(0.25, 0.55, saturate(_LandScale / 5.0));
                float baseLand = fbm(warped + float3(42.3, 17.1, 83.7) + seedOff);
                float coastNoise = fbm(warped * 3.75 + float3(9.4, 51.8, 27.6) + seedOff) - 0.5;
                float coastBand = 1.0 - smoothstep(0.03, 0.16, abs(baseLand - _LandThreshold));
                float coastPerturb = coastNoise * coastBand * 0.22;
                return baseLand + coastPerturb;
            }

            // -----------------------------------------------------------------
            //  Color helpers
            // -----------------------------------------------------------------
            float3 GetClimateGrade(float latitude, float localMoist, float style)
            {
                float3 cold = float3(0.92, 0.95, 1.0);
                float3 warm = float3(1.04, 0.98, 0.92);
                float3 humid = float3(0.97, 1.02, 0.97);
                float polar = smoothstep(0.58, 0.95, latitude);
                float arid = saturate((1.0 - localMoist) * 0.75);
                float infernal = saturate((style - 0.35) / 0.35);
                float3 grade = lerp(1.0.xxx, cold, polar * 0.35);
                grade = lerp(grade, warm, arid * 0.25);
                grade = lerp(grade, humid, localMoist * 0.2);
                grade = lerp(grade, float3(1.06, 0.95, 0.9), infernal * 0.4);
                return grade;
            }

            float3 GetOceanColor(float temp)
            {
                return _OceanColor.rgb;
            }
            struct SurfaceBiomeWeights { float jungle; float desert; float savanna; float temperateGrass; float temperateForest; float tundra; float polar; float marsh; };
            SurfaceBiomeWeights GetSurfaceBiomeWeights(float latitude, float temperatureLocal, float globalMoisture, float localMoisture, float terrainHeight, float capMask, float3 objNorm, float seed)
            {
                SurfaceBiomeWeights w = (SurfaceBiomeWeights)0;
                float hotness = saturate((temperatureLocal - 0.45) / 0.35);
                float temperateSuitability = saturate(1.0 - abs(temperatureLocal - 0.50) / 0.30);
                float coldness = saturate((0.45 - temperatureLocal) / 0.35);
                float polarSuitability = saturate(capMask);
                float wetness = localMoisture;
                float dryness = 1.0 - localMoisture;
                w.jungle = hotness * smoothstep(0.55, 0.85, wetness);
                w.desert = hotness * smoothstep(0.45, 0.80, dryness);
                w.savanna = hotness * smoothstep(0.25, 0.65, wetness) * smoothstep(0.25, 0.65, dryness);
                w.temperateForest = temperateSuitability * smoothstep(0.48, 0.82, wetness);
                w.temperateGrass = temperateSuitability * smoothstep(0.30, 0.75, dryness);
                w.tundra = coldness * smoothstep(0.40, 0.85, dryness) * (1.0 - polarSuitability);
                w.polar = polarSuitability;
                w.marsh = smoothstep(0.82, 0.96, wetness) * smoothstep(0.0, 0.35, terrainHeight) * (1.0 - polarSuitability);
                float3 seedOff = float3(seed, seed * 0.7, seed * 1.3);
                float junglePatch = fbm(objNorm * 7.0 + seedOff * 2.1);
                float desertPatch = fbm(objNorm * 6.0 + seedOff * 3.2);
                float forestPatch = fbm(objNorm * 8.0 + seedOff * 4.3);
                w.jungle *= lerp(0.65, 1.35, junglePatch);
                w.desert *= lerp(0.70, 1.30, desertPatch);
                w.temperateForest *= lerp(0.75, 1.25, forestPatch);
                float total = w.jungle + w.desert + w.savanna + w.temperateForest + w.temperateGrass + w.tundra + w.polar + w.marsh + 0.0001;
                w.jungle /= total; w.desert /= total; w.savanna /= total; w.temperateGrass /= total; w.temperateForest /= total; w.tundra /= total; w.polar /= total; w.marsh /= total;
                return w;
            }
            float3 GetTextureBiomeAlbedo(SurfaceBiomeWeights w, float3 climateGrade, float3 positionOS, float3 objNorm)
            {
                float3 ju = SampleTriplanarScaled(TEXTURE2D_ARGS(_JungleAlbedoTex, sampler_MountainDetailTex), positionOS, objNorm, _BiomeTextureScale);
                float3 de = SampleTriplanarScaled(TEXTURE2D_ARGS(_DesertAlbedoTex, sampler_MountainDetailTex), positionOS, objNorm, _BiomeTextureScale);
                float3 sa = SampleTriplanarScaled(TEXTURE2D_ARGS(_SavannaAlbedoTex, sampler_MountainDetailTex), positionOS, objNorm, _BiomeTextureScale);
                float3 tg = SampleTriplanarScaled(TEXTURE2D_ARGS(_TemperateGrassAlbedoTex, sampler_MountainDetailTex), positionOS, objNorm, _BiomeTextureScale);
                float3 tf = SampleTriplanarScaled(TEXTURE2D_ARGS(_TemperateForestAlbedoTex, sampler_MountainDetailTex), positionOS, objNorm, _BiomeTextureScale);
                float3 tu = SampleTriplanarScaled(TEXTURE2D_ARGS(_TundraAlbedoTex, sampler_MountainDetailTex), positionOS, objNorm, _BiomeTextureScale);
                float3 po = SampleTriplanarScaled(TEXTURE2D_ARGS(_PolarAlbedoTex, sampler_MountainDetailTex), positionOS, objNorm, _BiomeTextureScale);
                float3 ma = SampleTriplanarScaled(TEXTURE2D_ARGS(_MarshAlbedoTex, sampler_MountainDetailTex), positionOS, objNorm, _BiomeTextureScale);
                float3 texBiome = ju*w.jungle + de*w.desert + sa*w.savanna + tg*w.temperateGrass + tf*w.temperateForest + tu*w.tundra + po*w.polar + ma*w.marsh;
                float texLuma = dot(texBiome, float3(0.299,0.587,0.114));
                texBiome = lerp(texBiome, texBiome * (1.0 + (texLuma - 0.5) * _BiomeTextureContrast), _BiomeTextureContrast);
                float3 gradedTexture = texBiome * climateGrade;
                return lerp(texBiome, gradedTexture, _BiomeTintStrength * _BiomeTextureStrength);
            }

            // -----------------------------------------------------------------
            //  Infernal / Demonic color helpers
            // -----------------------------------------------------------------
            float3 GetHellLandColor(float moist, float elevH)
            {
                // Charred, scorched earth tones
                float3 dryAsh    = float3(0.18, 0.12, 0.10); // dark charcoal
                float3 wetAsh    = float3(0.25, 0.15, 0.12); // slightly warmer charcoal
                float3 baseLand  = lerp(dryAsh, wetAsh, moist);

                // Higher elevation → darker, almost black obsidian
                float3 obsidian  = float3(0.08, 0.06, 0.05);
                baseLand = lerp(baseLand, obsidian, saturate(elevH * 1.2));

                return baseLand;
            }

            float3 GetLavaOceanColor(float3 sampleP, float timeVal)
            {
                // Animated lava: dark crust with bright orange/red cracks
                float lavaCrust = fbm(sampleP * 3.0 + float3(13.7, 7.3, 29.1));
                float lavaFlow  = noise3D(sampleP * 5.0 + float3(timeVal * 0.3, timeVal * 0.2, timeVal * 0.1));

                float3 darkCrust   = float3(0.12, 0.04, 0.02); // cooled lava crust
                float3 hotCrack    = float3(0.95, 0.35, 0.05); // bright orange
                float3 whiteHot    = float3(1.00, 0.75, 0.25); // yellow-white hot spots

                // Cracks where the noise creates thin bright lines
                float crackMask = smoothstep(0.42, 0.50, lavaCrust);
                float hotSpot   = smoothstep(0.60, 0.70, lavaFlow) * crackMask;

                float3 lavaColor = lerp(darkCrust, hotCrack, crackMask * 0.7);
                lavaColor = lerp(lavaColor, whiteHot, hotSpot * 0.5);

                return lavaColor;
            }

            // -----------------------------------------------------------------
            //  Vertex displacement helpers
            // -----------------------------------------------------------------
            float GetLandMask(float3 objNorm, float3 seedOff)
            {
                float n = GetWarpedLandValue(objNorm, seedOff);
                return smoothstep(_LandThreshold - 0.04, _LandThreshold + 0.04, n);
            }

            float GetCapMask(float3 objNorm, float3 seedOff)
            {
                float latitude = abs(objNorm.y);
                float iceEdgeNoise = noise3D(objNorm * 6.0 + float3(11.1, 5.5, 22.2) + seedOff);
                float capStart = lerp(1.10, 0.15, _IceCapSize);
                return smoothstep(capStart - 0.10, capStart + 0.10, latitude + (iceEdgeNoise - 0.5) * 0.15);
            }

            float GetMountainMask(float3 objNorm, float landMask)
            {
                float3 seedOff = float3(_Seed, _Seed * 0.7, _Seed * 1.3);
                float broadElevationNoise = fbm(objNorm * (_LandScale * 1.2) + seedOff + float3(29.3, 17.7, 63.1));
                float ridge = 1.0 - abs(fbm(objNorm * (_LandScale * 4.6) + seedOff + float3(83.5, 9.2, 44.7)) * 2.0 - 1.0);
                ridge = pow(saturate(ridge), 2.5);
                float mountainMask = smoothstep(0.58, 0.85, ridge + broadElevationNoise * 0.35);
                mountainMask *= landMask;
                mountainMask *= smoothstep(0.35, 1.0, _Elevation);
                return mountainMask;
            }

            float GetTerrainHeightValue(float3 objNorm, float landMask, float capMask)
            {
                float3 seedOff = float3(_Seed, _Seed * 0.7, _Seed * 1.3);
                float hills = fbm(objNorm * (_LandScale * 2.2) + seedOff + float3(99.1, 55.3, 12.7));
                hills *= landMask;
                float mountainMask = GetMountainMask(objNorm, landMask);
                float volcanicMask = smoothstep(0.35, 1.0, _MapStyle) * mountainMask * fbm(objNorm * (_LandScale * 6.0) + seedOff + float3(4.4, 66.1, 27.8));
                float waterMask = 1.0 - landMask;
                float finalHeight = landMask * _LandUpliftStrength + hills * _HillDisplacementStrength + mountainMask * _MountainDisplacementStrength + capMask * _IceDisplacementStrength + volcanicMask * _VolcanicDisplacementStrength - waterMask * _OceanDepthStrength;
                return finalHeight;
            }

            float GetPreviewDisplacementHeight(float3 objNorm)
            {
                float3 seedOff = float3(_Seed, _Seed * 0.7, _Seed * 1.3);
                float landMask = GetLandMask(objNorm, seedOff);
                float capMask = GetCapMask(objNorm, seedOff);
                float rawHeight = GetTerrainHeightValue(objNorm, landMask, capMask);
                return rawHeight * _DisplacementScale;
            }

            // -----------------------------------------------------------------
            //  Vertex
            // -----------------------------------------------------------------
            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                // --- Vertex displacement ---
                float baseRadius = max(1e-5, length(input.positionOS.xyz));
                float3 objNorm = input.positionOS.xyz / baseRadius;

                float3 seedOff = float3(_Seed, _Seed * 0.7, _Seed * 1.3);
                float disp = GetPreviewDisplacementHeight(objNorm);
                float3 displacedOS = objNorm * (baseRadius * (1.0 + disp));

                // Compute displaced normal from displaced positions (more stable than
                // trying to perturb the normal directly).
                float eps = 0.012;
                float3 tangent1 = normalize(cross(objNorm, abs(objNorm.y) < 0.99 ? float3(0,1,0) : float3(1,0,0)));
                float3 tangent2 = cross(objNorm, tangent1);

                float3 nU = normalize(objNorm + tangent1 * eps);
                float3 nV = normalize(objNorm + tangent2 * eps);
                float dispU = GetPreviewDisplacementHeight(nU);
                float dispV = GetPreviewDisplacementHeight(nV);
                float3 p  = displacedOS;
                float3 pU = nU * (baseRadius * (1.0 + dispU));
                float3 pV = nV * (baseRadius * (1.0 + dispV));
                float3 dispNormalOS = normalize(cross(pU - p, pV - p));
                if (dot(dispNormalOS, objNorm) < 0.0)
                {
                    dispNormalOS = -dispNormalOS;
                }

                float3 worldPos = TransformObjectToWorld(displacedOS);
                output.positionCS = TransformWorldToHClip(worldPos);
                output.positionWS = worldPos;
                if (_UseDisplacedNormals > 0.5)
                {
                    output.normalWS = TransformObjectToWorldNormal(dispNormalOS);
                }
                else
                {
                    output.normalWS = TransformObjectToWorldNormal(objNorm);
                }
                output.positionOS = input.positionOS.xyz; // undisplaced for fragment noise sampling

                return output;
            }

            // -----------------------------------------------------------------
            //  Fragment
            // -----------------------------------------------------------------
            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // _MapStyle: 0 = normal, 0.5 = infernal, 1.0 = demonic
                float style = saturate(_MapStyle);
                float infernal = saturate((style -0.35)/0.35);
                float demonic = saturate((style - 0.75)/0.25);
                float timeVal = _Time.y;

                // Sample noise at object-space position (seamless on sphere surface)
                float3 objNorm = normalize(input.positionOS);
                float3 seedOff = float3(_Seed, _Seed * 0.7, _Seed * 1.3);
                float3 samplePos = objNorm * _LandScale;
                float n = GetWarpedLandValue(objNorm, seedOff);

                // Soft edge between land and ocean
                float edge = smoothstep(_LandThreshold - 0.04, _LandThreshold + 0.04, n);

                // ---- Elevation noise ----
                float elevNoise = fbm(samplePos * 1.5 + float3(99.1, 55.3, 12.7) + seedOff);
                float terrainHeight = elevNoise * _Elevation;
                float landMask = edge;
                float capMask = GetCapMask(objNorm, seedOff);
                float mountainMask = GetMountainMask(objNorm, landMask);
                float finalHeight = GetTerrainHeightValue(objNorm, landMask, capMask);
                if (_ShowElevationOnly > 0.5) return float4(terrainHeight.xxx, 1);
                if (_ShowMountainMaskOnly > 0.5) return float4(mountainMask.xxx, 1);
                if (_ShowDisplacementHeightOnly > 0.5) return float4(saturate(finalHeight).xxx, 1);

                float highBand = smoothstep(0.30, 0.50, terrainHeight);
                float mtnBand  = smoothstep(0.50, 0.65, terrainHeight);
                float snowBand = smoothstep(0.62, 0.78, terrainHeight);

                float latitude = abs(objNorm.y);

                // ==============================================================
                //  Latitude-based local climate (biome zoning)
                // ==============================================================
                // Biomes are selected STRICTLY by latitude.
                // _Temperature shifts all band boundaries (hotter = bands move poleward).
                // _Moisture controls within-band color (especially equator: desert vs jungle).
                // Temperature shift: 0.5 = neutral, <0.5 = colder, >0.5 = hotter
                float tempShift = _Temperature - 0.5; // range -0.5 to +0.5
                // Global moisture should have a dramatic visual effect. Latitude/noise only add variation,
                // they should not overpower the slider.
                float climateNoiseA = fbm(objNorm * 2.0 + seedOff);
                float climateNoiseB = fbm(objNorm * 5.0 + seedOff * 1.37);
                // Moisture should be globally driven by slider, but terrain/latitude should
                // still shape realistic wet/dry pockets:
                // - wet coasts
                // - dry rain-shadow interiors
                // - temperature-limited humidity at extremes
                float coastProximity = 1.0 - abs(edge * 2.0 - 1.0); // high near shoreline transition
                float interiorness = saturate((edge - 0.45) / 0.50); // 0 near coast/ocean, 1 inland
                float rainShadow = saturate(mtnBand * interiorness * 1.15);
                float tempHumidityLimit = saturate(1.0 - abs(_Temperature - 0.55) * 1.05);
                float localMoist = saturate(
                    _Moisture * lerp(0.82, 1.12, tempHumidityLimit)
                    + climateNoiseA * 0.18
                    + (climateNoiseB - 0.5) * 0.14
                    + coastProximity * 0.10
                    - rainShadow * 0.20
                );

                // ==============================================================
                //  Rivers (shared noise — used by normal, infernal, and demonic)
                // ==============================================================
                // Multi-octave domain warping for natural, sinuous river curves.
                // First warp layer: large-scale bends
                float3 warp1 = float3(
                    noise3D(samplePos * 1.2 + float3(12.5, 34.7, 56.9) + seedOff),
                    noise3D(samplePos * 1.2 + float3(78.1, 23.4, 91.6) + seedOff),
                    noise3D(samplePos * 1.2 + float3(45.3, 67.8, 11.2) + seedOff)
                ) * 0.5 - 0.25;
                // Second warp layer: smaller meanders
                float3 warp2 = float3(
                    noise3D(samplePos * 3.5 + float3(91.3, 17.8, 42.1) + seedOff),
                    noise3D(samplePos * 3.5 + float3(33.7, 82.5, 64.9) + seedOff),
                    noise3D(samplePos * 3.5 + float3(58.2, 11.6, 73.4) + seedOff)
                ) * 0.15 - 0.075;

                float3 riverSample = samplePos + warp1 + warp2;
                // Primary river: one or two big continental rivers
                float riverNoise1 = noise3D(riverSample * 2.0 + float3(3.3, 77.7, 21.1) + seedOff);
                // Secondary: smaller tributary
                float riverNoise2 = noise3D(riverSample * 3.8 + float3(61.2, 9.8, 44.4) + seedOff);
                // Thinner bands for more realistic width
                float river1 = 1.0 - smoothstep(0.0, 0.040, abs(riverNoise1 - 0.5));
                float river2 = 1.0 - smoothstep(0.0, 0.028, abs(riverNoise2 - 0.5));
                float riverMask = max(river1 * 1.0, river2 * 0.4);
                riverMask *= edge; // on land only

                // ==============================================================
                //  NORMAL WORLD colors
                // ==============================================================
                // Biome color selected strictly by latitude, shifted by temperature
                float3 climateGrade = GetClimateGrade(latitude, localMoist, style);
                float capStart = lerp(1.10, 0.15, _IceCapSize);
                float iceEdgeNoise = noise3D(objNorm * 6.0 + float3(11.1, 5.5, 22.2) + seedOff);
                // Local temperature: latitude-first, then elevation lapse-rate cooling,
                // then small noise to avoid strict banding.
                float tempNoise = (noise3D(objNorm * 4.5 + float3(14.2, 36.1, 6.5) + seedOff) - 0.5) * 0.09;
                float elevationCooling = terrainHeight * lerp(0.10, 0.28, saturate(_Elevation + mtnBand * 0.8));
                float temperatureLocal = saturate((1.0 - latitude) * 0.66 + _Temperature * 0.34 + tempShift * 0.12 - elevationCooling + tempNoise);
                SurfaceBiomeWeights biomeWeights = GetSurfaceBiomeWeights(latitude, temperatureLocal, _Moisture, localMoist, terrainHeight, capMask, objNorm, _Seed);
                float3 biomeTextureAlbedo = GetTextureBiomeAlbedo(biomeWeights, climateGrade, input.positionOS, objNorm);

                // Uniform ocean color — single inspector-driven color, no depth/latitude variation
                float3 oceanColor = _OceanColor.rgb;

                // --------------------------------------------------------------
                //  High-frequency detail normal perturbation
                // --------------------------------------------------------------
                float eps = 0.002 / max(1.0, _DetailScale);
                float d0 = fbm(samplePos * _DetailScale + seedOff);
                float dx = fbm((samplePos + float3(eps,0,0)) * _DetailScale + seedOff);
                float dy = fbm((samplePos + float3(0,eps,0)) * _DetailScale + seedOff);
                float dz = fbm((samplePos + float3(0,0,eps)) * _DetailScale + seedOff);
                float3 grad = normalize(float3(dx - d0, dy - d0, dz - d0));
                float3 normal = normalize(input.normalWS + grad * _DetailStrength * 1.2);


                float3 snowPeakColor = float3(0.92, 0.93, 0.96);

                float3 elevatedLand = biomeTextureAlbedo;
                float mtnBlend = mountainMask * smoothstep(0.35, 0.60, terrainHeight);
                // Snow: strictly latitude-based — more snow at higher latitudes, less near equator
                float latSnowFactor = smoothstep(0.4, 0.7, latitude + tempShift * -0.5);
                float snowAmount = snowBand * saturate(latSnowFactor + _SnowFactor * 0.5);
                elevatedLand = lerp(elevatedLand, snowPeakColor, snowAmount);

                // Slope shading on mountains: darken steep faces for depth
                float3 sphereNormalWS = normalize(TransformObjectToWorldNormal(objNorm));
                float slopeDot = dot(normalize(input.normalWS), sphereNormalWS);
                float slopeFactor = smoothstep(0.88, 0.65, slopeDot) * edge;
                elevatedLand = lerp(elevatedLand, elevatedLand * 0.6, slopeFactor * 0.5);

                // Biome micro-textures: subtle noise variation to break up flat bands
                float microNoise = noise3D(samplePos * 35.0 + float3(7.1, 13.3, 21.7) + seedOff);
                // Light brightness variation preserving the biome's actual color
                elevatedLand *= lerp(0.95, 1.05, microNoise);

                float3 normalAlbedo = lerp(oceanColor, elevatedLand, edge);
                if (_UseDetailTextures > 0.5)
                {
                    float3 mtnDetail = SampleTriplanar(TEXTURE2D_ARGS(_MountainDetailTex, sampler_MountainDetailTex), input.positionOS, objNorm);
                    float3 ocnDetail = SampleTriplanar(TEXTURE2D_ARGS(_OceanDetailTex, sampler_MountainDetailTex), input.positionOS, objNorm);
                    float mtnFactor = (dot(mtnDetail, float3(0.333,0.333,0.333)) - 0.5) * 2.0;
                    float ocnFactor = (dot(ocnDetail, float3(0.333,0.333,0.333)) - 0.5) * 2.0;
                    elevatedLand *= (1.0 + mtnFactor * _MountainDetailStrength * mtnBlend * edge);
                    oceanColor *= (1.0 + ocnFactor * _OceanDetailStrength * (1.0 - edge));
                    normalAlbedo = lerp(oceanColor, elevatedLand, edge);
                }

                // Normal rivers (moisture-gated, not on mountains)
                // IMPORTANT: waterwayAmount now changes network density/shape,
                // not just brightness. Higher values widen channels, add tributaries,
                // and lower lake thresholds to create more actual water features.
                float waterwayAmount = _WaterwayAmount;
                float waterwayDensity = saturate(waterwayAmount);
                float riverWidthA = lerp(0.022, 0.055, waterwayDensity);
                float riverWidthB = lerp(0.016, 0.040, waterwayDensity);
                float mainRiverA = 1.0 - smoothstep(0.0, riverWidthA, abs(riverNoise1 - 0.5));
                float mainRiverB = 1.0 - smoothstep(0.0, riverWidthB, abs(riverNoise2 - 0.5));

                // Tributary layer appears primarily at medium/high settings.
                float tributaryNoise = noise3D((riverSample + warp2 * 0.6) * 6.4 + float3(19.4, 41.8, 12.6) + seedOff);
                float tributaryWidth = lerp(0.0, 0.026, smoothstep(0.35, 1.0, waterwayDensity));
                float tributaryMask = tributaryWidth > 0.0 ? (1.0 - smoothstep(0.0, tributaryWidth, abs(tributaryNoise - 0.5))) : 0.0;

                float riverNetworkMask = max(mainRiverA, mainRiverB * 0.8);
                riverNetworkMask = max(riverNetworkMask, tributaryMask * 0.7);
                // Hard constraints to keep waterways geologically plausible:
                // - never in ocean
                // - reduced on immediate coasts (estuaries are narrow in this stylized pass)
                // - suppressed in very arid or very cold regions
                float inlandMask = smoothstep(_LandThreshold + 0.03, _LandThreshold + 0.16, n);
                float aridityGate = smoothstep(0.18, 0.72, localMoist);
                float freezeGate = smoothstep(0.10, 0.28, temperatureLocal);
                float moistureRiverBoost = lerp(0.65, 1.08, localMoist);
                float normalRiverMask = riverNetworkMask
                                      * inlandMask
                                      * saturate(1.0 - mtnBand * 0.8)
                                      * aridityGate
                                      * freezeGate
                                      * moistureRiverBoost
                                      * lerp(0.35, 1.0, waterwayDensity);
                normalAlbedo = lerp(normalAlbedo, float3(0.10, 0.25, 0.45), saturate(normalRiverMask));

                // Lakes (normal)
                float lakeNoise = noise3D(samplePos * 12.0 + float3(7.7, 3.3, 9.9) + seedOff);
                float lakeShapeNoise = noise3D(samplePos * lerp(8.0, 15.5, waterwayDensity) + float3(27.2, 5.1, 13.7) + seedOff);
                float lakeCombined = lakeNoise * 0.68 + lakeShapeNoise * 0.32;
                float lakeEdgeLow = lerp(0.82, 0.63, waterwayDensity);
                float lakeEdgeHigh = lakeEdgeLow + lerp(0.06, 0.11, waterwayDensity);
                float lakeMask  = smoothstep(lakeEdgeLow, lakeEdgeHigh, lakeCombined)
                                * inlandMask
                                * smoothstep(0.35, 0.92, localMoist)
                                * saturate(1.0 - mtnBand * 0.5);
                normalAlbedo = lerp(normalAlbedo, float3(0.12, 0.30, 0.50), saturate(lakeMask));

                // ---- Ice caps ----
                // Driven entirely by _IceCapSize (set by the climate preset in MainMenuManager).
                // 0 = no ice,  1 = massive polar ice extending near equator.
                // No temperature math — the preset already encodes how icy the planet should be.

                // capMask computed earlier for biome + ice logic.

                // Ice/snow color — white at center, icy blue at outer fringe
                float3 snowWhite = float3(0.95, 0.97, 1.0);
                float3 icyBlue   = float3(0.72, 0.85, 0.95); // pale icy blue
                float iceVariation = noise3D(objNorm * 10.0 + float3(77.7, 88.8, 99.9) + seedOff);

                // Outer fringe of the cap is icy blue, inner core is snow white
                float edgeFade = smoothstep(capStart - 0.05, capStart + 0.15, latitude + (iceEdgeNoise - 0.5) * 0.15);
                float3 iceColor = lerp(icyBlue, snowWhite, edgeFade);
                // Subtle variation within ice
                iceColor = lerp(iceColor * 0.95, iceColor, smoothstep(0.3, 0.7, iceVariation));
                // Frozen ocean gets a slightly darker ice sheet
                iceColor = lerp(iceColor * 0.88, iceColor, edge);

                normalAlbedo = lerp(normalAlbedo, iceColor, saturate(capMask));
                if (_UseDetailTextures > 0.5)
                {
                    float3 iceDetail = SampleTriplanar(TEXTURE2D_ARGS(_IceDetailTex, sampler_MountainDetailTex), input.positionOS, objNorm);
                    float iceFactor = (dot(iceDetail, float3(0.333,0.333,0.333)) - 0.5) * 2.0;
                    normalAlbedo = lerp(normalAlbedo, normalAlbedo * (1.0 + iceFactor * _IceDetailStrength), saturate(capMask));
                }

                // ==============================================================
                //  INFERNAL WORLD colors (style ≈ 0.5)
                // ==============================================================
                float3 hellLand = GetHellLandColor(_Moisture, terrainHeight);

                // Volcanic glow on highlands
                float3 volcanicGlow = float3(0.60, 0.15, 0.03);
                float glowAmount = mtnBand * 0.4 + highBand * 0.15;
                float glowPulse = sin(timeVal * 1.5 + elevNoise * 12.0) * 0.5 + 0.5;
                glowAmount *= lerp(0.7, 1.0, glowPulse);
                hellLand = lerp(hellLand, volcanicGlow, glowAmount);

                float3 hellOcean = GetLavaOceanColor(samplePos, timeVal);
                float3 hellAlbedo = lerp(hellOcean, hellLand, edge);

                // Lava rivers
                float lavaRiverMask = riverMask * saturate(1.0 - mtnBand * 0.5);
                float3 lavaRiverColor = float3(0.90, 0.30, 0.04);
                float riverPulse = sin(timeVal * 2.0 + riverNoise1 * 8.0) * 0.5 + 0.5;
                lavaRiverColor = lerp(lavaRiverColor, float3(1.0, 0.65, 0.15), riverPulse * 0.4);
                hellAlbedo = lerp(hellAlbedo, lavaRiverColor, saturate(lavaRiverMask));

                // Volcanic vent spots
                float ventNoise = noise3D(samplePos * 10.0 + float3(44.4, 88.8, 22.2) + seedOff);
                float ventMask = smoothstep(0.78, 0.84, ventNoise) * edge;
                float ventFlicker = sin(timeVal * 4.0 + ventNoise * 20.0) * 0.5 + 0.5;
                float3 ventColor = lerp(float3(0.85, 0.25, 0.02), float3(1.0, 0.60, 0.10), ventFlicker);
                hellAlbedo = lerp(hellAlbedo, ventColor, ventMask * 0.8);

                // Lava lakes
                float hellLakeMask = smoothstep(0.70, 0.77, lakeNoise) * step(0.5, edge);
                float3 lavaLakeColor = float3(0.80, 0.22, 0.03);
                float lakePulse = sin(timeVal * 1.2 + lakeNoise * 6.0) * 0.5 + 0.5;
                lavaLakeColor = lerp(lavaLakeColor, float3(1.0, 0.55, 0.12), lakePulse * 0.5);
                hellAlbedo = lerp(hellAlbedo, lavaLakeColor, hellLakeMask);

                // Ash polar caps
                float ashCapStart = 0.78;
                float ashCapMask = smoothstep(ashCapStart - 0.06, ashCapStart + 0.06, latitude);
                float ashEdgeNoise = noise3D(objNorm * 5.0 + float3(33.3, 66.6, 99.9) + seedOff);
                ashCapMask *= smoothstep(ashCapStart - 0.10, ashCapStart + 0.03, latitude + (ashEdgeNoise - 0.5) * 0.12);
                float3 ashColor = lerp(float3(0.22, 0.18, 0.16), float3(0.30, 0.25, 0.22), edge);
                hellAlbedo = lerp(hellAlbedo, ashColor, saturate(ashCapMask));

                // ==============================================================
                //  DEMONIC WORLD colors (style ≈ 1.0, built on top of infernal)
                //  Much darker land, brighter/more intense lava, deeper reds
                // ==============================================================

                // Near-black land with blood-red undertone
                float3 demonLand = float3(0.06, 0.03, 0.02); // near-black
                float3 bloodTint = float3(0.25, 0.04, 0.02);  // deep blood red
                // Subtle variation from elevation noise
                demonLand = lerp(demonLand, bloodTint, elevNoise * 0.5);
                // Cracks of hellfire visible through the dark crust
                float crackNoise = noise3D(samplePos * 7.0 + float3(66.6, 13.1, 99.9) + seedOff);
                float crackMask = smoothstep(0.46, 0.50, crackNoise) * edge;
                float3 hellfireColor = float3(1.0, 0.20, 0.02); // intense hellfire red
                float crackPulse = sin(timeVal * 3.0 + crackNoise * 15.0) * 0.5 + 0.5;
                hellfireColor = lerp(hellfireColor, float3(1.0, 0.50, 0.05), crackPulse * 0.3);
                demonLand = lerp(demonLand, hellfireColor, crackMask * 0.7);

                // Brighter, more intense lava ocean
                float3 demonOcean = GetLavaOceanColor(samplePos, timeVal);
                // Boost brightness and shift toward pure red
                demonOcean = demonOcean * 1.4 + float3(0.15, 0.0, 0.0);

                float3 demonAlbedo = lerp(demonOcean, demonLand, edge);

                // Demonic lava rivers — brighter, wider presence
                float demonRiverMask = riverMask * saturate(1.0 - mtnBand * 0.3);
                float3 demonRiverColor = float3(1.0, 0.18, 0.02); // intense red-orange
                float demonRiverPulse = sin(timeVal * 2.5 + riverNoise1 * 10.0) * 0.5 + 0.5;
                demonRiverColor = lerp(demonRiverColor, float3(1.0, 0.55, 0.08), demonRiverPulse * 0.4);
                demonAlbedo = lerp(demonAlbedo, demonRiverColor, saturate(demonRiverMask));

                // More volcanic vents, brighter
                float demonVentMask = smoothstep(0.72, 0.80, ventNoise) * edge; // lower threshold = more vents
                float3 demonVentColor = lerp(float3(1.0, 0.15, 0.01), float3(1.0, 0.45, 0.05), ventFlicker);
                demonAlbedo = lerp(demonAlbedo, demonVentColor, demonVentMask * 0.9);

                // Lava lakes — brighter
                float3 demonLavaLake = float3(1.0, 0.25, 0.02);
                demonLavaLake = lerp(demonLavaLake, float3(1.0, 0.50, 0.08), lakePulse * 0.4);
                demonAlbedo = lerp(demonAlbedo, demonLavaLake, hellLakeMask);

                // No ash caps — pure scorched nothing at poles
                // (intentionally no cap blending for demonic)

                // ==============================================================
                //  Blend: normal → infernal → demonic
                // ==============================================================
                // First blend normal to infernal, then infernal to demonic
                float3 albedo = lerp(normalAlbedo, hellAlbedo, infernal);
                albedo = lerp(albedo, demonAlbedo, demonic);

                            float3 volcanicSample = SampleTriplanarScaled(TEXTURE2D_ARGS(_VolcanicRockTex, sampler_MountainDetailTex), input.positionOS, objNorm, _LavaTextureScale);
                float lavaMaskSample = SampleTriplanarScaled(TEXTURE2D_ARGS(_LavaCrackTex, sampler_MountainDetailTex), input.positionOS, objNorm, _LavaTextureScale).r;
                float lavaEmissiveSample = SampleTriplanarScaled(TEXTURE2D_ARGS(_LavaEmissiveTex, sampler_MountainDetailTex), input.positionOS, objNorm, _LavaTextureScale).r;
                float ashSample = SampleTriplanarScaled(TEXTURE2D_ARGS(_AshDetailTex, sampler_MountainDetailTex), input.positionOS, objNorm, _LavaTextureScale).r;
                float fallbackCrack = smoothstep(0.46, 0.50, noise3D(samplePos * 7.0 + float3(66.6, 13.1, 99.9) + seedOff));
                float lavaMask = max(lavaMaskSample, fallbackCrack * 0.75);
                float lavaEmissionMask = max(lavaEmissiveSample, lavaMask);
                float volcanic = dot(volcanicSample, float3(0.333, 0.333, 0.333));
                float volcanicLandMask = saturate(edge * (1.0 - capMask * 0.35));
                float crack = smoothstep(0.55, 0.9, lavaMask) * volcanicLandMask * infernal * _LavaCrackStrength;
                float emissiveCrack = crack * lavaEmissionMask;
                float volcanicDarken = lerp(0.25, 0.55, volcanic);
                float3 volcanicColor = lerp(albedo, albedo * volcanicDarken, _VolcanicRockStrength * infernal);
                albedo = lerp(albedo, volcanicColor, infernal);
                float ashMask = ashSample * _AshDetailStrength * infernal * saturate(0.4 + volcanicLandMask + capMask * 0.4);
                float ashGray = dot(albedo, float3(0.299, 0.587, 0.114));
                albedo = lerp(albedo, float3(ashGray, ashGray, ashGray), ashMask * 0.35);

                float oceanSmoothMask = SampleTriplanar(TEXTURE2D_ARGS(_OceanSmoothnessTex, sampler_MountainDetailTex), input.positionOS, objNorm).r;
                float iceSmoothMask = SampleTriplanar(TEXTURE2D_ARGS(_IceSmoothnessTex, sampler_MountainDetailTex), input.positionOS, objNorm).r;
                float marshSmoothMask = SampleTriplanarScaled(TEXTURE2D_ARGS(_MarshSmoothnessTex, sampler_MountainDetailTex), input.positionOS, objNorm, _BiomeTextureScale).r;
                float volcanicSmoothMask = SampleTriplanarScaled(TEXTURE2D_ARGS(_VolcanicSmoothnessTex, sampler_MountainDetailTex), input.positionOS, objNorm, _LavaTextureScale).r;
                float landBlendMask = 0.75;
                float oceanBlendMask = oceanSmoothMask;
                float iceBlendMask = iceSmoothMask;
                float marshWeight = saturate(biomeWeights.marsh);
                float infernalWeight = saturate(infernal + demonic);
                float landSmooth = lerp(_Smoothness * 0.70, _Smoothness * 0.95, landBlendMask);
                float oceanSmooth = lerp(_Smoothness * 0.52, _Smoothness * 0.72, oceanBlendMask) * saturate(_Smoothness + 0.20);
                float iceSmooth = lerp(_Smoothness * 0.38, _Smoothness * 0.58, iceBlendMask);
                float marshSmooth = lerp(_Smoothness * 0.62, _Smoothness * 0.82, marshSmoothMask);
                float volcanicSmooth = lerp(_Smoothness * 0.18, _Smoothness * 0.38, volcanicSmoothMask);
                float mountainSmooth = landSmooth * 0.68;
                float smoothnessMask = lerp(oceanSmooth, landSmooth, edge);
                smoothnessMask = lerp(smoothnessMask, marshSmooth, marshWeight * edge);
                smoothnessMask = lerp(smoothnessMask, mountainSmooth, mtnBlend * edge);
                smoothnessMask = lerp(smoothnessMask, iceSmooth, capMask);
                smoothnessMask = lerp(smoothnessMask, volcanicSmooth, infernalWeight * edge);

                // ==============================================================
                //  Custom lighting using blended triplanar normals
                // ==============================================================
                float3 oceanN = SampleTriplanar(TEXTURE2D_ARGS(_OceanNormalTex, sampler_MountainDetailTex), input.positionOS, objNorm) * 2.0 - 1.0;
                float3 mtnN = SampleTriplanar(TEXTURE2D_ARGS(_MountainNormalTex, sampler_MountainDetailTex), input.positionOS, objNorm) * 2.0 - 1.0;
                float3 iceN = SampleTriplanar(TEXTURE2D_ARGS(_IceNormalTex, sampler_MountainDetailTex), input.positionOS, objNorm) * 2.0 - 1.0;
                float3 juN = SampleTriplanarScaled(TEXTURE2D_ARGS(_JungleNormalTex, sampler_MountainDetailTex), input.positionOS, objNorm, _BiomeTextureScale) * 2.0 - 1.0;
                float3 deN = SampleTriplanarScaled(TEXTURE2D_ARGS(_DesertNormalTex, sampler_MountainDetailTex), input.positionOS, objNorm, _BiomeTextureScale) * 2.0 - 1.0;
                float3 saN = SampleTriplanarScaled(TEXTURE2D_ARGS(_SavannaNormalTex, sampler_MountainDetailTex), input.positionOS, objNorm, _BiomeTextureScale) * 2.0 - 1.0;
                float3 tgN = SampleTriplanarScaled(TEXTURE2D_ARGS(_TemperateGrassNormalTex, sampler_MountainDetailTex), input.positionOS, objNorm, _BiomeTextureScale) * 2.0 - 1.0;
                float3 tfN = SampleTriplanarScaled(TEXTURE2D_ARGS(_TemperateForestNormalTex, sampler_MountainDetailTex), input.positionOS, objNorm, _BiomeTextureScale) * 2.0 - 1.0;
                float3 tuN = SampleTriplanarScaled(TEXTURE2D_ARGS(_TundraNormalTex, sampler_MountainDetailTex), input.positionOS, objNorm, _BiomeTextureScale) * 2.0 - 1.0;
                float3 poN = SampleTriplanarScaled(TEXTURE2D_ARGS(_PolarNormalTex, sampler_MountainDetailTex), input.positionOS, objNorm, _BiomeTextureScale) * 2.0 - 1.0;
                float3 maN = SampleTriplanarScaled(TEXTURE2D_ARGS(_MarshNormalTex, sampler_MountainDetailTex), input.positionOS, objNorm, _BiomeTextureScale) * 2.0 - 1.0;
                float3 biomeN = normalize(juN*biomeWeights.jungle + deN*biomeWeights.desert + saN*biomeWeights.savanna + tgN*biomeWeights.temperateGrass + tfN*biomeWeights.temperateForest + tuN*biomeWeights.tundra + poN*biomeWeights.polar + maN*biomeWeights.marsh);
                float3 surfN = normal;
                surfN = normalize(lerp(surfN, normalize(input.normalWS + oceanN), (1.0-edge) * _OceanNormalStrength));
                surfN = normalize(lerp(surfN, normalize(input.normalWS + biomeN), edge * _BiomeNormalStrength * (_UseTextureDrivenBiomes > 0.5 ? 1.0 : 0.0)));
                surfN = normalize(lerp(surfN, normalize(input.normalWS + mtnN), edge * mtnBlend * _MountainNormalStrength));
                surfN = normalize(lerp(surfN, normalize(input.normalWS + iceN), capMask * _IceNormalStrength));

                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                float3 L = normalize(float3(0.45,0.65,0.55));
                float3 fillL = normalize(float3(-0.35,0.25,-0.9));
                float ndl = saturate(dot(surfN, L));
                ndl = smoothstep(0.0, max(0.01,_TerminatorSoftness), ndl);
                float fill = saturate(dot(surfN, fillL)) * 0.35;
                float3 lit = albedo * (ndl + fill + _AmbientStrength);
                float3 H = normalize(L + viewDir);
                float specN = saturate(dot(surfN, H));
                float oceanSpec = pow(specN, lerp(24,120,saturate(smoothnessMask))) * (1.0-edge) * 0.9;
                float iceSpec = pow(specN, 36) * capMask * 0.25;
                lit += oceanSpec + iceSpec;
                float3 finalColor = lit * _Brightness;

                if (_ShowLandMaskOnly > 0.5) return float4(edge.xxx, 1.0);
                if (_ShowDetailTexturesOnly > 0.5)
                {
                    float3 d = float3(0,0,0);
                    d += SampleTriplanar(TEXTURE2D_ARGS(_MountainDetailTex, sampler_MountainDetailTex), input.positionOS, objNorm) * (mtnBlend * edge);
                    d += SampleTriplanar(TEXTURE2D_ARGS(_IceDetailTex, sampler_MountainDetailTex), input.positionOS, objNorm) * capMask;
                    d += SampleTriplanar(TEXTURE2D_ARGS(_OceanDetailTex, sampler_MountainDetailTex), input.positionOS, objNorm) * (1.0-edge);
                    return float4(saturate(d), 1.0);
                }
                if (_ShowNormalsOnly > 0.5) return float4(surfN * 0.5 + 0.5, 1.0);
                if (_ShowBiomeWeightsOnly > 0.5) return float4(saturate(biomeWeights.jungle*float3(0.05,0.35,0.08)+biomeWeights.desert*float3(0.85,0.74,0.45)+biomeWeights.savanna*float3(0.58,0.52,0.2)+biomeWeights.temperateGrass*float3(0.2,0.6,0.22)+biomeWeights.temperateForest*float3(0.08,0.32,0.1)+biomeWeights.tundra*float3(0.5,0.45,0.4)+biomeWeights.polar*float3(0.85,0.92,1.0)+biomeWeights.marsh*float3(0.2,0.55,0.5)),1.0);
                if (_ShowBiomeTextureOnly > 0.5) return float4(saturate(biomeTextureAlbedo), 1.0);
                if (_ShowLocalMoistureOnly > 0.5) return float4(localMoist.xxx,1.0);
                if (_ShowWaterwaysOnly > 0.5) return float4(0.1*normalRiverMask,0.3*normalRiverMask,0.8*max(normalRiverMask,lakeMask),1.0);
                if (_ShowWaterwayAmountOnly > 0.5) return float4(_WaterwayAmount.xxx,1.0);
                if (_ShowSmoothnessOnly > 0.5) return float4(smoothnessMask.xxx, 1.0);

// ==============================================================
                //  Emissive additions (infernal + demonic)
                // ==============================================================
                float3 lavaEmit = GetLavaOceanColor(samplePos, timeVal);

                // Infernal emissives (scale with infernal blend)
                float oceanEmissive = (1.0 - edge) * infernal;
                finalColor += lavaEmit * oceanEmissive * 0.4;
                finalColor += ventColor * ventMask * infernal * 0.6;
                finalColor += lavaRiverColor * saturate(lavaRiverMask) * infernal * 0.35;
                finalColor += lavaLakeColor * hellLakeMask * infernal * 0.3;

                // Demonic extra emissives (brighter, on top of infernal)
                finalColor += demonOcean * (1.0 - edge) * demonic * 0.3;
                finalColor += hellfireColor * crackMask * demonic * 0.8;
                finalColor += demonRiverColor * saturate(demonRiverMask) * demonic * 0.5;
                finalColor += demonVentColor * demonVentMask * demonic * 0.7;
                finalColor += demonLavaLake * hellLakeMask * demonic * 0.4;
                float3 lavaColor = lerp(float3(1.0, 0.28, 0.05), float3(0.9, 0.02, 0.18), demonic);
                float3 lavaTextureEmission = lavaColor * emissiveCrack * _LavaEmissionStrength;
                finalColor += lavaTextureEmission;

                // ==============================================================
                //  Atmosphere rim glow
                // ==============================================================
                float fresnel = 1.0 - saturate(dot(normal, viewDir));

                // Infernal rim: red-orange
                float infernalRimMask = pow(fresnel, 3.0) * infernal;
                float3 infernalRimColor = lerp(float3(0.70, 0.12, 0.02), float3(0.90, 0.35, 0.05),
                                               sin(timeVal * 0.8) * 0.5 + 0.5);
                finalColor += infernalRimColor * infernalRimMask * 0.6;

                // Demonic rim: deeper, more intense blood-red with faster pulse
                float demonicRimMask = pow(fresnel, 2.5) * demonic; // wider rim
                float3 demonicRimColor = lerp(float3(0.80, 0.05, 0.01), float3(1.0, 0.15, 0.02),
                                              sin(timeVal * 1.5) * 0.5 + 0.5);
                finalColor += demonicRimColor * demonicRimMask * 0.8;

                // Frozen worlds: no special rim glow — just cold and snowy
                // (atmosphere shell handles any remaining rim)

                return float4(finalColor, 1.0);
            }

            ENDHLSL
        }

        // =====================================================================
        //  Depth pre-pass — required for HDRP forward rendering
        // =====================================================================
        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }

            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile_instancing
            #pragma multi_compile _ UNITY_DOTS_INSTANCING_ENABLED

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // Must match main pass CBUFFER layout exactly for SRP Batcher
            CBUFFER_START(UnityPerMaterial)
                float _LandScale;
                float _LandThreshold;
                float _Temperature;
                float _Moisture;
                float _WaterwayAmount;
                float _Elevation;
                float _DesertFactor;
                float _TropicalFactor;
                float _SnowFactor;
                float4 _OceanColor;
                float _IceCapSize;
                float _BiomeBlend;
                float _BiomeNoiseScale;
                float _BiomeNoiseStrength;
                float _Seed;
                float _MapStyle;
                float _DetailScale;
                float _DetailStrength;
                float4 _AtmosphereColor;
                float _AtmospherePower;
                float _AtmosphereRadius;
                float _DisplacementScale;
                float _LandUpliftStrength;
                float _HillDisplacementStrength;
                float _MountainDisplacementStrength;
                float _IceDisplacementStrength;
                float _VolcanicDisplacementStrength;
                float _OceanDepthStrength;
                float _ShowElevationOnly;
                float _ShowMountainMaskOnly;
                float _ShowDisplacementHeightOnly;
                float _Smoothness;
                float _Metallic;
                float _AmbientOcclusion;
                float _AmbientStrength;
                float _Brightness;
            CBUFFER_END

            float hash31(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.x + p.y) * p.z);
            }
            float noise3D(float3 p)
            {
                float3 i = floor(p); float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = hash31(i); float n100 = hash31(i + float3(1,0,0));
                float n010 = hash31(i + float3(0,1,0)); float n110 = hash31(i + float3(1,1,0));
                float n001 = hash31(i + float3(0,0,1)); float n101 = hash31(i + float3(1,0,1));
                float n011 = hash31(i + float3(0,1,1)); float n111 = hash31(i + float3(1,1,1));
                return lerp(lerp(lerp(n000,n100,f.x),lerp(n010,n110,f.x),f.y),
                            lerp(lerp(n001,n101,f.x),lerp(n011,n111,f.x),f.y),f.z);
            }
            float fbm(float3 p)
            {
                float v=0; float a=0.5; float fr=1;
                for(int i=0;i<5;i++){v+=a*noise3D(p*fr);fr*=2;a*=0.5;}
                return v;
            }
            float GetWarpedLandValue(float3 objNorm, float3 seedOff)
            {
                float3 samplePos = objNorm * _LandScale;
                float3 warp;
                warp.x = fbm(samplePos * 0.9 + float3(31.2, 11.7, 73.4) + seedOff);
                warp.y = fbm(samplePos * 0.9 + float3(8.4, 97.1, 45.6) + seedOff);
                warp.z = fbm(samplePos * 0.9 + float3(56.3, 24.9, 12.8) + seedOff);
                float3 warpedPos = samplePos + (warp - 0.5) * (_BiomeNoiseStrength * 8.0);
                return fbm(warpedPos + float3(42.3, 17.1, 83.7) + seedOff);
            }
            float GetLandMask(float3 objNorm, float3 seedOff)
            {
                float n = GetWarpedLandValue(objNorm, seedOff);
                return smoothstep(_LandThreshold - 0.04, _LandThreshold + 0.04, n);
            }
            float GetCapMask(float3 objNorm, float3 seedOff)
            {
                float latitude = abs(objNorm.y);
                float iceEdgeNoise = noise3D(objNorm * 6.0 + float3(11.1, 5.5, 22.2) + seedOff);
                float capStart = lerp(1.10, 0.15, _IceCapSize);
                return smoothstep(capStart - 0.10, capStart + 0.10, latitude + (iceEdgeNoise - 0.5) * 0.15);
            }
            float GetMountainMask(float3 objNorm, float landMask)
            {
                float3 seedOff = float3(_Seed, _Seed * 0.7, _Seed * 1.3);
                float broadElevationNoise = fbm(objNorm * (_LandScale * 1.2) + seedOff + float3(29.3, 17.7, 63.1));
                float ridge = 1.0 - abs(fbm(objNorm * (_LandScale * 4.6) + seedOff + float3(83.5, 9.2, 44.7)) * 2.0 - 1.0);
                ridge = pow(saturate(ridge), 2.5);
                float mountainMask = smoothstep(0.58, 0.85, ridge + broadElevationNoise * 0.35);
                mountainMask *= landMask;
                mountainMask *= smoothstep(0.35, 1.0, _Elevation);
                return mountainMask;
            }
            float GetTerrainHeightValue(float3 objNorm, float landMask, float capMask)
            {
                float3 seedOff = float3(_Seed, _Seed * 0.7, _Seed * 1.3);
                float hills = fbm(objNorm * (_LandScale * 2.2) + seedOff + float3(99.1, 55.3, 12.7));
                hills *= landMask;
                float mountainMask = GetMountainMask(objNorm, landMask);
                float volcanicMask = smoothstep(0.35, 1.0, _MapStyle) * mountainMask * fbm(objNorm * (_LandScale * 6.0) + seedOff + float3(4.4, 66.1, 27.8));
                float waterMask = 1.0 - landMask;
                return landMask * _LandUpliftStrength + hills * _HillDisplacementStrength + mountainMask * _MountainDisplacementStrength + capMask * _IceDisplacementStrength + volcanicMask * _VolcanicDisplacementStrength - waterMask * _OceanDepthStrength;
            }
            float GetPreviewDisplacementHeight(float3 objNorm)
            {
                float3 seedOff = float3(_Seed, _Seed * 0.7, _Seed * 1.3);
                float landMask = GetLandMask(objNorm, seedOff);
                float capMask = GetCapMask(objNorm, seedOff);
                float rawHeight = GetTerrainHeightValue(objNorm, landMask, capMask);
                return rawHeight * _DisplacementScale;
            }

            Varyings vertDepth(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float r = max(1e-5, length(input.positionOS.xyz));
                float3 n = input.positionOS.xyz / r;
                float disp = GetPreviewDisplacementHeight(n);
                float3 displaced = n * (r * (1.0 + disp));
                float3 worldPos = TransformObjectToWorld(displaced);
                output.positionCS = TransformWorldToHClip(worldPos);
                return output;
            }

            float4 fragDepth(Varyings input) : SV_Target
            {
                return 0;
            }

            ENDHLSL
        }

        // =====================================================================
        //  Shadow caster — lets the sphere cast shadows if needed
        // =====================================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_instancing
            #pragma multi_compile _ UNITY_DOTS_INSTANCING_ENABLED

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // Must match main pass CBUFFER layout exactly for SRP Batcher
            CBUFFER_START(UnityPerMaterial)
                float _LandScale;
                float _LandThreshold;
                float _Temperature;
                float _Moisture;
                float _WaterwayAmount;
                float _Elevation;
                float _DesertFactor;
                float _TropicalFactor;
                float _SnowFactor;
                float4 _OceanColor;
                float _IceCapSize;
                float _BiomeBlend;
                float _BiomeNoiseScale;
                float _BiomeNoiseStrength;
                float _Seed;
                float _MapStyle;
                float _DetailScale;
                float _DetailStrength;
                float4 _AtmosphereColor;
                float _AtmospherePower;
                float _AtmosphereRadius;
                float _DisplacementScale;
                float _LandUpliftStrength;
                float _HillDisplacementStrength;
                float _MountainDisplacementStrength;
                float _IceDisplacementStrength;
                float _VolcanicDisplacementStrength;
                float _OceanDepthStrength;
                float _ShowElevationOnly;
                float _ShowMountainMaskOnly;
                float _ShowDisplacementHeightOnly;
                float _Smoothness;
                float _Metallic;
                float _AmbientOcclusion;
                float _AmbientStrength;
                float _Brightness;
            CBUFFER_END

            float hash31(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.x + p.y) * p.z);
            }
            float noise3D(float3 p)
            {
                float3 i = floor(p); float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = hash31(i); float n100 = hash31(i + float3(1,0,0));
                float n010 = hash31(i + float3(0,1,0)); float n110 = hash31(i + float3(1,1,0));
                float n001 = hash31(i + float3(0,0,1)); float n101 = hash31(i + float3(1,0,1));
                float n011 = hash31(i + float3(0,1,1)); float n111 = hash31(i + float3(1,1,1));
                return lerp(lerp(lerp(n000,n100,f.x),lerp(n010,n110,f.x),f.y),
                            lerp(lerp(n001,n101,f.x),lerp(n011,n111,f.x),f.y),f.z);
            }
            float fbm(float3 p)
            {
                float v=0; float a=0.5; float fr=1;
                for(int i=0;i<5;i++){v+=a*noise3D(p*fr);fr*=2;a*=0.5;}
                return v;
            }
            float GetWarpedLandValue(float3 objNorm, float3 seedOff)
            {
                float3 samplePos = objNorm * _LandScale;
                float3 warp;
                warp.x = fbm(samplePos * 0.9 + float3(31.2, 11.7, 73.4) + seedOff);
                warp.y = fbm(samplePos * 0.9 + float3(8.4, 97.1, 45.6) + seedOff);
                warp.z = fbm(samplePos * 0.9 + float3(56.3, 24.9, 12.8) + seedOff);
                float3 warpedPos = samplePos + (warp - 0.5) * (_BiomeNoiseStrength * 8.0);
                return fbm(warpedPos + float3(42.3, 17.1, 83.7) + seedOff);
            }
            float GetLandMask(float3 objNorm, float3 seedOff)
            {
                float n = GetWarpedLandValue(objNorm, seedOff);
                return smoothstep(_LandThreshold - 0.04, _LandThreshold + 0.04, n);
            }
            float GetCapMask(float3 objNorm, float3 seedOff)
            {
                float latitude = abs(objNorm.y);
                float iceEdgeNoise = noise3D(objNorm * 6.0 + float3(11.1, 5.5, 22.2) + seedOff);
                float capStart = lerp(1.10, 0.15, _IceCapSize);
                return smoothstep(capStart - 0.10, capStart + 0.10, latitude + (iceEdgeNoise - 0.5) * 0.15);
            }
            float GetMountainMask(float3 objNorm, float landMask)
            {
                float3 seedOff = float3(_Seed, _Seed * 0.7, _Seed * 1.3);
                float broadElevationNoise = fbm(objNorm * (_LandScale * 1.2) + seedOff + float3(29.3, 17.7, 63.1));
                float ridge = 1.0 - abs(fbm(objNorm * (_LandScale * 4.6) + seedOff + float3(83.5, 9.2, 44.7)) * 2.0 - 1.0);
                ridge = pow(saturate(ridge), 2.5);
                float mountainMask = smoothstep(0.58, 0.85, ridge + broadElevationNoise * 0.35);
                mountainMask *= landMask;
                mountainMask *= smoothstep(0.35, 1.0, _Elevation);
                return mountainMask;
            }
            float GetTerrainHeightValue(float3 objNorm, float landMask, float capMask)
            {
                float3 seedOff = float3(_Seed, _Seed * 0.7, _Seed * 1.3);
                float hills = fbm(objNorm * (_LandScale * 2.2) + seedOff + float3(99.1, 55.3, 12.7));
                hills *= landMask;
                float mountainMask = GetMountainMask(objNorm, landMask);
                float volcanicMask = smoothstep(0.35, 1.0, _MapStyle) * mountainMask * fbm(objNorm * (_LandScale * 6.0) + seedOff + float3(4.4, 66.1, 27.8));
                float waterMask = 1.0 - landMask;
                return landMask * _LandUpliftStrength + hills * _HillDisplacementStrength + mountainMask * _MountainDisplacementStrength + capMask * _IceDisplacementStrength + volcanicMask * _VolcanicDisplacementStrength - waterMask * _OceanDepthStrength;
            }
            float GetPreviewDisplacementHeight(float3 objNorm)
            {
                float3 seedOff = float3(_Seed, _Seed * 0.7, _Seed * 1.3);
                float landMask = GetLandMask(objNorm, seedOff);
                float capMask = GetCapMask(objNorm, seedOff);
                float rawHeight = GetTerrainHeightValue(objNorm, landMask, capMask);
                return rawHeight * _DisplacementScale;
            }


            Varyings vertShadow(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float r = max(1e-5, length(input.positionOS.xyz));
                float3 n = input.positionOS.xyz / r;
                float disp = GetPreviewDisplacementHeight(n);
                float3 displaced = n * (r * (1.0 + disp));
                float3 worldPos = TransformObjectToWorld(displaced);
                output.positionCS = TransformWorldToHClip(worldPos);
                return output;
            }

            float4 fragShadow(Varyings input) : SV_Target
            {
                return 0;
            }

            ENDHLSL
        }
    }

    Fallback "Hidden/InternalErrorShader"
}
