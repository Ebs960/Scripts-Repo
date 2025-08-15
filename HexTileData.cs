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
    public ElevationTier elevationTier = ElevationTier.Flat;
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
    [Tooltip("Upgrades built on this improvement")]
    public System.Collections.Generic.List<string> builtUpgrades = new System.Collections.Generic.List<string>();
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

    // Local yield bonus aggregator
    private struct YieldAgg { public int foodAdd, productionAdd, goldAdd, scienceAdd, cultureAdd, faithAdd, policyAdd; public float foodPct, productionPct, goldPct, sciencePct, culturePct, faithPct, policyPct; }
    private static YieldAgg AggregateImprovementBonusesLocal(Civilization civ, ImprovementData imp)
    {
        YieldAgg a = new YieldAgg(); if (civ == null || imp == null) return a;
        if (civ.researchedTechs != null)
            foreach (var t in civ.researchedTechs)
            {
                if (t?.improvementBonuses == null) continue;
                foreach (var b in t.improvementBonuses)
                    if (b != null && b.improvement == imp)
                    {
                        a.foodAdd += b.foodAdd; a.productionAdd += b.productionAdd; a.goldAdd += b.goldAdd;
                        a.scienceAdd += b.scienceAdd; a.cultureAdd += b.cultureAdd; a.faithAdd += b.faithAdd; a.policyAdd += b.policyPointsAdd;
                        a.foodPct += b.foodPct; a.productionPct += b.productionPct; a.goldPct += b.goldPct;
                        a.sciencePct += b.sciencePct; a.culturePct += b.culturePct; a.faithPct += b.faithPct; a.policyPct += b.policyPointsPct;
                    }
            }
        if (civ.researchedCultures != null)
            foreach (var c in civ.researchedCultures)
            {
                if (c?.improvementBonuses == null) continue;
                foreach (var b in c.improvementBonuses)
                    if (b != null && b.improvement == imp)
                    {
                        a.foodAdd += b.foodAdd; a.productionAdd += b.productionAdd; a.goldAdd += b.goldAdd;
                        a.scienceAdd += b.scienceAdd; a.cultureAdd += b.cultureAdd; a.faithAdd += b.faithAdd; a.policyAdd += b.policyPointsAdd;
                        a.foodPct += b.foodPct; a.productionPct += b.productionPct; a.goldPct += b.goldPct;
                        a.sciencePct += b.sciencePct; a.culturePct += b.culturePct; a.faithPct += b.faithPct; a.policyPct += b.policyPointsPct;
                    }
            }
        return a;
    }

    private static YieldAgg AggregateGenericBonusesLocal(Civilization civ, ScriptableObject target)
    {
        YieldAgg a = new YieldAgg(); if (civ == null || target == null) return a;
        if (civ.researchedTechs != null)
            foreach (var t in civ.researchedTechs)
            {
                if (t?.genericYieldBonuses == null) continue;
                foreach (var b in t.genericYieldBonuses)
                    if (b != null && b.target == target)
                    {
                        a.foodAdd += b.foodAdd; a.productionAdd += b.productionAdd; a.goldAdd += b.goldAdd;
                        a.scienceAdd += b.scienceAdd; a.cultureAdd += b.cultureAdd; a.faithAdd += b.faithAdd;
                        a.foodPct += b.foodPct; a.productionPct += b.productionPct; a.goldPct += b.goldPct;
                        a.sciencePct += b.sciencePct; a.culturePct += b.culturePct; a.faithPct += b.faithPct;
                    }
            }
        if (civ.researchedCultures != null)
            foreach (var c in civ.researchedCultures)
            {
                if (c?.genericYieldBonuses == null) continue;
                foreach (var b in c.genericYieldBonuses)
                    if (b != null && b.target == target)
                    {
                        a.foodAdd += b.foodAdd; a.productionAdd += b.productionAdd; a.goldAdd += b.goldAdd;
                        a.scienceAdd += b.scienceAdd; a.cultureAdd += b.cultureAdd; a.faithAdd += b.faithAdd;
                        a.foodPct += b.foodPct; a.productionPct += b.productionPct; a.goldPct += b.goldPct;
                        a.sciencePct += b.sciencePct; a.culturePct += b.culturePct; a.faithPct += b.faithPct;
                    }
            }
        return a;
    }

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
            int f = improvement.foodPerTurn;
            int p = improvement.productionPerTurn;
            int g = improvement.goldPerTurn;
            int s = improvement.sciencePerTurn;
            int c = improvement.culturePerTurn;
            int pol = improvement.policyPointsPerTurn;
            int fa = improvement.faithPerTurn;

            if (HasOwner)
            {
                var agg = AggregateImprovementBonusesLocal(owner, improvement);
                f = Mathf.RoundToInt((f + agg.foodAdd) * (1f + agg.foodPct));
                p = Mathf.RoundToInt((p + agg.productionAdd) * (1f + agg.productionPct));
                g = Mathf.RoundToInt((g + agg.goldAdd) * (1f + agg.goldPct));
                s = Mathf.RoundToInt((s + agg.scienceAdd) * (1f + agg.sciencePct));
                c = Mathf.RoundToInt((c + agg.cultureAdd) * (1f + agg.culturePct));
                pol = Mathf.RoundToInt((pol + agg.policyAdd) * (1f + agg.policyPct));
                fa = Mathf.RoundToInt((fa + agg.faithAdd) * (1f + agg.faithPct));
            }

            y.Food += f;
            y.Production += p; 
            y.Gold += g;
            y.Science += s;
            y.Culture += c;
            y.Policy += pol;
            y.Faith += fa;
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
            int f = district.baseFood;
            int p = district.baseProduction;
            int g = district.baseGold;
            int s = district.baseScience;
            int c = district.baseCulture;
            int fa = district.baseFaith;

            if (HasOwner)
            {
                var agg = AggregateGenericBonusesLocal(owner, district);
                f = Mathf.RoundToInt((f + agg.foodAdd) * (1f + agg.foodPct));
                p = Mathf.RoundToInt((p + agg.productionAdd) * (1f + agg.productionPct));
                g = Mathf.RoundToInt((g + agg.goldAdd) * (1f + agg.goldPct));
                s = Mathf.RoundToInt((s + agg.scienceAdd) * (1f + agg.sciencePct));
                c = Mathf.RoundToInt((c + agg.cultureAdd) * (1f + agg.culturePct));
                fa = Mathf.RoundToInt((fa + agg.faithAdd) * (1f + agg.faithPct));
            }

            y.Food += f;
            y.Production += p;
            y.Gold += g;
            y.Science += s;
            y.Culture += c;
            y.Faith += fa;
            
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