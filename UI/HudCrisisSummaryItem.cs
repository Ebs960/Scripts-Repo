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
    [SerializeField] private TextMeshProUGUI effectsText;
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

        // Display crisis effects
        if (effectsText != null)
        {
            effectsText.text = GetEffectsDescription(crisis);
        }
    }

    private string GetEffectsDescription(CrisisData crisis)
    {
        if (crisis == null || crisis.worldOverrides == null || crisis.worldOverrides.Length == 0)
            return GetContextDescription(crisis);

        var effectLines = new System.Collections.Generic.List<string>();

        foreach (var overrideEntry in crisis.worldOverrides)
        {
            string effectText = overrideEntry.type switch
            {
                CrisisData.WorldOverrideType.WinterDurationTurns => $"Winter Duration: +{(int)overrideEntry.value} turns",
                CrisisData.WorldOverrideType.DroughtChance => $"Drought Chance: +{(int)(overrideEntry.value * 100)}%",
                CrisisData.WorldOverrideType.DroughtSeverity => $"Drought Severity: {(int)(overrideEntry.value * 100)}%",
                CrisisData.WorldOverrideType.PreySpawnMultiplier => $"Prey Spawn: {overrideEntry.value:F1}x",
                CrisisData.WorldOverrideType.PredatorSpawnMultiplier => $"Predator Spawn: {overrideEntry.value:F1}x",
                CrisisData.WorldOverrideType.WinterAttritionDamage => $"Winter Damage: +{(int)overrideEntry.value}",
                CrisisData.WorldOverrideType.FoodYieldMultiplier => $"Food Yield: {overrideEntry.value:F1}x",
                CrisisData.WorldOverrideType.ForceWinter => "Winter forced",
                _ => "Unknown effect"
            };
            effectLines.Add(effectText);
        }

        string context=GetContextDescription(crisis);
        if (!string.IsNullOrEmpty(context)) effectLines.Insert(0,context);
        return string.Join("\n", effectLines);
    }

    private string GetContextDescription(CrisisData crisis)
    {
        var manager=CrisisManager.Instance;
        var context=manager != null && manager.ActiveCrisis==crisis ? manager.ActiveContext : null;
        if (context == null) return "No active effects";
        var lines=new System.Collections.Generic.List<string>();
        if (context.targetContinentId>=0) lines.Add($"Continent {context.targetContinentId}");
        if (context.targetCityId>=0) lines.Add("Targeted city");
        if (context.overlordIndex>=0 && context.subjectIndex>=0) lines.Add("Subject relationship crisis");
        if (!string.IsNullOrEmpty(context.riskExplanation)) lines.Add($"Risk {context.resolvedRisk:0.#}: {context.riskExplanation}");
        return lines.Count>0?string.Join("\n",lines):"Global crisis";
    }
}
