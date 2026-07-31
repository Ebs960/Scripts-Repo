using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class BattlePresenter : MonoBehaviour
{
    private BattleManager manager;
    private GameObject root;
    private TextMeshProUGUI summary;

    public static BattlePresenter GetOrCreate(BattleManager manager)
    {
        var existing = manager.GetComponent<BattlePresenter>();
        return existing != null ? existing : manager.gameObject.AddComponent<BattlePresenter>();
    }

    public void Bind(BattleManager battleManager)
    {
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

    public void Present(BattleSession session)
    {
        Build();
        if (session == null) { Hide(); return; }

        var text = new StringBuilder();
        text.AppendLine("Tactical Units");
        text.AppendLine($"{session.Theater} | Round {session.CurrentRound} | {session.ActiveSide}");
        for (int i = 0; i < session.Units.Count; i++)
        {
            var unit = session.Units[i];
            if (unit == null) continue;
            text.Append(unit.Side == BattleSide.Attacker ? "A " : "D ");
            text.Append($"#{unit.UnitId} {unit.Domain} C{unit.CellIndex} HP {unit.CurrentHealth}");
            if (unit.IsReserve) text.Append(" Reserve");
            if (unit.HasRetreated) text.Append(" Retreated");
            if (unit.IsDead) text.Append(" Destroyed");
            if (unit.IsWaiting) text.Append(" Waiting");
            text.AppendLine();
        }

        summary.text = text.ToString();
        root.SetActive(true);
    }

    private void Hide() { if (root != null) root.SetActive(false); }

    private void Build()
    {
        if (root != null) return;
        root = new GameObject("Battle Tactical View", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 505;
        root.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        root.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920f, 1080f);

        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(root.transform, false);
        var rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.sizeDelta = new Vector2(360f, 520f);
        panel.GetComponent<Image>().color = new Color(0.06f, 0.08f, 0.1f, 0.92f);

        var textGo = new GameObject("Snapshot", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(panel.transform, false);
        var textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0f, 0f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.offsetMin = new Vector2(14f, 14f);
        textRect.offsetMax = new Vector2(-14f, -14f);
        summary = textGo.GetComponent<TextMeshProUGUI>();
        summary.font = TMP_Settings.defaultFontAsset;
        summary.fontSize = 14f;
        summary.color = Color.white;
        summary.alignment = TextAlignmentOptions.TopLeft;
        root.SetActive(false);
    }
}
