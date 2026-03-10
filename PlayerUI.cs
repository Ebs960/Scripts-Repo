// Assets/Scripts/UI/PlayerUI.cs
using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerUI : MonoBehaviour
{
    [Header("Panels")]
    public GameObject playerPanel; // Made public for GameManager access
    [SerializeField] private GameObject turnChangePanel;

    [Header("Player Panel - Top Info")]
    [SerializeField] private TextMeshProUGUI civNameText;
    [SerializeField] private TextMeshProUGUI roundText;
    
    [SerializeField] private Button endTurnButton;
    [Header("Layer Selection")]
    [Tooltip("Single dropdown to switch between planet layers (Surface, Underwater, Atmosphere). Replaces the old individual toggle buttons.")]
    [SerializeField] private TMP_Dropdown layerDropdown;

    [SerializeField] private Button techButton;
    [SerializeField] private Button cultureButton;
    [SerializeField] private Button policyButton;
    [SerializeField] private Button diplomacyButton;
    [SerializeField] private Button equipmentButton;

    [Header("Player Panel - Yields")]
    [SerializeField] private TextMeshProUGUI foodYieldText;
    [SerializeField] private TextMeshProUGUI goldYieldText;
    [SerializeField] private TextMeshProUGUI scienceYieldText;
    [SerializeField] private TextMeshProUGUI cultureYieldText;
    [SerializeField] private TextMeshProUGUI policyPointYieldText;
    [SerializeField] private TextMeshProUGUI faithYieldText;

    [Header("Player Panel - Resources Inventory")]
    [SerializeField] private Transform resourceListContainer;
    [SerializeField] private GameObject resourceEntryPrefab; // icon + amount
    
    // Research/Culture textual displays removed — buttons remain

    [Header("Turn Change Panel")]
    [SerializeField] private TextMeshProUGUI upcomingCivText;
    [SerializeField] private Image upcomingCivIcon;

    private Civilization currentCiv;
    private Coroutine waitForTurnManagerCoroutine; // used to delay subscription until TurnManager exists

    void Start()
    {
        // playerPanel's active state should be true in its prefab if it's meant to be visible initially.
        // HandleTurnChanged will manage its visibility based on whose turn it is.
        if (turnChangePanel != null) turnChangePanel.SetActive(false); // Keep turn change panel hidden initially

        SetupButtonListeners();

        // Removed age/tech/culture text fields — nothing to hide at runtime
    }

    // Handlers for Civilization yield change events
    private void OnFoodChangedHandler(int newAmount, int delta)
    {
        if (currentCiv != null)
            UpdatePlayerPanel(currentCiv, TurnManager.Instance != null ? TurnManager.Instance.round : 0);
    }

    private void OnGoldChangedHandler(int newAmount, int delta)
    {
        if (currentCiv != null)
            UpdatePlayerPanel(currentCiv, TurnManager.Instance != null ? TurnManager.Instance.round : 0);
    }

    private void OnFaithChangedHandler(int newAmount, int delta)
    {
        if (currentCiv != null)
            UpdatePlayerPanel(currentCiv, TurnManager.Instance != null ? TurnManager.Instance.round : 0);
    }

    private void OnPolicyPointsChangedHandler(int newAmount, int delta)
    {
        if (currentCiv != null)
            UpdatePlayerPanel(currentCiv, TurnManager.Instance != null ? TurnManager.Instance.round : 0);
    }

    private void SetupButtonListeners()
    {
        if (endTurnButton != null)
        {
            endTurnButton.onClick.RemoveAllListeners(); // Good practice to remove before adding
            endTurnButton.onClick.AddListener(() => 
            {
                if (TurnManager.Instance != null)
                    TurnManager.Instance.EndPlayerTurn();
                else
                    Debug.LogError("PlayerUI: TurnManager.Instance is null when trying to end turn!");
            });
        }

        if (techButton != null)
        {
            techButton.onClick.RemoveAllListeners();
            techButton.onClick.AddListener(() => 
            {
// Use lazy initialization - get current civ from TurnManager if our stored reference is null
                Civilization civToUse = currentCiv;
                if (civToUse == null && TurnManager.Instance != null)
                {
                    civToUse = TurnManager.Instance.GetCurrentCivilization();
}
                
                if (UIManager.Instance != null && civToUse != null)
                {
                    UIManager.Instance.ShowTechPanel(civToUse);
                }
                else
                {
                    Debug.LogError($"PlayerUI: Cannot show tech panel - UIManager: {(UIManager.Instance != null ? "OK" : "NULL")}, Civilization: {(civToUse != null ? "OK" : "NULL")}");
                }
            });
        }

        if (cultureButton != null)
        {
            cultureButton.onClick.RemoveAllListeners();
            cultureButton.onClick.AddListener(() => 
            {
// Use lazy initialization - get current civ from TurnManager if our stored reference is null
                Civilization civToUse = currentCiv;
                if (civToUse == null && TurnManager.Instance != null)
                {
                    civToUse = TurnManager.Instance.GetCurrentCivilization();
}
                
                if (UIManager.Instance != null && civToUse != null)
                {
                    UIManager.Instance.ShowCulturePanel(civToUse);
                }
                else
                {
                    Debug.LogError($"PlayerUI: Cannot show culture panel - UIManager: {(UIManager.Instance != null ? "OK" : "NULL")}, Civilization: {(civToUse != null ? "OK" : "NULL")}");
                }
            });
        }

        // Assuming you have a policyButton and want to toggle a policy panel (ReligionPanel used as placeholder)
        if (policyButton != null)
        {
            policyButton.onClick.RemoveAllListeners();
            policyButton.onClick.AddListener(() =>
            {
                // Prefer a direct GovernmentPanel component if present in scene
                var govPanel = FindFirstObjectByType<GovernmentPanel>();
                Civilization civToUse = currentCiv;
                if (civToUse == null && TurnManager.Instance != null) civToUse = TurnManager.Instance.GetCurrentCivilization();
                if (govPanel != null && civToUse != null)
                {
                    govPanel.ShowForCivilization(civToUse);
                }
                else if (UIManager.Instance != null)
                {
                    // Fallback: try to open a named panel if UIManager manages it
                    UIManager.Instance.ShowPanel("GovernmentPanel");
                }
                else
                {
                    Debug.LogError("PlayerUI: Cannot open GovernmentPanel - no govPanel or UIManager available.");
                }
            });
        }

        // Equipment panel button
        if (equipmentButton != null)
        {
            equipmentButton.onClick.RemoveAllListeners();
            equipmentButton.onClick.AddListener(() =>
            {
Civilization civToUse = currentCiv;
                if (civToUse == null && TurnManager.Instance != null)
                {
                    civToUse = TurnManager.Instance.GetCurrentCivilization();
}

                if (UIManager.Instance != null && civToUse != null)
                {
                    UIManager.Instance.ShowEquipmentPanel(civToUse);
                }
                else
                {
                    Debug.LogError($"PlayerUI: Cannot show equipment panel - UIManager: {(UIManager.Instance != null ? "OK" : "NULL")}, Civilization: {(civToUse != null ? "OK" : "NULL")}");
                }
            });
        }
        

        if (diplomacyButton != null) 
        {
            diplomacyButton.onClick.RemoveAllListeners();
            diplomacyButton.onClick.AddListener(() => 
            {
// Use lazy initialization - get current civ from TurnManager if our stored reference is null
                Civilization civToUse = currentCiv;
                if (civToUse == null && TurnManager.Instance != null)
                {
                    civToUse = TurnManager.Instance.GetCurrentCivilization();
}
                
                if (UIManager.Instance != null && civToUse != null)
                {
                    // Use the new dedicated method
                    UIManager.Instance.ShowDiplomacyPanel(civToUse);
                }
                else
                {
                    Debug.LogError($"PlayerUI: Cannot show diplomacy panel - UIManager: {(UIManager.Instance != null ? "OK" : "NULL")}, Civilization: {(civToUse != null ? "OK" : "NULL")}");
                }
            });
        }

        // Layer dropdown (replaces the 3 individual toggle buttons)
        if (layerDropdown != null)
        {
            layerDropdown.onValueChanged.RemoveAllListeners();
            layerDropdown.onValueChanged.AddListener(OnLayerDropdownChanged);
            RefreshLayerDropdown();
        }
}

    /// <summary>
    /// Initializes the PlayerUI display with the player's data. 
    /// Called by GameManager after PlayerUI is instantiated.
    /// </summary>
    public void InitializePlayerDisplay(Civilization civ, int round)
    {
// Only activate if loading is not active
        bool shouldActivate = !IsLoadingActive();
        
        if (playerPanel != null) 
        {
            playerPanel.SetActive(shouldActivate);
}
        else
        {
            Debug.LogWarning("PlayerUI: playerPanel is null during InitializePlayerDisplay.");
        }
        if (turnChangePanel != null) turnChangePanel.SetActive(false);
        currentCiv = civ;
        if (currentCiv != null)
        {
            currentCiv.OnTechStarted += OnTechOrCultureStarted;
            currentCiv.OnCultureStarted += OnTechOrCultureStarted;
            currentCiv.OnTechResearched += OnTechOrCultureStarted;
            currentCiv.OnCultureCompleted += OnTechOrCultureStarted;
            
        }

        // Re-populate layer dropdown now that planet generation + LayerManager init are complete
        RefreshLayerDropdown();

        UpdatePlayerPanel(civ, round);
    }
    
    /// <summary>
    /// Check if any loading panel is currently active or minimap generation is in progress
    /// </summary>
    private bool IsLoadingActive()
    {
        if (LoadingPanelController.Instance != null)
        {
            if (LoadingPanelController.Instance.gameObject.activeSelf)
                return true;
        }
        
        // Also check if minimap generation is still in progress
        var minimapUI = FindFirstObjectByType<MinimapUI>();
        if (minimapUI != null && !minimapUI.MinimapsPreGenerated)
        {
return true;
        }
        
        return false;
    }

    void OnEnable()
    {
        // Delay setup until the game has actually started so that TurnManager
        // and civilizations are available
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnGameStarted += SetupAfterGameStart;

            // If the game is already in progress (e.g. UI re-enabled mid game)
            // immediately perform setup
            if (GameManager.Instance.gameInProgress)
            {
                SetupAfterGameStart();
            }
        }
        else
        {
            // Fallback: just wait for TurnManager directly
            waitForTurnManagerCoroutine = StartCoroutine(WaitForTurnManager());
        }
    }

    void OnDisable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnGameStarted -= SetupAfterGameStart;

        if (TurnManager.Instance != null)
            TurnManager.Instance.OnTurnChanged -= HandleTurnChanged;

        if (waitForTurnManagerCoroutine != null)
        {
            StopCoroutine(waitForTurnManagerCoroutine);
            waitForTurnManagerCoroutine = null;
        }
        if (currentCiv != null)
        {
            currentCiv.OnTechStarted -= OnTechOrCultureStarted;
            currentCiv.OnCultureStarted -= OnTechOrCultureStarted;
            currentCiv.OnTechResearched -= OnTechOrCultureStarted;
            currentCiv.OnCultureCompleted -= OnTechOrCultureStarted;
            currentCiv.OnFoodChanged -= OnFoodChangedHandler;
            currentCiv.OnGoldChanged -= OnGoldChangedHandler;
            currentCiv.OnFaithChanged -= OnFaithChangedHandler;
            currentCiv.OnPolicyPointsChanged -= OnPolicyPointsChangedHandler;
        }
    }

    private void SetupAfterGameStart()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnGameStarted -= SetupAfterGameStart;

        if (waitForTurnManagerCoroutine != null)
        {
            StopCoroutine(waitForTurnManagerCoroutine);
            waitForTurnManagerCoroutine = null;
        }

        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.OnTurnChanged += HandleTurnChanged;

            // Initialize immediately using current turn info if available
            var civ = TurnManager.Instance.GetCurrentCivilization();
            int round = TurnManager.Instance.round;
            if (civ != null)
                HandleTurnChanged(civ, round);
        }
        else
        {
            // If TurnManager still isn't ready, wait for it
            waitForTurnManagerCoroutine = StartCoroutine(WaitForTurnManager());
        }
    }

    private IEnumerator WaitForTurnManager()
    {
        while (TurnManager.Instance == null)
            yield return null;

        TurnManager.Instance.OnTurnChanged += HandleTurnChanged;

        var civ = TurnManager.Instance.GetCurrentCivilization();
        int round = TurnManager.Instance.round;
        if (civ != null)
            HandleTurnChanged(civ, round);

        waitForTurnManagerCoroutine = null;
    }

    private void HandleTurnChanged(Civilization civ, int round)
    {
if (civ == null)
        {
            Debug.LogError("[PlayerUI] HandleTurnChanged received a null civilization. Aborting.");
            return;
        }

        bool isPlayer = civ.isPlayerControlled;
// Show/hide turn change panel
        if (turnChangePanel != null)
        {
turnChangePanel.SetActive(!isPlayer);
        } else {
            Debug.LogWarning("[PlayerUI] turnChangePanel reference is NOT assigned in the inspector!");
        }

        // Update main player panel only if it's the player's turn
        if (isPlayer)
        {
UpdatePlayerPanel(civ, round);
        }

        // Update the content of the turn change panel regardless of its state
UpdateTurnChangePanel(civ, round);
    }

    private void UpdatePlayerPanel(Civilization civ, int round)
    {
        // If the civ being displayed changed, update subscriptions so UI reflects its live events
        if (currentCiv != civ)
        {
            if (currentCiv != null)
            {
                currentCiv.OnTechStarted -= OnTechOrCultureStarted;
                currentCiv.OnCultureStarted -= OnTechOrCultureStarted;
                currentCiv.OnTechResearched -= OnTechOrCultureStarted;
                currentCiv.OnCultureCompleted -= OnTechOrCultureStarted;
                currentCiv.OnFoodChanged -= OnFoodChangedHandler;
                currentCiv.OnGoldChanged -= OnGoldChangedHandler;
                currentCiv.OnFaithChanged -= OnFaithChangedHandler;
                currentCiv.OnPolicyPointsChanged -= OnPolicyPointsChangedHandler;
            }

            currentCiv = civ;

            if (currentCiv != null)
            {
                currentCiv.OnTechStarted += OnTechOrCultureStarted;
                currentCiv.OnCultureStarted += OnTechOrCultureStarted;
                currentCiv.OnTechResearched += OnTechOrCultureStarted;
                currentCiv.OnCultureCompleted += OnTechOrCultureStarted;
                currentCiv.OnFoodChanged += OnFoodChangedHandler;
                currentCiv.OnGoldChanged += OnGoldChangedHandler;
                currentCiv.OnFaithChanged += OnFaithChangedHandler;
                currentCiv.OnPolicyPointsChanged += OnPolicyPointsChangedHandler;
            }
        }
        
        // Top info
        if (civNameText != null) civNameText.text = civ.civData.civName;
        // Ensure round is at least 1
        int displayRound = (round <= 0) ? 1 : round;
        if (roundText != null) roundText.text = $"Round {displayRound}";
        // Age display removed

        // Yields - Calculate from cities
        int totalFood = SumCityYield(civ, city => city.GetFoodPerTurn());
        int totalGold = SumCityYield(civ, city => city.GetGoldPerTurn());
        int totalScience = SumCityYield(civ, city => city.GetSciencePerTurn());
        int totalCulture = SumCityYield(civ, city => city.GetCulturePerTurn());
        int totalPolicyPoints = SumCityYield(civ, city => city.GetPolicyPointPerTurn());
        int totalFaith = SumCityYield(civ, city => city.GetFaithPerTurn());

        // Add per-turn yields from combat units
        if (civ.combatUnits != null)
        {
            foreach (var u in civ.combatUnits)
            {
                if (u == null || u.data == null) continue;
                var y = civ.ComputeUnitPerTurnYield(u.data, u.Weapon, u.Shield, u.Armor, u.Miscellaneous);
                totalFood += y.food;
                totalGold += y.gold;
                totalScience += y.science;
                totalCulture += y.culture;
                totalFaith += y.faith;
                totalPolicyPoints += y.policy;
            }
        }

        // Add per-turn yields from worker units
        if (civ.workerUnits != null)
        {
            foreach (var w in civ.workerUnits)
            {
                if (w == null || w.data == null) continue;
                var y = civ.ComputeWorkerPerTurnYield(w.data);
                totalFood += y.food;
                totalGold += y.gold;
                totalScience += y.science;
                totalCulture += y.culture;
                totalFaith += y.faith;
                totalPolicyPoints += y.policy;
            }
        }

        string FormatRate(int rate) => rate >= 0 ? $"+{rate}" : rate.ToString();

        if (foodYieldText != null) foodYieldText.text = $"{civ.food} ({FormatRate(totalFood)})";
        if (goldYieldText != null) goldYieldText.text = $"{civ.gold} ({FormatRate(totalGold)})";
        if (scienceYieldText != null) scienceYieldText.text = $"{civ.science} ({FormatRate(totalScience)})";
        if (cultureYieldText != null) cultureYieldText.text = $"{civ.culture} ({FormatRate(totalCulture)})";
        if (policyPointYieldText != null) policyPointYieldText.text = $"{civ.policyPoints} ({FormatRate(totalPolicyPoints)})";
        if (faithYieldText != null) faithYieldText.text = $"{civ.faith} ({FormatRate(totalFaith)})";

        // Inventory - Use the existing ResourceManager to get the civilization's resource inventory
        PopulateResourceList(civ);
        
        // (Tech and culture text displays removed; buttons remain)
    }

    private int SumCityYield(Civilization civ, System.Func<City, int> selector)
    {
        int sum = 0;
        if (civ?.cities != null)
        {
            foreach (var city in civ.cities)
            {
                if (city != null)
                    sum += selector(city);
            }
        }
        return sum;
    }

    private void PopulateResourceList(Civilization civ)
    {
        if (resourceListContainer == null)
        {
            Debug.LogWarning("PlayerUI: resourceListContainer is null");
            return;
        }

        // Clear existing resource entries
        foreach (Transform t in resourceListContainer) 
        {
            if (t != null) Destroy(t.gameObject);
        }

        // Use the existing ResourceManager to get the civilization's resource inventory
        if (ResourceManager.Instance != null && resourceEntryPrefab != null)
        {
            var inventory = ResourceManager.Instance.GetInventory(civ);
            
            foreach (var pair in inventory)
            {
                if (pair.Key != null && pair.Value > 0) // Only show resources with positive amounts
                {
                    var go = Instantiate(resourceEntryPrefab, resourceListContainer);
                    TextMeshProUGUI buttonText = go.GetComponentInChildren<TextMeshProUGUI>();

                    if (buttonText != null)
                    {
                        buttonText.text = $"{pair.Key.resourceName}: {pair.Value}";
                    }
                    else
                    {
                        Debug.LogWarning("ResourceEntryPrefab is missing a TextMeshProUGUI component for displaying resource name and amount.");
                    }
                }
            }
        }
        else
        {
            Debug.LogWarning("PlayerUI: ResourceManager.Instance is null or resourceEntryPrefab is not assigned");
        }
    }

    private void UpdateTurnChangePanel(Civilization civ, int round)
    {
        if (upcomingCivText != null) upcomingCivText.text = $"{civ.civData.civName}'s Turn";
        if (upcomingCivIcon != null)
        {
            if (civ.civData.icon != null)
                upcomingCivIcon.sprite = civ.civData.icon;
            else
                upcomingCivIcon.sprite = null; // Or assign a default icon if you have one
        }
    }

    // ---------------- Layer Dropdown ----------------

    // Tracks which PlanetLayerType each dropdown index maps to (rebuilt when planet changes)
    private List<GameManager.PlanetLayerType> _layerDropdownMapping = new List<GameManager.PlanetLayerType>();

    private PlanetGenerator GetActivePlanetGenerator()
    {
        if (GameManager.Instance != null) return GameManager.Instance.GetCurrentPlanetGenerator();
        return FindAnyObjectByType<PlanetGenerator>();
    }

    private LayerManager GetActiveLayerManager()
    {
        var gen = GetActivePlanetGenerator();
        if (gen == null) return null;
        var lm = gen.GetComponent<LayerManager>();
        if (lm == null)
        {
            Debug.LogError("PlayerUI: No LayerManager found on active PlanetGenerator.");
        }
        return lm;
    }

    /// <summary>
    /// Rebuild the layer dropdown options based on the current planet's supported layers.
    /// Call this when switching planets.
    /// </summary>
    public void RefreshLayerDropdown()
    {
        if (layerDropdown == null) return;

        var lm = GetActiveLayerManager();
        _layerDropdownMapping.Clear();
        layerDropdown.ClearOptions();

        var options = new List<TMP_Dropdown.OptionData>();

        // Only add layers this planet actually supports
        GameManager.PlanetLayerType[] layersToCheck = {
            GameManager.PlanetLayerType.Surface,
            GameManager.PlanetLayerType.Underwater,
            GameManager.PlanetLayerType.Atmosphere,
            GameManager.PlanetLayerType.Orbit
        };

        foreach (var layer in layersToCheck)
        {
            if (lm != null && lm.IsLayerSupported(layer))
            {
                _layerDropdownMapping.Add(layer);
                options.Add(new TMP_Dropdown.OptionData(layer.ToString()));
            }
        }

        // Fallback: always have at least Surface
        if (options.Count == 0)
        {
            _layerDropdownMapping.Add(GameManager.PlanetLayerType.Surface);
            options.Add(new TMP_Dropdown.OptionData("Surface"));
        }

        layerDropdown.AddOptions(options);
        layerDropdown.SetValueWithoutNotify(0); // Default to first (Surface)
        layerDropdown.RefreshShownValue();
    }

    private void OnLayerDropdownChanged(int index)
    {
        if (index < 0 || index >= _layerDropdownMapping.Count) return;

        var selectedLayer = _layerDropdownMapping[index];
        var lm = GetActiveLayerManager();
        if (lm == null)
        {
            Debug.LogWarning("PlayerUI: No LayerManager available to switch layers.");
            return;
        }

        lm.SetOnlyLayerVisible(selectedLayer);
        Debug.Log($"PlayerUI: Switched to layer '{selectedLayer}' via dropdown.");
    }

    /// <summary>
    /// Legacy compatibility — calls RefreshLayerDropdown instead.
    /// </summary>
    public void UpdateLayerButtonVisibility()
    {
        RefreshLayerDropdown();
    }

    /// <summary>
    /// Disable all volumetric effects. Call when switching planets or cleaning up.
    /// </summary>
    public void DisableAllVolumetrics()
    {
        var lm = GetActiveLayerManager();
        if (lm == null) return;

        // LayerManager owns volumetrics; disabling Atmosphere visibility is the clean "no-volumetrics" state.
        if (lm.IsLayerSupported(GameManager.PlanetLayerType.Atmosphere))
        {
            lm.SetLayerVisible(GameManager.PlanetLayerType.Atmosphere, false);
        }
    }

    private void OnTechOrCultureStarted(TechData tech) { UpdatePlayerPanel(currentCiv, -1); }
    private void OnTechOrCultureStarted(CultureData cult) { UpdatePlayerPanel(currentCiv, -1); }
}
