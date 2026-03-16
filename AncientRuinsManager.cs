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
    }

    // Events for UI and game system integration
    public System.Action<RuinSite, Civilization> OnRuinDiscovered;
    public System.Action<RuinSite, Civilization, List<string>> OnRuinExplorationCompleted;

    // List of all generated ruins
    public List<RuinSite> generatedRuins = new List<RuinSite>();

    public static AncientRuinsManager Instance { get; private set; }

    public GameObject ruinPrefab;
    public int numberOfRuinsToSpawn = 10;

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
        }
    }

    public void SpawnRuins(PlanetGenerator generator)
    {
        planetGenerator = generator;
        if (planetGenerator == null) return;

        for (int i = 0; i < numberOfRuinsToSpawn; i++)
        {
            int tileIndex = Random.Range(0, planetGenerator.Grid.TileCount);
            HexTileData tileData = planetGenerator.GetHexTileData(tileIndex);

            if (tileData.biome != Biome.Ocean && tileData.biome != Biome.Seas)
            {
                var ts = TileSystem.GetForPlanet(planetGenerator.planetIndex) ?? TileSystem.Instance;
                Vector3 position = (ts != null && ts.IsReady())
                    ? ts.GetTileSurfacePosition(tileIndex)
                    : planetGenerator.transform.position;
                GameObject ruinGO = Instantiate(ruinPrefab, position, Quaternion.identity, transform);
                ruins.Add(ruinGO.GetComponent<AncientRuin>());
                // Register ruin with HexMapChunkManager so it follows wrap teleport
                try
                {
                    var mgr = FindObjectsByType<HexMapChunkManager>(FindObjectsSortMode.None).FirstOrDefault(m => m.PlanetGenerator == planetGenerator);
                    if (mgr != null)
                    {
                        var pg = planetGenerator;
                        if (pg != null && pg.Grid != null)
                        {
                            int tile = pg.Grid.GetTileAtPosition(position);
                            if (tile >= 0) mgr.RegisterObjectForWrapAtTile(tile, ruinGO);
                        }
                    }
                }
                catch { }
            }
        }
    }

    // Civ 5 style methods for discovering and exploring ruins
    public void CheckForRuinDiscovery(int planetIndex, Vector3 unitPosition, Civilization civilization)
    {
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
                var pg = civilization != null ? civilization.GetPlanetGeneratorForIndex(planetIndex) ?? (GameManager.Instance != null ? GameManager.Instance.GetPlanetGenerator(planetIndex) : null)
                                                   : (GameManager.Instance != null ? GameManager.Instance.GetPlanetGenerator(planetIndex) : null);
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
    }

    public void ExploreRuin(RuinSite ruin, Civilization civ)
    {
        if (ruin.isExplored) return;
        ruin.isExplored = true;
        var rewards = new List<string>();
        
        switch (ruin.ruinType)
        {
            case RuinType.Technology:
                rewards.Add("Discovered a lost technology!");
                break;
            case RuinType.Gold:
                int gold = UnityEngine.Random.Range(50, 201);
                civ.gold += gold; // Use existing gold field
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
                    civ.culture += culture; // Fallback: add to civ culture for this turn
                    rewards.Add($"Gained {culture} culture from ancient writings!");
                }
                break;
            case RuinType.Faith:
                int faith = UnityEngine.Random.Range(15, 75);
                civ.faith += faith; // Use existing faith field
                rewards.Add($"Gained {faith} faith from sacred relics!");
                break;
            case RuinType.Population:
                rewards.Add("Population increased in your nearest city!");
                break;
            case RuinType.Upgrade:
                rewards.Add("A unit has been upgraded!");
                break;
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
