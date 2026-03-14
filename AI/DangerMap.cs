using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-planet heatmap of how dangerous each tile is based on enemy attack ranges and animal threats.
/// Recalculated at the start of each AI turn so the planner can route units safely.
/// </summary>
public class DangerMap
{
    private readonly Dictionary<int, float> dangerValues = new Dictionary<int, float>(512);
    public int PlanetIndex { get; private set; }

    public float GetDanger(int tileIndex)
    {
        return dangerValues.TryGetValue(tileIndex, out float v) ? v : 0f;
    }

    public bool IsDangerous(int tileIndex, float threshold = 1f)
    {
        return GetDanger(tileIndex) >= threshold;
    }

    /// <summary>
    /// Returns the tile with the least danger from the given set, or -1 if empty.
    /// </summary>
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

    /// <summary>
    /// Regenerate the danger map for a planet from the perspective of a given civilization.
    /// </summary>
    public void Generate(Civilization perspective, int planetIndex)
    {
        dangerValues.Clear();
        PlanetIndex = planetIndex;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null || !ts.IsReady()) return;

        // Enemy combat units
        var allCivs = CivilizationManager.Instance != null ? CivilizationManager.Instance.GetAllCivs() : null;
        if (allCivs != null)
        {
            foreach (var civ in allCivs)
            {
                if (civ == perspective || civ.combatUnits == null) continue;
                foreach (var enemy in civ.combatUnits)
                {
                    if (enemy == null || enemy.currentTileIndex < 0 || enemy.planetIndex != planetIndex) continue;
                    MarkThreatRadius(ts, enemy.currentTileIndex, enemy.CurrentAttack, Mathf.FloorToInt(enemy.CurrentRange), 1f);
                }
                // Workers can also attack (weakly)
                if (civ.workerUnits != null)
                {
                    foreach (var w in civ.workerUnits)
                    {
                        if (w == null || w.currentTileIndex < 0 || w.planetIndex != planetIndex) continue;
                        MarkThreatRadius(ts, w.currentTileIndex, w.CurrentAttack, 1, 0.5f);
                    }
                }
            }
        }

        // Animals (reduced weight — they are threats but not strategic enemies)
        if (AnimalManager.Instance != null)
        {
            foreach (var animal in AnimalManager.Instance.GetActiveAnimals())
            {
                if (animal == null || animal.data == null || animal.currentTileIndex < 0 || animal.planetIndex != planetIndex) continue;
                bool isPredator = animal.data.animalBehavior == AnimalBehaviorType.Predator;
                float weight = isPredator ? 0.7f : 0.15f;
                MarkThreatRadius(ts, animal.currentTileIndex, animal.CurrentAttack, 1, weight);
            }
        }

        // Enemy cities (bombardment radius — mark 2 tiles around each enemy city)
        if (allCivs != null)
        {
            foreach (var civ in allCivs)
            {
                if (civ == perspective || civ.cities == null) continue;
                foreach (var city in civ.cities)
                {
                    if (city == null || city.planetIndex != planetIndex) continue;
                    MarkThreatRadius(ts, city.centerTileIndex, 5, 2, 0.8f);
                }
            }
        }
    }

    private void MarkThreatRadius(TileSystem ts, int centerTile, float attackPower, int range, float weight)
    {
        if (range <= 0)
        {
            AddDanger(centerTile, attackPower * weight);
            return;
        }

        // BFS from center up to range
        var visited = new HashSet<int>();
        var queue = new Queue<(int tile, int dist)>();
        queue.Enqueue((centerTile, 0));
        visited.Add(centerTile);

        while (queue.Count > 0)
        {
            var (tile, dist) = queue.Dequeue();
            // Danger falls off with distance: full at center, halved per step
            float falloff = 1f / (1f + dist);
            AddDanger(tile, attackPower * weight * falloff);

            if (dist >= range) continue;
            int[] neighbors = ts.GetNeighbors(tile);
            if (neighbors == null) continue;
            foreach (int n in neighbors)
            {
                if (n >= 0 && !visited.Contains(n))
                {
                    visited.Add(n);
                    queue.Enqueue((n, dist + 1));
                }
            }
        }
    }

    private void AddDanger(int tile, float amount)
    {
        if (dangerValues.ContainsKey(tile))
            dangerValues[tile] += amount;
        else
            dangerValues[tile] = amount;
    }
}
