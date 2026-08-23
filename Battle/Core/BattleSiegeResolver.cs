using System.Collections.Generic;

/// <summary>Resolves siege context solely from explicit campaign-data references.</summary>
public static class BattleSiegeResolver
{
    public static BattleFortificationProfile SelectCityProfile(IEnumerable<BuildingData> buildings)
    {
        BattleFortificationProfile best = null;
        if (buildings == null) return null;
        foreach (var building in buildings)
            if (building != null && building.grantsCityFortifications && building.tacticalFortificationProfile != null
                && (best == null || building.tacticalFortificationProfile.fortificationTier > best.fortificationTier))
                best = building.tacticalFortificationProfile;
        return best;
    }

    public static BattleSiegeType ClassifyCity(IEnumerable<BuildingData> buildings)
        => SelectCityProfile(buildings) == null ? BattleSiegeType.Settlement : BattleSiegeType.FortifiedSettlement;

    public static BattleFortificationProfile SelectFortProfile(ImprovementData improvement)
        => improvement != null && improvement.grantsFortifications ? improvement.tacticalFortificationProfile : null;
}
