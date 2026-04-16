using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class BeliefSlotUI : MonoBehaviour, IDropHandler
{
    public BeliefCategory category;
    public Image iconImage;
    public TextMeshProUGUI label;
    public TextMeshProUGUI effectSummary;
    public ReligionUI owner;
    public UnityEngine.UI.Button clearButton;

    public void Start()
    {
        if (label == null) label = GetComponentInChildren<TextMeshProUGUI>();
        if (iconImage == null) iconImage = GetComponentInChildren<Image>();
        if (clearButton != null)
        {
            clearButton.onClick.RemoveAllListeners();
            clearButton.onClick.AddListener(() => { owner?.ClearBeliefCategory(category); });
        }
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (eventData.pointerDrag == null) return;
        var btn = eventData.pointerDrag.GetComponent<BeliefButtonUI>();
        if (btn == null || btn.belief == null) return;

        // Notify owner to assign
        owner?.AssignBeliefToCategory(category, btn.belief);
    }

    public void SetAssigned(BeliefData belief)
    {
        if (label != null) label.text = belief != null ? belief.beliefName : "(empty)";
        if (iconImage != null) iconImage.sprite = belief != null ? belief.icon : null;
        if (effectSummary != null)
            effectSummary.text = belief != null ? BuildSummary(belief) : "";
        if (clearButton != null) clearButton.gameObject.SetActive(belief != null);
    }

    private string BuildSummary(BeliefData b)
    {
        if (b == null) return "";
        var parts = new System.Collections.Generic.List<string>();

        if (b.extraFaithInHolySite != 0) parts.Add($"+{b.extraFaithInHolySite} Faith (Holy Site)");
        if (b.extraFoodInHolySite != 0) parts.Add($"+{b.extraFoodInHolySite} Food (Holy Site)");
        if (b.extraProductionInHolySite != 0) parts.Add($"+{b.extraProductionInHolySite} Prod (Holy Site)");
        if (b.goldPerCity != 0) parts.Add($"+{b.goldPerCity} Gold/City");
        if (b.culturePerCity != 0) parts.Add($"+{b.culturePerCity} Culture/City");
        if (b.happinessBonus != 0) parts.Add($"+{b.happinessBonus} Happiness");
        if (b.combatStrengthNearHolySite != 0) parts.Add($"+{b.combatStrengthNearHolySite} Strength (near HS)");
        if (Mathf.Abs(b.growthRateModifier) > 0.0001f) parts.Add($"{(b.growthRateModifier>0?"+":"")}{b.growthRateModifier:P0} Growth");
        if (Mathf.Abs(b.productionRateModifier) > 0.0001f) parts.Add($"{(b.productionRateModifier>0?"+":"")}{b.productionRateModifier:P0} Prod");

        // Percentage yield modifiers
        if (Mathf.Abs(b.foodModifier) > 0.0001f) parts.Add($"{(b.foodModifier>0?"+":"")}{b.foodModifier:P0} Food");
        if (Mathf.Abs(b.productionModifier) > 0.0001f) parts.Add($"{(b.productionModifier>0?"+":"")}{b.productionModifier:P0} Prod%");
        if (Mathf.Abs(b.goldModifier) > 0.0001f) parts.Add($"{(b.goldModifier>0?"+":"")}{b.goldModifier:P0} Gold");
        if (Mathf.Abs(b.scienceModifier) > 0.0001f) parts.Add($"{(b.scienceModifier>0?"+":"")}{b.scienceModifier:P0} Sci");
        if (Mathf.Abs(b.cultureModifier) > 0.0001f) parts.Add($"{(b.cultureModifier>0?"+":"")}{b.cultureModifier:P0} Culture");
        if (Mathf.Abs(b.faithModifier) > 0.0001f) parts.Add($"{(b.faithModifier>0?"+":"")}{b.faithModifier:P0} Faith");

        // If none, show short description fallback
        if (parts.Count == 0)
        {
            if (!string.IsNullOrEmpty(b.description))
            {
                var desc = b.description.Trim();
                if (desc.Length > 80) desc = desc.Substring(0, 77) + "...";
                return desc;
            }
            return "(no immediate effect)";
        }

        // Join with bullet separator and cap length
        var joined = string.Join(" • ", parts);
        if (joined.Length > 120) joined = joined.Substring(0, 117) + "...";
        return joined;
    }
}
