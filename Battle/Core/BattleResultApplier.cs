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

            TryRepositionSurvivor(preview, unit, outcome);
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

    private void TryRepositionSurvivor(EngagementPreview preview, CombatUnit unit, BattleUnitOutcome outcome)
    {
        int tile = outcome.SuggestedCampaignTile >= 0 ? outcome.SuggestedCampaignTile : unit.currentTileIndex;
        if (preview.Theater == BattleTheater.DeepSpace)
            tile = unit.currentSpaceTileIndex >= 0 ? unit.currentSpaceTileIndex : preview.AnchorTile;
        if (tile < 0)
            tile = preview.AnchorTile;

        placement.TryPlaceAfterBattle(unit, new BattleCampaignPlacementRequest
        {
            PlanetIndex = preview.PlanetIndex,
            CampaignTileIndex = tile,
            SpaceTileIndex = preview.Theater == BattleTheater.DeepSpace ? tile : -1,
            Layer = unit.currentLayer,
            PreferredStackSlot = outcome.SuggestedStackSlot,
        }, out _);
    }
}
