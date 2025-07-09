using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class AnimalManager : MonoBehaviour
{
    public static AnimalManager Instance { get; private set; }

    [System.Serializable]
    public class AnimalSpawnRule
    {
        public CombatUnitData unitData;
        public int initialCount;
        public int spawnRate;
        public int maxCount;
        public Biome[] allowedBiomes;
    }

    [Header("Configure each animal type here")]
    public AnimalSpawnRule[] spawnRules;

    private readonly List<CombatUnit> activeAnimals = new List<CombatUnit>();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    public void SpawnInitialAnimals()
    {
        int prevalence = GameManager.Instance != null ? GameManager.Instance.animalPrevalence : 3;
        float[] multipliers = { 0f, 0.25f, 0.5f, 1f, 2f, 3f };
        float mult = multipliers[Mathf.Clamp(prevalence, 0, multipliers.Length - 1)];
        if (mult == 0f) return;

        foreach (var rule in spawnRules)
        {
            int count = Mathf.CeilToInt(rule.initialCount * mult);
            if (count < 1 && mult > 0f) count = 1;
            for (int i = 0; i < count; i++)
                TrySpawn(rule);
        }
    }

    public void ProcessTurn()
    {
        SpawnNewAnimals();
        MoveAllAnimals();
    }

    void SpawnNewAnimals()
    {
        int prevalence = GameManager.Instance != null ? GameManager.Instance.animalPrevalence : 3;
        float[] multipliers = { 0f, 0.25f, 0.5f, 1f, 2f, 3f };
        float mult = multipliers[Mathf.Clamp(prevalence, 0, multipliers.Length - 1)];
        if (mult == 0f) return;

        foreach (var rule in spawnRules)
        {
            int already = activeAnimals.Count(u => u != null && u.data == rule.unitData);
            int maxCount = Mathf.CeilToInt(rule.maxCount * mult);
            if (maxCount < 1 && mult > 0f) maxCount = 1;
            int spawnRate = Mathf.CeilToInt(rule.spawnRate * mult);
            if (spawnRate < 1 && mult > 0f) spawnRate = 1;
            int toSpawn = Mathf.Min(spawnRate, maxCount - already);

            for (int i = 0; i < toSpawn; i++)
                TrySpawn(rule);
        }
    }

    void MoveAllAnimals()
    {
        foreach (var unit in activeAnimals.ToList())
        {
            if (unit == null)
            {
                activeAnimals.Remove(unit);
                continue;
            }

            unit.ResetForNewTurn();

            var (tileData, _) = TileDataHelper.Instance.GetTileData(unit.currentTileIndex);
            if (tileData == null) continue;

            var neighborIndices = TileDataHelper.Instance.GetTileNeighbors(unit.currentTileIndex);
            var validDestinations = neighborIndices
                .Where(index =>
                {
                    var (neighbor, _) = TileDataHelper.Instance.GetTileData(index);
                    return neighbor != null && unit.CanMoveTo(index);
                })
                .ToList();

            if (validDestinations.Count > 0)
            {
                int targetTile = validDestinations[Random.Range(0, validDestinations.Count)];
                unit.MoveTo(targetTile);
            }
        }
    }

    void TrySpawn(AnimalSpawnRule rule)
    {
        var candidates = new List<int>();
        var planet = GameManager.Instance?.planetGenerator ?? FindAnyObjectByType<PlanetGenerator>();
        int tileCount = planet != null && planet.Grid != null ? planet.Grid.TileCount : 0;

        for (int i = 0; i < tileCount; i++)
        {
            var (tile, _) = TileDataHelper.Instance.GetTileData(i);
            if (tile == null || !tile.isLand) continue;

            if (rule.allowedBiomes != null && rule.allowedBiomes.Length > 0)
            {
                if (!rule.allowedBiomes.Contains(tile.biome)) continue;
            }

            candidates.Add(i);
        }

        if (candidates.Count == 0) return;

        int chosenIndex = candidates[Random.Range(0, candidates.Count)];
        Vector3 pos = TileDataHelper.Instance.GetTileCenter(chosenIndex);

        var go = Instantiate(rule.unitData.prefab, pos, Quaternion.identity);
        var unit = go.GetComponent<CombatUnit>();
        unit.Initialize(rule.unitData, null);
        unit.currentTileIndex = chosenIndex;

        activeAnimals.Add(unit);
        unit.OnDeath += () => activeAnimals.Remove(unit);
    }
}
