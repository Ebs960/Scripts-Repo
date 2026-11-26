using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Shared Options presentation used by both the Main Menu and the in-game Pause Menu.
///
/// It attaches to the existing (legacy) Options panel GameObject and shows the Options Menu prefab on its own
/// screen-space canvas. The layout, artwork and controls are saved in that prefab; this class fills in the
/// values from <see cref="GameOptionsSettings"/> and handles the controls. Legacy controls under the panel
/// stay wired but are made non-interactable while the overlay is visible. The BACK button calls the host's
/// existing back handler.
/// </summary>
[DisallowMultipleComponent]
public sealed class FirstDraftOptionsMenu : MonoBehaviour
{
    private const string PrefabPath = "MenuUI/Options Menu";
    private const float ConfirmSeconds = 15f;

    private static readonly Color Gold = new Color(0.88f, 0.76f, 0.48f, 1f);
    private static readonly Color TextColor = new Color(0.95f, 0.92f, 0.85f, 1f);
    private static readonly Color Secondary = new Color(0.72f, 0.76f, 0.79f, 1f);
    private static readonly Color SelectedTab = new Color(1f, 0.8f, 0.46f, 1f);

    private Action goBack;
    private string contextLabel = "";
    private GameObject overlayRoot;
    private OptionsMenuView view;
    private RectTransform content;
    private ScrollRect scroll;
    private TMP_Text contextText;
    private TMP_Text footerStatus;
    private readonly List<Action> refreshers = new List<Action>();
    private readonly List<Selectable> suppressedLegacy = new List<Selectable>();
    private int selectedTab;
    private GameObject lastSelected;
    private bool visible;

    // Display confirmation state
    private TMP_Dropdown displayModeDropdown;
    private TMP_Dropdown resolutionDropdown;
    private Button applyButton;
    private Button keepButton;
    private Button revertButton;
    private TMP_Text displayHint;
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
        if (events == null || view == null || view.tabs.Length == 0) return;
        events.SetSelectedGameObject(null);
        events.SetSelectedGameObject(view.tabs[Mathf.Clamp(selectedTab, 0, view.tabs.Length - 1)].button.gameObject);
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

    private void SetStatus(string text)
    {
        if (footerStatus != null) footerStatus.text = text;
    }

    // ------------------------------------------------------------------ bind

    private void EnsureBuilt()
    {
        if (overlayRoot != null) return;
        var prefab = Resources.Load<GameObject>(PrefabPath);
        if (prefab == null || prefab.GetComponent<OptionsMenuView>() == null)
        {
            Debug.LogError($"Missing or misconfigured options prefab at Resources/{PrefabPath}.");
            return;
        }

        overlayRoot = Instantiate(prefab);
        overlayRoot.name = "Options Overlay Canvas";
        view = overlayRoot.GetComponent<OptionsMenuView>();
        content = view.content;
        scroll = view.scroll;
        contextText = view.contextText;
        footerStatus = view.footerStatus;
        refreshers.Clear();

        view.backButton.onClick.AddListener(() =>
        {
            if (confirmationActive) RevertDisplay();
            goBack?.Invoke();
        });
        for (int i = 0; i < view.tabs.Length; i++)
        {
            int index = i;
            view.tabs[i].button.onClick.AddListener(() => SelectTab(index));
        }

        BindGraphics();
        BindAudio();
        BindGameplay();
        BindInterface();
        overlayRoot.SetActive(false);
    }

    private void SelectTab(int index)
    {
        if (view == null || view.tabs.Length == 0) return;
        if (confirmationActive) RevertDisplay();
        selectedTab = Mathf.Clamp(index, 0, view.tabs.Length - 1);
        for (int i = 0; i < view.tabs.Length; i++)
        {
            bool active = i == selectedTab;
            view.tabs[i].page.gameObject.SetActive(active);
            view.tabs[i].image.color = active ? SelectedTab : Color.white;
            view.tabs[i].label.color = active ? Gold : TextColor;
        }
        content.anchoredPosition = Vector2.zero;
    }

    // ------------------------------------------------------------------ pages

    private void BindGraphics()
    {
        string[] modes = { "Fullscreen", "Borderless", "Windowed" };
        displayModeDropdown = view.displayMode;
        BindDropdown(displayModeDropdown, modes, () => (int)pendingMode, i =>
        {
            pendingMode = (GameOptionsSettings.DisplayMode)i;
            UpdateDisplayButtons();
        });

        resolutionDropdown = view.resolution;
        BindDropdown(resolutionDropdown, new string[0], () => pendingResolutionIndex, i =>
        {
            pendingResolutionIndex = i;
            UpdateDisplayButtons();
        });

        displayHint = view.displayHint;
        applyButton = view.applyDisplay;
        keepButton = view.keepDisplay;
        revertButton = view.revertDisplay;
        applyButton.onClick.AddListener(PreviewDisplay);
        keepButton.onClick.AddListener(ConfirmDisplay);
        revertButton.onClick.AddListener(RevertDisplay);

        string[] quality = GameOptionsSettings.QualityNames;
        view.qualityRow.SetActive(quality.Length > 0);
        if (quality.Length > 0)
        {
            BindDropdown(view.quality, quality,
                () => Mathf.Clamp(GameOptionsSettings.CurrentQuality, 0, quality.Length - 1),
                GameOptionsSettings.SetQuality);
        }

        BindToggle(view.vSync, () => GameOptionsSettings.VSync, GameOptionsSettings.SetVSync);

        string[] fpsLabels = { "30 FPS", "60 FPS", "120 FPS", "Unlimited" };
        BindDropdown(view.frameLimit, fpsLabels,
            () => Mathf.Max(0, Array.IndexOf(GameOptionsSettings.FrameLimitChoices, GameOptionsSettings.FrameLimit)),
            i => GameOptionsSettings.SetFrameLimit(GameOptionsSettings.FrameLimitChoices[i]));
    }

    private void BindAudio()
    {
        BindToggle(view.menuMusic, () => GameOptionsSettings.MenuMusicEnabled, GameOptionsSettings.SetMenuMusicEnabled);
        BindSlider(view.menuMusicVolume, 0f, 1f, false, () => GameOptionsSettings.MenuMusicVolume,
            GameOptionsSettings.SetMenuMusicVolume, FormatPercent);
        BindToggle(view.gameMusic, () => GameOptionsSettings.GameMusicEnabled, GameOptionsSettings.SetGameMusicEnabled);
        BindSlider(view.gameMusicVolume, 0f, 1f, false, () => GameOptionsSettings.GameMusicVolume,
            GameOptionsSettings.SetGameMusicVolume, FormatPercent);
    }

    private void BindGameplay()
    {
        BindToggle(view.autosave, () => GameOptionsSettings.AutosaveEnabled, GameOptionsSettings.SetAutosaveEnabled);
        BindSlider(view.autosaveInterval, 1f, 10f, true, () => GameOptionsSettings.AutosaveInterval,
            v => GameOptionsSettings.SetAutosaveInterval(Mathf.RoundToInt(v)), FormatTurns);
        BindSlider(view.cameraPan, GameOptionsSettings.MinCameraMultiplier, GameOptionsSettings.MaxCameraMultiplier, false,
            () => GameOptionsSettings.CameraPanMultiplier, GameOptionsSettings.SetCameraPanMultiplier, FormatMultiplier);
        BindSlider(view.cameraZoom, GameOptionsSettings.MinCameraMultiplier, GameOptionsSettings.MaxCameraMultiplier, false,
            () => GameOptionsSettings.CameraZoomMultiplier, GameOptionsSettings.SetCameraZoomMultiplier, FormatMultiplier);
    }

    private void BindInterface()
    {
        string[] delays = { "Instant", "Short (0.15 s)", "Normal (0.3 s)" };
        BindDropdown(view.tooltipDelay, delays, () => (int)GameOptionsSettings.CurrentTooltipDelay,
            i => GameOptionsSettings.SetTooltipDelay((GameOptionsSettings.TooltipDelay)i));
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

    private void BindToggle(OptionsMenuView.ToggleRow row, Func<bool> getter, Action<bool> setter)
    {
        void Paint(bool value)
        {
            row.state.text = value ? "On" : "Off";
            row.state.color = value ? Gold : Secondary;
        }

        row.toggle.onValueChanged.AddListener(v => { Paint(v); setter(v); });
        refreshers.Add(() => { bool v = getter(); row.toggle.SetIsOnWithoutNotify(v); Paint(v); });
    }

    private void BindSlider(OptionsMenuView.SliderRow row, float min, float max, bool whole,
        Func<float> getter, Action<float> setter, Func<float, string> format)
    {
        row.slider.minValue = min;
        row.slider.maxValue = max;
        row.slider.wholeNumbers = whole;
        row.slider.onValueChanged.AddListener(v => { row.value.text = format(v); setter(v); });
        refreshers.Add(() => { float v = getter(); row.slider.SetValueWithoutNotify(v); row.value.text = format(v); });
    }

    private void BindDropdown(TMP_Dropdown dropdown, string[] optionLabels, Func<int> getter, Action<int> setter)
    {
        var options = new List<TMP_Dropdown.OptionData>(optionLabels.Length);
        foreach (string text in optionLabels) options.Add(new TMP_Dropdown.OptionData(text));
        dropdown.options = options;
        dropdown.onValueChanged.AddListener(i => setter(i));
        refreshers.Add(() =>
        {
            int count = dropdown.options.Count;
            if (count == 0) return;
            dropdown.SetValueWithoutNotify(Mathf.Clamp(getter(), 0, count - 1));
            dropdown.RefreshShownValue();
        });
    }
    // ------------------------------------------------------------------ formatting

    private static string FormatPercent(float v) => Mathf.RoundToInt(v * 100f) + "%";
    private static string FormatMultiplier(float v) => Mathf.RoundToInt(v * 100f) + "%";
    private static string FormatTurns(float v)
    {
        int turns = Mathf.RoundToInt(v);
        return turns == 1 ? "1 turn" : turns + " turns";
    }

}
