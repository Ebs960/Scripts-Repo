using System;
using System.Collections.Generic;
using UnityEngine;

public enum SpaceOperationKind { ExploreRegion, DefendPlanet, BreakBlockade, EstablishBlockade, EscortColonyShip, InvadePlanet, RaidTradeRoute, RepairFleet, CaptureResource, BuildImprovement, DestroyEnemyStation }
public enum SpaceOperationPhase { Planning, Assembling, Moving, Deploying, Executing, Sustaining, Withdrawing, Completed, Aborted }
public enum SpaceStrategicGoalType { ExploreSystem, SecureResourceRegion, ExpandTradeNetwork, FortifyPlanet, DefendSpaceImprovement, BuildSpaceStation, BuildShipyard, ColonizePlanet, EscortCivilianFleet, EstablishBlockade, BreakBlockade, RaidTradeRoute, InvadePlanet, DestroyEnemyStation, RepairAndRegroup, RespondToEnemyFleet }

[Serializable]
public class SpaceOperation
{
    public int operationId;
    public SpaceOperationKind kind;
    public SpaceOperationPhase phase;
    public int targetPlanetId = -1;
    public int targetTileIndex = -1;
    public int targetFeatureId = -1;
    public int targetImprovementId = -1;
    public List<int> assignedFleetIds = new List<int>();
    public List<int> assignedShipIds = new List<int>();
    public List<int> assignedConstructionShipIds = new List<int>();
    public List<int> requiredSectorIds = new List<int>();
    public Dictionary<int, List<int>> assignedUnitIdsBySector = new Dictionary<int, List<int>>();
    public int priority;
    public int createdTurn;
    public int lastProgressTurn;
    public string currentReason;
    public string debugReason;
}

[Serializable] public class LastKnownSpaceContact { public int entityId; public int tileIndex; public int lastSeenTurn; public bool wasFleet; public bool wasImprovement; }
public class SpaceEconomicValueMap { public readonly Dictionary<int, float> valueByTile = new Dictionary<int, float>(); public float GetValue(int tileIndex) => valueByTile.TryGetValue(tileIndex, out var v) ? v : 0f; }
public class SpaceControlMap { public readonly Dictionary<int, int> controllerByTile = new Dictionary<int, int>(); }
public class SpaceSensorMap { public readonly Dictionary<int, float> sensorByTile = new Dictionary<int, float>(); }

public class SpaceAIWorldState
{
    public int civilizationId; public int turn; public List<int> knownPlanetIds = new List<int>(); public List<int> ownedPlanetIds = new List<int>();
    public List<int> knownFeatureIds = new List<int>(); public List<int> ownedImprovementIds = new List<int>(); public List<int> visibleEnemyImprovementIds = new List<int>();
    public List<int> friendlyShipIds = new List<int>(); public List<int> visibleEnemyShipIds = new List<int>(); public List<LastKnownSpaceContact> rememberedContacts = new List<LastKnownSpaceContact>();
    public SpaceThreatMap threatMap = new SpaceThreatMap(); public SpaceEconomicValueMap economicValueMap = new SpaceEconomicValueMap(); public SpaceControlMap controlMap = new SpaceControlMap(); public SpaceSensorMap sensorMap = new SpaceSensorMap();
}

[CreateAssetMenu(fileName = "Space AI Strategy Profile", menuName = "Data/AI/Space Strategy Profile")]
public class SpaceAIStrategyProfile : ScriptableObject
{
    public float explorationWeight = 1f; public float colonizationWeight = 1f; public float tradeWeight = 1f; public float resourceWeight = 1f; public float militaryWeight = 1f; public float defenseWeight = 1f; public float blockadeWeight = 1f; public float stationWeight = 1f; public float riskTolerance = .5f;
}

public class SpaceAIStrategicDirector { public SpaceStrategicGoalType ChooseGoal(SpaceAIWorldState state, SpaceAIStrategyProfile profile) => state.visibleEnemyShipIds.Count > 0 ? SpaceStrategicGoalType.RespondToEnemyFleet : SpaceStrategicGoalType.ExploreSystem; }
public class SpaceAIOperationPlanner { private int nextId = 1; public SpaceOperation EnsureOperation(List<SpaceOperation> ops, SpaceStrategicGoalType goal, int turn) { var existing = ops.Find(o => o.phase != SpaceOperationPhase.Completed && o.phase != SpaceOperationPhase.Aborted); if (existing != null) return existing; var op = new SpaceOperation { operationId = nextId++, kind = goal == SpaceStrategicGoalType.RespondToEnemyFleet ? SpaceOperationKind.DefendPlanet : SpaceOperationKind.ExploreRegion, phase = SpaceOperationPhase.Planning, createdTurn = turn, lastProgressTurn = turn, currentReason = goal.ToString() }; ops.Add(op); return op; } }
public class SpaceAITacticalController { public SpaceTacticalEvaluator evaluator = new SpaceTacticalEvaluator(); }
public class SpaceAIConstructionPlanner { public int ChooseBuildTile(SpaceAIWorldState state, SpaceImprovementData data, SpaceHexGrid grid) { if (grid == null || data == null) return -1; foreach (var t in grid.tiles) if (!t.blocksMovement && t.terrainType == SpaceTerrainType.EmptySpace) return t.tileIndex; return -1; } }
public class SpaceAIEconomicPlanner { public void RebuildEconomicMap(SpaceAIWorldState state, SpaceHexGrid grid) { if (state == null || grid == null) return; state.economicValueMap.valueByTile.Clear(); foreach (var t in grid.tiles) state.economicValueMap.valueByTile[t.tileIndex] = t.resource != null ? t.resource.quantity : 0f; } }
public class SpaceAICommandExecutor { public void Execute(SpaceOperation operation) { if (operation != null && operation.phase == SpaceOperationPhase.Planning) operation.phase = SpaceOperationPhase.Assembling; } }

public class SpaceThreatMap
{
    public readonly Dictionary<int, float> threatByTile = new Dictionary<int, float>();
    public int cachedForCivilizationId = -1;
    public int cachedTurn = -1;
    public float GetThreat(int tileIndex) => threatByTile.TryGetValue(tileIndex, out float value) ? value : 0f;
    public void Rebuild(SpaceHexGrid grid, Civilization viewer, IEnumerable<CombatUnit> visibleOrRememberedEnemies, int turn)
    {
        threatByTile.Clear(); cachedForCivilizationId = viewer != null ? viewer.gameObject.GetRuntimeId() : -1; cachedTurn = turn;
        if (grid == null || visibleOrRememberedEnemies == null) return;
        foreach (var enemy in visibleOrRememberedEnemies)
        {
            if (enemy == null || enemy.data == null || enemy.currentHealth <= 0) continue;
            int origin = enemy.currentSpaceTileIndex; int range = Mathf.Max(1, enemy.data.directSpaceAttackRange + (enemy.data.spaceAttackPattern == SpaceAttackPattern.Blast ? enemy.data.spaceBlastRadius : 0));
            foreach (var tile in grid.tiles) if (grid.GetDistance(origin, tile.tileIndex) <= range) threatByTile[tile.tileIndex] = GetThreat(tile.tileIndex) + enemy.CurrentSpaceAttack;
        }
    }
}

public class SpaceTacticalEvaluator
{
    public float ScoreAttack(CombatUnit attacker, CombatUnit target, SpaceCombatPreview preview)
    {
        if (attacker == null || target == null || preview == null) return float.MinValue;
        float score = 0f;
        foreach (var hit in preview.affectedUnits) score += hit.predictedDamage * (hit.isFriendlyFire ? -2f : 1f);
        var primary = preview.affectedUnits.Find(h => h.isPrimaryTarget);
        if (primary != null && target.currentHealth <= primary.predictedDamage) score += 25f;
        score -= preview.predictedCounterDamage * 0.75f;
        return score;
    }
}

public class SpaceAIPlanner : MonoBehaviour
{
    public List<SpaceOperation> activeOperations = new List<SpaceOperation>();
    public SpaceThreatMap threatMap = new SpaceThreatMap();
    public SpaceTacticalEvaluator tacticalEvaluator = new SpaceTacticalEvaluator();
    public string selectedStrategicGoal;
    public SpaceAIStrategyProfile strategyProfile;
    private readonly SpaceAIStrategicDirector director = new SpaceAIStrategicDirector();
    private readonly SpaceAIOperationPlanner operationPlanner = new SpaceAIOperationPlanner();
    private readonly SpaceAICommandExecutor executor = new SpaceAICommandExecutor();
    public void PlanSpaceTurn(Civilization civilization, SpaceHexGrid grid, IEnumerable<CombatUnit> visibleEnemies, int turn)
    {
        threatMap.Rebuild(grid, civilization, visibleEnemies, turn);
        var snapshot = new SpaceAIWorldState { civilizationId = civilization != null ? civilization.gameObject.GetRuntimeId() : -1, turn = turn, threatMap = threatMap };
        if (visibleEnemies != null) foreach (var e in visibleEnemies) if (e != null) snapshot.visibleEnemyShipIds.Add(e.gameObject.GetRuntimeId());
        var goal = director.ChooseGoal(snapshot, strategyProfile);
        selectedStrategicGoal = goal.ToString();
        executor.Execute(operationPlanner.EnsureOperation(activeOperations, goal, turn));
    }
}
