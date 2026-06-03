using System.Collections;
using System.Collections.Generic;
using UnityEngine;
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

        try
        {
            if (civ.combatUnits != null)
            {
                foreach (var cu in civ.combatUnits)
                {
                    if (cu == null) continue;
                    if (cu.moveOrderPath != null && cu.moveOrderNextStep < cu.moveOrderPath.Count && !cu.isMoving)
                    {
                        ExecuteMovement(cu);
                    }
                }
            }

            if (civ.workerUnits != null)
            {
                foreach (var wu in civ.workerUnits)
                {
                    if (wu == null) continue;
                    if (wu.moveOrderPath != null && wu.moveOrderNextStep < wu.moveOrderPath.Count && !wu.isMoving)
                    {
                        ExecuteMovement(wu);
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[UnitMoveCtrl] HandleTurnChanged EXCEPTION: {ex.Message}\n{ex.StackTrace}");
        }
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
        int unitId = unit != null ? unit.GetRuntimeId() : 0;
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

                    // Zone of Control: entering tiles adjacent to enemy units costs extra MP
                    moveCost += CombatHelpers.GetZoneOfControlCost(neighbor, unit, pIndex);
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

    /// <summary>
    /// Danger-aware pathfinding for AI units. Adds DangerMap values as weighted cost
    /// to the movement cost, causing the path to route around high-threat tiles when
    /// a slightly longer but safer route exists.
    /// </summary>
    /// <param name="dangerWeight">How much danger cost matters vs movement cost. 0 = ignore danger, 1.0 = full danger penalty.</param>
    public List<int> FindPathDangerAware(int startIndex, int endIndex, BaseUnit unit, DangerMap dangerMap, float dangerWeight = 0.5f)
    {
        if (dangerMap == null || dangerWeight <= 0f)
            return FindPath(startIndex, endIndex, unit);

        int pIndex = unit != null ? unit.planetIndex : 0;
        var ts = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;
        if (ts == null || !ts.IsReady()) return null;

        if (startIndex == endIndex) return new List<int>();

        bool isOrbit = unit != null && unit.currentLayer == TileLayer.Orbit;
        int orbitCost = BiomeHelper.DefaultOrbitMovementCost;
        if (isOrbit)
        {
            var cu = unit as CombatUnit;
            if (cu != null && cu.data != null) orbitCost = cu.data.orbitMovementCost;
        }

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

            if (bestCost.TryGetValue(current, out float best) && currentG > best) continue;

            if (current == endIndex)
            {
                var path = new List<int>();
                int trace = endIndex;
                while (trace != startIndex)
                {
                    path.Add(trace);
                    if (!cameFrom.TryGetValue(trace, out int prev)) return null;
                    trace = prev;
                }
                path.Reverse();
                return path;
            }

            expanded++;
            if (expanded > MaxPathSearchNodes) return null;

            foreach (int neighbor in ts.GetNeighbors(current))
            {
                var neighborTile = ts.GetTileData(neighbor);
                if (neighborTile == null) continue;

                float moveCost;
                if (isOrbit)
                {
                    moveCost = orbitCost;
                }
                else
                {
                    int baseCost = BiomeHelper.GetMovementCost(neighborTile, unit);
                    if (baseCost >= 99) continue;

                    baseCost += CombatHelpers.GetZoneOfControlCost(neighbor, unit, pIndex);

                    // Add danger cost (weighted)
                    float danger = dangerMap.GetDanger(neighbor);
                    moveCost = baseCost + danger * dangerWeight;
                }

                float tentativeG = currentG + moveCost;
                if (bestCost.TryGetValue(neighbor, out float existingG) && tentativeG >= existingG) continue;

                bestCost[neighbor] = tentativeG;
                cameFrom[neighbor] = current;
                float h = useHeuristic ? ts.GetWrappedHexDistance(neighbor, endIndex) * MIN_MOVE_COST : 0f;
                openSet.Enqueue(neighbor, tentativeG + h);
            }
        }
        return null;
    }

    /// <summary>
    /// Issue a new movement order for a unit. Single FindPath call, sets moveOrder, starts execution.
    /// Called by BaseUnit.MoveTo and can be called directly by AI/UI.
    /// </summary>
    public void IssueMove(BaseUnit unit, int targetTileIndex)
    {
        if (unit == null) return;

        var fullPath = FindPath(unit.currentTileIndex, targetTileIndex, unit);
        if (fullPath == null || fullPath.Count == 0)
        {
            try
            {
                var playerCiv = CivilizationManager.Instance != null ? CivilizationManager.Instance.playerCiv : null;
                if (Application.isEditor || Debug.isDebugBuild)
                {
                    if (unit.owner == playerCiv)
                    {
                        var ts2 = TileSystem.GetForPlanet(unit.planetIndex) ?? TileSystem.Instance;
                        var td = ts2 != null ? ts2.GetTileData(targetTileIndex) : null;
                        int cost = td != null ? BiomeHelper.GetMovementCost(td, unit) : -1;
                        Debug.LogWarning($"[UnitMoveCtrl] IssueMove failed to find path for {unit.name} -> target={targetTileIndex} passable={(td!=null && td.isPassable)} cost={cost}");
                    }
                }
                if (UIManager.Instance != null && unit.owner == playerCiv)
                    UIManager.Instance.ShowNotification($"{unit.UnitName} can't reach that tile!");
            }
            catch { }
            return;
        }

        StopMoveForUnit(unit);
        unit.UpdateWalkingState(false);

        unit.moveOrderPath = new List<int>(fullPath);
        unit.moveOrderNextStep = 0;

        // Locked stack movement: share the same path with all stacked companions
        var stackCompanions = unit.GetStackedUnits();
        foreach (var comp in stackCompanions)
        {
            StopMoveForUnit(comp);
            comp.UpdateWalkingState(false);
            comp.moveOrderPath = new List<int>(fullPath);
            comp.moveOrderNextStep = 0;
        }

        if (Application.isEditor || Debug.isDebugBuild)
        {
            var playerCiv = CivilizationManager.Instance != null ? CivilizationManager.Instance.playerCiv : null;
            if (unit.owner == playerCiv)
                Debug.Log($"[UnitMoveCtrl] IssueMove {unit.name} pathLen={fullPath.Count} target={targetTileIndex} stackSize={stackCompanions.Count + 1}");
        }

        ExecuteMovement(unit);
    }

    /// <summary>
    /// Two-phase movement: commit as many steps as MP allows (synchronous), then animate (coroutine).
    /// Called by IssueMove and HandleTurnChanged.
    /// </summary>
    public void ExecuteMovement(BaseUnit unit)
    {
        if (unit == null) return;
        if (unit.moveOrderPath == null || unit.moveOrderNextStep >= unit.moveOrderPath.Count)
        {
            Debug.Log($"[UnitMoveCtrl] ExecuteMovement {unit.name} — early exit: path={unit.moveOrderPath?.Count ?? -1} nextStep={unit.moveOrderNextStep}");
            unit.moveOrderPath = null;
            unit.moveOrderNextStep = 0;
            return;
        }

        StopMoveForUnit(unit);

        int pIndex = unit.planetIndex;
        var ts = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;
        var occ = TileOccupancyManager.GetForPlanet(pIndex) ?? TileOccupancyManager.Instance;
        CombatUnit combatUnit = unit as CombatUnit;
        WorkerUnit workerUnit = unit as WorkerUnit;

        // Gather stacked companions for locked movement
        var stackCompanions = unit.GetStackedUnits();
        foreach (var comp in stackCompanions)
            StopMoveForUnit(comp);

        var path = unit.moveOrderPath;
        int stepIndex = unit.moveOrderNextStep;
        int previousTile = unit.currentTileIndex;

        var committedTiles = new List<int>();
        int startingTile = unit.currentTileIndex;
        int mpBefore = unit.currentMovePoints;
        bool orderCancelled = false;
        string breakReason = null;

        // PHASE 1: Commit as many steps as MP allows (no animation, no yields)
        while (stepIndex < path.Count)
        {
            int targetTile = path[stepIndex];
            var tileData = ts != null ? ts.GetTileData(targetTile) : null;

            int movementCost;
            if (unit.currentLayer == TileLayer.Orbit)
                movementCost = (combatUnit != null && combatUnit.data != null) ? combatUnit.data.orbitMovementCost : BiomeHelper.DefaultOrbitMovementCost;
            else
                movementCost = tileData != null ? BiomeHelper.GetMovementCost(tileData, unit) : 1;

            // Apply Zone-of-Control penalty so execution matches pathfinder predictions
            if (unit.currentLayer != TileLayer.Orbit)
                movementCost += CombatHelpers.GetZoneOfControlCost(targetTile, unit, pIndex);

            if (movementCost >= 99)
            {
                orderCancelled = true;
                breakReason = $"impassable (cost={movementCost}) at step {stepIndex} tile={targetTile}";
                break;
            }

            // Locked stack movement: use the minimum MP across the stack
            int effectiveMP = unit.currentMovePoints;
            foreach (var comp in stackCompanions)
                effectiveMP = Mathf.Min(effectiveMP, comp.currentMovePoints);

            if (effectiveMP < movementCost)
            {
                breakReason = $"insufficient MP ({effectiveMP} < {movementCost}) at step {stepIndex} tile={targetTile}";
                break;
            }

            // Stack-aware occupancy check
            if (occ != null)
            {
                int maxStack = unit.owner != null ? unit.owner.GetMaxStackSize() : 1;
                var allIds = occ.GetAllOccupantIds(targetTile, unit.currentLayer);
                int selfId = unit.gameObject.GetRuntimeId();
                bool blocked = false;
                int othersCount = 0;
                foreach (int occId in allIds)
                {
                    if (occId == selfId) continue;
                    othersCount++;
                    var obj = UnitRegistry.GetObject(occId);
                    if (obj == null) continue;
                    var other = obj.GetComponent<BaseUnit>();
                    if (other == null || other.owner != unit.owner) { blocked = true; break; }
                }
                if (blocked || othersCount >= maxStack)
                {
                    breakReason = $"tile {targetTile} stack full or enemy present at step {stepIndex}";
                    break;
                }
            }

            if (unit.currentLayer != TileLayer.Orbit)
            {
                if (tileData == null || !tileData.isPassable)
                {
                    orderCancelled = true;
                    breakReason = $"impassable terrain at step {stepIndex} tile={targetTile} tileData={tileData != null}";
                    break;
                }

                if (!tileData.isLand)
                {
                    bool isNaval = combatUnit != null && combatUnit.data != null &&
                        CombatUnitData.IsNavalCategory(combatUnit.data.unitType);
                    if (!isNaval && !(workerUnit != null))
                    {
                        breakReason = $"water tile {targetTile} but unit is not naval/worker at step {stepIndex}";
                        break;
                    }
                }
            }

            // Stack-aware occupancy claim for lead unit
            int claimedSlot = -1;
            if (occ != null)
            {
                int stackTotal = 1 + stackCompanions.Count;
                int maxStack = unit.owner != null ? unit.owner.GetMaxStackSize() : 1;
                maxStack = Mathf.Max(maxStack, stackTotal); // must accommodate the full stack
                claimedSlot = occ.TryAddToStack(targetTile, unit.currentLayer, unit.gameObject, maxStack);
            }
            bool claimed = occ == null || claimedSlot >= 0;
            if (claimed && claimedSlot >= 0) unit.stackSlot = claimedSlot;

            // Claim slots for companions
            if (claimed && occ != null)
            {
                int stackTotal = 1 + stackCompanions.Count;
                int maxStack = unit.owner != null ? unit.owner.GetMaxStackSize() : 1;
                maxStack = Mathf.Max(maxStack, stackTotal);
                foreach (var comp in stackCompanions)
                {
                    int compSlot = occ.TryAddToStack(targetTile, comp.currentLayer, comp.gameObject, maxStack);
                    if (compSlot >= 0) comp.stackSlot = compSlot;
                }
            }
            if (!claimed)
            {
                breakReason = $"TrySetOccupant failed at step {stepIndex} tile={targetTile}";
                break;
            }

            // Clear old occupancy for lead unit and companions
            if (previousTile >= 0 && previousTile != targetTile)
            {
                try { occ?.ClearOccupantById(previousTile, unit.currentLayer, unit.gameObject.GetRuntimeId()); } catch { }
                foreach (var comp in stackCompanions)
                {
                    try { occ?.ClearOccupantById(previousTile, comp.currentLayer, comp.gameObject.GetRuntimeId()); } catch { }
                }
            }

            unit.DeductMovePoints(movementCost);
            unit.currentTileIndex = targetTile;
            // Deduct MP and update position for all companions
            foreach (var comp in stackCompanions)
            {
                comp.DeductMovePoints(movementCost);
                comp.currentTileIndex = targetTile;
            }
            previousTile = targetTile;
            stepIndex++;
            committedTiles.Add(targetTile);

            try
            {
                if (combatUnit != null)
                    ImprovementManager.Instance?.NotifyUnitEnteredTile(targetTile, combatUnit);
                else if (workerUnit != null)
                    ImprovementManager.Instance?.NotifyUnitEnteredTile(targetTile, workerUnit);
            }
            catch { }

            // Check whether the unit has stepped into a ruin discovery radius.
            try
            {
                if (AncientRuinsManager.Instance != null && unit.owner != null)
                {
                    var ts2 = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;
                    Vector3 tileWorldPos = (ts2 != null && ts2.IsReady())
                        ? ts2.GetTileSurfacePosition(targetTile)
                        : unit.transform.position;
                    AncientRuinsManager.Instance.CheckForRuinDiscovery(pIndex, tileWorldPos, unit.owner);
                }
            }
            catch { }

            if (unit.isStored || unit.currentHealth <= 0 || unit.IsTrapped)
            {
                breakReason = $"unit state changed: stored={unit.isStored} hp={unit.currentHealth} trapped={unit.IsTrapped}";
                break;
            }
        }

        if (breakReason == null && stepIndex >= path.Count)
            breakReason = "path complete";

        unit.moveOrderNextStep = stepIndex;

        // Sync companion move order state
        foreach (var comp in stackCompanions)
        {
            comp.moveOrderNextStep = stepIndex;
        }

        if (orderCancelled || stepIndex >= path.Count)
        {
            unit.moveOrderPath = null;
            unit.moveOrderNextStep = 0;
            foreach (var comp in stackCompanions)
            {
                comp.moveOrderPath = null;
                comp.moveOrderNextStep = 0;
            }
        }

        Debug.Log($"[UnitMoveCtrl] ExecuteMovement {unit.name} committed={committedTiles.Count} mpBefore={mpBefore} mpRemaining={unit.currentMovePoints} step={stepIndex}/{path.Count} orderDone={unit.moveOrderPath == null} cancelled={orderCancelled} breakReason=\"{breakReason}\" isMoving={unit.isMoving} stackSize={stackCompanions.Count + 1}");

        if (committedTiles.Count == 0) return;

        SyncUnitWrapRegistration(unit, committedTiles[committedTiles.Count - 1]);
        foreach (var comp in stackCompanions)
            SyncUnitWrapRegistration(comp, committedTiles[committedTiles.Count - 1]);

        // Raise per-step UnitMoved events
        for (int i = 0; i < committedTiles.Count; i++)
        {
            int from = i == 0 ? startingTile : committedTiles[i - 1];
            try { GameEventManager.Instance.RaiseUnitMovedEvent(unit, from, committedTiles[i], 1); } catch { }
        }

        // PHASE 2: Animate the committed tiles (coroutine with guaranteed cleanup)
        unit.UpdateWalkingState(true);
        var c = StartCoroutine(AnimateAlongPath(unit, committedTiles));
        try { _activeMoveCoroutines[unit.GetRuntimeId()] = c; } catch { }

        // Animate stacked companions alongside lead unit
        foreach (var comp in stackCompanions)
        {
            comp.UpdateWalkingState(true);
            var cc = StartCoroutine(AnimateAlongPath(comp, committedTiles));
            try { _activeMoveCoroutines[comp.GetRuntimeId()] = cc; } catch { }
        }
    }

    /// <summary>
    /// Segments the remaining move-order path into per-turn sublists for path preview display.
    /// Does not modify any state. Used by UI only.
    /// </summary>
    public List<List<int>> GetPathSegmentsForPreview(BaseUnit unit)
    {
        if (unit == null || unit.moveOrderPath == null || unit.moveOrderNextStep >= unit.moveOrderPath.Count)
            return null;

        var path = unit.moveOrderPath;
        int startIdx = unit.moveOrderNextStep;
        int remaining = unit.currentMovePoints;
        int fullPerTurn = unit.GetStartingMovePoints();

        var ts = TileSystem.GetForPlanet(unit.planetIndex) ?? TileSystem.Instance;
        var segments = new List<List<int>>();
        var currentSeg = new List<int>();

        for (int i = startIdx; i < path.Count; i++)
        {
            var td = ts != null ? ts.GetTileData(path[i]) : null;
            int cost = td != null ? BiomeHelper.GetMovementCost(td, unit) : 1;
            if (cost >= 99) break;

            if (remaining < cost)
            {
                if (currentSeg.Count > 0)
                {
                    segments.Add(new List<int>(currentSeg));
                    currentSeg.Clear();
                }
                if (fullPerTurn <= 0) break;
                while (remaining < cost) remaining += fullPerTurn;
            }

            currentSeg.Add(path[i]);
            remaining -= cost;
        }

        if (currentSeg.Count > 0)
            segments.Add(currentSeg);

        return segments;
    }

    /// <summary>
    /// Segments an explicit path into per-turn sublists for hover-preview display.
    /// Used before a move order is issued (e.g., mouse-hover path preview).
    /// </summary>
    public List<List<int>> GetPathSegmentsForDisplay(BaseUnit unit, List<int> path)
    {
        if (unit == null || path == null || path.Count == 0) return null;

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

            if (remaining < cost)
            {
                if (currentSeg.Count > 0)
                {
                    segments.Add(new List<int>(currentSeg));
                    currentSeg.Clear();
                }
                if (fullPerTurn <= 0) break;
                while (remaining < cost) remaining += fullPerTurn;
            }

            currentSeg.Add(tileIndex);
            remaining -= cost;
        }

        if (currentSeg.Count > 0)
            segments.Add(currentSeg);

        return segments;
    }

    /// <summary>
    /// Pure visual coroutine: smoothly interpolates the unit's transform along already-committed tiles.
    /// All gameplay state was committed in ExecuteMovement before this runs.
    /// Guaranteed to clear isMoving and fire completion events in the finally block.
    /// </summary>
    private IEnumerator AnimateAlongPath(BaseUnit unit, List<int> committedTiles)
    {
        if (unit == null || committedTiles == null || committedTiles.Count == 0)
        {
            if (unit != null) unit.UpdateWalkingState(false);
            yield break;
        }

        try
        {
            Transform unitTransform = unit.transform;
            int pIndex = unit.planetIndex;
            var ts = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;

            BuildSmoothedVisualPath(unit, ts, committedTiles, unitTransform.position,
                out var visualPoints, out var visualDistances, out var stepEndDistances);
            float totalVisualDistance = visualDistances.Count > 0 ? visualDistances[visualDistances.Count - 1] : 0f;

            if (totalVisualDistance > 0.001f)
            {
                float totalDuration = Mathf.Max(totalVisualDistance / moveSpeed, 0.01f);
                float elapsed = 0f;

                while (elapsed < totalDuration)
                {
                    elapsed += Time.deltaTime;
                    float normalizedTime = Mathf.Clamp01(elapsed / totalDuration);
                    float curvedProgress = movementCurve.Evaluate(normalizedTime);
                    float travelDistance = curvedProgress * totalVisualDistance;

                    Vector3 sampledPosition = SamplePolylinePosition(visualPoints, visualDistances, travelDistance);
                    unitTransform.position = sampledPosition;
                    UpdateRotationAlongPolyline(unitTransform, visualPoints, visualDistances, travelDistance, totalVisualDistance);
                    yield return null;
                }
            }

            // Snap to final tile position
            int finalTile = committedTiles[committedTiles.Count - 1];
            if (unit.currentLayer == TileLayer.Orbit)
                unitTransform.position = GetClosestWrappedWorldPosition(unit, ts, unitTransform.position, finalTile);
            else
                PositionUnitOnSurface(unitTransform, finalTile, unit);

            // Apply stack offset so stacked units appear in separate rows
            unit.ApplyStackOffset();
        }
        finally
        {
            try { _activeMoveCoroutines.Remove(unit != null ? unit.GetRuntimeId() : -1); } catch { }

            if (unit != null)
            {
                if (committedTiles != null && committedTiles.Count > 0)
                    SyncUnitWrapRegistration(unit, committedTiles[committedTiles.Count - 1]);

                unit.UpdateWalkingState(false);

                try
                {
                    GameEventManager.Instance.RaiseMovementCompletedEvent(
                        (MonoBehaviour)unit, committedTiles[0],
                        committedTiles[committedTiles.Count - 1], committedTiles.Count);
                }
                catch { }

                try
                {
                    if (UnitVisionManager.Instance != null && unit.owner != null)
                        UnitVisionManager.Instance.UpdateVisionForCiv(UnitVisionManager.GetCivIndex(unit.owner));
                }
                catch { }
            }
        }
    }

    /// <summary>
    /// Issue movement for a packed Herd. Finds a path and animates the herd visual along it.
    /// This is a lightweight movement flow separate from BaseUnit movement semantics.
    /// </summary>
    public void IssueHerdMove(Herd herd, int targetTileIndex)
    {
        if (herd == null) return;
        var fullPath = FindPath(herd.currentTileIndex, targetTileIndex, null);
        if (fullPath == null || fullPath.Count == 0)
        {
            try { if (UIManager.Instance != null && herd.owner == CivilizationManager.Instance?.playerCiv) UIManager.Instance.ShowNotification("Herd can't reach that tile!"); } catch { }
            return;
        }

        // Start visual animation coroutine
        StartCoroutine(AnimateHerdAlongPath(herd, fullPath));
    }

    /// <summary>
    /// Segment a herd path into per-turn groups using simple per-tile costs (1 per tile) and herd MP.
    /// Used for preview display.
    /// </summary>
    public List<System.Collections.Generic.List<int>> GetPathSegmentsForHerd(Herd herd, System.Collections.Generic.List<int> path)
    {
        if (herd == null || path == null || path.Count == 0) return null;
        int remaining = herd.movementPoints;
        int fullPerTurn = herd.maxMovementPoints;
        var segments = new System.Collections.Generic.List<System.Collections.Generic.List<int>>();
        var currentSeg = new System.Collections.Generic.List<int>();

        foreach (int tileIndex in path)
        {
            int cost = 1;
            if (remaining < cost)
            {
                if (currentSeg.Count > 0)
                {
                    segments.Add(new System.Collections.Generic.List<int>(currentSeg));
                    currentSeg.Clear();
                }
                if (fullPerTurn <= 0) break;
                while (remaining < cost) remaining += fullPerTurn;
            }

            currentSeg.Add(tileIndex);
            remaining -= cost;
        }

        if (currentSeg.Count > 0) segments.Add(currentSeg);
        return segments;
    }

    private System.Collections.IEnumerator AnimateHerdAlongPath(Herd herd, System.Collections.Generic.List<int> path)
    {
        if (herd == null || path == null || path.Count == 0) yield break;
        var ts = TileSystem.GetForPlanet(herd.planetIndex) ?? TileSystem.Instance;
        if (ts == null) yield break;

        // Ensure occupancy manager updates
        var occ = TileOccupancyManager.GetForPlanet(herd.planetIndex) ?? TileOccupancyManager.Instance;

        Vector3 startPos = herd.transform.position;
        for (int i = 0; i < path.Count; i++)
        {
            int tile = path[i];
            Vector3 targetPos = ts.GetTileSurfacePosition(tile) + Vector3.up * 0.02f;

            float distance = Vector3.Distance(herd.transform.position, targetPos);
            float duration = Mathf.Max(0.01f, distance / moveSpeed);
            float elapsed = 0f;
            Vector3 from = herd.transform.position;

            // Clear old occupancy before stepping onto new tile (best-effort)
            try { if (occ != null) occ.ClearOccupant(herd.currentTileIndex, TileLayer.Surface); } catch { }

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float curved = movementCurve.Evaluate(t);
                herd.transform.position = Vector3.Lerp(from, targetPos, curved);

                // simple rotation to face movement direction
                Vector3 dir = (targetPos - herd.transform.position);
                if (dir.sqrMagnitude > 0.001f)
                {
                    herd.transform.rotation = Quaternion.Slerp(herd.transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * rotationSpeed);
                }
                yield return null;
            }

            // snap to tile center
            herd.transform.position = targetPos;
            int previous = herd.currentTileIndex;
            herd.currentTileIndex = tile;

            try { if (occ != null) occ.SetOccupant(tile, herd.gameObject, TileLayer.Surface); } catch { }
            try { if (previous >= 0 && occ != null) occ.ClearOccupant(previous, TileLayer.Surface); } catch { }

            yield return null;
        }

        yield break;
    }

    /// <summary>
    /// Stop any active movement coroutine for the provided unit.
    /// Safe to call even if no coroutine is active.
    /// </summary>
    public void StopMoveForUnit(BaseUnit unit)
    {
        if (unit == null) return;
        int id = unit.GetRuntimeId();
        if (_activeMoveCoroutines.TryGetValue(id, out var coroutine))
        {
            try { if (coroutine != null) StopCoroutine(coroutine); } catch { }
            _activeMoveCoroutines.Remove(id);
            // StopCoroutine does NOT run the finally block, so the coroutine's
            // cleanup (UpdateWalkingState(false)) never executes. We must clear
            // isMoving here or it stays true forever, blocking HandleTurnChanged
            // continuation and potentially causing root-motion drift.
            unit.isMoving = false;
            Debug.Log($"[UnitMoveCtrl] StopMoveForUnit killed coroutine for {unit.name}, forced isMoving=false");
        }
    }

    public bool HasActiveMove(BaseUnit unit)
    {
        if (unit == null) return false;
        return _activeMoveCoroutines.ContainsKey(unit.GetRuntimeId());
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

    private float GetHorizontalWrapWidth(BaseUnit unit)
    {
        try
        {
            var gridForPlanet = unit?.owner?.GetGridForPlanetIndex(unit.planetIndex);
            if (gridForPlanet != null && gridForPlanet.MapWidth > 0.001f)
                return gridForPlanet.MapWidth;

            var planetGenerator = GameManager.Instance?.GetPlanetGenerator(unit != null ? unit.planetIndex : 0);
            if (planetGenerator != null && planetGenerator.Grid != null && planetGenerator.Grid.MapWidth > 0.001f)
                return planetGenerator.Grid.MapWidth;
        }
        catch { }

        return 0f;
    }

    private Vector3 NormalizeWrappedX(Vector3 referencePosition, Vector3 targetPosition, float wrapWidth)
    {
        if (wrapWidth <= 0.001f) return targetPosition;

        float dx = targetPosition.x - referencePosition.x;
        if (dx > wrapWidth * 0.5f)
            targetPosition.x -= wrapWidth;
        else if (dx < -wrapWidth * 0.5f)
            targetPosition.x += wrapWidth;

        return targetPosition;
    }

    private Vector3 GetClosestWrappedWorldPosition(BaseUnit unit, TileSystem ts, Vector3 referencePosition, int tileIndex)
    {
        Vector3 targetPosition = GetMovementWorldPosition(unit, ts, tileIndex);
        return NormalizeWrappedX(referencePosition, targetPosition, GetHorizontalWrapWidth(unit));
    }

    private void SyncUnitWrapRegistration(BaseUnit unit, int tileIndex)
    {
        if (unit == null || tileIndex < 0) return;

        try
        {
            var planetGenerator = GameManager.Instance?.GetPlanetGenerator(unit.planetIndex) ?? unit.owner?.GetPlanetGeneratorForIndex(unit.planetIndex);
            if (planetGenerator == null) return;

            var wrapManager = FindObjectsByType<HexMapChunkManager>().FirstOrDefault(m => m.PlanetGenerator == planetGenerator);
            wrapManager?.RegisterObjectForWrapAtTile(tileIndex, unit.gameObject);
        }
        catch { }
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
            Vector3 targetCenter = GetClosestWrappedWorldPosition(unit, ts, segmentStart, path[i]);
            Vector3 segmentEnd = targetCenter;
            if (i < path.Count - 1)
            {
                Vector3 nextCenter = GetClosestWrappedWorldPosition(unit, ts, targetCenter, path[i + 1]);
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

    private void PositionUnitOnSurface(Transform unitTransform, int tileIndex, BaseUnit unit = null)
    {
        // Best-effort: use current planet TileSystem for surface positioning.
        int pIndex = (GameManager.Instance != null) ? GameManager.Instance.currentPlanetIndex : 0;
        var ts = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;
        var tileData = ts != null ? ts.GetTileData(tileIndex) : null;
        if (tileData == null) return;

        // Place unit at terrain surface with proper height and upright orientation
        Vector3 flatCenter = ts.GetTileSurfacePosition(tileIndex);
        if (unit != null)
            flatCenter = NormalizeWrappedX(unitTransform.position, flatCenter, GetHorizontalWrapWidth(unit));
        unitTransform.position = flatCenter;

        Vector3 forward = unitTransform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        unitTransform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
    }
} 
