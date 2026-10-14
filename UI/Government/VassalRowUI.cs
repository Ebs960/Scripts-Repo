using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Authored vassal card: realm name, liberty desire, effective opinion and tribute summary.</summary>
public class VassalRowUI : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Image civIconImage;
    [SerializeField] private TMP_Text libertyText;
    [SerializeField] private TMP_Text opinionText;
    [SerializeField] private TMP_Text tributeText;
    [SerializeField] private Graphic selectedHighlightGraphic;
    [SerializeField] private GameObject restlessMarker;

    public void Bind(VassalContract contract, bool selected, bool restless, Action<VassalContract> onClick)
    {
        float opinion = SubjectManager.Instance != null ? SubjectManager.Instance.GetEffectiveSubjectOpinion(contract) : contract.subjectOpinion;
        Sprite icon = contract != null && contract.subject != null && contract.subject.civData != null
            ? contract.subject.civData.icon
            : null;

        GovernmentUiUtil.SetImage(civIconImage, icon);
        GovernmentUiUtil.SetText(nameText, contract.subjectCivName);
        GovernmentUiUtil.SetText(libertyText, $"Liberty {contract.libertyDesire:0}/{contract.EffectiveBreakawayThreshold:0}");
        GovernmentUiUtil.SetText(opinionText, $"Opinion {GovernmentUiUtil.Signed(opinion)}");
        GovernmentUiUtil.SetText(tributeText, FormatTribute(contract));
        GovernmentUiUtil.SetActive(restlessMarker, restless);
        if (selectedHighlightGraphic != null)
            selectedHighlightGraphic.gameObject.SetActive(selected);
        GovernmentUiUtil.SetClick(button, () => onClick?.Invoke(contract));
    }

    public static string FormatTribute(VassalContract c)
    {
        string text = $"Gold {c.goldTributePct:P0} \u2022 Food {c.foodTributePct:P0}";
        if (c.resourceTribute != null && c.resourceTributePerTurn > 0)
            text += $" \u2022 {c.resourceTributePerTurn} {c.resourceTribute.resourceName}/turn";
        return text;
    }
}
