public enum BattleStatusEffectType
{
    Exposed,
}

public sealed class BattleStatusEffect
{
    public BattleStatusEffectType Type;
    public int RemainingRounds;
    public float Magnitude = 1f;
}
