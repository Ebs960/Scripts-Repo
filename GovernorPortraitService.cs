using System.Collections.Generic;
using UnityEngine;

/// <summary>Single resolver for selection, duplicate avoidance, IDs, sprites, and fallback behavior.</summary>
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
        return governor.AssignPortrait(ChoosePortraitId(pool, used));
    }

    /// <summary>Selection core exposed for deterministic validation/tests without coupling UI to selection.</summary>
    public static string ChoosePortraitId(GovernorPortraitPool pool, IEnumerable<string> usedPortraitIds)
    {
        if (pool?.portraits == null) return null;
        var valid = pool.portraits.FindAll(p => p != null && !string.IsNullOrWhiteSpace(p.portraitId));
        if (valid.Count == 0) return null;
        var used = usedPortraitIds != null ? new HashSet<string>(usedPortraitIds) : new HashSet<string>();
        var unused = valid.FindAll(p => !used.Contains(p.portraitId));
        var choices = unused.Count > 0 ? unused : valid;
        return choices[Random.Range(0, choices.Count)].portraitId;
    }
}
