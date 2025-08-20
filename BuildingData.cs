// Assets/Scripts/Data/BuildingData.cs
using UnityEngine;

[CreateAssetMenu(menuName="CivGame/Building Data")]
public class BuildingData : ScriptableObject
{
    [Header("Identity")]
    public string buildingName;
    public Sprite icon;
    [TextArea] public string description;

    [Header("Prefab")]
    public GameObject buildingPrefab;
    public GameObject prefab;
    public GameObject constructionPrefab;

    [Header("Replacement (Upgrade)")]
    [Tooltip("If non-null, this building will replace the specified older building when completed")]
    public BuildingData replacesBuilding;

    [Header("Construction")]
    [Tooltip("Production cost in city build points")]
    public int productionCost;
    [Tooltip("Gold cost for instant buy")]
    public int goldCost;
    public bool requiresAdjacentTile;    // e.g., walls, farms
    [Tooltip("Must have these resources in empire stockpile")]
    public ResourceData[] requiredResources;
    [Tooltip("City must control at least one tile of these biomes")]
    public Biome[] requiredTerrains;
    public Biome[] allowedBiomes;

    [Header("Equipment Production")]
    [Tooltip("Equipment produced when this building is completed (one-time, not recurring)")]
    public EquipmentProduction[] equipmentProduction;

    [Header("Special Flags")]
    [Tooltip("Grants harbor functionality (lets city build ships/subs)")]
    public bool providesHarbor;
    [Tooltip("Is this building a market?")]
    public bool isMarket;
    [Tooltip("Is this building a bank?")]
    public bool isBank;
    [Tooltip("Is this building a mill?")]
    public bool isMill;
    [Tooltip("Is this building a factory?")]
    public bool isFactory;
    [Tooltip("Is this building a granary?")]
    public bool isGranary;
    [Tooltip("Is this building a farm?")]
    public bool isFarm;

    [Header("Requirements")]
    public TechData[] requiredTechs;
    public int requiredPopulation;

    [Header("Building Limits")]
    [Tooltip("Maximum number of this building type a civilization can have (-1 = unlimited)")]
    public int buildingLimit = -1;
    [Tooltip("Unique identifier for buildings that share the same limit (leave empty for individual limits)")]
    public string limitCategory = "";
    [Tooltip("Maximum number of this building per city (1 = one per city, -1 = unlimited per city)")]
    public int perCityLimit = 1;

    [Header("Yields (per turn)")]
    public int foodPerTurn;
    public int productionPerTurn;
    public int goldPerTurn;
    public int sciencePerTurn;
    public int culturePerTurn;
    public int policyPointsPerTurn;
    public int faithPerTurn;

    [Header("Other Effects")]
    public float defenseBonus;
    public float happinessBonus;
}

[System.Serializable]
public class EquipmentProduction
{
    [Tooltip("The type of equipment produced")]
    public EquipmentData equipment;
    
    [Tooltip("The quantity produced when the building is completed")]
    public int quantity = 1;
}