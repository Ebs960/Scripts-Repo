// Assets/Scripts/Data/ImprovementData.cs
using UnityEngine;

[System.Serializable]
public struct ImprovementUpgradeVisualOverride
{
    [Tooltip("Civilization that uses this improvement option visual override.")]
    public CivData civ;
    [Tooltip("Prefabs to attach for this civilization. Leave empty to use the default attach prefabs.")]
    public GameObject[] attachPrefabs;
    [Tooltip("Replacement prefab for this civilization. Leave empty to use the default replacement prefab.")]
    public GameObject replacePrefab;
}

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
    [Tooltip("Optional per-civilization visual overrides for this improvement option/upgrade.")]
    public ImprovementUpgradeVisualOverride[] civVisualOverrides;

    [Header("Requirements")]
    [Tooltip("Technology required to unlock this upgrade")]
    public TechData requiredTech;
    [Tooltip("Culture required to unlock this upgrade")]
    public CultureData requiredCulture;
    [Tooltip("Gold cost to build this upgrade")]
    public int goldCost;
    [Tooltip("Resources required to build this option/upgrade")]
    public ResourceCost[] resourceCosts;
    [Tooltip("If true, only one valid entry in Resource Costs must be paid instead of every listed cost. Example: 5 Copper OR 2 Bronze.")]
    public bool hasSubstituteCosts = false;

    [Header("Effects")]
    [Tooltip("Additional yields this upgrade provides per turn")]
    public int additionalFood;
    public int additionalProduction;
    public int additionalGold;
    public int additionalScience;
    public int additionalCulture;
    [Tooltip("Additional policy points this upgrade provides per turn")]
    public int additionalPolicyPoints;
    public int additionalFaith;
    [Tooltip("Increase to shelter capacity when this upgrade is applied (adds to ImprovementData.shelterCapacity)")]
    public int additionalShelterCapacity = 0;

    [Header("Rural Specialist Slots")]
    [Tooltip("Specialist jobs added by this improvement upgrade.")]
    public SpecialistSlotDefinition[] addedRuralSpecialistSlots;

    [Header("Defense Effects")]
    [Tooltip("Flat defense added to any unit standing on this tile when this upgrade is built")]
    public int defenseAdd = 0;
    [Tooltip("Percent (0.25 = +25%) multiplicative defense applied to any unit on this tile")]
    public float defensePct = 0f;
    [Tooltip("Flat attack added to this improvement if it is a fort.")]
    public int fortAttackAdd = 0;
    [Tooltip("Percent (0.25 = +25%) multiplicative attack bonus applied to this improvement if it is a fort.")]
    public float fortAttackPct = 0f;
    [Tooltip("Flat defense added to this improvement if it is a fort.")]
    public int fortDefenseAdd = 0;
    [Tooltip("Percent (0.25 = +25%) multiplicative defense bonus applied to this improvement if it is a fort.")]
    public float fortDefensePct = 0f;
    [Tooltip("Additional max hit points added to this improvement if it is a fort.")]
    public int additionalFortHitPoints = 0;
    [Tooltip("If true, this upgrade causes the tile to exert Zone of Control on adjacent tiles (like a watchtower or fortified position)")]
    public bool grantsZoneOfControl = false;
    [Tooltip("If true, enemy Zone of Control does not apply to this tile (acts as a safe corridor or fortified road)")]
    public bool blocksZoneOfControl = false;

    [Tooltip("If true, this upgrade can only be built once per improvement")]
    public bool uniqueUpgrade = true;

    [Header("Option / Upgrade Pathing")]
    [Tooltip("If true, this entry behaves like a swappable improvement option; building it can replace the current option in the same slot.")]
    public bool isSwitchableOption = false;
    [Tooltip("Optional slot/category for option choices, such as crop_type, farm_tools, farm_labor, or farm_addons.")]
    public string upgradeSlot = "";
    [Tooltip("Optional path within a slot, such as sickles or plows. Different paths in the same exclusive group lock each other out unless this is a switchable option.")]
    public string upgradePath = "";
    [Tooltip("Optional exclusive group. Built upgrades in the same group but a different path prevent this upgrade from being built.")]
    public string exclusiveGroupId = "";
    [Tooltip("Tier/order within a path. Higher tiers can supersede lower tiers without requiring them first.")]
    public int pathTier = 0;
    [Tooltip("If true, multiple upgrades can coexist in this slot.")]
    public bool allowMultipleInSlot = false;
    [Tooltip("Maximum upgrades allowed in this slot when allowMultipleInSlot is true. 0 or lower means unlimited.")]
    public int maxUpgradesInSlot = 0;
    [Tooltip("If true, building this upgrade removes lower-tier upgrades in the same slot/path from active persisted effects.")]
    public bool supersedesLowerTiersInPath = true;
    [Tooltip("Specific upgrade ids/names that prevent this upgrade from being built when already present.")]
    public string[] blockedByUpgradeIds;
    [Tooltip("Specific upgrade ids/names that this upgrade blocks after being built.")]
    public string[] blocksUpgradeIds;

    public string GetUpgradeKey()
    {
        return !string.IsNullOrEmpty(upgradeId) ? upgradeId : upgradeName;
    }

    public GameObject GetReplacePrefab(Civilization civ)
    {
        if (civVisualOverrides != null && civ != null && civ.civData != null)
        {
            foreach (var visualOverride in civVisualOverrides)
            {
                if (visualOverride.civ == civ.civData && visualOverride.replacePrefab != null)
                    return visualOverride.replacePrefab;
            }
        }

        return replacePrefab;
    }

    public GameObject[] GetAttachPrefabs(Civilization civ)
    {
        if (civVisualOverrides != null && civ != null && civ.civData != null)
        {
            foreach (var visualOverride in civVisualOverrides)
            {
                if (visualOverride.civ == civ.civData && visualOverride.attachPrefabs != null && visualOverride.attachPrefabs.Length > 0)
                    return visualOverride.attachPrefabs;
            }
        }

        return attachPrefabs;
    }

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

        if (!ResourceCost.CanAfford(civ, resourceCosts, hasSubstituteCosts))
            return false;

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

        if (!ResourceCost.Consume(civ, resourceCosts, hasSubstituteCosts))
            return false;

        return true;
    }
}

[System.Serializable]
public class ResourceCost
{
    public ResourceData resource;
    public int amount;

    public static bool CanAfford(Civilization civ, ResourceCost[] costs, bool hasSubstituteCosts = false)
    {
        if (civ == null) return false;
        if (costs == null || costs.Length == 0) return true;

        bool sawValidCost = false;
        foreach (var cost in costs)
        {
            if (cost == null || cost.resource == null || cost.amount <= 0) continue;
            sawValidCost = true;
            bool canPayThisCost = civ.GetResourceCount(cost.resource) >= cost.amount;
            if (hasSubstituteCosts && canPayThisCost) return true;
            if (!hasSubstituteCosts && !canPayThisCost) return false;
        }

        return !hasSubstituteCosts || !sawValidCost;
    }

    public static bool Consume(Civilization civ, ResourceCost[] costs, bool hasSubstituteCosts = false)
    {
        if (!CanAfford(civ, costs, hasSubstituteCosts)) return false;
        if (costs == null) return true;

        foreach (var cost in costs)
        {
            if (cost == null || cost.resource == null || cost.amount <= 0) continue;
            if (!hasSubstituteCosts || civ.GetResourceCount(cost.resource) >= cost.amount)
            {
                civ.ConsumeResource(cost.resource, cost.amount);
                if (hasSubstituteCosts) break;
            }
        }

        return true;
    }

    public static bool HasRequiredResources(Civilization civ, ResourceData[] resources, bool hasSubstituteResources = false)
    {
        if (civ == null) return false;
        if (resources == null || resources.Length == 0) return true;

        bool sawValidResource = false;
        foreach (var resource in resources)
        {
            if (resource == null) continue;
            sawValidResource = true;
            bool hasResource = civ.GetResourceCount(resource) > 0;
            if (hasSubstituteResources && hasResource) return true;
            if (!hasSubstituteResources && !hasResource) return false;
        }

        return !hasSubstituteResources || !sawValidResource;
    }

    public static string FormatCosts(ResourceCost[] costs, bool hasSubstituteCosts = false)
    {
        if (costs == null || costs.Length == 0) return string.Empty;
        var parts = new System.Collections.Generic.List<string>();
        foreach (var cost in costs)
        {
            if (cost == null || cost.resource == null || cost.amount <= 0) continue;
            parts.Add($"{cost.resource.resourceName}: {cost.amount}");
        }

        if (parts.Count == 0) return string.Empty;
        return string.Join(hasSubstituteCosts ? " OR " : ", ", parts);
    }
}

[System.Serializable]
public struct ImprovementVisualOverride
{
    [Tooltip("Civilization that uses this improvement visual override.")]
    public CivData civ;
    public GameObject constructionPrefab;
    public GameObject completePrefab;
    public GameObject destroyedPrefab;
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
    [Tooltip("Optional per-civilization prefab overrides for this improvement.")]
    public ImprovementVisualOverride[] civVisualOverrides;
    [Tooltip("If true, only one valid entry in Build Resource Costs must be paid instead of every listed cost.")]
    public bool hasSubstituteBuildCosts = false;

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
    [Tooltip("If true, only one resource in Required Resources must be present instead of every listed resource.")]
    public bool hasSubstituteRequiredResources = false;

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

    [Header("Missile Silo")]
    [Tooltip("If true, this improvement acts as a missile silo and can store and launch missiles. The missile inventory is managed by MissileManager keyed on tile index.")]
    public bool isMissileSilo = false;
    [Tooltip("Maximum number of missiles this silo can store.")]
    [Range(0, 50)]
    public int siloMissileCapacity = 5;
    [Tooltip("Specific missile types this silo is allowed to store. Leave empty to allow all types.")]
    public MissileData[] allowedMissileTypes;

    [Header("Yield Bonus (per turn)")]
    public int foodPerTurn;
    public int productionPerTurn;
    public int goldPerTurn;
    public int sciencePerTurn;
    public int culturePerTurn;
    public int policyPointsPerTurn;
    public int faithPerTurn;

    [Header("Rural Specialist Slots")]
    [Tooltip("Specialist jobs created by the base improvement. These are worked by Rural Specialists.")]
    public SpecialistSlotDefinition[] ruralSpecialistSlots;

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

    [Header("Fort Settings")]
    [Tooltip("If true, this improvement is a fortification that can attack nearby enemies and can be neutralized by damage.")]
    public bool isFort = false;
    [Tooltip("Base attack used when the fort fires at enemies.")]
    public int fortAttack = 0;
    [Tooltip("Base defense used when incoming damage is resolved against the fort.")]
    public int fortDefense = 0;
    [Tooltip("Maximum hit points before this fort is neutralized. Neutralized forts cannot fire or store/garrison units.")]
    public int fortHitPoints = 100;
    [Tooltip("Maximum tile range this fort can fire.")]
    public int fortAttackRange = 1;
    [Tooltip("If true, the fort automatically fires at enemy units that enter range.")]
    public bool fortAutoFireOnEnemyEntry = true;
    [Tooltip("How many times this fort can fire each turn.")]
    public int fortAttacksPerTurn = 1;

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


    public GameObject GetConstructionPrefab(Civilization civ) => GetVisualPrefab(civ, VisualPrefabKind.Construction);
    public GameObject GetCompletePrefab(Civilization civ) => GetVisualPrefab(civ, VisualPrefabKind.Complete);
    public GameObject GetDestroyedPrefab(Civilization civ) => GetVisualPrefab(civ, VisualPrefabKind.Destroyed);

    private enum VisualPrefabKind { Construction, Complete, Destroyed }

    private GameObject GetVisualPrefab(Civilization civ, VisualPrefabKind kind)
    {
        if (civVisualOverrides != null && civ != null && civ.civData != null)
        {
            foreach (var visualOverride in civVisualOverrides)
            {
                if (visualOverride.civ != civ.civData) continue;
                GameObject overridePrefab = null;
                switch (kind)
                {
                    case VisualPrefabKind.Construction:
                        overridePrefab = visualOverride.constructionPrefab;
                        break;
                    case VisualPrefabKind.Complete:
                        overridePrefab = visualOverride.completePrefab;
                        break;
                    case VisualPrefabKind.Destroyed:
                        overridePrefab = visualOverride.destroyedPrefab;
                        break;
                }
                if (overridePrefab != null) return overridePrefab;
            }
        }

        switch (kind)
        {
            case VisualPrefabKind.Construction:
                return constructionPrefab;
            case VisualPrefabKind.Complete:
                return completePrefab;
            case VisualPrefabKind.Destroyed:
                return destroyedPrefab;
            default:
                return null;
        }
    }

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
        if (!ResourceCost.CanAfford(civ, buildResourceCosts, hasSubstituteBuildCosts)) return false;
        return true;
    }

    public bool ConsumeBuildCosts(Civilization civ)
    {
        if (!CanPayBuildCosts(civ)) return false;
        if (buildGoldCost > 0) civ.gold -= buildGoldCost;
        if (!ResourceCost.Consume(civ, buildResourceCosts, hasSubstituteBuildCosts)) return false;
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
