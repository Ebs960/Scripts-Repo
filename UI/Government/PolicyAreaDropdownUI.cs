using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One authored dropdown row for one policy area holding up to two active policies.</summary>
public class PolicyAreaDropdownUI : MonoBehaviour
{
    [SerializeField] private TMP_Text areaNameText;
    [SerializeField] private Button dropdownButton;
    [Tooltip("Shows both active policies stacked on two lines.")]
    [SerializeField] private TMP_Text currentPolicyText;
    [Tooltip("Optional compact slot indicator, e.g. 1/2.")]
    [SerializeField] private TMP_Text capacityText;
    [SerializeField] private TMP_Text dropdownArrowText;
    [SerializeField] private Transform optionsRoot;
    [SerializeField] private PolicyDropdownOptionUI optionPrefab;

    private readonly List<PolicyDropdownOptionUI> options = new List<PolicyDropdownOptionUI>();
    private Civilization civ;
    private PolicyArea area;
    private PolicyManager manager;
    private PolicyTooltipUI tooltip;
    private Action<PolicyData, PolicyData> onPolicySelected;
    private Action<PolicyData> onRepealRequested;
    private List<PolicyData> active = new List<PolicyData>();

    public void Bind(Civilization civilization, PolicyArea policyArea, PolicyManager policyManager,
        PolicyTooltipUI sharedTooltip, Action<PolicyData, PolicyData> selected, Action<PolicyData> repeal)
    {
        bool keepOpen = civ == civilization && area == policyArea && manager == policyManager
            && optionsRoot != null && optionsRoot.gameObject.activeSelf;
        civ = civilization; area = policyArea; manager = policyManager; tooltip = sharedTooltip;
        onPolicySelected = selected; onRepealRequested = repeal;
        active = manager != null ? manager.GetActivePoliciesInArea(civ, area) : new List<PolicyData>();
        GovernmentUiUtil.SetText(areaNameText, GovernmentPresentation.PolicyAreaDisplayName(area));
        GovernmentUiUtil.SetText(currentPolicyText, FormatActivePolicies(active));
        GovernmentUiUtil.SetText(capacityText, $"{active.Count}/{PolicyManager.MaxActivePoliciesPerArea}");
        GovernmentUiUtil.SetClick(dropdownButton, Toggle);
        if (keepOpen) RebuildOptions();
        else Close();
        RebuildLayout();
    }

    /// <summary>Collapsed-row text: both active policies on separate lines.</summary>
    public static string FormatActivePolicies(IReadOnlyList<PolicyData> activePolicies)
    {
        if (activePolicies == null || activePolicies.Count == 0) return "No active policies";
        string first = GovernmentPresentation.NameOf(activePolicies[0]);
        return activePolicies.Count == 1 ? $"{first}\nNo second policy" : $"{first}\n{GovernmentPresentation.NameOf(activePolicies[1])}";
    }

    public void Close()
    {
        tooltip?.Hide();
        if (optionsRoot != null) optionsRoot.gameObject.SetActive(false);
        GovernmentUiUtil.SetText(dropdownArrowText, "v");
        RebuildLayout();
    }

    private void Toggle()
    {
        bool open = optionsRoot != null && !optionsRoot.gameObject.activeSelf;
        if (!open) { Close(); return; }
        active = manager != null ? manager.GetActivePoliciesInArea(civ, area) : new List<PolicyData>();
        RebuildOptions();
        optionsRoot.gameObject.SetActive(true);
        GovernmentUiUtil.SetText(dropdownArrowText, "^");
        RebuildLayout();
    }

    private void RebuildLayout()
    {
        if (transform is RectTransform row) LayoutRebuilder.MarkLayoutForRebuild(row);
        var scroll = GetComponentInParent<ScrollRect>();
        if (scroll != null && scroll.content != null) LayoutRebuilder.MarkLayoutForRebuild(scroll.content);
    }

    private void ClearOptions()
    {
        tooltip?.Hide();
        foreach (var option in options)
        {
            if (option == null) continue;
            option.gameObject.SetActive(false);
            Destroy(option.gameObject);
        }
        options.Clear();
    }

    private PolicyDropdownOptionUI AddOption()
    {
        var option = Instantiate(optionPrefab, optionsRoot);
        options.Add(option);
        return option;
    }

    private void RebuildOptions()
    {
        ClearOptions();
        if (optionsRoot == null || optionPrefab == null || manager == null) return;
        foreach (var current in active)
        {
            var captured = current;
            AddOption().BindAction($"Repeal {GovernmentPresentation.NameOf(captured)}", "Repeal this policy", true,
                () => { Close(); onRepealRequested?.Invoke(captured); });
        }
        foreach (var policy in manager.GetPoliciesInArea(area).OrderBy(GovernmentPresentation.NameOf))
            AddOption().Bind(civ, policy, manager.EvaluatePolicy(civ, policy), tooltip, OnCandidateChosen, Close);
        RebuildLayout();
    }

    private void OnCandidateChosen(PolicyData candidate)
    {
        var evaluation = manager?.EvaluatePolicy(civ, candidate);
        if (evaluation == null) return;
        if (evaluation.canAdopt) { Close(); onPolicySelected?.Invoke(candidate, null); return; }
        if (evaluation.CanAdoptWithReplacement) ShowReplacementChoices(candidate, evaluation);
    }

    /// <summary>Area is full: the player must explicitly pick which active policy the candidate replaces.</summary>
    private void ShowReplacementChoices(PolicyData candidate, PolicyAdoptionEvaluation evaluation)
    {
        ClearOptions();
        foreach (var current in evaluation.activeAreaPolicies)
        {
            var captured = current;
            bool valid = evaluation.replacementChoices.Contains(captured);
            AddOption().BindAction($"Replace {GovernmentPresentation.NameOf(captured)}",
                valid ? $"with {GovernmentPresentation.NameOf(candidate)}" : "Cannot be replaced (required)", valid,
                () => { Close(); onPolicySelected?.Invoke(candidate, captured); });
        }
        AddOption().BindAction("Cancel", "Keep both current policies", true, RebuildOptions);
        RebuildLayout();
    }
}
