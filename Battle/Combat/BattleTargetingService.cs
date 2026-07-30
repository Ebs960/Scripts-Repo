public readonly struct TargetingResult
{
    public readonly bool Allowed;
    public readonly string Reason;
    public TargetingResult(bool allowed, string reason = "") { Allowed = allowed; Reason = reason; }
}

/// <summary>Central data-driven cross-domain target validation.</summary>
public sealed class BattleTargetingService
{
    private readonly BattleDetectionService detection;
    public BattleTargetingService(BattleDetectionService detection) { this.detection = detection; }

    public TargetingResult CanTarget(BattleSession session, BattleUnitState attacker, BattleUnitState defender, bool ranged)
    {
        if (attacker == null || defender == null || attacker.Side == defender.Side)
            return new TargetingResult(false, "invalid target");
        if (!defender.IsAliveAndActive)
            return new TargetingResult(false, "target inactive");
        if (detection != null && !detection.CanDirectlyTarget(attacker.Side, defender))
            return new TargetingResult(false, "target undetected");

        var profile = attacker.Snapshot?.TacticalProfile;
        BattleDomainMask allowed = profile != null
            ? profile.targetDomains
            : BattleDomainResolver.ToMask(attacker.Domain);
        if ((allowed & BattleDomainResolver.ToMask(defender.Domain)) == 0)
            return new TargetingResult(false, "weapon cannot target domain");

        int distance = session.MapDistance(attacker.CellIndex, defender.CellIndex);
        int minimum = profile != null ? profile.minimumRange : 0;
        float maximum = ranged ? attacker.Snapshot.Range : 1f;
        if (distance < minimum || distance > maximum)
            return new TargetingResult(false, "target out of range");
        return new TargetingResult(true);
    }
}
