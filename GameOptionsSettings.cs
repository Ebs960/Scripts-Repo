using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central settings store shared by the Main Menu and in-game Pause Menu Options screens.
///
/// - One PlayerPrefs key per setting. Existing audio/autosave keys are reused so saved preferences survive.
/// - Setters apply the change to the live system immediately and raise events for systems that own
///   their own state (camera, autosave, tooltips) so no menu code has to search the scene.
/// - PlayerPrefs writes are batched: setters mark the store dirty and <see cref="Flush"/> persists.
///   The Options UI flushes when it closes; the store also flushes on application quit.
/// - Display mode / resolution are previewed via <see cref="ApplyDisplayTemporarily"/> and only persisted
///   through <see cref="SaveConfirmedDisplay"/> after the UI's confirmation window.
/// </summary>
public static class GameOptionsSettings
{
    // Graphics
    private const string DisplayModeKey = "Options.DisplayMode.v1";
    private const string WidthKey = "Options.Width.v1";
    private const string HeightKey = "Options.Height.v1";
    private const string QualityKey = "Options.Quality.v1";
    private const string VsyncKey = "Options.VSync.v1";
    private const string FpsKey = "Options.FrameLimit.v1";
    // Audio (pre-existing keys used by MenuMusicManager / MusicManager)
    private const string MenuMusicEnabledKey = "MenuMusicEnabled";
    private const string MenuMusicVolumeKey = "MenuMusicVolume";
    private const string GameMusicEnabledKey = "MusicEnabled";
    private const string GameMusicVolumeKey = "GameMusicVolume";
    // Gameplay (pre-existing autosave keys written by the Main Menu)
    private const string AutosaveEnabledKey = "AutosaveEnabled";
    private const string AutosaveIntervalKey = "AutosaveInterval";
    private const string PanKey = "Options.CameraPan.v1";
    private const string ZoomKey = "Options.CameraZoom.v1";
    // Interface
    private const string TooltipDelayKey = "Options.TooltipDelay.v1";

    public const float DefaultMusicVolume = 0.75f;
    public const int DefaultAutosaveInterval = 3;
    public const float MinCameraMultiplier = 0.5f;
    public const float MaxCameraMultiplier = 2f;
    public static readonly int[] FrameLimitChoices = { 30, 60, 120, 0 };

    public enum DisplayMode { ExclusiveFullscreen = 0, Borderless = 1, Windowed = 2 }
    public enum TooltipDelay { Instant = 0, Short = 1, Normal = 2 }

    /// <summary>Raised after camera pan/zoom multipliers change.</summary>
    public static event Action CameraSettingsChanged;
    /// <summary>Raised after autosave enabled/interval change.</summary>
    public static event Action AutosaveSettingsChanged;
    /// <summary>Raised after the tooltip delay preference changes.</summary>
    public static event Action TooltipDelayChanged;

    private static bool dirty;
    private static bool quitHooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplySavedGraphicsAtStartup()
    {
        HookQuit();
        if (PlayerPrefs.HasKey(QualityKey))
            ApplyQuality(PlayerPrefs.GetInt(QualityKey, QualitySettings.GetQualityLevel()));
        ApplyVSync(VSync);
        ApplyFrameLimit(FrameLimit);
        if (PlayerPrefs.HasKey(DisplayModeKey) && PlayerPrefs.HasKey(WidthKey) && PlayerPrefs.HasKey(HeightKey))
        {
            ApplyDisplayTemporarily(
                (DisplayMode)Mathf.Clamp(PlayerPrefs.GetInt(DisplayModeKey), 0, 2),
                new Vector2Int(PlayerPrefs.GetInt(WidthKey), PlayerPrefs.GetInt(HeightKey)));
        }
    }

    private static void HookQuit()
    {
        if (quitHooked) return;
        quitHooked = true;
        Application.quitting += Flush;
    }

    private static void MarkDirty()
    {
        HookQuit();
        dirty = true;
    }

    /// <summary>Persists pending PlayerPrefs changes. Cheap when nothing changed.</summary>
    public static void Flush()
    {
        if (!dirty) return;
        dirty = false;
        PlayerPrefs.Save();
    }

    // ------------------------------------------------------------------ Graphics

    public static DisplayMode CurrentDisplayMode
    {
        get
        {
            switch (Screen.fullScreenMode)
            {
                case FullScreenMode.ExclusiveFullScreen: return DisplayMode.ExclusiveFullscreen;
                case FullScreenMode.Windowed: return DisplayMode.Windowed;
                default: return DisplayMode.Borderless;
            }
        }
    }

    public static Vector2Int CurrentResolution => new Vector2Int(Screen.width, Screen.height);
    public static string[] QualityNames => QualitySettings.names;
    public static int CurrentQuality => QualitySettings.GetQualityLevel();
    public static bool VSync => PlayerPrefs.GetInt(VsyncKey, QualitySettings.vSyncCount > 0 ? 1 : 0) != 0;
    public static int FrameLimit => NormalizeFrameLimit(PlayerPrefs.GetInt(FpsKey, 60));

    /// <summary>Supported resolutions (deduplicated by size, ascending) with the current one always present.</summary>
    public static List<Vector2Int> AvailableResolutions()
    {
        var list = new List<Vector2Int>();
        var seen = new HashSet<long>();
        var current = CurrentResolution;
        AddResolution(list, seen, current.x, current.y);
        foreach (Resolution r in Screen.resolutions)
            AddResolution(list, seen, r.width, r.height);
        if (list.Count == 0)
            list.Add(new Vector2Int(1280, 720));
        list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        return list;
    }

    private static void AddResolution(List<Vector2Int> list, HashSet<long> seen, int w, int h)
    {
        if (w < 640 || h < 480) return;
        long key = ((long)w << 32) | (uint)h;
        if (seen.Add(key)) list.Add(new Vector2Int(w, h));
    }

    /// <summary>Applies a display mode/resolution without persisting it (the UI confirms within 15 s).</summary>
    public static void ApplyDisplayTemporarily(DisplayMode mode, Vector2Int resolution)
    {
        int w = Mathf.Max(640, resolution.x);
        int h = Mathf.Max(480, resolution.y);
#if UNITY_EDITOR
        Debug.Log($"[GameOptionsSettings] Display preview ({mode}, {w}x{h}) is only applied in standalone builds; the Editor Game view is unchanged.");
#else
        FullScreenMode fullMode = FullScreenMode.FullScreenWindow;
        if (mode == DisplayMode.ExclusiveFullscreen) fullMode = FullScreenMode.ExclusiveFullScreen;
        if (mode == DisplayMode.Windowed) fullMode = FullScreenMode.Windowed;
        Screen.SetResolution(w, h, fullMode);
#endif
    }

    public static void SaveConfirmedDisplay(DisplayMode mode, Vector2Int resolution)
    {
        PlayerPrefs.SetInt(DisplayModeKey, (int)mode);
        PlayerPrefs.SetInt(WidthKey, resolution.x);
        PlayerPrefs.SetInt(HeightKey, resolution.y);
        MarkDirty();
        Flush();
    }

    public static void SetQuality(int index)
    {
        if (!ApplyQuality(index)) return;
        PlayerPrefs.SetInt(QualityKey, QualitySettings.GetQualityLevel());
        MarkDirty();
    }

    private static bool ApplyQuality(int index)
    {
        if (QualitySettings.names.Length == 0) return false;
        int safe = Mathf.Clamp(index, 0, QualitySettings.names.Length - 1);
        if (safe != QualitySettings.GetQualityLevel())
            QualitySettings.SetQualityLevel(safe, true);
        // Changing quality level can reset vSyncCount from the quality asset; re-apply the player's choice.
        ApplyVSync(VSync);
        return true;
    }

    public static void SetVSync(bool enabled)
    {
        PlayerPrefs.SetInt(VsyncKey, enabled ? 1 : 0);
        ApplyVSync(enabled);
        MarkDirty();
    }

    private static void ApplyVSync(bool enabled)
    {
        QualitySettings.vSyncCount = enabled ? 1 : 0;
    }

    public static void SetFrameLimit(int fps)
    {
        int safe = NormalizeFrameLimit(fps);
        PlayerPrefs.SetInt(FpsKey, safe);
        ApplyFrameLimit(safe);
        MarkDirty();
    }

    private static void ApplyFrameLimit(int fps)
    {
        Application.targetFrameRate = fps <= 0 ? -1 : fps;
    }

    private static int NormalizeFrameLimit(int fps)
    {
        return Array.IndexOf(FrameLimitChoices, fps) >= 0 ? fps : 0;
    }

    // ------------------------------------------------------------------ Audio

    public static bool MenuMusicEnabled => PlayerPrefs.GetInt(MenuMusicEnabledKey, 1) == 1;
    public static float MenuMusicVolume => Mathf.Clamp01(PlayerPrefs.GetFloat(MenuMusicVolumeKey, DefaultMusicVolume));
    public static bool GameMusicEnabled => PlayerPrefs.GetInt(GameMusicEnabledKey, 1) == 1;
    public static float GameMusicVolume => Mathf.Clamp01(PlayerPrefs.GetFloat(GameMusicVolumeKey, DefaultMusicVolume));

    public static void SetMenuMusicEnabled(bool enabled)
    {
        PlayerPrefs.SetInt(MenuMusicEnabledKey, enabled ? 1 : 0);
        // The manager stops/fades the source but never touches the saved volume.
        if (MenuMusicManager.Instance != null)
            MenuMusicManager.Instance.SetMusicEnabled(enabled);
        MarkDirty();
    }

    public static void SetMenuMusicVolume(float volume)
    {
        float safe = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(MenuMusicVolumeKey, safe);
        if (MenuMusicEnabled && MenuMusicManager.Instance != null)
            MenuMusicManager.Instance.SetVolume(safe);
        MarkDirty();
    }

    public static void SetGameMusicEnabled(bool enabled)
    {
        PlayerPrefs.SetInt(GameMusicEnabledKey, enabled ? 1 : 0);
        if (MusicManager.Instance != null)
            MusicManager.Instance.SetMusicEnabled(enabled);
        MarkDirty();
    }

    public static void SetGameMusicVolume(float volume)
    {
        float safe = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(GameMusicVolumeKey, safe);
        if (GameMusicEnabled && MusicManager.Instance != null)
            MusicManager.Instance.SetVolume(safe);
        MarkDirty();
    }

    // ------------------------------------------------------------------ Gameplay

    public static bool AutosaveEnabled => PlayerPrefs.GetInt(AutosaveEnabledKey, 1) == 1;
    public static int AutosaveInterval => Mathf.Clamp(PlayerPrefs.GetInt(AutosaveIntervalKey, DefaultAutosaveInterval), 1, 10);
    public static float CameraPanMultiplier => Mathf.Clamp(PlayerPrefs.GetFloat(PanKey, 1f), MinCameraMultiplier, MaxCameraMultiplier);
    public static float CameraZoomMultiplier => Mathf.Clamp(PlayerPrefs.GetFloat(ZoomKey, 1f), MinCameraMultiplier, MaxCameraMultiplier);

    public static void SetAutosaveEnabled(bool enabled)
    {
        PlayerPrefs.SetInt(AutosaveEnabledKey, enabled ? 1 : 0);
        MarkDirty();
        AutosaveSettingsChanged?.Invoke();
    }

    public static void SetAutosaveInterval(int turns)
    {
        PlayerPrefs.SetInt(AutosaveIntervalKey, Mathf.Clamp(turns, 1, 10));
        MarkDirty();
        AutosaveSettingsChanged?.Invoke();
    }

    public static void SetCameraPanMultiplier(float multiplier)
    {
        PlayerPrefs.SetFloat(PanKey, Mathf.Clamp(multiplier, MinCameraMultiplier, MaxCameraMultiplier));
        MarkDirty();
        CameraSettingsChanged?.Invoke();
    }

    public static void SetCameraZoomMultiplier(float multiplier)
    {
        PlayerPrefs.SetFloat(ZoomKey, Mathf.Clamp(multiplier, MinCameraMultiplier, MaxCameraMultiplier));
        MarkDirty();
        CameraSettingsChanged?.Invoke();
    }

    // ------------------------------------------------------------------ Interface

    public static TooltipDelay CurrentTooltipDelay => (TooltipDelay)Mathf.Clamp(PlayerPrefs.GetInt(TooltipDelayKey, (int)TooltipDelay.Normal), 0, 2);

    /// <summary>Seconds hover tooltips wait before appearing. Read by every tooltip presenter.</summary>
    public static float TooltipDelaySeconds
    {
        get
        {
            switch (CurrentTooltipDelay)
            {
                case TooltipDelay.Instant: return 0f;
                case TooltipDelay.Short: return 0.15f;
                default: return 0.3f;
            }
        }
    }

    public static void SetTooltipDelay(TooltipDelay delay)
    {
        PlayerPrefs.SetInt(TooltipDelayKey, (int)delay);
        MarkDirty();
        TooltipDelayChanged?.Invoke();
    }
}
