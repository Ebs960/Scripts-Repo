using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using TMPro;
using GameCombat;

[RequireComponent(typeof(Animator))]
public class CombatUnit : MonoBehaviour
{
    [Header("Stats (Override Data Asset)")]
    [SerializeField] private int attack = 0;
    [SerializeField] private int defense = 0;
    [SerializeField] private int health = 0; 
    [SerializeField] private int movePoints = 0;
    [SerializeField] private int range = 0;
    [SerializeField] private int attackPoints = 0;
    [SerializeField] private int morale = 0;
    [SerializeField] private bool useOverrideStats = false;
    
    [Header("Equipment Attachment Points")]
    [Tooltip("Transform where weapons will be attached")]
    public Transform weaponHolder;
    [Tooltip("Transform where projectile/ranged weapon visuals will be attached (separate from melee weapon)")]
    public Transform projectileWeaponHolder;
    [Tooltip("Transform where shields will be attached")]
    public Transform shieldHolder;
    [Tooltip("Transform where armor will be displayed")]
    public Transform armorHolder;
    [Tooltip("Transform where miscellaneous items will be attached")]
    public Transform miscHolder;
    [Tooltip("Transform where projectiles will spawn from")]
    // Projectiles should specify their spawn point on the equipment prefab instead of on the unit.
    // Unit-level spawn point removed — fallbacks will use equipment holders.
    
    
    // Dictionary to track instantiated equipment GameObjects
    protected Dictionary<EquipmentType, GameObject> equippedItemObjects = new Dictionary<EquipmentType, GameObject>();
    // Extra map for secondary equipment visuals (e.g., projectile weapon stored separately)
    protected Dictionary<string, GameObject> extraEquippedItemObjects = new Dictionary<string, GameObject>();
    // Auto-disengage check
    private float meleeCheckInterval = 0.5f;
    private float meleeCheckTimer = 0f;
    
    // Equipment in use (beyond just the single 'equipped' reference)
    [Header("Equipped Items (Editable)")]
    [SerializeField] private EquipmentData _equippedWeapon;
    [SerializeField] private EquipmentData _equippedShield;
    [SerializeField] private EquipmentData _equippedArmor;
    [SerializeField] private EquipmentData _equippedMiscellaneous;
    // Separate weapon slot for projectile weapons; melee uses the main equipped weapon and `weaponHolder`.
    [SerializeField] private EquipmentData _equippedProjectileWeapon;
    
    [Header("Editor")]
    [Tooltip("If true, changing equipment in the Inspector will update visuals immediately in Edit mode. Disable to keep equipment invisible when editing the prefab/scene.")]
    [SerializeField] private bool updateEquipmentInEditor = true;
    
    public EquipmentData Weapon => equippedWeapon;
    public EquipmentData Shield => equippedShield;
    public EquipmentData Armor => equippedArmor;
    public EquipmentData Miscellaneous => equippedMiscellaneous;

    public EquipmentData ProjectileWeapon => equippedProjectileWeapon;

    public EquipmentData equippedWeapon
    {
        get => _equippedWeapon;  // Simplified getter - no fallback
        set
        {
            if (_equippedWeapon == value) return;
            // Prevent equipping worker-only items on combat units
            if (value != null && value.targetUnit == EquipmentTarget.WorkerUnit)
            {
                return;
            }
            _equippedWeapon = value;
            if (Application.isPlaying || updateEquipmentInEditor)
                UpdateEquipmentVisuals();
        }
    }
    // Melee no longer has a dedicated slot; melee uses `equippedWeapon` and `weaponHolder`.

    public EquipmentData equippedProjectileWeapon
    {
        get => _equippedProjectileWeapon;
        private set
        {
            if (_equippedProjectileWeapon == value) return;
            _equippedProjectileWeapon = value;
            if (Application.isPlaying || updateEquipmentInEditor)
                UpdateEquipmentVisuals();
        }
    }
    public EquipmentData equippedShield {
        get => _equippedShield; // Remove fallback logic
        set {
            if (_equippedShield == value) return;
            if (value != null && value.targetUnit == EquipmentTarget.WorkerUnit)
            {
                return;
            }
            _equippedShield = value;
            if (Application.isPlaying || updateEquipmentInEditor)
                UpdateEquipmentVisuals();
        }
    }
    public EquipmentData equippedArmor {
        get => _equippedArmor; // Remove fallback logic
        set {
            if (_equippedArmor == value) return;
            if (value != null && value.targetUnit == EquipmentTarget.WorkerUnit)
            {
                return;
            }
            _equippedArmor = value;
            if (Application.isPlaying || updateEquipmentInEditor)
                UpdateEquipmentVisuals();
        }
    }
    public EquipmentData equippedMiscellaneous {
        get => _equippedMiscellaneous; // Remove fallback logic
        set {
            if (_equippedMiscellaneous == value) return;
            if (value != null && value.targetUnit == EquipmentTarget.WorkerUnit)
            {
                return;
            }
            _equippedMiscellaneous = value;
            if (Application.isPlaying || updateEquipmentInEditor)
                UpdateEquipmentVisuals();
        }
    }
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
    
    SphericalHexGrid grid;
    PlanetGenerator planet;
    Animator animator;

    // Cached weapon grip from currently equipped weapon visual (found by name on instantiated equipment)
    private Transform _weaponGrip;

    [Header("Projectiles")]
    [Tooltip("If true, projectiles from weapons will be fired via an animation event calling FireQueuedProjectile(); if false they fire immediately during Attack.")]
    public bool useAnimationEventForProjectiles = true;

    // Queued projectile data populated on Attack and fired from animation event
    private EquipmentData queuedProjectileEquipment;
    private CombatUnit queuedProjectileTargetUnit;
    private Vector3 queuedProjectileTargetPosition;
    private int queuedProjectileDamage = -1;
    private bool hasQueuedProjectile = false;

    // Melee engagement state: when true the unit uses its melee weapon instead of projectile weapon
    private bool engagedInMelee = false;
    private Coroutine meleeEngageCoroutine = null;

    [field: SerializeField] public CombatUnitData data { get; private set; }  // Now serializable and assignable in Inspector
    public Civilization owner { get; private set; }

    // Remove old events and use GameEventManager instead
    public event System.Action OnDeath;
    public event System.Action<int,int> OnHealthChanged;      // (newHealth, maxHealth)
    public event System.Action<string> OnAnimationTrigger;    // (triggerName)
    public event System.Action<int,int> OnMoraleChanged;
    public event System.Action OnEquipmentChanged;

    // Equipment & Abilities
    // Currently equipped item (last set via Equip/EquipItem) used for yield calculations, too
    public EquipmentData equipped { get; private set; }
    public List<Ability> unlockedAbilities { get; private set; } = new List<Ability>();

    // Runtime stats
    public int currentHealth { get; private set; }
    public int currentMovePoints { get; private set; }
    public int currentAttackPoints { get; private set; }
    public int experience { get; private set; }
    public int level { get; private set; }
    // New: morale runtime stat
    public int currentMorale    { get; private set; }
    
    // Flag for tracking winter movement penalty
    public bool hasWinterPenalty { get; set; }
    
    // Weather susceptibility
    [Header("Weather")]
    [Tooltip("If true, this unit takes weather attrition in severe seasons (e.g., winter)")]
    public bool takesWeatherDamage = true;

    // Routed flag when morale hits zero
    public bool isRouted { get; private set; }

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

    [Header("UI")]
    [SerializeField] private GameObject unitLabelPrefab;
    private UnitLabel unitLabelInstance;



    void Start()
    {
        // If equipment was assigned in Inspector before play mode, ensure visuals are created at runtime
        if (Application.isPlaying)
        {
            UpdateEquipmentVisuals();
        }
    }


    void Awake()
    {
        animator = GetComponent<Animator>();
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
    void OnValidate()
    {
        // Only run in edit mode, not during play mode
        if (!Application.isPlaying && updateEquipmentInEditor)
        {
            // Use a more direct approach to avoid timing issues
            // Schedule the update for the next frame to ensure all inspector changes are processed
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && !Application.isPlaying && updateEquipmentInEditor)
                {
                    UpdateEquipmentVisuals();
                    // Mark the object as dirty so changes are saved
                    UnityEditor.EditorUtility.SetDirty(this);
                }
            };

            // Editor-time validation: ensure assigned equipment is compatible with CombatUnit
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                if (!Application.isPlaying)
                {
                    if (_equippedWeapon != null && _equippedWeapon.targetUnit == EquipmentTarget.WorkerUnit)
                    {
                        _equippedWeapon = null;
                    }
                    if (_equippedShield != null && _equippedShield.targetUnit == EquipmentTarget.WorkerUnit)
                    {
                        _equippedShield = null;
                    }
                    if (_equippedArmor != null && _equippedArmor.targetUnit == EquipmentTarget.WorkerUnit)
                    {
                        _equippedArmor = null;
                    }
                    if (_equippedMiscellaneous != null && _equippedMiscellaneous.targetUnit == EquipmentTarget.WorkerUnit)
                    {
                        _equippedMiscellaneous = null;
                    }
                    UpdateEquipmentVisuals();
                    UnityEditor.EditorUtility.SetDirty(this);
                }
            };
        }
    }

    void OnDestroy()
    {
        // Clean up all equipment GameObjects
        foreach (var item in equippedItemObjects.Values)
        {
            if (item != null)
                Destroy(item);
        }
        equippedItemObjects.Clear();
        
        // Unsubscribe from events
        GameEventManager.Instance.OnMovementCompleted -= HandleMovementCompleted;
        GameEventManager.Instance.OnCombatStarted -= HandleCombatStarted;
        GameEventManager.Instance.OnDamageApplied -= HandleDamageApplied;

        UnitRegistry.Unregister(gameObject);
    }

    public void Initialize(CombatUnitData unitData, Civilization unitOwner)
    {
        data = unitData;
        owner = unitOwner;
        level = 1;
        experience = 0;

        // Equip all default equipment slots
    // Equip projectile and melee defaults (defaultWeapon is authoritative melee)
    if (data.defaultProjectileWeapon != null) EquipItem(data.defaultProjectileWeapon);
    if (data.defaultWeapon != null) EquipItem(data.defaultWeapon);
    if (data.defaultShield != null) EquipItem(data.defaultShield);
    if (data.defaultArmor != null) EquipItem(data.defaultArmor);
    if (data.defaultMiscellaneous != null) EquipItem(data.defaultMiscellaneous);

        // Weather susceptibility from data
        takesWeatherDamage = (data != null) ? data.takesWeatherDamage : takesWeatherDamage;

        currentHealth = MaxHealth;
        currentMorale = useOverrideStats && morale > 0 ? morale : data.baseMorale;

        RecalculateStats();

        animator = GetComponent<Animator>();
        // Ensure animator is not null before trying to set a trigger
        if (animator != null) 
        {
            animator.SetTrigger("idleYoung");
        }
        else
        {
            Debug.LogWarning($"CombatUnit {gameObject.name} is missing an Animator component.");
        }

        UpdateEquipmentVisuals();

        if (UnitFormationManager.Instance != null)
        {
            UnitFormationManager.Instance.RegisterFormation(this);
        }

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

    // Base stats exposed
    public int BaseAttack => useOverrideStats && attack > 0 ? attack : data.baseAttack;
    public int BaseDefense => useOverrideStats && defense > 0 ? defense : data.baseDefense;
    public int BaseHealth => useOverrideStats && health > 0 ? health : data.baseHealth;
    public int BaseMovePoints => useOverrideStats && movePoints > 0 ? movePoints : data.baseMovePoints;
    public int BaseRange => useOverrideStats && range > 0 ? range : data.baseRange;
    public int BaseAttackPoints => useOverrideStats && attackPoints > 0 ? attackPoints : data.baseAttackPoints;

    // Equipment bonuses (sum across all equipped slots)
    // Equipment bonuses aggregated as floats (can be fractional)
    public float EquipmentAttackBonus
        => (equippedWeapon?.attackBonus ?? 0f)
         + (equippedShield?.attackBonus ?? 0f)
         + (equippedArmor?.attackBonus ?? 0f)
         + (equippedMiscellaneous?.attackBonus ?? 0f);
    public float EquipmentDefenseBonus
        => (equippedWeapon?.defenseBonus ?? 0f)
         + (equippedShield?.defenseBonus ?? 0f)
         + (equippedArmor?.defenseBonus ?? 0f)
         + (equippedMiscellaneous?.defenseBonus ?? 0f);
    public float EquipmentHealthBonus
        => (equippedWeapon?.healthBonus ?? 0f)
         + (equippedShield?.healthBonus ?? 0f)
         + (equippedArmor?.healthBonus ?? 0f)
         + (equippedMiscellaneous?.healthBonus ?? 0f);
    public float EquipmentMoveBonus
        => (equippedWeapon?.movementBonus ?? 0f)
         + (equippedShield?.movementBonus ?? 0f)
         + (equippedArmor?.movementBonus ?? 0f)
         + (equippedMiscellaneous?.movementBonus ?? 0f);
    public float EquipmentRangeBonus
        => (equippedWeapon?.rangeBonus ?? 0f)
         + (equippedShield?.rangeBonus ?? 0f)
         + (equippedArmor?.rangeBonus ?? 0f)
         + (equippedMiscellaneous?.rangeBonus ?? 0f);
    public float EquipmentAttackPointsBonus
        => (equippedWeapon?.attackPointsBonus ?? 0f)
         + (equippedShield?.attackPointsBonus ?? 0f)
         + (equippedArmor?.attackPointsBonus ?? 0f)
         + (equippedMiscellaneous?.attackPointsBonus ?? 0f);

    // Ability modifiers - ADDED
    public int GetAbilityAttackModifier()
    {
        int total = 0;
        foreach (var ability in unlockedAbilities)
            total += ability.attackModifier;
        return total;
    }

    public int GetAbilityDefenseModifier()
    {
        int total = 0;
        foreach (var ability in unlockedAbilities)
            total += ability.defenseModifier;
        return total;
    }

    // Add new ability modifier methods
    public int GetAbilityHealthModifier()
    {
        int total = 0;
        foreach (var ability in unlockedAbilities)
            total += ability.healthModifier;
        return total;
    }

    public int GetAbilityRangeModifier()
    {
        int total = 0;
        foreach (var ability in unlockedAbilities)
            total += ability.rangeModifier;
        return total;
    }

    public int GetAbilityAttackPointsModifier()
    {
        int total = 0;
        foreach (var ability in unlockedAbilities)
            total += ability.attackPointsModifier;
        return total;
    }

    public float GetAbilityDamageMultiplier()
    {
        float total = 1.0f; // Start with base value
        foreach (var ability in unlockedAbilities)
            total *= ability.damageMultiplier;
        return total;
    }

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
                        a.moveAdd += b.movePointsAdd; a.rangeAdd += b.rangeAdd; a.apAdd += b.attackPointsAdd; a.moraleAdd += b.moraleAdd;
                        a.attackPct += b.attackPct; a.defensePct += b.defensePct; a.healthPct += b.healthPct;
                        a.movePct += b.movePointsPct; a.rangePct += b.rangePct; a.apPct += b.attackPointsPct; a.moralePct += b.moralePct;
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
                        a.moveAdd += b.movePointsAdd; a.rangeAdd += b.rangeAdd; a.apAdd += b.attackPointsAdd; a.moraleAdd += b.moraleAdd;
                        a.attackPct += b.attackPct; a.defensePct += b.defensePct; a.healthPct += b.healthPct;
                        a.movePct += b.movePointsPct; a.rangePct += b.rangePct; a.apPct += b.attackPointsPct; a.moralePct += b.moralePct;
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
                        a.moveAdd += b.movePointsAdd; a.rangeAdd += b.rangeAdd; a.apAdd += b.attackPointsAdd;
                        a.attackPct += b.attackPct; a.defensePct += b.defensePct; a.healthPct += b.healthPct;
                        a.movePct += b.movePointsPct; a.rangePct += b.rangePct; a.apPct += b.attackPointsPct;
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
                        a.moveAdd += b.movePointsAdd; a.rangeAdd += b.rangeAdd; a.apAdd += b.attackPointsAdd;
                        a.attackPct += b.attackPct; a.defensePct += b.defensePct; a.healthPct += b.healthPct;
                        a.movePct += b.movePointsPct; a.rangePct += b.rangePct; a.apPct += b.attackPointsPct;
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

    public int CurrentAttack
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
            // Apply per-target bonuses (if this unit is attacking a specific target, callers may need to apply extra modifiers).
            return Mathf.RoundToInt(valF);
        }
    }
    public int CurrentDefense
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
    public int MaxHealth
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
    public int CurrentRange
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
            return Mathf.RoundToInt(valF);
        }
    }
    public int MaxAttackPoints   => Mathf.RoundToInt(BaseAttackPoints + EquipmentAttackPointsBonus + GetAbilityAttackPointsModifier());
    public int MaxMorale         => useOverrideStats && morale > 0 ? morale : data.baseMorale;
    


    // Only land units can move on land, naval on water
    public bool CanMoveTo(int tileIndex)
    {
        var (tileData, isMoonTile) = TileDataHelper.Instance.GetTileData(tileIndex);
        if(tileData == null || !tileData.isPassable) return false;
        
        // Special case for Moon: ONLY spaceships with canTravelToMoon flag can go there
        if (isMoonTile)
        {
            bool isSpaceshipWithMoonAccess = data.unitType == CombatCategory.Spaceship && data.canTravelToMoon;
            
            if (!isSpaceshipWithMoonAccess)
                return false;
                
            if (currentMovePoints < BiomeHelper.GetMovementCost(tileData, this)) return false;
            
            if (tileData.occupantId != 0 && tileData.occupantId != gameObject.GetInstanceID())
                return false;
                
            return true;
        }
        else
        {
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
        }

    if (currentMovePoints < BiomeHelper.GetMovementCost(tileData, this)) return false;

        if (tileData.occupantId != 0 && tileData.occupantId != gameObject.GetInstanceID())
            return false;

        return true;
    }

    public void MoveAlongPath(List<int> path)
    {
        var (tileData, isMoon) = TileDataHelper.Instance.GetTileData(path[0]);
        SphericalHexGrid currentGrid = grid;

        foreach (int idx in path)
        {
            var (currentTileData, _) = TileDataHelper.Instance.GetTileData(idx);

            int cost = BiomeHelper.GetMovementCost(currentTileData, this);
            currentMovePoints -= cost;

            Vector3 pos = currentGrid.tileCenters[idx];
            transform.position = pos;

            // Update tile occupancy
            TileDataHelper.Instance.SetTileOccupant(idx, gameObject);
            
            currentTileIndex = idx;
        }

        // Raise movement completed event
        if (path.Count > 0)
        {
            GameEventManager.Instance.RaiseMovementCompletedEvent(this, path[0], path[path.Count - 1], path.Count);
        }
    }

    public bool CanAttack(CombatUnit target)
    {
        if (currentAttackPoints <= 0) return false;
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

    public void Attack(CombatUnit target)
    {
        if (!CanAttack(target)) return;

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

    // Choose animation trigger based on whether this is a ranged attack (weapon defines projectileData)
    bool isRangedAttack = activeWeapon != null && activeWeapon.projectileData != null;
    string triggerName = isRangedAttack ? "RangedAttack" : "Attack";
    animator.SetTrigger(triggerName);
    OnAnimationTrigger?.Invoke(triggerName);

        // Tile defense bonus for target (e.g., hills)
        int tileBonus = 0;
        var (tileData, _) = TileDataHelper.Instance.GetTileData(target.currentTileIndex);
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

    // If the active weapon defines projectile data, either queue or spawn the projectile depending on settings
    if (activeWeapon != null && activeWeapon.projectileData != null)
        {
            if (useAnimationEventForProjectiles)
            {
        QueueProjectileForAnimation(activeWeapon, target.transform.position, target, damage);
                currentAttackPoints--;
                // Projectile will be fired by animation event (FireQueuedProjectile)
                return;
            }
            else
            {
                // Spawn immediately (legacy behaviour)
        SpawnProjectileFromEquipment(activeWeapon, target.transform.position, target, damage);
                currentAttackPoints--;
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

        currentAttackPoints--;
        GainExperience(damage);
    }
    
    /// <summary>
    /// Apply damage to this unit, which reduces its health
    /// </summary>
    /// <param name="damageAmount">Amount of damage to deal</param>
    /// <returns>True if the unit is destroyed by this damage</returns>
    public bool ApplyDamage(int damageAmount)
    {
        animator.SetTrigger("hit");
        
        currentHealth -= damageAmount;
        
        // Raise damage event
        GameEventManager.Instance.RaiseDamageAppliedEvent(null, this, damageAmount);
        
        // Mark animal as recently attacked for predator/prey behavior system
        if (data.unitType == CombatCategory.Animal && AnimalManager.Instance != null)
        {
            AnimalManager.Instance.MarkAnimalAsAttacked(this);
        }
        
        // Morale penalty proportional to HP lost
        ChangeMorale(-damageAmount * data.moraleLostPerHealth);
        
        if (currentHealth <= 0)
        {
            Die();
            return true;
        }
        
        if (currentHealth <= MaxHealth * 0.2f && !isRouted)
        {
            Rout();
        }
        
        if (owner != null && owner.isPlayerControlled && UIManager.Instance != null)
        {
            // Get tile data to show biome in notification
            var (tileDataForNotification, _) = TileDataHelper.Instance.GetTileData(currentTileIndex);
            if (tileDataForNotification != null)
            {
                UIManager.Instance.ShowNotification($"{data.unitName} took {damageAmount} damage from {tileDataForNotification.biome} terrain!");
            }
        }
        
        return false;
    }

    /// <summary>
    /// Apply damage with context about the attacker. If the attacker is adjacent (melee) then mark this unit as engaged in melee
    /// so it will use its melee weapon for a short duration.
    /// </summary>
    public bool ApplyDamage(int damageAmount, CombatUnit attacker, bool attackerIsMelee)
    {
        if (attackerIsMelee && data != null && data.defaultWeapon != null)
        {
            // Mark engaged in melee and start/restart the timer
            engagedInMelee = true;
            if (meleeEngageCoroutine != null) StopCoroutine(meleeEngageCoroutine);
            meleeEngageCoroutine = StartCoroutine(EndMeleeEngageAfterDelay(data.meleeEngageDuration));
        }

        return ApplyDamage(damageAmount);
    }

    private System.Collections.IEnumerator EndMeleeEngageAfterDelay(float delay)
    {
        float t = 0f;
        while (t < delay)
        {
            t += Time.deltaTime;
            yield return null;
        }

        engagedInMelee = false;
        meleeEngageCoroutine = null;
    }
    
    /// <summary>
    /// Set this unit to routed state (reduced effectiveness)
    /// </summary>
    private void Rout()
    {
        isRouted = true;
        // Visual indicator for routing could be added here
        Debug.Log($"{data.unitName} has been routed!");
    }
    
    /// <summary>
    /// Destroy this unit
    /// </summary>
    private void Die()
    {
        animator.SetTrigger("death");
        
        // Raise death event
        GameEventManager.Instance.RaiseUnitKilledEvent(null, this, currentHealth);
        
        // Fire local death event for listeners (e.g., AnimalManager)
        OnDeath?.Invoke();
        
        Debug.Log($"{data.unitName} has been destroyed!");
        owner.food += data.foodOnKill;
        
        owner.combatUnits.Remove(this);
        
        if (currentTileIndex >= 0)
        {
            TileDataHelper.Instance.ClearTileOccupant(currentTileIndex);
        }
        
        if (unitLabelInstance != null)
        {
            Destroy(unitLabelInstance.gameObject);
        }
        
        Destroy(gameObject, 1.5f);
    }

    /// <summary>
    /// Performs a counter-attack back at the attacker.
    /// </summary>
    public void CounterAttack(CombatUnit attacker)
    {
        if (!data.canCounterAttack) return;
        if (currentAttackPoints <= 0) return;
        if (isRouted) return; // Routed units cannot counter-attack

        animator.SetTrigger("attack");
        OnAnimationTrigger?.Invoke("attack");

        int tileBonus = 0;
        var (tileData, _) = TileDataHelper.Instance.GetTileData(attacker.currentTileIndex);
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

    attacker.ApplyDamage(damage, this, true);
        currentAttackPoints--;
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

        // Check if unit is now routed
        if (!isRouted && currentMorale == 0)
        {
            // Unit routs: cannot attack, moves randomly away
            isRouted = true;
            animator.SetTrigger("rout");
            OnAnimationTrigger?.Invoke("rout");
            // Flee one tile away
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
        return BaseDefense + EquipmentDefenseBonus + GetAbilityDefenseModifier();
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
    /// Simple placeholder flee logic: move to a random neighbouring tile.
    /// </summary>
    private void AttemptFlee()
    {
        if (grid == null) return;

        int[] neighbours = grid.neighbors[currentTileIndex].ToArray();
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
        animator.SetTrigger("idleExperienced");
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
        float baseMoveF = BaseMovePoints + EquipmentMoveBonus;
        float baseAPF = BaseAttackPoints + EquipmentAttackPointsBonus + GetAbilityAttackPointsModifier();
        float maxHPF = BaseHealth + EquipmentHealthBonus + GetAbilityHealthModifier();

        // Apply targeted bonuses from techs/cultures
        if (owner != null && data != null)
        {
            var agg = AggregateUnitBonusesLocal(owner, data);
            // Apply additive first
            baseMoveF += agg.moveAdd;
            baseAPF += agg.apAdd;
            maxHPF += agg.healthAdd;
            // Apply multiplicative
            baseMoveF = baseMoveF * (1f + agg.movePct);
            baseAPF = baseAPF * (1f + agg.apPct);
            maxHPF = maxHPF * (1f + agg.healthPct);
            // Attack/Defense/Range/Morale handled dynamically via getters or in combat; keep HP/move/AP here
            // Apply equipment-targeted bonuses across all equipped items
            var eagg = AggregateAllEquippedBonusesLocal(owner);
            baseMoveF = (baseMoveF + eagg.moveAdd) * (1f + eagg.movePct);
            baseAPF = (baseAPF + eagg.apAdd) * (1f + eagg.apPct);
            maxHPF = (maxHPF + eagg.healthAdd) * (1f + eagg.healthPct);
        }

        currentMovePoints = Mathf.RoundToInt(baseMoveF);
        currentAttackPoints = Mathf.RoundToInt(baseAPF);
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
        Vector3 tileSurfaceCenter = TileDataHelper.Instance.GetTileSurfacePosition(tileIndex, 0f, 0); // Force Earth (planet index 0)
        
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
        string trigger = level == 1 ? "idleYoung" : "idleExperienced";
        animator.SetTrigger(trigger);
    }
    
    public int currentTileIndex;
    public float moveSpeed = 2f;
    public bool isMoving { get; set; }

    // Event fired when multi-tile move finishes
    public event System.Action OnMovementComplete;

    /// <summary>
    /// Requests a move to the target tile index.
    /// Will pathfind and animate until movement points run out or destination reached.
    /// </summary>
    public void MoveTo(int targetTileIndex)
    {
        var path = UnitMovementController.Instance.FindPath(currentTileIndex, targetTileIndex);
        if (path == null || path.Count == 0)
            return;

        StopAllCoroutines();
        StartCoroutine(UnitMovementController.Instance.MoveAlongPath(this, path));
    }

    /// <summary>
    /// Resets movement and attack points at start of turn. Also replenishes morale.
    /// </summary>
    public void ResetForNewTurn()
    {
    // Calculate base move points including equipment bonuses (float intermediate)
    float baseMoveF = BaseMovePoints + EquipmentMoveBonus;
        
        // If trapped, decrement duration and block movement for this turn
        if (IsTrapped)
        {
            trappedTurnsRemaining = Mathf.Max(0, trappedTurnsRemaining - 1);
            currentMovePoints = 0;
        }
        else
        {
        // Apply winter penalty if applicable
        if (hasWinterPenalty && ClimateManager.Instance != null && 
            ClimateManager.Instance.currentSeason == Season.Winter)
        {
            currentMovePoints = Mathf.Max(1, Mathf.RoundToInt(baseMoveF) - 1);
        }
        else
        {
            currentMovePoints = Mathf.RoundToInt(baseMoveF);
        }
        }
        
        currentAttackPoints = MaxAttackPoints;
        
        // Morale replenishment
        int moraleRecovery = 10; // Default minimum recovery
        ChangeMorale(moraleRecovery);
        
        // Clear routed flag if morale is above 0
        if (isRouted && currentMorale > 0)
            isRouted = false;
            
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
        var (tileData, isMoonTile) = TileDataHelper.Instance.GetTileData(currentTileIndex);
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

    /// <summary>
    /// Check if unit has enough movement points for a given cost
    /// </summary>
    public bool CanMove(int movementCost)
    {
        return currentMovePoints >= movementCost;
    }
    
    /// <summary>
    /// Deduct movement points safely
    /// </summary>
    public void DeductMovementPoints(int amount)
    {
        currentMovePoints = Mathf.Max(0, currentMovePoints - amount);
    }
    
    /// <summary>
    /// Safely trigger the OnMovementComplete event from external systems
    /// </summary>
    public void TriggerMovementComplete()
    {
        OnMovementComplete?.Invoke();
    }

    private int CountAdjacentAllies(int tileIndex)
    {
        int count = 0;
        var (tileData, isMoon) = TileDataHelper.Instance.GetTileData(tileIndex);
        if (tileData == null) return 0;

        SphericalHexGrid currentGrid = grid;
        if (currentGrid == null) return 0;

        int[] neighbours = TileDataHelper.Instance.GetTileNeighbors(tileIndex);
        if (neighbours != null)
        {
            foreach (int idx in neighbours)
            {
                var (neighborTileData, _) = TileDataHelper.Instance.GetTileData(idx);
                if (neighborTileData != null)
                {
                    // Get the GameObject from the occupantId
                    int occupantId = neighborTileData.occupantId;
                    if (occupantId != 0)
                    {
                        var objectWithId = UnitRegistry.GetObject(occupantId);
                        if (objectWithId != null)
                        {
                            CombatUnit unit = objectWithId.GetComponent<CombatUnit>();
                            if (unit != null && unit.owner == this.owner)
                            {
                                count++;
                            }
                        }
                    }
                }
            }
        }
        return count;
    }
    


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
            var (tileData, isMoon) = TileDataHelper.Instance.GetTileData(currentTileIndex);
            SphericalHexGrid currentGrid = grid;
            int[] neighbors = TileDataHelper.Instance.GetTileNeighbors(currentTileIndex);
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
            var (tileData, isMoon) = TileDataHelper.Instance.GetTileData(currentTileIndex);
            SphericalHexGrid currentGrid = grid;
            int[] neighbors = TileDataHelper.Instance.GetTileNeighbors(currentTileIndex);
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
        var (targetTileData, isTargetMoon) = TileDataHelper.Instance.GetTileData(targetTileIndex);
        SphericalHexGrid targetGrid = grid;
        unit.transform.position = targetGrid.tileCenters[targetTileIndex];
        unit.currentTileIndex = targetTileIndex;
        
        // Update tile occupancy
        TileDataHelper.Instance.SetTileOccupant(targetTileIndex, unit.gameObject);

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
    
    /// <summary>
    /// Equips an item in the appropriate slot based on its type
    /// </summary>
    public virtual void EquipItem(EquipmentData equipmentData)
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
                                    equippedProjectileWeapon = equipmentData;
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
        
        // Update the general equipped reference too for backward compatibility
    // Keep legacy `equipped` pointing to a sensible primary weapon and determine internal primary _equippedWeapon
    equipped = equipmentData;
    // Primary selection: if engaged in melee prefer the main equipped weapon, otherwise prefer projectile if present, then main weapon
    if (engagedInMelee && equippedWeapon != null) _equippedWeapon = equippedWeapon;
    else if (equippedProjectileWeapon != null) _equippedWeapon = equippedProjectileWeapon;
    else if (equippedWeapon != null) _equippedWeapon = equippedWeapon;
    else _equippedWeapon = equipmentData;
        
        // Update visuals if something changed
        if (changed)
        {
            UpdateEquipmentVisuals();
            
            // Recalculate stats that might be affected by equipment
            RecalculateStats();
            
            // Notify listeners
            OnEquipmentChanged?.Invoke();
        }
    }
    
    /// <summary>
    /// Updates the visual representation of all equipped items
    /// </summary>
    public virtual void UpdateEquipmentVisuals()
    {
        // Animals don't use equipment; skip any equipment processing or editor logs for them.
        if (data != null && data.unitType == CombatCategory.Animal)
        {
            // Quietly destroy any lingering equipment visuals without logging (editor or play)
            foreach (var item in equippedItemObjects.Values)
            {
                if (item != null)
                {
#if UNITY_EDITOR
                    if (!Application.isPlaying)
                        UnityEngine.Object.DestroyImmediate(item);
                    else
#endif
                        UnityEngine.Object.Destroy(item);
                }
            }
            equippedItemObjects.Clear();
            return;
        }

        // Clear cached grips before replacing visuals
        _weaponGrip = null;

    // Remove any existing equipment visual objects
    foreach (var item in equippedItemObjects.Values)
        {
            if (item != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    UnityEngine.Object.DestroyImmediate(item);
                else
#endif
                    UnityEngine.Object.Destroy(item);
            }
        }
        equippedItemObjects.Clear();

    // Process ALL slots, including empty ones to ensure proper cleanup
    // Melee weapon visuals
    ProcessEquipmentSlot(EquipmentType.Weapon, equippedWeapon, weaponHolder);
    // Projectile weapon visuals - instantiate into the projectileWeaponHolder using the extra map
    // Clear previous projectile holder children
    if (projectileWeaponHolder != null)
    {
        for (int i = projectileWeaponHolder.childCount - 1; i >= 0; i--)
        {
            var child = projectileWeaponHolder.GetChild(i);
            if (child != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                else
#endif
                    UnityEngine.Object.Destroy(child.gameObject);
            }
        }
    }
    // Manage projectile weapon visuals in the extra map
    if (extraEquippedItemObjects == null) extraEquippedItemObjects = new Dictionary<string, GameObject>();
    // Clear previous projectile entry if any
    if (extraEquippedItemObjects.TryGetValue("projectile", out var prevProj))
    {
        if (prevProj != null) { if (Application.isPlaying) Destroy(prevProj); else UnityEngine.Object.DestroyImmediate(prevProj); }
        extraEquippedItemObjects.Remove("projectile");
    }
    if (equippedProjectileWeapon != null && projectileWeaponHolder != null)
    {
        // Instantiate projectile weapon prefab into separate holder
        if (equippedProjectileWeapon.equipmentPrefab != null)
        {
            var projObj = Instantiate(equippedProjectileWeapon.equipmentPrefab);
            var authoredLocal = projObj.transform.localRotation;
            projObj.transform.SetParent(projectileWeaponHolder, false);
            projObj.transform.localPosition = Vector3.zero;
            projObj.transform.localRotation = authoredLocal;
            extraEquippedItemObjects["projectile"] = projObj;
        }
    }
    ProcessEquipmentSlot(EquipmentType.Shield, equippedShield, shieldHolder);
    ProcessEquipmentSlot(EquipmentType.Armor, equippedArmor, armorHolder);
    ProcessEquipmentSlot(EquipmentType.Miscellaneous, equippedMiscellaneous, miscHolder);
    }
    
    /// <summary>
    /// Process a single equipment slot - handles both equipping and clearing
    /// </summary>
    private void ProcessEquipmentSlot(EquipmentType type, EquipmentData itemData, Transform holder)
    {
        // Animals don't use equipment; skip processing and avoid editor/runtime warnings/logs for them.
        if (data != null && data.unitType == CombatCategory.Animal)
        {
            // If there is a holder, quietly remove any children without logging
            if (holder != null)
            {
                for (int i = holder.childCount - 1; i >= 0; i--)
                {
                    var child = holder.GetChild(i);
                    if (child != null)
                    {
#if UNITY_EDITOR
                        if (!Application.isPlaying)
                            UnityEngine.Object.DestroyImmediate(child.gameObject);
                        else
#endif
                            UnityEngine.Object.Destroy(child.gameObject);
                    }
                }
            }
            return;
        }
        
        // Clear existing equipment in this slot first
        for (int i = holder.childCount - 1; i >= 0; i--)
        {
            var child = holder.GetChild(i);
            if (child != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                else
#endif
                    UnityEngine.Object.Destroy(child.gameObject);
            }
        }
        
        // If no equipment data, slot is now empty - we're done
        if (itemData == null)
        {
            return;
        }
        
        // Process the equipment data
        UpdateEquipmentSlot(type, itemData, holder);
    }
    
    /// <summary>
    /// Updates a specific equipment slot with the specified item
    /// </summary>
    protected virtual void UpdateEquipmentSlot(EquipmentType type, EquipmentData itemData, Transform holder)
    {
        if (holder == null)
        {
            return;
        }
        if (itemData == null)
        {
            return;
        }
        if (itemData.equipmentPrefab == null)
        {
            return;
        }

    // Record holder transform so we can detect unexpected changes during instantiation
    Quaternion holderWorldBefore = holder.rotation;
    Quaternion holderLocalBefore = holder.localRotation;
    Vector3 holderWorldPosBefore = holder.position;
    Vector3 holderLocalPosBefore = holder.localPosition;
    Transform holderParentBefore = holder.parent;
    int holderChildCountBefore = holder.childCount;

    // Remove any existing visuals under the holder
        for (int i = holder.childCount - 1; i >= 0; i--)
        {
            var child = holder.GetChild(i);
            if (child != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                else
#endif
                    UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        // Instantiate the equipment and parent it directly to holder
        GameObject equipObj = Instantiate(itemData.equipmentPrefab);

        Quaternion authoredLocal = equipObj.transform.localRotation;
        equipObj.transform.SetParent(holder, false);
        equipObj.transform.localPosition = Vector3.zero;
        equipObj.transform.localRotation = authoredLocal;

            // Defensive: ensure the instantiated prefab actually has visible renderers.
            // Attempt to repair SkinnedMeshRenderer bone bindings if they reference external bones,
            // enable disabled renderers, and fall back to a placeholder if nothing is visible.
            var allRenderers = equipObj.GetComponentsInChildren<Renderer>(true);
            if (allRenderers == null || allRenderers.Length == 0)
            {
                var placeholder = GameObject.CreatePrimitive(PrimitiveType.Cube);
                placeholder.name = equipObj.name + "_placeholder";
                placeholder.transform.SetParent(holder, false);
                placeholder.transform.localPosition = Vector3.zero;
                placeholder.transform.localRotation = Quaternion.identity;
                placeholder.transform.localScale = Vector3.one * 0.2f;
                equippedItemObjects[type] = placeholder;
            }
            else
            {
                foreach (var r in allRenderers)
                {
                    if (r == null) continue;
                    if (!r.enabled) r.enabled = true;

                    var smr = r as SkinnedMeshRenderer;
                    if (smr != null)
                    {
                        // Equipment is already rigged to use unit bones - don't try to repair
                        // The bone binding repair code was causing equipment to move with animations
                        // by incorrectly replacing unit bone references with equipment bone references
                    }
                }
                // keep equipObj as the tracked item unless we explicitly replaced it above
                if (!equippedItemObjects.ContainsKey(type) || equippedItemObjects[type] == null)
                    equippedItemObjects[type] = equipObj;
            }

        // Detect if the holder was modified by any side-effect during instantiation
        if (holder.rotation != holderWorldBefore || holder.localRotation != holderLocalBefore || holder.position != holderWorldPosBefore || holder.localPosition != holderLocalPosBefore || holder.parent != holderParentBefore || holder.childCount != holderChildCountBefore + 1)
        {
            // Restore holder to previous local rotation and position to enforce invariant
            holder.localRotation = holderLocalBefore;
            holder.localPosition = holderLocalPosBefore;
        }

        if (type == EquipmentType.Weapon)
            _weaponGrip = null;

        equippedItemObjects[type] = equipObj;
    }

    private static bool IsDescendantOf(Transform node, Transform potentialAncestor)
    {
        if (node == null || potentialAncestor == null) return false;
        var cur = node;
        while (cur != null)
        {
            if (cur == potentialAncestor) return true;
            cur = cur.parent;
        }
        return false;
    }

    private static Transform FindChildRecursive(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name)) return null;
        foreach (Transform child in root)
        {
            if (child.name == name) return child;
            var found = FindChildRecursive(child, name);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// Finds a projectile spawn transform on the currently equipped item of the given type.
    /// Falls back to sensible holders when equipment doesn't specify a specific spawn transform.
    /// </summary>
    public Transform GetProjectileSpawnTransform(EquipmentData equipment)
    {
        if (equipment != null && equipment.useEquipmentProjectileSpawn && !string.IsNullOrEmpty(equipment.projectileSpawnName))
        {
            // Try to find the instantiated equipment object for this slot
            if (equippedItemObjects != null)
            {
                foreach (var kv in equippedItemObjects)
                {
                    var go = kv.Value;
                    if (go == null) continue;
                    // Match by equipment prefab name to avoid exposing internals
                    if (go.name.Contains(equipment.equipmentPrefab != null ? equipment.equipmentPrefab.name : equipment.equipmentName))
                    {
                        var found = FindChildRecursive(go.transform, equipment.projectileSpawnName);
                        if (found != null) return found;
                    }
                }
            }
            // Also check extra equipped objects (e.g., projectile weapon instantiated into separate holder)
            if (extraEquippedItemObjects != null)
            {
                foreach (var kv in extraEquippedItemObjects)
                {
                    var go = kv.Value;
                    if (go == null) continue;
                    if (go.name.Contains(equipment.equipmentPrefab != null ? equipment.equipmentPrefab.name : equipment.equipmentName))
                    {
                        var found = FindChildRecursive(go.transform, equipment.projectileSpawnName);
                        if (found != null) return found;
                    }
                }
            }
        }

        // Fallback order when equipment doesn't provide a spawn:
        // 1) projectileWeaponHolder (separate ranged-weapon holder)
        // 2) weaponHolder (assumed melee weapon holder)
        // 3) this.transform as final fallback
        if (projectileWeaponHolder != null)
            return projectileWeaponHolder;

        if (weaponHolder != null)
            return weaponHolder;

        return this.transform;
    }

    private bool HasEnemyAdjacent()
    {
        if (TileDataHelper.Instance == null) return false;
        if (currentTileIndex < 0) return false;

        // Check this tile and neighbours for enemy occupants
        var (tileData, _) = TileDataHelper.Instance.GetTileData(currentTileIndex);
        if (tileData == null) return false;

        List<int> tilesToCheck = new List<int> { currentTileIndex };
        var neighbours = TileDataHelper.Instance.GetTileNeighbors(currentTileIndex);
        if (neighbours != null) tilesToCheck.AddRange(neighbours);

        foreach (int idx in tilesToCheck)
        {
            var (tdata, _) = TileDataHelper.Instance.GetTileData(idx);
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
        // Periodically check whether we should auto-disengage from melee
        if (engagedInMelee)
        {
            meleeCheckTimer += Time.deltaTime;
            if (meleeCheckTimer >= meleeCheckInterval)
            {
                meleeCheckTimer = 0f;
                if (!HasEnemyAdjacent())
                {
                    engagedInMelee = false;
                    if (meleeEngageCoroutine != null) { StopCoroutine(meleeEngageCoroutine); meleeEngageCoroutine = null; }
                }
            }
        }
        else
        {
            // reset timer when not engaged
            meleeCheckTimer = 0f;
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
        OnEquipmentChanged?.Invoke();
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
        OnEquipmentChanged?.Invoke();
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

    /// <summary>
    /// Spawns a projectile (as specified by equipment.projectileData) from the equipment's spawn transform toward a target position or target unit.
    /// Non-destructive: does not modify existing methods or state.
    /// </summary>
    public void SpawnProjectileFromEquipment(EquipmentData equipment, Vector3 targetPosition, CombatUnit targetUnit = null, int overrideDamage = -1)
    {
        if (equipment == null || equipment.projectileData == null || equipment.projectileData.projectilePrefab == null)
            return;

        Transform spawn = GetProjectileSpawnTransform(equipment);
        Vector3 startPos = spawn != null ? spawn.position : transform.position;

        GameObject projGO = null;
        // Try to spawn from the pool when available
        if (ProjectilePool.Instance != null)
        {
            projGO = ProjectilePool.Instance.Spawn(equipment.projectileData.projectilePrefab, startPos, Quaternion.identity);
        }
        else
        {
            projGO = Instantiate(equipment.projectileData.projectilePrefab, startPos, Quaternion.identity);
            // Ensure marker exists so Despawn can find the original prefab if a pool is later added
            var marker = projGO.GetComponent<PooledPrefabMarker>();
            if (marker == null) marker = projGO.AddComponent<PooledPrefabMarker>();
            marker.originalPrefab = equipment.projectileData.projectilePrefab;
        }

        if (projGO == null) return;

        Projectile proj = projGO.GetComponent<Projectile>();
        if (proj == null)
        {
            // If prefab doesn't already have Projectile, add one and configure a simple GameObject wrapper
            proj = projGO.AddComponent<Projectile>();
        }

        // Initialize the projectile with relevant data and override damage if provided
        proj.Initialize(equipment.projectileData, startPos, targetPosition, this.gameObject, targetUnit != null ? targetUnit.transform : null, overrideDamage);
    }

    /// <summary>
    /// Queue up a projectile to be fired later by an animation event.
    /// Callers should call FireQueuedProjectile() from animation event when the animation wants the projectile to appear.
    /// </summary>
    public void QueueProjectileForAnimation(EquipmentData equipment, Vector3 targetPosition, CombatUnit targetUnit, int damage)
    {
        queuedProjectileEquipment = equipment;
        queuedProjectileTargetUnit = targetUnit;
        queuedProjectileTargetPosition = targetPosition;
        queuedProjectileDamage = damage;
        hasQueuedProjectile = (equipment != null && equipment.projectileData != null);
    }

    /// <summary>
    /// Public method intended to be called by an animation event (e.g., an attack animation) to fire a previously queued projectile.
    /// </summary>
    public void FireQueuedProjectile()
    {
        if (!hasQueuedProjectile || queuedProjectileEquipment == null) return;
        SpawnProjectileFromEquipment(queuedProjectileEquipment, queuedProjectileTargetPosition, queuedProjectileTargetUnit, queuedProjectileDamage);
        // Clear queued
        hasQueuedProjectile = false;
        queuedProjectileEquipment = null;
        queuedProjectileTargetUnit = null;
        queuedProjectileDamage = -1;
    }

    /// <summary>
    /// Cancel any queued projectile (e.g., animation interrupted).
    /// </summary>
    public void CancelQueuedProjectile()
    {
        hasQueuedProjectile = false;
        queuedProjectileEquipment = null;
        queuedProjectileTargetUnit = null;
        queuedProjectileDamage = -1;
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
            OnEquipmentChanged?.Invoke();
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

    // --- Trap mechanics ---
    private int trappedTurnsRemaining = 0;
    public bool IsTrapped => trappedTurnsRemaining > 0;

    public void ApplyTrap(int turns)
    {
        trappedTurnsRemaining = Mathf.Max(trappedTurnsRemaining, turns);
        // Optional: visual/UI feedback could be triggered here
    }

    /// <summary>
    /// Handle mouse clicks on the combat unit
    /// </summary>
    void OnMouseDown()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null && 
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
        {
            // Click was on UI, ignore
            Debug.Log($"[CombatUnit] Click on {data.unitName} ignored, was on UI.");
            return;
        }
        
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
                Debug.Log($"[CombatUnit] Requested UnitInfoPanel for {data.unitName}");

                // Fallback notification if UnitInfoPanel is not available
                if (UIManager.Instance.unitInfoPanel == null || !UIManager.Instance.unitInfoPanel.activeInHierarchy)
                {
                    string msg = $"{data.unitName} (Combat)\nHealth: {currentHealth}/{MaxHealth}\nAttack: {CurrentAttack}  Defense: {CurrentDefense}\nMove: {currentMovePoints}/{BaseMovePoints}";
                    UIManager.Instance.ShowNotification(msg);
                }
            }
            else
            {
                Debug.LogError($"[CombatUnit] UIManager.Instance is null. Cannot show notification for {data.unitName}.");
            }
        }
    }
}