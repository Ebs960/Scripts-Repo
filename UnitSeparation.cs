using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Handles separation between units to prevent overlap
/// Uses collider-based overlap detection (Physics.OverlapSphere) instead of distance calculations
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
    public float maxSeparationForce = 2.5f;
    
    [Header("Enemy Separation")]
    [Tooltip("Allow enemies to get closer (multiplier for enemy separation distance)")]
    public float enemySeparationMultiplier = 0.5f;
    
    [Tooltip("Weaker separation force for enemies (allows melee combat)")]
    public float enemySeparationStrength = 0.5f;
    
    [Header("Debug")]
    [Tooltip("Show separation forces in scene view")]
    public bool showDebugGizmos = false;
    
    private CombatUnit combatUnit;
    private FormationUnit formationUnit;
    private BattleTestSimple battleTestInstance;
    private Collider unitCollider; // Cache our own collider for exclusion
    
    // Layer mask for unit colliders (optimization - only check Units layer if it exists)
    private int unitsLayerMask = -1; // Default to all layers if Units layer doesn't exist
    
    void Awake()
    {
        combatUnit = GetComponent<CombatUnit>();
        formationUnit = GetComponentInParent<FormationUnit>();
        unitCollider = GetComponent<Collider>();
        
        // Cache BattleTestSimple instance
        if (BattleTestSimple.Instance != null)
        {
            battleTestInstance = BattleTestSimple.Instance;
        }
        
        // Try to get Units layer mask for optimization (only check Units layer if it exists)
        int unitsLayer = LayerMask.NameToLayer("Units");
        if (unitsLayer != -1)
        {
            unitsLayerMask = 1 << unitsLayer; // Only check Units layer
        }
        else
        {
            unitsLayerMask = -1; // Check all layers if Units layer doesn't exist
        }
    }
    
    /// <summary>
    /// Apply separation to a desired position to prevent overlap with nearby units
    /// Returns adjusted position with smooth interpolation
    /// DISABLED during combat to allow units to engage in melee
    /// </summary>
    public Vector3 ApplySeparation(Vector3 desiredPosition)
    {
        if (combatUnit == null || formationUnit == null) return desiredPosition;
        
        // DISABLE separation during combat - units need to engage in melee
        // Separation is only for preventing visual overlap when not in combat
        if (formationUnit.isInCombat)
        {
            return desiredPosition;
        }
        
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
    /// Calculate separation force from nearby units using collider overlap detection
    /// Uses Physics.OverlapSphere() to detect overlapping colliders at the desired position
    /// This is more Unity-native than manual distance calculations
    /// </summary>
    Vector3 CalculateSeparationForce(Vector3 currentPosition)
    {
        Vector3 totalForce = Vector3.zero;
        Vector3 unitPos = currentPosition;
        
        // Use Physics.OverlapSphere to detect colliders within minimum separation distance
        // This finds all colliders that would overlap if we moved to the desired position
        Collider[] overlappingColliders = Physics.OverlapSphere(unitPos, minSeparation, unitsLayerMask);
        
        foreach (var col in overlappingColliders)
        {
            // Skip our own collider
            if (col == null || col == unitCollider || col.gameObject == gameObject) continue;
            
            // Only process colliders that belong to units (have CombatUnit component)
            var nearbyCombatUnit = col.GetComponent<CombatUnit>();
            if (nearbyCombatUnit == null) continue; // Skip non-unit colliders (terrain, etc.)
            
            GameObject nearbyUnit = col.gameObject;
            if (!nearbyUnit.activeInHierarchy) continue;
            
            // Determine if this is a friendly or enemy unit
            var nearbyFormation = nearbyUnit.GetComponentInParent<FormationUnit>();
            bool isFriendly = (nearbyFormation != null && formationUnit != null && 
                              nearbyFormation == formationUnit);
            bool isEnemy = (nearbyFormation != null && formationUnit != null && 
                           nearbyFormation.isAttacker != formationUnit.isAttacker &&
                           formationUnit.isInCombat);
            
            // Get the closest point on the other unit's collider to our position
            Vector3 closestPointOnOther = col.ClosestPoint(unitPos);
            Vector3 nearbyPos = closestPointOnOther;
            
            // If closest point is inside our desired position, use the collider's center as fallback
            if (Vector3.Distance(nearbyPos, unitPos) < 0.01f)
            {
                nearbyPos = col.bounds.center;
            }
            
            if (isFriendly)
            {
                // Friendly unit - use normal separation
                Vector3 force = CalculateSeparationFromUnit(nearbyPos, unitPos, minSeparation, separationStrength);
                totalForce += force;
            }
            else if (isEnemy)
            {
                // Enemy unit - use weaker separation (allows melee combat)
                // Check overlap with smaller radius for enemies
                float enemySeparationDist = minSeparation * enemySeparationMultiplier;
                float distanceToEnemy = Vector3.Distance(unitPos, nearbyPos);
                
                // Only apply enemy separation if within enemy separation distance
                if (distanceToEnemy < enemySeparationDist)
                {
                    Vector3 force = CalculateSeparationFromUnit(nearbyPos, unitPos, enemySeparationDist, enemySeparationStrength);
                    totalForce += force;
                }
            }
        }
        
        return totalForce;
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

