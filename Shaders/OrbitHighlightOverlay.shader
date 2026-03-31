Shader "Custom/OrbitHighlightOverlay"
{
    Properties
    {
        _LUT ("Tile Index LUT", 2D) = "black" {}
        _HighlightTileIndex ("Highlight Tile Index", Float) = -1
        _HighlightColor ("Highlight Color", Color) = (1, 1, 0, 0.3)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }

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

            TEXTURE2D(_LUT);
            SAMPLER(sampler_LUT);
            float _HighlightTileIndex;
            float4 _HighlightColor;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            int DecodeTileIndex(float2 uv)
            {
                float4 lutSample = SAMPLE_TEXTURE2D_LOD(_LUT, sampler_LUT, uv, 0);
                int r = (int)(lutSample.r * 255.0 + 0.5);
                int g = (int)(lutSample.g * 255.0 + 0.5);
                int b = (int)(lutSample.b * 255.0 + 0.5);
                return r + g * 256 + b * 65536;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                if (_HighlightTileIndex < 0)
                    discard;

                int currentTile = DecodeTileIndex(input.uv);
                if (currentTile != (int)_HighlightTileIndex)
                    discard;

                return _HighlightColor;
            }
            ENDHLSL
        }
    }
}
