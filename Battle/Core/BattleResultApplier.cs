using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class BattleResultApplier
{
    private readonly IBattleCampaignPlacementService placement = new BattleCampaignPlacementService();
    public void Apply(BattleResult result, EngagementPreview preview)
    {
        if (result == null || result.CampaignApplied)
            return;
        if (preview == null)
        { Debug.LogError("[BattleResultApplier] Result cannot be applied without its campaign engagement context."); return; }

        var byId = BuildUnitLookup(preview);
        var deferredCargo = new List<BattleUnitOutcome>();

        for (int i = 0; i < result.UnitOutcomes.Count; i++)
        {
            var outcome = result.UnitOutcomes[i];
            if (!byId.TryGetValue(outcome.CampaignRuntimeId, out var unit) || unit == null)
                continue;

            // A queued reinforcement that never crossed a tactical entry edge
            // retains its campaign orders, action, XP, health, and location.
            if (!outcome.Participated)
                continue;

            unit.ClearCampaignOrdersForBattle();
            unit.MarkCampaignActionConsumedByBattle();
            unit.ApplyBattleExperience(outcome.ExperienceGained);

            if (outcome.Died)
            {
                unit.KillFromBattle();
                continue;
            }

            unit.ApplyBattleHealth(outcome.FinalHealth);

            if (outcome.IsEmbarked)
            {
                deferredCargo.Add(outcome);
                continue;
            }

            TryRepositionSurvivor(result, preview, unit, outcome);
        }

        for (int i = 0; i < deferredCargo.Count; i++)
        {
            var outcome = deferredCargo[i];
            if (!byId.TryGetValue(outcome.CampaignRuntimeId, out var cargo) || cargo == null)
                continue;
            if (byId.TryGetValue(outcome.CarrierOrTransportCampaignRuntimeId, out var carrier)
                && carrier != null
                && carrier.currentHealth > 0
                && carrier.TryRestoreBattleCargo(cargo))
                continue;

            TryRepositionSurvivor(result, preview, cargo, outcome);
        }

        foreach (var unit in byId.Values)
            if (unit != null && unit.currentHealth > 0)
            {
                CampaignArmyService.RefreshPresentation(unit);
                CivilianAttachmentService.SynchronizeFormationLocation(unit);
            }
        ApplyCivilianFormationFate(result, preview);
        ApplyBandObjectiveResult(result, preview);
        result.CampaignApplied = true;
    }

    private static void ApplyCivilianFormationFate(BattleResult result, EngagementPreview preview)
    {
        var losing = result.WinningSide == BattleSide.Attacker ? preview.DefenderParty : preview.AttackerParty;
        var winning = result.WinningSide == BattleSide.Attacker ? preview.AttackerParty : preview.DefenderParty;
        if (losing == null || losing.Kind != CampaignBattlePartyKind.Army ||
            losing.CombatUnits.Any(x => x != null && x.currentHealth > 0)) return;
        var victor = winning?.CombatUnits?.FirstOrDefault(x => x != null && x.currentHealth > 0);
        CivilianAttachmentService.ResolveFormationLoss(losing.Owner, losing.ArmyFormationId, victor);
    }

    private static void ApplyBandObjectiveResult(BattleResult result, EngagementPreview preview)
    {
        var losingParty = result.WinningSide == BattleSide.Attacker ? preview.DefenderParty : preview.AttackerParty;
        var winningParty = result.WinningSide == BattleSide.Attacker ? preview.AttackerParty : preview.DefenderParty;
        if (losingParty == null || losingParty.Kind != CampaignBattlePartyKind.BandGarrison || losingParty.BandHost == null) return;

        var band = losingParty.BandHost;
        // Legitimate survivors withdraw as their old owner's real army before the objective changes hands.
        band.ReleaseSurvivingGarrisonAsArmy();
        var victor = winningParty?.CombatUnits?.FirstOrDefault(x => x != null && x.currentHealth > 0);
        if (victor != null && victor.data != null && victor.data.unitType == CombatCategory.Animal)
            band.DestroyBand(BandLossReason.AnimalAttack);
        else if (winningParty?.Owner != null)
            band.Capture(winningParty.Owner);
    }

    private static Dictionary<int, CombatUnit> BuildUnitLookup(EngagementPreview preview)
    {
        var map = new Dictionary<int, CombatUnit>();

        AddSide(preview.AttackerUnits, map);
        AddSide(preview.DefenderUnits, map);
        for (int i = 0; i < preview.Reinforcements.Count; i++)
            AddSide(preview.Reinforcements[i].Units, map);

        return map;
    }

    private static void AddSide(List<BattleUnitSnapshot> snapshots, Dictionary<int, CombatUnit> map)
    {
        for (int i = 0; i < snapshots.Count; i++)
        {
            var s = snapshots[i];
            if (s?.SourceUnit == null)
                continue;

            map[s.CampaignRuntimeId] = s.SourceUnit;
        }
    }

    private void TryRepositionSurvivor(BattleResult result, EngagementPreview preview, CombatUnit unit, BattleUnitOutcome outcome)
    {
        if (preview.Theater == BattleTheater.DeepSpace)
        {
            int attempted=-1;
            foreach(int spaceTile in GetSpacePlacementCandidates(result,preview,unit,outcome))
            {
                attempted=spaceTile;
                if(placement.TryPlaceAfterBattle(unit,new BattleCampaignPlacementRequest{PlanetIndex=preview.PlanetIndex,
                    SpaceTileIndex=spaceTile,Layer=unit.currentLayer,PreferredStackSlot=outcome.SuggestedStackSlot},out _))
                { BattleRecoveryHoldingService.GetOrCreate().Resolve(unit); return; }
            }
            string reason = $"Living space unit {unit.GetRuntimeId()} has no legal post-battle placement; it remains at its recoverable pre-placement location.";
            result.PlacementFailures.Add(new BattlePlacementFailure { CampaignRuntimeId=outcome.CampaignRuntimeId, Side=outcome.Side,
                Reason=reason, OriginalTile=unit.currentSpaceTileIndex, RequestedTile=attempted, IsDeepSpace=true });
            Debug.LogError($"[BattleResultApplier] {reason}");
            BattleRecoveryHoldingService.GetOrCreate().Hold(unit, reason, true);
            return;
        }

        foreach (int tile in GetPlacementCandidates(result, preview, unit, outcome))
        {
            if (placement.TryPlaceAfterBattle(unit, new BattleCampaignPlacementRequest
            {
                PlanetIndex = preview.PlanetIndex,
                CampaignTileIndex = tile,
                Layer = unit.currentLayer,
                PreferredStackSlot = outcome.SuggestedStackSlot,
            }, out _))
            { BattleRecoveryHoldingService.GetOrCreate().Resolve(unit); return; }
        }
        string failure = $"Living unit {unit.GetRuntimeId()} has no legal post-battle placement; it remains at its recoverable pre-placement location.";
        result.PlacementFailures.Add(new BattlePlacementFailure { CampaignRuntimeId=outcome.CampaignRuntimeId, Side=outcome.Side,
            Reason=failure, OriginalTile=unit.currentTileIndex, RequestedTile=outcome.WithdrawalCampaignTile, IsDeepSpace=false });
        Debug.LogError($"[BattleResultApplier] {failure}");
        BattleRecoveryHoldingService.GetOrCreate().Hold(unit, failure, false);
    }

    private static IEnumerable<int> GetPlacementCandidates(BattleResult result, EngagementPreview preview, CombatUnit unit, BattleUnitOutcome outcome)
    {
        int startingTile = outcome.SuggestedCampaignTile >= 0 ? outcome.SuggestedCampaignTile : unit.currentTileIndex;
        if (outcome.Retreated && outcome.WithdrawalCampaignTile >= 0)
            yield return outcome.WithdrawalCampaignTile;
        bool winner = outcome.Side == result.WinningSide;
        int preferredTile = outcome.Retreated && outcome.WithdrawalCampaignTile >= 0
            ? outcome.WithdrawalCampaignTile
            : winner
            ? (outcome.Side == BattleSide.Attacker ? preview.AnchorTile : startingTile)
            : startingTile;

        if (!outcome.Retreated && winner && outcome.Side == BattleSide.Defender)
            preferredTile = preview.AnchorTile;

        if (preferredTile >= 0)
            yield return preferredTile;

        bool mustWithdraw = !winner || outcome.Retreated;
        if (!mustWithdraw && startingTile >= 0 && startingTile != preferredTile)
            yield return startingTile;

        var tileSystem = TileSystem.GetForPlanet(preview.PlanetIndex) ?? TileSystem.Instance;
        int retreatOrigin = startingTile >= 0 ? startingTile : preview.AnchorTile;
        if (tileSystem == null || retreatOrigin < 0)
            yield break;

        var seen=new HashSet<int>{retreatOrigin}; var queue=new Queue<(int tile,int depth)>(); queue.Enqueue((retreatOrigin,0));
        while(queue.Count>0)
        {
            var current=queue.Dequeue(); if(current.depth>=3)continue;
            int[] neighbors=tileSystem.GetNeighbors(current.tile); System.Array.Sort(neighbors);
            for(int i=0;i<neighbors.Length;i++)
            {
                int candidate=neighbors[i]; if(!seen.Add(candidate))continue;
                var tile=tileSystem.GetTileData(candidate);
                if(tile!=null&&UnitLayerRules.CanUnitUseTileOnLayer(unit,tile,unit.currentLayer)
                    && (tile.owner==null||tile.owner==unit.owner)) yield return candidate;
                queue.Enqueue((candidate,current.depth+1));
            }
        }
    }

    private static IEnumerable<int> GetSpacePlacementCandidates(BattleResult result, EngagementPreview preview, CombatUnit unit, BattleUnitOutcome outcome)
    {
        var grid=SpaceWorldManager.Instance!=null?SpaceWorldManager.Instance.Grid:SpaceCombatManager.Instance?.spaceGrid;
        if(grid==null)yield break;
        var starts=new List<int>();
        if(outcome.Retreated&&outcome.WithdrawalCampaignTile>=0)starts.Add(outcome.WithdrawalCampaignTile);
        if(unit.currentSpaceTileIndex>=0)starts.Add(unit.currentSpaceTileIndex);
        if(preview.AnchorTile>=0)starts.Add(preview.AnchorTile);
        var seen=new HashSet<int>(); var candidates=new List<int>();
        foreach(int start in starts) if(seen.Add(start))candidates.Add(start);
        for(int cursor=0;cursor<candidates.Count&&cursor<64;cursor++)
        {
            int current=candidates[cursor]; var tile=grid.GetTile(current);
            int ownerId=unit.owner!=null&&CivilizationManager.Instance!=null?CivilizationManager.Instance.GetCivIndex(unit.owner):-1;
            if(tile!=null&&!tile.blocksMovement&&(tile.controllingCivilizationId<0||tile.controllingCivilizationId==ownerId))yield return current;
            var neighbors=new List<int>(grid.GetNeighbors(current)); neighbors.Sort();
            foreach(int next in neighbors)if(seen.Add(next))candidates.Add(next);
        }
    }
}
