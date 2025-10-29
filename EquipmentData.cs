using UnityEngine;
using GameCombat;

/// <summary>
/// Types of equipment that can be equipped by units
/// </summary>
public enum EquipmentType
{
    Weapon,
    Shield,
    Armor,
    Miscellaneous,
    Tool
}

public enum EquipmentTarget
{
    CombatUnit,
    WorkerUnit,
    Both
}

[System.Serializable]
public struct UnitTypeFloat
{
    public CombatCategory unitType;
    public float value;
}

[CreateAssetMenu(fileName = "NewEquipmentData", menuName = "Data/Equipment Data")]
public class EquipmentData : ScriptableObject
{
    [Header("Identity")]
    public string equipmentName;
    public Sprite icon;
    [Tooltip("3D model that will be instantiated when this equipment is attached to a unit")]
    public GameObject equipmentPrefab;

    [Header("Type")]
    [Tooltip("Equipment slot this item will occupy")]
    public EquipmentType equipmentType = EquipmentType.Weapon;
    
    [Header("Projectile")]
    [Tooltip("Optional DEFAULT projectile data used when this equipment fires (can be overridden by unit's active projectile)")]
    public ProjectileData projectileData;
    [Tooltip("Category of projectile this weapon accepts (e.g., Arrow, Bolt, Bullet). Leave empty if weapon doesn't use projectiles.")]
    public GameCombat.ProjectileCategory projectileCategory = GameCombat.ProjectileCategory.Arrow;
    [Tooltip("If true, this weapon can fire projectiles and will use the unit's active projectile of the matching category")]
    public bool usesProjectiles = false;
    [Tooltip("Name of the child transform on the equipment prefab to use as the projectile spawn point. If empty, a sensible holder (projectileWeaponHolder or weaponHolder) will be used.")]
    public string projectileSpawnName = "ProjectileSpawn";
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
    public int productionCost;

    [Header("Stat Bonuses")]
    [Tooltip("Flat attack bonus provided by this equipment (can be fractional)")]
    public float attackBonus;
    [Tooltip("Flat defense bonus provided by this equipment (can be fractional)")]
    public float defenseBonus;
    [Tooltip("Flat health bonus provided by this equipment (can be fractional)")]
    public float healthBonus;
    [Tooltip("Flat movement bonus provided by this equipment (can be fractional)")]
    public float movementBonus;
    [Tooltip("Flat range bonus provided by this equipment (can be fractional)")]
    public float rangeBonus;
    [Tooltip("Flat attack points bonus provided by this equipment (can be fractional)")]
    public float attackPointsBonus;

    [Header("Per-Turn Yields (optional)")]
    [Tooltip("If set, a unit equipped with this item grants these additional per-turn yields to its owner.")]
    public int foodPerTurn;
    public int goldPerTurn;
    public int sciencePerTurn;
    public int culturePerTurn;
    public int faithPerTurn;
    public int policyPointsPerTurn;

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

        // Check if civilization has enough gold for production cost
        if (productionCost > 0 && civ.gold < productionCost)
            return false;

        // TODO: Add resource requirement checks when resource system is implemented
        // if (requiredResources != null && requiredResources.Length > 0)
        // {
        //     foreach (var resource in requiredResources)
        //     {
        //         if (!civ.HasResource(resource, 1))
        //             return false;
        //     }
        // }

        return true;
    }
} 