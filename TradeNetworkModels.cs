using System;
using System.Collections.Generic;
using UnityEngine;

[Flags]
public enum TradeDomainMask
{
    None = 0,
    PlanetSurface = 1 << 0,
    PlanetMaritime = 1 << 1,
    PlanetAir = 1 << 2,
    PlanetOrbit = 1 << 3,
    SolarSystemSpace = 1 << 4,
    Interstellar = 1 << 5,
    All = PlanetSurface | PlanetMaritime | PlanetAir | PlanetOrbit | SolarSystemSpace | Interstellar
}

public enum TradeMapDomain
{
    PlanetSurface,
    PlanetMaritime,
    PlanetAir,
    PlanetOrbit,
    SolarSystemSpace,
    Interstellar
}

[Serializable]
public struct TradeLocation
{
    public TradeMapDomain domain;
    public int planetId;
    public int planetaryTileIndex;
    public int spaceTileIndex;
    public int starSystemId;
}

public enum TradeNodeType
{
    City,
    TradePost,
    Caravanserai,
    Harbor,
    Airport,
    Spaceport,
    SpaceStation,
    OrbitalTradeHub,
    DeepSpaceTradePost
}

[Serializable]
public class TradeNodeCapability
{
    public bool providesTradeNode;
    public TradeNodeType nodeType = TradeNodeType.TradePost;
    public TradeDomainMask supportedDomains = TradeDomainMask.PlanetSurface;
    public int tradeRange;
    public int surfaceTradeRange;
    public int maritimeTradeRange;
    public int airTradeRange;
    public int orbitTradeRange;
    public int solarSpaceTradeRange;
    public int interstellarTradeRange;
    [Tooltip("Legacy/backward-compatible capacity field. Prefer civilizationRouteCapacityBonus for empire-wide slots and nodeThroughputCapacity for local traffic limits.")]
    public int routeCapacity;
    public int civilizationRouteCapacityBonus;
    public int nodeThroughputCapacity;
    public bool canOriginateRoutes;
    public bool canReceiveRoutes = true;
    public bool canRelayRoutes;
    public float routeGoldModifier;
    public float raidChanceReduction;
    public bool isPlanetaryGateway;
    public bool isOrbitalGateway;
}

[Serializable]
public class TradeNodeRuntime
{
    public int nodeId;
    public int ownerCivilizationId;
    public TradeNodeType nodeType;
    public TradeLocation location;
    public int tradeRange;
    public int surfaceTradeRange;
    public int maritimeTradeRange;
    public int airTradeRange;
    public int orbitTradeRange;
    public int solarSpaceTradeRange;
    public int interstellarTradeRange;
    [Tooltip("Legacy/backward-compatible capacity field. Prefer civilizationRouteCapacityBonus for empire-wide slots and nodeThroughputCapacity for local traffic limits.")]
    public int routeCapacity;
    public int civilizationRouteCapacityBonus;
    public int nodeThroughputCapacity;
    public bool canOriginateRoutes;
    public bool canReceiveRoutes;
    public bool canRelayRoutes;
    public bool isOperational = true;
    public TradeDomainMask supportedDomains;
    public float routeGoldModifier;
    public float raidChanceReduction;
    public bool isPlanetaryGateway;
    public bool isOrbitalGateway;
    [NonSerialized] public City city;
    [NonSerialized] public ImprovementInstance improvement;
    public string displayName;
}

public enum TradeSuspensionReason { None, InvalidPath, Blockade, Diplomacy, Capacity, DisabledEndpoint, Raid }
public enum TradeSegmentRiskType { GroundBandits, MaritimePiracy, AirDisruption, SpacePiracy, SpaceHazard, BlockadedGateway }

[Serializable]
public class TradeYield
{
    public int goldPerTurn;
    public int foodPerTurn;
    public int productionPerTurn;
    public int sciencePerTurn;
    public int culturePerTurn;
    public int faithPerTurn;
    public int policyPointsPerTurn;
    public List<ResourceCost> resourcesPerTurn = new List<ResourceCost>();
}

[Serializable]
public class TradeRouteSegment
{
    public int fromNodeId;
    public int toNodeId;
    public TradeMapDomain domain;
    public int pathCost;
    public List<int> planetaryTilePath = new List<int>();
    public List<int> spaceTilePath = new List<int>();
    public float raidChance;
    public TradeSegmentRiskType riskType;
}

[Serializable]
public class TradeTurnBreakdownEntry
{
    public int routeId;
    public string label;
    public TradeYield yields = new TradeYield();
    public bool suspended;
    public bool wasRaidedThisTurn;
    public TradeSuspensionReason suspensionReason;
}
