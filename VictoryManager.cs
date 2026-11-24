using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central campaign victory engine. Tracks the major civilizations that participated in the match,
/// remembers eliminations after their GameObjects are destroyed, evaluates Domination, Science and
/// Culture, and hands a single <see cref="VictoryResult"/> to <see cref="GameManager"/>.
/// Lives in the Game scene and persists its bookkeeping through <see cref="SaveGameRegistry"/>.
/// </summary>
public sealed class VictoryManager : MonoBehaviour, ISaveGameParticipant
{
    public static VictoryManager Instance { get; private set; }

    /// <summary>Raised once when the match concludes.</summary>
    public static event Action<VictoryResult> MatchConcluded;

    private readonly List<string> majorRoster = new List<string>();
    private readonly List<string> initialMajorRoster = new List<string>();
    private readonly HashSet<string> eliminated = new HashSet<string>(StringComparer.Ordinal);
    private bool initialized;
    private VictoryMatchState restoredConclusion;

    public bool IsInitialized => initialized;
    public bool IsConcluded { get; private set; }
    public VictoryResult Result { get; private set; }
    public IReadOnlyList<string> MajorRoster => majorRoster;
    public IReadOnlyCollection<string> EliminatedCivilizations => eliminated;

    public string SaveKey => "VictoryManager_v1";

    public static VictoryManager EnsureInstance()
    {
        if (Instance != null)
            return Instance;
        var existing = FindAnyObjectByType<VictoryManager>();
        if (existing != null)
            return existing;
        return new GameObject("VictoryManager").AddComponent<VictoryManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        SaveGameRegistry.Register(this);
    }

    private void OnDestroy()
    {
        SaveGameRegistry.Unregister(this);
        if (Instance == this)
            Instance = null;
    }

    // ── Match lifecycle ───────────────────────────────────────────────────────

    /// <summary>
    /// Records the starting major-civilization roster. Call once after the initial playable roster
    /// has been registered (TurnManager.StartTurns). Clears any state from a previous match.
    /// </summary>
    public void InitializeMatch()
    {
        majorRoster.Clear();
        initialMajorRoster.Clear();
        eliminated.Clear();
        IsConcluded = false;
        Result = null;
        restoredConclusion = null;

        var manager = CivilizationManager.Instance;
        if (manager != null)
        {
            foreach (var civ in manager.GetAllCivs())
                TryAddToRoster(civ);
        }

        initialMajorRoster.AddRange(majorRoster);
        initialized = true;
        Debug.Log($"[VictoryManager] Match initialized with {majorRoster.Count} major civilizations.");
    }

    /// <summary>Majors that join after the match starts (e.g. breakaway realms) become domination rivals too.</summary>
    public void NotifyCivilizationRegistered(Civilization civ)
    {
        if (!initialized || IsConcluded)
            return;
        TryAddToRoster(civ);
    }

    /// <summary>Called by CivilizationManager when a civilization leaves the live registry.</summary>
    public void NotifyCivilizationRemoved(Civilization civ)
    {
        if (!initialized || !CanEvaluate())
            return;
        string name = CivName(civ);
        if (string.IsNullOrEmpty(name) || !majorRoster.Contains(name))
            return;
        if (eliminated.Add(name))
            Debug.Log($"[VictoryManager] {name} has been eliminated.");
        CheckDomination();
    }

    public void NotifyTechnologyCompleted(Civilization civ) => CheckScience(civ);
    public void NotifyCultureCompleted(Civilization civ) => CheckCulture(civ);

    /// <summary>Called after a save has been fully restored and the loading screen is gone.</summary>
    public void OnSaveRestored()
    {
        if (restoredConclusion != null)
        {
            var state = restoredConclusion;
            restoredConclusion = null;
            Civilization winner = FindLiveCiv(state.winnerCivName);
            var result = new VictoryResult
            {
                Winner = winner,
                WinnerCivName = state.winnerCivName,
                WinnerLeaderName = state.winnerLeaderName,
                Type = state.victoryType >= 0 ? (VictoryType?)(VictoryType)state.victoryType : null,
                Turn = state.victoryTurn,
                Reason = state.reason,
                PlayerWon = state.playerWon,
            };
            Conclude(result);
            return;
        }

        CheckDomination();
    }

    // ── Condition checks ──────────────────────────────────────────────────────

    public void CheckScience(Civilization civ)
    {
        if (!CanWin(civ)) return;
        var catalog = TechManager.Instance != null ? TechManager.Instance.allTechs : null;
        if (VictoryRules.IsCatalogComplete(catalog, civ.researchedTechs))
            Finish(civ, VictoryType.Science, $"{CivName(civ)} has researched every technology.");
    }

    public void CheckCulture(Civilization civ)
    {
        if (!CanWin(civ)) return;
        var catalog = CultureManager.Instance != null ? CultureManager.Instance.allCultures : null;
        if (VictoryRules.IsCatalogComplete(catalog, civ.researchedCultures))
            Finish(civ, VictoryType.Culture, $"{CivName(civ)} has adopted every culture.");
    }

    /// <summary>
    /// Re-evaluates domination for every live major. Safe to call after conquest, elimination,
    /// and vassal contract changes; no-ops during save restoration or after conclusion.
    /// </summary>
    public void CheckDomination()
    {
        if (!initialized || !CanEvaluate() || CivilizationManager.Instance == null)
            return;

        var live = CivilizationManager.Instance.GetAllCivs();
        for (int i = 0; i < live.Count; i++)
        {
            var candidate = live[i];
            if (!CanWin(candidate)) continue;
            if (EvaluateDomination(candidate, out _, out _))
            {
                Finish(candidate, VictoryType.Domination,
                    $"{CivName(candidate)} has conquered or vassalized every rival civilization.");
                return;
            }
        }
    }

    // ── Progress ──────────────────────────────────────────────────────────────

    public VictoryProgress GetProgress(Civilization civ, VictoryType type)
    {
        var progress = new VictoryProgress { Type = type };
        if (civ == null) return progress;

        switch (type)
        {
            case VictoryType.Domination:
                EvaluateDomination(civ, initialMajorRoster, out progress.Current, out progress.Total);
                break;
            case VictoryType.Science:
                progress.Current = VictoryRules.CountCatalogCompletion(
                    TechManager.Instance != null ? TechManager.Instance.allTechs : null,
                    civ.researchedTechs, out progress.Total);
                break;
            case VictoryType.Culture:
                progress.Current = VictoryRules.CountCatalogCompletion(
                    CultureManager.Instance != null ? CultureManager.Instance.allCultures : null,
                    civ.researchedCultures, out progress.Total);
                break;
        }
        return progress;
    }

    public List<VictoryProgress> GetAllProgress(Civilization civ)
    {
        var list = new List<VictoryProgress>(VictoryRules.EnabledTypes.Length);
        foreach (var type in VictoryRules.EnabledTypes)
            list.Add(GetProgress(civ, type));
        return list;
    }

    public bool IsEliminated(Civilization civ) => civ != null && eliminated.Contains(CivName(civ));
    public bool IsEliminated(string civName) => !string.IsNullOrEmpty(civName) && eliminated.Contains(civName);

    // ── Internals ─────────────────────────────────────────────────────────────

    private bool EvaluateDomination(Civilization candidate, out int resolved, out int total)
        => EvaluateDomination(candidate, majorRoster, out resolved, out total);

    private bool EvaluateDomination(Civilization candidate, IReadOnlyList<string> roster, out int resolved, out int total)
    {
        var subjects = SubjectManager.Instance;
        bool subordinated = subjects != null && subjects.IsSubject(candidate);
        return VictoryRules.EvaluateDomination(
            roster,
            CivName(candidate),
            IsDefeated(candidate) || subordinated,
            rivalName => IsRivalDefeated(rivalName),
            rivalName =>
            {
                if (subjects == null) return false;
                var rival = FindLiveCiv(rivalName);
                return rival != null && subjects.GetContract(candidate, rival) != null;
            },
            out resolved, out total);
    }

    private bool IsRivalDefeated(string rivalName)
    {
        if (eliminated.Contains(rivalName))
            return true;
        var rival = FindLiveCiv(rivalName);
        // Absent from the live registry without an elimination record (e.g. destroyed before
        // this manager saw it) still means the rival no longer exists in the match.
        return rival == null || IsDefeated(rival);
    }

    private static bool IsDefeated(Civilization civ)
    {
        if (civ == null) return true;
        if (civ.markedForRemoval) return true;
        bool hasCities = civ.cities != null && civ.cities.Exists(c => c != null);
        bool hasCombat = civ.combatUnits != null && civ.combatUnits.Exists(u => u != null);
        bool hasWorkers = civ.workerUnits != null && civ.workerUnits.Exists(u => u != null);
        bool hasBands = civ.bands != null && civ.bands.Exists(b => b != null);
        return !hasCities && !hasCombat && !hasWorkers && !hasBands;
    }

    private bool CanEvaluate()
    {
        if (IsConcluded) return false;
        var gm = GameManager.Instance;
        return gm != null && gm.gameInProgress && !gm.IsRestoringSave && !gm.IsMatchConcluded;
    }

    private bool CanWin(Civilization civ)
    {
        if (!initialized || !CanEvaluate() || !IsMajor(civ)) return false;
        string name = CivName(civ);
        return !eliminated.Contains(name) && !IsDefeated(civ) && majorRoster.Contains(name);
    }

    private void Finish(Civilization winner, VictoryType type, string reason)
    {
        if (!CanWin(winner) || !VictoryRules.IsEnabled(type)) return;

        var gm = GameManager.Instance;
        var player = CivilizationManager.Instance != null ? CivilizationManager.Instance.playerCiv : null;
        var result = new VictoryResult
        {
            Winner = winner,
            WinnerCivName = CivName(winner),
            WinnerLeaderName = winner.leader != null ? winner.leader.leaderName : null,
            Type = type,
            Turn = gm != null ? gm.currentTurn : 0,
            Reason = reason,
            PlayerWon = player != null && player == winner,
        };
        Conclude(result);
    }

    private void Conclude(VictoryResult result)
    {
        if (IsConcluded || result == null) return;
        IsConcluded = true;
        Result = result;
        Debug.Log($"[VictoryManager] {result.WinnerCivName ?? "No one"} — {result.TypeDisplayName} on turn {result.Turn}.");
        MatchConcluded?.Invoke(result);
        GameManager.Instance?.ConcludeMatch(result);
    }

    private void TryAddToRoster(Civilization civ)
    {
        if (!IsMajor(civ)) return;
        string name = CivName(civ);
        if (!string.IsNullOrEmpty(name) && !majorRoster.Contains(name))
            majorRoster.Add(name);
    }

    private static bool IsMajor(Civilization civ)
        => civ != null && civ.civData != null && !civ.civData.isTribe && !civ.civData.isCityState;

    private static string CivName(Civilization civ)
        => civ != null && civ.civData != null ? civ.civData.civName : null;

    private static Civilization FindLiveCiv(string civName)
    {
        if (string.IsNullOrEmpty(civName) || CivilizationManager.Instance == null) return null;
        var live = CivilizationManager.Instance.GetAllCivs();
        for (int i = 0; i < live.Count; i++)
        {
            var civ = live[i];
            if (civ != null && civ.civData != null && string.Equals(civ.civData.civName, civName, StringComparison.Ordinal))
                return civ;
        }
        return null;
    }

    // ── ISaveGameParticipant ──────────────────────────────────────────────────

    public string CaptureStateJson()
    {
        var state = new VictoryMatchState
        {
            majorCivNames = new List<string>(majorRoster),
            initialMajorCivNames = new List<string>(initialMajorRoster),
            eliminatedCivNames = new List<string>(eliminated),
            concluded = IsConcluded,
        };
        if (Result != null)
        {
            state.winnerCivName = Result.WinnerCivName;
            state.winnerLeaderName = Result.WinnerLeaderName;
            state.victoryType = Result.Type.HasValue ? (int)Result.Type.Value : -1;
            state.victoryTurn = Result.Turn;
            state.reason = Result.Reason;
            state.playerWon = Result.PlayerWon;
        }
        return JsonUtility.ToJson(state);
    }

    public void RestoreStateJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        var state = JsonUtility.FromJson<VictoryMatchState>(json);
        if (state == null) return;

        // Saved roster wins over whatever was regenerated so conquered rivals are never forgotten,
        // but keep any regenerated majors that the save somehow lacks.
        var merged = new List<string>(state.majorCivNames ?? new List<string>());
        foreach (string name in majorRoster)
            if (!merged.Contains(name)) merged.Add(name);
        majorRoster.Clear();
        majorRoster.AddRange(merged);
        initialMajorRoster.Clear();
        initialMajorRoster.AddRange(state.initialMajorCivNames ?? state.majorCivNames ?? merged);

        eliminated.Clear();
        if (state.eliminatedCivNames != null)
            foreach (string name in state.eliminatedCivNames)
                if (!string.IsNullOrEmpty(name)) eliminated.Add(name);

        initialized = majorRoster.Count > 0 || initialized;
        restoredConclusion = state.concluded ? state : null;
        Debug.Log($"[VictoryManager] Restored roster={majorRoster.Count} eliminated={eliminated.Count} concluded={state.concluded}.");
    }
}
