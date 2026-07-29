using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Helper component for upgrade button prefabs. Configures visuals and click handling.
/// </summary>
public class UpgradeButton : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI statusText;

    public void Setup(ImprovementUpgradeData upgrade, System.Action onClick, bool canBuild)
    {
        Setup(upgrade, onClick, new ImprovementUpgradeEvaluation(
            canBuild ? ImprovementUpgradeAvailability.Available : ImprovementUpgradeAvailability.Locked,
            canBuild ? string.Empty : "Requirements not met."));
    }

    public void Setup(ImprovementUpgradeData upgrade, System.Action onClick, ImprovementUpgradeEvaluation evaluation)
    {
        if (nameText != null)
        {
            string costText = $"Gold: {upgrade.goldCost}";
            string resourceText = ResourceCost.FormatCosts(upgrade.resourceCosts, upgrade.hasSubstituteCosts);
            if (!string.IsNullOrEmpty(resourceText))
                costText += $"\n{resourceText}";
            nameText.text = $"{upgrade.upgradeName}\n{costText}";
        }

        if (iconImage != null && upgrade.icon != null)
            iconImage.sprite = upgrade.icon;

        if (statusText != null)
        {
            statusText.text = evaluation.Reason;
            statusText.gameObject.SetActive(!string.IsNullOrWhiteSpace(evaluation.Reason));
        }

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            if (evaluation.IsInteractable && onClick != null) button.onClick.AddListener(() => onClick.Invoke());
            button.interactable = evaluation.IsInteractable;
        }
    }
}
