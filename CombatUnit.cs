using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using TMPro;
using GameCombat;

public class CombatUnit : BaseUnit
{
    [Header("Stats (Override Data Asset)")]
    [HideInInspector][SerializeField] private int attack = 0; // legacy generic fallback
    [SerializeField] private int meleeAttack = 0;
    [SerializeField] private int rangedAttack = 0;
    [SerializeField] private int cityAttack = 0;
    [SerializeField] private int groundAttack = 0;
    [SerializeField] private int underwaterAttack = 0;
    [SerializeField] private int airAttack = 0;
    [SerializeField] private int spaceAttack = 0;
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
    public override int BaseMeleeAttack => useOverrideStats && meleeAttack > 0 ? meleeAttack : (data?.baseMeleeAttack ?? 0);
    public override int BaseRangedAttack => useOverrideStats && rangedAttack > 0 ? rangedAttack : (data?.baseRangedAttack ?? 0);
    public override int BaseCityAttack => useOverrideStats && cityAttack > 0 ? cityAttack : (data?.baseCityAttack ?? 0);
    public override int BaseGroundAttack => useOverrideStats && groundAttack > 0 ? groundAttack : (data?.baseGroundAttack ?? 0);
    public override int BaseUnderwaterAttack => useOverrideStats && underwaterAttack > 0 ? underwaterAttack : (data?.baseUnderwaterAttack ?? 0);
    public override int BaseAirAttack => useOverrideStats && airAttack > 0 ? airAttack : (data?.baseAirAttack ?? 0);
    public override int BaseSpaceAttack => useOverrideStats && spaceAttack > 0 ? spaceAttack : (data?.baseSpaceAttack ?? 0);
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

    [Header("Military Formation")]
    [SerializeField] private string militaryFormationId;
    [SerializeField] private MilitaryFormationType militaryFormationType;
    [SerializeField] private string militaryFormationName;

    public string MilitaryFormationId => militaryFormationId;
    public MilitaryFormationType MilitaryFormationType => militaryFormationType;
    public string MilitaryFormationName => militaryFormationName;

    public string EnsureMilitaryFormationIdentity()
    {
        if (string.IsNullOrEmpty(militaryFormationId))
            militaryFormationId = System.Guid.NewGuid().ToString("N");

        return militaryFormationId;
    }

    public void AssignMilitaryFormation(string formationId, MilitaryFormationType formationType, string formationName = null)
    {
        if (string.IsNullOrEmpty(formationId))
            return;

        militaryFormationId = formationId;
        militaryFormationType = formationType;
        if (!string.IsNullOrEmpty(formationName))
            militaryFormationName = formationName;
    }

    [System.NonSerialized] private bool goldMaintenanceSatisfied = true;
    public bool IsGoldMaintenanceSatisfied => goldMaintenanceSatisfied;

    public void SetGoldMaintenanceState(bool satisfied)
    {
        goldMaintenanceSatisfied = satisfied;
    }

    private float ApplyGoldMaintenanceToCombatStat(float value)
    {
        return goldMaintenanceSatisfied ? value : value * 0.5f;
    }
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

    /// <summary>Whether this unit performed any movement/attack/action during its previous turn.</summary>
    public bool actedLastTurn { get; private set; }

    /// <summary>Tracks any action this turn for rest-based reinforcement without changing turn-consuming action semantics.</summary>
    public bool performedActionThisTurn { get; private set; }
    
    /// <summary>
    /// Whether this unit is currently stationed in a friendly city.
    /// Set by UnitReinforcementManager each turn.
    /// </summary>
    public bool isGarrisonedInCity { get; set; }


    /// <summary>Launches an aircraft mission through the central aircraft mission manager.</summary>
    public AircraftMissionResult LaunchAircraftMission(AircraftMissionKind missionKind, int targetTileIndex)
    {
        if (AircraftMissionManager.Instance == null)
        {
            Debug.LogWarning($"[CombatUnit] Cannot launch {missionKind}: no AircraftMissionManager in scene.");
            return AircraftMissionResult.Invalid;
        }
        return AircraftMissionManager.Instance.LaunchMission(this, missionKind, targetTileIndex);
    }

    /// <summary>Launches a space/orbital mission through the central space mission manager.</summary>
    public SpaceMissionResult LaunchSpaceMission(SpaceMissionKind missionKind, int targetTileIndex)
    {
        if (SpaceMissionManager.Instance == null)
        {
            Debug.LogWarning($"[CombatUnit] Cannot launch {missionKind}: no SpaceMissionManager in scene.");
            return SpaceMissionResult.Invalid;
        }
        return SpaceMissionManager.Instance.LaunchMission(this, missionKind, targetTileIndex);
    }

    /// <summary>True when this unit can perform air-jump/paratrooper deployment.</summary>
    public bool CanAirJump => data != null && data.canAirJump && data.airJumpRange > 0;

    /// <summary>Validate an air-jump/paratrooper deployment target.</summary>
    public bool CanAirJumpTo(int targetTileIndex, out string reason)
    {
        reason = null;
        if (data == null) { reason = "missing unit data"; return false; }
        if (!data.canAirJump) { reason = $"{UnitName} cannot air jump"; return false; }
        if (CampaignArmyService.GetMembers(this).Count > 1) { reason = "split this unit from its army before air jumping"; return false; }
        if (targetTileIndex < 0) { reason = "invalid target tile"; return false; }
        if (targetTileIndex == currentTileIndex) { reason = "target tile is the current tile"; return false; }
        if (IsTransported || isStored) { reason = $"{UnitName} is currently loaded or stored"; return false; }
        if (currentLayer != TileLayer.Surface) { reason = $"{UnitName} must start on the surface"; return false; }
        if (data.airJumpConsumesAction && hasActedThisTurn) { reason = $"{UnitName} has already acted this turn"; return false; }

        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null) { reason = "no tile system"; return false; }
        var tile = ts.GetTileData(targetTileIndex);
        if (tile == null || !tile.isPassable) { reason = "target tile is not passable"; return false; }
        if (!tile.isLand) { reason = "air jump must land on land"; return false; }

        int range = Mathf.Max(0, data.airJumpRange);
        int distance = Mathf.RoundToInt(ts.GetTileDistance(currentTileIndex, targetTileIndex));
        if (distance > range) { reason = $"target is out of air-jump range ({distance}>{range})"; return false; }

        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        if (occ == null) { reason = "no occupancy manager"; return false; }
        if (!occ.CanJoinStack(targetTileIndex, TileLayer.Surface, 1))
        {
            reason = "target tile has no available surface stack slot";
            return false;
        }

        return true;
    }

    /// <summary>Perform an air-jump/paratrooper deployment to a target tile on the same planet.</summary>
    public bool AirJumpTo(int targetTileIndex)
    {
        if (!CanAirJumpTo(targetTileIndex, out string reason))
        {
            Debug.LogWarning($"[CombatUnit] Air jump rejected: {reason}");
            return false;
        }

        moveOrderPath = null;
        moveOrderNextStep = 0;
        try { UnitMovementController.Instance?.StopMoveForUnit(this); } catch { }
        ClearFortify();
        StartCoroutine(AirJumpRoutine(targetTileIndex));
        return true;
    }

    private IEnumerator AirJumpRoutine(int targetTileIndex)
    {
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        if (ts == null || occ == null) yield break;

        int oldTile = currentTileIndex;
        Vector3 start = transform.position;
        Vector3 landing = ts.GetTileSurfacePosition(targetTileIndex);
        float height = data != null ? data.airJumpDropHeight : 12f;
        float duration = data != null ? Mathf.Max(0.01f, data.airJumpAnimationDuration) : 0.8f;

        if (data != null && data.airJumpLaunchVFX != null)
            Instantiate(data.airJumpLaunchVFX, start, Quaternion.identity);

        UpdateWalkingState(false);
        Vector3 apex = (start + landing) * 0.5f + Vector3.up * height;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            Vector3 a = Vector3.Lerp(start, apex, t);
            Vector3 b = Vector3.Lerp(apex, landing, t);
            transform.position = Vector3.Lerp(a, b, t);
            yield return null;
        }

        int newSlot = occ.TryAddToStack(targetTileIndex, TileLayer.Surface, gameObject, 1);
        if (newSlot < 0)
        {
            Debug.LogWarning($"[CombatUnit] Air jump landing failed: tile {targetTileIndex} became occupied.");
            PositionUnitOnSurface(oldTile);
            yield break;
        }

        try { occ.ClearOccupantById(oldTile, currentLayer, gameObject.GetRuntimeId()); } catch { }
        currentTileIndex = targetTileIndex;
        currentLayer = TileLayer.Surface;
        stackSlot = newSlot;
        transform.position = landing;

        if (data != null && data.airJumpLandingVFX != null)
            Instantiate(data.airJumpLandingVFX, landing, Quaternion.identity);

        if (data == null || data.airJumpConsumesAction)
            ConsumeAction();
        else
            RecordTurnAction();
        AddFatigue(6f);
        TriggerMovementComplete();
        try { ImprovementManager.Instance?.NotifyUnitEnteredTile(targetTileIndex, this); } catch { }
    }

    /// <summary>
    /// Missiles currently carried by this unit (missile submarines, missile cruisers, MLRS, etc.).
    /// Only populated when <see cref="CombatUnitData.canStoreMissiles"/> is true.
    /// </summary>
    public List<MissileData> storedMissiles = new List<MissileData>();

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
        RebuildEquipmentGrantedAbilities();
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
        CampaignArmyService.EnsureArmyIdentity(this);
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

        // Campaign armies use one representative figure. The authored soldier count remains
        // available on CombatUnitData for tactical battle presentation.
    }

    /// <summary>Stores this persistent military unit in a Band without recreating it.</summary>
    public void StoreInBand(Band band)
    {
        if (band == null) return;
        StoredInBand = band;
        isStored = true;
        currentTileIndex = -1;
        moveOrderPath = null;
        gameObject.SetActive(false);
    }

    /// <summary>Returns this same unit (including health, XP and equipment) to the campaign.</summary>
    public void ReleaseFromBand(int tileIndex, int targetPlanetIndex)
    {
        StoredInBand = null;
        isStored = false;
        planetIndex = targetPlanetIndex;
        currentTileIndex = tileIndex;
        gameObject.SetActive(true);
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts != null) transform.position = ts.GetTileCenterFlat(tileIndex);
        (TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance)?.SetOccupant(tileIndex, gameObject, currentLayer);
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
    private struct UnitAgg { public int attackAdd, meleeAttackAdd, rangedAttackAdd, cityAttackAdd, groundAttackAdd, underwaterAttackAdd, airAttackAdd, spaceAttackAdd, defenseAdd, healthAdd, moveAdd, rangeAdd, apAdd; public float attackPct, meleeAttackPct, rangedAttackPct, cityAttackPct, groundAttackPct, underwaterAttackPct, airAttackPct, spaceAttackPct, defensePct, healthPct, movePct, rangePct, apPct; }
    private struct EquipAgg { public int attackAdd, meleeAttackAdd, rangedAttackAdd, cityAttackAdd, groundAttackAdd, underwaterAttackAdd, airAttackAdd, spaceAttackAdd, defenseAdd, healthAdd, moveAdd, rangeAdd, apAdd; public float attackPct, meleeAttackPct, rangedAttackPct, cityAttackPct, groundAttackPct, underwaterAttackPct, airAttackPct, spaceAttackPct, defensePct, healthPct, movePct, rangePct, apPct; }


    private bool MatchesTerritoryRequirement(HexTileData tile, Civilization civ, UnitTerritoryRequirement requirement)
    {
        if (requirement == UnitTerritoryRequirement.Any)
            return true;
        if (tile == null || civ == null)
            return false;

        var tileOwner = tile.owner;
        switch (requirement)
        {
            case UnitTerritoryRequirement.Owned:
                return tileOwner == civ;
            case UnitTerritoryRequirement.Unowned:
                return tileOwner == null;
            case UnitTerritoryRequirement.Enemy:
                return tileOwner != null && tileOwner != civ && DiplomacyManager.Instance != null
                    ? DiplomacyManager.Instance.GetRelationship(civ, tileOwner) == DiplomaticState.War
                    : tileOwner != null && tileOwner != civ && civ.relations.TryGetValue(tileOwner, out var enemyState) && enemyState == DiplomaticState.War;
            case UnitTerritoryRequirement.Friendly:
                if (tileOwner == null || tileOwner == civ) return false;
                if (DiplomacyManager.Instance != null)
                    return DiplomacyManager.Instance.GetRelationship(civ, tileOwner) != DiplomaticState.War;
                return !civ.relations.TryGetValue(tileOwner, out var friendlyState) || friendlyState != DiplomaticState.War;
            default:
                return true;
        }
    }

    private bool MatchesUnitBonusLocation(Civilization civ, UnitStatBonus bonus)
    {
        if (bonus == null)
            return false;

        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var tile = ts != null && currentTileIndex >= 0 ? ts.GetTileData(currentTileIndex) : null;
        bool isCityTile = tile?.controllingCity != null;

        if (!MatchesRequirement(bonus.cityRequirement, isCityTile))
            return false;
        if (bonus.useBiomeFilter && (tile == null || tile.biome != bonus.biome))
            return false;
        if (!MatchesRequirement(bonus.hillRequirement, tile != null && tile.isHill))
            return false;
        if (!MatchesRequirement(bonus.mountainRequirement, tile != null && tile.isMountain))
            return false;
        if (!MatchesLayerRequirement(bonus.layerRequirement, tile))
            return false;
        if (!MatchesRequirement(bonus.underwaterRequirement, tile != null && tile.IsUnderwaterTile))
            return false;
        if (!MatchesRequirement(bonus.orbitRequirement, tile != null && tile.isSpace))
            return false;
        if (bonus.useResourceFilter && (tile == null || tile.resource != bonus.resource))
            return false;
        if (!MatchesTerritoryRequirement(tile, civ, bonus.territoryRequirement))
            return false;
        if (civ != null && !civ.MatchesSeasonFilterForPlanet(bonus.useSeasonFilter, bonus.seasons, planetIndex))
            return false;
        if (civ == null && bonus.useSeasonFilter)
            return false;
        if (!PlanetBonusFilterUtility.MatchesPlanetFilter(bonus.earthWorldScope, bonus.usePlanetFilter, bonus.planets, bonus.planetTypes, planetIndex))
            return false;

        return true;
    }

    private City GetCurrentCityContext()
    {
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var tile = ts != null && currentTileIndex >= 0 ? ts.GetTileData(currentTileIndex) : null;
        return tile?.controllingCity;
    }

    private UnitAgg AggregateUnitBonusesLocal(Civilization civ, CombatUnitData u)
    {
        UnitAgg a = new UnitAgg(); if (u == null) return a;

        void Add(UnitStatBonus b)
        {
            a.attackAdd += b.attackAdd; a.meleeAttackAdd += b.meleeAttackAdd; a.rangedAttackAdd += b.rangedAttackAdd; a.cityAttackAdd += b.cityAttackAdd; a.groundAttackAdd += b.groundAttackAdd; a.underwaterAttackAdd += b.underwaterAttackAdd; a.airAttackAdd += b.airAttackAdd; a.spaceAttackAdd += b.spaceAttackAdd; a.defenseAdd += b.defenseAdd; a.healthAdd += b.healthAdd;
            a.moveAdd += b.movePointsAdd; a.rangeAdd += b.rangeAdd;
            a.attackPct += b.attackPct; a.meleeAttackPct += b.meleeAttackPct; a.rangedAttackPct += b.rangedAttackPct; a.cityAttackPct += b.cityAttackPct; a.groundAttackPct += b.groundAttackPct; a.underwaterAttackPct += b.underwaterAttackPct; a.airAttackPct += b.airAttackPct; a.spaceAttackPct += b.spaceAttackPct; a.defensePct += b.defensePct; a.healthPct += b.healthPct;
            a.movePct += b.movePointsPct; a.rangePct += b.rangePct;
        }

        void Accumulate(UnitStatBonus[] bonuses)
        {
            if (bonuses == null) return;
            foreach (var b in bonuses)
            {
                if (b == null || !Civilization.MatchesCombatUnitBonusTarget(u, b.unit, b.useUnitCategoryFilter, b.unitCategory) || !MatchesUnitBonusLocation(civ, b))
                    continue;
                if (b.targetUnit != null || b.targetWorker != null || b.useTargetUnitCategoryFilter)
                    continue;

                Add(b);
            }
        }

        Accumulate(u.intrinsicStatBonuses);
        if (civ == null) return a;
        Accumulate(civ.civData?.unitBonuses);
        Accumulate(civ.civData?.effects?.unitBonuses);
        Accumulate(civ.leader?.unitBonuses);

        if (civ.researchedTechs != null)
            foreach (var t in civ.researchedTechs)
                Accumulate(t?.unitBonuses);

        if (civ.researchedCultures != null)
            foreach (var c in civ.researchedCultures)
                Accumulate(c?.unitBonuses);

        Accumulate(civ.currentGovernment?.unitBonuses);

        if (civ.activePolicies != null)
            foreach (var policy in civ.activePolicies)
                Accumulate(policy?.unitBonuses);

        foreach (var pantheonBonuses in civ.EnumeratePantheonBonuses())
            Accumulate(pantheonBonuses?.unitBonuses);

        foreach (var belief in civ.EnumerateActiveBeliefs())
            if (civ.IsBeliefSeasonActive(belief, planetIndex))
                Accumulate(belief?.unitBonuses);

        AccumulateBuildingUnitBonuses(civ, BuildingUnitBonusScope.AllCivilizationUnits, Accumulate);

        var cityContext = GetCurrentCityContext();
        if (cityContext != null)
        {
            foreach (var (building, _, _) in cityContext.EnumerateOperationalBuildings())
                AccumulateBuildingUnitBonuses(building?.unitBonuses, BuildingUnitBonusScope.SameCity, Accumulate);
        }

        return a;
    }

    private UnitAgg AggregateTargetedCombatBonuses(Civilization civ, CombatUnitData actualUnit, BaseUnit opponent)
    {
        UnitAgg a = new UnitAgg(); if (civ == null || actualUnit == null || opponent == null) return a;

        void Accumulate(UnitStatBonus[] bonuses)
        {
            if (bonuses == null) return;
            foreach (var b in bonuses)
            {
                if (b == null || !Civilization.MatchesCombatUnitBonusTarget(actualUnit, b.unit, b.useUnitCategoryFilter, b.unitCategory) || !MatchesUnitBonusLocation(civ, b))
                    continue;
                if (!Civilization.MatchesCombatBonusOpponent(opponent, b.targetUnit, b.targetWorker, b.useTargetUnitCategoryFilter, b.targetUnitCategory))
                    continue;

                a.attackAdd += b.attackAdd; a.meleeAttackAdd += b.meleeAttackAdd; a.rangedAttackAdd += b.rangedAttackAdd; a.cityAttackAdd += b.cityAttackAdd; a.groundAttackAdd += b.groundAttackAdd; a.underwaterAttackAdd += b.underwaterAttackAdd; a.airAttackAdd += b.airAttackAdd; a.spaceAttackAdd += b.spaceAttackAdd;
                a.defenseAdd += b.defenseAdd;
                a.attackPct += b.attackPct; a.meleeAttackPct += b.meleeAttackPct; a.rangedAttackPct += b.rangedAttackPct; a.cityAttackPct += b.cityAttackPct; a.groundAttackPct += b.groundAttackPct; a.underwaterAttackPct += b.underwaterAttackPct; a.airAttackPct += b.airAttackPct; a.spaceAttackPct += b.spaceAttackPct;
                a.defensePct += b.defensePct;
            }
        }

        Accumulate(actualUnit.intrinsicStatBonuses);
        Accumulate(civ.civData?.unitBonuses);
        Accumulate(civ.civData?.effects?.unitBonuses);
        Accumulate(civ.leader?.unitBonuses);

        if (civ.researchedTechs != null)
            foreach (var t in civ.researchedTechs)
                Accumulate(t?.unitBonuses);

        if (civ.researchedCultures != null)
            foreach (var c in civ.researchedCultures)
                Accumulate(c?.unitBonuses);

        Accumulate(civ.currentGovernment?.unitBonuses);

        if (civ.activePolicies != null)
            foreach (var policy in civ.activePolicies)
                Accumulate(policy?.unitBonuses);

        foreach (var pantheonBonuses in civ.EnumeratePantheonBonuses())
            Accumulate(pantheonBonuses?.unitBonuses);

        foreach (var belief in civ.EnumerateActiveBeliefs())
            if (civ.IsBeliefSeasonActive(belief, planetIndex))
                Accumulate(belief?.unitBonuses);

        AccumulateBuildingUnitBonuses(civ, BuildingUnitBonusScope.AllCivilizationUnits, Accumulate);

        var cityContext = GetCurrentCityContext();
        if (cityContext != null)
        {
            foreach (var (building, _, _) in cityContext.EnumerateOperationalBuildings())
                AccumulateBuildingUnitBonuses(building?.unitBonuses, BuildingUnitBonusScope.SameCity, Accumulate);
        }

        return a;
    }

    private static void AccumulateBuildingUnitBonuses(Civilization civ, BuildingUnitBonusScope scope, System.Action<UnitStatBonus[]> accumulate)
    {
        if (civ?.cities == null || accumulate == null)
            return;

        foreach (var city in civ.cities)
        {
            if (city == null)
                continue;

            foreach (var (building, _, _) in city.EnumerateOperationalBuildings())
                AccumulateBuildingUnitBonuses(building?.unitBonuses, scope, accumulate);
        }
    }

    private static void AccumulateBuildingUnitBonuses(UnitStatBonus[] bonuses, BuildingUnitBonusScope scope, System.Action<UnitStatBonus[]> accumulate)
    {
        if (bonuses == null || accumulate == null)
            return;

        var scopedBonuses = bonuses.Where(bonus => bonus != null && bonus.buildingScope == scope).ToArray();
        if (scopedBonuses.Length > 0)
            accumulate(scopedBonuses);
    }

    public override IEnumerable<UnitAuraBonus> EnumerateOwnedAuraBonuses()
    {
        foreach (var aura in base.EnumerateOwnedAuraBonuses())
            yield return aura;
        if (data?.auraBonuses != null)
            foreach (var aura in data.auraBonuses)
                if (aura != null) yield return aura;
        if (owner?.civData?.auraBonuses != null)
            foreach (var aura in owner.civData.auraBonuses)
                if (aura != null) yield return aura;
        if (owner?.civData?.effects?.auraBonuses != null)
            foreach (var aura in owner.civData.effects.auraBonuses)
                if (aura != null) yield return aura;
    }

    public override int GetSituationalAttackAddAgainst(BaseUnit target)
    {
        if (owner == null || data == null || target == null) return 0;
        var unitBonuses = AggregateTargetedCombatBonuses(owner, data, target);
        float total = unitBonuses.attackAdd + GetTargetedAbilityAttackModifierAgainst(target);
        return Mathf.RoundToInt(total);
    }

    public override float GetSituationalAttackPctAgainst(BaseUnit target)
    {
        if (owner == null || data == null || target == null) return 0f;
        var unitBonuses = AggregateTargetedCombatBonuses(owner, data, target);
        return unitBonuses.attackPct;
    }

    public int GetSituationalAttackAddAgainst(BaseUnit target, AttackType attackType)
    {
        if (owner == null || data == null || target == null) return 0;
        var unitBonuses = AggregateTargetedCombatBonuses(owner, data, target);
        float total = unitBonuses.attackAdd + GetTargetedAbilityAttackModifierAgainst(target);
        switch (attackType)
        {
            case AttackType.Melee:
                total += unitBonuses.meleeAttackAdd;
                break;
            case AttackType.Ranged:
                total += unitBonuses.rangedAttackAdd;
                break;
            case AttackType.City:
                total += unitBonuses.cityAttackAdd;
                break;
        }
        return Mathf.RoundToInt(total);
    }

    public float GetSituationalAttackPctAgainst(BaseUnit target, AttackType attackType)
    {
        if (owner == null || data == null || target == null) return 0f;
        var unitBonuses = AggregateTargetedCombatBonuses(owner, data, target);
        float total = unitBonuses.attackPct;
        switch (attackType)
        {
            case AttackType.Melee:
                total += unitBonuses.meleeAttackPct;
                break;
            case AttackType.Ranged:
                total += unitBonuses.rangedAttackPct;
                break;
            case AttackType.City:
                total += unitBonuses.cityAttackPct;
                break;
        }
        return total;
    }

    public int GetSituationalAttackAddAgainst(BaseUnit target, CombatTargetDomain targetDomain, AttackType legacyFallbackType, bool includeLegacyTypedBonuses)
    {
        if (owner == null || data == null || target == null) return 0;
        var unitBonuses = AggregateTargetedCombatBonuses(owner, data, target);
        float total = unitBonuses.attackAdd + GetTargetedAbilityAttackModifierAgainst(target);
        CombatTargetDomain bonusDomain = GetAttackDomainForTarget(targetDomain);
        float scratchPct = 0f;
        AddDomainBonuses(unitBonuses, bonusDomain, ref total, ref scratchPct);
        if (includeLegacyTypedBonuses)
        {
            scratchPct = 0f;
            AddLegacyTypedUnitBonuses(unitBonuses, legacyFallbackType, ref total, ref scratchPct);
        }
        return Mathf.RoundToInt(total);
    }

    public float GetSituationalAttackPctAgainst(BaseUnit target, CombatTargetDomain targetDomain, AttackType legacyFallbackType, bool includeLegacyTypedBonuses)
    {
        if (owner == null || data == null || target == null) return 0f;
        var unitBonuses = AggregateTargetedCombatBonuses(owner, data, target);
        float total = unitBonuses.attackPct;
        CombatTargetDomain bonusDomain = GetAttackDomainForTarget(targetDomain);
        float scratchAdd = 0f;
        AddDomainBonuses(unitBonuses, bonusDomain, ref scratchAdd, ref total);
        if (includeLegacyTypedBonuses)
        {
            scratchAdd = 0f;
            AddLegacyTypedUnitBonuses(unitBonuses, legacyFallbackType, ref scratchAdd, ref total);
        }
        return total;
    }

    public override int GetSituationalDefenseAddAgainst(BaseUnit attacker)
    {
        if (owner == null || data == null || attacker == null) return 0;
        var unitBonuses = AggregateTargetedCombatBonuses(owner, data, attacker);
        var equipmentBonuses = AggregateAllEquippedTargetedBonusesLocal(owner, attacker);
        var directEquipmentBonuses = AggregateEquippedItemTargetedModifiers(attacker);
        float total = unitBonuses.defenseAdd + equipmentBonuses.defenseAdd + directEquipmentBonuses.defenseAdd + GetTargetedAbilityDefenseModifierAgainst(attacker);
        return Mathf.RoundToInt(total);
    }

    public override float GetSituationalDefensePctAgainst(BaseUnit attacker)
    {
        if (owner == null || data == null || attacker == null) return 0f;
        var unitBonuses = AggregateTargetedCombatBonuses(owner, data, attacker);
        var equipmentBonuses = AggregateAllEquippedTargetedBonusesLocal(owner, attacker);
        var directEquipmentBonuses = AggregateEquippedItemTargetedModifiers(attacker);
        return unitBonuses.defensePct + equipmentBonuses.defensePct + directEquipmentBonuses.defensePct;
    }

    private EquipAgg AggregateEquipBonusesLocal(Civilization civ, EquipmentData eq)
    {
        EquipAgg a = new EquipAgg(); if (eq == null) return a;
        void AddEquipmentStatBonus(EquipmentStatBonus b)
        {
            if (b == null || !MatchesEquipmentBonusLocation(b) || Civilization.HasCombatBonusOpponentFilter(b.targetUnit, b.targetWorker, b.useTargetUnitCategoryFilter))
                return;
            a.attackAdd += b.attackAdd; a.meleeAttackAdd += b.meleeAttackAdd; a.rangedAttackAdd += b.rangedAttackAdd; a.cityAttackAdd += b.cityAttackAdd; a.groundAttackAdd += b.groundAttackAdd; a.underwaterAttackAdd += b.underwaterAttackAdd; a.airAttackAdd += b.airAttackAdd; a.spaceAttackAdd += b.spaceAttackAdd; a.defenseAdd += b.defenseAdd; a.healthAdd += b.healthAdd;
            a.rangeAdd += b.rangeAdd;
            a.attackPct += b.attackPct; a.meleeAttackPct += b.meleeAttackPct; a.rangedAttackPct += b.rangedAttackPct; a.cityAttackPct += b.cityAttackPct; a.groundAttackPct += b.groundAttackPct; a.underwaterAttackPct += b.underwaterAttackPct; a.airAttackPct += b.airAttackPct; a.spaceAttackPct += b.spaceAttackPct; a.defensePct += b.defensePct; a.healthPct += b.healthPct;
            a.rangePct += b.rangePct;
        }
        if (eq.conditionalStatBonuses != null)
            foreach (var b in eq.conditionalStatBonuses)
                AddEquipmentStatBonus(b);
        if (civ == null) return a;
        void ScanCivEquipmentBonuses(EquipmentStatBonus[] bonuses)
        {
            if (bonuses == null) return;
            foreach (var b in bonuses)
                if (b != null && b.equipment == eq && MatchesEquipmentBonusLocation(b))
                {
                    if (!Civilization.HasCombatBonusOpponentFilter(b.targetUnit, b.targetWorker, b.useTargetUnitCategoryFilter))
                    {
                        a.attackAdd += b.attackAdd; a.meleeAttackAdd += b.meleeAttackAdd; a.rangedAttackAdd += b.rangedAttackAdd; a.cityAttackAdd += b.cityAttackAdd; a.groundAttackAdd += b.groundAttackAdd; a.underwaterAttackAdd += b.underwaterAttackAdd; a.airAttackAdd += b.airAttackAdd; a.spaceAttackAdd += b.spaceAttackAdd; a.defenseAdd += b.defenseAdd;
                        a.attackPct += b.attackPct; a.meleeAttackPct += b.meleeAttackPct; a.rangedAttackPct += b.rangedAttackPct; a.cityAttackPct += b.cityAttackPct; a.groundAttackPct += b.groundAttackPct; a.underwaterAttackPct += b.underwaterAttackPct; a.airAttackPct += b.airAttackPct; a.spaceAttackPct += b.spaceAttackPct; a.defensePct += b.defensePct;
                    }
                    a.healthAdd += b.healthAdd;
                    a.rangeAdd += b.rangeAdd;
                    a.healthPct += b.healthPct;
                    a.rangePct += b.rangePct;
                }
        }
        ScanCivEquipmentBonuses(civ.civData?.equipmentBonuses);
        ScanCivEquipmentBonuses(civ.civData?.effects?.equipmentBonuses);
        if (civ.researchedTechs != null)
            foreach (var t in civ.researchedTechs)
            {
                if (t?.equipmentBonuses == null) continue;
                foreach (var b in t.equipmentBonuses)
                    if (b != null && b.equipment == eq && MatchesEquipmentBonusLocation(b))
                    {
                        if (!Civilization.HasCombatBonusOpponentFilter(b.targetUnit, b.targetWorker, b.useTargetUnitCategoryFilter))
                        {
                            a.attackAdd += b.attackAdd; a.meleeAttackAdd += b.meleeAttackAdd; a.rangedAttackAdd += b.rangedAttackAdd; a.cityAttackAdd += b.cityAttackAdd; a.groundAttackAdd += b.groundAttackAdd; a.underwaterAttackAdd += b.underwaterAttackAdd; a.airAttackAdd += b.airAttackAdd; a.spaceAttackAdd += b.spaceAttackAdd; a.defenseAdd += b.defenseAdd;
                            a.attackPct += b.attackPct; a.meleeAttackPct += b.meleeAttackPct; a.rangedAttackPct += b.rangedAttackPct; a.cityAttackPct += b.cityAttackPct; a.groundAttackPct += b.groundAttackPct; a.underwaterAttackPct += b.underwaterAttackPct; a.airAttackPct += b.airAttackPct; a.spaceAttackPct += b.spaceAttackPct; a.defensePct += b.defensePct;
                        }
                        a.healthAdd += b.healthAdd;
                        a.rangeAdd += b.rangeAdd;
                        a.healthPct += b.healthPct;
                        a.rangePct += b.rangePct;
                    }
            }
        if (civ.researchedCultures != null)
            foreach (var c in civ.researchedCultures)
            {
                if (c?.equipmentBonuses == null) continue;
                foreach (var b in c.equipmentBonuses)
                    if (b != null && b.equipment == eq && MatchesEquipmentBonusLocation(b))
                    {
                        if (!Civilization.HasCombatBonusOpponentFilter(b.targetUnit, b.targetWorker, b.useTargetUnitCategoryFilter))
                        {
                            a.attackAdd += b.attackAdd; a.meleeAttackAdd += b.meleeAttackAdd; a.rangedAttackAdd += b.rangedAttackAdd; a.cityAttackAdd += b.cityAttackAdd; a.groundAttackAdd += b.groundAttackAdd; a.underwaterAttackAdd += b.underwaterAttackAdd; a.airAttackAdd += b.airAttackAdd; a.spaceAttackAdd += b.spaceAttackAdd; a.defenseAdd += b.defenseAdd;
                            a.attackPct += b.attackPct; a.meleeAttackPct += b.meleeAttackPct; a.rangedAttackPct += b.rangedAttackPct; a.cityAttackPct += b.cityAttackPct; a.groundAttackPct += b.groundAttackPct; a.underwaterAttackPct += b.underwaterAttackPct; a.airAttackPct += b.airAttackPct; a.spaceAttackPct += b.spaceAttackPct; a.defensePct += b.defensePct;
                        }
                        a.healthAdd += b.healthAdd;
                        a.rangeAdd += b.rangeAdd;
                        a.healthPct += b.healthPct;
                        a.rangePct += b.rangePct;
                    }
            }
        return a;
    }

    private EquipAgg AggregateTargetedEquipBonuses(Civilization civ, EquipmentData eq, BaseUnit opponent)
    {
        EquipAgg a = new EquipAgg(); if (civ == null || eq == null || opponent == null) return a;
        if (eq.conditionalStatBonuses != null)
            foreach (var b in eq.conditionalStatBonuses)
                if (b != null
                    && MatchesEquipmentBonusLocation(b)
                    && Civilization.HasCombatBonusOpponentFilter(b.targetUnit, b.targetWorker, b.useTargetUnitCategoryFilter)
                    && Civilization.MatchesCombatBonusOpponent(opponent, b.targetUnit, b.targetWorker, b.useTargetUnitCategoryFilter, b.targetUnitCategory))
                {
                    a.attackAdd += b.attackAdd; a.meleeAttackAdd += b.meleeAttackAdd; a.rangedAttackAdd += b.rangedAttackAdd; a.cityAttackAdd += b.cityAttackAdd; a.groundAttackAdd += b.groundAttackAdd; a.underwaterAttackAdd += b.underwaterAttackAdd; a.airAttackAdd += b.airAttackAdd; a.spaceAttackAdd += b.spaceAttackAdd; a.defenseAdd += b.defenseAdd;
                    a.attackPct += b.attackPct; a.meleeAttackPct += b.meleeAttackPct; a.rangedAttackPct += b.rangedAttackPct; a.cityAttackPct += b.cityAttackPct; a.groundAttackPct += b.groundAttackPct; a.underwaterAttackPct += b.underwaterAttackPct; a.airAttackPct += b.airAttackPct; a.spaceAttackPct += b.spaceAttackPct; a.defensePct += b.defensePct;
                }
        void ScanCivTargetedEquipmentBonuses(EquipmentStatBonus[] bonuses)
        {
            if (bonuses == null) return;
            foreach (var b in bonuses)
                if (b != null && b.equipment == eq && MatchesEquipmentBonusLocation(b)
                    && Civilization.HasCombatBonusOpponentFilter(b.targetUnit, b.targetWorker, b.useTargetUnitCategoryFilter)
                    && Civilization.MatchesCombatBonusOpponent(opponent, b.targetUnit, b.targetWorker, b.useTargetUnitCategoryFilter, b.targetUnitCategory))
                {
                    a.attackAdd += b.attackAdd; a.meleeAttackAdd += b.meleeAttackAdd; a.rangedAttackAdd += b.rangedAttackAdd; a.cityAttackAdd += b.cityAttackAdd; a.groundAttackAdd += b.groundAttackAdd; a.underwaterAttackAdd += b.underwaterAttackAdd; a.airAttackAdd += b.airAttackAdd; a.spaceAttackAdd += b.spaceAttackAdd; a.defenseAdd += b.defenseAdd;
                    a.attackPct += b.attackPct; a.meleeAttackPct += b.meleeAttackPct; a.rangedAttackPct += b.rangedAttackPct; a.cityAttackPct += b.cityAttackPct; a.groundAttackPct += b.groundAttackPct; a.underwaterAttackPct += b.underwaterAttackPct; a.airAttackPct += b.airAttackPct; a.spaceAttackPct += b.spaceAttackPct; a.defensePct += b.defensePct;
                    a.healthAdd += b.healthAdd; a.rangeAdd += b.rangeAdd; a.healthPct += b.healthPct; a.rangePct += b.rangePct;
                }
        }
        ScanCivTargetedEquipmentBonuses(civ.civData?.equipmentBonuses);
        ScanCivTargetedEquipmentBonuses(civ.civData?.effects?.equipmentBonuses);
        if (civ.researchedTechs != null)
            foreach (var t in civ.researchedTechs)
            {
                if (t?.equipmentBonuses == null) continue;
                foreach (var b in t.equipmentBonuses)
                    if (b != null && b.equipment == eq && MatchesEquipmentBonusLocation(b)
                        && Civilization.HasCombatBonusOpponentFilter(b.targetUnit, b.targetWorker, b.useTargetUnitCategoryFilter)
                        && Civilization.MatchesCombatBonusOpponent(opponent, b.targetUnit, b.targetWorker, b.useTargetUnitCategoryFilter, b.targetUnitCategory))
                    {
                        a.attackAdd += b.attackAdd; a.meleeAttackAdd += b.meleeAttackAdd; a.rangedAttackAdd += b.rangedAttackAdd; a.cityAttackAdd += b.cityAttackAdd; a.groundAttackAdd += b.groundAttackAdd; a.underwaterAttackAdd += b.underwaterAttackAdd; a.airAttackAdd += b.airAttackAdd; a.spaceAttackAdd += b.spaceAttackAdd; a.defenseAdd += b.defenseAdd;
                        a.attackPct += b.attackPct; a.meleeAttackPct += b.meleeAttackPct; a.rangedAttackPct += b.rangedAttackPct; a.cityAttackPct += b.cityAttackPct; a.groundAttackPct += b.groundAttackPct; a.underwaterAttackPct += b.underwaterAttackPct; a.airAttackPct += b.airAttackPct; a.spaceAttackPct += b.spaceAttackPct; a.defensePct += b.defensePct;
                    }
            }
        if (civ.researchedCultures != null)
            foreach (var c in civ.researchedCultures)
            {
                if (c?.equipmentBonuses == null) continue;
                foreach (var b in c.equipmentBonuses)
                    if (b != null && b.equipment == eq && MatchesEquipmentBonusLocation(b)
                        && Civilization.HasCombatBonusOpponentFilter(b.targetUnit, b.targetWorker, b.useTargetUnitCategoryFilter)
                        && Civilization.MatchesCombatBonusOpponent(opponent, b.targetUnit, b.targetWorker, b.useTargetUnitCategoryFilter, b.targetUnitCategory))
                    {
                        a.attackAdd += b.attackAdd; a.meleeAttackAdd += b.meleeAttackAdd; a.rangedAttackAdd += b.rangedAttackAdd; a.cityAttackAdd += b.cityAttackAdd; a.groundAttackAdd += b.groundAttackAdd; a.underwaterAttackAdd += b.underwaterAttackAdd; a.airAttackAdd += b.airAttackAdd; a.spaceAttackAdd += b.spaceAttackAdd; a.defenseAdd += b.defenseAdd;
                        a.attackPct += b.attackPct; a.meleeAttackPct += b.meleeAttackPct; a.rangedAttackPct += b.rangedAttackPct; a.cityAttackPct += b.cityAttackPct; a.groundAttackPct += b.groundAttackPct; a.underwaterAttackPct += b.underwaterAttackPct; a.airAttackPct += b.airAttackPct; a.spaceAttackPct += b.spaceAttackPct; a.defensePct += b.defensePct;
                    }
            }
        return a;
    }
    
    // Sum equipment-targeted bonuses across all currently equipped items
    private EquipAgg AggregateAllEquippedBonusesLocal(Civilization civ)
    {
        EquipAgg total = new EquipAgg();
        EquipmentData[] items = { equippedWeapon, equippedShield, equippedArmor, equippedMiscellaneous };
        foreach (var it in items)
        {
            if (it == null) continue;
            var e = AggregateEquipBonusesLocal(civ, it);
            total.attackAdd += e.attackAdd; total.meleeAttackAdd += e.meleeAttackAdd; total.rangedAttackAdd += e.rangedAttackAdd; total.cityAttackAdd += e.cityAttackAdd; total.groundAttackAdd += e.groundAttackAdd; total.underwaterAttackAdd += e.underwaterAttackAdd; total.airAttackAdd += e.airAttackAdd; total.spaceAttackAdd += e.spaceAttackAdd; total.defenseAdd += e.defenseAdd; total.healthAdd += e.healthAdd;
            total.moveAdd += e.moveAdd; total.rangeAdd += e.rangeAdd; total.apAdd += e.apAdd;
            total.attackPct += e.attackPct; total.meleeAttackPct += e.meleeAttackPct; total.rangedAttackPct += e.rangedAttackPct; total.cityAttackPct += e.cityAttackPct; total.groundAttackPct += e.groundAttackPct; total.underwaterAttackPct += e.underwaterAttackPct; total.airAttackPct += e.airAttackPct; total.spaceAttackPct += e.spaceAttackPct; total.defensePct += e.defensePct; total.healthPct += e.healthPct;
            total.movePct += e.movePct; total.rangePct += e.rangePct; total.apPct += e.apPct;
        }
        return total;
    }

    // Sum equipment-targeted bonuses across all currently equipped items (opponent-specific)
    private EquipAgg AggregateAllEquippedTargetedBonusesLocal(Civilization civ, BaseUnit opponent)
    {
        EquipAgg total = new EquipAgg();
        if (civ == null || opponent == null) return total;
        EquipmentData[] items = { equippedWeapon, equippedShield, equippedArmor, equippedMiscellaneous };
        foreach (var it in items)
        {
            if (it == null) continue;
            var e = AggregateTargetedEquipBonuses(civ, it, opponent);
            total.attackAdd += e.attackAdd; total.meleeAttackAdd += e.meleeAttackAdd; total.rangedAttackAdd += e.rangedAttackAdd; total.cityAttackAdd += e.cityAttackAdd; total.groundAttackAdd += e.groundAttackAdd; total.underwaterAttackAdd += e.underwaterAttackAdd; total.airAttackAdd += e.airAttackAdd; total.spaceAttackAdd += e.spaceAttackAdd; total.defenseAdd += e.defenseAdd; total.healthAdd += e.healthAdd;
            total.moveAdd += e.moveAdd; total.rangeAdd += e.rangeAdd; total.apAdd += e.apAdd;
            total.attackPct += e.attackPct; total.meleeAttackPct += e.meleeAttackPct; total.rangedAttackPct += e.rangedAttackPct; total.cityAttackPct += e.cityAttackPct; total.groundAttackPct += e.groundAttackPct; total.underwaterAttackPct += e.underwaterAttackPct; total.airAttackPct += e.airAttackPct; total.spaceAttackPct += e.spaceAttackPct; total.defensePct += e.defensePct; total.healthPct += e.healthPct;
            total.movePct += e.movePct; total.rangePct += e.rangePct; total.apPct += e.apPct;
        }
        return total;
    }

    public enum AttackType { Generic, Melee, Ranged, City }

    private float ApplyTypeSpecificAttackBonuses(float valF, AttackType attackType)
    {
        if (data != null)
        {
            var u = AggregateUnitBonusesLocal(owner, data);
            float attackAdd = u.attackAdd;
            float attackPct = u.attackPct;
            switch (attackType)
            {
                case AttackType.Melee:
                    attackAdd += u.meleeAttackAdd;
                    attackPct += u.meleeAttackPct;
                    break;
                case AttackType.Ranged:
                    attackAdd += u.rangedAttackAdd;
                    attackPct += u.rangedAttackPct;
                    break;
                case AttackType.City:
                    attackAdd += u.cityAttackAdd;
                    attackPct += u.cityAttackPct;
                    break;
            }
            valF = (valF + attackAdd) * (1f + attackPct);
        }

        if (owner != null)
        {
            var e = AggregateAllEquippedBonusesLocal(owner);
            float attackAdd = e.attackAdd;
            float attackPct = e.attackPct;
            switch (attackType)
            {
                case AttackType.Melee:
                    attackAdd += e.meleeAttackAdd;
                    attackPct += e.meleeAttackPct;
                    break;
                case AttackType.Ranged:
                    attackAdd += e.rangedAttackAdd;
                    attackPct += e.rangedAttackPct;
                    break;
                case AttackType.City:
                    attackAdd += e.cityAttackAdd;
                    attackPct += e.cityAttackPct;
                    break;
            }
            valF = (valF + attackAdd) * (1f + attackPct);
        }

        var aura = AggregateIncomingAuraBonuses();
        float auraAttackAdd = aura.attackAdd;
        float auraAttackPct = aura.attackPct;
        AddLegacyTypedAuraBonuses(aura, attackType, ref auraAttackAdd, ref auraAttackPct);
        valF = (valF + auraAttackAdd) * (1f + auraAttackPct);

        return valF;
    }


    private static void AddLegacyTypedAuraBonuses(UnitAuraAgg bonuses, AttackType attackType, ref float attackAdd, ref float attackPct)
    {
        switch (attackType)
        {
            case AttackType.Melee:
                attackAdd += bonuses.meleeAttackAdd;
                attackPct += bonuses.meleeAttackPct;
                break;
            case AttackType.Ranged:
                attackAdd += bonuses.rangedAttackAdd;
                attackPct += bonuses.rangedAttackPct;
                break;
            case AttackType.City:
                attackAdd += bonuses.cityAttackAdd;
                attackPct += bonuses.cityAttackPct;
                break;
        }
    }

    private static void AddDomainAuraBonuses(UnitAuraAgg bonuses, CombatTargetDomain targetDomain, ref float attackAdd, ref float attackPct)
    {
        switch (targetDomain)
        {
            case CombatTargetDomain.Ground:
                attackAdd += bonuses.groundAttackAdd;
                attackPct += bonuses.groundAttackPct;
                break;
            case CombatTargetDomain.Underwater:
                attackAdd += bonuses.underwaterAttackAdd;
                attackPct += bonuses.underwaterAttackPct;
                break;
            case CombatTargetDomain.Air:
                attackAdd += bonuses.airAttackAdd;
                attackPct += bonuses.airAttackPct;
                break;
            case CombatTargetDomain.Space:
                attackAdd += bonuses.spaceAttackAdd;
                attackPct += bonuses.spaceAttackPct;
                break;
            case CombatTargetDomain.City:
                attackAdd += bonuses.cityAttackAdd;
                attackPct += bonuses.cityAttackPct;
                break;
        }
    }

    private float AddTypedEquipmentAttackBonus(float valF, AttackType attackType)
    {
        switch (attackType)
        {
            case AttackType.Melee:
                return valF + EquipmentMeleeAttackBonus;
            case AttackType.Ranged:
                return valF + EquipmentRangedAttackBonus;
            case AttackType.City:
                return valF + EquipmentCityAttackBonus;
            default:
                return valF;
        }
    }

    private float ApplyOwnerAttackBonuses(float valF, AttackType attackType)
    {
        if (owner == null)
            return valF;

        float attackPct = owner.attackBonus;
        switch (attackType)
        {
            case AttackType.Melee:
                attackPct += owner.meleeAttackBonus;
                break;
            case AttackType.Ranged:
                attackPct += owner.rangedAttackBonus;
                break;
            case AttackType.City:
                attackPct += owner.cityAttackBonus;
                break;
        }

        return valF * (1f + attackPct);
    }


    private float GetAttackTypeBaseValue(AttackType attackType)
    {
        return attackType switch
        {
            AttackType.Melee => BaseMeleeAttack,
            AttackType.Ranged => BaseRangedAttack,
            AttackType.City => BaseCityAttack,
            _ => BaseAttack,
        };
    }

    private float GetDomainBaseAttackValue(CombatTargetDomain targetDomain)
    {
        return targetDomain switch
        {
            CombatTargetDomain.Ground => BaseGroundAttack,
            CombatTargetDomain.Underwater => BaseUnderwaterAttack,
            CombatTargetDomain.Air => BaseAirAttack,
            CombatTargetDomain.Space => BaseSpaceAttack,
            CombatTargetDomain.City => BaseCityAttack,
            _ => BaseAttack,
        };
    }

    private float AddDomainEquipmentAttackBonus(float valF, CombatTargetDomain targetDomain)
    {
        return targetDomain switch
        {
            CombatTargetDomain.Ground => valF + EquipmentGroundAttackBonus,
            CombatTargetDomain.Underwater => valF + EquipmentUnderwaterAttackBonus,
            CombatTargetDomain.Air => valF + EquipmentAirAttackBonus,
            CombatTargetDomain.Space => valF + EquipmentSpaceAttackBonus,
            CombatTargetDomain.City => valF + EquipmentCityAttackBonus,
            _ => valF,
        };
    }

    private void AddDomainBonuses(UnitAgg bonuses, CombatTargetDomain targetDomain, ref float attackAdd, ref float attackPct)
    {
        switch (targetDomain)
        {
            case CombatTargetDomain.Ground:
                attackAdd += bonuses.groundAttackAdd;
                attackPct += bonuses.groundAttackPct;
                break;
            case CombatTargetDomain.Underwater:
                attackAdd += bonuses.underwaterAttackAdd;
                attackPct += bonuses.underwaterAttackPct;
                break;
            case CombatTargetDomain.Air:
                attackAdd += bonuses.airAttackAdd;
                attackPct += bonuses.airAttackPct;
                break;
            case CombatTargetDomain.Space:
                attackAdd += bonuses.spaceAttackAdd;
                attackPct += bonuses.spaceAttackPct;
                break;
            case CombatTargetDomain.City:
                attackAdd += bonuses.cityAttackAdd;
                attackPct += bonuses.cityAttackPct;
                break;
        }
    }

    private void AddDomainBonuses(EquipAgg bonuses, CombatTargetDomain targetDomain, ref float attackAdd, ref float attackPct)
    {
        switch (targetDomain)
        {
            case CombatTargetDomain.Ground:
                attackAdd += bonuses.groundAttackAdd;
                attackPct += bonuses.groundAttackPct;
                break;
            case CombatTargetDomain.Underwater:
                attackAdd += bonuses.underwaterAttackAdd;
                attackPct += bonuses.underwaterAttackPct;
                break;
            case CombatTargetDomain.Air:
                attackAdd += bonuses.airAttackAdd;
                attackPct += bonuses.airAttackPct;
                break;
            case CombatTargetDomain.Space:
                attackAdd += bonuses.spaceAttackAdd;
                attackPct += bonuses.spaceAttackPct;
                break;
            case CombatTargetDomain.City:
                attackAdd += bonuses.cityAttackAdd;
                attackPct += bonuses.cityAttackPct;
                break;
        }
    }

    private float ApplyTargetDomainAttackBonuses(float valF, CombatTargetDomain targetDomain, AttackType legacyFallbackType, bool includeLegacyTypedBonuses)
    {
        if (data != null)
        {
            var u = AggregateUnitBonusesLocal(owner, data);
            CombatTargetDomain bonusDomain = GetAttackDomainForTarget(targetDomain);
            float attackAdd = u.attackAdd;
            float attackPct = u.attackPct;
            AddDomainBonuses(u, bonusDomain, ref attackAdd, ref attackPct);
            if (includeLegacyTypedBonuses)
                AddLegacyTypedUnitBonuses(u, legacyFallbackType, ref attackAdd, ref attackPct);
            valF = (valF + attackAdd) * (1f + attackPct);
        }

        if (owner != null)
        {
            var e = AggregateAllEquippedBonusesLocal(owner);
            CombatTargetDomain bonusDomain = GetAttackDomainForTarget(targetDomain);
            float attackAdd = e.attackAdd;
            float attackPct = e.attackPct;
            AddDomainBonuses(e, bonusDomain, ref attackAdd, ref attackPct);
            if (includeLegacyTypedBonuses)
                AddLegacyTypedEquipBonuses(e, legacyFallbackType, ref attackAdd, ref attackPct);
            valF = (valF + attackAdd) * (1f + attackPct);
        }

        var aura = AggregateIncomingAuraBonuses();
        CombatTargetDomain auraDomain = GetAttackDomainForTarget(targetDomain);
        float auraAttackAdd = aura.attackAdd;
        float auraAttackPct = aura.attackPct;
        AddDomainAuraBonuses(aura, auraDomain, ref auraAttackAdd, ref auraAttackPct);
        if (includeLegacyTypedBonuses)
            AddLegacyTypedAuraBonuses(aura, legacyFallbackType, ref auraAttackAdd, ref auraAttackPct);
        valF = (valF + auraAttackAdd) * (1f + auraAttackPct);

        return valF;
    }

    private void AddLegacyTypedUnitBonuses(UnitAgg bonuses, AttackType attackType, ref float attackAdd, ref float attackPct)
    {
        switch (attackType)
        {
            case AttackType.Melee:
                attackAdd += bonuses.meleeAttackAdd;
                attackPct += bonuses.meleeAttackPct;
                break;
            case AttackType.Ranged:
                attackAdd += bonuses.rangedAttackAdd;
                attackPct += bonuses.rangedAttackPct;
                break;
            case AttackType.City:
                attackAdd += bonuses.cityAttackAdd;
                attackPct += bonuses.cityAttackPct;
                break;
        }
    }

    private void AddLegacyTypedEquipBonuses(EquipAgg bonuses, AttackType attackType, ref float attackAdd, ref float attackPct)
    {
        switch (attackType)
        {
            case AttackType.Melee:
                attackAdd += bonuses.meleeAttackAdd;
                attackPct += bonuses.meleeAttackPct;
                break;
            case AttackType.Ranged:
                attackAdd += bonuses.rangedAttackAdd;
                attackPct += bonuses.rangedAttackPct;
                break;
            case AttackType.City:
                attackAdd += bonuses.cityAttackAdd;
                attackPct += bonuses.cityAttackPct;
                break;
        }
    }

    private float GetBaseAttackFloat(AttackType attackType)
    {
        float baseValue = GetAttackTypeBaseValue(attackType);

        float valF = baseValue + EquipmentAttackBonus + GetAbilityAttackModifier();
        valF = AddTypedEquipmentAttackBonus(valF, attackType);
        return valF;
    }

    private bool UsesGroundAttackAgainstSurfaceNavalTarget()
    {
        return data != null && (CombatUnitData.IsAirCategory(data.unitType) || currentLayer == TileLayer.Atmosphere);
    }

    private CombatTargetDomain GetAttackDomainForTarget(CombatTargetDomain targetDomain)
    {
        if (targetDomain == CombatTargetDomain.NavalSurface && UsesGroundAttackAgainstSurfaceNavalTarget())
            return CombatTargetDomain.Ground;
        return targetDomain;
    }

    private float GetBaseAttackFloat(CombatTargetDomain targetDomain, AttackType legacyFallbackType, out bool usingLegacyTypedFallback)
    {
        if (targetDomain == CombatTargetDomain.NavalSurface && !UsesGroundAttackAgainstSurfaceNavalTarget())
        {
            usingLegacyTypedFallback = true;
            float surfaceVal = GetAttackTypeBaseValue(legacyFallbackType) + EquipmentAttackBonus + GetAbilityAttackModifier();
            surfaceVal = AddTypedEquipmentAttackBonus(surfaceVal, legacyFallbackType);
            return surfaceVal;
        }

        targetDomain = GetAttackDomainForTarget(targetDomain);

        float domainBaseValue = GetDomainBaseAttackValue(targetDomain);
        usingLegacyTypedFallback = domainBaseValue <= 0f && targetDomain != CombatTargetDomain.City;
        float baseValue = usingLegacyTypedFallback ? GetAttackTypeBaseValue(legacyFallbackType) : domainBaseValue;

        float valF = baseValue + EquipmentAttackBonus + GetAbilityAttackModifier();
        valF = AddDomainEquipmentAttackBonus(valF, targetDomain);
        if (usingLegacyTypedFallback)
            valF = AddTypedEquipmentAttackBonus(valF, legacyFallbackType);
        return valF;
    }

    private float GetUnitBaseAttackFloat(CombatTargetDomain targetDomain, AttackType legacyFallbackType, out bool usingLegacyTypedFallback)
    {
        if (targetDomain == CombatTargetDomain.NavalSurface && !UsesGroundAttackAgainstSurfaceNavalTarget())
        {
            usingLegacyTypedFallback = true;
            return GetAttackTypeBaseValue(legacyFallbackType) + GetAbilityAttackModifier();
        }

        targetDomain = GetAttackDomainForTarget(targetDomain);
        float domainBaseValue = GetDomainBaseAttackValue(targetDomain);
        usingLegacyTypedFallback = domainBaseValue <= 0f && targetDomain != CombatTargetDomain.City;
        return (usingLegacyTypedFallback ? GetAttackTypeBaseValue(legacyFallbackType) : domainBaseValue)
            + GetAbilityAttackModifier();
    }

    private float GetActiveWeaponBaseAttackContribution(EquipmentData activeWeapon, CombatTargetDomain targetDomain, AttackType legacyFallbackType, bool usingLegacyTypedFallback)
    {
        if (activeWeapon == null)
            return 0f;

        float contribution = activeWeapon.attackBonus;
        if (targetDomain == CombatTargetDomain.NavalSurface && !UsesGroundAttackAgainstSurfaceNavalTarget())
            return contribution + GetTypedWeaponAttackBonus(activeWeapon, legacyFallbackType);

        targetDomain = GetAttackDomainForTarget(targetDomain);
        contribution += GetDomainWeaponAttackBonus(activeWeapon, targetDomain);
        if (usingLegacyTypedFallback)
            contribution += GetTypedWeaponAttackBonus(activeWeapon, legacyFallbackType);
        return contribution;
    }

    private static float GetTypedWeaponAttackBonus(EquipmentData weapon, AttackType attackType)
    {
        return attackType switch
        {
            AttackType.Melee => weapon.meleeAttackBonus,
            AttackType.Ranged => weapon.rangedAttackBonus,
            AttackType.City => weapon.cityAttackBonus,
            _ => 0f,
        };
    }

    private static float GetDomainWeaponAttackBonus(EquipmentData weapon, CombatTargetDomain targetDomain)
    {
        return targetDomain switch
        {
            CombatTargetDomain.Ground => weapon.groundAttackBonus,
            CombatTargetDomain.Underwater => weapon.underwaterAttackBonus,
            CombatTargetDomain.Air => weapon.airAttackBonus,
            CombatTargetDomain.Space => weapon.spaceAttackBonus,
            _ => 0f,
        };
    }

    private void GetWeaponTargetedAttackBonusesAgainst(EquipmentData activeWeapon, BaseUnit target, CombatTargetDomain targetDomain,
        AttackType legacyFallbackType, bool includeLegacyTypedBonuses, out float attackAdd, out float attackPct)
    {
        attackAdd = 0f;
        attackPct = 0f;
        if (activeWeapon == null || owner == null || target == null)
            return;

        EquipAgg equipmentBonuses = AggregateTargetedEquipBonuses(owner, activeWeapon, target);
        attackAdd = equipmentBonuses.attackAdd;
        attackPct = equipmentBonuses.attackPct;

        CombatTargetDomain bonusDomain = GetAttackDomainForTarget(targetDomain);
        AddDomainBonuses(equipmentBonuses, bonusDomain, ref attackAdd, ref attackPct);
        if (includeLegacyTypedBonuses)
            AddLegacyTypedEquipBonuses(equipmentBonuses, legacyFallbackType, ref attackAdd, ref attackPct);

        if (target is CombatUnit combatTarget && combatTarget.data != null && activeWeapon.attackBonusAgainst != null)
        {
            foreach (var entry in activeWeapon.attackBonusAgainst)
                if (entry.unitType == combatTarget.data.unitType)
                    attackAdd += entry.value;
        }

        if (activeWeapon.combatModifiersAgainst == null)
            return;

        foreach (var modifier in activeWeapon.combatModifiersAgainst)
        {
            if (!Civilization.MatchesCombatBonusOpponent(target, modifier.targetUnit, modifier.targetWorker,
                modifier.useTargetUnitCategoryFilter, modifier.targetUnitCategory))
                continue;
            attackAdd += modifier.attackAdd;
            attackPct += modifier.attackPct;
        }
    }

    private float GetTargetedAttackValue(EquipmentData activeWeapon, BaseUnit target, CombatTargetDomain targetDomain,
        AttackType attackType, float unitBaseAdd = 0f)
    {
        float unitBase = GetUnitBaseAttackFloat(targetDomain, attackType, out bool usingLegacyTypedFallback) + unitBaseAdd;
        float unitAttack = (unitBase + GetSituationalAttackAddAgainst(target, targetDomain, attackType, usingLegacyTypedFallback))
            * (1f + GetSituationalAttackPctAgainst(target, targetDomain, attackType, usingLegacyTypedFallback));

        float weaponBase = GetActiveWeaponBaseAttackContribution(activeWeapon, targetDomain, attackType, usingLegacyTypedFallback);
        GetWeaponTargetedAttackBonusesAgainst(activeWeapon, target, targetDomain, attackType, usingLegacyTypedFallback,
            out float weaponAttackAdd, out float weaponAttackPct);
        float weaponAttack = (weaponBase + weaponAttackAdd) * (1f + weaponAttackPct);
        return unitAttack + weaponAttack;
    }

    public override int CurrentMeleeAttack
    {
        get
        {
            float valF = GetBaseAttackFloat(AttackType.Melee);
            valF = ApplyTypeSpecificAttackBonuses(valF, AttackType.Melee);
            valF = ApplyOwnerAttackBonuses(valF, AttackType.Melee);
            valF *= FatigueMultiplier;
            valF = ApplyResourceUpkeepToStat(valF);
            valF = ApplyGoldMaintenanceToCombatStat(valF);
            return Mathf.RoundToInt(valF);
        }
    }

    public override int CurrentRangedAttack
    {
        get
        {
            float valF = GetBaseAttackFloat(AttackType.Ranged);
            valF = ApplyTypeSpecificAttackBonuses(valF, AttackType.Ranged);
            valF = ApplyOwnerAttackBonuses(valF, AttackType.Ranged);
            valF *= FatigueMultiplier;
            valF = ApplyResourceUpkeepToStat(valF);
            valF = ApplyGoldMaintenanceToCombatStat(valF);
            return Mathf.RoundToInt(valF);
        }
    }

    public override int CurrentCityAttack
    {
        get
        {
            float valF = GetBaseAttackFloat(AttackType.City);
            valF = ApplyTypeSpecificAttackBonuses(valF, AttackType.City);
            valF = ApplyOwnerAttackBonuses(valF, AttackType.City);
            valF *= FatigueMultiplier;
            valF = ApplyResourceUpkeepToStat(valF);
            valF = ApplyGoldMaintenanceToCombatStat(valF);
            return Mathf.RoundToInt(valF);
        }
    }

    private int GetCurrentTargetDomainAttack(CombatTargetDomain targetDomain)
    {
        float valF = GetBaseAttackFloat(targetDomain, AttackType.Generic, out bool usingLegacyTypedFallback);
        valF = ApplyTargetDomainAttackBonuses(valF, targetDomain, AttackType.Generic, usingLegacyTypedFallback);
        valF = ApplyOwnerAttackBonuses(valF, AttackType.Generic);
        valF *= FatigueMultiplier;
        valF = ApplyResourceUpkeepToStat(valF);
        valF = ApplyGoldMaintenanceToCombatStat(valF);
        return Mathf.RoundToInt(valF);
    }

    public override int CurrentGroundAttack => GetCurrentTargetDomainAttack(CombatTargetDomain.Ground);
    public override int CurrentUnderwaterAttack => GetCurrentTargetDomainAttack(CombatTargetDomain.Underwater);
    public override int CurrentAirAttack => GetCurrentTargetDomainAttack(CombatTargetDomain.Air);
    public override int CurrentSpaceAttack => GetCurrentTargetDomainAttack(CombatTargetDomain.Space);

    public override int CurrentAttack
    {
        get
        {
            // Use floats internally for accuracy, then round when returning an int for gameplay values that expect ints.
            float valF = BaseAttack + EquipmentAttackBonus + GetAbilityAttackModifier();
            if (data != null)
            {
                var u = AggregateUnitBonusesLocal(owner, data);
                valF = (valF + u.attackAdd) * (1f + u.attackPct);
            }
            if (owner != null)
            {
                var e = AggregateAllEquippedBonusesLocal(owner);
                valF = (valF + e.attackAdd) * (1f + e.attackPct);
            }
            var aura = AggregateIncomingAuraBonuses();
            valF = (valF + aura.attackAdd) * (1f + aura.attackPct);

            valF = ApplyOwnerAttackBonuses(valF, AttackType.Generic);

            // Fatigue scaling
            valF *= FatigueMultiplier;
            valF = ApplyResourceUpkeepToStat(valF);
            
            // Apply per-target bonuses (if this unit is attacking a specific target, callers may need to apply extra modifiers).
            valF = ApplyGoldMaintenanceToCombatStat(valF);
            return Mathf.RoundToInt(valF);
        }
    }
    public override int CurrentDefense
    {
        get
        {
            return Mathf.RoundToInt(ApplyGoldMaintenanceToCombatStat(GetCurrentDefenseValueFloat()));
        }
    }

    protected override float ApplyOwnerDefenseBonuses(float defenseValue)
    {
        float valF = defenseValue;
        if (data != null)
        {
            var u = AggregateUnitBonusesLocal(owner, data);
            valF = (valF + u.defenseAdd) * (1f + u.defensePct);
        }
        if (owner != null)
        {
            var e = AggregateAllEquippedBonusesLocal(owner);
            valF = (valF + e.defenseAdd) * (1f + e.defensePct);
        }
        var aura = AggregateIncomingAuraBonuses();
        valF = (valF + aura.defenseAdd) * (1f + aura.defensePct);
        return valF;
    }


    public override int GetStartingMovePoints()
    {
        int baseMove = data != null ? data.baseMovePoints : 0;
        if (baseMove <= 0 && data != null)
            baseMove = data.animalMovePoints;

        float move = baseMove;
        if (data != null)
        {
            var u = AggregateUnitBonusesLocal(owner, data);
            move = (move + u.moveAdd) * (1f + u.movePct);
        }
        if (owner != null)
        {
            var e = AggregateAllEquippedBonusesLocal(owner);
            move = (move + e.moveAdd) * (1f + e.movePct);
        }

        return Mathf.Max(0, Mathf.RoundToInt(move));
    }

    public override int MaxHealth
    {
        get
        {
            float valF = BaseHealth + EquipmentHealthBonus + GetAbilityHealthModifier();
            if (data != null)
            {
                var u = AggregateUnitBonusesLocal(owner, data);
                valF = (valF + u.healthAdd) * (1f + u.healthPct);
            }
            if (owner != null)
            {
                var e = AggregateAllEquippedBonusesLocal(owner);
                valF = (valF + e.healthAdd) * (1f + e.healthPct);
            }
            var aura = AggregateIncomingAuraBonuses();
            valF = (valF + aura.healthAdd) * (1f + aura.healthPct);
            return Mathf.RoundToInt(valF);
        }
    }

    public override float CurrentRange
    {
        get
        {
            float valF = BaseRange + EquipmentRangeBonus + GetAbilityRangeModifier();
            if (data != null)
            {
                var u = AggregateUnitBonusesLocal(owner, data);
                valF = (valF + u.rangeAdd) * (1f + u.rangePct);
            }
            if (owner != null)
            {
                var e = AggregateAllEquippedBonusesLocal(owner);
                valF = (valF + e.rangeAdd) * (1f + e.rangePct);
            }
            var aura = AggregateIncomingAuraBonuses();
            valF = (valF + aura.rangeAdd) * (1f + aura.rangePct);
            valF = ApplyResourceUpkeepToStat(valF);
            return IsDeactivatedByResourceUpkeep ? 0f : valF; // Return as float, no rounding
        }
    }


    // CanMoveTo is fully consolidated in BaseUnit — no override needed.

    // MoveAlongPath removed -- all movement now goes through UnitMovementController.ExecuteMovement

    private static CombatTargetDomain GetTargetDomainForCombatUnit(CombatUnit target)
    {
        if (target == null || target.data == null)
            return CombatTargetDomain.Ground;

        CombatCategory category = target.data.unitType;
        if (CombatUnitData.IsAirCategory(category) || target.currentLayer == TileLayer.Atmosphere)
            return CombatTargetDomain.Air;
        if (category == CombatCategory.Spaceship || target.currentLayer == TileLayer.Orbit)
            return CombatTargetDomain.Space;
        if (CombatUnitData.IsUnderwaterCategory(category))
            return CombatTargetDomain.Underwater;
        if (CombatUnitData.IsNavalSurfaceCategory(category))
            return CombatTargetDomain.NavalSurface;
        if (target.currentLayer == TileLayer.Underwater)
            return CombatTargetDomain.Underwater;
        return CombatTargetDomain.Ground;
    }

    private static CombatTargetDomain GetTargetDomainForWorker(WorkerUnit target)
    {
        if (target == null)
            return CombatTargetDomain.Ground;
        if (target.currentLayer == TileLayer.Orbit)
            return CombatTargetDomain.Space;
        if (target.currentLayer == TileLayer.Atmosphere)
            return CombatTargetDomain.Air;
        if (target.currentLayer == TileLayer.Underwater)
            return CombatTargetDomain.Underwater;
        return CombatTargetDomain.Ground;
    }

    private bool CanAttackTargetDomain(CombatTargetDomain targetDomain)
    {
        if (data == null) return false;
        switch (targetDomain)
        {
            case CombatTargetDomain.Air:
                return data.canAttackAir;
            case CombatTargetDomain.Space:
                return data.canAttackSpace;
            case CombatTargetDomain.NavalSurface:
                return true;
            case CombatTargetDomain.Underwater:
                return data.canAttackUnderwater;
            default:
                return true;
        }
    }

    // ===== COMBAT UNIT VS COMBAT UNIT =====
    
    public bool CanAttack(CombatUnit target)
    {
        if (target == null || target.data == null || data == null) return false;

        CombatTargetDomain targetDomain = GetTargetDomainForCombatUnit(target);
        if (!CanAttackTargetDomain(targetDomain)) return false;

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
    /// Checks whether this unit can resolve hostile contact with a civilian.
    /// This is target validation only; workers never enter the damage pipeline.
    /// </summary>
    public bool CanAttack(WorkerUnit target)
    {
        if (target == null || data == null) return false;

        CombatTargetDomain targetDomain = GetTargetDomainForWorker(target);
        if (!CanAttackTargetDomain(targetDomain)) return false;
        
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
        float dmgMul = GetAbilityDamageMultiplier() * GetTargetedAbilityDamageMultiplierAgainst(target);

        AttackType attackType = activeWeapon != null && IsProjectileWeapon(activeWeapon) ? AttackType.Ranged : AttackType.Melee;
        CombatTargetDomain targetDomain = GetTargetDomainForCombatUnit(target);
        float attackerValue = GetTargetedAttackValue(activeWeapon, target, targetDomain, attackType);
        attackerValue = ApplyGoldMaintenanceToCombatStat(attackerValue);
        float defenderValue = target.GetBaseDefenseFloat();
        defenderValue = (defenderValue + target.GetSituationalDefenseAddAgainst(this)) * (1f + target.GetSituationalDefensePctAgainst(this));
        defenderValue = target.ApplyGoldMaintenanceToCombatStat(defenderValue);

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

    // If the active weapon is a projectile weapon, queue/spawn the projectile visual and apply damage on impact.
    if (activeWeapon != null && IsProjectileWeapon(activeWeapon))
        {
            ProjectileData resolvedProjectile = ResolveProjectileForWeapon(activeWeapon);
            if (resolvedProjectile == null)
            {
                Debug.LogWarning($"{name}: Ranged attack aborted because no compatible ActiveProjectile/default projectile is available for {activeWeapon.name}.");
                return;
            }

            if (!TryConsumeAttackPoint())
                return;

            SetAnimatorTriggerForFormation(attackHash);
            AddFatigue(8f);

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
    /// Resolves hostile civilian contact without weapon, defence, counterattack, or
    /// tactical-battle calculations. The contact consumes the normal attack action.
    /// </summary>
    public void Attack(WorkerUnit target)
    {
        if (target == null) return;
        if (!CanAttack(target)) return;

        if (!TryConsumeAttackPoint()) return;
        CivilianCaptureService.ResolveAttack(this, target);
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
        SpawnProjectileFromEquipment(equipment, targetPosition, null, damage);
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

        float dmgMul = GetAbilityDamageMultiplier() * GetTargetedAbilityDamageMultiplierAgainst(attacker);
        AttackType attackType = AttackType.Melee;
        CombatTargetDomain targetDomain = GetTargetDomainForCombatUnit(attacker);
        EquipmentData activeWeapon = equippedWeapon;
        float attackerValue = GetTargetedAttackValue(activeWeapon, attacker, targetDomain, attackType);
        attackerValue = ApplyGoldMaintenanceToCombatStat(attackerValue);
        float defenderValue = attacker.GetBaseDefenseFloat();
        defenderValue = (defenderValue + attacker.GetSituationalDefenseAddAgainst(this)) * (1f + attacker.GetSituationalDefensePctAgainst(this));
        defenderValue = attacker.ApplyGoldMaintenanceToCombatStat(defenderValue);

        float rawF = Mathf.Max(0f, attackerValue - defenderValue - tileBonus);
        int damage = Mathf.RoundToInt(rawF * dmgMul);

        damage = ApplySharedMeleeCombatModifiers(damage, attacker);

    var ctxCounter = new BaseUnit.AttackContext { attacker = this, defender = attacker, weapon = activeWeapon, damage = damage, isMelee = true, isRanged = false };
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


    public CombatUnitData GetLatestUnlockedUpgrade()
    {
        if (data == null || owner == null) return null;
        var latest = data.GetLatestUnlockedUpgrade(owner);
        return latest != data ? latest : null;
    }

    public int GetUpgradeGoldCost(CombatUnitData target)
    {
        return data != null ? data.GetUpgradeGoldCostTo(target) : 0;
    }

    public bool CanUpgrade(out CombatUnitData target, out int goldCost, out string reason)
    {
        target = GetLatestUnlockedUpgrade();
        goldCost = GetUpgradeGoldCost(target);
        reason = null;

        if (data == null) { reason = "missing unit data"; return false; }
        if (owner == null) { reason = "missing owner"; return false; }
        if (target == null) { reason = "no unlocked upgrade"; return false; }
        if (owner.gold < goldCost) { reason = $"requires {goldCost} gold"; return false; }
        return true;
    }

    public bool TryUpgradeToLatest()
    {
        if (!CanUpgrade(out var target, out int goldCost, out string reason))
        {
            Debug.Log($"[CombatUnit] Upgrade rejected for {data?.unitName ?? name}: {reason}");
            return false;
        }

        int previousHealth = currentHealth;
        var previousData = data;
        owner.gold -= goldCost;
        data = target;

        try { attackPointsPerTurn = data.attackPointsPerTurn; currentAttackPoints = Mathf.Min(currentAttackPoints, MaxAttackPoints); } catch { }
        takesWeatherDamage = data.takesWeatherDamage;
        currentHealth = Mathf.Min(previousHealth, MaxHealth);
        RecalculateStats();
        currentHealth = Mathf.Min(previousHealth, MaxHealth);

        UpdateEquipmentVisuals();
        UpdateUnitLabel();
        OnHealthChanged?.Invoke(currentHealth, MaxHealth);
        try { GameEventManager.Instance?.RaiseHealthChanged(this, previousHealth, currentHealth, MaxHealth); } catch { }
        UIManager.Instance?.ShowNotification($"Upgraded {previousData.unitName} to {target.unitName} for {goldCost} gold.");
        return true;
    }

    public void GainExperience(int xp)
    {
        experience += xp;
        while (data != null && level <= data.xpToNextLevel.Length && experience >= data.xpToNextLevel[level - 1])
            LevelUp();
    }

    public override void ApplyBattleExperience(int gainedExperience)
    {
        if (gainedExperience <= 0)
            return;

        GainExperience(gainedExperience);
    }

    public override void MarkCampaignActionConsumedByBattle()
    {
        ConsumeAction();
        TryConsumeAttackPoint();
    }

    /// <summary>
    /// Called by projectiles or other external systems when this unit's attack caused a kill.
    /// Awards XP for the kill.
    /// </summary>
    public void RegisterKillFromProjectile(int damage)
    {
        GainExperience(damage);
    }

    public void ApplyStartingProgression(int startingExperience, int startingLevels)
    {
        if (startingLevels > 0)
        {
            for (int i = 0; i < startingLevels; i++)
                LevelUp();
        }

        if (startingExperience > 0)
            GainExperience(startingExperience);

        currentHealth = MaxHealth;
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
            // Attack/Defense/Range handled dynamically via getters or in combat
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
            var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
            currentLayer = UnitLayerRules.GetSpawnTileLayerForUnit(this, ts != null ? ts.GetTileData(tileIndex) : null);
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
        actedLastTurn = performedActionThisTurn || hasActedThisTurn;
        performedActionThisTurn = false;
        hasActedThisTurn = false;

        // Base resets (move points, AP, winter penalties)
        RestoreMovePointsForNewTurn();
        ResetAttackPointsForNewTurn();

        // Warfare depth systems (fatigue recovery, status effect ticks)
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
    public void RecordTurnAction()
    {
        performedActionThisTurn = true;
    }

    public void ConsumeAction()
    {
        RecordTurnAction();
        hasActedThisTurn = true;
    }

    public override void DeductMovePoints(int amount)
    {
        base.DeductMovePoints(amount);
        if (amount > 0) RecordTurnAction();
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
    /// Attempts to load a unit into this transport/carrier.
    /// </summary>
    /// <param name="unit">The unit to load</param>
    /// <returns>True if successful, false otherwise</returns>
    public bool LoadUnit(CombatUnit unit)
    {
        if (!CanLoadUnit(unit))
            return false;

        transportedUnits.Add(unit);

        // Remove the passenger from the map layer it used before becoming based on
        // the transport. Otherwise a hidden aircraft/spaceship can still block tiles.
        var occ = TileOccupancyManager.GetForPlanet(unit.planetIndex) ?? TileOccupancyManager.Instance;
        try { occ?.ClearOccupantById(unit.currentTileIndex, unit.currentLayer, unit.gameObject.GetRuntimeId()); } catch { }

        // Update the loaded unit's state and keep it logically co-located with the carrier.
        unit.currentTileIndex = currentTileIndex;
        unit.planetIndex = planetIndex;
        unit.currentLayer = currentLayer;
        unit.IsTransported = true;
        unit.TransportingUnit = this;
        unit.OnLoadedIntoTransport.Invoke(this);

        // Hide the unit visually while it is based inside the carrier/transport.
        unit.gameObject.SetActive(false);

        // Fire event for UI updates
        OnUnitLoaded.Invoke(unit);

        return true;
    }

    /// <summary>Restores a tactical battle cargo relationship without requiring campaign-map adjacency.</summary>
    public bool TryRestoreBattleCargo(CombatUnit unit)
    {
        if (unit == null || unit == this || data == null || unit.data == null || !data.isTransport || !data.CanCarryUnitCategory(unit.data.unitType))
            return false;
        if (transportedUnits.Contains(unit))
            return true;
        if (transportedUnits.Count >= data.transportCapacity)
            return false;
        if (unit.owner != owner)
            return false;

        var occupancy = TileOccupancyManager.GetForPlanet(unit.planetIndex) ?? TileOccupancyManager.Instance;
        try { occupancy?.ClearOccupantById(unit.currentTileIndex, unit.currentLayer, unit.gameObject.GetRuntimeId()); } catch { }
        transportedUnits.Add(unit);
        unit.currentTileIndex = currentTileIndex;
        unit.planetIndex = planetIndex;
        unit.currentLayer = currentLayer;
        unit.IsTransported = true;
        unit.TransportingUnit = this;
        unit.gameObject.SetActive(false);
        unit.OnLoadedIntoTransport.Invoke(this);
        OnUnitLoaded.Invoke(unit);
        return true;
    }

    /// <summary>
    /// Returns true if this transport/carrier is allowed to load the given unit right now.
    /// </summary>
    public bool CanLoadUnit(CombatUnit unit)
    {
        if (unit == null || unit == this || data == null || unit.data == null)
            return false;

        if (!data.isTransport || !data.CanCarryUnitCategory(unit.data.unitType))
            return false;

        if (unit.IsTransported || unit.TransportingUnit != null)
            return false;

        if (transportedUnits.Count >= data.transportCapacity)
            return false;

        if (unit.owner != owner)
            return false;

        if (unit.planetIndex != planetIndex)
            return false;

        // Units may load from the carrier's tile or an adjacent tile. Aircraft and
        // spaceships may occupy a different map layer than the carrier, so layer is
        // intentionally not part of the adjacency check.
        if (!IsSameOrAdjacentTile(unit.currentTileIndex))
            return false;

        return true;
    }

    private bool IsSameOrAdjacentTile(int tileIndex)
    {
        if (tileIndex == currentTileIndex)
            return true;

        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        int[] neighbors = ts != null ? ts.GetNeighbors(currentTileIndex) : null;
        if (neighbors == null)
            return false;

        foreach (int neighbor in neighbors)
        {
            if (tileIndex == neighbor)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Unloads a transported/based unit to a specific tile.
    /// </summary>
    /// <param name="unit">The unit to unload</param>
    /// <param name="targetTileIndex">The tile to unload to</param>
    /// <returns>True if successful, false otherwise</returns>
    public bool UnloadUnit(CombatUnit unit, int targetTileIndex)
    {
        if (!CanUnloadUnitTo(unit, targetTileIndex))
            return false;

        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null)
        {
            Debug.LogError("[CombatUnit] TileSystem not ready; cannot unload unit in flat-only mode.");
            return false;
        }

        TileLayer deployLayer = GetPassengerDeploymentLayer(unit);

        // Update tile occupancy using layered occupancy manager before making the unit visible.
        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        try
        {
            if (occ != null && !occ.TrySetOccupant(targetTileIndex, unit.gameObject, deployLayer))
                return false;
        }
        catch { return false; }

        // Remove from transport only after the destination claim succeeds.
        transportedUnits.Remove(unit);

        // Update the unloaded unit's state
        unit.IsTransported = false;
        unit.TransportingUnit = null;
        unit.currentTileIndex = targetTileIndex;
        unit.planetIndex = planetIndex;
        unit.currentLayer = deployLayer;
        unit.OnUnloadedFromTransport.Invoke(this);

        // Position the unit at the target tile and show it
        unit.gameObject.SetActive(true);
        unit.transform.position = ts.GetTileSurfacePosition(targetTileIndex);

        // Trigger trap if unloading onto a trapped tile
        ImprovementManager.Instance?.NotifyUnitEnteredTile(targetTileIndex, unit);

        // Fire event for UI updates
        OnUnitUnloaded.Invoke(unit);

        return true;
    }

    /// <summary>
    /// Returns true if a transported/based unit can deploy to the requested tile.
    /// </summary>
    public bool CanUnloadUnitTo(CombatUnit unit, int targetTileIndex)
    {
        if (unit == null || !transportedUnits.Contains(unit))
            return false;

        if (!IsSameOrAdjacentTile(targetTileIndex))
            return false;

        TileLayer originalLayer = unit.currentLayer;
        int originalPlanet = unit.planetIndex;
        int originalTile = unit.currentTileIndex;

        try
        {
            unit.planetIndex = planetIndex;
            unit.currentTileIndex = currentTileIndex;
            unit.currentLayer = GetPassengerDeploymentLayer(unit);
            return unit.CanMoveTo(targetTileIndex);
        }
        finally
        {
            unit.currentLayer = originalLayer;
            unit.planetIndex = originalPlanet;
            unit.currentTileIndex = originalTile;
        }
    }

    private TileLayer GetPassengerDeploymentLayer(CombatUnit unit)
    {
        if (unit != null && unit.data != null)
        {
            if (CombatUnitData.IsAirCategory(unit.data.unitType))
                return TileLayer.Atmosphere;

            if (CombatUnitData.IsSpaceCategory(unit.data.unitType))
                return TileLayer.Orbit;
        }

        return currentLayer;
    }

    private void SyncTransportedUnitsToCarrier()
    {
        for (int i = 0; i < transportedUnits.Count; i++)
        {
            CombatUnit unit = transportedUnits[i];
            if (unit == null)
                continue;

            unit.currentTileIndex = currentTileIndex;
            unit.planetIndex = planetIndex;
            unit.currentLayer = currentLayer;
        }
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
                            if (equipmentData.isTwoHanded && equippedShield != null)
                                equippedShield = null;
                            // Decide whether this weapon should occupy the projectile slot or the main weapon slot (melee uses the main weapon).
                            if (IsProjectileWeapon(equipmentData))
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
            case EquipmentType.Head:
                if (equippedHead != equipmentData) { equippedHead = equipmentData; changed = true; }
                break;
            case EquipmentType.Tool:
                if (equippedTool != equipmentData) { equippedTool = equipmentData; changed = true; }
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
        ClearLoadedProjectileVisual();
        currentProjectileWeaponVisual = null;

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
            currentProjectileWeaponVisual = equipObj;
        }
        else
        {
            equippedItemObjects[type] = equipObj;
        }
    }

    // SpawnProjectileFromEquipment needs to be overridden to handle CombatUnit specific target tracking
    public override void SpawnProjectileFromEquipment(EquipmentData equipment, Vector3 targetPosition, BaseUnit targetUnit = null, int overrideDamage = -1)
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
        RebuildEquipmentGrantedAbilities();
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
        RebuildEquipmentGrantedAbilities();
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
            SyncTransportedUnitsToCarrier();
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
