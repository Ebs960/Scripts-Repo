using UnityEngine;

[CreateAssetMenu(fileName = "NewGovernmentData", menuName = "Data/Government Data")]
public class GovernmentData : ScriptableObject
{
    [Header("Map Presentation")]
    [Tooltip("Shared thematic color used by every civilization with this government type.")]
    public Color mapModeColor = Color.clear;

    [Header("Identity")]
    public string governmentName;
    [Tooltip("Icon shown for this government in menus and HUD elements.")]
    public Sprite icon;
    public string leaderTitleSuffix;    // e.g. "Emperor", "Chieftain"
    [TextArea] public string description;

    [Header("Cost & Requirements")]
    [Tooltip("Policy points to enact this government")]
    public int policyPointCost;
    public TechData[] requiredTechs; // Changed from TechnologyData to TechData
    public CultureData[] requiredCultures;
    public int requiredCityCount;
    [Tooltip("If true, this government requires the civilization to have founded or adopted a state religion.")]
    public bool requiresStateReligion;
    [Tooltip("Minimum number of active subject/vassal contracts required to adopt this government.")]
    public int requiredVassalCount;

    [Header("Bonuses & Restrictions")]
    // REMOVED: unlocksPolicies
    // Policy availability is now controlled solely by requiredTechs/requiredCultures/requiredGovernments in PolicyData
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
    [Tooltip("Temporary city capacity modifier while this government is active.")]
    public int cityCapModifier;
    [Tooltip("Temporary city building slot capacity modifiers while this government is active.")]
    public CitySlotModifier[] citySlotModifiers;

    [Header("Tile Yield Bonuses")]
    [Tooltip("Per-tile yield bonuses granted by this government while active. Filters can target terrain, resources, improvements, and more.")]
    public TileYieldBonus[] tileYieldBonuses;

    [Header("Building Yield Bonuses")]
    [Tooltip("Per-building yield/stat bonuses granted by this government while active. Can target exact buildings or building categories.")]
    public BuildingYieldBonus[] buildingBonuses;

    [Header("Unit Yield Bonuses")]
    [Tooltip("Per-unit per-turn yield bonuses granted by this government while active.")]
    public UnitYieldBonus[] unitYieldBonuses;
    [Tooltip("Per-unit stat bonuses granted by this government while active (e.g., healing speed).")]
    public UnitStatBonus[] unitBonuses;
    [Tooltip("Per-equipment per-turn yield bonuses granted by this government (applies when equipped).")]
    public EquipmentYieldBonus[] equipmentYieldBonuses;
    [Tooltip("Per-worker per-turn yield bonuses granted by this government while active.")]
    public WorkerUnitYieldBonus[] workerYieldBonuses;
    [Tooltip("Per-worker stat bonuses granted by this government while active (e.g., healing speed).")]
    public WorkerUnitStatBonus[] workerBonuses;
    [Tooltip("Disease modifiers granted by this government while active.")]
    public DiseaseModifierBonus[] diseaseBonuses;
    public AttritionModifierBonus[] attritionBonuses;
    [Tooltip("Per-city yield, defense, and happiness bonuses granted by this government while active.")]
    public CityYieldBonus[] cityBonuses;
    [Tooltip("Government modifiers to per-turn city unhappiness caused by citizens following religions other than the state religion.")]
    public NonStateReligionUnhappinessModifier[] nonStateReligionUnhappinessModifiers;

    [Header("Herd Modifiers")]
    [Tooltip("Reduces the percent of herd animals lost to starvation (e.g. 0.05 = -5 percentage points)")]
    public float herdStarvationPercentReduction = 0f;
    [Tooltip("Per-herd per-turn yield bonuses granted by this government (can filter by animal species).")]
    public HerdYieldBonus[] herdYieldBonuses;

    [Header("Council & Political Structure")]
    [Tooltip("Whether this government uses a Royal Council of seated governors. When false, seat count and veto domains are ignored entirely.")]
    public bool usesRoyalCouncil = false;
    [Tooltip("How many governors may sit on the royal council under this government type. Only used when usesRoyalCouncil is true.")]
    public int councilSeatCount = 0;
    [Tooltip("Which domains the seated council may vote to veto. Flags — combine freely. Only used when usesRoyalCouncil is true.")]
    public VetoDomain councilVetoDomains = VetoDomain.None;
    [Tooltip("Opinion reactions pushed to governors when this government is adopted. " +
             "Use negative values to anger ambitious/discontented governors.")]
    public GovernorOpinionEffect[] governorOpinionEffects;

    [Header("Institution Identity")]
    [Tooltip("Player-facing name for the council/legislature. The existing council voting service is reused underneath.")]
    public string institutionDisplayName = "Council";

    [Header("Systemic Government Modifiers")]
    [Tooltip("The same reusable institutional modifiers exposed by policies. Values are applied and removed on government changes.")]
    public GovernmentInstitutionModifiers institutions = new GovernmentInstitutionModifiers();
    [Tooltip("When true, ordinary governor/faction politics remain allocated safely but do not tick or generate demands.")]
    public bool suppressConventionalPolitics;
    [TextArea] public string signatureMechanic;
    [TextArea] public string majorTradeoff;

    [Header("National Elections")]
    public ElectionRules electionRules = new ElectionRules();
}

[System.Serializable]
public class GovernmentInstitutionModifiers
{
    public float administrativeEfficiencyModifier;
    public float distanceLoyaltyPenaltyModifier;
    public float policyPointGenerationModifier;
    public float domesticTradeModifier;
    public float foreignTradeModifier;
    public int tradeRouteCapacityBonus;
    public float laborProductivityModifier;
    public float unemploymentUnhappinessModifier;
    public float reinforcementSpeedModifier;
    public float militaryUpkeepModifier;
    public float warWearinessModifier;
    public float corruptionModifier;
    public float unrestModifier;
    public float migrationAttractionModifier;
    public float planetaryLoyaltyModifier;
    public float cyberDefenseModifier;
}
