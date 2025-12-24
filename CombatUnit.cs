using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using TMPro;
using GameCombat;

/// <summary>
/// Unit states during battle
/// </summary>
public enum BattleUnitState
{
    Idle,           // Standing still
    Moving,         // Moving to position
    Attacking,      // Engaging enemy
    Defending,      // Holding position
    Routing,        // Fleeing from battle
    Dead            // Unit eliminated
}

public class CombatUnit : BaseUnit
{
    [Header("Stats (Override Data Asset)")]
    [SerializeField] private int attack = 0;
    [SerializeField] private int defense = 0;
    [SerializeField] private int health = 0; 
    [SerializeField] private float range = 0;
    [SerializeField] private int morale = 0;
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
    protected override float MeleeEngageDuration => data?.meleeEngageDuration ?? 8f;
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
    
    // Throttle melee range checks for performance (check every 0.3 seconds)
    private float lastMeleeRangeCheck = 0f;
    private const float MELEE_RANGE_CHECK_INTERVAL = 0.3f;
    private const float MELEE_ENGAGEMENT_RANGE = 2.5f; // Distance to consider "in melee range"

    [field: SerializeField] public CombatUnitData data { get; private set; }  // Now serializable and assignable in Inspector
    // owner, currentHealth are inherited from BaseUnit

    // Remove old events and use GameEventManager instead
    public event System.Action OnDeath;
    public event System.Action<int,int> OnHealthChanged;      // (newHealth, maxHealth)
    public event System.Action<string> OnAnimationTrigger;    // (triggerName)
    public event System.Action<int,int> OnMoraleChanged;
    // OnEquipmentChanged is inherited from BaseUnit

    // equipped, unlockedAbilities, currentHealth are inherited from BaseUnit

    // CombatUnit-specific runtime stats
    public int experience { get; private set; }
    public int level { get; private set; }
    public int currentMorale { get; private set; }
    // Fatigue system (0-100, where 100 = completely exhausted)
    public float currentFatigue { get; private set; }
    // Ammunition system (for ranged units)
    public int currentAmmo { get; private set; }
    public bool isOutOfAmmo => data != null && data.isRangedUnit && currentAmmo <= 0;
    
    // takesWeatherDamage, hasWinterPenalty are inherited from BaseUnit
    
    // Performance optimization: track last displayed health to avoid unnecessary UI updates
    private int _lastDisplayedHealth = -1;

    // Battle system fields
    [Header("Battle System")]
    [Tooltip("Current state in battle")]
    public BattleUnitState battleState = BattleUnitState.Idle;
    [Tooltip("Whether this unit is part of the attacker's forces")]
    public bool isAttacker = true;
    [Tooltip("Current target for this unit")]
    public CombatUnit currentTarget;
    
    [Header("Unit Personnel")]
    [Tooltip("Current number of soldiers/people in this unit (represents unit strength)")]
    public int soldierCount = 100; // Default: 100 people per unit
    [Tooltip("Maximum number of soldiers this unit can have")]
    public int maxSoldierCount = 100;
    [Tooltip("Whether this unit is currently garrisoned in a city (for reinforcement bonuses)")]
    public bool isGarrisonedInCity = false;
    
    [Tooltip("Reference to source unit (for soldiers spawned in formations)")]
    public CombatUnit sourceUnit; // Links soldier GameObject to original unit for casualty tracking
    
    [Tooltip("Movement speed in battle")]
    public float battleMoveSpeed = 5f;
    [Tooltip("Attack range in battle")]
    public float battleAttackRange = 3f;
    
    /// <summary>
    /// Get effective movement speed accounting for equipment bonuses and fatigue penalties
    /// Equipment bonuses (like gold boots) make units move faster
    /// </summary>
    public float EffectiveMoveSpeed
    {
        get
        {
            if (data == null) return battleMoveSpeed;
            
            // Start with base speed + equipment movement bonus
            float baseSpeed = battleMoveSpeed + EquipmentMoveBonus;
            
            // Apply fatigue speed penalty (lerp between full speed and reduced speed based on fatigue level)
            float fatigueLevel = currentFatigue / 100f; // 0.0 to 1.0
            float speedMultiplier = Mathf.Lerp(1.0f, 1.0f - data.fatigueSpeedPenalty, fatigueLevel);
            
            // Ensure minimum speed of 0.1 (units should always be able to move, even if very slowly)
            return Mathf.Max(0.1f, baseSpeed * speedMultiplier);
        }
    }
    
    // Routed flag when morale hits zero
    public bool isRouted { get; private set; }
    
    /// <summary>
    /// Set routed state (public method for external systems like formations)
    /// </summary>
    public void SetRouted(bool routed)
    {
        isRouted = routed;
    }

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
        
        // Use GameManager API for multi-planet support
        planet = GameManager.Instance?.GetCurrentPlanetGenerator();
        if (planet != null) grid = planet.Grid;
        UnitRegistry.Register(gameObject);

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
        
        // Initialize soldier count (default 100, can be overridden)
        if (soldierCount == 100 && maxSoldierCount == 100)
        {
            // Only set defaults if not already customized
            soldierCount = 100;
            maxSoldierCount = 100;
        }

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

        // Set health and morale - ensure data is valid before accessing properties
        if (data != null)
        {
            currentHealth = MaxHealth;
            currentMorale = useOverrideStats && morale > 0 ? morale : data.baseMorale;
            
            // Initialize fatigue (0 = fresh, 100 = exhausted)
            currentFatigue = 0f;
            
            // Initialize ammunition (full ammo for ranged units)
            currentAmmo = data.isRangedUnit ? data.maxAmmo : 0;
            
            // Only recalculate stats if data is valid (properties access data)
            RecalculateStats();
        }
        else
        {
            // Fallback if data is null (shouldn't happen but defensive programming)
            currentHealth = 10; // Default health
            currentMorale = 50; // Default morale
            currentFatigue = 0f; // Fresh
            currentAmmo = 0; // No ammo
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
            // Animator controller check removed (no longer needed for debugging)

            // Initialize as not moving (idle state)
            // Let UpdateIdleAnimation() handle setting IsIdle if needed
            _isMoving = false;
            UpdateIdleAnimation();
        }
        else
        {
            Debug.LogWarning($"CombatUnit {gameObject.name} is missing an Animator component.");
        }

        UpdateEquipmentVisuals();

        // Subscribe to events
        GameEventManager.Instance.OnMovementCompleted += HandleMovementCompleted;
        GameEventManager.Instance.OnCombatStarted += HandleCombatStarted;
        GameEventManager.Instance.OnDamageApplied += HandleDamageApplied;

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
    }

    // Base stats, equipment bonuses, and ability modifiers are inherited from BaseUnit or overridden above.


    // Combined stats - UPDATED to include all ability modifiers
    // Local aggregation structs
    private struct UnitAgg { public int attackAdd, defenseAdd, healthAdd, moveAdd, rangeAdd, apAdd, moraleAdd; public float attackPct, defensePct, healthPct, movePct, rangePct, apPct, moralePct; }
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
                        a.rangeAdd += b.rangeAdd; a.moraleAdd += b.moraleAdd;
                        a.attackPct += b.attackPct; a.defensePct += b.defensePct; a.healthPct += b.healthPct;
                        a.rangePct += b.rangePct; a.moralePct += b.moralePct;
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
                        a.rangeAdd += b.rangeAdd; a.moraleAdd += b.moraleAdd;
                        a.attackPct += b.attackPct; a.defensePct += b.defensePct; a.healthPct += b.healthPct;
                        a.rangePct += b.rangePct; a.moralePct += b.moralePct;
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
            
            // Apply fatigue penalty (lerp between full attack and reduced attack based on fatigue level)
            if (data != null && currentFatigue > 0f)
            {
                float fatigueLevel = currentFatigue / 100f; // 0.0 to 1.0
                float penaltyMultiplier = Mathf.Lerp(1.0f, data.fatigueAttackPenalty, fatigueLevel);
                valF *= penaltyMultiplier;
            }
            
            // Apply out-of-ammo penalty for ranged units in melee
            if (data != null && data.isRangedUnit && isOutOfAmmo && data.canSwitchToMelee)
            {
                valF *= data.outOfAmmoMeleePenalty;
            }
            
            // Apply per-target bonuses (if this unit is attacking a specific target, callers may need to apply extra modifiers).
            return Mathf.RoundToInt(valF);
        }
    }
    public override int CurrentDefense
    {
        get
        {
            float valF = BaseDefense + EquipmentDefenseBonus + GetAbilityDefenseModifier();
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
            
            // Apply fatigue penalty (lerp between full defense and reduced defense based on fatigue level)
            if (data != null && currentFatigue > 0f)
            {
                float fatigueLevel = currentFatigue / 100f; // 0.0 to 1.0
                float penaltyMultiplier = Mathf.Lerp(1.0f, data.fatigueDefensePenalty, fatigueLevel);
                valF *= penaltyMultiplier;
            }
            
            return Mathf.RoundToInt(valF);
        }
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

    public int MaxMorale         => useOverrideStats && morale > 0 ? morale : data.baseMorale;
    


    // Only land units can move on land, naval on water
    public override bool CanMoveTo(int tileIndex)
    {
        var tileData = TileSystem.Instance != null ? TileSystem.Instance.GetTileData(tileIndex) : null;
        if(tileData == null || !tileData.isPassable) return false;
        
        // Regular planet rules: water check for naval units
        if (!tileData.isLand)
        {
            switch (data.unitType)
            {
                case CombatCategory.Ship:
                case CombatCategory.Boat:
                case CombatCategory.Submarine:
                case CombatCategory.SeaCrawler:
                    break;
                default:
                    return false;
            }
        }

    // Movement points removed - units can always move (movement speed is now fatigue-based)

        if (tileData.occupantId != 0 && tileData.occupantId != gameObject.GetInstanceID())
            return false;

        return true;
    }

    public override void MoveTo(int targetTileIndex)
    {
        var path = UnitMovementController.Instance.FindPath(currentTileIndex, targetTileIndex);
        if (path == null || path.Count == 0)
            return;

        StopAllCoroutines();
        StartCoroutine(UnitMovementController.Instance.MoveAlongPath(this, path));
    }

    public void MoveAlongPath(List<int> path)
    {
        SphericalHexGrid currentGrid = grid;

        foreach (int idx in path)
        {
            var currentTileData = TileSystem.Instance != null ? TileSystem.Instance.GetTileData(idx) : null;

            // Movement points removed - movement speed is now fatigue-based

            Vector3 pos = TileSystem.Instance != null ? TileSystem.Instance.GetTileCenter(idx) : currentGrid.tileCenters[idx];
            transform.position = pos;

            // Update tile occupancy
            if (TileSystem.Instance != null) TileSystem.Instance.SetTileOccupant(idx, gameObject);
            
            currentTileIndex = idx;
        }

        // Raise movement completed event
        if (path.Count > 0)
        {
            GameEventManager.Instance.RaiseMovementCompletedEvent(this, path[0], path[path.Count - 1], path.Count);
        }
    }

    // ===== COMBAT UNIT VS COMBAT UNIT =====
    
    public bool CanAttack(CombatUnit target)
    {
        if (isRouted) return false; // Routed units cannot attack

        // Target category checks
    bool targetIsAir = target.data.unitType == CombatCategory.Aircraft;
    bool targetIsSpace = target.data.unitType == CombatCategory.Spaceship;
    bool targetIsUnderwater = target.data.unitType == CombatCategory.Submarine || 
                 target.data.unitType == CombatCategory.SeaCrawler;

        // Check specific attack capabilities
        if (targetIsAir && !data.canAttackAir) return false;
        if (targetIsSpace && !data.canAttackSpace) return false;
        if (targetIsUnderwater && !data.canAttackUnderwater) return false;

        // Range check
        float dist = Vector3.Distance(transform.position, target.transform.position);
        return dist <= CurrentRange;
    }
    
    // ===== COMBAT UNIT VS WORKER UNIT =====
    
    /// <summary>
    /// Check if this combat unit can attack a worker unit - NEW!
    /// Combat units can now attack workers (usually one-sided!)
    /// </summary>
    public bool CanAttack(WorkerUnit target)
    {
        if (isRouted) return false;
        if (target == null) return false;
        
        // Range check
        float dist = Vector3.Distance(transform.position, target.transform.position);
        return dist <= CurrentRange;
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
        if (!CanAttack(target)) return;

        // Check if this should trigger a real-time battle
        if (ShouldStartBattle(target))
        {
            StartRealTimeBattle(target);
            return;
        }

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
        activeWeapon = equippedWeapon; // legacy fallback

    // Set battle state to Attacking - IsAttacking bool will handle continuous attack animations
    battleState = BattleUnitState.Attacking;
    
    // For ranged attacks, still use the trigger (one-shot projectile launch animation)
    bool isRangedAttack = activeWeapon != null && activeWeapon.projectileData != null;
    
    // Check ammunition for ranged attacks
    if (isRangedAttack)
    {
        if (data != null && data.isRangedUnit && !ConsumeAmmo())
        {
            // Out of ammo! Can't fire ranged attack
            Debug.Log($"{gameObject.name} is out of ammo! {(data.canSwitchToMelee ? "Switching to melee." : "Cannot attack!")}");
            
            if (!data.canSwitchToMelee)
            {
                // Can't attack at all without ammo
                return;
            }
            // Otherwise, fall through to melee attack (with penalty applied in CurrentAttack)
            isRangedAttack = false;
        }
        else if (isRangedAttack)
        {
            // Has ammo, fire ranged attack
            animator.SetTrigger(rangedAttackHash);
            string triggerName = "RangedAttack";
    OnAnimationTrigger?.Invoke(triggerName);
        }
    }
    // Melee attacks use IsAttacking bool (continuous), not a trigger

        // Tile defense bonus for target (e.g., hills)
        int tileBonus = 0;
        var tileData = TileSystem.Instance != null ? TileSystem.Instance.GetTileData(target.currentTileIndex) : null;
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
        int damage = Mathf.RoundToInt(rawF * dmgMul);

        // Flanking: adjacent allied units give +10% per extra unit
        int flankCount = CountAdjacentAllies(target.currentTileIndex) - 1;
        if (flankCount > 0)
            damage = Mathf.RoundToInt(damage * (1 + 0.1f * flankCount));

        // Elevation advantage: higher attacker gains up to +10%, lower attacker up to -10%
        {
            float elevationDiff = transform.position.y - target.transform.position.y;
            float elevationMultiplier = 1f + Mathf.Clamp(elevationDiff * 0.02f, -0.1f, 0.1f);
            damage = Mathf.Max(0, Mathf.RoundToInt(damage * elevationMultiplier));
        }

    // If the active weapon defines projectile data, either queue or spawn the projectile depending on settings
    if (activeWeapon != null && activeWeapon.projectileData != null)
        {
            if (useAnimationEventForProjectiles)
            {
        QueueProjectileForAnimation(activeWeapon, target.transform.position, target, damage);
                // Projectile will be fired by animation event (FireQueuedProjectile)
                return;
            }
            else
            {
                // Spawn immediately (legacy behaviour)
        SpawnProjectileFromEquipment(activeWeapon, target.transform.position, target, damage);
                return;
            }
        }

    // Melee / instant-hit path: apply damage immediately and provide attacker context so the melee weapon behavior can trigger
    bool targetDies = target.ApplyDamage(damage, this, true);

        if (targetDies)
        {
            ChangeMorale(data.moraleGainOnKill);
        }
        else
        {
            // Counter-attack if target can
            if (target.CanAttack(this))
                target.CounterAttack(this);
        }

        GainExperience(damage);
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

        // Set battle state to Attacking - IsAttacking bool will handle continuous melee attack animations
        battleState = BattleUnitState.Attacking;
        
        // For ranged attacks, still use the trigger (one-shot projectile launch animation)
        bool isRangedAttack = activeWeapon != null && activeWeapon.projectileData != null;
        if (isRangedAttack)
        {
            animator.SetTrigger(rangedAttackHash);
            string triggerName = "RangedAttack";
        OnAnimationTrigger?.Invoke(triggerName);
        }
        // Melee attacks use IsAttacking bool (continuous), not a trigger

        // Combat units fight at advantage against workers (+2 bonus vs non-combatants)
        int combatBonus = 2;
        
        float attackerValue = GetBaseAttackFloat() + combatBonus;
        float defenderValue = target.CurrentDefense;
        
        float rawDamage = Mathf.Max(0f, attackerValue - defenderValue);
        int finalDamage = Mathf.RoundToInt(rawDamage * GetAbilityDamageMultiplier());

        // Flanking bonus
        int flankCount = CountAdjacentAllies(target.currentTileIndex) - 1;
        if (flankCount > 0)
            finalDamage = Mathf.RoundToInt(finalDamage * (1 + 0.1f * flankCount));

        // Elevation advantage
        {
            float elevationDiff = transform.position.y - target.transform.position.y;
            float elevationMultiplier = 1f + Mathf.Clamp(elevationDiff * 0.02f, -0.1f, 0.1f);
            finalDamage = Mathf.Max(0, Mathf.RoundToInt(finalDamage * elevationMultiplier));
        }

        // Handle ranged vs melee
        if (isRangedAttack)
        {
            if (useAnimationEventForProjectiles)
            {
                // Queue projectile (but target is WorkerUnit, not CombatUnit)
                // We'll fire immediately since projectile system expects CombatUnit
                SpawnProjectileTowardsWorker(activeWeapon, target.transform.position, finalDamage);
                GainExperience(finalDamage);
                return;
            }
            else
            {
                SpawnProjectileTowardsWorker(activeWeapon, target.transform.position, finalDamage);
                GainExperience(finalDamage);
                return;
            }
        }

        // Melee attack
        bool targetDied = target.ApplyDamage(finalDamage, this, true);
        
        if (targetDied)
        {
            ChangeMorale(data.moraleGainOnKill);
            GainExperience(finalDamage * 2); // Extra XP for kills
        }
        else
        {
            // Worker can try to fight back (usually futile!)
            if (target.CanAttack(this))
            {
                target.Attack(this);
            }
        }

        GainExperience(finalDamage);
    }
    
    /// <summary>
    /// Helper to spawn projectile towards a worker target position
    /// </summary>
    private void SpawnProjectileTowardsWorker(EquipmentData equipment, Vector3 targetPosition, int damage)
    {
        if (equipment == null || equipment.projectileData == null || equipment.projectileData.projectilePrefab == null)
            return;

        Transform spawn = GetProjectileSpawnTransform(equipment);
        Vector3 startPos = spawn != null ? spawn.position : transform.position;

        GameObject projGO = null;
        if (SimpleObjectPool.Instance != null)
        {
            projGO = SimpleObjectPool.Instance.Get(equipment.projectileData.projectilePrefab, startPos, Quaternion.identity);
        }
        else
        {
            projGO = Instantiate(equipment.projectileData.projectilePrefab, startPos, Quaternion.identity);
            var marker = projGO.GetComponent<PooledPrefabMarker>();
            if (marker == null) marker = projGO.AddComponent<PooledPrefabMarker>();
            marker.originalPrefab = equipment.projectileData.projectilePrefab;
        }

        if (projGO == null) return;

        Projectile proj = projGO.GetComponent<Projectile>();
        if (proj == null)
            proj = projGO.AddComponent<Projectile>();

        // Initialize with null for both gameObject source and transform target
        // The projectile will just fly to the position and deal area damage
        proj.Initialize(equipment.projectileData, startPos, targetPosition, this.gameObject, null, damage);
    }

    /// <summary>
    /// Apply damage to this unit, which reduces its health
    /// </summary>
    /// <param name="damageAmount">Amount of damage to deal</param>
    /// <returns>True if the unit is destroyed by this damage</returns>
    public override bool ApplyDamage(int damageAmount)
    {
        Debug.Log($"[CombatUnit] {gameObject.name} ApplyDamage called: damage={damageAmount}, currentHealth={currentHealth}, MaxHealth={MaxHealth}");
        
        // Play hit animation using trigger (one-shot, not continuous)
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            if (HasParameter(animator, hitHash))
            {
                animator.SetTrigger(hitHash);
                Debug.Log($"[CombatUnit] {gameObject.name} triggered Hit animation");
            }
            else
            {
                Debug.LogWarning($"[CombatUnit] {gameObject.name} - Hit trigger parameter not found in animator!");
            }
        }
        else
        {
            Debug.LogWarning($"[CombatUnit] {gameObject.name} - Animator or controller is null!");
        }
        
        // currentHealth is protected set in BaseUnit, so we can set it directly
        currentHealth -= damageAmount;
        Debug.Log($"[CombatUnit] {gameObject.name} after damage: currentHealth={currentHealth}");
        
        // Raise damage event
        GameEventManager.Instance.RaiseDamageAppliedEvent(null, this, damageAmount);
        
        // Mark animal as recently attacked for predator/prey behavior system
        if (data != null && data.unitType == CombatCategory.Animal && AnimalManager.Instance != null)
        {
            AnimalManager.Instance.MarkAnimalAsAttacked(this);
        }
        
        // Morale penalty proportional to HP lost
        if (data != null) ChangeMorale(-damageAmount * data.moraleLostPerHealth);
        
        if (currentHealth <= 0)
        {
            Die();
            return true;
        }
        
        // Only rout in battle scenes, not on campaign map
        if (currentHealth <= MaxHealth * 0.2f && !isRouted && IsInBattleScene())
        {
            Rout();
        }
        
        if (owner != null && owner.isPlayerControlled && UIManager.Instance != null)
        {
            // Get tile data to show biome in notification
            var tileDataForNotification = TileSystem.Instance != null ? TileSystem.Instance.GetTileData(currentTileIndex) : null;
            if (tileDataForNotification != null)
            {
                UIManager.Instance.ShowNotification($"{data.unitName} took {damageAmount} damage from {tileDataForNotification.biome} terrain!");
            }
        }
        
        return false;
    }

    /// <summary>
    /// Apply damage with context about the attacker. If the attacker is adjacent (melee) then mark this unit as engaged in melee
    /// so it will use its melee weapon. Engagement state is now managed by range checks, not a timer.
    /// </summary>
    public override bool ApplyDamage(int damageAmount, BaseUnit attacker, bool attackerIsMelee)
    {
        if (attackerIsMelee && data != null && data.defaultWeapon != null)
        {
            // Mark engaged in melee - range check will maintain this state
            engagedInMelee = true;
        }

        return ApplyDamage(damageAmount);
    }
    
    // The specific overloads for CombatUnit/WorkerUnit are now covered by the BaseUnit override above.
    
    /// <summary>
    /// Check if unit is currently in a battle scene (not campaign map)
    /// </summary>
    private bool IsInBattleScene()
    {
        // Check if BattleTestSimple exists (indicates we're in a battle scene)
        return BattleTestSimple.Instance != null;
    }
    
    /// <summary>
    /// Set this unit to routed state (reduced effectiveness)
    /// Only works in battle scenes, not on campaign map
    /// </summary>
    private void Rout()
    {
        // Double-check we're in battle before routing
        if (!IsInBattleScene())
        {
            return; // Don't rout on campaign map
        }
        
        isRouted = true;
        // Set battle state to Routing and start retreat
        battleState = BattleUnitState.Routing;
        StartRetreat();
        Debug.Log($"{data.unitName} has been routed!");
    }
    
    /// <summary>
    /// Destroy this unit
    /// </summary>
    protected override void Die()
    {
        // Stop all coroutines including retreat coroutine
        if (retreatCoroutine != null)
        {
            StopCoroutine(retreatCoroutine);
            retreatCoroutine = null;
        }
        StopAllCoroutines();
        
        // Use hash for consistent naming (capitalized to match WorkerUnit)
        // Death animation should play fully - don't clear IsIdle here, let animator handle it
        if (animator != null && HasParameter(animator, deathHash))
            animator.SetTrigger(deathHash);
        
        // Clear IsIdle and IsWalking when dead (death animation should interrupt everything)
        bool hasIsIdle = HasParameter(animator, isIdleHash);
        if (hasIsIdle)
        {
            animator.SetBool(isIdleHash, false);
        }
        if (animator != null && HasParameter(animator, isWalkingHash))
        {
            animator.SetBool(isWalkingHash, false);
        }
        
        // Raise death event
        GameEventManager.Instance.RaiseUnitKilledEvent(null, this, currentHealth);
        
        // Fire local death event for listeners (e.g., AnimalManager)
        OnDeath?.Invoke();
        
        Debug.Log($"{data.unitName} has been destroyed!");
        if (data != null && owner != null) owner.food += data.foodOnKill;
        
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
        // Attack points removed - units can always counter-attack if able
        if (isRouted) return; // Routed units cannot counter-attack

        // Set battle state to Attacking - IsAttacking bool will handle continuous attack animations
        battleState = BattleUnitState.Attacking;
        // No trigger needed for melee counter-attacks - IsAttacking bool handles it
        
        OnAnimationTrigger?.Invoke("attack");

        int tileBonus = 0;
        var tileData = TileSystem.Instance != null ? TileSystem.Instance.GetTileData(attacker.currentTileIndex) : null;
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

        // Flanking for counter-attacks too
        int flankCount = CountAdjacentAllies(attacker.currentTileIndex) - 1;
        if (flankCount > 0)
            damage = Mathf.RoundToInt(damage * (1 + 0.1f * flankCount));

        // Elevation advantage (defender counter-attacking): compare defender (this) vs attacker
        {
            float elevationDiff = transform.position.y - attacker.transform.position.y;
            float elevationMultiplier = 1f + Mathf.Clamp(elevationDiff * 0.02f, -0.1f, 0.1f);
            damage = Mathf.Max(0, Mathf.RoundToInt(damage * elevationMultiplier));
        }

    attacker.ApplyDamage(damage, this, true);
        GainExperience(damage);
    }

    /// <summary>
    /// Adjust morale, clamp [0..max], fire event, and handle low-morale penalties.
    /// </summary>
    private void ChangeMorale(int delta)
    {
        int old = currentMorale;
        currentMorale = Mathf.Clamp(currentMorale + delta, 0, data.baseMorale);
        if (currentMorale != old)
            OnMoraleChanged?.Invoke(currentMorale, data.baseMorale);

        // Check if unit is now routed (only in battle scenes, not campaign map)
        if (!isRouted && currentMorale == 0 && IsInBattleScene())
        {
            // Unit routs: cannot attack, moves randomly away
            isRouted = true;
            // Set battle state to Routing - IsRouting bool will handle continuous routing animations
            battleState = BattleUnitState.Routing;
            // Flee one tile away (or start continuous retreat in battle)
            AttemptFlee();
        }
    }

    // --- Float helpers for combat that include equipment per-target modifiers ---
    private float GetBaseAttackFloat()
    {
        // BaseAttack is int; equipment and abilities may be fractional
        return BaseAttack + EquipmentAttackBonus + GetAbilityAttackModifier();
    }

    private float GetBaseDefenseFloat()
    {
        float val = BaseDefense + EquipmentDefenseBonus + GetAbilityDefenseModifier();
        // Include tile-based improvement defense modifiers
        if (currentTileIndex >= 0)
        {
            var tileData = TileSystem.Instance != null ? TileSystem.Instance.GetTileData(currentTileIndex) : null;
            if (tileData != null)
            {
                val += tileData.improvementDefenseAddCombat;
                val = val * (1f + tileData.improvementDefensePctCombat);
            }
        }
        return val;
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

    /// <summary>
    /// Simple placeholder flee logic: move to a random neighbouring tile (campaign map only).
    /// In battle, routing is handled by StartRetreat() instead.
    /// </summary>
    private void AttemptFlee()
    {
        // Only flee/rout in battle scenes, not on campaign map
        if (!IsInBattleScene())
        {
            return; // Don't rout on campaign map - armies handle defeat there
        }
        
        // If in battle, use battle-specific retreat logic
        if (battleState != BattleUnitState.Idle && battleState != BattleUnitState.Dead)
        {
            StartRetreat();
            return;
        }
        
        // Campaign map routing logic (shouldn't reach here if IsInBattle() check works)
        if (grid == null) return;

        int[] neighbours = TileSystem.Instance != null ? TileSystem.Instance.GetNeighbors(currentTileIndex) : grid.neighbors[currentTileIndex].ToArray();
        if (neighbours == null || neighbours.Length == 0) return;

        // Build a list of viable tiles we can move to
        var candidates = new List<int>();
        foreach (int idx in neighbours)
        {
            if (CanMoveTo(idx))
                candidates.Add(idx);
        }

        if (candidates.Count == 0) return;

        int target = candidates[Random.Range(0, candidates.Count)];
        MoveTo(target);
    }

    public void GainExperience(int xp)
    {
        experience += xp;
        if (level < data.xpToNextLevel.Length && experience >= data.xpToNextLevel[level - 1])
            LevelUp();
    }

    /// <summary>
    /// Called by projectiles or other external systems when this unit's attack caused a kill.
    /// Awards XP and applies morale gains tied to killing a unit.
    /// </summary>
    public void RegisterKillFromProjectile(int damage)
    {
        GainExperience(damage);
        // Use the existing private ChangeMorale method to apply morale gain on kill
        ChangeMorale(data.moraleGainOnKill);
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
            // Update idle animation (which will use IsIdle bool if available)
            // Don't trigger if unit is moving
            if (!_isMoving)
            {
                UpdateIdleAnimation();
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
            // Use GameManager API for multi-planet support
        planet = GameManager.Instance?.GetCurrentPlanetGenerator();
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
            Debug.LogError($"CombatUnit {gameObject.name} could not find SphericalHexGrid to position itself on tile {tileIndex}.");
            return null;
        }
        return this;
    }

    /// <summary>
    /// Properly positions and orients the unit on the planet surface
    /// </summary>
    /// <summary>
    /// Properly positions and orients the unit on the planet surface
    /// </summary>
    public void PositionUnitOnSurface(SphericalHexGrid G, int tileIndex) // Renamed parameter to avoid conflict
    {
        if (G == null)
        {
            Debug.LogError("SphericalHexGrid reference is null in PositionUnitOnSurface.");
            return;
        }

        // FIXED: For civilization units, always use Earth (planet index 0)
        // Check if Earth surface is ready
        var earthPlanet = GameManager.Instance?.GetPlanetGenerator(0);
        if (earthPlanet == null)
        {
            Debug.LogError("Earth planet generator not found for unit positioning!");
            return;
        }
        
        if (!earthPlanet.HasGeneratedSurface)
        {
            Debug.LogError($"Earth planet not ready for unit positioning! HasGeneratedSurface = {earthPlanet.HasGeneratedSurface}");
            return;
        }
        
        // Get the extruded center of the tile in world space on Earth
    Vector3 tileSurfaceCenter = TileSystem.Instance != null ? TileSystem.Instance.GetTileSurfacePosition(tileIndex, 0f, 0) : Vector3.zero; // Force Earth (planet index 0)
        
        // Set unit position directly on the surface
        transform.position = tileSurfaceCenter;

        // FIXED: Calculate proper surface normal pointing AWAY from planet center (toward atmosphere)
        Vector3 surfaceNormal = (tileSurfaceCenter - earthPlanet.transform.position).normalized;
        
        // FIXED: Use a more robust method to calculate forward direction
        // Get a reference direction (north pole direction projected onto the surface)
        Vector3 northDirection = Vector3.up;
        Vector3 tangentForward = Vector3.Cross(Vector3.Cross(surfaceNormal, northDirection), surfaceNormal).normalized;
        
        // If the cross product fails (at poles), use an alternative reference
        if (tangentForward.magnitude < 0.1f)
        {
            Vector3 eastDirection = Vector3.right;
            tangentForward = Vector3.Cross(Vector3.Cross(surfaceNormal, eastDirection), surfaceNormal).normalized;
        }
        
        // Final fallback if still problematic
        if (tangentForward.magnitude < 0.1f)
        {
            tangentForward = Vector3.forward;
        }
        
        // Set rotation so unit stands upright on the surface
        transform.rotation = Quaternion.LookRotation(tangentForward, surfaceNormal);
        currentTileIndex = tileIndex;
        
    }

    private void UpdateIdleAnimation()
    {
        if (animator == null)
        {
            Debug.LogWarning($"[CombatUnit] {gameObject.name}: UpdateIdleAnimation called but animator is null");
            return;
        }
        
        // Check if IsIdle parameter exists (do this once at method level)
        bool hasIsIdleParam = HasParameter(animator, isIdleHash);
        
        // Only update idle animation if unit is not moving, not attacking, AND not playing other animations
        if (_isMoving)
        {
            // If moving, clear IsIdle
            if (hasIsIdleParam)
            {
                animator.SetBool(isIdleHash, false);
            }
            Debug.Log($"[CombatUnit] {gameObject.name}: UpdateIdleAnimation skipped - unit is moving");
            return;
        }
        
        // If attacking, clear IsIdle
        if (battleState == BattleUnitState.Attacking)
        {
            if (hasIsIdleParam)
            {
                animator.SetBool(isIdleHash, false);
            }
            Debug.Log($"[CombatUnit] {gameObject.name}: UpdateIdleAnimation skipped - unit is attacking");
            return;
        }
        
        // Don't set idle if we're in the middle of playing other animations (attack, hit, death, etc.)
        // Check current state - if we're in a non-idle state, don't force idle
        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
        string currentStateName = GetCurrentStateName(animator);
        
        if (currentState.IsName("Attack") ||
            currentState.IsName("Hit") ||
            currentState.IsName("Death") ||
            currentState.IsName("Rout") ||
            currentState.IsName("RangedAttack"))
        {
            Debug.Log($"[CombatUnit] {gameObject.name}: UpdateIdleAnimation skipped - currently playing {currentStateName}");
            return;
        }
        
        // Use IsIdle bool parameter (required for single idle state)
        if (hasIsIdleParam)
        {
            animator.SetBool(isIdleHash, true);
        }
        
        // CRITICAL FIX: Force immediate transition to Idle if not already there
        // This ensures the animation actually plays
        bool isInIdleState = currentState.IsName("Idle") || currentState.IsName("idle");
        bool isTransitioning = animator.IsInTransition(0);
        
        if (!isInIdleState && !isTransitioning)
        {
            // Force immediate transition to Idle state
            try
            {
                animator.CrossFade("Idle", 0.1f, 0);
            }
            catch
            {
                // Idle state might not exist, which is fine if using parameters only
            }
        }
    }
    
    /// <summary>
    /// Helper to get current animator state name for debugging
    /// </summary>
    private string GetCurrentStateName(Animator anim)
    {
        if (anim == null || anim.runtimeAnimatorController == null) return "No Animator";
        
        AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
        // Try to get state name from layer
        if (anim.layerCount > 0)
        {
            AnimatorClipInfo[] clipInfo = anim.GetCurrentAnimatorClipInfo(0);
            if (clipInfo != null && clipInfo.Length > 0)
            {
                return clipInfo[0].clip.name;
            }
        }
        
        // Fallback: check common state names
        if (stateInfo.IsName("Attack")) return "Attack";
        if (stateInfo.IsName("Hit")) return "Hit";
        if (stateInfo.IsName("Death")) return "Death";
        if (stateInfo.IsName("Rout")) return "Rout";
        if (stateInfo.IsName("Idle") || stateInfo.IsName("idle")) return "Idle";
        if (stateInfo.IsName("Walk") || stateInfo.IsName("Walking")) return "Walk";
        
        return $"Unknown (normalizedTime: {stateInfo.normalizedTime:F2})";
    }
    
    /// <summary>
    /// Log all animator parameters for debugging
    /// </summary>
    private void LogAnimatorParameters(Animator anim)
    {
        if (anim == null || anim.runtimeAnimatorController == null) return;
        
        System.Text.StringBuilder paramLog = new System.Text.StringBuilder();
        paramLog.Append($"[CombatUnit] {gameObject.name}: Animator Parameters - ");
        
        foreach (var param in anim.parameters)
        {
            string value = "";
            switch (param.type)
            {
                case AnimatorControllerParameterType.Bool:
                    value = anim.GetBool(param.nameHash).ToString();
                    break;
                case AnimatorControllerParameterType.Int:
                    value = anim.GetInteger(param.nameHash).ToString();
                    break;
                case AnimatorControllerParameterType.Float:
                    value = anim.GetFloat(param.nameHash).ToString("F2");
                    break;
                case AnimatorControllerParameterType.Trigger:
                    value = "Trigger";
                    break;
            }
            paramLog.Append($"{param.name}={value}, ");
        }
        
        Debug.Log(paramLog.ToString());
        
        // Also log transition info
        if (anim.IsInTransition(0))
        {
            AnimatorTransitionInfo transInfo = anim.GetAnimatorTransitionInfo(0);
            Debug.Log($"[CombatUnit] {gameObject.name}: Currently transitioning! normalizedTime: {transInfo.normalizedTime:F2}, duration: {transInfo.duration:F2}");
        }
        else
        {
            AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
            Debug.Log($"[CombatUnit] {gameObject.name}: NOT transitioning. State: {GetCurrentStateName(anim)}, normalizedTime: {stateInfo.normalizedTime:F2}, speed: {stateInfo.speed:F2}");
        }
    }
    
    // currentTileIndex, moveSpeed are inherited from BaseUnit
    
    // CombatUnit-specific animation hashes (base hashes like isWalkingHash, attackHash, etc. are in BaseUnit)
    private static readonly int isIdleHash = Animator.StringToHash("IsIdle");
    private static readonly int isAttackingHash = Animator.StringToHash("IsAttacking");
    private static readonly int isRoutingHash = Animator.StringToHash("IsRouting");
    private static readonly int rangedAttackHash = Animator.StringToHash("RangedAttack");
    
    // Centralized animation state tracking
    private bool _isMoving = false;
    private bool _isInBattle = false;
    
    /// <summary>
    /// Is this unit currently moving? Automatically syncs with animator IsWalking parameter
    /// Hides base class implementation to add animation sync
    /// </summary>
    public new bool isMoving 
    { 
        get => _isMoving;
        set 
        {
            if (_isMoving != value)
            {
                _isMoving = value;
                UpdateWalkingAnimation();
            }
        }
    }
    
    /// <summary>
    /// Is this unit in a battle? Battle movement overrides world map movement
    /// </summary>
    public bool IsInBattle 
    { 
        get => _isInBattle;
        set => _isInBattle = value;
    }

    // Event fired when multi-tile move finishes
    public event System.Action OnMovementComplete;

    // MoveTo is overridden above (line ~610)

    /// <summary>
    /// Resets movement and attack points at start of turn. Also replenishes morale.
    /// </summary>
    public override void ResetForNewTurn()
    {
        // If trapped, decrement duration (trappedTurnsRemaining is in BaseUnit)
        if (IsTrapped)
        {
            trappedTurnsRemaining = Mathf.Max(0, trappedTurnsRemaining - 1);
        }
        
        // Morale replenishment
        int moraleRecovery = 10; // Default minimum recovery
        ChangeMorale(moraleRecovery);
        
        // Clear routed flag if morale is above 0
        if (isRouted && currentMorale > 0)
        {
            isRouted = false;
            // Stop routing animation and return to normal state
            if (battleState == BattleUnitState.Routing)
            {
                battleState = BattleUnitState.Idle;
            }
            // Stop retreat coroutine if running
            if (retreatCoroutine != null)
            {
                StopCoroutine(retreatCoroutine);
                retreatCoroutine = null;
            }
            // Immediately update routing animation to clear IsRouting parameter
            UpdateRoutingAnimation();
        }
            
        // Check for damage from hazardous biomes
        CheckForHazardousBiomeDamage();
    }

    /// <summary>
    /// Checks if the unit is on a hazardous biome and applies damage if needed
    /// </summary>
    private void CheckForHazardousBiomeDamage()
    {
        if (currentTileIndex < 0) return;
        
        // Get tile data
        var tileData = TileSystem.Instance != null ? TileSystem.Instance.GetTileData(currentTileIndex) : null;
        if (tileData == null) return;
        
        // Check if the biome can cause damage
        if (BiomeHelper.IsDamagingBiome(tileData.biome))
        {
            float damagePercent = BiomeHelper.GetBiomeDamage(tileData.biome);
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

    // Movement points removed - movement speed is now fatigue-based
    
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
            int[] neighbors = TileSystem.Instance != null ? TileSystem.Instance.GetNeighbors(currentTileIndex) : null;
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
            int[] neighbors = TileSystem.Instance != null ? TileSystem.Instance.GetNeighbors(currentTileIndex) : null;
            foreach (int neighbor in neighbors)
            {
                if (targetTileIndex == neighbor)
                {
                    isValidTile = true;
                    break;
                }
            }
        }
        
        if (!isValidTile)
            return false;
            
        // Check if the unit can move to the target tile
        if (!unit.CanMoveTo(targetTileIndex))
            return false;
            
        // Remove from transport
        transportedUnits.Remove(unit);
        
        // Update the unloaded unit's state
        unit.IsTransported = false;
        unit.TransportingUnit = null;
        unit.OnUnloadedFromTransport.Invoke(this);
        
        // Position the unit at the target tile and show it
        unit.gameObject.SetActive(true);
    var targetTileData = TileSystem.Instance != null ? TileSystem.Instance.GetTileData(targetTileIndex) : null;
    unit.transform.position = (TileSystem.Instance != null ? TileSystem.Instance.GetTileCenter(targetTileIndex) : (grid != null ? grid.tileCenters[targetTileIndex] : unit.transform.position));
        unit.currentTileIndex = targetTileIndex;
        
        // Update tile occupancy
    if (TileSystem.Instance != null) TileSystem.Instance.SetTileOccupant(targetTileIndex, unit.gameObject);

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
                    Destroy(item);
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
                Destroy(item);
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
                Destroy(item);
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
                Destroy(child.gameObject);
            }
        }

        if (itemData == null) return;

        UpdateEquipmentSlot(type, itemData, holder);
    }

    protected override void UpdateEquipmentSlot(EquipmentType type, EquipmentData itemData, Transform holder)
    {
        if (holder == null || itemData == null || itemData.equipmentPrefab == null) return;

        // Instantiate
        GameObject equipObj = Instantiate(itemData.equipmentPrefab);
        
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
        if (TileSystem.Instance == null) return false;
        if (currentTileIndex < 0) return false;

        // Check this tile and neighbours for enemy occupants
        var tileData = TileSystem.Instance.GetTileData(currentTileIndex);
        if (tileData == null) return false;

        List<int> tilesToCheck = new List<int> { currentTileIndex };
        var neighbours = TileSystem.Instance.GetNeighbors(currentTileIndex);
        if (neighbours != null) tilesToCheck.AddRange(neighbours);

        foreach (int idx in tilesToCheck)
        {
            var tdata = TileSystem.Instance.GetTileData(idx);
            if (tdata == null) continue;
            if (tdata.occupantId == 0) continue;
            var obj = UnitRegistry.GetObject(tdata.occupantId);
            if (obj == null) continue;
            var unit = obj.GetComponent<CombatUnit>();
            if (unit == null) continue;
            if (unit.owner != this.owner) return true;
        }
        return false;
    }

    void Update()
    {
        // Only update every few frames for performance
        if (Time.frameCount % 3 != 0) return;

        // Melee engagement state is now handled by UpdateMeleeEngagementState() based on range checks
        // Old timer-based system removed - engagement is now reactive to actual enemy proximity
        
        // Update fatigue system
        UpdateFatigue();

        // CRITICAL: Update IsAttacking bool based on battle state
        UpdateAttackingAnimation();
        
        // CRITICAL: Update IsRouting bool based on battle state
        UpdateRoutingAnimation();

        // Update unit label only when health changes
        if (unitLabelInstance != null && currentHealth != _lastDisplayedHealth)
        {
            unitLabelInstance.UpdateLabel(data.unitName, owner.civData.civName, currentHealth, MaxHealth);
            _lastDisplayedHealth = currentHealth;
        }
    }
    
    /// <summary>
    /// Update fatigue accumulation and recovery based on unit state
    /// </summary>
    private void UpdateFatigue()
    {
        if (data == null) return;
        
        float deltaTime = Time.deltaTime * 3f; // Compensate for every-3rd-frame update
        
        // Accumulate fatigue based on activity
        if (battleState == BattleUnitState.Attacking)
        {
            // Fighting is very tiring
            currentFatigue = Mathf.Min(100f, currentFatigue + data.fatigueRateFighting * deltaTime);
        }
        else if (battleState == BattleUnitState.Routing || isRouted)
        {
            // Routing is very tiring (running away in panic)
            currentFatigue = Mathf.Min(100f, currentFatigue + data.fatigueRateFighting * deltaTime);
        }
        else if (isMoving)
        {
            // Moving is moderately tiring
            currentFatigue = Mathf.Min(100f, currentFatigue + data.fatigueRateMoving * deltaTime);
        }
        else if (battleState == BattleUnitState.Idle)
        {
            // Resting recovers fatigue
            currentFatigue = Mathf.Max(0f, currentFatigue - data.fatigueRecoveryRate * deltaTime);
        }
    }
    
    /// <summary>
    /// Apply instant fatigue (for charge attacks, forced marches, etc.)
    /// </summary>
    public void ApplyInstantFatigue(float amount)
    {
        currentFatigue = Mathf.Clamp(currentFatigue + amount, 0f, 100f);
    }
    
    /// <summary>
    /// Get fatigue level as a percentage (0.0 = fresh, 1.0 = exhausted)
    /// </summary>
    public float GetFatigueLevel()
    {
        return currentFatigue / 100f;
    }
    
    /// <summary>
    /// Check if unit is fatigued (>50% fatigue)
    /// </summary>
    public bool IsFatigued()
    {
        return currentFatigue > 50f;
    }
    
    /// <summary>
    /// Check if unit is exhausted (>80% fatigue)
    /// </summary>
    public bool IsExhausted()
    {
        return currentFatigue > 80f;
    }
    
    /// <summary>
    /// Consume ammunition for ranged attack
    /// </summary>
    public bool ConsumeAmmo()
    {
        if (data == null || !data.isRangedUnit) return true; // Non-ranged units always have "ammo"
        
        if (currentAmmo <= 0) return false; // Out of ammo
        
        currentAmmo--;
        return true;
    }
    
    /// <summary>
    /// Reload/resupply ammunition (for future resupply mechanics)
    /// </summary>
    public void ResupplyAmmo()
    {
        if (data == null) return;
        currentAmmo = data.maxAmmo;
    }

    /// <summary>
    /// Update IsAttacking animation parameter based on battle state
    /// This creates continuous attack animations while in combat
    /// </summary>
    private void UpdateAttackingAnimation()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        
        // Check if IsAttacking parameter exists
        bool hasIsAttacking = HasParameter(animator, isAttackingHash);
        if (!hasIsAttacking) return; // Parameter doesn't exist, can't update
        
        // Set IsAttacking bool based on battle state
        // Don't attack if routing (routed units can't attack)
        bool shouldBeAttacking = (battleState == BattleUnitState.Attacking && !isRouted);
        animator.SetBool(isAttackingHash, shouldBeAttacking);
        
        // Also sync with IsIdle - can't be idle while attacking
        bool hasIsIdle = HasParameter(animator, isIdleHash);
        if (hasIsIdle && shouldBeAttacking)
        {
            animator.SetBool(isIdleHash, false);
        }
        
        // FALLBACK: If IsAttacking parameter doesn't exist, use CrossFade as backup
        if (!hasIsAttacking && shouldBeAttacking)
        {
            try
            {
                animator.CrossFade("Attack", 0.1f, 0);
            }
            catch
            {
                // Can't play attack animation
            }
        }
    }
    
    /// <summary>
    /// Update melee engagement state based on whether unit is actively in melee combat
    /// Unit is in melee if: attacking AND has a target within melee weapon range
    /// </summary>
    private void UpdateMeleeEngagementState()
    {
        // Only check if unit has a ranged weapon (no need to check for pure melee units)
        if (data == null || equippedProjectileWeapon == null || !data.isRangedUnit)
        {
            // Pure melee unit or no ranged weapon - always use melee
            engagedInMelee = true;
            return;
        }
        
        // Throttle checks for performance
        if (Time.time - lastMeleeRangeCheck < MELEE_RANGE_CHECK_INTERVAL)
        {
            return;
        }
        lastMeleeRangeCheck = Time.time;
        
        // Check if unit is actively in melee combat
        bool isInMeleeCombat = IsInMeleeCombat();
        
        // Update engagement state
        engagedInMelee = isInMeleeCombat;
    }
    
    /// <summary>
    /// Check if unit is actively engaged in melee combat
    /// Returns true if: unit is attacking AND has a target within melee weapon range
    /// </summary>
    private bool IsInMeleeCombat()
    {
        // Must be in attacking state
        if (battleState != BattleUnitState.Attacking)
        {
            return false;
        }
        
        // Must have a current target
        if (currentTarget == null || currentTarget.currentHealth <= 0)
        {
            return false;
        }
        
        // Check if target is within melee weapon range
        // Melee range is typically the base range (without projectile weapon bonuses)
        // For ranged units, melee range is usually 1.5-2.5 units
        float meleeWeaponRange = GetMeleeWeaponRange();
        float distanceToTarget = Vector3.Distance(transform.position, currentTarget.transform.position);
        
        // If target is within melee range, we're in melee combat
        return distanceToTarget <= meleeWeaponRange;
    }
    
    /// <summary>
    /// Get the effective range of the melee weapon
    /// For ranged units, this is typically the base range or equipped melee weapon range
    /// </summary>
    private float GetMeleeWeaponRange()
    {
        // If unit has an equipped melee weapon, use its range
        if (equippedWeapon != null && equippedWeapon.projectileData == null)
        {
            // Melee weapon - use its range bonus or default melee range
            float weaponRange = equippedWeapon.rangeBonus > 0 ? equippedWeapon.rangeBonus : 2.0f;
            return weaponRange;
        }
        
        // Default melee range (for units without explicit melee weapon)
        // This is typically shorter than ranged weapon range
        return 2.0f; // Standard melee engagement distance
    }
    
    /// <summary>
    /// Update IsRouting animation parameter based on battle state
    /// This creates continuous routing animations while fleeing
    /// </summary>
    private void UpdateRoutingAnimation()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        
        // Check if IsRouting parameter exists
        bool hasIsRouting = HasParameter(animator, isRoutingHash);
        if (!hasIsRouting) return; // Parameter doesn't exist, can't update
        
        // Set IsRouting bool based on battle state and routed flag
        bool shouldBeRouting = (battleState == BattleUnitState.Routing || isRouted);
        animator.SetBool(isRoutingHash, shouldBeRouting);
        
        // Also sync with IsIdle and IsAttacking - can't be idle or attacking while routing
        bool hasIsIdle = HasParameter(animator, isIdleHash);
        if (hasIsIdle && shouldBeRouting)
        {
            animator.SetBool(isIdleHash, false);
        }
        
        bool hasIsAttacking = HasParameter(animator, isAttackingHash);
        if (hasIsAttacking && shouldBeRouting)
        {
            animator.SetBool(isAttackingHash, false);
        }
        
        // FALLBACK: If IsRouting parameter doesn't exist, use CrossFade as backup
        if (!hasIsRouting && shouldBeRouting)
        {
            try
            {
                animator.CrossFade("Rout", 0.1f, 0);
            }
            catch
            {
                // Can't play routing animation
            }
        }
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

    [ContextMenu("Validate Equipped Projectile Spawn")]
    public void ValidateEquippedProjectileSpawn()
    {
        if (equippedProjectileWeapon == null)
        {
            Debug.Log("No projectile weapon equipped.");
            return;
        }
        if (!equippedProjectileWeapon.useEquipmentProjectileSpawn)
        {
            Debug.LogWarning($"{equippedProjectileWeapon.equipmentName} does not use equipment spawn transform flag.");
            return;
        }
        var spawn = GetProjectileSpawnTransform(equippedProjectileWeapon);
        if (spawn == null)
            Debug.LogWarning($"Projectile spawn transform '{equippedProjectileWeapon.projectileSpawnName}' not found on equipped projectile weapon.");
        else
            Debug.Log($"Found projectile spawn: {spawn.name}");
    }

    // SpawnProjectileFromEquipment, QueueProjectileForAnimation, FireQueuedProjectile, CancelQueuedProjectile
    // are overridden/inherited via BaseUnit
    
    /// <summary>
    /// Centralized method to update walking animation state
    /// Syncs isMoving property with IsWalking animator parameter
    /// </summary>
    private void UpdateWalkingAnimation()
    {
        if (animator == null)
        {
            Debug.LogWarning($"[CombatUnit] {gameObject.name}: UpdateWalkingAnimation called but animator is null");
            return;
        }
        
        // Check if animator controller is assigned
        if (animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning($"[CombatUnit] {gameObject.name}: UpdateWalkingAnimation called but animator controller is null");
            return;
        }
        
        // Check if IsWalking parameter exists
        bool hasIsWalking = HasParameter(animator, isWalkingHash);
        if (!hasIsWalking)
        {
            Debug.LogWarning($"[CombatUnit] {gameObject.name}: IsWalking parameter does not exist in animator controller");
            return; // Parameter doesn't exist, can't update
        }
        
        // Don't walk if attacking
        // Don't use walking animation if routing (routing has its own animation)
        bool shouldWalk = _isMoving && (battleState != BattleUnitState.Attacking) && (battleState != BattleUnitState.Routing && !isRouted);
        
        // Set IsWalking bool parameter based on _isMoving state
        animator.SetBool(isWalkingHash, shouldWalk);
        
        // Always sync IsIdle with IsWalking (opposite states)
        bool hasIsIdle = HasParameter(animator, isIdleHash);
        if (hasIsIdle)
        {
            animator.SetBool(isIdleHash, !shouldWalk && battleState != BattleUnitState.Attacking && battleState != BattleUnitState.Routing && !isRouted);
        }
        
        // CRITICAL FIX: Force immediate transition if animator isn't responding to parameters
        // This handles cases where animator controller has exit time or other blocking conditions
        if (shouldWalk)
        {
            // Check if we're already in or transitioning to a walk state
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            bool isInWalkState = stateInfo.IsName("Walk") || stateInfo.IsName("Walking") || stateInfo.IsName("walk");
            bool isTransitioning = animator.IsInTransition(0);
            
            // If not walking and not transitioning to walk, force the transition
            if (!isInWalkState && !isTransitioning)
            {
                // Try to play Walk state directly with CrossFade for smooth transition
                try
                {
                    animator.CrossFade("Walk", 0.1f, 0); // 0.1 second blend time
                }
                catch
                {
                    // If "Walk" doesn't exist, try "Walking"
                    try
                    {
                        animator.CrossFade("Walking", 0.1f, 0);
                    }
                    catch
                    {
                        // Could not find walk state - animator controller may not have it set up properly
                    }
                }
            }
        }
    }
    
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
                    // REMOVED: Attack is now controlled by IsAttacking bool, not a trigger
                    // Just set the battle state instead
                    battleState = BattleUnitState.Attacking;
                    return; // Don't set a trigger
                case "Hit":
                case "hit":
                    triggerHash = hitHash;
                    break;
                case "Death":
                case "death":
                    triggerHash = deathHash;
                    break;
                case "Rout":
                case "rout":
                    triggerHash = routHash;
                    break;
                case "RangedAttack":
                    triggerHash = rangedAttackHash;
                    break;
            }
            
            // Use hash if available, otherwise use string (for custom animations)
            if (triggerHash != -1 && HasParameter(animator, triggerHash))
            {
                animator.SetTrigger(triggerHash);
                Debug.Log($"[CombatUnit] {gameObject.name}: TriggerAnimation({animationName}) - using hash, current state: {GetCurrentStateName(animator)}");
            }
            else if (triggerHash != -1)
            {
                Debug.LogWarning($"[CombatUnit] {gameObject.name}: TriggerAnimation({animationName}) - hash found but parameter doesn't exist in animator");
                // Fallback to string-based trigger
            animator.SetTrigger(animationName);
            }
            else
            {
                // Fallback to string-based trigger for custom animations
                animator.SetTrigger(animationName);
                Debug.Log($"[CombatUnit] {gameObject.name}: TriggerAnimation({animationName}) - using string (custom animation), current state: {GetCurrentStateName(animator)}");
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
    /// This overrides world map movement
    /// </summary>
    public void SetWalkingState(bool walking)
    {
        isMoving = walking; // This will automatically update animator via property setter
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
            // Handle any post-movement logic
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
            pointerData.position = Input.mousePosition;
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
        
        // CAMPAIGN MAP: Units are in armies - redirect selection to army
        if (!IsInBattleScene() && ArmyManager.Instance != null)
        {
            // Find which army contains this unit
            var army = ArmyManager.Instance.GetArmyContainingUnit(this);
            if (army != null)
            {
                // Select the army instead of the individual unit
                ArmyManager.Instance.SelectArmy(army);
                return;
            }
            // If unit is not in an army, it will be auto-added by EnforceArmyOnlySystem()
            // For now, just return (unit shouldn't be visible on campaign map anyway)
            return;
        }
        
        // BATTLE MAP: Handle unit selection normally
        var battleTest = FindFirstObjectByType<BattleTestSimple>();
        if (battleTest != null)
        {
            // Unit selection is now handled directly in HandleSelection, but we can still clear formations here
            // This provides a backup in case HandleSelection didn't catch it
            battleTest.ClearSelection();
            
            // Call the SelectUnit method directly
            battleTest.SelectUnitDirectly(this);
            return;
        }
        
        // Fallback if not in battle test context
        // Use the UnitSelectionManager for selection
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
                    string msg = $"{data.unitName} (Combat)\nHealth: {currentHealth}/{MaxHealth}\nAttack: {CurrentAttack}  Defense: {CurrentDefense}\nFatigue: {Mathf.RoundToInt(currentFatigue)}%";
                    UIManager.Instance.ShowNotification(msg);
                }
            }
        }
    }

    /// <summary>
    /// Initialize unit for battle mode
    /// </summary>
    public void InitializeForBattle(bool isAttackerSide)
    {
        isAttacker = isAttackerSide;
        battleState = BattleUnitState.Idle;
        currentTarget = null;
        
        // Mark unit as in battle (prevents world map movement from interfering)
        IsInBattle = true;
        
        // Set up battle-specific components
        SetupBattleComponents();
        
        // Re-stagger animation when entering battle to ensure variety
        StaggerAnimationStart();
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

    /// <summary>
    /// Set the battle state of this unit
    /// </summary>
    public void SetBattleState(BattleUnitState newState)
    {
        // Check if we're transitioning from Routing to a non-routing state
        bool wasRouting = (battleState == BattleUnitState.Routing || isRouted);
        
        battleState = newState;
        
        switch (newState)
        {
            case BattleUnitState.Idle:
                // Stop current actions
                StopAllCoroutines();
                // If we were routing, immediately update animation to clear IsRouting parameter
                if (wasRouting)
                {
                    UpdateRoutingAnimation();
                }
                break;
            case BattleUnitState.Attacking:
                // Look for nearby enemies
                FindNearestEnemy();
                // If we were routing, immediately update animation to clear IsRouting parameter
                if (wasRouting)
                {
                    UpdateRoutingAnimation();
                }
                break;
            case BattleUnitState.Defending:
                // Hold position
                // If we were routing, immediately update animation to clear IsRouting parameter
                if (wasRouting)
                {
                    UpdateRoutingAnimation();
                }
                break;
            case BattleUnitState.Routing:
                // Start retreating
                StartRetreat();
                // Immediately update routing animation
                UpdateRoutingAnimation();
                break;
        }
    }

    /// <summary>
    /// Move to a specific position in battle
    /// </summary>
    public void MoveToPosition(Vector3 targetPosition)
    {
        if (battleState == BattleUnitState.Dead) return;

        battleState = BattleUnitState.Moving;
        StartCoroutine(MoveToPositionCoroutine(targetPosition));
    }

    /// <summary>
    /// Attack a specific target in battle
    /// </summary>
    public void AttackTarget(CombatUnit target)
    {
        if (target == null || battleState == BattleUnitState.Dead) return;

        currentTarget = target;
        battleState = BattleUnitState.Attacking;
        
        // Check if in range
        float distance = Vector3.Distance(transform.position, target.transform.position);
        if (distance <= battleAttackRange)
        {
            // In range, attack immediately
            Attack(target);
        }
        else
        {
            // Move towards target
            StartCoroutine(MoveToTargetCoroutine(target));
        }
    }

    private void SetupBattleComponents()
    {
        // Add battle-specific components if needed
        // This could include battle-specific AI, movement controllers, etc.
    }

    private void FindNearestEnemy()
    {
        CombatUnit nearestEnemy = null;
        float nearestDistance = float.MaxValue;

        // Find all enemy units
        var allUnits = FindObjectsByType<CombatUnit>(FindObjectsSortMode.None);
        foreach (var unit in allUnits)
        {
            if (unit != this && unit.isAttacker != this.isAttacker && unit.battleState != BattleUnitState.Dead)
            {
                float distance = Vector3.Distance(transform.position, unit.transform.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestEnemy = unit;
                }
            }
        }

        if (nearestEnemy != null)
        {
            AttackTarget(nearestEnemy);
        }
    }

    private void StartRetreat()
    {
        // Set battle state to Routing
        battleState = BattleUnitState.Routing;
        
        // Start continuous retreat coroutine (keeps moving away from enemies)
        if (retreatCoroutine != null)
        {
            StopCoroutine(retreatCoroutine);
        }
        retreatCoroutine = StartCoroutine(ContinuousRetreatCoroutine());
    }
    
    private Coroutine retreatCoroutine = null;
    
    /// <summary>
    /// Continuously retreat from enemies (routing animation plays continuously)
    /// </summary>
    private System.Collections.IEnumerator ContinuousRetreatCoroutine()
    {
        while (battleState == BattleUnitState.Routing && isRouted)
        {
            // Find retreat direction away from nearest enemy
            Vector3 retreatDirection = GetRetreatDirection();
            
            // Move away from enemies continuously
            Vector3 newPosition = transform.position + retreatDirection * battleMoveSpeed * Time.deltaTime;
            
            // Clamp to battlefield bounds
            newPosition = ClampToBattlefieldBounds(newPosition);
            
            // Smoothly move to new position
            transform.position = newPosition;
            
            // Face away from enemies (routing direction)
            if (retreatDirection.magnitude > 0.1f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(retreatDirection);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 5f);
            }
            
            yield return null;
        }
        
        retreatCoroutine = null;
    }

    // Cache enemies for retreat direction calculation (updated every 0.5 seconds)
    private static List<CombatUnit> cachedEnemies = new List<CombatUnit>();
    private static float lastEnemyCacheUpdate = 0f;
    private const float ENEMY_CACHE_UPDATE_INTERVAL = 0.5f;
    
    // Cache all units for GetNearbyAlliedUnits (updated every 0.5 seconds)
    private static CombatUnit[] cachedAllUnits;
    private static float lastAllUnitsCacheUpdate = 0f;
    private const float ALL_UNITS_CACHE_UPDATE_INTERVAL = 0.5f;
    
    private Vector3 GetRetreatDirection()
    {
        // Update enemy cache periodically to avoid expensive FindObjectsByType every frame
        if (Time.time - lastEnemyCacheUpdate > ENEMY_CACHE_UPDATE_INTERVAL)
        {
            cachedEnemies.Clear();
            var allUnits = FindObjectsByType<CombatUnit>(FindObjectsSortMode.None);
            foreach (var unit in allUnits)
            {
                if (unit != null && unit.battleState != BattleUnitState.Dead)
                {
                    cachedEnemies.Add(unit);
                }
            }
            lastEnemyCacheUpdate = Time.time;
        }
        
        // Simple retreat logic - move away from nearest enemy
        CombatUnit nearestEnemy = null;
        float nearestDistanceSqr = float.MaxValue;

        foreach (var unit in cachedEnemies)
        {
            if (unit == null || unit == this || unit.isAttacker == this.isAttacker || unit.battleState == BattleUnitState.Dead)
                continue;
                
            float distanceSqr = (transform.position - unit.transform.position).sqrMagnitude;
            if (distanceSqr < nearestDistanceSqr)
            {
                nearestDistanceSqr = distanceSqr;
                nearestEnemy = unit;
            }
        }

        Vector3 retreatDir;
        if (nearestEnemy != null)
        {
            retreatDir = (transform.position - nearestEnemy.transform.position).normalized;
        }
        else
        {
            // Default retreat direction
            retreatDir = isAttacker ? Vector3.left : Vector3.right;
        }
        
        // Ensure retreat direction keeps unit within battlefield bounds
        Vector3 testPosition = transform.position + retreatDir * 10f;
        Vector3 clampedPosition = ClampToBattlefieldBounds(testPosition);
        Vector3 newRetreatDir = (clampedPosition - transform.position).normalized;
        
        // Fix zero vector edge case - if clamped position equals current position, use fallback direction
        if (newRetreatDir.magnitude < 0.1f)
        {
            // Use perpendicular direction or default direction
            retreatDir = isAttacker ? Vector3.left : Vector3.right;
        }
        else
        {
            retreatDir = newRetreatDir;
        }
        
        return retreatDir;
    }
    
    /// <summary>
    /// Clamp a position to stay within battlefield bounds (prevents units from routing off the map)
    /// </summary>
    private Vector3 ClampToBattlefieldBounds(Vector3 position)
    {
        // Get battlefield size from BattleTestSimple if available
        float battlefieldSize = 100f; // Default size
        if (BattleTestSimple.Instance != null)
        {
            battlefieldSize = BattleTestSimple.Instance.battleMapSize;
        }
        
        // Clamp to a square battlefield centered at origin
        // Battlefield extends from -battlefieldSize/2 to +battlefieldSize/2 on X and Z axes
        float halfSize = battlefieldSize * 0.5f;
        float margin = 5f; // Keep units 5 units away from edge
        
        position.x = Mathf.Clamp(position.x, -halfSize + margin, halfSize - margin);
        position.z = Mathf.Clamp(position.z, -halfSize + margin, halfSize - margin);
        
        // Keep Y position (height) unchanged - let it stay on ground
        
        return position;
    }

    private System.Collections.IEnumerator MoveToPositionCoroutine(Vector3 targetPosition)
    {
        Vector3 startPosition = transform.position;
        float distance = Vector3.Distance(startPosition, targetPosition);
        float moveTime = distance / battleMoveSpeed;
        float elapsedTime = 0f;

        while (elapsedTime < moveTime && battleState == BattleUnitState.Moving && !isRouted)
        {
            // Check if unit started routing - if so, stop this movement
            if (battleState == BattleUnitState.Routing || isRouted)
            {
                yield break;
            }
            
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / moveTime;
            transform.position = Vector3.Lerp(startPosition, targetPosition, t);
            yield return null;
        }

        // Only set final position and idle state if we completed movement (not interrupted by routing)
        if (battleState == BattleUnitState.Moving && !isRouted)
        {
            transform.position = targetPosition;
            battleState = BattleUnitState.Idle;
        }
    }

    private System.Collections.IEnumerator MoveToTargetCoroutine(CombatUnit target)
    {
        // Check if target is valid and not dead
        if (target == null || target.battleState == BattleUnitState.Dead)
        {
            currentTarget = null;
            battleState = BattleUnitState.Idle;
            yield break;
        }
        
        bool isChasingRouted = (target.isRouted || target.battleState == BattleUnitState.Routing);
        
        // If chasing routed unit, use Moving state (walking animation)
        // Otherwise use Attacking state
        BattleUnitState chaseState = isChasingRouted ? BattleUnitState.Moving : BattleUnitState.Attacking;
        battleState = chaseState;
        
        while (currentTarget != null && target != null && target.battleState != BattleUnitState.Dead && 
               (battleState == BattleUnitState.Attacking || battleState == BattleUnitState.Moving))
        {
            // Check if we should stop (routing, dead, etc.)
            if (battleState == BattleUnitState.Routing || battleState == BattleUnitState.Dead)
            {
                yield break;
            }
            
            // Update chase state if target routing status changes
            bool targetIsRouted = (target.isRouted || target.battleState == BattleUnitState.Routing);
            if (targetIsRouted != isChasingRouted)
            {
                isChasingRouted = targetIsRouted;
                chaseState = isChasingRouted ? BattleUnitState.Moving : BattleUnitState.Attacking;
                battleState = chaseState;
            }
            
            float distance = Vector3.Distance(transform.position, target.transform.position);
            
            if (distance <= battleAttackRange)
            {
                // In range
                if (targetIsRouted)
                {
                    // Keep chasing routed units (they can't fight back effectively)
                    // Continue moving towards them with walking animation
                    Vector3 direction = (target.transform.position - transform.position).normalized;
                    Vector3 newPosition = transform.position + direction * battleMoveSpeed * Time.deltaTime;
                    transform.position = newPosition;
                }
                else
                {
                    // Target is not routed, attack normally
                    battleState = BattleUnitState.Attacking;
                    Attack(target);
                    yield break;
                }
            }
            else
            {
                // Move towards target (walking animation when chasing routed units, attack animation otherwise)
                Vector3 direction = (target.transform.position - transform.position).normalized;
                Vector3 newPosition = transform.position + direction * battleMoveSpeed * Time.deltaTime;
                transform.position = newPosition;
            }

            yield return null;
        }
    }

    /// <summary>
    /// Check if this attack should trigger a real-time battle
    /// </summary>
    private bool ShouldStartBattle(CombatUnit target)
    {
        // Only start battles for different civilizations or when attacking animals
        if (target == null || owner == null) return false;
        
        // Don't start battles for same civilization (unless it's an animal)
        if (owner == target.owner && target.owner != null) return false;
        
        // Start battle for any engagement between different civilizations or when attacking animals
        return true;
    }

    /// <summary>
    /// Get the battle strength of this unit and nearby allies
    /// </summary>
    private int GetBattleStrength()
    {
        int strength = 1; // This unit counts as 1
        
        // Count nearby allied units
        var nearbyUnits = GetNearbyAlliedUnits(10f); // 10 unit radius
        strength += nearbyUnits.Count;
        
        return strength;
    }

    /// <summary>
    /// Get nearby allied units within range
    /// Uses cached units array to avoid expensive FindObjectsByType call
    /// </summary>
    private List<CombatUnit> GetNearbyAlliedUnits(float range)
    {
        List<CombatUnit> nearbyUnits = new List<CombatUnit>();
        
        // Update cache periodically to avoid expensive FindObjectsByType every call
        if (Time.time - lastAllUnitsCacheUpdate > ALL_UNITS_CACHE_UPDATE_INTERVAL)
        {
            cachedAllUnits = FindObjectsByType<CombatUnit>(FindObjectsSortMode.None);
            lastAllUnitsCacheUpdate = Time.time;
        }
        
        if (cachedAllUnits == null) return nearbyUnits;
        
        foreach (var unit in cachedAllUnits)
        {
            if (unit != this && unit.owner == this.owner)
            {
                float distance = Vector3.Distance(transform.position, unit.transform.position);
                if (distance <= range)
                {
                    nearbyUnits.Add(unit);
                }
            }
        }
        
        return nearbyUnits;
    }

    /// <summary>
    /// Start a real-time battle
    /// </summary>
    private void StartRealTimeBattle(CombatUnit target)
    {
        if (BattleTestSimple.Instance == null)
        {
            Debug.LogWarning("[CombatUnit] BattleManager not found, falling back to normal combat");
            return;
        }

        // Get all nearby units for both sides (including single units)
        List<CombatUnit> attackerUnits = GetNearbyAlliedUnits(15f);
        List<CombatUnit> defenderUnits = target.GetNearbyAlliedUnits(15f);
        
        // Ensure we have at least the attacking and defending units
        if (!attackerUnits.Contains(this))
        {
            attackerUnits.Add(this);
        }
        
        if (!defenderUnits.Contains(target))
        {
            defenderUnits.Add(target);
        }

        // Handle animals - they don't have an owner, so create a dummy civilization
        Civilization attackerCiv = owner;
        Civilization defenderCiv = target.owner;
        
        if (defenderCiv == null) // This is an animal
        {
            // Create a temporary civilization for the animal
            defenderCiv = CreateTemporaryAnimalCiv(target);
        }

        Debug.Log($"[CombatUnit] Starting real-time battle: {attackerUnits.Count} vs {defenderUnits.Count} units");
        if (target.owner == null)
        {
            Debug.Log($"[CombatUnit] Battle includes animal: {target.data.unitName}");
        }
        
        // Start the battle
            BattleTestSimple.Instance.StartBattle(attackerCiv, defenderCiv, attackerUnits, defenderUnits);
    }

    /// <summary>
    /// Create a temporary civilization for animals in battle
    /// </summary>
    private Civilization CreateTemporaryAnimalCiv(CombatUnit animal)
    {
        // Create a temporary civilization for the animal
        GameObject tempCivGO = new GameObject("TemporaryAnimalCiv");
        Civilization tempCiv = tempCivGO.AddComponent<Civilization>();
        
        // Initialize with basic data
        tempCiv.Initialize(null, null, false); // No civData, no leader, not player controlled
        
        return tempCiv;
    }
}