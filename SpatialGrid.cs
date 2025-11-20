using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Spatial partitioning grid for efficient unit queries
/// Divides the world into a grid to limit collision/separation checks to nearby units
/// </summary>
public class SpatialGrid
{
    private Dictionary<Vector2Int, List<GameObject>> grid = new Dictionary<Vector2Int, List<GameObject>>();
    private float cellSize;
    private float maxQueryRadius;
    
    public SpatialGrid(float cellSize, float maxQueryRadius)
    {
        this.cellSize = cellSize;
        this.maxQueryRadius = maxQueryRadius;
    }
    
    /// <summary>
    /// Clear all entries from the grid
    /// </summary>
    public void Clear()
    {
        grid.Clear();
    }
    
    /// <summary>
    /// Add a unit to the spatial grid
    /// </summary>
    public void Add(GameObject unit, Vector3 position)
    {
        Vector2Int cell = WorldToCell(position);
        
        if (!grid.ContainsKey(cell))
        {
            grid[cell] = new List<GameObject>();
        }
        
        if (!grid[cell].Contains(unit))
        {
            grid[cell].Add(unit);
        }
    }
    
    /// <summary>
    /// Get all units within query radius of a position
    /// </summary>
    public List<GameObject> GetNearbyUnits(Vector3 position, float radius)
    {
        List<GameObject> nearbyUnits = new List<GameObject>();
        HashSet<GameObject> addedUnits = new HashSet<GameObject>(); // Prevent duplicates
        
        // Calculate which cells to check based on radius
        int cellsToCheck = Mathf.CeilToInt(radius / cellSize) + 1;
        Vector2Int centerCell = WorldToCell(position);
        
        for (int x = -cellsToCheck; x <= cellsToCheck; x++)
        {
            for (int z = -cellsToCheck; z <= cellsToCheck; z++)
            {
                Vector2Int cell = centerCell + new Vector2Int(x, z);
                
                if (grid.ContainsKey(cell))
                {
                    foreach (var unit in grid[cell])
                    {
                        if (unit != null && unit.activeInHierarchy && !addedUnits.Contains(unit))
                        {
                            // Check actual distance (not just cell distance)
                            float distance = Vector3.Distance(position, unit.transform.position);
                            if (distance <= radius)
                            {
                                nearbyUnits.Add(unit);
                                addedUnits.Add(unit);
                            }
                        }
                    }
                }
            }
        }
        
        return nearbyUnits;
    }
    
    /// <summary>
    /// Convert world position to grid cell coordinates
    /// </summary>
    private Vector2Int WorldToCell(Vector3 worldPos)
    {
        int x = Mathf.FloorToInt(worldPos.x / cellSize);
        int z = Mathf.FloorToInt(worldPos.z / cellSize);
        return new Vector2Int(x, z);
    }
}

