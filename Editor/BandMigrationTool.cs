#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;

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
        var data = AssetDatabase.LoadAssetAtPath<BandData>(path);
        if (data == null)
        {
            data = ScriptableObject.CreateInstance<BandData>();
            data.id = "paleolithic-band";
            data.displayName = "Band";
            data.description = "A mobile Paleolithic society that can travel, forage, encamp, and develop internal structures.";
            AssetDatabase.CreateAsset(data, path);
        }
        MigrateCampUpgrades(data);
        AssignPaleolithicUnits(data);
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        Selection.activeObject = data;
        Debug.Log("Created/updated Paleolithic BandData, six Camp-derived Band structures, and the four Paleolithic recruitment entries. Legacy assets were retained for compatibility.");
    }

    private static void MigrateCampUpgrades(BandData bandData)
    {
        const string campPath = "Assets/Improvements/Camp.asset";
        const string outputFolder = "Assets/Bands/Paleolithic Structures";
        EnsureFolder("Assets/Bands"); EnsureFolder(outputFolder);
        var camp = AssetDatabase.LoadAssetAtPath<ImprovementData>(campPath);
        if (camp == null) { Debug.LogWarning("Camp.asset was not found; Band structure migration skipped."); return; }
        var serialized = new SerializedObject(camp);
        var upgrades = serialized.FindProperty("availableUpgrades");
        bandData.allowedStructures = new List<BandStructureData>();
        for (int i = 0; upgrades != null && i < upgrades.arraySize; i++)
        {
            var source = upgrades.GetArrayElementAtIndex(i);
            string structureName = source.FindPropertyRelative("upgradeName").stringValue;
            if (System.Array.IndexOf(new[] { "Foraging Tent", "Story Circle", "Burial Pit", "Stone Pile", "Tool Maker", "Fishing Tent" }, structureName) < 0) continue;
            string assetPath = $"{outputFolder}/{structureName}.asset";
            var target = AssetDatabase.LoadAssetAtPath<BandStructureData>(assetPath);
            if (target == null) { target = ScriptableObject.CreateInstance<BandStructureData>(); AssetDatabase.CreateAsset(target, assetPath); }
            target.structureName = structureName;
            target.description = source.FindPropertyRelative("description").stringValue;
            target.icon = source.FindPropertyRelative("icon").objectReferenceValue as Sprite;
            target.goldCost = source.FindPropertyRelative("goldCost").intValue;
            target.productionCost = Mathf.Max(6, Mathf.CeilToInt(target.goldCost / 2f));
            target.requiredTech = source.FindPropertyRelative("requiredTech").objectReferenceValue as TechData;
            target.requiredCulture = source.FindPropertyRelative("requiredCulture").objectReferenceValue as CultureData;
            target.resourceCosts = ReadResourceCosts(source.FindPropertyRelative("resourceCosts"));
            target.yields = new BandYieldSet
            {
                food = source.FindPropertyRelative("additionalFood").intValue,
                production = source.FindPropertyRelative("additionalProduction").intValue,
                gold = source.FindPropertyRelative("additionalGold").intValue,
                science = source.FindPropertyRelative("additionalScience").intValue,
                culture = source.FindPropertyRelative("additionalCulture").intValue,
                policyPoints = source.FindPropertyRelative("additionalPolicyPoints").intValue,
                faith = source.FindPropertyRelative("additionalFaith").intValue
            };
            EditorUtility.SetDirty(target); bandData.allowedStructures.Add(target);
        }
    }

    private static ResourceCost[] ReadResourceCosts(SerializedProperty source)
    {
        if (source == null) return new ResourceCost[0];
        var costs = new ResourceCost[source.arraySize];
        for (int i = 0; i < source.arraySize; i++)
        {
            var item = source.GetArrayElementAtIndex(i);
            costs[i] = new ResourceCost { resource = item.FindPropertyRelative("resource").objectReferenceValue as ResourceData, amount = item.FindPropertyRelative("amount").intValue };
        }
        return costs;
    }

    private static void AssignPaleolithicUnits(BandData bandData)
    {
        string[] names = { "Hunter", "Clubman", "Spear Thrower", "Raft" };
        bandData.allowedMilitaryRecruitment = new List<CombatUnitData>();
        foreach (string unitName in names)
        {
            var unit = AssetDatabase.LoadAssetAtPath<CombatUnitData>($"Assets/Units/Paleolithic Units/{unitName}.asset");
            if (unit == null) continue;
            unit.buildableByBand = true;
            unit.bandProductionCost = Mathf.Max(1, unit.productionCost);
            EditorUtility.SetDirty(unit); bandData.allowedMilitaryRecruitment.Add(unit);
        }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        string name = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }
}
#endif
