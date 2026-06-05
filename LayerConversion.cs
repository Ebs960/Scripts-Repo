using UnityEngine;

/// <summary>
/// Adapter helpers between planet gameplay layers (GameManager.PlanetLayerType) and tile occupancy layers (TileLayer).
/// These enums intentionally remain separate to avoid risky merges across systems.
/// </summary>
public static class LayerConversion
{
    public static bool TryToTileLayer(GameManager.PlanetLayerType planetLayer, out TileLayer tileLayer)
    {
        switch (planetLayer)
        {
            case GameManager.PlanetLayerType.Surface:
                tileLayer = TileLayer.Surface;
                return true;
            case GameManager.PlanetLayerType.Underwater:
                tileLayer = TileLayer.Underwater;
                return true;
            case GameManager.PlanetLayerType.Atmosphere:
                tileLayer = TileLayer.Atmosphere;
                return true;
            case GameManager.PlanetLayerType.Orbit:
                tileLayer = TileLayer.Orbit;
                return true;
            case GameManager.PlanetLayerType.Mantle:
            default:
                tileLayer = TileLayer.Surface;
                return false;
        }
    }

    public static bool TryToPlanetLayerType(TileLayer tileLayer, out GameManager.PlanetLayerType planetLayer)
    {
        switch (tileLayer)
        {
            case TileLayer.Surface:
                planetLayer = GameManager.PlanetLayerType.Surface;
                return true;
            case TileLayer.Underwater:
                planetLayer = GameManager.PlanetLayerType.Underwater;
                return true;
            case TileLayer.Atmosphere:
                planetLayer = GameManager.PlanetLayerType.Atmosphere;
                return true;
            case TileLayer.Orbit:
                planetLayer = GameManager.PlanetLayerType.Orbit;
                return true;
            default:
                planetLayer = GameManager.PlanetLayerType.Surface;
                return false;
        }
    }

    public static UnitLayerMask ToMask(TileLayer tileLayer)
    {
        switch (tileLayer)
        {
            case TileLayer.Surface: return UnitLayerMask.Surface;
            case TileLayer.Underwater: return UnitLayerMask.Underwater;
            case TileLayer.Atmosphere: return UnitLayerMask.Atmosphere;
            case TileLayer.Orbit: return UnitLayerMask.Orbit;
            default: return UnitLayerMask.None;
        }
    }

    public static bool MaskContains(UnitLayerMask mask, TileLayer tileLayer)
    {
        return (mask & ToMask(tileLayer)) != 0;
    }
}

