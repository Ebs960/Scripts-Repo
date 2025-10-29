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
    [Tooltip("Per-equipment per-turn yield bonuses granted by this government (applies when equipped).")]
    public EquipmentYieldBonus[] equipmentYieldBonuses;
    [Tooltip("Per-worker per-turn yield bonuses granted by this government while active.")]
    public WorkerUnitYieldBonus[] workerYieldBonuses;

    // REMOVED: Unlocked Content arrays
    // Availability is now controlled solely by requiredTechs/requiredCultures in the respective data classes
    // Government-specific units should have GovernmentData in their requiredTechs or requiredCultures
} 