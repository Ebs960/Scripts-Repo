// Assets/Scripts/UI/HudBreakdownService.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Central service that aggregates all yield sources for breakdown display.
/// 
/// Uses provider groups per yield type and also adds a residual line when
/// provider totals do not match the civilization cached per-turn totals.
/// This ensures policy/government/bonus side-effects are still captured.
/// </summary>
public class HudBreakdownService : MonoBehaviour
{
    public struct BreakdownItem
    {
        public string source;
        public int amount;
        public string category;
    }

    private Civilization currentCiv;
    private readonly List<IYieldProvider> foodProviders = new();
    private readonly List<IYieldProvider> goldProviders = new();
    private readonly List<IYieldProvider> policyProviders = new();
    private readonly List<IYieldProvider> scienceProviders = new();
    private readonly List<IYieldProvider> cultureProviders = new();
    private readonly List<IYieldProvider> faithProviders = new();

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

        if (TurnManager.Instance != null)
            TurnManager.Instance.OnTurnChanged += HandleTurnChanged;

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

    public void SetCurrentCivilization(Civilization civ, int turn)
    {
        currentCiv = civ;
    }

    private void InitializeProviders()
    {
        // Food
        foodProviders.Add(new CityFoodProvider());
        foodProviders.Add(new UnitFoodYieldProvider());
        foodProviders.Add(new WorkerFoodYieldProvider());
        foodProviders.Add(new HerdFoodYieldProvider());
        foodProviders.Add(new FlatFoodBonusProvider());
        foodProviders.Add(new UnitFoodConsumptionProvider());
        foodProviders.Add(new CityFoodConsumptionProvider());

        // Gold
        goldProviders.Add(new CityGoldProvider());
        goldProviders.Add(new TradeRouteGoldProvider());
        goldProviders.Add(new UnitGoldYieldProvider());
        goldProviders.Add(new WorkerGoldYieldProvider());
        goldProviders.Add(new HerdGoldYieldProvider());
        goldProviders.Add(new FlatGoldBonusProvider());

        // Policy
        policyProviders.Add(new CityPolicyProvider());
        policyProviders.Add(new UnitPolicyYieldProvider());
        policyProviders.Add(new WorkerPolicyYieldProvider());
        policyProviders.Add(new HerdPolicyYieldProvider());

        // Science / Culture / Faith currently use total-per-turn fallback rows.
    }

    public List<BreakdownItem> GetFoodBreakdown()
    {
        if (currentCiv == null)
            return new List<BreakdownItem>();

        var items = CollectFromProviders(currentCiv, foodProviders);
        int target = currentCiv.cachedFoodPerTurn - currentCiv.cachedFoodConsumption;
        AddResidual(items, target, "Other Food Effects", "Policies / Government / Misc");
        return items;
    }

    public List<BreakdownItem> GetGoldBreakdown()
    {
        if (currentCiv == null)
            return new List<BreakdownItem>();

        var items = CollectFromProviders(currentCiv, goldProviders);
        AddResidual(items, currentCiv.cachedGoldPerTurn, "Other Gold Effects", "Policies / Government / Misc");
        return items;
    }

    public List<BreakdownItem> GetPolicyBreakdown()
    {
        if (currentCiv == null)
            return new List<BreakdownItem>();

        var items = CollectFromProviders(currentCiv, policyProviders);
        AddResidual(items, currentCiv.cachedPolicyPerTurn, "Other Policy Effects", "Policies / Government / Misc");
        return items;
    }


    public List<BreakdownItem> GetScienceBreakdown()
    {
        if (currentCiv == null)
            return new List<BreakdownItem>();

        var items = CollectFromProviders(currentCiv, scienceProviders);
        AddResidual(items, currentCiv.cachedSciencePerTurn, "Total Science Per Turn", "Science");
        return items;
    }

    public List<BreakdownItem> GetCultureBreakdown()
    {
        if (currentCiv == null)
            return new List<BreakdownItem>();

        var items = CollectFromProviders(currentCiv, cultureProviders);
        AddResidual(items, currentCiv.cachedCulturePerTurn, "Total Culture Per Turn", "Culture");
        return items;
    }

    public List<BreakdownItem> GetFaithBreakdown()
    {
        if (currentCiv == null)
            return new List<BreakdownItem>();

        var items = CollectFromProviders(currentCiv, faithProviders);
        AddResidual(items, currentCiv.cachedFaithPerTurn, "Total Faith Per Turn", "Faith");
        return items;
    }

    private static List<BreakdownItem> CollectFromProviders(Civilization civ, IEnumerable<IYieldProvider> providers)
    {
        var items = new List<BreakdownItem>();
        foreach (var provider in providers)
        {
            if (provider == null) continue;
            var providerItems = provider.GetBreakdown(civ);
            if (providerItems != null && providerItems.Count > 0)
                items.AddRange(providerItems);
        }

        return items;
    }

    private static void AddResidual(List<BreakdownItem> items, int targetTotal, string source, string category)
    {
        int known = 0;
        foreach (var item in items)
            known += item.amount;

        int residual = targetTotal - known;
        if (residual != 0)
        {
            items.Add(new BreakdownItem
            {
                source = source,
                amount = residual,
                category = category
            });
        }
    }
}

public interface IYieldProvider
{
    List<HudBreakdownService.BreakdownItem> GetBreakdown(Civilization civ);
}

internal static class BreakdownProviderHelpers
{
    public static void AddItem(List<HudBreakdownService.BreakdownItem> items, string source, int amount, string category)
    {
        if (amount == 0) return;
        items.Add(new HudBreakdownService.BreakdownItem
        {
            source = source,
            amount = amount,
            category = category
        });
    }

    public static (int food, int gold, int science, int culture, int faith, int policy) ComputeCombatUnitYield(Civilization civ, CombatUnit unit)
    {
        return civ.ComputeUnitPerTurnYield(
            unit.data,
            unit.planetIndex,
            unit.Weapon,
            unit.Shield,
            unit.Armor,
            unit.Miscellaneous);
    }
}

public class CityFoodProvider : IYieldProvider
{
    public List<HudBreakdownService.BreakdownItem> GetBreakdown(Civilization civ)
    {
        var items = new List<HudBreakdownService.BreakdownItem>();
        if (civ?.cities == null) return items;

        foreach (var city in civ.cities)
        {
            if (city == null) continue;
            int foodProduction = Mathf.RoundToInt(city.GetFoodPerTurn() * (1f + civ.foodModifier));
            if (foodProduction <= 0) continue;

            items.Add(new HudBreakdownService.BreakdownItem
            {
                source = $"City: {city.name}",
                amount = foodProduction,
                category = "City Yields"
            });
        }

        return items;
    }
}

public class CityFoodConsumptionProvider : IYieldProvider
{
    public List<HudBreakdownService.BreakdownItem> GetBreakdown(Civilization civ)
    {
        var items = new List<HudBreakdownService.BreakdownItem>();
        if (civ?.cities == null) return items;

        int total = 0;
        foreach (var city in civ.cities)
        {
            if (city == null) continue;
            total += city.GetFoodConsumptionPerTurn();
        }

        if (total > 0)
        {
            items.Add(new HudBreakdownService.BreakdownItem
            {
                source = "City Consumption",
                amount = -total,
                category = "Consumption"
            });
        }

        return items;
    }
}

public class UnitFoodConsumptionProvider : IYieldProvider
{
    public List<HudBreakdownService.BreakdownItem> GetBreakdown(Civilization civ)
    {
        var items = new List<HudBreakdownService.BreakdownItem>();
        if (civ == null) return items;

        int totalConsumption = 0;

        if (civ.combatUnits != null)
        {
            foreach (var unit in civ.combatUnits)
            {
                if (unit?.data == null) continue;
                totalConsumption += unit.data.foodConsumptionPerTurn;
            }
        }

        if (civ.workerUnits != null)
        {
            foreach (var unit in civ.workerUnits)
            {
                if (unit?.data == null) continue;
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

public class HerdFoodYieldProvider : IYieldProvider
{
    public List<HudBreakdownService.BreakdownItem> GetBreakdown(Civilization civ)
    {
        var items = new List<HudBreakdownService.BreakdownItem>();
        if (civ?.herds == null) return items;

        foreach (var herd in civ.herds)
        {
            if (herd == null) continue;

            var yields = herd.GetAnimalYields();
            var bonuses = civ.AggregateHerdYieldBonuses(herd, herd.planetIndex);
            int food = Mathf.RoundToInt((yields.Food + bonuses.foodAdd) * (1f + civ.foodModifier + bonuses.foodPct));
            if (food <= 0) continue;

            items.Add(new HudBreakdownService.BreakdownItem
            {
                source = $"Herd: {herd.name}",
                amount = food,
                category = "Herd Yields"
            });
        }

        return items;
    }
}

public class UnitFoodYieldProvider : IYieldProvider
{
    public List<HudBreakdownService.BreakdownItem> GetBreakdown(Civilization civ)
    {
        var items = new List<HudBreakdownService.BreakdownItem>();
        if (civ?.combatUnits == null) return items;

        foreach (var unit in civ.combatUnits)
        {
            if (unit?.data == null) continue;

            var yields = civ.ComputeUnitPerTurnYield(
                unit.data,
                unit.planetIndex,
                unit.Weapon,
                unit.Shield,
                unit.Armor,
                unit.Miscellaneous);

            int food = Mathf.RoundToInt(yields.food * (1f + civ.foodModifier));
            BreakdownProviderHelpers.AddItem(items, $"Unit: {unit.UnitName}", food, "Unit Yields");
        }

        return items;
    }
}

public class WorkerFoodYieldProvider : IYieldProvider
{
    public List<HudBreakdownService.BreakdownItem> GetBreakdown(Civilization civ)
    {
        var items = new List<HudBreakdownService.BreakdownItem>();
        if (civ?.workerUnits == null) return items;

        foreach (var unit in civ.workerUnits)
        {
            if (unit?.data == null) continue;

            var yields = civ.ComputeWorkerPerTurnYield(unit.data, unit.planetIndex);
            int food = Mathf.RoundToInt(yields.food * (1f + civ.foodModifier));
            BreakdownProviderHelpers.AddItem(items, $"Worker: {unit.UnitName}", food, "Worker Yields");
        }

        return items;
    }
}

public class FlatFoodBonusProvider : IYieldProvider
{
    public List<HudBreakdownService.BreakdownItem> GetBreakdown(Civilization civ)
    {
        var items = new List<HudBreakdownService.BreakdownItem>();
        if (civ == null) return items;

        var totalBonuses = civ.CalculateTotalBonuses(civ.researchedTechs, civ.researchedCultures);
        if (totalBonuses.flatFoodBonus != 0)
        {
            items.Add(new HudBreakdownService.BreakdownItem
            {
                source = "Flat Food Bonuses",
                amount = totalBonuses.flatFoodBonus,
                category = "Global Bonuses"
            });
        }

        return items;
    }
}

public class CityGoldProvider : IYieldProvider
{
    public List<HudBreakdownService.BreakdownItem> GetBreakdown(Civilization civ)
    {
        var items = new List<HudBreakdownService.BreakdownItem>();
        if (civ?.cities == null) return items;

        foreach (var city in civ.cities)
        {
            if (city == null) continue;

            int goldProduction = Mathf.RoundToInt(city.GetGoldPerTurn() * (1f + civ.goldModifier));
            if (goldProduction <= 0) continue;

            items.Add(new HudBreakdownService.BreakdownItem
            {
                source = $"City: {city.name}",
                amount = goldProduction,
                category = "City Yields"
            });
        }

        return items;
    }
}

public class TradeRouteGoldProvider : IYieldProvider
{
    public List<HudBreakdownService.BreakdownItem> GetBreakdown(Civilization civ)
    {
        var items = new List<HudBreakdownService.BreakdownItem>();
        if (civ == null) return items;

        var routes = civ.GetInterplanetaryTradeRoutes();
        if (routes == null) return items;

        foreach (var route in routes)
        {
            if (route == null) continue;

            int goldIncome = Mathf.RoundToInt(route.goldPerTurn * (1f + civ.goldModifier));
            if (goldIncome == 0) continue;

            string destination = route.destinationCity != null ? route.destinationCity.cityName : "Unknown";
            items.Add(new HudBreakdownService.BreakdownItem
            {
                source = $"Trade Route: {destination}",
                amount = goldIncome,
                category = "Trade"
            });
        }

        return items;
    }
}

public class UnitGoldYieldProvider : IYieldProvider
{
    public List<HudBreakdownService.BreakdownItem> GetBreakdown(Civilization civ)
    {
        var items = new List<HudBreakdownService.BreakdownItem>();
        if (civ?.combatUnits == null) return items;

        foreach (var unit in civ.combatUnits)
        {
            if (unit?.data == null) continue;

            var yields = BreakdownProviderHelpers.ComputeCombatUnitYield(civ, unit);

            int gold = Mathf.RoundToInt(yields.gold * (1f + civ.goldModifier));
            BreakdownProviderHelpers.AddItem(items, $"Unit: {unit.UnitName}", gold, "Unit Yields");
        }

        return items;
    }
}

public class WorkerGoldYieldProvider : IYieldProvider
{
    public List<HudBreakdownService.BreakdownItem> GetBreakdown(Civilization civ)
    {
        var items = new List<HudBreakdownService.BreakdownItem>();
        if (civ?.workerUnits == null) return items;

        foreach (var unit in civ.workerUnits)
        {
            if (unit?.data == null) continue;

            var yields = civ.ComputeWorkerPerTurnYield(unit.data, unit.planetIndex);
            int gold = Mathf.RoundToInt(yields.gold * (1f + civ.goldModifier));
            BreakdownProviderHelpers.AddItem(items, $"Worker: {unit.UnitName}", gold, "Worker Yields");
        }

        return items;
    }
}

public class HerdGoldYieldProvider : IYieldProvider
{
    public List<HudBreakdownService.BreakdownItem> GetBreakdown(Civilization civ)
    {
        var items = new List<HudBreakdownService.BreakdownItem>();
        if (civ?.herds == null) return items;

        foreach (var herd in civ.herds)
        {
            if (herd == null) continue;

            var yields = herd.GetAnimalYields();
            var bonuses = civ.AggregateHerdYieldBonuses(herd, herd.planetIndex);
            int gold = Mathf.RoundToInt((yields.Gold + bonuses.goldAdd) * (1f + civ.goldModifier + bonuses.goldPct));
            if (gold == 0) continue;

            items.Add(new HudBreakdownService.BreakdownItem
            {
                source = $"Herd: {herd.name}",
                amount = gold,
                category = "Herd Yields"
            });
        }

        return items;
    }
}

public class FlatGoldBonusProvider : IYieldProvider
{
    public List<HudBreakdownService.BreakdownItem> GetBreakdown(Civilization civ)
    {
        var items = new List<HudBreakdownService.BreakdownItem>();
        if (civ == null) return items;

        var totalBonuses = civ.CalculateTotalBonuses(civ.researchedTechs, civ.researchedCultures);
        if (totalBonuses.flatGoldBonus != 0)
        {
            items.Add(new HudBreakdownService.BreakdownItem
            {
                source = "Flat Gold Bonuses",
                amount = totalBonuses.flatGoldBonus,
                category = "Global Bonuses"
            });
        }

        return items;
    }
}

public class CityPolicyProvider : IYieldProvider
{
    public List<HudBreakdownService.BreakdownItem> GetBreakdown(Civilization civ)
    {
        var items = new List<HudBreakdownService.BreakdownItem>();
        if (civ?.cities == null) return items;

        foreach (var city in civ.cities)
        {
            if (city == null) continue;

            int policyProduction = city.GetPolicyPointPerTurn();
            if (policyProduction == 0) continue;

            items.Add(new HudBreakdownService.BreakdownItem
            {
                source = $"City: {city.name}",
                amount = policyProduction,
                category = "City Yields"
            });
        }

        return items;
    }
}

public class UnitPolicyYieldProvider : IYieldProvider
{
    public List<HudBreakdownService.BreakdownItem> GetBreakdown(Civilization civ)
    {
        var items = new List<HudBreakdownService.BreakdownItem>();
        if (civ?.combatUnits == null) return items;

        foreach (var unit in civ.combatUnits)
        {
            if (unit?.data == null) continue;
            var yields = BreakdownProviderHelpers.ComputeCombatUnitYield(civ, unit);
            BreakdownProviderHelpers.AddItem(items, $"Unit: {unit.UnitName}", yields.policy, "Unit Yields");
        }

        return items;
    }
}

public class WorkerPolicyYieldProvider : IYieldProvider
{
    public List<HudBreakdownService.BreakdownItem> GetBreakdown(Civilization civ)
    {
        var items = new List<HudBreakdownService.BreakdownItem>();
        if (civ?.workerUnits == null) return items;

        foreach (var unit in civ.workerUnits)
        {
            if (unit?.data == null) continue;
            var yields = civ.ComputeWorkerPerTurnYield(unit.data, unit.planetIndex);

            BreakdownProviderHelpers.AddItem(items, $"Worker: {unit.UnitName}", yields.policy, "Worker Yields");
        }

        return items;
    }
}

public class HerdPolicyYieldProvider : IYieldProvider
{
    public List<HudBreakdownService.BreakdownItem> GetBreakdown(Civilization civ)
    {
        var items = new List<HudBreakdownService.BreakdownItem>();
        if (civ?.herds == null) return items;

        foreach (var herd in civ.herds)
        {
            if (herd == null) continue;
            var yields = herd.GetAnimalYields();
            var bonuses = civ.AggregateHerdYieldBonuses(herd, herd.planetIndex);
            int policy = yields.Policy + bonuses.policyPointsAdd;

            if (policy == 0) continue;
            items.Add(new HudBreakdownService.BreakdownItem
            {
                source = $"Herd: {herd.name}",
                amount = policy,
                category = "Herd Yields"
            });
        }

        return items;
    }
}
