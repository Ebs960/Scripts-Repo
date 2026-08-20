public sealed class BattleAIController
{
    private readonly BattleAIEvaluator evaluator = new();
    private readonly BattleDetectionService detection;
    public BattleTacticalPlan CurrentPlan { get; private set; }
    public void RestorePlan(BattleTacticalPlan plan) => CurrentPlan = plan;

    public BattleAIController(BattleDetectionService detection = null)
    {
        this.detection = detection;
    }

    public bool ExecuteSide(BattleSession session, BattleCommandExecutor executor, BattleOccupancy occupancy, int maxCommands, out int commandsExecuted, System.Action<BattleCommand> onExecuted = null, System.Action<BattleCommand> onBeforeExecute = null)
    {
        commandsExecuted = 0;
        if (session == null || executor == null)
            return false;

        bool any = false;
        CurrentPlan = BattleTacticalPlan.Build(session, session.ActiveSide, detection);
        bool releasedDelayedUnits = false;
        while (commandsExecuted < maxCommands)
        {
            bool executedThisPass = false;
            for (int i = 0; i < CurrentPlan.ActivationOrder.Count && commandsExecuted < maxCommands; i++)
            {
                var unit = FindUnit(session, CurrentPlan.ActivationOrder[i]);
                if (unit == null || (!unit.CanAct(session.ActiveSide)
                    && !(unit.Side == session.ActiveSide && unit.IsEmbarked && !unit.IsDead && unit.CurrentActionPoints > 0)))
                    continue;

                // Candidates are ordered by tactical value. If the preferred
                // action becomes illegal (LOS, occupancy, ammo, detection), try
                // the next legal command instead of abandoning the activation.
                var candidates = evaluator.BuildCandidates(session, unit, occupancy, detection, CurrentPlan);
                for (int c = 0; c < candidates.Count; c++)
                {
                    var command = candidates[c].Command;
                    onBeforeExecute?.Invoke(command);
                    if (!executor.Execute(session, occupancy, command, out _))
                        continue;
                    onExecuted?.Invoke(command);
                    any = true;
                    executedThisPass = true;
                    commandsExecuted++;
                    break;
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

    private static BattleUnitState FindUnit(BattleSession session, int id)
    { for (int i=0;i<session.Units.Count;i++) if (session.Units[i]?.UnitId == id) return session.Units[i]; return null; }

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
