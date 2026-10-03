using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Authored row shared by city and herd holdings.</summary>
public class GovernorHoldingRowUI : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text detailText;
    [SerializeField] private TMP_Text ownerText;
    [SerializeField] private Button actionButton;
    [SerializeField] private TMP_Text actionLabel;
    [SerializeField] private GameObject warningMarker;

    public void Bind(Sprite icon, string holdingName, string detail, string owner, string actionText,
        bool dangerous, bool interactable, Action onClick)
    {
        if (iconImage != null) { iconImage.sprite = icon; iconImage.gameObject.SetActive(icon != null); }
        GovernmentUiUtil.SetText(nameText, holdingName);
        GovernmentUiUtil.SetText(detailText, detail);
        GovernmentUiUtil.SetText(ownerText, owner);
        GovernmentUiUtil.SetText(actionLabel, actionText);
        if (warningMarker != null) warningMarker.SetActive(dangerous);
        GovernmentUiUtil.SetInteractable(actionButton, interactable);
        GovernmentUiUtil.SetClick(actionButton, onClick);
    }
}
