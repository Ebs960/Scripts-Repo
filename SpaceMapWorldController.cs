using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Sole renderer and interaction controller for the solar-system map. It renders a
/// dedicated world-space hex grid, 3D planet/moon markers anchored to stable space
/// tiles, visible spacecraft, selection highlights, and queued hex movement paths.
/// </summary>
public class SpaceMapWorldController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera spaceMapCamera;
    [SerializeField] private SpaceMapCameraController cameraController;
    [SerializeField] private Transform mapRoot;
    [SerializeField] private Transform planetRoot;
    [SerializeField] private Transform hexRoot;
    [SerializeField] private Transform unitRoot;
    [SerializeField] private Transform routeRoot;
    [SerializeField] private SpaceMapUI spaceMapUI;

    [Header("Planet Visuals")]
    [SerializeField] private SpaceMapPlanetMarker planetMarkerPrefab;
    [SerializeField] private Material planetMaterialTemplate;
    [SerializeField] private float minPlanetRadius = 1.5f;
    [SerializeField] private float maxPlanetRadius = 5f;

    [Header("Hex Grid")]
    [SerializeField] private int gridRadius = 12;
    [SerializeField] private float hexSize = 5f;
    [SerializeField] private Material hexMaterial;
    [SerializeField] private Material reachableHexMaterial;
    [SerializeField] private Color hexColor = new Color(0.1f, 0.5f, 1f, 0.25f);
    [SerializeField] private Color reachableColor = new Color(0f, 1f, 0.7f, 0.45f);
    [SerializeField] private Color blockedColor = new Color(1f, 0.15f, 0.1f, 0.35f);

    [Header("Map Markers")]
    [SerializeField] private Sprite shipMarkerSprite;
    [SerializeField] private Sprite fleetMarkerSprite;
    [SerializeField] private Sprite infrastructureMarkerSprite;
    [Tooltip("Where the artwork sits inside each marker image, as a fraction of the image measured from its bottom-left corner. " +
        "Markers are sized and clicked by this area, not by the transparent padding. Leave empty to measure it from the sprite mesh.")]
    [SerializeField] private Rect shipMarkerArtArea;
    [SerializeField] private Rect fleetMarkerArtArea;
    [SerializeField] private Rect infrastructureMarkerArtArea;
    [Tooltip("Visible marker widths as a fraction of the hex size.")]
    [SerializeField] private float shipMarkerSize = 0.66f;
    [SerializeField] private float fleetMarkerSize = 0.95f;
    [Tooltip("An unpacked fleet's marker sits beside its ships, so it is drawn smaller than a packed formation.")]
    [SerializeField] private float escortFleetMarkerSize = 0.4f;
    [SerializeField] private float infrastructureMarkerSize = 0.36f;
    [Tooltip("Width of a structure's own icon inside the marker's empty recess, as a fraction of the marker width.")]
    [SerializeField] private float infrastructureIconSize = 0.37f;
    [Tooltip("Markers grow when the camera zooms out so a ship never drops below this many screen pixels.")]
    [SerializeField] private float minimumShipMarkerPixels = 26f;
    [SerializeField] private float maximumMarkerZoomScale = 1.6f;

    private const int InfrastructureSortingOrder = 10;
    private const int EscortFleetSortingOrder = 20;
    private const int ShipSortingOrder = 30;
    private const float EntityCheckInterval = 0.5f;

    private sealed class MapMarker
    {
        public Transform root;
        public SpriteRenderer art;
        public SpriteRenderer ring;
        public Vector3 anchor;
        public Vector3 offset;
        public float height;
        public float width;
        public Rect visible;
        public Color baseTint;
    }

    public SpaceHexGrid Grid { get; private set; }
    private readonly Dictionary<int, SpaceMapPlanetMarker> markersByPlanet = new Dictionary<int, SpaceMapPlanetMarker>();
    private readonly Dictionary<int, GameObject> hexObjects = new Dictionary<int, GameObject>();
    private readonly Dictionary<Camera, int> hiddenGameplayCameraMasks = new Dictionary<Camera, int>();
    private readonly Dictionary<int, MapMarker> shipMarkers = new Dictionary<int, MapMarker>();
    private readonly Dictionary<int, MapMarker> fleetMarkers = new Dictionary<int, MapMarker>();
    private readonly Dictionary<int, MapMarker> improvementMarkers = new Dictionary<int, MapMarker>();
    private int lastImprovementSignature = int.MinValue;
    private int lastEntitySignature = int.MinValue;
    private float nextEntityCheck;
    private float markerZoom = 1f;
    private readonly List<SpaceFleet> fleetBuffer = new List<SpaceFleet>();
    // Tints multiply the artwork, so the gold-and-navy marker stays recognizable in every state.
    private static readonly Color foreignMarkerTint = new Color(1f, 0.7f, 0.68f, 1f);
    private static readonly Color fleetMemberTint = new Color(0.62f, 0.95f, 1f, 1f);
    private static readonly Color selectedEntityColor = Color.yellow;
    private static readonly Color selectedSectorColor = new Color(1f, 0.8f, 0.2f, 0.55f);
    private static readonly Color routeEndColor = Color.cyan;
    private BaseUnit selectedShip;
    private bool selectedShipIsInspection;
    private int selectedFleetId = -1;
    private int selectedPlanetIndex = -1;
    private int selectedSectorTile = -1;
    private int selectedImprovementId = -1;
    private int pendingDestinationTile = -1;
    private List<int> pendingPath;
    private bool isVisible;
    private Material fallbackHexMaterial;
    private Material routeMaterial;
    private MaterialPropertyBlock colorProperties;

    private void Awake()
    {
        colorProperties = new MaterialPropertyBlock();
        EnsureSceneObjects();
        SetMapActive(false);
    }
    private void Update()
    {
        if (!isVisible) return;
        var structures = SpaceWorldManager.Instance?.CurrentSystem?.improvements;
        if (structures != null && lastImprovementSignature != GetImprovementSignature(structures)) RefreshImprovementMarkers(structures);
        // Ships and fleets move, pack, deploy, and die outside this controller, so their markers are re-checked here.
        if (Time.unscaledTime >= nextEntityCheck)
        {
            nextEntityCheck = Time.unscaledTime + EntityCheckInterval;
            if (GetEntitySignature() != lastEntitySignature) RefreshTravelVisuals();
        }
        if (pendingPath != null && !IsPendingRouteValid()) CancelPendingRoute();
        UpdateMarkerZoom(false);
        HandleSelectionInput();
    }

    public void ShowMap(SpaceMapUI ui)
    {
        if (ui != null) spaceMapUI = ui;
        ResetSelection();
        EnsureSceneObjects(); HideGameplayCameras(); SetMapActive(true); RebuildSpaceMap(); CenterOnCurrentPlanet();
    }
    public void HideMap() { ResetSelection(); SetMapActive(false); RestoreGameplayCameras(); }

    public void RebuildSpaceMap()
    {
        colorProperties ??= new MaterialPropertyBlock();
        EnsureSceneObjects();
        if (!isVisible) SetMapActive(false);
        bool createWorld = SpaceWorldManager.Instance == null;
        if (createWorld)
            new GameObject("SpaceWorldManager").AddComponent<SpaceWorldManager>();
        if (createWorld || SpaceWorldManager.Instance.Grid == null)
            SpaceWorldManager.Instance.CreateSystem(0, gridRadius, hexSize);
        var previousGrid = Grid;
        Grid = SpaceWorldManager.Instance.Grid;
        if (SpaceShipMovementController.Instance == null) new GameObject("SpaceShipMovementController").AddComponent<SpaceShipMovementController>();
        SpaceShipMovementController.Instance.SetGrid(Grid);
        if (SpaceOccupancyManager.Instance == null) new GameObject("SpaceOccupancyManager").AddComponent<SpaceOccupancyManager>();
        SpaceOccupancyManager.Instance.spaceGrid = Grid;
        if (SpaceCombatManager.Instance != null) SpaceCombatManager.Instance.spaceGrid = Grid;
        if (SpaceFleetManager.Instance == null) new GameObject("SpaceFleetManager").AddComponent<SpaceFleetManager>();
        foreach (var unit in UnitRegistry.GetCombatUnits().ToArray())
            SpaceShipMovementController.Instance.EnsureSingletonFleet(unit);
        bool rebuildHexes = previousGrid != Grid || hexObjects.Count != Grid.tiles.Count;
        if (rebuildHexes)
        {
            ClearChildren(hexRoot);
            improvementMarkers.Clear();
            hexObjects.Clear();
            BuildHexVisuals();
        }
        ClearChildren(planetRoot); ClearChildren(unitRoot); ClearChildren(routeRoot); markersByPlanet.Clear();
        RebuildPlanets();
        RefreshTravelVisuals();
        RefreshImprovementMarkers(SpaceWorldManager.Instance.CurrentSystem.improvements);
    }

    public void RebuildPlanets()
    {
        var data = GameManager.Instance != null ? GameManager.Instance.GetPlanetData() : null; if (data == null) return;
        var planets = data.Values.Where(p => p != null).OrderBy(p => p.planetIndex).ToList();
        for (int i = 0; i < planets.Count; i++)
        {
            var planet = planets[i];
            Vector3 desired = planet.worldPosition.sqrMagnitude > 0.01f ? new Vector3(planet.worldPosition.x, 0f, planet.worldPosition.z) : RingPosition(i, planets.Count);
            int tileIndex = Grid.GetNearestTileIndex(desired); var tile = Grid.GetTile(tileIndex);
            if (tile == null) continue;
            int bodyId = planet.celestialBodyId >= 0 ? planet.celestialBodyId : planet.planetIndex;
            if (tile.planetId >= 0 && tile.planetId != bodyId)
                tile = Grid.tiles.Where(t => t.planetId == bodyId || t.planetId < 0)
                    .OrderBy(t => t.planetId == bodyId ? 0 : 1)
                    .ThenBy(t => (Grid.GetWorldPosition(t.tileIndex) - desired).sqrMagnitude).FirstOrDefault();
            if (tile == null) continue;
            tileIndex = tile.tileIndex;
            tile.isPlanetOrbitSector = false;
            tile.associatedPlanetId = -1;
            tile.terrainType = planet.celestialBodyType == GameManager.CelestialBodyType.Moon ? SpaceTerrainType.Moon : SpaceTerrainType.Planet;
            tile.blocksMovement = true; tile.planetId = planet.celestialBodyId >= 0 ? planet.celestialBodyId : planet.planetIndex;
            var marker = CreatePlanetMarker(planet, Grid.GetWorldPosition(tileIndex)); marker.AnchorSpaceTileIndex = tileIndex; var view = marker.GetComponent<SpacePlanetView>() ?? marker.gameObject.AddComponent<SpacePlanetView>(); view.entityId = tile.planetId; markersByPlanet[planet.planetIndex] = marker;
        }
        foreach (var marker in markersByPlanet.Values)
        {
            var anchor = Grid.GetTile(marker.AnchorSpaceTileIndex);
            if (anchor == null) continue;
            foreach (int neighbor in Grid.GetNeighbors(anchor.tileIndex))
            {
                var tile = Grid.GetTile(neighbor);
                if (tile.blocksMovement || (tile.isPlanetOrbitSector && tile.associatedPlanetId != anchor.planetId)) continue;
                tile.isPlanetOrbitSector = true;
                tile.associatedPlanetId = anchor.planetId;
                int dq = tile.q - anchor.q, dr = tile.r - anchor.r;
                tile.orbitSectorDirection = dq == 1 && dr == 0 ? 0 : dq == 1 && dr == -1 ? 1 :
                    dq == 0 && dr == -1 ? 2 : dq == -1 && dr == 0 ? 3 : dq == -1 && dr == 1 ? 4 : 5;
            }
        }
        RefreshCurrentPlanetHighlight(); RefreshHexVisuals();
    }

    public void RefreshCurrentPlanetHighlight()
    {
        int current = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : -1;
        foreach (var kv in markersByPlanet) kv.Value.SetSelectionState(kv.Key == selectedPlanetIndex, kv.Key == current);
    }
    public void RefreshTravelVisuals()
    {
        if (!isVisible) return;
        int current = selectedShip != null ? selectedShip.currentSpaceTileIndex
            : SpaceMapSelectionInfo.FindFleet(selectedFleetId)?.currentSpaceTileIndex ?? -1;
        if (pendingPath != null && pendingPath.Count > 0 && pendingPath[0] != current)
        { pendingDestinationTile = -1; pendingPath = null; }
        RefreshShipVisuals(); RefreshHexVisuals(); RefreshRouteVisuals();
    }
    public int GetPlanetAnchorTile(int planetIndex) => markersByPlanet.TryGetValue(planetIndex, out var m) ? m.AnchorSpaceTileIndex : -1;
    public List<int> GetPlanetOrbitSectors(int planetIndex)
    {
        var result = new List<int>();
        if (Grid == null || !markersByPlanet.TryGetValue(planetIndex, out var marker)) return result;
        var anchor = Grid.GetTile(marker.AnchorSpaceTileIndex);
        if (anchor == null) return result;
        foreach (int index in Grid.GetNeighbors(anchor.tileIndex))
        {
            var tile = Grid.GetTile(index);
            if (!tile.blocksMovement && tile.isPlanetOrbitSector && tile.associatedPlanetId == anchor.planetId)
                result.Add(index);
        }
        return result;
    }

    public SpaceMapSelectionKind SelectionKind { get; private set; }

    public void SelectPlanetMarker(SpaceMapPlanetMarker marker)
    {
        if (marker == null) return;
        ResetSelection();
        SelectionKind = SpaceMapSelectionKind.Planet;
        selectedPlanetIndex = marker.PlanetIndex;
        ApplySelectionVisuals();
        spaceMapUI?.SelectPlanet(marker.PlanetData);
    }

    /// <summary>Selects a deployed ship from the map. Only this selection can receive movement orders.</summary>
    public void SelectShip(BaseUnit unit) => SetShipSelection(unit, false);

    /// <summary>Shows a ship's card without making it orderable, e.g. when opened from a fleet roster.</summary>
    public void InspectShip(BaseUnit unit) => SetShipSelection(unit, true);

    public void SelectFleet(SpaceFleet fleet)
    {
        if (fleet == null) { ClearSelection(); return; }
        ResetSelection();
        SelectionKind = SpaceMapSelectionKind.Fleet;
        selectedFleetId = fleet.fleetId;
        ApplySelectionVisuals();
        spaceMapUI?.SelectFleet(fleet);
    }

    public void SelectSector(int tileIndex)
    {
        if (Grid == null || Grid.GetTile(tileIndex) == null) { ClearSelection(); return; }
        ResetSelection();
        SelectionKind = SpaceMapSelectionKind.Sector;
        selectedSectorTile = tileIndex;
        ApplySelectionVisuals();
        spaceMapUI?.SelectSector(tileIndex);
    }

    private void SelectImprovement(SpaceImprovementInstance improvement)
    {
        ResetSelection();
        SelectionKind = SpaceMapSelectionKind.Improvement;
        selectedSectorTile = improvement.spaceTileIndex;
        selectedImprovementId = improvement.instanceId;
        ApplySelectionVisuals();
        spaceMapUI?.SelectImprovement(improvement);
    }

    public void ClearSelection()
    {
        ResetSelection();
        ApplySelectionVisuals();
        spaceMapUI?.ShowNoSelection();
    }

    private void SetShipSelection(BaseUnit unit, bool inspectionOnly)
    {
        if (unit == null) { ClearSelection(); return; }
        ResetSelection();
        SelectionKind = SpaceMapSelectionKind.Ship;
        selectedShip = unit;
        selectedShipIsInspection = inspectionOnly;
        ApplySelectionVisuals();
        spaceMapUI?.SelectShip(unit);
    }

    private void ResetSelection()
    {
        SelectionKind = SpaceMapSelectionKind.None;
        selectedShip = null;
        selectedShipIsInspection = false;
        selectedFleetId = -1;
        selectedPlanetIndex = -1;
        selectedSectorTile = -1;
        selectedImprovementId = -1;
        pendingDestinationTile = -1;
        pendingPath = null;
    }

    // ── Pending route (the destination preview shown before a movement order is committed) ──

    public readonly struct RoutePreview
    {
        public readonly string entityName;
        public readonly bool isFleet;
        public readonly int destinationTile;
        public readonly int hexes;
        public readonly int cost;
        public readonly int availablePoints;
        public readonly int pointsPerTurn;

        public RoutePreview(string entityName, bool isFleet, int destinationTile, int hexes, int cost, int availablePoints, int pointsPerTurn)
        {
            this.entityName = entityName; this.isFleet = isFleet; this.destinationTile = destinationTile;
            this.hexes = hexes; this.cost = cost; this.availablePoints = availablePoints; this.pointsPerTurn = pointsPerTurn;
        }
    }

    public bool HasPendingRoute => pendingPath != null;
    public int PendingDestinationTile => pendingDestinationTile;

    /// <summary>Describes the uncommitted route using the movement controller's own cost rules.</summary>
    public bool TryGetPendingRoute(out RoutePreview preview)
    {
        preview = default;
        if (!IsPendingRouteValid()) return false;
        var movement = SpaceShipMovementController.Instance;
        var fleet = GetOrderableFleet();
        int hexes = pendingPath.Count - 1;
        if (fleet != null)
        {
            if (!movement.TryGetPackedMembers(fleet, out var members, out _)) return false;
            // A formation moves at the pace of its most constrained member.
            int cost = 0, available = int.MaxValue, perTurn = int.MaxValue;
            foreach (var member in members)
            {
                cost = Mathf.Max(cost, movement.GetRouteCost(member, pendingPath, 0));
                available = Mathf.Min(available, member.currentSpaceMovementPoints);
                perTurn = Mathf.Min(perTurn, member.spaceMovementPointsPerTurn);
            }
            string fleetName = string.IsNullOrEmpty(fleet.fleetName) ? $"Fleet {fleet.fleetId}" : fleet.fleetName;
            preview = new RoutePreview(fleetName, true, pendingDestinationTile, hexes, cost, available, perTurn);
            return true;
        }
        preview = new RoutePreview(SpaceMapSelectionInfo.UnitName(selectedShip), false, pendingDestinationTile, hexes,
            movement.GetRouteCost(selectedShip, pendingPath, 0), selectedShip.currentSpaceMovementPoints, selectedShip.spaceMovementPointsPerTurn);
        return true;
    }

    /// <summary>Commits the previewed route. Clicking the destination a second time and the Confirm button both end here.</summary>
    public bool ConfirmPendingRoute()
    {
        if (pendingPath == null) return false;
        if (!IsPendingRouteValid())
        {
            CancelPendingRoute();
            UIManager.Instance?.ShowNotification("That route is no longer valid.");
            return false;
        }
        ConfirmDestination(pendingDestinationTile);
        return true;
    }

    /// <summary>Drops only the uncommitted preview. Routes that were already queued are left alone.</summary>
    public void CancelPendingRoute()
    {
        if (pendingPath == null && pendingDestinationTile < 0) return;
        pendingDestinationTile = -1;
        pendingPath = null;
        RefreshHexVisuals();
        RefreshRouteVisuals();
    }

    private bool IsPendingRouteValid()
    {
        if (pendingPath == null || pendingPath.Count < 2 || Grid == null || SpaceShipMovementController.Instance == null) return false;
        if (pendingPath[pendingPath.Count - 1] != pendingDestinationTile) return false;
        var destination = Grid.GetTile(pendingDestinationTile);
        if (destination == null || destination.blocksMovement) return false;
        var fleet = GetOrderableFleet();
        if (fleet != null) return pendingPath[0] == fleet.currentSpaceTileIndex;
        return CanOrderSelectedShip() && pendingPath[0] == selectedShip.currentSpaceTileIndex;
    }

    // ── Selection state read by the UI ──

    /// <summary>True for a living, player-owned ship picked on the map itself; roster inspection never qualifies.</summary>
    public bool SelectedShipAcceptsCommands
    {
        get
        {
            var player = SpaceMapSelectionInfo.PlayerCivilization;
            return SelectionKind == SpaceMapSelectionKind.Ship && SpaceMapSelectionInfo.IsAlive(selectedShip) &&
                !selectedShipIsInspection && player != null && selectedShip.owner == player;
        }
    }

    public Color SelectionColor => selectedSectorColor;
    public Color ReachableColor => reachableColor;
    public Color BlockedColor => blockedColor;
    public Color RouteColor => routeEndColor;
    public Color ForeignMarkerTint => foreignMarkerTint;
    public Sprite ShipMarkerSprite => shipMarkerSprite;
    public Sprite FleetMarkerSprite => fleetMarkerSprite;
    public Sprite InfrastructureMarkerSprite => infrastructureMarkerSprite;

    /// <summary>Called after the UI resolves a confirmed attack, so markers and the selection follow the outcome.</summary>
    public void NotifyCombatResolved(BaseUnit attacker)
    {
        RefreshTravelVisuals();
        if (SpaceMapSelectionInfo.IsAlive(attacker) && attacker.currentSpaceTileIndex >= 0 && !attacker.isPackedInSpaceFleet)
            SelectShip(attacker);
        else
            ClearSelection();
    }

    // An attack is offered only for the player's own orderable ship against a hostile ship the combat rules accept.
    private bool TryBeginAttack(BaseUnit clicked)
    {
        var combat = SpaceCombatManager.Instance;
        var attacker = selectedShip as CombatUnit;
        var target = clicked as CombatUnit;
        var player = SpaceMapSelectionInfo.PlayerCivilization;
        if (combat == null || spaceMapUI == null || attacker == null || target == null || target == attacker || attacker.data == null) return false;
        if (player == null || attacker.owner != player || !CanOrderSelectedShip()) return false;
        if (!AircraftMissionManager.IsHostile(attacker.owner, target.owner)) return false;
        if (!combat.CanDirectAttack(attacker, target, out string reason))
        {
            if (attacker.data.canAttackSpace && attacker.data.canDirectlyAttackSpacecraft)
                UIManager.Instance?.ShowNotification($"Cannot attack {SpaceMapSelectionInfo.UnitName(target)}: {reason}.");
            return false;
        }
        CancelPendingRoute();
        return spaceMapUI.ShowCombatPreview(attacker, target);
    }

    private void ApplySelectionVisuals()
    {
        RefreshCurrentPlanetHighlight();
        RefreshHexVisuals();
        RefreshRouteVisuals();
        RefreshEntityHighlights();
    }

    // Movement orders are limited to a ship the player picked on the map; roster inspection and foreign ships never qualify.
    private bool CanOrderSelectedShip()
    {
        if (selectedShip == null || selectedShipIsInspection) return false;
        if (selectedShip.currentHealth <= 0 || selectedShip.isPackedInSpaceFleet || selectedShip.currentSpaceTileIndex < 0) return false;
        var player = SpaceMapSelectionInfo.PlayerCivilization;
        return player == null || selectedShip.owner == player;
    }

    private SpaceFleet GetOrderableFleet()
    {
        if (SelectionKind != SpaceMapSelectionKind.Fleet || SpaceShipMovementController.Instance == null) return null;
        var fleet = SpaceMapSelectionInfo.FindFleet(selectedFleetId);
        var player = SpaceMapSelectionInfo.PlayerCivilization;
        if (fleet == null || player == null || fleet.ownerCivilizationId != player.gameObject.GetRuntimeId()) return null;
        return SpaceShipMovementController.Instance.TryGetPackedMembers(fleet, out _, out _) ? fleet : null;
    }

    private void HandleSelectionInput()
    {
        if (spaceMapCamera == null || Mouse.current == null) return;
        bool inspect = Mouse.current.rightButton.wasPressedThisFrame;
        if (!inspect && !Mouse.current.leftButton.wasPressedThisFrame) return;
        // An open forecast or confirmation owns the click; it must never reach the map behind it.
        if (ConfirmationDialogService.IsVisible || (spaceMapUI != null && spaceMapUI.BlocksMapInput)) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        Ray ray = spaceMapCamera.ScreenPointToRay(Mouse.current.position.ReadValue()); if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity)) return;
        var world = SpaceWorldManager.Instance;
        if (inspect)
        {
            // Markers float above their sector, so inspection looks past them to the structure or sector underneath.
            foreach (var candidate in Physics.RaycastAll(ray, Mathf.Infinity).OrderBy(h => h.distance))
            {
                var view = candidate.collider.GetComponentInParent<SpaceImprovementView>();
                if (view != null && world != null && world.Entities.TryGetImprovement(view.entityId, out var structure))
                {
                    SelectImprovement(structure);
                    return;
                }
                if (candidate.collider.TryGetComponent<SpaceHexTileView>(out var sector))
                {
                    SelectSector(sector.tileIndex);
                    return;
                }
            }
            return;
        }
        var fleetView = hit.collider.GetComponentInParent<SpaceFleetView>();
        if (fleetView != null)
        {
            var clickedFleet = SpaceMapSelectionInfo.FindFleet(fleetView.fleetId);
            if (clickedFleet != null) SelectFleet(clickedFleet); else { ClearSelection(); RefreshTravelVisuals(); }
            return;
        }
        var shipView = hit.collider.GetComponentInParent<SpaceShipView>();
        var ship = shipView != null ? shipView.Unit : hit.collider.GetComponentInParent<BaseUnit>();
        if (ship != null && ship.currentSpaceTileIndex >= 0 && !ship.isPackedInSpaceFleet)
        {
            if (!TryBeginAttack(ship)) SelectShip(ship);
            return;
        }
        var marker = hit.collider.GetComponentInParent<SpaceMapPlanetMarker>(); if (marker != null) { SelectPlanetMarker(marker); return; }
        var improvementView = hit.collider.GetComponentInParent<SpaceImprovementView>();
        if (improvementView != null && world != null && world.Entities.TryGetImprovement(improvementView.entityId, out var improvement))
        {
            if (pendingDestinationTile == improvement.spaceTileIndex) ConfirmPendingRoute();
            else SelectImprovement(improvement);
            return;
        }
        var hex = hit.collider.GetComponent<SpaceHexTileView>();
        if (hex == null) return;
        if (pendingDestinationTile == hex.tileIndex) { ConfirmPendingRoute(); return; }
        var fleet = GetOrderableFleet();
        bool shipOrder = CanOrderSelectedShip();
        if (!shipOrder && fleet == null) { SelectSector(hex.tileIndex); return; }
        int currentTile = fleet != null ? fleet.currentSpaceTileIndex : selectedShip.currentSpaceTileIndex;
        if (hex.tileIndex == currentTile) return;
        var movement = SpaceShipMovementController.Instance;
        var path = fleet != null ? movement.FindFleetPath(fleet, hex.tileIndex) : movement.FindShipPath(selectedShip, hex.tileIndex);
        if (path.Count < 2)
        {
            pendingDestinationTile = -1; pendingPath = null;
            RefreshHexVisuals(); RefreshRouteVisuals();
            UIManager.Instance?.ShowNotification("No valid space route is available to that destination.");
            return;
        }
        pendingDestinationTile = hex.tileIndex;
        pendingPath = path;
        RefreshHexVisuals();
        SetHexColor(hex.tileIndex, Color.yellow);
        RefreshRouteVisuals();
    }

    private void ConfirmDestination(int tileIndex)
    {
        var fleet = GetOrderableFleet();
        pendingDestinationTile = -1;
        pendingPath = null;
        if (fleet == null && !CanOrderSelectedShip()) { ClearSelection(); return; }
        bool queued = fleet != null
            ? SpaceShipMovementController.Instance.QueueFleetMove(fleet, tileIndex)
            : SpaceShipMovementController.Instance.QueueMove(selectedShip, tileIndex);
        if (!queued) UIManager.Instance?.ShowNotification("No valid space route is available to that destination.");
        RefreshTravelVisuals();
        if (fleet != null) spaceMapUI?.SelectFleet(fleet);
        else spaceMapUI?.SelectShip(selectedShip);
    }

    private void BuildHexVisuals()
    {
        foreach (var tile in Grid.tiles)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); go.name = $"SpaceHex_{tile.tileIndex}_{tile.q}_{tile.r}"; go.transform.SetParent(hexRoot, false);
            go.transform.position = Grid.GetWorldPosition(tile.tileIndex) - Vector3.up * 0.04f; go.transform.localScale = new Vector3(hexSize * 0.85f, 0.02f, hexSize * 0.85f);
            go.AddComponent<SpaceHexTileView>().tileIndex = tile.tileIndex; hexObjects[tile.tileIndex] = go;
        }
    }

    private void HighlightReachableTiles(IReadOnlyList<BaseUnit> units, int start)
    {
        var reachable = SpaceShipMovementController.Instance.GetReachableTiles(units, start);
        foreach (var tile in Grid.tiles)
        {
            SetHexColor(tile.tileIndex, reachable.Contains(tile.tileIndex) ? reachableColor : (tile.blocksMovement ? blockedColor : hexColor));
        }
    }
    private void RefreshHexVisuals()
    {
        if (Grid == null) return;
        foreach (var t in Grid.tiles) SetHexColor(t.tileIndex, t.blocksMovement ? blockedColor : hexColor);
        if (CanOrderSelectedShip()) HighlightReachableTiles(new[] { selectedShip }, selectedShip.currentSpaceTileIndex);
        else
        {
            var fleet = GetOrderableFleet();
            if (fleet != null && SpaceShipMovementController.Instance.TryGetPackedMembers(fleet, out var members, out _))
                HighlightReachableTiles(members, fleet.currentSpaceTileIndex);
        }
        if (selectedSectorTile >= 0) SetHexColor(selectedSectorTile, selectedSectorColor);
        if (pendingDestinationTile >= 0) SetHexColor(pendingDestinationTile, Color.yellow);
    }
    private void SetHexColor(int index, Color color)
    {
        if (!hexObjects.TryGetValue(index, out var go)) return;
        var renderer = go.GetComponent<Renderer>();
        if (renderer == null) return;
        if (renderer.sharedMaterial == null)
        {
            fallbackHexMaterial ??= new Material(Shader.Find("Standard"));
            renderer.sharedMaterial = fallbackHexMaterial;
        }
        renderer.GetPropertyBlock(colorProperties);
        colorProperties.SetColor("_Color", color);
        colorProperties.SetColor("_BaseColor", color);
        renderer.SetPropertyBlock(colorProperties);
    }

    private void RefreshShipVisuals()
    {
        ClearChildren(unitRoot);
        shipMarkers.Clear();
        fleetMarkers.Clear();
        if (Grid == null) return;
        var player = SpaceMapSelectionInfo.PlayerCivilization;
        var deployedUnits = FindObjectsByType<BaseUnit>(FindObjectsInactive.Exclude)
            .Concat(UnitRegistry.GetCombatUnits()).Distinct();
        foreach (var unit in deployedUnits)
        {
            if (!SpaceMapSelectionInfo.IsAlive(unit) || Grid.GetTile(unit.currentSpaceTileIndex) == null || unit.isPackedInSpaceFleet) continue;
            int unitId = unit.gameObject.GetRuntimeId();
            var marker = CreateMarker($"SpaceShipView_{unitId}", unitRoot, shipMarkerSprite, shipMarkerArtArea,
                Grid.GetWorldPosition(unit.currentSpaceTileIndex), Vector3.zero, 1.2f, hexSize * shipMarkerSize, ShipSortingOrder);
            marker.baseTint = player != null && unit.owner != player ? foreignMarkerTint : Color.white;
            marker.root.gameObject.AddComponent<SpaceShipView>().Initialize(unit);
            shipMarkers[unitId] = marker;
        }

        // Fleets get their own marker so they are selectable as fleets, including packed ones whose ships are inactive.
        SpaceMapSelectionInfo.CollectFleets(fleetBuffer);
        foreach (var fleet in fleetBuffer)
        {
            int tileIndex = GetFleetMarkerTile(fleet, out bool besideShips);
            if (Grid.GetTile(tileIndex) == null) continue;
            Vector3 offset = besideShips ? new Vector3(hexSize * 0.45f, 0f, hexSize * 0.45f) : Vector3.zero;
            var marker = CreateMarker($"SpaceFleetView_{fleet.fleetId}", unitRoot, fleetMarkerSprite, fleetMarkerArtArea,
                Grid.GetWorldPosition(tileIndex), offset, 1.4f,
                hexSize * (besideShips ? escortFleetMarkerSize : fleetMarkerSize),
                besideShips ? EscortFleetSortingOrder : ShipSortingOrder);
            marker.baseTint = player != null && SpaceMapSelectionInfo.GetFleetOwner(fleet) != player ? foreignMarkerTint : Color.white;
            marker.root.gameObject.AddComponent<SpaceFleetView>().fleetId = fleet.fleetId;
            fleetMarkers[fleet.fleetId] = marker;
        }
        lastEntitySignature = GetEntitySignature();
        RefreshEntityHighlights();
    }

    private void RefreshImprovementMarkers(List<SpaceImprovementInstance> improvements)
    {
        foreach (var marker in improvementMarkers.Values)
        {
            if (marker.root == null) continue;
            marker.root.gameObject.SetActive(false);
            Destroy(marker.root.gameObject);
        }
        improvementMarkers.Clear();
        lastImprovementSignature = GetImprovementSignature(improvements);
        if (Grid == null) return;
        var player = SpaceMapSelectionInfo.PlayerCivilization;
        int playerId = player != null ? player.gameObject.GetRuntimeId() : -1;
        var offsets = new Dictionary<int, int>();
        foreach (var improvement in improvements)
        {
            if (improvement == null || Grid.GetTile(improvement.spaceTileIndex) == null) continue;
            offsets.TryGetValue(improvement.spaceTileIndex, out int next);
            offsets[improvement.spaceTileIndex] = next + 1;

            // Structures sharing a sector are spread around its centre, one marker each.
            float angle = next * 60f * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (Grid.tileSize * 0.48f);
            var marker = CreateMarker($"SpaceImprovementView_{improvement.instanceId}", hexRoot, infrastructureMarkerSprite, infrastructureMarkerArtArea,
                Grid.GetWorldPosition(improvement.spaceTileIndex), offset, 0.65f, Grid.tileSize * infrastructureMarkerSize, InfrastructureSortingOrder);
            marker.baseTint = player != null && improvement.ownerCivilizationId != playerId ? foreignMarkerTint : Color.white;
            marker.root.gameObject.AddComponent<SpaceImprovementView>().entityId = improvement.instanceId;
            AddStructureIcon(marker, SpaceImprovementRegistry.GetData(improvement.improvementDataId)?.icon);
            improvementMarkers.Add(improvement.instanceId, marker);
        }
        RefreshEntityHighlights();
    }

    // The universal base has an empty recess; each structure's own icon is placed in it at runtime.
    private void AddStructureIcon(MapMarker marker, Sprite icon)
    {
        bool generic = icon == null;
        if (generic) icon = SpaceMapMarkerArt.Fallback;
        var iconObject = new GameObject("Structure Icon");
        iconObject.transform.SetParent(marker.root, false);
        iconObject.transform.localPosition = new Vector3(marker.visible.center.x, marker.visible.center.y, 0f);
        var renderer = iconObject.AddComponent<SpriteRenderer>();
        renderer.sprite = icon;
        renderer.sortingOrder = InfrastructureSortingOrder + 1;
        Rect iconRect = SpaceMapMarkerArt.GetVisibleRect(icon);
        float target = marker.visible.width * infrastructureIconSize * (generic ? 0.5f : 1f);
        iconObject.transform.localScale = Vector3.one * (target / Mathf.Max(0.0001f, Mathf.Max(iconRect.width, iconRect.height)));
        iconObject.transform.localPosition -= new Vector3(iconRect.center.x, iconRect.center.y, 0f) * iconObject.transform.localScale.x;
    }

    /// <summary>
    /// Builds one world-space marker: the sprite faces the map camera, keeps a collider the size of its
    /// visible artwork for selection, and carries a hidden ring that is shown while it is selected.
    /// </summary>
    private MapMarker CreateMarker(string markerName, Transform parent, Sprite sprite, Rect artArea, Vector3 anchor, Vector3 offset, float height, float width, int sortingOrder)
    {
        if (sprite == null) { sprite = SpaceMapMarkerArt.Fallback; artArea = default; }
        var go = new GameObject(markerName);
        go.transform.SetParent(parent, false);
        go.transform.rotation = spaceMapCamera != null ? spaceMapCamera.transform.rotation : Quaternion.Euler(90f, 0f, 0f);
        var art = go.AddComponent<SpriteRenderer>();
        art.sprite = sprite;
        art.sortingOrder = sortingOrder;

        Rect visible = SpaceMapMarkerArt.GetVisibleRect(sprite, artArea);
        var selection = go.AddComponent<BoxCollider>();
        selection.center = new Vector3(visible.center.x, visible.center.y, 0f);
        selection.size = new Vector3(visible.width, visible.height, 0.05f);

        var ringObject = new GameObject("Selection Ring");
        ringObject.transform.SetParent(go.transform, false);
        ringObject.transform.localPosition = new Vector3(visible.center.x, visible.center.y, 0f);
        var ring = ringObject.AddComponent<SpriteRenderer>();
        ring.sprite = SpaceMapMarkerArt.Ring;
        ring.color = selectedEntityColor;
        ring.sortingOrder = sortingOrder - 1;
        ring.enabled = false;
        ringObject.transform.localScale = Vector3.one * (Mathf.Max(visible.width, visible.height) * 1.24f / SpaceMapMarkerArt.Ring.bounds.size.x);

        var marker = new MapMarker { root = go.transform, art = art, ring = ring, anchor = anchor, offset = offset, height = height, width = width, visible = visible, baseTint = Color.white };
        LayoutMarker(marker);
        return marker;
    }

    private void LayoutMarker(MapMarker marker)
    {
        if (marker.root == null) return;
        float scale = marker.width * markerZoom / Mathf.Max(0.0001f, marker.visible.width);
        marker.root.localScale = Vector3.one * scale;
        // Centre the visible artwork, not the padded sprite rectangle, on the marker's position.
        Vector3 centre = marker.anchor + marker.offset * markerZoom + Vector3.up * marker.height;
        marker.root.position = centre - marker.root.rotation * new Vector3(marker.visible.center.x, marker.visible.center.y, 0f) * scale;
    }

    // Keeps markers readable when zoomed out without letting them outgrow their hexes when zoomed in.
    private void UpdateMarkerZoom(bool force)
    {
        float zoom = 1f;
        if (spaceMapCamera != null && spaceMapCamera.orthographic && Screen.height > 0)
        {
            float worldPerPixel = spaceMapCamera.orthographicSize * 2f / Screen.height;
            float shipWidth = Mathf.Max(0.01f, hexSize * shipMarkerSize);
            zoom = Mathf.Clamp(worldPerPixel * minimumShipMarkerPixels / shipWidth, 1f, Mathf.Max(1f, maximumMarkerZoomScale));
        }
        if (!force && Mathf.Abs(zoom - markerZoom) < 0.01f) return;
        markerZoom = zoom;
        foreach (var marker in shipMarkers.Values) LayoutMarker(marker);
        foreach (var marker in fleetMarkers.Values) LayoutMarker(marker);
        foreach (var marker in improvementMarkers.Values) LayoutMarker(marker);
    }

    private int GetEntitySignature()
    {
        unchecked
        {
            int hash = 17;
            foreach (var unit in UnitRegistry.GetCombatUnits())
            {
                if (unit == null) continue;
                hash = hash * 31 + unit.gameObject.GetRuntimeId();
                hash = hash * 31 + unit.currentSpaceTileIndex;
                hash = hash * 31 + (unit.isPackedInSpaceFleet ? 1 : 0) + (unit.currentHealth > 0 ? 2 : 0);
            }
            SpaceMapSelectionInfo.CollectFleets(fleetBuffer);
            foreach (var fleet in fleetBuffer)
            {
                hash = hash * 31 + fleet.fleetId;
                hash = hash * 31 + GetFleetMarkerTile(fleet, out bool besideShips);
                hash = hash * 31 + (fleet.isPacked ? 1 : 0) + (besideShips ? 2 : 0);
            }
            return hash;
        }
    }

    private static int GetImprovementSignature(List<SpaceImprovementInstance> improvements)
    {
        unchecked
        {
            int hash = improvements.Count;
            foreach (var improvement in improvements)
                if (improvement != null) hash = hash * 31 + improvement.instanceId * 7 + improvement.spaceTileIndex;
            return hash;
        }
    }

    // Packed fleets sit on the fleet's own tile; unpacked fleets follow their first surviving member.
    private int GetFleetMarkerTile(SpaceFleet fleet, out bool besideShips)
    {
        besideShips = false;
        if (fleet.isPacked || fleet.memberUnitIds == null) return fleet.currentSpaceTileIndex;
        foreach (int id in fleet.memberUnitIds)
        {
            var unit = SpaceMapSelectionInfo.ResolveUnit(id);
            if (!SpaceMapSelectionInfo.IsAlive(unit) || unit.currentSpaceTileIndex < 0) continue;
            besideShips = true;
            return unit.currentSpaceTileIndex;
        }
        return fleet.currentSpaceTileIndex;
    }

    private void RefreshEntityHighlights()
    {
        var selectedFleet = SelectionKind == SpaceMapSelectionKind.Fleet ? SpaceMapSelectionInfo.FindFleet(selectedFleetId) : null;
        int selectedShipId = selectedShip != null ? selectedShip.gameObject.GetRuntimeId() : 0;
        foreach (var pair in shipMarkers)
        {
            bool selected = selectedShip != null && pair.Key == selectedShipId;
            bool fleetMember = !selected && selectedFleet != null && selectedFleet.memberUnitIds != null && selectedFleet.memberUnitIds.Contains(pair.Key);
            SetMarkerState(pair.Value, selected, fleetMember ? fleetMemberTint : pair.Value.baseTint);
        }
        foreach (var pair in fleetMarkers)
            SetMarkerState(pair.Value, selectedFleet != null && pair.Key == selectedFleetId, pair.Value.baseTint);
        foreach (var pair in improvementMarkers)
            SetMarkerState(pair.Value, SelectionKind == SpaceMapSelectionKind.Improvement && pair.Key == selectedImprovementId, pair.Value.baseTint);
    }

    // Selection is shown by a ring around the marker, so the state never depends on recolouring the artwork.
    private static void SetMarkerState(MapMarker marker, bool selected, Color tint)
    {
        if (marker.art != null) marker.art.color = tint;
        if (marker.ring != null) marker.ring.enabled = selected;
    }

    private void RefreshRouteVisuals()
    {
        ClearChildren(routeRoot);
        if (Grid == null) return;
        List<int> path = null; int cursor = 0;
        var player = SpaceMapSelectionInfo.PlayerCivilization;
        if (selectedShip != null && (player == null || selectedShip.owner == player))
        {
            path = selectedShip.queuedSpacePath; cursor = selectedShip.queuedSpacePathCursor;
        }
        else if (SelectionKind == SpaceMapSelectionKind.Fleet)
        {
            var fleet = SpaceMapSelectionInfo.FindFleet(selectedFleetId);
            if (fleet != null && (player == null || SpaceMapSelectionInfo.GetFleetOwner(fleet) == player)) { path = fleet.queuedPath; cursor = fleet.queuedPathCursor; }
        }
        if (pendingPath != null) { path = pendingPath; cursor = 0; }
        if (path == null || path.Count < 2) return;
        var route = new GameObject("SelectedRoute");
        route.transform.SetParent(routeRoot, false);
        var line = route.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.widthMultiplier = 0.35f;
        int firstPathIndex = Mathf.Clamp(cursor, 0, path.Count - 1);
        line.positionCount = path.Count - firstPathIndex;
        line.startColor = reachableColor;
        line.endColor = routeEndColor;
        routeMaterial ??= new Material(Shader.Find("Sprites/Default"));
        line.sharedMaterial = routeMaterial;
        for (int i = firstPathIndex; i < path.Count; i++)
            line.SetPosition(i - firstPathIndex, Grid.GetWorldPosition(path[i]) + Vector3.up * 0.45f);
    }

    private SpaceMapPlanetMarker CreatePlanetMarker(GameManager.PlanetData planet, Vector3 position)
    {
        SpaceMapPlanetMarker marker = planetMarkerPrefab != null ? Instantiate(planetMarkerPrefab, planetRoot) : new GameObject("SpaceMapPlanetMarker").AddComponent<SpaceMapPlanetMarker>();
        marker.transform.SetParent(planetRoot, false); marker.transform.position = position; marker.Initialize(planet, this, GetPlanetRadius(planet), planetMaterialTemplate); return marker;
    }
    private Vector3 RingPosition(int i, int count) { if (i == 0) return Vector3.zero; float a = (360f / Mathf.Max(1, count - 1)) * (i - 1) * Mathf.Deg2Rad; float r = Mathf.Lerp(hexSize * 3f, gridRadius * hexSize * 0.75f, i / Mathf.Max(1f, count - 1f)); return new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r); }
    private float GetPlanetRadius(GameManager.PlanetData planet) { if (!SpaceMapSelectionInfo.IsPlanetDiscovered(planet)) return minPlanetRadius; float t = planet.planetSize == GameManager.MapSize.Small ? .35f : planet.planetSize == GameManager.MapSize.Large ? .85f : .55f; if (planet.planetType == GameManager.PlanetType.Gas_Giant) t = 1f; if (planet.celestialBodyType == GameManager.CelestialBodyType.Moon) t *= .45f; return Mathf.Lerp(minPlanetRadius, maxPlanetRadius, Mathf.Clamp01(t)); }

    private void EnsureSceneObjects()
    {
        if (mapRoot == null) { var go = new GameObject("SpaceMapWorldRoot"); go.transform.SetParent(transform, false); mapRoot = go.transform; }
        if (hexRoot == null) { var go = new GameObject("SpaceHexGrid"); go.transform.SetParent(mapRoot, false); hexRoot = go.transform; }
        if (planetRoot == null) { var go = new GameObject("SpaceMapPlanets"); go.transform.SetParent(mapRoot, false); planetRoot = go.transform; }
        if (unitRoot == null) { var go = new GameObject("SpaceMapShips"); go.transform.SetParent(mapRoot, false); unitRoot = go.transform; }
        if (routeRoot == null) { var go = new GameObject("SpaceMapRoutes"); go.transform.SetParent(mapRoot, false); routeRoot = go.transform; }
        if (spaceMapCamera == null) { var go = new GameObject("SpaceMapCamera"); go.transform.SetParent(mapRoot, false); go.transform.position = new Vector3(0f, 140f, 0f); go.transform.rotation = Quaternion.Euler(90f,0f,0f); spaceMapCamera = go.AddComponent<Camera>(); spaceMapCamera.orthographic = true; spaceMapCamera.orthographicSize = 110f; spaceMapCamera.depth = 100f; }
        if (cameraController == null) cameraController = spaceMapCamera.GetComponent<SpaceMapCameraController>() ?? spaceMapCamera.gameObject.AddComponent<SpaceMapCameraController>();
    }
    private void CenterOnCurrentPlanet() { int current = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : -1; if (current >= 0 && markersByPlanet.TryGetValue(current, out var m)) cameraController?.CenterOn(m.transform.position); }
    private void SetMapActive(bool active) { isVisible = active; if (mapRoot != null) mapRoot.gameObject.SetActive(active); if (spaceMapCamera != null) spaceMapCamera.enabled = active; }
    private void HideGameplayCameras() { hiddenGameplayCameraMasks.Clear(); foreach (var cam in Camera.allCameras) if (cam != null && cam != spaceMapCamera && (cam.CompareTag("MainCamera") || cam.GetComponent<PlanetaryCameraManager>() != null)) { hiddenGameplayCameraMasks[cam] = cam.cullingMask; cam.cullingMask = 0; } }
    private void RestoreGameplayCameras() { foreach (var kv in hiddenGameplayCameraMasks) if (kv.Key != null) kv.Key.cullingMask = kv.Value; hiddenGameplayCameraMasks.Clear(); }
    private void ClearChildren(Transform root) { if (root == null) return; for (int i = root.childCount - 1; i >= 0; i--) { var child = root.GetChild(i).gameObject; child.SetActive(false); Destroy(child); } }
    private void OnDestroy() { RestoreGameplayCameras(); if (fallbackHexMaterial != null) Destroy(fallbackHexMaterial); if (routeMaterial != null) Destroy(routeMaterial); }
}

public class SpaceHexTileView : MonoBehaviour { public int tileIndex; }

/// <summary>Small generated sprites the map markers share: the selection ring and a restrained generic symbol.</summary>
public static class SpaceMapMarkerArt
{
    private static Sprite ring;
    private static Sprite fallback;

    public static Sprite Ring => ring != null ? ring : ring = Build("Space Map Selection Ring", 128, (x, y) =>
    {
        float distance = Mathf.Sqrt(x * x + y * y);
        return Mathf.Clamp01(1f - Mathf.Abs(distance - 0.86f) / 0.07f);
    }, Color.white);

    /// <summary>Shown when a structure has no icon, or when a marker sprite was never assigned.</summary>
    public static Sprite Fallback => fallback != null ? fallback : fallback = Build("Space Map Generic Symbol", 64, (x, y) =>
        Mathf.Clamp01((0.8f - (Mathf.Abs(x) + Mathf.Abs(y))) / 0.08f), new Color(0.88f, 0.72f, 0.36f, 1f));

    /// <summary>The sprite's opaque area in local units, so padding around the artwork never affects size or clicks.</summary>
    public static Rect GetVisibleRect(Sprite sprite, Rect artArea = default)
    {
        if (artArea.width > 0f && artArea.height > 0f)
        {
            // The sprite's own rectangle in local units, then the stated share of it.
            Vector2 size = sprite.rect.size / sprite.pixelsPerUnit;
            Vector2 origin = -sprite.pivot / sprite.pixelsPerUnit;
            return new Rect(origin.x + artArea.x * size.x, origin.y + artArea.y * size.y, artArea.width * size.x, artArea.height * size.y);
        }
        var vertices = sprite.vertices;
        if (vertices == null || vertices.Length < 3)
            return new Rect(sprite.bounds.min.x, sprite.bounds.min.y, sprite.bounds.size.x, sprite.bounds.size.y);
        Vector2 min = vertices[0], max = vertices[0];
        for (int i = 1; i < vertices.Length; i++) { min = Vector2.Min(min, vertices[i]); max = Vector2.Max(max, vertices[i]); }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static Sprite Build(string spriteName, int size, System.Func<float, float, float> coverage, Color color)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = spriteName, hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float alpha = coverage((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f);
            pixels[y * size + x] = new Color(color.r, color.g, color.b, alpha);
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        sprite.name = spriteName;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}
