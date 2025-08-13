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
    [Tooltip("Planet index this climate manager is responsible for")]
    public int planetIndex = 0;

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
        // For multi-planet systems, don't enforce singleton pattern
        if (GameManager.Instance?.enableMultiPlanetSystem == true)
        {
            // Just set up this instance without singleton enforcement
            BuildSeasonalTextureLookup();
            return;
        }

        // Traditional singleton behavior for single planet mode
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        BuildSeasonalTextureLookup();
    }

    void Start()
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

    void OnDestroy()
    {
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.OnTurnChanged -= HandleTurnChanged;
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

        if (currentTurn - seasonStartTurn >= turnsPerSeason || forceSeasonChange)
        {
            seasonStartTurn = currentTurn;
            currentSeason = forceSeasonChange ? debugTargetSeason : GetNextSeason(currentSeason);
            forceSeasonChange = false;

            ApplySeasonalEffects(currentSeason);
        }
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

    private void ApplySeasonalEffects(Season season)
    {
        Debug.Log($"[ClimateManager] Changing to {season} season");

        OnSeasonChanged?.Invoke(season);

        if (season == Season.Winter)
        {
            ApplyWinterMovementPenalty();
            if (enableWinterAttrition)
            {
                ApplyWinterAttrition();
            }
        }
        else
        {
            RemoveWinterMovementPenalty();
        }
    }

    // Apply HP damage to units that are outdoors (not in shelter) during winter
    private void ApplyWinterAttrition()
    {
        if (winterAttritionDamage <= 0) return;

        // Combat units
        foreach (var unit in UnitRegistry.GetCombatUnits())
        {
            try
            {
                if (unit == null) continue;
                if (!unit.takesWeatherDamage) continue;
                int idx = unit.currentTileIndex;
                if (idx < 0) continue;

                var (tileData, isMoon) = TileDataHelper.Instance.GetTileData(idx);
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
                int idx = worker.currentTileIndex;
                if (idx < 0) continue;

                var (tileData, isMoon) = TileDataHelper.Instance.GetTileData(idx);
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

    public (Texture2D albedo, Texture2D normal) GetSeasonalTexturesForBiome(Biome biome)
    {
        if (seasonalTextureLookup.TryGetValue(biome, out var seasonTextures))
        {
            if (seasonTextures.TryGetValue(currentSeason, out var textures))
            {
                return textures;
            }
        }
        return (null, null);
    }

    private void ApplyWinterMovementPenalty()
    {
        foreach (var unit in UnitRegistry.GetCombatUnits())
        {
            if (!unit.hasWinterPenalty)
            {
                unit.hasWinterPenalty = true;
                unit.DeductMovementPoints(1);
            }
        }

        foreach (var worker in UnitRegistry.GetWorkerUnits())
        {
            if (!worker.hasWinterPenalty)
            {
                worker.hasWinterPenalty = true;
                worker.DeductMovementPoints(1);
            }
        }
    }

    private void RemoveWinterMovementPenalty()
    {
        foreach (var unit in UnitRegistry.GetCombatUnits())
        {
            unit.hasWinterPenalty = false;
        }

        foreach (var worker in UnitRegistry.GetWorkerUnits())
        {
            worker.hasWinterPenalty = false;
        }
    }

    public void SimulateClimateChange(float temperatureChange, float timescale)
    {
        Debug.Log($"Simulating climate change: {temperatureChange} degrees over {timescale} years...");
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
                    // Use GameManager API for multi-planet support
            planet = GameManager.Instance?.GetCurrentPlanetGenerator();
        if (planet == null)
        {
            Debug.LogError("[ClimateManager] Could not find PlanetGenerator in scene!");
        }
    }
}
