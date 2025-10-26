// Assets/Scripts/Data/TechData.cs
using UnityEngine;
using System;

public enum TechAge
{
    PaleolithicAge,
    NeolithicAge,
    MonumentAge,
    CopperAge,
    BronzeAge,
    IronAge,
    ClassicalAge,
    AxialAge,
    DarkAge,
    FeudalAge,
    CastleAge,
    RenaissanceAge,
    ColonialAge,
    ReformationAge,
    EnlightenmentAge,
    RevolutionAge,
    SteamAge,
    RailroadAge,
    ImperialAge,
    ModernAge,
    AtomicAge,
    InformationAge,
    NanoAge,
    MutantAge,
    SolarAge,
    AquarianAge,
    PlasmaAge,
    InterstellarAge,
    GalacticAge
}

public enum TechCategory
{
    General,
    Military,
    Economic,
    Cultural,
    Religious,
    Scientific,
    Infrastructure
}

[CreateAssetMenu(fileName = "NewTechData", menuName = "Data/Technology Data")]
public class TechData : ScriptableObject
{
    [Header("Identification")]
    public string techName;
    public TechAge techAge;

    [TextArea]
    public string description;

    [Header("Visual & Tree Layout")]
    [Tooltip("Icon sprite for the tech tree")]
    public Sprite techIcon;
    [Tooltip("Hint for vertical positioning within dependency layer (0=top, higher=bottom)")]
    public int positionHint = 0;
    [Tooltip("Group related techs together")]
    public TechCategory category = TechCategory.General;
    [Tooltip("Custom color for this tech node (optional)")]
    public Color techColor = Color.white;

    [Header("Cost & Requirements")]
    public int scienceCost;
    public TechData[] requiredTechnologies;
    public CultureData[] requiredCultures;      // ← new: culture prereqs
    public int requiredCityCount;
    public Biome[] requiredControlledBiomes;

    [Header("Unlocks & Bonuses")]
    public CombatUnitData[] unlockedUnits;
    public WorkerUnitData[] unlockedWorkerUnits;
    public ImprovementData[] unlocksImprovements;
    public BuildingData[] unlockedBuildings;
    // REMOVED: unlockedEquipment - Equipment availability now controlled ONLY by EquipmentData.requiredTechs
    public PolicyData[] unlockedPolicies;
    public GovernmentData[] unlockedGovernments;
    public ReligionData[] unlockedReligions;
    public LeaderData[] unlockedLeaders;
    
    [Tooltip("Whether this technology unlocks pantheon founding and religion mechanics")]
    public bool unlocksReligion;
    public float attackBonus;                   // e.g. +10% attack
    public float defenseBonus;                  // e.g. +10% defense
    public float movementBonus;                 // e.g. +1 move point
    public float foodModifier;                  // Percentage modifier (e.g. 0.1 = +10%)
    public float productionModifier;            // Percentage modifier (e.g. 0.1 = +10%)
    public float goldModifier;                  // Percentage modifier (e.g. 0.1 = +10%)
    public float scienceModifier;               // Percentage modifier (e.g. 0.1 = +10%)
    public float cultureModifier;               // Percentage modifier (e.g. 0.1 = +10%)
    public float faithModifier;                 // Percentage modifier (e.g. 0.1 = +10%)

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

    [Header("Targeted Bonuses")]
    [Tooltip("Per-unit stat bonuses granted by this technology.")]
    public UnitStatBonus[] unitBonuses;
    [Tooltip("Per-unit per-turn yield bonuses granted by this technology.")]
    public UnitYieldBonus[] unitYieldBonuses;
    [Tooltip("Per-worker stat bonuses granted by this technology.")]
    public WorkerUnitStatBonus[] workerBonuses;
    [Tooltip("Per-worker per-turn yield bonuses granted by this technology.")]
    public WorkerUnitYieldBonus[] workerYieldBonuses;
    [Tooltip("Flat work points added to ALL worker units when this technology is researched.")]
    public int allWorkersWorkPoints = 0;

    // Backwards-compatible aliases for older code that referenced globalWorkerWorkBonus/globalWorkerWorkModifier
    // These expose the same flat value and a zero multiplier to avoid changing runtime behavior.
    public int globalWorkerWorkBonus => allWorkersWorkPoints;
    public float globalWorkerWorkModifier => 0f;
    [Tooltip("Per-equipment stat bonuses granted by this technology.")]
    public EquipmentStatBonus[] equipmentBonuses;
    [Tooltip("Per-equipment per-turn yield bonuses granted by this technology (applies when equipped).")]
    public EquipmentYieldBonus[] equipmentYieldBonuses;
    [Tooltip("Per-improvement yield bonuses granted by this technology.")]
    public ImprovementYieldBonus[] improvementBonuses;
    [Tooltip("Per-building yield bonuses granted by this technology.")]
    public BuildingYieldBonus[] buildingBonuses;
    [Tooltip("Generic yield bonuses for other ScriptableObject targets (e.g., districts).")]
    public GenericYieldBonus[] genericYieldBonuses;

    [Header("Governor Bonuses")]
    public int additionalGovernorSlots;
    public GovernorTrait[] unlockedGovernorTraits;

    [Header("Unit & Building Limits")]
    [Tooltip("Increases the limit for specific units/buildings")]
    public UnitLimitModifier[] unitLimitModifiers;
    public BuildingLimitModifier[] buildingLimitModifiers;

    [Header("Cities & Settlement")]
    [Tooltip("How much this technology increases the maximum number of cities a civilization may found. Use 1 on the first sedentary tech to allow the first city." )]
    public int cityCapIncrease = 0;
    [Tooltip("How much this technology increases the maximum number of pantheons a civilization may found.")]
    public int pantheonCapIncrease = 0;
}