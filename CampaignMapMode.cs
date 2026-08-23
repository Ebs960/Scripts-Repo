using System;
using UnityEngine;

public enum CampaignMapMode
{
    Normal,
    PoliticalOwnership,
    GovernmentType,
    Religion,
    Continents,
    Administration,
    Diplomacy
}

public enum CampaignDiplomacyCategory { Unknown, Self, Friendly, Neutral, Hostile }

[Serializable]
public struct MapModeTileVisual
{
    public int CategoryId;
    public Color Color;
    [Range(0f, 1f)] public float Strength;
    public string CategoryName;
    public bool IsNationalBorderCategory;

    public static MapModeTileVisual Hidden => new MapModeTileVisual
    { CategoryId = -1, Color = Color.clear, Strength = 0f, CategoryName = "Unknown" };
}

[Serializable]
public struct CampaignMapLegendEntry
{
    public int categoryId;
    public string label;
    public Color color;
    public int tileCount;
    public Sprite icon;
}
