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
        _AlphaBase ("Base Alpha", Range(0, 1)) = 0.70

        [Header(River Color)]
        _RiverShallowColor ("River Shallow Color", Color) = (0.20, 0.56, 0.86, 1)
        _RiverDeepColor ("River Deep Color", Color) = (0.08, 0.24, 0.36, 1)
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
            float4 _RiverShallowColor;
            float4 _RiverDeepColor;

            Varyings vert(Attributes input)
            {
                Varyings output;

                float3 posOS = input.positionOS;
                float3 tint = saturate(input.color.rgb);
                float waterType = round(saturate(input.color.a) * 3.0);
                bool isRiver = waterType >= 2.5;
                bool isLava = !isRiver && tint.r > 0.75 && tint.g < 0.4 && tint.b < 0.2;

                // Small sine bob based on world-space XZ
                float3 posWS = TransformObjectToWorld(posOS);
                float wave = sin(posWS.x * _WaveFrequency + _Time.y * 2.0)
                           * cos(posWS.z * _WaveFrequency * 0.7 + _Time.y * 1.5);
                posOS.y += wave * (isLava ? 0.0 : _WaveAmplitude);

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
                // Vertex alpha encodes water type: 1/3 ocean, 2/3 lake, 1 river.
                float waterType = round(saturate(input.vertexColor.a) * 3.0);
                bool isRiver = waterType >= 2.5;
                float3 stillTint = saturate(input.vertexColor.rgb);
                bool isLava = !isRiver && stillTint.r > 0.75 && stillTint.g < 0.4 && stillTint.b < 0.2;

                // Rivers use RG as flow direction. Still water uses RGB as a tint hint.
                float2 flowDir = isRiver ? input.vertexColor.rg * 2.0 - 1.0 : float2(0.0, 0.0);
                float dirLen2 = dot(flowDir, flowDir);
                float2 dirN = (dirLen2 > 1e-4) ? normalize(flowDir) : float2(0.0, 0.0);
                float flowFactor = (dirLen2 > 1e-4) ? 1.0 : 0.0; // zero dir => still water
                float2 flowOffset = dirN * (_FlowSpeed * _Time.y * flowFactor);

                // Scroll UVs for two normal maps
                float2 worldUV = input.positionWS.xz * 0.1; // world-space tiling
                float2 stillScrollA = isLava ? float2(0.0, 0.0) : _ScrollSpeedA.xy * _Time.y;
                float2 stillScrollB = isLava ? float2(0.0, 0.0) : _ScrollSpeedB.xy * _Time.y;
                float2 uvA = worldUV + stillScrollA + flowOffset;
                float2 uvB = worldUV + stillScrollB + flowOffset * 0.5;

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

                float3 shallowColor = _ShallowColor.rgb;
                float3 deepColor = _DeepColor.rgb;

                if (isRiver)
                {
                    // Rivers use dedicated shallow/deep colors to match lake appearance
                    shallowColor = _RiverShallowColor.rgb;
                    deepColor = _RiverDeepColor.rgb;
                }
                else if (!isLava)
                {
                    shallowColor = lerp(_ShallowColor.rgb, stillTint, 0.88);
                    deepColor = lerp(_DeepColor.rgb, stillTint * 0.42, 0.92);
                }

                // Color blend: shallow near edges, deep at center/steep angles
                float4 color = float4(lerp(shallowColor, deepColor, fresnel), 1.0);

                // --- Caustics ---
                // Two layers scrolling at different speeds/angles for a shimmering effect.
                // Uses world-space XZ so the pattern is seamless across hex tiles.
                float2 causticsUV_base = input.positionWS.xz * _CausticsTiling;
                float t = _Time.y * _CausticsSpeed;

                // Layer A: scroll diagonally
                float2 causticsUV_A = causticsUV_base + (isLava ? float2(0.0, 0.0) : float2(t * 0.7, t * 0.5));
                // Layer B: scroll in a different direction, slightly rotated
                float2 causticsUV_B = causticsUV_base * 1.15 + (isLava ? float2(0.0, 0.0) : float2(-t * 0.5, t * 0.8));

                float causticsA = SAMPLE_TEXTURE2D(_CausticsTex, sampler_CausticsTex, causticsUV_A).r;
                float causticsB = SAMPLE_TEXTURE2D(_CausticsTex, sampler_CausticsTex, causticsUV_B).r;

                // Multiply the two layers for a more organic, shifting pattern
                float caustics = causticsA * causticsB * 4.0; // *4 because multiplying two 0-1 values dims it
                caustics = saturate(caustics);

                // Attenuate caustics by the inverse of fresnel — stronger at shallow/top-down view,
                // fading at glancing angles where you'd see more reflection than refraction.
                float causticsAtten = (1.0 - fresnel * 0.7);
                color.rgb += caustics * _CausticsIntensity * causticsAtten * shallowColor;

                // Lava should render fully opaque while normal water stays noticeably less transparent.
                color.a = isLava ? 1.0 : saturate(max(_AlphaBase, 0.70));

                return color;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/InternalErrorShader"
}
