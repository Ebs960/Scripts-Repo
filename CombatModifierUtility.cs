/// <summary>Canonical global combat scaling. All arguments are fractional (0.10 = +10%).</summary>
public static class CombatModifierUtility
{
    public static float ApplyFractionalModifier(float baseValue, float modifier) => baseValue * (1f + modifier);
}
