    // Add this stub so property setters compile. Implement visuals as needed.

using UnityEngine;
using System.Collections;
using System.Linq; // Add this for array extension methods like Contains
using TMPro;

[RequireComponent(typeof(Animator))]
public class WorkerUnit : MonoBehaviour
{
    [SerializeField] SphericalHexGrid grid;
    [Header("Equipment Attachment Points")]
    [Tooltip("Transform where weapons will be attached")]
    public Transform weaponHolder;
    [Tooltip("Transform where shields will be attached")]
    public Transform shieldHolder;
    [Tooltip("Transform where armor will be displayed")]
    public Transform armorHolder;
    [Tooltip("Transform where miscellaneous items will be attached")]
    public Transform miscHolder;
    PlanetGenerator planet;
    Animator animator;

    [Header("Animation Control")]
    private Animator unitAnimator;
    
    // Animation parameter hashes for efficiency
    private readonly int idleYoungHash = Animator.StringToHash("IdleYoung");
    private readonly int idleExperiencedHash = Animator.StringToHash("IdleExperienced");
    private readonly int attackHash = Animator.StringToHash("Attack");
    private readonly int hitHash = Animator.StringToHash("Hit");
    private readonly int deathHash = Animator.StringToHash("Death");
    private readonly int routHash = Animator.StringToHash("Rout");
    private readonly int foundCityHash = Animator.StringToHash("FoundCity");
    private readonly int isWalkingHash = Animator.StringToHash("IsWalking");
    private readonly int forageHash = Animator.StringToHash("Forage");
    private readonly int buildHash = Animator.StringToHash("Build");  // Worker-specific

    [Header("Progression")]
    public int level = 1;  // starts at 1

    public WorkerUnitData data { get; private set; }
    public Civilization owner { get; private set; }

    public int currentHealth { get; private set; }
    public int currentWorkPoints { get; private set; }
    public int currentMovePoints { get; private set; }
    
    // Trap immobilization state
    private int trappedTurnsRemaining = 0;
    public bool IsTrapped => trappedTurnsRemaining > 0;
    
    // Flag for tracking winter movement penalty
    public bool hasWinterPenalty { get; set; }

    [Header("Weather")]
    [Tooltip("If true, this unit takes weather attrition in severe seasons (e.g., winter)")]
    public bool takesWeatherDamage = true;


    [Header("Equipped Items (Editable)")]
    [SerializeField] private EquipmentData _equippedWeapon;
    [SerializeField] private EquipmentData _equippedShield;
    [SerializeField] private EquipmentData _equippedArmor;
    [SerializeField] private EquipmentData _equippedMiscellaneous;

    [Header("Editor")]
    [Tooltip("If true, changing equipment in the Inspector will update visuals immediately in Edit mode. Disable to keep equipment invisible when editing the prefab/scene.")]
    [SerializeField] private bool updateEquipmentInEditor = true;

    // Track instantiated equipment objects by slot
    private readonly System.Collections.Generic.Dictionary<EquipmentType, GameObject> equippedItemObjects = new System.Collections.Generic.Dictionary<EquipmentType, GameObject>();

    // Cached weapon grip from currently equipped weapon visual (found by name on instantiated equipment)
    private Transform _weaponGrip;

    private void UpdateEquipmentVisuals()
    {
    // Clear cached grip before replacing visuals
    _weaponGrip = null;
        
        // Remove old visuals
        foreach (var obj in equippedItemObjects.Values)
        {
            if (obj != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    UnityEngine.Object.DestroyImmediate(obj);
                else
#endif
                    UnityEngine.Object.Destroy(obj);
            }
        }
        equippedItemObjects.Clear();

        // Process all equipment slots, including empty ones
        ProcessEquipmentSlot(EquipmentType.Weapon, equippedWeapon);
        ProcessEquipmentSlot(EquipmentType.Shield, equippedShield);
        ProcessEquipmentSlot(EquipmentType.Armor, equippedArmor);
        ProcessEquipmentSlot(EquipmentType.Miscellaneous, equippedMiscellaneous);
    }
    
    private void ProcessEquipmentSlot(EquipmentType type, EquipmentData equipment)
    {
        // If no equipment data, slot is empty - we're done
        if (equipment == null || equipment.equipmentPrefab == null)
        {
            Debug.Log($"[ProcessEquipmentSlot] Slot {type} is empty on {gameObject.name}");
            return;
        }
        
    // Instantiate the equipment UNPARENTED so we can align grips correctly, then parent preserving world transform
    var go = Instantiate(equipment.equipmentPrefab);

    // Disable physics early
    foreach (var rb in go.GetComponentsInChildren<Rigidbody>())
    {
        rb.isKinematic = true;
        rb.useGravity = false;
    }
    foreach (var c in go.GetComponentsInChildren<Collider>())
        c.enabled = false;

    // Determine the target parent/holder for parenting later
    Transform targetParent = transform;
    if (type == EquipmentType.Weapon && weaponHolder != null) targetParent = weaponHolder;
    else if (type == EquipmentType.Shield && shieldHolder != null) targetParent = shieldHolder;
    else if (type == EquipmentType.Armor && armorHolder != null) targetParent = armorHolder;
    else if (type == EquipmentType.Miscellaneous && miscHolder != null) targetParent = miscHolder;

    // Cache grip sockets by convention (support WeaponGripPoints component)
    if (type == EquipmentType.Weapon && targetParent != null)
    {
        WeaponGripPoints wg = go.GetComponentInChildren<WeaponGripPoints>();
        _weaponGrip = null;
        if (wg != null)
        {
            if (wg.rightHandGrip != null) _weaponGrip = wg.rightHandGrip;
            else if (wg.leftHandGrip != null) _weaponGrip = wg.leftHandGrip;
        }
        if (_weaponGrip == null)
            _weaponGrip = FindChildRecursive(go.transform, "Grip_R") ?? FindChildRecursive(go.transform, "Grip_L");

        if (_weaponGrip != null && weaponHolder != null)
        {
            Vector3 gripWorld = _weaponGrip.position;
            Quaternion gripRot = _weaponGrip.rotation;
            Vector3 desiredPos = weaponHolder.position;
            Quaternion desiredRot = weaponHolder.rotation;

            Vector3 delta = desiredPos - gripWorld;
            go.transform.position += delta;
            Quaternion fromTo = desiredRot * Quaternion.Inverse(gripRot);
            go.transform.rotation = fromTo * go.transform.rotation;
        }

        // Parent while preserving the world transform we just set
        go.transform.SetParent(targetParent, true);
    }
    else
    {
        // Default parenting behavior for non-weapon slots or when no holder is available
        go.transform.SetParent(targetParent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
    }

    equippedItemObjects[type] = go;
        Debug.Log($"[ProcessEquipmentSlot] Equipped {equipment.equipmentName} in {type} slot on {gameObject.name}");
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

    public EquipmentData equippedWeapon
    {
        get => _equippedWeapon; // Remove fallback logic
        set
        {
            if (_equippedWeapon == value) return;
            if (value != null && value.targetUnit == EquipmentTarget.CombatUnit)
            {
                Debug.LogWarning($"[Equip] Tried to equip combat-only item '{value.equipmentName}' onto worker {gameObject.name}. Ignored.");
                return;
            }
            _equippedWeapon = value;
            if (Application.isPlaying || updateEquipmentInEditor)
                UpdateEquipmentVisuals();
        }
    }
    public EquipmentData equippedShield {
        get => _equippedShield; // Remove fallback logic
        set {
            if (_equippedShield == value) return;
            if (value != null && value.targetUnit == EquipmentTarget.CombatUnit)
            {
                Debug.LogWarning($"[Equip] Tried to equip combat-only item '{value.equipmentName}' onto worker {gameObject.name}. Ignored.");
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
            if (value != null && value.targetUnit == EquipmentTarget.CombatUnit)
            {
                Debug.LogWarning($"[Equip] Tried to equip combat-only item '{value.equipmentName}' onto worker {gameObject.name}. Ignored.");
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
            if (value != null && value.targetUnit == EquipmentTarget.CombatUnit)
            {
                Debug.LogWarning($"[Equip] Tried to equip combat-only item '{value.equipmentName}' onto worker {gameObject.name}. Ignored.");
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
            Debug.LogWarning("No WorkerUnitData assigned. Cannot equip defaults.");
            return;
        }
        equippedWeapon = data.defaultWeapon;
        equippedShield = data.defaultShield;
        equippedArmor = data.defaultArmor;
        equippedMiscellaneous = data.defaultMiscellaneous;
        #if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        #endif
        Debug.Log($"Equipped default equipment for {gameObject.name} in editor.");
    }

    // --- Combat Stats (if applicable) ---
    public int CurrentAttack => (data != null) ? data.baseAttack : 0;
    public int CurrentDefense => (data != null) ? data.baseDefense : 0;

    [Header("UI")]
    [SerializeField] private GameObject unitLabelPrefab;
    private UnitLabel unitLabelInstance;

    // Local worker bonus aggregation
    // Note: work/move/health flat adds are floats to support fractional bonuses from tools/equipment
    private struct WorkerAgg { public float workAdd, moveAdd, healthAdd; public float workPct, movePct, healthPct; }
    private WorkerAgg AggregateWorkerBonusesLocal(Civilization civ, WorkerUnitData wu)
    {
        WorkerAgg a = new WorkerAgg(); if (civ == null || wu == null) return a;
        if (civ.researchedTechs != null)
            foreach (var t in civ.researchedTechs)
            {
                if (t?.workerBonuses == null) continue;
                foreach (var b in t.workerBonuses)
                    if (b != null && b.worker == wu)
                    {
                        a.workAdd += b.workPointsAdd; a.moveAdd += b.movePointsAdd; a.healthAdd += b.healthAdd;
                        a.workPct += b.workPointsPct; a.movePct += b.movePointsPct; a.healthPct += b.healthPct;
                    }
            }
        if (civ.researchedCultures != null)
            foreach (var c in civ.researchedCultures)
            {
                if (c?.workerBonuses == null) continue;
                foreach (var b in c.workerBonuses)
                    if (b != null && b.worker == wu)
                    {
                        a.workAdd += b.workPointsAdd; a.moveAdd += b.movePointsAdd; a.healthAdd += b.healthAdd;
                        a.workPct += b.workPointsPct; a.movePct += b.movePointsPct; a.healthPct += b.healthPct;
                    }
            }

        // Include equipped items (tools) that grant work/move/health bonuses.
        // Only include equipment that is valid for workers (EquipmentTarget.WorkerUnit or Both).
        var equippedList = new EquipmentData[] { _equippedWeapon, _equippedShield, _equippedArmor, _equippedMiscellaneous };
        foreach (var eq in equippedList)
        {
            if (eq == null) continue;
            if (eq.targetUnit == EquipmentTarget.CombatUnit) continue; // skip combat-only items
            a.workAdd += eq.workPointsBonus;
            a.moveAdd += eq.movementBonus;
            a.healthAdd += eq.healthBonus;
        }

        return a;
    }

    void Awake()
    {
        animator = GetComponent<Animator>();
        // FIXED: For civilization units, always use Earth (planet index 0)
        // Don't use GetCurrentPlanetGenerator which can point to wrong planet during multi-planet generation
        planet = GameManager.Instance?.GetPlanetGenerator(0); // Force Earth
        if (planet != null) grid = planet.Grid;
        unitAnimator = GetComponent<Animator>();
        UnitRegistry.Register(gameObject);

        // Fallback: If any equipped slot is null but data/default exists, auto-equip it
        bool equippedAny = false;
        if (data != null)
        {
            if (_equippedWeapon == null && data.defaultWeapon != null)
            {
                _equippedWeapon = data.defaultWeapon;
                equippedAny = true;
                Debug.Log($"[Awake] Auto-assigned default weapon {data.defaultWeapon.equipmentName} to {gameObject.name}");
            }
            if (_equippedShield == null && data.defaultShield != null)
            {
                _equippedShield = data.defaultShield;
                equippedAny = true;
                Debug.Log($"[Awake] Auto-assigned default shield {data.defaultShield.equipmentName} to {gameObject.name}");
            }
            if (_equippedArmor == null && data.defaultArmor != null)
            {
                _equippedArmor = data.defaultArmor;
                equippedAny = true;
                Debug.Log($"[Awake] Auto-assigned default armor {data.defaultArmor.equipmentName} to {gameObject.name}");
            }
            if (_equippedMiscellaneous == null && data.defaultMiscellaneous != null)
            {
                _equippedMiscellaneous = data.defaultMiscellaneous;
                equippedAny = true;
                Debug.Log($"[Awake] Auto-assigned default miscellaneous {data.defaultMiscellaneous.equipmentName} to {gameObject.name}");
            }
        }
        if (equippedAny)
        {
            Debug.Log($"[Awake] Auto-equipped default equipment for {gameObject.name}.");
        }
        // If you have visuals, update them here (add UpdateEquipmentVisuals if needed)
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

            // Editor-time validation: ensure assigned equipment is compatible with WorkerUnit
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                // If an equipment asset is explicitly assigned that targets CombatUnit only, clear it and warn
                if (_equippedWeapon != null && _equippedWeapon.targetUnit == EquipmentTarget.CombatUnit)
                {
                    Debug.LogWarning($"[OnValidate] Clearing incompatible equipment '{_equippedWeapon.equipmentName}' from worker '{gameObject.name}' (combat-only).");
                    _equippedWeapon = null;
                }
                if (_equippedShield != null && _equippedShield.targetUnit == EquipmentTarget.CombatUnit)
                {
                    Debug.LogWarning($"[OnValidate] Clearing incompatible equipment '{_equippedShield.equipmentName}' from worker '{gameObject.name}' (combat-only).");
                    _equippedShield = null;
                }
                if (_equippedArmor != null && _equippedArmor.targetUnit == EquipmentTarget.CombatUnit)
                {
                    Debug.LogWarning($"[OnValidate] Clearing incompatible equipment '{_equippedArmor.equipmentName}' from worker '{gameObject.name}' (combat-only).");
                    _equippedArmor = null;
                }
                if (_equippedMiscellaneous != null && _equippedMiscellaneous.targetUnit == EquipmentTarget.CombatUnit)
                {
                    Debug.LogWarning($"[OnValidate] Clearing incompatible equipment '{_equippedMiscellaneous.equipmentName}' from worker '{gameObject.name}' (combat-only).");
                    _equippedMiscellaneous = null;
                }
                UpdateEquipmentVisuals();
                UnityEditor.EditorUtility.SetDirty(this);
            };
        }
    }

    void OnDestroy()
    {
        // Unsubscribe from events
        GameEventManager.Instance.OnMovementCompleted -= HandleMovementCompleted;
        UnitRegistry.Unregister(gameObject);
    }

    public void Initialize(WorkerUnitData unitData, Civilization unitOwner)
    {
        data = unitData;
        owner = unitOwner;
        level = 1;                    // reset
    // Apply targeted bonuses (health affects max/current)
    var wb = AggregateWorkerBonusesLocal(unitOwner, unitData);
    int maxHealth = Mathf.RoundToInt((data.baseHealth + wb.healthAdd) * (1f + wb.healthPct));
    currentHealth    = maxHealth;
    currentWorkPoints = Mathf.RoundToInt((data.baseWorkPoints + wb.workAdd) * (1f + wb.workPct));
    currentMovePoints = Mathf.RoundToInt((data.baseMovePoints + wb.moveAdd) * (1f + wb.movePct));
    // Weather susceptibility from data
    takesWeatherDamage = (data != null) ? data.takesWeatherDamage : takesWeatherDamage;
        unitAnimator.SetTrigger("IdleYoung");  // explicit initial idle

        // Subscribe to events
        GameEventManager.Instance.OnMovementCompleted += HandleMovementCompleted;

        // Instantiate and initialize the unit label
        if (unitLabelPrefab != null && unitLabelInstance == null)
        {
            var labelGO = Instantiate(unitLabelPrefab, transform);
            unitLabelInstance = labelGO.GetComponent<UnitLabel>();
            if (unitLabelInstance != null)
            {
                string ownerName = owner != null && owner.civData != null ? owner.civData.civName : "Unknown";
                unitLabelInstance.Initialize(transform, data.unitName, ownerName, currentHealth, data.baseHealth);

                // Disable raycast targets on the label's text components
                var textComponents = unitLabelInstance.GetComponentsInChildren<TextMeshProUGUI>();
                foreach (var textComponent in textComponents)
                {
                    if (textComponent != null) textComponent.raycastTarget = false;
                }
            }
        }
    }

    /// <summary>
    /// Initialize the worker unit with a specific tile index
    /// </summary>
    public void Initialize(WorkerUnitData unitData, Civilization unitOwner, int tileIndex)
    {
        // Call the main initialize method first
        Initialize(unitData, unitOwner);
        
        // Set the tile index
        currentTileIndex = tileIndex;
        
        // Update the unit's transform position with proper surface orientation
        if (grid != null)
        {
            PositionUnitOnSurface(grid, tileIndex);
        }
    }

    /// <summary>
    /// Properly positions and orients the unit on the planet surface
    /// </summary>
    /// <summary>
    /// Properly positions and orients the unit on the planet surface
    /// </summary>
    private void PositionUnitOnSurface(SphericalHexGrid G, int tileIndex)
    {
        // FIXED: For civilization units, always use Earth (planet index 0)
        // Get the extruded center of the tile in world space on Earth
        Vector3 tileSurfaceCenter = TileDataHelper.Instance.GetTileSurfacePosition(tileIndex, 0f, 0); // Force Earth (planet index 0)
        
        // Set unit position directly on the surface
        transform.position = tileSurfaceCenter;

        // FIXED: Get Earth planet generator for proper orientation
        var earthPlanet = GameManager.Instance?.GetPlanetGenerator(0);
        if (earthPlanet == null)
        {
            Debug.LogError("[WorkerUnit] Cannot find Earth planet generator for unit positioning!");
            return;
        }

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
        
        Debug.Log($"[WorkerUnit] Positioned {data.unitName} at tile {tileIndex}. Surface normal: {surfaceNormal}, Forward: {tangentForward}");
    }

    public bool CanBuild(ImprovementData imp, int tileIndex)
    {
    var (tileData, _) = TileDataHelper.Instance.GetTileData(tileIndex);
    if (tileData == null) return false;

    // Use civ helper to respect obsolescence filtering
    var available = owner != null ? owner.GetAvailableImprovementsForWorker(data, tileIndex) : null;
    bool listedAndAvailable = available != null && available.Contains(imp);

    // must be land and in filtered availability list
    return tileData.isLand && listedAndAvailable;
    }

    public void StartBuilding(ImprovementData imp, int tileIndex)
    {
        bool started = ImprovementManager.Instance
            .CreateBuildJob(imp, tileIndex, owner);
        if (!started) return;

        // Show construction prefab
        Vector3 pos = grid.tileCenters[tileIndex];
        if (imp.constructionPrefab != null)
            Instantiate(imp.constructionPrefab, pos, Quaternion.identity);

        animator.SetTrigger("building");
    }

    // --- Unit construction via workers ---
    public bool CanBuildUnit(CombatUnitData unitData, int tileIndex)
    {
        if (unitData == null || owner == null) return false;
        if (!unitData.buildableByWorker) return false;
        if (!unitData.AreRequirementsMet(owner)) return false;
        if (!LimitManager.Instance.CanCreateCombatUnit(owner, unitData)) return false;

        var (tileData, _) = TileDataHelper.Instance.GetTileData(tileIndex);
        if (tileData == null) return false;
        if (!tileData.isLand) return false; // simple rule for now
        if (tileData.occupantId != 0 && tileData.occupantId != gameObject.GetInstanceID()) return false;
        return true;
    }

    public void StartBuildingUnit(CombatUnitData unitData, int tileIndex)
    {
        if (!CanBuildUnit(unitData, tileIndex)) return;
        bool started = ImprovementManager.Instance.CreateUnitJob(unitData, tileIndex, owner);
        if (!started) return;
        animator.SetTrigger("building");
    }

    // --- Build worker units via workers ---
    public bool CanBuildWorker(WorkerUnitData workerData, int tileIndex)
    {
        if (workerData == null || owner == null) return false;
        if (!workerData.buildableByWorker) return false;
        if (!workerData.AreRequirementsMet(owner)) return false;
        if (!LimitManager.Instance.CanCreateWorkerUnit(owner, workerData)) return false;

        var (tileData, _) = TileDataHelper.Instance.GetTileData(tileIndex);
        if (tileData == null) return false;
        if (!tileData.isLand) return false;
        if (tileData.occupantId != 0 && tileData.occupantId != gameObject.GetInstanceID()) return false;
        return true;
    }

    public void StartBuildingWorker(WorkerUnitData workerData, int tileIndex)
    {
        if (!CanBuildWorker(workerData, tileIndex)) return;
        bool started = ImprovementManager.Instance.CreateWorkerJob(workerData, tileIndex, owner);
        if (!started) return;
        animator.SetTrigger("building");
    }

    /// <summary>
    /// Use this worker's work points to add progress to the build job on its current tile.
    /// </summary>
    public void ContributeWork()
    {
    if (currentWorkPoints <= 0) return;

    int toAdd = currentWorkPoints;
    ImprovementManager.Instance.AddWork(currentTileIndex, toAdd);
    currentWorkPoints = 0;  // worker is spent for this turn
    }

    /// <summary>
    /// Use this worker's work points to add progress to a unit build job on its current tile.
    /// </summary>
    public void ContributeWorkToUnit()
    {
    if (currentWorkPoints <= 0) return;
    int toAdd = currentWorkPoints;
    ImprovementManager.Instance.AddUnitWork(currentTileIndex, toAdd);
    currentWorkPoints = 0;
    }

    /// <summary>
    /// Contribute work to a worker unit job on this tile.
    /// </summary>
    public void ContributeWorkToWorker()
    {
    if (currentWorkPoints <= 0) return;
    int toAdd = currentWorkPoints;
    ImprovementManager.Instance.AddWorkerWork(currentTileIndex, toAdd);
    currentWorkPoints = 0;
    }

    public bool CanForage(ResourceData resource, int tileIndex)
    {
        if (resource == null) return false;
        if (currentWorkPoints <= 0) return false;
        
        // Check if worker has required tech to harvest this resource
        if (resource.requiredTech != null && owner != null) 
        {
            if (!owner.researchedTechs.Contains(resource.requiredTech))
                return false;
        }
        
        // Check if tile is adjacent or same as worker's position
        if (tileIndex != currentTileIndex)
        {
            bool isAdjacent = false;
            var neighbors = grid.neighbors[currentTileIndex];
            foreach (int neighbor in neighbors)
            {
                if (neighbor == tileIndex)
                {
                    isAdjacent = true;
                    break;
                }
            }
            
            if (!isAdjacent) return false;
        }
        
        // Check if the worker has the necessary skills/tools for this resource type
        if (resource.requiresSpecialHarvester && !data.canHarvestSpecialResources)
            return false;
            
        return true;
    }
    
    public void Forage(ResourceData resource, int tileIndex)
    {
        if (!CanForage(resource, tileIndex)) return;
        
        // Deduct work points
        currentWorkPoints--;
        
        // Add resource to civilization's stockpile
        if (owner != null)
        {
            // Add the resource to stockpile
            owner.AddResource(resource, 1);
            
            // Add one-time forage yields
            if (resource.forageFood > 0) owner.food += resource.forageFood;
            if (resource.forageGold > 0) owner.gold += resource.forageGold;
            if (resource.forageScience > 0) owner.science += resource.forageScience;
            if (resource.forageCulture > 0) owner.culture += resource.forageCulture;
            if (resource.foragePolicyPoints > 0) owner.policyPoints += resource.foragePolicyPoints;
            if (resource.forageFaith > 0) owner.faith += resource.forageFaith;
            
            // Raise resource harvested event
            GameEventManager.Instance.RaiseResourceHarvestedEvent(this, resource.resourceName, 1);
            
            Debug.Log($"{owner.civData.civName} harvested {resource.resourceName}");
        }
        
        // Play animation if needed
        if (animator != null)
        {
            animator.SetTrigger("Forage");
        }
    }

    public void FoundCity()
    {
        if (!CanFoundCityOnCurrentTile()) return;

        // Trigger animation
        if (animator != null)
        {
            animator.SetTrigger(foundCityHash);
        }

        // Tell the owner to create a city at this location, passing correct references
        if (owner != null)
        {
            owner.FoundNewCity(currentTileIndex, grid, planet);
        }
        
        // This unit is consumed in the process
        Die();
    }

    public bool CanFoundCityOnCurrentTile()
    {
    if (!data.canFoundCity || owner == null) return false;
    // City-cap gate: nomads cannot settle until tech raises cap
    if (!owner.CanFoundMoreCities()) return false;

        // Basic check: is the tile land and not occupied by another city?
        var (tileData, _) = TileDataHelper.Instance.GetTileData(currentTileIndex);
        if (tileData == null || !tileData.isLand) return false;

        // More robust check: is there another city (owned by anyone) too close?
        // Let's define a minimum distance between cities.
        const float minCityDistance = 4.0f; // Approx. 3-4 tiles away on a default sphere. Adjust as needed.

        // Check against all cities from all known civilizations
        var allCivs = CivilizationManager.Instance.GetAllCivs();
        foreach (var civ in allCivs)
        {
            foreach (var city in civ.cities)
            {
                // Calculate distance between this unit's tile and the other city's tile
                float distance = Vector3.Distance(grid.tileCenters[currentTileIndex], grid.tileCenters[city.centerTileIndex]);
                if (distance < minCityDistance)
                {
                    // Too close to another city
                    return false;
                }
            }
        }

        // All checks passed
        return true;
    }

    public bool CanBuildRoute(RouteType type, HexTileData tile)
    {
        foreach (var r in data.buildableRoutes)
            if (r == type) return true;
        return false;
    }

    /// <summary>
    /// Apply damage to this unit, which reduces its health
    /// </summary>
    /// <param name="amount">Amount of damage to deal</param>
    /// <returns>True if the unit is destroyed by this damage</returns>
    public bool ApplyDamage(int amount)
    {
        animator.SetTrigger("hit");
        currentHealth -= amount;
        
        // Update label
        if (unitLabelInstance != null)
        {
            string ownerName = owner != null && owner.civData != null ? owner.civData.civName : "Unknown";
            unitLabelInstance.UpdateLabel(data.unitName, ownerName, currentHealth, data.baseHealth);
        }
        
        // Check if unit is now destroyed
        if (currentHealth <= 0)
        {
            Die();
            return true;
        }
        
        return false;
    }

    private void Die()
    {
        animator.SetTrigger("death");
        
        // Remove from civilization's unit list
        if (owner != null)
        {
            owner.workerUnits.Remove(this);
        }
        
        // Clean up any references or occupancy
        if (currentTileIndex >= 0)
        {
            TileDataHelper.Instance.ClearTileOccupant(currentTileIndex);
        }
        
        // Destroy the GameObject with a delay for death animation
        Destroy(gameObject, 2.5f);

        if (unitLabelInstance != null)
        {
            Destroy(unitLabelInstance.gameObject);
        }
    }
    
    public int currentTileIndex;
    public float moveSpeed = 2f;
    public bool isMoving { get; set; }

    public void MoveTo(int targetTileIndex)
    {
        var path = UnitMovementController.Instance.FindPath(currentTileIndex, targetTileIndex);
        if (path == null || path.Count == 0) return;
        StopAllCoroutines();
        StartCoroutine(UnitMovementController.Instance.MoveAlongPath(this, path));
    }

    public void ResetForNewTurn()
    {
        // Aggregate targeted bonuses
        var wb = AggregateWorkerBonusesLocal(owner, data);
        currentWorkPoints = Mathf.RoundToInt((data.baseWorkPoints + wb.workAdd) * (1f + wb.workPct));
        int baseMove = Mathf.RoundToInt((data.baseMovePoints + wb.moveAdd) * (1f + wb.movePct));

        // If trapped, decrement duration and block movement this turn
        if (IsTrapped)
        {
            trappedTurnsRemaining = Mathf.Max(0, trappedTurnsRemaining - 1);
            currentMovePoints = 0;
        }
        else
        {
            // Reset movement points with winter penalty if applicable
            currentMovePoints = baseMove;
            if (hasWinterPenalty && ClimateManager.Instance != null && 
                ClimateManager.Instance.currentSeason == Season.Winter)
            {
                currentMovePoints = Mathf.Max(1, currentMovePoints - 1);
            }
        }
        
        // Check for damage from hazardous biomes
        CheckForHazardousBiomeDamage();
    }

    // Apply immobilization effect from traps
    public void ApplyTrap(int turns)
    {
        trappedTurnsRemaining = Mathf.Max(trappedTurnsRemaining, turns);
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
            int damageAmount = Mathf.CeilToInt(data.baseHealth * damagePercent);
            
            // Apply damage
            ApplyDamage(damageAmount);
            
            // Notify player if this is their unit
            if (owner != null && owner.isPlayerControlled && UIManager.Instance != null)
            {
                UIManager.Instance.ShowNotification($"{data.unitName} took {damageAmount} damage from {tileData.biome} terrain!");
            }
        }
    }
    
    private void HandleMovementCompleted(GameEventManager.UnitMovementEventArgs args)
    {
        if (args.Unit == this)
        {
            // Handle any post-movement logic specific to this unit
            UpdateWalkingState(false);
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

    public bool CanMoveTo(int tileIndex)
    {
        var (tileData, isMoonTile) = TileDataHelper.Instance.GetTileData(tileIndex);
        if (tileData == null || !tileData.isPassable) return false;
        
        // Special case for moon tiles: workers can move freely on moon
        if (isMoonTile)
        {
            // Get movement cost
            if (currentMovePoints < tileData.movementCost) return false;
            
            // occupant check
            if (tileData.occupantId != 0 && tileData.occupantId != gameObject.GetInstanceID())
                return false;
                
            return true;
        }
        
        // For planet tiles: must be land
        if (!tileData.isLand) return false;
        
        // Check movement points
        if (currentMovePoints < tileData.movementCost) return false;
        
        // occupant check
        if (tileData.occupantId != 0 && tileData.occupantId != gameObject.GetInstanceID())
            return false;
            
        return true;
    }

    /// <summary>
    /// Handle mouse clicks on the worker unit
    /// </summary>
    void OnMouseDown()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null && 
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
        {
            // Click was on UI, ignore
            Debug.Log($"[WorkerUnit] Click on {data.unitName} ignored, was on UI.");
            return;
        }
        
        Debug.Log($"[WorkerUnit] Clicked on {data.unitName}. Owner: {owner?.civData?.civName ?? "Unknown"}");

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
                Debug.Log($"[WorkerUnit] Requested UnitInfoPanel for {data.unitName}");

                // Fallback notification if UnitInfoPanel is not available
                if (UIManager.Instance.unitInfoPanel == null || !UIManager.Instance.unitInfoPanel.activeInHierarchy)
                {
                    string msg = $"{data.unitName} (Worker)\nHealth: {currentHealth}/{data.baseHealth}\nWork: {currentWorkPoints}/{data.baseWorkPoints}\nMove: {currentMovePoints}/{data.baseMovePoints}\nAttack: {CurrentAttack}  Defense: {CurrentDefense}";
                    UIManager.Instance.ShowNotification(msg);
                }
            }
            else
            {
                Debug.LogError($"[WorkerUnit] UIManager.Instance is null. Cannot show notification for {data.unitName}.");
            }
        }
    }

    // Animation trigger methods
    
    public void PlayIdleAnimation()
    {
        if (unitAnimator == null) return;
        
        if (level > 1)
            unitAnimator.SetTrigger(idleExperiencedHash);
        else
            unitAnimator.SetTrigger(idleYoungHash);
    }
    
    public void PlayAttackAnimation()
    {
        if (unitAnimator != null && HasParameter(unitAnimator, attackHash))
            unitAnimator.SetTrigger(attackHash);
    }
    
    public void PlayHitAnimation()
    {
        if (unitAnimator != null && HasParameter(unitAnimator, hitHash))
            unitAnimator.SetTrigger(hitHash);
    }
    
    public void PlayDeathAnimation()
    {
        if (unitAnimator != null && HasParameter(unitAnimator, deathHash))
            unitAnimator.SetTrigger(deathHash);
    }
    
    public void PlayForageAnimation()
    {
        if (unitAnimator != null && HasParameter(unitAnimator, forageHash))
            unitAnimator.SetTrigger(forageHash);
    }
    
    public void PlayBuildAnimation()
    {
        if (unitAnimator != null && HasParameter(unitAnimator, buildHash))
            unitAnimator.SetTrigger(buildHash);
    }
    
    public void PlayFoundCityAnimation()
    {
        if (unitAnimator != null && HasParameter(unitAnimator, foundCityHash))
            unitAnimator.SetTrigger(foundCityHash);
    }
    
    public void UpdateWalkingState(bool isWalking)
    {
        if (unitAnimator == null) return;
        
        if (HasParameter(unitAnimator, isWalkingHash))
            unitAnimator.SetBool(isWalkingHash, isWalking);
            
        isMoving = isWalking;
    }
    
    // Utility to safely check parameter existence
    private bool HasParameter(Animator animator, int paramHash)
    {
        foreach (AnimatorControllerParameter param in animator.parameters)
        {
            if (param.nameHash == paramHash)
                return true;
        }
        return false;
    }

    // Called by Civilization when civ-wide bonuses (tech/culture) change.
    // Intentionally does not refill move/work mid-turn; clamps health to new cap.
    public void OnCivBonusesChanged()
    {
        // Recompute new max health based on current targeted bonuses
        var wb = AggregateWorkerBonusesLocal(owner, data);
        int newMax = Mathf.RoundToInt((data.baseHealth + wb.healthAdd) * (1f + wb.healthPct));
        int before = currentHealth;
        currentHealth = Mathf.Min(currentHealth, newMax);

        if (unitLabelInstance != null && before != currentHealth)
        {
            string ownerName = owner != null && owner.civData != null ? owner.civData.civName : "Unknown";
            unitLabelInstance.UpdateLabel(data.unitName, ownerName, currentHealth, data.baseHealth);
        }
        // Movement/work point caps will be applied at next ResetForNewTurn.
    }
}
