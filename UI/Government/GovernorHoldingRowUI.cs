using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum GovernorHoldingAction
{
    Assign,
    Remove,
    Transfer
}

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

    [Header("Action")]
    [SerializeField] private Button actionButton;
    [SerializeField] private Image actionButtonImage;
    [SerializeField] private Sprite assignButtonSprite;
    [SerializeField] private Sprite removeButtonSprite;
    [SerializeField] private Sprite transferButtonSprite;

    [Header("Status")]
    [SerializeField] private Image warningMarker;

    private void Awake()
    {
        if (actionButtonImage == null && actionButton != null)
            actionButtonImage = actionButton.image;

        if (warningMarker != null)
            warningMarker.raycastTarget = false;
    }

    public void Bind(string holdingName, string detail, string yields, string owner, GovernorHoldingAction action,
        bool dangerous, bool interactable, Action onClick)
    {
        GovernmentUiUtil.SetText(nameText, holdingName);
        GovernmentUiUtil.SetText(detailText, detail);
        GovernmentUiUtil.SetText(yieldsText, yields);
        GovernmentUiUtil.SetText(ownerText, owner);
        ApplyActionVisual(action);
        if (warningMarker != null) warningMarker.gameObject.SetActive(dangerous);
        GovernmentUiUtil.SetInteractable(actionButton, interactable);
        GovernmentUiUtil.SetClick(actionButton, onClick);
    }

    private void ApplyActionVisual(GovernorHoldingAction action)
    {
        Sprite sprite = null;

        switch (action)
        {
            case GovernorHoldingAction.Assign:
                sprite = assignButtonSprite;
                break;
            case GovernorHoldingAction.Remove:
                sprite = removeButtonSprite;
                break;
            case GovernorHoldingAction.Transfer:
                sprite = transferButtonSprite;
                break;
        }

        if (actionButtonImage != null)
            actionButtonImage.sprite = sprite;
    }
}
