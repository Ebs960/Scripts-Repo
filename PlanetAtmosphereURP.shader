Shader "Custom/PlanetAtmosphereURP"
{
    Properties
    {
        [Header(Atmosphere Colors)]
        _AtmosphereColor ("Atmosphere Color (Day)", Color) = (0.4, 0.6, 1.0, 1)
        _HorizonColor ("Horizon Color (Sunset)", Color) = (1.0, 0.7, 0.5, 1)
        _NightColor ("Atmosphere Color (Night)", Color) = (0.1, 0.15, 0.3, 1)
        
        [Header(Scattering)]
        _RayleighStrength ("Rayleigh Scattering (Blue Sky)", Range(0, 5)) = 1.5
        _MieStrength ("Mie Scattering (Haze)", Range(0, 3)) = 0.8
        _ScatteringPower ("Scattering Falloff", Range(1, 10)) = 3.0
        
        [Header(Atmosphere Properties)]
        _AtmosphereThickness ("Thickness", Range(0.01, 0.3)) = 0.08
        _Density ("Density", Range(0, 2)) = 1.0
        _Falloff ("Edge Falloff", Range(0.1, 5)) = 2.0
        
        [Header(Fresnel)]
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3.0
        _FresnelIntensity ("Fresnel Intensity", Range(0, 3)) = 1.5
        
        [Header(Advanced)]
        _MinAlpha ("Minimum Alpha", Range(0, 1)) = 0.0
        _MaxAlpha ("Maximum Alpha", Range(0, 1)) = 0.9
    }

    SubShader
    {
        Tags 
        { 
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderPipeline"="UniversalRenderPipeline"
        }

        Pass
        {
            Name "AtmospherePass"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back  // Render front faces (atmosphere shell viewed from outside)

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // Properties
            CBUFFER_START(UnityPerMaterial)
                half4 _AtmosphereColor;
                half4 _HorizonColor;
                half4 _NightColor;
                half _RayleighStrength;
                half _MieStrength;
                half _ScatteringPower;
                half _AtmosphereThickness;
                half _Density;
                half _Falloff;
                half _FresnelPower;
                half _FresnelIntensity;
                half _MinAlpha;
                half _MaxAlpha;
            CBUFFER_END

            // Per-instance properties (set via MaterialPropertyBlock)
            float3 _PlanetCenterWS;
            float _PlanetRadius;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                
                // Transform to world space
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                
                // Normal in world space
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                
                // View direction
                output.viewDirWS = GetWorldSpaceViewDir(vertexInput.positionWS);
                
                // Fog
                output.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
                
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Normalize vectors
                half3 N = normalize(input.normalWS);
                half3 V = normalize(input.viewDirWS);
                
                // Get main light direction
                Light mainLight = GetMainLight();
                half3 L = mainLight.direction;
                
                // Calculate position relative to planet center
                float3 planetToPoint = input.positionWS - _PlanetCenterWS;
                float distanceFromCenter = length(planetToPoint);
                half3 planetNormal = planetToPoint / distanceFromCenter;
                
                // ===== FRESNEL (Atmosphere Rim) =====
                half fresnel = 1.0 - saturate(dot(V, N));
                fresnel = pow(fresnel, _FresnelPower) * _FresnelIntensity;
                
                // ===== ATMOSPHERIC DEPTH =====
                // Calculate how much atmosphere we're looking through
                half viewAngle = saturate(dot(V, planetNormal));
                half atmosphereDepth = pow(1.0 - viewAngle, _Falloff) * _Density;
                
                // ===== DAY/NIGHT TRANSITION =====
                // Determine if this part of atmosphere is facing sun
                half sunAlignment = dot(planetNormal, L);
                half dayNightBlend = saturate(sunAlignment * 2.0 + 0.5); // Smooth transition
                
                // ===== SCATTERING =====
                // Rayleigh scattering (blue sky) - strongest when sun is overhead
                half rayleighFactor = pow(saturate(dot(V, L)), _ScatteringPower);
                half3 rayleighColor = lerp(_AtmosphereColor.rgb, half3(1,1,1), rayleighFactor * _RayleighStrength);
                
                // Mie scattering (sunset/horizon glow) - strongest at horizon
                half mieFactor = pow(saturate(1.0 - viewAngle), 2.0);
                half sunAlignment2 = saturate(dot(planetNormal, L));
                half3 mieColor = _HorizonColor.rgb * mieFactor * sunAlignment2 * _MieStrength;
                
                // ===== COMBINE COLORS =====
                // Blend day and night atmosphere
                half3 dayAtmosphere = rayleighColor + mieColor;
                half3 nightAtmosphere = _NightColor.rgb * 0.5; // Dimmer at night
                half3 finalColor = lerp(nightAtmosphere, dayAtmosphere, dayNightBlend);
                
                // ===== CALCULATE FINAL ALPHA =====
                // Combine atmospheric depth, fresnel, and density
                half finalAlpha = (atmosphereDepth + fresnel) * _Density;
                finalAlpha = saturate(finalAlpha);
                finalAlpha = lerp(_MinAlpha, _MaxAlpha, finalAlpha);
                
                // Fade out atmosphere on night side (but keep some visibility)
                finalAlpha *= lerp(0.3, 1.0, dayNightBlend);
                
                // Apply fog
                finalColor = MixFog(finalColor, input.fogFactor);
                
                return half4(finalColor, finalAlpha);
            }
            ENDHLSL
        }
        
        // Second pass for inner atmosphere (when camera is close/inside)
        Pass
        {
            Name "AtmosphereInnerPass"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Front  // Render back faces (atmosphere viewed from inside)

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // Properties
            CBUFFER_START(UnityPerMaterial)
                half4 _AtmosphereColor;
                half4 _HorizonColor;
                half4 _NightColor;
                half _RayleighStrength;
                half _MieStrength;
                half _ScatteringPower;
                half _AtmosphereThickness;
                half _Density;
                half _Falloff;
                half _FresnelPower;
                half _FresnelIntensity;
                half _MinAlpha;
                half _MaxAlpha;
            CBUFFER_END

            float3 _PlanetCenterWS;
            float _PlanetRadius;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceViewDir(vertexInput.positionWS);
                output.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
                
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Inverted normal for back face
                half3 N = normalize(-input.normalWS);
                half3 V = normalize(input.viewDirWS);
                
                Light mainLight = GetMainLight();
                half3 L = mainLight.direction;
                
                float3 planetToPoint = input.positionWS - _PlanetCenterWS;
                float distanceFromCenter = length(planetToPoint);
                half3 planetNormal = planetToPoint / distanceFromCenter;
                
                // Softer fresnel for inner atmosphere
                half fresnel = 1.0 - saturate(dot(V, N));
                fresnel = pow(fresnel, _FresnelPower * 0.5) * _FresnelIntensity * 0.5;
                
                // Day/night
                half sunAlignment = dot(planetNormal, L);
                half dayNightBlend = saturate(sunAlignment * 2.0 + 0.5);
                
                // Simpler color for inner atmosphere
                half3 finalColor = lerp(_NightColor.rgb * 0.3, _AtmosphereColor.rgb * 0.7, dayNightBlend);
                
                // Lower alpha for inner atmosphere
                half finalAlpha = fresnel * _Density * 0.3;
                finalAlpha = saturate(finalAlpha);
                finalAlpha = lerp(_MinAlpha, _MaxAlpha * 0.5, finalAlpha);
                
                finalColor = MixFog(finalColor, input.fogFactor);
                
                return half4(finalColor, finalAlpha);
            }
            ENDHLSL
        }
    }
    
    Fallback Off
}
