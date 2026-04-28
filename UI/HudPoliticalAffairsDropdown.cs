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

    public void Bind(Civilization civ)
    {
        currentCiv = civ;

        if (dropdownButton == null)
            dropdownButton = GetComponent<HudDropdownButton>();

        if (dropdownButton != null)
            dropdownButton.SetMainClick(OpenPoliticalAffairsPanel);

        Refresh();
    }

    public void Refresh()
    {
        if (dropdownButton == null)
            return;

        int governorCount = currentCiv?.governors?.Count ?? 0;
        dropdownButton.SetLabel($"Political Affairs: {governorCount} Governors");
        RebuildBody();
    }

    private void RebuildBody()
    {
        dropdownButton.ClearBody();
        var bodyRoot = dropdownButton.BodyRootTransform;
        if (bodyRoot == null)
            return;

        BuildGovernorSection(bodyRoot);
        BuildVassalSection(bodyRoot);
        BuildFactionSection(bodyRoot);
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
            if (governorRowPrefab == null)
            {
                AddEmptyRow(governor?.Name ?? "Governor", bodyRoot);
                continue;
            }

            var row = Instantiate(governorRowPrefab, bodyRoot, false);
            var rowComponent = row.GetComponent<HudGovernorSummaryRow>();
            if (rowComponent != null)
                rowComponent.Populate(governor, currentCiv);
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
            if (vassalRowPrefab == null)
            {
                AddEmptyRow($"{entry.Key.civData?.civName ?? entry.Key.name} ({entry.Value})", bodyRoot);
                continue;
            }

            var row = Instantiate(vassalRowPrefab, bodyRoot, false);
            var rowComponent = row.GetComponent<HudVassalSummaryRow>();
            if (rowComponent != null)
                rowComponent.Populate(entry.Key, entry.Value);
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
            if (factionRowPrefab == null)
            {
                AddEmptyRow(faction?.FactionName ?? "Faction", bodyRoot);
                continue;
            }

            var row = Instantiate(factionRowPrefab, bodyRoot, false);
            var rowComponent = row.GetComponent<HudFactionSummaryRow>();
            if (rowComponent != null)
                rowComponent.Populate(faction);
        }
    }

    private void AddSectionHeader(string text, Transform parent)
    {
        if (sectionHeaderPrefab != null)
        {
            var instance = Instantiate(sectionHeaderPrefab, parent, false);
            var tmp = instance.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null)
                tmp.text = text;
            return;
        }

        var go = new GameObject("SectionHeader", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var label = go.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 19;
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
    }

    private void AddEmptyRow(string text, Transform parent)
    {
        if (emptyStatePrefab != null)
        {
            var instance = Instantiate(emptyStatePrefab, parent, false);
            var tmp = instance.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null)
                tmp.text = text;
            return;
        }

        var go = new GameObject("EmptyState", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var label = go.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 16;
        label.color = Color.white;
    }

    private void OpenPoliticalAffairsPanel()
    {
        if (UIManager.Instance != null)
            UIManager.Instance.ShowPoliticalAffairsPanel(currentCiv);
    }
}
