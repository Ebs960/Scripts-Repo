// Assets/Scripts/UI/ResourceInventoryManager.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central manager for resource inventory display.
/// Maintains mappings between ResourceCategoryDefinitionSO and provider instances.
/// Queries providers to build aggregated inventory for each category.
/// </summary>
public class ResourceInventoryManager : MonoBehaviour
{
    [SerializeField] private ResourceCategoryDefinitionSO[] categories;

    private Dictionary<ResourceCategoryDefinitionSO, IResourceCategoryProvider[]> providersByCategory = new();

    public static ResourceInventoryManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);

        InitializeProviders();
    }

    /// <summary>
    /// Initialize provider instances for each category based on configuration.
    /// </summary>
    private void InitializeProviders()
    {
        if (categories == null)
        {
            Debug.LogWarning("ResourceInventoryManager: No categories assigned");
            return;
        }

        foreach (var category in categories)
        {
            if (category == null) continue;

            var providers = new List<IResourceCategoryProvider>();

            if (category.UseOwnedNodeProvider)
                providers.Add(new OwnedNodeCategoryProvider());

            if (category.UseStockpileProvider)
                providers.Add(new StockpileCategoryProvider());

            if (category.UseEquipmentProvider)
                providers.Add(new EquipmentCategoryProvider());

            if (providers.Count > 0)
                providersByCategory[category] = providers.ToArray();
        }
    }

    /// <summary>
    /// Get aggregated inventory for a specific category and civilization.
    /// Queries all configured providers and combines results.
    /// </summary>
    public Dictionary<ResourceData, int> GetCategoryInventory(Civilization civ, ResourceCategoryDefinitionSO category)
    {
        var combined = new Dictionary<ResourceData, int>();

        if (civ == null || category == null)
            return combined;

        if (!providersByCategory.TryGetValue(category, out var providers))
            return combined;

        // Aggregate from all providers
        foreach (var provider in providers)
        {
            var providerInventory = provider.GetInventory(civ, category);
            foreach (var kvp in providerInventory)
            {
                if (combined.ContainsKey(kvp.Key))
                    combined[kvp.Key] += kvp.Value;
                else
                    combined[kvp.Key] = kvp.Value;
            }
        }

        return combined;
    }

    /// <summary>
    /// Get all categories (sorted by display order).
    /// </summary>
    public ResourceCategoryDefinitionSO[] GetAllCategories()
    {
        if (categories == null)
            return new ResourceCategoryDefinitionSO[0];

        // Sort by display order
        var sorted = new List<ResourceCategoryDefinitionSO>(categories);
        sorted.Sort((a, b) => a.DisplayOrder.CompareTo(b.DisplayOrder));
        return sorted.ToArray();
    }
}
