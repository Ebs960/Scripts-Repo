using System.Collections.Generic;
using UnityEngine;

public sealed class BattleCommandExecutor
{
    private readonly BattleMovementService movementService;
    private readonly BattleCombatResolver combatResolver;
    private readonly BattleLineOfSight los;
    private readonly BattleDetectionService detection;
    private readonly BattleTargetingService targeting;

    public BattleCommandExecutor(
        BattleMovementService movementService,
        BattleCombatResolver combatResolver,
        BattleLineOfSight los,
        BattleDetectionService detection = null)
    {
        this.movementService = movementService;
        this.combatResolver = combatResolver;
        this.los = los;
        this.detection = detection ?? new BattleDetectionService();
        targeting = new BattleTargetingService(this.detection);
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

        bool isEmbarkedAction = command is BattleDisembarkCommand && unit.IsEmbarked && unit.Side == session.ActiveSide;
        bool allowPostAttackMove = command is BattleMoveCommand && CanUsePostAttackMove(unit, session.ActiveSide);
        if (!unit.CanAct(session.ActiveSide) && !allowPostAttackMove && !isEmbarkedAction)
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
            case BattleEmbarkCommand embark:
                return ExecuteEmbark(session, occupancy, unit, embark, out reason);
            case BattleDisembarkCommand disembark:
                return ExecuteDisembark(session, occupancy, unit, disembark, out reason);
            case BattleLaunchAircraftCommand launch:
                return ExecuteLaunchAircraft(session, occupancy, unit, launch, out reason);
            case BattleRecoverAircraftCommand recover:
                return ExecuteRecoverAircraft(session, occupancy, unit, recover, out reason);
            case BattleChangeDepthCommand changeDepth:
                return ExecuteChangeDepth(session, occupancy, unit, changeDepth, out reason);
            case BattleActiveDetectionCommand:
                if (unit.Snapshot?.TacticalProfile == null || unit.Snapshot.TacticalProfile.sensorRange <= 0)
                { reason = "unit has no active sensor"; return false; }
                detection.ActiveScan(session, unit);
                unit.HasActed = true; unit.CurrentActionPoints = 0;
                return true;
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


        var targetCheck = targeting.CanTarget(session, attacker, defender, attack.IsRanged, attack.WeaponIndex);
        if (!targetCheck.Allowed)
        {
            reason = !attack.IsRanged && targetCheck.Reason == "target out of range" ? "melee out of range" : targetCheck.Reason;
            return false;
        }

        var selectedWeapon = BattleTargetingService.GetWeapon(attacker, attack.WeaponIndex);
        if (!IsWeaponReady(attacker, attack.WeaponIndex, out reason))
            return false;
        if (attack.IsRanged && !(selectedWeapon?.usesIndirectFire ?? false))
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
            session.RandomSeed + session.CurrentRound + attacker.UnitId + defender.UnitId,
            selectedWeapon);

        var result = combatResolver.Resolve(context, session.Random);
        ApplyDamage(session, occupancy, defender, result.Damage);

        attacker.HasActed = true;
    attacker.HasAttackedThisTurn = true;
        attacker.CurrentActionPoints = 0;
        attacker.IsDefending = false;
        attacker.RevealedByAttack = true;
        ConsumeWeapon(attacker, attack.WeaponIndex, selectedWeapon);

        int counterDistance = session.MapDistance(defender.CellIndex, attacker.CellIndex);
        int counterWeaponIndex = BattleTargetingService.FindWeaponIndex(defender, attacker, counterDistance);
        var counterWeapon = BattleTargetingService.GetWeapon(defender, counterWeaponIndex);
        var counterTargetCheck = counterWeaponIndex >= 0
            ? targeting.CanTarget(session, defender, attacker, counterWeapon.usesRangedAttack, counterWeaponIndex)
            : new TargetingResult(false, "no compatible counterattack weapon");
        if (!defender.IsDead && CanCounterAttack(defender) && counterTargetCheck.Allowed)
        {
            var counterContext = new BattleCombatContext(
                defender,
                attacker,
                !counterWeapon.usesRangedAttack,
                counterWeapon.usesRangedAttack,
                true,
                defenderCell != null ? defenderCell.ElevationLevel : 1,
                attackerCell != null ? attackerCell.ElevationLevel : 1,
                false,
                false,
                attacker.IsDefending,
                HasExposed(attacker),
                0,
                session.RandomSeed + session.CurrentRound + defender.UnitId + attacker.UnitId + 31,
                counterWeapon);

            var counter = combatResolver.Resolve(counterContext, session.Random);
            ApplyDamage(session, occupancy, attacker, counter.Damage);
            defender.CounterAttackedThisActivation = true;
            ConsumeWeapon(defender, counterWeaponIndex, counterWeapon);
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

        if (cell.RetreatExitForSide != unit.Side)
        {
            reason = "not a valid retreat exit";
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
        unit.WithdrawalCampaignTile = cell.CampaignTileIndex;
        unit.HasActed = true;
        unit.CurrentActionPoints = 0;
        unit.CurrentMovePoints = 0;
        return true;
    }

    private static bool ExecuteEmbark(BattleSession session, BattleOccupancy occupancy, BattleUnitState passenger, BattleEmbarkCommand command, out string reason)
    {
        reason = string.Empty;
        var transport = FindUnit(session, command.TransportUnitId);
        if (!CanCarry(passenger, transport, out reason))
            return false;
        if (!AreSameOrAdjacent(session, passenger.CellIndex, transport.CellIndex))
        {
            reason = "transport is not adjacent";
            return false;
        }

        occupancy.Remove(passenger);
        passenger.IsEmbarked = true;
        passenger.CarrierOrTransportBattleUnitId = transport.UnitId;
        passenger.CellIndex = -1;
        transport.EmbarkedBattleUnitIds.Add(passenger.UnitId);
        passenger.HasActed = true;
        passenger.CurrentActionPoints = 0;
        passenger.CurrentMovePoints = 0;
        return true;
    }

    private static bool ExecuteDisembark(BattleSession session, BattleOccupancy occupancy, BattleUnitState passenger, BattleDisembarkCommand command, out string reason)
    {
        reason = string.Empty;
        var transport = FindUnit(session, passenger.CarrierOrTransportBattleUnitId);
        if (transport == null || !passenger.IsEmbarked || !transport.EmbarkedBattleUnitIds.Contains(passenger.UnitId))
        {
            reason = "unit is not embarked";
            return false;
        }

        var destination = session.Map.GetCell(command.DestinationCell);
        if (destination == null || !destination.Supports(passenger.Domain))
        {
            reason = "invalid disembark destination";
            return false;
        }
        if (!AreSameOrAdjacent(session, transport.CellIndex, command.DestinationCell))
        {
            reason = "disembark destination is not adjacent";
            return false;
        }
        if (passenger.Domain == BattleDomain.Land && !destination.HasBeach && !destination.HasPort && !session.Map.GetCell(transport.CellIndex).SupportsLand)
        {
            reason = "land disembark requires a beach or port";
            return false;
        }
        passenger.IsEmbarked = false;
        if (!occupancy.TryMove(passenger, command.DestinationCell, session.Map))
        {
            passenger.IsEmbarked = true;
            reason = "disembark destination is blocked";
            return false;
        }

        passenger.CarrierOrTransportBattleUnitId = -1;
        transport.EmbarkedBattleUnitIds.Remove(passenger.UnitId);
        passenger.HasActed = true;
        passenger.CurrentActionPoints = 0;
        passenger.CurrentMovePoints = 0;
        return true;
    }

    private static bool ExecuteLaunchAircraft(BattleSession session, BattleOccupancy occupancy, BattleUnitState carrier, BattleLaunchAircraftCommand command, out string reason)
    {
        reason = string.Empty;
        var aircraft = FindUnit(session, command.AircraftUnitId);
        if (aircraft == null || !aircraft.IsEmbarked || aircraft.CarrierOrTransportBattleUnitId != carrier.UnitId)
        {
            reason = "aircraft is not assigned to this carrier";
            return false;
        }
        if (aircraft.Domain != BattleDomain.Air && aircraft.Domain != BattleDomain.Space)
        {
            reason = "only aircraft or space craft can launch";
            return false;
        }
        var launchCell = session.Map.GetCell(command.LaunchCell);
        if (launchCell == null || !launchCell.Supports(aircraft.Domain) || !AreSameOrAdjacent(session, carrier.CellIndex, command.LaunchCell))
        {
            reason = "invalid launch cell";
            return false;
        }
        aircraft.IsEmbarked = false;
        if (!occupancy.TryMove(aircraft, command.LaunchCell, session.Map))
        {
            aircraft.IsEmbarked = true;
            reason = "launch cell is blocked";
            return false;
        }

        aircraft.CarrierOrTransportBattleUnitId = -1;
        carrier.EmbarkedBattleUnitIds.Remove(aircraft.UnitId);
        carrier.HasActed = true;
        carrier.CurrentActionPoints = 0;
        return true;
    }

    private static bool ExecuteRecoverAircraft(BattleSession session, BattleOccupancy occupancy, BattleUnitState aircraft, BattleRecoverAircraftCommand command, out string reason)
    {
        reason = string.Empty;
        var carrier = FindUnit(session, command.CarrierUnitId);
        if (aircraft.Domain != BattleDomain.Air && aircraft.Domain != BattleDomain.Space)
        {
            reason = "only aircraft or space craft can recover";
            return false;
        }
        if (!CanCarry(aircraft, carrier, out reason))
            return false;
        if (!AreSameOrAdjacent(session, aircraft.CellIndex, carrier.CellIndex))
        {
            reason = "carrier is not in recovery range";
            return false;
        }

        occupancy.Remove(aircraft);
        aircraft.IsEmbarked = true;
        aircraft.CarrierOrTransportBattleUnitId = carrier.UnitId;
        aircraft.CellIndex = -1;
        carrier.EmbarkedBattleUnitIds.Add(aircraft.UnitId);
        aircraft.HasActed = true;
        aircraft.CurrentActionPoints = 0;
        aircraft.CurrentMovePoints = 0;
        return true;
    }

    private static bool ExecuteChangeDepth(BattleSession session, BattleOccupancy occupancy, BattleUnitState unit, BattleChangeDepthCommand command, out string reason)
    {
        reason = string.Empty;
        if (session.Theater != BattleTheater.Underwater || unit.Domain != BattleDomain.Underwater)
        {
            reason = "unit cannot change underwater depth";
            return false;
        }
        var cell = session.Map.GetCell(unit.CellIndex);
        if (cell == null || !cell.SupportsUnderwater)
        {
            reason = "invalid underwater location";
            return false;
        }
        if (command.Depth == BattleDepthBand.Surface)
        {
            reason = "underwater units must surface through a separate transition";
            return false;
        }
        if (command.Depth == BattleDepthBand.Deep && cell.WaterDepthLevel < 2)
        {
            reason = "water is too shallow to dive deep";
            return false;
        }
        if (unit.DepthBand == command.Depth)
        {
            reason = "unit is already at that depth";
            return false;
        }

        int oldBand = unit.OccupancyBand;
        int newBand = command.Depth == BattleDepthBand.Deep ? 2 : 1;
        if (occupancy.IsOccupied(unit.CellIndex, unit.Domain, newBand))
        {
            reason = "requested depth is occupied";
            return false;
        }

        int cellIndex = unit.CellIndex;
        occupancy.Remove(unit);
        unit.DepthBand = command.Depth;
        unit.OccupancyBand = newBand;
        if (!occupancy.TryMove(unit, cellIndex, session.Map))
        {
            // This should only be reachable if map state changed between the
            // validation and move. Restore the previous depth atomically.
            unit.OccupancyBand = oldBand;
            unit.DepthBand = oldBand >= 2 ? BattleDepthBand.Deep : BattleDepthBand.Shallow;
            occupancy.TryMove(unit, cellIndex, session.Map);
            reason = "unable to change depth";
            return false;
        }
        unit.HasActed = true;
        unit.CurrentActionPoints = 0;
        unit.CurrentMovePoints = 0;
        return true;
    }

    private static bool CanCarry(BattleUnitState passenger, BattleUnitState transport, out string reason)
    {
        reason = string.Empty;
        if (passenger == null || transport == null || passenger.Side != transport.Side || transport.IsEmbarked)
        {
            reason = "invalid transport";
            return false;
        }
        var data = transport.Snapshot?.UnitData;
        var passengerData = passenger.Snapshot?.UnitData;
        if (data == null || passengerData == null || !data.isTransport || !data.CanCarryUnitCategory(passengerData.unitType))
        {
            reason = "transport cannot carry this unit";
            return false;
        }
        if (transport.EmbarkedBattleUnitIds.Count >= data.transportCapacity)
        {
            reason = "transport capacity is full";
            return false;
        }
        return true;
    }

    private static bool AreSameOrAdjacent(BattleSession session, int fromCell, int toCell)
    {
        if (fromCell == toCell)
            return true;
        var from = session.Map.GetCell(fromCell);
        if (from?.NeighborIndices == null)
            return false;
        for (int i = 0; i < from.NeighborIndices.Length; i++)
            if (from.NeighborIndices[i] == toCell)
                return true;
        return false;
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
        if (unit.CounterAttackedThisActivation || unit.Snapshot.Weapons.Count == 0)
            return false;
        for (int i = 0; i < unit.Snapshot.Weapons.Count; i++)
            if (IsWeaponReady(unit, i, out _)) return true;
        return false;
    }

    private static bool IsWeaponReady(BattleUnitState unit, int index, out string reason)
    {
        reason = string.Empty;
        if (unit == null || index < 0 || index >= unit.WeaponAmmo.Count || index >= unit.WeaponCooldowns.Count)
        { reason = "weapon state unavailable"; return false; }
        if (unit.WeaponAmmo[index] == 0) { reason = "weapon is out of ammunition"; return false; }
        if (unit.WeaponCooldowns[index] > 0) { reason = "weapon is cooling down"; return false; }
        return true;
    }

    private static void ConsumeWeapon(BattleUnitState unit, int index, TacticalWeaponProfile weapon)
    {
        if (unit == null || index < 0 || index >= unit.WeaponAmmo.Count) return;
        if (unit.WeaponAmmo[index] > 0) unit.WeaponAmmo[index]--;
        unit.WeaponCooldowns[index] = weapon != null ? Mathf.Max(0, weapon.cooldownRounds) : 0;
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

    private static void ApplyDamage(BattleSession session, BattleOccupancy occupancy, BattleUnitState target, int damage)
    {
        target.CurrentHealth -= damage;
        if (target.CurrentHealth <= 0)
        {
            target.CurrentHealth = 0;
            target.IsDead = true;
            // Preserve the host's battlefield position before occupancy.Remove
            // resets CellIndex. Cargo survival and carrier recovery are resolved
            // from the destruction location, not from the sentinel cell -1.
            int destroyedAtCell = target.CellIndex;
            occupancy.Remove(target);
            ResolveCargoOnHostDestroyed(session, occupancy, target, destroyedAtCell);
        }
    }

    private static void ResolveCargoOnHostDestroyed(BattleSession session, BattleOccupancy occupancy, BattleUnitState host, int destroyedAtCell)
    {
        if (host.EmbarkedBattleUnitIds.Count == 0)
            return;

        var cargoIds = new List<int>(host.EmbarkedBattleUnitIds);
        host.EmbarkedBattleUnitIds.Clear();
        for (int i = 0; i < cargoIds.Count; i++)
        {
            var cargo = FindUnit(session, cargoIds[i]);
            if (cargo == null || cargo.IsDead)
                continue;

            cargo.IsEmbarked = false;
            cargo.CarrierOrTransportBattleUnitId = -1;
            bool rescuedByCarrier = TryRecoverToFriendlyCarrier(session, cargo, host.Side, destroyedAtCell);
            bool canDeploy = !rescuedByCarrier
                && session.Map.GetCell(destroyedAtCell)?.Supports(cargo.Domain) == true
                && session.Random.NextUnitFloat() < 0.35f
                && occupancy.TryMove(cargo, destroyedAtCell, session.Map);

            if (rescuedByCarrier)
                continue;

            if (canDeploy)
            {
                cargo.CurrentHealth = System.Math.Max(1, cargo.CurrentHealth / 2);
                cargo.StatusEffects.Add(new BattleStatusEffect { Type = BattleStatusEffectType.Exposed, RemainingRounds = 1 });
                cargo.HasActed = true;
                cargo.CurrentActionPoints = 0;
                cargo.CurrentMovePoints = 0;
                continue;
            }

            cargo.CurrentHealth = 0;
            cargo.IsDead = true;
            occupancy.Remove(cargo);
        }
    }

    private static bool TryRecoverToFriendlyCarrier(BattleSession session, BattleUnitState cargo, BattleSide side, int hostCell)
    {
        if (cargo.Domain != BattleDomain.Air && cargo.Domain != BattleDomain.Space)
            return false;

        for (int i = 0; i < session.Units.Count; i++)
        {
            var carrier = session.Units[i];
            if (carrier == null || carrier.IsDead || carrier.Side != side || carrier.IsEmbarked)
                continue;
            if (!AreSameOrAdjacent(session, hostCell, carrier.CellIndex))
                continue;
            if (!CanCarry(cargo, carrier, out _))
                continue;

            cargo.IsEmbarked = true;
            cargo.CarrierOrTransportBattleUnitId = carrier.UnitId;
            cargo.CellIndex = -1;
            carrier.EmbarkedBattleUnitIds.Add(cargo.UnitId);
            return true;
        }
        return false;
    }
}
