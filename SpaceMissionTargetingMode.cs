using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Target picking for space missions. SpaceMissionManager resolves every mission against tiles of the
/// planet the spacecraft is positioned over, so targets are chosen on that planet's own map and are
/// planetary tile indices; a space-map sector index is never used here. This class only gathers a
/// target and a confirmation: validation and resolution stay in SpaceMissionManager.
/// </summary>
public class SpaceMissionTargetingMode : MonoBehaviour
{
    public static SpaceMissionTargetingMode Instance { get; private set; }

    private const float HoverInterval = 0.15f;

    private CombatUnit spacecraft;
    private SpaceMissionKind mission;
    private bool active;
    private bool confirmationPending;
    private int inputConsumedFrame = -1;
    private int hoveredTile = -1;
    private float nextHoverCheck;

    public bool IsActive => active;
    public CombatUnit Spacecraft => spacecraft;
    public SpaceMissionKind Mission => mission;
    /// <summary>Short state of the tile under the pointer, shown by the mission bar while targeting.</summary>
    public string HoverStatus { get; private set; } = string.Empty;
    /// <summary>True while targeting owns map clicks, including the frame it ends on.</summary>
    public bool BlocksMapInput => active || inputConsumedFrame == Time.frameCount;

    public static SpaceMissionTargetingMode GetOrCreate()
    {
        if (Instance == null) new GameObject(nameof(SpaceMissionTargetingMode)).AddComponent<SpaceMissionTargetingMode>();
        return Instance;
    }

    public static bool IsTargeting(CombatUnit unit, SpaceMissionKind kind) =>
        Instance != null && Instance.active && Instance.spacecraft == unit && Instance.mission == kind;

    public static string MissionName(SpaceMissionKind kind)
    {
        switch (kind)
        {
            case SpaceMissionKind.SpaceStrike: return "Space Strike";
            case SpaceMissionKind.CityBombardment: return "City Bombardment";
            default: return "Reconnaissance";
        }
    }

    /// <summary>A unit the mission manager treats as a space mission platform at all.</summary>
    public static bool IsMissionPlatform(CombatUnit unit)
    {
        return unit != null && unit.data != null &&
            (CombatUnitData.IsSpaceCategory(unit.data.unitType) || unit.IsInOrbit || unit.data.canLaunchSpaceMissions);
    }

    /// <summary>
    /// Whether a mission can be offered right now, with a short reason when it cannot. This mirrors the
    /// mission manager's requirements and adds the one thing its range check cannot express: the
    /// spacecraft needs a real tile on a loaded planet, because that is where missions are measured.
    /// </summary>
    public static bool CanOfferMission(CombatUnit unit, SpaceMissionKind kind, out string reason)
    {
        reason = null;
        if (unit == null || unit.data == null || unit.currentHealth <= 0) { reason = "Unavailable"; return false; }
        if (!IsMissionPlatform(unit) || !SpaceMissionManager.SupportsMission(unit.data, unit.IsInOrbit, kind)) { reason = "Not supported"; return false; }
        if (unit.isPackedInSpaceFleet) { reason = "Packed in a fleet"; return false; }
        if (!unit.HasAttackPoints()) { reason = "No action points"; return false; }
        if (unit.spaceLocation.IsInSolarSystemSpace || unit.planetIndex < 0 || unit.currentTileIndex < 0) { reason = "Must be at a planet"; return false; }
        var tiles = TileSystem.GetForPlanet(unit.planetIndex);
        if (tiles == null || !tiles.IsReady()) { reason = "Planet not loaded"; return false; }
        if (GameManager.Instance != null && GameManager.Instance.currentPlanetIndex != unit.planetIndex) { reason = "View its planet first"; return false; }
        if (SpaceMissionManager.Instance == null) { reason = "Mission control offline"; return false; }
        return true;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDisable()
    {
        if (Instance == this && active) Cancel();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public bool Begin(CombatUnit unit, SpaceMissionKind kind)
    {
        if (!CanOfferMission(unit, kind, out string reason))
        {
            UIManager.Instance?.ShowNotification($"{MissionName(kind)} is unavailable: {reason}.");
            return false;
        }
        if (active) Cancel();
        if (MissileLaunchMode.Instance != null && MissileLaunchMode.Instance.IsActive) MissileLaunchMode.Instance.Cancel();
        spacecraft = unit;
        mission = kind;
        active = true;
        hoveredTile = -1;
        nextHoverCheck = 0f;
        HoverStatus = "Right-click a target";
        inputConsumedFrame = Time.frameCount;
        UIManager.Instance?.ShowNotification(
            $"{MissionName(kind)}: right-click a target tile within {SpaceMissionManager.GetSpaceMissionRange(unit)} tiles of {unit.UnitName}. Press Escape to cancel.");
        return true;
    }

    public void Cancel()
    {
        if (!active) return;
        if (confirmationPending)
        {
            confirmationPending = false;
            ConfirmationDialogService.DismissActive();
        }
        Deactivate();
    }

    private void Deactivate()
    {
        active = false;
        confirmationPending = false;
        spacecraft = null;
        hoveredTile = -1;
        HoverStatus = string.Empty;
        inputConsumedFrame = Time.frameCount;
    }

    private void Update()
    {
        if (!active) return;
        if (!CanOfferMission(spacecraft, mission, out string reason))
        {
            UIManager.Instance?.ShowNotification($"{MissionName(mission)} cancelled: {reason}.");
            Cancel();
            return;
        }
        if (confirmationPending || ConfirmationDialogService.IsVisible || inputConsumedFrame == Time.frameCount) return;

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cancel();
            return;
        }
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        if (InputManager.Instance != null && !InputManager.Instance.IsInputEnabled()) return;

        var tiles = TileSystem.GetForPlanet(spacecraft.planetIndex);
        var pointer = tiles.GetMouseHitInfo();
        int tile = pointer.hit ? pointer.tileIndex : -1;
        if (tile != hoveredTile && Time.unscaledTime >= nextHoverCheck)
        {
            // The range check walks the tile graph, so it runs on hover changes only, at a limited rate.
            nextHoverCheck = Time.unscaledTime + HoverInterval;
            hoveredTile = tile;
            if (tile < 0) HoverStatus = "Right-click a target";
            else HoverStatus = SpaceMissionManager.Instance.CanLaunchMission(spacecraft, mission, tile, out string blocked)
                ? $"Tile {tile}: valid" : Shorten(blocked);
        }

        if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame) TryPick(tile);
    }

    private void TryPick(int tile)
    {
        if (tile < 0) return;
        if (!SpaceMissionManager.Instance.CanLaunchMission(spacecraft, mission, tile, out string reason))
        {
            UIManager.Instance?.ShowNotification($"Invalid target: {reason}.");
            return;
        }

        var unit = spacecraft;
        var kind = mission;
        confirmationPending = true;
        bool shown = ConfirmationDialogService.ShowConfirmation(
            $"Confirm {MissionName(kind)}",
            $"{unit.UnitName} will launch a {MissionName(kind).ToLowerInvariant()} mission at tile {tile}. The mission uses an action point and may be intercepted. Proceed?",
            () => Launch(unit, kind, tile),
            () => { confirmationPending = false; inputConsumedFrame = Time.frameCount; },
            "Launch", "Cancel", destructive: kind != SpaceMissionKind.Recon, owner: this);
        if (!shown)
        {
            confirmationPending = false;
            UIManager.Instance?.ShowNotification("Mission confirmation is unavailable.");
        }
    }

    private void Launch(CombatUnit unit, SpaceMissionKind kind, int tile)
    {
        if (!confirmationPending || !active || unit != spacecraft || kind != mission) return;
        confirmationPending = false;
        inputConsumedFrame = Time.frameCount;
        // The world can change while the dialog is open, so the manager's rules are asked again before launch.
        if (!CanOfferMission(unit, kind, out string reason) ||
            !SpaceMissionManager.Instance.CanLaunchMission(unit, kind, tile, out reason))
        {
            UIManager.Instance?.ShowNotification($"{MissionName(kind)} cancelled: {reason}.");
            Deactivate();
            return;
        }
        Deactivate();
        SpaceMissionResult result = SpaceMissionManager.Instance.LaunchMission(unit, kind, tile);
        UIManager.Instance?.ShowNotification(DescribeResult(kind, result));
    }

    private static string DescribeResult(SpaceMissionKind kind, SpaceMissionResult result)
    {
        switch (result)
        {
            case SpaceMissionResult.Completed: return $"{MissionName(kind)} completed.";
            case SpaceMissionResult.Intercepted: return $"{MissionName(kind)} was intercepted before reaching its target.";
            case SpaceMissionResult.Aborted: return $"{MissionName(kind)} was aborted.";
            case SpaceMissionResult.Launched: return $"{MissionName(kind)} launched.";
            default: return $"{MissionName(kind)} could not be launched.";
        }
    }

    private static string Shorten(string reason)
    {
        if (string.IsNullOrEmpty(reason)) return "Invalid target";
        return reason.StartsWith("target is out of", StringComparison.Ordinal) ? "Out of range" : char.ToUpperInvariant(reason[0]) + reason.Substring(1);
    }
}
