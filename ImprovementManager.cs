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
            Instantiate(job.data.completePrefab, pos, Quaternion.identity);

        var (tileData, isMoonTile) = TileDataHelper.Instance.GetTileData(job.tileIndex);
        if (tileData != null)
        {
            tileData.improvement = job.data;
            TileDataHelper.Instance.SetTileData(job.tileIndex, tileData);
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
}
