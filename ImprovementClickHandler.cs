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
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
        {
            // Click was on UI, ignore
            return;
        }

        if (improvementData == null || tileIndex < 0) return;

        // Get the tile owner to check if player controls it
        var (tileData, _) = TileDataHelper.Instance.GetTileData(tileIndex);
        if (tileData?.owner == null) return;

        // Only show upgrade UI for player-controlled improvements
        if (!tileData.owner.isPlayerControlled) return;

        // Show the improvement upgrade UI
        var upgradeUI = FindFirstObjectByType<ImprovementUpgradeUI>();
        if (upgradeUI != null)
        {
            upgradeUI.ShowUpgradePanel(improvementData, tileIndex, tileData.owner);
        }
        else
        {
            Debug.LogWarning("ImprovementUpgradeUI not found in scene!");
        }
    }
}
