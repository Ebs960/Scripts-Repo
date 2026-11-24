using System;
using System.Collections.Generic;

/// <summary>
/// Pure victory-condition rules with no scene or singleton dependencies so they can be unit tested.
/// <see cref="VictoryManager"/> adapts live game state onto these helpers.
/// </summary>
public static class VictoryRules
{
    /// <summary>The only victory types that can currently end a match. Order is used for UI/AI iteration.</summary>
    public static readonly VictoryType[] EnabledTypes =
    {
        VictoryType.Domination,
        VictoryType.Science,
        VictoryType.Culture,
    };

    public static bool IsEnabled(VictoryType type)
    {
        return type == VictoryType.Domination || type == VictoryType.Science || type == VictoryType.Culture;
    }

    /// <summary>
    /// Maps leader preferences for not-yet-implemented victory types onto a supported path so AI
    /// planning never pursues a condition that cannot be won.
    /// </summary>
    public static VictoryType NormalizeForAi(VictoryType preferred)
    {
        switch (preferred)
        {
            case VictoryType.Religious:
            case VictoryType.Diplomatic:
                return VictoryType.Culture;
            case VictoryType.Economic:
                return VictoryType.Science;
            default:
                return IsEnabled(preferred) ? preferred : VictoryType.Domination;
        }
    }

    public static string DisplayName(VictoryType? type)
    {
        switch (type)
        {
            case VictoryType.Domination: return "Domination Victory";
            case VictoryType.Science: return "Science Victory";
            case VictoryType.Culture: return "Culture Victory";
            case null: return "Match Concluded";
            default: return type.Value + " Victory";
        }
    }

    /// <summary>
    /// Counts how many distinct, non-null catalog entries appear in <paramref name="completed"/>.
    /// Entries outside the catalog never count, so stale or foreign references cannot inflate progress.
    /// </summary>
    public static int CountCatalogCompletion<T>(IEnumerable<T> catalog, IEnumerable<T> completed, out int total) where T : class
    {
        total = 0;
        if (catalog == null)
            return 0;

        var required = new HashSet<T>();
        foreach (var entry in catalog)
            if (entry != null)
                required.Add(entry);
        total = required.Count;
        if (total == 0 || completed == null)
            return 0;

        var seen = new HashSet<T>();
        int count = 0;
        foreach (var entry in completed)
            if (entry != null && required.Contains(entry) && seen.Add(entry))
                count++;
        return count;
    }

    /// <summary>True only when the catalog is non-empty and every entry has been completed.</summary>
    public static bool IsCatalogComplete<T>(IEnumerable<T> catalog, IEnumerable<T> completed) where T : class
    {
        int done = CountCatalogCompletion(catalog, completed, out int total);
        return total > 0 && done == total;
    }

    /// <summary>
    /// Evaluates domination for one candidate against the major-civilization roster that participated in the match.
    /// A rival is resolved when it is defeated or a direct subject of the candidate. Tribes and city-states must be
    /// excluded from <paramref name="majorRoster"/> by the caller.
    /// </summary>
    /// <returns>True when at least one rival existed and all rivals are resolved.</returns>
    public static bool EvaluateDomination(
        IReadOnlyList<string> majorRoster,
        string candidate,
        bool candidateDefeatedOrSubordinated,
        Func<string, bool> isRivalDefeated,
        Func<string, bool> isRivalDirectSubjectOfCandidate,
        out int resolvedRivals,
        out int totalRivals)
    {
        resolvedRivals = 0;
        totalRivals = 0;
        if (majorRoster == null || string.IsNullOrEmpty(candidate))
            return false;

        bool candidateInRoster = false;
        var counted = new HashSet<string>(StringComparer.Ordinal);
        foreach (string rival in majorRoster)
        {
            if (string.IsNullOrEmpty(rival) || !counted.Add(rival))
                continue;
            if (string.Equals(rival, candidate, StringComparison.Ordinal))
            {
                candidateInRoster = true;
                continue;
            }

            totalRivals++;
            if ((isRivalDefeated != null && isRivalDefeated(rival))
                || (isRivalDirectSubjectOfCandidate != null && isRivalDirectSubjectOfCandidate(rival)))
                resolvedRivals++;
        }

        if (!candidateInRoster || candidateDefeatedOrSubordinated)
            return false;
        return totalRivals > 0 && resolvedRivals == totalRivals;
    }
}

/// <summary>Read-only progress toward one victory type for a civilization.</summary>
public struct VictoryProgress
{
    public VictoryType Type;
    public int Current;
    public int Total;

    /// <summary>False when the underlying catalog/roster is empty and the condition cannot be achieved.</summary>
    public bool Available => Total > 0;
    public float Fraction => Total > 0 ? (float)Current / Total : 0f;

    public override string ToString()
    {
        switch (Type)
        {
            case VictoryType.Domination:
                return $"Domination: {Current} of {Total} rival civilizations conquered or vassalized.";
            case VictoryType.Science:
                return $"Science: {Current} of {Total} technologies researched.";
            case VictoryType.Culture:
                return $"Culture: {Current} of {Total} cultures researched.";
            default:
                return $"{Type}: {Current} of {Total}.";
        }
    }
}

/// <summary>Structured outcome handed to <see cref="GameManager"/> when a match concludes.</summary>
public sealed class VictoryResult
{
    public Civilization Winner;
    public string WinnerCivName;
    public string WinnerLeaderName;
    /// <summary>Null for non-victory conclusions such as a conceded election.</summary>
    public VictoryType? Type;
    public int Turn;
    public string Reason;
    public bool PlayerWon;

    public string TypeDisplayName => VictoryRules.DisplayName(Type);
}

/// <summary>Serialized victory bookkeeping persisted through the save-participant registry.</summary>
[Serializable]
public sealed class VictoryMatchState
{
    public int version = 1;
    public List<string> majorCivNames = new List<string>();
    public List<string> eliminatedCivNames = new List<string>();
    public bool concluded;
    public string winnerCivName;
    public string winnerLeaderName;
    /// <summary>Cast of <see cref="VictoryType"/>, or -1 when the match ended without a victory type.</summary>
    public int victoryType = -1;
    public int victoryTurn;
    public string reason;
    public bool playerWon;
}
