using Unity.VisualScripting;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class TradeRoute
{
    [Header("City Trade")]
    public City sourceCity;
    public City destinationCity;
    
    [Header("Interplanetary Trade")]
    public bool isInterplanetaryRoute = false;
    public int originPlanetIndex = -1;
    public int destinationPlanetIndex = -1;
    public Civilization tradingCivilization; // For planet-to-planet trade
    
    [Header("Yields")]
    public int goldPerTurn;
    public int foodPerTurn;
    public int productionPerTurn;

    public int sciencePerTurn;

    public int culturePerTurn;
    public int faithPerTurn;
    public int policyPointsPerTurn;

    [Header("Route Rules")]
    public int routeDistance;
    public bool usesRoadConnection;
    public bool usesHarborConnection;
    public bool usesAirportConnection;
    public bool usesSpaceportConnection;
    public float raidChance;
    public bool wasRaidedThisTurn;
    public List<ResourceCost> resourcesPerTurn = new List<ResourceCost>();
    
    // Constants for trade route configuration
    public const int BASE_CITY_TRADE_GOLD_PER_TURN = 6;
    public const int DEFAULT_MAX_CITY_TRADE_RANGE = 25;
    public const int DEFAULT_MAX_AIRPORT_TRADE_RANGE = 80;
    public const float DEFAULT_RAID_CHANCE = 0.10f;
    private const float DISTANCE_GOLD_MULTIPLIER = 0.5f; // More gold for longer routes
    
    public TradeRoute(City source, City destination)
    {
        sourceCity = source;
        destinationCity = destination;
        isInterplanetaryRoute = false;
        
        // Calculate initial yields
        CalculateYields();
    }
    
    // New constructor for interplanetary trade
    public TradeRoute(Civilization civ, int originPlanet, int destPlanet)
    {
        tradingCivilization = civ;
        originPlanetIndex = originPlanet;
        destinationPlanetIndex = destPlanet;
        isInterplanetaryRoute = true;
        
        // Calculate initial yields for interplanetary trade
        CalculateYields();
    }
    
    public void CalculateYields()
    {
        // Base yields
        goldPerTurn = 0;
        foodPerTurn = 0;
        productionPerTurn = 0;
        sciencePerTurn = 0;
        culturePerTurn = 0;
        faithPerTurn = 0;
        policyPointsPerTurn = 0;
        resourcesPerTurn.Clear();
        
        if (isInterplanetaryRoute)
        {
            if (!TradeManager.CanSpaceTradeBetweenPlanets(originPlanetIndex, destinationPlanetIndex))
                return;

            // Interplanetary trade calculation - much more profitable but longer distance
            int planetDistance = Mathf.Abs(destinationPlanetIndex - originPlanetIndex);
            
            // Base interplanetary profit is higher than city trade
            int baseInterplanetaryGold = 8; // Higher base than cities
            
            // Distance bonus: farther planets = much more profit (like luxury goods)
            int distanceBonus = Mathf.RoundToInt(planetDistance * DISTANCE_GOLD_MULTIPLIER * 3);
            
            goldPerTurn = baseInterplanetaryGold + distanceBonus;
            
            // No food/production for interplanetary - just pure gold profit
            foodPerTurn = 0; 
            productionPerTurn = 0;
        }
        else if (sourceCity != null && destinationCity != null)
        {
            routeDistance = CalculateTileDistance(sourceCity, destinationCity);
            int maxRange = TradeManager.CurrentMaxCityTradeRange;
            bool samePlanet = sourceCity.planetIndex == destinationCity.planetIndex;
            usesHarborConnection = samePlanet && sourceCity.HasOperationalHarbor() && destinationCity.HasOperationalHarbor() && routeDistance <= maxRange;
            usesAirportConnection = samePlanet && sourceCity.HasOperationalAirport() && destinationCity.HasOperationalAirport() && routeDistance <= TradeManager.CurrentMaxAirportTradeRange;
            usesSpaceportConnection = TradeManager.CanSpacePortTradeBetween(sourceCity, destinationCity);
            usesRoadConnection = samePlanet && RoadConnectivityHelper.TryFindRoadPath(sourceCity, destinationCity, maxRange, out var roadPath);
            if (usesRoadConnection && roadPath != null && roadPath.Count > 0)
                routeDistance = roadPath.Count;
            else if (usesSpaceportConnection && !samePlanet)
                routeDistance = Mathf.Abs(destinationCity.planetIndex - sourceCity.planetIndex);

            goldPerTurn = TradeManager.CurrentBaseCityTradeGold
                          + Mathf.Max(0, destinationCity.level)
                          + Mathf.FloorToInt(Mathf.Max(0, destinationCity.GetGoldPerTurn()) / 5f);

            float goldMultiplier = 1f + CalculateRouteGoldBonus();
            goldPerTurn = Mathf.Max(0, Mathf.RoundToInt(goldPerTurn * goldMultiplier));
            faithPerTurn = CalculateRouteFaithBonus();
            raidChance = CalculateRaidChance();
            resourcesPerTurn.AddRange(destinationCity.GetTradeResourceExports());
        }
    }

    public bool RollRaidForTurn()
    {
        CalculateYields();
        wasRaidedThisTurn = !isInterplanetaryRoute && Random.value < raidChance;
        return wasRaidedThisTurn;
    }

    private float CalculateRouteGoldBonus()
    {
        float bonus = 0f;
        if (sourceCity?.owner != null)
            bonus += Mathf.Max(0f, sourceCity.owner.goldModifier);

        if (usesRoadConnection && RoadConnectivityHelper.TryFindRoadPath(sourceCity, destinationCity, TradeManager.CurrentMaxCityTradeRange, out var path))
            bonus += CalculateImprovementRouteGoldBonus(path);

        return Mathf.Clamp(bonus, 0f, 1f);
    }

    private float CalculateRaidChance()
    {
        if (isInterplanetaryRoute) return 0f;

        float chance = TradeManager.CurrentBaseCityTradeRaidChance;
        if (routeDistance > 10)
            chance += Mathf.Floor((routeDistance - 10) / 5f) * 0.02f;

        if (usesHarborConnection && !usesRoadConnection)
            chance += 0.02f; // piracy risk until future naval/security systems reduce it
        if (usesAirportConnection && !usesRoadConnection)
            chance = Mathf.Max(0f, chance - 0.03f);
        if (usesSpaceportConnection)
            chance = Mathf.Max(0f, chance - 0.05f);

        if (usesRoadConnection && RoadConnectivityHelper.TryFindRoadPath(sourceCity, destinationCity, TradeManager.CurrentMaxCityTradeRange, out var path))
            chance -= CalculateImprovementRaidReduction(path);

        chance -= CalculateOwnerRaidReduction(sourceCity?.owner);
        chance -= CalculateCitySecurityRaidReduction(sourceCity);
        chance -= CalculateCitySecurityRaidReduction(destinationCity);

        return Mathf.Clamp(chance, 0f, 0.40f);
    }

    private int CalculateRouteFaithBonus()
    {
        if (!usesRoadConnection || !RoadConnectivityHelper.TryFindRoadPath(sourceCity, destinationCity, TradeManager.CurrentMaxCityTradeRange, out var path))
            return 0;

        int faith = 0;
        foreach (int tileIndex in path)
        {
            var tileData = GetRouteTileData(tileIndex);
            string routeText = GetImprovementRouteText(tileData);
            if (ContainsRouteKeyword(routeText, "pilgrim")) faith += 1;
            if (ContainsRouteKeyword(routeText, "shrine")) faith += 1;
            if (ContainsRouteKeyword(routeText, "chapel")) faith += 1;
        }

        return Mathf.Min(faith, 5);
    }

    private float CalculateImprovementRouteGoldBonus(List<int> roadPath)
    {
        float bonus = 0f;
        foreach (int tileIndex in roadPath)
        {
            var tileData = GetRouteTileData(tileIndex);
            string routeText = GetImprovementRouteText(tileData);
            if (ContainsRouteKeyword(routeText, "trade post")) bonus += 0.05f;
            if (ContainsRouteKeyword(routeText, "market")) bonus += 0.05f;
            if (ContainsRouteKeyword(routeText, "caravan")) bonus += 0.10f;
            if (ContainsRouteKeyword(routeText, "customs")) bonus += 0.10f;
            if (ContainsRouteKeyword(routeText, "toll")) bonus += 0.05f;
        }

        return Mathf.Min(bonus, 0.50f);
    }

    private float CalculateImprovementRaidReduction(List<int> roadPath)
    {
        float reduction = 0f;
        foreach (int tileIndex in roadPath)
        {
            var tileData = GetRouteTileData(tileIndex);
            string routeText = GetImprovementRouteText(tileData);
            if (ContainsRouteKeyword(routeText, "trade post")) reduction += 0.01f;
            if (ContainsRouteKeyword(routeText, "guard")) reduction += 0.03f;
            if (ContainsRouteKeyword(routeText, "station")) reduction += 0.02f;
            if (ContainsRouteKeyword(routeText, "caravan")) reduction += 0.03f;
            if (ContainsRouteKeyword(routeText, "fort")) reduction += 0.02f;
            if (ContainsRouteKeyword(routeText, "patrol")) reduction += 0.02f;
        }

        return Mathf.Min(reduction, 0.10f);
    }

    private HexTileData GetRouteTileData(int tileIndex)
    {
        int planetIndex = sourceCity != null ? sourceCity.planetIndex : -1;
        var tileSystem = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        return tileSystem != null ? tileSystem.GetTileData(tileIndex) : null;
    }

    private static string GetImprovementRouteText(HexTileData tileData)
    {
        if (tileData?.improvement == null) return string.Empty;

        string text = $"{tileData.improvement.improvementName} {tileData.improvement.description}";
        if (tileData.builtUpgrades != null && tileData.builtUpgrades.Count > 0)
            text += " " + string.Join(" ", tileData.builtUpgrades);
        return text;
    }

    private static bool ContainsRouteKeyword(string text, string keyword)
    {
        return !string.IsNullOrEmpty(text)
               && text.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static float CalculateOwnerRaidReduction(Civilization civ)
    {
        if (civ == null) return 0f;

        float reduction = 0f;

        if (civ.researchedTechs != null)
        {
            foreach (var tech in civ.researchedTechs)
            {
                if (tech == null) continue;
                reduction += SecurityKeywordReduction(tech.techName);
                reduction += SecurityKeywordReduction(tech.description);
            }
        }

        if (civ.researchedCultures != null)
        {
            foreach (var culture in civ.researchedCultures)
            {
                if (culture == null) continue;
                reduction += SecurityKeywordReduction(culture.cultureName);
                reduction += SecurityKeywordReduction(culture.description);
            }
        }

        if (civ.currentGovernment != null)
        {
            reduction += SecurityKeywordReduction(civ.currentGovernment.governmentName);
            reduction += SecurityKeywordReduction(civ.currentGovernment.description);
        }

        if (civ.activePolicies != null)
        {
            foreach (var policy in civ.activePolicies)
            {
                if (policy == null) continue;
                reduction += SecurityKeywordReduction(policy.policyName);
                reduction += SecurityKeywordReduction(policy.description);
            }
        }

        return Mathf.Min(reduction, 0.15f);
    }

    private static float CalculateCitySecurityRaidReduction(City city)
    {
        if (city == null) return 0f;

        float reduction = 0f;
        foreach (var (building, _, _) in city.EnumerateOperationalBuildings())
        {
            if (building == null) continue;
            reduction += SecurityKeywordReduction(building.buildingName);
            reduction += SecurityKeywordReduction(building.description);
            if (building.isPerimeterWall || building.defenseBonus > 0) reduction += 0.01f;
        }

        return Mathf.Min(reduction, 0.10f);
    }

    private static float SecurityKeywordReduction(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0f;

        float reduction = 0f;
        if (ContainsRouteKeyword(text, "road")) reduction += 0.005f;
        if (ContainsRouteKeyword(text, "paved")) reduction += 0.005f;
        if (ContainsRouteKeyword(text, "trade")) reduction += 0.005f;
        if (ContainsRouteKeyword(text, "caravan")) reduction += 0.01f;
        if (ContainsRouteKeyword(text, "silk road")) reduction += 0.02f;
        if (ContainsRouteKeyword(text, "guard")) reduction += 0.015f;
        if (ContainsRouteKeyword(text, "watch")) reduction += 0.015f;
        if (ContainsRouteKeyword(text, "patrol")) reduction += 0.02f;
        if (ContainsRouteKeyword(text, "police")) reduction += 0.02f;
        if (ContainsRouteKeyword(text, "law")) reduction += 0.01f;
        if (ContainsRouteKeyword(text, "fort")) reduction += 0.015f;
        if (ContainsRouteKeyword(text, "walls")) reduction += 0.01f;

        return reduction;
    }

    private static int CalculateTileDistance(City source, City destination)
    {
        if (source == null || destination == null || source.planetIndex != destination.planetIndex)
            return int.MaxValue;

        var ts = TileSystem.GetForPlanet(source.planetIndex) ?? TileSystem.Instance;
        if (ts == null) return int.MaxValue;
        return Mathf.RoundToInt(ts.GetTileDistance(source.centerTileIndex, destination.centerTileIndex));
    }
    
    /// <summary>
    /// Calculate estimated trade route benefits between two cities
    /// </summary>
    public static (int goldPerTurn, int foodPerTurn, int productionPerTurn) CalculateTradeRouteBenefits(City source, City destination)
    {
        TradeRoute simulatedRoute = new TradeRoute(source, destination);
        return (simulatedRoute.goldPerTurn, simulatedRoute.foodPerTurn, simulatedRoute.productionPerTurn);
    }
    
    /// <summary>
    /// Calculate estimated interplanetary trade route benefits
    /// </summary>
    public static (int goldPerTurn, int foodPerTurn, int productionPerTurn) CalculateInterplanetaryTradeBenefits(Civilization civ, int originPlanet, int destPlanet)
    {
        TradeRoute simulatedRoute = new TradeRoute(civ, originPlanet, destPlanet);
        return (simulatedRoute.goldPerTurn, simulatedRoute.foodPerTurn, simulatedRoute.productionPerTurn);
    }
}
