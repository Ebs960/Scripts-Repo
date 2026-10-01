using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Validates and atomically applies a proposed reorganization of two friendly armies.</summary>
public static class ArmyTransferService
{
    public sealed class TransferPlan
    {
        public CombatUnit LeftArmy;
        public CombatUnit RightArmy;
        public readonly List<CombatUnit> LeftCombat = new();
        public readonly List<CombatUnit> RightCombat = new();
        public readonly List<WorkerUnit> LeftWorkers = new();
        public readonly List<WorkerUnit> RightWorkers = new();

        public static TransferPlan Capture(CombatUnit left, CombatUnit right)
        {
            var plan = new TransferPlan { LeftArmy = CampaignArmyService.GetRepresentative(left), RightArmy = CampaignArmyService.GetRepresentative(right) };
            plan.LeftCombat.AddRange(CampaignArmyService.GetMembers(left));
            plan.RightCombat.AddRange(CampaignArmyService.GetMembers(right));
            plan.LeftWorkers.AddRange(CivilianAttachmentService.GetAttachments(left));
            plan.RightWorkers.AddRange(CivilianAttachmentService.GetAttachments(right));
            return plan;
        }
    }

    public static event Action<CombatUnit, CombatUnit> TransferRequested;
    public static void RequestTransfer(CombatUnit left, CombatUnit right)
    {
        if (CanTransfer(left, right, out _)) TransferRequested?.Invoke(CampaignArmyService.GetRepresentative(left), CampaignArmyService.GetRepresentative(right));
    }

    public static bool CanTransfer(CombatUnit left, CombatUnit right, out string reason)
    {
        reason = string.Empty;
        left = CampaignArmyService.GetRepresentative(left); right = CampaignArmyService.GetRepresentative(right);
        if (left == null || right == null) { reason = "Both armies are required."; return false; }
        if (left.MilitaryFormationId == right.MilitaryFormationId) { reason = "Select two different armies."; return false; }
        if (left.owner == null || left.owner != right.owner) { reason = "Armies must belong to the same civilization."; return false; }
        if (left.planetIndex != right.planetIndex || left.currentLayer != right.currentLayer) { reason = "Armies must be on the same planet and layer."; return false; }
        var tiles = TileSystem.GetForPlanet(left.planetIndex) ?? TileSystem.Instance;
        if (tiles == null || tiles.GetWrappedHexDistance(left.currentTileIndex, right.currentTileIndex) > 1)
        { reason = "Armies must rendezvous on adjacent tiles."; return false; }
        return true;
    }

    public static bool ValidateTransferPlan(TransferPlan plan, out string reason)
    {
        reason = string.Empty;
        if (plan == null || !CanTransfer(plan.LeftArmy, plan.RightArmy, out reason)) return false;
        var originalCombat = CampaignArmyService.GetMembers(plan.LeftArmy).Concat(CampaignArmyService.GetMembers(plan.RightArmy)).ToList();
        var proposedCombat = plan.LeftCombat.Concat(plan.RightCombat).ToList();
        var originalWorkers = CivilianAttachmentService.GetAttachments(plan.LeftArmy).Concat(CivilianAttachmentService.GetAttachments(plan.RightArmy)).ToList();
        var proposedWorkers = plan.LeftWorkers.Concat(plan.RightWorkers).ToList();
        if (proposedCombat.Any(x => x == null || x.owner != plan.LeftArmy.owner || x.isStored || x.currentHealth <= 0)
            || proposedCombat.Count != proposedCombat.Distinct().Count() || !SameSet(originalCombat, proposedCombat))
        { reason = "The combat roster contains a missing, duplicate, stored, dead, or unrelated unit."; return false; }
        if (proposedWorkers.Any(x => x == null || x.owner != plan.LeftArmy.owner || x.isStored || x.currentHealth <= 0)
            || proposedWorkers.Count != proposedWorkers.Distinct().Count() || !SameSet(originalWorkers, proposedWorkers))
        { reason = "The worker roster contains a missing, duplicate, stored, dead, or unrelated unit."; return false; }
        if (plan.LeftCombat.Count == 0 && plan.RightCombat.Count == 0) { reason = "At least one army must survive."; return false; }
        int capacity = plan.LeftArmy.owner.GetMaxArmySize();
        if (plan.LeftCombat.Count > capacity || plan.RightCombat.Count > capacity)
        { reason = $"Combat-unit capacity exceeded (maximum {capacity})."; return false; }
        return true;
    }

    public static bool ApplyTransferPlan(TransferPlan plan, out string reason)
    {
        if (!ValidateTransferPlan(plan, out reason)) return false;
        string leftId = plan.LeftArmy.MilitaryFormationId, rightId = plan.RightArmy.MilitaryFormationId;
        int leftPlanet = plan.LeftArmy.planetIndex, rightPlanet = plan.RightArmy.planetIndex;
        int leftTile = plan.LeftArmy.currentTileIndex, rightTile = plan.RightArmy.currentTileIndex;
        TileLayer leftLayer = plan.LeftArmy.currentLayer, rightLayer = plan.RightArmy.currentLayer;
        MilitaryFormationType leftType = plan.LeftArmy.MilitaryFormationType, rightType = plan.RightArmy.MilitaryFormationType;
        string leftName = plan.LeftArmy.MilitaryFormationName, rightName = plan.RightArmy.MilitaryFormationName;
        // A zero-combat side is dissolved. Its remaining workers follow the surviving army so
        // attachments can never reference a nonexistent military formation.
        if (plan.LeftCombat.Count == 0) { plan.RightWorkers.AddRange(plan.LeftWorkers.Where(x => !plan.RightWorkers.Contains(x))); plan.LeftWorkers.Clear(); }
        if (plan.RightCombat.Count == 0) { plan.LeftWorkers.AddRange(plan.RightWorkers.Where(x => !plan.LeftWorkers.Contains(x))); plan.RightWorkers.Clear(); }
        var occupancy = TileOccupancyManager.GetForPlanet(plan.LeftArmy.planetIndex) ?? TileOccupancyManager.Instance;
        occupancy?.ClearOccupantById(plan.LeftArmy.currentTileIndex, plan.LeftArmy.currentLayer, plan.LeftArmy.gameObject.GetRuntimeId());
        occupancy?.ClearOccupantById(plan.RightArmy.currentTileIndex, plan.RightArmy.currentLayer, plan.RightArmy.gameObject.GetRuntimeId());
        AssignSide(plan.LeftCombat, plan.LeftWorkers, leftId, leftType, leftName, leftPlanet, leftTile, leftLayer);
        AssignSide(plan.RightCombat, plan.RightWorkers, rightId, rightType, rightName, rightPlanet, rightTile, rightLayer);
        if (plan.LeftCombat.Count > 0) occupancy?.TryAddToStack(leftTile, leftLayer, plan.LeftCombat[0].gameObject, 1);
        if (plan.RightCombat.Count > 0) occupancy?.TryAddToStack(rightTile, rightLayer, plan.RightCombat[0].gameObject, 1);
        if (plan.LeftCombat.Count > 0) CampaignArmyService.RefreshPresentation(plan.LeftCombat[0]);
        if (plan.RightCombat.Count > 0) CampaignArmyService.RefreshPresentation(plan.RightCombat[0]);
        return true;
    }

    private static void AssignSide(List<CombatUnit> combat, List<WorkerUnit> workers, string id,
        MilitaryFormationType type, string name, int planet, int tile, TileLayer layer)
    {
        var tiles = TileSystem.GetForPlanet(planet) ?? TileSystem.Instance;
        for (int i = 0; i < combat.Count; i++)
        {
            combat[i].AssignMilitaryFormation(id, type, name);
            combat[i].stackSlot = i;
            combat[i].planetIndex = planet;
            combat[i].currentLayer = layer;
            combat[i].currentTileIndex = tile;
            if (tiles != null) combat[i].transform.position = tiles.GetTileSurfacePosition(tile);
        }
        foreach (var worker in workers)
        {
            worker.SetCivilianAttachment(id);
            worker.planetIndex = planet;
            worker.currentLayer = layer;
        }
    }
    private static bool SameSet<T>(IEnumerable<T> a, IEnumerable<T> b) => new HashSet<T>(a).SetEquals(b);
}
