using System.Text;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Profiling;
#endif

/// <summary>
/// Development-only performance instrumentation. Aggregates frame time, memory, world-size
/// counters, and AI/DangerMap diagnostics into a single human-readable report so regressions can
/// be measured and compared across sessions, rather than relying on scattered ad-hoc logs.
///
/// This class intentionally does nothing in shipping builds: the ProfilerRecorder fields only
/// exist in editor/development builds, and the periodic auto-log Update() is fully compiled out
/// outside of UNITY_EDITOR/DEVELOPMENT_BUILD, so there is zero runtime cost in a release build.
///
/// Usage: add to a persistent scene object (or call PerformanceBenchmarkRunner.GetOrCreate()),
/// then call GenerateReport()/LogReport() from a debug menu, or let autoLogIntervalSeconds > 0
/// print periodically.
/// </summary>
public sealed class PerformanceBenchmarkRunner : MonoBehaviour
{
    public static PerformanceBenchmarkRunner Instance { get; private set; }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [Tooltip("If > 0, automatically logs a report every N seconds. 0 disables auto-logging.")]
    [SerializeField] private float autoLogIntervalSeconds = 0f;

    private ProfilerRecorder mainThreadTimeRecorder;
    private ProfilerRecorder gcAllocatedInFrameRecorder;
    private ProfilerRecorder totalReservedMemoryRecorder;
    private ProfilerRecorder totalUsedMemoryRecorder;

    private float nextAutoLogTime;

    // Rolling frame-time sample for a smoothed average (avoids single-frame spikes dominating the report).
    private const int FrameSampleCount = 60;
    private readonly double[] frameTimesMs = new double[FrameSampleCount];
    private int frameSampleIndex;
    private int frameSamplesTaken;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        mainThreadTimeRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
        gcAllocatedInFrameRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
        totalReservedMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Reserved Memory", 1);
        totalUsedMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory", 1);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        mainThreadTimeRecorder.Dispose();
        gcAllocatedInFrameRecorder.Dispose();
        totalReservedMemoryRecorder.Dispose();
        totalUsedMemoryRecorder.Dispose();
    }

    private void Update()
    {
        if (mainThreadTimeRecorder.Valid)
        {
            frameTimesMs[frameSampleIndex] = mainThreadTimeRecorder.LastValue / 1_000_000.0; // ns -> ms
            frameSampleIndex = (frameSampleIndex + 1) % FrameSampleCount;
            if (frameSamplesTaken < FrameSampleCount) frameSamplesTaken++;
        }

        if (autoLogIntervalSeconds > 0f && Time.unscaledTime >= nextAutoLogTime)
        {
            nextAutoLogTime = Time.unscaledTime + autoLogIntervalSeconds;
            Debug.Log(GenerateReport());
        }
    }

    private double AverageFrameTimeMs()
    {
        if (frameSamplesTaken == 0) return 0.0;
        double sum = 0.0;
        for (int i = 0; i < frameSamplesTaken; i++) sum += frameTimesMs[i];
        return sum / frameSamplesTaken;
    }
#endif

    public static PerformanceBenchmarkRunner GetOrCreate()
    {
        if (Instance != null) return Instance;
        var existing = FindAnyObjectByType<PerformanceBenchmarkRunner>();
        if (existing != null) return existing;
        var go = new GameObject("PerformanceBenchmarkRunner");
        return go.AddComponent<PerformanceBenchmarkRunner>();
    }

    /// <summary>
    /// Builds a full diagnostic report: frame time/memory (editor/dev builds only), world-size
    /// counters, AI phase timings for the most recently processed civ, and DangerMap lifecycle
    /// diagnostics (full rebuilds vs incremental updates - see the Performance Hardening Pass notes).
    /// </summary>
    public string GenerateReport()
    {
        var sb = new StringBuilder(512);
        sb.AppendLine("=== Performance Benchmark Report ===");

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        double avgFrameMs = AverageFrameTimeMs();
        double fps = avgFrameMs > 0 ? 1000.0 / avgFrameMs : 0;
        sb.AppendLine($"Frame time: {avgFrameMs:F2} ms avg over last {frameSamplesTaken} frames (~{fps:F0} FPS)");
        if (gcAllocatedInFrameRecorder.Valid)
            sb.AppendLine($"GC alloc (last frame): {gcAllocatedInFrameRecorder.LastValue / 1024.0:F1} KB");
        if (totalUsedMemoryRecorder.Valid)
            sb.AppendLine($"Managed memory used: {totalUsedMemoryRecorder.LastValue / (1024.0 * 1024.0):F1} MB");
        if (totalReservedMemoryRecorder.Valid)
            sb.AppendLine($"Managed memory reserved: {totalReservedMemoryRecorder.LastValue / (1024.0 * 1024.0):F1} MB");
#else
        sb.AppendLine("Frame time / memory: unavailable outside editor/development builds.");
#endif

        // World-size counters
        var civs = CivilizationManager.Instance != null ? CivilizationManager.Instance.GetAllCivs() : null;
        int civCount = 0, combatUnits = 0, workerUnits = 0, cities = 0;
        if (civs != null)
        {
            civCount = civs.Count;
            foreach (var c in civs)
            {
                if (c == null) continue;
                combatUnits += c.combatUnits?.Count ?? 0;
                workerUnits += c.workerUnits?.Count ?? 0;
                cities += c.cities?.Count ?? 0;
            }
        }
        int animals = AnimalManager.Instance != null ? AnimalManager.Instance.GetActiveAnimals().Count : 0;
        sb.AppendLine($"World size: civs={civCount} combatUnits={combatUnits} workerUnits={workerUnits} cities={cities} animals={animals}");

        // AI phase timings (most recently processed civ's turn - AIPlanner is a single shared instance)
        var planner = CivilizationManager.Instance != null ? CivilizationManager.Instance.AiPlanner : null;
        if (planner != null)
        {
            sb.AppendLine($"AI last turn: danger={planner.TimeDangerMap:F1}ms ctx={planner.TimeContext:F1}ms " +
                          $"strat={planner.TimeStrategic:F1}ms ops={planner.TimeOperational:F1}ms " +
                          $"tact={planner.TimeTactical:F1}ms total={planner.TimeTotal:F1}ms");
            sb.AppendLine($"DangerMap lifecycle: active={planner.ActiveDangerMapCount} " +
                          $"fullRebuilds={DangerMap.FullRebuildCount} incrementalUpdates={DangerMap.IncrementalUpdateCount} " +
                          $"activeSubscriptions={DangerMap.ActiveSubscriptionCount}");
        }

        sb.AppendLine("=====================================");
        return sb.ToString();
    }

    public void LogReport() => Debug.Log(GenerateReport());
}
