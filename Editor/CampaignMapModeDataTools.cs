#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class CampaignMapModeDataTools
{
    [MenuItem("Tools/Map Modes/Validate Map Mode Data")]
    public static void Validate()
    {
        int warnings = 0;
        foreach (var government in LoadAssets<GovernmentData>())
            if (!IsMeaningful(government.mapModeColor)) { warnings++; Debug.LogWarning($"[Map Modes] Government '{government.name}' has no authored map color.", government); }
        foreach (var religion in LoadAssets<ReligionData>())
            if (!IsMeaningful(religion.mapModeColor)) { warnings++; Debug.LogWarning($"[Map Modes] Religion '{religion.name}' has no authored map color.", religion); }
        foreach (var tileSystem in Object.FindObjectsByType<TileSystem>(FindObjectsSortMode.None))
        {
            int count = tileSystem.GetOwnerArray()?.Length ?? 0;
            for (int i = 0; i < count; i++)
            {
                var tile = tileSystem.GetTileData(i); if (tile == null) continue;
                if (!tile.IsWaterTile && tile.continentId < 0) { warnings++; Debug.LogWarning($"[Map Modes] Planet {tileSystem.planetIndex}, land tile {i} has invalid continentId.", tileSystem); }
                if (tile.controllingCity != null && tile.owner != tile.controllingCity.owner) { warnings++; Debug.LogWarning($"[Map Modes] Tile {i} controlling city owner is inconsistent.", tileSystem); }
            }
        }
        Debug.Log($"[Map Modes] Validation complete: {warnings} warning(s).");
    }

    [MenuItem("Tools/Map Modes/Assign Missing Default Colors")]
    public static void AssignMissing()
    {
        int index = 0, changed = 0;
        foreach (var government in LoadAssets<GovernmentData>()) if (!IsMeaningful(government.mapModeColor))
        { Undo.RecordObject(government, "Assign map mode color"); government.mapModeColor = Palette(index++); EditorUtility.SetDirty(government); changed++; }
        foreach (var religion in LoadAssets<ReligionData>()) if (!IsMeaningful(religion.mapModeColor))
        { Undo.RecordObject(religion, "Assign map mode color"); religion.mapModeColor = Palette(index++); EditorUtility.SetDirty(religion); changed++; }
        AssetDatabase.SaveAssets(); Debug.Log($"[Map Modes] Assigned {changed} missing colors; authored colors were preserved.");
    }

    private static List<T> LoadAssets<T>() where T : Object
    { var result = new List<T>(); foreach (string guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}")) { var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid)); if (asset != null) result.Add(asset); } return result; }
    private static bool IsMeaningful(Color c) => c.a > .01f && (c.r + c.g + c.b) > .03f;
    private static Color Palette(int index) => Color.HSVToRGB(Mathf.Repeat(index * .61803398875f, 1f), .68f, .9f);
}
#endif
