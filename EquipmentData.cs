using UnityEngine;
using GameCombat;
using UnityEngine.Serialization;

/// <summary>
/// Types of equipment that can be equipped by units
/// </summary>
public enum EquipmentType
{
    Weapon = 0,
    Shield = 1,
    Body = 2,
    Armor = Body, // Legacy serialized name/value; new assets should use Body.
    Miscellaneous = 3,
    Tool = 4,
    Utility = Tool,
    Head = 5
}

public enum EquipmentTarget
{
    CombatUnit,
    WorkerUnit,
    Both
}

public enum EquipmentRarity
{
    Common = 0,
    Uncommon = 1,
    Rare = 2,
    Elite = 3,
    Legendary = 4
}

[System.Serializable]
public struct UnitTypeFloat
{
    public CombatCategory unitType;
    public float value;
}

[System.Serializable]
public class SubstituteResourceGroup
{
    [Tooltip("Alternative resource costs for this requirement. Production consumes the first affordable alternative.")]
    public ResourceCost[] alternatives;
}

[CreateAssetMenu(fileName = "NewEquipmentData", menuName = "Data/Equipment Data")]
public class EquipmentData : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Stable, generator-owned identifier. Never derive save-game identity from the display name.")]
    public string stableId;
    public string equipmentName;
    public Sprite icon;
    [Tooltip("3D model that will be instantiated when this equipment is attached to a unit")]
    public GameObject equipmentPrefab;
    public TechAge equipmentAge;
    public EquipmentRarity rarity = EquipmentRarity.Common;
    public string[] gameplayTags;

    [Header("Type")]
    [Tooltip("Equipment slot this item will occupy")]
    public EquipmentType equipmentType = EquipmentType.Weapon;
    [Tooltip("Inventory category for HUD/resource grouping.")]
    public ResourceCategory category = ResourceCategory.Equipment;

    [Header("Projectile")]
    [Tooltip("Optional DEFAULT projectile data used when this equipment fires (can be overridden by unit's active projectile)")]
    public ProjectileData projectileData;
    [Tooltip("Category of projectile this weapon accepts (e.g., Arrow, Bolt, Bullet). Leave empty if weapon doesn't use projectiles.")]
    public GameCombat.ProjectileCategory projectileCategory = GameCombat.ProjectileCategory.Arrow;
    [Tooltip("If true, this weapon can fire projectiles and will use the unit's active projectile of the matching category")]
    public bool usesProjectiles = false;
    [Tooltip("Name of the child transform on the equipment prefab to use as the projectile spawn point. If empty, a sensible holder (projectileWeaponHolder or weaponHolder) will be used.")]
    public string projectileSpawnName = "ProjectileSpawn";
    [Header("Projectile Weapon Attachment Points")]
    [Tooltip("Name of the child transform on the equipment prefab where arrows are held while nocked/drawn.")]
    public string projectileNockName = "ProjectileNock";
    [Tooltip("If true and a spawn transform is found on the equipment prefab, use it instead of the unit's projectile spawn point.")]
    public bool useEquipmentProjectileSpawn = true;
    [Header("Grip & Usage")]
    [Tooltip("If true, this weapon requires two hands. The left grip (Grip_L) will be aligned to the unit's shield holder when equipped.")]
    public bool isTwoHanded = false;

    [Header("Targeting")]
    [Tooltip("Defines which unit types this equipment can be used by")]
    public EquipmentTarget targetUnit;

    [Header("Per-Unit-Type Modifiers")]
    [Tooltip("Additional flat attack bonus against specific unit types (additive, can be fractional)")]
    public UnitTypeFloat[] attackBonusAgainst;
    [Tooltip("Additional flat defense bonus against specific unit types (additive, can be fractional)")]
    public UnitTypeFloat[] defenseBonusAgainst;

    [Header("Targeted Combat Modifiers")]
    [Tooltip("Additional combat modifiers that only apply against matching enemy units or categories.")]
    public CombatTargetedModifier[] combatModifiersAgainst;

    [Header("Work / Tool Bonuses")]
    [Tooltip("If this equipment is a tool, grants additional work points to worker units (can be fractional)")]
    public float workPointsBonus;

    [Header("Requirements")]
    [Tooltip("Unit types that can equip this item")]
    public CombatCategory[] allowedUnitTypes;
    [Tooltip("Minimum unit level required to equip this item")]
    public int minimumLevel = 1;
    public TechData[] requiredTechs;
    [Tooltip("Cultures required to unlock this equipment (optional)")]
    public CultureData[] requiredCultures;
    [Tooltip("Operational building requirements for the city producing this equipment.")]
    public CityBuildingRequirement[] requiredBuildings;
    public int productionCost;
    [Tooltip("Gold price when this item is purchased instantly; not required for normal production.")]
    public int goldCost;
    [Tooltip("Material quantities consumed when production completes.")]
    [FormerlySerializedAs("resourceCosts")]
    public ResourceCost[] requiredResourceCosts;
    [Tooltip("Optional material quantities consumed each turn while this equipment is in use.")]
    [FormerlySerializedAs("resourceUpkeepPerTurn")]
    public ResourceCost[] upkeepPerTurn;
    [Tooltip("Optional groups of substitute materials. Every group must have at least one affordable alternative.")]
    public SubstituteResourceGroup[] substituteResourceGroups;
    [Tooltip("Manufacturing capability tags that the producing city must provide.")]
    public string[] requiredManufacturingCapabilities;

    [Header("Stat Bonuses")]
    [Tooltip("Flat attack bonus provided by this equipment (can be fractional)")]
    public float attackBonus;
    [Tooltip("Flat melee attack bonus provided by this equipment (can be fractional)")]
    public float meleeAttackBonus;
    [Tooltip("Flat ranged attack bonus provided by this equipment (can be fractional)")]
    public float rangedAttackBonus;
    [Tooltip("Flat city attack bonus provided by this equipment (can be fractional)")]
    public float cityAttackBonus;
    [Tooltip("Flat attack bonus against land/surface ground targets (can be fractional)")]
    public float groundAttackBonus;
    [Tooltip("Flat attack bonus against underwater targets (can be fractional)")]
    public float underwaterAttackBonus;
    [Tooltip("Flat attack bonus against air targets (can be fractional)")]
    public float airAttackBonus;
    [Tooltip("Flat attack bonus against space/orbit targets (can be fractional)")]
    public float spaceAttackBonus;
    [Tooltip("Flat defense bonus provided by this equipment (can be fractional)")]
    public float defenseBonus;
    [Tooltip("Flat health bonus provided by this equipment (can be fractional)")]
    public float healthBonus;
    [Tooltip("Flat movement bonus provided by this equipment (can be fractional)")]
    public float movementBonus;
    [Tooltip("Flat range bonus provided by this equipment (can be fractional)")]
    public float rangeBonus;
    [Tooltip("Flat sight/vision range provided by this equipment while equipped (can be fractional).")]
    public float sightRangeBonus;

    [Header("Conditional Stat Bonuses")]
    [Tooltip("Additional bonuses this equipment grants only when its location filters match.")]
    public EquipmentStatBonus[] conditionalStatBonuses;
    [Tooltip("Auras projected by units while this equipment is equipped.")]
    public UnitAuraBonus[] auraBonuses;
    [Tooltip("Passive abilities supplied only while this item is equipped.")]
    public AbilityData[] grantedAbilities;
    [Tooltip("Shared effects evaluated by the combat hit pipeline.")]
    public StatusEffectApplication[] onHitEffects;

    [Header("Per-Turn Yields (optional)")]
    [Tooltip("If set, a unit equipped with this item grants these additional per-turn yields to its owner.")]
    public int foodPerTurn;
    public int goldPerTurn;
    public int sciencePerTurn;
    public int culturePerTurn;
    public int faithPerTurn;
    public int policyPointsPerTurn;

    [Header("Weather Resistance")]
    [Tooltip("If true, this equipment reduces the amount of weather damage (e.g. winter attrition) taken by the unit wearing it.")]
    public bool reducesWeatherDamage = false;
    [Tooltip("Fraction of weather damage to reduce, from 0 to 1. "
           + "0.5 means this piece of equipment blocks 50% of incoming weather damage. "
           + "Reductions from all equipped items are added together, then capped at 1 (100%).")]
    [Range(0f, 1f)]
    public float weatherDamageReduction = 0f;

    [Header("Natural Disaster Resistance")]
    [Tooltip("Percent damage reduction from earthquakes while this equipment is worn. 0.10 = 10% less damage. Reductions from all equipped items are added together, then capped at 1 (100%).")]
    [Range(0f, 1f)] public float earthquakeDamageReductionPct = 0f;
    [Tooltip("Percent damage reduction from floods while this equipment is worn. 0.10 = 10% less damage.")]
    [Range(0f, 1f)] public float floodDamageReductionPct = 0f;
    [Tooltip("Percent damage reduction from storms while this equipment is worn. 0.10 = 10% less damage.")]
    [Range(0f, 1f)] public float stormDamageReductionPct = 0f;

    public float GetDisasterDamageReductionPct(NaturalDisasterType type) => type switch
    {
        NaturalDisasterType.Earthquake => earthquakeDamageReductionPct,
        NaturalDisasterType.Flood => floodDamageReductionPct,
        NaturalDisasterType.Storm => stormDamageReductionPct,
        _ => 0f
    };

    public bool IsValidForUnit(CombatUnit unit, Civilization civ = null)
    {
        if (unit == null) return false;

        // Check if the unit type matches any of the allowed unit types
        if (allowedUnitTypes != null && allowedUnitTypes.Length > 0)
        {
            bool typeAllowed = false;
            foreach (var allowedType in allowedUnitTypes)
            {
                if (unit.data.unitType == allowedType)
                {
                    typeAllowed = true;
                    break;
                }
            }
            if (!typeAllowed) return false;
        }

        // Check minimum level requirement
        if (unit.level < minimumLevel) return false;

        // Check tech requirements (if civ provided)
        if (civ != null && requiredTechs != null && requiredTechs.Length > 0)
        {
            foreach (var tech in requiredTechs)
            {
                if (tech == null || civ.researchedTechs == null || !civ.researchedTechs.Contains(tech))
                    return false;
            }
        }

        // Check culture requirements (if civ provided)
        if (civ != null && requiredCultures != null && requiredCultures.Length > 0)
        {
            foreach (var culture in requiredCultures)
            {
                if (culture == null || civ.researchedCultures == null || !civ.researchedCultures.Contains(culture))
                    return false;
            }
        }

        // Check if civilization has this equipment in inventory (if civ provided)
        if (civ != null && !civ.HasEquipment(this))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Check if a civilization can produce this equipment (has required resources, etc.)
    /// </summary>
    public bool CanBeProducedBy(Civilization civ)
    {
        if (civ == null) return false;

        // Check tech requirements
        if (requiredTechs != null && requiredTechs.Length > 0)
        {
            foreach (var tech in requiredTechs)
            {
                if (tech == null || civ.researchedTechs == null || !civ.researchedTechs.Contains(tech))
                    return false;
            }
        }

        // Check culture requirements
        if (requiredCultures != null && requiredCultures.Length > 0)
        {
            foreach (var culture in requiredCultures)
            {
                if (culture == null || civ.researchedCultures == null || !civ.researchedCultures.Contains(culture))
                    return false;
            }
        }

        if (!ResourceCost.CanAfford(civ, requiredResourceCosts, false)) return false;
        if (substituteResourceGroups != null)
        {
            foreach (var group in substituteResourceGroups)
                if (group != null && !ResourceCost.CanAfford(civ, group.alternatives, true)) return false;
        }
        return true;
    }

    public bool ConsumeProductionResources(Civilization civ)
    {
        if (!CanBeProducedBy(civ) || !ResourceCost.Consume(civ, requiredResourceCosts, false)) return false;
        if (substituteResourceGroups != null)
            foreach (var group in substituteResourceGroups)
                if (group != null && !ResourceCost.Consume(civ, group.alternatives, true)) return false;
        return true;
    }
}
