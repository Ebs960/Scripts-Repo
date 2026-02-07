using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using System.Reflection;
using Debug = UnityEngine.Debug;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Startup memory reporter to help diagnose out-of-memory issues.
/// Prints:
/// - OS process memory (working set + private bytes)
/// - Unity total reserved/allocated/mono heap
/// - Breakdown by major Unity object types (textures, render textures, texture arrays, meshes, materials)
/// - Top-N largest textures/arrays by runtime memory size
///
/// Notes:
/// - Heavy-ish (uses Resources.FindObjectsOfTypeAll). Intended for Editor + Development Builds.
/// - This is diagnostics only; it does not change gameplay behavior.
/// </summary>
public sealed class RuntimeMemoryBreakdown : MonoBehaviour
{
#if UNITY_EDITOR
    private const int TopN = 15;

    // If true, the heavy object breakdown (textures, arrays) is only collected for the first planet (planetIndex==0).
    // This reduces repeated heavy reports that can allocate or produce excessive logging in multi-planet runs.
    private static bool _detailedOnlyFirstPlanet = true;
    private static bool _detailedSnapshotTaken = false;

    // Public toggle to allow enabling/disabling first-planet-only detailed reports at runtime
    public static bool DetailedOnlyFirstPlanet
    {
        get => _detailedOnlyFirstPlanet;
        set => _detailedOnlyFirstPlanet = value;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        // If domain reload is disabled, static caches can persist across Play sessions and
        // accumulate huge Texture2DArray allocations. Clear known caches on entering Play.
        try
        {
            PlanetTextureBaker.ClearAllCaches();
            BiomeVisualDatabase.ClearAllCachedSurfaceLibraries();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Memory] Cache clear on play start failed: {ex.Message}");
        }

        // Create an early, persistent reporter.
        var go = new GameObject("__RuntimeMemoryBreakdown");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.DontSave;
        go.AddComponent<RuntimeMemoryBreakdown>();

        // Snapshot as early as Unity allows.
        // Keep the very-first snapshot lightweight to avoid tipping over into OOM.
        Snapshot("BeforeSceneLoad", includeObjectBreakdown: false);
    }

    private bool _subscribed;

    private void Start()
    {
        Snapshot("AfterSceneLoad", includeObjectBreakdown: true);
    }

    private void Update()
    {
        // Subscribe once GameManager exists so we can record key lifecycle points.
        if (_subscribed) return;
        if (GameManager.Instance == null) return;

        _subscribed = true;
        GameManager.Instance.OnPlanetGridBuilt += HandlePlanetGridBuilt;
        GameManager.Instance.OnPlanetSurfaceGenerated += HandlePlanetSurfaceGenerated;
        GameManager.Instance.OnPlanetReady += HandlePlanetReady;
    }

    private void OnDestroy()
    {
        if (!_subscribed) return;
        if (GameManager.Instance == null) return;
        GameManager.Instance.OnPlanetGridBuilt -= HandlePlanetGridBuilt;
        GameManager.Instance.OnPlanetSurfaceGenerated -= HandlePlanetSurfaceGenerated;
        GameManager.Instance.OnPlanetReady -= HandlePlanetReady;
    }

    private void HandlePlanetGridBuilt(int planetIndex)
    {
        // Lightweight grid-built snapshot (always include breakdown for quick diagnostics)
        Snapshot($"OnPlanetGridBuilt planet={planetIndex}", includeObjectBreakdown: true);
    }

    private void HandlePlanetSurfaceGenerated(int planetIndex)
    {
        // Optionally restrict heavy object breakdown to first planet only to reduce noise/overhead
        bool include = !_detailedOnlyFirstPlanet || planetIndex == 0 || !_detailedSnapshotTaken;
        Snapshot($"OnPlanetSurfaceGenerated planet={planetIndex}", includeObjectBreakdown: include);
        if (planetIndex == 0) _detailedSnapshotTaken = true;
    }

    private void HandlePlanetReady(int planetIndex)
    {
        bool include = !_detailedOnlyFirstPlanet || planetIndex == 0 || !_detailedSnapshotTaken;
        Snapshot($"OnPlanetReady planet={planetIndex}", includeObjectBreakdown: include);
        if (planetIndex == 0) _detailedSnapshotTaken = true;
    }

    private static void Snapshot(string label, bool includeObjectBreakdown)
    {
        try
        {
            var proc = Process.GetCurrentProcess();
            long ws = proc.WorkingSet64;
            long priv = proc.PrivateMemorySize64;

            long totalAlloc = Profiler.GetTotalAllocatedMemoryLong();
            long totalReserved = Profiler.GetTotalReservedMemoryLong();
            long totalUnusedReserved = Profiler.GetTotalUnusedReservedMemoryLong();
            long monoHeap = Profiler.GetMonoHeapSizeLong();
            long monoUsed = Profiler.GetMonoUsedSizeLong();
            long gcTotal = GC.GetTotalMemory(false);

            Debug.Log(
                $"[Memory][{label}] Process: WorkingSet={FormatBytes(ws)} Private={FormatBytes(priv)} | " +
                $"Unity: Alloc={FormatBytes(totalAlloc)} Reserved={FormatBytes(totalReserved)} UnusedReserved={FormatBytes(totalUnusedReserved)} | " +
                $"MonoHeap={FormatBytes(monoHeap)} MonoUsed={FormatBytes(monoUsed)} | GC.GetTotalMemory={FormatBytes(gcTotal)}");

            if (!includeObjectBreakdown) return;

            // Major object type breakdown
            SummarizeType<Texture2DArray>("Texture2DArray");
            SummarizeType<RenderTexture>("RenderTexture");
            SummarizeType<Texture2D>("Texture2D");
            SummarizeType<Mesh>("Mesh");
            SummarizeType<Material>("Material");

            LogTopNTextures();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Memory][{label}] Snapshot failed: {ex.Message}");
        }
    }

    private static void SummarizeType<T>(string typeName) where T : UnityEngine.Object
    {
        var objs = Resources.FindObjectsOfTypeAll<T>();
        long total = 0;
        int count = 0;

        foreach (var o in objs)
        {
            if (o == null) continue;
            // Filter to project assets (Assets/ or Packages/) OR allow named runtime objects
            bool include = false;
#if UNITY_EDITOR
            string path = AssetDatabase.GetAssetPath(o);
            if (!string.IsNullOrEmpty(path) && (path.StartsWith("Assets/") || path.StartsWith("Packages/")))
            {
                include = true;
            }
#endif
            if (!include)
            {
                // Allow runtime-created textures if they contain common game prefixes
                var n = o.name ?? string.Empty;
                if (n.IndexOf("biome", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("planet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("minimap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("tile", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("biometexturearray", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    include = true;
                }
            }

            if (!include) continue;

            count++;
            total += Profiler.GetRuntimeMemorySizeLong(o);
        }

        Debug.Log($"[Memory] {typeName}: count={count} runtimeMem≈{FormatBytes(total)}");
    }

    private static void LogTopNTextures()
    {
        var entries = new List<(string kind, string name, long bytes)>(256);

        void AddObject(UnityEngine.Object o, string kind)
        {
            if (o == null) return;
            long b = Profiler.GetRuntimeMemorySizeLong(o);
            if (b <= 0) return;
            entries.Add((kind, o.name, b));
        }

        // 1) PlanetTextureBaker caches (private static fields) via reflection
        try
        {
            var ptType = typeof(PlanetTextureBaker);
            var f3 = ptType.GetField("_biomeTextureCache", BindingFlags.NonPublic | BindingFlags.Static);
            var f4 = ptType.GetField("_heightTextureCache", BindingFlags.NonPublic | BindingFlags.Static);
            if (f3 != null)
            {
                var dict = f3.GetValue(null) as System.Collections.IDictionary;
                if (dict != null)
                {
                    foreach (System.Collections.DictionaryEntry de in dict)
                        AddObject(de.Value as UnityEngine.Object, "RenderTexture");
                }
            }
            if (f4 != null)
            {
                var dict = f4.GetValue(null) as System.Collections.IDictionary;
                if (dict != null)
                {
                    foreach (System.Collections.DictionaryEntry de in dict)
                        AddObject(de.Value as UnityEngine.Object, "RenderTexture");
                }
            }
        }
        catch { }

        // 2) BiomeVisualDatabase cached surface libraries (private static cache)
        try
        {
            var bvType = typeof(BiomeVisualDatabase);
            var cacheField = bvType.GetField("_surfaceLibraryCacheByDb", BindingFlags.NonPublic | BindingFlags.Static);
            if (cacheField != null)
            {
                var cache = cacheField.GetValue(null) as System.Collections.IDictionary;
                if (cache != null)
                {
                    foreach (System.Collections.DictionaryEntry de in cache)
                    {
                        var cached = de.Value;
                        if (cached == null) continue;
                        // cached is a struct value; use reflection to get 'library'
                        var libField = cached.GetType().GetField("library");
                        if (libField == null) continue;
                        var lib = libField.GetValue(cached) as object;
                        if (lib == null) continue;
                        var libType = lib.GetType();
                        var a = libType.GetField("albedoArray");
                        var n = libType.GetField("normalArray");
                        var m = libType.GetField("maskArray");
                        var e = libType.GetField("emissiveArray");
                        if (a != null) AddObject(a.GetValue(lib) as UnityEngine.Object, "Texture2DArray");
                        if (n != null) AddObject(n.GetValue(lib) as UnityEngine.Object, "Texture2DArray");
                        if (m != null) AddObject(m.GetValue(lib) as UnityEngine.Object, "Texture2DArray");
                        if (e != null) AddObject(e.GetValue(lib) as UnityEngine.Object, "Texture2DArray");
                    }
                }
            }
        }
        catch { }

        // Sort and print top entries (by bytes)
        var top = entries
            .OrderByDescending(e => e.bytes)
            .Take(TopN)
            .ToArray();

        if (top.Length == 0) return;

        Debug.Log("[Memory] Top game textures by runtime memory:");
        for (int i = 0; i < top.Length; i++)
        {
            Debug.Log($"[Memory]  #{i + 1:00} {top[i].kind} '{top[i].name}' ≈ {FormatBytes(top[i].bytes)}");
        }
    }

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
#endif
}

