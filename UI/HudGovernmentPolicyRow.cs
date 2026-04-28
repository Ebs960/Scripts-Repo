using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HudGovernmentPolicyRow : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private TextMeshProUGUI effectsText;

    public void Populate(PolicyData policy, string effectSummary)
    {
        if (iconImage != null)
        {
            iconImage.sprite = policy != null ? policy.icon : null;
            iconImage.enabled = iconImage.sprite != null;
        }

        if (nameText != null)
            nameText.text = policy != null ? GetPolicyName(policy) : "Policy";

        if (descriptionText != null)
            descriptionText.text = policy != null ? policy.description : string.Empty;

        if (effectsText != null)
            effectsText.text = string.IsNullOrWhiteSpace(effectSummary) ? "No effects listed." : effectSummary;
    }

    private static string GetPolicyName(PolicyData policy)
    {
        if (!string.IsNullOrWhiteSpace(policy.policyName))
            return policy.policyName;
        return policy.name;
    }
}
