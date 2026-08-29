using System;
using UnityEngine;

public enum CityVisualPeriod
{
    Prehistoric,
    Ancient,
    Classical,
    Medieval,
    Enlightenment,
    Modern,
    Information,
    Futuristic
}

public enum CityVisualSize
{
    Village,
    Town,
    City,
    Metropolis
}

[Serializable]
public class CityVisualSet
{
    public CityVisualPeriod period;
    public GameObject villagePrefab;
    public GameObject townPrefab;
    public GameObject cityPrefab;
    public GameObject metropolisPrefab;

    public GameObject GetPrefab(CityVisualSize size)
    {
        return size switch
        {
            CityVisualSize.Village => villagePrefab,
            CityVisualSize.Town => townPrefab,
            CityVisualSize.City => cityPrefab,
            CityVisualSize.Metropolis => metropolisPrefab,
            _ => null
        };
    }
}

/// <summary>Authoritative conversion from gameplay state to authored settlement artwork.</summary>
public static class CityVisualResolver
{
    public static CityVisualPeriod GetVisualPeriod(TechAge age)
    {
        return age switch
        {
            TechAge.PaleolithicAge or TechAge.NeolithicAge or TechAge.CopperAge => CityVisualPeriod.Prehistoric,
            TechAge.BronzeAge or TechAge.IronAge => CityVisualPeriod.Ancient,
            TechAge.ClassicalAge => CityVisualPeriod.Classical,
            TechAge.DarkAge or TechAge.FeudalAge or TechAge.CastleAge or TechAge.RenaissanceAge => CityVisualPeriod.Medieval,
            TechAge.ColonialAge or TechAge.EnlightenmentAge or TechAge.SteamAge => CityVisualPeriod.Enlightenment,
            TechAge.ImperialAge or TechAge.ModernAge => CityVisualPeriod.Modern,
            TechAge.InformationAge or TechAge.NanoAge => CityVisualPeriod.Information,
            TechAge.SolarAge or TechAge.InterstellarAge or TechAge.GalacticAge => CityVisualPeriod.Futuristic,
            _ => throw new ArgumentOutOfRangeException(nameof(age), age, "Unknown technology age")
        };
    }

    public static CityVisualSize GetVisualSize(int cityLevel)
    {
        if (cityLevel >= 20) return CityVisualSize.Metropolis;
        if (cityLevel >= 10) return CityVisualSize.City;
        if (cityLevel >= 5) return CityVisualSize.Town;
        return CityVisualSize.Village;
    }

    public static CityVisualSize GetMaxVisualSizeForAge(TechAge age)
    {
        return age switch
        {
            TechAge.PaleolithicAge => CityVisualSize.Village,
            TechAge.NeolithicAge or TechAge.CopperAge => CityVisualSize.Town,
            TechAge.BronzeAge or TechAge.IronAge => CityVisualSize.City,
            TechAge.ClassicalAge or
            TechAge.DarkAge or TechAge.FeudalAge or TechAge.CastleAge or TechAge.RenaissanceAge or
            TechAge.ColonialAge or TechAge.EnlightenmentAge or TechAge.SteamAge or
            TechAge.ImperialAge or TechAge.ModernAge or
            TechAge.InformationAge or TechAge.NanoAge or
            TechAge.SolarAge or TechAge.InterstellarAge or TechAge.GalacticAge => CityVisualSize.Metropolis,
            _ => throw new ArgumentOutOfRangeException(nameof(age), age, "Unknown technology age")
        };
    }

    public static CityVisualSize GetVisualSizeForAge(TechAge age, int cityLevel)
    {
        CityVisualSize rawSize = GetVisualSize(cityLevel);
        CityVisualSize maxSize = GetMaxVisualSizeForAge(age);
        return rawSize <= maxSize ? rawSize : maxSize;
    }

    public static GameObject ResolveCityVisual(CivData civData, TechAge age, int cityLevel)
    {
        if (civData == null || civData.cityVisuals == null) return null;
        CityVisualPeriod period = GetVisualPeriod(age);
        CityVisualSize size = GetVisualSizeForAge(age, cityLevel);
        foreach (var set in civData.cityVisuals)
            if (set != null && set.period == period)
                return set.GetPrefab(size);
        return null;
    }
}
