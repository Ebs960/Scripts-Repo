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
        _HeightMapTex     ("Height Map", 2D)    = "black" {}
        _DetailHeightTex  ("Detail Height", 2D) = "grey"  {}
        _HeightAmp        ("Height Amp", Range(0,1)) = 0.3
        _DetailAmp        ("Detail Amp", Range(0,0.2)) = 0.06
        _DetailFreq       ("Detail Freq", Float) = 32.0
        _NoiseSeed        ("Noise Seed", Float) = 0.0
        _TessFactor       ("Tessellation", Range(1,64)) = 8
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
            #pragma hull     hull
            #pragma domain   domain
            #pragma tessfactor 8

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            /* ───── sampler declarations ───── */
            Texture2DArray _BiomeAlbedoArray;   SamplerState sampler_BiomeAlbedoArray;
            Texture2DArray _BiomeNormalArray;   SamplerState sampler_BiomeNormalArray;
            Texture2D     _BiomeIndexTex;       SamplerState sampler_BiomeIndexTex;
            Texture2D     _BiomeDetail;         SamplerState sampler_BiomeDetail;
            Texture2D     _LatTintTex;          SamplerState sampler_LatTintTex;
            Texture2D     _HeightMapTex;        SamplerState sampler_HeightMapTex;
            Texture2D     _DetailHeightTex;     SamplerState sampler_DetailHeightTex;

            /* ───── material params ───── */
            CBUFFER_START(UnityPerMaterial)
                float _BiomeCount;
                float _SharpBoundaries;
                float _UsePerTileBiomeData;
                float _LatTintStrength;
                float _DetailStrength;
                float _NormalStrength;
                float _SphereRadius;
                float _HeightAmp;
                float _DetailAmp;
                float _DetailFreq;
                float _NoiseSeed;
                float _TessFactor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv1        : TEXCOORD1;
            float4 color      : COLOR;      // biome index in .r
            };

            // ----- 3D value noise -----
            float hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float valueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = hash(i);
                float n100 = hash(i + float3(1,0,0));
                float n010 = hash(i + float3(0,1,0));
                float n110 = hash(i + float3(1,1,0));
                float n001 = hash(i + float3(0,0,1));
                float n101 = hash(i + float3(1,0,1));
                float n011 = hash(i + float3(0,1,1));
                float n111 = hash(i + float3(1,1,1));

                float n00 = lerp(n000, n100, f.x);
                float n10 = lerp(n010, n110, f.x);
                float n01 = lerp(n001, n101, f.x);
                float n11 = lerp(n011, n111, f.x);
                float n0 = lerp(n00, n10, f.y);
                float n1 = lerp(n01, n11, f.y);
                return lerp(n0, n1, f.z);
            }

            float fbm(float3 p, int octaves = 3, float lacunarity = 2.0, float gain = 0.5)
            {
                float a = 0.0; float f = 1.0; float amp = 0.5;
                for (int i = 0; i < octaves; i++)
                {
                    a  += amp * valueNoise(p * f);
                    f  *= lacunarity;
                    amp*= gain;
                }
                return a;
            }

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv0         : TEXCOORD0;
                float2 uvLat       : TEXCOORD1;
                float3 nWS         : NORMAL;
                float3 wPos        : TEXCOORD2;
                float3 positionOS  : TEXCOORD3;
                float4 col         : COLOR;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.uv0   = IN.uv;
                OUT.uvLat = IN.uv1;
                OUT.nWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionOS = IN.positionOS.xyz;
                OUT.col = IN.color;

#ifndef UNITY_DOMAIN_SHADER
                float3 dirOS = normalize(IN.positionOS.xyz);

                float2 hUV;
                hUV.x = (atan2(dirOS.x, dirOS.z) / PI + 1.0) * 0.5;
                hUV.y = asin(dirOS.y) / PI + 0.5;

                float hMacro = _HeightMapTex.SampleLevel(sampler_HeightMapTex, hUV, 0).r;

                float hMicro;
#ifdef _USE_TEXTURE_DETAIL
                hMicro = _DetailHeightTex.Sample(sampler_DetailHeightTex, hUV).r;
#else
                hMicro = fbm(dirOS * _DetailFreq + _NoiseSeed);
#endif

                float h = hMacro + (hMicro - 0.5) * 2.0 * _DetailAmp;
                float disp = h * _HeightAmp * _SphereRadius;

                float3 displaced = dirOS * (_SphereRadius + disp);

                float3 world = TransformObjectToWorld(displaced);
                OUT.positionHCS = TransformWorldToHClip(world);
                OUT.wPos = world;
#else
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.wPos = TransformObjectToWorld(IN.positionOS.xyz);
#endif
                return OUT;
            }

            // constant hull – same tess factor for all 3 verts
            struct HS_CONSTANT_OUT { float TessFactor[3] : SV_TessFactor; };

            HS_CONSTANT_OUT hull (InputPatch<Varyings,3> patch, uint pid : SV_PrimitiveID)
            {
                HS_CONSTANT_OUT o;
                float tess = _TessFactor;
                o.TessFactor[0] = o.TessFactor[1] = o.TessFactor[2] = tess;
                return o;
            }

            // pass-through hull function
            [domain("tri")] [partitioning("integer")] [outputtopology("triangle_cw")]
            [patchconstantfunc("hull")]
            void hull (inout InputPatch<Varyings,3> patch, uint i : SV_OutputControlPointID,
                       out Varyings o)
            {
                o = patch[i];
            }

            [domain("tri")]
            Varyings domain (HS_CONSTANT_OUT pc,
                             const OutputPatch<Varyings,3> patch,
                             float3 bary : SV_DomainLocation)
            {
                Varyings v;
                v.positionOS = patch[0].positionOS * bary.x +
                               patch[1].positionOS * bary.y +
                               patch[2].positionOS * bary.z;

                float3 dirOS = normalize(v.positionOS.xyz);

                float2 hUV;
                hUV.x = (atan2(dirOS.x, dirOS.z) / PI + 1.0) * 0.5;
                hUV.y = asin(dirOS.y) / PI + 0.5;

                float hMacro = _HeightMapTex.SampleLevel(sampler_HeightMapTex, hUV, 0).r;

                float hMicro;
#ifdef _USE_TEXTURE_DETAIL
                hMicro = _DetailHeightTex.Sample(sampler_DetailHeightTex, hUV).r;
#else
                hMicro = fbm(dirOS * _DetailFreq + _NoiseSeed);
#endif

                float h = hMacro + (hMicro - 0.5) * 2.0 * _DetailAmp;
                float disp = h * _HeightAmp * _SphereRadius;

                float3 displaced = dirOS * (_SphereRadius + disp);

                float3 world = TransformObjectToWorld(displaced);
                v.positionHCS = TransformWorldToHClip(world);
                v.wPos = world;
                v.uv0   = patch[0].uv0   * bary.x + patch[1].uv0   * bary.y + patch[2].uv0   * bary.z;
                v.uvLat = patch[0].uvLat * bary.x + patch[1].uvLat * bary.y + patch[2].uvLat * bary.z;
                v.nWS = normalize(patch[0].nWS * bary.x + patch[1].nWS * bary.y + patch[2].nWS * bary.z);
                v.col = patch[0].col * bary.x + patch[1].col * bary.y + patch[2].col * bary.z;
                return v;
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