using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum BandState { Packed, Encamped }
public enum BandLossReason { Starvation, AnimalAttack, Scripted, ConvertedToSettlement }

/// <summary>
/// A campaign proto-settlement. Deliberately does not inherit BaseUnit and has no combat
/// health, attack, or defence. Its real CombatUnit garrison is its sole military force.
/// </summary>
public sealed class Band : MonoBehaviour
{
    [SerializeField] private BandData data;
    [SerializeField] private string persistentId;
    [SerializeField] private int planetIndex;
    [SerializeField] private int currentTileIndex = -1;
    [SerializeField] private BandState state = BandState.Packed;
    [SerializeField] private int population;
    [SerializeField] private int foodReserve;
    [SerializeField] private int consecutiveStarvationTurns;
    [SerializeField] private int currentMovePoints;
    [SerializeField] private List<CombatUnit> garrison = new List<CombatUnit>();
    [SerializeField] private List<BandStructureData> builtStructures = new List<BandStructureData>();
    [SerializeField] private BandStructureData queuedStructure;
    [SerializeField] private CombatUnitData queuedUnit;
    [SerializeField] private int productionProgress;
    private readonly List<GameObject> structureVisuals = new List<GameObject>();
    private Civilization owner;

    public static event Action<Band> BandCreated, BandPacked, BandEncamped, BandMoved;
    public static event Action<Band, Civilization, Civilization> BandCaptured;
    public static event Action<Band, BandLossReason> BandDestroyed;
    public static event Action<Band> BandStarvationStarted, BandStarvationEnded, BandGarrisonChanged;
    public static event Action<Band, int, int> BandPopulationChanged;
    public static event Action<Band, BandStructureData> BandStructureCompleted;

    public BandData Data => data;
    public string PersistentId => string.IsNullOrEmpty(persistentId) ? (persistentId = Guid.NewGuid().ToString("N")) : persistentId;
    public Civilization Owner => owner;
    public int PlanetIndex => planetIndex;
    public int CurrentTileIndex => currentTileIndex;
    public BandState State => state;
    public int Population => population;
    public int FoodReserve => foodReserve;
    public int ConsecutiveStarvationTurns => consecutiveStarvationTurns;
    public int CurrentMovePoints => currentMovePoints;
    public IReadOnlyList<CombatUnit> Garrison => garrison;
    public IReadOnlyList<BandStructureData> BuiltStructures => builtStructures;
    public BandStructureData QueuedStructure => queuedStructure;
    public CombatUnitData QueuedUnit => queuedUnit;
    public int ProductionProgress => productionProgress;
    public bool IsStarving => consecutiveStarvationTurns > 0;
    public int FoodRequiredPerTurn => data == null ? 0 : Mathf.Max(0, data.baseFoodConsumptionPerTurn) + Mathf.CeilToInt(population / (float)Mathf.Max(1, data.populationPerFoodUnit));
    public int FoodCapacity => Mathf.Max(0, data != null ? data.foodStorageCapacity : 0) + builtStructures.Where(x => x != null).Sum(x => x.foodStorageBonus);
    public int GarrisonCapacity => Mathf.Max(0, data != null ? data.baseGarrisonCapacity : 0) + builtStructures.Where(x => x != null).Sum(x => x.garrisonCapacityBonus);

    public void Initialize(BandData bandData, Civilization bandOwner, int startPlanet, int startTile)
    {
        if (bandData == null) throw new ArgumentNullException(nameof(bandData));
        data = bandData; owner = bandOwner; planetIndex = startPlanet; currentTileIndex = startTile;
        state = BandState.Packed; population = Mathf.Max(1, data.startingPopulation);
        foodReserve = Mathf.Clamp(data.startingFoodReserve, 0, FoodCapacity);
        consecutiveStarvationTurns = 0; currentMovePoints = Mathf.Max(0, data.movementPoints);
        owner?.RegisterBand(this);
        PositionVisual(); RefreshVisuals();
        SpawnStartingGarrison();
        BandCreated?.Invoke(this);
    }

    /// <summary>Restores Band-owned state after ordinary CombatUnits/formations have loaded.</summary>
    public void RestoreState(string savedId, BandState savedState, int savedPopulation, int savedFood,
        int savedStarvationTurns, int savedMovePoints, IEnumerable<BandStructureData> structures,
        BandStructureData savedQueuedStructure, CombatUnitData savedQueuedUnit, int savedProgress)
    {
        if (!string.IsNullOrEmpty(savedId)) persistentId = savedId;
        state = savedState;
        population = Mathf.Max(0, savedPopulation);
        builtStructures = structures != null ? structures.Where(x => x != null).Distinct().ToList() : new List<BandStructureData>();
        foodReserve = Mathf.Clamp(savedFood, 0, FoodCapacity);
        consecutiveStarvationTurns = Mathf.Max(0, savedStarvationTurns);
        currentMovePoints = Mathf.Max(0, savedMovePoints);
        queuedStructure = savedQueuedStructure;
        queuedUnit = savedQueuedUnit;
        productionProgress = Mathf.Max(0, savedProgress);
        PositionVisual(); RefreshVisuals();
    }

    public void ResetForNewTurn()
    {
        currentMovePoints = Mathf.Max(0, data.movementPoints + builtStructures.Where(x => x != null).Sum(x => x.movementBonus));
        ProcessFoodUpkeep();
        if (this != null && state == BandState.Encamped) ProcessProduction(GetProductionYield());
    }

    public void ProcessFoodUpkeep()
    {
        int required = FoodRequiredPerTurn;
        if (foodReserve >= required)
        {
            foodReserve -= required;
            bool wasStarving = consecutiveStarvationTurns > 0;
            consecutiveStarvationTurns = 0;
            if (wasStarving) BandStarvationEnded?.Invoke(this);
            return;
        }
        foodReserve = 0;
        if (consecutiveStarvationTurns == 0) BandStarvationStarted?.Invoke(this);
        consecutiveStarvationTurns++;
        if (consecutiveStarvationTurns > data.starvationGraceTurns)
        {
            int old = population;
            population -= Mathf.Max(1, Mathf.CeilToInt(population * data.populationLossPctPerStarvingTurn));
            BandPopulationChanged?.Invoke(this, old, population);
        }
        if (population <= 0 || consecutiveStarvationTurns >= data.collapseAfterStarvationTurns)
            DestroyBand(BandLossReason.Starvation);
    }

    public bool Pack()
    {
        if (state == BandState.Packed || currentMovePoints < data.packMovementCost) return false;
        currentMovePoints -= data.packMovementCost; state = BandState.Packed; RefreshVisuals(); BandPacked?.Invoke(this); return true;
    }

    public bool Encamp()
    {
        if (state == BandState.Encamped || currentMovePoints < data.encampMovementCost) return false;
        currentMovePoints -= data.encampMovementCost; state = BandState.Encamped; RefreshVisuals(); BandEncamped?.Invoke(this); return true;
    }

    public bool TryMove(int tileIndex, int cost = 1)
    {
        if (state != BandState.Packed || tileIndex < 0 || cost < 0 || currentMovePoints < cost) return false;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var tile = ts != null ? ts.GetTileData(tileIndex) : null;
        if (tile == null || !tile.isPassable) return false;
        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        if (occ != null && occ.GetOccupantObject(tileIndex, TileLayer.Surface) != null) return false;
        if (occ != null && currentTileIndex >= 0) occ.ClearOccupantById(currentTileIndex, TileLayer.Surface, gameObject.GetRuntimeId());
        currentTileIndex = tileIndex; currentMovePoints -= cost; PositionVisual();
        occ?.SetOccupant(tileIndex, gameObject, TileLayer.Surface); BandMoved?.Invoke(this); return true;
    }

    public int Forage(int amount = -1)
    {
        if (currentMovePoints < data.forageMovementCost) return 0;
        int gathered = amount >= 0 ? amount : data.baseForageFood + builtStructures.Where(x => x != null).Sum(x => x.forageBonus);
        int accepted = Mathf.Clamp(gathered, 0, Mathf.Max(0, FoodCapacity - foodReserve));
        foodReserve += accepted; currentMovePoints -= data.forageMovementCost; return accepted;
    }

    public bool QueueStructure(BandStructureData structure)
    {
        if (state != BandState.Encamped || structure == null || !data.allowedStructures.Contains(structure) || builtStructures.Contains(structure)) return false;
        queuedStructure = structure; queuedUnit = null; productionProgress = 0; return true;
    }

    public bool QueueMilitaryUnit(CombatUnitData unit)
    {
        if (state != BandState.Encamped || unit == null || !unit.buildableByBand || !data.allowedMilitaryRecruitment.Contains(unit)) return false;
        queuedUnit = unit; queuedStructure = null; productionProgress = 0; return true;
    }

    public void ProcessProduction(int production)
    {
        if (state != BandState.Encamped || production <= 0) return;
        productionProgress += production;
        if (queuedStructure != null && productionProgress >= queuedStructure.productionCost)
        {
            var completed = queuedStructure; builtStructures.Add(completed); queuedStructure = null; productionProgress = 0;
            RefreshVisuals(); BandStructureCompleted?.Invoke(this, completed);
        }
        else if (queuedUnit != null && productionProgress >= Mathf.Max(1, queuedUnit.bandProductionCost))
        {
            var completed = queuedUnit; queuedUnit = null; productionProgress = 0; SpawnAndGarrison(completed);
        }
    }

    public bool TryAddToGarrison(CombatUnit unit)
    {
        if (unit == null || unit.owner != owner || unit.planetIndex != planetIndex || garrison.Contains(unit) || garrison.Count >= GarrisonCapacity) return false;
        if (unit.currentTileIndex >= 0 && unit.currentTileIndex != currentTileIndex) return false;
        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        if (unit.currentTileIndex >= 0) occ?.ClearOccupantById(unit.currentTileIndex, unit.currentLayer, unit.gameObject.GetRuntimeId());
        garrison.Add(unit); unit.StoreInBand(this); BandGarrisonChanged?.Invoke(this); return true;
    }

    public bool FormArmy(IList<CombatUnit> selected, out CombatUnit representative)
    {
        representative = null;
        if (selected == null || selected.Count == 0 || selected.Any(x => x == null || !garrison.Contains(x))) return false;
        if (selected.Count > (owner != null ? owner.GetMaxArmySize() : CampaignArmyService.DefaultArmySize)) return false;
        string id = Guid.NewGuid().ToString("N");
        for (int i = 0; i < selected.Count; i++)
        {
            var unit = selected[i]; garrison.Remove(unit); unit.ReleaseFromBand(currentTileIndex, planetIndex);
            unit.AssignMilitaryFormation(id, MilitaryFormationType.Army); unit.stackSlot = i;
            if (i == 0) representative = unit;
        }
        CampaignArmyService.RefreshPresentation(representative); BandGarrisonChanged?.Invoke(this); return true;
    }

    public void Capture(Civilization newOwner)
    {
        if (newOwner == null || newOwner == owner) return;
        var old = owner; old?.UnregisterBand(this); owner = newOwner; newOwner.RegisterBand(this); RefreshVisuals();
        BandCaptured?.Invoke(this, old, newOwner);
    }

    public void DestroyBand(BandLossReason reason)
    {
        if (reason == BandLossReason.Starvation || reason == BandLossReason.Scripted)
            FormArmy(garrison.ToList(), out _);
        else
            foreach (var unit in garrison.ToList())
            {
                garrison.Remove(unit);
                if (unit != null) { owner?.combatUnits.Remove(unit); Destroy(unit.gameObject); }
            }
        owner?.UnregisterBand(this);
        (TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance)?.ClearOccupantById(currentTileIndex, TileLayer.Surface, gameObject.GetRuntimeId());
        BandDestroyed?.Invoke(this, reason); Destroy(gameObject);
    }

    private int GetProductionYield() => Mathf.Max(0, data.encampedYields.production + builtStructures.Where(x => x != null).Sum(x => x.yields.production));
    private void SpawnStartingGarrison() { foreach (var e in data.startingGarrison) if (e != null) for (int i = 0; i < e.count; i++) SpawnAndGarrison(e.unit); }
    private bool SpawnAndGarrison(CombatUnitData unitData)
    {
        if (unitData == null || garrison.Count >= GarrisonCapacity) return false;
        var prefab = unitData.GetPrefab(owner); if (prefab == null) return false;
        var go = Instantiate(prefab, transform.position, Quaternion.identity, transform.parent); var unit = go.GetComponent<CombatUnit>();
        if (unit == null) { Destroy(go); return false; }
        unit.Initialize(unitData, owner); unit.planetIndex = planetIndex; unit.currentTileIndex = currentTileIndex;
        if (owner != null && !owner.combatUnits.Contains(unit)) owner.combatUnits.Add(unit);
        return TryAddToGarrison(unit);
    }
    private void PositionVisual() { var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance; if (ts != null && currentTileIndex >= 0) transform.position = ts.GetTileCenterFlat(currentTileIndex); }
    private void RefreshVisuals()
    {
        foreach (var visual in structureVisuals) if (visual != null) Destroy(visual); structureVisuals.Clear();
        if (state == BandState.Encamped) foreach (var s in builtStructures) if (s != null && s.visualAttachmentPrefab != null) structureVisuals.Add(Instantiate(s.visualAttachmentPrefab, transform));
    }
}
