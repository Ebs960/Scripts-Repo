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

    private void OnEnable() { GovernorPortraitService.Configure(this); }
}

/// <summary>Resolves portrait pools and sprites and assigns permanent portrait identities.</summary>
public static class GovernorPortraitService
{
    private static GovernorPortraitLibrary library;

    public static void Configure(GovernorPortraitLibrary value) { library = value; }

    public static GovernorPortraitPool GetPool(CultureGroup culture, GovernorPortraitEra era)
    {
        if (library?.pools == null) return null;
        return library.pools.Find(p => p != null && p.cultureGroup == culture && p.era == era);
    }

    public static Sprite GetSprite(string portraitId)
    {
        if (library?.pools != null && !string.IsNullOrWhiteSpace(portraitId))
            foreach (var pool in library.pools)
                if (pool?.portraits != null)
                    foreach (var entry in pool.portraits)
                        if (entry != null && entry.portraitId == portraitId)
                            return entry.sprite != null ? entry.sprite : library.genericSilhouette;
        return library != null ? library.genericSilhouette : null;
    }

    public static bool AssignPortrait(Civilization civ, Governor governor)
    {
        if (civ?.civData == null || governor == null || !string.IsNullOrEmpty(governor.PortraitId)) return false;
        var pool = GetPool(civ.civData.cultureGroup, GovernorPortraitEraUtility.GetPortraitEra(civ.GetCurrentAge()));
        if (pool?.portraits == null) return false;

        var used = new HashSet<string>();
        if (civ.governors != null)
            foreach (var existing in civ.governors)
                if (existing != null && !string.IsNullOrEmpty(existing.PortraitId)) used.Add(existing.PortraitId);

        var valid = pool.portraits.FindAll(p => p != null && !string.IsNullOrWhiteSpace(p.portraitId));
        if (valid.Count == 0) return false;
        var unused = valid.FindAll(p => !used.Contains(p.portraitId));
        var choices = unused.Count > 0 ? unused : valid;
        return governor.AssignPortrait(choices[Random.Range(0, choices.Count)].portraitId);
    }
}
