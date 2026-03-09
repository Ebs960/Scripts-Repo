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

    private void HandleTileClicked(int clickedTileIndex, Vector3 worldPos)
    {
        if (clickedTileIndex != tileIndex) return;
        if (InputManager.Instance != null && InputManager.Instance.IsPointerOverUI()) return;
        if (data == null || tileIndex < 0) return;

        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts != null && ts.isReady)
        {
            var tileData = ts.GetTileData(tileIndex);
            if (tileData?.owner == null || !tileData.owner.isPlayerControlled) return;
            var upgradeUI = FindFirstObjectByType<ImprovementUpgradeUI>();
            if (upgradeUI != null) upgradeUI.ShowUpgradePanel(data, tileIndex, tileData.owner, planetIndex);
            else Debug.LogWarning("ImprovementUpgradeUI not found in scene!");
        }
        else
        {
            var tileData = ts != null ? ts.GetTileData(tileIndex) : null;
            if (tileData?.owner == null || !tileData.owner.isPlayerControlled) return;
            var upgradeUI = FindFirstObjectByType<ImprovementUpgradeUI>();
            if (upgradeUI != null) upgradeUI.ShowUpgradePanel(data, tileIndex, tileData.owner, planetIndex);
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
