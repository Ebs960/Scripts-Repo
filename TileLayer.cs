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
