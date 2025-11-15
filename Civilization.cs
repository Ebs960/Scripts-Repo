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
    
    // Performance caches
    private Dictionary<CombatUnitData, bool> _unitAvailabilityCache = new Dictionary<CombatUnitData, bool>();
    private Dictionary<WorkerUnitData, bool> _workerAvailabilityCache = new Dictionary<WorkerUnitData, bool>();
    private Dictionary<BuildingData, bool> _buildingAvailabilityCache = new Dictionary<BuildingData, bool>();
    private Dictionary<EquipmentData, bool> _equipmentAvailabilityCache = new Dictionary<EquipmentData, bool>();
    private bool _availabilityCacheDirty = true;
    
    // Runtime property for diplomatic state access
    public bool isPlayerControlled = false;

    [Header("Map & Military")]
    public List<int> ownedTileIndices       = new List<int>();
    public List<City> cities                = new List<City>();
    public List<CombatUnit> combatUnits     = new List<CombatUnit>();
    public List<WorkerUnit> workerUnits     = new List<WorkerUnit>();
    
    [Header("Interplanetary Trade")]
    public List<TradeRoute> interplanetaryTradeRoutes = new List<TradeRoute>();
    
    [Header("Trade System")]
    [Tooltip("When true this civilization may initiate trade routes (set when adopting certain cultures)")]
    public bool tradeEnabled = false;
    
    [Header("Resources")]
    public Dictionary<ResourceData, int> resourceStockpile = new Dictionary<ResourceData, int>();
    
    [Header("Equipment Inventory")]
    // Track equipment availability - each civ has stockpiles of equipment
    public Dictionary<EquipmentData, int> equipmentInventory = new Dictionary<EquipmentData, int>();
    // Starting equipment to spawn with
    [SerializeField] private List<EquipmentData> startingEquipment = new List<EquipmentData>();
    [Tooltip("The base prefab used to create a new city. The City script on this prefab will handle spawning the correct visual model based on tech age.")]
    [SerializeField] private GameObject cityPrefab;
    
    [Header("Projectile Inventory")]
    // Track projectile availability - each civ has stockpiles of different projectile types
    public Dictionary<GameCombat.ProjectileData, int> projectileInventory = new Dictionary<GameCombat.ProjectileData, int>();
    // Event for projectile changes
    public System.Action<GameCombat.ProjectileData, int> OnProjectileChanged;

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
    
    [Header("Consumption Settings")]
    [Tooltip("Minimum food stockpile (prevents going below zero with buffer)")]
    public int minimumFoodStockpile = -10;
    [Tooltip("Fallback food consumption for units without foodConsumptionPerTurn set")]
    public int defaultFoodPerCombatUnit = 2;
    [Tooltip("Fallback food consumption for workers without foodConsumptionPerTurn set")]
    public int defaultFoodPerWorkerUnit = 1;

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
    // Support multiple pantheons (spirits/gods). Key: the pantheon asset, Value: chosen founder belief for that pantheon
    public List<PantheonData> foundedPantheons = new List<PantheonData>();
    public Dictionary<PantheonData, BeliefData> chosenFounderBeliefs = new Dictionary<PantheonData, BeliefData>();
    public ReligionData foundedReligion;
    public bool hasFoundedReligion;
    // Pantheons/beliefs unlocked by adopted cultures (in addition to global available list)
    public List<PantheonData> cultureUnlockedPantheons = new List<PantheonData>();
    public List<BeliefData> cultureUnlockedBeliefs = new List<BeliefData>();

    [Header("Pantheon Limits")]
    [Tooltip("Base maximum number of pantheons this civilization may found (default 1).")]
    public int basePantheonCap = 1;
    [Tooltip("Additional pantheon capacity gained from techs/cultures/policies (computed at runtime)")]
    public int pantheonCapFromBonuses = 0;

    public int CurrentPantheonCap => Mathf.Max(0, basePantheonCap + pantheonCapFromBonuses);

    public bool CanFoundMorePantheons()
    {
    // Count actual founded pantheons
    int owned = (foundedPantheons != null) ? foundedPantheons.Count : 0;
        return owned < CurrentPantheonCap;
    }


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
    // Whether this civilization has the governor feature unlocked (via cultures/policies/tech)
    [Tooltip("If true this civilization may create and assign governors.")]
    public bool governorsEnabled = false;

    // List of governor traits this civ has unlocked (for trait assignment UI)
    public List<GovernorTrait> unlockedGovernorTraits = new List<GovernorTrait>();
    
    [Header("City Cap")]
    [Tooltip("Base maximum number of cities this civilization may own. Set to 0 for Paleolithic nomads.")]
    [SerializeField] private int baseCityCap = 0;
    [Tooltip("Additional city capacity gained from technologies, policies, etc. Computed at runtime.")]
    [SerializeField] private int cityCapFromBonuses = 0;
    /// <summary>
    /// Current max cities allowed = base + bonuses. Default 0 so early ages are nomadic.
    /// </summary>
    public int CurrentCityCap => Mathf.Max(0, baseCityCap + cityCapFromBonuses);
    /// <summary>
    /// Returns true if this civ may found/own another city given the cap.
    /// </summary>
    public bool CanFoundMoreCities() => cities == null || cities.Count < CurrentCityCap;
    

    // Increase the number of governors this civ can create
    public void IncreaseGovernorCount(int amount = 1)
    {
        governorCount += amount;
    }

    // Create a new governor if there is an available slot
    public Governor CreateGovernor(string name, Governor.Specialization specialization)
    {
    if (!governorsEnabled) return null;
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
    if (!governorsEnabled) return false;
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

        // Register with the turn order (only if CivilizationManager exists)
        if (CivilizationManager.Instance != null)
        {
            CivilizationManager.Instance.RegisterCiv(this);
        }

        // If loading from a save or starting with pre-researched cultures, ensure governorsEnabled reflects those cultures
        if (!governorsEnabled && researchedCultures != null)
        {
            foreach (var cult in researchedCultures)
            {
                if (cult != null && cult.enablesGovernors)
                {
                    governorsEnabled = true;
                    break;
                }
            }
        }
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
        try
        {
            turnCount = round;

            // 1) Reset units
            foreach (var u in combatUnits) 
            {
                if (u != null) u.ResetForNewTurn();
            }
            foreach (var w in workerUnits)  
            {
                if (w != null) w.ResetForNewTurn();
            }

            // 2) Process each city (production, growth, morale, surrender, label)
            foreach (var city in cities)
            {
                if (city != null)
                {
                    try
                    {
                        city.ProcessCityTurn();
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[Civilization] Error processing city {city.cityName}: {e.Message}");
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Civilization] Error in BeginTurn for {civData?.civName ?? "Unknown"}: {e.Message}");
        }

        // 3) Collect city yields into storage
        foreach (var city in cities)
        {
            if (city != null)
            {
                try
                {
                    gold         += Mathf.RoundToInt(city.GetGoldPerTurn() * (1 + goldModifier));
                    food         += Mathf.RoundToInt(city.GetFoodPerTurn() * (1 + foodModifier));
                    science      += Mathf.RoundToInt(city.GetSciencePerTurn() * (1 + scienceModifier));
                    culture      += Mathf.RoundToInt(city.GetCulturePerTurn() * (1 + cultureModifier));
                    policyPoints += city.GetPolicyPointPerTurn(); // Assuming no direct modifier for policy points yet
                    faith        += Mathf.RoundToInt(city.GetFaithPerTurn() * (1 + faithModifier));
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[Civilization] Error collecting yields from city {city.cityName}: {e.Message}");
                }
            }
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
        var yields = ComputeUnitPerTurnYield(u.data, u.Weapon, u.Shield, u.Armor, u.Miscellaneous);
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
                var yields = ComputeWorkerPerTurnYield(w.data);
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

        // 3.8) FOOD CONSUMPTION - Units and cities must eat!
        int totalFoodConsumption = 0;
        
        // Combat units consume food based on their data
        if (combatUnits != null)
        {
            foreach (var u in combatUnits)
            {
                if (u != null && u.data != null)
                    totalFoodConsumption += u.data.foodConsumptionPerTurn;
                else
                    totalFoodConsumption += defaultFoodPerCombatUnit; // Fallback
            }
        }
        
        // Worker units consume food based on their data
        if (workerUnits != null)
        {
            foreach (var w in workerUnits)
            {
                if (w != null && w.data != null)
                    totalFoodConsumption += w.data.foodConsumptionPerTurn;
                else
                    totalFoodConsumption += defaultFoodPerWorkerUnit; // Fallback
            }
        }
        
        // Cities consume food based on population size
        if (cities != null)
        {
            foreach (var city in cities)
            {
                if (city != null)
                    totalFoodConsumption += city.GetFoodConsumptionPerTurn();
            }
        }
        
        // Consume food from stockpile
        food -= totalFoodConsumption;
        
        // Clamp to minimum (allows small negative buffer before critical famine)
        if (food < minimumFoodStockpile)
            food = minimumFoodStockpile;

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

        // Check famine: true if food stockpile <= 0 (AFTER consumption)
        famineActive = (food <= 0);
        if (famineActive)
        {
            // Each turn of famine, all units lose 5% max health
            int unitsAffected = 0;
            foreach (var u in combatUnits)
            {
                if (u != null)
                {
                    u.ApplyDamage(Mathf.CeilToInt(u.MaxHealth * 0.05f));
                    unitsAffected++;
                }
            }
            foreach (var w in workerUnits)
            {
                if (w != null && w.data != null)
                {
                    w.ApplyDamage(Mathf.CeilToInt(w.data.baseHealth * 0.05f));
                    unitsAffected++;
                }
            }
            
            // Notify player if this is their civilization
            if (isPlayerControlled && UIManager.Instance != null && unitsAffected > 0)
            {
                UIManager.Instance.ShowNotification($"FAMINE! {civData.civName} has no food. {unitsAffected} units are starving!");
            }
        }
        else if (food < totalFoodConsumption * 2 && isPlayerControlled && UIManager.Instance != null)
        {
            // Warning: food running low (less than 2 turns worth)
            UIManager.Instance.ShowNotification($"Warning: {civData.civName} food reserves are low ({food} remaining)");
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
        // REMOVED: TechData no longer directly unlocks units/buildings
        // Availability is now controlled solely by requiredTechs in the respective data classes
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
        // REMOVED: CultureData no longer directly unlocks units/buildings/abilities
        // REMOVED: CultureData no longer directly unlocks policies
        // Policy availability is now controlled solely by requiredTechs/requiredCultures/requiredGovernments in PolicyData
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
        if (tech == null) return false;
        if (currentTech != null) return false;
        if (researchedTechs.Contains(tech)) return false;
        // if (science <= 0) { Debug.Log($"[Civilization] CanResearch ({tech.techName}): Science output is <= 0."); return false; } // Usually, we allow selection even with 0 science, it just won't progress.

        foreach (var req in tech.requiredTechnologies)
        {
            if (!researchedTechs.Contains(req)) return false;
        }
        foreach (var req in tech.requiredCultures)
        {
            if (!researchedCultures.Contains(req)) return false;
        }
        if (cities.Count < tech.requiredCityCount) return false;
        // Add biome check if needed
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
        if (cult == null) return false;
        if (currentCulture != null) return false;
        if (researchedCultures.Contains(cult)) return false;
        // if (culture <= 0) { Debug.Log($"[Civilization] CanCultivate ({cult.cultureName}): Culture output is <= 0."); return false; }
        foreach (var req in cult.requiredCultures)
        {
            if (!researchedCultures.Contains(req)) return false;
        }
        if (cities.Count < cult.requiredCityCount) return false;
        // Add biome check if needed
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

        // REMOVED: GovernmentData no longer directly unlocks units/buildings
        // Availability is now controlled solely by requiredTechs/requiredCultures in the respective data classes

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

        // REMOVED: GovernmentData no longer directly unlocks policies
        // Policy availability is now controlled solely by requiredTechs/requiredCultures/requiredGovernments in PolicyData
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
        // Check if the civilization meets a pantheon founding prereq:
        // either a tech that unlocks religion or an adopted culture that unlocks pantheons.
        bool hasPantheonPrereq = false;
        if (researchedTechs != null)
        {
            foreach (var tech in researchedTechs)
            {
                if (tech != null && tech.unlocksReligion)
                {
                    hasPantheonPrereq = true;
                    break;
                }
            }
        }
        // Also allow cultures to enable pantheon founding
        if (!hasPantheonPrereq && researchedCultures != null)
        {
            foreach (var cult in researchedCultures)
            {
                if (cult != null && cult.unlocksPantheon)
                {
                    hasPantheonPrereq = true;
                    break;
                }
            }
        }

        if (!hasPantheonPrereq)
        {
            Debug.Log($"{civData.civName} cannot found a pantheon: missing required tech or culture unlock.");
            return false;
        }
        
        // Check pantheon cap
        if (!CanFoundMorePantheons())
        {
            Debug.Log($"{civData.civName} cannot found a pantheon: pantheon cap reached ({CurrentPantheonCap}).");
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
        
    // Found the pantheon: add to list and store chosen belief
    faith -= pantheon.faithCost;
    if (foundedPantheons == null) foundedPantheons = new List<PantheonData>();
    foundedPantheons.Add(pantheon);
    if (chosenFounderBeliefs == null) chosenFounderBeliefs = new Dictionary<PantheonData, BeliefData>();
    chosenFounderBeliefs[pantheon] = founderBelief;

    // Apply any faith yield modifiers from beliefs (recompute across all)
    UpdateFaithYieldModifier();

    Debug.Log($"{civData.civName} founded the {pantheon.pantheonName} pantheon (spirit/god) with the {founderBelief.beliefName} belief.");
    return true;
    }
    
    /// <summary>
    /// Attempt to found a Religion (requires pantheon, holy site, and enough faith).
    /// </summary>
    public bool FoundReligion(ReligionData religion, City holySiteCity)
    {
        // Check prerequisites: civ must have founded the required pantheon
        if (foundedPantheons == null || !foundedPantheons.Contains(religion.requiredPantheon))
        {
            Debug.Log($"{civData.civName} cannot found a religion: required pantheon not owned.");
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
        var tileDataHS = TileSystem.Instance != null ? TileSystem.Instance.GetTileData(holySiteCity.centerTileIndex) : null;
        if (tileDataHS != null)
        {
            hasHolySite = tileDataHS.HasHolySite;
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

        // REMOVED: ReligionData no longer directly unlocks units/buildings
        // Availability is now controlled solely by requiredTechs/requiredCultures in the respective data classes

        // Notify cities to update their available buildings
        foreach (var city in cities)
        {
            city.UpdateAvailableBuildings();
        }
        
        Debug.Log($"{civData.civName} founded {religion.religionName} in {holySiteCity.cityName}.");
        return true;
    }

    /// <summary>
    /// Upgrade an existing founded pantheon (spirit) into its upgraded pantheon (God), if available.
    /// Preserves the chosen founder belief mapping where possible.
    /// </summary>
    public bool UpgradePantheon(PantheonData spiritPantheon)
    {
        if (spiritPantheon == null) return false;
        if (foundedPantheons == null || !foundedPantheons.Contains(spiritPantheon)) return false;
        if (!spiritPantheon.isSpirit || !spiritPantheon.canUpgradeToGod || spiritPantheon.upgradedPantheon == null) return false;

        var god = spiritPantheon.upgradedPantheon;

        // Replace in the list, preserving order (replace first occurrence)
        int idx = foundedPantheons.IndexOf(spiritPantheon);
        if (idx < 0) return false;

        foundedPantheons[idx] = god;

        // Preserve chosen belief mapping if the belief is still valid for the upgraded pantheon,
        // otherwise remove the mapping for that pantheon.
        if (chosenFounderBeliefs != null && chosenFounderBeliefs.TryGetValue(spiritPantheon, out BeliefData oldBelief))
        {
            chosenFounderBeliefs.Remove(spiritPantheon);
            if (oldBelief != null && god.possibleFounderBeliefs != null && System.Array.Exists(god.possibleFounderBeliefs, b => b == oldBelief))
            {
                chosenFounderBeliefs[god] = oldBelief;
            }
        }

        Debug.Log($"{civData.civName} upgraded pantheon {spiritPantheon.pantheonName} -> {god.pantheonName}.");

        // Recompute belief-based modifiers
        UpdateFaithYieldModifier();
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

        // Apply modifiers from all founded pantheons' chosen founder beliefs
        if (foundedPantheons != null && foundedPantheons.Count > 0 && chosenFounderBeliefs != null)
        {
            foreach (var p in foundedPantheons)
            {
                if (p == null) continue;
                if (!chosenFounderBeliefs.TryGetValue(p, out BeliefData b) || b == null) continue;
                foodModifier += b.foodModifier;
                productionModifier += b.productionModifier;
                goldModifier += b.goldModifier;
                scienceModifier += b.scienceModifier;
                cultureModifier += b.cultureModifier;
                faithModifier += b.faithModifier;
            }
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
        var tileDataMS = TileSystem.Instance != null ? TileSystem.Instance.GetTileData(city.centerTileIndex) : null;
        if (tileDataMS != null)
        {
            hasHolySite = tileDataMS.HasHolySite;
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
            Vector3 pos = TileSystem.Instance != null ? TileSystem.Instance.GetTileSurfacePosition(city.centerTileIndex, 0.5f) : Vector3.zero;
            var missionaryPrefab = missionaryData.GetPrefab();
            if (missionaryPrefab == null)
            {
                Debug.LogError($"[Civilization] Cannot spawn missionary {missionaryData.unitName}: prefab not found at path '{missionaryData.prefabPath}'. Check prefabPath in ScriptableObject.");
                return false;
            }
            
            var missionaryGO = Instantiate(missionaryPrefab, pos, Quaternion.identity);
            var missionaryUnit = missionaryGO.GetComponent<CombatUnit>();
            if (missionaryUnit == null)
            {
                Debug.LogError($"[Civilization] Spawned prefab for {missionaryData.unitName} is missing CombatUnit component.");
                Destroy(missionaryGO);
                return false;
            }
            missionaryUnit.Initialize(missionaryData, this);
            
            // Add unit to army system
            if (missionaryUnit.currentTileIndex >= 0)
            {
                ArmyIntegration.OnUnitCreated(missionaryUnit, missionaryUnit.currentTileIndex);
            }
            else
            {
                missionaryUnit.currentTileIndex = city.centerTileIndex;
                ArmyIntegration.OnUnitCreated(missionaryUnit, city.centerTileIndex);
            }
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
        
        // City-cap increase from this technology (enables settlement when first >0 is researched)
        if (tech.cityCapIncrease != 0)
        {
            cityCapFromBonuses = Mathf.Max(0, cityCapFromBonuses + tech.cityCapIncrease);
        }
        // Pantheon cap increase
        if (tech.pantheonCapIncrease != 0)
        {
            pantheonCapFromBonuses = Mathf.Max(0, pantheonCapFromBonuses + tech.pantheonCapIncrease);
        }

        // REMOVED: AddUnlockedEquipment(tech) - Equipment no longer auto-added to inventory
        // Equipment availability is now controlled solely by EquipmentData.requiredTechs
        // Players must produce equipment they want (via cities or other means)

        // Update city models if this tech changes the age
        UpdateCityModelsForNewAge();

        // Invalidate availability cache
        InvalidateAvailabilityCache();

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

    // Ensure flat all-workers work point bonuses are applied to already-spawned workers
    ApplyAllWorkersWorkPointsToExisting();

    // Notify listeners that unlock-driven availability may have changed
    OnUnlocksChanged?.Invoke();
    }

    // Compute aggregated flat work points granted to ALL workers by techs/cultures/policies/government
    public int GetAggregatedAllWorkersWorkPoints()
    {
        int total = 0;
        if (researchedTechs != null)
        {
            foreach (var t in researchedTechs)
                if (t != null) total += t.allWorkersWorkPoints;
        }
        if (researchedCultures != null)
        {
            foreach (var c in researchedCultures)
                if (c != null) total += c.allWorkersWorkPoints;
        }
    // Note: policies and government currently do not expose allWorkersWorkPoints
    // If they gain that field in the future, include them here.
        return total;
    }

    // Apply current aggregated all-worker flat bonuses to all existing WorkerUnit instances
    private void ApplyAllWorkersWorkPointsToExisting()
    {
        int flat = GetAggregatedAllWorkersWorkPoints();
        if (flat == 0) return;
        if (workerUnits == null) return;

        foreach (var w in workerUnits)
        {
            if (w == null) continue;
            // Prefer an explicit API on WorkerUnit to receive civ-level updates
            try {
                w.OnCivBonusesChanged(); // allow worker to recompute its effective work points
                // Also ensure any persistent field is adjusted if WorkerUnit exposes one
                // e.g., if WorkerUnit has AddTemporaryWorkPoints(int), call it here. We'll rely on OnCivBonusesChanged for now.
            } catch (System.Exception ex) {
                Debug.LogWarning($"[Civilization] Failed to apply allWorkersWorkPoints to worker {w.name}: {ex}");
            }
        }
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

        // Invalidate availability cache
        InvalidateAvailabilityCache();

        // Apply culture unlocks for religion/pantheons
        if (cult.unlocksPantheons != null)
        {
            if (cultureUnlockedPantheons == null) cultureUnlockedPantheons = new List<PantheonData>();
            foreach (var p in cult.unlocksPantheons)
            {
                if (p != null && !cultureUnlockedPantheons.Contains(p)) cultureUnlockedPantheons.Add(p);
            }
        }
        if (cult.unlocksBeliefs != null)
        {
            if (cultureUnlockedBeliefs == null) cultureUnlockedBeliefs = new List<BeliefData>();
            foreach (var b in cult.unlocksBeliefs)
            {
                if (b != null && !cultureUnlockedBeliefs.Contains(b)) cultureUnlockedBeliefs.Add(b);
            }
        }

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

    // Ensure flat all-workers work point bonuses are applied to already-spawned workers
    ApplyAllWorkersWorkPointsToExisting();

    // Notify listeners that unlock-driven availability may have changed
    OnUnlocksChanged?.Invoke();

        // If this culture enables the trade system, enable it for this civ and notify player
        if (cult.enablesTradeSystem)
        {
            tradeEnabled = true;
            UIManager.Instance?.ShowNotification($"{civData.civName} has unlocked the Trade system!");
        }
        // If this culture enables the governor mechanic, enable it for this civ and notify player
        if (cult.enablesGovernors)
        {
            governorsEnabled = true;
            UIManager.Instance?.ShowNotification($"{civData.civName} has unlocked Governors!");
        }
        // Apply pantheon cap increase from culture
        if (cult.pantheonCapIncrease != 0)
        {
            pantheonCapFromBonuses = Mathf.Max(0, pantheonCapFromBonuses + cult.pantheonCapIncrease);
        }
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
    /// Produce equipment and add it to inventory (consumes production cost)
    /// </summary>
    public bool ProduceEquipment(EquipmentData equipment, int count = 1)
    {
        if (equipment == null || count <= 0) return false;
        
        // Check if we can produce this equipment
        if (!equipment.CanBeProducedBy(this))
        {
            Debug.LogWarning($"{civData.civName} cannot produce {equipment.equipmentName} - requirements not met");
            return false;
        }
        
        // Calculate total cost
        int totalCost = equipment.productionCost * count;
        
        // Check if we have enough gold
        if (totalCost > 0 && gold < totalCost)
        {
            Debug.LogWarning($"{civData.civName} cannot produce {equipment.equipmentName} - not enough gold ({gold}/{totalCost})");
            return false;
        }
        
        // Deduct production cost
        if (totalCost > 0)
        {
            gold -= totalCost;
            Debug.Log($"{civData.civName} spent {totalCost} gold to produce {count} {equipment.equipmentName}");
        }
        
        // Add equipment to inventory
        AddEquipment(equipment, count);
        
        return true;
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
    
    // ===================== PROJECTILE INVENTORY MANAGEMENT =====================
    
    /// <summary>
    /// Adds projectiles to the civilization's inventory
    /// </summary>
    public void AddProjectile(GameCombat.ProjectileData projectile, int count = 1)
    {
        if (projectile == null || count <= 0) return;
        
        if (!projectileInventory.ContainsKey(projectile))
            projectileInventory[projectile] = 0;
        
        projectileInventory[projectile] += count;
        OnProjectileChanged?.Invoke(projectile, projectileInventory[projectile]);
        
        if (isPlayerControlled)
        {
            Debug.Log($"{civData.civName} produced {count}x {projectile.projectileName}. Total: {projectileInventory[projectile]}");
        }
    }
    
    /// <summary>
    /// Produces projectiles (checks resources and adds to inventory)
    /// </summary>
    public bool ProduceProjectile(GameCombat.ProjectileData projectile, int count = 1)
    {
        if (projectile == null || count <= 0) return false;
        
        // Check if can be produced
        if (!projectile.CanBeProducedBy(this))
        {
            Debug.LogWarning($"{civData.civName} cannot produce {projectile.projectileName} - requirements not met!");
            return false;
        }
        
        // Consume required resources
        if (projectile.requiredResources != null)
        {
            foreach (var resource in projectile.requiredResources)
            {
                if (resource != null)
                {
                    if (!ConsumeResource(resource, count))
                    {
                        Debug.LogWarning($"{civData.civName} lacks {resource.resourceName} to produce {projectile.projectileName}!");
                        return false;
                    }
                }
            }
        }
        
        // Add to inventory
        AddProjectile(projectile, count);
        return true;
    }
    
    /// <summary>
    /// Consumes projectiles from inventory (not implemented yet - for future ammo consumption)
    /// </summary>
    public bool ConsumeProjectile(GameCombat.ProjectileData projectile, int count = 1)
    {
        if (projectile == null || count <= 0) return false;
        
        if (!projectileInventory.ContainsKey(projectile) || projectileInventory[projectile] < count)
        {
            Debug.LogWarning($"{civData.civName} doesn't have enough {projectile.projectileName}! Need {count}, have {GetProjectileCount(projectile)}");
            return false;
        }
        
        projectileInventory[projectile] -= count;
        OnProjectileChanged?.Invoke(projectile, projectileInventory[projectile]);
        
        // Remove from dictionary if depleted
        if (projectileInventory[projectile] <= 0)
            projectileInventory.Remove(projectile);
        
        return true;
    }
    
    /// <summary>
    /// Gets the count of a specific projectile type in inventory
    /// </summary>
    public int GetProjectileCount(GameCombat.ProjectileData projectile)
    {
        if (projectile == null) return 0;
        return projectileInventory.ContainsKey(projectile) ? projectileInventory[projectile] : 0;
    }
    
    /// <summary>
    /// Checks if the civilization has at least the specified count of this projectile
    /// </summary>
    public bool HasProjectile(GameCombat.ProjectileData projectile, int count = 1)
    {
        if (projectile == null) return false;
        return GetProjectileCount(projectile) >= count;
    }
    
    /// <summary>
    /// Gets all available projectiles for a specific category
    /// </summary>
    public List<GameCombat.ProjectileData> GetAvailableProjectiles(GameCombat.ProjectileCategory category)
    {
        var available = new List<GameCombat.ProjectileData>();
        
        foreach (var kvp in projectileInventory)
        {
            if (kvp.Key != null && kvp.Key.category == category && kvp.Value > 0)
            {
                available.Add(kvp.Key);
            }
        }
        
        return available;
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
        
        // Validate that the equipment is suitable for this unit
        if (!equipment.IsValidForUnit(unit, this))
        {
            Debug.LogWarning($"Cannot equip unit: {equipment.equipmentName} is not valid for {unit.data.unitName}");
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
        
        // Consume the new equipment from inventory FIRST
        if (!ConsumeEquipment(equipment))
        {
            Debug.LogError($"Failed to consume {equipment.equipmentName} from inventory");
            return false;
        }
        
        // Return the existing equipment to inventory if any
        if (currentEquipment != null)
        {
            AddEquipment(currentEquipment);
        }
        
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
    
    // REMOVED: AddUnlockedEquipment() method
    // Equipment is no longer automatically added to inventory when researching techs.
    // Instead, civilizations must produce equipment through cities or other game mechanics.
    // Equipment availability is gated solely by EquipmentData.requiredTechs field.

    /// <summary>
    /// Creates a new city for this civilization at the specified tile.
    /// This is now the primary method for founding cities.
    /// </summary>
    /// <param name="tileIndex">The tile where the city will be founded.</param>
    public void FoundNewCity(int tileIndex, SphericalHexGrid gridOverride = null, PlanetGenerator planetOverride = null)
    {
        Debug.Log($"[FoundNewCity] Called for civ {civData?.civName ?? "NULL"} at tile {tileIndex}. cityPrefab={(cityPrefab != null ? cityPrefab.name : "NULL")}");
        // City-cap gating
        if (!CanFoundMoreCities())
        {
            Debug.LogWarning($"[{civData?.civName ?? "Civ"}] cannot found a new city: at city cap ({cities?.Count ?? 0}/{CurrentCityCap}).");
            return;
        }
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
        AddCity(newCity);
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
        // REMOVED: TechData no longer directly unlocks improvements
        // Improvement availability is now controlled solely by requiredTechs in ImprovementData
        // This method now returns an empty list - improvements should be checked via their requiredTechs
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
            tileData = TileSystem.Instance != null ? TileSystem.Instance.GetTileData(tileIndex) : null;
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

    /// <summary>
    /// Calculate total food consumption per turn (for UI display)
    /// Includes units AND city populations!
    /// </summary>
    public int GetFoodConsumptionPerTurn()
    {
        int totalConsumption = 0;
        
        // Combat units
        if (combatUnits != null)
        {
            foreach (var u in combatUnits)
            {
                if (u != null && u.data != null)
                    totalConsumption += u.data.foodConsumptionPerTurn;
                else
                    totalConsumption += defaultFoodPerCombatUnit;
            }
        }
        
        // Worker units
        if (workerUnits != null)
        {
            foreach (var w in workerUnits)
            {
                if (w != null && w.data != null)
                    totalConsumption += w.data.foodConsumptionPerTurn;
                else
                    totalConsumption += defaultFoodPerWorkerUnit;
            }
        }
        
        // City populations
        if (cities != null)
        {
            foreach (var city in cities)
            {
                if (city != null)
                    totalConsumption += city.GetFoodConsumptionPerTurn();
            }
        }
        
        return totalConsumption;
    }
    
    /// <summary>
    /// Get net food per turn (production - consumption)
    /// </summary>
    public int GetNetFoodPerTurn()
    {
        int production = 0;
        if (cities != null)
        {
            foreach (var city in cities)
            {
                production += Mathf.RoundToInt(city.GetFoodPerTurn() * (1 + foodModifier));
            }
        }
        return production - GetFoodConsumptionPerTurn();
    }
    
    /// <summary>
    /// Get detailed food consumption breakdown (for UI tooltips)
    /// </summary>
    public (int units, int cities, int total) GetFoodConsumptionBreakdown()
    {
        int unitConsumption = 0;
        int cityConsumption = 0;
        
        // Units
        if (combatUnits != null)
        {
            foreach (var u in combatUnits)
            {
                if (u != null && u.data != null)
                    unitConsumption += u.data.foodConsumptionPerTurn;
                else
                    unitConsumption += defaultFoodPerCombatUnit;
            }
        }
        if (workerUnits != null)
        {
            foreach (var w in workerUnits)
            {
                if (w != null && w.data != null)
                    unitConsumption += w.data.foodConsumptionPerTurn;
                else
                    unitConsumption += defaultFoodPerWorkerUnit;
            }
        }
        
        // Cities
        if (cities != null)
        {
            foreach (var city in cities)
            {
                if (city != null)
                    cityConsumption += city.GetFoodConsumptionPerTurn();
            }
        }
        
        return (unitConsumption, cityConsumption, unitConsumption + cityConsumption);
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

    // --- Consolidated bonus aggregation & calculation (moved from BonusAggregator.cs / BonusCalculator.cs) ---

    public struct UnitBonusAgg
    {
        public int attackAdd, defenseAdd, healthAdd, rangeAdd, moraleAdd;
        public float attackPct, defensePct, healthPct, rangePct, moralePct;
    }

    public struct WorkerBonusAgg
    {
        public int workPointsAdd, movePointsAdd, healthAdd;
        public float workPointsPct, movePointsPct, healthPct;
    }
    
    public struct ArmyBonusAgg
    {
        public int movePointsAdd, attackAdd, defenseAdd, healthAdd, moraleAdd;
        public float movePointsPct, attackPct, defensePct, healthPct, moralePct;
    }

    public struct YieldBonusAgg
    {
        public int foodAdd, productionAdd, goldAdd, scienceAdd, cultureAdd, faithAdd, policyPointsAdd;
        public float foodPct, productionPct, goldPct, sciencePct, culturePct, faithPct, policyPointsPct;
    }

    public struct EquipBonusAgg
    {
        public int attackAdd, defenseAdd, healthAdd, rangeAdd;
        public float attackPct, defensePct, healthPct, rangePct;
    }

    private YieldBonusAgg AggregateUnitYieldBonuses(CombatUnitData unit)
    {
        YieldBonusAgg agg = new YieldBonusAgg();
        if (unit == null) return agg;

        // Techs
        if (researchedTechs != null)
        {
            foreach (var tech in researchedTechs)
            {
                if (tech == null || tech.unitYieldBonuses == null) continue;
                foreach (var b in tech.unitYieldBonuses)
                {
                    if (b != null && b.unit == unit)
                    {
                        agg.foodAdd += b.foodAdd; agg.productionAdd += b.productionAdd; agg.goldAdd += b.goldAdd;
                        agg.scienceAdd += b.scienceAdd; agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                        agg.foodPct += b.foodPct; agg.productionPct += b.productionPct; agg.goldPct += b.goldPct;
                        agg.sciencePct += b.sciencePct; agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                    }
                }
            }
        }
        // Cultures
        if (researchedCultures != null)
        {
            foreach (var culture in researchedCultures)
            {
                if (culture == null || culture.unitYieldBonuses == null) continue;
                foreach (var b in culture.unitYieldBonuses)
                {
                    if (b != null && b.unit == unit)
                    {
                        agg.foodAdd += b.foodAdd; agg.productionAdd += b.productionAdd; agg.goldAdd += b.goldAdd;
                        agg.scienceAdd += b.scienceAdd; agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                        agg.foodPct += b.foodPct; agg.productionPct += b.productionPct; agg.goldPct += b.goldPct;
                        agg.sciencePct += b.sciencePct; agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                    }
                }
            }
        }
        // Policies
        if (activePolicies != null)
        {
            foreach (var policy in activePolicies)
            {
                if (policy == null || policy.unitYieldBonuses == null) continue;
                foreach (var b in policy.unitYieldBonuses)
                {
                    if (b != null && b.unit == unit)
                    {
                        agg.foodAdd += b.foodAdd; agg.productionAdd += b.productionAdd; agg.goldAdd += b.goldAdd;
                        agg.scienceAdd += b.scienceAdd; agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                        agg.foodPct += b.foodPct; agg.productionPct += b.productionPct; agg.goldPct += b.goldPct;
                        agg.sciencePct += b.sciencePct; agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                    }
                }
            }
        }
        // Government
        if (currentGovernment != null && currentGovernment.unitYieldBonuses != null)
        {
            foreach (var b in currentGovernment.unitYieldBonuses)
            {
                if (b != null && b.unit == unit)
                {
                    agg.foodAdd += b.foodAdd; agg.productionAdd += b.productionAdd; agg.goldAdd += b.goldAdd;
                    agg.scienceAdd += b.scienceAdd; agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                    agg.foodPct += b.foodPct; agg.productionPct += b.productionPct; agg.goldPct += b.goldPct;
                    agg.sciencePct += b.sciencePct; agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                }
            }
        }

        return agg;
    }

    private YieldBonusAgg AggregateEquipmentYieldBonuses(EquipmentData equip)
    {
        YieldBonusAgg agg = new YieldBonusAgg();
        if (equip == null) return agg;

        // Techs
        if (researchedTechs != null)
        {
            foreach (var tech in researchedTechs)
            {
                if (tech == null || tech.equipmentYieldBonuses == null) continue;
                foreach (var b in tech.equipmentYieldBonuses)
                {
                    if (b != null && b.equipment == equip)
                    {
                        agg.foodAdd += b.foodAdd; agg.goldAdd += b.goldAdd; agg.scienceAdd += b.scienceAdd;
                        agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                        agg.foodPct += b.foodPct; agg.goldPct += b.goldPct; agg.sciencePct += b.sciencePct;
                        agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                    }
                }
            }
        }
        // Cultures
        if (researchedCultures != null)
        {
            foreach (var culture in researchedCultures)
            {
                if (culture == null || culture.equipmentYieldBonuses == null) continue;
                foreach (var b in culture.equipmentYieldBonuses)
                {
                    if (b != null && b.equipment == equip)
                    {
                        agg.foodAdd += b.foodAdd; agg.goldAdd += b.goldAdd; agg.scienceAdd += b.scienceAdd;
                        agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                        agg.foodPct += b.foodPct; agg.goldPct += b.goldPct; agg.sciencePct += b.sciencePct;
                        agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                    }
                }
            }
        }
        // Policies
        if (activePolicies != null)
        {
            foreach (var policy in activePolicies)
            {
                if (policy == null || policy.equipmentYieldBonuses == null) continue;
                foreach (var b in policy.equipmentYieldBonuses)
                {
                    if (b != null && b.equipment == equip)
                    {
                        agg.foodAdd += b.foodAdd; agg.goldAdd += b.goldAdd; agg.scienceAdd += b.scienceAdd;
                        agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                        agg.foodPct += b.foodPct; agg.goldPct += b.goldPct; agg.sciencePct += b.sciencePct;
                        agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                    }
                }
            }
        }
        // Government
        if (currentGovernment != null && currentGovernment.equipmentYieldBonuses != null)
        {
            foreach (var b in currentGovernment.equipmentYieldBonuses)
            {
                if (b != null && b.equipment == equip)
                {
                    agg.foodAdd += b.foodAdd; agg.goldAdd += b.goldAdd; agg.scienceAdd += b.scienceAdd;
                    agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                    agg.foodPct += b.foodPct; agg.goldPct += b.goldPct; agg.sciencePct += b.sciencePct;
                    agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                }
            }
        }

        return agg;
    }

    public (int food, int gold, int science, int culture, int faith, int policy) ComputeUnitPerTurnYield(CombatUnitData unit, params EquipmentData[] equippedItems)
    {
        if (unit == null) return (0,0,0,0,0,0);
        int baseFood = unit.foodPerTurn;
        int baseGold = unit.goldPerTurn;
        int baseSci  = unit.sciencePerTurn;
        int baseCul  = unit.culturePerTurn;
        int baseFai  = unit.faithPerTurn;
        int basePol  = unit.policyPointsPerTurn;

        // Include base equipment yields from all equipped items
        if (equippedItems != null)
        {
            foreach (var eq in equippedItems)
            {
                if (eq == null) continue;
                baseFood += eq.foodPerTurn;
                baseGold += eq.goldPerTurn;
                baseSci  += eq.sciencePerTurn;
                baseCul  += eq.culturePerTurn;
                baseFai  += eq.faithPerTurn;
                basePol  += eq.policyPointsPerTurn;
            }
        }

        var u = AggregateUnitYieldBonuses(unit);
        // Sum equipment-based yield modifiers from bonuses too
        YieldBonusAgg eAgg = new YieldBonusAgg();
        if (equippedItems != null)
        {
            foreach (var eq in equippedItems)
            {
                var e = AggregateEquipmentYieldBonuses(eq);
                eAgg.foodAdd += e.foodAdd; eAgg.goldAdd += e.goldAdd; eAgg.scienceAdd += e.scienceAdd; eAgg.cultureAdd += e.cultureAdd; eAgg.faithAdd += e.faithAdd; eAgg.policyPointsAdd += e.policyPointsAdd;
                eAgg.foodPct += e.foodPct; eAgg.goldPct += e.goldPct; eAgg.sciencePct += e.sciencePct; eAgg.culturePct += e.culturePct; eAgg.faithPct += e.faithPct; eAgg.policyPointsPct += e.policyPointsPct;
            }
        }

        int food = Mathf.RoundToInt((baseFood + u.foodAdd + eAgg.foodAdd) * (1f + u.foodPct + eAgg.foodPct));
        int gold = Mathf.RoundToInt((baseGold + u.goldAdd + eAgg.goldAdd) * (1f + u.goldPct + eAgg.goldPct));
        int sci  = Mathf.RoundToInt((baseSci  + u.scienceAdd + eAgg.scienceAdd) * (1f + u.sciencePct + eAgg.sciencePct));
        int cul  = Mathf.RoundToInt((baseCul  + u.cultureAdd + eAgg.cultureAdd) * (1f + u.culturePct + eAgg.culturePct));
        int fai  = Mathf.RoundToInt((baseFai  + u.faithAdd + eAgg.faithAdd) * (1f + u.faithPct + eAgg.faithPct));
        int pol  = Mathf.RoundToInt((basePol  + u.policyPointsAdd + eAgg.policyPointsAdd) * (1f + u.policyPointsPct + eAgg.policyPointsPct));

        return (food, gold, sci, cul, fai, pol);
    }

    private YieldBonusAgg AggregateWorkerYieldBonuses(WorkerUnitData worker)
    {
        YieldBonusAgg agg = new YieldBonusAgg();
        if (worker == null) return agg;

        if (researchedTechs != null)
        {
            foreach (var tech in researchedTechs)
            {
                if (tech == null || tech.workerYieldBonuses == null) continue;
                foreach (var b in tech.workerYieldBonuses)
                {
                    if (b != null && b.worker == worker)
                    {
                        agg.foodAdd += b.foodAdd; agg.goldAdd += b.goldAdd; agg.scienceAdd += b.scienceAdd;
                        agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                        agg.foodPct += b.foodPct; agg.goldPct += b.goldPct; agg.sciencePct += b.sciencePct;
                        agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                    }
                }
            }
        }
        if (researchedCultures != null)
        {
            foreach (var culture in researchedCultures)
            {
                if (culture == null || culture.workerYieldBonuses == null) continue;
                foreach (var b in culture.workerYieldBonuses)
                {
                    if (b != null && b.worker == worker)
                    {
                        agg.foodAdd += b.foodAdd; agg.goldAdd += b.goldAdd; agg.scienceAdd += b.scienceAdd;
                        agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                        agg.foodPct += b.foodPct; agg.goldPct += b.goldPct; agg.sciencePct += b.sciencePct;
                        agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                    }
                }
            }
        }
        if (activePolicies != null)
        {
            foreach (var policy in activePolicies)
            {
                if (policy == null || policy.workerYieldBonuses == null) continue;
                foreach (var b in policy.workerYieldBonuses)
                {
                    if (b != null && b.worker == worker)
                    {
                        agg.foodAdd += b.foodAdd; agg.goldAdd += b.goldAdd; agg.scienceAdd += b.scienceAdd;
                        agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                        agg.foodPct += b.foodPct; agg.goldPct += b.goldPct; agg.sciencePct += b.sciencePct;
                        agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                    }
                }
            }
        }
        if (currentGovernment != null && currentGovernment.workerYieldBonuses != null)
        {
            foreach (var b in currentGovernment.workerYieldBonuses)
            {
                if (b != null && b.worker == worker)
                {
                    agg.foodAdd += b.foodAdd; agg.goldAdd += b.goldAdd; agg.scienceAdd += b.scienceAdd;
                    agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                    agg.foodPct += b.foodPct; agg.goldPct += b.goldPct; agg.sciencePct += b.sciencePct;
                    agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                }
            }
        }
        return agg;
    }
    
    /// <summary>
    /// Aggregate all army bonuses from techs, cultures, policies, and government
    /// Army bonuses apply to ALL armies (no target filtering)
    /// </summary>
    public ArmyBonusAgg AggregateArmyBonuses()
    {
        ArmyBonusAgg agg = new ArmyBonusAgg();
        
        // Techs
        if (researchedTechs != null)
        {
            foreach (var tech in researchedTechs)
            {
                if (tech == null || tech.armyBonuses == null) continue;
                foreach (var b in tech.armyBonuses)
                {
                    if (b != null)
                    {
                        agg.movePointsAdd += b.movePointsAdd;
                        agg.attackAdd += b.attackAdd;
                        agg.defenseAdd += b.defenseAdd;
                        agg.healthAdd += b.healthAdd;
                        agg.moraleAdd += b.moraleAdd;
                        agg.movePointsPct += b.movePointsPct;
                        agg.attackPct += b.attackPct;
                        agg.defensePct += b.defensePct;
                        agg.healthPct += b.healthPct;
                        agg.moralePct += b.moralePct;
                    }
                }
            }
        }
        
        // Cultures
        if (researchedCultures != null)
        {
            foreach (var culture in researchedCultures)
            {
                if (culture == null || culture.armyBonuses == null) continue;
                foreach (var b in culture.armyBonuses)
                {
                    if (b != null)
                    {
                        agg.movePointsAdd += b.movePointsAdd;
                        agg.attackAdd += b.attackAdd;
                        agg.defenseAdd += b.defenseAdd;
                        agg.healthAdd += b.healthAdd;
                        agg.moraleAdd += b.moraleAdd;
                        agg.movePointsPct += b.movePointsPct;
                        agg.attackPct += b.attackPct;
                        agg.defensePct += b.defensePct;
                        agg.healthPct += b.healthPct;
                        agg.moralePct += b.moralePct;
                    }
                }
            }
        }
        
        // Policies (if they have army bonuses in the future)
        // Note: PolicyData doesn't have armyBonuses yet, but structure is ready
        
        // Government (if it has army bonuses in the future)
        // Note: GovernmentData doesn't have armyBonuses yet, but structure is ready
        
        return agg;
    }

    public (int food, int gold, int science, int culture, int faith, int policy) ComputeWorkerPerTurnYield(WorkerUnitData worker)
    {
        if (worker == null) return (0,0,0,0,0,0);
        int baseFood = worker.foodPerTurn;
        int baseGold = worker.goldPerTurn;
        int baseSci  = worker.sciencePerTurn;
        int baseCul  = worker.culturePerTurn;
        int baseFai  = worker.faithPerTurn;
        int basePol  = worker.policyPointsPerTurn;

        var w = AggregateWorkerYieldBonuses(worker);
        int food = Mathf.RoundToInt((baseFood + w.foodAdd) * (1f + w.foodPct));
        int gold = Mathf.RoundToInt((baseGold + w.goldAdd) * (1f + w.goldPct));
        int sci  = Mathf.RoundToInt((baseSci  + w.scienceAdd) * (1f + w.sciencePct));
        int cul  = Mathf.RoundToInt((baseCul  + w.cultureAdd) * (1f + w.culturePct));
        int fai  = Mathf.RoundToInt((baseFai  + w.faithAdd) * (1f + w.faithPct));
        int pol  = Mathf.RoundToInt((basePol  + w.policyPointsAdd) * (1f + w.policyPointsPct));
        return (food, gold, sci, cul, fai, pol);
    }

    // --- CombinedBonuses (from BonusCalculator) ---
    [System.Serializable]
    public struct CombinedBonuses
    {
        public float foodModifier;
        public float productionModifier;
        public float goldModifier;
        public float scienceModifier;
        public float cultureModifier;
        public float faithModifier;
        public float attackBonus;
        public float defenseBonus;
        public float movementBonus;

        public int flatFoodBonus;
        public int flatProductionBonus;
        public int flatGoldBonus;
        public int flatScienceBonus;
        public int flatCultureBonus;
        public int flatFaithBonus;

        public int additionalGovernorSlots;

        public List<UnitLimitModifier> unitLimitModifiers;
        public List<BuildingLimitModifier> buildingLimitModifiers;
    }

    public CombinedBonuses CalculateTechBonuses(List<TechData> technologies)
    {
        CombinedBonuses result = new CombinedBonuses();
        if (technologies == null || technologies.Count == 0)
            return result;
        foreach (var tech in technologies)
        {
            if (tech == null) continue;
            result.foodModifier += tech.foodModifier;
            result.productionModifier += tech.productionModifier;
            result.goldModifier += tech.goldModifier;
            result.scienceModifier += tech.scienceModifier;
            result.cultureModifier += tech.cultureModifier;
            result.faithModifier += tech.faithModifier;
            result.attackBonus += tech.attackBonus;
            result.defenseBonus += tech.defenseBonus;
            result.movementBonus += tech.movementBonus;

            result.flatFoodBonus += tech.flatFoodBonus;
            result.flatProductionBonus += tech.flatProductionBonus;
            result.flatGoldBonus += tech.flatGoldBonus;
            result.flatScienceBonus += tech.flatScienceBonus;
            result.flatCultureBonus += tech.flatCultureBonus;
            result.flatFaithBonus += tech.flatFaithBonus;

            result.additionalGovernorSlots += tech.additionalGovernorSlots;

            if (result.unitLimitModifiers == null)
                result.unitLimitModifiers = new List<UnitLimitModifier>();
            if (result.buildingLimitModifiers == null)
                result.buildingLimitModifiers = new List<BuildingLimitModifier>();

            if (tech.unitLimitModifiers != null)
                result.unitLimitModifiers.AddRange(tech.unitLimitModifiers);
            if (tech.buildingLimitModifiers != null)
                result.buildingLimitModifiers.AddRange(tech.buildingLimitModifiers);
        }
        return result;
    }

    public CombinedBonuses CalculateCultureBonuses(List<CultureData> cultures)
    {
        CombinedBonuses result = new CombinedBonuses();
        if (cultures == null || cultures.Count == 0)
            return result;
        foreach (var culture in cultures)
        {
            if (culture == null) continue;
            result.foodModifier += culture.foodModifier;
            result.productionModifier += culture.productionModifier;
            result.goldModifier += culture.goldModifier;
            result.scienceModifier += culture.scienceModifier;
            result.cultureModifier += culture.cultureModifier;
            result.faithModifier += culture.faithModifier;
            result.attackBonus += culture.attackBonus;
            result.defenseBonus += culture.defenseBonus;
            result.movementBonus += culture.movementBonus;

            result.flatFoodBonus += culture.flatFoodBonus;
            result.flatProductionBonus += culture.flatProductionBonus;
            result.flatGoldBonus += culture.flatGoldBonus;
            result.flatScienceBonus += culture.flatScienceBonus;
            result.flatCultureBonus += culture.flatCultureBonus;
            result.flatFaithBonus += culture.flatFaithBonus;

            result.additionalGovernorSlots += culture.additionalGovernorSlots;

            if (result.unitLimitModifiers == null)
                result.unitLimitModifiers = new List<UnitLimitModifier>();
            if (result.buildingLimitModifiers == null)
                result.buildingLimitModifiers = new List<BuildingLimitModifier>();

            if (culture.unitLimitModifiers != null)
                result.unitLimitModifiers.AddRange(culture.unitLimitModifiers);
            if (culture.buildingLimitModifiers != null)
                result.buildingLimitModifiers.AddRange(culture.buildingLimitModifiers);
        }
        return result;
    }

    public CombinedBonuses CalculateTotalBonuses(List<TechData> technologies, List<CultureData> cultures)
    {
        var techBonuses = CalculateTechBonuses(technologies);
        var cultureBonuses = CalculateCultureBonuses(cultures);
        return CombineBonuses(techBonuses, cultureBonuses);
    }

    public CombinedBonuses CombineBonuses(CombinedBonuses bonuses1, CombinedBonuses bonuses2)
    {
        CombinedBonuses result = new CombinedBonuses();
        result.foodModifier = bonuses1.foodModifier + bonuses2.foodModifier;
        result.productionModifier = bonuses1.productionModifier + bonuses2.productionModifier;
        result.goldModifier = bonuses1.goldModifier + bonuses2.goldModifier;
        result.scienceModifier = bonuses1.scienceModifier + bonuses2.scienceModifier;
        result.cultureModifier = bonuses1.cultureModifier + bonuses2.cultureModifier;
        result.faithModifier = bonuses1.faithModifier + bonuses2.faithModifier;
        result.attackBonus = bonuses1.attackBonus + bonuses2.attackBonus;
        result.defenseBonus = bonuses1.defenseBonus + bonuses2.defenseBonus;
        result.movementBonus = bonuses1.movementBonus + bonuses2.movementBonus;

        result.flatFoodBonus = bonuses1.flatFoodBonus + bonuses2.flatFoodBonus;
        result.flatProductionBonus = bonuses1.flatProductionBonus + bonuses2.flatProductionBonus;
        result.flatGoldBonus = bonuses1.flatGoldBonus + bonuses2.flatGoldBonus;
        result.flatScienceBonus = bonuses1.flatScienceBonus + bonuses2.flatScienceBonus;
        result.flatCultureBonus = bonuses1.flatCultureBonus + bonuses2.flatCultureBonus;
        result.flatFaithBonus = bonuses1.flatFaithBonus + bonuses2.flatFaithBonus;

        result.additionalGovernorSlots = bonuses1.additionalGovernorSlots + bonuses2.additionalGovernorSlots;

        result.unitLimitModifiers = new List<UnitLimitModifier>();
        result.buildingLimitModifiers = new List<BuildingLimitModifier>();
        if (bonuses1.unitLimitModifiers != null)
            result.unitLimitModifiers.AddRange(bonuses1.unitLimitModifiers);
        if (bonuses2.unitLimitModifiers != null)
            result.unitLimitModifiers.AddRange(bonuses2.unitLimitModifiers);
        if (bonuses1.buildingLimitModifiers != null)
            result.buildingLimitModifiers.AddRange(bonuses1.buildingLimitModifiers);
        if (bonuses2.buildingLimitModifiers != null)
            result.buildingLimitModifiers.AddRange(bonuses2.buildingLimitModifiers);

        return result;
    }

    /// <summary>
    /// Simple yield collection used by bonus calculations and application helpers.
    /// This replaces the type that used to live in BonusCalculator.cs which was removed.
    /// </summary>
    [System.Serializable]
    public struct YieldCollection
    {
        public int food;
        public int production;
        public int gold;
        public int science;
        public int culture;
        public int faith;

        public YieldCollection(int food = 0, int production = 0, int gold = 0, int science = 0, int culture = 0, int faith = 0)
        {
            this.food = food;
            this.production = production;
            this.gold = gold;
            this.science = science;
            this.culture = culture;
            this.faith = faith;
        }

        public static YieldCollection operator +(YieldCollection a, YieldCollection b)
        {
            return new YieldCollection(
                a.food + b.food,
                a.production + b.production,
                a.gold + b.gold,
                a.science + b.science,
                a.culture + b.culture,
                a.faith + b.faith
            );
        }

        public override string ToString()
        {
            return $"food:{food} prod:{production} gold:{gold} sci:{science} cul:{culture} faith:{faith}";
        }
    }

    public int ApplyBonuses(int baseYield, float percentageModifier, int flatBonus)
    {
        float modifiedYield = baseYield * (1f + percentageModifier);
        return Mathf.RoundToInt(modifiedYield) + flatBonus;
    }

    public YieldCollection ApplyYieldBonuses(YieldCollection baseYields, CombinedBonuses bonuses)
    {
        YieldCollection finalYields = new YieldCollection();
        finalYields.food = ApplyBonuses(baseYields.food, bonuses.foodModifier, bonuses.flatFoodBonus);
        finalYields.production = ApplyBonuses(baseYields.production, bonuses.productionModifier, bonuses.flatProductionBonus);
        finalYields.gold = ApplyBonuses(baseYields.gold, bonuses.goldModifier, bonuses.flatGoldBonus);
        finalYields.science = ApplyBonuses(baseYields.science, bonuses.scienceModifier, bonuses.flatScienceBonus);
        finalYields.culture = ApplyBonuses(baseYields.culture, bonuses.cultureModifier, bonuses.flatCultureBonus);
        finalYields.faith = ApplyBonuses(baseYields.faith, bonuses.faithModifier, bonuses.flatFaithBonus);
        return finalYields;
    }

    public SphericalHexGrid planetGrid; // Add this field to store the main planet's grid
    public PlanetGenerator planetGenerator; // Add this field to store the main planet's generator

    /// <summary>
    /// Invalidate availability cache when techs/cultures change
    /// </summary>
    private void InvalidateAvailabilityCache()
    {
        _availabilityCacheDirty = true;
        _unitAvailabilityCache.Clear();
        _workerAvailabilityCache.Clear();
        _buildingAvailabilityCache.Clear();
        _equipmentAvailabilityCache.Clear();
    }

    /// <summary>
    /// Check if a combat unit is available (cached)
    /// </summary>
    public bool IsCombatUnitAvailable(CombatUnitData unitData)
    {
        if (unitData == null) return false;
        
        if (_availabilityCacheDirty || !_unitAvailabilityCache.ContainsKey(unitData))
        {
            bool available = unitData.AreRequirementsMet(this);
            _unitAvailabilityCache[unitData] = available;
        }
        
        return _unitAvailabilityCache[unitData];
    }

    /// <summary>
    /// Check if a worker unit is available (cached)
    /// </summary>
    public bool IsWorkerUnitAvailable(WorkerUnitData unitData)
    {
        if (unitData == null) return false;
        
        if (_availabilityCacheDirty || !_workerAvailabilityCache.ContainsKey(unitData))
        {
            bool available = unitData.AreRequirementsMet(this);
            _workerAvailabilityCache[unitData] = available;
        }
        
        return _workerAvailabilityCache[unitData];
    }

    /// <summary>
    /// Check if a building is available (cached)
    /// </summary>
    public bool IsBuildingAvailable(BuildingData buildingData)
    {
        if (buildingData == null) return false;
        
        if (_availabilityCacheDirty || !_buildingAvailabilityCache.ContainsKey(buildingData))
        {
            bool available = buildingData.AreRequirementsMet(this);
            _buildingAvailabilityCache[buildingData] = available;
        }
        
        return _buildingAvailabilityCache[buildingData];
    }

    /// <summary>
    /// Check if equipment is available (cached)
    /// </summary>
    public bool IsEquipmentAvailable(EquipmentData equipmentData)
    {
        if (equipmentData == null) return false;
        
        if (_availabilityCacheDirty || !_equipmentAvailabilityCache.ContainsKey(equipmentData))
        {
            bool available = equipmentData.CanBeProducedBy(this);
            _equipmentAvailabilityCache[equipmentData] = available;
        }
        
        return _equipmentAvailabilityCache[equipmentData];
    }
}

