using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages unit reinforcement - units recover soldier count over time
/// - 10% per turn when on owned (occupied) territory
/// - 15% per turn when in/inside a shelter improvement
/// - 33% per turn when garrisoned/stored in cities
/// </summary>
public class UnitReinforcementManager : MonoBehaviour
{
    public static UnitReinforcementManager Instance { get; private set; }
    
    [Header("Reinforcement Rates")]
    [Tooltip("Reinforcement rate per turn for units on owned/occupied territory (as percentage)")]
    [Range(0f, 100f)]
    public float reinforcementRateOccupiedTerritory = 10f; // 10% per turn

    [Tooltip("Reinforcement rate per turn for units in/inside shelter improvements (as percentage)")]
    [Range(0f, 100f)]
    public float reinforcementRateInShelter = 15f; // 15% per turn

    [Tooltip("Reinforcement rate per turn for units garrisoned in cities (as percentage)")]
    [Range(0f, 100f)]
    public float reinforcementRateInCity = 33f; // 33% per turn
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    void OnEnable()
    {
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.OnTurnChanged += HandleTurnChanged;
        }
    }

    void OnDisable()
    {
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.OnTurnChanged -= HandleTurnChanged;
        }
    }

    private void HandleTurnChanged(Civilization civ, int round)
    {
        if (civ == null) return;
        // Only process the active civ's units — not all civs in the game
        UpdateGarrisonStatusForCiv(civ);
        ApplyReinforcementForCiv(civ);
    }
    
    /// <summary>
    /// Apply reinforcement to all units at the start of a turn.
    /// Called by GameManager or TurnManager.
    /// </summary>
    public void ApplyReinforcementToAllUnits()
    {
        var allCivs = CivilizationManager.Instance?.GetAllCivs();
        if (allCivs == null) return;
        foreach (var civ in allCivs)
            ApplyReinforcementForCiv(civ);
    }

    private void ApplyReinforcementForCiv(Civilization civ)
    {
        if (civ == null) return;

        if (civ.combatUnits != null)
        {
            foreach (var unit in civ.combatUnits)
            {
                if (unit == null || unit.data == null) continue;
                ApplyReinforcement(unit);
            }
        }

        if (civ.workerUnits != null)
        {
            foreach (var w in civ.workerUnits)
            {
                if (w == null) continue;
                var bu = w as BaseUnit;
                if (bu == null) continue;
                float reinforcementRate = GetReinforcementRate(bu);
                if (reinforcementRate > 0f)
                {
                    int healAmount = Mathf.RoundToInt(bu.MaxHealth * (reinforcementRate / 100f));
                    bu.Heal(healAmount);
                }
            }
        }
    }

    private float GetReinforcementRate(BaseUnit bu)
    {
        if (bu is CombatUnit cu && cu.isGarrisonedInCity)
            return reinforcementRateInCity;
        if (bu.isStored && bu.storedInImprovement != null)
            return reinforcementRateInShelter;
        if (bu.currentTileIndex < 0) return 0f;

        var ts = TileSystem.GetForPlanet(bu.planetIndex) ?? TileSystem.Instance;
        if (ts == null || !ts.IsReady()) return 0f;

        var td = ts.GetTileData(bu.currentTileIndex);
        if (td == null) return 0f;

        if (td.improvement != null && td.improvement.isShelter)
            return reinforcementRateInShelter;
        if (td.owner != null && td.owner == bu.owner)
            return reinforcementRateOccupiedTerritory;
        return 0f;
    }
    
    /// <summary>
    /// Apply reinforcement to a single unit
    /// </summary>
    public void ApplyReinforcement(CombatUnit unit)
    {
        if (unit == null || unit.data == null) return;
        if (unit.currentHealth >= unit.MaxHealth) return; // Already at max
        
        // Determine reinforcement rate based on location
        float reinforcementRate = 0f;

        if (unit.isGarrisonedInCity)
        {
            reinforcementRate = reinforcementRateInCity;
        }
        else if (unit.isStored && unit.storedInImprovement != null)
        {
            // Stored inside a shelter improvement
            reinforcementRate = reinforcementRateInShelter;
        }
        else if (unit.currentTileIndex >= 0)
        {
            var ts = TileSystem.GetForPlanet(unit.planetIndex) ?? TileSystem.Instance;
            if (ts != null && ts.IsReady())
            {
                var td = ts.GetTileData(unit.currentTileIndex);
                if (td != null)
                {
                    if (td.improvement != null && td.improvement.isShelter)
                    {
                        reinforcementRate = reinforcementRateInShelter;
                    }
                    else if (td.owner != null && td.owner == unit.owner)
                    {
                        reinforcementRate = reinforcementRateOccupiedTerritory;
                    }
                }
            }
        }
        
        // Calculate healing amount (percentage of max HP)
        int healAmount = Mathf.RoundToInt(unit.MaxHealth * (reinforcementRate / 100f));
        
        // Apply healing
        int oldHealth = unit.currentHealth;
        unit.Heal(healAmount);
        
        if (unit.currentHealth > oldHealth)
        {
}
    }
    
    /// <summary>
    /// Update garrison status for units based on their tile location.
    /// Should be called when units move or at start of turn.
    /// </summary>
    public void UpdateGarrisonStatus()
    {
        var allCivs = CivilizationManager.Instance?.GetAllCivs();
        if (allCivs == null) return;
        foreach (var civ in allCivs)
            UpdateGarrisonStatusForCiv(civ);
    }

    private void UpdateGarrisonStatusForCiv(Civilization civ)
    {
        if (civ == null || civ.combatUnits == null) return;
        foreach (var unit in civ.combatUnits)
        {
            if (unit == null || unit.currentTileIndex < 0) continue;
            unit.isGarrisonedInCity = IsUnitInCity(unit);
        }
    }
    
    /// <summary>
    /// Check if a unit is currently in a friendly city.
    /// Uses TileData.HasCity + owner check for O(1) lookup instead of searching all cities.
    /// </summary>
    private bool IsUnitInCity(CombatUnit unit)
    {
        if (unit == null || unit.currentTileIndex < 0) return false;
        var ts = TileSystem.GetForPlanet(unit.planetIndex) ?? TileSystem.Instance;
        if (ts == null || !ts.IsReady()) return false;
        
        var tileData = ts.GetTileData(unit.currentTileIndex);
        if (tileData == null) return false;
        
        // TileData.HasCity is already set; check owner matches for friendly garrison
        return tileData.HasCity && tileData.owner == unit.owner;
    }
}

