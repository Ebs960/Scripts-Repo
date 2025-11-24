// Assets/Units/CombatUnitData.cs
using UnityEngine;

public enum CombatCategory
{
    Spearman, Swordsman, Axeman, Clubman, Artillery,
    Archer, Crossbowman, SpearThrower, Spaceship, Aircraft,
    Submarine, Ship, Boat, SeaCrawler,
    Gunman, Robot, Mutant, Cyborg,
    Driller, LavaSwimmer, Tank,
    Cavalry, HeavyCavalry, RangedCavalry,
    Dragoon, Animal
}

public enum TravelCapability
{
    OrbitOnly,          // Can only enter orbit around current planet (stub)
    PlanetAndMoon,      // Can travel between planet and its moon (stub)
    Interplanetary,     // Can travel to other planets within the same solar system (implemented)
    Interstellar,       // Can travel to other stars (stub)
    Intergalactic       // Can travel to other galaxies (stub)
}

public enum AnimalBehaviorType
{
    Neutral,    // Standard random movement (default)
    Predator,   // Actively hunts and attacks civilization units
    Prey        // Avoids civilization units but fights back when recently attacked
}

public enum FormationShape { Square, Circle, Wedge }

[CreateAssetMenu(fileName = "NewCombatUnitData", menuName = "Data/Combat Unit Data")]
public class CombatUnitData : ScriptableObject
{
    [Header("Basic Info")]
    public string unitName;
    public CombatCategory unitType;
    public Sprite icon;
    
    [Header("Prefab Paths (Memory Optimized)")]
    [Tooltip("Path to prefab in Resources folder (e.g., 'Units/Swordsman'). Prefabs load on-demand to save memory.")]
    public string prefabPath;
    [Tooltip("Paths to model variants (comma-separated or array). Loaded on-demand.")]
    public string[] modelVariantPaths;
    
    [Header("Legacy Prefab References (Auto-loaded, use prefabPath instead)")]
    [Tooltip("DEPRECATED: Use prefabPath instead. This field auto-loads prefabs and wastes memory.")]
    [System.Obsolete("Use prefabPath and GetPrefab() instead to prevent auto-loading")]
    public GameObject prefab;
    [Tooltip("DEPRECATED: Use modelVariantPaths instead.")]
    [System.Obsolete("Use modelVariantPaths instead to prevent auto-loading")]
    public GameObject[] modelVariants;

    [Header("Formation")]
    [Tooltip("Path to formation member prefab in Resources folder. Loaded on-demand.")]
    public string formationMemberPrefabPath;
    [Tooltip("DEPRECATED: Use formationMemberPrefabPath instead.")]
    [System.Obsolete("Use formationMemberPrefabPath instead to prevent auto-loading")]
    public GameObject formationMemberPrefab;
    [Range(1, 100)] public int formationSize = 9;
    public FormationShape formationShape = FormationShape.Square;
    [Range(0.5f, 5f)] public float formationSpacing = 1.5f;

    [Header("Category & Deployment")]
    public bool requiresAirport;
    public bool requiresSpaceport;
    [Header("Animal Behavior")]
    [Tooltip("Defines how this animal behaves towards civilization units (only applies to Animal category units)")]
    public AnimalBehaviorType animalBehavior = AnimalBehaviorType.Neutral;
    [Header("Space Travel Capability (Stub Gates)")]
    [Tooltip("Defines how far this ship can travel. Only Interplanetary is implemented now.")]
    public TravelCapability travelCapability = TravelCapability.Interplanetary;

    [Header("Space Travel Stats")]
    [Tooltip("If > 0, use this as absolute speed (AU per turn). Overrides default speed model.")]
    public float spaceAUPerTurn = 0f;
    [Tooltip("Multiplier on default speed model (higher = faster). Used when AU/turn is 0.")]
    public float spaceSpeedMultiplier = 1.0f;
    
    [Header("Transport Capabilities")]
    [Tooltip("Whether this unit can transport other units")]
    public bool isTransport = false;
    [Tooltip("Maximum number of units this transport can carry")]
    [Range(1, 10)]
    public int transportCapacity = 3;
    [Tooltip("Whether this transport can travel to the moon (only spaceships)")]
    public bool canTravelToMoon = false;
    
    [Header("Naval Requirements")]
    [Tooltip("Must control at least one coastal tile (coast, seas, ocean)")]
    public bool requiresCoastalCity = false;
    [Tooltip("Must have a Harbor building in the city")]
    public bool requiresHarbor = false;

    [Header("Combat System")]
    [Tooltip("Whether this unit can attack air units (Aircraft)")]
    public bool canAttackAir = false;
    [Tooltip("Whether this unit can attack space units (Spaceship)")]
    public bool canAttackSpace = false;
    [Tooltip("Whether this unit can attack underwater units (Submarine/SeaCrawler)")]
    public bool canAttackUnderwater = false;
    [Tooltip("Whether this unit can perform a counter-attack when attacked")]
    public bool canCounterAttack = false;
    [Tooltip("Base morale for the unit")]
    public int baseMorale = 100;
    [Tooltip("Morale lost per HP lost")]
    public int moraleLostPerHealth = 1;
    [Tooltip("Morale gained when killing an enemy unit")]
    public int moraleGainOnKill = 10;
    [Tooltip("Charge bonus multiplier for melee attacks (only applies when charging). Higher values = more charge damage. Cavalry units typically have higher values (1.5-2.0), infantry lower (1.2-1.5).")]
    [Range(1.0f, 3.0f)]
    public float chargeBonusMultiplier = 1.5f;
    
    [Header("Fatigue System")]
    [Tooltip("Rate at which fatigue increases per second while moving (0-100 scale)")]
    [Range(0f, 10f)]
    public float fatigueRateMoving = 0.25f; // Tired after 400 seconds of continuous movement
    [Tooltip("Rate at which fatigue increases per second while fighting (0-100 scale)")]
    [Range(0f, 20f)]
    public float fatigueRateFighting = 0.5f; // Tired after 200 seconds of continuous fighting
    [Tooltip("Rate at which fatigue recovers per second while resting (0-100 scale)")]
    [Range(0f, 20f)]
    public float fatigueRecoveryRate = 10f; // Full recovery in 10 seconds
    [Tooltip("Attack penalty at 100% fatigue (0.5 = 50% attack damage)")]
    [Range(0f, 1f)]
    public float fatigueAttackPenalty = 0.5f;
    [Tooltip("Defense penalty at 100% fatigue (0.5 = 50% defense)")]
    [Range(0f, 1f)]
    public float fatigueDefensePenalty = 0.5f;
    [Tooltip("Speed penalty at 100% fatigue (0.5 = 50% move speed)")]
    [Range(0f, 1f)]
    public float fatigueSpeedPenalty = 0.5f;
    [Tooltip("Fatigue gained instantly when executing a charge attack")]
    [Range(0f, 50f)]
    public float chargeInstantFatigue = 25f; // Cavalry get more tired from charges
    
    [Header("Ammunition System (Ranged Units)")]
    [Tooltip("Is this a ranged unit that uses ammunition?")]
    public bool isRangedUnit = false;
    [Tooltip("Maximum ammunition this unit carries (0 = infinite)")]
    [Range(0, 100)]
    public int maxAmmo = 30; // Default: 30 arrows/bolts
    [Tooltip("Can this unit switch to melee when out of ammo?")]
    public bool canSwitchToMelee = true;
    [Tooltip("Melee attack penalty when out of ammo (0.5 = 50% attack damage in melee)")]
    [Range(0f, 1f)]
    public float outOfAmmoMeleePenalty = 0.5f;

    [Header("Weather")]
    [Tooltip("If true, this unit takes weather attrition in severe seasons (e.g., winter)")]
    public bool takesWeatherDamage = true;

    [Header("Production & Purchase")]
    public int productionCost;
    public int goldCost;
    public ResourceData[] requiredResources;
    public Biome[] requiredTerrains;
    
    [Header("Worker Construction")]
    [Tooltip("If true, workers can construct this unit on the map using work points.")]
    public bool buildableByWorker = false;
    [Tooltip("Total work points required by workers to construct this unit on a tile.")]
    public int workerWorkCost = 40;

    [Header("Base Stats")]
    public int baseAttack;
    public int baseDefense;
    public int baseHealth;
    public float baseRange;

    [Header("Progression")]
    public int[] xpToNextLevel;
    public AbilityData[] abilitiesByLevel;

    [Header("Requirements")]
    [Tooltip("All these techs must be researched to unlock this unit")]
    public TechData[] requiredTechs;
    [Tooltip("All these cultures must be adopted to unlock this unit")]
    public CultureData[] requiredCultures;

    [Header("Unit Limits")]
    [Tooltip("Maximum number of this unit type a civilization can have (-1 = unlimited)")]
    public int unitLimit = -1;
    [Tooltip("Unique identifier for units that share the same limit (leave empty for individual limits)")]
    public string limitCategory = "";

    [Header("Default Equipment")]
    [Tooltip("Default weapon equipped by this unit (optional)")]
    public EquipmentData defaultWeapon;
    [Tooltip("Default shield equipped by this unit (optional)")]
    public EquipmentData defaultShield;
    [Tooltip("Default armor equipped by this unit (optional)")]
    public EquipmentData defaultArmor;
    [Tooltip("Default miscellaneous equipment equipped by this unit (optional)")]
    public EquipmentData defaultMiscellaneous;

    [Header("Weapon Slots")]
    // defaultWeapon is the authoritative melee weapon. Legacy 'defaultMeleeWeapon' removed.
    [Tooltip("Default projectile/ranged weapon equipped by this unit (used when firing) ")]
    public EquipmentData defaultProjectileWeapon;
    [Tooltip("How many seconds a unit stays 'engaged in melee' after receiving a melee hit before reverting to ranged behavior.")]
    public float meleeEngageDuration = 8f;

    [Header("Yield")]
    public int foodOnKill;
    
    [Header("Per-Turn Yields")]
    [Tooltip("Flat yields this unit provides each turn while alive (added to owning civilization)")]
    public int foodPerTurn;
    public int goldPerTurn;
    public int sciencePerTurn;
    public int culturePerTurn;
    public int policyPointsPerTurn;
    public int faithPerTurn;
    
    [Header("Per-Turn Consumption")]
    [Tooltip("Food this unit consumes each turn (subtracted from civilization stockpile)")]
    public int foodConsumptionPerTurn = 2;
    
    /// <summary>
    /// Checks if all requirements (techs, cultures) are met for this unit
    /// </summary>
    public bool AreRequirementsMet(Civilization civ)
    {
        if (civ == null) return false;
        
        // Check tech requirements
        if (requiredTechs != null && requiredTechs.Length > 0)
        {
            foreach (var tech in requiredTechs)
            {
                if (tech == null) continue;
                
                // Check if this tech has been researched
                if (!civ.researchedTechs.Contains(tech))
                    return false;
            }
        }
        
        // Check culture requirements
        if (requiredCultures != null && requiredCultures.Length > 0)
        {
            foreach (var culture in requiredCultures)
            {
                if (culture == null) continue;
                
                // Check if this culture has been adopted
                if (!civ.researchedCultures.Contains(culture))
                    return false;
            }
        }
        
        return true;
    }

    // No editor-time migration: legacy defaultMeleeWeapon removed.
    
    // Private cached prefabs (loaded on-demand, not auto-loaded)
    private GameObject _cachedPrefab;
    private GameObject[] _cachedModelVariants;
    private GameObject _cachedFormationMemberPrefab;
    
    /// <summary>
    /// Get the prefab, loading it on-demand from prefabPath if needed.
    /// This prevents Unity from auto-loading prefabs when ScriptableObjects are loaded.
    /// Returns null if prefab cannot be loaded - always check for null before using!
    /// </summary>
    public GameObject GetPrefab()
    {
        // If prefab is already cached, return it
        if (_cachedPrefab != null) return _cachedPrefab;
        
        // If we have a path, load from it
        if (!string.IsNullOrEmpty(prefabPath))
        {
            // Remove .prefab extension if present (Resources.Load doesn't need it)
            string pathToLoad = prefabPath;
            if (pathToLoad.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
            {
                pathToLoad = pathToLoad.Substring(0, pathToLoad.Length - 7);
            }
            
            _cachedPrefab = Resources.Load<GameObject>(pathToLoad);
            if (_cachedPrefab != null)
            {
                // CRITICAL DEBUG: Log ALL components on the loaded prefab to see what's actually there
                Debug.Log($"[CombatUnitData] Prefab loaded from '{pathToLoad}' for unit '{unitName}':");
                Debug.Log($"[CombatUnitData]   Prefab name: {_cachedPrefab.name}");
                Debug.Log($"[CombatUnitData]   Prefab instance ID: {_cachedPrefab.GetInstanceID()}");
                
                // Check for Animator
                var animator = _cachedPrefab.GetComponent<Animator>();
                if (animator != null)
                {
                    Debug.Log($"[CombatUnitData]   Animator component: EXISTS");
                    Debug.Log($"[CombatUnitData]   Animator controller: {(animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "NULL - MISSING!")}");
                    Debug.Log($"[CombatUnitData]   Animator avatar: {(animator.avatar != null ? animator.avatar.name : "NULL")}");
                }
                else
                {
                    Debug.LogWarning($"[CombatUnitData]   Animator component: MISSING!");
                }
                
                // Check for CombatUnit
                var combatUnit = _cachedPrefab.GetComponent<CombatUnit>();
                if (combatUnit != null)
                {
                    Debug.Log($"[CombatUnitData]   CombatUnit component: EXISTS");
                }
                else
                {
                    Debug.LogWarning($"[CombatUnitData]   CombatUnit component: MISSING!");
                }
                
                // Component listing debug removed - not needed for production
                // var allComponents = _cachedPrefab.GetComponents<Component>();
                // Debug.Log($"[CombatUnitData]   Total components on root: {allComponents.Length}");
                // ... component listing code removed
                
                return _cachedPrefab;
            }
            else
            {
                Debug.LogWarning($"[CombatUnitData] Could not load prefab from path: '{pathToLoad}' (original: '{prefabPath}') for unit '{unitName}'. " +
                    $"Check that the prefab exists at Resources/{pathToLoad}.prefab");
            }
        }
        else
        {
            Debug.LogWarning($"[CombatUnitData] Unit '{unitName}' has no prefabPath set. " +
                $"Please set prefabPath in the ScriptableObject to prevent auto-loading. " +
                $"Falling back to legacy prefab field (which auto-loads).");
        }
        
        // Fallback: try legacy prefab field (for backward compatibility with old assets)
        // WARNING: This defeats the memory optimization - prefabs will auto-load!
        #pragma warning disable CS0618 // Suppress obsolete warning for backward compatibility
        if (prefab != null)
        {
            _cachedPrefab = prefab;
            Debug.LogWarning($"[CombatUnitData] Unit '{unitName}' is using legacy prefab field. " +
                $"Please migrate to prefabPath to prevent auto-loading.");
            return _cachedPrefab;
        }
        #pragma warning restore CS0618
        
        // Check legacy prefab for error message (suppress warning)
        #pragma warning disable CS0618
        bool legacyPrefabExists = prefab != null;
        #pragma warning restore CS0618
        
        Debug.LogError($"[CombatUnitData] Unit '{unitName}' has no prefab available. " +
            $"prefabPath: '{prefabPath}', legacy prefab: {(legacyPrefabExists ? "exists" : "null")}");
        return null;
    }
    
    /// <summary>
    /// Get model variants, loading them on-demand from paths if needed.
    /// </summary>
    public GameObject[] GetModelVariants()
    {
        // If already cached, return them
        if (_cachedModelVariants != null && _cachedModelVariants.Length > 0) return _cachedModelVariants;
        
        // Load from paths if available
        if (modelVariantPaths != null && modelVariantPaths.Length > 0)
        {
            _cachedModelVariants = new GameObject[modelVariantPaths.Length];
            for (int i = 0; i < modelVariantPaths.Length; i++)
            {
                if (!string.IsNullOrEmpty(modelVariantPaths[i]))
                {
                    _cachedModelVariants[i] = Resources.Load<GameObject>(modelVariantPaths[i]);
                }
            }
            return _cachedModelVariants;
        }
        
        // Fallback to legacy field (for backward compatibility)
        #pragma warning disable CS0618
        if (modelVariants != null && modelVariants.Length > 0)
        {
            _cachedModelVariants = modelVariants;
            return _cachedModelVariants;
        }
        #pragma warning restore CS0618
        
        return null;
    }
    
    /// <summary>
    /// Get formation member prefab, loading it on-demand from path if needed.
    /// </summary>
    public GameObject GetFormationMemberPrefab()
    {
        // If already cached, return it
        if (_cachedFormationMemberPrefab != null) return _cachedFormationMemberPrefab;
        
        // Load from path if available
        if (!string.IsNullOrEmpty(formationMemberPrefabPath))
        {
            _cachedFormationMemberPrefab = Resources.Load<GameObject>(formationMemberPrefabPath);
            return _cachedFormationMemberPrefab;
        }
        
        // Fallback to legacy field (for backward compatibility)
        #pragma warning disable CS0618
        if (formationMemberPrefab != null)
        {
            _cachedFormationMemberPrefab = formationMemberPrefab;
            return _cachedFormationMemberPrefab;
        }
        #pragma warning restore CS0618
        
        return null;
    }
}
