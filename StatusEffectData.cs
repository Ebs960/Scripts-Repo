using UnityEngine;

/// <summary>
/// Defines all possible status effect types in the game.
/// Each type maps to specific gameplay impacts applied through StatusEffect runtime instances.
/// </summary>
public enum StatusEffectType
{
    // Damage-over-time
    Poison,
    Burn,

    // Combat debuffs
    Weaken,         // Reduces attack
    Expose,         // Reduces defense
    Slow,           // Reduces movement points
    Suppression,    // Reduces attack + defense from ranged fire
    Fear,           // Reduces morale

    // Combat buffs
    Strengthen,     // Increases attack
    Fortitude,      // Increases defense
    Haste,          // Increases movement
    Inspire,        // Increases morale

    // Control
    Stun,           // Skip next action (0 AP)
    Root,           // Cannot move (0 MP) but can attack
    Blind,          // Reduces range

    // Recovery
    Regeneration,   // Heal-over-time
}

/// <summary>
/// How duplicate applications of the same effect are resolved.
/// </summary>
public enum StatusEffectStacking
{
    Replace,        // New application replaces old (resets duration)
    Refresh,        // Refresh duration but keep existing magnitude
    Stack,          // Add magnitude and take max duration
    Ignore          // If already active, do nothing
}

/// <summary>
/// ScriptableObject defining a status effect template.
/// Projectiles, abilities, and game events reference this to apply effects to units.
/// </summary>
[CreateAssetMenu(fileName = "NewStatusEffect", menuName = "Data/Status Effect Data")]
public class StatusEffectData : ScriptableObject
{
    [Header("Identity")]
    public string effectName;
    [TextArea(2, 4)]
    public string description;
    public Sprite icon;

    [Header("Behavior")]
    public StatusEffectType effectType;
    public StatusEffectStacking stacking = StatusEffectStacking.Refresh;

    [Header("Duration & Magnitude")]
    [Tooltip("Number of turns this effect lasts. 0 = instant (apply once and discard).")]
    public int baseDuration = 3;
    [Tooltip("Primary magnitude value. Interpretation depends on effectType:\n" +
             "  DoT/HoT: damage/heal per turn\n" +
             "  Stat modifiers: flat bonus/penalty\n" +
             "  Suppression/Fear: percentage (0-1)")]
    public float magnitude = 5f;

    [Header("Per-Turn Tick")]
    [Tooltip("If true, magnitude is applied each turn. If false, it modifies stats passively.")]
    public bool ticksPerTurn = false;

    [Header("Visual Feedback")]
    [Tooltip("Particle effect spawned on the affected unit while active")]
    public GameObject persistentVFX;
    [Tooltip("Particle effect spawned once on application")]
    public GameObject applyVFX;
    [Tooltip("Sound played on application")]
    public AudioClip applySound;
}
