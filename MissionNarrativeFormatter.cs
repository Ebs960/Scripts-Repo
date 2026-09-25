using System.Text.RegularExpressions;

/// <summary>Single token resolver used by mission selection and active mission UI.</summary>
public static class MissionNarrativeFormatter
{
    private static readonly Regex ObjectiveToken = new Regex(@"\{Objective(\d+)(Target|HoldTurns)\}");
    private static readonly Regex UnresolvedToken = new Regex(@"\{[A-Za-z][A-Za-z0-9]*\}");

    public static string Resolve(string text, MissionData mission, CrisisManager manager, Civilization civ, CrisisManager.MissionState state=null)
    {
        if (string.IsNullOrEmpty(text) || mission == null) return text ?? string.Empty;
        string resolved = ObjectiveToken.Replace(text, match => {
            int index;
            if (!int.TryParse(match.Groups[1].Value,out index) || index<0 || index>=mission.objectives.Count) return string.Empty;
            var objective=mission.objectives[index];
            if (match.Groups[2].Value=="HoldTurns") return objective.requiredConsecutiveTurns.ToString();
            if (state?.resolvedTargets != null && index<state.resolvedTargets.Length) return state.resolvedTargets[index].ToString();
            return (manager != null ? manager.PreviewObjectiveTarget(objective,civ) : objective.targetValue).ToString();
        });
        var snapshot = state?.narrativeSnapshot ?? manager?.GetNarrativeSnapshot(civ);
        resolved = resolved.Replace("{FactionName}", snapshot?.factionName ?? "the concerned faction")
            .Replace("{Demand}", snapshot?.demand ?? "a settlement of its outstanding demand")
            .Replace("{FactionDemandSummary}", snapshot?.factionDemandSummary ?? "The factions have presented their demands.")
            .Replace("{PrimaryGrievance}", snapshot?.primaryGrievance ?? "unresolved political grievances");
        // Never expose authoring markup in player-facing text.
        return UnresolvedToken.Replace(resolved,string.Empty);
    }
}
