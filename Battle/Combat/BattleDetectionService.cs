using System.Collections.Generic;

/// <summary>Stores fog-of-war knowledge per observing side; hidden positions are never global.</summary>
public sealed class BattleDetectionService
{
    private readonly Dictionary<(BattleSide side, int unitId), BattleDetectionLevel> levels = new();

    public BattleDetectionLevel GetLevel(BattleSide side, BattleUnitState target)
    {
        if (target == null) return BattleDetectionLevel.Undetected;
        if (target.Side == side || target.RevealedByAttack) return BattleDetectionLevel.Identified;
        if (levels.TryGetValue((side, target.UnitId), out var level)) return level;
        return target.Domain == BattleDomain.Underwater ? BattleDetectionLevel.Undetected : BattleDetectionLevel.Detected;
    }

    public bool CanDirectlyTarget(BattleSide side, BattleUnitState target) => GetLevel(side, target) >= BattleDetectionLevel.Detected;
    public void Reveal(BattleSide side, BattleUnitState target, BattleDetectionLevel level = BattleDetectionLevel.Detected)
    { if (target != null && level > GetLevel(side, target)) levels[(side, target.UnitId)] = level; }

    public void Update(BattleSession session, BattleSide observingSide)
    {
        foreach (var detector in session.Units)
        {
            if (detector.Side != observingSide || !detector.IsAliveAndActive) continue;
            var profile = detector.Snapshot?.TacticalProfile;
            if (profile == null || profile.sensorRange <= 0) continue;
            foreach (var target in session.Units)
            {
                if (target.Side == observingSide || !target.IsAliveAndActive) continue;
                if ((profile.sensorDomains & BattleDomainResolver.ToMask(target.Domain)) == 0) continue;
                if (session.MapDistance(detector.CellIndex, target.CellIndex) <= profile.sensorRange + (target.RevealedByAttack ? 1 : 0))
                    Reveal(observingSide, target);
            }
        }
    }
}
