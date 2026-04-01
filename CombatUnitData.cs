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

[System.Serializable]
public struct CombatUnitVisualOverride
{
    [Tooltip("Civilization that uses this visual override.")]
    public CivData civ;

    [Tooltip("Override Addressables key for this civ's unit prefab. Leave empty to use the default unit prefab.")]
    public string addressableAddress;

    [Tooltip("Legacy toggle. A matching civ override now always uses the soldier display settings below.")]
    public bool overrideSoldierDisplay;

    [Range(1, 12)]
    public int soldierCount;

    public FormationType formationType;

    [Range(0.1f, 10f)]
    public float formationSpacing;

    public SoldierVariant[] soldierVariants;
}

[CreateAssetMenu(fileName = "NewCombatUnitData", menuName = "Data/Combat Unit Data")]
public class CombatUnitData : ScriptableObject
{
    [Header("Basic Info")]
    public string unitName;
    public CombatCategory unitType;

    [Header("Audio")]
    [Tooltip("Sound played when this unit is selected/clicked on the map. Leave empty for no sound.")]
    public AudioClip selectSound;
    [Tooltip("Random pitch variation range (±) applied to select sound for variety.")]
    [Range(0f, 0.3f)]
    public float selectPitchVariation = 0.08f;
    
    // MEMORY FIX: Lazy icon loading - icons are large textures that shouldn't load automatically
    // When ScriptableObjects load, they auto-load all referenced assets including sprites/textures
    // This can use 100s of MB when loading ALL unit data at startup
    [SerializeField, Tooltip("Icon loaded on-demand. Use GetIcon() to access.")]
    private Sprite _iconDirect;  // Renamed from 'icon' - still serialized for existing data
    
    // Lazy-loaded icon cache
    private Sprite _cachedIcon;
    private bool _iconLoaded = false;
    
    /// <summary>
    /// Get the unit icon, loading lazily if needed.
    /// MEMORY OPTIMIZATION: Icons are only loaded when actually displayed, not at startup.
    /// </summary>
    public Sprite icon
    {
        get
        {
            // Return direct reference if set (backwards compatibility)
            if (_iconDirect != null)
            {
                return _iconDirect;
            }
            
            // Already loaded via lazy path
            if (_iconLoaded)
            {
                return _cachedIcon;
            }
            
            // No icon available
            return null;
        }
        set
        {
            _iconDirect = value;
        }
    }
    
    /// <summary>
    /// Unload cached icon to free memory. Call this when unit UI is closed.
    /// </summary>
    public void UnloadIcon()
    {
        _cachedIcon = null;
        _iconLoaded = false;
    }
    
    [Header("Unit Prefab (Addressables)")]
    [Tooltip("The Addressable address for the unit prefab. If empty, uses unitName. " +
             "Check Addressables Groups window to see/set the address (e.g., 'Assets/Units/Monument Units/Bow Warrior').")]
    public string addressableAddress;
    
    /// <summary>
    /// Gets the address to use for loading the prefab via Addressables.
    /// Uses addressableAddress if set, otherwise falls back to unitName.
    /// </summary>
    public string GetAddressableKey()
    {
        return string.IsNullOrEmpty(addressableAddress) ? unitName : addressableAddress;
    }

    [Header("Category & Deployment")]
    public bool requiresAirport;
    public bool requiresSpaceport;
    [Header("Animal Behavior")]
    [Tooltip("Defines how this animal behaves towards civilization units (only applies to Animal category units)")]
    public AnimalBehaviorType animalBehavior = AnimalBehaviorType.Neutral;
    [Tooltip("Movement points per turn for animals on campaign map (1-3 typical)")]
    [Range(1, 5)]
    public int animalMovePoints = 1;
    [Header("Space Travel Capability (Stub Gates)")]
    [Tooltip("Defines how far this ship can travel. Only Interplanetary is implemented now.")]
    public TravelCapability travelCapability = TravelCapability.Interplanetary;

    [Header("Space Travel Stats")]
    [Tooltip("If > 0, use this as absolute speed (AU per turn). Overrides default speed model.")]
    public float spaceAUPerTurn = 0f;
    [Tooltip("Multiplier on default speed model (higher = faster). Used when AU/turn is 0.")]
    public float spaceSpeedMultiplier = 1.0f;

    [Header("Orbit Mechanics")]
    [Tooltip("Movement points consumed when entering orbit from surface.")]
    [Range(1, 10)]
    public int orbitEntryCost = 2;
    [Tooltip("Explicitly allow this unit to enter orbit. If false, legacy Spaceship category still allows orbit.")]
    public bool canEnterOrbit = false;
    [Tooltip("Movement points consumed when landing from orbit to surface.")]
    [Range(1, 10)]
    public int orbitExitCost = 1;
    [Tooltip("Movement cost per tile while moving in orbit (usually 1 — no terrain friction in space).")]
    [Range(1, 5)]
    public int orbitMovementCost = 1;
    [Tooltip("Whether this unit requires a spaceport on the tile to land (exit orbit). Spaceships typically do NOT.")]
    public bool requiresSpaceportToLand = false;
    [Tooltip("Whether this unit can bombard surface tiles from orbit.")]
    public bool canBombardSurface = false;
    [Tooltip("Extra vision range granted while in orbit (added on top of sightRange).")]
    [Range(0, 10)]
    public int orbitVisionBonus = 3;
    
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
    [Tooltip("If true, this unit ignores mosquito damage even on infected tiles.")]
    public bool immuneToMosquitoes = false;

    [Header("Action Points")]
    [Tooltip("How many attacks/actions this unit can perform per turn.")]
    [Range(0, 10)]
    public int attackPointsPerTurn = 1;

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
    [Tooltip("Base movement points for this combat unit (per turn). Set to 0 to opt-out of turn-based movement.)")]
    public int baseMovePoints = 0;
    public int baseDefense;
    public int baseHealth;
    public float baseRange;
    
    [Header("Vision")]
    [Tooltip("How many tiles this unit can see (reveals fog of war). Default is 2 tiles.")]
    [Range(1, 10)]
    public int sightRange = 2;

    [Header("Progression")]
    public int[] xpToNextLevel;
    public AbilityData[] abilitiesByLevel;

    [Header("Requirements")]
    [Tooltip("All these techs must be researched to unlock this unit")]
    public TechData[] requiredTechs;
    [Tooltip("All these cultures must be adopted to unlock this unit")]
    public CultureData[] requiredCultures;
    [Tooltip("At least one of these governments must be active to allow this unit (optional)")]
    public GovernmentData[] requiredGovernments;
    [Tooltip("All of these policies must be active to allow this unit (optional)")]
    public PolicyData[] requiredPolicies;

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
    // meleeEngageDuration removed (deprecated)

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

    [Header("Capture")]
    [Tooltip("If true this animal can be captured by capture actions and converted into herd counts")]
    public bool captureable = false;
    [Tooltip("If >0, number of herd 'animals' added to a herd when this unit is captured/killed and converted")]
    public int captureHerdCount = 0;
    [Tooltip("If set, explicit species this capture converts to (overrides name-matching).")]
    public Herd.HerdSpecies captureSpecies = Herd.HerdSpecies.Other;
    
    [Header("Per-Turn Consumption")]
    [Tooltip("Food this unit consumes each turn (subtracted from civilization stockpile)")]
    public int foodConsumptionPerTurn = 2;

    [Header("Multi-Soldier Display")]
    [Tooltip("Number of soldier figures displayed for this unit (1 = single model like today).")]
    [Range(1, 12)]
    public int soldierCount = 1;

    [Tooltip("Formation arrangement for multiple soldiers.")]
    public FormationType formationType = FormationType.Square;

    [Tooltip("Visual model variants to randomly pick from for each additional soldier. Each variant prefab should have the same equipment holder transforms (WeaponHolder, ShieldHolder, etc.).")]
    public SoldierVariant[] soldierVariants;

    [Tooltip("Spacing between soldiers in formation (world units).")]
    [Range(0.1f, 10f)]
    public float formationSpacing = 0.5f;

    [Header("Civilization Visual Overrides")]
    [Tooltip("Optional per-civilization visual overrides. Use these when the gameplay unit stays the same but the art should change by civ.")]
    public CombatUnitVisualOverride[] civVisualOverrides;

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
        // Government requirement (any-of)
        if (requiredGovernments != null && requiredGovernments.Length > 0)
        {
            bool govOk = false;
            foreach (var gov in requiredGovernments)
            {
                if (gov == null) continue;
                if (civ.currentGovernment == gov) { govOk = true; break; }
            }
            if (!govOk) return false;
        }

        // Policy requirements (all-of)
        if (requiredPolicies != null && requiredPolicies.Length > 0)
        {
            foreach (var pol in requiredPolicies)
            {
                if (pol == null) continue;
                if (!civ.activePolicies.Contains(pol)) return false;
            }
        }
        
        return true;
    }

    // No editor-time migration: legacy defaultMeleeWeapon removed.
    
    // Private cached prefabs (loaded on-demand via Addressables)
    private GameObject _cachedPrefab;
    private bool _isLoadingPrefab = false;
    private readonly System.Collections.Generic.Dictionary<string, GameObject> _cachedPrefabsByKey = new System.Collections.Generic.Dictionary<string, GameObject>();

    private bool TryGetVisualOverride(Civilization civ, out CombatUnitVisualOverride visualOverride)
    {
        if (civVisualOverrides != null && civ != null && civ.civData != null)
        {
            for (int i = 0; i < civVisualOverrides.Length; i++)
            {
                if (civVisualOverrides[i].civ == civ.civData)
                {
                    visualOverride = civVisualOverrides[i];
                    return true;
                }
            }
        }

        visualOverride = default;
        return false;
    }

    public string GetAddressableKey(Civilization civ)
    {
        if (TryGetVisualOverride(civ, out var visualOverride) && !string.IsNullOrWhiteSpace(visualOverride.addressableAddress))
            return visualOverride.addressableAddress;

        return GetAddressableKey();
    }

    public int GetSoldierCount(Civilization civ)
    {
        if (TryGetVisualOverride(civ, out var visualOverride))
            return Mathf.Max(1, visualOverride.soldierCount);

        return soldierCount;
    }

    public FormationType GetFormationType(Civilization civ)
    {
        if (TryGetVisualOverride(civ, out var visualOverride))
            return visualOverride.formationType;

        return formationType;
    }

    public SoldierVariant[] GetSoldierVariants(Civilization civ)
    {
        if (TryGetVisualOverride(civ, out var visualOverride))
            return visualOverride.soldierVariants;

        return soldierVariants;
    }

    public float GetFormationSpacing(Civilization civ)
    {
        if (TryGetVisualOverride(civ, out var visualOverride))
            return Mathf.Max(0.1f, visualOverride.formationSpacing);

        return formationSpacing;
    }
    
    /// <summary>
    /// Get the prefab, loading it on-demand from Addressables.
    /// Unit prefab must be marked as Addressable with address matching unitName.
    /// Returns null if prefab cannot be loaded - always check for null before using!
    /// </summary>
    public GameObject GetPrefab()
    {
        return GetPrefab(null);
    }

    public GameObject GetPrefab(Civilization civ)
    {
        // Validate unitName
        if (string.IsNullOrEmpty(unitName))
        {
            Debug.LogError($"[CombatUnitData] Unit name is null or empty! Cannot load prefab.");
            return null;
        }

        string addressKey = GetAddressableKey(civ);
        if (string.IsNullOrWhiteSpace(addressKey))
        {
            Debug.LogError($"[CombatUnitData] No Addressables key configured for unit '{unitName}'.");
            return null;
        }

        if (_cachedPrefabsByKey.TryGetValue(addressKey, out var cachedPrefab) && cachedPrefab != null)
            return cachedPrefab;
        
        // Load from Addressables
        if (AddressableUnitLoader.Instance != null)
        {
            GameObject loadedPrefab = AddressableUnitLoader.Instance.LoadUnitPrefabSync(addressKey);
            if (loadedPrefab != null)
            {
                _cachedPrefabsByKey[addressKey] = loadedPrefab;
                if (civ == null)
                    _cachedPrefab = loadedPrefab;
                return loadedPrefab;
            }
            else
            {
                Debug.LogError($"[CombatUnitData] AddressableUnitLoader returned null for unit '{unitName}' (address: '{addressKey}')");
            }
        }
        else
        {
            Debug.LogError($"[CombatUnitData] AddressableUnitLoader.Instance is NULL! Cannot load unit '{unitName}'. " +
                "Make sure Addressables package is installed and AddressableUnitLoader is initialized.");
        }
        
        Debug.LogError($"[CombatUnitData] Failed to load prefab for unit '{unitName}'. " +
            $"Make sure:\n" +
            $"1. The prefab is marked as Addressable in the Inspector (checkbox at top)\n" +
            $"2. Set the 'Addressable Address' field in this CombatUnitData to match the prefab's address in Addressables Groups\n" +
            $"   Current address being used: '{addressKey}'\n" +
            $"3. OR change the prefab's address in Addressables Groups window to just: '{unitName}'\n" +
            $"4. The prefab is in an Addressable group that's included in the build");
        return null;
    }
    
    /// <summary>
    /// Async version - use this when possible for better performance (doesn't block main thread)
    /// </summary>
    public void GetPrefabAsync(System.Action<GameObject> onComplete)
    {
        if (_cachedPrefab != null)
        {
            onComplete?.Invoke(_cachedPrefab);
            return;
        }

        if (_isLoadingPrefab)
        {
            Debug.LogWarning($"[CombatUnitData] Unit '{unitName}' is already loading, async call may be delayed");
            onComplete?.Invoke(null);
            return;
        }

        _isLoadingPrefab = true;

        if (AddressableUnitLoader.Instance != null)
        {
            string addressKey = GetAddressableKey();
            AddressableUnitLoader.Instance.LoadUnitPrefab(addressKey, (prefab) =>
            {
                _cachedPrefab = prefab;
                _isLoadingPrefab = false;
                onComplete?.Invoke(prefab);
            });
        }
        else
        {
            Debug.LogError($"[CombatUnitData] AddressableUnitLoader not found! Cannot load unit '{unitName}'.");
            _isLoadingPrefab = false;
            onComplete?.Invoke(null);
        }
    }
    
    /// <summary>
    /// Get model variants, loading them on-demand from Addressables if needed.
    /// Model variants must be marked as Addressable.
    /// </summary>
    public GameObject[] GetModelVariants()
    {
        // Model variants not currently implemented with Addressables
        // Can be added later if needed
        return null;
    }
    
    /// <summary>
    /// Get formation member prefab, loading it on-demand from Addressables if needed.
    /// Formation member prefab must be marked as Addressable.
    /// </summary>
    public GameObject GetFormationMemberPrefab()
    {
        // Formation member prefab not currently implemented with Addressables
        // Can use main unit prefab or implement separately if needed
        return GetPrefab();
    }
}
