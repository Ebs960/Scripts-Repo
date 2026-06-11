using UnityEngine;
using System.Collections.Generic;

public enum CultureGroup
{
    Western,
    Germanic,
    Eastern,
    WestAfrican,
    EastAfrican,
    PacificIslanders,
    SouthEastAsian,
    Latino,
    Indigenous,
    Mesoamerican,
    NorthAmerican,
    CentralAsian,
    MiddleEastern,
    // add others as needed
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

    [Header("City Models")]
    [Tooltip("City prefabs for different tech ages")]
    public CityPrefabByAge[] cityPrefabsByAge;

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

/// <summary>
/// Represents a city prefab for a specific tech age
/// </summary>
[System.Serializable]
public class CityPrefabByAge
{
    public TechAge techAge;
    public GameObject cityPrefab;
}