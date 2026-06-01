Shader "Custom/BiomeTerrainHDRP"
{
    Properties
    {
        [Header(Biome Texture Arrays)]
        _BiomeAlbedoArray ("Biome Albedo Array", 2DArray) = "" {}
        _BiomeNormalArray ("Biome Normal Array", 2DArray) = "" {}
        _BiomeMaskArray ("Biome Mask Array", 2DArray) = "" {}
        _SurfaceEmissiveArray ("Surface Emissive Array", 2DArray) = "" {}
        _BiomeHeightArray ("Biome Height Array", 2DArray) = "" {}

        [Header(Index and Height Maps)]
        _BiomeIndexMap ("Biome Index Map (RFloat slice index)", 2D) = "black" {}
        _Heightmap ("Heightmap (RHalf elevation)", 2D) = "black" {}
        _LUT ("Tile Index LUT", 2D) = "black" {}

        [Header(Biome Data Textures)]
        _BiomeSurfaceMapTex ("Biome Surface Map (start,count,surface,forced)", 2D) = "black" {}
        _BiomeEmissiveMapTex ("Biome Emissive Map (RGB=tint, A=intensity)", 2D) = "black" {}
        _TileSeasonMask ("Tile Season Mask (R=snow,G=wet,B=dry)", 2D) = "white" {}
        _TileSeasonMask_TexSize ("Tile Season Mask TexSize", Vector) = (0,0,0,0)
        _TileSeasonMask_ST ("Tile Season Mask ST", Vector) = (1,1,0,0)

        [Header(Frozen Water)]
        _FreezeMaskTex ("Freeze Mask (R=freeze,G=lake,B=river)", 2D) = "black" {}
        _FreezeMask_ST ("Freeze Mask ST", Vector) = (1,1,0,0)
        _FreezeProgress ("Freeze Progress", Range(0, 1)) = 0
        _LakeIceAlbedoArray ("Lake Ice Albedo Array", 2DArray) = "" {}
        _LakeIceNormalArray ("Lake Ice Normal Array", 2DArray) = "" {}
        _LakeIceMaskArray ("Lake Ice Mask Array", 2DArray) = "" {}
        _LakeIceHeightArray ("Lake Ice Height Array", 2DArray) = "" {}
        _RiverIceAlbedoArray ("River Ice Albedo Array", 2DArray) = "" {}
        _RiverIceNormalArray ("River Ice Normal Array", 2DArray) = "" {}
        _RiverIceMaskArray ("River Ice Mask Array", 2DArray) = "" {}
        _RiverIceHeightArray ("River Ice Height Array", 2DArray) = "" {}
        _LakeIceTint ("Lake Ice Tint", Color) = (1,1,1,1)
        _RiverIceTint ("River Ice Tint", Color) = (1,1,1,1)
        _LakeIceTiling ("Lake Ice Tiling", Range(0.01, 200)) = 8.0
        _RiverIceTiling ("River Ice Tiling", Range(0.01, 200)) = 12.0
        _LakeIceSliceCount ("Lake Ice Slice Count", Float) = 0
        _RiverIceSliceCount ("River Ice Slice Count", Float) = 0
        _IceNormalStrength ("Ice Normal Strength", Range(0, 3)) = 1.0
        _IceSmoothness ("Ice Smoothness", Range(0, 1)) = 0.85
        _IceMetallic ("Ice Metallic", Range(0, 1)) = 0.0
        _FreezeOpaqueThreshold ("Freeze Opaque Threshold", Range(0.5, 1)) = 0.9

        [Header(Displacement)]
        _ElevationScale ("Elevation Scale", Range(0.1, 20)) = 1.0
        _NormalStrength ("Normal Strength", Range(0.01, 20)) = 1.0
        _NormalSampleRadius ("Normal Sample Radius (texels)", Range(1, 50)) = 4
        _BiomeNormalStrength ("Biome Normal Strength", Range(0, 5)) = 1.0

        [Header(Triplanar)]
        _TriTiling ("Triplanar Tiling", Range(0.01, 5)) = 1.15
        _TriBlend ("Triplanar Blend Sharpness", Range(1, 50)) = 5.0
        _UseTriplanar ("Use Triplanar", Float) = 1

        [Header(Map Dimensions)]
        _MapWidth ("Map Width", Float) = 100
        _MapHeight ("Map Height", Float) = 100
        _BiomeCount ("Biome Count", Float) = 32
        _TotalSlices ("Total Texture Slices", Float) = 32

        [Header(Global Modifiers)]
        _GlobalSnowAmount ("Global Snow Amount", Range(0, 1)) = 0
        _GlobalWetness ("Global Wetness (legacy, unused)", Range(0, 1)) = 0
        _MetallicMultiplier ("Global Metallic Multiplier", Range(0, 2)) = 1.0
        _AOIntensity ("Global AO Intensity", Range(0, 2)) = 1.0
        _SmoothnessMultiplier ("Global Smoothness Multiplier", Range(0, 2)) = 1.0
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

        [Header(Hex Grid Overlay)]
        _ShowHexGrid ("Show Hex Grid", Float) = 0
        _HexGridColor ("Hex Grid Color", Color) = (0, 0, 0, 1)
        _HexGridWidth ("Hex Grid Width (texels)", Range(0.1, 8)) = 1.0
        _HexGridFadeDistance ("Hex Grid Fade Distance", Range(0, 1000)) = 200

        [Header(Tile Highlight)]
        _HighlightTileIndex ("Highlight Tile Index", Float) = -1
        _HighlightColor ("Highlight Color", Color) = (1, 1, 0, 1)

        // ============== NEW PROPERTIES ==============

        [Header(Micro Detail)]
        _DetailAlbedoMap ("Detail Albedo Map", 2D) = "gray" {}
        _DetailNormalMap ("Detail Normal Map", 2D) = "bump" {}
        _DetailTiling ("Detail Tiling", Range(1, 100)) = 20.0
        _DetailStrength ("Detail Albedo Strength", Range(0, 10)) = 0.3
        _DetailNormalStrength ("Detail Normal Strength", Range(0, 20)) = 0.5
        _DetailFadeStart ("Detail Fade Start Distance", Range(0, 200)) = 5.0
        _DetailFadeEnd ("Detail Fade End Distance", Range(0, 500)) = 50.0
        _SurfaceHeightScale ("Surface Height Scale", Range(0, 2)) = 0.05

        

        [Header(Biome Blending)]
        _BiomeBlendRadius ("Biome Blend Radius (texels)", Range(0, 16)) = 4.0
        _BiomeBlendSharpness ("Height Blend Sharpness", Range(0.01, 10)) = 3.0

        [Header(Snow Detail)]
        _SnowNormalStrength ("Snow Normal Strength", Range(0, 2)) = 0.5
        _SnowNormalTiling ("Snow Normal Tiling", Range(0.1, 20)) = 5.0
        _SnowSparkleStrength ("Snow Sparkle Strength", Range(0, 1)) = 0.3
        
        [Header(Wetness)]
        _WetNormalStrength ("Wet Normal Strength", Range(0, 2)) = 0.35
        _WetNormalTiling ("Wet Normal Tiling", Range(0.1, 20)) = 8.0
        _WetSmoothnessBoost ("Wet Smoothness Boost", Range(0, 1)) = 0.5
        _WetAlbedoDarken ("Wet Albedo Darken", Range(0, 1)) = 0.5

        [Header(Distance LOD)]
        _TriplanarLODStart ("Triplanar LOD Start Distance", Range(0, 200)) = 50.0
        _TriplanarLODEnd ("Triplanar LOD End Distance", Range(0, 500)) = 150.0

        [Header(Tessellation)]
        [Toggle(_TESSELLATION_ON)] _EnableTessellation ("Enable Tessellation", Float) = 0
        _TessellationFactor ("Tessellation Factor", Range(1, 64)) = 8.0
        _TessellationFadeStart ("Tessellation Fade Start", Range(0, 200)) = 10.0
        _TessellationFadeEnd ("Tessellation Fade End", Range(0, 500)) = 100.0
        
        [Header(Cliff Overlay)]
        _CliffAlbedoArray ("Cliff Albedo Array", 2DArray) = "" {}
        _CliffNormalArray ("Cliff Normal Array", 2DArray) = "" {}
        // Preview fallbacks (regular 2D textures) - visible in older Unity inspectors
        _CliffAlbedoPreview ("Cliff Albedo Preview (fallback)", 2D) = "white" {}
        _CliffNormalPreview ("Cliff Normal Preview (fallback)", 2D) = "bump" {}
        _CliffTiling ("Cliff Tiling", Range(0.1, 200)) = 12.0
        _CliffStrength ("Cliff Strength", Range(0,10)) = 1.0
        _CliffSlopeThreshold ("Cliff Slope Threshold", Range(0,1)) = 0.5
        _CliffSlopeBlend ("Cliff Slope Blend", Range(0,10)) = 0.2
        _CliffStepThreshold ("Cliff Step Threshold (texel units)", Range(0,1)) = 0.15
        _CliffStepBlend ("Cliff Step Blend (texel units)", Range(0,10)) = 0.08
        _CliffSliceCount ("Cliff Slice Count", Float) = 1
    }

    HLSLINCLUDE

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

    // ===================== Texture & Sampler Declarations =====================

    TEXTURE2D_ARRAY(_BiomeAlbedoArray);    SAMPLER(sampler_BiomeAlbedoArray);
        TEXTURE2D_ARRAY(_BiomeNormalArray);    SAMPLER(sampler_BiomeNormalArray);
        TEXTURE2D_ARRAY(_BiomeMaskArray);      SAMPLER(sampler_BiomeMaskArray);
        TEXTURE2D_ARRAY(_SurfaceEmissiveArray); SAMPLER(sampler_SurfaceEmissiveArray);
        TEXTURE2D_ARRAY(_BiomeHeightArray);    SAMPLER(sampler_BiomeHeightArray);
        TEXTURE2D_ARRAY(_CliffAlbedoArray);    SAMPLER(sampler_CliffAlbedoArray);
        TEXTURE2D_ARRAY(_CliffNormalArray);    SAMPLER(sampler_CliffNormalArray);
        TEXTURE2D_ARRAY(_LakeIceAlbedoArray);  SAMPLER(sampler_LakeIceAlbedoArray);
        TEXTURE2D_ARRAY(_LakeIceNormalArray);  SAMPLER(sampler_LakeIceNormalArray);
        TEXTURE2D_ARRAY(_LakeIceMaskArray);    SAMPLER(sampler_LakeIceMaskArray);
        TEXTURE2D_ARRAY(_LakeIceHeightArray);  SAMPLER(sampler_LakeIceHeightArray);
        TEXTURE2D_ARRAY(_RiverIceAlbedoArray); SAMPLER(sampler_RiverIceAlbedoArray);
        TEXTURE2D_ARRAY(_RiverIceNormalArray); SAMPLER(sampler_RiverIceNormalArray);
        TEXTURE2D_ARRAY(_RiverIceMaskArray);   SAMPLER(sampler_RiverIceMaskArray);
        TEXTURE2D_ARRAY(_RiverIceHeightArray); SAMPLER(sampler_RiverIceHeightArray);
    TEXTURE2D(_CliffAlbedoPreview);
    TEXTURE2D(_CliffNormalPreview);

    TEXTURE2D(_BiomeIndexMap);       SAMPLER(sampler_BiomeIndexMap);
    TEXTURE2D(_Heightmap);           SAMPLER(sampler_Heightmap);
    TEXTURE2D(_LUT);
    TEXTURE2D(_BiomeSurfaceMapTex);
    TEXTURE2D(_BiomeEmissiveMapTex);
    TEXTURE2D(_TileSeasonMask);
    TEXTURE2D(_FreezeMaskTex);
    TEXTURE2D(_FogMask);
    TEXTURE2D(_OwnershipOverlay);
    TEXTURE2D(_SliceToBiomeMap);
    TEXTURE2D(_DetailAlbedoMap);     SAMPLER(sampler_DetailAlbedoMap);
    TEXTURE2D(_DetailNormalMap);     SAMPLER(sampler_DetailNormalMap);

    // ===================== Uniforms =====================

    float _ElevationScale;
    float _NormalStrength;
    float _NormalSampleRadius;
    float _BiomeNormalStrength;
    float4 _Heightmap_TexelSize;
    float4 _BiomeIndexMap_TexelSize;
    float _TriTiling;
    float _TriBlend;
    float _UseTriplanar;
    float _MapWidth;
    float _MapHeight;
    float _BiomeCount;
    float _TotalSlices;
    float _GlobalSnowAmount;
    float _GlobalWetness;
    float _MetallicMultiplier;
    float _AOIntensity;
    float _SmoothnessMultiplier;
    float4 _SnowColor;
    float _SnowSmoothness;
    float _EnableFog;
    float4 _TerrainFogColor;
    float _EnableOwnership;
    float _OwnershipAlpha;
    float _HighlightTileIndex;
    float4 _HighlightColor;

    // New uniforms
    float _DetailTiling;
    float _DetailStrength;
    float _DetailNormalStrength;
    float _DetailFadeStart;
    float _DetailFadeEnd;
    float4 _TileSeasonMask_TexSize;
    float4 _TileSeasonMask_ST;
    float4 _FreezeMask_ST;
    float _FreezeProgress;
    float4 _LakeIceTint;
    float4 _RiverIceTint;
    float _LakeIceTiling;
    float _RiverIceTiling;
    float _LakeIceSliceCount;
    float _RiverIceSliceCount;
    float _IceNormalStrength;
    float _IceSmoothness;
    float _IceMetallic;
    float _FreezeOpaqueThreshold;
    
    float _BiomeBlendRadius;
    float _BiomeBlendSharpness;
    float _SnowNormalStrength;
    float _SnowNormalTiling;
    float _SnowSparkleStrength;
    float _WetNormalStrength;
    float _WetNormalTiling;
    float _WetSmoothnessBoost;
    float _WetAlbedoDarken;
    float _TriplanarLODStart;
    float _TriplanarLODEnd;
    float _CliffTiling;
    float _CliffStrength;
    float _CliffSlopeThreshold;
    float _CliffSlopeBlend;
    float _CliffStepThreshold;
    float _CliffStepBlend;
    float _CliffSliceCount;
    float _SurfaceHeightScale;
    float _TessellationFactor;
    float _TessellationFadeStart;
    float _TessellationFadeEnd;
    float _ShowHexGrid;
    float4 _HexGridColor;
    float _HexGridWidth;
    float _HexGridFadeDistance;

    // Per-biome arrays (set via SetVectorArray from C#, max 64 biomes)
    float4 _BiomeTints[64];
    float4 _BiomeParams[64];
    float4 _BiomeRoughnessOffsets[16]; // packed: each float4 holds 4 biome offsets (max 64 biomes)
    // per-slice height will be read from _BiomeHeightArray if present

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

        // Use raw triangle weights (anti-tiling removed).
        float wSum = w1 + w2 + w3;

        // Use gradient-aware sampling to provide correct LOD selection and avoid swimming/seams
        float2 ddx1 = ddx(uv1); float2 ddy1 = ddy(uv1);
        float2 ddx2 = ddx(uv2); float2 ddy2 = ddy(uv2);
        float2 ddx3 = ddx(uv3); float2 ddy3 = ddy(uv3);

        float4 s1 = tex.SampleGrad(samp, float3(uv1, sliceIndex), ddx1, ddy1) * w1;
        float4 s2 = tex.SampleGrad(samp, float3(uv2, sliceIndex), ddx2, ddy2) * w2;
        float4 s3 = tex.SampleGrad(samp, float3(uv3, sliceIndex), ddx3, ddy3) * w3;

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

        // Use raw triangle weights (anti-tiling removed).
        float wSum = w1 + w2 + w3;

        // Gradient-aware sampling for normals too
        float2 ddx1 = ddx(uv1); float2 ddy1 = ddy(uv1);
        float2 ddx2 = ddx(uv2); float2 ddy2 = ddy(uv2);
        float2 ddx3 = ddx(uv3); float2 ddy3 = ddy(uv3);

        float3 n1 = UnpackNormal(tex.SampleGrad(samp, float3(uv1, sliceIndex), ddx1, ddy1));
        float3 n2 = UnpackNormal(tex.SampleGrad(samp, float3(uv2, sliceIndex), ddx2, ddy2));
        float3 n3 = UnpackNormal(tex.SampleGrad(samp, float3(uv3, sliceIndex), ddx3, ddy3));

        float3 blended = (n1 * w1 + n2 * w2 + n3 * w3) / wSum;
        // Scale tangent-space XY by _BiomeNormalStrength before normalization.
        // >1 amplifies surface bumps, <1 flattens them, 0 = flat normal.
        blended.xy *= _BiomeNormalStrength;
        return normalize(blended);
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
        // Use gradient-aware sampling for each projection to avoid incorrect LOD selection
        float2 ux = worldPos.zy * tiling;
        float2 uy = worldPos.xz * tiling;
        float2 uz = worldPos.xy * tiling;

        float2 ddxUx = ddx(ux); float2 ddyUx = ddy(ux);
        float2 ddxUy = ddx(uy); float2 ddyUy = ddy(uy);
        float2 ddxUz = ddx(uz); float2 ddyUz = ddy(uz);

        float4 sX = tex.SampleGrad(samp, float3(ux, sliceIndex), ddxUx, ddyUx);
        float4 sY = tex.SampleGrad(samp, float3(uy, sliceIndex), ddxUy, ddyUy);
        float4 sZ = tex.SampleGrad(samp, float3(uz, sliceIndex), ddxUz, ddyUz);

        return sX * weights.x + sY * weights.y + sZ * weights.z;
    }

    // Original triplanar normal with whiteout blending (non-hex fallback)
    float3 SampleNormalTriplanar(TEXTURE2D_ARRAY_PARAM(tex, samp),
        float3 worldPos, float3 worldNormal, float3 weights, float sliceIndex, float tiling)
    {
        // Compute projected UVs and their derivatives
        float2 ux = worldPos.zy * tiling;
        float2 uy = worldPos.xz * tiling;
        float2 uz = worldPos.xy * tiling;

        float2 ddxUx = ddx(ux); float2 ddyUx = ddy(ux);
        float2 ddxUy = ddx(uy); float2 ddyUy = ddy(uy);
        float2 ddxUz = ddx(uz); float2 ddyUz = ddy(uz);

        float3 tnX = UnpackNormal(tex.SampleGrad(samp, float3(ux, sliceIndex), ddxUx, ddyUx));
        float3 tnY = UnpackNormal(tex.SampleGrad(samp, float3(uy, sliceIndex), ddxUy, ddyUy));
        float3 tnZ = UnpackNormal(tex.SampleGrad(samp, float3(uz, sliceIndex), ddxUz, ddyUz));

        // Scale tangent-space XY by biome normal strength before whiteout reorientation
        tnX.xy *= _BiomeNormalStrength;
        tnY.xy *= _BiomeNormalStrength;
        tnZ.xy *= _BiomeNormalStrength;

        // Reorient sampled tangent-space normals into projection-space using worldNormal as bias
        float3 nX = float3(tnX.xy + worldNormal.zy, abs(worldNormal.x));
        float3 nY = float3(tnY.xy + worldNormal.xz, abs(worldNormal.y));
        float3 nZ = float3(tnZ.xy + worldNormal.xy, abs(worldNormal.z));

        // Blend and renormalize
        float3 blended = nX.zyx * weights.x + nY.xzy * weights.y + nZ.xyz * weights.z;
        return normalize(blended);
    }

    // ===================== Distance-Adaptive Sampling (#4 + #6) =====================
    // Blends between hex-tiled triplanar (close) and single Y-planar (far)

    float4 SampleBiomeTexture(TEXTURE2D_ARRAY_PARAM(tex, samp),
        float3 worldPos, float3 triWeights, float sliceIndex, float tiling, float camDist, float2 mapUV)
    {
        float denom = _TriplanarLODEnd - _TriplanarLODStart;
        float lodBlend = (denom <= 0.001) ? 0.0 : saturate((camDist - _TriplanarLODStart) / max(denom, 0.01));
        float2 uvY = worldPos.xz * tiling;

        float4 result = SAMPLE_TEXTURE2D_ARRAY(tex, samp, uvY, sliceIndex);

        if (_UseTriplanar < 0.5)
            return result;

        // Far distance: single Y-axis planar sample (1 sample)
        if (lodBlend >= 0.999)
            return result;

        // Compute a per-tile hash (using the biome index map UV) so large hex-cells
        // don't end up repeating identically across neighboring map tiles when
        // triplanar tiling is small. This biases the per-axis sample UVs by a
        // deterministic tile-local offset.
        float2 tileIdx = floor(mapUV / max(_BiomeIndexMap_TexelSize.xy, 1e-6));
        float2 tileHash = HexHash(tileIdx);

        // Hex-tiled triplanar (9 samples: 3 axes x 3 hex cells)
        float4 sX = SampleHexTiled(TEXTURE2D_ARRAY_ARGS(tex, samp), worldPos.zy * tiling + tileHash, sliceIndex);
        float4 sY = SampleHexTiled(TEXTURE2D_ARRAY_ARGS(tex, samp), uvY + tileHash, sliceIndex);
        float4 sZ = SampleHexTiled(TEXTURE2D_ARRAY_ARGS(tex, samp), worldPos.xy * tiling + tileHash, sliceIndex);
        float4 fullResult = sX * triWeights.x + sY * triWeights.y + sZ * triWeights.z;

        result = fullResult;

        if (lodBlend > 0.001)
        {
            float4 yOnly = SAMPLE_TEXTURE2D_ARRAY(tex, samp, uvY + tileHash, sliceIndex);
            result = lerp(fullResult, yOnly, lodBlend);
        }

        return result;
    }

    // Distance-adaptive normal sampling with hex tiling and whiteout blending
    float3 SampleBiomeNormal(TEXTURE2D_ARRAY_PARAM(tex, samp),
        float3 worldPos, float3 worldNormal, float3 triWeights,
        float sliceIndex, float tiling, float camDist, float2 mapUV)
    {
        float denom = _TriplanarLODEnd - _TriplanarLODStart;
        float lodBlend = (denom <= 0.001) ? 0.0 : saturate((camDist - _TriplanarLODStart) / max(denom, 0.01));
        float2 uvY = worldPos.xz * tiling;
        float3 tnYBase = UnpackNormal(SAMPLE_TEXTURE2D_ARRAY(tex, samp, uvY, sliceIndex));
        tnYBase.xy *= _BiomeNormalStrength;
        float3 nYBase = float3(tnYBase.xy + worldNormal.xz, abs(worldNormal.y));
        float3 result = normalize(nYBase.xzy);

        // If triplanar disabled, always use Y-only planar normal
        if (_UseTriplanar < 0.5)
            return result;

        // Far distance: single Y-axis normal
        if (lodBlend >= 0.999)
            return result;

        // Per-tile hash to offset hex sampling similarly to albedo sampling.
        float2 tileIdx = floor(mapUV / max(_BiomeIndexMap_TexelSize.xy, 1e-6));
        float2 tileHash = HexHash(tileIdx);

        // Hex-tiled triplanar normals with whiteout reorientation
        float3 tnX = SampleNormalHexTiled(TEXTURE2D_ARRAY_ARGS(tex, samp), worldPos.zy * tiling + tileHash, sliceIndex);
        float3 tnY = SampleNormalHexTiled(TEXTURE2D_ARRAY_ARGS(tex, samp), uvY + tileHash, sliceIndex);
        float3 tnZ = SampleNormalHexTiled(TEXTURE2D_ARRAY_ARGS(tex, samp), worldPos.xy * tiling + tileHash, sliceIndex);

        float3 nX = float3(tnX.xy + worldNormal.zy, abs(worldNormal.x));
        float3 nY = float3(tnY.xy + worldNormal.xz, abs(worldNormal.y));
        float3 nZ = float3(tnZ.xy + worldNormal.xy, abs(worldNormal.z));
        float3 fullResult = normalize(nX.zyx * triWeights.x + nY.xzy * triWeights.y + nZ.xyz * triWeights.z);

        result = fullResult;

        if (lodBlend > 0.001)
        {
            float3 tnYFar = UnpackNormal(SAMPLE_TEXTURE2D_ARRAY(tex, samp, uvY, sliceIndex));
            tnYFar.xy *= _BiomeNormalStrength;
            float3 nYFar = float3(tnYFar.xy + worldNormal.xz, abs(worldNormal.y));
            float3 yResult = normalize(nYFar.xzy);
            result = normalize(lerp(fullResult, yResult, lodBlend));
        }
        return result;
    }

    // ===================== Shared Helpers =====================

    // Legacy fallback: only used if biomeIdx was not stored directly in the index map.
    int GetBiomeIndexFromSlice(float sliceIndex)
    {
        float u = (sliceIndex + 0.5) / max(_TotalSlices, 1.0);
        return (int)(SAMPLE_TEXTURE2D_LOD(_SliceToBiomeMap, sampler_BiomeIndexMap, float2(u, 0.5), 0).r + 0.5);
    }

    int DecodeTileIndex(float2 uv)
    {
        float4 lutSample = SAMPLE_TEXTURE2D_LOD(_LUT, sampler_BiomeIndexMap, uv, 0);
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

            #define SHADERPASS SHADERPASS_FORWARD
            #define SHADEROPTIONS_SHADOW_ALGORITHM SHADOW_ALGORITHM_CLASSIC
            #define PUNCTUAL_SHADOW_MEDIUM
            #define DIRECTIONAL_SHADOW_MEDIUM
            #define AREA_SHADOW_MEDIUM
            #define HAS_LIGHTLOOP
            #include "Packages/com.unity.render-pipelines.high-definition-config/Runtime/ShaderConfig.cs.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoopDef.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoop.hlsl"



            // Distance-adaptive tessellation factor
            float CalcTessellationFactor(float3 positionWS)
            {
                // HDRP often uses camera-relative rendering for "world space" positions.
                // For world-anchored texturing / distances we must operate in absolute WS,
                // otherwise textures appear to "swim" as the camera moves.
                float3 absPosWS = GetAbsolutePositionWS(positionWS);
                float dist = distance(absPosWS, _WorldSpaceCameraPos);
                float f = 1.0 - saturate((dist - _TessellationFadeStart) /
                    max(_TessellationFadeEnd - _TessellationFadeStart, 0.01));
                return max(f * _TessellationFactor, 1.0);
            }

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
                float4 biomeParams; // x=tiling, y=winterSnow, z=inherentWetness (0-1), w=isWaterBiome
                float height;
            };

            float SelectIceSlice(float3 worldPos, float sliceCount)
            {
                if (sliceCount <= 1.0)
                    return 0.0;

                float h = frac(sin(dot(floor(worldPos.xz * 0.125), float2(12.9898, 78.233))) * 43758.5453);
                return floor(h * sliceCount);
            }

            BiomeSample SampleIceSurface(
                TEXTURE2D_ARRAY_PARAM(albedoTex, albedoSampler),
                TEXTURE2D_ARRAY_PARAM(normalTex, normalSampler),
                TEXTURE2D_ARRAY_PARAM(maskTex, maskSampler),
                TEXTURE2D_ARRAY_PARAM(heightTex, heightSampler),
                float sliceCount,
                float4 tint,
                float tiling,
                float3 worldPos,
                float3 displacedNormal,
                float3 triWeights,
                float camDist,
                float2 mapUV)
            {
                BiomeSample s;
                ZERO_INITIALIZE(BiomeSample, s);
                s.normalWS = displacedNormal;
                s.mask = float4(0, 1, 0, _IceSmoothness);

                if (sliceCount <= 0.0)
                    return s;

                float sliceIndex = SelectIceSlice(worldPos, sliceCount);
                float effectiveTiling = max(tiling, 0.01);

                float4 albedoRaw = SampleBiomeTexture(
                    TEXTURE2D_ARRAY_ARGS(albedoTex, albedoSampler),
                    worldPos, triWeights, sliceIndex, effectiveTiling, camDist, mapUV);
                float3 normalRaw = SampleBiomeNormal(
                    TEXTURE2D_ARRAY_ARGS(normalTex, normalSampler),
                    worldPos, displacedNormal, triWeights, sliceIndex, effectiveTiling, camDist, mapUV);
                float4 maskRaw = SampleBiomeTexture(
                    TEXTURE2D_ARRAY_ARGS(maskTex, maskSampler),
                    worldPos, triWeights, sliceIndex, effectiveTiling, camDist, mapUV);
                float4 heightRaw = SampleBiomeTexture(
                    TEXTURE2D_ARRAY_ARGS(heightTex, heightSampler),
                    worldPos, triWeights, sliceIndex, effectiveTiling, camDist, mapUV);

                s.albedo = albedoRaw.rgb * tint.rgb;
                s.normalWS = normalize(lerp(displacedNormal, normalRaw, saturate(_IceNormalStrength)));
                s.mask = maskRaw;
                s.mask.r = _IceMetallic;
                s.mask.a = _IceSmoothness;
                s.height = heightRaw.r;
                return s;
            }

            BiomeSample SampleFullBiome(float sliceIndex, int biomeIdx, float3 worldPos,
                float3 displacedNormal, float3 triWeights, float camDist, float2 mapUV)
            {
                BiomeSample s;
                biomeIdx = clamp(biomeIdx, 0, 63);

                float4 biomeTint = _BiomeTints[biomeIdx];
                s.biomeParams = _BiomeParams[biomeIdx];
                float biomeTiling = max(s.biomeParams.x, 0.01);
                float effectiveTiling = _TriTiling * biomeTiling;

                // Distance-adaptive hex-tiled triplanar sampling (#4, #6)
                float4 albedoRaw = SampleBiomeTexture(
                    TEXTURE2D_ARRAY_ARGS(_BiomeAlbedoArray, sampler_BiomeAlbedoArray),
                    worldPos, triWeights, sliceIndex, effectiveTiling, camDist, mapUV);
                s.normalWS = SampleBiomeNormal(
                    TEXTURE2D_ARRAY_ARGS(_BiomeNormalArray, sampler_BiomeNormalArray),
                    worldPos, displacedNormal, triWeights, sliceIndex, effectiveTiling, camDist, mapUV);
                s.mask = SampleBiomeTexture(
                    TEXTURE2D_ARRAY_ARGS(_BiomeMaskArray, sampler_BiomeMaskArray),
                    worldPos, triWeights, sliceIndex, effectiveTiling, camDist, mapUV);

                // Overlay blend: preserves substrate texture contrast while applying
                // strong biome tints (grayscale substrate × saturated color).
                // Below 0.5 gray: 2*base*tint (darkens). Above 0.5: 1-2*(1-base)*(1-tint) (lightens).
                float3 base = albedoRaw.rgb;
                float3 tint = biomeTint.rgb;
                float3 overlayR = (base < 0.5)
                    ? 2.0 * base * tint
                    : 1.0 - 2.0 * (1.0 - base) * (1.0 - tint);
                s.albedo = overlayR;

                // Emissive
                float4 emissiveParams = SAMPLE_TEXTURE2D_LOD(_BiomeEmissiveMapTex, sampler_BiomeIndexMap,
                    float2((biomeIdx + 0.5) / max(_BiomeCount, 1.0), 0.5), 0);
                float4 emissiveTex = SampleBiomeTexture(
                    TEXTURE2D_ARRAY_ARGS(_SurfaceEmissiveArray, sampler_SurfaceEmissiveArray),
                    worldPos, triWeights, sliceIndex, effectiveTiling, camDist, mapUV);
                s.emission = emissiveTex.rgb * emissiveParams.rgb * emissiveParams.a;

                // Optional per-surface height (single-channel in R). If the family provided
                // a height Texture2DArray, sample it the same way as other surface textures.
                float4 heightTex = SampleBiomeTexture(TEXTURE2D_ARRAY_ARGS(_BiomeHeightArray, sampler_BiomeHeightArray),
                    worldPos, triWeights, sliceIndex, effectiveTiling, camDist, mapUV);
                s.height = heightTex.r;

                return s;
            }

            // ===================== Fragment Shader =====================

            float4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                // Convert to absolute WS so triplanar UVs are world-anchored (no camera swimming).
                float3 worldPos = GetAbsolutePositionWS(input.positionWS);
                float3 V = normalize(_WorldSpaceCameraPos - worldPos);
                // Use camera-forward distance: how far the sampled world point
                // lies along the camera view ray. This ties fades strictly to
                // the camera position/direction rather than any map-center or
                // other artifacts.
                float camDist = distance(_WorldSpaceCameraPos, worldPos);

                // --- Displaced normal from heightmap ---
                float3 displacedNormal = ComputeDisplacedNormal(uv);
                float3 triWeights = TriplanarWeights(displacedNormal);

                // ==========================================================
                // BIOME INDEX & TRANSITION BLENDING (#3, #7)
                // _BiomeIndexMap: R = surface slice index, G = biome index
                // ==========================================================
                float4 centerSample = SAMPLE_TEXTURE2D_LOD(_BiomeIndexMap, sampler_BiomeIndexMap, uv, 0);
                float centerSlice = round(centerSample.r);
                int centerBiome = (int)(centerSample.g + 0.5);

                // Sample neighbors to detect biome boundaries
                float2 biomeStep = _BiomeIndexMap_TexelSize.xy * max(_BiomeBlendRadius, 0.5);

                float4 sampleR = SAMPLE_TEXTURE2D_LOD(_BiomeIndexMap, sampler_BiomeIndexMap,
                    uv + float2(biomeStep.x, 0), 0);
                float4 sampleL = SAMPLE_TEXTURE2D_LOD(_BiomeIndexMap, sampler_BiomeIndexMap,
                    uv - float2(biomeStep.x, 0), 0);
                float4 sampleU = SAMPLE_TEXTURE2D_LOD(_BiomeIndexMap, sampler_BiomeIndexMap,
                    uv + float2(0, biomeStep.y), 0);
                float4 sampleD = SAMPLE_TEXTURE2D_LOD(_BiomeIndexMap, sampler_BiomeIndexMap,
                    uv - float2(0, biomeStep.y), 0);

                float sliceR = round(sampleR.r);
                float sliceL = round(sampleL.r);
                float sliceU = round(sampleU.r);
                float sliceD = round(sampleD.r);

                // Find secondary biome for blending
                float secondarySlice = centerSlice;
                int secondaryBiome = centerBiome;
                int diffCount = 0;
                float2 neighborOffset = float2(0.0, 0.0);
                if (sliceR != centerSlice) { secondarySlice = sliceR; secondaryBiome = (int)(sampleR.g + 0.5); neighborOffset = float2(biomeStep.x, 0); diffCount++; }
                if (sliceL != centerSlice) { if (diffCount == 0) { secondarySlice = sliceL; secondaryBiome = (int)(sampleL.g + 0.5); neighborOffset = float2(-biomeStep.x, 0); } diffCount++; }
                if (sliceU != centerSlice) { if (diffCount == 0) { secondarySlice = sliceU; secondaryBiome = (int)(sampleU.g + 0.5); neighborOffset = float2(0, biomeStep.y); } diffCount++; }
                if (sliceD != centerSlice) { if (diffCount == 0) { secondarySlice = sliceD; secondaryBiome = (int)(sampleD.g + 0.5); neighborOffset = float2(0, -biomeStep.y); } diffCount++; }

                // Sample primary biome
                BiomeSample primary = SampleFullBiome(centerSlice, centerBiome, worldPos, displacedNormal, triWeights, camDist, uv);

                float3 albedo;
                float3 normalWS;
                float4 mask;
                float3 emission;
                float4 biomeParams;
                float blendedHeight = 0.0;

                // Height-based biome blending at boundaries (#3, #7: mask.b = height)
                if (diffCount > 0 && secondarySlice != centerSlice && _BiomeBlendRadius > 0.01)
                {
                    BiomeSample secondary = SampleFullBiome(secondarySlice, secondaryBiome, worldPos, displacedNormal, triWeights, camDist, uv);

                    // Use generated global _Heightmap for height-based blend (sample center and neighbor)
                    float hPrimary = SAMPLE_TEXTURE2D_LOD(_Heightmap, sampler_Heightmap, uv, 0).r;
                    float hSecondary = SAMPLE_TEXTURE2D_LOD(_Heightmap, sampler_Heightmap, uv + neighborOffset, 0).r;

                    // Spatial blend from neighbor count + height-weighted modulation
                    float spatialBlend = (float)diffCount / 4.0;
                    float heightDiff = (hSecondary - hPrimary) * _BiomeBlendSharpness;
                    float blend = saturate(spatialBlend * 0.5 + heightDiff * 0.25 + 0.25 * spatialBlend);

                    albedo = lerp(primary.albedo, secondary.albedo, blend);
                    normalWS = normalize(lerp(primary.normalWS, secondary.normalWS, blend));
                    mask = lerp(primary.mask, secondary.mask, blend);
                    emission = lerp(primary.emission, secondary.emission, blend);
                    biomeParams = lerp(primary.biomeParams, secondary.biomeParams, blend);
                     blendedHeight = lerp(primary.height, secondary.height, blend);
                }
                else
                {
                    albedo = primary.albedo;
                    normalWS = primary.normalWS;
                    mask = primary.mask;
                    emission = primary.emission;
                    biomeParams = primary.biomeParams;
                     blendedHeight = primary.height;
                }

                // ==========================================================
                // CLIFF OVERLAY: combine slope-based and tile-step detection
                //  - slope-based: preserves previous slope behavior
                //  - step-based: detects abrupt per-texel elevation jumps (tile sides)
                // ==========================================================
                if (_CliffStrength > 0.001 && _CliffSliceCount >= 1.0)
                {
                    // slope-based component (existing)
                    float slope = saturate(1.0 - displacedNormal.y);
                    float slopeBlend = smoothstep(_CliffSlopeThreshold - _CliffSlopeBlend, _CliffSlopeThreshold + _CliffSlopeBlend, slope);

                    // step-based component: sample immediate neighbors in heightmap (texel offsets)
                    float2 texel = _Heightmap_TexelSize.xy;
                    float hC = SAMPLE_TEXTURE2D_LOD(_Heightmap, sampler_Heightmap, uv, 0).r;
                    float hL = SAMPLE_TEXTURE2D_LOD(_Heightmap, sampler_Heightmap, uv - float2(texel.x, 0), 0).r;
                    float hR = SAMPLE_TEXTURE2D_LOD(_Heightmap, sampler_Heightmap, uv + float2(texel.x, 0), 0).r;
                    float hD = SAMPLE_TEXTURE2D_LOD(_Heightmap, sampler_Heightmap, uv - float2(0, texel.y), 0).r;
                    float hU = SAMPLE_TEXTURE2D_LOD(_Heightmap, sampler_Heightmap, uv + float2(0, texel.y), 0).r;

                    float sL = saturate((hC - hL - _CliffStepThreshold) / max(_CliffStepBlend, 1e-5));
                    float sR = saturate((hC - hR - _CliffStepThreshold) / max(_CliffStepBlend, 1e-5));
                    float sD = saturate((hC - hD - _CliffStepThreshold) / max(_CliffStepBlend, 1e-5));
                    float sU = saturate((hC - hU - _CliffStepThreshold) / max(_CliffStepBlend, 1e-5));

                    // strongest step among neighbors
                    float stepMask = max(max(sL, sR), max(sU, sD));

                    // combined blend (scale by global cliff strength)
                    float cliffBlend = max(slopeBlend, stepMask) * _CliffStrength;

                    if (cliffBlend > 0.001)
                    {
                        // pick a variant slice deterministically from worldPos
                        float hash = frac(sin(dot(worldPos.xz, float2(12.9898,78.233))) * 43758.5453);
                        float sliceF = floor(hash * max(1.0, _CliffSliceCount - 1.0) + 0.5);

                        // Sample cliff albedo and normal using triplanar/hex helpers
                        float4 cliffAlb = SampleBiomeTexture(TEXTURE2D_ARRAY_ARGS(_CliffAlbedoArray, sampler_CliffAlbedoArray), worldPos, triWeights, sliceF, _CliffTiling, camDist, uv);
                        float3 cliffNorm = SampleBiomeNormal(TEXTURE2D_ARRAY_ARGS(_CliffNormalArray, sampler_CliffNormalArray), worldPos, normalWS, triWeights, sliceF, _CliffTiling, camDist, uv);

                        // For step edges, prefer darker, more vertical look: lerp by cliffBlend
                        albedo = lerp(albedo, cliffAlb.rgb, cliffBlend);
                        normalWS = normalize(lerp(normalWS, cliffNorm, cliffBlend));
                        mask.a = lerp(mask.a, max(0.05, mask.a * 0.3), cliffBlend);
                    }
                }

                // Unpack mask: R=Metallic, G=AO, B=Height (used above), A=Smoothness
                float metallic = saturate(mask.r * _MetallicMultiplier);
                float ao = saturate(mask.g * _AOIntensity);
                // Apply per-biome roughness offset from SurfaceFamilyData
                // (packed 4 per float4; blend between primary/secondary at boundaries)
                int roIdxP = centerBiome >> 2;  // Bitwise shift = faster than division by 4
                int roCompP = centerBiome & 3;  // Bitwise AND = faster than modulus 4
                float roughnessOffset = _BiomeRoughnessOffsets[roIdxP][roCompP];
                float smoothness = saturate(mask.a * _SmoothnessMultiplier - roughnessOffset);

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
                    // Modulate detail normal strength by blended per-surface height so
                    // surfaces with more height show stronger micro-relief.
                    float dnStr = _DetailNormalStrength * detailFade * (1.0 + blendedHeight * _SurfaceHeightScale);
                    normalWS = normalize(float3(
                        normalWS.x + detailNorm.x * dnStr,
                        normalWS.y,
                        normalWS.z + detailNorm.y * dnStr
                    ));
                }

                // ==========================================================
                // SNOW OVERLAY WITH NORMAL PERTURBATION (#8)
                // ==========================================================
                float biomeWinterSnow = biomeParams.y;
                float isWaterBiome = biomeParams.w;

                float2 freezeUV = uv * _FreezeMask_ST.xy + _FreezeMask_ST.zw;
                float4 freezeMaskSample = SAMPLE_TEXTURE2D(_FreezeMaskTex, sampler_BiomeIndexMap, freezeUV);
                float freezeAmount = saturate(freezeMaskSample.r * _FreezeProgress);
                float lakeMask = saturate(freezeMaskSample.g);
                float riverMask = saturate(freezeMaskSample.b);

                // Allow zero retention (no forced baseline). Previously used a hard 0.3 minimum
                // which prevented per-biome zero-snow behavior. Use 0.0 -> 1.0 mapping so
                // biome value of 0 produces no snow when other factors are zero.
                float snowRetention = lerp(0.0, 1.0, biomeWinterSnow);
                // Base snow mask based on slope/normal and global amount
                float snowMask = saturate(displacedNormal.y) * _GlobalSnowAmount * snowRetention;
                snowMask *= smoothstep(0.4, 0.7, displacedNormal.y);
                snowMask *= (1.0 - isWaterBiome);

                // Sample per-chunk season mask (if provided) to modulate snow/wet/dry per-tile.
                float2 seasonUV = uv * _TileSeasonMask_ST.xy + _TileSeasonMask_ST.zw;
                float4 seasonMaskSample = SAMPLE_TEXTURE2D(_TileSeasonMask, sampler_BiomeIndexMap, seasonUV);
                // seasonMaskSample.r = snow, .g = wet, .b = dry
                snowMask *= seasonMaskSample.r;

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
                // PER-BIOME INHERENT WETNESS
                // Driven by biomeParams.z (inherentWetness from BiomeVisualData).
                // Wet biomes (swamps, marshes) get darkened albedo + boosted smoothness.
                // No seasonal gating — this is the texture's natural look.
                // ==========================================================
                float inherentWet = biomeParams.z; // 0 = dry biome, 1 = fully wet
                if (inherentWet > 0.01)
                {
                    // Darken albedo (wet surfaces absorb more light)
                    albedo *= lerp(1.0, 1.0 - _WetAlbedoDarken, inherentWet);

                    // Boost smoothness (wet surfaces are glossier)
                    smoothness = lerp(smoothness, min(smoothness + _WetSmoothnessBoost, 0.99), inherentWet);

                    // Subtle normal perturbation for puddles/wetness feel
                    float2 wetUV = worldPos.xz * _WetNormalTiling;
                    float wetNx = sin(wetUV.x * 6.2831 + wetUV.y * 3.14) * 0.25
                                + sin(wetUV.x * 12.566 - wetUV.y * 7.85) * 0.125;
                    float wetNy = cos(wetUV.y * 6.2831 - wetUV.x * 2.71) * 0.25
                                + cos(wetUV.y * 15.707 + wetUV.x * 4.33) * 0.125;
                    float3 wetPerturb = normalize(float3(
                        wetNx * _WetNormalStrength * inherentWet,
                        1.0,
                        wetNy * _WetNormalStrength * inherentWet));

                    normalWS = normalize(lerp(normalWS, wetPerturb, inherentWet * 0.5));
                }

                // ==========================================================
                // FROZEN WATER BLEND
                // ==========================================================
                if (freezeAmount > 0.001 && (lakeMask > 0.001 || riverMask > 0.001))
                {
                    BiomeSample lakeIce = SampleIceSurface(
                        TEXTURE2D_ARRAY_ARGS(_LakeIceAlbedoArray, sampler_LakeIceAlbedoArray),
                        TEXTURE2D_ARRAY_ARGS(_LakeIceNormalArray, sampler_LakeIceNormalArray),
                        TEXTURE2D_ARRAY_ARGS(_LakeIceMaskArray, sampler_LakeIceMaskArray),
                        TEXTURE2D_ARRAY_ARGS(_LakeIceHeightArray, sampler_LakeIceHeightArray),
                        _LakeIceSliceCount,
                        _LakeIceTint,
                        _LakeIceTiling,
                        worldPos,
                        displacedNormal,
                        triWeights,
                        camDist,
                        uv);

                    BiomeSample riverIce = SampleIceSurface(
                        TEXTURE2D_ARRAY_ARGS(_RiverIceAlbedoArray, sampler_RiverIceAlbedoArray),
                        TEXTURE2D_ARRAY_ARGS(_RiverIceNormalArray, sampler_RiverIceNormalArray),
                        TEXTURE2D_ARRAY_ARGS(_RiverIceMaskArray, sampler_RiverIceMaskArray),
                        TEXTURE2D_ARRAY_ARGS(_RiverIceHeightArray, sampler_RiverIceHeightArray),
                        _RiverIceSliceCount,
                        _RiverIceTint,
                        _RiverIceTiling,
                        worldPos,
                        displacedNormal,
                        triWeights,
                        camDist,
                        uv);

                    float waterTypeWeight = max(lakeMask, riverMask);
                    float totalWater = max(lakeMask + riverMask, 1e-5);
                    float lakeWeight = lakeMask / totalWater;
                    float riverWeight = riverMask / totalWater;

                    float3 iceAlbedo = lakeIce.albedo * lakeWeight + riverIce.albedo * riverWeight;
                    float3 iceNormal = normalize(lakeIce.normalWS * lakeWeight + riverIce.normalWS * riverWeight);
                    float4 iceMask = lakeIce.mask * lakeWeight + riverIce.mask * riverWeight;
                    float iceHeight = lakeIce.height * lakeWeight + riverIce.height * riverWeight;

                    float solidIceBlend = saturate((freezeAmount - _FreezeOpaqueThreshold) / max(1.0 - _FreezeOpaqueThreshold, 0.001));
                    float finalFreezeBlend = saturate(max(freezeAmount, solidIceBlend) * waterTypeWeight);

                    albedo = lerp(albedo, iceAlbedo, finalFreezeBlend);
                    normalWS = normalize(lerp(normalWS, iceNormal, finalFreezeBlend));
                    metallic = lerp(metallic, saturate(iceMask.r), finalFreezeBlend);
                    ao = lerp(ao, saturate(iceMask.g), finalFreezeBlend);
                    smoothness = lerp(smoothness, saturate(iceMask.a), finalFreezeBlend);
                    blendedHeight = lerp(blendedHeight, iceHeight, finalFreezeBlend);
                }

                // ==========================================================
                // FOG OVERLAY
                // ==========================================================
                if (_EnableFog > 0.5)
                {
                    float4 fogSample = SAMPLE_TEXTURE2D(_FogMask, sampler_BiomeIndexMap, uv);
                    float fogAmount = fogSample.r * _TerrainFogColor.a;
                    albedo = lerp(albedo, _TerrainFogColor.rgb, fogAmount);
                    emission *= (1.0 - fogAmount);
                }

                // ==========================================================
                // OWNERSHIP OVERLAY
                // ==========================================================
                if (_EnableOwnership > 0.5)
                {
                    float4 ownerColor = SAMPLE_TEXTURE2D(_OwnershipOverlay, sampler_BiomeIndexMap, uv);
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

                // 1. Build SurfaceData (albedo, normal, smoothness, metallic, emission)
                SurfaceData surfaceData;
                ZERO_INITIALIZE(SurfaceData, surfaceData);
                surfaceData.baseColor = albedo;
                surfaceData.normalWS = normalWS;   // already in world space
                surfaceData.perceptualSmoothness = 1.0 - smoothness; // HDRP uses roughness = 1 - smoothness
                surfaceData.metallic = metallic;
                surfaceData.specularColor = 0;     // we let metallic drive it
                surfaceData.materialFeatures = 0;  // no special features
                surfaceData.diffusionProfileHash = 0; // not using subsurface scattering

                // 2. Build BuiltinData (required by HDRP)
                BuiltinData builtinData;
                ZERO_INITIALIZE(BuiltinData, builtinData);
                builtinData.opacity = 1.0;
                builtinData.emissiveColor = emission;

                // 3. Let HDRP compute lighting
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                PositionInputs posInput = GetPositionInput((uint2)input.positionCS.xy, _ScreenSize.zw, input.positionCS.z, input.positionCS.w, input.positionWS, input.normalWS);
                BSDFData bsdfData = ConvertSurfaceDataToBSDFData(input.positionCS.xy, surfaceData);
                PreLightData preLightData = GetPreLightData(viewDirWS, posInput, bsdfData);
                uint featureFlags = LIGHT_FEATURE_MASK_FLAGS_OPAQUE;
                LightLoopOutput lightLoopOutput;
                LightLoop(viewDirWS, posInput, preLightData, bsdfData, builtinData, featureFlags, lightLoopOutput);
                float3 finalColor = (lightLoopOutput.diffuseLighting + lightLoopOutput.specularLighting) * GetCurrentExposureMultiplier();

                // ==========================================================
                // HEX GRID OVERLAY (simple biome-edge detection)
                // Samples the biome index map and darkens/tints edges between
                // neighboring biome slices. Controlled by material properties.
                // ==========================================================
                if (_ShowHexGrid > 0.5)
                {
                    // center and 4-neighbors
                    float center = round(SAMPLE_TEXTURE2D_LOD(_BiomeIndexMap, sampler_BiomeIndexMap, uv, 0).r);
                    float2 bstep = _BiomeIndexMap_TexelSize.xy;
                    float r = round(SAMPLE_TEXTURE2D_LOD(_BiomeIndexMap, sampler_BiomeIndexMap, uv + float2(bstep.x, 0), 0).r);
                    float l = round(SAMPLE_TEXTURE2D_LOD(_BiomeIndexMap, sampler_BiomeIndexMap, uv - float2(bstep.x, 0), 0).r);
                    float u = round(SAMPLE_TEXTURE2D_LOD(_BiomeIndexMap, sampler_BiomeIndexMap, uv + float2(0, bstep.y), 0).r);
                    float d = round(SAMPLE_TEXTURE2D_LOD(_BiomeIndexMap, sampler_BiomeIndexMap, uv - float2(0, bstep.y), 0).r);

                    float edgeCount = 0.0;
                    edgeCount += (abs(center - r) > 0.5) ? 1.0 : 0.0;
                    edgeCount += (abs(center - l) > 0.5) ? 1.0 : 0.0;
                    edgeCount += (abs(center - u) > 0.5) ? 1.0 : 0.0;
                    edgeCount += (abs(center - d) > 0.5) ? 1.0 : 0.0;

                    // fade by camera distance
                    float fade = 1.0;
                    if (_HexGridFadeDistance > 0.01)
                        fade = saturate(1.0 - camDist / _HexGridFadeDistance);

                    float mask = saturate(edgeCount) * fade * _ShowHexGrid;

                    // apply color tint where edges found
                    finalColor = lerp(finalColor, _HexGridColor.rgb, mask * _HexGridColor.a);
                }

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

            #include "Packages/com.unity.render-pipelines.high-definition-config/Runtime/ShaderConfig.cs.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"

            // Distance-adaptive tessellation factor
            float CalcTessellationFactor(float3 positionWS)
            {
                // HDRP often uses camera-relative rendering for "world space" positions.
                // For world-anchored texturing / distances we must operate in absolute WS,
                // otherwise textures appear to "swim" as the camera moves.
                float3 absPosWS = GetAbsolutePositionWS(positionWS);
                float dist = distance(absPosWS, _WorldSpaceCameraPos);
                float f = 1.0 - saturate((dist - _TessellationFadeStart) /
                    max(_TessellationFadeEnd - _TessellationFadeStart, 0.01));
                return max(f * _TessellationFactor, 1.0);
            }

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

            #include "Packages/com.unity.render-pipelines.high-definition-config/Runtime/ShaderConfig.cs.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"

            // Distance-adaptive tessellation factor
            float CalcTessellationFactor(float3 positionWS)
            {
                // HDRP often uses camera-relative rendering for "world space" positions.
                // For world-anchored texturing / distances we must operate in absolute WS,
                // otherwise textures appear to "swim" as the camera moves.
                float3 absPosWS = GetAbsolutePositionWS(positionWS);
                float dist = distance(absPosWS, _WorldSpaceCameraPos);
                float f = 1.0 - saturate((dist - _TessellationFadeStart) /
                    max(_TessellationFadeEnd - _TessellationFadeStart, 0.01));
                return max(f * _TessellationFactor, 1.0);
            }

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

            #define SHADERPASS SHADERPASS_DEPTHNORMALS_ONLY
            #define SHADEROPTIONS_SHADOW_ALGORITHM SHADOW_ALGORITHM_CLASSIC
            #define PUNCTUAL_SHADOW_MEDIUM
            #define DIRECTIONAL_SHADOW_MEDIUM
            #define AREA_SHADOW_MEDIUM
            #include "Packages/com.unity.render-pipelines.high-definition-config/Runtime/ShaderConfig.cs.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoopDef.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"

            // Distance-adaptive tessellation factor
            float CalcTessellationFactor(float3 positionWS)
            {
                // HDRP often uses camera-relative rendering for "world space" positions.
                // For world-anchored texturing / distances we must operate in absolute WS,
                // otherwise textures appear to "swim" as the camera moves.
                float3 absPosWS = GetAbsolutePositionWS(positionWS);
                float dist = distance(absPosWS, _WorldSpaceCameraPos);
                float f = 1.0 - saturate((dist - _TessellationFadeStart) /
                    max(_TessellationFadeEnd - _TessellationFadeStart, 0.01));
                return max(f * _TessellationFactor, 1.0);
            }

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
                float3 positionWS : TEXCOORD2;
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
                float3 worldPos = TransformObjectToWorld(posOS);
                o.positionCS = TransformWorldToHClip(worldPos);
                o.normalWS = TransformObjectToWorldNormal(normalOS);
                o.positionWS = worldPos;
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
                float3 worldPos = TransformObjectToWorld(posOS);
                o.positionCS = TransformWorldToHClip(worldPos);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.positionWS = worldPos;
                o.uv = input.uv;
                return o;
            }

            #endif

            // Write combined displaced + biome normal to HDRP normal buffer for screen-space effects
            float4 frag(Varyings input) : SV_Target0
            {
                float2 uv = input.uv;
                float3 worldPos = GetAbsolutePositionWS(input.positionWS);
                float camDist = distance(_WorldSpaceCameraPos, worldPos);

                // Heightmap-derived displaced normal (macro terrain shape)
                float3 displacedNormal = ComputeDisplacedNormal(uv);
                float3 triWeights = TriplanarWeights(displacedNormal);

                // Look up biome slice and index from the biome index map
                float4 centerSample = SAMPLE_TEXTURE2D_LOD(_BiomeIndexMap, sampler_BiomeIndexMap, uv, 0);
                float centerSlice = round(centerSample.r);
                int centerBiome = (int)(centerSample.g + 0.5);
                centerBiome = clamp(centerBiome, 0, 63);

                // Per-biome tiling
                float4 biomeParams = _BiomeParams[centerBiome];
                float biomeTiling = max(biomeParams.x, 0.01);
                float effectiveTiling = _TriTiling * biomeTiling;

                // Sample biome normal (same path as ForwardOnly)
                float3 biomeNormal = SampleBiomeNormal(
                    TEXTURE2D_ARRAY_ARGS(_BiomeNormalArray, sampler_BiomeNormalArray),
                    worldPos, displacedNormal, triWeights, centerSlice, effectiveTiling, camDist, uv);

                // Blend displaced (macro) normal with biome (micro) normal.
                // Use biome normal as primary, falling back to displaced normal where
                // biome normal strength is zero.
                float3 finalNormal = normalize(biomeNormal);

                // Unpack smoothness from mask for perceptual roughness
                float4 maskSample = SampleBiomeTexture(
                    TEXTURE2D_ARRAY_ARGS(_BiomeMaskArray, sampler_BiomeMaskArray),
                    worldPos, triWeights, centerSlice, effectiveTiling, camDist, uv);
                float smoothness = saturate(maskSample.a * _SmoothnessMultiplier);
                float perceptualRoughness = 1.0 - smoothness;

                // Encode as octahedron for HDRP normal buffer
                float2 encodedNormal = PackNormalOctQuadEncode(finalNormal);

                // HDRP normal buffer: xy = encoded normal, z = perceptual roughness, w = flags
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

            #include "Packages/com.unity.render-pipelines.high-definition-config/Runtime/ShaderConfig.cs.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"

            // Distance-adaptive tessellation factor
            float CalcTessellationFactor(float3 positionWS)
            {
                // HDRP often uses camera-relative rendering for "world space" positions.
                // For world-anchored texturing / distances we must operate in absolute WS,
                // otherwise textures appear to "swim" as the camera moves.
                float3 absPosWS = GetAbsolutePositionWS(positionWS);
                float dist = distance(absPosWS, _WorldSpaceCameraPos);
                float f = 1.0 - saturate((dist - _TessellationFadeStart) /
                    max(_TessellationFadeEnd - _TessellationFadeStart, 0.01));
                return max(f * _TessellationFactor, 1.0);
            }

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
