using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Manages the UI for interplanetary trade routes.
/// </summary>
public class InterplanetaryTradePanel : MonoBehaviour
{
    [Header("UI References")]
    public GameObject tradePanel;
    public TMP_Dropdown originPlanetDropdown;
    public TMP_Dropdown destinationPlanetDropdown;
    public TMP_Dropdown resourceDropdown;
    public Button establishRouteButton;
    public TextMeshProUGUI routeInfoText;
    public Transform activeRoutesContainer;
    public GameObject routeItemPrefab;

    private SolarSystemManager solarSystemManager;
    private InterplanetaryTradeManager tradeManager;
    private Civilization playerCiv;

    void Start()
    {
        solarSystemManager = FindObjectOfType<SolarSystemManager>();
        tradeManager = FindObjectOfType<InterplanetaryTradeManager>();
        
        establishRouteButton.onClick.AddListener(OnEstablishRouteClicked);
        originPlanetDropdown.onValueChanged.AddListener(delegate { UpdateUI(); });
        destinationPlanetDropdown.onValueChanged.AddListener(delegate { UpdateUI(); });
        resourceDropdown.onValueChanged.AddListener(delegate { UpdateUI(); });

        HidePanel();
    }

    public void ShowPanel(Civilization civ)
    {
        this.playerCiv = civ;
        tradePanel.SetActive(true);
        PopulateDropdowns();
        UpdateUI();
    }

    public void HidePanel()
    {
        tradePanel.SetActive(false);
    }

    private void PopulateDropdowns()
    {
        originPlanetDropdown.ClearOptions();
        destinationPlanetDropdown.ClearOptions();
        resourceDropdown.ClearOptions();

        List<string> planetNames = new List<string>();
        foreach (var planet in solarSystemManager.planets)
        {
            planetNames.Add(planet.planetName);
        }
        originPlanetDropdown.AddOptions(planetNames);
        destinationPlanetDropdown.AddOptions(planetNames);

        // Populate with tradable resources from your ResourceManager or a similar system
        // This is a placeholder
        resourceDropdown.AddOptions(new List<string> { "Food", "Production", "Science" });
    }

    private void UpdateUI()
    {
        // Update route info text
        // ...

        // Update active routes list
        foreach (Transform child in activeRoutesContainer)
        {
            Destroy(child.gameObject);
        }

        if (tradeManager == null) return;

        foreach (var route in tradeManager.GetTradeRoutes(playerCiv))
        {
            GameObject item = Instantiate(routeItemPrefab, activeRoutesContainer);
            // Populate item with route details
        }
    }

    private void OnEstablishRouteClicked()
    {
        int originIndex = originPlanetDropdown.value;
        int destIndex = destinationPlanetDropdown.value;
        // Get resource from dropdown

        // tradeManager.EstablishTradeRoute(playerCiv, originIndex, destIndex, selectedResource);
        UpdateUI();
    }
}
