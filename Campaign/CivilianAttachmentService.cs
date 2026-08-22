using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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
        if (representative == null) return false;
        string oldId = civilian.AttachedArmyFormationId;
        if (!TryPlaceDetachedCivilian(civilian, representative, out _)) return false;
        CivilianDetached?.Invoke(civilian, oldId);
        return true;
    }

    public static bool TryDetachForHostileContact(WorkerUnit civilian, out string reason)
    {
        reason = string.Empty;
        if (civilian == null) { reason = "Missing civilian."; return false; }
        if (!civilian.IsArmyAttachedCivilian) return true;
        if (!TryGetAttachedArmyRepresentative(civilian, out var representative))
        {
            civilian.SetCivilianAttachment(null);
            reason = "Protecting formation no longer exists.";
            return true;
        }
        return TryPlaceDetachedCivilian(civilian, representative, out reason);
    }

    private static bool TryPlaceDetachedCivilian(WorkerUnit civilian, CombatUnit representative, out string reason)
    {
        reason = string.Empty;
        var occupancy = TileOccupancyManager.GetForPlanet(representative.planetIndex) ?? TileOccupancyManager.Instance;
        var tiles = TileSystem.GetForPlanet(representative.planetIndex) ?? TileSystem.Instance;
        if (occupancy == null || tiles == null) { reason = "Campaign placement is unavailable."; return false; }

        var candidates = new List<int> { representative.currentTileIndex };
        candidates.AddRange(tiles.GetNeighbors(representative.currentTileIndex));
        foreach (int tile in candidates.Distinct())
        {
            var tileData = tiles.GetTileData(tile);
            if (tile < 0 || tileData == null || !tileData.isPassable) continue;
            int slot = occupancy.TryAddToStack(tile, representative.currentLayer, civilian.gameObject,
                TileOccupancyManager.MAX_STACK_SLOTS);
            if (slot < 0) continue;
            civilian.SetCivilianAttachment(null);
            civilian.planetIndex = representative.planetIndex;
            civilian.currentLayer = representative.currentLayer;
            civilian.currentTileIndex = tile;
            civilian.stackSlot = slot;
            civilian.transform.position = tiles.GetTileCenterFlat(tile);
            return true;
        }
        reason = "No legal tile is available to detach the civilian.";
        return false;
    }

    public static void TransferOnMerge(Civilization owner, string losingFormationId, string resultingFormationId)
    {
        if (owner == null || string.IsNullOrEmpty(losingFormationId)) return;
        foreach (var worker in owner.workerUnits.Where(w => w != null && w.AttachedArmyFormationId == losingFormationId))
            worker.SetCivilianAttachment(resultingFormationId);
    }

    public static void ResolveFormationLoss(CombatUnit defeatedArmy, CombatUnit victor)
    {
        ResolveFormationLoss(defeatedArmy != null ? defeatedArmy.owner : null,
            defeatedArmy != null ? defeatedArmy.MilitaryFormationId : string.Empty, victor);
    }

    public static void ResolveFormationLoss(Civilization defeatedOwner, string formationId, CombatUnit victor)
    {
        if (defeatedOwner == null || string.IsNullOrEmpty(formationId)) return;
        foreach (var worker in defeatedOwner.workerUnits.Where(x => x != null && x.AttachedArmyFormationId == formationId).ToList())
        {
            if (victor != null && victor.data != null && victor.data.unitType == CombatCategory.Animal) worker.KillCivilian();
            else if (victor != null)
            {
                if (TryPlaceDetachedCivilian(worker, CampaignArmyService.GetRepresentative(victor), out _))
                    worker.TransferCivilianOwnership(victor.owner);
                else
                    Debug.LogWarning("[CivilianAttachment] Captured civilian has no legal campaign placement; ownership was not changed.");
            }
            else worker.SetCivilianAttachment(null);
        }
    }
}
