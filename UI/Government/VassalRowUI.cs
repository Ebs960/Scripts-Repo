using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Authored vassal card: realm name, liberty desire, effective opinion, autonomy and tribute summary.</summary>
public class VassalRowUI : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text libertyText;
    [SerializeField] private TMP_Text opinionText;
    [SerializeField] private TMP_Text autonomyText;
    [SerializeField] private TMP_Text tributeText;
    [SerializeField] private GameObject selectedMarker;
    [SerializeField] private GameObject restlessMarker;

    public void Bind(VassalContract contract, bool selected, bool restless, Action<VassalContract> onClick)
    {
        float opinion = SubjectManager.Instance != null ? SubjectManager.Instance.GetEffectiveSubjectOpinion(contract) : contract.subjectOpinion;
        GovernmentUiUtil.SetText(nameText, contract.subjectCivName);
        GovernmentUiUtil.SetText(libertyText, $"Liberty {contract.libertyDesire:0}/{contract.EffectiveBreakawayThreshold:0}");
        GovernmentUiUtil.SetText(opinionText, $"Opinion {GovernmentUiUtil.Signed(opinion)}");
        GovernmentUiUtil.SetText(autonomyText, $"Autonomy {contract.autonomyLevel}");
        GovernmentUiUtil.SetText(tributeText, FormatTribute(contract));
        GovernmentUiUtil.SetActive(restlessMarker, restless);
        GovernmentUiUtil.SetActive(selectedMarker, selected);
        GovernmentUiUtil.SetClick(button, () => onClick?.Invoke(contract));
    }

    public static string FormatTribute(VassalContract c)
        => $"Tribute: gold {c.goldTributePct:P0}, science {c.scienceTributePct:P0}, food {c.foodTributePct:P0}";
}
