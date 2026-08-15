using System;
using System.Collections.Generic;
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
    public static SpaceImprovementData GetData(string id) { data.TryGetValue(id, out var value); return value; }
}

[Serializable]
public class SpaceConstructionOrder
{
    public int orderId; public int ownerCivilizationId; public string improvementDataId; public int targetTileIndex; public int requiredProduction; public int accumulatedProduction; public List<int> contributingUnitIds = new List<int>(); public bool paused; public bool completed;
}

public class SpaceConstructionManager : MonoBehaviour
{
    public bool CanConstruct(Civilization civilization, SpaceImprovementData data, int tileIndex, out string reason)
    {
        reason = null; var world = SpaceWorldManager.Instance; var tile = world?.Grid?.GetTile(tileIndex);
        if (civilization == null) { reason = "missing civilization"; return false; }
        if (data == null) { reason = "missing improvement data"; return false; }
        if (tile == null) { reason = "invalid tile"; return false; }
        var rules = data.placementRules;
        if (rules != null)
        {
            if (!rules.mayShareTileWithAnotherImprovement && tile.improvementInstanceIds.Count > 0) { reason = "tile already has a space improvement"; return false; }
            if (!rules.mayShareTileWithNaturalFeature && tile.featureInstanceIds.Count > 0) { reason = "tile contains a natural feature"; return false; }
            if (rules.requiresOrbitSector && !tile.isPlanetOrbitSector) { reason = "requires orbit sector"; return false; }
            if (rules.requiresDeepSpace && (tile.isPlanetOrbitSector || tile.planetId >= 0)) { reason = "requires deep space"; return false; }
            if (rules.requiresResourceDeposit && tile.resource == null && tile.featureInstanceIds.Count == 0) { reason = "requires resource deposit"; return false; }
            if (rules.requiresFriendlyControl && tile.controllingCivilizationId != civilization.gameObject.GetRuntimeId()) { reason = "requires friendly control"; return false; }
        }
        return true;
    }
    public bool StartConstruction(Civilization civilization, SpaceImprovementData data, int tileIndex, out SpaceConstructionOrder order, out string reason)
    {
        order = null; if (!CanConstruct(civilization, data, tileIndex, out reason)) return false;
        var state = SpaceWorldManager.Instance.CurrentSystem; SpaceImprovementRegistry.Register(data);
        order = new SpaceConstructionOrder { orderId = state.nextConstructionOrderId++, ownerCivilizationId = civilization.gameObject.GetRuntimeId(), improvementDataId = data.improvementId, targetTileIndex = tileIndex, requiredProduction = Mathf.Max(1, data.constructionCost) };
        state.constructionOrders.Add(order); return true;
    }
    public void AddConstructionWork(SpaceConstructionOrder order, int production) { if (order == null || order.completed || order.paused) return; order.accumulatedProduction += Mathf.Max(0, production); if (order.accumulatedProduction >= order.requiredProduction) Complete(order); }
    public void ProcessConstructionTurn(Civilization civilization) { var state = SpaceWorldManager.Instance?.CurrentSystem; if (state == null || civilization == null) return; int id = civilization.gameObject.GetRuntimeId(); foreach (var o in state.constructionOrders) if (o.ownerCivilizationId == id) AddConstructionWork(o, 1); }
    public void CancelConstruction(SpaceConstructionOrder order) { SpaceWorldManager.Instance?.CurrentSystem?.constructionOrders.Remove(order); }
    private void Complete(SpaceConstructionOrder order)
    {
        var state = SpaceWorldManager.Instance.CurrentSystem; var data = SpaceImprovementRegistry.GetData(order.improvementDataId); order.completed = true;
        var inst = new SpaceImprovementInstance { instanceId = state.nextImprovementId++, improvementDataId = order.improvementDataId, ownerCivilizationId = order.ownerCivilizationId, spaceTileIndex = order.targetTileIndex, currentHealth = data != null ? data.maximumHealth : 1, operational = true };
        state.improvements.Add(inst);
        SpaceWorldManager.Instance.Entities.Register(inst);
        SpaceWorldManager.Instance.Grid.GetTile(order.targetTileIndex)?.improvementInstanceIds.Add(inst.instanceId);
    }
}
