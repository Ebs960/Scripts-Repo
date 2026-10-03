using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One authored dropdown row for one mutually-exclusive policy area.</summary>
public class PolicyAreaDropdownUI : MonoBehaviour
{
    [SerializeField] private TMP_Text areaNameText;
    [SerializeField] private Button dropdownButton;
    [SerializeField] private TMP_Text currentPolicyText;
    [SerializeField] private GameObject optionsRoot;
    [SerializeField] private Transform optionsContent;
    [SerializeField] private PolicyDropdownOptionUI optionPrefab;
    [SerializeField] private GameObject warningMarker;

    private readonly List<PolicyDropdownOptionUI> options = new List<PolicyDropdownOptionUI>();
    private Civilization civ;
    private PolicyArea area;
    private PolicyManager manager;
    private PolicyTooltipUI tooltip;
    private Action<PolicyData> onPolicySelected;
    private Action<PolicyData> onRepealRequested;
    private PolicyData active;

    public void Bind(Civilization civilization, PolicyArea policyArea, PolicyManager policyManager,
        PolicyTooltipUI sharedTooltip, Action<PolicyData> selected, Action<PolicyData> repeal)
    {
        civ = civilization; area = policyArea; manager = policyManager; tooltip = sharedTooltip;
        onPolicySelected = selected; onRepealRequested = repeal;
        active = manager?.GetActivePolicyInArea(civ, area);
        GovernmentUiUtil.SetText(areaNameText, GovernmentPresentation.PolicyAreaDisplayName(area));
        GovernmentUiUtil.SetText(currentPolicyText, active != null ? GovernmentPresentation.NameOf(active) : "No Policy");
        GovernmentUiUtil.SetActive(warningMarker, active != null && !manager.SatisfiesPolicyStructuralRequirements(civ, active));
        GovernmentUiUtil.SetClick(dropdownButton, Toggle);
        Close();
    }

    public void Close()
    {
        tooltip?.Hide();
        GovernmentUiUtil.SetActive(optionsRoot, false);
    }

    private void Toggle()
    {
        bool open = optionsRoot != null && !optionsRoot.activeSelf;
        if (!open) { Close(); return; }
        RebuildOptions();
        optionsRoot.SetActive(true);
    }

    private void RebuildOptions()
    {
        foreach (var option in options) if (option != null) Destroy(option.gameObject);
        options.Clear();
        if (optionsContent == null || optionPrefab == null || manager == null) return;
        if (active != null)
        {
            var none = Instantiate(optionPrefab, optionsContent); options.Add(none);
            none.BindNone(() => { Close(); onRepealRequested?.Invoke(active); });
        }
        foreach (var policy in manager.GetPoliciesInArea(area).OrderBy(GovernmentPresentation.NameOf))
        {
            var option = Instantiate(optionPrefab, optionsContent); options.Add(option);
            option.Bind(civ, policy, manager.EvaluatePolicy(civ, policy), tooltip,
                value => { Close(); onPolicySelected?.Invoke(value); }, Close);
        }
    }
}
