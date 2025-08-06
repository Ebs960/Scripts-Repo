// Assets/Scripts/Managers/ResourceManager.cs
using System.Collections.Generic;
using UnityEngine;

public class ResourceManager : MonoBehaviour
{
    public static ResourceManager Instance { get; private set; }

    [Header("All resource types")]
    public ResourceData[] resourceTypes;

    // all spawned nodes in the world
    private readonly List<ResourceInstance> spawnedResources = new List<ResourceInstance>();

    private PlanetGenerator planetGenerator;
    private SphericalHexGrid grid;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        // CRITICAL: Wait for TileDataHelper before proceeding
        if (TileDataHelper.Instance == null)
        {
            Debug.LogWarning("[ResourceManager] TileDataHelper not ready yet, will retry in Start");
            StartCoroutine(WaitForTileDataHelper());
            return;
        }
        
        InitializeResourceManager();
    }
    
    private System.Collections.IEnumerator WaitForTileDataHelper()
    {
        while (TileDataHelper.Instance == null)
        {
            Debug.Log("[ResourceManager] Waiting for TileDataHelper...");
            yield return new WaitForSeconds(0.1f);
        }
        
        Debug.Log("[ResourceManager] TileDataHelper found, initializing...");
        InitializeResourceManager();
    }
    
    private void InitializeResourceManager()
    {
        // Find references to key components
        planetGenerator = GameManager.Instance?.GetCurrentPlanetGenerator();
        if (planetGenerator != null)
            grid = planetGenerator.Grid;
        
        // Load resources from asset folder if needed
        if (resourceTypes == null || resourceTypes.Length == 0)
        {
            LoadResources();
        }
        
        // Start listening to events
        SpawnResources();
        if (TurnManager.Instance != null)
            TurnManager.Instance.OnTurnChanged += HandleTurnChanged;
    }

    void OnDestroy()
    {
        if (TurnManager.Instance != null)
            TurnManager.Instance.OnTurnChanged -= HandleTurnChanged;
    }

    /// <summary>
    /// Scatter resource nodes across the map based on each ResourceData's rules.
    /// </summary>
    private void SpawnResources()
    {
        // SAFETY: Don't spawn if already spawned (prevents memory leak)
        if (spawnedResources.Count > 0)
        {
            Debug.Log("[ResourceManager] Resources already spawned, skipping");
            return;
        }

        // SAFETY: Check all required references
        if (grid == null || planetGenerator == null)
        {
            Debug.LogWarning("[ResourceManager] Missing grid or planetGenerator, cannot spawn resources");
            return;
        }
        
        if (TileDataHelper.Instance == null)
        {
            Debug.LogError("[ResourceManager] TileDataHelper.Instance is null! Cannot spawn resources.");
            return;
        }

        Debug.Log("[ResourceManager] Starting resource spawn...");
        int tileCount = grid.TileCount;

        for (int idx = 0; idx < tileCount; idx++)
        {
            var (tileData, isMoonTile) = TileDataHelper.Instance.GetTileData(idx);
            if (tileData == null || isMoonTile) continue; // No resources on the moon for now

            foreach (var rd in resourceTypes)
            {
                // skip if biome not allowed
                bool ok = false;
                foreach (var b in rd.allowedBiomes)
                    if (b == tileData.biome) { ok = true; break; }
                if (!ok) continue;

                if (Random.value <= rd.spawnChance)
                {
                    Vector3 pos = TileDataHelper.Instance.GetTileCenter(idx);
                    var go = Instantiate(rd.prefab, pos, Quaternion.identity);
                    var inst = go.AddComponent<ResourceInstance>();
                    inst.data = rd;
                    inst.tileIndex = idx;
                    spawnedResources.Add(inst);

                    // Mirror into the tile data
                    tileData.resource = rd;
                    TileDataHelper.Instance.SetTileData(idx, tileData);
                }
            }
        }
    }

    // Load resources from Resources folder if not set in inspector
    private void LoadResources()
    {
        resourceTypes = Resources.LoadAll<ResourceData>("Data/Resources");
        if (resourceTypes == null || resourceTypes.Length == 0)
        {
            Debug.LogWarning("No resource types found in Resources/Data/Resources folder. Please assign them in the inspector.");
        }
    }

    /// <summary>
    /// At the start of each civ's turn, grant per-turn yields for resources within its territory.
    /// </summary>
    private void HandleTurnChanged(Civilization civ, int round)
    {
        // Only grant at the start of a civ's own turn
        // (TurnManager invokes OnTurnChanged before civ.BeginTurn)
        var inv = GetInventory(civ);
        foreach (var kv in inv)
        {
            var rd = kv.Key;
            int count = kv.Value;
            civ.food          += rd.foodPerTurn * count;
            civ.gold          += rd.goldPerTurn * count;
            civ.science       += rd.sciencePerTurn * count;
            civ.culture       += rd.culturePerTurn * count;
            civ.policyPoints  += rd.policyPointsPerTurn * count;
            civ.faith         += rd.faithPerTurn * count;
        }
    }

    /// <summary>
    /// Returns how many nodes of each resource a civ controls (by tile ownership).
    /// </summary>
    public Dictionary<ResourceData,int> GetInventory(Civilization civ)
    {
        var dict = new Dictionary<ResourceData,int>();
        foreach (var inst in spawnedResources)
        {
            if (civ.ownedTileIndices.Contains(inst.tileIndex))
            {
                if (!dict.ContainsKey(inst.data)) dict[inst.data] = 0;
                dict[inst.data]++;
            }
        }
        return dict;
    }

    /// <summary>
    /// Called by a worker's forage action. Grants one-off yields and removes the node.
    /// </summary>
    public void ForageResource(ResourceInstance inst, Civilization civ)
    {
        var rd = inst.data;
        civ.food         += rd.forageFood;
        civ.gold         += rd.forageGold;
        civ.science      += rd.forageScience;
        civ.culture      += rd.forageCulture;
        civ.policyPoints += rd.foragePolicyPoints;

        // Clear the tile's resource in the hex data
        var (tileData, isMoonTile) = TileDataHelper.Instance.GetTileData(inst.tileIndex);
        if (tileData != null) {
            tileData.resource = null;
            TileDataHelper.Instance.SetTileData(inst.tileIndex, tileData);
        }

        spawnedResources.Remove(inst);
        Destroy(inst.gameObject);
    }

    // Method to spawn a resource instance
   private void SpawnResourceInstance(ResourceData resource, int tileIndex)
    {
        if (resource == null) return;

        // Ensure we have a grid reference
        if (grid == null && planetGenerator != null)
            grid = planetGenerator.Grid;

        if (grid == null) return;

        // Get the position for the resource
        Vector3 position = TileDataHelper.Instance.GetTileCenter(tileIndex);

        // Instantiate the resource prefab at the tile position
        var go = Instantiate(resource.prefab, position, Quaternion.identity);
        var inst = go.AddComponent<ResourceInstance>();
        inst.data = resource;
        inst.tileIndex = tileIndex;
        spawnedResources.Add(inst);

        // Update the tile data to reflect the new resource
        var (tileData, _) = TileDataHelper.Instance.GetTileData(tileIndex);
        if (tileData != null)
        {
            tileData.resource = resource;
            TileDataHelper.Instance.SetTileData(tileIndex, tileData);
        }
    }
}
