using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Manages unit and building limits for civilizations
/// </summary>
public class LimitManager : MonoBehaviour
{
    public static LimitManager Instance { get; private set; }
    
    private Dictionary<Civilization, Dictionary<string, int>> unitCounts = new Dictionary<Civilization, Dictionary<string, int>>();
    private Dictionary<Civilization, Dictionary<string, int>> buildingCounts = new Dictionary<Civilization, Dictionary<string, int>>();
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    #region Unit Limits
    
    /// <summary>
    /// Get the effective limit for a combat unit (base limit + modifiers)
    /// </summary>
    public int GetCombatUnitLimit(Civilization civ, CombatUnitData unit)
    {
        if (unit.unitLimit < 0) return -1; // Unlimited
        
        int effectiveLimit = unit.unitLimit;
        
        // Add modifiers from technologies
        if (civ.researchedTechs != null)
        {
            foreach (var tech in civ.researchedTechs)
            {
                if (tech.unitLimitModifiers != null)
                {
                    foreach (var modifier in tech.unitLimitModifiers)
                    {
                        if (modifier.AppliesTo(unit))
                        {
                            effectiveLimit += modifier.limitIncrease;
                        }
                    }
                }
            }
        }
        
        // Add modifiers from cultures
        if (civ.researchedCultures != null)
        {
            foreach (var culture in civ.researchedCultures)
            {
                if (culture.unitLimitModifiers != null)
                {
                    foreach (var modifier in culture.unitLimitModifiers)
                    {
                        if (modifier.AppliesTo(unit))
                        {
                            effectiveLimit += modifier.limitIncrease;
                        }
                    }
                }
            }
        }
        
        return Mathf.Max(0, effectiveLimit);
    }
    
    /// <summary>
    /// Get the effective limit for a worker unit (base limit + modifiers)
    /// </summary>
    public int GetWorkerUnitLimit(Civilization civ, WorkerUnitData unit)
    {
        if (unit.unitLimit < 0) return -1; // Unlimited
        
        int effectiveLimit = unit.unitLimit;
        
        // Add modifiers from technologies
        if (civ.researchedTechs != null)
        {
            foreach (var tech in civ.researchedTechs)
            {
                if (tech.unitLimitModifiers != null)
                {
                    foreach (var modifier in tech.unitLimitModifiers)
                    {
                        if (modifier.AppliesTo(unit))
                        {
                            effectiveLimit += modifier.limitIncrease;
                        }
                    }
                }
            }
        }
        
        // Add modifiers from cultures
        if (civ.researchedCultures != null)
        {
            foreach (var culture in civ.researchedCultures)
            {
                if (culture.unitLimitModifiers != null)
                {
                    foreach (var modifier in culture.unitLimitModifiers)
                    {
                        if (modifier.AppliesTo(unit))
                        {
                            effectiveLimit += modifier.limitIncrease;
                        }
                    }
                }
            }
        }
        
        return Mathf.Max(0, effectiveLimit);
    }
    
    /// <summary>
    /// Check if a civilization can create more of this combat unit
    /// </summary>
    public bool CanCreateCombatUnit(Civilization civ, CombatUnitData unit)
    {
        int limit = GetCombatUnitLimit(civ, unit);
        if (limit < 0) return true; // Unlimited
        
        int currentCount = GetCombatUnitCount(civ, unit);
        return currentCount < limit;
    }
    
    /// <summary>
    /// Check if a civilization can create more of this worker unit
    /// </summary>
    public bool CanCreateWorkerUnit(Civilization civ, WorkerUnitData unit)
    {
        int limit = GetWorkerUnitLimit(civ, unit);
        if (limit < 0) return true; // Unlimited
        
        int currentCount = GetWorkerUnitCount(civ, unit);
        return currentCount < limit;
    }
    
    /// <summary>
    /// Get current count of combat units for a civilization
    /// </summary>
    public int GetCombatUnitCount(Civilization civ, CombatUnitData unit)
    {
        string key = GetUnitKey(unit);
        
        if (!unitCounts.ContainsKey(civ))
            unitCounts[civ] = new Dictionary<string, int>();
        
        return unitCounts[civ].GetValueOrDefault(key, 0);
    }
    
    /// <summary>
    /// Get current count of worker units for a civilization
    /// </summary>
    public int GetWorkerUnitCount(Civilization civ, WorkerUnitData unit)
    {
        string key = GetUnitKey(unit);
        
        if (!unitCounts.ContainsKey(civ))
            unitCounts[civ] = new Dictionary<string, int>();
        
        return unitCounts[civ].GetValueOrDefault(key, 0);
    }
    
    /// <summary>
    /// Increment unit count when a unit is created
    /// </summary>
    public void AddCombatUnit(Civilization civ, CombatUnitData unit)
    {
        string key = GetUnitKey(unit);
        
        if (!unitCounts.ContainsKey(civ))
            unitCounts[civ] = new Dictionary<string, int>();
        
        unitCounts[civ][key] = unitCounts[civ].GetValueOrDefault(key, 0) + 1;
    }
    
    /// <summary>
    /// Increment worker unit count when a unit is created
    /// </summary>
    public void AddWorkerUnit(Civilization civ, WorkerUnitData unit)
    {
        string key = GetUnitKey(unit);
        
        if (!unitCounts.ContainsKey(civ))
            unitCounts[civ] = new Dictionary<string, int>();
        
        unitCounts[civ][key] = unitCounts[civ].GetValueOrDefault(key, 0) + 1;
    }
    
    /// <summary>
    /// Decrement unit count when a unit is destroyed
    /// </summary>
    public void RemoveCombatUnit(Civilization civ, CombatUnitData unit)
    {
        string key = GetUnitKey(unit);
        
        if (!unitCounts.ContainsKey(civ))
            return;
        
        if (unitCounts[civ].ContainsKey(key))
        {
            unitCounts[civ][key] = Mathf.Max(0, unitCounts[civ][key] - 1);
        }
    }
    
    /// <summary>
    /// Decrement worker unit count when a unit is destroyed
    /// </summary>
    public void RemoveWorkerUnit(Civilization civ, WorkerUnitData unit)
    {
        string key = GetUnitKey(unit);
        
        if (!unitCounts.ContainsKey(civ))
            return;
        
        if (unitCounts[civ].ContainsKey(key))
        {
            unitCounts[civ][key] = Mathf.Max(0, unitCounts[civ][key] - 1);
        }
    }
    
    private string GetUnitKey(CombatUnitData unit)
    {
        return string.IsNullOrEmpty(unit.limitCategory) ? $"combat_{unit.name}" : $"category_{unit.limitCategory}";
    }
    
    private string GetUnitKey(WorkerUnitData unit)
    {
        return string.IsNullOrEmpty(unit.limitCategory) ? $"worker_{unit.name}" : $"category_{unit.limitCategory}";
    }
    
    #endregion
    
    #region Building Limits
    
    /// <summary>
    /// Get the effective limit for a building (base limit + modifiers)
    /// </summary>
    public int GetBuildingLimit(Civilization civ, BuildingData building)
    {
        if (building.buildingLimit < 0) return -1; // Unlimited
        
        int effectiveLimit = building.buildingLimit;
        
        // Add modifiers from technologies
        if (civ.researchedTechs != null)
        {
            foreach (var tech in civ.researchedTechs)
            {
                if (tech.buildingLimitModifiers != null)
                {
                    foreach (var modifier in tech.buildingLimitModifiers)
                    {
                        if (modifier.AppliesTo(building))
                        {
                            effectiveLimit += modifier.limitIncrease;
                        }
                    }
                }
            }
        }
        
        // Add modifiers from cultures
        if (civ.researchedCultures != null)
        {
            foreach (var culture in civ.researchedCultures)
            {
                if (culture.buildingLimitModifiers != null)
                {
                    foreach (var modifier in culture.buildingLimitModifiers)
                    {
                        if (modifier.AppliesTo(building))
                        {
                            effectiveLimit += modifier.limitIncrease;
                        }
                    }
                }
            }
        }
        
        return Mathf.Max(0, effectiveLimit);
    }
    
    /// <summary>
    /// Check if a civilization can build more of this building
    /// </summary>
    public bool CanCreateBuilding(Civilization civ, BuildingData building)
    {
        int limit = GetBuildingLimit(civ, building);
        if (limit < 0) return true; // Unlimited
        
        int currentCount = GetBuildingCount(civ, building);
        return currentCount < limit;
    }
    
    /// <summary>
    /// Check if a city can build this building (per-city limit)
    /// </summary>
    public bool CanCityCreateBuilding(City city, BuildingData building)
    {
        if (building.perCityLimit < 0) return true; // Unlimited per city
        
        int currentInCity = GetBuildingCountInCity(city, building);
        return currentInCity < building.perCityLimit;
    }
    
    /// <summary>
    /// Get current count of buildings for a civilization
    /// </summary>
    public int GetBuildingCount(Civilization civ, BuildingData building)
    {
        string key = GetBuildingKey(building);
        
            buildingCounts[civ] = new Dictionary<string, int>();
        
        return buildingCounts[civ].GetValueOrDefault(key, 0);
    }
    
    /// <summary>
    /// Get current count of buildings in a specific city
    /// </summary>
    public int GetBuildingCountInCity(City city, BuildingData building)
    {
        // This would need to be integrated with your city building system
        // For now, return 0 as placeholder
        return 0;
    }
    
    /// <summary>
    /// Increment building count when a building is constructed
    /// </summary>
    public void AddBuilding(Civilization civ, BuildingData building)
    {
        string key = GetBuildingKey(building);
        
        if (!buildingCounts.ContainsKey(civ))
            buildingCounts[civ] = new Dictionary<string, int>();
        
        buildingCounts[civ][key] = buildingCounts[civ].GetValueOrDefault(key, 0) + 1;
    }
    
    /// <summary>
    /// Decrement building count when a building is destroyed
    /// </summary>
    public void RemoveBuilding(Civilization civ, BuildingData building)
    {
        string key = GetBuildingKey(building);
        
        if (!buildingCounts.ContainsKey(civ))
            return;
        
        if (buildingCounts[civ].ContainsKey(key))
        {
            buildingCounts[civ][key] = Mathf.Max(0, buildingCounts[civ][key] - 1);
        }
    }
    
    private string GetBuildingKey(BuildingData building)
    {
        return string.IsNullOrEmpty(building.limitCategory) ? $"building_{building.name}" : $"category_{building.limitCategory}";
    }
    
    #endregion
    
    #region Utility Methods
    
    /// <summary>
    /// Get a summary of all limits for a civilization
    /// </summary>
    public string GetLimitSummary(Civilization civ)
    {
        var summary = new System.Text.StringBuilder();
        summary.AppendLine($"Limits for {civ.civData?.civName ?? "Unknown Civilization"}:");
        
        // Unit limits
        if (unitCounts.ContainsKey(civ))
        {
            summary.AppendLine("\nUnits:");
            foreach (var kvp in unitCounts[civ])
            {
                summary.AppendLine($"  {kvp.Key}: {kvp.Value}");
            }
        }
        
        // Building limits
        if (buildingCounts.ContainsKey(civ))
        {
            summary.AppendLine("\nBuildings:");
            foreach (var kvp in buildingCounts[civ])
            {
                summary.AppendLine($"  {kvp.Key}: {kvp.Value}");
            }
        }
        
        return summary.ToString();
    }
    
    /// <summary>
    /// Reset all counts for a civilization (useful for game restart)
    /// </summary>
    public void ResetCivilizationCounts(Civilization civ)
    {
        if (unitCounts.ContainsKey(civ))
            unitCounts[civ].Clear();
        
        if (buildingCounts.ContainsKey(civ))
            buildingCounts[civ].Clear();
    }
    
    #endregion
}

/// <summary>
/// Extension method for Dictionary to provide GetValueOrDefault functionality
/// </summary>
public static class DictionaryExtensions
{
    public static TValue GetValueOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue = default(TValue))
    {
        return dictionary.TryGetValue(key, out TValue value) ? value : defaultValue;
    }
}
