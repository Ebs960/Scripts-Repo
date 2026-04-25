using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(fileName = "ReligionDatabase", menuName = "Data/Religion Database", order = 210)]
public class ReligionDatabase : ScriptableObject
{
    [Header("Core Collections")]
    public PantheonData[] pantheons;
    public ReligionData[] religions;
    public BeliefData[] beliefs;

    [TextArea(3, 6)]
    public string notes;

#if UNITY_EDITOR
    [ContextMenu("Populate From Project")]
    private void PopulateFromProject()
    {
        pantheons = LoadAssets<PantheonData>("Assets/Scripts Repo/Religion/Pantheons");
        religions = LoadAssets<ReligionData>("Assets/Scripts Repo/Religion/Different Religions");
        beliefs = LoadAssets<BeliefData>("Assets/Scripts Repo/Religion/Beliefs");
        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();
    }

    private static T[] LoadAssets<T>(string folder) where T : ScriptableObject
    {
        string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { folder });
        return guids
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<T>)
            .Where(asset => asset != null)
            .OrderBy(asset => asset.name)
            .ToArray();
    }
#endif
}