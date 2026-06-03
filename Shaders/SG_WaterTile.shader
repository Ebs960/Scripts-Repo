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

        [Header(Freeze)]
        _FreezeProgress ("Freeze Progress", Range(0, 1)) = 0
        _FreezeOpaqueThreshold ("Freeze Opaque Threshold", Range(0.5, 1)) = 0.9
        _IceAlbedoArray ("Ice Albedo Array", 2DArray) = "" {}
        _IceNormalArray ("Ice Normal Array", 2DArray) = "" {}
        _IceSliceCount ("Ice Slice Count", Float) = 0
        _LakeIceTint ("Lake Ice Tint", Color) = (1, 1, 1, 1)
        _RiverIceTint ("River Ice Tint", Color) = (1, 1, 1, 1)
        _LakeIceTiling ("Lake Ice Tiling", Float) = 8
        _RiverIceTiling ("River Ice Tiling", Float) = 12
        _IceNormalStrength ("Ice Normal Strength", Range(0, 3)) = 1
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
                float4 freezeData : TEXCOORD1;
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
                float4 freezeData : TEXCOORD5;
            };

            TEXTURE2D(_NormalMapA);
            SAMPLER(sampler_NormalMapA);
            TEXTURE2D(_NormalMapB);
            SAMPLER(sampler_NormalMapB);
            TEXTURE2D(_CausticsTex);
            SAMPLER(sampler_CausticsTex);
            TEXTURE2D_ARRAY(_IceAlbedoArray);
            SAMPLER(sampler_IceAlbedoArray);
            TEXTURE2D_ARRAY(_IceNormalArray);
            SAMPLER(sampler_IceNormalArray);

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
            float _FreezeProgress;
            float _FreezeOpaqueThreshold;
            float _IceSliceCount;
            float4 _LakeIceTint;
            float4 _RiverIceTint;
            float _LakeIceTiling;
            float _RiverIceTiling;
            float _IceNormalStrength;

            float Hash11(float value)
            {
                return frac(sin(value * 12.9898 + 78.233) * 43758.5453);
            }

            float3 SampleIceNormal(TEXTURE2D_ARRAY_PARAM(tex, samp), float2 uv, float slice)
            {
                float3 sampled = UnpackNormal(SAMPLE_TEXTURE2D_ARRAY(tex, samp, uv, slice));
                sampled.xy *= _IceNormalStrength;
                sampled.z = sqrt(saturate(1.0 - dot(sampled.xy, sampled.xy)));
                return sampled;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;

                float3 posOS = input.positionOS;
                float3 tint = saturate(input.color.rgb);
                float waterType = round(saturate(input.color.a) * 3.0);
                bool isRiver = waterType >= 2.5;
                bool isLava = !isRiver && tint.r > 0.75 && tint.g < 0.4 && tint.b < 0.2;
                float freezeAmount = saturate(max(input.freezeData.x * _FreezeProgress, input.freezeData.y));

                // Small sine bob based on world-space XZ
                float3 posWS = TransformObjectToWorld(posOS);
                float wave = sin(posWS.x * _WaveFrequency + _Time.y * 2.0)
                           * cos(posWS.z * _WaveFrequency * 0.7 + _Time.y * 1.5);
                posOS.y += wave * (isLava ? 0.0 : _WaveAmplitude * (1.0 - freezeAmount));

                output.positionWS = TransformObjectToWorld(posOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceNormalizeViewDir(output.positionWS);
                output.uv = input.uv;
                output.vertexColor = input.color;
                output.freezeData = input.freezeData;

                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                // Vertex alpha encodes water type: 1/3 ocean, 2/3 lake, 1 river.
                float waterType = round(saturate(input.vertexColor.a) * 3.0);
                bool isRiver = waterType >= 2.5;
                bool isLake = !isRiver && waterType >= 1.5;
                float3 stillTint = saturate(input.vertexColor.rgb);
                bool isLava = !isRiver && stillTint.r > 0.75 && stillTint.g < 0.4 && stillTint.b < 0.2;
                float freezeTarget = saturate(input.freezeData.x);
                float persistedFreeze = saturate(input.freezeData.y);
                float freezeVariant = frac(input.freezeData.z + Hash11(input.positionWS.x + input.positionWS.z));
                float freezeAmount = saturate(max(freezeTarget * _FreezeProgress, persistedFreeze));
                float solidIceBlend = saturate((freezeAmount - _FreezeOpaqueThreshold) / max(1.0 - _FreezeOpaqueThreshold, 0.001));

                // Rivers use RG as flow direction. Still water uses RGB as a tint hint.
                float2 flowDir = isRiver ? input.vertexColor.rg * 2.0 - 1.0 : float2(0.0, 0.0);
                float dirLen2 = dot(flowDir, flowDir);
                float2 dirN = (dirLen2 > 1e-4) ? normalize(flowDir) : float2(0.0, 0.0);
                float flowFactor = (dirLen2 > 1e-4) ? 1.0 : 0.0; // zero dir => still water
                float2 flowOffset = dirN * (_FlowSpeed * _Time.y * flowFactor);

                // Scroll UVs for two normal maps
                float2 worldUV = input.positionWS.xz * 0.1; // world-space tiling
                float2 stillScrollA = float2(0.0, 0.0);
                float2 stillScrollB = float2(0.0, 0.0);
                float2 uvA = worldUV + stillScrollA + (isRiver ? flowOffset : float2(0.0, 0.0));
                float2 uvB = worldUV + stillScrollB + (isRiver ? flowOffset * 0.5 : float2(0.0, 0.0));

                // Sample and blend normals
                float3 normalA = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMapA, sampler_NormalMapA, uvA));
                float3 normalB = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMapB, sampler_NormalMapB, uvB));
                float3 blendedNormal = normalize(float3(
                    (normalA.xy + normalB.xy) * _NormalStrength,
                    1.0
                ));

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

                // All non-lava water should render as exactly one color.
                float4 color = isLava
                    ? float4(shallowColor, 1.0)
                    : float4(_ShallowColor.rgb, 1.0);

                float3 viewDir = normalize(input.viewDirWS);
                float3 worldNormal = normalize(input.normalWS + float3(blendedNormal.x, 0, blendedNormal.y));

                if (!isLava && (isRiver || isLake) && freezeAmount > 0.001 && _IceSliceCount > 0.5)
                {
                    bool useRiverIce = isRiver;
                    float slice = max(0.0, floor(freezeVariant * max(_IceSliceCount, 1.0)));
                    slice = min(slice, max(_IceSliceCount - 1.0, 0.0));
                    float tiling = max(useRiverIce ? _RiverIceTiling : _LakeIceTiling, 0.01);
                    float2 iceUV = input.positionWS.xz * tiling;
                    float3 tint = useRiverIce ? _RiverIceTint.rgb : _LakeIceTint.rgb;

                    float3 iceAlbedo = SAMPLE_TEXTURE2D_ARRAY(_IceAlbedoArray, sampler_IceAlbedoArray, iceUV, slice).rgb * tint;
                    float3 iceNormalTS = SampleIceNormal(TEXTURE2D_ARRAY_ARGS(_IceNormalArray, sampler_IceNormalArray), iceUV, slice);
                    float3 iceWorldNormal = normalize(input.normalWS + float3(iceNormalTS.x, 0, iceNormalTS.y));

                    float finalFreezeBlend = saturate(max(freezeAmount, solidIceBlend));
                    color.rgb = lerp(color.rgb, iceAlbedo, finalFreezeBlend);
                    worldNormal = normalize(lerp(worldNormal, iceWorldNormal, finalFreezeBlend));
                }

                float fresnel = pow(1.0 - saturate(dot(viewDir, worldNormal)), _FresnelPower);
                if (isLava)
                    color.rgb = lerp(shallowColor, deepColor, fresnel);

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
                if (isLava)
                    color.rgb += caustics * _CausticsIntensity * causticsAtten * shallowColor;

                // Lava should render fully opaque while frozen water becomes opaque as it solidifies.
                color.a = isLava ? 1.0 : lerp(saturate(max(_AlphaBase, 0.70)), 1.0, solidIceBlend);

                return color;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/InternalErrorShader"
}
