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
        bool embarkedActivation = unit != null && unit.Side == session?.ActiveSide && unit.IsEmbarked
            && !unit.IsDead && unit.CurrentActionPoints > 0;
        if (session == null || unit == null || (!unit.CanAct(session.ActiveSide) && !embarkedActivation))
            return new List<BattleAICandidate>();

        var candidates = new List<BattleAICandidate>(16);

        if (unit.Snapshot?.TacticalProfile != null && unit.Snapshot.TacticalProfile.sensorRange > 0)
            candidates.Add(new BattleAICandidate(new BattleActiveDetectionCommand
            { UnitId = unit.UnitId, CommandType = BattleCommandType.ActiveDetection }, 6f));

        if (unit.IsEmbarked)
        {
            BattleUnitState host = null;
            for (int i = 0; i < session.Units.Count; i++)
                if (session.Units[i]?.UnitId == unit.CarrierOrTransportBattleUnitId) { host = session.Units[i]; break; }
            var hostCell = host != null ? session.Map.GetCell(host.CellIndex) : null;
            if (hostCell?.NeighborIndices != null)
                for (int i = 0; i < hostCell.NeighborIndices.Length; i++)
                {
                    var destination = session.Map.GetCell(hostCell.NeighborIndices[i]);
                    if (destination == null || !destination.Supports(unit.Domain)
                        || occupancy.IsOccupied(destination.BattleIndex, unit.Domain, unit.OccupancyBand)) continue;
                    BattleCommand command = unit.Domain == BattleDomain.Air || unit.Domain == BattleDomain.Space
                        ? new BattleLaunchAircraftCommand { UnitId = host.UnitId, CommandType = BattleCommandType.LaunchAircraft, AircraftUnitId = unit.UnitId, LaunchCell = destination.BattleIndex }
                        : new BattleDisembarkCommand { UnitId = unit.UnitId, CommandType = BattleCommandType.Disembark, DestinationCell = destination.BattleIndex };
                    candidates.Add(new BattleAICandidate(command, destination.IsObjective ? 30f : 14f));
                }
            candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
            return candidates;
        }

        // Recover aircraft before fuel/endurance expires.
        if ((unit.Domain == BattleDomain.Air || unit.Domain == BattleDomain.Space) && unit.FuelOrEndurance >= 0 && unit.FuelOrEndurance <= 1)
            for (int i = 0; i < session.Units.Count; i++)
            {
                var carrier = session.Units[i];
                if (carrier == null || carrier.Side != unit.Side || carrier.IsDead || carrier.IsEmbarked) continue;
                if (session.MapDistance(unit.CellIndex, carrier.CellIndex) > 1) continue;
                candidates.Add(new BattleAICandidate(new BattleRecoverAircraftCommand
                { UnitId = unit.UnitId, CommandType = BattleCommandType.RecoverAircraft, CarrierUnitId = carrier.UnitId }, 50f));
            }

        // Land and air units can board a compatible adjacent transport.
        for (int i = 0; i < session.Units.Count; i++)
        {
            var transport = session.Units[i];
            if (transport == null || transport.Side != unit.Side || transport.IsDead || transport.IsEmbarked) continue;
            if (session.MapDistance(unit.CellIndex, transport.CellIndex) > 1) continue;
            var data = transport.Snapshot?.UnitData;
            if (data == null || !data.isTransport || unit.Snapshot?.UnitData == null || !data.CanCarryUnitCategory(unit.Snapshot.UnitData.unitType)) continue;
            candidates.Add(new BattleAICandidate(new BattleEmbarkCommand
            { UnitId = unit.UnitId, CommandType = BattleCommandType.Embark, TransportUnitId = transport.UnitId }, 4f));
        }

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
            var path = FindObjectivePath(session, unit, occupancy);
            if (path != null && path.Count > 1)
            {
                candidates.Add(new BattleAICandidate(new BattleMoveCommand
                { UnitId = unit.UnitId, CommandType = BattleCommandType.Move, Path = path }, 8f + path.Count * .1f));
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

    private static List<int> FindObjectivePath(BattleSession session, BattleUnitState unit, BattleOccupancy occupancy)
    {
        var queue = new Queue<int>(); var previous = new Dictionary<int, int>();
        queue.Enqueue(unit.CellIndex); previous[unit.CellIndex] = -1;
        int best = unit.CellIndex, bestDistance = session.MapDistance(best, session.Objective.CellIndex);
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            var cell = session.Map.GetCell(current);
            if (cell?.NeighborIndices == null) continue;
            foreach (int next in cell.NeighborIndices)
            {
                if (previous.ContainsKey(next) || !occupancy.CanEnter(unit, next, session.Map)) continue;
                previous[next] = current; queue.Enqueue(next);
                int distance = session.MapDistance(next, session.Objective.CellIndex);
                if (distance < bestDistance || distance == bestDistance && next < best)
                { best = next; bestDistance = distance; }
            }
        }
        if (best == unit.CellIndex) return null;
        var reverse = new List<int>();
        for (int at = best; at >= 0; at = previous[at]) reverse.Add(at);
        reverse.Reverse();
        if (reverse.Count > unit.CurrentMovePoints + 1) reverse.RemoveRange(unit.CurrentMovePoints + 1, reverse.Count - unit.CurrentMovePoints - 1);
        return reverse;
    }
}
