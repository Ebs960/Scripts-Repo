using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using TMPro;

[RequireComponent(typeof(Animator))]
public class CombatUnit : MonoBehaviour
{
    [Header("Stats (Override Data Asset)")]
    [SerializeField] private int attack = 0;
    [SerializeField] private int defense = 0;
    [SerializeField] private int health = 0; 
    [SerializeField] private int movePoints = 0;
    [SerializeField] private int range = 0;
    [SerializeField] private int attackPoints = 0;
    [SerializeField] private int morale = 0;
    [SerializeField] private bool useOverrideStats = false;
    
    [Header("Equipment Attachment Points")]
    [Tooltip("Transform where weapons will be attached")]
    public Transform weaponHolder;
    [Tooltip("Transform where shields will be attached")]
    public Transform shieldHolder;
    [Tooltip("Transform where armor will be displayed")]
    public Transform armorHolder;
    [Tooltip("Transform where miscellaneous items will be attached")]
    public Transform miscHolder;
    [Tooltip("Transform where projectiles will spawn from")]
    public Transform projectileSpawnPoint;
    
    // Dictionary to track instantiated equipment GameObjects
    protected Dictionary<EquipmentType, GameObject> equippedItemObjects = new Dictionary<EquipmentType, GameObject>();
    
    // Equipment in use (beyond just the single 'equipped' reference)
    protected EquipmentData equippedWeapon;
    protected EquipmentData equippedShield;
    protected EquipmentData equippedArmor;
    protected EquipmentData equippedMiscellaneous;
    
    SphericalHexGrid grid;
    PlanetGenerator planet;
    Animator animator;

    public CombatUnitData data { get; private set; }
    public Civilization owner { get; private set; }

    // Remove old events and use GameEventManager instead
    public event System.Action OnDeath;
    public event System.Action<int,int> OnHealthChanged;      // (newHealth, maxHealth)
    public event System.Action<string> OnAnimationTrigger;    // (triggerName)
    public event System.Action<int,int> OnMoraleChanged;
    public event System.Action OnEquipmentChanged;

    // Equipment & Abilities
    public EquipmentData equipped { get; private set; }
    public List<Ability> unlockedAbilities { get; private set; } = new List<Ability>();

    // Runtime stats
    public int currentHealth { get; private set; }
    public int currentMovePoints { get; private set; }
    public int currentAttackPoints { get; private set; }
    public int experience { get; private set; }
    public int level { get; private set; }
    // New: morale runtime stat
    public int currentMorale    { get; private set; }
    
    // Flag for tracking winter movement penalty
    public bool hasWinterPenalty { get; set; }
    
    // Weather susceptibility
    [Header("Weather")]
    [Tooltip("If true, this unit takes weather attrition in severe seasons (e.g., winter)")]
    public bool takesWeatherDamage = true;

    // Routed flag when morale hits zero
    public bool isRouted { get; private set; }

    // Transport system
    private List<CombatUnit> transportedUnits = new List<CombatUnit>();
    // Events for UI updates when units are loaded/unloaded
    public UnityEvent<CombatUnit> OnUnitLoaded = new UnityEvent<CombatUnit>();
    public UnityEvent<CombatUnit> OnUnitUnloaded = new UnityEvent<CombatUnit>();
    // Event for when this unit is loaded into another transport
    public UnityEvent<CombatUnit> OnLoadedIntoTransport = new UnityEvent<CombatUnit>();
    // Event for when this unit is unloaded from a transport
    public UnityEvent<CombatUnit> OnUnloadedFromTransport = new UnityEvent<CombatUnit>();
    
    // Property to check if this unit is currently transported
    public bool IsTransported { get; private set; }
    // Reference to the transport carrying this unit (if any)
    public CombatUnit TransportingUnit { get; private set; }

    [Header("UI")]
    [SerializeField] private GameObject unitLabelPrefab;
    private UnitLabel unitLabelInstance;

    void Awake()
    {
        animator = GetComponent<Animator>();
        // Use GameManager API for multi-planet support
        planet = GameManager.Instance?.GetCurrentPlanetGenerator();
        if (planet != null) grid = planet.Grid;
        UnitRegistry.Register(gameObject);
    }

    void OnDestroy()
    {
        // Clean up all equipment GameObjects
        foreach (var item in equippedItemObjects.Values)
        {
            if (item != null)
                Destroy(item);
        }
        equippedItemObjects.Clear();
        
        // Unsubscribe from events
        GameEventManager.Instance.OnMovementCompleted -= HandleMovementCompleted;
        GameEventManager.Instance.OnCombatStarted -= HandleCombatStarted;
        GameEventManager.Instance.OnDamageApplied -= HandleDamageApplied;

        UnitRegistry.Unregister(gameObject);
    }

    public void Initialize(CombatUnitData unitData, Civilization unitOwner)
    {
        data = unitData;
        owner = unitOwner;
        level = 1;
        experience = 0;
        equipped = data.defaultEquipment;
    // Weather susceptibility from data
    takesWeatherDamage = (data != null) ? data.takesWeatherDamage : takesWeatherDamage;
        
        if (data.defaultEquipment != null)
        {
            EquipItem(data.defaultEquipment);
        }
        
        currentHealth = MaxHealth;
        currentMorale = useOverrideStats && morale > 0 ? morale : data.baseMorale;
        
        RecalculateStats();

        animator = GetComponent<Animator>();
        // Ensure animator is not null before trying to set a trigger
        if (animator != null) 
        {
            animator.SetTrigger("idleYoung");
        }
        else
        {
            Debug.LogWarning($"CombatUnit {gameObject.name} is missing an Animator component.");
        }
        
        UpdateEquipmentVisuals();
        
        if (UnitFormationManager.Instance != null)
        {
            UnitFormationManager.Instance.RegisterFormation(this);
        }

        // Subscribe to events
        GameEventManager.Instance.OnMovementCompleted += HandleMovementCompleted;
        GameEventManager.Instance.OnCombatStarted += HandleCombatStarted;
        GameEventManager.Instance.OnDamageApplied += HandleDamageApplied;

        // Instantiate and initialize the unit label
        if (unitLabelPrefab != null && unitLabelInstance == null)
        {
            var labelGO = Instantiate(unitLabelPrefab, transform); // Parent to the unit
            unitLabelInstance = labelGO.GetComponent<UnitLabel>();
            if (unitLabelInstance != null)
            {
                string ownerName = owner != null && owner.civData != null ? owner.civData.civName : "Unknown";
                unitLabelInstance.Initialize(transform, data.unitName, ownerName, currentHealth, MaxHealth);

                // Disable raycast targets on the label's text components
                var textComponents = unitLabelInstance.GetComponentsInChildren<TMPro.TextMeshProUGUI>();
                foreach (var textComponent in textComponents)
                {
                    if (textComponent != null) textComponent.raycastTarget = false;
                }
            }
        }
        // Subscribe to health change for label update
        OnHealthChanged += UpdateUnitLabelHealth;
    }

    // Base stats exposed
    public int BaseAttack => useOverrideStats && attack > 0 ? attack : data.baseAttack;
    public int BaseDefense => useOverrideStats && defense > 0 ? defense : data.baseDefense;
    public int BaseHealth => useOverrideStats && health > 0 ? health : data.baseHealth;
    public int BaseMovePoints => useOverrideStats && movePoints > 0 ? movePoints : data.baseMovePoints;
    public int BaseRange => useOverrideStats && range > 0 ? range : data.baseRange;
    public int BaseAttackPoints => useOverrideStats && attackPoints > 0 ? attackPoints : data.baseAttackPoints;

    // Equipment bonuses
    public int EquipmentAttackBonus        => equipped?.attackBonus        ?? 0;
    public int EquipmentDefenseBonus       => equipped?.defenseBonus       ?? 0;
    public int EquipmentHealthBonus        => equipped?.healthBonus        ?? 0;
    public int EquipmentMoveBonus          => equipped?.movementBonus      ?? 0;
    public int EquipmentRangeBonus         => equipped?.rangeBonus         ?? 0;
    public int EquipmentAttackPointsBonus  => equipped?.attackPointsBonus  ?? 0;

    // Ability modifiers - ADDED
    public int GetAbilityAttackModifier()
    {
        int total = 0;
        foreach (var ability in unlockedAbilities)
            total += ability.attackModifier;
        return total;
    }

    public int GetAbilityDefenseModifier()
    {
        int total = 0;
        foreach (var ability in unlockedAbilities)
            total += ability.defenseModifier;
        return total;
    }

    // Add new ability modifier methods
    public int GetAbilityHealthModifier()
    {
        int total = 0;
        foreach (var ability in unlockedAbilities)
            total += ability.healthModifier;
        return total;
    }

    public int GetAbilityRangeModifier()
    {
        int total = 0;
        foreach (var ability in unlockedAbilities)
            total += ability.rangeModifier;
        return total;
    }

    public int GetAbilityAttackPointsModifier()
    {
        int total = 0;
        foreach (var ability in unlockedAbilities)
            total += ability.attackPointsModifier;
        return total;
    }

    public float GetAbilityDamageMultiplier()
    {
        float total = 1.0f; // Start with base value
        foreach (var ability in unlockedAbilities)
            total *= ability.damageMultiplier;
        return total;
    }

    // Combined stats - UPDATED to include all ability modifiers
    // Local aggregation structs
    private struct UnitAgg { public int attackAdd, defenseAdd, healthAdd, moveAdd, rangeAdd, apAdd, moraleAdd; public float attackPct, defensePct, healthPct, movePct, rangePct, apPct, moralePct; }
    private struct EquipAgg { public int attackAdd, defenseAdd, healthAdd, moveAdd, rangeAdd, apAdd; public float attackPct, defensePct, healthPct, movePct, rangePct, apPct; }

    private UnitAgg AggregateUnitBonusesLocal(Civilization civ, CombatUnitData u)
    {
        UnitAgg a = new UnitAgg(); if (civ == null || u == null) return a;
        if (civ.researchedTechs != null)
            foreach (var t in civ.researchedTechs)
            {
                if (t?.unitBonuses == null) continue;
                foreach (var b in t.unitBonuses)
                    if (b != null && b.unit == u)
                    {
                        a.attackAdd += b.attackAdd; a.defenseAdd += b.defenseAdd; a.healthAdd += b.healthAdd;
                        a.moveAdd += b.movePointsAdd; a.rangeAdd += b.rangeAdd; a.apAdd += b.attackPointsAdd; a.moraleAdd += b.moraleAdd;
                        a.attackPct += b.attackPct; a.defensePct += b.defensePct; a.healthPct += b.healthPct;
                        a.movePct += b.movePointsPct; a.rangePct += b.rangePct; a.apPct += b.attackPointsPct; a.moralePct += b.moralePct;
                    }
            }
        if (civ.researchedCultures != null)
            foreach (var c in civ.researchedCultures)
            {
                if (c?.unitBonuses == null) continue;
                foreach (var b in c.unitBonuses)
                    if (b != null && b.unit == u)
                    {
                        a.attackAdd += b.attackAdd; a.defenseAdd += b.defenseAdd; a.healthAdd += b.healthAdd;
                        a.moveAdd += b.movePointsAdd; a.rangeAdd += b.rangeAdd; a.apAdd += b.attackPointsAdd; a.moraleAdd += b.moraleAdd;
                        a.attackPct += b.attackPct; a.defensePct += b.defensePct; a.healthPct += b.healthPct;
                        a.movePct += b.movePointsPct; a.rangePct += b.rangePct; a.apPct += b.attackPointsPct; a.moralePct += b.moralePct;
                    }
            }
        return a;
    }

    private EquipAgg AggregateEquipBonusesLocal(Civilization civ, EquipmentData eq)
    {
        EquipAgg a = new EquipAgg(); if (civ == null || eq == null) return a;
        if (civ.researchedTechs != null)
            foreach (var t in civ.researchedTechs)
            {
                if (t?.equipmentBonuses == null) continue;
                foreach (var b in t.equipmentBonuses)
                    if (b != null && b.equipment == eq)
                    {
                        a.attackAdd += b.attackAdd; a.defenseAdd += b.defenseAdd; a.healthAdd += b.healthAdd;
                        a.moveAdd += b.movePointsAdd; a.rangeAdd += b.rangeAdd; a.apAdd += b.attackPointsAdd;
                        a.attackPct += b.attackPct; a.defensePct += b.defensePct; a.healthPct += b.healthPct;
                        a.movePct += b.movePointsPct; a.rangePct += b.rangePct; a.apPct += b.attackPointsPct;
                    }
            }
        if (civ.researchedCultures != null)
            foreach (var c in civ.researchedCultures)
            {
                if (c?.equipmentBonuses == null) continue;
                foreach (var b in c.equipmentBonuses)
                    if (b != null && b.equipment == eq)
                    {
                        a.attackAdd += b.attackAdd; a.defenseAdd += b.defenseAdd; a.healthAdd += b.healthAdd;
                        a.moveAdd += b.movePointsAdd; a.rangeAdd += b.rangeAdd; a.apAdd += b.attackPointsAdd;
                        a.attackPct += b.attackPct; a.defensePct += b.defensePct; a.healthPct += b.healthPct;
                        a.movePct += b.movePointsPct; a.rangePct += b.rangePct; a.apPct += b.attackPointsPct;
                    }
            }
        return a;
    }

    public int CurrentAttack
    {
        get
        {
            int val = BaseAttack + EquipmentAttackBonus + GetAbilityAttackModifier();
            if (owner != null && data != null)
            {
                var u = AggregateUnitBonusesLocal(owner, data);
                val = Mathf.RoundToInt((val + u.attackAdd) * (1f + u.attackPct));
            }
            if (owner != null && equipped != null)
            {
                var e = AggregateEquipBonusesLocal(owner, equipped);
                val = Mathf.RoundToInt((val + e.attackAdd) * (1f + e.attackPct));
            }
            return val;
        }
    }
    public int CurrentDefense
    {
        get
        {
            int val = BaseDefense + EquipmentDefenseBonus + GetAbilityDefenseModifier();
            if (owner != null && data != null)
            {
                var u = AggregateUnitBonusesLocal(owner, data);
                val = Mathf.RoundToInt((val + u.defenseAdd) * (1f + u.defensePct));
            }
            if (owner != null && equipped != null)
            {
                var e = AggregateEquipBonusesLocal(owner, equipped);
                val = Mathf.RoundToInt((val + e.defenseAdd) * (1f + e.defensePct));
            }
            return val;
        }
    }
    public int MaxHealth
    {
        get
        {
            int val = BaseHealth + EquipmentHealthBonus + GetAbilityHealthModifier();
            if (owner != null && data != null)
            {
                var u = AggregateUnitBonusesLocal(owner, data);
                val = Mathf.RoundToInt((val + u.healthAdd) * (1f + u.healthPct));
            }
            if (owner != null && equipped != null)
            {
                var e = AggregateEquipBonusesLocal(owner, equipped);
                val = Mathf.RoundToInt((val + e.healthAdd) * (1f + e.healthPct));
            }
            return val;
        }
    }
    public int CurrentRange
    {
        get
        {
            int val = BaseRange + EquipmentRangeBonus + GetAbilityRangeModifier();
            if (owner != null && data != null)
            {
                var u = AggregateUnitBonusesLocal(owner, data);
                val = Mathf.RoundToInt((val + u.rangeAdd) * (1f + u.rangePct));
            }
            if (owner != null && equipped != null)
            {
                var e = AggregateEquipBonusesLocal(owner, equipped);
                val = Mathf.RoundToInt((val + e.rangeAdd) * (1f + e.rangePct));
            }
            return val;
        }
    }
    public int MaxAttackPoints   => BaseAttackPoints + EquipmentAttackPointsBonus + GetAbilityAttackPointsModifier();
    public int MaxMorale         => useOverrideStats && morale > 0 ? morale : data.baseMorale;
    
    // Equipment Properties
    public EquipmentData Weapon => equippedWeapon;
    public EquipmentData Shield => equippedShield;
    public EquipmentData Armor => equippedArmor;
    public EquipmentData Miscellaneous => equippedMiscellaneous;

    // Only land units can move on land, naval on water
    public bool CanMoveTo(int tileIndex)
    {
        var (tileData, isMoonTile) = TileDataHelper.Instance.GetTileData(tileIndex);
        if(tileData == null || !tileData.isPassable) return false;
        
        // Special case for Moon: ONLY spaceships with canTravelToMoon flag can go there
        if (isMoonTile)
        {
            bool isSpaceshipWithMoonAccess = data.category == CombatCategory.Spaceship && data.canTravelToMoon;
            
            if (!isSpaceshipWithMoonAccess)
                return false;
                
            if (currentMovePoints < tileData.movementCost) return false;
            
            if (tileData.occupantId != 0 && tileData.occupantId != gameObject.GetInstanceID())
                return false;
                
            return true;
        }
        else
        {
            // Regular planet rules: water check for naval units
            if (!tileData.isLand)
            {
                switch (data.category)
                {
                    case CombatCategory.Ship:
                    case CombatCategory.Boat:
                    case CombatCategory.Submarine:
                    case CombatCategory.SeaCrawler:
                        break;
                    default:
                        return false;
                }
            }
        }

        if (currentMovePoints < tileData.movementCost) return false;

        if (tileData.occupantId != 0 && tileData.occupantId != gameObject.GetInstanceID())
            return false;

        return true;
    }

    public void MoveAlongPath(List<int> path)
    {
        var (tileData, isMoon) = TileDataHelper.Instance.GetTileData(path[0]);
        SphericalHexGrid currentGrid = grid;

        foreach (int idx in path)
        {
            var (currentTileData, _) = TileDataHelper.Instance.GetTileData(idx);

            int cost = currentTileData.movementCost;
            currentMovePoints -= cost;

            Vector3 pos = currentGrid.tileCenters[idx];
            transform.position = pos;

            // Update tile occupancy
            TileDataHelper.Instance.SetTileOccupant(idx, gameObject);
            
            currentTileIndex = idx;
        }

        // Raise movement completed event
        if (path.Count > 0)
        {
            GameEventManager.Instance.RaiseMovementCompletedEvent(this, path[0], path[path.Count - 1], path.Count);
        }
    }

    public bool CanAttack(CombatUnit target)
    {
        if (currentAttackPoints <= 0) return false;
        if (isRouted) return false; // Routed units cannot attack

        // Target category checks
        bool targetIsAir = target.data.category == CombatCategory.Aircraft;
        bool targetIsSpace = target.data.category == CombatCategory.Spaceship;
        bool targetIsUnderwater = target.data.category == CombatCategory.Submarine || 
                                 target.data.category == CombatCategory.SeaCrawler;

        // Check specific attack capabilities
        if (targetIsAir && !data.canAttackAir) return false;
        if (targetIsSpace && !data.canAttackSpace) return false;
        if (targetIsUnderwater && !data.canAttackUnderwater) return false;

        // Range check
        float dist = Vector3.Distance(transform.position, target.transform.position);
        return dist <= CurrentRange;
    }

    public void Attack(CombatUnit target)
    {
        if (!CanAttack(target)) return;

        animator.SetTrigger("attack");
        OnAnimationTrigger?.Invoke("attack");
        
        // Tile defense bonus for target (e.g., hills)
        int tileBonus = 0;
        var (tileData, _) = TileDataHelper.Instance.GetTileData(target.currentTileIndex);
        if (tileData != null)
        {
            tileBonus = BiomeHelper.GetDefenseBonus(tileData.biome);
            if (tileData.isHill)
                tileBonus += 2;
        }
        
        // Damage calculation
        float dmgMul = GetAbilityDamageMultiplier();
        int raw = Mathf.Max(0, CurrentAttack - target.CurrentDefense - tileBonus);
        int damage = Mathf.RoundToInt(raw * dmgMul);

        // Flanking: adjacent allied units give +10% per extra unit
        int flankCount = CountAdjacentAllies(target.currentTileIndex) - 1;
        if (flankCount > 0)
            damage = Mathf.RoundToInt(damage * (1 + 0.1f * flankCount));

        bool targetDies = target.ApplyDamage(damage);

        if (targetDies)
        {
            ChangeMorale(data.moraleGainOnKill);
        }
        else
        {
            // Counter-attack if target can
            if (target.CanAttack(this))
                target.CounterAttack(this);
        }

        currentAttackPoints--;
        GainExperience(damage);
    }
    
    /// <summary>
    /// Apply damage to this unit, which reduces its health
    /// </summary>
    /// <param name="damageAmount">Amount of damage to deal</param>
    /// <returns>True if the unit is destroyed by this damage</returns>
    public bool ApplyDamage(int damageAmount)
    {
        animator.SetTrigger("hit");
        
        currentHealth -= damageAmount;
        
        // Raise damage event
        GameEventManager.Instance.RaiseDamageAppliedEvent(null, this, damageAmount);
        
        // Morale penalty proportional to HP lost
        ChangeMorale(-damageAmount * data.moraleLostPerHealth);
        
        if (currentHealth <= 0)
        {
            Die();
            return true;
        }
        
        if (currentHealth <= MaxHealth * 0.2f && !isRouted)
        {
            Rout();
        }
        
        if (owner != null && owner.isPlayerControlled && UIManager.Instance != null)
        {
            // Get tile data to show biome in notification
            var (tileDataForNotification, _) = TileDataHelper.Instance.GetTileData(currentTileIndex);
            if (tileDataForNotification != null)
            {
                UIManager.Instance.ShowNotification($"{data.unitName} took {damageAmount} damage from {tileDataForNotification.biome} terrain!");
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Set this unit to routed state (reduced effectiveness)
    /// </summary>
    private void Rout()
    {
        isRouted = true;
        // Visual indicator for routing could be added here
        Debug.Log($"{data.unitName} has been routed!");
    }
    
    /// <summary>
    /// Destroy this unit
    /// </summary>
    private void Die()
    {
        animator.SetTrigger("death");
        
        // Raise death event
        GameEventManager.Instance.RaiseUnitKilledEvent(null, this, currentHealth);
        
        // Fire local death event for listeners (e.g., AnimalManager)
        OnDeath?.Invoke();
        
        Debug.Log($"{data.unitName} has been destroyed!");
        owner.food += data.foodOnKill;
        
        owner.combatUnits.Remove(this);
        
        if (currentTileIndex >= 0)
        {
            TileDataHelper.Instance.ClearTileOccupant(currentTileIndex);
        }
        
        if (unitLabelInstance != null)
        {
            Destroy(unitLabelInstance.gameObject);
        }
        
        Destroy(gameObject, 1.5f);
    }

    /// <summary>
    /// Performs a counter-attack back at the attacker.
    /// </summary>
    public void CounterAttack(CombatUnit attacker)
    {
        if (!data.canCounterAttack) return;
        if (currentAttackPoints <= 0) return;
        if (isRouted) return; // Routed units cannot counter-attack

        animator.SetTrigger("attack");
        OnAnimationTrigger?.Invoke("attack");

        int tileBonus = 0;
        var (tileData, _) = TileDataHelper.Instance.GetTileData(attacker.currentTileIndex);
        if (tileData != null)
        {
            tileBonus = BiomeHelper.GetDefenseBonus(tileData.biome);
            if (tileData.isHill)
                tileBonus += 2;
        }

        float dmgMul = GetAbilityDamageMultiplier();
        int raw = Mathf.Max(0, CurrentAttack - attacker.CurrentDefense - tileBonus);
        int damage = Mathf.RoundToInt(raw * dmgMul);

        // Flanking for counter-attacks too
        int flankCount = CountAdjacentAllies(attacker.currentTileIndex) - 1;
        if (flankCount > 0)
            damage = Mathf.RoundToInt(damage * (1 + 0.1f * flankCount));

        attacker.ApplyDamage(damage);
        currentAttackPoints--;
        GainExperience(damage);
    }

    /// <summary>
    /// Adjust morale, clamp [0..max], fire event, and handle low-morale penalties.
    /// </summary>
    private void ChangeMorale(int delta)
    {
        int old = currentMorale;
        currentMorale = Mathf.Clamp(currentMorale + delta, 0, data.baseMorale);
        if (currentMorale != old)
            OnMoraleChanged?.Invoke(currentMorale, data.baseMorale);

        // Check if unit is now routed
        if (!isRouted && currentMorale == 0)
        {
            // Unit routs: cannot attack, moves randomly away
            isRouted = true;
            animator.SetTrigger("rout");
            OnAnimationTrigger?.Invoke("rout");
            // Flee one tile away
            AttemptFlee();
        }
    }

    /// <summary>
    /// Simple placeholder flee logic: move to a random neighbouring tile.
    /// </summary>
    private void AttemptFlee()
    {
        if (grid == null) return;

        int[] neighbours = grid.neighbors[currentTileIndex].ToArray();
        if (neighbours == null || neighbours.Length == 0) return;

        // Build a list of viable tiles we can move to
        var candidates = new List<int>();
        foreach (int idx in neighbours)
        {
            if (CanMoveTo(idx))
                candidates.Add(idx);
        }

        if (candidates.Count == 0) return;

        int target = candidates[Random.Range(0, candidates.Count)];
        MoveTo(target);
    }

    public void GainExperience(int xp)
    {
        experience += xp;
        if (level < data.xpToNextLevel.Length && experience >= data.xpToNextLevel[level - 1])
            LevelUp();
    }

    private void LevelUp()
    {
        level++;
        animator.SetTrigger("idleExperienced");
        if (level - 1 < data.abilitiesByLevel.Length && data.abilitiesByLevel[level - 1] != null)
        {
            unlockedAbilities.Add(data.abilitiesByLevel[level - 1].CreateAbility());
            // Recalculate stats when adding a new ability
            RecalculateStats();
        }
    }

    public void Equip(EquipmentData newEquip)
    {
        equipped = newEquip;
        // Optionally update visuals:
        if (equipped.equipmentPrefab != null)
            Instantiate(equipped.equipmentPrefab, transform);
        // Recalculate move/attack points and health
        RecalculateStats();
    }
    
    // New helper method to recalculate stats affected by equipment and abilities
    private void RecalculateStats()
    {
        // Base + equipment + abilities are already encapsulated in properties
        int baseMove = BaseMovePoints + EquipmentMoveBonus;
    int baseAP = BaseAttackPoints + EquipmentAttackPointsBonus + GetAbilityAttackPointsModifier();
    int maxHP = BaseHealth + EquipmentHealthBonus + GetAbilityHealthModifier();

        // Apply targeted bonuses from techs/cultures
        if (owner != null && data != null)
        {
            var agg = AggregateUnitBonusesLocal(owner, data);
            // Apply additive first
            baseMove += agg.moveAdd;
            baseAP += agg.apAdd;
            maxHP += agg.healthAdd;
            // Apply multiplicative
            baseMove = Mathf.RoundToInt(baseMove * (1f + agg.movePct));
            baseAP = Mathf.RoundToInt(baseAP * (1f + agg.apPct));
            maxHP = Mathf.RoundToInt(maxHP * (1f + agg.healthPct));
            // Attack/Defense/Range/Morale handled dynamically via getters or in combat; keep HP/move/AP here
            if (equipped != null)
            {
                var eagg = AggregateEquipBonusesLocal(owner, equipped);
                baseMove = Mathf.RoundToInt((baseMove + eagg.moveAdd) * (1f + eagg.movePct));
                baseAP = Mathf.RoundToInt((baseAP + eagg.apAdd) * (1f + eagg.apPct));
                maxHP = Mathf.RoundToInt((maxHP + eagg.healthAdd) * (1f + eagg.healthPct));
            }
        }

        currentMovePoints = baseMove;
        currentAttackPoints = baseAP;
        currentHealth = Mathf.Min(currentHealth, maxHP);
    }

    // Added helper method for City.cs usage
    public CombatUnit InitializeAndReturn(CombatUnitData unitData, Civilization unitOwner, int tileIndex)
    {
        Initialize(unitData, unitOwner);
        // Position unit on the appropriate tile with proper surface orientation
        // Ensure grid is initialized before calling PositionUnitOnSurface
        if (grid == null)
        {
            // Use GameManager API for multi-planet support
        planet = GameManager.Instance?.GetCurrentPlanetGenerator();
            if (planet != null)
            {
                grid = planet.Grid;
            }
        }

        if (grid != null)
        {
            PositionUnitOnSurface(grid, tileIndex);
            currentTileIndex = tileIndex; // Make sure to set the currentTileIndex
        }
        else
        {
            Debug.LogError($"CombatUnit {gameObject.name} could not find SphericalHexGrid to position itself on tile {tileIndex}.");
            return null;
        }
        return this;
    }

    /// <summary>
    /// Properly positions and orients the unit on the planet surface
    /// </summary>
    public void PositionUnitOnSurface(SphericalHexGrid G, int tileIndex) // Renamed parameter to avoid conflict
    {
        if (G == null)
        {
            Debug.LogError("SphericalHexGrid reference is null in PositionUnitOnSurface.");
            return;
        }

        // Get the extruded center of the tile in world space. This is the new surface position.
        Vector3 tileSurfaceCenter = TileDataHelper.Instance.GetTileSurfacePosition(tileIndex);
        
        // Set unit position directly on the surface. Any small visual offset (like for unit height)
        // should ideally be handled by the model's pivot point within the prefab itself.
        transform.position = tileSurfaceCenter;

        // The surface normal is just the direction from the planet's center to the tile's surface center
        Vector3 calculatedSurfaceNormal = (tileSurfaceCenter - planet.transform.position).normalized;
        
        // Orient unit to stand upright on the surface
        Vector3 worldUp = Vector3.up; // A general world up direction
        if (Vector3.Dot(worldUp, calculatedSurfaceNormal) > 0.99f || Vector3.Dot(worldUp, calculatedSurfaceNormal) < -0.99f)
        {
            worldUp = Vector3.forward;
        }

        Vector3 unitForward = Vector3.Cross(transform.right, calculatedSurfaceNormal).normalized; 
        if (unitForward.sqrMagnitude < 0.001f)
        {
            unitForward = Vector3.Cross(worldUp, calculatedSurfaceNormal).normalized;
             if (unitForward.sqrMagnitude < 0.001f)
            {
                unitForward = Vector3.Cross(Vector3.left, calculatedSurfaceNormal).normalized;
            }
        }

        transform.rotation = Quaternion.LookRotation(unitForward, calculatedSurfaceNormal);
        currentTileIndex = tileIndex; // Update the unit's current tile index
    }

    private void UpdateIdleAnimation()
    {
        string trigger = level == 1 ? "idleYoung" : "idleExperienced";
        animator.SetTrigger(trigger);
    }
    
    public int currentTileIndex;
    public float moveSpeed = 2f;
    public bool isMoving { get; set; }

    // Event fired when multi-tile move finishes
    public event System.Action OnMovementComplete;

    /// <summary>
    /// Requests a move to the target tile index.
    /// Will pathfind and animate until movement points run out or destination reached.
    /// </summary>
    public void MoveTo(int targetTileIndex)
    {
        var path = UnitMovementController.Instance.FindPath(currentTileIndex, targetTileIndex);
        if (path == null || path.Count == 0)
            return;

        StopAllCoroutines();
        StartCoroutine(UnitMovementController.Instance.MoveAlongPath(this, path));
    }

    /// <summary>
    /// Resets movement and attack points at start of turn. Also replenishes morale.
    /// </summary>
    public void ResetForNewTurn()
    {
        // Calculate base move points including equipment bonuses
        int baseMove = BaseMovePoints + EquipmentMoveBonus;
        
        // If trapped, decrement duration and block movement for this turn
        if (IsTrapped)
        {
            trappedTurnsRemaining = Mathf.Max(0, trappedTurnsRemaining - 1);
            currentMovePoints = 0;
        }
        else
        {
        // Apply winter penalty if applicable
        if (hasWinterPenalty && ClimateManager.Instance != null && 
            ClimateManager.Instance.currentSeason == Season.Winter)
        {
            currentMovePoints = Mathf.Max(1, baseMove - 1);
        }
        else
        {
            currentMovePoints = baseMove;
        }
        }
        
        currentAttackPoints = MaxAttackPoints;
        
        // Morale replenishment
        int moraleRecovery = 10; // Default minimum recovery
        ChangeMorale(moraleRecovery);
        
        // Clear routed flag if morale is above 0
        if (isRouted && currentMorale > 0)
            isRouted = false;
            
        // Check for damage from hazardous biomes
        CheckForHazardousBiomeDamage();
    }

    /// <summary>
    /// Checks if the unit is on a hazardous biome and applies damage if needed
    /// </summary>
    private void CheckForHazardousBiomeDamage()
    {
        if (currentTileIndex < 0) return;
        
        // Get tile data
        var (tileData, isMoonTile) = TileDataHelper.Instance.GetTileData(currentTileIndex);
        if (tileData == null) return;
        
        // Check if the biome can cause damage
        if (BiomeHelper.IsDamagingBiome(tileData.biome))
        {
            float damagePercent = BiomeHelper.GetBiomeDamage(tileData.biome);
            int damageAmount = Mathf.CeilToInt(MaxHealth * damagePercent);
            
            // Apply damage
            ApplyDamage(damageAmount);
            
            // Notify player if this is their unit
            if (owner != null && owner.isPlayerControlled && UIManager.Instance != null)
            {
                UIManager.Instance.ShowNotification($"{data.unitName} took {damageAmount} damage from {tileData.biome} terrain!");
            }
        }
    }

    /// <summary>
    /// Check if unit has enough movement points for a given cost
    /// </summary>
    public bool CanMove(int movementCost)
    {
        return currentMovePoints >= movementCost;
    }
    
    /// <summary>
    /// Deduct movement points safely
    /// </summary>
    public void DeductMovementPoints(int amount)
    {
        currentMovePoints = Mathf.Max(0, currentMovePoints - amount);
    }
    
    /// <summary>
    /// Safely trigger the OnMovementComplete event from external systems
    /// </summary>
    public void TriggerMovementComplete()
    {
        OnMovementComplete?.Invoke();
    }

    private int CountAdjacentAllies(int tileIndex)
    {
        int count = 0;
        var (tileData, isMoon) = TileDataHelper.Instance.GetTileData(tileIndex);
        if (tileData == null) return 0;

        SphericalHexGrid currentGrid = grid;
        if (currentGrid == null) return 0;

        int[] neighbours = TileDataHelper.Instance.GetTileNeighbors(tileIndex);
        if (neighbours != null)
        {
            foreach (int idx in neighbours)
            {
                var (neighborTileData, _) = TileDataHelper.Instance.GetTileData(idx);
                if (neighborTileData != null)
                {
                    // Get the GameObject from the occupantId
                    int occupantId = neighborTileData.occupantId;
                    if (occupantId != 0)
                    {
                        var objectWithId = UnitRegistry.GetObject(occupantId);
                        if (objectWithId != null)
                        {
                            CombatUnit unit = objectWithId.GetComponent<CombatUnit>();
                            if (unit != null && unit.owner == this.owner)
                            {
                                count++;
                            }
                        }
                    }
                }
            }
        }
        return count;
    }
    


    // Transport System Methods

    /// <summary>
    /// Attempts to load a unit into this transport.
    /// </summary>
    /// <param name="unit">The unit to load</param>
    /// <returns>True if successful, false otherwise</returns>
    public bool LoadUnit(CombatUnit unit)
    {
        // Check if this unit is a transport
        if (!data.isTransport)
            return false;
            
        // Check if transport is at capacity
        if (transportedUnits.Count >= data.transportCapacity)
            return false;
            
        // Check if unit belongs to same owner
        if (unit.owner != owner)
            return false;
            
        // Check if unit is adjacent or on same tile
        bool isAdjacent = false;
        if (unit.currentTileIndex == currentTileIndex)
        {
            isAdjacent = true;
        }
        else
        {
            var (tileData, isMoon) = TileDataHelper.Instance.GetTileData(currentTileIndex);
            SphericalHexGrid currentGrid = grid;
            int[] neighbors = TileDataHelper.Instance.GetTileNeighbors(currentTileIndex);
            foreach (int neighbor in neighbors)
            {
                if (unit.currentTileIndex == neighbor)
                {
                    isAdjacent = true;
                    break;
                }
            }
        }
        
        if (!isAdjacent)
            return false;
            
        // Load the unit
        transportedUnits.Add(unit);
        
        // Update the loaded unit's state
        unit.IsTransported = true;
        unit.TransportingUnit = this;
        unit.OnLoadedIntoTransport.Invoke(this);
        
        // Hide the unit visually
        unit.gameObject.SetActive(false);
        
        // Fire event for UI updates
        OnUnitLoaded.Invoke(unit);
        
        return true;
    }
    
    /// <summary>
    /// Unloads a transported unit to a specific tile.
    /// </summary>
    /// <param name="unit">The unit to unload</param>
    /// <param name="targetTileIndex">The tile to unload to</param>
    /// <returns>True if successful, false otherwise</returns>
    public bool UnloadUnit(CombatUnit unit, int targetTileIndex)
    {
        // Check if the unit is being transported by this transport
        if (!transportedUnits.Contains(unit))
            return false;
            
        // Check if target tile is adjacent or the same tile
        bool isValidTile = false;
        if (targetTileIndex == currentTileIndex)
        {
            isValidTile = true;
        }
        else
        {
            var (tileData, isMoon) = TileDataHelper.Instance.GetTileData(currentTileIndex);
            SphericalHexGrid currentGrid = grid;
            int[] neighbors = TileDataHelper.Instance.GetTileNeighbors(currentTileIndex);
            foreach (int neighbor in neighbors)
            {
                if (targetTileIndex == neighbor)
                {
                    isValidTile = true;
                    break;
                }
            }
        }
        
        if (!isValidTile)
            return false;
            
        // Check if the unit can move to the target tile
        if (!unit.CanMoveTo(targetTileIndex))
            return false;
            
        // Remove from transport
        transportedUnits.Remove(unit);
        
        // Update the unloaded unit's state
        unit.IsTransported = false;
        unit.TransportingUnit = null;
        unit.OnUnloadedFromTransport.Invoke(this);
        
        // Position the unit at the target tile and show it
        unit.gameObject.SetActive(true);
        var (targetTileData, isTargetMoon) = TileDataHelper.Instance.GetTileData(targetTileIndex);
        SphericalHexGrid targetGrid = grid;
        unit.transform.position = targetGrid.tileCenters[targetTileIndex];
        unit.currentTileIndex = targetTileIndex;
        
        // Update tile occupancy
        TileDataHelper.Instance.SetTileOccupant(targetTileIndex, unit.gameObject);

    // Trigger trap if unloading onto a trapped tile
    ImprovementManager.Instance?.NotifyUnitEnteredTile(targetTileIndex, unit);
        
        // Fire event for UI updates
        OnUnitUnloaded.Invoke(unit);
        
        return true;
    }
    
    /// <summary>
    /// Gets a list of all units currently transported.
    /// </summary>
    public List<CombatUnit> GetTransportedUnits()
    {
        return new List<CombatUnit>(transportedUnits);
    }
    
    /// <summary>
    /// Checks if this transport has capacity for more units.
    /// </summary>
    public bool HasRemainingCapacity()
    {
        return data.isTransport && transportedUnits.Count < data.transportCapacity;
    }
    
    /// <summary>
    /// Gets the number of units that can still be loaded.
    /// </summary>
    public int GetRemainingCapacity()
    {
        if (!data.isTransport) return 0;
        return data.transportCapacity - transportedUnits.Count;
    }
    
    // END Transport System Methods

    // NEW EQUIPMENT METHODS
    
    /// <summary>
    /// Equips an item in the appropriate slot based on its type
    /// </summary>
    public virtual void EquipItem(EquipmentData equipmentData)
    {
        if (equipmentData == null) return;
        
        bool changed = false;
        
        // Store in the specific slot based on type
        switch (equipmentData.equipmentType)
        {
            case EquipmentType.Weapon:
                if (equippedWeapon != equipmentData)
                {
                    equippedWeapon = equipmentData;
                    changed = true;
                }
                break;
            case EquipmentType.Shield:
                if (equippedShield != equipmentData)
                {
                    equippedShield = equipmentData;
                    changed = true;
                }
                break;
            case EquipmentType.Armor:
                if (equippedArmor != equipmentData)
                {
                    equippedArmor = equipmentData;
                    changed = true;
                }
                break;
            case EquipmentType.Miscellaneous:
                if (equippedMiscellaneous != equipmentData)
                {
                    equippedMiscellaneous = equipmentData;
                    changed = true;
                }
                break;
        }
        
        // Update the general equipped reference too for backward compatibility
        equipped = equipmentData;
        
        // Update visuals if something changed
        if (changed)
        {
            UpdateEquipmentVisuals();
            
            // Recalculate stats that might be affected by equipment
            RecalculateStats();
            
            // Notify listeners
            OnEquipmentChanged?.Invoke();
        }
    }
    
    /// <summary>
    /// Updates the visual representation of all equipped items
    /// </summary>
    public virtual void UpdateEquipmentVisuals()
    {
        // Remove any existing equipment visual objects
        foreach (var item in equippedItemObjects.Values)
        {
            if (item != null)
                Destroy(item);
        }
        equippedItemObjects.Clear();
        
        // Dictionary to batch update all slots
        var slotsToUpdate = new Dictionary<EquipmentType, (EquipmentData data, Transform holder)>();
        
        // Only add slots with valid holders and equipment
        if (weaponHolder != null && equippedWeapon != null)
            slotsToUpdate[EquipmentType.Weapon] = (equippedWeapon, weaponHolder);
            
        if (shieldHolder != null && equippedShield != null)
            slotsToUpdate[EquipmentType.Shield] = (equippedShield, shieldHolder);
            
        if (armorHolder != null && equippedArmor != null)
            slotsToUpdate[EquipmentType.Armor] = (equippedArmor, armorHolder);
            
        if (miscHolder != null && equippedMiscellaneous != null)
            slotsToUpdate[EquipmentType.Miscellaneous] = (equippedMiscellaneous, miscHolder);
        
        // Process all slots
        foreach (var entry in slotsToUpdate)
        {
            EquipmentType type = entry.Key;
            EquipmentData itemData = entry.Value.data;
            Transform holder = entry.Value.holder;
            
            UpdateEquipmentSlot(type, itemData, holder);
        }
    }
    
    /// <summary>
    /// Updates a specific equipment slot with the specified item
    /// </summary>
    protected virtual void UpdateEquipmentSlot(EquipmentType type, EquipmentData itemData, Transform holder)
    {
        if (holder == null || itemData == null || itemData.equipmentPrefab == null)
            return;
            
        // Clear existing equipment in this slot
        for (int i = 0; i < holder.childCount; i++)
        {
            Destroy(holder.GetChild(i).gameObject);
        }
        
        // Instantiate the new equipment
        GameObject equipObj = Instantiate(itemData.equipmentPrefab, holder);
        equipObj.transform.localPosition = Vector3.zero;
        equipObj.transform.localRotation = Quaternion.identity;
        
        // Store reference to the instantiated object
        equippedItemObjects[type] = equipObj;
    }
    
    /// <summary>
    /// Removes equipment from a specific slot
    /// </summary>
    public virtual void UnequipItem(EquipmentType type)
    {
        bool changed = false;
        
        switch (type)
        {
            case EquipmentType.Weapon:
                if (equippedWeapon != null)
                {
                    equippedWeapon = null;
                    changed = true;
                }
                break;
            case EquipmentType.Shield:
                if (equippedShield != null)
                {
                    equippedShield = null;
                    changed = true;
                }
                break;
            case EquipmentType.Armor:
                if (equippedArmor != null)
                {
                    equippedArmor = null;
                    changed = true;
                }
                break;
            case EquipmentType.Miscellaneous:
                if (equippedMiscellaneous != null)
                {
                    equippedMiscellaneous = null;
                    changed = true;
                }
                break;
        }
        
        if (changed)
        {
            // Clear the visual for this slot
            if (equippedItemObjects.TryGetValue(type, out GameObject equipObj) && equipObj != null)
            {
                Destroy(equipObj);
                equippedItemObjects.Remove(type);
            }
            
            // Recalculate stats
            RecalculateStats();
            
            // Notify listeners
            OnEquipmentChanged?.Invoke();
        }
    }

    private void HandleMovementCompleted(GameEventManager.UnitMovementEventArgs args)
    {
        if (args.Unit == this)
        {
            // Handle any post-movement logic
        }
    }

    private void HandleCombatStarted(GameEventManager.CombatEventArgs args)
    {
        if (args.Defender == this)
        {
            // Handle being attacked
        }
    }

    private void HandleDamageApplied(GameEventManager.CombatEventArgs args)
    {
        if (args.Defender == this)
        {
            // Handle damage taken
            OnHealthChanged?.Invoke(currentHealth, MaxHealth);
        }
    }

    private void UpdateUnitLabelHealth(int newHealth, int maxHealth)
    {
        if (unitLabelInstance != null)
        {
            string ownerName = owner != null && owner.civData != null ? owner.civData.civName : "Unknown";
            unitLabelInstance.UpdateLabel(data.unitName, ownerName, newHealth, maxHealth);
        }
    }

    // Called by Civilization when civ-wide bonuses (tech/culture) change.
    // Intentionally does not refill movement or attack points mid-turn.
    public void OnCivBonusesChanged()
    {
        // Clamp current health to new max if modifiers decreased it; keep current otherwise.
        int before = currentHealth;
        int max = MaxHealth; // property already includes tech/culture/equipment
        currentHealth = Mathf.Min(currentHealth, max);
        if (currentHealth != before)
        {
            OnHealthChanged?.Invoke(currentHealth, max);
        }
        // Movement/AP maximums might increase due to tech, but we don't refill here;
        // they'll be applied on next ResetForNewTurn via RecalculateStats.
    }

    // --- Trap mechanics ---
    private int trappedTurnsRemaining = 0;
    public bool IsTrapped => trappedTurnsRemaining > 0;

    public void ApplyTrap(int turns)
    {
        trappedTurnsRemaining = Mathf.Max(trappedTurnsRemaining, turns);
        // Optional: visual/UI feedback could be triggered here
    }

    /// <summary>
    /// Handle mouse clicks on the combat unit
    /// </summary>
    void OnMouseDown()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null && 
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
        {
            // Click was on UI, ignore
            Debug.Log($"[CombatUnit] Click on {data.unitName} ignored, was on UI.");
            return;
        }
        
        Debug.Log($"[CombatUnit] Clicked on {data.unitName}. Owner: {owner?.civData?.civName ?? "None"}");

        // Use the UnitSelectionManager for selection
        if (UnitSelectionManager.Instance != null)
        {
            UnitSelectionManager.Instance.SelectUnit(this);
        }
        else
        {
            // Fallback to old behavior if UnitSelectionManager is not available
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowUnitInfoPanelForUnit(this);
                Debug.Log($"[CombatUnit] Requested UnitInfoPanel for {data.unitName}");

                // Fallback notification if UnitInfoPanel is not available
                if (UIManager.Instance.unitInfoPanel == null || !UIManager.Instance.unitInfoPanel.activeInHierarchy)
                {
                    string msg = $"{data.unitName} (Combat)\nHealth: {currentHealth}/{MaxHealth}\nAttack: {CurrentAttack}  Defense: {CurrentDefense}\nMove: {currentMovePoints}/{BaseMovePoints}";
                    UIManager.Instance.ShowNotification(msg);
                }
            }
            else
            {
                Debug.LogError($"[CombatUnit] UIManager.Instance is null. Cannot show notification for {data.unitName}.");
            }
        }
    }
}