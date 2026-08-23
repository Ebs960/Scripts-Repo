using UnityEngine;

[CreateAssetMenu(fileName = "CampaignMapModePresentation", menuName = "Campaign/Map Mode Presentation")]
public class CampaignMapModePresentationData : ScriptableObject
{
    [Header("Overlay Blend")]
    [Range(0, 1)] public float politicalBlend = .78f;
    [Range(0, 1)] public float governmentBlend = .76f;
    [Range(0, 1)] public float religionBlend = .72f;
    [Range(0, 1)] public float continentBlend = .58f;
    [Range(0, 1)] public float administrationBlend = .78f;
    [Range(0, 1)] public float diplomacyBlend = .82f;

    [Header("Categories")]
    public Color selfColor = new Color(.1f, .72f, .95f, 1);
    public Color friendlyColor = new Color(.18f, .75f, .32f, 1);
    public Color neutralColor = new Color(.48f, .5f, .52f, 1);
    public Color hostileColor = new Color(.88f, .18f, .16f, 1);
    public Color unknownColor = new Color(.18f, .2f, .22f, .45f);
    public Color noReligionColor = new Color(.4f, .4f, .4f, .25f);
    public Color noGovernmentColor = new Color(.55f, .42f, .25f, 1);
    public Color waterColor = new Color(.08f, .12f, .18f, .15f);

    [Header("Visibility")]
    [Range(0, 1)] public float staleDynamicStrength = .25f;
    [Range(0, 1)] public float exploredStaticStrength = .45f;
    [Range(.1f, 1f)] public float minimumReligionDominanceStrength = .4f;

    [Header("Borders")]
    public Color nationalBorderColor = new Color(.04f, .04f, .04f, .95f);
    public Color thematicBorderColor = new Color(.08f, .08f, .08f, .6f);
    [Min(.01f)] public float nationalBorderThickness = .12f;
    [Min(.01f)] public float thematicBorderThickness = .05f;

    public float BlendFor(CampaignMapMode mode)
    {
        switch (mode)
        {
            case CampaignMapMode.PoliticalOwnership: return politicalBlend;
            case CampaignMapMode.GovernmentType: return governmentBlend;
            case CampaignMapMode.Religion: return religionBlend;
            case CampaignMapMode.Continents: return continentBlend;
            case CampaignMapMode.Administration: return administrationBlend;
            case CampaignMapMode.Diplomacy: return diplomacyBlend;
            default: return 0f;
        }
    }

    private void OnValidate()
    {
        nationalBorderThickness=Mathf.Max(.01f,nationalBorderThickness);
        thematicBorderThickness=Mathf.Clamp(thematicBorderThickness,.01f,nationalBorderThickness);
        minimumReligionDominanceStrength=Mathf.Clamp01(minimumReligionDominanceStrength);
        selfColor.a=friendlyColor.a=neutralColor.a=hostileColor.a=1f;
    }
}
