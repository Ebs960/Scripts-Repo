using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Selectable government option in the selection list: icon and name only.</summary>
public class GovernmentSelectionOptionUI : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text nameText;

    public void Bind(GovernmentData government, Action<GovernmentData> onClick)
    {
        GovernmentUiUtil.SetText(nameText, GovernmentPresentation.NameOf(government));
        GovernmentUiUtil.SetImage(iconImage, government != null ? government.icon : null);
        GovernmentUiUtil.SetClick(button, () => onClick?.Invoke(government));
    }
}
