using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base class for all behavior tree nodes
/// </summary>
public abstract class BTNode
{
    public enum NodeState
    {
        Running,
        Success,
        Failure
    }
    
    protected NodeState state;
    protected CombatUnit unit;
    protected BattleAI ai;
    
    public BTNode(CombatUnit unit, BattleAI ai)
    {
        this.unit = unit;
        this.ai = ai;
    }
    
    public abstract NodeState Evaluate();
    
    public virtual void Reset()
    {
        state = NodeState.Running;
    }
}

/// <summary>
/// Selector node - tries children until one succeeds
/// </summary>
public class BTSelector : BTNode
{
    private List<BTNode> children;
    
    public BTSelector(CombatUnit unit, BattleAI ai, List<BTNode> children) : base(unit, ai)
    {
        this.children = children;
    }
    
    public override NodeState Evaluate()
    {
        foreach (var child in children)
        {
            NodeState result = child.Evaluate();
            if (result == NodeState.Success)
            {
                state = NodeState.Success;
                return state;
            }
            if (result == NodeState.Running)
            {
                state = NodeState.Running;
                return state;
            }
        }
        
        state = NodeState.Failure;
        return state;
    }
}

/// <summary>
/// Sequence node - runs children until one fails
/// </summary>
public class BTSequence : BTNode
{
    private List<BTNode> children;
    
    public BTSequence(CombatUnit unit, BattleAI ai, List<BTNode> children) : base(unit, ai)
    {
        this.children = children;
    }
    
    public override NodeState Evaluate()
    {
        foreach (var child in children)
        {
            NodeState result = child.Evaluate();
            if (result == NodeState.Failure)
            {
                state = NodeState.Failure;
                return state;
            }
            if (result == NodeState.Running)
            {
                state = NodeState.Running;
                return state;
            }
        }
        
        state = NodeState.Success;
        return state;
    }
}

/// <summary>
/// Condition node - checks if a condition is met
/// </summary>
public abstract class BTCondition : BTNode
{
    public BTCondition(CombatUnit unit, BattleAI ai) : base(unit, ai) { }
    
    public override NodeState Evaluate()
    {
        state = CheckCondition() ? NodeState.Success : NodeState.Failure;
        return state;
    }
    
    protected abstract bool CheckCondition();
}

/// <summary>
/// Action node - performs an action
/// </summary>
public abstract class BTAction : BTNode
{
    public BTAction(CombatUnit unit, BattleAI ai) : base(unit, ai) { }
    
    public override NodeState Evaluate()
    {
        state = ExecuteAction() ? NodeState.Success : NodeState.Running;
        return state;
    }
    
    protected abstract bool ExecuteAction();
}

/// <summary>
/// Main behavior tree class
/// </summary>
public class BehaviorTree
{
    private BTNode root;
    private CombatUnit unit;
    private BattleAI ai;
    
    public BehaviorTree(CombatUnit unit, BattleAI ai)
    {
        this.unit = unit;
        this.ai = ai;
        BuildTree();
    }
    
    private void BuildTree()
    {
        // Create the main behavior tree structure
        var mainSelector = new BTSelector(unit, ai, new List<BTNode>
        {
            // Retreat if necessary
            new BTSequence(unit, ai, new List<BTNode>
            {
                new BTCheckShouldRetreat(unit, ai),
                new BTActionRetreat(unit, ai)
            }),
            
            // Regroup if isolated
            new BTSequence(unit, ai, new List<BTNode>
            {
                new BTCheckShouldRegroup(unit, ai),
                new BTActionRegroup(unit, ai)
            }),
            
            // Flanking strategy
            new BTSequence(unit, ai, new List<BTNode>
            {
                new BTCheckCanFlank(unit, ai),
                new BTActionFlank(unit, ai)
            }),
            
            // Direct attack
            new BTSequence(unit, ai, new List<BTNode>
            {
                new BTCheckInRange(unit, ai),
                new BTActionAttack(unit, ai)
            }),
            
            // Advance to target
            new BTSequence(unit, ai, new List<BTNode>
            {
                new BTCheckHasTarget(unit, ai),
                new BTActionAdvance(unit, ai)
            }),
            
            // Defend position
            new BTActionDefend(unit, ai)
        });
        
        root = mainSelector;
    }
    
    public BTNode.NodeState Evaluate()
    {
        return root.Evaluate();
    }
    
    public void Reset()
    {
        root.Reset();
    }
}

// ===== CONDITION NODES =====

/// <summary>
/// Check if unit should retreat
/// </summary>
public class BTCheckShouldRetreat : BTCondition
{
    public BTCheckShouldRetreat(CombatUnit unit, BattleAI ai) : base(unit, ai) { }
    
    protected override bool CheckCondition()
    {
        // Check health threshold
        float healthRatio = (float)unit.currentHealth / unit.MaxHealth;
        if (healthRatio <= ai.retreatHealthThreshold)
            return true;
        
        // Check morale threshold
        float moraleRatio = (float)unit.currentMorale / unit.MaxMorale;
        if (moraleRatio <= ai.retreatMoraleThreshold)
            return true;
        
        // Check if outnumbered
        var nearbyEnemies = ai.GetNearbyEnemies();
        var nearbyAllies = ai.GetNearbyAllies();
        if (nearbyEnemies.Count > nearbyAllies.Count * 2)
            return true;
        
        return false;
    }
}

/// <summary>
/// Check if unit should regroup
/// </summary>
public class BTCheckShouldRegroup : BTCondition
{
    public BTCheckShouldRegroup(CombatUnit unit, BattleAI ai) : base(unit, ai) { }
    
    protected override bool CheckCondition()
    {
        // If too far from formation center
        float distanceFromFormation = Vector3.Distance(unit.transform.position, ai.GetFormationCenter());
        if (distanceFromFormation > ai.maxFormationDistance)
            return true;
        
        // If isolated from allies
        var nearbyAllies = ai.GetNearbyAllies();
        var nearbyEnemies = ai.GetNearbyEnemies();
        if (nearbyAllies.Count == 0 && nearbyEnemies.Count > 0)
            return true;
        
        return false;
    }
}

/// <summary>
/// Check if unit can flank
/// </summary>
public class BTCheckCanFlank : BTCondition
{
    public BTCheckCanFlank(CombatUnit unit, BattleAI ai) : base(unit, ai) { }
    
    protected override bool CheckCondition()
    {
        var target = ai.GetCurrentTarget();
        if (target == null) return false;
        
        // Don't flank if already in melee
        float distanceToTarget = Vector3.Distance(unit.transform.position, target.transform.position);
        if (distanceToTarget <= 2f) return false;
        
        // Check if flanking position is available
        Vector3 targetDirection = (target.transform.position - unit.transform.position).normalized;
        Vector3 flankDirection = Vector3.Cross(targetDirection, Vector3.up);
        Vector3 flankPosition = target.transform.position + flankDirection * 3f;
        
        return ai.IsPositionClear(flankPosition);
    }
}

/// <summary>
/// Check if unit is in attack range
/// </summary>
public class BTCheckInRange : BTCondition
{
    public BTCheckInRange(CombatUnit unit, BattleAI ai) : base(unit, ai) { }
    
    protected override bool CheckCondition()
    {
        var target = ai.GetCurrentTarget();
        if (target == null) return false;
        
        float distance = Vector3.Distance(unit.transform.position, target.transform.position);
        return distance <= unit.battleAttackRange;
    }
}

/// <summary>
/// Check if unit has a target
/// </summary>
public class BTCheckHasTarget : BTCondition
{
    public BTCheckHasTarget(CombatUnit unit, BattleAI ai) : base(unit, ai) { }
    
    protected override bool CheckCondition()
    {
        return ai.GetCurrentTarget() != null;
    }
}

// ===== ACTION NODES =====

/// <summary>
/// Retreat action
/// </summary>
public class BTActionRetreat : BTAction
{
    public BTActionRetreat(CombatUnit unit, BattleAI ai) : base(unit, ai) { }
    
    protected override bool ExecuteAction()
    {
        ai.ExecuteRetreat();
        return true; // Retreat is always successful
    }
}

/// <summary>
/// Regroup action
/// </summary>
public class BTActionRegroup : BTAction
{
    public BTActionRegroup(CombatUnit unit, BattleAI ai) : base(unit, ai) { }
    
    protected override bool ExecuteAction()
    {
        ai.ExecuteRegroup();
        return true; // Regroup is always successful
    }
}

/// <summary>
/// Flank action
/// </summary>
public class BTActionFlank : BTAction
{
    public BTActionFlank(CombatUnit unit, BattleAI ai) : base(unit, ai) { }
    
    protected override bool ExecuteAction()
    {
        ai.ExecuteFlank();
        return true; // Flank is always successful
    }
}

/// <summary>
/// Attack action
/// </summary>
public class BTActionAttack : BTAction
{
    public BTActionAttack(CombatUnit unit, BattleAI ai) : base(unit, ai) { }
    
    protected override bool ExecuteAction()
    {
        ai.ExecuteAttack();
        return true; // Attack is always successful
    }
}

/// <summary>
/// Advance action
/// </summary>
public class BTActionAdvance : BTAction
{
    public BTActionAdvance(CombatUnit unit, BattleAI ai) : base(unit, ai) { }
    
    protected override bool ExecuteAction()
    {
        ai.ExecuteAdvance();
        return true; // Advance is always successful
    }
}

/// <summary>
/// Defend action
/// </summary>
public class BTActionDefend : BTAction
{
    public BTActionDefend(CombatUnit unit, BattleAI ai) : base(unit, ai) { }
    
    protected override bool ExecuteAction()
    {
        ai.ExecuteDefend();
        return true; // Defend is always successful
    }
}
