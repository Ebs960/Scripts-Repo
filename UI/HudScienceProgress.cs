// Assets/Scripts/UI/HudScienceProgress.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Science progress widget for left HUD panel.
/// Shows current tech research progress and displays breakdown on hover.
/// </summary>
public class HudScienceProgress : MonoBehaviour
{
    [SerializeField] private Image progressBar;
    [SerializeField] private TextMeshProUGUI techNameText;
    [SerializeField] private TextMeshProUGUI progressText;
    [SerializeField] private Button mainButton; // Click to open tech panel

    private Civilization currentCiv;

    private void Start()
    {
        if (mainButton != null)
        {
            mainButton.onClick.AddListener(() =>
            {
                if (UIManager.Instance != null && currentCiv != null)
                    UIManager.Instance.ShowTechPanel(currentCiv);
            });
        }
    }

    private void OnDestroy()
    {
        if (mainButton != null)
            mainButton.onClick.RemoveAllListeners();
    }

    public void Bind(Civilization civ)
    {
        currentCiv = civ;
        if (civ == null) return;

        // Get current research tech
        var researchTech = civ.currentTech;
        if (researchTech != null)
        {
            if (techNameText != null)
                techNameText.text = researchTech.techName;

            // Calculate progress percentage
            float progressPct = civ.currentTechProgress / (float)researchTech.scienceCost;
            if (progressBar != null)
                progressBar.fillAmount = Mathf.Clamp01(progressPct);

            if (progressText != null)
                progressText.text = $"{civ.currentTechProgress}/{researchTech.scienceCost}";
        }
        else
        {
            if (techNameText != null)
                techNameText.text = "No Research";
            if (progressBar != null)
                progressBar.fillAmount = 0;
            if (progressText != null)
                progressText.text = "0/0";
        }
    }
}
