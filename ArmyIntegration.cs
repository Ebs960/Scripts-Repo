using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Integration helper to automatically manage armies when units are created
/// When units are spawned, they are automatically added to armies
/// </summary>
public static class ArmyIntegration
{
    /// <summary>
    /// Called when a new combat unit is created - automatically adds to army
    /// </summary>
    public static void OnUnitCreated(CombatUnit unit, int tileIndex)
    {
        if (unit == null || ArmyManager.Instance == null) return;
        
        // Find existing army at this tile
        var armiesAtTile = ArmyManager.Instance.GetArmiesAtTile(tileIndex);
        var friendlyArmy = armiesAtTile.FirstOrDefault(a => a != null && a.owner == unit.owner);
        
        if (friendlyArmy != null)
        {
            // Add to existing army
            friendlyArmy.AddUnit(unit);
            Debug.Log($"[ArmyIntegration] Added {unit.data.unitName} to existing army {friendlyArmy.armyName} at tile {tileIndex}");
        }
        else
        {
            // Check for nearby armies (within 1 tile)
            var nearbyArmy = FindNearbyArmy(unit.owner, tileIndex);
            if (nearbyArmy != null && nearbyArmy.units.Count < nearbyArmy.maxUnits)
            {
                // Add to nearby army
                nearbyArmy.AddUnit(unit);
                // Move unit to army's tile
                unit.currentTileIndex = nearbyArmy.currentTileIndex;
                if (TileSystem.Instance != null)
                {
                    Vector3 armyPos = TileSystem.Instance.GetTileCenterFlat(nearbyArmy.currentTileIndex);
                    unit.transform.position = armyPos;
                }
                Debug.Log($"[ArmyIntegration] Added {unit.data.unitName} to nearby army {nearbyArmy.armyName}");
            }
            else
            {
                // Create new army with just this unit
                var newArmy = ArmyManager.Instance.CreateArmy(new List<CombatUnit> { unit }, unit.owner);
                if (newArmy != null)
                {
                    newArmy.MoveToTile(tileIndex);
                    Debug.Log($"[ArmyIntegration] Created new army {newArmy.armyName} with {unit.data.unitName}");
                }
            }
        }
    }
    
    /// <summary>
    /// Find a nearby army (within 1 tile) that can accept this unit
    /// </summary>
    private static Army FindNearbyArmy(Civilization owner, int tileIndex)
    {
        if (TileSystem.Instance == null) return null;
        
        // Check current tile and neighbors
        var tilesToCheck = new List<int> { tileIndex };
        tilesToCheck.AddRange(TileSystem.Instance.GetNeighbors(tileIndex));
        
        foreach (int tile in tilesToCheck)
        {
            var armiesAtTile = ArmyManager.Instance.GetArmiesAtTile(tile);
            var friendlyArmy = armiesAtTile.FirstOrDefault(a => 
                a != null && 
                a.owner == owner && 
                a.units.Count < a.maxUnits);
            
            if (friendlyArmy != null)
            {
                return friendlyArmy;
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Remove unit from its army (when unit is destroyed or removed)
    /// </summary>
    public static void OnUnitDestroyed(CombatUnit unit)
    {
        if (unit == null || ArmyManager.Instance == null) return;
        
        // Find army containing this unit
        var allArmies = ArmyManager.Instance.GetAllArmies();
        foreach (var army in allArmies)
        {
            if (army != null && army.units.Contains(unit))
            {
                army.RemoveUnit(unit);
                
                // If army is now empty or destroyed, destroy it
                if (army.IsDestroyed())
                {
                    ArmyManager.Instance.DestroyArmy(army);
                }
                break;
            }
        }
    }
}

