Shader "Custom/MenuPlanetPreview"
{
    Properties
    {
        _LandScale("Land Scale", Range(0.5, 5.0)) = 2.0
        _LandThreshold("Land Threshold", Range(0, 1)) = 0.4
        _Temperature("Temperature", Range(0, 1)) = 0.5
        _Moisture("Moisture", Range(0, 1)) = 0.5
        _Elevation("Elevation", Range(0, 1)) = 0.3
            _BiomeTint("Biome Tint", Color) = (0.33,0.6,0.26,1)
            _DesertFactor("Desert Factor", Range(0,1)) = 0.0
            _TropicalFactor("Tropical Factor", Range(0,1)) = 0.0
            _SnowFactor("Snow Factor", Range(0,1)) = 0.0
        [Header(Biome Zone Colors)]
            _EquatorialColor("Equatorial (Desert/Jungle)", Color) = (0.01, 0.30, 0.04, 1)
            _DesertSand("Desert Sand", Color) = (0.96, 0.89, 0.65, 1)
            _SubtropicalColor("Subtropical (Savanna/Monsoon)", Color) = (0.82, 0.70, 0.25, 1)
            _TemperateColor("Temperate (Grassland/Forest)", Color) = (0.14, 0.68, 0.12, 1)
            _BorealColor("Boreal (Conifers)", Color) = (0.06, 0.25, 0.10, 1)
            _TundraColor("Tundra", Color) = (0.58, 0.50, 0.38, 1)
            _PolarColor("Polar Ice/Snow", Color) = (0.93, 0.95, 0.97, 1)
        [Header(Ocean Color)]
            _OceanColor("Ocean Color", Color) = (0.06, 0.22, 0.45, 1)
        [Header(Mountain Color)]
            _MountainColor("Mountain Color", Color) = (0.72, 0.58, 0.38, 1)
        [Header(Biome Tuning)]
            _IceCapSize("Ice Cap Size", Range(0, 1)) = 0.5
            _BiomeBlend("Biome Blend", Range(0, 1.0)) = 0.03
            _BiomeNoiseScale("Biome Noise Scale", Range(0, 10)) = 3.0
            _BiomeNoiseStrength("Biome Noise Strength", Range(0, 0.2)) = 0.08
            _ColorVibrancy("Color Vibrancy", Range(0.5, 2.0)) = 1.1
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
            _DisplacementScale("Displacement Scale", Range(0, 0.15)) = 0.035
        [Header(Surface)]
            _Smoothness("Smoothness", Range(0, 1)) = 0.3
            _Metallic("Metallic", Range(0, 1)) = 0.0
            _AmbientOcclusion("Ambient Occlusion", Range(0, 1)) = 1.0
            _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.12
            _Brightness("Brightness", Range(0.5, 3.0)) = 1.12
            _LandDetailTex("Land Detail", 2D) = "gray" {}
            _MountainDetailTex("Mountain Detail", 2D) = "gray" {}
            _IceDetailTex("Ice Detail", 2D) = "gray" {}
            _OceanDetailTex("Ocean Detail", 2D) = "gray" {}
            _OceanNormalTex("Ocean Normal", 2D) = "bump" {}
            _LandNormalTex("Land Normal", 2D) = "bump" {}
            _MountainNormalTex("Mountain Normal", 2D) = "bump" {}
            _IceNormalTex("Ice Normal", 2D) = "bump" {}
            _RoughnessDetailTex("Roughness Detail", 2D) = "gray" {}
            _VolcanicRockTex("Volcanic Rock", 2D) = "gray" {}
            _LavaCrackTex("Lava Crack", 2D) = "gray" {}
            _LavaEmissiveTex("Lava Emissive", 2D) = "white" {}
            _AshDetailTex("Ash Detail", 2D) = "gray" {}
            _LandDetailStrength("Land Detail Strength", Range(0,1)) = 0.1
            _MountainDetailStrength("Mountain Detail Strength", Range(0,1)) = 0.14
            _IceDetailStrength("Ice Detail Strength", Range(0,1)) = 0.12
            _OceanDetailStrength("Ocean Detail Strength", Range(0,1)) = 0.15
            _OceanNormalStrength("Ocean Normal Strength", Range(0,1)) = 0.1
            _LandNormalStrength("Land Normal Strength", Range(0,1)) = 0.12
            _MountainNormalStrength("Mountain Normal Strength", Range(0,1)) = 0.18
            _IceNormalStrength("Ice Normal Strength", Range(0,1)) = 0.22
            _TextureDetailScale("Texture Detail Scale", Range(0.1,30)) = 8
            _UseDetailTextures("Use Detail Textures", Float) = 0
            _UseTextureDrivenBiomes("Use Texture Driven Biomes", Float) = 1
            _EquatorialAlbedoTex("Equatorial Albedo", 2D) = "gray" {}
            _SubtropicalAlbedoTex("Subtropical Albedo", 2D) = "gray" {}
            _TemperateAlbedoTex("Temperate Albedo", 2D) = "gray" {}
            _BorealAlbedoTex("Boreal Albedo", 2D) = "gray" {}
            _TundraAlbedoTex("Tundra Albedo", 2D) = "gray" {}
            _PolarAlbedoTex("Polar Albedo", 2D) = "gray" {}
            _EquatorialNormalTex("Equatorial Normal", 2D) = "bump" {}
            _SubtropicalNormalTex("Subtropical Normal", 2D) = "bump" {}
            _TemperateNormalTex("Temperate Normal", 2D) = "bump" {}
            _BorealNormalTex("Boreal Normal", 2D) = "bump" {}
            _TundraNormalTex("Tundra Normal", 2D) = "bump" {}
            _PolarNormalTex("Polar Normal", 2D) = "bump" {}
            _BiomeTextureStrength("Biome Texture Strength", Range(0,1)) = 0.75
            _BiomeTintStrength("Biome Tint Strength", Range(0,1)) = 0.12
            _BiomeNormalStrength("Biome Normal Strength", Range(0,1)) = 0.18
            _BiomeTextureScale("Biome Texture Scale", Range(0.1,30)) = 6
            _BiomeTextureContrast("Biome Texture Contrast", Range(0,1)) = 0.18
            _ShowBiomeWeightsOnly("Show Biome Weights Only", Float) = 0
            _ShowBiomeTextureOnly("Show Biome Texture Only", Float) = 0
            _ShowBiomeTintOnly("Show Biome Tint Only", Float) = 0
            _ShowSmoothnessOnly("Show Smoothness Only", Float) = 0
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
                float _Elevation;
                float4 _BiomeTint;
                float _DesertFactor;
                float _TropicalFactor;
                float _SnowFactor;
                float4 _EquatorialColor;
                float4 _DesertSand;
                float4 _SubtropicalColor;
                float4 _TemperateColor;
                float4 _BorealColor;
                float4 _TundraColor;
                float4 _PolarColor;
                float4 _OceanColor;
                float4 _MountainColor;
                float _IceCapSize;
                float _BiomeBlend;
                float _BiomeNoiseScale;
                float _BiomeNoiseStrength;
                float _ColorVibrancy;
                float _Seed;
                float _MapStyle;
                    float _DetailScale;
                    float _DetailStrength;
                    float4 _AtmosphereColor;
                    float _AtmospherePower;
                    float _AtmosphereRadius;
                float _DisplacementScale;
                float _Smoothness;
                float _Metallic;
                float _AmbientOcclusion;
                float _AmbientStrength;
                float _Brightness;
                float _LandDetailStrength;
                float _MountainDetailStrength;
                float _IceDetailStrength;
                float _OceanDetailStrength;
                float _OceanNormalStrength;
                float _LandNormalStrength;
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
                float _ShowBiomeTintOnly;
                float _ShowSmoothnessOnly;
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
            TEXTURE2D(_LandDetailTex); SAMPLER(sampler_LandDetailTex);
            TEXTURE2D(_MountainDetailTex);
            TEXTURE2D(_IceDetailTex);
            TEXTURE2D(_OceanDetailTex);
            TEXTURE2D(_OceanNormalTex);
            TEXTURE2D(_LandNormalTex);
            TEXTURE2D(_MountainNormalTex);
            TEXTURE2D(_IceNormalTex);
            TEXTURE2D(_RoughnessDetailTex);
            TEXTURE2D(_EquatorialAlbedoTex);
            TEXTURE2D(_SubtropicalAlbedoTex);
            TEXTURE2D(_TemperateAlbedoTex);
            TEXTURE2D(_BorealAlbedoTex);
            TEXTURE2D(_TundraAlbedoTex);
            TEXTURE2D(_PolarAlbedoTex);
            TEXTURE2D(_EquatorialNormalTex);
            TEXTURE2D(_SubtropicalNormalTex);
            TEXTURE2D(_TemperateNormalTex);
            TEXTURE2D(_BorealNormalTex);
            TEXTURE2D(_TundraNormalTex);
            TEXTURE2D(_PolarNormalTex);
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
            float3 GetLandColor(float lat, float tempShift, float moist, float3 objNorm)
            {
                // Latitude-based biome bands with noise-perturbed edges.
                // lat = 0.0 at equator, 1.0 at poles.
                // tempShift shifts band edges; moist controls within-band palette.
                // objNorm is used for 3D noise to break up straight latitude lines.
                //
                // Band layout (at neutral tempShift=0):
                //   lat 0.00-0.15 : EQUATORIAL (desert <-> jungle)
                //   lat 0.15-0.30 : SUBTROPICAL (savanna <-> monsoon)
                //   lat 0.30-0.50 : TEMPERATE (grassland <-> forest)
                //   lat 0.50-0.65 : BOREAL (dark conifers)
                //   lat 0.65-0.80 : TUNDRA (barren gray-brown)
                //   lat 0.80-1.00 : POLAR ICE

                // ---- ALL colors from inspector ----
                float3 desertSand   = _DesertSand.rgb;
                float3 desertRed    = desertSand * float3(0.92, 0.58, 0.34); // derived arid variant
                float3 jungleDeep   = _EquatorialColor.rgb;
                float3 savannaBase  = _SubtropicalColor.rgb;
                float3 jungleLush   = jungleDeep * 1.3;
                float3 tempBase     = _TemperateColor.rgb;
                float3 tempDry      = tempBase * 0.7 + float3(0.10, 0.08, 0.02); // drier variant
                float3 borealBase   = _BorealColor.rgb;
                float3 borealLight  = borealBase * 1.5;
                float3 tundraBase   = _TundraColor.rgb;
                float3 tundraGray   = tundraBase * 0.85 + float3(0.0, 0.0, 0.04);
                float3 snowWhite    = _PolarColor.rgb;

                // Clamp temperature shift so bands never fully vanish off the sphere.
                float shift = clamp(tempShift * 0.6, -0.25, 0.25);

                // --- Noise perturbation of latitude for organic, wavy band edges ---
                float3 seedOffset = float3(_Seed, _Seed * 0.7, _Seed * 1.3);
                float latNoise = (fbm(objNorm * _BiomeNoiseScale + seedOffset + float3(55.5, 22.2, 88.8)) - 0.5)
                               * _BiomeNoiseStrength * 2.0;
                float sLat = lat - shift + latNoise;

                // --- Moisture-driven color within each band ---
                float3 equatC  = lerp(lerp(desertSand, desertRed, 0.4), jungleDeep, moist);
                float3 subtrC  = lerp(savannaBase, jungleLush, moist);
                float3 tempC   = lerp(tempDry, tempBase, moist);
                float3 borealC = lerp(borealBase, borealLight, moist);
                float3 tundraC = lerp(tundraBase, tundraGray, moist);

                // --- Moisture-driven band expansion: global dryness should dominate the whole planet ---
                float globalDry = pow(saturate(1.0 - _Moisture), 0.65);
                float bandPush = globalDry * 0.42;

                // On very dry worlds, push arid/subtropical bands far toward the poles.
                float e0 = 0.12 + bandPush;
                float e1 = 0.24 + bandPush * 0.78;
                float e2 = 0.42 + bandPush * 0.55;
                float e3 = 0.60 + bandPush * 0.28;
                float e4 = 0.80 + bandPush * 0.10;
                float b = max(0.001, _BiomeBlend); // blend half-width

                // Blend between adjacent bands using configurable transition width
                float3 result;
                if (sLat < e0 - b) result = equatC;
                else if (sLat < e0 + b) result = lerp(equatC, subtrC, smoothstep(e0 - b, e0 + b, sLat));
                else if (sLat < e1 - b) result = subtrC;
                else if (sLat < e1 + b) result = lerp(subtrC, tempC, smoothstep(e1 - b, e1 + b, sLat));
                else if (sLat < e2 - b) result = tempC;
                else if (sLat < e2 + b) result = lerp(tempC, borealC, smoothstep(e2 - b, e2 + b, sLat));
                else if (sLat < e3 - b) result = borealC;
                else if (sLat < e3 + b) result = lerp(borealC, tundraC, smoothstep(e3 - b, e3 + b, sLat));
                else if (sLat < e4 - b) result = tundraC;
                else if (sLat < e4 + b) result = lerp(tundraC, snowWhite, smoothstep(e4 - b, e4 + b, sLat));
                else result = snowWhite;

                // Boost vibrancy: push colors away from gray toward their hue
                float gray = dot(result, float3(0.299, 0.587, 0.114));
                result = lerp(float3(gray, gray, gray), result, lerp(1.0, _ColorVibrancy, 0.35));

                return result;
            }

            float3 GetOceanColor(float temp)
            {
                return _OceanColor.rgb;
            }
            struct BiomeWeights { float equatorial; float subtropical; float temperate; float boreal; float tundra; float polar; };
            BiomeWeights GetBiomeWeights(float lat, float tempShift, float localMoist, float3 objNorm)
            {
                BiomeWeights w = (BiomeWeights)0;
                float shift = clamp(tempShift * 0.6, -0.25, 0.25);
                float3 seedOffset = float3(_Seed, _Seed * 0.7, _Seed * 1.3);
                float latNoise = (fbm(objNorm * _BiomeNoiseScale + seedOffset + float3(55.5, 22.2, 88.8)) - 0.5) * _BiomeNoiseStrength * 2.0;
                float sLat = lat - shift + latNoise;
                float globalDry = pow(saturate(1.0 - _Moisture), 0.65);
                float bandPush = globalDry * 0.42;
                float e0 = 0.12 + bandPush; float e1 = 0.24 + bandPush * 0.78; float e2 = 0.42 + bandPush * 0.55; float e3 = 0.60 + bandPush * 0.28; float e4 = 0.80 + bandPush * 0.10;
                float b = max(0.001, _BiomeBlend);
                w.equatorial = 1.0 - smoothstep(e0 - b, e0 + b, sLat);
                w.subtropical = smoothstep(e0 - b, e0 + b, sLat) * (1.0 - smoothstep(e1 - b, e1 + b, sLat));
                w.temperate = smoothstep(e1 - b, e1 + b, sLat) * (1.0 - smoothstep(e2 - b, e2 + b, sLat));
                w.boreal = smoothstep(e2 - b, e2 + b, sLat) * (1.0 - smoothstep(e3 - b, e3 + b, sLat));
                w.tundra = smoothstep(e3 - b, e3 + b, sLat) * (1.0 - smoothstep(e4 - b, e4 + b, sLat));
                w.polar = smoothstep(e4 - b, e4 + b, sLat);
                return w;
            }
            float3 GetTextureBiomeAlbedo(BiomeWeights w, float3 colorBiome, float3 positionOS, float3 objNorm)
            {
                float3 eq = SampleTriplanarScaled(TEXTURE2D_ARGS(_EquatorialAlbedoTex, sampler_LandDetailTex), positionOS, objNorm, _BiomeTextureScale);
                float3 st = SampleTriplanarScaled(TEXTURE2D_ARGS(_SubtropicalAlbedoTex, sampler_LandDetailTex), positionOS, objNorm, _BiomeTextureScale);
                float3 te = SampleTriplanarScaled(TEXTURE2D_ARGS(_TemperateAlbedoTex, sampler_LandDetailTex), positionOS, objNorm, _BiomeTextureScale);
                float3 bo = SampleTriplanarScaled(TEXTURE2D_ARGS(_BorealAlbedoTex, sampler_LandDetailTex), positionOS, objNorm, _BiomeTextureScale);
                float3 tu = SampleTriplanarScaled(TEXTURE2D_ARGS(_TundraAlbedoTex, sampler_LandDetailTex), positionOS, objNorm, _BiomeTextureScale);
                float3 po = SampleTriplanarScaled(TEXTURE2D_ARGS(_PolarAlbedoTex, sampler_LandDetailTex), positionOS, objNorm, _BiomeTextureScale);
                float3 texBiome = eq*w.equatorial + st*w.subtropical + te*w.temperate + bo*w.boreal + tu*w.tundra + po*w.polar;
                float texLuma = dot(texBiome, float3(0.299,0.587,0.114));
                texBiome = lerp(texBiome, texBiome * (1.0 + (texLuma - 0.5) * _BiomeTextureContrast), _BiomeTextureContrast);
                float3 tintedTexture = lerp(texBiome, texBiome * colorBiome, _BiomeTintStrength);
                return lerp(colorBiome, tintedTexture, _BiomeTextureStrength);
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
            //  Vertex displacement helper
            // -----------------------------------------------------------------
            float GetDisplacement(float3 objNorm)
            {
                float3 seedOff = float3(_Seed, _Seed * 0.7, _Seed * 1.3);
                float3 samplePos = objNorm * _LandScale;
                float n = GetWarpedLandValue(objNorm, seedOff);

                // Wide transition (0.12 each side) so coastlines are gentle slopes, not cliffs
                float edge = smoothstep(_LandThreshold - 0.12, _LandThreshold + 0.12, n);

                // --- Ice cap mask (match fragment logic) ---
                // Compute latitude and an ice-edge noise to allow jagged ice boundaries
                float latitude = abs(objNorm.y);
                float iceEdgeNoise = noise3D(objNorm * 6.0 + float3(11.1, 5.5, 22.2) + seedOff);
                float capStart = lerp(1.10, 0.15, _IceCapSize);
                float capMask = smoothstep(capStart - 0.10, capStart + 0.10, latitude + (iceEdgeNoise - 0.5) * 0.15);

                // Combine land edge and ice cap mask so ice caps also receive displacement
                float combinedEdge = max(edge, capMask);

                float elevNoise = fbm(samplePos * 1.5 + float3(99.1, 55.3, 12.7) + seedOff);
                // Displace land and ice outward; oceans stay at base radius
                return combinedEdge * elevNoise * _DisplacementScale;
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

                float disp = GetDisplacement(objNorm);
                float3 displacedOS = objNorm * (baseRadius * (1.0 + disp));

                // Compute displaced normal from displaced positions (more stable than
                // trying to perturb the normal directly).
                float eps = 0.012;
                float3 tangent1 = normalize(cross(objNorm, abs(objNorm.y) < 0.99 ? float3(0,1,0) : float3(1,0,0)));
                float3 tangent2 = cross(objNorm, tangent1);

                float3 nU = normalize(objNorm + tangent1 * eps);
                float3 nV = normalize(objNorm + tangent2 * eps);
                float dispU = GetDisplacement(nU);
                float dispV = GetDisplacement(nV);
                float3 p  = displacedOS;
                float3 pU = nU * (baseRadius * (1.0 + dispU));
                float3 pV = nV * (baseRadius * (1.0 + dispV));
                float3 dispNormalOS = normalize(cross(pV - p, pU - p));

                float3 worldPos = TransformObjectToWorld(displacedOS);
                output.positionCS = TransformWorldToHClip(worldPos);
                output.positionWS = worldPos;
                output.normalWS   = TransformObjectToWorldNormal(dispNormalOS);
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
                float moistureBase = lerp(-0.45, 1.10, saturate(_Moisture));
                float moistLatitude = cos(latitude * 3.14159 * 2.0) * lerp(0.03, 0.16, saturate(_Moisture));
                float moistNoise = (noise3D(objNorm * 5.0 + float3(77.7, 33.3, 11.1) + seedOff) - 0.5) * lerp(0.02, 0.08, saturate(_Moisture));
                float localMoist = saturate(moistureBase + moistLatitude + moistNoise);

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
                float3 landColor  = GetLandColor(latitude, tempShift, localMoist, objNorm);
                BiomeWeights biomeWeights = GetBiomeWeights(latitude, tempShift, localMoist, objNorm);
                float3 textureBiomeColor = GetTextureBiomeAlbedo(biomeWeights, landColor, input.positionOS, objNorm);
                landColor = (_UseTextureDrivenBiomes > 0.5) ? textureBiomeColor : landColor;

                // Uniform ocean color — single inspector-driven color, no depth/latitude variation
                float3 oceanColor = _OceanColor.rgb;

                // GetLandColor already handles all biome band coloring —
                // no additional desert/tropical tint overlays needed.
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

                // _BiomeTint used only as a very subtle hint — per-pixel latitude colors dominate
                landColor = lerp(landColor, _BiomeTint.rgb, 0.05);

                // Mountains use their own inspector color (cartographic style)
                float3 mountainColor = _MountainColor.rgb;
                float3 snowPeakColor = float3(0.92, 0.93, 0.96);

                float3 elevatedLand = landColor;
                // Mountains blend in at high elevation — distinct color like a map
                float mtnBlend = smoothstep(0.35, 0.60, terrainHeight);
                elevatedLand = lerp(elevatedLand, mountainColor, mtnBlend);
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
                    float3 landDetail = SampleTriplanar(TEXTURE2D_ARGS(_LandDetailTex, sampler_LandDetailTex), input.positionOS, objNorm);
                    float3 mtnDetail = SampleTriplanar(TEXTURE2D_ARGS(_MountainDetailTex, sampler_LandDetailTex), input.positionOS, objNorm);
                    float3 ocnDetail = SampleTriplanar(TEXTURE2D_ARGS(_OceanDetailTex, sampler_LandDetailTex), input.positionOS, objNorm);
                    float landFactor = (dot(landDetail, float3(0.333,0.333,0.333)) - 0.5) * 2.0;
                    float mtnFactor = (dot(mtnDetail, float3(0.333,0.333,0.333)) - 0.5) * 2.0;
                    float ocnFactor = (dot(ocnDetail, float3(0.333,0.333,0.333)) - 0.5) * 2.0;
                    elevatedLand *= (1.0 + landFactor * _LandDetailStrength * edge);
                    elevatedLand *= (1.0 + mtnFactor * _MountainDetailStrength * mtnBlend * edge);
                    oceanColor *= (1.0 + ocnFactor * _OceanDetailStrength * (1.0 - edge));
                    normalAlbedo = lerp(oceanColor, elevatedLand, edge);
                }

                // Normal rivers (moisture-gated, not on mountains)
                float normalRiverMask = riverMask * saturate((localMoist - 0.20) * 2.0)
                                      * saturate(1.0 - mtnBand * 0.8);
                normalAlbedo = lerp(normalAlbedo, float3(0.10, 0.25, 0.45), saturate(normalRiverMask));

                // Lakes (normal)
                float lakeNoise = noise3D(samplePos * 12.0 + float3(7.7, 3.3, 9.9) + seedOff);
                float lakeMask  = smoothstep(0.72, 0.78, lakeNoise)
                                * saturate((localMoist - 0.6) * 2.5) * step(0.5, edge);
                normalAlbedo = lerp(normalAlbedo, float3(0.12, 0.30, 0.50), saturate(lakeMask));

                // ---- Ice caps ----
                // Driven entirely by _IceCapSize (set by the climate preset in MainMenuManager).
                // 0 = no ice,  1 = massive polar ice extending near equator.
                // No temperature math — the preset already encodes how icy the planet should be.

                float capStart = lerp(1.10, 0.15, _IceCapSize); // 0→above poles (none), 1→near equator

                float iceEdgeNoise = noise3D(objNorm * 6.0 + float3(11.1, 5.5, 22.2) + seedOff);
                float capMask = smoothstep(capStart - 0.10, capStart + 0.10, latitude + (iceEdgeNoise - 0.5) * 0.15);

                // Ice/snow color — white at center, icy blue at outer fringe
                float3 snowWhite = _PolarColor.rgb;
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
                    float3 iceDetail = SampleTriplanar(TEXTURE2D_ARGS(_IceDetailTex, sampler_LandDetailTex), input.positionOS, objNorm);
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

                            float3 volcanicSample = SampleTriplanarScaled(TEXTURE2D_ARGS(_VolcanicRockTex, sampler_LandDetailTex), input.positionOS, objNorm, _LavaTextureScale);
                float lavaMaskSample = SampleTriplanarScaled(TEXTURE2D_ARGS(_LavaCrackTex, sampler_LandDetailTex), input.positionOS, objNorm, _LavaTextureScale).r;
                float lavaEmissiveSample = SampleTriplanarScaled(TEXTURE2D_ARGS(_LavaEmissiveTex, sampler_LandDetailTex), input.positionOS, objNorm, _LavaTextureScale).r;
                float ashSample = SampleTriplanarScaled(TEXTURE2D_ARGS(_AshDetailTex, sampler_LandDetailTex), input.positionOS, objNorm, _LavaTextureScale).r;
                float fallbackCrack = smoothstep(0.46, 0.50, noise3D(samplePos * 7.0 + float3(66.6, 13.1, 99.9) + seedOff));
                float lavaMask = max(lavaMaskSample, fallbackCrack * 0.75);
                float lavaEmissionMask = max(lavaEmissiveSample, lavaMask);
                float volcanic = dot(volcanicSample, float3(0.333, 0.333, 0.333));
                float landMask = saturate(edge * (1.0 - capMask * 0.35));
                float crack = smoothstep(0.55, 0.9, lavaMask) * landMask * infernal * _LavaCrackStrength;
                float emissiveCrack = crack * lavaEmissionMask;
                float volcanicDarken = lerp(0.25, 0.55, volcanic);
                float3 volcanicColor = lerp(albedo, albedo * volcanicDarken, _VolcanicRockStrength * infernal);
                albedo = lerp(albedo, volcanicColor, infernal);
                float ashMask = ashSample * _AshDetailStrength * infernal * saturate(0.4 + landMask + capMask * 0.4);
                float ashGray = dot(albedo, float3(0.299, 0.587, 0.114));
                albedo = lerp(albedo, float3(ashGray, ashGray, ashGray), ashMask * 0.35);

                float roughMask = SampleTriplanar(TEXTURE2D_ARGS(_RoughnessDetailTex, sampler_LandDetailTex), input.positionOS, objNorm).r;
                float landSmooth = lerp(_Smoothness * 0.75, _Smoothness * 1.15, roughMask);
                float oceanSmooth = lerp(0.85, 1.05, roughMask) * saturate(_Smoothness + 0.35);
                float iceSmooth = lerp(0.35, 0.65, roughMask);
                float mountainSmooth = landSmooth * 0.7;
                float smoothnessMask = lerp(oceanSmooth, landSmooth, edge);
                smoothnessMask = lerp(smoothnessMask, mountainSmooth, mtnBlend * edge);
                smoothnessMask = lerp(smoothnessMask, iceSmooth, capMask);

                // ==============================================================
                //  Custom lighting using blended triplanar normals
                // ==============================================================
                float3 oceanN = SampleTriplanar(TEXTURE2D_ARGS(_OceanNormalTex, sampler_LandDetailTex), input.positionOS, objNorm) * 2.0 - 1.0;
                float3 landN = SampleTriplanar(TEXTURE2D_ARGS(_LandNormalTex, sampler_LandDetailTex), input.positionOS, objNorm) * 2.0 - 1.0;
                float3 mtnN = SampleTriplanar(TEXTURE2D_ARGS(_MountainNormalTex, sampler_LandDetailTex), input.positionOS, objNorm) * 2.0 - 1.0;
                float3 iceN = SampleTriplanar(TEXTURE2D_ARGS(_IceNormalTex, sampler_LandDetailTex), input.positionOS, objNorm) * 2.0 - 1.0;
                float3 eqN = SampleTriplanarScaled(TEXTURE2D_ARGS(_EquatorialNormalTex, sampler_LandDetailTex), input.positionOS, objNorm, _BiomeTextureScale) * 2.0 - 1.0;
                float3 stN = SampleTriplanarScaled(TEXTURE2D_ARGS(_SubtropicalNormalTex, sampler_LandDetailTex), input.positionOS, objNorm, _BiomeTextureScale) * 2.0 - 1.0;
                float3 teN = SampleTriplanarScaled(TEXTURE2D_ARGS(_TemperateNormalTex, sampler_LandDetailTex), input.positionOS, objNorm, _BiomeTextureScale) * 2.0 - 1.0;
                float3 boN = SampleTriplanarScaled(TEXTURE2D_ARGS(_BorealNormalTex, sampler_LandDetailTex), input.positionOS, objNorm, _BiomeTextureScale) * 2.0 - 1.0;
                float3 tuN = SampleTriplanarScaled(TEXTURE2D_ARGS(_TundraNormalTex, sampler_LandDetailTex), input.positionOS, objNorm, _BiomeTextureScale) * 2.0 - 1.0;
                float3 poN = SampleTriplanarScaled(TEXTURE2D_ARGS(_PolarNormalTex, sampler_LandDetailTex), input.positionOS, objNorm, _BiomeTextureScale) * 2.0 - 1.0;
                float3 biomeN = normalize(eqN*biomeWeights.equatorial + stN*biomeWeights.subtropical + teN*biomeWeights.temperate + boN*biomeWeights.boreal + tuN*biomeWeights.tundra + poN*biomeWeights.polar);
                float3 surfN = normal;
                surfN = normalize(lerp(surfN, normalize(input.normalWS + oceanN), (1.0-edge) * _OceanNormalStrength));
                surfN = normalize(lerp(surfN, normalize(input.normalWS + biomeN), edge * _BiomeNormalStrength * (_UseTextureDrivenBiomes > 0.5 ? 1.0 : 0.0)));
                surfN = normalize(lerp(surfN, normalize(input.normalWS + landN), edge * _LandNormalStrength));
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
                    d += SampleTriplanar(TEXTURE2D_ARGS(_LandDetailTex, sampler_LandDetailTex), input.positionOS, objNorm) * edge;
                    d += SampleTriplanar(TEXTURE2D_ARGS(_OceanDetailTex, sampler_LandDetailTex), input.positionOS, objNorm) * (1.0-edge);
                    return float4(saturate(d), 1.0);
                }
                if (_ShowNormalsOnly > 0.5) return float4(surfN * 0.5 + 0.5, 1.0);
                if (_ShowBiomeWeightsOnly > 0.5) return float4(saturate(float3(biomeWeights.equatorial + biomeWeights.polar * 0.5, biomeWeights.temperate + biomeWeights.subtropical * 0.5, biomeWeights.boreal + biomeWeights.tundra)), 1.0);
                if (_ShowBiomeTextureOnly > 0.5) return float4(saturate(textureBiomeColor), 1.0);
                if (_ShowBiomeTintOnly > 0.5) return float4(saturate(GetLandColor(latitude, tempShift, localMoist, objNorm)), 1.0);
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
                float _Elevation;
                float4 _BiomeTint;
                float _DesertFactor;
                float _TropicalFactor;
                float _SnowFactor;
                float4 _EquatorialColor;
                float4 _DesertSand;
                float4 _SubtropicalColor;
                float4 _TemperateColor;
                float4 _BorealColor;
                float4 _TundraColor;
                float4 _PolarColor;
                float4 _OceanColor;
                float4 _MountainColor;
                float _IceCapSize;
                float _BiomeBlend;
                float _BiomeNoiseScale;
                float _BiomeNoiseStrength;
                float _ColorVibrancy;
                float _Seed;
                float _MapStyle;
                float _DetailScale;
                float _DetailStrength;
                float4 _AtmosphereColor;
                float _AtmospherePower;
                float _AtmosphereRadius;
                float _DisplacementScale;
                float _Smoothness;
                float _Metallic;
                float _AmbientOcclusion;
                float _AmbientStrength;
                float _Brightness;
            CBUFFER_END

            // Inline noise for displacement (same as main pass)
            float hash31_d(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.x + p.y) * p.z);
            }
            float noise3D_d(float3 p)
            {
                float3 i = floor(p); float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = hash31_d(i); float n100 = hash31_d(i + float3(1,0,0));
                float n010 = hash31_d(i + float3(0,1,0)); float n110 = hash31_d(i + float3(1,1,0));
                float n001 = hash31_d(i + float3(0,0,1)); float n101 = hash31_d(i + float3(1,0,1));
                float n011 = hash31_d(i + float3(0,1,1)); float n111 = hash31_d(i + float3(1,1,1));
                return lerp(lerp(lerp(n000,n100,f.x),lerp(n010,n110,f.x),f.y),
                            lerp(lerp(n001,n101,f.x),lerp(n011,n111,f.x),f.y),f.z);
            }
            float fbm_d(float3 p)
            {
                float v=0; float a=0.5; float fr=1;
                for(int i=0;i<4;i++){v+=a*noise3D_d(p*fr);fr*=2;a*=0.5;}
                return v;
            }
            float GetDisp(float3 objNorm)
            {
                float3 seedOff = float3(_Seed, _Seed * 0.7, _Seed * 1.3);
                float3 sp = objNorm * _LandScale;
                float n = fbm_d(sp + float3(42.3,17.1,83.7) + seedOff);
                float edge = smoothstep(_LandThreshold-0.12,_LandThreshold+0.12,n);
                float elev = fbm_d(sp*1.5+float3(99.1,55.3,12.7) + seedOff);
                return edge*elev*_DisplacementScale;
            }

            Varyings vertDepth(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float r = max(1e-5, length(input.positionOS.xyz));
                float3 n = input.positionOS.xyz / r;
                float d = GetDisp(n);
                float3 displaced = n * (r * (1.0 + d));
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
                float _Elevation;
                float4 _BiomeTint;
                float _DesertFactor;
                float _TropicalFactor;
                float _SnowFactor;
                float4 _EquatorialColor;
                float4 _DesertSand;
                float4 _SubtropicalColor;
                float4 _TemperateColor;
                float4 _BorealColor;
                float4 _TundraColor;
                float4 _PolarColor;
                float4 _OceanColor;
                float4 _MountainColor;
                float _IceCapSize;
                float _BiomeBlend;
                float _BiomeNoiseScale;
                float _BiomeNoiseStrength;
                float _ColorVibrancy;
                float _Seed;
                float _MapStyle;
                float _DetailScale;
                float _DetailStrength;
                float4 _AtmosphereColor;
                float _AtmospherePower;
                float _AtmosphereRadius;
                float _DisplacementScale;
                float _Smoothness;
                float _Metallic;
                float _AmbientOcclusion;
                float _AmbientStrength;
                float _Brightness;
            CBUFFER_END

            float hash31_s(float3 p){p=frac(p*float3(0.1031,0.1030,0.0973));p+=dot(p,p.yxz+33.33);return frac((p.x+p.y)*p.z);}
            float noise3D_s(float3 p){float3 i=floor(p);float3 f=frac(p);f=f*f*(3.0-2.0*f);
                return lerp(lerp(lerp(hash31_s(i),hash31_s(i+float3(1,0,0)),f.x),lerp(hash31_s(i+float3(0,1,0)),hash31_s(i+float3(1,1,0)),f.x),f.y),
                            lerp(lerp(hash31_s(i+float3(0,0,1)),hash31_s(i+float3(1,0,1)),f.x),lerp(hash31_s(i+float3(0,1,1)),hash31_s(i+float3(1,1,1)),f.x),f.y),f.z);}
            float fbm_s(float3 p){float v=0;float a=0.5;float fr=1;for(int i=0;i<4;i++){v+=a*noise3D_s(p*fr);fr*=2;a*=0.5;}return v;}
            float GetDispS(float3 n){float3 seedOff=float3(_Seed,_Seed*0.7,_Seed*1.3);float3 sp=n*_LandScale;float e=smoothstep(_LandThreshold-0.12,_LandThreshold+0.12,fbm_s(sp+float3(42.3,17.1,83.7)+seedOff));
                return e*fbm_s(sp*1.5+float3(99.1,55.3,12.7)+seedOff)*_DisplacementScale;}

            Varyings vertShadow(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float r = max(1e-5, length(input.positionOS.xyz));
                float3 n = input.positionOS.xyz / r;
                float3 displaced = n * (r * (1.0 + GetDispS(n)));
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
