using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>Display-only helpers shared by the tactical battle UI.</summary>
public static class BattleUIUtil
{
    public const string ResourceFolder = "TacticalBattleUI/";

    public static string UnitName(BattleUnitState unit)
    {
        string name = unit?.Snapshot?.SourceUnit != null ? unit.Snapshot.SourceUnit.UnitName : null;
        if (string.IsNullOrEmpty(name)) name = unit?.Snapshot?.UnitData != null ? unit.Snapshot.UnitData.unitName : null;
        return string.IsNullOrEmpty(name) ? $"Unit {unit?.UnitId}" : name;
    }

    public static Sprite UnitIcon(BattleUnitState unit)
    {
        var data = unit?.Snapshot?.UnitData;
        return data != null ? data.GetIcon(unit.Snapshot.Owner) : null;
    }

    public static string CivName(Civilization civ)
    {
        if (civ == null) return "Unknown Civilization";
        if (civ.civData != null && !string.IsNullOrEmpty(civ.civData.civName)) return civ.civData.civName;
        return string.IsNullOrEmpty(civ.name) ? "Unknown Civilization" : civ.name;
    }

    public static Sprite CivEmblem(Civilization civ) => civ != null && civ.civData != null ? civ.civData.icon : null;

    public static Civilization SideOwner(EngagementPreview preview, BattleSide side)
        => side == BattleSide.Attacker ? preview?.Attacker?.owner : preview?.Defender?.owner;

    public static string Humanize(string pascalCase)
        => string.IsNullOrEmpty(pascalCase) ? string.Empty : Regex.Replace(pascalCase, "(?<=[a-z])(?=[A-Z])", " ");

    public static BattleSide Opposite(BattleSide side) => side == BattleSide.Attacker ? BattleSide.Defender : BattleSide.Attacker;
}
