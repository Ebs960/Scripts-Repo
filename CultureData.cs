// Assets/Scripts/Data/CultureData.cs
using UnityEngine;
using System;

[CreateAssetMenu(fileName = "NewCultureData", menuName = "Data/Culture Data")]
public class CultureData : ScriptableObject
{
    [Header("Identification")]
    public string cultureName;

    [TextArea]
    public string description;

    [Header("Cost & Requirements")]
    public int cultureCost;
    public CultureData[] requiredCultures;
    public int requiredCityCount;
    public Biome[] requiredControlledBiomes;

    [Header("Unlocks & Bonuses")]
    public PolicyData[] unlocksPolicies;
    public CombatUnitData[] unlockedUnits;
    public WorkerUnitData[] unlockedWorkerUnits;
    public BuildingData[] unlockedBuildings;
    public AbilityData[] unlockedAbilities;
    public GovernmentData[] unlockedGovernments;
    public ReligionData[] unlockedReligions;
    public float attackBonus;
    public float defenseBonus;
    public float movementBonus;
    public float foodModifier;
    public float productionModifier;
    public float goldModifier;
    public float scienceModifier;
    public float cultureModifier;
    public float faithModifier;

    [Header("Governor Bonuses")]
    public int additionalGovernorSlots;
    public GovernorTrait[] unlockedGovernorTraits;

    [Header("Targeted Bonuses")]
    [Tooltip("Per-unit stat bonuses granted by this culture.")]
    public UnitStatBonus[] unitBonuses;
    [Tooltip("Per-worker stat bonuses granted by this culture.")]
    public WorkerUnitStatBonus[] workerBonuses;
    [Tooltip("Per-equipment stat bonuses granted by this culture.")]
    public EquipmentStatBonus[] equipmentBonuses;
    [Tooltip("Per-improvement yield bonuses granted by this culture.")]
    public ImprovementYieldBonus[] improvementBonuses;
    [Tooltip("Per-building yield bonuses granted by this culture.")]
    public BuildingYieldBonus[] buildingBonuses;
    [Tooltip("Generic yield bonuses for other ScriptableObject targets (e.g., districts).")]
    public GenericYieldBonus[] genericYieldBonuses;
}