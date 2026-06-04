using UnityEngine;

// Shared serializable types for targeted bonuses applied by Techs and Cultures.
// These are data-only containers; game systems should read them and apply the effects at runtime.

public enum BoolRequirement
{
    Any,
    MustBeTrue,
    MustBeFalse,
}

public enum CityYieldScope
{
    AllCities,
    CapitalOnly,
}

public enum UnitTerritoryRequirement
{
    Any,
    Owned,
    Friendly,
    Enemy,
    Unowned,
}

[System.Serializable]
public struct CombatTargetedModifier
{
    [Tooltip("If set, this modifier only applies against this specific enemy combat unit.")]
    public CombatUnitData targetUnit;
    [Tooltip("If set, this modifier only applies against this specific enemy worker unit.")]
    public WorkerUnitData targetWorker;
    [Tooltip("If enabled, this modifier only applies against enemy combat units in the selected category.")]
    public bool useTargetUnitCategoryFilter;
    public CombatCategory targetUnitCategory;

    [Header("Additive (flat)")]
    public float attackAdd;
    public float defenseAdd;

    [Header("Multiplicative (%)")]
    [Tooltip("Percent increase as 0.10 = +10%.")]
    public float attackPct;
    public float defensePct;
}

[System.Serializable]
public class UnitStatBonus
{
    [Header("Unit Filters")]
    public CombatUnitData unit;
    [Tooltip("If enabled, this bonus only applies to combat units in the selected category.")]
    public bool useUnitCategoryFilter = false;
    public CombatCategory unitCategory;

    [Header("Combat Target Filters")]
    [Tooltip("If set, attack/defense portions of this bonus only apply against this specific enemy combat unit.")]
    public CombatUnitData targetUnit;
    [Tooltip("If set, attack/defense portions of this bonus only apply against this specific enemy worker unit.")]
    public WorkerUnitData targetWorker;
    [Tooltip("If enabled, attack/defense portions of this bonus only apply against enemy combat units in the selected category.")]
    public bool useTargetUnitCategoryFilter = false;
    public CombatCategory targetUnitCategory;

    [Header("Location Filters")]
    [Tooltip("Whether the unit must be standing in a city tile.")]
    public BoolRequirement cityRequirement;
    public bool useBiomeFilter;
    public Biome biome;
    public BoolRequirement hillRequirement;
    public BoolRequirement mountainRequirement;
    [Tooltip("Require a specific resource on the unit's tile.")]
    public bool useResourceFilter;
    public ResourceData resource;
    [Tooltip("Require a territory relationship for the tile the unit is standing on.")]
    public UnitTerritoryRequirement territoryRequirement = UnitTerritoryRequirement.Any;

    [Header("Season Filter")]
    [Tooltip("If enabled, this bonus applies only during the selected seasons.")]
    public bool useSeasonFilter = false;
    public Season[] seasons;

    [Header("Additive (flat)")]
    public int attackAdd;
    public int meleeAttackAdd;
    public int rangedAttackAdd;
    public int cityAttackAdd;
    public int groundAttackAdd;
    public int navalAttackAdd;
    public int underwaterAttackAdd;
    public int airAttackAdd;
    public int spaceAttackAdd;
    public int defenseAdd;
    public int healthAdd;
    public int rangeAdd;

    [Header("Multiplicative (%)")]
    [Tooltip("Percent increase as 0.10 = +10%.")]
    public float attackPct;
    public float meleeAttackPct;
    public float rangedAttackPct;
    public float cityAttackPct;
    public float groundAttackPct;
    public float navalAttackPct;
    public float underwaterAttackPct;
    public float airAttackPct;
    public float spaceAttackPct;
    public float defensePct;
    public float healthPct;
    public float rangePct;
    [Header("Healing")]
    [Tooltip("Percent faster healing/reinforcement rate for this unit (0.10 = +10% faster).")]
    public float healingRatePct = 0f;
    [Header("New Unit Progression")]
    [Tooltip("Bonus experience granted to newly built matching combat units.")]
    public int startingExperienceAdd = 0;
    [Tooltip("Bonus levels granted to newly built matching combat units.")]
    public int startingLevelsAdd = 0;
}

[System.Serializable]
public class UnitYieldBonus
{
    [Header("Unit Filters")]
    [Tooltip("Target combat unit archetype whose per-turn yields will be modified")] 
    public CombatUnitData unit;
    [Tooltip("If enabled, this bonus only applies to combat units in the selected category.")]
    public bool useUnitCategoryFilter = false;
    public CombatCategory unitCategory;

    [Header("Season Filter")]
    [Tooltip("If enabled, this bonus applies only during the selected seasons.")]
    public bool useSeasonFilter = false;
    public Season[] seasons;

    [Header("Yield Add (flat per unit per turn)")]
    public int foodAdd;
    public int productionAdd; // kept for symmetry, not used by unit yields currently
    public int goldAdd;
    public int scienceAdd;
    public int cultureAdd;
    public int faithAdd;
    public int policyPointsAdd;

    [Header("Yield % (per unit per turn)")]
    [Tooltip("Percent increase as 0.10 = +10%.")]
    public float foodPct;
    public float productionPct; // kept for symmetry
    public float goldPct;
    public float sciencePct;
    public float culturePct;
    public float faithPct;
    public float policyPointsPct;
}

[System.Serializable]
public class WorkerUnitStatBonus
{
    public WorkerUnitData worker;

    [Header("Combat Target Filters")]
    [Tooltip("If set, attack/defense portions of this bonus only apply against this specific enemy combat unit.")]
    public CombatUnitData targetUnit;
    [Tooltip("If set, attack/defense portions of this bonus only apply against this specific enemy worker unit.")]
    public WorkerUnitData targetWorker;
    [Tooltip("If enabled, attack/defense portions of this bonus only apply against enemy combat units in the selected category.")]
    public bool useTargetUnitCategoryFilter = false;
    public CombatCategory targetUnitCategory;

    [Header("Location Filters")]
    [Tooltip("Whether the worker must be standing in a city tile.")]
    public BoolRequirement cityRequirement;
    public bool useBiomeFilter;
    public Biome biome;
    public BoolRequirement hillRequirement;
    public BoolRequirement mountainRequirement;
    [Tooltip("Require a specific resource on the worker's tile.")]
    public bool useResourceFilter;
    public ResourceData resource;
    [Tooltip("Require a territory relationship for the tile the worker is standing on.")]
    public UnitTerritoryRequirement territoryRequirement = UnitTerritoryRequirement.Any;

    [Header("Season Filter")]
    [Tooltip("If enabled, this bonus applies only during the selected seasons.")]
    public bool useSeasonFilter = false;
    public Season[] seasons;

    [Header("Additive (flat)")]
    public int attackAdd;
    public int meleeAttackAdd;
    public int rangedAttackAdd;
    public int cityAttackAdd;
    public int groundAttackAdd;
    public int navalAttackAdd;
    public int underwaterAttackAdd;
    public int airAttackAdd;
    public int spaceAttackAdd;
    public int defenseAdd;
    public int workPointsAdd;
    public int movePointsAdd;
    public int healthAdd;
    public int rangeAdd;

    [Header("Multiplicative (%)")]
    [Tooltip("Percent increase as 0.10 = +10%.")]
    public float attackPct;
    public float meleeAttackPct;
    public float rangedAttackPct;
    public float cityAttackPct;
    public float groundAttackPct;
    public float navalAttackPct;
    public float underwaterAttackPct;
    public float airAttackPct;
    public float spaceAttackPct;
    public float defensePct;
    public float workPointsPct;
    public float movePointsPct;
    public float healthPct;
    public float rangePct;
    [Header("Healing")]
    [Tooltip("Percent faster healing/reinforcement rate for this worker (0.10 = +10% faster).")]
    public float healingRatePct = 0f;
    [Header("New Unit Progression")]
    [Tooltip("Bonus experience granted to newly built matching worker units.")]
    public int startingExperienceAdd = 0;
    [Tooltip("Bonus levels granted to newly built matching worker units.")]
    public int startingLevelsAdd = 0;
}

[System.Serializable]
public class WorkerUnitYieldBonus
{
    public WorkerUnitData worker;

    [Header("Season Filter")]
    [Tooltip("If enabled, this bonus applies only during the selected seasons.")]
    public bool useSeasonFilter = false;
    public Season[] seasons;

    [Header("Yield Add (flat per unit per turn)")]
    public int foodAdd;
    public int goldAdd;
    public int scienceAdd;
    public int cultureAdd;
    public int faithAdd;
    public int policyPointsAdd;

    [Header("Yield % (per unit per turn)")]
    [Tooltip("Percent increase as 0.10 = +10%.")]
    public float foodPct;
    public float goldPct;
    public float sciencePct;
    public float culturePct;
    public float faithPct;
    public float policyPointsPct;
}

[System.Serializable]
public class EquipmentStatBonus
{
    public EquipmentData equipment;

    [Header("Combat Target Filters")]
    [Tooltip("If set, attack/defense portions of this bonus only apply against this specific enemy combat unit.")]
    public CombatUnitData targetUnit;
    [Tooltip("If set, attack/defense portions of this bonus only apply against this specific enemy worker unit.")]
    public WorkerUnitData targetWorker;
    [Tooltip("If enabled, attack/defense portions of this bonus only apply against enemy combat units in the selected category.")]
    public bool useTargetUnitCategoryFilter = false;
    public CombatCategory targetUnitCategory;

    [Header("Additive (flat)")]
    public int attackAdd;
    public int meleeAttackAdd;
    public int rangedAttackAdd;
    public int cityAttackAdd;
    public int groundAttackAdd;
    public int navalAttackAdd;
    public int underwaterAttackAdd;
    public int airAttackAdd;
    public int spaceAttackAdd;
    public int defenseAdd;
    public int healthAdd;
    public int rangeAdd;

    [Header("Multiplicative (%)")]
    [Tooltip("Percent increase as 0.10 = +10%.")]
    public float attackPct;
    public float meleeAttackPct;
    public float rangedAttackPct;
    public float cityAttackPct;
    public float groundAttackPct;
    public float navalAttackPct;
    public float underwaterAttackPct;
    public float airAttackPct;
    public float spaceAttackPct;
    public float defensePct;
    public float healthPct;
    public float rangePct;
}

[System.Serializable]
public class EquipmentYieldBonus
{
    [Tooltip("Target equipment whose per-unit yields are modified while equipped")]
    public EquipmentData equipment;

    [Header("Yield Add (flat per unit per turn)")]
    public int foodAdd;
    public int goldAdd;
    public int scienceAdd;
    public int cultureAdd;
    public int faithAdd;
    public int policyPointsAdd;

    [Header("Yield % (per unit per turn)")]
    [Tooltip("Percent increase as 0.10 = +10%.")]
    public float foodPct;
    public float goldPct;
    public float sciencePct;
    public float culturePct;
    public float faithPct;
    public float policyPointsPct;
}

[System.Serializable]
public class ImprovementYieldBonus
{
    public ImprovementData improvement;

    [Header("Yield Add (flat per turn)")]
    public int foodAdd;
    public int productionAdd;
    public int goldAdd;
    public int scienceAdd;
    public int cultureAdd;
    public int faithAdd;
    public int policyPointsAdd;

    [Header("Yield % (per turn)")]
    [Tooltip("Percent increase as 0.10 = +10%.")]
    public float foodPct;
    public float productionPct;
    public float goldPct;
    public float sciencePct;
    public float culturePct;
    public float faithPct;
    public float policyPointsPct;
}

[System.Serializable]
public class BuildingYieldBonus
{
    public BuildingData building;

    [Header("Season Filter")]
    [Tooltip("If enabled, this bonus applies only during the selected seasons.")]
    public bool useSeasonFilter = false;
    public Season[] seasons;

    [Header("Yield Add (flat per turn)")]
    public int foodAdd;
    public int productionAdd;
    public int goldAdd;
    public int scienceAdd;
    public int cultureAdd;
    public int faithAdd;
    public int policyPointsAdd;

    [Header("Yield % (per turn)")]
    [Tooltip("Percent increase as 0.10 = +10%.")]
    public float foodPct;
    public float productionPct;
    public float goldPct;
    public float sciencePct;
    public float culturePct;
    public float faithPct;
    public float policyPointsPct;
}

[System.Serializable]
public class TileYieldBonus
{
    [Header("Tile Filters")]
    public bool useBiomeFilter;
    public Biome biome;
    public BoolRequirement hillRequirement;
    public BoolRequirement mountainRequirement;
    [Header("Season Filter")]
    [Tooltip("If enabled, this bonus applies only during the selected seasons on the tile.")]
    public bool useSeasonFilter = false;
    public Season[] seasons;
    [Tooltip("Require a specific resource on the tile (enable with useResourceFilter)")]
    public bool useResourceFilter;
    public ResourceData resource;

    [Header("Yield Add (flat per turn)")]
    public int foodAdd;
    public int productionAdd;
    public int goldAdd;
    public int scienceAdd;
    public int cultureAdd;
    public int faithAdd;
    public int policyPointsAdd;

    [Header("Yield % (per turn)")]
    [Tooltip("Percent increase as 0.10 = +10%.")]
    public float foodPct;
    public float productionPct;
    public float goldPct;
    public float sciencePct;
    public float culturePct;
    public float faithPct;
    public float policyPointsPct;
}

[System.Serializable]
public class CityYieldBonus
{
    public CityYieldScope scope = CityYieldScope.AllCities;

    [Header("Season Filter")]
    [Tooltip("If enabled, this bonus applies only during the selected seasons.")]
    public bool useSeasonFilter = false;
    public Season[] seasons;

    [Header("Yield Add (flat per city per turn)")]
    public int foodAdd;
    public int productionAdd;
    public int goldAdd;
    public int scienceAdd;
    public int cultureAdd;
    public int faithAdd;
    public int policyPointsAdd;

    [Header("Yield % (per city per turn)")]
    [Tooltip("Percent increase as 0.10 = +10%.")]
    public float foodPct;
    public float productionPct;
    public float goldPct;
    public float sciencePct;
    public float culturePct;
    public float faithPct;
    public float policyPointsPct;
}

[System.Serializable]
public class DiseaseModifierBonus
{
    [Tooltip("Specific disease affected by this modifier. Ignored when affectsAllDiseases is true.")]
    public DiseaseData disease;
    [Tooltip("If true, this modifier applies to all diseases.")]
    public bool affectsAllDiseases;

    [Header("Season Filter")]
    [Tooltip("If enabled, this bonus applies only during the selected seasons.")]
    public bool useSeasonFilter = false;
    public Season[] seasons;

    [Header("Immunity")]
    [Tooltip("If true, the owning civilization/city/herd is immune to the matching disease.")]
    public bool grantsImmunity;

    [Header("Chance & Duration (%)")]
    [Tooltip("Signed percent modifier to infection chance. -0.25 = 25% less likely, 0.25 = 25% more likely.")]
    public float infectionChancePct;
    [Tooltip("Signed percent modifier to spread chance from already infected sources.")]
    public float spreadChancePct;
    [Tooltip("Signed percent modifier to disease duration.")]
    public float durationPct;

    [Header("City Severity (%)")]
    [Tooltip("Signed percent modifier to city population loss caused by the disease.")]
    public float cityPopulationLossPct;
    [Tooltip("Signed percent modifier to all city yield penalties caused by the disease.")]
    public float cityYieldPenaltyPct;
    [Tooltip("Signed percent modifier to morale loss caused by the disease.")]
    public float cityMoralePenaltyPct;
    [Tooltip("Signed percent modifier to loyalty loss caused by the disease.")]
    public float cityLoyaltyPenaltyPct;

    [Header("Herd Severity (%)")]
    [Tooltip("Signed percent modifier to herd animal mortality caused by the disease.")]
    public float herdMortalityPct;
    [Tooltip("Signed percent modifier to herd forage penalties caused by the disease.")]
    public float herdForagePenaltyPct;
}

public struct DiseaseModifierTotals
{
    public bool grantsImmunity;
    public float infectionChancePct;
    public float spreadChancePct;
    public float durationPct;
    public float cityPopulationLossPct;
    public float cityYieldPenaltyPct;
    public float cityMoralePenaltyPct;
    public float cityLoyaltyPenaltyPct;
    public float herdMortalityPct;
    public float herdForagePenaltyPct;

    public float InfectionChanceMultiplier => Mathf.Max(0f, 1f + infectionChancePct);
    public float SpreadChanceMultiplier => Mathf.Max(0f, 1f + spreadChancePct);
    public float DurationMultiplier => Mathf.Max(0f, 1f + durationPct);
    public float CityPopulationLossMultiplier => Mathf.Max(0f, 1f + cityPopulationLossPct);
    public float CityYieldPenaltyMultiplier => Mathf.Max(0f, 1f + cityYieldPenaltyPct);
    public float CityMoralePenaltyMultiplier => Mathf.Max(0f, 1f + cityMoralePenaltyPct);
    public float CityLoyaltyPenaltyMultiplier => Mathf.Max(0f, 1f + cityLoyaltyPenaltyPct);
    public float HerdMortalityMultiplier => Mathf.Max(0f, 1f + herdMortalityPct);
    public float HerdForagePenaltyMultiplier => Mathf.Max(0f, 1f + herdForagePenaltyPct);
}

[System.Serializable]
public class AttritionModifierBonus
{
    [Header("Season Filter")]
    [Tooltip("If enabled, this bonus applies only during the selected seasons.")]
    public bool useSeasonFilter = false;
    public Season[] seasons;

    [Header("Attrition Reductions")]
    [Tooltip("Percent reduction to winter attrition damage. 0.10 = 10% reduction.")]
    public float winterDamageReductionPct;
    [Tooltip("Percent reduction to famine attrition damage. 0.10 = 10% reduction.")]
    public float famineDamageReductionPct;
    [Tooltip("Percent reduction to biome/environmental damage from damaging terrain tiles like desert, tundra, lava, and similar hazards. 0.10 = 10% reduction.")]
    public float biomeDamageReductionPct;
}

public struct AttritionModifierTotals
{
    public float winterDamageReductionPct;
    public float famineDamageReductionPct;
    public float biomeDamageReductionPct;

    public float WinterDamageMultiplier => Mathf.Max(0f, 1f - winterDamageReductionPct);
    public float FamineDamageMultiplier => Mathf.Max(0f, 1f - famineDamageReductionPct);
    public float BiomeDamageMultiplier => Mathf.Max(0f, 1f - biomeDamageReductionPct);
}

[System.Serializable]
public class GenericYieldBonus
{
    [Tooltip("Assign any asset that represents a district or other yield-bearing entity (e.g., DistrictData if present).")]
    public ScriptableObject target;

    [Header("Yield Add (flat per turn)")]
    public int foodAdd;
    public int productionAdd;
    public int goldAdd;
    public int scienceAdd;
    public int cultureAdd;
    public int faithAdd;

    [Header("Yield % (per turn)")]
    [Tooltip("Percent increase as 0.10 = +10%.")]
    public float foodPct;
    public float productionPct;
    public float goldPct;
    public float sciencePct;
    public float culturePct;
    public float faithPct;
}

[System.Serializable]
public class HerdYieldBonus
{
    [Header("Species Filter")]
    [Tooltip("If enabled, this bonus only applies to herds containing the selected animal species.")]
    public bool useSpeciesFilter = false;
    public Herd.HerdSpecies species;

    [Header("Season Filter")]
    [Tooltip("If enabled, this bonus applies only during the selected seasons.")]
    public bool useSeasonFilter = false;
    public Season[] seasons;

    [Header("Yield Add (flat per herd per turn)")]
    public int foodAdd;
    public int productionAdd;
    public int goldAdd;
    public int scienceAdd;
    public int cultureAdd;
    public int faithAdd;
    public int policyPointsAdd;

    [Header("Yield % (per herd per turn)")]
    [Tooltip("Percent increase as 0.10 = +10%.")]
    public float foodPct;
    public float productionPct;
    public float goldPct;
    public float sciencePct;
    public float culturePct;
    public float faithPct;
    public float policyPointsPct;
}
