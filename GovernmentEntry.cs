using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GovernmentEntry : MonoBehaviour
{
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI descText;
    public Button adoptButton;

    GovernmentData data;
    Civilization civ;

    public void Setup(GovernmentData data, Civilization civ)
    {
        this.data = data;
        this.civ = civ;
        if (nameText != null) nameText.text = data.governmentName;
        if (descText != null) descText.text = data.leaderTitleSuffix;
        if (adoptButton != null)
        {
            adoptButton.onClick.RemoveAllListeners();
            adoptButton.onClick.AddListener(OnAdoptClicked);
            adoptButton.interactable = PolicyManager.Instance != null && PolicyManager.Instance.GetAvailableGovernments(civ).Contains(data);
        }
    }

    void OnAdoptClicked()
    {
        if (PolicyManager.Instance == null || civ == null || data == null) return;
        var ok = PolicyManager.Instance.ChangeGovernment(civ, data);
        if (ok)
        {
            var panel = GetComponentInParent<GovernmentPanel>();
            if (panel != null) panel.RefreshAll();
        }
    }
}
