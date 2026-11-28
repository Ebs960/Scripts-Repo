// Assets/Scripts Repo/MissilePanelUI.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Panel UI for selecting a missile from a launcher's inventory and entering launch mode.
/// Opened from: city panel launch button, unit info panel launch button, or improvement context menu.
/// Attach to a persistent panel in the game's UI canvas and assign inspector references.
/// </summary>
public class MissilePanelUI : MonoBehaviour
{
    public static MissilePanelUI Instance { get; private set; }

    // ─── Inspector References ─────────────────────────────────────────────────
    [Header("Panel Root")]
    [SerializeField] private GameObject panelRoot;

    [Header("Header")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private Button closeButton;

    [Header("Missile List")]
    [Tooltip("Scrollable container for missile entry buttons.")]
    [SerializeField] private Transform missileListContainer;
    [Tooltip("Prefab for a single missile entry. Expects children: Icon(Image), Name(TMP), Range(TMP), NuclearBadge(GameObject).")]
    [SerializeField] private GameObject missileEntryPrefab;

    [Header("Selection Preview")]
    [SerializeField] private Image selectedMissileIcon;
    [SerializeField] private TextMeshProUGUI selectedMissileNameText;
    [SerializeField] private TextMeshProUGUI selectedMissileDescText;
    [SerializeField] private TextMeshProUGUI selectedMissileRangeText;
    [SerializeField] private TextMeshProUGUI selectedMissileBlastText;
    [SerializeField] private GameObject nuclearWarningLabel;

    [Header("Launch Button")]
    [SerializeField] private Button launchButton;
    [SerializeField] private TextMeshProUGUI launchButtonText;

    [Header("Missile Artwork")]
    [SerializeField] private Sprite rowArtwork;
    [SerializeField] private Sprite nuclearBadgeArtwork;
    [SerializeField] private Sprite targetingBarArtwork;
    [SerializeField] private Sprite buttonArtwork;

    // ─── Runtime State ────────────────────────────────────────────────────────
    private List<MissileData> _missiles = new List<MissileData>();
    private MissileData _selected;
    private Action<MissileData> _onConfirm;
    private bool _configured;
    private readonly List<(Image art, GameObject marker)> _rowVisuals = new List<(Image, GameObject)>();
    private Image _previewBadge;

    // ─── Lifecycle ────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        _configured = ConfigureLayout();
        if (panelRoot != null) panelRoot.SetActive(false);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (launchButton != null)
        {
            launchButton.onClick.AddListener(OnLaunchClicked);
            var buttonLabel = launchButton.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonLabel != null) buttonLabel.text = "Choose Target";
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ─── Open Overloads ───────────────────────────────────────────────────────
    /// <summary>Open the panel to launch a missile from a city.</summary>
    public void OpenForCity(City city)
    {
        if (city == null || city.storedMissiles == null || city.storedMissiles.Count == 0) return;
        Open(
            title: $"Launch Missile — {city.cityName}",
            missiles: city.storedMissiles,
            onConfirm: missile =>
            {
                Close();
                MissileLaunchMode.Instance?.BeginLaunchFromCity(city, missile);
            });
    }

    /// <summary>Open the panel to launch a missile from a combat unit.</summary>
    public void OpenForUnit(CombatUnit unit)
    {
        if (unit == null || unit.data == null || !unit.data.canStoreMissiles ||
            unit.storedMissiles == null || unit.storedMissiles.Count == 0) return;
        Open(
            title: $"Launch Missile — {unit.data.unitName}",
            missiles: unit.storedMissiles,
            onConfirm: missile =>
            {
                Close();
                MissileLaunchMode.Instance?.BeginLaunchFromUnit(unit, missile);
            });
    }

    /// <summary>Open the panel to launch a missile from a silo improvement.</summary>
    public void OpenForSilo(int siloTileIndex, int planetIndex)
    {
        var missiles = MissileManager.Instance?.GetSiloMissiles(planetIndex, siloTileIndex);
        if (missiles == null || missiles.Count == 0) return;
        Open(
            title: "Launch Missile — Silo",
            missiles: missiles,
            onConfirm: missile =>
            {
                Close();
                MissileLaunchMode.Instance?.BeginLaunchFromSilo(siloTileIndex, planetIndex, missile);
            });
    }

    // ─── Internal Show / Close ────────────────────────────────────────────────
    private void Open(string title, List<MissileData> missiles, Action<MissileData> onConfirm)
    {
        if (!_configured)
        {
            Debug.LogError("[MissilePanelUI] Cannot open the missile panel because its UI is not configured.", this);
            return;
        }
        if (MissileLaunchMode.Instance != null && MissileLaunchMode.Instance.IsActive)
            MissileLaunchMode.Instance.Cancel();
        _missiles  = new List<MissileData>(missiles);
        _onConfirm = onConfirm;
        _selected  = null;

        if (titleText != null) titleText.text = title;
        if (panelRoot != null) panelRoot.SetActive(true);

        PopulateList();
        RefreshPreview();
        if (launchButtonText != null) launchButtonText.text = "Select a missile to continue.";

        if (launchButton != null) launchButton.interactable = false;
    }

    public void Close()
    {
        _selected  = null;
        _missiles.Clear();
        _onConfirm = null;
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    // ─── List ─────────────────────────────────────────────────────────────────
    private void PopulateList()
    {
        if (missileListContainer == null || missileEntryPrefab == null) return;

        foreach (Transform child in missileListContainer)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
        _rowVisuals.Clear();

        foreach (var missile in _missiles)
        {
            if (missile == null) continue;
            var go   = Instantiate(missileEntryPrefab, missileListContainer);
            foreach (Transform child in go.transform) child.gameObject.SetActive(false);
            var hit = go.GetComponent<Image>();
            if (hit != null) hit.color = new Color(1, 1, 1, 0);
            var layout = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            layout.preferredHeight = 48;
            layout.minHeight = 48;

            var art = CreateImage("Art", go.transform, rowArtwork, Vector2.zero, new Vector2(260, 87));
            art.color = new Color(0.8f, 0.8f, 0.8f, 1);
            var icon = CreateImage("Icon", go.transform, missile.icon, new Vector2(-105, 0), new Vector2(28, 28));
            icon.enabled = missile.icon != null;
            var name = CreateText("Name", go.transform, missile.missileName, new Vector2(-18, 8), new Vector2(135, 20), 16);
            name.alignment = TextAlignmentOptions.Left;
            var range = CreateText("Range", go.transform, $"Rng {missile.range}  Blast {missile.blastRadius}", new Vector2(60, -8), new Vector2(135, 18), 12);
            var badge = CreateImage("NuclearBadge", go.transform, nuclearBadgeArtwork, new Vector2(106, 0), new Vector2(28, 28));
            badge.gameObject.SetActive(missile.isNuclear);
            var marker = CreateText("Selection", go.transform, ">", new Vector2(-128, 0), new Vector2(16, 24), 19);
            marker.color = new Color(1f, 0.8f, 0.25f);
            marker.gameObject.SetActive(false);

            var captured = missile;
            var btn = go.GetComponent<Button>() ?? go.GetComponentInChildren<Button>();
            int row = _rowVisuals.Count;
            _rowVisuals.Add((art, marker.gameObject));
            if (btn != null)
            {
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => SelectMissile(captured, row));
            }
        }
    }

    private static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        if (rect == null) return;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }

    private Image CreateImage(string name, Transform parent, Sprite sprite, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        Place((RectTransform)go.transform, position, size);
        return image;
    }

    private TextMeshProUGUI CreateText(string name, Transform parent, string text, Vector2 position, Vector2 size, float fontSize)
    {
        var label = Instantiate(selectedMissileNameText, parent, false);
        label.name = name;
        label.gameObject.SetActive(true);
        label.text = text;
        label.fontSize = fontSize;
        label.enableAutoSizing = false;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
        Place((RectTransform)label.transform, position, size);
        return label;
    }

    private bool ConfigureLayout()
    {
        if (panelRoot == null || missileListContainer == null || selectedMissileNameText == null ||
            selectedMissileIcon == null || selectedMissileDescText == null || selectedMissileRangeText == null ||
            selectedMissileBlastText == null || titleText == null || launchButtonText == null ||
            launchButton == null || closeButton == null || rowArtwork == null ||
            nuclearBadgeArtwork == null || targetingBarArtwork == null || buttonArtwork == null)
        {
            Debug.LogError("[MissilePanelUI] Missile panel artwork or required controls are missing.", this);
            return false;
        }

        var canvas = panelRoot.GetComponentInParent<Canvas>();
        var mode = GetComponentInParent<MissileLaunchMode>();
        var scroll = missileListContainer.GetComponentInParent<ScrollRect>();
        if (canvas == null || mode == null || scroll == null || scroll.viewport == null)
        {
            Debug.LogError("[MissilePanelUI] Canvas, launch mode, or missile scroll view is missing.", this);
            return false;
        }

        Place(panelRoot.GetComponent<RectTransform>(), Vector2.zero, new Vector2(960, 640));
        var shell = panelRoot.GetComponent<Image>();
        if (shell != null) { shell.preserveAspect = true; shell.material = null; }
        Place(titleText.rectTransform, new Vector2(0, 276), new Vector2(380, 40));
        Place(scroll.GetComponent<RectTransform>(), new Vector2(-304, 0), new Vector2(280, 380));
        var scrollImage = scroll.GetComponent<Image>();
        if (scrollImage != null) scrollImage.color = Color.clear;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        scroll.horizontalScrollbar = null;
        var horizontal = scroll.transform.Find("Scrollbar Horizontal");
        if (horizontal != null) horizontal.gameObject.SetActive(false);
        scroll.viewport.anchorMin = Vector2.zero;
        scroll.viewport.anchorMax = Vector2.one;
        scroll.viewport.offsetMin = new Vector2(0, 0);
        scroll.viewport.offsetMax = new Vector2(-16, 0);
        if (scroll.verticalScrollbar != null)
        {
            var bar = (RectTransform)scroll.verticalScrollbar.transform;
            bar.anchorMin = new Vector2(1, 0);
            bar.anchorMax = Vector2.one;
            bar.pivot = new Vector2(1, 0.5f);
            bar.anchoredPosition = Vector2.zero;
            bar.sizeDelta = new Vector2(14, 0);
        }
        var content = (RectTransform)missileListContainer;
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0.5f, 1);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        var group = content.GetComponent<VerticalLayoutGroup>();
        if (group != null) { group.spacing = 2; group.childForceExpandHeight = false; }
        var fitter = content.GetComponent<ContentSizeFitter>();
        if (fitter != null) { fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize; }

        var inventoryTitle = panelRoot.transform.Find("Available Missiles") as RectTransform;
        Place(inventoryTitle, new Vector2(-304, 210), new Vector2(270, 30));
        Place(selectedMissileIcon.rectTransform, new Vector2(-90, 100), new Vector2(140, 140));
        selectedMissileIcon.preserveAspect = true;
        Place(selectedMissileNameText.rectTransform, new Vector2(190, 170), new Vector2(360, 40));
        Place(selectedMissileDescText.rectTransform, new Vector2(190, 75), new Vector2(360, 140));
        Place(selectedMissileRangeText.rectTransform, new Vector2(20, -90), new Vector2(200, 30));
        Place(selectedMissileBlastText.rectTransform, new Vector2(240, -90), new Vector2(240, 30));
        var warning = nuclearWarningLabel != null ? nuclearWarningLabel.GetComponent<TextMeshProUGUI>() : null;
        if (warning != null)
        {
            Place(warning.rectTransform, new Vector2(120, -150), new Vector2(300, 30));
            warning.text = "NUCLEAR WEAPON";
        }
        _previewBadge = CreateImage("Nuclear Classification Badge", panelRoot.transform, nuclearBadgeArtwork, new Vector2(-60, -150), new Vector2(42, 42));
        Place(launchButton.transform as RectTransform, new Vector2(230, -260), new Vector2(150, 40));
        Place(closeButton.transform as RectTransform, new Vector2(390, -260), new Vector2(110, 40));
        Place(launchButtonText.rectTransform, new Vector2(-175, -260), new Vector2(430, 40));
        StyleButton(launchButton, 150);
        StyleButton(closeButton, 110);

        var hud = CreateImage("Missile Targeting HUD", canvas.transform, targetingBarArtwork, Vector2.zero, new Vector2(650, 217));
        var hudRect = hud.rectTransform;
        hudRect.anchorMin = hudRect.anchorMax = new Vector2(0.5f, 0);
        hudRect.anchoredPosition = new Vector2(0, 400);
        var hudHit = CreateImage("Hit Area", hud.transform, null, new Vector2(0, 6), new Vector2(620, 90));
        hudHit.color = Color.clear;
        hudHit.raycastTarget = true;
        var hudIcon = CreateImage("Missile Icon", hud.transform, null, new Vector2(-260, 6), new Vector2(36, 36));
        var hudName = CreateText("Missile Name", hud.transform, "", new Vector2(-182, 6), new Vector2(95, 42), 14);
        var hudInfo = CreateText("Targeting Instructions", hud.transform, "", new Vector2(0, 6), new Vector2(245, 50), 13);
        var cancel = Instantiate(closeButton, hud.transform, false);
        cancel.name = "Cancel Targeting";
        cancel.gameObject.SetActive(true);
        cancel.onClick.RemoveAllListeners();
        cancel.onClick.AddListener(mode.Cancel);
        Place(cancel.transform as RectTransform, new Vector2(260, 6), new Vector2(92, 32));
        Place(cancel.transform.Find("Art") as RectTransform, Vector2.zero, new Vector2(92, 31));
        var cancelText = cancel.GetComponentInChildren<TextMeshProUGUI>();
        if (cancelText != null) { cancelText.text = "Cancel"; cancelText.fontSize = 13; }
        hud.gameObject.SetActive(false);
        mode.ConfigureHud(hud.gameObject, hudIcon, hudName, hudInfo);
        return true;
    }

    private void StyleButton(Button button, float width)
    {
        var hit = button.GetComponent<Image>();
        if (hit != null) hit.color = Color.clear;
        var art = CreateImage("Art", button.transform, buttonArtwork, Vector2.zero, new Vector2(width, width / 3f));
        art.transform.SetAsFirstSibling();
        button.targetGraphic = art;
        var label = button.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) { label.color = Color.white; label.fontSize = 16; label.raycastTarget = false; }
    }

    // ─── Selection ────────────────────────────────────────────────────────────
    private void SelectMissile(MissileData missile, int row)
    {
        _selected = missile;
        for (int i = 0; i < _rowVisuals.Count; i++)
        {
            _rowVisuals[i].art.color = i == row ? Color.white : new Color(0.8f, 0.8f, 0.8f, 1);
            _rowVisuals[i].marker.SetActive(i == row);
        }
        RefreshPreview();
        if (launchButtonText != null) launchButtonText.text = "Choose Target to enter targeting mode.";
        if (launchButton != null) launchButton.interactable = true;
    }

    private void RefreshPreview()
    {
        if (_selected == null)
        {
            if (selectedMissileNameText != null) selectedMissileNameText.text = "Select a missile";
            if (selectedMissileDescText != null) selectedMissileDescText.text = "";
            if (selectedMissileRangeText != null) selectedMissileRangeText.text = "";
            if (selectedMissileBlastText != null) selectedMissileBlastText.text = "";
            if (selectedMissileIcon != null) { selectedMissileIcon.sprite = null; selectedMissileIcon.enabled = false; }
            if (nuclearWarningLabel != null) nuclearWarningLabel.SetActive(false);
            if (_previewBadge != null) _previewBadge.gameObject.SetActive(false);
            return;
        }

        if (selectedMissileIcon != null) { selectedMissileIcon.sprite = _selected.icon; selectedMissileIcon.enabled = _selected.icon != null; }
        if (selectedMissileNameText != null) selectedMissileNameText.text = _selected.missileName;
        if (selectedMissileDescText != null) selectedMissileDescText.text = _selected.description;
        if (selectedMissileRangeText != null) selectedMissileRangeText.text = $"Range: {_selected.range} tiles";
        if (selectedMissileBlastText != null)
            selectedMissileBlastText.text = _selected.blastRadius > 0
                ? $"Blast radius: {_selected.blastRadius} tiles"
                : "Direct impact only";
        if (nuclearWarningLabel != null) nuclearWarningLabel.SetActive(_selected.isNuclear);
        if (_previewBadge != null) _previewBadge.gameObject.SetActive(_selected.isNuclear);
    }

    // ─── Launch ───────────────────────────────────────────────────────────────
    private void OnLaunchClicked()
    {
        if (_selected == null) return;
        if (MissileLaunchMode.Instance == null)
        {
            Debug.LogError("[MissilePanelUI] Cannot choose a target: MissileLaunchMode is missing.");
            return;
        }
        _onConfirm?.Invoke(_selected);
    }
}
