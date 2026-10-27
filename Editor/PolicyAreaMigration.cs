#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Repeat-safe remap of PolicyData.policyArea from the 20 legacy areas to the 11 consolidated areas.</summary>
public static class PolicyAreaMigration
{
    /// <summary>Legacy Space (20) policies are assigned by function, keyed by asset name.</summary>
    public static readonly Dictionary<string, PolicyArea> SpacePolicyAssignments = new Dictionary<string, PolicyArea>
    {
        { "Space Colony Autonomy", PolicyArea.Administration },
        { "Centralized Planetary Authority", PolicyArea.Administration },
        { "Asteroid Mining Rights", PolicyArea.EconomyTrade },
        { "Orbital Industrialization", PolicyArea.AdvancedTechnology },
    };

    [MenuItem("Tools/Data Migration/Migrate Policy Areas")]
    public static void MigrateAll()
    {
        var report = new StringBuilder("[Policy Area Migration]\n");
        int migrated = 0, unchanged = 0, unresolved = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:PolicyData"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var policy = AssetDatabase.LoadAssetAtPath<PolicyData>(path);
            if (policy == null) continue;
            var serialized = new SerializedObject(policy);
            var property = serialized.FindProperty("policyArea");
            int legacy = property.intValue;
            var target = ResolveArea(policy.name, legacy);
            if (target == PolicyArea.Unassigned)
            {
                unresolved++;
                report.AppendLine($"  UNRESOLVED {policy.name}: legacy value {legacy} needs a manual assignment.");
                continue;
            }
            if ((int)target == legacy) { unchanged++; continue; }
            property.intValue = (int)target;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(policy);
            migrated++;
            report.AppendLine($"  {policy.name}: {legacy} -> {GovernmentPresentation.PolicyAreaDisplayName(target)}");
        }
        AssetDatabase.SaveAssets();
        report.AppendLine($"Migrated {migrated}, already valid {unchanged}, unresolved {unresolved}.");
        if (unresolved > 0) Debug.LogWarning(report.ToString()); else Debug.Log(report.ToString());
    }

    /// <summary>Consolidated area for a raw serialized value; valid values map to themselves.</summary>
    public static PolicyArea ResolveArea(string assetName, int legacyValue)
    {
        if (legacyValue == 20)
            return assetName != null && SpacePolicyAssignments.TryGetValue(assetName, out var area) ? area : PolicyArea.Unassigned;
        return PolicyAreaLegacyMap.Map(legacyValue);
    }
}
#endif
