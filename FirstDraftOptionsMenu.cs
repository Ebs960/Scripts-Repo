using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Shared Options presentation used by both the Main Menu and the in-game Pause Menu.
///
/// It attaches to the existing (legacy) Options panel GameObject and shows a code-built overlay on its own
/// screen-space canvas (1920x1080 scale-with-screen-size) so legacy canvases keep their scaling. Legacy
/// controls under the panel stay serialized and wired but are made non-interactable while the overlay is
/// visible. Widgets are built once; opening again only refreshes their values from
/// <see cref="GameOptionsSettings"/>. The BACK button calls the host's existing back handler.
/// </summary>
[DisallowMultipleComponent]
public sealed class FirstDraftOptionsMenu : MonoBehaviour
{
    private const int OverlaySortingOrder = 900;
    private const float ConfirmSeconds = 15f;
    private static readonly Vector2 ShellMaxSize = new Vector2(1560f, 880f);

    private static readonly Color Backdrop = new Color(0.039f, 0.059f, 0.078f, 0.97f);   // #0A0F14
    private static readonly Color Shell = new Color(0.051f, 0.106f, 0.169f, 1f);         // #0D1B2B
    private static readonly Color Panel = new Color(0.063f, 0.122f, 0.192f, 1f);
    private static readonly Color Row = new Color(0.082f, 0.149f, 0.227f, 1f);
    private static readonly Color Track = new Color(0.133f, 0.204f, 0.290f, 1f);
    private static readonly Color Gold = new Color(0.784f, 0.573f, 0.208f, 1f);          // #C89235
    private static readonly Color MutedGold = new Color(0.647f, 0.463f, 0.227f, 1f);     // #A5763A
    private static readonly Color TextColor = new Color(0.929f, 0.902f, 0.839f, 1f);     // warm off-white
    private static readonly Color Secondary = new Color(0.592f, 0.651f, 0.706f, 1f);
    private static readonly Color OnGold = new Color(0.039f, 0.059f, 0.078f, 1f);

    private sealed class Tab
    {
        public string Name;
        public Button Button;
        public Image Image;
        public TextMeshProUGUI Label;
        public RectTransform Page;
    }

    private Action goBack;
    private string contextLabel = "";
    private GameObject overlayRoot;
    private RectTransform overlayRect;
    private RectTransform shell;
    private RectTransform tabColumn;
    private RectTransform content;
    private ScrollRect scroll;
    private TextMeshProUGUI contextText;
    private TextMeshProUGUI footerStatus;
    private readonly List<Tab> tabs = new List<Tab>();
    private readonly List<Action> refreshers = new List<Action>();
    private readonly List<Selectable> suppressedLegacy = new List<Selectable>();
    private int selectedTab;
    private TMP_FontAsset titleFont;
    private TMP_FontAsset bodyFont;
    private Sprite checkmarkSprite;
    private Sprite arrowSprite;
    private Sprite knobSprite;
    private Vector2Int lastScreenSize;
    private GameObject lastSelected;
    private bool visible;

    // Display confirmation state
    private TMP_Dropdown displayModeDropdown;
    private TMP_Dropdown resolutionDropdown;
    private Button applyButton;
    private Button keepButton;
    private Button revertButton;
    private TextMeshProUGUI displayHint;
    private List<Vector2Int> resolutions = new List<Vector2Int>();
    private GameOptionsSettings.DisplayMode pendingMode;
    private int pendingResolutionIndex;
    private bool confirmationActive;
    private GameOptionsSettings.DisplayMode previousMode;
    private Vector2Int previousResolution;
    private float confirmDeadline;
    private int lastCountdown = -1;

    /// <summary>Opens the shared options overlay on top of the given (already active) legacy options panel.</summary>
    public static void Open(GameObject optionsPanel, Action back, string context = null)
    {
        if (optionsPanel == null) return;
        var presenter = optionsPanel.GetComponent<FirstDraftOptionsMenu>();
        if (presenter == null) presenter = optionsPanel.AddComponent<FirstDraftOptionsMenu>();
        presenter.goBack = back;
        presenter.contextLabel = context ?? "";
        presenter.EnsureBuilt();
        presenter.Show();
        if (presenter.contextText != null) presenter.contextText.text = presenter.contextLabel;
    }

    private void OnEnable()
    {
        if (overlayRoot != null) Show();
    }

    private void OnDisable()
    {
        Hide();
    }

    private void OnDestroy()
    {
        if (overlayRoot != null) Destroy(overlayRoot);
    }

    // ------------------------------------------------------------------ show / hide

    private void Show()
    {
        if (overlayRoot == null) return;
        if (visible) { RefreshAll(); return; }
        visible = true;
        SuppressLegacyControls();
        overlayRoot.SetActive(true);
        if (contextText != null) contextText.text = contextLabel;
        RefreshAll();
        SelectTab(selectedTab);
        FitShell();
        FocusFirstControl();
    }

    private void Hide()
    {
        if (!visible) return;
        visible = false;
        if (confirmationActive) RevertDisplay();
        RestoreLegacyControls();
        if (overlayRoot != null) overlayRoot.SetActive(false);
        GameOptionsSettings.Flush();
    }

    private void RefreshAll()
    {
        if (confirmationActive) RevertDisplay();
        resolutions = GameOptionsSettings.AvailableResolutions();
        pendingMode = GameOptionsSettings.CurrentDisplayMode;
        pendingResolutionIndex = Mathf.Max(0, resolutions.IndexOf(GameOptionsSettings.CurrentResolution));
        RebuildResolutionOptions();
        foreach (var refresh in refreshers) refresh();
        UpdateDisplayButtons();
        SetStatus("Settings are saved automatically; display changes require confirmation.");
    }

    private void FocusFirstControl()
    {
        var events = EventSystem.current;
        if (events == null || tabs.Count == 0) return;
        events.SetSelectedGameObject(null);
        events.SetSelectedGameObject(tabs[Mathf.Clamp(selectedTab, 0, tabs.Count - 1)].Button.gameObject);
    }

    // Legacy sliders/toggles/buttons beneath the overlay must not be reachable via controller navigation.
    private void SuppressLegacyControls()
    {
        suppressedLegacy.Clear();
        foreach (var selectable in GetComponentsInChildren<Selectable>(true))
        {
            if (selectable == null || !selectable.interactable) continue;
            selectable.interactable = false;
            suppressedLegacy.Add(selectable);
        }
    }

    private void RestoreLegacyControls()
    {
        foreach (var selectable in suppressedLegacy)
            if (selectable != null) selectable.interactable = true;
        suppressedLegacy.Clear();
    }

    private void Update()
    {
        if (!visible) return;

        if (Screen.width != lastScreenSize.x || Screen.height != lastScreenSize.y)
            FitShell();

        if (confirmationActive)
        {
            float remaining = confirmDeadline - Time.unscaledTime;
            if (remaining <= 0f)
            {
                RevertDisplay();
                SetStatus("Display changes were not confirmed and have been reverted.");
            }
            else
            {
                int seconds = Mathf.CeilToInt(remaining);
                if (seconds != lastCountdown)
                {
                    lastCountdown = seconds;
                    SetStatus("Keep these display settings? Reverting in " + seconds + " s.");
                }
            }
        }
    }

    private void LateUpdate()
    {
        if (!visible || scroll == null || EventSystem.current == null) return;
        var selected = EventSystem.current.currentSelectedGameObject;
        if (selected == lastSelected) return;
        lastSelected = selected;
        if (selected != null && selected.transform.IsChildOf(content))
            ScrollIntoView(selected.transform as RectTransform);
    }

    // Keyboard/controller navigation through a scrolled list must keep the focused row visible.
    private void ScrollIntoView(RectTransform target)
    {
        if (target == null) return;
        Canvas.ForceUpdateCanvases();
        var viewport = scroll.viewport;
        Vector3[] corners = new Vector3[4];
        target.GetWorldCorners(corners);
        Vector3 bottom = viewport.InverseTransformPoint(corners[0]);
        Vector3 top = viewport.InverseTransformPoint(corners[1]);
        Rect view = viewport.rect;
        float delta = 0f;
        if (top.y > view.yMax) delta = top.y - view.yMax;
        else if (bottom.y < view.yMin) delta = bottom.y - view.yMin;
        if (Mathf.Approximately(delta, 0f)) return;
        content.anchoredPosition -= new Vector2(0f, delta);
        scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition);
    }

    private void FitShell()
    {
        if (shell == null || overlayRect == null) return;
        Vector2 available = overlayRect.rect.size;
        if (available.x <= 0f || available.y <= 0f) return; // canvas not laid out yet; retried next Update
        lastScreenSize = new Vector2Int(Screen.width, Screen.height);
        shell.sizeDelta = new Vector2(Mathf.Min(ShellMaxSize.x, available.x - 48f), Mathf.Min(ShellMaxSize.y, available.y - 32f));
    }

    private void SetStatus(string text)
    {
        if (footerStatus != null) footerStatus.text = text;
    }

    // ------------------------------------------------------------------ build

    private void EnsureBuilt()
    {
        if (overlayRoot != null) return;
        tabs.Clear();
        refreshers.Clear();
        ResolveFonts();
        ResolveBuiltinSprites();

        overlayRoot = new GameObject("Options Overlay Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        overlayRoot.layer = gameObject.layer;
        overlayRect = overlayRoot.GetComponent<RectTransform>();
        var canvas = overlayRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = OverlaySortingOrder;
        var scaler = overlayRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        var backdrop = Rect("Backdrop", overlayRect);
        Stretch(backdrop);
        AddImage(backdrop, Backdrop).raycastTarget = true;

        shell = Rect("Options Shell", backdrop);
        shell.anchorMin = shell.anchorMax = shell.pivot = new Vector2(0.5f, 0.5f);
        shell.sizeDelta = ShellMaxSize;
        AddImage(shell, Shell);
        Vertical(shell, 12f, 26, false);

        BuildHeader();
        Separator(shell);
        BuildBody();
        Separator(shell);
        BuildFooter();

        BuildGraphicsPage(AddPage("Graphics"));
        BuildAudioPage(AddPage("Audio"));
        BuildGameplayPage(AddPage("Gameplay"));
        BuildInterfacePage(AddPage("Interface"));
        overlayRoot.SetActive(false);
    }

    private void BuildHeader()
    {
        var header = Rect("Header", shell);
        Height(header, 54f);
        Horizontal(header, 12f, 0);
        var title = Label(header, "OPTIONS", titleFont, 34f, Gold);
        Flex(title.rectTransform, 1f, 0f);
        contextText = Label(header, contextLabel, bodyFont, 16f, Secondary, TextAlignmentOptions.MidlineRight);
        Width(contextText.rectTransform, 260f);
    }

    private void BuildBody()
    {
        var body = Rect("Body", shell);
        Flex(body, 1f, 1f);
        Horizontal(body, 16f, 0);

        tabColumn = Rect("Categories", body);
        Width(tabColumn, 216f);
        AddImage(tabColumn, Panel);
        var tabLayout = Vertical(tabColumn, 6f, 10, false);
        tabLayout.childAlignment = TextAnchor.UpperCenter;

        var contentPanel = Rect("Settings", body);
        Flex(contentPanel, 1f, 1f);
        AddImage(contentPanel, Panel);

        var scrollRect = Rect("Scroll View", contentPanel);
        Stretch(scrollRect);
        scroll = scrollRect.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 32f;
        scroll.inertia = false;

        var viewport = Rect("Viewport", scrollRect);
        Stretch(viewport);
        viewport.offsetMax = new Vector2(-12f, 0f);
        viewport.gameObject.AddComponent<RectMask2D>();
        scroll.viewport = viewport;

        content = Rect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = content.offsetMax = Vector2.zero;
        Vertical(content, 10f, 18, false);
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content;

        var scrollbarRect = Rect("Scrollbar", scrollRect);
        scrollbarRect.anchorMin = new Vector2(1f, 0f);
        scrollbarRect.anchorMax = new Vector2(1f, 1f);
        scrollbarRect.pivot = new Vector2(1f, 0.5f);
        scrollbarRect.sizeDelta = new Vector2(8f, 0f);
        scrollbarRect.anchoredPosition = Vector2.zero;
        AddImage(scrollbarRect, Track);
        var scrollbar = scrollbarRect.gameObject.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        var slidingArea = Rect("Sliding Area", scrollbarRect);
        Stretch(slidingArea);
        var handle = Rect("Handle", slidingArea);
        Stretch(handle);
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = AddImage(handle, MutedGold);
        scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
    }

    private void BuildFooter()
    {
        var footer = Rect("Footer", shell);
        Height(footer, 48f);
        Horizontal(footer, 12f, 0);
        footerStatus = Label(footer, "", bodyFont, 16f, Secondary);
        footerStatus.textWrappingMode = TextWrappingModes.Normal;
        Flex(footerStatus.rectTransform, 1f, 0f);
        var back = MakeButton(footer, "BACK", () =>
        {
            if (confirmationActive) RevertDisplay();
            goBack?.Invoke();
        }, 170f, true);
        back.name = "Back Button";
    }

    private RectTransform AddPage(string name)
    {
        var tab = new Tab { Name = name };
        int index = tabs.Count;
        tab.Button = MakeButton(tabColumn, name, () => SelectTab(index), 0f, false);
        Height(tab.Button.transform as RectTransform, 46f);
        tab.Image = tab.Button.GetComponent<Image>();
        tab.Label = tab.Button.GetComponentInChildren<TextMeshProUGUI>();
        tab.Label.alignment = TextAlignmentOptions.MidlineLeft;
        tab.Label.rectTransform.offsetMin = new Vector2(16f, 0f);
        tab.Label.font = titleFont;

        tab.Page = Rect(name + " Page", content);
        Vertical(tab.Page, 8f, 0, false);
        var heading = Label(tab.Page, name.ToUpperInvariant(), titleFont, 22f, Gold);
        Height(heading.rectTransform, 34f);
        tab.Page.gameObject.SetActive(false);
        tabs.Add(tab);
        return tab.Page;
    }

    private void SelectTab(int index)
    {
        if (tabs.Count == 0) return;
        if (confirmationActive) RevertDisplay();
        selectedTab = Mathf.Clamp(index, 0, tabs.Count - 1);
        for (int i = 0; i < tabs.Count; i++)
        {
            bool active = i == selectedTab;
            tabs[i].Page.gameObject.SetActive(active);
            tabs[i].Image.color = active ? Gold : Row;
            tabs[i].Label.color = active ? OnGold : TextColor;
        }
        content.anchoredPosition = Vector2.zero;
    }

    // ------------------------------------------------------------------ pages

    private void BuildGraphicsPage(RectTransform page)
    {
        string[] modes = { "Fullscreen", "Borderless", "Windowed" };
        displayModeDropdown = DropdownRow(page, "Display Mode", modes, () => (int)pendingMode, i =>
        {
            pendingMode = (GameOptionsSettings.DisplayMode)i;
            UpdateDisplayButtons();
        });

        resolutionDropdown = DropdownRow(page, "Resolution", new string[0], () => pendingResolutionIndex, i =>
        {
            pendingResolutionIndex = i;
            UpdateDisplayButtons();
        });

        var actions = Rect("Display Actions", page);
        Height(actions, 44f);
        var actionLayout = Horizontal(actions, 10f, 0);
        actionLayout.childAlignment = TextAnchor.MiddleRight;
        displayHint = Label(actions, "", bodyFont, 15f, Secondary);
        Flex(displayHint.rectTransform, 1f, 0f);
        applyButton = MakeButton(actions, "APPLY DISPLAY CHANGES", PreviewDisplay, 270f, true);
        keepButton = MakeButton(actions, "KEEP CHANGES", ConfirmDisplay, 190f, true);
        revertButton = MakeButton(actions, "REVERT", RevertDisplay, 130f, false);

        string[] quality = GameOptionsSettings.QualityNames;
        if (quality.Length > 0)
        {
            DropdownRow(page, "Graphics Quality", quality,
                () => Mathf.Clamp(GameOptionsSettings.CurrentQuality, 0, quality.Length - 1),
                GameOptionsSettings.SetQuality);
        }

        ToggleRow(page, "Vertical Sync", () => GameOptionsSettings.VSync, GameOptionsSettings.SetVSync);

        string[] fpsLabels = { "30 FPS", "60 FPS", "120 FPS", "Unlimited" };
        DropdownRow(page, "Frame Rate Limit", fpsLabels,
            () => Mathf.Max(0, Array.IndexOf(GameOptionsSettings.FrameLimitChoices, GameOptionsSettings.FrameLimit)),
            i => GameOptionsSettings.SetFrameLimit(GameOptionsSettings.FrameLimitChoices[i]));

        Hint(page, "Quality presets are the project's real Unity/HDRP quality levels. While VSync is on, the frame rate follows the display refresh rate instead of the limit above.");
    }

    private void BuildAudioPage(RectTransform page)
    {
        ToggleRow(page, "Menu Music", () => GameOptionsSettings.MenuMusicEnabled, GameOptionsSettings.SetMenuMusicEnabled);
        SliderRow(page, "Menu Music Volume", 0f, 1f, false, () => GameOptionsSettings.MenuMusicVolume,
            GameOptionsSettings.SetMenuMusicVolume, FormatPercent);
        ToggleRow(page, "Gameplay Music", () => GameOptionsSettings.GameMusicEnabled, GameOptionsSettings.SetGameMusicEnabled);
        SliderRow(page, "Gameplay Music Volume", 0f, 1f, false, () => GameOptionsSettings.GameMusicVolume,
            GameOptionsSettings.SetGameMusicVolume, FormatPercent);
        Hint(page, "Turning music off keeps the saved volume; turning it back on restores it. A sound-effects slider will follow once all effects play through one shared mixer.");
    }

    private void BuildGameplayPage(RectTransform page)
    {
        ToggleRow(page, "Autosave", () => GameOptionsSettings.AutosaveEnabled, GameOptionsSettings.SetAutosaveEnabled);
        SliderRow(page, "Autosave Frequency", 1f, 10f, true, () => GameOptionsSettings.AutosaveInterval,
            v => GameOptionsSettings.SetAutosaveInterval(Mathf.RoundToInt(v)), FormatTurns);
        SliderRow(page, "Camera Movement Speed", GameOptionsSettings.MinCameraMultiplier, GameOptionsSettings.MaxCameraMultiplier, false,
            () => GameOptionsSettings.CameraPanMultiplier, GameOptionsSettings.SetCameraPanMultiplier, FormatMultiplier);
        SliderRow(page, "Camera Zoom Sensitivity", GameOptionsSettings.MinCameraMultiplier, GameOptionsSettings.MaxCameraMultiplier, false,
            () => GameOptionsSettings.CameraZoomMultiplier, GameOptionsSettings.SetCameraZoomMultiplier, FormatMultiplier);
        Hint(page, "Camera settings affect player panning, dragging and zooming only; scripted camera transitions are unchanged. World generation rules remain in New Game setup.");
    }

    private void BuildInterfacePage(RectTransform page)
    {
        string[] delays = { "Instant", "Short (0.15 s)", "Normal (0.3 s)" };
        DropdownRow(page, "Tooltip Delay", delays, () => (int)GameOptionsSettings.CurrentTooltipDelay,
            i => GameOptionsSettings.SetTooltipDelay((GameOptionsSettings.TooltipDelay)i));
        Hint(page, "Applies to hover tooltips across the campaign, research, government and battle screens. A UI scale option will be added once every HUD canvas uses screen-size scaling.");
    }

    // ------------------------------------------------------------------ display confirmation

    private void RebuildResolutionOptions()
    {
        if (resolutionDropdown == null) return;
        var options = new List<TMP_Dropdown.OptionData>(resolutions.Count);
        foreach (var r in resolutions) options.Add(new TMP_Dropdown.OptionData(r.x + " x " + r.y));
        resolutionDropdown.options = options;
    }

    private bool DisplayChangesPending =>
        pendingMode != GameOptionsSettings.CurrentDisplayMode ||
        (resolutions.Count > 0 && resolutions[Mathf.Clamp(pendingResolutionIndex, 0, resolutions.Count - 1)] != GameOptionsSettings.CurrentResolution);

    private void UpdateDisplayButtons()
    {
        if (applyButton == null) return;
        applyButton.gameObject.SetActive(!confirmationActive);
        applyButton.interactable = DisplayChangesPending;
        keepButton.gameObject.SetActive(confirmationActive);
        revertButton.gameObject.SetActive(confirmationActive);
        if (displayModeDropdown != null) displayModeDropdown.interactable = !confirmationActive;
        if (resolutionDropdown != null) resolutionDropdown.interactable = !confirmationActive;
        if (displayHint != null)
        {
            if (confirmationActive) displayHint.text = "Confirm within " + Mathf.CeilToInt(ConfirmSeconds) + " seconds or the previous display settings return.";
#if UNITY_EDITOR
            else displayHint.text = "Display changes take effect in standalone builds; the Editor Game view does not change.";
#else
            else displayHint.text = DisplayChangesPending ? "Changes are previewed first and must be confirmed." : "";
#endif
        }
    }

    private void PreviewDisplay()
    {
        if (confirmationActive || resolutions.Count == 0 || !DisplayChangesPending) return;
        previousMode = GameOptionsSettings.CurrentDisplayMode;
        previousResolution = GameOptionsSettings.CurrentResolution;
        confirmationActive = true;
        lastCountdown = -1;
        confirmDeadline = Time.unscaledTime + ConfirmSeconds;
        GameOptionsSettings.ApplyDisplayTemporarily(pendingMode, resolutions[pendingResolutionIndex]);
        UpdateDisplayButtons();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(keepButton.gameObject);
    }

    private void ConfirmDisplay()
    {
        if (!confirmationActive) return;
        confirmationActive = false;
        GameOptionsSettings.SaveConfirmedDisplay(pendingMode, resolutions[pendingResolutionIndex]);
        UpdateDisplayButtons();
        SetStatus("Display settings saved.");
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(applyButton.gameObject);
    }

    private void RevertDisplay()
    {
        if (!confirmationActive) return;
        confirmationActive = false;
        GameOptionsSettings.ApplyDisplayTemporarily(previousMode, previousResolution);
        pendingMode = previousMode;
        pendingResolutionIndex = Mathf.Max(0, resolutions.IndexOf(previousResolution));
        if (displayModeDropdown != null) { displayModeDropdown.SetValueWithoutNotify((int)pendingMode); displayModeDropdown.RefreshShownValue(); }
        if (resolutionDropdown != null) { resolutionDropdown.SetValueWithoutNotify(pendingResolutionIndex); resolutionDropdown.RefreshShownValue(); }
        UpdateDisplayButtons();
    }

    // ------------------------------------------------------------------ row widgets

    private RectTransform NewRow(RectTransform page, string label)
    {
        var row = Rect(label + " Row", page);
        Height(row, 54f);
        AddImage(row, Row);
        var layout = Horizontal(row, 16f, 16);
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childForceExpandHeight = false;
        var title = Label(row, label, bodyFont, 19f, TextColor);
        Flex(title.rectTransform, 1f, 0f);
        return row;
    }

    private void Hint(RectTransform page, string text)
    {
        var hint = Label(page, text, bodyFont, 14.5f, Secondary);
        hint.textWrappingMode = TextWrappingModes.Normal;
        hint.overflowMode = TextOverflowModes.Overflow;
        hint.margin = new Vector4(6f, 2f, 6f, 2f);
    }

    private void ToggleRow(RectTransform page, string label, Func<bool> getter, Action<bool> setter)
    {
        var row = NewRow(page, label);
        var controls = Rect("Toggle Controls", row);
        Width(controls, 420f);
        var layout = Horizontal(controls, 12f, 0);
        layout.childAlignment = TextAnchor.MiddleRight;
        layout.childForceExpandHeight = false;

        var state = Label(controls, "", bodyFont, 17f, Secondary, TextAlignmentOptions.MidlineRight);
        Width(state.rectTransform, 60f);

        var box = Rect("Toggle", controls);
        Width(box, 30f);
        Height(box, 30f);
        var background = AddImage(box, Track);
        var toggle = box.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = background;
        toggle.colors = InteractableColors();
        var check = Rect("Checkmark", box);
        Stretch(check);
        check.offsetMin = new Vector2(5f, 5f);
        check.offsetMax = new Vector2(-5f, -5f);
        var checkImage = AddImage(check, Gold);
        checkImage.sprite = checkmarkSprite;
        checkImage.preserveAspect = true;
        toggle.graphic = checkImage;

        void Paint(bool value)
        {
            state.text = value ? "On" : "Off";
            state.color = value ? Gold : Secondary;
        }

        toggle.onValueChanged.AddListener(v => { Paint(v); setter(v); });
        refreshers.Add(() => { bool v = getter(); toggle.SetIsOnWithoutNotify(v); Paint(v); });
    }

    private void SliderRow(RectTransform page, string label, float min, float max, bool whole,
        Func<float> getter, Action<float> setter, Func<float, string> format)
    {
        var row = NewRow(page, label);
        var controls = Rect("Slider Controls", row);
        Width(controls, 420f);
        var layout = Horizontal(controls, 14f, 0);
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childForceExpandHeight = false;

        var bar = Rect("Slider", controls);
        Flex(bar, 1f, 0f);
        Height(bar, 30f);
        var slider = bar.gameObject.AddComponent<Slider>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = whole;
        slider.colors = InteractableColors();

        var track = Rect("Background", bar);
        Stretch(track);
        track.offsetMin = new Vector2(0f, 11f);
        track.offsetMax = new Vector2(0f, -11f);
        AddImage(track, Track);

        var fillArea = Rect("Fill Area", bar);
        Stretch(fillArea);
        fillArea.offsetMin = new Vector2(0f, 11f);
        fillArea.offsetMax = new Vector2(0f, -11f);
        var fill = Rect("Fill", fillArea);
        Stretch(fill);
        AddImage(fill, MutedGold);
        slider.fillRect = fill;

        var handleArea = Rect("Handle Slide Area", bar);
        Stretch(handleArea);
        handleArea.offsetMin = new Vector2(8f, 0f);
        handleArea.offsetMax = new Vector2(-8f, 0f);
        var handle = Rect("Handle", handleArea);
        handle.sizeDelta = new Vector2(16f, 16f);
        var handleImage = AddImage(handle, TextColor);
        handleImage.sprite = knobSprite;
        slider.handleRect = handle;
        slider.targetGraphic = handleImage;

        var value = Label(controls, "", bodyFont, 17f, Gold, TextAlignmentOptions.MidlineRight);
        Width(value.rectTransform, 84f);

        slider.onValueChanged.AddListener(v => { value.text = format(v); setter(v); });
        refreshers.Add(() => { float v = getter(); slider.SetValueWithoutNotify(v); value.text = format(v); });
    }

    private TMP_Dropdown DropdownRow(RectTransform page, string label, string[] optionLabels, Func<int> getter, Action<int> setter)
    {
        var row = NewRow(page, label);
        var dropdown = MakeDropdown(row, optionLabels);
        Width(dropdown.transform as RectTransform, 420f);
        dropdown.onValueChanged.AddListener(i => setter(i));
        refreshers.Add(() =>
        {
            int count = dropdown.options.Count;
            if (count == 0) return;
            dropdown.SetValueWithoutNotify(Mathf.Clamp(getter(), 0, count - 1));
            dropdown.RefreshShownValue();
        });
        return dropdown;
    }

    private TMP_Dropdown MakeDropdown(RectTransform parent, string[] optionLabels)
    {
        var root = Rect("Dropdown", parent);
        Height(root, 36f);
        var background = AddImage(root, Track);
        var dropdown = root.gameObject.AddComponent<TMP_Dropdown>();
        dropdown.targetGraphic = background;
        dropdown.colors = InteractableColors();

        var caption = Label(root, "", bodyFont, 17f, TextColor);
        Stretch(caption.rectTransform);
        caption.rectTransform.offsetMin = new Vector2(12f, 0f);
        caption.rectTransform.offsetMax = new Vector2(-34f, 0f);
        dropdown.captionText = caption;

        var arrow = Rect("Arrow", root);
        arrow.anchorMin = arrow.anchorMax = new Vector2(1f, 0.5f);
        arrow.pivot = new Vector2(1f, 0.5f);
        arrow.anchoredPosition = new Vector2(-10f, 0f);
        arrow.sizeDelta = new Vector2(18f, 18f);
        var arrowImage = AddImage(arrow, MutedGold);
        arrowImage.sprite = arrowSprite;
        arrowImage.preserveAspect = true;

        // Template (inactive). TMP_Dropdown clones it into a sorted popup canvas when opened.
        var template = Rect("Template", root);
        template.anchorMin = new Vector2(0f, 0f);
        template.anchorMax = new Vector2(1f, 0f);
        template.pivot = new Vector2(0.5f, 1f);
        template.anchoredPosition = new Vector2(0f, -2f);
        template.sizeDelta = new Vector2(0f, 196f);
        AddImage(template, Shell);
        var templateScroll = template.gameObject.AddComponent<ScrollRect>();
        templateScroll.horizontal = false;
        templateScroll.movementType = ScrollRect.MovementType.Clamped;
        templateScroll.scrollSensitivity = 24f;

        var viewport = Rect("Viewport", template);
        Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        templateScroll.viewport = viewport;

        var listContent = Rect("Content", viewport);
        listContent.anchorMin = new Vector2(0f, 1f);
        listContent.anchorMax = new Vector2(1f, 1f);
        listContent.pivot = new Vector2(0.5f, 1f);
        listContent.sizeDelta = new Vector2(0f, 32f);
        templateScroll.content = listContent;

        var item = Rect("Item", listContent);
        item.anchorMin = new Vector2(0f, 0.5f);
        item.anchorMax = new Vector2(1f, 0.5f);
        item.sizeDelta = new Vector2(0f, 32f);
        var itemToggle = item.gameObject.AddComponent<Toggle>();
        var itemBackground = Rect("Item Background", item);
        Stretch(itemBackground);
        var itemBackgroundImage = AddImage(itemBackground, Shell);
        var itemCheck = Rect("Item Checkmark", item);
        itemCheck.anchorMin = itemCheck.anchorMax = new Vector2(0f, 0.5f);
        itemCheck.pivot = new Vector2(0f, 0.5f);
        itemCheck.anchoredPosition = new Vector2(10f, 0f);
        itemCheck.sizeDelta = new Vector2(16f, 16f);
        var itemCheckImage = AddImage(itemCheck, Gold);
        itemCheckImage.sprite = checkmarkSprite;
        itemCheckImage.preserveAspect = true;
        var itemLabel = Label(item, "", bodyFont, 17f, TextColor);
        Stretch(itemLabel.rectTransform);
        itemLabel.rectTransform.offsetMin = new Vector2(34f, 0f);
        itemLabel.rectTransform.offsetMax = new Vector2(-10f, 0f);
        itemToggle.targetGraphic = itemBackgroundImage;
        itemToggle.graphic = itemCheckImage;
        itemToggle.isOn = true;
        var itemColors = InteractableColors();
        itemColors.normalColor = Shell;
        itemColors.highlightedColor = Row;
        itemColors.selectedColor = Row;
        itemColors.pressedColor = Track;
        itemToggle.colors = itemColors;

        dropdown.template = template;
        dropdown.itemText = itemLabel;
        template.gameObject.SetActive(false);

        var options = new List<TMP_Dropdown.OptionData>(optionLabels.Length);
        foreach (string text in optionLabels) options.Add(new TMP_Dropdown.OptionData(text));
        dropdown.options = options;
        return dropdown;
    }

    private Button MakeButton(Transform parent, string label, Action action, float width, bool primary)
    {
        var rect = Rect(label + " Button", parent);
        if (width > 0f) Width(rect, width);
        Height(rect, 40f);
        var image = AddImage(rect, primary ? Gold : Row);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.colors = InteractableColors();
        button.onClick.AddListener(() => action?.Invoke());
        var text = Label(rect, label, titleFont, 16f, primary ? OnGold : TextColor, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        return button;
    }

    private static ColorBlock InteractableColors()
    {
        var colors = ColorBlock.defaultColorBlock;
        colors.normalColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        colors.highlightedColor = Color.white;
        colors.selectedColor = Color.white;
        colors.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
        colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
        colors.fadeDuration = 0.08f;
        return colors;
    }

    // ------------------------------------------------------------------ formatting

    private static string FormatPercent(float v) => Mathf.RoundToInt(v * 100f) + "%";
    private static string FormatMultiplier(float v) => Mathf.RoundToInt(v * 100f) + "%";
    private static string FormatTurns(float v)
    {
        int turns = Mathf.RoundToInt(v);
        return turns == 1 ? "1 turn" : turns + " turns";
    }

    // ------------------------------------------------------------------ assets

    // Use the fonts already present in the host menu so the overlay matches the surrounding UI.
    private void ResolveFonts()
    {
        titleFont = null;
        bodyFont = null;
        var canvas = GetComponentInParent<Canvas>();
        Transform searchRoot = canvas != null ? canvas.rootCanvas.transform : transform;
        foreach (var text in searchRoot.GetComponentsInChildren<TMP_Text>(true))
        {
            var font = text.font;
            if (font == null) continue;
            string name = font.name;
            if (titleFont == null && name.IndexOf("Cinzel", StringComparison.OrdinalIgnoreCase) >= 0) titleFont = font;
            else if (name.IndexOf("Title", StringComparison.OrdinalIgnoreCase) >= 0 && name.IndexOf("Cinzel", StringComparison.OrdinalIgnoreCase) >= 0) titleFont = font;
            if (bodyFont == null && name.IndexOf("Source Sans", StringComparison.OrdinalIgnoreCase) >= 0) bodyFont = font;
        }
        if (bodyFont == null) bodyFont = TMP_Settings.defaultFontAsset;
        if (titleFont == null) titleFont = bodyFont;
    }

    private void ResolveBuiltinSprites()
    {
        checkmarkSprite = LoadBuiltinSprite("UI/Skin/Checkmark.psd");
        arrowSprite = LoadBuiltinSprite("UI/Skin/DropdownArrow.psd");
        knobSprite = LoadBuiltinSprite("UI/Skin/Knob.psd");
    }

    private static Sprite LoadBuiltinSprite(string path)
    {
        try { return Resources.GetBuiltinResource<Sprite>(path); }
        catch (Exception) { return null; }
    }

    // ------------------------------------------------------------------ primitives

    private RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = overlayRoot != null ? overlayRoot.layer : gameObject.layer;
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static Image AddImage(RectTransform rect, Color color)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = true;
        return image;
    }

    private static TextMeshProUGUI Label(Transform parent, string value, TMP_FontAsset font, float size, Color color,
        TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    private void Separator(RectTransform parent)
    {
        var line = Rect("Separator", parent);
        Height(line, 1f);
        AddImage(line, new Color(MutedGold.r, MutedGold.g, MutedGold.b, 0.45f)).raycastTarget = false;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static LayoutElement Layout(RectTransform rect)
    {
        return rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
    }

    private static void Height(RectTransform rect, float height)
    {
        var layout = Layout(rect);
        layout.minHeight = height;
        layout.preferredHeight = height;
    }

    private static void Width(RectTransform rect, float width)
    {
        var layout = Layout(rect);
        layout.minWidth = width;
        layout.preferredWidth = width;
    }

    private static void Flex(RectTransform rect, float width, float height)
    {
        var layout = Layout(rect);
        layout.flexibleWidth = width;
        layout.flexibleHeight = height;
    }

    private static VerticalLayoutGroup Vertical(RectTransform rect, float spacing, int padding, bool expandHeight)
    {
        var group = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        group.padding = new RectOffset(padding, padding, padding, padding);
        group.spacing = spacing;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = expandHeight;
        return group;
    }

    private static HorizontalLayoutGroup Horizontal(RectTransform rect, float spacing, int padding)
    {
        var group = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        group.padding = new RectOffset(padding, padding, 0, 0);
        group.spacing = spacing;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = false;
        group.childForceExpandHeight = true;
        return group;
    }
}
