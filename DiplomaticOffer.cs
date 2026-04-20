using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The type of item included in a diplomatic deal.
/// </summary>
public enum DealItemType
{
    Gold,
    GoldPerTurn,
    Resource,
    Technology,
    City,
    DeclareWarOn,   // Demand the other civ declares war on a third party
    MakePeace,      // Part of a peace deal (no extra item, just the action)
    Alliance        // Offer/request an alliance
}

/// <summary>
/// A single item in one side of a diplomatic deal.
/// </summary>
[System.Serializable]
public class DealItem
{
    public DealItemType itemType;

    [Header("Gold")]
    public int goldAmount;          // Lump sum
    public int goldPerTurn;         // Per-turn gold for N turns
    public int goldPerTurnDuration; // How many turns the per-turn payment lasts

    [Header("Resource")]
    public ResourceData resource;
    public int resourceAmount = 1;

    [Header("Technology")]
    public TechData tech;

    [Header("City")]
    public City city;

    [Header("Diplomatic Action")]
    public Civilization targetCiv;  // For DeclareWarOn – which civ to attack

    /// <summary>
    /// Human-readable summary of this deal item.
    /// </summary>
    public string GetDisplayText()
    {
        switch (itemType)
        {
            case DealItemType.Gold:
                return $"{goldAmount} Gold";
            case DealItemType.GoldPerTurn:
                return $"{goldPerTurn} Gold/turn ({goldPerTurnDuration} turns)";
            case DealItemType.Resource:
                return resource != null
                    ? $"{resourceAmount}x {resource.resourceName}"
                    : "Unknown Resource";
            case DealItemType.Technology:
                return tech != null ? $"Tech: {tech.techName}" : "Unknown Tech";
            case DealItemType.City:
                return city != null ? $"City: {city.cityName}" : "Unknown City";
            case DealItemType.DeclareWarOn:
                return targetCiv != null
                    ? $"Declare War on {targetCiv.civData.civName}"
                    : "Declare War on ???";
            case DealItemType.MakePeace:
                return "Peace Treaty";
            case DealItemType.Alliance:
                return "Alliance";
            default:
                return "???";
        }
    }

    /// <summary>
    /// Estimate a numeric "value" of this item for AI evaluation.
    /// Higher = more valuable.
    /// </summary>
    public float EstimateValue(Civilization perspective)
    {
        switch (itemType)
        {
            case DealItemType.Gold:
                return goldAmount;
            case DealItemType.GoldPerTurn:
                return goldPerTurn * goldPerTurnDuration;
            case DealItemType.Resource:
                // Base value from yields
                if (resource == null) return 0f;
                float resVal = (resource.goldPerTurn + resource.productionPerTurn * 2
                                + resource.foodPerTurn * 1.5f + resource.sciencePerTurn * 2f
                                + resource.culturePerTurn * 1.5f + resource.faithPerTurn) * 10f;
                return resVal * resourceAmount;
            case DealItemType.Technology:
                return tech != null ? tech.scienceCost * 0.8f : 0f;
            case DealItemType.City:
                if (city == null) return 0f;
                float cityVal = 200f + city.level * 50f;
                if (city.isCapital) cityVal *= 3f; // Capitals are way more valuable
                return cityVal;
            case DealItemType.DeclareWarOn:
                return 150f; // Declaring war is a big ask
            case DealItemType.MakePeace:
                return 100f;
            case DealItemType.Alliance:
                return 80f;
            default:
                return 0f;
        }
    }
}

/// <summary>
/// Represents a full diplomatic deal between two civilizations.
/// Each side has a list of items they give.
/// </summary>
[System.Serializable]
public class DiplomaticOffer
{
    public Civilization proposer;
    public Civilization recipient;

    /// <summary>Items the proposer is offering (giving away).</summary>
    public List<DealItem> proposerItems = new List<DealItem>();

    /// <summary>Items the proposer is demanding from the recipient.</summary>
    public List<DealItem> recipientItems = new List<DealItem>();

    /// <summary>
    /// Is this deal empty (nothing on either side)?
    /// </summary>
    public bool IsEmpty => proposerItems.Count == 0 && recipientItems.Count == 0;

    /// <summary>
    /// Estimate the total value of one side of the deal.
    /// </summary>
    public float GetProposerValue(Civilization perspective)
    {
        float total = 0f;
        foreach (var item in proposerItems)
            total += item.EstimateValue(perspective);
        return total;
    }

    public float GetRecipientValue(Civilization perspective)
    {
        float total = 0f;
        foreach (var item in recipientItems)
            total += item.EstimateValue(perspective);
        return total;
    }

    /// <summary>
    /// Fairness ratio from the recipient's perspective.
    /// > 1 means deal favors recipient; &lt; 1 means deal favors proposer.
    /// </summary>
    public float GetFairnessRatio(Civilization recipientPerspective)
    {
        float giving = GetRecipientValue(recipientPerspective);
        float receiving = GetProposerValue(recipientPerspective);
        if (giving < 1f) giving = 1f; // Avoid div-by-zero
        return receiving / giving;
    }

    /// <summary>
    /// Clear both sides.
    /// </summary>
    public void Clear()
    {
        proposerItems.Clear();
        recipientItems.Clear();
    }
}
