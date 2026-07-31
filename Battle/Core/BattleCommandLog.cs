public static class BattleCommandLog
{
    public static string Format(BattleSession session, BattleCommand command)
    {
        if (session == null || command == null) return string.Empty;
        return session.CurrentRound + ":" + session.ActiveSide + ":" + command.UnitId + ":" + command.CommandType;
    }
}
