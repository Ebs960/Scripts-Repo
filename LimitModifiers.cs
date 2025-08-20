using UnityEngine;

/// <summary>
/// Modifier for increasing unit limits through technologies and cultures
/// </summary>
[System.Serializable]
public class UnitLimitModifier
{
    [Header("Target")]
    [Tooltip("Specific unit to modify limit for")]
    public CombatUnitData targetCombatUnit;
    [Tooltip("Specific worker unit to modify limit for")]
    public WorkerUnitData targetWorkerUnit;
    [Tooltip("Or target by category name (overrides specific unit if both set)")]
    public string targetCategory;
    
    [Header("Modifier")]
    [Tooltip("Amount to increase the limit by")]
    public int limitIncrease = 1;
    [Tooltip("Description for UI display")]
    public string description;
    
    /// <summary>
    /// Check if this modifier applies to the given unit
    /// </summary>
    public bool AppliesTo(CombatUnitData unit)
    {
        if (!string.IsNullOrEmpty(targetCategory))
            return unit.limitCategory == targetCategory;
        return targetCombatUnit == unit;
    }
    
    /// <summary>
    /// Check if this modifier applies to the given worker unit
    /// </summary>
    public bool AppliesTo(WorkerUnitData unit)
    {
        if (!string.IsNullOrEmpty(targetCategory))
            return unit.limitCategory == targetCategory;
        return targetWorkerUnit == unit;
    }
}

/// <summary>
/// Modifier for increasing building limits through technologies and cultures
/// </summary>
[System.Serializable]
public class BuildingLimitModifier
{
    [Header("Target")]
    [Tooltip("Specific building to modify limit for")]
    public BuildingData targetBuilding;
    [Tooltip("Or target by category name (overrides specific building if both set)")]
    public string targetCategory;
    
    [Header("Modifier")]
    [Tooltip("Amount to increase the limit by")]
    public int limitIncrease = 1;
    [Tooltip("Description for UI display")]
    public string description;
    
    /// <summary>
    /// Check if this modifier applies to the given building
    /// </summary>
    public bool AppliesTo(BuildingData building)
    {
        if (!string.IsNullOrEmpty(targetCategory))
            return building.limitCategory == targetCategory;
        return targetBuilding == building;
    }
}
