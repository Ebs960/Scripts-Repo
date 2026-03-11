using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime helper attached to instantiated improvement GameObjects to track applied upgrades
/// and any attached child parts spawned by upgrades.
/// </summary>
public class ImprovementInstance : MonoBehaviour
{
    public int tileIndex = -1;
    public ImprovementData data;
    // Civilization that built/owns this improvement instance (runtime only)
    public Civilization owner;
    // Track applied upgrades by id/name
    public HashSet<string> appliedUpgrades = new HashSet<string>();
    // Track instantiated child parts so we don't duplicate them
    public List<GameObject> attachedParts = new List<GameObject>();

    // Runtime list of units stored inside this improvement (shelter)
    public List<BaseUnit> storedUnits = new List<BaseUnit>();

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
    }

    /// <summary>
    /// Store a unit inside this improvement (shelter). The unit will be removed from tile occupancy
    /// and deactivated until unstored. Returns true on success.
    /// </summary>
    public bool StoreUnit(BaseUnit unit)
    {
        if (unit == null || data == null || !data.isShelter) return false;
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
        if (planetIndex < 0) planetIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
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
