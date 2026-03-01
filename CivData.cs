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
    Nomadic,
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
    public List<LeaderData> availableLeaders; // Replaced single leader with a list

    [Header("Culture & Diplomacy")]
    public CultureGroup cultureGroup;        // Cultural affinity group
    public CultureData[] cultureBonuses;     // Traits or bonus policies

    [Header("Description")]
    [TextArea(2, 6)]
    public string description;

    [Header("Starting Assets")]
    public TechData[] startingTechs;         // Technologies known at game start
    public PolicyData[] startingPolicies;    // Initial policies or governments
    public CombatUnitData[] uniqueUnits;     // Civilizational unique units
    public BuildingData[] uniqueBuildings;   // Unique city or tile improvements

    [Header("Preferences & Modifiers")]
    public Biome[] climatePreferences;   // Preferred biomes for starting placement
    public float attackBonus;                // % bonus to all unit attacks
    public float defenseBonus;               // % bonus to all unit defenses
    public float movementBonus;              // % bonus to movement points
    public float foodModifier;              // New
    public float productionModifier;        // New
    public float goldModifier;              // New
    public float scienceModifier;           // New
    public float cultureModifier;           // New
    public float faithModifier;             // New

    [Header("Gameplay Flags")]
    public bool isTribe;                     // Limited to max 3 cities, starts at war
    public bool isCityState;                 // Single-city civ with diplomatic traits

    [Header("Music")]
    public MusicData musicData;

    [Header("City Models")]
    [Tooltip("City prefabs for different tech ages")]
    public CityPrefabByAge[] cityPrefabsByAge;

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