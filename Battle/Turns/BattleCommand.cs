using System.Collections.Generic;

public abstract class BattleCommand
{
    public int UnitId;
    public BattleCommandType CommandType;
}

public sealed class BattleMoveCommand : BattleCommand
{
    public IReadOnlyList<int> Path;
}

public sealed class BattleAttackCommand : BattleCommand
{
    public int TargetUnitId;
    public int AttackFromCell;
    public bool IsRanged;
    public int WeaponIndex;
}

public sealed class BattleDefendCommand : BattleCommand
{
}

public sealed class BattleRetreatCommand : BattleCommand
{
    public int ExitCell;
    public IReadOnlyList<int> Route;
}

public sealed class BattleWaitCommand : BattleCommand
{
}

public sealed class BattleEmbarkCommand : BattleCommand
{
    public int TransportUnitId;
}

public sealed class BattleDisembarkCommand : BattleCommand
{
    public int DestinationCell;
}

public sealed class BattleLaunchAircraftCommand : BattleCommand
{
    public int AircraftUnitId;
    public int LaunchCell;
}

public sealed class BattleRecoverAircraftCommand : BattleCommand
{
    public int CarrierUnitId;
}

public sealed class BattleChangeDepthCommand : BattleCommand
{
    public BattleDepthBand Depth;
}

public sealed class BattleActiveDetectionCommand : BattleCommand { }
