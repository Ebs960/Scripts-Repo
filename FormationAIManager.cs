using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Consolidated AI system for formations
/// Merges BattleAI and BattleAIManager functionality, working at formation level
/// </summary>
public class FormationAIManager : MonoBehaviour
{
    [Header("AI Coordination")]
    [Tooltip("How often the AI manager updates formation coordination")]
    public float coordinationInterval = 2f;
    [Tooltip("How often the AI manager updates tactical decisions")]
    public float tacticalInterval = 5f;
    
    [Header("Formation Behavior")]
    [Tooltip("Distance at which formations will engage enemies")]
    public float preferredEngagementRange = 10f;
    [Tooltip("Distance at which formations will retreat if outnumbered")]
    public float retreatDistance = 15f;
    [Tooltip("Health percentage below which formation will retreat")]
    [Range(0f, 1f)]
    public float retreatHealthThreshold = 0.3f;
    [Tooltip("Morale threshold below which formation will retreat")]
    [Range(0f, 1f)]
    public float retreatMoraleThreshold = 0.2f;
    
    [Header("Tactical Behavior")]
    [Tooltip("How much the AI values flanking enemies")]
    public float flankingBonus = 1.5f;
    [Tooltip("How tightly formations should maintain position")]
    public float formationTightness = 2f;
    
    private List<FormationUnit> allFormations = new List<FormationUnit>();
    private List<FormationUnit> attackerFormations = new List<FormationUnit>();
    private List<FormationUnit> defenderFormations = new List<FormationUnit>();
    private float lastCoordinationTime;
    private float lastTacticalTime;
    
    // Singleton instance for registration
    private static FormationAIManager instance;
    public static FormationAIManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<FormationAIManager>();
            }
            return instance;
        }
    }
    
    void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }
    
    // Tactical state
    private Vector3 attackerFormationCenter;
    private Vector3 defenderFormationCenter;
    private TacticalSituation currentSituation;
    
    public enum TacticalSituation
    {
        Balanced,       // Equal forces
        AttackerAdvantage,  // Attackers have advantage
        DefenderAdvantage,  // Defenders have advantage
        AttackerOverwhelming,  // Attackers greatly outnumber
        DefenderOverwhelming   // Defenders greatly outnumber
    }
    
    public enum FormationAIState
    {
        Idle,           // Not doing anything
        Advancing,      // Moving towards enemies
        Engaging,       // In combat
        Flanking,       // Trying to get behind enemy
        Retreating,     // Running away
        Defending       // Holding position
    }
    
    /// <summary>
    /// Initialize the AI manager with formations (legacy method - kept for compatibility)
    /// </summary>
    public void Initialize(List<FormationUnit> formations)
    {
        allFormations = new List<FormationUnit>(formations);
        RefreshFormationLists();
        
        // Initialize formation centers
        UpdateFormationCenters();
        
        lastCoordinationTime = Time.time;
        lastTacticalTime = Time.time;
        
        Debug.Log($"[FormationAIManager] Initialized with {allFormations.Count} formations ({attackerFormations.Count} attackers, {defenderFormations.Count} defenders)");
    }
    
    /// <summary>
    /// Register a formation with the AI manager (called by FormationUnit when ready)
    /// </summary>
    public void RegisterFormation(FormationUnit formation)
    {
        if (formation == null) return;
        
        // Check if already registered
        if (allFormations.Contains(formation)) return;
        
        // Add to all formations
        allFormations.Add(formation);
        
        // Refresh formation lists to update attacker/defender lists
        RefreshFormationLists();
        
        // Update formation centers
        UpdateFormationCenters();
        
        Debug.Log($"[FormationAIManager] Registered formation: {formation.formationName} ({(formation.isAttacker ? "Attacker" : "Defender")}) - Total: {allFormations.Count} ({attackerFormations.Count} attackers, {defenderFormations.Count} defenders)");
    }
    
    /// <summary>
    /// Unregister a formation from the AI manager (called when formation is destroyed)
    /// </summary>
    public void UnregisterFormation(FormationUnit formation)
    {
        if (formation == null) return;
        
        // Remove from all lists
        allFormations.Remove(formation);
        attackerFormations.Remove(formation);
        defenderFormations.Remove(formation);
        
        Debug.Log($"[FormationAIManager] Unregistered formation: {formation.formationName} - Remaining: {allFormations.Count} ({attackerFormations.Count} attackers, {defenderFormations.Count} defenders)");
    }
    
    void Update()
    {
        float currentTime = Time.time;
        
        // Update coordination
        if (currentTime - lastCoordinationTime >= coordinationInterval)
        {
            UpdateCoordination();
            lastCoordinationTime = currentTime;
        }
        
        // Update tactical decisions
        if (currentTime - lastTacticalTime >= tacticalInterval)
        {
            UpdateTacticalSituation();
            lastTacticalTime = currentTime;
        }
    }
    
    /// <summary>
    /// Refresh formation lists by side
    /// </summary>
    private void RefreshFormationLists()
    {
        attackerFormations.Clear();
        defenderFormations.Clear();
        
        foreach (var formation in allFormations)
        {
            if (formation == null) continue;
            
            if (formation.isAttacker)
            {
                attackerFormations.Add(formation);
            }
            else
            {
                defenderFormations.Add(formation);
            }
        }
    }
    
    /// <summary>
    /// Update coordination between formations
    /// </summary>
    private void UpdateCoordination()
    {
        // Update formation centers
        UpdateFormationCenters();
        
        // Coordinate formation behavior based on tactical situation
        switch (currentSituation)
        {
            case TacticalSituation.Balanced:
                CoordinateBalancedFight();
                break;
            case TacticalSituation.AttackerAdvantage:
                CoordinateAttackerAdvantage();
                break;
            case TacticalSituation.DefenderAdvantage:
                CoordinateDefenderAdvantage();
                break;
            case TacticalSituation.AttackerOverwhelming:
                CoordinateOverwhelmingAttack();
                break;
            case TacticalSituation.DefenderOverwhelming:
                CoordinateOverwhelmingDefense();
                break;
        }
    }
    
    /// <summary>
    /// Update tactical situation assessment
    /// </summary>
    private void UpdateTacticalSituation()
    {
        int attackerCount = GetActiveFormationCount(attackerFormations);
        int defenderCount = GetActiveFormationCount(defenderFormations);
        float attackerStrength = GetTotalFormationStrength(attackerFormations);
        float defenderStrength = GetTotalFormationStrength(defenderFormations);
        
        // Determine tactical situation
        if (attackerCount == 0 || defenderCount == 0)
        {
            currentSituation = TacticalSituation.Balanced; // Battle over
            return;
        }
        
        float strengthRatio = attackerStrength / defenderStrength;
        float countRatio = (float)attackerCount / defenderCount;
        
        if (strengthRatio > 2f || countRatio > 2f)
        {
            currentSituation = TacticalSituation.AttackerOverwhelming;
        }
        else if (strengthRatio < 0.5f || countRatio < 0.5f)
        {
            currentSituation = TacticalSituation.DefenderOverwhelming;
        }
        else if (strengthRatio > 1.2f || countRatio > 1.2f)
        {
            currentSituation = TacticalSituation.AttackerAdvantage;
        }
        else if (strengthRatio < 0.8f || countRatio < 0.8f)
        {
            currentSituation = TacticalSituation.DefenderAdvantage;
        }
        else
        {
            currentSituation = TacticalSituation.Balanced;
        }
        
        Debug.Log($"[FormationAIManager] Tactical situation: {currentSituation} (A:{attackerCount}/{attackerStrength:F1} vs D:{defenderCount}/{defenderStrength:F1})");
    }
    
    /// <summary>
    /// Coordinate balanced fight
    /// </summary>
    private void CoordinateBalancedFight()
    {
        // In balanced fights, focus on formation and flanking
        foreach (var formation in allFormations)
        {
            if (formation == null || formation.isRouted || formation.isPlayerControlled) continue; // Skip player-controlled formations
            
            // Find nearest enemy formation
            FormationUnit nearestEnemy = FindNearestEnemyFormation(formation);
            if (nearestEnemy != null)
            {
                float distance = Vector3.Distance(formation.formationCenter, nearestEnemy.formationCenter);
                
                if (distance > preferredEngagementRange)
                {
                    // Move towards enemy
                    formation.MoveToPosition(nearestEnemy.formationCenter);
                }
                else
                {
                    // Engage enemy
                    formation.isMoving = false;
                }
            }
        }
    }
    
    /// <summary>
    /// Coordinate when attackers have advantage
    /// </summary>
    private void CoordinateAttackerAdvantage()
    {
        // Attackers should be more aggressive
        foreach (var formation in attackerFormations)
        {
            if (formation == null || formation.isRouted || formation.isPlayerControlled) continue; // Skip player-controlled formations
            
            FormationUnit nearestEnemy = FindNearestEnemyFormation(formation);
            if (nearestEnemy != null)
            {
                formation.MoveToPosition(nearestEnemy.formationCenter);
            }
        }
        
        // Defenders should be more defensive
        foreach (var formation in defenderFormations)
        {
            if (formation == null || formation.isRouted || formation.isPlayerControlled) continue; // Skip player-controlled formations
            
            // Hold position or retreat if low health
            if (formation.currentHealth < formation.totalHealth * retreatHealthThreshold)
            {
                // Retreat away from nearest enemy
                FormationUnit nearestEnemy = FindNearestEnemyFormation(formation);
                if (nearestEnemy != null)
                {
                    Vector3 retreatDirection = (formation.formationCenter - nearestEnemy.formationCenter).normalized;
                    formation.MoveToPosition(formation.formationCenter + retreatDirection * retreatDistance);
                }
            }
        }
    }
    
    /// <summary>
    /// Coordinate when defenders have advantage
    /// </summary>
    private void CoordinateDefenderAdvantage()
    {
        // Defenders should be more aggressive
        foreach (var formation in defenderFormations)
        {
            if (formation == null || formation.isRouted || formation.isPlayerControlled) continue; // Skip player-controlled formations
            
            FormationUnit nearestEnemy = FindNearestEnemyFormation(formation);
            if (nearestEnemy != null)
            {
                formation.MoveToPosition(nearestEnemy.formationCenter);
            }
        }
        
        // Attackers should be more defensive
        foreach (var formation in attackerFormations)
        {
            if (formation == null || formation.isRouted || formation.isPlayerControlled) continue; // Skip player-controlled formations
            
            // Hold position or retreat if low health
            if (formation.currentHealth < formation.totalHealth * retreatHealthThreshold)
            {
                // Retreat away from nearest enemy
                FormationUnit nearestEnemy = FindNearestEnemyFormation(formation);
                if (nearestEnemy != null)
                {
                    Vector3 retreatDirection = (formation.formationCenter - nearestEnemy.formationCenter).normalized;
                    formation.MoveToPosition(formation.formationCenter + retreatDirection * retreatDistance);
                }
            }
        }
    }
    
    /// <summary>
    /// Coordinate overwhelming attack
    /// </summary>
    private void CoordinateOverwhelmingAttack()
    {
        // All attackers should be aggressive
        foreach (var formation in attackerFormations)
        {
            if (formation == null || formation.isRouted || formation.isPlayerControlled) continue; // Skip player-controlled formations
            
            FormationUnit nearestEnemy = FindNearestEnemyFormation(formation);
            if (nearestEnemy != null)
            {
                formation.MoveToPosition(nearestEnemy.formationCenter);
            }
        }
        
        // Defenders should try to retreat or hold position
        foreach (var formation in defenderFormations)
        {
            if (formation == null || formation.isRouted || formation.isPlayerControlled) continue; // Skip player-controlled formations
            
            if (formation.currentHealth < formation.totalHealth * retreatHealthThreshold ||
                formation.currentMorale < retreatMoraleThreshold * 100f)
            {
                // Retreat
                FormationUnit nearestEnemy = FindNearestEnemyFormation(formation);
                if (nearestEnemy != null)
                {
                    Vector3 retreatDirection = (formation.formationCenter - nearestEnemy.formationCenter).normalized;
                    formation.MoveToPosition(formation.formationCenter + retreatDirection * retreatDistance);
                }
            }
        }
    }
    
    /// <summary>
    /// Coordinate overwhelming defense
    /// </summary>
    private void CoordinateOverwhelmingDefense()
    {
        // All defenders should be aggressive
        foreach (var formation in defenderFormations)
        {
            if (formation == null || formation.isRouted || formation.isPlayerControlled) continue; // Skip player-controlled formations
            
            FormationUnit nearestEnemy = FindNearestEnemyFormation(formation);
            if (nearestEnemy != null)
            {
                formation.MoveToPosition(nearestEnemy.formationCenter);
            }
        }
        
        // Attackers should try to retreat or hold position
        foreach (var formation in attackerFormations)
        {
            if (formation == null || formation.isRouted || formation.isPlayerControlled) continue; // Skip player-controlled formations
            
            if (formation.currentHealth < formation.totalHealth * retreatHealthThreshold ||
                formation.currentMorale < retreatMoraleThreshold * 100f)
            {
                // Retreat
                FormationUnit nearestEnemy = FindNearestEnemyFormation(formation);
                if (nearestEnemy != null)
                {
                    Vector3 retreatDirection = (formation.formationCenter - nearestEnemy.formationCenter).normalized;
                    formation.MoveToPosition(formation.formationCenter + retreatDirection * retreatDistance);
                }
            }
        }
    }
    
    /// <summary>
    /// Find nearest enemy formation
    /// </summary>
    private FormationUnit FindNearestEnemyFormation(FormationUnit formation)
    {
        List<FormationUnit> enemies = formation.isAttacker ? defenderFormations : attackerFormations;
        FormationUnit nearest = null;
        float nearestDistance = float.MaxValue;
        
        foreach (var enemy in enemies)
        {
            if (enemy == null || enemy.isRouted) continue;
            
            float distance = Vector3.Distance(formation.formationCenter, enemy.formationCenter);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = enemy;
            }
        }
        
        return nearest;
    }
    
    /// <summary>
    /// Update formation centers for both sides
    /// </summary>
    private void UpdateFormationCenters()
    {
        attackerFormationCenter = CalculateFormationCenter(attackerFormations);
        defenderFormationCenter = CalculateFormationCenter(defenderFormations);
    }
    
    /// <summary>
    /// Calculate formation center for a group of formations
    /// </summary>
    private Vector3 CalculateFormationCenter(List<FormationUnit> formations)
    {
        if (formations.Count == 0) return Vector3.zero;
        
        Vector3 center = Vector3.zero;
        int count = 0;
        
        foreach (var formation in formations)
        {
            if (formation != null && !formation.isRouted)
            {
                center += formation.formationCenter;
                count++;
            }
        }
        
        return count > 0 ? center / count : Vector3.zero;
    }
    
    /// <summary>
    /// Get count of active formations
    /// </summary>
    private int GetActiveFormationCount(List<FormationUnit> formations)
    {
        int count = 0;
        foreach (var formation in formations)
        {
            if (formation != null && !formation.isRouted && formation.currentHealth > 0)
            {
                count++;
            }
        }
        return count;
    }
    
    /// <summary>
    /// Get total strength of formations
    /// </summary>
    private float GetTotalFormationStrength(List<FormationUnit> formations)
    {
        float totalStrength = 0f;
        foreach (var formation in formations)
        {
            if (formation != null && !formation.isRouted && formation.currentHealth > 0)
            {
                // Calculate formation strength based on health ratio and attack
                float healthRatio = (float)formation.currentHealth / formation.totalHealth;
                float strength = formation.totalAttack * healthRatio;
                totalStrength += strength;
            }
        }
        return totalStrength;
    }
    
    /// <summary>
    /// Get current tactical situation
    /// </summary>
    public TacticalSituation GetCurrentSituation()
    {
        return currentSituation;
    }
    
    /// <summary>
    /// Get formation center for a side
    /// </summary>
    public Vector3 GetFormationCenter(bool isAttacker)
    {
        return isAttacker ? attackerFormationCenter : defenderFormationCenter;
    }
}

