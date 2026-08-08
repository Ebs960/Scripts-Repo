using UnityEngine;

[CreateAssetMenu(menuName = "Data/Battle Attack Profile")]
public sealed class BattleAttackProfile : ScriptableObject
{
    [Header("Identity")]
    public string attackName = "Special Attack";
    [TextArea] public string description;

    [Header("Mode")]
    public bool isRanged;
    public bool isSpecial;

    [Header("Range")]
    [Min(0)] public int minimumRange;
    [Min(1)] public int maximumRange = 1;

    [Header("Splash")]
    public bool hasSplashDamage;
    [Min(0)] public int splashRadius = 1;
    [Range(0f, 1f)] public float splashDamageMultiplier = 0.5f;

    [Header("Damage")]
    [Range(0f, 5f)] public float damageMultiplier = 1f;

    [Header("Usage")]
    [Min(0)] public int cooldownRounds;
    [Min(0)] public int maxUses = -1;
}
