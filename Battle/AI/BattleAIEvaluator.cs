using System.Collections.Generic;

public sealed class BattleAIEvaluator
{
    public BattleCommand PickBestCommand(BattleSession session, BattleUnitState unit, BattleOccupancy occupancy, BattleDetectionService detection = null)
    {
        var candidates = BuildCandidates(session, unit, occupancy, detection);
        return candidates.Count > 0 ? candidates[0].Command : null;
    }

    public List<BattleAICandidate> BuildCandidates(BattleSession session, BattleUnitState unit, BattleOccupancy occupancy, BattleDetectionService detection = null)
    {
        if (session == null || unit == null || !unit.CanAct(session.ActiveSide))
            return new List<BattleAICandidate>();

        var candidates = new List<BattleAICandidate>(16);

        // Damaged units prefer a real friendly edge exit when one is adjacent.
        if (unit.CurrentHealth * 4 <= unit.Snapshot.MaximumHealth)
        {
            var current = session.Map.GetCell(unit.CellIndex);
            if (current?.NeighborIndices != null)
                for (int i = 0; i < current.NeighborIndices.Length; i++)
                {
                    var exit = session.Map.GetCell(current.NeighborIndices[i]);
                    if (exit?.RetreatExitForSide != unit.Side || !exit.Supports(unit.Domain))
                        continue;
                    candidates.Add(new BattleAICandidate(new BattleRetreatCommand
                    {
                        UnitId = unit.UnitId,
                        CommandType = BattleCommandType.Retreat,
                        ExitCell = exit.BattleIndex,
                    }, 40f));
                }
        }

        // Submarines use the depth layer tactically: dive when exposed and
        // surface to shallow water when deep weapons have no detected target.
        if (session.Theater == BattleTheater.Underwater && unit.Domain == BattleDomain.Underwater)
        {
            var current = session.Map.GetCell(unit.CellIndex);
            if (unit.DepthBand == BattleDepthBand.Shallow && current != null && current.WaterDepthLevel >= 2)
                candidates.Add(new BattleAICandidate(new BattleChangeDepthCommand
                {
                    UnitId = unit.UnitId, CommandType = BattleCommandType.ChangeDepth, Depth = BattleDepthBand.Deep,
                }, unit.RevealedByAttack ? 18f : 3f));
            else if (unit.DepthBand == BattleDepthBand.Deep)
                candidates.Add(new BattleAICandidate(new BattleChangeDepthCommand
                {
                    UnitId = unit.UnitId, CommandType = BattleCommandType.ChangeDepth, Depth = BattleDepthBand.Shallow,
                }, 2f));
        }

        // Attack candidates first
        for (int i = 0; i < session.Units.Count; i++)
        {
            var enemy = session.Units[i];
            if (enemy == null || !enemy.IsAliveAndActive || enemy.Side == unit.Side)
                continue;
            if (detection != null && !detection.CanDirectlyTarget(unit.Side, enemy))
                continue;

            int dist = session.MapDistance(unit.CellIndex, enemy.CellIndex);
            int weaponIndex = BattleTargetingService.FindWeaponIndex(unit, enemy, dist);
            if (weaponIndex < 0)
                continue;
            var weapon = BattleTargetingService.GetWeapon(unit, weaponIndex);
            if (!weapon.usesRangedAttack)
            {
                var attack = new BattleAttackCommand
                {
                    UnitId = unit.UnitId,
                    CommandType = BattleCommandType.MeleeAttack,
                    TargetUnitId = enemy.UnitId,
                    AttackFromCell = unit.CellIndex,
                    IsRanged = false,
                    WeaponIndex = weaponIndex,
                };
                float score = 20f + (enemy.CurrentHealth <= unit.Snapshot.MeleeAttack ? 10f : 0f);
                candidates.Add(new BattleAICandidate(attack, score));
                continue;
            }

            else
            {
                var attack = new BattleAttackCommand
                {
                    UnitId = unit.UnitId,
                    CommandType = BattleCommandType.RangedAttack,
                    TargetUnitId = enemy.UnitId,
                    AttackFromCell = unit.CellIndex,
                    IsRanged = true,
                    WeaponIndex = weaponIndex,
                };
                float score = 15f;
                candidates.Add(new BattleAICandidate(attack, score));
            }
        }

        // Move toward objective
        if (session.Objective.CellIndex >= 0 && unit.CurrentMovePoints > 0)
        {
            var cell = session.Map.GetCell(unit.CellIndex);
            if (cell?.NeighborIndices != null)
            {
                int bestCell = -1;
                int bestDist = int.MaxValue;
                for (int i = 0; i < cell.NeighborIndices.Length; i++)
                {
                    int n = cell.NeighborIndices[i];
                    if (!occupancy.CanEnter(unit, n, session.Map))
                        continue;

                    int d = session.MapDistance(n, session.Objective.CellIndex);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestCell = n;
                    }
                }

                if (bestCell >= 0)
                {
                    var move = new BattleMoveCommand
                    {
                        UnitId = unit.UnitId,
                        CommandType = BattleCommandType.Move,
                        Path = new[] { unit.CellIndex, bestCell },
                    };
                    float score = 8f;
                    candidates.Add(new BattleAICandidate(move, score));
                }
            }
        }

        // Wait preserves a delayed activation; defend remains the guaranteed
        // legal fallback if every theater-specific command fails.
        candidates.Add(new BattleAICandidate(new BattleWaitCommand
        {
            UnitId = unit.UnitId,
            CommandType = BattleCommandType.Wait,
        }, 2f));

        candidates.Add(new BattleAICandidate(new BattleDefendCommand
        {
            UnitId = unit.UnitId,
            CommandType = BattleCommandType.Defend,
        }, 1f));

        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        return candidates;
    }
}
