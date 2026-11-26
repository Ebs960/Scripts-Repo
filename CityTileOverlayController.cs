// Assets/Scripts/UI/CityTileOverlayController.cs
using System.Collections.Generic;
using UnityEngine;

public class CityTileOverlayController : MonoBehaviour
{
    private static CityTileOverlayController instance;

    /// <summary>The controller sits on the city panel, which is inactive until first opened, so it is located on demand.</summary>
    public static CityTileOverlayController Instance
    {
        get
        {
            if (instance == null) instance = FindAnyObjectByType<CityTileOverlayController>(FindObjectsInactive.Include);
            return instance;
        }
    }

    [Header("Overlay Prefabs")]
    [SerializeField] private CityTileOverlayMarker tileMarkerPrefab;
    [Tooltip("World-space parent for the markers. Created at the scene root when left empty.")]
    [SerializeField] private Transform overlayRoot;
    [Tooltip("Marker width as a fraction of the distance between neighbouring tiles.")]
    [SerializeField, Range(0.2f, 1f)] private float markerTileFraction = 0.55f;
    [SerializeField] private float markerHeightOffset = 0.35f;

    [Header("UI")]
    [SerializeField] private CityCitizenAssignmentPanel assignmentPanel;

    private City currentCity;
    private readonly List<CityTileOverlayMarker> activeMarkers = new List<CityTileOverlayMarker>();
    private TileSystem subscribedTileSystem;
    private int subscribedPlanetIndex = -1;
    private bool ownsOverlayRoot;
    private int selectedTileIndex = -1;

    public City CurrentCity => currentCity;

    private void Awake()
    {
        instance = this;
    }

    private void OnDestroy()
    {
        ExitCityAssignmentMode();
        if (ownsOverlayRoot && overlayRoot != null) Destroy(overlayRoot.gameObject);
        if (instance == this) instance = null;
    }

    // Markers are world objects; they must not be parented under this screen-space panel.
    private Transform ResolveOverlayRoot()
    {
        if (overlayRoot == null)
        {
            overlayRoot = new GameObject("City Tile Markers").transform;
            ownsOverlayRoot = true;
        }
        return overlayRoot;
    }

    public void EnterCityAssignmentMode(City city)
    {
        EnterCityAssignmentMode(city, -1);
    }

    public void EnterCityAssignmentMode(City city, int selectedTileIndex)
    {
        ExitCityAssignmentMode();
        currentCity = city;
        if (currentCity == null) return;
        CenterCameraOnCity(currentCity);
        SubscribeToTileClicks(currentCity.planetIndex);
        BuildOverlayForCity(currentCity);
        this.selectedTileIndex = selectedTileIndex;
        foreach (var marker in activeMarkers) if (marker != null) marker.SetSelected(marker.TileIndex == selectedTileIndex);
        if (assignmentPanel != null)
        {
            if (selectedTileIndex >= 0) assignmentPanel.ShowForTile(currentCity, selectedTileIndex);
            else assignmentPanel.ShowForCity(currentCity);
        }
    }

    public void ExitCityAssignmentMode()
    {
        ClearMarkers();
        UnsubscribeFromTileClicks();
        if (assignmentPanel != null) assignmentPanel.Hide();
        currentCity = null;
        selectedTileIndex = -1;
    }

    private void CenterCameraOnCity(City city)
    {
        if (CityCameraFocus.Instance != null) { CityCameraFocus.Instance.FocusCity(city); return; }
        Camera cam = Camera.main;
        if (cam != null && city != null) cam.transform.position = city.transform.position - cam.transform.forward * 25f;
    }

    private void BuildOverlayForCity(City city)
    {
        if (city == null || tileMarkerPrefab == null) return;
        var ts = TileSystem.GetForPlanet(city.planetIndex) ?? TileSystem.Instance;
        if (ts == null) return;
        var tiles = city.GetWorkableTileIndexes();
        var positions = new List<Vector3>(tiles.Count);
        foreach (int tileIndex in tiles) positions.Add(ts.GetTileSurfacePosition(tileIndex, markerHeightOffset));

        float size = GetTileSpacing(positions) * markerTileFraction;
        Transform root = ResolveOverlayRoot();
        for (int i = 0; i < tiles.Count; i++)
        {
            var marker = Instantiate(tileMarkerPrefab, positions[i], Quaternion.identity, root);
            if (size > 0f) marker.SetWorldSize(size);
            marker.Initialize(city, tiles[i], this);
            activeMarkers.Add(marker);
        }
    }

    // Neighbouring tiles are the closest pair in the set, so the smallest gap is the tile spacing.
    private static float GetTileSpacing(List<Vector3> positions)
    {
        float best = float.MaxValue;
        for (int i = 0; i < positions.Count; i++)
            for (int j = i + 1; j < positions.Count; j++)
            {
                Vector3 delta = positions[i] - positions[j];
                delta.y = 0f;
                float distance = delta.magnitude;
                if (distance > 0.001f && distance < best) best = distance;
            }
        return best < float.MaxValue ? best : 0f;
    }

    public void RefreshOverlay()
    {
        foreach (var marker in activeMarkers) if (marker != null) marker.Refresh();
        if (assignmentPanel != null && currentCity != null) assignmentPanel.Refresh();
    }

    private void ClearMarkers()
    {
        foreach (var marker in activeMarkers) if (marker != null) Destroy(marker.gameObject);
        activeMarkers.Clear();
    }

    private void SubscribeToTileClicks(int planetIndex)
    {
        UnsubscribeFromTileClicks();
        subscribedPlanetIndex = planetIndex;
        subscribedTileSystem = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (subscribedTileSystem != null) subscribedTileSystem.AddPriorityTileClickHandler(HandleTileClicked);
    }

    private void UnsubscribeFromTileClicks()
    {
        if (subscribedTileSystem != null) subscribedTileSystem.OnTileClicked -= HandleTileClicked;
        subscribedTileSystem = null;
        subscribedPlanetIndex = -1;
    }

    private bool HandleTileClicked(int tileIndex, Vector3 worldPos)
    {
        if (currentCity == null) return false;
        if (InputManager.Instance != null && InputManager.Instance.IsPointerOverUI()) return false;
        if (FindMarker(tileIndex) == null) return false;

        // The first click selects a tile; clicking the selected tile again adds or removes its citizen.
        bool alreadySelected = tileIndex == selectedTileIndex;
        SelectTile(tileIndex);
        if (assignmentPanel == null || alreadySelected) ToggleTileWorker(tileIndex);
        return true;
    }

    private CityTileOverlayMarker FindMarker(int tileIndex)
    {
        foreach (var marker in activeMarkers) if (marker != null && marker.TileIndex == tileIndex) return marker;
        return null;
    }

    public void SelectTile(int tileIndex)
    {
        if (currentCity == null) return;
        selectedTileIndex = tileIndex;
        foreach (var marker in activeMarkers) if (marker != null) marker.SetSelected(marker.TileIndex == tileIndex);
        if (assignmentPanel != null) assignmentPanel.ShowForTile(currentCity, tileIndex);
    }

    private void ToggleTileWorker(int tileIndex)
    {
        var assignment = currentCity.GetTileAssignment(tileIndex);
        if (assignment != null)
        {
            if (assignment.locked) UIManager.Instance?.ShowNotification("This tile's citizen is locked in place.");
            else currentCity.RemoveAssignmentFromTile(tileIndex);
        }
        else
        {
            var marker = FindMarker(tileIndex);
            if (marker != null && !marker.IsAvailable)
                UIManager.Instance?.ShowNotification("This tile cannot be worked by this city.");
            else if (!currentCity.AssignTileWorker(tileIndex, out string reason))
                UIManager.Instance?.ShowNotification(reason);
        }

        RefreshOverlay();
        foreach (var view in FindObjectsByType<CityUI>(FindObjectsInactive.Exclude))
            view.RefreshUI();
    }
}
