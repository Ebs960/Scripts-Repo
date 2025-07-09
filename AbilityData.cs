using UnityEngine;

[CreateAssetMenu(fileName = "NewAbilityData", menuName = "Data/Ability Data")]
public class AbilityData : ScriptableObject
{
    public string abilityName;
    public Sprite icon;
    [TextArea] public string description;
    public int requiredLevel;

    [Header("Modifiers")]
    public int attackModifier;
    public int defenseModifier;
    public float damageMultiplier;
    
    // New modifiers
    public int healthModifier;
    public int rangeModifier;
    public int attackPointsModifier;

    public Ability CreateAbility()
    {
        return new Ability
        {
            abilityName      = abilityName,
            icon             = icon,
            description      = description,
            requiredLevel    = requiredLevel,
            attackModifier   = attackModifier,
            defenseModifier  = defenseModifier,
            damageMultiplier = damageMultiplier,
            healthModifier   = healthModifier,
            rangeModifier    = rangeModifier,
            attackPointsModifier = attackPointsModifier
        };
    }
} 