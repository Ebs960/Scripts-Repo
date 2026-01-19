// TEMPORARY: This hand-written shader is kept as a compatibility fallback.
// Replace this file with a Shader Graph asset named `BiomeTerrain_HDRP`.
Shader "HDRP/BiomeTerrain"
{
    Properties
    {
        _BiomeIndexMap("Biome Index Map", 2D) = "black" {}
        _BiomeAlbedoArray("Biome Albedo Array", 2DArray) = "white" {}
        _BiomeNormalArray("Biome Normal Array", 2DArray) = "bump" {}
        _BiomeMaskArray("Biome Mask Array", 2DArray) = "white" {}
        _Heightmap("Heightmap", 2D) = "black" {}
        _ElevationScale("Elevation Scale", Float) = 1
        _GlobalSnowAmount("Global Snow Amount", Range(0,1)) = 0
        _GlobalWetness("Global Wetness", Range(0,1)) = 0
        _MapWidth("Map Width", Float) = 1
        _MapHeight("Map Height", Float) = 1
    }

    HLSLINCLUDE
    #pragma target 4.5
    #pragma only_renderers d3d11 ps4 xboxone vulkan metal switch
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"

    TEXTURE2D(_BiomeIndexMap);
    SAMPLER(sampler_BiomeIndexMap);
    TEXTURE2D(_Heightmap);
    SAMPLER(sampler_Heightmap);

    TEXTURE2D_ARRAY(_BiomeAlbedoArray);
    TEXTURE2D_ARRAY(_BiomeNormalArray);
    TEXTURE2D_ARRAY(_BiomeMaskArray);
    SAMPLER(sampler_BiomeAlbedoArray);
    SAMPLER(sampler_BiomeNormalArray);
    SAMPLER(sampler_BiomeMaskArray);

    #define BIOME_MAX 256

    CBUFFER_START(UnityPerMaterial)
        float4 _BiomeTints[BIOME_MAX];
        float4 _BiomeParams[BIOME_MAX];
        float _ElevationScale;
        float _GlobalSnowAmount;
        float _GlobalWetness;
        float _MapWidth;
        float _MapHeight;
    CBUFFER_END

    struct Attributes
    {
        float3 positionOS : POSITION;
        float3 normalOS : NORMAL;
        float4 tangentOS : TANGENT;
        float2 uv0 : TEXCOORD0;
    };

    struct Varyings
    {
        float4 positionCS : SV_Position;
        float3 positionWS : TEXCOORD0;
        float3 normalWS : TEXCOORD1;
        float4 tangentWS : TEXCOORD2;
        float2 uv0 : TEXCOORD3;
    };

    float3 ApplySnow(float3 albedo, float snowAmount)
    {
        float luminance = dot(albedo, float3(0.3, 0.59, 0.11));
        float3 desaturated = lerp(albedo, luminance.xxx, 0.6);
        float3 snowColor = lerp(desaturated, float3(1.0, 1.0, 1.0), 0.4);
        return lerp(albedo, snowColor, snowAmount);
    }

    float3 ApplyWetness(float3 albedo, float wetnessAmount)
    {
        return albedo * lerp(1.0, 0.7, wetnessAmount);
    }

    Varyings Vert(Attributes input)
    {
        Varyings output;

        float height = SAMPLE_TEXTURE2D(_Heightmap, sampler_Heightmap, input.uv0).r;
        float3 positionOS = input.positionOS;
        positionOS.y += height * _ElevationScale;

        float3 positionWS = TransformObjectToWorld(positionOS);
        output.positionWS = positionWS;
        output.positionCS = TransformWorldToHClip(positionWS);
        output.normalWS = TransformObjectToWorldNormal(input.normalOS);
        output.tangentWS = float4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w);
        output.uv0 = input.uv0;
        return output;
    }

    void GetSurfaceData(Varyings input, out SurfaceData surfaceData)
    {
        ZERO_INITIALIZE(SurfaceData, surfaceData);

        float biomeIndexValue = SAMPLE_TEXTURE2D(_BiomeIndexMap, sampler_BiomeIndexMap, input.uv0).r;
        int biomeIndex = (int)round(saturate(biomeIndexValue) * 255.0);
        biomeIndex = clamp(biomeIndex, 0, BIOME_MAX - 1);

        float4 biomeParams = _BiomeParams[biomeIndex];
        float tiling = max(0.001, biomeParams.x);
        float snowRetention = biomeParams.y;
        float wetnessResponse = biomeParams.z;
        float isWater = biomeParams.w;

        float2 tiledUV = input.uv0 * tiling;

        float4 albedoSample = SAMPLE_TEXTURE2D_ARRAY(_BiomeAlbedoArray, sampler_BiomeAlbedoArray, tiledUV, biomeIndex);
        float4 maskSample = SAMPLE_TEXTURE2D_ARRAY(_BiomeMaskArray, sampler_BiomeMaskArray, tiledUV, biomeIndex);
        float4 normalSample = SAMPLE_TEXTURE2D_ARRAY(_BiomeNormalArray, sampler_BiomeNormalArray, tiledUV, biomeIndex);

        float3 albedo = albedoSample.rgb * _BiomeTints[biomeIndex].rgb;

        float snowAmount = _GlobalSnowAmount * snowRetention * (1.0 - isWater);
        float wetnessAmount = _GlobalWetness * wetnessResponse * (1.0 - isWater);

        albedo = ApplySnow(albedo, snowAmount);
        albedo = ApplyWetness(albedo, wetnessAmount);

        if (isWater > 0.5)
        {
            albedo *= 0.5;
        }

        float metallic = maskSample.r;
        float ao = maskSample.g;
        float smoothness = maskSample.a;

        smoothness = lerp(smoothness, 0.95, wetnessAmount);
        if (isWater > 0.5)
        {
            smoothness = min(smoothness, 0.1);
        }

        float3 normalTS = UnpackNormalmap(normalSample);
        float3 normalWS = TransformTangentToWorld(normalTS, input.tangentWS, input.normalWS);

        surfaceData.baseColor = albedo;
        surfaceData.perceptualSmoothness = smoothness;
        surfaceData.metallic = metallic;
        surfaceData.ambientOcclusion = ao;
        surfaceData.normalWS = normalWS;
        surfaceData.specularOcclusion = 1.0;
    }

    void GetBuiltinData(Varyings input, SurfaceData surfaceData, out BuiltinData builtinData)
    {
        ZERO_INITIALIZE(BuiltinData, builtinData);
        builtinData.opacity = 1.0;
        builtinData.emissiveColor = 0.0;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "Forward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _SURFACE_TYPE_TRANSPARENT

            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassForward.hlsl"

            void Frag(Varyings input, out float4 outColor : SV_Target0)
            {
                SurfaceData surfaceData;
                BuiltinData builtinData;
                GetSurfaceData(input, surfaceData);
                GetBuiltinData(input, surfaceData, builtinData);

                BSDFData bsdfData;
                InitBuiltinData(input.positionWS, input.positionCS, builtinData);
                ConvertSurfaceDataToBSDFData(surfaceData, bsdfData);

                PreLightData preLightData;
                ZERO_INITIALIZE(PreLightData, preLightData);
                outColor = EvaluateBSDF(input.positionWS, input.normalWS, bsdfData, preLightData, builtinData);
            }
            ENDHLSL
        }
    }
}
