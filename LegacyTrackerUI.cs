using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Runtime-built HUD panel that displays earned legacies and lets the player promote/demote them.
/// Follows the same creation pattern as CrisisMissionTrackerUI.
/// </summary>
public class LegacyTrackerUI : MonoBehaviour
{
    private sealed class LegacyEntryView
    {
        public GameObject root;
        public Image icon;
        public TextMeshProUGUI nameText;
        public TextMeshProUGUI statusText;
        public Button actionButton;
        public TextMeshProUGUI actionLabel;
        public LegacyData legacy;
    }

    // ─── UI references ───
    private RectTransform rootRect;
    private Image backgroundImage;
    private TextMeshProUGUI headerText;
    private TextMeshProUGUI slotsText;
    private RectTransform listRect;
    private GameObject emptyStateObject;
    private TextMeshProUGUI emptyStateText;

    // Detail overlay
    private GameObject detailBackdrop;
    private GameObject detailPanel;
    private Image detailIcon;
    private TextMeshProUGUI detailTitle;
    private TextMeshProUGUI detailDescription;
    private TextMeshProUGUI detailFlavor;
    private TextMeshProUGUI detailCostText;
    private TextMeshProUGUI detailBonusesText;
    private Button detailPromoteButton;
    private TextMeshProUGUI detailPromoteLabel;
    private Button detailDemoteButton;
    private TextMeshProUGUI detailDemoteLabel;
    private Button detailCloseButton;

    private readonly List<LegacyEntryView> entryViews = new List<LegacyEntryView>();

    private LegacyManager subscribedLegacyManager;
    private Civilization playerCiv;
    private bool uiBuilt;
    private LegacyData shownLegacy;

    // ─── Colors ───
    private static readonly Color PanelColor = new Color(0.07f, 0.08f, 0.12f, 0.94f);
    private static readonly Color HeaderColor = new Color(0.14f, 0.16f, 0.22f, 1f);
    private static readonly Color EntryColor = new Color(0.11f, 0.13f, 0.18f, 0.98f);
    private static readonly Color ActiveEntryColor = new Color(0.16f, 0.2f, 0.12f, 0.98f);
    private static readonly Color ButtonColor = new Color(0.2f, 0.24f, 0.32f, 1f);
    private static readonly Color PromoteColor = new Color(0.18f, 0.42f, 0.22f, 1f);
    private static readonly Color DemoteColor = new Color(0.42f, 0.18f, 0.18f, 1f);
    private static readonly Color DimTextColor = new Color(0.78f, 0.82f, 0.9f, 1f);
    private static readonly Color GoldColor = new Color(0.84f, 0.69f, 0.29f, 1f);

    // ─── Lifecycle ───

    private void Awake()
    {
        EnsureUiBuilt();
        RefreshAll();
    }

    private void OnEnable()
    {
        EnsureUiBuilt();
        TrySubscribe();
        RefreshAll();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Update()
    {
        TrySubscribe();
        if (playerCiv == null)
            playerCiv = ResolvePlayerCivilization();
    }

    // ─── Subscriptions ───

    private void TrySubscribe()
    {
        if (subscribedLegacyManager == null && LegacyManager.Instance != null)
        {
            subscribedLegacyManager = LegacyManager.Instance;
            subscribedLegacyManager.OnLegacyEarned += HandleLegacyChanged;
            subscribedLegacyManager.OnLegacyPromoted += HandleLegacyChanged;
            subscribedLegacyManager.OnLegacyDemoted += HandleLegacyChanged;
        }
    }

    private void Unsubscribe()
    {
        if (subscribedLegacyManager != null)
        {
            subscribedLegacyManager.OnLegacyEarned -= HandleLegacyChanged;
            subscribedLegacyManager.OnLegacyPromoted -= HandleLegacyChanged;
            subscribedLegacyManager.OnLegacyDemoted -= HandleLegacyChanged;
            subscribedLegacyManager = null;
        }
    }

    private void HandleLegacyChanged(Civilization civ, LegacyData legacy)
    {
        if (!IsPlayerCivilization(civ)) return;
        RefreshAll();
        // Also refresh the detail panel if it's showing this legacy
        if (shownLegacy == legacy)
            RefreshDetailPanel(legacy);
    }

    // ─── UI Construction ───

    private void EnsureUiBuilt()
    {
        if (uiBuilt) return;
        uiBuilt = true;

        rootRect = gameObject.GetComponent<RectTransform>();
        backgroundImage = gameObject.GetComponent<Image>();
        if (backgroundImage == null)
            backgroundImage = gameObject.AddComponent<Image>();
        backgroundImage.color = PanelColor;

        var layout = gameObject.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
            layout = gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 8;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        var fitter = gameObject.GetComponent<ContentSizeFitter>();
        if (fitter == null)
            fitter = gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        BuildHeader();
        BuildLegacyList();
        BuildDetailPanel();
    }

    private void BuildHeader()
    {
        var headerObject = CreatePanelObject("LegacyHeader", transform, HeaderColor);
        var headerRect = headerObject.GetComponent<RectTransform>();
        headerRect.sizeDelta = new Vector2(0f, 48f);

        var headerLayout = headerObject.AddComponent<HorizontalLayoutGroup>();
        headerLayout.padding = new RectOffset(10, 10, 8, 8);
        headerLayout.spacing = 10;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        headerLayout.childControlHeight = true;
        headerLayout.childControlWidth = false;
        headerLayout.childForceExpandHeight = false;
        headerLayout.childForceExpandWidth = false;

        headerText = CreateText("HeaderTitle", headerObject.transform, 20f, FontStyles.Bold, TextAlignmentOptions.Left);
        headerText.text = "Legacies";
        var headerTextLayout = headerText.GetComponent<LayoutElement>();
        headerTextLayout.flexibleWidth = 1f;

        slotsText = CreateText("SlotsText", headerObject.transform, 16f, FontStyles.Bold, TextAlignmentOptions.Right);
        slotsText.color = GoldColor;
        slotsText.rectTransform.sizeDelta = new Vector2(92f, 32f);
    }

    private void BuildLegacyList()
    {
        var listObject = new GameObject("LegacyList", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        listObject.transform.SetParent(transform, false);
        listRect = listObject.GetComponent<RectTransform>();
        var layout = listObject.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 6;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        listObject.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        emptyStateObject = CreatePanelObject("EmptyState", listObject.transform, new Color(1f, 1f, 1f, 0.06f));
        emptyStateObject.AddComponent<LayoutElement>().preferredHeight = 44f;
        emptyStateText = CreateText("EmptyText", emptyStateObject.transform, 14f, FontStyles.Normal, TextAlignmentOptions.Center);
        emptyStateText.text = "No legacies earned yet";
        emptyStateText.color = DimTextColor;
    }

    private void BuildDetailPanel()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        // Backdrop
        detailBackdrop = new GameObject("LegacyDetailBackdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        detailBackdrop.transform.SetParent(canvas.transform, false);
        var backdropRect = detailBackdrop.GetComponent<RectTransform>();
        backdropRect.anchorMin = Vector2.zero;
        backdropRect.anchorMax = Vector2.one;
        backdropRect.offsetMin = Vector2.zero;
        backdropRect.offsetMax = Vector2.zero;
        detailBackdrop.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);
        detailBackdrop.GetComponent<Button>().onClick.AddListener(HideDetail);

        // Panel
        detailPanel = CreatePanelObject("LegacyDetailPanel", detailBackdrop.transform, new Color(0.09f, 0.11f, 0.16f, 0.98f));
        var panelRect = detailPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(480f, 520f);

        var panelLayout = detailPanel.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(20, 20, 20, 20);
        panelLayout.spacing = 10;
        panelLayout.childControlHeight = true;
        panelLayout.childControlWidth = true;
        panelLayout.childForceExpandHeight = false;
        panelLayout.childForceExpandWidth = true;

        // Icon + title row
        var topRow = new GameObject("TopRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        topRow.transform.SetParent(detailPanel.transform, false);
        var topLayout = topRow.GetComponent<HorizontalLayoutGroup>();
        topLayout.spacing = 14;
        topLayout.childAlignment = TextAnchor.MiddleLeft;
        topLayout.childControlHeight = true;
        topLayout.childControlWidth = false;
        topLayout.childForceExpandHeight = false;
        topLayout.childForceExpandWidth = false;

        detailIcon = CreateImage("LegacyIcon", topRow.transform, 64f, 64f);

        var titleColumn = new GameObject("TitleColumn", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        titleColumn.transform.SetParent(topRow.transform, false);
        titleColumn.GetComponent<LayoutElement>().flexibleWidth = 1f;
        var titleLayout = titleColumn.GetComponent<VerticalLayoutGroup>();
        titleLayout.spacing = 2;
        titleLayout.childAlignment = TextAnchor.MiddleLeft;
        titleLayout.childControlHeight = true;
        titleLayout.childControlWidth = true;
        titleLayout.childForceExpandHeight = false;
        titleLayout.childForceExpandWidth = true;

        detailTitle = CreateText("Title", titleColumn.transform, 24f, FontStyles.Bold, TextAlignmentOptions.Left);
        detailCostText = CreateText("Cost", titleColumn.transform, 14f, FontStyles.Normal, TextAlignmentOptions.Left);
        detailCostText.color = GoldColor;

        // Scroll area for description, flavor, bonuses
        var scrollObject = new GameObject("DetailScroll", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect), typeof(LayoutElement));
        scrollObject.transform.SetParent(detailPanel.transform, false);
        scrollObject.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f);
        scrollObject.GetComponent<Mask>().showMaskGraphic = false;
        scrollObject.GetComponent<LayoutElement>().flexibleHeight = 1f;
        var scrollRect = scrollObject.GetComponent<ScrollRect>();
        scrollRect.horizontal = false;

        var contentObject = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentObject.transform.SetParent(scrollObject.transform, false);
        var contentRect = contentObject.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = new Vector2(10f, 10f);
        contentRect.offsetMax = new Vector2(-10f, -10f);
        var contentLayout = contentObject.GetComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 10;
        contentLayout.childControlHeight = true;
        contentLayout.childControlWidth = true;
        contentLayout.childForceExpandHeight = false;
        contentLayout.childForceExpandWidth = true;
        contentObject.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scrollRect.content = contentRect;
        scrollRect.viewport = scrollObject.GetComponent<RectTransform>();

        detailDescription = CreateText("Description", contentObject.transform, 16f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        detailDescription.color = DimTextColor;

        detailFlavor = CreateText("Flavor", contentObject.transform, 14f, FontStyles.Italic, TextAlignmentOptions.TopLeft);
        detailFlavor.color = new Color(0.7f, 0.7f, 0.8f, 0.8f);

        detailBonusesText = CreateText("Bonuses", contentObject.transform, 15f, FontStyles.Normal, TextAlignmentOptions.TopLeft);

        // Buttons row
        var buttonRow = new GameObject("ButtonRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        buttonRow.transform.SetParent(detailPanel.transform, false);
        var buttonRowLayout = buttonRow.GetComponent<HorizontalLayoutGroup>();
        buttonRowLayout.spacing = 10;
        buttonRowLayout.childAlignment = TextAnchor.MiddleCenter;
        buttonRowLayout.childControlHeight = true;
        buttonRowLayout.childControlWidth = true;
        buttonRowLayout.childForceExpandHeight = false;
        buttonRowLayout.childForceExpandWidth = true;

        detailPromoteButton = CreateButton("PromoteButton", buttonRow.transform, "Promote", PromoteColor);
        detailPromoteLabel = detailPromoteButton.GetComponentInChildren<TextMeshProUGUI>();
        detailPromoteButton.onClick.AddListener(OnPromoteClicked);

        detailDemoteButton = CreateButton("DemoteButton", buttonRow.transform, "Demote", DemoteColor);
        detailDemoteLabel = detailDemoteButton.GetComponentInChildren<TextMeshProUGUI>();
        detailDemoteButton.onClick.AddListener(OnDemoteClicked);

        detailCloseButton = CreateButton("CloseButton", buttonRow.transform, "Close", ButtonColor);
        detailCloseButton.onClick.AddListener(HideDetail);

        detailBackdrop.SetActive(false);
    }

    // ─── Refresh ───

    private void RefreshAll()
    {
        playerCiv = ResolvePlayerCivilization();
        if (playerCiv == null || playerCiv.earnedLegacies == null || playerCiv.earnedLegacies.Count == 0)
        {
            gameObject.SetActive(false);
            HideDetail();
            return;
        }

        gameObject.SetActive(true);
        RefreshHeader();
        RefreshEntries();
    }

    private void RefreshHeader()
    {
        if (playerCiv == null) return;
        int active = playerCiv.activeLegacies != null ? playerCiv.activeLegacies.Count : 0;
        int max = playerCiv.maxActiveLegacies;
        slotsText.text = $"{active}/{max} Active";
    }

    private void RefreshEntries()
    {
        if (playerCiv == null) return;
        var earned = playerCiv.earnedLegacies;
        if (earned == null || earned.Count == 0)
        {
            ClearEntries();
            emptyStateObject.SetActive(true);
            return;
        }

        emptyStateObject.SetActive(false);
        EnsureEntryCount(earned.Count);

        for (int i = 0; i < earned.Count; i++)
        {
            var legacy = earned[i];
            var view = entryViews[i];
            bool isActive = playerCiv.activeLegacies != null && playerCiv.activeLegacies.Contains(legacy);
            RefreshEntry(view, legacy, isActive);
        }

        for (int i = earned.Count; i < entryViews.Count; i++)
            entryViews[i].root.SetActive(false);
    }

    private void ClearEntries()
    {
        for (int i = 0; i < entryViews.Count; i++)
            entryViews[i].root.SetActive(false);
    }

    private void EnsureEntryCount(int count)
    {
        while (entryViews.Count < count)
            entryViews.Add(CreateEntryView());
    }

    private LegacyEntryView CreateEntryView()
    {
        var view = new LegacyEntryView();
        view.root = CreatePanelObject("LegacyEntry", listRect, EntryColor);

        var entryLayout = view.root.AddComponent<HorizontalLayoutGroup>();
        entryLayout.padding = new RectOffset(10, 10, 8, 8);
        entryLayout.spacing = 10;
        entryLayout.childAlignment = TextAnchor.MiddleLeft;
        entryLayout.childControlHeight = true;
        entryLayout.childControlWidth = false;
        entryLayout.childForceExpandHeight = false;
        entryLayout.childForceExpandWidth = false;

        view.icon = CreateImage("Icon", view.root.transform, 36f, 36f);

        var textColumn = new GameObject("TextColumn", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        textColumn.transform.SetParent(view.root.transform, false);
        textColumn.GetComponent<LayoutElement>().flexibleWidth = 1f;
        var textLayout = textColumn.GetComponent<VerticalLayoutGroup>();
        textLayout.spacing = 2;
        textLayout.childAlignment = TextAnchor.MiddleLeft;
        textLayout.childControlHeight = true;
        textLayout.childControlWidth = true;
        textLayout.childForceExpandHeight = false;
        textLayout.childForceExpandWidth = true;

        view.nameText = CreateText("Name", textColumn.transform, 16f, FontStyles.Bold, TextAlignmentOptions.Left);
        view.statusText = CreateText("Status", textColumn.transform, 13f, FontStyles.Normal, TextAlignmentOptions.Left);
        view.statusText.color = DimTextColor;

        view.actionButton = CreateButton("Action", view.root.transform, "View", ButtonColor);
        view.actionButton.GetComponent<LayoutElement>().preferredWidth = 72f;
        view.actionLabel = view.actionButton.GetComponentInChildren<TextMeshProUGUI>();

        view.root.SetActive(false);
        return view;
    }

    private void RefreshEntry(LegacyEntryView view, LegacyData legacy, bool isActive)
    {
        view.root.SetActive(true);
        view.legacy = legacy;

        view.icon.sprite = legacy.icon;
        view.icon.gameObject.SetActive(legacy.icon != null);
        view.nameText.text = string.IsNullOrWhiteSpace(legacy.legacyName) ? "Legacy" : legacy.legacyName;
        view.statusText.text = isActive ? "Active" : "Earned";
        view.statusText.color = isActive ? GoldColor : DimTextColor;

        view.root.GetComponent<Image>().color = isActive ? ActiveEntryColor : EntryColor;

        view.actionButton.onClick.RemoveAllListeners();
        var capturedLegacy = legacy;
        view.actionButton.onClick.AddListener(() => ShowDetail(capturedLegacy));
        view.actionLabel.text = "View";
    }

    // ─── Detail Panel ───

    private void ShowDetail(LegacyData legacy)
    {
        if (legacy == null || detailBackdrop == null) return;
        shownLegacy = legacy;
        RefreshDetailPanel(legacy);
        detailBackdrop.SetActive(true);
    }

    private void RefreshDetailPanel(LegacyData legacy)
    {
        if (legacy == null) return;
        playerCiv = ResolvePlayerCivilization();

        detailIcon.sprite = legacy.icon;
        detailIcon.gameObject.SetActive(legacy.icon != null);
        detailTitle.text = string.IsNullOrWhiteSpace(legacy.legacyName) ? "Legacy" : legacy.legacyName;
        detailCostText.text = $"Promote Cost: {legacy.goldCost} Gold, {legacy.policyPointCost} Policy Points";

        detailDescription.text = !string.IsNullOrWhiteSpace(legacy.description) ? legacy.description : string.Empty;
        detailFlavor.text = !string.IsNullOrWhiteSpace(legacy.flavorText) ? legacy.flavorText : string.Empty;
        detailFlavor.gameObject.SetActive(!string.IsNullOrWhiteSpace(legacy.flavorText));

        detailBonusesText.text = BuildBonusSummary(legacy);

        bool isActive = playerCiv != null && playerCiv.activeLegacies != null && playerCiv.activeLegacies.Contains(legacy);
        bool canPromote = subscribedLegacyManager != null && playerCiv != null && subscribedLegacyManager.CanPromote(playerCiv, legacy);

        detailPromoteButton.gameObject.SetActive(!isActive);
        detailPromoteButton.interactable = canPromote;
        if (!isActive && !canPromote && playerCiv != null)
        {
            // Show why they can't promote
            if (playerCiv.activeLegacies != null && playerCiv.activeLegacies.Count >= playerCiv.maxActiveLegacies)
                detailPromoteLabel.text = "Slots Full";
            else if (playerCiv.gold < legacy.goldCost || playerCiv.policyPoints < legacy.policyPointCost)
                detailPromoteLabel.text = "Can't Afford";
            else
                detailPromoteLabel.text = "Promote";
        }
        else
        {
            detailPromoteLabel.text = "Promote";
        }

        detailDemoteButton.gameObject.SetActive(isActive);
    }

    private void HideDetail()
    {
        if (detailBackdrop != null)
            detailBackdrop.SetActive(false);
        shownLegacy = null;
    }

    private void OnPromoteClicked()
    {
        if (shownLegacy == null || subscribedLegacyManager == null || playerCiv == null) return;
        subscribedLegacyManager.PromoteLegacy(playerCiv, shownLegacy);
        // RefreshAll will be triggered by the OnLegacyPromoted event
    }

    private void OnDemoteClicked()
    {
        if (shownLegacy == null || subscribedLegacyManager == null || playerCiv == null) return;
        subscribedLegacyManager.DemoteLegacy(playerCiv, shownLegacy);
        // RefreshAll will be triggered by the OnLegacyDemoted event
    }

    // ─── Helpers ───

    private string BuildBonusSummary(LegacyData legacy)
    {
        var sb = new StringBuilder();
        if (legacy.attackBonus != 0f) sb.AppendLine($"Attack: +{legacy.attackBonus:0.#}");
        if (legacy.defenseBonus != 0f) sb.AppendLine($"Defense: +{legacy.defenseBonus:0.#}");
        if (legacy.movementBonus != 0f) sb.AppendLine($"Movement: +{legacy.movementBonus:0.#}");
        if (legacy.attackModifier != 0f) sb.AppendLine($"Attack: {legacy.attackModifier * 100f:+0.#;-0.#}%");
        if (legacy.defenseModifier != 0f) sb.AppendLine($"Defense: {legacy.defenseModifier * 100f:+0.#;-0.#}%");
        if (legacy.movementModifier != 0f) sb.AppendLine($"Movement: {legacy.movementModifier * 100f:+0.#;-0.#}%");
        if (legacy.foodModifier != 0f) sb.AppendLine($"Food: {legacy.foodModifier * 100f:+0.#;-0.#}%");
        if (legacy.productionModifier != 0f) sb.AppendLine($"Production: {legacy.productionModifier * 100f:+0.#;-0.#}%");
        if (legacy.goldModifier != 0f) sb.AppendLine($"Gold: {legacy.goldModifier * 100f:+0.#;-0.#}%");
        if (legacy.scienceModifier != 0f) sb.AppendLine($"Science: {legacy.scienceModifier * 100f:+0.#;-0.#}%");
        if (legacy.cultureModifier != 0f) sb.AppendLine($"Culture: {legacy.cultureModifier * 100f:+0.#;-0.#}%");
        if (legacy.faithModifier != 0f) sb.AppendLine($"Faith: {legacy.faithModifier * 100f:+0.#;-0.#}%");

        if (sb.Length == 0) sb.Append("No stat bonuses");
        return sb.ToString().TrimEnd();
    }

    private Civilization ResolvePlayerCivilization()
    {
        if (CivilizationManager.Instance != null && CivilizationManager.Instance.playerCiv != null)
            return CivilizationManager.Instance.playerCiv;
        if (TurnManager.Instance != null && TurnManager.Instance.playerCiv != null)
            return TurnManager.Instance.playerCiv;
        return null;
    }

    private bool IsPlayerCivilization(Civilization civ)
    {
        var player = ResolvePlayerCivilization();
        return civ != null && player != null && civ == player;
    }

    // ─── UI Factory ───

    private GameObject CreatePanelObject(string name, Transform parent, Color color)
    {
        var panel = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panel.transform.SetParent(parent, false);
        panel.GetComponent<Image>().color = color;
        return panel;
    }

    private Image CreateImage(string name, Transform parent, float width, float height)
    {
        var imageObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
        imageObject.transform.SetParent(parent, false);
        var layout = imageObject.GetComponent<LayoutElement>();
        if (width > 0f) layout.preferredWidth = width;
        if (height > 0f) layout.preferredHeight = height;
        var image = imageObject.GetComponent<Image>();
        image.preserveAspect = true;
        image.color = Color.white;
        return image;
    }

    private TextMeshProUGUI CreateText(string name, Transform parent, float fontSize, FontStyles fontStyle, TextAlignmentOptions alignment)
    {
        var textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        textObject.transform.SetParent(parent, false);
        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.color = Color.white;
        return text;
    }

    private Button CreateButton(string name, Transform parent, string label, Color color)
    {
        var buttonObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        buttonObject.transform.SetParent(parent, false);
        buttonObject.GetComponent<Image>().color = color;
        buttonObject.GetComponent<LayoutElement>().preferredHeight = 40f;

        var button = buttonObject.GetComponent<Button>();
        var text = CreateText("Label", buttonObject.transform, 15f, FontStyles.Bold, TextAlignmentOptions.Center);
        text.text = label;
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = Vector2.zero;
        text.rectTransform.offsetMax = Vector2.zero;
        return button;
    }
}
