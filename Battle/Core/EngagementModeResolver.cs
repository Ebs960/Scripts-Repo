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

        if (atkCombat.planetIndex != defCombat.planetIndex)
            return EngagementMode.LegacyDirectAttack;

        if (atkCombat.currentLayer != TileLayer.Surface || defCombat.currentLayer != TileLayer.Surface)
            return atkCombat.currentLayer switch
            {
                TileLayer.Atmosphere => EngagementMode.AirMission,
                TileLayer.Orbit => EngagementMode.OrbitalAttack,
                TileLayer.Underwater => EngagementMode.NavalCombat,
                _ => EngagementMode.LegacyDirectAttack,
            };

        if (atkCombat.data != null && (atkCombat.data.unitType == CombatCategory.Aircraft || atkCombat.data.unitType == CombatCategory.Spaceship || CombatUnitData.IsNavalCategory(atkCombat.data.unitType)))
            return EngagementMode.LegacyDirectAttack;

        if (defCombat.data != null && defCombat.data.unitType == CombatCategory.Animal)
            return EngagementMode.LegacyDirectAttack;

        if (!AircraftMissionManager.IsHostile(atkCombat.owner, defCombat.owner))
            return EngagementMode.Unsupported;

        return EngagementMode.TacticalLandBattle;
    }
}
