public readonly struct BattleDamagePreview
{
    public readonly string WeaponName;
    public readonly string RejectionReason;
    public readonly int PredictedDamage;
    public readonly BattleLosBlockReason LosReason;
    public readonly bool CanAttack;
    public readonly bool IsRanged, IsSpecial, HasSoftCover, HasHardCover, CounterattackPossible;
    public readonly int Range, MinimumRange, MaximumRange, AmmoRemaining, CooldownRemaining, ElevationDelta, PredictedCounterDamage;

    public BattleDamagePreview(string weaponName,bool canAttack,string rejectionReason,int predictedDamage,BattleLosBlockReason losReason,bool isRanged,bool isSpecial,int range,int minimumRange,int maximumRange,int ammoRemaining,int cooldownRemaining,bool soft,bool hard,int elevationDelta,bool counterattackPossible,int predictedCounterDamage)
    {
        WeaponName=weaponName;CanAttack=canAttack;RejectionReason=rejectionReason;PredictedDamage=predictedDamage;LosReason=losReason;IsRanged=isRanged;IsSpecial=isSpecial;Range=range;MinimumRange=minimumRange;MaximumRange=maximumRange;AmmoRemaining=ammoRemaining;CooldownRemaining=cooldownRemaining;HasSoftCover=soft;HasHardCover=hard;ElevationDelta=elevationDelta;CounterattackPossible=counterattackPossible;PredictedCounterDamage=predictedCounterDamage;
    }
}
