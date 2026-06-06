using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

[CreateAssetMenu(menuName = "Terrain/Biome Visual Database")]
public class BiomeVisualDatabase : ScriptableObject
{
    public List<BiomeVisualData> biomes = new List<BiomeVisualData>();

    private Dictionary<Biome, BiomeVisualData> lookup;

    // --------------------------- Surface Library Cache (Editor/Runtime) ---------------------------
    // Building Texture2DArrays is extremely memory-heavy. In multi-planet mode, many systems may call
    // BuildSurfaceLibrary() repeatedly (and across play sessions if domain reload is disabled).
    // Cache the built library so we only allocate once per database signature, and provide explicit cleanup.
    private struct CachedSurfaceLibrary
    {
        public string signature;
        public SurfaceLibrary library;
    }

    private static readonly Dictionary<int, CachedSurfaceLibrary> _surfaceLibraryCacheByDb = new Dictionary<int, CachedSurfaceLibrary>();

    public static void ClearAllCachedSurfaceLibraries()
    {
        foreach (var kvp in _surfaceLibraryCacheByDb)
        {
            ReleaseSurfaceLibrary(kvp.Value.library);
        }
        _surfaceLibraryCacheByDb.Clear();
    }

    private static void ReleaseSurfaceLibrary(SurfaceLibrary lib)
    {
        if (lib == null) return;
        DestroyUnityObject(lib.albedoArray);
        DestroyUnityObject(lib.normalArray);
        DestroyUnityObject(lib.maskArray);
        DestroyUnityObject(lib.heightArray);
        DestroyUnityObject(lib.emissiveArray);
        lib.albedoArray = null;
        lib.normalArray = null;
        lib.maskArray = null;
        lib.heightArray = null;
        lib.emissiveArray = null;
    }

    private static void DestroyUnityObject(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj, allowDestroyingAssets: false);
    }

    // Utility used during profiling/diagnostics.
    // Kept as a regular method (not a local function) to avoid CS8321 warnings when diagnostics are disabled.
    private static string FormatBytes(long bytes)
    {
        const double KB = 1024.0;
        const double MB = 1024.0 * 1024.0;
        const double GB = 1024.0 * 1024.0 * 1024.0;
        if (bytes >= GB) return $"{bytes / GB:0.00} GB";
        if (bytes >= MB) return $"{bytes / MB:0.0} MB";
        if (bytes >= KB) return $"{bytes / KB:0.0} KB";
        return $"{bytes} B";
    }

    // Surface library returned by BuildSurfaceLibrary()
    public class SurfaceLibrary
    {
        public Texture2DArray albedoArray;
        public Texture2DArray normalArray;
        public Texture2DArray maskArray;
        public Texture2DArray heightArray;
        public Texture2DArray emissiveArray;

        // per-surface
        public int[] surfaceStartSlice;
        public int[] surfaceVariantCounts;
        public int[] surfaceMountainStartSlice;
        public int[] surfaceMountainVariantCounts;

        // per-biome mapping
        public int[] biomeToSurfaceIndex;
        public int[] biomeForcedVariant; // -1 if none

        public int totalSlices;
    }

    public BiomeVisualData Get(Biome biome)
    {
        if (lookup == null || lookup.Count != biomes.Count)
        {
            BuildLookup();
        }

        if (lookup != null && lookup.TryGetValue(biome, out var data))
        {
            return data;
        }

        return null;
    }

    private void BuildLookup()
    {
        lookup = new Dictionary<Biome, BiomeVisualData>();
        foreach (var entry in biomes)
        {
            if (entry == null) continue;
            lookup[entry.biome] = entry;
        }
    }

    /// <summary>
    /// Build a flattened surface library from referenced SurfaceFamilyData.
    /// Returns null on failure.
    /// </summary>
    /// <param name="overrideWidth">If &gt; 0, force texture array width (e.g. 2048). Otherwise inferred from first source.</param>
    /// <param name="overrideHeight">If &gt; 0, force texture array height (e.g. 2048). Otherwise inferred from first source.</param>
    public SurfaceLibrary BuildSurfaceLibrary(int overrideWidth = 0, int overrideHeight = 0)
    {
        if (biomes == null) return null;

        // Build flattened arrays via Graphics.CopyTexture from SurfaceFamilyData Texture2DArrays.\n        // This preserves compression (e.g., BC7) and prevents accidental RGBA32 blowups.

        // Discover families in encounter order (SurfaceFamilyData only)
        var families = new List<SurfaceFamilyData>();
        var biomeToSurface = new int[biomes.Count];
        var biomeForced = new int[biomes.Count];

        for (int i = 0; i < biomes.Count; i++)
        {
            var b = biomes[i];
            biomeForced[i] = (b != null) ? b.forcedVariant : -1;

            if (b == null)
            {
                biomeToSurface[i] = -1;
                continue;
            }

            if (b.surfaceFamily == null)
            {
                biomeToSurface[i] = -1;
                continue;
            }

            int idx = families.IndexOf(b.surfaceFamily);
            if (idx < 0)
            {
                idx = families.Count;
                families.Add(b.surfaceFamily);
            }
            biomeToSurface[i] = idx;
        }

        if (families.Count == 0)
        {
            Debug.LogError("[BiomeVisualDatabase] BuildSurfaceLibrary FAILED (strict): No SurfaceFamilyData assigned on any biome entry.");
            return null;
        }

        // Determine target size
        int targetW = 0, targetH = 0;
        if (overrideWidth > 0 && overrideHeight > 0)
        {
            targetW = overrideWidth;
            targetH = overrideHeight;
        }
        else
        {
            var first = families[0];
            if (first != null && first.albedoArray != null)
            {
                targetW = first.albedoArray.width;
                targetH = first.albedoArray.height;
            }
        }

        if (targetW <= 0 || targetH <= 0)
        {
            Debug.LogError("[BiomeVisualDatabase] BuildSurfaceLibrary FAILED (strict): Could not infer a valid target size. Provide overrideWidth/overrideHeight and ensure surface families have albedo arrays.");
            return null;
        }

        // Determine expected formats + mip counts from the first family (must match across all families)
        var firstFamily = families[0];
        if (firstFamily == null)
        {
            Debug.LogError("[BiomeVisualDatabase] BuildSurfaceLibrary FAILED (strict): First surface family is null.");
            return null;
        }
        if (firstFamily.albedoArray == null || firstFamily.normalArray == null || firstFamily.maskArray == null)
        {
            Debug.LogError($"[BiomeVisualDatabase] BuildSurfaceLibrary FAILED (strict): SurfaceFamily '{firstFamily.name}' is missing required arrays (albedo/normal/mask).");
            return null;
        }

        TextureFormat albedoFmt = firstFamily.albedoArray.format;
        TextureFormat normalFmt = firstFamily.normalArray.format;
        TextureFormat maskFmt = firstFamily.maskArray.format;
        int albedoMipCount = firstFamily.albedoArray.mipmapCount;
        int normalMipCount = firstFamily.normalArray.mipmapCount;
        int maskMipCount = firstFamily.maskArray.mipmapCount;

        bool includeEmissive = false;
        TextureFormat emissiveFmt = TextureFormat.RGBAHalf;
        int emissiveMipCount = 0;
        if (firstFamily.emissiveArray != null)
        {
            includeEmissive = true;
            emissiveFmt = firstFamily.emissiveArray.format;
            emissiveMipCount = firstFamily.emissiveArray.mipmapCount;
        }
        bool includeHeight = false;
        TextureFormat heightFmt = TextureFormat.RHalf;
        int heightMipCount = 0;
        if (firstFamily.heightArray != null)
        {
            includeHeight = true;
            heightFmt = firstFamily.heightArray.format;
            heightMipCount = firstFamily.heightArray.mipmapCount;
        }

        // If any other family has emissive, include it and enforce consistency.
        foreach (var f in families)
        {
            if (f != null && f.emissiveArray != null) { includeEmissive = true; break; }
        }
        foreach (var f in families)
        {
            if (f != null && f.heightArray != null) { includeHeight = true; break; }
        }

        // Calculate total slices + validate families
        var variantCounts = new int[families.Count];
        var mountainVariantCounts = new int[families.Count];
        int total = 0;
        var errors = new List<string>(16);

        for (int i = 0; i < families.Count; i++)
        {
            var sf = families[i];
            if (sf == null)
            {
                errors.Add($"Family[{i}] is null.");
                variantCounts[i] = 0;
                continue;
            }

            int variants = Mathf.Max(1, sf.VariantCount);
            variantCounts[i] = variants;
            total += variants;

            int mountainVariants = 0;
            if (sf.HasMountainVariants)
            {
                mountainVariants = Mathf.Max(1, sf.MountainVariantCount);
                total += mountainVariants;
            }
            mountainVariantCounts[i] = mountainVariants;

            void CheckArray(string kind, Texture2DArray arr, TextureFormat expectedFmt, int expectedMipCount)
            {
                if (arr == null)
                {
                    errors.Add($"SurfaceFamily '{sf.name}' missing {kind} Texture2DArray.");
                    return;
                }
                if (arr.width != targetW || arr.height != targetH)
                {
                    errors.Add($"SurfaceFamily '{sf.name}' {kind} size is {arr.width}x{arr.height}, expected {targetW}x{targetH}.");
                }
                if (arr.format != expectedFmt)
                {
                    errors.Add($"SurfaceFamily '{sf.name}' {kind} format is {arr.format}, expected {expectedFmt}.");
                }
                if (arr.mipmapCount != expectedMipCount)
                {
                    errors.Add($"SurfaceFamily '{sf.name}' {kind} mipmapCount is {arr.mipmapCount}, expected {expectedMipCount}.");
                }
                if (arr.depth < variants)
                {
                    errors.Add($"SurfaceFamily '{sf.name}' {kind} depth is {arr.depth}, but VariantCount is {variants}.");
                }
            }

            CheckArray("albedo", sf.albedoArray, albedoFmt, albedoMipCount);
            CheckArray("normal", sf.normalArray, normalFmt, normalMipCount);
            CheckArray("mask", sf.maskArray, maskFmt, maskMipCount);
            if (includeHeight) CheckArray("height", sf.heightArray, heightFmt, heightMipCount);

            if (sf.HasMountainVariants)
            {
                void CheckMountainArray(string kind, Texture2DArray arr, TextureFormat expectedFmt, int expectedMipCount)
                {
                    if (arr == null)
                    {
                        errors.Add($"SurfaceFamily '{sf.name}' has mountain overrides but is missing mountain {kind} Texture2DArray.");
                        return;
                    }
                    if (arr.width != targetW || arr.height != targetH)
                    {
                        errors.Add($"SurfaceFamily '{sf.name}' mountain {kind} size is {arr.width}x{arr.height}, expected {targetW}x{targetH}.");
                    }
                    if (arr.format != expectedFmt)
                    {
                        errors.Add($"SurfaceFamily '{sf.name}' mountain {kind} format is {arr.format}, expected {expectedFmt}.");
                    }
                    if (arr.mipmapCount != expectedMipCount)
                    {
                        errors.Add($"SurfaceFamily '{sf.name}' mountain {kind} mipmapCount is {arr.mipmapCount}, expected {expectedMipCount}.");
                    }
                    if (arr.depth < mountainVariants)
                    {
                        errors.Add($"SurfaceFamily '{sf.name}' mountain {kind} depth is {arr.depth}, but MountainVariantCount is {mountainVariants}.");
                    }
                }

                CheckMountainArray("albedo", sf.mountainAlbedoArray, albedoFmt, albedoMipCount);
                CheckMountainArray("normal", sf.mountainNormalArray, normalFmt, normalMipCount);
                CheckMountainArray("mask", sf.mountainMaskArray, maskFmt, maskMipCount);

                if (sf.mountainHeightArray != null)
                    CheckMountainArray("height", sf.mountainHeightArray, heightFmt, heightMipCount);

                if (sf.mountainEmissiveArray != null)
                    CheckMountainArray("emissive", sf.mountainEmissiveArray, emissiveFmt, emissiveMipCount);
            }

            if (includeEmissive)
            {
                if (sf.emissiveArray == null)
                {
                    errors.Add($"SurfaceFamily '{sf.name}' missing emissive Texture2DArray (strict: emissive is enabled because at least one family provides it).");
                }
                else
                {
                    if (emissiveMipCount == 0)
                    {
                        emissiveFmt = sf.emissiveArray.format;
                        emissiveMipCount = sf.emissiveArray.mipmapCount;
                    }
                    CheckArray("emissive", sf.emissiveArray, emissiveFmt, emissiveMipCount);
                }
            }
        }

        if (total <= 0)
        {
            Debug.LogError("[BiomeVisualDatabase] BuildSurfaceLibrary FAILED (strict): No slices to build.");
            return null;
        }

        if (errors.Count > 0)
        {
            Debug.LogError($"[BiomeVisualDatabase] BuildSurfaceLibrary FAILED (strict): Surface families are not consistent. Fix the Texture2DArrays so they all share the same size/format/mips.\n- {string.Join("\n- ", errors)}");
            return null;
        }

        // Cache lookup: include every assignment/input that can affect flattened slice layout.
        // This prevents stale Texture2DArrays after changing biome-to-family assignments, source assets,
        // forced variants, mountain overrides, or texture dimensions.
        int dbId = this.GetRuntimeId();
        var signatureBuilder = new System.Text.StringBuilder(1024);
        signatureBuilder.Append(name)
            .Append("|dbId=").Append(dbId)
            .Append("|biomes=").Append(biomes.Count)
            .Append("|families=").Append(families.Count)
            .Append("|size=").Append(targetW).Append('x').Append(targetH)
            .Append("|totalSlices=").Append(total)
            .Append("|A=").Append(albedoFmt).Append('/').Append(albedoMipCount)
            .Append("|N=").Append(normalFmt).Append('/').Append(normalMipCount)
            .Append("|M=").Append(maskFmt).Append('/').Append(maskMipCount)
            .Append("|H=").Append(includeHeight ? $"{heightFmt}/{heightMipCount}" : "none")
            .Append("|E=").Append(includeEmissive ? $"{emissiveFmt}/{emissiveMipCount}" : "none");

        for (int i = 0; i < biomes.Count; i++)
        {
            var visual = biomes[i];
            var family = visual != null ? visual.surfaceFamily : null;
            signatureBuilder.Append("|B").Append(i)
                .Append(":visual=").Append(visual != null ? visual.GetRuntimeId() : 0)
                .Append(":biome=").Append(visual != null ? visual.biome.ToString() : "null")
                .Append(":family=").Append(family != null ? family.GetRuntimeId() : 0)
                .Append(":forced=").Append(visual != null ? visual.forcedVariant : -1);
        }

        for (int i = 0; i < families.Count; i++)
        {
            var sf = families[i];
            signatureBuilder.Append("|F").Append(i)
                .Append(":family=").Append(sf != null ? sf.GetRuntimeId() : 0)
                .Append(":variants=").Append(i < variantCounts.Length ? variantCounts[i] : 0)
                .Append(":mountainVariants=").Append(i < mountainVariantCounts.Length ? mountainVariantCounts[i] : 0);

            if (sf != null)
            {
                signatureBuilder
                    .Append(":albedo=").Append(sf.albedoArray != null ? sf.albedoArray.GetRuntimeId() : 0)
                    .Append(":normal=").Append(sf.normalArray != null ? sf.normalArray.GetRuntimeId() : 0)
                    .Append(":mask=").Append(sf.maskArray != null ? sf.maskArray.GetRuntimeId() : 0)
                    .Append(":height=").Append(sf.heightArray != null ? sf.heightArray.GetRuntimeId() : 0)
                    .Append(":emissive=").Append(sf.emissiveArray != null ? sf.emissiveArray.GetRuntimeId() : 0)
                    .Append(":mountainAlbedo=").Append(sf.mountainAlbedoArray != null ? sf.mountainAlbedoArray.GetRuntimeId() : 0)
                    .Append(":mountainNormal=").Append(sf.mountainNormalArray != null ? sf.mountainNormalArray.GetRuntimeId() : 0)
                    .Append(":mountainMask=").Append(sf.mountainMaskArray != null ? sf.mountainMaskArray.GetRuntimeId() : 0)
                    .Append(":mountainHeight=").Append(sf.mountainHeightArray != null ? sf.mountainHeightArray.GetRuntimeId() : 0)
                    .Append(":mountainEmissive=").Append(sf.mountainEmissiveArray != null ? sf.mountainEmissiveArray.GetRuntimeId() : 0);
            }
        }

        string signature = signatureBuilder.ToString();

        if (_surfaceLibraryCacheByDb.TryGetValue(dbId, out var cached) &&
            cached.library != null &&
            cached.signature == signature &&
            cached.library.albedoArray != null &&
            cached.library.normalArray != null &&
            cached.library.maskArray != null &&
            (!includeEmissive || cached.library.emissiveArray != null))
        {
            return cached.library;
        }

        if (cached.library != null)
        {
            ReleaseSurfaceLibrary(cached.library);
        }

        // Allocate destination arrays in the SAME formats as the source arrays (preserves compression like BC7).
        bool hasMipsA = albedoMipCount > 1;
        bool hasMipsN = normalMipCount > 1;
        bool hasMipsM = maskMipCount > 1;
        bool hasMipsE = includeEmissive && emissiveMipCount > 1;
        bool hasMipsH = includeHeight && heightMipCount > 1;

        var albedoArray = new Texture2DArray(targetW, targetH, total, albedoFmt, hasMipsA, linear: false);
        var normalArray = new Texture2DArray(targetW, targetH, total, normalFmt, hasMipsN, linear: true);
        var maskArray = new Texture2DArray(targetW, targetH, total, maskFmt, hasMipsM, linear: true);
        Texture2DArray emissiveArray = null;
        Texture2DArray heightArray = null;
        if (includeEmissive)
        {
            // Emissive can be compressed or half; we preserve whatever format is in the source family assets.
            emissiveArray = new Texture2DArray(targetW, targetH, total, emissiveFmt, hasMipsE, linear: true);
        }
        if (includeHeight)
        {
            heightArray = new Texture2DArray(targetW, targetH, total, heightFmt, hasMipsH, linear: true);
        }

        albedoArray.wrapMode = TextureWrapMode.Repeat;
        normalArray.wrapMode = TextureWrapMode.Repeat;
        maskArray.wrapMode = TextureWrapMode.Repeat;
        if (emissiveArray != null) emissiveArray.wrapMode = TextureWrapMode.Repeat;
        if (heightArray != null) heightArray.wrapMode = TextureWrapMode.Repeat;

        int writeSlice = 0;
        var surfaceStart = new int[families.Count];
        var surfaceMountainStart = new int[families.Count];

        for (int s = 0; s < families.Count; s++)
        {
            surfaceStart[s] = writeSlice;
            var sf = families[s];
            int variants = variantCounts[s];

            for (int v = 0; v < variants; v++)
            {
                // Copy mip chain explicitly so compressed arrays keep correct mips.
                for (int mip = 0; mip < albedoMipCount; mip++)
                    Graphics.CopyTexture(sf.albedoArray, v, mip, albedoArray, writeSlice, mip);
                for (int mip = 0; mip < normalMipCount; mip++)
                    Graphics.CopyTexture(sf.normalArray, v, mip, normalArray, writeSlice, mip);
                for (int mip = 0; mip < maskMipCount; mip++)
                    Graphics.CopyTexture(sf.maskArray, v, mip, maskArray, writeSlice, mip);
                if (includeEmissive && emissiveArray != null)
                {
                    for (int mip = 0; mip < emissiveMipCount; mip++)
                        Graphics.CopyTexture(sf.emissiveArray, v, mip, emissiveArray, writeSlice, mip);
                }
                if (includeHeight && heightArray != null)
                {
                    for (int mip = 0; mip < heightMipCount; mip++)
                        Graphics.CopyTexture(sf.heightArray, v, mip, heightArray, writeSlice, mip);
                }
                writeSlice++;
            }

            surfaceMountainStart[s] = writeSlice;
            int mountainVariants = mountainVariantCounts[s];
            if (mountainVariants <= 0)
                continue;

            for (int v = 0; v < mountainVariants; v++)
            {
                for (int mip = 0; mip < albedoMipCount; mip++)
                    Graphics.CopyTexture(sf.mountainAlbedoArray, v, mip, albedoArray, writeSlice, mip);
                for (int mip = 0; mip < normalMipCount; mip++)
                    Graphics.CopyTexture(sf.mountainNormalArray, v, mip, normalArray, writeSlice, mip);
                for (int mip = 0; mip < maskMipCount; mip++)
                    Graphics.CopyTexture(sf.mountainMaskArray, v, mip, maskArray, writeSlice, mip);
                if (includeEmissive && emissiveArray != null)
                {
                    Texture2DArray emissiveSource = sf.mountainEmissiveArray != null ? sf.mountainEmissiveArray : sf.emissiveArray;
                    int emissiveSlice = emissiveSource != null && emissiveSource.depth > 0 ? (v % emissiveSource.depth) : 0;
                    for (int mip = 0; mip < emissiveMipCount; mip++)
                        Graphics.CopyTexture(emissiveSource, emissiveSlice, mip, emissiveArray, writeSlice, mip);
                }
                if (includeHeight && heightArray != null)
                {
                    Texture2DArray heightSource = sf.mountainHeightArray != null ? sf.mountainHeightArray : sf.heightArray;
                    int heightSlice = heightSource != null && heightSource.depth > 0 ? (v % heightSource.depth) : 0;
                    for (int mip = 0; mip < heightMipCount; mip++)
                        Graphics.CopyTexture(heightSource, heightSlice, mip, heightArray, writeSlice, mip);
                }
                writeSlice++;
            }
        }

        var lib = new SurfaceLibrary
        {
            albedoArray = albedoArray,
            normalArray = normalArray,
            maskArray = maskArray,
            heightArray = heightArray,
            emissiveArray = emissiveArray,
            totalSlices = writeSlice,
            surfaceStartSlice = surfaceStart,
            surfaceVariantCounts = variantCounts,
            surfaceMountainStartSlice = surfaceMountainStart,
            surfaceMountainVariantCounts = mountainVariantCounts,
            biomeToSurfaceIndex = biomeToSurface,
            biomeForcedVariant = biomeForced
        };

        _surfaceLibraryCacheByDb[dbId] = new CachedSurfaceLibrary { signature = signature, library = lib };

        // Unload source SurfaceFamilyData Texture2DArrays from GPU memory.
        // After Graphics.CopyTexture, the flattened output arrays contain all the data the shader needs.
        // The source arrays are Unity asset references that stay loaded by default, doubling VRAM usage.
        UnloadSourceFamilyArrays(families);

        return lib;
    }

    /// <summary>
    /// After BuildSurfaceLibrary copies slices into flattened arrays, the per-family source
    /// Texture2DArrays are no longer needed at runtime. Unload them to reclaim VRAM.
    /// Uses Resources.UnloadAsset which releases the GPU copy while keeping the asset reference
    /// valid (Unity will reload from disk if accessed again, e.g. in a subsequent BuildSurfaceLibrary call).
    /// </summary>
    private void UnloadSourceFamilyArrays(List<SurfaceFamilyData> families)
    {
        int unloaded = 0;
        foreach (var sf in families)
        {
            if (sf == null) continue;
            if (sf.albedoArray != null) { Resources.UnloadAsset(sf.albedoArray); unloaded++; }
            if (sf.normalArray != null) { Resources.UnloadAsset(sf.normalArray); unloaded++; }
            if (sf.maskArray != null) { Resources.UnloadAsset(sf.maskArray); unloaded++; }
            if (sf.heightArray != null) { Resources.UnloadAsset(sf.heightArray); unloaded++; }
            if (sf.emissiveArray != null) { Resources.UnloadAsset(sf.emissiveArray); unloaded++; }
            if (sf.mountainAlbedoArray != null) { Resources.UnloadAsset(sf.mountainAlbedoArray); unloaded++; }
            if (sf.mountainNormalArray != null) { Resources.UnloadAsset(sf.mountainNormalArray); unloaded++; }
            if (sf.mountainMaskArray != null) { Resources.UnloadAsset(sf.mountainMaskArray); unloaded++; }
            if (sf.mountainHeightArray != null) { Resources.UnloadAsset(sf.mountainHeightArray); unloaded++; }
            if (sf.mountainEmissiveArray != null) { Resources.UnloadAsset(sf.mountainEmissiveArray); unloaded++; }
        }
    }
}
