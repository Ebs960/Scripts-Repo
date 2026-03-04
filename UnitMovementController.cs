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

    private class PathNode : System.IComparable<PathNode>
    {
        public int tileIndex;
        public float gCost; // cost from start
        public float hCost; // heuristic cost to end
        public float FCost => gCost + hCost;
        public PathNode parent;

        public PathNode(int tileIndex) { this.tileIndex = tileIndex; }

        public int CompareTo(PathNode other)
        {
            int cmp = FCost.CompareTo(other.FCost);
            if (cmp == 0) cmp = hCost.CompareTo(other.hCost);
            if (cmp == 0) cmp = tileIndex.CompareTo(other.tileIndex);
            return cmp;
        }
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        // Initial attempt to find references in scene
        FindReferencesInCurrentScene();
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

        // Determine if this is orbit-layer pathfinding
        bool isOrbit = unit != null && unit.currentLayer == TileLayer.Orbit;
        int orbitCost = BiomeHelper.DefaultOrbitMovementCost;
        if (isOrbit)
        {
            var cu = unit as CombatUnit;
            if (cu != null && cu.data != null)
                orbitCost = cu.data.orbitMovementCost;
        }

        PathNode startNode = new PathNode(startIndex);
        PathNode endNode = new PathNode(endIndex);

        SortedSet<PathNode> openSet = new SortedSet<PathNode> { startNode };
        HashSet<int> closedSet = new HashSet<int>();

        Dictionary<int, PathNode> allNodes = new Dictionary<int, PathNode>
        {
            [startIndex] = startNode
        };

        startNode.gCost = 0;
        // Use planar centers for heuristic on flat map
        startNode.hCost = Vector3.Distance(
            ts.GetTileCenterFlat(startIndex),
            ts.GetTileCenterFlat(endIndex));


        while (openSet.Count > 0)
        {
            PathNode currentNode = openSet.Min;

            if (currentNode.tileIndex == endIndex)
            {
                return RetracePath(startNode, currentNode);
            }

            openSet.Remove(currentNode);
            closedSet.Add(currentNode.tileIndex);

            foreach (int neighborIndex in ts.GetNeighbors(currentNode.tileIndex))
            {
                if (closedSet.Contains(neighborIndex))
                {
                    continue;
                }

                var neighborTileData = ts.GetTileData(neighborIndex);
                if (neighborTileData == null) continue; // Skip invalid tiles

                int moveCost;
                if (isOrbit)
                {
                    // Orbit: flat cost, no terrain restrictions (all tiles traversable)
                    moveCost = orbitCost;
                }
                else
                {
                    moveCost = BiomeHelper.GetMovementCost(neighborTileData, null);
                    if (moveCost >= 99) continue; // Unpassable
                }

                float tentativeGCost = currentNode.gCost + moveCost;

                if (!allNodes.TryGetValue(neighborIndex, out PathNode neighborNode) || tentativeGCost < neighborNode.gCost)
                {
                    if (neighborNode == null)
                    {
                        neighborNode = new PathNode(neighborIndex);
                        allNodes[neighborIndex] = neighborNode;
                    }
                    
                    neighborNode.parent = currentNode;
                    neighborNode.gCost = tentativeGCost;
                    // Heuristic based on planar distance between tile centers
                    neighborNode.hCost = Vector3.Distance(
                        ts.GetTileCenterFlat(neighborIndex),
                        ts.GetTileCenterFlat(endIndex));
                    
                    if (openSet.Contains(neighborNode))
                        openSet.Remove(neighborNode);
                    openSet.Add(neighborNode);
                }
            }
        }

        return null; // No path found
    }

    private List<int> RetracePath(PathNode startNode, PathNode endNode)
    {
        List<int> path = new List<int>();
        PathNode currentNode = endNode;
        while (currentNode != null && currentNode.tileIndex != startNode.tileIndex)
        {
            path.Add(currentNode.tileIndex);
            currentNode = currentNode.parent;
        }
        path.Reverse();
        return path;
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
            
            // Get movement cost for this step (orbit uses flat cost, surface uses terrain cost)
            var tileData = ts != null ? ts.GetTileData(targetTileIndex) : null;
            int movementCost;
            if (unit.currentLayer == TileLayer.Orbit)
            {
                var orbitCu = combatUnit;
                movementCost = (orbitCu != null && orbitCu.data != null) ? orbitCu.data.orbitMovementCost : BiomeHelper.DefaultOrbitMovementCost;
            }
            else
            {
                movementCost = tileData != null ? BiomeHelper.GetMovementCost(tileData, workerUnit) : 1;
            }
            
            // Deduct movement points for workers (they still use turn-based movement)
            if (workerUnit != null)
            {
                // Check if worker can afford this move
                if (workerUnit.currentMovePoints < movementCost)
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
        Debug.Log($"[UnitMoveCtrl] {unit.gameObject.name} COMPLETED full path ({path.Count} steps)");
        unit.UpdateWalkingState(false);
        
        // Fire movement completed event
        GameEventManager.Instance.RaiseMovementCompletedEvent((MonoBehaviour)unit, path[0], path[path.Count - 1], path.Count);

        // Fog of War: movement completed; refresh vision for unit owner.
        if (UnitVisionManager.Instance != null && unit.owner != null)
        {
            UnitVisionManager.Instance.UpdateVisionForCiv(UnitVisionManager.GetCivIndex(unit.owner));
        }
    }

    /// <summary>
    /// Properly positions and orients a unit on the flat map surface
    /// </summary>
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
