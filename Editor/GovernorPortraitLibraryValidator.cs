using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class GovernorPortraitLibraryValidator
{
    [MenuItem("Tools/Validate Governor Portrait Libraries")]
    public static void ValidateAll()
    {
        var guids = AssetDatabase.FindAssets("t:GovernorPortraitLibrary");
        if (guids.Length == 0)
        {
            Debug.LogError("Governor Portrait Library\nPools: 0 / 64\nPortraits: 0 / 640\nErrors: 64\nWarnings: 0\nNo library asset found.");
            return;
        }
        foreach (var guid in guids)
            Validate(AssetDatabase.LoadAssetAtPath<GovernorPortraitLibrary>(AssetDatabase.GUIDToAssetPath(guid)));
    }

    public static bool Validate(GovernorPortraitLibrary library)
    {
        int errors = 0, warnings = 0, portraitCount = 0;
        var combinations = new HashSet<string>();
        var globalIds = new HashSet<string>();
        var pools = library != null ? library.pools : null;
        if (pools != null)
        {
            foreach (var pool in pools)
            {
                if (pool == null) { errors++; Debug.LogError("Governor portrait pool is null.", library); continue; }
                string key = $"{(int)pool.cultureGroup}:{(int)pool.era}";
                if (!combinations.Add(key)) { errors++; Debug.LogError($"Duplicate governor portrait pool {pool.cultureGroup}/{pool.era}.", library); }
                int count = pool.portraits?.Count ?? 0;
                portraitCount += count;
                if (count != 10) { errors++; Debug.LogError($"Pool {pool.cultureGroup}/{pool.era} contains {count}, expected 10.", library); }
                var sprites = new HashSet<Sprite>();
                if (pool.portraits == null) continue;
                foreach (var entry in pool.portraits)
                {
                    if (entry == null) { errors++; Debug.LogError($"Null entry in {pool.cultureGroup}/{pool.era}.", library); continue; }
                    if (string.IsNullOrWhiteSpace(entry.portraitId)) { errors++; Debug.LogError("Empty portrait ID.", library); }
                    else if (!globalIds.Add(entry.portraitId)) { errors++; Debug.LogError($"Duplicate portrait ID '{entry.portraitId}'.", library); }
                    if (entry.sprite == null) { errors++; Debug.LogError($"Portrait '{entry.portraitId}' has no Sprite.", library); }
                    else if (!sprites.Add(entry.sprite)) { warnings++; Debug.LogWarning($"Sprite reused in {pool.cultureGroup}/{pool.era}.", library); }
                }
            }
        }
        foreach (CultureGroup culture in Enum.GetValues(typeof(CultureGroup)))
            foreach (GovernorPortraitEra era in Enum.GetValues(typeof(GovernorPortraitEra)))
                if (!combinations.Contains($"{(int)culture}:{(int)era}")) { errors++; Debug.LogError($"Missing pool {culture}/{era}.", library); }
        Debug.Log($"Governor Portrait Library\nPools: {combinations.Count} / 64\nPortraits: {portraitCount} / 640\nErrors: {errors}\nWarnings: {warnings}", library);
        return errors == 0;
    }
}
