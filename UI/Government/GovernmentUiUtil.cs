using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Null-safe binding helpers shared by the Government screen. Rows always come from authored prefabs.</summary>
public static class GovernmentUiUtil
{
    public static void SetText(TMP_Text text, string value)
    {
        if (text != null) text.text = value ?? string.Empty;
    }

    public static void SetActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active) go.SetActive(active);
    }

    public static void SetInteractable(Button button, bool interactable)
    {
        if (button != null) button.interactable = interactable;
    }

    public static void SetImage(Image image, Sprite sprite)
    {
        if (image == null) return;
        image.sprite = sprite;
        image.preserveAspect = true;
        image.enabled = sprite != null;
    }

    public static void SetClick(Button button, Action onClick)
    {
        if (button == null) return;
        button.onClick.RemoveAllListeners();
        if (onClick != null) button.onClick.AddListener(() => onClick());
    }

    /// <summary>Reuses pooled rows: instantiates only when the pool is short and hides the surplus.</summary>
    public static void FillList<TRow, TData>(Transform root, TRow prefab, List<TRow> pool,
        IReadOnlyList<TData> items, Action<TRow, TData> bind) where TRow : Component
    {
        if (root == null || prefab == null) return;
        int count = items?.Count ?? 0;
        while (pool.Count < count) pool.Add(UnityEngine.Object.Instantiate(prefab, root, false));
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] == null) pool[i] = UnityEngine.Object.Instantiate(prefab, root, false);
            bool active = i < count;
            if (pool[i].gameObject.activeSelf != active) pool[i].gameObject.SetActive(active);
            if (active) bind(pool[i], items[i]);
        }
    }

    public static float CaptureScroll(ScrollRect scroll)
        => scroll != null && scroll.content != null ? scroll.content.anchoredPosition.y : 0f;

    /// <summary>Restores the pre-refresh scroll offset after layout has caught up with the new row count.</summary>
    public static void RestoreScroll(ScrollRect scroll, float offsetY)
    {
        if (scroll == null || scroll.content == null) return;
        Canvas.ForceUpdateCanvases();
        var pos = scroll.content.anchoredPosition;
        float max = Mathf.Max(0f, scroll.content.rect.height - (scroll.viewport != null ? scroll.viewport.rect.height : 0f));
        scroll.content.anchoredPosition = new Vector2(pos.x, Mathf.Clamp(offsetY, 0f, max));
    }

    public static string Signed(float value) => $"{value:+0;-0;0}";
}

/// <summary>A pooled list of PoliticalLineRowUI under one authored root.</summary>
public sealed class PoliticalLineList
{
    private readonly Transform root;
    private readonly PoliticalLineRowUI prefab;
    private readonly List<PoliticalLineRowUI> pool = new List<PoliticalLineRowUI>();

    public PoliticalLineList(Transform root, PoliticalLineRowUI prefab)
    {
        this.root = root;
        this.prefab = prefab;
    }

    public void ShowEffects(IReadOnlyList<PoliticalEffectLine> lines)
        => GovernmentUiUtil.FillList(root, prefab, pool, lines, (row, line) => row.BindEffect(line));

    public void ShowRequirements(IReadOnlyList<PoliticalRequirementLine> lines)
        => GovernmentUiUtil.FillList(root, prefab, pool, lines, (row, line) => row.BindRequirement(line));

    public void ShowTexts(IReadOnlyList<string> lines)
        => GovernmentUiUtil.FillList(root, prefab, pool, lines, (row, line) => row.BindText(line));

    public void Show<T>(IReadOnlyList<T> items, Action<PoliticalLineRowUI, T> bind)
        => GovernmentUiUtil.FillList(root, prefab, pool, items, bind);

    public void Clear() => GovernmentUiUtil.FillList(root, prefab, pool, Array.Empty<string>(), (row, line) => { });
}

/// <summary>Authored widget showing a council vote outlook (summary plus one row per councillor).</summary>
[Serializable]
public class CouncilOutlookWidget
{
    public GameObject root;
    public TMP_Text summaryText;
    public Transform voteRowsRoot;
    public CouncilVoteRowUI voteRowPrefab;

    private readonly List<CouncilVoteRowUI> pool = new List<CouncilVoteRowUI>();

    public void Show(Civilization civ, CouncilVoteResult result)
    {
        if (civ == null || !civ.HasRoyalCouncil)
        {
            Hide();
            return;
        }

        GovernmentUiUtil.SetActive(root, true);
        string institution = GovernmentPresentation.GetInstitutionName(civ);
        if (result == null || !result.applicable)
        {
            GovernmentUiUtil.SetText(summaryText, $"No {institution.ToLowerInvariant()} vote required.");
            GovernmentUiUtil.FillList(voteRowsRoot, voteRowPrefab, pool, Array.Empty<CouncilVote>(), (row, vote) => { });
            return;
        }

        GovernmentUiUtil.SetText(summaryText,
            $"{institution} outlook: {result.yesVotes} yes / {result.noVotes} no ({result.requiredYesVotes} needed) - {(result.passed ? "would pass" : "would fail")}");
        GovernmentUiUtil.FillList(voteRowsRoot, voteRowPrefab, pool, result.individualVotes, (row, vote) => row.BindIndividual(civ, vote));
    }

    public void Hide()
    {
        GovernmentUiUtil.SetActive(root, false);
        GovernmentUiUtil.FillList(voteRowsRoot, voteRowPrefab, pool, Array.Empty<CouncilVote>(), (row, vote) => { });
    }
}
