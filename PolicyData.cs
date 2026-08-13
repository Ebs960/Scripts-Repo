using UnityEngine;

public enum PolicyTag
{
    Administration, Agriculture, Colonial, Economy, Education, Environment,
    Infrastructure, Labor, Law, Military, Religion, Rights, Security, Trade,
    Welfare, Digital, Synthetic, Genetics, Space
}

[System.Serializable]
public class PolicyReligiousRequirementGroup
{
    public bool requiresStateReligion;
    public ReligionData[] anyStateReligions;
    public PantheonData[] anyPantheons;
    [Tooltip("Allow a required pantheon to match a pantheon reached by following its upgrade chain.")]
    public bool allowPantheonUpgradeDescendants = true;
    public bool useMinimumPantheonTier;
    public PantheonTier minimumPantheonTier;
    public BeliefData[] anyBeliefs;
    public BeliefCategory[] anyBeliefCategories;
}

[CreateAssetMenu(fileName = "NewPolicyData", menuName = "Data/Policy Data")]
public class PolicyData : ScriptableObject
{
    [Header("Identity")]
    public string policyName;
    public Sprite icon;
    [TextArea] public string description;

    [Header("Cost & Requirements")]
    [Tooltip("Policy points required to adopt this policy")]
    public int policyPointCost;
    public TechData[] requiredTechs;
    public CultureData[] requiredCultures;
    public GovernmentData[] requiredGovernments;
    public int requiredCityCount;
    [Tooltip("Alternative religious routes. Groups are OR; populated clauses within a group are AND.")]
    public PolicyReligiousRequirementGroup[] religiousRequirementGroups;

    [Header("Policy Relationships")]
    [Tooltip("All referenced policies must currently be active.")]
    public PolicyData[] requiredPolicies;
    [Tooltip("Policies that cannot coexist. Runtime checks are symmetric.")]
    public PolicyData[] incompatiblePolicies;
    [Tooltip("Active policies automatically repealed after a successful adoption vote.")]
    public PolicyData[] supersedesPolicies;

    [Header("Classification")]
    public PolicyTag[] policyTags;

    [Header("Council")]
    [Tooltip("Council veto domains implicated in addition to Policy Change.")]
    public VetoDomain additionalVetoDomains;

    [Header("Bonuses")]
    public float attackBonus;
    public float meleeAttackBonus;
    public float rangedAttackBonus;
    public float cityAttackBonus;
    public float defenseBonus;
    public float movementBonus;
    public float foodModifier;          // New
    public float productionModifier;    // New
    public float goldModifier;          // New
    public float scienceModifier;       // New
    public float cultureModifier;       // New
    public float faithModifier;         // New

    [Header("Society")]
    public float populationGrowthModifier;
    public float migrationAttractionModifier;
    public float warWearinessModifier;
    public float corruptionModifier;
    public float unrestModifier;

    [Header("Administration")]
    public float administrativeEfficiencyModifier;
    public float distanceLoyaltyPenaltyModifier;
    public float policyPointGenerationModifier;

    [Header("Trade")]
    public float domesticTradeModifier;
    public float foreignTradeModifier;
    public int tradeRouteCapacityBonus;

    [Header("Labor")]
    public float laborProductivityModifier;
    public float unemploymentUnhappinessModifier;

    [Header("Strategic")]
    public float reinforcementSpeedModifier;
    public float militaryUpkeepModifier;

    [Header("Digital")]
    public float cyberDefenseModifier;
    public float cyberOffenseModifier;
    public float espionageDefenseModifier;

    [Header("Space")]
    public float orbitalProductionModifier;
    public float interplanetaryTradeModifier;
    public float planetaryLoyaltyModifier;
    public float planetaryDefenseModifier;

    [Header("Tile Yield Bonuses")]
    [Tooltip("Per-tile yield bonuses granted by this policy. Filters can target terrain, resources, improvements, and more.")]
    public TileYieldBonus[] tileYieldBonuses;

    [Header("Building Yield Bonuses")]
    [Tooltip("Per-building yield/stat bonuses granted by this policy while active. Can target exact buildings or building categories.")]
    public BuildingYieldBonus[] buildingBonuses;

    [Header("Unit Yield Bonuses")]
    [Tooltip("Per-unit per-turn yield bonuses granted by this policy.")]
    public UnitYieldBonus[] unitYieldBonuses;
    [Tooltip("Per-unit stat bonuses granted by this policy (e.g., healing speed).")]
    public UnitStatBonus[] unitBonuses;
    [Tooltip("Per-equipment per-turn yield bonuses granted by this policy (applies when equipped).")]
    public EquipmentYieldBonus[] equipmentYieldBonuses;
    [Tooltip("Per-worker per-turn yield bonuses granted by this policy.")]
    public WorkerUnitYieldBonus[] workerYieldBonuses;
    [Tooltip("Per-worker stat bonuses granted by this policy (e.g., healing speed).")]
    public WorkerUnitStatBonus[] workerBonuses;
    [Tooltip("Disease modifiers granted by this policy.")]
    public DiseaseModifierBonus[] diseaseBonuses;
    public AttritionModifierBonus[] attritionBonuses;
    [Tooltip("Per-city yield, defense, and happiness bonuses granted by this policy while active.")]
    public CityYieldBonus[] cityBonuses;
    [Tooltip("Policy modifiers to per-turn city unhappiness caused by citizens following religions other than the state religion.")]
    public NonStateReligionUnhappinessModifier[] nonStateReligionUnhappinessModifiers;

    [Header("Herd Modifiers")]
    [Tooltip("Reduces the percent of herd animals lost to starvation (e.g. 0.05 = -5 percentage points)")]
    public float herdStarvationPercentReduction = 0f;
    [Tooltip("Per-herd per-turn yield bonuses granted by this policy (can filter by animal species).")]
    public HerdYieldBonus[] herdYieldBonuses;

    [Header("Governor Bonuses")]
    public int additionalGovernorSlots;
    public GovernorTrait[] unlockedGovernorTraits;

    [Header("Governor Political Reactions")]
    [Tooltip("Opinion reactions pushed to governors when this policy is adopted. " +
             "Use personality filters to target specific governor archetypes.")]
    public GovernorOpinionEffect[] governorOpinionEffects;
}
