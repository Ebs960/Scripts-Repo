#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class HerdSetupValidator
{
    [MenuItem("Tools/Alpha/Validate Herd Setup")]
    public static void Validate()
    {
        int errors = 0, warnings = 0;
        if (Object.FindObjectOfType<HerdManager>() == null && AssetDatabase.FindAssets("t:Prefab Herd Manager").Length == 0)
        { Debug.LogError("[Herd Validation] No HerdManager exists in the open scene and no manager prefab was found."); errors++; }

        var panelGuids = AssetDatabase.FindAssets("t:Prefab Herd UI");
        var panel = panelGuids.Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
            .Select(x => x != null ? x.GetComponentInChildren<HerdPanel>(true) : null).FirstOrDefault(x => x != null);
        if (panel == null) { Debug.LogError("[Herd Validation] HerdPanel prefab is missing."); errors++; }
        else
        {
            var so = new SerializedObject(panel);
            foreach (string field in new[] { "buildEntryPrefab", "queueEntryPrefab", "garrisonEntryPrefab", "civilianEntryPrefab" })
                if (so.FindProperty(field)?.objectReferenceValue == null)
                { Debug.LogError($"[Herd Validation] Assign HerdPanel.{field} on {AssetDatabase.GetAssetPath(panel.gameObject)}."); errors++; }
        }

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach (var herd in prefab != null ? prefab.GetComponentsInChildren<Herd>(true) : new Herd[0])
            {
                if (herd.baseGarrisonCapacity < 0) { Debug.LogError($"[Herd Validation] {path}: garrison capacity cannot be negative."); errors++; }
                var serialized = new SerializedObject(herd);
                if ((serialized.FindProperty("storedUnits")?.arraySize ?? 0) > 0) { Debug.LogWarning($"[Herd Validation] {path}: migrate legacy mixed storedUnits to typed storage."); warnings++; }
                if (herd.owner?.civData != null && (herd.owner.civData.herdPackedPrefab == null || herd.owner.civData.herdSettledPrefab == null))
                { Debug.LogWarning($"[Herd Validation] {path}: assign both packed and settled visual prefabs on the civilization data."); warnings++; }
            }
        }
        Debug.Log($"Herd setup validation complete: {errors} errors, {warnings} warnings.");
    }
}
#endif
