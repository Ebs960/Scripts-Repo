using System;
using System.Collections.Generic;

[Serializable]
public sealed class BattleReplayLog
{
    public int BattleId;
    public int Seed;
    public BattleTheater Theater;
    public readonly List<int> InitialParticipantRuntimeIds = new();
    public readonly List<BattleCommandRecord> Commands = new();
}

[Serializable]
public sealed class BattleCommandRecord
{
    public int Round;
    public BattleSide Side;
    public int UnitId;
    public BattleCommandType Type;
    public int TargetUnitId = -1;
    public int DestinationCell = -1;
    public int WeaponIndex = -1;
    public BattleDepthBand Depth;

    public static BattleCommandRecord From(BattleSession session, BattleCommand command)
    {
        var record = new BattleCommandRecord
        {
            Round = session.CurrentRound,
            Side = session.ActiveSide,
            UnitId = command.UnitId,
            Type = command.CommandType,
        };
        switch (command)
        {
            case BattleAttackCommand attack:
                record.TargetUnitId = attack.TargetUnitId;
                record.WeaponIndex = attack.WeaponIndex;
                break;
            case BattleMoveCommand move when move.Path != null && move.Path.Count > 0:
                record.DestinationCell = move.Path[move.Path.Count - 1];
                break;
            case BattleRetreatCommand retreat:
                record.DestinationCell = retreat.ExitCell;
                break;
            case BattleDisembarkCommand disembark:
                record.DestinationCell = disembark.DestinationCell;
                break;
            case BattleEmbarkCommand embark:
                record.TargetUnitId = embark.TransportUnitId;
                break;
            case BattleLaunchAircraftCommand launch:
                record.TargetUnitId = launch.AircraftUnitId;
                record.DestinationCell = launch.LaunchCell;
                break;
            case BattleRecoverAircraftCommand recover:
                record.TargetUnitId = recover.CarrierUnitId;
                break;
            case BattleChangeDepthCommand depth:
                record.Depth = depth.Depth;
                break;
        }
        return record;
    }
}
