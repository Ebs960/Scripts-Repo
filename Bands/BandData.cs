using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class StartingBandGarrisonEntry
{
    public CombatUnitData unit;
    [Min(1)] public int count = 1;
}

[Serializable]
public sealed class BandVisualOverride
{
    public CivData civilization;
    public GameObject packedVisual;
    public GameObject encampedVisual;
}

/// <summary>Authoring data for a mobile, non-combat proto-settlement.</summary>
[CreateAssetMenu(fileName = "NewBandData", menuName = "Data/Band Data")]
public sealed class BandData : ScriptableObject
{
    [Header("Identity")]
    public string id;
    public string displayName = "Band";
    [TextArea] public string description;
    public Sprite icon;
    public GameObject prefab;

    [Header("Population and local food")]
    [Min(1)] public int startingPopulation = 30;
    [Min(0)] public int startingFoodReserve = 12;
    [Min(1)] public int foodStorageCapacity = 30;
    [Min(0)] public int baseFoodConsumptionPerTurn = 1;
    [Min(1)] public int populationPerFoodUnit = 10;
    [Min(0)] public int starvationGraceTurns = 2;
    [Range(0f, 1f)] public float populationLossPctPerStarvingTurn = .1f;
    [Min(1)] public int collapseAfterStarvationTurns = 8;

    [Header("Campaign actions")]
    [Min(0)] public int movementPoints = 2;
    [Min(0)] public int packMovementCost = 1;
    [Min(0)] public int encampMovementCost = 1;
    [Min(0)] public int forageMovementCost = 1;
    [Min(0)] public int baseForageFood = 4;

    [Header("Garrison and production")]
    [Min(0)] public int baseGarrisonCapacity = 4;
    public List<StartingBandGarrisonEntry> startingGarrison = new List<StartingBandGarrisonEntry>();
    public List<BandStructureData> allowedStructures = new List<BandStructureData>();
    public List<CombatUnitData> allowedMilitaryRecruitment = new List<CombatUnitData>();

    [Header("Empire yields (food remains local)")]
    public BandYieldSet packedYields;
    public BandYieldSet encampedYields;

    [Header("Settlement conversion")]
    public bool canFoundSettlement = true;
    [Min(1)] public int cityPopulationDivisor = 10;

    [Header("Presentation")]
    public GameObject packedVisual;
    public GameObject encampedVisual;
    public List<BandVisualOverride> civilizationVisualOverrides = new List<BandVisualOverride>();
}

[Serializable]
public struct BandYieldSet
{
    public int food;
    public int production;
    public int gold;
    public int science;
    public int culture;
    public int faith;
    public int policyPoints;
}
