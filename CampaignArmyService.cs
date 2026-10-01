using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Resolves persistent campaign armies from CombatUnits that share a military formation ID.
/// CombatUnits remain the authoritative member state; one representative occupies and renders
/// on the campaign map while tactical battles expand the members into separate battle units.
/// </summary>
public static class CampaignArmyService
{
    public const int DefaultArmySize = 4;

    /// <summary>
    /// Resolves the civilization-authored campaign army prefab for the army's current age.
    /// Army membership remains CombatUnit-only; this prefab is presentation, not a unit.
    /// </summary>
    public static GameObject GetPresentationPrefab(CombatUnit unit)
    {
        return unit != null && unit.owner != null ? unit.owner.GetCampaignArmyPrefab() : null;
    }

    public static string EnsureArmyIdentity(CombatUnit unit)
    {
        if (unit == null)
            return string.Empty;

        string armyId = unit.EnsureMilitaryFormationIdentity();
        if (unit.stackSlot < 0)
            unit.stackSlot = 0;
        return armyId;
    }

    public static List<CombatUnit> GetMembers(CombatUnit unit)
    {
        var members = new List<CombatUnit>();
        if (unit == null)
            return members;

        string armyId = EnsureArmyIdentity(unit);
        var candidates = unit.owner != null ? unit.owner.combatUnits : null;
        if (candidates == null)
        {
            members.Add(unit);
            return members;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (candidate != null && candidate.MilitaryFormationId == armyId)
                members.Add(candidate);
        }

        if (!members.Contains(unit))
            members.Add(unit);

        members.Sort(CompareArmyOrder);
        return members;
    }

    public static CombatUnit GetRepresentative(CombatUnit unit)
    {
        var members = GetMembers(unit);
        return members.Count > 0 ? members[0] : unit;
    }

    public static bool IsRepresentative(CombatUnit unit)
    {
        return unit != null && GetRepresentative(unit) == unit;
    }

    public static void RefreshPresentation(CombatUnit unit)
    {
        if (unit == null)
            return;

        var members = GetMembers(unit);
        for (int i = 0; i < members.Count; i++)
        {
            var member = members[i];
            if (member == null || member.gameObject == null || member.IsTransported || member.isStored)
                continue;

            bool shouldBeVisible = i == 0;
            if (member.gameObject.activeSelf != shouldBeVisible)
                member.gameObject.SetActive(shouldBeVisible);
        }
    }

    public static bool TryMerge(CombatUnit receivingArmy, CombatUnit joiningArmy, out string reason)
    {
        reason = string.Empty;
        if (receivingArmy == null || joiningArmy == null)
        {
            reason = "missing army";
            return false;
        }
        if (receivingArmy.owner == null || receivingArmy.owner != joiningArmy.owner)
        {
            reason = "armies must have the same owner";
            return false;
        }
        if (receivingArmy.planetIndex != joiningArmy.planetIndex
            || receivingArmy.currentTileIndex != joiningArmy.currentTileIndex
            || receivingArmy.currentLayer != joiningArmy.currentLayer)
        {
            reason = "armies must occupy the same campaign location";
            return false;
        }

        var receivingMembers = GetMembers(receivingArmy);
        var joiningMembers = GetMembers(joiningArmy);
        if (receivingMembers.Count == 0 || joiningMembers.Count == 0)
        {
            reason = "army has no members";
            return false;
        }
        if (receivingMembers[0].MilitaryFormationId == joiningMembers[0].MilitaryFormationId)
            return true;

        int capacity = receivingArmy.owner.GetMaxArmySize();
        if (!CanMergeMemberCounts(receivingMembers.Count, joiningMembers.Count, capacity))
        {
            reason = $"army capacity exceeded ({receivingMembers.Count + joiningMembers.Count}/{capacity})";
            return false;
        }

        string armyId = receivingMembers[0].EnsureMilitaryFormationIdentity();
        string joiningArmyId = joiningMembers[0].MilitaryFormationId;
        string armyName = receivingMembers[0].MilitaryFormationName;
        MilitaryFormationType armyType = receivingMembers[0].MilitaryFormationType;
        for (int i = 0; i < receivingMembers.Count; i++)
            receivingMembers[i].stackSlot = i;
        for (int i = 0; i < joiningMembers.Count; i++)
        {
            joiningMembers[i].AssignMilitaryFormation(armyId, armyType, armyName);
            joiningMembers[i].stackSlot = receivingMembers.Count + i;
        }

        RefreshPresentation(receivingMembers[0]);
        CivilianAttachmentService.TransferOnMerge(receivingArmy.owner, joiningArmyId, armyId);
        return true;
    }

    public static void CreateSingletonArmy(CombatUnit unit)
    {
        if (unit == null)
            return;

        unit.AssignMilitaryFormation(Guid.NewGuid().ToString("N"), ResolveFormationType(unit));
        unit.stackSlot = 0;
        if (unit.gameObject != null && !unit.gameObject.activeSelf)
            unit.gameObject.SetActive(true);
    }

    public static bool CanMergeMemberCounts(int receivingCount, int joiningCount, int capacity)
    {
        return receivingCount > 0
            && joiningCount > 0
            && capacity > 0
            && receivingCount + joiningCount <= capacity;
    }

    public static IReadOnlyList<WorkerUnit> GetArmyWorkers(CombatUnit army) => CivilianAttachmentService.GetAttachments(army);
    public static bool HasAttachedWorker(CombatUnit army) => GetArmyWorkers(army).Count > 0;

    public static void EnterDefenseMode(CombatUnit army)
    {
        foreach (var member in GetMembers(army))
            if (member != null) member.Fortify();
    }

    public static void ExitDefenseMode(CombatUnit army)
    {
        foreach (var member in GetMembers(army))
            if (member != null) member.ClearFortify();
    }

    public static bool IsInDefenseMode(CombatUnit army)
    {
        var members = GetMembers(army);
        return members.Count > 0 && members.TrueForAll(x => x != null && x.IsFortified);
    }

    /// <summary>
    /// Applies roster selection before a movement order. This changes no movement points and
    /// never uses BaseUnit.Unstack. A null result with worker selections means worker-only movement.
    /// </summary>
    public static bool TryCreateMovingDetachment(CombatUnit sourceArmy,
        IReadOnlyList<CombatUnit> selectedCombatUnits, IReadOnlyList<WorkerUnit> selectedWorkers,
        out CombatUnit resultingRepresentative, out string reason)
    {
        resultingRepresentative = null;
        reason = string.Empty;
        var source = GetRepresentative(sourceArmy);
        if (source == null) { reason = "The source army no longer exists."; return false; }
        var members = GetMembers(source);
        var workers = CivilianAttachmentService.GetAttachments(source);
        var selectedCombat = selectedCombatUnits?.Where(x => x != null).Distinct().ToList() ?? new List<CombatUnit>();
        var selectedWorkerList = selectedWorkers?.Where(x => x != null).Distinct().ToList() ?? new List<WorkerUnit>();
        if (selectedCombat.Any(x => !members.Contains(x)) || selectedWorkerList.Any(x => !workers.Contains(x)))
        { reason = "The roster selection is stale."; return false; }

        if (selectedCombat.Count == 0)
        {
            if (selectedWorkerList.Count == 0) { resultingRepresentative = source; return true; }
            // Workers remain civilians and acquire ordinary occupancy before independent moves.
            return TryDetachWorkersAtomically(selectedWorkerList, source, out reason);
        }

        if (selectedCombat.Count == members.Count)
        {
            if (!TryDetachWorkersAtomically(workers.Where(x => !selectedWorkerList.Contains(x)).ToList(), source, out reason))
                return false;
            NormalizeSlots(members);
            resultingRepresentative = members[0];
            RefreshPresentation(resultingRepresentative);
            return true;
        }

        string newId = Guid.NewGuid().ToString("N");
        string sourceName = source.MilitaryFormationName;
        MilitaryFormationType type = source.MilitaryFormationType;
        for (int i = 0; i < selectedCombat.Count; i++)
        {
            selectedCombat[i].AssignMilitaryFormation(newId, type, sourceName);
            selectedCombat[i].stackSlot = i;
        }
        var remaining = members.Where(x => !selectedCombat.Contains(x)).ToList();
        NormalizeSlots(remaining);
        resultingRepresentative = selectedCombat[0];
        foreach (var worker in selectedWorkerList)
            worker.SetCivilianAttachment(newId);
        // The source tile remains claimed by the surviving formation. The detachment claims
        // its first destination through UnitMovementController rather than co-occupying here.
        var occupancy = TileOccupancyManager.GetForPlanet(source.planetIndex) ?? TileOccupancyManager.Instance;
        occupancy?.ClearOccupantById(source.currentTileIndex, source.currentLayer, source.gameObject.GetRuntimeId());
        occupancy?.TryAddToStack(remaining[0].currentTileIndex, remaining[0].currentLayer, remaining[0].gameObject, 1);
        RefreshPresentation(remaining[0]);
        RefreshPresentation(resultingRepresentative);
        return true;
    }

    private static void NormalizeSlots(IReadOnlyList<CombatUnit> members)
    {
        for (int i = 0; i < members.Count; i++) members[i].stackSlot = i;
    }

    private static bool TryDetachWorkersAtomically(IEnumerable<WorkerUnit> workers, CombatUnit source, out string reason)
    {
        var detached = new List<WorkerUnit>();
        foreach (var worker in workers)
        {
            if (CivilianAttachmentService.TryDetach(worker, source, out reason)) { detached.Add(worker); continue; }
            // Roll back occupancy and attachment IDs if any member cannot be legally placed.
            foreach (var rollback in detached)
                CivilianAttachmentService.TransferAttachment(rollback, source, out _);
            return false;
        }
        reason = string.Empty;
        return true;
    }

    public static void RenameArmy(CombatUnit unit, string armyName)
    {
        if (unit == null)
            return;

        string normalizedName = string.IsNullOrWhiteSpace(armyName)
            ? unit.MilitaryFormationType.ToString()
            : armyName.Trim();
        var members = GetMembers(unit);
        for (int i = 0; i < members.Count; i++)
        {
            var member = members[i];
            if (member != null)
                member.AssignMilitaryFormation(member.MilitaryFormationId,
                    member.MilitaryFormationType, normalizedName);
        }
    }

    public static bool SetRepresentative(CombatUnit unit)
    {
        if (unit == null)
            return false;

        var members = GetMembers(unit);
        if (members.Count == 0)
            return false;

        int requestedIndex = members.IndexOf(unit);
        if (requestedIndex < 0)
            return false;

        members.RemoveAt(requestedIndex);
        members.Insert(0, unit);
        for (int i = 0; i < members.Count; i++)
            members[i].stackSlot = i;

        var occupancy = TileOccupancyManager.GetForPlanet(unit.planetIndex) ?? TileOccupancyManager.Instance;
        if (occupancy != null)
        {
            for (int i = 0; i < members.Count; i++)
            {
                var member = members[i];
                occupancy.ClearOccupantById(member.currentTileIndex, member.currentLayer,
                    member.gameObject.GetRuntimeId());
            }
            if (occupancy.TryAddToStack(unit.currentTileIndex, unit.currentLayer, unit.gameObject, 1) < 0)
                return false;
        }

        RefreshPresentation(unit);
        return true;
    }

    private static int CompareArmyOrder(CombatUnit left, CombatUnit right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left == null) return 1;
        if (right == null) return -1;

        int slotComparison = left.stackSlot.CompareTo(right.stackSlot);
        if (slotComparison != 0)
            return slotComparison;

        return left.gameObject.GetRuntimeId().CompareTo(right.gameObject.GetRuntimeId());
    }

    private static MilitaryFormationType ResolveFormationType(CombatUnit unit)
    {
        if (unit?.data == null)
            return MilitaryFormationType.Army;
        if (CombatUnitData.IsSpaceCategory(unit.data.unitType)) return MilitaryFormationType.SpaceFleet;
        if (CombatUnitData.IsAirCategory(unit.data.unitType)) return MilitaryFormationType.AirWing;
        if (CombatUnitData.IsUnderwaterCategory(unit.data.unitType)) return MilitaryFormationType.UnderwaterGroup;
        if (CombatUnitData.IsNavalSurfaceCategory(unit.data.unitType)) return MilitaryFormationType.SurfaceFleet;
        return MilitaryFormationType.Army;
    }
}
