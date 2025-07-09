// Assets/Scripts/Managers/TurnManager.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using System.Collections;

public class TurnManager : MonoBehaviour
{
    public static TurnManager Instance { get; private set; }

    /// <summary> Fired whenever the active civilization changes. </summary>
    public event Action<Civilization, int> OnTurnChanged;
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
            Debug.Log($"TurnManager: Registered civilization {civ.civData.civName}");
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
        
        Debug.Log($"TurnManager: Starting turn cycle with {civs.Count} civilizations. Player civ: {playerCiv.civData.civName}");
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

        currentIndex++;
        if (currentIndex >= civs.Count)
        {
            currentIndex = 0;
            round++;
        }

        var civ = civs[currentIndex];
        bool isPlayer = civ == playerCiv;

        Debug.Log($"TurnManager: Turn {round}, Civ: {civ.civData.civName}, IsPlayer: {isPlayer}");

        if (isPlayer && round > 1 && AnimalManager.Instance != null)
            AnimalManager.Instance.ProcessTurn();

        OnTurnChanged?.Invoke(civ, round);
        OnAIProcessingChanged?.Invoke(!isPlayer, civ);

        civ.BeginTurn(round);

        if (ImprovementManager.Instance != null)
            ImprovementManager.Instance.ProcessTurn(civ);

        if (!isPlayer)
        {
            if (CivilizationManager.Instance != null)
                yield return CivilizationManager.Instance.PerformAITurnCoroutine(civ);
            StartCoroutine(AdvanceTurnCoroutine());
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
