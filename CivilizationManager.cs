// Assets/Scripts/Civs/CivilizationManager.cs
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System.Linq;
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
        allCivDatas = ResourceCache.GetAllCivDatas();
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
        // REMOVED: TechData no longer directly unlocks units
        // Military focus bonus now based on military-related modifiers instead
            
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
                // REMOVED: TechData no longer directly unlocks units
                // Military agenda bonus now based on military-related modifiers instead
                if (tech.attackBonus > 0 || tech.defenseBonus > 0)
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
    /// <summary>
    /// Prioritize military unit production in cities with high production
    /// </summary>
    private void PrioritizeMilitaryProduction(Civilization civ)
    {
        if (civ == null || civ.cities == null || civ.cities.Count == 0) return;
        
        // Get all available combat units
        var allUnitData = ResourceCache.GetAllCombatUnits();
        if (allUnitData == null || allUnitData.Length == 0) return;
        
        // Filter to available military units (meet requirements)
        var availableUnits = new List<CombatUnitData>();
        foreach (var unitData in allUnitData)
        {
            if (unitData == null) continue;
            if (unitData.AreRequirementsMet(civ))
            {
                // Use unique unit replacement if available
                var unitToUse = civ.GetUnitData(unitData);
                availableUnits.Add(unitToUse);
            }
        }
        
        if (availableUnits.Count == 0) return;
        
        // Sort cities by production (highest first)
        var citiesByProduction = civ.cities
            .Where(c => c != null && c.GetProductionPerTurn() > 0)
            .OrderByDescending(c => c.GetProductionPerTurn())
            .ToList();
        
        // Prioritize units based on leader agenda
        var leader = civ.leader;
        var preferredUnitTypes = new List<CombatCategory>();
        
        if (leader != null)
        {
            // Militaristic leaders prefer melee units
            if (leader.primaryAgenda == LeaderAgenda.Militaristic)
            {
                preferredUnitTypes.AddRange(new[] { 
                    CombatCategory.Swordsman, CombatCategory.Spearman, 
                    CombatCategory.Axeman, CombatCategory.Cavalry 
                });
            }
        }
        
        // Queue units in cities
        int unitsQueued = 0;
        int maxUnitsToQueue = Mathf.Min(citiesByProduction.Count, 3); // Queue in top 3 cities
        
        foreach (var city in citiesByProduction.Take(maxUnitsToQueue))
        {
            if (city == null) continue;
            
            // Skip if city already has something in production
            if (city.productionQueue != null && city.productionQueue.Count > 0) continue;
            
            // Find best unit for this city
            CombatUnitData bestUnit = null;
            
            // Prefer units matching leader agenda
            if (preferredUnitTypes.Count > 0)
            {
                bestUnit = availableUnits.FirstOrDefault(u => preferredUnitTypes.Contains(u.unitType));
            }
            
            // Fallback to any available unit
            if (bestUnit == null)
            {
                bestUnit = availableUnits.FirstOrDefault();
            }
            
            if (bestUnit != null && city.QueueProduction(bestUnit))
            {
                unitsQueued++;
                Debug.Log($"[CivilizationManager] {civ.civData.civName}: Queued {bestUnit.unitName} in {city.cityName}");
            }
        }
    }
    
    /// <summary>
    /// Consider declaring war on weak neighbors
    /// </summary>
    private void ConsiderWarDeclarations(Civilization civ)
    {
        if (civ == null || civ.leader == null) return;
        
        var leader = civ.leader;
        var myStrength = ComputeMilitaryStrength(civ);
        var weakNeighbors = FindWeakNeighbors(civ);
        
        if (weakNeighbors == null || weakNeighbors.Count == 0) return;
        
        // Check leader traits
        float warChance = 0.1f; // Base 10% chance
        if (leader.isWarmonger) warChance *= 2f; // Warmongers more likely
        if (leader.primaryAgenda == LeaderAgenda.Militaristic) warChance *= 1.5f;
        
        // Consider each weak neighbor
        foreach (var target in weakNeighbors)
        {
            if (target == null) continue;
            
            // Check current relationship
            var currentRelation = DiplomacyManager.Instance != null 
                ? DiplomacyManager.Instance.GetRelationship(civ, target) 
                : DiplomaticState.Peace;
            
            if (currentRelation != DiplomaticState.Peace) continue; // Already at war or allied
            
            // Check diplomatic memory
            if (DiplomacyManager.Instance != null)
            {
                var memory = DiplomacyManager.Instance.GetDiplomaticMemory(civ);
                var reputation = memory.GetReputation(target);
                var trustLevel = memory.GetTrustLevel(target);
                
                // Less likely to declare war on trusted allies
                if (trustLevel >= 7) warChance *= 0.3f;
                if (reputation > 20f) warChance *= 0.5f;
                
                // More likely if they have negative reputation
                if (reputation < -20f) warChance *= 1.5f;
            }
            
            // Check military strength ratio
            var targetStrength = ComputeMilitaryStrength(target);
            float strengthRatio = myStrength / Mathf.Max(targetStrength, 1f);
            
            // More likely if we're significantly stronger
            if (strengthRatio >= 1.5f) warChance *= 1.5f;
            if (strengthRatio >= 2.0f) warChance *= 2f;
            
            // Check for shared borders (casus belli)
            bool sharesBorder = CheckSharedBorders(civ, target);
            if (sharesBorder) warChance *= 1.3f;
            
            // Roll the dice
            if (UnityEngine.Random.value < warChance)
            {
                if (DiplomacyManager.Instance != null)
                {
                    DiplomacyManager.Instance.ProposeDeal(civ, target, DealType.War);
                    Debug.Log($"[CivilizationManager] {civ.civData.civName} declared war on {target.civData.civName}");
                }
                break; // Only declare one war per turn
            }
        }
    }
    
    /// <summary>
    /// Prioritize expansion by queueing pioneer production
    /// </summary>
    private void PrioritizeExpansion(Civilization civ)
    {
        if (civ == null || civ.cities == null || civ.cities.Count == 0) return;
        
        // Check expansion limits (tribes limited to 3 cities)
        if (civ.civData != null && civ.civData.isTribe && civ.cities.Count >= 3)
        {
            return; // Tribes can't expand beyond 3 cities
        }
        
        // Get pioneer unit data
        if (pioneerData == null)
        {
            Debug.LogWarning("[CivilizationManager] PrioritizeExpansion: pioneerData not assigned");
            return;
        }
        
        // Check if pioneer can be produced
        if (!pioneerData.AreRequirementsMet(civ)) return;
        
        // Find cities that can produce pioneers
        var citiesByProduction = civ.cities
            .Where(c => c != null && c.GetProductionPerTurn() > 0)
            .OrderByDescending(c => c.GetProductionPerTurn())
            .ToList();
        
        // Queue pioneer in the best production city that doesn't already have production
        foreach (var city in citiesByProduction)
        {
            if (city == null) continue;
            if (city.productionQueue != null && city.productionQueue.Count > 0) continue;
            
            if (city.QueueProduction(pioneerData))
            {
                Debug.Log($"[CivilizationManager] {civ.civData.civName}: Queued pioneer in {city.cityName} for expansion");
                break; // Only queue one pioneer per turn
            }
        }
    }
    
    /// <summary>
    /// Prioritize scientific advancement by queueing science buildings
    /// </summary>
    private void PrioritizeScientificAdvancement(Civilization civ)
    {
        if (civ == null || civ.cities == null || civ.cities.Count == 0) return;
        
        // Get all available buildings
        var allBuildings = ResourceCache.GetAllBuildings();
        if (allBuildings == null || allBuildings.Length == 0) return;
        
        // Filter to science buildings (buildings that provide science)
        var scienceBuildings = allBuildings
            .Where(b => b != null && b.AreRequirementsMet(civ) && b.sciencePerTurn > 0)
            .OrderByDescending(b => b.sciencePerTurn)
            .ToList();
        
        if (scienceBuildings.Count == 0) return;
        
        // Find cities without science buildings
        var citiesNeedingScience = civ.cities
            .Where(c => c != null && c.GetProductionPerTurn() > 0)
            .Where(c => !HasBuildingType(c, scienceBuildings))
            .OrderByDescending(c => c.GetProductionPerTurn())
            .ToList();
        
        // Queue science buildings in cities
        int buildingsQueued = 0;
        int maxBuildingsToQueue = Mathf.Min(citiesNeedingScience.Count, 2); // Queue in top 2 cities
        
        foreach (var city in citiesNeedingScience.Take(maxBuildingsToQueue))
        {
            if (city == null) continue;
            if (city.productionQueue != null && city.productionQueue.Count > 0) continue;
            
            // Find best science building for this city
            var bestBuilding = scienceBuildings.FirstOrDefault();
            
            if (bestBuilding != null && city.QueueProduction(bestBuilding))
            {
                buildingsQueued++;
                Debug.Log($"[CivilizationManager] {civ.civData.civName}: Queued {bestBuilding.buildingName} in {city.cityName}");
            }
        }
    }
    
    /// <summary>
    /// Prioritize diplomatic solutions by seeking alliances
    /// </summary>
    private void PrioritizeDiplomaticSolutions(Civilization civ)
    {
        if (civ == null || civ.leader == null || DiplomacyManager.Instance == null) return;
        
        var leader = civ.leader;
        var potentialAllies = FindPotentialAllies(civ);
        
        if (potentialAllies == null || potentialAllies.Count == 0) return;
        
        // Only diplomatic leaders actively seek alliances
        if (leader.primaryAgenda != LeaderAgenda.Diplomatic) return;
        
        // Consider each potential ally
        foreach (var target in potentialAllies)
        {
            if (target == null) continue;
            
            // Check current relationship
            var currentRelation = DiplomacyManager.Instance.GetRelationship(civ, target);
            if (currentRelation != DiplomaticState.Peace) continue; // Already allied or at war
            
            // Check diplomatic memory
            var memory = DiplomacyManager.Instance.GetDiplomaticMemory(civ);
            var reputation = memory.GetReputation(target);
            var trustLevel = memory.GetTrustLevel(target);
            
            // Propose alliance if conditions are good
            if (reputation > 20f && trustLevel >= 6 && UnityEngine.Random.value < 0.3f)
            {
                DiplomacyManager.Instance.ProposeDeal(civ, target, DealType.Alliance);
                Debug.Log($"[CivilizationManager] {civ.civData.civName} proposed alliance to {target.civData.civName}");
                break; // Only propose one alliance per turn
            }
        }
    }
    
    /// <summary>
    /// Prioritize economic growth by queueing gold-generating buildings
    /// </summary>
    private void PrioritizeEconomicGrowth(Civilization civ)
    {
        if (civ == null || civ.cities == null || civ.cities.Count == 0) return;
        
        // Get all available buildings
        var allBuildings = ResourceCache.GetAllBuildings();
        if (allBuildings == null || allBuildings.Length == 0) return;
        
        // Filter to economic buildings (buildings that provide gold)
        var economicBuildings = allBuildings
            .Where(b => b != null && b.AreRequirementsMet(civ) && b.goldPerTurn > 0)
            .OrderByDescending(b => b.goldPerTurn)
            .ToList();
        
        if (economicBuildings.Count == 0) return;
        
        // Find cities without economic buildings
        var citiesNeedingGold = civ.cities
            .Where(c => c != null && c.GetProductionPerTurn() > 0)
            .Where(c => !HasBuildingType(c, economicBuildings))
            .OrderByDescending(c => c.GetProductionPerTurn())
            .ToList();
        
        // Queue economic buildings in cities
        int buildingsQueued = 0;
        int maxBuildingsToQueue = Mathf.Min(citiesNeedingGold.Count, 2); // Queue in top 2 cities
        
        foreach (var city in citiesNeedingGold.Take(maxBuildingsToQueue))
        {
            if (city == null) continue;
            if (city.productionQueue != null && city.productionQueue.Count > 0) continue;
            
            // Find best economic building for this city
            var bestBuilding = economicBuildings.FirstOrDefault();
            
            if (bestBuilding != null && city.QueueProduction(bestBuilding))
            {
                buildingsQueued++;
                Debug.Log($"[CivilizationManager] {civ.civData.civName}: Queued {bestBuilding.buildingName} in {city.cityName}");
            }
        }
    }
    
    /// <summary>
    /// Prioritize religious spread by founding pantheons/religions and building religious buildings
    /// </summary>
    private void PrioritizeReligiousSpread(Civilization civ)
    {
        if (civ == null) return;
        
        // Try to found pantheon if not already founded
        if (civ.foundedPantheons == null || civ.foundedPantheons.Count == 0)
        {
            if (ReligionManager.Instance != null)
            {
                var availablePantheons = ReligionManager.Instance.GetAvailablePantheons();
                if (availablePantheons != null && availablePantheons.Count > 0)
                {
                    // Check if we have enough faith
                    var pantheon = availablePantheons[0]; // Pick first available
                    if (pantheon != null && civ.faith >= pantheon.faithCost)
                    {
                        // Get available beliefs for this pantheon
                        if (pantheon.possibleFounderBeliefs != null && pantheon.possibleFounderBeliefs.Length > 0)
                        {
                            var belief = pantheon.possibleFounderBeliefs[0]; // Pick first belief
                            if (civ.FoundPantheon(pantheon, belief))
                            {
                                Debug.Log($"[CivilizationManager] {civ.civData.civName} founded pantheon: {pantheon.pantheonName}");
                            }
                        }
                    }
                }
            }
        }
        
        // Try to found religion if we have a pantheon but no religion
        if (civ.foundedPantheons != null && civ.foundedPantheons.Count > 0 && !civ.hasFoundedReligion)
        {
            if (ReligionManager.Instance != null)
            {
                var availableReligions = ReligionManager.Instance.GetAvailableReligions();
                if (availableReligions != null && availableReligions.Count > 0)
                {
                    // Find a city with a holy site
                    var holySiteCity = civ.cities.FirstOrDefault(c => c != null && c.HasHolySite());
                    if (holySiteCity != null)
                    {
                        var religion = availableReligions[0]; // Pick first available
                        if (religion != null && civ.faith >= religion.faithCost)
                        {
                            if (civ.FoundReligion(religion, holySiteCity))
                            {
                                Debug.Log($"[CivilizationManager] {civ.civData.civName} founded religion: {religion.religionName}");
                            }
                        }
                    }
                }
            }
        }
        
        // Queue religious buildings in cities
        if (civ.cities != null && civ.cities.Count > 0)
        {
            var allBuildings = ResourceCache.GetAllBuildings();
            if (allBuildings != null && allBuildings.Length > 0)
            {
                // Filter to religious buildings (buildings that provide faith)
                var religiousBuildings = allBuildings
                    .Where(b => b != null && b.AreRequirementsMet(civ) && b.faithPerTurn > 0)
                    .OrderByDescending(b => b.faithPerTurn)
                    .ToList();
                
                if (religiousBuildings.Count > 0)
                {
                    var citiesNeedingFaith = civ.cities
                        .Where(c => c != null && c.GetProductionPerTurn() > 0)
                        .Where(c => !HasBuildingType(c, religiousBuildings))
                        .OrderByDescending(c => c.GetProductionPerTurn())
                        .ToList();
                    
                    foreach (var city in citiesNeedingFaith.Take(1)) // Queue in one city
                    {
                        if (city == null) continue;
                        if (city.productionQueue != null && city.productionQueue.Count > 0) continue;
                        
                        var bestBuilding = religiousBuildings.FirstOrDefault();
                        if (bestBuilding != null && city.QueueProduction(bestBuilding))
                        {
                            Debug.Log($"[CivilizationManager] {civ.civData.civName}: Queued {bestBuilding.buildingName} in {city.cityName}");
                        }
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Count nearby enemy military units within threat range
    /// </summary>
    private int CountNearbyThreats(Civilization civ)
    {
        if (civ == null || civ.ownedTileIndices == null || TileSystem.Instance == null) return 0;
        
        int threatCount = 0;
        const int threatRange = 3; // Tiles to check around owned tiles
        
        // Get all owned tiles
        var ownedTiles = new HashSet<int>(civ.ownedTileIndices);
        
        // Check each owned tile's neighbors
        foreach (int tileIndex in ownedTiles)
        {
            if (tileIndex < 0) continue;
            
            // Get tiles in threat range
            var tilesInRange = GetTilesInRange(tileIndex, threatRange);
            
            foreach (int neighborTile in tilesInRange)
            {
                if (neighborTile < 0) continue;
                
                // Check if tile is owned by an enemy
                var tileData = TileSystem.Instance.GetTileData(neighborTile);
                if (tileData == null || tileData.owner == null) continue;
                if (tileData.owner == civ) continue; // Own tile
                
                // Check if we're at war with this civ
                var currentRelation = DiplomacyManager.Instance != null
                    ? DiplomacyManager.Instance.GetRelationship(civ, tileData.owner)
                    : DiplomaticState.Peace;
                
                if (currentRelation == DiplomaticState.War)
                {
                    // Count enemy units on this tile
                    var enemyUnits = tileData.owner.combatUnits
                        .Where(u => u != null && u.currentTileIndex == neighborTile)
                        .Count();
                    
                    threatCount += enemyUnits;
                }
            }
        }
        
        return threatCount;
    }
    
    /// <summary>
    /// Find neighboring civilizations that are weaker than us
    /// </summary>
    private List<Civilization> FindWeakNeighbors(Civilization civ)
    {
        var weakNeighbors = new List<Civilization>();
        if (civ == null || civ.ownedTileIndices == null || TileSystem.Instance == null) return weakNeighbors;
        
        var myStrength = ComputeMilitaryStrength(civ);
        var neighboringCivs = new HashSet<Civilization>();
        
        // Find all neighboring civilizations
        foreach (int tileIndex in civ.ownedTileIndices)
        {
            if (tileIndex < 0) continue;
            
            var neighbors = TileSystem.Instance.GetNeighbors(tileIndex);
            if (neighbors == null) continue;
            
            foreach (int neighborTile in neighbors)
            {
                if (neighborTile < 0) continue;
                
                var tileData = TileSystem.Instance.GetTileData(neighborTile);
                if (tileData == null || tileData.owner == null) continue;
                if (tileData.owner == civ) continue; // Own tile
                
                neighboringCivs.Add(tileData.owner);
            }
        }
        
        // Filter to weak neighbors (at least 1.3x stronger)
        const float strengthThreshold = 1.3f;
        foreach (var neighbor in neighboringCivs)
        {
            if (neighbor == null) continue;
            
            // Check diplomatic state (only consider peace/neutral)
            var currentRelation = DiplomacyManager.Instance != null
                ? DiplomacyManager.Instance.GetRelationship(civ, neighbor)
                : DiplomaticState.Peace;
            
            if (currentRelation != DiplomaticState.Peace && currentRelation != DiplomaticState.Trade) continue;
            
            var neighborStrength = ComputeMilitaryStrength(neighbor);
            float strengthRatio = myStrength / Mathf.Max(neighborStrength, 1f);
            
            if (strengthRatio >= strengthThreshold)
            {
                weakNeighbors.Add(neighbor);
            }
        }
        
        return weakNeighbors;
    }
    
    /// <summary>
    /// Find civilizations suitable for alliance
    /// </summary>
    private List<Civilization> FindPotentialAllies(Civilization civ)
    {
        var potentialAllies = new List<Civilization>();
        if (civ == null || DiplomacyManager.Instance == null) return potentialAllies;
        
        var allCivs = GetAllCivs();
        if (allCivs == null) return potentialAllies;
        
        var myStrength = ComputeMilitaryStrength(civ);
        
        foreach (var otherCiv in allCivs)
        {
            if (otherCiv == null || otherCiv == civ) continue;
            
            // Check current relationship (must be at peace)
            var currentRelation = DiplomacyManager.Instance.GetRelationship(civ, otherCiv);
            if (currentRelation != DiplomaticState.Peace) continue;
            
            // Check diplomatic memory
            var memory = DiplomacyManager.Instance.GetDiplomaticMemory(civ);
            var reputation = memory.GetReputation(otherCiv);
            var trustLevel = memory.GetTrustLevel(otherCiv);
            
            // Must have good reputation and trust
            if (reputation < 20f || trustLevel < 6) continue;
            
            // Evaluate trait compatibility
            float traitModifier = EvaluateCivilizationTraits(civ, otherCiv);
            if (traitModifier < -10f) continue; // Too incompatible
            
            // Check for shared enemies (bonus for potential allies)
            bool hasSharedEnemy = false;
            foreach (var thirdCiv in allCivs)
            {
                if (thirdCiv == null || thirdCiv == civ || thirdCiv == otherCiv) continue;
                
                var relationToThird = DiplomacyManager.Instance.GetRelationship(civ, thirdCiv);
                var otherRelationToThird = DiplomacyManager.Instance.GetRelationship(otherCiv, thirdCiv);
                
                if (relationToThird == DiplomaticState.War && otherRelationToThird == DiplomaticState.War)
                {
                    hasSharedEnemy = true;
                    break;
                }
            }
            
            // Consider military strength (prefer similar or stronger allies)
            var otherStrength = ComputeMilitaryStrength(otherCiv);
            bool acceptableStrength = otherStrength >= myStrength * 0.7f; // At least 70% of our strength
            
            if (acceptableStrength || hasSharedEnemy)
            {
                potentialAllies.Add(otherCiv);
            }
        }
        
        return potentialAllies;
    }
    
    // Helper methods for the above
    /// <summary>
    /// Check if two civilizations share borders
    /// </summary>
    private bool CheckSharedBorders(Civilization civ1, Civilization civ2)
    {
        if (civ1 == null || civ2 == null || civ1.ownedTileIndices == null || civ2.ownedTileIndices == null) return false;
        if (TileSystem.Instance == null) return false;
        
        var civ2Tiles = new HashSet<int>(civ2.ownedTileIndices);
        
        // Check if any of civ1's tiles are adjacent to civ2's tiles
        foreach (int tileIndex in civ1.ownedTileIndices)
        {
            if (tileIndex < 0) continue;
            
            var neighbors = TileSystem.Instance.GetNeighbors(tileIndex);
            if (neighbors == null) continue;
            
            foreach (int neighborTile in neighbors)
            {
                if (civ2Tiles.Contains(neighborTile))
                {
                    return true; // Found shared border
                }
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Get tiles in range of a center tile (BFS)
    /// </summary>
    private List<int> GetTilesInRange(int centerTile, int range)
    {
        var tilesInRange = new List<int>();
        if (centerTile < 0 || TileSystem.Instance == null) return tilesInRange;
        
        var visited = new HashSet<int>();
        var queue = new Queue<(int tile, int distance)>();
        queue.Enqueue((centerTile, 0));
        visited.Add(centerTile);
        
        while (queue.Count > 0)
        {
            var (currentTile, distance) = queue.Dequeue();
            
            if (distance > 0) // Don't include center tile
            {
                tilesInRange.Add(currentTile);
            }
            
            if (distance >= range) continue; // Reached max range
            
            var neighbors = TileSystem.Instance.GetNeighbors(currentTile);
            if (neighbors == null) continue;
            
            foreach (int neighbor in neighbors)
            {
                if (neighbor < 0 || visited.Contains(neighbor)) continue;
                
                visited.Add(neighbor);
                queue.Enqueue((neighbor, distance + 1));
            }
        }
        
        return tilesInRange;
    }
    
    /// <summary>
    /// Check if a city has any building of the given types
    /// </summary>
    private bool HasBuildingType(City city, List<BuildingData> buildingTypes)
    {
        if (city == null || buildingTypes == null || buildingTypes.Count == 0) return false;
        if (city.builtBuildings == null) return false;
        
        var builtBuildingData = city.builtBuildings.Select(t => t.Item1).ToHashSet();
        
        return buildingTypes.Any(b => builtBuildingData.Contains(b));
    }

    /// <summary>
    /// Spawns the player civ, AI civs, tribes, and city-states after the map is generated.
    /// Instead of cities, each civ starts with a pioneer unit at their start tile.
    /// </summary>
    public void SpawnCivilizations(CivData playerCivData, int aiCount, int cityStateCount, int tribeCount)
    {
        Debug.Log($"[CivilizationManager] SpawnCivilizations called: AI={aiCount}, CityStates={cityStateCount}, Tribes={tribeCount}");
        
        // Clear any existing civs
        civs.Clear();
        currentCivIndex = -1;
        
        // FIXED: Always spawn civilizations on Earth (planet index 0) regardless of current planet
        var planet   = GameManager.Instance?.GetPlanetGenerator(0); // Force Earth
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
        }
        
        // Spawn the player civilization
        SpawnOneCivilization(playerCivData, occupied, isPlayer: true);

        // 2) AI civs
        Shuffle(normalPool);
        for (int i = 0; i < aiCount && i < normalPool.Count; i++)
        {
            SpawnOneCivilization(normalPool[i], occupied, isPlayer: false);
        }

        // 3) City-states
        Shuffle(cityStatePool);
        for (int i = 0; i < cityStateCount && i < cityStatePool.Count; i++)
        {
            SpawnOneCivilization(cityStatePool[i], occupied, isPlayer: false);
        }

        // 4) Tribes
        Shuffle(tribePool);
        for (int i = 0; i < tribeCount && i < tribePool.Count; i++)
        {
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
            
        }
        else
        {
            Debug.LogError("TurnManager.Instance is null! Cannot start turn cycle.");
            // Fallback: just advance turn once
            AdvanceTurn();
        }
        
        // FIXED: Position camera to focus on player's pioneer starting tile
        if (playerCiv != null && playerCiv.workerUnits.Count > 0)
        {
            PositionCameraOnPlayerStart();
        }
        else
        {
            Debug.LogWarning("[CivilizationManager] Cannot position camera: No player pioneer found!");
        }
        
        Debug.Log($"[CivilizationManager] Civilization spawning complete. Total civs spawned: {civs.Count}");
        Debug.Log($"[CivilizationManager] Total worker units spawned: {civs.Sum(c => c.workerUnits.Count)}");
    }

    /// <summary>
    /// Instantiates a Civilization and its starting pioneer.
    /// </summary>
    void SpawnOneCivilization(CivData data, HashSet<int> occupied, bool isPlayer)
    {
        // Check for null data
        if (data == null)
        {
            Debug.LogError("SpawnOneCivilization: CivData is null!");
            return;
        }
        
        // FIXED: Always spawn civilizations on Earth (planet index 0) regardless of current planet
        var planet = GameManager.Instance?.GetPlanetGenerator(0); // Force Earth
        var grid  = planet != null ? planet.Grid : null;
        if (grid == null)
        {
            Debug.LogError("SpawnOneCivilization: Earth's SphericalHexGrid not found!");
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
        
        Debug.Log($"[CivilizationManager] Civilization GameObject created successfully for {data.civName}");
        
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

        Debug.Log($"[CivilizationManager] Creating pioneer for {data.civName} at tile {tile}...");
        
        // Instantiate pioneer (same as animals - no parenting to planet)
        // FIXED: Use Earth-specific positioning for pioneer
    Vector3 pos = TileSystem.Instance != null ? TileSystem.Instance.GetTileSurfacePosition(tile, 0.5f, 0) : Vector3.zero; // Force planet index 0 (Earth)
        Debug.Log($"[CivilizationManager] Pioneer position calculated: {pos}");
        
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
        
    }

    /// <summary>
    /// Finds a random unoccupied land tile, optionally matching climate preferences.
    /// Uses the same approach as AnimalManager for reliable tile finding.
    /// </summary>
    int FindSpawnTile(CivData data, HashSet<int> occupied, bool enforceClimate)
    {
        Debug.Log($"[CivilizationManager] FindSpawnTile for {data?.civName} (enforceClimate: {enforceClimate})");
        
        // COPIED FROM ANIMALMANAGER: Use exact same approach for reliability
        var candidates = new List<int>();
        // FIXED: Always spawn civilizations on Earth (planet index 0) regardless of current planet
        var planet = GameManager.Instance?.GetPlanetGenerator(0); // Force Earth
        int tileCount = planet != null && planet.Grid != null ? planet.Grid.TileCount : 0;

        if (planet == null)
        {
            Debug.LogError("FindSpawnTile: No Earth planet generator found!");
            return -1;
        }
        
        if (planet.Grid == null)
        {
            Debug.LogError("FindSpawnTile: Earth grid is null!");
            return -1;
        }
        
        if (!planet.Grid.IsBuilt)
        {
            Debug.LogError("FindSpawnTile: Earth grid is not built!");
            return -1;
        }
        
        if (!planet.HasGeneratedSurface)
        {
            Debug.LogError($"FindSpawnTile: Earth surface not ready! HasGeneratedSurface = {planet.HasGeneratedSurface}");
            return -1;
        }
        
        
        
        int landTileCount = 0;
        int waterTileCount = 0;
        int invalidTileCount = 0;
        int climateFilteredCount = 0;

    // Copied from AnimalManager: tile checks now use TileSystem
        for (int i = 0; i < tileCount; i++)
        {
            if (occupied.Contains(i)) continue;
            
            // Use same tile data retrieval as AnimalManager
            var tile = TileSystem.Instance != null ? TileSystem.Instance.GetTileData(i) : null;
            if (tile == null) {
                invalidTileCount++;
                continue;
            }
            if (!tile.isLand) {
                waterTileCount++;
                continue;
            }

            landTileCount++;

            // FIXED: Ensure starting units never spawn on water tiles
            if (IsWaterTile(tile.biome)) {
                waterTileCount++; // Count water biomes on land tiles
                continue;
            }

            if (enforceClimate && data.climatePreferences.Length > 0)
            {
                if (!data.climatePreferences.Contains(tile.biome)) {
                    climateFilteredCount++;
                    continue;
                }
            }
            candidates.Add(i);
        }
        
        

        if (candidates.Count == 0) {
            Debug.LogError($"[CivilizationManager] No valid spawn candidates found for {data.civName}!");
            return -1;
        }

        int result = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        Debug.Log($"[CivilizationManager] FindSpawnTile result for {data.civName}: {result}");
        return result;
    }

    /// <summary>
    /// Checks if a biome is a water tile or inhospitable wetland (coast, glacier, river, seas, ocean, lakes, marshes, swamps, floodlands)
    /// </summary>
    private bool IsWaterTile(Biome biome)
    {
        return biome == Biome.Coast ||
               biome == Biome.Glacier ||
               biome == Biome.River ||
               biome == Biome.Seas ||
               biome == Biome.Ocean;
    }

    /// <summary>
    /// Positions the camera to focus on the player's pioneer starting tile at game start
    /// </summary>
    private void PositionCameraOnPlayerStart()
    {
        if (playerCiv == null || playerCiv.workerUnits.Count == 0)
        {
            Debug.LogWarning("Cannot position camera: no player civilization or pioneer found");
            return;
        }

        // Get the player's pioneer (starting unit)
        var pioneer = playerCiv.workerUnits[0];
        if (pioneer == null)
        {
            Debug.LogWarning("Cannot position camera: pioneer is null");
            return;
        }

        // Get the tile index where the pioneer is located
        int pioneerTileIndex = pioneer.currentTileIndex;
        if (pioneerTileIndex < 0)
        {
            Debug.LogWarning("Cannot position camera: pioneer tile index is invalid");
            return;
        }

        // Find the PlanetaryCameraManager in the scene
        var cameraManager = FindAnyObjectByType<PlanetaryCameraManager>();
        if (cameraManager == null)
        {
            Debug.LogWarning("Cannot position camera: PlanetaryCameraManager not found in scene");
            return;
        }

        // Get the planet generator (Earth - index 0)
        var planet = GameManager.Instance?.GetPlanetGenerator(0);
        if (planet == null || planet.Grid == null)
        {
            Debug.LogWarning("Cannot position camera: Earth planet generator not found");
            return;
        }

        // Get the tile position and convert to direction from planet center
        Vector3 tileWorldPosition = TileSystem.Instance != null ? TileSystem.Instance.GetTileSurfacePosition(pioneerTileIndex, 0f, 0) : Vector3.zero;

        // Focus the camera on the tile in flat space
        cameraManager.JumpToWorldPoint(tileWorldPosition);

        Debug.Log($"Camera positioned to focus on player pioneer at tile {pioneerTileIndex}");
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
        // Use GameManager API for multi-planet support
        var planet = GameManager.Instance?.GetCurrentPlanetGenerator();
        var grid = planet != null ? planet.Grid : null;
        civ.Initialize(template, null, false, grid, planet);

        RegisterCiv(civ);
        Debug.Log($"Created new rebel faction '{go.name}' from revolt in {revoltedCity.cityName}");
        return civ;
    }

    void Update()
    {
        // Example: player presses End Turn (Enter key to avoid conflict with Space for space travel)
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
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
