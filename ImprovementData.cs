// Assets/Scripts/Data/ImprovementData.cs
using UnityEngine;

[System.Serializable]
public class ImprovementUpgradeData
{
    [Header("Identity")]
    public string upgradeName;
    [Tooltip("Unique identifier for this upgrade. If empty, upgradeName will be used.")]
    public string upgradeId;
    public Sprite icon;
    [TextArea] public string description;

    [Header("Visual / Prefab")]
    [Tooltip("If true, this upgrade will alter the improvement's visual appearance (attach parts or replace the base prefab)")]
    public bool makesVisualChange = false;
    [Tooltip("Prefabs that will be instantiated as children of the improvement when this upgrade is applied (good for modular pieces like walls, moats, keeps)")]
    public GameObject[] attachPrefabs;
    [Tooltip("Local position offsets to apply to each corresponding entry in attachPrefabs. If empty or shorter than attachPrefabs, missing entries default to Vector3.zero.")]
    public Vector3[] attachLocalPositions;
    [Tooltip("Local rotation (Euler angles in degrees) to apply to each corresponding entry in attachPrefabs. If empty or shorter than attachPrefabs, missing entries default to (0,0,0).")]
    public Vector3[] attachLocalEulerAngles;
    [Tooltip("Optional: fully replace the improvement GameObject when this upgrade is applied. Use for complex visual reworks.")]
    public GameObject replacePrefab;

    [Header("Requirements")]
    [Tooltip("Technology required to unlock this upgrade")]
    public TechData requiredTech;
    [Tooltip("Culture required to unlock this upgrade")]
    public CultureData requiredCulture;
    [Tooltip("Gold cost to build this upgrade")]
    public int goldCost;
    [Tooltip("Resources required to build this upgrade")]
    public ResourceCost[] resourceCosts;

    [Header("Effects")]
    [Tooltip("Additional yields this upgrade provides per turn")]
    public int additionalFood;
    public int additionalProduction;
    public int additionalGold;
    public int additionalScience;
    public int additionalCulture;
    public int additionalFaith;
    [Tooltip("Increase to shelter capacity when this upgrade is applied (adds to ImprovementData.shelterCapacity)")]
    public int additionalShelterCapacity = 0;

    [Header("Defense Effects")]
    [Tooltip("Flat defense added to any unit standing on this tile when this upgrade is built")]
    public int defenseAdd = 0;
    [Tooltip("Percent (0.25 = +25%) multiplicative defense applied to any unit on this tile")]
    public float defensePct = 0f;
    [Tooltip("If true, this upgrade causes the tile to exert Zone of Control on adjacent tiles (like a watchtower or fortified position)")]
    public bool grantsZoneOfControl = false;
    [Tooltip("If true, enemy Zone of Control does not apply to this tile (acts as a safe corridor or fortified road)")]
    public bool blocksZoneOfControl = false;

    [Tooltip("If true, this upgrade can only be built once per improvement")]
    public bool uniqueUpgrade = true;

    /// <summary>
    /// Check if this upgrade can be built by the given civilization
    /// </summary>
    public bool CanBuild(Civilization civ)
    {
        if (civ == null) return false;

        // Check tech requirement
        if (requiredTech != null && !civ.researchedTechs.Contains(requiredTech))
            return false;

        // Check culture requirement
        if (requiredCulture != null && !civ.researchedCultures.Contains(requiredCulture))
            return false;

        // Check gold cost
        if (civ.gold < goldCost)
            return false;

        // Check resource costs
        if (resourceCosts != null)
        {
            foreach (var cost in resourceCosts)
            {
                if (cost.resource != null && civ.GetResourceCount(cost.resource) < cost.amount)
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Consume the required resources and gold from the civilization
    /// </summary>
    public bool ConsumeRequirements(Civilization civ)
    {
        if (!CanBuild(civ)) return false;

        // Deduct gold
        civ.gold -= goldCost;

        // Deduct resources
        if (resourceCosts != null)
        {
            foreach (var cost in resourceCosts)
            {
                if (cost.resource != null)
                    civ.ConsumeResource(cost.resource, cost.amount);
            }
        }

        return true;
    }
}

[System.Serializable]
public class ResourceCost
{
    public ResourceData resource;
    public int amount;
}

[CreateAssetMenu(fileName = "NewImprovementData", menuName = "Data/Improvement Data")]
public class ImprovementData : ScriptableObject
{
    [Header("Identity")]
    public string improvementName;
    public Sprite icon;
    [TextArea] public string description;

    [Header("Construction")]
    [Tooltip("How many work points required to finish")]
    public int workCost;
    [Tooltip("Gold consumed when this improvement build starts.")]
    public int buildGoldCost;
    [Tooltip("Resources consumed when this improvement build starts.")]
    public ResourceCost[] buildResourceCosts;
    [Tooltip("Prefab to show while building")]
    public GameObject constructionPrefab;
    [Tooltip("Prefab to spawn when complete")]
    public GameObject completePrefab;
    [Tooltip("Prefab to spawn if destroyed")]
    public GameObject destroyedPrefab;

    [Header("Dismantle")]
    public bool canBeDismantled = true;
    public int dismantleGoldRefund;
    public ResourceCost[] dismantleResourceRefunds;
    
    [Header("Shelter")]
    [Tooltip("If true, units on this tile are considered sheltered from weather (e.g., winter attrition)")]
    public bool isShelter = false;
    [Tooltip("How many units this shelter can store inside. 0 = cannot store (only shelters from weather).")]
    public int shelterCapacity = 0;
    
    [Header("Territory Requirements")]
    [Tooltip("Must be built within a city's direct influence")]
    public bool needsCity;
    [Tooltip("Can only be built on tiles controlled by the builder's civilization")]
    public bool requiresControlledTerritory;
    [Tooltip("Can be built in neutral/unclaimed territory")]
    public bool canBuildInNeutralTerritory;
    [Tooltip("Can be built in enemy territory")]
    public bool canBuildInEnemyTerritory;

    [Header("Location Requirements")]
    public Biome[] allowedBiomes;
    [Tooltip("Which underwater floor biomes this improvement can be placed on (checked against HexTileData.underwaterBiome). Leave empty to disallow underwater placement.")]
    public Biome[] allowedUnderwaterBiomes;
    public ResourceData[] requiredResources;

    [Header("Tech & Culture Requirements")]
    [Tooltip("All these techs must be researched to unlock this improvement")]
    public TechData[] requiredTechs;
    [Tooltip("All these cultures must be adopted to unlock this improvement")]
    public CultureData[] requiredCultures;

    [Header("Underwater")]
    [Tooltip("If true, this improvement is placed on the ocean floor (underwater layer). Bypasses the isLand check and validates against underwaterBiome instead.")]
    public bool isUnderwaterImprovement = false;

    [Header("Orbital")]
    [Tooltip("If true, this improvement is built in the orbit layer above a tile. Uses allowedBiomes to check the surface biome below. Requires the building unit to be in orbit or a spaceport on the tile.")]
    public bool isOrbitalImprovement = false;

    [Header("Yield Bonus (per turn)")]
    public int foodPerTurn;
    public int productionPerTurn;
    public int goldPerTurn;
    public int sciencePerTurn;
    public int culturePerTurn;
    public int policyPointsPerTurn;
    public int faithPerTurn;

    [Header("Movement")]
    [Tooltip("Flat movement bonus (can be fractional) applied to units moving on this tile (adds to their movement points).")]
    public float movementSpeedBonus = 0f;

    [Header("Road Settings")]
    [Tooltip("If true, this improvement is considered a road. Roads can connect cities and provide connected-city bonuses.")]
    public bool isRoad = false;
    [Tooltip("When two cities are connected by continuous roads (improvements with isRoad=true), each connected city gains these flat per-turn bonuses. Each ImprovementData can specify its own bonus magnitudes.")]
    public int connectedGoldPerTurn = 0;
    public int connectedProductionPerTurn = 0;
    public int connectedSciencePerTurn = 0;
    public int connectedCulturePerTurn = 0;
    public int connectedFaithPerTurn = 0;
    public int connectedPolicyPointsPerTurn = 0;

    [Header("Trap Settings")]
    [Tooltip("If true, this improvement acts as a trap that can trigger on unit entry.")]
    public bool isTrap = false;

    [Tooltip("Damage dealt when the trap triggers.")]
    public int trapDamage = 20;

    [Tooltip("If true, only affects animals. Otherwise uses trapAffectedCategories list.")]
    public bool trapAffectsAnimalsOnly = true;

    [Tooltip("Optional whitelist of affected categories if not animals-only.")]
    public CombatCategory[] trapAffectedCategories;

    [Tooltip("If true, immobilizes trapped unit for a number of turns.")]
    public bool trapImmobilize = false;

    [Tooltip("How many turns the unit is immobilized when the trap triggers.")]
    public int trapImmobilizeTurns = 1;

    [Tooltip("How many times this trap can trigger before being consumed.")]
    public int trapMaxTriggers = 1;

    [Tooltip("If true, remove the trap improvement from the tile once uses are depleted.")]
    public bool trapConsumeOnDeplete = true;

    [Tooltip("If true, units from the builder's civ do not trigger this trap.")]
    public bool trapFriendlySafe = true;

    [Header("Zone of Control")]
    [Tooltip("If true, this improvement causes the tile to exert Zone of Control on all adjacent tiles (e.g., a fort or watchtower).")]
    public bool grantsZoneOfControl = false;
    [Tooltip("If true, enemy Zone of Control does not apply to this tile even if an enemy unit is adjacent (e.g., a fortified road or military road).")]
    public bool blocksZoneOfControl = false;

    [Header("Upgrades")]
    [Tooltip("If set, this improvement replaces the listed older improvements once unlocked; those become obsolete in build menus.")]
    public ImprovementData[] replacesImprovements;
    
    [Tooltip("Available upgrades that can be built on this improvement")]
    public ImprovementUpgradeData[] availableUpgrades;

    /// <summary>
    /// Checks if the civilization meets this improvement's tech/culture requirements.
    /// </summary>
    public bool AreRequirementsMet(Civilization civ)
    {
        if (civ == null) return false;
        if (requiredTechs != null)
            foreach (var tech in requiredTechs)
                if (tech != null && !civ.researchedTechs.Contains(tech))
                    return false;
        if (requiredCultures != null)
            foreach (var culture in requiredCultures)
                if (culture != null && !civ.researchedCultures.Contains(culture))
                    return false;
        return true;
    }

    public bool CanPayBuildCosts(Civilization civ)
    {
        if (civ == null) return false;
        if (buildGoldCost > 0 && civ.gold < buildGoldCost) return false;
        if (buildResourceCosts != null)
        {
            foreach (var cost in buildResourceCosts)
            {
                if (cost == null || cost.resource == null || cost.amount <= 0) continue;
                if (civ.GetResourceCount(cost.resource) < cost.amount) return false;
            }
        }
        return true;
    }

    public bool ConsumeBuildCosts(Civilization civ)
    {
        if (!CanPayBuildCosts(civ)) return false;
        if (buildGoldCost > 0) civ.gold -= buildGoldCost;
        if (buildResourceCosts != null)
        {
            foreach (var cost in buildResourceCosts)
            {
                if (cost == null || cost.resource == null || cost.amount <= 0) continue;
                civ.ConsumeResource(cost.resource, cost.amount);
            }
        }
        return true;
    }

    public void RefundDismantleCosts(Civilization civ)
    {
        if (civ == null) return;
        if (dismantleGoldRefund > 0) civ.AddGold(dismantleGoldRefund);
        if (dismantleResourceRefunds != null)
        {
            foreach (var cost in dismantleResourceRefunds)
            {
                if (cost == null || cost.resource == null || cost.amount <= 0) continue;
                civ.AddResource(cost.resource, cost.amount);
            }
        }
    }
}