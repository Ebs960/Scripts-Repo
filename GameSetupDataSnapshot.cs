using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

[Serializable]
public sealed class GameSetupDataSnapshot
{
    [Serializable]
    private sealed class FieldValue
    {
        public string name;
        public string value;
    }

    [SerializeField] private List<FieldValue> values = new List<FieldValue>();
    public string selectedCivilizationName;
    public string selectedLeaderName;

    public static GameSetupDataSnapshot Capture()
    {
        var snapshot = new GameSetupDataSnapshot
        {
            selectedCivilizationName = GameSetupData.selectedPlayerCivilizationData != null
                ? GameSetupData.selectedPlayerCivilizationData.civName
                : null,
            selectedLeaderName = GameSetupData.selectedLeaderData != null
                ? GameSetupData.selectedLeaderData.leaderName
                : null
        };

        foreach (FieldInfo field in typeof(GameSetupData).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.IsLiteral || field.IsInitOnly || !IsSupported(field.FieldType))
                continue;

            object value = field.GetValue(null);
            snapshot.values.Add(new FieldValue
            {
                name = field.Name,
                value = Convert.ToString(value, CultureInfo.InvariantCulture)
            });
        }

        return snapshot;
    }

    public bool Restore()
    {
        if (values == null || values.Count == 0)
            return false;

        foreach (FieldValue saved in values)
        {
            if (saved == null || string.IsNullOrEmpty(saved.name))
                continue;

            FieldInfo field = typeof(GameSetupData).GetField(saved.name, BindingFlags.Public | BindingFlags.Static);
            if (field == null || !IsSupported(field.FieldType))
                continue;

            try
            {
                object parsed = field.FieldType.IsEnum
                    ? Enum.Parse(field.FieldType, saved.value)
                    : Convert.ChangeType(saved.value, field.FieldType, CultureInfo.InvariantCulture);
                field.SetValue(null, parsed);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SaveLoad] Could not restore game setup field '{saved.name}': {ex.Message}");
                return false;
            }
        }

        var civs = ResourceCache.GetAllCivDatas();
        GameSetupData.selectedPlayerCivilizationData = Array.Find(civs,
            civ => civ != null && string.Equals(civ.civName, selectedCivilizationName, StringComparison.Ordinal));

        var leaders = ResourceCache.GetAllLeaderData();
        GameSetupData.selectedLeaderData = Array.Find(leaders,
            leader => leader != null && string.Equals(leader.leaderName, selectedLeaderName, StringComparison.Ordinal));

        return !string.IsNullOrEmpty(selectedCivilizationName)
            && GameSetupData.selectedPlayerCivilizationData != null;
    }

    private static bool IsSupported(Type type)
    {
        return type == typeof(bool)
            || type == typeof(int)
            || type == typeof(float)
            || type == typeof(string)
            || type.IsEnum;
    }
}
