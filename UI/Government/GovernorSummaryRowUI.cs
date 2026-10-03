using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Authored compact governor row shared by the Government overview and Holdings header.</summary>
public class GovernorSummaryRowUI : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Image portraitImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text loyaltyText;
    [SerializeField] private TMP_Text personalityText;
    [SerializeField] private TMP_Text cityCountText;
    [SerializeField] private Transform warningAnchor;

    private GameObject warningInstance;

    public void Bind(Civilization civ, Governor governor, Action<Governor> onClick, GameObject warningPrefab)
    {
        BindCommon(civ, governor, warningPrefab);
        int cities = governor?.Cities?.Count ?? 0;
        GovernmentUiUtil.SetText(cityCountText, $"{cities} {(cities == 1 ? "City" : "Cities")}");
        GovernmentUiUtil.SetClick(button, () => onClick?.Invoke(governor));
        GovernmentUiUtil.SetInteractable(button, true);
    }

    public void BindHoldingsHeader(Civilization civ, Governor governor, GameObject warningPrefab)
    {
        BindCommon(civ, governor, warningPrefab);
        int cities = governor?.Cities?.Count ?? 0;
        int herds = governor?.Herds?.Count ?? 0;
        GovernmentUiUtil.SetText(cityCountText,
            $"{cities} {(cities == 1 ? "City" : "Cities")} • {herds} {(herds == 1 ? "Herd" : "Herds")}");
        GovernmentUiUtil.SetClick(button, null);
        GovernmentUiUtil.SetInteractable(button, false);
    }

    private void BindCommon(Civilization civ, Governor governor, GameObject warningPrefab)
    {
        GovernmentUiUtil.SetImage(portraitImage, governor != null ? GovernorPortraitService.GetSprite(governor.PortraitId) : null);
        GovernmentUiUtil.SetText(nameText, governor != null ? GovernmentPresentation.FormatGovernorName(civ, governor) : string.Empty);
        GovernmentUiUtil.SetText(loyaltyText, governor != null ? $"Loyalty {GovernmentUiUtil.Signed(governor.Opinion)}" : string.Empty);
        GovernmentUiUtil.SetText(personalityText, governor != null ? string.Join(", ", governor.PersonalityTraits.Take(2)) : string.Empty);
        SetWarning(governor != null && (governor.IsInRebellion
            || governor.Opinion <= PoliticalWarningBuilder.GovernorDiscontentOpinion), warningPrefab);
    }

    private void SetWarning(bool visible, GameObject warningPrefab)
    {
        if (visible && warningInstance == null && warningPrefab != null && warningAnchor != null)
        {
            warningInstance = Instantiate(warningPrefab, warningAnchor);
            if (warningInstance.transform is RectTransform rt)
            {
                rt.anchorMin = new Vector2(.5f, .5f);
                rt.anchorMax = new Vector2(.5f, .5f);
                rt.anchoredPosition = Vector2.zero;
            }
        }
        if (warningInstance != null) warningInstance.SetActive(visible);
    }
}
