using UnityEngine;

public sealed class BattleCombatResolver
{
    private readonly BattleRuleset ruleset;

    public BattleCombatResolver(BattleRuleset ruleset)
    {
        this.ruleset = ruleset;
    }

    public BattleCombatResult Resolve(in BattleCombatContext ctx)
    {
        return Resolve(ctx, new BattleDeterministicRandom(ctx.RandomSeed));
    }

    public BattleCombatResult Resolve(in BattleCombatContext ctx, BattleDeterministicRandom random)
    {
        int attackStrength = ctx.IsRanged ? ctx.Attacker.Snapshot.RangedAttack : ctx.Attacker.Snapshot.MeleeAttack;
        attackStrength = Mathf.Max(0, Mathf.RoundToInt((attackStrength + GetDomainWeaponBonus(ctx.Weapon, ctx.Defender.Domain))
            * (ctx.Weapon != null ? Mathf.Max(0.01f, ctx.Weapon.attackMultiplier) : 1f)));
        int defenseStrength = ctx.Defender.Snapshot.Defense;

        float highGroundAttack = ctx.AttackerElevation > ctx.DefenderElevation ? ruleset.highGroundAttackMultiplier : 1f;
        float highGroundDefense = ctx.DefenderElevation > ctx.AttackerElevation ? ruleset.highGroundDefenseMultiplier : 1f;

        float coverMul = 1f;
        if (ctx.DefenderHasHardCover)
            coverMul *= ruleset.hardCoverDefenseMultiplier;
        else if (ctx.DefenderHasSoftCover)
            coverMul *= ruleset.softCoverDefenseMultiplier;

        float defendMul = ctx.DefenderIsDefending ? ruleset.defendMultiplier : 1f;
        float exposedMul = ctx.DefenderIsExposed ? ruleset.exposedDefenseMultiplier : 1f;

        float flankMul = 1f + (0.05f * Mathf.Max(0, ctx.FlankingCount));

        float effectiveAttack = attackStrength * highGroundAttack * flankMul * ctx.Attacker.CommanderAttackMultiplier;
        float effectiveDefense = defenseStrength * highGroundDefense * coverMul * defendMul * exposedMul * ctx.Defender.CommanderDefenseMultiplier;

        float scale = Mathf.Max(1f, Mathf.Max(effectiveAttack, effectiveDefense));
        float normalizedAdvantage = (effectiveAttack - effectiveDefense) / scale;

        float t = Mathf.InverseLerp(ruleset.minAdvantage, ruleset.maxAdvantage, normalizedAdvantage);
        float damagePct = Mathf.Lerp(ruleset.minDamagePercent, ruleset.maxDamagePercent, t);
        int damage = Mathf.RoundToInt(ctx.Defender.Snapshot.MaximumHealth * damagePct);

        float jitter = 0.95f + ((random ?? new BattleDeterministicRandom(ctx.RandomSeed)).NextUnitFloat() * 0.1f);
        damage = Mathf.Max(1, Mathf.RoundToInt(damage * jitter));

        bool dead = ctx.Defender.CurrentHealth - damage <= 0;
        return new BattleCombatResult(damage, dead, effectiveAttack, effectiveDefense);
    }

    private static float GetDomainWeaponBonus(TacticalWeaponProfile weapon, BattleDomain target)
    {
        var equipment = weapon?.equipment;
        if (equipment == null) return 0f;
        float bonus = equipment.attackBonus + (weapon.usesRangedAttack ? equipment.rangedAttackBonus : equipment.meleeAttackBonus);
        bonus += target switch
        {
            BattleDomain.Underwater => equipment.underwaterAttackBonus,
            BattleDomain.Air => equipment.airAttackBonus,
            BattleDomain.Orbit => equipment.spaceAttackBonus,
            BattleDomain.Space => equipment.spaceAttackBonus,
            _ => equipment.groundAttackBonus,
        };
        return bonus;
    }
}
