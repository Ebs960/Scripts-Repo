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

public enum CityBuildingRequirementScope
{
    SameCity,
    Capital,
    AnyOtherCity,
    AnyCity,
    EveryOtherCity,
}

[System.Serializable]
public class CityBuildingRequirement
{
    [Tooltip("Where to look for the required operational building.")]
    public CityBuildingRequirementScope scope = CityBuildingRequirementScope.SameCity;

    [Tooltip("Exact building that must be operational. Leave empty to use only the category filter.")]
    public BuildingData building;

    [Tooltip("If enabled, any operational building in this category satisfies the requirement.")]
    public bool useBuildingCategoryFilter = false;
    public BuildingCategory buildingCategory;
}

[System.Serializable]
public class NonStateReligionUnhappinessModifier
{
    [Tooltip("Flat unhappiness added per citizen following a religion other than the civilization state religion. Negative values reduce the penalty.")]
    public float unhappinessPerFollowerAdd;

    [Tooltip("Percent modifier to non-state-religion unhappiness. -0.25 = 25% less, 0.25 = 25% more.")]
    public float unhappinessPct;
}

public enum UnitTerritoryRequirement
{
    Any,
    Owned,
    Friendly,
    Enemy,
    Unowned,
}

public enum UnitLayerRequirement
{
    Any,
    Surface,
    Underwater,
    Orbit,
}

public enum UnitAuraTargetRelationship
{
    Friendly,
    SameCivilization,
    Enemy,
    Any,
}

public enum BuildingUnitBonusScope
{
    [Tooltip("For building-sourced unit bonuses, apply to units in this building's city and to units trained in this building's city.")]
    SameCity,
    [Tooltip("For building-sourced unit bonuses, apply to all units owned by the civilization, regardless of city.")]
    AllCivilizationUnits,
    [Tooltip("For building-sourced unit bonuses, only apply one-time new-unit progression bonuses to units trained in this building's city.")]
    TrainedUnitsOnly,
}


[System.Serializable]
public class UnitBuildingRequirement
{
    [Tooltip("Building that must be operational in the producing city before this unit can be trained.")]
    public BuildingData building;

    [Tooltip("If enabled, any operational building in this category satisfies the requirement.")]
    public bool useBuildingCategoryFilter = false;
    public BuildingCategory buildingCategory;
}

[System.Serializable]
public class UnitProductionModifier
{
    [Header("Unit Filters")]
    [Tooltip("If set, this modifier only applies when training this exact combat unit.")]
    public CombatUnitData combatUnit;
    [Tooltip("If set, this modifier only applies when training this exact worker unit.")]
    public WorkerUnitData workerUnit;
    [Tooltip("If enabled, this modifier applies to combat units in the selected category, such as Spearman or Artillery.")]
    public bool useCombatCategoryFilter = false;
    public CombatCategory combatCategory;

    [Header("Production Speed")]
    [Tooltip("Flat production points added each turn while training a matching unit in this city.")]
    public int productionAdd;
    [Tooltip("Percent faster unit training. 0.10 = +10%, 0.15 = +15%.")]
    public float productionPct;
}

public enum BuildingCategory
{
    Food,
    Production,
    Gold,
    Science,
    Culture,
    Faith,
    Health,
    Defense,
    Energy,
    Harbor,
    PerimeterWall,
    Airport,
    Spaceport,
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

    [Header("Building Source Scope")]
    [Tooltip("Only used when this bonus is placed on a BuildingData asset.")]
    public BuildingUnitBonusScope buildingScope = BuildingUnitBonusScope.SameCity;

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
    [Tooltip("Require the unit to be in a particular map layer. Surface excludes underwater and orbit tiles.")]
    public UnitLayerRequirement layerRequirement = UnitLayerRequirement.Any;
    [Tooltip("Whether the unit must be standing on an underwater tile/layer.")]
    public BoolRequirement underwaterRequirement;
    [Tooltip("Whether the unit must be standing on an orbit/space tile/layer.")]
    public BoolRequirement orbitRequirement;
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
    public int underwaterAttackAdd;
    public int airAttackAdd;
    public int spaceAttackAdd;
    public int defenseAdd;
    public int healthAdd;
    [Tooltip("Flat movement points added to matching combat units at the start of each turn.")]
    public int movePointsAdd;
    public int rangeAdd;
    [Tooltip("Flat sight/vision range added to matching units when vision is calculated.")]
    public int sightRangeAdd;

    [Header("Multiplicative (%)")]
    [Tooltip("Percent increase as 0.10 = +10%.")]
    public float attackPct;
    public float meleeAttackPct;
    public float rangedAttackPct;
    public float cityAttackPct;
    public float groundAttackPct;
    public float underwaterAttackPct;
    public float airAttackPct;
    public float spaceAttackPct;
    public float defensePct;
    public float healthPct;
    [Tooltip("Percent movement point increase as 0.10 = +10%.")]
    public float movePointsPct;
    public float rangePct;
    [Tooltip("Percent sight/vision range increase as 0.10 = +10%.")]
    public float sightRangePct;
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

    [Header("Building Source Scope")]
    [Tooltip("Only used when this bonus is placed on a BuildingData asset.")]
    public BuildingUnitBonusScope buildingScope = BuildingUnitBonusScope.SameCity;

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
    [Tooltip("Require the worker to be in a particular map layer. Surface excludes underwater and orbit tiles.")]
    public UnitLayerRequirement layerRequirement = UnitLayerRequirement.Any;
    [Tooltip("Whether the worker must be standing on an underwater tile/layer.")]
    public BoolRequirement underwaterRequirement;
    [Tooltip("Whether the worker must be standing on an orbit/space tile/layer.")]
    public BoolRequirement orbitRequirement;
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
    public int underwaterAttackAdd;
    public int airAttackAdd;
    public int spaceAttackAdd;
    public int defenseAdd;
    public int workPointsAdd;
    public int movePointsAdd;
    public int healthAdd;
    public int rangeAdd;
    [Tooltip("Flat sight/vision range added to matching workers when vision is calculated.")]
    public int sightRangeAdd;

    [Header("Multiplicative (%)")]
    [Tooltip("Percent increase as 0.10 = +10%.")]
    public float attackPct;
    public float meleeAttackPct;
    public float rangedAttackPct;
    public float cityAttackPct;
    public float groundAttackPct;
    public float underwaterAttackPct;
    public float airAttackPct;
    public float spaceAttackPct;
    public float defensePct;
    public float workPointsPct;
    public float movePointsPct;
    public float healthPct;
    public float rangePct;
    [Tooltip("Percent sight/vision range increase as 0.10 = +10%.")]
    public float sightRangePct;
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

    [Header("Additive (flat)")]
    public int attackAdd;
    public int meleeAttackAdd;
    public int rangedAttackAdd;
    public int cityAttackAdd;
    public int groundAttackAdd;
    public int underwaterAttackAdd;
    public int airAttackAdd;
    public int spaceAttackAdd;
    public int defenseAdd;
    public int healthAdd;
    public int rangeAdd;
    [Tooltip("Flat sight/vision range added to units using matching equipment when vision is calculated.")]
    public int sightRangeAdd;

    [Header("Multiplicative (%)")]
    [Tooltip("Percent increase as 0.10 = +10%.")]
    public float attackPct;
    public float meleeAttackPct;
    public float rangedAttackPct;
    public float cityAttackPct;
    public float groundAttackPct;
    public float underwaterAttackPct;
    public float airAttackPct;
    public float spaceAttackPct;
    public float defensePct;
    public float healthPct;
    public float rangePct;
    [Tooltip("Percent sight/vision range increase as 0.10 = +10%.")]
    public float sightRangePct;
}

[System.Serializable]
public class UnitAuraBonus
{
    [Header("Aura")]
    [Tooltip("Maximum hex distance from the source unit. 2 means units up to two hexes away.")]
    public int radius = 1;
    [Tooltip("Whether the source unit receives its own aura.")]
    public bool includeSelf = false;
    [Tooltip("Which units can receive this aura relative to the source unit.")]
    public UnitAuraTargetRelationship targetRelationship = UnitAuraTargetRelationship.Friendly;

    [Header("Target Filters")]
    public CombatUnitData targetCombatUnit;
    public WorkerUnitData targetWorkerUnit;
    public bool useTargetUnitCategoryFilter = false;
    public CombatCategory targetUnitCategory;

    [Header("Additive (flat)")]
    public float attackAdd;
    public float meleeAttackAdd;
    public float rangedAttackAdd;
    public float cityAttackAdd;
    public float groundAttackAdd;
    public float underwaterAttackAdd;
    public float airAttackAdd;
    public float spaceAttackAdd;
    public float defenseAdd;
    public float healthAdd;
    public float rangeAdd;

    [Header("Multiplicative (%)")]
    [Tooltip("Percent increase as 0.10 = +10%.")]
    public float attackPct;
    public float meleeAttackPct;
    public float rangedAttackPct;
    public float cityAttackPct;
    public float groundAttackPct;
    public float underwaterAttackPct;
    public float airAttackPct;
    public float spaceAttackPct;
    public float defensePct;
    public float healthPct;
    public float rangePct;

    [Header("Passive Healing")]
    [Tooltip("Percent of the target unit's maximum health restored at the start of that unit's turn. 0.20 = heals 20% of max health per turn.")]
    public float healingRatePct;

    [Header("City Aura Additive (flat)")]
    public int cityFoodAdd;
    public int cityProductionAdd;
    public int cityGoldAdd;
    public int cityScienceAdd;
    public int cityCultureAdd;
    public int cityFaithAdd;
    public int cityPolicyPointsAdd;
    public int cityOrderAdd;
    public int cityHappinessAdd;
    public int cityDefenseAdd;

    [Header("City Aura Multiplicative (%)")]
    [Tooltip("Percent increase as 0.10 = +10%.")]
    public float cityFoodPct;
    public float cityProductionPct;
    public float cityGoldPct;
    public float citySciencePct;
    public float cityCulturePct;
    public float cityFaithPct;
    public float cityPolicyPointsPct;
    public float cityOrderPct;
    public float cityHappinessPct;
    public float cityDefensePct;
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
    [Header("Building Filters")]
    [Tooltip("Optional exact building target. Leave empty to target every building, or combine with the category filter.")]
    public BuildingData building;
    [Tooltip("If enabled, this bonus only applies to buildings in the selected category (for example IsFoodBuilding).")]
    public bool useBuildingCategoryFilter = false;
    public BuildingCategory buildingCategory;

    [Header("Presence Filters")]
    [Tooltip("If set, this bonus only applies in cities controlling at least one tile with this resource.")]
    public ResourceData requiredCityResource;
    [Tooltip("Optional operational building requirements that must be satisfied in this city/civilization before the bonus applies.")]
    public CityBuildingRequirement[] requiredBuildings;

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

    [Header("City Stat Add")]
    [Tooltip("Flat defense added to the matching building.")]
    public int defenseAdd;
    [Tooltip("Flat happiness/morale added to the matching building.")]
    public int happinessAdd;

    [Header("Yield % (per turn)")]
    [Tooltip("Percent increase as 0.10 = +10%.")]
    public float foodPct;
    public float productionPct;
    public float goldPct;
    public float sciencePct;
    public float culturePct;
    public float faithPct;
    public float policyPointsPct;

    [Header("City Stat %")]
    [Tooltip("Percent defense increase as 0.10 = +10%.")]
    public float defensePct;
    [Tooltip("Percent happiness/morale increase as 0.10 = +10%.")]
    public float happinessPct;
}

[System.Serializable]
public class TileYieldBonus
{
    [Header("Tile Filters")]
    public bool useBiomeFilter;
    public Biome biome;
    public BoolRequirement hillRequirement;
    public BoolRequirement mountainRequirement;
    [Tooltip("Require the tile to be land or non-land/water. Ignore leaves either valid.")]
    public BoolRequirement landRequirement;
    [Tooltip("Require the tile to be any water tile (ocean, lake, river, or underwater). Ignore leaves either valid.")]
    public BoolRequirement waterRequirement;
    [Tooltip("Require the tile to have or not have any improvement. Ignore leaves either valid.")]
    public BoolRequirement improvementRequirement;
    [Tooltip("Require the tile to have or not have any district. Ignore leaves either valid.")]
    public BoolRequirement districtRequirement;
    [Tooltip("Require a specific improvement on the tile (enable with useImprovementFilter).")]
    public bool useImprovementFilter;
    public ImprovementData improvement;
    [Tooltip("Require a specific district on the tile (enable with useDistrictFilter).")]
    public bool useDistrictFilter;
    public DistrictData district;
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

    [Header("Layer Filter")]
    [Tooltip("If enabled, this city bonus only applies to cities on one of the selected planet layers.")]
    public bool useLayerFilter = false;
    [Tooltip("Planet layers this city bonus applies to when Use Layer Filter is enabled.")]
    public GameManager.PlanetLayerType[] layers;

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

    [Header("City Stat Add")]
    [Tooltip("Flat defense added to each matching city.")]
    public int defenseAdd;
    [Tooltip("Flat happiness/morale added to each matching city.")]
    public int happinessAdd;

    [Header("Yield % (per city per turn)")]
    [Tooltip("Percent increase as 0.10 = +10%.")]
    public float foodPct;
    public float productionPct;
    public float goldPct;
    public float sciencePct;
    public float culturePct;
    public float faithPct;
    public float policyPointsPct;

    [Header("City Stat %")]
    [Tooltip("Percent defense increase as 0.10 = +10%.")]
    public float defensePct;
    [Tooltip("Percent happiness/morale increase as 0.10 = +10%.")]
    public float happinessPct;
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

    [Header("Natural Disaster Reductions")]
    [Tooltip("Percent chance reduction against earthquakes striking owned tiles/buildings. 0.10 = 10% reduction.")]
    public float earthquakeChanceReductionPct;
    [Tooltip("Percent damage reduction from earthquakes. 0.10 = 10% reduction.")]
    public float earthquakeDamageReductionPct;
    [Tooltip("Percent chance reduction against floods striking owned tiles/buildings. 0.10 = 10% reduction.")]
    public float floodChanceReductionPct;
    [Tooltip("Percent damage reduction from floods. 0.10 = 10% reduction.")]
    public float floodDamageReductionPct;
    [Tooltip("Percent chance reduction against storms striking owned tiles/buildings. 0.10 = 10% reduction.")]
    public float stormChanceReductionPct;
    [Tooltip("Percent damage reduction from storms. 0.10 = 10% reduction.")]
    public float stormDamageReductionPct;
}

public struct AttritionModifierTotals
{
    public float winterDamageReductionPct;
    public float famineDamageReductionPct;
    public float biomeDamageReductionPct;
    public float earthquakeChanceReductionPct;
    public float earthquakeDamageReductionPct;
    public float floodChanceReductionPct;
    public float floodDamageReductionPct;
    public float stormChanceReductionPct;
    public float stormDamageReductionPct;

    public float WinterDamageMultiplier => Mathf.Max(0f, 1f - winterDamageReductionPct);
    public float FamineDamageMultiplier => Mathf.Max(0f, 1f - famineDamageReductionPct);
    public float BiomeDamageMultiplier => Mathf.Max(0f, 1f - biomeDamageReductionPct);
    public float EarthquakeChanceMultiplier => Mathf.Max(0f, 1f - earthquakeChanceReductionPct);
    public float EarthquakeDamageMultiplier => Mathf.Max(0f, 1f - earthquakeDamageReductionPct);
    public float FloodChanceMultiplier => Mathf.Max(0f, 1f - floodChanceReductionPct);
    public float FloodDamageMultiplier => Mathf.Max(0f, 1f - floodDamageReductionPct);
    public float StormChanceMultiplier => Mathf.Max(0f, 1f - stormChanceReductionPct);
    public float StormDamageMultiplier => Mathf.Max(0f, 1f - stormDamageReductionPct);

    public float GetChanceMultiplier(NaturalDisasterType type)
    {
        return type switch
        {
            NaturalDisasterType.Earthquake => EarthquakeChanceMultiplier,
            NaturalDisasterType.Flood => FloodChanceMultiplier,
            NaturalDisasterType.Storm => StormChanceMultiplier,
            _ => 1f
        };
    }

    public float GetDamageMultiplier(NaturalDisasterType type)
    {
        return type switch
        {
            NaturalDisasterType.Earthquake => EarthquakeDamageMultiplier,
            NaturalDisasterType.Flood => FloodDamageMultiplier,
            NaturalDisasterType.Storm => StormDamageMultiplier,
            _ => 1f
        };
    }
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
