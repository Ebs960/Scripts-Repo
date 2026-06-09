using UnityEngine;

[CreateAssetMenu(menuName="CivGame/Religion/Religion")]
public class ReligionData : ScriptableObject
{
    [Header("Identity")]
    public string religionName;
    [TextArea] public string description;
    public Sprite icon;

    [Header("Foundation")]
    [Tooltip("Which Pantheon you must have first")]
    public PantheonData requiredPantheon;
    [Tooltip("Faith cost to found this Religion (in a Holy Site)")]
    public int faithCost;

    [Header("Beliefs")]
    // Optional additional beliefs that can be added later
    [Tooltip("Additional beliefs that can be unlocked later in the game")]
    public BeliefData[] enhancerBeliefs;

    [Header("Targeted Yield Bonuses")]
    [Tooltip("Per-tile yield bonuses granted by civilizations that have founded this religion.")]
    public TileYieldBonus[] tileYieldBonuses;

    // REMOVED: Unlocked Content arrays
    // Availability is now controlled solely by requiredTechs/requiredCultures in the respective data classes
    // Religious units should have ReligionData in their requiredTechs or requiredCultures
} 