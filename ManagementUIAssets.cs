using UnityEngine;

public sealed class ManagementUIAssets : ScriptableObject
{
    public Sprite legacyTrackerShell;
    public Sprite legacyEntryRow;
    public Sprite legacyDetailShell;
    public Sprite armyTransferShell;
    public ArmyRosterCardView armyRosterCard;

    public static ManagementUIAssets Load()
    {
        var assets = Resources.Load<ManagementUIAssets>("MenuUI/Management UI Assets");
        if (assets == null)
            Debug.LogError("[ManagementUIAssets] Management UI Assets resource is missing.");
        return assets;
    }
}
