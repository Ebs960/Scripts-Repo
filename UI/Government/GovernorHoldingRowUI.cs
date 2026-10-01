using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Authored row in the holdings subpanel: a city or herd with its governor status and an assign/remove button.</summary>
public class GovernorHoldingRowUI : MonoBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button actionButton;
    [SerializeField] private TMP_Text actionLabel;

    public void Bind(string holdingName, string status, string actionText, bool interactable, Action onClick)
    {
        GovernmentUiUtil.SetText(nameText, holdingName);
        GovernmentUiUtil.SetText(statusText, status);
        GovernmentUiUtil.SetText(actionLabel, actionText);
        GovernmentUiUtil.SetInteractable(actionButton, interactable);
        GovernmentUiUtil.SetClick(actionButton, onClick);
    }
}
