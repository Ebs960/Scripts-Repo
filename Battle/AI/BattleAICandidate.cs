public readonly struct BattleAICandidate
{
    public readonly BattleCommand Command;
    public readonly float Score;

    public BattleAICandidate(BattleCommand command, float score)
    {
        Command = command;
        Score = score;
    }
}
