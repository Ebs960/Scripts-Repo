Shader "Custom/FlatMapDisplacement_URP"
{
    Properties
    {
        _MainTex ("Biome Texture", 2D) = "gray" {}
        _Heightmap ("Heightmap", 2D) = "gray" {}
        _FogMask ("Fog Mask", 2D) = "white" {}
        _OwnershipOverlay ("Ownership Overlay", 2D) = "black" {}
        _LUT ("Tile Index LUT", 2D) = "black" {}
        _Color ("Tint", Color) = (0.5,0.5,0.5,1)
        _FlatHeightScale ("Flat Height Scale", Float) = 0.1
        _MapHeight ("Map Height", Float) = 180.0
        [Toggle] _EnableFog ("Enable Fog", Float) = 1.0
        [Toggle] _EnableOwnership ("Enable Ownership", Float) = 1.0
        
        [Header(Hex Grid Outline)]
        [Toggle] _EnableHexGrid ("Enable Hex Grid", Float) = 0.0
        _HexGridColor ("Grid Line Color", Color) = (0.1, 0.1, 0.1, 0.5)
        _HexGridWidth ("Grid Line Width", Range(0.01, 0.1)) = 0.03
        _HexScale ("Hex Scale (tiles across)", Float) = 80.0
        
        [Header(Tile Highlight)]
        [Toggle] _EnableTileHighlight ("Enable Tile Highlight", Float) = 0.0
        _HighlightTileIndex ("Highlight Tile Index", Int) = -1
        _HighlightColor ("Highlight Color", Color) = (1, 1, 1, 0.3)
        _HighlightWidth ("Highlight Edge Width", Range(0.001, 0.02)) = 0.005
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _FlatHeightScale;
                float _MapHeight;
                float _EnableFog;
                float _EnableOwnership;
                float _EnableHexGrid;
                float4 _HexGridColor;
                float _HexGridWidth;
                float _HexScale;
                float _EnableTileHighlight;
                int _HighlightTileIndex;
                float4 _HighlightColor;
                float _HighlightWidth;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_Heightmap);
            SAMPLER(sampler_Heightmap);
            TEXTURE2D(_FogMask);
            SAMPLER(sampler_FogMask);
            TEXTURE2D(_OwnershipOverlay);
            SAMPLER(sampler_OwnershipOverlay);
            TEXTURE2D(_LUT);
            SAMPLER(sampler_LUT);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };
            
            // Hex grid helper functions
            // Pointy-top hexagon distance function
            float HexDist(float2 p)
            {
                p = abs(p);
                float c = dot(p, normalize(float2(1.0, 1.73205))); // sqrt(3) ≈ 1.73205
                return max(c, p.x);
            }
            
            // Get hex cell coordinates and distance to edge
            float4 HexCoords(float2 uv)
            {
                const float2 r = float2(1.0, 1.73205); // sqrt(3)
                const float2 h = r * 0.5;
                
                float2 a = fmod(uv, r) - h;
                float2 b = fmod(uv - h, r) - h;
                
                float2 gv = dot(a, a) < dot(b, b) ? a : b;
                
                float2 id = uv - gv; // Hex center
                float edgeDist = 0.5 - HexDist(gv); // Distance to edge (0 at edge, 0.5 at center)
                
                return float4(gv.x, gv.y, id.x, id.y);
            }

            Varyings vert(Attributes input)
            {
                Varyings o;
                // Sample height and displace in object space
                float height = SAMPLE_TEXTURE2D_LOD(_Heightmap, sampler_Heightmap, input.uv, 0).r;
                // Displace using a world-unit amplitude; do not scale by map height
                float disp = height * _FlatHeightScale;
                float3 posOS = input.positionOS.xyz;
                posOS.y += disp;

                o.positionCS = TransformObjectToHClip(posOS);
                o.uv = input.uv;
                o.positionWS = TransformObjectToWorld(posOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                
                // Detect magenta (missing texture) robustly
                half magentaR = step(0.95h, c.r);
                half magentaB = step(0.95h, c.b);
                half magentaG = step(0.05h, 1.0h - c.g);
                half missing = magentaR * magentaB * magentaG;
                c = lerp(c, half4(0.0h, 1.0h, 1.0h, 1.0h), step(0.5h, missing));
                c *= _Color;

                // Ownership overlay
                if (_EnableOwnership > 0.5)
                {
                    half4 ownership = SAMPLE_TEXTURE2D(_OwnershipOverlay, sampler_OwnershipOverlay, i.uv);
                    c.rgb = lerp(c.rgb, ownership.rgb, ownership.a);
                }

                // Fog of war
                if (_EnableFog > 0.5)
                {
                    half fog = SAMPLE_TEXTURE2D(_FogMask, sampler_FogMask, i.uv).r;
                    if (fog < 0.01h)
                    {
                        c.rgb = half3(0,0,0);
                    }
                    else if (fog < 0.75h)
                    {
                        half gray = dot(c.rgb, half3(0.299h, 0.587h, 0.114h));
                        c.rgb = lerp(half3(0,0,0), gray * 0.6h, saturate(fog * 2.0h));
                    }
                }
                
                // Hex grid overlay
                if (_EnableHexGrid > 0.5)
                {
                    // Scale UV to hex grid
                    float2 hexUV = i.uv * float2(_HexScale, _HexScale * 0.866); // 0.866 = sqrt(3)/2 for aspect ratio
                    float4 hex = HexCoords(hexUV);
                    
                    // Distance to hex edge (smaller = closer to edge)
                    float edgeDist = 0.5 - HexDist(hex.xy);
                    
                    // Create grid line based on distance to edge
                    float gridLineWidth = _HexGridWidth;
                    float gridLine = 1.0 - smoothstep(0.0, gridLineWidth, edgeDist);
                    
                    // Blend grid line color
                    c.rgb = lerp(c.rgb, _HexGridColor.rgb, gridLine * _HexGridColor.a);
                }
                
                // Tile highlight (LUT-based)
                if (_EnableTileHighlight > 0.5 && _HighlightTileIndex >= 0)
                {
                    // Sample LUT to get tile index at this pixel
                    float4 lutSample = SAMPLE_TEXTURE2D(_LUT, sampler_LUT, i.uv);
                    // LUT stores tile index encoded in RGB (R + G*256 + B*65536)
                    int tileIndex = (int)(lutSample.r * 255.0) + 
                                   (int)(lutSample.g * 255.0) * 256 + 
                                   (int)(lutSample.b * 255.0) * 65536;
                    
                    // Check if this pixel belongs to highlighted tile
                    if (tileIndex == _HighlightTileIndex)
                    {
                        // Sample neighbors to detect edge
                        float2 texelSize = float2(_HighlightWidth, _HighlightWidth);
                        float4 lutN = SAMPLE_TEXTURE2D(_LUT, sampler_LUT, i.uv + float2(0, texelSize.y));
                        float4 lutS = SAMPLE_TEXTURE2D(_LUT, sampler_LUT, i.uv - float2(0, texelSize.y));
                        float4 lutE = SAMPLE_TEXTURE2D(_LUT, sampler_LUT, i.uv + float2(texelSize.x, 0));
                        float4 lutW = SAMPLE_TEXTURE2D(_LUT, sampler_LUT, i.uv - float2(texelSize.x, 0));
                        
                        int idxN = (int)(lutN.r * 255.0) + (int)(lutN.g * 255.0) * 256 + (int)(lutN.b * 255.0) * 65536;
                        int idxS = (int)(lutS.r * 255.0) + (int)(lutS.g * 255.0) * 256 + (int)(lutS.b * 255.0) * 65536;
                        int idxE = (int)(lutE.r * 255.0) + (int)(lutE.g * 255.0) * 256 + (int)(lutE.b * 255.0) * 65536;
                        int idxW = (int)(lutW.r * 255.0) + (int)(lutW.g * 255.0) * 256 + (int)(lutW.b * 255.0) * 65536;
                        
                        // Is this pixel on the edge? (any neighbor is different tile)
                        bool isEdge = (idxN != _HighlightTileIndex) || 
                                      (idxS != _HighlightTileIndex) || 
                                      (idxE != _HighlightTileIndex) || 
                                      (idxW != _HighlightTileIndex);
                        
                        if (isEdge)
                        {
                            // Strong highlight on edge (outline)
                            c.rgb = lerp(c.rgb, _HighlightColor.rgb, _HighlightColor.a * 2.0);
                        }
                        else
                        {
                            // Subtle fill highlight
                            c.rgb = lerp(c.rgb, _HighlightColor.rgb, _HighlightColor.a * 0.5);
                        }
                    }
                }

                return c;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
