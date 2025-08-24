using System;
using UnityEngine;

/// <summary>
/// Planet-scoped context passed to systems instead of relying on implicit globals.
/// </summary>
public sealed class PlanetContext
{
    public int Index;
    public PlanetGenerator Generator;
    public SphericalHexGrid Grid;
    public ClimateManager Climate;
    public MoonGenerator Moon;
    public GameObject Root; // planet root GameObject
}

/// <summary>
/// Event bus for planet lifecycle notifications. Fired by GameManager.
/// </summary>
public sealed class PlanetEventBus
{
    // Fired after grid is built (subdivision complete), before surface generation.
    public event Action<int> OnGridBuilt;
    // Fired when surface generation completes (HasGeneratedSurface = true).
    public event Action<int> OnSurfaceGenerated;
    // Fired when managers (Climate, Moon, etc.) are attached/configured.
    public event Action<int> OnManagersAttached;
    // Fired as the final step when the planet is fully ready for gameplay.
    public event Action<int> OnPlanetReady;

    public void FireGridBuilt(int planetIndex) => OnGridBuilt?.Invoke(planetIndex);
    public void FireSurfaceGenerated(int planetIndex) => OnSurfaceGenerated?.Invoke(planetIndex);
    public void FireManagersAttached(int planetIndex) => OnManagersAttached?.Invoke(planetIndex);
    public void FirePlanetReady(int planetIndex) => OnPlanetReady?.Invoke(planetIndex);
}
