using System.Collections.Generic;
using System.Text;
using UnityEngine;

public enum ResearchPopupStatus { Locked, Available, Active, Completed }

public class ResearchPopupInfo
{
    public string name;
    public Sprite icon;
    public string age;
    public ResearchPopupStatus status;
    public string description;
    public float cost;
    public float progress;
    public string currency;
    public int? turnsRemaining;
    public bool canStart;
    public string actionLabel;
    public string prerequisites;
    public string buildings;
    public string improvements;
    public string units;
    public string otherUnlocks;
    public string yields;
    public string bonuses;
    public string economicEffects;

    public static ResearchPopupInfo Create(string name, Sprite icon, TechAge age, ResearchPopupStatus status,
        float cost, float progress, int perTurn, string currency)
    {
        cost = Mathf.Max(0, cost);
        progress = status == ResearchPopupStatus.Completed ? cost : Mathf.Clamp(progress, 0, cost);
        return new ResearchPopupInfo
        {
            name = name, icon = icon, age = age.ToString().Replace("Age", " Age"), status = status,
            cost = cost, progress = progress, currency = currency,
            turnsRemaining = status == ResearchPopupStatus.Completed ? 0 : perTurn > 0
                ? (int?)Mathf.CeilToInt(Mathf.Max(0, cost - progress) / perTurn) : null
        };
    }

    public string BuildBody()
    {
        var body = new StringBuilder();
        Append(body, null, description);
        Append(body, "Cost", $"{cost:0} {currency}");
        if (status == ResearchPopupStatus.Active) Append(body, "Progress", $"{progress:0}/{cost:0}");
        if (status != ResearchPopupStatus.Completed)
            Append(body, "Estimated Turns", turnsRemaining.HasValue ? turnsRemaining.Value.ToString() : "No income");
        Append(body, "Prerequisites", prerequisites);
        Append(body, "Buildings", buildings);
        Append(body, "Improvements", improvements);
        Append(body, "Units", units);
        Append(body, "Other Unlocks", otherUnlocks);
        Append(body, null, yields);
        Append(body, "Bonuses", bonuses);
        Append(body, "Economic Effects", economicEffects);
        return body.ToString();
    }

    private static void Append(StringBuilder body, string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (body.Length > 0) body.Append("\n\n");
        if (!string.IsNullOrEmpty(label)) body.Append("<b>").Append(label).Append(":</b> ");
        body.Append(value);
    }

    public static string JoinRelevant(List<string> values) { return values == null ? string.Empty : string.Join(", ", values); }

    public static string FormatBonuses(float attack, float defense, float movement)
    {
        var values = new List<string>();
        if (attack != 0) values.Add($"{(attack > 0 ? "+" : "")}{attack:0.#}% Attack");
        if (defense != 0) values.Add($"{(defense > 0 ? "+" : "")}{defense:0.#}% Defense");
        if (movement != 0) values.Add($"{(movement > 0 ? "+" : "")}{movement:0.#} Movement");
        return JoinRelevant(values);
    }
}