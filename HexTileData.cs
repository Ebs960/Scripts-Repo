using System;
using UnityEngine;

public enum Season
{
    Spring,
    Summer,
    Autumn,
    Winter
}

[Serializable]
public class HexTileData
{
    // --- Core Map Properties (existing) ---
    public Biome biome;
    public float elevation;        // 0–1
    public bool isLand;
    public bool isPassable;
    public int movementCost;
    public bool isHill; // Preserving existing property
    public bool isMoonTile; // Preserving existing property
    public float temperature; // Per-tile temperature, set during map generation
    public float moisture; // Per-tile moisture, set during map generation

    
    // --- Ownership & Control ---
    [Tooltip("Which civilization owns this tile (for city yields, improvements, etc.)")]
    public Civilization owner;
    [Tooltip("Which city exerts control over this tile")]
    public City controllingCity;

    // --- Static Features ---
    [Tooltip("Improvement built here, if any")]
    public ImprovementData improvement;
    [Tooltip("Resource on this tile, if any")]
    public ResourceData resource;
    [Tooltip("District built on this tile, if any")]
    public DistrictData district;

    // --- Religion Status ---
    [Tooltip("Religious pressure data for this tile")]
    public TileReligionStatus religionStatus;

    // --- Yields (per-turn) ---
    [Header("Tile Yields")]
    public int food;      // Renamed to match existing
    public int production; // Renamed to match existing
    public int gold;      // Renamed to match existing
    public int science;   // Renamed to match existing
    public int culture;   // Renamed to match existing
    public int policyPointYield;
    public int faithYield; // Added faith yield for tiles

    // --- Occupancy & Control ---
    public int occupantId; // Preserving existing property

    // --- Seasonal Modifier ---
    [Header("Seasonal")]
    [Tooltip("Current season on this planet")]
    public Season season;
    [Tooltip("Additional yield modifier this season (e.g. +20% in summer on farms)")]
    public float seasonalYieldModifier;

    // --- Special Space Flags ---
    [Header("Space Tile Flags")]
    [Tooltip("True if this tile is in orbit/space rather than on the planet surface")]
    public bool isSpace;
    [Tooltip("True if this tile is in a gas giant atmosphere (impassable)")]
    public bool isGasGiant;

    // --- Convenience Properties (computed at runtime) ---
    public bool HasResource => resource != null;
    public bool HasImprovement => improvement != null;
    public bool HasOwner => owner != null;
    public bool HasCity => controllingCity != null;
    public bool HasDistrict => district != null;
    public bool HasHolySite => HasDistrict && district.isHolySite;
    public bool HasReligion => religionStatus.GetDominantReligion() != null;

    /// <summary>
    /// Total effective yield after improvements, resource, and season.
    /// You can call this per-turn in your city/civ income loops.
    /// </summary>
    public TileYield GetTotalYield()
    {
        var y = new TileYield
        {
            Food = Mathf.RoundToInt(food * seasonalYieldModifier),
            Production = Mathf.RoundToInt(production * seasonalYieldModifier),
            Gold = Mathf.RoundToInt(gold * seasonalYieldModifier),
            Science = Mathf.RoundToInt(science * seasonalYieldModifier),
            Culture = Mathf.RoundToInt(culture * seasonalYieldModifier),
            Policy = Mathf.RoundToInt(policyPointYield * seasonalYieldModifier),
            Faith = Mathf.RoundToInt(faithYield * seasonalYieldModifier)
        };

        if (HasImprovement)
        {
            y.Food += improvement.foodPerTurn;
            y.Production += improvement.productionPerTurn; 
            y.Gold += improvement.goldPerTurn;
            y.Science += improvement.sciencePerTurn;
            y.Culture += improvement.culturePerTurn;
            y.Policy += improvement.policyPointsPerTurn;
            y.Faith += improvement.faithPerTurn;
        }

        if (HasResource)
        {
            y.Food += resource.foodPerTurn;
            y.Production += resource.productionPerTurn;
            y.Gold += resource.goldPerTurn;
            y.Science += resource.sciencePerTurn;
            y.Culture += resource.culturePerTurn;
            y.Policy += resource.policyPointsPerTurn;
            y.Faith += resource.faithPerTurn;
        }
        
        if (HasDistrict)
        {
            y.Food += district.baseFood;
            y.Production += district.baseProduction;
            y.Gold += district.baseGold;
            y.Science += district.baseScience;
            y.Culture += district.baseCulture;
            y.Faith += district.baseFaith;
            
            // Apply adjacency bonuses if this is a Holy Site
            if (HasHolySite)
            {
                // Get dominant religion to apply bonuses if applicable
                ReligionData dominantReligion = religionStatus.GetDominantReligion();
                if (dominantReligion != null)
                {
                    // Update to use founder belief bonuses if needed.
                }
            }
        }

        return y;
    }
}

/// <summary>
/// Simple struct to carry yields around.
/// </summary>
public struct TileYield
{
    public int Food;
    public int Production;
    public int Gold;
    public int Science;
    public int Culture;
    public int Policy;
    public int Faith;
}