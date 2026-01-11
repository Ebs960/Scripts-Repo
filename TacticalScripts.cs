using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tactical scripts system for situation-specific AI responses
/// </summary>
public class TacticalScripts : MonoBehaviour
{
    [Header("Script Triggers")]
    [Tooltip("Health percentage below which defensive scripts activate")]
    [Range(0f, 1f)]
    public float defensiveScriptThreshold = 0.3f;
    
    [Tooltip("Morale percentage below which retreat scripts activate")]
    [Range(0f, 1f)]
    public float retreatScriptThreshold = 0.2f;
    
    [Tooltip("Force ratio below which aggressive scripts activate")]
    [Range(0f, 1f)]
    public float aggressiveScriptThreshold = 0.5f;
    
    // BattleAI removed - this script is now optional/legacy
    // Will only work if BattleAI is present, otherwise does nothing
    // Using object type to avoid compilation errors when BattleAI doesn't exist
    private object ai; // Was BattleAI, now object for compatibility
    private CombatUnit unit;
    private List<TacticalScript> activeScripts = new List<TacticalScript>();
    
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
            Debug.LogWarning("[TacticalScripts] Missing CombatUnit component! Disabling.");
            enabled = false;
            return;
        }
        
        // BattleAI is optional now - script will do nothing without it
        if (ai == null)
        {
            Debug.LogWarning("[TacticalScripts] BattleAI not found - this script requires individual unit AI. Disabling.");
            enabled = false;
            return;
        }
        
        InitializeScripts();
    }
    
    void Update()
    {
        // Only evaluate if AI is available (script may be disabled if BattleAI not present)
        if (ai == null || unit == null) return;
        EvaluateScripts();
    }
    
    /// <summary>
    /// Initialize all tactical scripts
    /// </summary>
    private void InitializeScripts()
    {
        // Add all available scripts
        activeScripts.Add(new DefensiveFormationScript(unit, ai));
        activeScripts.Add(new FlankingManeuverScript(unit, ai));
        activeScripts.Add(new RetreatToChokepointScript(unit, ai));
        activeScripts.Add(new ArcherPriorityScript(unit, ai));
        activeScripts.Add(new CavalryChargeScript(unit, ai));
        activeScripts.Add(new ShieldWallScript(unit, ai));
        activeScripts.Add(new HitAndRunScript(unit, ai));
        activeScripts.Add(new OverwhelmingForceScript(unit, ai));
}
    
    /// <summary>
    /// Evaluate all scripts and execute applicable ones
    /// </summary>
    private void EvaluateScripts()
    {
        foreach (var script in activeScripts)
        {
            if (script.ShouldActivate())
            {
                script.Execute();
            }
        }
    }
    
    /// <summary>
    /// Get active scripts for debugging
    /// </summary>
    public List<string> GetActiveScriptNames()
    {
        List<string> names = new List<string>();
        foreach (var script in activeScripts)
        {
            if (script.IsActive())
            {
                names.Add(script.GetScriptName());
            }
        }
        return names;
    }
}

/// <summary>
/// Base class for all tactical scripts
/// </summary>
public abstract class TacticalScript
{
    protected CombatUnit unit;
    protected object ai; // Was BattleAI, now object for compatibility
    protected bool isActive = false;
    protected float lastActivationTime = 0f;
    protected float cooldownTime = 5f; // Minimum time between activations
    
    public TacticalScript(CombatUnit unit, object ai) // Was BattleAI, now object for compatibility
    {
        this.unit = unit;
        this.ai = ai;
    }
    
    public abstract bool ShouldActivate();
    public abstract void Execute();
    public abstract string GetScriptName();
    
    public bool IsActive()
    {
        return isActive;
    }
    
    protected bool CanActivate()
    {
        return Time.time - lastActivationTime >= cooldownTime;
    }
    
    /// <summary>
    /// Helper method to call methods on ai using reflection (since BattleAI type may not exist)
    /// </summary>
    protected T CallMethod<T>(object obj, string methodName, params object[] parameters)
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
    
    /// <summary>
    /// Helper method to call void methods on ai using reflection
    /// </summary>
    protected void CallMethod(object obj, string methodName, params object[] parameters)
    {
        if (obj == null) return;
        
        try
        {
            System.Type type = obj.GetType();
            var method = type.GetMethod(methodName);
            if (method != null)
            {
                method.Invoke(obj, parameters);
            }
        }
        catch (System.Exception)
        {
            // Method not found or type mismatch - ignore
        }
    }
}

/// <summary>
/// Script: Form defensive formation when outnumbered
/// </summary>
public class DefensiveFormationScript : TacticalScript
{
    public DefensiveFormationScript(CombatUnit unit, object ai) : base(unit, ai) { }
    
    public override bool ShouldActivate()
    {
        if (!CanActivate()) return false;
        
        // Use reflection to call methods on ai (was BattleAI)
        var enemies = CallMethod<List<CombatUnit>>(ai, "GetNearbyEnemies");
        var allies = CallMethod<List<CombatUnit>>(ai, "GetNearbyAllies");
        
        // Activate if outnumbered 2:1 or more
        return enemies.Count >= allies.Count * 2;
    }
    
    public override void Execute()
    {
        // Move to formation center and hold position
        Vector3 formationCenter = CallMethod<Vector3>(ai, "GetFormationCenter");
        unit.MoveToPosition(formationCenter);
        unit.SetBattleState(BattleUnitState.Defending);
        
        isActive = true;
        lastActivationTime = Time.time;
}
    
    public override string GetScriptName()
    {
        return "Defensive Formation";
    }
}

/// <summary>
/// Script: Execute flanking maneuver when opportunity presents
/// </summary>
public class FlankingManeuverScript : TacticalScript
{
    public FlankingManeuverScript(CombatUnit unit, object ai) : base(unit, ai) { }
    
    public override bool ShouldActivate()
    {
        if (!CanActivate()) return false;
        
        // Use reflection to call method on ai (was BattleAI)
        var target = CallMethod<CombatUnit>(ai, "GetCurrentTarget");
        if (target == null) return false;
        
        // Check if we can get behind the enemy
        Vector3 toTarget = (target.transform.position - unit.transform.position).normalized;
        Vector3 targetForward = target.transform.forward;
        
        // If we're not already behind the target
        return Vector3.Dot(toTarget, targetForward) < 0.5f;
    }
    
    public override void Execute()
    {
        // Use reflection to call method on ai (was BattleAI)
        var target = CallMethod<CombatUnit>(ai, "GetCurrentTarget");
        if (target == null) return;
        
        // Calculate flanking position
        Vector3 targetDirection = (target.transform.position - unit.transform.position).normalized;
        Vector3 flankDirection = Vector3.Cross(targetDirection, Vector3.up);
        Vector3 flankPosition = target.transform.position + flankDirection * 3f;
        
        unit.MoveToPosition(flankPosition);
        unit.SetBattleState(BattleUnitState.Attacking);
        
        isActive = true;
        lastActivationTime = Time.time;
}
    
    public override string GetScriptName()
    {
        return "Flanking Maneuver";
    }
}

/// <summary>
/// Script: Retreat to chokepoint when heavily outnumbered
/// </summary>
public class RetreatToChokepointScript : TacticalScript
{
    public RetreatToChokepointScript(CombatUnit unit, object ai) : base(unit, ai) { }
    
    public override bool ShouldActivate()
    {
        if (!CanActivate()) return false;
        
        // Use reflection to call methods on ai (was BattleAI)
        var enemies = CallMethod<List<CombatUnit>>(ai, "GetNearbyEnemies");
        var allies = CallMethod<List<CombatUnit>>(ai, "GetNearbyAllies");
        
        // Activate if outnumbered 3:1 or more
        return enemies.Count >= allies.Count * 3;
    }
    
    public override void Execute()
    {
        // Find nearest chokepoint (simplified - just move away from enemies)
        Vector3 retreatDirection = Vector3.zero;
        // Use reflection to call method on ai (was BattleAI)
        var enemies = CallMethod<List<CombatUnit>>(ai, "GetNearbyEnemies");
        
        foreach (var enemy in enemies)
        {
            Vector3 awayFromEnemy = (unit.transform.position - enemy.transform.position).normalized;
            retreatDirection += awayFromEnemy;
        }
        
        Vector3 retreatPosition = unit.transform.position + retreatDirection.normalized * 10f;
        unit.MoveToPosition(retreatPosition);
        unit.SetBattleState(BattleUnitState.Routing);
        
        isActive = true;
        lastActivationTime = Time.time;
}
    
    public override string GetScriptName()
    {
        return "Retreat to Chokepoint";
    }
}

/// <summary>
/// Script: Prioritize archers when they're present
/// </summary>
public class ArcherPriorityScript : TacticalScript
{
    public ArcherPriorityScript(CombatUnit unit, object ai) : base(unit, ai) { }
    
    public override bool ShouldActivate()
    {
        if (!CanActivate()) return false;
        
        // Use reflection to call method on ai (was BattleAI)
        var enemies = CallMethod<List<CombatUnit>>(ai, "GetNearbyEnemies");
        
        // Check if any enemies are archers
        foreach (var enemy in enemies)
        {
            if (enemy.data.unitType == CombatCategory.Archer || 
                enemy.data.unitType == CombatCategory.Crossbowman)
            {
                return true;
            }
        }
        
        return false;
    }
    
    public override void Execute()
    {
        // Use reflection to call method on ai (was BattleAI)
        var enemies = CallMethod<List<CombatUnit>>(ai, "GetNearbyEnemies");
        CombatUnit archerTarget = null;
        
        // Find nearest archer
        float nearestDistance = float.MaxValue;
        foreach (var enemy in enemies)
        {
            if (enemy.data.unitType == CombatCategory.Archer || 
                enemy.data.unitType == CombatCategory.Crossbowman)
            {
                float distance = Vector3.Distance(unit.transform.position, enemy.transform.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    archerTarget = enemy;
                }
            }
        }
        
        if (archerTarget != null)
        {
            // Set archer as priority target
            CallMethod(ai, "SetCurrentTarget", archerTarget);
            unit.MoveToPosition(archerTarget.transform.position);
            unit.SetBattleState(BattleUnitState.Attacking);
            
            isActive = true;
            lastActivationTime = Time.time;
}
    }
    
    public override string GetScriptName()
    {
        return "Archer Priority";
    }
}

/// <summary>
/// Script: Execute cavalry charge when conditions are right
/// </summary>
public class CavalryChargeScript : TacticalScript
{
    public CavalryChargeScript(CombatUnit unit, object ai) : base(unit, ai) { }
    
    public override bool ShouldActivate()
    {
        if (!CanActivate()) return false;
        
        // Only activate for cavalry units
        return unit.data.unitType == CombatCategory.Cavalry || 
               unit.data.unitType == CombatCategory.HeavyCavalry ||
               unit.data.unitType == CombatCategory.RangedCavalry;
    }
    
    public override void Execute()
    {
        // Use reflection to call method on ai (was BattleAI)
        var target = CallMethod<CombatUnit>(ai, "GetCurrentTarget");
        if (target == null) return;
        
        // Charge directly at target
        unit.MoveToPosition(target.transform.position);
        unit.SetBattleState(BattleUnitState.Attacking);
        
        isActive = true;
        lastActivationTime = Time.time;
}
    
    public override string GetScriptName()
    {
        return "Cavalry Charge";
    }
}

/// <summary>
/// Script: Form shield wall when defending
/// </summary>
public class ShieldWallScript : TacticalScript
{
    public ShieldWallScript(CombatUnit unit, object ai) : base(unit, ai) { }
    
    public override bool ShouldActivate()
    {
        if (!CanActivate()) return false;
        
        // Activate if we have a shield and are defending
        return unit.equippedShield != null && unit.battleState == BattleUnitState.Defending;
    }
    
    public override void Execute()
    {
        // Hold position and face nearest enemy
        // Use reflection to call method on ai (was BattleAI)
        var enemies = CallMethod<List<CombatUnit>>(ai, "GetNearbyEnemies");
        if (enemies.Count > 0)
        {
            CombatUnit nearestEnemy = null;
            float nearestDistance = float.MaxValue;
            
            foreach (var enemy in enemies)
            {
                float distance = Vector3.Distance(unit.transform.position, enemy.transform.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestEnemy = enemy;
                }
            }
            
            if (nearestEnemy != null)
            {
                // Face the enemy
                Vector3 direction = (nearestEnemy.transform.position - unit.transform.position).normalized;
                unit.transform.rotation = Quaternion.LookRotation(direction);
            }
        }
        
        unit.SetBattleState(BattleUnitState.Defending);
        
        isActive = true;
        lastActivationTime = Time.time;
}
    
    public override string GetScriptName()
    {
        return "Shield Wall";
    }
}

/// <summary>
/// Script: Hit and run tactics for mobile units
/// </summary>
public class HitAndRunScript : TacticalScript
{
    public HitAndRunScript(CombatUnit unit, object ai) : base(unit, ai) { }
    
    public override bool ShouldActivate()
    {
        if (!CanActivate()) return false;
        
        // Activate for mobile units with ranged weapons
        // Movement points removed - check for ranged weapon only
        return unit.equippedProjectileWeapon != null;
    }
    
    public override void Execute()
    {
        // Use reflection to call method on ai (was BattleAI)
        var target = CallMethod<CombatUnit>(ai, "GetCurrentTarget");
        if (target == null) return;
        
        float distanceToTarget = Vector3.Distance(unit.transform.position, target.transform.position);
        
        if (distanceToTarget <= unit.battleAttackRange)
        {
            // Attack and then move away
            unit.AttackTarget(target);
            
            // Move to a safer position
            Vector3 awayFromTarget = (unit.transform.position - target.transform.position).normalized;
            Vector3 safePosition = unit.transform.position + awayFromTarget * 5f;
            unit.MoveToPosition(safePosition);
        }
        else
        {
            // Move closer to attack
            unit.MoveToPosition(target.transform.position);
        }
        
        isActive = true;
        lastActivationTime = Time.time;
}
    
    public override string GetScriptName()
    {
        return "Hit and Run";
    }
}

/// <summary>
/// Script: Overwhelming force when we have advantage
/// </summary>
public class OverwhelmingForceScript : TacticalScript
{
    public OverwhelmingForceScript(CombatUnit unit, object ai) : base(unit, ai) { }
    
    public override bool ShouldActivate()
    {
        if (!CanActivate()) return false;
        
        // Use reflection to call methods on ai (was BattleAI)
        var enemies = CallMethod<List<CombatUnit>>(ai, "GetNearbyEnemies");
        var allies = CallMethod<List<CombatUnit>>(ai, "GetNearbyAllies");
        
        // Activate if we outnumber enemies 2:1 or more
        return allies.Count >= enemies.Count * 2;
    }
    
    public override void Execute()
    {
        // Be more aggressive
        unit.SetBattleState(BattleUnitState.Attacking);
        
        // Use reflection to call method on ai (was BattleAI)
        var target = CallMethod<CombatUnit>(ai, "GetCurrentTarget");
        if (target != null)
        {
            unit.MoveToPosition(target.transform.position);
        }
        
        isActive = true;
        lastActivationTime = Time.time;
}
    
    public override string GetScriptName()
    {
        return "Overwhelming Force";
    }
}
