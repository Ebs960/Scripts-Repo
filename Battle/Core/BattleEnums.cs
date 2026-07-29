using System;

public enum BattlePhase
{
    None,
    Preview,
    Deployment,
    AttackerTurn,
    DefenderTurn,
    RoundEnd,
    Resolved,
}

public enum BattleSide
{
    Attacker,
    Defender,
}

public enum BattleResolutionType
{
    Elimination,
    ObjectiveCaptured,
    DefenderHeld,
    AttackerRetreated,
    DefenderRetreated,
    AutoResolved,
    Invalid,
}

public enum BattleCommandType
{
    Move,
    MeleeAttack,
    RangedAttack,
    Defend,
    Wait,
    Retreat,
    Deploy,
    Reinforce,
}

public enum EngagementMode
{
    TacticalLandBattle,
    LegacyDirectAttack,
    AirMission,
    OrbitalAttack,
    SpaceCombat,
    NavalCombat,
    WorkerCapture,
    Unsupported,
}

public enum BattleRole
{
    LineInfantry,
    HeavyInfantry,
    AntiCavalry,
    Skirmisher,
    Ranged,
    Cavalry,
    Artillery,
    Support,
    Siege,
    Special,
}

public enum BattleInteractionLockReason
{
    None,
    BattlePreview,
    BattleDeployment,
    BattleActive,
    BattleResult,
}

public enum BattleElevationLevel
{
    Low = 0,
    Level = 1,
    High = 2,
    Peak = 3,
}

public enum BattleLosBlockReason
{
    None,
    OutOfRange,
    BlockedByForest,
    BlockedByUnit,
    BlockedByElevation,
    BlockedByStructure,
    InvalidTarget,
}

[Serializable]
public struct BattleObjective
{
    public int CellIndex;
    public BattleSide Owner;
}

public enum GameInteractionMode
{
    Campaign,
    BattlePreview,
    BattleDeployment,
    BattleActive,
    BattleResult,
}
