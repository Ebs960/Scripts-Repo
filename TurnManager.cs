// Assets/Scripts/Managers/TurnManager.cs
using System;
using System.Collections.Generic;
using UnityEngine;
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
    /// <summary> Fired when a civilization has begun its turn (after BeginTurn). </summary>
    public event Action<Civilization, int> OnCivTurnStarted;
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
                OnRoundEnded?.Invoke(round);
                OnNeutralTurn?.Invoke(round);

                // Yield a frame so any coroutines started by neutral-turn subscribers
                // (e.g. AnimalManager) can begin processing before the next round.
                yield return null;

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
            }
        }

        // Prune civilizations that have no cities and no units to avoid stalled turns
        if (civs.Count > 0)
        {
            var snapshot = civs.ToArray();
            bool removedAny = false;
            foreach (var c in snapshot)
            {
                if (c == null)
                {
                    civs.Remove(c);
                    removedAny = true;
                    continue;
                }

                // Remove any null entries that may have accumulated in the civ's lists
                try { c.cities?.RemoveAll(x => x == null); } catch { }
                try { c.combatUnits?.RemoveAll(x => x == null); } catch { }
                try { c.workerUnits?.RemoveAll(x => x == null); } catch { }

                bool hasCities = c.cities != null && c.cities.Count > 0;
                bool hasCombat = c.combatUnits != null && c.combatUnits.Count > 0;
                bool hasWorkers = c.workerUnits != null && c.workerUnits.Count > 0;

                if (!hasCities && !hasCombat && !hasWorkers)
                {
                    Debug.Log($"TurnManager: Removing empty civilization '{c.civData?.civName ?? "(unknown)"}'");
                    // Remove from turn list and unregister with CivilizationManager, then destroy object
                    civs.Remove(c);
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

            // If we removed any civs, reset index so turn order remains valid
            if (removedAny) currentIndex = -1;
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

        OnCivTurnStarting?.Invoke(civ, round);
        OnAIProcessingChanged?.Invoke(!isPlayer, civ);

        civ.BeginTurn(round);

        // Simple automatic worker contribution: call the same public methods the UI uses.
        // Keeps logic minimal (no new scripts) — workers will attempt to contribute work at their turn start.
        if (civ != null && civ.workerUnits != null)
        {
            foreach (var w in civ.workerUnits)
            {
                if (w == null) continue;
                // Call the public contribution methods (these already guard for no-job / no-points)
                w.ContributeWork();
                w.ContributeWorkToUnit();
                w.ContributeWorkToWorker();
            }
        }

        OnCivTurnStarted?.Invoke(civ, round);
        // Legacy: many systems subscribe here.
        OnTurnChanged?.Invoke(civ, round);

        if (!isPlayer)
        {
            if (CivilizationManager.Instance != null)
                yield return CivilizationManager.Instance.PerformAITurnCoroutine(civ);
            // FIXED: Remove recursive StartCoroutine to prevent infinite call stack
            // Instead, yield return null then call AdvanceTurn() normally
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
