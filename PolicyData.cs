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
    public float defenseBonus;
    public float movementBonus;
    public float foodModifier;          // New
    public float productionModifier;    // New
    public float goldModifier;          // New
    public float scienceModifier;       // New
    public float cultureModifier;       // New
    public float faithModifier;         // New

    [Header("Governor Bonuses")]
    public int additionalGovernorSlots;
    public GovernorTrait[] unlockedGovernorTraits;
} 