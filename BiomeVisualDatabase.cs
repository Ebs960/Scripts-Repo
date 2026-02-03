using System.Collections.Generic;
using UnityEngine;

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

        // Create a fallback black emissive texture matching the emissive array format/size
        Texture2D fallbackEmissive = new Texture2D(targetW, targetH, TextureFormat.RGBAHalf, true, true)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            name = "SurfaceEmissiveFallback"
        };
        var blackPixels = new Color[targetW * targetH];
        for (int i = 0; i < blackPixels.Length; i++) blackPixels[i] = Color.black;
        fallbackEmissive.SetPixels(blackPixels);
        fallbackEmissive.Apply(true, false);

        albedoArray.wrapMode = TextureWrapMode.Repeat;
        normalArray.wrapMode = TextureWrapMode.Repeat;
        maskArray.wrapMode = TextureWrapMode.Repeat;

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
                        // Fix: handle size mismatches (e.g. 2048x2048 sources) by resampling to targetW/targetH.
                        if (sf.albedoArray.width != targetW || sf.albedoArray.height != targetH)
                        {
                            var tmpSrc = new Texture2D(sf.albedoArray.width, sf.albedoArray.height, TextureFormat.RGBA32, false, false);
                            Graphics.CopyTexture(sf.albedoArray, v, 0, tmpSrc, 0, 0);

                            var rt = RenderTexture.GetTemporary(targetW, targetH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                            Graphics.Blit(tmpSrc, rt);

                            var scaled = new Texture2D(targetW, targetH, TextureFormat.RGBA32, true, false);
                            var prev = RenderTexture.active;
                            RenderTexture.active = rt;
                            scaled.ReadPixels(new Rect(0, 0, targetW, targetH), 0, 0);
                            scaled.Apply(true, false);
                            RenderTexture.active = prev;

                            RenderTexture.ReleaseTemporary(rt);
                            UnityEngine.Object.Destroy(tmpSrc);

                            Graphics.CopyTexture(scaled, 0, 0, albedoArray, writeSlice, 0);
                            UnityEngine.Object.Destroy(scaled);
                        }
                        else
                        {
                            Graphics.CopyTexture(sf.albedoArray, v, 0, albedoArray, writeSlice, 0);
                        }
                    }
                    else
                    {
                        // fill white
                        var white = Texture2D.whiteTexture;
                        Graphics.CopyTexture(white, 0, 0, albedoArray, writeSlice, 0);
                    }

                    if (sf.normalArray != null && v < sf.normalArray.depth)
                    {
                        if (sf.normalArray.width != targetW || sf.normalArray.height != targetH)
                        {
                            var tmpSrc = new Texture2D(sf.normalArray.width, sf.normalArray.height, TextureFormat.RGBA32, false, true);
                            Graphics.CopyTexture(sf.normalArray, v, 0, tmpSrc, 0, 0);

                            var rt = RenderTexture.GetTemporary(targetW, targetH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                            Graphics.Blit(tmpSrc, rt);

                            var scaled = new Texture2D(targetW, targetH, TextureFormat.RGBA32, true, true);
                            var prev = RenderTexture.active;
                            RenderTexture.active = rt;
                            scaled.ReadPixels(new Rect(0, 0, targetW, targetH), 0, 0);
                            scaled.Apply(true, false);
                            RenderTexture.active = prev;

                            RenderTexture.ReleaseTemporary(rt);
                            UnityEngine.Object.Destroy(tmpSrc);

                            Graphics.CopyTexture(scaled, 0, 0, normalArray, writeSlice, 0);
                            UnityEngine.Object.Destroy(scaled);
                        }
                        else
                        {
                            Graphics.CopyTexture(sf.normalArray, v, 0, normalArray, writeSlice, 0);
                        }
                    }
                    else
                    {
                        var flat = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
                        flat.SetPixel(0, 0, new Color(0.5f, 0.5f, 1f));
                        flat.Apply();
                        Graphics.CopyTexture(flat, 0, 0, normalArray, writeSlice, 0);
                    }

                    if (sf.maskArray != null && v < sf.maskArray.depth)
                    {
                        if (sf.maskArray.width != targetW || sf.maskArray.height != targetH)
                        {
                            var tmpSrc = new Texture2D(sf.maskArray.width, sf.maskArray.height, TextureFormat.RGBA32, false, true);
                            Graphics.CopyTexture(sf.maskArray, v, 0, tmpSrc, 0, 0);

                            var rt = RenderTexture.GetTemporary(targetW, targetH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                            Graphics.Blit(tmpSrc, rt);

                            var scaled = new Texture2D(targetW, targetH, TextureFormat.RGBA32, true, true);
                            var prev = RenderTexture.active;
                            RenderTexture.active = rt;
                            scaled.ReadPixels(new Rect(0, 0, targetW, targetH), 0, 0);
                            scaled.Apply(true, false);
                            RenderTexture.active = prev;

                            RenderTexture.ReleaseTemporary(rt);
                            UnityEngine.Object.Destroy(tmpSrc);

                            Graphics.CopyTexture(scaled, 0, 0, maskArray, writeSlice, 0);
                            UnityEngine.Object.Destroy(scaled);
                        }
                        else
                        {
                            Graphics.CopyTexture(sf.maskArray, v, 0, maskArray, writeSlice, 0);
                        }
                    }
                    else
                    {
                        var def = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
                        def.SetPixel(0, 0, new Color(0f, 1f, 0f, 0.5f));
                        def.Apply();
                        Graphics.CopyTexture(def, 0, 0, maskArray, writeSlice, 0);
                    }

                    // emissive
                    if (sf.emissiveArray != null && v < sf.emissiveArray.depth)
                    {
                        if (sf.emissiveArray.width != targetW || sf.emissiveArray.height != targetH)
                        {
                            var tmpSrc = new Texture2D(sf.emissiveArray.width, sf.emissiveArray.height, TextureFormat.RGBAHalf, false, true);
                            Graphics.CopyTexture(sf.emissiveArray, v, 0, tmpSrc, 0, 0);

                            var rt = RenderTexture.GetTemporary(targetW, targetH, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                            Graphics.Blit(tmpSrc, rt);

                            var scaled = new Texture2D(targetW, targetH, TextureFormat.RGBAHalf, true, true);
                            var prev = RenderTexture.active;
                            RenderTexture.active = rt;
                            scaled.ReadPixels(new Rect(0, 0, targetW, targetH), 0, 0);
                            scaled.Apply(true, false);
                            RenderTexture.active = prev;

                            RenderTexture.ReleaseTemporary(rt);
                            UnityEngine.Object.Destroy(tmpSrc);

                            Graphics.CopyTexture(scaled, 0, 0, emissiveArray, writeSlice, 0);
                            UnityEngine.Object.Destroy(scaled);
                        }
                        else
                        {
                            Graphics.CopyTexture(sf.emissiveArray, v, 0, emissiveArray, writeSlice, 0);
                        }
                    }
                    else
                    {
                        Graphics.CopyTexture(fallbackEmissive, 0, 0, emissiveArray, writeSlice, 0);
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
                        var rt = RenderTexture.GetTemporary(targetW, targetH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                        Graphics.Blit(bv.albedo, rt);

                        var scaled = new Texture2D(targetW, targetH, TextureFormat.RGBA32, true, false);
                        var prev = RenderTexture.active;
                        RenderTexture.active = rt;
                        scaled.ReadPixels(new Rect(0, 0, targetW, targetH), 0, 0);
                        scaled.Apply(true, false);
                        RenderTexture.active = prev;

                        RenderTexture.ReleaseTemporary(rt);
                        Graphics.CopyTexture(scaled, 0, 0, albedoArray, writeSlice, 0);
                        UnityEngine.Object.Destroy(scaled);
                    }
                    else
                    {
                        Graphics.CopyTexture(bv.albedo, 0, 0, albedoArray, writeSlice, 0);
                    }
                }
                else
                {
                    Graphics.CopyTexture(Texture2D.whiteTexture, 0, 0, albedoArray, writeSlice, 0);
                }

                if (bv.normal != null)
                {
                    if (bv.normal.width != targetW || bv.normal.height != targetH)
                    {
                        var rt = RenderTexture.GetTemporary(targetW, targetH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                        Graphics.Blit(bv.normal, rt);

                        var scaled = new Texture2D(targetW, targetH, TextureFormat.RGBA32, true, true);
                        var prev = RenderTexture.active;
                        RenderTexture.active = rt;
                        scaled.ReadPixels(new Rect(0, 0, targetW, targetH), 0, 0);
                        scaled.Apply(true, false);
                        RenderTexture.active = prev;

                        RenderTexture.ReleaseTemporary(rt);
                        Graphics.CopyTexture(scaled, 0, 0, normalArray, writeSlice, 0);
                        UnityEngine.Object.Destroy(scaled);
                    }
                    else
                    {
                        Graphics.CopyTexture(bv.normal, 0, 0, normalArray, writeSlice, 0);
                    }
                }
                else
                {
                    var flat = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
                    flat.SetPixel(0, 0, new Color(0.5f, 0.5f, 1f));
                    flat.Apply();
                    Graphics.CopyTexture(flat, 0, 0, normalArray, writeSlice, 0);
                }

                if (bv.maskMap != null)
                {
                    if (bv.maskMap.width != targetW || bv.maskMap.height != targetH)
                    {
                        var rt = RenderTexture.GetTemporary(targetW, targetH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                        Graphics.Blit(bv.maskMap, rt);

                        var scaled = new Texture2D(targetW, targetH, TextureFormat.RGBA32, true, true);
                        var prev = RenderTexture.active;
                        RenderTexture.active = rt;
                        scaled.ReadPixels(new Rect(0, 0, targetW, targetH), 0, 0);
                        scaled.Apply(true, false);
                        RenderTexture.active = prev;

                        RenderTexture.ReleaseTemporary(rt);
                        Graphics.CopyTexture(scaled, 0, 0, maskArray, writeSlice, 0);
                        UnityEngine.Object.Destroy(scaled);
                    }
                    else
                    {
                        Graphics.CopyTexture(bv.maskMap, 0, 0, maskArray, writeSlice, 0);
                    }
                }
                else
                {
                    var def = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
                    def.SetPixel(0, 0, new Color(0f, 1f, 0f, 0.5f));
                    def.Apply();
                    Graphics.CopyTexture(def, 0, 0, maskArray, writeSlice, 0);
                }

                    // emissive: legacy per-biome BV has no emissive texture, fill black (use matching fallback)
                    Graphics.CopyTexture(fallbackEmissive, 0, 0, emissiveArray, writeSlice, 0);

                writeSlice++;
            }
        }

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

        return lib;
    }
}
