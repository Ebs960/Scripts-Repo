using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>How an improvement option is presented. Derived from the rules' evaluation; it does not change them.</summary>
public enum ImprovementOptionState
{
    Available,
    Installed,
    Locked,
    Unaffordable,
    Incompatible,
    Unavailable
}

/// <summary>
/// Prefab-facing view for one improvement option (an upgrade or a labor type). The layout and artwork are saved
/// in the row prefab. Locked and installed options remain visible so the player can see the complete contents
/// of a slot and why an option cannot be selected.
/// </summary>
public class ImprovementUpgradeOptionRow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [Tooltip("Optional. When unassigned the description is shown in the tooltip only.")]
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private TextMeshProUGUI effectsText;
    [SerializeField] private TextMeshProUGUI costText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Button selectButton;
    [SerializeField] private Graphic stateGraphic;
    [SerializeField] private Color availableColor = Color.white;
    [SerializeField] private Color unavailableColor = new Color(0.62f, 0.62f, 0.68f, 1f);
    [SerializeField] private Color installedColor = new Color(0.78f, 0.95f, 0.78f, 1f);
    [SerializeField] private Color availableStatusColor = new Color(0.88f, 0.76f, 0.48f, 1f);
    [SerializeField] private Color installedStatusColor = new Color(0.55f, 0.85f, 0.55f, 1f);
    [SerializeField] private Color blockedStatusColor = new Color(0.9f, 0.5f, 0.4f, 1f);

    private string tooltipTitle;
    private string tooltipBody;

    /// <summary>Maps the rules' evaluation onto a presentation state. Path conflicts are reported by the caller as Incompatible.</summary>
    public static ImprovementOptionState StateFor(ImprovementUpgradeEvaluation evaluation)
    {
        switch (evaluation.Availability)
        {
            case ImprovementUpgradeAvailability.Available: return ImprovementOptionState.Available;
            case ImprovementUpgradeAvailability.Installed: return ImprovementOptionState.Installed;
            case ImprovementUpgradeAvailability.Locked: return ImprovementOptionState.Locked;
            case ImprovementUpgradeAvailability.Unaffordable: return ImprovementOptionState.Unaffordable;
            default: return ImprovementOptionState.Unavailable;
        }
    }

    public static string LabelFor(ImprovementOptionState state)
    {
        switch (state)
        {
            case ImprovementOptionState.Available: return "AVAILABLE";
            case ImprovementOptionState.Installed: return "INSTALLED";
            case ImprovementOptionState.Locked: return "LOCKED";
            case ImprovementOptionState.Unaffordable: return "CANNOT AFFORD";
            case ImprovementOptionState.Incompatible: return "INCOMPATIBLE";
            default: return "UNAVAILABLE";
        }
    }

    public void Bind(ImprovementUpgradeData upgrade, ImprovementUpgradeEvaluation evaluation, Action selected)
    {
        Bind(upgrade, evaluation, StateFor(evaluation), null, selected);
    }

    /// <param name="requirements">Extra tooltip lines (prerequisites, replacements, exclusions); may be null.</param>
    public void Bind(ImprovementUpgradeData upgrade, ImprovementUpgradeEvaluation evaluation, ImprovementOptionState state,
        string requirements, Action selected)
    {
        if (upgrade == null) return;
        string effects = ImprovementUpgradeEffectFormatter.Format(upgrade);
        string cost = BuildCostText(upgrade);

        var tooltip = new List<string>();
        if (!string.IsNullOrWhiteSpace(upgrade.description)) tooltip.Add(upgrade.description);
        tooltip.Add($"Effects: {effects}");
        tooltip.Add($"Cost: {cost}");
        if (!string.IsNullOrWhiteSpace(requirements)) tooltip.Add(requirements);
        tooltip.Add(string.IsNullOrWhiteSpace(evaluation.Reason) || state == ImprovementOptionState.Installed
            ? LabelFor(state)
            : $"{LabelFor(state)}: {evaluation.Reason}");

        BindOption(upgrade.icon, upgrade.upgradeName, upgrade.description, effects, cost, state,
            state == ImprovementOptionState.Installed ? string.Empty : evaluation.Reason,
            string.Join("\n\n", tooltip), selected);
    }

    /// <summary>Binds any option. <paramref name="selected"/> is only wired when the state is Available.</summary>
    public void BindOption(Sprite optionIcon, string title, string description, string effects, string cost,
        ImprovementOptionState state, string reason, string tooltip, Action selected)
    {
        if (nameText == null || selectButton == null)
        {
            Debug.LogError("[ImprovementUpgradeOptionRow] The row prefab is missing its Name text or Button reference.", this);
            return;
        }

        bool interactable = state == ImprovementOptionState.Available;
        tooltipTitle = title;
        tooltipBody = tooltip;

        if (icon != null)
        {
            icon.sprite = optionIcon;
            icon.enabled = optionIcon != null;
        }
        nameText.text = title;
        if (descriptionText != null) descriptionText.text = description;
        if (effectsText != null) effectsText.text = effects;
        if (costText != null)
            costText.text = string.IsNullOrWhiteSpace(reason) ? cost : $"{cost}  -  {reason}";
        if (statusText != null)
        {
            statusText.text = LabelFor(state);
            statusText.color = state == ImprovementOptionState.Installed ? installedStatusColor
                : interactable ? availableStatusColor : blockedStatusColor;
        }

        selectButton.onClick.RemoveAllListeners();
        selectButton.interactable = interactable;
        if (interactable && selected != null)
            selectButton.onClick.AddListener(() => selected());

        if (stateGraphic != null)
        {
            stateGraphic.color = state == ImprovementOptionState.Installed
                ? installedColor
                : interactable ? availableColor : unavailableColor;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (string.IsNullOrEmpty(tooltipBody) || TooltipSystem.Instance == null) return;
        TooltipSystem.Instance.ShowSimpleTooltip(tooltipTitle, tooltipBody);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        TooltipSystem.Instance?.RequestHideTooltip();
    }

    private void OnDisable()
    {
        TooltipSystem.Instance?.RequestHideTooltip();
    }

    public static string BuildCostText(ImprovementUpgradeData upgrade)
    {
        var parts = new List<string>();
        if (upgrade.goldCost > 0) parts.Add($"{upgrade.goldCost} Gold");
        string resources = ResourceCost.FormatCosts(upgrade.resourceCosts, upgrade.hasSubstituteCosts);
        if (!string.IsNullOrEmpty(resources)) parts.Add(resources);
        return parts.Count == 0 ? "No cost" : string.Join(" • ", parts);
    }
}