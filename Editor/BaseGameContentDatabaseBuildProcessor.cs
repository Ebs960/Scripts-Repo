#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Refreshes the base-game manifest immediately before a player build.</summary>
public sealed class BaseGameContentDatabaseBuildProcessor : IPreprocessBuildWithReport
{
    public int callbackOrder => -1000;

    public void OnPreprocessBuild(BuildReport report)
    {
        const string directory = "Assets/Scripts Repo/Resources";
        const string path = directory + "/BaseGameContentDatabase.asset";
        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
        var database = AssetDatabase.LoadAssetAtPath<BaseGameContentDatabase>(path);
        if (database == null)
        {
            database = ScriptableObject.CreateInstance<BaseGameContentDatabase>();
            AssetDatabase.CreateAsset(database, path);
        }
        database.PopulateFromProject();
        Debug.Log("[BaseGameContentDatabase] Refreshed build-safe base content manifest.");
    }
}
#endif
