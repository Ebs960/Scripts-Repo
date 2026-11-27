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

    public SpaceHexGrid Grid { get; private set; }
    private readonly Dictionary<int, SpaceMapPlanetMarker> markersByPlanet = new Dictionary<int, SpaceMapPlanetMarker>();
    private readonly Dictionary<int, GameObject> hexObjects = new Dictionary<int, GameObject>();
    private readonly Dictionary<Camera, int> hiddenGameplayCameraMasks = new Dictionary<Camera, int>();
    private readonly Dictionary<int, Renderer> shipProxyRenderers = new Dictionary<int, Renderer>();
    private readonly Dictionary<int, Renderer> fleetMarkerRenderers = new Dictionary<int, Renderer>();
    private readonly Dictionary<int, GameObject> improvementMarkers = new Dictionary<int, GameObject>();
    private int lastImprovementCount = -1;
    private readonly List<SpaceFleet> fleetBuffer = new List<SpaceFleet>();
    private static readonly Color shipColor = new Color(0.8f, 0.85f, 0.9f, 1f);
    private static readonly Color fleetColor = new Color(0.85f, 0.65f, 0.2f, 1f);
    private static readonly Color fleetMemberColor = new Color(0.3f, 0.9f, 1f, 1f);
    private static readonly Color selectedEntityColor = Color.yellow;
    private static readonly Color selectedSectorColor = new Color(1f, 0.8f, 0.2f, 0.55f);
    private BaseUnit selectedShip;
    private bool selectedShipIsInspection;
    private int selectedFleetId = -1;
    private int selectedPlanetIndex = -1;
    private int selectedSectorTile = -1;
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
        if (structures != null && lastImprovementCount != structures.Count) RefreshImprovementMarkers(structures);
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
        pendingDestinationTile = -1;
        pendingPath = null;
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
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        Ray ray = spaceMapCamera.ScreenPointToRay(Mouse.current.position.ReadValue()); if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity)) return;
        var world = SpaceWorldManager.Instance;
        if (inspect)
        {
            var view = hit.collider.GetComponentInParent<SpaceImprovementView>();
            if (view != null && world != null && world.Entities.TryGetImprovement(view.entityId, out var structure))
                SelectImprovement(structure);
            else if (hit.collider.TryGetComponent<SpaceHexTileView>(out var sector))
                SelectSector(sector.tileIndex);
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
        if (ship != null && ship.currentSpaceTileIndex >= 0 && !ship.isPackedInSpaceFleet) { SelectShip(ship); return; }
        var marker = hit.collider.GetComponentInParent<SpaceMapPlanetMarker>(); if (marker != null) { SelectPlanetMarker(marker); return; }
        var improvementView = hit.collider.GetComponentInParent<SpaceImprovementView>();
        if (improvementView != null && world != null && world.Entities.TryGetImprovement(improvementView.entityId, out var improvement))
        {
            if (pendingDestinationTile == improvement.spaceTileIndex) ConfirmDestination(improvement.spaceTileIndex);
            else SelectImprovement(improvement);
            return;
        }
        var hex = hit.collider.GetComponent<SpaceHexTileView>();
        if (hex == null) return;
        if (pendingDestinationTile == hex.tileIndex) { ConfirmDestination(hex.tileIndex); return; }
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
        UIManager.Instance?.ShowNotification("Click again to confirm movement, or right-click to inspect a sector.");
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
        shipProxyRenderers.Clear();
        fleetMarkerRenderers.Clear();
        if (Grid == null) return;
        var deployedUnits = FindObjectsByType<BaseUnit>(FindObjectsInactive.Exclude)
            .Concat(UnitRegistry.GetCombatUnits()).Distinct();
        foreach (var unit in deployedUnits)
        {
            if (!SpaceMapSelectionInfo.IsAlive(unit) || Grid.GetTile(unit.currentSpaceTileIndex) == null || unit.isPackedInSpaceFleet) continue;
            var proxy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            proxy.name = $"SpaceShipView_{unit.gameObject.GetRuntimeId()}";
            proxy.transform.SetParent(unitRoot, false);
            proxy.transform.position = Grid.GetWorldPosition(unit.currentSpaceTileIndex) + Vector3.up * 1.2f;
            proxy.AddComponent<SpaceShipView>().Initialize(unit);
            shipProxyRenderers[unit.gameObject.GetRuntimeId()] = proxy.GetComponent<Renderer>();
        }

        // Fleets get their own marker so they are selectable as fleets, including packed ones whose ships are inactive.
        SpaceMapSelectionInfo.CollectFleets(fleetBuffer);
        foreach (var fleet in fleetBuffer)
        {
            int tileIndex = GetFleetMarkerTile(fleet, out bool besideShips);
            if (Grid.GetTile(tileIndex) == null) continue;
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = $"SpaceFleetView_{fleet.fleetId}";
            marker.transform.SetParent(unitRoot, false);
            Vector3 offset = besideShips ? new Vector3(hexSize * 0.45f, 0f, hexSize * 0.45f) : Vector3.zero;
            marker.transform.position = Grid.GetWorldPosition(tileIndex) + offset + Vector3.up * 1.4f;
            float diameter = hexSize * (besideShips ? 0.3f : 0.55f);
            marker.transform.localScale = new Vector3(diameter, 0.2f, diameter);
            marker.AddComponent<SpaceFleetView>().fleetId = fleet.fleetId;
            fleetMarkerRenderers[fleet.fleetId] = marker.GetComponent<Renderer>();
        }
        RefreshEntityHighlights();
    }

    private void RefreshImprovementMarkers(List<SpaceImprovementInstance> improvements)
    {
        foreach (var marker in improvementMarkers.Values)
        {
            marker.SetActive(false);
            Destroy(marker);
        }
        improvementMarkers.Clear();
        lastImprovementCount = improvements.Count;
        var offsets = new Dictionary<int, int>();
        foreach (var improvement in improvements)
        {
            if (improvement == null || Grid.GetTile(improvement.spaceTileIndex) == null) continue;
            offsets.TryGetValue(improvement.spaceTileIndex, out int next);
            offsets[improvement.spaceTileIndex] = next + 1;

            // The system map uses lightweight selection proxies for gameplay objects.
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = $"SpaceImprovementView_{improvement.instanceId}";
            marker.transform.SetParent(hexRoot, false);
            float angle = next * 60f * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (Grid.tileSize * 0.48f);
            marker.transform.position = Grid.GetWorldPosition(improvement.spaceTileIndex) + offset + Vector3.up * 0.65f;
            marker.transform.localScale = Vector3.one * (Grid.tileSize * 0.22f);
            marker.AddComponent<SpaceImprovementView>().entityId = improvement.instanceId;
            improvementMarkers.Add(improvement.instanceId, marker);
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
        foreach (var pair in shipProxyRenderers)
        {
            Color color = shipColor;
            if (selectedShip != null && pair.Key == selectedShipId) color = selectedEntityColor;
            else if (selectedFleet != null && selectedFleet.memberUnitIds != null && selectedFleet.memberUnitIds.Contains(pair.Key)) color = fleetMemberColor;
            SetRendererColor(pair.Value, color);
        }
        foreach (var pair in fleetMarkerRenderers)
            SetRendererColor(pair.Value, selectedFleet != null && pair.Key == selectedFleetId ? selectedEntityColor : fleetColor);
    }

    private void SetRendererColor(Renderer renderer, Color color)
    {
        if (renderer == null) return;
        colorProperties ??= new MaterialPropertyBlock();
        renderer.GetPropertyBlock(colorProperties);
        colorProperties.SetColor("_Color", color);
        colorProperties.SetColor("_BaseColor", color);
        renderer.SetPropertyBlock(colorProperties);
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
        line.endColor = Color.cyan;
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
