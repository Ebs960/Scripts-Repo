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

    #region Core Unit Fields

    [Header("Unit UI")]
    [SerializeField] protected GameObject unitLabelPrefab;
    protected UnitLabel unitLabelInstance;

    [Header("Weather")]
    [Tooltip("If true, this unit takes weather attrition in severe seasons")]
    public bool takesWeatherDamage = true;

    // Core references
    protected SphericalHexGrid grid;
    protected PlanetGenerator planet;
    protected Animator animator;

    // Runtime state
    public Civilization owner { get; protected set; }
    public int currentHealth { get; protected set; }
    public int currentTileIndex;
    public float moveSpeed = 2f;
    public bool isMoving { get; set; }

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
    protected Coroutine meleeEngageCoroutine = null;

    // Trap immobilization
    protected int trappedTurnsRemaining = 0;
    public bool IsTrapped => trappedTurnsRemaining > 0;
    
    // Winter penalty flag
    public bool hasWinterPenalty { get; set; }

    #endregion

    #region Animation Hashes

    // Animation parameter hashes for efficiency
    protected static readonly int isWalkingHash = Animator.StringToHash("IsWalking");
    protected static readonly int attackHash = Animator.StringToHash("Attack");
    protected static readonly int hitHash = Animator.StringToHash("Hit");
    protected static readonly int deathHash = Animator.StringToHash("Death");
    protected static readonly int routHash = Animator.StringToHash("Rout");

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
    protected abstract float MeleeEngageDuration { get; }

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
            float valF = BaseDefense + EquipmentDefenseBonus + GetAbilityDefenseModifier();
            // Include tile-based defense bonus
            if (currentTileIndex >= 0 && TileSystem.Instance != null)
            {
                var tileData = TileSystem.Instance.GetTileData(currentTileIndex);
                if (tileData != null)
                {
                    // Subclasses can add their own tile bonuses
                }
            }
            return Mathf.RoundToInt(valF);
        }
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

        // Get planet/grid references
        planet = GameManager.Instance?.GetPlanetGenerator(0); // Default to Earth
        if (planet != null)
            grid = planet.Grid;

        // Register with UnitRegistry
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
                    Destroy(item);
            }
        }
        equippedItemObjects.Clear();

        // Process all equipment slots
        ProcessEquipmentSlot(EquipmentType.Weapon, _equippedWeapon, weaponHolder);
        ProcessEquipmentSlot(EquipmentType.Weapon, _equippedProjectileWeapon, projectileWeaponHolder);
        ProcessEquipmentSlot(EquipmentType.Shield, _equippedShield, shieldHolder);
        ProcessEquipmentSlot(EquipmentType.Armor, _equippedArmor, armorHolder);
        ProcessEquipmentSlot(EquipmentType.Miscellaneous, _equippedMiscellaneous, miscHolder);
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
                    Destroy(child.gameObject);
            }
        }

        if (itemData == null) return;

        UpdateEquipmentSlot(type, itemData, holder);
    }

    protected virtual void UpdateEquipmentSlot(EquipmentType type, EquipmentData itemData, Transform holder)
    {
        if (holder == null || itemData == null || itemData.equipmentPrefab == null) return;

        // Instantiate equipment
        GameObject equipObj = Instantiate(itemData.equipmentPrefab);
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
        if (animator != null && HasParameter(animator, hitHash))
            animator.SetTrigger(hitHash);

        currentHealth -= damageAmount;

        // Update label
        UpdateUnitLabel();

        if (currentHealth <= 0)
        {
            Die();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Apply damage with attacker context for melee engagement tracking
    /// </summary>
    public virtual bool ApplyDamage(int damageAmount, BaseUnit attacker, bool attackerIsMelee)
    {
        if (attackerIsMelee)
        {
            engagedInMelee = true;
            if (meleeEngageCoroutine != null) StopCoroutine(meleeEngageCoroutine);
            meleeEngageCoroutine = StartCoroutine(EndMeleeEngageAfterDelay(MeleeEngageDuration));
        }
        return ApplyDamage(damageAmount);
    }

    protected System.Collections.IEnumerator EndMeleeEngageAfterDelay(float delay)
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
    /// Handle unit death. Override in subclasses for additional cleanup.
    /// </summary>
    protected virtual void Die()
    {
        if (animator != null && HasParameter(animator, deathHash))
            animator.SetTrigger(deathHash);

        // Clear tile occupancy
        if (currentTileIndex >= 0 && TileSystem.Instance != null)
        {
            TileSystem.Instance.ClearTileOccupant(currentTileIndex);
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
    /// Request movement to target tile. Uses UnitMovementController.
    /// </summary>
    public virtual void MoveTo(int targetTileIndex)
    {
        var path = UnitMovementController.Instance.FindPath(currentTileIndex, targetTileIndex);
        if (path == null || path.Count == 0) return;

        StopAllCoroutines();
        StartCoroutine(UnitMovementController.Instance.MoveAlongPath(this, path));
    }

    /// <summary>
    /// Check if unit can move to a tile. Override in subclasses for specific rules.
    /// </summary>
    public virtual bool CanMoveTo(int tileIndex)
    {
        var td = TileSystem.Instance?.GetTileData(tileIndex);
        if (td == null || !td.isPassable) return false;
        if (td.occupantId != 0 && td.occupantId != gameObject.GetInstanceID()) return false;
        return true;
    }

    /// <summary>
    /// Update the walking animation state
    /// </summary>
    public virtual void UpdateWalkingState(bool walking)
    {
        if (animator == null) return;
        if (HasParameter(animator, isWalkingHash))
            animator.SetBool(isWalkingHash, walking);
        isMoving = walking;
    }

    /// <summary>
    /// Position unit on surface (flat-only). Places unit at planar tile center and keeps upright orientation.
    /// </summary>
    protected virtual void PositionUnitOnSurface(int tileIndex)
    {
        if (TileSystem.Instance == null) return;
        Vector3 flatCenter = TileSystem.Instance.GetTileCenterFlat(tileIndex);
        transform.position = flatCenter;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        currentTileIndex = tileIndex;
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
            var labelGO = Instantiate(unitLabelPrefab, transform);
            unitLabelInstance = labelGO.GetComponent<UnitLabel>();
            if (unitLabelInstance != null)
            {
                string ownerName = owner != null && owner.civData != null 
                    ? owner.civData.civName : "Unknown";
                unitLabelInstance.Initialize(transform, UnitName, ownerName, currentHealth, MaxHealth);

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
        var neighbours = TileSystem.Instance?.GetNeighbors(tileIndex);
        if (neighbours == null) return 0;

        foreach (int idx in neighbours)
        {
            var tdata = TileSystem.Instance?.GetTileData(idx);
            if (tdata == null || tdata.occupantId == 0) continue;

            var obj = UnitRegistry.GetObject(tdata.occupantId);
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

    #endregion
}
