using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Simple assignment subpanel for one governor's cities and herds. Uses only Civilization's assign/remove APIs.</summary>
public class GovernorHoldingsPanelUI : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button closeButton;
    [SerializeField] private Transform citiesRoot;
    [SerializeField] private Transform herdsRoot;
    [SerializeField] private GovernorHoldingRowUI rowPrefab;

    private readonly List<GovernorHoldingRowUI> cityRows = new List<GovernorHoldingRowUI>();
    private readonly List<GovernorHoldingRowUI> herdRows = new List<GovernorHoldingRowUI>();
    private Civilization civ;
    private Governor governor;
    private Action onChanged;

    private GameObject Root => root != null ? root : gameObject;
    public bool IsVisible => Root.activeSelf;

    public void Show(Civilization civilization, Governor target, Action changed)
    {
        civ = civilization;
        governor = target;
        onChanged = changed;
        GovernmentUiUtil.SetClick(closeButton, Hide);
        Root.SetActive(true);
        Refresh();
    }

    public void Hide()
    {
        civ = null;
        governor = null;
        Root.SetActive(false);
    }

    private void Refresh()
    {
        if (civ == null || governor == null) return;
        GovernmentUiUtil.SetText(titleText, $"Holdings of {GovernmentPresentation.FormatGovernorName(civ, governor)}");

        var cities = civ.cities.FindAll(c => c != null);
        GovernmentUiUtil.FillList(citiesRoot, rowPrefab, cityRows, cities, (row, city) =>
        {
            bool mine = city.governor == governor;
            string status = mine ? "Governed by this " + GovernmentPresentation.GetGovernorTitleSingular(civ).ToLowerInvariant()
                : city.governor == null ? "Unassigned"
                : $"Governed by {GovernmentPresentation.FormatGovernorName(civ, city.governor)} (reassigning angers them)";
            row.Bind(GovernmentPresentation.NameOf(city), status, mine ? "Remove" : "Assign", civ.governorsEnabled,
                () => Apply(mine ? civ.RemoveGovernorFromCity(governor, city) : civ.AssignGovernorToCity(governor, city)));
        });

        var herds = civ.herds != null ? civ.herds.FindAll(h => h != null) : new List<Herd>();
        GovernmentUiUtil.FillList(herdsRoot, rowPrefab, herdRows, herds, (row, herd) =>
        {
            bool mine = herd.governor == governor;
            string status = mine ? "Governed by this " + GovernmentPresentation.GetGovernorTitleSingular(civ).ToLowerInvariant()
                : herd.governor == null ? "Unassigned"
                : $"Governed by {GovernmentPresentation.FormatGovernorName(civ, herd.governor)}";
            row.Bind(GovernmentPresentation.NameOf(herd), status, mine ? "Remove" : "Assign", civ.governorsEnabled,
                () => Apply(mine ? civ.RemoveGovernorFromHerd(governor, herd) : civ.AssignGovernorToHerd(governor, herd)));
        });
    }

    private void Apply(bool succeeded)
    {
        GovernmentUiUtil.SetText(statusText, succeeded ? string.Empty : "That assignment could not be made.");
        Refresh();
        onChanged?.Invoke();
    }
}
