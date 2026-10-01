using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Authored election candidate card with an endorse button. Endorsement itself runs through ElectionManager.</summary>
public class ElectionCandidateRowUI : MonoBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text factionText;
    [SerializeField] private TMP_Text competenceText;
    [SerializeField] private TMP_Text appealText;
    [SerializeField] private TMP_Text prioritiesText;
    [SerializeField] private TMP_Text supportText;
    [SerializeField] private GameObject incumbentMarker;
    [SerializeField] private GameObject endorsedMarker;
    [SerializeField] private Button endorseButton;

    public void Bind(ElectionCandidateRecord candidate, bool endorsed, bool canEndorse, bool showFinalSupport, Action<ElectionCandidateRecord> onEndorse)
    {
        GovernmentUiUtil.SetText(nameText, candidate.displayName);
        GovernmentUiUtil.SetText(factionText, string.IsNullOrEmpty(candidate.factionName) ? "Independent" : candidate.factionName);
        GovernmentUiUtil.SetText(competenceText, $"Competence {candidate.competence:0.##}");
        GovernmentUiUtil.SetText(appealText, $"Elite appeal {candidate.eliteAppeal:0.##} / Public appeal {candidate.publicAppeal:0.##}");
        GovernmentUiUtil.SetText(prioritiesText, candidate.priorities != null && candidate.priorities.Count > 0
            ? "Priorities: " + string.Join(", ", candidate.priorities.Select(p => p.ToString())) : string.Empty);
        GovernmentUiUtil.SetText(supportText, showFinalSupport ? $"Support {candidate.finalSupport:0.##}" : string.Empty);
        GovernmentUiUtil.SetActive(incumbentMarker, candidate.incumbent);
        GovernmentUiUtil.SetActive(endorsedMarker, endorsed);
        GovernmentUiUtil.SetInteractable(endorseButton, canEndorse && !endorsed);
        GovernmentUiUtil.SetClick(endorseButton, () => onEndorse?.Invoke(candidate));
    }
}
