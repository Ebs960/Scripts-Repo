using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Shell and router for the unified, empire-wide Government screen (Overview, Government, Policies, Governors,
/// Vassals, Politics). It owns tab switching, the shared confirmation dialog and event-driven refresh; each tab
/// controller binds its own authored UI. Pre-existing serialized fields are kept so the current prefab still loads;
/// the old runtime-built lists live in GovernmentPanel.Legacy.cs and are used only when no tab shell is wired.
/// </summary>
public partial class GovernmentPanel : MonoBehaviour
{
    public static GovernmentPanel Instance { get; private set; }

    [Serializable]
    public class TabEntry
    {
        public GovernmentTab tab;
        public GovernmentNavButtonUI navButton;
        public GameObject root;
        public GovernmentTabBase controller;
    }

    [Header("Root")]
    public GameObject panelRoot;
    public TextMeshProUGUI headerText;
    [Tooltip("Close button authored in the panel.")]
    public Button closeButton;

    [Header("Tab Shell (authored)")]
    [SerializeField] private List<TabEntry> tabs = new List<TabEntry>();
    [SerializeField] private TMP_Text subtitleText;
    [Tooltip("The one shared confirmation dialog used by every tab.")]
    [SerializeField] private PoliticalConfirmDialog confirmDialogUI;
    [SerializeField] private GovernmentTab defaultTab = GovernmentTab.Overview;

    private Civilization civ;
    private Civilization subscribedCiv;
    private GovernmentTab currentTab;
    private bool isOpen;
    private bool isShowing;
    private bool refreshQueued;
    private bool closeButtonWired;

    public Civilization Civilization => civ;
    public GovernmentTab CurrentTab => currentTab;
    public bool IsOpen => isOpen && (panelRoot == null || panelRoot.activeInHierarchy);
    public bool IsConfirmationVisible => (confirmDialogUI != null && confirmDialogUI.IsVisible) || IsLegacyConfirmVisible;

    /// <summary>True once the Government prefab has authored tab roots and controllers wired.</summary>
    public bool HasTabShell => tabs != null && tabs.Any(t => t != null && t.root != null && t.controller != null);

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (panelRoot != null)
            panelRoot.SetActive(false);
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
        if (isShowing) return;

        // Something other than ShowForCivilization activated the panel (e.g. an inspector-wired button).
        if (panelRoot != null && panelRoot.activeInHierarchy)
        {
            var target = civ != null ? civ : PoliticalActionRules.FindPlayerCivilization();
            if (target != null) ShowForCivilization(target, currentTab);
        }
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
        if (IsConfirmationVisible) CancelConfirmation();
        else Close();
    }

    private void LateUpdate()
    {
        if (!refreshQueued) return;
        refreshQueued = false;
        RefreshAllVisible();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void ShowForCivilization(Civilization civilization) => ShowForCivilization(civilization, defaultTab);

    public void ShowForCivilization(Civilization civilization, GovernmentTab tab)
    {
        civilization ??= PoliticalActionRules.FindPlayerCivilization();
        if (civilization == null) return;

        isShowing = true;
        try
        {
            if (!OpenModal()) return;
            SetCivilization(civilization);
            EnsureCloseButtonWired();
            OpenTab(tab);
        }
        finally
        {
            isShowing = false;
        }
    }

    public void OpenTab(GovernmentTab tab)
    {
        currentTab = tab;
        if (civ == null) return;

        if (!HasTabShell)
        {
            RefreshChrome();
            RefreshLegacyLists();
            return;
        }

        foreach (var entry in tabs)
            if (entry != null) GovernmentUiUtil.SetActive(entry.root, entry.tab == tab);

        RefreshChrome();
        RefreshController(FindEntry(tab));
    }

    public void RefreshCurrentTab()
    {
        if (civ == null) return;
        RefreshChrome();
        if (HasTabShell) RefreshController(FindEntry(currentTab));
        else RefreshLegacyLists();
    }

    /// <summary>Refreshes everything currently on screen: the header/nav plus the one visible tab.</summary>
    public void RefreshAllVisible()
    {
        if (!IsOpen) return;
        RefreshCurrentTab();
    }

    public void Close()
    {
        confirmDialogUI?.Hide();
        HideLegacyConfirmation();
        ClearLegacySpawned();
        UnsubscribeCivilization();
        civ = null;
        isOpen = false;
        refreshQueued = false;

        if (UIManager.Instance != null)
        {
            UIManager.Instance.HideAllPanels();
            if (UIManager.Instance.gameplayHudRoot != null)
                UIManager.Instance.gameplayHudRoot.SetActive(true);
        }
        if (panelRoot != null && panelRoot.activeSelf)
            panelRoot.SetActive(false);
    }

    /// <summary>Kept for older callers; identical to Close().</summary>
    public void Hide() => Close();

    /// <summary>Shows the single shared confirmation dialog. The action runs only if the player confirms.</summary>
    public void RequestConfirmation(PoliticalConfirmRequest request)
    {
        if (request == null) return;
        if (confirmDialogUI != null) confirmDialogUI.Show(request);
        else ShowLegacyConfirmation(request);
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private bool OpenModal()
    {
        var ui = UIManager.Instance;
        if (ui != null)
        {
            if (ui.IsBlockingModalVisible) return false;
            if (ui.GetPanel("GovernmentPanel") == null && panelRoot != null)
                ui.RegisterPanel("GovernmentPanel", panelRoot);

            ui.ShowPanel("governmentPanel");
            ui.HidePanel("gameplayHudRoot");

            // ShowPanel declines while loading or behind a modal; do not force the screen open in that case.
            var registered = ui.GetPanel("GovernmentPanel");
            if (registered != null && !registered.activeInHierarchy) return false;
        }

        if (panelRoot != null && !panelRoot.activeSelf)
            panelRoot.SetActive(true);
        isOpen = true;
        return true;
    }

    private void CancelConfirmation()
    {
        if (confirmDialogUI != null && confirmDialogUI.IsVisible) confirmDialogUI.Cancel();
        else CancelLegacyConfirmation();
    }

    private TabEntry FindEntry(GovernmentTab tab)
        => tabs?.FirstOrDefault(t => t != null && t.tab == tab);

    private void RefreshController(TabEntry entry)
    {
        if (entry?.controller == null) return;
        entry.controller.Bind(civ, this);
        entry.controller.Refresh();
    }

    private void RefreshChrome()
    {
        if (civ == null) return;
        GovernmentUiUtil.SetText(headerText, $"{GovernmentPresentation.NameOf(civ)} - Government");
        GovernmentUiUtil.SetText(subtitleText, civ.currentGovernment != null ? GovernmentPresentation.NameOf(civ.currentGovernment) : "No government");

        if (tabs == null) return;
        foreach (var entry in tabs)
        {
            if (entry == null || entry.navButton == null) continue;
            var tab = entry.tab;
            entry.navButton.Bind(TabLabel(tab), tab == currentTab, () => OpenTab(tab));
        }
    }

    private string TabLabel(GovernmentTab tab)
    {
        switch (tab)
        {
            case GovernmentTab.Overview: return "Overview";
            case GovernmentTab.Government: return "Government";
            case GovernmentTab.Policies: return "Policies";
            case GovernmentTab.Governors: return GovernmentPresentation.GetGovernorTitlePlural(civ);
            case GovernmentTab.Vassals: return "Vassals";
            case GovernmentTab.Politics: return "Politics";
            default: return tab.ToString();
        }
    }

    private void EnsureCloseButtonWired()
    {
        if (closeButton == null) return;
        if (!closeButtonWired)
        {
            closeButton.onClick.RemoveListener(Close);
            closeButton.onClick.AddListener(Close);
            closeButtonWired = true;
        }
        UIManager.Instance?.WireUIInteractions(closeButton.gameObject);
    }

    // ── Event-driven refresh ──────────────────────────────────────────────────

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

    // Refreshes are coalesced into one LateUpdate pass so a burst of events (e.g. a government change) rebuilds once.
    private void QueueRefresh()
    {
        if (IsOpen) refreshQueued = true;
    }

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
