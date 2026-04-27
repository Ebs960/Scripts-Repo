// Assets/Scripts/UI/HudCultureProgress.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Culture progress widget for left HUD panel.
/// Shows current culture progress and displays breakdown on hover.
/// </summary>
public class HudCultureProgress : MonoBehaviour
{
    [SerializeField] private Image progressBar;
    [SerializeField] private TextMeshProUGUI cultureNameText;
    [SerializeField] private TextMeshProUGUI progressText;
    [SerializeField] private Button mainButton; // Click to open culture panel

    private Civilization currentCiv;

    private void Start()
    {
        if (mainButton != null)
        {
            mainButton.onClick.AddListener(() =>
            {
                if (UIManager.Instance != null && currentCiv != null)
                    UIManager.Instance.ShowCulturePanel(currentCiv);
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

        // Get current culture era or goal
        // For now, simplified display
        if (cultureNameText != null)
            cultureNameText.text = "Culture";

        // Calculate progress based on culture toward next era/policy
        // Placeholder implementation
        int cultureRequired = 100; // This should come from game rules
        float progressPct = civ.culture / (float)cultureRequired;
        if (progressBar != null)
            progressBar.fillAmount = Mathf.Clamp01(progressPct);

        if (progressText != null)
            progressText.text = $"{civ.culture}/{cultureRequired}";
    }
}
