using UnityEngine;
using UnityEngine.AddressableAssets;
using System.Collections.Generic;
using System.Linq;
using GameCombat;

/// <summary>
/// Static cache for all Resources.LoadAll calls to avoid repeated expensive I/O operations.
/// Uses lazy loading - only loads resources when first accessed, not all at once.
/// This prevents memory spikes from loading everything at startup.
/// </summary>
public static class ResourceCache
{
    // Optional ResearchDatabase instance. Can be set at runtime via SetResearchDatabase()
    private static BaseGameContentDatabase _baseGameDatabase;
    private static bool _baseGameDatabaseLoadAttempted;
    private static ResearchDatabase _researchDatabase = null;
    private static ReligionDatabase _religionDatabase = null;
    private static EquipmentDatabase _equipmentDatabase = null;

    public static void SetEquipmentDatabase(EquipmentDatabase db)
    {
        _equipmentDatabase = db;
        _allEquipment = db != null ? db.equipment : null;
        _allProjectiles = db != null ? db.projectiles : null;
        _equipmentLoaded = db != null;
        _projectilesLoaded = db != null;
    }

    public static EquipmentDatabase GetEquipmentDatabase() => _equipmentDatabase;

    /// <summary>
    /// Assign a ResearchDatabase at runtime (useful for GameManager/TechManager wiring).
    /// If set, `GetAllTechData()` and `GetAllCultureData()` will prefer the database contents.
    /// </summary>
    public static void SetResearchDatabase(ResearchDatabase db)
    {
        _researchDatabase = db;
        if (_researchDatabase != null)
        {
            _allTechData = _researchDatabase.techs ?? new TechData[0];
            _allCultureData = _researchDatabase.cultures ?? new CultureData[0];
            _techDataLoaded = true;
            _cultureDataLoaded = true;
        }
        else
        {
            _techDataLoaded = false;
            _cultureDataLoaded = false;
            _allTechData = null;
            _allCultureData = null;
        }
    }

    public static ResearchDatabase GetResearchDatabase() => _researchDatabase;

    public static void SetReligionDatabase(ReligionDatabase db)
    {
        _religionDatabase = db;
        _pantheonDataLoaded = false;
        _religionDataLoaded = false;
        _beliefDataLoaded = false;
        _allPantheonData = null;
        _allReligionData = null;
        _allBeliefData = null;
    }

    public static ReligionDatabase GetReligionDatabase() => _religionDatabase;

    private static bool _initialized = false;
    
    // Cached resource arrays - loaded lazily on first access
    private static CombatUnitData[] _allCombatUnits;
    private static WorkerUnitData[] _allWorkerUnits;
    private static BuildingData[] _allBuildings;
    private static ProjectileData[] _allProjectiles;
    private static MissileData[] _allMissiles;
    private static CivData[] _allCivDatas;
    private static EquipmentData[] _allEquipment;
    private static DistrictData[] _allDistricts;
    private static ImprovementData[] _allImprovements;
    private static ResourceData[] _allResourceData;
    private static TechData[] _allTechData;
    private static CultureData[] _allCultureData;
    private static PantheonData[] _allPantheonData;
    private static ReligionData[] _allReligionData;
    private static BeliefData[] _allBeliefData;
    private static LeaderData[] _allLeaderData;
    private static GovernmentData[] _allGovernmentData;
    private static PolicyData[] _allPolicyData;
    
    // Track which resources have been loaded (for lazy loading)
    private static bool _combatUnitsLoaded = false;
    private static bool _workerUnitsLoaded = false;
    private static bool _buildingsLoaded = false;
    private static bool _projectilesLoaded = false;
    private static bool _missilesLoaded = false;
    private static bool _civDatasLoaded = false;
    private static bool _equipmentLoaded = false;
    private static bool _districtsLoaded = false;
    private static bool _improvementsLoaded = false;
    private static bool _resourceDataLoaded = false;
    private static bool _techDataLoaded = false;
    private static bool _cultureDataLoaded = false;
    private static bool _pantheonDataLoaded = false;
    private static bool _religionDataLoaded = false;
    private static bool _beliefDataLoaded = false;
    private static bool _leaderDataLoaded;
    private static bool _governmentDataLoaded;
    private static bool _policyDataLoaded;
    
    /// <summary>
    /// Initialize the resource cache - now just marks as initialized, resources load lazily
    /// Also initializes AddressableUnitLoader for on-demand unit loading
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        
        // Initialize AddressableUnitLoader if available
        // Force instance creation to ensure it exists
        AddressableUnitLoader loader = AddressableUnitLoader.Instance;
        if (loader == null)
        {
            Debug.LogWarning("[ResourceCache] AddressableUnitLoader.Instance is null! Units may fail to load.");
        }
    }
    
    /// <summary>
    /// Clear the cache and unload prefab references to free memory
    /// </summary>
    public static void Clear()
    {
        // Unload prefab references from ScriptableObjects before clearing
        UnloadPrefabReferences();
        
        _initialized = false;
        _allCombatUnits = null;
        _allWorkerUnits = null;
        _allBuildings = null;
        _allProjectiles = null;
        _allMissiles = null;
        _allCivDatas = null;
        _allEquipment = null;
        _allDistricts = null;
        _allImprovements = null;
        _allResourceData = null;
        _allTechData = null;
        _allCultureData = null;
        _allPantheonData = null;
        _allReligionData = null;
        _allBeliefData = null;
        _allLeaderData = null;
        _allGovernmentData = null;
        _allPolicyData = null;
        _baseGameDatabase = null;
        _researchDatabase = null;
        _religionDatabase = null;
        _equipmentDatabase = null;
        _baseGameDatabaseLoadAttempted = false;
        
        // Reset loaded flags
        _combatUnitsLoaded = false;
        _workerUnitsLoaded = false;
        _buildingsLoaded = false;
        _projectilesLoaded = false;
        _missilesLoaded = false;
        _civDatasLoaded = false;
        _equipmentLoaded = false;
        _districtsLoaded = false;
        _improvementsLoaded = false;
        _resourceDataLoaded = false;
        _techDataLoaded = false;
        _cultureDataLoaded = false;
        _pantheonDataLoaded = false;
        _religionDataLoaded = false;
        _beliefDataLoaded = false;
        _leaderDataLoaded = false;
        _governmentDataLoaded = false;
        _policyDataLoaded = false;
        _unitNamesLoaded = false;
        _cachedUnitNames = null;
    }
    
    /// <summary>
    /// Unload prefab references from cached ScriptableObjects to free memory.
    /// Clears cached prefabs that were loaded on-demand.
    /// </summary>
    public static void UnloadPrefabReferences()
    {
        // Note: With the new path-based system, prefabs are cached in private fields
        // We can't directly clear them, but they'll be garbage collected when not referenced
        // The main benefit is that prefabs aren't auto-loaded when ScriptableObjects load
    }
    
    /// <summary>
    /// Load prefab for a specific unit on-demand using Addressables.
    /// This loads the prefab only when needed (when battle starts).
    /// </summary>
    public static void LoadUnitPrefab(CombatUnitData unitData)
    {
        if (unitData == null) return;
        
        // Use GetPrefab() which loads from Addressables
        GameObject prefab = unitData.GetPrefab();
        if (prefab == null)
        {
            Debug.LogWarning($"[ResourceCache] Could not load prefab for {unitData.unitName}. Make sure prefab is marked as Addressable with address matching unitName.");
        }
    }
    
    /// <summary>
    /// Get all combat unit data (cached, lazy-loaded)
    /// WARNING: This loads all units with their icons into memory!
    /// For menu dropdowns, consider using GetCombatUnitNames() instead.
    /// </summary>
    public static CombatUnitData[] GetAllCombatUnits()
    {
        EnsureInitialized();
        EnsureCombatUnitsLoaded();
        return _allCombatUnits ?? new CombatUnitData[0];
    }
    
    // Cached unit names (lightweight, no icons)
    private static string[] _cachedUnitNames;
    private static bool _unitNamesLoaded = false;
    
    /// <summary>
    /// Get just unit names for dropdown menus WITHOUT loading full ScriptableObjects.
    /// This is much lighter on memory since it doesn't load icons.
    /// </summary>
    public static string[] GetCombatUnitNames()
    {
        if (_unitNamesLoaded && _cachedUnitNames != null)
        {
            return _cachedUnitNames;
        }
        
        List<string> names = new List<string>();
        foreach (var unit in GetAllCombatUnits())
            if (unit != null) names.Add(unit.unitName ?? "Unknown");

        _cachedUnitNames = names.ToArray();
        _unitNamesLoaded = true;
        return _cachedUnitNames;
    }
    
    /// <summary>
    /// Get a specific combat unit by name (loads on demand)
    /// </summary>
    public static CombatUnitData GetCombatUnitByName(string unitName)
    {
        if (string.IsNullOrEmpty(unitName)) return null;
        
        // First check if units are already loaded
        if (_combatUnitsLoaded && _allCombatUnits != null)
        {
            return System.Array.Find(_allCombatUnits, u => u != null && u.unitName == unitName);
        }
        
        EnsureCombatUnitsLoaded();
        return System.Array.Find(_allCombatUnits, u => u != null && u.unitName == unitName);
    }
    
    private static void EnsureCombatUnitsLoaded()
    {
        if (!_combatUnitsLoaded)
        {
            EnsureBaseGameDatabaseLoaded();
            _allCombatUnits = _baseGameDatabase != null ? _baseGameDatabase.combatUnits : null;
            _combatUnitsLoaded = true;
            
            int count = _allCombatUnits?.Length ?? 0;
            
            if (count == 0)
            {
                Debug.LogError("[ResourceCache] WARNING: No CombatUnitData found in Assets/Scripts Repo/Units/ folder! " +
                    "Make sure your ScriptableObjects are in Assets/Scripts Repo/Units/");
            }
        }
    }
    
    /// <summary>
    /// Get all worker unit data (cached, lazy-loaded)
    /// </summary>
    public static WorkerUnitData[] GetAllWorkerUnits()
    {
        EnsureInitialized();
        EnsureWorkerUnitsLoaded();
        return _allWorkerUnits ?? new WorkerUnitData[0];
    }

    private static void EnsureWorkerUnitsLoaded()
    {
        if (!_workerUnitsLoaded)
        {
            EnsureBaseGameDatabaseLoaded();
            _allWorkerUnits = _baseGameDatabase != null ? _baseGameDatabase.workerUnits : null;

            _workerUnitsLoaded = true;

            int count = _allWorkerUnits?.Length ?? 0;
            if (count == 0)
            {
                Debug.LogError("[ResourceCache] WARNING: No WorkerUnitData found in Assets/Workers/ folder! Make sure your ScriptableObjects are in Assets/Workers/ or marked as Addressable with label 'WorkerUnitData'.");
            }
        }
    }
    
    /// <summary>
    /// Get all building data (cached, lazy-loaded)
    /// </summary>
    public static BuildingData[] GetAllBuildings()
    {
        EnsureInitialized();
        if (!_buildingsLoaded)
        {
            EnsureBaseGameDatabaseLoaded();
            _allBuildings = _baseGameDatabase != null ? _baseGameDatabase.buildings : null;
            _buildingsLoaded = true;
        }
        return _allBuildings ?? new BuildingData[0];
    }
    
    /// <summary>
    /// Get all projectile data (cached, lazy-loaded)
    /// </summary>
    public static ProjectileData[] GetAllProjectiles()
    {
        EnsureInitialized();
        EnsureProjectilesLoaded();
        return _allProjectiles ?? new ProjectileData[0];
    }
    
    private static void EnsureProjectilesLoaded()
    {
        if (!_projectilesLoaded)
        {
            if (_equipmentDatabase == null)
                EnsureBaseGameDatabaseLoaded();
            _allProjectiles = _equipmentDatabase != null && _equipmentDatabase.projectiles != null
                ? _equipmentDatabase.projectiles
                : new ProjectileData[0];
            _projectilesLoaded = true;
        }
    }

    /// <summary>
    /// Get all missile data assets (cached, lazy-loaded from Resources/Missiles).
    /// </summary>
    public static MissileData[] GetAllMissiles()
    {
        EnsureInitialized();
        if (!_missilesLoaded)
        {
            EnsureBaseGameDatabaseLoaded();
            _allMissiles = _baseGameDatabase != null ? _baseGameDatabase.missiles : null;
            _missilesLoaded = true;
        }
        return _allMissiles ?? new MissileData[0];
    }
    
    /// <summary>
    /// Get all civilization data (cached, lazy-loaded)
    /// </summary>
    public static CivData[] GetAllCivDatas()
    {
        EnsureInitialized();
        EnsureCivDatasLoaded();
        return _allCivDatas ?? new CivData[0];
    }
    
    private static void EnsureCivDatasLoaded()
    {
        if (!_civDatasLoaded)
        {
            EnsureBaseGameDatabaseLoaded();
            _allCivDatas = _baseGameDatabase != null ? _baseGameDatabase.civilizations : null;
            _civDatasLoaded = true;
        }
    }
    
    /// <summary>
    /// Get all equipment data (cached, lazy-loaded)
    /// </summary>
    public static EquipmentData[] GetAllEquipment()
    {
        EnsureInitialized();
        if (!_equipmentLoaded)
        {
            if (_equipmentDatabase == null)
                EnsureBaseGameDatabaseLoaded();
            _allEquipment = _equipmentDatabase != null && _equipmentDatabase.equipment != null
                ? _equipmentDatabase.equipment
                : new EquipmentData[0];
            _equipmentLoaded = true;
        }
        return _allEquipment ?? new EquipmentData[0];
    }
    
    /// <summary>
    /// Get all district data (cached, lazy-loaded)
    /// </summary>
    public static DistrictData[] GetAllDistricts()
    {
        EnsureInitialized();
        if (!_districtsLoaded)
        {
            EnsureBaseGameDatabaseLoaded();
            _allDistricts = _baseGameDatabase != null ? _baseGameDatabase.districts : null;
            _districtsLoaded = true;
        }
        return _allDistricts ?? new DistrictData[0];
    }
    
    /// <summary>
    /// Get all improvement data (cached, lazy-loaded)
    /// </summary>
    public static ImprovementData[] GetAllImprovements()
    {
        EnsureInitialized();
        if (!_improvementsLoaded)
        {
            EnsureBaseGameDatabaseLoaded();
            _allImprovements = _baseGameDatabase != null ? _baseGameDatabase.improvements : null;
            _improvementsLoaded = true;
        }
        return _allImprovements ?? new ImprovementData[0];
    }
    
    /// <summary>
    /// Get all resource data (cached, lazy-loaded)
    /// </summary>
    public static ResourceData[] GetAllResourceData()
    {
        EnsureInitialized();
        if (!_resourceDataLoaded)
        {
            EnsureBaseGameDatabaseLoaded();
            _allResourceData = _baseGameDatabase != null ? _baseGameDatabase.resources : null;
            _resourceDataLoaded = true;
        }
        return _allResourceData ?? new ResourceData[0];
    }
    
    /// <summary>
    /// Get all tech data (cached, lazy-loaded)
    /// FIXED: Now uses specific path instead of scanning entire Resources folder
    /// </summary>
    public static TechData[] GetAllTechData()
    {
        EnsureInitialized();
        if (!_techDataLoaded)
        {
            EnsureBaseGameDatabaseLoaded();
            _allTechData = _researchDatabase != null ? _researchDatabase.techs : null;
            _techDataLoaded = true;
        }
        return _allTechData ?? new TechData[0];
    }
    
    /// <summary>
    /// Get all culture data (cached, lazy-loaded)
    /// FIXED: Now uses specific path instead of scanning entire Resources folder
    /// </summary>
    public static CultureData[] GetAllCultureData()
    {
        EnsureInitialized();
        if (!_cultureDataLoaded)
        {
            EnsureBaseGameDatabaseLoaded();
            _allCultureData = _researchDatabase != null ? _researchDatabase.cultures : null;
            _cultureDataLoaded = true;
        }
        return _allCultureData ?? new CultureData[0];
    }

    public static PantheonData[] GetAllPantheonData()
    {
        EnsureInitialized();
        if (!_pantheonDataLoaded)
        {
            EnsureBaseGameDatabaseLoaded();
            if (_religionDatabase != null && _religionDatabase.pantheons != null && _religionDatabase.pantheons.Length > 0)
            {
                _allPantheonData = _religionDatabase.pantheons;
            }
            else
            {
                Debug.LogError("[ResourceCache] No ReligionDatabase assigned. Pantheon list will be empty. Assign a ReligionDatabase to ReligionManager or call ResourceCache.SetReligionDatabase().");
                _allPantheonData = new PantheonData[0];
            }

            _pantheonDataLoaded = true;
        }

        return _allPantheonData ?? new PantheonData[0];
    }

    public static ReligionData[] GetAllReligionData()
    {
        EnsureInitialized();
        if (!_religionDataLoaded)
        {
            EnsureBaseGameDatabaseLoaded();
            if (_religionDatabase != null && _religionDatabase.religions != null && _religionDatabase.religions.Length > 0)
            {
                _allReligionData = _religionDatabase.religions;
            }
            else
            {
                Debug.LogError("[ResourceCache] No ReligionDatabase assigned. Religion list will be empty.");
                _allReligionData = new ReligionData[0];
            }

            _religionDataLoaded = true;
        }

        return _allReligionData ?? new ReligionData[0];
    }

    /// <summary>
    /// Get all belief data without scanning the entire Resources tree.
    /// In editor, load directly from the belief asset folder.
    /// In builds, fall back to a scoped Resources path if beliefs are moved there later.
    /// </summary>
    public static BeliefData[] GetAllBeliefData()
    {
        EnsureInitialized();
        if (!_beliefDataLoaded)
        {
            EnsureBaseGameDatabaseLoaded();
            if (_religionDatabase != null && _religionDatabase.beliefs != null && _religionDatabase.beliefs.Length > 0)
            {
                _allBeliefData = _religionDatabase.beliefs;
            }
            else
            {
                Debug.LogError("[ResourceCache] No ReligionDatabase assigned. Belief list will be empty. Assign a ReligionDatabase to ReligionManager or call ResourceCache.SetReligionDatabase().");
                _allBeliefData = new BeliefData[0];
            }
            _beliefDataLoaded = true;
        }

        return _allBeliefData ?? new BeliefData[0];
    }
    
    public static LeaderData[] GetAllLeaderData()
    {
        EnsureInitialized();
        if (!_leaderDataLoaded) { EnsureBaseGameDatabaseLoaded(); _allLeaderData = _baseGameDatabase?.leaders; _leaderDataLoaded = true; }
        return _allLeaderData ?? new LeaderData[0];
    }

    public static GovernmentData[] GetAllGovernmentData()
    {
        EnsureInitialized();
        if (!_governmentDataLoaded) { EnsureBaseGameDatabaseLoaded(); _allGovernmentData = _baseGameDatabase?.governments; _governmentDataLoaded = true; }
        return _allGovernmentData ?? new GovernmentData[0];
    }

    public static PolicyData[] GetAllPolicyData()
    {
        EnsureInitialized();
        if (!_policyDataLoaded) { EnsureBaseGameDatabaseLoaded(); _allPolicyData = _baseGameDatabase?.policies; _policyDataLoaded = true; }
        return _allPolicyData ?? new PolicyData[0];
    }

    private static void EnsureBaseGameDatabaseLoaded()
    {
        if (_baseGameDatabaseLoadAttempted) return;
        _baseGameDatabaseLoadAttempted = true;
        _baseGameDatabase = Resources.Load<BaseGameContentDatabase>("BaseGameContentDatabase");
        if (_baseGameDatabase == null)
        {
            Debug.LogError("[ResourceCache] BaseGameContentDatabase could not be loaded. Dynamic units, buildings, improvements, governments, and civilizations may be unavailable. Rebuild it with 'Populate From Project'.");
            return;
        }
        if (_researchDatabase == null) SetResearchDatabase(_baseGameDatabase.research);
        if (_religionDatabase == null) SetReligionDatabase(_baseGameDatabase.religion);
        if (_equipmentDatabase == null) SetEquipmentDatabase(_baseGameDatabase.equipment);
    }

    /// <summary>
    /// Get available combat units for a civilization (meets requirements)
    /// </summary>
    public static List<CombatUnitData> GetAvailableCombatUnits(Civilization civ)
    {
        if (civ == null) return new List<CombatUnitData>();
        
        var allUnits = GetAllCombatUnits();
        var available = new List<CombatUnitData>();
        var seen = new HashSet<CombatUnitData>();
        foreach (var unit in allUnits)
        {
            if (unit == null) continue;
            var resolved = unit.GetLatestUnlockedUpgrade(civ);
            if (resolved == null || seen.Contains(resolved)) continue;
            seen.Add(resolved);
            available.Add(resolved);
        }
        return available;
    }
    
    /// <summary>
    /// Get available worker units for a civilization (meets requirements)
    /// </summary>
    public static List<WorkerUnitData> GetAvailableWorkerUnits(Civilization civ)
    {
        if (civ == null) return new List<WorkerUnitData>();
        
        var allWorkers = GetAllWorkerUnits();
        return allWorkers.Where(w => w != null && w.IsBuildableFor(civ)).ToList();
    }
    
    /// <summary>
    /// Get available buildings for a civilization (meets requirements)
    /// </summary>
    public static List<BuildingData> GetAvailableBuildings(Civilization civ)
    {
        if (civ == null) return new List<BuildingData>();
        
        var allBuildings = GetAllBuildings();
        return allBuildings.Where(b => b != null && b.AreRequirementsMet(civ)).ToList();
    }
    
    /// <summary>
    /// Get available projectiles for a civilization (meets requirements)
    /// </summary>
    public static List<ProjectileData> GetAvailableProjectiles(Civilization civ)
    {
        if (civ == null) return new List<ProjectileData>();
        
        var allProjectiles = GetAllProjectiles();
        return allProjectiles.Where(p => p != null && p.CanBeProducedBy(civ)).ToList();
    }
    
    /// <summary>
    /// Ensure cache is initialized (auto-initialize if not already done)
    /// </summary>
    private static void EnsureInitialized()
    {
        if (!_initialized)
        {
            Initialize();
        }
    }
}
