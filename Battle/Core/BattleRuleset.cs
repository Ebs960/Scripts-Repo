using UnityEngine;

[CreateAssetMenu(menuName = "Data/Battle Ruleset")]
public sealed class BattleRuleset : ScriptableObject
{
    [Header("Rounds")]
    public int maxRounds = 5;
    public BattleSide firstActiveSide = BattleSide.Attacker;

    [Header("Deployment")]
    public int maxInitialUnitsPerSide = 6;
    public int deploymentDepthCells = 3;

    [Header("Map Size")]
    public int smallMapMinCells = 19;
    public int smallMapMaxCells = 25;
    public int mediumMapMinCells = 30;
    public int mediumMapMaxCells = 40;
    public int largeMapMinCells = 45;
    public int largeMapMaxCells = 61;
    public int hugeMapMinCells = 60;
    public int hugeMapMaxCells = 80;

    [Header("Movement")]
    public int riverEnterCost = 1;
    public int uphillCost = 1;
    public int cliffDeltaThreshold = 2;
    public int forestMoveCostHeavy = 2;
    public int forestMoveCostDefault = 1;

    [Header("Combat")]
    public float minDamagePercent = 0.08f;
    public float maxDamagePercent = 0.40f;
    public float minAdvantage = -0.75f;
    public float maxAdvantage = 0.75f;
    public float highGroundAttackMultiplier = 1.10f;
    public float highGroundDefenseMultiplier = 1.10f;
    public float defendMultiplier = 1.20f;
    public float softCoverDefenseMultiplier = 1.12f;
    public float hardCoverDefenseMultiplier = 1.25f;
    public float exposedDefenseMultiplier = 0.85f;

    [Header("Reinforcements")]
    public int reinforcementRadius = 3;
    public int reinforcementStartRound = 2;

    [Header("Safety")]
    public int maxGenerationAttempts = 6;
    public int maxAutoResolveCommandsPerRound = 512;
    public int maxAutoResolveTotalCommands = 4096;

    public int GetTargetCellCount(int participantCount)
    {
        if (participantCount <= 4)
            return Random.Range(smallMapMinCells, smallMapMaxCells + 1);
        if (participantCount <= 8)
            return Random.Range(mediumMapMinCells, mediumMapMaxCells + 1);
        if (participantCount <= 14)
            return Random.Range(largeMapMinCells, largeMapMaxCells + 1);
        return Random.Range(hugeMapMinCells, hugeMapMaxCells + 1);
    }
}
