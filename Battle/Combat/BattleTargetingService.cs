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

    public TargetingResult CanTarget(BattleSession session, BattleUnitState attacker, BattleUnitState defender, bool ranged, int weaponIndex = 0)
    {
        if (attacker == null || defender == null || attacker.Side == defender.Side)
            return new TargetingResult(false, "invalid target");
        if (!defender.IsAliveAndActive)
            return new TargetingResult(false, "target inactive");
        if (detection != null && !detection.CanDirectlyTarget(attacker.Side, defender))
            return new TargetingResult(false, "target undetected");

        if (defender.Domain == BattleDomain.Underwater
            && attacker.Domain != BattleDomain.Underwater
            && attacker.Snapshot?.UnitData != null
            && !attacker.Snapshot.UnitData.canAttackUnderwater)
            return new TargetingResult(false, "unit lacks anti-underwater capability");

        TacticalWeaponProfile weapon = GetWeapon(attacker, weaponIndex);
        if (weapon == null)
            return new TargetingResult(false, "weapon not available");
        if (weapon.usesRangedAttack != ranged)
            return new TargetingResult(false, "weapon attack mode mismatch");
        BattleDomainMask allowed = weapon.targetDomains;
        if ((allowed & BattleDomainResolver.ToMask(defender.Domain)) == 0)
            return new TargetingResult(false, "weapon cannot target domain");

        int distance = session.MapDistance(attacker.CellIndex, defender.CellIndex);
        int minimum = weapon.minimumRange;
        float maximum = weapon.maximumRange;
        if (distance < minimum || distance > maximum)
            return new TargetingResult(false, "target out of range");
        return new TargetingResult(true);
    }

    public static int FindWeaponIndex(BattleUnitState attacker, BattleUnitState defender, int distance)
    {
        if (attacker?.Snapshot?.Weapons == null || defender == null)
            return -1;

        BattleDomainMask targetDomain = BattleDomainResolver.ToMask(defender.Domain);
        for (int i = 0; i < attacker.Snapshot.Weapons.Count; i++)
        {
            var weapon = attacker.Snapshot.Weapons[i];
            if (weapon != null
                && (i >= attacker.WeaponAmmo.Count || attacker.WeaponAmmo[i] != 0)
                && (i >= attacker.WeaponCooldowns.Count || attacker.WeaponCooldowns[i] <= 0)
                && (weapon.targetDomains & targetDomain) != 0
                && distance >= weapon.minimumRange
                && distance <= weapon.maximumRange)
                return i;
        }
        return -1;
    }

    public static TacticalWeaponProfile GetWeapon(BattleUnitState unit, int index)
    {
        if (unit?.Snapshot?.Weapons == null || index < 0 || index >= unit.Snapshot.Weapons.Count)
            return null;
        return unit.Snapshot.Weapons[index];
    }
}
