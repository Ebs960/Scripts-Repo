// Assets/Scripts/Managers/ImprovementClickHandler.cs
using UnityEngine;

public class ImprovementClickHandler : MonoBehaviour
{
    private int tileIndex = -1;
    private ImprovementData improvementData;

    public void Initialize(int tileIndex, ImprovementData data)
    {
        this.tileIndex = tileIndex;
        this.improvementData = data;
    }

    private void OnMouseDown()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;
        if (improvementData == null || tileIndex < 0) return;

        // Delegate to TileSystem central handler if present
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
