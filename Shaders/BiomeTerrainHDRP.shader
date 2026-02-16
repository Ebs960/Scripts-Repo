Shader "Custom/BiomeTerrainHDRP"
{
    Properties
    {
        [Header(Biome Texture Arrays)]
        _BiomeAlbedoArray ("Biome Albedo Array", 2DArray) = "" {}
        _BiomeNormalArray ("Biome Normal Array", 2DArray) = "" {}
        _BiomeMaskArray ("Biome Mask Array", 2DArray) = "" {}
        _SurfaceEmissiveArray ("Surface Emissive Array", 2DArray) = "" {}

        [Header(Index and Height Maps)]
        _BiomeIndexMap ("Biome Index Map (RFloat slice index)", 2D) = "black" {}
        _Heightmap ("Heightmap (RHalf elevation)", 2D) = "black" {}
        _LUT ("Tile Index LUT", 2D) = "black" {}

        [Header(Biome Data Textures)]
        _BiomeSurfaceMapTex ("Biome Surface Map (start,count,surface,forced)", 2D) = "black" {}
        _BiomeEmissiveMapTex ("Biome Emissive Map (RGB=tint, A=intensity)", 2D) = "black" {}

        [Header(Displacement)]
        _ElevationScale ("Elevation Scale", Range(0.1, 20)) = 1.0
        _NormalStrength ("Normal Strength", Range(0.01, 5)) = 1.0
        _NormalSampleRadius ("Normal Sample Radius (texels)", Range(1, 12)) = 4

        [Header(Triplanar)]
        _TriTiling ("Triplanar Tiling", Range(0.01, 5)) = 1.15
        _TriBlend ("Triplanar Blend Sharpness", Range(1, 20)) = 5.0

        [Header(Map Dimensions)]
        _MapWidth ("Map Width", Float) = 100
        _MapHeight ("Map Height", Float) = 100
        _BiomeCount ("Biome Count", Float) = 32

        [Header(Global Modifiers)]
        _GlobalSnowAmount ("Global Snow Amount", Range(0, 1)) = 0
        _GlobalWetness ("Global Wetness", Range(0, 1)) = 0
        _SnowColor ("Snow Color", Color) = (0.92, 0.93, 0.96, 1)
        _SnowSmoothness ("Snow Smoothness", Range(0, 1)) = 0.3

        [Header(Overlays)]
        _FogMask ("Fog Mask", 2D) = "black" {}
        _EnableFog ("Enable Fog Overlay", Float) = 0
        _TerrainFogColor ("Fog Color", Color) = (0.15, 0.15, 0.2, 0.85)
        _OwnershipOverlay ("Ownership Overlay", 2D) = "black" {}
        _EnableOwnership ("Enable Ownership Overlay", Float) = 0
        _OwnershipAlpha ("Ownership Overlay Alpha", Range(0, 1)) = 0.3

        [Header(Per Biome Lookup)]
        _SliceToBiomeMap ("Slice To Biome Map", 2D) = "black" {}

        [Header(Lighting Fallback)]
        _SunDir ("Sun Direction (normalized)", Vector) = (0.3, -0.8, 0.5, 0)
        _SunColor ("Sun Color", Color) = (1, 0.95, 0.85, 1)
        _SunIntensity ("Sun Intensity", Range(0, 5)) = 1.5
        _AmbientSkyColor ("Ambient Sky Color", Color) = (0.5, 0.6, 0.75, 1)
        _AmbientGroundColor ("Ambient Ground Color", Color) = (0.15, 0.12, 0.1, 1)
        _AmbientIntensity ("Ambient Intensity", Range(0, 3)) = 0.4

        [Header(Tile Highlight)]
        _HighlightTileIndex ("Highlight Tile Index", Float) = -1
        _HighlightColor ("Highlight Color", Color) = (1, 1, 0, 1)

        // ============== NEW PROPERTIES ==============

        [Header(Micro Detail)]
        _DetailAlbedoMap ("Detail Albedo Map", 2D) = "gray" {}
        _DetailNormalMap ("Detail Normal Map", 2D) = "bump" {}
        _DetailTiling ("Detail Tiling", Range(1, 100)) = 20.0
        _DetailStrength ("Detail Albedo Strength", Range(0, 1)) = 0.3
        _DetailNormalStrength ("Detail Normal Strength", Range(0, 2)) = 0.5
        _DetailFadeStart ("Detail Fade Start Distance", Range(0, 200)) = 5.0
        _DetailFadeEnd ("Detail Fade End Distance", Range(0, 500)) = 50.0

        [Header(Anti Tiling)]
        _AntiTileStrength ("Anti-Tile Strength", Range(0, 1)) = 1.0

        [Header(Biome Blending)]
        _BiomeBlendRadius ("Biome Blend Radius (texels)", Range(0, 16)) = 4.0
        _BiomeBlendSharpness ("Height Blend Sharpness", Range(0.01, 10)) = 3.0

        [Header(Snow Detail)]
        _SnowNormalStrength ("Snow Normal Strength", Range(0, 2)) = 0.5
        _SnowNormalTiling ("Snow Normal Tiling", Range(0.1, 20)) = 5.0
        _SnowSparkleStrength ("Snow Sparkle Strength", Range(0, 1)) = 0.3

        [Header(Distance LOD)]
        _TriplanarLODStart ("Triplanar LOD Start Distance", Range(0, 200)) = 50.0
        _TriplanarLODEnd ("Triplanar LOD End Distance", Range(0, 500)) = 150.0

        [Header(Tessellation)]
        [Toggle(_TESSELLATION_ON)] _EnableTessellation ("Enable Tessellation", Float) = 0
        _TessellationFactor ("Tessellation Factor", Range(1, 64)) = 8.0
        _TessellationFadeStart ("Tessellation Fade Start", Range(0, 200)) = 10.0
        _TessellationFadeEnd ("Tessellation Fade End", Range(0, 500)) = 100.0
    }

    HLSLINCLUDE

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

    // ===================== Texture & Sampler Declarations =====================

    TEXTURE2D_ARRAY(_BiomeAlbedoArray);    SAMPLER(sampler_BiomeAlbedoArray);
    TEXTURE2D_ARRAY(_BiomeNormalArray);    SAMPLER(sampler_BiomeNormalArray);
    TEXTURE2D_ARRAY(_BiomeMaskArray);      SAMPLER(sampler_BiomeMaskArray);
    TEXTURE2D_ARRAY(_SurfaceEmissiveArray);SAMPLER(sampler_SurfaceEmissiveArray);

    TEXTURE2D(_BiomeIndexMap);       SAMPLER(sampler_BiomeIndexMap);
    TEXTURE2D(_Heightmap);           SAMPLER(sampler_Heightmap);
    TEXTURE2D(_LUT);                 SAMPLER(sampler_LUT);
    TEXTURE2D(_BiomeSurfaceMapTex);  SAMPLER(sampler_BiomeSurfaceMapTex);
    TEXTURE2D(_BiomeEmissiveMapTex); SAMPLER(sampler_BiomeEmissiveMapTex);
    TEXTURE2D(_FogMask);             SAMPLER(sampler_FogMask);
    TEXTURE2D(_OwnershipOverlay);    SAMPLER(sampler_OwnershipOverlay);
    TEXTURE2D(_SliceToBiomeMap);     SAMPLER(sampler_SliceToBiomeMap);
    TEXTURE2D(_DetailAlbedoMap);     SAMPLER(sampler_DetailAlbedoMap);
    TEXTURE2D(_DetailNormalMap);     SAMPLER(sampler_DetailNormalMap);

    // ===================== Uniforms =====================

    float _ElevationScale;
    float _NormalStrength;
    float _NormalSampleRadius;
    float4 _Heightmap_TexelSize;
    float4 _BiomeIndexMap_TexelSize;
    float _TriTiling;
    float _TriBlend;
    float _MapWidth;
    float _MapHeight;
    float _BiomeCount;
    float _GlobalSnowAmount;
    float _GlobalWetness;
    float4 _SnowColor;
    float _SnowSmoothness;
    float _EnableFog;
    float4 _TerrainFogColor;
    float _EnableOwnership;
    float _OwnershipAlpha;
    float4 _SunDir;
    float4 _SunColor;
    float _SunIntensity;
    float4 _AmbientSkyColor;
    float4 _AmbientGroundColor;
    float _AmbientIntensity;
    float _HighlightTileIndex;
    float4 _HighlightColor;

    // New uniforms
    float _DetailTiling;
    float _DetailStrength;
    float _DetailNormalStrength;
    float _DetailFadeStart;
    float _DetailFadeEnd;
    float _AntiTileStrength;
    float _BiomeBlendRadius;
    float _BiomeBlendSharpness;
    float _SnowNormalStrength;
    float _SnowNormalTiling;
    float _SnowSparkleStrength;
    float _TriplanarLODStart;
    float _TriplanarLODEnd;
    float _TessellationFactor;
    float _TessellationFadeStart;
    float _TessellationFadeEnd;

    // Per-biome arrays (set via SetVectorArray from C#, max 64 biomes)
    float4 _BiomeTints[64];
    float4 _BiomeParams[64];

    // ===================== Hash Functions for Hex Tiling (#4) =====================

    // Hash-without-sine for cross-platform reliability
    float2 HexHash(float2 p)
    {
        float2 q = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
        return frac(sin(q) * 43758.5453);
    }

    // Compute triangle grid for hex tiling
    // Based on "Practical Real-Time Hex-Tiling" (Mikkelsen 2022)
    void HexTriangleGrid(float2 uv,
        out float w1, out float w2, out float w3,
        out float2 v1, out float2 v2, out float2 v3)
    {
        float2x2 gridToSkewed = float2x2(1.0, -0.57735027, 0.0, 1.15470054);
        float2 skewed = mul(gridToSkewed, uv * 3.46410162); // 2 * sqrt(3)

        float2 baseId = floor(skewed);
        float3 temp = float3(frac(skewed), 0.0);
        temp.z = 1.0 - temp.x - temp.y;

        float s = step(0.0, -temp.z);
        float s2 = 2.0 * s - 1.0;

        w1 = -temp.z * s2;
        w2 = s - temp.y * s2;
        w3 = s - temp.x * s2;

        v1 = baseId + float2(s, s);
        v2 = baseId + float2(s, 1.0 - s);
        v3 = baseId + float2(1.0 - s, s);
    }

    // Sample texture array with hex tiling to break visible repetition
    float4 SampleHexTiled(TEXTURE2D_ARRAY_PARAM(tex, samp), float2 uv, float sliceIndex)
    {
        float w1, w2, w3;
        float2 v1, v2, v3;
        HexTriangleGrid(uv, w1, w2, w3, v1, v2, v3);

        float2 uv1 = uv + HexHash(v1);
        float2 uv2 = uv + HexHash(v2);
        float2 uv3 = uv + HexHash(v3);

        float exponent = lerp(1.0, 7.0, _AntiTileStrength);
        w1 = pow(w1, exponent);
        w2 = pow(w2, exponent);
        w3 = pow(w3, exponent);
        float wSum = w1 + w2 + w3;

        float4 s1 = SAMPLE_TEXTURE2D_ARRAY(tex, samp, uv1, sliceIndex) * w1;
        float4 s2 = SAMPLE_TEXTURE2D_ARRAY(tex, samp, uv2, sliceIndex) * w2;
        float4 s3 = SAMPLE_TEXTURE2D_ARRAY(tex, samp, uv3, sliceIndex) * w3;

        return (s1 + s2 + s3) / wSum;
    }

    // Hex-tiled normal sampling (returns tangent-space normal)
    float3 SampleNormalHexTiled(TEXTURE2D_ARRAY_PARAM(tex, samp), float2 uv, float sliceIndex)
    {
        float w1, w2, w3;
        float2 v1, v2, v3;
        HexTriangleGrid(uv, w1, w2, w3, v1, v2, v3);

        float2 uv1 = uv + HexHash(v1);
        float2 uv2 = uv + HexHash(v2);
        float2 uv3 = uv + HexHash(v3);

        float exponent = lerp(1.0, 7.0, _AntiTileStrength);
        w1 = pow(w1, exponent);
        w2 = pow(w2, exponent);
        w3 = pow(w3, exponent);
        float wSum = w1 + w2 + w3;

        float3 n1 = UnpackNormal(SAMPLE_TEXTURE2D_ARRAY(tex, samp, uv1, sliceIndex));
        float3 n2 = UnpackNormal(SAMPLE_TEXTURE2D_ARRAY(tex, samp, uv2, sliceIndex));
        float3 n3 = UnpackNormal(SAMPLE_TEXTURE2D_ARRAY(tex, samp, uv3, sliceIndex));

        return normalize((n1 * w1 + n2 * w2 + n3 * w3) / wSum);
    }

    // ===================== Triplanar Helpers =====================

    float3 TriplanarWeights(float3 normalWS)
    {
        float3 w = abs(normalWS);
        w = pow(w, _TriBlend);
        w /= max(dot(w, float3(1, 1, 1)), 1e-5);
        return w;
    }

    // Original triplanar sample (non-hex fallback)
    float4 SampleArrayTriplanar(TEXTURE2D_ARRAY_PARAM(tex, samp),
        float3 worldPos, float3 weights, float sliceIndex, float tiling)
    {
        float4 sX = SAMPLE_TEXTURE2D_ARRAY(tex, samp, worldPos.zy * tiling, sliceIndex);
        float4 sY = SAMPLE_TEXTURE2D_ARRAY(tex, samp, worldPos.xz * tiling, sliceIndex);
        float4 sZ = SAMPLE_TEXTURE2D_ARRAY(tex, samp, worldPos.xy * tiling, sliceIndex);
        return sX * weights.x + sY * weights.y + sZ * weights.z;
    }

    // Original triplanar normal with whiteout blending (non-hex fallback)
    float3 SampleNormalTriplanar(TEXTURE2D_ARRAY_PARAM(tex, samp),
        float3 worldPos, float3 worldNormal, float3 weights, float sliceIndex, float tiling)
    {
        float3 tnX = UnpackNormal(SAMPLE_TEXTURE2D_ARRAY(tex, samp, worldPos.zy * tiling, sliceIndex));
        float3 tnY = UnpackNormal(SAMPLE_TEXTURE2D_ARRAY(tex, samp, worldPos.xz * tiling, sliceIndex));
        float3 tnZ = UnpackNormal(SAMPLE_TEXTURE2D_ARRAY(tex, samp, worldPos.xy * tiling, sliceIndex));

        float3 nX = float3(tnX.xy + worldNormal.zy, abs(worldNormal.x));
        float3 nY = float3(tnY.xy + worldNormal.xz, abs(worldNormal.y));
        float3 nZ = float3(tnZ.xy + worldNormal.xy, abs(worldNormal.z));

        return normalize(nX.zyx * weights.x + nY.xzy * weights.y + nZ.xyz * weights.z);
    }

    // ===================== Distance-Adaptive Sampling (#4 + #6) =====================
    // Blends between hex-tiled triplanar (close) and single Y-planar (far)

    float4 SampleBiomeTexture(TEXTURE2D_ARRAY_PARAM(tex, samp),
        float3 worldPos, float3 triWeights, float sliceIndex, float tiling, float camDist)
    {
        float lodBlend = saturate((camDist - _TriplanarLODStart) /
            max(_TriplanarLODEnd - _TriplanarLODStart, 0.01));
        float2 uvY = worldPos.xz * tiling;

        // Far distance: single Y-axis planar sample (1 sample)
        if (lodBlend >= 0.999)
            return SAMPLE_TEXTURE2D_ARRAY(tex, samp, uvY, sliceIndex);

        float4 fullResult;
        if (_AntiTileStrength > 0.01)
        {
            // Hex-tiled triplanar (9 samples: 3 axes x 3 hex cells)
            float4 sX = SampleHexTiled(TEXTURE2D_ARRAY_ARGS(tex, samp), worldPos.zy * tiling, sliceIndex);
            float4 sY = SampleHexTiled(TEXTURE2D_ARRAY_ARGS(tex, samp), uvY, sliceIndex);
            float4 sZ = SampleHexTiled(TEXTURE2D_ARRAY_ARGS(tex, samp), worldPos.xy * tiling, sliceIndex);
            fullResult = sX * triWeights.x + sY * triWeights.y + sZ * triWeights.z;
        }
        else
        {
            // Standard triplanar (3 samples)
            fullResult = SampleArrayTriplanar(TEXTURE2D_ARRAY_ARGS(tex, samp),
                worldPos, triWeights, sliceIndex, tiling);
        }

        // Blend toward Y-only at distance
        if (lodBlend > 0.001)
        {
            float4 yOnly = SAMPLE_TEXTURE2D_ARRAY(tex, samp, uvY, sliceIndex);
            return lerp(fullResult, yOnly, lodBlend);
        }
        return fullResult;
    }

    // Distance-adaptive normal sampling with hex tiling and whiteout blending
    float3 SampleBiomeNormal(TEXTURE2D_ARRAY_PARAM(tex, samp),
        float3 worldPos, float3 worldNormal, float3 triWeights,
        float sliceIndex, float tiling, float camDist)
    {
        float lodBlend = saturate((camDist - _TriplanarLODStart) /
            max(_TriplanarLODEnd - _TriplanarLODStart, 0.01));
        float2 uvY = worldPos.xz * tiling;

        // Far distance: single Y-axis normal
        if (lodBlend >= 0.999)
        {
            float3 tnY = UnpackNormal(SAMPLE_TEXTURE2D_ARRAY(tex, samp, uvY, sliceIndex));
            float3 nY = float3(tnY.xy + worldNormal.xz, abs(worldNormal.y));
            return normalize(nY.xzy);
        }

        float3 fullResult;
        if (_AntiTileStrength > 0.01)
        {
            // Hex-tiled triplanar normals with whiteout reorientation
            float3 tnX = SampleNormalHexTiled(TEXTURE2D_ARRAY_ARGS(tex, samp), worldPos.zy * tiling, sliceIndex);
            float3 tnY = SampleNormalHexTiled(TEXTURE2D_ARRAY_ARGS(tex, samp), uvY, sliceIndex);
            float3 tnZ = SampleNormalHexTiled(TEXTURE2D_ARRAY_ARGS(tex, samp), worldPos.xy * tiling, sliceIndex);

            float3 nX = float3(tnX.xy + worldNormal.zy, abs(worldNormal.x));
            float3 nY = float3(tnY.xy + worldNormal.xz, abs(worldNormal.y));
            float3 nZ = float3(tnZ.xy + worldNormal.xy, abs(worldNormal.z));
            fullResult = normalize(nX.zyx * triWeights.x + nY.xzy * triWeights.y + nZ.xyz * triWeights.z);
        }
        else
        {
            fullResult = SampleNormalTriplanar(TEXTURE2D_ARRAY_ARGS(tex, samp),
                worldPos, worldNormal, triWeights, sliceIndex, tiling);
        }

        if (lodBlend > 0.001)
        {
            float3 tnY = UnpackNormal(SAMPLE_TEXTURE2D_ARRAY(tex, samp, uvY, sliceIndex));
            float3 nY = float3(tnY.xy + worldNormal.xz, abs(worldNormal.y));
            float3 yResult = normalize(nY.xzy);
            return normalize(lerp(fullResult, yResult, lodBlend));
        }
        return fullResult;
    }

    // ===================== Shared Helpers =====================

    int GetBiomeIndexFromSlice(float sliceIndex, float totalSlices)
    {
        float u = (sliceIndex + 0.5) / max(totalSlices, 1.0);
        return (int)(SAMPLE_TEXTURE2D_LOD(_SliceToBiomeMap, sampler_SliceToBiomeMap, float2(u, 0.5), 0).r + 0.5);
    }

    int DecodeTileIndex(float2 uv)
    {
        float4 lutSample = SAMPLE_TEXTURE2D_LOD(_LUT, sampler_LUT, uv, 0);
        int r = (int)(lutSample.r * 255.0 + 0.5);
        int g = (int)(lutSample.g * 255.0 + 0.5);
        int b = (int)(lutSample.b * 255.0 + 0.5);
        return r + g * 256 + b * 65536;
    }

    // Compute displaced world-space normal from heightmap gradient (shared across passes)
    float3 ComputeDisplacedNormal(float2 uv)
    {
        float2 texel = _Heightmap_TexelSize.xy;
        float2 sampleOffset = texel * _NormalSampleRadius;

        float hL = SAMPLE_TEXTURE2D_LOD(_Heightmap, sampler_Heightmap, uv - float2(sampleOffset.x, 0), 0).r;
        float hR = SAMPLE_TEXTURE2D_LOD(_Heightmap, sampler_Heightmap, uv + float2(sampleOffset.x, 0), 0).r;
        float hD = SAMPLE_TEXTURE2D_LOD(_Heightmap, sampler_Heightmap, uv - float2(0, sampleOffset.y), 0).r;
        float hU = SAMPLE_TEXTURE2D_LOD(_Heightmap, sampler_Heightmap, uv + float2(0, sampleOffset.y), 0).r;

        float dhdx = (hR - hL) * _ElevationScale / (2.0 * sampleOffset.x * _MapWidth);
        float dhdz = (hU - hD) * _ElevationScale / (2.0 * sampleOffset.y * _MapHeight);

        dhdx *= _NormalStrength;
        dhdz *= _NormalStrength;
        return normalize(float3(-dhdx, 1.0, -dhdz));
    }

    // Distance-adaptive tessellation factor
    float CalcTessellationFactor(float3 positionWS)
    {
        float dist = distance(positionWS, _WorldSpaceCameraPos);
        float f = 1.0 - saturate((dist - _TessellationFadeStart) /
            max(_TessellationFadeEnd - _TessellationFadeStart, 0.01));
        return max(f * _TessellationFactor, 1.0);
    }

    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        // =============================================================
        // PASS 1: FORWARD ONLY — Main lit rendering (all improvements)
        // =============================================================
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

            // HDRP multi_compile keywords
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile_fragment _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ SHADOW_LOW SHADOW_MEDIUM SHADOW_HIGH SHADOW_VERY_HIGH
            #pragma multi_compile _ LIGHT_LAYERS

            // NOTE:
            // This shader intentionally uses its own (property-driven) lighting so it can compile
            // without relying on HDRP lightloop internals that can change between HDRP versions.

            // ===================== Structures =====================

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 tangentWS : TEXCOORD3;
                float3 bitangentWS : TEXCOORD4;
                float3 viewDirWS : TEXCOORD5;
            };

            // ===================== Tessellation (#9) =====================
            #ifdef _TESSELLATION_ON

            struct FwdHullCP
            {
                float3 positionOS : INTERNALTESSPOS;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
            };

            struct FwdTessFactors
            {
                float edge[3] : SV_TessFactor;
                float inside : SV_InsideTessFactor;
            };

            // Vertex shader passes through to hull shader (no displacement yet)
            FwdHullCP vert(Attributes input)
            {
                FwdHullCP o;
                o.positionOS = input.positionOS;
                o.uv = input.uv;
                o.normalOS = input.normalOS;
                o.tangentOS = input.tangentOS;
                return o;
            }

            FwdTessFactors FwdPatchConstant(InputPatch<FwdHullCP, 3> patch)
            {
                FwdTessFactors f;
                float3 p0 = TransformObjectToWorld(patch[0].positionOS);
                float3 p1 = TransformObjectToWorld(patch[1].positionOS);
                float3 p2 = TransformObjectToWorld(patch[2].positionOS);
                f.edge[0] = CalcTessellationFactor((p1 + p2) * 0.5);
                f.edge[1] = CalcTessellationFactor((p2 + p0) * 0.5);
                f.edge[2] = CalcTessellationFactor((p0 + p1) * 0.5);
                f.inside = (f.edge[0] + f.edge[1] + f.edge[2]) / 3.0;
                return f;
            }

            [domain("tri")]
            [partitioning("fractional_odd")]
            [outputtopology("triangle_cw")]
            [outputcontrolpoints(3)]
            [patchconstantfunc("FwdPatchConstant")]
            [maxtessfactor(64.0)]
            FwdHullCP hull(InputPatch<FwdHullCP, 3> patch, uint id : SV_OutputControlPointID)
            {
                return patch[id];
            }

            [domain("tri")]
            Varyings domain(FwdTessFactors factors, OutputPatch<FwdHullCP, 3> patch, float3 bary : SV_DomainLocation)
            {
                float3 posOS = bary.x * patch[0].positionOS + bary.y * patch[1].positionOS + bary.z * patch[2].positionOS;
                float2 uv = bary.x * patch[0].uv + bary.y * patch[1].uv + bary.z * patch[2].uv;
                float3 normalOS = normalize(bary.x * patch[0].normalOS + bary.y * patch[1].normalOS + bary.z * patch[2].normalOS);
                float4 tangentOS = bary.x * patch[0].tangentOS + bary.y * patch[1].tangentOS + bary.z * patch[2].tangentOS;
                tangentOS.xyz = normalize(tangentOS.xyz);

                // Heightmap displacement on tessellated vertex
                float elevation = SAMPLE_TEXTURE2D_LOD(_Heightmap, sampler_Heightmap, uv, 0).r;
                posOS.y += elevation * _ElevationScale;

                Varyings o;
                o.positionWS = TransformObjectToWorld(posOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(normalOS);
                o.tangentWS = TransformObjectToWorldDir(tangentOS.xyz);
                o.bitangentWS = cross(o.normalWS, o.tangentWS) * tangentOS.w;
                o.viewDirWS = GetWorldSpaceNormalizeViewDir(o.positionWS);
                o.uv = uv;
                return o;
            }

            #else // !_TESSELLATION_ON

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 posOS = input.positionOS;
                float elevation = SAMPLE_TEXTURE2D_LOD(_Heightmap, sampler_Heightmap, input.uv, 0).r;
                posOS.y += elevation * _ElevationScale;

                o.positionWS = TransformObjectToWorld(posOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.tangentWS = TransformObjectToWorldDir(input.tangentOS.xyz);
                o.bitangentWS = cross(o.normalWS, o.tangentWS) * input.tangentOS.w;
                o.viewDirWS = GetWorldSpaceNormalizeViewDir(o.positionWS);
                o.uv = input.uv;
                return o;
            }

            #endif // _TESSELLATION_ON

            // ===================== Biome Sampling Helper =====================

            struct BiomeSample
            {
                float3 albedo;
                float3 normalWS;
                float4 mask; // R=metallic, G=AO, B=height, A=smoothness
                float3 emission;
                float4 biomeParams; // x=tiling, y=snowRetention, z=wetnessResponse, w=isWaterBiome
            };

            BiomeSample SampleFullBiome(float sliceIndex, float3 worldPos,
                float3 displacedNormal, float3 triWeights, float camDist)
            {
                BiomeSample s;
                int biomeIdx = GetBiomeIndexFromSlice(sliceIndex, max(_BiomeCount, 1.0));
                biomeIdx = clamp(biomeIdx, 0, 63);

                float4 biomeTint = _BiomeTints[biomeIdx];
                s.biomeParams = _BiomeParams[biomeIdx];
                float biomeTiling = max(s.biomeParams.x, 0.01);
                float effectiveTiling = _TriTiling * biomeTiling;

                // Distance-adaptive hex-tiled triplanar sampling (#4, #6)
                float4 albedoRaw = SampleBiomeTexture(
                    TEXTURE2D_ARRAY_ARGS(_BiomeAlbedoArray, sampler_BiomeAlbedoArray),
                    worldPos, triWeights, sliceIndex, effectiveTiling, camDist);
                s.normalWS = SampleBiomeNormal(
                    TEXTURE2D_ARRAY_ARGS(_BiomeNormalArray, sampler_BiomeNormalArray),
                    worldPos, displacedNormal, triWeights, sliceIndex, effectiveTiling, camDist);
                s.mask = SampleBiomeTexture(
                    TEXTURE2D_ARRAY_ARGS(_BiomeMaskArray, sampler_BiomeMaskArray),
                    worldPos, triWeights, sliceIndex, effectiveTiling, camDist);

                s.albedo = albedoRaw.rgb * biomeTint.rgb;

                // Emissive
                float4 emissiveParams = SAMPLE_TEXTURE2D_LOD(_BiomeEmissiveMapTex, sampler_BiomeEmissiveMapTex,
                    float2((sliceIndex + 0.5) / max(_BiomeCount, 1.0), 0.5), 0);
                float4 emissiveTex = SampleBiomeTexture(
                    TEXTURE2D_ARRAY_ARGS(_SurfaceEmissiveArray, sampler_SurfaceEmissiveArray),
                    worldPos, triWeights, sliceIndex, effectiveTiling, camDist);
                s.emission = emissiveTex.rgb * emissiveParams.rgb * emissiveParams.a;

                return s;
            }

            // ===================== Fragment Shader =====================

            float4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float3 worldPos = input.positionWS;
                float3 V = normalize(input.viewDirWS);
                float camDist = distance(worldPos, _WorldSpaceCameraPos);

                // --- Displaced normal from heightmap ---
                float3 displacedNormal = ComputeDisplacedNormal(uv);
                float3 triWeights = TriplanarWeights(displacedNormal);

                // ==========================================================
                // BIOME INDEX & TRANSITION BLENDING (#3, #7)
                // ==========================================================
                float centerSlice = round(SAMPLE_TEXTURE2D_LOD(_BiomeIndexMap, sampler_BiomeIndexMap, uv, 0).r);

                // Sample neighbors to detect biome boundaries
                float2 biomeStep = _BiomeIndexMap_TexelSize.xy * max(_BiomeBlendRadius, 0.5);
                float sliceR = round(SAMPLE_TEXTURE2D_LOD(_BiomeIndexMap, sampler_BiomeIndexMap,
                    uv + float2(biomeStep.x, 0), 0).r);
                float sliceL = round(SAMPLE_TEXTURE2D_LOD(_BiomeIndexMap, sampler_BiomeIndexMap,
                    uv - float2(biomeStep.x, 0), 0).r);
                float sliceU = round(SAMPLE_TEXTURE2D_LOD(_BiomeIndexMap, sampler_BiomeIndexMap,
                    uv + float2(0, biomeStep.y), 0).r);
                float sliceD = round(SAMPLE_TEXTURE2D_LOD(_BiomeIndexMap, sampler_BiomeIndexMap,
                    uv - float2(0, biomeStep.y), 0).r);

                // Find secondary biome for blending
                float secondarySlice = centerSlice;
                int diffCount = 0;
                if (sliceR != centerSlice) { secondarySlice = sliceR; diffCount++; }
                if (sliceL != centerSlice) { if (diffCount == 0) secondarySlice = sliceL; diffCount++; }
                if (sliceU != centerSlice) { if (diffCount == 0) secondarySlice = sliceU; diffCount++; }
                if (sliceD != centerSlice) { if (diffCount == 0) secondarySlice = sliceD; diffCount++; }

                // Sample primary biome
                BiomeSample primary = SampleFullBiome(centerSlice, worldPos, displacedNormal, triWeights, camDist);

                float3 albedo;
                float3 normalWS;
                float4 mask;
                float3 emission;
                float4 biomeParams;

                // Height-based biome blending at boundaries (#3, #7: mask.b = height)
                if (diffCount > 0 && secondarySlice != centerSlice && _BiomeBlendRadius > 0.01)
                {
                    BiomeSample secondary = SampleFullBiome(secondarySlice, worldPos, displacedNormal, triWeights, camDist);

                    // Use mask.b (height channel, #7) for height-based blend
                    float hPrimary = primary.mask.b;
                    float hSecondary = secondary.mask.b;

                    // Spatial blend from neighbor count + height-weighted modulation
                    float spatialBlend = (float)diffCount / 4.0;
                    float heightDiff = (hSecondary - hPrimary) * _BiomeBlendSharpness;
                    float blend = saturate(spatialBlend * 0.5 + heightDiff * 0.25 + 0.25 * spatialBlend);

                    albedo = lerp(primary.albedo, secondary.albedo, blend);
                    normalWS = normalize(lerp(primary.normalWS, secondary.normalWS, blend));
                    mask = lerp(primary.mask, secondary.mask, blend);
                    emission = lerp(primary.emission, secondary.emission, blend);
                    biomeParams = lerp(primary.biomeParams, secondary.biomeParams, blend);
                }
                else
                {
                    albedo = primary.albedo;
                    normalWS = primary.normalWS;
                    mask = primary.mask;
                    emission = primary.emission;
                    biomeParams = primary.biomeParams;
                }

                // Unpack mask: R=Metallic, G=AO, B=Height (used above), A=Smoothness
                float metallic = mask.r;
                float ao = mask.g;
                float smoothness = mask.a;

                // ==========================================================
                // MICRO-DETAIL LAYER (#5)
                // Distance-faded detail texture for close-range visual crunch
                // ==========================================================
                float detailFade = 1.0 - saturate((camDist - _DetailFadeStart) /
                    max(_DetailFadeEnd - _DetailFadeStart, 0.01));
                if (detailFade > 0.01)
                {
                    float2 detailUV = worldPos.xz * _DetailTiling;
                    float4 detailAlbedo = SAMPLE_TEXTURE2D(_DetailAlbedoMap, sampler_DetailAlbedoMap, detailUV);
                    float3 detailNorm = UnpackNormal(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, detailUV));

                    // Modulate albedo (detail map centered around 0.5 gray = no change)
                    float detailMod = lerp(1.0, detailAlbedo.r * 2.0, _DetailStrength * detailFade);
                    albedo *= detailMod;

                    // Blend detail normal into surface normal
                    float dnStr = _DetailNormalStrength * detailFade;
                    normalWS = normalize(float3(
                        normalWS.x + detailNorm.x * dnStr,
                        normalWS.y,
                        normalWS.z + detailNorm.y * dnStr
                    ));
                }

                // ==========================================================
                // SNOW OVERLAY WITH NORMAL PERTURBATION (#8)
                // ==========================================================
                float biomeSnowRetention = biomeParams.y;
                float biomeWetnessResponse = biomeParams.z;
                float isWaterBiome = biomeParams.w;

                float snowRetention = lerp(0.3, 1.0, biomeSnowRetention);
                float snowMask = saturate(displacedNormal.y) * _GlobalSnowAmount * snowRetention;
                snowMask *= smoothstep(0.4, 0.7, displacedNormal.y);
                snowMask *= (1.0 - isWaterBiome);

                if (snowMask > 0.01)
                {
                    albedo = lerp(albedo, _SnowColor.rgb, snowMask);
                    smoothness = lerp(smoothness, _SnowSmoothness, snowMask);

                    // Procedural snow normal perturbation (soft bumps)
                    float2 snowUV = worldPos.xz * _SnowNormalTiling;
                    float snowNx = sin(snowUV.x * 6.2831 + snowUV.y * 3.14) * 0.5
                                 + sin(snowUV.x * 12.566 - snowUV.y * 7.85) * 0.25;
                    float snowNy = cos(snowUV.y * 6.2831 - snowUV.x * 2.71) * 0.5
                                 + cos(snowUV.y * 15.707 + snowUV.x * 4.33) * 0.25;
                    float3 snowPerturb = normalize(float3(
                        snowNx * _SnowNormalStrength * snowMask,
                        1.0,
                        snowNy * _SnowNormalStrength * snowMask));
                    normalWS = normalize(lerp(normalWS, snowPerturb, snowMask * 0.5));

                    // View-dependent snow sparkle (glinting highlights)
                    float sparkleHash = frac(sin(dot(floor(worldPos.xz * 50.0 + V.xz * 10.0),
                        float2(12.9898, 78.233))) * 43758.5453);
                    float sparkle = pow(saturate(dot(reflect(-V, normalWS), float3(0, 1, 0))), 32.0)
                                  * sparkleHash;
                    emission += sparkle * _SnowSparkleStrength * snowMask;
                }

                // ==========================================================
                // WETNESS
                // ==========================================================
                float wetFactor = _GlobalWetness * lerp(0.2, 1.0, biomeWetnessResponse);
                if (wetFactor > 0.01)
                {
                    albedo *= lerp(1.0, 0.6, wetFactor);
                    smoothness = lerp(smoothness, min(smoothness + 0.3, 0.95), wetFactor);
                }

                // ==========================================================
                // FOG OVERLAY
                // ==========================================================
                if (_EnableFog > 0.5)
                {
                    float4 fogSample = SAMPLE_TEXTURE2D(_FogMask, sampler_FogMask, uv);
                    float fogAmount = fogSample.r * _TerrainFogColor.a;
                    albedo = lerp(albedo, _TerrainFogColor.rgb, fogAmount);
                    emission *= (1.0 - fogAmount);
                }

                // ==========================================================
                // OWNERSHIP OVERLAY
                // ==========================================================
                if (_EnableOwnership > 0.5)
                {
                    float4 ownerColor = SAMPLE_TEXTURE2D(_OwnershipOverlay, sampler_OwnershipOverlay, uv);
                    float ownerMask = ownerColor.a * _OwnershipAlpha;
                    albedo = lerp(albedo, ownerColor.rgb, ownerMask);
                }

                // ==========================================================
                // TILE HIGHLIGHT
                // ==========================================================
                if (_HighlightTileIndex >= 0)
                {
                    int currentTile = DecodeTileIndex(uv);
                    if (currentTile == (int)_HighlightTileIndex)
                    {
                        // Make highlight visible even in shadow: tint albedo AND add a small emissive boost.
                        albedo = lerp(albedo, _HighlightColor.rgb, _HighlightColor.a);
                        emission += _HighlightColor.rgb * (_HighlightColor.a * 0.35);
                    }
                }

                // ==========================================================
                // PBR LIGHTING (property-driven, no HDRP lightloop internals)
                // ==========================================================

                float3 N = normalize(normalWS);

                // Sun direction from material property (negate so it points toward the surface)
                float3 lightDir = -normalize(_SunDir.xyz);
                float3 lightColor = _SunColor.rgb * _SunIntensity;

                float NdotL = saturate(dot(N, lightDir));
                float3 H = normalize(lightDir + V);
                float NdotH = saturate(dot(N, H));
                float NdotV = saturate(dot(N, V));

                // Diffuse (Lambert)
                float3 diffuse = albedo * (1.0 - metallic) * NdotL * lightColor;

                // Specular (GGX approximation)
                float roughness = max(1.0 - smoothness, 0.04);
                float alpha_r = roughness * roughness;
                float alpha2 = alpha_r * alpha_r;
                float denom = NdotH * NdotH * (alpha2 - 1.0) + 1.0;
                float D = alpha2 / (PI * denom * denom);

                float k = (roughness + 1.0) * (roughness + 1.0) / 8.0;
                float G_V = NdotV / (NdotV * (1.0 - k) + k);
                float G_L = NdotL / (NdotL * (1.0 - k) + k);
                float G = G_V * G_L;

                float3 F0 = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);
                float3 F = F0 + (1.0 - F0) * pow(1.0 - saturate(dot(H, V)), 5.0);

                float3 specular = D * G * F / max(4.0 * NdotV * NdotL, 0.001) * lightColor * NdotL;

                // Ambient (hemisphere approximation using material properties)
                float hemiFactor = N.y * 0.5 + 0.5;
                float3 ambientColor = lerp(_AmbientGroundColor.rgb, _AmbientSkyColor.rgb, hemiFactor);
                float3 ambient = ambientColor * _AmbientIntensity * albedo * (1.0 - metallic);

                // Combine with AO
                float3 finalColor = (diffuse + specular + ambient) * ao + emission;

                return float4(finalColor, 1.0);
            }
            ENDHLSL
        }

        // =============================================================
        // PASS 2: SHADOW CASTER
        // =============================================================
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

            #pragma vertex vert
            #pragma fragment frag

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            #ifdef _TESSELLATION_ON

            struct HullCP
            {
                float3 positionOS : INTERNALTESSPOS;
                float2 uv : TEXCOORD0;
            };

            struct TessFactors
            {
                float edge[3] : SV_TessFactor;
                float inside : SV_InsideTessFactor;
            };

            HullCP vert(Attributes input)
            {
                HullCP o;
                o.positionOS = input.positionOS;
                o.uv = input.uv;
                return o;
            }

            TessFactors PatchConst(InputPatch<HullCP, 3> patch)
            {
                TessFactors f;
                float3 p0 = TransformObjectToWorld(patch[0].positionOS);
                float3 p1 = TransformObjectToWorld(patch[1].positionOS);
                float3 p2 = TransformObjectToWorld(patch[2].positionOS);
                f.edge[0] = CalcTessellationFactor((p1 + p2) * 0.5);
                f.edge[1] = CalcTessellationFactor((p2 + p0) * 0.5);
                f.edge[2] = CalcTessellationFactor((p0 + p1) * 0.5);
                f.inside = (f.edge[0] + f.edge[1] + f.edge[2]) / 3.0;
                return f;
            }

            [domain("tri")]
            [partitioning("fractional_odd")]
            [outputtopology("triangle_cw")]
            [outputcontrolpoints(3)]
            [patchconstantfunc("PatchConst")]
            [maxtessfactor(64.0)]
            HullCP hull(InputPatch<HullCP, 3> patch, uint id : SV_OutputControlPointID)
            {
                return patch[id];
            }

            [domain("tri")]
            Varyings domain(TessFactors factors, OutputPatch<HullCP, 3> patch, float3 bary : SV_DomainLocation)
            {
                float3 posOS = bary.x * patch[0].positionOS + bary.y * patch[1].positionOS + bary.z * patch[2].positionOS;
                float2 uv = bary.x * patch[0].uv + bary.y * patch[1].uv + bary.z * patch[2].uv;
                float elevation = SAMPLE_TEXTURE2D_LOD(_Heightmap, sampler_Heightmap, uv, 0).r;
                posOS.y += elevation * _ElevationScale;

                Varyings o;
                o.positionCS = TransformWorldToHClip(TransformObjectToWorld(posOS));
                return o;
            }

            #else

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 posOS = input.positionOS;
                float elevation = SAMPLE_TEXTURE2D_LOD(_Heightmap, sampler_Heightmap, input.uv, 0).r;
                posOS.y += elevation * _ElevationScale;
                o.positionCS = TransformWorldToHClip(TransformObjectToWorld(posOS));
                return o;
            }

            #endif

            float4 frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // =============================================================
        // PASS 3: DEPTH ONLY
        // =============================================================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5

            #pragma vertex vert
            #pragma fragment frag

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            #ifdef _TESSELLATION_ON

            struct HullCP
            {
                float3 positionOS : INTERNALTESSPOS;
                float2 uv : TEXCOORD0;
            };

            struct TessFactors
            {
                float edge[3] : SV_TessFactor;
                float inside : SV_InsideTessFactor;
            };

            HullCP vert(Attributes input)
            {
                HullCP o;
                o.positionOS = input.positionOS;
                o.uv = input.uv;
                return o;
            }

            TessFactors PatchConst(InputPatch<HullCP, 3> patch)
            {
                TessFactors f;
                float3 p0 = TransformObjectToWorld(patch[0].positionOS);
                float3 p1 = TransformObjectToWorld(patch[1].positionOS);
                float3 p2 = TransformObjectToWorld(patch[2].positionOS);
                f.edge[0] = CalcTessellationFactor((p1 + p2) * 0.5);
                f.edge[1] = CalcTessellationFactor((p2 + p0) * 0.5);
                f.edge[2] = CalcTessellationFactor((p0 + p1) * 0.5);
                f.inside = (f.edge[0] + f.edge[1] + f.edge[2]) / 3.0;
                return f;
            }

            [domain("tri")]
            [partitioning("fractional_odd")]
            [outputtopology("triangle_cw")]
            [outputcontrolpoints(3)]
            [patchconstantfunc("PatchConst")]
            [maxtessfactor(64.0)]
            HullCP hull(InputPatch<HullCP, 3> patch, uint id : SV_OutputControlPointID)
            {
                return patch[id];
            }

            [domain("tri")]
            Varyings domain(TessFactors factors, OutputPatch<HullCP, 3> patch, float3 bary : SV_DomainLocation)
            {
                float3 posOS = bary.x * patch[0].positionOS + bary.y * patch[1].positionOS + bary.z * patch[2].positionOS;
                float2 uv = bary.x * patch[0].uv + bary.y * patch[1].uv + bary.z * patch[2].uv;
                float elevation = SAMPLE_TEXTURE2D_LOD(_Heightmap, sampler_Heightmap, uv, 0).r;
                posOS.y += elevation * _ElevationScale;

                Varyings o;
                o.positionCS = TransformWorldToHClip(TransformObjectToWorld(posOS));
                return o;
            }

            #else

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 posOS = input.positionOS;
                float elevation = SAMPLE_TEXTURE2D_LOD(_Heightmap, sampler_Heightmap, input.uv, 0).r;
                posOS.y += elevation * _ElevationScale;
                o.positionCS = TransformWorldToHClip(TransformObjectToWorld(posOS));
                return o;
            }

            #endif

            float4 frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // =============================================================
        // PASS 4: DEPTH NORMAL ONLY (NEW — #1)
        // Enables SSAO, SSR, and contact shadows by writing displaced
        // normals to HDRP's normal buffer during the prepass.
        // =============================================================
        Pass
        {
            Name "DepthNormalOnly"
            Tags { "LightMode" = "DepthNormalOnly" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5

            #pragma vertex vert
            #pragma fragment frag

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            #ifdef _TESSELLATION_ON

            struct HullCP
            {
                float3 positionOS : INTERNALTESSPOS;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
            };

            struct TessFactors
            {
                float edge[3] : SV_TessFactor;
                float inside : SV_InsideTessFactor;
            };

            HullCP vert(Attributes input)
            {
                HullCP o;
                o.positionOS = input.positionOS;
                o.uv = input.uv;
                o.normalOS = input.normalOS;
                return o;
            }

            TessFactors PatchConst(InputPatch<HullCP, 3> patch)
            {
                TessFactors f;
                float3 p0 = TransformObjectToWorld(patch[0].positionOS);
                float3 p1 = TransformObjectToWorld(patch[1].positionOS);
                float3 p2 = TransformObjectToWorld(patch[2].positionOS);
                f.edge[0] = CalcTessellationFactor((p1 + p2) * 0.5);
                f.edge[1] = CalcTessellationFactor((p2 + p0) * 0.5);
                f.edge[2] = CalcTessellationFactor((p0 + p1) * 0.5);
                f.inside = (f.edge[0] + f.edge[1] + f.edge[2]) / 3.0;
                return f;
            }

            [domain("tri")]
            [partitioning("fractional_odd")]
            [outputtopology("triangle_cw")]
            [outputcontrolpoints(3)]
            [patchconstantfunc("PatchConst")]
            [maxtessfactor(64.0)]
            HullCP hull(InputPatch<HullCP, 3> patch, uint id : SV_OutputControlPointID)
            {
                return patch[id];
            }

            [domain("tri")]
            Varyings domain(TessFactors factors, OutputPatch<HullCP, 3> patch, float3 bary : SV_DomainLocation)
            {
                float3 posOS = bary.x * patch[0].positionOS + bary.y * patch[1].positionOS + bary.z * patch[2].positionOS;
                float2 uv = bary.x * patch[0].uv + bary.y * patch[1].uv + bary.z * patch[2].uv;
                float3 normalOS = normalize(
                    bary.x * patch[0].normalOS + bary.y * patch[1].normalOS + bary.z * patch[2].normalOS);
                float elevation = SAMPLE_TEXTURE2D_LOD(_Heightmap, sampler_Heightmap, uv, 0).r;
                posOS.y += elevation * _ElevationScale;

                Varyings o;
                o.positionCS = TransformWorldToHClip(TransformObjectToWorld(posOS));
                o.normalWS = TransformObjectToWorldNormal(normalOS);
                o.uv = uv;
                return o;
            }

            #else

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 posOS = input.positionOS;
                float elevation = SAMPLE_TEXTURE2D_LOD(_Heightmap, sampler_Heightmap, input.uv, 0).r;
                posOS.y += elevation * _ElevationScale;
                o.positionCS = TransformWorldToHClip(TransformObjectToWorld(posOS));
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = input.uv;
                return o;
            }

            #endif

            // Write displaced normal to HDRP normal buffer for screen-space effects
            float4 frag(Varyings input) : SV_Target0
            {
                float3 displacedNormal = ComputeDisplacedNormal(input.uv);

                // Encode as octahedron for HDRP normal buffer
                // PackNormalOctQuadEncode is from Core RP Packing.hlsl (via Common.hlsl)
                float2 encodedNormal = PackNormalOctQuadEncode(displacedNormal);

                // HDRP normal buffer: xy = encoded normal, z = perceptual roughness, w = flags
                float perceptualRoughness = 0.5;
                return float4(encodedNormal, perceptualRoughness, 0.0);
            }
            ENDHLSL
        }

        // =============================================================
        // PASS 5: MOTION VECTORS
        // =============================================================
        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5

            #pragma vertex vert
            #pragma fragment frag

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            #ifdef _TESSELLATION_ON

            struct HullCP
            {
                float3 positionOS : INTERNALTESSPOS;
                float2 uv : TEXCOORD0;
            };

            struct TessFactors
            {
                float edge[3] : SV_TessFactor;
                float inside : SV_InsideTessFactor;
            };

            HullCP vert(Attributes input)
            {
                HullCP o;
                o.positionOS = input.positionOS;
                o.uv = input.uv;
                return o;
            }

            TessFactors PatchConst(InputPatch<HullCP, 3> patch)
            {
                TessFactors f;
                float3 p0 = TransformObjectToWorld(patch[0].positionOS);
                float3 p1 = TransformObjectToWorld(patch[1].positionOS);
                float3 p2 = TransformObjectToWorld(patch[2].positionOS);
                f.edge[0] = CalcTessellationFactor((p1 + p2) * 0.5);
                f.edge[1] = CalcTessellationFactor((p2 + p0) * 0.5);
                f.edge[2] = CalcTessellationFactor((p0 + p1) * 0.5);
                f.inside = (f.edge[0] + f.edge[1] + f.edge[2]) / 3.0;
                return f;
            }

            [domain("tri")]
            [partitioning("fractional_odd")]
            [outputtopology("triangle_cw")]
            [outputcontrolpoints(3)]
            [patchconstantfunc("PatchConst")]
            [maxtessfactor(64.0)]
            HullCP hull(InputPatch<HullCP, 3> patch, uint id : SV_OutputControlPointID)
            {
                return patch[id];
            }

            [domain("tri")]
            Varyings domain(TessFactors factors, OutputPatch<HullCP, 3> patch, float3 bary : SV_DomainLocation)
            {
                float3 posOS = bary.x * patch[0].positionOS + bary.y * patch[1].positionOS + bary.z * patch[2].positionOS;
                float2 uv = bary.x * patch[0].uv + bary.y * patch[1].uv + bary.z * patch[2].uv;
                float elevation = SAMPLE_TEXTURE2D_LOD(_Heightmap, sampler_Heightmap, uv, 0).r;
                posOS.y += elevation * _ElevationScale;

                Varyings o;
                o.positionCS = TransformWorldToHClip(TransformObjectToWorld(posOS));
                return o;
            }

            #else

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 posOS = input.positionOS;
                float elevation = SAMPLE_TEXTURE2D_LOD(_Heightmap, sampler_Heightmap, input.uv, 0).r;
                posOS.y += elevation * _ElevationScale;
                o.positionCS = TransformWorldToHClip(TransformObjectToWorld(posOS));
                return o;
            }

            #endif

            float4 frag(Varyings input) : SV_Target
            {
                return float4(0, 0, 0, 1);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/InternalErrorShader"
    CustomEditor "BiomeTerrainHDRPShaderGUI"
}
