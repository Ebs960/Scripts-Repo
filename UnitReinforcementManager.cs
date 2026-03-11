using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages unit reinforcement - units recover soldier count over time
/// - 10% per turn when on owned (occupied) territory
/// - 15% per turn when in/inside a shelter improvement
/// - 33% per turn when garrisoned/stored in cities
/// </summary>
public class UnitReinforcementManager : MonoBehaviour
{
    public static UnitReinforcementManager Instance { get; private set; }
    
    [Header("Reinforcement Rates")]
    [Tooltip("Reinforcement rate per turn for units on owned/occupied territory (as percentage)")]
    [Range(0f, 100f)]
    public float reinforcementRateOccupiedTerritory = 10f; // 10% per turn

    [Tooltip("Reinforcement rate per turn for units in/inside shelter improvements (as percentage)")]
    [Range(0f, 100f)]
    public float reinforcementRateInShelter = 15f; // 15% per turn

    [Tooltip("Reinforcement rate per turn for units garrisoned in cities (as percentage)")]
    [Range(0f, 100f)]
    public float reinforcementRateInCity = 33f; // 33% per turn
    
    // Cached FindObjectsByType results to avoid expensive scene searches
    private static Civilization[] cachedAllCivs;
    private static float lastCivCacheUpdate = 0f;
    private const float CIV_CACHE_UPDATE_INTERVAL = 1f; // Update cache every 1 second
    
    private static City[] cachedAllCities;
    private static float lastCityCacheUpdate = 0f;
    private const float CITY_CACHE_UPDATE_INTERVAL = 1f; // Update cache every 1 second
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    void OnEnable()
    {
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.OnTurnChanged += HandleTurnChanged;
        }
    }

    void OnDisable()
    {
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.OnTurnChanged -= HandleTurnChanged;
        }
    }

    private void HandleTurnChanged(Civilization civ, int round)
    {
        // Update garrison status and apply reinforcement at the start of each turn
        UpdateGarrisonStatus();
        ApplyReinforcementToAllUnits();
    }
    
    /// <summary>
    /// Apply reinforcement to all units at the start of a turn
    /// Called by GameManager or TurnManager
    /// </summary>
    public void ApplyReinforcementToAllUnits()
    {
        // Use cached civilizations array to avoid expensive FindObjectsByType call
        if (Time.time - lastCivCacheUpdate > CIV_CACHE_UPDATE_INTERVAL)
        {
            cachedAllCivs = FindObjectsByType<Civilization>(FindObjectsSortMode.None);
            lastCivCacheUpdate = Time.time;
        }
        
        var allCivs = cachedAllCivs;
        
        foreach (var civ in allCivs)
        {
            if (civ == null) continue;

            if (civ.combatUnits != null)
            {
                foreach (var unit in civ.combatUnits)
                {
                    if (unit == null || unit.data == null) continue;
                    ApplyReinforcement(unit);
                }
            }

            if (civ.workerUnits != null)
            {
                foreach (var w in civ.workerUnits)
                {
                    if (w == null) continue;
                    // WorkerUnit inherits BaseUnit and has Heal(int)
                    var bu = w as BaseUnit;
                    if (bu == null) continue;
                    float reinforcementRate = 0f;
                    if (bu is CombatUnit cu2 && cu2.isGarrisonedInCity)
                        reinforcementRate = reinforcementRateInCity;
                    else if (bu.isStored && bu.storedInImprovement != null)
                        reinforcementRate = reinforcementRateInShelter;
                    else if (bu.currentTileIndex >= 0)
                    {
                        var ts2 = TileSystem.GetForPlanet(bu.planetIndex) ?? TileSystem.Instance;
                        if (ts2 != null && ts2.IsReady())
                        {
                            var td2 = ts2.GetTileData(bu.currentTileIndex);
                            if (td2 != null)
                            {
                                if (td2.improvement != null && td2.improvement.isShelter)
                                    reinforcementRate = reinforcementRateInShelter;
                                else if (td2.owner != null && td2.owner == bu.owner)
                                    reinforcementRate = reinforcementRateOccupiedTerritory;
                            }
                        }
                    }

                    if (reinforcementRate > 0f)
                    {
                        int healAmount = Mathf.RoundToInt(bu.MaxHealth * (reinforcementRate / 100f));
                        int oldHealth = bu.currentHealth;
                        bu.Heal(healAmount);
                    }
                }
            }
        }
}
    
    /// <summary>
    /// Apply reinforcement to a single unit
    /// </summary>
    public void ApplyReinforcement(CombatUnit unit)
    {
        if (unit == null || unit.data == null) return;
        if (unit.currentHealth >= unit.MaxHealth) return; // Already at max
        
        // Determine reinforcement rate based on location
        float reinforcementRate = 0f;

        if (unit.isGarrisonedInCity)
        {
            reinforcementRate = reinforcementRateInCity;
        }
        else if (unit.isStored && unit.storedInImprovement != null)
        {
            // Stored inside a shelter improvement
            reinforcementRate = reinforcementRateInShelter;
        }
        else if (unit.currentTileIndex >= 0)
        {
            var ts = TileSystem.GetForPlanet(unit.planetIndex) ?? TileSystem.Instance;
            if (ts != null && ts.IsReady())
            {
                var td = ts.GetTileData(unit.currentTileIndex);
                if (td != null)
                {
                    if (td.improvement != null && td.improvement.isShelter)
                    {
                        reinforcementRate = reinforcementRateInShelter;
                    }
                    else if (td.owner != null && td.owner == unit.owner)
                    {
                        reinforcementRate = reinforcementRateOccupiedTerritory;
                    }
                }
            }
        }
        
        // Calculate healing amount (percentage of max HP)
        int healAmount = Mathf.RoundToInt(unit.MaxHealth * (reinforcementRate / 100f));
        
        // Apply healing
        int oldHealth = unit.currentHealth;
        unit.Heal(healAmount);
        
        if (unit.currentHealth > oldHealth)
        {
}
    }
    
    /// <summary>
    /// Update garrison status for units based on their tile location
    /// Should be called when units move or at start of turn
    /// </summary>
    public void UpdateGarrisonStatus()
    {
        // Use cached civilizations array to avoid expensive FindObjectsByType call
        if (Time.time - lastCivCacheUpdate > CIV_CACHE_UPDATE_INTERVAL)
        {
            cachedAllCivs = FindObjectsByType<Civilization>(FindObjectsSortMode.None);
            lastCivCacheUpdate = Time.time;
        }
        
        var allCivs = cachedAllCivs;
        
        foreach (var civ in allCivs)
        {
            if (civ == null || civ.combatUnits == null) continue;
            
            foreach (var unit in civ.combatUnits)
            {
                if (unit == null || unit.currentTileIndex < 0) continue;
                
                // Check if unit is in a city
                bool isInCity = IsUnitInCity(unit);
                unit.isGarrisonedInCity = isInCity;
            }
        }
    }
    
    /// <summary>
    /// Check if a unit is currently in a city
    /// </summary>
    private bool IsUnitInCity(CombatUnit unit)
    {
        if (unit == null || unit.currentTileIndex < 0) return false;
        var ts = TileSystem.GetForPlanet(unit.planetIndex) ?? TileSystem.Instance;
        if (ts == null || !ts.IsReady()) return false;
        
        var tileData = ts.GetTileData(unit.currentTileIndex);
        if (tileData == null) return false;
        
        // Check if tile has a city
        if (tileData.HasCity)
        {
            // Check if city belongs to same civilization
            var city = FindCityAtTile(unit.currentTileIndex);
            if (city != null && city.owner == unit.owner)
            {
                return true;
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Find city at a specific tile
    /// </summary>
    private City FindCityAtTile(int tileIndex)
    {
        // Use cached cities array to avoid expensive FindObjectsByType call
        if (Time.time - lastCityCacheUpdate > CITY_CACHE_UPDATE_INTERVAL)
        {
            cachedAllCities = FindObjectsByType<City>(FindObjectsSortMode.None);
            lastCityCacheUpdate = Time.time;
        }
        
        if (cachedAllCities == null) return null;
        
        foreach (var city in cachedAllCities)
        {
            if (city == null) continue;
            if (city.centerTileIndex == tileIndex)
            {
                return city;
            }
        }
        return null;
    }
}

