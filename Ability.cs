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
    
    // New modifiers for health, range, attack points, and movement
    public int healthModifier;
    public int rangeModifier;
    public int attackPointsModifier;
    public int movePointsModifier;

    // TODO: add methods to apply effects, manage cooldowns, etc.
} 