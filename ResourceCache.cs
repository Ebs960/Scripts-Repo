using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using System.Collections.Generic;
using System.Linq;
using GameCombat;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Static cache for all Resources.LoadAll calls to avoid repeated expensive I/O operations.
/// Uses lazy loading - only loads resources when first accessed, not all at once.
/// This prevents memory spikes from loading everything at startup.
/// </summary>
public static class ResourceCache
{
    // Optional ResearchDatabase instance. Can be set at runtime via SetResearchDatabase()
    private static ResearchDatabase _researchDatabase = null;
    private static ReligionDatabase _religionDatabase = null;

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
        
        // Reset loaded flags
        _combatUnitsLoaded = false;
        _workerUnitsLoaded = false;
        _buildingsLoaded = false;
        _projectilesLoaded = false;
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
        
#if UNITY_EDITOR
        // In editor: Use AssetDatabase to get names without loading full assets
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:CombatUnitData", new[] { "Assets/Units" });
        foreach (string guid in guids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            // Extract name from path without loading the asset
            string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
            names.Add(fileName);
        }
#else
        // In build: Need to load ScriptableObjects (no way around it without a manifest)
        // But we can at least cache the names
        var units = GetAllCombatUnits();
        foreach (var unit in units)
        {
            if (unit != null)
            {
                names.Add(unit.unitName ?? "Unknown");
            }
        }
#endif
        
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
        
#if UNITY_EDITOR
        // In editor: Load just the specific unit
        string[] guids = UnityEditor.AssetDatabase.FindAssets($"t:CombatUnitData {unitName}", new[] { "Assets/Units" });
        foreach (string guid in guids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            CombatUnitData unit = UnityEditor.AssetDatabase.LoadAssetAtPath<CombatUnitData>(path);
            if (unit != null && unit.unitName == unitName)
            {
                return unit;
            }
        }
        return null;
#else
        // In build: Must load all units to find the one we want
        EnsureCombatUnitsLoaded();
        return System.Array.Find(_allCombatUnits, u => u != null && u.unitName == unitName);
#endif
    }
    
    private static void EnsureCombatUnitsLoaded()
    {
        if (!_combatUnitsLoaded)
        {
            // Load ScriptableObjects from Assets/Units/ (not Resources folder)
            
#if UNITY_EDITOR
            // In editor: Use AssetDatabase to load from Assets/Units/
            string[] guids = AssetDatabase.FindAssets("t:CombatUnitData", new[] { "Assets/Units" });
            List<CombatUnitData> units = new List<CombatUnitData>();
            
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                CombatUnitData unit = AssetDatabase.LoadAssetAtPath<CombatUnitData>(path);
                if (unit != null)
                {
                    units.Add(unit);
                }
            }
            
            _allCombatUnits = units.ToArray();
#else
            // In build: Try to load from Addressables first, fallback to Resources
            // If ScriptableObjects are marked as Addressable, load them via Addressables
            // Otherwise, fallback to Resources (for backward compatibility)
            try
            {
                // Try Addressables first (if ScriptableObjects are marked as addressable)
                var handle = Addressables.LoadAssetsAsync<CombatUnitData>("CombatUnitData", null);
                handle.WaitForCompletion();
                
                if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
                {
                    _allCombatUnits = handle.Result.ToArray();
                    Addressables.Release(handle);
                }
                else
                {
                    // Fallback to Resources
                    Debug.LogWarning("[ResourceCache] Addressables failed, falling back to Resources/Units/");
                    _allCombatUnits = Resources.LoadAll<CombatUnitData>("Units");
                }
            }
            catch
            {
                // Fallback to Resources
                Debug.LogWarning("[ResourceCache] Addressables not available, falling back to Resources/Units/");
            _allCombatUnits = Resources.LoadAll<CombatUnitData>("Units");
            }
#endif
            _combatUnitsLoaded = true;
            
            int count = _allCombatUnits?.Length ?? 0;
            
            if (count == 0)
            {
                Debug.LogError("[ResourceCache] WARNING: No CombatUnitData found in Assets/Units/ folder! " +
                    "Make sure your ScriptableObjects are in Assets/Units/");
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
#if UNITY_EDITOR
            // Search the whole project for WorkerUnitData assets to avoid depending on a specific folder.
            string[] guids = AssetDatabase.FindAssets("t:WorkerUnitData");
            List<WorkerUnitData> units = new List<WorkerUnitData>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                WorkerUnitData unit = AssetDatabase.LoadAssetAtPath<WorkerUnitData>(path);
                if (unit != null)
                {
                    units.Add(unit);
                }
            }

            _allWorkerUnits = units.ToArray();
#else
            try
            {
                var handle = Addressables.LoadAssetsAsync<WorkerUnitData>("WorkerUnitData", null);
                handle.WaitForCompletion();

                if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
                {
                    _allWorkerUnits = handle.Result.ToArray();
                    Addressables.Release(handle);
                }
                else
                {
                    Debug.LogWarning("[ResourceCache] Addressables failed, falling back to Resources/Workers/");
                    _allWorkerUnits = Resources.LoadAll<WorkerUnitData>("Workers");
                }
            }
            catch
            {
                Debug.LogWarning("[ResourceCache] Addressables not available, falling back to Resources/Workers/");
                _allWorkerUnits = Resources.LoadAll<WorkerUnitData>("Workers");
            }
#endif

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
            _allBuildings = Resources.LoadAll<BuildingData>("Buildings");
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
            _allProjectiles = Resources.LoadAll<ProjectileData>("Projectiles");
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
            _allMissiles = Resources.LoadAll<MissileData>("Missiles");
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
            _allCivDatas = Resources.LoadAll<CivData>("Civilizations");
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
            _allEquipment = Resources.LoadAll<EquipmentData>("Equipment");
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
            _allDistricts = Resources.LoadAll<DistrictData>("Districts");
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
            _allImprovements = Resources.LoadAll<ImprovementData>("Improvements");
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
            _allResourceData = Resources.LoadAll<ResourceData>("Data/Resources");
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
            // Prefer an assigned ResearchDatabase (settable at runtime or found in editor)
            if (_researchDatabase != null && _researchDatabase.techs != null && _researchDatabase.techs.Length > 0)
            {
                _allTechData = _researchDatabase.techs;
                _techDataLoaded = true;
            }
            else
            {
                // In editor, try to find a ResearchDatabase asset anywhere in the project
#if UNITY_EDITOR
                if (_researchDatabase == null)
                {
                    string[] guids = AssetDatabase.FindAssets("t:ResearchDatabase");
                    if (guids != null && guids.Length > 0)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                        var db = AssetDatabase.LoadAssetAtPath<ResearchDatabase>(path);
                        if (db != null)
                        {
                            _researchDatabase = db;
                            _allTechData = _researchDatabase.techs ?? new TechData[0];
                            _techDataLoaded = true;
                        }
                    }
                }
#endif
                // Fallback: load from Resources/Tech (backward compatibility)
                if (!_techDataLoaded)
                {
                    _allTechData = Resources.LoadAll<TechData>("Tech");
                    _techDataLoaded = true;
                }
            }
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
            // Prefer an assigned ResearchDatabase
            if (_researchDatabase != null && _researchDatabase.cultures != null && _researchDatabase.cultures.Length > 0)
            {
                _allCultureData = _researchDatabase.cultures;
                _cultureDataLoaded = true;
            }
            else
            {
                // In editor, try to find a ResearchDatabase asset anywhere in the project
#if UNITY_EDITOR
                if (_researchDatabase == null)
                {
                    string[] guids = AssetDatabase.FindAssets("t:ResearchDatabase");
                    if (guids != null && guids.Length > 0)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                        var db = AssetDatabase.LoadAssetAtPath<ResearchDatabase>(path);
                        if (db != null)
                        {
                            _researchDatabase = db;
                            _allCultureData = _researchDatabase.cultures ?? new CultureData[0];
                            _cultureDataLoaded = true;
                        }
                    }
                }
#endif
                // Fallback: load from Resources/Culture (backward compatibility)
                if (!_cultureDataLoaded)
                {
                    _allCultureData = Resources.LoadAll<CultureData>("Culture");
                    _cultureDataLoaded = true;
                }
            }
        }
        return _allCultureData ?? new CultureData[0];
    }

    public static PantheonData[] GetAllPantheonData()
    {
        EnsureInitialized();
        if (!_pantheonDataLoaded)
        {
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
            if (_religionDatabase != null && _religionDatabase.religions != null && _religionDatabase.religions.Length > 0)
            {
                _allReligionData = _religionDatabase.religions;
            }
            else
            {
                Debug.LogError("[ResourceCache] No ReligionDatabase assigned. Religion list will be empty. Assign a ReligionDatabase to ReligionManager or call ResourceCache.SetReligionDatabase().");
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
    
    /// <summary>
    /// Get available combat units for a civilization (meets requirements)
    /// </summary>
    public static List<CombatUnitData> GetAvailableCombatUnits(Civilization civ)
    {
        if (civ == null) return new List<CombatUnitData>();
        
        var allUnits = GetAllCombatUnits();
        return allUnits.Where(u => u != null && u.AreRequirementsMet(civ)).ToList();
    }
    
    /// <summary>
    /// Get available worker units for a civilization (meets requirements)
    /// </summary>
    public static List<WorkerUnitData> GetAvailableWorkerUnits(Civilization civ)
    {
        if (civ == null) return new List<WorkerUnitData>();
        
        var allWorkers = GetAllWorkerUnits();
        return allWorkers.Where(w => w != null && w.AreRequirementsMet(civ)).ToList();
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

