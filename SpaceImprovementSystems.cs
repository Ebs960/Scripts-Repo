using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum SpaceImprovementType { Satellite, SensorArray, TradePost, OrbitalTradeHub, SpaceStation, RepairStation, Shipyard, OrbitalFortress, AsteroidMine, IceHarvester, GasCollector, SolarCollector, OrbitalRefinery, ResearchStation, ColonyHabitat, JumpGate }

[Serializable]
public class SpacePlacementRuleSet
{
    public SpaceTerrainType[] allowedTerrainTypes; public bool requiresOrbitSector; public bool requiresDeepSpace; public bool requiresResourceDeposit; public bool requiresFriendlyControl;
    public int minimumDistanceFromPlanet; public int maximumDistanceFromPlanet = -1; public int minimumDistanceFromSameImprovement; public int maximumPerPlanet = -1; public int maximumPerCivilization = -1;
    public bool mayShareTileWithNaturalFeature; public bool mayShareTileWithAnotherImprovement;
}

[CreateAssetMenu(fileName = "New Space Improvement", menuName = "Data/Space/Space Improvement")]
public class SpaceImprovementData : ScriptableObject
{
    [Header("Identity")] public string improvementId; public string displayName; [TextArea] public string description; public SpaceImprovementType improvementType; public Sprite icon; public GameObject constructionPrefab; public GameObject completedPrefab; public GameObject destroyedPrefab;
    [Header("Construction")] public int constructionCost; public int goldCost; public ResourceCost[] resourceCosts; public TechData[] requiredTechs;
    [Header("Placement")] public SpacePlacementRuleSet placementRules;
    [Header("Durability")] public int maximumHealth = 100; public int defense; public bool mayBeCaptured; public bool leavesWreckage;
    [Header("Ownership")] public bool claimsTile; public int controlRadius;
    [Header("Trade")] public TradeNodeCapability tradeCapability;
    [Header("Vision and Recon")] public int visionRange; public int detectionStrength; public int reconRange;
    [Header("Repair")] public int repairPerTurn; public int repairRange; public int repairCapacity;
    [Header("Combat")] public bool canAttack; public int spaceAttack; public int attackRange; public int attacksPerTurn; public bool contributesToBlockadeControl;
    [Header("Production")] public bool canProduceShips; public CombatUnitData[] permittedShipTypes; public int productionBonus;
    [Header("Extraction")] public bool extractsResources; public string requiredResourceType; public int extractionPerTurn;
    [Header("Storage")] public int shipStorageCapacity; public int cargoStorageCapacity;
    [Header("Yields")] public int goldPerTurn; public int sciencePerTurn; public int productionPerTurn;
}

[Serializable]
public class SpaceImprovementInstance : ISpaceCombatTarget
{
    public int instanceId; public string improvementDataId; public int ownerCivilizationId; public int spaceTileIndex; public int associatedPlanetId = -1; public int currentHealth; public bool operational; public bool disabled; public bool underConstruction; public int storedProduction; public int storedShips; public int storedCargo; public List<string> appliedUpgradeIds = new List<string>();
    public int SpaceTileIndex => spaceTileIndex; public int CurrentHealth => currentHealth; public int Defense => SpaceImprovementRegistry.GetData(improvementDataId)?.defense ?? 0;
    public bool ApplySpaceDamage(int amount, int attackerId) { currentHealth = Mathf.Max(0, currentHealth - Mathf.Max(0, amount)); if (currentHealth <= 0) { operational = false; disabled = true; } return currentHealth <= 0; }
}

public interface ISpaceTradeNode { TradeNodeRuntime BuildTradeNode(); }
public interface ISpaceRepairSource { int RepairRange { get; } int RepairPerTurn { get; } }
public interface ISpaceSensorSource { int VisionRange { get; } int DetectionStrength { get; } }
public interface ISpaceProductionSource { bool CanProduce(CombatUnitData unit); }
public interface ISpaceBlockadeContributor { int GetSectorControlStrength(); }
public interface ISpaceCombatTarget { int SpaceTileIndex { get; } int CurrentHealth { get; } int Defense { get; } bool ApplySpaceDamage(int amount, int attackerId); }

public static class SpaceImprovementRegistry
{
    private static readonly Dictionary<string, SpaceImprovementData> data = new Dictionary<string, SpaceImprovementData>();
    public static void Register(SpaceImprovementData value) { if (value != null && !string.IsNullOrEmpty(value.improvementId)) data[value.improvementId] = value; }
    public static IEnumerable<SpaceImprovementData> All => data.Values;
    public static SpaceImprovementData GetData(string id) => id != null && data.TryGetValue(id, out var value) ? value : null;
}

[Serializable]
public class SpaceConstructionOrder
{
    public int orderId; public int ownerCivilizationId; public string improvementDataId; public int targetTileIndex; public int requiredProduction; public int accumulatedProduction; public List<int> contributingUnitIds = new List<int>(); public bool paused; public bool completed;
}

public class SpaceConstructionManager : MonoBehaviour
{
    private bool committingCosts;

    public bool CanConstruct(Civilization civilization, SpaceImprovementData data, int tileIndex, out string reason)
    {
        var state = SpaceWorldManager.Instance?.CurrentSystem;
        var tile = state?.grid?.GetTile(tileIndex);
        if (civilization == null) return Reject("No civilization selected", out reason);
        if (data == null || string.IsNullOrWhiteSpace(data.improvementId)) return Reject("Invalid improvement definition", out reason);
        if (tile == null || tile.planetId >= 0) return Reject("Requires a space sector outside a celestial body", out reason);

        int owner = civilization.gameObject.GetRuntimeId();
        var rules = data.placementRules;
        var pending = state.constructionOrders.Where(o => o != null && !o.completed);
        if (pending.Any(o => o.targetTileIndex == tileIndex))
            return Reject("A project already occupies this sector", out reason);
        if (rules?.mayShareTileWithAnotherImprovement != true && (tile.stationId >= 0 || tile.improvementInstanceIds.Count > 0))
            return Reject("Sector already has a structure", out reason);
        if (rules?.mayShareTileWithNaturalFeature != true && tile.featureInstanceIds.Count > 0)
            return Reject("Sector contains a natural feature", out reason);

        if (rules != null)
        {
            if (rules.allowedTerrainTypes?.Length > 0 && !rules.allowedTerrainTypes.Contains(tile.terrainType))
                return Reject("Terrain is unsuitable", out reason);
            if (rules.requiresOrbitSector && !tile.isPlanetOrbitSector) return Reject("Requires an orbit sector", out reason);
            if (rules.requiresDeepSpace && tile.isPlanetOrbitSector) return Reject("Requires deep space", out reason);
            if (rules.requiresFriendlyControl && tile.controllingCivilizationId != owner)
                return Reject("Requires control by your civilization", out reason);

            int distance = state.grid.tiles.Where(t => t.planetId >= 0)
                .Select(t => state.grid.GetDistance(tileIndex, t.tileIndex)).DefaultIfEmpty(int.MaxValue).Min();
            if (distance < rules.minimumDistanceFromPlanet || (rules.maximumDistanceFromPlanet >= 0 && distance > rules.maximumDistanceFromPlanet))
                return Reject("Outside the permitted distance from the nearest celestial body", out reason);

            var existing = state.improvements.Where(i => i != null && i.currentHealth > 0 && i.improvementDataId == data.improvementId);
            var matchingOrders = pending.Where(o => o.improvementDataId == data.improvementId);
            if (rules.minimumDistanceFromSameImprovement > 0 &&
                existing.Select(i => i.spaceTileIndex).Concat(matchingOrders.Select(o => o.targetTileIndex))
                    .Any(index => state.grid.GetDistance(tileIndex, index) < rules.minimumDistanceFromSameImprovement))
                return Reject("Too close to another structure or project of this type", out reason);

            if (rules.maximumPerCivilization >= 0 &&
                existing.Count(i => i.ownerCivilizationId == owner) + matchingOrders.Count(o => o.ownerCivilizationId == owner) >= rules.maximumPerCivilization)
                return Reject("Civilization structure limit reached", out reason);
            if (rules.maximumPerPlanet >= 0 && tile.associatedPlanetId >= 0 &&
                existing.Count(i => (i.associatedPlanetId >= 0 ? i.associatedPlanetId : state.grid.GetTile(i.spaceTileIndex)?.associatedPlanetId) == tile.associatedPlanetId) +
                matchingOrders.Count(o => state.grid.GetTile(o.targetTileIndex)?.associatedPlanetId == tile.associatedPlanetId) >= rules.maximumPerPlanet)
                return Reject("Planet structure limit reached", out reason);
        }

        if ((rules?.requiresResourceDeposit == true || !string.IsNullOrEmpty(data.requiredResourceType)) &&
            !HasDeposit(tile, data.requiredResourceType, owner))
            return Reject("Requires a matching known resource deposit", out reason);
        foreach (var tech in data.requiredTechs ?? Array.Empty<TechData>())
            if (tech != null && civilization.researchedTechs?.Contains(tech) != true)
                return Reject($"Requires {tech.techName}", out reason);
        if (data.goldCost > 0 && civilization.gold < data.goldCost) return Reject("Insufficient gold", out reason);
        if (!ResourceCost.CanAfford(civilization, GetResourceCosts(data))) return Reject("Insufficient resources", out reason);

        reason = null;
        return true;
    }

    private static bool HasDeposit(SpaceHexTile tile, string resourceType, int owner)
    {
        bool Matches(SpaceResourceData deposit) => deposit != null && !string.IsNullOrEmpty(deposit.resourceId) &&
            (string.IsNullOrEmpty(resourceType) || deposit.resourceId == resourceType);

        if (Matches(tile.resource) && !tile.resource.hidden && tile.resource.quantity > 0) return true;
        var manager = SpaceFeatureManager.Instance;
        if (manager == null) return false;
        foreach (int id in tile.featureInstanceIds)
        {
            var feature = manager.GetFeature(id);
            if (feature == null || feature.remainingResourceQuantity <= 0) continue;
            var data = manager.GetFeatureData(feature.featureDataId);
            if (Matches(data?.resourceDeposit) &&
                ((!data.hiddenUntilDetected && !data.resourceDeposit.hidden) || feature.civilizationsThatDiscoveredIt?.Contains(owner) == true))
                return true;
        }
        return false;
    }

    private static bool Reject(string message, out string reason)
    {
        reason = message;
        return false;
    }

    public static ResourceCost[] GetResourceCosts(SpaceImprovementData data) =>
        (data.resourceCosts ?? Array.Empty<ResourceCost>())
            .Where(cost => cost != null && cost.resource != null && cost.amount > 0)
            .GroupBy(cost => cost.resource)
            .Select(group => new ResourceCost { resource = group.Key, amount = group.Sum(cost => cost.amount) })
            .ToArray();

    public bool StartConstruction(Civilization civilization, SpaceImprovementData data, int tileIndex, out SpaceConstructionOrder order, out string reason)
    {
        order = null;
        if (committingCosts) return Reject("Another construction transaction is in progress", out reason);
        if (!CanConstruct(civilization, data, tileIndex, out reason)) return false;
        committingCosts = true;
        try
        {
            if (!ResourceCost.Consume(civilization, GetResourceCosts(data))) return Reject("Insufficient resources", out reason);
            if (data.goldCost > 0) civilization.gold -= data.goldCost;
        }
        finally
        {
            committingCosts = false;
        }
        var state = SpaceWorldManager.Instance.CurrentSystem;
        SpaceImprovementRegistry.Register(data);
        order = new SpaceConstructionOrder
        {
            orderId = state.nextConstructionOrderId++,
            ownerCivilizationId = civilization.gameObject.GetRuntimeId(),
            improvementDataId = data.improvementId,
            targetTileIndex = tileIndex,
            requiredProduction = Mathf.Max(1, data.constructionCost)
        };
        state.constructionOrders.Add(order);
        return true;
    }

    public void AddConstructionWork(SpaceConstructionOrder order, int production)
    {
        var state = SpaceWorldManager.Instance?.CurrentSystem;
        if (order == null || order.completed || order.paused || state == null || !state.constructionOrders.Contains(order)) return;
        order.accumulatedProduction += Mathf.Min(Mathf.Max(0, production), Mathf.Max(0, order.requiredProduction - order.accumulatedProduction));
        if (order.accumulatedProduction < order.requiredProduction) return;

        var data = SpaceImprovementRegistry.GetData(order.improvementDataId);
        var tile = state.grid.GetTile(order.targetTileIndex);
        if (data == null || tile == null) return;
        var improvement = new SpaceImprovementSpawner().Spawn(state, data, order.ownerCivilizationId, order.targetTileIndex);
        improvement.associatedPlanetId = tile.associatedPlanetId;
        if (data.claimsTile)
        {
            foreach (var sector in state.grid.tiles)
                if (sector.planetId < 0 && sector.controllingCivilizationId == -1 &&
                    state.grid.GetDistance(tile.tileIndex, sector.tileIndex) <= Mathf.Max(0, data.controlRadius))
                    sector.controllingCivilizationId = order.ownerCivilizationId;
        }
        order.completed = true;
    }

    public void ProcessConstructionTurn(Civilization civilization)
    {
        var state = SpaceWorldManager.Instance?.CurrentSystem;
        if (state == null || civilization == null) return;
        int owner = civilization.gameObject.GetRuntimeId();
        foreach (var order in state.constructionOrders)
            if (order != null && order.ownerCivilizationId == owner) AddConstructionWork(order, 1);
    }

    // Upfront costs are committed when a project begins, as with band production.
    public void CancelConstruction(SpaceConstructionOrder order)
    {
        if (order != null && !order.completed)
            SpaceWorldManager.Instance?.CurrentSystem?.constructionOrders.Remove(order);
    }
}
