// Assets/Scripts/Units/Ability.cs
using UnityEngine;

public class Ability
{
    public string abilityName;
    public Sprite icon;
    public string description;
    public int requiredLevel;
    public int attackModifier;
    public int defenseModifier;
    public float damageMultiplier = 1f;
    public CombatUnitData targetUnit;
    public WorkerUnitData targetWorker;
    public bool useTargetUnitCategoryFilter;
    public CombatCategory targetUnitCategory;

    // New modifiers for health and range
    public int healthModifier;
    public int rangeModifier;
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
    public UnitAuraBonus[] auraBonuses;

    // TODO: add methods to apply effects, manage cooldowns, etc.
}
