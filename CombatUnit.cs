using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using TMPro;
using GameCombat;

public class CombatUnit : BaseUnit
{
    [Header("Stats (Override Data Asset)")]
    [SerializeField] private int attack = 0;
    [SerializeField] private int defense = 0;
    [SerializeField] private int health = 0; 
    [SerializeField] private float range = 0;
    [SerializeField] private bool useOverrideStats = false;
    
    // Extra map for secondary equipment visuals (e.g., projectile weapon stored separately)
    protected Dictionary<string, GameObject> extraEquippedItemObjects = new Dictionary<string, GameObject>();
    
    public EquipmentData Weapon => equippedWeapon;
    public EquipmentData Shield => equippedShield;
    public EquipmentData Armor => equippedArmor;
    public EquipmentData Miscellaneous => equippedMiscellaneous;
    public EquipmentData ProjectileWeapon => equippedProjectileWeapon;

    // === IMPLEMENT ABSTRACT MEMBERS FROM BaseUnit ===
    
    public override string UnitName => data?.unitName ?? "Unknown";
    
    public override int BaseAttack => useOverrideStats && attack > 0 ? attack : (data?.baseAttack ?? 0);
    public override int BaseDefense => useOverrideStats && defense > 0 ? defense : (data?.baseDefense ?? 0);
    public override int BaseHealth => useOverrideStats && health > 0 ? health : (data?.baseHealth ?? 0);
    public override float BaseRange => useOverrideStats && range > 0 ? range : (data?.baseRange ?? 0);
    
    protected override EquipmentTarget AcceptedEquipmentTarget => EquipmentTarget.CombatUnit;
    /// <summary>
    /// Editor button to equip all default equipment from the assigned data asset.
    /// </summary>
    [ContextMenu("Equip Default Equipment (Editor)")]
    public void EquipDefaultEquipmentEditor()
    {
        if (data == null)
        {
            return;
        }
    // Map default weapon slots: prefer explicit projectile weapon; melee uses the authoritative defaultWeapon
    if (data.defaultProjectileWeapon != null) EquipItem(data.defaultProjectileWeapon);
    if (data.defaultWeapon != null) EquipItem(data.defaultWeapon);
        equippedShield = data.defaultShield;
        equippedArmor = data.defaultArmor;
        equippedMiscellaneous = data.defaultMiscellaneous;
        #if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        #endif
    }
    
    // grid, planet, animator are inherited from BaseUnit


    // Projectile fields (useAnimationEventForProjectiles, queuedProjectile*, hasQueuedProjectile, engagedInMelee) 
    // are inherited from BaseUnit
    
    private const float MELEE_RANGE_CHECK_INTERVAL = 0.3f;
    private const float MELEE_ENGAGEMENT_RANGE = 2.5f; // Distance to consider "in melee range"

    [field: SerializeField] public CombatUnitData data { get; private set; }  // Now serializable and assignable in Inspector
    // owner, currentHealth are inherited from BaseUnit

    // Remove old events and use GameEventManager instead
    public event System.Action OnDeath;
    public event System.Action<int,int> OnHealthChanged;      // (newHealth, maxHealth)
    public event System.Action<string> OnAnimationTrigger;    // (triggerName)
    // OnEquipmentChanged is inherited from BaseUnit

    // equipped, unlockedAbilities, currentHealth are inherited from BaseUnit

    // CombatUnit-specific runtime stats
    public int experience { get; private set; }
    public int level { get; private set; }
    // takesWeatherDamage, hasWinterPenalty are inherited from BaseUnit

    // Transport system
    private List<CombatUnit> transportedUnits = new List<CombatUnit>();
    // Events for UI updates when units are loaded/unloaded
    public UnityEvent<CombatUnit> OnUnitLoaded = new UnityEvent<CombatUnit>();
    public UnityEvent<CombatUnit> OnUnitUnloaded = new UnityEvent<CombatUnit>();
    // Event for when this unit is loaded into another transport
    public UnityEvent<CombatUnit> OnLoadedIntoTransport = new UnityEvent<CombatUnit>();
    // Event for when this unit is unloaded from a transport
    public UnityEvent<CombatUnit> OnUnloadedFromTransport = new UnityEvent<CombatUnit>();
    
    // Property to check if this unit is currently transported
    public bool IsTransported { get; private set; }
    // Reference to the transport carrying this unit (if any)
    public CombatUnit TransportingUnit { get; private set; }
    
    /// <summary>
    /// Whether this unit has performed a turn-consuming action (orbit entry/exit, etc.).
    /// Prevents further movement or attacks this turn.
    /// </summary>
    public bool hasActedThisTurn { get; private set; }
    
    /// <summary>
    /// Whether this unit is currently stationed in a friendly city.
    /// Set by UnitReinforcementManager each turn.
    /// </summary>
    public bool isGarrisonedInCity { get; set; }

    // unitLabelPrefab, unitLabelInstance are inherited from BaseUnit



    protected override void Start()
    {
        base.Start();
        // If equipment was assigned in Inspector before play mode, ensure visuals are created at runtime
        if (Application.isPlaying)
        {
            UpdateEquipmentVisuals();
        }
    }


    protected override void Awake()
    {
        base.Awake(); // This handles animator, grid, planet, and UnitRegistry
        
        // CRITICAL FIX: Ensure animator is properly configured
        if (animator != null)
        {
            animator.applyRootMotion = false;
            // BaseUnit cached animator parameter availability during base.Awake().
            // CombatUnit can rebind/adjust its animator after that, so refresh the cache
            // against the final animator instance used by this combat unit.
            _hasWalkParam = HasParameter(animator, isWalkingHash);
            _hasHitParam = HasParameter(animator, hitHash);
            _hasDeathParam = HasParameter(animator, deathHash);
            _hasFortifyParam = HasParameter(animator, isFortifiedHash);
            // Set update mode to Normal (updates every frame with Time.deltaTime)
            animator.updateMode = AnimatorUpdateMode.Normal;
            // Ensure culling mode allows animation even when off-screen during setup
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            
            // STAGGER IDLE ANIMATIONS: Add random offset so units don't animate in sync
            // This makes formations look more natural and alive
            StaggerAnimationStart();
        }
        else
        {
            Debug.LogWarning($"[CombatUnit] {gameObject.name} Awake: NO ANIMATOR FOUND!");
        }
        
        // BaseUnit.Awake already binds planet/grid using planetIndex.
        // Resolve planet generator with diagnostics: prefer owner helper, then GameManager, then current.
        PlanetGenerator resolved = null;
        if (owner != null)
        {
            try { resolved = owner.GetPlanetGeneratorForIndex(planetIndex); } catch { resolved = null; }
            if (resolved == null)
            {
                Debug.LogWarning($"[CombatUnit] Owner '{owner.civData?.civName ?? owner.name}' returned null for GetPlanetGeneratorForIndex({planetIndex}); falling back to GameManager.");
            }
        }

        if (resolved == null)
        {
            var gm = GameManager.Instance;
            if (gm != null)
            {
                resolved = gm.GetPlanetGenerator(planetIndex);
                if (resolved == null)
                {
                    Debug.LogWarning($"[CombatUnit] GameManager has no generator for planetIndex {planetIndex}; falling back to current planet generator.");
                    resolved = gm.GetCurrentPlanetGenerator();
                }
            }
            else
            {
                Debug.LogWarning("[CombatUnit] GameManager.Instance is null; cannot resolve PlanetGenerator.");
            }
        }

        planet = resolved ?? planet;
        if (planet != null) grid = planet.Grid;
        // Do not auto-register here; registration is performed explicitly by spawners
        // after initialization and placement to avoid premature occupancy claims.

        // Improved fallback: Auto-assign defaults if data exists
        if (data != null)
        {
            if (_equippedWeapon == null && data.defaultWeapon != null)
            {
                _equippedWeapon = data.defaultWeapon;
            }
            if (_equippedShield == null && data.defaultShield != null)
            {
                _equippedShield = data.defaultShield;
            }
            if (_equippedArmor == null && data.defaultArmor != null)
            {
                _equippedArmor = data.defaultArmor;
            }
            if (_equippedMiscellaneous == null && data.defaultMiscellaneous != null)
            {
                _equippedMiscellaneous = data.defaultMiscellaneous;
            }
        }
        // Always update visuals
        UpdateEquipmentVisuals();
    }

    // Ensure equipment visuals update in edit mode when fields are changed
    protected override void OnValidate()
    {
        base.OnValidate();
        // Additional CombatUnit-specific validation if needed
    }


    protected override void OnDestroy()
    {
        // Unsubscribe from CombatUnit-specific events
        GameEventManager.Instance.OnMovementCompleted -= HandleMovementCompleted;
        GameEventManager.Instance.OnCombatStarted -= HandleCombatStarted;
        GameEventManager.Instance.OnDamageApplied -= HandleDamageApplied;

        // Base handles equipment cleanup and UnitRegistry
        base.OnDestroy();
    }

    public void Initialize(CombatUnitData unitData, Civilization unitOwner)
    {
        data = unitData;
        owner = unitOwner;
        level = 1;
        experience = 0;

        // Equip all default equipment slots - only if data is valid
        if (data != null)
        {
            // Equip projectile and melee defaults (defaultWeapon is authoritative melee)
            if (data.defaultProjectileWeapon != null) EquipItem(data.defaultProjectileWeapon);
            if (data.defaultWeapon != null) EquipItem(data.defaultWeapon);
            if (data.defaultShield != null) EquipItem(data.defaultShield);
            if (data.defaultArmor != null) EquipItem(data.defaultArmor);
            if (data.defaultMiscellaneous != null) EquipItem(data.defaultMiscellaneous);
            
            // Weather susceptibility from data
            takesWeatherDamage = data.takesWeatherDamage;
        }
        else
        {
            // Fallback if data is null - keep default weather damage setting
            Debug.LogWarning($"CombatUnit.Initialize called with null unitData for {gameObject.name}");
        }

        // Set health - ensure data is valid before accessing properties
        if (data != null)
        {
            currentHealth = MaxHealth;

            // Only recalculate stats if data is valid (properties access data)
            RecalculateStats();
            // Configure attack points from data asset
            try { attackPointsPerTurn = data.attackPointsPerTurn; ResetAttackPointsForNewTurn(); } catch { }
        }
        else
        {
            // Fallback if data is null (shouldn't happen but defensive programming)
            currentHealth = 10; // Default health
            // Don't call RecalculateStats() if data is null - properties will throw NullReferenceException
        }

        // CRITICAL FIX: Use GetComponentInChildren to find Animator on child objects (like Armature)
        // Prefabs often have Animator on child objects, not root
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
            if (animator == null)
            {
                // Fallback to root if no child Animator found
        animator = GetComponent<Animator>();
            }
        }
        // Ensure animator is not null before trying to set a trigger
        if (animator != null) 
        {
            animator.applyRootMotion = false;
            _hasWalkParam = HasParameter(animator, isWalkingHash);
            _hasHitParam = HasParameter(animator, hitHash);
            _hasDeathParam = HasParameter(animator, deathHash);
            _hasFortifyParam = HasParameter(animator, isFortifiedHash);

            // Initialize as not moving (idle state)
            UpdateWalkingState(false);
        }
        else
        {
            Debug.LogWarning($"CombatUnit {gameObject.name} is missing an Animator component.");
        }

        UpdateEquipmentVisuals();

        // Subscribe to events (defensive: GameEventManager may not be initialized during generation)
        if (GameEventManager.Instance != null)
        {
            GameEventManager.Instance.OnMovementCompleted += HandleMovementCompleted;
            GameEventManager.Instance.OnCombatStarted += HandleCombatStarted;
            GameEventManager.Instance.OnDamageApplied += HandleDamageApplied;
        }
        else
        {
            // Defer subscription until GameEventManager exists to avoid NullReferenceException during large generation sequences
            StartCoroutine(DeferredSubscribeToGameEventManager());
        }

        // Instantiate and initialize the unit label
        if (unitLabelPrefab != null && unitLabelInstance == null)
        {
            var labelGO = Instantiate(unitLabelPrefab, transform); // Parent to the unit
            unitLabelInstance = labelGO.GetComponent<UnitLabel>();
            if (unitLabelInstance != null)
            {
                string ownerName = owner != null && owner.civData != null ? owner.civData.civName : "Unknown";
                unitLabelInstance.Initialize(transform, data.unitName, ownerName, currentHealth, MaxHealth);

                // Disable raycast targets on the label's text components
                var textComponents = unitLabelInstance.GetComponentsInChildren<TMPro.TextMeshProUGUI>();
                foreach (var textComponent in textComponents)
                {
                    if (textComponent != null) textComponent.raycastTarget = false;
                }
            }
        }
        // Subscribe to health change for label update
        OnHealthChanged += UpdateUnitLabelHealth;

        // Initialize multi-soldier group if configured
        if (data != null)
        {
            int visualCount = data.GetSoldierCount(owner);
            if (visualCount > 1)
                InitializeSoldierGroup(visualCount, data.GetSoldierVariants(owner), data.GetFormationType(owner), data.GetFormationSpacing(owner));
        }
    }

    private System.Collections.IEnumerator DeferredSubscribeToGameEventManager()
    {
        while (GameEventManager.Instance == null)
            yield return null;

        try
        {
            GameEventManager.Instance.OnMovementCompleted += HandleMovementCompleted;
            GameEventManager.Instance.OnCombatStarted += HandleCombatStarted;
            GameEventManager.Instance.OnDamageApplied += HandleDamageApplied;
        }
        catch { }
    }

    // Base stats, equipment bonuses, and ability modifiers are inherited from BaseUnit or overridden above.


    // Combined stats - UPDATED to include all ability modifiers
    // Local aggregation structs
    private struct UnitAgg { public int attackAdd, defenseAdd, healthAdd, moveAdd, rangeAdd, apAdd; public float attackPct, defensePct, healthPct, movePct, rangePct, apPct; }
    private struct EquipAgg { public int attackAdd, defenseAdd, healthAdd, moveAdd, rangeAdd, apAdd; public float attackPct, defensePct, healthPct, movePct, rangePct, apPct; }

    private UnitAgg AggregateUnitBonusesLocal(Civilization civ, CombatUnitData u)
    {
        UnitAgg a = new UnitAgg(); if (civ == null || u == null) return a;
        if (civ.researchedTechs != null)
            foreach (var t in civ.researchedTechs)
            {
                if (t?.unitBonuses == null) continue;
                foreach (var b in t.unitBonuses)
                    if (b != null && b.unit == u)
                    {
                        a.attackAdd += b.attackAdd; a.defenseAdd += b.defenseAdd; a.healthAdd += b.healthAdd;
                        a.rangeAdd += b.rangeAdd;
                        a.attackPct += b.attackPct; a.defensePct += b.defensePct; a.healthPct += b.healthPct;
                        a.rangePct += b.rangePct;
                    }
            }
        if (civ.researchedCultures != null)
            foreach (var c in civ.researchedCultures)
            {
                if (c?.unitBonuses == null) continue;
                foreach (var b in c.unitBonuses)
                    if (b != null && b.unit == u)
                    {
                        a.attackAdd += b.attackAdd; a.defenseAdd += b.defenseAdd; a.healthAdd += b.healthAdd;
                        a.rangeAdd += b.rangeAdd;
                        a.attackPct += b.attackPct; a.defensePct += b.defensePct; a.healthPct += b.healthPct;
                        a.rangePct += b.rangePct;
                    }
            }
        return a;
    }

    private EquipAgg AggregateEquipBonusesLocal(Civilization civ, EquipmentData eq)
    {
        EquipAgg a = new EquipAgg(); if (civ == null || eq == null) return a;
        if (civ.researchedTechs != null)
            foreach (var t in civ.researchedTechs)
            {
                if (t?.equipmentBonuses == null) continue;
                foreach (var b in t.equipmentBonuses)
                    if (b != null && b.equipment == eq)
                    {
                        a.attackAdd += b.attackAdd; a.defenseAdd += b.defenseAdd; a.healthAdd += b.healthAdd;
                        a.rangeAdd += b.rangeAdd;
                        a.attackPct += b.attackPct; a.defensePct += b.defensePct; a.healthPct += b.healthPct;
                        a.rangePct += b.rangePct;
                    }
            }
        if (civ.researchedCultures != null)
            foreach (var c in civ.researchedCultures)
            {
                if (c?.equipmentBonuses == null) continue;
                foreach (var b in c.equipmentBonuses)
                    if (b != null && b.equipment == eq)
                    {
                        a.attackAdd += b.attackAdd; a.defenseAdd += b.defenseAdd; a.healthAdd += b.healthAdd;
                        a.rangeAdd += b.rangeAdd;
                        a.attackPct += b.attackPct; a.defensePct += b.defensePct; a.healthPct += b.healthPct;
                        a.rangePct += b.rangePct;
                    }
            }
        return a;
    }
    
    // Sum equipment-targeted bonuses across all currently equipped items
    private EquipAgg AggregateAllEquippedBonusesLocal(Civilization civ)
    {
        EquipAgg total = new EquipAgg();
        if (civ == null) return total;
        EquipmentData[] items = { equippedWeapon, equippedShield, equippedArmor, equippedMiscellaneous };
        foreach (var it in items)
        {
            if (it == null) continue;
            var e = AggregateEquipBonusesLocal(civ, it);
            total.attackAdd += e.attackAdd; total.defenseAdd += e.defenseAdd; total.healthAdd += e.healthAdd;
            total.moveAdd += e.moveAdd; total.rangeAdd += e.rangeAdd; total.apAdd += e.apAdd;
            total.attackPct += e.attackPct; total.defensePct += e.defensePct; total.healthPct += e.healthPct;
            total.movePct += e.movePct; total.rangePct += e.rangePct; total.apPct += e.apPct;
        }
        return total;
    }

    public override int CurrentAttack
    {
        get
        {
            // Use floats internally for accuracy, then round when returning an int for gameplay values that expect ints.
            float valF = BaseAttack + EquipmentAttackBonus + GetAbilityAttackModifier();
            if (owner != null && data != null)
            {
                var u = AggregateUnitBonusesLocal(owner, data);
                valF = (valF + u.attackAdd) * (1f + u.attackPct);
            }
            if (owner != null)
            {
                var e = AggregateAllEquippedBonusesLocal(owner);
                valF = (valF + e.attackAdd) * (1f + e.attackPct);
            }

            // Morale and fatigue scaling
            valF *= FatigueMultiplier;
            valF *= MoraleDamageMultiplier;
            
            // Apply per-target bonuses (if this unit is attacking a specific target, callers may need to apply extra modifiers).
            return Mathf.RoundToInt(valF);
        }
    }
    public override int CurrentDefense
    {
        get
        {
            return Mathf.RoundToInt(GetCurrentDefenseValueFloat());
        }
    }

    protected override float ApplyOwnerDefenseBonuses(float defenseValue)
    {
        float valF = defenseValue;
        if (owner != null && data != null)
        {
            var u = AggregateUnitBonusesLocal(owner, data);
            valF = (valF + u.defenseAdd) * (1f + u.defensePct);
        }
        if (owner != null)
        {
            var e = AggregateAllEquippedBonusesLocal(owner);
            valF = (valF + e.defenseAdd) * (1f + e.defensePct);
        }
        return valF;
    }

    public override int MaxHealth
    {
        get
        {
            float valF = BaseHealth + EquipmentHealthBonus + GetAbilityHealthModifier();
            if (owner != null && data != null)
            {
                var u = AggregateUnitBonusesLocal(owner, data);
                valF = (valF + u.healthAdd) * (1f + u.healthPct);
            }
            if (owner != null)
            {
                var e = AggregateAllEquippedBonusesLocal(owner);
                valF = (valF + e.healthAdd) * (1f + e.healthPct);
            }
            return Mathf.RoundToInt(valF);
        }
    }

    public override float CurrentRange
    {
        get
        {
            float valF = BaseRange + EquipmentRangeBonus + GetAbilityRangeModifier();
            if (owner != null && data != null)
            {
                var u = AggregateUnitBonusesLocal(owner, data);
                valF = (valF + u.rangeAdd) * (1f + u.rangePct);
            }
            if (owner != null)
            {
                var e = AggregateAllEquippedBonusesLocal(owner);
                valF = (valF + e.rangeAdd) * (1f + e.rangePct);
            }
            return valF; // Return as float, no rounding
        }
    }


    // CanMoveTo is fully consolidated in BaseUnit — no override needed.

    // MoveAlongPath removed -- all movement now goes through UnitMovementController.ExecuteMovement

    // ===== COMBAT UNIT VS COMBAT UNIT =====
    
    public bool CanAttack(CombatUnit target)
    {
        // Target category checks
    bool targetIsAir = target.data.unitType == CombatCategory.Aircraft;
    bool targetIsSpace = target.data.unitType == CombatCategory.Spaceship;
    bool targetIsUnderwater = target.data.unitType == CombatCategory.Submarine || 
                 target.data.unitType == CombatCategory.SeaCrawler;

        // Check specific attack capabilities
        if (targetIsAir && !data.canAttackAir) return false;
        if (targetIsSpace && !data.canAttackSpace) return false;
        if (targetIsUnderwater && !data.canAttackUnderwater) return false;

        // Orbit layer interaction rules
        bool attackerInOrbit = currentLayer == TileLayer.Orbit;
        bool targetInOrbit  = target.currentLayer == TileLayer.Orbit;

        // Orbit-to-Orbit: both in orbit — normal combat, no restrictions beyond range
        // Orbit-to-Surface (bombardment): attacker must have canBombardSurface
        if (attackerInOrbit && !targetInOrbit)
        {
            if (!data.canBombardSurface) return false;
        }

        // Surface-to-Orbit: must have canAttackSpace
        if (!attackerInOrbit && targetInOrbit)
        {
            if (!data.canAttackSpace) return false;
        }

        // Range check — use tile-step distance (hex steps) consistent with movement/path math
        try
        {
            var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
            if (ts != null && currentTileIndex >= 0 && target.currentTileIndex >= 0)
            {
                int tileSteps = ts.GetWrappedHexDistance(currentTileIndex, target.currentTileIndex);
                if (tileSteps >= 0)
                {
                    int maxSteps = Mathf.FloorToInt(CurrentRange);
                    if (tileSteps > maxSteps) return false;

                    // LOS check for ranged attacks (distance > 1)
                    if (tileSteps > 1 && !CombatHelpers.HasLineOfSight(currentTileIndex, target.currentTileIndex, planetIndex))
                        return false;

                    return true;
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[CombatUnit] Tile-based range check failed: {ex.Message}");
        }

        // If tile-based check couldn't be performed, do not allow attack (no fallbacks)
        return false;
    }

    /// <summary>
    /// Whether this unit can currently bombard surface tiles from orbit.
    /// </summary>
    public bool CanBombardSurface => currentLayer == TileLayer.Orbit
                                     && data != null
                                     && data.canBombardSurface;
    
    // ===== COMBAT UNIT VS WORKER UNIT =====
    
    /// <summary>
    /// Check if this combat unit can attack a worker unit - NEW!
    /// Combat units can now attack workers (usually one-sided!)
    /// </summary>
    public bool CanAttack(WorkerUnit target)
    {
        if (target == null) return false;
        
        // Orbit-to-surface: must have canBombardSurface to attack ground targets
        if (currentLayer == TileLayer.Orbit && target.currentLayer != TileLayer.Orbit)
        {
            if (data == null || !data.canBombardSurface) return false;
        }
        
        // Range check — use tile-step distance (hex steps) consistent with movement/path math
        try
        {
            var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
            if (ts != null && currentTileIndex >= 0 && target.currentTileIndex >= 0)
            {
                int tileSteps = ts.GetWrappedHexDistance(currentTileIndex, target.currentTileIndex);
                if (tileSteps >= 0)
                {
                    int maxSteps = Mathf.FloorToInt(CurrentRange);
                    if (tileSteps > maxSteps) return false;

                    // LOS check for ranged attacks (distance > 1)
                    if (tileSteps > 1 && !CombatHelpers.HasLineOfSight(currentTileIndex, target.currentTileIndex, planetIndex))
                        return false;

                    return true;
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[CombatUnit] Tile-based range check failed (worker): {ex.Message}");
        }

        // If tile-based check couldn't be performed, do not allow attack (no fallbacks)
        return false;
    }
    
    /// <summary>
    /// Generic check if combat unit can attack any target (for UI highlighting)
    /// </summary>
    public bool CanAttackAnyTarget(GameObject target)
    {
        if (target == null) return false;
        
        // Try as CombatUnit first (most common)
        var combatUnit = target.GetComponent<CombatUnit>();
        if (combatUnit != null)
            return CanAttack(combatUnit);
        
        // Try as WorkerUnit
        var workerUnit = target.GetComponent<WorkerUnit>();
        if (workerUnit != null)
            return CanAttack(workerUnit);
        
        return false;
    }
    
    /// <summary>
    /// Generic attack method that automatically detects target type
    /// </summary>
    public void AttackTarget(GameObject target)
    {
        if (target == null) return;
        
        // Try as CombatUnit first (most common)
        var combatUnit = target.GetComponent<CombatUnit>();
        if (combatUnit != null)
        {
            Attack(combatUnit);
            return;
        }
        
        // Try as WorkerUnit
        var workerUnit = target.GetComponent<WorkerUnit>();
        if (workerUnit != null)
        {
            Attack(workerUnit);
            return;
        }
        
        Debug.LogWarning($"[CombatUnit] Cannot attack {target.name} - no valid unit component found");
    }

    public void Attack(CombatUnit target)
    {
        bool canAttack = CanAttack(target);
        if (!canAttack)
        {
            Debug.Log($"[CombatUnit] {name} Attack aborted: CanAttack returned false for target={target?.name}");
            return;
        }

        Debug.Log($"[CombatUnit] {name} Attack requested on {target.name} (selTile={currentTileIndex} tgtTile={target.currentTileIndex} range={CurrentRange})");

        // Front-row damage routing: melee attacks automatically target the front unit of a stack
        try
        {
            var tsRoute = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
            if (tsRoute != null && currentTileIndex >= 0 && target.currentTileIndex >= 0)
            {
                int dist = tsRoute.GetWrappedHexDistance(currentTileIndex, target.currentTileIndex);
                if (dist <= 1) // melee range — redirect to front unit
                {
                    var front = target.GetFrontUnit() as CombatUnit;
                    if (front != null && front != target)
                    {
                        Debug.Log($"[CombatUnit] Melee damage routed from {target.name} (slot {target.stackSlot}) to front unit {front.name} (slot {front.stackSlot})");
                        target = front;
                    }
                }
            }
        }
        catch { }

        try
        {

    // Choose active weapon based on melee engagement
    EquipmentData activeWeapon = null;
    if (engagedInMelee && equippedWeapon != null)
        activeWeapon = equippedWeapon;
    else if (equippedProjectileWeapon != null)
        activeWeapon = equippedProjectileWeapon;
    else if (equippedWeapon != null)
        activeWeapon = equippedWeapon;
    else
        activeWeapon = equippedWeapon;
    
        // Tile defense bonus for target (e.g., hills)
        int tileBonus = 0;
        var tsTarget = TileSystem.GetForPlanet(target.planetIndex) ?? TileSystem.Instance;
        var tileData = tsTarget != null ? tsTarget.GetTileData(target.currentTileIndex) : null;
        if (tileData != null)
        {
            tileBonus = BiomeHelper.GetDefenseBonus(tileData.biome);
            if (tileData.isHill)
                tileBonus += 2;
        }

        // Damage calculation using floats and per-target equipment modifiers
        float dmgMul = GetAbilityDamageMultiplier();

        float attackerValue = GetBaseAttackFloat() + GetEquipmentAttackBonusAgainst(target.data.unitType);
        float defenderValue = target.GetBaseDefenseFloat() + target.GetEquipmentDefenseBonusAgainst(this.data.unitType);

        float rawF = Mathf.Max(0f, attackerValue - defenderValue - tileBonus);

        // Charge bonus: if attacker had to move more than 1 tile to reach this target, apply a percent bonus
        float chargeMul = 1f;
        try
        {
            var tsLocal = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
            if (tsLocal != null && currentTileIndex >= 0 && target.currentTileIndex >= 0)
            {
                int moveDist = tsLocal.GetWrappedHexDistance(currentTileIndex, target.currentTileIndex);
                if (moveDist > 1 && data != null && data.chargeBonusPercent > 0f)
                    chargeMul += data.chargeBonusPercent;
            }
        }
        catch { }

        int damage = Mathf.RoundToInt(rawF * dmgMul * chargeMul);

        damage = ApplySharedMeleeCombatModifiers(damage, target);

    // If the active weapon defines projectile data, either queue or spawn the projectile depending on settings
    if (activeWeapon != null && activeWeapon.projectileData != null)
        {
            // Apply status effect from projectile on hit
            if (activeWeapon.projectileData.statusEffect != null && target != null)
                target.ApplyStatusEffect(activeWeapon.projectileData.statusEffect);

            if (useAnimationEventForProjectiles)
            {
        QueueProjectileForAnimation(activeWeapon, target.transform.position, target, damage);
                return;
            }
            else
            {
        SpawnProjectileFromEquipment(activeWeapon, target.transform.position, target, damage);
                return;
            }
        }

    // Melee / instant-hit path: apply damage immediately and provide attacker context so the melee weapon behavior can trigger
    var ctx = new BaseUnit.AttackContext { attacker = this, defender = target, weapon = activeWeapon, damage = damage, isMelee = true, isRanged = false };
    bool targetDies = PerformAttack(ctx);

        // Apply weapon status effect on hit (if any)
        if (activeWeapon?.projectileData?.statusEffect != null && target != null && target.currentHealth > 0)
            target.ApplyStatusEffect(activeWeapon.projectileData.statusEffect);

        if (targetDies)
        {
            // Post-hit handling centralized in BaseUnit.ApplyDamage(attacker...)
        }
        else
        {
            // Counter-attack if target can
            if (target.CanAttack(this))
                target.CounterAttack(this);
        }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[CombatUnit] Error in Attack: {e.Message}");
        }
    }
    
    /// <summary>
    /// Attack a worker unit - NEW!
    /// Combat units can attack workers (usually devastating!)
    /// </summary>
    public void Attack(WorkerUnit target)
    {
        if (target == null) return;
        if (!CanAttack(target)) return;

        // Choose active weapon
        EquipmentData activeWeapon = null;
        if (engagedInMelee && equippedWeapon != null)
            activeWeapon = equippedWeapon;
        else if (equippedProjectileWeapon != null)
            activeWeapon = equippedProjectileWeapon;
        else if (equippedWeapon != null)
            activeWeapon = equippedWeapon;

        // For ranged attacks, still use the trigger (one-shot projectile launch animation)
        bool isRangedAttack = activeWeapon != null && activeWeapon.projectileData != null;
        if (isRangedAttack)
        {
            // Ranged visuals handled centrally by BaseUnit.PerformAttack
        }
        // Melee attacks use IsAttacking bool (continuous), not a trigger

        // Combat units fight at advantage against workers (+2 bonus vs non-combatants)
        int combatBonus = 2;
        
        float attackerValue = GetBaseAttackFloat() + combatBonus;
        float defenderValue = target.CurrentDefense;
        
        float rawDamage = Mathf.Max(0f, attackerValue - defenderValue);

        float chargeMulW = 1f;
        try
        {
            var tsLocalW = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
            if (tsLocalW != null && currentTileIndex >= 0 && target.currentTileIndex >= 0)
            {
                int moveDistW = tsLocalW.GetWrappedHexDistance(currentTileIndex, target.currentTileIndex);
                if (moveDistW > 1 && data != null && data.chargeBonusPercent > 0f)
                    chargeMulW += data.chargeBonusPercent;
            }
        }
        catch { }

        int finalDamage = Mathf.RoundToInt(rawDamage * GetAbilityDamageMultiplier() * chargeMulW);

        finalDamage = ApplySharedMeleeCombatModifiers(finalDamage, target);

        // Handle ranged vs melee
        if (isRangedAttack)
        {
            if (activeWeapon?.projectileData?.statusEffect != null && target != null)
                target.ApplyStatusEffect(activeWeapon.projectileData.statusEffect);

            SpawnProjectileTowardsWorker(activeWeapon, target.transform.position, finalDamage);
            return;
        }

        // Melee attack — use unified orchestrator so kill rewards/events are centralized
        var ctxWorker = new BaseUnit.AttackContext { attacker = this, defender = target, weapon = activeWeapon, damage = finalDamage, isMelee = true, isRanged = false };
        bool targetDied = PerformAttack(ctxWorker);

        // Apply weapon status effect on melee hit
        if (activeWeapon?.projectileData?.statusEffect != null && target != null && target.currentHealth > 0)
            target.ApplyStatusEffect(activeWeapon.projectileData.statusEffect);

        if (targetDied)
        {
            // Post-hit handling centralized in BaseUnit.ApplyDamage(attacker...)
        }
        else
        {
            // Worker can try to fight back (usually futile!)
            if (target.CanAttack(this))
            {
                target.Attack(this);
            }
        }

        // XP awarded centrally in PerformAttack
    }

    /// <summary>
    /// Override of unified Attack(BaseUnit) entry point. Dispatches to the
    /// existing type-specific Attack implementations to preserve current behavior.
    /// </summary>
    public override void Attack(BaseUnit target)
    {
        if (target is CombatUnit tc)
        {
            Attack(tc);
        }
        else if (target is WorkerUnit tw)
        {
            Attack(tw);
        }
        else
        {
            Debug.LogWarning($"[CombatUnit] Attack(BaseUnit) received unsupported target type: {target?.GetType().Name}");
        }
    }
    
    /// <summary>
    /// Helper to play projectile sounds towards a worker target position
    /// </summary>
    private void SpawnProjectileTowardsWorker(EquipmentData equipment, Vector3 targetPosition, int damage)
    {
        if (equipment == null || equipment.projectileData == null) return;

        ProjectileData pd = equipment.projectileData;
        if (pd.launchSound != null)
            AudioSource.PlayClipAtPoint(pd.launchSound, transform.position);
        if (pd.impactSound != null)
            AudioSource.PlayClipAtPoint(pd.impactSound, targetPosition);
    }

    /// <summary>
    /// Destroy this unit
    /// </summary>
    protected override void Die()
    {
        StopAllCoroutines();
        
        // Clear walking/idle state when dead
        UpdateWalkingState(false);
        
        // Fire local death event for listeners (e.g., AnimalManager)
        OnDeath?.Invoke();
        
        if (owner != null) owner.combatUnits.Remove(this);
        
        // Base handles tile cleanup, label cleanup, and GameObject destruction
        base.Die();
    }

    /// <summary>
    /// Performs a counter-attack back at the attacker.
    /// </summary>
    public void CounterAttack(CombatUnit attacker)
    {
        if (!data.canCounterAttack) return;
        
        OnAnimationTrigger?.Invoke("attack");

        int tileBonus = 0;
        var tsAttacker = TileSystem.GetForPlanet(attacker.planetIndex) ?? TileSystem.Instance;
        var tileData = tsAttacker != null ? tsAttacker.GetTileData(attacker.currentTileIndex) : null;
        if (tileData != null)
        {
            tileBonus = BiomeHelper.GetDefenseBonus(tileData.biome);
            if (tileData.isHill)
                tileBonus += 2;
        }

        float dmgMul = GetAbilityDamageMultiplier();

        float attackerValue = GetBaseAttackFloat() + GetEquipmentAttackBonusAgainst(attacker.data.unitType);
        float defenderValue = attacker.GetBaseDefenseFloat() + attacker.GetEquipmentDefenseBonusAgainst(this.data.unitType);

        float rawF = Mathf.Max(0f, attackerValue - defenderValue - tileBonus);
        int damage = Mathf.RoundToInt(rawF * dmgMul);

        damage = ApplySharedMeleeCombatModifiers(damage, attacker);

    var ctxCounter = new BaseUnit.AttackContext { attacker = this, defender = attacker, weapon = null, damage = damage, isMelee = true, isRanged = false };
    bool counterDidKill = PerformAttack(ctxCounter);
    }


    // --- Float helpers for combat that include equipment per-target modifiers ---
    private float GetBaseAttackFloat()
    {
        // BaseAttack is int; equipment and abilities may be fractional
        return BaseAttack + EquipmentAttackBonus + GetAbilityAttackModifier();
    }

    private float GetBaseDefenseFloat()
    {
        return GetCurrentDefenseValueFloat();
    }

    private float GetEquipmentAttackBonusAgainst(CombatCategory targetType)
    {
        float add = 0f;
        EquipmentData[] items = { equippedWeapon, equippedShield, equippedArmor, equippedMiscellaneous };
        foreach (var it in items)
        {
            if (it == null) continue;
            if (it.attackBonusAgainst != null)
            {
                foreach (var entry in it.attackBonusAgainst)
                {
                    if (entry.unitType == targetType) add += entry.value;
                }
            }
        }
        return add;
    }

    private float GetEquipmentDefenseBonusAgainst(CombatCategory attackerType)
    {
        float add = 0f;
        EquipmentData[] items = { equippedWeapon, equippedShield, equippedArmor, equippedMiscellaneous };
        foreach (var it in items)
        {
            if (it == null) continue;
            if (it.defenseBonusAgainst != null)
            {
                foreach (var entry in it.defenseBonusAgainst)
                {
                    if (entry.unitType == attackerType) add += entry.value;
                }
            }
        }
        return add;
    }


    public void GainExperience(int xp)
    {
        experience += xp;
        if (level < data.xpToNextLevel.Length && experience >= data.xpToNextLevel[level - 1])
            LevelUp();
    }

    /// <summary>
    /// Called by projectiles or other external systems when this unit's attack caused a kill.
    /// Awards XP for the kill.
    /// </summary>
    public void RegisterKillFromProjectile(int damage)
    {
        GainExperience(damage);
    }

    private void LevelUp()
    {
        level++;
        if (animator == null)
        {
            // CRITICAL FIX: Use GetComponentInChildren to find Animator on child objects (like Armature)
            animator = GetComponentInChildren<Animator>();
            if (animator == null)
            {
                // Fallback to root if no child Animator found
            animator = GetComponent<Animator>();
            }
        }
        if (animator != null)
        {
            animator.applyRootMotion = false;
            _hasWalkParam = HasParameter(animator, isWalkingHash);
            _hasHitParam = HasParameter(animator, hitHash);
            _hasDeathParam = HasParameter(animator, deathHash);
            _hasFortifyParam = HasParameter(animator, isFortifiedHash);
            // Update idle animation if not moving
            if (!isMoving)
            {
                UpdateWalkingState(false);
            }
        }
        if (level - 1 < data.abilitiesByLevel.Length && data.abilitiesByLevel[level - 1] != null)
        {
            unlockedAbilities.Add(data.abilitiesByLevel[level - 1].CreateAbility());
            // Recalculate stats when adding a new ability
            RecalculateStats();
        }
    }

    public void Equip(EquipmentData newEquip)
    {
    equipped = newEquip;
    // Use the central visual update path to avoid duplicate instantiation
    UpdateEquipmentVisuals();
    // Recalculate move/attack points and health
    RecalculateStats();
    }
    
    // New helper method to recalculate stats affected by equipment and abilities
    private void RecalculateStats()
    {
        // Base + equipment + abilities are already encapsulated in properties
        float maxHPF = BaseHealth + EquipmentHealthBonus + GetAbilityHealthModifier();

        // Apply targeted bonuses from techs/cultures
        if (owner != null && data != null)
        {
            var agg = AggregateUnitBonusesLocal(owner, data);
            // Apply additive first
            maxHPF += agg.healthAdd;
            // Apply multiplicative
            maxHPF = maxHPF * (1f + agg.healthPct);
            // Attack/Defense/Range/Morale handled dynamically via getters or in combat
            // Apply equipment-targeted bonuses across all equipped items
            var eagg = AggregateAllEquippedBonusesLocal(owner);
            maxHPF = (maxHPF + eagg.healthAdd) * (1f + eagg.healthPct);
        }

        currentHealth = Mathf.Min(currentHealth, Mathf.RoundToInt(maxHPF));
    }

    // Added helper method for City.cs usage
    public CombatUnit InitializeAndReturn(CombatUnitData unitData, Civilization unitOwner, int tileIndex)
    {
        Initialize(unitData, unitOwner);
        // Position unit on the appropriate tile with proper surface orientation
        // Ensure grid is initialized before calling PositionUnitOnSurface
        if (grid == null)
        {
            // Resolve with diagnostics similar to Awake.
            PlanetGenerator resolved2 = null;
            if (owner != null)
            {
                try { resolved2 = owner.GetPlanetGeneratorForIndex(planetIndex); } catch { resolved2 = null; }
                if (resolved2 == null)
                    Debug.LogWarning($"[CombatUnit] InitializeAndReturn: owner '{owner.civData?.civName ?? owner.name}' returned null for planet {planetIndex}; falling back to GameManager.");
            }
            if (resolved2 == null)
            {
                var gm2 = GameManager.Instance;
                if (gm2 != null)
                {
                    resolved2 = gm2.GetPlanetGenerator(planetIndex);
                    if (resolved2 == null)
                    {
                        Debug.LogWarning($"[CombatUnit] InitializeAndReturn: GameManager has no generator for planetIndex {planetIndex}; falling back to current planet generator.");
                        resolved2 = gm2.GetCurrentPlanetGenerator();
                    }
                }
                else
                {
                    Debug.LogWarning("[CombatUnit] InitializeAndReturn: GameManager.Instance is null; cannot resolve PlanetGenerator.");
                }
            }
            planet = resolved2 ?? planet;
            if (planet != null)
            {
                grid = planet.Grid;
            }
        }

        if (grid != null)
        {
            PositionUnitOnSurface(grid, tileIndex);
            currentTileIndex = tileIndex; // Make sure to set the currentTileIndex
        }
        else
        {
            Debug.LogError($"CombatUnit {gameObject.name} could not find HexGrid to position itself on tile {tileIndex}.");
            return null;
        }
        return this;
    }

    /// <summary>
    /// Restore saved runtime state after Initialize has been called.
    /// Used by the save/load system to re-apply experience, health, level, etc.
    /// </summary>
    public void RestoreState(int savedHealth, int savedExperience, int savedLevel, bool savedHasActed, TileLayer savedLayer)
    {
        currentHealth = Mathf.Clamp(savedHealth, 0, MaxHealth);
        experience = savedExperience;
        level = Mathf.Max(1, savedLevel);
        hasActedThisTurn = savedHasActed;
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

    /// <summary>
    /// Properly positions and orients the unit on the planet surface
    /// </summary>
    /// <summary>
    /// Properly positions and orients the unit on the planet surface
    /// </summary>
    public void PositionUnitOnSurface(HexGrid G, int tileIndex) // Renamed parameter to avoid conflict
    {
        // Flat-only placement: place on terrain surface with proper height
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null) return;
        Vector3 flatCenter = ts.GetTileSurfacePosition(tileIndex);
        transform.position = flatCenter;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        currentTileIndex = tileIndex;

        // Register with tile occupancy so tile-based selection works
        RegisterOccupancy(tileIndex);
    }

    // currentTileIndex, moveSpeed are inherited from BaseUnit
    
    // CombatUnit-specific animation hashes (base hashes like isWalkingHash, attackHash, etc. are in BaseUnit)
    private static readonly int isIdleHash = Animator.StringToHash("IsIdle");
    private static readonly int rangedAttackHash = Animator.StringToHash("RangedAttack");

    /// <summary>
    /// Override BaseUnit.UpdateWalkingState — sets IsWalking and syncs IsIdle.
    /// This is the ONLY path for walking animation changes.
    /// </summary>
    public override void UpdateWalkingState(bool walking)
    {
        base.UpdateWalkingState(walking); // sets isMoving + IsWalking on animator

        // Sync IsIdle (opposite of walking)
        if (animator != null && HasParameter(animator, isIdleHash))
        {
            SetAnimatorBoolForFormation(isIdleHash, !walking);
        }
    }

    // Event fired when multi-tile move finishes
    public event System.Action OnMovementComplete;

    // MoveTo uses BaseUnit.MoveTo (canonical multi-turn path logic)

    /// <summary>
    /// Resets at start of turn.
    /// </summary>
    public override void ResetForNewTurn()
    {
        hasActedThisTurn = false;

        // Base resets (move points, AP, winter penalties)
        RestoreMovePointsForNewTurn();
        ResetAttackPointsForNewTurn();

        // Warfare depth systems (morale recovery, fatigue recovery, status effect ticks)
        ProcessWarfareSystems();

        // If trapped, decrement duration (trappedTurnsRemaining is in BaseUnit)
        if (IsTrapped)
        {
            trappedTurnsRemaining = Mathf.Max(0, trappedTurnsRemaining - 1);
        }

        // Check for damage from hazardous biomes
        CheckForHazardousBiomeDamage();
        ApplyMosquitoDamageIfNeeded(data != null ? data.unitName : UnitName);
    }

    /// <summary>
    /// Checks if the unit is on a hazardous biome and applies damage if needed
    /// </summary>
    private void CheckForHazardousBiomeDamage()
    {
        if (currentTileIndex < 0) return;
        
        // Units in orbit are above the surface — not affected by surface biome hazards
        if (IsInOrbit) return;
        
        // Get tile data
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var tileData = ts != null ? ts.GetTileData(currentTileIndex) : null;
        if (tileData == null) return;
        
        if (TryGetEnvironmentalDamagePercent(tileData, out float damagePercent))
        {
            int damageAmount = Mathf.CeilToInt(MaxHealth * damagePercent);
            
            // Apply damage
            ApplyDamage(damageAmount);
            
            // Notify player if this is their unit
            if (owner != null && owner.isPlayerControlled && UIManager.Instance != null)
            {
                UIManager.Instance.ShowNotification($"{data.unitName} took {damageAmount} damage from {tileData.biome} terrain!");
            }
        }
    }

    // Movement points removed
    
    /// <summary>
    /// Mark this unit as having consumed its action for the turn.
    /// Called by orbit entry/exit and similar turn-consuming actions.
    /// </summary>
    public void ConsumeAction()
    {
        hasActedThisTurn = true;
    }

    /// <summary>
    /// Safely trigger the OnMovementComplete event from external systems
    /// </summary>
    public void TriggerMovementComplete()
    {
        OnMovementComplete?.Invoke();
    }

    // CountAdjacentAllies, trappedTurnsRemaining, IsTrapped, ApplyTrap are inherited from BaseUnit
    


    // Transport System Methods

    /// <summary>
    /// Attempts to load a unit into this transport.
    /// </summary>
    /// <param name="unit">The unit to load</param>
    /// <returns>True if successful, false otherwise</returns>
    public bool LoadUnit(CombatUnit unit)
    {
        // Check if this unit is a transport
        if (!data.isTransport)
            return false;
            
        // Check if transport is at capacity
        if (transportedUnits.Count >= data.transportCapacity)
            return false;
            
        // Check if unit belongs to same owner
        if (unit.owner != owner)
            return false;
            
        // Check if unit is adjacent or on same tile
        bool isAdjacent = false;
        if (unit.currentTileIndex == currentTileIndex)
        {
            isAdjacent = true;
        }
        else
        {
            var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
            int[] neighbors = ts != null ? ts.GetNeighbors(currentTileIndex) : null;
            foreach (int neighbor in neighbors)
            {
                if (unit.currentTileIndex == neighbor)
                {
                    isAdjacent = true;
                    break;
                }
            }
        }
        
        if (!isAdjacent)
            return false;
            
        // Load the unit
        transportedUnits.Add(unit);
        
        // Update the loaded unit's state
        unit.IsTransported = true;
        unit.TransportingUnit = this;
        unit.OnLoadedIntoTransport.Invoke(this);
        
        // Hide the unit visually
        unit.gameObject.SetActive(false);
        
        // Fire event for UI updates
        OnUnitLoaded.Invoke(unit);
        
        return true;
    }
    
    /// <summary>
    /// Unloads a transported unit to a specific tile.
    /// </summary>
    /// <param name="unit">The unit to unload</param>
    /// <param name="targetTileIndex">The tile to unload to</param>
    /// <returns>True if successful, false otherwise</returns>
    public bool UnloadUnit(CombatUnit unit, int targetTileIndex)
    {
        // Check if the unit is being transported by this transport
        if (!transportedUnits.Contains(unit))
            return false;
            
        // Check if target tile is adjacent or the same tile
        bool isValidTile = false;
        if (targetTileIndex == currentTileIndex)
        {
            isValidTile = true;
        }
        else
        {
            var tsCheck = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
            int[] neighbors = tsCheck != null ? tsCheck.GetNeighbors(currentTileIndex) : null;
            if (neighbors != null)
            {
                foreach (int neighbor in neighbors)
                {
                    if (targetTileIndex == neighbor)
                    {
                        isValidTile = true;
                        break;
                    }
                }
            }
        }
        
        if (!isValidTile)
            return false;
            
        // Check if the unit can move to the target tile
        if (!unit.CanMoveTo(targetTileIndex))
            return false;
            
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null) {
            Debug.LogError("[CombatUnit] TileSystem not ready; cannot unload unit in flat-only mode.");
            return false;
        }

        // Update tile occupancy using layered occupancy manager
        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        try
        {
            if (occ != null && !occ.TrySetOccupant(targetTileIndex, unit.gameObject, unit.currentLayer))
                return false;
        }
        catch { return false; }

        // Remove from transport only after the destination claim succeeds.
        transportedUnits.Remove(unit);
        
        // Update the unloaded unit's state
        unit.IsTransported = false;
        unit.TransportingUnit = null;
        unit.OnUnloadedFromTransport.Invoke(this);
        
        // Position the unit at the target tile and show it
        unit.gameObject.SetActive(true);
        unit.transform.position = ts.GetTileSurfacePosition(targetTileIndex);
        unit.currentTileIndex = targetTileIndex;

    // Trigger trap if unloading onto a trapped tile
    ImprovementManager.Instance?.NotifyUnitEnteredTile(targetTileIndex, unit);
        
        // Fire event for UI updates
        OnUnitUnloaded.Invoke(unit);
        
        return true;
    }
    
    /// <summary>
    /// Gets a list of all units currently transported.
    /// </summary>
    public List<CombatUnit> GetTransportedUnits()
    {
        return new List<CombatUnit>(transportedUnits);
    }
    
    /// <summary>
    /// Checks if this transport has capacity for more units.
    /// </summary>
    public bool HasRemainingCapacity()
    {
        return data.isTransport && transportedUnits.Count < data.transportCapacity;
    }
    
    /// <summary>
    /// Gets the number of units that can still be loaded.
    /// </summary>
    public int GetRemainingCapacity()
    {
        if (!data.isTransport) return 0;
        return data.transportCapacity - transportedUnits.Count;
    }
    
    // END Transport System Methods

    // NEW EQUIPMENT METHODS
    
    // CountAdjacentAllies is inherited from BaseUnit

    // Transport System Methods
    // ... (keeping these as they are CombatUnit-specific) ...

    // NEW EQUIPMENT METHODS
    
    /// <summary>
    /// Equips an item in the appropriate slot based on its type
    /// </summary>
    public override void EquipItem(EquipmentData equipmentData)
    {
        if (equipmentData == null) return;
        
        bool changed = false;
        
        // Store in the specific slot based on type
        switch (equipmentData.equipmentType)
        {
            case EquipmentType.Weapon:
                            // Decide whether this weapon should occupy the projectile slot or the main weapon slot (melee uses the main weapon).
                            if (equipmentData.projectileData != null)
                            {
                                if (equippedProjectileWeapon != equipmentData)
                                {
                                    // Use reflection to set the protected property in BaseUnit
                                    var prop = typeof(BaseUnit).GetProperty("equippedProjectileWeapon", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                                    if (prop != null) prop.SetValue(this, equipmentData);
                                    changed = true;
                                }
                            }
                            else
                            {
                                if (equippedWeapon != equipmentData)
                                {
                                    equippedWeapon = equipmentData;
                                    changed = true;
                                }
                            }
                break;
            case EquipmentType.Shield:
                if (equippedShield != equipmentData)
                {
                    equippedShield = equipmentData;
                    changed = true;
                }
                break;
            case EquipmentType.Armor:
                if (equippedArmor != equipmentData)
                {
                    equippedArmor = equipmentData;
                    changed = true;
                }
                break;
            case EquipmentType.Miscellaneous:
                if (equippedMiscellaneous != equipmentData)
                {
                    equippedMiscellaneous = equipmentData;
                    changed = true;
                }
                break;
        }

        // Base property equipped is inherited
        var equippedProp = typeof(BaseUnit).GetProperty("equipped", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        if (equippedProp != null) equippedProp.SetValue(this, equipmentData);
        
        if (changed)
        {
            UpdateEquipmentVisuals();
            
            // Recalculate stats that might be affected by equipment
            RecalculateStats();
            
            RaiseEquipmentChanged();
        }
    }

    /// <summary>
    /// Centralized method to update ALL equipment visuals.
    /// Clears existing equipment GameObjects and reinstantiates them based on current data.
    /// </summary>
    public override void UpdateEquipmentVisuals()
    {
        // Animals don't use equipment; skip any equipment processing or editor logs for them.
        if (data != null && data.unitType == CombatCategory.Animal)
        {
            // Quietly destroy any lingering equipment visuals
            foreach (var item in equippedItemObjects.Values)
            {
                if (item != null)
                {
                    #if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEngine.Object.DestroyImmediate(item); else
                    #endif
                    EquipmentVisualPool.Release(item);
                }
            }
            equippedItemObjects.Clear();
            return;
        }

        // Clean up all existing equipment GameObjects from the dictionaries
        foreach (var item in equippedItemObjects.Values)
        {
            if (item != null)
            {
                #if UNITY_EDITOR
                if (!Application.isPlaying) UnityEngine.Object.DestroyImmediate(item); else
                #endif
                EquipmentVisualPool.Release(item);
            }
        }
        equippedItemObjects.Clear();

        foreach (var item in extraEquippedItemObjects.Values)
        {
            if (item != null)
            {
                #if UNITY_EDITOR
                if (!Application.isPlaying) UnityEngine.Object.DestroyImmediate(item); else
                #endif
                EquipmentVisualPool.Release(item);
            }
        }
        extraEquippedItemObjects.Clear();

        // Process each slot
        ProcessEquipmentSlot(EquipmentType.Weapon, equippedWeapon, weaponHolder);
        
        // PROJECTILE WEAPON VISUALS:
        // Projectile weapons usually have separate holders or visuals
        ProcessEquipmentSlot(EquipmentType.Weapon, equippedProjectileWeapon, projectileWeaponHolder);
        
        ProcessEquipmentSlot(EquipmentType.Shield, equippedShield, shieldHolder);
        ProcessEquipmentSlot(EquipmentType.Armor, equippedArmor, armorHolder);
        ProcessEquipmentSlot(EquipmentType.Miscellaneous, equippedMiscellaneous, miscHolder);

        // Distribute equipment to additional soldiers in the group
        DistributeEquipmentToSoldiers();
    }

    protected override void ProcessEquipmentSlot(EquipmentType type, EquipmentData itemData, Transform holder)
    {
        if (holder == null) return;

        // Clear existing children
        for (int i = holder.childCount - 1; i >= 0; i--)
        {
            var child = holder.GetChild(i);
            if (child != null)
            {
                #if UNITY_EDITOR
                if (!Application.isPlaying) UnityEngine.Object.DestroyImmediate(child.gameObject); else
                #endif
                {
                    if (EquipmentVisualPool.IsPooledInstance(child.gameObject))
                        EquipmentVisualPool.Release(child.gameObject);
                    else
                        Destroy(child.gameObject);
                }
            }
        }

        if (itemData == null) return;

        UpdateEquipmentSlot(type, itemData, holder);
    }

    protected override void UpdateEquipmentSlot(EquipmentType type, EquipmentData itemData, Transform holder)
    {
        if (holder == null || itemData == null || itemData.equipmentPrefab == null) return;

        // Acquire (pooled in play mode)
        GameObject equipObj =
        #if UNITY_EDITOR
            (!Application.isPlaying) ? Instantiate(itemData.equipmentPrefab) :
        #endif
            EquipmentVisualPool.Acquire(itemData.equipmentPrefab);
        
        // CombatUnit-specific attachment logic
        Quaternion authoredLocal = equipObj.transform.localRotation;
        equipObj.transform.SetParent(holder, false);
        equipObj.transform.localPosition = Vector3.zero;
        equipObj.transform.localRotation = authoredLocal;

        // Enable renderers
        var renderers = equipObj.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r != null && !r.enabled) r.enabled = true;
        }

        // Store reference
        if (holder == projectileWeaponHolder)
        {
            extraEquippedItemObjects["Projectile"] = equipObj;
        }
        else
        {
            equippedItemObjects[type] = equipObj;
        }
    }

    // SpawnProjectileFromEquipment needs to be overridden to handle CombatUnit specific target tracking
    public override void SpawnProjectileFromEquipment(EquipmentData equipment, Vector3 targetPosition, CombatUnit targetUnit = null, int overrideDamage = -1)
    {
        base.SpawnProjectileFromEquipment(equipment, targetPosition, targetUnit, overrideDamage);
    }

    // HasParameter, trappedTurnsRemaining, IsTrapped, ApplyTrap are inherited from BaseUnit


    private bool HasEnemyAdjacent()
    {
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null) return false;
        if (currentTileIndex < 0) return false;

        // Check this tile and neighbours for enemy occupants
        var tileData = ts.GetTileData(currentTileIndex);
        if (tileData == null) return false;

        List<int> tilesToCheck = new List<int> { currentTileIndex };
        var neighbours = ts.GetNeighbors(currentTileIndex);
        if (neighbours != null) tilesToCheck.AddRange(neighbours);

        foreach (int idx in tilesToCheck)
        {
            var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
            GameObject obj = occ != null ? occ.GetOccupantObjectWithFallback(idx, currentLayer) : null;
            if (obj == null) continue;
            if (obj == null) continue;
            var unit = obj.GetComponent<CombatUnit>();
            if (unit == null) continue;
            if (unit.owner != this.owner) return true;
        }
        return false;
    }

    // Equip UX helpers
    [ContextMenu("Equip Melee Weapon (Editor)")]
    public void EquipMeleeWeaponEditor()
    {
    if (data == null || data.defaultWeapon == null) return;
    EquipMeleeWeapon(data.defaultWeapon);
    }

    public void EquipMeleeWeapon(EquipmentData weapon)
    {
        if (weapon == null) return;
    _equippedWeapon = weapon;
        UpdateEquipmentVisuals();
        RecalculateStats();
        RaiseEquipmentChanged();
    }

    [ContextMenu("Equip Projectile Weapon (Editor)")]
    public void EquipProjectileWeaponEditor()
    {
        if (data == null || data.defaultProjectileWeapon == null) return;
        EquipProjectileWeapon(data.defaultProjectileWeapon);
    }

    public void EquipProjectileWeapon(EquipmentData weapon)
    {
        if (weapon == null) return;
        _equippedProjectileWeapon = weapon;
        UpdateEquipmentVisuals();
        RecalculateStats();
        RaiseEquipmentChanged();
    }

    // SpawnProjectileFromEquipment, QueueProjectileForAnimation, FireQueuedProjectile, CancelQueuedProjectile
    // are overridden/inherited via BaseUnit
    
    // HasParameter is inherited from BaseUnit
    
    /// <summary>
    /// Public method to trigger animations from external classes
    /// Note: Triggers should not be used for idle/walking states - use isMoving property instead
    /// </summary>
    public void TriggerAnimation(string animationName)
    {
        if (animator != null)
        {
            // Map common animation names to hashes for consistency
            int triggerHash = -1;
            switch (animationName)
            {
                case "Attack":
                case "attack":
                    triggerHash = attackHash;
                    break;
                case "Hit":
                case "hit":
                    triggerHash = hitHash;
                    break;
                case "Death":
                case "death":
                    triggerHash = deathHash;
                    break;
                case "RangedAttack":
                    triggerHash = rangedAttackHash;
                    break;
            }
            
            // Use hash if available, otherwise use string (for custom animations)
            if (triggerHash != -1 && HasParameter(animator, triggerHash))
            {
                SetAnimatorTriggerForFormation(triggerHash);
            }
            else if (triggerHash != -1)
            {
                Debug.LogWarning($"[CombatUnit] {gameObject.name}: TriggerAnimation({animationName}) - hash found but parameter doesn't exist in animator");
                // Fallback to string-based trigger
                animator.SetTrigger(animationName);
                if (soldierGroup != null)
                    soldierGroup.ForwardTrigger(Animator.StringToHash(animationName));
            }
            else
            {
                // Fallback to string-based trigger for custom animations
                animator.SetTrigger(animationName);
                if (soldierGroup != null)
                    soldierGroup.ForwardTrigger(Animator.StringToHash(animationName));
            }
            
            OnAnimationTrigger?.Invoke(animationName);
        }
        else
        {
            Debug.LogWarning($"[CombatUnit] {gameObject.name}: TriggerAnimation({animationName}) called but animator is null");
        }
    }
    
    /// <summary>
    /// Set walking state explicitly (for battle movement)
    /// </summary>
    public void SetWalkingState(bool walking)
    {
        UpdateWalkingState(walking);
    }
    
    /// <summary>
    /// Removes equipment from a specific slot
    /// </summary>
    public virtual void UnequipItem(EquipmentType type)
    {
        bool changed = false;
        
        switch (type)
        {
            case EquipmentType.Weapon:
                if (equippedWeapon != null)
                {
                    equippedWeapon = null;
                    changed = true;
                }
                break;
            case EquipmentType.Shield:
                if (equippedShield != null)
                {
                    equippedShield = null;
                    changed = true;
                }
                break;
            case EquipmentType.Armor:
                if (equippedArmor != null)
                {
                    equippedArmor = null;
                    changed = true;
                }
                break;
            case EquipmentType.Miscellaneous:
                if (equippedMiscellaneous != null)
                {
                    equippedMiscellaneous = null;
                    changed = true;
                }
                break;
        }
        
        if (changed)
        {
            // Use the centralized visual update system for consistency
            UpdateEquipmentVisuals();
            
            // Recalculate stats
            RecalculateStats();
            
            // Notify listeners
            RaiseEquipmentChanged();
        }
    }

    private void HandleMovementCompleted(GameEventManager.UnitMovementEventArgs args)
    {
        if (args.Unit == this)
        {
            // Safety net: ensure walking animation stops when movement completes
            isMoving = false;
        }
    }

    private void HandleCombatStarted(GameEventManager.CombatEventArgs args)
    {
        if (args.Defender == this)
        {
            // Handle being attacked
        }
    }

    private void HandleDamageApplied(GameEventManager.CombatEventArgs args)
    {
        if (args.Defender == this)
        {
            // Handle damage taken
            OnHealthChanged?.Invoke(currentHealth, MaxHealth);
        }
    }

    private void UpdateUnitLabelHealth(int newHealth, int maxHealth)
    {
        if (unitLabelInstance != null)
        {
            string ownerName = owner != null && owner.civData != null ? owner.civData.civName : "Unknown";
            unitLabelInstance.UpdateLabel(data.unitName, ownerName, newHealth, maxHealth);
        }
    }

    // Called by Civilization when civ-wide bonuses (tech/culture) change.
    // Intentionally does not refill movement or attack points mid-turn.
    public void OnCivBonusesChanged()
    {
        // Clamp current health to new max if modifiers decreased it; keep current otherwise.
        int before = currentHealth;
        int max = MaxHealth; // property already includes tech/culture/equipment
        currentHealth = Mathf.Min(currentHealth, max);
        if (currentHealth != before)
        {
            OnHealthChanged?.Invoke(currentHealth, max);
        }
        // Movement/AP maximums might increase due to tech, but we don't refill here;
        // they'll be applied on next ResetForNewTurn via RecalculateStats.
    }

    // Trap mechanics (trappedTurnsRemaining, IsTrapped, ApplyTrap) are inherited from BaseUnit

    /// <summary>
    /// Handle mouse clicks on the combat unit
    /// </summary>
    void OnMouseDown()
    {
        // More precise UI check - only block if actually clicking on UI element (not just any GameObject)
        if (UnityEngine.EventSystems.EventSystem.current != null)
        {
            // Check if we're clicking on a UI element that should block selection
            var pointerData = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
            pointerData.position = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            var results = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
            UnityEngine.EventSystems.EventSystem.current.RaycastAll(pointerData, results);
            
            // Only block if we hit an actual UI element with a Graphic component (like buttons, panels, etc.)
            // Don't block if we hit the unit's own UI label (which should allow clicking through to the unit)
            bool shouldBlock = false;
            foreach (var result in results)
            {
                // Check if this is a UI element we should block clicks on
                if (result.gameObject.GetComponent<UnityEngine.UI.Graphic>() != null)
                {
                    // Check if it's part of the unit's own UI (like UnitLabel) - if so, don't block
                    var unitLabel = result.gameObject.GetComponentInParent<UnitLabel>();
                    if (unitLabel == null)
                    {
                        // It's a UI element that's not part of the unit's UI - block the click
                        shouldBlock = true;
                        break;
                    }
                }
            }
            
            if (shouldBlock)
            {
                // Click was on UI, ignore silently (don't spam logs)
                return;
            }
        }
        
        // Use the UnitSelectionManager for individual unit selection (Civ5-style)
        if (UnitSelectionManager.Instance != null)
        {
            UnitSelectionManager.Instance.SelectUnit(this);
        }
        else
        {
            // Fallback to old behavior if UnitSelectionManager is not available
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowUnitInfoPanelForUnit(this);

                // Fallback notification if UnitInfoPanel is not available
                if (UIManager.Instance.unitInfoPanel == null || !UIManager.Instance.unitInfoPanel.activeInHierarchy)
                {
                    string msg = $"{data.unitName} (Combat)\nHealth: {currentHealth}/{MaxHealth}\nAttack: {CurrentAttack}  Defense: {CurrentDefense}";
                    UIManager.Instance.ShowNotification(msg);
                }
            }
        }
    }


    /// <summary>
    /// Stagger the animation start time so units don't all animate in sync
    /// This creates a more natural, organic look for formations
    /// </summary>
    private void StaggerAnimationStart()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        
        // Get a random offset between 0 and 1 (full animation cycle)
        float randomOffset = UnityEngine.Random.Range(0f, 1f);
        
        // Apply offset to the current animation state
        // This works by playing the current state at a random normalized time
        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        if (stateInfo.length > 0)
        {
            // Play the current state at a random point in the animation
            animator.Play(stateInfo.fullPathHash, 0, randomOffset);
            
            // Also add slight speed variation (95-105%) for even more natural look
            float speedVariation = UnityEngine.Random.Range(0.95f, 1.05f);
            animator.speed = speedVariation;
        }
    }


}