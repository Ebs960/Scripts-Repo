using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PolicyEntry : MonoBehaviour
{
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI descriptionText;
    public Button adoptButton;

    PolicyData data;
    Civilization civ;

    public void Setup(PolicyData data, Civilization civ)
    {
        this.data = data;
        this.civ = civ;
        if (nameText != null) nameText.text = data.policyName;
        if (descriptionText != null) descriptionText.text = data.description;

        if (adoptButton != null)
        {
            adoptButton.onClick.RemoveAllListeners();
            adoptButton.onClick.AddListener(OnAdoptClicked);
            adoptButton.interactable = PolicyManager.Instance != null && PolicyManager.Instance.GetAvailablePolicies(civ).Contains(data);
        }
    }

    void OnAdoptClicked()
    {
        if (PolicyManager.Instance == null || civ == null || data == null) return;
        var ok = PolicyManager.Instance.AdoptPolicy(civ, data);
        if (ok)
        {
            // refresh parent's list if possible
            var panel = GetComponentInParent<GovernmentPanel>();
            if (panel != null) panel.RefreshAll();
        }
    }
}
