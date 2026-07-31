using System.Collections.Generic;

public sealed class BattleAIEvaluator
{
    public BattleCommand PickBestCommand(BattleSession session, BattleUnitState unit, BattleOccupancy occupancy, BattleDetectionService detection = null)
    {
        if (session == null || unit == null || !unit.CanAct(session.ActiveSide))
            return null;

        var candidates = new List<BattleAICandidate>(16);

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

        // Defend fallback
        candidates.Add(new BattleAICandidate(new BattleDefendCommand
        {
            UnitId = unit.UnitId,
            CommandType = BattleCommandType.Defend,
        }, 1f));

        BattleAICandidate best = candidates[0];
        for (int i = 1; i < candidates.Count; i++)
        {
            if (candidates[i].Score > best.Score)
                best = candidates[i];
        }

        return best.Command;
    }
}
