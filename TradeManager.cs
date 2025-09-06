// Assets/Scripts/Trade/TradeManager.cs
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central manager for the trade system. Tracks global unlock state and which civilizations
/// have trade enabled. Provides helpers for UI and gameplay code to query availability.
/// </summary>
public class TradeManager : MonoBehaviour
{
    public static TradeManager Instance { get; private set; }

    [Tooltip("When true, trade is available for all civilizations.")]
    public bool globalTradeEnabled = false;

    // Track specific civilizations that have trade unlocked (if global not enabled)
    private HashSet<Civilization> civsWithTrade = new HashSet<Civilization>();

    public event Action OnGlobalTradeEnabled;
    public event Action<Civilization> OnCivilizationTradeEnabled;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    /// <summary>
    /// Unlock trade globally for all civilizations.
    /// </summary>
    public void UnlockGlobalTrade()
    {
        if (globalTradeEnabled) return;
        globalTradeEnabled = true;
        OnGlobalTradeEnabled?.Invoke();
        UIManager.Instance?.ShowNotification("Global trade system unlocked!");
    }

    /// <summary>
    /// Unlock trade for a specific civilization.
    /// </summary>
    public void UnlockTradeForCivilization(Civilization civ)
    {
        if (civ == null) return;
        if (globalTradeEnabled)
        {
            // Already globally enabled - ensure civ flag is consistent
            civ.tradeEnabled = true;
            OnCivilizationTradeEnabled?.Invoke(civ);
            return;
        }

        if (civsWithTrade.Contains(civ)) return;
        civsWithTrade.Add(civ);
        civ.tradeEnabled = true; // keep per-civ flag in sync
        OnCivilizationTradeEnabled?.Invoke(civ);
        UIManager.Instance?.ShowNotification($"{civ.civData.civName} has unlocked trade!");
    }

    /// <summary>
    /// Returns true when trade is available for the given civilization (either global or per-civ).
    /// </summary>
    public bool IsTradeEnabledForCivilization(Civilization civ)
    {
        if (globalTradeEnabled) return true;
        if (civ == null) return false;
        if (civ.tradeEnabled) return true;
        return civsWithTrade.Contains(civ);
    }
}
