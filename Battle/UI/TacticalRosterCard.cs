using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum TacticalRosterCardState { Ready, Waiting, Acted, Deployed, Reserve }

/// <summary>Reusable authored roster card. Art carries no unit data; this binds icon, health and readiness.</summary>
public sealed class TacticalRosterCard : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI fallbackLabel;
    [SerializeField] private GameObject selectionIndicator;
    [SerializeField] private RectTransform healthFill;
    [SerializeField] private Image healthFillImage;
    [SerializeField] private Image statusDot;
    [SerializeField] private GameObject dimOverlay;
    [SerializeField] private BattleHoverTip hoverTip;

    private static readonly Color ReadyColor = new(0.4f, 0.85f, 0.45f);
    private static readonly Color WaitingColor = new(0.95f, 0.75f, 0.3f);
    private static readonly Color ActedColor = new(0.5f, 0.52f, 0.58f);
    private static readonly Color DeployedColor = new(0.93f, 0.76f, 0.28f);
    private static readonly Color ReserveColor = new(0.4f, 0.65f, 0.95f);

    public int UnitId { get; private set; } = -1;
    public event Action<int> Clicked;

    private void Awake()
    {
        if (button != null) button.onClick.AddListener(HandleClick);
    }

    private void OnDestroy()
    {
        if (button != null) button.onClick.RemoveListener(HandleClick);
    }

    public void SetTooltipView(BattleTooltipView view)
    {
        if (hoverTip != null) hoverTip.Initialize(view);
    }

    public void Bind(BattleUnitState unit, bool selected, TacticalRosterCardState state, string tooltip)
    {
        UnitId = unit.UnitId;
        var sprite = BattleUIUtil.UnitIcon(unit);
        icon.sprite = sprite;
        icon.enabled = sprite != null;
        if (fallbackLabel != null)
        {
            fallbackLabel.gameObject.SetActive(sprite == null);
            string name = BattleUIUtil.UnitName(unit);
            fallbackLabel.text = name.Length > 0 ? name.Substring(0, Mathf.Min(2, name.Length)).ToUpperInvariant() : "?";
        }

        int max = Mathf.Max(1, unit.Snapshot != null ? unit.Snapshot.MaximumHealth : 1);
        float ratio = Mathf.Clamp01(unit.CurrentHealth / (float)max);
        healthFill.anchorMax = new Vector2(ratio, 1f);
        healthFillImage.color = ratio > 0.6f ? ReadyColor : ratio > 0.3f ? WaitingColor : new Color(0.95f, 0.4f, 0.35f);

        statusDot.color = state switch
        {
            TacticalRosterCardState.Ready => ReadyColor,
            TacticalRosterCardState.Waiting => WaitingColor,
            TacticalRosterCardState.Deployed => DeployedColor,
            TacticalRosterCardState.Reserve => ReserveColor,
            _ => ActedColor,
        };
        dimOverlay.SetActive(state == TacticalRosterCardState.Acted);
        selectionIndicator.SetActive(selected);
        if (hoverTip != null) hoverTip.SetText(tooltip);
    }

    private void HandleClick() => Clicked?.Invoke(UnitId);
}
