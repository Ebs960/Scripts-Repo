using UnityEngine;

[CreateAssetMenu(fileName = "ResearchDatabase", menuName = "Data/Research Database", order = 200)]
public class ResearchDatabase : ScriptableObject
{
    [Header("Core Collections")]
    public TechData[] techs;
    public CultureData[] cultures;

    [Header("UI Backgrounds (optional)")]
    public Sprite techTreeBackground;
    public Sprite cultureTreeBackground;

    [TextArea(3,6)]
    public string notes;
}
