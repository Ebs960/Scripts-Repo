using UnityEngine;

/// <summary>
/// Ensures all space UI starts hidden on game launch.
/// Add this to any GameObject in your main scene as a backup safety measure.
/// </summary>
public class HideSpaceUIOnStart : MonoBehaviour
{
    void Start()
    {
        HideAllSpaceUI();
        
        // Destroy this script after hiding everything (one-time use)
        Destroy(this);
    }
    
    /// <summary>
    /// Hide all space UI elements
    /// </summary>
    private void HideAllSpaceUI()
    {
        // Hide space map UI
        var spaceMapUI = FindFirstObjectByType<SpaceMapUI>();
        if (spaceMapUI != null)
        {
            spaceMapUI.Hide();
            Debug.Log("[HideSpaceUI] Space map UI hidden");
        }
        
        // Hide space loading panels
        var spaceLoadingPanels = FindObjectsByType<SpaceLoadingPanelController>(FindObjectsSortMode.None);
        foreach (var panel in spaceLoadingPanels)
        {
            panel.HideSpaceLoading();
            Debug.Log("[HideSpaceUI] Space loading panel hidden");
        }
        
        // Hide singleton instance
        if (SpaceLoadingPanelController.Instance != null)
        {
            SpaceLoadingPanelController.Instance.HideSpaceLoading();
            Debug.Log("[HideSpaceUI] Singleton space loading panel hidden");
        }
        
        // Hide any GameObjects with space-related names
        HideGameObjectsByName("SpaceMap");
        HideGameObjectsByName("SpaceLoading");
        HideGameObjectsByName("Galaxy");
        
        Debug.Log("[HideSpaceUI] All space UI hidden on game start");
    }
    
    /// <summary>
    /// Hide GameObjects that contain specific name patterns
    /// </summary>
    private void HideGameObjectsByName(string namePattern)
    {
        GameObject[] allObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        foreach (var obj in allObjects)
        {
            if (obj.name.Contains(namePattern))
            {
                obj.SetActive(false);
                Debug.Log($"[HideSpaceUI] Hidden GameObject: {obj.name}");
            }
        }
    }
}
