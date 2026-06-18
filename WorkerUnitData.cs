using UnityEngine;

public enum RouteType { Road, Railroad }

[System.Serializable]
public struct WorkerUnitVisualOverride
{
    [Tooltip("Civilization that uses this visual override.")]
    public CivData civ;

    [Tooltip("Optional Addressables fallback for this civ's worker prefab. Direct prefab references are used first.")]
    public string addressableAddress;

    [Tooltip("Override direct prefab reference for this civ. This is used before any Addressables fallback.")]
    public GameObject prefab;

    [Tooltip("A matching civ override always uses the soldier display settings below.")]
    public bool overrideSoldierDisplay;

    [Range(1, 12)]
    public int soldierCount;

    public FormationType formationType;

    public SoldierVariant[] soldierVariants;

    [Range(0.1f, 10f)]
    public float formationSpacing;
}

 [CreateAssetMenu(fileName = "NewWorkerUnitData", menuName = "Data/Worker Unit Data")]
public class WorkerUnitData : ScriptableObject
{
    [Header("Default Equipment")]
    [Tooltip("Default weapon equipped by this worker (optional)")]
    public EquipmentData defaultWeapon;
    [Tooltip("Default projectile/ranged weapon equipped by this worker (used when firing)")]
    public EquipmentData defaultProjectileWeapon;
    [Tooltip("Default shield equipped by this worker (optional)")]
    public EquipmentData defaultShield;
    [Tooltip("Default armor equipped by this worker (optional)")]
    public EquipmentData defaultArmor;
    [Tooltip("Default miscellaneous equipment equipped by this worker (optional)")]
    public EquipmentData defaultMiscellaneous;
    public string unitName;
    public Sprite icon;
    public GameObject prefab;
    [Tooltip("Optional Addressables fallback for this worker prefab. The direct prefab reference is used first when assigned.")]
    public string addressableAddress;

    [Header("Audio")]
    [Tooltip("Sound played when this worker is selected/clicked on the map. Leave empty for no sound.")]
    public AudioClip selectSound;
    [Tooltip("Random pitch variation range (±) applied to select sound for variety.")]
    [Range(0f, 0.3f)]
    public float selectPitchVariation = 0.08f;

    [Header("Stats")] public int baseWorkPoints;
    [Tooltip("Whether this worker unit can enter orbit (landing/launch).")]
    public bool canEnterOrbit = false;

    [Header("Layer Operation")]
    [Tooltip("Usual gameplay layer for this worker. If layer masks below are left as None, legacy defaults are inferred.")]
    public TileLayer nativeLayer = TileLayer.Surface;
    [Tooltip("Layers this worker is allowed to occupy. None means Surface, plus Orbit when canEnterOrbit is true.")]
    public UnitLayerMask allowedLayers = UnitLayerMask.None;
    [Tooltip("Layers this worker may be born/placed on. None means Surface for backwards compatibility.")]
    public UnitLayerMask spawnLayers = UnitLayerMask.None;
    [Tooltip("Allow explicit layer transitions between surface water and underwater, e.g. divers/undersea builders.")]
    public bool canTransitionSurfaceUnderwater = false;
    [Tooltip("Allow explicit layer transitions between surface and atmosphere.")]
    public bool canTransitionSurfaceAtmosphere = false;

    public int baseMovePoints;
    public int baseHealth;
    public int baseAttack = 0;
    public int baseDefense = 0;
    public bool canFoundCity;

    [Header("Progression")]
    [Tooltip("Total XP thresholds required to reach the next levels. Entry 0 is the XP needed to reach level 2.")]
    public int[] xpToNextLevel;
    [Tooltip("Fallback total-XP curve used when xpToNextLevel does not define the next threshold. Total XP required is fallbackXpPerLevel times level squared.")]
    public int fallbackXpPerLevel = 10;
    [Tooltip("Flat attack gained per level above 1.")]
    public int attackPerLevel = 0;
    [Tooltip("Flat defense gained per level above 1.")]
    public int defensePerLevel = 0;
    [Tooltip("Flat health gained per level above 1.")]
    public int healthPerLevel = 0;
    [Tooltip("Flat work points gained per level above 1.")]
    public int workPointsPerLevel = 0;
    [Tooltip("Flat move points gained per level above 1.")]
    public int movePointsPerLevel = 0;
    [Tooltip("Flat range gained per level above 1.")]
    public float rangePerLevel = 0f;
    [Tooltip("XP gained per work point successfully applied to a project.")]
    public int experiencePerWorkPoint = 1;
    [Tooltip("Flat XP gained when successfully foraging.")]
    public int forageExperience = 5;
    [Tooltip("XP gained per point of combat damage dealt.")]
    public int experiencePerCombatDamage = 1;
    [Tooltip("Flat XP bonus gained when this worker lands the killing blow.")]
    public int killExperience = 5;

    [Header("Charge")]
    [Tooltip("Percent bonus to attack when the unit must move more than 1 tile to make the attack (0 = disabled). Example: 0.2 = +20%")]
    public float chargeBonusPercent = 0f;

    [Header("Action Points")]
    [Tooltip("How many attacks/actions this unit can perform per turn.")]
    [Range(0, 10)]
    public int attackPointsPerTurn = 1;
    
    [Header("Vision")]
    [Tooltip("How many tiles this unit can see (reveals fog of war). Default is 2 tiles.")]
    [Range(1, 10)]
    public int sightRange = 2;
    [Tooltip("Can harvest resources that require special skills/equipment")]
    public bool canHarvestSpecialResources = false;
    [Tooltip("Can forage resources from unimproved tiles")]
    public bool canForage = false;

    [Header("Weather")]
    [Tooltip("If true, this unit takes weather attrition in severe seasons (e.g., winter)")]
    public bool takesWeatherDamage = true;
    [Tooltip("If true, this unit ignores mosquito damage even on infected tiles.")]
    public bool immuneToMosquitoes = false;
    [Tooltip("If true, this unit can safely enter lava tiles and ignores lava damage.")]
    public bool immuneToLava = false;

    [Header("Production & Purchase")]
    public int productionCost;
    public int goldCost;
    [Tooltip("If true, instant purchase spends policy points instead of only gold.")]
    public bool canPurchaseWithPolicyPoints = false;
    [Tooltip("Policy points spent when canPurchaseWithPolicyPoints is enabled.")]
    public int policyPointPurchaseCost = 0;
    [Tooltip("If true, this unit requires the owning civilization to have at least requiredPolicyPoints to purchase or produce it.")]
    public bool requiresPolicyPoints = false;
    [Tooltip("Minimum policy points required to purchase or produce this unit. This is not spent unless canPurchaseWithPolicyPoints is enabled.")]
    public int requiredPolicyPoints = 0;
    [Tooltip("If true, instant purchase spends faith in addition to any other enabled purchase costs.")]
    public bool canPurchaseWithFaith = false;
    [Tooltip("Faith spent when canPurchaseWithFaith is enabled.")]
    public int faithPurchaseCost = 0;
    [Tooltip("If true, this unit requires the owning civilization to have at least requiredFaith to purchase or produce it.")]
    public bool requiresFaith = false;
    [Tooltip("Minimum faith required to purchase or produce this unit. This is not spent unless canPurchaseWithFaith is enabled.")]
    public int requiredFaith = 0;
    [Tooltip("Legacy one-of-each resource requirements. Each listed resource requires at least 1 in the civilization stockpile.")]
    public ResourceData[] requiredResources;
    [Tooltip("Resource amount requirements for production/purchase. Example: set Iron amount 5 to require 5 Iron in the civilization stockpile.")]
    public ResourceCost[] requiredResourceCosts;
    public Biome[] requiredTerrains;
    [Tooltip("Coastal city required for production")]
    public bool requiresCoastalCity = false;
    [Tooltip("Harbor building required for production")]
    public bool requiresHarbor = false;

    [Header("Worker Construction")]
    [Tooltip("If true, workers can construct this worker type on the map using work points.")]
    public bool buildableByWorker = false;
    [Tooltip("Total work points required by workers to construct this worker unit on a tile.")]
    public int workerWorkCost = 30;

    [Header("Per-Turn Yields")]
    [Tooltip("Flat yields this worker provides each turn while alive (added to owning civilization)")]
    public int foodPerTurn;
    [Header("Yield")]
    [Tooltip("Food awarded to attacker when this worker is killed (0 = none)")]
    public int foodOnKill;
    public int goldPerTurn;
    public int sciencePerTurn;
    public int culturePerTurn;
    public int policyPointsPerTurn;
    public int faithPerTurn;

    [Header("Intrinsic Conditional Bonuses")]
    [Tooltip("Bonuses supplied directly by this worker type. Use location filters for terrain/layer bonuses such as hills, mountains, underwater, or orbit.")]
    public WorkerUnitStatBonus[] intrinsicStatBonuses;
    [Tooltip("Auras projected by this worker type to nearby units.")]
    public UnitAuraBonus[] auraBonuses;

    [Header("Resource Upkeep (per turn)")]
    [Tooltip("Resources this worker consumes from the civilization stockpile each turn.")]
    public ResourceCost[] resourceUpkeepPerTurn;
    [Tooltip("What happens when the civilization cannot pay this worker's per-turn upkeep.")]
    public ResourceUpkeepFailureBehavior upkeepFailureBehavior = ResourceUpkeepFailureBehavior.Deactivate;
    [Tooltip("Applied to combat stats, work points, action points, and movement when upkeep failure uses Debuff mode.")]
    [Range(0f, 1f)]
    public float upkeepFailureDebuffMultiplier = 0.5f;
    
    [Header("Per-Turn Consumption")]
    [Tooltip("Food this worker consumes each turn (subtracted from civilization stockpile)")]
    public int foodConsumptionPerTurn = 1;

    [Header("Capture")]
    [Tooltip("If true this worker/animal can be captured into a herd")]
    public bool captureable = false;
    [Tooltip("If >0, number of herd 'animals' added to a herd when this worker is captured/killed and converted")]
    public int captureHerdCount = 0;
    [Tooltip("If set, explicit species this capture converts to (overrides name-matching).")]
    public Herd.HerdSpecies captureSpecies = Herd.HerdSpecies.Other;

    [Header("Multi-Worker Display")]
    [Tooltip("Number of worker figures displayed for this unit (1 = single model like today).")]
    [Range(1, 12)]
    public int soldierCount = 1;

    [Tooltip("Formation arrangement for multiple workers.")]
    public FormationType formationType = FormationType.Square;

    [Tooltip("Spacing between workers in formation (world units).")]
    [Range(0.1f, 10f)]
    public float formationSpacing = 0.5f;

    [Tooltip("Visual model variants to randomly pick from for each additional worker. Each variant prefab should have the same equipment holder transforms. ")]
    public SoldierVariant[] soldierVariants;

    [Header("Civilization Visual Overrides")]
    [Tooltip("Optional per-civilization visual overrides. Use these when the gameplay unit stays the same but the art should change by civ.")]
    public WorkerUnitVisualOverride[] civVisualOverrides;

    [Header("Build Options")]
    public RouteType[] buildableRoutes;

    [Header("Requirements")]
    [Tooltip("All these techs must be researched to unlock this unit")]
    public TechData[] requiredTechs;
    [Tooltip("All these cultures must be adopted to unlock this unit")]
    public CultureData[] requiredCultures;
    [Tooltip("At least one of these governments must be active to allow this worker (optional)")]
    public GovernmentData[] requiredGovernments;
    [Tooltip("All of these policies must be active to allow this worker (optional)")]
    public PolicyData[] requiredPolicies;
    [Tooltip("Optional alternative unlocks. If any entries are assigned, at least one listed tech, culture, government, or policy must be owned/active.")]
    public UnlockRequirementOption[] alternativeUnlockRequirements;
    [Tooltip("All of these operational buildings must be present in the producing city to train this unit (optional). Use for requirements like Temple Guard needing a Grand Temple, or cannons needing a cannon maker.")]
    public UnitBuildingRequirement[] requiredCityBuildings;

    [Header("Unit Limits")]
    [Tooltip("Maximum number of this unit type a civilization can have (-1 = unlimited)")]
    public int unitLimit = -1;
    [Tooltip("Unique identifier for units that share the same limit (leave empty for individual limits)")]
    public string limitCategory = "";

    public UnitLayerMask EffectiveAllowedLayers => allowedLayers != UnitLayerMask.None
        ? allowedLayers
        : (canEnterOrbit ? UnitLayerMask.Surface | UnitLayerMask.Orbit : UnitLayerMask.Surface);

    public UnitLayerMask EffectiveSpawnLayers => spawnLayers != UnitLayerMask.None ? spawnLayers : UnitLayerMask.Surface;

    public TileLayer EffectiveNativeLayer
    {
        get
        {
            if (allowedLayers != UnitLayerMask.None || spawnLayers != UnitLayerMask.None)
                return nativeLayer;
            return TileLayer.Surface;
        }
    }

    public bool CanOccupyLayer(TileLayer layer) => LayerConversion.MaskContains(EffectiveAllowedLayers, layer);
    public bool CanSpawnOnLayer(TileLayer layer) => LayerConversion.MaskContains(EffectiveSpawnLayers, layer) && CanOccupyLayer(layer);

    public bool CanTransitionBetweenLayers(TileLayer from, TileLayer to)
    {
        if (from == to) return CanOccupyLayer(from);
        if (!CanOccupyLayer(from) || !CanOccupyLayer(to)) return false;
        if ((from == TileLayer.Surface && to == TileLayer.Underwater) || (from == TileLayer.Underwater && to == TileLayer.Surface))
            return canTransitionSurfaceUnderwater;
        if ((from == TileLayer.Surface && to == TileLayer.Atmosphere) || (from == TileLayer.Atmosphere && to == TileLayer.Surface))
            return canTransitionSurfaceAtmosphere;
        if ((from == TileLayer.Surface && to == TileLayer.Orbit) || (from == TileLayer.Orbit && to == TileLayer.Surface))
            return canEnterOrbit;
        return false;
    }

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
                if (civ.activePolicies == null || !civ.activePolicies.Contains(pol)) return false;
            }
        }

        if (alternativeUnlockRequirements != null && alternativeUnlockRequirements.Length > 0)
        {
            bool anyAlternativeMet = false;
            foreach (var option in alternativeUnlockRequirements)
            {
                if (option.IsMet(civ)) { anyAlternativeMet = true; break; }
            }
            if (!anyAlternativeMet) return false;
        }
        
        return true;
    }

    private bool TryGetVisualOverride(Civilization civ, out WorkerUnitVisualOverride visualOverride)
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

    public GameObject GetPrefab(Civilization civ)
    {
        if (TryGetVisualOverride(civ, out var visualOverride))
        {
            if (visualOverride.prefab != null)
                return visualOverride.prefab;

            if (!string.IsNullOrWhiteSpace(visualOverride.addressableAddress))
            {
                if (AddressableUnitLoader.Instance != null)
                {
                    var loaded = AddressableUnitLoader.Instance.LoadUnitPrefabSync(visualOverride.addressableAddress);
                    if (loaded != null) return loaded;
                }
            }
        }

        if (prefab != null)
            return prefab;

        if (!string.IsNullOrWhiteSpace(addressableAddress) && AddressableUnitLoader.Instance != null)
        {
            var loaded = AddressableUnitLoader.Instance.LoadUnitPrefabSync(addressableAddress);
            if (loaded != null) return loaded;
        }

        return null;
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
}
