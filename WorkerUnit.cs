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
    public int currentAttackPoints { get; private set; }
    public int currentMovePoints { get; private set; }

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
    private readonly int buildHash = Animator.StringToHash("Build");

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
    protected override float MeleeEngageDuration => data?.meleeEngageDuration ?? 8f;

    public override void ResetForNewTurn()
    {
        var wb = AggregateWorkerBonusesLocal(owner, data);
        currentWorkPoints = Mathf.RoundToInt((data.baseWorkPoints + wb.workAdd) * (1f + wb.workPct));
        int baseMove = Mathf.RoundToInt((data.baseMovePoints + wb.moveAdd) * (1f + wb.movePct));

        if (IsTrapped)
        {
            // trappedTurnsRemaining is inherited from BaseUnit
            var prop = typeof(BaseUnit).GetField("trappedTurnsRemaining", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (prop != null) prop.SetValue(this, Mathf.Max(0, (int)prop.GetValue(this) - 1));
            currentMovePoints = 0;
        }
        else
        {
            currentMovePoints = baseMove;
            if (hasWinterPenalty && ClimateManager.Instance != null && ClimateManager.Instance.currentSeason == Season.Winter)
            {
                currentMovePoints = Mathf.Max(1, currentMovePoints - 1);
            }
        }

        CheckForHazardousBiomeDamage();

        // Auto-contribute to jobs
        if (currentWorkPoints > 0 && ImprovementManager.Instance != null)
        {
            if (ImprovementManager.Instance.JobAssignedToWorker(currentTileIndex, this))
            {
                var tileData = TileSystem.Instance?.GetTileData(currentTileIndex);
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
        currentMovePoints = Mathf.RoundToInt((unitData.baseMovePoints + wb.moveAdd) * (1f + wb.movePct));
        takesWeatherDamage = unitData.takesWeatherDamage;

        // Position the unit on the tile
        PositionUnitOnSurface(startTileIndex);

        if (animator != null) animator.SetTrigger(idleYoungHash);

        InitializeUnitLabel();
    }

    public void ContributeWork()
    {
        if (currentWorkPoints <= 0) return;
        ImprovementManager.Instance.AddWork(currentTileIndex, currentWorkPoints);
        currentWorkPoints = 0;
    }

    public void ContributeWorkToUnit()
    {
        if (currentWorkPoints <= 0) return;
        ImprovementManager.Instance.AddUnitWork(currentTileIndex, currentWorkPoints);
        currentWorkPoints = 0;
    }

    public void ContributeWorkToWorker()
    {
        if (currentWorkPoints <= 0) return;
        ImprovementManager.Instance.AddWorkerWork(currentTileIndex, currentWorkPoints);
        currentWorkPoints = 0;
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

        var td = TileSystem.Instance?.GetTileData(currentTileIndex);
        if (td == null || !td.isLand) return false;

        // Distance check
        const float minCityDist = 4.0f;
        var allCivs = CivilizationManager.Instance.GetAllCivs();
        foreach (var civ in allCivs)
        {
            foreach (var city in civ.cities)
            {
                float d = Vector3.Distance(grid.tileCenters[currentTileIndex], grid.tileCenters[city.centerTileIndex]);
                if (d < minCityDist) return false;
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
            float valF = base.CurrentDefense;
            if (currentTileIndex >= 0)
            {
                var td = TileSystem.Instance?.GetTileData(currentTileIndex);
                if (td != null)
                {
                    valF += td.improvementDefenseAddWorker;
                    valF *= (1f + td.improvementDefensePctWorker);
                }
            }
            return Mathf.RoundToInt(valF);
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

    public override bool CanMoveTo(int tileIndex)
    {
        var td = TileSystem.Instance?.GetTileData(tileIndex);
        if (td == null || !td.isPassable || !td.isLand) return false;
        
        // Cost check
        int cost = BiomeHelper.GetMovementCost(td, this);
        if (currentMovePoints < cost) return false;

        if (td.occupantId != 0 && td.occupantId != gameObject.GetInstanceID()) return false;
        return true;
    }

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
    /// Workers can be attacked by combat units; workers can also weakly retaliate.
    /// </summary>
    public bool CanAttack(CombatUnit target)
    {
        if (target == null) return false;
        float dist = Vector3.Distance(transform.position, target.transform.position);
        return dist <= BaseRange;
    }

    public void Attack(CombatUnit target)
    {
        if (!CanAttack(target)) return;
        int damage = Mathf.Max(1, CurrentAttack);
        target.ApplyDamage(damage, this, attackerIsMelee: true);
    }

    /// <summary>
    /// Check if worker can forage the specified resource instance on the specified tile.
    /// This matches the UI call sites (UnitInfoPanel).
    /// </summary>
    public bool CanForage(ResourceData resource, int tileIndex)
    {
        if (resource == null) return false;
        if (currentWorkPoints <= 0) return false;
        if (tileIndex != currentTileIndex) return false;
        var td = TileSystem.Instance?.GetTileData(tileIndex);
        if (td == null || !td.isLand) return false;
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
        return true;
    }

    public bool CanBuildWorker(WorkerUnitData workerData, int tileIndex)
    {
        if (workerData == null) return false;
        if (owner == null) return false;
        if (currentWorkPoints <= 0) return false;
        if (tileIndex != currentTileIndex) return false;
        return true;
    }

    public void StartBuildingUnit(CombatUnitData unitData, int tileIndex)
    {
        if (!CanBuildUnit(unitData, tileIndex)) return;
        ImprovementManager.Instance?.AddUnitWork(tileIndex, currentWorkPoints);
        currentWorkPoints = 0;
    }

    public void StartBuildingWorker(WorkerUnitData workerData, int tileIndex)
    {
        if (!CanBuildWorker(workerData, tileIndex)) return;
        ImprovementManager.Instance?.AddWorkerWork(tileIndex, currentWorkPoints);
        currentWorkPoints = 0;
    }

    public void StartBuilding(ImprovementData improvement, int tileIndex)
    {
        if (improvement == null) return;
        if (currentWorkPoints <= 0) return;
        if (tileIndex != currentTileIndex) return;
        ImprovementManager.Instance?.AddWork(tileIndex, currentWorkPoints);
        currentWorkPoints = 0;
    }

    #endregion

    #region Helper Methods

    private void CheckForHazardousBiomeDamage()
    {
        if (currentTileIndex < 0) return;
        var td = TileSystem.Instance?.GetTileData(currentTileIndex);
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
