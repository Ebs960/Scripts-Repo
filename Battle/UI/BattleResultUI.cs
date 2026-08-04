using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class BattleResultUI : MonoBehaviour
{
    private BattleManager manager;
    private GameObject root;
    private TextMeshProUGUI summary;

    public static BattleResultUI GetOrCreate(BattleManager manager)
    {
        var existing = manager.GetComponent<BattleResultUI>();
        return existing != null ? existing : manager.gameObject.AddComponent<BattleResultUI>();
    }

    public void Bind(BattleManager battleManager)
    {
        manager = battleManager;
        manager.BattleResolved += Show;
        Build();
    }

    private void OnDestroy()
    {
        if (manager != null)
            manager.BattleResolved -= Show;
    }

    private void Build()
    {
        if (root != null) return;
        root = new GameObject("Battle Result", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 530;
        root.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        root.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920f, 1080f);

        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(root.transform, false);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(560f, 380f);
        panel.GetComponent<Image>().color = new Color(0.08f, 0.1f, 0.13f, 0.99f);

        summary = CreateText(panel.transform, "Summary", new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(500f, 270f), 19f);
        summary.alignment = TextAlignmentOptions.TopLeft;
        CreateButton(panel.transform, "Continue", new Vector2(0f, 26f), Continue);
        root.SetActive(false);
    }

    private void Show(BattleResult result)
    {
        // Background AI-vs-AI engagements publish campaign events but must not
        // interrupt the player with a tactical result modal.
        var preview = manager != null ? manager.PendingPreview : null;
        bool playerInvolved = result != null && result.WasPlayerInvolved || preview != null
            && ((preview.Attacker != null && preview.Attacker.owner != null && preview.Attacker.owner.isPlayerControlled)
                || (preview.Defender != null && preview.Defender.owner != null && preview.Defender.owner.isPlayerControlled));
        if (!playerInvolved)
            return;

        Build();
        int attackerLosses = 0;
        int defenderLosses = 0;
        int attackerRetreats = 0;
        int defenderRetreats = 0;
        int survivors = 0;
        for (int i = 0; i < result.UnitOutcomes.Count; i++)
        {
            var outcome = result.UnitOutcomes[i];
            if (outcome.Died)
            {
                if (outcome.Side == BattleSide.Attacker) attackerLosses++; else defenderLosses++;
            }
            else survivors++;
            if (outcome.Retreated)
            {
                if (outcome.Side == BattleSide.Attacker) attackerRetreats++; else defenderRetreats++;
            }
        }

        summary.text = $"Battle Result\n\nWinner: {result.WinningSide}\nResolution: {result.ResolutionType}\nFinal round: {result.FinalRound}\n\n" +
            $"Attacker losses: {attackerLosses}\nDefender losses: {defenderLosses}\n" +
            $"Attacker retreats: {attackerRetreats}\nDefender retreats: {defenderRetreats}\n" +
            $"Survivors: {survivors}\nAuto-resolved: {result.WasAutoResolved}";
        root.SetActive(true);
    }

    public void PresentRestored(BattleResult result) => Show(result);

    private void Continue()
    {
        manager?.ContinueAfterBattleResult();
        if (root != null) root.SetActive(false);
    }

    private static TextMeshProUGUI CreateText(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, float fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var text = go.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = fontSize;
        text.color = Color.white;
        return text;
    }

    private static void CreateButton(Transform parent, string label, Vector2 position, UnityEngine.Events.UnityAction action)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(180f, 42f);
        go.GetComponent<Image>().color = new Color(0.72f, 0.67f, 0.52f, 1f);
        go.GetComponent<Button>().onClick.AddListener(action);
        var text = CreateText(go.transform, "Label", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(170f, 34f), 15f);
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.black;
        text.text = label;
    }
}
