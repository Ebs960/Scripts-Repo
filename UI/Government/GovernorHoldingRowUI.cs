using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Compact assignment row for a City or Herd shown in the Governor Holdings section. Displays
/// basic holding information, yields, current controller, assignment action, and political warning state.
/// </summary>
public class GovernorHoldingRowUI : MonoBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text detailText;
    [SerializeField] private TMP_Text yieldsText;
    [SerializeField] private TMP_Text ownerText;
    [SerializeField] private Button actionButton;
    [SerializeField] private TMP_Text actionLabel;
    [SerializeField] private GameObject warningMarker;

    public void Bind(string holdingName, string detail, string yields, string owner, string actionText,
        bool dangerous, bool interactable, Action onClick)
    {
        GovernmentUiUtil.SetText(nameText, holdingName);
        GovernmentUiUtil.SetText(detailText, detail);
        GovernmentUiUtil.SetText(yieldsText, yields);
        GovernmentUiUtil.SetText(ownerText, owner);
        GovernmentUiUtil.SetText(actionLabel, actionText);
        if (warningMarker != null) warningMarker.SetActive(dangerous);
        GovernmentUiUtil.SetInteractable(actionButton, interactable);
        GovernmentUiUtil.SetClick(actionButton, onClick);
    }
}
