Shader "Custom/GlobeMap"
{
    Properties
    {
        _MainTex ("Flat Map Texture", 2D) = "white" {}
        _Heightmap ("Heightmap", 2D) = "black" {}
        _FogMask ("Fog Mask", 2D) = "white" {}
        _OwnershipOverlay ("Ownership Overlay", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _Smoothness ("Smoothness", Range(0,1)) = 0.3
        _LightIntensity ("Light Intensity", Range(0,2)) = 1.0
        _DisplacementStrength ("Displacement Strength", Range(0,1)) = 0.1
        _PlanetRadius ("Planet Radius", Float) = 21.0
        [Toggle] _EnableFog ("Enable Fog", Float) = 1.0
        [Toggle] _EnableOwnership ("Enable Ownership", Float) = 1.0
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0
        
        sampler2D _MainTex;
        sampler2D _Heightmap;
        sampler2D _FogMask;
        sampler2D _OwnershipOverlay;
        fixed4 _Color;
        half _Metallic;
        half _Smoothness;
        half _LightIntensity;
        half _DisplacementStrength;
        float _PlanetRadius;
        float _EnableFog;
        float _EnableOwnership;
        
        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float3 worldNormal;
        };
        
        // Vertex shader: displace vertices based on heightmap
        // GPU-based displacement - works with both Texture2D (R8) and RenderTexture (RFloat) heightmaps
        void vert(inout appdata_full v)
        {
            // Sample heightmap at vertex UV (GPU-based, much faster than CPU sampling)
            // Heightmap can be RFloat (single channel) or R8 (grayscale)
            // For RFloat RenderTextures, .r contains the height value directly
            // For R8 Texture2D, .r also contains the height value (0-1 range)
            float height = tex2Dlod(_Heightmap, float4(v.texcoord.xy, 0, 0)).r;
            
            // Calculate displacement amount
            // Height is 0-1, scale by displacement strength and planet radius
            float displacement = height * _DisplacementStrength * _PlanetRadius * 0.1f; // 10% of radius max
            
            // Displace vertex along its normal (outward from sphere center)
            v.vertex.xyz += v.normal * displacement;
        }
        
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // Convert sphere UV to lat/lon for equirectangular sampling
            // UV is already in map format (u = horizontal, v = vertical)
            float2 uv = IN.uv_MainTex;
            
            // Sample the flat map texture
            // The texture wraps horizontally (U wraps)
            fixed4 c = tex2D(_MainTex, uv) * _Color;
            
            // Apply ownership overlay (Phase 6)
            if (_EnableOwnership > 0.5)
            {
                fixed4 ownership = tex2D(_OwnershipOverlay, uv);
                if (ownership.a > 0.01) // Has ownership overlay
                {
                    c.rgb = lerp(c.rgb, ownership.rgb, ownership.a);
                }
            }
            
            // Apply fog of war (Phase 6)
            if (_EnableFog > 0.5)
            {
                float fog = tex2D(_FogMask, uv).r;
                if (fog < 0.01)
                {
                    // Hidden: black
                    c.rgb = float3(0, 0, 0);
                }
                else if (fog < 0.75)
                {
                    // Explored: desaturated gray
                    float gray = dot(c.rgb, float3(0.299, 0.587, 0.114));
                    c.rgb = lerp(float3(0, 0, 0), gray * 0.6, fog * 2.0);
                }
                // fog >= 0.75: visible, keep original color
            }
            
            // Apply basic lighting enhancement
            float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
            float NdotL = max(0, dot(IN.worldNormal, lightDir));
            float lighting = lerp(0.5, 1.0, NdotL) * _LightIntensity;
            
            o.Albedo = c.rgb * lighting;
            o.Metallic = _Metallic;
            o.Smoothness = _Smoothness;
            o.Alpha = c.a;
        }
        ENDCG
    }
    
    FallBack "Diffuse"
}
