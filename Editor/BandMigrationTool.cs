#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates the new Band authoring asset without deleting the legacy WorkerUnitData Band or Camp.
/// Designers can copy project-specific visuals/unlocks after reviewing the generated asset.
/// </summary>
public static class BandMigrationTool
{
    [MenuItem("Tools/Campaign/Migrate Paleolithic Band Data")]
    public static void CreateBandData()
    {
        const string path = "Assets/Units/Paleolithic Units/Paleolithic Band Data.asset";
        if (AssetDatabase.LoadAssetAtPath<BandData>(path) != null) { Selection.activeObject = AssetDatabase.LoadAssetAtPath<BandData>(path); return; }
        var data = ScriptableObject.CreateInstance<BandData>();
        data.id = "paleolithic-band";
        data.displayName = "Band";
        data.description = "A mobile Paleolithic society that can travel, forage, encamp, and develop internal structures.";
        AssetDatabase.CreateAsset(data, path);
        AssetDatabase.SaveAssets();
        Selection.activeObject = data;
        Debug.Log("Created BandData. The legacy Band.asset and Camp.asset were retained for save/reference compatibility.");
    }
}
#endif
