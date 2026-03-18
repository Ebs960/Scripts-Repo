using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-turn computed cache built once at the start of each AI turn.
/// Prevents TacticalEvaluator from repeating BFS scans per unit.
/// Contains: frontier tiles, resource hotspots, forage targets, threat summaries,
/// nearest-enemy caches, city-site candidates, exploration percentages.
/// Passed through AIPlanner → OperationalPlanner → TacticalEvaluator.
/// </summary>
public class AIContext
{
    public Civilization Civ { get; private set; }
    public int TurnNumber { get; private set; }

    // ──────────────── Per-planet caches ────────────────

    public readonly Dictionary<int, DangerMap> DangerMaps = new();
    public readonly Dictionary<int, HashSet<int>> FrontierTiles = new();
    public readonly Dictionary<int, List<ResourceHotspot>> ResourceHotspots = new();
    public readonly Dictionary<int, List<ForageTarget>> ForageTargets = new();
    public readonly Dictionary<int, List<CityCandidate>> CitySites = new();
    public readonly Dictionary<int, ThreatSummary> ThreatByPlanet = new();
    public readonly Dictionary<int, NearestEnemy> NearestEnemyByPlanet = new();

    // ──────────────── Cross-planet aggregates ────────────────

    public float TotalMilitaryStrength;
    public float TotalEnemyStrength;
    public int TotalOwnedTiles;
    public bool IsFamine;
    public bool NeedFood;
    public bool HasCities;
    public float ExplorationPercent;  // 0–1

    // Budget (controls scan limits)
    private AiBudget _budget;

    // ──────────────── Structs ────────────────

    public struct ResourceHotspot
    {
        public int TileIndex;
        public int PlanetIndex;
        public ResourceData Resource;
        public float Score;
    }

    public struct ForageTarget
    {
        public int TileIndex;
        public int PlanetIndex;
        public ResourceData Resource;
        public float ForageFood;
        public float Score;
    }

    public struct CityCandidate
    {
        public int TileIndex;
        public int PlanetIndex;
        public float Score;
    }

    public struct ThreatSummary
    {
        public int EnemyCombatUnits;
        public int EnemyWorkers;
        public int EnemyCities;
        public float TotalEnemyAttack;
        public int PredatorAnimals;
    }

    public struct NearestEnemy
    {
        public int TileIndex;
        public int Distance;
    }

    // ──────────────── Build (call once per AI turn) ────────────────

    /// <summary>
    /// Rebuild all caches for this turn. Optional AiBudget controls scan limits.
    /// </summary>
    public void Build(Civilization civ, Dictionary<int, DangerMap> dangerMaps, AiBudget budget = null)
    {
        _budget = budget;
        Civ = civ;
        TurnNumber = GameManager.Instance != null ? GameManager.Instance.currentTurn : 0;

        DangerMaps.Clear();
        foreach (var kv in dangerMaps) DangerMaps[kv.Key] = kv.Value;

        HasCities = civ.cities != null && civ.cities.Count > 0;
        IsFamine = civ.food <= 0;
        NeedFood = !HasCities || civ.food < 20;

        TotalMilitaryStrength = CivilizationManager.Instance != null
            ? CivilizationManager.Instance.ComputeMilitaryStrength(civ) : 0f;

        TotalOwnedTiles = 0;
        if (civ.ownedTilesByPlanet != null)
            foreach (var kv in civ.ownedTilesByPlanet) TotalOwnedTiles += kv.Value.Count;

        var planets = CollectActivePlanets(civ);

        TotalEnemyStrength = 0f;
        FrontierTiles.Clear();
        ResourceHotspots.Clear();
        ForageTargets.Clear();
        CitySites.Clear();
        ThreatByPlanet.Clear();
        NearestEnemyByPlanet.Clear();
        ExplorationPercent = 0f;

        int exploredTotal = 0, fogTotal = 0;
        var allCivs = CivilizationManager.Instance?.GetAllCivs();

        foreach (int pIndex in planets)
        {
            BuildThreatSummary(civ, pIndex, allCivs);
            var (explored, total) = BuildFrontierAndExploration(civ, pIndex);
            exploredTotal += explored;
            fogTotal += total;
            BuildResourceAndForageTargets(civ, pIndex);
            BuildCitySites(civ, pIndex);
            BuildNearestEnemy(civ, pIndex, allCivs);
        }
        ExplorationPercent = fogTotal > 0 ? (float)exploredTotal / fogTotal : 0f;
    }

    // ──────────────── Collect planets ────────────────

    private static HashSet<int> CollectActivePlanets(Civilization civ)
    {
        var planets = new HashSet<int>();
        if (civ.combatUnits != null)
            foreach (var u in civ.combatUnits)
                if (u != null && u.planetIndex >= 0) planets.Add(u.planetIndex);
        if (civ.workerUnits != null)
            foreach (var w in civ.workerUnits)
                if (w != null && w.planetIndex >= 0) planets.Add(w.planetIndex);
        if (civ.cities != null)
            foreach (var c in civ.cities)
                if (c != null && c.planetIndex >= 0) planets.Add(c.planetIndex);
        return planets;
    }

    // ──────────────── Threat summary ────────────────

    private void BuildThreatSummary(Civilization civ, int planetIndex, IReadOnlyList<Civilization> allCivs)
    {
        var summary = new ThreatSummary();
        if (allCivs != null)
        {
            foreach (var other in allCivs)
            {
                if (other == civ) continue;
                if (other.combatUnits != null)
                    foreach (var u in other.combatUnits)
                        if (u != null && u.planetIndex == planetIndex)
                        {
                            summary.EnemyCombatUnits++;
                            summary.TotalEnemyAttack += u.CurrentAttack;
                        }
                if (other.workerUnits != null)
                    foreach (var w in other.workerUnits)
                        if (w != null && w.planetIndex == planetIndex) summary.EnemyWorkers++;
                if (other.cities != null)
                    foreach (var c in other.cities)
                        if (c != null && c.planetIndex == planetIndex) summary.EnemyCities++;
            }
        }
        if (AnimalManager.Instance != null)
            foreach (var a in AnimalManager.Instance.GetActiveAnimals())
                if (a != null && a.data != null && a.planetIndex == planetIndex &&
                    a.data.animalBehavior == AnimalBehaviorType.Predator)
                    summary.PredatorAnimals++;

        TotalEnemyStrength += summary.TotalEnemyAttack;
        ThreatByPlanet[planetIndex] = summary;
    }

    // ──────────────── Frontier tiles + exploration % ────────────────

    private (int explored, int total) BuildFrontierAndExploration(Civilization civ, int planetIndex)
    {
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null || !ts.IsReady()) return (0, 0);
        int civId = UnitVisionManager.GetCivIndex(civ);
        if (civId < 0) return (0, 0);
        byte[] fog = ts.GetFogForCiv(civId);
        if (fog == null) return (0, 0);

        int explored = 0;
        int unseen = 0;
        int dim = 0;
        int visible = 0;
        var frontier = new HashSet<int>();
        for (int i = 0; i < fog.Length; i++)
        {
            if (fog[i] == 0)
            {
                unseen++;
                continue;
            }
            if (fog[i] == 1) dim++;
            else if (fog[i] == 2) visible++;
            explored++;
            int[] neighbors = ts.GetNeighbors(i);
            if (neighbors == null) continue;
            foreach (int n in neighbors)
                if (n >= 0 && n < fog.Length && fog[n] == 0) { frontier.Add(i); break; }
        }
        FrontierTiles[planetIndex] = frontier;
        if (Debug.isDebugBuild)
        {
            string civName = civ != null && civ.civData != null ? civ.civData.civName : "?";
            Debug.Log($"[AIContext] {civName} planet={planetIndex} fogSummary: enableFog={(ts.enableFogOfWar ? "on" : "off")} total={fog.Length} unseen={unseen} dim={dim} visible={visible} explored={explored} frontier={frontier.Count}");
        }
        return (explored, fog.Length);
    }

    // ──────────────── Resource hotspots + forage targets ────────────────
    // Single pass over explored tiles: identifies both high-value resource tiles
    // and immediately forageable resource tiles.

    private void BuildResourceAndForageTargets(Civilization civ, int planetIndex)
    {
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null) return;
        int civId = UnitVisionManager.GetCivIndex(civ);
        if (civId < 0) return;
        byte[] fog = ts.GetFogForCiv(civId);
        if (fog == null) return;

        DangerMaps.TryGetValue(planetIndex, out var dm);
        var safetyDm = dm ?? new DangerMap();
        var rm = ResourceManager.Instance;

        var hotspots = new List<ResourceHotspot>();
        var forages = new List<ForageTarget>();

        for (int i = 0; i < fog.Length; i++)
        {
            if (fog[i] == 0) continue;
            var td = ts.GetTileData(i);
            if (td == null || !td.isLand) continue;
            if (td.owner != null && td.owner != civ) continue;

            if (td.resource != null)
            {
                float score = AIScorer.ScoreResourceTile(civ, td, i, safetyDm);
                if (score > 2f)
                    hotspots.Add(new ResourceHotspot
                    {
                        TileIndex = i, PlanetIndex = planetIndex,
                        Resource = td.resource, Score = score
                    });
            }

            if (rm != null)
            {
                var inst = rm.GetResourceInstanceAtTile(i, planetIndex);
                if (inst != null && inst.data != null && inst.data.canBeForaged && inst.data.forageFood > 0)
                {
                    float fScore = inst.data.forageFood * AIScorer.W_FORAGE_FOOD
                                 + safetyDm.GetDanger(i) * AIScorer.W_DANGER_PENALTY;
                    forages.Add(new ForageTarget
                    {
                        TileIndex = i, PlanetIndex = planetIndex,
                        Resource = inst.data, ForageFood = inst.data.forageFood, Score = fScore
                    });
                }
            }
        }

        hotspots.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (hotspots.Count > 30) hotspots.RemoveRange(30, hotspots.Count - 30);
        ResourceHotspots[planetIndex] = hotspots;

        forages.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (forages.Count > 30) forages.RemoveRange(30, forages.Count - 30);
        ForageTargets[planetIndex] = forages;
    }

    // ──────────────── City site candidates ────────────────
    // Scans explored land tiles for settlement quality. Expensive, so capped.

    private int MaxCitySiteScan => _budget?.CitySiteScanLimit ?? 300;

    private void BuildCitySites(Civilization civ, int planetIndex)
    {
        if (!civ.CanFoundMoreCities()) return;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null) return;
        int civId = UnitVisionManager.GetCivIndex(civ);
        if (civId < 0) return;
        byte[] fog = ts.GetFogForCiv(civId);
        if (fog == null) return;

        DangerMaps.TryGetValue(planetIndex, out var dm);
        var safetyDm = dm ?? new DangerMap();
        var allCivs = CivilizationManager.Instance?.GetAllCivs();

        var candidates = new List<CityCandidate>();
        int scanned = 0;

        for (int i = 0; i < fog.Length && scanned < MaxCitySiteScan; i++)
        {
            if (fog[i] == 0) continue;
            var td = ts.GetTileData(i);
            if (td == null || !td.isLand) continue;
            if (td.owner != null && td.owner != civ) continue;
            scanned++;

            // City spacing check (hard minimum)
            bool tooClose = false;
            if (allCivs != null)
                foreach (var c in allCivs)
                {
                    if (c.cities == null) continue;
                    foreach (var city in c.cities)
                    {
                        if (city == null || city.planetIndex != planetIndex) continue;
                        if (ts.GetTileDistance(i, city.centerTileIndex) < 4) { tooClose = true; break; }
                    }
                    if (tooClose) break;
                }
            if (tooClose) continue;

            float score = ScoreCitySiteLightweight(ts, civ, i, safetyDm, planetIndex);
            if (score > 10f)
                candidates.Add(new CityCandidate { TileIndex = i, PlanetIndex = planetIndex, Score = score });
        }

        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (candidates.Count > 10) candidates.RemoveRange(10, candidates.Count - 10);
        CitySites[planetIndex] = candidates;
    }

    /// <summary>
    /// Lightweight city-site scoring (no worker needed). Evaluates 1-ring yields, rivers, hills.
    /// Full scoring via AIScorer.ScoreSettleCity happens when a settler is actually assigned.
    /// </summary>
    private static float ScoreCitySiteLightweight(TileSystem ts, Civilization civ, int tileIndex, DangerMap dm, int planetIndex)
    {
        float score = 5f;
        int[] neighbors = ts.GetNeighbors(tileIndex);
        if (neighbors == null) return score;

        float food = 0, prod = 0;
        int hills = 0;
        bool river = false;
        var td0 = ts.GetTileData(tileIndex);
        if (td0 != null)
        {
            var y0 = td0.GetTotalYield();
            food += y0.Food; prod += y0.Production;
            if (td0.waterType == TileWaterType.River) river = true;
        }
        foreach (int n in neighbors)
        {
            if (n < 0) continue;
            var ntd = ts.GetTileData(n);
            if (ntd == null) continue;
            if (ntd.isLand)
            {
                var ny = ntd.GetTotalYield();
                food += ny.Food * 0.6f;
                prod += ny.Production * 0.6f;
                if (ntd.isHill) hills++;
            }
            if (ntd.waterType == TileWaterType.River) river = true;
        }

        score += food * 2f + prod * 1.5f;
        score += hills * 1.5f;
        if (river) score += 5f;

        // City spacing sweet spot (prefer 5–8 from nearest own city)
        if (civ.cities != null)
        {
            int closest = int.MaxValue;
            foreach (var city in civ.cities)
            {
                if (city == null || city.planetIndex != planetIndex) continue;
                int d = ts.GetTileDistance(tileIndex, city.centerTileIndex);
                if (d < closest) closest = d;
            }
            if (closest >= 5 && closest <= 8) score += 4f;
            else if (closest > 12) score -= 3f;
        }
        else
        {
            score += 8f; // first city bonus
        }

        score += dm.GetDanger(tileIndex) * -1f;
        return score;
    }

    // ──────────────── Nearest enemy ────────────────

    private void BuildNearestEnemy(Civilization civ, int planetIndex, IReadOnlyList<Civilization> allCivs)
    {
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null || allCivs == null) return;

        int sumTile = 0, count = 0;
        if (civ.combatUnits != null)
            foreach (var u in civ.combatUnits)
                if (u != null && u.planetIndex == planetIndex && u.currentTileIndex >= 0) { sumTile += u.currentTileIndex; count++; }
        if (civ.workerUnits != null)
            foreach (var w in civ.workerUnits)
                if (w != null && w.planetIndex == planetIndex && w.currentTileIndex >= 0) { sumTile += w.currentTileIndex; count++; }
        if (count == 0) return;
        int avgTile = sumTile / count;

        int bestTile = -1, bestDist = int.MaxValue;
        foreach (var other in allCivs)
        {
            if (other == civ || other.combatUnits == null) continue;
            foreach (var e in other.combatUnits)
            {
                if (e == null || e.planetIndex != planetIndex || e.currentTileIndex < 0) continue;
                int d = ts.GetTileDistance(avgTile, e.currentTileIndex);
                if (d < bestDist) { bestDist = d; bestTile = e.currentTileIndex; }
            }
        }
        if (bestTile >= 0)
            NearestEnemyByPlanet[planetIndex] = new NearestEnemy { TileIndex = bestTile, Distance = bestDist };
    }

    // ──────────────── Query helpers ────────────────

    public DangerMap GetDangerMap(int planetIndex)
    {
        DangerMaps.TryGetValue(planetIndex, out var dm);
        return dm;
    }

    public ThreatSummary GetThreats(int planetIndex)
    {
        ThreatByPlanet.TryGetValue(planetIndex, out var t);
        return t;
    }

    public HashSet<int> GetFrontier(int planetIndex)
    {
        FrontierTiles.TryGetValue(planetIndex, out var set);
        return set;
    }

    public List<ResourceHotspot> GetResourceHotspots(int planetIndex)
    {
        ResourceHotspots.TryGetValue(planetIndex, out var list);
        return list;
    }

    public List<ForageTarget> GetForageTargets(int planetIndex)
    {
        ForageTargets.TryGetValue(planetIndex, out var list);
        return list;
    }

    public List<CityCandidate> GetCitySites(int planetIndex)
    {
        CitySites.TryGetValue(planetIndex, out var list);
        return list;
    }

    public NearestEnemy? GetNearestEnemy(int planetIndex)
    {
        if (NearestEnemyByPlanet.TryGetValue(planetIndex, out var ne)) return ne;
        return null;
    }
}
