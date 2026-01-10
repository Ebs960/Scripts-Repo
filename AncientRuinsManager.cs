using System.Collections.Generic;
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
                Vector3 position = (TileSystem.Instance != null && TileSystem.Instance.IsReady())
                    ? TileSystem.Instance.GetTileCenterFlat(tileIndex)
                    : planetGenerator.transform.position;
                GameObject ruinGO = Instantiate(ruinPrefab, position, Quaternion.identity, transform);
                ruins.Add(ruinGO.GetComponent<AncientRuin>());
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

            float distance = Vector3.Distance(unitPosition, ruin.position);
            if (distance <= 2.0f) // Discovery range
            {
                DiscoverRuin(ruin, civilization);
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
                civ.culture += culture; // Use existing culture field
                rewards.Add($"Gained {culture} culture from ancient writings!");
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
