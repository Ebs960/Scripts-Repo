using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;
using GameCombat;

/// <summary>
/// Base abstract class for all units (CombatUnit, WorkerUnit).
/// Contains shared functionality: equipment, movement, health, animations, projectiles.
/// 
/// Architecture:
/// - BaseUnit handles common systems (equipment, movement, damage, animations)
/// - CombatUnit adds: fatigue, formations, battle system
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
    [Header("Projectile Holders")]
    [Tooltip("Off-hand holder for temporary arrow/ammo visuals before they are nocked/loaded.")]
    [SerializeField] protected Transform projectileHandHolder;
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
    [SerializeField] protected EquipmentData _equippedHead;
    [SerializeField] protected EquipmentData _equippedTool;

    [Header("Editor Equipment")]
    [Tooltip("If true, changing equipment in the Inspector will update visuals immediately in Edit mode.")]
    [SerializeField] protected bool updateEquipmentInEditor = true;

    [Header("Active Projectile")]
    [Tooltip("The projectile type this unit will use when firing ranged weapons")]
    [SerializeField] protected ProjectileData _activeProjectile;

    protected GameObject currentProjectileWeaponVisual;
    protected GameObject currentLoadedProjectileVisual;
    protected EquipmentData currentLoadedProjectileWeapon;
    protected ProjectileData currentLoadedProjectileData;

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
        RefreshVisionAfterEquipmentChanged();
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

        // Fatigue from attacking
        if (ctx.attacker != null)
            ctx.attacker.AddFatigue(8f);

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

    protected const float MosquitoDamagePercent = 0.33f;

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
        var cu = this as CombatUnit;
        if (cu != null)
        {
            cu.RecordTurnAction();
            if (currentAttackPoints <= 0) cu.ConsumeAction();
        }
        try { GameEventManager.Instance?.RaiseAttackPointsChanged(this, oldAttackPoints, currentAttackPoints, MaxAttackPoints); } catch { }
        return true;
    }

    public virtual void ResetAttackPointsForNewTurn()
    {
        int oldAttackPoints = currentAttackPoints;
        currentAttackPoints = ApplyResourceUpkeepToTurnPoints(attackPointsPerTurn);
        hasUsedSpaceReactionThisTurn = false;
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

    [Header("Space Hex Location")]
    public SpaceLocation spaceLocation = SpaceLocation.OnSurface(-1, -1);
    public int spaceMovementPointsPerTurn = 3;
    public int currentSpaceMovementPoints = 3;
    public int currentSpaceTileIndex = -1;
    public List<int> queuedSpacePath = new List<int>();
    public int queuedSpacePathCursor = 0;
    public int spaceVisionRange = 2;
    public int spaceReconRange = 5;
    public bool canPerformSpaceRecon = false;
    public int reconActionCost = 1;
    public int spaceFleetId = -1;
    public bool isPackedInSpaceFleet = false;
    public bool hasUsedSpaceReactionThisTurn = false;

    [Tooltip("Which planet this unit belongs to (multi-planet gameplay).")]
    public int planetIndex = -1;

    /// <summary>
    /// Which slot this unit occupies when stacked on a tile (0=front, 1=middle, 2=rear).
    /// -1 means not assigned to a stack slot (legacy single-unit behavior).
    /// </summary>
    [System.NonSerialized] public int stackSlot = -1;

    /// <summary>
    /// World-space offset distance per stack slot (units behind the front get offset backward).
    /// </summary>
    private const float STACK_OFFSET_DISTANCE = 1.2f;

    /// <summary>
    /// Apply a positional offset based on this unit's stack slot so stacked units
    /// appear in distinct rows. Slot 0 = tile center, slot 1 = offset back, slot 2 = further back.
    /// The "back" direction is away from the unit's forward facing (or a default direction if none).
    /// </summary>
    public void ApplyStackOffset()
    {
        if (stackSlot <= 0) return; // slot 0 or unassigned = no offset needed

        // Offset along the unit's local backward direction (so rear ranks are behind front)
        Vector3 backDir = -transform.forward;
        // Fallback if forward is zero
        if (backDir.sqrMagnitude < 0.01f) backDir = Vector3.back;

        transform.position += backDir * (stackSlot * STACK_OFFSET_DISTANCE);
    }

    /// <summary>
    /// Snap this unit's world position to its tile center, then re-apply the current stack slot
    /// offset. Call this after stackSlot is changed externally (e.g., stack reorder via UI).
    /// </summary>
    public void SnapToSlotPosition()
    {
        if (currentTileIndex < 0) return;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null) return;
        transform.position = ts.GetTileSurfacePosition(currentTileIndex);
        ApplyStackOffset();
    }

    /// <summary>
    /// Register this unit on a tile with stack-aware occupancy.
    /// Finds an available slot, assigns stackSlot, and applies visual offset.
    /// Returns true if successful.
    /// </summary>
    public bool RegisterOnTileStack(int tileIndex, TileLayer layer)
    {
        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        if (occ == null) return false;

        int maxStack = owner != null ? owner.GetMaxStackSize() : 1;
        int slot = occ.TryAddToStack(tileIndex, layer, gameObject, maxStack);
        if (slot < 0) return false;

        stackSlot = slot;
        return true;
    }

    /// <summary>
    /// Unregister this unit from its current tile stack.
    /// </summary>
    public void UnregisterFromTileStack()
    {
        if (currentTileIndex < 0) return;
        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        if (occ != null)
            occ.ClearOccupantById(currentTileIndex, currentLayer, gameObject.GetRuntimeId());
        stackSlot = -1;
    }

    /// <summary>
    /// Get all other units stacked on the same tile as this unit.
    /// </summary>
    public List<BaseUnit> GetStackedUnits()
    {
        var result = new List<BaseUnit>();
        if (currentTileIndex < 0) return result;
        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        if (occ == null) return result;

        var objects = occ.GetAllOccupantObjects(currentTileIndex, currentLayer);
        int selfId = gameObject.GetRuntimeId();
        foreach (var obj in objects)
        {
            if (obj.GetRuntimeId() == selfId) continue;
            var unit = obj.GetComponent<BaseUnit>();
            if (unit != null) result.Add(unit);
        }
        return result;
    }

    /// <summary>
    /// Returns the front-most (slot 0) unit on this tile, or this unit if alone/front.
    /// Used for damage routing: melee damage hits the front unit first.
    /// </summary>
    public BaseUnit GetFrontUnit()
    {
        if (currentTileIndex < 0) return this;
        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        if (occ == null) return this;

        int frontId = occ.GetOccupantIdAtSlot(currentTileIndex, currentLayer, 0);
        if (frontId == 0 || frontId == gameObject.GetRuntimeId()) return this;

        var frontObj = UnitRegistry.GetObject(frontId);
        if (frontObj == null) return this;
        var frontUnit = frontObj.GetComponent<BaseUnit>();
        return frontUnit != null ? frontUnit : this;
    }

    /// <summary>
    /// Unstack this unit from the current tile, placing it on an adjacent empty tile.
    /// Costs the unit's full turn (all move points and attack points consumed).
    /// Returns true if the unstack succeeded.
    /// </summary>
    public bool Unstack()
    {
        if (stackSlot <= 0) return false; // Already front or not in a stack
        if (currentTileIndex < 0) return false;

        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null) return false;

        // Find an adjacent empty tile to move to
        int destTile = -1;
        foreach (int neighbor in ts.GetNeighbors(currentTileIndex))
        {
            if (CanMoveTo(neighbor))
            {
                var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
                if (occ != null && occ.GetOccupantCount(neighbor, currentLayer) == 0)
                {
                    destTile = neighbor;
                    break;
                }
            }
        }

        if (destTile < 0)
        {
            Debug.LogWarning($"[BaseUnit] {name} cannot unstack: no adjacent empty tile.");
            return false;
        }

        // Unregister from current stack
        UnregisterFromTileStack();

        // Register on the new tile
        var occNew = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        if (occNew != null)
        {
            int slot = occNew.TryAddToStack(destTile, currentLayer, gameObject, 1);
            stackSlot = slot >= 0 ? slot : 0;
        }

        currentTileIndex = destTile;
        PositionUnitOnSurface(destTile);

        // Consume full turn
        currentMovePoints = 0;
        currentAttackPoints = 0;

        Debug.Log($"[BaseUnit] {name} unstacked to tile {destTile}");
        return true;
    }
    public float moveSpeed = 2f;
    public bool isMoving { get; set; }
    public bool IsFortified => isFortified;

    // Projectile queueing
    [Header("Projectiles")]
    [Tooltip("If true, projectiles fire via animation event; if false, fire immediately")]
    public bool useAnimationEventForProjectiles = true;
    protected EquipmentData queuedProjectileEquipment;
    protected BaseUnit queuedProjectileTargetUnit;
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

    #region Status Effects & Fatigue

    [System.NonSerialized]
    protected List<StatusEffect> activeStatusEffects = new List<StatusEffect>();

    /// <summary>Current fatigue (0–100). Rises from actions, reduces attack and defense.</summary>
    [System.NonSerialized]
    public float currentFatigue = 0f;
    public const float MaxFatigue = 100f;

    public IReadOnlyList<StatusEffect> ActiveStatusEffects => activeStatusEffects;

    // ── Status Effect API ──

    public void ApplyStatusEffect(StatusEffectData data, BaseUnit source = null)
    {
        if (data == null) return;

        var existing = activeStatusEffects.Find(e => e.data == data);
        if (existing != null)
        {
            switch (data.stacking)
            {
                case StatusEffectStacking.Replace:
                    existing.Cleanup();
                    activeStatusEffects.Remove(existing);
                    break;
                case StatusEffectStacking.Refresh:
                    existing.remainingTurns = data.baseDuration;
                    return;
                case StatusEffectStacking.Stack:
                    existing.magnitude += data.magnitude;
                    existing.remainingTurns = Mathf.Max(existing.remainingTurns, data.baseDuration);
                    return;
                case StatusEffectStacking.Ignore:
                    return;
            }
        }

        var effect = new StatusEffect(data, source);
        activeStatusEffects.Add(effect);

        // Spawn VFX
        if (data.applyVFX != null)
            Instantiate(data.applyVFX, transform.position, Quaternion.identity);
        if (data.persistentVFX != null)
            effect.persistentVFXInstance = Instantiate(data.persistentVFX, transform);
        if (data.applySound != null)
            AudioSource.PlayClipAtPoint(data.applySound, transform.position);
    }

    public void RemoveStatusEffect(StatusEffectData data)
    {
        for (int i = activeStatusEffects.Count - 1; i >= 0; i--)
        {
            if (activeStatusEffects[i].data == data)
            {
                activeStatusEffects[i].Cleanup();
                activeStatusEffects.RemoveAt(i);
            }
        }
    }

    public bool HasStatusEffect(StatusEffectType type)
    {
        foreach (var e in activeStatusEffects)
            if (e.data.effectType == type) return true;
        return false;
    }

    /// <summary>
    /// Tick all status effects. Called at start of turn. Returns net HP change (positive = damage, negative = healing).
    /// </summary>
    public int TickStatusEffects()
    {
        int netHpChange = 0;
        for (int i = activeStatusEffects.Count - 1; i >= 0; i--)
        {
            int tick = activeStatusEffects[i].Tick();
            netHpChange += tick;

            if (activeStatusEffects[i].IsExpired)
            {
                activeStatusEffects[i].Cleanup();
                activeStatusEffects.RemoveAt(i);
            }
        }
        return netHpChange;
    }

    /// <summary>Sum of all status effect attack modifiers.</summary>
    public float GetStatusEffectAttackModifier()
    {
        float total = 0f;
        foreach (var e in activeStatusEffects)
            total += e.GetAttackModifier();
        return total;
    }

    /// <summary>Sum of all status effect defense modifiers.</summary>
    public float GetStatusEffectDefenseModifier()
    {
        float total = 0f;
        foreach (var e in activeStatusEffects)
            total += e.GetDefenseModifier();
        return total;
    }

    /// <summary>Sum of all status effect range modifiers.</summary>
    public float GetStatusEffectRangeModifier()
    {
        float total = 0f;
        foreach (var e in activeStatusEffects)
            total += e.GetRangeModifier();
        return total;
    }

    /// <summary>Sum of all status effect movement modifiers.</summary>
    public int GetStatusEffectMovementModifier()
    {
        int total = 0;
        foreach (var e in activeStatusEffects)
            total += e.GetMovementModifier();
        return total;
    }

    /// <summary>
    /// Fatigue multiplier for combat stats. 1.0 at 0 fatigue, drops to 0.7 at max fatigue.
    /// </summary>
    public float FatigueMultiplier => Mathf.Lerp(1f, 0.7f, currentFatigue / MaxFatigue);

    /// <summary>Add fatigue from an action (attack, long move, etc.).</summary>
    public void AddFatigue(float amount)
    {
        currentFatigue = Mathf.Clamp(currentFatigue + amount, 0f, MaxFatigue);
    }

    /// <summary>Recover fatigue at start of turn. Fortified units recover faster.</summary>
    public void RecoverFatigueForNewTurn()
    {
        float recovery = IsFortified ? 20f : 10f;
        currentFatigue = Mathf.Max(0f, currentFatigue - recovery);
    }

    /// <summary>
    /// Process fatigue/status effects at start of turn.
    /// Call from subclass ResetForNewTurn after base resets.
    /// </summary>
    public void ProcessWarfareSystems()
    {
        RecoverFatigueForNewTurn();

        // Tick status effects (DoT, HoT, expiry)
        // Tick() contract: positive = damage (Poison/Burn), negative = healing (Regeneration)
        int hpChange = TickStatusEffects();
        if (hpChange > 0)
        {
            // Status effect damage (poison, burn)
            ApplyDamage(hpChange, null, false);
        }
        else if (hpChange < 0)
        {
            // Status effect healing (regeneration)
            Heal(-hpChange);
        }

        ApplyPassiveAuraHealingForNewTurn();
    }

    private void ApplyPassiveAuraHealingForNewTurn()
    {
        if (currentHealth <= 0 || currentHealth >= MaxHealth) return;

        float healingPct = AggregateIncomingAuraBonuses().healingRatePct;
        if (healingPct <= 0f) return;

        int healAmount = Mathf.RoundToInt(MaxHealth * healingPct);
        if (healAmount > 0)
            Heal(healAmount);
    }

    /// <summary>Clean up all status effects (called on death).</summary>
    protected void CleanupAllStatusEffects()
    {
        foreach (var e in activeStatusEffects)
            e.Cleanup();
        activeStatusEffects.Clear();
    }

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

        soldierGroup.Initialize(count, variants, formationType, formationSpacing, gameObject.GetRuntimeId());
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

    private void RefreshVisionAfterEquipmentChanged()
    {
        if (UnitVisionManager.Instance == null || owner == null)
            return;

        UnitVisionManager.Instance.UpdateVisionForCiv(UnitVisionManager.GetCivIndex(owner));
    }

    #endregion

    #region Equipment Properties

    public ProjectileData ActiveProjectile
    {
        get => _activeProjectile;
        set => TrySetActiveProjectile(value);
    }

    public EquipmentData CurrentProjectileWeapon => _equippedProjectileWeapon;

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
            if (value != null && (!IsEquipmentCompatible(value) || HasTwoHandedWeaponEquipped())) return;
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

    public EquipmentData equippedHead { get => _equippedHead; set { if (value != null && !IsEquipmentCompatible(value)) return; _equippedHead = value; if (Application.isPlaying || updateEquipmentInEditor) UpdateEquipmentVisuals(); } }
    public EquipmentData equippedTool { get => _equippedTool; set { if (value != null && !IsEquipmentCompatible(value)) return; _equippedTool = value; if (Application.isPlaying || updateEquipmentInEditor) UpdateEquipmentVisuals(); } }

    protected bool HasTwoHandedWeaponEquipped() =>
        (_equippedWeapon?.isTwoHanded ?? false) || (_equippedProjectileWeapon?.isTwoHanded ?? false);

    /// <summary>Check if equipment is compatible with this unit type</summary>
    protected bool IsEquipmentCompatible(EquipmentData equipment)
    {
        if (equipment == null) return true;
        return equipment.targetUnit == EquipmentTarget.Both || 
               equipment.targetUnit == AcceptedEquipmentTarget;
    }

    #endregion

    #region Equipment Stat Bonuses

    private float SumEquipment(System.Func<EquipmentData, float> selector) => EnumerateEquippedItems().Sum(selector);

    public float EquipmentAttackBonus => SumEquipment(item => item.attackBonus);

    public float EquipmentMeleeAttackBonus =>
        SumEquipment(item => item.meleeAttackBonus);

    public float EquipmentRangedAttackBonus =>
        SumEquipment(item => item.rangedAttackBonus);

    public float EquipmentCityAttackBonus =>
        SumEquipment(item => item.cityAttackBonus);

    public float EquipmentGroundAttackBonus =>
        SumEquipment(item => item.groundAttackBonus);

    public float EquipmentUnderwaterAttackBonus =>
        SumEquipment(item => item.underwaterAttackBonus);

    public float EquipmentAirAttackBonus =>
        SumEquipment(item => item.airAttackBonus);

    public float EquipmentSpaceAttackBonus =>
        SumEquipment(item => item.spaceAttackBonus);

    public float EquipmentDefenseBonus =>
        SumEquipment(item => item.defenseBonus);

    public float EquipmentHealthBonus =>
        SumEquipment(item => item.healthBonus);

    public float EquipmentMoveBonus =>
        SumEquipment(item => item.movementBonus);

    public float EquipmentRangeBonus =>
        SumEquipment(item => item.rangeBonus);

    #endregion

    #region Ability Modifiers

    protected struct TargetedCombatAgg
    {
        public float attackAdd;
        public float defenseAdd;
        public float attackPct;
        public float defensePct;
    }

    protected IEnumerable<EquipmentData> EnumerateEquippedItems()
    {
        if (_equippedWeapon != null)
            yield return _equippedWeapon;
        if (_equippedProjectileWeapon != null)
            yield return _equippedProjectileWeapon;
        if (_equippedShield != null)
            yield return _equippedShield;
        if (_equippedArmor != null)
            yield return _equippedArmor;
        if (_equippedMiscellaneous != null)
            yield return _equippedMiscellaneous;
        if (_equippedHead != null)
            yield return _equippedHead;
        if (_equippedTool != null)
            yield return _equippedTool;
    }

    public IEnumerable<EquipmentData> EnumerateEquippedItemsForVision()
    {
        return EnumerateEquippedItems();
    }

    protected static bool AbilityHasCombatTargetFilter(Ability ability)
    {
        return ability != null
            && Civilization.HasCombatBonusOpponentFilter(ability.targetUnit, ability.targetWorker, ability.useTargetUnitCategoryFilter);
    }

    protected TargetedCombatAgg AggregateEquippedItemTargetedModifiers(BaseUnit opponent)
    {
        TargetedCombatAgg total = new TargetedCombatAgg();
        if (opponent == null)
            return total;

        foreach (var item in EnumerateEquippedItems())
        {
            if (item == null)
                continue;

            if (opponent is CombatUnit combatOpponent && combatOpponent.data != null)
            {
                if (item.attackBonusAgainst != null)
                {
                    foreach (var entry in item.attackBonusAgainst)
                    {
                        if (entry.unitType == combatOpponent.data.unitType)
                            total.attackAdd += entry.value;
                    }
                }

                if (item.defenseBonusAgainst != null)
                {
                    foreach (var entry in item.defenseBonusAgainst)
                    {
                        if (entry.unitType == combatOpponent.data.unitType)
                            total.defenseAdd += entry.value;
                    }
                }
            }

            if (item.combatModifiersAgainst == null)
                continue;

            foreach (var modifier in item.combatModifiersAgainst)
            {
                if (!Civilization.MatchesCombatBonusOpponent(opponent, modifier.targetUnit, modifier.targetWorker, modifier.useTargetUnitCategoryFilter, modifier.targetUnitCategory))
                    continue;

                total.attackAdd += modifier.attackAdd;
                total.defenseAdd += modifier.defenseAdd;
                total.attackPct += modifier.attackPct;
                total.defensePct += modifier.defensePct;
            }
        }

        return total;
    }

    protected struct UnitAuraAgg
    {
        public float attackAdd, meleeAttackAdd, rangedAttackAdd, cityAttackAdd, groundAttackAdd, underwaterAttackAdd, airAttackAdd, spaceAttackAdd;
        public float defenseAdd, healthAdd, rangeAdd;
        public float attackPct, meleeAttackPct, rangedAttackPct, cityAttackPct, groundAttackPct, underwaterAttackPct, airAttackPct, spaceAttackPct;
        public float defensePct, healthPct, rangePct;
        public float healingRatePct;
    }

    protected static bool MatchesRequirement(BoolRequirement requirement, bool value)
    {
        return requirement == BoolRequirement.Any
            || (requirement == BoolRequirement.MustBeTrue && value)
            || (requirement == BoolRequirement.MustBeFalse && !value);
    }

    protected HexTileData GetCurrentTileData()
    {
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        return ts != null && currentTileIndex >= 0 ? ts.GetTileData(currentTileIndex) : null;
    }

    protected bool MatchesLayerRequirement(UnitLayerRequirement requirement, HexTileData tile)
    {
        bool isOrbit = tile != null && tile.isSpace;
        bool isUnderwater = tile != null && tile.IsUnderwaterTile;
        switch (requirement)
        {
            case UnitLayerRequirement.Surface:
                return !isOrbit && !isUnderwater;
            case UnitLayerRequirement.Underwater:
                return isUnderwater;
            case UnitLayerRequirement.Orbit:
                return isOrbit;
            default:
                return true;
        }
    }

    protected bool MatchesAbilityLocation(Ability ability)
    {
        if (ability == null) return false;
        var tile = GetCurrentTileData();
        bool isCityTile = tile?.controllingCity != null;
        bool isUnderwater = tile != null && tile.IsUnderwaterTile;
        bool isOrbit = tile != null && tile.isSpace;

        if (!MatchesRequirement(ability.cityRequirement, isCityTile)) return false;
        if (ability.useBiomeFilter && (tile == null || tile.biome != ability.biome)) return false;
        if (!MatchesRequirement(ability.hillRequirement, tile != null && tile.isHill)) return false;
        if (!MatchesRequirement(ability.mountainRequirement, tile != null && tile.isMountain)) return false;
        if (!MatchesLayerRequirement(ability.layerRequirement, tile)) return false;
        if (!MatchesRequirement(ability.underwaterRequirement, isUnderwater)) return false;
        if (!MatchesRequirement(ability.orbitRequirement, isOrbit)) return false;
        if (ability.useResourceFilter && (tile == null || tile.resource != ability.resource)) return false;
        return true;
    }

    protected bool MatchesEquipmentBonusLocation(EquipmentStatBonus bonus)
    {
        if (bonus == null) return false;
        var tile = GetCurrentTileData();
        bool isCityTile = tile?.controllingCity != null;
        bool isUnderwater = tile != null && tile.IsUnderwaterTile;
        bool isOrbit = tile != null && tile.isSpace;

        if (!MatchesRequirement(bonus.cityRequirement, isCityTile)) return false;
        if (bonus.useBiomeFilter && (tile == null || tile.biome != bonus.biome)) return false;
        if (!MatchesRequirement(bonus.hillRequirement, tile != null && tile.isHill)) return false;
        if (!MatchesRequirement(bonus.mountainRequirement, tile != null && tile.isMountain)) return false;
        if (!MatchesLayerRequirement(bonus.layerRequirement, tile)) return false;
        if (!MatchesRequirement(bonus.underwaterRequirement, isUnderwater)) return false;
        if (!MatchesRequirement(bonus.orbitRequirement, isOrbit)) return false;
        if (bonus.useResourceFilter && (tile == null || tile.resource != bonus.resource)) return false;
        return true;
    }

    public virtual IEnumerable<UnitAuraBonus> EnumerateOwnedAuraBonuses()
    {
        if (unlockedAbilities != null)
            foreach (var ability in unlockedAbilities)
                if (ability != null && MatchesAbilityLocation(ability) && ability.auraBonuses != null)
                    foreach (var aura in ability.auraBonuses)
                        if (aura != null) yield return aura;

        foreach (var equipment in EnumerateEquippedItems())
            if (equipment?.auraBonuses != null)
                foreach (var aura in equipment.auraBonuses)
                    if (aura != null) yield return aura;
    }

    public bool AuraCanAffect(BaseUnit target, UnitAuraBonus aura)
    {
        if (target == null || aura == null) return false;
        if (target == this && !aura.includeSelf) return false;
        switch (aura.targetRelationship)
        {
            case UnitAuraTargetRelationship.SameCivilization:
                if (owner == null || target.owner != owner) return false;
                break;
            case UnitAuraTargetRelationship.Friendly:
                if (owner == null || target.owner == null) return false;
                if (target.owner != owner)
                {
                    var state = DiplomacyManager.Instance != null
                        ? DiplomacyManager.Instance.GetRelationship(owner, target.owner)
                        : (owner.relations != null && owner.relations.TryGetValue(target.owner, out var rel) ? rel : DiplomaticState.Peace);
                    if (state == DiplomaticState.War) return false;
                }
                break;
            case UnitAuraTargetRelationship.Enemy:
                if (owner == null || target.owner == null || target.owner == owner) return false;
                {
                    var state = DiplomacyManager.Instance != null
                        ? DiplomacyManager.Instance.GetRelationship(owner, target.owner)
                        : (owner.relations != null && owner.relations.TryGetValue(target.owner, out var rel) ? rel : DiplomaticState.Peace);
                    if (state != DiplomaticState.War) return false;
                }
                break;
        }

        return Civilization.MatchesCombatBonusOpponent(target, aura.targetCombatUnit, aura.targetWorkerUnit, aura.useTargetUnitCategoryFilter, aura.targetUnitCategory);
    }

    protected UnitAuraAgg AggregateIncomingAuraBonuses()
    {
        UnitAuraAgg total = new UnitAuraAgg();
        if (currentTileIndex < 0 || owner == null) return total;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null) return total;

        void AccumulateFrom(BaseUnit source)
        {
            if (source == null || source.planetIndex != planetIndex || source.currentTileIndex < 0) return;
            foreach (var aura in source.EnumerateOwnedAuraBonuses())
            {
                if (aura == null || aura.radius < 0 || !source.AuraCanAffect(this, aura)) continue;
                var tiles = MissileManager.GetTilesInRadius(ts, source.currentTileIndex, aura.radius);
                if (tiles == null || !tiles.Contains(currentTileIndex)) continue;
                total.attackAdd += aura.attackAdd;
                total.meleeAttackAdd += aura.meleeAttackAdd; total.rangedAttackAdd += aura.rangedAttackAdd; total.cityAttackAdd += aura.cityAttackAdd;
                total.groundAttackAdd += aura.groundAttackAdd; total.underwaterAttackAdd += aura.underwaterAttackAdd; total.airAttackAdd += aura.airAttackAdd; total.spaceAttackAdd += aura.spaceAttackAdd;
                total.defenseAdd += aura.defenseAdd; total.healthAdd += aura.healthAdd; total.rangeAdd += aura.rangeAdd;
                total.attackPct += aura.attackPct;
                total.meleeAttackPct += aura.meleeAttackPct; total.rangedAttackPct += aura.rangedAttackPct; total.cityAttackPct += aura.cityAttackPct;
                total.groundAttackPct += aura.groundAttackPct; total.underwaterAttackPct += aura.underwaterAttackPct; total.airAttackPct += aura.airAttackPct; total.spaceAttackPct += aura.spaceAttackPct;
                total.defensePct += aura.defensePct; total.healthPct += aura.healthPct; total.rangePct += aura.rangePct;
                total.healingRatePct += aura.healingRatePct;
            }
        }

        foreach (var civ in FindObjectsByType<Civilization>())
        {
            if (civ == null) continue;
            if (civ.combatUnits != null)
                foreach (var unit in civ.combatUnits)
                    AccumulateFrom(unit);
            if (civ.workerUnits != null)
                foreach (var unit in civ.workerUnits)
                    AccumulateFrom(unit);
        }

        foreach (var improvement in FindObjectsByType<ImprovementInstance>())
        {
            if (improvement == null || improvement.PlanetIndex != planetIndex || improvement.tileIndex < 0) continue;
            foreach (var aura in improvement.EnumerateOwnedAuraBonuses())
            {
                if (aura == null || aura.radius < 0 || !improvement.AuraCanAffect(this, aura)) continue;
                var tiles = MissileManager.GetTilesInRadius(ts, improvement.tileIndex, aura.radius);
                if (tiles == null || !tiles.Contains(currentTileIndex)) continue;
                total.attackAdd += aura.attackAdd;
                total.meleeAttackAdd += aura.meleeAttackAdd; total.rangedAttackAdd += aura.rangedAttackAdd; total.cityAttackAdd += aura.cityAttackAdd;
                total.groundAttackAdd += aura.groundAttackAdd; total.underwaterAttackAdd += aura.underwaterAttackAdd; total.airAttackAdd += aura.airAttackAdd; total.spaceAttackAdd += aura.spaceAttackAdd;
                total.defenseAdd += aura.defenseAdd; total.healthAdd += aura.healthAdd; total.rangeAdd += aura.rangeAdd;
                total.attackPct += aura.attackPct;
                total.meleeAttackPct += aura.meleeAttackPct; total.rangedAttackPct += aura.rangedAttackPct; total.cityAttackPct += aura.cityAttackPct;
                total.groundAttackPct += aura.groundAttackPct; total.underwaterAttackPct += aura.underwaterAttackPct; total.airAttackPct += aura.airAttackPct; total.spaceAttackPct += aura.spaceAttackPct;
                total.defensePct += aura.defensePct; total.healthPct += aura.healthPct; total.rangePct += aura.rangePct;
                total.healingRatePct += aura.healingRatePct;
            }
        }

        return total;
    }

    protected int GetTargetedAbilityAttackModifierAgainst(BaseUnit target)
    {
        int total = 0;
        if (unlockedAbilities == null || target == null) return total;
        foreach (var ability in unlockedAbilities)
        {
            if (!AbilityHasCombatTargetFilter(ability) || !MatchesAbilityLocation(ability))
                continue;
            if (!Civilization.MatchesCombatBonusOpponent(target, ability.targetUnit, ability.targetWorker, ability.useTargetUnitCategoryFilter, ability.targetUnitCategory))
                continue;
            total += ability.attackModifier;
        }
        return total;
    }

    protected int GetTargetedAbilityDefenseModifierAgainst(BaseUnit attacker)
    {
        int total = 0;
        if (unlockedAbilities == null || attacker == null) return total;
        foreach (var ability in unlockedAbilities)
        {
            if (!AbilityHasCombatTargetFilter(ability) || !MatchesAbilityLocation(ability))
                continue;
            if (!Civilization.MatchesCombatBonusOpponent(attacker, ability.targetUnit, ability.targetWorker, ability.useTargetUnitCategoryFilter, ability.targetUnitCategory))
                continue;
            total += ability.defenseModifier;
        }
        return total;
    }

    public int GetAbilityAttackModifier()
    {
        int total = 0;
        if (unlockedAbilities == null) return total;
        foreach (var ability in unlockedAbilities)
        {
            if (AbilityHasCombatTargetFilter(ability) || !MatchesAbilityLocation(ability))
                continue;
            total += ability.attackModifier;
        }
        return total;
    }

    public int GetAbilityDefenseModifier()
    {
        int total = 0;
        if (unlockedAbilities == null) return total;
        foreach (var ability in unlockedAbilities)
        {
            if (AbilityHasCombatTargetFilter(ability) || !MatchesAbilityLocation(ability))
                continue;
            total += ability.defenseModifier;
        }
        return total;
    }

    public int GetAbilityHealthModifier()
    {
        int total = 0;
        if (unlockedAbilities == null) return total;
        foreach (var ability in unlockedAbilities)
            if (MatchesAbilityLocation(ability))
                total += ability.healthModifier;
        return total;
    }

    public int GetAbilityRangeModifier()
    {
        int total = 0;
        if (unlockedAbilities == null) return total;
        foreach (var ability in unlockedAbilities)
            if (MatchesAbilityLocation(ability))
                total += ability.rangeModifier;
        return total;
    }


    public int GetAbilitySightRangeModifier()
    {
        int total = 0;
        if (unlockedAbilities == null) return total;
        foreach (var ability in unlockedAbilities)
            if (MatchesAbilityLocation(ability))
                total += ability.sightRangeModifier;
        return total;
    }

    public float GetAbilityAccuracyModifier()
    {
        float total = 0f;
        if (unlockedAbilities == null) return total;
        foreach (var ability in unlockedAbilities)
        {
            if (AbilityHasCombatTargetFilter(ability) || !MatchesAbilityLocation(ability))
                continue;
            total += ability.accuracyModifier;
        }
        return total;
    }

    public float GetAbilitySpaceMovementEfficiencyModifier()
    {
        float total = 0f;
        if (unlockedAbilities == null) return total;
        foreach (var ability in unlockedAbilities)
            if (MatchesAbilityLocation(ability))
                total += ability.spaceMovementEfficiencyModifier;
        return Mathf.Clamp(total, 0f, 0.75f);
    }

    public float GetAbilityRepairResistanceModifier()
    {
        float total = 0f;
        if (unlockedAbilities == null) return total;
        foreach (var ability in unlockedAbilities)
            if (MatchesAbilityLocation(ability))
                total += ability.repairResistanceModifier;
        return total;
    }

    public float GetAbilityCarrierLaunchEfficiencyModifier()
    {
        float total = 0f;
        if (unlockedAbilities == null) return total;
        foreach (var ability in unlockedAbilities)
            if (MatchesAbilityLocation(ability))
                total += ability.carrierLaunchEfficiencyModifier;
        return total;
    }

    public int GetAbilityFighterCapacityModifier()
    {
        int total = 0;
        if (unlockedAbilities == null) return total;
        foreach (var ability in unlockedAbilities)
            if (MatchesAbilityLocation(ability))
                total += ability.fighterCapacityModifier;
        return total;
    }

    public float GetAbilityFleetSupportModifier()
    {
        float total = 0f;
        if (unlockedAbilities == null) return total;
        foreach (var ability in unlockedAbilities)
            if (MatchesAbilityLocation(ability))
                total += ability.fleetSupportModifier;
        return total;
    }

    public float GetAbilityDamageMultiplier()
    {
        float total = 1f;
        if (unlockedAbilities == null) return total;
        foreach (var ability in unlockedAbilities)
        {
            if (AbilityHasCombatTargetFilter(ability) || !MatchesAbilityLocation(ability))
                continue;
            total *= ability.damageMultiplier <= 0f ? 1f : ability.damageMultiplier;
        }
        return total;
    }

    protected float GetTargetedAbilityDamageMultiplierAgainst(BaseUnit target)
    {
        float total = 1f;
        if (unlockedAbilities == null || target == null) return total;
        foreach (var ability in unlockedAbilities)
        {
            if (!AbilityHasCombatTargetFilter(ability) || !MatchesAbilityLocation(ability))
                continue;
            if (!Civilization.MatchesCombatBonusOpponent(target, ability.targetUnit, ability.targetWorker, ability.useTargetUnitCategoryFilter, ability.targetUnitCategory))
                continue;
            total *= ability.damageMultiplier <= 0f ? 1f : ability.damageMultiplier;
        }
        return total;
    }

    #endregion

    #region Current Stats (virtual - can be overridden for additional bonuses)

    public virtual int BaseMeleeAttack => BaseAttack;
    public virtual int BaseRangedAttack => BaseAttack;
    public virtual int BaseCityAttack => BaseAttack;
    public virtual int BaseGroundAttack => BaseAttack;
    public virtual int BaseUnderwaterAttack => BaseAttack;
    public virtual int BaseAirAttack => BaseAttack;
    public virtual int BaseSpaceAttack => BaseAttack;

    public virtual int CurrentMeleeAttack => CurrentAttack;
    public virtual int CurrentRangedAttack => CurrentAttack;
    public virtual int CurrentCityAttack => CurrentAttack;
    public virtual int CurrentGroundAttack => CurrentAttack;
    public virtual int CurrentUnderwaterAttack => CurrentAttack;
    public virtual int CurrentAirAttack => CurrentAttack;
    public virtual int CurrentSpaceAttack => CurrentAttack;

    public virtual int CurrentAttack
    {
        get
        {
            float valF = BaseAttack + EquipmentAttackBonus + GetAbilityAttackModifier();
            valF += GetStatusEffectAttackModifier();
            valF *= FatigueMultiplier;
            valF = ApplyResourceUpkeepToStat(valF);
            return Mathf.Max(0, Mathf.RoundToInt(valF));
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
        valF += GetStatusEffectDefenseModifier();
        valF *= FatigueMultiplier;
        valF = ApplyOwnerDefenseBonuses(valF);
        valF = ApplyTileDefenseBonuses(valF);
        valF = ApplyFortifyDefenseBonus(valF);
        valF = ApplyResourceUpkeepToStat(valF);
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
            valF += GetStatusEffectRangeModifier();
            valF = ApplyResourceUpkeepToStat(valF);
            return IsDeactivatedByResourceUpkeep ? 0f : Mathf.Max(1f, valF);
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
                    int id = gameObject != null ? gameObject.GetRuntimeId() : 0;
                    if (id != 0 && AnimalManager.Instance.TryGetSpawnComponentDump(id, out var dump))
                    {
                        compDump = "\n" + dump;
                        AnimalManager.Instance.ClearSpawnComponentDump(id);
                    }
                }
                catch { }
                Debug.LogWarning(
                    $"[BaseUnit][AnimalDestroyDiag] Animal OnDestroy: name='{name}' id={(gameObject!=null?gameObject.GetRuntimeId():0)} " +
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
        ClearLoadedProjectileVisual();
        currentProjectileWeaponVisual = null;

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
        ProcessEquipmentSlot(EquipmentType.Head, _equippedHead, armorHolder);
        ProcessEquipmentSlot(EquipmentType.Tool, _equippedTool, miscHolder);
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
        if (holder == projectileWeaponHolder)
            currentProjectileWeaponVisual = equipObj;
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
                if (equipmentData.isTwoHanded && _equippedShield != null)
                    equippedShield = null;
                if (IsProjectileWeapon(equipmentData))
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
            case EquipmentType.Head:
                if (_equippedHead != equipmentData) { equippedHead = equipmentData; changed = true; }
                break;
            case EquipmentType.Tool:
                if (_equippedTool != equipmentData) { equippedTool = equipmentData; changed = true; }
                break;
        }

        equipped = equipmentData;
        if (changed)
        {
            UpdateEquipmentVisuals();
            RaiseEquipmentChanged();
        }
    }

    #endregion

    #region Projectile System

    public static bool IsProjectileWeapon(EquipmentData equipment)
    {
        if (equipment == null)
            return false;

        return equipment.usesProjectiles ||
               equipment.projectileData != null;
    }

    protected ProjectileData ResolveProjectileForWeapon(EquipmentData weapon)
    {
        if (weapon == null)
            return null;

        if (!IsProjectileWeapon(weapon))
            return null;

        if (_activeProjectile != null)
        {
            if (IsProjectileCompatibleWithWeapon(weapon, _activeProjectile))
                return _activeProjectile;

            Debug.LogWarning($"{name}: Projectile {_activeProjectile.name} category {_activeProjectile.category} is not compatible with weapon {weapon.name} category {weapon.projectileCategory}.");
        }

        if (weapon.projectileData != null)
            return weapon.projectileData;

        return null;
    }

    protected bool IsProjectileCompatibleWithWeapon(EquipmentData weapon, ProjectileData projectile)
    {
        if (weapon == null || projectile == null)
            return false;

        if (!IsProjectileWeapon(weapon))
            return false;

        if (weapon.projectileCategory == projectile.category)
            return true;

        return weapon.projectileData == projectile;
    }

    public bool TrySetActiveProjectile(ProjectileData projectile)
    {
        EquipmentData weapon = CurrentProjectileWeapon;

        if (projectile == null)
        {
            _activeProjectile = null;
            ClearLoadedProjectileVisual();
            return true;
        }

        if (weapon != null && !IsProjectileCompatibleWithWeapon(weapon, projectile))
        {
            Debug.LogWarning($"{name}: Projectile {projectile.name} is not compatible with weapon {weapon.name}.");
            return false;
        }

        _activeProjectile = projectile;
        ClearLoadedProjectileVisual();
        return true;
    }

    protected Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null || string.IsNullOrWhiteSpace(childName))
            return null;

        foreach (Transform child in root)
        {
            if (child.name == childName)
                return child;

            Transform found = FindChildRecursive(child, childName);
            if (found != null)
                return found;
        }

        return null;
    }

    protected Transform GetProjectileNockTransform(EquipmentData weapon)
    {
        if (weapon == null)
            return null;

        if (currentProjectileWeaponVisual != null)
        {
            Transform found = FindChildRecursive(currentProjectileWeaponVisual.transform, weapon.projectileNockName);
            if (found != null)
                return found;
        }

        Debug.LogWarning($"{name}: No {weapon.projectileNockName} found on {weapon.name}.");
        return null;
    }

    protected Transform GetProjectileSpawnTransform(EquipmentData weapon)
    {
        if (weapon == null)
            return transform;

        if (weapon.useEquipmentProjectileSpawn && currentProjectileWeaponVisual != null)
        {
            Transform found = FindChildRecursive(currentProjectileWeaponVisual.transform, weapon.projectileSpawnName);
            if (found != null)
                return found;

            Debug.LogWarning($"{name}: Bow/projectile weapon {weapon.name} is missing {weapon.projectileSpawnName}; falling back to unit holders.");
        }

        Transform unitSpawn = FindChildRecursive(transform, weapon.projectileSpawnName);
        if (unitSpawn != null)
            return unitSpawn;

        if (projectileWeaponHolder != null)
            return projectileWeaponHolder;

        return transform;
    }

    protected Transform GetProjectileHandHolder()
    {
        if (projectileHandHolder != null)
            return projectileHandHolder;

        Debug.LogWarning($"{name}: ProjectileHandHolder is missing; falling back to shield/weapon/unit holder.");
        if (shieldHolder != null)
            return shieldHolder;
        if (weaponHolder != null)
            return weaponHolder;
        return transform;
    }

    protected bool ShouldUseHeldProjectileVisual(ProjectileData projectile)
    {
        return projectile != null && projectile.category == ProjectileCategory.Arrow;
    }

    protected void ClearLoadedProjectileVisual()
    {
        if (currentLoadedProjectileVisual != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(currentLoadedProjectileVisual);
            else
#endif
                Destroy(currentLoadedProjectileVisual);
            currentLoadedProjectileVisual = null;
        }

        currentLoadedProjectileWeapon = null;
        currentLoadedProjectileData = null;
    }

    public void Anim_LoadProjectile()
    {
        EquipmentData weapon = queuedProjectileEquipment != null ? queuedProjectileEquipment : CurrentProjectileWeapon;
        ProjectileData projectile = ResolveProjectileForWeapon(weapon);

        if (projectile == null)
        {
            Debug.LogWarning($"{name}: Tried to load projectile but no compatible projectile was found.");
            return;
        }

        if (!ShouldUseHeldProjectileVisual(projectile))
            return;

        ClearLoadedProjectileVisual();

        GameObject prefab = projectile.heldProjectilePrefab != null ? projectile.heldProjectilePrefab : projectile.projectilePrefab;
        if (prefab == null)
        {
            Debug.LogWarning($"{name}: Projectile {projectile.name} has no projectilePrefab or heldProjectilePrefab.");
            return;
        }

        Transform holder = GetProjectileHandHolder();
        currentLoadedProjectileVisual = Instantiate(prefab, holder);
        currentLoadedProjectileVisual.transform.localPosition = projectile.heldLocalPosition;
        currentLoadedProjectileVisual.transform.localRotation = Quaternion.Euler(projectile.heldLocalEulerAngles);
        currentLoadedProjectileVisual.transform.localScale = projectile.heldLocalScale;

        currentLoadedProjectileWeapon = weapon;
        currentLoadedProjectileData = projectile;
    }

    public void Anim_NockProjectile()
    {
        if (currentLoadedProjectileVisual == null)
            Anim_LoadProjectile();

        if (currentLoadedProjectileVisual == null)
            return;

        if (!ShouldUseHeldProjectileVisual(currentLoadedProjectileData))
            return;

        Transform nock = GetProjectileNockTransform(currentLoadedProjectileWeapon);
        if (nock == null)
            return;

        currentLoadedProjectileVisual.transform.SetParent(nock, false);
        currentLoadedProjectileVisual.transform.localPosition = currentLoadedProjectileData.nockedLocalPosition;
        currentLoadedProjectileVisual.transform.localRotation = Quaternion.Euler(currentLoadedProjectileData.nockedLocalEulerAngles);
        currentLoadedProjectileVisual.transform.localScale = currentLoadedProjectileData.nockedLocalScale;
    }

    public void Anim_ReleaseProjectile()
    {
        FireQueuedProjectile();
        ClearLoadedProjectileVisual();
    }

    public void Anim_ClearLoadedProjectile()
    {
        ClearLoadedProjectileVisual();
    }

    protected virtual bool CanConsumeProjectile(ProjectileData projectile)
    {
        return owner != null && owner.GetProjectileCount(projectile) > 0;
    }

    protected virtual bool TryConsumeProjectile(ProjectileData projectile)
    {
        return owner != null && owner.ConsumeProjectile(projectile, 1);
    }

    public virtual void SpawnProjectileFromEquipment(EquipmentData equipment, Vector3 targetPosition,
        BaseUnit targetUnit = null, int overrideDamage = -1)
    {
        ProjectileData projectile = ResolveProjectileForWeapon(equipment);

        if (projectile == null)
        {
            Debug.LogWarning($"{name}: No projectile resolved for weapon {equipment?.name}.");
            return;
        }

        if (projectile.projectilePrefab == null)
        {
            Debug.LogWarning($"{name}: Projectile {projectile.name} has no projectilePrefab.");
            return;
        }

        if (!CanConsumeProjectile(projectile))
        {
            Debug.LogWarning($"{name}: Cannot fire {projectile.name}; no ammo available.");
            return;
        }

        if (!TryConsumeProjectile(projectile))
        {
            Debug.LogWarning($"{name}: Failed to consume projectile {projectile.name}.");
            return;
        }

        Transform spawn = GetProjectileSpawnTransform(equipment);
        if (projectile.launchSound != null)
            AudioSource.PlayClipAtPoint(projectile.launchSound, spawn.position);

        GameObject obj = Instantiate(projectile.projectilePrefab, spawn.position, spawn.rotation);
        LaunchedProjectile launched = obj.GetComponent<LaunchedProjectile>();
        if (launched == null)
            launched = obj.AddComponent<LaunchedProjectile>();

        Vector3 finalTargetPosition = targetUnit != null ? targetUnit.transform.position : targetPosition;
        // The combat calculation supplies the unit/weapon portion. Ammunition contributes its
        // own flat damage here so projectile assets are mechanically meaningful.
        int finalDamage = Mathf.Max(0, overrideDamage) + Mathf.RoundToInt(projectile.damage);
        launched.Initialize(projectile, this, targetUnit, finalTargetPosition, finalDamage, spawn.forward, OnProjectileImpact);
    }

    public void QueueProjectileForAnimation(EquipmentData equipment, Vector3 targetPosition,
        BaseUnit targetUnit, int damage)
    {
        ProjectileData projectile = ResolveProjectileForWeapon(equipment);
        if (equipment != null && projectile != null)
        {
            queuedProjectileEquipment = equipment;
            queuedProjectileTargetUnit = targetUnit;
            queuedProjectileTargetPosition = targetPosition;
            queuedProjectileDamage = damage;
            hasQueuedProjectile = true;
        }
        else
        {
            Debug.LogWarning($"{name}: Could not queue projectile attack because no compatible projectile was available.");
            CancelQueuedProjectile();
        }
    }

    public void FireQueuedProjectile()
    {
        if (!hasQueuedProjectile || queuedProjectileEquipment == null) return;
        if (queuedProjectileTargetUnit == null && queuedProjectileTargetPosition == Vector3.zero)
            Debug.LogWarning($"{name}: Projectile was queued but no target exists.");

        SpawnProjectileFromEquipment(queuedProjectileEquipment, queuedProjectileTargetPosition,
            queuedProjectileTargetUnit, queuedProjectileDamage);

        hasQueuedProjectile = false;
        queuedProjectileEquipment = null;
        queuedProjectileTargetUnit = null;
        queuedProjectileDamage = -1;
    }

    public void CancelQueuedProjectile()
    {
        hasQueuedProjectile = false;
        queuedProjectileEquipment = null;
        queuedProjectileTargetUnit = null;
        queuedProjectileDamage = -1;
        ClearLoadedProjectileVisual();
    }

    protected virtual void OnProjectileImpact(LaunchedProjectile projectile)
    {
        if (projectile == null || projectile.TargetUnit == null || projectile.Damage <= 0)
            return;

        bool died = projectile.TargetUnit.ApplyDamage(projectile.Damage, this, false);
        if (projectile.ProjectileData != null && projectile.ProjectileData.statusEffect != null && projectile.TargetUnit.currentHealth > 0)
            projectile.TargetUnit.ApplyStatusEffect(projectile.ProjectileData.statusEffect);

        if (this is CombatUnit combatUnit)
        {
            combatUnit.GainExperience(projectile.Damage);
            if (died)
                combatUnit.GainExperience(projectile.Damage);
        }
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
            engagedInMelee = true;
        }

        // Inline damage application so we can raise the damage event with attacker context (avoid double events)
        try
        {
            var cu = this as CombatUnit;
            if (cu != null && cu.data != null && cu.data.unitType == CombatCategory.Animal && AnimalManager.Instance != null && AnimalManager.Instance.debugSpawning)
            {
                Debug.LogWarning($"[BaseUnit][AnimalDamageDiag] ApplyDamage called: name='{name}' id={(gameObject!=null?gameObject.GetRuntimeId():0)} damage={damageAmount} hpBefore={currentHealth} maxHP={MaxHealth} frame={Time.frameCount} time={Time.time:F3}\nStackTrace:\n{System.Environment.StackTrace}");
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

    /// <summary>
    /// Handle unit death. Override in subclasses for additional cleanup.
    /// </summary>
    protected virtual void Die()
    {
        ClearLoadedProjectileVisual();
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
                    $"[BaseUnit][AnimalDieDiag] Die called: name='{name}' id={(gameObject!=null?gameObject.GetRuntimeId():0)} " +
                    $"hp={currentHealth}/{MaxHealth} frame={Time.frameCount} time={Time.time:F3}\n" +
                    $"StackTrace:\n{System.Environment.StackTrace}"
                );
            }
        }
        catch { }

        if (animator != null && _hasDeathParam)
            SetAnimatorTriggerForFormation(deathHash);

        // Clear tile occupancy (layer-aware, stack-aware)
        if (currentTileIndex >= 0)
        {
            try
            {
                var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
                if (occ != null)
                {
                    occ.ClearOccupantById(currentTileIndex, currentLayer, gameObject.GetRuntimeId());
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

        // Clean up status effects
        CleanupAllStatusEffects();

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
        if (existingOccupant != null && existingOccupant.GetRuntimeId() != gameObject.GetRuntimeId())
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
                occ.ClearOccupantById(currentTileIndex, currentLayer, gameObject.GetRuntimeId());
            }
        }
        catch { }

        currentTileIndex = tileIndex;
        currentLayer = TileLayer.Orbit;
        stackSlot = 0; // Orbit entries are single-unit, always slot 0

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
    /// Lands from orbit through the centralized space transition flow.
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
        if (surfaceOccupant != null && surfaceOccupant.GetRuntimeId() != gameObject.GetRuntimeId())
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
            occ.ClearOccupantById(currentTileIndex, TileLayer.Orbit, gameObject.GetRuntimeId());
        }
        catch { }

        currentTileIndex = landingTileIndex;
        currentLayer = TileLayer.Surface;
        stackSlot = 0; // Landing units take slot 0

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

        if (!UnitLayerRules.CanUnitUseTileOnCurrentLayer(this, td))
        {
            if (Application.isEditor || Debug.isDebugBuild) Debug.LogWarning($"[BaseUnit] CanReachTile false: unit={name} cannot use layer={currentLayer} on tile={tileIndex}");
            return false;
        }

        if (currentLayer == TileLayer.Orbit) return true;

        int moveCost = BiomeHelper.GetMovementCost(td, this);
        if (moveCost >= 99)
        {
            if (Application.isEditor || Debug.isDebugBuild) Debug.LogWarning($"[BaseUnit] CanReachTile false: impassable cost={moveCost} unit={name} tile={tileIndex}");
            return false;
        }

        if (currentLayer == TileLayer.Surface && !td.isLand)
        {
            bool isNaval = cu != null && cu.data != null &&
                (CombatUnitData.IsNavalCategory(cu.data.unitType) || cu.data.unitType == CombatCategory.SeaPlane);
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

        if (!UnitLayerRules.CanUnitUseTileOnCurrentLayer(this, td))
        {
            if (Application.isEditor || Debug.isDebugBuild) Debug.LogWarning($"[BaseUnit] CanMoveTo false: unit={name} cannot use layer={currentLayer} on tile={tileIndex}");
            return false;
        }

        // Orbit units: skip terrain rules, only check orbit-layer occupancy
        if (currentLayer == TileLayer.Orbit)
        {
            try
            {
                var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
                var occObj = occ != null ? occ.GetOccupantObjectWithFallback(tileIndex, TileLayer.Orbit) : null;
                if (occObj != null && occObj.GetRuntimeId() != gameObject.GetRuntimeId()) return false;
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
        bool canTraverseLava = td.biome == Biome.Lava && BiomeHelper.CanUnitTraverseLava(this);
        if (currentLayer == TileLayer.Surface && !td.isLand && !canTraverseLava)
        {
            // Only specific naval CombatUnit types may enter water
            bool isNaval = cu != null && cu.data != null &&
                (CombatUnitData.IsNavalCategory(cu.data.unitType) || cu.data.unitType == CombatCategory.SeaPlane);
            if (!isNaval)
            {
                if (Application.isEditor || Debug.isDebugBuild) Debug.LogWarning($"[BaseUnit] CanMoveTo false: requires naval unit unit={name} tile={tileIndex}");
                return false;
            }
        }

        // Move-point check for units with turn-based movement. Any unit with at
        // least 1 MP can enter the next tile, even when the full movement cost
        // is higher than its remaining MP; DeductMovePoints clamps the result
        // to zero after the move is committed.
        if (GetStartingMovePoints() > 0 && currentMovePoints < 1)
        {
            if (Application.isEditor || Debug.isDebugBuild) Debug.LogWarning($"[BaseUnit] CanMoveTo false: no MP remaining current={currentMovePoints} requiredAtLeast=1 fullCost={moveCost} unit={name} tile={tileIndex}");
            return false;
        }

        // Layer-aware occupancy check (supports unit stacking)
        try
        {
            var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
            if (occ != null)
            {
                var allIds = occ.GetAllOccupantIds(tileIndex, currentLayer);
                if (allIds.Count > 0)
                {
                    int selfId = gameObject.GetRuntimeId();
                    bool selfPresent = false;
                    bool hasCity = false;
                    bool hasEnemyOrNonUnit = false;
                    Civilization myOwner = this.owner;
                    int maxStack = myOwner != null ? myOwner.GetMaxStackSize() : 1;

                    foreach (int id in allIds)
                    {
                        if (id == selfId) { selfPresent = true; continue; }
                        var obj = UnitRegistry.GetObject(id);
                        if (obj == null) continue;
                        if (obj.GetComponent<City>() != null) { hasCity = true; break; }
                        var otherUnit = obj.GetComponent<BaseUnit>();
                        if (otherUnit != null)
                        {
                            // Stacking only allowed with own units
                            if (otherUnit.owner != myOwner) { hasEnemyOrNonUnit = true; break; }
                        }
                        else
                        {
                            hasEnemyOrNonUnit = true; break;
                        }
                    }

                    if (hasCity)
                    {
                        if (Application.isEditor || Debug.isDebugBuild) Debug.LogWarning($"[BaseUnit] CanMoveTo false: tile occupied by city unit={name} tile={tileIndex}");
                        return false;
                    }
                    if (hasEnemyOrNonUnit)
                    {
                        if (Application.isEditor || Debug.isDebugBuild) Debug.LogWarning($"[BaseUnit] CanMoveTo false: tile has enemy/non-stackable occupant unit={name} tile={tileIndex}");
                        return false;
                    }

                    // Check stack capacity (exclude self if already present)
                    int othersCount = selfPresent ? allIds.Count - 1 : allIds.Count;
                    if (othersCount >= maxStack)
                    {
                        if (Application.isEditor || Debug.isDebugBuild) Debug.LogWarning($"[BaseUnit] CanMoveTo false: stack full ({othersCount}/{maxStack}) unit={name} tile={tileIndex}");
                        return false;
                    }
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
                    occ.ClearOccupantById(currentTileIndex, currentLayer, gameObject.GetRuntimeId());
                }
                int maxStack = owner != null ? owner.GetMaxStackSize() : 1;
                int slot = occ.TryAddToStack(tileIndex, currentLayer, gameObject, maxStack);
                if (slot < 0)
                {
                    // Fallback to slot 0 for backward compat (non-stacking units)
                    if (!occ.TrySetOccupant(tileIndex, gameObject, currentLayer))
                        Debug.LogWarning($"[BaseUnit] RegisterOccupancy could not claim tile {tileIndex} for {name} on layer {currentLayer}.");
                    else
                        stackSlot = 0;
                }
                else
                {
                    stackSlot = slot;
                }
            }
        }
        catch (System.Exception ex) { Debug.LogWarning($"[BaseUnit] RegisterOccupancy failed for {name}: {ex.Message}"); }
    }


    public bool CanTransitionToLayer(TileLayer targetLayer, out string reason)
    {
        reason = string.Empty;
        if (currentTileIndex < 0) { reason = "unit is not on a tile"; return false; }
        if (targetLayer == currentLayer) return true;
        if (!UnitLayerRules.CanUnitTransitionBetweenLayers(this, currentLayer, targetLayer))
        {
            reason = $"{UnitName} cannot transition from {currentLayer} to {targetLayer}";
            return false;
        }

        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var tileData = ts != null ? ts.GetTileData(currentTileIndex) : null;
        if (!UnitLayerRules.CanUnitUseTileOnLayer(this, tileData, targetLayer))
        {
            reason = $"target layer {targetLayer} is not valid on this tile";
            return false;
        }

        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        if (occ != null)
        {
            int maxStack = owner != null ? owner.GetMaxStackSize() : 1;
            if (!occ.CanJoinStack(currentTileIndex, targetLayer, maxStack))
            {
                reason = $"no available {targetLayer} stack slot";
                return false;
            }
        }

        return true;
    }

    public bool TryTransitionToLayer(TileLayer targetLayer)
    {
        if (!CanTransitionToLayer(targetLayer, out var reason))
        {
            if (Application.isEditor || Debug.isDebugBuild) Debug.LogWarning($"[BaseUnit] Layer transition failed for {name}: {reason}");
            return false;
        }

        if (targetLayer == currentLayer) return true;

        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        TileLayer oldLayer = currentLayer;
        int newSlot = -1;
        if (occ != null)
        {
            int maxStack = owner != null ? owner.GetMaxStackSize() : 1;
            newSlot = occ.TryAddToStack(currentTileIndex, targetLayer, gameObject, maxStack);
            if (newSlot < 0) return false;
            try { occ.ClearOccupantById(currentTileIndex, oldLayer, gameObject.GetRuntimeId()); } catch { }
        }

        currentLayer = targetLayer;
        if (newSlot >= 0) stackSlot = newSlot;

        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts != null)
        {
            Vector3 pos = ts.GetTileSurfacePosition(currentTileIndex);
            if (targetLayer == TileLayer.Orbit) pos += Vector3.up * PlanetGenerator.GetOrbitHeight(planetIndex);
            transform.position = pos;
            ApplyStackOffset();
        }

        return true;
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

    protected int ApplySharedMeleeCombatModifiers(int baseDamage, BaseUnit target)
    {
        if (target == null)
            return Mathf.Max(0, baseDamage);

        int modifiedDamage = Mathf.Max(0, baseDamage);

        int flankCount = CountAdjacentAllies(target.currentTileIndex) - 1;
        if (flankCount > 0)
            modifiedDamage = Mathf.RoundToInt(modifiedDamage * (1f + 0.1f * flankCount));

        if (!IsInOrbit && !target.IsInOrbit)
        {
            float elevationDiff = transform.position.y - target.transform.position.y;
            float elevationMultiplier = 1f + Mathf.Clamp(elevationDiff * 0.02f, -0.1f, 0.1f);
            modifiedDamage = Mathf.Max(0, Mathf.RoundToInt(modifiedDamage * elevationMultiplier));
        }

        return modifiedDamage;
    }

    public virtual int GetSituationalAttackAddAgainst(BaseUnit target) => 0;
    public virtual float GetSituationalAttackPctAgainst(BaseUnit target) => 0f;
    public virtual int GetSituationalDefenseAddAgainst(BaseUnit attacker) => 0;
    public virtual float GetSituationalDefensePctAgainst(BaseUnit attacker) => 0f;

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

    [System.NonSerialized] private bool resourceUpkeepSatisfied = true;
    [System.NonSerialized] private ResourceUpkeepFailureBehavior resourceUpkeepFailureBehavior = ResourceUpkeepFailureBehavior.Deactivate;
    [System.NonSerialized] private float resourceUpkeepFailureDebuffMultiplier = 1f;

    public bool IsResourceUpkeepSatisfied => resourceUpkeepSatisfied;
    public bool IsDeactivatedByResourceUpkeep => !resourceUpkeepSatisfied && resourceUpkeepFailureBehavior == ResourceUpkeepFailureBehavior.Deactivate;
    public bool IsDebuffedByResourceUpkeep => !resourceUpkeepSatisfied && resourceUpkeepFailureBehavior == ResourceUpkeepFailureBehavior.Debuff;
    public float ResourceUpkeepDebuffMultiplier => IsDebuffedByResourceUpkeep ? Mathf.Clamp01(resourceUpkeepFailureDebuffMultiplier) : 1f;

    public void SetResourceUpkeepState(bool satisfied, ResourceUpkeepFailureBehavior failureBehavior, float debuffMultiplier)
    {
        resourceUpkeepSatisfied = satisfied;
        resourceUpkeepFailureBehavior = failureBehavior;
        resourceUpkeepFailureDebuffMultiplier = Mathf.Clamp01(debuffMultiplier);
        ApplyResourceUpkeepToCurrentActionPools();
    }

    protected int ApplyResourceUpkeepToTurnPoints(int amount)
    {
        if (IsDeactivatedByResourceUpkeep)
            return 0;

        if (IsDebuffedByResourceUpkeep)
            return Mathf.Max(0, Mathf.FloorToInt(amount * ResourceUpkeepDebuffMultiplier));

        return amount;
    }

    protected float ApplyResourceUpkeepToStat(float value)
    {
        if (IsDeactivatedByResourceUpkeep)
            return 0f;

        if (IsDebuffedByResourceUpkeep)
            return value * ResourceUpkeepDebuffMultiplier;

        return value;
    }

    private void ApplyResourceUpkeepToCurrentActionPools()
    {
        int oldMove = currentMovePoints;
        int moveCap = ApplyResourceUpkeepToTurnPoints(currentMovePoints);
        currentMovePoints = Mathf.Min(currentMovePoints, moveCap);
        if (oldMove != currentMovePoints)
            try { GameEventManager.Instance?.RaiseMovePointsChanged(this, oldMove, currentMovePoints); } catch { }

        int oldAttackPoints = currentAttackPoints;
        int attackCap = ApplyResourceUpkeepToTurnPoints(currentAttackPoints);
        currentAttackPoints = Mathf.Min(currentAttackPoints, attackCap);
        if (oldAttackPoints != currentAttackPoints)
            try { GameEventManager.Instance?.RaiseAttackPointsChanged(this, oldAttackPoints, currentAttackPoints, MaxAttackPoints); } catch { }
    }

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

        // Status effect movement modifiers
        move += GetStatusEffectMovementModifier();

        if (hasWinterPenalty && ClimateManager.Instance != null && ClimateManager.Instance.currentSeason == Season.Winter)
        {
            move = Mathf.Max(1, move - 1);
        }
        move = Mathf.Max(0, move);
        move = ApplyResourceUpkeepToTurnPoints(move);
        int old = currentMovePoints;
        currentMovePoints = move;
        try { GameEventManager.Instance?.RaiseMovePointsChanged(this, old, currentMovePoints); } catch { }
    }

    protected bool IsImmuneToMosquitoes()
    {
        if (this is CombatUnit combatUnit)
        {
            if (combatUnit.data != null)
            {
                var ut = combatUnit.data.unitType;
                if (ut == CombatCategory.Animal || CombatUnitData.IsAirCategory(ut) || CombatUnitData.IsNavalCategory(ut))
                    return true;
            }

            return combatUnit.data != null && combatUnit.data.immuneToMosquitoes;
        }

        if (this is WorkerUnit workerUnit)
            return workerUnit.data != null && workerUnit.data.immuneToMosquitoes;

        return true;
    }

    protected bool IsImmuneToLava()
    {
        if (this is CombatUnit combatUnit)
        {
            if (combatUnit.data == null)
                return false;

            if (combatUnit.data.immuneToLava || combatUnit.data.unitType == CombatCategory.LavaSwimmer)
                return true;

            if (combatUnit.data is DemonUnitData demonData)
                return demonData.canCrossLava;

            return false;
        }

        if (this is WorkerUnit workerUnit)
            return workerUnit.data != null && workerUnit.data.immuneToLava;

        return false;
    }

    protected bool TryGetEnvironmentalDamagePercent(HexTileData tileData, out float damagePercent)
    {
        damagePercent = 0f;

        if (tileData == null || currentLayer != TileLayer.Surface)
            return false;

        if (isStored || storedInImprovement != null || storedInHerd != null)
            return false;

        if (this is CombatUnit combatUnit && combatUnit.IsTransported)
            return false;

        if (!BiomeHelper.IsDamagingBiome(tileData.biome))
            return false;

        if (tileData.biome == Biome.Lava && IsImmuneToLava())
            return false;

        damagePercent = BiomeHelper.GetBiomeDamage(tileData.biome);
        if (damagePercent <= 0f)
            return false;

        if (owner != null)
        {
            try
            {
                damagePercent *= owner.GetAttritionModifierTotals(null, null).BiomeDamageMultiplier;
            }
            catch
            {
            }
        }

        return damagePercent > 0f;
    }

    protected void ApplyMosquitoDamageIfNeeded(string unitDisplayName)
    {
        if (owner == null || owner.civData == null)
            return;

        if (owner.civData.isTribe || owner.civData.isCityState)
            return;

        // Stored/sheltered units, transported units, and any non-surface unit are immune.
        if (isStored || storedInImprovement != null || storedInHerd != null)
            return;

        if (this is CombatUnit combatUnit && combatUnit.IsTransported)
            return;

        if (currentTileIndex < 0 || currentLayer != TileLayer.Surface)
            return;

        if (IsImmuneToMosquitoes() || owner.HasMosquitoImmunityTechnology())
            return;

        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var tileData = ts != null ? ts.GetTileData(currentTileIndex) : null;
        if (tileData == null || !tileData.hasMosquitoes)
            return;

        int damageAmount = Mathf.Max(1, Mathf.CeilToInt(MaxHealth * MosquitoDamagePercent));
        ApplyDamage(damageAmount);

        if (owner.isPlayerControlled)
        {
            string tileLabel = !string.IsNullOrWhiteSpace(tileData.continentName) ? tileData.continentName : "mosquito-infested territory";
            UIManager.Instance?.ShowNotification($"{unitDisplayName} took {damageAmount} mosquito damage in {tileLabel}!");
        }
    }

    #endregion
}
