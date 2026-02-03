/// <summary>
/// Centralized rules for determining which planet layer a unit should spawn on.
/// Keep logic minimal and consistent to avoid scattered "water => underwater else surface" checks.
/// </summary>
public static class UnitLayerRules
{
    /// <summary>
    /// Determine the appropriate gameplay layer for spawning/placing a unit on a given tile.
    /// </summary>
    public static GameManager.PlanetLayerType GetSpawnLayerForUnit(BaseUnit unit, HexTileData tileData)
    {
        // Minimal legacy behavior: land => Surface, water => Underwater.
        // Do NOT infer orbit/atmosphere/mantle here unless existing gameplay explicitly sets it.
        if (tileData == null) return GameManager.PlanetLayerType.Surface;
        return tileData.isLand ? GameManager.PlanetLayerType.Surface : GameManager.PlanetLayerType.Underwater;
    }
}

