// Assets/Scripts/UI/ResourceCategoryProvider.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Utility for getting resource inventory from combined sources:
/// - Civilization.resourceStockpile (stored resources)
/// - ResourceManager (owned resource nodes on the map)
/// </summary>
public static class ResourceCategoryProviderUtility
{
    /// <summary>
    /// Get merged inventory for a category from stockpile + owned nodes.
    /// Returns map of ResourceData -> quantity.
    /// </summary>
    public static Dictionary<ResourceData, int> GetMergedInventory(Civilization civ, ResourceCategory category)
    {
        var merged = new Dictionary<ResourceData, int>();

        if (civ == null)
            return merged;

        // Add from stockpile
        if (civ.resourceStockpile != null)
        {
            foreach (var kvp in civ.resourceStockpile)
            {
                if (kvp.Key != null && kvp.Key.category == category)
                {
                    if (merged.ContainsKey(kvp.Key))
                        merged[kvp.Key] += kvp.Value;
                    else
                        merged[kvp.Key] = kvp.Value;
                }
            }
        }

        // Add from owned nodes
        if (ResourceManager.Instance != null)
        {
            var ownedInventory = ResourceManager.Instance.GetInventory(civ);
            if (ownedInventory != null)
            {
                foreach (var kvp in ownedInventory)
                {
                    if (kvp.Key != null && kvp.Key.category == category)
                    {
                        if (merged.ContainsKey(kvp.Key))
                            merged[kvp.Key] += kvp.Value;
                        else
                            merged[kvp.Key] = kvp.Value;
                    }
                }
            }
        }

        return merged;
    }

    /// <summary>
    /// Get total count of resources in a category.
    /// </summary>
    public static int GetTotalCount(Civilization civ, ResourceCategory category)
    {
        int count = 0;
        foreach (var kvp in GetMergedInventory(civ, category))
            count += kvp.Value;
        return count;
    }

    /// <summary>
    /// Get combined per-turn yield for all resources in a category.
    /// </summary>
    public static int GetYieldPerTurn(Civilization civ, ResourceCategory category)
    {
        int total = 0;
        foreach (var kvp in GetMergedInventory(civ, category))
        {
            if (kvp.Key == null || kvp.Value == 0)
                continue;

            total += kvp.Value * (
                kvp.Key.foodPerTurn +
                kvp.Key.productionPerTurn +
                kvp.Key.goldPerTurn +
                kvp.Key.sciencePerTurn +
                kvp.Key.culturePerTurn +
                kvp.Key.policyPointsPerTurn +
                kvp.Key.faithPerTurn);
        }

        return total;
    }
}
