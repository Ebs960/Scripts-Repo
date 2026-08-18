using System.Collections.Generic;

public enum BattlePresentationEventType { Move, Attack, CounterAttack, Damage, Death, Defend, Retreat, Embark, Disembark, Launch, Recover, Reinforcement, DepthChange, DetectionChange }

/// <summary>Transient IDs/value data describing an already resolved authoritative action.</summary>
public sealed class BattlePresentationEvent
{
    public BattlePresentationEventType Type;
    public int UnitId;
    public int TargetUnitId = -1;
    public int SourceCell = -1;
    public int TargetCell = -1;
    public int WeaponIndex = -1;
    public bool IsRanged;
    public bool IsSpecial;
    public int Damage;
    public int HealthBefore;
    public int HealthAfter;
    public bool Died;
    public int CounterDamage;
    public readonly List<int> Path = new();
}
