/// <summary>
/// Centralized rules for determining which planet layer a unit should spawn on,
/// occupy, and use when validating layer-specific terrain.
/// </summary>
public static class UnitLayerRules
{
    /// <summary>
    /// Determine the appropriate gameplay layer for spawning/placing a unit on a given tile.
    /// </summary>
    public static GameManager.PlanetLayerType GetSpawnLayerForUnit(BaseUnit unit, HexTileData tileData)
    {
        var layer = GetSpawnTileLayerForUnit(unit, tileData);
        return LayerConversion.TryToPlanetLayerType(layer, out var planetLayer)
            ? planetLayer
            : GameManager.PlanetLayerType.Surface;
    }

    public static TileLayer GetSpawnTileLayerForUnit(BaseUnit unit, HexTileData tileData)
    {
        TileLayer preferred = GetNativeLayerForUnit(unit);
        if (CanUnitSpawnOnLayer(unit, preferred) && CanUnitUseTileOnLayer(unit, tileData, preferred))
            return preferred;

        UnitLayerMask spawnLayers = GetSpawnLayersForUnit(unit);
        TileLayer[] orderedLayers =
        {
            TileLayer.Surface,
            TileLayer.Underwater,
            TileLayer.Atmosphere,
            TileLayer.Orbit
        };

        for (int i = 0; i < orderedLayers.Length; i++)
        {
            var layer = orderedLayers[i];
            if (LayerConversion.MaskContains(spawnLayers, layer) && CanUnitSpawnOnLayer(unit, layer) && CanUnitUseTileOnLayer(unit, tileData, layer))
                return layer;
        }

        // If spawn-layer preferences do not fit this tile, still choose a layer the
        // unit can actually occupy on the tile rather than silently assigning an invalid one.
        UnitLayerMask allowedLayers = GetAllowedLayersForUnit(unit);
        for (int i = 0; i < orderedLayers.Length; i++)
        {
            var layer = orderedLayers[i];
            if (LayerConversion.MaskContains(allowedLayers, layer) && CanUnitUseTileOnLayer(unit, tileData, layer))
                return layer;
        }

        return TileLayer.Surface;
    }

    public static UnitLayerMask GetAllowedLayersForUnit(BaseUnit unit)
    {
        if (unit is CombatUnit combatUnit && combatUnit.data != null)
            return combatUnit.data.EffectiveAllowedLayers;
        if (unit is WorkerUnit workerUnit && workerUnit.data != null)
            return workerUnit.data.EffectiveAllowedLayers;
        return UnitLayerMask.Surface;
    }

    public static UnitLayerMask GetSpawnLayersForUnit(BaseUnit unit)
    {
        if (unit is CombatUnit combatUnit && combatUnit.data != null)
            return combatUnit.data.EffectiveSpawnLayers;
        if (unit is WorkerUnit workerUnit && workerUnit.data != null)
            return workerUnit.data.EffectiveSpawnLayers;
        return UnitLayerMask.Surface;
    }

    public static TileLayer GetNativeLayerForUnit(BaseUnit unit)
    {
        if (unit is CombatUnit combatUnit && combatUnit.data != null)
            return combatUnit.data.EffectiveNativeLayer;
        if (unit is WorkerUnit workerUnit && workerUnit.data != null)
            return workerUnit.data.EffectiveNativeLayer;
        return TileLayer.Surface;
    }

    public static bool CanUnitOccupyLayer(BaseUnit unit, TileLayer layer)
    {
        if (unit == null) return false;
        if (unit is CombatUnit combatUnit && combatUnit.data != null)
            return combatUnit.data.CanOccupyLayer(layer);
        if (unit is WorkerUnit workerUnit && workerUnit.data != null)
            return workerUnit.data.CanOccupyLayer(layer);
        return layer == TileLayer.Surface;
    }

    public static bool CanUnitSpawnOnLayer(BaseUnit unit, TileLayer layer)
    {
        if (unit == null) return false;
        if (unit is CombatUnit combatUnit && combatUnit.data != null)
            return combatUnit.data.CanSpawnOnLayer(layer);
        if (unit is WorkerUnit workerUnit && workerUnit.data != null)
            return workerUnit.data.CanSpawnOnLayer(layer);
        return layer == TileLayer.Surface;
    }

    public static bool CanUnitTransitionBetweenLayers(BaseUnit unit, TileLayer from, TileLayer to)
    {
        if (unit == null) return false;
        if (unit is CombatUnit combatUnit && combatUnit.data != null)
            return combatUnit.data.CanTransitionBetweenLayers(from, to);
        if (unit is WorkerUnit workerUnit && workerUnit.data != null)
            return workerUnit.data.CanTransitionBetweenLayers(from, to);
        return from == to && from == TileLayer.Surface;
    }

    public static bool CanUnitUseTileOnCurrentLayer(BaseUnit unit, HexTileData tileData)
    {
        return unit != null && CanUnitUseTileOnLayer(unit, tileData, unit.currentLayer);
    }

    public static bool CanUnitUseTileOnLayer(BaseUnit unit, HexTileData tileData, TileLayer layer)
    {
        if (unit == null || tileData == null) return false;
        if (!CanUnitOccupyLayer(unit, layer)) return false;

        switch (layer)
        {
            case TileLayer.Orbit:
            case TileLayer.Atmosphere:
                return true;
            case TileLayer.Underwater:
                return !tileData.isLand;
            case TileLayer.Surface:
            default:
                if (tileData.isLand) return true;
                if (unit is CombatUnit combatUnit && combatUnit.data != null)
                {
                    var category = combatUnit.data.unitType;
                    return CombatUnitData.IsNavalSurfaceCategory(category)
                           || category == CombatCategory.Submarine
                           || category == CombatCategory.SeaPlane;
                }
                return false;
        }
    }
}
