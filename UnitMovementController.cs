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

        string civName = civ.civData != null ? civ.civData.civName : "null";
        int civCombatCount = civ.combatUnits != null ? civ.combatUnits.Count : -1;
        int civWorkerCount = civ.workerUnits != null ? civ.workerUnits.Count : -1;
        int registryCombatCount = 0;
        int registryWorkerCount = 0;
        foreach (var cu in UnitRegistry.GetCombatUnits()) if (cu != null && cu.owner == civ) registryCombatCount++;
        foreach (var wu in UnitRegistry.GetWorkerUnits()) if (wu != null && wu.owner == civ) registryWorkerCount++;
        Debug.Log($"[UnitMoveCtrl] HandleTurnChanged START civ={civName} round={round} | civCombat={civCombatCount} civWorkers={civWorkerCount} registryCombat={registryCombatCount} registryWorkers={registryWorkerCount}");

        int combatChecked = 0, combatContinued = 0;
        int workerChecked = 0, workerContinued = 0;

        try
        {
            if (civ.combatUnits != null)
            {
                foreach (var cu in civ.combatUnits)
                {
                    if (cu == null)
                    {
                        Debug.LogWarning($"[UnitMoveCtrl] SKIPPED null combat unit entry for civ={civName}");
                        continue;
                    }
                    combatChecked++;
                    bool hasPath = cu.moveOrderPath != null;
                    bool hasSteps = hasPath && cu.moveOrderNextStep < cu.moveOrderPath.Count;
                    bool notMoving = !cu.isMoving;
                    int mp = cu.currentMovePoints;

                    if (hasPath && hasSteps && notMoving)
                    {
                        Debug.Log($"[UnitMoveCtrl] CONTINUING combat unit '{cu.name}' | mp={mp} step={cu.moveOrderNextStep}/{cu.moveOrderPath.Count} isMoving={cu.isMoving} tile={cu.currentTileIndex}");
                        combatContinued++;
                        ExecuteMovement(cu);
                    }
                    else if (hasPath)
                    {
                        Debug.LogWarning($"[UnitMoveCtrl] SKIPPED combat unit '{cu.name}' — hasPath={hasPath} hasSteps={hasSteps} notMoving={notMoving} isMoving={cu.isMoving} mp={mp} step={cu.moveOrderNextStep}/{(cu.moveOrderPath?.Count ?? -1)} tile={cu.currentTileIndex}");
                    }
                }
            }

            if (civ.workerUnits != null)
            {
                foreach (var wu in civ.workerUnits)
                {
                    if (wu == null)
                    {
                        Debug.LogWarning($"[UnitMoveCtrl] SKIPPED null worker unit entry for civ={civName}");
                        continue;
                    }
                    workerChecked++;
                    bool hasPath = wu.moveOrderPath != null;
                    bool hasSteps = hasPath && wu.moveOrderNextStep < wu.moveOrderPath.Count;
                    bool notMoving = !wu.isMoving;
                    int mp = wu.currentMovePoints;

                    if (hasPath && hasSteps && notMoving)
                    {
                        Debug.Log($"[UnitMoveCtrl] CONTINUING worker unit '{wu.name}' | mp={mp} step={wu.moveOrderNextStep}/{wu.moveOrderPath.Count} isMoving={wu.isMoving} tile={wu.currentTileIndex}");
                        workerContinued++;
                        ExecuteMovement(wu);
                    }
                    else if (hasPath)
                    {
                        Debug.LogWarning($"[UnitMoveCtrl] SKIPPED worker unit '{wu.name}' — hasPath={hasPath} hasSteps={hasSteps} notMoving={notMoving} isMoving={wu.isMoving} mp={mp} step={wu.moveOrderNextStep}/{(wu.moveOrderPath?.Count ?? -1)} tile={wu.currentTileIndex}");
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[UnitMoveCtrl] HandleTurnChanged EXCEPTION (aborted remaining units!): {ex.Message}\n{ex.StackTrace}");
        }

        Debug.Log($"[UnitMoveCtrl] HandleTurnChanged END civ={civName} round={round} | combatChecked={combatChecked} combatContinued={combatContinued} workerChecked={workerChecked} workerContinued={workerContinued}");
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

        if (Application.isEditor || Debug.isDebugBuild)
        {
            var playerCiv = CivilizationManager.Instance != null ? CivilizationManager.Instance.playerCiv : null;
            if (unit.owner == playerCiv)
                Debug.Log($"[UnitMoveCtrl] IssueMove {unit.name} pathLen={fullPath.Count} target={targetTileIndex}");
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

        var path = unit.moveOrderPath;
        int stepIndex = unit.moveOrderNextStep;
        int previousTile = unit.currentTileIndex;

        var committedTiles = new List<int>();
        int startingTile = unit.currentTileIndex;
        int mpBefore = unit.currentMovePoints;
        int mpSpent = 0;
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

            if (movementCost >= 99)
            {
                orderCancelled = true;
                breakReason = $"impassable (cost={movementCost}) at step {stepIndex} tile={targetTile}";
                break;
            }

            if (unit.currentMovePoints < movementCost)
            {
                breakReason = $"insufficient MP ({unit.currentMovePoints} < {movementCost}) at step {stepIndex} tile={targetTile}";
                break;
            }

            if (occ != null)
            {
                var existing = occ.GetOccupantObject(targetTile, unit.currentLayer);
                if (existing != null && existing.GetInstanceID() != unit.gameObject.GetInstanceID())
                {
                    breakReason = $"tile {targetTile} occupied by {existing.name} (id={existing.GetInstanceID()}) at step {stepIndex}";
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
                        (combatUnit.data.unitType == CombatCategory.Ship ||
                         combatUnit.data.unitType == CombatCategory.Boat ||
                         combatUnit.data.unitType == CombatCategory.Submarine ||
                         combatUnit.data.unitType == CombatCategory.SeaCrawler);
                    if (!isNaval && !(workerUnit != null))
                    {
                        breakReason = $"water tile {targetTile} but unit is not naval/worker at step {stepIndex}";
                        break;
                    }
                }
            }

            bool claimed = occ == null || occ.TrySetOccupant(targetTile, unit.gameObject, unit.currentLayer);
            if (!claimed)
            {
                breakReason = $"TrySetOccupant failed at step {stepIndex} tile={targetTile}";
                break;
            }

            if (previousTile >= 0 && previousTile != targetTile)
            {
                try { occ?.ClearOccupant(previousTile, unit.currentLayer); } catch { }
            }

            unit.DeductMovePoints(movementCost);
            mpSpent += movementCost;
            unit.currentTileIndex = targetTile;
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

            if (unit.isStored || unit.currentHealth <= 0 || unit.IsTrapped)
            {
                breakReason = $"unit state changed: stored={unit.isStored} hp={unit.currentHealth} trapped={unit.IsTrapped}";
                break;
            }
        }

        if (breakReason == null && stepIndex >= path.Count)
            breakReason = "path complete";

        unit.moveOrderNextStep = stepIndex;

        if (orderCancelled || stepIndex >= path.Count)
        {
            unit.moveOrderPath = null;
            unit.moveOrderNextStep = 0;
        }

        Debug.Log($"[UnitMoveCtrl] ExecuteMovement {unit.name} committed={committedTiles.Count} mpBefore={mpBefore} mpSpent={mpSpent} mpRemaining={unit.currentMovePoints} step={stepIndex}/{path.Count} orderDone={unit.moveOrderPath == null} cancelled={orderCancelled} breakReason=\"{breakReason}\" isMoving={unit.isMoving}");

        if (committedTiles.Count == 0) return;

        SyncUnitWrapRegistration(unit, committedTiles[committedTiles.Count - 1]);

        // Raise per-step UnitMoved events
        for (int i = 0; i < committedTiles.Count; i++)
        {
            int from = i == 0 ? startingTile : committedTiles[i - 1];
            try { GameEventManager.Instance.RaiseUnitMovedEvent(unit, from, committedTiles[i], 1); } catch { }
        }

        // PHASE 2: Animate the committed tiles (coroutine with guaranteed cleanup)
        unit.UpdateWalkingState(true);
        var c = StartCoroutine(AnimateAlongPath(unit, committedTiles));
        try { _activeMoveCoroutines[unit.GetInstanceID()] = c; } catch { }
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
        }
        finally
        {
            try { _activeMoveCoroutines.Remove(unit != null ? unit.GetInstanceID() : -1); } catch { }

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
        return _activeMoveCoroutines.ContainsKey(unit.GetInstanceID());
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

            var wrapManager = FindObjectsByType<HexMapChunkManager>(FindObjectsSortMode.None).FirstOrDefault(m => m.PlanetGenerator == planetGenerator);
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
