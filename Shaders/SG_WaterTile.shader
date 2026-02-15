Shader "Custom/SG_WaterTile"
{
    Properties
    {
        [Header(Water Color)]
        _ShallowColor ("Shallow Color", Color) = (0.2, 0.5, 0.7, 0.8)
        _DeepColor ("Deep Color", Color) = (0.05, 0.15, 0.35, 0.95)
        _FresnelPower ("Fresnel Power", Range(0.5, 10)) = 3.0

        [Header(Normal Maps)]
        _NormalMapA ("Normal Map A", 2D) = "bump" {}
        _NormalMapB ("Normal Map B", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Range(0, 2)) = 0.5
        _ScrollSpeedA ("Scroll Speed A", Vector) = (0.03, 0.02, 0, 0)
        _ScrollSpeedB ("Scroll Speed B", Vector) = (-0.02, 0.03, 0, 0)

        [Header(Wave Animation)]
        _WaveAmplitude ("Wave Amplitude", Range(0, 0.1)) = 0.02
        _WaveFrequency ("Wave Frequency", Range(0.5, 10)) = 2.0

        [Header(Flow)]
        _FlowSpeed ("Flow Speed (rivers)", Range(0, 2)) = 0.5

        [Header(Caustics)]
        _CausticsTex ("Caustics Texture", 2D) = "black" {}
        _CausticsTiling ("Caustics Tiling", Range(0.01, 2)) = 0.3
        _CausticsSpeed ("Caustics Scroll Speed", Range(0, 1)) = 0.08
        _CausticsIntensity ("Caustics Intensity", Range(0, 2)) = 0.6

        [Header(Transparency)]
        _AlphaBase ("Base Alpha", Range(0, 1)) = 0.85
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            // Continuous inland-water mesh can have mixed winding from marching squares.
            // Cull Off ensures the surface renders even when triangle winding flips.
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
                float4 color : COLOR;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 viewDirWS : TEXCOORD3;
                float4 vertexColor : TEXCOORD4;
            };

            TEXTURE2D(_NormalMapA);
            SAMPLER(sampler_NormalMapA);
            TEXTURE2D(_NormalMapB);
            SAMPLER(sampler_NormalMapB);
            TEXTURE2D(_CausticsTex);
            SAMPLER(sampler_CausticsTex);

            float4 _ShallowColor;
            float4 _DeepColor;
            float _FresnelPower;
            float _NormalStrength;
            float4 _ScrollSpeedA;
            float4 _ScrollSpeedB;
            float _WaveAmplitude;
            float _WaveFrequency;
            float _FlowSpeed;
            float _CausticsTiling;
            float _CausticsSpeed;
            float _CausticsIntensity;
            float _AlphaBase;

            Varyings vert(Attributes input)
            {
                Varyings output;

                float3 posOS = input.positionOS;

                // Small sine bob based on world-space XZ
                float3 posWS = TransformObjectToWorld(posOS);
                float wave = sin(posWS.x * _WaveFrequency + _Time.y * 2.0)
                           * cos(posWS.z * _WaveFrequency * 0.7 + _Time.y * 1.5);
                posOS.y += wave * _WaveAmplitude;

                output.positionWS = TransformObjectToWorld(posOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceNormalizeViewDir(output.positionWS);
                output.uv = input.uv;
                output.vertexColor = input.color;

                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                // Decode flow direction from vertex color (rg: 0..1 -> -1..1)
                float2 flowDir = input.vertexColor.rg * 2.0 - 1.0;

                // Water type from alpha:
                // - per-tile water mesh encodes: Ocean=0.33, Lake=0.67, River=1.0
                // - continuous inland mesh uses the same convention
                // Use a smooth factor so river→lake transitions don't hard-step when triangles interpolate alpha.
                float waterTypeAlpha = input.vertexColor.a;
                float riverFactor = smoothstep(0.85, 0.95, waterTypeAlpha);

                // Flow offset for rivers
                float2 flowOffset = flowDir * (_FlowSpeed * _Time.y * riverFactor);

                // Scroll UVs for two normal maps
                float2 worldUV = input.positionWS.xz * 0.1; // world-space tiling
                float2 uvA = worldUV + _ScrollSpeedA.xy * _Time.y + flowOffset;
                float2 uvB = worldUV + _ScrollSpeedB.xy * _Time.y + flowOffset * 0.5;

                // Sample and blend normals
                float3 normalA = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMapA, sampler_NormalMapA, uvA));
                float3 normalB = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMapB, sampler_NormalMapB, uvB));
                float3 blendedNormal = normalize(float3(
                    (normalA.xy + normalB.xy) * _NormalStrength,
                    1.0
                ));

                // Fresnel
                float3 viewDir = normalize(input.viewDirWS);
                float3 worldNormal = normalize(input.normalWS + float3(blendedNormal.x, 0, blendedNormal.y));
                float fresnel = pow(1.0 - saturate(dot(viewDir, worldNormal)), _FresnelPower);

                // Color blend: shallow near edges, deep at center/steep angles
                float4 color = lerp(_ShallowColor, _DeepColor, fresnel);

                // --- Caustics ---
                // Two layers scrolling at different speeds/angles for a shimmering effect.
                // Uses world-space XZ so the pattern is seamless across hex tiles.
                float2 causticsUV_base = input.positionWS.xz * _CausticsTiling;
                float t = _Time.y * _CausticsSpeed;

                // Layer A: scroll diagonally
                float2 causticsUV_A = causticsUV_base + float2(t * 0.7, t * 0.5);
                // Layer B: scroll in a different direction, slightly rotated
                float2 causticsUV_B = causticsUV_base * 1.15 + float2(-t * 0.5, t * 0.8);

                float causticsA = SAMPLE_TEXTURE2D(_CausticsTex, sampler_CausticsTex, causticsUV_A).r;
                float causticsB = SAMPLE_TEXTURE2D(_CausticsTex, sampler_CausticsTex, causticsUV_B).r;

                // Multiply the two layers for a more organic, shifting pattern
                float caustics = causticsA * causticsB * 4.0; // *4 because multiplying two 0-1 values dims it
                caustics = saturate(caustics);

                // Attenuate caustics by the inverse of fresnel — stronger at shallow/top-down view,
                // fading at glancing angles where you'd see more reflection than refraction.
                float causticsAtten = (1.0 - fresnel * 0.7);
                color.rgb += caustics * _CausticsIntensity * causticsAtten * _ShallowColor.rgb;

                color.a = _AlphaBase;

                return color;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/InternalErrorShader"
}
