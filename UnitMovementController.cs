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

    private sealed class MinHeap
    {
        private readonly List<(int tile, float priority)> heap = new List<(int, float)>();

        public int Count => heap.Count;
        public void Clear() => heap.Clear();

        public void Enqueue(int tile, float priority)
        {
            heap.Add((tile, priority));
            int i = heap.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (heap[i].priority < heap[p].priority) { var tmp = heap[i]; heap[i] = heap[p]; heap[p] = tmp; i = p; }
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
                if (l < n && heap[l].priority < heap[s].priority) s = l;
                if (r < n && heap[r].priority < heap[s].priority) s = r;
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
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.OnTurnChanged += HandleTurnChanged;
        }
    }

    void OnDestroy()
    {
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.OnTurnChanged -= HandleTurnChanged;
        }
    }

    private void HandleTurnChanged(Civilization civ, int round)
    {
        if (civ == null) return;

        // Resume queued movement for units that belong to this civ
        try
        {
            foreach (var cu in UnitRegistry.GetCombatUnits())
            {
                if (cu == null || cu.owner != civ) continue;
                if (cu.queuedMovementSegments != null && cu.queuedMovementSegments.Count > 0 && !cu.isMoving)
                {
                    var seg = cu.queuedMovementSegments.Dequeue();
                    if (seg != null && seg.Count > 0)
                        StartMoveForUnit(cu, seg);
                }
            }

            foreach (var wu in UnitRegistry.GetWorkerUnits())
            {
                if (wu == null || wu.owner != civ) continue;
                if (wu.queuedMovementSegments != null && wu.queuedMovementSegments.Count > 0 && !wu.isMoving)
                {
                    var seg = wu.queuedMovementSegments.Dequeue();
                    if (seg != null && seg.Count > 0)
                        StartMoveForUnit(wu, seg);
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
        // Multi-planet: FindPath without unit context uses the current planet.
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

        bool isOrbit = unit != null && unit.currentLayer == TileLayer.Orbit;
        int orbitCost = BiomeHelper.DefaultOrbitMovementCost;
        if (isOrbit)
        {
            var cu = unit as CombatUnit;
            if (cu != null && cu.data != null)
                orbitCost = cu.data.orbitMovementCost;
        }

        // Dijkstra (h=0) guarantees optimal paths regardless of hex spacing or map wrapping.
        openSet.Clear();
        bestCost.Clear();
        cameFrom.Clear();

        bestCost[startIndex] = 0f;
        openSet.Enqueue(startIndex, 0f);

        int expanded = 0;

        while (openSet.Count > 0)
        {
            int current = openSet.Dequeue();

            float currentG = bestCost.TryGetValue(current, out float cg) ? cg : float.MaxValue;

            // Skip stale heap entries: if we already found a cheaper way to this tile, ignore
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
                        return null;
                    trace = prev;
                }
                path.Reverse();
                return path;
            }

            expanded++;
            if (expanded > MaxPathSearchNodes)
            {
                Debug.LogWarning($"[UnitMovementController] Aborting path search: expanded > {MaxPathSearchNodes} nodes (start={startIndex} end={endIndex})");
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
                openSet.Enqueue(neighbor, tentativeG);
            }
        }

        // No path found
        return null;
    }

    public List<int> TrimPathToAvailableMovement(BaseUnit unit, List<int> path)
    {
        if (unit == null || path == null || path.Count == 0) return path;
        if (unit.currentLayer == TileLayer.Orbit) return path;
        if (unit is not WorkerUnit workerUnit) return path;

        int pIndex = unit.planetIndex;
        var ts = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;
        if (ts == null) return path;

        int remainingMovePoints = workerUnit.currentMovePoints;
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
        if (unit == null || unit is not WorkerUnit)
        {
            return new List<List<int>> { new List<int>(path) };
        }

        var worker = unit as WorkerUnit;
        int remaining = worker.currentMovePoints;
        int fullPerTurn = worker.GetStartingMovePoints();

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
                // reset for next turn
                remaining = fullPerTurn;
                // If still can't pay this tile even on a fresh turn, abort
                if (cost > fullPerTurn) break;
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
            Debug.Log($"[UnitMoveCtrl] {unit.gameObject.name} START path len={path.Count} from tile {currentTileIndex} | type={unit.GetType().Name}");
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

                if (workerUnit != null && workerUnit.currentMovePoints < movementCost)
                {
                    Debug.Log($"[UnitMoveCtrl] {unit.gameObject.name} OUT OF MOVE POINTS at step {i}/{path.Count} (has {workerUnit.currentMovePoints}, need {movementCost})");
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
                    Debug.Log($"[UnitMoveCtrl] {unit.gameObject.name} movement blocked at step {i}/{path.Count} for tile {targetTileIndex}.");
                    unit.UpdateWalkingState(false);
                    if (i > 0)
                        GameEventManager.Instance.RaiseMovementCompletedEvent(unit, path[0], path[i - 1], i);
                    yield break;
                }

                // Deduct movement points for workers (they still use turn-based movement)
                if (workerUnit != null)
                {
                    workerUnit.DeductMovePoints(movementCost);
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

                // If unit was trapped (immobilized) or killed by a trap, stop further movement this path
                if (unit.currentHealth <= 0 || unit.IsTrapped)
                {
                    Debug.Log($"[UnitMoveCtrl] {unit.gameObject.name} TRAPPED/DEAD at step {i} (hp={unit.currentHealth}, trapped={unit.IsTrapped})");
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
