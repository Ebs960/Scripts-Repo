using System.Collections.Generic;

/// <summary>
/// Simulation fidelity tier for a planet, used to scale down per-frame/per-turn work
/// (wildlife, climate, etc.) on planets the player is not currently interacting with.
///
/// IMPORTANT: this affects simulation FREQUENCY/DETAIL only. It must never make any
/// AI-controlled civilization/tribe/city-state weaker, slower to react, or less
/// competitive than the player - civ turn processing (AI planning, combat, economy,
/// diplomacy) always runs at full fidelity for every civ regardless of tier. Only
/// cosmetic/ambient systems (wildlife movement ticks, climate tile-color repaint,
/// etc.) are scaled by tier.
/// </summary>
public enum PlanetSimulationTier
{
    /// <summary>Currently viewed planet (WorldViewContext.Current). Full per-frame fidelity.</summary>
    Full,
    /// <summary>Generated planet with active civilization presence (cities). Reduced-frequency simulation.</summary>
    Warm,
    /// <summary>Generated planet with no civilization presence. Aggregate/statistical simulation only.</summary>
    Cold,
    /// <summary>Not yet generated / never visited. No simulation runs.</summary>
    Dormant
}

/// <summary>
/// Stateless lookup for per-planet simulation tier. Computes on demand from existing
/// authorities (WorldViewContext for "currently viewed", GameManager.PlanetData for
/// "generated", CivilizationManager for "has active civ presence") rather than
/// maintaining its own duplicated state, so it can never go stale.
///
/// Consumers (AnimalManager, ClimateManager, etc.) should call <see cref="GetTier"/>
/// ONCE per planet per update pass (not per-entity) and reuse the result for every
/// entity on that planet.
/// </summary>
public static class PlanetSimulationManager
{
    /// <summary>Warm-tier planets are re-simulated every N turns instead of every turn.</summary>
    public const int WarmTierTurnInterval = 3;
    /// <summary>Cold-tier planets are re-simulated every N turns instead of every turn.</summary>
    public const int ColdTierTurnInterval = 10;

    public static PlanetSimulationTier GetTier(int planetIndex)
    {
        var gm = GameManager.Instance;
        var planetData = gm != null ? gm.GetPlanetData() : null;
        if (planetData == null || !planetData.TryGetValue(planetIndex, out var data) || data == null || !data.isGenerated)
            return PlanetSimulationTier.Dormant;

        var view = WorldViewContext.Instance;
        if (view != null && view.Current.Mode == WorldViewMode.Planet && view.Current.PlanetIndex.HasValue && view.Current.PlanetIndex.Value == planetIndex)
            return PlanetSimulationTier.Full;
        if (view == null && gm != null && gm.currentPlanetIndex == planetIndex)
            return PlanetSimulationTier.Full;

        return HasActiveCivPresence(planetIndex) ? PlanetSimulationTier.Warm : PlanetSimulationTier.Cold;
    }

    /// <summary>Whether a tier-scaled system should run its update this turn given its tier.</summary>
    public static bool ShouldSimulateThisTurn(int planetIndex, int currentTurn)
    {
        switch (GetTier(planetIndex))
        {
            case PlanetSimulationTier.Full:
                return true;
            case PlanetSimulationTier.Warm:
                return currentTurn % WarmTierTurnInterval == 0;
            case PlanetSimulationTier.Cold:
                return currentTurn % ColdTierTurnInterval == 0;
            default: // Dormant
                return false;
        }
    }

    private static bool HasActiveCivPresence(int planetIndex)
    {
        var cm = CivilizationManager.Instance;
        if (cm == null) return false;
        var civs = cm.GetAllCivs();
        if (civs == null) return false;
        foreach (var civ in civs)
        {
            if (civ == null || civ.cities == null) continue;
            for (int i = 0; i < civ.cities.Count; i++)
            {
                var city = civ.cities[i];
                if (city != null && city.planetIndex == planetIndex) return true;
            }
        }
        return false;
    }
}
