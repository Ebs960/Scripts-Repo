// Assets/Scripts/Cities/CityCitizenAssignment.cs
using System;
using UnityEngine;

public enum CityCitizenJobType
{
    TileWorker,
    RuralSpecialist,
    UrbanSpecialist,
    Unemployed
}

public enum SpecialistYieldType
{
    Food,
    Production,
    Gold,
    Science,
    Culture,
    Faith,
    PolicyPoints,
    Order,
    MilitaryXP,
    Administration
}

[Serializable]
public class SpecialistSlotDefinition
{
    [Header("Identity")]
    public string slotId;
    public string displayName;
    public Sprite icon;

    [Header("Job Type")]
    public CityCitizenJobType jobType;

    [Header("Yields")]
    public int food;
    public int production;
    public int gold;
    public int science;
    public int culture;
    public int faith;
    public int policyPoints;
    public int order;
    public int militaryXP;
    public int administration;

    [Header("Rules")]
    public bool requiresWorkedTile = false;
    public bool consumesPopulation = true;
}

[Serializable]
public class CityCitizenAssignment
{
    public CityCitizenJobType jobType;
    public int tileIndex = -1;

    public BuildingData building;
    public DistrictData district;
    public ImprovementData improvement;

    public string specialistSlotId;
    public bool locked;

    public string GetDebugLabel()
    {
        if (jobType == CityCitizenJobType.TileWorker)
            return $"Tile Worker on tile {tileIndex}";
        if (jobType == CityCitizenJobType.RuralSpecialist)
            return $"Rural Specialist {specialistSlotId} on tile {tileIndex}";
        if (jobType == CityCitizenJobType.UrbanSpecialist)
            return $"Urban Specialist {specialistSlotId}";
        return "Unemployed";
    }
}
