using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class BandStructureVisualOverride
{
    public CivData civilization;
    public GameObject visualAttachmentPrefab;
}

[Serializable]
public sealed class BandStructureCultureGroupVisualOverride
{
    public CultureGroup cultureGroup;
    public GameObject visualAttachmentPrefab;
}

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
    [Tooltip("Optional per-civilization replacements. These take precedence over culture-group replacements.")]
    public List<BandStructureVisualOverride> civilizationVisualOverrides = new List<BandStructureVisualOverride>();
    [Tooltip("Optional replacements shared by civilizations of the selected culture group. Falls back to visualAttachmentPrefab.")]
    public List<BandStructureCultureGroupVisualOverride> cultureGroupVisualOverrides = new List<BandStructureCultureGroupVisualOverride>();

    public GameObject GetVisualAttachmentPrefab(CivData civilization)
    {
        var visualOverride = civilizationVisualOverrides?.Find(x =>
            x != null && x.civilization == civilization && x.visualAttachmentPrefab != null);
        if (visualOverride != null) return visualOverride.visualAttachmentPrefab;

        var cultureGroupOverride = cultureGroupVisualOverrides?.Find(x =>
            x != null && civilization != null && x.cultureGroup == civilization.cultureGroup && x.visualAttachmentPrefab != null);
        return cultureGroupOverride != null ? cultureGroupOverride.visualAttachmentPrefab : visualAttachmentPrefab;
    }
}
