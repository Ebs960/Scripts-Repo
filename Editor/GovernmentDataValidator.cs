#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class GovernmentDataValidator
{
    [MenuItem("Tools/Data Validation/Validate Governments")]
    public static void ValidateGovernments()
    {
        int errors = 0, warnings = 0;
        var identities = new Dictionary<string, GovernmentData>();
        foreach (string guid in AssetDatabase.FindAssets("t:GovernmentData"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var g = AssetDatabase.LoadAssetAtPath<GovernmentData>(path);
            if (g == null) { Error(path, "asset cannot be loaded", null, ref errors); continue; }
            if (string.IsNullOrWhiteSpace(g.governmentName)) Error(path, "governmentName is empty", g, ref errors);
            if (string.IsNullOrWhiteSpace(g.description)) Error(path, "description is empty", g, ref errors);
            if (g.policyPointCost < 0 || g.requiredCityCount < 0 || g.requiredVassalCount < 0) Error(path, "cost and prerequisites cannot be negative", g, ref errors);
            if (!string.IsNullOrWhiteSpace(g.governmentName) && identities.ContainsKey(g.governmentName)) Error(path, $"duplicate identity '{g.governmentName}'", g, ref errors); else if (!string.IsNullOrWhiteSpace(g.governmentName)) identities[g.governmentName] = g;
            ValidateRefs(g.requiredTechs, "requiredTechs", path, g, ref errors);
            ValidateRefs(g.requiredCultures, "requiredCultures", path, g, ref errors);
            if (!g.usesRoyalCouncil && (g.councilSeatCount > 0 || g.councilVetoDomains != VetoDomain.None))
                Error(path, "council seats/vetoes are configured while the institution is disabled", g, ref errors);
            if (g.usesRoyalCouncil && (g.councilSeatCount <= 0 || string.IsNullOrWhiteSpace(g.institutionDisplayName)))
                Error(path, "enabled institution requires seats and a display name", g, ref errors);
            if (g.electionRules != null && g.electionRules.enabled)
            {
                if (g.electionRules.termLengthTurns <= 0 || g.electionRules.candidateCount < 2 || g.electionRules.candidateCount > 4)
                    Error(path, "election term/candidate configuration is malformed", g, ref errors);
                if (g.electionRules.publicOpinionWeight < 0 || g.electionRules.governorEliteWeight < 0)
                    Error(path, "electorate weights cannot be negative", g, ref errors);
            }
            if (g.icon == null) Warn(path, "icon is not assigned", g, ref warnings);
            if (Mathf.Abs(g.attackBonus) > .5f || Mathf.Abs(g.productionModifier) > .5f || Mathf.Abs(g.goldModifier) > .5f || Mathf.Abs(g.scienceModifier) > .5f || Mathf.Abs(g.cultureModifier) > .5f || Mathf.Abs(g.faithModifier) > .5f)
                Warn(path, "contains a suspiciously extreme global modifier", g, ref warnings);
            if (!HasIdentity(g)) Warn(path, "has effectively no gameplay identity", g, ref warnings);
            if ((g.governmentName?.Contains("Republic") == true || g.governmentName?.Contains("Democracy") == true) && (g.electionRules == null || !g.electionRules.enabled))
                Warn(path, "representative elected government has no election rules", g, ref warnings);
            if (g.governorOpinionEffects == null || g.governorOpinionEffects.Length == 0)
                Warn(path, "has no governor/political reaction", g, ref warnings);
        }
        Debug.Log($"[Government Validation] Complete: {errors} error(s), {warnings} warning(s).");
    }

    public static bool HasInvalidCouncilConfiguration(GovernmentData g) => g != null && ((!g.usesRoyalCouncil && (g.councilSeatCount > 0 || g.councilVetoDomains != VetoDomain.None)) || (g.usesRoyalCouncil && g.councilSeatCount <= 0));
    public static bool HasMalformedElectionRules(GovernmentData g) => g?.electionRules != null && g.electionRules.enabled && (g.electionRules.termLengthTurns <= 0 || g.electionRules.candidateCount < 2 || g.electionRules.candidateCount > 4);
    private static bool HasIdentity(GovernmentData g) => g.usesRoyalCouncil || g.suppressConventionalPolitics || (g.electionRules?.enabled ?? false) || !string.IsNullOrWhiteSpace(g.signatureMechanic) || g.cityCapModifier != 0 || g.attackBonus != 0 || g.productionModifier != 0 || g.goldModifier != 0 || g.scienceModifier != 0 || g.cultureModifier != 0 || g.faithModifier != 0;
    private static void ValidateRefs<T>(T[] values, string field, string path, Object context, ref int errors) where T:Object { if(values==null)return; for(int i=0;i<values.Length;i++) if(values[i]==null) Error(path,$"{field}[{i}] is broken",context,ref errors); }
    private static void Error(string p,string m,Object c,ref int n){Debug.LogError($"[Government Validation] {p}: {m}.",c);n++;}
    private static void Warn(string p,string m,Object c,ref int n){Debug.LogWarning($"[Government Validation] {p}: {m}.",c);n++;}
}
#endif
