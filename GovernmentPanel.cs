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
    [SerializeField] private TMP_Text policyPointsText;

    [Header("Governors")]
    [SerializeField] private Transform governorsRoot;
    [SerializeField] private ScrollRect governorsScroll;
    [SerializeField] private GovernorSummaryRowUI governorEntryPrefab;

    [Header("Vassals")]
    [SerializeField] private Transform vassalsRoot;
    [SerializeField] private ScrollRect vassalsScroll;
    [SerializeField] private VassalSummaryRowUI vassalEntryPrefab;

    [Header("Policies")]
    [SerializeField] private Transform policiesRoot;
    [SerializeField] private ScrollRect policiesScroll;
    [SerializeField] private PolicyAreaDropdownUI policyAreaDropdownPrefab;
    [SerializeField] private PolicyTooltipUI policyTooltip;

    [Header("Detail Screens")]
    [SerializeField] private GovernmentSelectionUI governmentSelectionUI;
    [SerializeField] private GovernmentGovernorsUI governorsUI;
    [SerializeField] private GovernmentVassalsUI vassalsUI;
    [SerializeField] private GovernmentPoliticsUI politicsUI;

    [Header("Warning Marker")]
    [SerializeField] private GameObject warningMarkerPrefab;

    [Header("Entity Popup")]
    [SerializeField] private GovernmentEntityActionPopup entityActionPopup;

    [Header("Governor Holdings")]
    [SerializeField] private GovernorHoldingsPanelUI governorHoldingsPanel;

    [Header("Confirmation")]
    [SerializeField] private PoliticalConfirmDialog confirmDialogUI;

    private static readonly PolicyArea[] DashboardAreaOrder =
    {
        PolicyArea.Administration, PolicyArea.Military, PolicyArea.Law, PolicyArea.CivilRights,
        PolicyArea.Slavery, PolicyArea.Labor, PolicyArea.Economy, PolicyArea.Trade,
        PolicyArea.Agriculture, PolicyArea.Infrastructure, PolicyArea.Education, PolicyArea.Religion,
        PolicyArea.Security, PolicyArea.Welfare, PolicyArea.Environment, PolicyArea.Colonial,
        PolicyArea.Digital, PolicyArea.Synthetic, PolicyArea.Genetics, PolicyArea.Space,
    };

    private readonly List<GovernorSummaryRowUI> governorRows = new List<GovernorSummaryRowUI>();
    private readonly List<VassalSummaryRowUI> vassalRows = new List<VassalSummaryRowUI>();
    private readonly List<PolicyAreaDropdownUI> policyRows = new List<PolicyAreaDropdownUI>();
    private Civilization civ;
    private Civilization subscribedCiv;
    private GameObject currentViewRoot;
    private bool isOpen;
    private bool isShowing;
    private bool refreshQueued;
    private bool closeButtonWired;
    private bool dashboardWired;

    public Civilization Civilization => civ;
    public GameObject WarningMarkerPrefab => warningMarkerPrefab;
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
        SetActiveView(governmentSelectionUI);
        RefreshController(governmentSelectionUI);
    }

    public void ShowPolicies()
    {
        ShowOverview();
        if (policiesScroll != null) policiesScroll.verticalNormalizedPosition = 1f;
    }

    public void ShowPolicy(PolicyData policy)
    {
        ShowPolicies();
    }

    public void ShowGovernors()
    {
        SetActiveView(governorsUI);
        RefreshController(governorsUI);
    }

    public void ShowGovernor(Governor governor)
    {
        governorsUI?.FocusGovernor(governor);
        ShowGovernors();
    }

    public void ShowVassals()
    {
        SetActiveView(vassalsUI);
        RefreshController(vassalsUI);
    }

    public void ShowVassal(VassalContract contract)
    {
        vassalsUI?.FocusVassal(contract);
        ShowVassals();
    }

    public void ShowPolitics()
    {
        SetActiveView(politicsUI);
        RefreshController(politicsUI);
    }

    public void ShowGovernorHoldings(Governor governor)
    {
        if (civ == null || governor == null || governorHoldingsPanel == null) return;
        governorHoldingsPanel.Show(civ, governor, RefreshAllVisible);
    }

    public void RefreshAllVisible()
    {
        if (!IsOpen || civ == null) return;
        if (currentViewRoot == overviewRoot) RefreshOverview();
        else if (governmentSelectionUI != null && currentViewRoot == governmentSelectionUI.gameObject) RefreshController(governmentSelectionUI);
        else if (governorsUI != null && currentViewRoot == governorsUI.gameObject) RefreshController(governorsUI);
        else if (vassalsUI != null && currentViewRoot == vassalsUI.gameObject) RefreshController(vassalsUI);
        else if (politicsUI != null && currentViewRoot == politicsUI.gameObject) RefreshController(politicsUI);
    }

    public void Close()
    {
        confirmDialogUI?.Hide();
        entityActionPopup?.Hide();
        governorHoldingsPanel?.Hide();
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

    private void SetActiveView(GovernmentScreenBase screen) => SetActiveView(screen != null ? screen.gameObject : null);

    private void SetActiveView(GameObject viewRoot)
    {
        GameObject[] roots =
        {
            overviewRoot,
            governmentSelectionUI != null ? governmentSelectionUI.gameObject : null,
            governorsUI != null ? governorsUI.gameObject : null,
            vassalsUI != null ? vassalsUI.gameObject : null,
            politicsUI != null ? politicsUI.gameObject : null,
        };
        foreach (var root in roots)
            if (root != null) root.SetActive(root == viewRoot);
        currentViewRoot = viewRoot;
    }

    private void RefreshController(GovernmentScreenBase controller)
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
        GovernmentUiUtil.SetText(policyPointsText, $"Policy Points: {civ.policyPoints}");
        RefreshGovernors();
        RefreshVassals();
        RefreshPolicies();
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
            row.Bind(civ, governor, OpenGovernorSummary, warningMarkerPrefab);
        });
        GovernmentUiUtil.RestoreScroll(governorsScroll, scroll);
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
            row.Bind(civ, contract, OpenVassalSummary, warningMarkerPrefab);
        });
        GovernmentUiUtil.RestoreScroll(vassalsScroll, scroll);
    }

    private void OpenVassalSummary(VassalContract contract)
    {
        if (entityActionPopup != null) entityActionPopup.ShowVassal(civ, contract, this);
        else ShowVassal(contract);
    }

    private void RefreshPolicies()
    {
        float scroll = GovernmentUiUtil.CaptureScroll(policiesScroll);
        var manager = PolicyManager.Instance;
        foreach (var row in policyRows) if (row != null) Destroy(row.gameObject);
        policyRows.Clear();
        if (manager != null && policiesRoot != null && policyAreaDropdownPrefab != null)
        {
            foreach (var area in DashboardAreaOrder)
            {
                var row = Instantiate(policyAreaDropdownPrefab, policiesRoot);
                policyRows.Add(row);
                row.Bind(civ, area, manager, policyTooltip, RequestPolicyAdoption, RequestPolicyRepeal);
            }
        }
        GovernmentUiUtil.RestoreScroll(policiesScroll, scroll);
    }

    private void RequestPolicyAdoption(PolicyData target)
    {
        var manager = PolicyManager.Instance;
        var evaluation = manager?.EvaluatePolicy(civ, target);
        if (target == null || evaluation == null || !evaluation.canAdopt) return;
        var oldName = evaluation.replacedAreaPolicy != null
            ? GovernmentPresentation.NameOf(evaluation.replacedAreaPolicy) : "No Policy";
        var lines = PoliticalEffectSummaryBuilder.BuildPolicyEffects(target);
        lines.AddRange(PoliticalEffectSummaryBuilder.BuildGovernorReactionLines(target.governorOpinionEffects));
        foreach (var requirement in PoliticalEffectSummaryBuilder.BuildPolicyRequirements(civ, target))
            lines.Add(new PoliticalEffectLine { label = "Requirement", value = $"{(requirement.met ? "Met" : "Missing")}: {requirement.label}", harmful = !requirement.met });
        AddPolicyRelations(lines, target);
        RequestConfirmation(new PoliticalConfirmRequest
        {
            title = $"Adopt {GovernmentPresentation.NameOf(target)}?",
            description = $"Replacing: {oldName}\n\nCurrent:\n{oldName}\n\nNew:\n{GovernmentPresentation.NameOf(target)}\n\nPolicy Points:\n-{target.policyPointCost}\n\nThe old policy is not refunded.",
            icon = target.icon,
            confirmLabel = "Adopt",
            lines = lines,
            onConfirm = () => { manager.AdoptPolicy(civ, target); RefreshAllVisible(); },
        });
    }

    private void RequestPolicyRepeal(PolicyData target)
    {
        if (target == null || PolicyManager.Instance == null) return;
        RequestConfirmation(new PoliticalConfirmRequest
        {
            title = $"Repeal {GovernmentPresentation.NameOf(target)}?",
            description = "The area will have No Policy. Repealing does not refund policy points and requires the normal council approval.",
            icon = target.icon,
            confirmLabel = "Repeal",
            onConfirm = () => { PolicyManager.Instance.RevokePolicy(civ, target); RefreshAllVisible(); },
        });
    }

    private static void AddPolicyRelations(List<PoliticalEffectLine> lines, PolicyData policy)
    {
        AddPolicyRelation(lines, "Requires", policy.requiredPolicies);
        AddPolicyRelation(lines, "Conflicts", policy.incompatiblePolicies);
        AddPolicyRelation(lines, "Supersedes", policy.supersedesPolicies);
    }

    private static void AddPolicyRelation(List<PoliticalEffectLine> lines, string label, PolicyData[] policies)
    {
        var names = policies?.Where(p => p != null).Select(GovernmentPresentation.NameOf).ToList();
        if (names != null && names.Count > 0)
            lines.Add(new PoliticalEffectLine { label = label, value = string.Join(", ", names) });
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
    private void HandleGovernmentChanged(Civilization c, GovernmentData g)
    {
        if (c == civ) PolicyManager.Instance?.RevalidateActivePolicies(c);
        QueueRefresh();
    }
    private void HandlePolicyPointsChanged(int total, int delta) => QueueRefresh();
    private void HandleGovernorAssignmentChanged(Civilization c, City city) { if (c == civ) QueueRefresh(); }
    private void HandleCivilizationChanged(Civilization c) { if (c == civ) QueueRefresh(); }
    private void HandleContractChanged(Civilization overlord, Civilization subject)
    {
        if (overlord == civ || subject == civ) QueueRefresh();
    }
}
