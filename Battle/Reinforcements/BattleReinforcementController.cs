public sealed class BattleReinforcementController
{
    public void DeployRoundReinforcements(BattleSession session, int round)
    {
        if (session == null)
            return;

        for (int i = 0; i < session.Reinforcements.Count; i++)
        {
            var g = session.Reinforcements[i];
            if (g == null || g.AvailableFromRound > round)
                continue;

            // MVP: reinforcement deployment is represented through reserve states and command phase hooks.
            // Placement can be expanded in presentation layer without changing battle authority.
        }
    }
}
