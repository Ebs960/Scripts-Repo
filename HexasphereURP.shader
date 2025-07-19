Shader "Custom/HexasphereURP"
{
    Properties
    {
        _BiomeAlbedoArray ("Biome Albedo Array", 2DArray) = "" {}
        _BiomeNormalArray ("Biome Normal Array", 2DArray) = "" {}
        _BiomeIndexTex    ("Tile → Biome map (Legacy)", 2D) = "white" {}
        _BiomeDetail      ("Biome Detail", 2D) = "white" {}
        _LatTintTex       ("Lat Tint Tex", 2D) = "white" {}
        _LatTintStrength  ("Lat Tint Strength", Range(0,1)) = 0.35
        _DetailStrength   ("Detail Strength", Range(0,1)) = 0.4
        _NormalStrength   ("Normal Strength", Range(0,2)) = 1
        _BiomeCount       ("BiomeCount", Float) = 1
        [Toggle] _SharpBoundaries ("Sharp Biome Boundaries", Float) = 0
        [Toggle] _UsePerTileBiomeData ("Use Per‑Tile Biome Data", Float) = 1
        _SphereRadius     ("Sphere Radius", Float) = 1.0              // <‑ new
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalRenderPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   4.5
            #pragma require  2darray

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            /* ───── sampler declarations ───── */
            Texture2DArray _BiomeAlbedoArray;   SamplerState sampler_BiomeAlbedoArray;
            Texture2DArray _BiomeNormalArray;   SamplerState sampler_BiomeNormalArray;
            Texture2D     _BiomeIndexTex;       SamplerState sampler_BiomeIndexTex;
            Texture2D     _BiomeDetail;         SamplerState sampler_BiomeDetail;
            Texture2D     _LatTintTex;          SamplerState sampler_LatTintTex;

            /* ───── material params ───── */
            CBUFFER_START(UnityPerMaterial)
                float _BiomeCount;
                float _SharpBoundaries;
                float _UsePerTileBiomeData;
                float _LatTintStrength;
                float _DetailStrength;
                float _NormalStrength;
                float _SphereRadius;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv1        : TEXCOORD1;
                float4 color      : COLOR;      // biome index in .r
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv0         : TEXCOORD0;
                float2 uvLat       : TEXCOORD1;
                float3 nWS         : NORMAL;
                float3 wPos        : TEXCOORD2;
                float4 col         : COLOR;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                float3 world = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(world);
                OUT.uv0   = IN.uv;
                OUT.uvLat = IN.uv1;
                OUT.nWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.wPos = world;
                OUT.col = IN.color;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                /* 1️⃣ biome albedo as before */
                float biomeId;
                if (_UsePerTileBiomeData > 0.5)
                    biomeId = IN.col.r;
                else
                {
                    float sample = (_SharpBoundaries>0.5) ?
                        _BiomeIndexTex.SampleLevel(sampler_BiomeIndexTex, IN.uvLat, 0).r :
                        _BiomeIndexTex.Sample      (sampler_BiomeIndexTex, IN.uvLat).r;
                    biomeId = sample;
                }

                int slice = (int)round(biomeId * (_BiomeCount-1));
                half3 col = _BiomeAlbedoArray.Sample(
                                    sampler_BiomeAlbedoArray,
                                    float3(IN.uv0, slice)).rgb;

                // Sample normal map and compute world normal
                half3 nTex = _BiomeNormalArray.Sample(
                                    sampler_BiomeNormalArray,
                                    float3(IN.uv0, slice)).xyz * 2 - 1;
                nTex.xy *= _NormalStrength;
                half3 T = normalize(float3(-IN.nWS.z, 0, IN.nWS.x));
                half3 B = normalize(cross(IN.nWS, T));
                half3 N = normalize(nTex.x * T + nTex.y * B + nTex.z * IN.nWS);

                /* 2️⃣ per-tile planar detail overlay */
                half detail = _BiomeDetail.Sample(sampler_BiomeDetail, IN.uv0).r;
                col = lerp(col, col * detail, _DetailStrength);

                /* 3️⃣ latitude tint (latitude = n.y) */
                float latU = IN.uvLat.y;
                half3 latTint = _LatTintTex.Sample(sampler_LatTintTex, float2(latU,0)).rgb;
                col = lerp(col, latTint, _LatTintStrength);

                // Basic Lambert lighting using main light
                float3 lightDir = normalize(_MainLightPosition.xyz);
                float3 lightColor = _MainLightColor.rgb;
                half NdotL = saturate(dot(N, lightDir));
                half3 litCol = col * (lightColor * NdotL + lightColor * 0.2);

                return half4(litCol, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}