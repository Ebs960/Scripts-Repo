using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class AncientRuinsManager : MonoBehaviour
{
    // Civ 5 style Ruin types
    public enum RuinType { Technology, Gold, Unit, Map, Culture, Faith, Population, Upgrade, Temple }

    [System.Serializable]
    public class RuinSite
    {
        public int planetIndex;
        public Vector3 position;
        public RuinType ruinType;
        public bool isDiscovered;
        public bool isExplored;
        public Civilization discoveredBy;
        public string ruinName;
        public string description;
        // ScriptableObject defining this ruin's rewards; null for legacy hand-placed ruins.
        public RuinData ruinData;
        // The instantiated world-space prefab for this ruin; destroyed when the ruin is explored.
        [System.NonSerialized] public GameObject visualObject;
    }

    // Events for UI and game system integration
    public System.Action<RuinSite, Civilization> OnRuinDiscovered;
    public System.Action<RuinSite, Civilization, List<string>> OnRuinExplorationCompleted;

    // List of all generated ruins
    public List<RuinSite> generatedRuins = new List<RuinSite>();

    public static AncientRuinsManager Instance { get; private set; }

    public int numberOfRuinsToSpawn = 10;
    [Tooltip("Pool of RuinData assets to select from when placing ruins on a newly generated planet. "
           + "Each asset's Spawn Weight determines its relative frequency.")]
    public RuinData[] ruinDataPool;

    private List<AncientRuin> ruins = new List<AncientRuin>();
    private PlanetGenerator planetGenerator;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // Subscribe here (not Start) so we don't miss events fired before the first frame.
        GameManager.OnPlanetFullyGenerated += HandlePlanetFullyGeneratedForRuins;
    }

    void OnDestroy()
    {
        GameManager.OnPlanetFullyGenerated -= HandlePlanetFullyGeneratedForRuins;
    }

    private void HandlePlanetFullyGeneratedForRuins(PlanetGenerator generator)
    {
        if (generator == null) return;
        SpawnRuins(generator);
    }

    public void SpawnRuins(PlanetGenerator generator)
    {
        planetGenerator = generator;
        if (planetGenerator == null) return;

        int pIdx = generator.planetIndex;

        // Prevent double-spawning if this planet already has ruins registered.
        if (generatedRuins.Exists(r => r.planetIndex == pIdx)) return;

        if (ruinDataPool == null || ruinDataPool.Length == 0)
        {
            Debug.LogWarning("[AncientRuinsManager] ruinDataPool is empty — no ruins will be spawned. "
                           + "Assign RuinData assets in the Inspector.");
            return;
        }

        var ts = TileSystem.GetForPlanet(pIdx) ?? TileSystem.Instance;
        var chunkMgr = FindObjectsByType<HexMapChunkManager>(FindObjectsSortMode.None)
                           .FirstOrDefault(m => m.PlanetGenerator == generator);

        int spawnedCount = 0; // number of RuinSite records created
        int visualSpawned = 0; // number of visual GameObjects instantiated
        PlanetType pType = generator.planetType;

        for (int i = 0; i < numberOfRuinsToSpawn; i++)
        {
            // Pick ruin data first so we know if water spawning is allowed
            RuinData data = GetWeightedRandomRuinData(pType);
            if (data == null) continue;

            // Find a valid tile (up to 20 attempts).
            int tileIndex = -1;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                int candidate = Random.Range(0, generator.Grid.TileCount);
                HexTileData td = generator.GetHexTileData(candidate);
                // Always skip deep ocean / seas biomes
                if (td.biome == Biome.Ocean || td.biome == Biome.Seas) continue;
                // Skip water tiles (rivers/lakes) unless this ruin explicitly allows it
                if (td.waterType != TileWaterType.None && !data.canSpawnInWater) continue;
                tileIndex = candidate;
                break;
            }
            if (tileIndex < 0) continue;
            if (data == null) continue;

            // Use the TileSystem's surface position which accounts for terrain displacement.
            // Add a small vertical offset to avoid Z-fighting with the terrain shader displacement.
            Vector3 position = Vector3.zero;
            if (ts != null && ts.IsReady())
            {
                position = ts.GetTileSurfacePosition(tileIndex, 0.03f);
            }
            else
            {
                position = generator.transform.position;
            }

            // Register the RuinSite data record (used by discovery & reward logic).
            var site = new RuinSite
            {
                planetIndex = pIdx,
                position    = position,
                ruinType    = data.ruinType,
                ruinName    = data.ruinName,
                description = data.description,
                ruinData    = data
            };
            generatedRuins.Add(site);
            spawnedCount++;

            // Spawn the visual prefab. Strictly require per-type prefab on the RuinData.
            if (data.ruinPrefab == null)
            {
                Debug.LogWarning($"[AncientRuinsManager] RuinData '{data.ruinName}' has no 'ruinPrefab' assigned. Skipping visual spawn for this ruin.");
            }
            else
            {
                GameObject ruinGO = Instantiate(data.ruinPrefab, position, Quaternion.identity, transform);
                site.visualObject = ruinGO;
                visualSpawned++;

                // Ensure the AncientRuin component is present and carries the RuinData.
                var ruinComp = ruinGO.GetComponent<AncientRuin>() ?? ruinGO.AddComponent<AncientRuin>();
                ruinComp.ruinData = data;
                ruins.Add(ruinComp);

                // Register with HexMapChunkManager so the prefab follows wrap teleport.
                try
                {
                    if (chunkMgr != null && generator.Grid != null)
                    {
                        int tile = generator.Grid.GetTileAtPosition(position);
                        if (tile >= 0) chunkMgr.RegisterObjectForWrapAtTile(tile, ruinGO);
                    }
                }
                catch { }
            }
        }

        if (spawnedCount == 0 && (ruinDataPool == null || ruinDataPool.Length == 0))
        {
            Debug.LogError($"[AncientRuinsManager] No ruins spawned on planet {pIdx}: ruinDataPool is empty or no valid land tiles found.");
        }
        else if (spawnedCount == 0)
        {
            Debug.LogWarning($"[AncientRuinsManager] No RuinSite records were created on planet {pIdx}. Check generation constraints and tile availability.");
        }

        if (visualSpawned == 0)
        {
            Debug.LogWarning($"[AncientRuinsManager] No ruin visuals were instantiated on planet {pIdx}. Ensure each RuinData in the pool has a 'ruinPrefab' assigned. RuinData count={ruinDataPool?.Length ?? 0}.");
        }

        Debug.Log($"[AncientRuinsManager] Spawned {spawnedCount} ruins on planet {pIdx} (visuals: {visualSpawned}).");
    }

    /// <summary>
    /// Picks a RuinData from ruinDataPool using weighted random selection,
    /// filtered by the given planet type.
    /// </summary>
    private RuinData GetWeightedRandomRuinData(PlanetType planetType)
    {
        if (ruinDataPool == null || ruinDataPool.Length == 0) return null;

        float totalWeight = 0f;
        foreach (var d in ruinDataPool)
        {
            if (d == null) continue;
            // Skip ruin types not allowed on this planet
            if (d.allowedPlanetTypes != null && d.allowedPlanetTypes.Length > 0
                && !System.Array.Exists(d.allowedPlanetTypes, pt => pt == planetType))
                continue;
            totalWeight += d.spawnWeight;
        }

        if (totalWeight <= 0f) return ruinDataPool.FirstOrDefault(d => d != null);

        float roll = Random.Range(0f, totalWeight);
        float cumulative = 0f;
        foreach (var d in ruinDataPool)
        {
            if (d == null) continue;
            if (d.allowedPlanetTypes != null && d.allowedPlanetTypes.Length > 0
                && !System.Array.Exists(d.allowedPlanetTypes, pt => pt == planetType))
                continue;
            cumulative += d.spawnWeight;
            if (roll <= cumulative) return d;
        }
        return ruinDataPool.LastOrDefault(d => d != null);
    }

    /// <summary>
    /// Returns true if this civilization is eligible to discover and explore ruins.
    /// Tribes, city-states, animal civs, and demon civs cannot benefit from ruins.
    /// </summary>
    private static bool CivCanExploreRuins(Civilization civ)
    {
        if (civ == null || civ.civData == null) return false;
        if (civ.civData.isTribe || civ.civData.isCityState) return false;

        // Block demon civs — identified by all their combat units being DemonUnitData.
        if (civ.combatUnits != null && civ.combatUnits.Count > 0)
        {
            bool allDemons = true;
            foreach (var u in civ.combatUnits)
            {
                if (u == null || u.data == null) continue;
                if (!(u.data is DemonUnitData)) { allDemons = false; break; }
            }
            if (allDemons) return false;
        }

        // Block animal civs — identified by all their combat units being Category.Animal.
        if (civ.combatUnits != null && civ.combatUnits.Count > 0)
        {
            bool allAnimals = true;
            foreach (var u in civ.combatUnits)
            {
                if (u == null || u.data == null) continue;
                if (u.data.unitType != CombatCategory.Animal) { allAnimals = false; break; }
            }
            if (allAnimals) return false;
        }

        return true;
    }

    // Civ 5 style methods for discovering and exploring ruins
    public void CheckForRuinDiscovery(int planetIndex, Vector3 unitPosition, Civilization civilization)
    {
        if (!CivCanExploreRuins(civilization)) return;

        foreach (var ruin in generatedRuins)
        {
            if (ruin.planetIndex != planetIndex || ruin.isDiscovered)
                continue;

            // Prefer tile-step (hex) distance for discovery
            var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
            int unitTile = -1;
            int ruinTile = -1;
            bool usedTileCheck = false;
            if (ts != null && ts.IsReady())
            {
                PlanetGenerator pg = null;
                if (civilization != null)
                {
                    try { pg = civilization.GetPlanetGeneratorForIndex(planetIndex); } catch { pg = null; }
                    if (pg == null)
                        Debug.LogWarning($"[AncientRuinsManager] Civilization '{civilization.civData?.civName ?? civilization.name}' returned null for planet {planetIndex}; falling back to GameManager.");
                }
                if (pg == null)
                {
                    pg = GameManager.Instance != null ? GameManager.Instance.GetPlanetGenerator(planetIndex) : null;
                    if (pg == null)
                        Debug.LogWarning($"[AncientRuinsManager] GameManager has no PlanetGenerator for index {planetIndex} when checking ruins.");
                }
                if (pg != null && pg.Grid != null)
                {
                    unitTile = pg.Grid.GetTileAtPosition(unitPosition);
                    ruinTile = pg.Grid.GetTileAtPosition(ruin.position);
                    if (unitTile >= 0 && ruinTile >= 0)
                    {
                        int steps = ts.GetWrappedHexDistance(unitTile, ruinTile);
                        if (steps <= 2)
                        {
                            DiscoverRuin(ruin, civilization);
                        }
                        usedTileCheck = true;
                    }
                }
            }

            // If we couldn't map to tiles, fall back to world-space distance
            if (!usedTileCheck)
            {
                float distance = Vector3.Distance(unitPosition, ruin.position);
                if (distance <= 2.0f) // Discovery range (world-space fallback)
                {
                    DiscoverRuin(ruin, civilization);
                }
            }
        }
    }

    public void DiscoverRuin(RuinSite ruin, Civilization civ)
    {
        if (ruin.isDiscovered) return;
        ruin.isDiscovered = true;
        ruin.discoveredBy = civ;
        OnRuinDiscovered?.Invoke(ruin, civ);
        ExploreRuin(ruin, civ); // Instant exploration like Civ 5

        // Tell the prefab component to destroy itself.
        if (ruin.visualObject != null)
        {
            var ruinComp = ruin.visualObject.GetComponent<AncientRuin>();
            if (ruinComp != null)
                ruinComp.OnExplored();
            else
                Destroy(ruin.visualObject); // fallback if component was removed
            ruin.visualObject = null;
        }
    }

    public void ExploreRuin(RuinSite ruin, Civilization civ)
    {
        if (ruin.isExplored) return;
        ruin.isExplored = true;
        var rewards = new List<string>();

        var data = ruin.ruinData;
        if (data != null)
        {
            // Gold
            if (data.grantsGold)
            {
                int gold = UnityEngine.Random.Range(data.goldMin, data.goldMax + 1);
                civ.gold += gold;
                rewards.Add($"Found {gold} gold!");
            }

            // Culture
            if (data.grantsCulture)
            {
                int culture = UnityEngine.Random.Range(data.cultureMin, data.cultureMax + 1);
                if (civ.currentCulture != null)
                {
                    civ.currentCultureProgress += culture;
                    rewards.Add($"Gained {culture} culture toward current adoption!");
                }
                else
                {
                    civ.culture += culture;
                    rewards.Add($"Gained {culture} culture from ancient writings!");
                }
            }

            // Faith
            if (data.grantsFaith)
            {
                int faith = UnityEngine.Random.Range(data.faithMin, data.faithMax + 1);
                civ.faith += faith;
                rewards.Add($"Gained {faith} faith from sacred relics!");
            }

            // Map reveal (stub — hook into fog-of-war reveal when available)
            if (data.revealsMap)
            {
                rewards.Add("Revealed part of the map!");
            }

            // Population (stub)
            if (data.grantsPopulation)
            {
                rewards.Add("Population increased in your nearest city!");
            }

            // Guaranteed technologies
            if (data.guaranteedTechs != null)
            {
                foreach (var tech in data.guaranteedTechs)
                {
                    if (tech == null) continue;
                    if (civ.researchedTechs != null && !civ.researchedTechs.Contains(tech))
                    {
                        civ.researchedTechs.Add(tech);
                        rewards.Add($"Discovered the technology: {tech.techName}!");
                    }
                }
            }

            if (rewards.Count == 0)
                rewards.Add("Ancient ruins explored. The secrets of a lost civilization are yours.");
        }
        else
        {
            // Fallback for RuinSites without a RuinData asset (e.g. hand-placed legacy ruins).
            switch (ruin.ruinType)
            {
                case RuinType.Technology:
                    rewards.Add("Discovered a lost technology!");
                    break;
                case RuinType.Gold:
                    int gold = UnityEngine.Random.Range(50, 201);
                    civ.gold += gold;
                    rewards.Add($"Found {gold} gold!");
                    break;
                case RuinType.Unit:
                    rewards.Add("A friendly unit joins your cause!");
                    break;
                case RuinType.Map:
                    rewards.Add("Revealed part of the map!");
                    break;
                case RuinType.Culture:
                    int culture = UnityEngine.Random.Range(20, 100);
                    if (civ.currentCulture != null)
                    {
                        civ.currentCultureProgress += culture;
                        rewards.Add($"Applied {culture} culture directly to current culture adoption!");
                    }
                    else
                    {
                        civ.culture += culture;
                        rewards.Add($"Gained {culture} culture from ancient writings!");
                    }
                    break;
                case RuinType.Faith:
                    int faith = UnityEngine.Random.Range(15, 75);
                    civ.faith += faith;
                    rewards.Add($"Gained {faith} faith from sacred relics!");
                    break;
                case RuinType.Population:
                    rewards.Add("Population increased in your nearest city!");
                    break;
                case RuinType.Upgrade:
                    rewards.Add("A unit has been upgraded!");
                    break;
            }
        }

        OnRuinExplorationCompleted?.Invoke(ruin, civ, rewards);
    }

    public bool StartRuinExploration(RuinSite ruin, Civilization civilization)
    {
        if (!ruin.isDiscovered || ruin.isExplored) return false;
        ExploreRuin(ruin, civilization);
        return true;
    }

    public List<RuinSite> GetDiscoveredRuins(Civilization civ)
    {
        var discovered = new List<RuinSite>();
        foreach (var ruin in generatedRuins)
            if (ruin.isDiscovered && ruin.discoveredBy == civ)
                discovered.Add(ruin);
        return discovered;
    }

    public List<RuinSite> GetRuinsOnPlanet(int planetIndex)
    {
        var ruinsOnPlanet = new List<RuinSite>();
        foreach (var ruin in generatedRuins)
            if (ruin.planetIndex == planetIndex)
                ruinsOnPlanet.Add(ruin);
        return ruinsOnPlanet;
    }
}
