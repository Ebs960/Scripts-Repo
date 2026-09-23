using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// The main AI orchestrator. Plans an entire turn's worth of commands for a civilization,
/// then executes them in priority order. This is the single entry point called by CivilizationManager.
///
/// Full turn pipeline:
///  1. Generate DangerMaps for each planet the civ has units on.
///  2. Build AIContext (per-turn cache: frontiers, resource hotspots, threat summaries).
///  3. Update EmpireAI (persistent strategic state → produces EmpireIntent with HTN pillars/objectives).
///  4. Update OperationalPlanner (turns intent + objectives into per-unit role assignments).
///  5. Unstore sheltered units (when not winter).
///  6. Auto-form army groups for coordinated attacks.
///  7. Plan group-level actions (Rally/Advance/Attack/Hold) for army groups.
///  8. For remaining ungrouped units, use TacticalEvaluator (with context + assignment) for best command.
///  9. Apply score noise from AiBudget (difficulty scaling).
///  10. Sort all commands by score (highest first) and execute sequentially.
///
/// AiBudget controls search depth, candidate limits, and score noise per difficulty,
/// ensuring harder AI thinks deeper rather than getting resource bonuses.
/// </summary>
public class AIPlanner
{
    // Persistent danger maps: civ runtime id -> planet index -> DangerMap.
    // Created once per (civ, planet) and reused/updated incrementally across turns
    // instead of being rebuilt from scratch every AI turn (see EnsureDangerMaps).
    private readonly Dictionary<int, Dictionary<int, DangerMap>> dangerMapsByCiv = new Dictionary<int, Dictionary<int, DangerMap>>();

    // View over the current civ's planet->DangerMap dict for this turn (points into dangerMapsByCiv).
    private Dictionary<int, DangerMap> dangerMaps = new Dictionary<int, DangerMap>();
    private static readonly Dictionary<int, DangerMap> EmptyDangerMaps = new Dictionary<int, DangerMap>();

    private readonly ArmyGroupManager armyGroups = new ArmyGroupManager();

    // Empire layer: persistent per-civ strategic state
    private readonly Dictionary<int, EmpireAI> empireStates = new Dictionary<int, EmpireAI>();
    // Operational layer: persistent per-civ role assignments
    private readonly Dictionary<int, OperationalPlanner> opPlanners = new Dictionary<int, OperationalPlanner>();
    // Persistent per-civilization contexts. Static domains are reused across turns;
    // AIContext refreshes volatile threats conservatively.
    private readonly Dictionary<int, AIContext> contextsByCiv = new Dictionary<int, AIContext>();
    private AIContext currentContext;

    // Difficulty-scaling intelligence budget
    private AiBudget budget = AiBudget.ForDifficulty(AIDifficulty.Hard);

    // Planned commands for the current turn
    private readonly List<AICommand> plannedCommands = new List<AICommand>(64);
    private readonly HashSet<int> assignedUnits = new HashSet<int>();

    public IReadOnlyList<AICommand> PlannedCommands => plannedCommands;
    public ArmyGroupManager Groups => armyGroups;
    public AIContext Context => currentContext;
    public AiBudget Budget => budget;

    // Phase timing telemetry (ms per phase, readable by debug overlay)
    public float TimeDangerMap { get; private set; }
    public float TimeContext { get; private set; }
    public float TimeStrategic { get; private set; }
    public float TimeOperational { get; private set; }
    public float TimeTactical { get; private set; }
    public float TimeTotal { get; private set; }

    /// <summary>
    /// Set the intelligence budget (call once at game start or when difficulty changes).
    /// </summary>
    public void SetBudget(AiBudget newBudget) { if (newBudget != null) budget = newBudget; }
    public void SetDifficulty(AIDifficulty difficulty) => budget = AiBudget.ForDifficulty(difficulty);

    /// <summary>
    /// Full AI turn: plan then execute. Called from CivilizationManager.CompleteAITurn.
    /// </summary>
    public void ExecuteTurn(Civilization civ)
    {
        if (civ == null) return;
        PlanTurn(civ);
        ExecuteCommands();
        ExecutePostCommandDecisions(civ);
    }

    /// <summary>
    /// Executes the non-command actors after tactical commands have resolved.  The split turn
    /// coroutine calls this explicitly so planning and command execution can remain on separate
    /// frames without silently omitting Bands or executing tactical commands twice.
    /// </summary>
    public void ExecutePostCommandDecisions(Civilization civ)
    {
        if (civ == null) return;
        ExecuteHerdDecisions(civ);
        ExecuteBandDecisions(civ);
    }

    /// <summary>Cheap, turn-based Herd survival decisions. Tactical combat remains in the campaign engagement flow.</summary>
    internal static void ExecuteHerdDecisions(Civilization civ)
    {
        if (civ?.herds == null) return;
        foreach (var herd in civ.herds.ToArray())
        {
            if (herd == null || herd.currentTileIndex < 0) continue;
            var ts = TileSystem.GetForPlanet(herd.planetIndex) ?? TileSystem.Instance;
            if (ts == null) continue;
            var occupancy = TileOccupancyManager.GetForPlanet(herd.planetIndex) ?? TileOccupancyManager.Instance;
            bool danger = false;
            foreach (int tile in ts.GetNeighbors(herd.currentTileIndex))
            {
                var enemy = occupancy?.GetOccupantObject(tile, TileLayer.Surface)?.GetComponent<CombatUnit>();
                if (enemy != null && enemy.owner != civ) { danger = true; break; }
            }
            int need = herd.FoodRequiredPerTurn;
            int localForage = HerdManager.Instance != null ? HerdManager.Instance.ComputeHerdForageShare(herd.planetIndex, herd.currentTileIndex, herd) : 0;
            bool foodDanger = herd.foodReserve + localForage < need * 2;

            // Survival has priority. Settled camps pack first; packed camps inspect only adjacent tiles.
            if ((foodDanger || danger) && !herd.isPacked) { herd.Pack(); continue; }
            if (herd.isPacked && foodDanger && herd.movementPoints > 0)
            {
                int bestTile = herd.currentTileIndex, bestForage = localForage;
                foreach (int tile in ts.GetNeighbors(herd.currentTileIndex))
                {
                    if (occupancy?.GetOccupantObject(tile, TileLayer.Surface) != null) continue;
                    int forage = HerdManager.Instance != null ? HerdManager.Instance.ComputeHerdForageShare(herd.planetIndex, tile, herd) : Mathf.Max(0, ts.GetTileData(tile)?.food ?? 0);
                    if (forage > bestForage + 1) { bestForage = forage; bestTile = tile; }
                }
                if (bestTile != herd.currentTileIndex) { herd.MoveToTile(bestTile); continue; }
            }
            if (herd.isPacked && !foodDanger && !danger && localForage >= need) { herd.Settle(); continue; }

            if (!herd.isPacked && herd.productionQueue.Count == 0)
            {
                // Prefer explicit Herd survival effects, then output; never infer purpose from an asset's name.
                var choices = (civ.unlockedBuildings ?? new List<BuildingData>()).Where(b => b != null && b.buildableByHerd && b.AreRequirementsMet(civ))
                    .OrderByDescending(b => (b.herdStorageBonus > 0 || b.foodPerTurn > 0 ? 10000 : 0) + b.foodPerTurn * 100 + b.productionPerTurn).ToList();
                foreach (var building in choices) if (herd.QueueProduction(building)) break;
            }

            // Preserve two defenders. An excess force is released only for a nearby strategic threat.
            if (!danger || herd.MilitaryGarrison.Count <= 2) continue;
            herd.FormArmy(herd.MilitaryGarrison.Skip(2).Take(civ.GetMaxArmySize()).ToList(), out _);
        }
    }

    internal void ExecuteBandDecisions(Civilization civ)
    {
        if (civ?.bands == null) return;
        foreach (var band in civ.bands.ToArray())
        {
            if (band == null || band.Data == null) continue;

            var ts = TileSystem.GetForPlanet(band.PlanetIndex) ?? TileSystem.Instance;
            if (ts == null || band.CurrentTileIndex < 0) continue;
            var occupancy = TileOccupancyManager.GetForPlanet(band.PlanetIndex) ?? TileOccupancyManager.Instance;
            var dangerMap = currentContext?.GetDangerMap(band.PlanetIndex);
            int need = band.FoodRequiredPerTurn;
            int turnsOfFood = need > 0 ? band.FoodReserve / need : int.MaxValue;
            bool foodDanger = band.IsStarving || turnsOfFood < 2;
            bool storageHasRoom = band.FoodReserve < band.FoodCapacity;
            bool nearbyThreat = IsBandThreatened(civ, band, ts, occupancy, dangerMap);

            // Foraging is an emergency action, not a reflex: do it only if it can store food and
            // still has the movement required by the authored Band rules.
            if (foodDanger && storageHasRoom && band.Data.baseForageFood > 0 &&
                band.CurrentMovePoints >= band.Data.forageMovementCost && band.Forage() > 0)
            {
                continue;
            }

            if (band.State == BandState.Packed)
            {
                int destination = ChooseBandDestination(civ, band, ts, occupancy, dangerMap, foodDanger, nearbyThreat);
                if (destination >= 0 && TryMoveBandOneStep(band, destination)) continue;

                // Encamp only where the local yields can support the Band, except that a Band
                // with no reachable improvement must stop travelling before it starves.
                if (!nearbyThreat && band.CurrentMovePoints >= band.Data.encampMovementCost &&
                    (IsSustainableBandTile(band, ts, band.CurrentTileIndex) || foodDanger))
                    band.Encamp();
                continue;
            }

            // An encamped first Band is allowed to create the bootstrap city. Prefer a materially
            // better *reachable* known site, but never wait forever for a perfect one.
            if (civ.cities != null && civ.cities.Count == 0 && band.CanFoundSettlement(out _))
            {
                int betterSite = ChooseMateriallyBetterCitySite(band, ts, occupancy, dangerMap);
                if (betterSite < 0 || foodDanger)
                {
                    band.FoundSettlement(out _);
                    continue;
                }
                if (band.CurrentMovePoints >= band.Data.packMovementCost && band.Pack())
                    continue; // movement begins next turn; prevents pack/move/encamp oscillation.
            }

            if ((nearbyThreat || (!IsSustainableBandTile(band, ts, band.CurrentTileIndex) && foodDanger)) &&
                band.CurrentMovePoints >= band.Data.packMovementCost && band.Pack())
                continue;

            if (band.QueuedStructure == null && band.QueuedUnit == null)
            {
                int desiredGarrison = nearbyThreat ? Mathf.Min(3, band.GarrisonCapacity) : Mathf.Min(1, band.GarrisonCapacity);
                if (nearbyThreat && band.Garrison.Count < desiredGarrison && !foodDanger)
                {
                    var defender = band.Data.allowedMilitaryRecruitment
                        .Where(x => x != null && band.CanQueueMilitaryUnit(x, out _))
                        .OrderBy(x => Mathf.Max(1, x.bandProductionCost))
                        .ThenBy(x => x.goldCost)
                        .FirstOrDefault();
                    if (defender != null) { band.QueueMilitaryUnit(defender); continue; }
                }

                var structure = band.Data.allowedStructures
                    .Where(x => x != null && band.CanQueueStructure(x, out _))
                    .OrderByDescending(x => ScoreBandStructure(x, foodDanger, band))
                    .FirstOrDefault();
                if (structure != null) { band.QueueStructure(structure); continue; }

                if (band.Garrison.Count < desiredGarrison && !foodDanger)
                {
                    var unit = band.Data.allowedMilitaryRecruitment
                        .Where(x => x != null && band.CanQueueMilitaryUnit(x, out _))
                        .OrderBy(x => Mathf.Max(1, x.bandProductionCost))
                        .ThenBy(x => x.goldCost)
                        .FirstOrDefault();
                    if (unit != null) band.QueueMilitaryUnit(unit);
                }
            }
        }
    }

    private int ChooseBandDestination(Civilization civ, Band band, TileSystem ts, TileOccupancyManager occupancy,
        DangerMap dangerMap, bool foodDanger, bool threatened)
    {
        // Reuse the bounded city-site cache produced during normal planning. This avoids another
        // world scan and naturally respects actor-tier scan budgets.
        if (!foodDanger && !threatened && civ.cities != null && civ.cities.Count == 0 &&
            currentContext != null && currentContext.CitySites.TryGetValue(band.PlanetIndex, out var sites))
        {
            foreach (var site in sites)
            {
                // CitySites is score ordered. Once the best viable site is reached, stop roaming
                // rather than immediately targeting the runner-up.
                if (site.TileIndex == band.CurrentTileIndex) return -1;
                if (IsAvailableBandTile(site.TileIndex, ts, occupancy) &&
                    UnitMovementController.Instance?.FindPathForBand(band, site.TileIndex)?.Count > 0)
                    return site.TileIndex;
            }
        }

        int best = -1;
        float bestScore = ScoreBandTile(band, ts, band.CurrentTileIndex, dangerMap);
        foreach (int tile in ts.GetNeighbors(band.CurrentTileIndex))
        {
            if (!IsAvailableBandTile(tile, ts, occupancy)) continue;
            float score = ScoreBandTile(band, ts, tile, dangerMap);
            if (foodDanger) score += Mathf.Max(0, ts.GetTileData(tile).food) * 3f;
            if (score > bestScore + (threatened ? 0f : 2f)) { bestScore = score; best = tile; }
        }
        return best;
    }

    private int ChooseMateriallyBetterCitySite(Band band, TileSystem ts, TileOccupancyManager occupancy, DangerMap dangerMap)
    {
        if (currentContext == null || !currentContext.CitySites.TryGetValue(band.PlanetIndex, out var sites)) return -1;
        float current = ScoreBandTile(band, ts, band.CurrentTileIndex, dangerMap);
        foreach (var site in sites)
        {
            if (site.TileIndex == band.CurrentTileIndex || site.Score < current + 8f) continue;
            if (!IsAvailableBandTile(site.TileIndex, ts, occupancy)) continue;
            var path = UnitMovementController.Instance?.FindPathForBand(band, site.TileIndex);
            if (path != null && path.Count > 0 && path.Count <= 6) return site.TileIndex;
        }
        return -1;
    }

    private static bool TryMoveBandOneStep(Band band, int destination)
    {
        var movement = UnitMovementController.Instance;
        var path = movement?.FindPathForBand(band, destination);
        if (path == null || path.Count == 0) return false;
        var ts = TileSystem.GetForPlanet(band.PlanetIndex) ?? TileSystem.Instance;
        var tile = ts?.GetTileData(path[0]);
        int cost = tile != null ? BiomeHelper.GetMovementCost(tile, null) : 99;
        return cost < 99 && band.CurrentMovePoints >= cost && band.TryMove(path[0], cost);
    }

    private static bool IsAvailableBandTile(int tile, TileSystem ts, TileOccupancyManager occupancy)
    {
        var data = ts.GetTileData(tile);
        return data != null && data.isLand && data.isPassable &&
               (occupancy == null || occupancy.GetOccupantObject(tile, TileLayer.Surface) == null);
    }

    private static bool IsSustainableBandTile(Band band, TileSystem ts, int tile)
    {
        var data = ts.GetTileData(tile);
        if (data == null) return false;
        var yields = data.GetTotalYield();
        return yields.Food + band.Data.baseForageFood >= band.FoodRequiredPerTurn;
    }

    private static float ScoreBandTile(Band band, TileSystem ts, int tile, DangerMap dangerMap)
    {
        var data = ts.GetTileData(tile);
        if (data == null || !data.isLand || !data.isPassable) return float.MinValue;
        var yields = data.GetTotalYield();
        float score = yields.Food * 4f + yields.Production * 2f;
        foreach (int neighbor in ts.GetNeighbors(tile))
        {
            var nearby = ts.GetTileData(neighbor);
            if (nearby == null) continue;
            var nearbyYield = nearby.GetTotalYield();
            score += nearbyYield.Food + nearbyYield.Production * .5f;
        }
        if (data.hasMosquitoes && band.Owner?.civData != null && !band.Owner.IsImmuneToMosquitoDamage()) score -= 20f;
        if (dangerMap != null) score -= dangerMap.GetDanger(tile) * 4f;
        return score;
    }

    private static bool IsBandThreatened(Civilization civ, Band band, TileSystem ts,
        TileOccupancyManager occupancy, DangerMap dangerMap)
    {
        if (dangerMap != null && dangerMap.GetDanger(band.CurrentTileIndex) > 0f) return true;
        if (occupancy == null) return false;
        foreach (int tile in ts.GetNeighbors(band.CurrentTileIndex))
        {
            var unit = occupancy.GetOccupantObject(tile, TileLayer.Surface)?.GetComponent<CombatUnit>();
            if (unit != null && unit.owner != civ) return true;
        }
        return false;
    }

    private static float ScoreBandStructure(BandStructureData structure, bool foodDanger, Band band)
    {
        float score = structure.yields.production * 5f + structure.populationGrowthBonus * 4f +
                      structure.foodStorageBonus * 1.5f + structure.forageBonus * 3f +
                      structure.garrisonCapacityBonus;
        if (foodDanger) score += structure.yields.food * 10f + structure.forageBonus * 8f + structure.foodStorageBonus * 2f;
        if (band.Garrison.Count >= band.GarrisonCapacity) score += structure.garrisonCapacityBonus * 5f;
        score -= Mathf.Max(0, structure.productionCost - band.ProductionProgress) * .05f;
        return score;
    }

    // ─────────────────────── Phase 1: Planning ───────────────────────

    public void PlanTurn(Civilization civ)
    {
        var totalSw = Stopwatch.StartNew();
        var phaseSw = new Stopwatch();
        plannedCommands.Clear();
        assignedUnits.Clear();

        // ── Apply procedural persona (modulates AIScorer weights for this civ) ──
        AIScorer.ApplyPersona(civ.leader);

        // ── Phase 1: Danger Maps ──
        // Persistent per (civ, planet); reused and incrementally updated rather than
        // rebuilt from scratch every turn. See EnsureDangerMaps / GetOrCreateDangerMap.
        phaseSw.Restart();
        if (budget.EnableDangerMap)
            EnsureDangerMaps(civ);
        else
            dangerMaps = EmptyDangerMaps;
        TimeDangerMap = phaseSw.ElapsedMilliseconds;

        // ── Phase 2: Context Cache ──
        phaseSw.Restart();
        int civId = civ.GetRuntimeId();
        if (!contextsByCiv.TryGetValue(civId, out currentContext))
        {
            currentContext = new AIContext();
            contextsByCiv[civId] = currentContext;
        }
        currentContext.Build(civ, dangerMaps, budget);
        TimeContext = phaseSw.ElapsedMilliseconds;

        // ── Phase 3: Strategic (EmpireAI) ──
        phaseSw.Restart();
        EmpireIntent intent = null;
        OperationalPlanner opPlanner = null;
        if (budget.EnableStrategicPlanning)
        {
            var empire = GetOrCreateEmpire(civ);
            empire.UpdateForTurn(civ, currentContext);
            intent = empire.Intent;
        }
        TimeStrategic = phaseSw.ElapsedMilliseconds;

        // ── Phase 4: Operational (OperationalPlanner) ──
        phaseSw.Restart();
        if (budget.EnableStrategicPlanning)
        {
            opPlanner = GetOrCreateOpPlanner(civ);
            opPlanner.UpdateAssignments(civ, intent, currentContext);
        }
        PlanUnstores(civ);
        if (budget.EnableArmyGroups)
        {
            armyGroups.Refresh();
            foreach (var kv in dangerMaps)
                armyGroups.AutoFormGroups(civ, kv.Value, kv.Key, budget.ArmyGroupRange);
        }
        if (budget.EnableArmyGroups)
            PlanArmyGroups(civ, intent);
        TimeOperational = phaseSw.ElapsedMilliseconds;

        // ── Phase 5: Tactical (per-unit TacticalEvaluator) ──
        phaseSw.Restart();
        PlanCombatUnits(civ, intent, opPlanner);
        PlanWorkerUnits(civ, intent, opPlanner);
        if (budget.ScoreNoise > 0f)
            ApplyScoreNoise();
        plannedCommands.Sort((a, b) => b.score.CompareTo(a.score));
        TimeTactical = phaseSw.ElapsedMilliseconds;

        // ── Restore default scoring weights ──
        AIScorer.ResetPersona();

        totalSw.Stop();
        TimeTotal = totalSw.ElapsedMilliseconds;

        if (Debug.isDebugBuild)
        {
            string civName = civ.civData != null ? civ.civData.civName : "?";
            string goalStr = intent != null ? intent.Goal.ToString() : "none";
            Debug.Log($"[AIPlanner] {civName}: {plannedCommands.Count} cmds, goal={goalStr}, " +
                      $"timing: danger={TimeDangerMap:F0}ms ctx={TimeContext:F0}ms " +
                      $"strat={TimeStrategic:F0}ms ops={TimeOperational:F0}ms " +
                      $"tact={TimeTactical:F0}ms total={TimeTotal:F0}ms");
        }
    }

    // ─────────────────────── Phase 2: Execution ───────────────────────

    public void ExecuteCommands()
    {
        int executed = 0;
        int skipped = 0;
        foreach (var cmd in plannedCommands)
        {
            if (cmd == null) continue;
            try
            {
                bool canExecute = cmd.CanExecute();
                if (canExecute)
                {
                    cmd.Execute();
                    executed++;
                }
                else
                {
                    skipped++;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[AIPlanner] Command failed for {cmd.unit?.name}: {ex.Message}");
            }
        }
        if (Debug.isDebugBuild)
            Debug.Log($"[AIPlanner] ExecuteCommands summary: planned={plannedCommands.Count} executed={executed} skipped={skipped}");
        plannedCommands.Clear();
        assignedUnits.Clear();
    }

    private static string DescribeCommand(AICommand cmd)
    {
        if (cmd == null) return "<null cmd>";

        string target = "";
        switch (cmd)
        {
            case AIMoveCommand mv:
                target = $" targetTile={mv.targetTileIndex}";
                break;
            case AIExploreCommand ex:
                target = $" targetTile={ex.targetTileIndex}";
                break;
            case AIApproachCommand ap:
                target = $" approachTile={ap.approachTileIndex} targetUnit={ap.target?.name ?? "<null>"}";
                break;
            case AIRetreatCommand rt:
                target = $" retreatTile={rt.retreatTileIndex}";
                break;
            case AIAttackCommand atk:
                target = $" targetUnit={atk.target?.name ?? "<null>"}";
                break;
            case AIBuildImprovementCommand build:
                target = $" improvement={build.improvement?.improvementName ?? "<null>"}";
                break;
            case AIForageCommand forage:
                target = $" resource={forage.resource?.resourceName ?? "<null>"}";
                break;
        }

        return $"{cmd.GetType().Name} unit={cmd.unit?.name ?? "<null>"} score={cmd.score:F2}{target}";
    }

    // ─────────────────────── Per-civ persistent state ───────────────────────

    private EmpireAI GetOrCreateEmpire(Civilization civ)
    {
        int id = civ.GetRuntimeId();
        if (!empireStates.TryGetValue(id, out var e))
        {
            e = new EmpireAI();
            empireStates[id] = e;
        }
        return e;
    }

    private OperationalPlanner GetOrCreateOpPlanner(Civilization civ)
    {
        int id = civ.GetRuntimeId();
        if (!opPlanners.TryGetValue(id, out var p))
        {
            p = new OperationalPlanner();
            opPlanners[id] = p;
        }
        return p;
    }

    // ─────────────────────── Danger map lifecycle ───────────────────────
    //
    // DangerMaps persist per (civ, planet) for the life of the game. The first time a civ
    // needs a map for a planet it is fully generated and subscribed to unit move/kill events;
    // after that, incremental updates from those events keep it current, and AI turns simply
    // reuse the existing object. Full rebuilds only happen for the explicit invalidation cases
    // below (diplomacy shifts, planet regeneration, elimination, save/load, etc.).

    private Dictionary<int, DangerMap> GetOrCreateCivDangerMaps(Civilization civ)
    {
        int civId = civ.GetRuntimeId();
        if (!dangerMapsByCiv.TryGetValue(civId, out var perPlanet))
        {
            perPlanet = new Dictionary<int, DangerMap>();
            dangerMapsByCiv[civId] = perPlanet;
        }
        return perPlanet;
    }

    /// <summary>Returns the persistent DangerMap for (civ, planet), creating and fully generating it if needed.</summary>
    public DangerMap GetOrCreateDangerMap(Civilization civ, int planetIndex)
    {
        if (civ == null) return null;
        var perPlanet = GetOrCreateCivDangerMaps(civ);
        if (!perPlanet.TryGetValue(planetIndex, out var dm))
        {
            dm = new DangerMap();
            dm.Generate(civ, planetIndex);
            perPlanet[planetIndex] = dm;
        }
        return dm;
    }

    /// <summary>Ensures danger maps exist for every planet the civ currently has units on. Reuses existing maps.</summary>
    private void EnsureDangerMaps(Civilization civ)
    {
        var perPlanet = GetOrCreateCivDangerMaps(civ);

        if (civ.combatUnits != null)
            foreach (var u in civ.combatUnits)
                if (u != null && u.planetIndex >= 0 && !perPlanet.ContainsKey(u.planetIndex))
                    GetOrCreateDangerMap(civ, u.planetIndex);
        if (civ.workerUnits != null)
            foreach (var w in civ.workerUnits)
                if (w != null && w.planetIndex >= 0 && !perPlanet.ContainsKey(w.planetIndex))
                    GetOrCreateDangerMap(civ, w.planetIndex);

        dangerMaps = perPlanet;
    }

    /// <summary>Forces a full rebuild of one civ's danger map for one planet, in place (keeps the same object/subscription).</summary>
    public void InvalidateDangerMap(Civilization civ, int planetIndex)
    {
        if (civ == null) return;
        var perPlanet = GetOrCreateCivDangerMaps(civ);
        if (perPlanet.TryGetValue(planetIndex, out var dm))
            dm.Generate(civ, planetIndex);
    }

    /// <summary>Forces a full rebuild of every danger map this civ currently has (e.g. hostility/rules change).</summary>
    public void InvalidateDangerMapsForCivilization(Civilization civ)
    {
        if (civ == null) return;
        if (!dangerMapsByCiv.TryGetValue(civ.GetRuntimeId(), out var perPlanet)) return;
        foreach (var kv in perPlanet)
            kv.Value.Generate(civ, kv.Key);
    }

    /// <summary>Unsubscribes and removes all danger maps for a civilization (permanent elimination).</summary>
    public void RemoveDangerMapsForCivilization(Civilization civ)
    {
        if (civ == null) return;
        int civId = civ.GetRuntimeId();
        if (!dangerMapsByCiv.TryGetValue(civId, out var perPlanet)) return;
        foreach (var dm in perPlanet.Values) dm.Unsubscribe();
        dangerMapsByCiv.Remove(civId);
        if (ReferenceEquals(dangerMaps, perPlanet)) dangerMaps = EmptyDangerMaps;
    }

    /// <summary>Unsubscribes and removes every civ's danger map for a planet (planet unloaded/regenerated).</summary>
    public void RemoveDangerMapsForPlanet(int planetIndex)
    {
        foreach (var perPlanet in dangerMapsByCiv.Values)
        {
            if (perPlanet.TryGetValue(planetIndex, out var dm))
            {
                dm.Unsubscribe();
                perPlanet.Remove(planetIndex);
            }
        }
    }

    /// <summary>Unsubscribes and clears every danger map (AI shutdown / session end).</summary>
    public void DisposeAllDangerMaps()
    {
        foreach (var perPlanet in dangerMapsByCiv.Values)
            foreach (var dm in perPlanet.Values)
                dm.Unsubscribe();
        dangerMapsByCiv.Clear();
        dangerMaps = EmptyDangerMaps;
    }

    /// <summary>Diagnostics: total number of live DangerMap objects across all civs/planets.</summary>
    public int ActiveDangerMapCount
    {
        get
        {
            int count = 0;
            foreach (var perPlanet in dangerMapsByCiv.Values) count += perPlanet.Count;
            return count;
        }
    }

    public DangerMap GetDangerMap(int planetIndex)
    {
        dangerMaps.TryGetValue(planetIndex, out var dm);
        return dm;
    }

    // ─────────────────────── Unstore planning ───────────────────────

    private void PlanUnstores(Civilization civ)
    {
        if (ClimateManager.Instance == null) return;

        void TryUnstore(BaseUnit unit)
        {
            if (unit == null || !unit.isStored || unit.storedInImprovement == null) return;
            if (ClimateManager.Instance.GetSeasonForPlanet(unit.planetIndex) == Season.Winter) return;
            var cmd = new AIUnstoreCommand { unit = unit, planetIndex = unit.planetIndex, score = 100f };
            plannedCommands.Add(cmd);
            assignedUnits.Add(unit.GetRuntimeId());
        }

        if (civ.combatUnits != null)
            foreach (var u in civ.combatUnits) TryUnstore(u);
        if (civ.workerUnits != null)
            foreach (var w in civ.workerUnits) TryUnstore(w);
    }

    // ─────────────────────── Group-level planning ───────────────────────
    // Instead of each grouped unit independently deciding, the group decides ONE action
    // and derives per-unit commands. This gives coherent coordinated behavior.

    private void PlanArmyGroups(Civilization civ, EmpireIntent intent)
    {
        foreach (var group in armyGroups.Groups)
        {
            if (group.Count == 0) continue;
            var dm = GetDangerMap(group.PlanetIndex);
            if (dm == null) dm = new DangerMap();

            GroupAction action = group.DecideAction(dm);
            var commands = group.ExpandToCommands(action, civ, dm);

            foreach (var cmd in commands)
            {
                plannedCommands.Add(cmd);
                if (cmd.unit != null) assignedUnits.Add(cmd.unit.GetRuntimeId());
            }
        }
    }

    // ─────────────────────── Combat unit planning ───────────────────────

    private void PlanCombatUnits(Civilization civ, EmpireIntent intent, OperationalPlanner opPlanner)
    {
        if (civ.combatUnits == null) return;
        foreach (var unit in civ.combatUnits)
        {
            if (unit == null || unit.hasActedThisTurn || unit.IsInOrbit) continue;
            if (assignedUnits.Contains(unit.GetRuntimeId())) continue;
            if (unit.isStored) continue;
            if (unit.planetIndex < 0) continue;

            var dm = GetDangerMap(unit.planetIndex);
            if (dm == null) dm = new DangerMap();

            var assignment = opPlanner?.GetAssignment(unit);
            var cmd = TacticalEvaluator.DecideBestAction(unit, civ, dm, armyGroups, currentContext, assignment, intent, budget);
            if (cmd != null)
            {
                plannedCommands.Add(cmd);
                assignedUnits.Add(unit.GetRuntimeId());
            }
        }
    }

    // ─────────────────────── Worker unit planning ───────────────────────

    private void PlanWorkerUnits(Civilization civ, EmpireIntent intent, OperationalPlanner opPlanner)
    {
        if (civ.workerUnits == null) return;
        foreach (var worker in civ.workerUnits)
        {
            if (worker == null || worker.currentTileIndex < 0) continue;
            if (assignedUnits.Contains(worker.GetRuntimeId())) continue;
            if (worker.isStored) continue;
            if (worker.planetIndex < 0) continue;

            var dm = GetDangerMap(worker.planetIndex);
            if (dm == null) dm = new DangerMap();

            var assignment = opPlanner?.GetAssignment(worker);
            var cmd = TacticalEvaluator.DecideBestAction(worker, civ, dm, armyGroups, currentContext, assignment, intent, budget);
            if (cmd != null)
            {
                plannedCommands.Add(cmd);
                assignedUnits.Add(worker.GetRuntimeId());
            }
        }
    }

    // ─────────────────────── Score noise (difficulty scaling) ───────────────────────

    private void ApplyScoreNoise()
    {
        float noise = budget.ScoreNoise;
        foreach (var cmd in plannedCommands)
            cmd.score += Random.Range(-noise, noise);
    }
}
