Shader "Custom/MenuPlanetAtmosphere"
{
    Properties
    {
        _AtmosphereColor("Atmosphere Color", Color) = (0.4, 0.65, 1.0, 1)
        _AtmosphereFalloff("Atmosphere Falloff", Range(1, 8)) = 3.5
        _AtmosphereIntensity("Atmosphere Intensity", Range(0, 3)) = 1.2
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
            "Queue" = "Transparent+1"
        }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }

            Cull Back
            ZWrite Off
            ZTest LEqual
            Blend One One   // Additive — atmosphere glows against black space

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
                float4 _AtmosphereColor;
                float _AtmosphereFalloff;
                float _AtmosphereIntensity;
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(worldPos);
                output.positionWS = worldPos;
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float3 normal  = normalize(input.normalWS);
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);

                // --- Fresnel-driven rim alpha ---
                float fresnel = 1.0 - saturate(dot(viewDir, normal));
                float rim = pow(fresnel, _AtmosphereFalloff);

                // --- Sun-side brightening ---
                // The atmosphere is visible all the way around (additive), but
                // the day side is brighter — scattering is stronger toward the sun.
                float3 lightDir = normalize(-_SunDirection.xyz);
                float NdotL     = dot(normal, lightDir);

                // Day side boost: atmosphere is ~2.5x brighter where the sun
                // hits, with a smooth wrap so the terminator "thin blue line" is visible.
                float dayBoost  = saturate(NdotL * 0.6 + 0.5);
                float lightMix  = lerp(0.25, 1.0, dayBoost); // dark side still 25% visible

                // --- Style-adaptive color ---
                float style    = saturate(_MapStyle);
                float infernal = saturate(style * 2.0);
                float demonic  = saturate((style - 0.5) * 2.0);
                float frozen   = saturate((0.15 - _Temperature) * 8.0);

                // Base atmosphere color (blue Rayleigh scatter)
                float3 atmosCol = _AtmosphereColor.rgb;

                // Frozen: pale icy blue
                float3 frozenAtmos = float3(0.6, 0.78, 0.95);
                atmosCol = lerp(atmosCol, frozenAtmos, frozen);

                // Infernal: orange ember haze
                float3 infernalAtmos = float3(0.85, 0.25, 0.05);
                atmosCol = lerp(atmosCol, infernalAtmos, infernal);

                // Demonic: deep blood-red haze with pulsing
                float3 demonicAtmos = lerp(float3(0.7, 0.04, 0.01), float3(0.9, 0.12, 0.02),
                                           sin(_Time.y * 1.5) * 0.5 + 0.5);
                atmosCol = lerp(atmosCol, demonicAtmos, demonic);

                // Tint by sun color
                float3 sunCol = _SunColor.rgb * _SunIntensity;
                atmosCol *= sunCol;

                // --- Final ---
                // Atmosphere intensity modulated by rim and sun side
                float alpha = rim * _AtmosphereIntensity * lightMix;

                // Clamp so it doesn't blow out
                alpha = min(alpha, 1.5);

                return float4(atmosCol * alpha, 0.0); // alpha unused for additive
            }

            ENDHLSL
        }
    }

    Fallback Off
}
