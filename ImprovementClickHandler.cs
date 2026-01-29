// Assets/Scripts/Managers/ImprovementClickHandler.cs
using UnityEngine;

/// <summary>
/// MIGRATED: Now uses TileSystem.OnTileClicked event instead of OnMouseDown
/// This prevents conflicts with other input systems and ensures proper priority handling
/// </summary>
public class ImprovementClickHandler : MonoBehaviour
{
    private int tileIndex = -1;
    private int planetIndex = -1;
    private ImprovementData improvementData;
    private TileSystem eventTileSystem;

    public void Initialize(int tileIndex, ImprovementData data, int planetIndex = -1)
    {
        this.tileIndex = tileIndex;
        this.improvementData = data;
        this.planetIndex = planetIndex;
    }

    private void OnEnable()
    {
        // MIGRATED: Subscribe to TileSystem's click event instead of using OnMouseDown
        if (planetIndex < 0) planetIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        eventTileSystem = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (eventTileSystem != null)
        {
            eventTileSystem.OnTileClicked += HandleTileClicked;
        }
    }

    private void OnDisable()
    {
        // MIGRATED: Unsubscribe from event to prevent memory leaks
        if (eventTileSystem != null)
        {
            eventTileSystem.OnTileClicked -= HandleTileClicked;
        }
        eventTileSystem = null;
    }

    private void HandleTileClicked(int clickedTileIndex, Vector3 worldPos)
    {
        // Only handle clicks on our tile
        if (clickedTileIndex != tileIndex) return;
        
        // MIGRATED: Use InputManager for UI blocking check
        if (InputManager.Instance != null && InputManager.Instance.IsPointerOverUI())
            return;
            
        if (improvementData == null || tileIndex < 0) return;

        // Show upgrade panel for this improvement
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts != null && ts.isReady)
        {
            var data = ts.GetTileData(tileIndex);
            if (data?.owner == null || !data.owner.isPlayerControlled) return;
            var upgradeUI = FindFirstObjectByType<ImprovementUpgradeUI>();
            if (upgradeUI != null) upgradeUI.ShowUpgradePanel(improvementData, tileIndex, data.owner, planetIndex);
            else Debug.LogWarning("ImprovementUpgradeUI not found in scene!");
        }
        else
        {
            // Fallback to centralized TileSystem query even if not fully ready
            var tileData = ts != null ? ts.GetTileData(tileIndex) : null;
            if (tileData?.owner == null || !tileData.owner.isPlayerControlled) return;
            var upgradeUI = FindFirstObjectByType<ImprovementUpgradeUI>();
            if (upgradeUI != null) upgradeUI.ShowUpgradePanel(improvementData, tileIndex, tileData.owner, planetIndex);
        }
    }
}
