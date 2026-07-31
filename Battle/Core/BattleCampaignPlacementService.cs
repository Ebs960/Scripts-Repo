using UnityEngine;

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
    public int StackSlot = -1;
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
        if (ts == null || occupancy == null || tile == null || !tile.isPassable)
            return false;

        if (!UnitLayerRules.CanUnitUseTileOnLayer(unit, tile, request.Layer))
            return false;

        var previousOccupancy = TileOccupancyManager.GetForPlanet(unit.planetIndex) ?? TileOccupancyManager.Instance;
        int previousPlanet = unit.planetIndex;
        int previousTile = unit.currentTileIndex;
        TileLayer previousLayer = unit.currentLayer;
        int runtimeId = unit.gameObject.GetRuntimeId();

        ClearUnitFromAllLayers(previousOccupancy, previousTile, runtimeId);
        if (occupancy != previousOccupancy || request.CampaignTileIndex != previousTile)
            ClearUnitFromAllLayers(occupancy, request.CampaignTileIndex, runtimeId);

        int maxStack = unit.owner != null ? unit.owner.GetMaxStackSize() : 1;
        int stackSlot = occupancy.TryAddToStack(request.CampaignTileIndex, request.Layer, unit.gameObject, maxStack);
        if (stackSlot < 0)
        {
            if (previousOccupancy != null && previousTile >= 0)
                previousOccupancy.TryAddToStack(previousTile, previousLayer, unit.gameObject, maxStack);
            return false;
        }

        SpaceOccupancyManager.Instance?.UnregisterUnit(unit);
        unit.planetIndex = request.PlanetIndex;
        unit.currentTileIndex = request.CampaignTileIndex;
        unit.currentLayer = request.Layer;
        unit.stackSlot = stackSlot;
        PositionUnitForLayer(unit, ts, request.CampaignTileIndex, request.Layer);

        result = new BattleCampaignPlacementResult
        {
            CampaignTileIndex = request.CampaignTileIndex,
            Layer = request.Layer,
            StackSlot = stackSlot,
        };
        return true;
    }

    private static void ClearUnitFromAllLayers(TileOccupancyManager occupancy, int tileIndex, int runtimeId)
    {
        if (occupancy == null || tileIndex < 0 || runtimeId == 0)
            return;

        occupancy.ClearOccupantById(tileIndex, TileLayer.Surface, runtimeId);
        occupancy.ClearOccupantById(tileIndex, TileLayer.Underwater, runtimeId);
        occupancy.ClearOccupantById(tileIndex, TileLayer.Atmosphere, runtimeId);
        occupancy.ClearOccupantById(tileIndex, TileLayer.Orbit, runtimeId);
    }

    private static void PositionUnitForLayer(CombatUnit unit, TileSystem tileSystem, int tileIndex, TileLayer layer)
    {
        Vector3 position = tileSystem.GetTileSurfacePosition(tileIndex);
        if (layer == TileLayer.Orbit)
            position += Vector3.up * PlanetGenerator.GetOrbitHeight(unit.planetIndex);
        else if (layer == TileLayer.Atmosphere)
            position += Vector3.up * Mathf.Max(1f, PlanetGenerator.GetOrbitHeight(unit.planetIndex) * 0.5f);

        unit.transform.position = position;
        if (layer == TileLayer.Surface)
            unit.ApplyStackOffset();
    }
}
