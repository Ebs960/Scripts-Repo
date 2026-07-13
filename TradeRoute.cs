using System;
using System.Collections.Generic;

[Serializable]
public class TradeRoute
{
    public int routeId;
    public int ownerCivilizationId;
    public int sourceNodeId;
    public int destinationNodeId;
    public List<int> relayNodeIds = new List<int>();
    public List<TradeRouteSegment> segments = new List<TradeRouteSegment>();
    public bool suspended;
    public TradeSuspensionReason suspensionReason;
    public TradeYield yields = new TradeYield();
    public float raidChance;
    public bool wasRaidedThisTurn;

    // Legacy read-only compatibility fields; the TradeNetworkManager is authoritative.
    public City sourceCity;
    public City destinationCity;
    public bool isInterplanetaryRoute;
    public int originPlanetIndex = -1;
    public int destinationPlanetIndex = -1;
    public Civilization tradingCivilization;
    public int goldPerTurn, foodPerTurn, productionPerTurn, sciencePerTurn, culturePerTurn, faithPerTurn, policyPointsPerTurn;
    public int routeDistance;
    public bool usesRoadConnection, usesHarborConnection, usesAirportConnection, usesSpaceportConnection;
    public List<ResourceCost> resourcesPerTurn = new List<ResourceCost>();

    public const int BASE_CITY_TRADE_GOLD_PER_TURN = 6;
    public const int DEFAULT_MAX_CITY_TRADE_RANGE = 25;
    public const int DEFAULT_MAX_AIRPORT_TRADE_RANGE = 80;
    public const float DEFAULT_RAID_CHANCE = 0.10f;

    public TradeRoute() { }

    public TradeRoute(City source, City destination)
    {
        sourceCity = source;
        destinationCity = destination;
        tradingCivilization = source != null ? source.owner : null;
        originPlanetIndex = source != null ? source.planetIndex : -1;
        destinationPlanetIndex = destination != null ? destination.planetIndex : -1;
        isInterplanetaryRoute = originPlanetIndex >= 0 && destinationPlanetIndex >= 0 && originPlanetIndex != destinationPlanetIndex;
    }

    public TradeRoute(Civilization civ, int originPlanet, int destPlanet)
    {
        tradingCivilization = civ;
        originPlanetIndex = originPlanet;
        destinationPlanetIndex = destPlanet;
        isInterplanetaryRoute = true;
    }

    public void CalculateYields()
    {
        TradeNetworkManager.Instance?.RecalculateRoute(this);
        SyncLegacyYieldFields();
    }

    public bool RollRaidForTurn()
    {
        CalculateYields();
        wasRaidedThisTurn = TradeNetworkManager.Instance != null && TradeNetworkManager.Instance.RollRaidForRoute(this);
        return wasRaidedThisTurn;
    }

    public void SyncLegacyYieldFields()
    {
        if (yields == null) yields = new TradeYield();
        goldPerTurn = yields.goldPerTurn;
        foodPerTurn = yields.foodPerTurn;
        productionPerTurn = yields.productionPerTurn;
        sciencePerTurn = yields.sciencePerTurn;
        culturePerTurn = yields.culturePerTurn;
        faithPerTurn = yields.faithPerTurn;
        policyPointsPerTurn = yields.policyPointsPerTurn;
        resourcesPerTurn = yields.resourcesPerTurn ?? new List<ResourceCost>();
        routeDistance = 0;
        usesRoadConnection = usesHarborConnection = usesAirportConnection = usesSpaceportConnection = false;
        if (segments != null)
        {
            foreach (var s in segments)
            {
                if (s == null) continue;
                routeDistance += Math.Max(0, s.pathCost);
                usesRoadConnection |= s.domain == TradeMapDomain.PlanetSurface;
                usesAirportConnection |= s.domain == TradeMapDomain.PlanetAir;
                usesSpaceportConnection |= s.domain == TradeMapDomain.PlanetOrbit || s.domain == TradeMapDomain.SolarSystemSpace || s.domain == TradeMapDomain.Interstellar;
            }
        }
    }

    public static (int goldPerTurn, int foodPerTurn, int productionPerTurn) CalculateTradeRouteBenefits(City source, City destination)
    {
        var route = TradeNetworkManager.Instance != null ? TradeNetworkManager.Instance.PreviewRoute(source, destination) : new TradeRoute(source, destination);
        route.CalculateYields();
        return (route.goldPerTurn, route.foodPerTurn, route.productionPerTurn);
    }

    public static (int goldPerTurn, int foodPerTurn, int productionPerTurn) CalculateInterplanetaryTradeBenefits(Civilization civ, int originPlanet, int destPlanet)
    {
        var route = TradeNetworkManager.Instance != null ? TradeNetworkManager.Instance.PreviewRoute(civ, originPlanet, destPlanet) : new TradeRoute(civ, originPlanet, destPlanet);
        route.CalculateYields();
        return (route.goldPerTurn, route.foodPerTurn, route.productionPerTurn);
    }
}
