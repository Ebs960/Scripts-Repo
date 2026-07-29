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
    private BaseUnit selectedShip;
    private int selectedPlanetIndex = -1;
    private int pendingDestinationTile = -1;
    private bool isVisible;
    private Material fallbackHexMaterial;
    private Material routeMaterial;
    private readonly MaterialPropertyBlock colorProperties = new MaterialPropertyBlock();

    private void Awake() { EnsureSceneObjects(); SetMapActive(false); }
    private void Update() { if (isVisible) HandleSelectionInput(); }

    public void ShowMap(SpaceMapUI ui)
    {
        if (ui != null) spaceMapUI = ui;
        EnsureSceneObjects(); HideGameplayCameras(); SetMapActive(true); RebuildSpaceMap(); CenterOnCurrentPlanet();
    }
    public void HideMap() { SetMapActive(false); RestoreGameplayCameras(); }

    public void RebuildSpaceMap()
    {
        if (SpaceWorldManager.Instance == null)
            new GameObject("SpaceWorldManager").AddComponent<SpaceWorldManager>();
        if (SpaceWorldManager.Instance.Grid == null)
            SpaceWorldManager.Instance.CreateSystem(GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0, gridRadius, hexSize);
        var previousGrid = Grid;
        Grid = SpaceWorldManager.Instance.Grid;
        if (SpaceShipMovementController.Instance == null) new GameObject("SpaceShipMovementController").AddComponent<SpaceShipMovementController>();
        SpaceShipMovementController.Instance.SetGrid(Grid);
        bool rebuildHexes = previousGrid != Grid || hexObjects.Count != Grid.tiles.Count;
        if (rebuildHexes) { ClearChildren(hexRoot); hexObjects.Clear(); BuildHexVisuals(); }
        ClearChildren(planetRoot); ClearChildren(unitRoot); ClearChildren(routeRoot); markersByPlanet.Clear();
        RebuildPlanets(); RefreshShipVisuals(); RefreshRouteVisuals();
    }

    public void RebuildPlanets()
    {
        var data = GameManager.Instance != null ? GameManager.Instance.GetPlanetData() : null; if (data == null) return;
        var planets = data.Values.OrderBy(p => p.planetIndex).ToList();
        for (int i = 0; i < planets.Count; i++)
        {
            var planet = planets[i];
            Vector3 desired = planet.worldPosition.sqrMagnitude > 0.01f ? new Vector3(planet.worldPosition.x, 0f, planet.worldPosition.z) : RingPosition(i, planets.Count);
            int tileIndex = Grid.GetNearestTileIndex(desired); var tile = Grid.GetTile(tileIndex); if (tile == null) continue;
            tile.terrainType = planet.celestialBodyType == GameManager.CelestialBodyType.Moon ? SpaceTerrainType.Moon : SpaceTerrainType.Planet;
            tile.blocksMovement = true; tile.planetId = planet.celestialBodyId >= 0 ? planet.celestialBodyId : planet.planetIndex;
            var marker = CreatePlanetMarker(planet, Grid.GetWorldPosition(tileIndex)); marker.AnchorSpaceTileIndex = tileIndex; var view = marker.GetComponent<SpacePlanetView>() ?? marker.gameObject.AddComponent<SpacePlanetView>(); view.entityId = tile.planetId; markersByPlanet[planet.planetIndex] = marker;
        }
        RefreshCurrentPlanetHighlight(); RefreshHexVisuals();
    }

    public void RefreshCurrentPlanetHighlight()
    {
        int current = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : -1;
        foreach (var kv in markersByPlanet) kv.Value.SetSelectionState(kv.Key == selectedPlanetIndex, kv.Key == current);
    }
    public void RefreshTravelVisuals() { RefreshShipVisuals(); RefreshHexVisuals(); RefreshRouteVisuals(); }
    public int GetPlanetAnchorTile(int planetIndex) => markersByPlanet.TryGetValue(planetIndex, out var m) ? m.AnchorSpaceTileIndex : -1;

    public void SelectPlanetMarker(SpaceMapPlanetMarker marker) { if (marker == null) return; selectedShip = null; pendingDestinationTile = -1; selectedPlanetIndex = marker.PlanetIndex; spaceMapUI?.SelectPlanet(marker.PlanetData); RefreshCurrentPlanetHighlight(); RefreshRouteVisuals(); }
    public void SelectShip(BaseUnit unit) { selectedShip = unit; selectedPlanetIndex = -1; pendingDestinationTile = -1; RefreshCurrentPlanetHighlight(); HighlightReachableTiles(unit); RefreshRouteVisuals(); spaceMapUI?.SelectShip(unit); }
    public void ClearSelection() { selectedShip = null; selectedPlanetIndex = -1; pendingDestinationTile = -1; RefreshCurrentPlanetHighlight(); RefreshHexVisuals(); RefreshRouteVisuals(); }

    private void HandleSelectionInput()
    {
        if (spaceMapCamera == null || Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        Ray ray = spaceMapCamera.ScreenPointToRay(Mouse.current.position.ReadValue()); if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity)) return;
        var shipView = hit.collider.GetComponentInParent<SpaceShipView>();
        var ship = shipView != null ? shipView.Unit : hit.collider.GetComponentInParent<BaseUnit>();
        if (ship != null && ship.currentSpaceTileIndex >= 0) { SelectShip(ship); return; }
        var marker = hit.collider.GetComponentInParent<SpaceMapPlanetMarker>(); if (marker != null) { SelectPlanetMarker(marker); return; }
        var hex = hit.collider.GetComponent<SpaceHexTileView>();
        if (hex != null && selectedShip != null)
        {
            if (pendingDestinationTile != hex.tileIndex)
            {
                pendingDestinationTile = hex.tileIndex;
                SetHexColor(hex.tileIndex, Color.yellow);
                UIManager.Instance?.ShowNotification("Click the destination again to confirm the space movement order.");
                return;
            }
            pendingDestinationTile = -1;
            bool queued = SpaceShipMovementController.Instance.QueueMove(selectedShip, hex.tileIndex);
            if (!queued) UIManager.Instance?.ShowNotification("No valid space route is available to that destination.");
            RefreshTravelVisuals();
        }
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

    private void HighlightReachableTiles(BaseUnit unit)
    {
        int range = unit != null ? Mathf.Max(0, unit.currentSpaceMovementPoints) : 0;
        foreach (var tile in Grid.tiles)
        {
            bool reachable = unit != null && unit.currentSpaceTileIndex >= 0 && Grid.GetDistance(unit.currentSpaceTileIndex, tile.tileIndex) <= range && !tile.blocksMovement;
            SetHexColor(tile.tileIndex, reachable ? reachableColor : (tile.blocksMovement ? blockedColor : hexColor));
        }
    }
    private void RefreshHexVisuals() { foreach (var t in Grid.tiles) SetHexColor(t.tileIndex, t.blocksMovement ? blockedColor : hexColor); if (selectedShip != null) HighlightReachableTiles(selectedShip); }
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
        foreach (var unit in FindObjectsByType<BaseUnit>(FindObjectsInactive.Exclude))
        {
            if (unit.currentSpaceTileIndex < 0) continue;
            var proxy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            proxy.name = $"SpaceShipView_{unit.gameObject.GetRuntimeId()}";
            proxy.transform.SetParent(unitRoot, false);
            proxy.transform.position = Grid.GetWorldPosition(unit.currentSpaceTileIndex) + Vector3.up * 1.2f;
            proxy.AddComponent<SpaceShipView>().Initialize(unit);
        }
    }

    private void RefreshRouteVisuals()
    {
        ClearChildren(routeRoot);
        if (selectedShip == null || selectedShip.queuedSpacePath == null || selectedShip.queuedSpacePath.Count < 2) return;
        var route = new GameObject("SelectedShipRoute");
        route.transform.SetParent(routeRoot, false);
        var line = route.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.widthMultiplier = 0.35f;
        int firstPathIndex = Mathf.Clamp(selectedShip.queuedSpacePathCursor, 0, selectedShip.queuedSpacePath.Count - 1);
        line.positionCount = selectedShip.queuedSpacePath.Count - firstPathIndex;
        line.startColor = reachableColor;
        line.endColor = Color.cyan;
        routeMaterial ??= new Material(Shader.Find("Sprites/Default"));
        line.sharedMaterial = routeMaterial;
        for (int i = firstPathIndex; i < selectedShip.queuedSpacePath.Count; i++)
            line.SetPosition(i - firstPathIndex, Grid.GetWorldPosition(selectedShip.queuedSpacePath[i]) + Vector3.up * 0.45f);
    }

    private SpaceMapPlanetMarker CreatePlanetMarker(GameManager.PlanetData planet, Vector3 position)
    {
        SpaceMapPlanetMarker marker = planetMarkerPrefab != null ? Instantiate(planetMarkerPrefab, planetRoot) : new GameObject("SpaceMapPlanetMarker").AddComponent<SpaceMapPlanetMarker>();
        marker.transform.SetParent(planetRoot, false); marker.transform.position = position; marker.Initialize(planet, this, GetPlanetRadius(planet), planetMaterialTemplate); return marker;
    }
    private Vector3 RingPosition(int i, int count) { if (i == 0) return Vector3.zero; float a = (360f / Mathf.Max(1, count - 1)) * (i - 1) * Mathf.Deg2Rad; float r = Mathf.Lerp(hexSize * 3f, gridRadius * hexSize * 0.75f, i / Mathf.Max(1f, count - 1f)); return new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r); }
    private float GetPlanetRadius(GameManager.PlanetData planet) { float t = planet.planetSize == GameManager.MapSize.Small ? .35f : planet.planetSize == GameManager.MapSize.Large ? .85f : .55f; if (planet.planetType == GameManager.PlanetType.Gas_Giant) t = 1f; if (planet.celestialBodyType == GameManager.CelestialBodyType.Moon) t *= .45f; return Mathf.Lerp(minPlanetRadius, maxPlanetRadius, Mathf.Clamp01(t)); }

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
    private void ClearChildren(Transform root) { if (root == null) return; for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject); }
    private void OnDestroy() { RestoreGameplayCameras(); if (fallbackHexMaterial != null) Destroy(fallbackHexMaterial); if (routeMaterial != null) Destroy(routeMaterial); }
}

public class SpaceHexTileView : MonoBehaviour { public int tileIndex; }
