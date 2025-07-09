using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Den.Tools;
using System.Linq;

public class UnitMovementController : MonoBehaviour
{
    public static UnitMovementController Instance { get; private set; }
    private IcoSphereGrid grid;
    private PlanetGenerator planet;
    private MoonGenerator moon;
    
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 10f;
    [SerializeField] private float rotationSpeed = 10f;
    [SerializeField] private AnimationCurve movementCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private class PathNode
    {
        public int tileIndex;
        public float gCost; // cost from start
        public float hCost; // heuristic cost to end
        public float FCost => gCost + hCost;
        public PathNode parent;

        public PathNode(int tileIndex) { this.tileIndex = tileIndex; }
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
    public void SetReferences(IcoSphereGrid icoGrid, PlanetGenerator planetGen, MoonGenerator moonGen)
    {
        grid = icoGrid;
        planet = planetGen;
        moon = moonGen;
        
        Debug.Log($"[UnitMovementController] References set from GameManager - Grid: {grid != null}, Planet: {planet != null}, Moon: {moon != null}");
    }
    
    /// <summary>
    /// Find all necessary references in the current scene (fallback method)
    /// </summary>
    public void FindReferencesInCurrentScene()
    {
        // Find IcoSphereGrid directly in the current scene via PlanetGenerator
        if (grid == null)
        {
            var pg = FindAnyObjectByType<PlanetGenerator>();
            grid = pg != null ? pg.Grid : null;
        }
        
        // Find PlanetGenerator directly in the current scene
        if (planet == null)
        {
            planet = FindAnyObjectByType<PlanetGenerator>();
        }
        
        // Find MoonGenerator directly in the current scene
        if (moon == null)
        {
            moon = FindAnyObjectByType<MoonGenerator>();
        }
        
        Debug.Log($"[UnitMovementController] Found references in scene - Grid: {grid != null}, Planet: {planet != null}, Moon: {moon != null}");
        
        // If we still don't have grid but we have planet, try to get grid from planet
        if (grid == null && planet != null)
        {
            grid = planet.Grid;
            Debug.Log($"[UnitMovementController] Got IcoSphereGrid from PlanetGenerator: {grid != null}");
        }
    }

    /// <summary>
    /// Finds a path (list of tile indices) from start to end using A*, considering each tile's movement cost.
    /// Handles both planet and moon tiles.
    /// Returns null if unreachable.
    /// </summary>
    public List<int> FindPath(int startIndex, int endIndex)
    {
        var (startTile, isStartMoon) = TileDataHelper.Instance.GetTileData(startIndex);
        var (endTile, isEndMoon) = TileDataHelper.Instance.GetTileData(endIndex);

        if (startTile == null || endTile == null || isStartMoon != isEndMoon)
        {
            Debug.LogWarning($"[UnitMovementController] Pathfinding error: Tiles are on different celestial bodies or invalid. Start: {startIndex}, End: {endIndex}");
            return null;
        }

        IcoSphereGrid currentGrid = grid;
        if (currentGrid == null)
        {
            Debug.LogError("[UnitMovementController] IcoSphereGrid reference is null for pathfinding!");
            return null;
        }

        PathNode startNode = new PathNode(startIndex);
        PathNode endNode = new PathNode(endIndex);

        List<PathNode> openList = new List<PathNode> { startNode };
        HashSet<int> closedSet = new HashSet<int>();

        Dictionary<int, PathNode> allNodes = new Dictionary<int, PathNode>
        {
            [startIndex] = startNode
        };

        startNode.gCost = 0;
        startNode.hCost = Vector3.Distance(
            TileDataHelper.Instance.GetTileCenter(startIndex),
            TileDataHelper.Instance.GetTileCenter(endIndex));


        while (openList.Count > 0)
        {
            PathNode currentNode = openList[0];
            for (int i = 1; i < openList.Count; i++)
            {
                if (openList[i].FCost < currentNode.FCost || openList[i].FCost == currentNode.FCost && openList[i].hCost < currentNode.hCost)
                {
                    currentNode = openList[i];
                }
            }

            if (currentNode.tileIndex == endIndex)
            {
                return RetracePath(startNode, currentNode);
            }

            openList.Remove(currentNode);
            closedSet.Add(currentNode.tileIndex);

            foreach (int neighborIndex in TileDataHelper.Instance.GetTileNeighbors(currentNode.tileIndex))
            {
                if (closedSet.Contains(neighborIndex))
                {
                    continue;
                }

                var (neighborTileData, _) = TileDataHelper.Instance.GetTileData(neighborIndex);
                if (neighborTileData == null) continue; // Skip invalid tiles

                int moveCost = BiomeHelper.GetMovementCost(neighborTileData.biome);
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
                        TileDataHelper.Instance.GetTileCenter(neighborIndex),
                        TileDataHelper.Instance.GetTileCenter(endIndex));
                    
                    if (!openList.Contains(neighborNode))
                    {
                        openList.Add(neighborNode);
                    }
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
        if (combatUnit != null)
            combatUnit.isMoving = true;
        else
            workerUnit.UpdateWalkingState(true);
        
        // Move along each tile in path
        for (int i = 0; i < path.Count; i++)
        {
            int targetTileIndex = path[i];
            
            // Get movement cost for this step
            int movementCost = 1; // Default
            var (tileData, isMoon) = TileDataHelper.Instance.GetTileData(targetTileIndex);
            if (tileData != null)
                movementCost = BiomeHelper.GetMovementCost(tileData.biome);
            
            // Deduct movement points
            if (combatUnit != null)
                combatUnit.DeductMovementPoints(movementCost);
            else
                workerUnit.DeductMovementPoints(movementCost);
            
            // Calculate positions including extrusion
            Vector3 startPosition = unitTransform.position;
            IcoSphereGrid currentGrid = grid;
            Vector3 endPosition = TileDataHelper.Instance.GetTileCenter(targetTileIndex);
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
                TileDataHelper.Instance.SetTileOccupant(targetTileIndex, combatUnit.gameObject);
            }
            else
            {
                workerUnit.currentTileIndex = targetTileIndex;
                TileDataHelper.Instance.SetTileOccupant(targetTileIndex, workerUnit.gameObject);
            }
            
            // Fire movement event for each step
            GameEventManager.Instance.RaiseUnitMovedEvent(unit, previousTileIndex, targetTileIndex, movementCost);
            previousTileIndex = targetTileIndex;
            
            // Small delay between steps
            yield return new WaitForSeconds(0.1f);
        }
        
        // Set unit back to idle state
        if (combatUnit != null)
            combatUnit.isMoving = false;
        else
            workerUnit.UpdateWalkingState(false);
        
        // Fire movement completed event
        GameEventManager.Instance.RaiseMovementCompletedEvent(unit, path[0], path[path.Count - 1], path.Count);
    }

    /// <summary>
    /// Properly positions and orients a unit on the planet surface using surface normal
    /// </summary>
    private void PositionUnitOnSurface(Transform unitTransform, int tileIndex)
    {
        if (grid == null) return;
        
        var (tileData, isMoon) = TileDataHelper.Instance.GetTileData(tileIndex);
        if (tileData == null) return;

        IcoSphereGrid currentGrid = grid;

        // Get the extruded center of the tile. This is the new correct surface position.
        Vector3 surfacePosition = TileDataHelper.Instance.GetTileCenter(tileIndex);
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
    }} 