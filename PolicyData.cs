using UnityEngine;

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
             "Use personality filters to target specific lord archetypes.")]
    public GovernorOpinionEffect[] governorOpinionEffects;
} 