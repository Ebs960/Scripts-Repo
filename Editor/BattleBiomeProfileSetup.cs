#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public static class BattleBiomeProfileSetup
{
    [MenuItem("Tools/Battle/Create Missing Biome Visual Profiles")]
    public static void CreateMissingProfiles()
    {
        var database=Resources.Load<BattleBiomeVisualDatabase>("Battle Biome Visual Database");
        if(database==null){Debug.LogError("Create Resources/Battle Biome Visual Database first.");return;}
        const string folder="Assets/Resources/Battle Biome Profiles";
        EnsureFolders(folder);
        int created=0;
        foreach(Biome biome in Enum.GetValues(typeof(Biome)))
        {
            if(biome==Biome.Any||database.Get(biome)!=null)continue;
            var profile=ScriptableObject.CreateInstance<BattleBiomeVisualProfile>();profile.biome=biome;
            string path=$"{folder}/{biome}.asset";AssetDatabase.CreateAsset(profile,AssetDatabase.GenerateUniqueAssetPath(path));database.profiles.Add(profile);created++;
        }
        EditorUtility.SetDirty(database);AssetDatabase.SaveAssets();AssetDatabase.Refresh();
        Debug.Log($"Created {created} missing tactical biome visual profiles. Assign optional art prefabs in the generated assets.",database);
    }
    private static void EnsureFolders(string path){string current="Assets";foreach(string part in path.Substring(7).Split('/')){string next=$"{current}/{part}";if(!AssetDatabase.IsValidFolder(next))AssetDatabase.CreateFolder(current,part);current=next;}}
}
#endif
