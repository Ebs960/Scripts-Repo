using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Selection model for cards in the campaign Army HUD. This deliberately does not replace
/// UnitSelectionManager: the latter still owns the one campaign-map selection.
/// </summary>
public sealed class ArmyRosterSelection : MonoBehaviour
{
    public static ArmyRosterSelection Active { get; private set; }

    private Civilization owner;
    private string formationId = string.Empty;
    private readonly HashSet<int> combatIds = new();
    private readonly HashSet<int> workerIds = new();

    public string CurrentFormationId => formationId;
    public bool HasSelection => combatIds.Count > 0 || workerIds.Count > 0;
    public IReadOnlyList<CombatUnit> SelectedCombatUnits => owner == null
        ? Array.Empty<CombatUnit>()
        : owner.combatUnits.Where(x => x != null && combatIds.Contains(x.gameObject.GetRuntimeId())).ToList();
    public IReadOnlyList<WorkerUnit> SelectedWorkers => owner == null
        ? Array.Empty<WorkerUnit>()
        : owner.workerUnits.Where(x => x != null && workerIds.Contains(x.gameObject.GetRuntimeId())).ToList();

    private void OnEnable() => Active = this;
    private void OnDisable() { if (Active == this) Active = null; }

    public void SetFormation(CombatUnit army)
    {
        string next = army != null ? CampaignArmyService.EnsureArmyIdentity(army) : string.Empty;
        if (formationId == next && owner == army?.owner) return;
        owner = army != null ? army.owner : null;
        formationId = next;
        Clear();
    }

    public void Clear() { combatIds.Clear(); workerIds.Clear(); }

    public void SelectExclusive(CombatUnit unit) { Clear(); if (IsInCurrentArmy(unit)) combatIds.Add(unit.gameObject.GetRuntimeId()); }
    public void SelectExclusive(WorkerUnit unit) { Clear(); if (IsAttachedToCurrentArmy(unit)) workerIds.Add(unit.gameObject.GetRuntimeId()); }
    public void Toggle(CombatUnit unit) { if (!IsInCurrentArmy(unit)) return; ToggleId(combatIds, unit.gameObject.GetRuntimeId()); }
    public void Toggle(WorkerUnit unit) { if (!IsAttachedToCurrentArmy(unit)) return; ToggleId(workerIds, unit.gameObject.GetRuntimeId()); }
    public bool IsSelected(CombatUnit unit) => unit != null && combatIds.Contains(unit.gameObject.GetRuntimeId());
    public bool IsSelected(WorkerUnit unit) => unit != null && workerIds.Contains(unit.gameObject.GetRuntimeId());

    private bool IsInCurrentArmy(CombatUnit unit) => unit != null && unit.owner == owner && unit.MilitaryFormationId == formationId;
    private bool IsAttachedToCurrentArmy(WorkerUnit unit) => unit != null && unit.owner == owner && unit.AttachedArmyFormationId == formationId;
    private static void ToggleId(HashSet<int> ids, int id) { if (!ids.Remove(id)) ids.Add(id); }
}
