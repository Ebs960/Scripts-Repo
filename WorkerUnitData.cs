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

    [Header("Production & Purchase")] public int productionCost;
    public int goldCost;
    public ResourceData[] requiredResources;
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

    [Header("Unit Limits")]
    [Tooltip("Maximum number of this unit type a civilization can have (-1 = unlimited)")]
    public int unitLimit = -1;
    [Tooltip("Unique identifier for units that share the same limit (leave empty for individual limits)")]
    public string limitCategory = "";

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