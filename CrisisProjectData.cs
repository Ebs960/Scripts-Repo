using UnityEngine;

[CreateAssetMenu(fileName="New Crisis Project", menuName="Data/Crisis Project")]
public class CrisisProjectData : ScriptableObject
{
    public string projectName;
    public Sprite icon;
    [TextArea] public string description;
    [Min(1)] public int productionCost = 1;
    [Min(0)] public int goldCost;
    public CrisisData validCrisis;
    public TechData[] requiredTechs;
    public CultureData[] requiredCultures;
}
