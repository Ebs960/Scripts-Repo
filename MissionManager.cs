using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Tracks active missions per-civilization, checks objective progress, applies world overrides,
/// and awards legacies on completion.
/// </summary>
public class MissionManager : MonoBehaviour
{
    public static MissionManager Instance { get; private set; }

    [Header("All Missions")]
    [Tooltip("Every mission available in the game")]
    public List<MissionData> allMissions = new List<MissionData>();

    /// <summary>Fired when a civilization starts a new mission.</summary>
    public event Action<Civilization, MissionData> OnMissionStarted;
    /// <summary>Fired when an objective within a mission is completed.</summary>
    public event Action<Civilization, MissionData, int> OnObjectiveCompleted;
    /// <summary>Fired when a mission ends (all objectives done or mission times out).</summary>
    public event Action<Civilization, MissionData, MissionState> OnMissionCompleted;

    // ─── Per-civ mission state ───
    private readonly Dictionary<Civilization, MissionState> activeMissions = new Dictionary<Civilization, MissionState>();

    /// <summary>
    /// Runtime state for one civilization's active mission.
    /// </summary>
    public class MissionState
    {
        public MissionData mission;
        public int currentObjectiveIndex;
        public int[] objectiveProgress;        // progress counter per objective
        public bool[] objectiveCompleted;
        public int startTurn;

        public int CompletedObjectiveCount => objectiveCompleted != null ? objectiveCompleted.Count(c => c) : 0;
        public bool AllObjectivesComplete => objectiveCompleted != null && objectiveCompleted.All(c => c);
        public MissionData.Objective CurrentObjective =>
            mission != null && currentObjectiveIndex < mission.objectives.Count
                ? mission.objectives[currentObjectiveIndex]
                : null;
    }

    // ─── Unity lifecycle ───

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        // Subscribe to existing game events for objective tracking
        if (TurnManager.Instance != null)
            TurnManager.Instance.OnRoundStarted += HandleRoundStarted;

        if (GameEventManager.Instance != null)
        {
            GameEventManager.Instance.OnUnitKilled += HandleUnitKilled;
            GameEventManager.Instance.OnTileImproved += HandleTileImproved;
        }
    }

    void OnDestroy()
    {
        if (TurnManager.Instance != null)
            TurnManager.Instance.OnRoundStarted -= HandleRoundStarted;

        if (GameEventManager.Instance != null)
        {
            GameEventManager.Instance.OnUnitKilled -= HandleUnitKilled;
            GameEventManager.Instance.OnTileImproved -= HandleTileImproved;
        }
    }

    // ─── Public API ───

    /// <summary>
    /// Start a mission for a civilization. Returns false if the civ already has an active mission
    /// or the mission's activation requirements aren't met.
    /// </summary>
    public bool StartMission(Civilization civ, MissionData mission)
    {
        if (civ == null || mission == null) return false;
        if (activeMissions.ContainsKey(civ)) return false;

        int turn = GameManager.Instance != null ? GameManager.Instance.currentTurn : 0;
        if (turn < mission.earliestTurn) return false;
        if (mission.latestTurn > 0 && turn > mission.latestTurn) return false;

        if (mission.requiredTechs != null)
            foreach (var t in mission.requiredTechs)
                if (t != null && !civ.researchedTechs.Contains(t)) return false;

        if (mission.requiredCultures != null)
            foreach (var c in mission.requiredCultures)
                if (c != null && !civ.researchedCultures.Contains(c)) return false;

        var state = new MissionState
        {
            mission = mission,
            currentObjectiveIndex = 0,
            objectiveProgress = new int[mission.objectives.Count],
            objectiveCompleted = new bool[mission.objectives.Count],
            startTurn = turn
        };

        activeMissions[civ] = state;
        ApplyWorldOverrides(mission);
        OnMissionStarted?.Invoke(civ, mission);
        Debug.Log($"[MissionManager] {civ.civData?.civName} started mission '{mission.missionName}'");
        return true;
    }

    /// <summary>Get the active mission state for a civ, or null if none.</summary>
    public MissionState GetActiveMission(Civilization civ)
    {
        return civ != null && activeMissions.TryGetValue(civ, out var s) ? s : null;
    }

    /// <summary>Which missions can this civ start right now?</summary>
    public List<MissionData> GetAvailableMissions(Civilization civ)
    {
        var available = new List<MissionData>();
        if (civ == null) return available;
        if (activeMissions.ContainsKey(civ)) return available; // already on a mission

        int turn = GameManager.Instance != null ? GameManager.Instance.currentTurn : 0;

        foreach (var m in allMissions)
        {
            if (turn < m.earliestTurn) continue;
            if (m.latestTurn > 0 && turn > m.latestTurn) continue;

            bool ok = true;
            if (m.requiredTechs != null)
                foreach (var t in m.requiredTechs)
                    if (t != null && !civ.researchedTechs.Contains(t)) { ok = false; break; }
            if (!ok) continue;

            if (m.requiredCultures != null)
                foreach (var c in m.requiredCultures)
                    if (c != null && !civ.researchedCultures.Contains(c)) { ok = false; break; }
            if (!ok) continue;

            available.Add(m);
        }
        return available;
    }

    /// <summary>
    /// Manually increment progress on the current objective for a civ.
    /// Useful for objective types not covered by automatic event tracking.
    /// </summary>
    public void AddProgress(Civilization civ, MissionData.ObjectiveType type, int amount = 1,
        object filter = null)
    {
        if (civ == null || !activeMissions.TryGetValue(civ, out var state)) return;
        if (state.AllObjectivesComplete) return;

        var obj = state.CurrentObjective;
        if (obj == null || obj.type != type) return;

        // Optional filter matching
        if (!MatchesFilter(obj, filter)) return;

        state.objectiveProgress[state.currentObjectiveIndex] += amount;
        CheckObjectiveCompletion(civ, state);
    }

    // ─── Event handlers ───

    private void HandleRoundStarted(int round)
    {
        // Process turn-based objectives (SurviveTurns, AccumulateGold, etc.)
        foreach (var kvp in activeMissions.ToList())
        {
            var civ = kvp.Key;
            var state = kvp.Value;
            if (civ == null || state.AllObjectivesComplete) continue;

            var obj = state.CurrentObjective;
            if (obj == null) continue;

            switch (obj.type)
            {
                case MissionData.ObjectiveType.SurviveTurns:
                    state.objectiveProgress[state.currentObjectiveIndex] =
                        round - state.startTurn;
                    break;
                case MissionData.ObjectiveType.AccumulateGold:
                    state.objectiveProgress[state.currentObjectiveIndex] = civ.gold;
                    break;
                case MissionData.ObjectiveType.AccumulateFood:
                    state.objectiveProgress[state.currentObjectiveIndex] = civ.food;
                    break;
                case MissionData.ObjectiveType.AccumulateFaith:
                    state.objectiveProgress[state.currentObjectiveIndex] = civ.faith;
                    break;
                case MissionData.ObjectiveType.ReachPopulation:
                    int pop = 0;
                    if (civ.cities != null)
                        foreach (var city in civ.cities)
                            if (city != null) pop += city.level;
                    state.objectiveProgress[state.currentObjectiveIndex] = pop;
                    break;
                case MissionData.ObjectiveType.OwnTiles:
                    int tiles = 0;
                    if (civ.ownedTilesByPlanet != null)
                        foreach (var set in civ.ownedTilesByPlanet.Values)
                            tiles += set.Count;
                    state.objectiveProgress[state.currentObjectiveIndex] = tiles;
                    break;
                case MissionData.ObjectiveType.FoundCity:
                    state.objectiveProgress[state.currentObjectiveIndex] =
                        civ.cities != null ? civ.cities.Count : 0;
                    break;
            }

            CheckObjectiveCompletion(civ, state);
        }
    }

    private void HandleUnitKilled(GameEventManager.CombatEventArgs args)
    {
        if (args == null || args.Attacker == null) return;

        // Determine which civ did the killing
        CombatUnit attacker = args.Attacker as CombatUnit;
        if (attacker == null || attacker.owner == null) return;

        CombatUnit defender = args.Defender as CombatUnit;
        if (defender == null) return;

        bool isAnimal = defender.data != null && defender.data.unitType == CombatCategory.Animal;
        var type = isAnimal ? MissionData.ObjectiveType.DefeatAnimals : MissionData.ObjectiveType.DefeatUnits;

        AddProgress(attacker.owner, type, 1, defender.data);
    }

    private void HandleTileImproved(GameEventManager.TileEventArgs args)
    {
        if (args == null || args.Cause == null) return;

        // Try to find the owning civ from the worker that built it
        var worker = args.Cause as WorkerUnit;
        if (worker == null || worker.owner == null) return;

        AddProgress(worker.owner, MissionData.ObjectiveType.BuildImprovements, 1);
    }

    // ─── Internal ───

    private void CheckObjectiveCompletion(Civilization civ, MissionState state)
    {
        int idx = state.currentObjectiveIndex;
        if (idx >= state.mission.objectives.Count) return;

        var obj = state.mission.objectives[idx];
        if (state.objectiveProgress[idx] >= obj.targetValue && !state.objectiveCompleted[idx])
        {
            state.objectiveCompleted[idx] = true;
            Debug.Log($"[MissionManager] {civ.civData?.civName} completed objective {idx}: {obj.objectiveName}");
            OnObjectiveCompleted?.Invoke(civ, state.mission, idx);

            // Advance to next incomplete objective
            for (int i = idx + 1; i < state.mission.objectives.Count; i++)
            {
                if (!state.objectiveCompleted[i])
                {
                    state.currentObjectiveIndex = i;
                    return;
                }
            }

            // All objectives done — complete the mission
            CompleteMission(civ, state);
        }
    }

    private void CompleteMission(Civilization civ, MissionState state)
    {
        Debug.Log($"[MissionManager] {civ.civData?.civName} completed mission '{state.mission.missionName}' ({state.CompletedObjectiveCount}/{state.mission.objectives.Count} objectives)");

        // Determine best reward tier
        var tiers = state.mission.rewardTiers;
        if (tiers != null)
        {
            // Sort descending by required objectives so we pick the best qualifying tier
            var sorted = tiers.OrderByDescending(t => t.requiredObjectivesCompleted);
            foreach (var tier in sorted)
            {
                if (state.CompletedObjectiveCount >= tier.requiredObjectivesCompleted && tier.rewardLegacy != null)
                {
                    if (LegacyManager.Instance != null)
                        LegacyManager.Instance.AwardLegacy(civ, tier.rewardLegacy);
                    Debug.Log($"[MissionManager] Awarded '{tier.rewardLegacy.legacyName}' ({tier.tierName}) to {civ.civData?.civName}");
                    break;
                }
            }
        }

        RemoveWorldOverrides(state.mission);
        OnMissionCompleted?.Invoke(civ, state.mission, state);
        activeMissions.Remove(civ);
    }

    private bool MatchesFilter(MissionData.Objective obj, object filter)
    {
        if (filter == null) return true;

        if (obj.specificUnit != null && filter is CombatUnitData unitData)
            return unitData == obj.specificUnit;
        if (obj.specificTech != null && filter is TechData tech)
            return tech == obj.specificTech;
        if (obj.specificCulture != null && filter is CultureData culture)
            return culture == obj.specificCulture;
        if (obj.specificImprovement != null && filter is ImprovementData improvement)
            return improvement == obj.specificImprovement;

        return true;
    }

    // ─── World Overrides ───

    private void ApplyWorldOverrides(MissionData mission)
    {
        if (mission.worldOverrides == null) return;
        foreach (var ov in mission.worldOverrides)
        {
            switch (ov.type)
            {
                case MissionData.WorldOverrideType.WinterDurationTurns:
                    if (ClimateManager.Instance != null)
                        ClimateManager.Instance.SetWinterDurationOverride(Mathf.RoundToInt(ov.value));
                    break;
                case MissionData.WorldOverrideType.DroughtChance:
                    if (ClimateManager.Instance != null)
                        ClimateManager.Instance.summerDroughtChance = ov.value;
                    break;
                case MissionData.WorldOverrideType.DroughtSeverity:
                    if (ClimateManager.Instance != null)
                        ClimateManager.Instance.summerDroughtSeverity = ov.value;
                    break;
                case MissionData.WorldOverrideType.WinterAttritionDamage:
                    if (ClimateManager.Instance != null)
                        ClimateManager.Instance.winterAttritionDamage = Mathf.RoundToInt(ov.value);
                    break;
            }
        }
    }

    private void RemoveWorldOverrides(MissionData mission)
    {
        if (mission.worldOverrides == null) return;
        foreach (var ov in mission.worldOverrides)
        {
            switch (ov.type)
            {
                case MissionData.WorldOverrideType.WinterDurationTurns:
                    if (ClimateManager.Instance != null)
                        ClimateManager.Instance.ClearWinterDurationOverride();
                    break;
                // Drought/attrition values are left as-is after mission ends
                // (they'll reset to inspector defaults on next scene load)
            }
        }
    }
}
