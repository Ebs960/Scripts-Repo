Shader "Custom/FlatMapDisplacement"
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
        _FlatHeightScale ("Flat Height Scale", Float) = 0.1
        _MapHeight ("Map Height", Float) = 180.0
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
        float _FlatHeightScale;
        float _MapHeight;
        float _EnableFog;
        float _EnableOwnership;
        
        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float3 worldNormal;
        };
        
        // Vertex shader: displace vertices upward based on heightmap
        // This replaces CPU GetPixel() sampling with GPU texture sampling
        void vert(inout appdata_full v)
        {
            // Sample heightmap at vertex UV (GPU-based, much faster than CPU GetPixel)
            // Heightmap can be RFloat (single channel) or R8 (grayscale)
            float height = tex2Dlod(_Heightmap, float4(v.texcoord.xy, 0, 0)).r;
            
            // Calculate displacement amount
            // Height is 0-1, scale by displacement strength and map height
            float displacement = height * _FlatHeightScale * _MapHeight;
            
            // Displace vertex upward (Y axis for flat map)
            v.vertex.y += displacement;
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

