// Assets/Scripts/Data/ImprovementData.cs
using UnityEngine;

[System.Serializable]
public class ImprovementUpgradeData
{
    [Header("Identity")]
    public string upgradeName;
    public Sprite icon;
    [TextArea] public string description;

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

    [Tooltip("Prefab to spawn when this upgrade is built")]
    public GameObject upgradePrefab;
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
    [Tooltip("Prefab to show while building")]
    public GameObject constructionPrefab;
    [Tooltip("Prefab to spawn when complete")]
    public GameObject completePrefab;
    [Tooltip("Prefab to spawn if destroyed")]
    public GameObject destroyedPrefab;
    
    [Header("Shelter")]
    [Tooltip("If true, units on this tile are considered sheltered from weather (e.g., winter attrition)")]
    public bool isShelter = false;
    
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
    public ResourceData[] requiredResources;

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

    [Header("Upgrades")]
    [Tooltip("If set, this improvement replaces the listed older improvements once unlocked; those become obsolete in build menus.")]
    public ImprovementData[] replacesImprovements;
    
    [Tooltip("Available upgrades that can be built on this improvement")]
    public ImprovementUpgradeData[] availableUpgrades;
}