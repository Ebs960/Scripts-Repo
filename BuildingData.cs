// Assets/Scripts/Data/BuildingData.cs
using UnityEngine;

[CreateAssetMenu(menuName="Data/Building Data")]
public class BuildingData : ScriptableObject
{
    [Header("Identity")]
    public string buildingName;
    public Sprite icon;
    [TextArea] public string description;

    [Header("Prefab")]
    public GameObject buildingPrefab;

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

    public bool isScienceBuilding;

    public bool isFoodBuilding;

    public bool isProductionBuilding;

    public bool isGoldBuilding;

    public bool isCultureBuilding;
  


    [Header("Requirements")]
    public TechData[] requiredTechs;
    [Tooltip("All these cultures must be adopted to build this building")]
    public CultureData[] requiredCultures;
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
public class BuildingRequirements
{
    // Reserved for future grouping if needed
}

public static class BuildingDataExtensions
{
    /// <summary>
    /// Checks if the civilization meets this building's tech/culture requirements.
    /// Note: Resource/terrain/coastal checks are handled by City.CanProduce and related logic.
    /// </summary>
    public static bool AreRequirementsMet(this BuildingData building, Civilization civ)
    {
        if (building == null || civ == null) return false;

        // Tech requirements
        if (building.requiredTechs != null && building.requiredTechs.Length > 0)
        {
            foreach (var tech in building.requiredTechs)
            {
                if (tech == null) continue;
                if (!civ.researchedTechs.Contains(tech))
                    return false;
            }
        }

        // Culture requirements
        if (building.requiredCultures != null && building.requiredCultures.Length > 0)
        {
            foreach (var culture in building.requiredCultures)
            {
                if (culture == null) continue;
                if (!civ.researchedCultures.Contains(culture))
                    return false;
            }
        }

        return true;
    }
}

[System.Serializable]
public class EquipmentProduction
{
    [Tooltip("The type of equipment produced")]
    public EquipmentData equipment;
    
    [Tooltip("The quantity produced when the building is completed")]
    public int quantity = 1;
    
    [Tooltip("Optional override of production cost (production points) for this building's produced equipment. If 0, uses EquipmentData.productionCost.")]
    public int productionCostOverride = 0;

    [Tooltip("Optional override of gold cost for instant buy of this produced equipment. If 0, no gold cost is applied.")]
    public int goldCostOverride = 0;
    
    [Tooltip("If true, this equipment is granted to the civilization immediately when the building completes instead of being enqueued in the city's production queue.")]
    public bool produceImmediately = false;
}