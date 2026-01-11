using System;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class SeasonalTextureEntry
{
    public Biome biome;
    public Season season;
    public Texture2D seasonalAlbedo;
    public Texture2D seasonalNormal;
}

public class ClimateManager : MonoBehaviour
{
    public static ClimateManager Instance { get; private set; }
    public static event Action<int, Season> OnPlanetSeasonChanged;

    [Header("Season Configuration")]
    public int turnsPerSeason = 3;
    public Season currentSeason = Season.Spring;

    [Header("Debug")]
    public bool forceSeasonChange = false;
    public Season debugTargetSeason = Season.Winter;

    [Header("Seasonal Textures")]
    public List<SeasonalTextureEntry> seasonalTextures = new List<SeasonalTextureEntry>();
    private Dictionary<Biome, Dictionary<Season, (Texture2D albedo, Texture2D normal)>> seasonalTextureLookup = new();

    [Header("Multi-Planet Support")]
    [Tooltip("This global ClimateManager handles climate for all planets in the solar system")]
    public bool isGlobalClimateManager = true;
    public int planetIndex = 0;
    
    // Per-planet climate data
    private Dictionary<int, Season> planetSeasons = new Dictionary<int, Season>();
    private Dictionary<int, int> planetSeasonStartTurns = new Dictionary<int, int>();

    private int currentTurn = 0;
    private int seasonStartTurn = 0;

    private PlanetGenerator planet;

    [Header("Winter Attrition")]
    [Tooltip("HP damage applied to exposed units each turn during Winter")]
    public int winterAttritionDamage = 1;
    [Tooltip("If true, apply winter attrition to units that are not sheltered")]
    public bool enableWinterAttrition = true;

    public event Action<Season> OnSeasonChanged;

    void Awake()
    {
        if (isGlobalClimateManager)
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        BuildSeasonalTextureLookup();
        
        // Initialize climate data for all planets when in multi-planet mode
        if (isGlobalClimateManager)
        {
            if (GameManager.Instance?.enableMultiPlanetSystem == true)
            {
                InitializeMultiPlanetClimate();
            }
        }
    }

    void Start()
    {
        if (isGlobalClimateManager)
        {
            UpdateReferences();

            if (TurnManager.Instance != null)
            {
                TurnManager.Instance.OnTurnChanged += HandleTurnChanged;
            }
            else
            {
                Debug.LogWarning("[ClimateManager] Could not find TurnManager to subscribe to turn changes.");
            }

            CheckSeasonChange();
        }
        else
        {
            if (Instance != null)
            {
                OnPlanetSeasonChanged += HandlePlanetSeasonChanged;
            }
        }
    }

    void OnDestroy()
    {
        if (isGlobalClimateManager)
        {
            if (TurnManager.Instance != null)
            {
                TurnManager.Instance.OnTurnChanged -= HandleTurnChanged;
            }
        }
        else
        {
            OnPlanetSeasonChanged -= HandlePlanetSeasonChanged;
        }
    }

    private void HandleTurnChanged(Civilization civ, int turnNumber)
    {
        currentTurn = turnNumber;
        CheckSeasonChange();
    }

    private void CheckSeasonChange()
    {
        if (turnsPerSeason <= 0) return;

        if (GameManager.Instance?.enableMultiPlanetSystem == true)
        {
            // Check season changes for all planets
            CheckMultiPlanetSeasonChanges();
        }
        else
        {
            // Single planet mode
            CheckSinglePlanetSeasonChange();
        }
    }

    private void CheckSinglePlanetSeasonChange()
    {
        if (currentTurn - seasonStartTurn >= turnsPerSeason || forceSeasonChange)
        {
            seasonStartTurn = currentTurn;
            currentSeason = forceSeasonChange ? debugTargetSeason : GetNextSeason(currentSeason);
            forceSeasonChange = false;

            ApplySeasonalEffects(currentSeason, 0); // Planet index 0 for single planet
        }
    }

    private void CheckMultiPlanetSeasonChanges()
    {
        var planetData = GameManager.Instance.GetPlanetData();
        foreach (var kvp in planetData)
        {
            int planetIndex = kvp.Key;
            
            if (!planetSeasons.ContainsKey(planetIndex))
            {
                planetSeasons[planetIndex] = Season.Spring;
                planetSeasonStartTurns[planetIndex] = 0;
            }

            int seasonStart = planetSeasonStartTurns[planetIndex];
            if (currentTurn - seasonStart >= turnsPerSeason || forceSeasonChange)
            {
                planetSeasonStartTurns[planetIndex] = currentTurn;
                var newSeason = forceSeasonChange ? debugTargetSeason : GetNextSeason(planetSeasons[planetIndex]);
                planetSeasons[planetIndex] = newSeason;
                
                ApplySeasonalEffects(newSeason, planetIndex);
            }
        }
        
        if (forceSeasonChange)
        {
            forceSeasonChange = false;
        }
    }

    private void InitializeMultiPlanetClimate()
    {
planetSeasons.Clear();
        planetSeasonStartTurns.Clear();
        
        // Climate data will be initialized per-planet as they become available
    }

    private Season GetNextSeason(Season current)
    {
        return current switch
        {
            Season.Spring => Season.Summer,
            Season.Summer => Season.Autumn,
            Season.Autumn => Season.Winter,
            Season.Winter => Season.Spring,
            _ => Season.Spring,
        };
    }

    private void ApplySeasonalEffects(Season season, int planetIndex = 0)
    {
OnSeasonChanged?.Invoke(season);
        OnPlanetSeasonChanged?.Invoke(planetIndex, season);

        if (season == Season.Winter)
        {
            ApplyWinterMovementPenalty(planetIndex);
            if (enableWinterAttrition)
            {
                ApplyWinterAttrition(planetIndex);
            }
        }
        else
        {
            RemoveWinterMovementPenalty(planetIndex);
        }
    }

    // Apply HP damage to units that are outdoors (not in shelter) during winter
    private void ApplyWinterAttrition(int planetIndex = 0)
    {
        if (winterAttritionDamage <= 0) return;

        // Combat units
        foreach (var unit in UnitRegistry.GetCombatUnits())
        {
            try
            {
                if (unit == null) continue;
                if (!unit.takesWeatherDamage) continue;
                
                // Skip units not on this planet
                if (!IsUnitOnPlanet(unit, planetIndex)) continue;
                
                int idx = unit.currentTileIndex;
                if (idx < 0) continue;

                var tileData = TileSystem.Instance != null ? TileSystem.Instance.GetTileData(idx) : null;
                if (tileData == null) continue;

                bool sheltered = tileData.improvement != null && tileData.improvement.isShelter;
                if (!sheltered)
                {
                    unit.ApplyDamage(winterAttritionDamage);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[ClimateManager] Winter attrition (combat) failed: {ex.Message}");
            }
        }

        // Worker units
        foreach (var worker in UnitRegistry.GetWorkerUnits())
        {
            try
            {
                if (worker == null) continue;
                if (!worker.takesWeatherDamage) continue;
                
                // Skip units not on this planet
                if (!IsUnitOnPlanet(worker, planetIndex)) continue;
                
                int idx = worker.currentTileIndex;
                if (idx < 0) continue;

                var tileData = TileSystem.Instance != null ? TileSystem.Instance.GetTileData(idx) : null;
                if (tileData == null) continue;

                bool sheltered = tileData.improvement != null && tileData.improvement.isShelter;
                if (!sheltered)
                {
                    // Apply attrition damage to workers
                    worker.ApplyDamage(winterAttritionDamage);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[ClimateManager] Winter attrition (worker) failed: {ex.Message}");
            }
        }
    }

    public (Texture2D albedo, Texture2D normal) GetSeasonalTexturesForBiome(Biome biome, int planetIndex = 0)
    {
        Season seasonToUse = GetSeasonForPlanet(planetIndex);
        
        if (seasonalTextureLookup.TryGetValue(biome, out var seasonTextures))
        {
            if (seasonTextures.TryGetValue(seasonToUse, out var textures))
            {
                return textures;
            }
        }
        return (null, null);
    }

    /// <summary>
    /// Get the current season for a specific planet
    /// </summary>
    public Season GetSeasonForPlanet(int planetIndex = 0)
    {
        if (GameManager.Instance?.enableMultiPlanetSystem == true)
        {
            return planetSeasons.TryGetValue(planetIndex, out var season) ? season : Season.Spring;
        }
        else
        {
            return currentSeason;
        }
    }

    private void ApplyWinterMovementPenalty(int planetIndex = 0)
    {
        foreach (var unit in UnitRegistry.GetCombatUnits())
        {
            if (IsUnitOnPlanet(unit, planetIndex) && !unit.hasWinterPenalty)
            {
                unit.hasWinterPenalty = true;
                // Movement points removed - winter penalty now affects movement speed via fatigue
            }
        }

        foreach (var worker in UnitRegistry.GetWorkerUnits())
        {
            if (IsUnitOnPlanet(worker, planetIndex) && !worker.hasWinterPenalty)
            {
                worker.hasWinterPenalty = true;
                // Movement points removed - winter penalty now affects movement speed via fatigue
            }
        }
    }

    private void RemoveWinterMovementPenalty(int planetIndex = 0)
    {
        foreach (var unit in UnitRegistry.GetCombatUnits())
        {
            if (IsUnitOnPlanet(unit, planetIndex))
            {
                unit.hasWinterPenalty = false;
            }
        }

        foreach (var worker in UnitRegistry.GetWorkerUnits())
        {
            if (IsUnitOnPlanet(worker, planetIndex))
            {
                worker.hasWinterPenalty = false;
            }
        }
    }

    // Helper method to check if a unit is on a specific planet
    private bool IsUnitOnPlanet(object unit, int planetIndex)
    {
        // For single planet mode, always return true
        if (!GameManager.Instance.enableMultiPlanetSystem || planetIndex == 0)
        {
            return true;
        }
        
        // TODO: Implement actual planet checking logic when units have planet tracking
        // For now, assume all units are on the current planet
        return GameManager.Instance.currentPlanetIndex == planetIndex;
    }

    public void SimulateClimateChange(float temperatureChange, float timescale)
    {
// Placeholder for future systems
    }

    private void BuildSeasonalTextureLookup()
    {
        seasonalTextureLookup.Clear();
        foreach (var entry in seasonalTextures)
        {
            if (!seasonalTextureLookup.ContainsKey(entry.biome))
            {
                seasonalTextureLookup[entry.biome] = new Dictionary<Season, (Texture2D, Texture2D)>();
            }
            seasonalTextureLookup[entry.biome][entry.season] = (entry.seasonalAlbedo, entry.seasonalNormal);
        }
    }

    private void UpdateReferences()
    {
        // Global ClimateManager doesn't need a specific planet reference
        // It manages climate for all planets in the system
        if (GameManager.Instance?.enableMultiPlanetSystem == true)
        {
}
        else
        {
            // Single planet mode - use current planet generator
            planet = GameManager.Instance?.GetCurrentPlanetGenerator();
            if (planet == null)
            {
                Debug.LogError("[ClimateManager] Could not find PlanetGenerator in scene!");
            }
        }
    }

    private void HandlePlanetSeasonChanged(int idx, Season season)
    {
        if (idx == planetIndex)
        {
            OnSeasonChanged?.Invoke(season);
        }
    }
}
