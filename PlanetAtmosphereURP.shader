Shader "Custom/PlanetAtmosphereURP"
{
    Properties
    {
        _LatTintTex ("Latitude Gradient (EQ→Poles)", 2D) = "white" {}
        _AlphaMid   ("Mid‑altitude Alpha", Range(0,1)) = 0.5
        _AlphaEdge  ("Edge Fade Alpha"  , Range(0,1)) = 0.0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent"
               "Queue"="Transparent"
               "IgnoreProjector"="True"
               "RenderPipeline"="UniversalRenderPipeline" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Front         // we’re inside‐out

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_LatTintTex); SAMPLER(sampler_LatTintTex);

            CBUFFER_START(UnityPerMaterial)
                float _AlphaMid, _AlphaEdge;
                float3 _PlanetCenterWS;
                float  _PlanetRadius;
            CBUFFER_END

            struct app   { float4 vertex : POSITION; };
            struct v2f   {
                float4 pos      : SV_POSITION;
                float  lat01    : TEXCOORD0;
                float3 viewDir  : TEXCOORD1;
            };

            v2f vert(app IN)
            {
                v2f OUT;
                float3 worldPos = TransformObjectToWorld(IN.vertex.xyz);
                OUT.pos   = TransformWorldToHClip(worldPos);

                // latitude from world‑space position
                float3 n = normalize(worldPos - _PlanetCenterWS);
                float latitude = asin(n.y);        // −π/2…π/2
                OUT.lat01 = (latitude / HALF_PI) * 0.5 + 0.5; // 0‑1

                // view dir for simple atmospheric falloff
                OUT.viewDir = _WorldSpaceCameraPos - worldPos;
                return OUT;
            }

            half4 frag(v2f IN) : SV_Target
            {
                // 1. Sample gradient by latitude
                half3 skyCol = SAMPLE_TEXTURE2D(_LatTintTex, sampler_LatTintTex, float2(IN.lat01, 0.5)).rgb;

                // 2. Depth/edge fade – thin at top, denser near horizon
                half viewDist   = length(IN.viewDir);
                half shellAlpha = saturate(_AlphaMid - (_AlphaMid - _AlphaEdge) * (viewDist / _PlanetRadius));

                // 3. Fresnel-ish fade so horizon glows more
                half3 V = normalize(IN.viewDir);
                half3 N = normalize(-IN.viewDir);          // from camera to surface
                half fres = pow(saturate(1.0 - dot(V,N)), 3);
                shellAlpha *= fres;

                return half4(skyCol, shellAlpha);
            }
            ENDHLSL
        }
    }
}
