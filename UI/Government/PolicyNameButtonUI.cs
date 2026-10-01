using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Authored policy list entry. Intentionally text-only: the policy name is its identity, no icon or art required.</summary>
public class PolicyNameButtonUI : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text costText;
    [SerializeField] private GameObject selectedMarker;
    [SerializeField] private GameObject activeMarker;
    [SerializeField] private GameObject availableMarker;
    [SerializeField] private GameObject lockedMarker;

    public void Bind(PolicyData policy, PolicyAdoptionEvaluation evaluation, bool selected, Action<PolicyData> onClick)
    {
        GovernmentUiUtil.SetText(nameText, GovernmentPresentation.NameOf(policy));
        GovernmentUiUtil.SetText(costText, policy != null ? $"{policy.policyPointCost} PP" : string.Empty);

        var state = evaluation != null ? evaluation.State : PolicyListState.Locked;
        GovernmentUiUtil.SetActive(activeMarker, state == PolicyListState.Active);
        GovernmentUiUtil.SetActive(availableMarker, state == PolicyListState.Available);
        GovernmentUiUtil.SetActive(lockedMarker, state == PolicyListState.Locked);
        GovernmentUiUtil.SetActive(selectedMarker, selected);
        GovernmentUiUtil.SetClick(button, () => onClick?.Invoke(policy));
    }
}
