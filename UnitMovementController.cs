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
        // If unit has no movement points (GetStartingMovePoints == 0) treat as full-path (no trimming)
        if (unit.GetStartingMovePoints() <= 0) return path;

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

        // Non-worker units don't consume turn-based move points; return whole path as single segment
        // For non-turn-based units (starting MP <= 0) return single segment
        if (unit == null || unit.GetStartingMovePoints() <= 0)
        {
            return new List<List<int>> { new List<int>(path) };
        }

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
            // movement until we can pay the tile cost. Accumulation models
            // leftover MP carrying forward so preview matches runtime behavior
            // where remaining MP at segment boundaries can be combined with
            // subsequent turn's MP.
            if (remaining < cost)
            {
                if (currentSeg.Count > 0)
                {
                    segments.Add(new List<int>(currentSeg));
                    currentSeg.Clear();
                }

                // Accumulate next-turn movement repeatedly until we can afford the tile.
                // This allows tiles that require multiple turns to be accounted for.
                // Guard against non-advancing fullPerTurn (<=0) to avoid infinite loop.
                if (fullPerTurn <= 0)
                {
                    // cannot make progress; abort segmentation
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

            // Move along each tile in path
            for (int i = 0; i < path.Count; i++)
            {
                int targetTileIndex = path[i];

                // Per-planet TileSystem/Occupancy (true multi-planet gameplay)
                int pIndex = unit != null ? unit.planetIndex : (GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0);
                var ts = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;
                var occ = TileOccupancyManager.GetForPlanet(pIndex) ?? TileOccupancyManager.Instance;

                int movementCost;
                // Get movement cost for this step (orbit uses flat cost, surface uses terrain cost)
                var tileData = ts != null ? ts.GetTileData(targetTileIndex) : null;
                if (unit.currentLayer == TileLayer.Orbit)
                {
                    var orbitCu = combatUnit;
                    movementCost = (orbitCu != null && orbitCu.data != null) ? orbitCu.data.orbitMovementCost : BiomeHelper.DefaultOrbitMovementCost;
                }
                else
                {
                    movementCost = tileData != null ? BiomeHelper.GetMovementCost(tileData, unit) : 1;
                }

                if (unit != null && unit.currentMovePoints < movementCost)
                {
                    var playerCivLocal = CivilizationManager.Instance != null ? CivilizationManager.Instance.playerCiv : null;
                    if (Application.isEditor || Debug.isDebugBuild)
                    {
                            if (unit.owner == playerCivLocal)
                                Debug.Log($"[UnitMoveCtrl] {unit.gameObject.name} OUT OF MOVE POINTS at step {i}/{path.Count} (has {unit.currentMovePoints}, need {movementCost})");
                    }
                    // Summary for player-owned unit
                    if ((Application.isEditor || Debug.isDebugBuild) && unit.owner == playerCivLocal)
                    {
                        Debug.Log($"[UnitMoveCtrl] MOVE ORDER RESULT -> {unit.gameObject.name} moved={tilesMoved}/{path.Count} mpBefore={mpBefore} mpSpent={mpSpent} mpRemaining={ (unit!=null?unit.currentMovePoints:0) }");
                    }
                    unit.UpdateWalkingState(false);
                    if (i > 0)
                        GameEventManager.Instance.RaiseMovementCompletedEvent(unit, path[0], path[i - 1], i);

                    // Fog of War: moving stopped early, refresh vision for unit owner.
                    if (UnitVisionManager.Instance != null && unit.owner != null)
                    {
                        UnitVisionManager.Instance.UpdateVisionForCiv(UnitVisionManager.GetCivIndex(unit.owner));
                    }
                    yield break;
                }

                if (unit.currentLayer != TileLayer.Orbit && !unit.CanMoveTo(targetTileIndex))
                {
                    var playerCivLocal = CivilizationManager.Instance != null ? CivilizationManager.Instance.playerCiv : null;
                    if (Application.isEditor || Debug.isDebugBuild)
                    {
                        if (unit.owner == playerCivLocal)
                            Debug.Log($"[UnitMoveCtrl] {unit.gameObject.name} movement blocked at step {i}/{path.Count} for tile {targetTileIndex}.");
                    }
                    // Summary for player-owned unit
                    if ((Application.isEditor || Debug.isDebugBuild) && unit.owner == playerCivLocal)
                    {
                        Debug.Log($"[UnitMoveCtrl] MOVE ORDER RESULT -> {unit.gameObject.name} moved={tilesMoved}/{path.Count} mpBefore={mpBefore} mpSpent={mpSpent} mpRemaining={ (unit!=null?unit.currentMovePoints:0) }");
                    }
                    unit.UpdateWalkingState(false);
                    if (i > 0)
                        GameEventManager.Instance.RaiseMovementCompletedEvent(unit, path[0], path[i - 1], i);
                    yield break;
                }

                // Deduct movement points for workers (they still use turn-based movement)
                if (workerUnit != null)
                {
                    unit.DeductMovePoints(movementCost);
                    // Track spent MP and tiles moved for end-of-segment summary
                    try { mpSpent += movementCost; tilesMoved += 1; } catch { }
                }

                // Calculate planar positions on the flat map (orbit units stay elevated above surface)
                Vector3 startPosition = unitTransform.position;
                Vector3 endPosition = ts != null ? ts.GetTileSurfacePosition(targetTileIndex) : startPosition;
                if (unit.currentLayer == TileLayer.Orbit)
                {
                    endPosition += Vector3.up * PlanetGenerator.GetOrbitHeight(unit.planetIndex);
                }

                float journeyLength = Vector3.Distance(startPosition, endPosition);
                if (journeyLength < 0.001f) continue;

                float startTime = Time.time;
                float journeyDuration = journeyLength / moveSpeed;
                if (journeyDuration <= 0) journeyDuration = 0.01f;

                while (Time.time - startTime < journeyDuration)
                {
                    float timeProgress = (Time.time - startTime) / journeyDuration;
                    float curveProgress = movementCurve.Evaluate(Mathf.Clamp01(timeProgress));

                    // Interpolate position directly on the flat map
                    unitTransform.position = Vector3.Lerp(startPosition, endPosition, curveProgress);

                    // Rotate to face movement direction on the XZ plane
                    Vector3 movementDirection = endPosition - startPosition;
                    movementDirection.y = 0f;
                    if (movementDirection.sqrMagnitude > 0.001f)
                    {
                        Quaternion targetRotation = Quaternion.LookRotation(movementDirection.normalized, Vector3.up);
                        unitTransform.rotation = Quaternion.Slerp(unitTransform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
                    }

                    yield return null;
                }

                // Snap to final position and orientation on flat map
                if (unit.currentLayer == TileLayer.Orbit)
                {
                    // Keep orbit height — don't snap to terrain surface
                    unitTransform.position = endPosition;
                }
                else
                {
                    PositionUnitOnSurface(unitTransform, targetTileIndex);
                }

                // Clear previous tile occupancy before setting new one
                try
                {
                    if (previousTileIndex >= 0 && previousTileIndex != targetTileIndex)
                        occ?.ClearOccupant(previousTileIndex, unit.currentLayer);
                }
                catch (System.Exception ex) { Debug.LogWarning($"[UnitMovementController] ClearOccupant failed: {ex.Message}"); }

                // Update current tile and occupancy using BaseUnit properties
                unit.currentTileIndex = targetTileIndex;
                try { occ?.SetOccupant(targetTileIndex, unit.gameObject, unit.currentLayer); }
                catch (System.Exception ex) { Debug.LogWarning($"[UnitMovementController] SetOccupant failed: {ex.Message}"); }

                // Check for traps on arrival (ImprovementManager accepts either type)
                if (combatUnit != null)
                    ImprovementManager.Instance?.NotifyUnitEnteredTile(targetTileIndex, combatUnit);
                else if (workerUnit != null)
                    ImprovementManager.Instance?.NotifyUnitEnteredTile(targetTileIndex, workerUnit);

                // If the unit was stored by an improvement on arrival, stop movement and finish
                if (unit.isStored)
                {
                    var playerCivLocal = CivilizationManager.Instance != null ? CivilizationManager.Instance.playerCiv : null;
                    if ((Application.isEditor || Debug.isDebugBuild) && unit.owner == playerCivLocal)
                    {
                        Debug.Log($"[UnitMoveCtrl] MOVE ORDER RESULT -> {unit.gameObject.name} moved={tilesMoved}/{path.Count} mpBefore={mpBefore} mpSpent={mpSpent} mpRemaining={ (unit!=null?unit.currentMovePoints:0) }");
                    }
                    GameEventManager.Instance.RaiseMovementCompletedEvent(unit, path[0], targetTileIndex, i + 1);
                    unit.UpdateWalkingState(false);
                    if (UnitVisionManager.Instance != null && unit.owner != null)
                    {
                        UnitVisionManager.Instance.UpdateVisionForCiv(UnitVisionManager.GetCivIndex(unit.owner));
                    }
                    yield break;
                }

                // If unit was trapped (immobilized) or killed by a trap, stop further movement this path
                if (unit.currentHealth <= 0 || unit.IsTrapped)
                {
                    var playerCivLocal = CivilizationManager.Instance != null ? CivilizationManager.Instance.playerCiv : null;
                    if (Application.isEditor || Debug.isDebugBuild)
                    {
                        if (unit.owner == playerCivLocal)
                            Debug.Log($"[UnitMoveCtrl] {unit.gameObject.name} TRAPPED/DEAD at step {i} (hp={unit.currentHealth}, trapped={unit.IsTrapped})");
                    }
                    // Summary for player-owned unit
                    if ((Application.isEditor || Debug.isDebugBuild) && unit.owner == playerCivLocal)
                    {
                        Debug.Log($"[UnitMoveCtrl] MOVE ORDER RESULT -> {unit.gameObject.name} moved={tilesMoved}/{path.Count} mpBefore={mpBefore} mpSpent={mpSpent} mpRemaining={ (workerUnit!=null?workerUnit.currentMovePoints:0) }");
                    }
                    // Fire movement completed event up to this step and exit early
                    GameEventManager.Instance.RaiseMovementCompletedEvent(unit, path[0], targetTileIndex, i + 1);
                    unit.UpdateWalkingState(false);

                    // Fog of War: unit died or was trapped; update vision for owner at the final tile reached.
                    if (UnitVisionManager.Instance != null && unit.owner != null)
                    {
                        UnitVisionManager.Instance.UpdateVisionForCiv(UnitVisionManager.GetCivIndex(unit.owner));
                    }
                    yield break;
                }

                // Fire movement event for each step
                GameEventManager.Instance.RaiseUnitMovedEvent(unit, previousTileIndex, targetTileIndex, movementCost);
                previousTileIndex = targetTileIndex;

                // Small delay between steps
                yield return new WaitForSeconds(0.1f);
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
            try { _activeMoveCoroutines.Remove(unit != null ? unit.GetInstanceID() : -1); } catch { }
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
