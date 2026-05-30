using UnityEngine;

[CreateAssetMenu(fileName = "NewGovernmentData", menuName = "Data/Government Data")]
public class GovernmentData : ScriptableObject
{
    [Header("Identity")]
    public string governmentName;
    public string leaderTitleSuffix;    // e.g. "Emperor", "Chieftain"
    [TextArea] public string description;

    [Header("Cost & Requirements")]
    [Tooltip("Policy points to enact this government")]
    public int policyPointCost;
    public TechData[] requiredTechs; // Changed from TechnologyData to TechData
    public CultureData[] requiredCultures;
    public int requiredCityCount;

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

    [Header("Herd Modifiers")]
    [Tooltip("Reduces the percent of herd animals lost to starvation (e.g. 0.05 = -5 percentage points)")]
    public float herdStarvationPercentReduction = 0f;
    [Tooltip("Per-herd per-turn yield bonuses granted by this government (can filter by animal species).")]
    public HerdYieldBonus[] herdYieldBonuses;

    [Header("Council & Political Structure")]
    [Tooltip("How many governors may sit on the royal council under this government type.")]
    public int councilSeatCount = 0;
    [Tooltip("Which domains the seated council may veto. Flags — combine freely.")]
    public VetoDomain councilVetoDomains = VetoDomain.None;
    [Tooltip("Opinion reactions pushed to governors when this government is adopted. " +
             "Use negative values to anger ambitious/discontented lords.")]
    public GovernorOpinionEffect[] governorOpinionEffects;
} 