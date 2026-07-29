using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;

/// <summary>
/// Overlay-only UI for the mandatory world-space solar-system map. Rendering,
/// selection, hexes, planets, routes, and ships are owned by SpaceMapWorldController.
/// </summary>
public class SpaceMapUI : MonoBehaviour
{
    [Header("Overlay References")]
    public Canvas spaceMapCanvas;
    public GameObject spaceMapPanel;
    public Button closeButton;
    public TextMeshProUGUI titleText;

    [Header("Selected Entity Info")]
    public GameObject planetInfoPanel;
    public TextMeshProUGUI planetNameText;
    public TextMeshProUGUI planetTypeText;
    public TextMeshProUGUI planetStatusText;
    public TextMeshProUGUI distanceText;
    public Button travelButton;
    public Button cancelButton;

    [Header("Civilization List")]
    public Transform civilizationContainer;
    public GameObject civilizationEntryPrefab;
    public TextMeshProUGUI noCivilizationsText;

    [Header("World-Space Space Map")]
    [SerializeField] private SpaceMapWorldController spaceMapWorldController;

    private GameManager.PlanetData selectedPlanet;
    private BaseUnit selectedShip;
    private bool spaceMapModeActive;
    private float ignoreCloseInputUntil;

    private void Awake()
    {
        ResolveAssignedReferences();
        ValidateAssignedReferences();
        Hide();
    }

    private void Start()
    {
        if (closeButton != null) { closeButton.onClick.RemoveAllListeners(); closeButton.onClick.AddListener(Hide); }
        if (travelButton != null)
        {
            travelButton.onClick.RemoveAllListeners();
            travelButton.onClick.AddListener(() => { if (selectedPlanet != null) SwitchToPlanet(selectedPlanet); });
            var label = travelButton.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = "View Planet";
        }
        if (cancelButton != null) { cancelButton.onClick.RemoveAllListeners(); cancelButton.onClick.AddListener(ClearSelection); }
    }

    private void Update()
    {
        if (!spaceMapModeActive || Time.unscaledTime < ignoreCloseInputUntil) return;
        bool keyboardClose = Keyboard.current != null && (Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.mKey.wasPressedThisFrame);
        bool gamepadClose = Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame;
        if (keyboardClose || gamepadClose) Hide();
    }

    public void Show()
    {
        gameObject.SetActive(true); if (spaceMapCanvas != null) spaceMapCanvas.gameObject.SetActive(true); if (spaceMapPanel != null) spaceMapPanel.SetActive(true);
        EnterSpaceMapMode(); SetSpaceMapPanelBackgroundVisible(false);
        if (spaceMapWorldController != null)
            spaceMapWorldController.ShowMap(this);
        else
            Debug.LogError("[SpaceMapUI] Cannot show space map because SpaceMapWorldController is not assigned.");
        RefreshCivilizations(null);
    }

    public void Hide()
    {
        if (spaceMapWorldController != null) spaceMapWorldController.HideMap(); ExitSpaceMapMode();
        if (spaceMapCanvas != null) spaceMapCanvas.gameObject.SetActive(false); else gameObject.SetActive(false);
    }

    public void SelectPlanet(GameManager.PlanetData planet)
    {
        selectedPlanet = planet; selectedShip = null; if (planetInfoPanel != null) planetInfoPanel.SetActive(planet != null);
        if (planetNameText != null) planetNameText.text = planet != null ? planet.planetName : "No selection";
        if (planetTypeText != null) planetTypeText.text = planet != null ? $"{planet.celestialBodyType} • {planet.planetType}" : string.Empty;
        if (planetStatusText != null) planetStatusText.text = planet == null ? string.Empty : BuildPlanetStatus(planet);
        if (distanceText != null)
        {
            int anchorTile = planet != null && spaceMapWorldController != null ? spaceMapWorldController.GetPlanetAnchorTile(planet.planetIndex) : -1;
            distanceText.text = planet == null ? string.Empty : anchorTile >= 0 ? $"System location: hex {anchorTile}" : "System location unavailable";
        }
        if (travelButton != null) travelButton.gameObject.SetActive(planet != null);
        RefreshCivilizations(planet);
    }

    public void SelectShip(BaseUnit ship)
    {
        selectedShip = ship; selectedPlanet = null; if (planetInfoPanel != null) planetInfoPanel.SetActive(ship != null);
        if (planetNameText != null) planetNameText.text = ship != null ? ship.name : "No selection";
        if (planetTypeText != null) planetTypeText.text = ship != null ? "Spacecraft" : string.Empty;
        if (planetStatusText != null) planetStatusText.text = ship != null ? $"Tile {ship.currentSpaceTileIndex} • MP {ship.currentSpaceMovementPoints}/{ship.spaceMovementPointsPerTurn}" : string.Empty;
        if (distanceText != null)
        {
            int remaining = ship != null && ship.queuedSpacePath != null
                ? Mathf.Max(0, ship.queuedSpacePath.Count - 1 - ship.queuedSpacePathCursor)
                : 0;
            distanceText.text = remaining > 0 ? $"Remaining route: {remaining} hexes" : "No queued route";
        }
        if (travelButton != null) travelButton.gameObject.SetActive(false);
        RefreshCivilizations(null);
    }

    public void TestCloseButton() => Hide();

    private void ClearSelection()
    {
        selectedPlanet = null;
        selectedShip = null;
        spaceMapWorldController?.ClearSelection();
        SelectPlanet(null);
    }

    private string BuildPlanetStatus(GameManager.PlanetData planet)
    {
        var parts = new List<string>();
        parts.Add(planet.isHomeWorld ? "Homeworld" : planet.isColonized ? "Colonized" : planet.isExplored ? "Explored" : "Unexplored");
        parts.Add($"Size: {planet.planetSize}");
        if (planet.childMoonIds != null && planet.childMoonIds.Count > 0) parts.Add($"Moons {planet.childMoonIds.Count}");
        return string.Join(" • ", parts);
    }

    private void SwitchToPlanet(GameManager.PlanetData planet)
    {
        if (planet == null) return; Hide(); if (GameManager.Instance != null) GameManager.Instance.StartCoroutine(GameManager.Instance.SwitchToMultiPlanet(planet.planetIndex));
    }

    private void RefreshCivilizations(GameManager.PlanetData planet)
    {
        if (civilizationContainer == null) return; for (int i = civilizationContainer.childCount - 1; i >= 0; i--) Destroy(civilizationContainer.GetChild(i).gameObject);
        var names = planet?.civilizationNames; bool any = names != null && names.Count > 0; if (noCivilizationsText != null) noCivilizationsText.gameObject.SetActive(!any);
        if (!any) return; foreach (string name in names) CreateCivilizationEntry(name);
    }

    private void CreateCivilizationEntry(string text)
    {
        if (civilizationEntryPrefab == null)
        {
            Debug.LogWarning("[SpaceMapUI] Civilization entry prefab is not assigned; cannot render civilization entry.");
            return;
        }

        GameObject go = Instantiate(civilizationEntryPrefab, civilizationContainer);
        var label = go.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null)
            label.text = text;
        else
            Debug.LogWarning("[SpaceMapUI] Civilization entry prefab needs a TextMeshProUGUI child to display civilization names.");
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
        if (planetInfoPanel == null) Debug.LogWarning("[SpaceMapUI] Planet/ship Info Panel is not assigned. Create it manually in Unity and assign it.");
        if (spaceMapWorldController == null) Debug.LogWarning("[SpaceMapUI] SpaceMapWorldController is not assigned. Add one to the scene and assign it.");
    }
    private void SetSpaceMapPanelBackgroundVisible(bool visible) { var img = spaceMapPanel != null ? spaceMapPanel.GetComponent<Image>() : null; if (img != null) img.enabled = visible; }
    private void EnterSpaceMapMode() { spaceMapModeActive = true; ignoreCloseInputUntil = Time.unscaledTime + .15f; }
    private void ExitSpaceMapMode() { spaceMapModeActive = false; }
}
