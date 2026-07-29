using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Prefab-facing view for one improvement option. Locked and installed options remain visible
/// so the player can see the complete contents of a slot and why an option cannot be selected.
/// </summary>
public class ImprovementUpgradeOptionRow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private TextMeshProUGUI effectsText;
    [SerializeField] private TextMeshProUGUI costText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Button selectButton;
    [SerializeField] private Graphic stateGraphic;
    [SerializeField] private Color availableColor = Color.white;
    [SerializeField] private Color unavailableColor = new Color(0.65f, 0.65f, 0.65f, 1f);
    [SerializeField] private Color installedColor = new Color(0.75f, 0.85f, 0.7f, 1f);
    private ImprovementUpgradeData boundUpgrade;
    private ImprovementUpgradeEvaluation boundEvaluation;

    private void Awake()
    {
        EnsureVisualTree();
    }

    private void EnsureVisualTree()
    {
        var root = transform as RectTransform;
        if (root != null && root.sizeDelta.y <= 0f) root.sizeDelta = new Vector2(root.sizeDelta.x, 96f);

        if (stateGraphic == null)
        {
            var image = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            image.color = availableColor;
            stateGraphic = image;
        }
        if (selectButton == null)
        {
            selectButton = gameObject.GetComponent<Button>() ?? gameObject.AddComponent<Button>();
            selectButton.targetGraphic = stateGraphic;
        }

        icon ??= CreateImage("Icon", new Vector2(8f, -8f), new Vector2(64f, 64f));
        nameText ??= CreateText("Name", "Upgrade", 18f, new Vector2(82f, -6f), new Vector2(230f, 24f));
        descriptionText ??= CreateText("Description", "Description", 13f, new Vector2(82f, -31f), new Vector2(360f, 36f));
        effectsText ??= CreateText("Effects", "Effects", 13f, new Vector2(82f, -68f), new Vector2(360f, 22f));
        costText ??= CreateText("Cost", "Cost", 14f, new Vector2(450f, -8f), new Vector2(145f, 36f));
        statusText ??= CreateText("Status", "Status", 13f, new Vector2(450f, -49f), new Vector2(145f, 38f));
    }

    private Image CreateImage(string objectName, Vector2 position, Vector2 size)
    {
        var child = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        child.transform.SetParent(transform, false);
        var rect = (RectTransform)child.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return child.GetComponent<Image>();
    }

    private TextMeshProUGUI CreateText(string objectName, string value, float fontSize, Vector2 position, Vector2 size)
    {
        var child = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        child.transform.SetParent(transform, false);
        var rect = (RectTransform)child.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var text = child.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = fontSize;
        text.enableWordWrapping = true;
        text.raycastTarget = false;
        return text;
    }

    public void Bind(ImprovementUpgradeData upgrade, ImprovementUpgradeEvaluation evaluation, Action selected)
    {
        if (upgrade == null) return;
        EnsureVisualTree();
        boundUpgrade = upgrade;
        boundEvaluation = evaluation;

        if (icon != null)
        {
            icon.sprite = upgrade.icon;
            icon.enabled = upgrade.icon != null;
        }
        if (nameText != null) nameText.text = upgrade.upgradeName;
        if (descriptionText != null) descriptionText.text = upgrade.description;
        if (effectsText != null) effectsText.text = BuildEffectsText(upgrade);
        if (costText != null) costText.text = BuildCostText(upgrade);
        if (statusText != null)
        {
            statusText.text = evaluation.Reason;
            statusText.gameObject.SetActive(!string.IsNullOrWhiteSpace(evaluation.Reason));
        }
        if (selectButton != null)
        {
            selectButton.onClick.RemoveAllListeners();
            selectButton.interactable = evaluation.IsInteractable;
            if (evaluation.IsInteractable && selected != null)
                selectButton.onClick.AddListener(() => selected());
        }
        if (stateGraphic != null)
        {
            stateGraphic.color = evaluation.Availability == ImprovementUpgradeAvailability.Installed
                ? installedColor
                : evaluation.IsInteractable ? availableColor : unavailableColor;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (boundUpgrade == null || TooltipSystem.Instance == null) return;
        string details = boundUpgrade.description;
        string effects = BuildEffectsText(boundUpgrade);
        string cost = BuildCostText(boundUpgrade);
        if (!string.IsNullOrWhiteSpace(boundEvaluation.Reason))
            details += $"\n\n{boundEvaluation.Reason}";
        details += $"\n\nEffects: {effects}\nCost: {cost}";
        TooltipSystem.Instance.ShowSimpleTooltip(boundUpgrade.upgradeName, details);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        TooltipSystem.Instance?.RequestHideTooltip();
    }

    private static string BuildCostText(ImprovementUpgradeData upgrade)
    {
        var parts = new List<string>();
        if (upgrade.goldCost > 0) parts.Add($"{upgrade.goldCost} Gold");
        string resources = ResourceCost.FormatCosts(upgrade.resourceCosts, upgrade.hasSubstituteCosts);
        if (!string.IsNullOrEmpty(resources)) parts.Add(resources);
        return parts.Count == 0 ? "No cost" : string.Join(" • ", parts);
    }

    private static string BuildEffectsText(ImprovementUpgradeData upgrade)
    {
        return ImprovementUpgradeEffectFormatter.Format(upgrade);
    }
}
