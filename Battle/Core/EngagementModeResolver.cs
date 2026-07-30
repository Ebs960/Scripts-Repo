public static class EngagementModeResolver
{
    public static EngagementMode ResolveEngagementMode(BaseUnit attacker, BaseUnit defender)
    {
        if (attacker == null || defender == null)
            return EngagementMode.Unsupported;

        if (attacker is not CombatUnit atkCombat || defender is not CombatUnit defCombat)
            return EngagementMode.LegacyDirectAttack;

        if (atkCombat.currentHealth <= 0 || defCombat.currentHealth <= 0)
            return EngagementMode.Unsupported;

        if (!AircraftMissionManager.IsHostile(atkCombat.owner, defCombat.owner))
            return EngagementMode.Unsupported;

        // Every ordinary hostile CombatUnit engagement enters the same framework.
        // Map construction may still reject corrupt or genuinely unsupported data,
        // at which point callers retain their explicit migration fallback.
        return EngagementMode.TacticalBattle;
    }
}
