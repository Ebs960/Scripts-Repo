using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Authored compact vassal row used only by the Government overview.</summary>
public class VassalSummaryRowUI : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text opinionText;
    [SerializeField] private TMP_Text libertyText;
    [SerializeField] private TMP_Text cityCountText;
    [SerializeField] private GameObject warningMarker;

    public void Bind(Civilization overlord, VassalContract contract, Action<VassalContract> onClick)
    {
        var manager = SubjectManager.Instance;
        var subject = contract?.subject;
        float threshold = contract != null ? contract.EffectiveBreakawayThreshold : 100f;
        GovernmentUiUtil.SetImage(iconImage, subject?.civData?.icon);
        GovernmentUiUtil.SetText(nameText, contract?.subjectCivName ?? string.Empty);
        GovernmentUiUtil.SetText(opinionText, contract != null && manager != null ? $"Opinion {GovernmentUiUtil.Signed(manager.GetEffectiveSubjectOpinion(contract))}" : string.Empty);
        GovernmentUiUtil.SetText(libertyText, contract != null ? $"Liberty {contract.libertyDesire:0}/{threshold:0}" : string.Empty);
        int cities = subject?.cities?.Count ?? 0;
        GovernmentUiUtil.SetText(cityCountText, $"{cities} {(cities == 1 ? "City" : "Cities")}");
        bool warning = contract != null && (contract.libertyDesire >= threshold * 0.75f
            || manager != null && manager.GetPendingIndependenceDemand(overlord, subject) != null);
        GovernmentUiUtil.SetActive(warningMarker, warning);
        GovernmentUiUtil.SetClick(button, () => onClick?.Invoke(contract));
    }
}
