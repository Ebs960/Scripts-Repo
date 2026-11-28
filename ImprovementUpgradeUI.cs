// Assets/Scripts/UI/ImprovementUpgradeUI.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;

/// <summary>
/// Improvement Management panel. The layout, artwork and every control are saved in the
/// "Improvement Upgrade UI" prefab; this class fills them from the existing improvement systems
/// (ImprovementManager, ImprovementUpgradeRules, ImprovementLaborRules) and never changes their rules.
/// It lives on the prefab root so it keeps working while the panel itself is hidden.
/// </summary>
public class ImprovementUpgradeUI : MonoBehaviour
{
    [Serializable]
    public sealed class StatFrame
    {
        public GameObject root;
        public TextMeshProUGUI label;
        public TextMeshProUGUI value;
        public SimpleTooltipTarget tooltip;
    }

    [Header("Panel")]
    [SerializeField] private GameObject upgradePanel;
    [SerializeField] private Button closeButton;

    [Header("Header")]
    [SerializeField] private TextMeshProUGUI improvementNameText;
    [SerializeField] private TextMeshProUGUI improvementTypeText;
    [SerializeField] private TextMeshProUGUI improvementStatusText;

    [Header("Sidebar")]
    [SerializeField] private Image improvementIconImage;
    [SerializeField] private TextMeshProUGUI locationText;
    [SerializeField] private TextMeshProUGUI sidebarStatusText;
    [SerializeField] private TextMeshProUGUI laborSummaryText;
    [SerializeField] private TextMeshProUGUI dismantleRefundText;

    [Header("Yields and Statistics")]
    [SerializeField] private TextMeshProUGUI yieldsHeadingText;
    [Tooltip("Saved status frames. Only as many as are needed are shown.")]
    [SerializeField] private StatFrame[] statFrames;
    [Tooltip("Notes under the status frames (resources produced, damage).")]
    [SerializeField] private TextMeshProUGUI improvementYieldsText;

    [Header("Construction")]
    [SerializeField] private GameObject constructionSection;
    [SerializeField] private TextMeshProUGUI constructionText;
    [SerializeField] private Image constructionProgressFill;
    [SerializeField] private TextMeshProUGUI constructionProgressLabel;

    [Header("Upgrades and Labor")]
    [SerializeField] private ScrollRect contentScroll;
    [SerializeField] private TextMeshProUGUI installedUpgradesText;
    [SerializeField] private Transform upgradeButtonContainer;
    [SerializeField] private GameObject upgradeButtonPrefab;
    [Tooltip("Section prefab used to group options into slot-based rows.")]
    [SerializeField] private GameObject upgradeSlotSectionPrefab;
    [SerializeField] private TextMeshProUGUI emptyStateText;

    [Header("Stored Units")]
    [SerializeField] private GameObject storageSection;
    [SerializeField] private Transform storedUnitsContainer;
    [SerializeField] private GameObject storedUnitButtonPrefab;
    [SerializeField] private TextMeshProUGUI capacityText;

    [Header("Actions")]
    [SerializeField] private Button manageLaborButton;
    [SerializeField] private Button manageSpecialistsButton;
    [SerializeField] private Button openCityStorageButton;
    [SerializeField] private Button dismantleButton;
    private Button launchSiloMissileButton;
    private RectTransform actionsRect;
    private float actionsBaseHeight;
    private float contentBaseBottom;

    [Header("Slide Settings")]
    [SerializeField] private float slideDuration = 0.18f;
    [SerializeField] private float offscreenPadding = 12f;

    private ImprovementData currentImprovement;
    private int currentTileIndex = -1;
    private int currentPlanetIndex = -1;
    private Civilization currentCiv;
    private bool constructionMode;
    private bool hasTarget;
    private bool hiding;
    private bool dirty;
    private bool referencesValid;
    private bool reportedFrameOverflow;

    private readonly List<GameObject> upgradeButtons = new List<GameObject>();
    private readonly List<GameObject> upgradeSlotSections = new List<GameObject>();
    private readonly List<GameObject> storedUnitButtons = new List<GameObject>();
    private readonly List<(string label, string value, string tooltip)> statEntries = new List<(string, string, string)>();
    private RectTransform laborSection;

    private RectTransform panelRect;
    private Vector2 targetAnchoredPos;
    private Coroutine slideCoroutine;

    private TileSystem eventTileSystem;
    private int eventPlanetIndex = int.MinValue;
    private ImprovementManager subscribedManager;
    private GameEventManager subscribedEvents;
    private TurnManager subscribedTurns;
    private GameManager subscribedGame;
    private Civilization subscribedCiv;

    private bool IsVisible => hasTarget && !hiding && upgradePanel != null && upgradePanel.activeSelf;

    /// <summary>True while the panel is showing the given tile.</summary>
    public bool IsShowing(int tileIndex, int planetIndex)
    {
        return IsVisible && tileIndex == currentTileIndex && planetIndex == currentPlanetIndex;
    }

    // ------------------------------------------------------------------ lifecycle

    private void Awake()
    {
        referencesValid = ValidateReferences();
        if (closeButton != null) closeButton.onClick.AddListener(HidePanel);
        if (manageLaborButton != null) manageLaborButton.onClick.AddListener(ScrollToLabor);
        if (manageSpecialistsButton != null) manageSpecialistsButton.onClick.AddListener(OpenCitizenAssignment);
        if (openCityStorageButton != null) openCityStorageButton.onClick.AddListener(OpenCityStorage);
        if (dismantleButton != null) dismantleButton.onClick.AddListener(OnDismantleClicked);
        if (openCityStorageButton != null)
        {
            launchSiloMissileButton = Instantiate(openCityStorageButton, openCityStorageButton.transform.parent, false);
            launchSiloMissileButton.name = "Launch Silo Missile Button";
            launchSiloMissileButton.onClick.RemoveAllListeners();
            launchSiloMissileButton.onClick.AddListener(OpenSiloMissiles);
            var label = launchSiloMissileButton.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = "Missiles";
            launchSiloMissileButton.gameObject.SetActive(false);
            actionsRect = launchSiloMissileButton.transform.parent as RectTransform;
            if (actionsRect != null) actionsBaseHeight = actionsRect.rect.height;
            if (contentScroll != null) contentBaseBottom = ((RectTransform)contentScroll.transform).offsetMin.y;
        }

        if (upgradePanel != null && upgradePanel != gameObject)
        {
            panelRect = upgradePanel.GetComponent<RectTransform>();
            if (panelRect != null) targetAnchoredPos = panelRect.anchoredPosition;
            upgradePanel.SetActive(false);
        }
    }

    private bool ValidateReferences()
    {
        var missing = new List<string>();
        if (upgradePanel == null) missing.Add(nameof(upgradePanel));
        else if (upgradePanel == gameObject) missing.Add(nameof(upgradePanel) + " (must be a child of this object, not the object itself)");
        if (closeButton == null) missing.Add(nameof(closeButton));
        if (improvementNameText == null) missing.Add(nameof(improvementNameText));
        if (contentScroll == null) missing.Add(nameof(contentScroll));
        if (upgradeButtonContainer == null) missing.Add(nameof(upgradeButtonContainer));
        if (upgradeButtonPrefab == null) missing.Add(nameof(upgradeButtonPrefab));
        else if (upgradeButtonPrefab.GetComponent<ImprovementUpgradeOptionRow>() == null) missing.Add(nameof(upgradeButtonPrefab) + " (needs ImprovementUpgradeOptionRow)");
        if (statFrames == null || statFrames.Length == 0) missing.Add(nameof(statFrames));
        if (missing.Count == 0) return true;
        Debug.LogError("[ImprovementUpgradeUI] The Improvement Upgrade UI prefab is missing required references, so the panel cannot open: "
                       + string.Join(", ", missing), this);
        return false;
    }

    private void OnDisable()
    {
        if (slideCoroutine != null) { StopCoroutine(slideCoroutine); slideCoroutine = null; }
        if (hasTarget) DoClearAndHide();
        UnsubscribeGlobalEvents();
        UnsubscribeFromTileSystem();
    }

    private void OnDestroy()
    {
        UnsubscribeGlobalEvents();
        UnsubscribeFromTileSystem();
        SubscribeToCiv(null);
    }

    private void Update()
    {
        if (!IsVisible) return;

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame &&
            !ConfirmationDialogService.IsVisible &&
            (MissileLaunchMode.Instance == null || !MissileLaunchMode.Instance.BlocksMapInput))
        {
            HidePanel();
            return;
        }

        // The panel describes one tile on one planet; it must not linger over a different planet.
        if (GameManager.Instance != null && GameManager.Instance.currentPlanetIndex != currentPlanetIndex)
        {
            HidePanel();
            return;
        }

        if (launchSiloMissileButton != null && currentImprovement != null)
        {
            bool showMissiles = !constructionMode && currentImprovement.isMissileSilo &&
                MissileManager.Instance != null && MissileManager.Instance.SiloHasMissiles(currentPlanetIndex, currentTileIndex);
            if (launchSiloMissileButton.gameObject.activeSelf != showMissiles)
            {
                launchSiloMissileButton.gameObject.SetActive(showMissiles);
                ResizeMissileActions();
            }
        }
        if (dirty) RefreshAll(false);
    }

    // ------------------------------------------------------------------ opening

    /// <summary>Opens (or retargets) the panel for a completed improvement owned by the player.</summary>
    public bool ShowUpgradePanel(ImprovementData improvement, int tileIndex, Civilization civ, int planetIndex = -1)
    {
        if (improvement == null || civ == null)
        {
            Debug.LogWarning("[ImprovementUpgradeUI] ShowUpgradePanel called with a null improvement or civilization.", this);
            return false;
        }
        return Open(improvement, tileIndex, civ, planetIndex, false);
    }

    /// <summary>Opens the panel for a construction site owned by the player. Returns false when nothing is being built there.</summary>
    public bool ShowConstructionPanel(int tileIndex, Civilization civ, int planetIndex = -1)
    {
        var manager = ImprovementManager.Instance;
        if (manager == null || civ == null) return false;
        if (!manager.TryGetConstructionStatus(tileIndex, ResolvePlanet(planetIndex), out var status)) return false;
        return Open(status.Improvement, tileIndex, civ, planetIndex, true);
    }

    private static int ResolvePlanet(int planetIndex)
    {
        return planetIndex >= 0 ? planetIndex : (GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0);
    }

    private bool Open(ImprovementData improvement, int tileIndex, Civilization civ, int planetIndex, bool construction)
    {
        // Management actions are for the player's own improvements only.
        if (!civ.isPlayerControlled) return false;

        // The scene keeps this object switched off until it is first needed.
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        if (!gameObject.activeInHierarchy)
        {
            Debug.LogError("[ImprovementUpgradeUI] The panel cannot open because one of its parent objects is inactive.", this);
            return false;
        }
        if (!referencesValid) return false;

        int planet = ResolvePlanet(planetIndex);
        bool wasShowing = IsVisible;

        currentImprovement = improvement;
        currentTileIndex = tileIndex;
        currentPlanetIndex = planet;
        currentCiv = civ;
        constructionMode = construction;
        hasTarget = true;
        hiding = false;

        SubscribeGlobalEvents();
        SubscribeToCiv(civ);
        SubscribeToTileSystemForPlanet(planet);

        // Activate before filling so the layout groups can size the new rows straight away.
        bool wasActive = upgradePanel.activeSelf;
        upgradePanel.SetActive(true);
        if (panelRect != null && !wasActive) panelRect.anchoredPosition = HiddenAnchoredPos;

        RefreshAll(true);
        if (!hasTarget || hiding) return false; // the tile no longer holds anything to manage

        if (!wasShowing || slideCoroutine != null) StartSlide(targetAnchoredPos, null);
        return true;
    }

    public void HidePanel()
    {
        if (!hasTarget) return;
        hiding = true;
        TooltipSystem.Instance?.RequestHideTooltip();
        if (panelRect == null || !isActiveAndEnabled || upgradePanel == null || !upgradePanel.activeSelf)
        {
            DoClearAndHide();
            return;
        }
        StartSlide(HiddenAnchoredPos, DoClearAndHide);
    }

    private Vector2 HiddenAnchoredPos => targetAnchoredPos + new Vector2(panelRect.rect.width + offscreenPadding, 0f);

    private void StartSlide(Vector2 to, Action onComplete)
    {
        if (slideCoroutine != null) StopCoroutine(slideCoroutine);
        if (panelRect == null) { onComplete?.Invoke(); return; }
        slideCoroutine = StartCoroutine(Slide(panelRect.anchoredPosition, to, slideDuration, onComplete));
    }

    private IEnumerator Slide(Vector2 from, Vector2 to, float duration, Action onComplete)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float f = Mathf.Clamp01(t / duration);
            panelRect.anchoredPosition = Vector2.LerpUnclamped(from, to, f * f * (3f - 2f * f));
            yield return null;
        }
        panelRect.anchoredPosition = to;
        slideCoroutine = null;
        onComplete?.Invoke();
    }

    private void DoClearAndHide()
    {
        if (upgradePanel != null && upgradePanel != gameObject) upgradePanel.SetActive(false);
        if (panelRect != null) panelRect.anchoredPosition = targetAnchoredPos;
        UnsubscribeFromTileSystem();
        SubscribeToCiv(null);
        ClearUpgradeButtons();
        ClearStoredUnitButtons();
        currentImprovement = null;
        currentTileIndex = -1;
        currentPlanetIndex = -1;
        currentCiv = null;
        constructionMode = false;
        hasTarget = false;
        hiding = false;
        dirty = false;
    }

    // ------------------------------------------------------------------ events

    private void SubscribeGlobalEvents()
    {
        var manager = ImprovementManager.Instance;
        if (manager != subscribedManager)
        {
            if (subscribedManager != null)
            {
                subscribedManager.OnImprovementBuilt -= HandleImprovementBuilt;
                subscribedManager.OnImprovementRemoved -= HandleImprovementRemoved;
                subscribedManager.OnImprovementStateChanged -= HandleImprovementStateChanged;
            }
            subscribedManager = manager;
            if (manager != null)
            {
                manager.OnImprovementBuilt += HandleImprovementBuilt;
                manager.OnImprovementRemoved += HandleImprovementRemoved;
                manager.OnImprovementStateChanged += HandleImprovementStateChanged;
            }
        }

        var events = GameEventManager.Instance;
        if (events != subscribedEvents)
        {
            if (subscribedEvents != null)
            {
                subscribedEvents.OnWorkerAssignedToJob -= HandleWorkerAssignment;
                subscribedEvents.OnWorkerUnassignedFromJob -= HandleWorkerAssignment;
            }
            subscribedEvents = events;
            if (events != null)
            {
                events.OnWorkerAssignedToJob += HandleWorkerAssignment;
                events.OnWorkerUnassignedFromJob += HandleWorkerAssignment;
            }
        }

        var turns = TurnManager.Instance;
        if (turns != subscribedTurns)
        {
            if (subscribedTurns != null) subscribedTurns.OnRoundStarted -= HandleRoundStarted;
            subscribedTurns = turns;
            if (turns != null) turns.OnRoundStarted += HandleRoundStarted;
        }

        var game = GameManager.Instance;
        if (game != subscribedGame)
        {
            if (subscribedGame != null) subscribedGame.OnPlanetReady -= HandlePlanetReady;
            subscribedGame = game;
            if (game != null) game.OnPlanetReady += HandlePlanetReady;
        }
    }

    private void UnsubscribeGlobalEvents()
    {
        if (subscribedManager != null)
        {
            subscribedManager.OnImprovementBuilt -= HandleImprovementBuilt;
            subscribedManager.OnImprovementRemoved -= HandleImprovementRemoved;
            subscribedManager.OnImprovementStateChanged -= HandleImprovementStateChanged;
            subscribedManager = null;
        }
        if (subscribedEvents != null)
        {
            subscribedEvents.OnWorkerAssignedToJob -= HandleWorkerAssignment;
            subscribedEvents.OnWorkerUnassignedFromJob -= HandleWorkerAssignment;
            subscribedEvents = null;
        }
        if (subscribedTurns != null) { subscribedTurns.OnRoundStarted -= HandleRoundStarted; subscribedTurns = null; }
        if (subscribedGame != null) { subscribedGame.OnPlanetReady -= HandlePlanetReady; subscribedGame = null; }
    }

    // Research, policies and the treasury decide which options are unlocked and affordable.
    private void SubscribeToCiv(Civilization civ)
    {
        if (civ == subscribedCiv) return;
        if (subscribedCiv != null)
        {
            subscribedCiv.OnGoldChanged -= HandleCivAmountChanged;
            subscribedCiv.OnResourceChanged -= HandleCivResourceChanged;
            subscribedCiv.OnUnlocksChanged -= MarkDirty;
            subscribedCiv.OnPolicyAdopted -= HandleCivPolicyChanged;
            subscribedCiv.OnPolicyRevoked -= HandleCivPolicyChanged;
        }
        subscribedCiv = civ;
        if (civ != null)
        {
            civ.OnGoldChanged += HandleCivAmountChanged;
            civ.OnResourceChanged += HandleCivResourceChanged;
            civ.OnUnlocksChanged += MarkDirty;
            civ.OnPolicyAdopted += HandleCivPolicyChanged;
            civ.OnPolicyRevoked += HandleCivPolicyChanged;
        }
    }

    private void MarkDirty() { dirty = true; }
    private void HandleCivAmountChanged(int amount, int delta) { dirty = true; }
    private void HandleCivResourceChanged(ResourceData resource, int amount) { dirty = true; }
    private void HandleCivPolicyChanged(Civilization civ, PolicyData policy) { dirty = true; }
    private void HandleRoundStarted(int round) { dirty = true; }

    private bool IsCurrentTile(int tileIndex, int planetIndex)
    {
        return hasTarget && tileIndex == currentTileIndex && planetIndex == currentPlanetIndex;
    }

    private void HandleImprovementBuilt(Civilization owner, ImprovementData data, int tileIndex, int planetIndex)
    {
        if (!IsCurrentTile(tileIndex, planetIndex)) return;
        // Construction finished: carry on as the management panel for the completed improvement.
        constructionMode = false;
        currentImprovement = data;
        dirty = true;
    }

    private void HandleImprovementRemoved(Civilization owner, ImprovementData data, int tileIndex, int planetIndex,
        ImprovementManager.ImprovementRemovalReason reason)
    {
        if (IsCurrentTile(tileIndex, planetIndex)) HidePanel();
    }

    private void HandleImprovementStateChanged(int tileIndex, int planetIndex)
    {
        if (IsCurrentTile(tileIndex, planetIndex)) dirty = true;
    }

    private void HandleWorkerAssignment(GameEventManager.WorkerAssignmentEventArgs args)
    {
        if (args != null && IsCurrentTile(args.TileIndex, args.PlanetIndex)) dirty = true;
    }

    // A planet finishing generation or loading replaces its tiles, so anything shown for it is stale.
    private void HandlePlanetReady(int planetIndex)
    {
        if (hasTarget && planetIndex == currentPlanetIndex) HidePanel();
    }

    private void SubscribeToTileSystemForPlanet(int planetIndex)
    {
        if (eventTileSystem != null && eventPlanetIndex == planetIndex) return;
        UnsubscribeFromTileSystem();
        eventPlanetIndex = planetIndex;
        eventTileSystem = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (eventTileSystem != null)
            eventTileSystem.OnTileClicked += HandleAnyTileClicked;
    }

    private void UnsubscribeFromTileSystem()
    {
        if (eventTileSystem != null) eventTileSystem.OnTileClicked -= HandleAnyTileClicked;
        eventTileSystem = null;
        eventPlanetIndex = int.MinValue;
    }

    // Clicking another tile closes the panel; if that tile holds an improvement its own click reopens it.
    private bool HandleAnyTileClicked(int clickedTileIndex, Vector3 worldPos)
    {
        if (!IsVisible) return false;
        if (InputManager.Instance != null && InputManager.Instance.IsPointerOverUI()) return false;
        if (clickedTileIndex != currentTileIndex) HidePanel();
        return false; // never consume
    }

    // ------------------------------------------------------------------ refresh

    private HexTileData GetCurrentTileData()
    {
        var ts = TileSystem.GetForPlanet(currentPlanetIndex) ?? TileSystem.Instance;
        return ts != null ? ts.GetTileData(currentTileIndex) : null;
    }

    private static ImprovementInstance GetInstance(HexTileData tileData)
    {
        return tileData != null && tileData.improvementInstanceObject != null
            ? tileData.improvementInstanceObject.GetComponent<ImprovementInstance>()
            : null;
    }

    private void RefreshAll(bool resetScroll)
    {
        dirty = false;
        if (!hasTarget || currentCiv == null) return;

        var manager = ImprovementManager.Instance;
        var tileData = GetCurrentTileData();
        ImprovementManager.ConstructionStatus construction = default;
        bool building = manager != null && manager.TryGetConstructionStatus(currentTileIndex, currentPlanetIndex, out construction);

        if (constructionMode && !building)
        {
            if (tileData != null && tileData.improvement != null) constructionMode = false;
            else { HidePanel(); return; }
        }

        if (constructionMode)
        {
            currentImprovement = construction.Improvement;
        }
        else
        {
            // Removed, or taken by another civilization: nothing here is the player's to manage.
            if (tileData == null || tileData.improvement == null ||
                (tileData.improvementOwner != null && tileData.improvementOwner != currentCiv))
            {
                HidePanel();
                return;
            }
            currentImprovement = tileData.improvement;
        }

        var instance = constructionMode ? null : GetInstance(tileData);
        City city = manager != null ? manager.FindAdministeringCity(currentCiv, currentTileIndex, currentPlanetIndex) : null;

        RefreshHeaderAndSidebar(tileData, instance, city, building, construction, manager);
        RefreshStatFrames(tileData, instance);
        RefreshConstruction(building, construction);
        RefreshOptions(tileData, instance);
        RefreshStorage(instance);
        RefreshActions(instance, city);

        if (contentScroll != null && contentScroll.content != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentScroll.content);
            if (resetScroll)
            {
                contentScroll.StopMovement();
                contentScroll.verticalNormalizedPosition = 1f;
            }
        }
    }

    private static void SetText(TextMeshProUGUI text, string value)
    {
        if (text != null) text.text = value;
    }

    private static string Signed(int value) => value > 0 ? "+" + value : value.ToString();

    private static string DescribeType(ImprovementData data)
    {
        var parts = new List<string>();
        if (data.isRoad) parts.Add("Road");
        if (data.isFort) parts.Add("Fortification");
        if (data.isTrap) parts.Add("Trap");
        if (data.isMissileSilo) parts.Add("Missile Silo");
        if (data.isShelter) parts.Add("Shelter");
        if (data.isPort) parts.Add("Port");
        if (data.isOrbitalImprovement) parts.Add("Orbital");
        if (data.isUnderwaterImprovement) parts.Add("Underwater");
        return parts.Count == 0 ? "Improvement" : string.Join(" • ", parts);
    }

    private void RefreshHeaderAndSidebar(HexTileData tileData, ImprovementInstance instance, City city, bool building,
        ImprovementManager.ConstructionStatus construction, ImprovementManager manager)
    {
        SetText(improvementNameText, currentImprovement.improvementName);
        SetText(improvementTypeText, DescribeType(currentImprovement));

        string status;
        if (constructionMode)
            status = $"Under construction  {Mathf.RoundToInt(construction.Progress * 100f)}%";
        else if (tileData.isDisasterDamaged)
            status = manager != null && manager.IsRepairJobActive(currentTileIndex, currentPlanetIndex) ? "Damaged - being repaired" : "Damaged";
        else if (instance != null && instance.IsFortNeutralized)
            status = "Neutralized";
        else
            status = "Operational";
        SetText(improvementStatusText, status);

        if (improvementIconImage != null)
        {
            improvementIconImage.sprite = currentImprovement.GetIcon(currentCiv);
            improvementIconImage.enabled = improvementIconImage.sprite != null;
        }

        var location = new List<string>();
        location.Add(city != null ? city.cityName : "No administering city");
        if (tileData != null) location.Add(tileData.biome.ToString());
        var planets = GameManager.Instance != null ? GameManager.Instance.GetPlanetData() : null;
        if (planets != null && planets.TryGetValue(currentPlanetIndex, out var planet) && planet != null && !string.IsNullOrEmpty(planet.planetName))
            location.Add(planet.planetName);
        location.Add($"Tile {currentTileIndex}");
        SetText(locationText, string.Join("\n", location));

        var statusLines = new List<string> { status };
        if (!constructionMode && building) statusLines.Add($"Building {construction.Improvement.improvementName}");
        if (!constructionMode && tileData.isDisasterDamaged) statusLines.Add("Yields nothing until repaired");
        SetText(sidebarStatusText, string.Join("\n", statusLines));

        string labor;
        if (constructionMode)
            labor = $"{construction.AssignedWorkers} worker{(construction.AssignedWorkers == 1 ? "" : "s")} assigned";
        else if (!currentImprovement.usesLaborTypes)
            labor = "Does not use labor";
        else if (instance == null || instance.currentLaborType == null)
            labor = "No labor type assigned";
        else
        {
            var type = instance.currentLaborType;
            var lines = new List<string> { type.laborName, $"Output x{type.outputMultiplier:0.##}" };
            if (instance.GetLaborGoldUpkeepPerTurn() > 0) lines.Add($"Upkeep {instance.GetLaborGoldUpkeepPerTurn()} Gold");
            if (type.unhappinessPerTurn != 0) lines.Add($"Unhappiness {Signed(type.unhappinessPerTurn)}");
            labor = string.Join("\n", lines);
        }
        SetText(laborSummaryText, labor);
    }

    private void AddYieldStat(string label, int baseValue, int bonusValue, int laborValue, int upgradeValue, int total, ImprovementYieldBreakdown yield)
    {
        if (baseValue == 0 && bonusValue == 0 && upgradeValue == 0 && total == 0) return;
        var lines = new List<string> { $"Base: {Signed(baseValue)}" };
        if (bonusValue != baseValue) lines.Add($"With civilization bonuses: {Signed(bonusValue)}");
        if (!Mathf.Approximately(yield.LaborMultiplier, 1f)) lines.Add($"Labor x{yield.LaborMultiplier:0.##}: {Signed(laborValue)}");
        if (upgradeValue != 0) lines.Add($"Upgrades: {Signed(upgradeValue)}");
        if (yield.DisasterDamaged) lines.Add("Damaged: yields nothing until repaired.");
        lines.Add($"Effective: {Signed(total)} per turn");
        statEntries.Add((label, Signed(total), string.Join("\n", lines)));
    }

    private void AddPlannedStat(string label, int value)
    {
        if (value != 0)
            statEntries.Add((label, Signed(value), $"Base: {Signed(value)} per turn, before bonuses, labor and upgrades."));
    }

    private void RefreshStatFrames(HexTileData tileData, ImprovementInstance instance)
    {
        statEntries.Clear();
        var notes = new List<string>();

        if (constructionMode)
        {
            // Nothing is being produced yet, so only the data's own figures are known.
            SetText(yieldsHeadingText, "BASE YIELDS WHEN COMPLETE");
            var d = currentImprovement;
            AddPlannedStat("FOOD", d.foodPerTurn);
            AddPlannedStat("PRODUCTION", d.productionPerTurn);
            AddPlannedStat("GOLD", d.goldPerTurn);
            AddPlannedStat("SCIENCE", d.sciencePerTurn);
            AddPlannedStat("CULTURE", d.culturePerTurn);
            AddPlannedStat("POLICY", d.policyPointsPerTurn);
            AddPlannedStat("FAITH", d.faithPerTurn);
            if (d.resourceProductionPerTurn != null && d.resourceProductionPerTurn.Length > 0)
                notes.Add($"Will produce {ResourceCost.FormatCosts(d.resourceProductionPerTurn)} per turn.");
        }
        else
        {
            SetText(yieldsHeadingText, "YIELDS PER TURN");
            // The same calculation HexTileData.GetTotalYield uses to pay the owner.
            var y = tileData.GetImprovementYieldBreakdown();
            AddYieldStat("FOOD", y.Base.Food, y.WithBonuses.Food, y.AfterLabor.Food, y.Upgrades.Food, y.Total.Food, y);
            AddYieldStat("PRODUCTION", y.Base.Production, y.WithBonuses.Production, y.AfterLabor.Production, y.Upgrades.Production, y.Total.Production, y);
            AddYieldStat("GOLD", y.Base.Gold, y.WithBonuses.Gold, y.AfterLabor.Gold, y.Upgrades.Gold, y.Total.Gold, y);
            AddYieldStat("SCIENCE", y.Base.Science, y.WithBonuses.Science, y.AfterLabor.Science, y.Upgrades.Science, y.Total.Science, y);
            AddYieldStat("CULTURE", y.Base.Culture, y.WithBonuses.Culture, y.AfterLabor.Culture, y.Upgrades.Culture, y.Total.Culture, y);
            AddYieldStat("POLICY", y.Base.Policy, y.WithBonuses.Policy, y.AfterLabor.Policy, y.Upgrades.Policy, y.Total.Policy, y);
            AddYieldStat("FAITH", y.Base.Faith, y.WithBonuses.Faith, y.AfterLabor.Faith, y.Upgrades.Faith, y.Total.Faith, y);

            if (instance != null)
            {
                if (currentImprovement.usesLaborTypes && instance.currentLaborType != null)
                {
                    var type = instance.currentLaborType;
                    statEntries.Add(("LABOR", $"x{type.outputMultiplier:0.##}",
                        $"{type.laborName}\nMultiplies this improvement's own yields. Yields added by upgrades are not multiplied."));
                    int upkeep = instance.GetLaborGoldUpkeepPerTurn();
                    if (upkeep > 0) statEntries.Add(("UPKEEP", $"-{upkeep}", $"{type.laborName} costs {upkeep} Gold per turn."));
                }
                if (currentImprovement.isShelter)
                {
                    int stored = instance.storedUnits != null ? instance.storedUnits.Count : 0;
                    statEntries.Add(("STORAGE", $"{stored}/{instance.GetShelterCapacity()}", "Units stored here, and the most it can hold."));
                }
                if (instance.IsFort)
                    statEntries.Add(("FORT HP", $"{instance.CurrentFortHitPoints}/{instance.GetFortMaxHitPoints()}", "Current and maximum fort hit points."));

                var produced = instance.EnumerateResourceProductionPerTurn().Where(r => r != null && r.resource != null && r.amount > 0).ToArray();
                if (produced.Length > 0) notes.Add($"Produces {ResourceCost.FormatCosts(produced)} per turn.");
            }
            if (currentImprovement.isMissileSilo && MissileManager.Instance != null)
                statEntries.Add(("MISSILES", $"{MissileManager.Instance.GetSiloMissiles(currentPlanetIndex, currentTileIndex).Count}/{currentImprovement.siloMissileCapacity}",
                    "Missiles stored in this silo, and the most it can hold."));
            if (y.DisasterDamaged) notes.Add("Damaged by a disaster: this improvement yields nothing until it is repaired.");
        }

        if (statEntries.Count == 0) notes.Insert(0, constructionMode ? "No direct yields." : "This improvement has no direct yields.");

        if (statEntries.Count > statFrames.Length && !reportedFrameOverflow)
        {
            reportedFrameOverflow = true;
            Debug.LogWarning($"[ImprovementUpgradeUI] {statEntries.Count} statistics to show but the prefab has {statFrames.Length} status frames; the rest are omitted.", this);
        }
        for (int i = 0; i < statFrames.Length; i++)
        {
            var frame = statFrames[i];
            if (frame == null || frame.root == null) continue;
            bool used = i < statEntries.Count;
            frame.root.SetActive(used);
            if (!used) continue;
            SetText(frame.label, statEntries[i].label);
            SetText(frame.value, statEntries[i].value);
            if (frame.tooltip != null) frame.tooltip.Bind(statEntries[i].label, statEntries[i].tooltip);
        }

        if (improvementYieldsText != null)
        {
            improvementYieldsText.gameObject.SetActive(notes.Count > 0);
            improvementYieldsText.text = string.Join("\n", notes);
        }
    }

    private void RefreshConstruction(bool building, ImprovementManager.ConstructionStatus construction)
    {
        if (constructionSection != null) constructionSection.SetActive(building);
        if (!building) return;

        var lines = new List<string>
        {
            $"Building: {construction.Improvement.improvementName}",
            $"Work done: {construction.CompletedWork} of {construction.RequiredWork}",
            $"Work remaining: {construction.RemainingWork}",
            construction.AssignedWorkers > 0
                ? $"Assigned workers: {construction.AssignedWorkers}"
                : "No workers are assigned to this site."
        };
        SetText(constructionText, string.Join("\n", lines));
        if (constructionProgressFill != null) constructionProgressFill.fillAmount = construction.Progress;
        SetText(constructionProgressLabel, $"{Mathf.RoundToInt(construction.Progress * 100f)}%");
    }

    // ------------------------------------------------------------------ upgrades and labor

    private void RefreshOptions(HexTileData tileData, ImprovementInstance instance)
    {
        ClearUpgradeButtons();
        laborSection = null;

        if (constructionMode)
        {
            SetText(installedUpgradesText, "Upgrades and labor can be managed once construction is complete.");
            if (emptyStateText != null) emptyStateText.gameObject.SetActive(false);
            return;
        }

        var installed = currentImprovement.availableUpgrades != null && tileData.builtUpgrades != null
            ? currentImprovement.availableUpgrades
                .Where(upgrade => upgrade != null && tileData.builtUpgrades.Contains(ImprovementUpgradeRules.GetKey(upgrade)))
                .Select(upgrade => upgrade.upgradeName).ToList()
            : new List<string>();
        SetText(installedUpgradesText, installed.Count > 0 ? string.Join(", ", installed) : "None installed");

        PopulateUpgradeOptions(tileData);
        PopulateLaborTypeOptions(instance);

        if (emptyStateText != null)
        {
            emptyStateText.gameObject.SetActive(upgradeButtons.Count == 0);
            emptyStateText.text = "No upgrades or labor options are available for this improvement.";
        }
    }

    private Transform CreateSection(string slotName, int installedCount, int optionCount)
    {
        if (upgradeSlotSectionPrefab == null) return upgradeButtonContainer;
        var sectionObject = Instantiate(upgradeSlotSectionPrefab, upgradeButtonContainer);
        upgradeSlotSections.Add(sectionObject);
        var section = sectionObject.GetComponent<ImprovementUpgradeSlotSection>();
        if (section == null) return sectionObject.transform;
        section.Bind(slotName, installedCount, optionCount);
        return section.OptionContainer;
    }

    private ImprovementUpgradeOptionRow CreateRow(Transform parent)
    {
        var rowObject = Instantiate(upgradeButtonPrefab, parent);
        upgradeButtons.Add(rowObject);
        return rowObject.GetComponent<ImprovementUpgradeOptionRow>();
    }

    private void PopulateUpgradeOptions(HexTileData tileData)
    {
        if (currentImprovement.availableUpgrades == null || currentImprovement.availableUpgrades.Length == 0) return;

        var orderedUpgrades = currentImprovement.availableUpgrades.Where(upgrade => upgrade != null).ToList();
        orderedUpgrades.Sort((left, right) =>
        {
            int slotComparison = string.Compare(
                ImprovementUpgradeRules.GetDisplaySlot(left),
                ImprovementUpgradeRules.GetDisplaySlot(right),
                StringComparison.OrdinalIgnoreCase);
            return slotComparison != 0
                ? slotComparison
                : string.Compare(left.upgradeName, right.upgradeName, StringComparison.OrdinalIgnoreCase);
        });

        foreach (var slotGroup in orderedUpgrades.GroupBy(ImprovementUpgradeRules.GetDisplaySlot))
        {
            var evaluations = slotGroup
                .Select(upgrade => (upgrade, evaluation: ImprovementUpgradeRules.Evaluate(currentImprovement, tileData, upgrade, currentCiv)))
                .ToList();
            int installedCount = evaluations.Count(e => e.evaluation.Availability == ImprovementUpgradeAvailability.Installed);
            Transform optionParent = CreateSection(slotGroup.Key, installedCount, evaluations.Count);

            foreach (var (upgrade, evaluation) in evaluations)
            {
                var state = ImprovementUpgradeOptionRow.StateFor(evaluation);
                // The rules report both missing research and path conflicts as Locked; tell the player which it is.
                if (state == ImprovementOptionState.Locked &&
                    !ImprovementUpgradeRules.PassesPathRules(currentImprovement, tileData, upgrade, out _))
                    state = ImprovementOptionState.Incompatible;

                var row = CreateRow(optionParent);
                if (row == null) continue;
                var selected = upgrade;
                row.Bind(upgrade, evaluation, state, BuildRequirementText(tileData, upgrade), () => OnUpgradeSelected(selected));
            }
        }
    }

    private string UpgradeNames(IEnumerable<string> keys)
    {
        if (keys == null || currentImprovement.availableUpgrades == null) return string.Empty;
        var wanted = new HashSet<string>(keys.Where(key => !string.IsNullOrEmpty(key)));
        return string.Join(", ", currentImprovement.availableUpgrades
            .Where(candidate => candidate != null && wanted.Contains(ImprovementUpgradeRules.GetKey(candidate)))
            .Select(candidate => candidate.upgradeName));
    }

    private string BuildRequirementText(HexTileData tileData, ImprovementUpgradeData upgrade)
    {
        var lines = new List<string>();
        if (upgrade.requiredTech != null)
            lines.Add($"Requires technology: {upgrade.requiredTech.techName} ({(currentCiv.researchedTechs.Contains(upgrade.requiredTech) ? "researched" : "not researched")})");
        if (upgrade.requiredCulture != null)
            lines.Add($"Requires culture: {upgrade.requiredCulture.cultureName} ({(currentCiv.researchedCultures.Contains(upgrade.requiredCulture) ? "adopted" : "not adopted")})");
        string replaced = UpgradeNames(ImprovementUpgradeRules.GetSupersededUpgradeKeys(currentImprovement, tileData, upgrade));
        if (!string.IsNullOrEmpty(replaced)) lines.Add($"Replaces: {replaced}");
        string excluded = UpgradeNames(upgrade.mutuallyExclusiveUpgradeIds);
        if (!string.IsNullOrEmpty(excluded)) lines.Add($"Cannot be combined with: {excluded}");
        return string.Join("\n", lines);
    }

    private void PopulateLaborTypeOptions(ImprovementInstance instance)
    {
        var manager = ImprovementManager.Instance;
        if (!currentImprovement.usesLaborTypes || manager == null || manager.allLaborTypes == null) return;
        var laborTypes = manager.allLaborTypes.Where(labor => labor != null).ToList();
        if (laborTypes.Count == 0) return;

        Transform optionParent = CreateSection("Labor", instance != null && instance.currentLaborType != null ? 1 : 0, laborTypes.Count);
        if (optionParent != upgradeButtonContainer)
            laborSection = upgradeSlotSections[upgradeSlotSections.Count - 1].transform as RectTransform;

        foreach (var laborType in laborTypes)
        {
            var evaluation = ImprovementLaborRules.Evaluate(currentImprovement, instance, laborType, currentCiv);
            var state = ImprovementUpgradeOptionRow.StateFor(evaluation);
            int cost = ImprovementLaborRules.GetSwitchCost(laborType);

            var effects = new List<string> { $"Output x{laborType.outputMultiplier:0.##}" };
            if (laborType.goldUpkeepPerTurn > 0) effects.Add($"Upkeep {laborType.goldUpkeepPerTurn} Gold per turn");
            if (laborType.unhappinessPerTurn != 0) effects.Add($"{Signed(laborType.unhappinessPerTurn)} Unhappiness in the administering city");
            string effectText = string.Join(" • ", effects);
            string costText = cost > 0 ? $"Switch: {cost} Gold" : "Switch: free";
            string reason = state == ImprovementOptionState.Installed ? string.Empty : evaluation.Reason;

            var tooltip = new List<string>();
            if (!string.IsNullOrWhiteSpace(laborType.description)) tooltip.Add(laborType.description);
            tooltip.Add(effectText);
            tooltip.Add(costText);
            if (laborType.requiredPolicy != null) tooltip.Add($"Requires policy: {laborType.requiredPolicy.policyName}");
            tooltip.Add(state == ImprovementOptionState.Installed ? "Currently assigned"
                : string.IsNullOrWhiteSpace(reason) ? ImprovementUpgradeOptionRow.LabelFor(state)
                : $"{ImprovementUpgradeOptionRow.LabelFor(state)}: {reason}");

            var row = CreateRow(optionParent);
            if (row == null) continue;
            var selected = laborType;
            row.BindOption(laborType.icon, laborType.laborName, laborType.description, effectText, costText, state, reason,
                string.Join("\n\n", tooltip), () => OnLaborTypeSelected(selected));
        }
    }

    private void OnLaborTypeSelected(LaborTypeData laborType)
    {
        if (laborType == null || !hasTarget || constructionMode || ImprovementManager.Instance == null) return;
        if (!ImprovementManager.Instance.TryAssignLaborType(currentTileIndex, currentPlanetIndex, currentCiv, laborType, out string reason))
        {
            if (!string.IsNullOrEmpty(reason)) UIManager.Instance?.ShowNotification(reason);
            return;
        }
        RefreshAll(false);
    }

    private void OnUpgradeSelected(ImprovementUpgradeData upgrade)
    {
        if (upgrade == null || !hasTarget || constructionMode) return;
        var tileData = GetCurrentTileData();

        if (!ImprovementUpgradeRules.CanApplyUpgrade(currentImprovement, tileData, upgrade, currentCiv, out string reason))
        {
            if (!string.IsNullOrEmpty(reason)) UIManager.Instance?.ShowNotification(reason);
            RefreshAll(false);
            return;
        }

        string replaced = UpgradeNames(ImprovementUpgradeRules.GetSupersededUpgradeKeys(currentImprovement, tileData, upgrade));
        if (string.IsNullOrEmpty(replaced))
        {
            PurchaseUpgrade(upgrade);
            return;
        }

        // The confirmation may be answered after the panel has moved on; only act for the tile it was asked about.
        int tile = currentTileIndex, planet = currentPlanetIndex;
        ConfirmationDialogService.ShowConfirmation("REPLACE UPGRADE?",
            $"{upgrade.upgradeName} replaces {replaced}.\nThe full cost is charged: {ImprovementUpgradeOptionRow.BuildCostText(upgrade)}.",
            () => { if (IsCurrentTile(tile, planet) && !constructionMode) PurchaseUpgrade(upgrade); },
            confirmLabel: "REPLACE", owner: this);
    }

    private void PurchaseUpgrade(ImprovementUpgradeData upgrade)
    {
        if (ImprovementManager.Instance == null)
        {
            UIManager.Instance?.ShowNotification("The improvement system is unavailable.");
            return;
        }
        // Validation, payment, application and the visual change all happen in this one existing transaction.
        if (!ImprovementManager.Instance.TryPurchaseAndApplyUpgrade(currentTileIndex, currentPlanetIndex, currentCiv, upgrade, out string reason)
            && !string.IsNullOrEmpty(reason))
            UIManager.Instance?.ShowNotification(reason);
        RefreshAll(false);
    }

    private void ClearUpgradeButtons()
    {
        // Destroy is deferred to the end of the frame; hide first so the old rows do not take part in this frame's layout.
        foreach (var button in upgradeButtons)
            if (button != null) { button.SetActive(false); Destroy(button); }
        upgradeButtons.Clear();
        foreach (var section in upgradeSlotSections)
            if (section != null) { section.SetActive(false); Destroy(section); }
        upgradeSlotSections.Clear();
    }

    // ------------------------------------------------------------------ storage

    private void RefreshStorage(ImprovementInstance instance)
    {
        bool show = !constructionMode && currentImprovement.isShelter && instance != null;
        if (storageSection != null) storageSection.SetActive(show);
        ClearStoredUnitButtons();
        if (!show) return;

        int stored = instance.storedUnits != null ? instance.storedUnits.Count : 0;
        SetText(capacityText, $"Capacity: {stored}/{instance.GetShelterCapacity()}" + (stored == 0 ? "  -  no units stored" : "  -  select a unit to move it out"));
        if (storedUnitsContainer == null || storedUnitButtonPrefab == null || instance.storedUnits == null) return;

        foreach (var unit in instance.storedUnits)
        {
            if (unit == null) continue;
            var go = Instantiate(storedUnitButtonPrefab, storedUnitsContainer);
            storedUnitButtons.Add(go);
            var storedButton = go.GetComponent<StoredUnitButton>();
            if (storedButton != null) storedButton.Setup(unit, instance);
            else Debug.LogError("[ImprovementUpgradeUI] The stored unit button prefab has no StoredUnitButton component.", go);
        }
    }

    private void ClearStoredUnitButtons()
    {
        foreach (var button in storedUnitButtons)
            if (button != null) { button.SetActive(false); Destroy(button); }
        storedUnitButtons.Clear();
    }

    /// <summary>Called by StoredUnitButton after a unit leaves; refreshed next frame because the caller is one of the buttons.</summary>
    public void RefreshStoredUnits(ImprovementInstance impInstance)
    {
        dirty = true;
    }

    // ------------------------------------------------------------------ actions

    private void RefreshActions(ImprovementInstance instance, City city)
    {
        bool managing = !constructionMode;
        if (launchSiloMissileButton != null)
        {
            bool showMissiles = managing && currentImprovement.isMissileSilo &&
                MissileManager.Instance != null && MissileManager.Instance.SiloHasMissiles(currentPlanetIndex, currentTileIndex);
            launchSiloMissileButton.gameObject.SetActive(showMissiles);
            BindTooltip(launchSiloMissileButton, "Missiles", "Choose a missile stored in this silo.");
        }

        if (manageLaborButton != null)
        {
            manageLaborButton.gameObject.SetActive(managing && currentImprovement.usesLaborTypes);
            manageLaborButton.interactable = laborSection != null;
            BindTooltip(manageLaborButton, "Manage Labor", laborSection != null
                ? "Jump to this improvement's labor options."
                : "No labor types are defined.");
        }

        if (manageSpecialistsButton != null)
        {
            bool hasSlots = instance != null && instance.GetActiveRuralSpecialistSlots().Any(slot => slot != null);
            manageSpecialistsButton.gameObject.SetActive(managing && hasSlots);
            manageSpecialistsButton.interactable = city != null;
            BindTooltip(manageSpecialistsButton, "Manage Specialists", city == null
                ? "No city administers this improvement."
                : "Open citizen assignment focused on this improvement.");
        }

        if (openCityStorageButton != null)
        {
            bool hasStorage = currentImprovement.storageTypes != ImprovementStorageType.None ||
                              currentImprovement.isShelter || currentImprovement.isMissileSilo;
            openCityStorageButton.gameObject.SetActive(managing && hasStorage);
            openCityStorageButton.interactable = city != null;
            BindTooltip(openCityStorageButton, "Open Storage", city == null
                ? "No city administers this improvement."
                : "Open the administering city's Unit Storage section.");
        }

        bool showDismantle = managing && currentImprovement.canBeDismantled && ImprovementManager.Instance != null;
        string blockedReason = string.Empty;
        bool canDismantle = showDismantle &&
                            ImprovementManager.Instance.CanDismantleImprovement(currentTileIndex, currentCiv, currentPlanetIndex, out blockedReason);
        string refund = showDismantle ? BuildDismantleRefundText(currentImprovement) : string.Empty;
        if (dismantleButton != null)
        {
            dismantleButton.gameObject.SetActive(showDismantle);
            dismantleButton.interactable = canDismantle;
            BindTooltip(dismantleButton, "Dismantle", canDismantle ? $"Remove this improvement.\n{refund}" : blockedReason);
        }
        if (dismantleRefundText != null)
        {
            dismantleRefundText.gameObject.SetActive(showDismantle);
            dismantleRefundText.text = !showDismantle ? string.Empty : canDismantle ? "If dismantled\n" + refund : blockedReason;
        }
        ResizeMissileActions();
    }

    private void ResizeMissileActions()
    {
        if (actionsRect == null || contentScroll == null) return;
        var grid = actionsRect.GetComponent<GridLayoutGroup>();
        if (grid == null || grid.constraintCount <= 0) return;
        int activeCount = 0;
        foreach (Transform child in actionsRect)
            if (child.gameObject.activeSelf) activeCount++;
        int rows = Mathf.CeilToInt((float)activeCount / grid.constraintCount);
        float needed = grid.padding.vertical + rows * grid.cellSize.y + Mathf.Max(0, rows - 1) * grid.spacing.y;
        float extra = Mathf.Max(0, needed - actionsBaseHeight);
        actionsRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, actionsBaseHeight + extra);
        var scrollRect = (RectTransform)contentScroll.transform;
        var offset = scrollRect.offsetMin;
        offset.y = contentBaseBottom + extra;
        scrollRect.offsetMin = offset;
    }

    private void OpenSiloMissiles()
    {
        if (MissilePanelUI.Instance == null)
        {
            Debug.LogError("[ImprovementUpgradeUI] Missile panel is missing.", this);
            return;
        }
        MissilePanelUI.Instance.OpenForSilo(currentTileIndex, currentPlanetIndex);
    }

    private static string BuildDismantleRefundText(ImprovementData improvement)
    {
        var parts = new List<string>();
        if (improvement.dismantleGoldRefund > 0) parts.Add($"{improvement.dismantleGoldRefund} Gold");
        if (improvement.dismantleResourceRefunds != null)
        {
            foreach (var cost in improvement.dismantleResourceRefunds)
            {
                if (cost == null || cost.resource == null || cost.amount <= 0) continue;
                parts.Add($"{cost.amount} {cost.resource.resourceName}");
            }
        }
        return parts.Count > 0 ? "Refund: " + string.Join(", ", parts) : "Refund: none";
    }

    private void OnDismantleClicked()
    {
        if (!hasTarget || constructionMode || ImprovementManager.Instance == null) return;
        int tile = currentTileIndex, planet = currentPlanetIndex;
        var civ = currentCiv;
        ConfirmationDialogService.ShowConfirmation($"DISMANTLE {currentImprovement.improvementName.ToUpperInvariant()}?",
            $"This removes the improvement and its upgrades permanently.\n{BuildDismantleRefundText(currentImprovement)}",
            () =>
            {
                if (ImprovementManager.Instance == null) return;
                // The removal event closes the panel; DismantleImprovement reports its own failures.
                ImprovementManager.Instance.DismantleImprovement(tile, civ, planet);
            },
            confirmLabel: "DISMANTLE", destructive: true, owner: this);
    }

    private void ScrollToLabor()
    {
        if (laborSection == null || contentScroll == null || contentScroll.content == null) return;
        Canvas.ForceUpdateCanvases();
        var content = contentScroll.content;
        var viewport = contentScroll.viewport != null ? contentScroll.viewport : contentScroll.transform as RectTransform;
        float scrollable = content.rect.height - viewport.rect.height;
        if (scrollable <= 0f) return;
        float sectionTop = content.InverseTransformPoint(laborSection.TransformPoint(new Vector3(0f, laborSection.rect.yMax))).y;
        contentScroll.StopMovement();
        contentScroll.verticalNormalizedPosition = 1f - Mathf.Clamp01((content.rect.yMax - sectionTop) / scrollable);
    }

    private City FindOwningCity()
    {
        return ImprovementManager.Instance?.FindAdministeringCity(currentCiv, currentTileIndex, currentPlanetIndex);
    }

    private void OpenCitizenAssignment()
    {
        City city = FindOwningCity();
        if (city == null) return;
        int tileIndex = currentTileIndex;
        HidePanel();
        // The tile details live in the city panel, so open it before entering assignment mode.
        UIManager.Instance?.ShowPanel("CityPanel");
        UIManager.Instance?.GetPanel("CityPanel")?.GetComponent<CityUI>()?.ShowForCity(city);
        CityTileOverlayController.Instance?.EnterCityAssignmentMode(city, tileIndex);
    }

    private void OpenCityStorage()
    {
        City city = FindOwningCity();
        if (city == null) return;
        HidePanel();
        var cityUI = FindAnyObjectByType<CityUI>(FindObjectsInactive.Include);
        cityUI?.ShowForCityTab(city, CityUITab.UnitStorage);
    }

    private static void BindTooltip(Button button, string title, string description)
    {
        var tooltip = button.GetComponent<SimpleTooltipTarget>();
        if (tooltip != null) tooltip.Bind(title, description);
    }
}
