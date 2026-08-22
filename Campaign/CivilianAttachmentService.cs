using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Civilians reference an army ID but never become military formation members.</summary>
public static class CivilianAttachmentService
{
    public static event Action<WorkerUnit, string> CivilianAttached;
    public static event Action<WorkerUnit, string> CivilianDetached;

    public static IReadOnlyList<WorkerUnit> GetAttachments(CombatUnit army)
    {
        if (army == null || army.owner == null) return Array.Empty<WorkerUnit>();
        string id = army.MilitaryFormationId;
        return army.owner.workerUnits.Where(w => w != null && w.AttachedArmyFormationId == id).ToList();
    }

    public static bool TryGetAttachedArmyRepresentative(WorkerUnit civilian, out CombatUnit representative)
    {
        representative = null;
        if (civilian == null || civilian.owner == null || string.IsNullOrEmpty(civilian.AttachedArmyFormationId)) return false;
        var member = civilian.owner.combatUnits.FirstOrDefault(x => x != null && !x.IsBandGarrisoned &&
            x.MilitaryFormationId == civilian.AttachedArmyFormationId);
        representative = member != null ? CampaignArmyService.GetRepresentative(member) : null;
        return representative != null;
    }

    public static int GetStrategicTile(WorkerUnit civilian)
    {
        return TryGetAttachedArmyRepresentative(civilian, out var army) ? army.currentTileIndex : civilian != null ? civilian.currentTileIndex : -1;
    }

    public static int GetStrategicPlanet(WorkerUnit civilian)
    {
        return TryGetAttachedArmyRepresentative(civilian, out var army) ? army.planetIndex : civilian != null ? civilian.planetIndex : -1;
    }

    /// <summary>Updates attached civilian campaign metadata after formation movement without claiming occupancy.</summary>
    public static void SynchronizeFormationLocation(CombatUnit army)
    {
        var representative = CampaignArmyService.GetRepresentative(army);
        if (representative == null) return;
        foreach (var worker in GetAttachments(representative))
        {
            worker.planetIndex = representative.planetIndex;
            worker.currentLayer = representative.currentLayer;
        }
    }

    public static bool Attach(WorkerUnit civilian, CombatUnit army, out string reason)
    {
        reason = string.Empty;
        if (civilian == null || army == null) { reason = "missing civilian or army"; return false; }
        var representative = CampaignArmyService.GetRepresentative(army);
        if (civilian.owner != representative.owner || civilian.planetIndex != representative.planetIndex ||
            civilian.currentLayer != representative.currentLayer || civilian.currentTileIndex != representative.currentTileIndex)
        { reason = "civilian and friendly army must share a campaign location"; return false; }
        var occupancy = TileOccupancyManager.GetForPlanet(civilian.planetIndex) ?? TileOccupancyManager.Instance;
        occupancy?.ClearOccupantById(civilian.currentTileIndex, civilian.currentLayer, civilian.gameObject.GetRuntimeId());
        civilian.SetCivilianAttachment(CampaignArmyService.EnsureArmyIdentity(representative));
        CivilianAttached?.Invoke(civilian, representative.MilitaryFormationId);
        return true;
    }

    public static bool Detach(WorkerUnit civilian, CombatUnit army)
    {
        if (civilian == null || army == null || civilian.AttachedArmyFormationId != army.MilitaryFormationId) return false;
        var representative = CampaignArmyService.GetRepresentative(army);
        string oldId = civilian.AttachedArmyFormationId;
        civilian.SetCivilianAttachment(null);
        civilian.planetIndex = representative.planetIndex;
        civilian.currentLayer = representative.currentLayer;
        civilian.currentTileIndex = representative.currentTileIndex;
        civilian.transform.position = representative.transform.position;
        (TileOccupancyManager.GetForPlanet(civilian.planetIndex) ?? TileOccupancyManager.Instance)?.SetOccupant(civilian.currentTileIndex, civilian.gameObject, civilian.currentLayer);
        CivilianDetached?.Invoke(civilian, oldId);
        return true;
    }

    public static void TransferOnMerge(Civilization owner, string losingFormationId, string resultingFormationId)
    {
        if (owner == null || string.IsNullOrEmpty(losingFormationId)) return;
        foreach (var worker in owner.workerUnits.Where(w => w != null && w.AttachedArmyFormationId == losingFormationId))
            worker.SetCivilianAttachment(resultingFormationId);
    }

    public static void ResolveFormationLoss(CombatUnit defeatedArmy, CombatUnit victor)
    {
        foreach (var worker in GetAttachments(defeatedArmy).ToList())
        {
            if (victor != null && victor.data != null && victor.data.unitType == CombatCategory.Animal) worker.KillCivilian();
            else if (victor != null) worker.TransferCivilianOwnership(victor.owner);
        }
    }
}
