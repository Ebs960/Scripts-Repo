public sealed class BattleTurnController
{
    private readonly BattleRuleset ruleset;

    public BattleTurnController(BattleRuleset ruleset)
    {
        this.ruleset = ruleset;
    }

    public void BeginBattle(BattleSession session)
    {
        session.SetPhase(BattlePhase.Deployment);
    }

    public void BeginRound(BattleSession session)
    {
        session.StartSide(ruleset.firstActiveSide);
        RefreshRoundActions(session);
    }

    public void EndCurrentSide(BattleSession session)
    {
        if (session.ActiveSide == BattleSide.Attacker)
        {
            session.StartSide(BattleSide.Defender);
            return;
        }

        session.MoveToRoundEnd();
    }

    public bool EndRoundAndAdvance(BattleSession session)
    {
        return session.TryAdvanceRound();
    }

    private static void RefreshRoundActions(BattleSession session)
    {
        for (int i = 0; i < session.Units.Count; i++)
        {
            var u = session.Units[i];
            if (!u.IsAliveAndActive)
                continue;

            u.HasMoved = false;
            u.HasActed = false;
            u.IsDefending = false;
            u.IsWaiting = false;
            u.CurrentMovePoints = u.Snapshot.TacticalMovePoints;
            u.CurrentActionPoints = u.Snapshot.TacticalActionPoints;
            u.CounterAttackedThisActivation = false;
        }
    }
}
