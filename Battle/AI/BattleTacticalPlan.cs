using System.Collections.Generic;

public enum BattlePlanPosture { Assault, HoldObjective, PreserveForces, AmphibiousLanding, DepthControl, DistanceControl }

/// <summary>Deterministic side-level intent shared by every activation in a side turn.</summary>
public sealed class BattleTacticalPlan
{
    public BattleSide Side;
    public BattleTheater Theater;
    public BattlePlanPosture Posture;
    public int ObjectiveCell;
    public int FocusTargetUnitId = -1;
    public int BuiltRound;
    public readonly List<int> ActivationOrder = new();

    public static BattleTacticalPlan Build(BattleSession session, BattleSide side, BattleDetectionService detection)
    {
        var plan = new BattleTacticalPlan { Side=side, Theater=session.Theater, ObjectiveCell=session.Objective.CellIndex, BuiltRound=session.CurrentRound };
        bool ownsObjective = session.Objective.Owner == side;
        plan.Posture = session.Theater switch
        {
            BattleTheater.Underwater => BattlePlanPosture.DepthControl,
            BattleTheater.DeepSpace => BattlePlanPosture.DistanceControl,
            _ when ownsObjective => BattlePlanPosture.HoldObjective,
            _ => BattlePlanPosture.Assault,
        };
        if (session.CurrentRound >= session.MaximumRounds - 1 && !ownsObjective) plan.Posture = BattlePlanPosture.PreserveForces;

        int lowestHealth = int.MaxValue;
        foreach (var unit in session.Units)
        {
            if (unit == null || unit.Side == side || !unit.IsAliveAndActive || detection != null && !detection.CanDirectlyTarget(side, unit)) continue;
            if (unit.CurrentHealth < lowestHealth || unit.CurrentHealth == lowestHealth && unit.UnitId < plan.FocusTargetUnitId)
            { lowestHealth = unit.CurrentHealth; plan.FocusTargetUnitId = unit.UnitId; }
        }
        var friendly = new List<BattleUnitState>();
        foreach (var unit in session.Units) if (unit != null && unit.Side == side && !unit.IsDead) friendly.Add(unit);
        friendly.Sort((a,b) =>
        {
            int aScore = ActivationScore(session, a, plan), bScore = ActivationScore(session, b, plan);
            int score = bScore.CompareTo(aScore); return score != 0 ? score : a.UnitId.CompareTo(b.UnitId);
        });
        foreach (var unit in friendly) plan.ActivationOrder.Add(unit.UnitId);
        return plan;
    }

    private static int ActivationScore(BattleSession session, BattleUnitState unit, BattleTacticalPlan plan)
    {
        int score = unit.Snapshot?.Weapons?.Count > 0 ? 10 : 0;
        if (unit.Domain == BattleDomain.Air || unit.Domain == BattleDomain.Space) score += unit.FuelOrEndurance <= 1 && unit.FuelOrEndurance >= 0 ? 30 : 0;
        int distance = unit.CellIndex >= 0 ? session.MapDistance(unit.CellIndex, plan.ObjectiveCell) : int.MaxValue;
        if (distance != int.MaxValue) score += System.Math.Max(0, 12 - distance);
        if (unit.CurrentHealth * 3 <= unit.Snapshot.MaximumHealth) score -= 10;
        return score;
    }
}
