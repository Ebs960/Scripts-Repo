using UnityEngine;

[CreateAssetMenu(menuName="Data/ReligionUnit")]
public class ReligionUnitData : CombatUnitData
{
    [Header("Religion Unit Properties")]
    [Tooltip("Is this a missionary unit (vs. apostle or other religious unit)")]
    public bool isMissionary = false;
    [Tooltip("Is this an apostle unit (more powerful than missionary)")]
    public bool isApostle = false;
    [Tooltip("How many tiles away can this unit spread religion")]
    public int spreadRange = 1;
    [Tooltip("How much religious pressure is added when using spread ability")]
    public float spreadPressureAmount = 100f;
    [Tooltip("How many spread charges this unit has")]
    public int spreadCharges = 3;
    [Tooltip("Faith cost to purchase this unit")]
    public int faithCost;
} 