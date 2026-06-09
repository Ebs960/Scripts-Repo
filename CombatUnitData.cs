// Assets/Scripts Repo/CombatUnitData.cs
using UnityEngine;

public enum CombatCategory
{
    Spearman = 0,
    Swordsman = 1,
    Axeman = 2,
    Clubman = 3,
    Artillery = 4,
    Archer = 5,
    Crossbowman = 6,
    SpearThrower = 7,
    Spaceship = 8,
    Aircraft = 9,
    Submarine = 10,
    // Preserve original 'Ship' numeric index (11) by using HeavyShip here.
    HeavyShip = 11,
    Boat = 12,
    SeaCrawler = 13,
    Gunman = 14,
    Robot = 15,
    Mutant = 16,
    Cyborg = 17,
    Driller = 18,
    LavaSwimmer = 19,
    Tank = 20,
    Cavalry = 21,
    HeavyCavalry = 22,
    RangedCavalry = 23,
    Dragoon = 24,
    Animal = 25,
    // New ship specializations (added with high explicit values to avoid shifting existing indices)
    LightShip = 26,
    TorpedoShip = 27
    ,
    // New air specializations (added without shifting existing indices)
    Fighter = 28,
    Bomber = 29,
    GroundAttack = 30,
    // New ground specialization
    MachineGun = 31,
    // Carrier specializations
    AircraftCarrier = 32,
    SpaceCarrier = 33,
    // Anti-air specialization
    AntiAircraft = 34,

    Helicopter = 35,
    // Amphibious aircraft that can use air systems while being allowed to base/spawn from water-surface tiles.
    SeaPlane = 36
}

public enum CombatTargetDomain
{
    Ground,
    NavalSurface,
    Underwater,
    Air,
    Space,
    City
}

public enum TravelCapability
{
    OrbitOnly,          // Can only enter orbit around current planet
    PlanetAndMoon,      // Can travel between planet and its moon
    Interplanetary,     // Can travel to other planets within the same solar system
    Interstellar,       // Can travel to other stars
    Intergalactic       // Can travel to other galaxies
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

    [Tooltip("A matching civ override always uses the soldier display settings below.")]
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
             "Check Addressables Groups window to see/set the address (e.g., 'Assets/Scripts Repo/Units/Monument Units/Bow Warrior').")]
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

    [Header("Layer Operation")]
    [Tooltip("Usual gameplay layer for this unit. If layer masks below are left as None, legacy category-based defaults are inferred.")]
    public TileLayer nativeLayer = TileLayer.Surface;
    [Tooltip("Layers this unit is allowed to occupy. None means infer from unit category for backwards compatibility.")]
    public UnitLayerMask allowedLayers = UnitLayerMask.None;
    [Tooltip("Layers this unit may be born/placed on. None means infer from unit category and tile type.")]
    public UnitLayerMask spawnLayers = UnitLayerMask.None;
    [Tooltip("Allow explicit layer transitions between surface water and underwater, e.g. submarines diving/surfacing.")]
    public bool canTransitionSurfaceUnderwater = false;
    [Tooltip("Allow explicit layer transitions between surface and atmosphere, e.g. aircraft launching/landing.")]
    public bool canTransitionSurfaceAtmosphere = false;

    [Header("Air Jump Deployment")]
    [Tooltip("Allow this combat unit to redeploy by air drop to another surface tile on the same planet.")]
    public bool canAirJump = false;
    [Tooltip("Maximum tile distance for air-jump/paratrooper deployment.")]
    [Range(0, 50)] public int airJumpRange = 0;
    [Tooltip("Whether air-jump deployment consumes this unit's action for the turn.")]
    public bool airJumpConsumesAction = true;
    [Tooltip("Vertical height used for the air-drop animation arc.")]
    [Range(0f, 50f)] public float airJumpDropHeight = 12f;
    [Tooltip("Seconds used for the air-drop animation.")]
    [Range(0f, 5f)] public float airJumpAnimationDuration = 0.8f;
    [Tooltip("Optional visual effect spawned at the pickup/start tile when air jump begins.")]
    public GameObject airJumpLaunchVFX;
    [Tooltip("Optional visual effect spawned at the destination tile when the unit lands.")]
    public GameObject airJumpLandingVFX;

    [Header("Regeneration")]
    [Tooltip("Guaranteed healing at the start of this unit's turn, as a percent of max HP. This applies even if the unit moved or attacked last turn.")]
    [Range(0f, 100f)] public float guaranteedRegenPercentPerTurn = 0f;
    [Header("Animal Behavior")]
    [Tooltip("Defines how this animal behaves towards civilization units (only applies to Animal category units)")]
    public AnimalBehaviorType animalBehavior = AnimalBehaviorType.Neutral;
    [Tooltip("Movement points per turn for animals on campaign map (1-3 typical)")]
    [Range(1, 5)]
    public int animalMovePoints = 1;
    [Header("Animal Spawn Regions")]
    [Tooltip("If true, this animal can spawn in the Old World.")]
    public bool canSpawnInOldWorld = true;
    [Tooltip("If true, this animal can spawn in the primary New World.")]
    public bool canSpawnInNewWorld = true;
    [Tooltip("If true, this animal can spawn in New World II / the secondary New World.")]
    public bool canSpawnInNewWorldSecondary = true;
    [Header("Animal Map-Type Restrictions")]
    [Tooltip("If false, this animal will NOT spawn on Standard (normal) maps.")]
    public bool canSpawnOnStandardMaps = true;
    [Tooltip("If false, this animal will NOT spawn on IceWorld (frozen/arctic/glacial) maps.")]
    public bool canSpawnOnFrozenMaps = true;
    [Tooltip("If false, this animal will NOT spawn on Demonic (hellish terrain) maps.")]
    public bool canSpawnOnDemonicMaps = true;
    [Tooltip("If false, this animal will NOT spawn on Infernal (volcanic/fire-themed) maps.")]
    public bool canSpawnOnInfernalMaps = true;
    [Header("Space Travel Capability")]
    [Tooltip("Defines how far this ship can travel.")]
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
    [Tooltip("Explicitly allow this unit to enter orbit.")]
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
    [Tooltip("Whether this transport can carry regular land/naval combat units. Disable this for pure aircraft or spaceship carriers.")]
    public bool canTransportCombatUnits = true;
    [Tooltip("Whether this transport can act as an aircraft carrier and base Aircraft/Fighter/Bomber/GroundAttack/Helicopter/SeaPlane units.")]
    public bool canBaseAircraft = false;
    [Tooltip("Whether this transport can act as a spacecraft carrier and base Spaceship units.")]
    public bool canBaseSpaceships = false;
    [Tooltip("Whether this transport can travel to the moon (only spaceships)")]
    public bool canTravelToMoon = false;

    [Header("Missile Capabilities")]
    [Tooltip("Whether this unit can carry and launch missiles (e.g. missile submarine, missile cruiser, MLRS).")]
    public bool canStoreMissiles = false;
    [Tooltip("Maximum number of missiles this unit can carry.")]
    [Range(0, 20)]
    public int maxMissileStorage = 0;
    [Tooltip("Specific missile types this unit is allowed to carry. Leave empty to allow all types.")]
    public MissileData[] allowedMissileTypes;


    [Header("Aircraft Missions & Air Defense")]
    [Tooltip("Whether this unit can launch aircraft missions through AircraftMissionManager. Air-category units are also accepted by default for mission validation.")]
    public bool canLaunchAirMissions = false;
    [Tooltip("Allow this aircraft to attack units on a target tile.")]
    public bool canAirStrike = false;
    [Tooltip("Allow this aircraft to attack city defenses from the air.")]
    public bool canBombardCitiesFromAir = false;
    [Tooltip("Allow this aircraft to perform recon sweeps without dealing damage.")]
    public bool canReconAirMission = false;
    [Tooltip("Allow this unit to scramble and fire on hostile aircraft missions within interceptionRange.")]
    public bool canInterceptAirMissions = false;
    [Tooltip("Allow this unit to provide gated passive anti-air / missile-defense fire within antiAirRange.")]
    public bool canProvideAntiAir = false;
    [Tooltip("Maximum tile distance for launched aircraft missions. If 0, CurrentRange is used as a fallback.")]
    [Range(0, 50)] public int airMissionRange = 0;
    [Tooltip("Maximum tile distance at which this unit can intercept hostile aircraft missions.")]
    [Range(0, 25)] public int interceptionRange = 3;
    [Tooltip("Maximum tile distance at which this unit provides anti-air or missile defense.")]
    [Range(0, 25)] public int antiAirRange = 2;
    [Tooltip("Base chance for interceptor defensive fire to hit incoming aircraft before combat modifiers. A hit damages the aircraft; only lethal damage stops the mission.")]
    [Range(0f, 1f)] public float interceptionChance = 0.45f;
    [Tooltip("Base chance for local anti-air / missile-defense fire to hit before combat modifiers. A hit damages the target; only lethal damage stops it.")]
    [Range(0f, 1f)] public float antiAirInterceptionChance = 0.25f;
    [Tooltip("Flat damage dealt by passive anti-air. If 0, CurrentAirAttack is used.")]
    public int antiAirDamage = 0;

    [Header("Space Missions & Space Defense")]
    [Tooltip("Whether this unit can launch space/orbital missions through SpaceMissionManager. Space-category units and units in orbit are also accepted by default.")]
    public bool canLaunchSpaceMissions = false;
    [Tooltip("Allow this unit to attack units on a target tile from space/orbit.")]
    public bool canSpaceStrike = false;
    [Tooltip("Allow this unit to bombard city defenses from space/orbit.")]
    public bool canBombardCitiesFromSpace = false;
    [Tooltip("Allow this unit to perform space recon sweeps without dealing damage.")]
    public bool canReconSpaceMission = false;
    [Tooltip("Allow this unit to scramble and fire on hostile space missions within spaceInterceptionRange.")]
    public bool canInterceptSpaceMissions = false;
    [Tooltip("Allow this unit to provide gated passive anti-space / orbital-defense fire within antiSpaceRange.")]
    public bool canProvideAntiSpace = false;
    [Tooltip("Maximum tile distance for launched space missions. If 0, CurrentRange is used as a fallback.")]
    [Range(0, 50)] public int spaceMissionRange = 0;
    [Tooltip("Maximum tile distance at which this unit can intercept hostile space missions.")]
    [Range(0, 25)] public int spaceInterceptionRange = 3;
    [Tooltip("Maximum tile distance at which this unit provides anti-space/orbital defense.")]
    [Range(0, 25)] public int antiSpaceRange = 2;
    [Tooltip("Base chance for space interceptor defensive fire to hit before combat modifiers. A hit damages the spacecraft; only lethal damage stops the mission.")]
    [Range(0f, 1f)] public float spaceInterceptionChance = 0.45f;
    [Tooltip("Base chance for local anti-space/orbital-defense fire to hit before combat modifiers. A hit damages the target; only lethal damage stops it.")]
    [Range(0f, 1f)] public float antiSpaceInterceptionChance = 0.25f;
    [Tooltip("Flat damage dealt by passive anti-space defense. If 0, CurrentSpaceAttack is used.")]
    public int antiSpaceDamage = 0;

    [Header("Naval Requirements")]
    [Tooltip("Must control at least one coastal tile (coast, seas, ocean)")]
    public bool requiresCoastalCity = false;
    [Tooltip("Must have a Harbor building in the city")]
    public bool requiresHarbor = false;

    [Header("Combat System")]
    [Tooltip("Whether this unit can attack air units (Aircraft/Fighter/Bomber/GroundAttack/Helicopter/SeaPlane)")]
    public bool canAttackAir = false;
    [Tooltip("Whether this unit can attack space units (Spaceship or units in Orbit)")]
    public bool canAttackSpace = false;
    [Tooltip("Whether this unit can attack naval surface units (boats/ships). Separate from underwater attack capability.")]
    public bool canAttackNavalSurface = true;
    [Tooltip("Whether this unit can attack underwater units/targets (Submarine/SeaCrawler/undersea targets). Separate from naval surface attack capability.")]
    public bool canAttackUnderwater = false;
    [Tooltip("Whether this unit can perform a counter-attack when attacked")]
    public bool canCounterAttack = false;
    
    [System.Serializable]
    public struct CategoryBonus
    {
        public CombatCategory targetCategory;
        [Tooltip("Flat attack bonus against this category (added to attack)")]
        public int attackBonus;
        [Tooltip("Percent attack bonus (0.2 = +20%) applied multiplicatively")]
        public float attackPercent;
        [Tooltip("Flat defense bonus when attacking this category (applied to defender's defense calculation)")]
        public int defenseBonus;
        [Tooltip("Percent defense bonus (0.1 = +10%)")]
        public float defensePercent;
    }

    [System.Serializable]
    public struct SpecificUnitBonus
    {
        public CombatUnitData targetUnit;
        public int attackBonus;
        public float attackPercent;
        public int defenseBonus;
        public float defensePercent;
    }

    [Header("Bonuses Against")]
    [Tooltip("Flat/percent bonuses that apply when this unit attacks units of a specific category.")]
    public CategoryBonus[] bonusesAgainstCategories;

    [Tooltip("Flat/percent bonuses that apply when this unit attacks a specific unit type (CombatUnitData reference).")]
    public SpecificUnitBonus[] bonusesAgainstUnits;

    /// <summary>
    /// Returns true if the given category is considered a naval-type (ships, boats, subs, crawlers).
    /// Use this to preserve previous logic that treated 'Ship' specially.
    /// </summary>
    public static bool IsNavalCategory(CombatCategory cat)
    {
        return IsNavalSurfaceCategory(cat) || IsUnderwaterCategory(cat);
    }

    /// <summary>
    /// Returns true if the category represents a ship/boat on the sea surface.
    /// Keep this separate from underwater categories so aircraft/naval weapons can
    /// distinguish surface naval attacks from anti-sub/undersea attacks.
    /// </summary>
    public static bool IsNavalSurfaceCategory(CombatCategory cat)
    {
        return cat == CombatCategory.HeavyShip
               || cat == CombatCategory.LightShip
               || cat == CombatCategory.TorpedoShip
               || cat == CombatCategory.Boat;
    }

    /// <summary>
    /// Returns true if the category represents an underwater unit/target.
    /// </summary>
    public static bool IsUnderwaterCategory(CombatCategory cat)
    {
        return cat == CombatCategory.Submarine
               || cat == CombatCategory.SeaCrawler;
    }

    /// <summary>
    /// Returns true if the given category is an air-type (aircraft/fighter/bomber/ground-attack/helicopter/sea-plane)
    /// </summary>
    public static bool IsAirCategory(CombatCategory cat)
    {
        return cat == CombatCategory.Aircraft
               || cat == CombatCategory.Fighter
               || cat == CombatCategory.Bomber
               || cat == CombatCategory.GroundAttack
               || cat == CombatCategory.Helicopter
               || cat == CombatCategory.SeaPlane;
    }

    public static UnitLayerMask GetDefaultAllowedLayersForCategory(CombatCategory cat)
    {
        if (cat == CombatCategory.Submarine)
            return UnitLayerMask.Surface | UnitLayerMask.Underwater;
        if (cat == CombatCategory.SeaCrawler)
            return UnitLayerMask.Underwater;
        if (cat == CombatCategory.SeaPlane)
            return UnitLayerMask.Surface | UnitLayerMask.Atmosphere;
        if (IsAirCategory(cat))
            return UnitLayerMask.Atmosphere;
        if (IsSpaceCategory(cat))
            return UnitLayerMask.Surface | UnitLayerMask.Orbit;
        return UnitLayerMask.Surface;
    }

    public static UnitLayerMask GetDefaultSpawnLayersForCategory(CombatCategory cat)
    {
        if (cat == CombatCategory.Submarine || cat == CombatCategory.SeaCrawler)
            return UnitLayerMask.Underwater;
        if (cat == CombatCategory.SeaPlane)
            return UnitLayerMask.Surface;
        if (IsAirCategory(cat))
            return UnitLayerMask.Atmosphere;
        if (IsSpaceCategory(cat))
            return UnitLayerMask.Surface | UnitLayerMask.Orbit;
        return UnitLayerMask.Surface;
    }

    public static TileLayer GetDefaultNativeLayerForCategory(CombatCategory cat)
    {
        if (cat == CombatCategory.Submarine || cat == CombatCategory.SeaCrawler)
            return TileLayer.Underwater;
        if (IsAirCategory(cat))
            return TileLayer.Atmosphere;
        if (IsSpaceCategory(cat))
            return TileLayer.Orbit;
        return TileLayer.Surface;
    }

    public UnitLayerMask EffectiveAllowedLayers => allowedLayers != UnitLayerMask.None ? allowedLayers : GetDefaultAllowedLayersForCategory(unitType);
    public UnitLayerMask EffectiveSpawnLayers => spawnLayers != UnitLayerMask.None ? spawnLayers : GetDefaultSpawnLayersForCategory(unitType);
    public TileLayer EffectiveNativeLayer
    {
        get
        {
            if (allowedLayers != UnitLayerMask.None || spawnLayers != UnitLayerMask.None)
                return nativeLayer;
            return GetDefaultNativeLayerForCategory(unitType);
        }
    }

    public bool CanOccupyLayer(TileLayer layer) => LayerConversion.MaskContains(EffectiveAllowedLayers, layer);
    public bool CanSpawnOnLayer(TileLayer layer) => LayerConversion.MaskContains(EffectiveSpawnLayers, layer) && CanOccupyLayer(layer);

    public bool CanTransitionBetweenLayers(TileLayer from, TileLayer to)
    {
        if (from == to) return CanOccupyLayer(from);
        if (!CanOccupyLayer(from) || !CanOccupyLayer(to)) return false;
        if ((from == TileLayer.Surface && to == TileLayer.Underwater) || (from == TileLayer.Underwater && to == TileLayer.Surface))
            return canTransitionSurfaceUnderwater || unitType == CombatCategory.Submarine;
        if ((from == TileLayer.Surface && to == TileLayer.Atmosphere) || (from == TileLayer.Atmosphere && to == TileLayer.Surface))
            return canTransitionSurfaceAtmosphere || IsAirCategory(unitType);
        if ((from == TileLayer.Surface && to == TileLayer.Orbit) || (from == TileLayer.Orbit && to == TileLayer.Surface))
            return canEnterOrbit || IsSpaceCategory(unitType);
        return false;
    }

    /// <summary>
    /// Returns true if the category represents a space-capable combat unit.
    /// </summary>
    public static bool IsSpaceCategory(CombatCategory cat)
    {
        return cat == CombatCategory.Spaceship;
    }

    /// <summary>
    /// Returns true when this transport can carry/base a unit in the requested category.
    /// Aircraft and spaceships require explicit carrier-basing flags; all other combat
    /// units use the legacy transport flag unless regular combat transport is disabled.
    /// </summary>
    public bool CanCarryUnitCategory(CombatCategory passengerCategory)
    {
        if (!isTransport) return false;

        if (IsAirCategory(passengerCategory)) return canBaseAircraft;
        if (IsSpaceCategory(passengerCategory)) return canBaseSpaceships;

        return canTransportCombatUnits;
    }

    public int GetFlatAttackBonusAgainst(CombatUnitData defender)
    {
        if (defender == null) return 0;
        int bonus = 0;
        if (bonusesAgainstCategories != null)
        {
            for (int i = 0; i < bonusesAgainstCategories.Length; i++)
            {
                if (bonusesAgainstCategories[i].targetCategory == defender.unitType)
                    bonus += bonusesAgainstCategories[i].attackBonus;
            }
        }
        if (bonusesAgainstUnits != null)
        {
            for (int i = 0; i < bonusesAgainstUnits.Length; i++)
            {
                if (bonusesAgainstUnits[i].targetUnit == defender)
                    bonus += bonusesAgainstUnits[i].attackBonus;
            }
        }
        return bonus;
    }

    public float GetPercentAttackBonusAgainst(CombatUnitData defender)
    {
        if (defender == null) return 0f;
        float pct = 0f;
        if (bonusesAgainstCategories != null)
        {
            for (int i = 0; i < bonusesAgainstCategories.Length; i++)
            {
                if (bonusesAgainstCategories[i].targetCategory == defender.unitType)
                    pct += bonusesAgainstCategories[i].attackPercent;
            }
        }
        if (bonusesAgainstUnits != null)
        {
            for (int i = 0; i < bonusesAgainstUnits.Length; i++)
            {
                if (bonusesAgainstUnits[i].targetUnit == defender)
                    pct += bonusesAgainstUnits[i].attackPercent;
            }
        }
        return pct;
    }

    public int GetFlatDefenseBonusAgainst(CombatUnitData defender)
    {
        if (defender == null) return 0;
        int bonus = 0;
        if (bonusesAgainstCategories != null)
        {
            for (int i = 0; i < bonusesAgainstCategories.Length; i++)
            {
                if (bonusesAgainstCategories[i].targetCategory == defender.unitType)
                    bonus += bonusesAgainstCategories[i].defenseBonus;
            }
        }
        if (bonusesAgainstUnits != null)
        {
            for (int i = 0; i < bonusesAgainstUnits.Length; i++)
            {
                if (bonusesAgainstUnits[i].targetUnit == defender)
                    bonus += bonusesAgainstUnits[i].defenseBonus;
            }
        }
        return bonus;
    }

    public float GetPercentDefenseBonusAgainst(CombatUnitData defender)
    {
        if (defender == null) return 0f;
        float pct = 0f;
        if (bonusesAgainstCategories != null)
        {
            for (int i = 0; i < bonusesAgainstCategories.Length; i++)
            {
                if (bonusesAgainstCategories[i].targetCategory == defender.unitType)
                    pct += bonusesAgainstCategories[i].defensePercent;
            }
        }
        if (bonusesAgainstUnits != null)
        {
            for (int i = 0; i < bonusesAgainstUnits.Length; i++)
            {
                if (bonusesAgainstUnits[i].targetUnit == defender)
                    pct += bonusesAgainstUnits[i].defensePercent;
            }
        }
        return pct;
    }
    
    [Header("Weather")]
    [Tooltip("If true, this unit takes weather attrition in severe seasons (e.g., winter)")]
    public bool takesWeatherDamage = true;
    [Tooltip("If true, this unit ignores mosquito damage even on infected tiles.")]
    public bool immuneToMosquitoes = false;
    [Tooltip("If true, this unit can safely enter lava tiles and ignores lava damage.")]
    public bool immuneToLava = false;

    [Header("Action Points")]
    [Tooltip("How many attacks/actions this unit can perform per turn.")]
    [Range(0, 10)]
    public int attackPointsPerTurn = 1;

    [Header("Charge")]
    [Tooltip("Percent bonus to attack when the unit must move more than 1 tile to make the attack (0 = disabled). Example: 0.2 = +20%")]
    public float chargeBonusPercent = 0f;

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
    [HideInInspector] public int baseAttack; // legacy generic fallback value
    [Tooltip("Base melee attack value for this combat unit.")]
    public int baseMeleeAttack;
    [Tooltip("Base ranged attack value for this combat unit.")]
    public int baseRangedAttack;
    [Tooltip("Base city attack value for this combat unit.")]
    public int baseCityAttack;
    [Tooltip("Base attack value against land/surface ground units. Leave 0 to fall back to this unit's weapon-style attack.")]
    public int baseGroundAttack;
    [Tooltip("Base attack value against naval surface units such as boats and ships. Leave 0 to fall back to this unit's weapon-style attack.")]
    public int baseNavalAttack;
    [Tooltip("Base attack value against underwater targets such as submarines, sea crawlers, or undersea bases. Leave 0 to fall back to this unit's weapon-style attack.")]
    public int baseUnderwaterAttack;
    [Tooltip("Base attack value against air units. Leave 0 to fall back to this unit's weapon-style attack.")]
    public int baseAirAttack;
    [Tooltip("Base attack value against space/orbit units. Leave 0 to fall back to this unit's weapon-style attack.")]
    public int baseSpaceAttack;
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
    [Tooltip("All these pantheons must be founded to unlock this unit")]
    public PantheonData[] requiredPantheons;
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
    [Tooltip("Default projectile/ranged weapon equipped by this unit (used when firing) ")]
    public EquipmentData defaultProjectileWeapon;

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

    [Header("Gold Maintenance (per turn)")]
    [Tooltip("Gold this unit consumes from the civilization treasury each turn. If unpaid, combat attack/defense stats are halved for the turn.")]
    public int goldMaintenancePerTurn = 2;

    [Header("Resource Upkeep (per turn)")]
    [Tooltip("Resources this unit consumes from the civilization stockpile each turn.")]
    public ResourceCost[] resourceUpkeepPerTurn;
    [Tooltip("What happens when the civilization cannot pay this unit's per-turn upkeep.")]
    public ResourceUpkeepFailureBehavior upkeepFailureBehavior = ResourceUpkeepFailureBehavior.Deactivate;
    [Tooltip("Applied to combat stats, action points, and movement when upkeep failure uses Debuff mode.")]
    [Range(0f, 1f)]
    public float upkeepFailureDebuffMultiplier = 0.5f;

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

        // Pantheon requirements
        if (requiredPantheons != null && requiredPantheons.Length > 0)
        {
            foreach (var pantheon in requiredPantheons)
            {
                if (pantheon == null) continue;

                if (civ.foundedPantheons == null || !civ.foundedPantheons.Contains(pantheon))
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
