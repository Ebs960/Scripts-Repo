#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>Alpha content gate for the canonical city screen and campaign unit presentation.</summary>
public static class AlphaFinalizationValidator
{
    public const string CanonicalCityPrefabPath = "Assets/UI/City UI Rebuild.prefab";
    public const string LegacyCityPrefabPath = "Assets/UI/City UI 2.prefab";

    public sealed class Finding
    {
        public string assetPath;
        public string message;
        public MessageType severity;
        public override string ToString() => $"{assetPath}: {message}";
    }

    [MenuItem("Tools/Alpha/Validate City UI and Campaign Animations")]
    public static void ValidateMenu()
    {
        var findings = ValidateAll();
        foreach (var finding in findings)
        {
            if (finding.severity == MessageType.Error) Debug.LogError(finding);
            else if (finding.severity == MessageType.Warning) Debug.LogWarning(finding);
            else Debug.Log(finding);
        }
        Debug.Log($"Alpha validation complete: {findings.Count(f => f.severity == MessageType.Error)} errors, " +
                  $"{findings.Count(f => f.severity == MessageType.Warning)} warnings.");
    }

    public static List<Finding> ValidateAll()
    {
        var result = new List<Finding>();
        ValidateCity(result);
        ValidateUnits(result);
        return result;
    }

    private static void ValidateCity(List<Finding> result)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CanonicalCityPrefabPath);
        if (prefab == null) { Add(result, CanonicalCityPrefabPath, "Canonical prefab is missing.", MessageType.Error); return; }
        var ui = prefab.GetComponent<CityUI>();
        if (ui == null) Add(result, CanonicalCityPrefabPath, "CityUI component is missing.", MessageType.Error);
        if (prefab.GetComponent<CityUITabController>() == null)
            Add(result, CanonicalCityPrefabPath, "Tabbed feature navigation is missing.", MessageType.Error);

        var legacy = AssetDatabase.LoadAssetAtPath<GameObject>(LegacyCityPrefabPath);
        if (legacy != null)
            Add(result, LegacyCityPrefabPath, "Legacy compatibility asset; do not reference from gameplay scenes.", MessageType.Info);

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path == CanonicalCityPrefabPath || path == LegacyCityPrefabPath) continue;
            var candidate = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (candidate != null && candidate.GetComponentInChildren<CityUI>(true) != null)
                Add(result, path, "Competing CityUI found; normal gameplay must use the canonical prefab.", MessageType.Error);
        }
    }

    private static void ValidateUnits(List<Finding> result)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Units" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var unit = prefab != null ? prefab.GetComponentInChildren<CombatUnit>(true) : null;
            if (unit == null) continue;
            var animator = prefab.GetComponentInChildren<Animator>(true);
            if (animator == null) { Add(result, path, "CombatUnit has no Animator.", MessageType.Error); continue; }
            var controller = animator.runtimeAnimatorController as AnimatorController;
            if (controller == null) { Add(result, path, "Animator has no inspectable AnimatorController.", MessageType.Error); continue; }

            var parameters = new HashSet<string>(controller.parameters.Select(p => p.name));
            Require(result, path, parameters, "IsWalking");
            Require(result, path, parameters, "Hit");
            Require(result, path, parameters, "Death");

            var data = unit.data;
            bool ranged = data != null && (data.unitType == CombatCategory.Archer ||
                data.unitType == CombatCategory.Crossbowman || data.unitType == CombatCategory.SpearThrower ||
                data.unitType == CombatCategory.Artillery || data.unitType == CombatCategory.RangedCavalry ||
                data.unitType == CombatCategory.Gunman);
            Require(result, path, parameters, ranged ? "RangedAttack" : "Attack");
        }
    }

    private static void Require(List<Finding> result, string path, HashSet<string> parameters, string parameter)
    {
        if (!parameters.Contains(parameter))
            Add(result, path, $"Animator is missing required '{parameter}' parameter.", MessageType.Error);
    }

    private static void Add(List<Finding> result, string path, string message, MessageType severity) =>
        result.Add(new Finding { assetPath = path, message = message, severity = severity });
}
#endif
