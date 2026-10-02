using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Authored one-line row: label + value with optional good/bad/met markers. Reused for effects, requirements, warnings and plain text.</summary>
public class PoliticalLineRowUI : MonoBehaviour
{
    [SerializeField] private TMP_Text labelText;
    [SerializeField] private TMP_Text valueText;
    [SerializeField] private GameObject beneficialMarker;
    [SerializeField] private GameObject harmfulMarker;
    [SerializeField] private GameObject metMarker;
    [SerializeField] private GameObject unmetMarker;
    [SerializeField] private GameObject cautionMarker;
    [SerializeField] private GameObject criticalMarker;
    [SerializeField] private Button button;

    public void BindText(string label, string value = null)
    {
        Apply(label, value);
        SetMarkers(false, false, false, false, false, false);
        GovernmentUiUtil.SetClick(button, null);
    }

    public void BindEffect(PoliticalEffectLine line)
    {
        Apply(line.label, line.value);
        SetMarkers(line.beneficial, line.harmful, false, false, false, false);
        GovernmentUiUtil.SetClick(button, null);
    }

    public void BindRequirement(PoliticalRequirementLine line)
    {
        Apply(line.label, line.met ? "Met" : "Not met");
        SetMarkers(false, false, line.met, !line.met, false, false);
        GovernmentUiUtil.SetClick(button, null);
    }

    public void BindWarning(PoliticalWarning warning, Action<PoliticalWarningDestination> onClick)
    {
        Apply(warning.text, null);
        SetMarkers(false, false, false, false,
            warning.severity == PoliticalWarningSeverity.Caution, warning.severity == PoliticalWarningSeverity.Critical);
        PoliticalWarningDestination destination = warning.destination;
        GovernmentUiUtil.SetClick(button, onClick == null ? null : (Action)(() => onClick(destination)));
    }

    private void Apply(string label, string value)
    {
        GovernmentUiUtil.SetText(labelText, label);
        GovernmentUiUtil.SetText(valueText, value);
    }

    private void SetMarkers(bool beneficial, bool harmful, bool met, bool unmet, bool caution, bool critical)
    {
        GovernmentUiUtil.SetActive(beneficialMarker, beneficial);
        GovernmentUiUtil.SetActive(harmfulMarker, harmful);
        GovernmentUiUtil.SetActive(metMarker, met);
        GovernmentUiUtil.SetActive(unmetMarker, unmet);
        GovernmentUiUtil.SetActive(cautionMarker, caution);
        GovernmentUiUtil.SetActive(criticalMarker, critical);
    }
}
