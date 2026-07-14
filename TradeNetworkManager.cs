using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class TradeNetworkManager : MonoBehaviour
{
    public static TradeNetworkManager Instance { get; private set; }
    public List<TradeNodeRuntime> allTradeNodes = new List<TradeNodeRuntime>();
    public List<TradeRoute> activeRoutes = new List<TradeRoute>();
    public List<TradeTurnBreakdownEntry> lastProcessedBreakdown = new List<TradeTurnBreakdownEntry>();
    private int nextRouteId = 1;
    private readonly Dictionary<int, TradeNodeRuntime> nodesById = new Dictionary<int, TradeNodeRuntime>();
    private bool isRebuildingRegistry;
    private bool registryDirty = true;

    private void Awake() { if (Instance != null && Instance != this) { Destroy(gameObject); return; } Instance = this; }
    public static TradeNetworkManager EnsureInstance() { if (Instance != null) return Instance; return new GameObject("TradeNetworkManager").AddComponent<TradeNetworkManager>(); }

    public void RebuildRegistry()
    {
        if (isRebuildingRegistry) return;
        isRebuildingRegistry = true;
        try
        {
            allTradeNodes.Clear();
            nodesById.Clear();
            IEnumerable<Civilization> civs = CivilizationManager.Instance != null
                ? CivilizationManager.Instance.GetAllCivs()
                : FindObjectsByType<Civilization>(FindObjectsSortMode.None);
            foreach (var civ in civs)
                if (civ != null && civ.cities != null) foreach (var city in civ.cities) RegisterOrUpdateCityNode(city);
            foreach (var imp in FindObjectsByType<ImprovementInstance>(FindObjectsSortMode.None)) RegisterOrUpdateImprovementNode(imp);
            nextRouteId = Mathf.Max(nextRouteId, activeRoutes.Count > 0 ? activeRoutes.Max(r => r != null ? r.routeId : 0) + 1 : 1);
            registryDirty = false;
        }
        finally
        {
            isRebuildingRegistry = false;
        }

        foreach (var r in activeRoutes.ToArray()) RevalidateAndReroute(r);
    }

    private void EnsureRegistry()
    {
        if (registryDirty || nodesById.Count == 0) RebuildRegistry();
    }

    public TradeNodeRuntime RegisterOrUpdateCityNode(City city)
    {
        if (city == null) return null;
        var node = BuildCityNode(city); UpsertNode(node); return node;
    }

    public TradeNodeRuntime RegisterOrUpdateImprovementNode(ImprovementInstance improvement)
    {
        if (improvement == null || improvement.data == null || !improvement.data.tradeNodeCapability.providesTradeNode) return null;
        var cap = AggregateImprovementCapability(improvement);
        var node = new TradeNodeRuntime
        {
            nodeId = GetImprovementNodeId(improvement), ownerCivilizationId = improvement.owner != null ? improvement.owner.GetRuntimeId() : 0,
            nodeType = cap.nodeType, location = new TradeLocation { domain = MaskToPrimaryDomain(cap.supportedDomains), planetId = improvement.PlanetIndex, planetaryTileIndex = improvement.tileIndex, spaceTileIndex = improvement.spaceTileIndex, starSystemId = 0 },
            tradeRange = Mathf.Max(0, cap.tradeRange), civilizationRouteCapacityBonus = GetCapabilityCivilizationCapacity(cap), nodeThroughputCapacity = GetCapabilityThroughputCapacity(cap), canOriginateRoutes = cap.canOriginateRoutes,
            canReceiveRoutes = cap.canReceiveRoutes, canRelayRoutes = cap.canRelayRoutes, isOperational = improvement.gameObject.activeInHierarchy && !improvement.IsFortNeutralized,
            supportedDomains = cap.supportedDomains, routeGoldModifier = cap.routeGoldModifier, raidChanceReduction = cap.raidChanceReduction,
            isPlanetaryGateway = cap.isPlanetaryGateway, isOrbitalGateway = cap.isOrbitalGateway, improvement = improvement,
            displayName = improvement.data.improvementName
        };
        UpsertNode(node); return node;
    }

    private TradeNodeRuntime BuildCityNode(City city)
    {
        int baseSurfaceRange = TradeManager.CurrentMaxCityTradeRange;
        var node = new TradeNodeRuntime
        {
            nodeId = GetCityNodeId(city), ownerCivilizationId = city.owner != null ? city.owner.GetRuntimeId() : 0,
            nodeType = TradeNodeType.City,
            location = new TradeLocation { domain = TradeMapDomain.PlanetSurface, planetId = city.planetIndex, planetaryTileIndex = city.centerTileIndex, spaceTileIndex = -1, starSystemId = 0 },
            tradeRange = baseSurfaceRange, surfaceTradeRange = baseSurfaceRange, civilizationRouteCapacityBonus = 1,
            canOriginateRoutes = true, canReceiveRoutes = true, canRelayRoutes = false, isOperational = true,
            supportedDomains = TradeDomainMask.PlanetSurface, city = city, displayName = city.cityName
        };
        foreach (var tuple in city.EnumerateOperationalBuildings())
        {
            var b = tuple.data; if (b == null) continue;
            ApplyCapability(node, b.tradeNodeCapability);
            if (b.providesHarbor) { node.supportedDomains |= TradeDomainMask.PlanetMaritime; node.nodeType = TradeNodeType.Harbor; node.canReceiveRoutes = true; node.maritimeTradeRange = Mathf.Max(node.maritimeTradeRange, TradeManager.CurrentMaxCityTradeRange); }
            if (b.providesAirport) { node.supportedDomains |= TradeDomainMask.PlanetAir; node.airTradeRange = Mathf.Max(node.airTradeRange, TradeManager.CurrentMaxAirportTradeRange); }
            if (b.providesSpaceport) { node.supportedDomains |= TradeDomainMask.PlanetOrbit | TradeDomainMask.SolarSystemSpace; node.isPlanetaryGateway = true; node.isOrbitalGateway = true; node.orbitTradeRange = Mathf.Max(node.orbitTradeRange, 3); node.solarSpaceTradeRange = Mathf.Max(node.solarSpaceTradeRange, 3); }
        }
        node.civilizationRouteCapacityBonus = Mathf.Max(0, node.civilizationRouteCapacityBonus);
        return node;
    }

    private void ApplyCapability(TradeNodeRuntime node, TradeNodeCapability cap)
    {
        if (cap == null || !cap.providesTradeNode) return;
        node.supportedDomains |= cap.supportedDomains;
        AddCapabilityRange(node, cap);
        node.civilizationRouteCapacityBonus += GetCapabilityCivilizationCapacity(cap);
        node.nodeThroughputCapacity += GetCapabilityThroughputCapacity(cap);
        node.canOriginateRoutes |= cap.canOriginateRoutes; node.canReceiveRoutes |= cap.canReceiveRoutes; node.canRelayRoutes |= cap.canRelayRoutes;
        node.routeGoldModifier += cap.routeGoldModifier; node.raidChanceReduction += cap.raidChanceReduction; node.isPlanetaryGateway |= cap.isPlanetaryGateway; node.isOrbitalGateway |= cap.isOrbitalGateway;
    }

    private int GetCapabilityCivilizationCapacity(TradeNodeCapability cap)
    {
        if (cap == null) return 0;
        return Mathf.Max(0, cap.civilizationRouteCapacityBonus);
    }

    private int GetCapabilityThroughputCapacity(TradeNodeCapability cap)
    {
        if (cap == null) return 0;
        return Mathf.Max(0, cap.nodeThroughputCapacity);
    }

    private void AddCapabilityRange(TradeNodeRuntime node, TradeNodeCapability cap)
    {
        int fallback = Mathf.Max(0, cap.tradeRange);
        node.tradeRange += fallback;
        node.surfaceTradeRange += Mathf.Max(0, cap.surfaceTradeRange > 0 ? cap.surfaceTradeRange : (cap.supportedDomains.HasFlag(TradeDomainMask.PlanetSurface) ? fallback : 0));
        node.maritimeTradeRange += Mathf.Max(0, cap.maritimeTradeRange > 0 ? cap.maritimeTradeRange : (cap.supportedDomains.HasFlag(TradeDomainMask.PlanetMaritime) ? fallback : 0));
        node.airTradeRange += Mathf.Max(0, cap.airTradeRange > 0 ? cap.airTradeRange : (cap.supportedDomains.HasFlag(TradeDomainMask.PlanetAir) ? fallback : 0));
        node.orbitTradeRange += Mathf.Max(0, cap.orbitTradeRange > 0 ? cap.orbitTradeRange : (cap.supportedDomains.HasFlag(TradeDomainMask.PlanetOrbit) ? fallback : 0));
        node.solarSpaceTradeRange += Mathf.Max(0, cap.solarSpaceTradeRange > 0 ? cap.solarSpaceTradeRange : (cap.supportedDomains.HasFlag(TradeDomainMask.SolarSystemSpace) ? fallback : 0));
        node.interstellarTradeRange += Mathf.Max(0, cap.interstellarTradeRange > 0 ? cap.interstellarTradeRange : (cap.supportedDomains.HasFlag(TradeDomainMask.Interstellar) ? fallback : 0));
    }

    private TradeNodeCapability AggregateImprovementCapability(ImprovementInstance imp)
    {
        var cap = Clone(imp.data.tradeNodeCapability);
        foreach (var up in imp.EnumerateAppliedUpgrades()) if (up != null) { cap.supportedDomains |= up.tradeSupportedDomains; cap.tradeRange += up.tradeRangeModifier; cap.civilizationRouteCapacityBonus += up.tradeRouteCapacityModifier; cap.canRelayRoutes |= up.grantsTradeRelay; cap.routeGoldModifier += up.tradeRouteGoldModifier; cap.raidChanceReduction += up.tradeRaidChanceReduction; }
        return cap;
    }

    private static TradeNodeCapability Clone(TradeNodeCapability c) => c == null ? new TradeNodeCapability() : new TradeNodeCapability { providesTradeNode = c.providesTradeNode, nodeType = c.nodeType, supportedDomains = c.supportedDomains, tradeRange = c.tradeRange, surfaceTradeRange = c.surfaceTradeRange, maritimeTradeRange = c.maritimeTradeRange, airTradeRange = c.airTradeRange, orbitTradeRange = c.orbitTradeRange, solarSpaceTradeRange = c.solarSpaceTradeRange, interstellarTradeRange = c.interstellarTradeRange, civilizationRouteCapacityBonus = c.civilizationRouteCapacityBonus, nodeThroughputCapacity = c.nodeThroughputCapacity, canOriginateRoutes = c.canOriginateRoutes, canReceiveRoutes = c.canReceiveRoutes, canRelayRoutes = c.canRelayRoutes, routeGoldModifier = c.routeGoldModifier, raidChanceReduction = c.raidChanceReduction, isPlanetaryGateway = c.isPlanetaryGateway, isOrbitalGateway = c.isOrbitalGateway };
    private void UpsertNode(TradeNodeRuntime n) { if (n == null || n.nodeId == 0) return; nodesById[n.nodeId] = n; int i = allTradeNodes.FindIndex(x => x.nodeId == n.nodeId); if (i >= 0) allTradeNodes[i] = n; else allTradeNodes.Add(n); }
    public int GetCityNodeId(City c) => c != null ? StableTradeNodeId(TradeNodeType.City, 0, c.planetIndex, c.centerTileIndex, -1, c.cityName) : 0;
    public int GetImprovementNodeId(ImprovementInstance i) => i != null ? StableTradeNodeId(i.data != null ? i.data.tradeNodeCapability.nodeType : TradeNodeType.TradePost, 0, i.PlanetIndex, i.tileIndex, i.spaceTileIndex, i.data != null ? i.data.name : null) : 0;

    private int StableTradeNodeId(TradeNodeType type, int ownerId, int planetId, int tileIndex, int spaceTileIndex, string key)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + 0x54524E; // TRN
            hash = hash * 31 + (int)type;
            hash = hash * 31 + ownerId;
            hash = hash * 31 + planetId;
            hash = hash * 31 + tileIndex;
            hash = hash * 31 + spaceTileIndex;
            hash = hash * 31 + DeterministicStringHash(key);
            return hash == 0 ? 1 : hash;
        }
    }

    private int DeterministicStringHash(string value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        unchecked
        {
            int hash = 23;
            for (int i = 0; i < value.Length; i++) hash = hash * 31 + value[i];
            return hash;
        }
    }
    public TradeNodeRuntime GetNode(int id) { if (id == 0) return null; EnsureRegistry(); return nodesById.TryGetValue(id, out var n) ? n : null; }

    public bool TryCreateRoute(int sourceNodeId, int destinationNodeId, Civilization owner, out TradeRoute route)
    {
        EnsureRegistry();
        route = null;
        if (!HasCivilizationRouteCapacity(owner)) return false;
        route = BuildRoute(sourceNodeId, destinationNodeId, owner, false, true);
        if (route == null || route.suspended) return false;
        route.routeId = nextRouteId++;
        activeRoutes.Add(route);
        return true;
    }
    public TradeRoute PreviewRoute(City s, City d) { EnsureRegistry(); return PreviewRoute(GetCityNodeId(s), GetCityNodeId(d), s != null ? s.owner : null) ?? new TradeRoute(s, d); }
    public TradeRoute PreviewRoute(int sourceNodeId, int destinationNodeId, Civilization owner) { EnsureRegistry(); return BuildRoute(sourceNodeId, destinationNodeId, owner, true, true); }
    public TradeRoute PreviewRoute(Civilization civ, int originPlanet, int destPlanet)
    {
        EnsureRegistry();
        var s = allTradeNodes.FirstOrDefault(n => civ != null && n.ownerCivilizationId == civ.GetRuntimeId() && n.location.planetId == originPlanet && n.isPlanetaryGateway);
        var d = allTradeNodes.FirstOrDefault(n => n.location.planetId == destPlanet && n.isPlanetaryGateway);
        var route = s != null && d != null ? BuildRoute(s.nodeId, d.nodeId, civ, true, true) : null;
        if (route != null) return route;
        route = new TradeRoute(civ, originPlanet, destPlanet) { suspended = true, suspensionReason = TradeSuspensionReason.InvalidPath };
        route.SyncLegacyYieldFields();
        return route;
    }

    private TradeRoute BuildRoute(int sourceId, int destId, Civilization owner, bool preview, bool enforceCapacity)
    {
        if (sourceId == 0 || destId == 0 || sourceId == destId) return null;
        nodesById.TryGetValue(sourceId, out var source); nodesById.TryGetValue(destId, out var dest); if (source == null || dest == null) return null;
        var parent = new Dictionary<int,int>(); var segs = new Dictionary<int,TradeRouteSegment>(); var q = new Queue<int>(); q.Enqueue(sourceId); parent[sourceId] = 0;
        while (q.Count > 0 && !parent.ContainsKey(destId))
        { int cur = q.Dequeue(); if (!nodesById.TryGetValue(cur, out var curNode)) continue; foreach (var next in allTradeNodes) if (!parent.ContainsKey(next.nodeId) && TryBuildSegment(curNode, next, enforceCapacity, out var seg)) { if (next.nodeId != destId && !next.canRelayRoutes) continue; parent[next.nodeId]=cur; segs[next.nodeId]=seg; q.Enqueue(next.nodeId); } }
        var route = new TradeRoute { routeId = preview ? 0 : nextRouteId, ownerCivilizationId = owner != null ? owner.GetRuntimeId() : source.ownerCivilizationId, sourceNodeId = sourceId, destinationNodeId = destId, sourceCity = source.city, destinationCity = dest.city, tradingCivilization = owner, originPlanetIndex = source.location.planetId, destinationPlanetIndex = dest.location.planetId, isInterplanetaryRoute = source.location.planetId != dest.location.planetId };
        if (!parent.ContainsKey(destId)) { route.suspended = true; route.suspensionReason = TradeSuspensionReason.InvalidPath; return route; }
        var chain = new List<int>(); for (int n = destId; n != 0 && n != sourceId; n = parent[n]) chain.Add(n); chain.Reverse();
        foreach (int n in chain) { route.segments.Add(segs[n]); if (n != destId) route.relayNodeIds.Add(n); }
        RecalculateRoute(route); return route;
    }

    private void RevalidateAndReroute(TradeRoute route)
    {
        if (route == null) return;
        var rebuilt = BuildRoute(route.sourceNodeId, route.destinationNodeId, route.tradingCivilization, true, false);
        if (rebuilt == null || rebuilt.suspended)
        {
            route.segments?.Clear();
            route.relayNodeIds?.Clear();
            route.suspended = true;
            route.suspensionReason = rebuilt != null ? rebuilt.suspensionReason : TradeSuspensionReason.InvalidPath;
            if (route.yields == null) route.yields = new TradeYield();
            route.yields.goldPerTurn = 0;
            route.SyncLegacyYieldFields();
            return;
        }

        route.relayNodeIds = rebuilt.relayNodeIds;
        route.segments = rebuilt.segments;
        route.suspended = rebuilt.suspended;
        route.suspensionReason = rebuilt.suspensionReason;
        route.raidChance = rebuilt.raidChance;
        route.yields = rebuilt.yields;
        route.SyncLegacyYieldFields();
    }

    private int GetRangeForDomain(TradeNodeRuntime node, TradeMapDomain domain)
    {
        if (node == null) return 0;
        switch (domain)
        {
            case TradeMapDomain.PlanetSurface: return Mathf.Max(0, node.surfaceTradeRange);
            case TradeMapDomain.PlanetMaritime: return Mathf.Max(0, node.maritimeTradeRange);
            case TradeMapDomain.PlanetAir: return Mathf.Max(0, node.airTradeRange);
            case TradeMapDomain.PlanetOrbit: return Mathf.Max(0, node.orbitTradeRange);
            case TradeMapDomain.SolarSystemSpace: return Mathf.Max(0, node.solarSpaceTradeRange);
            case TradeMapDomain.Interstellar: return Mathf.Max(0, node.interstellarTradeRange);
            default: return Mathf.Max(0, node.tradeRange);
        }
    }

    private bool TryBuildSegment(TradeNodeRuntime a, TradeNodeRuntime b, bool enforceCapacity, out TradeRouteSegment seg)
    {
        seg = null; if (a == null || b == null || a.nodeId == b.nodeId || !a.isOperational || !b.isOperational || !b.canReceiveRoutes) return false; if (!TradePermitted(a, b)) return false; if (!DomainsCompatible(a,b,out var domain)) return false; if (IsGatewayBlockaded(a) || IsGatewayBlockaded(b)) return false;
        int cost; var planetPath = new List<int>(); var spacePath = new List<int>();
        if (domain == TradeMapDomain.SolarSystemSpace || domain == TradeMapDomain.PlanetOrbit || domain == TradeMapDomain.Interstellar) cost = GetSpacePathCost(a,b,spacePath); else if (domain == TradeMapDomain.PlanetMaritime) cost = GetMaritimePathCost(a,b,planetPath); else cost = GetSurfacePathCost(a,b,planetPath);
        int range = GetRangeForDomain(a, domain);
        if (cost == int.MaxValue || cost > range) return false;
        seg = new TradeRouteSegment { fromNodeId = a.nodeId, toNodeId = b.nodeId, domain = domain, pathCost = cost, planetaryTilePath = planetPath, spaceTilePath = spacePath, raidChance = Mathf.Clamp01(TradeManager.CurrentBaseCityTradeRaidChance - a.raidChanceReduction - b.raidChanceReduction), riskType = GetRiskType(domain) };
        return true;
    }

    private bool TradePermitted(TradeNodeRuntime a, TradeNodeRuntime b)
    {
        if (a == null || b == null) return false;
        if (a.ownerCivilizationId == 0 || b.ownerCivilizationId == 0 || a.ownerCivilizationId == b.ownerCivilizationId) return true;

        var civs = CivilizationManager.Instance != null ? CivilizationManager.Instance.GetAllCivs() : null;
        Civilization ownerA = civs != null ? civs.FirstOrDefault(c => c != null && c.GetRuntimeId() == a.ownerCivilizationId) : null;
        Civilization ownerB = civs != null ? civs.FirstOrDefault(c => c != null && c.GetRuntimeId() == b.ownerCivilizationId) : null;
        if (ownerA == null || ownerB == null) return false;

        var state = DiplomacyManager.Instance != null
            ? DiplomacyManager.Instance.GetRelationship(ownerA, ownerB)
            : (ownerA.relations != null && ownerA.relations.TryGetValue(ownerB, out var rel) ? rel : DiplomaticState.Peace);
        return state != DiplomaticState.War;
    }

    private bool DomainsCompatible(TradeNodeRuntime a, TradeNodeRuntime b, out TradeMapDomain domain)
    { domain = TradeMapDomain.PlanetSurface; var common = a.supportedDomains & b.supportedDomains; if (common.HasFlag(TradeDomainMask.PlanetSurface) && a.location.planetId == b.location.planetId) return true; if (common.HasFlag(TradeDomainMask.PlanetMaritime) && a.location.planetId == b.location.planetId) { domain = TradeMapDomain.PlanetMaritime; return true; } if (common.HasFlag(TradeDomainMask.PlanetAir) && a.location.planetId == b.location.planetId) { domain = TradeMapDomain.PlanetAir; return true; } if ((a.isPlanetaryGateway || a.isOrbitalGateway) && (b.isPlanetaryGateway || b.isOrbitalGateway) && (common & (TradeDomainMask.PlanetOrbit | TradeDomainMask.SolarSystemSpace | TradeDomainMask.Interstellar)) != 0) { domain = common.HasFlag(TradeDomainMask.SolarSystemSpace) ? TradeMapDomain.SolarSystemSpace : TradeMapDomain.PlanetOrbit; return true; } return false; }
    private int GetSurfacePathCost(TradeNodeRuntime a, TradeNodeRuntime b, List<int> path)
    {
        var ts = TileSystem.GetForPlanet(a.location.planetId) ?? TileSystem.Instance;
        if (ts == null || a.location.planetId != b.location.planetId) return int.MaxValue;
        return FindPlanetaryTradePath(ts, a.location.planetaryTileIndex, b.location.planetaryTileIndex, false, path);
    }

    private int GetMaritimePathCost(TradeNodeRuntime a, TradeNodeRuntime b, List<int> path)
    {
        var ts = TileSystem.GetForPlanet(a.location.planetId) ?? TileSystem.Instance;
        if (ts == null || a.location.planetId != b.location.planetId) return int.MaxValue;
        return FindPlanetaryTradePath(ts, a.location.planetaryTileIndex, b.location.planetaryTileIndex, true, path);
    }

    private int FindPlanetaryTradePath(TileSystem ts, int start, int goal, bool maritime, List<int> path)
    {
        path.Clear();
        if (start < 0 || goal < 0) return int.MaxValue;

        var open = new List<int> { start };
        var cost = new Dictionary<int, int> { [start] = 0 };
        var previous = new Dictionary<int, int> { [start] = -1 };

        while (open.Count > 0)
        {
            int current = open[0];
            int currentCost = cost[current];
            for (int i = 1; i < open.Count; i++)
            {
                int candidate = open[i];
                if (cost[candidate] < currentCost) { current = candidate; currentCost = cost[candidate]; }
            }
            open.Remove(current);
            if (current == goal) break;

            foreach (int neighbor in ts.GetNeighbors(current))
            {
                var td = ts.GetTileData(neighbor);
                if (!IsPlanetaryTradePassable(td, maritime, neighbor == goal)) continue;
                int stepCost = GetPlanetaryTradeMovementCost(td, maritime);
                int nextCost = currentCost + stepCost;
                if (cost.TryGetValue(neighbor, out int known) && known <= nextCost) continue;
                cost[neighbor] = nextCost;
                previous[neighbor] = current;
                if (!open.Contains(neighbor)) open.Add(neighbor);
            }
        }

        if (!previous.ContainsKey(goal)) return int.MaxValue;
        for (int cur = goal; cur >= 0; cur = previous.TryGetValue(cur, out int prev) ? prev : -1) path.Add(cur);
        path.Reverse();
        return cost.TryGetValue(goal, out int total) ? total : int.MaxValue;
    }

    private bool IsPlanetaryTradePassable(HexTileData td, bool maritime, bool isGoal)
    {
        if (td == null) return false;
        if (maritime) return !td.isLand || td.isLake || td.isRiver || isGoal;
        if (!td.isPassable || td.isMountain) return false;
        return td.isLand || isGoal;
    }

    private int GetPlanetaryTradeMovementCost(HexTileData td, bool maritime)
    {
        if (td == null) return 99;
        if (!maritime && td.improvement != null && td.improvement.isRoad) return 1;
        if (maritime) return td.isRiver ? 1 : 2;
        return Mathf.Max(2, td.movementCost > 0 ? td.movementCost : 2);
    }

    private int GetSpacePathCost(TradeNodeRuntime a, TradeNodeRuntime b, List<int> path)
    {
        var grid = SpaceCombatManager.Instance != null ? SpaceCombatManager.Instance.spaceGrid : null;
        if (grid == null) grid = FindAnyObjectByType<SpaceMapWorldController>()?.Grid;
        if (grid == null) return int.MaxValue;
        int sa = ResolveSpaceTile(a, grid), sb = ResolveSpaceTile(b, grid);
        if (sa < 0 || sb < 0) return int.MaxValue;
        path.AddRange(new SpaceHexPathfinder(grid).FindPath(sa, sb));
        if (path.Count == 0) return int.MaxValue;
        int pathCost = 0;
        for (int i = 1; i < path.Count; i++)
        {
            var tile = grid.GetTile(path[i]);
            pathCost += tile != null ? Mathf.Max(1, tile.movementCost) : 1;
        }
        return pathCost;
    }
    private int ResolveSpaceTile(TradeNodeRuntime n, SpaceHexGrid grid) { if (n.location.spaceTileIndex >= 0) return n.location.spaceTileIndex; var world = FindAnyObjectByType<SpaceMapWorldController>(); return world != null && n.location.planetId >= 0 ? world.GetPlanetAnchorTile(n.location.planetId) : -1; }
    private static TradeSegmentRiskType GetRiskType(TradeMapDomain d) => d == TradeMapDomain.PlanetMaritime ? TradeSegmentRiskType.MaritimePiracy : (d == TradeMapDomain.PlanetAir ? TradeSegmentRiskType.AirDisruption : (d == TradeMapDomain.SolarSystemSpace || d == TradeMapDomain.PlanetOrbit || d == TradeMapDomain.Interstellar ? TradeSegmentRiskType.SpacePiracy : TradeSegmentRiskType.GroundBandits));

    public void RecalculateRoute(TradeRoute route)
    {
        if (route == null) return; if (route.yields == null) route.yields = new TradeYield(); if (route.yields.resourcesPerTurn == null) route.yields.resourcesPerTurn = new List<ResourceCost>(); route.yields.resourcesPerTurn.Clear(); route.suspended = false; route.suspensionReason = TradeSuspensionReason.None; route.raidChance = 0f;
        if (route.segments == null || route.segments.Count == 0) { route.suspended = true; route.suspensionReason = TradeSuspensionReason.InvalidPath; }
        foreach (var seg in route.segments ?? new List<TradeRouteSegment>()) { route.raidChance += seg.raidChance; if (IsGatewayBlockaded(GetNode(seg.fromNodeId)) || IsGatewayBlockaded(GetNode(seg.toNodeId))) { route.suspended = true; route.suspensionReason = TradeSuspensionReason.Blockade; } }
        var dest = GetNode(route.destinationNodeId); var src = GetNode(route.sourceNodeId);
        int baseGold = TradeManager.CurrentBaseCityTradeGold + (dest?.city != null ? Mathf.Max(0, dest.city.level) + Mathf.FloorToInt(Mathf.Max(0, dest.city.GetGoldPerTurn()) / 5f) : 8);
        int distanceBonus = 0;
        float mod = 1f;
        var routeNodeIds = new List<int> { route.sourceNodeId };
        if (route.relayNodeIds != null) routeNodeIds.AddRange(route.relayNodeIds);
        routeNodeIds.Add(route.destinationNodeId);
        foreach (int nodeId in routeNodeIds)
        {
            var node = GetNode(nodeId);
            if (node != null) mod += Mathf.Max(0f, node.routeGoldModifier);
        }
        foreach (var seg in route.segments ?? new List<TradeRouteSegment>()) distanceBonus += Mathf.Max(0, seg.pathCost / 5);
        route.yields.goldPerTurn = route.suspended ? 0 : Mathf.RoundToInt((baseGold + distanceBonus) * mod);
        AddProducedResourceAccess(route.yields.resourcesPerTurn, src?.city);
        AddProducedResourceAccess(route.yields.resourcesPerTurn, dest?.city);
        route.SyncLegacyYieldFields();
    }
    private void AddProducedResourceAccess(List<ResourceCost> resources, City city)
    {
        if (resources == null || city == null) return;
        var exports = city.GetTradeResourceExports();
        if (exports == null) return;
        foreach (var export in exports)
        {
            if (export == null || export.resource == null || export.amount <= 0) continue;
            var existing = resources.Find(r => r != null && r.resource == export.resource);
            if (existing != null) existing.amount = Mathf.Max(existing.amount, export.amount);
            else resources.Add(new ResourceCost { resource = export.resource, amount = export.amount });
        }
    }

    public bool RollRaidForRoute(TradeRoute route) { if (route == null || route.suspended) return false; return UnityEngine.Random.value < Mathf.Clamp01(route.raidChance); }
    public void ProcessCivilizationTradeTurn(Civilization civ)
    {
        RebuildRegistry();
        if (civ == null) return;

        int civId = civ.GetRuntimeId();
        lastProcessedBreakdown.RemoveAll(e => activeRoutes.Any(r => r.routeId == e.routeId && r.ownerCivilizationId == civId));
        foreach (var r in activeRoutes.Where(r => r.ownerCivilizationId == civId))
        {
            RecalculateRoute(r);
            bool raided = !r.suspended && RollRaidForRoute(r);
            r.wasRaidedThisTurn = raided;
            var entry = new TradeTurnBreakdownEntry
            {
                routeId = r.routeId,
                label = GetRouteDisplayName(r),
                suspended = r.suspended || raided,
                wasRaidedThisTurn = raided,
                suspensionReason = raided ? TradeSuspensionReason.Raid : r.suspensionReason,
                yields = raided ? new TradeYield() : r.yields
            };
            lastProcessedBreakdown.Add(entry);
            if (entry.suspended) continue;

            civ.gold += r.yields.goldPerTurn;
            civ.food += r.yields.foodPerTurn;
            civ.policyPoints += r.yields.policyPointsPerTurn;
            civ.faith += r.yields.faithPerTurn;
            foreach (var res in r.yields.resourcesPerTurn) if (res?.resource != null && res.amount > 0) civ.AddResource(res.resource, res.amount);
        }
    }
    public List<TradeTurnBreakdownEntry> GetProcessedBreakdown(Civilization civ) => lastProcessedBreakdown.Where(e => activeRoutes.Any(r => r.routeId == e.routeId && r.ownerCivilizationId == civ.GetRuntimeId())).ToList();
    public IEnumerable<TradeRoute> GetRoutesForCivilization(Civilization civ) => activeRoutes.Where(r => civ != null && r.ownerCivilizationId == civ.GetRuntimeId());

    public int GetCivilizationTradeRouteCapacity(Civilization civ)
    {
        if (civ == null) return 0;
        EnsureRegistry();
        int civId = civ.GetRuntimeId();
        int capacity = 0;
        foreach (var node in allTradeNodes)
        {
            if (node == null || !node.isOperational || node.ownerCivilizationId != civId) continue;
            capacity += Mathf.Max(0, node.civilizationRouteCapacityBonus);
        }
        return capacity;
    }

    public int GetUsedCivilizationTradeRouteCapacity(Civilization civ)
    {
        if (civ == null) return 0;
        int civId = civ.GetRuntimeId();
        return activeRoutes.Count(r => r != null && r.ownerCivilizationId == civId);
    }

    public int GetAvailableCivilizationTradeRouteCapacity(Civilization civ) => Mathf.Max(0, GetCivilizationTradeRouteCapacity(civ) - GetUsedCivilizationTradeRouteCapacity(civ));
    public bool HasCivilizationRouteCapacity(Civilization civ) => GetAvailableCivilizationTradeRouteCapacity(civ) > 0;

    [System.Obsolete("Trade route capacity is civilization-wide. Node capacity is no longer tracked separately.")]
    public int GetUsedNodeCapacity(int nodeId) => 0;
    [System.Obsolete("Trade route capacity is civilization-wide. Use HasCivilizationRouteCapacity instead.")]
    public bool NodeHasCapacity(int nodeId) => true;
    public bool IsGatewayBlockaded(TradeNodeRuntime n) { if (n == null || (!n.isPlanetaryGateway && !n.isOrbitalGateway)) return false; var bm = FindAnyObjectByType<PlanetBlockadeManager>(); var st = bm != null ? bm.blockadeStates.Find(s => s.planetId == n.location.planetId) : null; return st != null && st.isBlockaded; }
    public void NotifyPlanetBlockadeChanged(int planetId) { foreach (var r in activeRoutes) RecalculateRoute(r); }
    public string GetRouteDisplayName(TradeRoute r) { var ids = new List<int>(); ids.Add(r.sourceNodeId); ids.AddRange(r.relayNodeIds); ids.Add(r.destinationNodeId); return string.Join(" → ", ids.Select(id => GetNode(id)?.displayName ?? $"Node {id}")); }
    private static TradeMapDomain MaskToPrimaryDomain(TradeDomainMask m) => m.HasFlag(TradeDomainMask.SolarSystemSpace) ? TradeMapDomain.SolarSystemSpace : (m.HasFlag(TradeDomainMask.PlanetOrbit) ? TradeMapDomain.PlanetOrbit : (m.HasFlag(TradeDomainMask.PlanetAir) ? TradeMapDomain.PlanetAir : TradeMapDomain.PlanetSurface));
}
