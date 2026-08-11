#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class PolicyDataValidator
{
    [MenuItem("Tools/Data Validation/Validate Policies")]
    public static void ValidatePolicies()
    {
        int errors = 0, warnings = 0;
        var names = new Dictionary<string, string>();
        foreach (string guid in AssetDatabase.FindAssets("t:PolicyData"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var policy = AssetDatabase.LoadAssetAtPath<PolicyData>(path);
            if (policy == null) { Error(path, "Asset could not be loaded.", ref errors); continue; }
            if (policy.icon == null) { Debug.LogWarning($"[Policy Validation] {path}: icon is not assigned.", policy); warnings++; }
            string identity = string.IsNullOrWhiteSpace(policy.policyName) ? policy.name : policy.policyName;
            if (names.TryGetValue(identity, out string existing)) Error(path, $"duplicate identity '{identity}' (also {existing}).", ref errors, policy);
            else names[identity] = path;

            ValidateReferences(policy.requiredTechs, "requiredTechs", path, policy, ref errors);
            ValidateReferences(policy.requiredCultures, "requiredCultures", path, policy, ref errors);
            ValidateReferences(policy.requiredGovernments, "requiredGovernments", path, policy, ref errors);
            ValidateReferences(policy.unlockedGovernorTraits, "unlockedGovernorTraits", path, policy, ref errors);
            if (policy.religiousRequirementGroups == null) continue;
            for (int i = 0; i < policy.religiousRequirementGroups.Length; i++)
            {
                var group = policy.religiousRequirementGroups[i];
                if (group == null) { Error(path, $"religious group {i} is null.", ref errors, policy); continue; }
                ValidateReferences(group.anyStateReligions, $"religious group {i} religions", path, policy, ref errors);
                ValidateReferences(group.anyPantheons, $"religious group {i} pantheons", path, policy, ref errors);
                ValidateReferences(group.anyBeliefs, $"religious group {i} beliefs", path, policy, ref errors);
                bool empty = !group.requiresStateReligion && !group.useMinimumPantheonTier
                    && !HasItems(group.anyStateReligions) && !HasItems(group.anyPantheons)
                    && !HasItems(group.anyBeliefs) && (group.anyBeliefCategories == null || group.anyBeliefCategories.Length == 0);
                if (empty) Error(path, $"religious group {i} is empty and would always pass.", ref errors, policy);
                if (group.useMinimumPantheonTier && HasItems(group.anyPantheons))
                    foreach (var pantheon in group.anyPantheons)
                        if (pantheon != null && pantheon.tier < group.minimumPantheonTier && !group.allowPantheonUpgradeDescendants)
                            Error(path, $"religious group {i} requires {pantheon.name}, but its minimum tier excludes it and descendants are disabled.", ref errors, policy);
                ValidateDuplicates(group.anyBeliefCategories, $"religious group {i} belief categories", path, policy, ref errors);
            }
        }
        Debug.Log($"[Policy Validation] Complete: {errors} error(s), {warnings} warning(s).");
    }

    private static bool HasItems<T>(T[] values) => values != null && values.Length > 0;
    private static void ValidateReferences<T>(T[] values, string field, string path, Object context, ref int errors) where T : Object
    {
        if (values == null) return;
        var seen = new HashSet<T>();
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] == null) Error(path, $"{field}[{i}] has a missing reference.", ref errors, context);
            else if (!seen.Add(values[i])) Error(path, $"{field} contains duplicate '{values[i].name}'.", ref errors, context);
        }
    }
    private static void ValidateDuplicates<T>(T[] values, string field, string path, Object context, ref int errors)
    {
        if (values == null) return;
        var seen = new HashSet<T>();
        foreach (var value in values) if (!seen.Add(value)) Error(path, $"{field} contains duplicate '{value}'.", ref errors, context);
    }
    private static void Error(string path, string message, ref int errors, Object context = null)
    { Debug.LogError($"[Policy Validation] {path}: {message}", context); errors++; }
}
#endif
