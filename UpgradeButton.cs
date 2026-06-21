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

    public void Setup(ImprovementUpgradeData upgrade, System.Action onClick, bool canBuild)
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

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            if (onClick != null) button.onClick.AddListener(() => onClick.Invoke());
            button.interactable = canBuild;
        }
    }
}
