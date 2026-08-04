using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public sealed class BattlePreviewUI : MonoBehaviour
{
    private BattleManager manager;
    private GameObject root;
    private TextMeshProUGUI summary;
    private Button manualButton;
    private Button autoResolveButton;
    private Button retreatButton;
    private Button cancelButton;
    private TMP_InputField governorIdInput;
    private TextMeshProUGUI commanderSelection;
    private readonly List<(BattleSide side, CommanderCharacterKind kind, int id, string name)> commanderChoices = new();
    private int commanderChoiceIndex;
    private int commandRoleIndex;

    public static BattlePreviewUI GetOrCreate(BattleManager manager)
    {
        var existing = manager.GetComponent<BattlePreviewUI>();
        return existing != null ? existing : manager.gameObject.AddComponent<BattlePreviewUI>();
    }

    public void Bind(BattleManager battleManager)
    {
        if (manager == battleManager)
            return;

        if (manager != null)
        {
            manager.BattlePreviewOpened -= Show;
            manager.BattlePreviewClosed -= Hide;
        }

        manager = battleManager;
        manager.BattlePreviewOpened += Show;
        manager.BattlePreviewClosed += Hide;
        Build();
    }

    private void OnDestroy()
    {
        if (manager != null)
        {
            manager.BattlePreviewOpened -= Show;
            manager.BattlePreviewClosed -= Hide;
        }
    }

    private void Build()
    {
        if (root != null)
            return;

        root = new GameObject("Battle Preview", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        root.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        root.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920f, 1080f);

        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(root.transform, false);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(620f, 440f);
        panel.GetComponent<Image>().color = new Color(0.08f, 0.1f, 0.13f, 0.98f);

        summary = CreateText(panel.transform, "Summary", new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(560f, 280f), 20f);
        summary.alignment = TextAlignmentOptions.TopLeft;

        manualButton = CreateButton(panel.transform, "Manual Battle", new Vector2(-195f, 34f), BeginManual);
        autoResolveButton = CreateButton(panel.transform, "Auto-Resolve", new Vector2(-65f, 34f), AutoResolve);
        retreatButton = CreateButton(panel.transform, "Retreat", new Vector2(65f, 34f), Retreat);
        cancelButton = CreateButton(panel.transform, "Cancel", new Vector2(195f, 34f), Cancel);
        governorIdInput = CreateInput(panel.transform, "Governor ID", new Vector2(0f, -30f));
        CreateButton(panel.transform, "Assign Attacker Governor", new Vector2(-130f, -86f), AssignAttackerGovernor, 180f);
        CreateButton(panel.transform, "Assign Defender Governor", new Vector2(130f, -86f), AssignDefenderGovernor, 180f);
        commanderSelection = CreateText(panel.transform, "Commander Selection", new Vector2(.5f, 0f), new Vector2(0f, 118f), new Vector2(560f, 54f), 14f);
        commanderSelection.alignment = TextAlignmentOptions.Center;
        CreateButton(panel.transform, "Previous Commander", new Vector2(-180f, 78f), PreviousCommander, 150f);
        CreateButton(panel.transform, "Next Commander", new Vector2(-20f, 78f), NextCommander, 150f);
        CreateButton(panel.transform, "Change Role", new Vector2(140f, 78f), NextRole, 120f);
        CreateButton(panel.transform, "Assign", new Vector2(270f, 78f), AssignSelectedCommander, 90f);
        root.SetActive(false);
    }

    private void Show(EngagementPreview preview)
    {
        Build();
        if (preview == null)
            return;

        summary.text = $"{preview.Theater}\n\n{preview.Attacker?.UnitName ?? "Attacker"} vs {preview.Defender?.UnitName ?? "Defender"}\n" +
            $"Attacker units: {preview.AttackerUnits.Count}\nDefender units: {preview.DefenderUnits.Count}\n" +
            $"Reinforcement formations: {preview.Reinforcements.Count}\nEnvironment: {preview.PlanetaryEnvironment}\n" +
            $"Objective: {preview.Objective.Type} at cell {preview.Objective.CellIndex}";
        manualButton.interactable = preview.AllowsManualBattle;
        autoResolveButton.interactable = true;
        retreatButton.interactable = preview.AllowsRetreat && preview.Theater != BattleTheater.DeepSpace;
        cancelButton.interactable = preview.AllowsCancel;
        PopulateCommanderChoices(preview);
        root.SetActive(true);
    }

    public void PresentRestored(EngagementPreview preview) => Show(preview);

    private void PopulateCommanderChoices(EngagementPreview preview)
    {
        commanderChoices.Clear();
        AddCommanderChoices(preview.Attacker?.owner, BattleSide.Attacker);
        AddCommanderChoices(preview.Defender?.owner, BattleSide.Defender);
        commanderChoiceIndex = Mathf.Clamp(commanderChoiceIndex, 0, Mathf.Max(0, commanderChoices.Count - 1));
        RefreshCommanderSelection();
    }

    private void AddCommanderChoices(Civilization owner, BattleSide side)
    {
        if (owner == null || !owner.isPlayerControlled) return;
        for (int i = 0; i < owner.governors.Count; i++)
            if (owner.governors[i] != null)
                commanderChoices.Add((side, CommanderCharacterKind.Governor, owner.governors[i].Id, owner.governors[i].Name));
        int ownerId = CivilizationManager.Instance != null ? CivilizationManager.Instance.GetCivIndex(owner) : -1;
        if (AdmiralManager.Instance == null) return;
        for (int i = 0; i < AdmiralManager.Instance.admirals.Count; i++)
        {
            var admiral = AdmiralManager.Instance.admirals[i];
            if (admiral != null && admiral.ownerCivilizationId == ownerId && admiral.status == AdmiralStatus.Active)
                commanderChoices.Add((side, CommanderCharacterKind.Admiral, admiral.admiralId, admiral.admiralName));
        }
    }

    private void PreviousCommander() { if (commanderChoices.Count > 0) commanderChoiceIndex = (commanderChoiceIndex - 1 + commanderChoices.Count) % commanderChoices.Count; RefreshCommanderSelection(); }
    private void NextCommander() { if (commanderChoices.Count > 0) commanderChoiceIndex = (commanderChoiceIndex + 1) % commanderChoices.Count; RefreshCommanderSelection(); }
    private void NextRole() { commandRoleIndex = (commandRoleIndex + 1) % System.Enum.GetValues(typeof(CommandRole)).Length; RefreshCommanderSelection(); }
    private void RefreshCommanderSelection()
    {
        if (commanderSelection == null) return;
        if (commanderChoices.Count == 0) { commanderSelection.text = "No eligible player commanders"; return; }
        var choice = commanderChoices[commanderChoiceIndex];
        commanderSelection.text = $"{choice.name} ({choice.kind}, {choice.side}) — {(CommandRole)commandRoleIndex}";
    }

    private void AssignSelectedCommander()
    {
        if (manager == null || commanderChoices.Count == 0) return;
        var choice = commanderChoices[commanderChoiceIndex];
        var role = (CommandRole)commandRoleIndex;
        string reason;
        bool ok = choice.kind == CommanderCharacterKind.Governor
            ? manager.TryAssignGovernorCommander(choice.side, choice.id, role, out reason)
            : manager.TryAssignAdmiralCommander(choice.side, choice.id, role, out reason);
        if (!ok) UIManager.Instance?.ShowNotification(reason);
        else UIManager.Instance?.ShowNotification($"Assigned {choice.name} as {role}.");
    }

    private void Hide()
    {
        if (root != null)
            root.SetActive(false);
    }

    private void BeginManual()
    {
        if (manager != null)
            manager.BeginPendingManualBattle();
    }

    private void AutoResolve()
    {
        if (manager != null)
            manager.AutoResolvePendingPreview(out _);
    }

    private void Cancel()
    {
        manager?.CancelPreview();
    }

    private void AssignAttackerGovernor() => AssignGovernor(BattleSide.Attacker);
    private void AssignDefenderGovernor() => AssignGovernor(BattleSide.Defender);
    private void AssignGovernor(BattleSide side)
    {
        if (!int.TryParse(governorIdInput.text, out int governorId))
        {
            UIManager.Instance?.ShowNotification("Enter a governor ID.");
            return;
        }
        if (!manager.TryAssignGovernorCommander(side, governorId, out string reason))
            UIManager.Instance?.ShowNotification(reason);
    }

    private void Retreat()
    {
        if (manager != null && !manager.RetreatPendingPreview(out string reason))
            UIManager.Instance?.ShowNotification(reason);
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

    private static Button CreateButton(Transform parent, string label, Vector2 position, UnityEngine.Events.UnityAction action)
    {
        return CreateButton(parent, label, position, action, 120f);
    }

    private static Button CreateButton(Transform parent, string label, Vector2 position, UnityEngine.Events.UnityAction action, float width)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(width, 42f);
        go.GetComponent<Image>().color = new Color(0.72f, 0.67f, 0.52f, 1f);
        var button = go.GetComponent<Button>();
        if (action != null)
            button.onClick.AddListener(action);
        var text = CreateText(go.transform, "Label", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width - 8f, 36f), 15f);
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.black;
        text.text = label;
        return button;
    }

    private static TMP_InputField CreateInput(Transform parent, string name, Vector2 position)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(220f, 36f);
        go.GetComponent<Image>().color = new Color(0.2f, 0.25f, 0.3f, 1f);
        var text = CreateText(go.transform, "Text", new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(200f, 30f), 14f);
        text.alignment = TextAlignmentOptions.Left;
        text.text = "Governor ID";
        var input = go.GetComponent<TMP_InputField>();
        input.textComponent = text;
        input.placeholder = text;
        input.contentType = TMP_InputField.ContentType.IntegerNumber;
        return input;
    }
}
