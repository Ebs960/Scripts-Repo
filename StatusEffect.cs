using UnityEngine;

/// <summary>
/// Runtime instance of an active status effect on a unit.
/// Created from StatusEffectData when applied, tracked per-unit in BaseUnit.activeStatusEffects.
/// </summary>
public class StatusEffect
{
    public StatusEffectData data;
    public int remainingTurns;
    public float magnitude;
    public BaseUnit source; // who applied this (nullable for environmental)
    public GameObject persistentVFXInstance;

    public bool IsExpired => remainingTurns <= 0 && data.baseDuration > 0;

    public StatusEffect(StatusEffectData data, BaseUnit source)
    {
        this.data = data;
        this.source = source;
        this.remainingTurns = data.baseDuration;
        this.magnitude = data.magnitude;
    }

    /// <summary>
    /// Called once per turn. Returns damage dealt (positive) or healed (negative). 0 for non-tick effects.
    /// </summary>
    public int Tick()
    {
        if (remainingTurns > 0)
            remainingTurns--;

        if (!data.ticksPerTurn)
            return 0;

        switch (data.effectType)
        {
            case StatusEffectType.Poison:
            case StatusEffectType.Burn:
                return Mathf.RoundToInt(magnitude); // damage

            case StatusEffectType.Regeneration:
                return -Mathf.RoundToInt(magnitude); // heal (negative = healing)

            default:
                return 0;
        }
    }

    /// <summary>
    /// Get the attack modifier this effect contributes (flat value, positive = buff).
    /// </summary>
    public float GetAttackModifier()
    {
        return data.effectType switch
        {
            StatusEffectType.Weaken => -magnitude,
            StatusEffectType.Strengthen => magnitude,
            StatusEffectType.Suppression => -magnitude,
            StatusEffectType.Stun => -9999f, // effectively prevents attack
            _ => 0f
        };
    }

    /// <summary>
    /// Get the defense modifier this effect contributes (flat value, positive = buff).
    /// </summary>
    public float GetDefenseModifier()
    {
        return data.effectType switch
        {
            StatusEffectType.Expose => -magnitude,
            StatusEffectType.Fortitude => magnitude,
            StatusEffectType.Suppression => -magnitude * 0.5f, // suppression hits defense less than attack
            _ => 0f
        };
    }

    /// <summary>
    /// Get the movement modifier (flat MP, positive = buff).
    /// </summary>
    public int GetMovementModifier()
    {
        return data.effectType switch
        {
            StatusEffectType.Slow => -Mathf.RoundToInt(magnitude),
            StatusEffectType.Haste => Mathf.RoundToInt(magnitude),
            StatusEffectType.Root => -9999,
            StatusEffectType.Stun => -9999,
            _ => 0
        };
    }

    /// <summary>
    /// Get the range modifier (flat, positive = buff).
    /// </summary>
    public float GetRangeModifier()
    {
        return data.effectType switch
        {
            StatusEffectType.Blind => -magnitude,
            _ => 0f
        };
    }

    /// <summary>
    /// Get the morale modifier (flat, positive = buff).
    /// </summary>
    public float GetMoraleModifier()
    {
        return data.effectType switch
        {
            StatusEffectType.Fear => -magnitude,
            StatusEffectType.Inspire => magnitude,
            _ => 0f
        };
    }

    /// <summary>
    /// Clean up VFX when effect expires.
    /// </summary>
    public void Cleanup()
    {
        if (persistentVFXInstance != null)
        {
            Object.Destroy(persistentVFXInstance);
            persistentVFXInstance = null;
        }
    }
}
