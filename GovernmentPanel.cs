using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Owns the authored Government dashboard, its explicit detail screens, shared popups, confirmation dialog,
/// and event-driven refresh lifecycle.
/// </summary>
public class GovernmentPanel : MonoBehaviour
{
    public static GovernmentPanel Instance { get; private set; }

    [Header("Root")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private GameObject overviewRoot;
    [SerializeField] private Button closeButton;

    [Header("Government Display")]
    [SerializeField] private TMP_Text civilizationNameText;
    [SerializeField] private TMP_Text governmentNameText;
    [SerializeField] private TMP_Text governmentSubtitleText;
    [SerializeField] private Image governmentArtworkImage;
    [SerializeField] private Image governmentIconImage;
    [SerializeField] private Button governmentButton;
    [SerializeField] private TMP_Text leaderTitleText;
    [SerializeField] private TMP_Text institutionText;
    [SerializeField] private TMP_Text policyPointsText;

    [Header("Governors")]
    [SerializeField] private Transform governorsRoot;
    [SerializeField] private ScrollRect governorsScroll;
    [SerializeField] private GovernmentSummaryRowUI governorEntryPrefab;
    [SerializeField] private TMP_Text noGovernorsText;

    [Header("Vassals")]
    [SerializeField] private Transform vassalsRoot;
    [SerializeField] private ScrollRect vassalsScroll;
    [SerializeField] private GovernmentSummaryRowUI vassalEntryPrefab;
    [SerializeField] private TMP_Text noVassalsText;

    [Header("Policies")]
    [SerializeField] private Transform policiesRoot;
    [SerializeField] private ScrollRect policiesScroll;
    [SerializeField] private GameObject policyCategoryHeaderPrefab;
    [SerializeField] private PolicyNameButtonUI policyEntryPrefab;
    [SerializeField] private TMP_Text noPoliciesText;

    [Header("Government Selection")]
    [SerializeField] private GameObject governmentSelectionRoot;
    [SerializeField] private GovernmentTypesTab governmentTypesController;

    [Header("Policies Detail")]
    [SerializeField] private GameObject policiesDetailRoot;
    [SerializeField] private GovernmentPoliciesTab policiesController;

    [Header("Governors Detail")]
    [SerializeField] private GameObject governorsDetailRoot;
    [SerializeField] private GovernmentGovernorsTab governorsController;

    [Header("Vassals Detail")]
    [SerializeField] private GameObject vassalsDetailRoot;
    [SerializeField] private GovernmentVassalsTab vassalsController;

    [Header("Politics Detail")]
    [SerializeField] private GameObject politicsRoot;
    [SerializeField] private GovernmentPoliticsTab politicsController;

    [Header("Entity Popup")]
    [SerializeField] private GovernmentEntityActionPopup entityActionPopup;

    [Header("Attention / Political Warnings (optional)")]
    [SerializeField] private Transform warningsRoot;
    [SerializeField] private PoliticalLineRowUI warningRowPrefab;
    [SerializeField] private GameObject noWarningsRoot;

    [Header("Confirmation")]
    [SerializeField] private PoliticalConfirmDialog confirmDialogUI;

    private static readonly PolicyTag[] DashboardCategoryOrder =
    {
        PolicyTag.Administration, PolicyTag.Military, PolicyTag.Law, PolicyTag.Rights, PolicyTag.Labor,
        PolicyTag.Economy, PolicyTag.Trade, PolicyTag.Agriculture, PolicyTag.Infrastructure,
        PolicyTag.Education, PolicyTag.Religion, PolicyTag.Security, PolicyTag.Welfare,
        PolicyTag.Environment, PolicyTag.Colonial, PolicyTag.Digital, PolicyTag.Synthetic,
        PolicyTag.Genetics, PolicyTag.Space,
    };

    private readonly List<GovernmentSummaryRowUI> governorRows = new List<GovernmentSummaryRowUI>();
    private readonly List<GovernmentSummaryRowUI> vassalRows = new List<GovernmentSummaryRowUI>();
    private readonly List<GameObject> policyObjects = new List<GameObject>();
    private Civilization civ;
    private Civilization subscribedCiv;
    private GameObject currentViewRoot;
    private PoliticalLineList warnings;
    private bool isOpen;
    private bool isShowing;
    private bool refreshQueued;
    private bool closeButtonWired;
    private bool dashboardWired;

    public Civilization Civilization => civ;
    public bool IsOpen => isOpen && (panelRoot == null || panelRoot.activeInHierarchy);
    public bool IsConfirmationVisible => confirmDialogUI != null && confirmDialogUI.IsVisible;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    private void OnEnable()
    {
        Civilization.GovernorAssignmentChanged += HandleGovernorAssignmentChanged;
        Civilization.CouncilMembershipChanged += HandleCivilizationChanged;
        Civilization.FactionsChanged += HandleCivilizationChanged;
        ElectionManager.ElectionStateChanged += HandleCivilizationChanged;
        CouncilVoteService.ResultRecorded += HandleCivilizationChanged;
        PoliticalEventManager.EventsChanged += QueueRefresh;
        SubjectManager.ContractChanged += HandleContractChanged;
        EnsureCloseButtonWired();

        if (!isShowing && panelRoot != null && panelRoot.activeInHierarchy)
            ShowForCivilization(civ ?? PoliticalActionRules.FindPlayerCivilization());
    }

    private void OnDisable()
    {
        Civilization.GovernorAssignmentChanged -= HandleGovernorAssignmentChanged;
        Civilization.CouncilMembershipChanged -= HandleCivilizationChanged;
        Civilization.FactionsChanged -= HandleCivilizationChanged;
        ElectionManager.ElectionStateChanged -= HandleCivilizationChanged;
        CouncilVoteService.ResultRecorded -= HandleCivilizationChanged;
        PoliticalEventManager.EventsChanged -= QueueRefresh;
        SubjectManager.ContractChanged -= HandleContractChanged;
        closeButtonWired = false;
    }

    private void OnDestroy()
    {
        UnsubscribeCivilization();
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!IsOpen || Keyboard.current == null || !Keyboard.current[Key.Escape].wasPressedThisFrame) return;
        if (IsConfirmationVisible) confirmDialogUI.Cancel();
        else Close();
    }

    private void LateUpdate()
    {
        if (!refreshQueued) return;
        refreshQueued = false;
        RefreshAllVisible();
    }

    public void ShowForCivilization(Civilization civilization)
    {
        civilization ??= PoliticalActionRules.FindPlayerCivilization();
        if (civilization == null) return;

        isShowing = true;
        try
        {
            if (!OpenModal()) return;
            SetCivilization(civilization);
            EnsureCloseButtonWired();
            ShowOverview();
        }
        finally
        {
            isShowing = false;
        }
    }

    public void ShowOverview()
    {
        SetActiveView(overviewRoot);
        RefreshOverview();
    }

    public void ShowGovernmentSelection()
    {
        SetActiveView(governmentSelectionRoot);
        RefreshController(governmentTypesController);
    }

    public void ShowPolicies()
    {
        SetActiveView(policiesDetailRoot);
        RefreshController(policiesController);
    }

    public void ShowPolicy(PolicyData policy)
    {
        policiesController?.FocusPolicy(policy);
        ShowPolicies();
    }

    public void ShowGovernors()
    {
        SetActiveView(governorsDetailRoot);
        RefreshController(governorsController);
    }

    public void ShowGovernor(Governor governor)
    {
        governorsController?.FocusGovernor(governor);
        ShowGovernors();
    }

    public void ShowVassals()
    {
        SetActiveView(vassalsDetailRoot);
        RefreshController(vassalsController);
    }

    public void ShowVassal(VassalContract contract)
    {
        vassalsController?.FocusVassal(contract);
        ShowVassals();
    }

    public void ShowPolitics()
    {
        SetActiveView(politicsRoot);
        RefreshController(politicsController);
    }

    public void OpenWarningDestination(PoliticalWarningDestination destination)
    {
        switch (destination)
        {
            case PoliticalWarningDestination.GovernmentSelection: ShowGovernmentSelection(); break;
            case PoliticalWarningDestination.Policies: ShowPolicies(); break;
            case PoliticalWarningDestination.Governors: ShowGovernors(); break;
            case PoliticalWarningDestination.Vassals: ShowVassals(); break;
            case PoliticalWarningDestination.Politics: ShowPolitics(); break;
        }
    }

    public void RefreshAllVisible()
    {
        if (!IsOpen || civ == null) return;
        if (currentViewRoot == overviewRoot) RefreshOverview();
        else if (currentViewRoot == governmentSelectionRoot) RefreshController(governmentTypesController);
        else if (currentViewRoot == policiesDetailRoot) RefreshController(policiesController);
        else if (currentViewRoot == governorsDetailRoot) RefreshController(governorsController);
        else if (currentViewRoot == vassalsDetailRoot) RefreshController(vassalsController);
        else if (currentViewRoot == politicsRoot) RefreshController(politicsController);
    }

    public void Close()
    {
        confirmDialogUI?.Hide();
        entityActionPopup?.Hide();
        UnsubscribeCivilization();
        civ = null;
        currentViewRoot = null;
        isOpen = false;
        refreshQueued = false;

        if (UIManager.Instance != null)
        {
            UIManager.Instance.HideAllPanels();
            if (UIManager.Instance.gameplayHudRoot != null)
                UIManager.Instance.gameplayHudRoot.SetActive(true);
        }
        if (panelRoot != null && panelRoot.activeSelf) panelRoot.SetActive(false);
    }

    public void Hide() => Close();

    public void RequestConfirmation(PoliticalConfirmRequest request)
    {
        if (request == null) return;
        if (confirmDialogUI == null)
        {
            Debug.LogWarning("[GovernmentPanel] No PoliticalConfirmDialog is assigned.");
            return;
        }
        confirmDialogUI.Show(request);
    }

    private void SetActiveView(GameObject viewRoot)
    {
        GameObject[] roots =
        {
            overviewRoot, governmentSelectionRoot, policiesDetailRoot,
            governorsDetailRoot, vassalsDetailRoot, politicsRoot,
        };
        foreach (var root in roots)
            if (root != null) root.SetActive(root == viewRoot);
        currentViewRoot = viewRoot;
    }

    private void RefreshController(GovernmentTabBase controller)
    {
        if (controller == null || civ == null) return;
        controller.Bind(civ, this);
        controller.Refresh();
    }

    private void RefreshOverview()
    {
        if (civ == null) return;
        EnsureDashboardWired();
        var government = civ.currentGovernment;
        GovernmentUiUtil.SetText(civilizationNameText, GovernmentPresentation.NameOf(civ));
        GovernmentUiUtil.SetText(governmentNameText, government != null ? GovernmentPresentation.NameOf(government) : "No government");
        GovernmentUiUtil.SetText(governmentSubtitleText, government != null ? government.description : string.Empty);
        GovernmentUiUtil.SetImage(governmentArtworkImage, government != null ? government.governmentArtwork : null);
        GovernmentUiUtil.SetImage(governmentIconImage, government != null ? government.icon : null);
        GovernmentUiUtil.SetText(leaderTitleText, GovernmentPresentation.GetLeaderTitle(civ));
        GovernmentUiUtil.SetText(institutionText, GovernmentPresentation.GetInstitutionName(civ));
        GovernmentUiUtil.SetText(policyPointsText, $"Policy Points: {civ.policyPoints}");
        RefreshGovernors();
        RefreshVassals();
        RefreshPolicies();
        RefreshWarnings();
    }

    private void EnsureDashboardWired()
    {
        if (dashboardWired) return;
        dashboardWired = true;
        var selectionButton = governmentButton != null
            ? governmentButton
            : governmentArtworkImage != null ? governmentArtworkImage.GetComponent<Button>() : null;
        GovernmentUiUtil.SetClick(selectionButton, ShowGovernmentSelection);
    }

    private void RefreshGovernors()
    {
        var governors = civ.governors?.Where(governor => governor != null).ToList() ?? new List<Governor>();
        float scroll = GovernmentUiUtil.CaptureScroll(governorsScroll);
        GovernmentUiUtil.FillList(governorsRoot, governorEntryPrefab, governorRows, governors, (row, governor) =>
        {
            string traits = string.Join(", ", governor.PersonalityTraits.Take(2));
            string faith = governor.PersonalReligion != null ? GovernmentPresentation.NameOf(governor.PersonalReligion) : "none";
            string secondary = string.IsNullOrEmpty(traits) ? $"Faith: {faith}" : $"{traits} • Faith: {faith}";
            row.Bind(GovernorPortraitService.GetSprite(governor.PortraitId),
                GovernmentPresentation.FormatGovernorName(civ, governor),
                $"Loyalty {GovernmentUiUtil.Signed(governor.Opinion)} • {governor.Cities.Count} Cities",
                secondary, governor.IsInRebellion || governor.Opinion < 0,
                () => OpenGovernorSummary(governor));
        });
        GovernmentUiUtil.RestoreScroll(governorsScroll, scroll);
        GovernmentUiUtil.SetText(noGovernorsText, governors.Count == 0 ? "No governors." : string.Empty);
    }

    private void OpenGovernorSummary(Governor governor)
    {
        if (entityActionPopup != null) entityActionPopup.ShowGovernor(civ, governor, this);
        else ShowGovernor(governor);
    }

    private void RefreshVassals()
    {
        var manager = SubjectManager.Instance;
        var contracts = manager != null ? manager.GetSubjects(civ) : new List<VassalContract>();
        float scroll = GovernmentUiUtil.CaptureScroll(vassalsScroll);
        GovernmentUiUtil.FillList(vassalsRoot, vassalEntryPrefab, vassalRows, contracts, (row, contract) =>
        {
            var subject = contract.subject;
            string faith = subject?.StateReligion != null ? GovernmentPresentation.NameOf(subject.StateReligion) : "none";
            row.Bind(subject?.civData?.icon, contract.subjectCivName,
                $"Opinion {GovernmentUiUtil.Signed(manager.GetEffectiveSubjectOpinion(contract))} • Liberty {contract.libertyDesire:0} • {subject?.cities?.Count ?? 0} Cities",
                $"Faith: {faith}", contract.libertyDesire >= contract.EffectiveBreakawayThreshold * 0.75f,
                () => OpenVassalSummary(contract));
        });
        GovernmentUiUtil.RestoreScroll(vassalsScroll, scroll);
        GovernmentUiUtil.SetText(noVassalsText, contracts.Count == 0 ? "You have no vassals." : string.Empty);
    }

    private void OpenVassalSummary(VassalContract contract)
    {
        if (entityActionPopup != null) entityActionPopup.ShowVassal(civ, contract, this);
        else ShowVassal(contract);
    }

    private void RefreshPolicies()
    {
        float scroll = GovernmentUiUtil.CaptureScroll(policiesScroll);
        foreach (var item in policyObjects)
            if (item != null) Destroy(item);
        policyObjects.Clear();

        var manager = PolicyManager.Instance;
        var entries = manager == null || manager.allPolicies == null
            ? new List<KeyValuePair<PolicyData, PolicyAdoptionEvaluation>>()
            : manager.allPolicies.Where(policy => policy != null).Distinct()
                .Select(policy => new KeyValuePair<PolicyData, PolicyAdoptionEvaluation>(policy, manager.EvaluatePolicy(civ, policy)))
                .Where(entry => entry.Value != null &&
                    (entry.Value.State == PolicyListState.Active || entry.Value.State == PolicyListState.Available))
                .ToList();

        foreach (var category in DashboardCategoryOrder)
        {
            var categoryEntries = entries
                .Where(entry => PrimaryCategory(entry.Key) == category)
                .OrderBy(entry => entry.Value.State == PolicyListState.Active ? 0 : 1)
                .ThenBy(entry => GovernmentPresentation.NameOf(entry.Key))
                .ToList();
            if (categoryEntries.Count == 0) continue;

            if (policiesRoot != null && policyCategoryHeaderPrefab != null)
            {
                var header = Instantiate(policyCategoryHeaderPrefab, policiesRoot);
                policyObjects.Add(header);
                GovernmentUiUtil.SetText(header.GetComponentInChildren<TMP_Text>(true), PolicyCategoryName(category));
            }
            if (policiesRoot == null || policyEntryPrefab == null) continue;
            foreach (var entry in categoryEntries)
            {
                var row = Instantiate(policyEntryPrefab, policiesRoot);
                policyObjects.Add(row.gameObject);
                row.Bind(entry.Key, entry.Value, false, ShowPolicy);
            }
        }

        GovernmentUiUtil.SetText(noPoliciesText, entries.Count == 0 ? "No active or available policies." : string.Empty);
        GovernmentUiUtil.RestoreScroll(policiesScroll, scroll);
    }

    private static PolicyTag PrimaryCategory(PolicyData policy)
        => policy.policyTags != null && policy.policyTags.Length > 0 ? policy.policyTags[0] : PolicyTag.Administration;

    private static string PolicyCategoryName(PolicyTag tag)
    {
        switch (tag)
        {
            case PolicyTag.Rights: return "Civil Rights";
            case PolicyTag.Law: return "Law & Justice";
            case PolicyTag.Digital: return "Digital Policy";
            case PolicyTag.Synthetic: return "Synthetic Life";
            case PolicyTag.Space: return "Space & Planetary";
            default: return tag.ToString();
        }
    }

    private void RefreshWarnings()
    {
        if (warningsRoot == null || warningRowPrefab == null) return;
        warnings ??= new PoliticalLineList(warningsRoot, warningRowPrefab);
        var list = PoliticalWarningBuilder.Build(civ);
        warnings.Show(list, (row, warning) => row.BindWarning(warning, OpenWarningDestination));
        GovernmentUiUtil.SetActive(noWarningsRoot, list.Count == 0);
    }

    private bool OpenModal()
    {
        var ui = UIManager.Instance;
        if (ui != null)
        {
            if (ui.IsBlockingModalVisible) return false;
            if (ui.GetPanel("GovernmentPanel") == null && panelRoot != null) ui.RegisterPanel("GovernmentPanel", panelRoot);
            ui.ShowPanel("governmentPanel");
            ui.HidePanel("gameplayHudRoot");
            var registered = ui.GetPanel("GovernmentPanel");
            if (registered != null && !registered.activeInHierarchy) return false;
        }
        if (panelRoot != null && !panelRoot.activeSelf) panelRoot.SetActive(true);
        isOpen = true;
        return true;
    }

    private void EnsureCloseButtonWired()
    {
        if (closeButton == null || closeButtonWired) return;
        closeButton.onClick.RemoveListener(Close);
        closeButton.onClick.AddListener(Close);
        closeButtonWired = true;
        UIManager.Instance?.WireUIInteractions(closeButton.gameObject);
    }

    private void SetCivilization(Civilization target)
    {
        if (subscribedCiv != target)
        {
            UnsubscribeCivilization();
            subscribedCiv = target;
            target.OnPolicyAdopted += HandlePolicyChanged;
            target.OnPolicyRevoked += HandlePolicyChanged;
            target.OnGovernmentChanged += HandleGovernmentChanged;
            target.OnUnlocksChanged += QueueRefresh;
            target.OnPolicyPointsChanged += HandlePolicyPointsChanged;
        }
        civ = target;
    }

    private void UnsubscribeCivilization()
    {
        if (subscribedCiv == null) return;
        subscribedCiv.OnPolicyAdopted -= HandlePolicyChanged;
        subscribedCiv.OnPolicyRevoked -= HandlePolicyChanged;
        subscribedCiv.OnGovernmentChanged -= HandleGovernmentChanged;
        subscribedCiv.OnUnlocksChanged -= QueueRefresh;
        subscribedCiv.OnPolicyPointsChanged -= HandlePolicyPointsChanged;
        subscribedCiv = null;
    }

    private void QueueRefresh() { if (IsOpen) refreshQueued = true; }
    private void HandlePolicyChanged(Civilization c, PolicyData p) => QueueRefresh();
    private void HandleGovernmentChanged(Civilization c, GovernmentData g) => QueueRefresh();
    private void HandlePolicyPointsChanged(int total, int delta) => QueueRefresh();
    private void HandleGovernorAssignmentChanged(Civilization c, City city) { if (c == civ) QueueRefresh(); }
    private void HandleCivilizationChanged(Civilization c) { if (c == civ) QueueRefresh(); }
    private void HandleContractChanged(Civilization overlord, Civilization subject)
    {
        if (overlord == civ || subject == civ) QueueRefresh();
    }
}
