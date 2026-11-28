using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The three-command space mission strip. It shows what the bound spacecraft can actually do under
/// SpaceMissionManager's rules, explains any command that is unavailable, and reports the chosen
/// mission to its host. It never launches a mission itself.
/// </summary>
public class SpaceMissionBarView : MonoBehaviour
{
    [Serializable]
    public class Slot
    {
        public SpaceMissionKind kind;
        public Button button;
        public TextMeshProUGUI label;
        public TextMeshProUGUI detail;
    }

    [SerializeField] private Slot[] slots = Array.Empty<Slot>();

    private const float RefreshInterval = 0.25f;
    private static readonly Color readyColor = new Color(0.88f, 0.76f, 0.48f, 1f);
    private static readonly Color unavailableColor = new Color(0.62f, 0.66f, 0.74f, 1f);

    private CombatUnit spacecraft;
    private float nextRefresh;
    private bool wired;

    /// <summary>Raised when the player picks an available mission for the bound spacecraft.</summary>
    public event Action<CombatUnit, SpaceMissionKind> MissionRequested;

    /// <summary>
    /// Shows the strip for a spacecraft the player may command, or hides it. Enemy ships, inspection-only
    /// selections, and units that are not mission platforms should be passed as null.
    /// </summary>
    public void Bind(CombatUnit unit)
    {
        Wire();
        bool show = unit != null && unit.currentHealth > 0 && SpaceMissionTargetingMode.IsMissionPlatform(unit);
        spacecraft = show ? unit : null;
        if (gameObject.activeSelf != show) gameObject.SetActive(show);
        if (show) Refresh();
    }

    private void Update()
    {
        if (spacecraft == null || Time.unscaledTime < nextRefresh) return;
        Refresh();
    }

    private void Wire()
    {
        if (wired) return;
        wired = true;
        foreach (var slot in slots)
        {
            if (slot == null || slot.button == null) continue;
            var captured = slot;
            slot.button.onClick.AddListener(() => OnSlotClicked(captured));
        }
    }

    private void Refresh()
    {
        nextRefresh = Time.unscaledTime + RefreshInterval;
        if (spacecraft == null || spacecraft.currentHealth <= 0) { Bind(null); return; }
        var targeting = SpaceMissionTargetingMode.Instance;
        bool targetingThisShip = targeting != null && targeting.IsActive && targeting.Spacecraft == spacecraft;
        foreach (var slot in slots)
        {
            if (slot == null) continue;
            string missionName = SpaceMissionTargetingMode.MissionName(slot.kind);
            bool isTargeting = targetingThisShip && targeting.Mission == slot.kind;
            bool available = SpaceMissionTargetingMode.CanOfferMission(spacecraft, slot.kind, out string reason);
            // While one mission is being targeted, its own slot becomes the cancel control and the others wait.
            bool interactable = isTargeting || (available && !targetingThisShip);
            if (slot.button != null) slot.button.interactable = interactable;
            if (slot.label != null)
            {
                slot.label.text = isTargeting ? $"Cancel {missionName}" : missionName;
                slot.label.color = interactable ? readyColor : unavailableColor;
            }
            if (slot.detail != null)
                slot.detail.text = isTargeting ? targeting.HoverStatus : available ? (targetingThisShip ? "Targeting in progress" : "Ready") : reason;
        }
    }

    private void OnSlotClicked(Slot slot)
    {
        if (spacecraft == null) return;
        if (SpaceMissionTargetingMode.IsTargeting(spacecraft, slot.kind))
        {
            SpaceMissionTargetingMode.Instance.Cancel();
            Refresh();
            return;
        }
        if (!SpaceMissionTargetingMode.CanOfferMission(spacecraft, slot.kind, out _)) { Refresh(); return; }
        MissionRequested?.Invoke(spacecraft, slot.kind);
        Refresh();
    }
}
