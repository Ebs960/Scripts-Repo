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
            registryDirty = false;
        }
        finally
        {
            isRebuildingRegistry = false;
        }

        foreach (var r in activeRoutes.ToArray()) RecalculateRoute(r);
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
            nodeType = cap.nodeType, location = new TradeLocation { domain = MaskToPrimaryDomain(cap.supportedDomains), planetId = improvement.PlanetIndex, planetaryTileIndex = improvement.tileIndex, spaceTileIndex = -1, starSystemId = 0 },
            tradeRange = Mathf.Max(0, cap.tradeRange), routeCapacity = Mathf.Max(0, cap.routeCapacity), canOriginateRoutes = cap.canOriginateRoutes,
            canReceiveRoutes = cap.canReceiveRoutes, canRelayRoutes = cap.canRelayRoutes, isOperational = improvement.gameObject.activeInHierarchy && !improvement.IsFortNeutralized,
            supportedDomains = cap.supportedDomains, routeGoldModifier = cap.routeGoldModifier, raidChanceReduction = cap.raidChanceReduction,
            isPlanetaryGateway = cap.isPlanetaryGateway, isOrbitalGateway = cap.isOrbitalGateway, improvement = improvement,
            displayName = improvement.data.improvementName
        };
        UpsertNode(node); return node;
    }

    private TradeNodeRuntime BuildCityNode(City city)
    {
        var node = new TradeNodeRuntime { nodeId = GetCityNodeId(city), ownerCivilizationId = city.owner != null ? city.owner.GetRuntimeId() : 0, nodeType = TradeNodeType.City, location = new TradeLocation { domain = TradeMapDomain.PlanetSurface, planetId = city.planetIndex, planetaryTileIndex = city.centerTileIndex, spaceTileIndex = -1, starSystemId = 0 }, tradeRange = TradeManager.CurrentMaxCityTradeRange, routeCapacity = 1, canOriginateRoutes = true, canReceiveRoutes = true, canRelayRoutes = false, isOperational = true, supportedDomains = TradeDomainMask.PlanetSurface, city = city, displayName = city.cityName };
        foreach (var tuple in city.EnumerateOperationalBuildings())
        {
            var b = tuple.data; if (b == null) continue;
            ApplyCapability(node, b.tradeNodeCapability);
            if (b.providesHarbor) { node.supportedDomains |= TradeDomainMask.PlanetSurface; node.nodeType = TradeNodeType.Harbor; node.canReceiveRoutes = true; }
            if (b.providesAirport) { node.supportedDomains |= TradeDomainMask.PlanetAir; node.tradeRange = Mathf.Max(node.tradeRange, TradeManager.CurrentMaxAirportTradeRange); }
            if (b.providesSpaceport) { node.supportedDomains |= TradeDomainMask.PlanetOrbit | TradeDomainMask.SolarSystemSpace; node.isPlanetaryGateway = true; node.isOrbitalGateway = true; node.tradeRange = Mathf.Max(node.tradeRange, 3); }
        }
        node.routeCapacity = Mathf.Max(0, node.routeCapacity);
        return node;
    }

    private void ApplyCapability(TradeNodeRuntime node, TradeNodeCapability cap)
    {
        if (cap == null || !cap.providesTradeNode) return;
        node.supportedDomains |= cap.supportedDomains; node.tradeRange += Mathf.Max(0, cap.tradeRange); node.routeCapacity += Mathf.Max(0, cap.routeCapacity);
        node.canOriginateRoutes |= cap.canOriginateRoutes; node.canReceiveRoutes |= cap.canReceiveRoutes; node.canRelayRoutes |= cap.canRelayRoutes;
        node.routeGoldModifier += cap.routeGoldModifier; node.raidChanceReduction += cap.raidChanceReduction; node.isPlanetaryGateway |= cap.isPlanetaryGateway; node.isOrbitalGateway |= cap.isOrbitalGateway;
    }

    private TradeNodeCapability AggregateImprovementCapability(ImprovementInstance imp)
    {
        var cap = Clone(imp.data.tradeNodeCapability);
        foreach (var up in imp.EnumerateAppliedUpgrades()) if (up != null) { cap.supportedDomains |= up.tradeSupportedDomains; cap.tradeRange += up.tradeRangeModifier; cap.routeCapacity += up.tradeRouteCapacityModifier; cap.canRelayRoutes |= up.grantsTradeRelay; cap.routeGoldModifier += up.tradeRouteGoldModifier; cap.raidChanceReduction += up.tradeRaidChanceReduction; }
        return cap;
    }

    private static TradeNodeCapability Clone(TradeNodeCapability c) => c == null ? new TradeNodeCapability() : new TradeNodeCapability { providesTradeNode = c.providesTradeNode, nodeType = c.nodeType, supportedDomains = c.supportedDomains, tradeRange = c.tradeRange, routeCapacity = c.routeCapacity, canOriginateRoutes = c.canOriginateRoutes, canReceiveRoutes = c.canReceiveRoutes, canRelayRoutes = c.canRelayRoutes, routeGoldModifier = c.routeGoldModifier, raidChanceReduction = c.raidChanceReduction, isPlanetaryGateway = c.isPlanetaryGateway, isOrbitalGateway = c.isOrbitalGateway };
    private void UpsertNode(TradeNodeRuntime n) { if (n == null || n.nodeId == 0) return; nodesById[n.nodeId] = n; int i = allTradeNodes.FindIndex(x => x.nodeId == n.nodeId); if (i >= 0) allTradeNodes[i] = n; else allTradeNodes.Add(n); }
    public int GetCityNodeId(City c) => c != null ? c.GetRuntimeId() : 0;
    public int GetImprovementNodeId(ImprovementInstance i) => i != null ? i.GetRuntimeId() : 0;
    public TradeNodeRuntime GetNode(int id) { if (id == 0) return null; EnsureRegistry(); return nodesById.TryGetValue(id, out var n) ? n : null; }

    public bool TryCreateRoute(int sourceNodeId, int destinationNodeId, Civilization owner, out TradeRoute route)
    {
        EnsureRegistry(); route = null; if (!NodeHasCapacity(sourceNodeId)) return false; route = BuildRoute(sourceNodeId, destinationNodeId, owner, false); if (route == null || route.suspended) return false; route.routeId = nextRouteId++; activeRoutes.Add(route); return true;
    }
    public TradeRoute PreviewRoute(City s, City d) { EnsureRegistry(); return PreviewRoute(GetCityNodeId(s), GetCityNodeId(d), s != null ? s.owner : null) ?? new TradeRoute(s, d); }
    public TradeRoute PreviewRoute(int sourceNodeId, int destinationNodeId, Civilization owner) { EnsureRegistry(); return BuildRoute(sourceNodeId, destinationNodeId, owner, true); }
    public TradeRoute PreviewRoute(Civilization civ, int originPlanet, int destPlanet) { EnsureRegistry(); var s = allTradeNodes.FirstOrDefault(n => civ != null && n.ownerCivilizationId == civ.GetRuntimeId() && n.location.planetId == originPlanet && n.isPlanetaryGateway); var d = allTradeNodes.FirstOrDefault(n => n.location.planetId == destPlanet && n.isPlanetaryGateway); return (s != null && d != null ? BuildRoute(s.nodeId, d.nodeId, civ, true) : null) ?? new TradeRoute(civ, originPlanet, destPlanet); }

    private TradeRoute BuildRoute(int sourceId, int destId, Civilization owner, bool preview)
    {
        if (sourceId == 0 || destId == 0 || sourceId == destId) return null;
        nodesById.TryGetValue(sourceId, out var source); nodesById.TryGetValue(destId, out var dest); if (source == null || dest == null) return null;
        var parent = new Dictionary<int,int>(); var segs = new Dictionary<int,TradeRouteSegment>(); var q = new Queue<int>(); q.Enqueue(sourceId); parent[sourceId] = 0;
        while (q.Count > 0 && !parent.ContainsKey(destId))
        { int cur = q.Dequeue(); if (!nodesById.TryGetValue(cur, out var curNode)) continue; foreach (var next in allTradeNodes) if (!parent.ContainsKey(next.nodeId) && TryBuildSegment(curNode, next, out var seg)) { if (next.nodeId != destId && !next.canRelayRoutes) continue; parent[next.nodeId]=cur; segs[next.nodeId]=seg; q.Enqueue(next.nodeId); } }
        var route = new TradeRoute { routeId = preview ? 0 : nextRouteId, ownerCivilizationId = owner != null ? owner.GetRuntimeId() : source.ownerCivilizationId, sourceNodeId = sourceId, destinationNodeId = destId, sourceCity = source.city, destinationCity = dest.city, tradingCivilization = owner, originPlanetIndex = source.location.planetId, destinationPlanetIndex = dest.location.planetId, isInterplanetaryRoute = source.location.planetId != dest.location.planetId };
        if (!parent.ContainsKey(destId)) { route.suspended = true; route.suspensionReason = TradeSuspensionReason.InvalidPath; return route; }
        var chain = new List<int>(); for (int n = destId; n != 0 && n != sourceId; n = parent[n]) chain.Add(n); chain.Reverse();
        foreach (int n in chain) { route.segments.Add(segs[n]); if (n != destId) route.relayNodeIds.Add(n); }
        RecalculateRoute(route); return route;
    }

    private bool TryBuildSegment(TradeNodeRuntime a, TradeNodeRuntime b, out TradeRouteSegment seg)
    {
        seg = null; if (a == null || b == null || a.nodeId == b.nodeId || !a.isOperational || !b.isOperational || !b.canReceiveRoutes) return false; if (!DomainsCompatible(a,b,out var domain)) return false; if (IsGatewayBlockaded(a) || IsGatewayBlockaded(b)) return false;
        int cost; var planetPath = new List<int>(); var spacePath = new List<int>();
        if (domain == TradeMapDomain.SolarSystemSpace || domain == TradeMapDomain.PlanetOrbit || domain == TradeMapDomain.Interstellar) cost = GetSpacePathCost(a,b,spacePath); else cost = GetSurfacePathCost(a,b,planetPath);
        if (cost == int.MaxValue || cost > Mathf.Max(0, a.tradeRange)) return false;
        seg = new TradeRouteSegment { fromNodeId = a.nodeId, toNodeId = b.nodeId, domain = domain, pathCost = cost, planetaryTilePath = planetPath, spaceTilePath = spacePath, raidChance = Mathf.Clamp01(TradeManager.CurrentBaseCityTradeRaidChance - a.raidChanceReduction - b.raidChanceReduction), riskType = GetRiskType(domain) };
        return true;
    }

    private bool DomainsCompatible(TradeNodeRuntime a, TradeNodeRuntime b, out TradeMapDomain domain)
    { domain = TradeMapDomain.PlanetSurface; var common = a.supportedDomains & b.supportedDomains; if (common.HasFlag(TradeDomainMask.PlanetSurface) && a.location.planetId == b.location.planetId) return true; if (common.HasFlag(TradeDomainMask.PlanetAir) && a.location.planetId == b.location.planetId) { domain = TradeMapDomain.PlanetAir; return true; } if ((a.isPlanetaryGateway || a.isOrbitalGateway) && (b.isPlanetaryGateway || b.isOrbitalGateway) && (common & (TradeDomainMask.PlanetOrbit | TradeDomainMask.SolarSystemSpace | TradeDomainMask.Interstellar)) != 0) { domain = common.HasFlag(TradeDomainMask.SolarSystemSpace) ? TradeMapDomain.SolarSystemSpace : TradeMapDomain.PlanetOrbit; return true; } return false; }
    private int GetSurfacePathCost(TradeNodeRuntime a, TradeNodeRuntime b, List<int> path) { var ts = TileSystem.GetForPlanet(a.location.planetId) ?? TileSystem.Instance; if (ts == null || a.location.planetId != b.location.planetId) return int.MaxValue; path.Add(a.location.planetaryTileIndex); path.Add(b.location.planetaryTileIndex); return Mathf.RoundToInt(ts.GetTileDistance(a.location.planetaryTileIndex, b.location.planetaryTileIndex)); }
    private int GetSpacePathCost(TradeNodeRuntime a, TradeNodeRuntime b, List<int> path) { var grid = SpaceCombatManager.Instance != null ? SpaceCombatManager.Instance.spaceGrid : null; if (grid == null) grid = FindAnyObjectByType<SpaceMapWorldController>()?.Grid; if (grid == null) return int.MaxValue; int sa = ResolveSpaceTile(a, grid), sb = ResolveSpaceTile(b, grid); if (sa < 0 || sb < 0) return int.MaxValue; path.AddRange(new SpaceHexPathfinder(grid).FindPath(sa, sb)); return path.Count > 0 ? grid.GetDistance(sa, sb) : int.MaxValue; }
    private int ResolveSpaceTile(TradeNodeRuntime n, SpaceHexGrid grid) { if (n.location.spaceTileIndex >= 0) return n.location.spaceTileIndex; var world = FindAnyObjectByType<SpaceMapWorldController>(); return world != null && n.location.planetId >= 0 ? world.GetPlanetAnchorTile(n.location.planetId) : -1; }
    private static TradeSegmentRiskType GetRiskType(TradeMapDomain d) => d == TradeMapDomain.PlanetAir ? TradeSegmentRiskType.AirDisruption : (d == TradeMapDomain.SolarSystemSpace || d == TradeMapDomain.PlanetOrbit || d == TradeMapDomain.Interstellar ? TradeSegmentRiskType.SpacePiracy : TradeSegmentRiskType.GroundBandits);

    public void RecalculateRoute(TradeRoute route)
    {
        if (route == null) return; if (route.yields == null) route.yields = new TradeYield(); if (route.yields.resourcesPerTurn == null) route.yields.resourcesPerTurn = new List<ResourceCost>(); route.yields.resourcesPerTurn.Clear(); route.suspended = false; route.suspensionReason = TradeSuspensionReason.None; route.raidChance = 0f;
        if (route.segments == null || route.segments.Count == 0) { route.suspended = true; route.suspensionReason = TradeSuspensionReason.InvalidPath; }
        foreach (var seg in route.segments ?? new List<TradeRouteSegment>()) { route.raidChance += seg.raidChance; if (IsGatewayBlockaded(GetNode(seg.fromNodeId)) || IsGatewayBlockaded(GetNode(seg.toNodeId))) { route.suspended = true; route.suspensionReason = TradeSuspensionReason.Blockade; } }
        var dest = GetNode(route.destinationNodeId); var src = GetNode(route.sourceNodeId); int baseGold = TradeManager.CurrentBaseCityTradeGold + (dest?.city != null ? Mathf.Max(0, dest.city.level) + Mathf.FloorToInt(Mathf.Max(0, dest.city.GetGoldPerTurn()) / 5f) : 8); float mod = 1f + Mathf.Max(0f, src?.routeGoldModifier ?? 0f) + Mathf.Max(0f, dest?.routeGoldModifier ?? 0f); route.yields.goldPerTurn = route.suspended ? 0 : Mathf.RoundToInt(baseGold * mod); route.yields.resourcesPerTurn.AddRange(dest?.city != null ? dest.city.GetTradeResourceExports() : new List<ResourceCost>()); route.SyncLegacyYieldFields();
    }
    public bool RollRaidForRoute(TradeRoute route) { if (route == null || route.suspended) return false; return UnityEngine.Random.value < Mathf.Clamp01(route.raidChance); }
    public void ProcessCivilizationTradeTurn(Civilization civ)
    { RebuildRegistry(); if (civ == null) return; int civId = civ.GetRuntimeId(); lastProcessedBreakdown.RemoveAll(e => activeRoutes.Any(r => r.routeId == e.routeId && r.ownerCivilizationId == civId)); foreach (var r in activeRoutes.Where(r => r.ownerCivilizationId == civId)) { RecalculateRoute(r); var entry = new TradeTurnBreakdownEntry { routeId = r.routeId, label = GetRouteDisplayName(r), suspended = r.suspended, suspensionReason = r.suspensionReason, yields = r.yields }; lastProcessedBreakdown.Add(entry); if (r.suspended || RollRaidForRoute(r)) continue; civ.gold += r.yields.goldPerTurn; civ.food += r.yields.foodPerTurn; civ.policyPoints += r.yields.policyPointsPerTurn; civ.faith += r.yields.faithPerTurn; foreach (var res in r.yields.resourcesPerTurn) if (res?.resource != null && res.amount > 0) civ.AddResource(res.resource, res.amount); } }
    public List<TradeTurnBreakdownEntry> GetProcessedBreakdown(Civilization civ) => lastProcessedBreakdown.Where(e => activeRoutes.Any(r => r.routeId == e.routeId && r.ownerCivilizationId == civ.GetRuntimeId())).ToList();
    public IEnumerable<TradeRoute> GetRoutesForCivilization(Civilization civ) => activeRoutes.Where(r => civ != null && r.ownerCivilizationId == civ.GetRuntimeId());
    public int GetUsedOriginCapacity(int nodeId) => activeRoutes.Count(r => r.sourceNodeId == nodeId && !r.suspended);
    public bool NodeHasCapacity(int nodeId) { var n = GetNode(nodeId); return n != null && GetUsedOriginCapacity(nodeId) < n.routeCapacity; }
    public bool IsGatewayBlockaded(TradeNodeRuntime n) { if (n == null || (!n.isPlanetaryGateway && !n.isOrbitalGateway)) return false; var bm = FindAnyObjectByType<PlanetBlockadeManager>(); var st = bm != null ? bm.blockadeStates.Find(s => s.planetId == n.location.planetId) : null; return st != null && st.isBlockaded; }
    public void NotifyPlanetBlockadeChanged(int planetId) { foreach (var r in activeRoutes) RecalculateRoute(r); }
    public string GetRouteDisplayName(TradeRoute r) { var ids = new List<int>(); ids.Add(r.sourceNodeId); ids.AddRange(r.relayNodeIds); ids.Add(r.destinationNodeId); return string.Join(" → ", ids.Select(id => GetNode(id)?.displayName ?? $"Node {id}")); }
    private static TradeMapDomain MaskToPrimaryDomain(TradeDomainMask m) => m.HasFlag(TradeDomainMask.SolarSystemSpace) ? TradeMapDomain.SolarSystemSpace : (m.HasFlag(TradeDomainMask.PlanetOrbit) ? TradeMapDomain.PlanetOrbit : (m.HasFlag(TradeDomainMask.PlanetAir) ? TradeMapDomain.PlanetAir : TradeMapDomain.PlanetSurface));
}
