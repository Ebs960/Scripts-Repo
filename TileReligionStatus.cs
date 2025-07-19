using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct TileReligionStatus
{
    // The religion with the most pressure is the dominant religion for this tile
    public Dictionary<ReligionData, float> religionPressures;
    public bool hasHolySite;
    public DistrictData holySiteDistrict;
    
    // Helper to get the dominant religion
    public ReligionData GetDominantReligion()
    {
        if (religionPressures == null || religionPressures.Count == 0)
            return null;
            
        ReligionData dominant = null;
        float highestPressure = 0f;
        
        foreach (var kvp in religionPressures)
        {
            if (kvp.Value > highestPressure)
            {
                highestPressure = kvp.Value;
                dominant = kvp.Key;
            }
        }
        
        return dominant;
    }
    
    // Initialize an empty pressure map
    public void Initialize()
    {
        religionPressures = new Dictionary<ReligionData, float>();
        hasHolySite = false;
        holySiteDistrict = null;
    }
    
    // Add pressure for a specific religion
    public void AddPressure(ReligionData religion, float amount)
    {
        if (religionPressures == null)
            Initialize();
            
        if (!religionPressures.ContainsKey(religion))
            religionPressures[religion] = 0f;
            
        religionPressures[religion] += amount;
    }
} 