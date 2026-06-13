using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Runtime helper attached to instantiated improvement GameObjects to track applied upgrades
/// and any attached child parts spawned by upgrades.
/// </summary>
public class ImprovementInstance : MonoBehaviour
{
    public int tileIndex = -1;
    public ImprovementData data;
    [Header("Placement")]
    [Tooltip("Optional child transform used as the placement root. Its world position will be snapped onto the tile surface.")]
    [SerializeField] private Transform placementRoot;
    [Tooltip("Fallback local-space placement point used when no placement root transform is assigned.")]
    [SerializeField] private Vector3 placementRootLocalPosition = Vector3.zero;
    // Civilization that built/owns this improvement instance (runtime only)
    public Civilization owner;
    // Track applied upgrades by id/name
    public HashSet<string> appliedUpgrades = new HashSet<string>();
    // Track instantiated child parts so we don't duplicate them
    public List<GameObject> attachedParts = new List<GameObject>();

    // Runtime list of units stored inside this improvement (shelter)
    public List<BaseUnit> storedUnits = new List<BaseUnit>();

    [Header("Fort Runtime")]
    [SerializeField] private int currentFortHitPoints = -1;
    [SerializeField] private bool fortNeutralized = false;
    [SerializeField] private int fortAttacksRemainingThisTurn = -1;

    /// <summary>
    /// Returns true if this improvement (or any applied upgrade) grants Zone of Control on adjacent tiles.
    /// </summary>
    public bool GrantsZoneOfControl()
    {
        if (data != null && data.grantsZoneOfControl) return true;
        if (appliedUpgrades != null && data?.availableUpgrades != null)
        {
            foreach (var up in data.availableUpgrades)
            {
                if (up == null) continue;
                string key = !string.IsNullOrEmpty(up.upgradeId) ? up.upgradeId : up.upgradeName;
                if (appliedUpgrades.Contains(key) && up.grantsZoneOfControl) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns true if this improvement (or any applied upgrade) blocks enemy Zone of Control on this tile.
    /// </summary>
    public bool BlocksZoneOfControl()
    {
        if (data != null && data.blocksZoneOfControl) return true;
        if (appliedUpgrades != null && data?.availableUpgrades != null)
        {
            foreach (var up in data.availableUpgrades)
            {
                if (up == null) continue;
                string key = !string.IsNullOrEmpty(up.upgradeId) ? up.upgradeId : up.upgradeName;
                if (appliedUpgrades.Contains(key) && up.blocksZoneOfControl) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Compute current shelter capacity: base data.shelterCapacity plus any applied upgrade bonuses.
    /// </summary>
    public int GetShelterCapacity()
    {
        int cap = 0;
        if (data != null) cap += data.shelterCapacity;
        if (appliedUpgrades != null && data != null && data.availableUpgrades != null)
        {
            foreach (var up in data.availableUpgrades)
            {
                if (up == null) continue;
                string key = !string.IsNullOrEmpty(up.upgradeId) ? up.upgradeId : up.upgradeName;
                if (appliedUpgrades.Contains(key))
                {
                    cap += up.additionalShelterCapacity;
                }
            }
        }
        return Mathf.Max(0, cap);
    }

    public bool IsFort => data != null && data.isFort;
    public bool IsFortNeutralized => IsFort && fortNeutralized;
    public int CurrentFortHitPoints => IsFort ? Mathf.Max(0, currentFortHitPoints) : 0;

    public int GetFortMaxHitPoints()
    {
        if (!IsFort) return 0;
        int hp = Mathf.Max(1, data.fortHitPoints);
        foreach (var upgrade in EnumerateAppliedUpgrades())
            hp += upgrade.additionalFortHitPoints;
        return Mathf.Max(1, hp);
    }

    public int GetFortAttack()
    {
        if (!IsFort || fortNeutralized) return 0;
        float attack = Mathf.Max(0, data.fortAttack);
        float pct = 0f;
        foreach (var upgrade in EnumerateAppliedUpgrades())
        {
            attack += upgrade.fortAttackAdd;
            pct += upgrade.fortAttackPct;
        }
        return Mathf.Max(0, Mathf.RoundToInt(attack * (1f + pct)));
    }

    public int GetFortDefense()
    {
        if (!IsFort) return 0;
        float defense = Mathf.Max(0, data.fortDefense);
        float pct = 0f;
        foreach (var upgrade in EnumerateAppliedUpgrades())
        {
            defense += upgrade.fortDefenseAdd;
            pct += upgrade.fortDefensePct;
        }
        return Mathf.Max(0, Mathf.RoundToInt(defense * (1f + pct)));
    }

    public bool CanFortFireAt(BaseUnit target)
    {
        if (!IsFort || fortNeutralized || target == null) return false;
        if (owner != null && target.owner == owner) return false;
        if (tileIndex < 0 || target.currentTileIndex < 0) return false;
        if (target.planetIndex != planetIndex) return false;
        if (GetFortAttack() <= 0) return false;

        int attacksRemaining = fortAttacksRemainingThisTurn < 0 ? Mathf.Max(1, data.fortAttacksPerTurn) : fortAttacksRemainingThisTurn;
        if (attacksRemaining <= 0) return false;

        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null) return false;
        int distance = ts.GetTileDistance(tileIndex, target.currentTileIndex);
        return distance >= 0 && distance <= Mathf.Max(1, data.fortAttackRange);
    }

    public int FireFortAt(BaseUnit target)
    {
        if (!CanFortFireAt(target)) return 0;

        int attack = GetFortAttack();
        float defense = target.CurrentDefense;
        defense = (defense + target.GetSituationalDefenseAddAgainst(null)) * (1f + target.GetSituationalDefensePctAgainst(null));
        int damage = Mathf.Max(1, Mathf.RoundToInt(attack - defense));
        target.ApplyDamage(damage);

        if (fortAttacksRemainingThisTurn < 0)
            fortAttacksRemainingThisTurn = Mathf.Max(1, data.fortAttacksPerTurn);
        fortAttacksRemainingThisTurn = Mathf.Max(0, fortAttacksRemainingThisTurn - 1);
        return damage;
    }

    public int ApplyFortDamage(int incomingDamage)
    {
        if (!IsFort || incomingDamage <= 0 || fortNeutralized) return 0;
        EnsureFortRuntimeInitialized();
        int mitigatedDamage = Mathf.Max(1, incomingDamage - GetFortDefense());
        currentFortHitPoints = Mathf.Max(0, currentFortHitPoints - mitigatedDamage);
        if (currentFortHitPoints <= 0)
            NeutralizeFort();
        return mitigatedDamage;
    }

    public void RepairFort(int amount)
    {
        if (!IsFort || amount <= 0) return;
        EnsureFortRuntimeInitialized();
        currentFortHitPoints = Mathf.Min(GetFortMaxHitPoints(), currentFortHitPoints + amount);
        if (currentFortHitPoints > 0)
            fortNeutralized = false;
    }

    public void ResetFortAttacksForTurn()
    {
        if (!IsFort) return;
        fortAttacksRemainingThisTurn = fortNeutralized ? 0 : Mathf.Max(1, data.fortAttacksPerTurn);
    }

    private void NeutralizeFort()
    {
        fortNeutralized = true;
        fortAttacksRemainingThisTurn = 0;
        if (storedUnits == null || storedUnits.Count == 0) return;

        var unitsToUnstore = storedUnits.ToArray();
        foreach (var unit in unitsToUnstore)
            TryUnstoreUnit(unit);
    }

    private void EnsureFortRuntimeInitialized()
    {
        if (!IsFort) return;
        int maxHp = GetFortMaxHitPoints();
        if (currentFortHitPoints < 0)
            currentFortHitPoints = maxHp;
        else if (currentFortHitPoints > maxHp)
            currentFortHitPoints = maxHp;

        if (fortAttacksRemainingThisTurn < 0)
            ResetFortAttacksForTurn();
    }

    private IEnumerable<ImprovementUpgradeData> EnumerateAppliedUpgrades()
    {
        if (appliedUpgrades == null || data?.availableUpgrades == null)
            yield break;

        foreach (var upgrade in data.availableUpgrades)
        {
            if (upgrade == null) continue;
            string key = !string.IsNullOrEmpty(upgrade.upgradeId) ? upgrade.upgradeId : upgrade.upgradeName;
            if (!string.IsNullOrEmpty(key) && appliedUpgrades.Contains(key))
                yield return upgrade;
        }
    }

    // Runtime click handling / tile awareness (consolidated from ImprovementClickHandler)
    private int planetIndex = -1;
    private TileSystem eventTileSystem;

    /// <summary>
    /// Initialize runtime state for this instantiated improvement.
    /// Also prepares the instance to receive tile-click events.
    /// </summary>
    public void Initialize(int tileIndex, ImprovementData data, int planetIndex = -1)
    {
        this.tileIndex = tileIndex;
        this.data = data;
        this.planetIndex = planetIndex;
        EnsureFortRuntimeInitialized();
        // Create a world-space label (icon) above the improvement if a prefab is configured
        try
        {
            var mgr = ImprovementManager.Instance;
            if (mgr.improvementLabelPrefab != null)
            {
                // Prefer a dedicated label anchor if the improvement prefab provides one
                Transform anchor = this.transform.Find("LabelAnchor") ?? this.transform.Find("labelAnchor") ?? this.transform;
                var labelGO = Instantiate(mgr.improvementLabelPrefab, anchor.position, anchor.rotation);
                labelGO.transform.SetParent(anchor, true);
                var ul = labelGO.GetComponent<UnitLabel>();
                if (ul != null)
                {
                    ul.Initialize(anchor, data.improvementName, owner != null && owner.civData != null ? owner.civData.civName : "", 0, 0);
                    ul.SetIcon(data != null ? data.icon : null);
                }
            }
        }
        catch { }
    }

    public Vector3 GetPlacementRootWorldPosition()
    {
        if (placementRoot != null)
            return placementRoot.position;

        return transform.TransformPoint(placementRootLocalPosition);
    }

    /// <summary>
    /// Store a unit inside this improvement (shelter). The unit will be removed from tile occupancy
    /// and deactivated until unstored. Returns true on success.
    /// </summary>
    public bool StoreUnit(BaseUnit unit)
    {
        if (unit == null || data == null || !data.isShelter) return false;
        if (IsFortNeutralized) return false;
        // Only owner units may be stored
        if (this.owner != null && unit.owner != this.owner) return false;
        if (storedUnits == null) storedUnits = new List<BaseUnit>();
        if (storedUnits.Contains(unit)) return false;

        // Only allow storing units that are currently on this tile
        if (unit.currentTileIndex != tileIndex) return false;

        // Enforce capacity
        int cap = GetShelterCapacity();
        if (cap <= 0) return false;
        if (storedUnits.Count >= cap) return false;

        var occ = TileOccupancyManager.GetForPlanet(unit.planetIndex) ?? TileOccupancyManager.Instance;
        if (occ != null)
        {
            occ.ClearOccupant(tileIndex, TileLayer.Surface);
        }

        unit.isStored = true;
        unit.storedInImprovement = this;
        unit.currentTileIndex = -1;
        // Hide the unit visually while stored
        unit.gameObject.SetActive(false);

        storedUnits.Add(unit);
        return true;
    }

    /// <summary>
    /// Try to unstore the given unit back to an adjacent free tile (or the improvement tile if free).
    /// Returns true if successfully unstored.
    /// </summary>
    public bool TryUnstoreUnit(BaseUnit unit)
    {
        if (unit == null || storedUnits == null || !storedUnits.Contains(unit)) return false;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;

        // First try the improvement tile itself
        if (occ != null && occ.GetOccupantObject(tileIndex, TileLayer.Surface) == null)
        {
            return UnstoreToTile(unit, tileIndex);
        }

        // Try neighbors
        if (ts != null)
        {
            var neighbors = ts.GetNeighbors(tileIndex);
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

        return false; // no free tile found
    }

    private bool UnstoreToTile(BaseUnit unit, int tile)
    {
        if (unit == null) return false;
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;

        // Place unit at tile center
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
        unit.storedInImprovement = null;

        storedUnits.Remove(unit);
        return true;
    }

    private void OnEnable()
    {
        if (planetIndex < 0)
        {
            if (owner != null && owner.ownedTilesByPlanet != null && owner.ownedTilesByPlanet.Count > 0)
            {
                planetIndex = owner.ownedTilesByPlanet.Keys.First();
            }
            else
            {
                planetIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
            }
        }
        eventTileSystem = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (eventTileSystem != null)
        {
            eventTileSystem.OnTileClicked += HandleTileClicked;
        }
    }

    private void OnDisable()
    {
        if (eventTileSystem != null)
        {
            eventTileSystem.OnTileClicked -= HandleTileClicked;
        }
        eventTileSystem = null;
    }

    private bool HandleTileClicked(int clickedTileIndex, Vector3 worldPos)
    {
        if (clickedTileIndex != tileIndex) return false;
        if (InputManager.Instance != null && InputManager.Instance.IsPointerOverUI()) return false;
        if (data == null || tileIndex < 0) return false;

        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts != null && ts.isReady)
        {
            var tileData = ts.GetTileData(tileIndex);
            var civOwner = this.owner ?? tileData?.improvementOwner;
            if (civOwner == null || !civOwner.isPlayerControlled) return false;
            // Use the ImprovementManager-provided reference only (no fallbacks)
            var upgradeUI = ImprovementManager.Instance != null ? ImprovementManager.Instance.improvementUpgradeUI : null;
            if (upgradeUI == null)
            {
                Debug.LogWarning("ImprovementUpgradeUI reference not assigned on ImprovementManager. Cannot open upgrade panel.");
                return false;
            }
            // Hide the unit info panel (if visible) so the upgrade panel is not obscured.
            if (UIManager.Instance != null)
                UIManager.Instance.HideUnitInfoPanel();
            upgradeUI.ShowUpgradePanel(data, tileIndex, civOwner, planetIndex);
            return true;
        }
        else
        {
            var tileData = ts != null ? ts.GetTileData(tileIndex) : null;
            var civOwner = this.owner ?? tileData?.improvementOwner;
            if (civOwner == null || !civOwner.isPlayerControlled) return false;
            var upgradeUI = ImprovementManager.Instance != null ? ImprovementManager.Instance.improvementUpgradeUI : null;
            if (upgradeUI == null)
            {
                Debug.LogWarning("ImprovementUpgradeUI reference not assigned on ImprovementManager. Cannot open upgrade panel.");
                return false;
            }
            if (UIManager.Instance != null)
                UIManager.Instance.HideUnitInfoPanel();
            upgradeUI.ShowUpgradePanel(data, tileIndex, civOwner, planetIndex);
            return true;
        }
    }

    public bool HasApplied(string idOrName)
    {
        if (string.IsNullOrEmpty(idOrName)) return false;
        return appliedUpgrades.Contains(idOrName);
    }

    public void MarkApplied(string idOrName)
    {
        if (string.IsNullOrEmpty(idOrName)) return;
        appliedUpgrades.Add(idOrName);
    }
}
