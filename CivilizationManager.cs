// Assets/Scripts/Civs/CivilizationManager.cs
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System.Linq;
using SpaceGraphicsToolkit;
using System.Collections.Generic;


public class CivilizationManager : MonoBehaviour
{
    public static CivilizationManager Instance { get; private set; }

    [Header("Prefabs & Data")]
    [Tooltip("Prefab with a Civilization component")]
    public GameObject civilizationPrefab;
    [Tooltip("Prefab with a WorkerUnit component for pioneers")]
    public GameObject pioneerPrefab;
    [Tooltip("WorkerUnitData asset describing the pioneer unit")]
    public WorkerUnitData pioneerData;
    [Tooltip("Prefab with a City component for founding new cities")]
    public GameObject cityPrefab;

    [Header("All Civilization Data")]
    [Tooltip("Include normal civs, tribes (isTribe), and city-states (isCityState). This will be loaded from Resources/Civilizations.")]
    public CivData[] allCivDatas;

    [HideInInspector] public Civilization playerCiv;
    private List<Civilization> civs = new List<Civilization>();
    private int currentCivIndex = -1;
    private TurnManager turnManager;
    
    // Property to access the current turn
    private int currentTurn => turnManager != null ? turnManager.round : 0;
    
    // Public accessor for the list of civilizations
    public List<Civilization> civilizations => new List<Civilization>(civs);

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        // Load all CivData from Resources
        allCivDatas = Resources.LoadAll<CivData>("Civilizations");
        if (allCivDatas == null || allCivDatas.Length == 0) {
            Debug.LogError("CivilizationManager: No CivData assets found in Resources/Civilizations!");
        }
    }

    void Start()
    {
        // Find turn manager reference
        turnManager = FindAnyObjectByType<TurnManager>();
        
        // Optionally find all Civilization components in scene
        var all = FindObjectsByType<Civilization>(FindObjectsSortMode.None);
        foreach (var civ in all) RegisterCiv(civ);
    }

    /// <summary>
    /// Registers a civ so it participates in turn order and tech callbacks.
    /// </summary>
    public void RegisterCiv(Civilization civ)
    {
        if (!civs.Contains(civ))
            civs.Add(civ);
    }

    /// <summary>
    /// Called by a Civilization when it completes a tech.
    /// </summary>
    public void OnTechResearched(Civilization civ, TechData tech)
    {
        // e.g. unlock new abilities, notify AI, UI update
        Debug.Log($"{civ.civData.civName} researched {tech.techName}");
    }

    /// <summary>
    /// Returns a copy of all registered civilizations.
    /// </summary>
    public List<Civilization> GetAllCivs() => new List<Civilization>(civs);

    /// <summary>
    /// Advances to the next civ's turn.
    /// </summary>
    public void AdvanceTurn()
    {
        if (civs.Count == 0) return;
        currentCivIndex = (currentCivIndex + 1) % civs.Count;
        var civ = civs[currentCivIndex];

        // Begin turn for this civ
        civ.BeginTurn(currentTurn);

        // If player civ, enable input; if AI, invoke AI logic here
        if (civ != playerCiv)
        {
            PerformAITurn(civ);
        }
    }

    /// <summary>
    /// Improved metric: sum of unit CurrentAttack + CurrentDefense across all combat units, plus city defense.
    /// </summary>
    public float ComputeMilitaryStrength(Civilization civ)
    {
        float unitStrength = 0f;
        foreach (var u in civ.combatUnits)
        {
            unitStrength += u.CurrentAttack + u.CurrentDefense;
        }
        float cityStrength = 0f;
        foreach (var city in civ.cities)
        {
            cityStrength += city.defenseRating;
        }
        return unitStrength + cityStrength;
    }

    /// <summary>
    /// Handles all AI decision-making for a civilization's turn.
    /// </summary>
    public void PerformAITurn(Civilization civ)
    {
        if (turnManager == null)
            turnManager = FindAnyObjectByType<TurnManager>();
            
        // Enhanced AI turn logic with strategic decision making
        StartCoroutine(CompleteAITurn(civ));
    }

    /// <summary>
    /// Coroutine for handling the completion of an AI turn with sophisticated decision making
    /// </summary>
    private IEnumerator CompleteAITurn(Civilization civ)
    {
        // Wait a small delay to simulate AI thinking (optional)
        yield return new WaitForSeconds(0.5f);
        
        // Perform AI decisions based on leader agenda and current situation
        PerformStrategicDecisions(civ);
        PerformDiplomaticDecisions(civ);
        PerformMilitaryDecisions(civ);
        PerformEconomicDecisions(civ);
        PerformTechnologicalDecisions(civ);
        PerformCulturalDecisions(civ);
        PerformReligiousDecisions(civ);
        
        Debug.Log($"AI turn completed for {civ.civData.civName}");
    }
    
    /// <summary>
    /// Make high-level strategic decisions based on leader agenda
    /// </summary>
    private void PerformStrategicDecisions(Civilization civ)
    {
        if (civ.leader == null) return;
        
        var leader = civ.leader;
        var agenda = leader.primaryAgenda;
        
        // Evaluate current situation
        var situation = EvaluateCivilizationSituation(civ);
        
        // Make decisions based on agenda and situation
        switch (agenda)
        {
            case LeaderAgenda.Militaristic:
                if (situation.militaryStrength < situation.averageMilitaryStrength * 1.2f)
                {
                    // Focus on military production
                    PrioritizeMilitaryProduction(civ);
                }
                else
                {
                    // Look for weak targets
                    ConsiderWarDeclarations(civ);
                }
                break;
                
            case LeaderAgenda.Expansionist:
                if (civ.cities.Count < situation.averageCityCount * 1.5f)
                {
                    // Focus on settling new cities
                    PrioritizeExpansion(civ);
                }
                break;
                
            case LeaderAgenda.Scientific:
                // Always prioritize science and research
                PrioritizeScientificAdvancement(civ);
                break;
                
            case LeaderAgenda.Diplomatic:
                // Seek alliances and avoid conflicts
                PrioritizeDiplomaticSolutions(civ);
                break;
                
            case LeaderAgenda.Economic:
                // Focus on trade and gold generation
                PrioritizeEconomicGrowth(civ);
                break;
                
            case LeaderAgenda.Religious:
                // Focus on faith and religious victory
                PrioritizeReligiousSpread(civ);
                break;
        }
    }
    
    /// <summary>
    /// Evaluate the current situation of a civilization
    /// </summary>
    private CivilizationSituation EvaluateCivilizationSituation(Civilization civ)
    {
        var allCivs = GetAllCivs();
        var situation = new CivilizationSituation();
        
        // Calculate averages
        situation.averageMilitaryStrength = (float)allCivs.Average(c => ComputeMilitaryStrength(c));
        situation.averageCityCount = (float)allCivs.Average(c => c.cities.Count);
        situation.averageGoldPerTurn = (float)allCivs.Average(c => c.cities.Sum(city => city.GetGoldPerTurn()));
        
        // Current civ stats
        situation.militaryStrength = ComputeMilitaryStrength(civ);
        situation.cityCount = civ.cities.Count;
        situation.goldPerTurn = civ.cities.Sum(city => city.GetGoldPerTurn());
        
        // Threat assessment
        situation.threatsNearby = CountNearbyThreats(civ);
        situation.isAtWar = civ.relations.Values.Any(r => r == DiplomaticState.War);
        
        // Opportunities
        situation.weakNeighbors = FindWeakNeighbors(civ);
        situation.potentialAllies = FindPotentialAllies(civ);
        
        return situation;
    }
    
    /// <summary>
    /// Make diplomatic decisions based on personality and situation
    /// </summary>
    private void PerformDiplomaticDecisions(Civilization civ)
    {
        if (civ.leader == null) return;
        
        var leader = civ.leader;
        var memory = DiplomacyManager.Instance.GetDiplomaticMemory(civ);
        
        foreach (var otherCiv in GetAllCivs())
        {
            if (otherCiv == civ) continue;
            
            var currentRelation = DiplomacyManager.Instance.GetRelationship(civ, otherCiv);
            var reputation = memory.GetReputation(otherCiv);
            var trustLevel = memory.GetTrustLevel(otherCiv);
            
            // Evaluate if this civ has traits we like/dislike
            float traitModifier = EvaluateCivilizationTraits(civ, otherCiv);
            
            // Consider diplomatic actions based on agenda
            if (leader.primaryAgenda == LeaderAgenda.Diplomatic && currentRelation == DiplomaticState.Peace)
            {
                if (reputation > 20f && trustLevel >= 6 && UnityEngine.Random.value < 0.3f)
                {
                    // Propose alliance
                    DiplomacyManager.Instance.ProposeDeal(civ, otherCiv, DealType.Alliance);
                }
            }
            else if (leader.primaryAgenda == LeaderAgenda.Militaristic && currentRelation == DiplomaticState.Peace)
            {
                if (reputation < -30f && ComputeMilitaryStrength(civ) > ComputeMilitaryStrength(otherCiv) * 1.3f)
                {
                    // Consider war declaration
                    if (UnityEngine.Random.value < 0.2f)
                    {
                        DiplomacyManager.Instance.ProposeDeal(civ, otherCiv, DealType.War);
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Evaluate traits of another civilization for diplomatic purposes
    /// </summary>
    private float EvaluateCivilizationTraits(Civilization evaluator, Civilization target)
    {
        if (evaluator.leader == null) return 0f;
        
        var leader = evaluator.leader;
        float modifier = 0f;
        
        // Check for warmonger trait
        int warCount = target.relations.Values.Count(r => r == DiplomaticState.War);
        if (warCount >= 2)
        {
            modifier += leader.GetTraitModifier(CivilizationTrait.Warmonger, false);
        }
        
        // Check for religious trait
        if (target.hasFoundedReligion)
        {
            modifier += leader.GetTraitModifier(CivilizationTrait.Religious, true);
        }
        
        // Check for scientific advancement
        if (target.researchedTechs.Count > evaluator.researchedTechs.Count * 1.2f)
        {
            modifier += leader.GetTraitModifier(CivilizationTrait.Scientific, true);
        }
        
        // Check for wealth
        int targetGold = target.cities.Sum(c => c.GetGoldPerTurn());
        int evaluatorGold = evaluator.cities.Sum(c => c.GetGoldPerTurn());
        if (targetGold > evaluatorGold * 1.5f)
        {
            modifier += leader.GetTraitModifier(CivilizationTrait.Wealthy, true);
        }
        
        return modifier;
    }
    
    /// <summary>
    /// Make military decisions
    /// </summary>
    private void PerformMilitaryDecisions(Civilization civ)
    {
        // Placeholder for military AI decisions
        // - Unit movement
        // - Attack decisions
        // - Defensive positioning
    }
    
    /// <summary>
    /// Make economic decisions
    /// </summary>
    private void PerformEconomicDecisions(Civilization civ)
    {
        // Placeholder for economic AI decisions
        // - Trade route establishment
        // - Resource management
        // - City specialization
    }
    
    /// <summary>
    /// Make technological decisions
    /// </summary>
    private void PerformTechnologicalDecisions(Civilization civ)
    {
        if (civ.currentTech != null) return; // Already researching something
        
        var leader = civ.leader;
        var availableTechs = TechManager.Instance.GetAvailableTechs(civ);
        
        if (availableTechs.Count == 0) return;
        
        // Score technologies based on leader priorities
        var scoredTechs = new List<(TechData tech, float score)>();
        
        foreach (var tech in availableTechs)
        {
            float score = CalculateTechScore(civ, tech);
            scoredTechs.Add((tech, score));
        }
        
        // Choose the highest scoring tech
        var bestTech = scoredTechs.OrderByDescending(t => t.score).First().tech;
        TechManager.Instance.StartResearch(civ, bestTech);
    }
    
    /// <summary>
    /// Calculate the value of a technology for this civilization
    /// </summary>
    private float CalculateTechScore(Civilization civ, TechData tech)
    {
        float score = 1f;
        var leader = civ.leader;
        
        // Base score from tech age (prefer current era)
        score += 10f;
        
        // Bonus for leader focus areas
        if (tech.unlockedUnits != null && tech.unlockedUnits.Length > 0)
            score += leader.GetFocusPriority(FocusArea.Military) * 5f;
            
        if (tech.goldModifier > 0 || tech.productionModifier > 0)
            score += leader.GetFocusPriority(FocusArea.Economic) * 5f;
            
        if (tech.scienceModifier > 0)
            score += leader.GetFocusPriority(FocusArea.Scientific) * 5f;
            
        if (tech.cultureModifier > 0)
            score += leader.GetFocusPriority(FocusArea.Cultural) * 5f;
            
        if (tech.faithModifier > 0 || tech.unlocksReligion)
            score += leader.GetFocusPriority(FocusArea.Religious) * 5f;
        
        // Agenda-specific bonuses
        switch (leader.primaryAgenda)
        {
            case LeaderAgenda.Militaristic:
                if (tech.unlockedUnits != null && tech.unlockedUnits.Length > 0)
                    score *= 1.5f;
                break;
            case LeaderAgenda.Scientific:
                if (tech.scienceModifier > 0)
                    score *= 1.5f;
                break;
            case LeaderAgenda.Religious:
                if (tech.unlocksReligion || tech.faithModifier > 0)
                    score *= 1.5f;
                break;
        }
        
        return score;
    }
    
    /// <summary>
    /// Make cultural decisions
    /// </summary>
    private void PerformCulturalDecisions(Civilization civ)
    {
        // Placeholder for cultural AI decisions
        // - Culture research priorities
        // - Policy adoption
        // - Government changes
    }
    
    /// <summary>
    /// Make religious decisions
    /// </summary>
    private void PerformReligiousDecisions(Civilization civ)
    {
        // Placeholder for religious AI decisions
        // - Pantheon founding
        // - Religion founding
        // - Missionary production and movement
    }
    
    // Helper methods for strategic decisions
    private void PrioritizeMilitaryProduction(Civilization civ) { /* Implementation */ }
    private void ConsiderWarDeclarations(Civilization civ) { /* Implementation */ }
    private void PrioritizeExpansion(Civilization civ) { /* Implementation */ }
    private void PrioritizeScientificAdvancement(Civilization civ) { /* Implementation */ }
    private void PrioritizeDiplomaticSolutions(Civilization civ) { /* Implementation */ }
    private void PrioritizeEconomicGrowth(Civilization civ) { /* Implementation */ }
    private void PrioritizeReligiousSpread(Civilization civ) { /* Implementation */ }
    
    private int CountNearbyThreats(Civilization civ) { return 0; /* Implementation */ }
    private List<Civilization> FindWeakNeighbors(Civilization civ) { return new List<Civilization>(); }
    private List<Civilization> FindPotentialAllies(Civilization civ) { return new List<Civilization>(); }

    /// <summary>
    /// Spawns the player civ, AI civs, tribes, and city-states after the map is generated.
    /// Instead of cities, each civ starts with a pioneer unit at their start tile.
    /// </summary>
    public void SpawnCivilizations(CivData playerCivData, int aiCount, int cityStateCount, int tribeCount)
    {
        Debug.Log($"SpawnCivilizations called: AI={aiCount}, CityStates={cityStateCount}, Tribes={tribeCount}");
        
        // Clear any existing civs
        civs.Clear();
        currentCivIndex = -1;
        
        var planet   = FindAnyObjectByType<PlanetGenerator>();
        var grid      = planet != null ? planet.Grid : null;
        var occupied = new HashSet<int>();

        // Check if allCivDatas is populated
        if (allCivDatas == null || allCivDatas.Length == 0)
        {
            Debug.LogError("allCivDatas is null or empty! Make sure CivData assets are in Resources/Civilizations/");
            return;
        }
        

        // Partition CivData pools
        var normalPool    = allCivDatas.Where(d => !d.isTribe && !d.isCityState).ToList();
        var tribePool     = allCivDatas.Where(d => d.isTribe).ToList();
        var cityStatePool = allCivDatas.Where(d => d.isCityState).ToList();


        // 1) Player civ
        if (playerCivData == null)
        {
            Debug.LogWarning("Player CivData is null! Selecting a default civilization.");
            
            // Try to find a suitable default civ from the normal pool
            if (normalPool.Count > 0)
            {
                playerCivData = normalPool[0]; // Select first available normal civ
                Debug.Log($"Selected default civilization: {playerCivData.civName}");
                
                // Update GameSetupData to reflect this choice
                GameSetupData.selectedPlayerCivilizationData = playerCivData;
            }
            else
            {
                Debug.LogError("No normal civilizations available to use as default!");
                return;
            }
        }
        
        // Remove player civ from the pool to avoid duplicates
        if (normalPool.Contains(playerCivData))
        {
            normalPool.Remove(playerCivData);
            Debug.Log($"Removed player civ from normal pool. Remaining normal civs: {normalPool.Count}");
        }
        
        // Spawn the player civilization
        Debug.Log("Spawning player civilization...");
        SpawnOneCivilization(playerCivData, occupied, isPlayer: true);

        // 2) AI civs
        Debug.Log($"Spawning {aiCount} AI civilizations from {normalPool.Count} available...");
        Shuffle(normalPool);
        for (int i = 0; i < aiCount && i < normalPool.Count; i++)
        {
            Debug.Log($"Spawning AI civ {i + 1}/{aiCount}: {normalPool[i].civName}");
            SpawnOneCivilization(normalPool[i], occupied, isPlayer: false);
        }

        // 3) City-states
        Debug.Log($"Spawning {cityStateCount} city-states from {cityStatePool.Count} available...");
        Shuffle(cityStatePool);
        for (int i = 0; i < cityStateCount && i < cityStatePool.Count; i++)
        {
            Debug.Log($"Spawning city-state {i + 1}/{cityStateCount}: {cityStatePool[i].civName}");
            SpawnOneCivilization(cityStatePool[i], occupied, isPlayer: false);
        }

        // 4) Tribes
        Debug.Log($"Spawning {tribeCount} tribes from {tribePool.Count} available...");
        Shuffle(tribePool);
        for (int i = 0; i < tribeCount && i < tribePool.Count; i++)
        {
            Debug.Log($"Spawning tribe {i + 1}/{tribeCount}: {tribePool[i].civName}");
            SpawnOneCivilization(tribePool[i], occupied, isPlayer: false);
        }

        // 5) Start turn cycle with the first turn
        // Assign player civ to TurnManager
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.playerCiv = playerCiv;
            
            // Register all spawned civs with TurnManager if needed
            foreach (var civ in civs)
            {
                TurnManager.Instance.RegisterCivilization(civ);
            }
            
            // Start the turn cycle
            TurnManager.Instance.StartTurns();
            
            Debug.Log($"Turn cycle started with player civ: {playerCiv?.civData?.civName ?? "NULL"}");
        }
        else
        {
            Debug.LogError("TurnManager.Instance is null! Cannot start turn cycle.");
            // Fallback: just advance turn once
            AdvanceTurn();
        }
        
        Debug.Log($"Civilization spawning complete. Total civs spawned: {civs.Count}");
    }

    /// <summary>
    /// Instantiates a Civilization and its starting pioneer.
    /// </summary>
    void SpawnOneCivilization(CivData data, HashSet<int> occupied, bool isPlayer)
    {
        Debug.Log($"Attempting to spawn civilization: {data?.civName ?? "NULL"}");
        
        // Check for null data
        if (data == null)
        {
            Debug.LogError("SpawnOneCivilization: CivData is null!");
            return;
        }
        
        var planet = FindAnyObjectByType<PlanetGenerator>();
        var grid  = planet != null ? planet.Grid : null;
        if (grid == null)
        {
            Debug.LogError("SpawnOneCivilization: SphericalHexGrid not found!");
            return;
        }
        
        var tile = FindSpawnTile(data, occupied, enforceClimate: true);
        if (tile < 0) tile = FindSpawnTile(data, occupied, enforceClimate: false);
        if (tile < 0)
        {
            Debug.LogError($"No valid spawn tile for {data.civName}");
            return;
        }
        occupied.Add(tile);

        // Check if civilizationPrefab is assigned
        if (civilizationPrefab == null)
        {
            Debug.LogError("SpawnOneCivilization: civilizationPrefab is not assigned in CivilizationManager!");
            return;
        }

        // Instantiate Civilization
        GameObject civGO = Instantiate(civilizationPrefab);
        if (civGO == null)
        {
            Debug.LogError($"Failed to instantiate civilization prefab for {data.civName}");
            return;
        }
        
        civGO.name = data.civName;
        var civ = civGO.GetComponent<Civilization>();
        if (civ == null)
        {
            Debug.LogError($"Civilization component not found on prefab for {data.civName}!");
            Destroy(civGO);
            return;
        }
        
        // --- Leader Selection ---
        LeaderData chosenLeader = null;
        if (isPlayer)
        {
            // For the player, use the leader selected in the main menu
            chosenLeader = GameSetupData.selectedLeaderData;
            // Fallback to default if none was selected
            if (chosenLeader == null && data.availableLeaders.Count > 0)
            {
                Debug.LogWarning($"Player selected {data.civName} but no leader was chosen in GameSetupData. Assigning default leader: {data.availableLeaders[0].name}");
                chosenLeader = data.availableLeaders[0];
            }
        }
        else
        {
            // For AI, pick a random available leader
            if (data.availableLeaders != null && data.availableLeaders.Count > 0)
            {
                chosenLeader = data.availableLeaders[UnityEngine.Random.Range(0, data.availableLeaders.Count)];
            }
        }

        if (chosenLeader == null)
        {
            Debug.LogError($"Could not assign a leader for {data.civName}! Check that leaders are assigned in the CivData asset.");
            Destroy(civGO);
            return;
        }
        // --- End Leader Selection ---

        civ.Initialize(data, chosenLeader, isPlayer, grid, planet);
        civs.Add(civ);

        if (isPlayer)
        {
            playerCiv = civ;
        }

        // Check if pioneerPrefab and pioneerData are assigned
        if (pioneerPrefab == null)
        {
            Debug.LogError("SpawnOneCivilization: pioneerPrefab is not assigned in CivilizationManager!");
            return;
        }
        
        if (pioneerData == null)
        {
            Debug.LogError("SpawnOneCivilization: pioneerData is not assigned in CivilizationManager!");
            return;
        }

        // Instantiate pioneer
        Vector3 pos = planet.Grid.tileCenters[tile];
        var wgo = Instantiate(pioneerPrefab, pos, Quaternion.identity);
        if (wgo == null)
        {
            Debug.LogError($"Failed to instantiate pioneer prefab for {data.civName}");
            return;
        }
        
        var pioneer = wgo.GetComponent<WorkerUnit>();
        if (pioneer == null)
        {
            Debug.LogError($"WorkerUnit component not found on pioneer prefab for {data.civName}!");
            Destroy(wgo);
            return;
        }
        
        pioneer.Initialize(pioneerData, civ, tile);
        civ.workerUnits.Add(pioneer);
        
        Debug.Log($"Successfully spawned civilization: {data.civName} (Player: {isPlayer}) at tile {tile}");
    }

    /// <summary>
    /// Finds a random unoccupied land tile, optionally matching climate preferences.
    /// </summary>
    int FindSpawnTile(CivData data, HashSet<int> occupied, bool enforceClimate)
    {
        var planet = FindAnyObjectByType<PlanetGenerator>();
        var grid    = planet != null ? planet.Grid : null;
        var candidates = new List<int>();

        // Get the total tile count in a safer way
        int tileCount = grid.TileCount;

        for (int i = 0; i < tileCount; i++)
        {
            if (occupied.Contains(i)) continue;
            var td = planet.GetHexTileData(i);
            if (td == null) continue;
            if (!td.isLand) continue;

            if (enforceClimate && data.climatePreferences.Length > 0)
            {
                if (!data.climatePreferences.Contains(td.biome))
                    continue;
            }
            candidates.Add(i);
        }

        return candidates.Count > 0
            ? candidates[UnityEngine.Random.Range(0, candidates.Count)]
            : -1;
    }

    /// <summary>
    /// Fisher–Yates shuffle.
    /// </summary>
    void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>
    /// Called when a city revolts. Either reuses an existing "Rebels" civ or creates a new one.
    /// </summary>
    public Civilization CreateRebelFaction(City revoltedCity)
    {
        // Try to reuse an existing rebel faction
        var existing = civs.FirstOrDefault(c => c.civData.civName.StartsWith("Rebels"));
        if (existing != null) return existing;

        // Otherwise, spawn a new one
        var go = Instantiate(civilizationPrefab);
        int rebelCounter = civs.Count(c => c.civData.civName.Contains("Rebel")) + 1;
        go.name = $"Rebels {rebelCounter}";
        var civ = go.GetComponent<Civilization>();

        // Pick a generic CivData (e.g. a city-state template)
        var template = allCivDatas.FirstOrDefault(d => d.isCityState) ?? allCivDatas[0];
        var planet = FindAnyObjectByType<PlanetGenerator>();
        var grid = planet != null ? planet.Grid : null;
        civ.Initialize(template, null, false, grid, planet);

        RegisterCiv(civ);
        Debug.Log($"Created new rebel faction '{go.name}' from revolt in {revoltedCity.cityName}");
        return civ;
    }

    void Update()
    {
        // Example: player presses End Turn
        if (Input.GetKeyDown(KeyCode.Space))
            AdvanceTurn();
    }

    public IEnumerator PerformAITurnCoroutine(Civilization civ)
    {
        yield return StartCoroutine(CompleteAITurn(civ));
    }
}

/// <summary>
/// Data structure to hold information about a civilization's current situation
/// </summary>
public class CivilizationSituation
{
    public float militaryStrength;
    public int cityCount;
    public int goldPerTurn;
    public float averageMilitaryStrength;
    public float averageCityCount;
    public float averageGoldPerTurn;
    public int threatsNearby;
    public bool isAtWar;
    public List<Civilization> weakNeighbors = new List<Civilization>();
    public List<Civilization> potentialAllies = new List<Civilization>();
}