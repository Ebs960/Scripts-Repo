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
        int attackStrength = ctx.IsRanged ? ctx.Attacker.Snapshot.RangedAttack : ctx.Attacker.Snapshot.MeleeAttack;
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

        float effectiveAttack = attackStrength * highGroundAttack * flankMul;
        float effectiveDefense = defenseStrength * highGroundDefense * coverMul * defendMul * exposedMul;

        float scale = Mathf.Max(1f, Mathf.Max(effectiveAttack, effectiveDefense));
        float normalizedAdvantage = (effectiveAttack - effectiveDefense) / scale;

        float t = Mathf.InverseLerp(ruleset.minAdvantage, ruleset.maxAdvantage, normalizedAdvantage);
        float damagePct = Mathf.Lerp(ruleset.minDamagePercent, ruleset.maxDamagePercent, t);
        int damage = Mathf.RoundToInt(ctx.Defender.Snapshot.MaximumHealth * damagePct);

        var rng = new System.Random(ctx.RandomSeed ^ (ctx.Attacker.UnitId * 92821) ^ (ctx.Defender.UnitId * 31337));
        float jitter = 0.95f + ((float)rng.NextDouble() * 0.1f);
        damage = Mathf.Max(1, Mathf.RoundToInt(damage * jitter));

        bool dead = ctx.Defender.CurrentHealth - damage <= 0;
        return new BattleCombatResult(damage, dead, effectiveAttack, effectiveDefense);
    }
}
