Shader "Custom/GlobeMap"
{
    Properties
    {
        _MainTex ("Flat Map Texture", 2D) = "white" {}
        _Heightmap ("Heightmap", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _Smoothness ("Smoothness", Range(0,1)) = 0.3
        _LightIntensity ("Light Intensity", Range(0,2)) = 1.0
        _DisplacementStrength ("Displacement Strength", Range(0,1)) = 0.1
        _PlanetRadius ("Planet Radius", Float) = 21.0
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
        fixed4 _Color;
        half _Metallic;
        half _Smoothness;
        half _LightIntensity;
        half _DisplacementStrength;
        float _PlanetRadius;
        
        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float3 worldNormal;
        };
        
        // Vertex shader: displace vertices based on heightmap
        void vert(inout appdata_full v)
        {
            // Sample heightmap at vertex UV
            float height = tex2Dlod(_Heightmap, float4(v.texcoord.xy, 0, 0)).r; // Read red channel (0-1)
            
            // Calculate displacement amount
            // Height is 0-1, scale by displacement strength and planet radius
            float displacement = height * _DisplacementStrength * _PlanetRadius * 0.1f; // 10% of radius max
            
            // Displace vertex along its normal (outward from sphere center)
            v.vertex.xyz += v.normal * displacement;
        }
        
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // Convert sphere UV to lat/lon for equirectangular sampling
            // UV is already in equirectangular format (u = longitude, v = latitude)
            float2 uv = IN.uv_MainTex;
            
            // Sample the flat map texture
            // The texture wraps horizontally (U wraps)
            fixed4 c = tex2D(_MainTex, uv) * _Color;
            
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

