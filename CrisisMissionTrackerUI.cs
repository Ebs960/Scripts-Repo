using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class CrisisMissionTrackerUI : MonoBehaviour
{
    private sealed class HoverTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public System.Func<string> TitleGetter;
        public System.Func<string> BodyGetter;
        public System.Action Clicked;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (TooltipSystem.Instance == null) return;
            TooltipSystem.Instance.ShowSimpleTooltip(TitleGetter != null ? TitleGetter() : string.Empty, BodyGetter != null ? BodyGetter() : string.Empty);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (TooltipSystem.Instance != null)
                TooltipSystem.Instance.HideTooltip();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            Clicked?.Invoke();
        }
    }

    private sealed class MissionEntryView
    {
        public GameObject root;
        public Image icon;
        public TextMeshProUGUI titleText;
        public TextMeshProUGUI objectiveText;
        public TextMeshProUGUI progressText;
        public Image progressFill;
        public HoverTarget hoverTarget;
    }

    private RectTransform rootRect;
    private Image backgroundImage;
    private Button crisisButton;
    private Image crisisIconImage;
    private TextMeshProUGUI crisisNameText;
    private TextMeshProUGUI crisisTurnsText;
    private TextMeshProUGUI crisisPhaseText;
    private RectTransform missionListRect;
    private GameObject emptyStateObject;
    private TextMeshProUGUI emptyStateText;

    private GameObject detailBackdrop;
    private GameObject detailPanel;
    private TextMeshProUGUI detailTitleText;
    private TextMeshProUGUI detailBodyText;
    private RectTransform detailObjectivesRect;

    private readonly System.Collections.Generic.List<MissionEntryView> missionEntries = new System.Collections.Generic.List<MissionEntryView>();

    private CrisisManager subscribedCrisisManager;
    private TurnManager subscribedTurnManager;
    private Civilization playerCiv;
    private bool uiBuilt;

    private static readonly Color PanelColor = new Color(0.08f, 0.1f, 0.14f, 0.94f);
    private static readonly Color HeaderColor = new Color(0.15f, 0.18f, 0.24f, 1f);
    private static readonly Color EntryColor = new Color(0.12f, 0.14f, 0.2f, 0.98f);
    private static readonly Color BarBackgroundColor = new Color(1f, 1f, 1f, 0.12f);
    private static readonly Color BarFillColor = new Color(0.84f, 0.69f, 0.29f, 1f);
    private static readonly Color DimTextColor = new Color(0.78f, 0.82f, 0.9f, 1f);

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
        if (TooltipSystem.Instance != null)
            TooltipSystem.Instance.HideTooltip();
    }

    private void Update()
    {
        TrySubscribe();
        if (playerCiv == null)
            playerCiv = ResolvePlayerCivilization();
    }

    private void TrySubscribe()
    {
        if (subscribedCrisisManager == null && CrisisManager.Instance != null)
        {
            subscribedCrisisManager = CrisisManager.Instance;
            subscribedCrisisManager.OnCrisisOminousWarning += HandleTrackerDirty;
            subscribedCrisisManager.OnCrisisObviousWarning += HandleTrackerDirty;
            subscribedCrisisManager.OnCrisisStarted += HandleTrackerDirty;
            subscribedCrisisManager.OnCrisisPhaseChanged += HandleCrisisPhaseChanged;
            subscribedCrisisManager.OnCrisisEnded += HandleTrackerDirty;
            subscribedCrisisManager.OnMissionStarted += HandleMissionChanged;
            subscribedCrisisManager.OnObjectiveCompleted += HandleObjectiveCompleted;
            subscribedCrisisManager.OnMissionCompleted += HandleMissionCompleted;
            subscribedCrisisManager.OnMissionFailed += HandleMissionFailed;
            Debug.Log("[CrisisMissionTrackerUI] Subscribed to CrisisManager events");
        }

        if (subscribedTurnManager == null && TurnManager.Instance != null)
        {
            subscribedTurnManager = TurnManager.Instance;
            subscribedTurnManager.OnTurnChanged += HandleTurnChanged;
        }
    }

    private void Unsubscribe()
    {
        if (subscribedCrisisManager != null)
        {
            subscribedCrisisManager.OnCrisisOminousWarning -= HandleTrackerDirty;
            subscribedCrisisManager.OnCrisisObviousWarning -= HandleTrackerDirty;
            subscribedCrisisManager.OnCrisisStarted -= HandleTrackerDirty;
            subscribedCrisisManager.OnCrisisPhaseChanged -= HandleCrisisPhaseChanged;
            subscribedCrisisManager.OnCrisisEnded -= HandleTrackerDirty;
            subscribedCrisisManager.OnMissionStarted -= HandleMissionChanged;
            subscribedCrisisManager.OnObjectiveCompleted -= HandleObjectiveCompleted;
            subscribedCrisisManager.OnMissionCompleted -= HandleMissionCompleted;
            subscribedCrisisManager.OnMissionFailed -= HandleMissionFailed;
            subscribedCrisisManager = null;
        }

        if (subscribedTurnManager != null)
        {
            subscribedTurnManager.OnTurnChanged -= HandleTurnChanged;
            subscribedTurnManager = null;
        }
    }

    private void HandleTrackerDirty(CrisisData crisis)
    {
        RefreshAll();
    }

    private void HandleCrisisPhaseChanged(CrisisData crisis, CrisisData.CrisisPhase phase)
    {
        RefreshAll();
    }

    private void HandleMissionChanged(Civilization civ, MissionData mission)
    {
        Debug.Log($"[CrisisMissionTrackerUI] HandleMissionChanged civ={civ?.civData?.civName ?? "null"} mission={mission?.missionName ?? "null"} isPlayer={IsPlayerCivilization(civ)}");
        if (!IsPlayerCivilization(civ)) return;
        RefreshAll();
    }

    private void HandleObjectiveCompleted(Civilization civ, MissionData mission, int objectiveIndex)
    {
        Debug.Log($"[CrisisMissionTrackerUI] HandleObjectiveCompleted civ={civ?.civData?.civName ?? "null"} mission={mission?.missionName ?? "null"} index={objectiveIndex} isPlayer={IsPlayerCivilization(civ)}");
        if (!IsPlayerCivilization(civ)) return;
        RefreshAll();
    }

    private void HandleMissionCompleted(Civilization civ, MissionData mission, CrisisManager.MissionState state)
    {
        Debug.Log($"[CrisisMissionTrackerUI] HandleMissionCompleted civ={civ?.civData?.civName ?? "null"} mission={mission?.missionName ?? "null"} isPlayer={IsPlayerCivilization(civ)} completedObjectives={state?.CompletedObjectiveCount}");
        if (!IsPlayerCivilization(civ)) return;
        RefreshAll();
        HideMissionDetails();
    }

    private void HandleMissionFailed(Civilization civ, MissionData mission, string reason)
    {
        Debug.Log($"[CrisisMissionTrackerUI] HandleMissionFailed civ={civ?.civData?.civName ?? "null"} mission={mission?.missionName ?? "null"} isPlayer={IsPlayerCivilization(civ)} reason={reason}");
        if (!IsPlayerCivilization(civ)) return;
        RefreshAll();
        HideMissionDetails();
    }

    private void HandleTurnChanged(Civilization civ, int round)
    {
        if (!IsPlayerCivilization(civ)) return;
        RefreshAll();
    }

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

        BuildCrisisHeader();
        BuildMissionList();
        BuildMissionDetailPanel();
    }

    private void BuildCrisisHeader()
    {
        var headerObject = CreatePanelObject("CrisisHeader", transform, HeaderColor);
        var headerRect = headerObject.GetComponent<RectTransform>();
        headerRect.sizeDelta = new Vector2(0f, 58f);

        crisisButton = headerObject.AddComponent<Button>();
        var headerLayout = headerObject.AddComponent<HorizontalLayoutGroup>();
        headerLayout.padding = new RectOffset(10, 10, 8, 8);
        headerLayout.spacing = 10;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        headerLayout.childControlHeight = true;
        headerLayout.childControlWidth = false;
        headerLayout.childForceExpandHeight = false;
        headerLayout.childForceExpandWidth = false;

        crisisIconImage = CreateImage("CrisisIcon", headerObject.transform, 38f, 38f);

        var textColumn = new GameObject("TextColumn", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        textColumn.transform.SetParent(headerObject.transform, false);
        var textColumnLayout = textColumn.GetComponent<VerticalLayoutGroup>();
        textColumnLayout.spacing = 2;
        textColumnLayout.childAlignment = TextAnchor.MiddleLeft;
        textColumnLayout.childControlHeight = true;
        textColumnLayout.childControlWidth = true;
        textColumnLayout.childForceExpandHeight = false;
        textColumnLayout.childForceExpandWidth = true;
        textColumn.GetComponent<LayoutElement>().flexibleWidth = 1f;

        crisisNameText = CreateText("CrisisName", textColumn.transform, 20f, FontStyles.Bold, TextAlignmentOptions.Left);
        crisisPhaseText = CreateText("CrisisPhase", textColumn.transform, 14f, FontStyles.Normal, TextAlignmentOptions.Left);
        crisisPhaseText.color = DimTextColor;

        crisisTurnsText = CreateText("CrisisTurns", headerObject.transform, 16f, FontStyles.Bold, TextAlignmentOptions.Right);
        crisisTurnsText.color = Color.white;
        crisisTurnsText.rectTransform.sizeDelta = new Vector2(92f, 38f);

        var hoverTarget = headerObject.AddComponent<HoverTarget>();
        hoverTarget.TitleGetter = BuildCrisisTooltipTitle;
        hoverTarget.BodyGetter = BuildCrisisTooltipBody;
    }

    private void BuildMissionList()
    {
        var listObject = new GameObject("MissionList", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        listObject.transform.SetParent(transform, false);
        missionListRect = listObject.GetComponent<RectTransform>();
        var layout = listObject.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 8;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        listObject.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        emptyStateObject = CreatePanelObject("EmptyState", listObject.transform, new Color(1f, 1f, 1f, 0.06f));
        var emptyLayout = emptyStateObject.AddComponent<LayoutElement>();
        emptyLayout.preferredHeight = 44f;
        emptyStateText = CreateText("EmptyStateText", emptyStateObject.transform, 14f, FontStyles.Normal, TextAlignmentOptions.Center);
        emptyStateText.text = "No active mission";
        emptyStateText.color = DimTextColor;
    }

    private void BuildMissionDetailPanel()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        detailBackdrop = new GameObject("MissionDetailBackdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        detailBackdrop.transform.SetParent(canvas.transform, false);
        var backdropRect = detailBackdrop.GetComponent<RectTransform>();
        backdropRect.anchorMin = Vector2.zero;
        backdropRect.anchorMax = Vector2.one;
        backdropRect.offsetMin = Vector2.zero;
        backdropRect.offsetMax = Vector2.zero;
        detailBackdrop.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);
        detailBackdrop.GetComponent<Button>().onClick.AddListener(HideMissionDetails);

        detailPanel = CreatePanelObject("MissionDetailPanel", detailBackdrop.transform, new Color(0.09f, 0.11f, 0.16f, 0.98f));
        var panelRect = detailPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(560f, 520f);

        var panelLayout = detailPanel.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(20, 20, 20, 20);
        panelLayout.spacing = 10;
        panelLayout.childControlHeight = true;
        panelLayout.childControlWidth = true;
        panelLayout.childForceExpandHeight = false;
        panelLayout.childForceExpandWidth = true;

        detailTitleText = CreateText("DetailTitle", detailPanel.transform, 26f, FontStyles.Bold, TextAlignmentOptions.Left);
        detailBodyText = CreateText("DetailBody", detailPanel.transform, 16f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        detailBodyText.color = DimTextColor;

        var scrollObject = new GameObject("DetailScroll", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect), typeof(LayoutElement));
        scrollObject.transform.SetParent(detailPanel.transform, false);
        scrollObject.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f);
        scrollObject.GetComponent<Mask>().showMaskGraphic = false;
        scrollObject.GetComponent<LayoutElement>().flexibleHeight = 1f;
        var scrollRect = scrollObject.GetComponent<ScrollRect>();
        scrollRect.horizontal = false;

        var contentObject = new GameObject("DetailObjectives", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentObject.transform.SetParent(scrollObject.transform, false);
        detailObjectivesRect = contentObject.GetComponent<RectTransform>();
        detailObjectivesRect.anchorMin = new Vector2(0f, 1f);
        detailObjectivesRect.anchorMax = new Vector2(1f, 1f);
        detailObjectivesRect.pivot = new Vector2(0.5f, 1f);
        detailObjectivesRect.offsetMin = new Vector2(14f, 14f);
        detailObjectivesRect.offsetMax = new Vector2(-14f, -14f);
        var objectivesLayout = contentObject.GetComponent<VerticalLayoutGroup>();
        objectivesLayout.spacing = 8;
        objectivesLayout.childControlHeight = true;
        objectivesLayout.childControlWidth = true;
        objectivesLayout.childForceExpandHeight = false;
        objectivesLayout.childForceExpandWidth = true;
        contentObject.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scrollRect.content = detailObjectivesRect;
        scrollRect.viewport = scrollObject.GetComponent<RectTransform>();

        var closeButton = CreateButton("CloseDetailButton", detailPanel.transform, "Close");
        closeButton.onClick.AddListener(HideMissionDetails);

        detailBackdrop.SetActive(false);
    }

    private void RefreshAll()
    {
        playerCiv = ResolvePlayerCivilization();
        if (playerCiv == null || subscribedCrisisManager == null || subscribedCrisisManager.ActiveCrisis == null)
        {
            gameObject.SetActive(false);
            HideMissionDetails();
            return;
        }

        gameObject.SetActive(true);
        RefreshCrisisHeader();
        RefreshMissionEntries();
    }

    private void RefreshCrisisHeader()
    {
        var crisis = subscribedCrisisManager.ActiveCrisis;
        if (crisis == null) return;

        crisisIconImage.sprite = crisis.icon;
        crisisIconImage.gameObject.SetActive(crisis.icon != null);
        crisisNameText.text = string.IsNullOrWhiteSpace(crisis.crisisName) ? "Crisis" : crisis.crisisName;
        crisisPhaseText.text = FormatPhaseLabel(subscribedCrisisManager.CurrentPhase);

        int turnsRemaining = subscribedCrisisManager.GetDisplayTurnsRemaining();
        if (subscribedCrisisManager.CurrentPhase == CrisisData.CrisisPhase.OminousWarning || subscribedCrisisManager.CurrentPhase == CrisisData.CrisisPhase.ObviousWarning)
            crisisTurnsText.text = turnsRemaining >= 0 ? $"{turnsRemaining} to go" : "Incoming";
        else
            crisisTurnsText.text = turnsRemaining >= 0 ? $"{turnsRemaining} left" : "Ongoing";
    }

    private void RefreshMissionEntries()
    {
        var state = subscribedCrisisManager.GetActiveMission(playerCiv);
        if (state == null || state.mission == null)
        {
            ClearMissionEntries();
            emptyStateObject.SetActive(true);
            return;
        }

        emptyStateObject.SetActive(false);
        EnsureMissionEntryCount(1);
        RefreshMissionEntry(missionEntries[0], state);
        for (int i = 1; i < missionEntries.Count; i++)
            missionEntries[i].root.SetActive(false);
    }

    private void ClearMissionEntries()
    {
        for (int i = 0; i < missionEntries.Count; i++)
            missionEntries[i].root.SetActive(false);
    }

    private void EnsureMissionEntryCount(int count)
    {
        while (missionEntries.Count < count)
            missionEntries.Add(CreateMissionEntry());
    }

    private MissionEntryView CreateMissionEntry()
    {
        var entry = new MissionEntryView();
        entry.root = CreatePanelObject("MissionEntry", missionListRect, EntryColor);
        entry.root.AddComponent<Button>();
        var vertical = entry.root.AddComponent<VerticalLayoutGroup>();
        vertical.padding = new RectOffset(10, 10, 10, 10);
        vertical.spacing = 8;
        vertical.childControlHeight = true;
        vertical.childControlWidth = true;
        vertical.childForceExpandHeight = false;
        vertical.childForceExpandWidth = true;

        var topRow = new GameObject("TopRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        topRow.transform.SetParent(entry.root.transform, false);
        var topLayout = topRow.GetComponent<HorizontalLayoutGroup>();
        topLayout.spacing = 10;
        topLayout.childAlignment = TextAnchor.MiddleLeft;
        topLayout.childControlHeight = true;
        topLayout.childControlWidth = false;
        topLayout.childForceExpandHeight = false;
        topLayout.childForceExpandWidth = false;

        entry.icon = CreateImage("MissionIcon", topRow.transform, 34f, 34f);

        var textColumn = new GameObject("MissionTextColumn", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        textColumn.transform.SetParent(topRow.transform, false);
        textColumn.GetComponent<LayoutElement>().flexibleWidth = 1f;
        var textLayout = textColumn.GetComponent<VerticalLayoutGroup>();
        textLayout.spacing = 2;
        textLayout.childAlignment = TextAnchor.MiddleLeft;
        textLayout.childControlHeight = true;
        textLayout.childControlWidth = true;
        textLayout.childForceExpandHeight = false;
        textLayout.childForceExpandWidth = true;

        entry.titleText = CreateText("MissionTitle", textColumn.transform, 18f, FontStyles.Bold, TextAlignmentOptions.Left);
        entry.objectiveText = CreateText("MissionObjective", textColumn.transform, 14f, FontStyles.Normal, TextAlignmentOptions.Left);
        entry.objectiveText.color = DimTextColor;

        entry.progressText = CreateText("MissionProgress", topRow.transform, 16f, FontStyles.Bold, TextAlignmentOptions.Right);
        entry.progressText.rectTransform.sizeDelta = new Vector2(92f, 34f);

        var barBackground = CreatePanelObject("ProgressBar", entry.root.transform, BarBackgroundColor);
        var barLayout = barBackground.AddComponent<LayoutElement>();
        barLayout.preferredHeight = 8f;
        var barRect = barBackground.GetComponent<RectTransform>();
        barRect.sizeDelta = new Vector2(0f, 8f);

        entry.progressFill = CreateImage("Fill", barBackground.transform, 0f, 0f);
        var fillRect = entry.progressFill.rectTransform;
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        entry.progressFill.color = BarFillColor;

        entry.hoverTarget = entry.root.AddComponent<HoverTarget>();
        entry.root.SetActive(false);
        return entry;
    }

    private void RefreshMissionEntry(MissionEntryView entry, CrisisManager.MissionState state)
    {
        entry.root.SetActive(true);
        entry.icon.sprite = state.mission.icon != null ? state.mission.icon : state.mission.splashImage;
        entry.icon.gameObject.SetActive(entry.icon.sprite != null);
        entry.titleText.text = string.IsNullOrWhiteSpace(state.mission.missionName) ? "Mission" : state.mission.missionName;

        var objective = state.CurrentObjective;
        int current = subscribedCrisisManager.GetCurrentObjectiveProgress(state);
        int target = Mathf.Max(1, subscribedCrisisManager.GetCurrentObjectiveTarget(state));
        bool isSurviveObjective = objective != null && objective.type == MissionData.ObjectiveType.SurviveTurns;
        int remaining = Mathf.Max(0, target - current);

        entry.objectiveText.text = BuildObjectiveSummary(objective, target);
        entry.progressText.text = isSurviveObjective ? $"{remaining} left" : $"{Mathf.Min(current, target)}/{target}";

        float fill = subscribedCrisisManager.GetCurrentObjectiveProgress01(state);
        entry.progressFill.rectTransform.anchorMax = new Vector2(fill, 1f);

        entry.hoverTarget.TitleGetter = () => state.mission != null ? state.mission.missionName : "Mission";
        entry.hoverTarget.BodyGetter = () => BuildMissionTooltipBody(state);
        entry.hoverTarget.Clicked = () => ShowMissionDetails(state);
    }

    private void ShowMissionDetails(CrisisManager.MissionState state)
    {
        if (state?.mission == null || detailBackdrop == null) return;

        detailTitleText.text = state.mission.missionName;

        var bodyBuilder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(state.mission.description))
            bodyBuilder.AppendLine(state.mission.description.Trim());
        if (!string.IsNullOrWhiteSpace(state.mission.flavorText))
        {
            if (bodyBuilder.Length > 0) bodyBuilder.AppendLine();
            bodyBuilder.AppendLine(state.mission.flavorText.Trim());
        }
        detailBodyText.text = bodyBuilder.ToString().Trim();

        foreach (Transform child in detailObjectivesRect)
            Destroy(child.gameObject);

        for (int i = 0; i < state.mission.objectives.Count; i++)
        {
            var objective = state.mission.objectives[i];
            if (objective == null) continue;

            var item = CreatePanelObject("ObjectiveItem", detailObjectivesRect, new Color(1f, 1f, 1f, 0.05f));
            var itemLayout = item.AddComponent<VerticalLayoutGroup>();
            itemLayout.padding = new RectOffset(10, 10, 10, 10);
            itemLayout.spacing = 6;
            itemLayout.childControlHeight = true;
            itemLayout.childControlWidth = true;
            itemLayout.childForceExpandHeight = false;
            itemLayout.childForceExpandWidth = true;

            var header = CreateText("Header", item.transform, 16f, FontStyles.Bold, TextAlignmentOptions.Left);
            header.text = BuildObjectiveHeader(objective, i, state.currentObjectiveIndex, state.objectiveCompleted != null && i < state.objectiveCompleted.Length && state.objectiveCompleted[i]);

            var description = CreateText("Description", item.transform, 14f, FontStyles.Normal, TextAlignmentOptions.Left);
            description.color = DimTextColor;
            description.text = !string.IsNullOrWhiteSpace(objective.description) ? objective.description : BuildObjectiveSummary(objective, objective.targetValue);

            int progress = state.objectiveProgress != null && i < state.objectiveProgress.Length ? state.objectiveProgress[i] : 0;
            int target = Mathf.Max(1, objective.targetValue);

            var progressLabel = CreateText("Progress", item.transform, 14f, FontStyles.Bold, TextAlignmentOptions.Left);
            if (objective.type == MissionData.ObjectiveType.SurviveTurns)
                progressLabel.text = $"Progress: {Mathf.Max(0, target - progress)} turns remaining";
            else
                progressLabel.text = $"Progress: {Mathf.Min(progress, target)}/{target}";

            var barBackground = CreatePanelObject("Bar", item.transform, BarBackgroundColor);
            barBackground.AddComponent<LayoutElement>().preferredHeight = 8f;
            var fill = CreateImage("Fill", barBackground.transform, 0f, 0f);
            fill.color = BarFillColor;
            var fillRect = fill.rectTransform;
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(Mathf.Clamp01((float)progress / target), 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
        }

        detailBackdrop.SetActive(true);
    }

    private void HideMissionDetails()
    {
        if (detailBackdrop != null)
            detailBackdrop.SetActive(false);
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

    private string BuildCrisisTooltipTitle()
    {
        return subscribedCrisisManager != null && subscribedCrisisManager.ActiveCrisis != null
            ? subscribedCrisisManager.ActiveCrisis.crisisName
            : "Crisis";
    }

    private string BuildCrisisTooltipBody()
    {
        var crisis = subscribedCrisisManager != null ? subscribedCrisisManager.ActiveCrisis : null;
        if (crisis == null) return string.Empty;

        var builder = new StringBuilder();
        builder.AppendLine($"Phase: {FormatPhaseLabel(subscribedCrisisManager.CurrentPhase)}");

        int turnsRemaining = subscribedCrisisManager.GetDisplayTurnsRemaining();
        if (turnsRemaining >= 0)
            builder.AppendLine($"Turns Remaining: {turnsRemaining}");

        string description = ResolveCrisisNarrative(crisis, subscribedCrisisManager.CurrentPhase);
        if (!string.IsNullOrWhiteSpace(description))
        {
            builder.AppendLine();
            builder.AppendLine(description.Trim());
        }

        if (crisis.worldOverrides != null && crisis.worldOverrides.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Effects:");
            for (int i = 0; i < crisis.worldOverrides.Length; i++)
            {
                var ov = crisis.worldOverrides[i];
                if (ov == null) continue;
                builder.AppendLine($"- {FormatWorldOverride(ov)}");
            }
        }

        return builder.ToString().Trim();
    }

    private string BuildMissionTooltipBody(CrisisManager.MissionState state)
    {
        if (state?.mission == null) return string.Empty;

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(state.mission.description))
            builder.AppendLine(state.mission.description.Trim());

        for (int i = 0; i < state.mission.objectives.Count; i++)
        {
            var objective = state.mission.objectives[i];
            if (objective == null) continue;

            int progress = state.objectiveProgress != null && i < state.objectiveProgress.Length ? state.objectiveProgress[i] : 0;
            int target = Mathf.Max(1, objective.targetValue);
            bool completed = state.objectiveCompleted != null && i < state.objectiveCompleted.Length && state.objectiveCompleted[i];
            string marker = completed ? "[Done]" : i == state.currentObjectiveIndex ? "[Current]" : "[Next]";
            string progressText = objective.type == MissionData.ObjectiveType.SurviveTurns
                ? $"{Mathf.Max(0, target - progress)} left"
                : $"{Mathf.Min(progress, target)}/{target}";

            builder.AppendLine();
            builder.AppendLine($"{marker} {BuildObjectiveSummary(objective, target)}");
            builder.AppendLine($"Progress: {progressText}");
        }

        return builder.ToString().Trim();
    }

    private string BuildObjectiveSummary(MissionData.Objective objective, int target)
    {
        if (objective == null) return "Objective";
        if (!string.IsNullOrWhiteSpace(objective.objectiveName)) return objective.objectiveName;
        if (!string.IsNullOrWhiteSpace(objective.description)) return objective.description;

        switch (objective.type)
        {
            case MissionData.ObjectiveType.SurviveTurns: return $"Survive {target} turns";
            case MissionData.ObjectiveType.DefeatAnimals: return $"Kill {target} animals";
            case MissionData.ObjectiveType.DefeatUnits: return $"Defeat {target} units";
            case MissionData.ObjectiveType.BuildImprovements: return $"Build {target} improvements";
            case MissionData.ObjectiveType.ReachPopulation: return $"Reach population {target}";
            case MissionData.ObjectiveType.ResearchTech: return "Research the required technology";
            case MissionData.ObjectiveType.ResearchCulture: return "Research the required culture";
            case MissionData.ObjectiveType.FoundCity: return $"Found {target} cities";
            case MissionData.ObjectiveType.OwnTiles: return $"Own {target} tiles";
            case MissionData.ObjectiveType.AccumulateGold: return $"Accumulate {target} gold";
            case MissionData.ObjectiveType.AccumulateFood: return $"Accumulate {target} food";
            case MissionData.ObjectiveType.AccumulateFaith: return $"Accumulate {target} faith";
            case MissionData.ObjectiveType.TrainUnits: return $"Train {target} units";
            case MissionData.ObjectiveType.BuildBuilding: return $"Build {target} buildings";
            case MissionData.ObjectiveType.AdoptPolicy: return $"Adopt {target} policies";
            case MissionData.ObjectiveType.ChangeGovernment: return $"Change government {target} times";
            case MissionData.ObjectiveType.FormAlliance: return $"Form {target} alliances";
            case MissionData.ObjectiveType.FoundPantheon: return $"Found {target} pantheons";
            default: return objective.type.ToString();
        }
    }

    private string BuildObjectiveHeader(MissionData.Objective objective, int index, int currentIndex, bool completed)
    {
        string prefix = completed ? "Complete" : index == currentIndex ? "Current" : "Upcoming";
        return $"{prefix}: {BuildObjectiveSummary(objective, objective != null ? objective.targetValue : 0)}";
    }

    private string ResolveCrisisNarrative(CrisisData crisis, CrisisData.CrisisPhase phase)
    {
        switch (phase)
        {
            case CrisisData.CrisisPhase.OminousWarning: return crisis.ominousWarningText;
            case CrisisData.CrisisPhase.ObviousWarning: return crisis.obviousWarningText;
            case CrisisData.CrisisPhase.Active: return crisis.crisisStartText;
            case CrisisData.CrisisPhase.Escalation: return crisis.escalationText;
            case CrisisData.CrisisPhase.Climax: return crisis.climaxText;
            case CrisisData.CrisisPhase.Resolution: return crisis.resolutionText;
            default: return string.Empty;
        }
    }

    private string FormatPhaseLabel(CrisisData.CrisisPhase phase)
    {
        switch (phase)
        {
            case CrisisData.CrisisPhase.OminousWarning: return "Ominous Warning";
            case CrisisData.CrisisPhase.ObviousWarning: return "Obvious Warning";
            case CrisisData.CrisisPhase.Active: return "Active";
            case CrisisData.CrisisPhase.Escalation: return "Escalation";
            case CrisisData.CrisisPhase.Climax: return "Climax";
            case CrisisData.CrisisPhase.Resolution: return "Resolution";
            default: return "Dormant";
        }
    }

    private string FormatWorldOverride(CrisisData.WorldOverride worldOverride)
    {
        switch (worldOverride.type)
        {
            case CrisisData.WorldOverrideType.WinterDurationTurns:
                return $"Winter lasts {Mathf.RoundToInt(worldOverride.value)} turns";
            case CrisisData.WorldOverrideType.ForceWinter:
                return "Winter persists for the entire crisis";
            case CrisisData.WorldOverrideType.DroughtChance:
                return $"Drought chance +{worldOverride.value * 100f:0.#}%";
            case CrisisData.WorldOverrideType.DroughtSeverity:
                return $"Drought severity +{worldOverride.value * 100f:0.#}%";
            case CrisisData.WorldOverrideType.PreySpawnMultiplier:
                return $"Prey spawn x{worldOverride.value:0.##}";
            case CrisisData.WorldOverrideType.PredatorSpawnMultiplier:
                return $"Predator spawn x{worldOverride.value:0.##}";
            case CrisisData.WorldOverrideType.WinterAttritionDamage:
                return $"Winter attrition +{Mathf.RoundToInt(worldOverride.value)}";
            case CrisisData.WorldOverrideType.FoodYieldMultiplier:
                return $"Food yield {(worldOverride.value >= 0f ? "+" : string.Empty)}{worldOverride.value * 100f:0.#}%";
            default:
                return worldOverride.type.ToString();
        }
    }

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

    private Button CreateButton(string name, Transform parent, string label)
    {
        var buttonObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        buttonObject.transform.SetParent(parent, false);
        buttonObject.GetComponent<Image>().color = HeaderColor;
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