using UnityEngine;

/// <summary>
/// Evaluation rules for assigning a labor type to an improvement. Mirrors ImprovementUpgradeRules but
/// models a single always-available toggle rather than a built/persisted upgrade tree.
/// </summary>
public static class ImprovementLaborRules
{
    public static int GetSwitchCost(LaborTypeData laborType)
    {
        if (laborType == null) return 0;
        return laborType.isDefaultLabor ? 0 : Mathf.Max(0, laborType.goldCostToSwitchTo);
    }

    public static ImprovementUpgradeEvaluation Evaluate(ImprovementData improvement, ImprovementInstance instance, LaborTypeData laborType, Civilization civ)
    {
        if (laborType == null)
            return new ImprovementUpgradeEvaluation(ImprovementUpgradeAvailability.Invalid, "No labor type selected.");
        if (improvement == null || !improvement.usesLaborTypes)
            return new ImprovementUpgradeEvaluation(ImprovementUpgradeAvailability.Invalid, "This improvement does not use labor.");
        if (instance != null && instance.currentLaborType == laborType)
            return new ImprovementUpgradeEvaluation(ImprovementUpgradeAvailability.Installed, "Currently assigned.");
        if (civ == null)
            return new ImprovementUpgradeEvaluation(ImprovementUpgradeAvailability.Invalid, "No civilization selected.");
        if (laborType.requiredPolicy != null && (civ.activePolicies == null || !civ.activePolicies.Contains(laborType.requiredPolicy)))
            return new ImprovementUpgradeEvaluation(ImprovementUpgradeAvailability.Locked, $"Requires policy: {laborType.requiredPolicy.policyName}");

        int cost = GetSwitchCost(laborType);
        if (civ.gold < cost)
            return new ImprovementUpgradeEvaluation(ImprovementUpgradeAvailability.Unaffordable, $"Requires {cost} Gold (available: {civ.gold}).");

        return new ImprovementUpgradeEvaluation(ImprovementUpgradeAvailability.Available, string.Empty);
    }
}
