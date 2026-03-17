using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Den.Tools;
using System.Linq;

public class UnitMovementController : MonoBehaviour
{
    public static UnitMovementController Instance { get; private set; }
    private HexGrid grid;
    private PlanetGenerator planet;
    
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 10f;
    [SerializeField] private float rotationSpeed = 10f;
    [SerializeField] private AnimationCurve movementCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField, Range(0f, 0.45f)] private float visualCornerCutFraction = 0.35f;
    [SerializeField, Min(2)] private int visualCurveSamplesPerTile = 8;
    [SerializeField, Min(0.05f)] private float rotationLookaheadDistance = 0.75f;

    private const int MaxPathSearchNodes = 10000;

    // A* mode: when true, uses hex distance heuristic for faster pathfinding.
    // Admissible heuristic = GetWrappedHexDistance * minMoveCost guarantees optimality.
    [Header("Pathfinding")]
    [SerializeField] private bool useAStarHeuristic = true;
    private const float MIN_MOVE_COST = 1f; // minimum possible single-step cost (Plains = 1)

    // Path cache: avoids recomputing identical paths within the same turn.
    // Key = (start, end, unitInstanceId), Value = (path, turn).
    private readonly Dictionary<(int, int, int), (List<int> path, int turn)> pathCache = new(32);
    private const int PATH_CACHE_MAX = 64;

    // Telemetry counters (reset each turn, readable by AIDebugOverlay)
    public int PathExpansions { get; private set; }
    public int PathAborts { get; private set; }
    public int PathCacheHits { get; private set; }
    public int PathQueries { get; private set; }

    public void ResetPathTelemetry()
    {
        PathExpansions = 0;
        PathAborts = 0;
        PathCacheHits = 0;
        PathQueries = 0;
    }

    private sealed class MinHeap
    {
        private readonly List<(int tile, float priority)> heap = new List<(int, float)>();

        public int Count => heap.Count;
        public void Clear() => heap.Clear();

        // Deterministic comparison: primary by cost, secondary by tile index to
        // guarantee the same path is returned for equal-cost alternatives.
        private static bool LessThan((int tile, float priority) a, (int tile, float priority) b)
        {
            if (a.priority != b.priority) return a.priority < b.priority;
            return a.tile < b.tile;
        }

        public void Enqueue(int tile, float priority)
        {
            heap.Add((tile, priority));
            int i = heap.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (LessThan(heap[i], heap[p])) { var tmp = heap[i]; heap[i] = heap[p]; heap[p] = tmp; i = p; }
                else break;
            }
        }

        public int Dequeue()
        {
            var min = heap[0];
            var last = heap[heap.Count - 1];
            heap.RemoveAt(heap.Count - 1);
            if (heap.Count > 0) { heap[0] = last; SiftDown(0); }
            return min.tile;
        }

        private void SiftDown(int i)
        {
            int n = heap.Count;
            while (true)
            {
                int l = 2 * i + 1, r = 2 * i + 2, s = i;
                if (l < n && LessThan(heap[l], heap[s])) s = l;
                if (r < n && LessThan(heap[r], heap[s])) s = r;
                if (s != i) { var tmp = heap[i]; heap[i] = heap[s]; heap[s] = tmp; i = s; }
                else break;
            }
        }
    }

    private readonly MinHeap openSet = new MinHeap();
    private readonly Dictionary<int, float> bestCost = new Dictionary<int, float>();
    private readonly Dictionary<int, int> cameFrom = new Dictionary<int, int>();
    // Track active move coroutines per-unit so they can be cancelled from BaseUnit (failsafe, interruptions)
    private readonly Dictionary<int, Coroutine> _activeMoveCoroutines = new Dictionary<int, Coroutine>();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        // Initial attempt to find references in scene
        FindReferencesInCurrentScene();
    }

    void Start()
    {
        // Resume queued movement when a civ ends its turn (so continuations happen after End Turn)
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.OnCivTurnEnded += HandleTurnChanged;
        }
    }

    void OnDestroy()
    {
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.OnCivTurnEnded -= HandleTurnChanged;
        }
    }

    private void HandleTurnChanged(Civilization civ, int round)
    {
        if (civ == null) return;

        if (Application.isEditor || Debug.isDebugBuild)
        {
            string civName = civ?.civData != null ? civ.civData.civName : "null";
            Debug.Log($"[UnitMoveCtrl] HandleTurnChanged for civ={civName} round={round}");
        }

        // Resume queued movement for units that belong to this civ
        try
        {
            var playerCiv = CivilizationManager.Instance != null ? CivilizationManager.Instance.playerCiv : null;

            foreach (var cu in UnitRegistry.GetCombatUnits())
            {
                if (cu == null || cu.owner != civ) continue;
                // Prefer consuming from the canonical movementOrderPath when present.
                if (cu.movementOrderPath != null && cu.movementOrderPathConsumed < cu.movementOrderPath.Count && !cu.isMoving)
                {
                    var remainingFlat = cu.movementOrderPath.Skip(cu.movementOrderPathConsumed).ToList();
                    var usableSeg = remainingFlat;
                    try { usableSeg = TrimPathToAvailableMovement(cu, remainingFlat); } catch { usableSeg = remainingFlat; }

                    if (usableSeg != null && usableSeg.Count > 0)
                    {
                        // Advance the canonical consumption index by the number of tiles we will move now
                        cu.movementOrderPathConsumed += usableSeg.Count;

                        // Keep queuedMovementSegments in sync by removing the consumed tiles from its front segments
                        int toConsume = usableSeg.Count;
                        while (toConsume > 0 && cu.queuedMovementSegments.Count > 0)
                        {
                            var front = cu.queuedMovementSegments.Peek();
                            if (front == null) { cu.queuedMovementSegments.Dequeue(); continue; }
                            if (front.Count <= toConsume)
                            {
                                cu.queuedMovementSegments.Dequeue();
                                toConsume -= front.Count;
                            }
                            else
                            {
                                // Remove the consumed tiles from the beginning of this queued segment
                                front.RemoveRange(0, toConsume);
                                toConsume = 0;
                            }
                        }

                        StartMoveForUnit(cu, usableSeg);
                    }
                }
                else if (cu.queuedMovementSegments != null && cu.queuedMovementSegments.Count > 0 && !cu.isMoving)
                {
                    // Fallback for units that don't have a canonical movementOrderPath: keep existing behavior
                    var nextSeg = cu.queuedMovementSegments.Peek();
                    var usableSeg = nextSeg;
                    try { usableSeg = TrimPathToAvailableMovement(cu, nextSeg); } catch { usableSeg = nextSeg; }

                    if (usableSeg != null && usableSeg.Count > 0)
                    {
                        var seg = cu.queuedMovementSegments.Dequeue();
                        if (usableSeg.Count < seg.Count)
                        {
                            var remainder = seg.Skip(usableSeg.Count).ToList();
                            if (remainder.Count > 0) cu.queuedMovementSegments.Enqueue(remainder);
                        }
                        StartMoveForUnit(cu, usableSeg);
                    }
                }
            }

            foreach (var wu in UnitRegistry.GetWorkerUnits())
            {
                if (wu == null || wu.owner != civ) continue;
                if (wu.movementOrderPath != null && wu.movementOrderPathConsumed < wu.movementOrderPath.Count && !wu.isMoving)
                {
                    var remainingFlat = wu.movementOrderPath.Skip(wu.movementOrderPathConsumed).ToList();
                    var usableSeg = remainingFlat;
                    try { usableSeg = TrimPathToAvailableMovement(wu, remainingFlat); } catch { usableSeg = remainingFlat; }

                    if (usableSeg != null && usableSeg.Count > 0)
                    {
                        wu.movementOrderPathConsumed += usableSeg.Count;

                        int toConsume = usableSeg.Count;
                        while (toConsume > 0 && wu.queuedMovementSegments.Count > 0)
                        {
                            var front = wu.queuedMovementSegments.Peek();
                            if (front == null) { wu.queuedMovementSegments.Dequeue(); continue; }
                            if (front.Count <= toConsume)
                            {
                                wu.queuedMovementSegments.Dequeue();
                                toConsume -= front.Count;
                            }
                            else
                            {
                                front.RemoveRange(0, toConsume);
                                toConsume = 0;
                            }
                        }

                        StartMoveForUnit(wu, usableSeg);
                    }
                }
                else if (wu.queuedMovementSegments != null && wu.queuedMovementSegments.Count > 0 && !wu.isMoving)
                {
                    var nextSeg = wu.queuedMovementSegments.Peek();
                    var usableSeg = nextSeg;
                    try { usableSeg = TrimPathToAvailableMovement(wu, nextSeg); } catch { usableSeg = nextSeg; }

                    if (usableSeg != null && usableSeg.Count > 0)
                    {
                        var seg = wu.queuedMovementSegments.Dequeue();
                        if (usableSeg.Count < seg.Count)
                        {
                            var remainder = seg.Skip(usableSeg.Count).ToList();
                            if (remainder.Count > 0) wu.queuedMovementSegments.Enqueue(remainder);
                        }
                        StartMoveForUnit(wu, usableSeg);
                    }
                }
            }
        }
        catch { }
    }
    
    /// <summary>
    /// Set references from GameManager after generators are created
    /// </summary>
    public void SetReferences(HexGrid icoGrid, PlanetGenerator planetGen)
    {
        grid = icoGrid;
        planet = planetGen;
}
    
    /// <summary>
    /// Find all necessary references in the current scene (fallback method)
    /// </summary>
    public void FindReferencesInCurrentScene()
    {
        // Find HexGrid directly in the current scene via PlanetGenerator
        // Use GameManager API for multi-planet support
        if (planet == null)
        {
            planet = GameManager.Instance?.GetCurrentPlanetGenerator();
        }
        
        if (grid == null && planet != null)
        {
            grid = planet.Grid;
        }
// If we still don't have grid but we have planet, try to get grid from planet
        if (grid == null && planet != null)
        {
            grid = planet.Grid;
}
    }

    /// <summary>
    /// Finds a path (list of tile indices) from start to end using A*, considering each tile's movement cost.
    /// Flat-map only; uses TileSystem for neighbors and costs.
    /// When a unit is in orbit, uses flat orbit movement cost and skips terrain restrictions.
    /// Returns null if unreachable.
    /// </summary>
    public List<int> FindPath(int startIndex, int endIndex, BaseUnit unit = null)
    {
        PathQueries++;

        int pIndex = unit != null ? unit.planetIndex : (GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0);
        var ts = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;
        if (ts == null || !ts.IsReady())
        {
            Debug.LogWarning("[UnitMovementController] Pathfinding error: TileSystem not ready.");
            return null;
        }

        var startTile = ts.GetTileData(startIndex);
        var endTile = ts.GetTileData(endIndex);

        if (startTile == null || endTile == null)
        {
            Debug.LogWarning($"[UnitMovementController] Pathfinding error: Tiles invalid. Start: {startIndex}, End: {endIndex}");
            return null;
        }

        if (startIndex == endIndex)
            return new List<int>();

        // ── Path cache lookup ──
        int unitId = unit != null ? unit.GetInstanceID() : 0;
        int currentTurn = GameManager.Instance != null ? GameManager.Instance.currentTurn : 0;
        var cacheKey = (startIndex, endIndex, unitId);
        if (pathCache.TryGetValue(cacheKey, out var cached) && cached.turn == currentTurn)
        {
            PathCacheHits++;
            return cached.path != null ? new List<int>(cached.path) : null;
        }

        bool isOrbit = unit != null && unit.currentLayer == TileLayer.Orbit;
        int orbitCost = BiomeHelper.DefaultOrbitMovementCost;
        if (isOrbit)
        {
            var cu = unit as CombatUnit;
            if (cu != null && cu.data != null)
                orbitCost = cu.data.orbitMovementCost;
        }

        // A* when heuristic enabled, Dijkstra (h=0) otherwise.
        // Heuristic: hex distance * min move cost is admissible (never overestimates).
        bool useHeuristic = useAStarHeuristic && !isOrbit;
        openSet.Clear();
        bestCost.Clear();
        cameFrom.Clear();

        bestCost[startIndex] = 0f;
        float h0 = useHeuristic ? ts.GetWrappedHexDistance(startIndex, endIndex) * MIN_MOVE_COST : 0f;
        openSet.Enqueue(startIndex, h0);

        int expanded = 0;

        while (openSet.Count > 0)
        {
            int current = openSet.Dequeue();

            float currentG = bestCost.TryGetValue(current, out float cg) ? cg : float.MaxValue;

            if (bestCost.TryGetValue(current, out float best) && currentG > best)
                continue;

            if (current == endIndex)
            {
                var path = new List<int>();
                int trace = endIndex;
                while (trace != startIndex)
                {
                    path.Add(trace);
                    if (!cameFrom.TryGetValue(trace, out int prev))
                    {
                        CacheResult(cacheKey, null, currentTurn);
                        return null;
                    }
                    trace = prev;
                }
                path.Reverse();
                PathExpansions += expanded;
                CacheResult(cacheKey, path, currentTurn);
                return path;
            }

            expanded++;
            if (expanded > MaxPathSearchNodes)
            {
                PathAborts++;
                PathExpansions += expanded;
                Debug.LogWarning($"[UnitMovementController] Aborting path search: expanded > {MaxPathSearchNodes} nodes (start={startIndex} end={endIndex})");
                CacheResult(cacheKey, null, currentTurn);
                return null;
            }

            foreach (int neighbor in ts.GetNeighbors(current))
            {
                var neighborTile = ts.GetTileData(neighbor);
                if (neighborTile == null) continue;

                int moveCost;
                if (isOrbit)
                {
                    moveCost = orbitCost;
                }
                else
                {
                    moveCost = BiomeHelper.GetMovementCost(neighborTile, unit);
                    if (moveCost >= 99) continue;
                }

                float tentativeG = currentG + moveCost;

                if (bestCost.TryGetValue(neighbor, out float existingG) && tentativeG >= existingG)
                    continue;

                bestCost[neighbor] = tentativeG;
                cameFrom[neighbor] = current;

                float h = useHeuristic ? ts.GetWrappedHexDistance(neighbor, endIndex) * MIN_MOVE_COST : 0f;
                openSet.Enqueue(neighbor, tentativeG + h);
            }
        }

        PathExpansions += expanded;
        CacheResult(cacheKey, null, currentTurn);
        return null;
    }

    private void CacheResult((int, int, int) key, List<int> path, int turn)
    {
        if (pathCache.Count >= PATH_CACHE_MAX) pathCache.Clear();
        pathCache[key] = (path, turn);
    }

    public List<int> TrimPathToAvailableMovement(BaseUnit unit, List<int> path)
    {
        if (unit == null || path == null || path.Count == 0) return path;
        if (unit.currentLayer == TileLayer.Orbit) return path;

        int pIndex = unit.planetIndex;
        var ts = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;
        if (ts == null) return path;

        int remainingMovePoints = unit.currentMovePoints;
        List<int> trimmedPath = new List<int>(path.Count);

        foreach (int tileIndex in path)
        {
            var tileData = ts.GetTileData(tileIndex);
            int movementCost = tileData != null ? BiomeHelper.GetMovementCost(tileData, unit) : 1;
            if (movementCost >= 99 || remainingMovePoints < movementCost)
                break;

            trimmedPath.Add(tileIndex);
            remainingMovePoints -= movementCost;
        }

        return trimmedPath;
    }

    /// <summary>
    /// Returns the path segmented into per-turn lists for the given unit.
    /// Each inner list represents tiles traversed during a single turn (first = current turn remainder).
    /// </summary>
    public List<List<int>> GetPathSegmentsByTurn(BaseUnit unit, int startIndex, int endIndex)
    {
        var path = FindPath(startIndex, endIndex, unit);
        if (path == null || path.Count == 0) return null;


        int remaining = unit.currentMovePoints;
        int fullPerTurn = unit.GetStartingMovePoints();

        var ts = TileSystem.GetForPlanet(unit.planetIndex) ?? TileSystem.Instance;
        var segments = new List<List<int>>();
        var currentSeg = new List<int>();

        foreach (int tileIndex in path)
        {
            var td = ts != null ? ts.GetTileData(tileIndex) : null;
            int cost = td != null ? BiomeHelper.GetMovementCost(td, unit) : 1;
            if (cost >= 99) break;

            // If we don't have enough movement available for this tile,
            // close the current segment (if any) and then accumulate next-turn
            // movement until we can pay the tile cost. If the unit's per-turn
            // movement is zero or negative, we cannot accumulate and must stop.
            if (remaining < cost)
            {
                if (currentSeg.Count > 0)
                {
                    segments.Add(new List<int>(currentSeg));
                    currentSeg.Clear();
                }

                if (fullPerTurn <= 0)
                {
                    // Unit cannot gain movement next turn; stop segmentation here.
                    break;
                }

                while (remaining < cost)
                {
                    remaining += fullPerTurn;
                }
            }

            currentSeg.Add(tileIndex);
            remaining -= cost;
        }

        if (currentSeg.Count > 0)
            segments.Add(currentSeg);

        return segments;
    }


    /// <summary>
    /// Unified movement method for any unit type.
    /// Moves unit along the given path with flat-map orientation.
    /// Now uses BaseUnit for shared functionality.
    /// </summary>
    public IEnumerator MoveAlongPath(BaseUnit unit, List<int> path)
    {
        if (unit == null || path == null || path.Count == 0)
            yield break;

        // Ensure we always remove the active coroutine record when this enumerator ends.
        try
        {
            // BaseUnit provides common properties for all unit types
            // Cast for type-specific behavior
            CombatUnit combatUnit = unit as CombatUnit;
            WorkerUnit workerUnit = unit as WorkerUnit;

            int currentTileIndex = unit.currentTileIndex;
            Transform unitTransform = unit.transform;

            // Track the previous tile for movement cost calculation
            int previousTileIndex = currentTileIndex;

            // Set unit to moving state
            var playerCiv = CivilizationManager.Instance != null ? CivilizationManager.Instance.playerCiv : null;
            // Precompute per-tile costs for a start/summary log
            int pIndexForLog = unit != null ? unit.planetIndex : (GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0);
            var tsForLog = TileSystem.GetForPlanet(pIndexForLog) ?? TileSystem.Instance;
            bool isOrbitForLog = unit != null && unit.currentLayer == TileLayer.Orbit;
            int orbitCostForLog = BiomeHelper.DefaultOrbitMovementCost;
            var cuForLog = unit as CombatUnit;
            if (isOrbitForLog && cuForLog != null && cuForLog.data != null) orbitCostForLog = cuForLog.data.orbitMovementCost;

            var perTileCosts = new System.Collections.Generic.List<int>(path.Count);
            foreach (int t in path)
            {
                if (isOrbitForLog) perTileCosts.Add(orbitCostForLog);
                else
                {
                    var td = tsForLog != null ? tsForLog.GetTileData(t) : null;
                    perTileCosts.Add(td != null ? BiomeHelper.GetMovementCost(td, unit) : 1);
                }
            }

            int mpBefore = unit != null ? unit.currentMovePoints : 0;
            int tilesMoved = 0;
            int mpSpent = 0;

            if (Application.isEditor || Debug.isDebugBuild)
            {
                if (unit != null && unit.owner == playerCiv)
                {
                    string costsStr = string.Join(",", perTileCosts.Select((c, idx) => $"{path[idx]}:{c}"));
                    Debug.Log($"[UnitMoveCtrl] MOVE ORDER START -> {unit.gameObject.name} from {currentTileIndex} to {path[path.Count - 1]} | pathLen={path.Count} | mpBefore={mpBefore} | perTileCosts={costsStr}");
                }
            }

            unit.UpdateWalkingState(true);

            int pIndex = unit != null ? unit.planetIndex : (GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0);
            var ts = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;
            var occ = TileOccupancyManager.GetForPlanet(pIndex) ?? TileOccupancyManager.Instance;

            int[] stepFromTiles = new int[path.Count];
            int[] stepMovementCosts = new int[path.Count];
            int logicalCurrentTile = currentTileIndex;

            void RefreshVision()
            {
                if (UnitVisionManager.Instance != null && unit.owner != null)
                {
                    UnitVisionManager.Instance.UpdateVisionForCiv(UnitVisionManager.GetCivIndex(unit.owner));
                }
            }

            void LogMoveSummary()
            {
                var playerCivLocal = CivilizationManager.Instance != null ? CivilizationManager.Instance.playerCiv : null;
                if ((Application.isEditor || Debug.isDebugBuild) && unit.owner == playerCivLocal)
                {
                    Debug.Log($"[UnitMoveCtrl] MOVE ORDER RESULT -> {unit.gameObject.name} moved={tilesMoved}/{path.Count} mpBefore={mpBefore} mpSpent={mpSpent} mpRemaining={ (unit!=null?unit.currentMovePoints:0) }");
                }
            }

            void StopMovementEarly(int completedSteps, int finalTileIndex)
            {
                LogMoveSummary();
                unit.UpdateWalkingState(false);
                if (completedSteps > 0)
                    GameEventManager.Instance.RaiseMovementCompletedEvent(unit, path[0], finalTileIndex, completedSteps);
                RefreshVision();
            }

            bool TryCommitStep(int stepIndex)
            {
                int targetTileIndex = path[stepIndex];
                var tileData = ts != null ? ts.GetTileData(targetTileIndex) : null;
                int movementCost;
                if (unit.currentLayer == TileLayer.Orbit)
                {
                    movementCost = (combatUnit != null && combatUnit.data != null) ? combatUnit.data.orbitMovementCost : BiomeHelper.DefaultOrbitMovementCost;
                }
                else
                {
                    movementCost = tileData != null ? BiomeHelper.GetMovementCost(tileData, unit) : 1;
                }

                if (unit.currentMovePoints < movementCost)
                {
                    var playerCivLocal = CivilizationManager.Instance != null ? CivilizationManager.Instance.playerCiv : null;
                    if (Application.isEditor || Debug.isDebugBuild)
                    {
                        if (unit.owner == playerCivLocal)
                            Debug.Log($"[UnitMoveCtrl] {unit.gameObject.name} OUT OF MOVE POINTS at step {stepIndex}/{path.Count} (has {unit.currentMovePoints}, need {movementCost})");
                    }
                    return false;
                }

                if (occ != null)
                {
                    var existingOccupant = occ.GetOccupantObject(targetTileIndex, unit.currentLayer);
                    if (existingOccupant != null && existingOccupant.GetInstanceID() != unit.gameObject.GetInstanceID())
                    {
                        var playerCivLocal = CivilizationManager.Instance != null ? CivilizationManager.Instance.playerCiv : null;
                        if (Application.isEditor || Debug.isDebugBuild)
                        {
                            if (unit.owner == playerCivLocal)
                                Debug.Log($"[UnitMoveCtrl] {unit.gameObject.name} occupancy blocked at step {stepIndex}/{path.Count} for tile {targetTileIndex} by {existingOccupant.name}.");
                        }
                        return false;
                    }
                }

                if (unit.currentLayer != TileLayer.Orbit && !unit.CanMoveTo(targetTileIndex))
                {
                    var playerCivLocal = CivilizationManager.Instance != null ? CivilizationManager.Instance.playerCiv : null;
                    if (Application.isEditor || Debug.isDebugBuild)
                    {
                        if (unit.owner == playerCivLocal)
                            Debug.Log($"[UnitMoveCtrl] {unit.gameObject.name} movement blocked at step {stepIndex}/{path.Count} for tile {targetTileIndex}.");
                    }
                    return false;
                }

                bool claimedTile = false;
                try
                {
                    claimedTile = occ == null || occ.TrySetOccupant(targetTileIndex, unit.gameObject, unit.currentLayer);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[UnitMovementController] SetOccupant failed: {ex.Message}");
                }

                if (!claimedTile)
                {
                    var playerCivLocal = CivilizationManager.Instance != null ? CivilizationManager.Instance.playerCiv : null;
                    if (Application.isEditor || Debug.isDebugBuild)
                    {
                        if (unit.owner == playerCivLocal)
                            Debug.LogWarning($"[UnitMoveCtrl] {unit.gameObject.name} failed to claim tile {targetTileIndex} at commit time. Movement aborted.");
                    }
                    return false;
                }

                stepFromTiles[stepIndex] = logicalCurrentTile;
                stepMovementCosts[stepIndex] = movementCost;

                unit.DeductMovePoints(movementCost);
                try { mpSpent += movementCost; tilesMoved += 1; } catch { }

                try
                {
                    if (logicalCurrentTile >= 0 && logicalCurrentTile != targetTileIndex)
                        occ?.ClearOccupant(logicalCurrentTile, unit.currentLayer);
                }
                catch (System.Exception ex) { Debug.LogWarning($"[UnitMovementController] ClearOccupant failed: {ex.Message}"); }

                unit.currentTileIndex = targetTileIndex;
                logicalCurrentTile = targetTileIndex;
                return true;
            }

            bool HandleArrivalForStep(int stepIndex)
            {
                int targetTileIndex = path[stepIndex];

                if (combatUnit != null)
                    ImprovementManager.Instance?.NotifyUnitEnteredTile(targetTileIndex, combatUnit);
                else if (workerUnit != null)
                    ImprovementManager.Instance?.NotifyUnitEnteredTile(targetTileIndex, workerUnit);

                if (unit.isStored)
                {
                    return true;
                }

                if (unit.currentHealth <= 0 || unit.IsTrapped)
                {
                    var playerCivLocal = CivilizationManager.Instance != null ? CivilizationManager.Instance.playerCiv : null;
                    if (Application.isEditor || Debug.isDebugBuild)
                    {
                        if (unit.owner == playerCivLocal)
                            Debug.Log($"[UnitMoveCtrl] {unit.gameObject.name} TRAPPED/DEAD at step {stepIndex} (hp={unit.currentHealth}, trapped={unit.IsTrapped})");
                    }
                    return true;
                }

                GameEventManager.Instance.RaiseUnitMovedEvent(unit, stepFromTiles[stepIndex], targetTileIndex, stepMovementCosts[stepIndex]);
                previousTileIndex = targetTileIndex;
                return false;
            }

            if (!TryCommitStep(0))
            {
                LogMoveSummary();
                unit.UpdateWalkingState(false);
                RefreshVision();
                yield break;
            }

            float[] stepEndDistances;
            List<Vector3> visualPoints;
            List<float> visualDistances;
            BuildSmoothedVisualPath(unit, ts, path, unitTransform.position, out visualPoints, out visualDistances, out stepEndDistances);
            float totalVisualDistance = visualDistances.Count > 0 ? visualDistances[visualDistances.Count - 1] : 0f;

            int currentVisualStep = 0;

            if (totalVisualDistance > 0.001f)
            {
                float totalDuration = Mathf.Max(totalVisualDistance / moveSpeed, 0.01f);
                float elapsed = 0f;

                while (elapsed < totalDuration && currentVisualStep < path.Count)
                {
                    elapsed += Time.deltaTime;
                    float normalizedTime = Mathf.Clamp01(elapsed / totalDuration);
                    float curvedProgress = movementCurve.Evaluate(normalizedTime);
                    float travelDistance = curvedProgress * totalVisualDistance;

                    while (currentVisualStep < path.Count && travelDistance >= stepEndDistances[currentVisualStep] - 0.0001f)
                    {
                        float boundaryDistance = stepEndDistances[currentVisualStep];
                        Vector3 boundaryPosition = SamplePolylinePosition(visualPoints, visualDistances, boundaryDistance);
                        unitTransform.position = boundaryPosition;
                        UpdateRotationAlongPolyline(unitTransform, visualPoints, visualDistances, boundaryDistance, totalVisualDistance);

                        bool shouldStop = HandleArrivalForStep(currentVisualStep);
                        if (shouldStop)
                        {
                            if (unit.currentLayer == TileLayer.Orbit)
                                unitTransform.position = boundaryPosition;
                            else
                                PositionUnitOnSurface(unitTransform, path[currentVisualStep]);

                            StopMovementEarly(currentVisualStep + 1, path[currentVisualStep]);
                            yield break;
                        }

                        currentVisualStep++;
                        if (currentVisualStep < path.Count && !TryCommitStep(currentVisualStep))
                        {
                            StopMovementEarly(currentVisualStep, path[currentVisualStep - 1]);
                            yield break;
                        }
                    }

                    if (currentVisualStep >= path.Count) break;

                    Vector3 sampledPosition = SamplePolylinePosition(visualPoints, visualDistances, travelDistance);
                    unitTransform.position = sampledPosition;
                    UpdateRotationAlongPolyline(unitTransform, visualPoints, visualDistances, travelDistance, totalVisualDistance);
                    yield return null;
                }
            }

            while (currentVisualStep < path.Count)
            {
                float boundaryDistance = stepEndDistances[currentVisualStep];
                Vector3 boundaryPosition = SamplePolylinePosition(visualPoints, visualDistances, boundaryDistance);
                unitTransform.position = boundaryPosition;
                UpdateRotationAlongPolyline(unitTransform, visualPoints, visualDistances, boundaryDistance, totalVisualDistance);

                bool shouldStop = HandleArrivalForStep(currentVisualStep);
                if (shouldStop)
                {
                    if (unit.currentLayer == TileLayer.Orbit)
                        unitTransform.position = boundaryPosition;
                    else
                        PositionUnitOnSurface(unitTransform, path[currentVisualStep]);

                    StopMovementEarly(currentVisualStep + 1, path[currentVisualStep]);
                    yield break;
                }

                currentVisualStep++;
                if (currentVisualStep < path.Count && !TryCommitStep(currentVisualStep))
                {
                    StopMovementEarly(currentVisualStep, path[currentVisualStep - 1]);
                    yield break;
                }
            }

            if (unit.currentLayer == TileLayer.Orbit)
            {
                unitTransform.position = GetMovementWorldPosition(unit, ts, path[path.Count - 1]);
            }
            else
            {
                PositionUnitOnSurface(unitTransform, path[path.Count - 1]);
            }

            // Set unit back to idle state
            // Summary for player-owned unit on full completion
            var playerCivLocalEnd = CivilizationManager.Instance != null ? CivilizationManager.Instance.playerCiv : null;
            if ((Application.isEditor || Debug.isDebugBuild) && unit.owner == playerCivLocalEnd)
            {
                Debug.Log($"[UnitMoveCtrl] MOVE ORDER RESULT -> {unit.gameObject.name} moved={tilesMoved}/{path.Count} mpBefore={mpBefore} mpSpent={mpSpent} mpRemaining={ (unit!=null?unit.currentMovePoints:0) }");
            }
            unit.UpdateWalkingState(false);

            // Fire movement completed event
            GameEventManager.Instance.RaiseMovementCompletedEvent((MonoBehaviour)unit, path[0], path[path.Count - 1], path.Count);

            // Fog of War: movement completed; refresh vision for unit owner.
            if (UnitVisionManager.Instance != null && unit.owner != null)
            {
                UnitVisionManager.Instance.UpdateVisionForCiv(UnitVisionManager.GetCivIndex(unit.owner));
            }
        }
        finally
        {
            try
            {
                _activeMoveCoroutines.Remove(unit != null ? unit.GetInstanceID() : -1);
                int uncommittedTiles = path.Count - tilesMoved;
                if (uncommittedTiles > 0 && unit != null && unit.movementOrderPathConsumed > 0)
                {
                    unit.movementOrderPathConsumed = Mathf.Max(0, unit.movementOrderPathConsumed - uncommittedTiles);
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Properly positions and orients a unit on the flat map surface
    /// </summary>
    /// <summary>
    /// Start movement coroutine for a unit and track it so it can be cancelled later.
    /// </summary>
    public void StartMoveForUnit(BaseUnit unit, List<int> path)
    {
        if (unit == null) return;
        // Cancel any previous movement for this unit first
        StopMoveForUnit(unit);
        if (Application.isEditor || Debug.isDebugBuild)
        {
            var playerCiv = CivilizationManager.Instance != null ? CivilizationManager.Instance.playerCiv : null;
            int queued = unit.queuedMovementSegments != null ? unit.queuedMovementSegments.Count : 0;
            if (unit.owner == playerCiv)
                Debug.Log($"[UnitMoveCtrl] StartMoveForUnit {unit.name} pathLen={ (path!=null?path.Count:0) } queuedRemaining={queued}");
        }
        var c = StartCoroutine(MoveAlongPath(unit, path));
        try { _activeMoveCoroutines[unit.GetInstanceID()] = c; } catch { }
    }

    /// <summary>
    /// Stop any active movement coroutine for the provided unit.
    /// Safe to call even if no coroutine is active.
    /// </summary>
    public void StopMoveForUnit(BaseUnit unit)
    {
        if (unit == null) return;
        int id = unit.GetInstanceID();
        if (_activeMoveCoroutines.TryGetValue(id, out var coroutine))
        {
            try { if (coroutine != null) StopCoroutine(coroutine); } catch { }
            _activeMoveCoroutines.Remove(id);
        }
    }

    private Vector3 GetMovementWorldPosition(BaseUnit unit, TileSystem ts, int tileIndex)
    {
        Vector3 worldPos = ts != null ? ts.GetTileSurfacePosition(tileIndex) : Vector3.zero;
        if (unit != null && unit.currentLayer == TileLayer.Orbit)
        {
            worldPos += Vector3.up * PlanetGenerator.GetOrbitHeight(unit.planetIndex);
        }
        return worldPos;
    }

    private void BuildSmoothedVisualPath(BaseUnit unit, TileSystem ts, List<int> path, Vector3 startPosition,
        out List<Vector3> visualPoints, out List<float> cumulativeDistances, out float[] stepEndDistances)
    {
        visualPoints = new List<Vector3>(Mathf.Max(2, path.Count * visualCurveSamplesPerTile + 1));
        cumulativeDistances = new List<float>(Mathf.Max(2, path.Count * visualCurveSamplesPerTile + 1));
        stepEndDistances = new float[path.Count];

        visualPoints.Add(startPosition);
        cumulativeDistances.Add(0f);

        Vector3 segmentStart = startPosition;
        float totalDistance = 0f;
        int samplesPerTile = Mathf.Max(2, visualCurveSamplesPerTile);

        for (int i = 0; i < path.Count; i++)
        {
            Vector3 targetCenter = GetMovementWorldPosition(unit, ts, path[i]);
            Vector3 segmentEnd = targetCenter;
            if (i < path.Count - 1)
            {
                Vector3 nextCenter = GetMovementWorldPosition(unit, ts, path[i + 1]);
                segmentEnd = Vector3.Lerp(targetCenter, nextCenter, visualCornerCutFraction);
            }

            for (int sampleIndex = 1; sampleIndex <= samplesPerTile; sampleIndex++)
            {
                float t = sampleIndex / (float)samplesPerTile;
                Vector3 sampledPoint = EvaluateQuadraticBezier(segmentStart, targetCenter, segmentEnd, t);
                float stepDistance = Vector3.Distance(visualPoints[visualPoints.Count - 1], sampledPoint);
                if (stepDistance <= 0.0001f) continue;

                totalDistance += stepDistance;
                visualPoints.Add(sampledPoint);
                cumulativeDistances.Add(totalDistance);
            }

            stepEndDistances[i] = totalDistance;
            segmentStart = segmentEnd;
        }
    }

    private static Vector3 EvaluateQuadraticBezier(Vector3 start, Vector3 control, Vector3 end, float t)
    {
        float omt = 1f - t;
        return (omt * omt * start) + (2f * omt * t * control) + (t * t * end);
    }

    private Vector3 SamplePolylinePosition(List<Vector3> points, List<float> cumulativeDistances, float targetDistance)
    {
        if (points == null || points.Count == 0) return Vector3.zero;
        if (points.Count == 1) return points[0];

        float clampedDistance = Mathf.Clamp(targetDistance, 0f, cumulativeDistances[cumulativeDistances.Count - 1]);
        for (int i = 1; i < cumulativeDistances.Count; i++)
        {
            if (clampedDistance <= cumulativeDistances[i])
            {
                float segmentStartDistance = cumulativeDistances[i - 1];
                float segmentLength = cumulativeDistances[i] - segmentStartDistance;
                if (segmentLength <= 0.0001f) return points[i];

                float lerpT = (clampedDistance - segmentStartDistance) / segmentLength;
                return Vector3.Lerp(points[i - 1], points[i], lerpT);
            }
        }

        return points[points.Count - 1];
    }

    private void UpdateRotationAlongPolyline(Transform unitTransform, List<Vector3> points, List<float> cumulativeDistances,
        float currentDistance, float totalDistance)
    {
        if (unitTransform == null || points == null || points.Count < 2) return;

        float clampedDistance = Mathf.Clamp(currentDistance, 0f, totalDistance);
        Vector3 currentPosition = SamplePolylinePosition(points, cumulativeDistances, clampedDistance);
        float lookaheadDistance = Mathf.Min(totalDistance, clampedDistance + rotationLookaheadDistance);
        Vector3 lookaheadPosition = SamplePolylinePosition(points, cumulativeDistances, lookaheadDistance);
        Vector3 movementDirection = lookaheadPosition - currentPosition;
        movementDirection.y = 0f;

        if (movementDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(movementDirection.normalized, Vector3.up);
            unitTransform.rotation = Quaternion.Slerp(unitTransform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }
    }

    private void PositionUnitOnSurface(Transform unitTransform, int tileIndex)
    {
        // Best-effort: use current planet TileSystem for surface positioning.
        int pIndex = (GameManager.Instance != null) ? GameManager.Instance.currentPlanetIndex : 0;
        var ts = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;
        var tileData = ts != null ? ts.GetTileData(tileIndex) : null;
        if (tileData == null) return;

        // Place unit at terrain surface with proper height and upright orientation
        Vector3 flatCenter = ts.GetTileSurfacePosition(tileIndex);
        unitTransform.position = flatCenter;

        Vector3 forward = unitTransform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        unitTransform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
    }
} 
