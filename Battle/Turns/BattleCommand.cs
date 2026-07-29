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
}

public sealed class BattleDefendCommand : BattleCommand
{
}

public sealed class BattleRetreatCommand : BattleCommand
{
    public int ExitCell;
}

public sealed class BattleWaitCommand : BattleCommand
{
}
