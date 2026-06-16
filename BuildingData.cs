// Assets/Scripts/Data/BuildingData.cs
using UnityEngine;

[System.Serializable]
public struct BuildingVisualOverride
{
    [Tooltip("Civilization that uses this building visual override.")]
    public CivData civ;

    [Tooltip("Override prefab for this civilization. Leave empty to use the default building prefab.")]
    public GameObject buildingPrefab;
}

[CreateAssetMenu(menuName="Data/Building Data")]
public class BuildingData : ScriptableObject
{
    [Header("Identity")]
    public string buildingName;
    public Sprite icon;
    [TextArea] public string description;

    [Header("Prefab")]
    public GameObject buildingPrefab;
    [Tooltip("Optional per-civilization prefab overrides for this building.")]
    public BuildingVisualOverride[] civVisualOverrides;

    [Header("Replacement (Upgrade)")]
    [Tooltip("If non-null, this building will replace the specified older building when completed")]
    public BuildingData replacesBuilding;

    [Header("Construction")]
    [Tooltip("Production cost in city build points")]
    public int productionCost;
    [Tooltip("Gold cost for instant buy")]
    public int goldCost;
    [Tooltip("Gold consumed when this building is queued or completed through normal construction.")]
    public int buildGoldCost;
    [Tooltip("Resources consumed when this building is queued through normal construction.")]
    public ResourceCost[] buildResourceCosts;
    public bool requiresAdjacentTile;    // e.g., walls, farms
    [Tooltip("Must have these resources in empire stockpile")]
    public ResourceData[] requiredResources;
    [Tooltip("City must control at least one tile of these biomes")]
    public Biome[] requiredTerrains;
    public Biome[] allowedBiomes;

    [Header("Dismantle")]
    public bool canBeDismantled = true;
    public int dismantleGoldRefund;
    public ResourceCost[] dismantleResourceRefunds;

    [Header("Equipment Production")]
    [Tooltip("Equipment produced when this building is completed (one-time, not recurring)")]
    public EquipmentProduction[] equipmentProduction;

    [Header("Projectile Production")]
    [Tooltip("Projectiles produced when this building is completed (one-time, not recurring)")]
    public ProjectileProduction[] projectileProduction;

    [Header("Special Flags")]
    [Tooltip("Grants harbor functionality (lets city build ships/subs)")]
    public bool providesHarbor;
    [Tooltip("Grants airport functionality for long-distance air trade routes.")]
    public bool providesAirport;
    [Tooltip("Grants spaceport functionality for planet/moon/orbital/interplanetary trade routes.")]
    public bool providesSpaceport;

    public bool isScienceBuilding;

    public bool isFoodBuilding;

    public bool isProductionBuilding;

    public bool isGoldBuilding;

    public bool isCultureBuilding;

    public bool isFaithBuilding;
    
    [Tooltip("Marks this building as a perimeter wall (special handling in City/Tile logic)")]
    public bool isPerimeterWall;
  


    [Header("Requirements")]
    public TechData[] requiredTechs;
    [Tooltip("All these cultures must be adopted to build this building")]
    public CultureData[] requiredCultures;
    public int requiredPopulation;
    [Tooltip("One of these governments must be active to build this building (optional)")]
    public GovernmentData[] requiredGovernments;
    [Tooltip("All of these policies must be active to build this building (optional)")]
    public PolicyData[] requiredPolicies;
    [Tooltip("Operational building prerequisites that may be checked in this city, the capital, other cities, or across all cities.")]
    public CityBuildingRequirement[] requiredBuildings;

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

    [Header("City Yield Modifiers")]
    [Tooltip("Percent modifier applied to this city's food output while this building is operational (0.05 = +5%).")]
    public float cityFoodModifier;
    [Tooltip("Percent modifier applied to this city's production output while this building is operational (0.05 = +5%).")]
    public float cityProductionModifier;
    [Tooltip("Percent modifier applied to this city's gold output while this building is operational (0.05 = +5%).")]
    public float cityGoldModifier;
    [Tooltip("Percent modifier applied to this city's science output while this building is operational (0.05 = +5%).")]
    public float cityScienceModifier;
    [Tooltip("Percent modifier applied to this city's culture output while this building is operational (0.05 = +5%).")]
    public float cityCultureModifier;
    [Tooltip("Percent modifier applied to this city's policy point output while this building is operational (0.05 = +5%).")]
    public float cityPolicyPointsModifier;
    [Tooltip("Percent modifier applied to this city's faith output while this building is operational (0.05 = +5%).")]
    public float cityFaithModifier;

    [Header("Tile Yield Bonuses")]
    [Tooltip("Per-tile yield bonuses applied to matching tiles worked by this city while this building is operational.")]
    public TileYieldBonus[] tileYieldBonuses;

    [Header("Building Yield Bonuses")]
    [Tooltip("Per-building yield/stat bonuses this building grants while operational. Can target exact buildings or building categories.")]
    public BuildingYieldBonus[] buildingBonuses;

    [Header("Resource Production (per turn)")]
    [Tooltip("Resources this building adds to the civilization stockpile each turn while present in a city.")]
    public ResourceCost[] resourceProductionPerTurn;

    [Header("Resource Upkeep (per turn)")]
    [Tooltip("Resources this building consumes from the civilization stockpile each turn.")]
    public ResourceCost[] resourceUpkeepPerTurn;
    [Tooltip("What happens when the civilization cannot pay this building's per-turn upkeep.")]
    public ResourceUpkeepFailureBehavior upkeepFailureBehavior = ResourceUpkeepFailureBehavior.Deactivate;
    [Tooltip("Applied to this building's numeric output when upkeep failure uses Debuff mode.")]
    [Range(0f, 1f)]
    public float upkeepFailureDebuffMultiplier = 0.5f;

    [Header("Other Effects")]
    public float defenseBonus;
    public float happinessBonus;
    [Tooltip("Flat max order added to this city. Order reduces rebellion/crime pressure and trade route raid risk.")]
    public float orderBonus;
    [Tooltip("Percent max-happiness modifier for this city while this building is operational (0.15 = +15%).")]
    public float cityHappinessModifier;
    [Tooltip("Percent max-order modifier for this city while this building is operational (0.15 = +15%).")]
    public float cityOrderModifier;
    [Tooltip("Per-unit stat bonuses granted by this building when present (e.g., healing speed).")]
    public UnitStatBonus[] unitBonuses;
    [Tooltip("Unit-training production modifiers granted by this building. Can affect all units, one combat/worker unit, or a combat category such as Spearman.")]
    public UnitProductionModifier[] unitProductionModifiers;
    [Tooltip("Per-worker stat bonuses granted by this building when present (e.g., healing speed).")]
    public WorkerUnitStatBonus[] workerBonuses;
    [Tooltip("Disease modifiers granted by this building when present.")]
    public DiseaseModifierBonus[] diseaseBonuses;
    public AttritionModifierBonus[] attritionBonuses;
    [Tooltip("Reduces the percent of herd animals lost to starvation when this building/structure is present (e.g. 0.05 = -5 percentage points)")]
    public float herdStarvationPercentReduction = 0f;
    [Tooltip("Per-herd per-turn yield bonuses granted by this building/structure (can filter by animal species).")]
    public HerdYieldBonus[] herdYieldBonuses;
    [Header("Herd / Nomad Options")]
    [Tooltip("If true, this building may be constructed by/for herds (mobile structures)")]
    public bool buildableByHerd = false;
    [Tooltip("If >0, increases herd food storage capacity when this building is present for a herd")]
    public int herdStorageBonus = 0;

    public GameObject GetBuildingPrefab(Civilization civ)
    {
        if (civVisualOverrides != null && civ != null && civ.civData != null)
        {
            for (int i = 0; i < civVisualOverrides.Length; i++)
            {
                if (civVisualOverrides[i].civ == civ.civData && civVisualOverrides[i].buildingPrefab != null)
                    return civVisualOverrides[i].buildingPrefab;
            }
        }

        return buildingPrefab;
    }
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

        // Government requirement (any-of)
        if (building.requiredGovernments != null && building.requiredGovernments.Length > 0)
        {
            bool govOk = false;
            foreach (var gov in building.requiredGovernments)
            {
                if (gov == null) continue;
                if (civ.currentGovernment == gov) { govOk = true; break; }
            }
            if (!govOk) return false;
        }

        // Policy requirements (all-of)
        if (building.requiredPolicies != null && building.requiredPolicies.Length > 0)
        {
            foreach (var pol in building.requiredPolicies)
            {
                if (pol == null) continue;
                if (!civ.activePolicies.Contains(pol)) return false;
            }
        }

        return true;
    }

    public static bool CanPayBuildCosts(this BuildingData building, Civilization civ)
    {
        if (building == null || civ == null) return false;
        if (building.buildGoldCost > 0 && civ.gold < building.buildGoldCost) return false;
        if (building.buildResourceCosts != null)
        {
            foreach (var cost in building.buildResourceCosts)
            {
                if (cost == null || cost.resource == null || cost.amount <= 0) continue;
                if (civ.GetResourceCount(cost.resource) < cost.amount) return false;
            }
        }
        return true;
    }

    public static bool ConsumeBuildCosts(this BuildingData building, Civilization civ)
    {
        if (!building.CanPayBuildCosts(civ)) return false;
        if (building.buildGoldCost > 0) civ.gold -= building.buildGoldCost;
        if (building.buildResourceCosts != null)
        {
            foreach (var cost in building.buildResourceCosts)
            {
                if (cost == null || cost.resource == null || cost.amount <= 0) continue;
                civ.ConsumeResource(cost.resource, cost.amount);
            }
        }
        return true;
    }

    public static void RefundDismantleCosts(this BuildingData building, Civilization civ)
    {
        if (building == null || civ == null) return;
        if (building.dismantleGoldRefund > 0) civ.AddGold(building.dismantleGoldRefund);
        if (building.dismantleResourceRefunds != null)
        {
            foreach (var cost in building.dismantleResourceRefunds)
            {
                if (cost == null || cost.resource == null || cost.amount <= 0) continue;
                civ.AddResource(cost.resource, cost.amount);
            }
        }
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

[System.Serializable]
public class ProjectileProduction
{
    [Tooltip("The type of projectile produced")]
    public GameCombat.ProjectileData projectile;

    [Tooltip("The quantity produced when the building is completed")]
    public int quantity = 1;

    [Tooltip("Optional override of production cost (production points) for this building's produced projectiles. If 0, uses ProjectileData.productionCost.")]
    public int productionCostOverride = 0;

    [Tooltip("Optional override of gold cost for instant buy of this produced projectile. If 0, no gold cost is applied.")]
    public int goldCostOverride = 0;

    [Tooltip("If true, these projectiles are granted to the civilization immediately when the building completes instead of being enqueued in the city's production queue.")]
    public bool produceImmediately = false;
}
