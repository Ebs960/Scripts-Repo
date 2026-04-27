// Assets/Scripts/UI/ResourceCategoryDefinitionSO.cs
using UnityEngine;

/// <summary>
/// Defines a resource category (Metals, Livestock, Fuel, Materials, Luxuries, Equipment).
/// 
/// Each category has:
/// - Display name and icon
/// - Display order (for consistent UI ordering)
/// - Provider types that supply inventory data (OwnedNodes, Stockpile, Equipment)
/// </summary>
[CreateAssetMenu(fileName = "ResourceCategory_", menuName = "Game/UI/Resource Category")]
public class ResourceCategoryDefinitionSO : ScriptableObject
{
    [SerializeField] private string categoryName;
    [SerializeField] private Sprite categoryIcon;
    [SerializeField] private int displayOrder;

    [Header("Provider Configuration")]
    [SerializeField] private bool useOwnedNodeProvider = true;
    [SerializeField] private bool useStockpileProvider = false;
    [SerializeField] private bool useEquipmentProvider = false;

    public string CategoryName => categoryName;
    public Sprite CategoryIcon => categoryIcon;
    public int DisplayOrder => displayOrder;

    public bool UseOwnedNodeProvider => useOwnedNodeProvider;
    public bool UseStockpileProvider => useStockpileProvider;
    public bool UseEquipmentProvider => useEquipmentProvider;
}
