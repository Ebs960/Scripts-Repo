// Assets/Scripts/Civs/Civilization.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public enum DiplomaticState
{
    War,
    Peace,
    Alliance,
    Vassal,
    Protected,
    Trade
}

/// <summary>
/// Represents one civilization's full runtime state: data, units, cities, research, culture, policies, government, yields, and relations.
/// </summary>
public class Civilization : MonoBehaviour
{
    [Header("Static Data")]
    public CivData civData { get; private set; }
    public LeaderData leader { get; private set; } // Added to store the active leader
    
    // Cache for unique unit and building replacements
    private Dictionary<CombatUnitData, CombatUnitData> uniqueUnitReplacements = new Dictionary<CombatUnitData, CombatUnitData>();
    private Dictionary<BuildingData, BuildingData> uniqueBuildingReplacements = new Dictionary<BuildingData, BuildingData>();
    
    // Runtime property for diplomatic state access
    public bool isPlayerControlled = false;

    [Header("Map & Military")]
    public List<int> ownedTileIndices       = new List<int>();
    public List<City> cities                = new List<City>();
    public List<CombatUnit> combatUnits     = new List<CombatUnit>();
    public List<WorkerUnit> workerUnits     = new List<WorkerUnit>();
    
    [Header("Interplanetary Trade")]
    public List<TradeRoute> interplanetaryTradeRoutes = new List<TradeRoute>();
    
    [Header("Resources")]
    public Dictionary<ResourceData, int> resourceStockpile = new Dictionary<ResourceData, int>();
    
    [Header("Equipment Inventory")]
    // Track equipment availability - each civ has stockpiles of equipment
    public Dictionary<EquipmentData, int> equipmentInventory = new Dictionary<EquipmentData, int>();
    // Starting equipment to spawn with
    [SerializeField] private List<EquipmentData> startingEquipment = new List<EquipmentData>();
    [Tooltip("The base prefab used to create a new city. The City script on this prefab will handle spawning the correct visual model based on tech age.")]
    [SerializeField] private GameObject cityPrefab;

    [Header("Science & Technology")]
    public List<TechData> researchedTechs    = new List<TechData>();
    public TechData      currentTech;
    public float         currentTechProgress;
    public float scienceModifier = 0f; // Civilization-wide percentage bonus, starts at 0%

    [Header("Culture")]
    public List<CultureData> researchedCultures    = new List<CultureData>();
    public CultureData       currentCulture;
    public float             currentCultureProgress;
    public float cultureModifier = 0f; // Civilization-wide percentage bonus, starts at 0%

    [Header("Policy & Government")]
    public List<PolicyData>      unlockedPolicies       = new List<PolicyData>();
    public List<PolicyData>      activePolicies         = new List<PolicyData>();
    public List<GovernmentData>  unlockedGovernments    = new List<GovernmentData>();
    public GovernmentData        currentGovernment;

    [Header("Unrest & Famine")]
    [Tooltip("0–1 scale. Increases when at war, reduces loyalty city-wide.")]
    [Range(0f, 1f)]
    public float warWeariness = 0f;
    [Tooltip("% warWeariness added per war partner, per turn")]
    public float warWearinessPerWarTurn = 0.02f;
    [Tooltip("% warWeariness recovered per peace turn")]
    public float warWearinessRecoveryPerTurn = 0.01f;
    [HideInInspector] public bool famineActive = false;

    [Header("Diplomacy")]
    public Dictionary<Civilization, DiplomaticState> relations = new Dictionary<Civilization, DiplomaticState>();

    [Header("Yields & Storage")]
    public int gold;
    public int food;
    public int science;
    public int culture;
    public int policyPoints;
    public int faith;

    [Header("Modifiers")]
    public float attackBonus;
    public float defenseBonus;
    public float movementBonus;
    // Specific yield modifiers
    public float foodModifier;
    public float productionModifier;
    public float goldModifier;
    // scienceModifier and cultureModifier already exist in the [Header("Science & Technology")] and [Header("Culture")] sections.
    // They will be repurposed to be the civilization-wide percentage modifiers.
    public float faithModifier;

    [Header("Religion")]
    public PantheonData foundedPantheon;
    public BeliefData chosenFounderBelief;
    public ReligionData foundedReligion;
    public bool hasFoundedPantheon;
    public bool hasFoundedReligion;


    [Header("Unlocked Units")]
    public List<CombatUnitData> unlockedCombatUnits = new List<CombatUnitData>();
    public List<WorkerUnitData> unlockedWorkerUnits = new List<WorkerUnitData>();
    public List<BuildingData> unlockedBuildings = new List<BuildingData>();
    public List<AbilityData> unlockedAbilities = new List<AbilityData>();
    // Unlocked religions are not stored here; ReligionManager handles availability.

    // Events for UI or other systems
    public event Action<Civilization,int> OnTurnStarted;  // civ, round
    public event Action<CultureData>        OnCultureCompleted;
    public event System.Action<ResourceData, int> OnResourceChanged;
    // Add equipment event
    public event System.Action<EquipmentData, int> OnEquipmentChanged;
    public event Action<TechData> OnTechStarted;
    public event Action<CultureData> OnCultureStarted;
    public event Action<TechData> OnTechResearched;  // The event
    // Fired after research/culture changes that may affect availability (units/buildings/improvements)
    public event Action OnUnlocksChanged;

    private int turnCount;

    // --- Governor System ---
    public int governorCount = 1; // Number of governors this civ can create (modifiable by events, policies, etc.)
    public List<Governor> governors = new List<Governor>(); // All created governors

    // List of governor traits this civ has unlocked (for trait assignment UI)
    public List<GovernorTrait> unlockedGovernorTraits = new List<GovernorTrait>();
    

    // Increase the number of governors this civ can create
    public void IncreaseGovernorCount(int amount = 1)
    {
        governorCount += amount;
    }

    // Create a new governor if there is an available slot
    public Governor CreateGovernor(string name, Governor.Specialization specialization)
    {
        if (governors.Count >= governorCount)
            return null; // No available slots
        int newId = governors.Count > 0 ? governors[governors.Count - 1].Id + 1 : 1;
        var gov = new Governor(newId, name, specialization);
        governors.Add(gov);
        return gov;
    }

    // Assign a governor to a city (removes from previous city if needed)
    public bool AssignGovernorToCity(Governor governor, City city)
    {
        if (governor == null || city == null) return false;
        // Remove from any previous city
        foreach (var c in governors.SelectMany(g => g.Cities).ToList())
        {
            if (c == city)
            {
                c.governor = null;
                governor.Cities.Remove(c);
            }
        }
        // Assign
        city.governor = governor;
        if (!governor.Cities.Contains(city))
            governor.Cities.Add(city);
        return true;
    }

    // Remove a governor from a city
    public bool RemoveGovernorFromCity(Governor governor, City city)
    {
        if (governor == null || city == null) return false;
        if (city.governor == governor)
        {
            city.governor = null;
            governor.Cities.Remove(city);
            return true;
        }
        return false;
    }

    // Get all cities in a province (all cities assigned to a governor)
    public List<City> GetProvinceCities(Governor governor)
    {
        return governor?.Cities ?? new List<City>();
    }

    void Awake()
    {
        // Initialize the leader-specific unique units and buildings
        if (leader != null)
        {
            InitializeLeaderUniques();
        }
    }

    void Start()
    {
        // Seed starting techs
        if (civData.startingTechs != null)
            researchedTechs.AddRange(civData.startingTechs);

        // Seed starting policies
        if (civData.startingPolicies != null)
        {
            unlockedPolicies.AddRange(civData.startingPolicies);
            activePolicies.AddRange(civData.startingPolicies);
        }
        
        // Initialize equipment inventory with starting equipment
        if (startingEquipment != null && startingEquipment.Count > 0)
        {
            foreach (var equipment in startingEquipment)
            {
                AddEquipment(equipment, 5); // Start with 5 of each equipment
            }
        }
        
        // Apply leader bonuses
        if (leader != null)
        {
            ApplyLeaderBonuses();
        }

        // Register with the turn order
        CivilizationManager.Instance.RegisterCiv(this);
    }
    
    /// <summary>
    /// Initialize dictionaries to map standard units and buildings to their unique replacements
    /// </summary>
    private void InitializeLeaderUniques()
    {
        // Clear existing dictionaries
        uniqueUnitReplacements.Clear();
        uniqueBuildingReplacements.Clear();

        if (leader == null || leader.uniqueUnits == null) return;

        // Process unique units from the leader
        foreach (var uniqueUnitDef in leader.uniqueUnits)
        {
            if (uniqueUnitDef != null && uniqueUnitDef.replacesUnit != null && uniqueUnitDef.uniqueUnit != null)
            {
                uniqueUnitReplacements[uniqueUnitDef.replacesUnit] = uniqueUnitDef.uniqueUnit;
            }
        }

        if (leader.uniqueBuildings == null) return;

        // Process unique buildings from the leader
        foreach (var uniqueBuildingDef in leader.uniqueBuildings)
        {
            if (uniqueBuildingDef != null && uniqueBuildingDef.replacesBuilding != null && uniqueBuildingDef.uniqueBuilding != null)
            {
                uniqueBuildingReplacements[uniqueBuildingDef.replacesBuilding] = uniqueBuildingDef.uniqueBuilding;
            }
        }
    }
    
    /// <summary>
    /// Apply leader's bonus modifiers
    /// </summary>
    private void ApplyLeaderBonuses()
    {
        if (leader == null) return;

        // Apply leader-specific modifiers
        attackBonus += leader.militaryStrengthModifier;
        goldModifier += leader.goldModifier;
        scienceModifier += leader.scienceModifier;
        productionModifier += leader.productionModifier;
        foodModifier += leader.foodModifier;
        cultureModifier += leader.cultureModifier;
        faithModifier += leader.faithModifier;
    }

    /// <summary>
    /// Called by TurnManager at the start of this civ's turn.
    /// </summary>
    public void BeginTurn(int round)
    {
        turnCount = round;

        // 1) Reset units
        foreach (var u in combatUnits) u.ResetForNewTurn();
        foreach (var w in workerUnits)  w.ResetForNewTurn();

        // 2) Process each city (production, growth, morale, surrender, label)
        foreach (var city in cities)
            city.ProcessCityTurn();

        // 3) Collect city yields into storage
        foreach (var city in cities)
        {
            gold         += Mathf.RoundToInt(city.GetGoldPerTurn() * (1 + goldModifier));
            food         += Mathf.RoundToInt(city.GetFoodPerTurn() * (1 + foodModifier));
            science      += Mathf.RoundToInt(city.GetSciencePerTurn() * (1 + scienceModifier));
            culture      += Mathf.RoundToInt(city.GetCulturePerTurn() * (1 + cultureModifier));
            policyPoints += city.GetPolicyPointPerTurn(); // Assuming no direct modifier for policy points yet
            faith        += Mathf.RoundToInt(city.GetFaithPerTurn() * (1 + faithModifier));
        }

        // 3.5) Process interplanetary trade routes
        foreach (var tradeRoute in interplanetaryTradeRoutes)
        {
            if (tradeRoute != null && tradeRoute.isInterplanetaryRoute)
            {
                gold += Mathf.RoundToInt(tradeRoute.goldPerTurn * (1 + goldModifier));
                Debug.Log($"[{civData.civName}] Interplanetary trade earned {tradeRoute.goldPerTurn} gold (Planet {tradeRoute.originPlanetIndex} → {tradeRoute.destinationPlanetIndex})");
            }
        }

        // 3.6) Per-unit yields (combat units). Applies after city yields, before research/culture processing.
        if (combatUnits != null && combatUnits.Count > 0)
        {
            int addFood = 0, addGold = 0, addSci = 0, addCul = 0, addFai = 0, addPol = 0;
            foreach (var u in combatUnits)
            {
                if (u == null || u.data == null) continue;
                var yields = BonusAggregator.ComputeUnitPerTurnYield(this, u.data, u.Weapon, u.Shield, u.Armor, u.Miscellaneous);
                addFood += yields.food;
                addGold += yields.gold;
                addSci  += yields.science;
                addCul  += yields.culture;
                addFai  += yields.faith;
                addPol  += yields.policy;
            }

            // Apply global civ yield modifiers to these additions as well
            gold    += Mathf.RoundToInt(addGold * (1 + goldModifier));
            food    += Mathf.RoundToInt(addFood * (1 + foodModifier));
            science += Mathf.RoundToInt(addSci  * (1 + scienceModifier));
            culture += Mathf.RoundToInt(addCul  * (1 + cultureModifier));
            faith   += Mathf.RoundToInt(addFai  * (1 + faithModifier));
            policyPoints += addPol; // no global modifier currently
        }

        // 3.7) Per-unit yields (workers)
        if (workerUnits != null && workerUnits.Count > 0)
        {
            int addFood = 0, addGold = 0, addSci = 0, addCul = 0, addFai = 0, addPol = 0;
            foreach (var w in workerUnits)
            {
                if (w == null || w.data == null) continue;
                var yields = BonusAggregator.ComputeWorkerPerTurnYield(this, w.data);
                addFood += yields.food;
                addGold += yields.gold;
                addSci  += yields.science;
                addCul  += yields.culture;
                addFai  += yields.faith;
                addPol  += yields.policy;
            }

            gold    += Mathf.RoundToInt(addGold * (1 + goldModifier));
            food    += Mathf.RoundToInt(addFood * (1 + foodModifier));
            science += Mathf.RoundToInt(addSci  * (1 + scienceModifier));
            culture += Mathf.RoundToInt(addCul  * (1 + cultureModifier));
            faith   += Mathf.RoundToInt(addFai  * (1 + faithModifier));
            policyPoints += addPol; // no global modifier currently
        }

        // 4) Advance technology
        ProcessResearch();

        // 5) Advance culture
        ProcessCulture();

        // --- NEW: Unrest & famine handling ---
        // Update war weariness
        int warCount = 0;
        foreach (var kv in relations)
            if (kv.Value == DiplomaticState.War)
                warCount++;
        if (warCount > 0)
            warWeariness += warCount * warWearinessPerWarTurn;
        else
            warWeariness = Mathf.Max(0f, warWeariness - warWearinessRecoveryPerTurn);

        // Clamp 0–1
        warWeariness = Mathf.Clamp01(warWeariness);

        // Check famine: true if food stockpile <= 0
        famineActive = (food <= 0);
        if (famineActive)
        {
            // Each turn of famine, all units lose 5% max health
            foreach (var u in combatUnits)
                u.ApplyDamage(Mathf.CeilToInt(u.MaxHealth * 0.05f));
            foreach (var w in workerUnits)
                w.ApplyDamage(Mathf.CeilToInt(w.data.baseHealth * 0.05f));
        }

        // 6) Fire turn‐started event
        OnTurnStarted?.Invoke(this, round);
    }

    private void ProcessResearch()
    {
        if (currentTech == null) return;
        currentTechProgress += science; // 'science' here is the total accumulated for the turn
        if (currentTechProgress >= currentTech.scienceCost)
        {
            TechData completedTech = currentTech;
            currentTech = null; // Stop further progress on this tech immediately
            currentTechProgress = 0;

            // Call TechManager to handle completion
            if (TechManager.Instance != null)
            {
                TechManager.Instance.CompleteResearch(this, completedTech);
            }
            else
            {
                Debug.LogError($"Civilization {civData.civName}: TechManager.Instance is null. Cannot complete research for {completedTech.techName}.");
                // Fallback: Manually do critical parts if manager is missing (not ideal)
                if (!researchedTechs.Contains(completedTech)) researchedTechs.Add(completedTech);
                ApplyTechBonuses(completedTech); 
            }
        }
    }

    /// <summary>
    /// Updates all city models when advancing to a new tech age
    /// </summary>
    private void UpdateCityModelsForNewAge()
    {
        foreach (var city in cities)
        {
            if (city != null)
            {
                city.UpdateCityModelForAge();
            }
        }
    }

    private void ApplyTechBonuses(TechData tech)
    {
        // Store old age to check if we advanced
        TechAge oldAge = GetCurrentAge();
        
        // Apply civilization bonuses from tech
        attackBonus += tech.attackBonus;
        defenseBonus += tech.defenseBonus;
        movementBonus += tech.movementBonus;
        foodModifier += tech.foodModifier;
        productionModifier += tech.productionModifier;
        goldModifier += tech.goldModifier;
        scienceModifier += tech.scienceModifier;
        cultureModifier += tech.cultureModifier;
        faithModifier += tech.faithModifier;

        // Unlock items from tech
        if (tech.unlockedUnits != null) {
            foreach (var unitData in tech.unlockedUnits) {
                if (!unlockedCombatUnits.Contains(unitData)) unlockedCombatUnits.Add(unitData);
            }
        }
        if (tech.unlockedWorkerUnits != null) {
            foreach (var workerData in tech.unlockedWorkerUnits) {
                if (!unlockedWorkerUnits.Contains(workerData)) unlockedWorkerUnits.Add(workerData);
            }
        }
        if (tech.unlockedBuildings != null) {
            foreach (var buildingData in tech.unlockedBuildings) {
                if (!unlockedBuildings.Contains(buildingData)) unlockedBuildings.Add(buildingData);
            }
        }
        // Note: TechData doesn't currently have unlockedAbilities. If added, handle here.

        // Apply governor-related bonuses
        if (tech.additionalGovernorSlots > 0)
        {
            IncreaseGovernorCount(tech.additionalGovernorSlots);
            Debug.Log($"{civData.civName} gained {tech.additionalGovernorSlots} governor slot(s) from {tech.techName}");
        }

        if (tech.unlockedGovernorTraits != null)
        {
            foreach (var trait in tech.unlockedGovernorTraits)
            {
                if (!unlockedGovernorTraits.Contains(trait))
                {
                    unlockedGovernorTraits.Add(trait);
                    Debug.Log($"{civData.civName} unlocked governor trait: {trait.traitName}");
                }
            }
        }
        
        // Check if we advanced to a new age and update city models
        TechAge newAge = GetCurrentAge();
        if (newAge != oldAge)
        {
            UpdateCityModelsForNewAge();
        }
    }

    private void ProcessCulture()
    {
        if (currentCulture == null) return;
        currentCultureProgress += culture; // 'culture' here is the total accumulated for the turn
        if (currentCultureProgress >= currentCulture.cultureCost)
        {
            CultureData completedCulture = currentCulture;
            // Stop further progress on this culture immediately, GameManager will null it after calling OnCultureAdopted.
            // currentCulture = null; 
            // currentCultureProgress = 0;

            // Call CultureManager to handle completion
            if (CultureManager.Instance != null)
            {
                CultureManager.Instance.CompleteCultureAdoption(this, completedCulture);
            }
            else
            {
                Debug.LogError($"Civilization {civData.civName}: CultureManager.Instance is null. Cannot complete culture adoption for {completedCulture.cultureName}.");
                // Fallback: Manually do critical parts if manager is missing (not ideal)
                OnCultureAdopted(completedCulture); // This will add to researchedCultures and apply bonuses
                currentCulture = null; // Still need to clear it here for fallback
                currentCultureProgress = 0;
            }
        }
    }

    private void ApplyCultureBonuses(CultureData cult)
    {
        attackBonus   += cult.attackBonus;
        defenseBonus  += cult.defenseBonus;
        movementBonus += cult.movementBonus;
        foodModifier += cult.foodModifier;
        productionModifier += cult.productionModifier;
        goldModifier += cult.goldModifier;
        scienceModifier += cult.scienceModifier;
        cultureModifier += cult.cultureModifier;
        faithModifier += cult.faithModifier;

        // Unlock items from culture
        if (cult.unlockedUnits != null) {
            foreach (var unitData in cult.unlockedUnits) {
                if (!unlockedCombatUnits.Contains(unitData)) unlockedCombatUnits.Add(unitData);
            }
        }
        if (cult.unlockedWorkerUnits != null) {
            foreach (var workerData in cult.unlockedWorkerUnits) {
                if (!unlockedWorkerUnits.Contains(workerData)) unlockedWorkerUnits.Add(workerData);
            }
        }
        if (cult.unlockedBuildings != null) {
            foreach (var buildingData in cult.unlockedBuildings) {
                if (!unlockedBuildings.Contains(buildingData)) unlockedBuildings.Add(buildingData);
            }
        }
        if (cult.unlockedAbilities != null) {
            foreach (var abilityData in cult.unlockedAbilities) {
                if (!unlockedAbilities.Contains(abilityData)) unlockedAbilities.Add(abilityData);
                // TODO: Decide how to grant these abilities to units (e.g., all existing, new ones, or as an unlock option)
            }
        }
        if (cult.unlocksPolicies != null) {
             foreach (var policyData in cult.unlocksPolicies) {
                if (!unlockedPolicies.Contains(policyData)) unlockedPolicies.Add(policyData);
            }
        }
        // cult.unlockedReligions are typically made available for founding, not directly "unlocked" into a list.
        // ReligionManager would handle their availability based on various factors including cultural unlocks if designed so.

        // Apply governor-related bonuses
        if (cult.additionalGovernorSlots > 0)
        {
            IncreaseGovernorCount(cult.additionalGovernorSlots);
            Debug.Log($"{civData.civName} gained {cult.additionalGovernorSlots} governor slot(s) from {cult.cultureName}");
        }

        if (cult.unlockedGovernorTraits != null)
        {
            foreach (var trait in cult.unlockedGovernorTraits)
            {
                if (!unlockedGovernorTraits.Contains(trait))
                {
                    unlockedGovernorTraits.Add(trait);
                    Debug.Log($"{civData.civName} unlocked governor trait: {trait.traitName}");
                }
            }
        }
    }

    // --- Tech & Culture API ---
    public bool CanResearch(TechData tech)
    {
        if (tech == null) { Debug.Log("[Civilization] CanResearch: tech is null"); return false; }
        if (currentTech != null) { Debug.Log($"[Civilization] CanResearch ({tech.techName}): Already researching {currentTech.techName}"); return false; }
        if (researchedTechs.Contains(tech)) { Debug.Log($"[Civilization] CanResearch ({tech.techName}): Already researched."); return false; }
        // if (science <= 0) { Debug.Log($"[Civilization] CanResearch ({tech.techName}): Science output is <= 0."); return false; } // Usually, we allow selection even with 0 science, it just won't progress.

        foreach (var req in tech.requiredTechnologies)
        {
            if (!researchedTechs.Contains(req)) { Debug.Log($"[Civilization] CanResearch ({tech.techName}): Missing tech prerequisite: {req.techName}"); return false; }
        }
        foreach (var req in tech.requiredCultures)
        {
            if (!researchedCultures.Contains(req)) { Debug.Log($"[Civilization] CanResearch ({tech.techName}): Missing culture prerequisite: {req.cultureName}"); return false; }
        }
        if (cities.Count < tech.requiredCityCount) { Debug.Log($"[Civilization] CanResearch ({tech.techName}): Insufficient cities. Have {cities.Count}, need {tech.requiredCityCount}"); return false; }
        // Add biome check if needed
        Debug.Log($"[Civilization] CanResearch ({tech.techName}): All conditions met. Returning true.");
        return true;
    }

    public void StartResearch(TechData tech)
    {
        if (!CanResearch(tech)) return;
        currentTech = tech;
        currentTechProgress = 0;
        Debug.Log($"[Civilization] Started research on {tech.techName}");
        OnTechStarted?.Invoke(tech); // Fire event for UI
    }

    public bool CanCultivate(CultureData cult)
    {
        if (cult == null) { Debug.Log("[Civilization] CanCultivate: culture is null"); return false; }
        if (currentCulture != null) { Debug.Log($"[Civilization] CanCultivate ({cult.cultureName}): Already adopting {currentCulture.cultureName}"); return false; }
        if (researchedCultures.Contains(cult)) { Debug.Log($"[Civilization] CanCultivate ({cult.cultureName}): Already adopted."); return false; }
        // if (culture <= 0) { Debug.Log($"[Civilization] CanCultivate ({cult.cultureName}): Culture output is <= 0."); return false; }
        foreach (var req in cult.requiredCultures)
        {
            if (!researchedCultures.Contains(req)) { Debug.Log($"[Civilization] CanCultivate ({cult.cultureName}): Missing culture prerequisite: {req.cultureName}"); return false; }
        }
        if (cities.Count < cult.requiredCityCount) { Debug.Log($"[Civilization] CanCultivate ({cult.cultureName}): Insufficient cities. Have {cities.Count}, need {cult.requiredCityCount}"); return false; }
        // Add biome check if needed
        Debug.Log($"[Civilization] CanCultivate ({cult.cultureName}): All conditions met. Returning true.");
        return true;
    }

    public void StartCulture(CultureData cult)
    {
        if (!CanCultivate(cult)) return;
        currentCulture = cult;
        currentCultureProgress = 0;
        Debug.Log($"[Civilization] Started culture adoption: {cult.cultureName}");
        OnCultureStarted?.Invoke(cult); // Fire event for UI
    }

    // --- Policy & Government API ---
    public bool CanAdoptPolicy(PolicyData p)
        => PolicyManager.Instance.GetAvailablePolicies(this).Contains(p);

    public void AdoptPolicy(PolicyData p)
    {
        // PolicyManager.Instance.AdoptPolicy(this, p); // This would typically handle adding to activePolicies
        if (p == null || !CanAdoptPolicy(p)) return;

        if (!activePolicies.Contains(p))
        {
            activePolicies.Add(p);
            ApplyPolicyBonuses(p); // Apply bonuses when adopted
            // TODO: UI update, notifications
        }
    }

    // New method to apply bonuses from a single policy
    private void ApplyPolicyBonuses(PolicyData policy)
    {
        if (policy == null) return;
        attackBonus += policy.attackBonus;
        defenseBonus += policy.defenseBonus;
        movementBonus += policy.movementBonus;
        foodModifier += policy.foodModifier;
        productionModifier += policy.productionModifier;
        goldModifier += policy.goldModifier;
        scienceModifier += policy.scienceModifier;
        cultureModifier += policy.cultureModifier;
        faithModifier += policy.faithModifier;

        // Governor slot and trait unlocks
        if (policy.additionalGovernorSlots > 0)
        {
            IncreaseGovernorCount(policy.additionalGovernorSlots);
            Debug.Log($"{civData.civName} gained {policy.additionalGovernorSlots} governor slot(s) from policy {policy.policyName}");
        }
        if (policy.unlockedGovernorTraits != null)
        {
            foreach (var trait in policy.unlockedGovernorTraits)
            {
                if (!unlockedGovernorTraits.Contains(trait))
                {
                    unlockedGovernorTraits.Add(trait);
                    Debug.Log($"{civData.civName} unlocked governor trait: {trait.traitName} from policy {policy.policyName}");
                }
            }
        }
    }

    // New method to remove bonuses from a policy (if policies can be revoked)
    private void RemovePolicyBonuses(PolicyData policy)
    {
        if (policy == null) return;
        attackBonus -= policy.attackBonus;
        defenseBonus -= policy.defenseBonus;
        movementBonus -= policy.movementBonus;
        foodModifier -= policy.foodModifier;
        productionModifier -= policy.productionModifier;
        goldModifier -= policy.goldModifier;
        scienceModifier -= policy.scienceModifier;
        cultureModifier -= policy.cultureModifier;
        faithModifier -= policy.faithModifier;
    }

    public bool CanChangeGovernment(GovernmentData g)
        => PolicyManager.Instance.GetAvailableGovernments(this).Contains(g);

    public void ChangeGovernment(GovernmentData g)
    {
        // PolicyManager.Instance.ChangeGovernment(this, g); // This would handle setting currentGovernment
        if (g == null || !CanChangeGovernment(g) || currentGovernment == g) return;

        // Remove bonuses from old government if one was active
        if (currentGovernment != null)
        {
            RemoveGovernmentBonuses(currentGovernment);
        }
        currentGovernment = g;
        ApplyGovernmentBonuses(g); // Apply bonuses from new government

        // Unlock new units and buildings
        if (g.unlockedUnits != null)
        {
            foreach (var unit in g.unlockedUnits)
            {
                if (!unlockedCombatUnits.Contains(unit))
                {
                    unlockedCombatUnits.Add(unit);
                    Debug.Log($"{civData.civName} unlocked {unit.unitName} through {g.governmentName}");
                }
            }
        }

        if (g.unlockedWorkerUnits != null)
        {
            foreach (var worker in g.unlockedWorkerUnits)
            {
                if (!unlockedWorkerUnits.Contains(worker))
                {
                    unlockedWorkerUnits.Add(worker);
                    Debug.Log($"{civData.civName} unlocked {worker.unitName} through {g.governmentName}");
                }
            }
        }

        if (g.unlockedBuildings != null)
        {
            foreach (var building in g.unlockedBuildings)
            {
                if (!unlockedBuildings.Contains(building))
                {
                    unlockedBuildings.Add(building);
                    Debug.Log($"{civData.civName} unlocked {building.buildingName} through {g.governmentName}");
                }
            }
        }

        // Notify cities to update their available buildings
        foreach (var city in cities)
        {
            city.UpdateAvailableBuildings();
        }

        // TODO: UI update, notifications
    }

    // New method to apply bonuses from a government
    private void ApplyGovernmentBonuses(GovernmentData gov)
    {
        if (gov == null) return;
        attackBonus += gov.attackBonus;
        defenseBonus += gov.defenseBonus;
        movementBonus += gov.movementBonus;
        foodModifier += gov.foodModifier;
        productionModifier += gov.productionModifier;
        goldModifier += gov.goldModifier;
        scienceModifier += gov.scienceModifier;
        cultureModifier += gov.cultureModifier;
        faithModifier += gov.faithModifier;

        // Unlock policies from government
        if (gov.unlocksPolicies != null) {
            foreach (var policyData in gov.unlocksPolicies) {
                if (!unlockedPolicies.Contains(policyData)) unlockedPolicies.Add(policyData);
            }
        }
    }

    // New method to remove bonuses from a government
    private void RemoveGovernmentBonuses(GovernmentData gov)
    {
        if (gov == null) return;
        attackBonus -= gov.attackBonus;
        defenseBonus -= gov.defenseBonus;
        movementBonus -= gov.movementBonus;
        foodModifier -= gov.foodModifier;
        productionModifier -= gov.productionModifier;
        goldModifier -= gov.goldModifier;
        scienceModifier -= gov.scienceModifier;
        cultureModifier -= gov.cultureModifier;
        faithModifier -= gov.faithModifier;
    }

    // --- Diplomacy ---
    public void SetRelation(Civilization other, DiplomaticState state)
        => relations[other] = state;

    /// <summary>
    /// Gets the appropriate building data, using unique building if available
    /// </summary>
    public BuildingData GetBuildingData(BuildingData standardBuilding)
    {
        // Check if we should use a unique building replacement
        if (uniqueBuildingReplacements.TryGetValue(standardBuilding, out BuildingData uniqueReplacement))
        {
            return uniqueReplacement;
        }
        
        return standardBuilding;
    }
    
    /// <summary>
    /// Gets the appropriate unit data, using unique unit if available
    /// </summary>
    public CombatUnitData GetUnitData(CombatUnitData standardUnit)
    {
        // Check if we should use a unique unit replacement
        if (uniqueUnitReplacements.TryGetValue(standardUnit, out CombatUnitData uniqueReplacement))
        {
            return uniqueReplacement;
        }
        
        return standardUnit;
    }
    
    /// <summary>
    /// Adds a city to this civilization's control
    /// </summary>
    public void AddCity(City city)
    {
        if (!cities.Contains(city))
        {
            cities.Add(city);
            Debug.Log($"{civData.civName} founded city: {city.cityName}");
        }
    }
    
    /// <summary>
    /// Add resources to the civilization's stockpile
    /// </summary>
    public void AddResource(ResourceData resource, int amount)
    {
        if (resource == null || amount <= 0) return;
        
        if (!resourceStockpile.ContainsKey(resource))
            resourceStockpile[resource] = 0;
            
        resourceStockpile[resource] += amount;
        
        // Notify any UI or other systems
        OnResourceChanged?.Invoke(resource, resourceStockpile[resource]);
    }
    
    /// <summary>
    /// Add an interplanetary trade route
    /// </summary>
    public void AddTradeRoute(TradeRoute route)
    {
        if (route != null && route.isInterplanetaryRoute)
        {
            interplanetaryTradeRoutes.Add(route);
        }
    }
    
    /// <summary>
    /// Get all interplanetary trade routes for this civilization
    /// </summary>
    public List<TradeRoute> GetInterplanetaryTradeRoutes()
    {
        return interplanetaryTradeRoutes;
    }
    
    /// <summary>
    /// Get total gold income from all interplanetary trade routes
    /// </summary>
    public int GetInterplanetaryTradeIncome()
    {
        int totalGold = 0;
        foreach (var route in interplanetaryTradeRoutes)
        {
            if (route != null && route.isInterplanetaryRoute)
                totalGold += route.goldPerTurn;
        }
        return totalGold;
    }
    
    /// <summary>
    /// Remove resources from the civilization's stockpile
    /// </summary>
    public bool ConsumeResource(ResourceData resource, int amount)
    {
        if (resource == null || amount <= 0) return true; // Nothing to consume
        
        if (!resourceStockpile.ContainsKey(resource) || resourceStockpile[resource] < amount)
            return false; // Not enough resources
            
        resourceStockpile[resource] -= amount;
        
        // Notify any UI or other systems
        OnResourceChanged?.Invoke(resource, resourceStockpile[resource]);
        return true;
    }
    
    /// <summary>
    /// Get current amount of a resource in stockpile
    /// </summary>
    public int GetResourceCount(ResourceData resource)
    {
        if (resource == null || !resourceStockpile.ContainsKey(resource))
            return 0;
            
        return resourceStockpile[resource];
    }

    /// <summary>
    /// Attempt to found a Pantheon (requires enough faith and prerequisite tech).
    /// </summary>
    public bool FoundPantheon(PantheonData pantheon, BeliefData founderBelief)
    {
        // Check if the Mysticism tech (or equivalent) is researched
        bool hasMysticism = false;
        foreach (var tech in researchedTechs)
        {
            if (tech.unlocksReligion)
            {
                hasMysticism = true;
                break;
            }
        }
        
        if (!hasMysticism)
        {
            Debug.Log($"{civData.civName} cannot found a pantheon: missing required technology.");
            return false;
        }
        
        // Check if already has a pantheon
        if (hasFoundedPantheon || foundedPantheon != null)
        {
            Debug.Log($"{civData.civName} already has a pantheon.");
            return false;
        }
        
        // Check if has enough faith
        if (faith < pantheon.faithCost)
        {
            Debug.Log($"{civData.civName} doesn't have enough faith to found {pantheon.pantheonName}. Needs {pantheon.faithCost}, has {faith}.");
            return false;
        }
        
        // Check if the chosen belief is valid for this pantheon
        bool validBelief = false;
        foreach (var belief in pantheon.possibleFounderBeliefs)
        {
            if (belief == founderBelief)
            {
                validBelief = true;
                break;
            }
        }
        
        if (!validBelief)
        {
            Debug.Log($"{civData.civName} chose an invalid belief for {pantheon.pantheonName}.");
            return false;
        }
        
        // Found the pantheon
        faith -= pantheon.faithCost;
        foundedPantheon = pantheon;
        chosenFounderBelief = founderBelief;
        hasFoundedPantheon = true;
        
        // Apply any faith yield modifiers from the founder belief
        UpdateFaithYieldModifier();
        
        Debug.Log($"{civData.civName} founded the {pantheon.pantheonName} pantheon with the {founderBelief.beliefName} belief.");
        return true;
    }
    
    /// <summary>
    /// Attempt to found a Religion (requires pantheon, holy site, and enough faith).
    /// </summary>
    public bool FoundReligion(ReligionData religion, City holySiteCity)
    {
        // Check prerequisites
        if (!hasFoundedPantheon || foundedPantheon == null)
        {
            Debug.Log($"{civData.civName} cannot found a religion: no pantheon.");
            return false;
        }
        
        if (foundedPantheon != religion.requiredPantheon)
        {
            Debug.Log($"{civData.civName} cannot found {religion.religionName}: wrong pantheon.");
            return false;
        }
        
        if (hasFoundedReligion || foundedReligion != null)
        {
            Debug.Log($"{civData.civName} already has a religion.");
            return false;
        }
        
        if (faith < religion.faithCost)
        {
            Debug.Log($"{civData.civName} doesn't have enough faith. Needs {religion.faithCost}, has {faith}.");
            return false;
        }
        
        // Check if the city has a Holy Site
        bool hasHolySite = false;
        
        // Get the hex tile data for the city's center tile
        var (tileData, isMoonTile) = TileDataHelper.Instance.GetTileData(holySiteCity.centerTileIndex);
        if (tileData != null)
        {
            hasHolySite = tileData.HasHolySite;
        }
        
        if (!hasHolySite)
        {
            Debug.Log($"{civData.civName} cannot found a religion in {holySiteCity.cityName}: no Holy Site district.");
            return false;
        }
        
        // Found the religion
        faith -= religion.faithCost;
        foundedReligion = religion;
        hasFoundedReligion = true;
        
        // Apply any additional faith yield modifiers
        UpdateFaithYieldModifier();

        // Unlock new units and buildings
        if (religion.unlockedUnits != null)
        {
            foreach (var unit in religion.unlockedUnits)
            {
                if (!unlockedCombatUnits.Contains(unit))
                {
                    unlockedCombatUnits.Add(unit);
                    Debug.Log($"{civData.civName} unlocked {unit.unitName} through {religion.religionName}");
                }
            }
        }

        if (religion.unlockedBuildings != null)
        {
            foreach (var building in religion.unlockedBuildings)
            {
                if (!unlockedBuildings.Contains(building))
                {
                    unlockedBuildings.Add(building);
                    Debug.Log($"{civData.civName} unlocked {building.buildingName} through {religion.religionName}");
                }
            }
        }

        // Notify cities to update their available buildings
        foreach (var city in cities)
        {
            city.UpdateAvailableBuildings();
        }
        
        Debug.Log($"{civData.civName} founded {religion.religionName} in {holySiteCity.cityName}.");
        return true;
    }
    
    /// <summary>
    /// Update faith yield modifier based on pantheon and religion beliefs
    /// </summary>
    private void UpdateFaithYieldModifier() // Renaming and repurposing for Beliefs
    {
        // This method will now specifically handle percentage yield modifiers from active Beliefs.
        // Flat bonuses from beliefs (like extraFaithInHolySite) are often handled directly where they apply (e.g., in City or TileData calculations).

        // Reset belief-based modifiers before recalculating, or ensure they are only applied once.
        // For simplicity, let's assume this is called when beliefs change or at turn start *after* other modifiers are set.
        // To avoid double-counting if called multiple times, we might need to store belief-specific modifiers separately
        // or subtract old belief modifiers before adding new ones if beliefs can change.

        // For now, this just adds belief modifiers. Ensure it's called appropriately.

        if (hasFoundedPantheon && chosenFounderBelief != null)
        {
            foodModifier += chosenFounderBelief.foodModifier;
            productionModifier += chosenFounderBelief.productionModifier;
            goldModifier += chosenFounderBelief.goldModifier;
            scienceModifier += chosenFounderBelief.scienceModifier;
            cultureModifier += chosenFounderBelief.cultureModifier;
            faithModifier += chosenFounderBelief.faithModifier;
        }
        
        if (hasFoundedReligion && foundedReligion != null && foundedReligion.founderBelief != null) // The religion's own founder belief
        {
             foodModifier += foundedReligion.founderBelief.foodModifier;
             productionModifier += foundedReligion.founderBelief.productionModifier;
             goldModifier += foundedReligion.founderBelief.goldModifier;
             scienceModifier += foundedReligion.founderBelief.scienceModifier;
             cultureModifier += foundedReligion.founderBelief.cultureModifier;
             faithModifier += foundedReligion.founderBelief.faithModifier;
        }
        
        // TODO: Consider Enhancer Beliefs if they also provide civ-wide percentage yield bonuses.
        // if (hasFoundedReligion && foundedReligion != null && foundedReligion.enhancerBeliefs != null) {
        //     foreach (var enhancerBelief in foundedReligion.enhancerBeliefs) {
        //         if (enhancerBelief != null && IsEnhancerBeliefActive(enhancerBelief)) { // Need logic for IsEnhancerBeliefActive
        //             foodModifier += enhancerBelief.foodModifier;
        //             // ... and so on for other modifiers
        //         }
        //     }
        // }
    }
    
    /// <summary>
    /// Purchase a missionary unit with faith in the specified city
    /// </summary>
    public bool PurchaseMissionary(ReligionUnitData missionaryData, City city)
    {
        if (!hasFoundedReligion || foundedReligion == null)
        {
            Debug.Log($"{civData.civName} cannot purchase a missionary: no founded religion.");
            return false;
        }
        
        if (faith < missionaryData.faithCost)
        {
            Debug.Log($"{civData.civName} doesn't have enough faith. Needs {missionaryData.faithCost}, has {faith}.");
            return false;
        }
        
        // Check if the city has a Holy Site
        bool hasHolySite = false;
        
        // Get the hex tile data for the city's center tile
        var (tileData, isMoonTile) = TileDataHelper.Instance.GetTileData(city.centerTileIndex);
        if (tileData != null)
        {
            hasHolySite = tileData.HasHolySite;
        }
        
        if (!hasHolySite)
        {
            Debug.Log($"{civData.civName} cannot purchase a missionary in {city.cityName}: no Holy Site district.");
            return false;
        }
        
        // Deduct faith cost
        faith -= missionaryData.faithCost;
        
        // Instantiate the missionary unit
        var grid = planetGenerator != null ? planetGenerator.Grid : null;
        if (grid != null)
        {
            Vector3 pos = TileDataHelper.Instance.GetTileSurfacePosition(city.centerTileIndex, 0.5f);
            var missionaryGO = Instantiate(missionaryData.prefab, pos, Quaternion.identity);
            var missionaryUnit = missionaryGO.GetComponent<CombatUnit>();
            missionaryUnit.Initialize(missionaryData, this);
            combatUnits.Add(missionaryUnit);
            
            // The missionary unit should have the civilization's religion associated with it
            // This would be handled by a specialized ReligionUnit component or by adding properties to CombatUnit
            
            Debug.Log($"{civData.civName} purchased a missionary in {city.cityName}.");
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Get the faith cost to found a pantheon, taking into account any modifiers
    /// </summary>
    public int GetPantheonCost(PantheonData pantheon)
    {
        if (pantheon == null) return 0;
        return pantheon.faithCost;
    }
    
    /// <summary>
    /// Get the faith cost to found a religion, taking into account any modifiers
    /// </summary>
    public int GetReligionCost(ReligionData religion)
    {
        if (religion == null) return 0;
        return religion.faithCost;
    }

    public void HandleTechResearched(TechData tech)  // Renamed from OnTechResearched
    {
        if (tech == null) return;

        // Add tech to researched list if not already there
        if (!researchedTechs.Contains(tech))
            researchedTechs.Add(tech);

        // Apply tech bonuses
        ApplyTechBonuses(tech);

        // Add any unlocked equipment to inventory
        AddUnlockedEquipment(tech);

        // Update city models if this tech changes the age
        UpdateCityModelsForNewAge();

        // Invoke the event
        OnTechResearched?.Invoke(tech);

        // Refresh derived stats and caches across the civ after research completes
        try
        {
            // Units/workers: update only health cap safely (do not refill points mid-turn)
            if (combatUnits != null)
                foreach (var u in combatUnits)
                    if (u != null) u.OnCivBonusesChanged();
            if (workerUnits != null)
                foreach (var w in workerUnits)
                    if (w != null) w.OnCivBonusesChanged();

            // Cities: invalidate caches and update available buildings (for new unlocks)
            if (cities != null)
                foreach (var c in cities)
                    if (c != null)
                    {
                        c.RefreshGovernorBonuses();
                        c.UpdateAvailableBuildings();
                    }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Civilization] Refresh after tech research threw: {ex}");
        }

    // Notify listeners that unlock-driven availability may have changed
    OnUnlocksChanged?.Invoke();
    }
    
    /// <summary>
    /// Called when a culture is fully adopted (e.g., by CultureManager)
    /// </summary>
    public void OnCultureAdopted(CultureData cult)
    {
        if (cult == null) return;

        // Add to researched cultures if not already there
        if (!researchedCultures.Contains(cult))
        {
            researchedCultures.Add(cult);
        }

        // Apply bonuses from the adopted culture
        ApplyCultureBonuses(cult);

        // Trigger the event for other systems (like UI) to update
        OnCultureCompleted?.Invoke(cult); 

        // Cities might need to update their buildable units/buildings if culture unlocks them
        if (cities != null)
        {
            foreach (var city in cities)
            {
                if (city != null)
                {
                    city.UpdateAvailableBuildings(); // And potentially units
                }
            }
        }

        // Refresh derived stats and caches across the civ after culture adoption
        try
        {
            if (combatUnits != null)
                foreach (var u in combatUnits)
                    if (u != null) u.OnCivBonusesChanged();
            if (workerUnits != null)
                foreach (var w in workerUnits)
                    if (w != null) w.OnCivBonusesChanged();
            if (cities != null)
                foreach (var c in cities)
                    if (c != null) c.RefreshGovernorBonuses();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Civilization] Refresh after culture adoption threw: {ex}");
        }

    // Notify listeners that unlock-driven availability may have changed
    OnUnlocksChanged?.Invoke();
    }

    // --- NEW: Equipment Inventory Methods ---
    
    /// <summary>
    /// Add equipment to the civilization's inventory
    /// </summary>
    public void AddEquipment(EquipmentData equipment, int count = 1)
    {
        if (equipment == null || count <= 0) return;
        
        if (!equipmentInventory.ContainsKey(equipment))
            equipmentInventory[equipment] = 0;
            
        equipmentInventory[equipment] += count;
        
        // Notify listeners
        OnEquipmentChanged?.Invoke(equipment, equipmentInventory[equipment]);
        
        Debug.Log($"{civData.civName} added {count} {equipment.equipmentName} to inventory. Now have {equipmentInventory[equipment]}");
    }
    
    /// <summary>
    /// Consume equipment from the civilization's inventory
    /// </summary>
    public bool ConsumeEquipment(EquipmentData equipment, int count = 1)
    {
        if (equipment == null || count <= 0) return true; // Nothing to consume
        
        if (!equipmentInventory.ContainsKey(equipment) || equipmentInventory[equipment] < count)
        {
            Debug.LogWarning($"{civData.civName} does not have enough {equipment.equipmentName} in inventory");
            return false; // Not enough equipment
        }
            
        equipmentInventory[equipment] -= count;
        
        // Notify listeners
        OnEquipmentChanged?.Invoke(equipment, equipmentInventory[equipment]);
        
        Debug.Log($"{civData.civName} consumed {count} {equipment.equipmentName} from inventory. {equipmentInventory[equipment]} remaining");
        return true;
    }
    
    /// <summary>
    /// Get the count of a specific equipment in inventory
    /// </summary>
    public int GetEquipmentCount(EquipmentData equipment)
    {
        if (equipment == null || !equipmentInventory.ContainsKey(equipment))
            return 0;
            
        return equipmentInventory[equipment];
    }
    
    /// <summary>
    /// Check if the civilization has enough of the equipment in inventory
    /// </summary>
    public bool HasEquipment(EquipmentData equipment, int count = 1)
    {
        return GetEquipmentCount(equipment) >= count;
    }
    
    /// <summary>
    /// Equip a unit with an item from the civilization's inventory
    /// </summary>
    public bool EquipUnit(CombatUnit unit, EquipmentData equipment)
    {
        if (unit == null || equipment == null)
            return false;
            
        // Check if the unit belongs to this civilization
        if (!combatUnits.Contains(unit))
        {
            Debug.LogWarning($"Cannot equip unit: {unit.name} does not belong to {civData.civName}");
            return false;
        }
        
        // Check if we have the equipment in stock
        if (!HasEquipment(equipment))
        {
            Debug.LogWarning($"Cannot equip unit: {civData.civName} does not have {equipment.equipmentName} in inventory");
            return false;
        }
        
        // Get the currently equipped item of this type (if any)
        EquipmentData currentEquipment = null;
        
        switch (equipment.equipmentType)
        {
            case EquipmentType.Weapon:
                currentEquipment = unit.Weapon;
                break;
            case EquipmentType.Shield:
                currentEquipment = unit.Shield;
                break;
            case EquipmentType.Armor:
                currentEquipment = unit.Armor;
                break;
            case EquipmentType.Miscellaneous:
                currentEquipment = unit.Miscellaneous;
                break;
        }
        
        // Return the existing equipment to inventory if any
        if (currentEquipment != null)
        {
            AddEquipment(currentEquipment);
        }
        
        // Consume the new equipment from inventory
        ConsumeEquipment(equipment);
        
        // Equip the unit with the new item
        unit.EquipItem(equipment);
        
        Debug.Log($"Equipped {unit.data.unitName} with {equipment.equipmentName}");
        return true;
    }
    
    /// <summary>
    /// Get equipment from the unit and return it to inventory
    /// </summary>
    public void UnequipUnit(CombatUnit unit, EquipmentType equipmentType)
    {
        if (unit == null)
            return;
            
        // Check if the unit belongs to this civilization
        if (!combatUnits.Contains(unit))
        {
            Debug.LogWarning($"Cannot unequip unit: {unit.name} does not belong to {civData.civName}");
            return;
        }
        
        // Get the currently equipped item of this type (if any)
        EquipmentData currentEquipment = null;
        
        switch (equipmentType)
        {
            case EquipmentType.Weapon:
                currentEquipment = unit.Weapon;
                break;
            case EquipmentType.Shield:
                currentEquipment = unit.Shield;
                break;
            case EquipmentType.Armor:
                currentEquipment = unit.Armor;
                break;
            case EquipmentType.Miscellaneous:
                currentEquipment = unit.Miscellaneous;
                break;
        }
        
        // Return the existing equipment to inventory if any
        if (currentEquipment != null)
        {
            AddEquipment(currentEquipment);
            unit.UnequipItem(equipmentType);
            Debug.Log($"Unequipped {equipmentType} from {unit.data.unitName} and returned to {civData.civName} inventory");
        }
    }
    
    /// <summary>
    /// Called when a tech is researched - add any equipment unlocked by the tech to inventory
    /// </summary>
    public void AddUnlockedEquipment(TechData tech, int count = 3)
    {
        // Add new equipment types unlocked by this tech to the civilization's inventory
        foreach (var equipment in tech.unlockedEquipment)
        {
            AddEquipment(equipment, count);
        }
    }

    /// <summary>
    /// Creates a new city for this civilization at the specified tile.
    /// This is now the primary method for founding cities.
    /// </summary>
    /// <param name="tileIndex">The tile where the city will be founded.</param>
    public void FoundNewCity(int tileIndex, SphericalHexGrid gridOverride = null, PlanetGenerator planetOverride = null)
    {
        Debug.Log($"[FoundNewCity] Called for civ {civData?.civName ?? "NULL"} at tile {tileIndex}. cityPrefab={(cityPrefab != null ? cityPrefab.name : "NULL")}");
        if (cityPrefab == null)
        {
            Debug.LogError("[FoundNewCity] City prefab not assigned to civilization!");
            return;
        }

        // Create the city game object from prefab (model and logic are the same)
        GameObject cityGO = null;
        try {
            cityGO = Instantiate(cityPrefab);
            Debug.Log($"[FoundNewCity] Instantiated city prefab: {cityGO?.name ?? "NULL"}");
        } catch (System.Exception ex) {
            Debug.LogError($"[FoundNewCity] Exception during Instantiate: {ex}");
            return;
        }
        if (cityGO == null)
        {
            Debug.LogError("[FoundNewCity] Instantiated city GameObject is null!");
            return;
        }

        City newCity = cityGO.GetComponent<City>();
        if (newCity == null)
        {
            Debug.LogError("[FoundNewCity] City prefab is missing the City component!");
            Destroy(cityGO);
            return;
        }
        Debug.Log($"[FoundNewCity] City component found on prefab: {newCity}");

        // Set references for correct world context
        SphericalHexGrid gridToUse = gridOverride ?? planetGrid;
        PlanetGenerator planetToUse = planetOverride ?? planetGenerator;
        if (gridToUse == null) {
            var currentPlanet = GameManager.Instance?.GetCurrentPlanetGenerator();
            gridToUse = currentPlanet?.Grid;
        }
        if (planetToUse == null)
            planetToUse = GameManager.Instance?.GetCurrentPlanetGenerator();
        // City class sets its own references now
        Debug.Log($"[FoundNewCity] Grid={gridToUse}, Planet={planetToUse}");

        // --- Position and orient the city on the correct tile ---
        if (gridToUse != null)
        {
            Vector3 tileCenter = gridToUse.tileCenters[tileIndex];
            Vector3 planetCenter = planetToUse.transform.position;
            Vector3 surfaceNormal = (tileCenter - planetCenter).normalized;
            float planetRadius = planetToUse.transform.localScale.x * 0.5f;
            
            // Extrusion logic removed: surface position now uses only radius and baseOffset
            float baseOffset = 0.1f; // Slightly above surface
            Vector3 surfacePosition = planetCenter + surfaceNormal * (planetRadius + baseOffset);
            cityGO.transform.position = surfacePosition;
            Debug.Log($"[FoundNewCity] City positioned at {surfacePosition}");

            // Orient city to stand upright on the surface
            Vector3 planetUp = planetToUse.transform.up;
            Vector3 right = Vector3.Cross(planetUp, surfaceNormal);
            if (right.sqrMagnitude < 0.01f)
            {
                right = Vector3.Cross(Vector3.forward, surfaceNormal);
                if (right.sqrMagnitude < 0.01f)
                {
                    right = Vector3.Cross(Vector3.right, surfaceNormal);
                }
            }
            right.Normalize();
            Vector3 forward = Vector3.Cross(right, surfaceNormal).normalized;
            cityGO.transform.rotation = Quaternion.LookRotation(forward, surfaceNormal);
            Debug.Log($"[FoundNewCity] City rotation set. Forward={forward}, Up={surfaceNormal}");
        }
        else
        {
            Debug.LogWarning("[FoundNewCity] gridToUse is null, city will not be positioned correctly!");
        }

        // --- Determine City Name ---
        string cityName;
        var existingCityNames = cities.Select(c => c.gameObject.name).ToList(); 
        string newNameFromList = civData.cityNames?.FirstOrDefault(name => !existingCityNames.Contains(name));
        if (!string.IsNullOrEmpty(newNameFromList))
        {
            cityName = newNameFromList;
        }
        else
        {
            if (cities.Count == 0 && !string.IsNullOrEmpty(civData.civName))
            {
                cityName = civData.civName;
            }
            else
            {
                cityName = $"{civData.civName} City {cities.Count + 1}";
            }
        }
        newCity.centerTileIndex = tileIndex;
        newCity.Initialize(cityName, this);
        Debug.Log($"[FoundNewCity] City initialized with name: {cityName}, owner: {this.civData.civName}");
        Debug.Log($"[FoundNewCity] City GameObject active: {cityGO.activeSelf}, position: {cityGO.transform.position}, scale: {cityGO.transform.localScale}");
        Debug.Log($"[FoundNewCity] Adding city to civilization's city list...");
        AddCity(newCity);
        Debug.Log($"[FoundNewCity] City added to civilization. Total cities: {cities.Count}");
    }

    public List<EquipmentData> GetAvailableEquipment()
    {
        return equipmentInventory.Keys.ToList();
    }

    // --- Improvements availability & obsolescence helpers ---
    /// <summary>
    /// Returns all improvements unlocked by researched technologies.
    /// Note: Cultures currently do not unlock improvements directly.
    /// </summary>
    public List<ImprovementData> GetUnlockedImprovements()
    {
        var result = new HashSet<ImprovementData>();
        if (researchedTechs != null)
        {
            foreach (var t in researchedTechs)
            {
                if (t?.unlocksImprovements == null) continue;
                foreach (var imp in t.unlocksImprovements)
                    if (imp != null) result.Add(imp);
            }
        }
        return result.ToList();
    }

    /// <summary>
    /// For a given worker archetype, compute which improvements should be considered obsolete
    /// because the civ has unlocked a replacement that this worker can also build.
    /// </summary>
    public HashSet<ImprovementData> GetObsoleteImprovementsForWorker(WorkerUnitData worker)
    {
        var obsolete = new HashSet<ImprovementData>();
        if (worker == null || worker.buildableImprovements == null) return obsolete;

        // Consider only replacements that are both unlocked AND buildable by this worker
        var unlocked = GetUnlockedImprovements();

        foreach (var replacement in unlocked)
        {
            if (replacement == null) continue;

            // Worker must be able to build the replacement
            bool workerCanBuildReplacement = System.Array.Exists(worker.buildableImprovements, i => i == replacement);
            if (!workerCanBuildReplacement) continue;

            // Any improvement listed in replacement.replacesImprovements becomes obsolete for this worker
            if (replacement.replacesImprovements != null)
            {
                foreach (var old in replacement.replacesImprovements)
                {
                    if (old != null)
                        obsolete.Add(old);
                }
            }
        }

        return obsolete;
    }

    /// <summary>
    /// Get the list of improvements this worker can currently build, filtered to remove obsolete ones.
    /// If tileIndex is provided, also filters by tile land/biome requirements.
    /// </summary>
    public List<ImprovementData> GetAvailableImprovementsForWorker(WorkerUnitData worker, int tileIndex = -1)
    {
        var list = new List<ImprovementData>();
        if (worker == null || worker.buildableImprovements == null) return list;

        var obsolete = GetObsoleteImprovementsForWorker(worker);

        // Optional tile filter
        HexTileData tileData = null;
        if (tileIndex >= 0)
        {
            var (td, _) = TileDataHelper.Instance.GetTileData(tileIndex);
            tileData = td;
        }

        // Precompute unlocked replacements for tile-aware obsolescence
        var unlocked = GetUnlockedImprovements();

        foreach (var imp in worker.buildableImprovements)
        {
            if (imp == null) continue;

            // Tile filters
            if (tileData != null)
            {
                if (!tileData.isLand) continue;
                if (imp.allowedBiomes != null && imp.allowedBiomes.Length > 0)
                {
                    bool allowed = System.Array.IndexOf(imp.allowedBiomes, tileData.biome) >= 0;
                    if (!allowed) continue;
                }
            }

            // Obsolescence: if a replacement is unlocked AND valid for this worker AND allowed on this tile, hide this improvement
            bool obsoleteHere = false;
            if (imp != null && unlocked != null)
            {
                foreach (var repl in unlocked)
                {
                    if (repl == null || repl.replacesImprovements == null) continue;
                    // Is 'imp' listed as replaced by 'repl'?
                    bool replacesThis = System.Array.IndexOf(repl.replacesImprovements, imp) >= 0;
                    if (!replacesThis) continue;

                    // Can this worker build the replacement?
                    bool workerCanBuildReplacement = System.Array.Exists(worker.buildableImprovements, i => i == repl);
                    if (!workerCanBuildReplacement) continue;

                    // If tile specified, ensure replacement is allowed here
                    if (tileData != null && repl.allowedBiomes != null && repl.allowedBiomes.Length > 0)
                    {
                        bool replAllowed = System.Array.IndexOf(repl.allowedBiomes, tileData.biome) >= 0;
                        if (!replAllowed) continue;
                    }

                    obsoleteHere = true;
                    break;
                }
            }

            if (obsoleteHere) continue;

            list.Add(imp);
        }

        return list;
    }

    public TechAge GetCurrentAge()
    {
        // If no techs researched, default to Paleolithic (first defined age)
        if (researchedTechs == null || researchedTechs.Count == 0)
            return TechAge.PaleolithicAge;

        TechAge maxAge = TechAge.PaleolithicAge;
        foreach (var tech in researchedTechs)
        {
            if (tech != null && tech.techAge > maxAge)
                maxAge = tech.techAge;
        }
        return maxAge;
    }

    public void Initialize(CivData data, LeaderData leaderData, bool isPlayer, SphericalHexGrid grid = null, PlanetGenerator planet = null)
    {
        civData = data;
        leader = leaderData; // Set the leader for this civilization instance
        isPlayerControlled = isPlayer;
        // Use GameManager API for multi-planet support
        planetGenerator = planet ?? GameManager.Instance?.GetCurrentPlanetGenerator();
        planetGrid = grid ?? planetGenerator?.Grid;
        
        // --- Ensure cityPrefab is set from CivData ---
        if (cityPrefab == null && civData != null && civData.cityPrefabsByAge != null && civData.cityPrefabsByAge.Length > 0)
        {
            // Choose the prefab that matches the starting tech age (assumed first entry)
            var prefabEntry = civData.cityPrefabsByAge[0];
            if (prefabEntry != null)
            {
                cityPrefab = prefabEntry.cityPrefab;
                if (cityPrefab == null)
                {
                    Debug.LogWarning($"[{civData.civName}] City prefab entry for starting age is null!");
                }
            }
        }
        
        // Use the new 'leader' field for initialization
        InitializeLeaderUniques();
        ApplyLeaderBonuses();

        // Initialize starting equipment
        if (startingEquipment != null)
        {
            foreach(var item in startingEquipment)
            {
                AddEquipment(item, 5); // Start with a default quantity
            }
        }
    }

    public SphericalHexGrid planetGrid; // Add this field to store the main planet's grid
    public PlanetGenerator planetGenerator; // Add this field to store the main planet's generator
}

