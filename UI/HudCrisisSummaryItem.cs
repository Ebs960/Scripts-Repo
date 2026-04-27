// Assets/Scripts/UI/HudCrisisSummaryItem.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Single crisis summary item in the dropdown.
/// Displays: crisis name, turns remaining, phase.
/// </summary>
public class HudCrisisSummaryItem : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI crisisNameText;
    [SerializeField] private TextMeshProUGUI turnsText;
    [SerializeField] private TextMeshProUGUI phaseText;
    [SerializeField] private Image crisisIcon;

    public void Populate(CrisisData crisis)
    {
        if (crisis == null) return;

        if (crisisNameText != null)
            crisisNameText.text = crisis.crisisName;

        if (turnsText != null)
        {
            int turnsRemaining = -1;
            if (CrisisManager.Instance != null && CrisisManager.Instance.ActiveCrisis == crisis)
                turnsRemaining = CrisisManager.Instance.GetDisplayTurnsRemaining();

            turnsText.text = turnsRemaining >= 0 ? $"{turnsRemaining} turns" : string.Empty;
        }

        if (phaseText != null)
        {
            string phaseStr = "Dormant";
            if (CrisisManager.Instance != null && CrisisManager.Instance.ActiveCrisis == crisis)
                phaseStr = CrisisManager.Instance.CurrentPhase.ToString();
            phaseText.text = $"Phase {phaseStr}";
        }

        if (crisisIcon != null && crisis.icon != null)
            crisisIcon.sprite = crisis.icon;
    }
}
