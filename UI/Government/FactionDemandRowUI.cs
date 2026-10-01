using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Authored faction demand card with Accept / Refuse buttons and an inline failure message.</summary>
public class FactionDemandRowUI : MonoBehaviour
{
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private TMP_Text targetsText;
    [SerializeField] private TMP_Text expiryText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button acceptButton;
    [SerializeField] private Button refuseButton;

    public void Bind(FactionBloc faction, FactionDemand demand, string failureReason, Action<FactionBloc, FactionDemand, bool> onResolve)
    {
        GovernmentUiUtil.SetText(descriptionText, demand.description);
        GovernmentUiUtil.SetText(targetsText, BuildTargets(demand));
        GovernmentUiUtil.SetText(expiryText, $"Issued turn {demand.issuedTurn}, expires turn {demand.issuedTurn + demand.expiryTurns}");
        GovernmentUiUtil.SetText(statusText, failureReason);
        GovernmentUiUtil.SetClick(acceptButton, () => onResolve?.Invoke(faction, demand, true));
        GovernmentUiUtil.SetClick(refuseButton, () => onResolve?.Invoke(faction, demand, false));
    }

    private static string BuildTargets(FactionDemand demand)
    {
        var sb = new StringBuilder();
        if (demand.targetPolicy != null) sb.Append("Policy: ").Append(GovernmentPresentation.NameOf(demand.targetPolicy)).Append("  ");
        if (demand.targetGovernment != null) sb.Append("Government: ").Append(GovernmentPresentation.NameOf(demand.targetGovernment)).Append("  ");
        if (demand.targetGovernor != null) sb.Append("Governor: ").Append(demand.targetGovernor.Name).Append("  ");
        if (demand.targetReligion != null) sb.Append("Religion: ").Append(GovernmentPresentation.NameOf(demand.targetReligion));
        return sb.ToString().Trim();
    }
}
