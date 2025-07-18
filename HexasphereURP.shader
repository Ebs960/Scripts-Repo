Shader "Custom/HexasphereURP"
{
    Properties
    {
        _BiomeAlbedoArray ("Biome Albedo Array", 2DArray) = "" {}
        _BiomeIndexTex    ("Tile → Biome map (Legacy)", 2D) = "white" {}
        _LatTintTex       ("Lat Tint Tex", 2D) = "white" {}          // <‑ new
        _LatTintStrength  ("Lat Tint Strength", Range(0,1)) = 0.35    // <‑ new
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

            /* ───── sampler declarations ───── */
            Texture2DArray _BiomeAlbedoArray;   SamplerState sampler_BiomeAlbedoArray;
            Texture2D     _BiomeIndexTex;       SamplerState sampler_BiomeIndexTex;
            Texture2D     _LatTintTex;          SamplerState sampler_LatTintTex;

            /* ───── material params ───── */
            CBUFFER_START(UnityPerMaterial)
                float _BiomeCount;
                float _SharpBoundaries;
                float _UsePerTileBiomeData;
                float _LatTintStrength;
                float _SphereRadius;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;      // biome index in .r
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float  biomeId     : TEXCOORD1; // pass as scalar
                float  yPos        : TEXCOORD2; // world‑space Y for latitude tint
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(posWS);
                OUT.uv      = IN.uv;
                OUT.biomeId = IN.color.r;
                OUT.yPos    = posWS.y;          // capture Y in world units
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                /* ──── biome lookup ──── */
                float biomeId;
                if (_UsePerTileBiomeData > 0.5)
                    biomeId = IN.biomeId;
                else
                {
                    float sample = (_SharpBoundaries>0.5) ?
                        _BiomeIndexTex.SampleLevel(sampler_BiomeIndexTex, IN.uv, 0).r :
                        _BiomeIndexTex.Sample      (sampler_BiomeIndexTex, IN.uv).r;
                    biomeId = sample;
                }

                int slice = (int)round(saturate(biomeId) * (_BiomeCount-1));

                float3 biomeCol = _BiomeAlbedoArray.Sample(
                                    sampler_BiomeAlbedoArray,
                                    float3(IN.uv, slice)).rgb;

                /* ──── latitude tint ──── */
                // World Y mapped to 0…1 where 0 = equator, 1 = pole
                float lat01 = saturate(abs(IN.yPos) / _SphereRadius); // use property for radius
                float3 latCol = _LatTintTex.Sample(sampler_LatTintTex, float2(lat01,0.5)).rgb;

                biomeCol = lerp(biomeCol, latCol, _LatTintStrength);

                return half4(biomeCol, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}