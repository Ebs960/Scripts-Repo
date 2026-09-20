using System;
using System.Collections;
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
    [Header("Presentation")]
    [SerializeField, Tooltip("Parent for the currently active packed or encamped prefab. Defaults to this transform for legacy prefabs.")]
    private Transform visualRoot;

    [Header("Packed Visual Animation")]
    [SerializeField] private string packedMovingParameter = "Moving";
    [SerializeField] private string packedIdleVariantParameter = "IdleVariant";
    [SerializeField] private string packedWalkVariantParameter = "WalkVariant";
    [Min(1), SerializeField] private int packedIdleVariantCount = 1;
    [Min(1), SerializeField] private int packedWalkVariantCount = 1;
    [SerializeField] private Vector2 packedAnimatorSpeedRange = new Vector2(.95f, 1.05f);
    [Min(0.01f), SerializeField] private float visualMoveDuration = .4f;

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
    private GameObject stateVisual;
    private Animator[] packedAnimators = Array.Empty<Animator>();
    private Coroutine visualMoveRoutine;
    private Transform visualMovementRoot;
    private Vector3 visualMovementRestingLocalPosition;
    private Civilization owner;

    public static event Action<Band> BandCreated, BandPacked, BandEncamped, BandMoved;
    public static event Action<Band, Civilization, Civilization> BandCaptured;
    public static event Action<Band, BandLossReason> BandDestroyed;
    public static event Action<Band> BandStarvationStarted, BandStarvationEnded, BandGarrisonChanged;
    public static event Action<Band> BandChanged;
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

    public void Initialize(BandData bandData, Civilization bandOwner, int startPlanet, int startTile,
        IEnumerable<StartingBandGarrisonEntry> startingGarrisonOverride = null, bool spawnStartingGarrison = true)
    {
        if (bandData == null) throw new ArgumentNullException(nameof(bandData));
        data = bandData; owner = bandOwner; planetIndex = startPlanet; currentTileIndex = startTile;
        state = BandState.Packed; population = Mathf.Max(1, data.startingPopulation);
        foodReserve = Mathf.Clamp(data.startingFoodReserve, 0, FoodCapacity);
        consecutiveStarvationTurns = 0; currentMovePoints = Mathf.Max(0, data.movementPoints);
        owner?.RegisterBand(this);
        PositionVisual(); RefreshVisual();
        GetComponentInChildren<BandWorldUI>(true)?.Initialize(this);
        if (spawnStartingGarrison) SpawnStartingGarrison(startingGarrisonOverride);
        BandCreated?.Invoke(this);
        RefreshOwnerVision(owner);
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
        PositionVisual(); RefreshVisual();
        NotifyChanged();
    }

    public void ResetForNewTurn()
    {
        currentMovePoints = Mathf.Max(0, data.movementPoints + builtStructures.Where(x => x != null).Sum(x => x.movementBonus));
        var yields = GetCurrentYields();
        foodReserve = Mathf.Clamp(foodReserve + Mathf.Max(0, yields.food), 0, FoodCapacity);
        ProcessFoodUpkeep();
        if (this != null && state == BandState.Encamped) ProcessProduction(GetProductionYield());
        if (this != null) NotifyChanged();
    }

    public BandYieldSet GetCurrentYields()
    {
        BandYieldSet result = state == BandState.Encamped ? data.encampedYields : data.packedYields;
        foreach (var structure in builtStructures.Where(x => x != null))
        {
            if (state == BandState.Packed && !structure.activeWhilePacked) continue;
            float multiplier = state == BandState.Packed ? Mathf.Clamp01(structure.packedEffectMultiplier) : 1f;
            result.food += Mathf.RoundToInt(structure.yields.food * multiplier);
            result.production += Mathf.RoundToInt(structure.yields.production * multiplier);
            result.gold += Mathf.RoundToInt(structure.yields.gold * multiplier);
            result.science += Mathf.RoundToInt(structure.yields.science * multiplier);
            result.culture += Mathf.RoundToInt(structure.yields.culture * multiplier);
            result.faith += Mathf.RoundToInt(structure.yields.faith * multiplier);
            result.policyPoints += Mathf.RoundToInt(structure.yields.policyPoints * multiplier);
        }
        return result;
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
        currentMovePoints -= data.packMovementCost; state = BandState.Packed; RefreshVisual(); BandPacked?.Invoke(this); NotifyChanged(); return true;
    }

    public bool Encamp()
    {
        if (state == BandState.Encamped || currentMovePoints < data.encampMovementCost) return false;
        currentMovePoints -= data.encampMovementCost; state = BandState.Encamped; RefreshVisual(); BandEncamped?.Invoke(this); NotifyChanged(); return true;
    }

    public bool TryMove(int tileIndex, int cost = 1)
    {
        if (state != BandState.Packed || tileIndex < 0 || cost < 0 || currentMovePoints < cost) return false;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var tile = ts != null ? ts.GetTileData(tileIndex) : null;
        if (tile == null || !tile.isPassable) return false;
        if (currentTileIndex < 0 || ts.GetWrappedHexDistance(currentTileIndex, tileIndex) != 1) return false;
        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        if (occ != null && occ.GetOccupantObject(tileIndex, TileLayer.Surface) != null) return false;
        Vector3 visualStartWorldPosition = GetPackedVisualWorldPosition();
        if (occ != null && currentTileIndex >= 0) occ.ClearOccupantById(currentTileIndex, TileLayer.Surface, gameObject.GetRuntimeId());
        currentTileIndex = tileIndex; currentMovePoints -= cost; PositionVisual();
        BeginPackedVisualMovement(visualStartWorldPosition);
        occ?.SetOccupant(tileIndex, gameObject, TileLayer.Surface); BandMoved?.Invoke(this); RefreshOwnerVision(owner); NotifyChanged(); return true;
    }

    public int Forage(int amount = -1)
    {
        if (currentMovePoints < data.forageMovementCost) return 0;
        int gathered = amount >= 0 ? amount : data.baseForageFood + builtStructures.Where(x => x != null).Sum(x => x.forageBonus);
        int accepted = Mathf.Clamp(gathered, 0, Mathf.Max(0, FoodCapacity - foodReserve));
        foodReserve += accepted; currentMovePoints -= data.forageMovementCost; NotifyChanged(); return accepted;
    }

    public bool QueueStructure(BandStructureData structure)
    {
        if (!CanQueueStructure(structure, out _)) return false;
        if (!ResourceCost.Consume(owner, structure.resourceCosts)) return false;
        if (structure.goldCost > 0) owner.gold -= structure.goldCost;
        queuedStructure = structure; queuedUnit = null; productionProgress = 0; NotifyChanged(); return true;
    }

    public bool CanQueueStructure(BandStructureData structure, out string reason)
    {
        reason = string.Empty;
        if (state != BandState.Encamped) { reason = "Encamp to build"; return false; }
        if (structure == null || data == null || !data.allowedStructures.Contains(structure)) { reason = "Not available to this Band"; return false; }
        if (builtStructures.Contains(structure)) { reason = "Completed"; return false; }
        if (queuedStructure == structure) { reason = "In progress"; return false; }
        if (queuedStructure != null || queuedUnit != null) { reason = "Production already active"; return false; }
        if (owner == null) { reason = "No owner"; return false; }
        if (structure.requiredTech != null && !owner.researchedTechs.Contains(structure.requiredTech)) { reason = "Requires technology"; return false; }
        if (structure.requiredCulture != null && !owner.researchedCultures.Contains(structure.requiredCulture)) { reason = "Requires culture"; return false; }
        if (owner.gold < structure.goldCost) { reason = $"Requires {structure.goldCost} Gold"; return false; }
        if (!ResourceCost.CanAfford(owner, structure.resourceCosts)) { reason = "Missing resources"; return false; }
        return true;
    }

    public bool QueueMilitaryUnit(CombatUnitData unit)
    {
        if (!CanQueueMilitaryUnit(unit, out _)) return false;
        if (!ResourceCost.Consume(owner, unit.requiredResourceCosts, unit.hasSubstituteResourceCosts)) return false;
        if (unit.goldCost > 0) owner.gold -= unit.goldCost;
        queuedUnit = unit; queuedStructure = null; productionProgress = 0; NotifyChanged(); return true;
    }

    public bool CanQueueMilitaryUnit(CombatUnitData unit, out string reason)
    {
        reason = string.Empty;
        if (state != BandState.Encamped) { reason = "Encamp to recruit"; return false; }
        if (unit == null || data == null || !unit.buildableByBand || !data.allowedMilitaryRecruitment.Contains(unit)) { reason = "Not recruitable by this Band"; return false; }
        if (owner == null || !unit.IsBuildableFor(owner)) { reason = "Requirements not met"; return false; }
        if (owner.gold < unit.goldCost) { reason = $"Requires {unit.goldCost} Gold"; return false; }
        if (!ResourceCost.CanAfford(owner, unit.requiredResourceCosts, unit.hasSubstituteResourceCosts))
        { reason = "Missing strategic resources"; return false; }
        if (garrison.Count >= GarrisonCapacity) { reason = "Garrison full"; return false; }
        if (queuedUnit == unit) { reason = "In progress"; return false; }
        if (queuedStructure != null || queuedUnit != null) { reason = "Production already active"; return false; }
        return true;
    }

    public void ProcessProduction(int production)
    {
        if (state != BandState.Encamped || production <= 0) return;
        productionProgress += production;
        if (queuedStructure != null && productionProgress >= queuedStructure.productionCost)
        {
            var completed = queuedStructure; builtStructures.Add(completed); queuedStructure = null; productionProgress = 0;
            RefreshStructureVisuals(); BandStructureCompleted?.Invoke(this, completed);
        }
        else if (queuedUnit != null && productionProgress >= Mathf.Max(1, queuedUnit.bandProductionCost))
        {
            var completed = queuedUnit; queuedUnit = null; productionProgress = 0; SpawnAndGarrison(completed);
        }
        NotifyChanged();
    }

    public bool TryAddToGarrison(CombatUnit unit)
    {
        if (unit == null || unit.owner != owner || unit.planetIndex != planetIndex || garrison.Contains(unit) || garrison.Count >= GarrisonCapacity) return false;
        if (unit.currentTileIndex >= 0 && unit.currentTileIndex != currentTileIndex) return false;
        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        if (unit.currentTileIndex >= 0) occ?.ClearOccupantById(unit.currentTileIndex, unit.currentLayer, unit.gameObject.GetRuntimeId());
        garrison.Add(unit); unit.StoreInBand(this); BandGarrisonChanged?.Invoke(this); NotifyChanged(); return true;
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
        CampaignArmyService.RefreshPresentation(representative); BandGarrisonChanged?.Invoke(this); NotifyChanged(); return true;
    }

    public void ReleaseSurvivingGarrisonAsArmy()
    {
        FormArmy(garrison.Where(x => x != null && x.currentHealth > 0).ToList(), out _);
    }

    public bool TryGarrisonArmy(CombatUnit army, out string reason)
    {
        reason = string.Empty;
        if (army == null) { reason = "Missing army."; return false; }
        var members = CampaignArmyService.GetMembers(army);
        if (owner == null || army.owner != owner) { reason = "Band and army must have the same owner."; return false; }
        if (army.planetIndex != planetIndex || army.currentLayer != TileLayer.Surface || army.currentTileIndex != currentTileIndex)
        { reason = "Army and Band must share a compatible campaign location."; return false; }
        if (members.Count == 0 || garrison.Count + members.Count > GarrisonCapacity)
        { reason = $"Not enough garrison capacity ({garrison.Count + members.Count}/{GarrisonCapacity})."; return false; }
        if (members.Any(x => x == null || x.owner != owner || x.planetIndex != planetIndex || x.currentLayer != TileLayer.Surface ||
            x.currentTileIndex != currentTileIndex || x.IsTransported || x.isStored || x.IsBandGarrisoned))
        { reason = "One or more army members cannot be garrisoned."; return false; }

        foreach (var member in members)
        {
            var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
            occ?.ClearOccupantById(currentTileIndex, member.currentLayer, member.gameObject.GetRuntimeId());
            garrison.Add(member);
            member.StoreInBand(this);
        }
        BandGarrisonChanged?.Invoke(this);
        NotifyChanged();
        return true;
    }

    public bool CanFoundSettlement(out string reason)
    {
        reason = string.Empty;
        if (data == null || !data.canFoundSettlement) { reason = "This Band cannot found a settlement."; return false; }
        if (state != BandState.Encamped) { reason = "Encamp before founding a settlement."; return false; }
        if (owner == null) { reason = "This Band has no owner."; return false; }
        if (!owner.CanFoundMoreCities()) { reason = $"City capacity reached ({owner.cities.Count}/{owner.CurrentCityCap})."; return false; }
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var tile = ts != null ? ts.GetTileData(currentTileIndex) : null;
        if (tile == null || !tile.isLand) { reason = "A settlement requires a valid land tile."; return false; }
        const int minimumCityDistance = 4;
        var civilizations = CivilizationManager.Instance != null ? CivilizationManager.Instance.GetAllCivs() : null;
        if (civilizations != null)
            foreach (var civilization in civilizations)
                foreach (var city in civilization.cities)
                    if (city != null && city.planetIndex == planetIndex && ts.GetWrappedHexDistance(currentTileIndex, city.centerTileIndex) < minimumCityDistance)
                    { reason = "Too close to another settlement."; return false; }
        return true;
    }

    public City FoundSettlement(out string reason)
    {
        if (!CanFoundSettlement(out reason)) return null;
        int oldCount = owner.cities.Count;
        owner.FoundNewCity(currentTileIndex, null, GameManager.Instance?.GetPlanetGenerator(planetIndex), GameManager.PlanetLayerType.Surface);
        if (owner.cities.Count <= oldCount) { reason = "Settlement creation failed."; return null; }
        City city = owner.cities[owner.cities.Count - 1];
        city.level = Mathf.Max(1, population / Mathf.Max(1, data.cityPopulationDivisor));
        city.UpdateLabel();
        ReleaseSurvivingGarrisonAsArmy();
        DestroyBand(BandLossReason.ConvertedToSettlement);
        return city;
    }

    public void Capture(Civilization newOwner)
    {
        if (newOwner == null || newOwner == owner) return;
        var old = owner; old?.UnregisterBand(this); owner = newOwner; newOwner.RegisterBand(this); RefreshVisual();
        RefreshOwnerVision(old); RefreshOwnerVision(newOwner);
        BandCaptured?.Invoke(this, old, newOwner);
        NotifyChanged();
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
        RefreshOwnerVision(owner); BandDestroyed?.Invoke(this, reason); Destroy(gameObject);
    }

    private void NotifyChanged()
    {
        BandChanged?.Invoke(this);
        GetComponentInChildren<BandWorldUI>(true)?.Refresh();
    }

    private int GetProductionYield() => Mathf.Max(0, data.encampedYields.production + builtStructures.Where(x => x != null).Sum(x => x.yields.production));
    private void SpawnStartingGarrison(IEnumerable<StartingBandGarrisonEntry> startingGarrisonOverride)
    {
        var entries = startingGarrisonOverride != null ? startingGarrisonOverride.ToList() : data.startingGarrison;
        foreach (var entry in entries)
        {
            if (entry == null || entry.unit == null || entry.count <= 0) continue;
            for (int i = 0; i < entry.count; i++)
            {
                if (SpawnAndGarrison(entry.unit)) continue;
                Debug.LogWarning($"[Band] Could not add starting {entry.unit.unitName} to {data.displayName}; check its prefab and garrison capacity ({Garrison.Count}/{GarrisonCapacity}).");
                break;
            }
        }
    }
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
    private static void RefreshOwnerVision(Civilization civilization)
    {
        if (civilization != null && UnitVisionManager.Instance != null)
            UnitVisionManager.Instance.UpdateVisionForCiv(UnitVisionManager.GetCivIndex(civilization));
    }
    /// <summary>Rebuilds presentation from Band data. Runtime visuals are never persistent state.</summary>
    public void RefreshVisual()
    {
        ClearVisuals();
        GameObject visualPrefab = ResolveStateVisualPrefab();
        if (visualPrefab == null) return;
        stateVisual = Instantiate(visualPrefab, visualRoot != null ? visualRoot : transform, false);
        if (state == BandState.Packed) InitializePackedAnimators();
        else RefreshStructureVisuals();
    }

    private void InitializePackedAnimators()
    {
        packedAnimators = stateVisual != null
            ? stateVisual.GetComponentsInChildren<Animator>(true)
            : Array.Empty<Animator>();

        float minimumSpeed = Mathf.Min(packedAnimatorSpeedRange.x, packedAnimatorSpeedRange.y);
        float maximumSpeed = Mathf.Max(packedAnimatorSpeedRange.x, packedAnimatorSpeedRange.y);
        foreach (var animator in packedAnimators)
        {
            if (animator == null) continue;

            // Packed actor clips must be in-place; the Band presentation root supplies all travel.
            animator.applyRootMotion = false;
            animator.speed = UnityEngine.Random.Range(minimumSpeed, maximumSpeed);
            SetAnimatorIntegerIfPresent(animator, packedIdleVariantParameter,
                UnityEngine.Random.Range(0, Mathf.Max(1, packedIdleVariantCount)));
            SetAnimatorIntegerIfPresent(animator, packedWalkVariantParameter,
                UnityEngine.Random.Range(0, Mathf.Max(1, packedWalkVariantCount)));
            SetAnimatorBoolIfPresent(animator, packedMovingParameter, false);

            if (!animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) continue;
            animator.Update(0f);
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            if (stateInfo.loop) animator.Play(stateInfo.fullPathHash, 0, UnityEngine.Random.value);
        }
    }

    private void SetPackedAnimatorsMoving(bool moving)
    {
        foreach (var animator in packedAnimators)
            SetAnimatorBoolIfPresent(animator, packedMovingParameter, moving);
    }

    private static void SetAnimatorBoolIfPresent(Animator animator, string parameterName, bool value)
    {
        if (animator == null || string.IsNullOrEmpty(parameterName)) return;
        int hash = Animator.StringToHash(parameterName);
        foreach (var parameter in animator.parameters)
            if (parameter.nameHash == hash && parameter.type == AnimatorControllerParameterType.Bool)
            {
                animator.SetBool(hash, value);
                return;
            }
    }

    private static void SetAnimatorIntegerIfPresent(Animator animator, string parameterName, int value)
    {
        if (animator == null || string.IsNullOrEmpty(parameterName)) return;
        int hash = Animator.StringToHash(parameterName);
        foreach (var parameter in animator.parameters)
            if (parameter.nameHash == hash && parameter.type == AnimatorControllerParameterType.Int)
            {
                animator.SetInteger(hash, value);
                return;
            }
    }

    private Vector3 GetPackedVisualWorldPosition()
    {
        Transform movementRoot = GetPackedVisualMovementRoot();
        return movementRoot != null ? movementRoot.position : transform.position;
    }

    private Transform GetPackedVisualMovementRoot()
    {
        if (visualRoot != null && visualRoot != transform) return visualRoot;
        return stateVisual != null ? stateVisual.transform : null;
    }

    private void BeginPackedVisualMovement(Vector3 startWorldPosition)
    {
        Transform movementRoot = GetPackedVisualMovementRoot();
        if (movementRoot == null) return;

        Vector3 restingLocalPosition = visualMoveRoutine != null && visualMovementRoot == movementRoot
            ? visualMovementRestingLocalPosition
            : movementRoot.localPosition;
        if (visualMoveRoutine != null) StopCoroutine(visualMoveRoutine);

        visualMovementRoot = movementRoot;
        visualMovementRestingLocalPosition = restingLocalPosition;
        movementRoot.position = startWorldPosition;
        SetPackedAnimatorsMoving(true);
        visualMoveRoutine = StartCoroutine(MovePackedVisualToRest(movementRoot, restingLocalPosition));
    }

    private IEnumerator MovePackedVisualToRest(Transform movementRoot, Vector3 restingLocalPosition)
    {
        Vector3 startLocalPosition = movementRoot.localPosition;
        float duration = Mathf.Max(0.01f, visualMoveDuration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (movementRoot == null)
            {
                visualMoveRoutine = null;
                visualMovementRoot = null;
                yield break;
            }

            elapsed += Time.deltaTime;
            movementRoot.localPosition = Vector3.Lerp(startLocalPosition, restingLocalPosition, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        movementRoot.localPosition = restingLocalPosition;
        SetPackedAnimatorsMoving(false);
        visualMoveRoutine = null;
        visualMovementRoot = null;
    }

    /// <summary>Reconstructs encamped attachments exclusively from builtStructures.</summary>
    public void RefreshStructureVisuals()
    {
        foreach (var visual in structureVisuals) if (visual != null) Destroy(visual); structureVisuals.Clear();
        if (state != BandState.Encamped || stateVisual == null) return;

        var camp = stateVisual.GetComponent<BandCampVisual>();
        var civData = owner != null ? owner.civData : null;
        if (camp == null && builtStructures.Any(x => x != null && x.GetVisualAttachmentPrefab(civData) != null))
        {
            Debug.LogWarning($"[Band] Encamped visual '{stateVisual.name}' has no BandCampVisual sockets; structure visuals were skipped.", stateVisual);
            return;
        }

        var occupied = new HashSet<Transform>();
        foreach (var structure in builtStructures.Where(x => x != null))
        {
            GameObject prefab = structure.GetVisualAttachmentPrefab(civData);
            if (prefab == null)
            {
                Debug.LogWarning($"[Band] Structure '{structure.structureName}' has no visual attachment prefab; gameplay is unaffected.", this);
                continue;
            }
            if (camp == null || !camp.TryGetSocket(structure.visualSlot, occupied, out var anchor))
            {
                Debug.LogWarning($"[Band] Camp '{stateVisual.name}' has no available {structure.visualSlot} socket for '{structure.structureName}'; gameplay is unaffected.", this);
                continue;
            }

            var attachment = Instantiate(prefab, anchor, false);
            attachment.transform.localPosition = Vector3.zero;
            attachment.transform.localRotation = Quaternion.identity;
            occupied.Add(anchor);
            structureVisuals.Add(attachment);
        }
    }

    public void ClearVisuals()
    {
        if (visualMoveRoutine != null)
        {
            StopCoroutine(visualMoveRoutine);
            visualMoveRoutine = null;
        }
        if (visualMovementRoot != null)
            visualMovementRoot.localPosition = visualMovementRestingLocalPosition;
        visualMovementRoot = null;
        SetPackedAnimatorsMoving(false);
        packedAnimators = Array.Empty<Animator>();

        foreach (var visual in structureVisuals) if (visual != null) Destroy(visual);
        structureVisuals.Clear();
        if (stateVisual != null) Destroy(stateVisual);
        stateVisual = null;
    }

    private GameObject ResolveStateVisualPrefab()
    {
        if (data == null) return null;
        GameObject fallback = state == BandState.Packed ? data.packedVisual : data.encampedVisual;
        var visualOverride = data.civilizationVisualOverrides?.FirstOrDefault(x =>
            x != null && owner != null && x.civilization == owner.civData);
        if (visualOverride == null) return fallback;
        GameObject overridden = state == BandState.Packed ? visualOverride.packedVisual : visualOverride.encampedVisual;
        return overridden != null ? overridden : fallback;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (visualRoot == null)
            Debug.LogWarning($"[Band] '{name}' has no visualRoot assigned; visuals will use the gameplay transform for backward compatibility.", this);
    }
#endif
}
