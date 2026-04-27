// Assets/Scripts/UI/HudBreakdownService.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Central service that aggregates all yield sources for breakdown display.
/// 
/// Computes complete ledger for:
/// - Food (sources: cities, units, workers, herds, consumption)
/// - Gold (sources: cities, trade routes, resources, bonuses)
/// - Policy (sources: culture, policies, bonuses)
/// 
/// Uses pluggable provider pattern to support future additions (vassals, lords, tribute, etc).
/// </summary>
public class HudBreakdownService : MonoBehaviour
{
    /// <summary>
    /// Single breakdown item in a ledger.
    /// </summary>
    public struct BreakdownItem
    {
        public string source;           // "City: Capital", "Trade Route: Gold +5", etc.
        public int amount;              // The value for this source
        public string category;         // "City Yields", "Trade Income", "Consumption", etc.
    }

    private Civilization currentCiv;
    private List<IYieldProvider> foodProviders = new();
    private List<IYieldProvider> goldProviders = new();
    private List<IYieldProvider> policyProviders = new();

    public static HudBreakdownService Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    private void Start()
    {
        InitializeProviders();

        // Subscribe to turn changes
        if (TurnManager.Instance != null)
            TurnManager.Instance.OnTurnChanged += HandleTurnChanged;

        // Set initial civ
        var allCivs = CivilizationManager.Instance?.GetAllCivs();
        if (allCivs != null && allCivs.Count > 0)
        {
            var playerCiv = allCivs.FirstOrDefault(c => c.isPlayerControlled);
            if (playerCiv != null)
                SetCurrentCivilization(playerCiv, 0);
        }
    }

    private void HandleTurnChanged(Civilization civ, int turn)
    {
        SetCurrentCivilization(civ, turn);
    }

    private void OnDestroy()
    {
        if (TurnManager.Instance != null)
            TurnManager.Instance.OnTurnChanged -= HandleTurnChanged;
    }

    /// <summary>
    /// Set which civilization's breakdown data this service aggregates.
    /// </summary>
    public void SetCurrentCivilization(Civilization civ, int turn)
    {
        currentCiv = civ;
    }

    /// <summary>
    /// Initialize all yield providers for each resource type.
    /// </summary>
    private void InitializeProviders()
    {
        // Food providers
        foodProviders.Add(new CityFoodProvider());
        foodProviders.Add(new UnitFoodConsumptionProvider());
        foodProviders.Add(new HerdFoodConsumptionProvider());

        // Gold providers
        goldProviders.Add(new CityGoldProvider());
        goldProviders.Add(new TradeRouteGoldProvider());
        goldProviders.Add(new ResourceNodeGoldProvider());

        // Policy providers
        policyProviders.Add(new CityPolicyProvider());
    }

    /// <summary>
    /// Get complete food breakdown ledger.
    /// </summary>
    public List<BreakdownItem> GetFoodBreakdown()
    {
        if (currentCiv == null)
            return new List<BreakdownItem>();

        var items = new List<BreakdownItem>();

        // Aggregate all provider data
        foreach (var provider in foodProviders)
        {
            var providerItems = provider.GetBreakdown(currentCiv);
            items.AddRange(providerItems);
        }

        return items;
    }

    /// <summary>
    /// Get complete gold breakdown ledger.
    /// </summary>
    public List<BreakdownItem> GetGoldBreakdown()
    {
        if (currentCiv == null)
            return new List<BreakdownItem>();

        var items = new List<BreakdownItem>();

        foreach (var provider in goldProviders)
        {
            var providerItems = provider.GetBreakdown(currentCiv);
            items.AddRange(providerItems);
        }

        return items;
    }

    /// <summary>
    /// Get complete policy point breakdown ledger.
    /// </summary>
    public List<BreakdownItem> GetPolicyBreakdown()
    {
        if (currentCiv == null)
            return new List<BreakdownItem>();

        var items = new List<BreakdownItem>();

        foreach (var provider in policyProviders)
        {
            var providerItems = provider.GetBreakdown(currentCiv);
            items.AddRange(providerItems);
        }

        return items;
    }
}

/// <summary>
/// Provider interface for pluggable yield breakdown logic.
/// </summary>
public interface IYieldProvider
{
    List<HudBreakdownService.BreakdownItem> GetBreakdown(Civilization civ);
}

// ===== Concrete Providers =====

/// <summary>
/// Aggregates food yields from all cities.
/// </summary>
public class CityFoodProvider : IYieldProvider
{
    public List<HudBreakdownService.BreakdownItem> GetBreakdown(Civilization civ)
    {
        var items = new List<HudBreakdownService.BreakdownItem>();

        if (civ?.cities == null)
            return items;

        foreach (var city in civ.cities)
        {
            if (city == null) continue;

            // Get city's food production
            int foodProduction = city.GetFoodPerTurn();
            if (foodProduction > 0)
            {
                items.Add(new HudBreakdownService.BreakdownItem
                {
                    source = $"City: {city.name}",
                    amount = foodProduction,
                    category = "City Yields"
                });
            }
        }

        return items;
    }
}

/// <summary>
/// Aggregates food consumption from combat and worker units.
/// </summary>
public class UnitFoodConsumptionProvider : IYieldProvider
{
    public List<HudBreakdownService.BreakdownItem> GetBreakdown(Civilization civ)
    {
        var items = new List<HudBreakdownService.BreakdownItem>();

        if (civ == null)
            return items;

        int totalConsumption = 0;
        
        // Combat units
        if (civ.combatUnits != null)
        {
            foreach (var unit in civ.combatUnits)
            {
                if (unit == null || unit.data == null) continue;
                totalConsumption += unit.data.foodConsumptionPerTurn;
            }
        }
        
        // Worker units
        if (civ.workerUnits != null)
        {
            foreach (var unit in civ.workerUnits)
            {
                if (unit == null || unit.data == null) continue;
                totalConsumption += unit.data.foodConsumptionPerTurn;
            }
        }

        if (totalConsumption > 0)
        {
            items.Add(new HudBreakdownService.BreakdownItem
            {
                source = "Units Consumption",
                amount = -totalConsumption,
                category = "Consumption"
            });
        }

        return items;
    }
}

/// <summary>
/// Aggregates food consumption from herds.
/// </summary>
public class HerdFoodConsumptionProvider : IYieldProvider
{
    public List<HudBreakdownService.BreakdownItem> GetBreakdown(Civilization civ)
    {
        var items = new List<HudBreakdownService.BreakdownItem>();

        if (civ?.herds == null)
            return items;

        int totalConsumption = 0;
        foreach (var herd in civ.herds)
        {
            if (herd == null || herd.animals == null) continue;
            foreach (var entry in herd.animals)
            {
                if (entry == null) continue;
                int per100 = Herd.GetFoodConsumptionPer100(entry.species);
                totalConsumption += (entry.count * per100) / 100;
            }
        }

        if (totalConsumption > 0)
        {
            items.Add(new HudBreakdownService.BreakdownItem
            {
                source = "Herd Consumption",
                amount = -totalConsumption,
                category = "Consumption"
            });
        }

        return items;
    }
}

/// <summary>
/// Aggregates gold yields from cities.
/// </summary>
public class CityGoldProvider : IYieldProvider
{
    public List<HudBreakdownService.BreakdownItem> GetBreakdown(Civilization civ)
    {
        var items = new List<HudBreakdownService.BreakdownItem>();

        if (civ?.cities == null)
            return items;

        foreach (var city in civ.cities)
        {
            if (city == null) continue;

            int goldProduction = city.GetGoldPerTurn();
            if (goldProduction > 0)
            {
                items.Add(new HudBreakdownService.BreakdownItem
                {
                    source = $"City: {city.name}",
                    amount = goldProduction,
                    category = "City Yields"
                });
            }
        }

        return items;
    }
}

/// <summary>
/// Placeholder for trade route gold income.
/// </summary>
public class TradeRouteGoldProvider : IYieldProvider
{
    public List<HudBreakdownService.BreakdownItem> GetBreakdown(Civilization civ)
    {
        var items = new List<HudBreakdownService.BreakdownItem>();

        // TODO: Aggregate from trade routes once TradeRoute system is accessible
        // Example:
        // if (civ?.tradeRoutes != null)
        // {
        //     foreach (var route in civ.tradeRoutes)
        //     {
        //         items.Add(new BreakdownItem { source = $"Trade: {route.destination}", amount = route.goldIncome, category = "Trade" });
        //     }
        // }

        return items;
    }
}

/// <summary>
/// Placeholder for resource node yields.
/// </summary>
public class ResourceNodeGoldProvider : IYieldProvider
{
    public List<HudBreakdownService.BreakdownItem> GetBreakdown(Civilization civ)
    {
        var items = new List<HudBreakdownService.BreakdownItem>();

        // TODO: Aggregate from owned resource nodes
        // Example:
        // if (ResourceManager.Instance != null)
        // {
        //     var inventory = ResourceManager.Instance.GetInventory(civ);
        //     foreach (var resource in inventory.Values)
        //     {
        //         if (resource.sellPrice > 0)
        //             items.Add(...);
        //     }
        // }

        return items;
    }
}

/// <summary>
/// Aggregates policy points from cities.
/// </summary>
public class CityPolicyProvider : IYieldProvider
{
    public List<HudBreakdownService.BreakdownItem> GetBreakdown(Civilization civ)
    {
        var items = new List<HudBreakdownService.BreakdownItem>();

        if (civ?.cities == null)
            return items;

        foreach (var city in civ.cities)
        {
            if (city == null) continue;

            // Policy points typically come from city's policy points method
            int policyProduction = city.GetPolicyPointPerTurn();
            if (policyProduction > 0)
            {
                items.Add(new HudBreakdownService.BreakdownItem
                {
                    source = $"City: {city.name}",
                    amount = policyProduction,
                    category = "City Yields"
                });
            }
        }

        return items;
    }
}
