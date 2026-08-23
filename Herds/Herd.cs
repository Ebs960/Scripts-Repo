using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class Herd : MonoBehaviour
{
    [SerializeField] private string persistentId;
    public string PersistentId => string.IsNullOrEmpty(persistentId) ? (persistentId = Guid.NewGuid().ToString("N")) : persistentId;
    public void RestorePersistentId(string value) { if (!string.IsNullOrWhiteSpace(value)) persistentId = value; }
    [Header("Core")]
    public Civilization owner;
    public int planetIndex = 0;
    public int currentTileIndex = -1;
    [Tooltip("Optional human-readable name for this herd (displayed in UI). If empty, GameObject name is used.")]
    public string herdName;

    public enum HerdSpecies { Chicken, Cow, Pig, Sheep, Goat, Elephant, Camel, Other }

    [System.Serializable]
    public class HerdEntry { public HerdSpecies species; public int count = 0; }

    [Header("Stored Units")]
    [Tooltip("Legacy mixed storage. Migrated to the typed collections on enable.")]
    [SerializeField, HideInInspector] public List<BaseUnit> storedUnits = new List<BaseUnit>();
    [SerializeField] private List<CombatUnit> militaryGarrison = new List<CombatUnit>();
    [SerializeField] private List<BaseUnit> storedCivilians = new List<BaseUnit>();
    [Min(0)] public int baseGarrisonCapacity = 4;
    public IReadOnlyList<CombatUnit> MilitaryGarrison => militaryGarrison;
    public IReadOnlyList<BaseUnit> StoredCivilians => storedCivilians;
    public int GarrisonCapacity => Mathf.Max(0, baseGarrisonCapacity);
    public bool HasMilitaryDefenders => militaryGarrison.Any(u => u != null && u.currentHealth > 0);

    [Header("Animals (counts, abstract)")]
    public List<HerdEntry> animals = new List<HerdEntry>();

    [Header("Grazing")]
    // Food currently held by herd from grazing (not civilization food stockpile)
    public int foodReserve = 0;
    // Last amount grazed on most recent grazing tick
    public int lastGrazedAmount = 0;

    [Header("Instance Config (per-herd; city-like)")]
    [Tooltip("Base storage capacity for this herd instance (editable per-herd).")]
    public int baseStorage = 50;
    [Tooltip("Which building types this herd instance may construct (overrides HerdData if present)")]
    public BuildingData[] allowedBuildings;

    [System.Serializable]
    public class HerdStartingAnimal { public HerdSpecies species; public int count = 0; }
    [Tooltip("Starting animals for this herd instance")]
    public HerdStartingAnimal[] startingAnimals;

    [Header("Governor")]
    [Tooltip("Governor assigned to this herd (can be null)")]
    public Governor governor;

    [Header("Level")]
    [Tooltip("Herd development level. Contributes 1 point per level to the assigned governor's PowerRank. Progression systems may raise this later.")]
    [Min(1)] public int level = 1;

    [Header("Pack/Settle")]
    [Tooltip("When true the herd is packed and may move; when false the herd is settled (camp) and generates full yields.")]
    public bool isPacked = true;

    [Header("Movement")]
    [Tooltip("Current movement points available for this herd (reset per turn)")]
    public int movementPoints = 2;
    [Tooltip("Maximum movement points this herd has per turn")]
    public int maxMovementPoints = 2;

    // Returns food consumption for the species per 100 animals.
    // For every 100 animals of the species, they consume this much food.
    public static int GetFoodConsumptionPer100(HerdSpecies s)
    {
        switch (s)
        {
            case HerdSpecies.Chicken: return 1;   // 100 chickens consume 1 food
            case HerdSpecies.Cow: return 2;       // 100 cows consume 2 food
            case HerdSpecies.Pig: return 1;       // 100 pigs consume 1 food
            case HerdSpecies.Sheep: return 1;     // 100 sheep consume 1 food
            case HerdSpecies.Goat: return 1;      // 100 goats consume 1 food
            case HerdSpecies.Elephant: return 3;  // 100 elephants consume 3 food
            case HerdSpecies.Camel: return 2;     // 100 camels consume 2 food
            default: return 1;
        }
    }

    // Note: Herds store abstract species counts only. CombatUnitData is not used here
    // except when converting captured units — see AddAnimals(CombatUnitData, int).

    [Header("Storage")]
    // How much food the herd can store from grazing / purchases
    public int storageCapacity = 50;

    // Retained only so old Unity scenes deserialize without losing data. Never used by combat.
    #pragma warning disable CS0414 // Serialized only to migrate legacy scene and prefab data.
    [Obsolete("Herd combat uses MilitaryGarrison CombatUnits."), SerializeField, HideInInspector] private int defenseRating = 50;
    [Obsolete("Herd combat uses MilitaryGarrison CombatUnits."), SerializeField, HideInInspector] private int maxDefense = 50;
    [Obsolete("Herd has no combat HP."), SerializeField, HideInInspector] private int health = 100;
    [Obsolete("Herd has no combat HP."), SerializeField, HideInInspector] private int maxHealth = 100;
    #pragma warning restore CS0414

    [Header("Livestock Upkeep / Raids")]
    [Range(0f, 1f)] public float predatorLivestockLossPct = 0.15f;
    [Range(0f, 1f)] public float predatorFoodLossPct = 0.25f;
    [HideInInspector] public int lastFoodConsumption;
    [HideInInspector] public int lastStarvationLoss;
    public int FoodRequiredPerTurn => animals.Sum(e => e == null ? 0 : Mathf.CeilToInt(Mathf.Max(0, e.count) * GetFoodConsumptionPer100(e.species) / 100f));
    public bool IsStarving => foodReserve < FoodRequiredPerTurn;

    [Header("Disease")]
    [Tooltip("Active diseases currently afflicting this herd.")]
    public List<DiseaseInstance> activeDiseases = new List<DiseaseInstance>();

    [Tooltip("Immunity cooldowns: disease → remaining immune turns after recovery.")]
    public Dictionary<DiseaseData, int> diseaseImmunities = new Dictionary<DiseaseData, int>();

    void OnEnable()
    {
        MigrateLegacyStoredUnits();
        if (HerdManager.Instance != null) HerdManager.Instance.RegisterHerd(this);
        if (owner != null)
        {
            try { owner.herds.Add(this); } catch { }
        }
        // initialize storage from per-herd baseStorage
        try { storageCapacity = Mathf.Max(1, baseStorage); } catch { }
        // create label UI for this herd
        try { CreateLabelUI(); UpdateLabelUI(); } catch { }
        // ensure visual prefab matches pack/settle state
        try { UpdateVisualRepresentation(); } catch { }
    }

    void OnDisable()
    {
        if (HerdManager.Instance != null) HerdManager.Instance.UnregisterHerd(this);
        if (owner != null)
        {
            try { owner.herds.Remove(this); } catch { }
        }
        try { if (worldUIInstance != null) Destroy(worldUIInstance); worldUIInstance = null; worldUI = null; } catch { }
        try { if (visualInstance != null) Destroy(visualInstance); visualInstance = null; } catch { }
    }

    /// <summary>
    /// Called externally (e.g., from a turn manager) once per turn to process grazing.
    /// Sums forage shares from current tile + neighbors and adds to `foodReserve` according
    /// to biome-based consumption intervals.
    /// </summary>
    public void ProcessGrazingTick(int round)
    {
        lastGrazedAmount = 0;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null || !ts.IsReady()) return;

        // Build list of considered tiles: current tile + neighbors
        var tiles = new List<int>();
        if (currentTileIndex >= 0) tiles.Add(currentTileIndex);
        if (currentTileIndex >= 0)
        {
            var neigh = ts.GetNeighbors(currentTileIndex);
            foreach (var n in neigh) tiles.Add(n);
        }

        // For each tile, decide whether it triggers grazing this turn based on biome interval
        foreach (var t in tiles)
        {
            var td = ts.GetTileData(t);
            if (td == null) continue;
            int interval = HerdManager.Instance != null ? HerdManager.Instance.GetConsumptionIntervalForBiome(td.biome) : 1;

            // Simple deterministic schedule: use round modulo interval to decide when to graze
            if (interval <= 1 || (round % interval) == 0)
            {
                int baseFood = HerdManager.Instance != null ? HerdManager.Instance.ComputeHerdForageShare(planetIndex, t, this) : Mathf.Max(0, td.food);
                float multiplier = 1f;
                try
                {
                    if (ClimateManager.Instance != null)
                    {
                        var season = ClimateManager.Instance.GetSeasonForPlanet(planetIndex);
                        var resp = ClimateManager.Instance.GetSeasonResponseForTile(t, season);
                        multiplier = resp != null ? resp.yieldMultiplier : 1f;
                    }
                }
                catch { }

                int share = Mathf.FloorToInt(baseFood * multiplier);
                lastGrazedAmount += Mathf.Max(0, share);
            }
        }

        // Apply disease forage penalty
        if (activeDiseases != null && activeDiseases.Count > 0 && lastGrazedAmount > 0)
        {
            float forageMult = 1f;
            foreach (var di in activeDiseases)
            {
                if (di == null || di.data == null) continue;
                var totals = owner != null ? owner.GetDiseaseModifierTotals(di.data, herdContext: this) : default;
                forageMult -= di.data.herdForagePenaltyPct * totals.HerdForagePenaltyMultiplier;
            }
            forageMult = Mathf.Max(0f, forageMult);
            lastGrazedAmount = Mathf.FloorToInt(lastGrazedAmount * forageMult);
        }

        // Add to reserve (cap by storage capacity)
        if (lastGrazedAmount > 0)
        {
            try { foodReserve = Mathf.Min(storageCapacity, foodReserve + lastGrazedAmount); } catch { foodReserve += lastGrazedAmount; }
        }
        if (worldUI != null) worldUI.MarkDirty();
    }

    /// <summary>
    /// Process disease effects for this herd: animal mortality, duration ticking, and immunity.
    /// Called per turn by DiseaseManager.
    /// </summary>
    public void ProcessDiseaseTurn()
    {
        if (activeDiseases == null || activeDiseases.Count == 0) return;

        for (int i = activeDiseases.Count - 1; i >= 0; i--)
        {
            var di = activeDiseases[i];
            if (di == null || di.data == null) { activeDiseases.RemoveAt(i); continue; }
            var totals = owner != null ? owner.GetDiseaseModifierTotals(di.data, herdContext: this) : default;

            // Animal mortality: kill a percentage of each species
            float mortalityRate = di.data.herdMortalityRatePerTurn * totals.HerdMortalityMultiplier;
            if (mortalityRate > 0f && animals != null)
            {
                for (int a = animals.Count - 1; a >= 0; a--)
                {
                    var entry = animals[a];
                    if (entry == null || entry.count <= 0) continue;
                    int deaths = Mathf.Max(1, Mathf.FloorToInt(entry.count * mortalityRate));
                    entry.count = Mathf.Max(0, entry.count - deaths);
                    if (entry.count <= 0) animals.RemoveAt(a);
                }
            }

            // Tick duration
            if (!di.TickDuration())
            {
                if (di.data.immunityTurnsAfterRecovery > 0)
                    diseaseImmunities[di.data] = di.data.immunityTurnsAfterRecovery;
                activeDiseases.RemoveAt(i);
            }
        }

        // Tick immunity cooldowns
        var expiredKeys = new List<DiseaseData>();
        foreach (var kv in diseaseImmunities)
        {
            diseaseImmunities[kv.Key] = kv.Value - 1;
            if (diseaseImmunities[kv.Key] <= 0)
                expiredKeys.Add(kv.Key);
        }
        foreach (var key in expiredKeys)
            diseaseImmunities.Remove(key);
    }

    /// <summary>
    /// Returns the total number of animals across all species in this herd.
    /// </summary>
    public int GetTotalAnimalCount()
    {
        int total = 0;
        if (animals != null)
            foreach (var e in animals)
                if (e != null) total += e.count;
        return total;
    }

    /// <summary>
    /// Returns true if this herd has immunity to the given disease.
    /// </summary>
    public bool HasDiseaseImmunity(DiseaseData disease)
    {
        if (disease == null) return false;
        if (owner != null && disease.immunityTechs != null)
        {
            foreach (var tech in disease.immunityTechs)
                if (tech != null && owner.researchedTechs.Contains(tech))
                    return true;
        }
        if (owner != null && owner.GetDiseaseModifierTotals(disease, herdContext: this).grantsImmunity)
            return true;
        if (diseaseImmunities != null && diseaseImmunities.ContainsKey(disease))
            return true;
        return false;
    }

    /// <summary>
    /// Returns true if this herd is currently infected by the given disease.
    /// </summary>
    public bool HasDisease(DiseaseData disease)
    {
        if (disease == null || activeDiseases == null) return false;
        return activeDiseases.Exists(d => d.data == disease);
    }

    /// <summary>
    /// Infects this herd with a disease. No-op if already infected or immune.
    /// </summary>
    public bool InfectWithDisease(DiseaseData disease)
    {
        if (disease == null) return false;
        if (HasDisease(disease)) return false;
        if (HasDiseaseImmunity(disease)) return false;
        int duration = disease.baseDuration > 0 ? disease.baseDuration : -1;
        if (owner != null && duration > 0)
        {
            var totals = owner.GetDiseaseModifierTotals(disease, herdContext: this);
            duration = Mathf.Max(1, Mathf.RoundToInt(duration * totals.DurationMultiplier));
        }
        activeDiseases.Add(new DiseaseInstance(disease, duration));
        return true;
    }

    /// <summary>
    /// Cures a specific disease from this herd. Optionally grants immunity.
    /// </summary>
    public bool CureDisease(DiseaseData disease, bool grantImmunity = true)
    {
        if (disease == null || activeDiseases == null) return false;
        int idx = activeDiseases.FindIndex(d => d.data == disease);
        if (idx < 0) return false;
        if (grantImmunity && disease.immunityTurnsAfterRecovery > 0)
            diseaseImmunities[disease] = disease.immunityTurnsAfterRecovery;
        activeDiseases.RemoveAt(idx);
        return true;
    }

    /// <summary>
    /// Add animals to the herd (by species). Herd stores abstract species counts only.
    /// </summary>
    public void AddAnimals(HerdSpecies species, int count)
    {
        if (count <= 0) return;
        var e = animals.Find(x => x.species == species);
        if (e == null) animals.Add(new HerdEntry { species = species, count = count }); else e.count += count;
    }

    // Conversion overload: map a captured CombatUnitData into a HerdSpecies and add
    public void AddAnimals(CombatUnitData type, int count)
    {
        if (type == null || count <= 0) return;
        // Prefer explicit species set on unit data
        HerdSpecies s = HerdSpecies.Other;
        try { s = type.captureSpecies; } catch { s = HerdSpecies.Other; }
        // Do NOT infer species from unit name; use the explicit `captureSpecies` assignment only.
        AddAnimals(s, count);
    }

    /// <summary>
    /// Store a unit inside this herd (e.g., the worker who founded it).
    /// Unit will be deactivated and marked as stored until unstored.
    /// </summary>
    public bool StoreUnit(BaseUnit unit)
    {
        if (unit == null) return false;
        if (unit is CombatUnit combat) return TryAddToGarrison(combat);
        if (owner != null && unit.owner != owner) return false;
        if (storedUnits == null) storedUnits = new System.Collections.Generic.List<BaseUnit>();
        if (storedUnits.Contains(unit)) return false;
        if (unit.currentTileIndex != currentTileIndex) return false;

        var occ = TileOccupancyManager.GetForPlanet(unit.planetIndex) ?? TileOccupancyManager.Instance;
        if (occ != null)
        {
            occ.ClearOccupant(currentTileIndex, TileLayer.Surface);
        }

        unit.isStored = true;
        unit.storedInHerd = this;
        unit.currentTileIndex = -1;
        unit.gameObject.SetActive(false);

        storedUnits.Add(unit);
        if (!storedCivilians.Contains(unit)) storedCivilians.Add(unit);
        return true;
    }

    /// <summary>
    /// Try to unstore a unit back to the herd tile or an adjacent free tile.
    /// </summary>
    public bool TryUnstoreUnit(BaseUnit unit)
    {
        if (unit == null || storedUnits == null || !storedUnits.Contains(unit)) return false;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;

        // First try herd tile
        if (occ != null && occ.GetOccupantObject(currentTileIndex, TileLayer.Surface) == null)
            return UnstoreToTile(unit, currentTileIndex);

        // Then try neighbors
        if (ts != null)
        {
            var neighbors = ts.GetNeighbors(currentTileIndex);
            if (neighbors != null)
            {
                foreach (var n in neighbors)
                {
                    if (n < 0) continue;
                    var td = ts.GetTileData(n);
                    if (td == null || !td.isPassable) continue;
                    if (occ != null && occ.GetOccupantObject(n, TileLayer.Surface) != null) continue;
                    return UnstoreToTile(unit, n);
                }
            }
        }

        return false;
    }

    private bool UnstoreToTile(BaseUnit unit, int tile)
    {
        if (unit == null) return false;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;

        if (ts != null)
        {
            Vector3 pos = ts.GetTileCenterFlat(tile);
            unit.transform.position = pos;
        }

        unit.currentTileIndex = tile;
        try { unit.RegisterToRegistry(); } catch { }
        if (occ != null)
            occ.SetOccupant(tile, unit.gameObject, TileLayer.Surface);

        unit.gameObject.SetActive(true);
        unit.isStored = false;
        unit.storedInHerd = null;

        storedUnits.Remove(unit);
        storedCivilians.Remove(unit);
        return true;
    }

    private void MigrateLegacyStoredUnits()
    {
        if (militaryGarrison == null) militaryGarrison = new List<CombatUnit>();
        if (storedCivilians == null) storedCivilians = new List<BaseUnit>();
        if (storedUnits == null) storedUnits = new List<BaseUnit>();
        foreach (var unit in storedUnits.Where(x => x != null).ToList())
            if (unit is CombatUnit combat) { if (!militaryGarrison.Contains(combat)) militaryGarrison.Add(combat); }
            else if (!storedCivilians.Contains(unit)) storedCivilians.Add(unit);
        storedUnits.RemoveAll(x => x == null || x is CombatUnit);
    }

    public bool TryAddToGarrison(CombatUnit unit)
    {
        if (unit == null || unit.owner != owner || unit.planetIndex != planetIndex || militaryGarrison.Contains(unit)) return false;
        if (militaryGarrison.Count >= GarrisonCapacity || (unit.currentTileIndex >= 0 && unit.currentTileIndex != currentTileIndex)) return false;
        (TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance)?.ClearOccupantById(unit.currentTileIndex, unit.currentLayer, unit.gameObject.GetRuntimeId());
        militaryGarrison.Add(unit); unit.isStored = true; unit.storedInHerd = this; unit.currentTileIndex = -1; unit.gameObject.SetActive(false);
        worldUI?.MarkDirty(); return true;
    }

    /// <summary>Reconnects a unit created by the normal unit loader without changing its identity or stats.</summary>
    public bool RestoreGarrisonReference(CombatUnit unit)
    {
        if (unit == null || unit.owner != owner || militaryGarrison.Contains(unit) || militaryGarrison.Count >= GarrisonCapacity) return false;
        (TileOccupancyManager.GetForPlanet(unit.planetIndex) ?? TileOccupancyManager.Instance)?.ClearOccupantById(unit.currentTileIndex, unit.currentLayer, unit.gameObject.GetRuntimeId());
        militaryGarrison.Add(unit); unit.isStored = true; unit.storedInHerd = this; unit.planetIndex = planetIndex; unit.currentTileIndex = -1; unit.gameObject.SetActive(false);
        return true;
    }

    public bool RestoreCivilianReference(BaseUnit unit)
    {
        if (unit == null || unit.owner != owner || unit is CombatUnit || storedCivilians.Contains(unit)) return false;
        (TileOccupancyManager.GetForPlanet(unit.planetIndex) ?? TileOccupancyManager.Instance)?.ClearOccupantById(unit.currentTileIndex, unit.currentLayer, unit.gameObject.GetRuntimeId());
        storedCivilians.Add(unit); unit.isStored = true; unit.storedInHerd = this; unit.planetIndex = planetIndex; unit.currentTileIndex = -1; unit.gameObject.SetActive(false);
        if (!storedUnits.Contains(unit)) storedUnits.Add(unit);
        return true;
    }

    public bool FormArmy(IList<CombatUnit> selected, out CombatUnit representative)
    {
        representative = null;
        if (selected == null || selected.Count == 0 || selected.Distinct().Count() != selected.Count || selected.Any(x => x == null || !militaryGarrison.Contains(x))) return false;
        if (selected.Count > (owner != null ? owner.GetMaxArmySize() : CampaignArmyService.DefaultArmySize)) return false;
        string formationId = Guid.NewGuid().ToString("N");
        for (int i = 0; i < selected.Count; i++) { var u = selected[i]; militaryGarrison.Remove(u); u.isStored = false; u.storedInHerd = null; u.planetIndex = planetIndex; u.currentTileIndex = currentTileIndex; u.AssignMilitaryFormation(formationId, MilitaryFormationType.Army); u.stackSlot = i; u.gameObject.SetActive(i == 0); if (i == 0) representative = u; }
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance; if (representative != null && ts != null) representative.transform.position = ts.GetTileCenterFlat(currentTileIndex);
        (TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance)?.TryAddToStack(currentTileIndex, TileLayer.Surface, representative.gameObject, 1);
        CampaignArmyService.RefreshPresentation(representative); worldUI?.MarkDirty(); return true;
    }

    public bool TryGarrisonArmy(CombatUnit army, out string reason)
    {
        reason = string.Empty; if (army == null) { reason = "Missing army."; return false; }
        var members = CampaignArmyService.GetMembers(army);
        if (owner == null || army.owner != owner) { reason = "Herd and army must have the same owner."; return false; }
        if (army.planetIndex != planetIndex || army.currentLayer != TileLayer.Surface || army.currentTileIndex != currentTileIndex) { reason = "Army and Herd must share the same land location."; return false; }
        if (members.Count == 0 || militaryGarrison.Count + members.Count > GarrisonCapacity) { reason = $"Not enough garrison capacity ({militaryGarrison.Count + members.Count}/{GarrisonCapacity})."; return false; }
        if (members.Any(x => x == null || x.owner != owner || x.planetIndex != planetIndex || x.currentLayer != TileLayer.Surface || x.currentTileIndex != currentTileIndex || x.IsTransported || x.isStored)) { reason = "One or more army members cannot be garrisoned."; return false; }
        foreach (var u in members) TryAddToGarrison(u); return true;
    }

    public void ReleaseSurvivingGarrisonAsArmy() => FormArmy(militaryGarrison.Where(x => x != null && x.currentHealth > 0).ToList(), out _);

    public void ProcessLivestockUpkeep()
    {
        int required = FoodRequiredPerTurn; int consumed = Mathf.Min(foodReserve, required); foodReserve -= consumed; lastFoodConsumption = consumed; lastStarvationLoss = 0;
        if (consumed >= required || required <= 0) return;
        float shortage = (required - consumed) / (float)required;
        foreach (var e in animals.Where(x => x != null && x.count > 0).ToList()) { int loss = Mathf.CeilToInt(e.count * shortage); e.count = Mathf.Max(0, e.count - loss); lastStarvationLoss += loss; }
        animals.RemoveAll(x => x == null || x.count <= 0); worldUI?.MarkDirty();
    }

    public void ResolveLivestockRaid()
    {
        lastStarvationLoss = 0;
        foreach (var e in animals.Where(x => x != null && x.count > 0).ToList()) { int loss = Mathf.CeilToInt(e.count * predatorLivestockLossPct); e.count -= loss; lastStarvationLoss += loss; }
        animals.RemoveAll(x => x == null || x.count <= 0); foodReserve = Mathf.Max(0, foodReserve - Mathf.CeilToInt(foodReserve * predatorFoodLossPct)); worldUI?.MarkDirty();
    }

    public void Capture(Civilization newOwner)
    {
        if (newOwner == null || newOwner == owner) return; var old = owner;
        if (governor != null && old != null) old.RemoveGovernorFromHerd(governor, this); governor = null;
        // Defeated/retreated soldiers never change civilization with the Herd.
        militaryGarrison.RemoveAll(unit => unit == null || unit.owner == old);
        old?.herds.Remove(this); owner = newOwner; if (!newOwner.herds.Contains(this)) newOwner.herds.Add(this);
        foreach (var unit in storedCivilians.OfType<WorkerUnit>().ToList()) { unit.gameObject.SetActive(true); unit.isStored = false; unit.storedInHerd = null; unit.planetIndex = planetIndex; unit.currentTileIndex = currentTileIndex; var victor = newOwner.combatUnits.FirstOrDefault(x => x != null); if (victor != null) CivilianCaptureService.ResolveAttack(victor, unit); storedCivilians.Remove(unit); storedUnits.Remove(unit); }
        UpdateLabelUI(); worldUI?.MarkDirty();
    }

    /// <summary>
    /// Apply a herd-capable building's effects to this herd (e.g., increase storage capacity).
    /// </summary>
    public void ApplyBuilding(BuildingData building)
    {
        if (building == null) return;
        try { if (building.herdStorageBonus != 0) storageCapacity += building.herdStorageBonus; } catch { }
    }

    [Header("Structures")]
    // Buildings/structures currently attached to this herd
    public List<BuildingData> builtStructures = new List<BuildingData>();

    /// <summary>
    /// Construct or attach a building to this herd (mobile structure). Applies its effects.
    /// </summary>
    public void BuildStructure(BuildingData building)
    {
        if (building == null) return;
        // Only allow buildings that are marked buildableByHerd
        if (!building.buildableByHerd) return;
        builtStructures.Add(building);
        ApplyBuilding(building);
    }

    /// <summary>
    /// Refresh any governor-applied bonuses on this herd. Called when governor levels up or traits change.
    /// </summary>
    public void RefreshGovernorBonuses()
    {
        // Placeholder: apply governor bonuses to herd yields/buildings as needed.
        // For now this is a no-op hook so governors can be assigned to herds and notified on level-up.
    }

    // --- Simple label UI (similar to City labels) ---
    private Canvas labelCanvas;
    private TextMeshProUGUI nameTextLabel;
    private TextMeshProUGUI governorTextLabel;
    private TextMeshProUGUI ownerTextLabel;
    private UnityEngine.UI.Image ownerIconImage;
    private HerdLabel herdLabelInstance;
    [Header("Label Prefab")]
    [Tooltip("Optional prefab to use for herd world labels. Prefab should contain child objects named 'HerdName' (Text), 'HerdOwner' (Text) and optional 'HerdIcon' (Image). If not assigned, a default generated label is created.")]
    public GameObject herdLabelPrefab;
    [Header("World UI Prefab")]
    [Tooltip("Optional prefab for the herd's floating stats UI. Assign this on the root herd prefab if you want a dedicated plug-in slot similar to unit labels. If left null, the active packed/settled visual is searched for a HerdWorldUI component.")]
    public GameObject herdWorldUIPrefab;
    [Tooltip("Optional transform used as the parent/anchor when instantiating `herdWorldUIPrefab`. If null the herd root transform is used.")]
    public Transform herdWorldUIAnchor;
    
    // Runtime visual instance for this herd (packed/settled prefab)
    [HideInInspector] public GameObject visualInstance;
    [HideInInspector] public HerdWorldUI worldUI;
    private GameObject worldUIInstance;

    private void CreateLabelUI()
    {
        if (labelCanvas != null) return;

        if (herdLabelPrefab != null)
        {
            var labelGO = Instantiate(herdLabelPrefab, transform);
            // Find Canvas on prefab (or its children)
            labelCanvas = labelGO.GetComponentInChildren<Canvas>();
            if (labelCanvas != null)
            {
                labelCanvas.renderMode = RenderMode.WorldSpace;
                if (labelCanvas.worldCamera == null) labelCanvas.worldCamera = Camera.main;
            }

            // Try to find HerdLabel component first (preferred)
            herdLabelInstance = labelGO.GetComponentInChildren<HerdLabel>(true);
            if (herdLabelInstance != null)
            {
                // Initialize now with available values
                var ownerName = owner != null && owner.civData != null ? owner.civData.civName : "(No Owner)";
                var ownerIcon = owner != null && owner.civData != null ? owner.civData.icon : null;
                var displayName = string.IsNullOrEmpty(herdName) ? gameObject.name : herdName;
                herdLabelInstance.Initialize(transform, displayName, ownerName, ownerIcon, governor != null ? governor.Name : null);
            }

            // Named children lookup (fallbacks) for prefab without HerdLabel
            var nameTf = labelGO.transform.Find("HerdName");
            if (nameTf != null) nameTextLabel = nameTf.GetComponent<TextMeshProUGUI>();
            var ownerTf = labelGO.transform.Find("HerdOwner");
            if (ownerTf != null) ownerTextLabel = ownerTf.GetComponent<TextMeshProUGUI>();
            var iconTf = labelGO.transform.Find("HerdIcon");
            if (iconTf != null) ownerIconImage = iconTf.GetComponent<UnityEngine.UI.Image>();

            // Fallbacks: pick first/second TMP child if named ones not present
            var tmps = labelGO.GetComponentsInChildren<TextMeshProUGUI>(true);
            if (nameTextLabel == null && tmps.Length > 0) nameTextLabel = tmps[0];
            if (ownerTextLabel == null && tmps.Length > 1) ownerTextLabel = tmps[1];

            // Wire click if prefab contains a Button
            var btn = labelGO.GetComponentInChildren<UnityEngine.UI.Button>(true);
            if (btn != null) btn.onClick.AddListener(OnLabelClicked);
            else
            {
                // If no Button, add one to the root canvas for click handling
                var rootGO = labelCanvas != null ? labelCanvas.gameObject : labelGO;
                var added = rootGO.GetComponent<UnityEngine.UI.Button>();
                if (added == null) added = rootGO.AddComponent<UnityEngine.UI.Button>();
                added.onClick.AddListener(OnLabelClicked);
            }
        }
        else
        {
            GameObject canvasGO = new GameObject("HerdLabelCanvas");
            canvasGO.transform.SetParent(transform);
            labelCanvas = canvasGO.AddComponent<Canvas>();
            labelCanvas.renderMode = RenderMode.WorldSpace;
            labelCanvas.worldCamera = Camera.main;
            var scaler = canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10;
            canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            var rect = canvasGO.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(2.5f, 0.9f);
            rect.localScale = Vector3.one * 0.08f;

            var bgGO = new GameObject("BG"); bgGO.transform.SetParent(canvasGO.transform, false);
            var bg = bgGO.AddComponent<UnityEngine.UI.Image>(); bg.color = new Color(0,0,0,0.4f);
            var bgRect = bgGO.GetComponent<RectTransform>(); bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one; bgRect.offsetMin = Vector2.zero; bgRect.offsetMax = Vector2.zero;

            var nameGO = new GameObject("HerdName"); nameGO.transform.SetParent(canvasGO.transform, false);
            nameTextLabel = nameGO.AddComponent<TextMeshProUGUI>(); nameTextLabel.fontSize = 22; nameTextLabel.alignment = TMPro.TextAlignmentOptions.Center;
            var nameRect = nameGO.GetComponent<RectTransform>(); nameRect.anchoredPosition = new Vector2(0, 8); nameRect.sizeDelta = new Vector2(220, 28);

            var govGO = new GameObject("HerdGovernor"); govGO.transform.SetParent(canvasGO.transform, false);
            governorTextLabel = govGO.AddComponent<TextMeshProUGUI>(); governorTextLabel.fontSize = 16; governorTextLabel.alignment = TMPro.TextAlignmentOptions.Center;
            var govRect = govGO.GetComponent<RectTransform>(); govRect.anchoredPosition = new Vector2(0, -8); govRect.sizeDelta = new Vector2(220, 20);

            // clickable area
            var btn = canvasGO.AddComponent<UnityEngine.UI.Button>(); btn.onClick.AddListener(OnLabelClicked);
        }
    }

    private void UpdateLabelUI()
    {
        // Prefer HerdLabel component when present
        var displayName = string.IsNullOrEmpty(herdName) ? gameObject.name : herdName;
        if (herdLabelInstance != null)
        {
            var ownerName = owner != null && owner.civData != null ? owner.civData.civName : "(No Owner)";
            var ownerIcon = owner != null && owner.civData != null ? owner.civData.icon : null;
            herdLabelInstance.UpdateLabel(displayName, ownerName, ownerIcon, governor != null ? governor.Name : null);
            return;
        }

        if (nameTextLabel != null) nameTextLabel.text = displayName;
        // Owner: use owner.civData.civName if available
        if (ownerTextLabel != null)
            ownerTextLabel.text = owner != null && owner.civData != null ? owner.civData.civName : "(No Owner)";
        if (governorTextLabel != null) governorTextLabel.text = governor != null ? governor.Name : "(No Governor)";
        if (ownerIconImage != null && owner != null && owner.civData != null && owner.civData.icon != null)
            ownerIconImage.sprite = owner.civData.icon;
    }

    private void OnLabelClicked()
    {
        // Open HerdPanel if available
        UIManager.Instance?.ShowHerdPanelForHerd(this);
    }

    /// <summary>
    /// Move this herd to the specified tile index on the same planet if possible.
    /// Returns true if move succeeded.
    /// </summary>
    public bool MoveToTile(int tileIndex)
    {
        // Herd must be packed (mobile) to move
        if (!isPacked)
        {
            Debug.Log("Herd.MoveToTile: cannot move while settled (unpack first).");
            return false;
        }

        if (tileIndex < 0) return false;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null || !ts.IsReady()) return false;

        var td = ts.GetTileData(tileIndex);
        if (td == null || !td.isPassable || movementPoints <= 0) return false;
        if (currentTileIndex < 0 || ts.GetWrappedHexDistance(currentTileIndex, tileIndex) != 1) return false;

        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        if (occ != null)
        {
            var existing = occ.GetOccupantObject(tileIndex, TileLayer.Surface);
            if (existing != null && existing != this.gameObject) return false; // occupied
        }

        // Clear old occupancy
        try { (TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance)?.ClearOccupant(currentTileIndex, TileLayer.Surface); } catch { }

        // Move transform to tile center
        try { transform.position = ts.GetTileCenterFlat(tileIndex); } catch { }

        currentTileIndex = tileIndex;
        movementPoints--;

        // Notify spatial index that a herd moved
        if (HerdManager.Instance != null) HerdManager.Instance.MarkDirty();

        try { (TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance)?.SetOccupant(tileIndex, this.gameObject, TileLayer.Surface); } catch { }

        // Re-register with chunk manager for wrapping if present
        try
        {
            var planetGen = owner != null ? owner.GetPlanetGeneratorForIndex(planetIndex) ?? (GameManager.Instance != null ? GameManager.Instance.GetPlanetGenerator(planetIndex) : null)
                                       : (GameManager.Instance != null ? GameManager.Instance.GetPlanetGenerator(planetIndex) : null);
            var mgr = FindObjectsByType<HexMapChunkManager>().FirstOrDefault(m => m.PlanetGenerator == planetGen);
            if (mgr != null) mgr.RegisterObjectForWrapAtTile(tileIndex, gameObject);
        }
        catch { }

        UpdateLabelUI();
        if (worldUI != null) worldUI.MarkDirty();
        return true;
    }

    // ------------------------- Production (herd-local, city-like) -------------------------
    // Herd-local production queue entry
    public class ProdEntry {
        public ScriptableObject data;
        public int remainingPts;
        public int goldCost;
        public ResourceData[] requiredTileResourceDeposits;
        public Biome[] requiredTerrains;

        public ProdEntry(ScriptableObject d, int prodCost, int gCost, ResourceData[] reqTileResourceDeposits, Biome[] reqTerrains)
        {
            data = d;
            remainingPts = prodCost;
            goldCost = gCost;
            requiredTileResourceDeposits = reqTileResourceDeposits;
            requiredTerrains = reqTerrains;
        }

        public void Clamp() { if (remainingPts < 0) remainingPts = 0; }
    }

    // Herd-owned production queue (behaves like a city's production queue but is owned by the herd)
    public List<ProdEntry> productionQueue = new List<ProdEntry>();

    /// <summary>
    /// Compute this herd's production per turn. Sum of nearby tile production, attached building yields,
    /// and animal-contributed production (per-100 rules as configured by design).
    /// </summary>
    public int GetProductionPerTurn()
    {
        int total = 0;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts != null && currentTileIndex >= 0)
        {
            // current tile + neighbors
            var tiles = new List<int> { currentTileIndex };
            var neigh = ts.GetNeighbors(currentTileIndex);
            foreach (var n in neigh) tiles.Add(n);

            foreach (var t in tiles)
            {
                var td = ts.GetTileData(t);
                if (td == null) continue;
                var ty = td.GetTotalYield();
                total += ty.Production;
            }
        }

        // Add yields from attached herd buildings
        if (builtStructures != null)
        {
            foreach (var b in builtStructures) if (b != null) total += b.productionPerTurn;
        }

        // Animal contributions (per-100 rules); prefer explicit mapping from per-herd instance fields
        int chickenCount = 0, cowCount = 0, pigCount = 0, sheepCount = 0, goatCount = 0, elephantCount = 0;
        foreach (var e in animals)
        {
            if (e == null) continue;
            switch (e.species)
            {
                case HerdSpecies.Chicken: chickenCount += e.count; break;
                case HerdSpecies.Cow: cowCount += e.count; break;
                case HerdSpecies.Pig: pigCount += e.count; break;
                case HerdSpecies.Sheep: sheepCount += e.count; break;
                case HerdSpecies.Goat: goatCount += e.count; break;
                case HerdSpecies.Elephant: elephantCount += e.count; break;
                default: break;
            }
        }

        // Per-design contributions per 100 animals
        // Cows: +2 production per 100
        total += (cowCount / 100) * 2;
        // Goats: +1 production per 100
        total += (goatCount / 100) * 1;
        // Elephants: +4 production per 100
        total += (elephantCount / 100) * 4;

        // Governor production bonus (flat added production per-turn)
        try
        {
            if (governor != null)
            {
                var gb = governor.GetTotalBonuses();
                total += gb.production;
            }
        }
        catch { }

        return Mathf.Max(0, total);
    }

    [System.Serializable]
    public struct AnimalYields { public int Food; public int Gold; public int Production; public int Science; public int Culture; public int Faith; public int Policy; }

    /// <summary>
    /// Returns per-turn yields contributed by the herd's animals (per-100 rules).
    /// Chickens: every 100 -> +2 Food, +1 Gold
    /// Cows:     every 100 -> +2 Production, +1 Gold
    /// Pigs:     every 100 -> +3 Food
    /// Sheep:    every 100 -> +1 Food, +2 Gold
    /// Goats:    every 100 -> +1 Food, +1 Gold, +1 Production
    /// Elephants: every 100 -> +4 Production (higher food cost)
    /// Camels:   every 100 -> +2 Gold, +1 Production
    /// </summary>
    public AnimalYields GetAnimalYields()
    {
        int chickenCount = 0, cowCount = 0, pigCount = 0, sheepCount = 0, goatCount = 0, elephantCount = 0, camelCount = 0;
        foreach (var e in animals)
        {
            if (e == null) continue;
            switch (e.species)
            {
                case HerdSpecies.Chicken: chickenCount += e.count; break;
                case HerdSpecies.Cow: cowCount += e.count; break;
                case HerdSpecies.Pig: pigCount += e.count; break;
                case HerdSpecies.Sheep: sheepCount += e.count; break;
                case HerdSpecies.Goat: goatCount += e.count; break;
                case HerdSpecies.Elephant: elephantCount += e.count; break;
                case HerdSpecies.Camel: camelCount += e.count; break;
                default: break;
            }
        }
    
        AnimalYields y = new AnimalYields();
        y.Food = (chickenCount / 100) * 2 + (pigCount / 100) * 3 + (sheepCount / 100) * 1 + (goatCount / 100) * 1;
        y.Gold = (chickenCount / 100) * 1 + (cowCount / 100) * 1 + (sheepCount / 100) * 2 + (goatCount / 100) * 1 + (camelCount / 100) * 2;
        y.Production = (cowCount / 100) * 2 + (goatCount / 100) * 1 + (camelCount / 100) * 1 + (elephantCount / 100) * 4;
        y.Science = 0;
        y.Culture = 0;
        y.Faith = 0;
        y.Policy = 0;
        // Apply governor bonuses to herd yields (flat additions)
        try
        {
            if (governor != null)
            {
                var gb = governor.GetTotalBonuses();
                y.Food += gb.food;
                y.Gold += gb.gold;
                y.Production += gb.production; // stacked with per-100 cow production
                y.Science += gb.science;
                y.Culture += gb.culture;
                y.Faith += gb.faith;
                // Note: GovernorBonuses has no Policy field; if needed, add it to GovernorBonuses and persist traits
            }
        }
        catch { }
        // When packed (mobile) the herd only receives food from grazing/animals; other yields are suppressed
        if (isPacked)
        {
            y.Gold = 0;
            y.Production = 0;
            y.Science = 0;
            y.Culture = 0;
            y.Faith = 0;
            y.Policy = 0;
        }
        return y;
    }

    /// <summary>
    /// Returns total population represented by this herd: stored units + animal counts.
    /// </summary>
    public int GetPopulation()
    {
        int pop = 0;
        if (storedUnits != null) pop += storedUnits.Count;
        if (animals != null)
        {
            foreach (var e in animals) if (e != null) pop += e.count;
        }
        return pop;
    }

    /// <summary>
    /// Sum tile-based yields from the herd tile and its neighbors.
    /// Food uses HerdManager.ComputeHerdForageShare to estimate per-herd food from each tile.
    /// </summary>
    public AnimalYields GetNeighborhoodTileYields()
    {
        AnimalYields y = new AnimalYields();
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null || currentTileIndex < 0) return y;

        var tiles = new System.Collections.Generic.List<int>();
        tiles.Add(currentTileIndex);
        var neigh = ts.GetNeighbors(currentTileIndex);
        if (neigh != null) tiles.AddRange(neigh);

        foreach (var t in tiles)
        {
            if (t < 0) continue;
            var td = ts.GetTileData(t);
            if (td == null) continue;
            // Food: use shared forage share if HerdManager present
            int f = td.food;
            if (HerdManager.Instance != null) f = HerdManager.Instance.ComputeHerdForageShare(planetIndex, t, this);
            y.Food += Mathf.Max(0, f);
            y.Gold += Mathf.Max(0, td.gold);
            y.Production += Mathf.Max(0, td.production);
            y.Science += td.science;
            y.Culture += td.culture;
            y.Faith += td.faithYield;
            // Policy points not represented per-tile in current design
        }
        return y;
    }

    /// <summary>
    /// Returns gold per turn generated by this herd (animals + governor/building contributions).
    ///</summary>
    public int GetGoldPerTurn()
    {
        return GetAnimalYields().Gold;
    }

    /// <summary>
    /// Returns faith per turn generated by this herd.
    ///</summary>
    public int GetFaithPerTurn()
    {
        return GetAnimalYields().Faith;
    }

    /// <summary>
    /// Returns science per turn generated by this herd.
    ///</summary>
    public int GetSciencePerTurn()
    {
        return GetAnimalYields().Science;
    }

    /// <summary>
    /// Returns culture per turn generated by this herd.
    ///</summary>
    public int GetCulturePerTurn()
    {
        return GetAnimalYields().Culture;
    }

    /// <summary>
    /// Returns policy points per turn generated by this herd.
    ///</summary>
    public int GetPolicyPerTurn()
    {
        return GetAnimalYields().Policy;
    }

    /// <summary>
    /// Queue a production item (building, unit, etc.) into the herd's production queue.
    /// Validates requirements similar to `City.QueueProduction` but does not consume resources/gold immediately.
    /// </summary>
    public bool QueueProduction(ScriptableObject d)
    {
        if (d == null || owner == null) return false;

        if (d is BuildingData b)
        {
            if (!b.buildableByHerd) return false;
            if (!b.AreRequirementsMet(owner)) return false;
            // Tile resource deposit requirement: herd tile or a neighbor must have a matching resource deposit
            if (b.requiredTileResourceDeposits != null && b.requiredTileResourceDeposits.Length > 0)
            {
                var tsDeposit = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
                if (tsDeposit == null) return false;
                bool foundDeposit = false;
                var depositTiles = new List<int>();
                if (currentTileIndex >= 0) { depositTiles.Add(currentTileIndex); foreach (var n in tsDeposit.GetNeighbors(currentTileIndex)) depositTiles.Add(n); }
                foreach (var t in depositTiles)
                {
                    var td = tsDeposit.GetTileData(t);
                    if (td == null || td.resource == null) continue;
                    if (System.Array.IndexOf(b.requiredTileResourceDeposits, td.resource) >= 0) { foundDeposit = true; break; }
                }
                if (!foundDeposit) return false;
            }
            // Terrain requirements: ensure herd occupies or neighbors a tile matching required terrains
            if (b.requiredTerrains != null && b.requiredTerrains.Length > 0)
            {
                var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
                if (ts == null) return false;
                bool found = false;
                var tiles = new List<int>();
                if (currentTileIndex >= 0) { tiles.Add(currentTileIndex); foreach (var n in ts.GetNeighbors(currentTileIndex)) tiles.Add(n); }
                foreach (var t in tiles)
                {
                    var td = ts.GetTileData(t);
                    if (td == null) continue;
                    foreach (var req in b.requiredTerrains) { if (req == td.biome) { found = true; break; } }
                    if (found) break;
                }
                if (!found) return false;
            }

            var entry = new ProdEntry(b, b.productionCost, b.goldCost, b.requiredTileResourceDeposits, b.requiredTerrains);
            productionQueue.Add(entry);
            return true;
        }

        // Other types (units/equipment) not supported for herds right now
        return false;
    }

    /// <summary>
    /// Process production for this herd once per turn: apply productionPerTurn to the front queue entry.
    /// </summary>
    public void ProcessProduction()
    {
        if (isPacked) return;
        if (productionQueue == null || productionQueue.Count == 0) return;
        var entry = productionQueue[0];
        if (entry == null) return;

        int prod = GetProductionPerTurn();
        entry.remainingPts -= prod;
        entry.Clamp();
        if (entry.remainingPts <= 0)
        {
            // Complete the item
            if (entry.data is BuildingData bd)
            {
                // Instantiate prefab as child of herd for organization
                var buildingPrefab = bd.GetBuildingPrefab(owner);
                if (buildingPrefab != null)
                {
                    var inst = Instantiate(buildingPrefab, transform.position, Quaternion.identity);
                    inst.transform.SetParent(transform, true);
                }
                BuildStructure(bd);
            }
            productionQueue.RemoveAt(0);
        }
    }

    /// <summary>
    /// Cancel a queued production entry at the given index. Returns true if removed.
    /// Note: Queue entries do not consume resources at queue time in current design, so no refund is performed.
    /// </summary>
    public bool CancelProduction(int index)
    {
        if (productionQueue == null || index < 0 || index >= productionQueue.Count) return false;
        var entry = productionQueue[index];
        if (entry != null && owner != null)
        {
            // Refund gold if any
            try { if (entry.goldCost > 0) owner.AddGold(entry.goldCost); } catch { }
        }

        productionQueue.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// Reorder production entries within the queue. Moves item at `fromIndex` to `toIndex`.
    /// </summary>
    public bool ReorderProduction(int fromIndex, int toIndex)
    {
        if (productionQueue == null) return false;
        if (fromIndex < 0 || fromIndex >= productionQueue.Count) return false;
        if (toIndex < 0 || toIndex >= productionQueue.Count) return false;
        if (fromIndex == toIndex) return true;
        var item = productionQueue[fromIndex];
        productionQueue.RemoveAt(fromIndex);
        productionQueue.Insert(toIndex, item);
        return true;
    }

    // Pack the herd (make it mobile)
    public void Pack()
    {
        if (isPacked) return;
        isPacked = true;
        try { UpdateVisualRepresentation(); } catch { }
    }

    // Settle the herd (establish camp)
    public void Settle()
    {
        if (!isPacked) return;
        isPacked = false;
        try { UpdateVisualRepresentation(); } catch { }
    }

    // Instantiate or swap visual prefab according to isPacked and civ settings
    public void UpdateVisualRepresentation()
    {
        GameObject prefab = null;
        if (owner != null && owner.civData != null)
        {
            if (isPacked && owner.civData.herdPackedPrefab != null) prefab = owner.civData.herdPackedPrefab;
            else if (!isPacked && owner.civData.herdSettledPrefab != null) prefab = owner.civData.herdSettledPrefab;
            else prefab = owner.civData.herdPrefab;
        }
        if (prefab == null) return;

        if (visualInstance != null)
        {
            // If current visual is same prefab name, keep it
            if (visualInstance.name.StartsWith(prefab.name))
            {
                EnsureWorldUI();
                return;
            }
            Destroy(visualInstance);
            visualInstance = null;
            worldUI = null;
        }

        visualInstance = Instantiate(prefab, transform.position, Quaternion.identity, transform);
        visualInstance.name = prefab.name + "_inst";

        EnsureWorldUI();
    }

    private void EnsureWorldUI()
    {
        if (herdWorldUIPrefab != null)
        {
            if (worldUIInstance == null)
            {
                var parent = herdWorldUIAnchor != null ? herdWorldUIAnchor : transform;
                worldUIInstance = Instantiate(herdWorldUIPrefab, parent);
                worldUIInstance.name = herdWorldUIPrefab.name + "_inst";
            }

            worldUI = worldUIInstance.GetComponentInChildren<HerdWorldUI>(true);
            if (worldUI == null)
            {
                Debug.LogWarning($"[Herd] herdWorldUIPrefab '{herdWorldUIPrefab.name}' does not contain a HerdWorldUI component.");
                return;
            }

            worldUI.Initialize(this);
            return;
        }

        if (worldUIInstance != null)
        {
            Destroy(worldUIInstance);
            worldUIInstance = null;
        }

        // Try to find or create a world UI component
        worldUI = visualInstance.GetComponentInChildren<HerdWorldUI>(true);
        if (worldUI == null)
        {
            // create a small placeholder object for world UI (user should supply proper prefab)
            var go = new GameObject("HerdWorldUI", typeof(RectTransform));
            go.transform.SetParent(visualInstance.transform, false);
            go.transform.localPosition = Vector3.up * 1.2f;
            worldUI = go.AddComponent<HerdWorldUI>();
        }
        worldUI.Initialize(this);
    }
}
