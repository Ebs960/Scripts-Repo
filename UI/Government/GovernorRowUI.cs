using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Authored governor card: portrait, government-specific title + name, opinion and power.</summary>
public class GovernorRowUI : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Image portraitImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text opinionText;
    [SerializeField] private TMP_Text powerText;
    [SerializeField] private Image selectedMarker;
    [SerializeField] private Image councilMarker;
    [SerializeField] private Image rebellionMarker;
    [SerializeField] private Image warningMarker;

    public void Bind(Civilization civ, Governor governor, bool selected, Action<Governor> onClick)
    {
        GovernmentUiUtil.SetText(nameText, GovernmentPresentation.FormatGovernorName(civ, governor));
        GovernmentUiUtil.SetText(opinionText, governor != null ? $"Opinion {GovernmentUiUtil.Signed(governor.Opinion)}" : string.Empty);
        GovernmentUiUtil.SetText(powerText, governor != null ? $"Power {governor.PowerRank}" : string.Empty);
        GovernmentUiUtil.SetImage(portraitImage, governor != null ? GovernorPortraitService.GetSprite(governor.PortraitId) : null);
        SetMarker(councilMarker, governor != null && governor.IsOnCouncil);
        SetMarker(rebellionMarker, governor != null && governor.IsInRebellion);
        SetMarker(selectedMarker, selected);
        SetMarker(warningMarker, governor != null && governor.Opinion <= PoliticalWarningBuilder.GovernorDiscontentOpinion);
        GovernmentUiUtil.SetClick(button, () => onClick?.Invoke(governor));
    }

    private static void SetMarker(Image marker, bool visible)
    {
        if (marker != null) marker.enabled = visible;
    }
}
