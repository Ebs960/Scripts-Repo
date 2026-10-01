using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Authored government card/row: small icon, name and current/available/locked state.</summary>
public class GovernmentTypeRowUI : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text stateText;
    [SerializeField] private GameObject selectedMarker;
    [SerializeField] private GameObject currentMarker;
    [SerializeField] private GameObject availableMarker;
    [SerializeField] private GameObject lockedMarker;

    public void Bind(GovernmentData government, GovernmentAdoptionEvaluation evaluation, bool selected, Action<GovernmentData> onClick)
    {
        GovernmentUiUtil.SetText(nameText, GovernmentPresentation.NameOf(government));
        GovernmentUiUtil.SetImage(iconImage, government != null ? government.icon : null);

        bool current = evaluation != null && evaluation.isCurrentGovernment;
        bool available = evaluation != null && evaluation.canAdopt;
        GovernmentUiUtil.SetText(stateText, current ? "Current" : available ? "Available" : "Locked");
        GovernmentUiUtil.SetActive(currentMarker, current);
        GovernmentUiUtil.SetActive(availableMarker, !current && available);
        GovernmentUiUtil.SetActive(lockedMarker, !current && !available);
        GovernmentUiUtil.SetActive(selectedMarker, selected);
        GovernmentUiUtil.SetClick(button, () => onClick?.Invoke(government));
    }
}
