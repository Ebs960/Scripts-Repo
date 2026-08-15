using System;
using System.Collections.Generic;
using UnityEngine;

public enum GovernorPortraitEra
{
    Prehistoric,
    Ancient,
    Medieval,
    Enlightenment,
    Modern,
    Future
}

public static class GovernorPortraitEraUtility
{
    public static GovernorPortraitEra GetPortraitEra(TechAge age)
    {
        switch (age)
        {
            case TechAge.PaleolithicAge:
            case TechAge.NeolithicAge:
            case TechAge.CopperAge: return GovernorPortraitEra.Prehistoric;
            case TechAge.BronzeAge:
            case TechAge.IronAge:
            case TechAge.ClassicalAge:
            case TechAge.DarkAge: return GovernorPortraitEra.Ancient;
            case TechAge.FeudalAge:
            case TechAge.CastleAge:
            case TechAge.RenaissanceAge: return GovernorPortraitEra.Medieval;
            case TechAge.ColonialAge:
            case TechAge.EnlightenmentAge:
            case TechAge.SteamAge: return GovernorPortraitEra.Enlightenment;
            case TechAge.ImperialAge:
            case TechAge.ModernAge:
            case TechAge.InformationAge:
            case TechAge.NanoAge: return GovernorPortraitEra.Modern;
            case TechAge.SolarAge:
            case TechAge.InterstellarAge:
            case TechAge.GalacticAge: return GovernorPortraitEra.Future;
            default: throw new ArgumentOutOfRangeException(nameof(age), age, "Unknown technology age");
        }
    }
}

[Serializable]
public class GovernorPortraitEntry
{
    public string portraitId;
    public Sprite sprite;
}

[Serializable]
public class GovernorPortraitPool
{
    public CultureGroup cultureGroup;
    public GovernorPortraitEra era;
    public List<GovernorPortraitEntry> portraits = new List<GovernorPortraitEntry>();
}

[CreateAssetMenu(fileName = "GovernorPortraitLibrary", menuName = "Data/Governor Portrait Library")]
public class GovernorPortraitLibrary : ScriptableObject
{
    public Sprite genericSilhouette;
    public List<GovernorPortraitPool> pools = new List<GovernorPortraitPool>();
}
