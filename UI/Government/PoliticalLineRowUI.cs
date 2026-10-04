using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Authored one-line row: label + value with one optional status marker. Reused for effects, requirements, warnings and plain text.</summary>
public class PoliticalLineRowUI : MonoBehaviour
{
    [SerializeField] private TMP_Text labelText;
    [SerializeField] private TMP_Text valueText;
    [SerializeField] private Button button;

    [Header("Marker")]
    [SerializeField] private Image markerImage;
    [SerializeField] private Sprite beneficialSprite;
    [SerializeField] private Sprite harmfulSprite;
    [SerializeField] private Sprite metSprite;
    [SerializeField] private Sprite unmetSprite;
    [SerializeField] private Sprite cautionSprite;
    [SerializeField] private Sprite criticalSprite;

    public void BindText(string label, string value = null)
    {
        Apply(label, value);
        GovernmentUiUtil.SetImage(markerImage, null);
        GovernmentUiUtil.SetClick(button, null);
    }

    public void BindEffect(PoliticalEffectLine line)
    {
        Apply(line.label, line.value);
        GovernmentUiUtil.SetImage(markerImage, line.harmful ? harmfulSprite : line.beneficial ? beneficialSprite : null);
        GovernmentUiUtil.SetClick(button, null);
    }

    public void BindRequirement(PoliticalRequirementLine line)
    {
        Apply(line.label, line.met ? "Met" : "Not met");
        GovernmentUiUtil.SetImage(markerImage, line.met ? metSprite : unmetSprite);
        GovernmentUiUtil.SetClick(button, null);
    }

    public void BindWarning(PoliticalWarning warning, Action<PoliticalWarningDestination> onClick)
    {
        Apply(warning.text, null);
        GovernmentUiUtil.SetImage(markerImage, warning.severity == PoliticalWarningSeverity.Critical ? criticalSprite
            : warning.severity == PoliticalWarningSeverity.Caution ? cautionSprite : null);
        PoliticalWarningDestination destination = warning.destination;
        GovernmentUiUtil.SetClick(button, onClick == null ? null : (Action)(() => onClick(destination)));
    }

    private void Apply(string label, string value)
    {
        GovernmentUiUtil.SetText(labelText, label);
        GovernmentUiUtil.SetText(valueText, value);
    }
}
