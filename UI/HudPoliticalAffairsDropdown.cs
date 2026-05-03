using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class HudPoliticalAffairsDropdown : MonoBehaviour
{
    [SerializeField] private HudDropdownButton dropdownButton;
    [SerializeField] private GameObject governorRowPrefab;
    [SerializeField] private GameObject vassalRowPrefab;
    [SerializeField] private GameObject factionRowPrefab;
    [SerializeField] private GameObject sectionHeaderPrefab;
    [SerializeField] private GameObject emptyStatePrefab;

    private Civilization currentCiv;

    private void Awake()
    {
        EnsureDropdownReference();
        Debug.Log($"[HudPoliticalAffairsDropdown] Awake on '{name}' dropdownButton={(dropdownButton != null ? dropdownButton.name : "null")}");
    }

    private void Reset()
    {
        EnsureDropdownReference();
    }

    private void OnEnable()
    {
        EnsureDropdownReference();

        if (dropdownButton != null)
            dropdownButton.SetMainClick(OpenPoliticalAffairsPanel);
    }

    private void OnValidate()
    {
        EnsureDropdownReference();
    }

    public void Bind(Civilization civ)
    {
        currentCiv = civ;
        EnsureDropdownReference();
        Debug.Log($"[HudPoliticalAffairsDropdown] Bind civ={(currentCiv != null ? currentCiv.civData?.civName : "null")} dropdownButton={(dropdownButton != null ? dropdownButton.name : "null")}");

        if (dropdownButton != null)
        {
            dropdownButton.SetMainClick(OpenPoliticalAffairsPanel);
            Refresh();
        }
    }

    public void Refresh()
    {
        if (dropdownButton == null)
            return;

        Debug.Log($"[HudPoliticalAffairsDropdown] Refresh civ={(currentCiv != null ? currentCiv.civData?.civName : "null")}");

        int governorCount = currentCiv?.governors?.Count ?? 0;
        dropdownButton.SetLabel($"Political Affairs: {governorCount} Governors");

        dropdownButton.ClearBody();
        var bodyRoot = dropdownButton.BodyRootTransform;
        if (bodyRoot == null)
            return;

        BuildGovernorSection(bodyRoot);
        BuildVassalSection(bodyRoot);
        BuildFactionSection(bodyRoot);

        dropdownButton.RebuildParentLayouts();
    }

    private void BuildGovernorSection(Transform bodyRoot)
    {
        AddSectionHeader("Governors / Lords", bodyRoot);

        var governors = currentCiv?.governors;
        if (governors == null || governors.Count == 0)
        {
            AddEmptyRow("No governors assigned", bodyRoot);
            return;
        }

        foreach (var governor in governors)
        {
            string fallback = governor?.Name ?? "Governor";

            if (governorRowPrefab != null)
            {
                var row = Instantiate(governorRowPrefab, bodyRoot, false);
                var rowComponent = row.GetComponent<HudGovernorSummaryRow>();
                if (rowComponent != null)
                {
                    rowComponent.Populate(governor, currentCiv);
                }
                else
                {
                    SetRowTextIfPresent(row, fallback);
                }
            }
            else
            {
                CreateSimpleTextRow("GovernorRow", fallback, bodyRoot, 16, FontStyles.Normal);
            }
        }
    }

    private void BuildVassalSection(Transform bodyRoot)
    {
        AddSectionHeader("Vassals / Subjects", bodyRoot);

        var entries = new List<KeyValuePair<Civilization, DiplomaticState>>();
        if (currentCiv?.relations != null)
        {
            foreach (var kv in currentCiv.relations)
            {
                if (kv.Key == null)
                    continue;

                if (kv.Value == DiplomaticState.Vassal || kv.Value == DiplomaticState.Protected)
                    entries.Add(kv);
            }
        }

        if (entries.Count == 0)
        {
            AddEmptyRow("No vassals", bodyRoot);
            return;
        }

        foreach (var entry in entries)
        {
            string civName = entry.Key.civData?.civName ?? entry.Key.name;
            string fallback = $"{civName} ({entry.Value})";

            if (vassalRowPrefab != null)
            {
                var row = Instantiate(vassalRowPrefab, bodyRoot, false);
                var rowComponent = row.GetComponent<HudVassalSummaryRow>();
                if (rowComponent != null)
                {
                    rowComponent.Populate(entry.Key, entry.Value);
                }
                else
                {
                    SetRowTextIfPresent(row, fallback);
                }
            }
            else
            {
                CreateSimpleTextRow("VassalRow", fallback, bodyRoot, 16, FontStyles.Normal);
            }
        }
    }

    private void BuildFactionSection(Transform bodyRoot)
    {
        AddSectionHeader("Noble Factions", bodyRoot);

        var factions = currentCiv?.nobleFactions;
        if (factions == null || factions.Count == 0)
        {
            AddEmptyRow("No noble factions", bodyRoot);
            return;
        }

        foreach (var faction in factions)
        {
            string fallback = faction?.FactionName ?? "Faction";

            if (factionRowPrefab != null)
            {
                var row = Instantiate(factionRowPrefab, bodyRoot, false);
                var rowComponent = row.GetComponent<HudFactionSummaryRow>();
                if (rowComponent != null)
                {
                    rowComponent.Populate(faction);
                }
                else
                {
                    SetRowTextIfPresent(row, fallback);
                }
            }
            else
            {
                CreateSimpleTextRow("FactionRow", fallback, bodyRoot, 16, FontStyles.Normal);
            }
        }
    }

    private void AddSectionHeader(string text, Transform parent)
    {
        if (sectionHeaderPrefab != null)
        {
            var instance = Instantiate(sectionHeaderPrefab, parent, false);
            SetRowTextIfPresent(instance, text);
            return;
        }

        CreateSimpleTextRow("SectionHeader", text, parent, 19, FontStyles.Bold);
    }

    private void AddEmptyRow(string text, Transform parent)
    {
        if (emptyStatePrefab != null)
        {
            var instance = Instantiate(emptyStatePrefab, parent, false);
            SetRowTextIfPresent(instance, text);
            return;
        }

        CreateSimpleTextRow("EmptyState", text, parent, 16, FontStyles.Italic);
    }

    private void OpenPoliticalAffairsPanel()
    {
        if (currentCiv == null)
        {
            currentCiv = CivilizationManager.Instance?.GetAllCivs()?.Find(c => c != null && c.isPlayerControlled);
            if (currentCiv == null)
            {
                Debug.LogWarning("[HudPoliticalAffairsDropdown] Cannot open Political Affairs panel because currentCiv is null.");
                return;
            }
        }

        Debug.Log($"[HudPoliticalAffairsDropdown] OpenPoliticalAffairsPanel civ={currentCiv.civData?.civName}");

        if (UIManager.Instance != null)
            UIManager.Instance.ShowPoliticalAffairsPanel(currentCiv);
    }

    private void EnsureDropdownReference()
    {
        if (dropdownButton == null)
            dropdownButton = GetComponent<HudDropdownButton>();
    }

    private static void SetRowTextIfPresent(GameObject instance, string text)
    {
        var tmp = instance.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmp != null)
            tmp.text = text;
    }

    private static void CreateSimpleTextRow(string objectName, string text, Transform parent, float fontSize, FontStyles fontStyle)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        var label = go.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = fontStyle;
        label.color = Color.white;
        label.enableWordWrapping = true;

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
    }
}
