using UnityEngine;

/// <summary>
/// Player-facing political vocabulary. Titles come from serialized GovernmentData fields only; they are
/// presentation and are never written into Governor.Name or saved.
/// </summary>
public static class GovernmentPresentation
{
    public static string PolicyAreaDisplayName(PolicyArea area)
    {
        switch (area)
        {
            case PolicyArea.Law: return "Law & Justice";
            case PolicyArea.CivilRights: return "Civil Rights";
            case PolicyArea.Digital: return "Digital Policy";
            case PolicyArea.Synthetic: return "Synthetic Life";
            case PolicyArea.Space: return "Space & Planetary";
            case PolicyArea.Unassigned: return "Unassigned";
            default: return area.ToString();
        }
    }
    public const string DefaultGovernorSingular = "Governor";
    public const string DefaultGovernorPlural = "Governors";
    public const string DefaultCouncilMember = "Councillor";
    public const string DefaultInstitution = "Council";

    public static string GetGovernorTitleSingular(Civilization civ)
        => Pick(civ?.currentGovernment?.governorTitleSingular, DefaultGovernorSingular);

    public static string GetGovernorTitlePlural(Civilization civ)
        => Pick(civ?.currentGovernment?.governorTitlePlural, DefaultGovernorPlural);

    public static string GetCouncilMemberTitle(Civilization civ)
        => Pick(civ?.currentGovernment?.councilMemberTitle, DefaultCouncilMember);

    /// <summary>Name of the council/legislature (never the hardcoded "Royal Council" unless authored that way).</summary>
    public static string GetInstitutionName(Civilization civ)
        => Pick(civ?.currentGovernment?.institutionDisplayName, DefaultInstitution);

    public static string GetLeaderTitle(Civilization civ)
        => civ?.currentGovernment != null ? civ.currentGovernment.leaderTitleSuffix ?? string.Empty : string.Empty;

    /// <summary>"Lord Marcus" - the underlying Governor.Name is left untouched.</summary>
    public static string FormatGovernorName(Civilization civ, Governor governor)
    {
        if (governor == null) return GetGovernorTitleSingular(civ);
        string name = string.IsNullOrWhiteSpace(governor.Name) ? string.Empty : governor.Name;
        return string.IsNullOrEmpty(name) ? GetGovernorTitleSingular(civ) : $"{GetGovernorTitleSingular(civ)} {name}";
    }

    /// <summary>Formats a governor by id (council votes only store ids/raw names).</summary>
    public static string FormatGovernorName(Civilization civ, int governorId, string fallbackName)
    {
        Governor found = null;
        if (civ?.governors != null)
            foreach (var g in civ.governors)
                if (g != null && g.Id == governorId) { found = g; break; }
        if (found != null) return FormatGovernorName(civ, found);
        return string.IsNullOrWhiteSpace(fallbackName) ? GetGovernorTitleSingular(civ) : $"{GetGovernorTitleSingular(civ)} {fallbackName}";
    }

    public static string FormatCreateGovernorLabel(Civilization civ) => $"Create {GetGovernorTitleSingular(civ)}";

    /// <summary>"Lords 4 / 6".</summary>
    public static string FormatGovernorCap(Civilization civ)
    {
        int count = civ?.governors?.Count ?? 0;
        int cap = civ != null ? Mathf.Max(0, civ.governorCount) : 0;
        return $"{GetGovernorTitlePlural(civ)} {count} / {cap}";
    }

    /// <summary>"Senate 3 / 5".</summary>
    public static string FormatCouncilSeats(Civilization civ)
    {
        if (civ == null || !civ.HasRoyalCouncil) return string.Empty;
        return $"{GetInstitutionName(civ)} {civ.royalCouncil?.Count ?? 0} / {civ.MaxCouncilSeats}";
    }

    // Display-name fallbacks (asset name when the authored name is blank).
    public static string NameOf(GovernmentData g) => g == null ? string.Empty : Pick(g.governmentName, g.name);
    public static string NameOf(PolicyData p) => p == null ? string.Empty : Pick(p.policyName, p.name);
    public static string NameOf(TechData t) => t == null ? string.Empty : Pick(t.techName, t.name);
    public static string NameOf(CultureData c) => c == null ? string.Empty : Pick(c.cultureName, c.name);
    public static string NameOf(ReligionData r) => r == null ? string.Empty : Pick(r.religionName, r.name);
    public static string NameOf(Civilization civ) => civ == null ? string.Empty : Pick(civ.civData != null ? civ.civData.civName : null, civ.name);
    public static string NameOf(City city) => city == null ? string.Empty : Pick(city.cityName, city.name);
    public static string NameOf(Herd herd) => herd == null ? string.Empty : Pick(herd.herdName, herd.name);

    private static string Pick(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
