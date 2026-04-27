// Assets/Scripts/UI/ResourceCategoryProvider.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base interface for resource inventory providers.
/// Different providers supply inventory from different sources:
/// - OwnedNodes: From ResourceManager (owned resource nodes)
/// - Stockpile: From Civilization.resourceStockpile
/// - Equipment: From Civilization.equipmentInventory
/// </summary>
public interface IResourceCategoryProvider
{
    /// <summary>
    /// Get inventory for a specific category and civilization.
    /// Returns map of ResourceData -> quantity.
    /// </summary>
    Dictionary<ResourceData, int> GetInventory(Civilization civ, ResourceCategoryDefinitionSO category);
}

/// <summary>
/// Provider backed by owned resource nodes from ResourceManager.
/// Returns quantities of resources the civilization currently owns on the map.
/// </summary>
public class OwnedNodeCategoryProvider : IResourceCategoryProvider
{
    public Dictionary<ResourceData, int> GetInventory(Civilization civ, ResourceCategoryDefinitionSO category)
    {
        var inventory = new Dictionary<ResourceData, int>();

        if (civ == null || ResourceManager.Instance == null || category == null)
            return inventory;

        // Get owned nodes inventory
        var ownedInventory = ResourceManager.Instance.GetInventory(civ);
        if (ownedInventory == null)
            return inventory;

        // Filter by category
        foreach (var kvp in ownedInventory)
        {
            var resourceData = kvp.Key;
            int quantity = kvp.Value;

            if (resourceData != null && resourceData.category == category)
                inventory[resourceData] = quantity;
        }

        return inventory;
    }
}

/// <summary>
/// Provider backed by Civilization.resourceStockpile.
/// Returns quantities of resources stored in the civilization's stockpile.
/// </summary>
public class StockpileCategoryProvider : IResourceCategoryProvider
{
    public Dictionary<ResourceData, int> GetInventory(Civilization civ, ResourceCategoryDefinitionSO category)
    {
        var inventory = new Dictionary<ResourceData, int>();

        if (civ == null || civ.resourceStockpile == null || category == null)
            return inventory;

        // Filter stockpile by category
        foreach (var kvp in civ.resourceStockpile)
        {
            var resourceData = kvp.Key;
            int quantity = kvp.Value;

            if (resourceData != null && resourceData.category == category)
                inventory[resourceData] = quantity;
        }

        return inventory;
    }
}

/// <summary>
/// Provider backed by Civilization.equipmentInventory.
/// Returns quantities of equipment stored in the civilization's inventory.
/// </summary>
public class EquipmentCategoryProvider : IResourceCategoryProvider
{
    public Dictionary<ResourceData, int> GetInventory(Civilization civ, ResourceCategoryDefinitionSO category)
    {
        // Equipment inventory uses EquipmentData keys, which are not ResourceData.
        // For now, this provider does not map equipment to ResourceData categories.
        // Return an empty inventory to avoid type mismatches.
        return new Dictionary<ResourceData, int>();
    }
}
