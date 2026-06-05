using System;

/// <summary>
/// Layers that may occupy a tile. Surface is the legacy/default layer.
/// </summary>
public enum TileLayer
{
    Surface = 0,
    Underwater = 1,
    Atmosphere = 2,
    Orbit = 3
}

/// <summary>
/// Inspector-friendly bitmask used by unit data to describe which gameplay
/// layers a unit may occupy or spawn on. Keep values in sync with TileLayer.
/// </summary>
[Flags]
public enum UnitLayerMask
{
    None = 0,
    Surface = 1 << (int)TileLayer.Surface,
    Underwater = 1 << (int)TileLayer.Underwater,
    Atmosphere = 1 << (int)TileLayer.Atmosphere,
    Orbit = 1 << (int)TileLayer.Orbit,
    All = Surface | Underwater | Atmosphere | Orbit
}
