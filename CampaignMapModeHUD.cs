using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>Inspector-bindable HUD adapter; it does not intercept map input or perform raycasts.</summary>
public class CampaignMapModeHUD : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown modeDropdown;
    [SerializeField] private TMP_Text legendText;
    [SerializeField] private TMP_Text hoverText;
    private CampaignMapModeController controller;

    private static readonly string[] Labels =
    { "Terrain", "Political", "Government", "Religion", "Continents", "Administration", "Diplomacy" };

    private void OnEnable()
    {
        controller = CampaignMapModeController.Instance ?? FindAnyObjectByType<CampaignMapModeController>();
        if (modeDropdown != null)
        {
            modeDropdown.ClearOptions(); modeDropdown.AddOptions(new List<string>(Labels));
            modeDropdown.SetValueWithoutNotify(controller != null ? (int)controller.CurrentMode : 0);
            modeDropdown.onValueChanged.AddListener(OnModeSelected);
        }
        if (controller != null)
        {
            controller.MapModeChanged += OnModeChanged; controller.LegendChanged += RefreshLegend;
            controller.HoverInfoChanged += SetHover; RefreshLegend();
        }
    }

    private void OnDisable()
    {
        if (modeDropdown != null) modeDropdown.onValueChanged.RemoveListener(OnModeSelected);
        if (controller != null)
        { controller.MapModeChanged -= OnModeChanged; controller.LegendChanged -= RefreshLegend; controller.HoverInfoChanged -= SetHover; }
    }

    private void OnModeSelected(int index) => controller?.SetMode((CampaignMapMode)Mathf.Clamp(index, 0, Labels.Length - 1));
    private void OnModeChanged(CampaignMapMode mode) { modeDropdown?.SetValueWithoutNotify((int)mode); RefreshLegend(); }
    private void SetHover(string value) { if (hoverText != null) hoverText.text = value; }
    private void RefreshLegend()
    {
        if (legendText == null || controller == null) return;
        var title = controller.CurrentMode == CampaignMapMode.Diplomacy
            ? $"DIPLOMACY — Relative to {controller.ReferenceCivilization?.civData?.civName ?? "Player"}"
            : controller.CurrentMode.ToString().ToUpperInvariant();
        var builder = new StringBuilder(title);
        foreach (var entry in controller.Legend)
            builder.Append("\n■ ").Append(entry.label).Append(" (").Append(entry.tileCount).Append(')');
        legendText.text = builder.ToString();
    }
}
