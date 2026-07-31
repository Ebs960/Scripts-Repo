public sealed class BattleCampaignPlacementRequest
{
    public int PlanetIndex;
    public int CampaignTileIndex;
    public int SpaceTileIndex = -1;
    public TileLayer Layer;
    public int PreferredStackSlot = -1;
}

public sealed class BattleCampaignPlacementResult
{
    public int CampaignTileIndex = -1;
    public int SpaceTileIndex = -1;
    public TileLayer Layer;
}

public interface IBattleCampaignPlacementService
{
    bool TryPlaceAfterBattle(CombatUnit unit, BattleCampaignPlacementRequest request, out BattleCampaignPlacementResult result);
}

/// <summary>Restores survivors through authoritative occupancy systems rather than transform-only placement.</summary>
public sealed class BattleCampaignPlacementService : IBattleCampaignPlacementService
{
    public bool TryPlaceAfterBattle(CombatUnit unit, BattleCampaignPlacementRequest request, out BattleCampaignPlacementResult result)
    {
        result = null;
        if (unit == null || request == null || unit.IsTransported) return unit != null && unit.IsTransported;
        if (BattleTheaterResolver.IsOnSpaceMap(unit) || request.SpaceTileIndex >= 0)
        {
            int target = request.SpaceTileIndex >= 0 ? request.SpaceTileIndex : unit.currentSpaceTileIndex;
            if (SpaceOccupancyManager.Instance == null || !SpaceOccupancyManager.Instance.RegisterUnit(unit, target)) return false;
            result = new BattleCampaignPlacementResult { SpaceTileIndex = target, Layer = unit.currentLayer }; return true;
        }

        var ts = TileSystem.GetForPlanet(request.PlanetIndex) ?? TileSystem.Instance;
        var occupancy = TileOccupancyManager.GetForPlanet(request.PlanetIndex) ?? TileOccupancyManager.Instance;
        var tile = ts != null ? ts.GetTileData(request.CampaignTileIndex) : null;
        if (ts == null || occupancy == null || tile == null || !tile.isPassable) return false;
        BattleDomain domain = BattleDomainResolver.Resolve(unit);
        if (domain == BattleDomain.Land && tile.IsWaterTile) return false;
        if ((domain == BattleDomain.NavalSurface || domain == BattleDomain.Underwater) && !tile.IsWaterTile) return false;

        occupancy.ClearOccupantById(unit.currentTileIndex, unit.currentLayer, unit.gameObject.GetRuntimeId());
        if (!occupancy.TrySetOccupant(request.CampaignTileIndex, unit.gameObject, request.Layer))
        {
            occupancy.TrySetOccupant(unit.currentTileIndex, unit.gameObject, unit.currentLayer);
            return false;
        }
        unit.planetIndex = request.PlanetIndex; unit.currentTileIndex = request.CampaignTileIndex; unit.currentLayer = request.Layer;
        unit.transform.position = ts.GetTileSurfacePosition(request.CampaignTileIndex);
        result = new BattleCampaignPlacementResult { CampaignTileIndex = request.CampaignTileIndex, Layer = request.Layer };
        return true;
    }
}
