using UnityEngine;

[CreateAssetMenu(menuName="CivGame/Religion/Belief")]
public class BeliefData : ScriptableObject
{
    [Header("Identity")]
    public string beliefName;
    [TextArea] public string description;
    public Sprite icon;

    [Header("Effects")]
    [Tooltip("Flat bonus faith in Holy Site per turn")]
    public int extraFaithInHolySite;
    [Tooltip("Flat bonus food in Holy Site per turn")]
    public int extraFoodInHolySite;
    [Tooltip("Flat bonus production in Holy Site per turn")]
    public int extraProductionInHolySite;
    [Tooltip("Bonus gold in cities with this religion")]
    public int goldPerCity;
    [Tooltip("Bonus culture in cities with this religion")]
    public int culturePerCity;
    [Tooltip("Happiness bonus in cities with this religion")]
    public int happinessBonus;
    [Tooltip("Combat strength bonus for units near Holy Site")]
    public int combatStrengthNearHolySite;
    [Tooltip("Growth rate bonus in cities with this religion")]
    public float growthRateModifier;
    [Tooltip("Production rate bonus in cities with this religion")]
    public float productionRateModifier;

    // Adding percentage-based yield modifiers for consistency
    [Header("Percentage Yield Modifiers")]
    public float foodModifier;          // New
    public float productionModifier;    // New
    public float goldModifier;          // New
    public float scienceModifier;       // New
    public float cultureModifier;       // New
    public float faithModifier;         // New
} 