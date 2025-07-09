// Assets/Scripts/Data/ImprovementData.cs
using UnityEngine;

[CreateAssetMenu(fileName = "NewImprovementData", menuName = "CivGame/Improvement Data")]
public class ImprovementData : ScriptableObject
{
    [Header("Identity")]
    public string improvementName;
    public Sprite icon;
    [TextArea] public string description;

    [Header("Construction")]
    [Tooltip("How many work points required to finish")]
    public int workCost;
    [Tooltip("Prefab to show while building")]
    public GameObject constructionPrefab;
    [Tooltip("Prefab to spawn when complete")]
    public GameObject completePrefab;
    [Tooltip("Prefab to spawn if destroyed")]
    public GameObject destroyedPrefab;
    
    [Header("Territory Requirements")]
    [Tooltip("Must be built within a city's direct influence")]
    public bool needsCity;
    [Tooltip("Can only be built on tiles controlled by the builder's civilization")]
    public bool requiresControlledTerritory;
    [Tooltip("Can be built in neutral/unclaimed territory")]
    public bool canBuildInNeutralTerritory;
    [Tooltip("Can be built in enemy territory")]
    public bool canBuildInEnemyTerritory;

    [Header("Location Requirements")]
    public Biome[] allowedBiomes;
    public ResourceData[] requiredResources;

    [Header("Yield Bonus (per turn)")]
    public int foodPerTurn;
    public int productionPerTurn;
    public int goldPerTurn;
    public int sciencePerTurn;
    public int culturePerTurn;
    public int policyPointsPerTurn;
    public int faithPerTurn;
}