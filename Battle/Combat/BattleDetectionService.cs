using System.Collections.Generic;
using UnityEngine;

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

    public bool ActiveScan(BattleSession session, BattleUnitState scanner)
    {
        var profile = scanner?.Snapshot?.TacticalProfile;
        if (session == null || scanner == null || profile == null || profile.sensorRange <= 0) return false;
        bool contact = false;
        int range = profile.sensorRange + Mathf.Max(0, profile.activeSensorRangeBonus);
        foreach (var target in session.Units)
        {
            if (target == null || target.Side == scanner.Side || !target.IsAliveAndActive) continue;
            if ((profile.sensorDomains & BattleDomainResolver.ToMask(target.Domain)) == 0) continue;
            int depthPenalty = target.DepthBand == BattleDepthBand.Deep ? 2 : 0;
            if (session.MapDistance(scanner.CellIndex, target.CellIndex) <= range - depthPenalty)
            { levels[(scanner.Side, target.UnitId)] = BattleDetectionLevel.Identified; contact = true; }
        }
        scanner.RevealedByAttack = true; // active emissions reveal the scanner.
        return contact;
    }

    public void Update(BattleSession session, BattleSide observingSide)
    {
        foreach (var target in session.Units)
        {
            if (target.Side == observingSide || !target.IsAliveAndActive) continue;
            BattleDetectionLevel best = BattleDetectionLevel.Undetected;
            foreach (var detector in session.Units)
            {
                if (detector.Side != observingSide || !detector.IsAliveAndActive) continue;
                var profile = detector.Snapshot?.TacticalProfile;
                if (profile == null || profile.sensorRange <= 0) continue;
                if ((profile.sensorDomains & BattleDomainResolver.ToMask(target.Domain)) == 0) continue;
                int distance = session.MapDistance(detector.CellIndex, target.CellIndex);
                int depthPenalty = target.DepthBand == BattleDepthBand.Deep ? 3 : target.DepthBand == BattleDepthBand.Shallow ? 1 : 0;
                int stealth = target.Snapshot?.TacticalProfile != null ? target.Snapshot.TacticalProfile.stealth : 0;
                int effectiveRange = profile.sensorRange - depthPenalty - stealth + (target.RevealedByAttack ? 1 : 0);
                if (distance <= effectiveRange)
                    best = target.RevealedByAttack ? BattleDetectionLevel.Identified : BattleDetectionLevel.Detected;
                else if (distance <= effectiveRange + 2)
                    best = best == BattleDetectionLevel.Undetected ? BattleDetectionLevel.Suspected : best;
            }
            var key = (observingSide, target.UnitId);
            if (best == BattleDetectionLevel.Undetected) levels.Remove(key);
            else levels[key] = best;
        }
    }
}
