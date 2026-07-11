using System;
using System.Collections.Generic;
using UnityEngine;

public enum SpaceOperationKind { ExploreRegion, DefendPlanet, BreakBlockade, EstablishBlockade, EscortColonyShip, InvadePlanet, RaidTradeRoute, RepairFleet, CaptureResource }

[Serializable]
public class SpaceOperation
{
    public int operationId;
    public SpaceOperationKind kind;
    public int targetPlanetId = -1;
    public List<int> assignedFleetIds = new List<int>();
    public List<int> requiredSectorIds = new List<int>();
    public Dictionary<int, List<int>> assignedUnitIdsBySector = new Dictionary<int, List<int>>();
    public string debugReason;
}

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
    public void PlanSpaceTurn(Civilization civilization, SpaceHexGrid grid, IEnumerable<CombatUnit> visibleEnemies, int turn)
    {
        threatMap.Rebuild(grid, civilization, visibleEnemies, turn);
        selectedStrategicGoal = activeOperations.Count > 0 ? activeOperations[0].kind.ToString() : "ExploreRegion";
    }
}
