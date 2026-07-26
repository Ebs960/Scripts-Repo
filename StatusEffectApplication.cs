using UnityEngine;

[System.Serializable]
public class StatusEffectApplication
{
    public StatusEffectData effect;
    [Range(0f, 1f)] public float applicationChance = 1f;
    [Tooltip("-1 uses the effect asset duration.")] public int durationOverride = -1;
    [Min(0f)] public float magnitudeMultiplier = 1f;
    public bool meleeOnly;
    public bool rangedOnly;
    public bool applyToSelf;
    public bool applyToTarget = true;
    public bool useTargetCategoryFilter;
    public CombatCategory targetCategory;
    public bool useTargetDomainFilter;
    public CombatTargetDomain targetDomain;

    public float Chance => Mathf.Clamp01(applicationChance);
    public float MagnitudeMultiplier => magnitudeMultiplier <= 0f ? 1f : magnitudeMultiplier;
}
