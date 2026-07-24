// Assets/Scripts/Managers/NaturalDisasterManager.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Central manager for the natural disaster system (earthquakes, floods, storms).
/// Rolls a per-round trigger chance per planet/disaster, picks an eligible tile, and applies
/// damage to units, improvements (forts take HP damage; other improvements become "damaged"
/// and produce no yield until repaired for free by a worker over 1 turn), buildings (which
/// become non-operational until repaired the same way), and a temporary city-wide yield
/// penalty / population loss for any city whose territory was struck.
/// Subscribes to TurnManager.OnRoundStarted for world-level processing each round.
/// </summary>
public class NaturalDisasterManager : MonoBehaviour
{
    public static NaturalDisasterManager Instance { get; private set; }

    [Header("Disaster Database")]
    [Tooltip("All natural disaster types that can occur in the game.")]
    public NaturalDisasterData[] allDisasters;

    [Header("Global Settings")]
    [Tooltip("Global multiplier applied to all disaster trigger chances (difficulty scaling).")]
    public float globalChanceMultiplier = 1f;
    [Tooltip("Minimum round before natural disasters can occur.")]
    public int minimumRoundForDisasters = 5;

    /// <summary>Fired when a disaster triggers: (disaster, planetIndex, primaryTileIndex, affectedTiles).</summary>
    public event System.Action<NaturalDisasterData, int, int, List<int>> OnNaturalDisasterTriggered;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) { Destroy(gameObject); return; }
    }

    void OnEnable()
    {
        if (TurnManager.Instance != null)
            TurnManager.Instance.OnRoundStarted += HandleRoundStarted;
    }

    void OnDisable()
    {
        if (TurnManager.Instance != null)
            TurnManager.Instance.OnRoundStarted -= HandleRoundStarted;
    }

    private void HandleRoundStarted(int round)
    {
        if (round < minimumRoundForDisasters) return;
        if (allDisasters == null || allDisasters.Length == 0) return;
        if (GameManager.Instance == null) return;

        var planetData = GameManager.Instance.GetPlanetData();
        if (planetData == null) return;

        foreach (var kvp in planetData)
        {
            int planetIndex = kvp.Key;
            foreach (var disaster in allDisasters)
            {
                if (disaster != null)
                    TryTriggerDisaster(disaster, planetIndex);
            }
        }
    }

    private void TryTriggerDisaster(NaturalDisasterData disaster, int planetIndex)
    {
        try
        {
            if (disaster.useSeasonFilter && disaster.seasons != null && disaster.seasons.Length > 0)
            {
                Season season = ClimateManager.Instance != null ? ClimateManager.Instance.GetSeasonForPlanet(planetIndex) : Season.Spring;
                if (!disaster.seasons.Contains(season)) return;
            }

            float chance = Mathf.Max(0f, disaster.baseChancePerTurn * globalChanceMultiplier);
            if (chance <= 0f || Random.value > chance) return;

            int tileIndex = PickEligibleTile(disaster, planetIndex);
            if (tileIndex < 0) return;

            var affectedTiles = GetAffectedTiles(tileIndex, disaster.areaRadius, planetIndex);
            ApplyDisasterEffects(disaster, planetIndex, affectedTiles);
            OnNaturalDisasterTriggered?.Invoke(disaster, planetIndex, tileIndex, affectedTiles);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[NaturalDisasterManager] Failed to process disaster '{disaster?.disasterName}': {ex.Message}");
        }
    }

    // ─── Tile Eligibility & Area ────────────────────────────────────────────────

    private int PickEligibleTile(NaturalDisasterData disaster, int planetIndex)
    {
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null || !ts.IsReady()) return -1;
        int tileCount = ts.TileCount;
        if (tileCount <= 0) return -1;

        const int maxAttempts = 40;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            int candidate = Random.Range(0, tileCount);
            var tileData = ts.GetTileData(candidate);
            if (tileData == null) continue;
            if (IsTileEligible(disaster.disasterType, tileData, candidate, ts))
                return candidate;
        }
        return -1;
    }

    private bool IsTileEligible(NaturalDisasterType type, HexTileData tileData, int tileIndex, TileSystem ts)
    {
        switch (type)
        {
            case NaturalDisasterType.Earthquake:
                return tileData.isLand && (tileData.isHill || tileData.isMountain || tileData.biome == Biome.Volcanic);
            case NaturalDisasterType.Flood:
                return tileData.isLand && (tileData.isRiver || tileData.isLake || IsCoastalTile(tileData, tileIndex, ts));
            case NaturalDisasterType.Storm:
                return tileData.IsWaterTile || IsCoastalTile(tileData, tileIndex, ts);
            default:
                return false;
        }
    }

    private bool IsCoastalTile(HexTileData tileData, int tileIndex, TileSystem ts)
    {
        if (tileData == null || !tileData.isLand) return false;
        var neighbors = ts.GetNeighbors(tileIndex);
        if (neighbors == null) return false;
        foreach (var n in neighbors)
        {
            if (n < 0) continue;
            var nd = ts.GetTileData(n);
            if (nd != null && nd.IsWaterTile) return true;
        }
        return false;
    }

    private List<int> GetAffectedTiles(int centerTile, int radius, int planetIndex)
    {
        var result = new List<int> { centerTile };
        if (radius <= 0) return result;

        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null) return result;

        var visited = new HashSet<int> { centerTile };
        var frontier = new List<int> { centerTile };

        for (int ring = 0; ring < radius; ring++)
        {
            var nextFrontier = new List<int>();
            foreach (var tile in frontier)
            {
                var neighbors = ts.GetNeighbors(tile);
                if (neighbors == null) continue;
                foreach (var n in neighbors)
                {
                    if (n < 0 || visited.Contains(n)) continue;
                    visited.Add(n);
                    nextFrontier.Add(n);
                    result.Add(n);
                }
            }
            frontier = nextFrontier;
        }

        return result;
    }

    // ─── Effect Application ─────────────────────────────────────────────────────

    private void ApplyDisasterEffects(NaturalDisasterData disaster, int planetIndex, List<int> affectedTiles)
    {
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null || affectedTiles == null || affectedTiles.Count == 0) return;

        var affectedSet = new HashSet<int>(affectedTiles);
        var struckCities = new HashSet<City>();

        foreach (var tileIndex in affectedTiles)
        {
            var tileData = ts.GetTileData(tileIndex);
            if (tileData == null) continue;

            DamageImprovementOnTile(disaster, tileData, tileIndex, planetIndex);

            if (tileData.controllingCity != null)
                struckCities.Add(tileData.controllingCity);
        }

        DamageUnitsOnTiles(disaster, affectedSet, planetIndex);

        foreach (var city in struckCities)
        {
            DamageBuildingsInCity(disaster, city);
            city?.ApplyNaturalDisasterEffect(disaster);
        }
    }

    private void DamageUnitsOnTiles(NaturalDisasterData disaster, HashSet<int> affectedSet, int planetIndex)
    {
        foreach (var unit in UnitRegistry.GetCombatUnits())
        {
            try
            {
                if (unit == null || !unit.takesWeatherDamage) continue;
                if (!IsUnitOnPlanet(unit, planetIndex)) continue;
                if (!affectedSet.Contains(unit.currentTileIndex)) continue;
                ApplyUnitDisasterDamage(disaster, unit);
            }
            catch (System.Exception ex) { Debug.LogWarning($"[NaturalDisasterManager] Unit damage (combat) failed: {ex.Message}"); }
        }

        foreach (var worker in UnitRegistry.GetWorkerUnits())
        {
            try
            {
                if (worker == null || !worker.takesWeatherDamage) continue;
                if (!IsUnitOnPlanet(worker, planetIndex)) continue;
                if (!affectedSet.Contains(worker.currentTileIndex)) continue;
                ApplyUnitDisasterDamage(disaster, worker);
            }
            catch (System.Exception ex) { Debug.LogWarning($"[NaturalDisasterManager] Unit damage (worker) failed: {ex.Message}"); }
        }
    }

    private void ApplyUnitDisasterDamage(NaturalDisasterData disaster, BaseUnit unit)
    {
        float equipReduction = 0f;
        foreach (var eq in unit.EnumerateEquippedItemsForVision())
        {
            if (eq == null) continue;
            equipReduction += eq.GetDisasterDamageReductionPct(disaster.disasterType);
        }
        equipReduction = Mathf.Clamp01(equipReduction);

        float civReduction = 0f;
        try
        {
            if (unit.owner != null)
                civReduction = 1f - unit.owner.GetAttritionModifierTotals(null, null).GetDamageMultiplier(disaster.disasterType);
        }
        catch { civReduction = 0f; }

        float totalReduction = Mathf.Clamp01(equipReduction + civReduction);
        int damage = Mathf.CeilToInt(disaster.unitDamage * (1f - totalReduction));
        if (damage > 0) unit.ApplyDamage(damage);
    }

    private void DamageImprovementOnTile(NaturalDisasterData disaster, HexTileData tileData, int tileIndex, int planetIndex)
    {
        if (tileData == null || !tileData.HasImprovement) return;

        var instance = tileData.improvementInstanceObject != null ? tileData.improvementInstanceObject.GetComponent<ImprovementInstance>() : null;

        if (instance != null && instance.IsFort)
        {
            if (instance.IsFortNeutralized) return;

            float civDamageReduction = 0f;
            try
            {
                if (instance.owner != null)
                    civDamageReduction = 1f - instance.owner.GetAttritionModifierTotals(null, null).GetDamageMultiplier(disaster.disasterType);
            }
            catch { civDamageReduction = 0f; }

            float selfDamageReduction = instance.GetDisasterDamageReduction(disaster.disasterType);
            float totalReduction = Mathf.Clamp01(civDamageReduction + selfDamageReduction);
            int dmg = Mathf.CeilToInt(disaster.fortDamageAmount * (1f - totalReduction));
            if (dmg > 0)
                ImprovementManager.Instance?.DamageFort(tileIndex, dmg, planetIndex);
            return;
        }

        if (tileData.isDisasterDamaged) return; // already damaged; don't reroll until repaired

        float chanceReduction = instance != null
            ? instance.GetDisasterChanceReduction(disaster.disasterType)
            : (tileData.improvement != null ? tileData.improvement.GetDisasterChanceReductionPct(disaster.disasterType) : 0f);

        var owner = tileData.improvementOwner;
        if (owner != null)
        {
            try { chanceReduction += 1f - owner.GetAttritionModifierTotals(null, null).GetChanceMultiplier(disaster.disasterType); }
            catch { }
        }

        chanceReduction = Mathf.Clamp01(chanceReduction);
        float finalChance = disaster.improvementDamageChance * (1f - chanceReduction);
        if (Random.value < finalChance)
        {
            tileData.isDisasterDamaged = true;
            tileData.lastDisasterType = disaster.disasterType;

            var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
            ts?.SetTileData(tileIndex, tileData);
        }
    }

    private void DamageBuildingsInCity(NaturalDisasterData disaster, City city)
    {
        if (city == null || disaster.buildingDamageChance <= 0f) return;

        float civChanceReduction = 0f;
        try
        {
            if (city.owner != null)
                civChanceReduction = 1f - city.owner.GetAttritionModifierTotals(city, null).GetChanceMultiplier(disaster.disasterType);
        }
        catch { civChanceReduction = 0f; }

        foreach (var (index, data, _, _) in city.EnumerateOperationalBuildingsWithIndex())
        {
            if (data == null) continue;

            float chanceReduction = Mathf.Clamp01(data.GetDisasterChanceReductionPct(disaster.disasterType) + civChanceReduction);
            float finalChance = disaster.buildingDamageChance * (1f - chanceReduction);
            if (Random.value < finalChance)
                city.SetBuildingDisasterDamaged(index, true);
        }
    }

    // Helper method to check if a unit is on a specific planet (mirrors ClimateManager's simplification)
    private bool IsUnitOnPlanet(BaseUnit unit, int planetIndex)
    {
        if (GameManager.Instance == null) return true;
        if (planetIndex == 0) return true;
        return GameManager.Instance.currentPlanetIndex == planetIndex;
    }
}
