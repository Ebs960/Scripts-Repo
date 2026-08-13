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
        var policies = new List<PolicyData>();
        foreach (string guid in AssetDatabase.FindAssets("t:PolicyData"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var policy = AssetDatabase.LoadAssetAtPath<PolicyData>(path);
            if (policy == null) { Error(path, "Asset could not be loaded.", ref errors); continue; }
            policies.Add(policy);
            if (string.IsNullOrWhiteSpace(policy.description)) Error(path, "description is empty.", ref errors, policy);
            if (HasBoilerplateDescription(policy.description))
            {
                Debug.LogWarning($"[Policy Validation] {path}: description uses generic active-policy boilerplate instead of explaining the policy's gameplay impact.", policy);
                warnings++;
            }
            if (policy.policyPointCost <= 0) Error(path, "policyPointCost must be positive.", ref errors, policy);
            if (policy.policyTags == null || policy.policyTags.Length == 0) Error(path, "policyTags is empty.", ref errors, policy);
            if (policy.icon == null) { Debug.LogWarning($"[Policy Validation] {path}: icon is not assigned.", policy); warnings++; }
            if (!HasGameplayEffect(policy))
                Error(path, "policy has no gameplay effects configured.", ref errors, policy);
            if (!HasProgressionRequirement(policy))
            {
                Debug.LogWarning($"[Policy Validation] {path}: policy has no technology, culture, government, city-count, religion, or required-policy gate and is structurally available from the start.", policy);
                warnings++;
            }

            string identity = string.IsNullOrWhiteSpace(policy.policyName) ? policy.name : policy.policyName;
            if (names.TryGetValue(identity, out string existing)) Error(path, $"duplicate identity '{identity}' (also {existing}).", ref errors, policy);
            else names[identity] = path;

            ValidateReferences(policy.requiredTechs, "requiredTechs", path, policy, ref errors);
            ValidateReferences(policy.requiredCultures, "requiredCultures", path, policy, ref errors);
            ValidateReferences(policy.requiredGovernments, "requiredGovernments", path, policy, ref errors);
            ValidatePolicyReferences(policy.requiredPolicies, "requiredPolicies", path, policy, ref errors);
            ValidatePolicyReferences(policy.incompatiblePolicies, "incompatiblePolicies", path, policy, ref errors);
            ValidatePolicyReferences(policy.supersedesPolicies, "supersedesPolicies", path, policy, ref errors);
            ValidateDuplicates(policy.policyTags, "policyTags", path, policy, ref errors);
            if (Contains(policy.requiredPolicies, policy)) Error(path, "a policy cannot require itself.", ref errors, policy);
            if (Contains(policy.incompatiblePolicies, policy)) Error(path, "a policy cannot conflict with itself.", ref errors, policy);
            if (Contains(policy.supersedesPolicies, policy)) Error(path, "a policy cannot supersede itself.", ref errors, policy);
            if (policy.requiredPolicies != null)
                foreach (var required in policy.requiredPolicies)
                    if (required != null && Contains(policy.incompatiblePolicies, required))
                        Error(path, $"'{required.name}' is both required and incompatible.", ref errors, policy);
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
        foreach (var policy in policies)
        {
            string path = AssetDatabase.GetAssetPath(policy);
            if (policy.incompatiblePolicies != null)
                foreach (var other in policy.incompatiblePolicies)
                    if (other != null && !Contains(other.incompatiblePolicies, policy))
                    { Debug.LogWarning($"[Policy Validation] {path}: incompatibility with '{other.name}' is asymmetric (runtime still enforces it symmetrically).", policy); warnings++; }
            var visiting = new HashSet<PolicyData>();
            if (HasRequiredCycle(policy, policy, visiting))
                Error(path, "circular required-policy chain detected.", ref errors, policy);
        }
        Debug.Log($"[Policy Validation] Complete: {errors} error(s), {warnings} warning(s).");
    }

    public static bool HasRequiredPolicyCycle(PolicyData policy)
        => policy != null && HasRequiredCycle(policy, policy, new HashSet<PolicyData>());

    public static bool HasGameplayEffect(PolicyData p)
    {
        if (p == null) return false;
        return NonZero(p.attackBonus) || NonZero(p.meleeAttackBonus) || NonZero(p.rangedAttackBonus)
            || NonZero(p.cityAttackBonus) || NonZero(p.defenseBonus) || NonZero(p.movementBonus)
            || NonZero(p.foodModifier) || NonZero(p.productionModifier) || NonZero(p.goldModifier)
            || NonZero(p.scienceModifier) || NonZero(p.cultureModifier) || NonZero(p.faithModifier)
            || NonZero(p.populationGrowthModifier) || NonZero(p.migrationAttractionModifier)
            || NonZero(p.warWearinessModifier) || NonZero(p.corruptionModifier) || NonZero(p.unrestModifier)
            || NonZero(p.administrativeEfficiencyModifier) || NonZero(p.distanceLoyaltyPenaltyModifier)
            || NonZero(p.policyPointGenerationModifier) || NonZero(p.domesticTradeModifier)
            || NonZero(p.foreignTradeModifier) || p.tradeRouteCapacityBonus != 0
            || NonZero(p.laborProductivityModifier) || NonZero(p.unemploymentUnhappinessModifier)
            || NonZero(p.reinforcementSpeedModifier) || NonZero(p.militaryUpkeepModifier)
            || NonZero(p.cyberDefenseModifier) || NonZero(p.cyberOffenseModifier)
            || NonZero(p.espionageDefenseModifier) || NonZero(p.orbitalProductionModifier)
            || NonZero(p.interplanetaryTradeModifier) || NonZero(p.planetaryLoyaltyModifier)
            || NonZero(p.planetaryDefenseModifier) || HasItems(p.tileYieldBonuses)
            || HasItems(p.buildingBonuses) || HasItems(p.unitYieldBonuses) || HasItems(p.unitBonuses)
            || HasItems(p.equipmentYieldBonuses) || HasItems(p.workerYieldBonuses) || HasItems(p.workerBonuses)
            || HasItems(p.diseaseBonuses) || HasItems(p.attritionBonuses) || HasItems(p.cityBonuses)
            || HasItems(p.nonStateReligionUnhappinessModifiers) || NonZero(p.herdStarvationPercentReduction)
            || HasItems(p.herdYieldBonuses) || p.additionalGovernorSlots != 0
            || HasItems(p.unlockedGovernorTraits) || HasItems(p.governorOpinionEffects);
    }

    public static bool HasProgressionRequirement(PolicyData p)
    {
        if (p == null) return false;
        return HasItems(p.requiredTechs) || HasItems(p.requiredCultures) || HasItems(p.requiredGovernments)
            || p.requiredCityCount > 0 || HasItems(p.religiousRequirementGroups) || HasItems(p.requiredPolicies);
    }

    private static bool HasBoilerplateDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return false;
        string lower = description.ToLowerInvariant();
        return lower.Contains("apply while active")
            || lower.Contains("while the policy is active")
            || lower.Contains("durable institution; its benefits")
            || lower.Contains("durable institution; its tradeoffs");
    }

    private static bool NonZero(float value) => !Mathf.Approximately(value, 0f);

    private static bool HasRequiredCycle(PolicyData root, PolicyData current, HashSet<PolicyData> visiting)
    {
        if (current == null || current.requiredPolicies == null || !visiting.Add(current)) return false;
        foreach (var next in current.requiredPolicies)
            if (next == root || (next != null && HasRequiredCycle(root, next, visiting))) return true;
        visiting.Remove(current);
        return false;
    }

    private static bool Contains(PolicyData[] values, PolicyData target)
    { if (values == null) return false; foreach (var value in values) if (value == target) return true; return false; }

    private static void ValidatePolicyReferences(PolicyData[] values, string field, string path, Object context, ref int errors)
        => ValidateReferences(values, field, path, context, ref errors);

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
