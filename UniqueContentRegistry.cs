using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks which unit and building assets are civilization/leader unique replacements so the
/// generic content scans can hide them from civilizations that do not own them.
/// </summary>
public static class UniqueContentRegistry
{
    private static readonly HashSet<ScriptableObject> uniqueContent = new HashSet<ScriptableObject>();
    private static bool built;

    public static bool IsUnique(ScriptableObject asset)
    {
        if (asset == null) return false;
        EnsureBuilt();
        return uniqueContent.Contains(asset);
    }

    public static void Invalidate()
    {
        uniqueContent.Clear();
        built = false;
    }

    private static void EnsureBuilt()
    {
        if (built) return;
        built = true;

        foreach (var civ in ResourceCache.GetAllCivDatas())
        {
            if (civ == null) continue;
            AddAll(civ.uniqueUnits);
            AddAll(civ.uniqueBuildings);
        }

        foreach (var leader in ResourceCache.GetAllLeaderData())
        {
            if (leader == null) continue;
            if (leader.uniqueUnits != null)
                foreach (var def in leader.uniqueUnits)
                    if (def?.uniqueUnit != null) uniqueContent.Add(def.uniqueUnit);
            if (leader.uniqueBuildings != null)
                foreach (var def in leader.uniqueBuildings)
                    if (def?.uniqueBuilding != null) uniqueContent.Add(def.uniqueBuilding);
        }
    }

    private static void AddAll<T>(T[] assets) where T : ScriptableObject
    {
        if (assets == null) return;
        foreach (var asset in assets)
            if (asset != null) uniqueContent.Add(asset);
    }
}
