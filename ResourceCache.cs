using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using GameCombat;

/// <summary>
/// Static cache for all Resources.LoadAll calls to avoid repeated expensive I/O operations.
/// Loads all resources once at startup and provides cached access.
/// </summary>
public static class ResourceCache
{
    private static bool _initialized = false;
    
    // Cached resource arrays
    private static CombatUnitData[] _allCombatUnits;
    private static WorkerUnitData[] _allWorkerUnits;
    private static BuildingData[] _allBuildings;
    private static ProjectileData[] _allProjectiles;
    private static CivData[] _allCivDatas;
    private static EquipmentData[] _allEquipment;
    private static DistrictData[] _allDistricts;
    private static ImprovementData[] _allImprovements;
    private static ResourceData[] _allResourceData;
    private static TechData[] _allTechData;
    private static CultureData[] _allCultureData;
    
    /// <summary>
    /// Initialize the resource cache by loading all resources once.
    /// Should be called early in game initialization (e.g., GameManager.Awake or Start).
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        
        _allCombatUnits = Resources.LoadAll<CombatUnitData>("Units");
        _allWorkerUnits = Resources.LoadAll<WorkerUnitData>("Workers");
        _allBuildings = Resources.LoadAll<BuildingData>("Buildings");
        _allProjectiles = Resources.LoadAll<ProjectileData>("Projectiles");
        _allCivDatas = Resources.LoadAll<CivData>("Civilizations");
        _allEquipment = Resources.LoadAll<EquipmentData>("Equipment");
        _allDistricts = Resources.LoadAll<DistrictData>("Districts");
        _allImprovements = Resources.LoadAll<ImprovementData>("Improvements");
        _allResourceData = Resources.LoadAll<ResourceData>("Data/Resources");
        _allTechData = Resources.LoadAll<TechData>(string.Empty);
        _allCultureData = Resources.LoadAll<CultureData>(string.Empty);
        
        _initialized = true;
        
        Debug.Log($"[ResourceCache] Initialized with {_allCombatUnits?.Length ?? 0} units, {_allWorkerUnits?.Length ?? 0} workers, {_allBuildings?.Length ?? 0} buildings, {_allProjectiles?.Length ?? 0} projectiles, {_allCivDatas?.Length ?? 0} civs, {_allResourceData?.Length ?? 0} resources, {_allTechData?.Length ?? 0} techs, {_allCultureData?.Length ?? 0} cultures");
    }
    
    /// <summary>
    /// Clear the cache (useful for testing or when resources are reloaded)
    /// </summary>
    public static void Clear()
    {
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
    }
    
    /// <summary>
    /// Get all combat unit data (cached)
    /// </summary>
    public static CombatUnitData[] GetAllCombatUnits()
    {
        EnsureInitialized();
        return _allCombatUnits ?? new CombatUnitData[0];
    }
    
    /// <summary>
    /// Get all worker unit data (cached)
    /// </summary>
    public static WorkerUnitData[] GetAllWorkerUnits()
    {
        EnsureInitialized();
        return _allWorkerUnits ?? new WorkerUnitData[0];
    }
    
    /// <summary>
    /// Get all building data (cached)
    /// </summary>
    public static BuildingData[] GetAllBuildings()
    {
        EnsureInitialized();
        return _allBuildings ?? new BuildingData[0];
    }
    
    /// <summary>
    /// Get all projectile data (cached)
    /// </summary>
    public static ProjectileData[] GetAllProjectiles()
    {
        EnsureInitialized();
        return _allProjectiles ?? new ProjectileData[0];
    }
    
    /// <summary>
    /// Get all civilization data (cached)
    /// </summary>
    public static CivData[] GetAllCivDatas()
    {
        EnsureInitialized();
        return _allCivDatas ?? new CivData[0];
    }
    
    /// <summary>
    /// Get all equipment data (cached)
    /// </summary>
    public static EquipmentData[] GetAllEquipment()
    {
        EnsureInitialized();
        return _allEquipment ?? new EquipmentData[0];
    }
    
    /// <summary>
    /// Get all district data (cached)
    /// </summary>
    public static DistrictData[] GetAllDistricts()
    {
        EnsureInitialized();
        return _allDistricts ?? new DistrictData[0];
    }
    
    /// <summary>
    /// Get all improvement data (cached)
    /// </summary>
    public static ImprovementData[] GetAllImprovements()
    {
        EnsureInitialized();
        return _allImprovements ?? new ImprovementData[0];
    }
    
    /// <summary>
    /// Get all resource data (cached)
    /// </summary>
    public static ResourceData[] GetAllResourceData()
    {
        EnsureInitialized();
        return _allResourceData ?? new ResourceData[0];
    }
    
    /// <summary>
    /// Get all tech data (cached)
    /// </summary>
    public static TechData[] GetAllTechData()
    {
        EnsureInitialized();
        return _allTechData ?? new TechData[0];
    }
    
    /// <summary>
    /// Get all culture data (cached)
    /// </summary>
    public static CultureData[] GetAllCultureData()
    {
        EnsureInitialized();
        return _allCultureData ?? new CultureData[0];
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

