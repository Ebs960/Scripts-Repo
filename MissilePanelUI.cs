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

    // ─── Runtime State ────────────────────────────────────────────────────────
    private List<MissileData> _missiles = new List<MissileData>();
    private MissileData _selected;
    private Action<MissileData> _onConfirm;

    // ─── Lifecycle ────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (panelRoot != null) panelRoot.SetActive(false);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (launchButton != null) launchButton.onClick.AddListener(OnLaunchClicked);
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
        if (unit == null || !unit.data.canStoreMissiles || unit.storedMissiles.Count == 0) return;
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
        _missiles  = new List<MissileData>(missiles);
        _onConfirm = onConfirm;
        _selected  = null;

        if (titleText != null) titleText.text = title;
        if (panelRoot != null) panelRoot.SetActive(true);

        PopulateList();
        RefreshPreview();

        if (launchButton != null) launchButton.interactable = false;
    }

    public void Close()
    {
        _selected  = null;
        _missiles.Clear();
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    // ─── List ─────────────────────────────────────────────────────────────────
    private void PopulateList()
    {
        if (missileListContainer == null || missileEntryPrefab == null) return;

        foreach (Transform child in missileListContainer) Destroy(child.gameObject);

        foreach (var missile in _missiles)
        {
            var go   = Instantiate(missileEntryPrefab, missileListContainer);
            var icon  = go.transform.Find("Icon")?.GetComponent<Image>();
            var name  = go.transform.Find("Name")?.GetComponent<TextMeshProUGUI>();
            var range = go.transform.Find("Range")?.GetComponent<TextMeshProUGUI>();
            var badge = go.transform.Find("NuclearBadge")?.gameObject;

            if (icon  != null) icon.sprite     = missile.icon;
            if (name  != null) name.text        = missile.missileName;
            if (range != null) range.text       = $"Rng {missile.range}  Blast {missile.blastRadius}";
            if (badge != null) badge.SetActive(missile.isNuclear);

            var captured = missile;
            var btn = go.GetComponent<Button>() ?? go.GetComponentInChildren<Button>();
            if (btn != null) btn.onClick.AddListener(() => SelectMissile(captured));
        }
    }

    // ─── Selection ────────────────────────────────────────────────────────────
    private void SelectMissile(MissileData missile)
    {
        _selected = missile;
        RefreshPreview();
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
            if (selectedMissileIcon != null) selectedMissileIcon.sprite = null;
            if (nuclearWarningLabel != null) nuclearWarningLabel.SetActive(false);
            return;
        }

        if (selectedMissileIcon != null) selectedMissileIcon.sprite = _selected.icon;
        if (selectedMissileNameText != null) selectedMissileNameText.text = _selected.missileName;
        if (selectedMissileDescText != null) selectedMissileDescText.text = _selected.description;
        if (selectedMissileRangeText != null) selectedMissileRangeText.text = $"Range: {_selected.range} tiles";
        if (selectedMissileBlastText != null)
            selectedMissileBlastText.text = _selected.blastRadius > 0
                ? $"Blast radius: {_selected.blastRadius} tiles"
                : "Direct impact only";
        if (nuclearWarningLabel != null) nuclearWarningLabel.SetActive(_selected.isNuclear);
    }

    // ─── Launch ───────────────────────────────────────────────────────────────
    private void OnLaunchClicked()
    {
        if (_selected == null) return;
        _onConfirm?.Invoke(_selected);
    }
}
