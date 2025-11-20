using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Handles separation between units to prevent overlap
/// Uses smooth interpolation to avoid teleportation
/// </summary>
public class UnitSeparation : MonoBehaviour
{
    [Header("Separation Settings")]
    [Tooltip("Minimum distance between units to prevent overlap")]
    public float minSeparation = 1.2f;
    
    [Tooltip("Separation strength multiplier (higher = stronger push)")]
    public float separationStrength = 1f;
    
    [Tooltip("Separation speed (how fast units separate)")]
    public float separationSpeed = 2f;
    
    [Tooltip("Maximum separation force per frame (prevents sudden jumps)")]
    public float maxSeparationForce = 4.5f;
    
    [Header("Enemy Separation")]
    [Tooltip("Allow enemies to get closer (multiplier for enemy separation distance)")]
    public float enemySeparationMultiplier = 0.8f;
    
    [Tooltip("Weaker separation force for enemies (allows melee combat)")]
    public float enemySeparationStrength = 1.5f;
    
    [Header("Debug")]
    [Tooltip("Show separation forces in scene view")]
    public bool showDebugGizmos = false;
    
    private CombatUnit combatUnit;
    private FormationUnit formationUnit;
    private BattleTestSimple battleTestInstance;
    
    // IMPROVED: Spatial partitioning for performance
    private static SpatialGrid spatialGrid;
    private static float lastSpatialGridUpdate = 0f;
    private const float SPATIAL_GRID_UPDATE_INTERVAL = 0.1f; // Update grid every 0.1 seconds
    private const float SPATIAL_GRID_CELL_SIZE = 5f; // 5 unit cells
    private const float SPATIAL_GRID_QUERY_RADIUS = 10f; // Query radius for separation checks
    
    void Awake()
    {
        combatUnit = GetComponent<CombatUnit>();
        formationUnit = GetComponentInParent<FormationUnit>();
        
        // Cache BattleTestSimple instance
        if (BattleTestSimple.Instance != null)
        {
            battleTestInstance = BattleTestSimple.Instance;
        }
        
        // Initialize spatial grid if needed
        if (spatialGrid == null)
        {
            spatialGrid = new SpatialGrid(SPATIAL_GRID_CELL_SIZE, SPATIAL_GRID_QUERY_RADIUS);
        }
    }
    
    /// <summary>
    /// Apply separation to a desired position to prevent overlap with nearby units
    /// Returns adjusted position with smooth interpolation
    /// </summary>
    public Vector3 ApplySeparation(Vector3 desiredPosition)
    {
        if (combatUnit == null || formationUnit == null) return desiredPosition;
        
        Vector3 separationForce = CalculateSeparationForce(desiredPosition);
        
        // Apply separation force with smooth interpolation
        if (separationForce.magnitude > 0.01f)
        {
            // Clamp separation force to prevent sudden jumps/teleportation
            if (separationForce.magnitude > maxSeparationForce)
            {
                separationForce = separationForce.normalized * maxSeparationForce;
            }
            
            // Apply separation smoothly over time
            Vector3 adjustedPos = desiredPosition + separationForce * Time.deltaTime * separationSpeed;
            
            // Ground the position
            adjustedPos = GroundPosition(adjustedPos);
            
            return adjustedPos;
        }
        
        return desiredPosition;
    }
    
    /// <summary>
    /// Calculate separation force from nearby units
    /// IMPROVED: Uses spatial partitioning for performance - only checks nearby units instead of all units
    /// Uses the desired position (currentPosition parameter) instead of transform.position
    /// This ensures separation is calculated from where the unit wants to go, not where it currently is
    /// </summary>
    Vector3 CalculateSeparationForce(Vector3 currentPosition)
    {
        Vector3 totalForce = Vector3.zero;
        // FIXED: Use currentPosition parameter instead of transform.position
        // This ensures separation is calculated from the desired position, preventing units from being pushed away from their destination
        Vector3 unitPos = currentPosition;
        
        // IMPROVED: Update spatial grid periodically
        if (Time.time - lastSpatialGridUpdate > SPATIAL_GRID_UPDATE_INTERVAL)
        {
            UpdateSpatialGrid();
            lastSpatialGridUpdate = Time.time;
        }
        
        // IMPROVED: Use spatial grid to get only nearby units (much faster than checking all units)
        List<GameObject> nearbyUnits = spatialGrid.GetNearbyUnits(unitPos, SPATIAL_GRID_QUERY_RADIUS);
        
        foreach (var nearbyUnit in nearbyUnits)
        {
            if (nearbyUnit == null || nearbyUnit == gameObject || !nearbyUnit.activeInHierarchy) continue;
            
            // Determine if this is a friendly or enemy unit
            var nearbyFormation = nearbyUnit.GetComponentInParent<FormationUnit>();
            bool isFriendly = (nearbyFormation != null && formationUnit != null && 
                              nearbyFormation == formationUnit);
            bool isEnemy = (nearbyFormation != null && formationUnit != null && 
                           nearbyFormation.isAttacker != formationUnit.isAttacker &&
                           formationUnit.isInCombat);
            
            if (isFriendly)
            {
                // Friendly unit - use normal separation
                Vector3 nearbyPos = nearbyUnit.transform.position;
                Vector3 force = CalculateSeparationFromUnit(nearbyPos, unitPos, minSeparation, separationStrength);
                totalForce += force;
            }
            else if (isEnemy)
            {
                // Enemy unit - use weaker separation (allows melee combat)
                float enemySeparationDist = minSeparation * enemySeparationMultiplier;
                Vector3 enemyPos = nearbyUnit.transform.position;
                Vector3 force = CalculateSeparationFromUnit(enemyPos, unitPos, enemySeparationDist, enemySeparationStrength);
                totalForce += force;
            }
        }
        
        return totalForce;
    }
    
    /// <summary>
    /// Update the spatial grid with all active units
    /// </summary>
    void UpdateSpatialGrid()
    {
        if (spatialGrid == null || battleTestInstance == null) return;
        
        spatialGrid.Clear();
        
        // Add all units from all formations to the spatial grid
        if (battleTestInstance.allFormations != null)
        {
            foreach (var formation in battleTestInstance.allFormations)
            {
                if (formation == null || formation.soldiers == null) continue;
                
                foreach (var soldier in formation.soldiers)
                {
                    if (soldier != null && soldier.activeInHierarchy)
                    {
                        spatialGrid.Add(soldier, soldier.transform.position);
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Calculate separation force from a single nearby unit
    /// </summary>
    Vector3 CalculateSeparationFromUnit(Vector3 otherPos, Vector3 myPos, float minDist, float strength)
    {
        Vector3 diff = myPos - otherPos;
        diff.y = 0; // Only consider horizontal distance
        float distance = diff.magnitude;
        
        // If too close, apply separation force
        if (distance < minDist && distance > 0.01f)
        {
            float overlap = minDist - distance;
            Vector3 separationDir = diff.normalized;
            
            // Force strength increases as units get closer (inverse relationship)
            float forceStrength = (overlap / minDist) * strength;
            
            return separationDir * forceStrength;
        }
        
        return Vector3.zero;
    }
    
    /// <summary>
    /// Ground a position using raycast - unified with FormationUnit.Ground() method
    /// Uses same raycast origin (100 units above) and distance (1000 units) for consistency
    /// </summary>
    Vector3 GroundPosition(Vector3 position)
    {
        // Use same grounding method as FormationUnit.Ground() for consistency
        // This prevents Y-axis jumps when different methods are used
        LayerMask layers = battleTestInstance != null ? battleTestInstance.battlefieldLayers : ~0;
        Vector3 origin = position + Vector3.up * 100f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 1000f, layers))
        {
            return hit.point;
        }
        // Fallback: clamp to y=0 if nothing hit (same as FormationUnit.Ground())
        return new Vector3(position.x, 0f, position.z);
    }
    
    void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;
        
        // Draw separation radius
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, minSeparation);
        
        // Draw separation force direction
        Vector3 force = CalculateSeparationForce(transform.position);
        if (force.magnitude > 0.01f)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawRay(transform.position, force.normalized * 2f);
        }
    }
}

