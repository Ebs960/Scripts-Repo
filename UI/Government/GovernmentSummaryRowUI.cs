using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Generic compact row used by Government overview summary lists.</summary>
public class GovernmentSummaryRowUI : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Image portraitImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text primaryText;
    [SerializeField] private TMP_Text secondaryText;
    [SerializeField] private GameObject warningMarker;

    public void Bind(Sprite portrait, string name, string primary, string secondary, bool warning, Action onClick)
    {
        GovernmentUiUtil.SetImage(portraitImage, portrait);
        GovernmentUiUtil.SetText(nameText, name);
        GovernmentUiUtil.SetText(primaryText, primary);
        GovernmentUiUtil.SetText(secondaryText, secondary);
        GovernmentUiUtil.SetActive(warningMarker, warning);
        GovernmentUiUtil.SetClick(button, onClick);
    }
}
