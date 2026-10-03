using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Authored compact governor row used only by the Government overview.</summary>
public class GovernorSummaryRowUI : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Image portraitImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text loyaltyText;
    [SerializeField] private TMP_Text personalityText;
    [SerializeField] private TMP_Text cityCountText;
    [SerializeField] private GameObject warningMarker;

    public void Bind(Civilization civ, Governor governor, Action<Governor> onClick)
    {
        GovernmentUiUtil.SetImage(portraitImage, governor != null ? GovernorPortraitService.GetSprite(governor.PortraitId) : null);
        GovernmentUiUtil.SetText(nameText, governor != null ? GovernmentPresentation.FormatGovernorName(civ, governor) : string.Empty);
        GovernmentUiUtil.SetText(loyaltyText, governor != null ? $"Loyalty {GovernmentUiUtil.Signed(governor.Opinion)}" : string.Empty);
        GovernmentUiUtil.SetText(personalityText, governor != null ? string.Join(", ", governor.PersonalityTraits.Take(2)) : string.Empty);
        int cities = governor?.Cities?.Count ?? 0;
        GovernmentUiUtil.SetText(cityCountText, $"{cities} {(cities == 1 ? "City" : "Cities")}");
        GovernmentUiUtil.SetActive(warningMarker, governor != null && (governor.IsInRebellion || governor.Opinion < 0));
        GovernmentUiUtil.SetClick(button, () => onClick?.Invoke(governor));
    }
}
