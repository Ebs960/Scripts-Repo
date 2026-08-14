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

    private void Awake()
    {
        EnsureDropdownReference();
    }

    private void Reset()
    {
        EnsureDropdownReference();
    }

    private void OnValidate()
    {
        EnsureDropdownReference();
    }

    public void Bind(Civilization civ)
    {
        currentCiv = civ;
        EnsureDropdownReference();

        if (dropdownButton != null)
            dropdownButton.SetMainClick(OpenGovernmentPanel);

        Refresh();
    }

    public void Refresh()
    {
        if (dropdownButton == null)
            return;

        string govName = ResolveGovernmentName();
        dropdownButton.SetLabel(govName);
        dropdownButton.SetIcon(currentCiv?.currentGovernment?.icon);

        if (governmentNameText != null)
            governmentNameText.text = govName;

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

        string governmentSummary = BuildGovernmentSummary();
        if (!string.IsNullOrEmpty(governmentSummary))
            CreateSimpleTextRow("GovernmentSummary", governmentSummary, bodyRoot, 17, FontStyles.Normal);

        var policies = currentCiv?.activePolicies;
        if (policies == null || policies.Count == 0)
        {
            AddEmptyRow("No active policies", bodyRoot);
            dropdownButton.RebuildParentLayouts();
            return;
        }

        foreach (var policy in policies)
        {
            if (policy == null)
                continue;

            string line = $"{GetPolicyName(policy)}\n{BuildPolicyEffectSummary(policy)}";

            if (policyRowPrefab != null)
            {
                var row = Instantiate(policyRowPrefab, bodyRoot, false);
                var rowComponent = row.GetComponent<HudGovernmentPolicyRow>();
                if (rowComponent != null)
                {
                    rowComponent.Populate(policy, BuildPolicyEffectSummary(policy));
                }
                else
                {
                    SetRowTextIfPresent(row, line);
                }
            }
            else
            {
                CreateSimpleTextRow("PolicyRow", line, bodyRoot, 18, FontStyles.Normal);
            }
        }

        dropdownButton.RebuildParentLayouts();
    }

    private string BuildGovernmentSummary()
    {
        var g = currentCiv?.currentGovernment;
        if (g == null) return string.Empty;
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(g.leaderTitleSuffix)) lines.Add($"Leader: {g.leaderTitleSuffix}");
        if (!string.IsNullOrWhiteSpace(g.signatureMechanic)) lines.Add($"Signature: {g.signatureMechanic}");
        if (!string.IsNullOrWhiteSpace(g.majorTradeoff)) lines.Add($"Tradeoff: {g.majorTradeoff}");
        if (g.usesRoyalCouncil) lines.Add($"{g.institutionDisplayName}: {g.councilSeatCount} seats • Vetoes: {g.councilVetoDomains}");
        var rules = g.electionRules;
        var state = currentCiv.electionState;
        if (rules != null && rules.enabled)
        {
            lines.Add($"Election: {rules.electorateModel} electorate • {rules.termLengthTurns}-turn term • next turn {state?.nextElectionTurn ?? -1}");
            lines.Add($"Approval {state?.publicApproval ?? 50f:0}% • Legitimacy {state?.governmentLegitimacy ?? 50f:0}%");
            if (state?.currentOffice != null) lines.Add($"{state.currentOffice.title}: {state.currentOffice.officeholderName} • term ends turn {state.currentOffice.termEndTurn}");
            if (state?.activeElection?.issues != null && state.activeElection.issues.Count > 0)
                lines.Add("Issues: " + string.Join(", ", state.activeElection.issues.ConvertAll(i => i.summary)));
            if (state?.activeElection?.candidates != null && !state.activeElection.resolved)
                lines.Add("Candidates: " + string.Join(", ", state.activeElection.candidates.ConvertAll(c => c.displayName)));
        }
        return string.Join("\n", lines);
    }

    private void EnsureDropdownReference()
    {
        if (dropdownButton == null)
            dropdownButton = GetComponent<HudDropdownButton>();
    }

    private string ResolveGovernmentName()
    {
        var gov = currentCiv?.currentGovernment;
        if (gov == null)
            return "No Government";

        if (!string.IsNullOrWhiteSpace(gov.governmentName))
            return gov.governmentName;

        if (!string.IsNullOrWhiteSpace(gov.name))
            return gov.name;

        return "No Government";
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
            SetRowTextIfPresent(instance, text);
            return;
        }

        CreateSimpleTextRow("EmptyState", text, parent, 18, FontStyles.Italic);
    }

    private static void SetRowTextIfPresent(GameObject instance, string text)
    {
        var tmp = instance.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmp != null)
            tmp.text = text;
    }

    private static void CreateSimpleTextRow(string objectName, string text, Transform parent, float fontSize, FontStyles fontStyle)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        var label = go.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = fontStyle;
        label.color = Color.white;
        label.textWrappingMode = TextWrappingModes.Normal;

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
    }

    private string BuildPolicyEffectSummary(PolicyData policy)
    {
        if (policy == null)
            return string.Empty;

        var effects = new List<string>();

        effects.Add($"Cost: {policy.policyPointCost} PP");
        AddNamedRequirements<TechData>(effects, "Technology", policy.requiredTechs, x => x.name, " AND ");
        AddNamedRequirements<CultureData>(effects, "Culture", policy.requiredCultures, x => x.name, " AND ");
        AddNamedRequirements<GovernmentData>(effects, "Government", policy.requiredGovernments,
            x => !string.IsNullOrWhiteSpace(x.governmentName) ? x.governmentName : x.name, " OR ");
        AddNamedRequirements<PolicyData>(effects, "Required policies", policy.requiredPolicies, GetPolicyName, " AND ");
        AddNamedRequirements<PolicyData>(effects, "Conflicts with", policy.incompatiblePolicies, GetPolicyName, ", ");
        AddNamedRequirements<PolicyData>(effects, "Supersedes", policy.supersedesPolicies, GetPolicyName, ", ");
        if (policy.religiousRequirementGroups != null && policy.religiousRequirementGroups.Length > 0)
            effects.Add($"Religion: {policy.religiousRequirementGroups.Length} alternative route(s) (OR; clauses within each route are AND)");

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
                if (trait == null)
                    continue;

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
            if (i > 0)
                sb.Append(" • ");

            sb.Append(effects[i]);
        }

        return sb.ToString();
    }

    private static void AddFloatPercent(List<string> effects, string label, float value)
    {
        if (Mathf.Approximately(value, 0f))
            return;

        effects.Add($"{label} {(value > 0f ? "+" : string.Empty)}{value * 100f:0.##}%");
    }

    private static void AddNamedRequirements<T>(List<string> effects, string label, T[] values,
        System.Func<T, string> getName, string separator) where T : Object
    {
        if (values == null) return;
        var names = new List<string>();
        foreach (var value in values) if (value != null) names.Add(getName(value));
        if (names.Count > 0) effects.Add($"{label}: {string.Join(separator, names)}");
    }

    private static string GetPolicyName(PolicyData policy)
    {
        if (!string.IsNullOrWhiteSpace(policy.policyName))
            return policy.policyName;

        return policy.name;
    }
}
