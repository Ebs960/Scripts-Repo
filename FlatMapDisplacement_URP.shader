Shader "Custom/FlatMapDisplacement_URP"
{
    Properties
    {
        _MainTex ("Biome Texture", 2D) = "gray" {}
        _Heightmap ("Heightmap", 2D) = "gray" {}
        _FogMask ("Fog Mask", 2D) = "white" {}
        _OwnershipOverlay ("Ownership Overlay", 2D) = "black" {}
        _Color ("Tint", Color) = (0.5,0.5,0.5,1)
        _FlatHeightScale ("Flat Height Scale", Float) = 0.1
        _MapHeight ("Map Height", Float) = 180.0
        [Toggle] _EnableFog ("Enable Fog", Float) = 1.0
        [Toggle] _EnableOwnership ("Enable Ownership", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _FlatHeightScale;
                float _MapHeight;
                float _EnableFog;
                float _EnableOwnership;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_Heightmap);
            SAMPLER(sampler_Heightmap);
            TEXTURE2D(_FogMask);
            SAMPLER(sampler_FogMask);
            TEXTURE2D(_OwnershipOverlay);
            SAMPLER(sampler_OwnershipOverlay);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                // Sample height and displace in object space
                float height = SAMPLE_TEXTURE2D_LOD(_Heightmap, sampler_Heightmap, input.uv, 0).r;
                float disp = height * _FlatHeightScale * _MapHeight;
                float3 posOS = input.positionOS.xyz;
                posOS.y += disp;

                o.positionCS = TransformObjectToHClip(posOS);
                o.uv = input.uv;
                o.positionWS = TransformObjectToWorld(posOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                
                // Detect magenta (missing texture) robustly
                half magentaR = step(0.95h, c.r);
                half magentaB = step(0.95h, c.b);
                half magentaG = step(0.05h, 1.0h - c.g);
                half missing = magentaR * magentaB * magentaG;
                c = lerp(c, half4(0.0h, 1.0h, 1.0h, 1.0h), step(0.5h, missing));
                c *= _Color;

                // Ownership overlay
                if (_EnableOwnership > 0.5)
                {
                    half4 ownership = SAMPLE_TEXTURE2D(_OwnershipOverlay, sampler_OwnershipOverlay, i.uv);
                    c.rgb = lerp(c.rgb, ownership.rgb, ownership.a);
                }

                // Fog of war
                if (_EnableFog > 0.5)
                {
                    half fog = SAMPLE_TEXTURE2D(_FogMask, sampler_FogMask, i.uv).r;
                    if (fog < 0.01h)
                    {
                        c.rgb = half3(0,0,0);
                    }
                    else if (fog < 0.75h)
                    {
                        half gray = dot(c.rgb, half3(0.299h, 0.587h, 0.114h));
                        c.rgb = lerp(half3(0,0,0), gray * 0.6h, saturate(fog * 2.0h));
                    }
                }

                return c;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
