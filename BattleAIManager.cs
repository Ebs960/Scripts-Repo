using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages AI behavior for all units in a battle
/// </summary>
public class BattleAIManager : MonoBehaviour
{
    [Header("AI Coordination")]
    [Tooltip("How often the AI manager updates unit coordination")]
    public float coordinationInterval = 2f;
    [Tooltip("How often the AI manager updates tactical decisions")]
    public float tacticalInterval = 5f;
    
    [Header("Formation Management")]
    [Tooltip("How tightly units should maintain formation")]
    public float formationTightness = 2f;
    [Tooltip("How far units can stray from formation before regrouping")]
    public float maxFormationDistance = 8f;
    
    private List<BattleAI> aiUnits = new List<BattleAI>();
    private List<BattleAI> attackerAIs = new List<BattleAI>();
    private List<BattleAI> defenderAIs = new List<BattleAI>();
    private float lastCoordinationTime;
    private float lastTacticalTime;
    
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
    
    void Start()
    {
        // Find all AI units in the scene
        RefreshAIUnits();
        
        // Initialize formation centers
        UpdateFormationCenters();
        
        lastCoordinationTime = Time.time;
        lastTacticalTime = Time.time;
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
    /// Refresh the list of AI units
    /// </summary>
    public void RefreshAIUnits()
    {
        aiUnits.Clear();
        attackerAIs.Clear();
        defenderAIs.Clear();
        
    BattleAI[] allAIs;
#if UNITY_2023_1_OR_NEWER
    allAIs = FindObjectsByType<BattleAI>(FindObjectsSortMode.None);
#else
    allAIs = FindObjectsOfType<BattleAI>();
#endif
        foreach (var ai in allAIs)
        {
            if (ai == null) continue;
            
            aiUnits.Add(ai);
            
            CombatUnit unit = ai.GetComponent<CombatUnit>();
            if (unit != null)
            {
                if (unit.isAttacker)
                {
                    attackerAIs.Add(ai);
                }
                else
                {
                    defenderAIs.Add(ai);
                }
            }
        }
        
        Debug.Log($"[BattleAIManager] Found {aiUnits.Count} AI units ({attackerAIs.Count} attackers, {defenderAIs.Count} defenders)");
    }
    
    /// <summary>
    /// Update coordination between AI units
    /// </summary>
    private void UpdateCoordination()
    {
        // Update formation centers
        UpdateFormationCenters();
        
        // Coordinate unit behavior based on tactical situation
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
        int attackerCount = GetActiveUnitCount(attackerAIs);
        int defenderCount = GetActiveUnitCount(defenderAIs);
        float attackerStrength = GetTotalStrength(attackerAIs);
        float defenderStrength = GetTotalStrength(defenderAIs);
        
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
        
        Debug.Log($"[BattleAIManager] Tactical situation: {currentSituation} (A:{attackerCount}/{attackerStrength:F1} vs D:{defenderCount}/{defenderStrength:F1})");
    }
    
    /// <summary>
    /// Coordinate balanced fight
    /// </summary>
    private void CoordinateBalancedFight()
    {
        // In balanced fights, focus on formation and flanking
        foreach (var ai in aiUnits)
        {
            if (ai == null) continue;
            
            // Encourage formation maintenance
            ai.GetComponent<CombatUnit>()?.SetBattleState(BattleUnitState.Defending);
        }
    }
    
    /// <summary>
    /// Coordinate when attackers have advantage
    /// </summary>
    private void CoordinateAttackerAdvantage()
    {
        // Attackers should be more aggressive
        foreach (var ai in attackerAIs)
        {
            if (ai == null) continue;
            
            var unit = ai.GetComponent<CombatUnit>();
            if (unit != null)
            {
                unit.SetBattleState(BattleUnitState.Attacking);
            }
        }
        
        // Defenders should be more defensive
        foreach (var ai in defenderAIs)
        {
            if (ai == null) continue;
            
            var unit = ai.GetComponent<CombatUnit>();
            if (unit != null)
            {
                unit.SetBattleState(BattleUnitState.Defending);
            }
        }
    }
    
    /// <summary>
    /// Coordinate when defenders have advantage
    /// </summary>
    private void CoordinateDefenderAdvantage()
    {
        // Defenders should be more aggressive
        foreach (var ai in defenderAIs)
        {
            if (ai == null) continue;
            
            var unit = ai.GetComponent<CombatUnit>();
            if (unit != null)
            {
                unit.SetBattleState(BattleUnitState.Attacking);
            }
        }
        
        // Attackers should be more defensive
        foreach (var ai in attackerAIs)
        {
            if (ai == null) continue;
            
            var unit = ai.GetComponent<CombatUnit>();
            if (unit != null)
            {
                unit.SetBattleState(BattleUnitState.Defending);
            }
        }
    }
    
    /// <summary>
    /// Coordinate overwhelming attack
    /// </summary>
    private void CoordinateOverwhelmingAttack()
    {
        // All attackers should be aggressive
        foreach (var ai in attackerAIs)
        {
            if (ai == null) continue;
            
            var unit = ai.GetComponent<CombatUnit>();
            if (unit != null)
            {
                unit.SetBattleState(BattleUnitState.Attacking);
            }
        }
        
        // Defenders should try to retreat or hold position
        foreach (var ai in defenderAIs)
        {
            if (ai == null) continue;
            
            var unit = ai.GetComponent<CombatUnit>();
            if (unit != null)
            {
                if (unit.currentHealth < unit.MaxHealth * 0.5f)
                {
                    unit.SetBattleState(BattleUnitState.Routing);
                }
                else
                {
                    unit.SetBattleState(BattleUnitState.Defending);
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
        foreach (var ai in defenderAIs)
        {
            if (ai == null) continue;
            
            var unit = ai.GetComponent<CombatUnit>();
            if (unit != null)
            {
                unit.SetBattleState(BattleUnitState.Attacking);
            }
        }
        
        // Attackers should try to retreat or hold position
        foreach (var ai in attackerAIs)
        {
            if (ai == null) continue;
            
            var unit = ai.GetComponent<CombatUnit>();
            if (unit != null)
            {
                if (unit.currentHealth < unit.MaxHealth * 0.5f)
                {
                    unit.SetBattleState(BattleUnitState.Routing);
                }
                else
                {
                    unit.SetBattleState(BattleUnitState.Defending);
                }
            }
        }
    }
    
    /// <summary>
    /// Update formation centers for both sides
    /// </summary>
    private void UpdateFormationCenters()
    {
        attackerFormationCenter = CalculateFormationCenter(attackerAIs);
        defenderFormationCenter = CalculateFormationCenter(defenderAIs);
    }
    
    /// <summary>
    /// Calculate formation center for a group of units
    /// </summary>
    private Vector3 CalculateFormationCenter(List<BattleAI> units)
    {
        if (units.Count == 0) return Vector3.zero;
        
        Vector3 center = Vector3.zero;
        int count = 0;
        
        foreach (var ai in units)
        {
            if (ai != null)
            {
                center += ai.transform.position;
                count++;
            }
        }
        
        return count > 0 ? center / count : Vector3.zero;
    }
    
    /// <summary>
    /// Get count of active units
    /// </summary>
    private int GetActiveUnitCount(List<BattleAI> units)
    {
        int count = 0;
        foreach (var ai in units)
        {
            if (ai != null)
            {
                var unit = ai.GetComponent<CombatUnit>();
                if (unit != null && unit.battleState != BattleUnitState.Dead)
                {
                    count++;
                }
            }
        }
        return count;
    }
    
    /// <summary>
    /// Get total strength of units
    /// </summary>
    private float GetTotalStrength(List<BattleAI> units)
    {
        float totalStrength = 0f;
        foreach (var ai in units)
        {
            if (ai != null)
            {
                var unit = ai.GetComponent<CombatUnit>();
                if (unit != null && unit.battleState != BattleUnitState.Dead)
                {
                    // Calculate unit strength based on health and attack
                    float healthRatio = (float)unit.currentHealth / unit.MaxHealth;
                    float strength = unit.CurrentAttack * healthRatio;
                    totalStrength += strength;
                }
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
