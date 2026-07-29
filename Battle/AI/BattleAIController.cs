public sealed class BattleAIController
{
    private readonly BattleAIEvaluator evaluator = new();

    public bool ExecuteSide(BattleSession session, BattleCommandExecutor executor, BattleOccupancy occupancy, int maxCommands, out int commandsExecuted)
    {
        commandsExecuted = 0;
        if (session == null || executor == null)
            return false;

        bool any = false;

        for (int i = 0; i < session.Units.Count; i++)
        {
            if (commandsExecuted >= maxCommands)
                break;

            var unit = session.Units[i];
            if (unit == null || !unit.CanAct(session.ActiveSide))
                continue;

            var command = evaluator.PickBestCommand(session, unit, occupancy);
            if (command == null)
                continue;

            if (executor.Execute(session, occupancy, command, out _))
            {
                any = true;
                commandsExecuted++;
            }
        }

        return any;
    }
}
