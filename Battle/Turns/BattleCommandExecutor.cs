using System.Collections.Generic;

public sealed class BattleCommandExecutor
{
    private readonly BattleMovementService movementService;
    private readonly BattleCombatResolver combatResolver;
    private readonly BattleLineOfSight los;
    private readonly BattleDetectionService detection = new();
    private readonly BattleTargetingService targeting;

    public BattleCommandExecutor(BattleMovementService movementService, BattleCombatResolver combatResolver, BattleLineOfSight los)
    {
        this.movementService = movementService;
        this.combatResolver = combatResolver;
        this.los = los;
        targeting = new BattleTargetingService(detection);
    }

    public bool Execute(BattleSession session, BattleOccupancy occupancy, BattleCommand command, out string reason)
    {
        reason = string.Empty;
        if (session == null || occupancy == null || command == null)
        {
            reason = "invalid command context";
            return false;
        }

        var unit = FindUnit(session, command.UnitId);
        if (unit == null)
        {
            reason = "unit not found";
            return false;
        }

        bool allowPostAttackMove = command is BattleMoveCommand && CanUsePostAttackMove(unit, session.ActiveSide);
        if (!unit.CanAct(session.ActiveSide) && !allowPostAttackMove)
        {
            reason = "unit cannot act";
            return false;
        }

        switch (command)
        {
            case BattleMoveCommand move:
                return ExecuteMove(session, occupancy, unit, move, out reason);
            case BattleAttackCommand attack:
                return ExecuteAttack(session, occupancy, unit, attack, out reason);
            case BattleDefendCommand:
                unit.IsDefending = true;
                unit.HasActed = true;
                unit.CurrentActionPoints = 0;
                return true;
            case BattleWaitCommand:
                if (!unit.HasWaitedThisTurn)
                {
                    unit.IsWaiting = true;
                    unit.HasWaitedThisTurn = true;
                }
                else
                {
                    unit.IsDefending = true;
                    unit.HasActed = true;
                    unit.CurrentActionPoints = 0;
                }
                return true;
            case BattleRetreatCommand retreat:
                return ExecuteRetreat(session, occupancy, unit, retreat, out reason);
            default:
                reason = "unsupported command";
                return false;
        }
    }

    private bool ExecuteMove(BattleSession session, BattleOccupancy occupancy, BattleUnitState unit, BattleMoveCommand move, out string reason)
    {
        reason = string.Empty;
        if (move.Path == null || move.Path.Count == 0)
        {
            reason = "empty path";
            return false;
        }

        if (unit.HasAttackedThisTurn)
        {
            if (!(unit.Snapshot?.TacticalProfile?.canMoveAfterAttacking ?? false))
            {
                reason = "cannot move after attacking";
                return false;
            }

            if (unit.HasMoved)
            {
                reason = "already moved";
                return false;
            }
        }

        if (unit.CurrentMovePoints <= 0)
        {
            reason = "no move points";
            return false;
        }

        int destination = move.Path[move.Path.Count - 1];
        if (!movementService.TryMove(session, unit, destination, occupancy, out _))
        {
            reason = "invalid move";
            return false;
        }

        return true;
    }

    private bool ExecuteAttack(BattleSession session, BattleOccupancy occupancy, BattleUnitState attacker, BattleAttackCommand attack, out string reason)
    {
        reason = string.Empty;
        var defender = FindUnit(session, attack.TargetUnitId);
        if (defender == null || !defender.IsAliveAndActive || defender.Side == attacker.Side)
        {
            reason = "invalid target";
            return false;
        }

        if (attack.AttackFromCell != attacker.CellIndex)
        {
            reason = "attacker position mismatch";
            return false;
        }

        if (attacker.HasAttackedThisTurn)
        {
            reason = "already attacked";
            return false;
        }

        if (attacker.HasMoved && !(attacker.Snapshot?.TacticalProfile?.canAttackAfterMoving ?? true))
        {
            reason = "cannot attack after moving";
            return false;
        }


        var targetCheck = targeting.CanTarget(session, attacker, defender, attack.IsRanged);
        if (!targetCheck.Allowed)
        {
            reason = targetCheck.Reason;
            return false;
        }

        int distance = session.MapDistance(attacker.CellIndex, defender.CellIndex);
        if (!attack.IsRanged && distance > 1)
        {
            reason = "melee out of range";
            return false;
        }

        if (attack.IsRanged)
        {
            if (!los.HasLineOfSight(session, attacker, defender, out var losReason))
            {
                reason = losReason.ToString();
                return false;
            }
        }

        var attackerCell = session.Map.GetCell(attacker.CellIndex);
        var defenderCell = session.Map.GetCell(defender.CellIndex);
        BattleCoverResolver.GetCover(defenderCell, out bool soft, out bool hard);

        var context = new BattleCombatContext(
            attacker,
            defender,
            !attack.IsRanged,
            attack.IsRanged,
            false,
            attackerCell != null ? attackerCell.ElevationLevel : 1,
            defenderCell != null ? defenderCell.ElevationLevel : 1,
            soft,
            hard,
            defender.IsDefending,
            HasExposed(defender),
            0,
            session.RandomSeed + session.CurrentRound + attacker.UnitId + defender.UnitId);

        var result = combatResolver.Resolve(context);
        ApplyDamage(occupancy, defender, result.Damage);

        attacker.HasActed = true;
    attacker.HasAttackedThisTurn = true;
        attacker.CurrentActionPoints = 0;
        attacker.IsDefending = false;
        attacker.RevealedByAttack = true;

        if (!defender.IsDead && !attack.IsRanged && CanCounterAttack(defender))
        {
            var counterContext = new BattleCombatContext(
                defender,
                attacker,
                true,
                false,
                true,
                defenderCell != null ? defenderCell.ElevationLevel : 1,
                attackerCell != null ? attackerCell.ElevationLevel : 1,
                false,
                false,
                attacker.IsDefending,
                HasExposed(attacker),
                0,
                session.RandomSeed + session.CurrentRound + defender.UnitId + attacker.UnitId + 31);

            var counter = combatResolver.Resolve(counterContext);
            ApplyDamage(occupancy, attacker, counter.Damage);
            defender.CounterAttackedThisActivation = true;
        }

        return true;
    }

    private bool ExecuteRetreat(BattleSession session, BattleOccupancy occupancy, BattleUnitState unit, BattleRetreatCommand retreat, out string reason)
    {
        reason = string.Empty;
        var cell = session.Map.GetCell(retreat.ExitCell);
        if (cell == null || !cell.Supports(unit.Domain))
        {
            reason = "invalid retreat exit";
            return false;
        }

        var fromCell = session.Map.GetCell(unit.CellIndex);
        bool adjacent = false;
        if (fromCell?.NeighborIndices != null)
        {
            for (int i = 0; i < fromCell.NeighborIndices.Length; i++)
            {
                if (fromCell.NeighborIndices[i] == retreat.ExitCell)
                {
                    adjacent = true;
                    break;
                }
            }
        }

        if (!adjacent)
        {
            reason = "retreat exit not adjacent";
            return false;
        }

        if (!occupancy.CanEnter(unit, retreat.ExitCell, session.Map))
        {
            reason = "retreat exit blocked";
            return false;
        }

        occupancy.Remove(unit);
        unit.HasRetreated = true;
        unit.HasActed = true;
        unit.CurrentActionPoints = 0;
        unit.CurrentMovePoints = 0;
        return true;
    }

    private static BattleUnitState FindUnit(BattleSession session, int unitId)
    {
        for (int i = 0; i < session.Units.Count; i++)
        {
            if (session.Units[i].UnitId == unitId)
                return session.Units[i];
        }

        return null;
    }

    private static bool HasExposed(BattleUnitState unit)
    {
        for (int i = 0; i < unit.StatusEffects.Count; i++)
        {
            if (unit.StatusEffects[i].Type == BattleStatusEffectType.Exposed)
                return true;
        }

        return false;
    }

    private static bool CanCounterAttack(BattleUnitState unit)
    {
        return !unit.CounterAttackedThisActivation && unit.Snapshot.MeleeAttack > 0;
    }

    private static bool CanUsePostAttackMove(BattleUnitState unit, BattleSide activeSide)
    {
        if (unit == null)
            return false;

        return unit.Side == activeSide
            && unit.IsAliveAndActive
            && unit.HasAttackedThisTurn
            && !unit.HasMoved
            && unit.CurrentMovePoints > 0
            && (unit.Snapshot?.TacticalProfile?.canMoveAfterAttacking ?? false);
    }

    private static void ApplyDamage(BattleOccupancy occupancy, BattleUnitState target, int damage)
    {
        target.CurrentHealth -= damage;
        if (target.CurrentHealth <= 0)
        {
            target.CurrentHealth = 0;
            target.IsDead = true;
            occupancy.Remove(target);
        }
    }
}
