// Assets/Scripts/Managers/ImprovementManager.cs
using System.Collections.Generic;
using UnityEngine;

public class ImprovementManager : MonoBehaviour
{
    public static ImprovementManager Instance { get; private set; }

    // All active build jobs on the map
    private readonly List<BuildJob> jobs = new();
    
    // Planet generator reference
    private PlanetGenerator planetGenerator;

    // Active traps by tile index
    private readonly Dictionary<int, TrapRuntime> traps = new Dictionary<int, TrapRuntime>();

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
            
            // TODO: Add click handler for upgrades when ImprovementClickHandler is available
            // var clickHandler = completedImprovement.GetComponent<ImprovementClickHandler>();
            // if (clickHandler == null)
            //     clickHandler = completedImprovement.AddComponent<ImprovementClickHandler>();
            // clickHandler.Initialize(job.tileIndex, job.data);
            
            // Add collider if needed for clicking
            if (completedImprovement.GetComponent<Collider>() == null)
            {
                var collider = completedImprovement.AddComponent<BoxCollider>();
                // Adjust collider size as needed
                collider.size = Vector3.one * 2f;
            }
        }

        var (tileData, isMoonTile) = TileDataHelper.Instance.GetTileData(job.tileIndex);
        if (tileData != null)
        {
            tileData.improvement = job.data;
            TileDataHelper.Instance.SetTileData(job.tileIndex, tileData);
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


    /// <summary>
    /// Represents a construction project on a tile.
    /// </summary>
    private class BuildJob
    {
        public int tileIndex;
        public Civilization owner;
        public ImprovementData data;
        public int remainingWork;

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
        var cat = unit.data != null ? unit.data.category : CombatCategory.Spearman;
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
}
