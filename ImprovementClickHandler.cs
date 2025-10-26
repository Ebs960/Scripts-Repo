// Assets/Scripts/Managers/ImprovementClickHandler.cs
using UnityEngine;

/// <summary>
/// MIGRATED: Now uses TileSystem.OnTileClicked event instead of OnMouseDown
/// This prevents conflicts with other input systems and ensures proper priority handling
/// </summary>
public class ImprovementClickHandler : MonoBehaviour
{
    private int tileIndex = -1;
    private ImprovementData improvementData;

    public void Initialize(int tileIndex, ImprovementData data)
    {
        this.tileIndex = tileIndex;
        this.improvementData = data;
    }

    private void OnEnable()
    {
        // MIGRATED: Subscribe to TileSystem's click event instead of using OnMouseDown
        if (TileSystem.Instance != null)
        {
            TileSystem.Instance.OnTileClicked += HandleTileClicked;
        }
    }

    private void OnDisable()
    {
        // MIGRATED: Unsubscribe from event to prevent memory leaks
        if (TileSystem.Instance != null)
        {
            TileSystem.Instance.OnTileClicked -= HandleTileClicked;
        }
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
        if (TileSystem.Instance != null && TileSystem.Instance.isReady)
        {
            var data = TileSystem.Instance.GetTileData(tileIndex);
            if (data?.owner == null || !data.owner.isPlayerControlled) return;
            var upgradeUI = FindFirstObjectByType<ImprovementUpgradeUI>();
            if (upgradeUI != null) upgradeUI.ShowUpgradePanel(improvementData, tileIndex, data.owner);
            else Debug.LogWarning("ImprovementUpgradeUI not found in scene!");
        }
        else
        {
            // Fallback to centralized TileSystem query even if not fully ready
            var tileData = TileSystem.Instance != null ? TileSystem.Instance.GetTileData(tileIndex) : null;
            if (tileData?.owner == null || !tileData.owner.isPlayerControlled) return;
            var upgradeUI = FindFirstObjectByType<ImprovementUpgradeUI>();
            if (upgradeUI != null) upgradeUI.ShowUpgradePanel(improvementData, tileIndex, tileData.owner);
        }
    }
}
