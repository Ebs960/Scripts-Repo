using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using GameCombat;

/// <summary>
/// WorkerUnit implementation.
/// Handles work points, building, foraging, and city founding.
/// Inherits shared functionality from BaseUnit.
/// </summary>
public class WorkerUnit : BaseUnit
{
    [Header("Worker Progression")]
    public int level = 1;

    [field: SerializeField] public WorkerUnitData data { get; private set; }

    [Header("Worker Points")]
    public int currentWorkPoints { get; private set; }
    // Compatibility: use `CurrentAttackPoints` on BaseUnit instead of a separate member

    [Header("Worker State")]
    [SerializeField] private string persistentId;
    public string PersistentId
    {
        get
        {
            if (string.IsNullOrEmpty(persistentId))
                persistentId = System.Guid.NewGuid().ToString();
            return persistentId;
        }
        private set => persistentId = value;
    }

    [Header("Animation Control")]
    private readonly int idleYoungHash = Animator.StringToHash("IdleYoung");
    private readonly int idleExperiencedHash = Animator.StringToHash("IdleExperienced");
    private readonly int foundCityHash = Animator.StringToHash("FoundCity");
    private readonly int forageHash = Animator.StringToHash("Forage");
    private readonly int buildBoolHash = Animator.StringToHash("IsBuilding");
    

    #region Implement Abstract Members from BaseUnit

    public override string UnitName => data?.unitName ?? "Worker";
    public override int BaseAttack => data?.baseAttack ?? 0;
    public override int BaseDefense => data?.baseDefense ?? 0;
    public override int BaseHealth => data?.baseHealth ?? 0;
    public override float BaseRange => 1f;

    public override int MaxHealth
    {
        get
        {
            var wb = AggregateWorkerBonusesLocal(owner, data);
            float maxHPF = (BaseHealth + wb.healthAdd) * (1f + wb.healthPct);
            return Mathf.RoundToInt(maxHPF);
        }
    }

    protected override EquipmentTarget AcceptedEquipmentTarget => EquipmentTarget.WorkerUnit;
    // MeleeEngageDuration removed — engagement duration deprecated.

    public override void ResetForNewTurn()
    {
        // Perform base-class per-turn resets (move points, AP, weather penalties)
        RestoreMovePointsForNewTurn();
        ResetAttackPointsForNewTurn();

        // Worker-specific resets
        var wb = AggregateWorkerBonusesLocal(owner, data);
        currentWorkPoints = Mathf.RoundToInt((data.baseWorkPoints + wb.workAdd) * (1f + wb.workPct));

        // If trapped, decrement trapped duration (was in previous worker logic)
        if (IsTrapped)
        {
            var prop = typeof(BaseUnit).GetField("trappedTurnsRemaining", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (prop != null) prop.SetValue(this, Mathf.Max(0, (int)prop.GetValue(this) - 1));
        }

        CheckForHazardousBiomeDamage();

        // Auto-contribute to jobs at start of turn
        AutoContributeToJobs();
    }

    /// <summary>
    /// Get the starting movement points a worker would have at the beginning of a turn,
    /// including civ/tech/equipment bonuses and winter penalty. This does not modify state.
    /// </summary>
    public new int GetStartingMovePoints()
    {
        var wb = AggregateWorkerBonusesLocal(owner, data);
        int baseMove = Mathf.RoundToInt((data.baseMovePoints + wb.moveAdd) * (1f + wb.movePct));
        // Winter/trapped penalties are handled centrally in BaseUnit.RestoreMovePointsForNewTurn()
        return baseMove;
    }

    /// <summary>
    /// Deduct movement points after moving. Uses BaseUnit implementation.
    /// Kept for compatibility but delegates to the base implementation.
    /// </summary>
    public new void DeductMovePoints(int amount)
    {
        base.DeductMovePoints(amount);
    }

    /// <summary>
    /// Auto-contribute to jobs if assigned (called separately from ResetForNewTurn for flexibility)
    /// </summary>
    private void AutoContributeToJobs()
    {
        // Auto-contribute to jobs
        if (currentWorkPoints > 0 && ImprovementManager.Instance != null)
        {
            if (ImprovementManager.Instance.JobAssignedToWorker(currentTileIndex, this, planetIndex))
            {
                var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
                var tileData = ts != null ? ts.GetTileData(currentTileIndex) : null;
                if (tileData != null && tileData.improvement != null)
                    ContributeWork();
                else
                {
                    ContributeWorkToUnit();
                    ContributeWorkToWorker();
                }
            }
        }
    }

    #endregion

    #region Unity Lifecycle

    protected override void Awake()
    {
        base.Awake();
        UnitRegistry.RegisterPersistent(PersistentId, gameObject);

        // Subscribe to movement completed and worker assignment events for animation updates
        if (GameEventManager.Instance != null)
        {
            GameEventManager.Instance.OnMovementCompleted += HandleMovementCompleted;
            GameEventManager.Instance.OnWorkerAssignedToJob += HandleWorkerAssignedEvent;
            GameEventManager.Instance.OnWorkerUnassignedFromJob += HandleWorkerUnassignedEvent;
        }
        else
        {
            // GameEventManager may not exist yet at Awake; defer subscription
            StartCoroutine(DeferredSubscribeToMovementEvent());
        }

        // Auto-equip defaults
        if (data != null)
        {
            if (equippedWeapon == null && data.defaultWeapon != null) EquipItem(data.defaultWeapon);
            if (equippedShield == null && data.defaultShield != null) EquipItem(data.defaultShield);
            if (equippedArmor == null && data.defaultArmor != null) EquipItem(data.defaultArmor);
            if (equippedMiscellaneous == null && data.defaultMiscellaneous != null) EquipItem(data.defaultMiscellaneous);
            if (equippedProjectileWeapon == null && data.defaultProjectileWeapon != null) EquipItem(data.defaultProjectileWeapon);
        }
    }

    private System.Collections.IEnumerator DeferredSubscribeToMovementEvent()
    {
        while (GameEventManager.Instance == null)
            yield return null;
        GameEventManager.Instance.OnMovementCompleted += HandleMovementCompleted;
        GameEventManager.Instance.OnWorkerAssignedToJob += HandleWorkerAssignedEvent;
        GameEventManager.Instance.OnWorkerUnassignedFromJob += HandleWorkerUnassignedEvent;
    }

    protected override void OnDestroy()
    {
        // Unsubscribe from movement event
        if (GameEventManager.Instance != null)
        {
            GameEventManager.Instance.OnMovementCompleted -= HandleMovementCompleted;
            GameEventManager.Instance.OnWorkerAssignedToJob -= HandleWorkerAssignedEvent;
            GameEventManager.Instance.OnWorkerUnassignedFromJob -= HandleWorkerUnassignedEvent;
        }
        base.OnDestroy();
    }

    #endregion

    #region Worker Logic (Building, Foraging, Cities)

    public void Initialize(WorkerUnitData unitData, Civilization unitOwner, int startTileIndex)
    {
        // Internal setup
        data = unitData;
        
        var ownerProp = typeof(BaseUnit).GetProperty("owner");
        ownerProp.SetValue(this, unitOwner);

        level = 1;
        currentTileIndex = startTileIndex;

        var wb = AggregateWorkerBonusesLocal(unitOwner, unitData);
        currentHealth = MaxHealth;
        currentWorkPoints = Mathf.RoundToInt((unitData.baseWorkPoints + wb.workAdd) * (1f + wb.workPct));
        int oldMP = currentMovePoints;
        currentMovePoints = Mathf.RoundToInt((unitData.baseMovePoints + wb.moveAdd) * (1f + wb.movePct));
        try { GameEventManager.Instance?.RaiseMovePointsChanged(this, oldMP, currentMovePoints); } catch { }
        takesWeatherDamage = unitData.takesWeatherDamage;

        // Configure attack points from data asset
        try { attackPointsPerTurn = unitData.attackPointsPerTurn; ResetAttackPointsForNewTurn(); } catch { }

        // Position the unit on the tile
        PositionUnitOnSurface(startTileIndex);

        if (animator != null) animator.SetBool(idleYoungHash, true);

        InitializeUnitLabel();
    }

    /// <summary>
    /// Restore saved runtime state after Initialize has been called.
    /// Used by the save/load system.
    /// </summary>
    public void RestoreState(int savedHealth, int savedWorkPoints, int savedMovePoints, TileLayer savedLayer)
    {
        currentHealth = Mathf.Clamp(savedHealth, 0, MaxHealth);
        currentWorkPoints = savedWorkPoints;
        int oldMP = currentMovePoints;
        currentMovePoints = savedMovePoints;
        try { GameEventManager.Instance?.RaiseMovePointsChanged(this, oldMP, currentMovePoints); } catch { }
        currentLayer = savedLayer;

        // If unit is in orbit, reposition at current orbit height (not stale saved Y)
        if (savedLayer == TileLayer.Orbit)
        {
            var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
            if (ts != null && currentTileIndex >= 0)
            {
                Vector3 surface = ts.GetTileSurfacePosition(currentTileIndex);
                transform.position = surface + Vector3.up * PlanetGenerator.GetOrbitHeight(planetIndex);
            }
            else
            {
                Vector3 pos = transform.position;
                pos.y = PlanetGenerator.GetOrbitHeight(planetIndex);
                transform.position = pos;
            }
        }
    }

    public void ContributeWork()
    {
        if (currentWorkPoints <= 0) return;
        if (ImprovementManager.Instance == null || !ImprovementManager.Instance.HasBuildJobAtTile(currentTileIndex, planetIndex)) return;
        ImprovementManager.Instance.AddWork(currentTileIndex, currentWorkPoints, planetIndex);
        currentWorkPoints = 0;
    }

    public void ContributeWorkToUnit()
    {
        if (currentWorkPoints <= 0) return;
        if (ImprovementManager.Instance == null || !ImprovementManager.Instance.HasUnitJobAtTile(currentTileIndex, planetIndex)) return;
        ImprovementManager.Instance.AddUnitWork(currentTileIndex, currentWorkPoints, planetIndex);
        currentWorkPoints = 0;
    }

    public void ContributeWorkToWorker()
    {
        if (currentWorkPoints <= 0) return;
        if (ImprovementManager.Instance == null || !ImprovementManager.Instance.HasWorkerJobAtTile(currentTileIndex, planetIndex)) return;
        ImprovementManager.Instance.AddWorkerWork(currentTileIndex, currentWorkPoints, planetIndex);
        currentWorkPoints = 0;
    }

    

    private void HandleWorkerAssignedEvent(GameEventManager.WorkerAssignmentEventArgs args)
    {
        if (args == null || args.Worker == null) return;
        if (args.Worker.GetInstanceID() != gameObject.GetInstanceID()) return;
            if (animator != null)
            {
                animator.SetBool(buildBoolHash, true);
            }
    }

    private void HandleWorkerUnassignedEvent(GameEventManager.WorkerAssignmentEventArgs args)
    {
        if (args == null || args.Worker == null) return;
        if (args.Worker.GetInstanceID() != gameObject.GetInstanceID()) return;
            if (animator != null)
            {
                animator.SetBool(buildBoolHash, false);
            }
    }

    public void FoundCity()
    {
        if (!CanFoundCityOnCurrentTile()) return;
        if (animator != null) animator.SetTrigger(foundCityHash);
        owner?.FoundNewCity(currentTileIndex, grid, planet);
        Die();
    }

    public bool CanFoundCityOnCurrentTile()
    {
        if (data == null || !data.canFoundCity || owner == null) return false;
        if (!owner.CanFoundMoreCities()) return false;

        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var td = ts != null ? ts.GetTileData(currentTileIndex) : null;
        if (td == null || !td.isLand) return false;

        // Distance check: use tile-step distance (hex steps) rather than world-space distance
        const int minCitySteps = 4; // must be at least this many tile steps away
        var allCivs = CivilizationManager.Instance.GetAllCivs();
        foreach (var civ in allCivs)
        {
            foreach (var city in civ.cities)
            {
                if (city == null) continue;
                if (ts != null && ts.IsReady())
                {
                    int steps = ts.GetWrappedHexDistance(currentTileIndex, city.centerTileIndex);
                    if (steps < minCitySteps) return false;
                }
                else
                {
                    // Fallback for legacy/runtime edgecases: approximate using flat center distance
                    Vector3 a = ts != null ? ts.GetTileCenterFlat(currentTileIndex) : Vector3.zero;
                    Vector3 b = ts != null ? ts.GetTileCenterFlat(city.centerTileIndex) : Vector3.zero;
                    float d = Vector3.Distance(a, b);
                    if (d < minCitySteps) return false;
                }
            }
        }
        return true;
    }

    #endregion

    #region Stats and Bonuses

    private struct WorkerAgg { public float workAdd, moveAdd, healthAdd; public float workPct, movePct, healthPct; }
    
    private WorkerAgg AggregateWorkerBonusesLocal(Civilization civ, WorkerUnitData wu)
    {
        WorkerAgg a = new WorkerAgg();
        if (civ == null || wu == null) return a;

        if (civ.researchedTechs != null)
        {
            foreach (var t in civ.researchedTechs)
            {
                if (t?.workerBonuses == null) continue;
                foreach (var b in t.workerBonuses)
                {
                    if (b != null && b.worker == wu)
                    {
                        a.workAdd += b.workPointsAdd; a.moveAdd += b.movePointsAdd; a.healthAdd += b.healthAdd;
                        a.workPct += b.workPointsPct; a.movePct += b.movePointsPct; a.healthPct += b.healthPct;
                    }
                }
            }
        }

        // Equipment bonuses
        EquipmentData[] eqs = { _equippedWeapon, _equippedShield, _equippedArmor, _equippedMiscellaneous };
        foreach (var eq in eqs)
        {
            if (eq == null || eq.targetUnit == EquipmentTarget.CombatUnit) continue;
            a.workAdd += eq.workPointsBonus;
            a.moveAdd += eq.movementBonus;
            a.healthAdd += eq.healthBonus;
        }

        return a;
    }

    public override int CurrentDefense
    {
        get
        {
            return Mathf.RoundToInt(GetCurrentDefenseValueFloat());
        }
    }

    #endregion

    #region Overrides for BaseUnit functionality

    public override bool ApplyDamage(int amount)
    {
        // Worker-specific hit logic if needed, otherwise base
        return base.ApplyDamage(amount);
    }

    protected override void Die()
    {
        if (owner != null) owner.workerUnits.Remove(this);
        ImprovementManager.Instance?.UnassignWorkerFromAllJobs(this);
        base.Die();
    }

    // CanMoveTo is fully consolidated in BaseUnit — no override needed.

    /// <summary>
    /// Called when civilization bonuses change (tech/culture research).
    /// Intentionally does not refill work/move points mid-turn.
    /// </summary>
    public void OnCivBonusesChanged()
    {
        int max = MaxHealth;
        currentHealth = Mathf.Min(currentHealth, max);
    }

    /// <summary>
    /// Workers can attack any unit (combat units, other workers, animals) — weakly.
    /// </summary>
    public bool CanAttack(BaseUnit target)
    {
        if (target == null) return false;
        // Use tile-step distance for attack checks to match movement metric
        try
        {
            var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
            if (ts != null && currentTileIndex >= 0 && target.currentTileIndex >= 0)
            {
                int tileSteps = ts.GetWrappedHexDistance(currentTileIndex, target.currentTileIndex);
                if (tileSteps >= 0)
                {
                    int maxSteps = Mathf.FloorToInt(BaseRange);
                    return tileSteps <= maxSteps;
                }
            }
        }
        catch (System.Exception) { }
        // If tile-based check can't be performed, do not allow attack
        return false;
    }

    public override void Attack(BaseUnit target)
    {
        if (!CanAttack(target)) return;

        // Attack visuals are handled centrally by BaseUnit.PerformAttack (no local trigger)

        int damage = Mathf.Max(1, CurrentAttack);
        var ctx = new BaseUnit.AttackContext { attacker = this, defender = target, weapon = null, damage = damage, isMelee = true, isRanged = false };
        bool died = PerformAttack(ctx);

        if (died)
        {
            // Post-hit handling centralized in BaseUnit.ApplyDamage(attacker...)
        }
    }

    /// <summary>
    /// Check if worker can forage the specified resource instance on the specified tile.
    /// This matches the UI call sites (UnitInfoPanel).
    /// </summary>
    public bool CanForage(ResourceData resource, int tileIndex)
    {
        if (resource == null) { Debug.Log("[WorkerUnit] CanForage=false: resource==null"); return false; }
        if (currentWorkPoints <= 0) { Debug.Log($"[WorkerUnit] CanForage=false: no work points ({currentWorkPoints}) tile={tileIndex}"); return false; }
        if (tileIndex != currentTileIndex) { Debug.Log($"[WorkerUnit] CanForage=false: tile mismatch current={currentTileIndex} requested={tileIndex}"); return false; }
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var td = ts != null ? ts.GetTileData(tileIndex) : null;
        if (td == null) { Debug.Log($"[WorkerUnit] CanForage=false: tile data null tile={tileIndex}"); return false; }
        if (!td.isLand) { Debug.Log($"[WorkerUnit] CanForage=false: tile not land tile={tileIndex}"); return false; }
        if (!resource.canBeForaged) { Debug.Log($"[WorkerUnit] CanForage=false: resource not foragable {resource.resourceName}"); return false; }
        if (data != null && !data.canForage) { Debug.Log($"[WorkerUnit] CanForage=false: worker data.canForage=false"); return false; }
        return true;
    }

    public void Forage(ResourceData resource, int tileIndex)
    {
        if (!CanForage(resource, tileIndex)) return;
        // Worker-side bookkeeping only; ResourceManager handles the actual resource consumption.
        currentWorkPoints = 0;
    }

    public bool CanBuildUnit(CombatUnitData unitData, int tileIndex)
    {
        if (unitData == null) return false;
        if (owner == null) return false;
        if (currentWorkPoints <= 0) return false;
        if (tileIndex != currentTileIndex) return false;
        if (!unitData.buildableByWorker) return false;
        if (!unitData.AreRequirementsMet(owner)) return false;
        if (LimitManager.Instance != null && !LimitManager.Instance.CanCreateCombatUnit(owner, unitData)) return false;
        return true;
    }

    public bool CanBuildWorker(WorkerUnitData workerData, int tileIndex)
    {
        if (workerData == null) return false;
        if (owner == null) return false;
        if (currentWorkPoints <= 0) return false;
        if (tileIndex != currentTileIndex) return false;
        if (!workerData.buildableByWorker) return false;
        if (!workerData.AreRequirementsMet(owner)) return false;
        if (LimitManager.Instance != null && !LimitManager.Instance.CanCreateWorkerUnit(owner, workerData)) return false;
        return true;
    }

    public void StartBuildingUnit(CombatUnitData unitData, int tileIndex)
    {
        if (!CanBuildUnit(unitData, tileIndex)) { Debug.Log($"[WorkerUnit] StartBuildingUnit failed: CanBuildUnit=false unit={unitData?.unitName} tile={tileIndex}"); return; }
        if (ImprovementManager.Instance == null) { Debug.Log($"[WorkerUnit] StartBuildingUnit failed: ImprovementManager missing"); return; }
        if (!ImprovementManager.Instance.HasUnitJobAtTile(tileIndex, planetIndex, unitData) &&
            !ImprovementManager.Instance.CreateUnitJob(unitData, tileIndex, owner, planetIndex))
        { Debug.Log($"[WorkerUnit] StartBuildingUnit failed: CreateUnitJob rejected unit={unitData?.unitName} tile={tileIndex}"); return; }

        ImprovementManager.Instance.AddUnitWork(tileIndex, currentWorkPoints, planetIndex);
        currentWorkPoints = 0;
    }

    public void StartBuildingWorker(WorkerUnitData workerData, int tileIndex)
    {
        if (!CanBuildWorker(workerData, tileIndex)) { Debug.Log($"[WorkerUnit] StartBuildingWorker failed: CanBuildWorker=false worker={workerData?.unitName} tile={tileIndex}"); return; }
        if (ImprovementManager.Instance == null) { Debug.Log($"[WorkerUnit] StartBuildingWorker failed: ImprovementManager missing"); return; }
        if (!ImprovementManager.Instance.HasWorkerJobAtTile(tileIndex, planetIndex, workerData) &&
            !ImprovementManager.Instance.CreateWorkerJob(workerData, tileIndex, owner, planetIndex))
        { Debug.Log($"[WorkerUnit] StartBuildingWorker failed: CreateWorkerJob rejected worker={workerData?.unitName} tile={tileIndex}"); return; }

        ImprovementManager.Instance.AddWorkerWork(tileIndex, currentWorkPoints, planetIndex);
        currentWorkPoints = 0;
    }

    public void StartBuilding(ImprovementData improvement, int tileIndex)
    {
        if (improvement == null) { Debug.Log("[WorkerUnit] StartBuilding failed: improvement==null"); return; }
        if (currentWorkPoints <= 0) { Debug.Log($"[WorkerUnit] StartBuilding failed: no work points (0) tile={tileIndex}"); return; }
        if (tileIndex != currentTileIndex) { Debug.Log($"[WorkerUnit] StartBuilding failed: tile mismatch current={currentTileIndex} requested={tileIndex}"); return; }
        if (ImprovementManager.Instance == null) { Debug.Log("[WorkerUnit] StartBuilding failed: ImprovementManager missing"); return; }
        if (!ImprovementManager.Instance.HasBuildJobAtTile(tileIndex, planetIndex, improvement) &&
            !ImprovementManager.Instance.CreateBuildJob(improvement, tileIndex, owner, planetIndex))
        { Debug.Log($"[WorkerUnit] StartBuilding failed: CreateBuildJob rejected improvement={improvement?.name} tile={tileIndex}"); return; }

        ImprovementManager.Instance.AssignWorkerToJob(tileIndex, this, planetIndex);
        ImprovementManager.Instance.AddWork(tileIndex, currentWorkPoints, planetIndex);
        currentWorkPoints = 0;
    }

    /// <summary>
    /// Build a herd-capable building using this worker's current work points.
    /// This consumes the worker's `currentWorkPoints` immediately and attaches the building to the nearest herd owned by the worker's civ.
    /// If no herd exists nearby, a new herd is created at the worker's tile and the building is attached.
    /// </summary>
    public void StartBuildingHerd(BuildingData building)
    {
        // Allow workers to found/create a herd prefab at their current tile (behaves like FoundCity but does not consume the worker).
        if (owner == null)
        {
            Debug.LogWarning("[WorkerUnit] StartBuildingHerd failed: owner civ null");
            return;
        }

        if (!owner.herdsEnabled)
        {
            Debug.LogWarning("[WorkerUnit] StartBuildingHerd failed: herding not enabled for civ");
            return;
        }

        var prefabToUse = owner.civData != null ? owner.civData.herdPrefab : null;
        if (prefabToUse == null)
        {
            Debug.LogWarning($"[WorkerUnit] Cannot create herd: civData.herdPrefab is not assigned for {owner.civData?.civName ?? owner.name}");
            return;
        }

        // Prevent creating a herd on a tile that already has any occupant (unit, city, or herd)
        try
        {
            var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
            if (occ != null)
            {
                var existing = TileOccupancyManager.GetOccupantObjectForTileWithFallback(currentTileIndex, TileLayer.Surface, planetIndex);
                if (existing != null)
                {
                    Debug.LogWarning($"[WorkerUnit] Cannot create herd: tile {currentTileIndex} is already occupied by '{existing.name}'.");
                    return;
                }
            }
        }
        catch { }

        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var spawnPos = (ts != null && currentTileIndex >= 0) ? ts.GetTileSurfacePosition(currentTileIndex) : Vector3.zero;

        // Instantiate inactive so we can assign ownership before OnEnable runs
        GameObject go = Instantiate(prefabToUse);
        go.transform.position = spawnPos;
        go.SetActive(false);

        var herd = go.GetComponent<Herd>() ?? go.AddComponent<Herd>();
        herd.owner = owner;
        herd.planetIndex = planetIndex;
        herd.currentTileIndex = currentTileIndex;

        if (building != null)
        {
            try { herd.BuildStructure(building); } catch { }
        }

        go.SetActive(true);
        // Register herd as tile occupant like cities/units
        try
        {
            var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
            if (occ != null)
            {
                occ.SetOccupant(currentTileIndex, go, TileLayer.Surface);
            }
        }
        catch { }

        Debug.Log($"[WorkerUnit] Created herd at tile {currentTileIndex} for civ {owner.civData?.civName ?? owner.name}");
    }

    #endregion

    #region Helper Methods

    private void CheckForHazardousBiomeDamage()
    {
        if (currentTileIndex < 0) return;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var td = ts != null ? ts.GetTileData(currentTileIndex) : null;
        if (td == null) return;

        if (BiomeHelper.IsDamagingBiome(td.biome))
        {
            float dmgPct = BiomeHelper.GetBiomeDamage(td.biome);
            int dmg = Mathf.CeilToInt(BaseHealth * dmgPct);
            ApplyDamage(dmg);
            
            if (owner != null && owner.isPlayerControlled)
                UIManager.Instance?.ShowNotification($"{UnitName} took {dmg} damage from {td.biome}!");
        }
    }

    public override void UpdateWalkingState(bool walking)
    {
        base.UpdateWalkingState(walking);
        // IdleYoung is a Bool parameter in the animator — sync it with idle state
        if (animator != null)
        {
            animator.SetBool(idleYoungHash, !walking);
            if (!walking)
            {
                var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                Debug.Log($"[WorkerUnit] {gameObject.name} set IdleYoung=true | animState={stateInfo.shortNameHash} | inTransition={animator.IsInTransition(0)}");
            }
        }
    }

    private void HandleMovementCompleted(GameEventManager.UnitMovementEventArgs args)
    {
        if (args.Unit == this) UpdateWalkingState(false);
    }

    void OnMouseDown()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null && 
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;

        UnitSelectionManager.Instance?.SelectUnit(this);
    }

    #endregion
}
