// Assets/Scripts/Managers/ImprovementManager.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ImprovementManager : MonoBehaviour
{
    public static ImprovementManager Instance { get; private set; }

    public enum ImprovementRemovalReason
    {
        Dismantled,
        Destroyed,
        Replaced,
    }

    public event System.Action<Civilization, ImprovementData, int, int> OnImprovementBuilt;
    public event System.Action<Civilization, ImprovementData, int, int, ImprovementRemovalReason> OnImprovementRemoved;

    [Header("Runtime")]
    [Tooltip("Incremented whenever road improvements change so cached road-network data can be invalidated")]
    public int roadNetworkVersion = 0;

    [System.Serializable]
    public class RoadConnectionConfig
    {
        [Tooltip("The improvement asset that acts as this road type")]
        public ImprovementData improvement;

        [Tooltip("Yield granted per connected city when this road type forms the connection")]
        public TileYield connectionYield;
    }

    [Header("Road Connection Settings")]
    [Tooltip("Define per-improvement connection yields here. If an improvement is listed, its connectionYield will be used when that improvement participates in a city-to-city connection.")]
    public List<RoadConnectionConfig> roadConnectionConfigs = new List<RoadConnectionConfig>();

    [Header("UI References")]
    [Tooltip("Optional: assign the ImprovementUpgradeUI instance here so ImprovementInstance can open it without a scene search.")]
    public ImprovementUpgradeUI improvementUpgradeUI;
    [Tooltip("Optional: world-space label prefab to show an icon above instantiated improvements")]
    public GameObject improvementLabelPrefab;

    // All active build jobs on the map
    private readonly List<BuildJob> jobs = new();
    // Parallel pipeline for worker-built combat units
    private readonly List<UnitJob> unitJobs = new();
    // Parallel pipeline for worker-built worker units
    private readonly List<WorkerJob> workerJobs = new();
    
    // Planet generator reference
    private PlanetGenerator planetGenerator;

    // Active traps by (planet,tile) key to avoid cross-planet index collisions
    private readonly Dictionary<long, TrapRuntime> traps = new Dictionary<long, TrapRuntime>();

    [System.Serializable]
    public class JobAssignmentSaveData
    {
        public int tileIndex;
        public List<string> assignedWorkerPersistentIds = new List<string>();
    }

    private struct TrapRuntime
    {
        public int planetIndex;
        public int tileIndex;
        public Civilization owner;
        public ImprovementData data;
        public int usesLeft;
        public bool armed;
    }


    // Get a reference to the planet generator (for legacy compatibility)
    private void InitializeReferences()
    {
        if (planetGenerator == null)
            // Use GameManager API for multi-planet support
        planetGenerator = GameManager.Instance?.GetCurrentPlanetGenerator();
    }
    
    /// <summary>
    /// Get tile data from any planet by checking all planets
    /// </summary>
    private HexTileData GetTileDataAcrossAllPlanets(int tileIndex, int planetIndex = -1)
    {
        int pIndex = ResolvePlanetIndex(planetIndex);
        var ts = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;
        return (ts != null) ? ts.GetTileData(tileIndex) : null;
    }

    private static int ResolvePlanetIndex(int planetIndex)
    {
        return planetIndex >= 0 ? planetIndex : (GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0);
    }

    private BuildJob FindBuildJob(int tileIndex, int planetIndex)
    {
        return jobs.Find(j => j.tileIndex == tileIndex && j.planetIndex == planetIndex);
    }

    private UnitJob FindUnitJob(int tileIndex, int planetIndex)
    {
        return unitJobs.Find(j => j.tileIndex == tileIndex && j.planetIndex == planetIndex);
    }

    private WorkerJob FindWorkerJob(int tileIndex, int planetIndex)
    {
        return workerJobs.Find(j => j.tileIndex == tileIndex && j.planetIndex == planetIndex);
    }

    private void SpawnConstructionVisual(ImprovementData data, int tileIndex, int planetIndex)
    {
        if (data == null || data.constructionPrefab == null) return;

        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null) return;

        var tileData = ts.GetTileData(tileIndex);
        if (tileData == null) return;

        if (tileData.improvementInstanceObject != null)
            Destroy(tileData.improvementInstanceObject);

        Vector3 pos = ts.GetTileSurfacePosition(tileIndex);
        GameObject constructionObject = Instantiate(data.constructionPrefab, pos, Quaternion.identity);
        // Prefer the tile's owner civ to determine the correct planet generator when available
        Civilization owner = tileData != null ? tileData.improvementOwner : null;
        PlanetGenerator planetGen = null;
        if (owner != null)
        {
            try { planetGen = owner.GetPlanetGeneratorForIndex(planetIndex); } catch { planetGen = null; }
            if (planetGen == null)
                Debug.LogWarning($"[ImprovementManager] Owner civ '{owner.civData?.civName ?? owner.name}' returned null for planet {planetIndex}; falling back to GameManager.");
        }
        if (planetGen == null)
        {
            planetGen = GameManager.Instance != null ? GameManager.Instance.GetPlanetGenerator(planetIndex) : null;
            if (planetGen == null)
                Debug.LogWarning($"[ImprovementManager] GameManager has no PlanetGenerator for index {planetIndex} when spawning construction visual.");
        }
        if (planetGen != null) constructionObject.transform.SetParent(planetGen.transform, true);

        // If improvement data specifies a placement root, snap the prefab so that
        // the root point ends up exactly at the tile surface position.
        try
        {
            if (!string.IsNullOrEmpty(data.placementRootName))
            {
                var root = constructionObject.transform.Find(data.placementRootName);
                if (root != null)
                {
                    Vector3 worldRoot = root.position;
                    Vector3 shift = pos - worldRoot;
                    constructionObject.transform.position += shift;
                }
                else
                {
                    // Fallback to explicit local position
                    Vector3 worldRoot = constructionObject.transform.TransformPoint(data.placementRootLocalPosition);
                    Vector3 shift = pos - worldRoot;
                    constructionObject.transform.position += shift;
                }
            }
            else
            {
                Vector3 worldRoot = constructionObject.transform.TransformPoint(data.placementRootLocalPosition);
                Vector3 shift = pos - worldRoot;
                constructionObject.transform.position += shift;
            }
        }
        catch { }

        // Register construction visual for wrap teleport
        try
        {
              var mgr = FindObjectsByType<HexMapChunkManager>(FindObjectsSortMode.None).FirstOrDefault(m => m.PlanetGenerator == planetGen);
            if (mgr != null) mgr.RegisterObjectForWrapAtTile(tileIndex, constructionObject);
        }
        catch { }
        tileData.improvementInstanceObject = constructionObject;
        ts.SetTileData(tileIndex, tileData);
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }
    
    void Start()
    {
        InitializeReferences();
        // Subscribe to planet-ready event so we can rehydrate upgrades after a planet is fully generated
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnPlanetReady += HandlePlanetReady;
        }
    }

    void OnEnable()
    {
    }

    void OnDisable()
    {
    }

    void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnPlanetReady -= HandlePlanetReady;
    }

    private void HandlePlanetReady(int planetIndex)
    {
        // Rehydrate all tile upgrades for the planet that just became ready
        RehydrateAllUpgradesOnPlanet(planetIndex);
    }

    /// <summary>
    /// Get the configured per-connection yield for a given improvement type.
    /// Returns an empty TileYield if not configured.
    /// </summary>
    public TileYield GetConnectionYieldForImprovement(ImprovementData imp)
    {
        if (imp == null) return new TileYield();
        foreach (var cfg in roadConnectionConfigs)
        {
            if (cfg != null && cfg.improvement == imp) return cfg.connectionYield;
        }
        return new TileYield();
    }

    /// <summary>
    /// Attempt to start a build job for this improvement on tileIndex.
    /// Returns false if a job already exists or tile is invalid.
    /// </summary>
    public bool CreateBuildJob(ImprovementData data, int tileIndex, Civilization owner, int planetIndex = -1)
    {
        planetIndex = ResolvePlanetIndex(planetIndex);
        // No duplicate jobs on same tile
        if (FindBuildJob(tileIndex, planetIndex) != null) { Debug.Log($"[ImprovementManager] CreateBuildJob rejected: duplicate job tile={tileIndex} planet={planetIndex}"); return false; }

        // Check tile requirements across all planets
        var td = GetTileDataAcrossAllPlanets(tileIndex, planetIndex);
        if (td == null) { Debug.Log($"[ImprovementManager] CreateBuildJob rejected: tile data null tile={tileIndex} planet={planetIndex}"); return false; }
        
        // Basic terrain checks
        if (data.isOrbitalImprovement)
        {
            // Orbital improvements: validate against surface biome below
            if (data.allowedBiomes != null && data.allowedBiomes.Length > 0 &&
                System.Array.IndexOf(data.allowedBiomes, td.biome) < 0 &&
                System.Array.IndexOf(data.allowedBiomes, Biome.Any) < 0)
                { Debug.Log($"[ImprovementManager] CreateBuildJob rejected: orbital allowedBiomes mismatch tile={tileIndex}"); return false; }
        }
        else if (data.isUnderwaterImprovement)
        {
            // Underwater improvements: tile must be a water tile with a valid underwaterBiome
            if (td.isLand) return false;
            if (td.underwaterBiome == Biome.Ocean && (data.allowedUnderwaterBiomes == null || data.allowedUnderwaterBiomes.Length == 0))
                { Debug.Log($"[ImprovementManager] CreateBuildJob rejected: underwater ocean floor not allowed tile={tileIndex}"); return false; } // plain ocean floor, and no explicit allowance
            if (data.allowedUnderwaterBiomes != null && data.allowedUnderwaterBiomes.Length > 0 &&
                System.Array.IndexOf(data.allowedUnderwaterBiomes, td.underwaterBiome) < 0)
                { Debug.Log($"[ImprovementManager] CreateBuildJob rejected: underwater allowedBiomes mismatch tile={tileIndex}"); return false; }
        }
        else
        {
            // Standard land improvements
            if (!td.isLand) { Debug.Log($"[ImprovementManager] CreateBuildJob rejected: not land tile={tileIndex}"); return false; }
            if (data.allowedBiomes != null && data.allowedBiomes.Length > 0 && 
                System.Array.IndexOf(data.allowedBiomes, td.biome) < 0) { Debug.Log($"[ImprovementManager] CreateBuildJob rejected: allowedBiomes mismatch tile={tileIndex} biome={td.biome}"); return false; }
        }
        
        // Territory control checks
        bool isOwnedByBuilder = td.owner == owner;
        bool isNeutral = td.owner == null;
        bool isEnemyTerritory = td.owner != null && td.owner != owner;
        
        // Check city requirement
        if (data.needsCity && !td.HasCity) { Debug.Log($"[ImprovementManager] CreateBuildJob rejected: needsCity but no city tile={tileIndex}"); return false; }
        
        // Check territory control requirements
        if (data.requiresControlledTerritory && !isOwnedByBuilder) { Debug.Log($"[ImprovementManager] CreateBuildJob rejected: requiresControlledTerritory not owned tile={tileIndex}"); return false; }
        if (isNeutral && !data.canBuildInNeutralTerritory) { Debug.Log($"[ImprovementManager] CreateBuildJob rejected: neutral territory not allowed tile={tileIndex}"); return false; }
        if (isEnemyTerritory && !data.canBuildInEnemyTerritory) { Debug.Log($"[ImprovementManager] CreateBuildJob rejected: enemy territory not allowed tile={tileIndex}"); return false; }

        if (!data.CanPayBuildCosts(owner)) { Debug.Log($"[ImprovementManager] CreateBuildJob rejected: insufficient build costs for {data.improvementName}"); return false; }
        if (!data.ConsumeBuildCosts(owner)) { Debug.Log($"[ImprovementManager] CreateBuildJob rejected: failed to consume build costs for {data.improvementName}"); return false; }

        var job = new BuildJob(tileIndex, planetIndex, owner, data);
        jobs.Add(job);
        SpawnConstructionVisual(data, tileIndex, planetIndex);
        return true;
    }

    /// <summary>
    /// Attempt to start a worker unit build job for a combat unit on tileIndex.
    /// Respects unit flags, requirements, limits, and tile occupancy.
    /// </summary>
    public bool CreateUnitJob(CombatUnitData unit, int tileIndex, Civilization owner, int planetIndex = -1)
    {
        if (unit == null || owner == null) return false;
        if (!unit.buildableByWorker) return false;
        if (!unit.AreRequirementsMet(owner)) return false;
        if (!LimitManager.Instance.CanCreateCombatUnit(owner, unit)) return false;

        // No duplicate jobs per tile
        planetIndex = ResolvePlanetIndex(planetIndex);
        if (FindUnitJob(tileIndex, planetIndex) != null) return false;

        // Tile must be valid and free
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var tileData = ts != null ? ts.GetTileData(tileIndex) : null;
        if (tileData == null) return false;
    // Allow job even if a worker is occupying the tile; we'll spawn the unit on a free neighbor if needed
    if (!tileData.isLand) return false; // basic restriction for now

        // Optional: validate adjacent friendly city or territory rules if desired later

        unitJobs.Add(new UnitJob(tileIndex, planetIndex, owner, unit));
        return true;
    }

    /// <summary>
    /// Attempt to start a worker build job for a worker unit on tileIndex.
    /// </summary>
    public bool CreateWorkerJob(WorkerUnitData unit, int tileIndex, Civilization owner, int planetIndex = -1)
    {
        if (unit == null || owner == null) return false;
        if (!unit.buildableByWorker) return false;
        if (!unit.AreRequirementsMet(owner)) return false;
        if (!LimitManager.Instance.CanCreateWorkerUnit(owner, unit)) return false;

        planetIndex = ResolvePlanetIndex(planetIndex);
        if (FindWorkerJob(tileIndex, planetIndex) != null) return false;

        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var tileData = ts != null ? ts.GetTileData(tileIndex) : null;
        if (tileData == null) return false;
        if (!tileData.isLand) return false;

        workerJobs.Add(new WorkerJob(tileIndex, planetIndex, owner, unit));
        return true;
    }

    /// <summary>
    /// Assign a worker to an existing build job on tileIndex. Returns true when assigned.
    /// Worker identity is tracked by GameObject InstanceID.
    /// </summary>
    public bool AssignWorkerToJob(int tileIndex, WorkerUnit worker, int planetIndex = -1)
    {
    if (worker == null) return false;
    planetIndex = ResolvePlanetIndex(planetIndex);
    var job = FindBuildJob(tileIndex, planetIndex);
    if (job == null) return false;
    string pid = worker.PersistentId;
    if (job.assignedWorkerPersistentIds == null) job.assignedWorkerPersistentIds = new List<string>();
    if (!job.assignedWorkerPersistentIds.Contains(pid)) job.assignedWorkerPersistentIds.Add(pid);
    // Notify listeners that a worker was assigned
    if (GameEventManager.Instance != null)
        GameEventManager.Instance.RaiseWorkerAssignedToJob(worker, tileIndex, planetIndex);
    return true;
    }

    /// <summary>
    /// Unassign a worker from a specific job.
    /// </summary>
    public void UnassignWorkerFromJob(int tileIndex, WorkerUnit worker, int planetIndex = -1)
    {
    if (worker == null) return;
    planetIndex = ResolvePlanetIndex(planetIndex);
    var job = FindBuildJob(tileIndex, planetIndex);
    if (job == null) return;
    string pid = worker.PersistentId;
    job.assignedWorkerPersistentIds?.RemoveAll(x => x == pid);
    // Notify listeners that a worker was unassigned
    if (GameEventManager.Instance != null)
        GameEventManager.Instance.RaiseWorkerUnassignedFromJob(worker, tileIndex, planetIndex);
    }

    /// <summary>
    /// Remove any assignment references for this worker across all jobs (called on death/move cleanup).
    /// </summary>
    public void UnassignWorkerFromAllJobs(WorkerUnit worker)
    {
        if (worker == null) return;
        string pid = worker.PersistentId;
        foreach (var j in jobs)
        {
            if (j.assignedWorkerPersistentIds != null && j.assignedWorkerPersistentIds.Contains(pid))
                j.assignedWorkerPersistentIds.RemoveAll(x => x == pid);
        }
    }

    /// <summary>
    /// Check if a worker is assigned to the build job on tileIndex.
    /// </summary>
    public bool JobAssignedToWorker(int tileIndex, WorkerUnit worker, int planetIndex = -1)
    {
        if (worker == null) return false;
        planetIndex = ResolvePlanetIndex(planetIndex);
        var job = FindBuildJob(tileIndex, planetIndex);
        if (job == null) return false;
        string pid = worker.PersistentId;
        return job.assignedWorkerPersistentIds != null && job.assignedWorkerPersistentIds.Contains(pid);
    }

    public bool HasBuildJobAtTile(int tileIndex, int planetIndex = -1, ImprovementData data = null)
    {
        planetIndex = ResolvePlanetIndex(planetIndex);
        var job = FindBuildJob(tileIndex, planetIndex);
        return job != null && (data == null || job.data == data);
    }

    // Legacy herd job pipeline removed; herds now manage their own production queues.

    public bool HasUnitJobAtTile(int tileIndex, int planetIndex = -1, CombatUnitData data = null)
    {
        planetIndex = ResolvePlanetIndex(planetIndex);
        var job = FindUnitJob(tileIndex, planetIndex);
        return job != null && (data == null || job.data == data);
    }

    public bool HasWorkerJobAtTile(int tileIndex, int planetIndex = -1, WorkerUnitData data = null)
    {
        planetIndex = ResolvePlanetIndex(planetIndex);
        var job = FindWorkerJob(tileIndex, planetIndex);
        return job != null && (data == null || job.data == data);
    }

    public bool HasAnyJobAtTile(int tileIndex, int planetIndex = -1)
    {
        planetIndex = ResolvePlanetIndex(planetIndex);
        return FindBuildJob(tileIndex, planetIndex) != null ||
               FindUnitJob(tileIndex, planetIndex) != null ||
               FindWorkerJob(tileIndex, planetIndex) != null;
    }

    /// <summary>
    /// Return the ImprovementData for the active build job on the given tile, or null if none.
    /// </summary>
    public ImprovementData GetBuildJobDataAtTile(int tileIndex, int planetIndex = -1)
    {
        planetIndex = ResolvePlanetIndex(planetIndex);
        var job = FindBuildJob(tileIndex, planetIndex);
        return job != null ? job.data : null;
    }

    /// <summary>
    /// Export current job assignments (persistent worker ids) for saving.
    /// </summary>
    public List<JobAssignmentSaveData> ExportJobAssignments()
    {
        var outList = new List<JobAssignmentSaveData>();
        foreach (var j in jobs)
        {
            if (j.assignedWorkerPersistentIds != null && j.assignedWorkerPersistentIds.Count > 0)
            {
                outList.Add(new JobAssignmentSaveData { tileIndex = j.tileIndex, assignedWorkerPersistentIds = new List<string>(j.assignedWorkerPersistentIds) });
            }
        }
        return outList;
    }

    /// <summary>
    /// Restore job assignments from saved persistent ids. Call after jobs and units are restored.
    /// </summary>
    public void ImportJobAssignments(List<JobAssignmentSaveData> data)
    {
        if (data == null) return;
        foreach (var d in data)
        {
            var job = jobs.Find(j => j.tileIndex == d.tileIndex);
            if (job == null) continue;
            job.assignedWorkerPersistentIds = new List<string>(d.assignedWorkerPersistentIds ?? new List<string>());
        }
    }

    /// <summary>
    /// Apply work points from a worker to the job on its tile.
    /// </summary>
    public void AddWork(int tileIndex, int workPoints, int planetIndex = -1)
    {
        planetIndex = ResolvePlanetIndex(planetIndex);
        var job = FindBuildJob(tileIndex, planetIndex);
        if (job == null) return;

        job.remainingWork -= workPoints;
        job.Clamp();

        if (job.remainingWork <= 0)
            CompleteJob(job);
    }

    /// <summary>
    /// Apply work points to a unit job on this tile.
    /// </summary>
    public void AddUnitWork(int tileIndex, int workPoints, int planetIndex = -1)
    {
        planetIndex = ResolvePlanetIndex(planetIndex);
        var job = FindUnitJob(tileIndex, planetIndex);
        if (job == null) return;

        job.remainingWork -= workPoints;
        job.Clamp();

        if (job.remainingWork <= 0)
            CompleteUnitJob(job);
    }

    /// <summary>
    /// Apply work points to a worker unit job on this tile.
    /// </summary>
    public void AddWorkerWork(int tileIndex, int workPoints, int planetIndex = -1)
    {
        planetIndex = ResolvePlanetIndex(planetIndex);
        var job = FindWorkerJob(tileIndex, planetIndex);
        if (job == null) return;

        job.remainingWork -= workPoints;
        job.Clamp();

        if (job.remainingWork <= 0)
            CompleteWorkerJob(job);
    }

    /// <summary>
    /// Called each turn by TurnManager after civ's turn, if you want auto-progress.
    /// </summary>
    public void ProcessTurn(Civilization civ)
    {
        // Herd production is handled by Herd.ProcessProduction called from Civilization per-turn loop.
    }

    /// <summary>
    /// Apply per-turn yields from owned, non-city improvements to a civilization.
    /// This iterates all planets and tiles and credits the civ with improvement yields
    /// (excluding resource-node yields which are handled by ResourceManager).
    /// </summary>
    public void ApplyImprovementYieldsToCiv(Civilization civ)
    {
        if (civ == null || GameManager.Instance == null) return;
        var planetData = GameManager.Instance.GetPlanetData();
        foreach (var kvp in planetData)
        {
            int planetIndex = kvp.Key;
            var planetGen = GameManager.Instance.GetPlanetGenerator(planetIndex);
            int tileCount = planetGen?.Grid?.TileCount ?? 0;
            var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
            for (int ti = 0; ti < tileCount; ti++)
            {
                var td = ts != null ? ts.GetTileData(ti) : (planetGen != null ? planetGen.GetHexTileData(ti) : null);
                if (td == null) continue;
                if (td.owner != civ) continue;
                if (td.HasCity) continue; // cities already contribute their tiles
                if (!td.HasImprovement) continue;

                var ty = td.GetTotalYield();
                if (td.HasResource && td.resource != null)
                {
                    ty.Food -= td.resource.foodPerTurn;
                    ty.Gold -= td.resource.goldPerTurn;
                    ty.Science -= td.resource.sciencePerTurn;
                    ty.Culture -= td.resource.culturePerTurn;
                    ty.Policy -= td.resource.policyPointsPerTurn;
                    ty.Faith -= td.resource.faithPerTurn;
                }

                if (ty.Food != 0) civ.AddFood(ty.Food);
                if (ty.Gold != 0) civ.AddGold(ty.Gold);
                if (ty.Science != 0) civ.science += ty.Science;
                if (ty.Culture != 0) civ.culture += ty.Culture;
                if (ty.Policy != 0) civ.AddPolicyPoints(ty.Policy);
                if (ty.Faith != 0) civ.AddFaith(ty.Faith);
                // Note: production remains city-scoped and is not applied here.
            }
        }
    }

    /// <summary>
    /// Apply the immediate yields for a single tile improvement to the owning civ.
    /// Used to give instant UI feedback when an upgrade is built.
    /// </summary>
    public void ApplyImprovementYieldsForTile(int tileIndex, Civilization civ, int planetIndex = -1)
    {
        if (civ == null) return;
        int pIndex = ResolvePlanetIndex(planetIndex);
        var ts = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;
        var planetGen = civ != null ? civ.GetPlanetGeneratorForIndex(pIndex) ?? (GameManager.Instance != null ? GameManager.Instance.GetPlanetGenerator(pIndex) : null)
                       : (GameManager.Instance != null ? GameManager.Instance.GetPlanetGenerator(pIndex) : null);
        var td = ts != null ? ts.GetTileData(tileIndex) : (planetGen != null ? planetGen.GetHexTileData(tileIndex) : null);
        if (td == null) return;
        if (td.owner != civ) return;
        if (!td.HasImprovement) return;
        if (td.HasCity) return;

        var ty = td.GetTotalYield();
        if (td.HasResource && td.resource != null)
        {
            ty.Food -= td.resource.foodPerTurn;
            ty.Gold -= td.resource.goldPerTurn;
            ty.Science -= td.resource.sciencePerTurn;
            ty.Culture -= td.resource.culturePerTurn;
            ty.Policy -= td.resource.policyPointsPerTurn;
            ty.Faith -= td.resource.faithPerTurn;
        }

        if (ty.Food != 0) civ.AddFood(ty.Food);
        if (ty.Gold != 0) civ.AddGold(ty.Gold);
        if (ty.Culture != 0) civ.culture += ty.Culture;
        if (ty.Policy != 0) civ.AddPolicyPoints(ty.Policy);
        if (ty.Faith != 0) civ.AddFaith(ty.Faith);
    }

    private void CompleteJob(BuildJob job)
    {
        var ts = TileSystem.GetForPlanet(job.planetIndex) ?? TileSystem.Instance;
        Vector3 pos = ts != null ? ts.GetTileSurfacePosition(job.tileIndex) : Vector3.zero;
        var planetGen = job.owner != null ? job.owner.GetPlanetGeneratorForIndex(job.planetIndex) ?? (GameManager.Instance != null ? GameManager.Instance.GetPlanetGenerator(job.planetIndex) : null)
                         : (GameManager.Instance != null ? GameManager.Instance.GetPlanetGenerator(job.planetIndex) : null);
        GameObject completedImprovement = null;

        var tileData = ts != null ? ts.GetTileData(job.tileIndex) : null;
        GameObject previousConstructionObject = tileData != null ? tileData.improvementInstanceObject : null;
        if (tileData != null)
        {
            tileData.improvement = job.data;
            // Persist owner on tile data for save/load and gameplay checks
            tileData.improvementOwner = job.owner;
            tileData.improvementInstanceObject = null;
            if (ts != null) ts.SetTileData(job.tileIndex, tileData);
        }

        if (job.data.completePrefab != null)
        {
            completedImprovement = Instantiate(job.data.completePrefab, pos, Quaternion.identity);
            // Keep hierarchy organized: parent improvements under their planet generator.
            if (planetGen != null) completedImprovement.transform.SetParent(planetGen.transform, true);

            // Snap the completed prefab so its placement root (named child or fallback local pos)
            // is exactly on the tile surface position.
            try
            {
                var data = job.data;
                if (!string.IsNullOrEmpty(data.placementRootName))
                {
                    var root = completedImprovement.transform.Find(data.placementRootName);
                    if (root != null)
                    {
                        Vector3 worldRoot = root.position;
                        Vector3 shift = pos - worldRoot;
                        completedImprovement.transform.position += shift;
                    }
                    else
                    {
                        Vector3 worldRoot = completedImprovement.transform.TransformPoint(data.placementRootLocalPosition);
                        Vector3 shift = pos - worldRoot;
                        completedImprovement.transform.position += shift;
                    }
                }
                else
                {
                    Vector3 worldRoot = completedImprovement.transform.TransformPoint(data.placementRootLocalPosition);
                    Vector3 shift = pos - worldRoot;
                    completedImprovement.transform.position += shift;
                }
            }
            catch { }

            // Register completed improvement for wrap teleport
                        try
                        {
                                    var mgr = FindObjectsByType<HexMapChunkManager>(FindObjectsSortMode.None).FirstOrDefault(m => m.PlanetGenerator == planetGen);
                                if (mgr != null) mgr.RegisterObjectForWrapAtTile(job.tileIndex, completedImprovement);
                        }
            catch { }

            // Attach ImprovementInstance component to track applied upgrades and attached parts
            var instance = completedImprovement.GetComponent<ImprovementInstance>();
            if (instance == null) instance = completedImprovement.AddComponent<ImprovementInstance>();
            instance.tileIndex = job.tileIndex;
            instance.data = job.data;
            // Record owning civ on runtime instance
            instance.owner = job.owner;

            // Initialize the runtime ImprovementInstance so it can handle clicks and state
            instance.Initialize(job.tileIndex, job.data, job.planetIndex);

            // If this improvement is a shelter and there were assigned workers, automatically
            // place any assigned worker that is currently on the tile inside the shelter.
            if (job.data != null && job.data.isShelter && instance != null && job.assignedWorkerPersistentIds != null)
            {
                foreach (var pid in job.assignedWorkerPersistentIds)
                {
                    var goWorker = UnitRegistry.GetByPersistentId(pid);
                    if (goWorker == null) continue;
                    var workerComp = goWorker.GetComponent<WorkerUnit>();
                    if (workerComp == null) continue;
                    // Only store if worker is on the same planet and tile
                    if (workerComp.planetIndex == job.planetIndex && workerComp.currentTileIndex == job.tileIndex)
                    {
                        // ImprovementInstance.StoreUnit will handle occupancy clearing and hiding the unit
                        instance.StoreUnit(workerComp);
                    }
                }
            }
            // Add collider if needed for clicking
            if (completedImprovement.GetComponent<Collider>() == null)
            {
                var collider = completedImprovement.AddComponent<BoxCollider>();
                // Adjust collider size as needed
                collider.size = Vector3.one * 2f;
            }

            // Store runtime reference on the tile data for later upgrade application
            tileData = ts != null ? ts.GetTileData(job.tileIndex) : null;
            if (tileData != null)
            {
                tileData.improvementInstanceObject = completedImprovement;
                if (ts != null) ts.SetTileData(job.tileIndex, tileData);
            }
        }

        if (previousConstructionObject != null && previousConstructionObject != completedImprovement)
            Destroy(previousConstructionObject);


        // If the completed improvement is a road, bump the network version to invalidate caches
        if (job.data != null && job.data.isRoad)
        {
            roadNetworkVersion++;
        }

        // Register trap runtime state if this improvement is a trap
        if (job.data.isTrap)
        {
            long trapKey = ((long)job.planetIndex << 32) ^ (uint)job.tileIndex;
            traps[trapKey] = new TrapRuntime
            {
                planetIndex = job.planetIndex,
                tileIndex = job.tileIndex,
                owner = job.owner,
                data = job.data,
                usesLeft = Mathf.Max(1, job.data.trapMaxTriggers),
                armed = true
            };
        }

        // Notify any assigned workers that the job is complete so they can clear their build animation/state
        if (job.assignedWorkerPersistentIds != null && job.assignedWorkerPersistentIds.Count > 0)
        {
            foreach (var pid in job.assignedWorkerPersistentIds)
            {
                var go = UnitRegistry.GetByPersistentId(pid);
                if (go == null) continue;
                var worker = go.GetComponent<WorkerUnit>();
                if (worker == null) continue;
                // Raise unassigned event so listeners (WorkerUnit) clear animator flag and any UI updates
                if (GameEventManager.Instance != null)
                    GameEventManager.Instance.RaiseWorkerUnassignedFromJob(worker, job.tileIndex, job.planetIndex);
            }
        }

        jobs.Remove(job);
        OnImprovementBuilt?.Invoke(job.owner, job.data, job.tileIndex, job.planetIndex);
    }

    // Legacy herd job completion removed; herds handle their own build completion.

    private void CompleteUnitJob(UnitJob job)
    {
        var ts = TileSystem.GetForPlanet(job.planetIndex) ?? TileSystem.Instance;
        var occ = TileOccupancyManager.GetForPlanet(job.planetIndex) ?? TileOccupancyManager.Instance;
        var planetGen = job.owner != null ? job.owner.GetPlanetGeneratorForIndex(job.planetIndex) ?? (GameManager.Instance != null ? GameManager.Instance.GetPlanetGenerator(job.planetIndex) : null)
                         : (GameManager.Instance != null ? GameManager.Instance.GetPlanetGenerator(job.planetIndex) : null);
        // Resolve unit data for this civilization (apply unique/unit replacements) and spawn
        var resolvedData = job.owner != null ? job.owner.GetUnitData(job.data) : job.data;
        // Spawn the unit and register occupancy
        var unitPrefab = resolvedData.GetPrefab(job.owner);
        if (unitPrefab == null)
        {
            Debug.LogError($"Unit {job.data?.unitName} has no prefab; cannot spawn.");
            unitJobs.Remove(job);
            return;
        }

        // Find a valid spawn tile (prefer job tile if unoccupied)
        int spawnIndex = FindSpawnTile(job.tileIndex, job.planetIndex);
        Vector3 pos = ts != null ? ts.GetTileSurfacePosition(spawnIndex) : Vector3.zero;
    var go = Object.Instantiate(unitPrefab, pos, Quaternion.identity);
        // Keep hierarchy organized: parent units under their planet generator.
        if (planetGen != null) go.transform.SetParent(planetGen.transform, true);
        // Register unit with wrap registry
              try { var mgr = FindObjectsByType<HexMapChunkManager>(FindObjectsSortMode.None).FirstOrDefault(m => m.PlanetGenerator == planetGen); if (mgr != null) mgr.RegisterObjectForWrapAtTile(spawnIndex, go); } catch { }
        var unit = go.GetComponent<CombatUnit>();
        if (unit == null)
        {
            Debug.LogError("Spawned unit prefab missing CombatUnit component.");
            Object.Destroy(go);
            unitJobs.Remove(job);
            return;
        }

        unit.Initialize(resolvedData, job.owner);
        unit.InitializeAndReturn(resolvedData, job.owner, spawnIndex);
        job.owner.combatUnits.Add(unit);
        LimitManager.Instance.AddCombatUnit(job.owner, resolvedData);
        // Determine layer (centralized rules) and convert to occupancy layer
        var tdata = ts != null ? ts.GetTileData(spawnIndex) : null;
        var spawnLayer = UnitLayerRules.GetSpawnLayerForUnit(unit, tdata);
        if (!LayerConversion.TryToTileLayer(spawnLayer, out var occLayer)) occLayer = TileLayer.Surface;
        unit.currentLayer = occLayer;
        unit.planetIndex = job.planetIndex;
        unit.currentTileIndex = spawnIndex;
        // Register this unit in the global registry, then claim occupancy
        try { unit.RegisterToRegistry(); } catch { }
        try { occ?.SetOccupant(spawnIndex, unit.gameObject, unit.currentLayer); } catch { }

        unitJobs.Remove(job);
    }

    private void CompleteWorkerJob(WorkerJob job)
    {
        var ts = TileSystem.GetForPlanet(job.planetIndex) ?? TileSystem.Instance;
        var occ = TileOccupancyManager.GetForPlanet(job.planetIndex) ?? TileOccupancyManager.Instance;
        var planetGen = job.owner != null ? job.owner.GetPlanetGeneratorForIndex(job.planetIndex) ?? (GameManager.Instance != null ? GameManager.Instance.GetPlanetGenerator(job.planetIndex) : null)
                         : (GameManager.Instance != null ? GameManager.Instance.GetPlanetGenerator(job.planetIndex) : null);
        // Spawn the worker unit and register occupancy
        var prefab = job.data.GetPrefab(job.owner);
        if (prefab == null)
        {
            Debug.LogError($"Worker unit {job.data?.unitName} has no prefab; cannot spawn.");
            workerJobs.Remove(job);
            return;
        }

        int spawnIndex = FindSpawnTile(job.tileIndex, job.planetIndex);
        Vector3 pos = ts != null ? ts.GetTileSurfacePosition(spawnIndex) : Vector3.zero;
        var go = Object.Instantiate(prefab, pos, Quaternion.identity);
        // Keep hierarchy organized: parent workers under their planet generator.
        if (planetGen != null) go.transform.SetParent(planetGen.transform, true);
        try { var mgr = FindObjectsByType<HexMapChunkManager>(FindObjectsSortMode.None).FirstOrDefault(m => m.PlanetGenerator == planetGen); if (mgr != null) mgr.RegisterObjectForWrapAtTile(spawnIndex, go); } catch { }
        var unit = go.GetComponent<WorkerUnit>();
        if (unit == null)
        {
            Debug.LogError("Spawned worker prefab missing WorkerUnit component.");
            Object.Destroy(go);
            workerJobs.Remove(job);
            return;
        }

        unit.Initialize(job.data, job.owner, spawnIndex);
        job.owner.workerUnits.Add(unit);
        LimitManager.Instance.AddWorkerUnit(job.owner, job.data);
        var tdataW = ts != null ? ts.GetTileData(spawnIndex) : null;
        var spawnLayerW = UnitLayerRules.GetSpawnLayerForUnit(unit, tdataW);
        if (!LayerConversion.TryToTileLayer(spawnLayerW, out var occLayerW)) occLayerW = TileLayer.Surface;
        unit.currentLayer = occLayerW;
        unit.planetIndex = job.planetIndex;
        unit.currentTileIndex = spawnIndex;
        try { unit.RegisterToRegistry(); } catch { }
        try { occ?.SetOccupant(spawnIndex, unit.gameObject, unit.currentLayer); } catch { }

        workerJobs.Remove(job);
    }

    private int FindSpawnTile(int centerIndex, int planetIndex)
    {
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        if (ts == null || !ts.IsReady()) return centerIndex;

        // If center tile is free, use it
        var tileData = ts.GetTileData(centerIndex);
        // Prefer occupancy manager when checking for free surface occupant
        var occCenter = occ != null ? occ.GetOccupantObjectWithFallback(centerIndex, TileLayer.Surface) : null;
        if (occCenter == null) return centerIndex;

        // Otherwise try neighbors
        var neighbors = ts.GetNeighbors(centerIndex);
        foreach (int n in neighbors)
        {
            var td = ts.GetTileData(n);
            bool free = false;
            var occObj = occ != null ? occ.GetOccupantObjectWithFallback(n, TileLayer.Surface) : null;
            free = occObj == null;

            if (td != null && td.isLand && free)
                return n;
        }

        // Fallback to center
        return centerIndex;
    }


    /// <summary>
    /// Represents a construction project on a tile.
    /// </summary>
    private class BuildJob
    {
        public int tileIndex;
        public int planetIndex;
        public Civilization owner;
        public ImprovementData data;
        public int remainingWork;
    // Track assigned workers by GameObject InstanceID so workers can auto-contribute each turn
    public List<int> assignedWorkerInstanceIds = new List<int>();
    // Persistent worker identifiers (GUIDs) to survive save/load
    public List<string> assignedWorkerPersistentIds = new List<string>();

        public BuildJob(int tileIndex, int planetIndex, Civilization owner, ImprovementData data)
        {
            this.tileIndex = tileIndex;
            this.planetIndex = planetIndex;
            this.owner = owner;
            this.data = data;
            this.remainingWork = Mathf.Max(1, data.workCost);
        }

        public void Clamp()
        {
            if (remainingWork < 0) remainingWork = 0;
        }
    }

    /// <summary>
    /// Represents a worker-built combat unit job on a tile.
    /// </summary>
    private class UnitJob
    {
        public int tileIndex;
        public int planetIndex;
        public Civilization owner;
        public CombatUnitData data;
        public int remainingWork;

        public UnitJob(int tileIndex, int planetIndex, Civilization owner, CombatUnitData data)
        {
            this.tileIndex = tileIndex;
            this.planetIndex = planetIndex;
            this.owner = owner;
            this.data = data;
            this.remainingWork = Mathf.Max(1, data.workerWorkCost);
        }

        public void Clamp()
        {
            if (remainingWork < 0) remainingWork = 0;
        }
    }

    /// <summary>
    /// Represents a worker-built worker unit job on a tile.
    /// </summary>
    private class WorkerJob
    {
        public int tileIndex;
        public int planetIndex;
        public Civilization owner;
        public WorkerUnitData data;
        public int remainingWork;

        public WorkerJob(int tileIndex, int planetIndex, Civilization owner, WorkerUnitData data)
        {
            this.tileIndex = tileIndex;
            this.planetIndex = planetIndex;
            this.owner = owner;
            this.data = data;
            this.remainingWork = Mathf.Max(1, data.workerWorkCost);
        }

        public void Clamp()
        {
            if (remainingWork < 0) remainingWork = 0;
        }
    }

    /// <summary>
    /// Notify the manager that a unit has entered a tile. Will trigger trap if present and applicable.
    /// </summary>
    public void NotifyUnitEnteredTile(int tileIndex, CombatUnit unit)
    {
        if (unit == null) return;
        // Get tile data up front so both trap processing and store logic can use it
        var ts = TileSystem.GetForPlanet(unit.planetIndex) ?? TileSystem.Instance;
        var tileData = ts != null ? ts.GetTileData(tileIndex) : null;

        // If there's an active trap runtime entry for this tile, process it.
        long trapKey = ((long)unit.planetIndex << 32) ^ (uint)tileIndex;
        if (traps.TryGetValue(trapKey, out var trap))
        {
            if (trap.armed && trap.usesLeft > 0)
            {
                // Validate improvement still exists and is a trap
                if (!(tileData?.improvement == null || !tileData.improvement.isTrap))
                {
                    // Friendly safe
                    if (!(trap.data.trapFriendlySafe && unit.owner == trap.owner))
                    {
                        // Category filter
                        var cat = unit.data != null ? unit.data.unitType : CombatCategory.Spearman;
                        bool affects = trap.data.trapAffectsAnimalsOnly
                            ? (cat == CombatCategory.Animal)
                            : (trap.data.trapAffectedCategories != null && System.Array.IndexOf(trap.data.trapAffectedCategories, cat) >= 0);
                        if (affects)
                        {
                            int dmg = Mathf.Max(0, trap.data.trapDamage);
                            if (dmg > 0) unit.ApplyDamage(dmg);
                            if (trap.data.trapImmobilize && trap.data.trapImmobilizeTurns > 0)
                            {
                                unit.ApplyTrap(trap.data.trapImmobilizeTurns);
                            }

                            // Decrement uses and update or remove
                            trap.usesLeft--;
                            traps[trapKey] = trap;
                            if (trap.usesLeft <= 0 && trap.data.trapConsumeOnDeplete)
                            {
                                RemoveImprovement(tileIndex, unit.planetIndex);
                            }
                        }
                    }
                }
            }
        }

        // After trap processing (or if no trap), attempt to auto-store unit if tile improvement is a shelter
        var tileDataStore = tileData;
        if (tileDataStore != null && tileDataStore.improvement != null && tileDataStore.improvement.isShelter)
        {
            var instanceObj = tileDataStore.improvementInstanceObject;
            if (instanceObj != null)
            {
                var impInst = instanceObj.GetComponent<ImprovementInstance>();
                if (impInst != null && impInst.owner == unit.owner)
                {
                    // Only store if capacity allows
                    if (impInst.storedUnits == null || impInst.storedUnits.Count < impInst.GetShelterCapacity())
                    {
                        bool stored = impInst.StoreUnit(unit);
                        if (stored)
                        {
                            // Successfully stored; stop further processing
                            return;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Notify the manager that a worker has entered a tile. Triggers trap if present and applicable.
    /// Workers are affected by traps unless the trap is animals-only or friendly-safe.
    /// </summary>
    public void NotifyUnitEnteredTile(int tileIndex, WorkerUnit worker)
    {
        if (worker == null) return;

        // Get tile data up front so both trap processing and store logic can use it
        var ts = TileSystem.GetForPlanet(worker.planetIndex) ?? TileSystem.Instance;
        var tileData = ts != null ? ts.GetTileData(tileIndex) : null;

        // If there's an active trap runtime entry for this tile, process it.
        long trapKey = ((long)worker.planetIndex << 32) ^ (uint)tileIndex;
        if (traps.TryGetValue(trapKey, out var trap))
        {
            if (trap.armed && trap.usesLeft > 0)
            {
                // Validate improvement still exists and is a trap
                if (!(tileData?.improvement == null || !tileData.improvement.isTrap))
                {
                    // Friendly safe
                    if (!(trap.data.trapFriendlySafe && worker.owner == trap.owner))
                    {
                        // If trap is animals-only, skip workers
                        if (!trap.data.trapAffectsAnimalsOnly)
                        {
                            int dmg = Mathf.Max(0, trap.data.trapDamage);
                            if (dmg > 0) worker.ApplyDamage(dmg);
                            if (trap.data.trapImmobilize && trap.data.trapImmobilizeTurns > 0)
                            {
                                worker.ApplyTrap(trap.data.trapImmobilizeTurns);
                            }

                            // Decrement uses and update or remove
                            trap.usesLeft--;
                            traps[trapKey] = trap;
                            if (trap.usesLeft <= 0 && trap.data.trapConsumeOnDeplete)
                            {
                                RemoveImprovement(tileIndex, worker.planetIndex);
                            }
                        }
                    }
                }
            }
        }

        // After trap processing (or if no trap), attempt to auto-store worker if tile improvement is a shelter
        var tileDataStore = tileData;
        if (tileDataStore != null && tileDataStore.improvement != null && tileDataStore.improvement.isShelter)
        {
            var instanceObj = tileDataStore.improvementInstanceObject;
            if (instanceObj != null)
            {
                var impInst = instanceObj.GetComponent<ImprovementInstance>();
                if (impInst != null && impInst.owner == worker.owner)
                {
                    if (impInst.storedUnits == null || impInst.storedUnits.Count < impInst.GetShelterCapacity())
                    {
                        bool stored = impInst.StoreUnit(worker);
                        if (stored)
                        {
                            return;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Remove any improvement from a tile, including trap state.
    /// </summary>
    public void RemoveImprovement(int tileIndex, int planetIndex = -1)
    {
        RemoveImprovementInternal(tileIndex, planetIndex, null, ImprovementRemovalReason.Destroyed, false);
    }

    public bool DismantleImprovement(int tileIndex, Civilization actor, int planetIndex = -1)
    {
        int resolvedPlanet = planetIndex >= 0 ? planetIndex : (GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0);
        var ts = TileSystem.GetForPlanet(resolvedPlanet) ?? TileSystem.Instance;
        var tileData = ts != null ? ts.GetTileData(tileIndex) : null;
        if (tileData == null || tileData.improvement == null) return false;
        if (!tileData.improvement.canBeDismantled) return false;
        if (actor != null && tileData.improvementOwner != null && tileData.improvementOwner != actor) return false;

        var refundCiv = tileData.improvementOwner ?? actor;
        RemoveImprovementInternal(tileIndex, resolvedPlanet, refundCiv, ImprovementRemovalReason.Dismantled, true);
        return true;
    }

    private void RemoveImprovementInternal(int tileIndex, int planetIndex, Civilization refundCiv, ImprovementRemovalReason reason, bool refundCosts)
    {
        if (planetIndex < 0) planetIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var tileData = ts != null ? ts.GetTileData(tileIndex) : null;
        if (tileData == null) return;
        var data = tileData.improvement;
        if (data == null) return;
        var owner = tileData.improvementOwner;
        var instanceObject = tileData.improvementInstanceObject;

        // Optional destroyed prefab
        if (reason == ImprovementRemovalReason.Destroyed && data.destroyedPrefab != null)
        {
            var go = Instantiate(data.destroyedPrefab, ts != null ? ts.GetTileSurfacePosition(tileIndex) : Vector3.zero, Quaternion.identity);
            try { var mgr = FindObjectsByType<HexMapChunkManager>(FindObjectsSortMode.None).FirstOrDefault(m => m.PlanetGenerator == planetGenerator); if (mgr != null) mgr.RegisterObjectForWrapAtTile(tileIndex, go); } catch { }
            var planetGen = GameManager.Instance != null ? GameManager.Instance.GetPlanetGenerator(planetIndex) : null; // no civ context here
            if (planetGen != null) go.transform.SetParent(planetGen.transform, true);
        }

        if (instanceObject != null)
            Destroy(instanceObject);

        if (refundCosts && refundCiv != null)
            data.RefundDismantleCosts(refundCiv);

        tileData.improvement = null;
        tileData.improvementOwner = null;
        tileData.improvementInstanceObject = null;
        tileData.builtUpgrades = null;
        ts?.SetTileData(tileIndex, tileData);

        if (data.isRoad)
            roadNetworkVersion++;

        long trapKey = ((long)planetIndex << 32) ^ (uint)tileIndex;
        traps.Remove(trapKey);

        OnImprovementRemoved?.Invoke(owner, data, tileIndex, planetIndex, reason);
    }

    /// <summary>
    /// Re-apply saved built upgrades to the runtime instantiated improvement on a tile.
    /// Call this after loading the map to rehydrate visual attachments for modular upgrades.
    /// </summary>
    // Planet-aware rehydration: if planetIndex >= 0, use planet-aware tile lookup so this works in multi-planet mode
    public void RehydrateTileUpgrades(int tileIndex, int planetIndex = -1)
    {
        var ts = (planetIndex >= 0) ? (TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance) : TileSystem.Instance;
        HexTileData tileData = (planetIndex >= 0) ? ts?.GetTileDataFromPlanet(tileIndex, planetIndex) : ts?.GetTileData(tileIndex);

        if (tileData == null) return;
        if (tileData.improvement == null || tileData.improvementInstanceObject == null) return;

        var instanceObj = tileData.improvementInstanceObject;
        var impInstance = instanceObj.GetComponent<ImprovementInstance>();
        if (impInstance == null) impInstance = instanceObj.AddComponent<ImprovementInstance>();
        impInstance.tileIndex = tileIndex;
        impInstance.data = tileData.improvement;
        // Restore owner on runtime instance from persisted tile data
        impInstance.owner = tileData.improvementOwner;

        if (tileData.builtUpgrades == null || tileData.builtUpgrades.Count == 0) return;

        foreach (var built in tileData.builtUpgrades)
        {
            // Find the corresponding upgrade definition on the improvement
            var found = System.Array.Find(tileData.improvement.availableUpgrades, u => (!string.IsNullOrEmpty(u.upgradeId) ? u.upgradeId == built : u.upgradeName == built));
            if (found == null) continue;

            // Apply visuals the same way BuildUpgrade would (attach or replace)
            string upgradeKey = !string.IsNullOrEmpty(found.upgradeId) ? found.upgradeId : found.upgradeName;
            if (impInstance.HasApplied(upgradeKey)) continue;

            if (found.makesVisualChange)
            {
                if (found.replacePrefab != null)
                {
                    Vector3 pos = instanceObj.transform.position;
                    Quaternion rot = instanceObj.transform.rotation;
                    var newObj = Instantiate(found.replacePrefab, pos, rot);
                    try { var mgr = FindObjectsByType<HexMapChunkManager>(FindObjectsSortMode.None).FirstOrDefault(m => m.PlanetGenerator == planetGenerator); if (mgr != null) mgr.RegisterObjectForWrapAtTile(tileIndex, newObj); } catch { }
                    var planetGen = GameManager.Instance != null ? GameManager.Instance.GetPlanetGenerator(planetIndex) : null; // no civ context here
                    if (planetGen != null) newObj.transform.SetParent(planetGen.transform, true);
                    var newInst = newObj.GetComponent<ImprovementInstance>() ?? newObj.AddComponent<ImprovementInstance>();
                    newInst.tileIndex = tileIndex;
                    newInst.data = impInstance.data;
                    newInst.appliedUpgrades = new System.Collections.Generic.HashSet<string>(impInstance.appliedUpgrades);

                    newInst.Initialize(tileIndex, tileData.improvement, planetIndex);

                    tileData.improvementInstanceObject = newObj;
                    // Persist change back to the correct planet
                    if (planetIndex >= 0) ts?.SetTileDataOnPlanet(tileIndex, tileData, planetIndex);
                    else ts?.SetTileData(tileIndex, tileData);

                    Destroy(instanceObj);
                    instanceObj = newObj;
                    impInstance = newInst;
                }
                else if (found.attachPrefabs != null)
                {
                    foreach (var prefab in found.attachPrefabs)
                    {
                        if (prefab == null) continue;
                        bool already = false;
                        foreach (var child in impInstance.attachedParts)
                        {
                            if (child != null && child.name.Contains(prefab.name)) { already = true; break; }
                        }
                        if (already) continue;

                        var go = Instantiate(prefab, instanceObj.transform);
                        go.transform.localPosition = Vector3.zero;
                        go.transform.localRotation = Quaternion.identity;
                        impInstance.attachedParts.Add(go);
                    }
                }

                impInstance.MarkApplied(upgradeKey);
            }
        }

        // After applying all visuals, recompute defense aggregates and persist tile data
        tileData.RecomputeImprovementDefenseAggregates();
        if (planetIndex >= 0) ts?.SetTileDataOnPlanet(tileIndex, tileData, planetIndex);
        else ts?.SetTileData(tileIndex, tileData);
    }

    /// <summary>
    /// Return the nearest armed trap tile on the given planet that can affect animals within maxRange.
    /// Returns null if none found.
    /// </summary>
    public int? GetNearestTrapForAnimals(int planetIndex, int fromTile, int maxRange)
    {
        if (GameManager.Instance == null) return null;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null) return null;

        int bestTile = -1;
        int bestDist = int.MaxValue;
        foreach (var kv in traps)
        {
            var t = kv.Value;
            if (t.planetIndex != planetIndex) continue;
            if (!t.armed || t.usesLeft <= 0) continue;
            var data = t.data;
            if (data == null) continue;

            bool affectsAnimals = data.trapAffectsAnimalsOnly ||
                (data.trapAffectedCategories != null && System.Array.IndexOf(data.trapAffectedCategories, CombatCategory.Animal) >= 0);
            if (!affectsAnimals) continue;

            int d = ts.GetTileDistance(fromTile, t.tileIndex);
            if (d < 0) continue;
            if (d > maxRange) continue;
            if (d < bestDist) { bestDist = d; bestTile = t.tileIndex; }
        }

        return bestTile >= 0 ? bestTile : (int?)null;
    }

    /// <summary>
    /// Apply an improvement upgrade to a tile (visual + persist). Caller must have already consumed requirements (ConsumeRequirements).
    /// Used by AI to upgrade improvements. Returns true if applied.
    /// </summary>
    public bool ApplyUpgradeToTile(int tileIndex, int planetIndex, ImprovementUpgradeData upgrade)
    {
        if (upgrade == null) return false;
        planetIndex = ResolvePlanetIndex(planetIndex);
        var td = GetTileDataAcrossAllPlanets(tileIndex, planetIndex);
        if (td == null || td.improvement == null || td.improvementInstanceObject == null) return false;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        GameObject instanceObj = td.improvementInstanceObject;
        var impInstance = instanceObj.GetComponent<ImprovementInstance>();
        if (impInstance == null) impInstance = instanceObj.AddComponent<ImprovementInstance>();
        string upgradeKey = !string.IsNullOrEmpty(upgrade.upgradeId) ? upgrade.upgradeId : upgrade.upgradeName;
        if (impInstance.HasApplied(upgradeKey)) return false;
        if (td.builtUpgrades != null && td.builtUpgrades.Contains(upgradeKey)) return false;

        if (upgrade.makesVisualChange)
        {
            if (upgrade.replacePrefab != null)
            {
                Vector3 pos = instanceObj.transform.position;
                Quaternion rot = instanceObj.transform.rotation;
                var newObj = Instantiate(upgrade.replacePrefab, pos, rot);
                try { var mgr = FindObjectsByType<HexMapChunkManager>(FindObjectsSortMode.None).FirstOrDefault(m => m.PlanetGenerator == planetGenerator); if (mgr != null) mgr.RegisterObjectForWrapAtTile(tileIndex, newObj); } catch { }
                var planetGen = GameManager.Instance != null ? GameManager.Instance.GetPlanetGenerator(planetIndex) : null; // no civ context here
                if (planetGen != null) newObj.transform.SetParent(planetGen.transform, true);
                var newInst = newObj.GetComponent<ImprovementInstance>() ?? newObj.AddComponent<ImprovementInstance>();
                newInst.tileIndex = tileIndex;
                newInst.data = impInstance.data;
                newInst.appliedUpgrades = new System.Collections.Generic.HashSet<string>(impInstance.appliedUpgrades);
                newInst.owner = impInstance.owner;
                newInst.Initialize(tileIndex, td.improvement, planetIndex);
                if (impInstance.attachedParts != null && impInstance.attachedParts.Count > 0)
                {
                    newInst.attachedParts = new System.Collections.Generic.List<GameObject>();
                    foreach (var child in impInstance.attachedParts)
                    {
                        if (child == null) continue;
                        child.transform.SetParent(newObj.transform, true);
                        newInst.attachedParts.Add(child);
                    }
                }
                td.improvementInstanceObject = newObj;
                if (planetIndex >= 0 && ts != null) ts.SetTileDataOnPlanet(tileIndex, td, planetIndex);
                else if (ts != null) ts.SetTileData(tileIndex, td);
                Destroy(instanceObj);
                instanceObj = newObj;
                impInstance = newInst;
            }
            else if (upgrade.attachPrefabs != null && upgrade.attachPrefabs.Length > 0)
            {
                for (int i = 0; i < upgrade.attachPrefabs.Length; i++)
                {
                    var prefab = upgrade.attachPrefabs[i];
                    if (prefab == null) continue;
                    bool already = impInstance.attachedParts != null && impInstance.attachedParts.Any(c => c != null && c.name.Contains(prefab.name));
                    if (already) continue;
                    var go = Instantiate(prefab, instanceObj.transform);
                    Vector3 localPos = Vector3.zero;
                    Quaternion localRot = Quaternion.identity;
                    if (upgrade.attachLocalPositions != null && i < upgrade.attachLocalPositions.Length) localPos = upgrade.attachLocalPositions[i];
                    if (upgrade.attachLocalEulerAngles != null && i < upgrade.attachLocalEulerAngles.Length) localRot = Quaternion.Euler(upgrade.attachLocalEulerAngles[i]);
                    go.transform.localPosition = localPos;
                    go.transform.localRotation = localRot;
                    if (impInstance.attachedParts == null) impInstance.attachedParts = new System.Collections.Generic.List<GameObject>();
                    impInstance.attachedParts.Add(go);
                }
            }
            impInstance.MarkApplied(upgradeKey);
        }
        else
        {
            impInstance.MarkApplied(upgradeKey);
        }

        if (td.builtUpgrades == null) td.builtUpgrades = new System.Collections.Generic.List<string>();
        if (!td.builtUpgrades.Contains(upgradeKey)) td.builtUpgrades.Add(upgradeKey);
        td.RecomputeImprovementDefenseAggregates();
        if (planetIndex >= 0 && ts != null) ts.SetTileDataOnPlanet(tileIndex, td, planetIndex);
        else if (ts != null) ts.SetTileData(tileIndex, td);
        return true;
    }

    /// <summary>
    /// Rehydrate all saved upgrades on every tile of the given planet.
    /// Uses planet-aware tile lookup so multi-planet games rehydrate correctly.
    /// </summary>
    public void RehydrateAllUpgradesOnPlanet(int planetIndex)
    {
        if (GameManager.Instance == null) return;
        var planetGen = GameManager.Instance.GetPlanetGenerator(planetIndex);
        if (planetGen == null) return;

        int count = planetGen.Grid?.TileCount ?? 0;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        for (int i = 0; i < count; i++)
        {
            var tileData = ts != null ? ts.GetTileDataFromPlanet(i, planetIndex) : null;
            if (tileData == null) continue;
            if (tileData.improvement == null) continue;
            // Attempt to rehydrate this tile (no-op if runtime instance not present)
            RehydrateTileUpgrades(i, planetIndex);
        }
    }
}
