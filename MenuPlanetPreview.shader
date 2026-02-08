Shader "Custom/MenuPlanetPreview"
{
    Properties
    {
        _LandScale("Land Scale", Range(0.5, 5.0)) = 2.0
        _LandThreshold("Land Threshold", Range(0, 1)) = 0.4
        _Temperature("Temperature", Range(0, 1)) = 0.5
        _Moisture("Moisture", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        // =====================================================================
        //  Forward rendering pass — main color output
        // =====================================================================
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ UNITY_DOTS_INSTANCING_ENABLED

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            // SRP Batcher compatible CBUFFER
            CBUFFER_START(UnityPerMaterial)
                float _LandScale;
                float _LandThreshold;
                float _Temperature;
                float _Moisture;
            CBUFFER_END

            // -----------------------------------------------------------------
            //  Structs
            // -----------------------------------------------------------------
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
            //  Noise — value noise with smooth interpolation (3D)
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
                f = f * f * (3.0 - 2.0 * f); // smoothstep

                float n000 = hash31(i + float3(0, 0, 0));
                float n100 = hash31(i + float3(1, 0, 0));
                float n010 = hash31(i + float3(0, 1, 0));
                float n110 = hash31(i + float3(1, 1, 0));
                float n001 = hash31(i + float3(0, 0, 1));
                float n101 = hash31(i + float3(1, 0, 1));
                float n011 = hash31(i + float3(0, 1, 1));
                float n111 = hash31(i + float3(1, 1, 1));

                return lerp(
                    lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                    lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y),
                    f.z
                );
            }

            // Fractal Brownian Motion — 4 octaves gives nice organic blobs
            float fbm(float3 p)
            {
                float value = 0.0;
                float amp   = 0.5;
                float freq  = 1.0;
                for (int i = 0; i < 4; i++)
                {
                    value += amp * noise3D(p * freq);
                    freq  *= 2.0;
                    amp   *= 0.5;
                }
                return value;
            }

            // -----------------------------------------------------------------
            //  Color helpers
            // -----------------------------------------------------------------
            float3 GetLandColor(float temp, float moist)
            {
                // Temperature gradient: cold(0) → temperate(0.5) → hot(1)
                float3 coldColor = float3(0.78, 0.85, 0.90); // icy blue-gray
                float3 tempColor = float3(0.30, 0.58, 0.22); // rich green
                float3 hotColor  = float3(0.82, 0.72, 0.42); // sandy tan

                float t1 = saturate(temp * 2.0);
                float t2 = saturate((temp - 0.5) * 2.0);
                float3 baseColor = lerp(lerp(coldColor, tempColor, t1), hotColor, t2);

                // Moisture: dry → browner/duller, wet → greener/richer
                float3 dryShift = float3(0.80, 0.68, 0.50);
                float3 wetShift = float3(0.40, 0.75, 0.35);
                float3 moistTint = lerp(dryShift, wetShift, moist);
                baseColor = saturate(baseColor * moistTint * 1.7);

                return baseColor;
            }

            float3 GetOceanColor(float temp)
            {
                float3 coldOcean = float3(0.08, 0.15, 0.30); // dark slate blue
                float3 warmOcean = float3(0.06, 0.22, 0.45); // medium blue
                float3 hotOcean  = float3(0.10, 0.32, 0.52); // tropical blue

                float t1 = saturate(temp * 2.0);
                float t2 = saturate((temp - 0.5) * 2.0);
                return lerp(lerp(coldOcean, warmOcean, t1), hotOcean, t2);
            }

            // -----------------------------------------------------------------
            //  Vertex
            // -----------------------------------------------------------------
            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(worldPos);
                output.positionWS = worldPos;
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                output.positionOS = input.positionOS.xyz;

                return output;
            }

            // -----------------------------------------------------------------
            //  Fragment
            // -----------------------------------------------------------------
            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // Sample noise at object-space position (seamless on sphere surface)
                float3 samplePos = normalize(input.positionOS) * _LandScale;
                float n = fbm(samplePos + float3(42.3, 17.1, 83.7));

                // Soft edge between land and ocean
                float edge = smoothstep(_LandThreshold - 0.04, _LandThreshold + 0.04, n);

                // Base colors
                float3 landColor  = GetLandColor(_Temperature, _Moisture);
                float3 oceanColor = GetOceanColor(_Temperature);
                float3 albedo = lerp(oceanColor, landColor, edge);

                // Optional: tiny blue speckles on land at high moisture (fake lakes)
                float lakeNoise = noise3D(samplePos * 12.0 + float3(7.7, 3.3, 9.9));
                float lakeMask  = smoothstep(0.72, 0.78, lakeNoise)
                                * saturate((_Moisture - 0.6) * 2.5)
                                * step(0.5, edge); // only on land
                float3 lakeColor = float3(0.12, 0.30, 0.50);
                albedo = lerp(albedo, lakeColor, saturate(lakeMask));

                // ---- Soft directional lighting ----
                float3 normal   = normalize(input.normalWS);
                float3 lightDir = normalize(float3(0.5, 0.7, -0.3));

                // Wrap diffuse for soft look (no hard terminator)
                float NdotL   = dot(normal, lightDir);
                float diffuse = saturate(NdotL * 0.6 + 0.4);

                // Subtle ambient from below to fill shadow side
                float ambient = saturate(normal.y * -0.15 + 0.20);
                float lighting = diffuse + ambient;

                // Subtle specular highlight on ocean
                // HDRP uses camera-relative rendering: camera is at origin
                float3 viewDir = normalize(-input.positionWS);
                float3 halfVec = normalize(lightDir + viewDir);
                float spec = pow(saturate(dot(normal, halfVec)), 48.0)
                           * (1.0 - edge)  // ocean only
                           * 0.35;

                float3 finalColor = albedo * lighting + spec;

                return float4(finalColor, 1.0);
            }

            ENDHLSL
        }

        // =====================================================================
        //  Depth pre-pass — required for HDRP forward rendering
        // =====================================================================
        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }

            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile_instancing
            #pragma multi_compile _ UNITY_DOTS_INSTANCING_ENABLED

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vertDepth(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(worldPos);
                return output;
            }

            float4 fragDepth(Varyings input) : SV_Target
            {
                return 0;
            }

            ENDHLSL
        }

        // =====================================================================
        //  Shadow caster — lets the sphere cast shadows if needed
        // =====================================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_instancing
            #pragma multi_compile _ UNITY_DOTS_INSTANCING_ENABLED

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vertShadow(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(worldPos);
                return output;
            }

            float4 fragShadow(Varyings input) : SV_Target
            {
                return 0;
            }

            ENDHLSL
        }
    }

    Fallback "Hidden/InternalErrorShader"
}
