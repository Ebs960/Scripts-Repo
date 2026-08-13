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

            string summary = BuildPolicyEffectSummary(policy);
            string line = $"{GetPolicyName(policy)}\n{summary}";

            if (policyRowPrefab != null)
            {
                var row = Instantiate(policyRowPrefab, bodyRoot, false);
                var rowComponent = row.GetComponent<HudGovernmentPolicyRow>();
                if (rowComponent != null)
                {
                    rowComponent.Populate(policy, summary);
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
        AddNamedRequirements<TechData>(effects, "Technology", policy.requiredTechs,
            x => !string.IsNullOrWhiteSpace(x.techName) ? x.techName : x.name, " AND ");
        AddNamedRequirements<CultureData>(effects, "Culture", policy.requiredCultures,
            x => !string.IsNullOrWhiteSpace(x.cultureName) ? x.cultureName : x.name, " AND ");
        AddNamedRequirements<GovernmentData>(effects, "Government", policy.requiredGovernments,
            x => !string.IsNullOrWhiteSpace(x.governmentName) ? x.governmentName : x.name, " OR ");
        AddNamedRequirements<PolicyData>(effects, "Required policies", policy.requiredPolicies, GetPolicyName, " AND ");
        AddNamedRequirements<PolicyData>(effects, "Conflicts with", policy.incompatiblePolicies, GetPolicyName, ", ");
        AddNamedRequirements<PolicyData>(effects, "Supersedes", policy.supersedesPolicies, GetPolicyName, ", ");
        if (policy.requiredCityCount > 0)
            effects.Add($"Cities Required: {policy.requiredCityCount}");
        if (policy.religiousRequirementGroups != null && policy.religiousRequirementGroups.Length > 0)
            effects.Add($"Religion: {policy.religiousRequirementGroups.Length} alternative route(s)");

        AddFloatPercent(effects, "Attack", policy.attackBonus);
        AddFloatPercent(effects, "Melee Attack", policy.meleeAttackBonus);
        AddFloatPercent(effects, "Ranged Attack", policy.rangedAttackBonus);
        AddFloatPercent(effects, "City Attack", policy.cityAttackBonus);
        AddFloatPercent(effects, "Defense", policy.defenseBonus);
        AddFloatPercent(effects, "Movement", policy.movementBonus);
        AddFloatPercent(effects, "Food", policy.foodModifier);
        AddFloatPercent(effects, "Production", policy.productionModifier);
        AddFloatPercent(effects, "Gold", policy.goldModifier);
        AddFloatPercent(effects, "Science", policy.scienceModifier);
        AddFloatPercent(effects, "Culture", policy.cultureModifier);
        AddFloatPercent(effects, "Faith", policy.faithModifier);

        AddFloatPercent(effects, "Population Growth", policy.populationGrowthModifier);
        AddFloatPercent(effects, "Migration Attraction", policy.migrationAttractionModifier);
        AddFloatPercent(effects, "War Weariness", policy.warWearinessModifier);
        AddFloatPercent(effects, "Corruption", policy.corruptionModifier);
        AddFloatPercent(effects, "Unrest", policy.unrestModifier);

        AddFloatPercent(effects, "Administrative Efficiency", policy.administrativeEfficiencyModifier);
        AddFloatPercent(effects, "Distance Loyalty Penalty", policy.distanceLoyaltyPenaltyModifier);
        AddFloatPercent(effects, "Policy Point Generation", policy.policyPointGenerationModifier);

        AddFloatPercent(effects, "Domestic Trade", policy.domesticTradeModifier);
        AddFloatPercent(effects, "Foreign Trade", policy.foreignTradeModifier);
        AddIntEffect(effects, "Trade Route Capacity", policy.tradeRouteCapacityBonus);

        AddFloatPercent(effects, "Labor Productivity", policy.laborProductivityModifier);
        AddFloatPercent(effects, "Unemployment Unhappiness", policy.unemploymentUnhappinessModifier);

        AddFloatPercent(effects, "Reinforcement Speed", policy.reinforcementSpeedModifier);
        AddFloatPercent(effects, "Military Upkeep", policy.militaryUpkeepModifier);

        AddFloatPercent(effects, "Cyber Defense", policy.cyberDefenseModifier);
        AddFloatPercent(effects, "Cyber Offense", policy.cyberOffenseModifier);
        AddFloatPercent(effects, "Espionage Defense", policy.espionageDefenseModifier);

        AddFloatPercent(effects, "Orbital Production", policy.orbitalProductionModifier);
        AddFloatPercent(effects, "Interplanetary Trade", policy.interplanetaryTradeModifier);
        AddFloatPercent(effects, "Planetary Loyalty", policy.planetaryLoyaltyModifier);
        AddFloatPercent(effects, "Planetary Defense", policy.planetaryDefenseModifier);

        AddEffectCount(effects, "Tile Yield Effects", policy.tileYieldBonuses);
        AddEffectCount(effects, "Building Effects", policy.buildingBonuses);
        AddEffectCount(effects, "Unit Yield Effects", policy.unitYieldBonuses);
        AddEffectCount(effects, "Unit Stat Effects", policy.unitBonuses);
        AddEffectCount(effects, "Equipment Yield Effects", policy.equipmentYieldBonuses);
        AddEffectCount(effects, "Worker Yield Effects", policy.workerYieldBonuses);
        AddEffectCount(effects, "Worker Stat Effects", policy.workerBonuses);
        AddEffectCount(effects, "Disease Effects", policy.diseaseBonuses);
        AddEffectCount(effects, "Attrition Effects", policy.attritionBonuses);
        AddEffectCount(effects, "City Effects", policy.cityBonuses);
        AddEffectCount(effects, "Non-State Religion Effects", policy.nonStateReligionUnhappinessModifiers);

        AddFloatPercent(effects, "Herd Starvation Reduction", policy.herdStarvationPercentReduction);
        AddEffectCount(effects, "Herd Yield Effects", policy.herdYieldBonuses);

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

        return string.Join(" • ", effects);
    }

    private static void AddFloatPercent(List<string> effects, string label, float value)
    {
        if (Mathf.Approximately(value, 0f))
            return;

        effects.Add($"{label} {(value > 0f ? "+" : string.Empty)}{value * 100f:0.##}%");
    }

    private static void AddIntEffect(List<string> effects, string label, int value)
    {
        if (value == 0)
            return;

        effects.Add($"{label} {(value > 0 ? "+" : string.Empty)}{value}");
    }

    private static void AddEffectCount<T>(List<string> effects, string label, T[] values)
    {
        if (values != null && values.Length > 0)
            effects.Add($"{label}: {values.Length}");
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
