using System.Collections.Generic;

public sealed class BattleState
{
    public BattleSession Session;
    public BattleTurnController TurnController;
    public BattleCommandExecutor CommandExecutor;
    public BattleAIController AiController;
    public BattleMovementService MovementService;
    public BattleCombatResolver CombatResolver;
    public BattleOccupancy Occupancy;

    public readonly List<string> ActionLog = new();
}
