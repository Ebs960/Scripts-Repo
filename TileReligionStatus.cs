using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct TileReligionStatus
{
    [System.Serializable]
    public struct ReligionPressureEntry
    {
        public ReligionData religion;
        public float pressure;
    }

    // Use a serializable list so Unity can persist this structure
    public List<ReligionPressureEntry> pressures;
    public bool hasHolySite;
    public DistrictData holySiteDistrict;

    // Helper to get the dominant religion
    public ReligionData GetDominantReligion()
    {
        if (pressures == null || pressures.Count == 0) return null;
        ReligionData best = null; float bestVal = 0f;
        foreach (var e in pressures)
        {
            if (e.religion == null) continue;
            if (e.pressure > bestVal) { bestVal = e.pressure; best = e.religion; }
        }
        return best;
    }

    public void Initialize()
    {
        pressures = new List<ReligionPressureEntry>();
        hasHolySite = false;
        holySiteDistrict = null;
    }

    // Add pressure for a specific religion (aggregates into list)
    public void AddPressure(ReligionData religion, float amount)
    {
        if (religion == null || amount == 0f) return;
        if (pressures == null) pressures = new List<ReligionPressureEntry>();
        for (int i = 0; i < pressures.Count; i++)
        {
            if (pressures[i].religion == religion)
            {
                var e = pressures[i]; e.pressure += amount; pressures[i] = e; return;
            }
        }
        pressures.Add(new ReligionPressureEntry { religion = religion, pressure = amount });
    }

    // Optional getter for total pressure for a religion
    public float GetPressureFor(ReligionData religion)
    {
        if (religion == null || pressures == null) return 0f;
        foreach (var e in pressures) if (e.religion == religion) return e.pressure;
        return 0f;
    }
}