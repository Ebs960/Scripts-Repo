// Assets/Scripts/Data/LaborTypeData.cs
using UnityEngine;

[CreateAssetMenu(fileName = "NewLaborTypeData", menuName = "Data/Labor Type Data")]
public class LaborTypeData : ScriptableObject
{
    [Header("Identity")]
    public string laborName;
    [Tooltip("Unique identifier for this labor type. If empty, laborName will be used.")]
    public string laborId;
    public Sprite icon;
    [TextArea] public string description;

    [Header("Requirements")]
    [Tooltip("Marks this as the free, always-available labor type (Unskilled) that improvements start with. Only one labor type should have this set.")]
    public bool isDefaultLabor = false;
    [Tooltip("Policy that must be active on the civilization to assign this labor type. Leave empty if no policy is required.")]
    public PolicyData requiredPolicy;
    [Tooltip("Flat gold cost charged when switching an improvement to this labor type. Ignored (always free) when isDefaultLabor is true.")]
    public int goldCostToSwitchTo = 0;

    [Header("Effects")]
    [Tooltip("Multiplier applied to the improvement's base yields while this labor type is active (1.0 = no change).")]
    public float outputMultiplier = 1f;
    [Tooltip("Flat gold upkeep charged per turn per improvement using this labor type.")]
    public int goldUpkeepPerTurn = 0;
    [Tooltip("Unhappiness added to the improvement's administering city while this labor type is active.")]
    public int unhappinessPerTurn = 0;

    public string GetLaborKey() => !string.IsNullOrEmpty(laborId) ? laborId : laborName;
}
