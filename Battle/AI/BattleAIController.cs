public sealed class BattleAIController
{
    private readonly BattleAIEvaluator evaluator = new();
    private readonly BattleDetectionService detection;

    public BattleAIController(BattleDetectionService detection = null)
    {
        this.detection = detection;
    }

    public bool ExecuteSide(BattleSession session, BattleCommandExecutor executor, BattleOccupancy occupancy, int maxCommands, out int commandsExecuted, System.Action<BattleCommand> onExecuted = null)
    {
        commandsExecuted = 0;
        if (session == null || executor == null)
            return false;

        bool any = false;
        bool releasedDelayedUnits = false;
        while (commandsExecuted < maxCommands)
        {
            bool executedThisPass = false;
            for (int i = 0; i < session.Units.Count && commandsExecuted < maxCommands; i++)
            {
                var unit = session.Units[i];
                if (unit == null || !unit.CanAct(session.ActiveSide))
                    continue;

                var command = evaluator.PickBestCommand(session, unit, occupancy, detection);
                if (command == null)
                    continue;

                if (executor.Execute(session, occupancy, command, out _))
                {
                    onExecuted?.Invoke(command);
                    any = true;
                    executedThisPass = true;
                    commandsExecuted++;
                }
            }

            if (executedThisPass)
                continue;

            if (!releasedDelayedUnits && ReleaseDelayedUnits(session))
            {
                releasedDelayedUnits = true;
                continue;
            }

            break;
        }

        return any;
    }

    private static bool ReleaseDelayedUnits(BattleSession session)
    {
        bool released = false;
        for (int i = 0; i < session.Units.Count; i++)
        {
            var unit = session.Units[i];
            if (unit != null && unit.Side == session.ActiveSide && unit.IsWaiting && unit.IsAliveAndActive)
            {
                unit.IsWaiting = false;
                released = true;
            }
        }
        return released;
    }
}
