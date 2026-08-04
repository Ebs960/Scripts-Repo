using System.Collections.Generic;

public sealed class BattleUnitState
{
    public int UnitId;
    public BattleUnitSnapshot Snapshot;

    public BattleSide Side;
    public int CellIndex;

    public int CurrentHealth;
    public int CurrentMovePoints;
    public int CurrentActionPoints;

    public bool HasMoved;
    public bool HasActed;
    public bool IsDefending;
    public bool IsWaiting;
    public bool HasWaitedThisTurn;
    public bool IsReserve;
    public int ReinforcementGroupId;
    public bool HasRetreated;
    public bool IsDead;
    public bool HasAttackedThisTurn;
    public bool HasEnteredBattle;
    public int WithdrawalCampaignTile = -1;
    public int WithdrawalTacticalExit = -1;
    public readonly List<int> RetreatPath = new();
    public string RetreatFailureReason;

    public BattleDomain Domain => Snapshot != null ? Snapshot.Domain : BattleDomain.Land;
    public int OccupancyBand;
    public bool IsEmbarked;
    public int CarrierOrTransportBattleUnitId = -1;
    public readonly List<int> EmbarkedBattleUnitIds = new();
    public readonly List<int> WeaponAmmo = new();
    public readonly List<int> WeaponCooldowns = new();
    public int FuelOrEndurance = -1;
    public BattleDepthBand DepthBand;
    public float CommanderAttackMultiplier = 1f;
    public float CommanderDefenseMultiplier = 1f;
    public bool RevealedByAttack;

    public bool CounterAttackedThisActivation;

    public readonly List<BattleStatusEffect> StatusEffects = new();

    public bool IsAliveAndActive => !IsDead && !HasRetreated && !IsReserve && !IsEmbarked && CurrentHealth > 0;

    public bool CanAct(BattleSide activeSide)
    {
        return Side == activeSide
            && IsAliveAndActive
            && !HasActed
            && !IsWaiting
            && CurrentActionPoints > 0;
    }
}
