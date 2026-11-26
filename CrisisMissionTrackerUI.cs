using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// HUD tracker for the active crisis and the player's mission, plus its details window.
/// The layout lives in the Crisis Tracker prefab; this component only binds game data to it.
/// The component stays enabled so it keeps hearing crisis events; only its two child roots are shown and hidden.
/// </summary>
public class CrisisMissionTrackerUI : MonoBehaviour
{
    private sealed class HoverTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public System.Func<string> TitleGetter;
        public System.Func<string> BodyGetter;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (TooltipSystem.Instance == null) return;
            TooltipSystem.Instance.ShowSimpleTooltip(TitleGetter != null ? TitleGetter() : string.Empty, BodyGetter != null ? BodyGetter() : string.Empty);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (TooltipSystem.Instance != null)
                TooltipSystem.Instance.RequestHideTooltip();
        }
    }

    [Header("Tracker")]
    [SerializeField] private GameObject trackerRoot;
    [SerializeField] private Button trackerButton;
    [SerializeField] private Image crisisIconImage;
    [SerializeField] private TextMeshProUGUI crisisNameText;
    [SerializeField] private TextMeshProUGUI crisisPhaseText;
    [SerializeField] private TextMeshProUGUI crisisTurnsText;
    [SerializeField] private TextMeshProUGUI missionTitleText;
    [SerializeField] private TextMeshProUGUI missionObjectiveText;
    [SerializeField] private RectTransform progressFill;
    [SerializeField] private TextMeshProUGUI progressText;
    [SerializeField] private TextMeshProUGUI footerText;
    [SerializeField] private GameObject crisisHoverZone;
    [SerializeField] private GameObject missionHoverZone;

    [Header("Details Window")]
    [SerializeField] private GameObject detailRoot;
    [SerializeField] private Button detailBackdropButton;
    [SerializeField] private Button detailCloseButton;
    [SerializeField] private Button negotiateButton;
    [SerializeField] private TextMeshProUGUI detailTitleText;
    [SerializeField] private TextMeshProUGUI detailBodyText;
    [SerializeField] private RectTransform objectiveListRoot;
    [SerializeField] private MissionObjectiveRowUI objectiveRowPrefab;

    private CrisisManager subscribedCrisisManager;
    private TurnManager subscribedTurnManager;
    private Civilization playerCiv;

    private void Awake()
    {
        if (trackerButton != null) trackerButton.onClick.AddListener(ShowDetails);
        if (detailBackdropButton != null) detailBackdropButton.onClick.AddListener(HideDetails);
        if (detailCloseButton != null) detailCloseButton.onClick.AddListener(HideDetails);
        if (negotiateButton != null) negotiateButton.onClick.AddListener(HandleNegotiateClicked);

        AddHover(crisisHoverZone, BuildCrisisTooltipTitle, BuildCrisisTooltipBody);
        AddHover(missionHoverZone, BuildMissionTooltipTitle, BuildActiveMissionTooltipBody);

        if (trackerRoot != null) trackerRoot.SetActive(false);
        HideDetails();
    }

    private static void AddHover(GameObject zone, System.Func<string> title, System.Func<string> body)
    {
        if (zone == null) return;
        var hover = zone.GetComponent<HoverTarget>();
        if (hover == null) hover = zone.AddComponent<HoverTarget>();
        hover.TitleGetter = title;
        hover.BodyGetter = body;
    }

    private void OnEnable()
    {
        TrySubscribe();
        RefreshAll();
    }

    private void OnDisable()
    {
        Unsubscribe();
        if (TooltipSystem.Instance != null)
            TooltipSystem.Instance.RequestHideTooltip();
    }

    private void Update()
    {
        bool changed = TrySubscribe();
        if (playerCiv == null)
        {
            playerCiv = ResolvePlayerCivilization();
            changed |= playerCiv != null;
        }
        if (changed) RefreshAll();
    }

    /// <summary>Returns true when a manager was newly subscribed.</summary>
    private bool TrySubscribe()
    {
        bool changed = false;
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
            changed = true;
        }

        if (subscribedTurnManager == null && TurnManager.Instance != null)
        {
            subscribedTurnManager = TurnManager.Instance;
            subscribedTurnManager.OnTurnChanged += HandleTurnChanged;
            changed = true;
        }
        return changed;
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
        }
        subscribedCrisisManager = null;

        if (subscribedTurnManager != null)
            subscribedTurnManager.OnTurnChanged -= HandleTurnChanged;
        subscribedTurnManager = null;
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
        if (!IsPlayerCivilization(civ)) return;
        RefreshAll();
    }

    private void HandleObjectiveCompleted(Civilization civ, MissionData mission, int objectiveIndex)
    {
        if (!IsPlayerCivilization(civ)) return;
        RefreshAll();
    }

    private void HandleMissionCompleted(Civilization civ, MissionData mission, CrisisManager.MissionState state)
    {
        if (!IsPlayerCivilization(civ)) return;
        HideDetails();
        RefreshAll();
    }

    private void HandleMissionFailed(Civilization civ, MissionData mission, string reason)
    {
        if (!IsPlayerCivilization(civ)) return;
        HideDetails();
        RefreshAll();
    }

    private void HandleTurnChanged(Civilization civ, int round)
    {
        if (!IsPlayerCivilization(civ)) return;
        RefreshAll();
    }

    private bool IsDetailVisible => detailRoot != null && detailRoot.activeSelf;

    private CrisisManager.MissionState GetPlayerMission()
    {
        if (subscribedCrisisManager == null || playerCiv == null) return null;
        var state = subscribedCrisisManager.GetActiveMission(playerCiv);
        return state != null && state.mission != null ? state : null;
    }

    private void RefreshAll()
    {
        playerCiv = ResolvePlayerCivilization();
        bool show = playerCiv != null && subscribedCrisisManager != null && subscribedCrisisManager.ActiveCrisis != null;
        if (trackerRoot != null) trackerRoot.SetActive(show);
        if (!show)
        {
            HideDetails();
            return;
        }

        RefreshCrisisHeader();
        RefreshMission();
        if (IsDetailVisible) PopulateDetails();
    }

    private void RefreshCrisisHeader()
    {
        var crisis = subscribedCrisisManager.ActiveCrisis;
        var phase = subscribedCrisisManager.CurrentPhase;

        if (crisisNameText != null)
            crisisNameText.text = string.IsNullOrWhiteSpace(crisis.crisisName) ? "Crisis" : crisis.crisisName;
        if (crisisPhaseText != null)
            crisisPhaseText.text = FormatPhaseLabel(phase);

        if (crisisTurnsText != null)
        {
            int turnsRemaining = subscribedCrisisManager.GetDisplayTurnsRemaining();
            bool warning = phase == CrisisData.CrisisPhase.OminousWarning || phase == CrisisData.CrisisPhase.ObviousWarning;
            if (warning)
                crisisTurnsText.text = turnsRemaining >= 0 ? $"{turnsRemaining} to go" : "Incoming";
            else
                crisisTurnsText.text = turnsRemaining >= 0 ? $"{turnsRemaining} left" : "Ongoing";
        }
    }

    private void RefreshMission()
    {
        var crisis = subscribedCrisisManager.ActiveCrisis;
        var state = GetPlayerMission();

        if (crisisIconImage != null)
        {
            Sprite sprite = crisis.icon;
            if (sprite == null && state != null) sprite = state.mission.icon != null ? state.mission.icon : state.mission.splashImage;
            if (sprite == null) sprite = crisis.crisisStartSplash;
            crisisIconImage.sprite = sprite;
            crisisIconImage.gameObject.SetActive(sprite != null);
            var fitter = crisisIconImage.GetComponent<AspectRatioFitter>();
            if (fitter != null && sprite != null && sprite.rect.height > 0f)
                fitter.aspectRatio = sprite.rect.width / sprite.rect.height;
        }

        if (state == null)
        {
            if (missionTitleText != null) missionTitleText.text = "No active mission";
            if (missionObjectiveText != null) missionObjectiveText.text = string.Empty;
            if (progressText != null) progressText.text = string.Empty;
            SetFill(progressFill, 0f);
            if (footerText != null) footerText.text = "Click for crisis details";
            return;
        }

        var objective = state.CurrentObjective;
        int current = subscribedCrisisManager.GetCurrentObjectiveProgress(state);
        int target = Mathf.Max(1, subscribedCrisisManager.GetCurrentObjectiveTarget(state));
        bool isSurviveObjective = objective != null && objective.type == MissionData.ObjectiveType.SurviveTurns;

        if (missionTitleText != null)
            missionTitleText.text = string.IsNullOrWhiteSpace(state.mission.missionName) ? "Mission" : state.mission.missionName;
        if (missionObjectiveText != null)
            missionObjectiveText.text = MissionNarrativeFormatter.Resolve(BuildObjectiveSummary(objective, target), state.mission, subscribedCrisisManager, playerCiv, state);
        if (progressText != null)
            progressText.text = isSurviveObjective ? $"{Mathf.Max(0, target - current)} left" : $"{Mathf.Min(current, target)}/{target}";
        SetFill(progressFill, subscribedCrisisManager.GetCurrentObjectiveProgress01(state));
        if (footerText != null) footerText.text = "Click for mission details";
    }

    private static void SetFill(RectTransform fill, float amount)
    {
        if (fill == null) return;
        fill.anchorMin = new Vector2(0f, 0f);
        fill.anchorMax = new Vector2(Mathf.Clamp01(amount), 1f);
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;
    }

    /// <summary>Opens the details window for the active crisis and the player's mission.</summary>
    public void ShowDetails()
    {
        if (detailRoot == null || subscribedCrisisManager == null || subscribedCrisisManager.ActiveCrisis == null) return;
        if (TooltipSystem.Instance != null)
            TooltipSystem.Instance.RequestHideTooltip();
        PopulateDetails();
        detailRoot.SetActive(true);
    }

    public void HideDetails()
    {
        if (detailRoot != null)
            detailRoot.SetActive(false);
    }

    private void PopulateDetails()
    {
        var crisis = subscribedCrisisManager.ActiveCrisis;
        var state = GetPlayerMission();

        if (detailTitleText != null)
            detailTitleText.text = state != null ? state.mission.missionName : crisis.crisisName;

        if (detailBodyText != null)
        {
            if (state == null)
            {
                detailBodyText.text = BuildCrisisTooltipBody();
            }
            else
            {
                var bodyBuilder = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(state.mission.description))
                    bodyBuilder.AppendLine(MissionNarrativeFormatter.Resolve(state.mission.description.Trim(), state.mission, subscribedCrisisManager, playerCiv, state));
                if (!string.IsNullOrWhiteSpace(state.mission.flavorText))
                {
                    if (bodyBuilder.Length > 0) bodyBuilder.AppendLine();
                    bodyBuilder.AppendLine(MissionNarrativeFormatter.Resolve(state.mission.flavorText.Trim(), state.mission, subscribedCrisisManager, playerCiv, state));
                }
                detailBodyText.text = bodyBuilder.ToString().Trim();
            }
        }

        if (objectiveListRoot != null)
        {
            for (int i = objectiveListRoot.childCount - 1; i >= 0; i--)
            {
                var child = objectiveListRoot.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            if (state != null && objectiveRowPrefab != null && state.mission.objectives != null)
            {
                for (int i = 0; i < state.mission.objectives.Count; i++)
                {
                    var objective = state.mission.objectives[i];
                    if (objective == null) continue;

                    bool completed = state.objectiveCompleted != null && i < state.objectiveCompleted.Length && state.objectiveCompleted[i];
                    bool isCurrent = !completed && i == state.currentObjectiveIndex;
                    int progress = state.objectiveProgress != null && i < state.objectiveProgress.Length ? state.objectiveProgress[i] : 0;
                    int target = Mathf.Max(1, state.resolvedTargets != null && i < state.resolvedTargets.Length ? state.resolvedTargets[i] : objective.targetValue);
                    string progressLabel = objective.type == MissionData.ObjectiveType.SurviveTurns
                        ? $"{Mathf.Max(0, target - progress)} turns remaining"
                        : $"{Mathf.Min(progress, target)}/{target}";
                    string description = MissionNarrativeFormatter.Resolve(
                        !string.IsNullOrWhiteSpace(objective.description) ? objective.description : BuildObjectiveSummary(objective, objective.targetValue),
                        state.mission, subscribedCrisisManager, playerCiv, state);

                    var row = Instantiate(objectiveRowPrefab, objectiveListRoot, false);
                    row.Bind(
                        i + 1,
                        BuildObjectiveHeader(objective, i, state.currentObjectiveIndex, completed),
                        description,
                        progressLabel,
                        completed ? 1f : (float)progress / target,
                        completed,
                        isCurrent);
                }
            }
        }

        if (negotiateButton != null)
            negotiateButton.gameObject.SetActive(subscribedCrisisManager.CanNegotiateAlienSettlement(playerCiv));
    }

    private void HandleNegotiateClicked()
    {
        if (subscribedCrisisManager == null) return;
        var civ = ResolvePlayerCivilization();
        bool negotiated = subscribedCrisisManager.TryNegotiateAlienSettlement(civ);
        HideDetails();
        if (negotiated) RefreshAll();
    }

    private string BuildMissionTooltipTitle()
    {
        var state = GetPlayerMission();
        return state != null ? state.mission.missionName : "No active mission";
    }

    private string BuildActiveMissionTooltipBody()
    {
        var state = GetPlayerMission();
        return state != null ? BuildMissionTooltipBody(state) : "No mission is being pursued in this crisis.";
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
        {
            if (crisis.mechanic==CrisisData.CrisisMechanic.AsteroidCountdown)
            {
                builder.AppendLine($"ASTEROID IMPACT IN: {turnsRemaining} TURNS");
                builder.AppendLine($"Threatened Region: {subscribedCrisisManager.GetThreatenedRegionDisplayName()}");
            }
            else builder.AppendLine($"Turns Remaining: {turnsRemaining}");
        }

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
            builder.AppendLine(MissionNarrativeFormatter.Resolve(state.mission.description.Trim(),state.mission,subscribedCrisisManager,ResolvePlayerCivilization(),state));

        for (int i = 0; i < state.mission.objectives.Count; i++)
        {
            var objective = state.mission.objectives[i];
            if (objective == null) continue;

            int progress = state.objectiveProgress != null && i < state.objectiveProgress.Length ? state.objectiveProgress[i] : 0;
            int target = Mathf.Max(1, state.resolvedTargets != null && i < state.resolvedTargets.Length ? state.resolvedTargets[i] : objective.targetValue);
            bool completed = state.objectiveCompleted != null && i < state.objectiveCompleted.Length && state.objectiveCompleted[i];
            string marker = completed ? "[Done]" : i == state.currentObjectiveIndex ? "[Current]" : "[Next]";
            string progressText = objective.type == MissionData.ObjectiveType.SurviveTurns
                ? $"{Mathf.Max(0, target - progress)} left"
                : $"{Mathf.Min(progress, target)}/{target}";

            builder.AppendLine();
            builder.AppendLine($"{marker} {MissionNarrativeFormatter.Resolve(BuildObjectiveSummary(objective, target),state.mission,subscribedCrisisManager,ResolvePlayerCivilization(),state)}");
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
}
