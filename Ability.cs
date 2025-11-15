// Assets/Scripts/Units/Ability.cs
using UnityEngine;

public class Ability
{
    public string abilityName;
    public Sprite icon;
    public string description;
    public int requiredLevel;
    public int attackModifier;
    public int defenseModifier;
    public float damageMultiplier;
    
    // New modifiers for health and range
    public int healthModifier;
    public int rangeModifier;

    // TODO: add methods to apply effects, manage cooldowns, etc.
} 