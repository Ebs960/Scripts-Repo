using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Central campaign victory rules. Only Domination, Science and Culture can end a match.
/// Keeps the initial roster because conquered civilizations are removed from the live registry.
/// Invoke InitializeMatch once after the starting civilizations are registered.
/// </summary>
public static class VictoryManager
{
    public static readonly VictoryType[] EnabledTypes =
    {
        VictoryType.Domination, VictoryType.Science, VictoryType.Culture
    };

    private static readonly List<CivData> startingPlayableCivs = new List<CivData>();
    private static bool initialized;
    private static bool concluded;

    public static bool IsEnabled(VictoryType type)
        => type == VictoryType.Domination || type == VictoryType.Science || type == VictoryType.Culture;

    public static void InitializeMatch()
    {
        startingPlayableCivs.Clear();
        concluded = false;
        initialized = false;

        var manager = CivilizationManager.Instance;
        if (manager == null) return;

        foreach (var civ in manager.GetAllCivs())
        {
            if (civ == null || civ.civData == null || civ.civData.isTribe || civ.civData.isCityState)
                continue;
            if (!startingPlayableCivs.Contains(civ.civData))
                startingPlayableCivs.Add(civ.civData);
        }

        // Never award domination in a solo game or before the full roster exists.
        initialized = startingPlayableCivs.Count >= 2;
    }

    /// <summary>Called when a technology finishes researching.</summary>
    public static void CheckScience(Civilization civ)
    {
        if (!CanWin(civ)) return;
        var catalog = TechManager.Instance != null ? TechManager.Instance.allTechs : null;
        if (catalog == null) return;
        var required = catalog.Where(t => t != null).Distinct().ToList();
        if (required.Count == 0 || civ.researchedTechs == null) return;
        if (required.All(civ.researchedTechs.Contains))
            Finish(civ, VictoryType.Science, "Science Victory: all technologies researched.");
    }

    /// <summary>Called when a culture is adopted.</summary>
    public static void CheckCulture(Civilization civ)
    {
        if (!CanWin(civ)) return;
        var catalog = CultureManager.Instance != null ? CultureManager.Instance.allCultures : null;
        if (catalog == null) return;
        var required = catalog.Where(c => c != null).Distinct().ToList();
        if (required.Count == 0 || civ.researchedCultures == null) return;
        if (required.All(civ.researchedCultures.Contains))
            Finish(civ, VictoryType.Culture, "Culture Victory: all cultures researched.");
    }

    /// <summary>
    /// Re-evaluate after a vassal contract changes or a civilization is eliminated.
    /// Tribes and city-states do not count as opponents.
    /// </summary>
    public static void CheckDomination()
    {
        if (!initialized || concluded || GameManager.Instance == null ||
            !GameManager.Instance.gameInProgress || CivilizationManager.Instance == null)
            return;

        var live = CivilizationManager.Instance.GetAllCivs();
        foreach (var candidate in live)
        {
            if (!CanWin(candidate)) continue;
            bool allResolved = true;
            foreach (var rivalData in startingPlayableCivs)
            {
                if (rivalData == candidate.civData) continue;

                // Absent from the live registry means the rival has been conquered/eliminated.
                Civilization rival = null;
                foreach (var c in live)
                {
                    if (c != null && c.civData == rivalData) { rival = c; break; }
                }
                if (rival == null) continue;

                // A surviving rival must be a direct subject of the candidate.
                if (SubjectManager.Instance == null ||
                    SubjectManager.Instance.GetContract(candidate, rival) == null)
                {
                    allResolved = false;
                    break;
                }
            }
            if (allResolved)
            {
                Finish(candidate, VictoryType.Domination,
                    "Domination Victory: every rival civilization has been conquered or vassalized.");
                return;
            }
        }
    }

    private static bool CanWin(Civilization civ)
        => initialized && !concluded && civ != null && civ.civData != null &&
           !civ.civData.isTribe && !civ.civData.isCityState &&
           startingPlayableCivs.Contains(civ.civData) &&
           GameManager.Instance != null && GameManager.Instance.gameInProgress;

    private static void Finish(Civilization winner, VictoryType type, string reason)
    {
        if (!CanWin(winner) || !IsEnabled(type)) return;
        concluded = true; // Prevent duplicate finish requests from research and turn callbacks.
        Debug.Log($"[VictoryManager] {winner.civData.civName} won by {type}.");
        GameManager.Instance.CompleteVictory(winner, type, reason);
    }
}
