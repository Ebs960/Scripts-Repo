using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Runtime-wired campaign army roster and member controls.</summary>
public sealed class CampaignArmyPanel : MonoBehaviour
{
    private static readonly Color PanelColor = new(0.055f, 0.065f, 0.075f, 0.97f);
    private static readonly Color HeaderColor = new(0.12f, 0.14f, 0.15f, 1f);
    private static readonly Color RowColor = new(0.09f, 0.105f, 0.115f, 1f);
    private static readonly Color SelectedRowColor = new(0.14f, 0.28f, 0.34f, 1f);
    private static readonly Color AccentColor = new(0.16f, 0.66f, 0.55f, 1f);
    private static readonly Color FullColor = new(0.84f, 0.34f, 0.18f, 1f);

    private GameObject root;
    private TextMeshProUGUI title;
    private TextMeshProUGUI summary;
    private Image capacityFill;
    private TextMeshProUGUI capacityText;
    private TMP_InputField renameInput;
    private RectTransform memberContent;
    private readonly List<GameObject> rows = new();
    private CombatUnit selectedUnit;

    private void OnDisable()
    {
        Hide();
    }

    private void OnDestroy()
    {
        if (root != null)
            Destroy(root);
    }

    public static CampaignArmyPanel GetOrCreate(Component host)
    {
        if (host == null)
            return null;
        var existing = host.GetComponent<CampaignArmyPanel>();
        return existing != null ? existing : host.gameObject.AddComponent<CampaignArmyPanel>();
    }

    public void Show(CombatUnit unit)
    {
        Build();
        selectedUnit = unit;
        if (root == null)
            return;
        root.SetActive(unit != null);
        if (unit != null)
            Refresh();
    }

    public void Hide()
    {
        selectedUnit = null;
        if (root != null)
            root.SetActive(false);
    }

    public void Refresh()
    {
        if (selectedUnit == null || root == null)
        {
            Hide();
            return;
        }

        var members = CampaignArmyService.GetMembers(selectedUnit);
        var representative = CampaignArmyService.GetRepresentative(selectedUnit);
        int capacity = selectedUnit.owner != null ? selectedUnit.owner.GetMaxArmySize() : members.Count;
        string armyName = string.IsNullOrWhiteSpace(representative?.MilitaryFormationName)
            ? representative?.MilitaryFormationType.ToString() ?? "Army"
            : representative.MilitaryFormationName;

        title.text = armyName;
        summary.text = $"Tile {representative.currentTileIndex}  |  {representative.currentLayer}";
        capacityText.text = $"{members.Count} / {capacity}";
        capacityFill.fillAmount = capacity > 0 ? Mathf.Clamp01(members.Count / (float)capacity) : 0f;
        capacityFill.color = members.Count >= capacity ? FullColor : AccentColor;
        renameInput.SetTextWithoutNotify(armyName);
        RebuildRows(members, representative);
    }

    private void Build()
    {
        if (root != null)
            return;

        Canvas parentCanvas = GetComponentInParent<Canvas>();
        Transform parent;
        if (parentCanvas != null)
        {
            parent = parentCanvas.transform;
        }
        else
        {
            var canvasObject = new GameObject("Campaign Army Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 420;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            parent = canvasObject.transform;
        }

        root = new GameObject("Campaign Army Panel", typeof(RectTransform), typeof(Image));
        root.transform.SetParent(parent, false);
        var rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = rootRect.anchorMax = new Vector2(1f, 0.5f);
        rootRect.pivot = new Vector2(1f, 0.5f);
        rootRect.anchoredPosition = new Vector2(-430f, 0f);
        rootRect.sizeDelta = new Vector2(390f, 560f);
        root.GetComponent<Image>().color = PanelColor;

        var header = CreateBand(root.transform, "Header", new Vector2(0f, 0.78f), Vector2.one, HeaderColor);
        title = CreateText(header.transform, "Army Name", 22f, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        SetRect(title.rectTransform, new Vector2(0f, 0.5f), Vector2.one, new Vector2(18f, 8f), new Vector2(-18f, -10f));
        summary = CreateText(header.transform, "Location", 13f, FontStyles.Normal, TextAlignmentOptions.BottomLeft);
        summary.color = new Color(0.72f, 0.76f, 0.76f, 1f);
        SetRect(summary.rectTransform, Vector2.zero, new Vector2(1f, 0.48f), new Vector2(18f, 10f), new Vector2(-18f, 0f));

        var capacityTrack = CreateBand(root.transform, "Capacity", new Vector2(0.05f, 0.70f), new Vector2(0.95f, 0.745f),
            new Color(0.18f, 0.2f, 0.21f, 1f));
        capacityFill = CreateBand(capacityTrack.transform, "Fill", Vector2.zero, Vector2.one, AccentColor).GetComponent<Image>();
        capacityFill.type = Image.Type.Filled;
        capacityFill.fillMethod = Image.FillMethod.Horizontal;
        capacityText = CreateText(capacityTrack.transform, "Capacity Text", 14f, FontStyles.Bold, TextAlignmentOptions.Center);
        Stretch(capacityText.rectTransform, Vector2.zero, Vector2.zero);

        renameInput = CreateInput(root.transform);
        var renameButton = CreateButton(root.transform, "Rename", new Vector2(0.72f, 0.615f), new Vector2(0.95f, 0.68f), RenameArmy);
        renameButton.GetComponentInChildren<TextMeshProUGUI>().fontSize = 13f;

        var viewportObject = new GameObject("Members", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
        viewportObject.transform.SetParent(root.transform, false);
        var viewportRect = viewportObject.GetComponent<RectTransform>();
        SetRect(viewportRect, new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.59f), Vector2.zero, Vector2.zero);
        viewportObject.GetComponent<Image>().color = new Color(0.035f, 0.04f, 0.045f, 1f);
        viewportObject.GetComponent<Mask>().showMaskGraphic = true;

        var contentObject = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentObject.transform.SetParent(viewportObject.transform, false);
        memberContent = contentObject.GetComponent<RectTransform>();
        memberContent.anchorMin = new Vector2(0f, 1f);
        memberContent.anchorMax = Vector2.one;
        memberContent.pivot = new Vector2(0.5f, 1f);
        memberContent.sizeDelta = Vector2.zero;
        var layout = contentObject.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(6, 6, 6, 6);
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        contentObject.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = viewportObject.GetComponent<ScrollRect>();
        scroll.viewport = viewportRect;
        scroll.content = memberContent;
        scroll.horizontal = false;
        scroll.vertical = true;
        root.SetActive(false);
    }

    private void RebuildRows(List<CombatUnit> members, CombatUnit representative)
    {
        for (int i = 0; i < rows.Count; i++)
            if (rows[i] != null) Destroy(rows[i]);
        rows.Clear();

        for (int i = 0; i < members.Count; i++)
        {
            var member = members[i];
            if (member == null) continue;
            var row = new GameObject($"Member {i + 1}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(memberContent, false);
            row.GetComponent<Image>().color = member == selectedUnit ? SelectedRowColor : RowColor;
            row.GetComponent<LayoutElement>().preferredHeight = 64f;
            rows.Add(row);

            var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            icon.transform.SetParent(row.transform, false);
            SetRect(icon.rectTransform, new Vector2(0f, 0.14f), new Vector2(0.14f, 0.86f), new Vector2(8f, 0f), Vector2.zero);
            icon.sprite = member.data != null ? member.data.GetIcon(member.owner) : null;
            icon.enabled = icon.sprite != null;
            icon.preserveAspect = true;

            string marker = member == representative ? "LEAD  " : string.Empty;
            var name = CreateText(row.transform, "Name", 14f, FontStyles.Bold, TextAlignmentOptions.TopLeft);
            name.text = marker + (member.data != null ? member.data.unitName : member.name);
            SetRect(name.rectTransform, new Vector2(0.16f, 0.48f), new Vector2(0.58f, 0.92f), Vector2.zero, Vector2.zero);
            var stats = CreateText(row.transform, "Stats", 11f, FontStyles.Normal, TextAlignmentOptions.BottomLeft);
            stats.color = new Color(0.72f, 0.76f, 0.76f, 1f);
            stats.text = $"HP {member.currentHealth}/{member.MaxHealth}  MP {member.currentMovePoints}";
            SetRect(stats.rectTransform, new Vector2(0.16f, 0.08f), new Vector2(0.58f, 0.5f), Vector2.zero, Vector2.zero);

            CombatUnit capturedMember = member;
            CreateButton(row.transform, "Select", new Vector2(0.60f, 0.18f), new Vector2(0.73f, 0.82f),
                () => UnitSelectionManager.Instance?.SelectUnit(capturedMember));
            var leadButton = CreateButton(row.transform, "Lead", new Vector2(0.745f, 0.18f), new Vector2(0.86f, 0.82f),
                () => SetLead(capturedMember));
            leadButton.interactable = member != representative;
            var splitButton = CreateButton(row.transform, "Split", new Vector2(0.875f, 0.18f), new Vector2(0.985f, 0.82f),
                () => Split(capturedMember));
            splitButton.interactable = member != representative && member.currentMovePoints > 0;
        }
    }

    private void SetLead(CombatUnit member)
    {
        if (!CampaignArmyService.SetRepresentative(member))
        {
            UIManager.Instance?.ShowNotification("Cannot change the army representative here.");
            return;
        }
        UnitSelectionManager.Instance?.SelectUnit(member);
        UIManager.Instance?.ShowNotification($"{member.UnitName} now represents the army.");
    }

    private void Split(CombatUnit member)
    {
        if (!member.Unstack())
        {
            UIManager.Instance?.ShowNotification("Cannot split this member: an adjacent legal tile is required.");
            return;
        }
        UnitSelectionManager.Instance?.SelectUnit(member);
        UIManager.Instance?.ShowNotification($"{member.UnitName} formed a new army.");
    }

    private void RenameArmy()
    {
        if (selectedUnit == null) return;
        CampaignArmyService.RenameArmy(selectedUnit, renameInput.text);
        Refresh();
    }

    private TMP_InputField CreateInput(Transform parent)
    {
        var inputObject = new GameObject("Rename Input", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        inputObject.transform.SetParent(parent, false);
        SetRect(inputObject.GetComponent<RectTransform>(), new Vector2(0.05f, 0.615f), new Vector2(0.69f, 0.68f), Vector2.zero, Vector2.zero);
        inputObject.GetComponent<Image>().color = new Color(0.15f, 0.17f, 0.18f, 1f);
        var text = CreateText(inputObject.transform, "Text", 14f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        SetRect(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-8f, 0f));
        var input = inputObject.GetComponent<TMP_InputField>();
        input.textComponent = text;
        input.characterLimit = 32;
        input.onSubmit.AddListener(_ => RenameArmy());
        return input;
    }

    private static GameObject CreateBand(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
    {
        var band = new GameObject(name, typeof(RectTransform), typeof(Image));
        band.transform.SetParent(parent, false);
        SetRect(band.GetComponent<RectTransform>(), anchorMin, anchorMax, Vector2.zero, Vector2.zero);
        band.GetComponent<Image>().color = color;
        return band;
    }

    private static Button CreateButton(Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax,
        UnityEngine.Events.UnityAction action)
    {
        var buttonObject = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        SetRect(buttonObject.GetComponent<RectTransform>(), anchorMin, anchorMax, Vector2.zero, Vector2.zero);
        buttonObject.GetComponent<Image>().color = new Color(0.18f, 0.22f, 0.23f, 1f);
        var button = buttonObject.GetComponent<Button>();
        button.onClick.AddListener(action);
        var text = CreateText(buttonObject.transform, "Label", 11f, FontStyles.Bold, TextAlignmentOptions.Center);
        text.text = label;
        Stretch(text.rectTransform, Vector2.zero, Vector2.zero);
        return button;
    }

    private static TextMeshProUGUI CreateText(Transform parent, string name, float size, FontStyles style,
        TextAlignmentOptions alignment)
    {
        var textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        SetRect(rect, Vector2.zero, Vector2.one, offsetMin, offsetMax);
    }

    private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }
}