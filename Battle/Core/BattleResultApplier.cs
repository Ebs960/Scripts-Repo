using System.Collections.Generic;
using UnityEngine;

public sealed class BattleResultApplier
{
    private readonly IBattleCampaignPlacementService placement = new BattleCampaignPlacementService();
    public void Apply(BattleResult result, EngagementPreview preview)
    {
        if (result == null)
            return;

        var byId = BuildUnitLookup(preview);
        var deferredCargo = new List<BattleUnitOutcome>();

        for (int i = 0; i < result.UnitOutcomes.Count; i++)
        {
            var outcome = result.UnitOutcomes[i];
            if (!byId.TryGetValue(outcome.CampaignRuntimeId, out var unit) || unit == null)
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
            int spaceTile = unit.currentSpaceTileIndex >= 0 ? unit.currentSpaceTileIndex : preview.AnchorTile;
            placement.TryPlaceAfterBattle(unit, new BattleCampaignPlacementRequest
            {
                PlanetIndex = preview.PlanetIndex,
                SpaceTileIndex = spaceTile,
                Layer = unit.currentLayer,
                PreferredStackSlot = outcome.SuggestedStackSlot,
            }, out _);
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
                return;
        }
    }

    private static IEnumerable<int> GetPlacementCandidates(BattleResult result, EngagementPreview preview, CombatUnit unit, BattleUnitOutcome outcome)
    {
        int startingTile = outcome.SuggestedCampaignTile >= 0 ? outcome.SuggestedCampaignTile : unit.currentTileIndex;
        bool winner = outcome.Side == result.WinningSide;
        int preferredTile = winner
            ? (outcome.Side == BattleSide.Attacker ? preview.AnchorTile : startingTile)
            : startingTile;

        if (winner && outcome.Side == BattleSide.Defender)
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

        int[] neighbors = tileSystem.GetNeighbors(retreatOrigin);
        System.Array.Sort(neighbors);
        for (int i = 0; i < neighbors.Length; i++)
        {
            int candidate = neighbors[i];
            if (candidate == preview.AnchorTile || candidate == preferredTile)
                continue;

            var tile = tileSystem.GetTileData(candidate);
            if (UnitLayerRules.CanUnitUseTileOnLayer(unit, tile, unit.currentLayer))
                yield return candidate;
        }
    }
}
