using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enhanced target selection system with multi-factor evaluation and learning
/// </summary>
public class EnhancedTargetSelection : MonoBehaviour
{
    [Header("Target Selection Weights")]
    [Tooltip("Weight for distance factor (closer = better)")]
    public float distanceWeight = 2.0f;
    [Tooltip("Weight for health factor (weaker = better)")]
    public float healthWeight = 3.0f;
    [Tooltip("Weight for threat factor (less dangerous = better)")]
    public float threatWeight = 2.0f;
    [Tooltip("Weight for flanking opportunity")]
    public float flankingWeight = 1.5f;
    [Tooltip("Weight for high ground advantage")]
    public float highGroundWeight = 1.3f;
    [Tooltip("Weight for formation cohesion")]
    public float formationWeight = 1.2f;
    [Tooltip("Weight for learned preferences")]
    public float learningWeight = 1.0f;
    
    [Header("Learning System")]
    [Tooltip("How quickly the AI learns from successful attacks")]
    public float learningRate = 0.1f;
    [Tooltip("How much past experience influences current decisions")]
    public float memoryDecay = 0.95f;
    
    // BattleAI removed - this script is now optional/legacy
    // Will only work if BattleAI is present, otherwise does nothing
    // Using object type to avoid compilation errors when BattleAI doesn't exist
    private object ai; // Was BattleAI, now object for compatibility
    private CombatUnit unit;
    private Dictionary<string, float> learnedPreferences = new Dictionary<string, float>();
    private Dictionary<string, float> targetTypeScores = new Dictionary<string, float>();
    
    void Start()
    {
        // Try to get BattleAI (optional - may not exist with formation-based AI)
        // Using reflection to avoid compilation errors when BattleAI type doesn't exist
        System.Type battleAIType = System.Type.GetType("BattleAI");
        if (battleAIType != null)
        {
            var component = GetComponent(battleAIType);
            ai = component;
        }
        else
        {
            ai = null;
        }
        
        unit = GetComponent<CombatUnit>();
        
        if (unit == null)
        {
            Debug.LogWarning("[EnhancedTargetSelection] Missing CombatUnit component! Disabling.");
            enabled = false;
            return;
        }
        
        // BattleAI is optional now - script will do nothing without it
        if (ai == null)
        {
            Debug.LogWarning("[EnhancedTargetSelection] BattleAI not found - this script requires individual unit AI. Disabling.");
            enabled = false;
            return;
        }
        
        InitializeLearning();
    }
    
    /// <summary>
    /// Initialize learning system with default values
    /// </summary>
    private void InitializeLearning()
    {
        // Initialize target type preferences
        targetTypeScores["Archer"] = 1.0f;
        targetTypeScores["Crossbowman"] = 1.0f;
        targetTypeScores["Swordsman"] = 0.8f;
        targetTypeScores["Spearman"] = 0.9f;
        targetTypeScores["Cavalry"] = 0.7f;
        targetTypeScores["HeavyCavalry"] = 0.6f;
        targetTypeScores["Artillery"] = 1.2f;
}
    
    /// <summary>
    /// Select the best target using enhanced evaluation
    /// </summary>
    public CombatUnit SelectBestTarget(List<CombatUnit> availableTargets)
    {
        // Return null if AI not available (script is disabled)
        if (ai == null || unit == null) return null;
        
        if (availableTargets == null || availableTargets.Count == 0)
            return null;
        
        CombatUnit bestTarget = null;
        float bestScore = float.MinValue;
        
        foreach (var target in availableTargets)
        {
            if (target == null || target.battleState == BattleUnitState.Dead)
                continue;
            
            float score = CalculateEnhancedTargetScore(target);
            if (score > bestScore)
            {
                bestScore = score;
                bestTarget = target;
            }
        }
        
        if (bestTarget != null)
        {
}
        
        return bestTarget;
    }
    
    /// <summary>
    /// Calculate enhanced target score using multiple factors
    /// </summary>
    private float CalculateEnhancedTargetScore(CombatUnit target)
    {
        if (target == null) return 0f;
        
        float totalScore = 0f;
        
        // 1. Distance Factor (closer is better, but not too close)
        float distanceScore = CalculateDistanceScore(target);
        totalScore += distanceScore * distanceWeight;
        
        // 2. Health Factor (weaker enemies are better targets)
        float healthScore = CalculateHealthScore(target);
        totalScore += healthScore * healthWeight;
        
        // 3. Threat Factor (less dangerous enemies are better targets)
        float threatScore = CalculateThreatScore(target);
        totalScore += threatScore * threatWeight;
        
        // 4. Flanking Opportunity
        float flankingScore = CalculateFlankingScore(target);
        totalScore += flankingScore * flankingWeight;
        
        // 5. High Ground Advantage
        float highGroundScore = CalculateHighGroundScore(target);
        totalScore += highGroundScore * highGroundWeight;
        
        // 6. Formation Cohesion
        float formationScore = CalculateFormationScore(target);
        totalScore += formationScore * formationWeight;
        
        // 7. Learned Preferences
        float learningScore = CalculateLearningScore(target);
        totalScore += learningScore * learningWeight;
        
        return totalScore;
    }
    
    /// <summary>
    /// Calculate distance-based score
    /// </summary>
    private float CalculateDistanceScore(CombatUnit target)
    {
        float distance = Vector3.Distance(unit.transform.position, target.transform.position);
        
        // Optimal distance is just outside attack range
        float optimalDistance = unit.battleAttackRange + 1f;
        float distanceDifference = Mathf.Abs(distance - optimalDistance);
        
        // Closer to optimal distance = higher score
        return 1f / (1f + distanceDifference);
    }
    
    /// <summary>
    /// Calculate health-based score
    /// </summary>
    private float CalculateHealthScore(CombatUnit target)
    {
        float healthRatio = (float)target.currentHealth / target.MaxHealth;
        
        // Weaker enemies are better targets
        return 1f - healthRatio;
    }
    
    /// <summary>
    /// Calculate threat-based score
    /// </summary>
    private float CalculateThreatScore(CombatUnit target)
    {
        float threatLevel = (float)target.CurrentAttack / unit.CurrentDefense;
        
        // Less threatening enemies are better targets
        return 1f / (1f + threatLevel);
    }
    
    /// <summary>
    /// Calculate flanking opportunity score
    /// </summary>
    private float CalculateFlankingScore(CombatUnit target)
    {
        Vector3 toTarget = (target.transform.position - unit.transform.position).normalized;
        Vector3 targetForward = target.transform.forward;
        
        // Check if we're behind the target
        float dotProduct = Vector3.Dot(toTarget, targetForward);
        
        if (dotProduct > 0.7f) // Behind target
        {
            return 1.0f;
        }
        else if (dotProduct > 0.3f) // Partially behind
        {
            return 0.5f;
        }
        else // In front of target
        {
            return 0.0f;
        }
    }
    
    /// <summary>
    /// Calculate high ground advantage score
    /// </summary>
    private float CalculateHighGroundScore(CombatUnit target)
    {
        float heightDifference = target.transform.position.y - unit.transform.position.y;
        
        if (heightDifference > 1f) // Target is higher
        {
            return 0.0f; // Disadvantage
        }
        else if (heightDifference < -1f) // We are higher
        {
            return 1.0f; // Advantage
        }
        else // Similar height
        {
            return 0.5f; // Neutral
        }
    }
    
    /// <summary>
    /// Calculate formation cohesion score
    /// </summary>
    private float CalculateFormationScore(CombatUnit target)
    {
        // Return neutral score if AI not available
        if (ai == null) return 0.5f;
        
        // Use reflection to call method on ai (was BattleAI)
        Vector3 formationCenter = CallMethod<Vector3>(ai, "GetFormationCenter");
        float distanceFromFormation = Vector3.Distance(target.transform.position, formationCenter);
        
        // Targets closer to formation center are better
        return 1f / (1f + distanceFromFormation);
    }
    
    /// <summary>
    /// Calculate learning-based score
    /// </summary>
    private float CalculateLearningScore(CombatUnit target)
    {
        string targetType = target.data.unitType.ToString();
        
        if (targetTypeScores.ContainsKey(targetType))
        {
            return targetTypeScores[targetType];
        }
        
        return 1.0f; // Default score for unknown types
    }
    
    /// <summary>
    /// Learn from successful attack
    /// </summary>
    public void LearnFromSuccessfulAttack(CombatUnit target, float damageDealt)
    {
        if (target == null) return;
        
        string targetType = target.data.unitType.ToString();
        
        // Increase preference for this target type
        if (targetTypeScores.ContainsKey(targetType))
        {
            targetTypeScores[targetType] += learningRate * (damageDealt / 100f);
        }
        else
        {
            targetTypeScores[targetType] = 1.0f + learningRate * (damageDealt / 100f);
        }
        
        // Clamp values to reasonable range
        targetTypeScores[targetType] = Mathf.Clamp(targetTypeScores[targetType], 0.1f, 3.0f);
}
    
    /// <summary>
    /// Learn from failed attack
    /// </summary>
    public void LearnFromFailedAttack(CombatUnit target, float damageReceived)
    {
        if (target == null) return;
        
        string targetType = target.data.unitType.ToString();
        
        // Decrease preference for this target type
        if (targetTypeScores.ContainsKey(targetType))
        {
            targetTypeScores[targetType] -= learningRate * (damageReceived / 100f);
        }
        else
        {
            targetTypeScores[targetType] = 1.0f - learningRate * (damageReceived / 100f);
        }
        
        // Clamp values to reasonable range
        targetTypeScores[targetType] = Mathf.Clamp(targetTypeScores[targetType], 0.1f, 3.0f);
}
    
    /// <summary>
    /// Update learning system (call this periodically)
    /// </summary>
    public void UpdateLearning()
    {
        // Apply memory decay to prevent overfitting
        var keys = new List<string>(targetTypeScores.Keys);
        foreach (var key in keys)
        {
            targetTypeScores[key] *= memoryDecay;
            targetTypeScores[key] = Mathf.Clamp(targetTypeScores[key], 0.1f, 3.0f);
        }
    }
    
    /// <summary>
    /// Get current target type preferences for debugging
    /// </summary>
    public Dictionary<string, float> GetTargetTypePreferences()
    {
        return new Dictionary<string, float>(targetTypeScores);
    }
    
    /// <summary>
    /// Reset learning system
    /// </summary>
    public void ResetLearning()
    {
        learnedPreferences.Clear();
        targetTypeScores.Clear();
        InitializeLearning();
}
    
    /// <summary>
    /// Helper method to call methods on ai using reflection (since BattleAI type may not exist)
    /// </summary>
    private T CallMethod<T>(object obj, string methodName, params object[] parameters)
    {
        if (obj == null) return default(T);
        
        try
        {
            System.Type type = obj.GetType();
            var method = type.GetMethod(methodName);
            if (method != null)
            {
                var result = method.Invoke(obj, parameters);
                return (T)result;
            }
        }
        catch (System.Exception)
        {
            // Method not found or type mismatch - return default
        }
        
        return default(T);
    }
}
