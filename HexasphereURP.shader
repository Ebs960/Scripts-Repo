Shader "Custom/HexasphereURP"
{
    Properties
    {
        _BiomeAlbedoArray ("Biome Albedo Array", 2DArray) = "" {}
        _BiomeIndexTex    ("Tile → Biome map", 2D) = "white" {}
        _BiomeCount       ("BiomeCount", Float) = 1
        [Toggle] _SharpBoundaries ("Sharp Biome Boundaries", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalRenderPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma require 2darray

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Declare textures and samplers manually
            Texture2DArray _BiomeAlbedoArray;
            SamplerState sampler_BiomeAlbedoArray;

            Texture2D _BiomeIndexTex;
            SamplerState sampler_BiomeIndexTex;

            // Material data
            CBUFFER_START(UnityPerMaterial)
                float _BiomeCount;
                float _SharpBoundaries;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;

                // Get biome ID from red channel (0–1) and scale to array index
                float biomeId;
                if (_SharpBoundaries > 0.5)
                {
                    // Use point sampling for sharp boundaries
                    biomeId = _BiomeIndexTex.SampleLevel(sampler_BiomeIndexTex, uv, 0).r;
                }
                else
                {
                    // Use trilinear sampling for smooth boundaries
                    biomeId = _BiomeIndexTex.Sample(sampler_BiomeIndexTex, uv).r;
                }
                
                float rawIndex = biomeId * (_BiomeCount - 1);
                int slice = clamp((int)round(rawIndex), 0, (int)_BiomeCount - 1);

                // Sample the texture array
                float4 color;
                if (_SharpBoundaries > 0.5)
                {
                    // Use point sampling for sharp boundaries
                    color = _BiomeAlbedoArray.SampleLevel(sampler_BiomeAlbedoArray, float3(uv, slice), 0);
                }
                else
                {
                    // Use trilinear sampling for smooth boundaries
                    color = _BiomeAlbedoArray.Sample(sampler_BiomeAlbedoArray, float3(uv, slice));
                }

                return float4(color.rgb, 1.0);
            }

            ENDHLSL
        }
    }

    FallBack Off
}
