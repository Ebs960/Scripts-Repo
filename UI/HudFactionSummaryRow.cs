using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HudFactionSummaryRow : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI detailText;
    [SerializeField] private Image iconImage;

    public void Populate(FactionBloc faction)
    {
        if (faction == null)
        {
            if (nameText != null) nameText.text = "Faction";
            if (statusText != null) statusText.text = string.Empty;
            if (detailText != null) detailText.text = string.Empty;
            if (iconImage != null) iconImage.enabled = false;
            return;
        }

        if (nameText != null)
            nameText.text = string.IsNullOrWhiteSpace(faction.FactionName) ? "Faction" : faction.FactionName;

        if (statusText != null)
            statusText.text = faction.IsInRebellion
                ? $"{faction.Alignment} • Rebellion"
                : faction.Alignment.ToString();

        if (detailText != null)
            detailText.text = $"Leader: {faction.Leader?.Name ?? "—"} • Members: {faction.Members?.Count ?? 0} • Demands: {faction.ActiveDemands?.Count ?? 0}";

        if (iconImage != null)
            iconImage.enabled = false;
    }
}
