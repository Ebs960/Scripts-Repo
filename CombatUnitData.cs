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

    [Header("Formation")]
    // Formation member prefab must be marked as Addressable. Loaded on-demand via Addressables.
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
    
    // Private cached prefabs (loaded on-demand via Addressables)
    private GameObject _cachedPrefab;
    private bool _isLoadingPrefab = false;
    
    /// <summary>
    /// Get the prefab, loading it on-demand from Addressables.
    /// Unit prefab must be marked as Addressable with address matching unitName.
    /// Returns null if prefab cannot be loaded - always check for null before using!
    /// </summary>
    public GameObject GetPrefab()
    {
        // If prefab is already cached, return it
        if (_cachedPrefab != null)
        {
            return _cachedPrefab;
        }
        
        // Validate unitName
        if (string.IsNullOrEmpty(unitName))
        {
            Debug.LogError($"[CombatUnitData] Unit name is null or empty! Cannot load prefab.");
            return null;
        }
        
        string addressKey = GetAddressableKey();
        
        // Load from Addressables
        if (AddressableUnitLoader.Instance != null)
        {
            _cachedPrefab = AddressableUnitLoader.Instance.LoadUnitPrefabSync(addressKey);
            if (_cachedPrefab != null)
            {
                return _cachedPrefab;
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
