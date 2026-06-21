using UnityEngine;

public enum AttachedSettlementType
{
    Hamlet,
    Village,
    Town,
    LargeTown,
    Suburb,
    Sector
}

/// <summary>
/// A smaller attached settlement that expands a city's territory and building slots.
/// </summary>
[System.Serializable]
public class CitySettlementExtension
{
    public AttachedSettlementType settlementType = AttachedSettlementType.Hamlet;
    [Tooltip("Optional display name for this attached settlement.")]
    public string settlementName;
    [Tooltip("Center tile for territory expansion. -1 means use the city center.")]
    public int centerTileIndex = -1;
    [Tooltip("Additional radius claimed around the extension center.")]
    public int territoryRadiusBonus = 0;
    [Tooltip("Additional building slots granted while attached to the city.")]
    public CitySlotModifier[] slotModifiers;
}
