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
    [SerializeField] private GameObject selectedMarker;
    [SerializeField] private GameObject councilMarker;
    [SerializeField] private GameObject rebellionMarker;

    public void Bind(Civilization civ, Governor governor, bool selected, Action<Governor> onClick)
    {
        GovernmentUiUtil.SetText(nameText, GovernmentPresentation.FormatGovernorName(civ, governor));
        GovernmentUiUtil.SetText(opinionText, governor != null ? $"Opinion {GovernmentUiUtil.Signed(governor.Opinion)}" : string.Empty);
        GovernmentUiUtil.SetText(powerText, governor != null ? $"Power {governor.PowerRank}" : string.Empty);
        GovernmentUiUtil.SetImage(portraitImage, governor != null ? GovernorPortraitService.GetSprite(governor.PortraitId) : null);
        GovernmentUiUtil.SetActive(councilMarker, governor != null && governor.IsOnCouncil);
        GovernmentUiUtil.SetActive(rebellionMarker, governor != null && governor.IsInRebellion);
        GovernmentUiUtil.SetActive(selectedMarker, selected);
        GovernmentUiUtil.SetClick(button, () => onClick?.Invoke(governor));
    }
}
