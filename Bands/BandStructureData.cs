using UnityEngine;

/// <summary>Internal Band development; it never creates an ImprovementInstance.</summary>
[CreateAssetMenu(fileName = "NewBandStructure", menuName = "Data/Band Structure Data")]
public sealed class BandStructureData : ScriptableObject
{
    public string structureName;
    [TextArea] public string description;
    public Sprite icon;
    [Min(0)] public int productionCost = 10;
    [Min(0)] public int goldCost;
    public ResourceCost[] resourceCosts;
    public TechData requiredTech;
    public CultureData requiredCulture;
    public BandYieldSet yields;
    public int foodStorageBonus;
    public int garrisonCapacityBonus;
    public int populationGrowthBonus;
    public int movementBonus;
    public int forageBonus;
    public bool activeWhilePacked;
    [Range(0f, 1f)] public float packedEffectMultiplier;

    [Header("Presentation")]
    [Tooltip("Semantic socket used by this structure on every culture's encamped visual prefab.")]
    public BandStructureVisualSlot visualSlot = BandStructureVisualSlot.Generic;
    public GameObject visualAttachmentPrefab;
}
