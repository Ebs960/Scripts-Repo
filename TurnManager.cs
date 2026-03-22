// Assets/Scripts/Managers/TurnManager.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;
using System.Collections;

public class TurnManager : MonoBehaviour
{
    public static TurnManager Instance { get; private set; }

    /// <summary>
    /// Fired whenever the active civilization changes (after <see cref="Civilization.BeginTurn"/>).
    /// Legacy event kept for existing subscribers.
    /// </summary>
    public event Action<Civilization, int> OnTurnChanged;

    /// <summary> Fired when a new round begins. </summary>
    public event Action<int> OnRoundStarted;
    /// <summary> Fired when a round ends (after the last civ ends its turn, before incrementing). </summary>
    public event Action<int> OnRoundEnded;
    /// <summary>
    /// Fired once per round between rounds for "world"/neutral systems (e.g. animals).
    /// This runs after <see cref="OnRoundEnded"/> and before <see cref="OnRoundStarted"/> of the next round.
    /// </summary>
    public event Action<int> OnNeutralTurn;
    /// <summary> Fired when a civilization is about to begin its turn (before BeginTurn). </summary>
    public event Action<Civilization, int> OnCivTurnStarting;
    /// <summary> Fired when a civilization ends its turn (triggered by turn advance). </summary>
    public event Action<Civilization, int> OnCivTurnEnded;
    /// <summary> Fired when AI processing begins or ends. </summary>
    public event Action<bool, Civilization> OnAIProcessingChanged;

    [Tooltip("Assign your human player Civilization here")]
    public Civilization playerCiv;

    private List<Civilization> civs = new List<Civilization>();
    private int currentIndex = -1;
    public int round = 1;
    private bool turnsStarted = false;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    void Start()
    {
        // Don't automatically gather civs here - let CivilizationManager register them
        // civs.AddRange(FindObjectsByType<Civilization>(FindObjectsSortMode.None));
    }

    /// <summary>
    /// Register a civilization with the turn manager
    /// </summary>
    public void RegisterCivilization(Civilization civ)
    {
        if (civ != null && !civs.Contains(civ))
        {
            civs.Add(civ);
        }
    }

    /// <summary>
    /// Unregister a civilization from the turn manager. Safe to call multiple times.
    /// </summary>
    public void UnregisterCivilization(Civilization civ)
    {
        if (civ == null) return;
        civs.RemoveAll(x => x == null);
        if (civs.Contains(civ))
        {
            int idx = civs.IndexOf(civ);
            civs.RemoveAt(idx);
            if (playerCiv == civ) playerCiv = null;
            // adjust currentIndex if necessary
            if (idx <= currentIndex) currentIndex = Mathf.Max(-1, currentIndex - 1);
        }
    }

    /// <summary>
    /// Begins the turn cycle. Call this once after spawning all civs.
    /// </summary>
    public void StartTurns()
    {
        if (turnsStarted)
        {
            Debug.LogWarning("TurnManager: Turns already started!");
            return;
        }

        if (civs.Count == 0)
        {
            Debug.LogError("TurnManager: No civilizations registered! Cannot start turns.");
            return;
        }

        if (playerCiv == null)
        {
            Debug.LogError("TurnManager: Player civilization not assigned!");
            return;
        }

        round = 1;
        currentIndex = -1;
        turnsStarted = true;

        var gameManager = GameManager.Instance;
        if (gameManager != null)
        {
            gameManager.currentTurn = round;
        }
        else
        {
            Debug.LogWarning("TurnManager: GameManager instance not found when starting turns.");
        }

        OnRoundStarted?.Invoke(round);
        StartCoroutine(AdvanceTurnCoroutine());
    }

    /// <summary>
    /// Advances to the next civilization's turn.
    /// </summary>
    public void AdvanceTurn()
    {
        StartCoroutine(AdvanceTurnCoroutine());
    }

    private IEnumerator InvokeTurnChangedHandlers(Civilization civ, int round, bool isPlayer, string civType, string civLabel)
    {
        var handlers = OnTurnChanged;
        if (handlers == null)
            yield break;

        foreach (Action<Civilization, int> handler in handlers.GetInvocationList())
        {
            var handlerSw = Stopwatch.StartNew();
            try
            {
                handler(civ, round);
            }
            catch (Exception ex)
            {
                string owner = handler.Method.DeclaringType != null ? handler.Method.DeclaringType.Name : "<unknown>";
                Debug.LogError($"[TurnManager] OnTurnChanged handler {owner}.{handler.Method.Name} failed for {civType} '{civLabel}' round={round}: {ex}");
            }

            long handlerMs = handlerSw.ElapsedMilliseconds;
            if (handlerMs > 10)
            {
                string owner = handler.Method.DeclaringType != null ? handler.Method.DeclaringType.Name : "<unknown>";
                Debug.Log($"[TurnProfile] {civType} '{civLabel}' OnTurnChanged handler {owner}.{handler.Method.Name}={handlerMs}ms");
            }

            if (!isPlayer)
                yield return null;
        }
    }

    private IEnumerator AdvanceTurnCoroutine()
    {
        if (!turnsStarted)
        {
            Debug.LogWarning("TurnManager: AdvanceTurn called before StartTurns()!");
            yield break;
        }

        // End the current civ's turn (if any)
        if (currentIndex >= 0 && currentIndex < civs.Count)
        {
            var endingCiv = civs[currentIndex];
            OnCivTurnEnded?.Invoke(endingCiv, round);

            // If we just ended the last civ in the list, run end-of-round phases.
            if (currentIndex == civs.Count - 1)
            {
                var roundSw = Stopwatch.StartNew();
                OnRoundEnded?.Invoke(round);
                long msRoundEnded = roundSw.ElapsedMilliseconds;
                OnNeutralTurn?.Invoke(round);
                // Wait for animal processing to complete before advancing to next round
                // (previously fire-and-forget — animals kept moving through civ turns).
                if (AnimalManager.Instance != null)
                    yield return StartCoroutine(AnimalManager.Instance.ProcessTurnCoroutine());
                long msNeutralTurn = roundSw.ElapsedMilliseconds;
                Debug.Log($"[TurnProfile] ROUND-END round={round} | OnRoundEnded={msRoundEnded}ms OnNeutralTurn={msNeutralTurn - msRoundEnded}ms");

                round++;

                var gmRoundAdvance = GameManager.Instance;
                if (gmRoundAdvance != null)
                {
                    gmRoundAdvance.currentTurn = round;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Assert(gmRoundAdvance.currentTurn == round, "TurnManager: GameManager turn counter out of sync after advancing round.");
#endif
                }

                OnRoundStarted?.Invoke(round);

                // --- Prune dead civs once per round (not every advance) ---
                bool removedAny = false;
                for (int pi = civs.Count - 1; pi >= 0; pi--)
                {
                    var c = civs[pi];
                    if (c == null)
                    {
                        civs.RemoveAt(pi);
                        removedAny = true;
                        continue;
                    }

                    bool hasCities = c.cities != null && c.cities.Count > 0;
                    bool hasCombat = c.combatUnits != null && c.combatUnits.Count > 0;
                    bool hasWorkers = c.workerUnits != null && c.workerUnits.Count > 0;

                    if (!hasCities && !hasCombat && !hasWorkers)
                    {
                        Debug.Log($"TurnManager: Removing empty civilization '{c.civData?.civName ?? "(unknown)"}'");
                        civs.RemoveAt(pi);
                        try { CivilizationManager.Instance?.UnregisterCiv(c); } catch { }
                        try { if (c.gameObject != null) Destroy(c.gameObject); } catch { }
                        if (playerCiv == c) playerCiv = null;
                        removedAny = true;
                    }
                }

                if (civs.Count == 0)
                {
                    Debug.LogWarning("TurnManager: No civilizations remain after pruning. Stopping turns.");
                    yield break;
                }

                if (removedAny) currentIndex = -1;

                // Yield another frame after round-end housekeeping so the player turn
                // doesn't share a frame with pruning + OnRoundStarted work.
                yield return null;
            }
        }

        // Advance to next civ
        currentIndex++;
        if (currentIndex >= civs.Count) currentIndex = 0;

        var civ = civs[currentIndex];
        bool isPlayer = civ == playerCiv;
        var gameManager = GameManager.Instance;
        if (gameManager != null)
        {
            gameManager.currentTurn = round;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Assert(gameManager.currentTurn == round, "TurnManager: GameManager turn counter out of sync after advancing turn.");
#endif
        }
        else
        {
            Debug.LogWarning("TurnManager: GameManager instance not found when advancing turns.");
        }

        string civLabel = civ.civData != null ? civ.civData.civName : "?";
        bool isTribe = civ.civData != null && civ.civData.isTribe;
        bool isCityState = civ.civData != null && civ.civData.isCityState;
        string civType = isTribe ? "TRIBE" : isCityState ? "CITYSTATE" : isPlayer ? "PLAYER" : "AI";
        var sw = Stopwatch.StartNew();

        OnCivTurnStarting?.Invoke(civ, round);
        OnAIProcessingChanged?.Invoke(!isPlayer, civ);

        civ.BeginTurn(round);
        long msBeginTurn = sw.ElapsedMilliseconds;

        // Guard: if BeginTurn flagged this civ for removal (lost all units/cities via famine, etc.)
        // skip the rest of its turn — it will be pruned at end of round.
        if (civ == null || civ.markedForRemoval)
        {
            Debug.Log($"[TurnProfile] {civType} '{civLabel}' SKIPPED (marked for removal after BeginTurn)");
            if (!isPlayer)
            {
                yield return null;
                AdvanceTurn();
            }
            yield break;
        }

        // Simple automatic worker contribution: call the same public methods the UI uses.
        if (civ != null && civ.workerUnits != null)
        {
            foreach (var w in civ.workerUnits)
            {
                if (w == null) continue;
                w.ContributeWork();
                w.ContributeWorkToUnit();
                w.ContributeWorkToWorker();
            }
        }
        long msWorkerContrib = sw.ElapsedMilliseconds;

        long msBeforeTurnChanged = sw.ElapsedMilliseconds;
        yield return StartCoroutine(InvokeTurnChangedHandlers(civ, round, isPlayer, civType, civLabel));
        long msOnTurnChanged = sw.ElapsedMilliseconds;

        Debug.Log($"[TurnProfile] {civType} '{civLabel}' round={round} | BeginTurn={msBeginTurn}ms Workers={msWorkerContrib - msBeginTurn}ms OnTurnChanged={msOnTurnChanged - msBeforeTurnChanged}ms total={msOnTurnChanged}ms | cities={civ.cities?.Count ?? 0} combat={civ.combatUnits?.Count ?? 0} workers={civ.workerUnits?.Count ?? 0}");

        if (!isPlayer)
        {
            long msBeforeAI = sw.ElapsedMilliseconds;
            if (CivilizationManager.Instance != null)
                yield return CivilizationManager.Instance.PerformAITurnCoroutine(civ);
            long msAfterAI = sw.ElapsedMilliseconds;
            Debug.Log($"[TurnProfile] {civType} '{civLabel}' AI={msAfterAI - msBeforeAI}ms");
            yield return null;
            AdvanceTurn();
        }
        // else: wait for player to end turn
    }

    /// <summary>
    /// Hook this to your "End Turn" button.
    /// </summary>
    public void EndPlayerTurn()
    {
        AdvanceTurn();
    }

    /// <summary>
    /// Get the current active civilization
    /// </summary>
    public Civilization GetCurrentCivilization()
    {
        if (currentIndex >= 0 && currentIndex < civs.Count)
            return civs[currentIndex];
        return null;
    }
}
