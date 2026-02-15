Shader "Custom/SG_FoamEdge"
{
    Properties
    {
        [Header(Foam)]
        _FoamColor ("Foam Color", Color) = (0.9, 0.95, 1.0, 0.8)
        _FoamTex ("Foam Texture", 2D) = "white" {}
        _FoamScrollSpeed ("Foam Scroll Speed", Range(0, 2)) = 0.3
        _FoamTiling ("Foam Tiling", Range(0.1, 10)) = 2.0

        [Header(Fade)]
        _EdgeFadePower ("Edge Fade Power", Range(0.5, 5)) = 2.0
        _AlphaMultiplier ("Alpha Multiplier", Range(0, 2)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+1"
        }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            TEXTURE2D(_FoamTex);
            SAMPLER(sampler_FoamTex);

            float4 _FoamColor;
            float _FoamScrollSpeed;
            float _FoamTiling;
            float _EdgeFadePower;
            float _AlphaMultiplier;

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                // UV.u runs along the edge, UV.v goes from water (0) to land (1)
                float2 foamUV = float2(
                    input.uv.x * _FoamTiling + _Time.y * _FoamScrollSpeed,
                    input.uv.y
                );

                float4 foamSample = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, foamUV);

                // Fade: full opacity at water edge (v=0), transparent at land edge (v=1)
                float fade = pow(1.0 - saturate(input.uv.y), _EdgeFadePower);

                float4 color = _FoamColor;
                color.a *= foamSample.r * fade * _AlphaMultiplier;

                // Soft alpha clip to avoid hard edges
                clip(color.a - 0.01);

                return color;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/InternalErrorShader"
}
