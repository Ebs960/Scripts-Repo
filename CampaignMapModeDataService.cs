using System.Collections.Generic;
using UnityEngine;

/// <summary>Presentation-only adapter over authoritative campaign state.</summary>
public sealed class CampaignMapModeDataService
{
    private readonly CampaignMapModePresentationData presentation;
    public CampaignMapModeDataService(CampaignMapModePresentationData presentation) { this.presentation = presentation; }

    public MapModeTileVisual GetVisual(TileSystem tiles, int tileIndex, CampaignMapMode mode,
        Civilization referenceCivilization, byte fogState)
    {
        if (mode == CampaignMapMode.Normal || tiles == null || fogState == 0) return MapModeTileVisual.Hidden;
        var tile = tiles.GetTileData(tileIndex);
        if (tile == null) return MapModeTileVisual.Hidden;

        bool dynamicMode = mode != CampaignMapMode.Continents;
        if (fogState == 1 && dynamicMode)
            return Visual(-2, "Stale / Unknown", presentation.unknownColor, presentation.staleDynamicStrength);

        MapModeTileVisual result;
        switch (mode)
        {
            case CampaignMapMode.PoliticalOwnership: result = Political(tiles, tile); break;
            case CampaignMapMode.GovernmentType: result = Government(tile); break;
            case CampaignMapMode.Religion: result = Religion(tiles, tileIndex); break;
            case CampaignMapMode.Continents: result = Continent(tile); break;
            case CampaignMapMode.Administration: result = Administration(tiles, tile); break;
            case CampaignMapMode.Diplomacy: result = Diplomacy(tile, referenceCivilization); break;
            default: return MapModeTileVisual.Hidden;
        }
        result.Strength *= presentation.BlendFor(mode);
        if (fogState == 1) result.Strength *= presentation.exploredStaticStrength;
        result.Color.a = result.Strength;
        return result;
    }

    private MapModeTileVisual Political(TileSystem ts, HexTileData tile)
    {
        if (tile.owner == null) return MapModeTileVisual.Hidden;
        return Visual(1000 + tile.owner.MapActorSlot, CivName(tile.owner), PoliticalColor(ts, tile.owner), 1f, true);
    }

    private MapModeTileVisual Government(HexTileData tile)
    {
        if (tile.owner == null) return MapModeTileVisual.Hidden;
        var gov = tile.owner.currentGovernment;
        if (gov == null) return Visual(2000, "No Formal Government", presentation.noGovernmentColor, 1f, true);
        Color color = IsMeaningful(gov.mapModeColor) ? gov.mapModeColor : DeterministicColor(gov.GetInstanceID(), .65f, .9f);
        return Visual(StableAssetCategory(gov, 2100), gov.governmentName, color, 1f, true);
    }

    private MapModeTileVisual Religion(TileSystem ts, int tileIndex)
    {
        var pressures = ts.GetReligionPressures(tileIndex);
        float total = 0f, highest = 0f; ReligionData dominant = null;
        if (pressures != null)
            for (int i = 0; i < pressures.Count; i++)
            {
                var entry = pressures[i];
                if (entry.religion == null || entry.pressure <= 0f) continue;
                total += entry.pressure;
                if (entry.pressure > highest) { highest = entry.pressure; dominant = entry.religion; }
            }
        if (dominant == null || total <= Mathf.Epsilon)
            return Visual(3000, "No Dominant Religion", presentation.noReligionColor, 1f);
        float dominance = highest / total;
        Color color = IsMeaningful(dominant.mapModeColor) ? dominant.mapModeColor : DeterministicColor(dominant.GetInstanceID(), .65f, .9f);
        return Visual(StableAssetCategory(dominant, 3100), dominant.religionName, color,
            Mathf.Lerp(presentation.minimumReligionDominanceStrength, 1f, dominance));
    }

    private MapModeTileVisual Continent(HexTileData tile)
    {
        if (tile.IsWaterTile || tile.continentId < 0)
            return Visual(4000, "Water / Unassigned", presentation.waterColor, 1f);
        string label = string.IsNullOrWhiteSpace(tile.continentName) ? $"Continent {tile.continentId + 1}" : tile.continentName;
        return Visual(4100 + tile.continentId, label, DeterministicColor(tile.continentId, .62f, .88f), 1f);
    }

    private MapModeTileVisual Administration(TileSystem ts, HexTileData tile)
    {
        if (tile.owner == null) return MapModeTileVisual.Hidden;
        Color baseColor = PoliticalColor(ts, tile.owner);
        var governor = tile.controllingCity != null ? tile.controllingCity.governor : null;
        if (governor == null)
            return Visual(500000 + Mathf.Max(0, tile.owner.MapActorSlot) * 1000, $"{CivName(tile.owner)} — Direct Rule", baseColor, 1f, true);
        int stable = Mathf.Abs(governor.Id);
        Color.RGBToHSV(baseColor, out float h, out float s, out float v);
        h = Mathf.Repeat(h + ((stable * 37) % 19 - 9) / 100f, 1f);
        v = Mathf.Clamp01(v + ((stable & 1) == 0 ? .14f : -.14f));
        Color variant = Color.HSVToRGB(h, Mathf.Clamp01(s * .9f), v);
        return Visual(500001 + Mathf.Max(0, tile.owner.MapActorSlot) * 1000 + stable,
            $"{CivName(tile.owner)} — {governor.Name}", variant, 1f, true);
    }

    private MapModeTileVisual Diplomacy(HexTileData tile, Civilization reference)
    {
        if (tile.owner == null) return MapModeTileVisual.Hidden;
        CampaignDiplomacyCategory category = ClassifyDiplomacy(reference, tile.owner);
        Color color = presentation.unknownColor;
        if (category == CampaignDiplomacyCategory.Self) color = presentation.selfColor;
        else if (category == CampaignDiplomacyCategory.Friendly) color = presentation.friendlyColor;
        else if (category == CampaignDiplomacyCategory.Neutral) color = presentation.neutralColor;
        else if (category == CampaignDiplomacyCategory.Hostile) color = presentation.hostileColor;
        return Visual(6000 + (int)category, category.ToString(), color, 1f, true);
    }

    public static CampaignDiplomacyCategory ClassifyDiplomacy(Civilization reference, Civilization other)
    {
        if (reference == null || other == null) return CampaignDiplomacyCategory.Unknown;
        if (reference == other) return CampaignDiplomacyCategory.Self;
        DiplomaticState state = DiplomacyManager.Instance != null
            ? DiplomacyManager.Instance.GetRelationship(reference, other) : DiplomaticState.Peace;
        return ClassifyDiplomaticState(false, state);
    }

    public static CampaignDiplomacyCategory ClassifyDiplomaticState(bool isSelf, DiplomaticState state)
    {
        if (isSelf) return CampaignDiplomacyCategory.Self;
        if (state == DiplomaticState.War) return CampaignDiplomacyCategory.Hostile;
        if (state == DiplomaticState.Alliance || state == DiplomaticState.Vassal) return CampaignDiplomacyCategory.Friendly;
        return CampaignDiplomacyCategory.Neutral;
    }

    public static float CalculateDominance(float dominantPressure, float totalPressure)
        => totalPressure > Mathf.Epsilon ? Mathf.Clamp01(dominantPressure / totalPressure) : 0f;

    public string GetHoverText(TileSystem ts, int tileIndex, CampaignMapMode mode, Civilization reference, byte fog)
    {
        var v = GetVisual(ts, tileIndex, mode, reference, fog);
        if (v.Strength <= 0f) return "Unexplored";
        var tile = ts.GetTileData(tileIndex);
        if (mode == CampaignMapMode.Religion)
        {
            var p = ts.GetReligionPressures(tileIndex); float total = 0, best = 0;
            if (p != null) for (int i = 0; i < p.Count; i++) { total += Mathf.Max(0, p[i].pressure); best = Mathf.Max(best, p[i].pressure); }
            return $"Dominant Religion: {v.CategoryName}\nDominance: {(total > 0 ? best / total : 0):P0}" + (ts.HasHolySite(tileIndex) ? "\nHoly Site" : "");
        }
        if (mode == CampaignMapMode.Administration && tile != null)
            return $"{CivName(tile.owner)}\nControlling City: {(tile.controllingCity != null ? tile.controllingCity.cityName : "None")}\n{(tile.controllingCity?.governor != null ? "Governor: " + tile.controllingCity.governor.Name : "Direct Rule")}";
        if (mode == CampaignMapMode.GovernmentType) return $"{CivName(tile?.owner)}\nGovernment: {v.CategoryName}";
        if (mode == CampaignMapMode.PoliticalOwnership) return $"{v.CategoryName}\nControlled by {v.CategoryName}";
        if (mode == CampaignMapMode.Continents) return $"Continent: {v.CategoryName}";
        if (mode == CampaignMapMode.Diplomacy) return $"{CivName(tile?.owner)}\nRelationship to {CivName(reference)}: {v.CategoryName}";
        return v.CategoryName;
    }

    private static MapModeTileVisual Visual(int id, string name, Color color, float strength, bool national = false)
    { return new MapModeTileVisual { CategoryId = id, CategoryName = name, Color = color, Strength = strength, IsNationalBorderCategory = national }; }
    private static string CivName(Civilization civ) => civ?.civData != null ? civ.civData.civName : (civ != null ? civ.name : "Neutral");
    private static Color PoliticalColor(TileSystem ts, Civilization civ)
    {
        var colors = ts.GetOwnerColors(); int slot = civ != null ? civ.MapActorSlot : -1;
        return colors != null && slot >= 0 && slot < colors.Length ? colors[slot] : DeterministicColor(slot, .65f, .95f);
    }
    private static int StableAssetCategory(Object asset, int offset) => offset + Mathf.Abs(asset.GetInstanceID());
    private static bool IsMeaningful(Color color) => color.a > .01f && color.r + color.g + color.b > .03f;
    private static Color DeterministicColor(int id, float saturation, float value)
    { return Color.HSVToRGB(Mathf.Repeat(id * .61803398875f, 1f), saturation, value); }
}
