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
            _OceanShallowColor("Ocean Shallow Color", Color) = (0.12, 0.36, 0.62, 1)
        [Header(Biome Tuning)]
            _IceCapSize("Ice Cap Size", Range(0, 1)) = 0.5
            _BiomeBlend("Biome Blend", Range(0, 1.0)) = 0.03
            _BiomeNoiseScale("Biome Noise Scale", Range(0, 10)) = 3.0
            _BiomeNoiseStrength("Biome Noise Strength", Range(0, 0.2)) = 0.08
        [Header(Seed)]
            _Seed("Planet Seed", Float) = 0.0
            _MoistureResponseScale("Moisture Response Scale", Range(0.5,1.2)) = 0.85
            _TemperatureHumidityInfluence("Temperature Humidity Influence", Range(0,0.2)) = 0.06
            _ClimateNoiseStrength("Climate Noise Strength", Range(0,0.3)) = 0.12
            _CoastWetnessStrength("Coast Wetness Strength", Range(0,0.3)) = 0.08
            _ContinentalDrynessStrength("Continental Dryness Strength", Range(0,0.3)) = 0.08
            _ContinentalTemperatureStrength("Continental Temperature Strength", Range(0,0.3)) = 0.06
            _RainShadowStrength("Rain Shadow Strength", Range(0,0.3)) = 0.16
            _OrographicWetnessStrength("Orographic Wetness Strength", Range(0,0.3)) = 0.08
            _OrographicSampleOffset("Orographic Sample Offset", Range(0.01,0.2)) = 0.08
            _RiparianWetnessStrength("Riparian Wetness Strength", Range(0,0.3)) = 0.12
            _SeasonalityStrength("Seasonality Strength", Range(0,1)) = 0.35
            _BiomeProvinceStrength("Biome Province Strength", Range(0,0.5)) = 0.2
            _BiomeCompetitionSharpness("Biome Competition Sharpness", Range(0.5,1.25)) = 0.85
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
            _WaterwayDetailTex("Waterway Detail", 2D) = "gray" {}
            _WaterwayMaskTex("Waterway Mask Texture", 2D) = "black" {}
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
            _TaigaAlbedoTex("Taiga Albedo", 2D) = "gray" {}
            _TundraAlbedoTex("Tundra Albedo", 2D) = "gray" {}
            _PolarAlbedoTex("Polar Albedo", 2D) = "gray" {}
            _MarshAlbedoTex("Marsh Albedo", 2D) = "gray" {}
            _JungleNormalTex("Jungle Normal", 2D) = "bump" {}
            _DesertNormalTex("Desert Normal", 2D) = "bump" {}
            _SavannaNormalTex("Savanna Normal", 2D) = "bump" {}
            _TemperateGrassNormalTex("Temperate Grass Normal", 2D) = "bump" {}
            _TemperateForestNormalTex("Temperate Forest Normal", 2D) = "bump" {}
            _TaigaNormalTex("Taiga Normal", 2D) = "bump" {}
            _TundraNormalTex("Tundra Normal", 2D) = "bump" {}
            _PolarNormalTex("Polar Normal", 2D) = "bump" {}
            _MarshNormalTex("Marsh Normal", 2D) = "bump" {}
            _TaigaSmoothnessTex("Taiga Smoothness", 2D) = "gray" {}
            _BiomeTextureStrength("Biome Texture Strength", Range(0,1)) = 0.75
            _BiomeTintStrength("Biome Tint Strength", Range(0,1)) = 0.12
            _BiomeNormalStrength("Biome Normal Strength", Range(0,1)) = 0.18
            _BiomeTextureScale("Biome Texture Scale", Range(0.1,30)) = 6
            _BiomeTextureContrast("Biome Texture Contrast", Range(0,1)) = 0.18
            _ShowBiomeWeightsOnly("Show Biome Weights Only", Float) = 0
            _ShowBiomeTextureOnly("Show Biome Texture Only", Float) = 0
            _ShowSmoothnessOnly("Show Smoothness Only", Float) = 0
            _ShowLocalMoistureOnly("Show Local Moisture Only", Float) = 0
            _ShowLocalTemperatureOnly("Show Local Temperature Only", Float) = 0
            _ShowContinentalityOnly("Show Continentality Only", Float) = 0
            _ShowSeasonalityOnly("Show Seasonality Only", Float) = 0
            _ShowRainShadowOnly("Show Rain Shadow Only", Float) = 0
            _ShowRiparianWetnessOnly("Show Riparian Wetness Only", Float) = 0
            _ShowDominantBiomeOnly("Show Dominant Biome Only", Float) = 0
            _ShowWaterwaysOnly("Show Waterways Only", Float) = 0
            _ShowWaterwayAmountOnly("Show Waterway Amount Only", Float) = 0
            _ShowRiverMaskOnly("Show River Mask Only", Float) = 0
            _ShowLakeMaskOnly("Show Lake Mask Only", Float) = 0
            _ShowCloudShadowMaskOnly("Show Cloud Shadow Mask Only", Float) = 0
            _ShowCoastShelfMaskOnly("Show Coast Shelf Mask Only", Float) = 0
            _ShowShorelineMaskOnly("Show Shoreline Mask Only", Float) = 0
            _ShowWetlandMaskOnly("Show Wetland Mask Only", Float) = 0
            _ShowWaterDepthMaskOnly("Show Water Depth Mask Only", Float) = 0
            _TerminatorSoftness("Terminator Softness", Range(0.05,1)) = 0.45
            _ShowLandMaskOnly("Show Land Mask Only", Float) = 0
            _ShowDetailTexturesOnly("Show Detail Textures Only", Float) = 0
            _ShowNormalsOnly("Show Normals Only", Float) = 0
            _VolcanicRockStrength("Volcanic Rock Strength", Range(0,1)) = 0.35
            _LavaCrackStrength("Lava Crack Strength", Range(0,1)) = 0.65
            _LavaEmissionStrength("Lava Emission Strength", Range(0,5)) = 2.2
            _LavaTextureScale("Lava Texture Scale", Range(0.1,30)) = 10
            _AshDetailStrength("Ash Detail Strength", Range(0,1)) = 0.25
            [HideInInspector] _KeyLightDirectionWS("Key Light Direction WS", Vector) = (0.45,0.65,0.55,0)
            [HideInInspector] _KeyLightColor("Key Light Color", Color) = (1,1,1,1)
            [HideInInspector] _KeyLightIntensity("Key Light Intensity", Float) = 1
            _CloudShadowDensity("Cloud Shadow Density", Float) = 0.55
            _CloudShadowScale("Cloud Shadow Scale", Float) = 3.0
            _CloudShadowSpeed("Cloud Shadow Speed", Float) = 0.08
            _CloudSurfaceShadowStrength("Cloud Surface Shadow Strength", Range(0,0.35)) = 0.12
            [HideInInspector] _FillLightColor("Fill Light Color", Color) = (1,1,1,1)
            [HideInInspector] _FillLightIntensity("Fill Light Intensity", Float) = 0.35
            [HideInInspector] _RimLightColor("Rim Light Color", Color) = (1,1,1,1)
            [HideInInspector] _RimLightIntensity("Rim Light Intensity", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _LandScale; float _LandThreshold; float _Temperature; float _Moisture; float _WaterwayAmount; float _Elevation;
                float _DesertFactor; float _TropicalFactor; float _SnowFactor; float4 _OceanColor; float4 _OceanShallowColor;
                float _IceCapSize; float _BiomeBlend; float _BiomeNoiseScale; float _BiomeNoiseStrength;
                float _Seed; float _MapStyle; float _DetailScale; float _DetailStrength; float4 _AtmosphereColor; float _AtmospherePower; float _AtmosphereRadius;
                float _DisplacementScale; float _LandUpliftStrength; float _HillDisplacementStrength; float _MountainDisplacementStrength; float _IceDisplacementStrength; float _VolcanicDisplacementStrength; float _OceanDepthStrength;
                float _ShowElevationOnly; float _ShowMountainMaskOnly; float _ShowDisplacementHeightOnly; float _UseDisplacedNormals;
                float _Smoothness; float _Metallic; float _AmbientOcclusion; float _AmbientStrength; float _Brightness;
                float _MountainDetailStrength; float _IceDetailStrength; float _OceanDetailStrength; float _OceanNormalStrength; float _MountainNormalStrength; float _IceNormalStrength;
                float _TextureDetailScale; float _UseDetailTextures; float _UseTextureDrivenBiomes; float _BiomeTextureStrength; float _BiomeTintStrength; float _BiomeNormalStrength; float _BiomeTextureScale; float _BiomeTextureContrast;
                float _ShowBiomeWeightsOnly; float _ShowBiomeTextureOnly; float _ShowSmoothnessOnly; float _ShowLocalMoistureOnly; float _ShowLocalTemperatureOnly; float _ShowContinentalityOnly; float _ShowSeasonalityOnly; float _ShowRainShadowOnly; float _ShowRiparianWetnessOnly; float _ShowDominantBiomeOnly; float _ShowWaterwaysOnly; float _ShowWaterwayAmountOnly; float _ShowRiverMaskOnly; float _ShowLakeMaskOnly; float _ShowCloudShadowMaskOnly; float _ShowCoastShelfMaskOnly; float _ShowShorelineMaskOnly; float _ShowWetlandMaskOnly; float _ShowWaterDepthMaskOnly;
                float _MoistureResponseScale; float _TemperatureHumidityInfluence; float _ClimateNoiseStrength; float _CoastWetnessStrength; float _ContinentalDrynessStrength; float _ContinentalTemperatureStrength; float _RainShadowStrength; float _OrographicWetnessStrength; float _OrographicSampleOffset; float _RiparianWetnessStrength; float _SeasonalityStrength; float _BiomeProvinceStrength; float _BiomeCompetitionSharpness;
                float _TerminatorSoftness; float _ShowLandMaskOnly; float _ShowDetailTexturesOnly; float _ShowNormalsOnly;
                float _VolcanicRockStrength; float _LavaCrackStrength; float _LavaEmissionStrength; float _LavaTextureScale; float _AshDetailStrength;
                float4 _KeyLightDirectionWS; float4 _KeyLightColor; float _KeyLightIntensity; float _CloudShadowDensity; float _CloudShadowScale; float _CloudShadowSpeed; float _CloudSurfaceShadowStrength;
                float4 _FillLightColor; float _FillLightIntensity;
                float4 _RimLightColor; float _RimLightIntensity;
            CBUFFER_END

            float hash31(float3 p) { p = frac(p * float3(0.1031, 0.1030, 0.0973)); p += dot(p, p.yxz + 33.33); return frac((p.x + p.y) * p.z); }
            float noise3D(float3 p)
            {
                float3 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
                float n000 = hash31(i + float3(0,0,0)), n100 = hash31(i + float3(1,0,0)), n010 = hash31(i + float3(0,1,0)), n110 = hash31(i + float3(1,1,0));
                float n001 = hash31(i + float3(0,0,1)), n101 = hash31(i + float3(1,0,1)), n011 = hash31(i + float3(0,1,1)), n111 = hash31(i + float3(1,1,1));
                return lerp(lerp(lerp(n000,n100,f.x),lerp(n010,n110,f.x),f.y), lerp(lerp(n001,n101,f.x),lerp(n011,n111,f.x),f.y), f.z);
            }
            float fbm(float3 p) { float v=0,a=0.5,f=1; for (int i=0;i<4;i++) { v += a * noise3D(p * f); f*=2; a*=0.5; } return v; }
            float GetWarpedLandValue(float3 objNorm, float3 seedOff)
            {
                float3 broadP = objNorm * _LandScale, warpP = objNorm * max(0.4, _LandScale * 0.55);
                float3 warp = float3(fbm(warpP + seedOff + float3(15.1,42.2,73.3)), fbm(warpP + seedOff + float3(66.4,24.8,11.5)), fbm(warpP + seedOff + float3(93.7,57.9,31.2))) - 0.5;
                float3 warped = broadP + warp * lerp(0.25, 0.55, saturate(_LandScale / 5.0));
                float baseLand = fbm(warped + float3(42.3, 17.1, 83.7) + seedOff);
                float coastNoise = fbm(warped * 3.75 + float3(9.4, 51.8, 27.6) + seedOff) - 0.5;
                return baseLand + (coastNoise * (1.0 - smoothstep(0.03, 0.16, abs(baseLand - _LandThreshold))) * 0.22);
            }
            float GetLandMask(float3 objNorm, float3 seedOff) { return smoothstep(_LandThreshold - 0.04, _LandThreshold + 0.04, GetWarpedLandValue(objNorm, seedOff)); }
            float GetCapMask(float3 objNorm, float3 seedOff) { float latitude = abs(objNorm.y); float iceEdgeNoise = noise3D(objNorm * 6.0 + float3(11.1, 5.5, 22.2) + seedOff); float capStart = lerp(0.99, 0.34, _IceCapSize); return smoothstep(capStart - 0.10, capStart + 0.10, latitude + (iceEdgeNoise - 0.5) * 0.15); }
            float GetMountainMask(float3 objNorm, float landMask) { float3 seedOff = float3(_Seed, _Seed * 0.7, _Seed * 1.3); float broad = fbm(objNorm * (_LandScale * 1.2) + seedOff + float3(29.3, 17.7, 63.1)); float ridge = 1.0 - abs(fbm(objNorm * (_LandScale * 4.6) + seedOff + float3(83.5, 9.2, 44.7)) * 2.0 - 1.0); ridge = pow(saturate(ridge), 2.5); return smoothstep(0.58, 0.85, ridge + broad * 0.35) * landMask * smoothstep(0.35, 1.0, _Elevation); }
            float GetTerrainHeightValue(float3 objNorm, float landMask, float capMask) { float3 seedOff = float3(_Seed, _Seed * 0.7, _Seed * 1.3); float hills = fbm(objNorm * (_LandScale * 2.2) + seedOff + float3(99.1, 55.3, 12.7)) * landMask; float mountainMask = GetMountainMask(objNorm, landMask); float volcanicMask = smoothstep(0.35, 1.0, _MapStyle) * mountainMask * fbm(objNorm * (_LandScale * 6.0) + seedOff + float3(4.4, 66.1, 27.8)); float waterMask = 1.0 - landMask; return landMask * _LandUpliftStrength + hills * _HillDisplacementStrength + mountainMask * _MountainDisplacementStrength + capMask * _IceDisplacementStrength + volcanicMask * _VolcanicDisplacementStrength - waterMask * _OceanDepthStrength; }
            float GetPreviewDisplacementHeight(float3 objNorm) { float3 seedOff = float3(_Seed, _Seed * 0.7, _Seed * 1.3); return GetTerrainHeightValue(objNorm, GetLandMask(objNorm, seedOff), GetCapMask(objNorm, seedOff)) * _DisplacementScale; }
        ENDHLSL

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

            TEXTURE2D(_MountainDetailTex); SAMPLER(sampler_MountainDetailTex);
            TEXTURE2D(_IceDetailTex);
            TEXTURE2D(_OceanDetailTex);
            TEXTURE2D(_WaterwayDetailTex);
            TEXTURE2D(_WaterwayMaskTex); SAMPLER(sampler_WaterwayMaskTex);
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
            TEXTURE2D(_TaigaAlbedoTex);
            TEXTURE2D(_TundraAlbedoTex);
            TEXTURE2D(_PolarAlbedoTex);
            TEXTURE2D(_MarshAlbedoTex);
            TEXTURE2D(_JungleNormalTex);
            TEXTURE2D(_DesertNormalTex);
            TEXTURE2D(_SavannaNormalTex);
            TEXTURE2D(_TemperateGrassNormalTex);
            TEXTURE2D(_TemperateForestNormalTex);
            TEXTURE2D(_TaigaNormalTex);
            TEXTURE2D(_TundraNormalTex);
            TEXTURE2D(_PolarNormalTex);
            TEXTURE2D(_MarshNormalTex);
            TEXTURE2D(_TaigaSmoothnessTex);
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
            struct SurfaceBiomeWeights { float jungle; float desert; float savanna; float temperateGrass; float temperateForest; float taiga; float tundra; float polar; float marsh; };
            struct PreviewClimateFields { float temperature; float moisture; float continentality; float seasonality; float rainShadow; float windwardWetness; float riparianWetness; };
            float BellFit(float v,float c,float w){ float d = (v - c) / max(w, 0.001); return exp(-(d * d)); }
            float RangeFit(float v,float low,float il,float ih,float high){ return saturate((v-low)/max(il-low,0.001))*saturate((high-v)/max(high-ih,0.001)); }
            SurfaceBiomeWeights GetSurfaceBiomeWeights(PreviewClimateFields c, float terrainHeight, float capMask, float latitude, float3 objNorm, float seed)
            {
                SurfaceBiomeWeights w=(SurfaceBiomeWeights)0;

                // Province-scale variation masks
                float forestP=fbm(objNorm*1.35+seed*1.1);
                float grassP=fbm(objNorm*1.3+seed*2.1);
                float aridP=fbm(objNorm*1.25+seed*3.1);
                float wetP=fbm(objNorm*1.2+seed*4.1);
                float coldP=fbm(objNorm*1.15+seed*5.1);

                float ps=_BiomeProvinceStrength;
                float polarMask=saturate(capMask);

                // Climate niches: primarily temperature + moisture driven.
                float tHot=BellFit(c.temperature,0.86,0.19);
                float tWarm=BellFit(c.temperature,0.70,0.23);
                float tTemp=BellFit(c.temperature,0.52,0.22);
                float tCool=BellFit(c.temperature,0.33,0.18);
                float tCold=BellFit(c.temperature,0.19,0.14);

                float mDry=BellFit(c.moisture,0.14,0.17);
                float mSemiDry=BellFit(c.moisture,0.34,0.19);
                float mMid=BellFit(c.moisture,0.52,0.22);
                float mMoist=BellFit(c.moisture,0.69,0.20);
                float mWet=BellFit(c.moisture,0.84,0.15);

                float continentalDry=saturate(c.continentality*0.62 + c.rainShadow*0.58);
                float windwardWet=saturate(c.windwardWetness*0.70 + (1.0-c.continentality)*0.25);
                float riparianBoost=saturate(c.riparianWetness);
                float seasonal=saturate(c.seasonality);

                float lowElev=smoothstep(0.45,0.05,terrainHeight);
                float highElev=smoothstep(0.55,0.92,terrainHeight);

                // Hot + wet, reduced by high seasonality and caps.
                w.jungle=tHot*mWet*(1.0-seasonal*0.50)*(1.0-polarMask)*lerp(1-ps,1+ps,forestP*0.72+wetP*0.28)*lerp(0.9,1.15,windwardWet);

                // Hot/warm + very dry, strengthened by continental/rain-shadow effects.
                w.desert=max(tHot,tWarm)*mDry*(1.0-polarMask)*lerp(1.0,1.35,continentalDry)*lerp(1-ps,1+ps,aridP);

                // Warm seasonal grassland, between dry and moist.
                w.savanna=tWarm*mMid*lerp(0.82,1.28,seasonal)*(1.0-polarMask)*lerp(1-ps,1+ps,grassP)*lerp(0.9,1.1,1.0-continentalDry);

                // Temperate semi-dry to moderate moisture continental grasslands.
                float temperateDryBand=RangeFit(c.moisture,0.12,0.24,0.58,0.72);
                w.temperateGrass=tTemp*temperateDryBand*(1.0-polarMask*0.8)*lerp(1.0,1.24,continentalDry)*lerp(1-ps,1+ps,grassP*0.7+aridP*0.3);

                // Temperate forests in moderate-to-moist climates, favored by windward wetness.
                w.temperateForest=tTemp*mMoist*(1.0-polarMask)*lerp(1.0,1.22,windwardWet)*lerp(1-ps,1+ps,forestP)*lerp(1.0,0.82,continentalDry);

                // Cool, moist to semi-moist forest biome; avoid strong direct latitude forcing.
                w.taiga=tCool*RangeFit(c.moisture,0.28,0.42,0.80,0.94)*(1.0-polarMask*0.65)*lerp(1-ps,1+ps,forestP*0.58+coldP*0.42)*lerp(1.0,1.12,seasonal);

                // Cold, generally drier biome with some moisture tolerance.
                w.tundra=tCold*RangeFit(c.moisture,0.08,0.20,0.58,0.76)*(1.0-polarMask*0.35)*lerp(1-ps,1+ps,coldP)*lerp(1.0,1.1,highElev);

                w.polar=polarMask;

                // Wet low-elevation and riparian zones.
                w.marsh=lowElev*mWet*(1.0-polarMask)*lerp(1.0,1.55,riparianBoost)*lerp(1.0,1.2,windwardWet)*lerp(1-ps,1+ps,wetP);

                #define SOFT(x) pow(max(x,0.0001),_BiomeCompetitionSharpness)
                w.jungle=SOFT(w.jungle);w.desert=SOFT(w.desert);w.savanna=SOFT(w.savanna);w.temperateGrass=SOFT(w.temperateGrass);w.temperateForest=SOFT(w.temperateForest);w.taiga=SOFT(w.taiga);w.tundra=SOFT(w.tundra);w.polar=SOFT(w.polar);w.marsh=SOFT(w.marsh);
                float total=w.jungle+w.desert+w.savanna+w.temperateGrass+w.temperateForest+w.taiga+w.tundra+w.polar+w.marsh+1e-4;
                w.jungle/=total;w.desert/=total;w.savanna/=total;w.temperateGrass/=total;w.temperateForest/=total;w.taiga/=total;w.tundra/=total;w.polar/=total;w.marsh/=total;
                return w;
            }
            float3 GetTextureBiomeAlbedo(SurfaceBiomeWeights w, float3 climateGrade, float3 positionOS, float3 objNorm, float localTemperature)
            {
                float3 ju=SampleTriplanarScaled(TEXTURE2D_ARGS(_JungleAlbedoTex, sampler_MountainDetailTex), positionOS,objNorm,_BiomeTextureScale);
                float3 de=SampleTriplanarScaled(TEXTURE2D_ARGS(_DesertAlbedoTex, sampler_MountainDetailTex), positionOS,objNorm,_BiomeTextureScale);
                float3 sa=SampleTriplanarScaled(TEXTURE2D_ARGS(_SavannaAlbedoTex, sampler_MountainDetailTex), positionOS,objNorm,_BiomeTextureScale);
                float3 tg=SampleTriplanarScaled(TEXTURE2D_ARGS(_TemperateGrassAlbedoTex, sampler_MountainDetailTex), positionOS,objNorm,_BiomeTextureScale);
                float3 tf=SampleTriplanarScaled(TEXTURE2D_ARGS(_TemperateForestAlbedoTex, sampler_MountainDetailTex), positionOS,objNorm,_BiomeTextureScale);
                float3 ta=SampleTriplanarScaled(TEXTURE2D_ARGS(_TaigaAlbedoTex, sampler_MountainDetailTex), positionOS,objNorm,_BiomeTextureScale);
                float3 tu=SampleTriplanarScaled(TEXTURE2D_ARGS(_TundraAlbedoTex, sampler_MountainDetailTex), positionOS,objNorm,_BiomeTextureScale);
                float3 po=SampleTriplanarScaled(TEXTURE2D_ARGS(_PolarAlbedoTex, sampler_MountainDetailTex), positionOS,objNorm,_BiomeTextureScale);
                float3 ma=SampleTriplanarScaled(TEXTURE2D_ARGS(_MarshAlbedoTex, sampler_MountainDetailTex), positionOS,objNorm,_BiomeTextureScale);
                float3 texBiome = ju*w.jungle + de*w.desert + sa*w.savanna + tg*w.temperateGrass + tf*w.temperateForest + ta*w.taiga + tu*w.tundra + po*w.polar + ma*w.marsh;
                return lerp(texBiome, texBiome*climateGrade, _BiomeTintStrength*_BiomeTextureStrength);
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
                float moistureBase = saturate(0.5 + (_Moisture - 0.5) * _MoistureResponseScale);
                float temperatureHumidityBias = (tempHumidityLimit - 0.5) * _TemperatureHumidityInfluence;
                float broadClimateNoise = (climateNoiseA - 0.5) * _ClimateNoiseStrength;
                float regionalClimateNoise = (climateNoiseB - 0.5) * (_ClimateNoiseStrength * 0.75);
                float coastWetness = coastProximity * _CoastWetnessStrength;
                float lowFrequencyInteriorNoise = fbm(objNorm * 1.1 + seedOff * 2.7);
                float continentality = saturate(interiorness * 0.85 + lowFrequencyInteriorNoise * 0.15);
                float continentalDryness = continentality * _ContinentalDrynessStrength;
                float3 windDir = normalize(float3(0.46, 0.15, 0.87) + float3(seedOff.x * 0.001, 0.0, seedOff.z * 0.001));
                float3 windTangent = normalize(windDir - objNorm * dot(windDir, objNorm) + 1e-4);
                float3 upwindNorm = normalize(objNorm - windTangent * _OrographicSampleOffset);
                float3 downwindNorm = normalize(objNorm + windTangent * _OrographicSampleOffset);
                float upwindMountain = GetMountainMask(upwindNorm, GetLandMask(upwindNorm, seedOff));
                float downwindMountain = GetMountainMask(downwindNorm, GetLandMask(downwindNorm, seedOff));
                float rainShadowDryness = saturate(upwindMountain * interiorness * _RainShadowStrength);
                float windwardWetness = saturate(downwindMountain * _OrographicWetnessStrength);

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
                float riparianWetness = riverMask * _RiparianWetnessStrength;

                // ==============================================================
                //  NORMAL WORLD colors
                // ==============================================================
                float capStart = lerp(0.99, 0.34, _IceCapSize);
                float iceEdgeNoise = noise3D(objNorm * 6.0 + float3(11.1, 5.5, 22.2) + seedOff);
                // Local temperature: latitude-first, then elevation lapse-rate cooling,
                // then small noise to avoid strict banding.
                float tempNoise = (noise3D(objNorm * 4.5 + float3(14.2, 36.1, 6.5) + seedOff) - 0.5) * 0.09;
                float elevationCooling = terrainHeight * lerp(0.10, 0.28, saturate(_Elevation + mtnBand * 0.8));
                float seasonalityNoise = fbm(objNorm * 2.2 + seedOff * 5.1);
                float subtropicalBias = smoothstep(0.15, 0.70, latitude) * (1.0 - smoothstep(0.60, 0.95, latitude));
                float seasonality = saturate((seasonalityNoise * 0.55 + continentality * 0.25 + subtropicalBias * 0.20) * _SeasonalityStrength + (1.0 - _SeasonalityStrength) * 0.5);
                float localMoist = saturate(moistureBase + temperatureHumidityBias + broadClimateNoise + regionalClimateNoise + coastWetness + windwardWetness + riparianWetness - continentalDryness - rainShadowDryness);
                float latTemperature = saturate((1.0 - latitude) * 0.82 + 0.18);
                float globalCooling = (0.5 - _Temperature) * 0.55;
                float globalWarming = (_Temperature - 0.5) * 0.30;
                float temperatureLocal = saturate(latTemperature - globalCooling + globalWarming - elevationCooling + tempNoise + (_Temperature - 0.5) * continentality * _ContinentalTemperatureStrength);
                // Biome color selected strictly by latitude, shifted by temperature
                float3 climateGrade = GetClimateGrade(latitude, localMoist, style);
                PreviewClimateFields climate; climate.temperature=temperatureLocal; climate.moisture=localMoist; climate.continentality=continentality; climate.seasonality=seasonality; climate.rainShadow=rainShadowDryness; climate.windwardWetness=windwardWetness; climate.riparianWetness=riparianWetness;
                SurfaceBiomeWeights biomeWeights = GetSurfaceBiomeWeights(climate, terrainHeight, capMask, latitude, objNorm, _Seed);
                if (_ShowLocalMoistureOnly > 0.5) return float4(localMoist.xxx,1);
                if (_ShowLocalTemperatureOnly > 0.5) return float4(temperatureLocal.xxx,1);
                if (_ShowContinentalityOnly > 0.5) return float4(continentality.xxx,1);
                if (_ShowSeasonalityOnly > 0.5) return float4(seasonality.xxx,1);
                if (_ShowRainShadowOnly > 0.5) return float4(rainShadowDryness.xxx,1);
                if (_ShowRiparianWetnessOnly > 0.5) return float4(riparianWetness.xxx,1);
                float3 biomeTextureAlbedo = GetTextureBiomeAlbedo(biomeWeights, climateGrade, input.positionOS, objNorm, temperatureLocal);

                // Uniform ocean color — single inspector-driven color, no depth/latitude variation
                float oceanMask = saturate(1.0 - edge);
                float coastShelfMask = oceanMask * (1.0 - smoothstep(0.0, 0.08, edge));
                float shorelineMask = oceanMask * smoothstep(0.0, 0.03, edge) * (1.0 - smoothstep(0.03, 0.06, edge));
                float3 oceanColor = lerp(_OceanColor.rgb, _OceanShallowColor.rgb, coastShelfMask * 0.65);
                oceanColor = lerp(oceanColor, oceanColor * 1.08, shorelineMask * 0.12);

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
                float waterwayDensity = saturate(pow(waterwayAmount, 0.85) * 1.2);
                float riverWidthA = lerp(0.028, 0.072, waterwayDensity);
                float riverWidthB = lerp(0.021, 0.052, waterwayDensity);
                float mainRiverA = 1.0 - smoothstep(0.0, riverWidthA, abs(riverNoise1 - 0.5));
                float mainRiverB = 1.0 - smoothstep(0.0, riverWidthB, abs(riverNoise2 - 0.5));

                // Tributary layer appears primarily at medium/high settings.
                float tributaryNoise = noise3D((riverSample + warp2 * 0.6) * 6.4 + float3(19.4, 41.8, 12.6) + seedOff);
                float tributaryWidth = lerp(0.010, 0.036, smoothstep(0.25, 1.0, waterwayDensity));
                float tributaryMask = tributaryWidth > 0.0 ? (1.0 - smoothstep(0.0, tributaryWidth, abs(tributaryNoise - 0.5))) : 0.0;

                float riverNetworkMask = max(mainRiverA, mainRiverB * 0.8);
                riverNetworkMask = max(riverNetworkMask, tributaryMask * 0.85);
                // Hard constraints to keep waterways geologically plausible:
                // - never in ocean
                // - reduced on immediate coasts (estuaries are narrow in this stylized pass)
                // - suppressed in very arid or very cold regions
                float inlandMask = smoothstep(_LandThreshold + 0.03, _LandThreshold + 0.16, n);
                float freezeGate = smoothstep(0.10, 0.28, temperatureLocal);
                float normalRiverMask = riverNetworkMask
                                      * inlandMask
                                      * saturate(1.0 - mtnBand * 0.8)
                                      * freezeGate
                                      * lerp(0.55, 1.2, waterwayDensity);
                // Lakes (normal)
                float lakeNoise = noise3D(samplePos * 12.0 + float3(7.7, 3.3, 9.9) + seedOff);
                float lakeShapeNoise = noise3D(samplePos * lerp(8.0, 15.5, waterwayDensity) + float3(27.2, 5.1, 13.7) + seedOff);
                float lakeCombined = lakeNoise * 0.68 + lakeShapeNoise * 0.32;
                float lakeEdgeLow = lerp(0.88, 0.72, waterwayDensity);
                float lakeEdgeHigh = lakeEdgeLow + lerp(0.04, 0.08, waterwayDensity);
                float lakeMask  = smoothstep(lakeEdgeLow, lakeEdgeHigh, lakeCombined)
                                * inlandMask
                                * saturate(1.0 - mtnBand * 0.5);
                float lowlandLakeGate = 1.0 - smoothstep(0.40, 0.68, terrainHeight);
                lakeMask *= lowlandLakeGate;

                float3 waterDir = normalize(objNorm);
                float waterU = atan2(waterDir.z, waterDir.x) / (2.0 * PI) + 0.5;
                float waterV = asin(clamp(waterDir.y, -1.0, 1.0)) / PI + 0.5;
                float2 waterwayUV = float2(frac(waterU), saturate(waterV));
                float4 generatedWaterwayMask = SAMPLE_TEXTURE2D(_WaterwayMaskTex, sampler_WaterwayMaskTex, waterwayUV);
                float generatedRiverMask = generatedWaterwayMask.r;
                float generatedLakeMask = generatedWaterwayMask.g;
                float wetlandMask = generatedWaterwayMask.b;
                float waterDepthMask = generatedWaterwayMask.a;
                float useGeneratedWaterways = step(0.001, dot(generatedWaterwayMask.rgb, 1.0));

                float riverMaskFinal = saturate(lerp(normalRiverMask, generatedRiverMask, useGeneratedWaterways));
                float lakeMaskFinal = saturate(lerp(lakeMask, generatedLakeMask, useGeneratedWaterways));
                float inlandWaterMask = saturate(max(riverMaskFinal, lakeMaskFinal));
                float inlandWaterRenderMask = smoothstep(0.18, 0.72, inlandWaterMask);

                float3 inlandWaterColor = _OceanColor.rgb;
                if (_UseDetailTextures > 0.5)
                {
                    float3 waterwayDetailForInland = SampleTriplanar(TEXTURE2D_ARGS(_WaterwayDetailTex, sampler_MountainDetailTex), input.positionOS, objNorm);
                    float inlandWaterDetailFactor = (dot(waterwayDetailForInland, float3(0.333, 0.333, 0.333)) - 0.5) * 2.0;
                    inlandWaterColor *= 1.0 + inlandWaterDetailFactor * max(_OceanDetailStrength, 0.15);
                }
                normalAlbedo = lerp(normalAlbedo, saturate(inlandWaterColor), inlandWaterRenderMask);

                float waterInfluence = inlandWaterRenderMask;
                riparianWetness = max(riparianWetness, waterInfluence * _RiparianWetnessStrength);

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
                float taigaSmoothMask = SampleTriplanarScaled(TEXTURE2D_ARGS(_TaigaSmoothnessTex, sampler_MountainDetailTex), input.positionOS, objNorm, _BiomeTextureScale).r;
                float volcanicSmoothMask = SampleTriplanarScaled(TEXTURE2D_ARGS(_VolcanicSmoothnessTex, sampler_MountainDetailTex), input.positionOS, objNorm, _LavaTextureScale).r;
                float landBlendMask = 0.75;
                float oceanBlendMask = oceanSmoothMask;
                float iceBlendMask = iceSmoothMask;
                float marshWeight = saturate(biomeWeights.marsh);
                float taigaWeight = saturate(biomeWeights.taiga);
                float infernalWeight = saturate(infernal + demonic);
                float landSmooth = lerp(_Smoothness * 0.70, _Smoothness * 0.95, landBlendMask);
                float oceanSmooth = lerp(_Smoothness * 0.52, _Smoothness * 0.72, oceanBlendMask) * saturate(_Smoothness + 0.20);
                float iceSmooth = lerp(_Smoothness * 0.38, _Smoothness * 0.58, iceBlendMask);
                float marshSmooth = lerp(_Smoothness * 0.62, _Smoothness * 0.82, marshSmoothMask);
                float taigaSmooth = lerp(_Smoothness * 0.50, _Smoothness * 0.70, taigaSmoothMask);
                float volcanicSmooth = lerp(_Smoothness * 0.18, _Smoothness * 0.38, volcanicSmoothMask);
                float mountainSmooth = landSmooth * 0.68;
                float smoothnessMask = lerp(oceanSmooth, landSmooth, edge);
                smoothnessMask = lerp(smoothnessMask, marshSmooth, marshWeight * edge);
                smoothnessMask = lerp(smoothnessMask, taigaSmooth, taigaWeight * edge);
                smoothnessMask = lerp(smoothnessMask, mountainSmooth, mtnBlend * edge);
                float inlandWaterSmooth = oceanSmooth;
                smoothnessMask = lerp(smoothnessMask, inlandWaterSmooth, inlandWaterRenderMask * (1.0 - infernalWeight));
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
                float3 taigaN = SampleTriplanarScaled(TEXTURE2D_ARGS(_TaigaNormalTex, sampler_MountainDetailTex), input.positionOS, objNorm, _BiomeTextureScale) * 2.0 - 1.0;
                float3 tuN = SampleTriplanarScaled(TEXTURE2D_ARGS(_TundraNormalTex, sampler_MountainDetailTex), input.positionOS, objNorm, _BiomeTextureScale) * 2.0 - 1.0;
                float3 poN = SampleTriplanarScaled(TEXTURE2D_ARGS(_PolarNormalTex, sampler_MountainDetailTex), input.positionOS, objNorm, _BiomeTextureScale) * 2.0 - 1.0;
                float3 maN = SampleTriplanarScaled(TEXTURE2D_ARGS(_MarshNormalTex, sampler_MountainDetailTex), input.positionOS, objNorm, _BiomeTextureScale) * 2.0 - 1.0;
                float3 biomeN = normalize(juN*biomeWeights.jungle + deN*biomeWeights.desert + saN*biomeWeights.savanna + tgN*biomeWeights.temperateGrass + tfN*biomeWeights.temperateForest + taigaN*biomeWeights.taiga + tuN*biomeWeights.tundra + poN*biomeWeights.polar + maN*biomeWeights.marsh);
                float3 surfN = normal;
                surfN = normalize(lerp(surfN, normalize(input.normalWS + oceanN), (1.0-edge) * _OceanNormalStrength));
                surfN = normalize(lerp(surfN, normalize(input.normalWS + biomeN), edge * _BiomeNormalStrength * (_UseTextureDrivenBiomes > 0.5 ? 1.0 : 0.0)));
                surfN = normalize(lerp(surfN, normalize(input.normalWS + mtnN), edge * mtnBlend * _MountainNormalStrength));
                surfN = normalize(lerp(surfN, normalize(input.normalWS + oceanN), inlandWaterRenderMask * _OceanNormalStrength * (1.0 - infernalWeight)));
                surfN = normalize(lerp(surfN, normalize(input.normalWS + iceN), capMask * _IceNormalStrength));

                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                float3 L = normalize(_KeyLightDirectionWS.xyz);
                float3 fillL = normalize(float3(-L.x, saturate(L.y * 0.5 + 0.1), -L.z));
                float ndl = saturate(dot(surfN, L));
                float3 cloudSampleDir = normalize(input.positionWS + L * 0.06);
                float cloudNoise = SAMPLE_TEXTURE2D(_CloudNoiseTex, sampler_MountainDetailTex, cloudSampleDir.xz * _CloudShadowScale + float2(_Time.y * _CloudShadowSpeed, _Time.y * _CloudShadowSpeed * 0.7)).r;
                float cloudCoverage = smoothstep(1.0 - _CloudShadowDensity, 1.0 - _CloudShadowDensity + 0.22, cloudNoise);
                float cloudShadowMask = cloudCoverage * _CloudSurfaceShadowStrength;
                ndl = smoothstep(0.0, max(0.01,_TerminatorSoftness), ndl);
                float fill = saturate(dot(surfN, fillL));
                float3 keyContrib = ndl * _KeyLightColor.rgb * _KeyLightIntensity;
                float3 fillContrib = fill * _FillLightColor.rgb * _FillLightIntensity;
                float3 lit = albedo * (keyContrib + fillContrib + _AmbientStrength);
                float3 H = normalize(L + viewDir);
                float specN = saturate(dot(surfN, H));
                float oceanWaterMask = saturate(1.0 - edge);
                float waterSpecMask = saturate(oceanWaterMask + inlandWaterRenderMask * 0.85 * (1.0 - infernalWeight));
                float waterSpec = pow(specN, lerp(24,120,saturate(smoothnessMask))) * waterSpecMask * 0.9;
                float iceSpec = pow(specN, 36) * capMask * 0.25;
                lit += (waterSpec + iceSpec) * _KeyLightColor.rgb * _KeyLightIntensity;
                float rim = pow(saturate(1.0 - dot(surfN, viewDir)), 2.5) * _RimLightIntensity;
                lit += rim * _RimLightColor.rgb * lerp(0.2, 1.0, edge);
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
                if (_ShowBiomeWeightsOnly > 0.5) return float4(saturate(biomeWeights.jungle*float3(0.05,0.35,0.08)+biomeWeights.desert*float3(0.85,0.74,0.45)+biomeWeights.savanna*float3(0.58,0.52,0.2)+biomeWeights.temperateGrass*float3(0.2,0.6,0.22)+biomeWeights.temperateForest*float3(0.08,0.32,0.1)+biomeWeights.taiga*float3(0.22,0.38,0.24)+biomeWeights.tundra*float3(0.5,0.45,0.4)+biomeWeights.polar*float3(0.85,0.92,1.0)+biomeWeights.marsh*float3(0.2,0.55,0.5)),1.0);
                if (_ShowBiomeTextureOnly > 0.5) return float4(saturate(biomeTextureAlbedo), 1.0);
                if (_ShowDominantBiomeOnly > 0.5) {
                    float best=biomeWeights.jungle; float3 c=float3(0.05,0.35,0.08);
                    if (biomeWeights.desert>best){best=biomeWeights.desert;c=float3(0.85,0.74,0.45);} if (biomeWeights.savanna>best){best=biomeWeights.savanna;c=float3(0.58,0.52,0.2);} if (biomeWeights.temperateGrass>best){best=biomeWeights.temperateGrass;c=float3(0.2,0.6,0.22);} if (biomeWeights.temperateForest>best){best=biomeWeights.temperateForest;c=float3(0.08,0.32,0.1);} if (biomeWeights.taiga>best){best=biomeWeights.taiga;c=float3(0.22,0.38,0.24);} if (biomeWeights.tundra>best){best=biomeWeights.tundra;c=float3(0.5,0.45,0.4);} if (biomeWeights.polar>best){best=biomeWeights.polar;c=float3(0.85,0.92,1.0);} if (biomeWeights.marsh>best){c=float3(0.2,0.55,0.5);} return float4(c,1);
                }

                if (_ShowLocalMoistureOnly > 0.5) return float4(localMoist.xxx,1.0);
                if (_ShowRiverMaskOnly > 0.5) return float4(riverMaskFinal.xxx, 1.0);
                if (_ShowLakeMaskOnly > 0.5) return float4(lakeMaskFinal.xxx, 1.0);
                if (_ShowCloudShadowMaskOnly > 0.5) return float4(cloudShadowMask.xxx, 1.0);
                if (_ShowCoastShelfMaskOnly > 0.5) return float4(coastShelfMask.xxx, 1.0);
                if (_ShowShorelineMaskOnly > 0.5) return float4(shorelineMask.xxx, 1.0);
                if (_ShowWetlandMaskOnly > 0.5) return float4(wetlandMask.xxx, 1.0);
                if (_ShowWaterDepthMaskOnly > 0.5) return float4(waterDepthMask.xxx, 1.0);
                if (_ShowWaterwaysOnly > 0.5) return float4(0.1*riverMaskFinal,0.3*riverMaskFinal,0.8*inlandWaterRenderMask,1.0);
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
