Shader "Custom/FlatGlobeMorph"
{
    Properties
    {
        _MainTex ("Biome Texture", 2D) = "white" {}
        _Heightmap ("Heightmap", 2D) = "black" {}
        _FogMask ("Fog Mask", 2D) = "white" {}
        _OwnershipOverlay ("Ownership Overlay", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _Smoothness ("Smoothness", Range(0,1)) = 0.3
        _Morph ("Morph (0=Flat, 1=Globe)", Range(0,1)) = 0.0
        _FlatHeightScale ("Flat Height Scale", Float) = 0.1
        _GlobeHeightScale ("Globe Height Scale", Float) = 0.1
        _MapHeight ("Map Height", Float) = 180.0
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
        float _Morph;
        float _FlatHeightScale;
        float _GlobeHeightScale;
        float _MapHeight;
        float _PlanetRadius;
        float _EnableFog;
        float _EnableOwnership;
        
        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float3 worldNormal;
        };
        
        // Vertex shader: morphs between flat and globe positions based on _Morph parameter
        // _Morph = 0 → flat map (vertices displaced upward)
        // _Morph = 1 → globe (vertices displaced outward along normals)
        void vert(inout appdata_full v)
        {
            // Sample heightmap at vertex UV (GPU-based)
            float height = tex2Dlod(_Heightmap, float4(v.texcoord.xy, 0, 0)).r;
            height = saturate(height); // Clamp to 0-1
            
            // Calculate flat position (original position + upward displacement)
            // v.vertex is already in flat plane coordinates (XZ plane, Y is up)
            float3 flatPos = v.vertex.xyz;
            float flatDisplacement = height * _FlatHeightScale * _MapHeight;
            flatPos.y += flatDisplacement;
            
            // Calculate globe position (sphere position + outward displacement along normal)
            // Convert UV to spherical coordinates (equirectangular mapping)
            float2 uv = v.texcoord.xy;
            // U: 0-1 maps to longitude -PI to PI
            float lon = (uv.x - 0.5f) * 2.0f * 3.14159265f;
            // V: 0-1 maps to latitude PI/2 to -PI/2 (inverted for standard UV)
            float lat = (0.5f - uv.y) * 3.14159265f;
            
            float cosLat = cos(lat);
            float sinLat = sin(lat);
            float cosLon = cos(lon);
            float sinLon = sin(lon);
            
            // Base sphere position (unit sphere, scaled by planet radius)
            // Standard spherical coordinates: (x, y, z) = (cos(lat)*cos(lon), sin(lat), cos(lat)*sin(lon))
            float3 spherePos = float3(
                cosLat * cosLon,
                sinLat,
                cosLat * sinLon
            ) * _PlanetRadius;
            
            // Calculate normal (outward from sphere center)
            float3 sphereNormal = normalize(spherePos);
            
            // Apply height displacement outward along normal
            float globeDisplacement = height * _GlobeHeightScale * _PlanetRadius * 0.1f; // 10% of radius max
            float3 globePos = spherePos + sphereNormal * globeDisplacement;
            
            // Morph between flat and globe positions
            // _Morph = 0 → flatPos, _Morph = 1 → globePos
            v.vertex.xyz = lerp(flatPos, globePos, _Morph);
            
            // Morph normals as well for proper lighting
            float3 flatNormal = float3(0, 1, 0); // Up for flat map
            float3 finalNormal = lerp(flatNormal, sphereNormal, _Morph);
            v.normal = normalize(finalNormal);
        }
        
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // Sample biome texture with horizontal wrapping
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            
            // Apply ownership overlay (Phase 6)
            if (_EnableOwnership > 0.5)
            {
                fixed4 ownership = tex2D(_OwnershipOverlay, IN.uv_MainTex);
                if (ownership.a > 0.01) // Has ownership overlay
                {
                    c.rgb = lerp(c.rgb, ownership.rgb, ownership.a);
                }
            }
            
            // Apply fog of war (Phase 6)
            if (_EnableFog > 0.5)
            {
                float fog = tex2D(_FogMask, IN.uv_MainTex).r;
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
            
            o.Albedo = c.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Smoothness;
            o.Alpha = c.a;
        }
        ENDCG
    }
    
    FallBack "Diffuse"
}

