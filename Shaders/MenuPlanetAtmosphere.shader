Shader "Custom/MenuPlanetAtmosphere"
{
    Properties
    {
        _AtmosphereColor("Atmosphere Color", Color) = (0.4, 0.65, 1.0, 1)
        _AtmosphereFalloff("Atmosphere Falloff", Range(1, 8)) = 3.5
        _AtmosphereIntensity("Atmosphere Intensity", Range(0, 3)) = 1.2
        _AtmosphereDayRimBoost("Atmosphere Day Rim Boost", Range(0,2)) = 1.15
        _AtmosphereNightRimStrength("Atmosphere Night Rim Strength", Range(0,1)) = 0.42
        _AtmosphereInnerScatterStrength("Atmosphere Inner Scatter Strength", Range(0,1)) = 0.12
        _AtmosLightDirectionWS("Atmosphere Light Direction WS", Vector) = (0.45,0.65,0.55,0)
        _Temperature("Temperature", Range(0, 1)) = 0.5
        _MapStyle("Map Style", Range(0, 1)) = 0.0
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

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _AtmosphereColor;
                float _AtmosphereFalloff;
                float _AtmosphereIntensity;
                float _AtmosphereDayRimBoost;
                float _AtmosphereNightRimStrength;
                float _AtmosphereInnerScatterStrength;
                float4 _AtmosLightDirectionWS;
                float _Temperature;
                float _MapStyle;
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

                float3 lightDir = normalize(_AtmosLightDirectionWS.xyz);
                float dayFacing = saturate(dot(normal, lightDir) * 0.5 + 0.5);
                float lightMix = lerp(_AtmosphereNightRimStrength, _AtmosphereDayRimBoost, dayFacing);
                float innerScatter = pow(saturate(1.0 - fresnel), 3.5) * _AtmosphereInnerScatterStrength * lerp(0.5, 1.0, dayFacing);

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

                // --- Final ---
                // Atmosphere intensity modulated by rim
                float alpha = (rim * lightMix + innerScatter) * _AtmosphereIntensity;

                // Clamp so it doesn't blow out
                alpha = min(alpha, 1.5);

                return float4(atmosCol * alpha, 0.0); // alpha unused for additive
            }

            ENDHLSL
        }
    }

    Fallback Off
}
