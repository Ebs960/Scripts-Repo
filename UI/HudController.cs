using System.Collections;
using System.Linq;
using UnityEngine;

public class HudController : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject hudRoot;

    [Header("HUD Sections")]
    [SerializeField] private HudTopBar topBar;
    [SerializeField] private HudPanelRouter panelRouter;
    [SerializeField] private HudScienceProgress scienceProgress;
    [SerializeField] private HudCultureProgress cultureProgress;
    [SerializeField] private HudCrisisMissionDropdown crisisMissionDropdown;
    [SerializeField] private HudBreakdownService breakdownService;
    [SerializeField] private HudGovernmentDropdown governmentDropdown;
    [SerializeField] private HudPoliticalAffairsDropdown politicalAffairsDropdown;

    private Civilization currentCiv;
    private bool subscribedToTurnManager;

    private void Awake()
    {
        if (hudRoot == null)
            hudRoot = gameObject;

        AutoFindMissingReferences();
    }

    private void OnEnable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnGameStarted += HandleGameStarted;

        StartCoroutine(DelayedInitialBind());
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnGameStarted -= HandleGameStarted;

        if (TurnManager.Instance != null && subscribedToTurnManager)
            TurnManager.Instance.OnTurnChanged -= HandleTurnChanged;

        subscribedToTurnManager = false;
    }

    private IEnumerator DelayedInitialBind()
    {
        yield return null;
        TrySubscribeToTurnManager();
        ResolveAndBindPlayerCivilization();
    }

    private void HandleGameStarted()
    {
        TrySubscribeToTurnManager();
        ResolveAndBindPlayerCivilization();
    }

    private void TrySubscribeToTurnManager()
    {
        if (subscribedToTurnManager) return;
        if (TurnManager.Instance == null) return;

        TurnManager.Instance.OnTurnChanged += HandleTurnChanged;
        subscribedToTurnManager = true;
    }

    private void HandleTurnChanged(Civilization civ, int turn)
    {
        // Keep HUD bound to the human/player civilization.
        // If the turn event is for another civ, refresh the existing player HUD instead of switching the UI to AI data.
        if (civ != null && civ.isPlayerControlled)
            currentCiv = civ;

        if (currentCiv == null)
            currentCiv = ResolvePlayerCivilization();

        RefreshAll();
    }

    private void ResolveAndBindPlayerCivilization()
    {
        currentCiv = ResolvePlayerCivilization();
        RefreshAll();
    }

    private Civilization ResolvePlayerCivilization()
    {
        var allCivs = CivilizationManager.Instance?.GetAllCivs();
        if (allCivs != null)
        {
            var player = allCivs.FirstOrDefault(c => c != null && c.isPlayerControlled);
            if (player != null)
                return player;
        }

        var currentTurnCiv = TurnManager.Instance?.GetCurrentCivilization();
        if (currentTurnCiv != null && currentTurnCiv.isPlayerControlled)
            return currentTurnCiv;

        return currentCiv;
    }

    public void RefreshAll()
    {
        bool showHud = !IsLoadingActive();

        if (hudRoot != null)
            hudRoot.SetActive(showHud);

        if (!showHud || currentCiv == null)
            return;

        int round = TurnManager.Instance != null ? TurnManager.Instance.round : 0;

        if (breakdownService != null)
            breakdownService.SetCurrentCivilization(currentCiv, round);

        if (panelRouter != null)
        {
            panelRouter.SetCurrentCivilization(currentCiv);
            panelRouter.RefreshLayerDropdown();
        }

        if (topBar != null)
            topBar.Bind(currentCiv);

        if (scienceProgress != null)
            scienceProgress.Bind(currentCiv);

        if (cultureProgress != null)
            cultureProgress.Bind(currentCiv);

        if (crisisMissionDropdown != null)
            crisisMissionDropdown.SetCurrentCivilization(currentCiv);

        if (governmentDropdown != null)
            governmentDropdown.Bind(currentCiv);

        if (politicalAffairsDropdown != null)
            politicalAffairsDropdown.Bind(currentCiv);
    }

    private bool IsLoadingActive()
    {
        return LoadingPanelController.Instance != null && LoadingPanelController.Instance.IsUiBlocked;
    }

    private void AutoFindMissingReferences()
    {
        if (topBar == null)
            topBar = GetComponentInChildren<HudTopBar>(true);

        if (panelRouter == null)
            panelRouter = GetComponentInChildren<HudPanelRouter>(true);

        if (scienceProgress == null)
            scienceProgress = GetComponentInChildren<HudScienceProgress>(true);

        if (cultureProgress == null)
            cultureProgress = GetComponentInChildren<HudCultureProgress>(true);

        if (crisisMissionDropdown == null)
            crisisMissionDropdown = GetComponentInChildren<HudCrisisMissionDropdown>(true);

        if (breakdownService == null)
            breakdownService = GetComponentInChildren<HudBreakdownService>(true);

        if (governmentDropdown == null)
            governmentDropdown = GetComponentInChildren<HudGovernmentDropdown>(true);

        if (politicalAffairsDropdown == null)
            politicalAffairsDropdown = GetComponentInChildren<HudPoliticalAffairsDropdown>(true);
    }
}
