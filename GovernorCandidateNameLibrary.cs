using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class GovernorCandidateNamePool
{
    public CultureGroup cultureGroup;
    public List<string> names = new List<string>();
}

[CreateAssetMenu(fileName = "GovernorCandidateNameLibrary", menuName = "Data/Governor Candidate Name Library")]
public class GovernorCandidateNameLibrary : ScriptableObject
{
    public List<GovernorCandidateNamePool> pools = new List<GovernorCandidateNamePool>();

    public string GetRandomName(CultureGroup cultureGroup)
    {
        var pool = pools?.Find(p => p != null && p.cultureGroup == cultureGroup && p.names != null && p.names.Exists(n => !string.IsNullOrWhiteSpace(n)));
        if (pool == null) return "Unnamed Candidate";
        var names = pool.names.FindAll(n => !string.IsNullOrWhiteSpace(n));
        return names[Random.Range(0, names.Count)].Trim();
    }
}

public static class GovernorCandidateNameService
{
    private static GovernorCandidateNameLibrary library;
    public static void Configure(GovernorCandidateNameLibrary value) => library = value;
    public static string GetRandomName(CultureGroup cultureGroup) => library != null ? library.GetRandomName(cultureGroup) : "Unnamed Candidate";
}
