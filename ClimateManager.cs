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
    public float dry;
    public Color tint = Color.white;
}

public class ClimateManager : MonoBehaviour
{
    [Header("Biome Visual Database")]
    public BiomeVisualDatabase biomeVisualDatabase;
    public static ClimateManager Instance { get; private set; }

    /// <summary>
    /// Fired once per season start after freeze targets have been written to each tile's
    /// <c>freezeTarget</c> field.  Subscribers (e.g. HexMapChunkManager) should bake the
    /// per-chunk _FreezeMaskTex at this point.
    /// </summary>
    public static event Action<int> OnPlanetFreezeTargetsReady;

    /// <summary>
    /// Fired every frame while a freeze or thaw animation is running.
    /// Parameters: (planetIndex, progress 0..1, isFreeze).
    /// Progress 0 = fully thawed; 1 = fully frozen.
    /// </summary>
    public static event Action<int, float, bool> OnPlanetFreezeProgressChanged;

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

    // ─────────────────────────────────────────────────────────────
    // Water Freeze Settings
    // ─────────────────────────────────────────────────────────────
    [Header("Water Freeze Settings")]
    [Tooltip("ScriptableObject containing ice albedo, normal, and mask maps for lakes and rivers. " +
             "Assign an IceSurfaceDatabase asset in the Inspector.")]
    public IceSurfaceDatabase iceSurfaceDatabase;

    [Tooltip("Tiles whose temperature field exceeds this threshold will NOT freeze, even during Winter. " +
             "Matches the normalised range used by tile.temperature (0 = arctic, 1 = equatorial/volcanic).")]
    [Range(0f, 1f)]
    public float iceTemperatureThreshold = 0.3f;

    [Tooltip("Maximum freeze amount (0..1) for interior lake tiles (those NOT adjacent to land). " +
             "Land-adjacent lake tiles and all rivers always reach 1.0 when below the temperature threshold.")]
    [Range(0f, 1f)]
    public float interiorLakeFreezeMax = 0.45f;

    [Tooltip("Duration in real seconds for the freeze-in or thaw-out animation.")]
    [Min(0.1f)]
    public float freezeTransitionDuration = 3f;

    // Per-planet freeze animation state (runtime only, not serialised)
    // progress 0..1: 0 = fully thawed, 1 = fully frozen.
    private readonly Dictionary<int, float> _freezeProgress  = new Dictionary<int, float>();
    private readonly Dictionary<int, bool>  _freezeAnimActive  = new Dictionary<int, bool>();
    private readonly Dictionary<int, bool>  _freezeAnimForward = new Dictionary<int, bool>(); // true=freeze, false=thaw
    // Reusable scratch buffer for Update()'s key snapshot - avoids a per-frame List<int> allocation.
    private readonly List<int> _freezeAnimKeysBuffer = new List<int>();

    // Per-planet precomputed freeze targets: tileIndex -> target freeze amount (0..1).
    // Only tiles that CAN freeze (water, below temp threshold) are stored here.
    private readonly Dictionary<int, Dictionary<int, float>> _tileFreezeTargets =
        new Dictionary<int, Dictionary<int, float>>();

    // Mission-driven season duration override (-1 = no override)
    private int winterDurationOverride = -1;

    private struct FrozenSeasonSnapshot
    {
        public Season season;
        public int elapsedTurns;
    }

    private readonly Dictionary<int, FrozenSeasonSnapshot> frozenSeasonSnapshots = new Dictionary<int, FrozenSeasonSnapshot>();
    private bool forceWinterOverrideActive;

    public event Action<Season> OnSeasonChanged;

    public bool IsWinterForced => forceWinterOverrideActive;

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
                        resp.dry = biomeData.springResponse.dry;
                        resp.tint = biomeData.springResponse.tint;
                        break;
                    case Season.Summer:
                        resp.snow = biomeData.summerResponse.snow;
                        resp.dry = biomeData.summerResponse.dry;
                        resp.tint = biomeData.summerResponse.tint;
                        break;
                    case Season.Autumn:
                        resp.snow = biomeData.autumnResponse.snow;
                        resp.dry = biomeData.autumnResponse.dry;
                        resp.tint = biomeData.autumnResponse.tint;
                        break;
                    case Season.Winter:
                        resp.snow = biomeData.winterResponse.snow;
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
        if (WorldViewContext.Instance != null) WorldViewContext.Instance.OnViewChanged += HandleWorldViewChanged;

        if (isGlobalClimateManager)
        {
            if (TurnManager.Instance != null)
            {
                TurnManager.Instance.OnRoundStarted += HandleRoundStarted;
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
        if (WorldViewContext.Instance != null) WorldViewContext.Instance.OnViewChanged -= HandleWorldViewChanged;

        if (isGlobalClimateManager)
        {
            if (TurnManager.Instance != null)
            {
                TurnManager.Instance.OnRoundStarted -= HandleRoundStarted;
            }
        }
        else
        {
            OnPlanetSeasonChanged -= HandlePlanetSeasonChanged;
        }
    }

    private void HandleWorldViewChanged(WorldViewState state)
    {
        if (state.Mode != WorldViewMode.Planet || !state.PlanetIndex.HasValue) return;
        int planetIndex = state.PlanetIndex.Value;
        // Promotion to Full synchronizes the renderer to authoritative logical state before
        // subsequent animations resume.
        OnPlanetSeasonChanged?.Invoke(planetIndex, GetSeasonForPlanet(planetIndex));
        OnPlanetFreezeTargetsReady?.Invoke(planetIndex);
        bool frozen = GetSeasonForPlanet(planetIndex) == Season.Winter;
        OnPlanetFreezeProgressChanged?.Invoke(planetIndex, GetFreezeProgressForPlanet(planetIndex), frozen);
    }

    // ─────────────────────────────────────────────────────────────
    // Freeze animation — runs every frame while active
    // ─────────────────────────────────────────────────────────────
    private void Update()
    {
        if (_freezeAnimActive.Count == 0) return;

        // Gather keys first to avoid modifying the dict while iterating. Reuses a persistent
        // buffer instead of allocating a new List<int> every frame an animation is active.
        _freezeAnimKeysBuffer.Clear();
        _freezeAnimKeysBuffer.AddRange(_freezeAnimActive.Keys);
        foreach (int pi in _freezeAnimKeysBuffer)
        {
            if (!_freezeAnimActive.TryGetValue(pi, out bool active) || !active) continue;

            bool forward = _freezeAnimForward.TryGetValue(pi, out bool fwd) && fwd;
            if (!IsPlanetVisible(pi))
            {
                CompleteFreezeTransitionImmediately(pi, forward, false);
                continue;
            }

            float current = _freezeProgress.TryGetValue(pi, out float cur) ? cur : 0f;
            float target  = forward ? 1f : 0f;
            float step    = Time.deltaTime / Mathf.Max(0.01f, freezeTransitionDuration);
            float next    = Mathf.MoveTowards(current, target, step);

            _freezeProgress[pi] = next;

            // Write to tiles every frame so gameplay reads an accurate freezeAmount
            WriteFreezeAmountsToPlanet(pi, next);

            // Notify rendering systems (HexMapChunkManager will update _FreezeProgress mat prop)
            OnPlanetFreezeProgressChanged?.Invoke(pi, next, forward);

            if (Mathf.Approximately(next, target))
            {
                _freezeAnimActive[pi] = false;

                // On full thaw completion: zero out all freeze amounts for cleanliness
                if (!forward)
                    ClearFreezeAmountsForPlanet(pi);
            }
        }
    }

    /// <summary>Returns the current 0..1 freeze progress for a planet (0 = thawed, 1 = frozen).</summary>
    public float GetFreezeProgressForPlanet(int pi) =>
        _freezeProgress.TryGetValue(pi, out float p) ? p : 0f;

    private void HandleRoundStarted(int turnNumber)
    {
        if (turnNumber == currentTurn) return;
        currentTurn = turnNumber;
        CheckSeasonChange();
        ApplyPerTurnWinterAttrition();
        ApplyPerTurnMissilePollution();
        ApplyPerTurnSubjectProcessing(turnNumber);
    }

    /// <summary>
    /// Applies winter attrition damage every turn for each planet currently in Winter.
    /// </summary>
    private void ApplyPerTurnWinterAttrition()
    {
        if (!enableWinterAttrition) return;

        var planetData = GameManager.Instance?.GetPlanetData();
        if (planetData == null) return;

        foreach (var kvp in planetData)
        {
            int planetIndex = kvp.Key;
            if (GetSeasonForPlanet(planetIndex) == Season.Winter)
            {
                ApplyWinterAttrition(planetIndex);
            }
        }
    }

    /// <summary>
    /// Ticks missile radiation pollution on all planets each turn.
    /// Delegates to MissileManager which owns the per-tile pollution logic.
    /// </summary>
    private void ApplyPerTurnMissilePollution()
    {
        if (MissileManager.Instance == null) return;

        var planetData = GameManager.Instance?.GetPlanetData();
        if (planetData == null) return;

        foreach (var kvp in planetData)
        {
            MissileManager.Instance.ProcessPollutionTick(kvp.Key);
        }
    }

    /// <summary>
    /// Processes vassal tribute transfer and liberty desire ticks each turn.
    /// Delegates to SubjectManager.
    /// </summary>
    private void ApplyPerTurnSubjectProcessing(int turnNumber)
    {
        if (SubjectManager.Instance == null) return;
        SubjectManager.Instance.ProcessTributeTick(turnNumber);
        SubjectManager.Instance.ProcessLibertyTick(turnNumber);
    }

    private void CheckSeasonChange()
    {
        if (turnsPerSeason <= 0) return;
        if (forceWinterOverrideActive) return;

        // Always evaluate multi-planet season changes (single-planet is deprecated)
        CheckMultiPlanetSeasonChanges();
    }

    private void CheckSinglePlanetSeasonChange()
    {
        int effectiveDuration = GetEffectiveTurnsForSeason(currentSeason);
        if (currentTurn - seasonStartTurn >= effectiveDuration || forceSeasonChange)
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
            int effectiveDuration = GetEffectiveTurnsForSeason(planetSeasons[planetIndex]);
            if (currentTurn - seasonStart >= effectiveDuration || forceSeasonChange)
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

    /// <summary>
    /// Returns how many turns the given season should last, accounting for mission overrides.
    /// </summary>
    public int GetEffectiveTurnsForSeason(Season season)
    {
        if (season == Season.Winter && winterDurationOverride > 0)
            return winterDurationOverride;
        return turnsPerSeason;
    }

    /// <summary>
    /// Set a mission-driven override for winter duration. Pass -1 to clear.
    /// </summary>
    public void SetWinterDurationOverride(int turns)
    {
        winterDurationOverride = turns;
        if (verboseLogs) Debug.Log($"[ClimateManager] Winter duration override set to {turns}");
    }

    /// <summary>Clear the winter duration override, reverting to turnsPerSeason.</summary>
    public void ClearWinterDurationOverride()
    {
        winterDurationOverride = -1;
        if (verboseLogs) Debug.Log("[ClimateManager] Winter duration override cleared");
    }

    public void SetForceWinterOverride(bool enabled)
    {
        if (enabled)
        {
            if (forceWinterOverrideActive)
                return;

            forceWinterOverrideActive = true;
            frozenSeasonSnapshots.Clear();

            foreach (int planetIndex in GetKnownPlanetIndices())
            {
                if (!planetSeasons.TryGetValue(planetIndex, out var season))
                    season = Season.Spring;

                int startTurn = planetSeasonStartTurns.TryGetValue(planetIndex, out var savedStartTurn)
                    ? savedStartTurn
                    : currentTurn;

                frozenSeasonSnapshots[planetIndex] = new FrozenSeasonSnapshot
                {
                    season = season,
                    elapsedTurns = Mathf.Max(0, currentTurn - startTurn)
                };

                planetSeasons[planetIndex] = Season.Winter;
                planetSeasonStartTurns[planetIndex] = currentTurn;
                ApplySeasonalEffects(Season.Winter, planetIndex);
            }

            if (verboseLogs || seasonChangeDebug)
                Debug.Log($"[ClimateManager] ForceWinter override enabled for {frozenSeasonSnapshots.Count} planets.");
            return;
        }

        if (!forceWinterOverrideActive)
            return;

        forceWinterOverrideActive = false;

        foreach (int planetIndex in GetKnownPlanetIndices())
        {
            if (frozenSeasonSnapshots.TryGetValue(planetIndex, out var snapshot))
            {
                planetSeasons[planetIndex] = snapshot.season;
                planetSeasonStartTurns[planetIndex] = currentTurn - snapshot.elapsedTurns;
            }
            else
            {
                if (!planetSeasons.ContainsKey(planetIndex))
                    planetSeasons[planetIndex] = Season.Spring;
                if (!planetSeasonStartTurns.ContainsKey(planetIndex))
                    planetSeasonStartTurns[planetIndex] = currentTurn;
            }

            ApplySeasonalEffects(GetSeasonForPlanet(planetIndex), planetIndex);
        }

        frozenSeasonSnapshots.Clear();

        if (verboseLogs || seasonChangeDebug)
            Debug.Log("[ClimateManager] ForceWinter override cleared.");
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
        if (PlanetSimulationManager.GetTier(planetIndex) == PlanetSimulationTier.Full)
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
                if (verboseLogs) Debug.Log($"[ClimateManager] Biome {resp.biome} - Snow: {resp.snow}, Dry: {resp.dry}, Tint: {resp.tint}");
            }
        }

        // Per-season debug messages and seasonal effects
        if (verboseLogs)
        {
            Debug.Log($"[ClimateManager] {season} detected, applying seasonal responses (wet/dry/snow debug).");
        }

        if (season == Season.Winter)
        {
            if (verboseLogs) Debug.Log("[ClimateManager] Applying winter-specific snow effects.");
            ApplyWinterMovementPenalty(planetIndex);
            // Winter attrition is now applied every turn via ApplyPerTurnWinterAttrition()

            // Begin gradual water-tile freeze animation
            var gen = GetGeneratorForPlanet(planetIndex);
            if (gen != null) BeginFreezeTransition(planetIndex, gen);
        }
        else
        {
            if (verboseLogs) Debug.Log($"[ClimateManager] Applying wetness/dryness handling for {season}.");
            RemoveWinterMovementPenalty(planetIndex);

            // Begin gradual thaw animation (harmless if nothing was frozen)
            BeginThawTransition(planetIndex);
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
                    // Sum weather damage reduction from all equipped items (capped at 100%).
                    float equipReduction = 0f;
                    foreach (var eq in unit.EnumerateEquippedItemsForVision())
                        if (eq != null && eq.reducesWeatherDamage) equipReduction += eq.weatherDamageReduction;
                    equipReduction = Mathf.Clamp01(equipReduction);

                    // Civilization / belief / building attrition reductions (added bonuses)
                    float civReduction = 0f;
                    try { civReduction = unit.owner != null ? unit.owner.GetAttritionModifierTotals(null, null).winterDamageReductionPct : 0f; } catch { civReduction = 0f; }

                    float totalReduction = Mathf.Clamp01(equipReduction + civReduction);
                    int damage = Mathf.CeilToInt(winterAttritionDamage * (1f - totalReduction));
                    if (damage > 0) unit.ApplyDamage(damage);
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
                    // Sum weather damage reduction from all equipped items (capped at 100%).
                    float equipReductionW = 0f;
                    foreach (var eq in worker.EnumerateEquippedItemsForVision())
                        if (eq != null && eq.reducesWeatherDamage) equipReductionW += eq.weatherDamageReduction;
                    equipReductionW = Mathf.Clamp01(equipReductionW);

                    float civReductionW = 0f;
                    try { civReductionW = worker.owner != null ? worker.owner.GetAttritionModifierTotals(null, null).winterDamageReductionPct : 0f; } catch { civReductionW = 0f; }

                    float totalReductionW = Mathf.Clamp01(equipReductionW + civReductionW);
                    int damage = Mathf.CeilToInt(winterAttritionDamage * (1f - totalReductionW));
                    if (damage > 0) worker.ApplyDamage(damage);
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
        if (forceWinterOverrideActive)
            return Season.Winter;
        return planetSeasons.TryGetValue(planetIndex, out var season) ? season : Season.Spring;
    }

    /// <summary>
    /// Returns how many turns until Winter on the given planet. Used by AI to plan shelter building.
    /// Returns 0 if already Winter.
    /// </summary>
    public int GetTurnsUntilWinter(int planetIndex = 0)
    {
        if (forceWinterOverrideActive) return 0;
        if (turnsPerSeason <= 0) return 999;
        int currentTurnNow = GameManager.Instance != null ? GameManager.Instance.currentTurn : currentTurn;
        Season s = GetSeasonForPlanet(planetIndex);
        if (s == Season.Winter) return 0;
        if (!planetSeasonStartTurns.TryGetValue(planetIndex, out int start)) return turnsPerSeason * 3;
        int effectiveDuration = GetEffectiveTurnsForSeason(s);
        int turnsInCurrentSeason = currentTurnNow - start;
        int turnsLeftInSeason = Mathf.Max(0, effectiveDuration - turnsInCurrentSeason);
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

    public enum ClimateChangeType { TemperatureOnly, MoistureOnly, Warm, Cold, Dry, Wet, Mixed }

    // Track active coroutines per-planet so we can cancel overlapping simulations
    private readonly Dictionary<int, Coroutine> _activeClimateCoroutines = new Dictionary<int, Coroutine>();

    public void SimulateClimateChange(float amount, float moistureAmount, float timescale, ClimateChangeType type = ClimateChangeType.Mixed, int planetIndex = 0)
    {
        var gen = GetGeneratorForPlanet(planetIndex);
        if (gen == null)
        {
            Debug.LogWarning($"[ClimateManager] SimulateClimateChange: no generator for planet {planetIndex}");
            return;
        }

        // Cancel any existing simulation on this planet
        if (_activeClimateCoroutines.TryGetValue(planetIndex, out var existing) && existing != null)
        {
            try { StopCoroutine(existing); } catch { }
            _activeClimateCoroutines.Remove(planetIndex);
        }

        Coroutine c = StartCoroutine(SimulateClimateChangeCoroutine(amount, moistureAmount, timescale, type, planetIndex));
        _activeClimateCoroutines[planetIndex] = c;
    }

    private System.Collections.IEnumerator SimulateClimateChangeCoroutine(float amount, float moistureAmount, float timescale, ClimateChangeType type, int planetIndex)
    {
        var gen = GetGeneratorForPlanet(planetIndex);
        if (gen == null) yield break;

        // Determine per-tile targets based on requested type
        float tempDelta = 0f;
        float moistDelta = 0f;
        switch (type)
        {
            case ClimateChangeType.TemperatureOnly:
                tempDelta = amount;
                break;
            case ClimateChangeType.MoistureOnly:
                moistDelta = moistureAmount;
                break;
            case ClimateChangeType.Warm:
                tempDelta = Mathf.Abs(amount);
                break;
            case ClimateChangeType.Cold:
                tempDelta = -Mathf.Abs(amount);
                break;
            case ClimateChangeType.Dry:
                moistDelta = -Mathf.Abs(amount);
                break;
            case ClimateChangeType.Wet:
                moistDelta = Mathf.Abs(amount);
                break;
            case ClimateChangeType.Mixed:
            default:
                tempDelta = amount;
                moistDelta = moistureAmount;
                break;
        }

        // Clamp timescale
        timescale = Mathf.Max(0.01f, timescale);

        // Compute speeds (per-second change)
        float tempSpeed = Mathf.Abs(tempDelta) / timescale;
        float moistSpeed = Mathf.Abs(moistDelta) / timescale;

        // Cache initial and target values per tile
        var data = gen.data;
        var targets = new Dictionary<int, (float tempTgt, float moistTgt)>();
        foreach (var kv in data)
        {
            int idx = kv.Key;
            var tile = kv.Value;
            float tgtTemp = Mathf.Clamp01(tile.temperature + tempDelta);
            float tgtMoist = Mathf.Clamp01(tile.moisture + moistDelta);
            targets[idx] = (tgtTemp, tgtMoist);
        }

        if (PlanetSimulationManager.GetTier(planetIndex) != PlanetSimulationTier.Full)
        {
            // Offscreen climate advances logically in one bounded step; it never performs
            // per-frame interpolation, terrain repainting, or repeated renderer callbacks.
            foreach (var kv in targets)
            {
                if (!data.TryGetValue(kv.Key, out var tile)) continue;
                tile.temperature = kv.Value.tempTgt;
                tile.moisture = kv.Value.moistTgt;
            }
            try { PrecomputeTileSeasonCacheForPlanet(gen); } catch { }
            try { ComputeTileFreezeTargets(planetIndex, gen); } catch { }
            if (GetSeasonForPlanet(planetIndex) == Season.Winter) CompleteFreezeTransitionImmediately(planetIndex, true, false);
            else CompleteFreezeTransitionImmediately(planetIndex, false, false);
            _activeClimateCoroutines.Remove(planetIndex);
            yield break;
        }

        float elapsed = 0f;
        float tickAccum = 0f;
        // Periodically update cached season responses and notify renderers to avoid per-frame heavy work
        const float notifyInterval = 0.5f;

        while (elapsed < timescale)
        {
            float dt = Time.deltaTime;
            elapsed += dt;
            tickAccum += dt;

            foreach (var kv in targets)
            {
                int idx = kv.Key;
                if (!data.TryGetValue(idx, out var tile)) continue;
                var tgt = kv.Value;
                if (!Mathf.Approximately(tile.temperature, tgt.tempTgt))
                {
                    tile.temperature = Mathf.MoveTowards(tile.temperature, tgt.tempTgt, tempSpeed * dt);
                }
                if (!Mathf.Approximately(tile.moisture, tgt.moistTgt))
                {
                    tile.moisture = Mathf.MoveTowards(tile.moisture, tgt.moistTgt, moistSpeed * dt);
                }
            }

            if (tickAccum >= notifyInterval)
            {
                tickAccum = 0f;
                try
                {
                    PrecomputeTileSeasonCacheForPlanet(gen);
                }
                catch { }

                // Notify renderers that season masks may need rebaking. Use current season so handlers re-batch.
                OnPlanetSeasonChanged?.Invoke(planetIndex, GetSeasonForPlanet(planetIndex));

                // Recompute freeze targets and notify freeze-ready so freeze masks can be rebuilt
                try { ComputeTileFreezeTargets(planetIndex, gen); } catch { }
                OnPlanetFreezeTargetsReady?.Invoke(planetIndex);

                // If it's winter, begin freeze animation to reflect changes in freeze targets
                if (GetSeasonForPlanet(planetIndex) == Season.Winter)
                    BeginFreezeTransition(planetIndex, gen);
                else
                    BeginThawTransition(planetIndex);
            }

            yield return null;
        }

        // Final snap to targets
        foreach (var kv in targets)
        {
            int idx = kv.Key;
            if (!data.TryGetValue(idx, out var tile)) continue;
            tile.temperature = kv.Value.tempTgt;
            tile.moisture = kv.Value.moistTgt;
        }

        // Finalize caches and notify
        try { PrecomputeTileSeasonCacheForPlanet(gen); } catch { }
        OnPlanetSeasonChanged?.Invoke(planetIndex, GetSeasonForPlanet(planetIndex));
        try { ComputeTileFreezeTargets(planetIndex, gen); } catch { }
        OnPlanetFreezeTargetsReady?.Invoke(planetIndex);
        if (GetSeasonForPlanet(planetIndex) == Season.Winter)
            BeginFreezeTransition(planetIndex, gen);
        else
            BeginThawTransition(planetIndex);

        // Remove tracking
        if (_activeClimateCoroutines.ContainsKey(planetIndex)) _activeClimateCoroutines.Remove(planetIndex);
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

    // ═══════════════════════════════════════════════════════════════
    // Freeze / Thaw — private helpers
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns the PlanetGenerator for planet <paramref name="pi"/> via GameManager.
    /// Falls back to the locally cached <c>planet</c> field if indices match.
    /// </summary>
    private PlanetGenerator GetGeneratorForPlanet(int pi)
    {
        var gm = GameManager.Instance;
        if (gm != null)
        {
            var gen = gm.GetPlanetGenerator(pi);
            if (gen != null) return gen;
        }
        return (planet != null && planet.planetIndex == pi) ? planet : null;
    }

    /// <summary>
    /// Walk all water tiles on <paramref name="gen"/> and compute each tile's
    /// <c>freezeTarget</c> value (0..1):
    /// <list type="bullet">
    ///   <item>Ocean / Lava-biome tiles → 0 (never freeze).</item>
    ///   <item>Tiles whose <c>temperature</c> ≥ <see cref="iceTemperatureThreshold"/> → 0.</item>
    ///   <item>River tiles → 1.0 (full freeze).</item>
    ///   <item>Lake tiles adjacent to a land tile → 1.0 (shore ice).</item>
    ///   <item>Interior lake tiles → <see cref="interiorLakeFreezeMax"/> (partial slush).</item>
    /// </list>
    /// Results are stored in <c>_tileFreezeTargets[pi]</c> AND written to
    /// <c>tile.freezeTarget</c> so HexMapChunk can bake the per-chunk mask texture.
    /// </summary>
    private void ComputeTileFreezeTargets(int pi, PlanetGenerator gen)
    {
        if (!_tileFreezeTargets.ContainsKey(pi))
            _tileFreezeTargets[pi] = new Dictionary<int, float>();
        else
            _tileFreezeTargets[pi].Clear();

        var targets = _tileFreezeTargets[pi];
        var grid    = gen.Grid;
        var data    = gen.data;

        foreach (var kvp in data)
        {
            int idx      = kvp.Key;
            var tile     = kvp.Value;

            // Reset every tile's runtime target to 0 first
            tile.freezeTarget = 0f;

            // Only water tiles can freeze
            if (tile.waterType == TileWaterType.None) continue;
            if (tile.waterType == TileWaterType.Ocean) continue;
            if (tile.biome == Biome.Lava) continue;

            // Temperature threshold — warm tiles stay liquid
            if (tile.temperature >= iceTemperatureThreshold) continue;

            float tgt;
            if (tile.waterType == TileWaterType.River)
            {
                tgt = 1f; // Rivers freeze solid
            }
            else // Lake
            {
                // Check whether ANY neighbour is a land tile
                bool adjacentToLand = false;
                if (grid.neighbors != null && idx >= 0 && idx < grid.neighbors.Length)
                {
                    var neighbours = grid.neighbors[idx];
                    if (neighbours != null)
                    {
                        foreach (int nIdx in neighbours)
                        {
                            if (data.TryGetValue(nIdx, out var neighbour) &&
                                neighbour.waterType == TileWaterType.None)
                            {
                                adjacentToLand = true;
                                break;
                            }
                        }
                    }
                }
                tgt = adjacentToLand ? 1f : interiorLakeFreezeMax;
            }

            tile.freezeTarget = tgt;
            targets[idx]      = tgt;
        }

        // Debug summary
        int waterCount = 0, frozenCount = 0, tooWarmCount = 0;
        foreach (var kvp in data)
        {
            if (kvp.Value.waterType != TileWaterType.None && kvp.Value.waterType != TileWaterType.Ocean)
            {
                waterCount++;
                if (kvp.Value.freezeTarget > 0f) frozenCount++;
                else if (kvp.Value.temperature >= iceTemperatureThreshold) tooWarmCount++;
            }
        }
        Debug.Log($"[ClimateManager] ComputeTileFreezeTargets planet={pi}: {waterCount} inland water tiles, {frozenCount} will freeze, {tooWarmCount} too warm (threshold={iceTemperatureThreshold:F2})");
    }

    /// <summary>
    /// Kick off a freeze animation for planet <paramref name="pi"/>.
    /// Computes per-tile targets, fires <see cref="OnPlanetFreezeTargetsReady"/> so
    /// HexMapChunkManager can bake the _FreezeMaskTex, then starts animating 0→1.
    /// </summary>
    private void BeginFreezeTransition(int pi, PlanetGenerator gen)
    {
        ComputeTileFreezeTargets(pi, gen);

        if (!IsPlanetVisible(pi))
        {
            CompleteFreezeTransitionImmediately(pi, true, false);
            return;
        }

        int targetCount = _tileFreezeTargets.ContainsKey(pi) ? _tileFreezeTargets[pi].Count : 0;
        Debug.Log($"[ClimateManager] BeginFreezeTransition planet={pi}: {targetCount} tiles have freeze targets. OnPlanetFreezeTargetsReady subscribers={OnPlanetFreezeTargetsReady?.GetInvocationList()?.Length ?? 0}");

        // Notify rendering layer to bake the per-chunk freeze target mask textures ONCE
        OnPlanetFreezeTargetsReady?.Invoke(pi);

        // Start animating progress from wherever it currently is (handles mid-thaw reversal)
        if (!_freezeProgress.ContainsKey(pi)) _freezeProgress[pi] = 0f;
        _freezeAnimForward[pi] = true;
        _freezeAnimActive[pi]  = true;

        Debug.Log($"[ClimateManager] Freeze animation started for planet {pi}. Initial progress={_freezeProgress[pi]:F2}");
    }

    /// <summary>
    /// Kick off a thaw animation for planet <paramref name="pi"/>.
    /// Animates the existing freeze progress back to 0.
    /// </summary>
    private void BeginThawTransition(int pi)
    {
        // Nothing to do if the planet was never frozen
        if (!_freezeProgress.ContainsKey(pi) || _freezeProgress[pi] <= 0f)
        {
            // Still zero out targets for any stale state
            ClearFreezeAmountsForPlanet(pi);
            return;
        }

        _freezeAnimForward[pi] = false;
        if (!IsPlanetVisible(pi))
        {
            CompleteFreezeTransitionImmediately(pi, false, false);
            return;
        }

        _freezeAnimActive[pi]  = true;
    }

    private static bool IsPlanetVisible(int pi)
    {
        var view = WorldViewContext.Instance;
        if (view != null)
            return view.Current.Mode == WorldViewMode.Planet && view.Current.PlanetIndex == pi;

        return GameManager.Instance != null && GameManager.Instance.currentPlanetIndex == pi;
    }

    private void CompleteFreezeTransitionImmediately(int pi, bool freeze, bool notifyRenderer = true)
    {
        float progress = freeze ? 1f : 0f;
        _freezeProgress[pi] = progress;
        _freezeAnimActive[pi] = false;

        if (freeze)
            WriteFreezeAmountsToPlanet(pi, progress);
        else
            ClearFreezeAmountsForPlanet(pi);

        if (notifyRenderer) OnPlanetFreezeProgressChanged?.Invoke(pi, progress, freeze);
    }

    /// <summary>
    /// Writes <c>tile.freezeAmount = target * progress</c> for every tile that has a
    /// non-zero freeze target.  Called every frame during animation.
    /// </summary>
    private void WriteFreezeAmountsToPlanet(int pi, float progress)
    {
        if (!_tileFreezeTargets.TryGetValue(pi, out var targets)) return;

        var gen = GetGeneratorForPlanet(pi);
        if (gen == null) return;
        var data = gen.data;

        foreach (var kvp in targets)
        {
            if (data.TryGetValue(kvp.Key, out var tile))
                tile.freezeAmount = kvp.Value * progress;
        }
    }

    /// <summary>
    /// Zeroes <c>tile.freezeAmount</c> for ALL water tiles on planet <paramref name="pi"/>.
    /// Called when a thaw animation fully completes.
    /// </summary>
    private void ClearFreezeAmountsForPlanet(int pi)
    {
        var gen = GetGeneratorForPlanet(pi);
        if (gen == null) return;
        var data = gen.data;

        foreach (var tile in data.Values)
        {
            if (tile.waterType != TileWaterType.None)
                tile.freezeAmount = 0f;
        }

        // Reset tracking state
        if (_tileFreezeTargets.ContainsKey(pi)) _tileFreezeTargets[pi].Clear();
        _freezeProgress[pi]    = 0f;
        _freezeAnimActive[pi]  = false;
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

    private IEnumerable<int> GetKnownPlanetIndices()
    {
        HashSet<int> indices = new HashSet<int>(planetSeasons.Keys);

        var planetData = GameManager.Instance?.GetPlanetData();
        if (planetData != null)
        {
            foreach (var kvp in planetData)
                indices.Add(kvp.Key);
        }

        if (indices.Count == 0)
            indices.Add(0);

        return indices;
    }

    private void HandlePlanetSeasonChanged(int idx, Season season)
    {
        if (idx == planetIndex)
        {
            OnSeasonChanged?.Invoke(season);
        }
    }
}
