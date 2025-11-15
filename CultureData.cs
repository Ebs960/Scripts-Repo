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

    [Header("Visual")]
    [Tooltip("Icon sprite for the culture")]
    public Sprite cultureIcon;

    [Header("Gameplay")]
    [Tooltip("If true this culture enables the global trade system when adopted.")]
    public bool enablesTradeSystem = false;

    [Header("Cost & Requirements")]
    public int cultureCost;
    public CultureData[] requiredCultures;
    public int requiredCityCount;
    public Biome[] requiredControlledBiomes;

    [Header("Unlocks & Bonuses")]
    // REMOVED: unlocksPolicies
    // Policy availability is now controlled solely by requiredTechs/requiredCultures/requiredGovernments in PolicyData
    public GovernmentData[] unlockedGovernments;
    public ReligionData[] unlockedReligions;
    // REMOVED: All unlocked arrays - availability now controlled ONLY by requiredCultures in the respective data classes
    // Unit availability: CombatUnitData.requiredCultures / WorkerUnitData.requiredCultures
    // Building availability: BuildingData.requiredCultures
    // Improvement availability: ImprovementData.requiredCultures
    public float attackBonus;
    public float defenseBonus;
    public float movementBonus;
    public float foodModifier;                  // Percentage modifier (e.g. 0.1 = +10%)
    public float productionModifier;            // Percentage modifier (e.g. 0.1 = +10%)
    public float goldModifier;                  // Percentage modifier (e.g. 0.1 = +10%)
    public float scienceModifier;               // Percentage modifier (e.g. 0.1 = +10%)
    public float cultureModifier;               // Percentage modifier (e.g. 0.1 = +10%)
    public float faithModifier;                 // Percentage modifier (e.g. 0.1 = +10%)

    [Header("Religion Unlocks")]
    [Tooltip("If true, adopting this culture allows the civ to found pantheons (e.g., spirits).")]
    public bool unlocksPantheon = false;
    [Tooltip("If true, adopting this culture enables religion founding mechanics for the civ (e.g., holy sites, religions).")]
    public bool unlocksReligion = false;

    [Header("Flat Bonuses")]
    [Tooltip("Flat food bonus per turn (e.g. +2 food per turn)")]
    public int flatFoodBonus;
    [Tooltip("Flat production bonus per turn (e.g. +2 production per turn)")]
    public int flatProductionBonus;
    [Tooltip("Flat gold bonus per turn (e.g. +3 gold per turn)")]
    public int flatGoldBonus;
    [Tooltip("Flat science bonus per turn (e.g. +2 science per turn)")]
    public int flatScienceBonus;
    [Tooltip("Flat culture bonus per turn (e.g. +1 culture per turn)")]
    public int flatCultureBonus;
    [Tooltip("Flat faith bonus per turn (e.g. +1 faith per turn)")]
    public int flatFaithBonus;

    [Header("Governor Bonuses")]
    public int additionalGovernorSlots;
    public GovernorTrait[] unlockedGovernorTraits;
    [Tooltip("If true, adopting this culture enables the governor mechanic for the civilization (allows creating/assigning governors).")]
    public bool enablesGovernors = false;

    [Header("Targeted Bonuses")]
    [Tooltip("Per-unit stat bonuses granted by this culture.")]
    public UnitStatBonus[] unitBonuses;
    [Tooltip("Per-unit per-turn yield bonuses granted by this culture.")]
    public UnitYieldBonus[] unitYieldBonuses;
    [Tooltip("Per-worker stat bonuses granted by this culture.")]
    public WorkerUnitStatBonus[] workerBonuses;
    [Tooltip("Per-worker per-turn yield bonuses granted by this culture.")]
    public WorkerUnitYieldBonus[] workerYieldBonuses;
    [Tooltip("Flat work points added to ALL worker units when this culture is adopted.")]
    public int allWorkersWorkPoints = 0;

    // Backwards-compatible aliases for older code that referenced globalWorkerWorkBonus/globalWorkerWorkModifier
    public int globalWorkerWorkBonus => allWorkersWorkPoints;
    public float globalWorkerWorkModifier => 0f;
    [Tooltip("Per-equipment stat bonuses granted by this culture.")]
    public EquipmentStatBonus[] equipmentBonuses;
    [Tooltip("Per-equipment per-turn yield bonuses granted by this culture (applies when equipped).")]
    public EquipmentYieldBonus[] equipmentYieldBonuses;
    [Tooltip("Per-improvement yield bonuses granted by this culture.")]
    public ImprovementYieldBonus[] improvementBonuses;
    [Tooltip("Per-building yield bonuses granted by this culture.")]
    public BuildingYieldBonus[] buildingBonuses;
    [Tooltip("Generic yield bonuses for other ScriptableObject targets (e.g., districts).")]
    public GenericYieldBonus[] genericYieldBonuses;
    [Tooltip("Army stat bonuses granted by this culture (applies to all armies).")]
    public ArmyStatBonus[] armyBonuses;

    [Header("Limits")]
    [Tooltip("How much this culture increases the maximum number of pantheons a civilization may found.")]
    public int pantheonCapIncrease = 0;

    [Header("Religion Unlocks (features)")]
    [Tooltip("Pantheons unlocked for the adopting civ when this culture is adopted")]
    public PantheonData[] unlocksPantheons;
    [Tooltip("Founder beliefs unlocked for the adopting civ when this culture is adopted")]
    public BeliefData[] unlocksBeliefs;

    [Header("Unit & Building Limits")]
    [Tooltip("Increases the limit for specific units/buildings")]
    public UnitLimitModifier[] unitLimitModifiers;
    public BuildingLimitModifier[] buildingLimitModifiers;
}