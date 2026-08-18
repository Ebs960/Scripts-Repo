using UnityEngine;

[CreateAssetMenu(menuName="Battle/Biome Visual Profile",fileName="Battle Biome Visual Profile")]
public sealed class BattleBiomeVisualProfile : ScriptableObject
{
    public Biome biome;
    [Header("Ground (optional override)")] public BiomeVisualData biomeVisualOverride;
    [Header("Vegetation and Props")]
    public GameObject[] treePrefabs;
    public GameObject[] grassPrefabs;
    public GameObject[] bushPrefabs;
    public GameObject[] rockPrefabs;
    public GameObject[] environmentalPropPrefabs;
    [Header("Feature Props")]
    public GameObject[] softCoverPrefabs;
    public GameObject[] hardCoverPrefabs;
    public GameObject[] portPrefabs;
    [Header("Base Density")]
    [Range(0f,1f)] public float treeDensity=.08f;
    [Range(0f,1f)] public float grassDensity=.5f;
    [Range(0f,1f)] public float bushDensity=.12f;
    [Range(0f,1f)] public float rockDensity=.08f;
    [Range(0f,1f)] public float propDensity=.04f;
    [Header("Maximum Instances Per Cell")]
    [Min(0)] public int maximumTrees=5;
    [Min(0)] public int maximumGrassClumps=28;
    [Min(0)] public int maximumBushes=6;
    [Min(0)] public int maximumRocks=6;
    [Min(0)] public int maximumProps=3;
    [Header("Feature Multipliers")]
    [Min(0f)] public float forestTreeMultiplier=4f;
    [Min(0f)] public float forestBushMultiplier=2f;
    [Min(0f)] public float mountainRockMultiplier=2f;
    [Min(0f)] public float mountainVegetationMultiplier=.5f;
    [Header("Scale Variation")]
    public Vector2 treeScaleRange=new(.85f,1.15f);
    public Vector2 grassScaleRange=new(.8f,1.2f);
    public Vector2 bushScaleRange=new(.8f,1.2f);
    public Vector2 rockScaleRange=new(.75f,1.25f);
    public Vector2 propScaleRange=new(.9f,1.1f);
    [Header("Placement")]
    [Min(0f)] public float unitClearRadius=.55f;
    [Range(0f,.9f)] public float edgePadding=.12f;
    [Range(0f,1f)] public float riverClearHalfWidth=.2f;
    [Header("Elevation (x = elevation 0..3)")]
    public AnimationCurve vegetationByElevation=AnimationCurve.Linear(0f,1f,3f,.25f);
    public AnimationCurve rockByElevation=AnimationCurve.Linear(0f,.5f,3f,2f);

    private void OnValidate()
    {
        treeScaleRange=ValidateRange(treeScaleRange);grassScaleRange=ValidateRange(grassScaleRange);
        bushScaleRange=ValidateRange(bushScaleRange);rockScaleRange=ValidateRange(rockScaleRange);propScaleRange=ValidateRange(propScaleRange);
        if(biomeVisualOverride!=null&&biomeVisualOverride.surfaceFamily==null)Debug.LogWarning($"[{name}] Ground override has no SurfaceFamilyData; campaign biome ground will be used as a fallback.",this);
        if(biome==Biome.Any)Debug.LogWarning($"[{name}] Select a concrete biome; Biome.Any is not rendered by generated battle cells.",this);
    }
    private static Vector2 ValidateRange(Vector2 value){value.x=Mathf.Max(.01f,value.x);value.y=Mathf.Max(value.x,value.y);return value;}
}
