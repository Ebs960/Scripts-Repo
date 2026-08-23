using System.Linq;

/// <summary>Central campaign entry point for engagements whose objective is a Band.</summary>
public static class CampaignEngagementService
{
    public static EngagementPreview AttackBand(CombatUnit attacker, Band defender, out string reason)
    {
        reason = string.Empty;
        if (attacker == null || defender == null) { reason = "Missing attacker or Band."; return null; }
        if (defender.Garrison.Count == 0)
        {
            if (!attacker.TryConsumeAttackPoint()) { reason = "Attacker has no actions remaining."; return null; }
            if (attacker.data != null && attacker.data.unitType == CombatCategory.Animal) defender.DestroyBand(BandLossReason.AnimalAttack);
            else defender.Capture(attacker.owner);
            return null;
        }
        var manager = BattleManager.Instance;
        if (manager == null) { reason = "Battle manager unavailable."; return null; }
        var preview = manager.RequestEngagement(CampaignBattleParty.FromArmy(attacker), CampaignBattleParty.FromBand(defender));
        if (!preview.IsValid) reason = preview.RejectionReason;
        return preview;
    }

    public static EngagementPreview AttackArmy(Band attacker, CombatUnit defender, out string reason)
    {
        reason = string.Empty;
        if (attacker == null || attacker.State != BandState.Packed) { reason = "Cannot attack: Band must be Packed."; return null; }
        if (!CampaignBattleParty.FromBand(attacker).CombatUnits.Any(x => x != null && x.currentHealth > 0))
        { reason = "Cannot attack: Band has no combat units."; return null; }
        if (defender == null || BattleManager.Instance == null) { reason = "Missing defender or battle manager."; return null; }
        var preview = BattleManager.Instance.RequestEngagement(CampaignBattleParty.FromBand(attacker), CampaignBattleParty.FromArmy(defender));
        if (!preview.IsValid) reason = preview.RejectionReason;
        return preview;
    }

    public static EngagementPreview AttackHerd(CombatUnit attacker, Herd defender, out string reason)
    {
        reason = string.Empty;
        if (attacker == null || defender == null || attacker.owner == defender.owner) { reason = "Missing or friendly target."; return null; }
        if (!defender.HasMilitaryDefenders)
        {
            if (!attacker.TryConsumeAttackPoint()) { reason = "Attacker has no actions remaining."; return null; }
            if (attacker.data != null && attacker.data.unitType == CombatCategory.Animal) defender.ResolveLivestockRaid(); else defender.Capture(attacker.owner);
            return null;
        }
        if (BattleManager.Instance == null) { reason = "Battle manager unavailable."; return null; }
        var preview = BattleManager.Instance.RequestEngagement(CampaignBattleParty.FromArmy(attacker), CampaignBattleParty.FromHerd(defender));
        if (!preview.IsValid) reason = preview.RejectionReason; return preview;
    }

    public static EngagementPreview AttackArmy(Herd attacker, CombatUnit defender, out string reason)
    {
        reason = string.Empty;
        if (attacker == null || !attacker.isPacked) { reason = "Cannot attack: Herd must be Packed."; return null; }
        if (!attacker.HasMilitaryDefenders) { reason = "Cannot attack: Herd has no combat units."; return null; }
        if (defender == null || BattleManager.Instance == null) { reason = "Missing defender or battle manager."; return null; }
        var preview = BattleManager.Instance.RequestEngagement(CampaignBattleParty.FromHerd(attacker), CampaignBattleParty.FromArmy(defender));
        if (!preview.IsValid) reason = preview.RejectionReason; return preview;
    }
}
