using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

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
        SetupUIReferences();
        if (spaceMapWorldController == null) spaceMapWorldController = FindAnyObjectByType<SpaceMapWorldController>(FindObjectsInactive.Include);
        if (spaceMapWorldController == null) spaceMapWorldController = new GameObject("SpaceMapWorldController").AddComponent<SpaceMapWorldController>();
        Hide();
    }

    private void Start()
    {
        if (closeButton != null) { closeButton.onClick.RemoveAllListeners(); closeButton.onClick.AddListener(Hide); }
        if (travelButton != null) { travelButton.onClick.RemoveAllListeners(); travelButton.onClick.AddListener(() => { if (selectedPlanet != null) SwitchToPlanet(selectedPlanet); }); }
        if (cancelButton != null) { cancelButton.onClick.RemoveAllListeners(); cancelButton.onClick.AddListener(() => SelectPlanet(null)); }
    }

    private void Update()
    {
        if (!spaceMapModeActive || Time.unscaledTime < ignoreCloseInputUntil) return;
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.M)) Hide();
    }

    public void Show()
    {
        gameObject.SetActive(true); if (spaceMapCanvas != null) spaceMapCanvas.gameObject.SetActive(true); if (spaceMapPanel != null) spaceMapPanel.SetActive(true);
        EnterSpaceMapMode(); SetSpaceMapPanelBackgroundVisible(false); spaceMapWorldController.ShowMap(this); RefreshCivilizations(null);
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
        if (distanceText != null) distanceText.text = planet == null ? string.Empty : $"Anchor hex: {spaceMapWorldController.GetPlanetAnchorTile(planet.planetIndex)}";
        if (travelButton != null) travelButton.gameObject.SetActive(planet != null);
        RefreshCivilizations(planet);
    }

    public void SelectShip(BaseUnit ship)
    {
        selectedShip = ship; selectedPlanet = null; if (planetInfoPanel != null) planetInfoPanel.SetActive(ship != null);
        if (planetNameText != null) planetNameText.text = ship != null ? ship.name : "No selection";
        if (planetTypeText != null) planetTypeText.text = ship != null ? "Spacecraft" : string.Empty;
        if (planetStatusText != null) planetStatusText.text = ship != null ? $"Tile {ship.currentSpaceTileIndex} • MP {ship.currentSpaceMovementPoints}/{ship.spaceMovementPointsPerTurn}" : string.Empty;
        if (distanceText != null) distanceText.text = ship != null && ship.queuedSpacePath != null ? $"Queued path: {ship.queuedSpacePath.Count} hexes" : string.Empty;
        if (travelButton != null) travelButton.gameObject.SetActive(false);
        RefreshCivilizations(null);
    }

    public void TestCloseButton() => Hide();

    private string BuildPlanetStatus(GameManager.PlanetData planet)
    {
        var parts = new List<string>();
        parts.Add(planet.isHomeWorld ? "Homeworld" : planet.isColonized ? "Colonized" : planet.isExplored ? "Explored" : "Unexplored");
        parts.Add($"Body ID {planet.celestialBodyId}"); if (planet.parentBodyId >= 0) parts.Add($"Parent {planet.parentBodyId}");
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
        GameObject go = civilizationEntryPrefab != null ? Instantiate(civilizationEntryPrefab, civilizationContainer) : CreateUIElement("CivilizationEntry", civilizationContainer);
        var label = go.GetComponentInChildren<TextMeshProUGUI>() ?? go.AddComponent<TextMeshProUGUI>(); label.text = text; label.fontSize = 14; label.color = Color.white;
    }

    private void SetupUIReferences()
    {
        if (spaceMapCanvas == null) { spaceMapCanvas = GetComponent<Canvas>() ?? gameObject.AddComponent<Canvas>(); spaceMapCanvas.renderMode = RenderMode.ScreenSpaceOverlay; spaceMapCanvas.sortingOrder = 100; }
        if (spaceMapCanvas.GetComponent<GraphicRaycaster>() == null) spaceMapCanvas.gameObject.AddComponent<GraphicRaycaster>();
        if (spaceMapPanel == null) CreateSpaceMapPanel();
    }

    private void CreateSpaceMapPanel()
    {
        spaceMapPanel = CreateUIElement("SpaceMapOverlay", spaceMapCanvas.transform); var img = spaceMapPanel.AddComponent<Image>(); img.color = new Color(0f,0f,0f,0.08f);
        var rect = spaceMapPanel.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        var closeGO = CreateUIElement("CloseButton", spaceMapPanel.transform); closeButton = closeGO.AddComponent<Button>(); closeGO.AddComponent<Image>().color = Color.red;
        var closeText = CreateUIElement("Text", closeGO.transform).AddComponent<TextMeshProUGUI>(); closeText.text = "X"; closeText.alignment = TextAlignmentOptions.Center; closeText.color = Color.white;
        var cr = closeGO.GetComponent<RectTransform>(); cr.anchorMin = new Vector2(.95f,.92f); cr.anchorMax = new Vector2(.995f,.99f); cr.offsetMin = cr.offsetMax = Vector2.zero;
        var panel = CreateUIElement("InfoPanel", spaceMapPanel.transform); planetInfoPanel = panel; panel.AddComponent<Image>().color = new Color(0f,0f,.08f,.75f); var pr = panel.GetComponent<RectTransform>(); pr.anchorMin = new Vector2(.72f,.08f); pr.anchorMax = new Vector2(.98f,.55f); pr.offsetMin = pr.offsetMax = Vector2.zero;
        planetNameText = AddText("Name", panel.transform, .82f, 1f, 22); planetTypeText = AddText("Type", panel.transform, .68f, .82f, 16); planetStatusText = AddText("Status", panel.transform, .38f, .68f, 14); distanceText = AddText("Movement", panel.transform, .22f, .38f, 14);
        var travelGO = CreateUIElement("ActionButton", panel.transform); travelButton = travelGO.AddComponent<Button>(); travelGO.AddComponent<Image>().color = new Color(.1f,.4f,.9f,.9f); var tr = travelGO.GetComponent<RectTransform>(); tr.anchorMin = new Vector2(.08f,.04f); tr.anchorMax = new Vector2(.92f,.18f); tr.offsetMin = tr.offsetMax = Vector2.zero; var tt = CreateUIElement("Text", travelGO.transform).AddComponent<TextMeshProUGUI>(); tt.text = "Open Planet"; tt.alignment = TextAlignmentOptions.Center; tt.color = Color.white;
    }

    private TextMeshProUGUI AddText(string name, Transform parent, float minY, float maxY, int size)
    {
        var t = CreateUIElement(name, parent).AddComponent<TextMeshProUGUI>(); t.fontSize = size; t.color = Color.white; t.alignment = TextAlignmentOptions.Left; var r = t.GetComponent<RectTransform>(); r.anchorMin = new Vector2(.06f,minY); r.anchorMax = new Vector2(.94f,maxY); r.offsetMin = r.offsetMax = Vector2.zero; return t;
    }
    private GameObject CreateUIElement(string name, Transform parent) { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); return go; }
    private void SetSpaceMapPanelBackgroundVisible(bool visible) { var img = spaceMapPanel != null ? spaceMapPanel.GetComponent<Image>() : null; if (img != null) img.enabled = visible; }
    private void EnterSpaceMapMode() { spaceMapModeActive = true; ignoreCloseInputUntil = Time.unscaledTime + .15f; }
    private void ExitSpaceMapMode() { spaceMapModeActive = false; }
}
