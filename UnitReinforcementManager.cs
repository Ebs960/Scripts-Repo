using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages unit reinforcement - units recover soldier count over time
/// - 10% per turn outside cities
/// - 35% per turn when garrisoned in cities
/// </summary>
public class UnitReinforcementManager : MonoBehaviour
{
    public static UnitReinforcementManager Instance { get; private set; }
    
    [Header("Reinforcement Rates")]
    [Tooltip("Reinforcement rate per turn for units outside cities (as percentage)")]
    [Range(0f, 100f)]
    public float reinforcementRateOutsideCity = 10f; // 10% per turn
    
    [Tooltip("Reinforcement rate per turn for units garrisoned in cities (as percentage)")]
    [Range(0f, 100f)]
    public float reinforcementRateInCity = 35f; // 35% per turn
    
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
            if (civ == null || civ.combatUnits == null) continue;
            
            foreach (var unit in civ.combatUnits)
            {
                if (unit == null || unit.data == null) continue;
                
                ApplyReinforcement(unit);
            }
        }
        
        Debug.Log("[UnitReinforcementManager] Applied reinforcement to all units");
    }
    
    /// <summary>
    /// Apply reinforcement to a single unit
    /// </summary>
    public void ApplyReinforcement(CombatUnit unit)
    {
        if (unit == null || unit.data == null) return;
        if (unit.soldierCount >= unit.maxSoldierCount) return; // Already at max
        
        // Determine reinforcement rate based on location
        float reinforcementRate = unit.isGarrisonedInCity 
            ? reinforcementRateInCity 
            : reinforcementRateOutsideCity;
        
        // Calculate reinforcement amount (percentage of max)
        int reinforcementAmount = Mathf.RoundToInt(unit.maxSoldierCount * (reinforcementRate / 100f));
        
        // Apply reinforcement
        int oldCount = unit.soldierCount;
        unit.soldierCount = Mathf.Min(unit.soldierCount + reinforcementAmount, unit.maxSoldierCount);
        
        if (unit.soldierCount > oldCount)
        {
            Debug.Log($"[UnitReinforcementManager] {unit.data.unitName} reinforced: {oldCount} -> {unit.soldierCount} soldiers ({(unit.isGarrisonedInCity ? "in city" : "outside")})");
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
        if (TileSystem.Instance == null) return false;
        
        var tileData = TileSystem.Instance.GetTileData(unit.currentTileIndex);
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

