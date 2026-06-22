// Assets/Scripts/UI/CityTileOverlayController.cs
using System.Collections.Generic;
using UnityEngine;

public class CityTileOverlayController : MonoBehaviour
{
    public static CityTileOverlayController Instance { get; private set; }

    [Header("Overlay Prefabs")]
    [SerializeField] private CityTileOverlayMarker tileMarkerPrefab;
    [SerializeField] private Transform overlayRoot;

    [Header("UI")]
    [SerializeField] private CityCitizenAssignmentPanel assignmentPanel;

    private City currentCity;
    private readonly List<CityTileOverlayMarker> activeMarkers = new List<CityTileOverlayMarker>();
    private TileSystem subscribedTileSystem;
    private int subscribedPlanetIndex = -1;

    public City CurrentCity => currentCity;

    private void Awake()
    {
        Instance = this;
        if (overlayRoot == null) overlayRoot = transform;
    }

    public void EnterCityAssignmentMode(City city)
    {
        ExitCityAssignmentMode();
        currentCity = city;
        if (currentCity == null) return;
        CenterCameraOnCity(currentCity);
        SubscribeToTileClicks(currentCity.planetIndex);
        BuildOverlayForCity(currentCity);
        if (assignmentPanel != null) assignmentPanel.ShowForCity(currentCity);
    }

    public void ExitCityAssignmentMode()
    {
        ClearMarkers();
        UnsubscribeFromTileClicks();
        if (assignmentPanel != null) assignmentPanel.Hide();
        currentCity = null;
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
        foreach (int tileIndex in city.GetWorkableTileIndexes())
        {
            Vector3 pos = ts.GetTileSurfacePosition(tileIndex, 0.35f);
            var marker = Instantiate(tileMarkerPrefab, pos, Quaternion.identity, overlayRoot);
            marker.Initialize(city, tileIndex, this);
            activeMarkers.Add(marker);
        }
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
        if (subscribedTileSystem != null) subscribedTileSystem.OnTileClicked += HandleTileClicked;
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
        if (!currentCity.IsTileWorkableByThisCity(tileIndex)) return false;
        SelectTile(tileIndex);
        return true;
    }

    public void SelectTile(int tileIndex)
    {
        if (currentCity == null) return;
        foreach (var marker in activeMarkers) if (marker != null) marker.SetSelected(marker.TileIndex == tileIndex);
        if (assignmentPanel != null) assignmentPanel.ShowForTile(currentCity, tileIndex);
    }
}
