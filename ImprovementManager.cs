// Assets/Scripts/Managers/ImprovementManager.cs
using System.Collections.Generic;
using UnityEngine;

public class ImprovementManager : MonoBehaviour
{
    public static ImprovementManager Instance { get; private set; }

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

    // All active build jobs on the map
    private readonly List<BuildJob> jobs = new();
    // Parallel pipeline for worker-built combat units
    private readonly List<UnitJob> unitJobs = new();
    // Parallel pipeline for worker-built worker units
    private readonly List<WorkerJob> workerJobs = new();
    
    // Planet generator reference
    private PlanetGenerator planetGenerator;

    // Active traps by tile index
    private readonly Dictionary<int, TrapRuntime> traps = new Dictionary<int, TrapRuntime>();

    [System.Serializable]
    public class JobAssignmentSaveData
    {
        public int tileIndex;
        public List<string> assignedWorkerPersistentIds = new List<string>();
    }

    private struct TrapRuntime
    {
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
    private HexTileData GetTileDataAcrossAllPlanets(int tileIndex)
    {
        if (TileDataHelper.Instance == null) return null;
        
        // First try current planet (most common case)
        var (tileData, _) = TileDataHelper.Instance.GetTileData(tileIndex);
        if (tileData != null) return tileData;
        
        // If not found, check all planets
        if (GameManager.Instance?.enableMultiPlanetSystem == true)
        {
            var planetData = GameManager.Instance.GetPlanetData();
            foreach (var kvp in planetData)
            {
                var (planetTileData, _) = TileDataHelper.Instance.GetTileDataFromPlanet(tileIndex, kvp.Key);
                if (planetTileData != null) return planetTileData;
            }
        }
        
        return null;
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }
    
    void Start()
    {
        InitializeReferences();
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
    public bool CreateBuildJob(ImprovementData data, int tileIndex, Civilization owner)
    {
        // No duplicate jobs on same tile
        if (jobs.Exists(j => j.tileIndex == tileIndex)) return false;

        // Check tile requirements across all planets
        var td = GetTileDataAcrossAllPlanets(tileIndex);
        if (td == null) return false;
        
        // Basic terrain checks
        if (!td.isLand) return false;
        if (data.allowedBiomes.Length > 0 && 
            System.Array.IndexOf(data.allowedBiomes, td.biome) < 0) return false;
        
        // Territory control checks
        bool isOwnedByBuilder = td.owner == owner;
        bool isNeutral = td.owner == null;
        bool isEnemyTerritory = td.owner != null && td.owner != owner;
        
        // Check city requirement
        if (data.needsCity && !td.HasCity) return false;
        
        // Check territory control requirements
        if (data.requiresControlledTerritory && !isOwnedByBuilder) return false;
        if (isNeutral && !data.canBuildInNeutralTerritory) return false;
        if (isEnemyTerritory && !data.canBuildInEnemyTerritory) return false;

        var job = new BuildJob(tileIndex, owner, data);
        jobs.Add(job);
        return true;
    }

    /// <summary>
    /// Attempt to start a worker unit build job for a combat unit on tileIndex.
    /// Respects unit flags, requirements, limits, and tile occupancy.
    /// </summary>
    public bool CreateUnitJob(CombatUnitData unit, int tileIndex, Civilization owner)
    {
        if (unit == null || owner == null) return false;
        if (!unit.buildableByWorker) return false;
        if (!unit.AreRequirementsMet(owner)) return false;
        if (!LimitManager.Instance.CanCreateCombatUnit(owner, unit)) return false;

        // No duplicate jobs per tile
        if (unitJobs.Exists(j => j.tileIndex == tileIndex)) return false;

        // Tile must be valid and free
    var (tileData, _) = TileDataHelper.Instance.GetTileData(tileIndex);
    if (tileData == null) return false;
    // Allow job even if a worker is occupying the tile; we'll spawn the unit on a free neighbor if needed
    if (!tileData.isLand) return false; // basic restriction for now

        // Optional: validate adjacent friendly city or territory rules if desired later

        unitJobs.Add(new UnitJob(tileIndex, owner, unit));
        return true;
    }

    /// <summary>
    /// Attempt to start a worker build job for a worker unit on tileIndex.
    /// </summary>
    public bool CreateWorkerJob(WorkerUnitData unit, int tileIndex, Civilization owner)
    {
        if (unit == null || owner == null) return false;
        if (!unit.buildableByWorker) return false;
        if (!unit.AreRequirementsMet(owner)) return false;
        if (!LimitManager.Instance.CanCreateWorkerUnit(owner, unit)) return false;

        if (workerJobs.Exists(j => j.tileIndex == tileIndex)) return false;

        var (tileData, _) = TileDataHelper.Instance.GetTileData(tileIndex);
        if (tileData == null) return false;
        if (!tileData.isLand) return false;

        workerJobs.Add(new WorkerJob(tileIndex, owner, unit));
        return true;
    }

    /// <summary>
    /// Assign a worker to an existing build job on tileIndex. Returns true when assigned.
    /// Worker identity is tracked by GameObject InstanceID.
    /// </summary>
    public bool AssignWorkerToJob(int tileIndex, WorkerUnit worker)
    {
    if (worker == null) return false;
    var job = jobs.Find(j => j.tileIndex == tileIndex);
    if (job == null) return false;
    string pid = worker.PersistentId;
    if (job.assignedWorkerPersistentIds == null) job.assignedWorkerPersistentIds = new List<string>();
    if (!job.assignedWorkerPersistentIds.Contains(pid)) job.assignedWorkerPersistentIds.Add(pid);
    return true;
    }

    /// <summary>
    /// Unassign a worker from a specific job.
    /// </summary>
    public void UnassignWorkerFromJob(int tileIndex, WorkerUnit worker)
    {
    if (worker == null) return;
    var job = jobs.Find(j => j.tileIndex == tileIndex);
    if (job == null) return;
    string pid = worker.PersistentId;
    job.assignedWorkerPersistentIds?.RemoveAll(x => x == pid);
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
    public bool JobAssignedToWorker(int tileIndex, WorkerUnit worker)
    {
        if (worker == null) return false;
        var job = jobs.Find(j => j.tileIndex == tileIndex);
        if (job == null) return false;
        string pid = worker.PersistentId;
        return job.assignedWorkerPersistentIds != null && job.assignedWorkerPersistentIds.Contains(pid);
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
    public void AddWork(int tileIndex, int workPoints)
    {
        var job = jobs.Find(j => j.tileIndex == tileIndex);
        if (job == null) return;

        job.remainingWork -= workPoints;
        job.Clamp();

        if (job.remainingWork <= 0)
            CompleteJob(job);
    }

    /// <summary>
    /// Apply work points to a unit job on this tile.
    /// </summary>
    public void AddUnitWork(int tileIndex, int workPoints)
    {
        var job = unitJobs.Find(j => j.tileIndex == tileIndex);
        if (job == null) return;

        job.remainingWork -= workPoints;
        job.Clamp();

        if (job.remainingWork <= 0)
            CompleteUnitJob(job);
    }

    /// <summary>
    /// Apply work points to a worker unit job on this tile.
    /// </summary>
    public void AddWorkerWork(int tileIndex, int workPoints)
    {
        var job = workerJobs.Find(j => j.tileIndex == tileIndex);
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
        // If you want civ-wide auto build, you can iterate jobs owned by civ
        // and automatically deduct workPoints from idle workers here.
    }

    private void CompleteJob(BuildJob job)
    {
        Vector3 pos = TileDataHelper.Instance.GetTileCenter(job.tileIndex);

        if (job.data.completePrefab != null)
        {
            GameObject completedImprovement = Instantiate(job.data.completePrefab, pos, Quaternion.identity);

            // Attach ImprovementInstance component to track applied upgrades and attached parts
            var instance = completedImprovement.GetComponent<ImprovementInstance>();
            if (instance == null) instance = completedImprovement.AddComponent<ImprovementInstance>();
            instance.tileIndex = job.tileIndex;
            instance.data = job.data;

            // Ensure the click handler exists and is initialized so the UI can open
            var clickHandler = completedImprovement.GetComponent<ImprovementClickHandler>();
            if (clickHandler == null) clickHandler = completedImprovement.AddComponent<ImprovementClickHandler>();
            clickHandler.Initialize(job.tileIndex, job.data);

            // Add collider if needed for clicking
            if (completedImprovement.GetComponent<Collider>() == null)
            {
                var collider = completedImprovement.AddComponent<BoxCollider>();
                // Adjust collider size as needed
                collider.size = Vector3.one * 2f;
            }

            // Store runtime reference on the tile data for later upgrade application
            var (tileData, _) = TileDataHelper.Instance.GetTileData(job.tileIndex);
            if (tileData != null)
            {
                tileData.improvement = job.data;
                tileData.improvementInstanceObject = completedImprovement;
                TileDataHelper.Instance.SetTileData(job.tileIndex, tileData);
            }
        }


        // If the completed improvement is a road, bump the network version to invalidate caches
        if (job.data != null && job.data.isRoad)
        {
            roadNetworkVersion++;
        }

        // Register trap runtime state if this improvement is a trap
        if (job.data.isTrap)
        {
            traps[job.tileIndex] = new TrapRuntime
            {
                tileIndex = job.tileIndex,
                owner = job.owner,
                data = job.data,
                usesLeft = Mathf.Max(1, job.data.trapMaxTriggers),
                armed = true
            };
        }

        jobs.Remove(job);
    }

    private void CompleteUnitJob(UnitJob job)
    {
        // Spawn the unit and register occupancy
        var unitPrefab = job.data.prefab;
        if (unitPrefab == null)
        {
            Debug.LogError($"Unit {job.data?.unitName} has no prefab; cannot spawn.");
            unitJobs.Remove(job);
            return;
        }

    // Find a valid spawn tile (prefer job tile if unoccupied)
    int spawnIndex = FindSpawnTile(job.tileIndex);
    Vector3 pos = TileDataHelper.Instance.GetTileCenter(spawnIndex);
    var go = Object.Instantiate(unitPrefab, pos, Quaternion.identity);
        var unit = go.GetComponent<CombatUnit>();
        if (unit == null)
        {
            Debug.LogError("Spawned unit prefab missing CombatUnit component.");
            Object.Destroy(go);
            unitJobs.Remove(job);
            return;
        }

        unit.Initialize(job.data, job.owner);
        unit.InitializeAndReturn(job.data, job.owner, spawnIndex);
        job.owner.combatUnits.Add(unit);
        LimitManager.Instance.AddCombatUnit(job.owner, job.data);
        TileDataHelper.Instance.SetTileOccupant(spawnIndex, unit.gameObject);

        unitJobs.Remove(job);
    }

    private void CompleteWorkerJob(WorkerJob job)
    {
        // Spawn the worker unit and register occupancy
        var prefab = job.data.prefab;
        if (prefab == null)
        {
            Debug.LogError($"Worker unit {job.data?.unitName} has no prefab; cannot spawn.");
            workerJobs.Remove(job);
            return;
        }

        int spawnIndex = FindSpawnTile(job.tileIndex);
        Vector3 pos = TileDataHelper.Instance.GetTileCenter(spawnIndex);
        var go = Object.Instantiate(prefab, pos, Quaternion.identity);
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
        TileDataHelper.Instance.SetTileOccupant(spawnIndex, unit.gameObject);

        workerJobs.Remove(job);
    }

    private int FindSpawnTile(int centerIndex)
    {
        // If center tile is free, use it
        var (tileData, _) = TileDataHelper.Instance.GetTileData(centerIndex);
        if (tileData != null && tileData.occupantId == 0)
            return centerIndex;

        // Otherwise try neighbors
        foreach (int n in TileDataHelper.Instance.GetTileNeighbors(centerIndex))
        {
            var (td, _) = TileDataHelper.Instance.GetTileData(n);
            if (td != null && td.isLand && td.occupantId == 0)
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
        public Civilization owner;
        public ImprovementData data;
        public int remainingWork;
    // Track assigned workers by GameObject InstanceID so workers can auto-contribute each turn
    public List<int> assignedWorkerInstanceIds = new List<int>();
    // Persistent worker identifiers (GUIDs) to survive save/load
    public List<string> assignedWorkerPersistentIds = new List<string>();

        public BuildJob(int tileIndex, Civilization owner, ImprovementData data)
        {
            this.tileIndex = tileIndex;
            this.owner = owner;
            this.data = data;
            this.remainingWork = data.workCost;
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
        public Civilization owner;
        public CombatUnitData data;
        public int remainingWork;

        public UnitJob(int tileIndex, Civilization owner, CombatUnitData data)
        {
            this.tileIndex = tileIndex;
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
        public Civilization owner;
        public WorkerUnitData data;
        public int remainingWork;

        public WorkerJob(int tileIndex, Civilization owner, WorkerUnitData data)
        {
            this.tileIndex = tileIndex;
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
        if (!traps.TryGetValue(tileIndex, out var trap)) return;
        if (!trap.armed || trap.usesLeft <= 0) return;

        // Validate improvement still exists and is a trap
        var (tileData, _) = TileDataHelper.Instance.GetTileData(tileIndex);
        if (tileData?.improvement == null || !tileData.improvement.isTrap)
            return;

        // Friendly safe
        if (trap.data.trapFriendlySafe && unit.owner == trap.owner)
            return;

        // Category filter
    var cat = unit.data != null ? unit.data.unitType : CombatCategory.Spearman;
        bool affects = trap.data.trapAffectsAnimalsOnly
            ? (cat == CombatCategory.Animal)
            : (trap.data.trapAffectedCategories != null && System.Array.IndexOf(trap.data.trapAffectedCategories, cat) >= 0);
        if (!affects) return;

        // Apply trap effects
        int dmg = Mathf.Max(0, trap.data.trapDamage);
        if (dmg > 0) unit.ApplyDamage(dmg);
        if (trap.data.trapImmobilize && trap.data.trapImmobilizeTurns > 0)
        {
            unit.ApplyTrap(trap.data.trapImmobilizeTurns);
            // Prevent further movement this turn
            unit.DeductMovementPoints(unit.currentMovePoints);
        }

        // Decrement uses and update or remove
        trap.usesLeft--;
        traps[tileIndex] = trap;
        if (trap.usesLeft <= 0 && trap.data.trapConsumeOnDeplete)
        {
            RemoveImprovement(tileIndex);
        }
    }

    /// <summary>
    /// Notify the manager that a worker has entered a tile. Triggers trap if present and applicable.
    /// Workers are affected by traps unless the trap is animals-only or friendly-safe.
    /// </summary>
    public void NotifyUnitEnteredTile(int tileIndex, WorkerUnit worker)
    {
        if (worker == null) return;
        if (!traps.TryGetValue(tileIndex, out var trap)) return;
        if (!trap.armed || trap.usesLeft <= 0) return;

        // Validate improvement still exists and is a trap
        var (tileData, _) = TileDataHelper.Instance.GetTileData(tileIndex);
        if (tileData?.improvement == null || !tileData.improvement.isTrap)
            return;

        // Friendly safe
        if (trap.data.trapFriendlySafe && worker.owner == trap.owner)
            return;

        // If trap is animals-only, skip workers
        if (trap.data.trapAffectsAnimalsOnly)
            return;

        // Apply trap effects
        int dmg = Mathf.Max(0, trap.data.trapDamage);
        if (dmg > 0) worker.ApplyDamage(dmg);
        if (trap.data.trapImmobilize && trap.data.trapImmobilizeTurns > 0)
        {
            worker.ApplyTrap(trap.data.trapImmobilizeTurns);
            // Prevent further movement this turn
            worker.DeductMovementPoints(worker.currentMovePoints);
        }

        // Decrement uses and update or remove
        trap.usesLeft--;
        traps[tileIndex] = trap;
        if (trap.usesLeft <= 0 && trap.data.trapConsumeOnDeplete)
        {
            RemoveImprovement(tileIndex);
        }
    }

    /// <summary>
    /// Remove any improvement from a tile, including trap state.
    /// </summary>
    public void RemoveImprovement(int tileIndex)
    {
        var (tileData, _) = TileDataHelper.Instance.GetTileData(tileIndex);
        if (tileData == null) return;
        var data = tileData.improvement;
        if (data == null) return;

        // Optional destroyed prefab
        if (data.destroyedPrefab != null)
        {
            Instantiate(data.destroyedPrefab, TileDataHelper.Instance.GetTileCenter(tileIndex), Quaternion.identity);
        }

        tileData.improvement = null;
        TileDataHelper.Instance.SetTileData(tileIndex, tileData);

        traps.Remove(tileIndex);
    }

    /// <summary>
    /// Re-apply saved built upgrades to the runtime instantiated improvement on a tile.
    /// Call this after loading the map to rehydrate visual attachments for modular upgrades.
    /// </summary>
    public void RehydrateTileUpgrades(int tileIndex)
    {
        var (tileData, _) = TileDataHelper.Instance.GetTileData(tileIndex);
        if (tileData == null) return;
        if (tileData.improvement == null || tileData.improvementInstanceObject == null) return;

        var instanceObj = tileData.improvementInstanceObject;
        var impInstance = instanceObj.GetComponent<ImprovementInstance>();
        if (impInstance == null) impInstance = instanceObj.AddComponent<ImprovementInstance>();
        impInstance.tileIndex = tileIndex;
        impInstance.data = tileData.improvement;

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
                    var newInst = newObj.GetComponent<ImprovementInstance>() ?? newObj.AddComponent<ImprovementInstance>();
                    newInst.tileIndex = tileIndex;
                    newInst.data = impInstance.data;
                    newInst.appliedUpgrades = new System.Collections.Generic.HashSet<string>(impInstance.appliedUpgrades);

                    var ch = newObj.GetComponent<ImprovementClickHandler>() ?? newObj.AddComponent<ImprovementClickHandler>();
                    ch.Initialize(tileIndex, tileData.improvement);

                    tileData.improvementInstanceObject = newObj;
                    TileDataHelper.Instance.SetTileData(tileIndex, tileData);

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
    TileDataHelper.Instance.SetTileData(tileIndex, tileData);
    }
}
