using UnityEngine;

[CreateAssetMenu(fileName = "NewAbilityData", menuName = "Data/Ability Data")]
public class AbilityData : ScriptableObject
{
    public string abilityName;
    public Sprite icon;
    [TextArea] public string description;
    public int requiredLevel;

    [Header("Modifiers")]
    public int attackModifier;
    public int defenseModifier;
    public float damageMultiplier = 1f;

    [Header("Combat Target Filters")]
    [Tooltip("If set, attack/defense/damage portions of this ability only apply against this specific enemy combat unit.")]
    public CombatUnitData targetUnit;
    [Tooltip("If set, attack/defense/damage portions of this ability only apply against this specific enemy worker unit.")]
    public WorkerUnitData targetWorker;
    [Tooltip("If enabled, attack/defense/damage portions of this ability only apply against enemy combat units in the selected category.")]
    public bool useTargetUnitCategoryFilter = false;
    public CombatCategory targetUnitCategory;

    // New modifiers
    public int healthModifier;
    public int rangeModifier;

    [Header("Location Filters")]
    public BoolRequirement cityRequirement;
    public bool useBiomeFilter;
    public Biome biome;
    public BoolRequirement hillRequirement;
    public BoolRequirement mountainRequirement;
    public UnitLayerRequirement layerRequirement = UnitLayerRequirement.Any;
    public BoolRequirement underwaterRequirement;
    public BoolRequirement orbitRequirement;
    public bool useResourceFilter;
    public ResourceData resource;

    [Header("Aura Bonuses")]
    public UnitAuraBonus[] auraBonuses;

    public Ability CreateAbility()
    {
        return new Ability
        {
            abilityName      = abilityName,
            icon             = icon,
            description      = description,
            requiredLevel    = requiredLevel,
            attackModifier   = attackModifier,
            defenseModifier  = defenseModifier,
            damageMultiplier = damageMultiplier,
            targetUnit       = targetUnit,
            targetWorker     = targetWorker,
            useTargetUnitCategoryFilter = useTargetUnitCategoryFilter,
            targetUnitCategory = targetUnitCategory,
            healthModifier   = healthModifier,
            rangeModifier    = rangeModifier,
            cityRequirement = cityRequirement,
            useBiomeFilter = useBiomeFilter,
            biome = biome,
            hillRequirement = hillRequirement,
            mountainRequirement = mountainRequirement,
            layerRequirement = layerRequirement,
            underwaterRequirement = underwaterRequirement,
            orbitRequirement = orbitRequirement,
            useResourceFilter = useResourceFilter,
            resource = resource,
            auraBonuses = auraBonuses
        };
    }
}
