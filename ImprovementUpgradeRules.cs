using System.Collections.Generic;
using System.Linq;

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
        reason = string.Empty;
        if (upgrade == null)
        {
            reason = "No upgrade selected.";
            return false;
        }

        if (!PassesPathRules(improvement, tileData, upgrade, out reason))
            return false;

        if (tileData == null || tileData.improvementInstanceObject == null)
        {
            reason = "This improvement is not ready for upgrades.";
            return false;
        }

        if (!upgrade.CanBuild(civ))
        {
            reason = "Requirements or costs are not met.";
            return false;
        }

        return true;
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

        if (ContainsKey(upgrade.blockedByUpgradeIds, builtKeys))
        {
            reason = "Blocked by another upgrade choice.";
            return false;
        }

        var builtUpgrades = GetBuiltUpgradeDefinitions(improvement, builtKeys);
        foreach (var built in builtUpgrades)
        {
            if (built == null) continue;
            string builtKey = GetKey(built);

            if (ContainsKey(built.blocksUpgradeIds, upgradeKey))
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
