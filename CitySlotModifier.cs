using UnityEngine;

/// <summary>
/// Adds or removes capacity for one city building slot type.
/// Used by technologies, cultures, governments, and settlement extensions.
/// </summary>
[System.Serializable]
public class CitySlotModifier
{
    public CitySlotType slotType = CitySlotType.Infrastructure;
    public int slotIncrease = 1;
    [Tooltip("Description for UI display")]
    public string description;
}
