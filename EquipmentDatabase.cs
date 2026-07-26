using GameCombat;
using UnityEngine;

[CreateAssetMenu(fileName = "EquipmentDatabase", menuName = "Data/Equipment Database")]
public class EquipmentDatabase : ScriptableObject
{
    public EquipmentData[] equipment;
    public ProjectileData[] projectiles;
    public AbilityData[] equipmentAbilities;
    public StatusEffectData[] equipmentStatusEffects;
}
