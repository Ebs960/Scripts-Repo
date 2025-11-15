using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Army system - groups combat units together for campaign map movement (Total War style)
/// Armies move as a single entity and enter battle when they meet enemy armies
/// </summary>
public class Army : MonoBehaviour
{
    [Header("Army Identity")]
    public string armyName;
    public Civilization owner;
    public int armyId; // Unique ID for this army
    
    [Header("Army Composition")]
    [Tooltip("Maximum number of units this army can contain")]
    [Range(1, 40)]
    public int maxUnits = 20; // Default: 20 units per army
    [Tooltip("Current units in this army")]
    public List<CombatUnit> units = new List<CombatUnit>();
    
    [Header("Commander/General")]
    [Tooltip("Optional: General unit that provides leadership bonuses")]
    public CombatUnit general;
    [Tooltip("Leadership bonus multiplier from general (1.0 = no bonus, 1.2 = +20%)")]
    [Range(1.0f, 2.0f)]
    public float leadershipBonus = 1.0f;
    
    [Header("Campaign Map Position")]
    [Tooltip("Current tile index on campaign map")]
    public int currentTileIndex = -1;
    [Tooltip("Movement speed of the army (based on slowest unit)")]
    public float armyMoveSpeed = 3f;
    
    [Header("Movement Points")]
    [Tooltip("Base movement points per turn (default: 2 tiles)")]
    public int baseMovePoints = 2;
    [Tooltip("Current movement points remaining this turn")]
    public int currentMovePoints = 2;
    
    [Header("Army Stats (Aggregated)")]
    [Tooltip("Total attack power of all units")]
    public int totalAttack;
    [Tooltip("Total defense power of all units")]
    public int totalDefense;
    [Tooltip("Total health of all units")]
    public int totalHealth;
    [Tooltip("Current health of all units")]
    public int currentHealth;
    [Tooltip("Total soldier count across all units")]
    public int totalSoldierCount;
    [Tooltip("Maximum soldier count across all units")]
    public int maxSoldierCount;
    [Tooltip("Average morale of all units")]
    public int averageMorale;
    
    [Header("Visual Representation")]
    [Tooltip("Visual representation of the army on campaign map (optional)")]
    public GameObject armyVisual;
    [Tooltip("Icon/sprite to represent this army")]
    public Sprite armyIcon;
    
    // Events
    public System.Action<Army> OnArmyDestroyed;
    public System.Action<CombatUnit> OnUnitAdded;
    public System.Action<CombatUnit> OnUnitRemoved;
    
    private static int nextArmyId = 1;
    
    void Awake()
    {
        if (armyId == 0)
        {
            armyId = nextArmyId++;
        }
        
        if (string.IsNullOrEmpty(armyName))
        {
            armyName = $"Army {armyId}";
        }
    }
    
    void Start()
    {
        UpdateArmyStats();
        UpdateArmyMoveSpeed();
        UpdateArmyMovePoints();
        currentMovePoints = baseMovePoints; // Initialize
        CreateArmyVisual();
    }
    
    /// <summary>
    /// Add a unit to this army
    /// </summary>
    public bool AddUnit(CombatUnit unit)
    {
        if (unit == null) return false;
        if (units.Count >= maxUnits)
        {
            Debug.LogWarning($"[Army] {armyName} is full! Cannot add {unit.data.unitName}");
            return false;
        }
        if (units.Contains(unit))
        {
            Debug.LogWarning($"[Army] {armyName} already contains {unit.data.unitName}");
            return false;
        }
        if (unit.owner != owner)
        {
            Debug.LogWarning($"[Army] Cannot add unit from different civilization!");
            return false;
        }
        
        units.Add(unit);
        
        // Set unit's parent to army (for organization)
        unit.transform.SetParent(transform);
        
        // Hide unit on campaign map (army represents it)
        unit.gameObject.SetActive(false);
        
        // Update army stats
        UpdateArmyStats();
        UpdateArmyMoveSpeed();
        UpdateArmyMovePoints();
        
        OnUnitAdded?.Invoke(unit);
        Debug.Log($"[Army] {armyName} added {unit.data.unitName} ({units.Count}/{maxUnits} units)");
        
        return true;
    }
    
    /// <summary>
    /// Remove a unit from this army
    /// </summary>
    public bool RemoveUnit(CombatUnit unit)
    {
        if (unit == null || !units.Contains(unit)) return false;
        
        units.Remove(unit);
        unit.transform.SetParent(null);
        
        // Show unit on campaign map if army is destroyed
        if (units.Count == 0)
        {
            unit.gameObject.SetActive(true);
        }
        
        UpdateArmyStats();
        UpdateArmyMoveSpeed();
        UpdateArmyMovePoints();
        
        OnUnitRemoved?.Invoke(unit);
        Debug.Log($"[Army] {armyName} removed {unit.data.unitName} ({units.Count}/{maxUnits} units)");
        
        return true;
    }
    
    /// <summary>
    /// Set the general/commander for this army
    /// </summary>
    public void SetGeneral(CombatUnit generalUnit)
    {
        if (generalUnit != null && !units.Contains(generalUnit))
        {
            Debug.LogWarning($"[Army] General must be part of the army! Adding {generalUnit.data.unitName} first.");
            AddUnit(generalUnit);
        }
        
        general = generalUnit;
        
        // Calculate leadership bonus based on general's level/stats
        if (general != null)
        {
            leadershipBonus = 1.0f + (general.level * 0.05f); // +5% per level, max 2.0x
            leadershipBonus = Mathf.Clamp(leadershipBonus, 1.0f, 2.0f);
        }
        else
        {
            leadershipBonus = 1.0f;
        }
        
        UpdateArmyStats();
    }
    
    /// <summary>
    /// Update aggregated army stats from all units
    /// </summary>
    public void UpdateArmyStats()
    {
        totalAttack = 0;
        totalDefense = 0;
        totalHealth = 0;
        currentHealth = 0;
        totalSoldierCount = 0;
        maxSoldierCount = 0;
        int moraleSum = 0;
        int count = 0;
        
        foreach (var unit in units)
        {
            if (unit == null) continue;
            
            totalAttack += unit.CurrentAttack;
            totalDefense += unit.CurrentDefense;
            totalHealth += unit.MaxHealth;
            currentHealth += unit.currentHealth;
            totalSoldierCount += unit.soldierCount;
            maxSoldierCount += unit.maxSoldierCount;
            moraleSum += unit.currentMorale;
            count++;
        }
        
        averageMorale = count > 0 ? moraleSum / count : 0;
        
        // Apply leadership bonus if general exists
        if (general != null && leadershipBonus > 1.0f)
        {
            totalAttack = Mathf.RoundToInt(totalAttack * leadershipBonus);
            totalDefense = Mathf.RoundToInt(totalDefense * leadershipBonus);
            averageMorale = Mathf.RoundToInt(averageMorale * leadershipBonus);
        }
        
        // Apply equipment bonuses from all units
        var equipmentBonuses = AggregateEquipmentBonuses();
        totalAttack += equipmentBonuses.attackAdd;
        totalDefense += equipmentBonuses.defenseAdd;
        totalHealth += equipmentBonuses.healthAdd;
        averageMorale += equipmentBonuses.moraleAdd;
        
        // Apply percentage bonuses
        totalAttack = Mathf.RoundToInt(totalAttack * (1f + equipmentBonuses.attackPct));
        totalDefense = Mathf.RoundToInt(totalDefense * (1f + equipmentBonuses.defensePct));
        totalHealth = Mathf.RoundToInt(totalHealth * (1f + equipmentBonuses.healthPct));
        averageMorale = Mathf.RoundToInt(averageMorale * (1f + equipmentBonuses.moralePct));
        
        // Also apply tech/culture army bonuses to stats
        if (owner != null)
        {
            var armyBonuses = owner.AggregateArmyBonuses();
            totalAttack += armyBonuses.attackAdd;
            totalDefense += armyBonuses.defenseAdd;
            totalHealth += armyBonuses.healthAdd;
            averageMorale += armyBonuses.moraleAdd;
            
            totalAttack = Mathf.RoundToInt(totalAttack * (1f + armyBonuses.attackPct));
            totalDefense = Mathf.RoundToInt(totalDefense * (1f + armyBonuses.defensePct));
            totalHealth = Mathf.RoundToInt(totalHealth * (1f + armyBonuses.healthPct));
            averageMorale = Mathf.RoundToInt(averageMorale * (1f + armyBonuses.moralePct));
        }
    }
    
    /// <summary>
    /// Update army movement speed based on slowest unit (fatigue-adjusted)
    /// </summary>
    public void UpdateArmyMoveSpeed()
    {
        if (units.Count == 0)
        {
            armyMoveSpeed = 3f; // Default speed
            return;
        }
        
        float slowestSpeed = float.MaxValue;
        
        foreach (var unit in units)
        {
            if (unit == null) continue;
            float effectiveSpeed = unit.EffectiveMoveSpeed;
            if (effectiveSpeed < slowestSpeed)
            {
                slowestSpeed = effectiveSpeed;
            }
        }
        
        armyMoveSpeed = slowestSpeed > 0 ? slowestSpeed : 3f; // Fallback to default
    }
    
    /// <summary>
    /// Update army base movement points based on unit composition
    /// Armies typically move 2-3 tiles per turn (based on slowest unit type)
    /// </summary>
    public void UpdateArmyMovePoints()
    {
        if (units.Count == 0)
        {
            baseMovePoints = 2; // Default: 2 tiles per turn
            return;
        }
        
        // Calculate based on slowest unit type
        // Most armies move 2 tiles per turn, fast armies (all cavalry) move 3
        int slowestMovePoints = int.MaxValue;
        bool hasFastUnits = false;
        
        foreach (var unit in units)
        {
            if (unit == null || unit.data == null) continue;
            
            // Estimate movement capability from unit type
            // Cavalry units typically have higher movement
            bool isCavalry = unit.data.unitType == CombatCategory.Cavalry || 
                            unit.data.unitType == CombatCategory.HeavyCavalry ||
                            unit.data.unitType == CombatCategory.RangedCavalry ||
                            unit.data.unitType == CombatCategory.Dragoon;
            
            if (isCavalry)
            {
                hasFastUnits = true;
            }
            
            // Base movement estimate: most units = 2, cavalry = 3
            int unitMoveEstimate = isCavalry ? 3 : 2;
            if (unitMoveEstimate < slowestMovePoints)
            {
                slowestMovePoints = unitMoveEstimate;
            }
        }
        
        // Base movement: If all units are fast (cavalry), army moves 3 tiles, otherwise 2
        int baseMove = (hasFastUnits && slowestMovePoints >= 3) ? 3 : Mathf.Max(2, slowestMovePoints);
        
        // Apply bonuses from techs, cultures, etc.
        if (owner != null)
        {
            var armyBonuses = owner.AggregateArmyBonuses();
            
            // Apply additive bonuses
            baseMove += armyBonuses.movePointsAdd;
            
            // Apply percentage bonuses
            baseMove = Mathf.RoundToInt(baseMove * (1f + armyBonuses.movePointsPct));
            
            // Apply civ data movement bonus (percentage)
            if (owner.civData != null)
            {
                baseMove = Mathf.RoundToInt(baseMove * (1f + owner.civData.movementBonus));
            }
        }
        
        // Apply equipment bonuses from all units in the army
        var equipmentBonuses = AggregateEquipmentBonuses();
        baseMove += equipmentBonuses.movePointsAdd;
        baseMove = Mathf.RoundToInt(baseMove * (1f + equipmentBonuses.movePointsPct));
        
        // Ensure minimum of 1 movement point
        baseMovePoints = Mathf.Max(1, baseMove);
    }
    
    /// <summary>
    /// Aggregate army bonuses from equipment equipped by units in this army
    /// Equipment with armyBonus applies to the entire army
    /// General's equipment is prioritized (counts double)
    /// </summary>
    private ArmyStatBonus AggregateEquipmentBonuses()
    {
        ArmyStatBonus agg = new ArmyStatBonus();
        
        foreach (var unit in units)
        {
            if (unit == null) continue;
            
            // Check if unit has equipment with army bonuses
            // Priority: General's equipment counts double
            bool isGeneral = (unit == general);
            float multiplier = isGeneral ? 2f : 1f;
            
            // Check all equipment slots
            var equipmentToCheck = new System.Collections.Generic.List<EquipmentData>();
            
            // Get equipped items (using reflection or direct access)
            if (unit.equippedWeapon != null) equipmentToCheck.Add(unit.equippedWeapon);
            if (unit.equippedShield != null) equipmentToCheck.Add(unit.equippedShield);
            if (unit.equippedArmor != null) equipmentToCheck.Add(unit.equippedArmor);
            if (unit.equippedMiscellaneous != null) equipmentToCheck.Add(unit.equippedMiscellaneous);
            if (unit.equippedProjectileWeapon != null) equipmentToCheck.Add(unit.equippedProjectileWeapon);
            
            foreach (var equip in equipmentToCheck)
            {
                if (equip == null) continue;
                
                // Check if equipment has army bonus (non-default values)
                var bonus = equip.armyBonus;
                if (bonus == null) continue;
                
                // Skip if all values are zero (no bonus)
                if (bonus.movePointsAdd == 0 && bonus.movePointsPct == 0f &&
                    bonus.attackAdd == 0 && bonus.attackPct == 0f &&
                    bonus.defenseAdd == 0 && bonus.defensePct == 0f &&
                    bonus.healthAdd == 0 && bonus.healthPct == 0f &&
                    bonus.moraleAdd == 0 && bonus.moralePct == 0f)
                {
                    continue;
                }
                agg.movePointsAdd += Mathf.RoundToInt(bonus.movePointsAdd * multiplier);
                agg.attackAdd += Mathf.RoundToInt(bonus.attackAdd * multiplier);
                agg.defenseAdd += Mathf.RoundToInt(bonus.defenseAdd * multiplier);
                agg.healthAdd += Mathf.RoundToInt(bonus.healthAdd * multiplier);
                agg.moraleAdd += Mathf.RoundToInt(bonus.moraleAdd * multiplier);
                
                // Percentage bonuses stack additively (not multiplicatively)
                agg.movePointsPct += bonus.movePointsPct * multiplier;
                agg.attackPct += bonus.attackPct * multiplier;
                agg.defensePct += bonus.defensePct * multiplier;
                agg.healthPct += bonus.healthPct * multiplier;
                agg.moralePct += bonus.moralePct * multiplier;
            }
        }
        
        return agg;
    }
    
    /// <summary>
    /// Reset movement points at start of turn
    /// </summary>
    public void ResetForNewTurn()
    {
        UpdateArmyMovePoints(); // Recalculate in case unit composition changed
        currentMovePoints = baseMovePoints;
    }
    
    /// <summary>
    /// Check if army can move with given movement cost
    /// </summary>
    public bool CanMove(int movementCost)
    {
        return currentMovePoints >= movementCost;
    }
    
    /// <summary>
    /// Deduct movement points after moving
    /// </summary>
    public void DeductMovementPoints(int amount)
    {
        currentMovePoints = Mathf.Max(0, currentMovePoints - amount);
    }
    
    /// <summary>
    /// Check if army can move to a specific tile (checks movement cost)
    /// </summary>
    public bool CanMoveTo(int tileIndex)
    {
        if (TileSystem.Instance == null) return false;
        
        var tileData = TileSystem.Instance.GetTileData(tileIndex);
        if (tileData == null || !tileData.isPassable) return false;
        
        int movementCost = BiomeHelper.GetMovementCost(tileData.biome);
        return CanMove(movementCost);
    }
    
    /// <summary>
    /// Move army to a new tile on campaign map
    /// </summary>
    public void MoveToTile(int tileIndex)
    {
        if (tileIndex < 0) return;
        
        currentTileIndex = tileIndex;
        
        // Update visual position
        if (TileSystem.Instance != null)
        {
            Vector3 worldPos = TileSystem.Instance.GetTileCenter(tileIndex);
            transform.position = worldPos;
        }
        
        // Update army visual position
        UpdateArmyVisualPosition();
    }
    
    /// <summary>
    /// Check if this army can merge with another army
    /// </summary>
    public bool CanMergeWith(Army otherArmy)
    {
        if (otherArmy == null) return false;
        if (otherArmy.owner != owner) return false;
        if (units.Count + otherArmy.units.Count > maxUnits) return false;
        
        return true;
    }
    
    /// <summary>
    /// Merge another army into this one
    /// </summary>
    public void MergeArmy(Army otherArmy)
    {
        if (!CanMergeWith(otherArmy)) return;
        
        // Transfer all units
        var unitsToTransfer = new List<CombatUnit>(otherArmy.units);
        foreach (var unit in unitsToTransfer)
        {
            otherArmy.RemoveUnit(unit);
            AddUnit(unit);
        }
        
        // Destroy the other army
        if (ArmyManager.Instance != null)
        {
            ArmyManager.Instance.DestroyArmy(otherArmy);
        }
        
        Debug.Log($"[Army] {armyName} merged with {otherArmy.armyName}");
    }
    
    /// <summary>
    /// Split this army, creating a new army with specified units
    /// </summary>
    public Army SplitArmy(List<CombatUnit> unitsToSplit)
    {
        if (unitsToSplit == null || unitsToSplit.Count == 0) return null;
        if (unitsToSplit.Count >= units.Count) return null; // Can't split all units
        
        // Create new army
        GameObject newArmyGO = new GameObject($"{armyName} - Detachment");
        Army newArmy = newArmyGO.AddComponent<Army>();
        newArmy.owner = owner;
        newArmy.currentTileIndex = currentTileIndex;
        newArmy.maxUnits = maxUnits;
        
        // Transfer units
        foreach (var unit in unitsToSplit)
        {
            if (units.Contains(unit))
            {
                RemoveUnit(unit);
                newArmy.AddUnit(unit);
            }
        }
        
        // Register with ArmyManager
        if (ArmyManager.Instance != null)
        {
            ArmyManager.Instance.RegisterArmy(newArmy);
        }
        
        Debug.Log($"[Army] {armyName} split into {newArmy.armyName} ({newArmy.units.Count} units)");
        
        return newArmy;
    }
    
    /// <summary>
    /// Get all units ready for battle (returns list of units)
    /// </summary>
    public List<CombatUnit> GetBattleUnits()
    {
        return new List<CombatUnit>(units.Where(u => u != null && u.currentHealth > 0));
    }
    
    /// <summary>
    /// Check if army is destroyed (no units or all units dead)
    /// </summary>
    public bool IsDestroyed()
    {
        return units.Count == 0 || units.All(u => u == null || u.currentHealth <= 0);
    }
    
    /// <summary>
    /// Destroy this army and release all units
    /// </summary>
    public void DestroyArmy()
    {
        // On campaign map, units should stay hidden (they'll be auto-added to new armies)
        // Only show units if we're in a special context (like disbanding)
        bool shouldShowUnits = false; // Default: keep units hidden, they'll be auto-added to armies
        
        // Release all units
        var unitsToRelease = new List<CombatUnit>(units);
        foreach (var unit in unitsToRelease)
        {
            if (unit != null)
            {
                RemoveUnit(unit);
                // Units stay hidden - they'll be automatically added to new armies by ArmyManager.EnforceArmyOnlySystem()
                // Only show if explicitly requested (shouldShowUnits = true)
                if (shouldShowUnits)
                {
                    unit.gameObject.SetActive(true);
                }
            }
        }
        
        // Destroy visual
        if (armyVisual != null)
        {
            Destroy(armyVisual);
        }
        
        OnArmyDestroyed?.Invoke(this);
        
        Debug.Log($"[Army] {armyName} destroyed");
    }
    
    /// <summary>
    /// Create visual representation of army on campaign map
    /// </summary>
    private void CreateArmyVisual()
    {
        if (armyVisual != null) return; // Already created
        
        // Create simple visual (can be replaced with better model later)
        armyVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        armyVisual.name = $"{armyName}_Visual";
        armyVisual.transform.SetParent(transform);
        armyVisual.transform.localPosition = Vector3.zero;
        armyVisual.transform.localScale = new Vector3(1.5f, 0.2f, 1.5f);
        
        // Color based on owner
        var renderer = armyVisual.GetComponent<Renderer>();
        if (renderer != null && owner != null)
        {
            // Use civilization color if available
            renderer.material.color = owner.civData != null ? GetCivColor(owner.civData.civName) : Color.gray;
        }
        
        // Add selection indicator
        var selectionIndicator = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        selectionIndicator.name = "SelectionIndicator";
        selectionIndicator.transform.SetParent(armyVisual.transform);
        selectionIndicator.transform.localPosition = Vector3.up * 0.3f;
        selectionIndicator.transform.localScale = new Vector3(1.8f, 0.1f, 1.8f);
        var selRenderer = selectionIndicator.GetComponent<Renderer>();
        if (selRenderer != null)
        {
            selRenderer.material.color = Color.yellow;
            selRenderer.enabled = false; // Hidden by default
        }
    }
    
    /// <summary>
    /// Update army visual position on campaign map
    /// </summary>
    private void UpdateArmyVisualPosition()
    {
        if (armyVisual == null) return;
        if (TileSystem.Instance == null) return;
        
        Vector3 worldPos = TileSystem.Instance.GetTileCenter(currentTileIndex);
        transform.position = worldPos;
    }
    
    /// <summary>
    /// Get color for civilization (simple hash-based coloring)
    /// </summary>
    private Color GetCivColor(string civName)
    {
        if (string.IsNullOrEmpty(civName)) return Color.gray;
        
        // Simple hash-based color generation
        int hash = civName.GetHashCode();
        float r = ((hash & 0xFF0000) >> 16) / 255f;
        float g = ((hash & 0x00FF00) >> 8) / 255f;
        float b = (hash & 0x0000FF) / 255f;
        
        return new Color(r, g, b, 1f);
    }
    
    void OnDestroy()
    {
        // Clean up when army is destroyed
        if (armyVisual != null)
        {
            Destroy(armyVisual);
        }
    }
}

