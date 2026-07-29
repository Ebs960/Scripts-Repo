public readonly struct BattleDamagePreview
{
    public readonly int PredictedDamage;
    public readonly BattleLosBlockReason LosReason;
    public readonly bool CanAttack;

    public BattleDamagePreview(int predictedDamage, BattleLosBlockReason losReason, bool canAttack)
    {
        PredictedDamage = predictedDamage;
        LosReason = losReason;
        CanAttack = canAttack;
    }
}
