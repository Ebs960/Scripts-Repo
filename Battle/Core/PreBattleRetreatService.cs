using System;
using System.Collections.Generic;

public sealed class PreBattleRetreatService
{
    private readonly IBattleCampaignPlacementService placement = new BattleCampaignPlacementService();

    public bool TryRetreat(EngagementPreview preview, out string reason)
    {
        reason = string.Empty;
        if (preview == null || preview.AttackerUnits.Count == 0 || preview.Theater == BattleTheater.DeepSpace)
        {
            reason = "pre-battle retreat is unavailable";
            return false;
        }

        var tileSystem = TileSystem.GetForPlanet(preview.PlanetIndex) ?? TileSystem.Instance;
        var occupancy = TileOccupancyManager.GetForPlanet(preview.PlanetIndex) ?? TileOccupancyManager.Instance;
        if (tileSystem == null || occupancy == null || preview.Attacker == null)
        {
            reason = "campaign map is unavailable";
            return false;
        }

        int[] candidates = tileSystem.GetNeighbors(preview.Attacker.currentTileIndex);
        Array.Sort(candidates);
        foreach (int candidate in candidates)
        {
            if (candidate == preview.AnchorTile)
                continue;

            if (CanPlaceAll(preview.AttackerUnits, candidate, tileSystem, occupancy))
            {
                for (int i = 0; i < preview.AttackerUnits.Count; i++)
                {
                    var unit = preview.AttackerUnits[i].SourceUnit;
                    if (!placement.TryPlaceAfterBattle(unit, new BattleCampaignPlacementRequest
                    {
                        PlanetIndex = preview.PlanetIndex,
                        CampaignTileIndex = candidate,
                        Layer = unit.currentLayer,
                    }, out _))
                    {
                        reason = "retreat placement failed";
                        return false;
                    }
                    unit.ClearCampaignOrdersForBattle();
                    unit.MarkCampaignActionConsumedByBattle();
                }
                return true;
            }
        }

        reason = "no legal retreat route";
        return false;
    }

    private static bool CanPlaceAll(IReadOnlyList<BattleUnitSnapshot> units, int tileIndex, TileSystem tileSystem, TileOccupancyManager occupancy)
    {
        var planned = new Dictionary<TileLayer, int>();
        for (int i = 0; i < units.Count; i++)
        {
            var unit = units[i]?.SourceUnit;
            var tile = tileSystem.GetTileData(tileIndex);
            if (unit == null || unit.IsTransported || !UnitLayerRules.CanUnitUseTileOnLayer(unit, tile, unit.currentLayer))
                return false;

            int maxStack = unit.owner != null ? unit.owner.GetMaxStackSize() : 1;
            planned.TryGetValue(unit.currentLayer, out int count);
            if (occupancy.GetOccupantCount(tileIndex, unit.currentLayer) + count >= maxStack)
                return false;
            planned[unit.currentLayer] = count + 1;
        }
        return true;
    }
}