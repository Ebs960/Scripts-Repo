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
        if (ts == null || !ts.IsReady())
        {
            Debug.LogWarning($"[AncientRuinsManager] TileSystem not ready for planet {pIdx}; cannot spawn ruins.");
            return;
        }

        var chunkMgr = FindObjectsByType<HexMapChunkManager>()
                           .FirstOrDefault(m => m.PlanetGenerator == generator);

        int spawnedCount = 0; // number of RuinSite records created
        int visualSpawned = 0; // number of visual GameObjects instantiated
        PlanetType pType = generator.planetType;

        var eligibleRuins = GetEligibleRuinDataForPlanet(pType);
        if (eligibleRuins.Count == 0)
        {
            Debug.LogWarning($"[AncientRuinsManager] No eligible RuinData assets for planet {pIdx} ({pType}).");
            return;
        }

        for (int i = 0; i < numberOfRuinsToSpawn; i++)
        {
            // Pick a ruin definition first, then build a full candidate list exactly like AnimalManager.
            RuinData data = GetWeightedRandomRuinData(eligibleRuins);
            if (data == null) continue;

            var candidates = BuildCandidateTileList(ts, generator, pIdx, data);
            if (candidates.Count == 0)
            {
                continue;
            }

            int chosenIndex = Random.Range(0, candidates.Count);
            int tileIndex = candidates[chosenIndex];
            HexTileData chosenTile = ts.GetTileData(tileIndex);
            if (chosenTile == null) continue;

            // Match AnimalManager: use TileSystem surface-aware position and parent by land/water state.
            Vector3 position = ts.GetTileSurfacePosition(tileIndex, data.canSpawnInWater ? 0f : 0.03f);

            Transform ruinParent = transform;
            if (chosenTile.isLand && generator.surfaceRoot != null)
            {
                ruinParent = generator.surfaceRoot.transform;
            }
            else if (!chosenTile.isLand && generator.underwaterRoot != null)
            {
                ruinParent = generator.underwaterRoot.transform;
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
                GameObject ruinGO = Instantiate(data.ruinPrefab, position, Quaternion.identity, ruinParent);
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

    private List<RuinData> GetEligibleRuinDataForPlanet(PlanetType planetType)
    {
        var eligible = new List<RuinData>();
        if (ruinDataPool == null) return eligible;

        foreach (var data in ruinDataPool)
        {
            if (data == null) continue;
            if (data.allowedPlanetTypes != null && data.allowedPlanetTypes.Length > 0 &&
                !System.Array.Exists(data.allowedPlanetTypes, pt => pt == planetType))
            {
                continue;
            }

            eligible.Add(data);
        }

        return eligible;
    }

    private List<int> BuildCandidateTileList(TileSystem ts, PlanetGenerator generator, int planetIndex, RuinData data)
    {
        var candidates = new List<int>();
        if (ts == null || generator == null || generator.Grid == null || data == null) return candidates;

        int tileCount = generator.Grid.TileCount;
        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        TileLayer targetLayer = data.canSpawnInWater ? TileLayer.Underwater : TileLayer.Surface;

        for (int i = 0; i < tileCount; i++)
        {
            var tile = ts.GetTileData(i);
            if (tile == null) continue;

            bool isWaterTile = !tile.isLand;
            if (isWaterTile)
            {
                if (!data.canSpawnInWater) continue;
            }
            else
            {
                if (data.canSpawnInWater) continue;
            }

            if (occ != null && occ.GetOccupantObject(i, targetLayer) != null) continue;
            if (HasGeneratedRuinAtTile(ts, planetIndex, i, targetLayer)) continue;

            candidates.Add(i);
        }

        return candidates;
    }

    private bool HasGeneratedRuinAtTile(TileSystem ts, int planetIndex, int tileIndex, TileLayer layer)
    {
        if (ts == null || planetGenerator == null || planetGenerator.Grid == null) return false;

        for (int i = 0; i < generatedRuins.Count; i++)
        {
            var ruin = generatedRuins[i];
            if (ruin == null || ruin.planetIndex != planetIndex) continue;

            int ruinTileIndex = planetGenerator.Grid.GetTileAtPosition(ruin.position);
            if (ruinTileIndex == tileIndex)
            {
                bool ruinIsWater = ruin.ruinData != null && ruin.ruinData.canSpawnInWater;
                if ((ruinIsWater && layer == TileLayer.Underwater) || (!ruinIsWater && layer == TileLayer.Surface))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Picks a RuinData from an already-filtered eligible list using weighted random selection.
    /// </summary>
    private RuinData GetWeightedRandomRuinData(List<RuinData> eligibleRuins)
    {
        if (eligibleRuins == null || eligibleRuins.Count == 0) return null;

        float totalWeight = 0f;
        foreach (var d in eligibleRuins)
        {
            if (d == null) continue;
            totalWeight += d.spawnWeight;
        }

        if (totalWeight <= 0f) return null;

        float roll = Random.Range(0f, totalWeight);
        float cumulative = 0f;
        foreach (var d in eligibleRuins)
        {
            if (d == null) continue;
            cumulative += d.spawnWeight;
            if (roll <= cumulative) return d;
        }
        return eligibleRuins.LastOrDefault(d => d != null);
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

            // Map reveal
            if (data.revealsMap)
            {
                int civIdx = UnitVisionManager.GetCivIndex(civ);
                if (civIdx >= 0 && UnitVisionManager.Instance != null)
                {
                    UnitVisionManager.Instance.RevealTilesAroundPosition(civIdx, ruin.position, data.revealRadius);
                }
                rewards.Add($"Revealed the surrounding area ({data.revealRadius} tiles)!");
            }

            // Population
            if (data.grantsPopulation)
            {
                City nearest = FindNearestCity(civ, ruin.position);
                if (nearest != null)
                {
                    nearest.level += data.populationBonus;
                    nearest.foodGrowthRequirement = nearest.level * 10;
                    rewards.Add($"Population increased by {data.populationBonus} in {nearest.cityName}!");
                }
                else
                {
                    // No city — grant gold as fallback
                    int fallbackGold = data.populationBonus * 25;
                    civ.gold += fallbackGold;
                    rewards.Add($"No nearby city found. Gained {fallbackGold} gold instead!");
                }
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
                    int fallbackRevealRadius = 5;
                    int fallbackCivIdx = UnitVisionManager.GetCivIndex(civ);
                    if (fallbackCivIdx >= 0 && UnitVisionManager.Instance != null)
                    {
                        UnitVisionManager.Instance.RevealTilesAroundPosition(fallbackCivIdx, ruin.position, fallbackRevealRadius);
                    }
                    rewards.Add($"Revealed the surrounding area ({fallbackRevealRadius} tiles)!");
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
                    City nearestCity = FindNearestCity(civ, ruin.position);
                    if (nearestCity != null)
                    {
                        nearestCity.level += 1;
                        nearestCity.foodGrowthRequirement = nearestCity.level * 10;
                        rewards.Add($"Population increased by 1 in {nearestCity.cityName}!");
                    }
                    else
                    {
                        civ.gold += 25;
                        rewards.Add("No nearby city found. Gained 25 gold instead!");
                    }
                    break;
                case RuinType.Upgrade:
                    rewards.Add("A unit has been upgraded!");
                    break;
            }
        }

        OnRuinExplorationCompleted?.Invoke(ruin, civ, rewards);
    }

    private City FindNearestCity(Civilization civ, Vector3 position)
    {
        if (civ.cities == null || civ.cities.Count == 0) return null;
        City nearest = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < civ.cities.Count; i++)
        {
            var city = civ.cities[i];
            if (city == null) continue;
            float dist = (city.transform.position - position).sqrMagnitude;
            if (dist < bestDist)
            {
                bestDist = dist;
                nearest = city;
            }
        }
        return nearest;
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
