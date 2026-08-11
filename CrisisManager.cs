using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Manages world-scale crises (Ice Age, Black Death, Bronze Age Collapse, etc.).
/// Only ONE crisis may be active at a time.
///
/// Lifecycle: Dormant → Warning → Active → Escalation → Climax → Resolution → Dormant
///
/// On activation the manager:
///   1. Captures original climate values
///   2. Applies world overrides
///   3. Exposes the crisis mission list directly from CrisisData
///
/// On end:
///   1. Restores original climate
///   2. Clears the active crisis so its mission list is no longer available
/// </summary>
public class CrisisManager : MonoBehaviour, ISaveGameParticipant
{
    public static CrisisManager Instance { get; private set; }
    public string SaveKey => "crisis-manager";

    [Header("All Crises")]
    [Tooltip("Every crisis available in the game.")]
    public List<CrisisData> allCrises = new List<CrisisData>();

    [Header("Debug")]
    [Tooltip("Logs the full crisis lifecycle, mission progress, and trigger evaluation.")]
    public bool enableCrisisDebugLogging = true;

    // ─── Events ───
    public event Action<CrisisData> OnCrisisOminousWarning;
    public event Action<CrisisData> OnCrisisObviousWarning;
    public event Action<CrisisData> OnCrisisStarted;
    public event Action<CrisisData, CrisisData.CrisisPhase> OnCrisisPhaseChanged;
    public event Action<CrisisData> OnCrisisEnded;
    public event Action<Civilization, MissionData> OnMissionStarted;
    public event Action<Civilization, MissionData, int> OnObjectiveCompleted;
    public event Action<Civilization, MissionData, MissionState> OnMissionCompleted;
    public event Action<Civilization, MissionData, string> OnMissionFailed;

    // ─── Public read-only state ───
    public CrisisData ActiveCrisis => activeCrisis;
    public CrisisData.CrisisPhase CurrentPhase => currentPhase;
    public bool IsCrisisActive => activeCrisis != null && currentPhase != CrisisData.CrisisPhase.Dormant;

    /// <summary>Crises that have already completed (by crisisName). Prevents re-triggering.</summary>
    public HashSet<string> CrisisHistory => crisisHistory;
    private readonly HashSet<string> crisisHistory = new HashSet<string>();

    public int TurnsRemaining
    {
        get
        {
            if (activeCrisis == null || activeCrisis.durationTurns <= 0) return -1;
            int elapsed = CurrentTurn - crisisActiveTurn;
            return Mathf.Max(0, activeCrisis.durationTurns - elapsed);
        }
    }

    // ─── Private state ───
    private CrisisData activeCrisis;
    private CrisisData.CrisisPhase currentPhase = CrisisData.CrisisPhase.Dormant;
    private int crisisTriggerTurn;  // turn when TriggerCrisis was called (warning begins)
    private int crisisActiveTurn;   // turn when the crisis actually started (post-warning)

    // Climate + modifier snapshot
    private struct OriginalWorldValues
    {
        public int winterDuration;
        public bool winterForced;
        public float droughtChance;
        public float droughtSeverity;
        public int winterAttritionDamage;
        public float preySpawnMultiplier;      // stored on AnimalManager
        public float predatorSpawnMultiplier;  // stored on AnimalManager
        public float foodMultiplier;            // stored per-civ (we apply a delta)
        public bool captured;
    }
    private OriginalWorldValues originalWorld;
    private readonly Dictionary<int, MissionState> activeMissions = new Dictionary<int, MissionState>();
    private readonly HashSet<int> subscribedCivs = new HashSet<int>();
    private bool subscribedToTurnManager;
    private bool subscribedToImprovementManager;
    private bool subscribedToGameEvents;
    private bool subscribedToDiplomacy;
    private bool warnedAboutMissingCrises;
    private int lastNoEligibleCrisisWarningRound = int.MinValue;

    [Serializable]
    public class MissionStateSaveData
    {
        public int civIndex;
        public string missionName;
        public int currentObjectiveIndex;
        public int[] objectiveProgress;
        public bool[] objectiveCompleted;
        public int startTurn;
    }

    public class MissionState
    {
        public MissionData mission;
        public int currentObjectiveIndex;
        public int[] objectiveProgress;
        public bool[] objectiveCompleted;
        public int startTurn;

        public int CompletedObjectiveCount => objectiveCompleted != null ? objectiveCompleted.Count(c => c) : 0;
        public bool AllObjectivesComplete => objectiveCompleted != null && objectiveCompleted.All(c => c);
        public MissionData.Objective CurrentObjective =>
            mission != null && currentObjectiveIndex < mission.objectives.Count
                ? mission.objectives[currentObjectiveIndex]
                : null;
    }

    // ═══════════════════════════════════════════════
    //  Unity lifecycle
    // ═══════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        SaveGameRegistry.Register(this);
        TrySubscribeToTurnManager();
        TrySubscribeToGameEvents();
        TrySubscribeToImprovementManager();
        TrySubscribeToDiplomacy();
        SubscribeToAllCivs();
    }

    void Update()
    {
        // Retry subscriptions that failed in Start() due to singletons not yet existing
        if (!subscribedToTurnManager) TrySubscribeToTurnManager();
        if (!subscribedToGameEvents) TrySubscribeToGameEvents();
        if (!subscribedToImprovementManager) TrySubscribeToImprovementManager();
        if (!subscribedToDiplomacy) TrySubscribeToDiplomacy();
    }

    void OnDestroy()
    {
        SaveGameRegistry.Unregister(this);
        if (subscribedToTurnManager && TurnManager.Instance != null)
            TurnManager.Instance.OnRoundStarted -= HandleRoundStarted;
        if (subscribedToGameEvents && GameEventManager.Instance != null)
        {
            GameEventManager.Instance.OnUnitKilled -= HandleUnitKilled;
            GameEventManager.Instance.OnUnitLost -= HandleUnitLost;
        }
        TryUnsubscribeFromImprovementManager();
        if (subscribedToDiplomacy && DiplomacyManager.Instance != null)
            DiplomacyManager.Instance.OnDiplomacyChanged -= HandleDiplomacyChanged;
        UnsubscribeFromAllCivs();
    }

    // ═══════════════════════════════════════════════
    //  Public API
    // ═══════════════════════════════════════════════

    /// <summary>
    /// Trigger a crisis. If it has warningTurns > 0, it enters Warning phase first.
    /// Otherwise it activates immediately. Returns false if another crisis is already running.
    /// </summary>
    public bool TriggerCrisis(CrisisData crisis)
    {
        LogCrisisDebug("TriggerCrisis", $"Request received for {DescribeCrisis(crisis)} at turn={CurrentTurn} while active={DescribeCrisis(activeCrisis)} phase={currentPhase}");

        if (crisis == null)
        {
            LogCrisisDebug("TriggerCrisis", "Rejected: crisis is null.");
            return false;
        }
        if (activeCrisis != null)
        {
            LogCrisisDebug("TriggerCrisis", $"Rejected: another crisis is already active ({DescribeCrisis(activeCrisis)} phase={currentPhase}).");
            return false; // one at a time
        }
        if (crisisHistory.Contains(crisis.crisisName))
        {
            LogCrisisDebug("TriggerCrisis", $"Rejected: '{crisis.crisisName}' is already in crisis history.");
            return false; // already happened
        }

        int turn = CurrentTurn;
        if (turn < crisis.earliestTurn)
        {
            LogCrisisDebug("TriggerCrisis", $"Rejected: current turn {turn} is earlier than earliestTurn {crisis.earliestTurn}.");
            return false;
        }
        if (crisis.latestTurn > 0 && turn > crisis.latestTurn)
        {
            LogCrisisDebug("TriggerCrisis", $"Rejected: current turn {turn} is later than latestTurn {crisis.latestTurn}.");
            return false;
        }

        activeCrisis = crisis;
        crisisTriggerTurn = turn;
        LogCrisisDebug("TriggerCrisis", $"Accepted: activeCrisis set to {DescribeCrisis(crisis)} with triggerTurn={crisisTriggerTurn}.");

        if (crisis.ominousWarningTurns > 0)
        {
            LogCrisisDebug("TriggerCrisis", $"Entering ominous warning for {crisis.ominousWarningTurns} turns.");
            SetPhase(CrisisData.CrisisPhase.OminousWarning);
            OnCrisisOminousWarning?.Invoke(crisis);
            Debug.Log($"[CrisisManager] Ominous warning for '{crisis.crisisName}' — {crisis.ominousWarningTurns} turns");
        }
        else if (crisis.obviousWarningTurns > 0)
        {
            LogCrisisDebug("TriggerCrisis", $"Skipping ominous warning and entering obvious warning for {crisis.obviousWarningTurns} turns.");
            SetPhase(CrisisData.CrisisPhase.ObviousWarning);
            OnCrisisObviousWarning?.Invoke(crisis);
            Debug.Log($"[CrisisManager] Obvious warning for '{crisis.crisisName}' — {crisis.obviousWarningTurns} turns");
        }
        else
        {
            LogCrisisDebug("TriggerCrisis", "No warning phases configured; activating immediately.");
            ActivateCrisis();
        }

        LogCrisisDebug("TriggerCrisis", $"Completed trigger request for {DescribeCrisis(crisis)}. Current phase={currentPhase}.");
        return true;
    }

    /// <summary>Force-end the active crisis immediately.</summary>
    public void ForceEndCrisis()
    {
        if (activeCrisis != null)
        {
            LogCrisisDebug("ForceEndCrisis", $"Force ending {DescribeCrisis(activeCrisis)} at phase={currentPhase} turn={CurrentTurn}.");
            EndCrisis();
        }
    }

    /// <summary>Which crises could be triggered right now?</summary>
    public List<CrisisData> GetAvailableCrises()
    {
        var list = new List<CrisisData>();
        if (activeCrisis != null) return list;
        int turn = CurrentTurn;
        foreach (var c in allCrises)
        {
            if (c == null) continue;
            if (crisisHistory.Contains(c.crisisName)) continue;
            if (turn < c.earliestTurn) continue;
            if (c.latestTurn > 0 && turn > c.latestTurn) continue;
            list.Add(c);
        }
        return list;
    }

    public MissionState GetActiveMission(Civilization civ)
    {
        int idx = GetCivIndex(civ);
        return idx >= 0 && activeMissions.TryGetValue(idx, out var state) ? state : null;
    }

    public int GetDisplayTurnsRemaining()
    {
        if (activeCrisis == null) return -1;

        switch (currentPhase)
        {
            case CrisisData.CrisisPhase.OminousWarning:
            {
                int elapsed = CurrentTurn - crisisTriggerTurn;
                return Mathf.Max(0, activeCrisis.ominousWarningTurns - elapsed);
            }
            case CrisisData.CrisisPhase.ObviousWarning:
            {
                int elapsed = CurrentTurn - crisisTriggerTurn;
                int totalWarning = activeCrisis.ominousWarningTurns + activeCrisis.obviousWarningTurns;
                return Mathf.Max(0, totalWarning - elapsed);
            }
            case CrisisData.CrisisPhase.Active:
            case CrisisData.CrisisPhase.Escalation:
            case CrisisData.CrisisPhase.Climax:
                return TurnsRemaining;
            default:
                return -1;
        }
    }

    public int GetCurrentObjectiveProgress(MissionState state)
    {
        if (state?.objectiveProgress == null) return 0;
        int idx = state.currentObjectiveIndex;
        if (idx < 0 || idx >= state.objectiveProgress.Length) return 0;
        return state.objectiveProgress[idx];
    }

    public int GetCurrentObjectiveTarget(MissionState state)
    {
        var objective = state?.CurrentObjective;
        return objective != null ? Mathf.Max(0, objective.targetValue) : 0;
    }

    public float GetCurrentObjectiveProgress01(MissionState state)
    {
        int target = GetCurrentObjectiveTarget(state);
        if (target <= 0) return 0f;
        return Mathf.Clamp01((float)GetCurrentObjectiveProgress(state) / target);
    }

    public List<MissionData> GetAvailableMissions(Civilization civ, CrisisData crisis = null)
    {
        var result = new List<MissionData>();
        var sourceCrisis = crisis != null ? crisis : activeCrisis;
        if (civ == null || sourceCrisis == null || sourceCrisis.crisisMissions == null)
            return result;

        int civIdx = GetCivIndex(civ);
        if (civIdx < 0 || activeMissions.ContainsKey(civIdx))
            return result;

        foreach (var mission in sourceCrisis.crisisMissions)
        {
            if (mission != null && MeetsPrerequisites(civ, mission))
                result.Add(mission);
        }

        return result;
    }

    public bool StartMission(Civilization civ, MissionData mission)
    {
        LogCrisisDebug("StartMission", $"Request civ={DescribeCiv(civ)} mission={DescribeMission(mission)} activeCrisis={DescribeCrisis(activeCrisis)}");

        if (civ == null || mission == null || activeCrisis == null || activeCrisis.crisisMissions == null)
        {
            LogCrisisDebug("StartMission", "Rejected: civ, mission, active crisis, or crisis mission list is missing.");
            return false;
        }

        int civIdx = GetCivIndex(civ);
        if (civIdx < 0 || activeMissions.ContainsKey(civIdx))
        {
            LogCrisisDebug("StartMission", $"Rejected: civIdx={civIdx}, alreadyHasMission={activeMissions.ContainsKey(civIdx)}.");
            return false;
        }

        if (!activeCrisis.crisisMissions.Contains(mission) || !MeetsPrerequisites(civ, mission))
        {
            LogCrisisDebug("StartMission", $"Rejected: mission not part of active crisis or prerequisites failed for civ={DescribeCiv(civ)} mission={DescribeMission(mission)}.");
            return false;
        }

        var state = CreateState(mission);
        activeMissions[civIdx] = state;
        LogCrisisDebug("StartMission", $"Mission state created for civIdx={civIdx} objectiveCount={state.objectiveProgress.Length} startTurn={state.startTurn}.");
        OnMissionStarted?.Invoke(civ, mission);
        Debug.Log($"[CrisisManager] {civ.civData?.civName} started mission '{mission.missionName}'");
        ValidateAllActiveConstraints(civ, civIdx, state);
        LogCrisisDebug("StartMission", $"Mission start completed for civ={DescribeCiv(civ)} mission={DescribeMission(mission)}.");
        return true;
    }

    public void AddProgress(Civilization civ, MissionData.ObjectiveType type, int amount = 1, object filter = null)
    {
        int civIdx = GetCivIndex(civ);
        if (civIdx < 0)
        {
            LogCrisisDebug("AddProgress", $"Ignored progress update because civ index is invalid. civ={DescribeCiv(civ)} type={type} amount={amount}");
            return;
        }

        if (activeMissions.TryGetValue(civIdx, out var state) && !state.AllObjectivesComplete)
        {
            LogCrisisDebug("AddProgress", $"Applying progress civ={DescribeCiv(civ)} civIdx={civIdx} mission={DescribeMission(state.mission)} objective={DescribeObjective(state.CurrentObjective)} type={type} amount={amount} filter={DescribeFilter(filter)}");
            TryAdvance(civ, civIdx, state, type, amount, filter);
        }
        else
        {
            LogCrisisDebug("AddProgress", $"No active mutable mission for civ={DescribeCiv(civ)} civIdx={civIdx}; progress type={type} amount={amount} ignored.");
        }
    }

    public List<MissionStateSaveData> ExportMissionStates()
    {
        var list = new List<MissionStateSaveData>();
        foreach (var kvp in activeMissions)
        {
            var state = kvp.Value;
            if (state?.mission == null)
                continue;

            list.Add(new MissionStateSaveData
            {
                civIndex = kvp.Key,
                missionName = state.mission.missionName,
                currentObjectiveIndex = state.currentObjectiveIndex,
                objectiveProgress = (int[])state.objectiveProgress.Clone(),
                objectiveCompleted = (bool[])state.objectiveCompleted.Clone(),
                startTurn = state.startTurn
            });
        }

        return list;
    }

    public void ImportMissionStates(List<MissionStateSaveData> saved)
    {
        activeMissions.Clear();
        if (saved == null)
            return;

        var missionLookup = new Dictionary<string, MissionData>();
        foreach (var crisis in allCrises)
        {
            if (crisis?.crisisMissions == null)
                continue;

            foreach (var mission in crisis.crisisMissions)
            {
                if (mission != null && !string.IsNullOrEmpty(mission.missionName))
                    missionLookup[mission.missionName] = mission;
            }
        }

        foreach (var saveData in saved)
        {
            if (saveData == null || string.IsNullOrEmpty(saveData.missionName))
                continue;
            if (!missionLookup.TryGetValue(saveData.missionName, out var mission))
                continue;

            activeMissions[saveData.civIndex] = new MissionState
            {
                mission = mission,
                currentObjectiveIndex = saveData.currentObjectiveIndex,
                objectiveProgress = saveData.objectiveProgress ?? new int[mission.objectives.Count],
                objectiveCompleted = saveData.objectiveCompleted ?? new bool[mission.objectives.Count],
                startTurn = saveData.startTurn
            };
        }

        SubscribeToAllCivs();
    }

    // ═══════════════════════════════════════════════
    //  Turn processing
    // ═══════════════════════════════════════════════

    private void HandleRoundStarted(int round)
    {
        LogCrisisDebug("HandleRoundStarted", $"Round start round={round} activeCrisis={DescribeCrisis(activeCrisis)} phase={currentPhase} activeMissionCount={activeMissions.Count}");
        TrySubscribeToImprovementManager();
        SubscribeToAllCivs();

        if (activeCrisis == null)
        {
            LogCrisisDebug("HandleRoundStarted", "No active crisis at round start; attempting auto trigger.");
            TryAutoTriggerCrisis(round);
        }

        foreach (var kvp in activeMissions.ToList())
        {
            var civ = GetCivByIndex(kvp.Key);
            if (civ != null)
            {
                LogCrisisDebug("HandleRoundStarted", $"Polling turn objectives for civ={DescribeCiv(civ)} civIdx={kvp.Key} mission={DescribeMission(kvp.Value.mission)} currentObjective={DescribeObjective(kvp.Value.CurrentObjective)}");
                PollTurnObjectives(civ, kvp.Key, kvp.Value, round);
                if (activeMissions.TryGetValue(kvp.Key, out var stillActive) && stillActive == kvp.Value)
                {
                    LogCrisisDebug("HandleRoundStarted", $"Validating active constraints for civ={DescribeCiv(civ)} mission={DescribeMission(kvp.Value.mission)}.");
                    ValidateAllActiveConstraints(civ, kvp.Key, kvp.Value);
                }
            }
        }

        if (activeCrisis == null)
        {
            LogCrisisDebug("HandleRoundStarted", "No active crisis after auto trigger evaluation; exiting round crisis processing.");
            return;
        }

        switch (currentPhase)
        {
            case CrisisData.CrisisPhase.OminousWarning:
            {
                int elapsed = round - crisisTriggerTurn;
                LogCrisisDebug("HandleRoundStarted", $"Ominous warning processing elapsed={elapsed}/{activeCrisis.ominousWarningTurns}.");
                if (elapsed >= activeCrisis.ominousWarningTurns)
                {
                    if (activeCrisis.obviousWarningTurns > 0)
                    {
                        LogCrisisDebug("HandleRoundStarted", $"Ominous warning complete; transitioning to obvious warning for {activeCrisis.obviousWarningTurns} turns.");
                        SetPhase(CrisisData.CrisisPhase.ObviousWarning);
                        OnCrisisObviousWarning?.Invoke(activeCrisis);
                    }
                    else
                    {
                        LogCrisisDebug("HandleRoundStarted", "Ominous warning complete and no obvious warning configured; activating crisis.");
                        ActivateCrisis();
                    }
                }
                break;
            }
            case CrisisData.CrisisPhase.ObviousWarning:
            {
                int totalWarning = activeCrisis.ominousWarningTurns + activeCrisis.obviousWarningTurns;
                int elapsed = round - crisisTriggerTurn;
                LogCrisisDebug("HandleRoundStarted", $"Obvious warning processing elapsed={elapsed}/{totalWarning}.");
                if (elapsed >= totalWarning)
                {
                    LogCrisisDebug("HandleRoundStarted", "Warning window complete; activating crisis.");
                    ActivateCrisis();
                }
                break;
            }
            case CrisisData.CrisisPhase.Active:
            case CrisisData.CrisisPhase.Escalation:
            case CrisisData.CrisisPhase.Climax:
                LogCrisisDebug("HandleRoundStarted", $"Advancing active phase for {DescribeCrisis(activeCrisis)} phase={currentPhase}.");
                AdvanceActivePhase(round);
                break;
        }
    }

    private void TryAutoTriggerCrisis(int round)
    {
        if (activeCrisis != null)
        {
            LogCrisisDebug("TryAutoTriggerCrisis", $"Skipped because crisis already active: {DescribeCrisis(activeCrisis)}.");
            return;
        }

        var candidates = GetAutoTriggerCandidates(round);
        LogCrisisDebug("TryAutoTriggerCrisis", $"Round={round} candidateCount={candidates.Count} configuredCrises={(allCrises != null ? allCrises.Count : 0)}");
        if (candidates.Count == 0)
        {
            if (!warnedAboutMissingCrises && (allCrises == null || allCrises.Count == 0))
            {
                warnedAboutMissingCrises = true;
                Debug.LogWarning("[CrisisManager] No crises are configured in allCrises, so no crisis can trigger.");
            }
            else
            {
                LogCrisisDebug("TryAutoTriggerCrisis", "No eligible crises found this round.");
                if (lastNoEligibleCrisisWarningRound != round)
                {
                    lastNoEligibleCrisisWarningRound = round;
                    Debug.LogWarning($"[CrisisManager] No eligible crises found on round {round}. Check crisis turn windows, history, and activation requirements.");
                }
            }
            return;
        }

        candidates.Sort(CompareAutoTriggerPriority);
        LogCrisisDebug("TryAutoTriggerCrisis", $"Chosen crisis after sorting: {DescribeCrisis(candidates[0])}");
        TriggerCrisis(candidates[0]);
    }

    private List<CrisisData> GetAutoTriggerCandidates(int round)
    {
        var candidates = new List<CrisisData>();
        if (allCrises == null || allCrises.Count == 0)
            return candidates;

        foreach (var crisis in allCrises)
        {
            if (!CanAutoTrigger(crisis, round, out string rejectionReason))
            {
                LogCrisisDebug("GetAutoTriggerCandidates", $"Rejected candidate {DescribeCrisis(crisis)} at round={round}: {rejectionReason}");
                continue;
            }

            LogCrisisDebug("GetAutoTriggerCandidates", $"Accepted candidate {DescribeCrisis(crisis)} at round={round}.");
            candidates.Add(crisis);
        }

        return candidates;
    }

    private bool CanAutoTrigger(CrisisData crisis, int round, out string rejectionReason)
    {
        rejectionReason = null;
        if (crisis == null)
        {
            rejectionReason = "crisis is null";
            return false;
        }
        if (crisisHistory.Contains(crisis.crisisName))
        {
            rejectionReason = "crisis already completed";
            return false;
        }
        if (round < crisis.earliestTurn)
        {
            rejectionReason = $"current round {round} is earlier than earliestTurn {crisis.earliestTurn}";
            return false;
        }
        if (crisis.latestTurn > 0 && round > crisis.latestTurn)
        {
            rejectionReason = $"current round {round} is later than latestTurn {crisis.latestTurn}";
            return false;
        }

        return MeetsCrisisActivationRequirements(crisis, out rejectionReason);
    }

    private bool MeetsCrisisActivationRequirements(CrisisData crisis, out string rejectionReason)
    {
        rejectionReason = null;
        if (crisis == null)
        {
            rejectionReason = "crisis is null";
            return false;
        }

        bool hasTechRequirements = crisis.requiredTechs != null && crisis.requiredTechs.Any(tech => tech != null);
        bool hasCultureRequirements = crisis.requiredCultures != null && crisis.requiredCultures.Any(culture => culture != null);
        if (!hasTechRequirements && !hasCultureRequirements)
        {
            LogCrisisDebug("MeetsCrisisActivationRequirements", $"{DescribeCrisis(crisis)} has no tech/culture requirements.");
            return true;
        }

        var civs = CivilizationManager.Instance?.GetAllCivs();
        if (civs == null)
        {
            rejectionReason = "no civilizations available to validate requirements";
            return false;
        }

        foreach (var civ in civs)
        {
            if (civ == null)
                continue;

            if (CivilizationMeetsCrisisRequirements(civ, crisis, out string civReason))
            {
                LogCrisisDebug("MeetsCrisisActivationRequirements", $"{DescribeCrisis(crisis)} requirements satisfied by civ={DescribeCiv(civ)}.");
                return true;
            }

            LogCrisisDebug("MeetsCrisisActivationRequirements", $"civ={DescribeCiv(civ)} does not satisfy {DescribeCrisis(crisis)} requirements: {civReason}");
        }

        rejectionReason = "no civilization satisfies the crisis requirements";
        return false;
    }

    private bool CivilizationMeetsCrisisRequirements(Civilization civ, CrisisData crisis, out string rejectionReason)
    {
        rejectionReason = null;
        if (civ == null || crisis == null)
        {
            rejectionReason = "civ or crisis is null";
            return false;
        }

        if (crisis.requiredTechs != null)
        {
            foreach (var tech in crisis.requiredTechs)
            {
                if (tech != null && (civ.researchedTechs == null || !civ.researchedTechs.Contains(tech)))
                {
                    rejectionReason = $"missing tech '{tech.name}'";
                    return false;
                }
            }
        }

        if (crisis.requiredCultures != null)
        {
            foreach (var culture in crisis.requiredCultures)
            {
                if (culture != null && (civ.researchedCultures == null || !civ.researchedCultures.Contains(culture)))
                {
                    rejectionReason = $"missing culture '{culture.name}'";
                    return false;
                }
            }
        }

        return true;
    }

    private static int CompareAutoTriggerPriority(CrisisData left, CrisisData right)
    {
        if (ReferenceEquals(left, right))
            return 0;
        if (left == null)
            return 1;
        if (right == null)
            return -1;

        int earliestCompare = left.earliestTurn.CompareTo(right.earliestTurn);
        if (earliestCompare != 0)
            return earliestCompare;

        int latestLeft = left.latestTurn > 0 ? left.latestTurn : int.MaxValue;
        int latestRight = right.latestTurn > 0 ? right.latestTurn : int.MaxValue;
        int latestCompare = latestLeft.CompareTo(latestRight);
        if (latestCompare != 0)
            return latestCompare;

        return string.Compare(left.crisisName, right.crisisName, StringComparison.OrdinalIgnoreCase);
    }

    private void AdvanceActivePhase(int round)
    {
        int elapsed = round - crisisActiveTurn;
        LogCrisisDebug("AdvanceActivePhase", $"round={round} elapsed={elapsed} phase={currentPhase} duration={activeCrisis.durationTurns} escalationAt={activeCrisis.escalationAtTurn} climaxAt={activeCrisis.climaxAtTurn}");

        // Check duration expiry
        if (activeCrisis.durationTurns > 0 && elapsed >= activeCrisis.durationTurns)
        {
            LogCrisisDebug("AdvanceActivePhase", $"Duration expired for {DescribeCrisis(activeCrisis)}; entering resolution.");
            SetPhase(CrisisData.CrisisPhase.Resolution);
            EndCrisis();
            return;
        }

        // Phase transitions (later phases take priority)
        if (activeCrisis.climaxAtTurn > 0 && elapsed >= activeCrisis.climaxAtTurn
            && currentPhase != CrisisData.CrisisPhase.Climax)
        {
            LogCrisisDebug("AdvanceActivePhase", "Climax threshold reached.");
            SetPhase(CrisisData.CrisisPhase.Climax);
        }
        else if (activeCrisis.escalationAtTurn > 0 && elapsed >= activeCrisis.escalationAtTurn
                 && currentPhase == CrisisData.CrisisPhase.Active)
        {
            LogCrisisDebug("AdvanceActivePhase", "Escalation threshold reached.");
            SetPhase(CrisisData.CrisisPhase.Escalation);
        }
    }

    // ═══════════════════════════════════════════════
    //  Crisis activation / end
    // ═══════════════════════════════════════════════

    private void ActivateCrisis()
    {
        LogCrisisDebug("ActivateCrisis", $"Activating {DescribeCrisis(activeCrisis)} at turn={CurrentTurn}. triggerTurn={crisisTriggerTurn}");
        crisisActiveTurn = CurrentTurn;
        SetPhase(CrisisData.CrisisPhase.Active);

        LogCrisisDebug("ActivateCrisis", "Capturing original world state.");
        CaptureOriginalWorld();
        LogCrisisDebug("ActivateCrisis", "Applying world overrides.");
        ApplyWorldOverrides();

        LogCrisisDebug("ActivateCrisis", "Invoking OnCrisisStarted listeners.");
        OnCrisisStarted?.Invoke(activeCrisis);
        Debug.Log($"[CrisisManager] Crisis '{activeCrisis.crisisName}' is now ACTIVE");
        LogCrisisDebug("ActivateCrisis", $"Activation complete for {DescribeCrisis(activeCrisis)} activeTurn={crisisActiveTurn}");
    }

    private void EndCrisis()
    {
        if (activeCrisis == null) return;

        var crisis = activeCrisis;
        LogCrisisDebug("EndCrisis", $"Ending {DescribeCrisis(crisis)} phase={currentPhase} activeMissionCount={activeMissions.Count}");
        Debug.Log($"[CrisisManager] Crisis '{crisis.crisisName}' has ended");

        LogCrisisDebug("EndCrisis", "Restoring original world state.");
        RestoreOriginalWorld();
        LogCrisisDebug("EndCrisis", "Cancelling crisis missions.");
        CancelActiveMissionsForCrisis(crisis);

        crisisHistory.Add(crisis.crisisName);
        LogCrisisDebug("EndCrisis", $"Added '{crisis.crisisName}' to crisis history. HistoryCount={crisisHistory.Count}");
        OnCrisisEnded?.Invoke(crisis);
        activeCrisis = null;
        currentPhase = CrisisData.CrisisPhase.Dormant;
        LogCrisisDebug("EndCrisis", "Crisis state cleared; manager returned to Dormant.");
    }

    private void HandleUnitKilled(GameEventManager.CombatEventArgs args)
    {
        if (args == null || args.Attacker == null) return;
        var attacker = args.Attacker as CombatUnit;
        if (attacker == null || attacker.owner == null) return;
        var defender = args.Defender as CombatUnit;
        if (defender == null) return;

        bool isAnimal = defender.data != null && defender.data.unitType == CombatCategory.Animal;
        var type = isAnimal ? MissionData.ObjectiveType.DefeatAnimals : MissionData.ObjectiveType.DefeatUnits;
        AddProgress(attacker.owner, type, 1, defender.data);
    }

    private void HandleImprovementBuilt(Civilization owner, ImprovementData improvement, int tileIndex, int planetIndex)
    {
        if (owner == null || improvement == null) return;
        AddProgress(owner, MissionData.ObjectiveType.BuildImprovements, 1, improvement);
        int civIdx = GetCivIndex(owner);
        if (civIdx >= 0 && activeMissions.TryGetValue(civIdx, out var state))
            ValidateAllActiveConstraints(owner, civIdx, state);
    }

    private void HandleImprovementRemoved(Civilization owner, ImprovementData improvement, int tileIndex, int planetIndex, ImprovementManager.ImprovementRemovalReason reason)
    {
        if (owner == null) return;
        int civIdx = GetCivIndex(owner);
        if (civIdx >= 0 && activeMissions.TryGetValue(civIdx, out var state))
            ValidateAllActiveConstraints(owner, civIdx, state);
    }

    private void HandleUnitLost(GameEventManager.UnitLostEventArgs args)
    {
        var lostUnit = args?.Unit as BaseUnit;
        if (lostUnit == null || lostUnit.owner == null) return;

        int civIdx = GetCivIndex(lostUnit.owner);
        if (civIdx < 0 || !activeMissions.TryGetValue(civIdx, out var state)) return;
        if (state.mission?.constraints == null) return;

        foreach (var constraint in state.mission.constraints)
        {
            if (constraint == null || constraint.type != MissionData.ConstraintType.NoUnitLosses) continue;
            if (!IsConstraintActive(state, constraint)) continue;
            if (!MatchesConstraintUnitFilter(constraint, lostUnit)) continue;

            string reason = ResolveFailureText(
                state.mission,
                constraint.failureFlavorText,
                $"Lost unit '{lostUnit.name}' during mission '{state.mission.missionName}'");
            FailMission(lostUnit.owner, civIdx, state, reason);
            return;
        }
    }

    private void HandleTechResearched(TechData tech)
    {
        if (tech == null) return;
        ForEachActiveCiv((civ, idx) =>
        {
            if (civ.researchedTechs != null && civ.researchedTechs.Contains(tech))
                AddProgress(civ, MissionData.ObjectiveType.ResearchTech, 1, tech);
        });
    }

    private void HandleCultureCompleted(CultureData culture)
    {
        if (culture == null) return;
        ForEachActiveCiv((civ, idx) =>
        {
            if (civ.researchedCultures != null && civ.researchedCultures.Contains(culture))
                AddProgress(civ, MissionData.ObjectiveType.ResearchCulture, 1, culture);
        });
    }

    private void HandlePolicyAdopted(Civilization civ, PolicyData policy)
    {
        if (civ != null && policy != null) AddProgress(civ, MissionData.ObjectiveType.AdoptPolicy, 1);
    }

    private void HandleGovernmentChanged(Civilization civ, GovernmentData gov)
    {
        if (civ != null && gov != null) AddProgress(civ, MissionData.ObjectiveType.ChangeGovernment, 1);
    }

    private void HandleCityFounded(Civilization civ, City city)
    {
        if (civ != null) AddProgress(civ, MissionData.ObjectiveType.FoundCity, 1);
        if (city != null)
        {
            city.OnBuildingCompleted += HandleBuildingCompleted;
            city.OnBuildingRemoved += HandleBuildingRemoved;
        }
    }

    private void HandleBuildingCompleted(City city, BuildingData building)
    {
        if (city != null && city.owner != null)
        {
            AddProgress(city.owner, MissionData.ObjectiveType.BuildBuilding, 1, building);
            int civIdx = GetCivIndex(city.owner);
            if (civIdx >= 0 && activeMissions.TryGetValue(civIdx, out var state))
                ValidateAllActiveConstraints(city.owner, civIdx, state);
        }
    }

    private void HandleBuildingRemoved(City city, BuildingData building, City.BuildingRemovalReason reason)
    {
        if (city == null || city.owner == null) return;
        int civIdx = GetCivIndex(city.owner);
        if (civIdx >= 0 && activeMissions.TryGetValue(civIdx, out var state))
            ValidateAllActiveConstraints(city.owner, civIdx, state);
    }

    private void HandlePantheonFounded(Civilization civ, PantheonData pantheon)
    {
        if (civ != null) AddProgress(civ, MissionData.ObjectiveType.FoundPantheon, 1);
    }

    private void HandleUnitTrained(Civilization civ, CombatUnitData unitData)
    {
        if (civ != null) AddProgress(civ, MissionData.ObjectiveType.TrainUnits, 1, unitData);
    }

    private void HandleDiplomacyChanged(Civilization from, Civilization to, DiplomaticState newState)
    {
        switch (newState)
        {
            case DiplomaticState.Alliance:
                if (from != null) AddProgress(from, MissionData.ObjectiveType.FormAlliance, 1);
                if (to != null) AddProgress(to, MissionData.ObjectiveType.FormAlliance, 1);
                break;
            case DiplomaticState.War:
                if (from != null) AddProgress(from, MissionData.ObjectiveType.DeclareWar, 1);
                if (to != null) AddProgress(to, MissionData.ObjectiveType.DeclareWar, 1);
                break;
            case DiplomaticState.Peace:
                if (from != null) AddProgress(from, MissionData.ObjectiveType.MakePeace, 1);
                if (to != null) AddProgress(to, MissionData.ObjectiveType.MakePeace, 1);
                break;
            case DiplomaticState.Trade:
                if (from != null) AddProgress(from, MissionData.ObjectiveType.EstablishTrade, 1);
                if (to != null) AddProgress(to, MissionData.ObjectiveType.EstablishTrade, 1);
                break;
        }
    }

    // ═══════════════════════════════════════════════
    //  World overrides
    // ═══════════════════════════════════════════════

    private void CaptureOriginalWorld()
    {
        if (originalWorld.captured) return;
        var climate = ClimateManager.Instance;
        var animals = AnimalManager.Instance;
        originalWorld = new OriginalWorldValues
        {
            winterDuration = climate != null ? climate.turnsPerSeason : 3,
            winterForced = climate != null && climate.IsWinterForced,
            droughtChance = climate != null ? climate.summerDroughtChance : 0f,
            droughtSeverity = climate != null ? climate.summerDroughtSeverity : 0f,
            winterAttritionDamage = climate != null ? climate.winterAttritionDamage : 0,
            preySpawnMultiplier = animals != null ? animals.crisisPreySpawnMultiplier : 1f,
            predatorSpawnMultiplier = animals != null ? animals.crisisPredatorSpawnMultiplier : 1f,
            foodMultiplier = 0f, // we store the delta we applied
            captured = true
        };
        LogCrisisDebug("CaptureOriginalWorld", $"Captured original world: winterDuration={originalWorld.winterDuration}, winterForced={originalWorld.winterForced}, droughtChance={originalWorld.droughtChance:F3}, droughtSeverity={originalWorld.droughtSeverity:F3}, winterAttrition={originalWorld.winterAttritionDamage}, preyMul={originalWorld.preySpawnMultiplier:F3}, predatorMul={originalWorld.predatorSpawnMultiplier:F3}");
    }

    private void RestoreOriginalWorld()
    {
        if (!originalWorld.captured) return;

        LogCrisisDebug("RestoreOriginalWorld", $"Restoring original world values. foodDelta={originalWorld.foodMultiplier:F3}");

        // Climate
        if (ClimateManager.Instance != null)
        {
            ClimateManager.Instance.ClearWinterDurationOverride();
            ClimateManager.Instance.SetForceWinterOverride(originalWorld.winterForced);
            ClimateManager.Instance.summerDroughtChance = originalWorld.droughtChance;
            ClimateManager.Instance.summerDroughtSeverity = originalWorld.droughtSeverity;
            ClimateManager.Instance.winterAttritionDamage = originalWorld.winterAttritionDamage;
        }

        // Animals
        if (AnimalManager.Instance != null)
        {
            AnimalManager.Instance.crisisPreySpawnMultiplier = originalWorld.preySpawnMultiplier;
            AnimalManager.Instance.crisisPredatorSpawnMultiplier = originalWorld.predatorSpawnMultiplier;
        }

        // Food — remove the delta we applied to each civ
        if (originalWorld.foodMultiplier != 0f)
        {
            var allCivs = CivilizationManager.Instance?.GetAllCivs();
            if (allCivs != null)
                foreach (var civ in allCivs)
                    if (civ != null) civ.foodModifier -= originalWorld.foodMultiplier;
        }

        originalWorld.captured = false;
        LogCrisisDebug("RestoreOriginalWorld", "Original world restoration complete.");
    }

    private void ApplyWorldOverrides()
    {
        if (activeCrisis.worldOverrides == null || activeCrisis.worldOverrides.Length == 0)
        {
            LogCrisisDebug("ApplyWorldOverrides", $"No world overrides configured for {DescribeCrisis(activeCrisis)}.");
            return;
        }

        foreach (var ov in activeCrisis.worldOverrides)
        {
            LogCrisisDebug("ApplyWorldOverrides", $"Applying override type={ov.type} value={ov.value:F3}");
            switch (ov.type)
            {
                case CrisisData.WorldOverrideType.WinterDurationTurns:
                    if (ClimateManager.Instance != null)
                        ClimateManager.Instance.SetWinterDurationOverride(Mathf.RoundToInt(ov.value));
                    break;
                case CrisisData.WorldOverrideType.ForceWinter:
                    if (ClimateManager.Instance != null && ov.value > 0f)
                        ClimateManager.Instance.SetForceWinterOverride(true);
                    break;
                case CrisisData.WorldOverrideType.DroughtChance:
                    if (ClimateManager.Instance != null)
                        ClimateManager.Instance.summerDroughtChance =
                            Mathf.Max(ClimateManager.Instance.summerDroughtChance, ov.value);
                    break;
                case CrisisData.WorldOverrideType.DroughtSeverity:
                    if (ClimateManager.Instance != null)
                        ClimateManager.Instance.summerDroughtSeverity =
                            Mathf.Max(ClimateManager.Instance.summerDroughtSeverity, ov.value);
                    break;
                case CrisisData.WorldOverrideType.WinterAttritionDamage:
                    if (ClimateManager.Instance != null)
                        ClimateManager.Instance.winterAttritionDamage =
                            Mathf.Max(ClimateManager.Instance.winterAttritionDamage, Mathf.RoundToInt(ov.value));
                    break;
                case CrisisData.WorldOverrideType.PreySpawnMultiplier:
                    if (AnimalManager.Instance != null)
                        AnimalManager.Instance.crisisPreySpawnMultiplier = ov.value;
                    break;
                case CrisisData.WorldOverrideType.PredatorSpawnMultiplier:
                    if (AnimalManager.Instance != null)
                        AnimalManager.Instance.crisisPredatorSpawnMultiplier = ov.value;
                    break;
                case CrisisData.WorldOverrideType.FoodYieldMultiplier:
                {
                    // Add as a delta to every civ's foodModifier (e.g. -0.5 = −50% food)
                    float delta = ov.value;
                    originalWorld.foodMultiplier = delta;
                    var allCivs = CivilizationManager.Instance?.GetAllCivs();
                    if (allCivs != null)
                        foreach (var civ in allCivs)
                            if (civ != null) civ.foodModifier += delta;
                    break;
                }
            }
        }

        LogCrisisDebug("ApplyWorldOverrides", $"Finished applying {activeCrisis.worldOverrides.Length} overrides for {DescribeCrisis(activeCrisis)}.");
    }

    // ═══════════════════════════════════════════════
    //  Phase helpers
    // ═══════════════════════════════════════════════

    private void SetPhase(CrisisData.CrisisPhase phase)
    {
        if (currentPhase == phase) return;
        LogCrisisDebug("SetPhase", $"Phase transition {currentPhase} -> {phase} for {DescribeCrisis(activeCrisis)}");
        currentPhase = phase;
        OnCrisisPhaseChanged?.Invoke(activeCrisis, phase);
        LogPhaseNarrative(phase);
    }

    private void LogPhaseNarrative(CrisisData.CrisisPhase phase)
    {
        if (activeCrisis == null) return;
        string text = phase switch
        {
            CrisisData.CrisisPhase.OminousWarning => activeCrisis.ominousWarningText,
            CrisisData.CrisisPhase.ObviousWarning => activeCrisis.obviousWarningText,
            CrisisData.CrisisPhase.Active => activeCrisis.crisisStartText,
            CrisisData.CrisisPhase.Escalation => activeCrisis.escalationText,
            CrisisData.CrisisPhase.Climax => activeCrisis.climaxText,
            CrisisData.CrisisPhase.Resolution => activeCrisis.resolutionText,
            _ => null,
        };
        if (!string.IsNullOrEmpty(text))
            Debug.Log($"[CrisisManager] [{phase}] {text}");
    }

    // ═══════════════════════════════════════════════
    //  Save / Load
    // ═══════════════════════════════════════════════

    [Serializable]
    public class CrisisSaveData
    {
        public string crisisName;
        public int phase;
        public int triggerTurn;
        public int activeTurn;
        public List<string> injectedMissionNames;
        public List<string> completedCrisisNames;
    }

    public CrisisSaveData ExportCrisisState()
    {
        if (activeCrisis == null) return null;
        var data = new CrisisSaveData
        {
            crisisName = activeCrisis.crisisName,
            phase = (int)currentPhase,
            triggerTurn = crisisTriggerTurn,
            activeTurn = crisisActiveTurn,
            injectedMissionNames = new List<string>()
        };
        data.completedCrisisNames = new List<string>(crisisHistory);
        return data;
    }

    public void ImportCrisisState(CrisisSaveData data)
    {
        // Reset
        activeCrisis = null;
        currentPhase = CrisisData.CrisisPhase.Dormant;
        originalWorld.captured = false;
        crisisHistory.Clear();

        // Restore history
        if (data != null && data.completedCrisisNames != null)
            foreach (var name in data.completedCrisisNames)
                crisisHistory.Add(name);

        if (data == null || string.IsNullOrEmpty(data.crisisName)) return;

        // Find the CrisisData asset
        CrisisData crisis = null;
        foreach (var c in allCrises)
        {
            if (c != null && c.crisisName == data.crisisName) { crisis = c; break; }
        }
        if (crisis == null)
        {
            Debug.LogWarning($"[CrisisManager] Could not find crisis '{data.crisisName}' during load");
            return;
        }

        activeCrisis = crisis;
        crisisTriggerTurn = data.triggerTurn;
        crisisActiveTurn = data.activeTurn;
        currentPhase = (CrisisData.CrisisPhase)data.phase;

        // Restore world overrides if the crisis is past the warning phases
        if (currentPhase != CrisisData.CrisisPhase.OminousWarning
            && currentPhase != CrisisData.CrisisPhase.ObviousWarning
            && currentPhase != CrisisData.CrisisPhase.Dormant)
        {
            CaptureOriginalWorld();
            ApplyWorldOverrides();
        }

        Debug.Log($"[CrisisManager] Restored crisis '{crisis.crisisName}' in phase {currentPhase}");
    }

    [Serializable]
    private class CrisisParticipantSaveData
    {
        public List<MissionStateSaveData> missionStates = new List<MissionStateSaveData>();
        public CrisisSaveData crisisState;
    }

    public string CaptureStateJson()
    {
        var data = new CrisisParticipantSaveData
        {
            missionStates = ExportMissionStates(),
            crisisState = ExportCrisisState()
        };
        return JsonUtility.ToJson(data);
    }

    public void RestoreStateJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;

        var data = JsonUtility.FromJson<CrisisParticipantSaveData>(json);
        if (data == null)
            return;

        ImportMissionStates(data.missionStates);
        ImportCrisisState(data.crisisState);
    }

    private void CancelActiveMissionsForCrisis(CrisisData crisis)
    {
        if (crisis?.crisisMissions == null || crisis.crisisMissions.Count == 0)
            return;

        var missionSet = new HashSet<MissionData>(crisis.crisisMissions.Where(m => m != null));
        var toRemove = new List<int>();

        foreach (var kvp in activeMissions)
        {
            if (kvp.Value?.mission != null && missionSet.Contains(kvp.Value.mission))
                toRemove.Add(kvp.Key);
        }

        foreach (var civIdx in toRemove)
            activeMissions.Remove(civIdx);
    }

    private bool MeetsPrerequisites(Civilization civ, MissionData mission)
    {
        int turn = CurrentTurn;
        if (turn < mission.earliestTurn) return false;
        if (mission.latestTurn > 0 && turn > mission.latestTurn) return false;
        if (mission.requiredTechs != null)
            foreach (var tech in mission.requiredTechs)
                if (tech != null && !civ.researchedTechs.Contains(tech)) return false;
        if (mission.requiredCultures != null)
            foreach (var culture in mission.requiredCultures)
                if (culture != null && !civ.researchedCultures.Contains(culture)) return false;
        return true;
    }

    private MissionState CreateState(MissionData mission)
    {
        LogCrisisDebug("CreateState", $"Creating mission state for {DescribeMission(mission)} objectiveCount={(mission != null && mission.objectives != null ? mission.objectives.Count : 0)} at turn={CurrentTurn}");
        return new MissionState
        {
            mission = mission,
            currentObjectiveIndex = 0,
            objectiveProgress = new int[mission.objectives.Count],
            objectiveCompleted = new bool[mission.objectives.Count],
            startTurn = CurrentTurn
        };
    }

    private void TryAdvance(Civilization civ, int civIdx, MissionState state, MissionData.ObjectiveType type, int amount, object filter)
    {
        var objective = state.CurrentObjective;
        if (objective == null)
        {
            LogCrisisDebug("TryAdvance", $"No current objective for civ={DescribeCiv(civ)} mission={DescribeMission(state?.mission)}.");
            return;
        }
        if (objective.type != type) return;
        if (!MatchesFilter(objective, filter))
        {
            LogCrisisDebug("TryAdvance", $"Filter mismatch for civ={DescribeCiv(civ)} mission={DescribeMission(state.mission)} objective={DescribeObjective(objective)} filter={DescribeFilter(filter)}");
            return;
        }

        int before = state.objectiveProgress[state.currentObjectiveIndex];
        state.objectiveProgress[state.currentObjectiveIndex] += amount;
        LogCrisisDebug("TryAdvance", $"Progress updated for civ={DescribeCiv(civ)} objective={DescribeObjective(objective)} before={before} after={state.objectiveProgress[state.currentObjectiveIndex]} target={objective.targetValue}");
        CheckObjectiveCompletion(civ, civIdx, state);
    }

    private void PollTurnObjectives(Civilization civ, int civIdx, MissionState state, int round)
    {
        if (state.AllObjectivesComplete) return;
        var objective = state.CurrentObjective;
        if (objective == null) return;

        int before = state.objectiveProgress[state.currentObjectiveIndex];

        switch (objective.type)
        {
            case MissionData.ObjectiveType.SurviveTurns:
                state.objectiveProgress[state.currentObjectiveIndex] = round - state.startTurn;
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
                int population = 0;
                if (civ.cities != null)
                    foreach (var city in civ.cities)
                        if (city != null) population += city.level;
                state.objectiveProgress[state.currentObjectiveIndex] = population;
                break;
            case MissionData.ObjectiveType.OwnTiles:
                int tiles = 0;
                if (civ.ownedTilesByPlanet != null)
                    foreach (var set in civ.ownedTilesByPlanet.Values)
                        tiles += set.Count;
                state.objectiveProgress[state.currentObjectiveIndex] = tiles;
                break;
            case MissionData.ObjectiveType.FoundCity:
                state.objectiveProgress[state.currentObjectiveIndex] = civ.cities != null ? civ.cities.Count : 0;
                break;
            case MissionData.ObjectiveType.TrainUnits:
                state.objectiveProgress[state.currentObjectiveIndex] = civ.combatUnits != null ? civ.combatUnits.Count : 0;
                break;
            case MissionData.ObjectiveType.FoundPantheon:
                state.objectiveProgress[state.currentObjectiveIndex] = civ.foundedPantheons != null ? civ.foundedPantheons.Count : 0;
                break;
        }

        int after = state.objectiveProgress[state.currentObjectiveIndex];
        if (before != after)
            LogCrisisDebug("PollTurnObjectives", $"civ={DescribeCiv(civ)} mission={DescribeMission(state.mission)} objective={DescribeObjective(objective)} progress {before}->{after} on round={round}");

        CheckObjectiveCompletion(civ, civIdx, state);
    }

    private void CheckObjectiveCompletion(Civilization civ, int civIdx, MissionState state)
    {
        int idx = state.currentObjectiveIndex;
        if (idx >= state.mission.objectives.Count) return;

        var objective = state.mission.objectives[idx];
        LogCrisisDebug("CheckObjectiveCompletion", $"Checking civ={DescribeCiv(civ)} mission={DescribeMission(state.mission)} objectiveIndex={idx} progress={state.objectiveProgress[idx]} target={objective.targetValue} completed={state.objectiveCompleted[idx]}");
        if (state.objectiveProgress[idx] >= objective.targetValue && !state.objectiveCompleted[idx])
        {
            state.objectiveCompleted[idx] = true;
            Debug.Log($"[CrisisManager] {civ.civData?.civName} completed objective {idx}: {objective.objectiveName}");
            OnObjectiveCompleted?.Invoke(civ, state.mission, idx);
            ValidateAllActiveConstraints(civ, civIdx, state);
            if (!activeMissions.ContainsKey(civIdx)) return;

            for (int i = idx + 1; i < state.mission.objectives.Count; i++)
            {
                if (!state.objectiveCompleted[i])
                {
                    state.currentObjectiveIndex = i;
                    LogCrisisDebug("CheckObjectiveCompletion", $"Advancing to next objective index={i} for civ={DescribeCiv(civ)} mission={DescribeMission(state.mission)}");
                    return;
                }
            }

            LogCrisisDebug("CheckObjectiveCompletion", $"All objectives complete for civ={DescribeCiv(civ)} mission={DescribeMission(state.mission)}");
            CompleteMission(civ, civIdx, state);
        }
    }

    private void CompleteMission(Civilization civ, int civIdx, MissionState state)
    {
        LogCrisisDebug("CompleteMission", $"Completing mission for civ={DescribeCiv(civ)} mission={DescribeMission(state.mission)} completedObjectives={state.CompletedObjectiveCount}/{state.mission.objectives.Count}");
        Debug.Log($"[CrisisManager] {civ.civData?.civName} completed mission '{state.mission.missionName}' ({state.CompletedObjectiveCount}/{state.mission.objectives.Count} objectives)");

        if (!string.IsNullOrEmpty(state.mission.victoryFlavorText))
            Debug.Log($"[CrisisManager] Victory flavor: {state.mission.victoryFlavorText}");

        var tiers = state.mission.rewardTiers;
        if (tiers != null)
        {
            var sorted = tiers.OrderByDescending(t => t.requiredObjectivesCompleted);
            foreach (var tier in sorted)
            {
                if (state.CompletedObjectiveCount >= tier.requiredObjectivesCompleted && tier.rewardLegacy != null)
                {
                    if (LegacyManager.Instance != null)
                        LegacyManager.Instance.AwardLegacy(civ, tier.rewardLegacy);
                    Debug.Log($"[CrisisManager] Awarded '{tier.rewardLegacy.legacyName}' ({tier.tierName}) to {civ.civData?.civName}");
                    LogCrisisDebug("CompleteMission", $"Awarded reward tier '{tier.tierName}' legacy='{tier.rewardLegacy.legacyName}' to civ={DescribeCiv(civ)}");
                    break;
                }
            }
        }

        OnMissionCompleted?.Invoke(civ, state.mission, state);
        activeMissions.Remove(civIdx);
        LogCrisisDebug("CompleteMission", $"Mission removed from active mission map for civIdx={civIdx}");
    }

    private bool MatchesFilter(MissionData.Objective objective, object filter)
    {
        if (filter == null) return true;
        if (filter is CombatUnitData unitData)
        {
            if (objective.specificUnit != null) return unitData == objective.specificUnit;
            if (objective.specificUnits != null && objective.specificUnits.Length > 0)
                return Array.Exists(objective.specificUnits, unit => unit == unitData);
            if (objective.specificCategories != null && objective.specificCategories.Length > 0)
                return Array.Exists(objective.specificCategories, category => category == unitData.unitType);
            return true;
        }
        if (filter is WorkerUnitData workerData)
        {
            if (objective.specificWorkerUnits != null && objective.specificWorkerUnits.Length > 0)
                return Array.Exists(objective.specificWorkerUnits, worker => worker == workerData);
            return true;
        }
        if (filter is BuildingData building)
        {
            if (objective.specificBuilding != null) return building == objective.specificBuilding;
            if (objective.specificBuildings != null && objective.specificBuildings.Length > 0)
                return Array.Exists(objective.specificBuildings, b => b == building);
            return true;
        }
        if (objective.specificTech != null && filter is TechData tech) return tech == objective.specificTech;
        if (objective.specificCulture != null && filter is CultureData culture) return culture == objective.specificCulture;
        if (filter is ImprovementData improvement)
        {
            if (objective.specificImprovement != null) return improvement == objective.specificImprovement;
            if (objective.specificImprovements != null && objective.specificImprovements.Length > 0)
                return Array.Exists(objective.specificImprovements, i => i == improvement);
            return true;
        }
        return true;
    }

    private void FailMission(Civilization civ, int civIdx, MissionState state, string reason)
    {
        if (civ == null || state?.mission == null) return;
        LogCrisisDebug("FailMission", $"Failing mission civ={DescribeCiv(civ)} mission={DescribeMission(state.mission)} reason={reason}");
        Debug.Log($"[CrisisManager] {civ.civData?.civName} failed mission '{state.mission.missionName}': {reason}");
        if (!string.IsNullOrEmpty(state.mission.failureFlavorText))
            Debug.Log($"[CrisisManager] Failure flavor: {state.mission.failureFlavorText}");
        activeMissions.Remove(civIdx);
        OnMissionFailed?.Invoke(civ, state.mission, reason);
        LogCrisisDebug("FailMission", $"Mission removed from active mission map for civIdx={civIdx}");
    }

    private bool IsConstraintActive(MissionState state, MissionData.MissionConstraint constraint)
    {
        if (state == null || constraint == null) return false;
        if (constraint.activatesAfterObjectiveIndex < 0) return true;
        if (state.objectiveCompleted == null) return false;
        int idx = constraint.activatesAfterObjectiveIndex;
        return idx >= 0 && idx < state.objectiveCompleted.Length && state.objectiveCompleted[idx];
    }

    private void ValidateAllActiveConstraints(Civilization civ, int civIdx, MissionState state)
    {
        if (civ == null || state?.mission?.constraints == null) return;
        foreach (var constraint in state.mission.constraints)
        {
            if (constraint == null || !IsConstraintActive(state, constraint)) continue;
            switch (constraint.type)
            {
                case MissionData.ConstraintType.MaintainImprovementCount:
                {
                    int count = CountMatchingImprovements(civ, constraint);
                    if (!MatchesCountConstraint(constraint, count))
                    {
                        string reason = ResolveFailureText(state.mission, constraint.failureFlavorText, $"Improvement count constraint broken ({count})");
                        FailMission(civ, civIdx, state, reason);
                        return;
                    }
                    break;
                }
                case MissionData.ConstraintType.MaintainBuildingCount:
                {
                    int count = CountMatchingBuildings(civ, constraint);
                    if (!MatchesCountConstraint(constraint, count))
                    {
                        string reason = ResolveFailureText(state.mission, constraint.failureFlavorText, $"Building count constraint broken ({count})");
                        FailMission(civ, civIdx, state, reason);
                        return;
                    }
                    break;
                }
            }
        }
    }

    private bool MatchesConstraintUnitFilter(MissionData.MissionConstraint constraint, BaseUnit lostUnit)
    {
        if (constraint == null || lostUnit == null) return false;

        bool hasAnyFilter = (constraint.specificUnit != null)
            || (constraint.specificUnits != null && constraint.specificUnits.Length > 0)
            || (constraint.specificWorkerUnits != null && constraint.specificWorkerUnits.Length > 0)
            || (constraint.specificCategories != null && constraint.specificCategories.Length > 0);

        if (!hasAnyFilter) return true;

        if (lostUnit is CombatUnit combatUnit)
        {
            var unitData = combatUnit.data;
            if (unitData == null) return false;
            if (constraint.specificUnit != null) return unitData == constraint.specificUnit;
            if (constraint.specificUnits != null && constraint.specificUnits.Length > 0)
                return Array.Exists(constraint.specificUnits, unit => unit == unitData);
            if (constraint.specificCategories != null && constraint.specificCategories.Length > 0)
                return Array.Exists(constraint.specificCategories, category => category == unitData.unitType);
            return false;
        }

        if (lostUnit is WorkerUnit workerUnit)
        {
            return constraint.specificWorkerUnits != null
                && constraint.specificWorkerUnits.Length > 0
                && Array.Exists(constraint.specificWorkerUnits, worker => worker == workerUnit.data);
        }

        return false;
    }

    private string ResolveFailureText(MissionData mission, string constraintText, string fallback)
    {
        if (!string.IsNullOrEmpty(constraintText)) return constraintText;
        if (mission != null && !string.IsNullOrEmpty(mission.failureFlavorText)) return mission.failureFlavorText;
        return fallback;
    }

    private bool MatchesCountConstraint(MissionData.MissionConstraint constraint, int count)
    {
        switch (constraint.comparison)
        {
            case MissionData.CountComparison.AtLeast: return count >= constraint.targetValue;
            case MissionData.CountComparison.AtMost: return count <= constraint.targetValue;
            default: return count == constraint.targetValue;
        }
    }

    private int CountMatchingImprovements(Civilization civ, MissionData.MissionConstraint constraint)
    {
        int count = 0;
        if (civ?.ownedTilesByPlanet == null) return 0;

        foreach (var kvp in civ.ownedTilesByPlanet)
        {
            var tileSystem = TileSystem.GetForPlanet(kvp.Key) ?? TileSystem.Instance;
            if (tileSystem == null || kvp.Value == null) continue;
            foreach (int tileIndex in kvp.Value)
            {
                var tile = tileSystem.GetTileData(tileIndex);
                var improvement = tile?.improvement;
                if (improvement == null) continue;
                if (tile.improvementOwner != civ) continue;
                if (constraint.specificImprovement != null && improvement != constraint.specificImprovement) continue;
                if (constraint.specificImprovements != null && constraint.specificImprovements.Length > 0
                    && !Array.Exists(constraint.specificImprovements, item => item == improvement)) continue;
                count++;
            }
        }

        return count;
    }

    private int CountMatchingBuildings(Civilization civ, MissionData.MissionConstraint constraint)
    {
        int count = 0;
        if (civ?.cities == null) return 0;
        foreach (var city in civ.cities)
        {
            if (city == null || city.builtBuildings == null) continue;
            foreach (var built in city.builtBuildings)
            {
                var building = built.data;
                if (building == null) continue;
                if (constraint.specificBuilding != null && building != constraint.specificBuilding) continue;
                if (constraint.specificBuildings != null && constraint.specificBuildings.Length > 0
                    && !Array.Exists(constraint.specificBuildings, item => item == building)) continue;
                count++;
            }
        }
        return count;
    }

    private void ForEachActiveCiv(Action<Civilization, int> action)
    {
        foreach (int idx in activeMissions.Keys)
        {
            var civ = GetCivByIndex(idx);
            if (civ != null) action(civ, idx);
        }
    }

    private void SubscribeToAllCivs()
    {
        var allCivs = CivilizationManager.Instance?.GetAllCivs();
        if (allCivs == null) return;
        for (int i = 0; i < allCivs.Count; i++)
        {
            var civ = allCivs[i];
            if (civ == null || subscribedCivs.Contains(i)) continue;
            civ.OnTechResearched += HandleTechResearched;
            civ.OnCultureCompleted += HandleCultureCompleted;
            civ.OnPolicyAdopted += HandlePolicyAdopted;
            civ.OnGovernmentChanged += HandleGovernmentChanged;
            civ.OnCityFounded += HandleCityFounded;
            civ.OnPantheonFounded += HandlePantheonFounded;
            civ.OnUnitTrained += HandleUnitTrained;
            if (civ.cities != null)
                foreach (var city in civ.cities)
                    if (city != null)
                    {
                        city.OnBuildingCompleted += HandleBuildingCompleted;
                        city.OnBuildingRemoved += HandleBuildingRemoved;
                    }
            subscribedCivs.Add(i);
        }
    }

    private void UnsubscribeFromAllCivs()
    {
        var allCivs = CivilizationManager.Instance?.GetAllCivs();
        if (allCivs == null) return;
        foreach (int i in subscribedCivs)
        {
            if (i < 0 || i >= allCivs.Count) continue;
            var civ = allCivs[i];
            if (civ == null) continue;
            civ.OnTechResearched -= HandleTechResearched;
            civ.OnCultureCompleted -= HandleCultureCompleted;
            civ.OnPolicyAdopted -= HandlePolicyAdopted;
            civ.OnGovernmentChanged -= HandleGovernmentChanged;
            civ.OnCityFounded -= HandleCityFounded;
            civ.OnPantheonFounded -= HandlePantheonFounded;
            civ.OnUnitTrained -= HandleUnitTrained;
            if (civ.cities != null)
                foreach (var city in civ.cities)
                    if (city != null)
                    {
                        city.OnBuildingCompleted -= HandleBuildingCompleted;
                        city.OnBuildingRemoved -= HandleBuildingRemoved;
                    }
        }
        subscribedCivs.Clear();
    }

    /// <summary>
    /// Stable per-session key used for activeMissions/save-data lookups. Uses Civilization.MapActorSlot
    /// (assigned once, monotonic, never reused/reassigned even after a civ is eliminated) rather than
    /// CivilizationManager.GetCivIndex (mutable list position that shifts when civs are removed from the
    /// roster - would silently corrupt/misattribute another civ's crisis mission progress after any
    /// civilization is eliminated, since activeMissions/save data persist across many turns).
    /// </summary>
    private int GetCivIndex(Civilization civ)
    {
        return civ != null ? civ.MapActorSlot : -1;
    }

    private void TrySubscribeToTurnManager()
    {
        if (subscribedToTurnManager || TurnManager.Instance == null) return;
        TurnManager.Instance.OnRoundStarted += HandleRoundStarted;
        subscribedToTurnManager = true;
        LogCrisisDebug("TrySubscribeToTurnManager", "Successfully subscribed to TurnManager.OnRoundStarted.");
    }

    private void TrySubscribeToGameEvents()
    {
        if (subscribedToGameEvents || GameEventManager.Instance == null) return;
        GameEventManager.Instance.OnUnitKilled += HandleUnitKilled;
        GameEventManager.Instance.OnUnitLost += HandleUnitLost;
        subscribedToGameEvents = true;
    }

    private void TrySubscribeToDiplomacy()
    {
        if (subscribedToDiplomacy || DiplomacyManager.Instance == null) return;
        DiplomacyManager.Instance.OnDiplomacyChanged += HandleDiplomacyChanged;
        subscribedToDiplomacy = true;
    }

    private void TrySubscribeToImprovementManager()
    {
        if (subscribedToImprovementManager || ImprovementManager.Instance == null) return;
        ImprovementManager.Instance.OnImprovementBuilt += HandleImprovementBuilt;
        ImprovementManager.Instance.OnImprovementRemoved += HandleImprovementRemoved;
        subscribedToImprovementManager = true;
    }

    private void TryUnsubscribeFromImprovementManager()
    {
        if (!subscribedToImprovementManager || ImprovementManager.Instance == null) return;
        ImprovementManager.Instance.OnImprovementBuilt -= HandleImprovementBuilt;
        ImprovementManager.Instance.OnImprovementRemoved -= HandleImprovementRemoved;
        subscribedToImprovementManager = false;
    }

    /// <summary>Reverse lookup for GetCivIndex - finds the civ whose stable MapActorSlot matches idx.</summary>
    private Civilization GetCivByIndex(int idx)
    {
        if (idx < 0 || CivilizationManager.Instance == null) return null;
        var all = CivilizationManager.Instance.GetAllCivs();
        if (all == null) return null;
        foreach (var civ in all)
            if (civ != null && civ.MapActorSlot == idx) return civ;
        return null;
    }

    // ═══════════════════════════════════════════════
    //  Utility
    // ═══════════════════════════════════════════════

    private void LogCrisisDebug(string step, string message)
    {
        if (!enableCrisisDebugLogging)
            return;

        Debug.Log($"[CrisisDebug][{step}] {message}");
    }

    private string DescribeCrisis(CrisisData crisis)
    {
        return crisis != null ? $"'{crisis.crisisName}'" : "<none>";
    }

    private string DescribeMission(MissionData mission)
    {
        return mission != null ? $"'{mission.missionName}'" : "<none>";
    }

    private string DescribeObjective(MissionData.Objective objective)
    {
        if (objective == null)
            return "<none>";

        string name = !string.IsNullOrWhiteSpace(objective.objectiveName) ? objective.objectiveName : objective.type.ToString();
        return $"'{name}'(type={objective.type}, target={objective.targetValue})";
    }

    private string DescribeCiv(Civilization civ)
    {
        if (civ == null)
            return "<null civ>";

        return !string.IsNullOrWhiteSpace(civ.civData?.civName) ? civ.civData.civName : civ.name;
    }

    private string DescribeFilter(object filter)
    {
        if (filter == null)
            return "<none>";

        if (filter is UnityEngine.Object unityObject)
            return $"{unityObject.GetType().Name}('{unityObject.name}')";

        return $"{filter.GetType().Name}({filter})";
    }

    private int CurrentTurn => GameManager.Instance != null ? GameManager.Instance.currentTurn : 0;
}
