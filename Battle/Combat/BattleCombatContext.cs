public readonly struct BattleCombatContext
{
    public readonly BattleUnitState Attacker;
    public readonly BattleUnitState Defender;

    public readonly bool IsMelee;
    public readonly bool IsRanged;
    public readonly bool IsCounterAttack;

    public readonly int AttackerElevation;
    public readonly int DefenderElevation;

    public readonly bool DefenderHasSoftCover;
    public readonly bool DefenderHasHardCover;
    public readonly bool DefenderIsDefending;
    public readonly bool DefenderIsExposed;

    public readonly int FlankingCount;
    public readonly int RandomSeed;
    public readonly TacticalWeaponProfile Weapon;

    public BattleCombatContext(
        BattleUnitState attacker,
        BattleUnitState defender,
        bool isMelee,
        bool isRanged,
        bool isCounterAttack,
        int attackerElevation,
        int defenderElevation,
        bool defenderHasSoftCover,
        bool defenderHasHardCover,
        bool defenderIsDefending,
        bool defenderIsExposed,
        int flankingCount,
        int randomSeed,
        TacticalWeaponProfile weapon = null)
    {
        Attacker = attacker;
        Defender = defender;
        IsMelee = isMelee;
        IsRanged = isRanged;
        IsCounterAttack = isCounterAttack;
        AttackerElevation = attackerElevation;
        DefenderElevation = defenderElevation;
        DefenderHasSoftCover = defenderHasSoftCover;
        DefenderHasHardCover = defenderHasHardCover;
        DefenderIsDefending = defenderIsDefending;
        DefenderIsExposed = defenderIsExposed;
        FlankingCount = flankingCount;
        RandomSeed = randomSeed;
        Weapon = weapon;
    }
}

public readonly struct BattleCombatResult
{
    public readonly int Damage;
    public readonly bool DefenderDied;
    public readonly float EffectiveAttack;
    public readonly float EffectiveDefense;

    public BattleCombatResult(int damage, bool defenderDied, float effectiveAttack, float effectiveDefense)
    {
        Damage = damage;
        DefenderDied = defenderDied;
        EffectiveAttack = effectiveAttack;
        EffectiveDefense = effectiveDefense;
    }
}
