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

public enum BattleTheater
{
    PlanetaryJoint,
    Underwater,
    DeepSpace,
}

public enum PlanetaryBattleEnvironment
{
    Inland,
    Coastal,
    OpenOcean,
    Archipelago,
    Island,
    Port,
    Amphibious,
    Mixed,
}

public enum BattleObjectiveType
{
    Elimination,
    LandControl,
    PortCapture,
    Beachhead,
    NavalControl,
    Escape,
    RegionControl,
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
    Embark,
    Disembark,
    LaunchAircraft,
    RecoverAircraft,
    ChangeDepth,
    ActiveDetection,
}

/// <summary>Independent occupancy/movement domains used by every tactical battle.</summary>
public enum BattleDomain
{
    Land,
    NavalSurface,
    Underwater,
    Air,
    Orbit,
    Space,
}

public enum BattleDetectionLevel { Undetected, Suspected, Detected, Identified }

public enum BattleDepthBand
{
    Surface,
    Shallow,
    Deep,
}

public enum BattleEntryMethod
{
    LandEdge, NavalEdge, UnderwaterEdge, AirArrival, CarrierLaunch,
    AirbaseLaunch, AmphibiousLanding, TransportDisembark, OrbitalArrival, SpaceArrival,
}

[Flags]
public enum BattleDomainMask
{
    None = 0,
    Land = 1 << 0,
    NavalSurface = 1 << 1,
    Underwater = 1 << 2,
    Air = 1 << 3,
    Orbit = 1 << 4,
    Space = 1 << 5,
    All = Land | NavalSurface | Underwater | Air | Orbit | Space,
}

public enum EngagementMode
{
    TacticalBattle,
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
    public BattleObjectiveType Type;
}

public enum GameInteractionMode
{
    Campaign,
    BattlePreview,
    BattleDeployment,
    BattleActive,
    BattleResult,
}
