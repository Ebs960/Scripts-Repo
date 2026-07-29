using System.Collections.Generic;
using UnityEngine;

public sealed class BattleResultApplier
{
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
            unit.ApplyBattleHealth(outcome.FinalHealth);
            unit.ApplyBattleExperience(outcome.ExperienceGained);

            if (outcome.Died)
            {
                // Route through existing damage/death flow for cleanup and events.
                if (unit.currentHealth > 0)
                    unit.ApplyDamage(unit.currentHealth + 1);
                continue;
            }

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

    private static void TryRepositionSurvivor(EngagementPreview preview, CombatUnit unit, BattleUnitOutcome outcome)
    {
        var ts = TileSystem.GetForPlanet(preview.PlanetIndex) ?? TileSystem.Instance;
        if (ts == null)
            return;

        int tile = outcome.SuggestedCampaignTile >= 0 ? outcome.SuggestedCampaignTile : unit.currentTileIndex;
        if (tile < 0)
            tile = preview.AnchorTile;

        var td = ts.GetTileData(tile);
        if (td == null || !td.isPassable)
            return;

        unit.currentTileIndex = tile;
        unit.transform.position = ts.GetTileSurfacePosition(tile);
    }
}
