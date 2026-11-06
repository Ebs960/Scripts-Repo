using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// First-draft options shared by the main menu and in-game pause menu.
/// Uses existing music preference keys so saved audio settings are not lost.
/// Graphics are applied on startup; display changes are confirmed by the UI before being saved.
/// </summary>
public static class GameOptionsSettings
{
    private const string DisplayModeKey = "Options.DisplayMode.v1";
    private const string WidthKey = "Options.Width.v1";
    private const string HeightKey = "Options.Height.v1";
    private const string QualityKey = "Options.Quality.v1";
    private const string VsyncKey = "Options.VSync.v1";
    private const string FpsKey = "Options.FrameLimit.v1";
    private const string PanKey = "Options.CameraPan.v1";
    private const string ZoomKey = "Options.CameraZoom.v1";

    public enum DisplayMode { ExclusiveFullscreen = 0, Borderless = 1, Windowed = 2 }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplySavedGraphicsAtStartup()
    {
        if (PlayerPrefs.HasKey(QualityKey))
            SetQuality(PlayerPrefs.GetInt(QualityKey, QualitySettings.GetQualityLevel()));
        SetVSync(PlayerPrefs.GetInt(VsyncKey, 1) != 0);
        SetFrameLimit(PlayerPrefs.GetInt(FpsKey, 60));
        if (PlayerPrefs.HasKey(DisplayModeKey) && PlayerPrefs.HasKey(WidthKey) && PlayerPrefs.HasKey(HeightKey))
        {
            ApplyDisplayTemporarily(
                (DisplayMode)Mathf.Clamp(PlayerPrefs.GetInt(DisplayModeKey), 0, 2),
                new Vector2Int(PlayerPrefs.GetInt(WidthKey), PlayerPrefs.GetInt(HeightKey)));
        }
    }

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
    public static bool VSync => QualitySettings.vSyncCount > 0;
    public static int FrameLimit => Application.targetFrameRate <= 0 ? 0 : Application.targetFrameRate;
    public static bool MenuMusicEnabled => PlayerPrefs.GetInt("MenuMusicEnabled", 1) == 1;
    public static float MenuMusicVolume => Mathf.Clamp01(PlayerPrefs.GetFloat("MenuMusicVolume", 0.75f));
    public static bool GameMusicEnabled => PlayerPrefs.GetInt("MusicEnabled", 1) == 1;
    public static float GameMusicVolume => Mathf.Clamp01(PlayerPrefs.GetFloat("GameMusicVolume", 0.75f));
    public static bool AutosaveEnabled => PlayerPrefs.GetInt("AutosaveEnabled", 1) == 1;
    public static int AutosaveInterval => Mathf.Clamp(PlayerPrefs.GetInt("AutosaveInterval", 3), 1, 10);
    public static float CameraPanMultiplier => Mathf.Clamp(PlayerPrefs.GetFloat(PanKey, 1f), 0.5f, 2f);
    public static float CameraZoomMultiplier => Mathf.Clamp(PlayerPrefs.GetFloat(ZoomKey, 1f), 0.5f, 2f);

    public static List<Vector2Int> AvailableResolutions()
    {
        var list = new List<Vector2Int>();
        var seen = new HashSet<long>();
        var current = CurrentResolution;
        AddResolution(list, seen, current.x, current.y);
        foreach (Resolution r in Screen.resolutions)
            AddResolution(list, seen, r.width, r.height);
        list.Sort((a, b) =>
        {
            int width = a.x.CompareTo(b.x);
            return width != 0 ? width : a.y.CompareTo(b.y);
        });
        return list;
    }

    private static void AddResolution(List<Vector2Int> list, HashSet<long> seen, int w, int h)
    {
        if (w < 640 || h < 480) return;
        long key = ((long)w << 32) | (uint)h;
        if (seen.Add(key)) list.Add(new Vector2Int(w, h));
    }

    // Display settings are NOT persisted here. The settings panel provides a 15-second confirmation.
    public static void ApplyDisplayTemporarily(DisplayMode mode, Vector2Int resolution)
    {
        int w = Mathf.Max(640, resolution.x);
        int h = Mathf.Max(480, resolution.y);
#if !UNITY_EDITOR
        FullScreenMode fullMode = FullScreenMode.FullScreenWindow;
        if (mode == DisplayMode.ExclusiveFullscreen) fullMode = FullScreenMode.ExclusiveFullScreen;
        if (mode == DisplayMode.Windowed) fullMode = FullScreenMode.Windowed;
        Screen.SetResolution(w, h, fullMode);
#else
        Debug.Log("[GameOptionsSettings] Display-mode preview is available in standalone builds; Editor Game view resolution is unchanged.");
#endif
    }

    public static void SaveConfirmedDisplay(DisplayMode mode, Vector2Int resolution)
    {
        PlayerPrefs.SetInt(DisplayModeKey, (int)mode);
        PlayerPrefs.SetInt(WidthKey, resolution.x);
        PlayerPrefs.SetInt(HeightKey, resolution.y);
        PlayerPrefs.Save();
    }

    public static void SetQuality(int index)
    {
        if (QualitySettings.names.Length == 0) return;
        int safe = Mathf.Clamp(index, 0, QualitySettings.names.Length - 1);
        QualitySettings.SetQualityLevel(safe, true);
        PlayerPrefs.SetInt(QualityKey, safe);
        PlayerPrefs.Save();
    }

    public static void SetVSync(bool enabled)
    {
        QualitySettings.vSyncCount = enabled ? 1 : 0;
        PlayerPrefs.SetInt(VsyncKey, enabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    public static void SetFrameLimit(int fps)
    {
        int safe = fps == 30 || fps == 60 || fps == 120 ? fps : 0;
        Application.targetFrameRate = safe == 0 ? -1 : safe;
        PlayerPrefs.SetInt(FpsKey, safe);
        PlayerPrefs.Save();
    }

    public static void SetMenuMusicEnabled(bool enabled)
    {
        PlayerPrefs.SetInt("MenuMusicEnabled", enabled ? 1 : 0);
        if (MenuMusicManager.Instance != null)
            MenuMusicManager.Instance.SetMusicEnabled(enabled);
        PlayerPrefs.Save();
    }

    public static void SetMenuMusicVolume(float volume)
    {
        float safe = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat("MenuMusicVolume", safe);
        if (MenuMusicEnabled && MenuMusicManager.Instance != null)
            MenuMusicManager.Instance.SetVolume(safe);
        PlayerPrefs.Save();
    }

    public static void SetGameMusicEnabled(bool enabled)
    {
        PlayerPrefs.SetInt("MusicEnabled", enabled ? 1 : 0);
        if (MusicManager.Instance != null)
            MusicManager.Instance.SetMusicEnabled(enabled);
        PlayerPrefs.Save();
    }

    public static void SetGameMusicVolume(float volume)
    {
        float safe = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat("GameMusicVolume", safe);
        if (GameMusicEnabled && MusicManager.Instance != null)
            MusicManager.Instance.SetVolume(safe);
        PlayerPrefs.Save();
    }

    public static void SetAutosaveEnabled(bool enabled)
    {
        PlayerPrefs.SetInt("AutosaveEnabled", enabled ? 1 : 0);
        var manager = UnityEngine.Object.FindAnyObjectByType<PauseMenuManager>();
        if (manager != null) manager.SetAutosaveEnabled(enabled);
        PlayerPrefs.Save();
    }

    public static void SetAutosaveInterval(int turns)
    {
        int safe = Mathf.Clamp(turns, 1, 10);
        PlayerPrefs.SetInt("AutosaveInterval", safe);
        var manager = UnityEngine.Object.FindAnyObjectByType<PauseMenuManager>();
        if (manager != null) manager.SetAutosaveInterval(safe);
        PlayerPrefs.Save();
    }

    public static void SetCameraPanMultiplier(float multiplier)
    {
        PlayerPrefs.SetFloat(PanKey, Mathf.Clamp(multiplier, 0.5f, 2f));
        ApplyCameraSettings();
        PlayerPrefs.Save();
    }

    public static void SetCameraZoomMultiplier(float multiplier)
    {
        PlayerPrefs.SetFloat(ZoomKey, Mathf.Clamp(multiplier, 0.5f, 2f));
        ApplyCameraSettings();
        PlayerPrefs.Save();
    }

    private static void ApplyCameraSettings()
    {
        foreach (var camera in UnityEngine.Object.FindObjectsByType<PlanetaryCameraManager>(FindObjectsSortMode.None))
            camera.ApplyOptionsSpeedMultipliers(CameraPanMultiplier, CameraZoomMultiplier);
    }
}
