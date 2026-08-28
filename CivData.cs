using UnityEngine;
using System.Collections.Generic;

public enum CultureGroup
{
    Western = 0,
    EastAsian = 2,
    WestAfrican = 3,
    EastAfrican = 4,
    Mesoamerican = 9,
    NativeNorthAmerican = 10,
    MiddleEastern = 12,
    SouthAsian = 13
}

/// <summary>
/// Holds all static, data-driven properties of a civilization.
/// </summary>
[CreateAssetMenu(fileName = "NewCivData", menuName = "Data/Civilization Data")]
public class CivData : ScriptableObject
{
    [Header("Identification")]
    public string civName;
    public Sprite icon;
    public List<string> cityNames; // List of historical or thematic city names
    public List<string> herdNames; // List of thematic herd names (used when spawning new herds)
    public List<LeaderData> availableLeaders; // Replaced single leader with a list

    [Header("Culture & Diplomacy")]
    public CultureGroup cultureGroup;        // Cultural affinity group
    public CultureData[] cultureBonuses;     // Traits or bonus policies

    [Header("Description")]
    [TextArea(2, 6)]
    public string description;

    [Header("Starting Assets")]
    public TechData[] startingTechs;         // Technologies known at game start
    public CultureData[] startingCultures;   // Cultures known at game start
    public PolicyData[] startingPolicies;    // Initial policies or governments
    public CombatUnitData[] uniqueUnits;     // Civilizational unique units
    public BuildingData[] uniqueBuildings;   // Unique city or tile improvements
    [Tooltip("Optional Band rules/content override for this civilization. Falls back to CivilizationManager.startingBandData.")]
    public BandData startingBandData;
    [Tooltip("The real CombatUnits placed in this civilization's opening Band garrison. When empty, the selected BandData starting garrison is used.")]
    public StartingBandGarrisonEntry[] startingBandGarrison;

    [Header("Preferences & Modifiers")]
    public Biome[] climatePreferences;   // Preferred biomes for starting placement
    public float attackBonus;                // % bonus to all unit attacks
    public float meleeAttackBonus;           // % bonus to melee attacks
    public float rangedAttackBonus;          // % bonus to ranged attacks
    public float cityAttackBonus;            // % bonus to city attacks
    public float defenseBonus;               // % bonus to all unit defenses
    public float movementBonus;              // % bonus to movement points
    public float foodModifier;              // New
    public float productionModifier;        // New
    public float goldModifier;              // New
    public float scienceModifier;           // New
    public float cultureModifier;           // New
    public float faithModifier;             // New

    [Header("Tile Yield Bonuses")]
    [Tooltip("Innate per-tile yield bonuses granted by this civilization to matching worked city tiles.")]
    public TileYieldBonus[] tileYieldBonuses;

    [Header("Building Yield Bonuses")]
    [Tooltip("Innate per-building yield/stat bonuses granted by this civilization. Can target exact buildings or building categories.")]
    public BuildingYieldBonus[] buildingBonuses;

    [Header("City Bonuses")]
    [Tooltip("Innate per-city yield, defense, and happiness bonuses granted by this civilization.")]
    public CityYieldBonus[] cityBonuses;

    [Header("Religion Unhappiness")]
    [Tooltip("Innate modifiers to per-turn city unhappiness caused by citizens following religions other than the state religion.")]
    public NonStateReligionUnhappinessModifier[] nonStateReligionUnhappinessModifiers;

    [Header("Unit Bonuses")]
    [Tooltip("Per-unit stat bonuses granted by this civilization's base identity (e.g., healing speed).")]
    public UnitStatBonus[] unitBonuses;
    [Tooltip("Per-worker stat bonuses granted by this civilization's base identity (e.g., healing speed).")]
    public WorkerUnitStatBonus[] workerBonuses;
    [Tooltip("Auras projected by this civilization's units.")]
    public UnitAuraBonus[] auraBonuses;
    [Tooltip("Unit-training production modifiers granted by this civilization.")]
    public UnitProductionModifier[] unitProductionModifiers;
    [Tooltip("Per-unit per-turn yield bonuses granted by this civilization.")]
    public UnitYieldBonus[] unitYieldBonuses;
    [Tooltip("Per-worker per-turn yield bonuses granted by this civilization.")]
    public WorkerUnitYieldBonus[] workerYieldBonuses;
    [Tooltip("Flat work points added to ALL worker units for this civilization.")]
    public int allWorkersWorkPoints = 0;

    [Header("Equipment Bonuses")]
    [Tooltip("Per-equipment stat bonuses granted by this civilization.")]
    public EquipmentStatBonus[] equipmentBonuses;
    [Tooltip("Per-equipment per-turn yield bonuses granted by this civilization (applies when equipped).")]
    public EquipmentYieldBonus[] equipmentYieldBonuses;

    [Header("Improvement & Generic Bonuses")]
    [Tooltip("Per-improvement yield bonuses granted by this civilization.")]
    public ImprovementYieldBonus[] improvementBonuses;
    [Tooltip("Generic yield bonuses for other ScriptableObject targets (e.g., districts).")]
    public GenericYieldBonus[] genericYieldBonuses;

    [Header("Unit & Building Limits")]
    [Tooltip("Increases the limit for specific units/buildings.")]
    public UnitLimitModifier[] unitLimitModifiers;
    public BuildingLimitModifier[] buildingLimitModifiers;
    [Tooltip("Increases city building slot capacity by slot type.")]
    public CitySlotModifier[] citySlotModifiers;

    [Header("Consolidated Effects")]
    [Tooltip("Shared effect set for new civ bonuses. Existing fields remain supported for backwards compatibility.")]
    public CivEffectSet effects;

    [Tooltip("Disease modifiers granted by this civilization's base identity.")]
    public DiseaseModifierBonus[] diseaseBonuses;
    [Tooltip("Attrition modifiers provided directly on the civilization asset.")]
    public AttritionModifierBonus[] attritionBonuses;
    [Tooltip("Reduces the percent of herd animals lost to starvation (e.g. 0.05 = -5 percentage points)")]
    public float herdStarvationPercentReduction = 0f;
    [Tooltip("Per-herd per-turn yield bonuses innate to this civilization (can filter by animal species).")]
    public HerdYieldBonus[] herdYieldBonuses;

    [Header("Gameplay Flags")]
    public bool isTribe;                     // Limited to max 3 cities, starts at war
    public bool isCityState;                 // Single-city civ with diplomatic traits

    [Header("Music")]
    public MusicData musicData;

    [Header("City Visuals")]
    [Tooltip("Whole-settlement visual prefabs by broad period. These explicit per-civilization references are authoritative when configured.")]
    public CityVisualSet[] cityVisuals;

    [Header("Legacy City Models (Migration Only)")]
    [Tooltip("Legacy per-tech-age city root prefabs. Retained for serialized asset migration; not used to resolve settlement artwork.")]
    public CityPrefabByAge[] cityPrefabsByAge;

    [Header("Campaign Army Models")]
    [Tooltip("Campaign-map army presentation prefabs by technological age. The latest configured age at or before the civilization's current age is used.")]
    public ArmyPrefabByAge[] armyPrefabsByAge;

    [Header("Band Model")]
    [Tooltip("Prefab for this civilization's mobile Band entity. Must contain a Band component. Falls back to BandData.prefab when unassigned.")]
    public GameObject bandPrefab;

    [Header("Herds")]
    [Tooltip("Prefab used to visually represent a herd when spawned for this civ (optional)")]
    public GameObject herdPrefab;
    [Tooltip("Prefab used when the herd is packed/mobile (optional)")]
    public GameObject herdPackedPrefab;
    [Tooltip("Prefab used when the herd is settled/camp (optional). If null, `herdPrefab` is used.")]
    public GameObject herdSettledPrefab;

    // Additional fields for future expansion:
    // public RouteType[] allowedRoutes;
    // public EquipmentData[] uniqueEquipment;
}

[System.Serializable]
public class ArmyPrefabByAge
{
    public TechAge techAge;
    public GameObject armyPrefab;
}

/// <summary>
/// Represents a city prefab for a specific tech age
/// </summary>
[System.Serializable]
public class CityPrefabByAge
{
    public TechAge techAge;
    public GameObject cityPrefab;
}
