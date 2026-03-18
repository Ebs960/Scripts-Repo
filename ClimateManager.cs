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

[System.Serializable]
public class BiomeSeasonResponse
{
    public Biome biome;
    public Season season;

    // Gameplay
    public float yieldMultiplier = 1f;

    // Visual mask values (0–1)
    public float snow;
    public float wet;
    public float dry;
    public Color tint = Color.white;
}

public class ClimateManager : MonoBehaviour
{
    [Header("Biome Visual Database")]
    public BiomeVisualDatabase biomeVisualDatabase;
    public static ClimateManager Instance { get; private set; }
    public static event Action<int, Season> OnPlanetSeasonChanged;

    [Header("Season Configuration")]
    public int turnsPerSeason = 3;
    public Season currentSeason = Season.Spring;

    [Header("Debug")]
    public bool forceSeasonChange = false;
    public Season debugTargetSeason = Season.Winter;
    [Tooltip("Enable verbose climate debug logs (per-biome values). Disable to reduce console spam.")]
    public bool verboseLogs = false;
    [Tooltip("Log a concise message when a planet's season changes.")]
    public bool seasonChangeDebug = true;

    [Header("Seasonal Textures")]
    public List<SeasonalTextureEntry> seasonalTextures = new List<SeasonalTextureEntry>();
    private Dictionary<Biome, Dictionary<Season, (Texture2D albedo, Texture2D normal)>> seasonalTextureLookup = new();

    private Dictionary<(Biome, Season), BiomeSeasonResponse> seasonResponseLookup = new();

    // Per-tile precomputed season responses (cached for performance).
    // Index: tileIndex -> array of 4 BiomeSeasonResponse (indexed by (int)season)
    private Dictionary<int, BiomeSeasonResponse[]> tileSeasonCache = new();

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

    [Header("Drought Settings")]
    [Tooltip("Chance (0-1) that a planet experiences a drought when Summer starts")]
    [Range(0f,1f)] public float summerDroughtChance = 0.25f;
    [Tooltip("Default drought severity applied to food yields when a drought occurs (0.35 = -35% food)")]
    [Range(0f,1f)] public float summerDroughtSeverity = 0.35f;

    // Per-planet drought state
    private Dictionary<int, bool> planetDroughtActive = new Dictionary<int, bool>();
    private Dictionary<int, float> planetDroughtSeverity = new Dictionary<int, float>();

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
        AutoPopulateBiomeSeasonResponses();
        
        // Initialize climate data for all planets (multi-planet is always used)
        if (isGlobalClimateManager)
        {
            InitializeMultiPlanetClimate();
        }
    }

    private void OnValidate()
    {
        BuildSeasonalTextureLookup();
        AutoPopulateBiomeSeasonResponses();
    }

    /// <summary>
    /// Auto-populate seasonResponseLookup from BiomeVisualDatabase's BiomeVisualData assets.
    /// </summary>
    private void AutoPopulateBiomeSeasonResponses()
    {
        seasonResponseLookup.Clear();
        if (biomeVisualDatabase == null || biomeVisualDatabase.biomes == null)
        {
            Debug.LogWarning("[ClimateManager] No BiomeVisualDatabase assigned or it is empty.");
            return;
        }

        int added = 0;
        foreach (var biomeData in biomeVisualDatabase.biomes)
        {
            if (biomeData == null) continue;
            foreach (Season season in Enum.GetValues(typeof(Season)))
            {
                var resp = new BiomeSeasonResponse();
                resp.biome = biomeData.biome;
                resp.season = season;
                resp.yieldMultiplier = 1f;
                switch (season)
                {
                    case Season.Spring:
                        resp.snow = biomeData.springResponse.snow;
                        resp.wet = biomeData.springResponse.wet;
                        resp.dry = biomeData.springResponse.dry;
                        resp.tint = biomeData.springResponse.tint;
                        break;
                    case Season.Summer:
                        resp.snow = biomeData.summerResponse.snow;
                        resp.wet = biomeData.summerResponse.wet;
                        resp.dry = biomeData.summerResponse.dry;
                        resp.tint = biomeData.summerResponse.tint;
                        break;
                    case Season.Autumn:
                        resp.snow = biomeData.autumnResponse.snow;
                        resp.wet = biomeData.autumnResponse.wet;
                        resp.dry = biomeData.autumnResponse.dry;
                        resp.tint = biomeData.autumnResponse.tint;
                        break;
                    case Season.Winter:
                        resp.snow = biomeData.winterResponse.snow;
                        resp.wet = biomeData.winterResponse.wet;
                        resp.dry = biomeData.winterResponse.dry;
                        resp.tint = biomeData.winterResponse.tint;
                        break;
                }
                seasonResponseLookup[(resp.biome, resp.season)] = resp;
                added++;
            }
        }
        if (verboseLogs) Debug.Log($"[ClimateManager] Auto-populated {added} biome/season responses from BiomeVisualDatabase into lookup.");
    }

    void Start()
    {
        GameManager.OnPlanetFullyGenerated += HandlePlanetFullyGenerated;

        if (isGlobalClimateManager)
        {
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
        GameManager.OnPlanetFullyGenerated -= HandlePlanetFullyGenerated;

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

        // Always evaluate multi-planet season changes (single-planet is deprecated)
        CheckMultiPlanetSeasonChanges();
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
        if (verboseLogs) Debug.Log($"[ClimateManager] Applying seasonal effects: {season} on planet {planetIndex}");
        OnSeasonChanged?.Invoke(season);
        OnPlanetSeasonChanged?.Invoke(planetIndex, season);

        if (seasonChangeDebug)
        {
            Debug.Log($"[ClimateManager] SeasonChanged -> Planet:{planetIndex} Season:{season}");
        }

        // Log snow values for all biomes for this season from lookup
        foreach (var resp in seasonResponseLookup.Values)
        {
            if (resp.season == season)
            {
                if (verboseLogs) Debug.Log($"[ClimateManager] Biome {resp.biome} - Snow: {resp.snow}, Wet: {resp.wet}, Dry: {resp.dry}, Tint: {resp.tint}");
            }
        }

        // Per-season debug messages and seasonal effects
        if (verboseLogs)
        {
            Debug.Log($"[ClimateManager] {season} detected, applying seasonal responses (wet/dry/snow debug).");
        }

        if (season == Season.Winter)
        {
            if (verboseLogs) Debug.Log("[ClimateManager] Applying winter-specific snow effects and attrition.");
            ApplyWinterMovementPenalty(planetIndex);
            if (enableWinterAttrition)
            {
                ApplyWinterAttrition(planetIndex);
            }
        }
        else
        {
            if (verboseLogs) Debug.Log($"[ClimateManager] Applying wetness/dryness handling for {season}.");
            RemoveWinterMovementPenalty(planetIndex);
        }

        // Drought handling: if Summer begins on this planet, maybe trigger a drought.
        if (season == Season.Summer)
        {
            MaybeTriggerSummerDrought(planetIndex);
        }
        else
        {
            // Clear drought outside of Summer
            if (planetDroughtActive.ContainsKey(planetIndex)) planetDroughtActive[planetIndex] = false;
            if (planetDroughtSeverity.ContainsKey(planetIndex)) planetDroughtSeverity[planetIndex] = 0f;
        }
    }

    private void MaybeTriggerSummerDrought(int planetIndex)
    {
        try
        {
            bool triggered = UnityEngine.Random.value < summerDroughtChance;
            if (triggered)
            {
                planetDroughtActive[planetIndex] = true;
                planetDroughtSeverity[planetIndex] = summerDroughtSeverity;
                if (seasonChangeDebug) Debug.Log($"[ClimateManager] Drought triggered on planet {planetIndex} (severity={summerDroughtSeverity})");
            }
            else
            {
                planetDroughtActive[planetIndex] = false;
                planetDroughtSeverity[planetIndex] = 0f;
            }
        }
        catch { }
    }

    public bool IsDroughtActive(int planetIndex = 0)
    {
        return planetDroughtActive.TryGetValue(planetIndex, out var v) && v;
    }

    public float GetDroughtSeverity(int planetIndex = 0)
    {
        return planetDroughtSeverity.TryGetValue(planetIndex, out var v) ? v : 0f;
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

                var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
                var tileData = ts != null ? ts.GetTileData(idx) : null;
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

                var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
                var tileData = ts != null ? ts.GetTileData(idx) : null;
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

    public BiomeSeasonResponse GetSeasonResponse(Biome biome, Season season)
    {
        if (seasonResponseLookup.TryGetValue((biome, season), out var response) && response != null)
        {
            return response;
        }

        return new BiomeSeasonResponse
        {
            biome = biome,
            season = season
        };
    }

    /// <summary>
    /// Overload that considers planet-level climate events (e.g., drought).
    /// </summary>
    public BiomeSeasonResponse GetSeasonResponse(Biome biome, Season season, int planetIndex)
    {
        var resp = GetSeasonResponse(biome, season);
        if (season == Season.Summer && IsDroughtActive(planetIndex))
        {
            float drought = GetDroughtSeverity(planetIndex);
            // Reduce food yields by drought severity via the yieldMultiplier
            resp.yieldMultiplier = resp.yieldMultiplier * (1f - drought);
        }
        return resp;
    }

    /// <summary>
    /// Returns a precomputed season response for a specific tile index if available.
    /// Falls back to biome-based lookup when cache is not present.
    /// </summary>
    public BiomeSeasonResponse GetSeasonResponseForTile(int tileIndex, Season season)
    {
        if (tileSeasonCache != null && tileSeasonCache.TryGetValue(tileIndex, out var arr) && arr != null)
        {
            int idx = (int)season;
            if (idx >= 0 && idx < arr.Length && arr[idx] != null) return arr[idx];
        }

        // Fallback: try to find tile biome and return biome-based response
        var gen = planet ?? GameManager.Instance?.GetCurrentPlanetGenerator();
        if (gen != null && gen.data != null && gen.data.TryGetValue(tileIndex, out var tile))
        {
            return GetSeasonResponse(tile.biome, season);
        }

        // Last resort: return empty response with default biome
        return new BiomeSeasonResponse { biome = default(Biome), season = season };
    }

    /// <summary>
    /// Precompute per-tile BiomeSeasonResponse arrays for the given planet generator.
    /// Stores results in `tileSeasonCache` to accelerate chunk mask generation.
    /// </summary>
    public void PrecomputeTileSeasonCacheForPlanet(PlanetGenerator generator)
    {
        if (generator == null || generator.data == null) return;
        tileSeasonCache.Clear();

        var seasons = System.Enum.GetValues(typeof(Season));
        foreach (var kv in generator.data)
        {
            int tileIndex = kv.Key;
            var tile = kv.Value;
            try
            {
                BiomeSeasonResponse[] arr = new BiomeSeasonResponse[System.Enum.GetValues(typeof(Season)).Length];
                foreach (Season s in seasons)
                {
                    if (seasonResponseLookup.TryGetValue((tile.biome, s), out var resp) && resp != null)
                    {
                        // Clone to avoid shared mutation
                        var clone = new BiomeSeasonResponse();
                        clone.biome = resp.biome;
                        clone.season = resp.season;
                        clone.yieldMultiplier = resp.yieldMultiplier;
                        clone.snow = resp.snow;
                        clone.wet = resp.wet;
                        clone.dry = resp.dry;
                        clone.tint = resp.tint;
                        arr[(int)s] = clone;
                    }
                    else
                    {
                        arr[(int)s] = new BiomeSeasonResponse { biome = tile.biome, season = s };
                    }
                }
                tileSeasonCache[tileIndex] = arr;
            }
            catch { /* ignore per-tile failures */ }
        }

        if (GameManager.Instance == null || !GameManager.Instance.restrictDiagnosticsToFirstPlanet || GameManager.Instance.currentPlanetIndex == generator.planetIndex)
            Debug.Log($"[ClimateManager] Precomputed season responses for {tileSeasonCache.Count} tiles.");
    }

    /// <summary>
    /// Get the current season for a specific planet
    /// </summary>
    public Season GetSeasonForPlanet(int planetIndex = 0)
    {
        return planetSeasons.TryGetValue(planetIndex, out var season) ? season : Season.Spring;
    }

    /// <summary>
    /// Returns how many turns until Winter on the given planet. Used by AI to plan shelter building.
    /// Returns 0 if already Winter.
    /// </summary>
    public int GetTurnsUntilWinter(int planetIndex = 0)
    {
        if (turnsPerSeason <= 0) return 999;
        int currentTurnNow = GameManager.Instance != null ? GameManager.Instance.currentTurn : currentTurn;
        Season s = GetSeasonForPlanet(planetIndex);
        if (s == Season.Winter) return 0;
        if (!planetSeasonStartTurns.TryGetValue(planetIndex, out int start)) return turnsPerSeason * 3;
        int turnsInCurrentSeason = currentTurnNow - start;
        int turnsLeftInSeason = Mathf.Max(0, turnsPerSeason - turnsInCurrentSeason);
        int seasonsUntilWinter = 2 - (int)s; // Autumn=0, Summer=1, Spring=2
        return turnsLeftInSeason + Mathf.Max(0, seasonsUntilWinter) * turnsPerSeason;
    }

    private void ApplyWinterMovementPenalty(int planetIndex = 0)
    {
        foreach (var unit in UnitRegistry.GetCombatUnits())
        {
            if (IsUnitOnPlanet(unit, planetIndex) && !unit.hasWinterPenalty)
            {
                try
                {
                    int oldPts = unit.GetStartingMovePoints();
                    unit.hasWinterPenalty = true;
                    int newPts = unit.GetStartingMovePoints();
                    try { GameEventManager.Instance?.RaiseMovePointsChanged(unit, oldPts, newPts); } catch { }
                }
                catch { unit.hasWinterPenalty = true; }
                // Movement points removed - winter penalty now affects movement speed via fatigue
            }
        }

        foreach (var worker in UnitRegistry.GetWorkerUnits())
        {
            if (IsUnitOnPlanet(worker, planetIndex) && !worker.hasWinterPenalty)
            {
                try
                {
                    int oldPts = worker.GetStartingMovePoints();
                    worker.hasWinterPenalty = true;
                    int newPts = worker.GetStartingMovePoints();
                    try { GameEventManager.Instance?.RaiseMovePointsChanged(worker, oldPts, newPts); } catch { }
                }
                catch { worker.hasWinterPenalty = true; }
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
                try
                {
                    int oldPts = unit.GetStartingMovePoints();
                    unit.hasWinterPenalty = false;
                    int newPts = unit.GetStartingMovePoints();
                    try { GameEventManager.Instance?.RaiseMovePointsChanged(unit, oldPts, newPts); } catch { }
                }
                catch { unit.hasWinterPenalty = false; }
            }
        }

        foreach (var worker in UnitRegistry.GetWorkerUnits())
        {
            if (IsUnitOnPlanet(worker, planetIndex))
            {
                try
                {
                    int oldPts = worker.GetStartingMovePoints();
                    worker.hasWinterPenalty = false;
                    int newPts = worker.GetStartingMovePoints();
                    try { GameEventManager.Instance?.RaiseMovePointsChanged(worker, oldPts, newPts); } catch { }
                }
                catch { worker.hasWinterPenalty = false; }
            }
        }
    }

    // Helper method to check if a unit is on a specific planet
    private bool IsUnitOnPlanet(object unit, int planetIndex)
    {
        if (GameManager.Instance == null) return true;
        // For now, assume units without explicit planet tracking are on the currently active planet
        if (planetIndex == 0) return true;
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
        // Attempt to cache a reference to the current planet generator
        planet = GameManager.Instance?.GetCurrentPlanetGenerator();
        if (planet == null)
        {
            Debug.LogWarning("[ClimateManager] Could not find PlanetGenerator (current). Some planet-specific ops may be deferred.");
        }
    }

    private void HandlePlanetFullyGenerated(PlanetGenerator generator)
    {
        if (generator == null) return;
        if (!isGlobalClimateManager && generator.planetIndex != planetIndex) return;

        planet = generator;
        if (isGlobalClimateManager && !planetSeasons.ContainsKey(generator.planetIndex))
        {
            planetSeasons[generator.planetIndex] = Season.Spring;
            planetSeasonStartTurns[generator.planetIndex] = currentTurn;
        }

        // Precompute per-tile season responses for this planet to avoid repeated biome lookups during chunk updates
        try
        {
            PrecomputeTileSeasonCacheForPlanet(generator);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ClimateManager] PrecomputeTileSeasonCacheForPlanet failed: {ex.Message}");
        }

        ApplySeasonalEffects(GetSeasonForPlanet(generator.planetIndex), generator.planetIndex);
    }

    private void HandlePlanetSeasonChanged(int idx, Season season)
    {
        if (idx == planetIndex)
        {
            OnSeasonChanged?.Invoke(season);
        }
    }
}
