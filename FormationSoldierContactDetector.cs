using UnityEngine;

/// <summary>
/// Component attached to formation soldiers to track enemy contacts via trigger colliders
/// Replaces distance-based contact detection with collision-based system
/// </summary>
public class FormationSoldierContactDetector : MonoBehaviour
{
    private FormationUnit formation;
    private GameObject soldier;
    
    public void Initialize(FormationUnit formationUnit, GameObject soldierObject)
    {
        formation = formationUnit;
        soldier = soldierObject;
    }
    
    void OnTriggerEnter(Collider other)
    {
        Debug.Log($"[ContactDetector] OnTriggerEnter called: {soldier?.name ?? "null"} detected {other?.gameObject?.name ?? "null"}");
        
        if (formation == null || soldier == null)
        {
            Debug.LogWarning($"[ContactDetector] Formation or soldier is null!");
            return;
        }
        
        // Get the GameObject that owns this collider (could be the collider itself or its parent)
        GameObject enemySoldier = other.gameObject;
        
        // Check if the other collider belongs to an enemy soldier
        var otherCombatUnit = other.GetComponent<CombatUnit>();
        if (otherCombatUnit == null)
        {
            // Try parent if collider is on child object
            otherCombatUnit = other.GetComponentInParent<CombatUnit>();
            if (otherCombatUnit != null)
            {
                enemySoldier = otherCombatUnit.gameObject;
                Debug.Log($"[ContactDetector] Found CombatUnit in parent: {enemySoldier.name}");
            }
            else
            {
                Debug.Log($"[ContactDetector] {soldier.name} detected {other.gameObject.name} but no CombatUnit found (ignoring)");
                return; // Not a combat unit
            }
        }
        
        // Find which formation this enemy belongs to
        FormationUnit enemyFormation = formation.FindFormationForSoldier(enemySoldier);
        if (enemyFormation == null)
        {
            Debug.Log($"[ContactDetector] {soldier.name} detected {enemySoldier.name} but couldn't find enemy formation");
            return;
        }
        
        // Check if it's actually an enemy (different attacker/defender status)
        if (enemyFormation.isAttacker == formation.isAttacker)
        {
            Debug.Log($"[ContactDetector] {soldier.name} detected {enemySoldier.name} but same team (ignoring)");
            return; // Same team, ignore
        }
        
        // Add to contact list
        formation.AddSoldierContact(soldier, enemySoldier);
        Debug.Log($"[ContactDetector] {soldier.name} ({formation.formationName}) detected enemy contact: {enemySoldier.name} ({enemyFormation.formationName})");
        
        // Trigger combat if not already in combat
        if (!formation.isInCombat)
        {
            Debug.Log($"[ContactDetector] Starting combat between {formation.formationName} and {enemyFormation.formationName}");
            formation.StartCombatWithFormation(enemyFormation);
        }
    }
    
    void OnTriggerExit(Collider other)
    {
        if (formation == null || soldier == null) return;
        
        // Get the GameObject that owns this collider
        GameObject enemySoldier = other.gameObject;
        var otherCombatUnit = other.GetComponent<CombatUnit>();
        if (otherCombatUnit == null)
        {
            otherCombatUnit = other.GetComponentInParent<CombatUnit>();
            if (otherCombatUnit != null)
            {
                enemySoldier = otherCombatUnit.gameObject;
            }
            else
            {
                return;
            }
        }
        
        // Remove from contact list when enemy leaves range
        formation.RemoveSoldierContact(soldier, enemySoldier);
    }
}

