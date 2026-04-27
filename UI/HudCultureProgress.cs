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

        var activeCulture = civ.currentCulture;
        if (activeCulture != null)
        {
            int cultureCost = Mathf.Max(1, activeCulture.cultureCost);
            int progress = Mathf.RoundToInt(civ.currentCultureProgress);
            float progressPct = progress / (float)cultureCost;

            if (cultureNameText != null)
                cultureNameText.text = activeCulture.cultureName;

            if (progressBar != null)
                progressBar.fillAmount = Mathf.Clamp01(progressPct);

            if (progressText != null)
                progressText.text = $"{progress}/{cultureCost}";
        }
        else
        {
            if (cultureNameText != null)
                cultureNameText.text = "No Culture";

            if (progressBar != null)
                progressBar.fillAmount = 0f;

            if (progressText != null)
                progressText.text = "0/0";
        }
    }
}
