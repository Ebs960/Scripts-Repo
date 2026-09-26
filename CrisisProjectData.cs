using UnityEngine;

[CreateAssetMenu(fileName="New Crisis Project", menuName="Data/Crisis Project")]
public class CrisisProjectData : ScriptableObject
{
    public enum ProjectEffect { SharedCrisisProject, EmergencyShelters, InfrastructureHardening }
    public string projectName;
    public Sprite icon;
    [TextArea] public string description;
    [Min(1)] public int productionCost = 1;
    [Min(0)] public int goldCost;
    public CrisisData validCrisis;
    public ProjectEffect projectEffect;
    public bool useDynamicProductionCost;
    [Min(1)] public int equivalentTopCityCount = 3;
    [Min(1)] public int targetProductionTurns = 5;
    [Min(1)] public int minimumResolvedProductionCost = 100;
    public TechData[] requiredTechs;
    public CultureData[] requiredCultures;
}
