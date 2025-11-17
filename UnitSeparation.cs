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
    public float separationStrength = 2f;
    
    [Tooltip("Separation speed (how fast units separate)")]
    public float separationSpeed = 5f;
    
    [Tooltip("Maximum separation force per frame (prevents sudden jumps)")]
    public float maxSeparationForce = 0.5f;
    
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
    
    void Awake()
    {
        combatUnit = GetComponent<CombatUnit>();
        formationUnit = GetComponentInParent<FormationUnit>();
        
        // Cache BattleTestSimple instance
        if (BattleTestSimple.Instance != null)
        {
            battleTestInstance = BattleTestSimple.Instance;
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
    /// </summary>
    Vector3 CalculateSeparationForce(Vector3 currentPosition)
    {
        Vector3 totalForce = Vector3.zero;
        Vector3 unitPos = transform.position;
        
        // Check friendly units in same formation
        if (formationUnit != null && formationUnit.soldiers != null)
        {
            foreach (var friendlySoldier in formationUnit.soldiers)
            {
                if (friendlySoldier == null || friendlySoldier == gameObject) continue;
                
                // Check if soldier is still active (not destroyed)
                if (!friendlySoldier.activeInHierarchy) continue;
                
                Vector3 force = CalculateSeparationFromUnit(friendlySoldier.transform.position, unitPos, minSeparation, separationStrength);
                totalForce += force;
            }
        }
        
        // Check enemy units (only when in combat)
        if (formationUnit != null && formationUnit.isInCombat && battleTestInstance != null)
        {
            foreach (var enemyFormation in battleTestInstance.allFormations)
            {
                if (enemyFormation == null || enemyFormation == formationUnit) continue;
                if (enemyFormation.isAttacker == formationUnit.isAttacker) continue; // Same team
                if (enemyFormation.soldiers == null) continue;
                
                foreach (var enemySoldier in enemyFormation.soldiers)
                {
                    if (enemySoldier == null) continue;
                    
                    // Check if enemy soldier is still active (not destroyed)
                    if (!enemySoldier.activeInHierarchy) continue;
                    
                    float enemySeparationDist = minSeparation * enemySeparationMultiplier;
                    Vector3 force = CalculateSeparationFromUnit(enemySoldier.transform.position, unitPos, enemySeparationDist, enemySeparationStrength);
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
    /// Ground a position using raycast (reuse BattleTestSimple's grounding logic)
    /// </summary>
    Vector3 GroundPosition(Vector3 position)
    {
        // Simple grounding - raycast down
        RaycastHit hit;
        if (Physics.Raycast(position + Vector3.up * 10f, Vector3.down, out hit, 20f))
        {
            return hit.point;
        }
        
        return position;
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

