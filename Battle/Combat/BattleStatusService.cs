public sealed class BattleStatusService
{
    public void ProcessRoundEnd(BattleSession session)
    {
        if (session == null)
            return;

        for (int unitIndex = 0; unitIndex < session.Units.Count; unitIndex++)
        {
            var unit = session.Units[unitIndex];
            if (unit == null)
                continue;

            for (int statusIndex = unit.StatusEffects.Count - 1; statusIndex >= 0; statusIndex--)
            {
                var status = unit.StatusEffects[statusIndex];
                if (status == null)
                {
                    unit.StatusEffects.RemoveAt(statusIndex);
                    continue;
                }

                status.RemainingRounds--;
                if (status.RemainingRounds <= 0)
                    unit.StatusEffects.RemoveAt(statusIndex);
            }
        }
    }
}