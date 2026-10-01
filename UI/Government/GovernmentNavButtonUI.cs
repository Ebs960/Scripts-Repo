using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Authored tab / filter button. Pure data binding: label, selected marker, click callback.</summary>
public class GovernmentNavButtonUI : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text label;
    [SerializeField] private GameObject selectedMarker;

    public void Bind(string text, bool selected, Action onClick)
    {
        GovernmentUiUtil.SetText(label, text);
        GovernmentUiUtil.SetActive(selectedMarker, selected);
        GovernmentUiUtil.SetClick(button, onClick);
    }
}
