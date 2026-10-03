using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

/// <summary>One shared, non-raycasting authored tooltip for every policy option.</summary>
public class PolicyTooltipUI : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private RectTransform tooltipRect;
    [SerializeField] private Canvas canvas;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TMP_Text policyNameText;
    [SerializeField] private TMP_Text areaText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private TMP_Text costText;
    [SerializeField] private float hoverDelay = 0.3f;

    private Coroutine pending;
    private GameObject Root => root != null ? root : gameObject;

    private void Awake()
    {
        if (canvasGroup != null) canvasGroup.blocksRaycasts = false;
        Hide();
    }

    public void ScheduleShow(Civilization civ, PolicyData policy, PolicyAdoptionEvaluation evaluation, Vector2 screenPosition)
    {
        Hide();
        pending = StartCoroutine(ShowDelayed(civ, policy, evaluation, screenPosition));
    }

    public void Hide()
    {
        if (pending != null) { StopCoroutine(pending); pending = null; }
        if (Root != null) Root.SetActive(false);
    }

    private IEnumerator ShowDelayed(Civilization civ, PolicyData policy, PolicyAdoptionEvaluation evaluation, Vector2 position)
    {
        yield return new WaitForSecondsRealtime(hoverDelay);
        pending = null;
        if (policy == null) yield break;
        GovernmentUiUtil.SetText(policyNameText, GovernmentPresentation.NameOf(policy));
        GovernmentUiUtil.SetText(areaText, GovernmentPresentation.PolicyAreaDisplayName(policy.policyArea));
        GovernmentUiUtil.SetText(statusText, evaluation != null ? evaluation.State.ToString() : "Locked");
        GovernmentUiUtil.SetText(descriptionText, BuildDescription(civ, policy, evaluation));
        GovernmentUiUtil.SetText(costText, $"Policy Points: -{policy.policyPointCost}");
        Root.SetActive(true);
        PositionAndClamp(position);
    }

    private static string BuildDescription(
        Civilization civ,
        PolicyData policy,
        PolicyAdoptionEvaluation evaluation)
    {
        var sections = new List<string>();
        if (!string.IsNullOrWhiteSpace(policy.description))
            sections.Add(policy.description.Trim());

        AddSection(sections, "EFFECTS",
            FormatEffects(PoliticalEffectSummaryBuilder.BuildPolicyEffects(policy)));
        AddSection(sections, "REQUIREMENTS",
            FormatRequirements(PoliticalEffectSummaryBuilder.BuildPolicyRequirements(civ, policy), evaluation));
        AddSection(sections, "RELATIONSHIPS", FormatRelationships(policy, evaluation));
        AddSection(sections, "GOVERNOR REACTIONS",
            FormatEffects(PoliticalEffectSummaryBuilder.BuildGovernorReactionLines(policy.governorOpinionEffects)));
        AddSection(sections, "COUNCIL OUTLOOK",
            FormatCouncil(PolicyManager.Instance?.PreviewPolicyVote(civ, policy, false)));

        return string.Join("\n\n", sections);
    }

    private static void AddSection(List<string> sections, string heading, string content)
    {
        if (!string.IsNullOrWhiteSpace(content))
            sections.Add($"<b>{heading}</b>\n{content.Trim()}");
    }

    private void PositionAndClamp(Vector2 screenPosition)
    {
        if (tooltipRect == null) return;
        var parent = tooltipRect.parent as RectTransform;
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        if (parent != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPosition, camera, out var local))
        {
            tooltipRect.anchoredPosition = local;
            Canvas.ForceUpdateCanvases();
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(parent, tooltipRect);
            var rect = parent.rect;
            local.x += Mathf.Max(0, rect.xMin - bounds.min.x) - Mathf.Max(0, bounds.max.x - rect.xMax);
            local.y += Mathf.Max(0, rect.yMin - bounds.min.y) - Mathf.Max(0, bounds.max.y - rect.yMax);
            tooltipRect.anchoredPosition = local;
        }
    }

    private static string FormatEffects(IEnumerable<PoliticalEffectLine> lines)
        => string.Join("\n", lines.Select(x => string.IsNullOrEmpty(x.value) ? x.label : $"{x.label}: {x.value}"));

    private static string FormatRequirements(IEnumerable<PoliticalRequirementLine> lines, PolicyAdoptionEvaluation evaluation)
    {
        var result = lines.Select(x => $"{(x.met ? "✓" : "✕")} {x.label}").ToList();
        if (evaluation != null) result.AddRange(evaluation.failureReasons);
        return string.Join("\n", result.Distinct());
    }

    private static string FormatRelationships(PolicyData policy, PolicyAdoptionEvaluation evaluation)
    {
        var lines = new List<string>();
        if (evaluation?.replacedAreaPolicy != null) lines.Add($"Would replace: {GovernmentPresentation.NameOf(evaluation.replacedAreaPolicy)}");
        Add(lines, "Requires", policy.requiredPolicies); Add(lines, "Conflicts", policy.incompatiblePolicies); Add(lines, "Supersedes", policy.supersedesPolicies);
        return string.Join("\n", lines);
    }

    private static void Add(List<string> lines, string label, PolicyData[] values)
    {
        var names = values?.Where(x => x != null).Select(GovernmentPresentation.NameOf).ToList();
        if (names != null && names.Count > 0) lines.Add($"{label}: {string.Join(", ", names)}");
    }

    private static string FormatCouncil(CouncilVoteResult vote)
        => vote == null || !vote.applicable
            ? string.Empty
            : $"{(vote.passed ? "Likely to pass" : "Likely to fail")} ({vote.yesVotes}–{vote.noVotes})";
}
