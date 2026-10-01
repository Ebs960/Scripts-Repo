using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Authored faction card: name, alignment, leader, power, members, rebellion state and open demands.</summary>
public class FactionRowUI : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text alignmentText;
    [SerializeField] private TMP_Text leaderText;
    [SerializeField] private TMP_Text powerText;
    [SerializeField] private TMP_Text membersText;
    [SerializeField] private TMP_Text demandCountText;
    [SerializeField] private GameObject selectedMarker;
    [SerializeField] private GameObject rebellionMarker;

    public void Bind(Civilization civ, FactionBloc faction, bool selected, Action<FactionBloc> onClick)
    {
        GovernmentUiUtil.SetText(nameText, faction.FactionName);
        GovernmentUiUtil.SetText(alignmentText, faction.Alignment.ToString());
        GovernmentUiUtil.SetText(leaderText, faction.Leader != null ? $"Led by {GovernmentPresentation.FormatGovernorName(civ, faction.Leader)}" : "No leader");
        GovernmentUiUtil.SetText(powerText, $"Power {faction.ComputePower():0.#}");
        GovernmentUiUtil.SetText(membersText, $"{faction.Members.Count(m => m != null)} members");
        GovernmentUiUtil.SetText(demandCountText, $"{faction.ActiveDemands.Count} demand(s)");
        GovernmentUiUtil.SetActive(rebellionMarker, faction.IsInRebellion);
        GovernmentUiUtil.SetActive(selectedMarker, selected);
        GovernmentUiUtil.SetClick(button, () => onClick?.Invoke(faction));
    }
}
