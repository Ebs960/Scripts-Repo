Shader "Custom/MenuPlanetClouds"
{
    Properties
    {
        _CloudDensity("Cloud Density", Range(0, 1)) = 0.5
        _CloudScale("Cloud Scale", Float) = 3.0
        _CloudSpeed("Cloud Speed", Float) = 0.08
        _CloudAltitude("Cloud Altitude", Float) = 0.015
        _Temperature("Temperature", Range(0, 1)) = 0.5
        _MapStyle("Map Style", Range(0, 1)) = 0.0
        _SunDirection("Sun Direction", Vector) = (-0.5, -0.7, 0.3, 0)
        _SunColor("Sun Color", Color) = (1, 0.95, 0.85, 1)
        _SunIntensity("Sun Intensity", Float) = 1.0
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

            Cull Back
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ UNITY_DOTS_INSTANCING_ENABLED

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _CloudDensity;
                float _CloudScale;
                float _CloudSpeed;
                float _CloudAltitude;
                float _Temperature;
                float _MapStyle;
                float4 _SunDirection;
                float4 _SunColor;
                float _SunIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // -----------------------------------------------------------------
            //  Noise
            // -----------------------------------------------------------------
            float hash31(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float noise3D(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = hash31(i);
                float n100 = hash31(i + float3(1,0,0));
                float n010 = hash31(i + float3(0,1,0));
                float n110 = hash31(i + float3(1,1,0));
                float n001 = hash31(i + float3(0,0,1));
                float n101 = hash31(i + float3(1,0,1));
                float n011 = hash31(i + float3(0,1,1));
                float n111 = hash31(i + float3(1,1,1));
                return lerp(
                    lerp(lerp(n000,n100,f.x), lerp(n010,n110,f.x), f.y),
                    lerp(lerp(n001,n101,f.x), lerp(n011,n111,f.x), f.y),
                    f.z);
            }

            float fbm3(float3 p)
            {
                float v = 0; float a = 0.5; float fr = 1.0;
                for (int i = 0; i < 3; i++)
                {
                    v += a * noise3D(p * fr);
                    fr *= 2.1;
                    a *= 0.45;
                }
                return v;
            }

            // -----------------------------------------------------------------
            //  Vertex — push sphere outward to cloud altitude
            // -----------------------------------------------------------------
            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                // Push outward along normal
                float3 displaced = input.positionOS.xyz + input.normalOS * _CloudAltitude;

                float3 worldPos = TransformObjectToWorld(displaced);
                output.positionCS = TransformWorldToHClip(worldPos);
                output.positionWS = worldPos;
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            // -----------------------------------------------------------------
            //  Fragment — procedural animated clouds
            // -----------------------------------------------------------------
            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float3 objNorm = normalize(input.positionOS);
                float timeVal = _Time.y;

                // Style blends
                float style    = saturate(_MapStyle);
                float infernal = saturate(style * 2.0);
                float demonic  = saturate((style - 0.5) * 2.0);
                float frozen   = saturate((0.15 - _Temperature) * 8.0);

                // ---- Cloud noise ----
                // Domain warp for more organic shapes
                float3 warp = float3(
                    noise3D(objNorm * 2.0 + float3(11.1, 33.3, 55.5)),
                    noise3D(objNorm * 2.0 + float3(77.7, 22.2, 44.4)),
                    noise3D(objNorm * 2.0 + float3(99.9, 66.6, 88.8))
                ) * 0.3;

                float3 samplePos = objNorm * _CloudScale + warp + float3(timeVal * _CloudSpeed, 0, timeVal * _CloudSpeed * 0.7);
                float cloudNoise = fbm3(samplePos);

                // Shape clouds: threshold controls coverage, density controls opacity
                float cloudMask = smoothstep(0.38, 0.58, cloudNoise) * _CloudDensity;

                // Add some wispy high-frequency detail
                float detail = noise3D(samplePos * 4.0 + float3(timeVal * _CloudSpeed * 2.0, 0, 0));
                cloudMask *= lerp(0.6, 1.0, detail);

                // ---- Lighting ----
                float3 normal   = normalize(input.normalWS);
                float3 lightDir = normalize(-_SunDirection.xyz);
                float NdotL     = dot(normal, lightDir);
                float sunSide   = saturate(NdotL * 0.5 + 0.5); // half-lambert for soft cloud shading

                // ---- Color by planet type ----
                // Normal: white clouds
                float3 cloudColor = float3(1.0, 1.0, 1.0) * lerp(0.45, 1.0, sunSide);

                // Frozen: icy blue tint, thinner
                float3 frozenCloud = float3(0.85, 0.92, 1.0) * lerp(0.35, 0.9, sunSide);
                cloudColor = lerp(cloudColor, frozenCloud, frozen);
                cloudMask *= lerp(1.0, 0.5, frozen); // thinner ice clouds

                // Infernal: dark smoke/ash with orange-lit edges
                float3 smokeColor = float3(0.15, 0.12, 0.10) * lerp(0.3, 0.8, sunSide);
                float emberEdge = smoothstep(0.35, 0.42, cloudNoise) * (1.0 - smoothstep(0.42, 0.55, cloudNoise));
                float3 emberColor = lerp(float3(0.8, 0.3, 0.05), float3(1.0, 0.5, 0.1),
                                         sin(timeVal * 2.0 + cloudNoise * 10.0) * 0.5 + 0.5);
                smokeColor = lerp(smokeColor, emberColor, emberEdge * 0.7);
                cloudColor = lerp(cloudColor, smokeColor, infernal);

                // Demonic: deep crimson haze with red glow at thin edges
                float3 crimsonHaze = float3(0.08, 0.03, 0.02) * lerp(0.3, 0.7, sunSide);
                float redEdge = emberEdge; // reuse edge mask
                float3 redGlow = float3(0.9, 0.08, 0.02) * (sin(timeVal * 3.0 + cloudNoise * 12.0) * 0.3 + 0.7);
                crimsonHaze = lerp(crimsonHaze, redGlow, redEdge * 0.8);
                cloudColor = lerp(cloudColor, crimsonHaze, demonic);

                // Apply sun color tinting
                float3 sunCol = _SunColor.rgb * _SunIntensity;
                cloudColor *= sunCol;

                // Fade at the terminator for realism (clouds on dark side are very dim)
                float nightFade = saturate(NdotL * 2.0 + 0.3);
                cloudMask *= lerp(0.08, 1.0, nightFade);

                return float4(cloudColor, saturate(cloudMask));
            }

            ENDHLSL
        }
    }

    Fallback Off
}
