using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime presentation attached to the existing Options Panel, in either Main Menu or Pause Menu.
/// Keeps old serialized sliders/buttons intact under an opaque overlay rather than breaking scene overrides.
/// The same controls and persistence are used in both contexts.
/// </summary>
[DisallowMultipleComponent]
public sealed class FirstDraftOptionsMenu : MonoBehaviour
{
    private static readonly Color Outer = C("#0A111B");
    private static readonly Color Panel = C("#101C2B");
    private static readonly Color RowBackground = C("#182839");
    private static readonly Color Gold = C("#B59258");
    private static readonly Color TextColor = C("#E3E7EB");
    private static readonly Color Secondary = C("#97A6B4");
    private static readonly Color Track = C("#283A4D");

    private Action goBack;
    private RectTransform overlay;
    private RectTransform page;
    private RectTransform tabs;
    private TextMeshProUGUI footerStatus;
    private string selectedTab = "Graphics";
    private List<Vector2Int> resolutions;
    private int resolutionIndex;
    private GameOptionsSettings.DisplayMode selectedMode;
    private bool confirmationActive;
    private GameOptionsSettings.DisplayMode previousMode;
    private Vector2Int previousResolution;
    private float confirmDeadline;

    public static void Open(GameObject optionsPanel, Action back)
    {
        if (optionsPanel == null) return;
        var presenter = optionsPanel.GetComponent<FirstDraftOptionsMenu>();
        if (presenter == null) presenter = optionsPanel.AddComponent<FirstDraftOptionsMenu>();
        presenter.goBack = back;
        presenter.BuildIfNeeded();
        presenter.RefreshFromCurrentSettings();
    }

    private void BuildIfNeeded()
    {
        if (overlay != null) return;
        overlay = Rect("First Draft Options Overlay", transform);
        Stretch(overlay);
        Image(overlay.gameObject, Outer);

        var shell = Rect("Options Shell", overlay);
        shell.anchorMin = new Vector2(.09f, .09f);
        shell.anchorMax = new Vector2(.91f, .91f);
        shell.offsetMin = Vector2.zero;
        shell.offsetMax = Vector2.zero;
        Image(shell.gameObject, Panel);
        Vertical(shell.gameObject, 16, 24, true);

        var header = Rect("Header", shell);
        Height(header.gameObject, 68);
        Horizontal(header.gameObject, 0, 4);
        var title = Label(header, "OPTIONS", 35, Gold);
        Flex(title.gameObject, 1, 0);
        Label(header, "CAVEMAN TO COSMOS", 17, Secondary, TextAlignmentOptions.MidlineRight);

        var body = Rect("Body", shell);
        Flex(body.gameObject, 1, 1);
        Horizontal(body.gameObject, 16, 0);

        tabs = Rect("Tabs", body);
        Width(tabs.gameObject, 192);
        Image(tabs.gameObject, C("#0E1825"));
        Vertical(tabs.gameObject, 12, 12, false);

        page = Rect("Settings", body);
        Flex(page.gameObject, 1, 1);
        Image(page.gameObject, C("#142133"));
        Vertical(page.gameObject, 12, 18, false);

        var footer = Rect("Footer", shell);
        Height(footer.gameObject, 56);
        Horizontal(footer.gameObject, 10, 0);
        footerStatus = Label(footer, "Settings are saved automatically", 17, Secondary);
        Flex(footerStatus.gameObject, 1, 0);
        MakeButton(footer, "BACK", () =>
        {
            if (confirmationActive) RevertDisplay();
            goBack?.Invoke();
        }, 140, Gold);

        var rootCanvas = GetComponent<Canvas>();
        if (rootCanvas != null)
        {
            var scaler = GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = .5f;
            }
        }
    }

    private void RefreshFromCurrentSettings()
    {
        if (confirmationActive) RevertDisplay();
        selectedMode = GameOptionsSettings.CurrentDisplayMode;
        previousResolution = GameOptionsSettings.CurrentResolution;
        resolutions = GameOptionsSettings.AvailableResolutions();
        resolutionIndex = Mathf.Max(0, resolutions.FindIndex(r => r == previousResolution));
        DrawTabs();
        DrawPage();
        overlay.SetAsLastSibling();
    }

    private void DrawTabs()
    {
        Clear(tabs);
        foreach (string name in new[] { "Graphics", "Audio", "Gameplay" })
        {
            string currentName = name;
            MakeButton(tabs, name, () =>
            {
                if (confirmationActive) RevertDisplay();
                selectedTab = currentName;
                DrawTabs();
                DrawPage();
            }, 0, selectedTab == name ? Gold : Track);
        }
    }

    private void DrawPage()
    {
        Clear(page);
        Header(selectedTab);
        switch (selectedTab)
        {
            case "Graphics": GraphicsPage(); break;
            case "Audio": AudioPage(); break;
            default: GameplayPage(); break;
        }
    }

    private void Header(string heading)
    {
        var text = Label(page, heading + " Settings", 28, Gold);
        Height(text.gameObject, 38);
    }

    private void GraphicsPage()
    {
        string[] modes = { "Fullscreen", "Borderless", "Windowed" };
        Stepper("Display Mode", modes[(int)selectedMode], () =>
        {
            selectedMode = (GameOptionsSettings.DisplayMode)(((int)selectedMode + 2) % 3);
            DrawPage();
        }, () =>
        {
            selectedMode = (GameOptionsSettings.DisplayMode)(((int)selectedMode + 1) % 3);
            DrawPage();
        });

        Vector2Int resolution = resolutions.Count > 0 ? resolutions[resolutionIndex] : GameOptionsSettings.CurrentResolution;
        Stepper("Resolution", resolution.x + " x " + resolution.y, () =>
        {
            resolutionIndex = (resolutionIndex + resolutions.Count - 1) % resolutions.Count;
            DrawPage();
        }, () =>
        {
            resolutionIndex = (resolutionIndex + 1) % resolutions.Count;
            DrawPage();
        });

        string[] quality = GameOptionsSettings.QualityNames;
        if (quality.Length > 0)
        {
            int selected = Mathf.Clamp(GameOptionsSettings.CurrentQuality, 0, quality.Length - 1);
            Stepper("Graphics Quality", quality[selected], () =>
            {
                GameOptionsSettings.SetQuality((selected + quality.Length - 1) % quality.Length);
                DrawPage();
            }, () =>
            {
                GameOptionsSettings.SetQuality((selected + 1) % quality.Length);
                DrawPage();
            });
        }

        ToggleRow("Vertical Sync", GameOptionsSettings.VSync, GameOptionsSettings.SetVSync);
        int[] frames = { 30, 60, 120, 0 };
        string[] fpsLabels = { "30 FPS", "60 FPS", "120 FPS", "Unlimited" };
        int index = Array.IndexOf(frames, GameOptionsSettings.FrameLimit);
        if (index < 0) index = 3;
        int frameIndex = index;
        Stepper("Frame Rate Limit", fpsLabels[frameIndex], () =>
        {
            GameOptionsSettings.SetFrameLimit(frames[(frameIndex + frames.Length - 1) % frames.Length]);
            DrawPage();
        }, () =>
        {
            GameOptionsSettings.SetFrameLimit(frames[(frameIndex + 1) % frames.Length]);
            DrawPage();
        });

        var actionRow = Rect("Display actions", page);
        Height(actionRow.gameObject, 52);
        Horizontal(actionRow.gameObject, 10, 0);
        if (confirmationActive)
        {
            MakeButton(actionRow, "KEEP CHANGES", ConfirmDisplay, 200, Gold);
            MakeButton(actionRow, "REVERT", RevertDisplay, 140, Track);
        }
        else
        {
            MakeButton(actionRow, "APPLY DISPLAY CHANGES", PreviewDisplay, 260, Gold);
        }
        Label(page, "VSync may override the FPS limit. Quality choices reflect the project's real Unity quality levels.", 16, Secondary);
        if (footerStatus != null && !confirmationActive)
            footerStatus.text = "Settings are saved automatically; display changes require confirmation";
    }

    private void AudioPage()
    {
        ToggleRow("Play Menu Music", GameOptionsSettings.MenuMusicEnabled, GameOptionsSettings.SetMenuMusicEnabled);
        SliderRow("Menu Music Volume", GameOptionsSettings.MenuMusicVolume, 0, 1, false,
            x => GameOptionsSettings.SetMenuMusicVolume(x), true);
        ToggleRow("Play Gameplay Music", GameOptionsSettings.GameMusicEnabled, GameOptionsSettings.SetGameMusicEnabled);
        SliderRow("Gameplay Music Volume", GameOptionsSettings.GameMusicVolume, 0, 1, false,
            x => GameOptionsSettings.SetGameMusicVolume(x), true);
        Label(page, "Sound-effects volume will be added after all sound sources use a common mixer.", 16, Secondary);
        footerStatus.text = "Audio preferences are saved automatically";
    }

    private void GameplayPage()
    {
        ToggleRow("Enable Autosave", GameOptionsSettings.AutosaveEnabled, GameOptionsSettings.SetAutosaveEnabled);
        SliderRow("Autosave Interval", GameOptionsSettings.AutosaveInterval, 1, 10, true,
            x => GameOptionsSettings.SetAutosaveInterval(Mathf.RoundToInt(x)), false, " turns");
        SliderRow("Camera Movement Speed", GameOptionsSettings.CameraPanMultiplier, .5f, 2f, false,
            GameOptionsSettings.SetCameraPanMultiplier, false, "x");
        SliderRow("Camera Zoom Sensitivity", GameOptionsSettings.CameraZoomMultiplier, .5f, 2f, false,
            GameOptionsSettings.SetCameraZoomMultiplier, false, "x");
        Label(page, "World generation and game rules remain in the New Game setup screen.", 16, Secondary);
        footerStatus.text = "Gameplay preferences are saved automatically";
    }

    private void ToggleRow(string name, bool value, Action<bool> onChange)
    {
        var row = NewRow(name);
        MakeButton(row, value ? "ON" : "OFF", () =>
        {
            if (confirmationActive) return; // lock controls during display confirmation
            onChange(!value);
            DrawPage();
        }, 155, value ? Gold : Track);
    }

    private void Stepper(string name, string value, Action left, Action right)
    {
        var row = NewRow(name);
        var controls = Rect("Choice", row);
        Width(controls.gameObject, 350);
        Horizontal(controls.gameObject, 8, 0);
        MakeButton(controls, "<", () => { if (!confirmationActive) left(); }, 46, Track);
        var selection = Label(controls, value, 21, TextColor, TextAlignmentOptions.Center);
        Flex(selection.gameObject, 1, 0);
        MakeButton(controls, ">", () => { if (!confirmationActive) right(); }, 46, Track);
    }

    private void SliderRow(string name, float initial, float min, float max,
        bool whole, Action<float> onChange, bool percent, string suffix = "")
    {
        var row = NewRow(name);
        var controls = Rect("Slider Controls", row);
        Width(controls.gameObject, 350);
        Horizontal(controls.gameObject, 12, 0);

        var bar = Rect("Bar", controls);
        Flex(bar.gameObject, 1, 0);
        Image(bar.gameObject, Track);
        var slider = bar.gameObject.AddComponent<Slider>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = whole;

        var fill = Rect("Fill", bar);
        Stretch(fill);
        fill.offsetMin = new Vector2(4, 3);
        fill.offsetMax = new Vector2(-4, -3);
        Image(fill.gameObject, Gold);
        slider.fillRect = fill;

        var handleArea = Rect("Handle Slide Area", bar);
        Stretch(handleArea);
        handleArea.offsetMin = new Vector2(6, 0);
        handleArea.offsetMax = new Vector2(-6, 0);
        var handle = Rect("Handle", handleArea);
        handle.sizeDelta = new Vector2(12, 25);
        Image(handle.gameObject, TextColor);
        slider.handleRect = handle;
        slider.targetGraphic = handle.GetComponent<Image>();

        var valueLabel = Label(controls, FormatValue(initial, percent, whole, suffix), 18, Gold,
            TextAlignmentOptions.MidlineRight);
        Width(valueLabel.gameObject, 88);

        slider.SetValueWithoutNotify(initial);
        slider.onValueChanged.AddListener(v =>
        {
            valueLabel.text = FormatValue(v, percent, whole, suffix);
            onChange(v);
        });
    }

    private static string FormatValue(float value, bool percent, bool whole, string suffix)
    {
        if (percent) return Mathf.RoundToInt(value * 100f) + "%";
        return whole ? Mathf.RoundToInt(value) + suffix : value.ToString("0.00") + suffix;
    }

    private RectTransform NewRow(string label)
    {
        var row = Rect(label + " Row", page);
        Height(row.gameObject, 66);
        Image(row.gameObject, RowBackground);
        Horizontal(row.gameObject, 18, 14);
        var title = Label(row, label, 21, TextColor);
        Flex(title.gameObject, 1, 0);
        return row;
    }

    private void PreviewDisplay()
    {
        if (confirmationActive || resolutions.Count == 0) return;
#if UNITY_EDITOR
        footerStatus.text = "Test display changes in a standalone build (Editor resolution unchanged).";
        return;
#endif
        previousMode = GameOptionsSettings.CurrentDisplayMode;
        previousResolution = GameOptionsSettings.CurrentResolution;
        confirmationActive = true;
        confirmDeadline = Time.unscaledTime + 15f;
        GameOptionsSettings.ApplyDisplayTemporarily(selectedMode, resolutions[resolutionIndex]);
        DrawPage();
    }

    private void ConfirmDisplay()
    {
        if (!confirmationActive) return;
        confirmationActive = false;
        if (resolutions.Count > 0)
            GameOptionsSettings.SaveConfirmedDisplay(selectedMode, resolutions[resolutionIndex]);
        footerStatus.text = "Display preferences saved";
        DrawPage();
    }

    private void RevertDisplay()
    {
        if (!confirmationActive) return;
        confirmationActive = false;
        GameOptionsSettings.ApplyDisplayTemporarily(previousMode, previousResolution);
        selectedMode = previousMode;
        resolutionIndex = Mathf.Max(0, resolutions.FindIndex(r => r == previousResolution));
        if (page != null && gameObject.activeInHierarchy) DrawPage();
    }

    private void Update()
    {
        if (!confirmationActive) return;
        if (Time.unscaledTime >= confirmDeadline)
        {
            RevertDisplay();
            return;
        }
        if (footerStatus != null)
            footerStatus.text = "Keep display changes? Reverting in " +
                                Mathf.CeilToInt(confirmDeadline - Time.unscaledTime) + "s";
    }

    private void OnDisable()
    {
        // Do not leave an unconfirmed display mode active when changing scenes or closing options.
        if (confirmationActive) RevertDisplay();
    }

    private static void Clear(Transform t)
    {
        for (int i = t.childCount - 1; i >= 0; --i)
        {
            var child = t.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static Image Image(GameObject go, Color color)
    {
        var image = go.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static TextMeshProUGUI Label(Transform parent, string value, float fontSize,
        Color color, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
    {
        var rect = Rect(value, parent);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    private static Button MakeButton(Transform parent, string label, Action action, float width, Color fill)
    {
        var rect = Rect(label + " Button", parent);
        if (width > 0) Width(rect.gameObject, width);
        Height(rect.gameObject, 45);
        var image = Image(rect.gameObject, fill);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => action?.Invoke());
        var text = Label(rect, label, 18, fill == Gold ? Outer : TextColor,
            TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        return button;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void Height(GameObject go, float height)
    {
        var layout = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        layout.minHeight = height;
        layout.preferredHeight = height;
    }

    private static void Width(GameObject go, float width)
    {
        var layout = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        layout.minWidth = width;
        layout.preferredWidth = width;
    }

    private static void Flex(GameObject go, float width, float height)
    {
        var layout = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        layout.flexibleWidth = width;
        layout.flexibleHeight = height;
    }

    private static void Vertical(GameObject go, float spacing, int pad, bool expandHeight)
    {
        var group = go.AddComponent<VerticalLayoutGroup>();
        group.padding = new RectOffset(pad, pad, pad, pad);
        group.spacing = spacing;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = expandHeight;
    }

    private static void Horizontal(GameObject go, float spacing, int pad)
    {
        var group = go.AddComponent<HorizontalLayoutGroup>();
        group.padding = new RectOffset(pad, pad, pad, pad);
        group.spacing = spacing;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = false;
        group.childForceExpandHeight = true;
    }

    private static Color C(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out var color);
        return color;
    }
}
