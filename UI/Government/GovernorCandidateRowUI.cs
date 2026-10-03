using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GovernorCandidateRowUI : MonoBehaviour
{
    [SerializeField] private Image portraitImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text specializationText;
    [SerializeField] private TMP_Text personalityText;
    [SerializeField] private TMP_Text religionText;
    [SerializeField] private TMP_Text cultureText;
    [SerializeField] private TMP_Text loyaltyText;
    [SerializeField] private Button appointButton;
    [SerializeField] private TMP_Text appointButtonText;

    public void Bind(Civilization civ, GovernorCandidate candidate, bool canAppoint, Action<GovernorCandidate> onAppoint)
    {
        GovernmentUiUtil.SetImage(portraitImage, GovernorPortraitService.GetSprite(candidate?.portraitId));
        GovernmentUiUtil.SetText(nameText, candidate?.name ?? "Unnamed Candidate");
        GovernmentUiUtil.SetText(specializationText, candidate != null ? candidate.specialization.ToString() : string.Empty);
        GovernmentUiUtil.SetText(personalityText, candidate != null && candidate.personalityTraits != null && candidate.personalityTraits.Count > 0
            ? string.Join(" • ", candidate.personalityTraits) : "No notable personality");
        GovernmentUiUtil.SetText(religionText, candidate?.personalReligion != null ? GovernmentPresentation.NameOf(candidate.personalReligion) : "No Religion");
        GovernmentUiUtil.SetText(cultureText, candidate?.personalCulture != null ? GovernmentPresentation.NameOf(candidate.personalCulture) : "No Culture");
        GovernmentUiUtil.SetText(loyaltyText, candidate != null ? $"Loyalty {GovernmentUiUtil.Signed(candidate.StartingOpinion)}" : string.Empty);
        GovernmentUiUtil.SetText(appointButtonText, "APPOINT");
        GovernmentUiUtil.SetInteractable(appointButton, canAppoint && candidate != null);
        GovernmentUiUtil.SetClick(appointButton, () => { if (candidate != null) onAppoint?.Invoke(candidate); });
    }
}
