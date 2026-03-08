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
    // Ammunition system (for ranged units)
    public int currentAmmo { get; private set; }
    public bool isOutOfAmmo => data != null && data.isRangedUnit && currentAmmo <= 0;
    
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
        // Keep planet/grid consistent with this unit's assigned planet.
        planet = GameManager.Instance?.GetPlanetGenerator(planetIndex) ?? planet;
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
            
            // Initialize ammunition (full ammo for ranged units)
            currentAmmo = data.isRangedUnit ? data.maxAmmo : 0;
            
            // Only recalculate stats if data is valid (properties access data)
            RecalculateStats();
        }
        else
        {
            // Fallback if data is null (shouldn't happen but defensive programming)
            currentHealth = 10; // Default health
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


    // Only land units can move on land, naval on water
    public override bool CanMoveTo(int tileIndex)
    {
        // Turn-consuming actions (orbit entry/exit) prevent further movement
        if (hasActedThisTurn) return false;
        
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var tileData = ts != null ? ts.GetTileData(tileIndex) : null;
        if(tileData == null || !tileData.isPassable) return false;
        
        // Units in orbit can move to any tile (no terrain restrictions in space)
        if (currentLayer == TileLayer.Orbit)
        {
            // Only check orbit-layer occupancy
            try
            {
                var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
                var occObj = occ != null ? occ.GetOccupantObjectWithFallback(tileIndex, TileLayer.Orbit) : null;
                if (occObj != null && occObj.GetInstanceID() != gameObject.GetInstanceID()) return false;
            }
            catch { }
            return true;
        }
        
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

        // Layer-aware occupancy check: use occupancy manager with legacy fallback
        try
        {
            var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
            var occObj = occ != null ? occ.GetOccupantObjectWithFallback(tileIndex, currentLayer) : null;
            if (occObj != null && occObj.GetInstanceID() != gameObject.GetInstanceID()) return false;
        }
        catch { /* ignore and fallback */ }

        return true;
    }

    public override void MoveTo(int targetTileIndex)
    {
        var path = UnitMovementController.Instance.FindPath(currentTileIndex, targetTileIndex, this);
        path = UnitMovementController.Instance.TrimPathToAvailableMovement(this, path);
        if (path == null || path.Count == 0)
            return;

        // Reset animation before killing the old coroutine — StopAllCoroutines
        // would destroy the MoveAlongPath cleanup code that sets isMoving = false
        UpdateWalkingState(false);
        StopAllCoroutines();
        StartCoroutine(UnitMovementController.Instance.MoveAlongPath(this, path));
    }

    public void MoveAlongPath(List<int> path)
    {
        // Flat-only movement: rely on TileSystem for planar centers
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;

        UpdateWalkingState(true);

        foreach (int idx in path)
        {
            var currentTileData = ts != null ? ts.GetTileData(idx) : null;

            // Movement points removed

            if (ts == null) {
                Debug.LogWarning("[CombatUnit] TileSystem not ready; skipping movement step.");
                continue;
            }
            Vector3 pos = ts.GetTileSurfacePosition(idx);
            // Orbit units stay at configured orbit height above surface
            if (IsInOrbit) pos.y += PlanetGenerator.GetOrbitHeight(planetIndex);
            transform.position = pos;

            // Clear previous tile occupancy before setting new one
            try
            {
                if (currentTileIndex >= 0 && currentTileIndex != idx)
                    occ?.ClearOccupant(currentTileIndex, currentLayer);
                occ?.SetOccupant(idx, gameObject, currentLayer);
            }
            catch (System.Exception ex) { Debug.LogWarning($"[CombatUnit] Occupancy update failed: {ex.Message}"); }

            currentTileIndex = idx;
        }

        UpdateWalkingState(false);

        // Raise movement completed event
        if (path.Count > 0)
        {
            GameEventManager.Instance?.RaiseMovementCompletedEvent(this, path[0], path[path.Count - 1], path.Count);
        }
    }

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

        // Range check — use horizontal (XZ) distance for cross-layer attacks so orbit height doesn't inflate range
        float dist;
        if (currentLayer != target.currentLayer)
        {
            Vector3 a = transform.position; a.y = 0f;
            Vector3 b = target.transform.position; b.y = 0f;
            dist = Vector3.Distance(a, b);
        }
        else
        {
            dist = Vector3.Distance(transform.position, target.transform.position);
        }
        return dist <= CurrentRange;
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
        
        // Range check — use horizontal distance for cross-layer attacks
        float dist;
        if (currentLayer != target.currentLayer)
        {
            Vector3 a = transform.position; a.y = 0f;
            Vector3 b = target.transform.position; b.y = 0f;
            dist = Vector3.Distance(a, b);
        }
        else
        {
            dist = Vector3.Distance(transform.position, target.transform.position);
        }
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
    
    // For ranged attacks, still use the trigger (one-shot projectile launch animation)
    bool isRangedAttack = activeWeapon != null && activeWeapon.projectileData != null;
    
    // Check ammunition for ranged attacks
    if (isRangedAttack)
    {
        if (data != null && data.isRangedUnit && !ConsumeAmmo())
        {
            // Out of ammo! Can't fire ranged attack
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
    // Melee attacks use trigger

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
        int damage = Mathf.RoundToInt(rawF * dmgMul);

        // Flanking: adjacent allied units give +10% per extra unit
        int flankCount = CountAdjacentAllies(target.currentTileIndex) - 1;
        if (flankCount > 0)
            damage = Mathf.RoundToInt(damage * (1 + 0.1f * flankCount));

        // Elevation advantage: higher attacker gains up to +10%, lower attacker up to -10%
        // Skip for orbit units — orbit height is artificial, not terrain elevation
        if (!IsInOrbit && !target.IsInOrbit)
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

        if (!targetDies)
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
        // Skip for orbit units — orbit height is artificial, not terrain elevation
        if (!IsInOrbit && !target.IsInOrbit)
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
// Play hit animation using trigger (one-shot, not continuous)
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            if (HasParameter(animator, hitHash))
            {
                animator.SetTrigger(hitHash);
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
        ShowHealthChangePopup(-Mathf.Abs(damageAmount));
        UpdateUnitLabel();
// Raise damage event
        GameEventManager.Instance.RaiseDamageAppliedEvent(null, this, damageAmount);
        
        // Mark animal as recently attacked for predator/prey behavior system
        if (data != null && data.unitType == CombatCategory.Animal && AnimalManager.Instance != null)
        {
            AnimalManager.Instance.MarkAnimalAsAttacked(this);
        }
        
        if (currentHealth <= 0)
        {
            Die();
            return true;
        }
        
        if (owner != null && owner.isPlayerControlled && UIManager.Instance != null)
        {
            // Get tile data to show biome in notification
            var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
            var tileDataForNotification = ts != null ? ts.GetTileData(currentTileIndex) : null;
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
    /// Destroy this unit
    /// </summary>
    protected override void Die()
    {
        StopAllCoroutines();
        
        // Clear walking/idle state when dead
        UpdateWalkingState(false);
        
        // Death animation should play fully
        if (animator != null && HasParameter(animator, deathHash))
            animator.SetTrigger(deathHash);
        
        // Raise death event
        GameEventManager.Instance.RaiseUnitKilledEvent(null, this, currentHealth);
        
        // Fire local death event for listeners (e.g., AnimalManager)
        OnDeath?.Invoke();
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

        // Flanking for counter-attacks too
        int flankCount = CountAdjacentAllies(attacker.currentTileIndex) - 1;
        if (flankCount > 0)
            damage = Mathf.RoundToInt(damage * (1 + 0.1f * flankCount));

        // Elevation advantage (defender counter-attacking): compare defender (this) vs attacker
        // Skip for orbit units — orbit height is artificial, not terrain elevation
        if (!IsInOrbit && !attacker.IsInOrbit)
        {
            float elevationDiff = transform.position.y - attacker.transform.position.y;
            float elevationMultiplier = 1f + Mathf.Clamp(elevationDiff * 0.02f, -0.1f, 0.1f);
            damage = Mathf.Max(0, Mathf.RoundToInt(damage * elevationMultiplier));
        }

    attacker.ApplyDamage(damage, this, true);
        GainExperience(damage);
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
            var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
            var tileData = ts != null ? ts.GetTileData(currentTileIndex) : null;
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
            // Use GameManager API for multi-planet support
            planet = GameManager.Instance?.GetPlanetGenerator(planetIndex) ?? GameManager.Instance?.GetCurrentPlanetGenerator();
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
    /// Used by the save/load system to re-apply experience, health, ammo, etc.
    /// </summary>
    public void RestoreState(int savedHealth, int savedExperience, int savedLevel, int savedAmmo, bool savedHasActed, TileLayer savedLayer)
    {
        currentHealth = Mathf.Clamp(savedHealth, 0, MaxHealth);
        experience = savedExperience;
        level = Mathf.Max(1, savedLevel);
        currentAmmo = savedAmmo;
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
            animator.SetBool(isIdleHash, !walking);
        }
    }

    // Event fired when multi-tile move finishes
    public event System.Action OnMovementComplete;

    // MoveTo is overridden above (line ~610)

    /// <summary>
    /// Resets at start of turn.
    /// </summary>
    public override void ResetForNewTurn()
    {
        hasActedThisTurn = false;
        
        // If trapped, decrement duration (trappedTurnsRemaining is in BaseUnit)
        if (IsTrapped)
        {
            trappedTurnsRemaining = Mathf.Max(0, trappedTurnsRemaining - 1);
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
        
        // Units in orbit are above the surface — not affected by surface biome hazards
        if (IsInOrbit) return;
        
        // Get tile data
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var tileData = ts != null ? ts.GetTileData(currentTileIndex) : null;
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
            
        // Remove from transport
        transportedUnits.Remove(unit);
        
        // Update the unloaded unit's state
        unit.IsTransported = false;
        unit.TransportingUnit = null;
        unit.OnUnloadedFromTransport.Invoke(this);
        
        // Position the unit at the target tile and show it
        unit.gameObject.SetActive(true);
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null) {
            Debug.LogError("[CombatUnit] TileSystem not ready; cannot unload unit in flat-only mode.");
            return false;
        }
        var targetTileData = ts.GetTileData(targetTileIndex);
        unit.transform.position = ts.GetTileSurfacePosition(targetTileIndex);
        unit.currentTileIndex = targetTileIndex;
        
        // Update tile occupancy using layered occupancy manager
        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        try { occ?.SetOccupant(targetTileIndex, unit.gameObject, unit.currentLayer); } catch { }

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
                animator.SetTrigger(triggerHash);
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