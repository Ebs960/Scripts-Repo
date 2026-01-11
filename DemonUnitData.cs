using UnityEngine;

[CreateAssetMenu(fileName = "NewDemonUnitData", menuName = "Data/Demon Unit Data")]
public class DemonUnitData : CombatUnitData
{
    [Header("Demon Properties")]
    [Tooltip("Additional damage when attacking in demonic biomes")]
    public float demonicBiomeDamageBonus = 0.5f;
    
    [Tooltip("Damage reduction when in demonic biomes")]
    public float demonicBiomeDefenseBonus = 0.3f;
    
    [Tooltip("Whether this demon can travel through lava")]
    public bool canCrossLava = true;
    
    [Tooltip("Whether this demon can cross water tiles")]
    public bool canCrossWater = false;
    
    [Header("Demon Army Settings")]
    [Tooltip("Movement points per turn when this demon type leads an army")]
    [Range(1, 5)]
    public int demonArmyMovePoints = 2;
    
    [Tooltip("Prefab for demon army visual on campaign map (optional - uses default if null)")]
    public GameObject demonArmyPrefab;
} 