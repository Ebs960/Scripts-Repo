using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;

/// <summary>
/// Overlay-only UI for the mandatory world-space solar-system map. Rendering,
/// selection, hexes, planets, routes, and ships are owned by SpaceMapWorldController.
/// This class shows exactly one contextual panel on the right side for whatever the
/// controller has selected: a planet, a fleet, a single spacecraft, or a space sector.
/// </summary>
public class SpaceMapUI : MonoBehaviour
{
    [Header("Overlay References")]
    public Canvas spaceMapCanvas;
    public GameObject spaceMapPanel;
    public Button closeButton;
    public TextMeshProUGUI titleText;

    [Header("Planet Information Panel")]
    public GameObject planetInfoPanel;
    public TextMeshProUGUI planetNameText;
    public TextMeshProUGUI planetTypeText;
    public TextMeshProUGUI planetStatusText;
    public TextMeshProUGUI distanceText;
    [SerializeField] private TextMeshProUGUI planetEnvironmentText;
    [SerializeField] private TextMeshProUGUI planetMoonsText;
    [SerializeField] private TextMeshProUGUI planetCivilizationsText;
    [SerializeField] private RawImage planetPreviewImage;
    public Button travelButton;
    public Button cancelButton;

    [Header("Fleet Overview Panel")]
    [SerializeField] private GameObject fleetPanel;
    [SerializeField] private TextMeshProUGUI fleetNameText;
    [SerializeField] private TextMeshProUGUI fleetOwnerText;
    [SerializeField] private TextMeshProUGUI fleetAdmiralText;
    [SerializeField] private TextMeshProUGUI fleetLocationText;
    [SerializeField] private ScrollRect fleetRosterScroll;
    [SerializeField] private RectTransform fleetRosterContent;
    [SerializeField] private SpaceFleetRosterRowView fleetRosterRowTemplate;
    [SerializeField] private TextMeshProUGUI fleetRosterEmptyText;
    [SerializeField] private Button fleetPackButton;
    [SerializeField] private Button fleetDeployButton;
    [SerializeField] private TextMeshProUGUI fleetActionStatusText;
    [SerializeField] private Button fleetCloseButton;
    [Tooltip("Members must be within this many sectors of the fleet's lead ship to pack. Matches the deployment spread.")]
    [SerializeField] private int fleetPackGatherRadius = 3;

    [Header("Individual Spacecraft Panel")]
    [SerializeField] private GameObject shipPanel;
    [SerializeField] private TextMeshProUGUI shipNameText;
    [SerializeField] private TextMeshProUGUI shipClassText;
    [SerializeField] private Image shipIconImage;
    [SerializeField] private TextMeshProUGUI shipIconFallbackText;
    [SerializeField] private TextMeshProUGUI shipSummaryText;
    [SerializeField] private TextMeshProUGUI shipHealthText;
    [SerializeField] private TextMeshProUGUI shipAttackText;
    [SerializeField] private TextMeshProUGUI shipDefenseText;
    [SerializeField] private TextMeshProUGUI shipMovementText;
    [SerializeField] private TextMeshProUGUI shipLocationText;
    [SerializeField] private TextMeshProUGUI shipRouteText;
    [SerializeField] private TextMeshProUGUI shipAbilitiesText;
    [SerializeField] private TextMeshProUGUI shipMissionsText;
    [SerializeField] private TextMeshProUGUI shipFleetText;
    [SerializeField] private Button backToFleetButton;
    [SerializeField] private Button shipCloseButton;

    [Header("Space Infrastructure Panel")]
    [SerializeField] private GameObject infrastructurePanel;
    [SerializeField] private TextMeshProUGUI sectorTitleText;
    [SerializeField] private TextMeshProUGUI sectorInfoText;
    [SerializeField] private RectTransform structureListContent;
    [SerializeField] private SpaceStructureCardView structureCardTemplate;
    [SerializeField] private TextMeshProUGUI structureEmptyText;
    [SerializeField] private TextMeshProUGUI sectorSummaryText;
    [SerializeField] private Button infrastructureCloseButton;

    [Header("Navigation Bar")]
    [SerializeField] private GameObject navigationBar;
    [SerializeField] private TextMeshProUGUI navigationEntityText;
    [SerializeField] private TextMeshProUGUI navigationRouteText;
    [SerializeField] private TextMeshProUGUI navigationDestinationText;
    [SerializeField] private Button navigationConfirmButton;
    [SerializeField] private Button navigationCancelButton;

    [Header("Map Legend")]
    [SerializeField] private Button legendToggleButton;
    [SerializeField] private GameObject legendPanel;
    [SerializeField] private Button legendCollapseButton;
    [SerializeField] private Image legendSelectionSwatch;
    [SerializeField] private Image legendReachableSwatch;
    [SerializeField] private Image legendBlockedSwatch;
    [SerializeField] private Image legendRouteSwatch;
    [SerializeField] private Image legendForeignSwatch;

    [Header("Space Missions")]
    [SerializeField] private SpaceMissionBarView missionBarPrefab;
    [Tooltip("Where the mission strip is placed, directly below the spacecraft card.")]
    [SerializeField] private RectTransform missionBarAnchor;

    [Header("Combat Forecast")]
    [SerializeField] private SpaceCombatPreviewPanel combatPreviewPanel;

    [Header("World-Space Space Map")]
    [SerializeField] private SpaceMapWorldController spaceMapWorldController;

    private const float RefreshInterval = 0.25f;

    /// <summary>True while any space map is open. Other Escape handlers use this to stay out of the way.</summary>
    public static bool AnyVisible { get; private set; }
    /// <summary>The frame in which the space map last consumed Escape, including the frame it closed on.</summary>
    public static int EscapeHandledFrame { get; private set; } = -1;

    private SpaceMissionBarView missionBar;
    private CombatUnit combatAttacker;
    private CombatUnit combatTarget;
    private int shownRouteDestination = -1;

    private SpaceMapSelectionKind activeKind = SpaceMapSelectionKind.None;
    private GameManager.PlanetData selectedPlanet;
    private BaseUnit selectedShip;
    private int selectedShipFleetId = -1;
    private int selectedFleetId = -1;
    private int selectedSectorTile = -1;
    private int rosterFleetId = -1;
    private int lastInspectedUnitId;
    private readonly List<SpaceFleetRosterRowView> rosterRows = new List<SpaceFleetRosterRowView>();
    private readonly List<SpaceStructureCardView> structureCards = new List<SpaceStructureCardView>();
    private readonly StringBuilder text = new StringBuilder();
    private FleetDeploymentManager deploymentManager;
    private MinimapUI minimapUI;
    private bool buttonsWired;
    private bool spaceMapModeActive;
    public bool IsVisible => spaceMapModeActive;
    private int selectedImprovementId = -1;
    private string constructionMessage;
    private float ignoreCloseInputUntil;
    private float nextRefreshTime;

    private void Awake()
    {
        ResolveAssignedReferences();
        ValidateAssignedReferences();
        WireButtons();
        Hide();
    }

    private void Update()
    {
        if (!spaceMapModeActive) return;
        bool tick = Time.unscaledTime >= nextRefreshTime;
        if (tick)
        {
            nextRefreshTime = Time.unscaledTime + RefreshInterval;
            RefreshActivePanel();
            ValidateCombatPreview();
        }
        RefreshNavigationBar(tick);
        if (Time.unscaledTime < ignoreCloseInputUntil || ConfirmationDialogService.IsVisible) return;
        bool back = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);
        if (back)
        {
            // Back out of the innermost interaction first; only an idle map closes.
            EscapeHandledFrame = Time.frameCount;
            if (CloseCombatPreview()) return;
            if (spaceMapWorldController != null && spaceMapWorldController.HasPendingRoute) { spaceMapWorldController.CancelPendingRoute(); return; }
            Hide();
        }
        else if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame) Hide();
    }

    public void Show()
    {
        gameObject.SetActive(true); if (spaceMapCanvas != null) spaceMapCanvas.gameObject.SetActive(true); if (spaceMapPanel != null) spaceMapPanel.SetActive(true);
        EnterSpaceMapMode(); SetSpaceMapPanelBackgroundVisible(false);
        WorldViewContext.GetOrCreate().SetStarSystemViewActive(true);
        ShowNoSelection();
        ResetOverlays();
        if (spaceMapWorldController != null)
            spaceMapWorldController.ShowMap(this);
        else
            Debug.LogError("[SpaceMapUI] Cannot show space map because SpaceMapWorldController is not assigned.");
        ApplyLegendColors();
    }

    public void Hide()
    {
        ResetOverlays();
        if (spaceMapWorldController != null) spaceMapWorldController.HideMap(); ExitSpaceMapMode();
        ShowNoSelection();
        if (spaceMapCanvas != null) spaceMapCanvas.gameObject.SetActive(false); else gameObject.SetActive(false);
        WorldViewContext.GetOrCreate().SetStarSystemViewActive(false);
    }

    // Temporary, uncommitted UI never outlives the map: forecasts, route previews, and the open legend all reset.
    private void ResetOverlays()
    {
        CloseCombatPreview();
        SetLegendExpanded(false);
        shownRouteDestination = -1;
        if (navigationBar != null) navigationBar.SetActive(false);
    }

    public void TestCloseButton() => Hide();

    // ── Selection display (called by SpaceMapWorldController, the single selection owner) ──

    public void SelectPlanet(GameManager.PlanetData planet)
    {
        if (planet == null) { ShowNoSelection(); return; }
        ClearSelectionState();
        selectedPlanet = planet;
        if (SpaceMapSelectionInfo.IsPlanetDiscovered(planet)) PlanetSimulationManager.RefreshPlanetPresence(planet.planetIndex);
        SetActivePanel(SpaceMapSelectionKind.Planet);
        RefreshPlanetPanel();
    }

    public void SelectShip(BaseUnit ship)
    {
        if (!SpaceMapSelectionInfo.IsAlive(ship)) { ShowNoSelection(); return; }
        ClearSelectionState();
        selectedShip = ship;
        var fleet = SpaceMapSelectionInfo.GetFleetOf(ship);
        selectedShipFleetId = fleet != null ? fleet.fleetId : -1;
        if (fleet != null && fleet.fleetId == rosterFleetId) lastInspectedUnitId = ship.gameObject.GetRuntimeId();
        SetActivePanel(SpaceMapSelectionKind.Ship);
        RefreshShipPanel();
    }

    public void SelectFleet(SpaceFleet fleet)
    {
        if (fleet == null) { ShowNoSelection(); return; }
        ClearSelectionState();
        selectedFleetId = fleet.fleetId;
        bool sameRoster = rosterFleetId == fleet.fleetId;
        if (!sameRoster) { rosterFleetId = fleet.fleetId; lastInspectedUnitId = 0; }
        SetActivePanel(SpaceMapSelectionKind.Fleet);
        RefreshFleetPanel();
        // Returning to the same fleet keeps the roster where the player left it.
        if (!sameRoster && fleetRosterScroll != null) fleetRosterScroll.verticalNormalizedPosition = 1f;
    }

    public void SelectSector(int tileIndex)
    {
        ClearSelectionState();
        selectedSectorTile = tileIndex;
        SetActivePanel(SpaceMapSelectionKind.Sector);
        RefreshInfrastructurePanel();
    }

    public void SelectImprovement(SpaceImprovementInstance improvement)
    {
        ClearSelectionState();
        selectedSectorTile = improvement.spaceTileIndex;
        selectedImprovementId = improvement.instanceId;
        SetActivePanel(SpaceMapSelectionKind.Improvement);
        RefreshInfrastructurePanel();
    }

    public void ShowNoSelection()
    {
        ClearSelectionState();
        SetActivePanel(SpaceMapSelectionKind.None);
    }

    private void ClearSelectionState()
    {
        CloseCombatPreview();
        selectedImprovementId = -1;
        constructionMessage = null;
        selectedPlanet = null;
        selectedShip = null;
        selectedShipFleetId = -1;
        selectedFleetId = -1;
        selectedSectorTile = -1;
    }

    /// <summary>The only place panels are switched, so two contextual panels can never be visible together.</summary>
    private void SetActivePanel(SpaceMapSelectionKind kind)
    {
        activeKind = kind;
        if (planetInfoPanel != null) planetInfoPanel.SetActive(kind == SpaceMapSelectionKind.Planet);
        if (fleetPanel != null) fleetPanel.SetActive(kind == SpaceMapSelectionKind.Fleet);
        if (shipPanel != null) shipPanel.SetActive(kind == SpaceMapSelectionKind.Ship);
        if (infrastructurePanel != null) infrastructurePanel.SetActive(kind == SpaceMapSelectionKind.Sector || kind == SpaceMapSelectionKind.Improvement);
        if (kind != SpaceMapSelectionKind.Ship && missionBar != null) missionBar.Bind(null);
    }

    // ── Navigation bar (the uncommitted route preview owned by the world controller) ──

    private void RefreshNavigationBar(bool tick)
    {
        if (navigationBar == null) return;
        bool pending = spaceMapWorldController != null && spaceMapWorldController.HasPendingRoute && !IsCombatPreviewOpen;
        if (!pending)
        {
            shownRouteDestination = -1;
            if (navigationBar.activeSelf) navigationBar.SetActive(false);
            return;
        }
        // Movement points and costs can change under the preview, so the text is rebuilt on the regular refresh too.
        if (navigationBar.activeSelf && spaceMapWorldController.PendingDestinationTile == shownRouteDestination && !tick) return;
        if (!spaceMapWorldController.TryGetPendingRoute(out var route))
        {
            shownRouteDestination = -1;
            if (navigationBar.activeSelf) navigationBar.SetActive(false);
            return;
        }
        shownRouteDestination = route.destinationTile;

        if (navigationEntityText != null)
            navigationEntityText.text = $"<color={SpaceMapSelectionInfo.LabelColor}>{(route.isFleet ? "Fleet" : "Spacecraft")}</color>\n{route.entityName}";
        if (navigationDestinationText != null)
            navigationDestinationText.text = $"<color={SpaceMapSelectionInfo.LabelColor}>Destination</color>\n{SpaceMapSelectionInfo.SectorName(route.destinationTile)}";
        if (navigationRouteText != null)
        {
            string status = route.cost <= route.availablePoints
                ? "Reachable this turn."
                : "Beyond this turn's movement; the route continues on later turns.";
            navigationRouteText.text =
                $"{SpaceMapSelectionInfo.Label("Route", $"{route.hexes} hex{(route.hexes == 1 ? string.Empty : "es")}")}   " +
                $"{SpaceMapSelectionInfo.Label("Cost", $"{route.cost} movement")}   " +
                $"{SpaceMapSelectionInfo.Label("Available", $"{route.availablePoints}/{route.pointsPerTurn}")}\n" +
                $"{status} Confirm, or click the destination again.";
        }
        if (!navigationBar.activeSelf) navigationBar.SetActive(true);
    }

    private void OnNavigationConfirmClicked()
    {
        if (spaceMapWorldController != null) spaceMapWorldController.ConfirmPendingRoute();
        RefreshNavigationBar(true);
    }

    private void OnNavigationCancelClicked()
    {
        if (spaceMapWorldController != null) spaceMapWorldController.CancelPendingRoute();
        RefreshNavigationBar(true);
    }

    // ── Map legend ──

    private void SetLegendExpanded(bool expanded)
    {
        if (legendPanel != null && legendPanel.activeSelf != expanded) legendPanel.SetActive(expanded);
    }

    // Swatches take the colours the map is really drawing with, so the legend cannot drift from the map.
    private void ApplyLegendColors()
    {
        if (spaceMapWorldController == null) return;
        SetSwatch(legendSelectionSwatch, spaceMapWorldController.SelectionColor);
        SetSwatch(legendReachableSwatch, spaceMapWorldController.ReachableColor);
        SetSwatch(legendBlockedSwatch, spaceMapWorldController.BlockedColor);
        SetSwatch(legendRouteSwatch, spaceMapWorldController.RouteColor);
        SetSwatch(legendForeignSwatch, spaceMapWorldController.ForeignMarkerTint);
    }

    private static void SetSwatch(Image swatch, Color color)
    {
        if (swatch == null) return;
        color.a = 1f;
        swatch.color = color;
    }

    // ── Space missions ──

    private void BindMissionBar(BaseUnit ship)
    {
        if (missionBar == null)
        {
            if (missionBarPrefab == null || missionBarAnchor == null) return;
            missionBar = Instantiate(missionBarPrefab, missionBarAnchor);
            missionBar.name = "Space Mission Bar";
            var rect = (RectTransform)missionBar.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            missionBar.MissionRequested += OnMissionRequested;
        }
        // Commands are for the player's own ship picked on the map; enemy ships and roster inspection get no strip.
        bool commands = spaceMapWorldController != null && spaceMapWorldController.SelectedShipAcceptsCommands;
        missionBar.Bind(commands ? ship as CombatUnit : null);
    }

    private void OnMissionRequested(CombatUnit spacecraft, SpaceMissionKind kind)
    {
        // Missions resolve on the tiles of the planet the spacecraft is over, so the target is picked on
        // that planet's map rather than on a space sector.
        Hide();
        if (UnitSelectionManager.Instance != null) UnitSelectionManager.Instance.SelectUnit(spacecraft);
        SpaceMissionTargetingMode.GetOrCreate().Begin(spacecraft, kind);
    }

    // ── Combat forecast ──

    /// <summary>True while a modal overlay owns input, so the world controller ignores map clicks.</summary>
    public bool BlocksMapInput => IsCombatPreviewOpen;
    private bool IsCombatPreviewOpen => combatPreviewPanel != null && combatPreviewPanel.IsOpen;

    /// <summary>Opens the forecast for an attack the combat rules already accepted. Nothing is resolved until Confirm.</summary>
    public bool ShowCombatPreview(CombatUnit attacker, CombatUnit target)
    {
        var combat = SpaceCombatManager.Instance;
        if (combat == null || combatPreviewPanel == null || !combatPreviewPanel.IsConfigured) return false;
        SpaceCombatPreview preview = combat.BuildPreview(attacker, target);
        if (preview.affectedUnits.Count == 0) return false;
        combatAttacker = attacker;
        combatTarget = target;
        combatPreviewPanel.Show(attacker, target, preview, ConfirmAttack, () => CloseCombatPreview());
        return true;
    }

    private bool CloseCombatPreview()
    {
        if (!IsCombatPreviewOpen) return false;
        combatPreviewPanel.Hide();
        combatAttacker = null;
        combatTarget = null;
        return true;
    }

    private void ConfirmAttack()
    {
        var attacker = combatAttacker;
        var target = combatTarget;
        CloseCombatPreview();
        var combat = SpaceCombatManager.Instance;
        if (combat == null || attacker == null || target == null) return;
        if (!combat.ResolveDirectAttack(attacker, target, out string reason))
            UIManager.Instance?.ShowNotification($"Attack cancelled: {reason}.");
        if (spaceMapWorldController != null) spaceMapWorldController.NotifyCombatResolved(attacker);
        else RefreshActivePanel();
    }

    // A forecast is closed as soon as the combat rules would no longer allow the attack it describes.
    private void ValidateCombatPreview()
    {
        if (!IsCombatPreviewOpen) return;
        var combat = SpaceCombatManager.Instance;
        if (combat != null && SpaceMapSelectionInfo.IsAlive(combatAttacker) && SpaceMapSelectionInfo.IsAlive(combatTarget) &&
            combat.CanDirectAttack(combatAttacker, combatTarget, out _)) return;
        CloseCombatPreview();
        UIManager.Instance?.ShowNotification("That attack is no longer possible.");
    }

    // Re-reads live data and drops selections whose entity no longer exists.
    private void RefreshActivePanel()
    {
        switch (activeKind)
        {
            case SpaceMapSelectionKind.Planet: RefreshPlanetPanel(); break;
            case SpaceMapSelectionKind.Fleet:
                if (SpaceMapSelectionInfo.FindFleet(selectedFleetId) == null) DropLostSelection(null);
                else RefreshFleetPanel();
                break;
            case SpaceMapSelectionKind.Ship:
                if (!SpaceMapSelectionInfo.IsAlive(selectedShip)) DropLostSelection(SpaceMapSelectionInfo.FindFleet(selectedShipFleetId));
                else RefreshShipPanel();
                break;
            case SpaceMapSelectionKind.Sector:
            case SpaceMapSelectionKind.Improvement: RefreshInfrastructurePanel(); break;
        }
    }

    private void DropLostSelection(SpaceFleet fallbackFleet)
    {
        if (spaceMapWorldController == null) { ShowNoSelection(); return; }
        spaceMapWorldController.RefreshTravelVisuals();
        if (fallbackFleet != null) spaceMapWorldController.SelectFleet(fallbackFleet);
        else spaceMapWorldController.ClearSelection();
    }

    // ── Planet ──

    private void RefreshPlanetPanel()
    {
        var planet = selectedPlanet;
        if (planet == null) return;
        bool known = SpaceMapSelectionInfo.IsPlanetDiscovered(planet);

        if (planetNameText != null) planetNameText.text = known ? planet.planetName : "Unexplored body";
        if (planetTypeText != null)
            planetTypeText.text = known ? $"{SpaceMapSelectionInfo.Nicify(planet.celestialBodyType)}, {SpaceMapSelectionInfo.Nicify(planet.planetType)}" : "Type unknown";
        if (planetStatusText != null)
        {
            string exploration = planet.isHomeWorld ? "Homeworld" : known ? "Explored" : "Unexplored";
            string colonization = !known ? "Unknown" : planet.isColonized ? "Colonized" : "Not colonized";
            planetStatusText.text = SpaceMapSelectionInfo.Label("Size", known ? SpaceMapSelectionInfo.Nicify(planet.planetSize) : "Unknown") + "\n"
                + SpaceMapSelectionInfo.Label("Exploration", exploration) + "\n"
                + SpaceMapSelectionInfo.Label("Colonization", colonization);
        }
        if (planetEnvironmentText != null)
        {
            string atmosphere = !known ? "Unknown"
                : !planet.hasAtmosphere ? "None"
                : string.IsNullOrEmpty(planet.atmosphereComposition) ? "Present" : planet.atmosphereComposition;
            string temperature = known ? $"{planet.averageTemperature:0} C average" : "Unknown";
            planetEnvironmentText.text = SpaceMapSelectionInfo.Label("Atmosphere", atmosphere) + "\n"
                + SpaceMapSelectionInfo.Label("Temperature", temperature);
        }
        if (planetMoonsText != null) planetMoonsText.text = known ? BuildMoonText(planet) : SpaceMapSelectionInfo.Label("Moons", "Unknown");
        if (planetCivilizationsText != null)
        {
            string civilizations = !known ? "Unknown"
                : string.Join("\n", (planet.civilizationNames ?? new List<string>())
                    .Concat((planet.civilizations ?? new List<CivData>()).Where(civ => civ != null).Select(civ => civ.civName))
                    .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct());
            if (string.IsNullOrEmpty(civilizations)) civilizations = "None known";
            planetCivilizationsText.text = $"<color={SpaceMapSelectionInfo.LabelColor}>Known Civilizations</color>\n{civilizations}";
        }
        if (distanceText != null)
        {
            int anchorTile = spaceMapWorldController != null ? spaceMapWorldController.GetPlanetAnchorTile(planet.planetIndex) : -1;
            distanceText.text = anchorTile >= 0 ? $"System location: sector {anchorTile}" : "System location unavailable";
            if (known && planet.orbitalPeriod > 0) distanceText.text += $"\nOrbital period: {planet.orbitalPeriod:0.#} days";
        }
        if (planetPreviewImage != null)
        {
            if (minimapUI == null) minimapUI = FindAnyObjectByType<MinimapUI>(FindObjectsInactive.Include);
            Texture preview = known && minimapUI != null ? minimapUI.GetPlanetMinimapTexture(planet.planetIndex) : null;
            planetPreviewImage.texture = preview;
            planetPreviewImage.enabled = preview != null;
        }
    }

    private string BuildMoonText(GameManager.PlanetData planet)
    {
        string header = $"<color={SpaceMapSelectionInfo.LabelColor}>Moons</color>\n";
        if (planet.celestialBodyType == GameManager.CelestialBodyType.Moon)
        {
            var parent = SpaceMapSelectionInfo.FindPlanetByBodyId(planet.parentBodyId);
            return header + (SpaceMapSelectionInfo.IsPlanetDiscovered(parent) ? $"Moon of {parent.planetName}" : "This body is a moon");
        }
        int count = Mathf.Max(planet.childMoonIds?.Count ?? 0, planet.moonNames?.Count ?? 0);
        var moons = (planet.childMoonIds ?? new List<int>()).Select(SpaceMapSelectionInfo.FindPlanetByBodyId)
            .Where(SpaceMapSelectionInfo.IsPlanetDiscovered).Select(moon => moon.planetName).ToArray();
        if (moons.Length > 0) return header + string.Join("\n", moons) +
            (count > moons.Length ? $"\n{count - moons.Length} unexplored" : string.Empty);
        return header + (count > 0 ? $"{count} moon{(count == 1 ? string.Empty : "s")}" : "None");
    }

    private void SwitchToPlanet(GameManager.PlanetData planet)
    {
        if (planet == null) return; Hide(); if (GameManager.Instance != null) GameManager.Instance.StartCoroutine(GameManager.Instance.SwitchToMultiPlanet(planet.planetIndex));
    }

    // ── Fleet ──

    private void RefreshFleetPanel()
    {
        var fleet = SpaceMapSelectionInfo.FindFleet(selectedFleetId);
        if (fleet == null) return;
        var owner = SpaceMapSelectionInfo.GetFleetOwner(fleet);

        if (fleetNameText != null) fleetNameText.text = string.IsNullOrEmpty(fleet.fleetName) ? $"Fleet {fleet.fleetId}" : fleet.fleetName;
        if (fleetOwnerText != null) fleetOwnerText.text = SpaceMapSelectionInfo.CivilizationName(owner);
        if (fleetAdmiralText != null)
            fleetAdmiralText.text = $"<color={SpaceMapSelectionInfo.LabelColor}>Admiral</color>\n{SpaceMapSelectionInfo.FleetAdmiral(fleet)}";
        if (fleetLocationText != null)
        {
            string movement = string.Empty;
            if (SpaceShipMovementController.Instance != null &&
                SpaceShipMovementController.Instance.TryGetPackedMembers(fleet, out var members, out _))
            {
                int remaining = int.MaxValue;
                int perTurn = int.MaxValue;
                foreach (var member in members)
                {
                    remaining = Mathf.Min(remaining, member.currentSpaceMovementPoints);
                    perTurn = Mathf.Min(perTurn, member.spaceMovementPointsPerTurn);
                }
                movement = $"\nMovement: {remaining}/{perTurn}";
            }
            fleetLocationText.text = $"<color={SpaceMapSelectionInfo.LabelColor}>Location</color>\n{SpaceMapSelectionInfo.SectorName(GetFleetTile(fleet))}\n{SpaceMapSelectionInfo.FleetStatus(fleet)}{movement}";
        }

        RefreshFleetRoster(fleet);
        RefreshFleetActions(fleet, owner);
    }

    private static int GetFleetTile(SpaceFleet fleet)
    {
        if (fleet.isPacked || fleet.memberUnitIds == null) return fleet.currentSpaceTileIndex;
        foreach (int id in fleet.memberUnitIds)
        {
            var unit = SpaceMapSelectionInfo.ResolveUnit(id);
            if (SpaceMapSelectionInfo.IsAlive(unit) && unit.currentSpaceTileIndex >= 0) return unit.currentSpaceTileIndex;
        }
        return fleet.currentSpaceTileIndex;
    }

    private void RefreshFleetRoster(SpaceFleet fleet)
    {
        int count = fleet.memberUnitIds != null ? fleet.memberUnitIds.Count : 0;
        if (fleetRosterEmptyText != null) fleetRosterEmptyText.gameObject.SetActive(count == 0);
        if (fleetRosterRowTemplate == null || fleetRosterContent == null) return;

        // Rows are pooled and rebound in place, so the scroll position survives every refresh.
        while (rosterRows.Count < count)
        {
            var row = Instantiate(fleetRosterRowTemplate, fleetRosterContent);
            row.name = $"Fleet Roster Row {rosterRows.Count + 1}";
            if (row.Button != null) row.Button.onClick.AddListener(() => OnRosterRowClicked(row));
            rosterRows.Add(row);
        }
        for (int i = 0; i < rosterRows.Count; i++)
        {
            bool used = i < count;
            rosterRows[i].gameObject.SetActive(used);
            if (!used) continue;
            int unitId = fleet.memberUnitIds[i];
            rosterRows[i].Bind(unitId, SpaceMapSelectionInfo.ResolveUnit(unitId), unitId == lastInspectedUnitId);
        }
    }

    private void OnRosterRowClicked(SpaceFleetRosterRowView row)
    {
        if (row == null || spaceMapWorldController == null) return;
        var unit = SpaceMapSelectionInfo.ResolveUnit(row.UnitId);
        if (!SpaceMapSelectionInfo.IsAlive(unit)) { RefreshActivePanel(); return; }
        lastInspectedUnitId = row.UnitId;
        // Inspection only: the ship stays in its fleet and cannot receive movement orders from here.
        spaceMapWorldController.InspectShip(unit);
    }

    private void RefreshFleetActions(SpaceFleet fleet, Civilization owner)
    {
        bool playerFleet = SpaceMapSelectionInfo.IsPlayerOwned(owner);
        bool showPack = playerFleet && !fleet.isPacked;
        bool showDeploy = playerFleet && fleet.isPacked;
        string status = playerFleet ? string.Empty : "No orders available for this fleet.";

        if (showPack)
        {
            bool canPack = CanPackFleet(fleet, out string reason);
            if (fleetPackButton != null) fleetPackButton.interactable = canPack;
            status = canPack ? "Pack the fleet into a single formation." : $"Cannot pack: {reason}.";
        }
        else if (showDeploy)
        {
            status = "Deploy the fleet's ships around its position.";
        }
        if (fleetPackButton != null) fleetPackButton.gameObject.SetActive(showPack);
        if (fleetDeployButton != null) fleetDeployButton.gameObject.SetActive(showDeploy);
        if (fleetActionStatusText != null) fleetActionStatusText.text = status;
    }

    private static bool CanPackFleet(SpaceFleet fleet, out string reason)
    {
        if (SpaceFleetManager.Instance != null) return SpaceFleetManager.Instance.CanPack(fleet, out reason);
        reason = "fleet manager is unavailable";
        return false;
    }

    private FleetDeploymentManager GetDeploymentManager()
    {
        if (deploymentManager == null) deploymentManager = FindAnyObjectByType<FleetDeploymentManager>(FindObjectsInactive.Include);
        if (deploymentManager == null) deploymentManager = gameObject.AddComponent<FleetDeploymentManager>();
        // Fleet and ship positions are indices into the world's grid, so packing must use that same grid.
        var worldGrid = SpaceWorldManager.Instance != null ? SpaceWorldManager.Instance.Grid : null;
        if (worldGrid != null) deploymentManager.spaceGrid = worldGrid;
        return deploymentManager;
    }

    private void OnPackFleetClicked()
    {
        var fleet = SpaceMapSelectionInfo.FindFleet(selectedFleetId);
        if (fleet == null || fleet.isPacked || !SpaceMapSelectionInfo.IsPlayerOwned(SpaceMapSelectionInfo.GetFleetOwner(fleet))) return;
        if (!CanPackFleet(fleet, out string reason) || !GetDeploymentManager().PackFleet(fleet, fleetPackGatherRadius, out reason))
            UIManager.Instance?.ShowNotification($"Cannot pack fleet: {reason}.");
        AfterFleetAction(fleet);
    }

    private void OnDeployFleetClicked()
    {
        var fleet = SpaceMapSelectionInfo.FindFleet(selectedFleetId);
        if (fleet == null || !fleet.isPacked || !SpaceMapSelectionInfo.IsPlayerOwned(SpaceMapSelectionInfo.GetFleetOwner(fleet))) return;
        if (!GetDeploymentManager().UnpackFleet(fleet, out string reason))
            UIManager.Instance?.ShowNotification($"Cannot deploy fleet: {reason}.");
        AfterFleetAction(fleet);
    }

    private void AfterFleetAction(SpaceFleet fleet)
    {
        if (spaceMapWorldController == null) { RefreshFleetPanel(); return; }
        spaceMapWorldController.RefreshTravelVisuals();
        spaceMapWorldController.SelectFleet(fleet);
    }

    // ── Individual spacecraft ──

    private void RefreshShipPanel()
    {
        var ship = selectedShip;
        if (ship == null) return;
        var fleet = SpaceMapSelectionInfo.GetFleetOf(ship);
        selectedShipFleetId = fleet != null ? fleet.fleetId : -1;
        bool playerShip = SpaceMapSelectionInfo.PlayerCivilization == null || SpaceMapSelectionInfo.IsPlayerOwned(ship.owner);
        string unitName = SpaceMapSelectionInfo.UnitName(ship);
        string fleetName = fleet == null ? "Independent" : string.IsNullOrEmpty(fleet.fleetName) ? $"Fleet {fleet.fleetId}" : fleet.fleetName;

        if (shipNameText != null) shipNameText.text = unitName;
        if (shipClassText != null) shipClassText.text = SpaceMapSelectionInfo.UnitClass(ship);
        Sprite sprite = SpaceMapSelectionInfo.UnitIcon(ship);
        if (shipIconImage != null) { shipIconImage.sprite = sprite; shipIconImage.enabled = sprite != null; }
        if (shipIconFallbackText != null)
        {
            shipIconFallbackText.gameObject.SetActive(sprite == null);
            shipIconFallbackText.text = unitName.Length > 0 ? unitName.Substring(0, 1).ToUpperInvariant() : "?";
        }
        if (shipSummaryText != null)
            shipSummaryText.text = SpaceMapSelectionInfo.Label("Owner", SpaceMapSelectionInfo.CivilizationName(ship.owner)) + "\n"
                + SpaceMapSelectionInfo.Label("Fleet", fleetName) + "\n"
                + SpaceMapSelectionInfo.Label("Status", SpaceMapSelectionInfo.UnitStatus(ship));
        if (shipHealthText != null) shipHealthText.text = SpaceMapSelectionInfo.Label("Health", $"{Mathf.Max(0, ship.currentHealth)}/{ship.MaxHealth}");
        if (shipAttackText != null) shipAttackText.text = SpaceMapSelectionInfo.Label("Space Attack", ship.CurrentSpaceAttack.ToString());
        if (shipDefenseText != null) shipDefenseText.text = SpaceMapSelectionInfo.Label("Defense", ship.CurrentDefense.ToString());
        if (shipMovementText != null) shipMovementText.text = SpaceMapSelectionInfo.Label("Movement", $"{ship.currentSpaceMovementPoints}/{ship.spaceMovementPointsPerTurn}");
        if (shipLocationText != null) shipLocationText.text = SpaceMapSelectionInfo.Label("Location", SpaceMapSelectionInfo.UnitLocation(ship));
        // A foreign ship's planned route is not something the player has discovered.
        if (shipRouteText != null) shipRouteText.text = SpaceMapSelectionInfo.Label("Route", playerShip ? SpaceMapSelectionInfo.UnitRoute(ship) : "Unknown");
        if (shipAbilitiesText != null)
            shipAbilitiesText.text = $"<color={SpaceMapSelectionInfo.LabelColor}>Abilities</color>\n{SpaceMapSelectionInfo.UnitAbilities(ship)}";
        if (shipMissionsText != null)
            shipMissionsText.text = $"<color={SpaceMapSelectionInfo.LabelColor}>Space Missions</color>\n{SpaceMapSelectionInfo.UnitMissions(ship)}";
        if (shipFleetText != null)
            shipFleetText.text = fleet != null ? $"<color={SpaceMapSelectionInfo.LabelColor}>Member of</color>\n{fleetName}" : "Independent spacecraft";
        if (backToFleetButton != null) backToFleetButton.gameObject.SetActive(fleet != null);
        BindMissionBar(ship);
    }

    private void OnBackToFleetClicked()
    {
        var fleet = SpaceMapSelectionInfo.FindFleet(selectedShipFleetId);
        if (fleet == null || spaceMapWorldController == null) { RefreshActivePanel(); return; }
        spaceMapWorldController.SelectFleet(fleet);
    }

    // ── Space infrastructure ──

    private void RefreshInfrastructurePanel()
    {
        var world = SpaceWorldManager.Instance;
        var state = world != null ? world.CurrentSystem : null;
        var tile = state != null && state.grid != null ? state.grid.GetTile(selectedSectorTile) : null;
        if (tile == null || (activeKind == SpaceMapSelectionKind.Improvement &&
            !world.Entities.TryGetImprovement(selectedImprovementId, out _)))
        {
            DropLostSelection(null);
            return;
        }

        if (sectorTitleText != null) sectorTitleText.text = SpaceMapSelectionInfo.SectorName(tile.tileIndex);

        int cardCount = 0;
        int structureCount = 0;
        int orderCount = 0;
        if (structureCardTemplate != null && structureListContent != null)
        {
            var player = SpaceMapSelectionInfo.PlayerCivilization;
            int owner = player != null ? player.gameObject.GetRuntimeId() : -1;
            var manager = world.Construction;
            var structures = state.improvements.Where(i => i != null && i.spaceTileIndex == tile.tileIndex)
                .OrderByDescending(i => i.instanceId == selectedImprovementId);
            foreach (var improvement in structures)
            {
                GetStructureCard(cardCount++).Bind(improvement);
                structureCount++;
            }
            foreach (var order in state.constructionOrders)
            {
                if (order == null || order.targetTileIndex != tile.tileIndex || order.ownerCivilizationId != owner || player == null) continue;
                var data = SpaceImprovementRegistry.GetData(order.improvementDataId);
                string title = data != null ? ProjectName(data) : order.improvementDataId;
                string progress = $"{order.accumulatedProduction}/{order.requiredProduction} production";
                string status = order.completed ? "Complete" : data == null ? "Definition unavailable" : order.paused ? "Paused" : "In progress";
                Action cancel = null;
                if (!order.completed && manager != null)
                    cancel = () => { manager.CancelConstruction(order); constructionMessage = null; RefreshInfrastructurePanel(); };
                GetStructureCard(cardCount++).BindRow(title, data?.icon,
                    $"{status} · {progress}" + (cancel != null ? "\nUpfront costs are committed" : string.Empty),
                    cancel != null ? "Cancel" : status, cancel);
                if (!order.completed) orderCount++;
            }

            if (manager != null && player != null)
            {
                foreach (var project in SpaceImprovementRegistry.All.Where(d => d != null).OrderBy(ProjectName).ToArray())
                {
                    bool allowed = manager.CanConstruct(player, project, tile.tileIndex, out string reason);
                    Action build = null;
                    if (allowed)
                        build = () =>
                        {
                            manager.StartConstruction(player, project, tile.tileIndex, out _, out constructionMessage);
                            RefreshInfrastructurePanel();
                        };
                    string requirements = DescribeProject(project);
                    if (!allowed) requirements += $"\nBlocked: {reason}";
                    GetStructureCard(cardCount++).BindRow(ProjectName(project), project.icon, requirements,
                        allowed ? "Build" : "Blocked", build);
                }
            }
        }
        for (int i = 0; i < structureCards.Count; i++) structureCards[i].gameObject.SetActive(i < cardCount);
        if (structureEmptyText != null)
        {
            structureEmptyText.gameObject.SetActive(cardCount == 0);
            if (cardCount == 0) structureEmptyText.text = "No structures or construction projects available.";
        }

        text.Clear();
        text.Append(SpaceMapSelectionInfo.Label("Sector", $"{tile.tileIndex} ({tile.q}, {tile.r})")).Append('\n');
        var body = tile.planetId >= 0 ? SpaceMapSelectionInfo.FindPlanetByBodyId(tile.planetId) : null;
        string terrain = body != null && !SpaceMapSelectionInfo.IsPlanetDiscovered(body)
            ? "Unexplored body" : SpaceMapSelectionInfo.Nicify(tile.terrainType);
        text.Append(SpaceMapSelectionInfo.Label("Terrain", terrain)).Append('\n');
        text.Append(SpaceMapSelectionInfo.Label("Movement Cost", tile.blocksMovement ? "Impassable" : tile.movementCost.ToString())).Append('\n');
        string controller = tile.controllingCivilizationId == -1 ? "Unclaimed" : SpaceMapSelectionInfo.CivilizationName(SpaceMapSelectionInfo.ResolveCivilization(tile.controllingCivilizationId));
        text.Append(SpaceMapSelectionInfo.Label("Control", controller)).Append('\n');
        if (tile.isPlanetOrbitSector)
        {
            var orbited = SpaceMapSelectionInfo.FindPlanetByBodyId(tile.associatedPlanetId);
            text.Append(SpaceMapSelectionInfo.Label("Orbit Sector", SpaceMapSelectionInfo.IsPlanetDiscovered(orbited) ? orbited.planetName : "Yes")).Append('\n');
        }
        if (tile.resource != null && !tile.resource.hidden && !string.IsNullOrEmpty(tile.resource.resourceId))
            text.Append(SpaceMapSelectionInfo.Label("Resource", $"{SpaceMapSelectionInfo.Nicify(tile.resource.resourceId)} x{tile.resource.quantity}")).Append('\n');
        if (tile.hazard != null && (!tile.hazard.hidden || tile.hazard.detected) && !string.IsNullOrEmpty(tile.hazard.hazardId))
            text.Append(SpaceMapSelectionInfo.Label("Hazard", SpaceMapSelectionInfo.Nicify(tile.hazard.hazardId))).Append('\n');
        AppendFeatures(tile);
        if (!string.IsNullOrEmpty(constructionMessage)) text.AppendLine(constructionMessage);
        if (sectorInfoText != null) sectorInfoText.text = text.ToString().TrimEnd('\n');
        if (sectorSummaryText != null)
            sectorSummaryText.text = $"{structureCount} structure{(structureCount == 1 ? string.Empty : "s")}, {orderCount} construction order{(orderCount == 1 ? string.Empty : "s")} in this sector";
    }

    private static string ProjectName(SpaceImprovementData data) =>
        string.IsNullOrEmpty(data.displayName) ? data.improvementId : data.displayName;

    private static string DescribeProject(SpaceImprovementData data)
    {
        var details = new List<string> { $"{Mathf.Max(1, data.constructionCost)} production" };
        if (data.goldCost > 0) details.Add($"{data.goldCost} gold");
        details.AddRange(SpaceConstructionManager.GetResourceCosts(data).Select(cost => $"{cost.amount} {cost.resource.resourceName}"));
        var techs = (data.requiredTechs ?? Array.Empty<TechData>()).Where(tech => tech != null).Select(tech => tech.techName).ToArray();
        if (techs.Length > 0) details.Add($"Tech: {string.Join(", ", techs)}");
        var rules = data.placementRules;
        if (!string.IsNullOrEmpty(data.requiredResourceType)) details.Add($"Deposit: {SpaceMapSelectionInfo.Nicify(data.requiredResourceType)}");
        else if (rules?.requiresResourceDeposit == true) details.Add("Resource deposit required");
        if (rules != null)
        {
            if (rules.allowedTerrainTypes?.Length > 0)
                details.Add($"Terrain: {string.Join(", ", rules.allowedTerrainTypes.Select(terrain => SpaceMapSelectionInfo.Nicify(terrain)))}");
            if (rules.requiresOrbitSector) details.Add("Orbit sector");
            if (rules.requiresDeepSpace) details.Add("Deep space");
            if (rules.requiresFriendlyControl) details.Add("Friendly control");
            if (rules.minimumDistanceFromPlanet > 0) details.Add($"At least {rules.minimumDistanceFromPlanet} hexes from a body");
            if (rules.maximumDistanceFromPlanet >= 0) details.Add($"Within {rules.maximumDistanceFromPlanet} hexes of a body");
            if (rules.minimumDistanceFromSameImprovement > 0) details.Add($"Spacing: {rules.minimumDistanceFromSameImprovement} hexes");
            if (rules.maximumPerPlanet >= 0) details.Add($"Limit {rules.maximumPerPlanet} per planet");
            if (rules.maximumPerCivilization >= 0) details.Add($"Limit {rules.maximumPerCivilization} per civilization");
            if (!rules.mayShareTileWithNaturalFeature) details.Add("Clear of natural features");
            if (!rules.mayShareTileWithAnotherImprovement) details.Add("Requires an empty structure slot");
        }
        return string.Join(" · ", details);
    }

    private void AppendFeatures(SpaceHexTile tile)
    {
        if (tile.featureInstanceIds == null || SpaceFeatureManager.Instance == null) return;
        foreach (int id in tile.featureInstanceIds)
        {
            var feature = SpaceFeatureManager.Instance.GetFeature(id);
            var data = feature != null ? SpaceFeatureManager.Instance.GetFeatureData(feature.featureDataId) : null;
            if (!SpaceMapSelectionInfo.IsFeatureKnown(feature, data)) continue;
            string featureName = data != null && !string.IsNullOrEmpty(data.displayName) ? data.displayName : feature.featureDataId;
            text.Append(SpaceMapSelectionInfo.Label("Feature", featureName)).Append('\n');
        }
    }

    private SpaceStructureCardView GetStructureCard(int index)
    {
        while (structureCards.Count <= index)
        {
            var card = Instantiate(structureCardTemplate, structureListContent);
            card.name = $"Space Structure Card {structureCards.Count + 1}";
            structureCards.Add(card);
        }
        return structureCards[index];
    }

    // ── Setup ──

    private void WireButtons()
    {
        if (buttonsWired) return;
        buttonsWired = true;
        if (closeButton != null) closeButton.onClick.AddListener(Hide);
        if (travelButton != null) travelButton.onClick.AddListener(() => { if (selectedPlanet != null) SwitchToPlanet(selectedPlanet); });
        foreach (var button in new[] { cancelButton, fleetCloseButton, shipCloseButton, infrastructureCloseButton })
            if (button != null) button.onClick.AddListener(ClearSelection);
        if (fleetPackButton != null) fleetPackButton.onClick.AddListener(OnPackFleetClicked);
        if (fleetDeployButton != null) fleetDeployButton.onClick.AddListener(OnDeployFleetClicked);
        if (backToFleetButton != null) backToFleetButton.onClick.AddListener(OnBackToFleetClicked);
        if (navigationConfirmButton != null) navigationConfirmButton.onClick.AddListener(OnNavigationConfirmClicked);
        if (navigationCancelButton != null) navigationCancelButton.onClick.AddListener(OnNavigationCancelClicked);
        if (legendToggleButton != null) legendToggleButton.onClick.AddListener(() => SetLegendExpanded(legendPanel != null && !legendPanel.activeSelf));
        if (legendCollapseButton != null) legendCollapseButton.onClick.AddListener(() => SetLegendExpanded(false));
        if (fleetRosterRowTemplate != null) fleetRosterRowTemplate.gameObject.SetActive(false);
        if (structureCardTemplate != null) structureCardTemplate.gameObject.SetActive(false);
    }

    private void ClearSelection()
    {
        if (spaceMapWorldController != null) spaceMapWorldController.ClearSelection();
        else ShowNoSelection();
    }

    private void ResolveAssignedReferences()
    {
        if (spaceMapCanvas == null) spaceMapCanvas = GetComponentInParent<Canvas>(true);
        if (spaceMapWorldController == null) spaceMapWorldController = FindAnyObjectByType<SpaceMapWorldController>(FindObjectsInactive.Include);
        if (spaceMapWorldController == null) spaceMapWorldController = gameObject.AddComponent<SpaceMapWorldController>();
    }

    private void ValidateAssignedReferences()
    {
        if (spaceMapCanvas == null) Debug.LogWarning("[SpaceMapUI] Space Map Canvas is not assigned. Assign it in the Unity inspector.");
        if (spaceMapPanel == null) Debug.LogWarning("[SpaceMapUI] Space Map Panel is not assigned. Create the panel manually in Unity and assign it.");
        if (closeButton == null) Debug.LogWarning("[SpaceMapUI] Close Button is not assigned. Create it manually in Unity and assign it.");
        if (planetInfoPanel == null) Debug.LogWarning("[SpaceMapUI] Planet Information panel is not assigned.");
        if (fleetPanel == null) Debug.LogWarning("[SpaceMapUI] Fleet Overview panel is not assigned.");
        if (shipPanel == null) Debug.LogWarning("[SpaceMapUI] Individual Spacecraft panel is not assigned.");
        if (infrastructurePanel == null) Debug.LogWarning("[SpaceMapUI] Space Infrastructure panel is not assigned.");
        if (spaceMapWorldController == null) Debug.LogWarning("[SpaceMapUI] SpaceMapWorldController is not assigned. Add one to the scene and assign it.");
    }
    private void SetSpaceMapPanelBackgroundVisible(bool visible) { var img = spaceMapPanel != null ? spaceMapPanel.GetComponent<Image>() : null; if (img != null) img.enabled = visible; }
    private void EnterSpaceMapMode() { spaceMapModeActive = true; AnyVisible = true; ignoreCloseInputUntil = Time.unscaledTime + .15f; nextRefreshTime = 0f; }
    private void ExitSpaceMapMode() { if (spaceMapModeActive) AnyVisible = false; spaceMapModeActive = false; }
    private void OnDestroy() { if (spaceMapModeActive) AnyVisible = false; }
}
