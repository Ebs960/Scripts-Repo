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
        DestroyUnityObject(lib.emissiveArray);
        lib.albedoArray = null;
        lib.normalArray = null;
        lib.maskArray = null;
        lib.emissiveArray = null;
    }

    private static void DestroyUnityObject(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj, allowDestroyingAssets: false);
    }

    // Surface library returned by BuildSurfaceLibrary()
    public class SurfaceLibrary
    {
        public Texture2DArray albedoArray;
        public Texture2DArray normalArray;
        public Texture2DArray maskArray;
        public Texture2DArray emissiveArray;

        // per-surface
        public int[] surfaceStartSlice;
        public int[] surfaceVariantCounts;

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
    /// Build a flattened surface library from referenced SurfaceFamilyData and legacy per-biome textures.
    /// Returns null on failure.
    /// </summary>
    /// <param name="overrideWidth">If &gt; 0, force texture array width (e.g. 2048). Otherwise inferred from first source.</param>
    /// <param name="overrideHeight">If &gt; 0, force texture array height (e.g. 2048). Otherwise inferred from first source.</param>
    public SurfaceLibrary BuildSurfaceLibrary(int overrideWidth = 0, int overrideHeight = 0)
    {
        if (biomes == null) return null;

        static long EstimateTextureArrayBytes(int w, int h, int depth, int bytesPerPixel, bool hasMipmaps)
        {
            // Full mip chain multiplier is ~4/3 for large POT textures. Good enough for diagnostics.
            double mipMul = hasMipmaps ? (4.0 / 3.0) : 1.0;
            return (long)System.Math.Round((double)w * h * depth * bytesPerPixel * mipMul);
        }

        static string FormatBytes(long bytes)
        {
            const double KB = 1024.0;
            const double MB = 1024.0 * 1024.0;
            const double GB = 1024.0 * 1024.0 * 1024.0;
            if (bytes >= GB) return $"{bytes / GB:0.00} GB";
            if (bytes >= MB) return $"{bytes / MB:0.0} MB";
            if (bytes >= KB) return $"{bytes / KB:0.0} KB";
            return $"{bytes} B";
        }

        // Discover families in encounter order
        var familyEntries = new List<object>(); // either SurfaceFamilyData or BiomeVisualData (legacy)
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

            if (b.surfaceFamily != null)
            {
                int idx = familyEntries.IndexOf(b.surfaceFamily);
                if (idx < 0)
                {
                    idx = familyEntries.Count;
                    familyEntries.Add(b.surfaceFamily);
                }
                biomeToSurface[i] = idx;
            }
            else
            {
                // legacy per-biome textures: treat each as its own family
                // only add if at least one texture exists
                if (b.albedo != null || b.normal != null || b.maskMap != null)
                {
                    int idx = familyEntries.Count;
                    familyEntries.Add(b); // marker for ad-hoc family
                    biomeToSurface[i] = idx;
                }
                else
                {
                    biomeToSurface[i] = -1;
                }
            }
        }

        // Determine target slice size: use override when specified, otherwise infer from first source
        int targetW = 0, targetH = 0;
        if (overrideWidth > 0 && overrideHeight > 0)
        {
            targetW = overrideWidth;
            targetH = overrideHeight;
        }
        else
        {
            foreach (var entry in familyEntries)
            {
                if (entry is SurfaceFamilyData sf)
                {
                    if (sf.albedoArray != null)
                    {
                        targetW = sf.albedoArray.width;
                        targetH = sf.albedoArray.height;
                        break;
                    }
                }
                else if (entry is BiomeVisualData bv)
                {
                    if (bv.albedo != null)
                    {
                        targetW = bv.albedo.width;
                        targetH = bv.albedo.height;
                        break;
                    }
                }
            }
        }

        if (targetW == 0 || targetH == 0)
        {
            Debug.LogWarning("[BiomeVisualDatabase] No valid textures found to build surface library.");
            return null;
        }

        // Calculate total slices
        var variantCounts = new List<int>();
        foreach (var entry in familyEntries)
        {
            if (entry is SurfaceFamilyData sf)
            {
                int v = sf.VariantCount;
                if (v <= 0) v = 1;
                variantCounts.Add(v);
            }
            else // BiomeVisualData ad-hoc
            {
                variantCounts.Add(1);
            }
        }

        int total = 0;
        foreach (var v in variantCounts) total += v;

        if (total == 0)
        {
            Debug.LogWarning("[BiomeVisualDatabase] No variants found in surface families.");
            return null;
        }

        // Diagnostic: rough memory estimate for the flattened arrays we are about to allocate.
        // This is usually the single largest GPU memory consumer at map start.
        try
        {
            // RGBA32 = 4 bytes/px. RGBAHalf = 8 bytes/px.
            long albedoBytes = EstimateTextureArrayBytes(targetW, targetH, total, 4, hasMipmaps: true);
            long normalBytes = EstimateTextureArrayBytes(targetW, targetH, total, 4, hasMipmaps: true);
            long maskBytes = EstimateTextureArrayBytes(targetW, targetH, total, 4, hasMipmaps: true);
            long emissiveBytes = EstimateTextureArrayBytes(targetW, targetH, total, 8, hasMipmaps: true);
            long totalBytes = albedoBytes + normalBytes + maskBytes + emissiveBytes;
            Debug.Log($"[BiomeVisualDatabase] BuildSurfaceLibrary request: db='{name}' size={targetW}x{targetH} totalSlices={total} (families={familyEntries.Count}). " +
                      $"Estimated GPU tex memory (arrays only): albedo={FormatBytes(albedoBytes)}, normal={FormatBytes(normalBytes)}, mask={FormatBytes(maskBytes)}, emissive={FormatBytes(emissiveBytes)}, TOTAL={FormatBytes(totalBytes)}");
        }
        catch { /* diagnostics only */ }

        // Cache lookup: build signature from the database + discovered family order + target size + slice count.
        // This prevents repeated Texture2DArray allocations (a common cause of 2nd/3rd Play OOM in the editor).
        int dbId = GetInstanceID();
        string signature = $"{name}|biomes={biomes.Count}|families={familyEntries.Count}|size={targetW}x{targetH}|totalSlices={total}";
        if (_surfaceLibraryCacheByDb.TryGetValue(dbId, out var cached) &&
            cached.library != null &&
            cached.signature == signature &&
            cached.library.albedoArray != null &&
            cached.library.normalArray != null &&
            cached.library.maskArray != null &&
            cached.library.emissiveArray != null)
        {
            return cached.library;
        }
        // If signature changed or cache is invalid, release any previous cached arrays to avoid leaks.
        if (cached.library != null)
        {
            ReleaseSurfaceLibrary(cached.library);
        }

        // Create destination flattened arrays
        var albedoArray = new Texture2DArray(targetW, targetH, total, TextureFormat.RGBA32, true, false);
        var normalArray = new Texture2DArray(targetW, targetH, total, TextureFormat.RGBA32, true, true);
        var maskArray = new Texture2DArray(targetW, targetH, total, TextureFormat.RGBA32, true, true);

        var emissiveArray = new Texture2DArray(targetW, targetH, total, TextureFormat.RGBAHalf, true, true);

        albedoArray.wrapMode = TextureWrapMode.Repeat;
        normalArray.wrapMode = TextureWrapMode.Repeat;
        maskArray.wrapMode = TextureWrapMode.Repeat;

        // Avoid huge managed allocations (Color[]) and CPU readbacks during slice scaling/fallback fills.
        // We use GPU RenderTextures as intermediate targets and CopyTexture into the Texture2DArray slices.
        RenderTexture rtSrgb = null;
        RenderTexture rtLinear = null;
        RenderTexture rtHalf = null;
        var prevActive = RenderTexture.active;

        // Reusable temporary 2D textures for pulling a slice out of a Texture2DArray when scaling is needed.
        // Key: (w,h,format,linearFlag).
        var tmp2DCache = new Dictionary<(int w, int h, TextureFormat fmt, bool linear), Texture2D>(8);

        Texture2D GetOrCreateTmp2D(int w, int h, TextureFormat fmt, bool linear)
        {
            var key = (w, h, fmt, linear);
            if (tmp2DCache.TryGetValue(key, out var existing) && existing != null) return existing;
            var t = new Texture2D(w, h, fmt, mipChain: false, linear: linear)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                name = $"_TmpSlice_{w}x{h}_{fmt}_{(linear ? "Lin" : "sRGB")}"
            };
            tmp2DCache[key] = t;
            return t;
        }

        RenderTexture GetOrCreateRT(ref RenderTexture rt, RenderTextureFormat fmt, RenderTextureReadWrite rw)
        {
            if (rt != null && rt.width == targetW && rt.height == targetH) return rt;
            if (rt != null) RenderTexture.ReleaseTemporary(rt);
            rt = RenderTexture.GetTemporary(targetW, targetH, 0, fmt, rw);
            rt.wrapMode = TextureWrapMode.Repeat;
            rt.filterMode = FilterMode.Bilinear;
            return rt;
        }

        void ClearRT(RenderTexture rt, Color clearColor)
        {
            RenderTexture.active = rt;
            GL.Clear(true, true, clearColor);
        }

        // Allocate RTs once up-front (keeps lifetime predictable).
        GetOrCreateRT(ref rtSrgb, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        GetOrCreateRT(ref rtLinear, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        GetOrCreateRT(ref rtHalf, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);

        // Debug: quick manual override (uncomment to force a visible red texture into slice 0)
        // var testTex = Texture2D.redTexture;
        // albedoArray.SetPixels(testTex.GetPixels(), 0, 0);
        // albedoArray.Apply();

        int writeSlice = 0;
        var surfaceStart = new int[familyEntries.Count];

        for (int s = 0; s < familyEntries.Count; s++)
        {
            surfaceStart[s] = writeSlice;
            var entry = familyEntries[s];
            if (entry is SurfaceFamilyData sf)
            {
                int variants = Mathf.Max(1, sf.VariantCount);
                for (int v = 0; v < variants; v++)
                {
                    // Debug + validation: confirm slice add & catch mismatches early
                    var familyDisplayName = !string.IsNullOrEmpty(sf.familyName) ? sf.familyName : sf.name;
                    var albedo = sf.albedoArray;
                    int expectedWidth = targetW;
                    int expectedHeight = targetH;

                    Debug.Log($"[BuildSurfaceLibrary] Adding slice: {familyDisplayName}, Variant: {v}, Albedo: {(albedo ? albedo.name : "null")}, Format: {(albedo ? albedo.format.ToString() : "null")}, Size: {(albedo ? albedo.width + "x" + albedo.height : "null")}");

                    if (albedo != null && albedo.format != TextureFormat.RGBA32)
                    {
                        Debug.LogWarning($"[BuildSurfaceLibrary] Albedo texture {albedo.name} format is {albedo.format}, expected RGBA32. This may break texture array.");
                    }

                    if (albedo != null && (albedo.width != expectedWidth || albedo.height != expectedHeight))
                    {
                        Debug.LogWarning($"[BuildSurfaceLibrary] Albedo texture {albedo.name} size is {albedo.width}x{albedo.height}, expected {expectedWidth}x{expectedHeight}");
                    }

                    // Copy from sf arrays if available
                    if (sf.albedoArray != null && v < sf.albedoArray.depth)
                    {
                        if (sf.albedoArray.width == targetW && sf.albedoArray.height == targetH)
                        {
                            Graphics.CopyTexture(sf.albedoArray, v, 0, albedoArray, writeSlice, 0);
                        }
                        else
                        {
                            var tmpSrc = GetOrCreateTmp2D(sf.albedoArray.width, sf.albedoArray.height, TextureFormat.RGBA32, linear: false);
                            Graphics.CopyTexture(sf.albedoArray, v, 0, tmpSrc, 0, 0);
                            Graphics.Blit(tmpSrc, rtSrgb);
                            Graphics.CopyTexture(rtSrgb, 0, 0, albedoArray, writeSlice, 0);
                        }
                    }
                    else
                    {
                        // Full-size fallback albedo (white) without allocating a huge Color[].
                        ClearRT(rtSrgb, Color.white);
                        Graphics.CopyTexture(rtSrgb, 0, 0, albedoArray, writeSlice, 0);
                    }

                    if (sf.normalArray != null && v < sf.normalArray.depth)
                    {
                        if (sf.normalArray.width == targetW && sf.normalArray.height == targetH)
                        {
                            Graphics.CopyTexture(sf.normalArray, v, 0, normalArray, writeSlice, 0);
                        }
                        else
                        {
                            var tmpSrc = GetOrCreateTmp2D(sf.normalArray.width, sf.normalArray.height, TextureFormat.RGBA32, linear: true);
                            Graphics.CopyTexture(sf.normalArray, v, 0, tmpSrc, 0, 0);
                            Graphics.Blit(tmpSrc, rtLinear);
                            Graphics.CopyTexture(rtLinear, 0, 0, normalArray, writeSlice, 0);
                        }
                    }
                    else
                    {
                        // Full-size fallback normal (flat blue) without allocating a huge Color[].
                        ClearRT(rtLinear, new Color(0.5f, 0.5f, 1f, 1f));
                        Graphics.CopyTexture(rtLinear, 0, 0, normalArray, writeSlice, 0);
                    }

                    if (sf.maskArray != null && v < sf.maskArray.depth)
                    {
                        if (sf.maskArray.width == targetW && sf.maskArray.height == targetH)
                        {
                            Graphics.CopyTexture(sf.maskArray, v, 0, maskArray, writeSlice, 0);
                        }
                        else
                        {
                            var tmpSrc = GetOrCreateTmp2D(sf.maskArray.width, sf.maskArray.height, TextureFormat.RGBA32, linear: true);
                            Graphics.CopyTexture(sf.maskArray, v, 0, tmpSrc, 0, 0);
                            Graphics.Blit(tmpSrc, rtLinear);
                            Graphics.CopyTexture(rtLinear, 0, 0, maskArray, writeSlice, 0);
                        }
                    }
                    else
                    {
                        // Full-size fallback mask without allocating a huge Color[].
                        ClearRT(rtLinear, new Color(0f, 1f, 0f, 0.5f));
                        Graphics.CopyTexture(rtLinear, 0, 0, maskArray, writeSlice, 0);
                    }

                    // emissive
                    if (sf.emissiveArray != null && v < sf.emissiveArray.depth)
                    {
                        if (sf.emissiveArray.width == targetW && sf.emissiveArray.height == targetH)
                        {
                            Graphics.CopyTexture(sf.emissiveArray, v, 0, emissiveArray, writeSlice, 0);
                        }
                        else
                        {
                            var tmpSrc = GetOrCreateTmp2D(sf.emissiveArray.width, sf.emissiveArray.height, TextureFormat.RGBAHalf, linear: true);
                            Graphics.CopyTexture(sf.emissiveArray, v, 0, tmpSrc, 0, 0);
                            Graphics.Blit(tmpSrc, rtHalf);
                            Graphics.CopyTexture(rtHalf, 0, 0, emissiveArray, writeSlice, 0);
                        }
                    }
                    else
                    {
                        // Full-size fallback emissive (black) without allocating a huge Color[].
                        ClearRT(rtHalf, Color.black);
                        Graphics.CopyTexture(rtHalf, 0, 0, emissiveArray, writeSlice, 0);
                    }

                    writeSlice++;
                }
            }
            else if (entry is BiomeVisualData bv)
            {
                // ad-hoc single variant from legacy fields
                // albedo
                if (bv.albedo != null)
                {
                    Debug.Log($"[BuildSurfaceLibrary] Adding legacy slice: Biome {bv.name}, Albedo: {(bv.albedo ? bv.albedo.name : "null")}, Format: {(bv.albedo ? bv.albedo.format.ToString() : "null")}, Size: {(bv.albedo ? bv.albedo.width + "x" + bv.albedo.height : "null")}");

                    if (bv.albedo.format != TextureFormat.RGBA32)
                    {
                        Debug.LogWarning($"[BuildSurfaceLibrary] Albedo texture {bv.albedo.name} format is {bv.albedo.format}, expected RGBA32. This may break texture array.");
                    }

                    if (bv.albedo.width != targetW || bv.albedo.height != targetH)
                    {
                        Debug.LogWarning($"[BuildSurfaceLibrary] Albedo texture {bv.albedo.name} size is {bv.albedo.width}x{bv.albedo.height}, expected {targetW}x{targetH}");
                    }

                    if (bv.albedo.width != targetW || bv.albedo.height != targetH)
                    {
                        Graphics.Blit(bv.albedo, rtSrgb);
                        Graphics.CopyTexture(rtSrgb, 0, 0, albedoArray, writeSlice, 0);
                    }
                    else
                    {
                        Graphics.CopyTexture(bv.albedo, 0, 0, albedoArray, writeSlice, 0);
                    }
                }
                else
                {
                    ClearRT(rtSrgb, Color.white);
                    Graphics.CopyTexture(rtSrgb, 0, 0, albedoArray, writeSlice, 0);
                }

                if (bv.normal != null)
                {
                    if (bv.normal.width != targetW || bv.normal.height != targetH)
                    {
                        Graphics.Blit(bv.normal, rtLinear);
                        Graphics.CopyTexture(rtLinear, 0, 0, normalArray, writeSlice, 0);
                    }
                    else
                    {
                        Graphics.CopyTexture(bv.normal, 0, 0, normalArray, writeSlice, 0);
                    }
                }
                else
                {
                    ClearRT(rtLinear, new Color(0.5f, 0.5f, 1f, 1f));
                    Graphics.CopyTexture(rtLinear, 0, 0, normalArray, writeSlice, 0);
                }

                if (bv.maskMap != null)
                {
                    if (bv.maskMap.width != targetW || bv.maskMap.height != targetH)
                    {
                        Graphics.Blit(bv.maskMap, rtLinear);
                        Graphics.CopyTexture(rtLinear, 0, 0, maskArray, writeSlice, 0);
                    }
                    else
                    {
                        Graphics.CopyTexture(bv.maskMap, 0, 0, maskArray, writeSlice, 0);
                    }
                }
                else
                {
                    ClearRT(rtLinear, new Color(0f, 1f, 0f, 0.5f));
                    Graphics.CopyTexture(rtLinear, 0, 0, maskArray, writeSlice, 0);
                }

                // emissive: legacy per-biome BV has no emissive texture, fill black (use matching fallback)
                ClearRT(rtHalf, Color.black);
                Graphics.CopyTexture(rtHalf, 0, 0, emissiveArray, writeSlice, 0);

                writeSlice++;
            }
        }

        // Cleanup intermediate resources (keep array allocations intact; those are returned/cached).
        RenderTexture.active = prevActive;
        if (rtSrgb != null) RenderTexture.ReleaseTemporary(rtSrgb);
        if (rtLinear != null) RenderTexture.ReleaseTemporary(rtLinear);
        if (rtHalf != null) RenderTexture.ReleaseTemporary(rtHalf);
        foreach (var kv in tmp2DCache)
        {
            if (kv.Value != null) DestroyUnityObject(kv.Value);
        }
        tmp2DCache.Clear();

        // Build result
        var lib = new SurfaceLibrary();
        lib.albedoArray = albedoArray;
        lib.normalArray = normalArray;
        lib.maskArray = maskArray;
        lib.emissiveArray = emissiveArray;
        lib.totalSlices = writeSlice;

        lib.surfaceStartSlice = surfaceStart;
        lib.surfaceVariantCounts = variantCounts.ToArray();
        lib.biomeToSurfaceIndex = biomeToSurface;
        lib.biomeForcedVariant = biomeForced;

        // Store cache entry (prevents multi-planet builds from allocating arrays repeatedly).
        _surfaceLibraryCacheByDb[dbId] = new CachedSurfaceLibrary { signature = signature, library = lib };

        // Diagnostic: log actual managed heap + Unity-reported runtime size for arrays.
        // Profiler.GetRuntimeMemorySizeLong is approximate but good for identifying biggest offenders.
        try
        {
            long a = lib.albedoArray != null ? Profiler.GetRuntimeMemorySizeLong(lib.albedoArray) : 0;
            long n = lib.normalArray != null ? Profiler.GetRuntimeMemorySizeLong(lib.normalArray) : 0;
            long m = lib.maskArray != null ? Profiler.GetRuntimeMemorySizeLong(lib.maskArray) : 0;
            long e = lib.emissiveArray != null ? Profiler.GetRuntimeMemorySizeLong(lib.emissiveArray) : 0;
            Debug.Log($"[BiomeVisualDatabase] BuildSurfaceLibrary complete: slicesWritten={writeSlice}/{total}. " +
                      $"RuntimeMemorySize: albedo={FormatBytes(a)}, normal={FormatBytes(n)}, mask={FormatBytes(m)}, emissive={FormatBytes(e)}, TOTAL={FormatBytes(a+n+m+e)}");
        }
        catch { /* diagnostics only */ }

        return lib;
    }
}
