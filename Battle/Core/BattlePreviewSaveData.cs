using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class BattlePreviewSaveData
{
    public bool isValid, allowsManual, allowsRetreat, allowsCancel;
    public string rejection;
    public int planet, anchor, attackerRuntimeId, defenderRuntimeId, mode, theater, environment, spaceRegion, seed;
    public float approachX, approachY;
    public BattleObjective objective;
    public List<BattleUnitReferenceSaveData> attackerUnits = new(), defenderUnits = new();
    public List<BattleMapCellSaveData> cells = new();
    public List<BattleReinforcementSaveData> reinforcements = new();
}

[Serializable]
public sealed class BattleUnitReferenceSaveData
{
    public int runtimeId, planet, tile, spaceTile, layer, stackSlot;
    public string formationId;
}

[Serializable]
public sealed class BattleMapCellSaveData
{
    public int index, campaignTile, elevation, waterDepth, deploymentOwner = -1, retreatSide = -1;
    public int[] neighbors;
    public List<int> cliffs = new();
    public bool passable, water, land, naval, underwater, air, orbit, space, port, beach, forest, river, hardCover, softCover, objective, reinforcementEntry;
}

[Serializable]
public sealed class BattleReinforcementSaveData
{
    public int id, side, theater, originTile, entryCell, availableRound, originSpace, domain, entryMethod, distance, lastAttempt;
    public string formation, eligibilityReason, delayReason, entryDelayReason;
    public bool eligible;
    public List<int> entries = new();
    public List<BattleUnitReferenceSaveData> units = new();
}

public static class BattlePreviewSaveCodec
{
    public static BattlePreviewSaveData Capture(EngagementPreview preview)
    {
        if (preview == null) return null;
        var save = new BattlePreviewSaveData { isValid=preview.IsValid, rejection=preview.RejectionReason, planet=preview.PlanetIndex,
            anchor=preview.AnchorTile, attackerRuntimeId=RuntimeId(preview.Attacker), defenderRuntimeId=RuntimeId(preview.Defender),
            mode=(int)preview.Mode, theater=(int)preview.Theater, environment=(int)preview.PlanetaryEnvironment,
            spaceRegion=preview.SpaceRegionId, allowsManual=preview.AllowsManualBattle, allowsRetreat=preview.AllowsRetreat,
            allowsCancel=preview.AllowsCancel, seed=preview.RandomSeed, objective=preview.Objective,
            approachX=preview.ApproachDirectionXZ.x, approachY=preview.ApproachDirectionXZ.y };
        AddSnapshotIds(preview.AttackerUnits, save.attackerUnits); AddSnapshotIds(preview.DefenderUnits, save.defenderUnits);
        if (preview.Map != null) foreach (var cell in preview.Map.Cells)
        {
            var c = new BattleMapCellSaveData { index=cell.BattleIndex, campaignTile=cell.CampaignTileIndex, neighbors=cell.NeighborIndices,
                elevation=cell.ElevationLevel, waterDepth=cell.WaterDepthLevel, deploymentOwner=cell.DeploymentOwner.HasValue?(int)cell.DeploymentOwner.Value:-1,
                retreatSide=cell.RetreatExitForSide.HasValue?(int)cell.RetreatExitForSide.Value:-1, passable=cell.IsPassable, water=cell.IsWater,
                land=cell.SupportsLand, naval=cell.SupportsNavalSurface, underwater=cell.SupportsUnderwater, air=cell.SupportsAir,
                orbit=cell.SupportsOrbit, space=cell.SupportsSpace, port=cell.HasPort, beach=cell.HasBeach, forest=cell.IsForest,
                river=cell.HasRiver, hardCover=cell.HasHardCover, softCover=cell.HasSoftCover, objective=cell.IsObjective,
                reinforcementEntry=cell.IsReinforcementEntry };
            foreach (int cliff in cell.CliffNeighbors) c.cliffs.Add(cliff);
            save.cells.Add(c);
        }
        foreach (var group in preview.Reinforcements)
        {
            var g = new BattleReinforcementSaveData { id=group.ReinforcementGroupId, formation=group.FormationId, side=(int)group.Side,
                theater=(int)group.Theater, originTile=group.OriginCampaignTile, entryCell=group.EntryCellIndex,
                availableRound=group.AvailableFromRound, originSpace=group.OriginSpaceRegion, domain=(int)group.Domain,
                entryMethod=(int)group.EntryMethod, eligible=group.IsEligible, distance=group.StrategicDistance,
                eligibilityReason=group.EligibilityReason, delayReason=group.DelayReason, lastAttempt=group.LastEntryAttemptRound,
                entryDelayReason=group.LastEntryDelayReason };
            g.entries.AddRange(group.EntryCellIndices); AddSnapshotIds(group.Units, g.units); save.reinforcements.Add(g);
        }
        return save;
    }

    public static EngagementPreview Restore(BattlePreviewSaveData save)
    {
        if (save == null) return null;
        var preview = new EngagementPreview { IsValid=save.isValid, RejectionReason=save.rejection, PlanetIndex=save.planet,
            AnchorTile=save.anchor, Attacker=FindUnit(save.attackerRuntimeId), Defender=FindUnit(save.defenderRuntimeId),
            Mode=(EngagementMode)save.mode, Theater=(BattleTheater)save.theater, PlanetaryEnvironment=(PlanetaryBattleEnvironment)save.environment,
            SpaceRegionId=save.spaceRegion, AllowsManualBattle=save.allowsManual, AllowsRetreat=save.allowsRetreat,
            AllowsCancel=save.allowsCancel, RandomSeed=save.seed, Objective=save.objective,
            ApproachDirectionXZ=new Vector2(save.approachX, save.approachY), Map=new BattleMap() };
        RestoreSnapshots(save.attackerUnits, preview.AttackerUnits); RestoreSnapshots(save.defenderUnits, preview.DefenderUnits);
        if (preview.Attacker==null && preview.AttackerUnits.Count>0) preview.Attacker=preview.AttackerUnits[0].SourceUnit;
        if (preview.Defender==null && preview.DefenderUnits.Count>0) preview.Defender=preview.DefenderUnits[0].SourceUnit;
        if (preview.Attacker == null || preview.Defender == null || preview.AttackerUnits.Count == 0 || preview.DefenderUnits.Count == 0)
            throw new InvalidOperationException("Saved battle participants cannot be rebound to campaign units.");
        if (save.cells == null || save.cells.Count == 0) throw new InvalidOperationException("Saved tactical map is empty.");
        save.cells.Sort((a,b)=>a.index.CompareTo(b.index));
        foreach (var c in save.cells)
        {
            var cell = new BattleCell { BattleIndex=c.index, CampaignTileIndex=c.campaignTile, NeighborIndices=c.neighbors,
                ElevationLevel=c.elevation, WaterDepthLevel=c.waterDepth, IsPassable=c.passable, IsWater=c.water,
                SupportsLand=c.land, SupportsNavalSurface=c.naval, SupportsUnderwater=c.underwater, SupportsAir=c.air,
                SupportsOrbit=c.orbit, SupportsSpace=c.space, HasPort=c.port, HasBeach=c.beach, IsForest=c.forest,
                HasRiver=c.river, HasHardCover=c.hardCover, HasSoftCover=c.softCover, IsObjective=c.objective,
                IsReinforcementEntry=c.reinforcementEntry,
                DeploymentOwner=c.deploymentOwner>=0?(BattleSide?)((BattleSide)c.deploymentOwner):null,
                RetreatExitForSide=c.retreatSide>=0?(BattleSide?)((BattleSide)c.retreatSide):null };
            if (c.cliffs != null) foreach (int cliff in c.cliffs) cell.SetCliffTowardNeighbor(cliff, true);
            preview.Map.AddCell(cell);
        }
        if (save.reinforcements != null) foreach (var g in save.reinforcements)
        {
            var group = new BattleReinforcementGroup { ReinforcementGroupId=g.id, FormationId=g.formation, Side=(BattleSide)g.side,
                Theater=(BattleTheater)g.theater, OriginCampaignTile=g.originTile, EntryCellIndex=g.entryCell,
                AvailableFromRound=g.availableRound, OriginSpaceRegion=g.originSpace, Domain=(BattleDomain)g.domain,
                EntryMethod=(BattleEntryMethod)g.entryMethod, IsEligible=g.eligible, StrategicDistance=g.distance,
                EligibilityReason=g.eligibilityReason, DelayReason=g.delayReason, LastEntryAttemptRound=g.lastAttempt,
                LastEntryDelayReason=g.entryDelayReason };
            if (g.entries != null) group.EntryCellIndices.AddRange(g.entries); RestoreSnapshots(g.units, group.Units);
            preview.Reinforcements.Add(group);
        }
        return preview;
    }

    private static int RuntimeId(CombatUnit unit) => unit != null && unit.gameObject != null ? unit.gameObject.GetRuntimeId() : 0;
    private static void AddSnapshotIds(IReadOnlyList<BattleUnitSnapshot> units, List<BattleUnitReferenceSaveData> target)
    {
        if (units == null) return;
        for (int i=0;i<units.Count;i++)
        {
            var snapshot=units[i]; var unit=snapshot?.SourceUnit;
            target.Add(new BattleUnitReferenceSaveData { runtimeId=snapshot?.CampaignRuntimeId??0, formationId=snapshot?.FormationId,
                planet=unit?.planetIndex??-1, tile=unit?.currentTileIndex??-1, spaceTile=unit?.currentSpaceTileIndex??-1,
                layer=(int)(unit?.currentLayer??TileLayer.Surface), stackSlot=unit?.stackSlot??-1 });
        }
    }
    private static void RestoreSnapshots(List<BattleUnitReferenceSaveData> ids, List<BattleUnitSnapshot> target)
    {
        if (ids == null) return;
        foreach (var id in ids)
        {
            var unit=FindUnit(id); if (unit == null) throw new InvalidOperationException($"Campaign combat unit {id?.runtimeId??0} is missing.");
            var profile=BattleProfileInference.Resolve(unit.data);
            target.Add(new BattleUnitSnapshot(unit, profile, profile!=null?Mathf.Max(1,profile.tacticalMovePoints):3, profile!=null?Mathf.Max(1,profile.tacticalActionPoints):1));
        }
    }
    private static CombatUnit FindUnit(BattleUnitReferenceSaveData reference)
    {
        if (reference==null) return null;
        var direct=FindUnit(reference.runtimeId); if (direct!=null) return direct;
        CombatUnit match=null;
        foreach (var unit in UnitRegistry.GetCombatUnits())
        {
            if (unit==null || unit.MilitaryFormationId!=reference.formationId || unit.planetIndex!=reference.planet
                || unit.currentTileIndex!=reference.tile || unit.currentSpaceTileIndex!=reference.spaceTile
                || (int)unit.currentLayer!=reference.layer || unit.stackSlot!=reference.stackSlot) continue;
            if (match!=null) throw new InvalidOperationException($"Stable battle identity for formation {reference.formationId} is ambiguous.");
            match=unit;
        }
        return match;
    }
    private static CombatUnit FindUnit(int id)
    {
        var direct=UnitRegistry.GetObject(id); if (direct != null && direct.TryGetComponent<CombatUnit>(out var found)) return found;
        foreach (var unit in UnitRegistry.GetCombatUnits()) if (RuntimeId(unit)==id) return unit;
        return null;
    }
}
