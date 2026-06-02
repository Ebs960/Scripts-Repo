using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-planet heatmap of how dangerous each tile is based on enemy attack ranges and animal threats.
///
/// Supports two modes:
///   1. Full rebuild — Generate() clears and recomputes everything (used first turn or after many changes).
///   2. Incremental — subscribes to GameEventManager for unit moved/killed events and
///      only updates affected tiles. Much cheaper when few units moved since last rebuild.
///
/// The map tracks per-threat contributions so individual threats can be removed and re-added
/// without rebuilding the entire map.
/// </summary>
public class DangerMap
{
    private readonly Dictionary<int, float> dangerValues = new Dictionary<int, float>(512);
    public int PlanetIndex { get; private set; }

    // Per-threat contribution tracking for incremental updates
    // threatKey → list of (tileIndex, dangerContribution) pairs
    private readonly Dictionary<int, List<(int tile, float amount)>> threatContributions = new(64);
    private Civilization perspective;
    private int lastGenerateTurn = -1;
    private bool isSubscribed;

    // Reusable BFS structures (avoid GC pressure)
    private readonly HashSet<int> bfsVisited = new(64);
    private readonly Queue<(int tile, int dist)> bfsQueue = new(64);

    // ──────────────── Public API ────────────────

    public float GetDanger(int tileIndex)
    {
        return dangerValues.TryGetValue(tileIndex, out float v) ? v : 0f;
    }

    public bool IsDangerous(int tileIndex, float threshold = 1f)
    {
        return GetDanger(tileIndex) >= threshold;
    }

    public int GetSafestTile(IEnumerable<int> tiles)
    {
        int best = -1;
        float bestDanger = float.MaxValue;
        foreach (int t in tiles)
        {
            float d = GetDanger(t);
            if (d < bestDanger) { bestDanger = d; best = t; }
        }
        return best;
    }

    // ──────────────── Full rebuild ────────────────

    public void Generate(Civilization perspective, int planetIndex)
    {
        dangerValues.Clear();
        threatContributions.Clear();
        this.perspective = perspective;
        PlanetIndex = planetIndex;
        lastGenerateTurn = GameManager.Instance != null ? GameManager.Instance.currentTurn : 0;

        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null || !ts.IsReady()) return;

        var allCivs = CivilizationManager.Instance != null ? CivilizationManager.Instance.GetAllCivs() : null;
        if (allCivs != null)
        {
            foreach (var civ in allCivs)
            {
                if (civ == perspective || civ.combatUnits == null) continue;
                foreach (var enemy in civ.combatUnits)
                {
                    if (enemy == null || enemy.currentTileIndex < 0 || enemy.planetIndex != planetIndex) continue;
                    int key = enemy.GetRuntimeId();
                    MarkThreatRadiusTracked(ts, key, enemy.currentTileIndex, enemy.CurrentAttack,
                        Mathf.FloorToInt(enemy.CurrentRange), 1f);
                }
                if (civ.workerUnits != null)
                    foreach (var w in civ.workerUnits)
                    {
                        if (w == null || w.currentTileIndex < 0 || w.planetIndex != planetIndex) continue;
                        MarkThreatRadiusTracked(ts, w.GetRuntimeId(), w.currentTileIndex, w.CurrentAttack, 1, 0.5f);
                    }
            }
        }

        if (AnimalManager.Instance != null)
        {
            foreach (var animal in AnimalManager.Instance.GetActiveAnimals())
            {
                if (animal == null || animal.data == null || animal.currentTileIndex < 0 || animal.planetIndex != planetIndex) continue;
                float weight = animal.data.animalBehavior == AnimalBehaviorType.Predator ? 0.7f : 0.15f;
                MarkThreatRadiusTracked(ts, animal.GetRuntimeId(), animal.currentTileIndex, animal.CurrentAttack, 1, weight);
            }
        }

        if (allCivs != null)
        {
            foreach (var civ in allCivs)
            {
                if (civ == perspective || civ.cities == null) continue;
                foreach (var city in civ.cities)
                {
                    if (city == null || city.planetIndex != planetIndex) continue;
                    MarkThreatRadiusTracked(ts, city.GetRuntimeId(), city.centerTileIndex, 5, 2, 0.8f);
                }
            }
        }

        SubscribeToEvents();
    }

    // ──────────────── Incremental updates ────────────────

    /// <summary>
    /// Remove a specific threat's contribution (call when unit moves or dies).
    /// </summary>
    public void RemoveThreat(int threatKey)
    {
        if (!threatContributions.TryGetValue(threatKey, out var contributions)) return;
        foreach (var (tile, amount) in contributions)
        {
            if (dangerValues.TryGetValue(tile, out float current))
            {
                float newVal = current - amount;
                if (newVal <= 0.001f) dangerValues.Remove(tile);
                else dangerValues[tile] = newVal;
            }
        }
        threatContributions.Remove(threatKey);
    }

    /// <summary>
    /// Add/update a threat at a new position (call after unit moves).
    /// </summary>
    public void UpdateThreat(int threatKey, int centerTile, float attackPower, int range, float weight)
    {
        RemoveThreat(threatKey);
        var ts = TileSystem.GetForPlanet(PlanetIndex) ?? TileSystem.Instance;
        if (ts == null) return;
        MarkThreatRadiusTracked(ts, threatKey, centerTile, attackPower, range, weight);
    }

    // ──────────────── Event handlers ────────────────

    private void SubscribeToEvents()
    {
        if (isSubscribed || GameEventManager.Instance == null) return;
        GameEventManager.Instance.OnUnitMoved += OnUnitMoved;
        GameEventManager.Instance.OnUnitKilled += OnUnitKilled;
        isSubscribed = true;
    }

    public void Unsubscribe()
    {
        if (!isSubscribed || GameEventManager.Instance == null) return;
        GameEventManager.Instance.OnUnitMoved -= OnUnitMoved;
        GameEventManager.Instance.OnUnitKilled -= OnUnitKilled;
        isSubscribed = false;
    }

    private void OnUnitMoved(GameEventManager.UnitMovementEventArgs args)
    {
        var unit = args.Unit as BaseUnit;
        if (unit == null || unit.planetIndex != PlanetIndex) return;
        // Only care about enemies (not our own units)
        var owner = GetOwner(unit);
        if (owner == perspective) return;

        int key = unit.GetRuntimeId();
        float weight = 1f;
        int range = 1;
        if (unit is CombatUnit cu)
        {
            range = Mathf.FloorToInt(cu.CurrentRange);
            if (cu.data != null && cu.data.unitType == CombatCategory.Animal)
                weight = cu.data.animalBehavior == AnimalBehaviorType.Predator ? 0.7f : 0.15f;
        }
        else if (unit is WorkerUnit) { weight = 0.5f; }

        UpdateThreat(key, args.ToTileIndex, unit.CurrentAttack, range, weight);
    }

    private void OnUnitKilled(GameEventManager.CombatEventArgs args)
    {
        var defender = args.Defender as BaseUnit;
        if (defender == null || defender.planetIndex != PlanetIndex) return;
        if (!args.IsLethal) return;
        RemoveThreat(defender.GetRuntimeId());
    }

    // ──────────────── Tracked BFS ────────────────

    private void MarkThreatRadiusTracked(TileSystem ts, int threatKey, int centerTile, float attackPower, int range, float weight)
    {
        var contributions = new List<(int, float)>(range <= 0 ? 1 : 7 * range);

        if (range <= 0)
        {
            float amt = attackPower * weight;
            AddDanger(centerTile, amt);
            contributions.Add((centerTile, amt));
            threatContributions[threatKey] = contributions;
            return;
        }

        bfsVisited.Clear();
        bfsQueue.Clear();
        bfsQueue.Enqueue((centerTile, 0));
        bfsVisited.Add(centerTile);

        while (bfsQueue.Count > 0)
        {
            var (tile, dist) = bfsQueue.Dequeue();
            float falloff = 1f / (1f + dist);
            float amt = attackPower * weight * falloff;
            AddDanger(tile, amt);
            contributions.Add((tile, amt));

            if (dist >= range) continue;
            int[] neighbors = ts.GetNeighbors(tile);
            if (neighbors == null) continue;
            foreach (int n in neighbors)
            {
                if (n >= 0 && !bfsVisited.Contains(n))
                {
                    bfsVisited.Add(n);
                    bfsQueue.Enqueue((n, dist + 1));
                }
            }
        }

        threatContributions[threatKey] = contributions;
    }

    private void AddDanger(int tile, float amount)
    {
        if (dangerValues.ContainsKey(tile))
            dangerValues[tile] += amount;
        else
            dangerValues[tile] = amount;
    }

    private static Civilization GetOwner(BaseUnit unit)
    {
        if (unit is CombatUnit cu) return cu.owner;
        if (unit is WorkerUnit wu) return wu.owner;
        return null;
    }
}
