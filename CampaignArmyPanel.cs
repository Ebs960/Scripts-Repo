using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Total-War-style campaign army HUD.
///
/// Selecting any CombatUnit opens one persistent horizontal army bar at the
/// bottom of the campaign map. Every member of the selected formation is shown
/// as a card in stack order. Hovering a card shows a compact stat popup; clicking
/// it selects that member. Left/right controls change stack order.
///
/// The panel is runtime-wired so the existing UnitInfoPanel / UnitSelectionManager
/// integration can keep calling Show(), Hide(), and Refresh() without prefab
/// migration. Visuals are intentionally plain placeholders until authored UI
/// sprites are assigned.
/// </summary>
public sealed class CampaignArmyPanel : MonoBehaviour
{
    private static readonly Color PanelColor = new(0.055f, 0.065f, 0.075f, 0.98f);
    private static readonly Color HeaderColor = new(0.115f, 0.125f, 0.135f, 1f);
    private static readonly Color CardColor = new(0.09f, 0.10f, 0.11f, 1f);
    private static readonly Color SelectedCardColor = new(0.19f, 0.25f, 0.28f, 1f);
    private static readonly Color FrontCardColor = new(0.19f, 0.16f, 0.08f, 1f);
    private static readonly Color AccentColor = new(0.73f, 0.60f, 0.28f, 1f);
    private static readonly Color HealthyColor = new(0.32f, 0.62f, 0.34f, 1f);
    private static readonly Color WoundedColor = new(0.72f, 0.34f, 0.22f, 1f);
    private static readonly Color MutedText = new(0.72f, 0.75f, 0.76f, 1f);

    private GameObject root;
    private RectTransform rootRect;
    private TextMeshProUGUI title;
    private TextMeshProUGUI summary;
    private TextMeshProUGUI capacityText;
    private TMP_InputField renameInput;
    private RectTransform memberContent;
    private ScrollRect memberScroll;

    private GameObject tooltipRoot;
    private RectTransform tooltipRect;
    private TextMeshProUGUI tooltipTitle;
    private TextMeshProUGUI tooltipBody;

    private Button setLeadButton;
    private Button splitButton;

    private readonly List<GameObject> cards = new();
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
        HideTooltip();

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
        if (representative == null)
        {
            Hide();
            return;
        }

        int capacity = selectedUnit.owner != null
            ? selectedUnit.owner.GetMaxArmySize()
            : Mathf.Max(1, members.Count);

        string armyName = string.IsNullOrWhiteSpace(representative.MilitaryFormationName)
            ? representative.MilitaryFormationType.ToString()
            : representative.MilitaryFormationName;

        title.text = armyName;
        summary.text =
            $"{representative.MilitaryFormationType}   •   Tile {representative.currentTileIndex}   •   {representative.currentLayer}";
        capacityText.text = $"{members.Count}/{capacity} units";
        renameInput.SetTextWithoutNotify(armyName);

        RebuildCards(members, representative);
        RefreshActionButtons(representative);
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
            var canvasObject = new GameObject(
                "Campaign Army Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 420;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            parent = canvasObject.transform;
        }

        root = new GameObject("Campaign Army HUD", typeof(RectTransform), typeof(Image));
        root.transform.SetParent(parent, false);

        rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0f);
        rootRect.pivot = new Vector2(0.5f, 0f);
        rootRect.anchoredPosition = new Vector2(0f, 18f);
        rootRect.sizeDelta = new Vector2(1540f, 255f);

        var rootImage = root.GetComponent<Image>();
        rootImage.color = PanelColor;
        rootImage.raycastTarget = true;

        BuildHeader();
        BuildIdentityBlock();
        BuildMemberStrip();
        BuildActionBlock();
        BuildTooltip();

        root.SetActive(false);
    }

    private void BuildHeader()
    {
        var header = CreateBand(
            root.transform,
            "Army Header",
            new Vector2(0f, 0.82f),
            Vector2.one,
            HeaderColor);

        title = CreateText(
            header.transform,
            "Army Name",
            22f,
            FontStyles.Bold,
            TextAlignmentOptions.Center);

        SetRect(
            title.rectTransform,
            new Vector2(0.20f, 0.20f),
            new Vector2(0.80f, 0.95f),
            Vector2.zero,
            Vector2.zero);

        summary = CreateText(
            header.transform,
            "Army Summary",
            12.5f,
            FontStyles.Normal,
            TextAlignmentOptions.Center);

        summary.color = MutedText;

        SetRect(
            summary.rectTransform,
            new Vector2(0.20f, 0.02f),
            new Vector2(0.80f, 0.42f),
            Vector2.zero,
            Vector2.zero);

        capacityText = CreateText(
            header.transform,
            "Capacity",
            14f,
            FontStyles.Bold,
            TextAlignmentOptions.MidlineRight);

        capacityText.color = AccentColor;

        SetRect(
            capacityText.rectTransform,
            new Vector2(0.82f, 0.15f),
            new Vector2(0.98f, 0.90f),
            Vector2.zero,
            Vector2.zero);
    }

    private void BuildIdentityBlock()
    {
        var block = CreateBand(
            root.transform,
            "Identity",
            new Vector2(0.012f, 0.055f),
            new Vector2(0.175f, 0.79f),
            new Color(0.07f, 0.08f, 0.09f, 1f));

        var label = CreateText(
            block.transform,
            "Label",
            12f,
            FontStyles.Bold,
            TextAlignmentOptions.TopLeft);
        label.text = "ARMY";
        label.color = AccentColor;
        SetRect(label.rectTransform, new Vector2(0.06f, 0.76f), new Vector2(0.94f, 0.94f), Vector2.zero, Vector2.zero);

        var renameLabel = CreateText(
            block.transform,
            "Rename Label",
            11f,
            FontStyles.Normal,
            TextAlignmentOptions.BottomLeft);
        renameLabel.text = "Name";
        renameLabel.color = MutedText;
        SetRect(renameLabel.rectTransform, new Vector2(0.06f, 0.50f), new Vector2(0.94f, 0.72f), Vector2.zero, Vector2.zero);

        renameInput = CreateInput(block.transform);
        SetRect(
            renameInput.GetComponent<RectTransform>(),
            new Vector2(0.06f, 0.31f),
            new Vector2(0.94f, 0.53f),
            Vector2.zero,
            Vector2.zero);

        var renameButton = CreateButton(
            block.transform,
            "Rename",
            new Vector2(0.06f, 0.07f),
            new Vector2(0.94f, 0.26f),
            RenameArmy);

        renameButton.GetComponentInChildren<TextMeshProUGUI>().fontSize = 12f;
    }

    private void BuildMemberStrip()
    {
        var viewportObject = new GameObject(
            "Army Members",
            typeof(RectTransform),
            typeof(Image),
            typeof(RectMask2D),
            typeof(ScrollRect));

        viewportObject.transform.SetParent(root.transform, false);

        var viewportRect = viewportObject.GetComponent<RectTransform>();
        SetRect(
            viewportRect,
            new Vector2(0.185f, 0.055f),
            new Vector2(0.855f, 0.79f),
            Vector2.zero,
            Vector2.zero);

        viewportObject.GetComponent<Image>().color =
            new Color(0.035f, 0.04f, 0.045f, 0.82f);

        var contentObject = new GameObject(
            "Content",
            typeof(RectTransform),
            typeof(HorizontalLayoutGroup),
            typeof(ContentSizeFitter));

        contentObject.transform.SetParent(viewportObject.transform, false);

        memberContent = contentObject.GetComponent<RectTransform>();
        memberContent.anchorMin = new Vector2(0f, 0f);
        memberContent.anchorMax = new Vector2(0f, 1f);
        memberContent.pivot = new Vector2(0f, 0.5f);
        memberContent.anchoredPosition = Vector2.zero;
        memberContent.sizeDelta = Vector2.zero;

        var layout = contentObject.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(9, 9, 8, 8);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        var fitter = contentObject.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        memberScroll = viewportObject.GetComponent<ScrollRect>();
        memberScroll.viewport = viewportRect;
        memberScroll.content = memberContent;
        memberScroll.horizontal = true;
        memberScroll.vertical = false;
        memberScroll.movementType = ScrollRect.MovementType.Clamped;
        memberScroll.inertia = true;
        memberScroll.scrollSensitivity = 32f;
    }

    private void BuildActionBlock()
    {
        var block = CreateBand(
            root.transform,
            "Army Actions",
            new Vector2(0.865f, 0.055f),
            new Vector2(0.988f, 0.79f),
            new Color(0.07f, 0.08f, 0.09f, 1f));

        var label = CreateText(
            block.transform,
            "Label",
            12f,
            FontStyles.Bold,
            TextAlignmentOptions.TopLeft);
        label.text = "SELECTED UNIT";
        label.color = AccentColor;
        SetRect(label.rectTransform, new Vector2(0.07f, 0.78f), new Vector2(0.93f, 0.95f), Vector2.zero, Vector2.zero);

        setLeadButton = CreateButton(
            block.transform,
            "Set Front",
            new Vector2(0.07f, 0.53f),
            new Vector2(0.93f, 0.73f),
            () => SetLead(selectedUnit));

        splitButton = CreateButton(
            block.transform,
            "Split Army",
            new Vector2(0.07f, 0.29f),
            new Vector2(0.93f, 0.49f),
            () => Split(selectedUnit));

        var centerButton = CreateButton(
            block.transform,
            "Center Camera",
            new Vector2(0.07f, 0.05f),
            new Vector2(0.93f, 0.25f),
            CenterOnSelectedArmy);

        centerButton.GetComponentInChildren<TextMeshProUGUI>().fontSize = 10.5f;
    }

    private void BuildTooltip()
    {
        tooltipRoot = new GameObject("Unit Hover Stats", typeof(RectTransform), typeof(Image));
        tooltipRoot.transform.SetParent(root.transform, false);

        tooltipRect = tooltipRoot.GetComponent<RectTransform>();
        tooltipRect.anchorMin = tooltipRect.anchorMax = new Vector2(0.5f, 0f);
        tooltipRect.pivot = new Vector2(0.5f, 0f);
        tooltipRect.sizeDelta = new Vector2(300f, 214f);
        tooltipRect.anchoredPosition = new Vector2(0f, 265f);

        tooltipRoot.GetComponent<Image>().color =
            new Color(0.045f, 0.05f, 0.055f, 0.99f);

        tooltipTitle = CreateText(
            tooltipRoot.transform,
            "Title",
            17f,
            FontStyles.Bold,
            TextAlignmentOptions.TopLeft);
        tooltipTitle.color = AccentColor;
        SetRect(
            tooltipTitle.rectTransform,
            new Vector2(0.06f, 0.72f),
            new Vector2(0.94f, 0.94f),
            Vector2.zero,
            Vector2.zero);

        tooltipBody = CreateText(
            tooltipRoot.transform,
            "Body",
            12.5f,
            FontStyles.Normal,
            TextAlignmentOptions.TopLeft);

        SetRect(
            tooltipBody.rectTransform,
            new Vector2(0.06f, 0.07f),
            new Vector2(0.94f, 0.72f),
            Vector2.zero,
            Vector2.zero);

        tooltipRoot.SetActive(false);
    }

    private void RebuildCards(List<CombatUnit> members, CombatUnit representative)
    {
        HideTooltip();

        for (int i = 0; i < cards.Count; i++)
            if (cards[i] != null)
                Destroy(cards[i]);

        cards.Clear();

        for (int i = 0; i < members.Count; i++)
        {
            var member = members[i];
            if (member == null)
                continue;

            CreateMemberCard(member, representative, i, members.Count);
        }

        Canvas.ForceUpdateCanvases();

        if (memberContent != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(memberContent);
    }

    private void CreateMemberCard(
        CombatUnit member,
        CombatUnit representative,
        int index,
        int count)
    {
        bool selected = member == selectedUnit;
        bool isFront = member == representative;

        var card = new GameObject(
            $"Unit Card {index + 1}",
            typeof(RectTransform),
            typeof(Image),
            typeof(Button),
            typeof(LayoutElement),
            typeof(EventTrigger));

        card.transform.SetParent(memberContent, false);
        cards.Add(card);

        var cardImage = card.GetComponent<Image>();
        cardImage.color = selected
            ? SelectedCardColor
            : isFront ? FrontCardColor : CardColor;

        var element = card.GetComponent<LayoutElement>();
        element.preferredWidth = 116f;
        element.minWidth = 116f;
        element.preferredHeight = 164f;

        var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        icon.transform.SetParent(card.transform, false);
        SetRect(
            icon.rectTransform,
            new Vector2(0.08f, 0.37f),
            new Vector2(0.92f, 0.95f),
            Vector2.zero,
            Vector2.zero);

        icon.sprite = member.data != null ? member.data.GetIcon(member.owner) : null;
        icon.enabled = icon.sprite != null;
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        if (isFront)
        {
            var front = CreateText(
                card.transform,
                "Front Badge",
                10f,
                FontStyles.Bold,
                TextAlignmentOptions.Center);

            front.text = "FRONT";
            front.color = AccentColor;
            SetRect(
                front.rectTransform,
                new Vector2(0.06f, 0.87f),
                new Vector2(0.48f, 0.98f),
                Vector2.zero,
                Vector2.zero);
        }

        var name = CreateText(
            card.transform,
            "Name",
            10.5f,
            FontStyles.Bold,
            TextAlignmentOptions.Center);

        name.text = member.data != null ? member.data.unitName : member.name;
        name.enableAutoSizing = true;
        name.fontSizeMin = 8f;
        name.fontSizeMax = 10.5f;
        name.textWrappingMode = TextWrappingModes.Normal;

        SetRect(
            name.rectTransform,
            new Vector2(0.05f, 0.23f),
            new Vector2(0.95f, 0.37f),
            Vector2.zero,
            Vector2.zero);

        CreateHealthBar(card.transform, member);

        var numbers = CreateText(
            card.transform,
            "Numbers",
            9.5f,
            FontStyles.Normal,
            TextAlignmentOptions.Center);

        numbers.color = MutedText;
        numbers.text =
            $"HP {member.currentHealth}/{member.MaxHealth}   MP {member.currentMovePoints:0.#}";

        SetRect(
            numbers.rectTransform,
            new Vector2(0.04f, 0.065f),
            new Vector2(0.96f, 0.16f),
            Vector2.zero,
            Vector2.zero);

        if (count > 1)
        {
            var left = CreateButton(
                card.transform,
                "◀",
                new Vector2(0.03f, 0.005f),
                new Vector2(0.26f, 0.075f),
                () => MoveMember(member, -1));

            left.interactable = index > 0;

            var right = CreateButton(
                card.transform,
                "▶",
                new Vector2(0.74f, 0.005f),
                new Vector2(0.97f, 0.075f),
                () => MoveMember(member, +1));

            right.interactable = index < count - 1;
        }

        var cardButton = card.GetComponent<Button>();
        cardButton.onClick.AddListener(() =>
            UnitSelectionManager.Instance?.SelectUnit(member));

        var trigger = card.GetComponent<EventTrigger>();
        AddPointerTrigger(trigger, EventTriggerType.PointerEnter, _ => ShowTooltip(member, card.GetComponent<RectTransform>()));
        AddPointerTrigger(trigger, EventTriggerType.PointerExit, _ => HideTooltip());
    }

    private void CreateHealthBar(Transform parent, CombatUnit member)
    {
        var track = CreateBand(
            parent,
            "Health Track",
            new Vector2(0.08f, 0.17f),
            new Vector2(0.92f, 0.215f),
            new Color(0.18f, 0.19f, 0.20f, 1f));

        var fill = CreateBand(
            track.transform,
            "Health Fill",
            Vector2.zero,
            Vector2.one,
            HealthyColor).GetComponent<Image>();

        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;

        float ratio = member.MaxHealth > 0
            ? Mathf.Clamp01(member.currentHealth / (float)member.MaxHealth)
            : 0f;

        fill.fillAmount = ratio;
        fill.color = ratio < 0.45f ? WoundedColor : HealthyColor;
        fill.raycastTarget = false;
    }

    private void ShowTooltip(CombatUnit member, RectTransform cardRect)
    {
        if (member == null || tooltipRoot == null || rootRect == null)
            return;

        tooltipTitle.text = member.data != null
            ? member.data.unitName
            : member.name;

        string typeName = member.data != null
            ? member.data.unitType.ToString()
            : "Combat Unit";

        tooltipBody.text =
            $"{typeName}\n" +
            $"Level {member.level}   XP {member.experience}\n\n" +
            $"Health        {member.currentHealth}/{member.MaxHealth}\n" +
            $"Attack        {member.CurrentAttack}\n" +
            $"Defense       {member.CurrentDefense}\n" +
            $"Range         {member.CurrentRange:0.#}\n" +
            $"Movement      {member.currentMovePoints:0.#}\n" +
            $"Attack Pts    {member.CurrentAttackPoints}/{member.MaxAttackPoints}";

        Vector3[] corners = new Vector3[4];
        cardRect.GetWorldCorners(corners);
        Vector3 cardTopCenterWorld = (corners[1] + corners[2]) * 0.5f;

        Canvas canvas = root.GetComponentInParent<Canvas>();
        Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;

        Vector2 screen = RectTransformUtility.WorldToScreenPoint(uiCamera, cardTopCenterWorld);
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rootRect,
            screen,
            uiCamera,
            out Vector2 local))
        {
            float half = tooltipRect.rect.width * 0.5f;
            float minX = rootRect.rect.xMin + half + 8f;
            float maxX = rootRect.rect.xMax - half - 8f;

            tooltipRect.anchoredPosition = new Vector2(
                Mathf.Clamp(local.x, minX, maxX),
                rootRect.rect.height + 8f);
        }

        tooltipRoot.SetActive(true);
        tooltipRoot.transform.SetAsLastSibling();
    }

    private void HideTooltip()
    {
        if (tooltipRoot != null)
            tooltipRoot.SetActive(false);
    }

    private void RefreshActionButtons(CombatUnit representative)
    {
        if (setLeadButton != null)
            setLeadButton.interactable =
                selectedUnit != null && representative != null && selectedUnit != representative;

        if (splitButton != null)
            splitButton.interactable =
                selectedUnit != null &&
                representative != null &&
                selectedUnit != representative &&
                selectedUnit.currentMovePoints > 0;
    }

    private void MoveMember(CombatUnit member, int direction)
    {
        if (member == null || direction == 0)
            return;

        var members = CampaignArmyService.GetMembers(member);
        int index = members.IndexOf(member);
        int targetIndex = index + direction;

        if (index < 0 || targetIndex < 0 || targetIndex >= members.Count)
            return;

        var other = members[targetIndex];
        if (other == null)
            return;

        int memberSlot = member.stackSlot;
        int otherSlot = other.stackSlot;

        member.stackSlot = otherSlot;
        other.stackSlot = memberSlot;

        if (member.stackSlot == 0)
            CampaignArmyService.SetRepresentative(member);
        else if (other.stackSlot == 0)
            CampaignArmyService.SetRepresentative(other);
        else
            CampaignArmyService.RefreshPresentation(member);

        Refresh();

        if (UnitSelectionManager.Instance != null && selectedUnit != null)
            UnitSelectionManager.Instance.SelectUnit(selectedUnit);
    }

    private void SetLead(CombatUnit member)
    {
        if (member == null)
            return;

        if (!CampaignArmyService.SetRepresentative(member))
        {
            UIManager.Instance?.ShowNotification(
                "Cannot change the army's front unit here.");
            return;
        }

        selectedUnit = member;
        UnitSelectionManager.Instance?.SelectUnit(member);
        UIManager.Instance?.ShowNotification(
            $"{member.UnitName} now leads the army.");
    }

    private void Split(CombatUnit member)
    {
        if (member == null)
            return;

        if (!member.Unstack())
        {
            UIManager.Instance?.ShowNotification(
                "Cannot split this unit: an adjacent legal tile is required.");
            return;
        }

        selectedUnit = member;
        UnitSelectionManager.Instance?.SelectUnit(member);
        UIManager.Instance?.ShowNotification(
            $"{member.UnitName} formed a new army.");
    }

    private void CenterOnSelectedArmy()
    {
        if (selectedUnit == null)
            return;

        var representative = CampaignArmyService.GetRepresentative(selectedUnit);
        if (representative == null)
            return;

        if (Camera.main != null)
        {
            var cameraTransform = Camera.main.transform;
            Vector3 delta = representative.transform.position - cameraTransform.position;
            Vector3 horizontal = Vector3.ProjectOnPlane(delta, cameraTransform.forward);
            cameraTransform.position += horizontal;
        }
    }

    private void RenameArmy()
    {
        if (selectedUnit == null || renameInput == null)
            return;

        CampaignArmyService.RenameArmy(selectedUnit, renameInput.text);
        Refresh();
    }

    private TMP_InputField CreateInput(Transform parent)
    {
        var inputObject = new GameObject(
            "Rename Input",
            typeof(RectTransform),
            typeof(Image),
            typeof(TMP_InputField));

        inputObject.transform.SetParent(parent, false);
        inputObject.GetComponent<Image>().color =
            new Color(0.13f, 0.14f, 0.15f, 1f);

        var text = CreateText(
            inputObject.transform,
            "Text",
            12f,
            FontStyles.Normal,
            TextAlignmentOptions.MidlineLeft);

        SetRect(
            text.rectTransform,
            Vector2.zero,
            Vector2.one,
            new Vector2(8f, 0f),
            new Vector2(-8f, 0f));

        var input = inputObject.GetComponent<TMP_InputField>();
        input.textComponent = text;
        input.characterLimit = 32;
        input.onSubmit.AddListener(_ => RenameArmy());

        return input;
    }

    private static void AddPointerTrigger(
        EventTrigger trigger,
        EventTriggerType type,
        UnityEngine.Events.UnityAction<BaseEventData> callback)
    {
        if (trigger == null)
            return;

        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(callback);
        trigger.triggers.Add(entry);
    }

    private static GameObject CreateBand(
        Transform parent,
        string name,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Color color)
    {
        var band = new GameObject(name, typeof(RectTransform), typeof(Image));
        band.transform.SetParent(parent, false);
        SetRect(
            band.GetComponent<RectTransform>(),
            anchorMin,
            anchorMax,
            Vector2.zero,
            Vector2.zero);

        band.GetComponent<Image>().color = color;
        return band;
    }

    private static Button CreateButton(
        Transform parent,
        string label,
        Vector2 anchorMin,
        Vector2 anchorMax,
        UnityEngine.Events.UnityAction action)
    {
        var buttonObject = new GameObject(
            label,
            typeof(RectTransform),
            typeof(Image),
            typeof(Button));

        buttonObject.transform.SetParent(parent, false);
        SetRect(
            buttonObject.GetComponent<RectTransform>(),
            anchorMin,
            anchorMax,
            Vector2.zero,
            Vector2.zero);

        buttonObject.GetComponent<Image>().color =
            new Color(0.16f, 0.18f, 0.19f, 1f);

        var button = buttonObject.GetComponent<Button>();
        if (action != null)
            button.onClick.AddListener(action);

        var text = CreateText(
            buttonObject.transform,
            "Label",
            11f,
            FontStyles.Bold,
            TextAlignmentOptions.Center);

        text.text = label;
        Stretch(text.rectTransform, Vector2.zero, Vector2.zero);

        return button;
    }

    private static TextMeshProUGUI CreateText(
        Transform parent,
        string name,
        float size,
        FontStyles style,
        TextAlignmentOptions alignment)
    {
        var textObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(TextMeshProUGUI));

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

    private static void Stretch(
        RectTransform rect,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        SetRect(rect, Vector2.zero, Vector2.one, offsetMin, offsetMax);
    }

    private static void SetRect(
        RectTransform rect,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }
}
