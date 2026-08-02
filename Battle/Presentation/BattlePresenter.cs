using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Runtime tactical board. Campaign objects are never moved by this view.</summary>
public sealed class BattlePresenter : MonoBehaviour
{
    private BattleManager manager;
    private GameObject root;
    private TextMeshProUGUI summary;
    private RectTransform board;
    private readonly List<Button> cellButtons = new();
    private readonly HashSet<int> moveOverlay = new();
    private readonly HashSet<int> attackOverlay = new();
    private int selectedCell = -1;
    private int renderedCellCount = -1;
    private BattleDomain? visibleDomain;
    private float zoom = 1f;

    public event Action<int> CellClicked;
    public string VisibleLayerName => visibleDomain?.ToString() ?? "All";

    public void CycleLayer()
    {
        visibleDomain = visibleDomain switch
        {
            null => BattleDomain.Land,
            BattleDomain.Land => BattleDomain.NavalSurface,
            BattleDomain.NavalSurface => BattleDomain.Underwater,
            BattleDomain.Underwater => BattleDomain.Air,
            BattleDomain.Air => BattleDomain.Orbit,
            BattleDomain.Orbit => BattleDomain.Space,
            _ => null,
        };
        RefreshCells();
    }

    public void AdjustZoom(float delta)
    {
        zoom = Mathf.Clamp(zoom + delta, .65f, 1.6f);
        if (board != null) board.localScale = Vector3.one * zoom;
    }

    public BattleUnitState GetDisplayedUnitAtCell(int cell)
    {
        var unit = manager != null ? manager.GetUnitAtCell(cell) : null;
        return unit != null && (!visibleDomain.HasValue || unit.Domain == visibleDomain.Value) ? unit : null;
    }

    public static BattlePresenter GetOrCreate(BattleManager manager)
    {
        var existing = manager.GetComponent<BattlePresenter>();
        return existing != null ? existing : manager.gameObject.AddComponent<BattlePresenter>();
    }

    public void Bind(BattleManager battleManager)
    {
        if (manager == battleManager) return;
        manager = battleManager;
        manager.BattleStarted += Present;
        manager.BattleStateChanged += Present;
        manager.BattlePreviewClosed += Hide;
        Build();
    }

    private void OnDestroy()
    {
        if (manager == null) return;
        manager.BattleStarted -= Present;
        manager.BattleStateChanged -= Present;
        manager.BattlePreviewClosed -= Hide;
    }

    public void SetOverlays(int selected, IEnumerable<int> moves, IEnumerable<int> attacks)
    {
        selectedCell = selected;
        moveOverlay.Clear();
        attackOverlay.Clear();
        if (moves != null) foreach (int cell in moves) moveOverlay.Add(cell);
        if (attacks != null) foreach (int cell in attacks) attackOverlay.Add(cell);
        RefreshCells();
    }

    public void Present(BattleSession session)
    {
        Build();
        if (session == null) { Hide(); return; }
        EnsureBoard(session);
        RefreshCells();

        int aliveA = 0, aliveD = 0;
        for (int i = 0; i < session.Units.Count; i++)
        {
            var unit = session.Units[i];
            if (unit == null || !unit.IsAliveAndActive) continue;
            if (unit.Side == BattleSide.Attacker) aliveA++; else aliveD++;
        }
        summary.text = $"{session.Theater}  Round {session.CurrentRound}  {session.ActiveSide}\n" +
            $"Attackers {aliveA}  Defenders {aliveD}  Objective C{session.Objective.CellIndex}";
        root.SetActive(true);
    }

    private void EnsureBoard(BattleSession session)
    {
        if (renderedCellCount == session.Map.CellCount) return;
        for (int i = board.childCount - 1; i >= 0; i--) Destroy(board.GetChild(i).gameObject);
        cellButtons.Clear();
        renderedCellCount = session.Map.CellCount;
        var grid = board.GetComponent<GridLayoutGroup>();
        int columns = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(renderedCellCount)), 4, 12);
        float width = board.rect.width > 0 ? board.rect.width : 900f;
        grid.cellSize = new Vector2(Mathf.Clamp((width - columns * 5f) / columns, 54f, 92f), 58f);
        grid.constraintCount = columns;

        for (int i = 0; i < renderedCellCount; i++)
        {
            int cellIndex = i;
            var go = new GameObject($"Cell {i}", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(board, false);
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(() => CellClicked?.Invoke(cellIndex));
            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(go.transform, false);
            var rect = labelGo.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var label = labelGo.GetComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset; label.fontSize = 11f; label.alignment = TextAlignmentOptions.Center;
            cellButtons.Add(button);
        }
    }

    private void RefreshCells()
    {
        var session = manager != null ? manager.ActiveBattle : null;
        if (session == null || cellButtons.Count != session.Map.CellCount) return;
        for (int i = 0; i < cellButtons.Count; i++)
        {
            var cell = session.Map.GetCell(i);
            var unit = manager.GetUnitAtCell(i);
            var label = cellButtons[i].GetComponentInChildren<TextMeshProUGUI>();
            var text = new StringBuilder();
            text.Append('C').Append(i);
            if (cell.IsObjective) text.Append(" ★");
            if (cell.HasPort) text.Append(" ⚓"); else if (cell.HasBeach) text.Append(" ▱");
            if (unit != null) text.Append('\n').Append(unit.Side == BattleSide.Attacker ? "A" : "D").Append('#').Append(unit.UnitId).Append(" ").Append(unit.CurrentHealth);
            label.text = text.ToString();
            label.color = Color.white;
            Color color = cell.IsWater ? new Color(.10f, .28f, .42f, .95f) : new Color(.20f, .30f, .17f, .95f);
            if (cell.DeploymentOwner.HasValue) color = cell.DeploymentOwner == BattleSide.Attacker ? new Color(.18f, .30f, .55f, .95f) : new Color(.50f, .22f, .18f, .95f);
            if (moveOverlay.Contains(i)) color = new Color(.12f, .65f, .75f, .98f);
            if (attackOverlay.Contains(i)) color = new Color(.82f, .18f, .12f, .98f);
            if (selectedCell == i) color = new Color(.95f, .78f, .16f, 1f);
            cellButtons[i].GetComponent<Image>().color = color;
            cellButtons[i].interactable = true;
        }
    }

    private void Hide() { if (root != null) root.SetActive(false); }

    private void Build()
    {
        if (root != null) return;
        root = new GameObject("Battle Tactical Board", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 505;
        var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920f, 1080f);
        var panel = new GameObject("Board Panel", typeof(RectTransform), typeof(Image)); panel.transform.SetParent(root.transform, false);
        var panelRect = panel.GetComponent<RectTransform>(); panelRect.anchorMin = new Vector2(.22f, .08f); panelRect.anchorMax = new Vector2(.98f, .92f); panelRect.offsetMin = panelRect.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(.035f, .045f, .06f, .96f);
        var summaryGo = new GameObject("Summary", typeof(RectTransform), typeof(TextMeshProUGUI)); summaryGo.transform.SetParent(panel.transform, false);
        var summaryRect = summaryGo.GetComponent<RectTransform>(); summaryRect.anchorMin = new Vector2(0, 1); summaryRect.anchorMax = Vector2.one; summaryRect.pivot = new Vector2(.5f, 1); summaryRect.sizeDelta = new Vector2(0, 52); summaryRect.anchoredPosition = Vector2.zero;
        summary = summaryGo.GetComponent<TextMeshProUGUI>(); summary.font = TMP_Settings.defaultFontAsset; summary.fontSize = 16; summary.color = Color.white; summary.alignment = TextAlignmentOptions.Center;
        var boardGo = new GameObject("Cells", typeof(RectTransform), typeof(GridLayoutGroup)); boardGo.transform.SetParent(panel.transform, false);
        board = boardGo.GetComponent<RectTransform>(); board.anchorMin = Vector2.zero; board.anchorMax = Vector2.one; board.offsetMin = new Vector2(18, 18); board.offsetMax = new Vector2(-18, -58);
        var grid = boardGo.GetComponent<GridLayoutGroup>(); grid.spacing = new Vector2(5, 5); grid.childAlignment = TextAnchor.MiddleCenter; grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 8;
        root.SetActive(false);
    }
}
