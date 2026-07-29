using System.Collections.Generic;
using System.Linq;

public enum ImprovementUpgradeAvailability
{
    Available,
    Installed,
    Locked,
    Unaffordable,
    Invalid
}

public readonly struct ImprovementUpgradeEvaluation
{
    public ImprovementUpgradeAvailability Availability { get; }
    public string Reason { get; }
    public bool IsInteractable => Availability == ImprovementUpgradeAvailability.Available;

    public ImprovementUpgradeEvaluation(ImprovementUpgradeAvailability availability, string reason)
    {
        Availability = availability;
        Reason = reason ?? string.Empty;
    }
}

public static class ImprovementUpgradeRules
{
    public static string GetKey(ImprovementUpgradeData upgrade)
    {
        return upgrade != null ? upgrade.GetUpgradeKey() : string.Empty;
    }

    public static bool CanShowInUpgradeList(ImprovementData improvement, HexTileData tileData, ImprovementUpgradeData upgrade)
    {
        if (upgrade == null) return false;
        string reason;
        return PassesPathRules(improvement, tileData, upgrade, out reason);
    }

    public static bool CanApplyUpgrade(ImprovementData improvement, HexTileData tileData, ImprovementUpgradeData upgrade, Civilization civ, out string reason)
    {
        var evaluation = Evaluate(improvement, tileData, upgrade, civ);
        reason = evaluation.Reason;
        return evaluation.IsInteractable;
    }

    public static ImprovementUpgradeEvaluation Evaluate(ImprovementData improvement, HexTileData tileData, ImprovementUpgradeData upgrade, Civilization civ)
    {
        if (upgrade == null)
            return new ImprovementUpgradeEvaluation(ImprovementUpgradeAvailability.Invalid, "No upgrade selected.");

        string key = GetKey(upgrade);
        if (upgrade.uniqueUpgrade && tileData?.builtUpgrades != null && tileData.builtUpgrades.Contains(key))
            return new ImprovementUpgradeEvaluation(ImprovementUpgradeAvailability.Installed, "Installed");

        if (tileData == null || tileData.improvementInstanceObject == null)
            return new ImprovementUpgradeEvaluation(ImprovementUpgradeAvailability.Invalid, "This improvement is not ready for upgrades.");

        if (!PassesPathRules(improvement, tileData, upgrade, out string pathReason))
            return new ImprovementUpgradeEvaluation(ImprovementUpgradeAvailability.Locked, pathReason);

        if (civ == null)
            return new ImprovementUpgradeEvaluation(ImprovementUpgradeAvailability.Invalid, "No civilization selected.");
        if (upgrade.requiredTech != null && !civ.researchedTechs.Contains(upgrade.requiredTech))
            return new ImprovementUpgradeEvaluation(ImprovementUpgradeAvailability.Locked, $"Requires technology: {upgrade.requiredTech.techName}");
        if (upgrade.requiredCulture != null && !civ.researchedCultures.Contains(upgrade.requiredCulture))
            return new ImprovementUpgradeEvaluation(ImprovementUpgradeAvailability.Locked, $"Requires culture: {upgrade.requiredCulture.cultureName}");
        if (civ.gold < upgrade.goldCost)
            return new ImprovementUpgradeEvaluation(ImprovementUpgradeAvailability.Unaffordable, $"Requires {upgrade.goldCost} Gold (available: {civ.gold}).");
        if (!ResourceCost.CanAfford(civ, upgrade.resourceCosts, upgrade.hasSubstituteCosts))
            return new ImprovementUpgradeEvaluation(ImprovementUpgradeAvailability.Unaffordable, $"Requires {ResourceCost.FormatCosts(upgrade.resourceCosts, upgrade.hasSubstituteCosts)}.");

        return new ImprovementUpgradeEvaluation(ImprovementUpgradeAvailability.Available, string.Empty);
    }

    public static string GetDisplaySlot(ImprovementUpgradeData upgrade)
    {
        string slot = GetEffectiveSlot(upgrade);
        return string.IsNullOrWhiteSpace(slot) ? "General" : slot;
    }

    public static bool PassesPathRules(ImprovementData improvement, HexTileData tileData, ImprovementUpgradeData upgrade, out string reason)
    {
        reason = string.Empty;
        if (upgrade == null) return false;

        string upgradeKey = GetKey(upgrade);
        List<string> builtKeys = tileData != null && tileData.builtUpgrades != null
            ? tileData.builtUpgrades
            : new List<string>();

        if (upgrade.uniqueUpgrade && builtKeys.Contains(upgradeKey))
        {
            reason = "Already built.";
            return false;
        }

        if (ContainsKey(upgrade.mutuallyExclusiveUpgradeIds, builtKeys))
        {
            reason = "Blocked by another upgrade choice.";
            return false;
        }

        var builtUpgrades = GetBuiltUpgradeDefinitions(improvement, builtKeys);
        foreach (var built in builtUpgrades)
        {
            if (built == null) continue;
            string builtKey = GetKey(built);

            if (ContainsKey(built.mutuallyExclusiveUpgradeIds, upgradeKey))
            {
                reason = "Blocked by another upgrade choice.";
                return false;
            }

            if (!upgrade.isSwitchableOption && ConflictsByExclusiveGroup(built, upgrade))
            {
                reason = "Blocked by a different upgrade path.";
                return false;
            }
        }

        string effectiveSlot = GetEffectiveSlot(upgrade);
        if (!string.IsNullOrEmpty(effectiveSlot))
        {
            var upgradesInSlot = builtUpgrades
                .Where(built => built != null && GetEffectiveSlot(built) == effectiveSlot)
                .ToList();

            if (!upgrade.allowMultipleInSlot)
            {
                foreach (var built in upgradesInSlot)
                {
                    if (built == null) continue;
                    if (upgrade.isSwitchableOption && GetKey(built) != upgradeKey)
                        continue;
                    if (built.upgradePath == upgrade.upgradePath && upgrade.supersedesLowerTiersInPath)
                    {
                        if (built.pathTier < upgrade.pathTier)
                            continue;

                        reason = "This path already has an equal or higher tier option.";
                        return false;
                    }

                    reason = "This option slot is already occupied.";
                    return false;
                }
            }
            else if (upgrade.maxUpgradesInSlot > 0 && upgradesInSlot.Count >= upgrade.maxUpgradesInSlot)
            {
                reason = "This upgrade slot is full.";
                return false;
            }
        }

        return true;
    }

    public static List<string> GetSupersededUpgradeKeys(ImprovementData improvement, HexTileData tileData, ImprovementUpgradeData upgrade)
    {
        var result = new List<string>();
        if (improvement == null || tileData == null || tileData.builtUpgrades == null || upgrade == null)
            return result;
        string effectiveSlot = GetEffectiveSlot(upgrade);
        if (string.IsNullOrEmpty(effectiveSlot))
            return result;

        foreach (var built in GetBuiltUpgradeDefinitions(improvement, tileData.builtUpgrades))
        {
            if (built == null) continue;
            if (GetEffectiveSlot(built) != effectiveSlot) continue;
            if (upgrade.isSwitchableOption)
            {
                if (GetKey(built) == GetKey(upgrade)) continue;
            }
            else
            {
                if (!upgrade.supersedesLowerTiersInPath) continue;
                if (built.upgradePath != upgrade.upgradePath) continue;
                if (built.pathTier >= upgrade.pathTier) continue;
            }

            string key = GetKey(built);
            if (!string.IsNullOrEmpty(key) && !result.Contains(key))
                result.Add(key);
        }

        return result;
    }

    private static IEnumerable<ImprovementUpgradeData> GetBuiltUpgradeDefinitions(ImprovementData improvement, IEnumerable<string> builtKeys)
    {
        if (improvement == null || improvement.availableUpgrades == null || builtKeys == null)
            yield break;

        foreach (string builtKey in builtKeys)
        {
            if (string.IsNullOrEmpty(builtKey)) continue;
            var found = improvement.availableUpgrades.FirstOrDefault(upgrade => GetKey(upgrade) == builtKey);
            if (found != null)
                yield return found;
        }
    }

    private static bool ConflictsByExclusiveGroup(ImprovementUpgradeData built, ImprovementUpgradeData candidate)
    {
        if (built == null || candidate == null) return false;
        if (string.IsNullOrEmpty(candidate.exclusiveGroupId)) return false;
        if (built.exclusiveGroupId != candidate.exclusiveGroupId) return false;
        return built.upgradePath != candidate.upgradePath;
    }

    private static string GetEffectiveSlot(ImprovementUpgradeData upgrade)
    {
        if (upgrade == null) return string.Empty;
        return !string.IsNullOrEmpty(upgrade.upgradeSlot) ? upgrade.upgradeSlot : upgrade.exclusiveGroupId;
    }

    private static bool ContainsKey(IEnumerable<string> keys, IEnumerable<string> builtKeys)
    {
        if (keys == null || builtKeys == null) return false;
        foreach (string key in keys)
        {
            if (!string.IsNullOrEmpty(key) && builtKeys.Contains(key))
                return true;
        }
        return false;
    }

    private static bool ContainsKey(IEnumerable<string> keys, string candidateKey)
    {
        if (keys == null || string.IsNullOrEmpty(candidateKey)) return false;
        foreach (string key in keys)
        {
            if (!string.IsNullOrEmpty(key) && key == candidateKey)
                return true;
        }
        return false;
    }
}
