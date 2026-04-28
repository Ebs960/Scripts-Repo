using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

public class HudGovernmentDropdown : MonoBehaviour
{
    [SerializeField] private HudDropdownButton dropdownButton;
    [SerializeField] private GameObject policyRowPrefab;
    [SerializeField] private TextMeshProUGUI governmentNameText;
    [SerializeField] private TextMeshProUGUI policyCountText;
    [SerializeField] private GameObject emptyStatePrefab;

    private Civilization currentCiv;

    public void Bind(Civilization civ)
    {
        currentCiv = civ;

        if (dropdownButton == null)
            dropdownButton = GetComponent<HudDropdownButton>();

        if (dropdownButton != null)
            dropdownButton.SetMainClick(OpenGovernmentPanel);

        Refresh();
    }

    public void Refresh()
    {
        if (dropdownButton == null)
            return;

        string govName = ResolveGovernmentName();
        if (governmentNameText != null)
            governmentNameText.text = govName;

        dropdownButton.SetLabel(govName);

        int policyCount = currentCiv?.activePolicies?.Count ?? 0;
        if (policyCountText != null)
            policyCountText.text = $"Policies: {policyCount}";

        RebuildBody();
    }

    private void RebuildBody()
    {
        dropdownButton.ClearBody();
        var bodyRoot = dropdownButton.BodyRootTransform;
        if (bodyRoot == null)
            return;

        var policies = currentCiv?.activePolicies;
        if (policies == null || policies.Count == 0)
        {
            AddEmptyRow("No active policies", bodyRoot);
            return;
        }

        foreach (var policy in policies)
        {
            if (policy == null)
                continue;

            if (policyRowPrefab == null)
            {
                AddEmptyRow($"{GetPolicyName(policy)}\n{BuildPolicyEffectSummary(policy)}", bodyRoot);
                continue;
            }

            var row = Instantiate(policyRowPrefab, bodyRoot, false);
            var rowComponent = row.GetComponent<HudGovernmentPolicyRow>();
            if (rowComponent != null)
            {
                rowComponent.Populate(policy, BuildPolicyEffectSummary(policy));
            }
            else
            {
                var tmp = row.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp != null)
                    tmp.text = $"{GetPolicyName(policy)}\n{BuildPolicyEffectSummary(policy)}";
            }
        }
    }

    private string ResolveGovernmentName()
    {
        var gov = currentCiv?.currentGovernment;
        if (gov == null)
            return "No Government";

        if (!string.IsNullOrWhiteSpace(gov.governmentName))
            return gov.governmentName;

        return gov.name;
    }

    private void OpenGovernmentPanel()
    {
        if (UIManager.Instance != null)
            UIManager.Instance.ShowPanel("GovernmentPanel");
    }

    private void AddEmptyRow(string text, Transform parent)
    {
        if (emptyStatePrefab != null)
        {
            var instance = Instantiate(emptyStatePrefab, parent, false);
            var tmp = instance.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null)
                tmp.text = text;
            return;
        }

        var go = new GameObject("EmptyState", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var label = go.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 18;
        label.color = Color.white;
    }

    private string BuildPolicyEffectSummary(PolicyData policy)
    {
        if (policy == null)
            return string.Empty;

        var effects = new List<string>();

        AddFloatPercent(effects, "Attack", policy.attackBonus);
        AddFloatPercent(effects, "Defense", policy.defenseBonus);
        AddFloatPercent(effects, "Movement", policy.movementBonus);
        AddFloatPercent(effects, "Food", policy.foodModifier);
        AddFloatPercent(effects, "Production", policy.productionModifier);
        AddFloatPercent(effects, "Gold", policy.goldModifier);
        AddFloatPercent(effects, "Science", policy.scienceModifier);
        AddFloatPercent(effects, "Culture", policy.cultureModifier);
        AddFloatPercent(effects, "Faith", policy.faithModifier);

        if (policy.additionalGovernorSlots != 0)
            effects.Add($"Governor Slots {(policy.additionalGovernorSlots > 0 ? "+" : string.Empty)}{policy.additionalGovernorSlots}");

        if (policy.unlockedGovernorTraits != null && policy.unlockedGovernorTraits.Length > 0)
        {
            var traitNames = new List<string>();
            foreach (var trait in policy.unlockedGovernorTraits)
            {
                if (trait == null) continue;
                traitNames.Add(!string.IsNullOrWhiteSpace(trait.traitName) ? trait.traitName : trait.name);
            }

            if (traitNames.Count > 0)
                effects.Add($"Unlocks Traits: {string.Join(", ", traitNames)}");
        }

        if (policy.governorOpinionEffects != null && policy.governorOpinionEffects.Length > 0)
            effects.Add($"Governor Opinion Effects: {policy.governorOpinionEffects.Length}");

        if (effects.Count == 0)
            return "No major modifiers";

        var sb = new StringBuilder();
        for (int i = 0; i < effects.Count; i++)
        {
            if (i > 0) sb.Append(" • ");
            sb.Append(effects[i]);
        }

        return sb.ToString();
    }

    private static void AddFloatPercent(List<string> effects, string label, float value)
    {
        if (Mathf.Approximately(value, 0f))
            return;

        effects.Add($"{label} {(value > 0f ? "+" : string.Empty)}{value:0.##}%");
    }

    private static string GetPolicyName(PolicyData policy)
    {
        if (!string.IsNullOrWhiteSpace(policy.policyName))
            return policy.policyName;
        return policy.name;
    }
}
