using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Den.Tools;
using System.Linq;

public class UnitMovementController : MonoBehaviour
{
    public static UnitMovementController Instance { get; private set; }
    private SphericalHexGrid grid;
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
    public void SetReferences(SphericalHexGrid icoGrid, PlanetGenerator planetGen)
    {
        grid = icoGrid;
        planet = planetGen;
        
        Debug.Log($"[UnitMovementController] References set from GameManager - Grid: {grid != null}, Planet: {planet != null}");
    }
    
    /// <summary>
    /// Find all necessary references in the current scene (fallback method)
    /// </summary>
    public void FindReferencesInCurrentScene()
    {
        // Find SphericalHexGrid directly in the current scene via PlanetGenerator
        // Use GameManager API for multi-planet support
        if (planet == null)
        {
            planet = GameManager.Instance?.GetCurrentPlanetGenerator();
        }
        
        if (grid == null && planet != null)
        {
            grid = planet.Grid;
        }
        
        Debug.Log($"[UnitMovementController] Found references in scene - Grid: {grid != null}, Planet: {planet != null}");
        
        // If we still don't have grid but we have planet, try to get grid from planet
        if (grid == null && planet != null)
        {
            grid = planet.Grid;
            Debug.Log($"[UnitMovementController] Got SphericalHexGrid from PlanetGenerator: {grid != null}");
        }
    }

    /// <summary>
    /// Finds a path (list of tile indices) from start to end using A*, considering each tile's movement cost.
    /// Handles both planet and moon tiles.
    /// Returns null if unreachable.
    /// </summary>
    public List<int> FindPath(int startIndex, int endIndex)
    {
        var startTile = TileSystem.Instance != null ? TileSystem.Instance.GetTileData(startIndex) : null;
        var endTile = TileSystem.Instance != null ? TileSystem.Instance.GetTileData(endIndex) : null;

        if (startTile == null || endTile == null)
        {
            Debug.LogWarning($"[UnitMovementController] Pathfinding error: Tiles invalid. Start: {startIndex}, End: {endIndex}");
            return null;
        }

        SphericalHexGrid currentGrid = grid;
        if (currentGrid == null)
        {
            Debug.LogError("[UnitMovementController] SphericalHexGrid reference is null for pathfinding!");
            return null;
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
        startNode.hCost = Vector3.Distance(
            TileSystem.Instance.GetTileSurfacePosition(startIndex, unitOffset: 0f),
            TileSystem.Instance.GetTileSurfacePosition(endIndex, unitOffset: 0f));


        while (openSet.Count > 0)
        {
            PathNode currentNode = openSet.Min;

            if (currentNode.tileIndex == endIndex)
            {
                return RetracePath(startNode, currentNode);
            }

            openSet.Remove(currentNode);
            closedSet.Add(currentNode.tileIndex);

            foreach (int neighborIndex in TileSystem.Instance.GetNeighbors(currentNode.tileIndex))
            {
                if (closedSet.Contains(neighborIndex))
                {
                    continue;
                }

                var neighborTileData = TileSystem.Instance.GetTileData(neighborIndex);
                if (neighborTileData == null) continue; // Skip invalid tiles

                int moveCost = BiomeHelper.GetMovementCost(neighborTileData, null);
                if (moveCost >= 99) continue; // Unpassable

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
                    neighborNode.hCost = Vector3.Distance(
                        TileSystem.Instance.GetTileSurfacePosition(neighborIndex, unitOffset: 0f),
                        TileSystem.Instance.GetTileSurfacePosition(endIndex, unitOffset: 0f));
                    
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
    /// Moves unit along the given path with proper spherical orientation.
    /// </summary>
    public IEnumerator MoveAlongPath(MonoBehaviour unit, List<int> path)
    {
        if (unit == null || path == null || path.Count == 0 || grid == null)
            yield break;
            
        // Get required unit properties via reflection/interface or direct type checking
        CombatUnit combatUnit = unit as CombatUnit;
        WorkerUnit workerUnit = unit as WorkerUnit;
        
        if (combatUnit == null && workerUnit == null)
        {
            Debug.LogError("Unit must be either CombatUnit or WorkerUnit");
            yield break;
        }
        
        int currentTileIndex = combatUnit != null ? combatUnit.currentTileIndex : workerUnit.currentTileIndex;
        Transform unitTransform = unit.transform;
        
        // Track the previous tile for movement cost calculation
        int previousTileIndex = currentTileIndex;
        
        // Set unit to moving state
        // Check if unit is in battle - if so, don't override battle movement
        if (combatUnit != null)
        {
            // Only set movement if not in battle (battle movement takes precedence)
            if (!combatUnit.IsInBattle)
            {
                combatUnit.isMoving = true; // This will automatically update IsWalking animator parameter
            }
        }
        else
        {
            workerUnit.UpdateWalkingState(true);
        }
        
        // Move along each tile in path
        for (int i = 0; i < path.Count; i++)
        {
            int targetTileIndex = path[i];
            
            // Get movement cost for this step (tile-aware: improvements may alter cost)
            var tileData = TileSystem.Instance.GetTileData(targetTileIndex);
            // Movement points removed - movement speed is now fatigue-based
            // Calculate movement cost for event (not used for movement points, but needed for event signature)
            int movementCost = tileData != null ? BiomeHelper.GetMovementCost(tileData, null) : 1;
            
            // Calculate positions including extrusion
            Vector3 startPosition = unitTransform.position;
            SphericalHexGrid currentGrid = grid;
            Vector3 endPosition = TileSystem.Instance.GetTileSurfacePosition(targetTileIndex, 0f);
            Vector3 planetCenter = planet != null ? planet.transform.position : Vector3.zero;

            // Get normals and radii for spherical interpolation
            Vector3 startNormal = (startPosition - planetCenter).normalized;
            Vector3 endNormal = (endPosition - planetCenter).normalized;
            float startRadius = Vector3.Distance(startPosition, planetCenter);
            float endRadius = Vector3.Distance(endPosition, planetCenter);

            float journeyLength = Vector3.Distance(startPosition, endPosition);
            if (journeyLength < 0.001f) continue;

            float startTime = Time.time;
            float journeyDuration = journeyLength / moveSpeed;
            if (journeyDuration <= 0) journeyDuration = 0.01f;

            while (Time.time - startTime < journeyDuration)
            {
                float timeProgress = (Time.time - startTime) / journeyDuration;
                float curveProgress = movementCurve.Evaluate(Mathf.Clamp01(timeProgress));
                
                // Interpolate normal and radius separately for smooth movement over curved, extruded surface
                Vector3 currentNormal = Vector3.Slerp(startNormal, endNormal, curveProgress);
                float currentRadius = Mathf.Lerp(startRadius, endRadius, curveProgress);

                // Calculate new position
                unitTransform.position = planetCenter + currentNormal * currentRadius;
                
                // --- NEW ROTATION LOGIC ---
                // The surface normal is the "up" direction for the unit
                Vector3 surfaceNormal = currentNormal;
                
                // Calculate the direction of the current movement segment
                Vector3 movementDirection = (endPosition - startPosition).normalized;

                // Project the movement direction onto the current tangent plane to get the "forward" vector
                Vector3 forward = Vector3.ProjectOnPlane(movementDirection, surfaceNormal).normalized;
                
                // Apply smooth rotation, only if the forward vector is valid
                if (forward.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(forward, surfaceNormal);
                    unitTransform.rotation = Quaternion.Slerp(unitTransform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
                }
                
                yield return null;
            }
            
            // Snap to final position and orientation
            PositionUnitOnSurface(unitTransform, targetTileIndex);
            
            // Update current tile and occupancy
            if (combatUnit != null)
            {
                combatUnit.currentTileIndex = targetTileIndex;
                TileSystem.Instance.SetTileOccupant(targetTileIndex, combatUnit.gameObject);
                // Check for traps on arrival
                ImprovementManager.Instance?.NotifyUnitEnteredTile(targetTileIndex, combatUnit);

                // If unit was trapped (immobilized) or killed by a trap, stop further movement this path
                if (combatUnit.currentHealth <= 0 || combatUnit.IsTrapped)
                {
                    // Fire movement completed event up to this step and exit early
                    GameEventManager.Instance.RaiseMovementCompletedEvent(unit, path[0], targetTileIndex, i + 1);
                    combatUnit.isMoving = false;
                    yield break;
                }
            }
            else
            {
                workerUnit.currentTileIndex = targetTileIndex;
                TileSystem.Instance.SetTileOccupant(targetTileIndex, workerUnit.gameObject);
                // Check for traps on arrival
                ImprovementManager.Instance?.NotifyUnitEnteredTile(targetTileIndex, workerUnit);

                // If worker was trapped (immobilized) or killed by a trap, stop further movement
                if (workerUnit.currentHealth <= 0 || workerUnit.IsTrapped)
                {
                    GameEventManager.Instance.RaiseMovementCompletedEvent(unit, path[0], targetTileIndex, i + 1);
                    workerUnit.UpdateWalkingState(false);
                    yield break;
                }
            }
            
            // Fire movement event for each step
            GameEventManager.Instance.RaiseUnitMovedEvent(unit, previousTileIndex, targetTileIndex, movementCost);
            previousTileIndex = targetTileIndex;
            
            // Small delay between steps
            yield return new WaitForSeconds(0.1f);
        }
        
        // Set unit back to idle state
        // Check if unit is in battle - if so, don't override battle movement
        if (combatUnit != null)
        {
            // Only set movement if not in battle (battle movement takes precedence)
            if (!combatUnit.IsInBattle)
            {
                combatUnit.isMoving = false; // This will automatically update IsWalking animator parameter
            }
        }
        else
        {
            workerUnit.UpdateWalkingState(false);
        }
        
        // Fire movement completed event
        GameEventManager.Instance.RaiseMovementCompletedEvent(unit, path[0], path[path.Count - 1], path.Count);
    }

    /// <summary>
    /// Properly positions and orients a unit on the planet surface using surface normal
    /// </summary>
    private void PositionUnitOnSurface(Transform unitTransform, int tileIndex)
    {
        if (grid == null) return;
        
    var tileData = TileSystem.Instance != null ? TileSystem.Instance.GetTileData(tileIndex) : null;
        if (tileData == null) return;

        SphericalHexGrid currentGrid = grid;

        // Get the extruded center of the tile. This is the new correct surface position.
    Vector3 surfacePosition = TileSystem.Instance.GetTileSurfacePosition(tileIndex, unitOffset: 0f);
        unitTransform.position = surfacePosition;

        Vector3 planetCenter = planet != null ? planet.transform.position : Vector3.zero;
        Vector3 surfaceNormal = (surfacePosition - planetCenter).normalized;

        // Orient unit to stand upright on the surface
        Vector3 planetUp = planet != null ? planet.transform.up : Vector3.up;
        Vector3 right = Vector3.Cross(planetUp, surfaceNormal);
        
        // Handle poles where right vector might be zero
        if (right.sqrMagnitude < 0.01f)
        {
            right = Vector3.Cross(Vector3.forward, surfaceNormal);
            if (right.sqrMagnitude < 0.01f)
            {
                right = Vector3.Cross(Vector3.right, surfaceNormal);
            }
        }
        right.Normalize();
        
        Vector3 forward = Vector3.Cross(right, surfaceNormal).normalized;
        
        // Set rotation so unit stands upright on surface
        unitTransform.rotation = Quaternion.LookRotation(forward, surfaceNormal);
    }
} 