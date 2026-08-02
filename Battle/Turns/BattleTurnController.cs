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
        if (ReleaseDelayedActivations(session))
            return;

        if (session.ActiveSide == BattleSide.Attacker)
        {
            session.StartSide(BattleSide.Defender);
            return;
        }

        session.MoveToRoundEnd();
    }

    public bool ReleaseDelayedActivations(BattleSession session)
    {
        if (session == null)
            return false;

        bool hasNormalActivation = false;
        bool hasDelayedActivation = false;
        for (int i = 0; i < session.Units.Count; i++)
        {
            var unit = session.Units[i];
            if (unit == null || unit.Side != session.ActiveSide || !unit.IsAliveAndActive || unit.HasActed || unit.CurrentActionPoints <= 0)
                continue;

            if (unit.IsWaiting)
                hasDelayedActivation = true;
            else
                hasNormalActivation = true;
        }

        if (hasNormalActivation || !hasDelayedActivation)
            return false;

        for (int i = 0; i < session.Units.Count; i++)
        {
            var unit = session.Units[i];
            if (unit != null && unit.Side == session.ActiveSide && unit.IsWaiting)
                unit.IsWaiting = false;
        }

        return true;
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
            u.HasWaitedThisTurn = false;
            u.HasAttackedThisTurn = false;
            u.CurrentMovePoints = u.Snapshot.TacticalMovePoints;
            u.CurrentActionPoints = u.Snapshot.TacticalActionPoints;
            u.CounterAttackedThisActivation = false;
            u.RevealedByAttack = false;
            if ((u.Domain == BattleDomain.Air || u.Domain == BattleDomain.Space) && !u.IsEmbarked && u.FuelOrEndurance > 0)
                u.FuelOrEndurance--;
            for (int weapon = 0; weapon < u.WeaponCooldowns.Count; weapon++)
                if (u.WeaponCooldowns[weapon] > 0) u.WeaponCooldowns[weapon]--;
        }
    }
}
