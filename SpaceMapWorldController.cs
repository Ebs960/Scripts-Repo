using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Builds and runs the true world-space space map. The existing SpaceMapUI remains
/// responsible for panels and travel buttons; this controller owns the 3D flat-map
/// view, planet markers, camera, and world-space travel visuals.
/// </summary>
public class SpaceMapWorldController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera spaceMapCamera;
    [SerializeField] private SpaceMapCameraController cameraController;
    [SerializeField] private Transform mapRoot;
    [SerializeField] private Transform planetRoot;
    [SerializeField] private Transform routeRoot;
    [SerializeField] private SpaceMapUI spaceMapUI;

    [Header("Planet Visuals")]
    [SerializeField] private SpaceMapPlanetMarker planetMarkerPrefab;
    [SerializeField] private Material planetMaterialTemplate;
    [SerializeField] private float mapScale = 0.05f;
    [SerializeField] private float fallbackOrbitSpacing = 35f;
    [SerializeField] private float minPlanetRadius = 1.5f;
    [SerializeField] private float maxPlanetRadius = 5f;

    [Header("Map Plane")]
    [SerializeField] private bool createBackgroundPlane = true;
    [SerializeField] private Vector2 backgroundSize = new Vector2(900f, 900f);
    [SerializeField] private Color backgroundColor = new Color(0.005f, 0.007f, 0.025f, 1f);

    [Header("Routes")]
    [SerializeField] private Material routeMaterial;
    [SerializeField] private Material activeRouteMaterial;
    [SerializeField] private Color connectionLineColor = new Color(1f, 1f, 1f, 0.25f);
    [SerializeField] private Color activeRouteColor = Color.cyan;
    [SerializeField] private float connectionLineWidth = 0.18f;
    [SerializeField] private float shipMarkerRadius = 0.9f;

    private readonly Dictionary<int, SpaceMapPlanetMarker> markersByPlanet = new Dictionary<int, SpaceMapPlanetMarker>();
    private readonly List<GameObject> routeObjects = new List<GameObject>();
    private readonly List<GameObject> shipObjects = new List<GameObject>();
    private SpaceMapPlanetMarker selectedMarker;
    private bool isVisible;
    private float nextTravelRefreshTime;
    private Material defaultLineMaterial;

    private void Awake()
    {
        EnsureSceneObjects();
        SetMapActive(false);
    }

    private void Update()
    {
        if (!isVisible) return;
        HandleSelectionInput();
        if (Time.unscaledTime >= nextTravelRefreshTime)
        {
            RefreshTravelVisuals();
            nextTravelRefreshTime = Time.unscaledTime + 0.25f;
        }
    }

    public void ShowMap(SpaceMapUI ui)
    {
        if (ui != null) spaceMapUI = ui;
        EnsureSceneObjects();
        SetMapActive(true);
        RebuildPlanets();
        RefreshTravelVisuals();
        nextTravelRefreshTime = Time.unscaledTime + 0.25f;
        CenterOnCurrentPlanet();
    }

    public void HideMap()
    {
        SetMapActive(false);
    }

    public void RebuildPlanets()
    {
        ClearPlanets();
        var planetData = GameManager.Instance != null ? GameManager.Instance.GetPlanetData() : null;
        if (planetData == null || planetData.Count == 0) return;

        List<GameManager.PlanetData> planets = planetData.Values.OrderBy(p => p.planetIndex).ToList();
        float maxDistance = Mathf.Max(1f, planets.Where(p => !p.isHomeWorld).Select(p => p.distanceFromStar).DefaultIfEmpty(1f).Max());

        for (int i = 0; i < planets.Count; i++)
        {
            GameManager.PlanetData planet = planets[i];
            Vector3 position = GetMapPosition(planet, i, planets.Count, maxDistance);
            SpaceMapPlanetMarker marker = CreatePlanetMarker(planet, position);
            markersByPlanet[planet.planetIndex] = marker;
        }

        RefreshCurrentPlanetHighlight();
    }

    public void RefreshCurrentPlanetHighlight()
    {
        int currentPlanet = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : -1;
        foreach (var kv in markersByPlanet)
        {
            bool selected = selectedMarker != null && selectedMarker.PlanetIndex == kv.Key;
            bool current = kv.Key == currentPlanet;
            kv.Value.SetSelectionState(selected, current);
        }
    }

    public void RefreshTravelVisuals()
    {
        if (!isVisible) return;
        ClearRouteVisuals();
        CreateConnectionLines();
        CreateActiveTravelVisuals();
    }

    public void SelectPlanetMarker(SpaceMapPlanetMarker marker)
    {
        if (marker == null || marker.PlanetData == null) return;
        selectedMarker = marker;
        RefreshCurrentPlanetHighlight();
        spaceMapUI?.SelectPlanet(marker.PlanetData);
    }

    private void HandleSelectionInput()
    {
        if (spaceMapCamera == null || !Input.GetMouseButtonDown(0)) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        Ray ray = spaceMapCamera.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity)) return;
        SpaceMapPlanetMarker marker = hit.collider.GetComponentInParent<SpaceMapPlanetMarker>();
        if (marker != null) SelectPlanetMarker(marker);
    }

    private SpaceMapPlanetMarker CreatePlanetMarker(GameManager.PlanetData planet, Vector3 position)
    {
        SpaceMapPlanetMarker marker;
        if (planetMarkerPrefab != null)
        {
            marker = Instantiate(planetMarkerPrefab, planetRoot);
        }
        else
        {
            GameObject markerGO = new GameObject("SpaceMapPlanetMarker");
            markerGO.transform.SetParent(planetRoot, false);
            marker = markerGO.AddComponent<SpaceMapPlanetMarker>();
        }

        marker.transform.position = position;
        marker.Initialize(planet, this, GetPlanetRadius(planet), planetMaterialTemplate);
        return marker;
    }

    private Vector3 GetMapPosition(GameManager.PlanetData planet, int index, int planetCount, float maxDistanceFromStar)
    {
        if (planet.worldPosition.sqrMagnitude > 0.001f)
            return new Vector3(planet.worldPosition.x, 0f, planet.worldPosition.z) * mapScale;

        if (planet.isHomeWorld)
            return Vector3.zero;

        float normalizedDistance = maxDistanceFromStar > 0.001f ? Mathf.Log(1f + planet.distanceFromStar) / Mathf.Log(1f + maxDistanceFromStar) : 0.5f;
        float radius = Mathf.Lerp(fallbackOrbitSpacing, fallbackOrbitSpacing * Mathf.Max(2f, planetCount * 0.65f), normalizedDistance);
        float angle = (360f / Mathf.Max(planetCount - 1, 1)) * Mathf.Max(0, index - 1) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
    }

    private float GetPlanetRadius(GameManager.PlanetData planet)
    {
        float t = planet.planetSize switch
        {
            MapSize.Small => 0.35f,
            MapSize.Standard => 0.55f,
            MapSize.Large => 0.85f,
            _ => 0.55f
        };

        if (planet.planetType == GameManager.PlanetType.Gas_Giant) t = 1f;
        if (planet.celestialBodyType == GameManager.CelestialBodyType.Moon) t *= 0.45f;
        return Mathf.Lerp(minPlanetRadius, maxPlanetRadius, Mathf.Clamp01(t));
    }

    private void CreateConnectionLines()
    {
        SpaceMapPlanetMarker home = markersByPlanet.Values.FirstOrDefault(m => m.PlanetData != null && m.PlanetData.isHomeWorld);
        if (home == null) return;

        foreach (SpaceMapPlanetMarker marker in markersByPlanet.Values)
        {
            if (marker == home) continue;
            routeObjects.Add(CreateLineObject($"Connection_{home.PlanetIndex}_{marker.PlanetIndex}", home.transform.position, marker.transform.position, connectionLineColor, connectionLineWidth, routeMaterial));
        }
    }

    private void CreateActiveTravelVisuals()
    {
        if (SpaceRouteManager.Instance == null) return;
        foreach (var travel in SpaceRouteManager.Instance.GetActiveTravels())
        {
            if (!markersByPlanet.TryGetValue(travel.originPlanetIndex, out var origin)) continue;
            if (!markersByPlanet.TryGetValue(travel.destinationPlanetIndex, out var destination)) continue;

            Vector3 start = origin.transform.position;
            Vector3 end = destination.transform.position;
            routeObjects.Add(CreateLineObject($"ActiveRoute_{travel.taskId}", start, end, activeRouteColor, connectionLineWidth * 2f, activeRouteMaterial));
            shipObjects.Add(CreateShipMarker(travel, Vector3.Lerp(start, end, Mathf.Clamp01(travel.Progress))));
        }
    }

    private GameObject CreateLineObject(string objectName, Vector3 start, Vector3 end, Color color, float width, Material material)
    {
        GameObject lineGO = new GameObject(objectName);
        lineGO.transform.SetParent(routeRoot, false);
        LineRenderer line = lineGO.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.SetPosition(0, start + Vector3.up * 0.15f);
        line.SetPosition(1, end + Vector3.up * 0.15f);
        line.startWidth = width;
        line.endWidth = width;
        line.material = material != null ? material : GetDefaultLineMaterial();
        line.startColor = color;
        line.endColor = color;
        return lineGO;
    }

    private GameObject CreateShipMarker(SpaceRouteManager.SpaceTravelTask travel, Vector3 position)
    {
        GameObject shipGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shipGO.name = $"SpaceMapShip_{travel.taskId}_{travel.unitName}";
        shipGO.transform.SetParent(routeRoot, false);
        shipGO.transform.position = position + Vector3.up * 0.75f;
        shipGO.transform.localScale = Vector3.one * shipMarkerRadius;
        MeshRenderer renderer = shipGO.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            Material mat = new Material(Shader.Find("Standard"));
            mat.color = activeRouteColor;
            renderer.material = mat;
        }
        return shipGO;
    }

    private Material GetDefaultLineMaterial()
    {
        if (defaultLineMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
            defaultLineMaterial = new Material(shader);
            defaultLineMaterial.color = Color.white;
        }
        return defaultLineMaterial;
    }

    private void CenterOnCurrentPlanet()
    {
        int currentPlanet = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : -1;
        if (currentPlanet >= 0 && markersByPlanet.TryGetValue(currentPlanet, out var marker) && cameraController != null)
            cameraController.CenterOn(marker.transform.position);
    }

    private void EnsureSceneObjects()
    {
        if (mapRoot == null)
        {
            Transform existingRoot = transform.Find("SpaceMapWorldRoot");
            if (existingRoot != null)
            {
                mapRoot = existingRoot;
            }
            else
            {
                GameObject rootGO = new GameObject("SpaceMapWorldRoot");
                rootGO.transform.SetParent(transform, false);
                mapRoot = rootGO.transform;
            }
        }

        if (planetRoot == null)
        {
            GameObject root = new GameObject("SpaceMapPlanets");
            root.transform.SetParent(mapRoot, false);
            planetRoot = root.transform;
        }

        if (routeRoot == null)
        {
            GameObject root = new GameObject("SpaceMapRoutes");
            root.transform.SetParent(mapRoot, false);
            routeRoot = root.transform;
        }

        if (spaceMapCamera == null)
            spaceMapCamera = GetComponentInChildren<Camera>(true);

        if (spaceMapCamera == null)
        {
            GameObject cameraGO = new GameObject("SpaceMapCamera");
            cameraGO.transform.SetParent(mapRoot, false);
            cameraGO.transform.position = new Vector3(0f, 120f, -0.01f);
            cameraGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            spaceMapCamera = cameraGO.AddComponent<Camera>();
            spaceMapCamera.orthographic = true;
            spaceMapCamera.orthographicSize = 120f;
            spaceMapCamera.clearFlags = CameraClearFlags.SolidColor;
            spaceMapCamera.backgroundColor = backgroundColor;
            spaceMapCamera.depth = 100f;
        }

        if (cameraController == null)
        {
            cameraController = spaceMapCamera.GetComponent<SpaceMapCameraController>();
            if (cameraController == null) cameraController = spaceMapCamera.gameObject.AddComponent<SpaceMapCameraController>();
        }

        if (createBackgroundPlane && mapRoot.Find("SpaceMapBackgroundPlane") == null)
        {
            GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "SpaceMapBackgroundPlane";
            plane.transform.SetParent(mapRoot, false);
            plane.transform.localScale = new Vector3(backgroundSize.x / 10f, 1f, backgroundSize.y / 10f);
            MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                Material mat = new Material(Shader.Find("Standard"));
                mat.color = backgroundColor;
                renderer.material = mat;
            }
        }
    }

    private void SetMapActive(bool active)
    {
        isVisible = active;
        if (mapRoot != null) mapRoot.gameObject.SetActive(active);
        if (spaceMapCamera != null) spaceMapCamera.enabled = active;
    }

    private void ClearPlanets()
    {
        foreach (var marker in markersByPlanet.Values)
        {
            if (marker != null) Destroy(marker.gameObject);
        }
        markersByPlanet.Clear();
        selectedMarker = null;
    }

    private void ClearRouteVisuals()
    {
        foreach (GameObject route in routeObjects) if (route != null) Destroy(route);
        foreach (GameObject ship in shipObjects) if (ship != null) Destroy(ship);
        routeObjects.Clear();
        shipObjects.Clear();
    }
}
