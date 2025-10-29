using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Advanced AI system for real-time battle combat
/// </summary>
public class BattleAI : MonoBehaviour
{
    [Header("AI Settings")]
    [Tooltip("How often the AI makes decisions (in seconds)")]
    public float decisionInterval = 1f;
    [Tooltip("How often the AI updates target selection (in seconds)")]
    public float targetUpdateInterval = 2f;
    [Tooltip("How often the AI checks for retreat conditions (in seconds)")]
    public float retreatCheckInterval = 3f;
    
    [Header("Combat Behavior")]
    [Tooltip("Distance at which unit will try to maintain from enemies")]
    public float preferredEngagementRange = 3f;
    [Tooltip("Distance at which unit will retreat if outnumbered")]
    public float retreatDistance = 8f;
    [Tooltip("Health percentage below which unit will retreat")]
    [Range(0f, 1f)]
    public float retreatHealthThreshold = 0.3f;
    [Tooltip("Morale threshold below which unit will retreat")]
    [Range(0f, 1f)]
    public float retreatMoraleThreshold = 0.2f;
    
    [Header("Tactical Behavior")]
    [Tooltip("How much the AI values flanking enemies")]
    public float flankingBonus = 1.5f;
    [Tooltip("How much the AI values staying in formation")]
    public float formationBonus = 1.2f;
    [Tooltip("How much the AI values high ground")]
    public float highGroundBonus = 1.3f;
    
    private CombatUnit unit;
    private float lastDecisionTime;
    private float lastTargetUpdateTime;
    private float lastRetreatCheckTime;
    private CombatUnit currentTarget;
    private Vector3 lastKnownTargetPosition;
    private List<CombatUnit> nearbyAllies = new List<CombatUnit>();
    private List<CombatUnit> nearbyEnemies = new List<CombatUnit>();
    
    // AI State
    private AIState currentState = AIState.Idle;
    private Vector3 formationCenter;
    private float lastFormationUpdateTime;
    
    // Behavior Tree
    private BehaviorTree behaviorTree;
    
    // Enhanced Target Selection
    private EnhancedTargetSelection enhancedTargetSelection;
    
    public enum AIState
    {
        Idle,           // Not doing anything
        Advancing,      // Moving towards enemies
        Engaging,       // In combat with target
        Flanking,       // Trying to get behind enemy
        Retreating,     // Running away
        Regrouping,     // Moving back to formation
        Defending       // Holding position
    }
    
    void Start()
    {
        unit = GetComponent<CombatUnit>();
        if (unit == null)
        {
            Debug.LogError("[BattleAI] No CombatUnit component found!");
            enabled = false;
            return;
        }
        
        // Initialize AI
        lastDecisionTime = Time.time;
        lastTargetUpdateTime = Time.time;
        lastRetreatCheckTime = Time.time;
        formationCenter = transform.position;
        
        // Initialize behavior tree
        behaviorTree = new BehaviorTree(unit, this);
        
        // Initialize enhanced target selection
        enhancedTargetSelection = GetComponent<EnhancedTargetSelection>();
        if (enhancedTargetSelection == null)
        {
            enhancedTargetSelection = unit.gameObject.AddComponent<EnhancedTargetSelection>();
        }
    }
    
    void Update()
    {
        if (unit == null || unit.battleState == BattleUnitState.Dead) return;
        
        float currentTime = Time.time;
        
        // Update target selection periodically
        if (currentTime - lastTargetUpdateTime >= targetUpdateInterval)
        {
            UpdateTargetSelection();
            lastTargetUpdateTime = currentTime;
        }
        
        // Check for retreat conditions
        if (currentTime - lastRetreatCheckTime >= retreatCheckInterval)
        {
            if (ShouldRetreat())
            {
                ExecuteRetreat();
            }
            lastRetreatCheckTime = currentTime;
        }
        
        // Make AI decisions using behavior tree
        if (currentTime - lastDecisionTime >= decisionInterval)
        {
            MakeAIDecisionWithBehaviorTree();
            lastDecisionTime = currentTime;
        }
        
        // Update formation center
        if (currentTime - lastFormationUpdateTime >= 5f)
        {
            UpdateFormationCenter();
            lastFormationUpdateTime = currentTime;
        }
    }
    
    /// <summary>
    /// Main AI decision making process using behavior tree
    /// </summary>
    private void MakeAIDecisionWithBehaviorTree()
    {
        // Update nearby units
        UpdateNearbyUnits();
        
        // Evaluate behavior tree
        var result = behaviorTree.Evaluate();
        
        // Update AI state based on behavior tree result
        switch (result)
        {
            case BTNode.NodeState.Running:
                // Behavior tree is still executing
                break;
            case BTNode.NodeState.Success:
                // Action completed successfully
                break;
            case BTNode.NodeState.Failure:
                // Action failed, behavior tree will try next option
                break;
        }
    }
    
    /// <summary>
    /// Legacy decision making process (kept for reference)
    /// </summary>
    private void MakeAIDecision()
    {
        // Update nearby units
        UpdateNearbyUnits();
        
        // Check if we should retreat
        if (ShouldRetreat())
        {
            SetAIState(AIState.Retreating);
            ExecuteRetreat();
            return;
        }
        
        // Check if we should regroup
        if (ShouldRegroup())
        {
            SetAIState(AIState.Regrouping);
            ExecuteRegroup();
            return;
        }
        
        // If we have a target, engage it
        if (currentTarget != null && currentTarget.battleState != BattleUnitState.Dead)
        {
            float distanceToTarget = Vector3.Distance(transform.position, currentTarget.transform.position);
            
            if (distanceToTarget <= unit.battleAttackRange)
            {
                // In range - attack
                SetAIState(AIState.Engaging);
                ExecuteAttack();
            }
            else
            {
                // Out of range - advance or flank
                if (ShouldFlank())
                {
                    SetAIState(AIState.Flanking);
                    ExecuteFlank();
                }
                else
                {
                    SetAIState(AIState.Advancing);
                    ExecuteAdvance();
                }
            }
        }
        else
        {
            // No target - look for one or defend
            if (nearbyEnemies.Count > 0)
            {
                SetAIState(AIState.Advancing);
                ExecuteAdvance();
            }
            else
            {
                SetAIState(AIState.Defending);
                ExecuteDefend();
            }
        }
    }
    
    /// <summary>
    /// Update target selection using enhanced system
    /// </summary>
    private void UpdateTargetSelection()
    {
        if (enhancedTargetSelection != null)
        {
            // Use enhanced target selection
            currentTarget = enhancedTargetSelection.SelectBestTarget(nearbyEnemies);
        }
        else
        {
            // Fallback to basic target selection
            CombatUnit bestTarget = null;
            float bestScore = float.MinValue;
            
            foreach (var enemy in nearbyEnemies)
            {
                if (enemy == null || enemy.battleState == BattleUnitState.Dead) continue;
                
                float score = CalculateTargetScore(enemy);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = enemy;
                }
            }
            
            currentTarget = bestTarget;
        }
        
        if (currentTarget != null)
        {
            lastKnownTargetPosition = currentTarget.transform.position;
        }
    }
    
    /// <summary>
    /// Calculate how good a target is based on multiple factors
    /// </summary>
    private float CalculateTargetScore(CombatUnit target)
    {
        if (target == null) return 0f;
        
        float score = 0f;
        float distance = Vector3.Distance(transform.position, target.transform.position);
        
        // Distance factor (closer is better, but not too close)
        float distanceScore = 1f / (1f + distance);
        score += distanceScore * 2f;
        
        // Health factor (weaker enemies are better targets)
        float healthRatio = (float)target.currentHealth / target.MaxHealth;
        score += (1f - healthRatio) * 3f;
        
        // Threat level (stronger enemies are more dangerous)
        float threatLevel = (float)target.CurrentAttack / unit.CurrentDefense;
        score += (1f / (1f + threatLevel)) * 2f;
        
        // Flanking bonus
        if (IsFlanking(target))
        {
            score += flankingBonus;
        }
        
        // High ground bonus
        if (IsOnHighGround(target))
        {
            score += highGroundBonus;
        }
        
        // Formation bonus (targets closer to formation center)
        float formationDistance = Vector3.Distance(target.transform.position, formationCenter);
        score += (1f / (1f + formationDistance)) * 1.5f;
        
        return score;
    }
    
    /// <summary>
    /// Check if we should retreat
    /// </summary>
    private bool ShouldRetreat()
    {
        // Check health threshold
        float healthRatio = (float)unit.currentHealth / unit.MaxHealth;
        if (healthRatio <= retreatHealthThreshold)
        {
            return true;
        }
        
        // Check morale threshold
        float moraleRatio = (float)unit.currentMorale / unit.MaxMorale;
        if (moraleRatio <= retreatMoraleThreshold)
        {
            return true;
        }
        
        // Check if outnumbered
        if (nearbyEnemies.Count > nearbyAllies.Count * 2)
        {
            return true;
        }
        
        // Check if surrounded
        if (IsSurrounded())
        {
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Check if we should regroup with allies
    /// </summary>
    private bool ShouldRegroup()
    {
        // If we're too far from formation center
        float distanceFromFormation = Vector3.Distance(transform.position, formationCenter);
        if (distanceFromFormation > retreatDistance)
        {
            return true;
        }
        
        // If we're isolated from allies
        if (nearbyAllies.Count == 0 && nearbyEnemies.Count > 0)
        {
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Check if we should try to flank
    /// </summary>
    private bool ShouldFlank()
    {
        if (currentTarget == null) return false;
        
        // Don't flank if we're already in melee
        float distanceToTarget = Vector3.Distance(transform.position, currentTarget.transform.position);
        if (distanceToTarget <= 2f) return false;
        
        // Flank if we can get behind the enemy
        Vector3 targetDirection = (currentTarget.transform.position - transform.position).normalized;
        Vector3 flankDirection = Vector3.Cross(targetDirection, Vector3.up);
        
        // Check if flanking position is clear
        Vector3 flankPosition = currentTarget.transform.position + flankDirection * 3f;
        if (IsPositionClear(flankPosition))
        {
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Execute attack on current target
    /// </summary>
    public void ExecuteAttack()
    {
        if (currentTarget == null) return;
        
        // Store target health before attack for learning
        int targetHealthBefore = currentTarget.currentHealth;
        
        unit.AttackTarget(currentTarget);
        
        // Learn from attack result
        if (enhancedTargetSelection != null)
        {
            int targetHealthAfter = currentTarget.currentHealth;
            float damageDealt = targetHealthBefore - targetHealthAfter;
            
            if (damageDealt > 0)
            {
                enhancedTargetSelection.LearnFromSuccessfulAttack(currentTarget, damageDealt);
            }
        }
        
        Debug.Log($"[BattleAI] {unit.data.unitName} attacking {currentTarget.data.unitName}");
    }
    
    /// <summary>
    /// Execute advance towards enemies
    /// </summary>
    public void ExecuteAdvance()
    {
        if (currentTarget != null)
        {
            // Move towards current target
            unit.MoveToPosition(currentTarget.transform.position);
        }
        else if (nearbyEnemies.Count > 0)
        {
            // Move towards nearest enemy
            CombatUnit nearestEnemy = GetNearestEnemy();
            if (nearestEnemy != null)
            {
                unit.MoveToPosition(nearestEnemy.transform.position);
            }
        }
    }
    
    /// <summary>
    /// Execute flanking maneuver
    /// </summary>
    public void ExecuteFlank()
    {
        if (currentTarget == null) return;
        
        Vector3 targetDirection = (currentTarget.transform.position - transform.position).normalized;
        Vector3 flankDirection = Vector3.Cross(targetDirection, Vector3.up);
        Vector3 flankPosition = currentTarget.transform.position + flankDirection * 3f;
        
        unit.MoveToPosition(flankPosition);
        Debug.Log($"[BattleAI] {unit.data.unitName} flanking {currentTarget.data.unitName}");
    }
    
    /// <summary>
    /// Execute retreat
    /// </summary>
    public void ExecuteRetreat()
    {
        Vector3 retreatDirection = GetRetreatDirection();
        Vector3 retreatPosition = transform.position + retreatDirection * retreatDistance;
        
        unit.MoveToPosition(retreatPosition);
        Debug.Log($"[BattleAI] {unit.data.unitName} retreating");
    }
    
    /// <summary>
    /// Execute regroup with allies
    /// </summary>
    public void ExecuteRegroup()
    {
        unit.MoveToPosition(formationCenter);
        Debug.Log($"[BattleAI] {unit.data.unitName} regrouping");
    }
    
    /// <summary>
    /// Execute defend position
    /// </summary>
    public void ExecuteDefend()
    {
        // Hold position - don't move
        Debug.Log($"[BattleAI] {unit.data.unitName} defending position");
    }
    
    /// <summary>
    /// Update nearby units lists
    /// </summary>
    private void UpdateNearbyUnits()
    {
        nearbyAllies.Clear();
        nearbyEnemies.Clear();
        
        var allUnits = FindObjectsByType<CombatUnit>(FindObjectsSortMode.None);
        foreach (var otherUnit in allUnits)
        {
            if (otherUnit == null || otherUnit == unit || otherUnit.battleState == BattleUnitState.Dead)
                continue;
            
            float distance = Vector3.Distance(transform.position, otherUnit.transform.position);
            if (distance <= 15f) // Within AI range
            {
                if (otherUnit.isAttacker == unit.isAttacker)
                {
                    nearbyAllies.Add(otherUnit);
                }
                else
                {
                    nearbyEnemies.Add(otherUnit);
                }
            }
        }
    }
    
    /// <summary>
    /// Check if we're surrounded by enemies
    /// </summary>
    private bool IsSurrounded()
    {
        if (nearbyEnemies.Count < 3) return false;
        
        // Check if enemies are on multiple sides
        int sidesWithEnemies = 0;
        Vector3[] directions = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
        
        foreach (var direction in directions)
        {
            bool hasEnemyInDirection = false;
            foreach (var enemy in nearbyEnemies)
            {
                Vector3 toEnemy = (enemy.transform.position - transform.position).normalized;
                if (Vector3.Dot(direction, toEnemy) > 0.5f)
                {
                    hasEnemyInDirection = true;
                    break;
                }
            }
            if (hasEnemyInDirection) sidesWithEnemies++;
        }
        
        return sidesWithEnemies >= 3;
    }
    
    /// <summary>
    /// Check if we're flanking a target
    /// </summary>
    private bool IsFlanking(CombatUnit target)
    {
        if (target == null) return false;
        
        Vector3 toTarget = (target.transform.position - transform.position).normalized;
        Vector3 targetForward = target.transform.forward;
        
        // Check if we're behind the target
        return Vector3.Dot(toTarget, targetForward) > 0.7f;
    }
    
    /// <summary>
    /// Check if target is on high ground
    /// </summary>
    private bool IsOnHighGround(CombatUnit target)
    {
        if (target == null) return false;
        
        return target.transform.position.y > transform.position.y + 1f;
    }
    
    
    /// <summary>
    /// Get the nearest enemy
    /// </summary>
    private CombatUnit GetNearestEnemy()
    {
        CombatUnit nearest = null;
        float nearestDistance = float.MaxValue;
        
        foreach (var enemy in nearbyEnemies)
        {
            if (enemy == null) continue;
            
            float distance = Vector3.Distance(transform.position, enemy.transform.position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = enemy;
            }
        }
        
        return nearest;
    }
    
    /// <summary>
    /// Get retreat direction (away from enemies)
    /// </summary>
    private Vector3 GetRetreatDirection()
    {
        if (nearbyEnemies.Count == 0)
        {
            return -transform.forward; // Default retreat direction
        }
        
        Vector3 retreatDirection = Vector3.zero;
        foreach (var enemy in nearbyEnemies)
        {
            if (enemy == null) continue;
            
            Vector3 awayFromEnemy = (transform.position - enemy.transform.position).normalized;
            retreatDirection += awayFromEnemy;
        }
        
        return retreatDirection.normalized;
    }
    
    /// <summary>
    /// Update formation center based on nearby allies
    /// </summary>
    private void UpdateFormationCenter()
    {
        if (nearbyAllies.Count == 0)
        {
            formationCenter = transform.position;
            return;
        }
        
        Vector3 center = Vector3.zero;
        int count = 0;
        
        foreach (var ally in nearbyAllies)
        {
            if (ally != null)
            {
                center += ally.transform.position;
                count++;
            }
        }
        
        if (count > 0)
        {
            formationCenter = center / count;
        }
    }
    
    /// <summary>
    /// Set AI state and log changes
    /// </summary>
    private void SetAIState(AIState newState)
    {
        if (currentState != newState)
        {
            Debug.Log($"[BattleAI] {unit.data.unitName} state: {currentState} -> {newState}");
            currentState = newState;
        }
    }
    
    /// <summary>
    /// Get current AI state for debugging
    /// </summary>
    public AIState GetCurrentState()
    {
        return currentState;
    }
    
    /// <summary>
    /// Get current target for debugging
    /// </summary>
    public CombatUnit GetCurrentTarget()
    {
        return currentTarget;
    }
    
    // ===== HELPER METHODS FOR BEHAVIOR TREE =====
    
    /// <summary>
    /// Get nearby allies for behavior tree
    /// </summary>
    public List<CombatUnit> GetNearbyAllies()
    {
        return nearbyAllies;
    }
    
    /// <summary>
    /// Get nearby enemies for behavior tree
    /// </summary>
    public List<CombatUnit> GetNearbyEnemies()
    {
        return nearbyEnemies;
    }
    
    /// <summary>
    /// Get formation center for behavior tree
    /// </summary>
    public Vector3 GetFormationCenter()
    {
        return formationCenter;
    }
    
    /// <summary>
    /// Get max formation distance for behavior tree
    /// </summary>
    public float maxFormationDistance = 8f;
    
    /// <summary>
    /// Check if position is clear for behavior tree
    /// </summary>
    public bool IsPositionClear(Vector3 position)
    {
        // Simple check - could be enhanced with proper pathfinding
        return true;
    }
    
    /// <summary>
    /// Set current target (for tactical scripts)
    /// </summary>
    public void SetCurrentTarget(CombatUnit target)
    {
        currentTarget = target;
        if (target != null)
        {
            lastKnownTargetPosition = target.transform.position;
        }
    }
}
