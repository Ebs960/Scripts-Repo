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

    private int currentTurn = 0;
    private int seasonStartTurn = 0;

    private PlanetGenerator planet;

    public event Action<Season> OnSeasonChanged;

    void Awake()
    {
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
        }
        else
        {
            RemoveWinterMovementPenalty();
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
        planet = FindAnyObjectByType<PlanetGenerator>();
        if (planet == null)
        {
            Debug.LogError("[ClimateManager] Could not find PlanetGenerator in scene!");
        }
    }
}
