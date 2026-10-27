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
    [SerializeField] private Sprite activeMarker;
    [SerializeField] private Sprite availableMarker;
    [SerializeField] private Sprite lockedMarker;
    [SerializeField] private Image markerImage;

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
        GovernmentUiUtil.SetText(statusText, StatusLabel(state));
        SetMarker(state == PolicyListState.Active ? activeMarker
            : state == PolicyListState.Locked ? lockedMarker : availableMarker);
        GovernmentUiUtil.SetInteractable(button, state != PolicyListState.Locked);
        GovernmentUiUtil.SetClick(button, () =>
        {
            if (evaluation == null) return;
            if (evaluation.alreadyActive) onDismiss?.Invoke();
            else if (evaluation.canAdopt || evaluation.CanAdoptWithReplacement) onSelected?.Invoke(policy);
        });
    }

    public static string StatusLabel(PolicyListState state)
        => state == PolicyListState.Locked ? "Locked"
            : state == PolicyListState.Replace ? "Available" : state.ToString();

    /// <summary>Non-policy row such as a repeal, replacement choice, or cancel action.</summary>
    public void BindAction(string label, string status, bool interactable, Action onSelected)
    {
        civ = null; policy = null; evaluation = null; tooltip = null;
        GovernmentUiUtil.SetText(policyNameText, label); GovernmentUiUtil.SetText(statusText, status);
        SetMarker(interactable ? availableMarker : lockedMarker);
        GovernmentUiUtil.SetInteractable(button, interactable); GovernmentUiUtil.SetClick(button, onSelected);
    }

    private void SetMarker(Sprite marker)
    {
        if (markerImage == null) return;
        markerImage.sprite = marker;
        markerImage.enabled = marker != null;
    }

    public void OnPointerEnter(PointerEventData eventData)
    { if (policy != null) tooltip?.ScheduleShow(civ, policy, evaluation, eventData.position); }
    public void OnPointerExit(PointerEventData eventData) => tooltip?.Hide();
    private void OnDisable() => tooltip?.Hide();
}
