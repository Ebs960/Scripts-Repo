using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Authored policy choice with deterministic click and delayed pointer-hover behavior.</summary>
public class PolicyDropdownOptionUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text policyNameText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Image activeMarker;
    [SerializeField] private Image availableMarker;
    [SerializeField] private Image lockedMarker;

    private Civilization civ;
    private PolicyData policy;
    private PolicyAdoptionEvaluation evaluation;
    private PolicyTooltipUI tooltip;

    public void Bind(Civilization civilization, PolicyData value, PolicyAdoptionEvaluation result,
        PolicyTooltipUI sharedTooltip, Action<PolicyData> onSelected, Action onDismiss)
    {
        civ = civilization; policy = value; evaluation = result; tooltip = sharedTooltip;
        var state = result != null ? result.State : PolicyListState.Locked;
        GovernmentUiUtil.SetText(policyNameText, GovernmentPresentation.NameOf(value));
        GovernmentUiUtil.SetText(statusText, state == PolicyListState.Locked ? "Requirements not met" : state.ToString());
        SetMarkerVisible(activeMarker, state == PolicyListState.Active);
        SetMarkerVisible(availableMarker, state == PolicyListState.Available);
        SetMarkerVisible(lockedMarker, state == PolicyListState.Locked);
        GovernmentUiUtil.SetInteractable(button, state != PolicyListState.Locked);
        GovernmentUiUtil.SetClick(button, () =>
        {
            if (evaluation != null && evaluation.canAdopt) onSelected?.Invoke(policy);
            else if (evaluation != null && evaluation.alreadyActive) onDismiss?.Invoke();
        });
    }

    public void BindNone(Action onSelected)
    {
        civ = null; policy = null; evaluation = null; tooltip = null;
        GovernmentUiUtil.SetText(policyNameText, "None"); GovernmentUiUtil.SetText(statusText, "Repeal current policy");
        SetMarkerVisible(activeMarker, false); SetMarkerVisible(availableMarker, true); SetMarkerVisible(lockedMarker, false);
        GovernmentUiUtil.SetInteractable(button, true); GovernmentUiUtil.SetClick(button, onSelected);
    }

    private static void SetMarkerVisible(Image marker, bool visible)
    {
        if (marker != null) marker.enabled = visible;
    }

    public void OnPointerEnter(PointerEventData eventData)
    { if (policy != null) tooltip?.ScheduleShow(civ, policy, evaluation, eventData.position); }
    public void OnPointerExit(PointerEventData eventData) => tooltip?.Hide();
    private void OnDisable() => tooltip?.Hide();
}
