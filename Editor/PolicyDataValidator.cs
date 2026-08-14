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
            string identity = string.IsNullOrWhiteSpace(policy.policyName) ? policy.name : policy.policyName;
            if (string.IsNullOrWhiteSpace(policy.description)) Error(path, "description is empty.", ref errors, policy);
            else if (ContainsBoilerplate(policy.description))
                Error(path, "description contains deprecated boilerplate ('durable institution' or 'while active').", ref errors, policy);
            if (!HasGameplayEffect(policy))
                Error(path, "policy has no gameplay effect.", ref errors, policy);
            if (!HasResearchGate(policy))
                Error(path, "policy must require at least one technology or culture.", ref errors, policy);
            if (policy.policyPointCost <= 0) Error(path, "policyPointCost must be positive.", ref errors, policy);
            if (policy.policyTags == null || policy.policyTags.Length == 0) Error(path, "policyTags is empty.", ref errors, policy);
            if (policy.icon == null) { Debug.LogWarning($"[Policy Validation] {path}: icon is not assigned.", policy); warnings++; }
            if (names.TryGetValue(identity, out string existing)) Error(path, $"duplicate identity '{identity}' (also {existing}).", ref errors, policy);
            else names[identity] = path;

            ValidateReferences(policy.requiredTechs, "requiredTechs", path, policy, ref errors);
            ValidateReferences(policy.requiredCultures, "requiredCultures", path, policy, ref errors);
            ValidateReferences(policy.requiredGovernments, "requiredGovernments", path, policy, ref errors);
            ValidateGovernorOpinionEffects(policy.governorOpinionEffects, path, policy, ref errors);
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

    private static bool HasRequiredCycle(PolicyData root, PolicyData current, HashSet<PolicyData> visiting)
    {
        if (current == null || current.requiredPolicies == null || !visiting.Add(current)) return false;
        foreach (var next in current.requiredPolicies)
            if (next == root || (next != null && HasRequiredCycle(root, next, visiting))) return true;
        visiting.Remove(current);
        return false;
    }

    private static bool ContainsBoilerplate(string description)
    {
        string text = description.ToLowerInvariant();
        return text.Contains("durable institution") || text.Contains("while active");
    }

    private static bool HasResearchGate(PolicyData policy)
        => HasItems(policy.requiredTechs) || HasItems(policy.requiredCultures);

    private static bool HasGameplayEffect(PolicyData p)
    {
        if (p.attackBonus != 0 || p.meleeAttackBonus != 0 || p.rangedAttackBonus != 0
            || p.cityAttackBonus != 0 || p.defenseBonus != 0 || p.movementBonus != 0
            || p.foodModifier != 0 || p.productionModifier != 0 || p.goldModifier != 0
            || p.scienceModifier != 0 || p.cultureModifier != 0 || p.faithModifier != 0
            || p.populationGrowthModifier != 0 || p.migrationAttractionModifier != 0
            || p.warWearinessModifier != 0 || p.corruptionModifier != 0 || p.unrestModifier != 0
            || p.administrativeEfficiencyModifier != 0 || p.distanceLoyaltyPenaltyModifier != 0
            || p.policyPointGenerationModifier != 0 || p.domesticTradeModifier != 0
            || p.foreignTradeModifier != 0 || p.tradeRouteCapacityBonus != 0
            || p.laborProductivityModifier != 0 || p.unemploymentUnhappinessModifier != 0
            || p.reinforcementSpeedModifier != 0 || p.militaryUpkeepModifier != 0
            || p.cyberDefenseModifier != 0 || p.cyberOffenseModifier != 0
            || p.espionageDefenseModifier != 0 || p.orbitalProductionModifier != 0
            || p.interplanetaryTradeModifier != 0 || p.planetaryLoyaltyModifier != 0
            || p.planetaryDefenseModifier != 0 || p.herdStarvationPercentReduction != 0
            || p.additionalGovernorSlots != 0) return true;
        return HasItems(p.tileYieldBonuses) || HasItems(p.buildingBonuses)
            || HasItems(p.unitYieldBonuses) || HasItems(p.unitBonuses)
            || HasItems(p.equipmentYieldBonuses) || HasItems(p.workerYieldBonuses)
            || HasItems(p.workerBonuses) || HasItems(p.diseaseBonuses)
            || HasItems(p.attritionBonuses) || HasItems(p.cityBonuses)
            || HasItems(p.nonStateReligionUnhappinessModifiers) || HasItems(p.herdYieldBonuses)
            || HasItems(p.unlockedGovernorTraits) || HasItems(p.governorOpinionEffects);
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
    private static void ValidateGovernorOpinionEffects(GovernorOpinionEffect[] effects, string path, Object context, ref int errors)
    {
        if (!HasItems(effects))
        {
            Error(path, "governorOpinionEffects is empty.", ref errors, context);
            return;
        }

        for (int i = 0; i < effects.Length; i++)
        {
            var effect = effects[i];
            if (effect == null)
            {
                Error(path, $"governorOpinionEffects[{i}] is null.", ref errors, context);
                continue;
            }
            if (string.IsNullOrWhiteSpace(effect.reason))
                Error(path, $"governorOpinionEffects[{i}].reason is empty.", ref errors, context);
            if (effect.value == 0)
                Error(path, $"governorOpinionEffects[{i}].value must be non-zero.", ref errors, context);
            if (effect.durationTurns == 0 || effect.durationTurns < -1)
                Error(path, $"governorOpinionEffects[{i}].durationTurns must be positive or -1.", ref errors, context);
            ValidateDuplicates(effect.requiresAnyPersonality,
                $"governorOpinionEffects[{i}].requiresAnyPersonality", path, context, ref errors);
        }
    }
    private static void Error(string path, string message, ref int errors, Object context = null)
    { Debug.LogError($"[Policy Validation] {path}: {message}", context); errors++; }
}
#endif
