using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Manages victory conditions for battles, focusing on morale-based routing
/// </summary>
public class BattleVictoryManager : MonoBehaviour
{
    [Header("Victory Settings")]
    [Tooltip("Morale threshold for routing (0-100)")]
    [Range(0, 100)]
    public int routingMoraleThreshold = 0;
    [Tooltip("Time units have to be routing before they're considered defeated")]
    public float routingTimeToDefeat = 10f;
    [Tooltip("Check for victory conditions every X seconds")]
    public float victoryCheckInterval = 2f;

    [Header("Debug")]
    [Tooltip("Show debug information about victory conditions")]
    public bool showDebugInfo = true;

    // Battle state
    private List<CombatUnit> attackerUnits = new List<CombatUnit>();
    private List<CombatUnit> defenderUnits = new List<CombatUnit>();
    private Dictionary<CombatUnit, float> routingStartTimes = new Dictionary<CombatUnit, float>();
    private bool battleInProgress = false;
    private float lastVictoryCheck = 0f;

    // Events
    public System.Action<BattleResult> OnBattleEnded;

    void Update()
    {
        if (battleInProgress)
        {
            if (Time.time - lastVictoryCheck >= victoryCheckInterval)
            {
                CheckVictoryConditions();
                lastVictoryCheck = Time.time;
            }
        }
    }

    /// <summary>
    /// Initialize the victory manager for a new battle
    /// </summary>
    public void InitializeBattle(List<CombatUnit> attackers, List<CombatUnit> defenders)
    {
        // Only initialize if we have actual units on both sides
        if (attackers == null || attackers.Count == 0 || defenders == null || defenders.Count == 0)
        {
            DebugLog($"Warning: Battle initialization skipped - invalid unit lists (attackers: {attackers?.Count ?? 0}, defenders: {defenders?.Count ?? 0})");
            battleInProgress = false;
            return;
        }
        
        // Filter out null units
        attackerUnits = attackers.Where(u => u != null).ToList();
        defenderUnits = defenders.Where(u => u != null).ToList();
        
        // Only initialize if we still have units after filtering
        if (attackerUnits.Count == 0 || defenderUnits.Count == 0)
        {
            DebugLog($"Warning: Battle initialization skipped - no valid units after filtering (attackers: {attackerUnits.Count}, defenders: {defenderUnits.Count})");
            battleInProgress = false;
            return;
        }
        
        routingStartTimes.Clear();
        battleInProgress = true;
        battleStartTime = Time.time;
        lastVictoryCheck = Time.time;

        // Battle initialized
    }
    
    /// <summary>
    /// Initialize with expected counts from menu selections (before units are spawned)
    /// This allows the victory manager to track expected unit counts
    /// </summary>
    public void InitializeWithExpectedCounts(int expectedAttackers, int expectedDefenders)
    {
        // Clear any existing battle state
        attackerUnits.Clear();
        defenderUnits.Clear();
        routingStartTimes.Clear();
        battleInProgress = false;
        
        // Store expected counts for later validation
        // The actual units will be added when InitializeBattle is called after spawning
    }

    /// <summary>
    /// Check if victory conditions have been met
    /// </summary>
    private void CheckVictoryConditions()
    {
        if (!battleInProgress) return;

        // Update routing times
        UpdateRoutingTimes();

        // Check if all units of one side are defeated or routing
        bool attackersDefeated = AreAllUnitsDefeatedOrRouting(attackerUnits);
        bool defendersDefeated = AreAllUnitsDefeatedOrRouting(defenderUnits);

        if (attackersDefeated && defendersDefeated)
        {
            // Draw - both sides defeated
            EndBattle(CreateBattleResult(null, null, true));
        }
        else if (attackersDefeated)
        {
            // Defenders win
            var defenderCiv = GetFirstCivilization(defenderUnits);
            var attackerCiv = GetFirstCivilization(attackerUnits);
            EndBattle(CreateBattleResult(defenderCiv, attackerCiv, false));
        }
        else if (defendersDefeated)
        {
            // Attackers win
            var attackerCiv = GetFirstCivilization(attackerUnits);
            var defenderCiv = GetFirstCivilization(defenderUnits);
            EndBattle(CreateBattleResult(attackerCiv, defenderCiv, false));
        }
    }

    /// <summary>
    /// Update routing times for units that are routing
    /// </summary>
    private void UpdateRoutingTimes()
    {
        var allUnits = attackerUnits.Concat(defenderUnits).ToList();
        
        foreach (var unit in allUnits)
        {
            if (unit == null) continue;

            // Check if unit is routing (morale at or below threshold)
            if (unit.currentMorale <= routingMoraleThreshold)
            {
                if (!routingStartTimes.ContainsKey(unit))
                {
                    routingStartTimes[unit] = Time.time;
                    DebugLog($"Unit {unit.data.unitName} started routing (morale: {unit.currentMorale})");
                }
            }
            else
            {
                // Unit is no longer routing, remove from tracking
                if (routingStartTimes.ContainsKey(unit))
                {
                    routingStartTimes.Remove(unit);
                    DebugLog($"Unit {unit.data.unitName} stopped routing (morale: {unit.currentMorale})");
                }
            }
        }
    }

    /// <summary>
    /// Check if all units in a list are defeated or have been routing long enough
    /// </summary>
    private bool AreAllUnitsDefeatedOrRouting(List<CombatUnit> units)
    {
        // Don't count empty lists as defeated - battle might not have started yet
        if (units == null || units.Count == 0) return false;

        foreach (var unit in units)
        {
            if (unit == null) continue;

            // Check if unit is dead
            if (unit.currentHealth <= 0) continue;

            // Check if unit is routing and has been routing long enough
            if (routingStartTimes.ContainsKey(unit))
            {
                float routingTime = Time.time - routingStartTimes[unit];
                if (routingTime >= routingTimeToDefeat) continue;
            }

            // Unit is still active and not defeated
            return false;
        }

        return true;
    }

    /// <summary>
    /// End the battle with the given result
    /// </summary>
    private void EndBattle(BattleResult result)
    {
        battleInProgress = false;
        
        DebugLog($"Battle ended: {result}");
        
        // Calculate battle statistics
        var battleStats = CalculateBattleStatistics();
        
        // Fire event
        OnBattleEnded?.Invoke(result);
        
        // Log detailed results
        LogBattleResults(result, battleStats);
    }

    /// <summary>
    /// Calculate battle statistics
    /// </summary>
    private BattleStatistics CalculateBattleStatistics()
    {
        var stats = new BattleStatistics();
        
        // Count surviving units
        stats.attackerSurvivors = attackerUnits.Count(u => u != null && u.currentHealth > 0);
        stats.defenderSurvivors = defenderUnits.Count(u => u != null && u.currentHealth > 0);
        
        // Count casualties
        stats.attackerCasualties = attackerUnits.Count - stats.attackerSurvivors;
        stats.defenderCasualties = defenderUnits.Count - stats.defenderSurvivors;
        
        // Count routing units
        stats.attackerRouting = attackerUnits.Count(u => u != null && routingStartTimes.ContainsKey(u));
        stats.defenderRouting = defenderUnits.Count(u => u != null && routingStartTimes.ContainsKey(u));
        
        return stats;
    }

    /// <summary>
    /// Log detailed battle results
    /// </summary>
    private void LogBattleResults(BattleResult result, BattleStatistics stats)
    {
        DebugLog("=== BATTLE RESULTS ===");
        DebugLog($"Result: {result}");
        DebugLog($"Attacker Survivors: {stats.attackerSurvivors} (Casualties: {stats.attackerCasualties}, Routing: {stats.attackerRouting})");
        DebugLog($"Defender Survivors: {stats.defenderSurvivors} (Casualties: {stats.defenderCasualties}, Routing: {stats.defenderRouting})");
        DebugLog("=====================");
    }

    /// <summary>
    /// Get current battle status for UI display
    /// </summary>
    public BattleStatus GetBattleStatus()
    {
        var status = new BattleStatus();
        
        status.attackerUnits = attackerUnits.Count(u => u != null && u.currentHealth > 0);
        status.defenderUnits = defenderUnits.Count(u => u != null && u.currentHealth > 0);
        status.attackerRouting = attackerUnits.Count(u => u != null && routingStartTimes.ContainsKey(u));
        status.defenderRouting = defenderUnits.Count(u => u != null && routingStartTimes.ContainsKey(u));
        
        return status;
    }

    /// <summary>
    /// Force end the battle (for testing or emergency)
    /// </summary>
    public void ForceEndBattle(BattleResult result)
    {
        EndBattle(result);
    }

    /// <summary>
    /// Create a BattleResult object with the given parameters
    /// </summary>
    private BattleResult CreateBattleResult(Civilization winner, Civilization loser, bool isDraw)
    {
        var result = new BattleResult();
        
        if (isDraw)
        {
            result.winner = null;
            result.loser = null;
        }
        else
        {
            result.winner = winner;
            result.loser = loser;
        }
        
        // Calculate surviving units
        var allUnits = attackerUnits.Concat(defenderUnits).Where(u => u != null && u.currentHealth > 0).ToList();
        result.survivingUnits = allUnits;
        
        // Calculate casualties
        int totalUnits = attackerUnits.Count + defenderUnits.Count;
        result.casualties = totalUnits - allUnits.Count;
        
        // Set other values
        result.experienceGained = 0; // Could be calculated based on battle performance
        result.loot = new Dictionary<ResourceData, int>(); // Could be calculated based on defeated units
        result.battleDuration = Time.time - battleStartTime;
        
        return result;
    }

    /// <summary>
    /// Get the first civilization from a list of units
    /// </summary>
    private Civilization GetFirstCivilization(List<CombatUnit> units)
    {
        foreach (var unit in units)
        {
            if (unit != null && unit.owner != null)
            {
                return unit.owner;
            }
        }
        return null;
    }

    private float battleStartTime;
    private void DebugLog(string message)
    {
        if (showDebugInfo)
        {
            Debug.Log($"[BattleVictoryManager] {message}");
        }
    }
}

/// <summary>
/// Battle statistics for reporting
/// </summary>
[System.Serializable]
public struct BattleStatistics
{
    public int attackerSurvivors;
    public int defenderSurvivors;
    public int attackerCasualties;
    public int defenderCasualties;
    public int attackerRouting;
    public int defenderRouting;
}

/// <summary>
/// Current battle status for UI display
/// </summary>
[System.Serializable]
public struct BattleStatus
{
    public int attackerUnits;
    public int defenderUnits;
    public int attackerRouting;
    public int defenderRouting;
}
