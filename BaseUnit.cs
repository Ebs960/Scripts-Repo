using System.Collections.Generic;
using UnityEngine;
using TMPro;
using GameCombat;

/// <summary>
/// Base abstract class for all units (CombatUnit, WorkerUnit).
/// Contains shared functionality: equipment, movement, health, animations, projectiles.
/// 
/// Architecture:
/// - BaseUnit handles common systems (equipment, movement, damage, animations)
/// - CombatUnit adds: morale, fatigue, ammunition, formations, battle system
/// - WorkerUnit adds: work points, building, foraging, city founding
/// </summary>
[RequireComponent(typeof(Animator))]
public abstract class BaseUnit : MonoBehaviour
{
    [Header("UI Anchors")]
    [Tooltip("Optional: Assign a child transform to control where the unit label appears. If not set, defaults to the unit root.")]
    [SerializeField] protected Transform labelAnchor;
    #region Equipment Fields
    
    [Header("Equipment Attachment Points")]
    [Tooltip("Transform where weapons will be attached")]
    public Transform weaponHolder;
    [Tooltip("Transform where projectile/ranged weapon visuals will be attached")]
    public Transform projectileWeaponHolder;
    [Tooltip("Transform where shields will be attached")]
    public Transform shieldHolder;
    [Tooltip("Transform where armor will be displayed")]
    public Transform armorHolder;
    [Tooltip("Transform where miscellaneous items will be attached")]
    public Transform miscHolder;

    [Header("Equipped Items")]
    [SerializeField] protected EquipmentData _equippedWeapon;
    [SerializeField] protected EquipmentData _equippedProjectileWeapon;
    [SerializeField] protected EquipmentData _equippedShield;
    [SerializeField] protected EquipmentData _equippedArmor;
    [SerializeField] protected EquipmentData _equippedMiscellaneous;

    [Header("Editor Equipment")]
    [Tooltip("If true, changing equipment in the Inspector will update visuals immediately in Edit mode.")]
    [SerializeField] protected bool updateEquipmentInEditor = true;

    [Header("Active Projectile")]
    [Tooltip("The projectile type this unit will use when firing ranged weapons")]
    [SerializeField] protected ProjectileData _activeProjectile;

    // Track instantiated equipment GameObjects
    protected Dictionary<EquipmentType, GameObject> equippedItemObjects = new Dictionary<EquipmentType, GameObject>();

    // Multi-soldier group (populated at runtime when soldierCount > 1)
    protected SoldierGroup soldierGroup;

    // Backwards-compatible equipped reference and abilities
    public EquipmentData equipped { get; protected set; }
    public List<Ability> unlockedAbilities { get; protected set; } = new List<Ability>();
    
    public event System.Action OnEquipmentChanged;

    /// <summary>
    /// Invoke the OnEquipmentChanged event (protected so subclasses can call it)
    /// </summary>
    protected void RaiseEquipmentChanged()
    {
        OnEquipmentChanged?.Invoke();
    }

    #endregion

    // --- Attack orchestrator ---
    public struct AttackContext
    {
        public BaseUnit attacker;
        public BaseUnit defender;
        public EquipmentData weapon; // may be null for unarmed
        public int damage;
        public bool isRanged;
        public bool isMelee;
    }

    /// <summary>
    /// Centralized step that applies damage from attacker to defender and performs
    /// shared post-hit handling. Returns true if defender died.
    /// Subclasses may call or override this to customize side-effects.
    /// </summary>
    public virtual bool PerformAttack(AttackContext ctx)
    {
        if (ctx.defender == null) return false;

        // Any attack order supersedes queued movement. Clear the existing move order
        // immediately so attacking does not leave stale continuation/path-preview state behind.
        if (ctx.attacker != null)
        {
            try
            {
                ctx.attacker.moveOrderPath = null;
                ctx.attacker.moveOrderNextStep = 0;
                UnitMovementController.Instance?.StopMoveForUnit(ctx.attacker);
                ctx.attacker.UpdateWalkingState(false);
                ctx.attacker.ClearFortify();
            }
            catch { }
        }

        // Ensure attacker has attack points available (centralized enforcement)
        if (ctx.attacker != null)
        {
            try
            {
                if (!ctx.attacker.TryConsumeAttackPoint())
                {
                    // Not enough AP to perform attack
                    return false;
                }
            }
            catch { }
        }

        // Play attack animation on the attacker (centralized)
        try
        {
            // Ensure attacker faces defender before attacking
            if (ctx.attacker != null && ctx.defender != null && ctx.attacker.transform != null && ctx.defender.transform != null)
            {
                var fwd = ctx.defender.transform.position - ctx.attacker.transform.position;
                fwd.y = 0f;
                if (fwd.sqrMagnitude > 0.0001f)
                    ctx.attacker.transform.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            }

            if (ctx.attacker != null && ctx.attacker.animator != null)
            {
                var au = (BaseUnit)ctx.attacker;
                au.SetAnimatorTriggerForFormation(attackHash);
            }
        }
        catch { }

        // Apply damage with attacker context (this will centralize death/reward raising in ApplyDamage)
        bool died = ctx.defender.ApplyDamage(ctx.damage, ctx.attacker, ctx.isMelee);

        // Centralized XP awarding: attackers that are CombatUnit gain XP for hits and additional XP on kills
        try
        {
            if (ctx.attacker != null && ctx.attacker is CombatUnit cu)
            {
                // Award XP for the hit
                cu.GainExperience(ctx.damage);

                // Award bonus XP for the kill
                if (died)
                    cu.GainExperience(ctx.damage);
            }
        }
        catch { }

        return died;
    }

    /// <summary>
    /// Unified attack entry point for all unit types. Subclasses should override
    /// to route to their specialized attack implementations (melee/ranged, worker/combat).
    /// External callers can call this method without needing to know the concrete unit type.
    /// </summary>
    public virtual void Attack(BaseUnit target)
    {
        Debug.LogWarning($"[BaseUnit] Attack(BaseUnit) not overridden on {GetType().Name} (target={target?.GetType().Name})");
    }

    #region Core Unit Fields

    [Header("Unit UI")]
    [SerializeField] protected GameObject unitLabelPrefab;
    protected UnitLabel unitLabelInstance;
    [Header("Popup Settings")]
    [SerializeField]
    [Tooltip("Vertical offset above unit for popup")]
    private float healthPopupVerticalOffset = 1.5f;
    [SerializeField]
    [Tooltip("Rise distance for popup animation")]
    private float healthPopupRiseDistance = 1.5f;
    [SerializeField]
    [Tooltip("Duration of popup animation in seconds")]
    private float healthPopupDuration = 0.7f;
    [SerializeField]
    [Tooltip("Scale applied to floating health/damage popups")]
    private float healthPopupScale = 1.2f;
    [SerializeField]
    [Tooltip("Font size used for floating health/damage popups")]
    private float healthPopupFontSize = 7f;
    private static readonly Color DamagePopupColor = new Color(0.9f, 0.2f, 0.2f, 1f);
    private static readonly Color HealPopupColor = new Color(0.2f, 0.9f, 0.2f, 1f);
    private static readonly Color HealthPopupOutlineColor = new Color(0f, 0f, 0f, 0.9f);

    [Header("Weather")]
    [Tooltip("If true, this unit takes weather attrition in severe seasons")]
    public bool takesWeatherDamage = true;

    [Header("Action Points")]
    [Tooltip("How many attacks/actions this unit can perform per turn.")]
    [SerializeField]
    protected int attackPointsPerTurn = 1;

    [System.NonSerialized]
    protected int currentAttackPoints = 0;

    public int CurrentAttackPoints => currentAttackPoints;

    public bool HasAttackPoints() => currentAttackPoints > 0;

    public bool TryConsumeAttackPoint()
    {
        int oldAttackPoints = currentAttackPoints;
        if (currentAttackPoints <= 0) return false;
        currentAttackPoints = Mathf.Max(0, currentAttackPoints - 1);
        if (currentAttackPoints <= 0)
        {
            var cu = this as CombatUnit;
            if (cu != null) cu.ConsumeAction();
        }
        try { GameEventManager.Instance?.RaiseAttackPointsChanged(this, oldAttackPoints, currentAttackPoints, MaxAttackPoints); } catch { }
        return true;
    }

    public virtual void ResetAttackPointsForNewTurn()
    {
        int oldAttackPoints = currentAttackPoints;
        currentAttackPoints = attackPointsPerTurn;
        try { GameEventManager.Instance?.RaiseAttackPointsChanged(this, oldAttackPoints, currentAttackPoints, MaxAttackPoints); } catch { }
    }

    // Public accessor for the per-turn max AP (configurable per-unit via data assets)
    public int MaxAttackPoints => attackPointsPerTurn;

    // Core references
    protected HexGrid grid;
    protected PlanetGenerator planet;
    protected Animator animator;

    // Runtime state
    public Civilization owner { get; protected set; }
    // Single source of truth for queued movement: full path and cursor.
    [System.NonSerialized] public System.Collections.Generic.List<int> moveOrderPath = null;
    [System.NonSerialized] public int moveOrderNextStep = 0;
    [System.NonSerialized] private bool isFortified = false;
    public int currentHealth { get; protected set; }
    public int currentTileIndex = -1;
    public TileLayer currentLayer = TileLayer.Surface;
    [Tooltip("Which planet this unit belongs to (multi-planet gameplay).")]
    public int planetIndex = -1;
    public float moveSpeed = 2f;
    public bool isMoving { get; set; }
    public bool IsFortified => isFortified;

    // Projectile queueing
    [Header("Projectiles")]
    [Tooltip("If true, projectiles fire via animation event; if false, fire immediately")]
    public bool useAnimationEventForProjectiles = true;
    protected EquipmentData queuedProjectileEquipment;
    protected CombatUnit queuedProjectileTargetUnit;
    protected Vector3 queuedProjectileTargetPosition;
    protected int queuedProjectileDamage = -1;
    protected bool hasQueuedProjectile = false;
    protected bool engagedInMelee = false;

    // Trap immobilization
    protected int trappedTurnsRemaining = 0;
    public bool IsTrapped => trappedTurnsRemaining > 0;
    
    // Winter penalty flag
    public bool hasWinterPenalty { get; set; }

    // Stored state when a unit is placed inside a shelter improvement
    [System.NonSerialized]
    public bool isStored = false;
    [System.NonSerialized]
    public ImprovementInstance storedInImprovement = null;
    [System.NonSerialized]
    public Herd storedInHerd = null;
    #endregion

    #region Animation Hashes

    // Animation parameter hashes for efficiency
    protected static readonly int isWalkingHash = Animator.StringToHash("IsWalking");
    protected static readonly int attackHash = Animator.StringToHash("Attack");
    protected static readonly int hitHash = Animator.StringToHash("Hit");
    protected static readonly int deathHash = Animator.StringToHash("Death");
    protected static readonly int routHash = Animator.StringToHash("Rout");
    protected static readonly int isFortifiedHash = Animator.StringToHash("IsFortified");

    // Cached parameter-existence flags (set once in Awake, avoids allocating parameters array each call)
    protected bool _hasWalkParam;
    protected bool _hasHitParam;
    protected bool _hasDeathParam;
    protected bool _hasFortifyParam;

    #endregion

    #region Soldier Group

    /// <summary>
    /// Initialize the multi-soldier visual group from data fields.
    /// Call once after the unit is fully initialized and positioned.
    /// </summary>
    protected void InitializeSoldierGroup(int count, SoldierVariant[] variants, FormationType formationType, float formationSpacing)
    {
        if (count <= 1) return;

        soldierGroup = gameObject.GetComponent<SoldierGroup>();
        if (soldierGroup == null)
            soldierGroup = gameObject.AddComponent<SoldierGroup>();

        soldierGroup.Initialize(count, variants, formationType, formationSpacing, gameObject.GetInstanceID());
        DistributeEquipmentToSoldiers();
        if (animator != null)
            soldierGroup.SyncBoolParametersFrom(animator);
    }

    protected void SetAnimatorBoolForFormation(int hash, bool value)
    {
        if (animator != null && HasParameter(animator, hash))
            animator.SetBool(hash, value);

        if (soldierGroup != null)
        {
            soldierGroup.ForwardBool(hash, value);
            if (animator != null)
                soldierGroup.SyncBoolParametersFrom(animator);
        }
    }

    protected void SetAnimatorTriggerForFormation(int hash)
    {
        if (animator != null && HasParameter(animator, hash))
            animator.SetTrigger(hash);

        if (soldierGroup != null)
            soldierGroup.ForwardTrigger(hash);
    }

    /// <summary>
    /// Push current equipment state to all soldiers in the group.
    /// </summary>
    protected void DistributeEquipmentToSoldiers()
    {
        if (soldierGroup == null) return;
        soldierGroup.DistributeEquipment(
            _equippedWeapon,
            _equippedProjectileWeapon,
            _equippedShield,
            _equippedArmor,
            _equippedMiscellaneous);
    }

    #endregion

    #region Abstract Properties (must be implemented by subclasses)

    /// <summary>Unit's display name from data asset</summary>
    public abstract string UnitName { get; }
    
    /// <summary>Base attack stat from data asset</summary>
    public abstract int BaseAttack { get; }
    
    /// <summary>Base defense stat from data asset</summary>
    public abstract int BaseDefense { get; }
    
    /// <summary>Base health stat from data asset</summary>
    public abstract int BaseHealth { get; }
    
    /// <summary>Base range stat from data asset</summary>
    public abstract float BaseRange { get; }
    
    /// <summary>Maximum health including all bonuses</summary>
    public abstract int MaxHealth { get; }

    /// <summary>Returns the target equipment type this unit accepts</summary>
    protected abstract EquipmentTarget AcceptedEquipmentTarget { get; }

    /// <summary>Duration unit stays in melee after being hit</summary>
    // Melee engagement duration deprecated — engagement state is managed by range checks / attack logic now.

    #endregion

    #region Equipment Properties

    public ProjectileData ActiveProjectile
    {
        get => _activeProjectile;
        set => _activeProjectile = value;
    }

    public EquipmentData equippedWeapon
    {
        get => _equippedWeapon;
        set
        {
            if (_equippedWeapon == value) return;
            if (value != null && !IsEquipmentCompatible(value)) return;
            _equippedWeapon = value;
            if (Application.isPlaying || updateEquipmentInEditor)
                UpdateEquipmentVisuals();
        }
    }

    public EquipmentData equippedProjectileWeapon
    {
        get => _equippedProjectileWeapon;
        protected set
        {
            if (_equippedProjectileWeapon == value) return;
            _equippedProjectileWeapon = value;
            if (Application.isPlaying || updateEquipmentInEditor)
                UpdateEquipmentVisuals();
        }
    }

    public EquipmentData equippedShield
    {
        get => _equippedShield;
        set
        {
            if (_equippedShield == value) return;
            if (value != null && !IsEquipmentCompatible(value)) return;
            _equippedShield = value;
            if (Application.isPlaying || updateEquipmentInEditor)
                UpdateEquipmentVisuals();
        }
    }

    public EquipmentData equippedArmor
    {
        get => _equippedArmor;
        set
        {
            if (_equippedArmor == value) return;
            if (value != null && !IsEquipmentCompatible(value)) return;
            _equippedArmor = value;
            if (Application.isPlaying || updateEquipmentInEditor)
                UpdateEquipmentVisuals();
        }
    }

    public EquipmentData equippedMiscellaneous
    {
        get => _equippedMiscellaneous;
        set
        {
            if (_equippedMiscellaneous == value) return;
            if (value != null && !IsEquipmentCompatible(value)) return;
            _equippedMiscellaneous = value;
            if (Application.isPlaying || updateEquipmentInEditor)
                UpdateEquipmentVisuals();
        }
    }

    /// <summary>Check if equipment is compatible with this unit type</summary>
    protected bool IsEquipmentCompatible(EquipmentData equipment)
    {
        if (equipment == null) return true;
        return equipment.targetUnit == EquipmentTarget.Both || 
               equipment.targetUnit == AcceptedEquipmentTarget;
    }

    #endregion

    #region Equipment Stat Bonuses

    public float EquipmentAttackBonus =>
        (_equippedWeapon?.attackBonus ?? 0f) +
        (_equippedShield?.attackBonus ?? 0f) +
        (_equippedArmor?.attackBonus ?? 0f) +
        (_equippedMiscellaneous?.attackBonus ?? 0f);

    public float EquipmentDefenseBonus =>
        (_equippedWeapon?.defenseBonus ?? 0f) +
        (_equippedShield?.defenseBonus ?? 0f) +
        (_equippedArmor?.defenseBonus ?? 0f) +
        (_equippedMiscellaneous?.defenseBonus ?? 0f);

    public float EquipmentHealthBonus =>
        (_equippedWeapon?.healthBonus ?? 0f) +
        (_equippedShield?.healthBonus ?? 0f) +
        (_equippedArmor?.healthBonus ?? 0f) +
        (_equippedMiscellaneous?.healthBonus ?? 0f);

    public float EquipmentMoveBonus =>
        (_equippedWeapon?.movementBonus ?? 0f) +
        (_equippedShield?.movementBonus ?? 0f) +
        (_equippedArmor?.movementBonus ?? 0f) +
        (_equippedMiscellaneous?.movementBonus ?? 0f);

    public float EquipmentRangeBonus =>
        (_equippedWeapon?.rangeBonus ?? 0f) +
        (_equippedShield?.rangeBonus ?? 0f) +
        (_equippedArmor?.rangeBonus ?? 0f) +
        (_equippedMiscellaneous?.rangeBonus ?? 0f);

    #endregion

    #region Ability Modifiers

    public int GetAbilityAttackModifier()
    {
        int total = 0;
        if (unlockedAbilities == null) return total;
        foreach (var ability in unlockedAbilities)
            total += ability.attackModifier;
        return total;
    }

    public int GetAbilityDefenseModifier()
    {
        int total = 0;
        if (unlockedAbilities == null) return total;
        foreach (var ability in unlockedAbilities)
            total += ability.defenseModifier;
        return total;
    }

    public int GetAbilityHealthModifier()
    {
        int total = 0;
        if (unlockedAbilities == null) return total;
        foreach (var ability in unlockedAbilities)
            total += ability.healthModifier;
        return total;
    }

    public int GetAbilityRangeModifier()
    {
        int total = 0;
        if (unlockedAbilities == null) return total;
        foreach (var ability in unlockedAbilities)
            total += ability.rangeModifier;
        return total;
    }

    public float GetAbilityDamageMultiplier()
    {
        float total = 1f;
        if (unlockedAbilities == null) return total;
        foreach (var ability in unlockedAbilities)
            total *= ability.damageMultiplier;
        return total;
    }

    #endregion

    #region Current Stats (virtual - can be overridden for additional bonuses)

    public virtual int CurrentAttack
    {
        get
        {
            float valF = BaseAttack + EquipmentAttackBonus + GetAbilityAttackModifier();
            return Mathf.RoundToInt(valF);
        }
    }

    public virtual int CurrentDefense
    {
        get
        {
            return Mathf.RoundToInt(GetCurrentDefenseValueFloat());
        }
    }

    protected virtual float GetCurrentDefenseValueFloat()
    {
        float valF = BaseDefense + EquipmentDefenseBonus + GetAbilityDefenseModifier();
        valF = ApplyOwnerDefenseBonuses(valF);
        valF = ApplyTileDefenseBonuses(valF);
        valF = ApplyFortifyDefenseBonus(valF);
        return valF;
    }

    protected virtual float ApplyOwnerDefenseBonuses(float defenseValue)
    {
        return defenseValue;
    }

    protected virtual float ApplyTileDefenseBonuses(float defenseValue)
    {
        if (currentTileIndex < 0) return defenseValue;

        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var tileData = ts != null ? ts.GetTileData(currentTileIndex) : null;
        if (tileData == null) return defenseValue;

        defenseValue += tileData.improvementDefenseAdd;
        defenseValue *= (1f + tileData.improvementDefensePct);
        return defenseValue;
    }

    public virtual float CurrentRange
    {
        get
        {
            float valF = BaseRange + EquipmentRangeBonus + GetAbilityRangeModifier();
            return valF;
        }
    }

    #endregion

    #region Unity Lifecycle

    protected virtual void Awake()
    {
        // Find animator (check children first, like CombatUnit does)
        animator = GetComponentInChildren<Animator>();
        if (animator == null)
            animator = GetComponent<Animator>();

        // Cache parameter existence once so we never iterate anim.parameters at runtime
        if (animator != null)
        {
            animator.applyRootMotion = false;
            _hasWalkParam  = HasParameter(animator, isWalkingHash);
            _hasHitParam   = HasParameter(animator, hitHash);
            _hasDeathParam = HasParameter(animator, deathHash);
            _hasFortifyParam = HasParameter(animator, isFortifiedHash);
        }

        // Bind to the correct planet/grid for multi-planet gameplay.
        // Priority:
        // 1) Explicitly set planetIndex (spawners/transport should set this)
        // 2) Parent PlanetGenerator (if unit is parented under a planet)
        // 3) Current planet in GameManager
        // 4) Earth (0) fallback
        if (planetIndex < 0)
        {
            var pg = GetComponentInParent<PlanetGenerator>();
            if (pg != null) planetIndex = pg.planetIndex;
        }
        if (planetIndex < 0 && GameManager.Instance != null) planetIndex = GameManager.Instance.currentPlanetIndex;
        if (planetIndex < 0) planetIndex = 0;

        // Resolve planet generator with diagnostics so we can detect when fallbacks are used.
        PlanetGenerator resolved = null;
        if (owner != null)
        {
            try { resolved = owner.GetPlanetGeneratorForIndex(planetIndex); } catch { resolved = null; }
            if (resolved == null)
            {
                Debug.LogWarning($"[BaseUnit] Owner '{owner.civData?.civName ?? owner.name}' returned null for GetPlanetGeneratorForIndex({planetIndex}); falling back to GameManager.");
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
                    Debug.LogWarning($"[BaseUnit] GameManager has no generator for planetIndex {planetIndex}; falling back to current planet generator.");
                    resolved = gm.GetCurrentPlanetGenerator();
                }
            }
            else
            {
                Debug.LogWarning("[BaseUnit] GameManager.Instance is null; cannot resolve PlanetGenerator.");
            }
        }

        planet = resolved;
        if (planet != null) grid = planet.Grid;

        // NOTE: registration with the global UnitRegistry must now be performed
        // explicitly by spawn/placement code after the unit has been properly
        // initialized and positioned. This avoids claiming tile occupancy using
        // prefab-serialized values during Awake/registration time.
    }

    /// <summary>
    /// Register this unit with the global `UnitRegistry`.
    /// Spawners and placement code should call this after initialization and
    /// before assigning tile occupancy.
    /// </summary>
    public void RegisterToRegistry()
    {
        UnitRegistry.Register(gameObject);
    }

    protected virtual void Start()
    {
        if (Application.isPlaying)
        {
            UpdateEquipmentVisuals();
        }
    }

    protected virtual void OnDestroy()
    {
        // DIAGNOSTIC (animal-only): animals are currently being destroyed immediately after spawn.
        // Log a stack trace so we can identify the true destroy caller.
        try
        {
            var cu = this as CombatUnit;
            if (cu != null &&
                cu.data != null &&
                cu.data.unitType == CombatCategory.Animal &&
                AnimalManager.Instance != null &&
                AnimalManager.Instance.debugSpawning)
            {
                string sceneName = gameObject != null && gameObject.scene.IsValid() ? gameObject.scene.name : "<invalid>";
                string parentName = transform != null && transform.parent != null ? transform.parent.name : "<none>";
                string compDump = "";
                try
                {
                    int id = gameObject != null ? gameObject.GetInstanceID() : 0;
                    if (id != 0 && AnimalManager.Instance.TryGetSpawnComponentDump(id, out var dump))
                    {
                        compDump = "\n" + dump;
                        AnimalManager.Instance.ClearSpawnComponentDump(id);
                    }
                }
                catch { }
                Debug.LogWarning(
                    $"[BaseUnit][AnimalDestroyDiag] Animal OnDestroy: name='{name}' id={(gameObject!=null?gameObject.GetInstanceID():0)} " +
                    $"scene={sceneName} frame={Time.frameCount} time={Time.time:F3} " +
                    $"planetIndex={planetIndex} tile={currentTileIndex} layer={currentLayer} ownerNull={(owner==null)} parent={parentName}\n" +
                    $"StackTrace:\n{System.Environment.StackTrace}" +
                    compDump
                );
            }
        }
        catch { }

        // Clean up equipment GameObjects
        foreach (var item in equippedItemObjects.Values)
        {
            if (item != null)
                Destroy(item);
        }
        equippedItemObjects.Clear();

        // Unregister
        UnitRegistry.Unregister(gameObject);
    }

#if UNITY_EDITOR
    protected virtual void OnValidate()
    {
        if (!Application.isPlaying && updateEquipmentInEditor)
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && !Application.isPlaying && updateEquipmentInEditor)
                {
                    ValidateEquipmentCompatibility();
                    UpdateEquipmentVisuals();
                    UnityEditor.EditorUtility.SetDirty(this);
                }
            };
        }
    }

    protected virtual void ValidateEquipmentCompatibility()
    {
        // Clear incompatible equipment
        if (_equippedWeapon != null && !IsEquipmentCompatible(_equippedWeapon))
            _equippedWeapon = null;
        if (_equippedShield != null && !IsEquipmentCompatible(_equippedShield))
            _equippedShield = null;
        if (_equippedArmor != null && !IsEquipmentCompatible(_equippedArmor))
            _equippedArmor = null;
        if (_equippedMiscellaneous != null && !IsEquipmentCompatible(_equippedMiscellaneous))
            _equippedMiscellaneous = null;
    }
#endif

    #endregion

    #region Equipment Visual Management

    /// <summary>
    /// Updates all equipment visuals. Override in subclasses for custom behavior.
    /// </summary>
    public virtual void UpdateEquipmentVisuals()
    {
        // Remove existing equipment objects
        foreach (var item in equippedItemObjects.Values)
        {
            if (item != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    DestroyImmediate(item);
                else
#endif
                {
                    // Pool equipment visuals in play mode to reduce Instantiate/Destroy churn.
                    EquipmentVisualPool.Release(item);
                }
            }
        }
        equippedItemObjects.Clear();

        // Process all equipment slots
        ProcessEquipmentSlot(EquipmentType.Weapon, _equippedWeapon, weaponHolder);
        ProcessEquipmentSlot(EquipmentType.Weapon, _equippedProjectileWeapon, projectileWeaponHolder);
        ProcessEquipmentSlot(EquipmentType.Shield, _equippedShield, shieldHolder);
        ProcessEquipmentSlot(EquipmentType.Armor, _equippedArmor, armorHolder);
        ProcessEquipmentSlot(EquipmentType.Miscellaneous, _equippedMiscellaneous, miscHolder);

        // Distribute equipment to additional soldiers in the group
        DistributeEquipmentToSoldiers();
    }

    protected virtual void ProcessEquipmentSlot(EquipmentType type, EquipmentData itemData, Transform holder)
    {
        if (holder == null) return;

        // Clear existing children under holder
        for (int i = holder.childCount - 1; i >= 0; i--)
        {
            var child = holder.GetChild(i);
            if (child != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    DestroyImmediate(child.gameObject);
                else
#endif
                {
                    // If this was an equipment visual we created, return it to pool; otherwise destroy.
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

    protected virtual void UpdateEquipmentSlot(EquipmentType type, EquipmentData itemData, Transform holder)
    {
        if (holder == null || itemData == null || itemData.equipmentPrefab == null) return;

        // Acquire equipment visual (pooled in play mode, instantiated in edit mode).
        GameObject equipObj =
#if UNITY_EDITOR
            (!Application.isPlaying) ? Instantiate(itemData.equipmentPrefab) :
#endif
            EquipmentVisualPool.Acquire(itemData.equipmentPrefab);

        // Preserve authored local rotation from prefab (pool resets to prefab-authored local).
        Quaternion authoredLocal = equipObj.transform.localRotation;
        equipObj.transform.SetParent(holder, false);
        equipObj.transform.localPosition = Vector3.zero;
        equipObj.transform.localRotation = authoredLocal;

        // Enable renderers if disabled
        var renderers = equipObj.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r != null && !r.enabled)
                r.enabled = true;
        }

        equippedItemObjects[type] = equipObj;
    }

    /// <summary>
    /// Equips an item in the appropriate slot based on its type
    /// </summary>
    public virtual void EquipItem(EquipmentData equipmentData)
    {
        if (equipmentData == null) return;
        if (!IsEquipmentCompatible(equipmentData)) return;

        bool changed = false;
        switch (equipmentData.equipmentType)
        {
            case EquipmentType.Weapon:
                if (equipmentData.projectileData != null)
                {
                    if (_equippedProjectileWeapon != equipmentData)
                    {
                        equippedProjectileWeapon = equipmentData;
                        changed = true;
                    }
                }
                else
                {
                    if (_equippedWeapon != equipmentData)
                    {
                        equippedWeapon = equipmentData;
                        changed = true;
                    }
                }
                break;
            case EquipmentType.Shield:
                if (_equippedShield != equipmentData)
                {
                    equippedShield = equipmentData;
                    changed = true;
                }
                break;
            case EquipmentType.Armor:
                if (_equippedArmor != equipmentData)
                {
                    equippedArmor = equipmentData;
                    changed = true;
                }
                break;
            case EquipmentType.Miscellaneous:
                if (_equippedMiscellaneous != equipmentData)
                {
                    equippedMiscellaneous = equipmentData;
                    changed = true;
                }
                break;
        }

        equipped = equipmentData;
        if (changed)
        {
            UpdateEquipmentVisuals();
            OnEquipmentChanged?.Invoke();
        }
    }

    #endregion

    #region Projectile System

    /// <summary>
    /// Find the transform to spawn projectiles from
    /// </summary>
    public Transform GetProjectileSpawnTransform(EquipmentData equipment)
    {
        if (equipment != null && equipment.useEquipmentProjectileSpawn && 
            !string.IsNullOrEmpty(equipment.projectileSpawnName))
        {
            foreach (var kv in equippedItemObjects)
            {
                var go = kv.Value;
                if (go == null) continue;
                if (equipment.equipmentPrefab != null && 
                    go.name.Contains(equipment.equipmentPrefab.name))
                {
                    var found = FindChildRecursive(go.transform, equipment.projectileSpawnName);
                    if (found != null) return found;
                }
            }
        }

        if (projectileWeaponHolder != null) return projectileWeaponHolder;
        if (weaponHolder != null) return weaponHolder;
        return transform;
    }

    /// <summary>
    /// Spawn a projectile from equipment towards a target
    /// </summary>
    public virtual void SpawnProjectileFromEquipment(EquipmentData equipment, Vector3 targetPosition, 
        CombatUnit targetUnit = null, int overrideDamage = -1)
    {
        // Priority 1: Use unit's active projectile if it matches weapon's category
        ProjectileData projectileToUse = null;

        if (_activeProjectile != null && equipment != null && equipment.usesProjectiles &&
            _activeProjectile.category == equipment.projectileCategory)
        {
            projectileToUse = _activeProjectile;
        }
        // Priority 2: Fall back to equipment's default projectile
        else if (equipment != null && equipment.projectileData != null)
        {
            projectileToUse = equipment.projectileData;
        }

        if (projectileToUse == null || projectileToUse.projectilePrefab == null) return;

        Transform spawn = GetProjectileSpawnTransform(equipment);
        Vector3 startPos = spawn != null ? spawn.position : transform.position;

        GameObject projGO = null;
        if (SimpleObjectPool.Instance != null)
        {
            projGO = SimpleObjectPool.Instance.Get(projectileToUse.projectilePrefab, startPos, Quaternion.identity);
        }
        else
        {
            projGO = Instantiate(projectileToUse.projectilePrefab, startPos, Quaternion.identity);
            var marker = projGO.GetComponent<PooledPrefabMarker>();
            if (marker == null) marker = projGO.AddComponent<PooledPrefabMarker>();
            marker.originalPrefab = projectileToUse.projectilePrefab;
        }

        if (projGO == null) return;

        Projectile proj = projGO.GetComponent<Projectile>();
        if (proj == null) proj = projGO.AddComponent<Projectile>();

        proj.Initialize(projectileToUse, startPos, targetPosition, gameObject, 
            targetUnit != null ? targetUnit.transform : null, overrideDamage);
    }

    /// <summary>
    /// Queue a projectile to be fired via animation event
    /// </summary>
    public void QueueProjectileForAnimation(EquipmentData equipment, Vector3 targetPosition, 
        CombatUnit targetUnit, int damage)
    {
        queuedProjectileEquipment = equipment;
        queuedProjectileTargetUnit = targetUnit;
        queuedProjectileTargetPosition = targetPosition;
        queuedProjectileDamage = damage;
        hasQueuedProjectile = (equipment != null && equipment.projectileData != null);
    }

    /// <summary>
    /// Called by animation event to fire queued projectile
    /// </summary>
    public void FireQueuedProjectile()
    {
        if (!hasQueuedProjectile || queuedProjectileEquipment == null) return;

        SpawnProjectileFromEquipment(queuedProjectileEquipment, queuedProjectileTargetPosition, 
            queuedProjectileTargetUnit, queuedProjectileDamage);

        hasQueuedProjectile = false;
        queuedProjectileEquipment = null;
        queuedProjectileTargetUnit = null;
        queuedProjectileDamage = -1;
    }

    /// <summary>
    /// Cancel any queued projectile
    /// </summary>
    public void CancelQueuedProjectile()
    {
        hasQueuedProjectile = false;
        queuedProjectileEquipment = null;
        queuedProjectileTargetUnit = null;
        queuedProjectileDamage = -1;
    }

    #endregion

    #region Damage System

    /// <summary>
    /// Apply damage to this unit. Override in subclasses for additional behavior.
    /// </summary>
    public virtual bool ApplyDamage(int damageAmount)
    {
        return ApplyDamage(damageAmount, null, false);
    }

    /// <summary>
    /// Apply damage with attacker context for melee engagement tracking
    /// </summary>
    public virtual bool ApplyDamage(int damageAmount, BaseUnit attacker, bool attackerIsMelee)
    {
        int previousHealth = currentHealth;
        if (attackerIsMelee)
        {
            // Mark engaged in melee — duration handling deprecated; engagement state should be managed
            // by range/attack logic or explicit code paths.
            engagedInMelee = true;
        }

        // Inline damage application so we can raise the damage event with attacker context (avoid double events)
        try
        {
            var cu = this as CombatUnit;
            if (cu != null && cu.data != null && cu.data.unitType == CombatCategory.Animal && AnimalManager.Instance != null && AnimalManager.Instance.debugSpawning)
            {
                Debug.LogWarning($"[BaseUnit][AnimalDamageDiag] ApplyDamage called: name='{name}' id={(gameObject!=null?gameObject.GetInstanceID():0)} damage={damageAmount} hpBefore={currentHealth} maxHP={MaxHealth} frame={Time.frameCount} time={Time.time:F3}\nStackTrace:\n{System.Environment.StackTrace}");
            }
        }
        catch { }

        // If attacker exists, rotate to face attacker before playing hit animation
        try
        {
            if (attacker != null && attacker.transform != null && transform != null)
            {
                var toAttacker = attacker.transform.position - transform.position;
                toAttacker.y = 0f;
                if (toAttacker.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(toAttacker.normalized, Vector3.up);
            }
        }
        catch { }

        if (animator != null && _hasHitParam)
            SetAnimatorTriggerForFormation(hitHash);

        currentHealth -= damageAmount;
        ShowHealthChangePopup(-Mathf.Abs(damageAmount));
        // Update multi-soldier attrition visuals
        if (soldierGroup != null) soldierGroup.UpdateAttrition(currentHealth, MaxHealth);
        try { GameEventManager.Instance?.RaiseHealthChanged(this, previousHealth, currentHealth, MaxHealth); } catch { }

        // Animals remember recent attacks for predator/prey behavior.
        try
        {
            var combatUnit = this as CombatUnit;
            if (combatUnit != null &&
                combatUnit.data != null &&
                combatUnit.data.unitType == CombatCategory.Animal &&
                AnimalManager.Instance != null)
            {
                AnimalManager.Instance.MarkAnimalAsAttacked(combatUnit);
            }
        }
        catch { }

        // Update label
        UpdateUnitLabel();

        // Raise damage event with attacker context
        try { GameEventManager.Instance?.RaiseDamageAppliedEvent(attacker, this, damageAmount); } catch { }

        if (currentHealth <= 0)
        {
            // Centralized kill handling: award foodOnKill to attacker civ and raise killed event
            try
            {
                if (attacker != null)
                {
                    var deadCombat = this as CombatUnit;
                    var deadWorker = this as WorkerUnit;

                    // Dead animals/workers are NOT automatically captured anymore.
                    // Award foodOnKill as before (if configured).
                    int food = 0;
                    if (deadCombat != null && deadCombat.data != null)
                        food = deadCombat.data.foodOnKill;
                    else if (deadWorker != null && deadWorker.data != null)
                        food = deadWorker.data.foodOnKill;

                    if (food > 0 && attacker.owner != null)
                        attacker.owner.AddFood(food);
                }

                GameEventManager.Instance?.RaiseUnitKilledEvent(attacker, this, damageAmount);
            }
            catch { }

            Die();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Heal this unit by the specified amount, capped at MaxHealth.
    /// </summary>
    public virtual void Heal(int amount)
    {
        if (amount <= 0) return;
        int previousHealth = currentHealth;
        currentHealth = Mathf.Min(currentHealth + amount, MaxHealth);
        int actualHealed = currentHealth - previousHealth;
        if (actualHealed <= 0) return;

        ShowHealthChangePopup(actualHealed);
        // Revive soldiers when healed
        if (soldierGroup != null) soldierGroup.UpdateAttrition(currentHealth, MaxHealth);
        try { GameEventManager.Instance?.RaiseHealthChanged(this, previousHealth, currentHealth, MaxHealth); } catch { }
        UpdateUnitLabel();
    }

    // Melee engagement timeout coroutine removed (deprecated).

    /// <summary>
    /// Handle unit death. Override in subclasses for additional cleanup.
    /// </summary>
    protected virtual void Die()
    {
        try { GameEventManager.Instance?.RaiseUnitLostEvent(this, null, currentTileIndex, planetIndex); } catch { }

        // DIAGNOSTIC (animal-only): if animals are dying, this stack trace will include the real caller chain.
        try
        {
            var cu = this as CombatUnit;
            if (cu != null &&
                cu.data != null &&
                cu.data.unitType == CombatCategory.Animal &&
                AnimalManager.Instance != null &&
                AnimalManager.Instance.debugSpawning)
            {
                Debug.LogWarning(
                    $"[BaseUnit][AnimalDieDiag] Die called: name='{name}' id={(gameObject!=null?gameObject.GetInstanceID():0)} " +
                    $"hp={currentHealth}/{MaxHealth} frame={Time.frameCount} time={Time.time:F3}\n" +
                    $"StackTrace:\n{System.Environment.StackTrace}"
                );
            }
        }
        catch { }

        if (animator != null && _hasDeathParam)
            SetAnimatorTriggerForFormation(deathHash);

        // Clear tile occupancy (layer-aware)
        if (currentTileIndex >= 0)
        {
            try
            {
                var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
                if (occ != null)
                {
                    occ.ClearOccupant(currentTileIndex, currentLayer);
                }
                else
                {
                    var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
                    ts?.ClearTileOccupant(currentTileIndex);
                }
            }
            catch { }
        }

        // Destroy label
        if (unitLabelInstance != null)
        {
            Destroy(unitLabelInstance.gameObject);
        }

        // Destroy with delay for death animation
        Destroy(gameObject, 2.5f);
    }

    /// <summary>
    /// Apply trap immobilization effect
    /// </summary>
    public void ApplyTrap(int turns)
    {
        trappedTurnsRemaining = Mathf.Max(trappedTurnsRemaining, turns);
    }

    #endregion

    #region Movement

    /// <summary>
    /// Whether this unit can enter orbit around its current planet.
    /// Minimal rule: only CombatUnit Spaceships may enter orbit (no travel changes).
    /// </summary>
    public virtual bool CanEnterOrbit()
    {
        // Prefer an explicit data-driven flag when available.
        var cu = this as CombatUnit;
        if (cu != null && cu.data != null)
        {
            if (cu.data.canEnterOrbit) return true;
            // Backwards-compat: treat Spaceship category as allowed if explicit flag not set.
            return cu.data.unitType == CombatCategory.Spaceship;
        }

        var wu = this as WorkerUnit;
        if (wu != null && wu.data != null)
        {
            return wu.data.canEnterOrbit;
        }

        return false;
    }

    /// <summary>
    /// Place this unit into orbit over a specific tile index.
    /// This updates occupancy (Orbit layer) and the unit's current layer.
    /// No interplanetary travel is performed here.
    /// </summary>
    public virtual void EnterOrbit(int tileIndex)
    {
        if (!CanEnterOrbit())
        {
            Debug.LogWarning($"[BaseUnit] {name} cannot enter orbit (unit not permitted).");
            return;
        }

        if (tileIndex < 0)
        {
            Debug.LogWarning($"[BaseUnit] {name} EnterOrbit called with invalid tileIndex={tileIndex}.");
            return;
        }

        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null)
        {
            Debug.LogWarning($"[BaseUnit] {name} cannot enter orbit: TileSystem missing for planetIndex={planetIndex}.");
            return;
        }

        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        if (occ == null)
        {
            Debug.LogWarning($"[BaseUnit] {name} cannot enter orbit: TileOccupancyManager missing for planetIndex={planetIndex}.");
            return;
        }

        // Check if orbit slot is already occupied by another unit
        var existingOccupant = occ.GetOccupantObject(tileIndex, TileLayer.Orbit);
        if (existingOccupant != null && existingOccupant.GetInstanceID() != gameObject.GetInstanceID())
        {
            Debug.LogWarning($"[BaseUnit] {name} cannot enter orbit on tile {tileIndex}: already occupied by {existingOccupant.name}.");
            return;
        }

        if (!occ.TrySetOccupant(tileIndex, gameObject, TileLayer.Orbit))
        {
            Debug.LogWarning($"[BaseUnit] {name} could not claim orbit occupancy on tile {tileIndex}.");
            return;
        }

        // Claim the destination first so orbit entry fails closed if another unit won the race.
        try
        {
            if (currentTileIndex >= 0)
            {
                occ.ClearOccupant(currentTileIndex, currentLayer);
            }
        }
        catch { }

        currentTileIndex = tileIndex;
        currentLayer = TileLayer.Orbit;

        // Position above the tile surface at the configured orbit height.
        Vector3 surface = ts.GetTileSurfacePosition(tileIndex);
        transform.position = surface + Vector3.up * PlanetGenerator.GetOrbitHeight(planetIndex);

        UpdateWalkingState(false);

        // Entering orbit consumes the unit's turn
        var cu2 = this as CombatUnit;
        if (cu2 != null) cu2.ConsumeAction();
    }

    /// <summary>
    /// Land this unit from orbit back to the surface layer.
    /// Consumes movement points equal to CombatUnitData.orbitExitCost.
    /// If requiresSpaceportToLand is true, the target tile must have a spaceport improvement.
    /// </summary>
    public virtual void ExitOrbit(int landingTileIndex = -1)
    {
        if (currentLayer != TileLayer.Orbit)
        {
            Debug.LogWarning($"[BaseUnit] {name} is not in orbit — cannot exit orbit.");
            return;
        }

        // Default: land on the tile we're currently orbiting
        if (landingTileIndex < 0) landingTileIndex = currentTileIndex;

        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null)
        {
            Debug.LogWarning($"[BaseUnit] {name} ExitOrbit: TileSystem missing.");
            return;
        }

        // Spaceport requirement check
        var cu = this as CombatUnit;
        if (cu != null && cu.data != null && cu.data.requiresSpaceportToLand)
        {
            var tile = ts.GetTileData(landingTileIndex);
            bool hasSpaceport = tile != null && tile.improvement != null
                && tile.improvement.improvementName.Contains("Spaceport");
            if (!hasSpaceport)
            {
                Debug.LogWarning($"[BaseUnit] {name} requires a Spaceport to land. Tile {landingTileIndex} has none.");
                return;
            }
        }

        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        if (occ == null)
        {
            Debug.LogWarning($"[BaseUnit] {name} ExitOrbit: TileOccupancyManager missing.");
            return;
        }

        // Check that the surface tile is not already occupied by another unit
        var surfaceOccupant = occ.GetOccupantObject(landingTileIndex, TileLayer.Surface);
        if (surfaceOccupant != null && surfaceOccupant.GetInstanceID() != gameObject.GetInstanceID())
        {
            Debug.LogWarning($"[BaseUnit] {name} cannot land: surface tile {landingTileIndex} is occupied by {surfaceOccupant.name}.");
            return;
        }

        if (!occ.TrySetOccupant(landingTileIndex, gameObject, TileLayer.Surface))
        {
            Debug.LogWarning($"[BaseUnit] {name} could not claim surface occupancy on tile {landingTileIndex} while exiting orbit.");
            return;
        }

        // Claim the surface first so landing cannot clear orbit occupancy and then fail the destination write.
        try
        {
            occ.ClearOccupant(currentTileIndex, TileLayer.Orbit);
        }
        catch { }

        currentTileIndex = landingTileIndex;
        currentLayer = TileLayer.Surface;

        // Position back on terrain surface
        PositionUnitOnSurface(landingTileIndex);
        UpdateWalkingState(false);

        // Exiting orbit consumes the unit's turn
        var cu2 = this as CombatUnit;
        if (cu2 != null) cu2.ConsumeAction();

        Debug.Log($"[BaseUnit] {name} has landed from orbit on tile {landingTileIndex}.");
    }

    /// <summary>
    /// Whether this unit is currently in orbit.
    /// </summary>
    public bool IsInOrbit => currentLayer == TileLayer.Orbit;

    /// <summary>
    /// Request movement to target tile. Delegates entirely to UnitMovementController.
    /// </summary>
    public virtual void MoveTo(int targetTileIndex)
    {
        if (UnitMovementController.Instance == null) return;
        ClearFortify();
        UnitMovementController.Instance.IssueMove(this, targetTileIndex);
    }

    public virtual void Fortify()
    {
        moveOrderPath = null;
        moveOrderNextStep = 0;
        try { UnitMovementController.Instance?.StopMoveForUnit(this); } catch { }
        UpdateWalkingState(false);
        SetFortified(true);
    }

    public virtual void ClearFortify()
    {
        SetFortified(false);
    }

    protected void SetFortified(bool fortified)
    {
        if (isFortified == fortified) return;
        isFortified = fortified;
        if (animator != null && _hasFortifyParam)
            SetAnimatorBoolForFormation(isFortifiedHash, fortified);

        try { GameEventManager.Instance?.RaiseMovePointsChanged(this, currentMovePoints, currentMovePoints); } catch { }
    }

    protected float ApplyFortifyDefenseBonus(float defenseValue)
    {
        return isFortified ? defenseValue * 1.10f : defenseValue;
    }

    /// <summary>
    /// Whether the tile is a valid destination in principle for this unit (passable,
    /// correct terrain type, not impassable cost). Does NOT check movement points or
    /// current occupancy, so it is safe to use for multi-turn move orders where the
    /// unit will not enter the tile this turn.
    /// </summary>
    public bool CanReachTile(int tileIndex)
    {
        var cu = this as CombatUnit;
        if (cu != null && cu.hasActedThisTurn)
        {
            if (Application.isEditor || Debug.isDebugBuild) Debug.LogWarning($"[BaseUnit] CanReachTile false: hasActedThisTurn unit={name} tile={tileIndex}");
            return false;
        }

        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var td = ts != null ? ts.GetTileData(tileIndex) : null;
        if (td == null || !td.isPassable)
        {
            if (Application.isEditor || Debug.isDebugBuild) Debug.LogWarning($"[BaseUnit] CanReachTile false: tileData null or not passable unit={name} tile={tileIndex}");
            return false;
        }

        if (currentLayer == TileLayer.Orbit) return true;

        int moveCost = BiomeHelper.GetMovementCost(td, this);
        if (moveCost >= 99)
        {
            if (Application.isEditor || Debug.isDebugBuild) Debug.LogWarning($"[BaseUnit] CanReachTile false: impassable cost={moveCost} unit={name} tile={tileIndex}");
            return false;
        }

        if (!td.isLand)
        {
            bool isNaval = cu != null && cu.data != null &&
                (cu.data.unitType == CombatCategory.Ship ||
                 cu.data.unitType == CombatCategory.Boat ||
                 cu.data.unitType == CombatCategory.Submarine ||
                 cu.data.unitType == CombatCategory.SeaCrawler);
            if (!isNaval)
            {
                if (Application.isEditor || Debug.isDebugBuild) Debug.LogWarning($"[BaseUnit] CanReachTile false: requires naval unit unit={name} tile={tileIndex}");
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Single source of truth for whether this unit can move to a tile.
    /// Handles all unit types: orbit, naval, land, worker, move-point checks.
    /// Uses the same movement-cost threshold (>= 99) as FindPath so the two
    /// never disagree on passability.
    /// </summary>
    public bool CanMoveTo(int tileIndex)
    {
        // CombatUnit: turn-consuming actions (orbit entry/exit) block further movement
        var cu = this as CombatUnit;
        if (cu != null && cu.hasActedThisTurn)
        {
            if (Application.isEditor || Debug.isDebugBuild) Debug.LogWarning($"[BaseUnit] CanMoveTo false: hasActedThisTurn unit={name} tile={tileIndex}");
            return false;
        }

        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var td = ts != null ? ts.GetTileData(tileIndex) : null;
        if (td == null || !td.isPassable)
        {
            if (Application.isEditor || Debug.isDebugBuild) Debug.LogWarning($"[BaseUnit] CanMoveTo false: tileData null or not passable unit={name} tile={tileIndex}");
            return false;
        }

        // Orbit units: skip terrain rules, only check orbit-layer occupancy
        if (currentLayer == TileLayer.Orbit)
        {
            try
            {
                var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
                var occObj = occ != null ? occ.GetOccupantObjectWithFallback(tileIndex, TileLayer.Orbit) : null;
                if (occObj != null && occObj.GetInstanceID() != gameObject.GetInstanceID()) return false;
            }
            catch { }
            return true;
        }

        // Movement cost — single source of truth shared with FindPath
        int moveCost = BiomeHelper.GetMovementCost(td, this);
        if (moveCost >= 99)
        {
            if (Application.isEditor || Debug.isDebugBuild) Debug.LogWarning($"[BaseUnit] CanMoveTo false: impassable cost={moveCost} unit={name} tile={tileIndex}");
            return false;
        }

        // Land / water rules
        if (!td.isLand)
        {
            // Only specific naval CombatUnit types may enter water
            bool isNaval = cu != null && cu.data != null &&
                (cu.data.unitType == CombatCategory.Ship ||
                 cu.data.unitType == CombatCategory.Boat ||
                 cu.data.unitType == CombatCategory.Submarine ||
                 cu.data.unitType == CombatCategory.SeaCrawler);
            if (!isNaval)
            {
                if (Application.isEditor || Debug.isDebugBuild) Debug.LogWarning($"[BaseUnit] CanMoveTo false: requires naval unit unit={name} tile={tileIndex}");
                return false;
            }
        }

        // Move-point check for units with turn-based movement
        if (GetStartingMovePoints() > 0 && currentMovePoints < moveCost)
        {
            if (Application.isEditor || Debug.isDebugBuild) Debug.LogWarning($"[BaseUnit] CanMoveTo false: insufficient MP current={currentMovePoints} required={moveCost} unit={name} tile={tileIndex}");
            return false;
        }

        // Layer-aware occupancy check
        try
        {
            var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
            var occObj = occ != null ? occ.GetOccupantObjectWithFallback(tileIndex, currentLayer) : null;
            if (occObj != null && occObj.GetInstanceID() != gameObject.GetInstanceID())
            {
                if (occObj.GetComponent<BaseUnit>() != null)
                {
                    if (Application.isEditor || Debug.isDebugBuild) Debug.LogWarning($"[BaseUnit] CanMoveTo false: tile occupied by unit={occObj.name} unit={name} tile={tileIndex}");
                    return false;
                }
                if (occObj.GetComponent<City>() != null)
                {
                    if (Application.isEditor || Debug.isDebugBuild) Debug.LogWarning($"[BaseUnit] CanMoveTo false: tile occupied by city={occObj.name} unit={name} tile={tileIndex}");
                    return false;
                }
            }
        }
        catch { }

        return true;
    }

    /// <summary>
    /// Update the walking animation state
    /// </summary>
    public virtual void UpdateWalkingState(bool walking)
    {
        if (animator == null)
        {
            Debug.LogWarning($"[BaseUnit] {gameObject.name} UpdateWalkingState({walking}) — animator is NULL, skipping");
            return;
        }
        bool wasMoving = isMoving;
        if (_hasWalkParam)
            SetAnimatorBoolForFormation(isWalkingHash, walking);
        else if (soldierGroup != null && animator != null)
            soldierGroup.SyncBoolParametersFrom(animator);
        isMoving = walking;
        _walkingStuckFrames = 0; // reset failsafe counter on any explicit state change
        if (!walking)
        {
            // Ensure any controller-side movement coroutine is stopped so the unit truly becomes idle.
            try { UnitMovementController.Instance?.StopMoveForUnit(this); } catch { }
        }
        
    }

    // Failsafe: detect stuck walking animation
    private int _walkingStuckFrames = 0;
    private Vector3 _lastFailsafePos;

    protected virtual void LateUpdate()
    {
        // If the unit says it's moving but hasn't actually changed position for 30+ frames,
        // force-clear the walking state. This catches coroutine interruption edge cases.
        if (isMoving)
        {
            bool hasActiveMove = false;
            try { hasActiveMove = UnitMovementController.Instance != null && UnitMovementController.Instance.HasActiveMove(this); } catch { }

            if (hasActiveMove)
            {
                _walkingStuckFrames = 0;
            }
            else if (Vector3.SqrMagnitude(transform.position - _lastFailsafePos) < 0.0001f)
            {
                _walkingStuckFrames++;
                if (_walkingStuckFrames > 30) // ~0.5s at 60fps
                {
                    Debug.LogWarning($"[BaseUnit] {gameObject.name} FAILSAFE: Walking stuck for {_walkingStuckFrames} frames at {transform.position}. Forcing idle.");
                    UpdateWalkingState(false);
                }
            }
            else
            {
                _walkingStuckFrames = 0;
            }
        }
        _lastFailsafePos = transform.position;
    }

    /// <summary>
    /// Position unit on surface (flat-only). Places unit at terrain surface with proper height.
    /// </summary>
    protected virtual void PositionUnitOnSurface(int tileIndex)
    {
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null) return;
        Vector3 flatCenter = ts.GetTileSurfacePosition(tileIndex);
        transform.position = flatCenter;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        currentTileIndex = tileIndex;

        // Register with tile occupancy so tile-based selection (WorldPicker → GetUnitOnTile) works
        RegisterOccupancy(tileIndex);
    }

    /// <summary>
    /// Register this unit with the TileOccupancyManager for the given tile.
    /// Called after currentTileIndex is set so tile-click selection can find us.
    /// </summary>
    protected void RegisterOccupancy(int tileIndex)
    {
        if (tileIndex < 0) return;
        try
        {
            var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
            if (occ != null)
            {
                // Clear old tile occupancy if moving to a different tile
                if (currentTileIndex >= 0 && currentTileIndex != tileIndex)
                {
                    occ.ClearOccupant(currentTileIndex, currentLayer);
                }
                if (!occ.TrySetOccupant(tileIndex, gameObject, currentLayer))
                {
                    Debug.LogWarning($"[BaseUnit] RegisterOccupancy could not claim tile {tileIndex} for {name} on layer {currentLayer}.");
                }
            }
        }
        catch (System.Exception ex) { Debug.LogWarning($"[BaseUnit] RegisterOccupancy failed for {name}: {ex.Message}"); }
    }

    #endregion

    #region UI Label

    /// <summary>
    /// Initialize the unit label UI
    /// </summary>
    protected virtual void InitializeUnitLabel()
    {
        if (unitLabelPrefab != null && unitLabelInstance == null)
        {
            Transform anchor = labelAnchor != null ? labelAnchor : transform;
            var labelGO = Instantiate(unitLabelPrefab, anchor);
            unitLabelInstance = labelGO.GetComponent<UnitLabel>();
            if (unitLabelInstance != null)
            {
                string ownerName = owner != null && owner.civData != null 
                    ? owner.civData.civName : "Unknown";
                unitLabelInstance.Initialize(anchor, UnitName, ownerName, currentHealth, MaxHealth);

                // Disable raycast on label text
                var textComponents = unitLabelInstance.GetComponentsInChildren<TextMeshProUGUI>();
                foreach (var txt in textComponents)
                {
                    if (txt != null) txt.raycastTarget = false;
                }
            }
        }
    }

    /// <summary>
    /// Update the unit label with current health
    /// </summary>
    protected virtual void UpdateUnitLabel()
    {
        if (unitLabelInstance != null)
        {
            string ownerName = owner != null && owner.civData != null 
                ? owner.civData.civName : "Unknown";
            unitLabelInstance.UpdateLabel(UnitName, ownerName, currentHealth, MaxHealth);
        }
    }

    protected void ShowHealthChangePopup(int amount)
    {
        if (amount == 0 || !gameObject.activeInHierarchy) return;

        Transform anchor = labelAnchor != null ? labelAnchor : transform;
        GameObject popupGO = new GameObject("HealthChangePopup");
        popupGO.transform.SetParent(anchor, false);
        popupGO.transform.localPosition = new Vector3(0f, healthPopupVerticalOffset, 0f);
        popupGO.transform.localRotation = Quaternion.identity;
        popupGO.transform.localScale = Vector3.one * healthPopupScale;

        var popupText = popupGO.AddComponent<TextMeshPro>();
        if (TMP_Settings.defaultFontAsset != null)
            popupText.font = TMP_Settings.defaultFontAsset;
        popupText.text = amount > 0 ? $"+{amount}" : amount.ToString();
        // Font size configurable via Inspector
        popupText.fontSize = healthPopupFontSize;
        popupText.alignment = TextAlignmentOptions.Center;
        popupText.textWrappingMode = TextWrappingModes.NoWrap;
        popupText.color = amount > 0 ? HealPopupColor : DamagePopupColor;
        popupText.outlineWidth = 0.18f;
        popupText.outlineColor = HealthPopupOutlineColor;
        popupText.raycastTarget = false;

        var popupRenderer = popupGO.GetComponent<MeshRenderer>();
        if (popupRenderer != null)
        {
            popupRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            popupRenderer.receiveShadows = false;
            popupRenderer.sortingOrder = 100;
        }

        StartCoroutine(AnimateHealthChangePopup(popupGO.transform, popupText));
    }

    private System.Collections.IEnumerator AnimateHealthChangePopup(Transform popupTransform, TextMeshPro popupText)
    {
        if (popupTransform == null || popupText == null)
            yield break;

        Camera worldCamera = Camera.main;
        Vector3 startLocalPosition = popupTransform.localPosition;
        Vector3 endLocalPosition = startLocalPosition + Vector3.up * healthPopupRiseDistance;
        Color baseColor = popupText.color;
        float elapsed = 0f;

        while (elapsed < healthPopupDuration)
        {
            if (popupTransform == null || popupText == null)
                yield break;

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / healthPopupDuration);
            popupTransform.localPosition = Vector3.Lerp(startLocalPosition, endLocalPosition, t);

            if (worldCamera == null)
                worldCamera = Camera.main;
            if (worldCamera != null)
                popupTransform.rotation = worldCamera.transform.rotation;

            Color fadedColor = baseColor;
            fadedColor.a = 1f - t;
            popupText.color = fadedColor;
            yield return null;
        }

        if (popupTransform != null)
            Destroy(popupTransform.gameObject);
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// Check if animator has a specific parameter
    /// </summary>
    protected bool HasParameter(Animator anim, int paramHash)
    {
        if (anim == null) return false;
        foreach (var param in anim.parameters)
        {
            if (param.nameHash == paramHash)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Find a child transform recursively by name
    /// </summary>
    protected static Transform FindChildRecursive(Transform root, string name)
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
    /// Count adjacent allied units
    /// </summary>
    protected int CountAdjacentAllies(int tileIndex)
    {
        int count = 0;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var neighbours = ts != null ? ts.GetNeighbors(tileIndex) : null;
        if (neighbours == null) return 0;

        foreach (int idx in neighbours)
        {
            var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
            GameObject obj = occ != null ? occ.GetOccupantObjectWithFallback(idx, currentLayer) : null;
            if (obj == null) continue;

            if (obj == null) continue;
            var unit = obj.GetComponent<BaseUnit>();
            if (unit != null && unit.owner == this.owner)
                count++;
        }
        return count;
    }

    #endregion

    #region Abstract Methods (must be implemented by subclasses)

    /// <summary>
    /// Reset unit for new turn (restore points, check hazards, etc.)
    /// </summary>
    public abstract void ResetForNewTurn();

    // -------------------------
    // Movement point APIs (unified for all unit types)
    // -------------------------
    /// <summary>
    /// Current move points remaining this turn. Managed by BaseUnit so all units share the same API.
    /// </summary>
    public int currentMovePoints { get; protected set; }

    /// <summary>
    /// Returns the starting movement points this unit would have at the beginning of a turn.
    /// Default implementation delegates to known unit types (WorkerUnit, CombatUnit animals).
    /// </summary>
    public virtual int GetStartingMovePoints()
    {
        // WorkerUnit has its own detailed calculation; delegate to it when available.
        var w = this as WorkerUnit;
        if (w != null) return w.GetStartingMovePoints();

        // CombatUnit may have animalMovePoints for animal-type units.
        var c = this as CombatUnit;
        if (c != null && c.data != null)
        {
            // Prefer a dedicated baseMovePoints if present (backwards-compatible), else fallback to animalMovePoints
            var fld = typeof(CombatUnitData).GetField("baseMovePoints");
            if (fld != null)
            {
                try { return (int)fld.GetValue(c.data); } catch { }
            }
            return c.data.animalMovePoints;
        }

        // Default: 0 (no turn-based movement by default unless data provides it)
        return 0;
    }

    /// <summary>
    /// Deduct move points (safe to call for any unit). Clamps at zero.
    /// </summary>
    public virtual void DeductMovePoints(int amount)
    {
        int oldPts = currentMovePoints;
        currentMovePoints = Mathf.Max(0, currentMovePoints - amount);
        try { GameEventManager.Instance?.RaiseMovePointsChanged(this, oldPts, currentMovePoints); } catch { }
    }

    /// <summary>
    /// Restore movement points for a new turn using `GetStartingMovePoints` and applying global penalties (e.g., winter).
    /// Call this from subclass `ResetForNewTurn` implementations (recommended at start).
    /// </summary>
    public virtual void RestoreMovePointsForNewTurn()
    {
        int start = GetStartingMovePoints();
        if (IsTrapped)
        {
            currentMovePoints = 0;
            return;
        }

        int move = start;
        if (hasWinterPenalty && ClimateManager.Instance != null && ClimateManager.Instance.currentSeason == Season.Winter)
        {
            move = Mathf.Max(1, move - 1);
        }
        int old = currentMovePoints;
        currentMovePoints = move;
        try { GameEventManager.Instance?.RaiseMovePointsChanged(this, old, currentMovePoints); } catch { }
    }

    #endregion
}
