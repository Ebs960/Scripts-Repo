using UnityEngine;

/// <summary>
/// Types of equipment that can be equipped by units
/// </summary>
public enum EquipmentType
{
    Weapon,
    Shield,
    Armor,
    Miscellaneous
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

    [Header("Requirements")]
    [Tooltip("Unit types that can equip this item")]
    public CombatCategory[] allowedUnitTypes;
    [Tooltip("Minimum unit level required to equip this item")]
    public int minimumLevel = 1;
    public TechData[] requiredTechs;
    public int productionCost;

    [Header("Stat Bonuses")]
    public int attackBonus;
    public int defenseBonus;
    public int healthBonus;
    public int movementBonus;
    public int rangeBonus;
    public int attackPointsBonus;

    public bool IsValidForUnit(CombatUnit unit)
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

        // Add any additional validation logic here
        
        return true;
    }
} 