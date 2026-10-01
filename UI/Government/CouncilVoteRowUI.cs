using TMPro;
using UnityEngine;

/// <summary>Authored council row: either a vote summary (BindResult) or one councillor's vote (BindIndividual).</summary>
public class CouncilVoteRowUI : MonoBehaviour
{
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text verdictText;
    [SerializeField] private TMP_Text detailText;
    [SerializeField] private GameObject yesMarker;
    [SerializeField] private GameObject noMarker;

    public void BindResult(CouncilVoteResult result)
    {
        GovernmentUiUtil.SetText(titleText, result.proposalDescription);
        GovernmentUiUtil.SetText(verdictText, result.Summary);
        GovernmentUiUtil.SetText(detailText, result.applicable ? $"{result.requiredYesVotes} yes votes were needed" : string.Empty);
        GovernmentUiUtil.SetActive(yesMarker, result.applicable && result.passed);
        GovernmentUiUtil.SetActive(noMarker, result.applicable && !result.passed);
    }

    public void BindIndividual(Civilization civ, CouncilVote vote)
    {
        GovernmentUiUtil.SetText(titleText, GovernmentPresentation.FormatGovernorName(civ, vote.governorId, vote.governorName));
        GovernmentUiUtil.SetText(verdictText, vote.approve ? "YES" : "NO");
        GovernmentUiUtil.SetText(detailText, vote.primaryReason);
        GovernmentUiUtil.SetActive(yesMarker, vote.approve);
        GovernmentUiUtil.SetActive(noMarker, !vote.approve);
    }
}
