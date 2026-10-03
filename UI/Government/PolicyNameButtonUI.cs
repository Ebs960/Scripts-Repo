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
    [SerializeField] private Sprite selectedMarker;
    [SerializeField] private Sprite activeMarker;
    [SerializeField] private Sprite availableMarker;
    [SerializeField] private Sprite lockedMarker;
    [SerializeField] private Image selectedMarkerImage;
    [SerializeField] private Image stateMarkerImage;

    public void Bind(PolicyData policy, PolicyAdoptionEvaluation evaluation, bool selected, Action<PolicyData> onClick)
    {
        GovernmentUiUtil.SetText(nameText, GovernmentPresentation.NameOf(policy));
        GovernmentUiUtil.SetText(costText, policy != null ? $"{policy.policyPointCost} PP" : string.Empty);

        var state = evaluation != null ? evaluation.State : PolicyListState.Locked;
        Sprite stateMarker = state == PolicyListState.Active ? activeMarker
            : state == PolicyListState.Available ? availableMarker : lockedMarker;
        SetMarker(stateMarkerImage, stateMarker);
        SetMarker(selectedMarkerImage, selected ? selectedMarker : null);
        GovernmentUiUtil.SetClick(button, () => onClick?.Invoke(policy));
    }

    private static void SetMarker(Image image, Sprite marker)
    {
        if (image == null) return;
        image.sprite = marker;
        image.enabled = marker != null;
    }
}
