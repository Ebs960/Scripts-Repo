using System.Collections.Generic;

public sealed class BattleSession
{
    public int BattleId { get; }
    public int PlanetIndex { get; }
    public BattleTheater Theater { get; }
    public int SpaceRegionId { get; }
    public int StrategicAnchorTile { get; }

    public BattlePhase Phase { get; private set; }
    public BattleSide ActiveSide { get; private set; }

    public int CurrentRound { get; private set; }
    public int MaximumRounds { get; }

    public BattleMap Map { get; }
    public IReadOnlyList<BattleUnitState> Units => units;

    public BattleObjective Objective;
    public IReadOnlyList<BattleReinforcementGroup> Reinforcements => reinforcements;

    public int RandomSeed { get; }

    private readonly List<BattleUnitState> units;
    private readonly List<BattleReinforcementGroup> reinforcements;

    // Preserve the original planetary-battle API for callers that do not need to
    // select a theater explicitly, including existing battle tests.
    public BattleSession(
        int battleId,
        int planetIndex,
        int strategicAnchorTile,
        int maxRounds,
        int randomSeed,
        BattleMap map,
        List<BattleUnitState> unitStates,
        BattleObjective objective,
        List<BattleReinforcementGroup> reinforcementGroups)
        : this(
            battleId,
            BattleTheater.PlanetaryJoint,
            planetIndex,
            -1,
            strategicAnchorTile,
            maxRounds,
            randomSeed,
            map,
            unitStates,
            objective,
            reinforcementGroups)
    {
    }

    public BattleSession(
        int battleId,
        BattleTheater theater,
        int planetIndex,
        int spaceRegionId,
        int strategicAnchorTile,
        int maxRounds,
        int randomSeed,
        BattleMap map,
        List<BattleUnitState> unitStates,
        BattleObjective objective,
        List<BattleReinforcementGroup> reinforcementGroups)
    {
        BattleId = battleId;
        Theater = theater;
        PlanetIndex = planetIndex;
        SpaceRegionId = spaceRegionId;
        StrategicAnchorTile = strategicAnchorTile;
        MaximumRounds = maxRounds;
        RandomSeed = randomSeed;
        Map = map;
        units = unitStates;
        Objective = objective;
        reinforcements = reinforcementGroups;

        Phase = BattlePhase.Deployment;
        ActiveSide = BattleSide.Attacker;
        CurrentRound = 1;
    }

    public void SetPhase(BattlePhase phase)
    {
        Phase = phase;
    }

    public void StartSide(BattleSide side)
    {
        ActiveSide = side;
        Phase = side == BattleSide.Attacker ? BattlePhase.AttackerTurn : BattlePhase.DefenderTurn;
    }

    public void MoveToRoundEnd()
    {
        Phase = BattlePhase.RoundEnd;
    }

    public bool TryAdvanceRound()
    {
        if (CurrentRound >= MaximumRounds)
            return false;

        CurrentRound++;
        Phase = BattlePhase.AttackerTurn;
        ActiveSide = BattleSide.Attacker;
        return true;
    }

    public int MapDistance(int fromCell, int toCell)
    {
        if (fromCell == toCell)
            return 0;

        var visited = new HashSet<int>();
        var queue = new Queue<(int cell, int dist)>();
        visited.Add(fromCell);
        queue.Enqueue((fromCell, 0));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var cell = Map.GetCell(current.cell);
            if (cell?.NeighborIndices == null)
                continue;

            for (int i = 0; i < cell.NeighborIndices.Length; i++)
            {
                int n = cell.NeighborIndices[i];
                if (!visited.Add(n))
                    continue;

                if (n == toCell)
                    return current.dist + 1;

                queue.Enqueue((n, current.dist + 1));
            }
        }

        return int.MaxValue;
    }
}
