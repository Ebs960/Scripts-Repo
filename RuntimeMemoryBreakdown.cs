using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        // Create an early, persistent reporter.
        var go = new GameObject("__RuntimeMemoryBreakdown");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.DontSave;
        go.AddComponent<RuntimeMemoryBreakdown>();

        // Snapshot as early as Unity allows.
        Snapshot("BeforeSceneLoad");
    }

    private bool _subscribed;

    private void Start()
    {
        Snapshot("AfterSceneLoad");
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

    private void HandlePlanetGridBuilt(int planetIndex) => Snapshot($"OnPlanetGridBuilt planet={planetIndex}");
    private void HandlePlanetSurfaceGenerated(int planetIndex) => Snapshot($"OnPlanetSurfaceGenerated planet={planetIndex}");
    private void HandlePlanetReady(int planetIndex) => Snapshot($"OnPlanetReady planet={planetIndex}");

    private static void Snapshot(string label)
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
            count++;
            total += Profiler.GetRuntimeMemorySizeLong(o);
        }

        Debug.Log($"[Memory] {typeName}: count={count} runtimeMem≈{FormatBytes(total)}");
    }

    private static void LogTopNTextures()
    {
        var entries = new List<(string kind, string name, long bytes)>(256);

        void Add<T>(string kind) where T : UnityEngine.Object
        {
            foreach (var o in Resources.FindObjectsOfTypeAll<T>())
            {
                if (o == null) continue;
                long b = Profiler.GetRuntimeMemorySizeLong(o);
                if (b <= 0) continue;
                entries.Add((kind, o.name, b));
            }
        }

        Add<Texture2DArray>("Texture2DArray");
        Add<RenderTexture>("RenderTexture");
        Add<Texture2D>("Texture2D");

        var top = entries
            .OrderByDescending(e => e.bytes)
            .Take(TopN)
            .ToArray();

        if (top.Length == 0) return;

        Debug.Log("[Memory] Top textures by runtime memory:");
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

