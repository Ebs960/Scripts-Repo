// Assets/Scripts/Civs/CivilizationManager.cs
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using TMPro;
using System.Linq;
using System.Collections.Generic;


public class CivilizationManager : MonoBehaviour
{
    public static CivilizationManager Instance { get; private set; }

    // The new command-based AI planner (plan-then-execute architecture)
    private readonly AIPlanner aiPlanner = new AIPlanner();

    [Header("Prefabs & Data")]
    [Tooltip("Prefab with a Civilization component")]
    public GameObject civilizationPrefab;
    [Tooltip("WorkerUnitData asset describing the global pioneer unit. Civ-specific visuals are resolved through WorkerUnitData prefab overrides first, with Addressables as fallback.")]
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
    
    // Public accessor for the list of civilizations (read-only view, no allocation)
    public IReadOnlyList<Civilization> civilizations => civs;

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
        var all = FindObjectsByType<Civilization>();
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
}

    /// <summary>
    /// Returns a read-only view of all registered civilizations (no allocation).
    /// </summary>
    public IReadOnlyList<Civilization> GetAllCivs() => civs;

    /// <summary>
    /// Returns the registration index of a civilization, or -1 if not found.
    /// </summary>
    public int GetCivIndex(Civilization civ)
    {
        for (int i = 0; i < civs.Count; i++)
            if (civs[i] == civ) return i;
        return -1;
    }

    /// <summary>
    /// Unregisters a civilization from the manager. Safe to call even if the civ
    /// is already destroyed or null; this will remove null entries and clear
    /// player references.
    /// </summary>
    public void UnregisterCiv(Civilization civ)
    {
        // prune null entries first
        civs.RemoveAll(x => x == null);
        if (civ == null) return;
        if (civs.Contains(civ)) civs.Remove(civ);
        if (playerCiv == civ) playerCiv = null;
    }

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
    /// Coroutine for handling the completion of an AI turn with sophisticated decision making.
    /// Uses the command-based AIPlanner for tactical unit decisions (move, attack, forage, hunt,
    /// retreat, settle, build, fortify) and retains the existing high-level strategic methods
    /// (diplomacy, tech, culture, religion, improvement upgrades).
    /// </summary>
    private IEnumerator CompleteAITurn(Civilization civ)
    {
        yield return null;

        // ───── Phase 1: Command-based tactical AI (plan-then-execute) ─────
        // The AIPlanner handles: danger maps, unstoring, army groups,
        // and per-unit decisions (attack, move, forage, hunt, build, settle, retreat, fortify).
        // Split planning and execution across frames to avoid single-frame freeze.
        aiPlanner.PlanTurn(civ);
        yield return null;
        aiPlanner.ExecuteCommands();
        yield return null;

        // ───── Phase 2: High-level strategic decisions (retained) ─────
        // These handle empire-wide choices that don't map to single-unit commands.
        PerformSeasonalDecisions(civ);
        PerformImprovementUpgradeDecisions(civ);
        yield return null;
        PerformStrategicDecisions(civ);
        EvaluateCapitalRelocation(civ);
        PerformDiplomaticDecisions(civ);
        yield return null;
        PerformTechnologicalDecisions(civ);
        PerformCulturalDecisions(civ);
        PerformReligiousDecisions(civ);
    }
    
    /// <summary>
    /// Release units from shelters when it's not winter so they can act (forage, hunt, move).
    /// Called at the start of the AI turn so released units can be used in worker/military decisions.
    /// </summary>
    private void PerformUnstoreDecisions(Civilization civ)
    {
        if (civ == null || ClimateManager.Instance == null) return;
        // Combat units
        if (civ.combatUnits != null)
        {
            foreach (var unit in civ.combatUnits.ToArray())
            {
                if (unit == null || !unit.isStored || unit.storedInImprovement == null) continue;
                if (ClimateManager.Instance.GetSeasonForPlanet(unit.planetIndex) == Season.Winter) continue;
                unit.storedInImprovement.TryUnstoreUnit(unit);
            }
        }
        // Worker units
        if (civ.workerUnits != null)
        {
            foreach (var worker in civ.workerUnits.ToArray())
            {
                if (worker == null || !worker.isStored || worker.storedInImprovement == null) continue;
                if (ClimateManager.Instance.GetSeasonForPlanet(worker.planetIndex) == Season.Winter) continue;
                worker.storedInImprovement.TryUnstoreUnit(worker);
            }
        }
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
        
        // Make decisions based on primary agenda and situation
        ExecuteAgendaStrategy(civ, agenda, situation, false);

        // Secondary agenda adds supplementary strategic behavior at reduced intensity
        if (leader.secondaryAgenda != LeaderAgenda.None && leader.secondaryAgenda != leader.primaryAgenda)
        {
            ExecuteAgendaStrategy(civ, leader.secondaryAgenda, situation, true);
        }
    }

    /// <summary>
    /// Execute strategic behavior for a given agenda.
    /// When isSecondary is true, actions are taken at reduced priority / with lower thresholds.
    /// </summary>
    private void ExecuteAgendaStrategy(Civilization civ, LeaderAgenda agenda, CivilizationSituation situation, bool isSecondary)
    {
        // Secondary agenda has a chance to be skipped each turn (acts less consistently)
        if (isSecondary && UnityEngine.Random.value > 0.6f) return;

        switch (agenda)
        {
            case LeaderAgenda.Militaristic:
                float milThreshold = isSecondary ? 1.5f : 1.2f;
                if (situation.militaryStrength < situation.averageMilitaryStrength * milThreshold)
                    PrioritizeMilitaryProduction(civ);
                else if (!isSecondary)
                    ConsiderWarDeclarations(civ);
                break;
                
            case LeaderAgenda.Expansionist:
                float expandThreshold = isSecondary ? 1.2f : 1.5f;
                if (civ.cities.Count < situation.averageCityCount * expandThreshold)
                    PrioritizeExpansion(civ);
                break;
                
            case LeaderAgenda.Scientific:
                PrioritizeScientificAdvancement(civ);
                break;
                
            case LeaderAgenda.Diplomatic:
                PrioritizeDiplomaticSolutions(civ);
                break;
                
            case LeaderAgenda.Economic:
                PrioritizeEconomicGrowth(civ);
                break;
                
            case LeaderAgenda.Religious:
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
        
        // Calculate averages (manual loops instead of LINQ to avoid allocations)
        float totalMilitary = 0f, totalCities = 0f, totalGold = 0f;
        int civCount = allCivs.Count;
        for (int i = 0; i < civCount; i++)
        {
            totalMilitary += ComputeMilitaryStrength(allCivs[i]);
            totalCities += allCivs[i].cities.Count;
            float gold = 0f;
            foreach (var city in allCivs[i].cities) gold += city.GetGoldPerTurn();
            totalGold += gold;
        }
        situation.averageMilitaryStrength = civCount > 0 ? totalMilitary / civCount : 0f;
        situation.averageCityCount = civCount > 0 ? totalCities / civCount : 0f;
        situation.averageGoldPerTurn = civCount > 0 ? totalGold / civCount : 0f;
        
        // Current civ stats
        situation.militaryStrength = ComputeMilitaryStrength(civ);
        situation.cityCount = civ.cities.Count;
        float civGold = 0f;
        foreach (var city in civ.cities) civGold += city.GetGoldPerTurn();
        situation.goldPerTurn = (int)civGold;
        
        // Threat assessment
        situation.threatsNearby = CountNearbyThreats(civ);
        situation.isAtWar = false;
        foreach (var r in civ.relations.Values)
        {
            if (r == DiplomaticState.War) { situation.isAtWar = true; break; }
        }
        
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
            float diplomaticWeightDelta = otherCiv.GetDiplomaticWeight() - civ.GetDiplomaticWeight();
            
            // Evaluate if this civ has traits we like/dislike
            float traitModifier = EvaluateCivilizationTraits(civ, otherCiv);
            float diplomaticWeightModifier = Mathf.Clamp(diplomaticWeightDelta * 0.12f, -10f, 10f);
            float adjustedReputation = reputation + traitModifier + diplomaticWeightModifier;
            
            // Consider diplomatic actions based on agenda
            bool isDiplomaticLeader = leader.primaryAgenda == LeaderAgenda.Diplomatic 
                                     || leader.secondaryAgenda == LeaderAgenda.Diplomatic;
            bool isMilitaristicLeader = leader.primaryAgenda == LeaderAgenda.Militaristic 
                                       || leader.secondaryAgenda == LeaderAgenda.Militaristic;

            if (isDiplomaticLeader && currentRelation == DiplomaticState.Peace)
            {
                // Lower threshold for secondary-only diplomatic leaders
                float repThreshold = leader.primaryAgenda == LeaderAgenda.Diplomatic ? 20f : 30f;
                float allianceChance = leader.primaryAgenda == LeaderAgenda.Diplomatic ? 0.3f : 0.15f;
                if (adjustedReputation > repThreshold && trustLevel >= 6 && UnityEngine.Random.value < allianceChance)
                {
                    // Propose alliance
                    DiplomacyManager.Instance.ProposeDeal(civ, otherCiv, DealType.Alliance);
                }
            }
            else if (isMilitaristicLeader && currentRelation == DiplomaticState.Peace)
            {
                if (adjustedReputation < -30f
                    && ComputeMilitaryStrength(civ) > ComputeMilitaryStrength(otherCiv) * 1.3f
                    && civ.GetDiplomaticWeight() >= otherCiv.GetDiplomaticWeight() * 0.8f)
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
        int warCount = 0;
        foreach (var r in target.relations.Values) if (r == DiplomaticState.War) warCount++;
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

        float diplomaticWeightDelta = target.GetDiplomaticWeight() - evaluator.GetDiplomaticWeight();
        modifier += Mathf.Clamp(diplomaticWeightDelta * 0.05f, -6f, 6f);
        
        // Check for wealth
        int targetGold = 0;
        foreach (var c in target.cities) if (c != null) targetGold += c.GetGoldPerTurn();
        int evaluatorGold = 0;
        foreach (var c in evaluator.cities) if (c != null) evaluatorGold += c.GetGoldPerTurn();
        if (targetGold > evaluatorGold * 1.5f)
        {
            modifier += leader.GetTraitModifier(CivilizationTrait.Wealthy, true);
        }
        
        return modifier;
    }

    private void EvaluateCapitalRelocation(Civilization civ)
    {
        if (civ == null || civ == playerCiv || civ.cities == null || civ.cities.Count <= 1)
            return;

        civ.EnsureCapitalCity();

        City bestCity = null;
        float bestScore = float.MinValue;

        foreach (var city in civ.cities)
        {
            if (city == null || city.owner != civ) continue;
            float score = city.level * 8f
                + city.productionPerTurn * 1.5f
                + city.defenseRating * 0.2f
                + city.loyalty * 0.75f;
            if (score > bestScore)
            {
                bestScore = score;
                bestCity = city;
            }
        }

        if (bestCity == null)
            return;

        var currentCapital = civ.CapitalCity;
        if (currentCapital == null)
        {
            civ.SetCapitalCity(bestCity);
            return;
        }

        float currentScore = currentCapital.level * 8f
            + currentCapital.productionPerTurn * 1.5f
            + currentCapital.defenseRating * 0.2f
            + currentCapital.loyalty * 0.75f;

        bool currentCapitalWeak = currentCapital.loyalty < 45f;
        if (bestCity != currentCapital && (bestScore >= currentScore + 20f || (currentCapitalWeak && bestCity.loyalty >= currentCapital.loyalty + 15f)))
        {
            civ.SetCapitalCity(bestCity);
        }
    }
    
    /// <summary>
    /// Make military decisions
    /// </summary>
    private void PerformMilitaryDecisions(Civilization civ)
    {
        if (civ == null || civ.combatUnits == null) return;

        foreach (var unit in civ.combatUnits)
        {
            if (unit == null || unit.data == null) continue;
            if (unit.hasActedThisTurn) continue;

            // --- Orbit AI for spaceship units ---
            if (unit.CanEnterOrbit() && !unit.IsInOrbit)
            {
                // Spaceship on surface with nothing to do: enter orbit to collect yields
                bool hasNearbyThreat = HasEnemyNearby(civ, unit, (int)unit.CurrentRange + 2);
                if (!hasNearbyThreat)
                {
                    unit.EnterOrbit(unit.currentTileIndex);
                    unit.ConsumeAction();
                    continue;
                }
            }

            if (unit.IsInOrbit)
            {
                // In orbit: try to bombard a nearby enemy if capable
                if (unit.CanBombardSurface)
                {
                    CombatUnit bestTarget = FindBestBombardTarget(civ, unit);
                    if (bestTarget != null && unit.CanAttack(bestTarget))
                    {
                        if (!TryRouteTacticalEngagement(unit, bestTarget))
                            unit.Attack(bestTarget);
                        continue;
                    }
                }
                // Otherwise stay in orbit (yields collected automatically via BeginTurn)
                continue;
            }

            // --- Basic surface combat AI ---
            // Attack any enemy (or animal, for food) in range
            CombatUnit surfaceTarget = FindBestAttackTargetIncludingAnimals(civ, unit);
            if (surfaceTarget != null && unit.CanAttack(surfaceTarget))
            {
                if (!TryRouteTacticalEngagement(unit, surfaceTarget))
                    unit.Attack(surfaceTarget);
                continue;
            }

            // Not in range: move toward nearest enemy or animal (hunting for food when no cities / low food)
            TryMoveCombatUnitTowardTarget(civ, unit);
        }
    }

    /// <summary>
    /// Find best attack target: enemies in range first, then animals (for food) when civ has no cities or low food.
    /// </summary>
    private CombatUnit FindBestAttackTargetIncludingAnimals(Civilization owner, CombatUnit attacker)
    {
        CombatUnit enemy = FindBestAttackTarget(owner, attacker);
        if (enemy != null) return enemy;

        // Prefer hunting animals when we have no cities or need food (early game)
        bool needFood = owner.cities == null || owner.cities.Count == 0 || owner.food < 10;
        if (!needFood) return null;

        if (AnimalManager.Instance == null) return null;
        var ts = TileSystem.GetForPlanet(attacker.planetIndex) ?? TileSystem.Instance;
        if (ts == null) return null;

        CombatUnit bestAnimal = null;
        float bestScore = float.MinValue;
        foreach (var animal in AnimalManager.Instance.GetActiveAnimals())
        {
            if (animal == null || animal.data == null || animal.data.unitType != CombatCategory.Animal) continue;
            if (animal.planetIndex != attacker.planetIndex) continue;
            if (animal.currentTileIndex < 0) continue;
            if (!attacker.CanAttack(animal)) continue;
            int dist = ts.GetTileDistance(attacker.currentTileIndex, animal.currentTileIndex);
            if (dist > 1) continue; // only in-range (adjacent) for melee
            float score = (animal.data != null && animal.data.foodOnKill > 0) ? animal.data.foodOnKill : 1f;
            if (score > bestScore) { bestScore = score; bestAnimal = animal; }
        }
        return bestAnimal;
    }

    /// <summary>
    /// Move a combat unit one turn toward the nearest enemy or animal (for hunting). Does nothing if already acted or no target.
    /// </summary>
    private void TryMoveCombatUnitTowardTarget(Civilization civ, CombatUnit unit)
    {
        if (unit == null || unit.hasActedThisTurn) return;
        var ts = TileSystem.GetForPlanet(unit.planetIndex) ?? TileSystem.Instance;
        if (ts == null) return;

        CombatUnit target = FindNearestTargetToApproach(civ, unit);
        if (target == null || target.currentTileIndex < 0) return;

        int[] neighborIndices = ts.GetNeighbors(target.currentTileIndex);
        if (neighborIndices == null || neighborIndices.Length == 0) return;

        List<int> bestPath = null;
        int bestApproachTile = -1;
        foreach (int neighbor in neighborIndices)
        {
            if (!unit.CanMoveTo(neighbor)) continue;
            var path = UnitMovementController.Instance != null ? UnitMovementController.Instance.FindPath(unit.currentTileIndex, neighbor, unit) : null;
            if (path == null || path.Count == 0) continue;
            if (bestPath == null || path.Count < bestPath.Count)
            {
                bestPath = path;
                bestApproachTile = neighbor;
            }
        }
        if (bestApproachTile >= 0 && unit.CanMoveTo(bestApproachTile))
            unit.MoveTo(bestApproachTile);
    }

    /// <summary>
    /// Move a combat unit one step toward a specific tile index using pathfinding.
    /// </summary>
    private void TryMoveCombatUnitTowardTile(CombatUnit unit, int targetTileIndex, TileSystem ts)
    {
        if (unit == null || unit.hasActedThisTurn || targetTileIndex < 0) return;
        if (UnitMovementController.Instance == null) return;

        var path = UnitMovementController.Instance.FindPath(unit.currentTileIndex, targetTileIndex, unit);
        if (path == null || path.Count == 0) return;

        // Move to the first step on the path
        int nextTile = path[0];
        if (unit.CanMoveTo(nextTile))
            unit.MoveTo(nextTile);
    }

    /// <summary>
    /// Nearest enemy or animal (for hunting) to approach this turn. Prefers animals when civ has no cities or low food.
    /// </summary>
    private CombatUnit FindNearestTargetToApproach(Civilization civ, CombatUnit unit)
    {
        var ts = TileSystem.GetForPlanet(unit.planetIndex) ?? TileSystem.Instance;
        if (ts == null) return null;

        bool needFood = civ.cities == null || civ.cities.Count == 0 || civ.food < 15;
        int myTile = unit.currentTileIndex;
        if (myTile < 0) return null;

        CombatUnit nearest = null;
        int nearestDist = int.MaxValue;

        // Enemies
        foreach (var otherCiv in GetAllCivs())
        {
            if (otherCiv == civ || otherCiv.combatUnits == null) continue;
            foreach (var enemy in otherCiv.combatUnits)
            {
                if (enemy == null || enemy.currentTileIndex < 0 || enemy.planetIndex != unit.planetIndex) continue;
                int d = ts.GetTileDistance(myTile, enemy.currentTileIndex);
                if (d < nearestDist && d > 1) { nearestDist = d; nearest = enemy; }
            }
        }

        // Animals (for food) when no cities or low food
        if (needFood && AnimalManager.Instance != null)
        {
            foreach (var animal in AnimalManager.Instance.GetActiveAnimals())
            {
                if (animal == null || animal.data == null || animal.data.unitType != CombatCategory.Animal) continue;
                if (animal.planetIndex != unit.planetIndex || animal.currentTileIndex < 0) continue;
                int d = ts.GetTileDistance(myTile, animal.currentTileIndex);
                if (d > 1 && d < nearestDist) { nearestDist = d; nearest = animal; }
            }
        }

        return nearest;
    }

    /// <summary>
    /// Check if there is an enemy combat unit within a given tile range of a unit.
    /// </summary>
    private bool HasEnemyNearby(Civilization owner, CombatUnit unit, int range)
    {
        var allCivs = GetAllCivs();
        foreach (var otherCiv in allCivs)
        {
            if (otherCiv == owner) continue;
            if (otherCiv.combatUnits == null) continue;
            foreach (var enemy in otherCiv.combatUnits)
            {
                if (enemy == null) continue;
                try
                {
                    var ts = TileSystem.GetForPlanet(unit.planetIndex) ?? TileSystem.Instance;
                    if (ts != null && unit.currentTileIndex >= 0 && enemy.currentTileIndex >= 0)
                    {
                        int steps = ts.GetWrappedHexDistance(unit.currentTileIndex, enemy.currentTileIndex);
                        if (steps >= 0 && steps <= range) return true;
                    }
                }
                catch { }
            }
        }
        return false;
    }

    /// <summary>
    /// Find the best enemy target in range for bombardment from orbit.
    /// </summary>
    private CombatUnit FindBestBombardTarget(Civilization owner, CombatUnit attacker)
    {
        CombatUnit best = null;
        float bestScore = float.MinValue;

        var allCivs = GetAllCivs();
        foreach (var otherCiv in allCivs)
        {
            if (otherCiv == owner) continue;
            if (otherCiv.combatUnits == null) continue;
            foreach (var enemy in otherCiv.combatUnits)
            {
                if (enemy == null || enemy.IsInOrbit) continue; // only bombard surface units
                if (!attacker.CanAttack(enemy)) continue;

                // Prefer low-health targets
                float score = (float)(enemy.MaxHealth - enemy.currentHealth) + enemy.CurrentAttack;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = enemy;
                }
            }
        }
        return best;
    }

    /// <summary>
    /// Find the best enemy target in attack range for a surface unit.
    /// </summary>
    private CombatUnit FindBestAttackTarget(Civilization owner, CombatUnit attacker)
    {
        CombatUnit best = null;
        float bestScore = float.MinValue;

        var allCivs = GetAllCivs();
        foreach (var otherCiv in allCivs)
        {
            if (otherCiv == owner) continue;
            if (otherCiv.combatUnits == null) continue;
            foreach (var enemy in otherCiv.combatUnits)
            {
                if (enemy == null) continue;
                if (!attacker.CanAttack(enemy)) continue;

                // Prefer low-health, high-value targets
                float score = (float)(enemy.MaxHealth - enemy.currentHealth) + enemy.CurrentAttack * 0.5f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = enemy;
                }
            }
        }
        return best;
    }
    
    /// <summary>
    /// Plan for seasons: build a shelter improvement before winter to avoid attrition.
    /// When turns until winter is at or below one season length, prioritize starting a shelter build.
    /// </summary>
    private void PerformSeasonalDecisions(Civilization civ)
    {
        if (ClimateManager.Instance == null || !ClimateManager.Instance.enableWinterAttrition) return;
        if (civ == null || civ.workerUnits == null) return;
        int turnsPerSeason = ClimateManager.Instance.turnsPerSeason;
        int winterThreshold = Mathf.Max(1, turnsPerSeason + 1); // build before winter when we're within ~1 season of it

        foreach (var worker in civ.workerUnits)
        {
            if (worker == null || worker.data == null || worker.currentWorkPoints <= 0) continue;
            if (worker.currentTileIndex < 0) continue;
            int pIndex = worker.planetIndex;
            int turnsUntilWinter = ClimateManager.Instance.GetTurnsUntilWinter(pIndex);
            if (turnsUntilWinter > winterThreshold) continue; // winter not soon on this planet

            var available = civ.GetAvailableImprovementsForWorker(worker.data, worker.currentTileIndex, pIndex);
            if (available == null) continue;
            ImprovementData shelterToBuild = null;
            foreach (var imp in available)
            {
                if (imp != null && imp.isShelter)
                {
                    shelterToBuild = imp;
                    break;
                }
            }
            if (shelterToBuild == null) continue;
            if (ImprovementManager.Instance != null && ImprovementManager.Instance.HasBuildJobAtTile(worker.currentTileIndex, pIndex)) continue;
            worker.StartBuilding(shelterToBuild, worker.currentTileIndex);
            break; // one shelter per turn
        }
    }

    /// <summary>
    /// AI: apply one affordable improvement upgrade per turn (shelter capacity, defense, yields).
    /// </summary>
    private void PerformImprovementUpgradeDecisions(Civilization civ)
    {
        if (civ == null || civ.ownedTilesByPlanet == null || ImprovementManager.Instance == null) return;
        foreach (var kv in civ.ownedTilesByPlanet)
        {
            int planetIndex = kv.Key;
            var tileSet = kv.Value;
            if (tileSet == null) continue;
            var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
            if (ts == null) continue;
            foreach (int tileIndex in tileSet)
            {
                var td = ts.GetTileData(tileIndex);
                if (td?.improvement == null || td.improvementOwner != civ) continue;
                if (td.improvement.availableUpgrades == null) continue;
                foreach (var upgrade in td.improvement.availableUpgrades)
                {
                    if (upgrade == null) continue;
                    string key = !string.IsNullOrEmpty(upgrade.upgradeId) ? upgrade.upgradeId : upgrade.upgradeName;
                    if (td.builtUpgrades != null && td.builtUpgrades.Contains(key)) continue;
                    if (!upgrade.CanBuild(civ)) continue;
                    if (!upgrade.ConsumeRequirements(civ)) continue;
                    if (ImprovementManager.Instance.ApplyUpgradeToTile(tileIndex, planetIndex, upgrade))
                    {
                        return; // one upgrade per turn
                    }
                }
            }
        }
    }

    /// <summary>
    /// Make economic decisions
    /// </summary>
    private void PerformEconomicDecisions(Civilization civ)
    {
        // Workers also forage when we have cities but food is low (no-city case handled in early-game block above)
        if (civ.workerUnits != null && civ.workerUnits.Count > 0 && civ.food < 20 && civ.cities != null && civ.cities.Count > 0)
            PerformWorkerDecisions(civ);
    }

    /// <summary>
    /// Workers forage on current tile if possible, or move toward nearest forageable resource (food priority, especially with no cities).
    /// </summary>
    private void PerformWorkerDecisions(Civilization civ)
    {
        if (civ == null || civ.workerUnits == null) return;
        var rm = ResourceManager.Instance;
        if (rm == null) return;
        var ts = TileSystem.GetForPlanet(GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0);
        if (ts == null) ts = TileSystem.Instance;
        if (ts == null || !ts.IsReady()) return;

        bool needFood = civ.cities == null || civ.cities.Count == 0 || civ.food < 20;

        foreach (var worker in civ.workerUnits)
        {
            if (worker == null || worker.data == null) continue;
            if (worker.currentTileIndex < 0) continue;
            int pIndex = worker.planetIndex;
            var tsPlanet = TileSystem.GetForPlanet(pIndex) ?? ts;
            bool didSomething = false;

            // 1) Forage on current tile if possible (uses work points)
            if (worker.currentWorkPoints > 0)
            {
                var inst = rm.GetResourceInstanceAtTile(worker.currentTileIndex, pIndex);
                if (inst != null && inst.data != null && inst.data.canBeForaged && worker.CanForage(inst.data, worker.currentTileIndex))
                {
                    worker.Forage(inst.data, worker.currentTileIndex);
                    rm.ForageResource(inst, civ);
                    didSomething = true;
                }
            }

            // 2) Workers can attack: hunt animal in range for food (early game / when hungry)
            if (!didSomething && needFood)
            {
                BaseUnit animalTarget = FindBestAnimalTargetInRangeForUnit(worker, civ);
                if (animalTarget != null && worker.CanAttack(animalTarget))
                {
                    worker.Attack(animalTarget);
                    didSomething = true;
                }
            }

            if (didSomething) continue;

            // 3) Move toward nearest forageable resource (prefer food) within range
            if (worker.currentWorkPoints > 0)
            {
                int nearestForageTile = FindNearestForageableTile(worker, pIndex, 5, rm, tsPlanet);
                if (nearestForageTile >= 0 && nearestForageTile != worker.currentTileIndex && worker.CanMoveTo(nearestForageTile))
                {
                    var path = UnitMovementController.Instance != null ? UnitMovementController.Instance.FindPath(worker.currentTileIndex, nearestForageTile, worker) : null;
                    if (path != null && path.Count > 0)
                    {
                        worker.MoveTo(nearestForageTile);
                        continue;
                    }
                }
            }

            // 4) Move toward nearest animal to hunt (workers need food too; early game only workers exist)
            if (needFood)
                TryMoveWorkerTowardAnimal(civ, worker);
        }
    }

    /// <summary>
    /// Find an animal in attack range that this unit can attack (for food). Works for both combat units and workers.
    /// </summary>
    private BaseUnit FindBestAnimalTargetInRangeForUnit(BaseUnit unit, Civilization civ)
    {
        if (AnimalManager.Instance == null || civ == null) return null;
        var ts = TileSystem.GetForPlanet(unit.planetIndex) ?? TileSystem.Instance;
        if (ts == null) return null;

        CombatUnit best = null;
        int bestFood = -1;
        foreach (var animal in AnimalManager.Instance.GetActiveAnimals())
        {
            if (animal == null || animal.data == null || animal.data.unitType != CombatCategory.Animal) continue;
            if (animal.planetIndex != unit.planetIndex || animal.currentTileIndex < 0) continue;
            if (unit is CombatUnit cu && !cu.CanAttack(animal)) continue;
            if (unit is WorkerUnit wu && !wu.CanAttack(animal)) continue;
            int dist = ts.GetTileDistance(unit.currentTileIndex, animal.currentTileIndex);
            if (dist > 1) continue; // melee range
            int food = animal.data != null ? animal.data.foodOnKill : 0;
            if (food > bestFood) { bestFood = food; best = animal; }
        }
        return best;
    }

    private bool TryRouteTacticalEngagement(CombatUnit attacker, CombatUnit defender)
    {
        if (attacker == null || defender == null)
            return false;

        var mode = EngagementModeResolver.ResolveEngagementMode(attacker, defender);
        if (mode != EngagementMode.TacticalBattle)
            return false;

        var manager = BattleManager.GetOrCreate();
        var preview = manager.RequestEngagement(attacker, defender);
        if (preview == null || !preview.IsValid)
            return false;

        return true;
    }

    /// <summary>
    /// Move a worker toward the nearest animal so they can hunt for food next turn.
    /// </summary>
    private void TryMoveWorkerTowardAnimal(Civilization civ, WorkerUnit worker)
    {
        if (worker == null || worker.currentTileIndex < 0) return;
        var ts = TileSystem.GetForPlanet(worker.planetIndex) ?? TileSystem.Instance;
        if (ts == null) return;

        CombatUnit target = FindNearestAnimalToApproach(worker.planetIndex, worker.currentTileIndex, ts);
        if (target == null) return;

        int[] neighborIndices = ts.GetNeighbors(target.currentTileIndex);
        if (neighborIndices == null || neighborIndices.Length == 0) return;

        int bestApproachTile = -1;
        int bestPathLength = int.MaxValue;
        foreach (int neighbor in neighborIndices)
        {
            if (!worker.CanMoveTo(neighbor)) continue;
            var path = UnitMovementController.Instance != null ? UnitMovementController.Instance.FindPath(worker.currentTileIndex, neighbor, worker) : null;
            if (path == null || path.Count == 0) continue;
            if (path.Count < bestPathLength) { bestPathLength = path.Count; bestApproachTile = neighbor; }
        }
        if (bestApproachTile >= 0 && worker.CanMoveTo(bestApproachTile))
            worker.MoveTo(bestApproachTile);
    }

    /// <summary>
    /// Nearest animal on the given planet to the given tile (for approach movement). Excludes already-adjacent.
    /// </summary>
    private CombatUnit FindNearestAnimalToApproach(int planetIndex, int fromTile, TileSystem ts)
    {
        if (AnimalManager.Instance == null || ts == null) return null;
        CombatUnit nearest = null;
        int nearestDist = int.MaxValue;
        foreach (var animal in AnimalManager.Instance.GetActiveAnimals())
        {
            if (animal == null || animal.data == null || animal.data.unitType != CombatCategory.Animal) continue;
            if (animal.planetIndex != planetIndex || animal.currentTileIndex < 0) continue;
            int d = ts.GetTileDistance(fromTile, animal.currentTileIndex);
            if (d > 1 && d < nearestDist) { nearestDist = d; nearest = animal; }
        }
        return nearest;
    }

    /// <summary>
    /// Find nearest tile index that has a forageable resource (prefer food) within maxRange. Returns -1 if none.
    /// </summary>
    private int FindNearestForageableTile(WorkerUnit worker, int planetIndex, int maxRange, ResourceManager rm, TileSystem ts)
    {
        if (rm == null || ts == null || worker == null) return -1;
        int start = worker.currentTileIndex;
        if (start < 0) return -1;

        var visited = new HashSet<int>();
        var queue = new Queue<(int tile, int dist)>();
        queue.Enqueue((start, 0));
        visited.Add(start);
        int bestTile = -1;
        int bestFood = -1;
        int bestDist = int.MaxValue;

        while (queue.Count > 0)
        {
            var (tile, dist) = queue.Dequeue();
            if (dist > maxRange) continue;

            var inst = rm.GetResourceInstanceAtTile(tile, planetIndex);
            if (inst != null && inst.data != null && inst.data.canBeForaged)
            {
                int foodVal = inst.data.forageFood;
                if (foodVal > bestFood || (foodVal == bestFood && dist < bestDist))
                {
                    bestFood = foodVal;
                    bestDist = dist;
                    bestTile = tile;
                }
            }

            if (dist >= maxRange) continue;
            int[] neighbors = ts.GetNeighbors(tile);
            if (neighbors == null) continue;
            foreach (int n in neighbors)
            {
                if (visited.Contains(n)) continue;
                visited.Add(n);
                queue.Enqueue((n, dist + 1));
            }
        }

        return bestTile;
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
            
        if (tech.goldModifier > 0 || tech.productionModifier > 0)
            score += leader.GetFocusPriority(FocusArea.Economic) * 5f;
            
        if (tech.scienceModifier > 0)
            score += leader.GetFocusPriority(FocusArea.Scientific) * 5f;
            
        if (tech.cultureModifier > 0)
            score += leader.GetFocusPriority(FocusArea.Cultural) * 5f;
            
        if (tech.faithModifier > 0 || tech.unlocksReligion)
            score += leader.GetFocusPriority(FocusArea.Religious) * 5f;
        
        // Agenda-specific bonuses (primary)
        switch (leader.primaryAgenda)
        {
            case LeaderAgenda.Militaristic:
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

        // Secondary agenda adds a smaller bonus (×1.2 instead of ×1.5)
        if (leader.secondaryAgenda != LeaderAgenda.None && leader.secondaryAgenda != leader.primaryAgenda)
        {
            switch (leader.secondaryAgenda)
            {
                case LeaderAgenda.Militaristic:
                    if (tech.attackBonus > 0 || tech.defenseBonus > 0)
                        score *= 1.2f;
                    break;
                case LeaderAgenda.Scientific:
                    if (tech.scienceModifier > 0)
                        score *= 1.2f;
                    break;
                case LeaderAgenda.Religious:
                    if (tech.unlocksReligion || tech.faithModifier > 0)
                        score *= 1.2f;
                    break;
                case LeaderAgenda.Economic:
                    if (tech.goldModifier > 0 || tech.productionModifier > 0)
                        score *= 1.2f;
                    break;
            }
        }
        
        return score;
    }
    
    /// <summary>
    /// Make cultural decisions
    /// </summary>
    private void PerformCulturalDecisions(Civilization civ)
    {
        if (civ.currentCulture != null || CultureManager.Instance == null) return;

        var availableCultures = CultureManager.Instance.GetAvailableCultures(civ);
        if (availableCultures.Count == 0) return;

        var scoredCultures = new List<(CultureData culture, float score)>();
        foreach (var culture in availableCultures)
        {
            float score = CalculateCultureScore(civ, culture);
            scoredCultures.Add((culture, score));
        }

        var bestCulture = scoredCultures.OrderByDescending(c => c.score).First().culture;
        CultureManager.Instance.StartCulture(civ, bestCulture);
    }

    private float CalculateCultureScore(Civilization civ, CultureData culture)
    {
        float score = 1f;
        var leader = civ.leader;

        score += 10f;

        if (culture.goldModifier > 0 || culture.productionModifier > 0)
            score += leader.GetFocusPriority(FocusArea.Economic) * 5f;

        if (culture.scienceModifier > 0)
            score += leader.GetFocusPriority(FocusArea.Scientific) * 5f;

        if (culture.cultureModifier > 0)
            score += leader.GetFocusPriority(FocusArea.Cultural) * 5f;

        if (culture.faithModifier > 0 || culture.unlocksReligion)
            score += leader.GetFocusPriority(FocusArea.Religious) * 5f;

        switch (leader.primaryAgenda)
        {
            case LeaderAgenda.Militaristic:
                if (culture.attackBonus > 0 || culture.defenseBonus > 0)
                    score *= 1.5f;
                break;
            case LeaderAgenda.Scientific:
                if (culture.scienceModifier > 0)
                    score *= 1.5f;
                break;
            case LeaderAgenda.Religious:
                if (culture.unlocksReligion || culture.faithModifier > 0)
                    score *= 1.5f;
                break;
        }

        // Secondary agenda adds a smaller bonus (×1.2)
        if (leader.secondaryAgenda != LeaderAgenda.None && leader.secondaryAgenda != leader.primaryAgenda)
        {
            switch (leader.secondaryAgenda)
            {
                case LeaderAgenda.Militaristic:
                    if (culture.attackBonus > 0 || culture.defenseBonus > 0)
                        score *= 1.2f;
                    break;
                case LeaderAgenda.Scientific:
                    if (culture.scienceModifier > 0)
                        score *= 1.2f;
                    break;
                case LeaderAgenda.Religious:
                    if (culture.unlocksReligion || culture.faithModifier > 0)
                        score *= 1.2f;
                    break;
                case LeaderAgenda.Cultural:
                    if (culture.cultureModifier > 0)
                        score *= 1.2f;
                    break;
                case LeaderAgenda.Economic:
                    if (culture.goldModifier > 0 || culture.productionModifier > 0)
                        score *= 1.2f;
                    break;
            }
        }

        return score;
    }
    
    /// <summary>
    /// Make religious decisions: pantheon founding, belief selection, religion founding,
    /// pantheon upgrading, missionary purchasing, and missionary movement.
    /// </summary>
    private void PerformReligiousDecisions(Civilization civ)
    {
        if (civ == null) return;

        var leader = civ.leader;
        float religionWeight = leader != null ? leader.religiousFocus : 1f;

        // ── 1. Found a Pantheon if we have none (or can found more) ──
        if (civ.CanFoundMorePantheons() && ReligionManager.Instance != null)
        {
            var availablePantheons = ReligionManager.Instance.GetAvailablePantheons();
            if (availablePantheons != null && availablePantheons.Count > 0)
            {
                TryChooseBestPantheonAndBelief(civ, availablePantheons, out var bestPantheon, out var bestBelief);
                if (bestPantheon != null)
                {
                    civ.FoundPantheon(bestPantheon);
                }
            }
        }

        // ── 2. Upgrade Spirits to Gods when possible ──
        if (civ.foundedPantheons != null)
        {
            // Iterate a copy because UpgradePantheon mutates the list
            foreach (var pantheon in civ.foundedPantheons.ToArray())
            {
                if (pantheon == null) continue;
                if (pantheon.IsSpirit && pantheon.canUpgradeToGod && pantheon.upgradedPantheon != null)
                {
                    civ.UpgradePantheon(pantheon);
                }
            }
        }

        // ── 3. Found a Religion if we have a pantheon but no religion ──
        if (civ.foundedPantheons != null && civ.foundedPantheons.Count > 0 && !civ.hasFoundedReligion)
        {
            if (ReligionManager.Instance != null)
            {
                var availableReligions = ReligionManager.Instance.GetAvailableReligions(civ);
                if (availableReligions != null && availableReligions.Count > 0)
                {
                    // Find best city with a Holy Site
                    City bestHolySiteCity = null;
                    foreach (var city in civ.cities)
                    {
                        if (city == null || !city.HasHolySite()) continue;
                        bestHolySiteCity = city;
                        break;
                    }

                    if (bestHolySiteCity != null)
                    {
                        var bestReligion = ChooseBestReligion(civ, availableReligions);
                        if (bestReligion != null)
                            civ.FoundReligion(bestReligion, bestHolySiteCity);
                    }
                }
            }
        }

        if (civ.hasFoundedReligion || !civ.CanFoundMorePantheons())
            AssignStrategicCustomBeliefs(civ);

        // ── 4. Purchase Missionaries / Apostles when faith is high enough ──
        if (civ.hasFoundedReligion && civ.foundedReligion != null)
        {
            // Find available religious unit data from the combat unit pool
            var allCombatUnits = ResourceCache.GetAllCombatUnits();
            ReligionUnitData bestMissionaryData = null;
            float bestMissionaryScore = float.MinValue;

            if (allCombatUnits != null)
            {
                foreach (var unitData in allCombatUnits)
                {
                    if (unitData == null) continue;
                    var relData = unitData as ReligionUnitData;
                    if (relData == null) continue;
                    if (civ.faith < relData.faithCost) continue;
                    if (!relData.AreRequirementsMet(civ)) continue;

                    // Prefer apostles over missionaries (more powerful)
                    float score = relData.spreadPressureAmount + relData.spreadCharges * 10f;
                    if (relData.isApostle) score += 50f;
                    if (score > bestMissionaryScore) { bestMissionaryScore = score; bestMissionaryData = relData; }
                }
            }

            if (bestMissionaryData != null)
            {
                // Count how many missionaries we already have
                int existingMissionaries = 0;
                if (civ.combatUnits != null)
                {
                    foreach (var u in civ.combatUnits)
                    {
                        if (u != null && u.data is ReligionUnitData) existingMissionaries++;
                    }
                }

                // Cap missionaries based on religion focus: low-focus leaders buy fewer
                int maxMissionaries = Mathf.Max(1, Mathf.RoundToInt(religionWeight * 2f));
                if (existingMissionaries < maxMissionaries)
                {
                    // Find a city with a Holy Site to purchase from
                    foreach (var city in civ.cities)
                    {
                        if (city == null || !city.HasHolySite()) continue;
                        if (civ.PurchaseMissionary(bestMissionaryData, city))
                            break; // One purchase per turn
                    }
                }
            }
        }

        // ── 5. Direct Missionaries toward unconverted cities ──
        if (civ.combatUnits != null && civ.hasFoundedReligion && civ.foundedReligion != null)
        {
            foreach (var unit in civ.combatUnits)
            {
                if (unit == null || unit.hasActedThisTurn) continue;
                var relData = unit.data as ReligionUnitData;
                if (relData == null) continue;

                // Find nearest city (ours or foreign) where our religion is NOT the majority
                City targetCity = FindBestMissionaryTarget(civ, unit);
                if (targetCity == null) continue;

                var ts = TileSystem.GetForPlanet(unit.planetIndex) ?? TileSystem.Instance;
                if (ts == null) continue;

                int dist = ts.GetTileDistance(unit.currentTileIndex, targetCity.centerTileIndex);
                if (dist <= relData.spreadRange)
                {
                    // Close enough: spread religion (add pressure to target city tiles)
                    if (ReligionManager.Instance != null)
                    {
                        ts.AddReligionPressure(targetCity.centerTileIndex, civ.foundedReligion, relData.spreadPressureAmount);
                    }
                    unit.ConsumeAction();
                }
                else
                {
                    // Move toward the target city
                    TryMoveCombatUnitTowardTile(unit, targetCity.centerTileIndex, ts);
                }
            }
        }

        // ── 6. Queue religious buildings (secondary priority for non-Religious leaders) ──
        if (civ.cities != null && civ.cities.Count > 0)
        {
            var allBuildings = ResourceCache.GetAllBuildings();
            if (allBuildings != null && allBuildings.Length > 0)
            {
                var religiousBuildings = new List<BuildingData>();
                foreach (var b in allBuildings)
                {
                    if (b != null && b.AreRequirementsMet(civ) && b.faithPerTurn > 0)
                        religiousBuildings.Add(b);
                }

                if (religiousBuildings.Count > 0)
                {
                    religiousBuildings.Sort((a, b) => b.faithPerTurn.CompareTo(a.faithPerTurn));

                    // Religious-focused leaders queue faith buildings in more cities
                    int maxCities = religionWeight >= 1.5f ? 3 : (religionWeight >= 1f ? 2 : 1);
                    int queued = 0;

                    foreach (var city in civ.cities)
                    {
                        if (queued >= maxCities) break;
                        if (city == null || city.GetProductionPerTurn() <= 0) continue;
                        if (city.productionQueue != null && city.productionQueue.Count > 0) continue;
                        if (HasBuildingType(city, religiousBuildings)) continue;

                        var bestBuilding = religiousBuildings[0];
                        if (city.QueueProduction(bestBuilding)) queued++;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Score a pantheon + belief combination for AI selection.
    /// Weighs stat bonuses by leader focus priorities.
    /// </summary>
    private float ScorePantheonAndBelief(Civilization civ, PantheonData pantheon, BeliefData belief)
    {
        float score = 1f;
        var leader = civ.leader;
        if (leader == null) return score;

        // Score from belief modifiers aligned with leader priorities
        score += belief.foodModifier * leader.GetFocusPriority(FocusArea.Economic) * 5f;
        score += belief.productionModifier * leader.GetFocusPriority(FocusArea.Economic) * 5f;
        score += belief.goldPerCity * leader.GetFocusPriority(FocusArea.Economic) * 3f;
        score += belief.scienceModifier * leader.GetFocusPriority(FocusArea.Scientific) * 5f;
        score += belief.culturePerCity * leader.GetFocusPriority(FocusArea.Cultural) * 3f;
        score += belief.cultureModifier * leader.GetFocusPriority(FocusArea.Cultural) * 5f;
        score += belief.faithModifier * leader.GetFocusPriority(FocusArea.Religious) * 5f;
        score += belief.extraFaithInHolySite * leader.GetFocusPriority(FocusArea.Religious) * 4f;

        // Score from pantheon bonuses (units, buildings, stat modifiers)
        if (pantheon.bonuses != null)
        {
            var b = pantheon.bonuses;
            score += b.attackBonus * leader.GetFocusPriority(FocusArea.Military) * 4f;
            score += b.defenseBonus * leader.GetFocusPriority(FocusArea.Military) * 4f;
            score += b.foodModifier * leader.GetFocusPriority(FocusArea.Economic) * 4f;
            score += b.productionModifier * leader.GetFocusPriority(FocusArea.Economic) * 4f;
            score += b.goldModifier * leader.GetFocusPriority(FocusArea.Economic) * 4f;
            score += b.scienceModifier * leader.GetFocusPriority(FocusArea.Scientific) * 4f;
            score += b.cultureModifier * leader.GetFocusPriority(FocusArea.Cultural) * 4f;
            score += b.faithModifier * leader.GetFocusPriority(FocusArea.Religious) * 4f;

            // Bonus for unlocked content
            if (b.unlockedCombatUnits != null) score += b.unlockedCombatUnits.Length * leader.GetFocusPriority(FocusArea.Military) * 3f;
            if (b.unlockedWorkerUnits != null) score += b.unlockedWorkerUnits.Length * leader.GetFocusPriority(FocusArea.Economic) * 2f;
            if (b.unlockedBuildings != null) score += b.unlockedBuildings.Length * 2f;
        }

        // Gods are generally more valuable than Spirits
        if (pantheon.IsGod) score *= 1.3f;

        // Religious leaders value all pantheons more
        if (leader.primaryAgenda == LeaderAgenda.Religious || leader.secondaryAgenda == LeaderAgenda.Religious)
            score *= 1.4f;

        if (!civ.HasActiveBeliefInCategory(belief.category))
            score += 8f;

        return score;
    }

    private float ScoreBeliefForCivilization(Civilization civ, BeliefData belief)
    {
        if (civ == null || belief == null) return float.MinValue;

        var leader = civ.leader;
        if (leader == null) return 0f;

        float economic = leader.GetFocusPriority(FocusArea.Economic);
        float scientific = leader.GetFocusPriority(FocusArea.Scientific);
        float cultural = leader.GetFocusPriority(FocusArea.Cultural);
        float religious = leader.GetFocusPriority(FocusArea.Religious);
        float military = leader.GetFocusPriority(FocusArea.Military);

        float score = 0f;
        score += belief.foodModifier * economic * 5f;
        score += belief.productionModifier * economic * 4f;
        score += belief.goldModifier * economic * 4f;
        score += belief.goldPerCity * economic * Mathf.Max(1, civ.cities != null ? civ.cities.Count : 1);
        score += belief.extraFoodInHolySite * economic * 2f;
        score += belief.extraProductionInHolySite * economic * 2f;
        score += belief.growthRateModifier * economic * 6f;
        score += belief.scienceModifier * scientific * 5f;
        score += belief.cultureModifier * cultural * 5f;
        score += belief.culturePerCity * cultural * Mathf.Max(1, civ.cities != null ? civ.cities.Count : 1);
        score += belief.faithModifier * religious * 6f;
        score += belief.extraFaithInHolySite * religious * 4f;
        score += belief.happinessBonus * cultural * 3f;
        score += belief.combatStrengthNearHolySite * military * 5f;

        if (!civ.HasActiveBeliefInCategory(belief.category))
            score += 10f;

        return score;
    }

    private void TryChooseBestPantheonAndBelief(Civilization civ, List<PantheonData> availablePantheons, out PantheonData bestPantheon, out BeliefData bestBelief)
    {
        bestPantheon = null;
        bestBelief = null;
        float bestScore = float.MinValue;

        if (availablePantheons == null) return;
        var allBeliefs = ResourceCache.GetAllBeliefData();
        if (allBeliefs == null || allBeliefs.Length == 0) return;

        foreach (var pantheon in availablePantheons)
        {
            if (pantheon == null || civ.faith < pantheon.faithCost) continue;

            foreach (var belief in allBeliefs)
            {
                if (belief == null) continue;
                if (!civ.CanUseBeliefForPantheon(pantheon, belief)) continue;
                if (civ.HasActiveBeliefInCategory(belief.category)) continue;

                float score = ScorePantheonAndBelief(civ, pantheon, belief);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestPantheon = pantheon;
                    bestBelief = belief;
                }
            }
        }
    }

    private ReligionData ChooseBestReligion(Civilization civ, List<ReligionData> availableReligions)
    {
        ReligionData bestReligion = null;
        float bestScore = float.MinValue;

        if (availableReligions == null) return null;

        foreach (var religion in availableReligions)
        {
            if (religion == null) continue;
            if (civ.faith < religion.faithCost) continue;
            if (religion.requiredPantheon != null && (civ.foundedPantheons == null || !civ.foundedPantheons.Contains(religion.requiredPantheon))) continue;

            float score = 0f;
            score -= religion.faithCost * 0.05f;

            if (score > bestScore)
            {
                bestScore = score;
                bestReligion = religion;
            }
        }

        return bestReligion;
    }

    private void AssignStrategicCustomBeliefs(Civilization civ)
    {
        if (civ == null) return;

        var allBeliefs = ResourceCache.GetAllBeliefData();
        if (allBeliefs == null || allBeliefs.Length == 0) return;

        foreach (BeliefCategory category in System.Enum.GetValues(typeof(BeliefCategory)))
        {
            var currentBelief = civ.GetCustomBeliefInCategory(category);
            BeliefData bestBelief = null;
            float bestScore = float.MinValue;
            foreach (var belief in allBeliefs)
            {
                if (belief == null || belief.category != category) continue;
                if (!civ.CanUseBelief(belief)) continue;
                if (currentBelief != belief && civ.faith < civ.GetBeliefFaithCost(belief)) continue;
                float score = ScoreBeliefForCivilization(civ, belief);
                score -= civ.GetBeliefFaithCost(belief) * 0.05f;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestBelief = belief;
                }
            }

            if (bestBelief == null) continue;

            float currentScore = currentBelief != null ? ScoreBeliefForCivilization(civ, currentBelief) : float.MinValue;
            if (currentBelief == null || bestScore > currentScore + 0.5f)
                civ.SetCustomBelief(category, bestBelief);
        }
    }

    /// <summary>
    /// Find the best city for a missionary to target — prefers foreign cities, then own cities
    /// without our religion as majority.
    /// </summary>
    private City FindBestMissionaryTarget(Civilization civ, CombatUnit missionary)
    {
        if (ReligionManager.Instance == null) return null;

        City bestTarget = null;
        float bestScore = float.MinValue;

        var ts = TileSystem.GetForPlanet(missionary.planetIndex) ?? TileSystem.Instance;
        if (ts == null) return null;

        // Check all civilizations' cities
        var allCivs = GetAllCivs();
        foreach (var otherCiv in allCivs)
        {
            if (otherCiv == null || otherCiv.cities == null) continue;

            bool isForeign = otherCiv != civ;

            foreach (var city in otherCiv.cities)
            {
                if (city == null) continue;
                if (city.planetIndex != missionary.planetIndex) continue;

                var majorityReligion = ReligionManager.Instance.GetCityMajorityReligion(city);
                if (majorityReligion == civ.foundedReligion) continue; // Already converted

                int dist = ts.GetTileDistance(missionary.currentTileIndex, city.centerTileIndex);
                float score = 100f - dist * 2f; // Prefer closer cities
                if (isForeign) score += 20f;     // Prefer spreading to foreign cities
                if (majorityReligion == null) score += 10f; // Prefer unconverted over those with rival religions

                if (score > bestScore) { bestScore = score; bestTarget = city; }
            }
        }

        return bestTarget;
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
        var seenUnits = new HashSet<CombatUnitData>();
        foreach (var unitData in allUnitData)
        {
            if (unitData == null) continue;

            // Use unique unit replacement and then resolve to the latest unlocked upgrade if available.
            var unitToUse = civ.GetUnitData(unitData);
            unitToUse = unitToUse != null ? unitToUse.GetLatestUnlockedUpgrade(civ) : null;
            if (unitToUse == null || seenUnits.Contains(unitToUse)) continue;

            seenUnits.Add(unitToUse);
            availableUnits.Add(unitToUse);
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
                    CombatCategory.Axeman, CombatCategory.LightCavalry 
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
}
        }
    }
    
    /// <summary>
    /// Consider declaring war using the utility-scored war timing system.
    /// Evaluates every potential target with AIScorer.ScoreWarDecision and only
    /// declares war when the score is positive AND exceeds a threshold.
    /// Replaces the old probability-based dice roll with deterministic scoring.
    /// </summary>
    private void ConsiderWarDeclarations(Civilization civ)
    {
        if (civ == null || civ.leader == null) return;
        if (DiplomacyManager.Instance == null) return;

        var allCivs = GetAllCivs();
        Civilization bestTarget = null;
        float bestScore = 5f; // minimum threshold to declare war

        foreach (var target in allCivs)
        {
            if (target == civ || target == null) continue;
            var rel = DiplomacyManager.Instance.GetRelationship(civ, target);
            if (rel == DiplomaticState.War || rel == DiplomaticState.Alliance) continue;

            float warScore = AIScorer.ScoreWarDecision(civ, target);

            // Diplomatic memory modifiers
            try
            {
                var memory = DiplomacyManager.Instance.GetDiplomaticMemory(civ);
                var reputation = memory.GetReputation(target);
                var trustLevel = memory.GetTrustLevel(target);
                if (trustLevel >= 7) warScore -= 15f;
                if (reputation > 20f) warScore -= 8f;
                if (reputation < -30f) warScore += 10f;
            }
            catch { }

            if (warScore > bestScore)
            {
                bestScore = warScore;
                bestTarget = target;
            }
        }

        if (bestTarget != null)
        {
            DiplomacyManager.Instance.ProposeDeal(civ, bestTarget, DealType.War);
            if (Debug.isDebugBuild)
            {
                string aName = civ.civData?.civName ?? "?";
                string tName = bestTarget.civData?.civName ?? "?";
                Debug.Log($"[AIWarTiming] {aName} declares war on {tName} (score={bestScore:F1})");
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
        
        // Pioneer production uses the global data asset; civ-specific visuals come from WorkerUnitData overrides.
        WorkerUnitData resolvedPioneerData = pioneerData;
        
        if (resolvedPioneerData == null)
        {
            Debug.LogWarning("[CivilizationManager] PrioritizeExpansion: no pioneerData configured on CivilizationManager");
            return;
        }
        
        // Check if pioneer can be produced
        if (!resolvedPioneerData.IsBuildableFor(civ)) return;
        
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
            
            if (city.QueueProduction(resolvedPioneerData))
            {
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
        
        // Diplomatic leaders (primary or secondary) actively seek alliances
        bool isDiplomaticLeader = leader.primaryAgenda == LeaderAgenda.Diplomatic
                                 || leader.secondaryAgenda == LeaderAgenda.Diplomatic;
        if (!isDiplomaticLeader) return;
        
        // Secondary diplomatic leaders are less aggressive about alliances
        float allianceChance = leader.primaryAgenda == LeaderAgenda.Diplomatic ? 0.3f : 0.15f;
        
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
            if (reputation > 20f && trustLevel >= 6 && UnityEngine.Random.value < allianceChance)
            {
                DiplomacyManager.Instance.ProposeDeal(civ, target, DealType.Alliance);
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
                    TryChooseBestPantheonAndBelief(civ, availablePantheons, out var pantheon, out var belief);
                    if (pantheon != null)
                        civ.FoundPantheon(pantheon);
                }
            }
        }
        
        // Try to found religion if we have a pantheon but no religion
        if (civ.foundedPantheons != null && civ.foundedPantheons.Count > 0 && !civ.hasFoundedReligion)
        {
            if (ReligionManager.Instance != null)
            {
                var availableReligions = ReligionManager.Instance.GetAvailableReligions(civ);
                if (availableReligions != null && availableReligions.Count > 0)
                {
                    // Find a city with a holy site
                    var holySiteCity = civ.cities.FirstOrDefault(c => c != null && c.HasHolySite());
                    if (holySiteCity != null)
                    {
                        var religion = ChooseBestReligion(civ, availableReligions);
                        if (religion != null)
                            civ.FoundReligion(religion, holySiteCity);
                    }
                }
            }
        }

        if (civ.hasFoundedReligion || !civ.CanFoundMorePantheons())
            AssignStrategicCustomBeliefs(civ);
        
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
        if (civ == null || civ.ownedTilesByPlanet == null || civ.ownedTilesByPlanet.Count == 0) return 0;
        
        int threatCount = 0;
        const int threatRange = 3; // Tiles to check around owned tiles
        
        // Check each owned tile's neighbors per-planet (tile indices repeat across planets).
        foreach (var kv in civ.ownedTilesByPlanet)
        {
            int planetIndex = kv.Key;
            var ownedTiles = kv.Value;
            if (ownedTiles == null || ownedTiles.Count == 0) continue;

            var ts = TileSystem.GetForPlanet(planetIndex);
            if (ts == null || !ts.IsReady()) continue;

            foreach (int tileIndex in ownedTiles)
            {
                if (tileIndex < 0) continue;

                // Get tiles in threat range (planet-local BFS)
                var tilesInRange = GetTilesInRange(tileIndex, threatRange, ts);

                foreach (int neighborTile in tilesInRange)
                {
                    if (neighborTile < 0) continue;

                    // Check if tile is owned by an enemy
                    var tileData = ts.GetTileData(neighborTile);
                    if (tileData == null || tileData.owner == null) continue;
                    if (tileData.owner == civ) continue; // Own tile

                    // Check if we're at war with this civ
                    var currentRelation = DiplomacyManager.Instance != null
                        ? DiplomacyManager.Instance.GetRelationship(civ, tileData.owner)
                        : DiplomaticState.Peace;

                    if (currentRelation == DiplomaticState.War)
                    {
                        // Count enemy units on this tile (planet-local)
                        int enemyUnits = tileData.owner.combatUnits
                            .Where(u => u != null && u.planetIndex == planetIndex && u.currentTileIndex == neighborTile)
                            .Count();

                        threatCount += enemyUnits;
                    }
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
        if (civ == null || civ.ownedTilesByPlanet == null || civ.ownedTilesByPlanet.Count == 0) return weakNeighbors;
        
        var myStrength = ComputeMilitaryStrength(civ);
        var neighboringCivs = new HashSet<Civilization>();
        
        // Find all neighboring civilizations per-planet
        foreach (var kv in civ.ownedTilesByPlanet)
        {
            int planetIndex = kv.Key;
            var ownedTiles = kv.Value;
            if (ownedTiles == null || ownedTiles.Count == 0) continue;

            var ts = TileSystem.GetForPlanet(planetIndex);
            if (ts == null || !ts.IsReady()) continue;

            foreach (int tileIndex in ownedTiles)
            {
                if (tileIndex < 0) continue;

                var neighbors = ts.GetNeighbors(tileIndex);
                if (neighbors == null) continue;

                foreach (int neighborTile in neighbors)
                {
                    if (neighborTile < 0) continue;

                    var tileData = ts.GetTileData(neighborTile);
                    if (tileData == null || tileData.owner == null) continue;
                    if (tileData.owner == civ) continue; // Own tile

                    neighboringCivs.Add(tileData.owner);
                }
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
        if (civ1 == null || civ2 == null) return false;
        if (civ1.ownedTilesByPlanet == null || civ2.ownedTilesByPlanet == null) return false;

        // Shared borders are planet-local (tile indices repeat across planets).
        foreach (var kv in civ1.ownedTilesByPlanet)
        {
            int planetIndex = kv.Key;
            if (!civ2.ownedTilesByPlanet.TryGetValue(planetIndex, out var civ2Tiles) || civ2Tiles == null || civ2Tiles.Count == 0)
                continue;

            var civ1Tiles = kv.Value;
            if (civ1Tiles == null || civ1Tiles.Count == 0) continue;

            var ts = TileSystem.GetForPlanet(planetIndex);
            if (ts == null || !ts.IsReady()) continue;

            foreach (int tileIndex in civ1Tiles)
            {
                if (tileIndex < 0) continue;
                var neighbors = ts.GetNeighbors(tileIndex);
                if (neighbors == null) continue;

                foreach (int neighborTile in neighbors)
                {
                    if (civ2Tiles.Contains(neighborTile))
                        return true;
                }
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Get tiles in range of a center tile (BFS)
    /// </summary>
    private List<int> GetTilesInRange(int centerTile, int range, TileSystem ts)
    {
        var tilesInRange = new List<int>();
        if (centerTile < 0 || ts == null) return tilesInRange;
        
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
            
            var neighbors = ts.GetNeighbors(currentTile);
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
// Clear any existing civs
        civs.Clear();
        currentCivIndex = -1;
        
        // Multi-planet: spawn on the currently active planet.
        int planetIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        var planet   = GameManager.Instance?.GetPlanetGenerator(planetIndex);
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
        
        // Multi-planet: spawn on the currently active planet.
        int planetIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        var planet = GameManager.Instance?.GetPlanetGenerator(planetIndex);
        var grid  = planet != null ? planet.Grid : null;
        if (grid == null)
        {
            Debug.LogError("SpawnOneCivilization: Planet grid not found!");
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

        // Instantiate Civilization — parent under the planet so it deactivates on planet switch
        GameObject civGO = Instantiate(civilizationPrefab);
        if (civGO == null)
        {
            Debug.LogError($"Failed to instantiate civilization prefab for {data.civName}");
            return;
        }
        
        civGO.name = data.civName;
        if (planet != null) civGO.transform.SetParent(planet.transform, true);
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

        // Pioneer spawning uses the global data asset; civ-specific visuals come from WorkerUnitData overrides.
        WorkerUnitData resolvedPioneerData = pioneerData;

        if (resolvedPioneerData == null)
        {
            Debug.LogError($"SpawnOneCivilization: No pioneerData configured on CivilizationManager for {data.civName}!");
            return;
        }

        // Use the normal worker prefab resolver so pioneers can come from direct prefabs
        // or Addressables, with prefab references preferred when assigned.
        GameObject resolvedPioneerPrefab = resolvedPioneerData.GetPrefab(civ);
        if (resolvedPioneerPrefab == null)
        {
            Debug.LogError($"SpawnOneCivilization: Failed to resolve pioneer prefab for {data.civName}. Aborting spawn.");
            return;
        }

        // Instantiate pioneer — parent under the planet so it deactivates on planet switch
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        Vector3 pos = ts != null ? ts.GetTileCenterFlat(tile) : Vector3.zero;
        var wgo = Instantiate(resolvedPioneerPrefab, pos, Quaternion.identity);
        if (planet != null) wgo.transform.SetParent(planet.transform, true);
        // Register pioneer with wrap registry
        try
        {
            var mgr = FindObjectsByType<HexMapChunkManager>().FirstOrDefault(m => m.PlanetGenerator == planet);
            if (mgr != null) mgr.RegisterObjectForWrapAtTile(tile, wgo);
        }
        catch { }
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
        
        pioneer.Initialize(resolvedPioneerData, civ, tile);
        pioneer.planetIndex = planetIndex;
        civ.workerUnits.Add(pioneer);
        try { pioneer.RegisterToRegistry(); } catch { }
        
    }

    /// <summary>
    /// Finds a random unoccupied land tile, optionally matching climate preferences.
    /// Uses the same approach as AnimalManager for reliable tile finding.
    /// </summary>
    int FindSpawnTile(CivData data, HashSet<int> occupied, bool enforceClimate)
    {
// COPIED FROM ANIMALMANAGER: Use exact same approach for reliability
        var candidates = new List<int>();
        int planetIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        var planet = GameManager.Instance?.GetPlanetGenerator(planetIndex);
        int tileCount = planet != null && planet.Grid != null ? planet.Grid.TileCount : 0;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;

        if (planet == null)
        {
            Debug.LogError("FindSpawnTile: No planet generator found!");
            return -1;
        }
        
        if (planet.Grid == null)
        {
            Debug.LogError("FindSpawnTile: planet grid is null!");
            return -1;
        }
        
        if (!planet.Grid.IsBuilt)
        {
            Debug.LogError("FindSpawnTile: planet grid is not built!");
            return -1;
        }
        
        if (!planet.HasGeneratedSurface)
        {
            Debug.LogError($"FindSpawnTile: planet surface not ready! HasGeneratedSurface = {planet.HasGeneratedSurface}");
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
            var tile = ts != null ? ts.GetTileData(i) : null;
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

            // Prevent normal civilizations from spawning inside the New World band(s).
            // Tribes and city-states may spawn anywhere, so only restrict non-tribe/non-citystate civs.
            if (!data.isTribe && !data.isCityState)
            {
                try
                {
                    if (planet != null && planet.IsTileInNewWorld(i))
                    {
                        // Skip candidate tiles that belong to the New World
                        continue;
                    }
                }
                catch { }
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

        int spread = Mathf.Clamp(GameSetupData.selectedStartingSpread, 0, 2);
        if (spread == 1 || occupied.Count == 0)
            return candidates[UnityEngine.Random.Range(0, candidates.Count)];

        int sampleCount = Mathf.Min(candidates.Count, spread == 2 ? 40 : 16);
        int bestTile = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        int bestScore = spread == 2 ? int.MinValue : int.MaxValue;
        for (int s = 0; s < sampleCount; s++)
        {
            int tileCandidate = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            int nearest = int.MaxValue;
            foreach (int occ in occupied)
            {
                int d = ts != null ? ts.GetTileDistance(tileCandidate, occ) : 0;
                if (d < nearest) nearest = d;
            }

            if (spread == 2)
            {
                if (nearest > bestScore) { bestScore = nearest; bestTile = tileCandidate; }
            }
            else
            {
                if (nearest < bestScore) { bestScore = nearest; bestTile = tileCandidate; }
            }
        }
        return bestTile;
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
               biome == Biome.Ocean ||
               biome == Biome.Lake;
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

        int planetIndex = pioneer.planetIndex >= 0
            ? pioneer.planetIndex
            : (GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0);
        var planet = GameManager.Instance?.GetPlanetGenerator(planetIndex);
        if (planet == null || planet.Grid == null)
        {
            Debug.LogWarning("Cannot position camera: planet generator not found");
            return;
        }

        // Get the tile position in flat map space. Move camera two tiles south of the pioneer.
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        Vector3 tileWorldPosition = Vector3.zero;
        if (ts != null)
        {
            // Try to compute a tile four rows (south) from the pioneer's tile using the planet grid.
            var grid = planet.Grid;
            if (grid != null && grid.IsBuilt && pioneerTileIndex >= 0 && pioneerTileIndex < grid.TileCount)
            {
                int row = pioneerTileIndex / Mathf.Max(1, grid.Width);
                int col = pioneerTileIndex % Mathf.Max(1, grid.Width);
                int newRow = Mathf.Min(grid.Height - 1, row - 4); // four tiles south (decreasing row => south)
                int southIndex = newRow * grid.Width + col;
                tileWorldPosition = ts.GetTileCenterFlat(southIndex);
            }
            else
            {
                tileWorldPosition = ts.GetTileCenterFlat(pioneerTileIndex);
            }
        }

        // Focus the camera on the pioneer and zoom in close (like Civilization start)
        cameraManager.JumpToWorldPoint(tileWorldPosition);
        // Zoom in to ~30% of the height range for a close-up start view
        float startHeight = Mathf.Lerp(cameraManager.minHeight, cameraManager.maxHeight, 0.30f);
        cameraManager.ZoomBy(cameraManager.CameraHeight - startHeight);
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
        return civ;
    }

    /// <summary>
    /// Like CreateRebelFaction but applies a custom name (used for noble-faction rebellions
    /// so the rebel civ is named after the largest city in the bloc).
    /// </summary>
    public Civilization CreateRebelFaction(City revoltedCity, string rebelName)
    {
        var civ = CreateRebelFaction(revoltedCity);
        if (civ != null && !string.IsNullOrEmpty(rebelName))
        {
            civ.gameObject.name = rebelName;
            // Update civData name if the ScriptableObject is writable at runtime
            if (civ.civData != null)
                civ.civData.civName = rebelName;
        }
        return civ;
    }

    void Update()
    {
        // Example: player presses End Turn (Enter key to avoid conflict with Space for space travel)
        var kb = Keyboard.current;
        if (kb != null && (kb[Key.Enter].wasPressedThisFrame || kb[Key.NumpadEnter].wasPressedThisFrame))
        {
            if (TurnManager.Instance != null)
                TurnManager.Instance.EndPlayerTurn();
        }
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
