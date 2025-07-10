// Assets/Scripts/UI/PlayerUI.cs
using System.Collections.Generic;
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
    [SerializeField] private TextMeshProUGUI ageText;
    [SerializeField] private Image ageIconImage;
    [SerializeField] private Button endTurnButton;
    [SerializeField] private Button techButton;
    [SerializeField] private Button cultureButton;
    [SerializeField] private Button policyButton;
    [SerializeField] private Button diplomacyButton;

    [Header("Player Panel - Yields")]
    [SerializeField] private TextMeshProUGUI foodYieldText;
    [SerializeField] private TextMeshProUGUI goldYieldText;
    [SerializeField] private TextMeshProUGUI scienceYieldText;
    [SerializeField] private TextMeshProUGUI cultureYieldText;
    [SerializeField] private TextMeshProUGUI policyPointYieldText;

    [Header("Player Panel - Resources Inventory")]
    [SerializeField] private Transform resourceListContainer;
    [SerializeField] private GameObject resourceEntryPrefab; // icon + amount
    
    [Header("Player Panel - Research/Culture Progress")]
    [SerializeField] private TextMeshProUGUI techNameText;
    [SerializeField] private TextMeshProUGUI techTurnsLeftText;
    [SerializeField] private TextMeshProUGUI cultureNameText;
    [SerializeField] private TextMeshProUGUI cultureTurnsLeftText;

    [Header("Turn Change Panel")]
    [SerializeField] private TextMeshProUGUI upcomingCivText;
    [SerializeField] private Image upcomingCivIcon;

    private Civilization currentCiv;

    void Start()
    {
        // playerPanel's active state should be true in its prefab if it's meant to be visible initially.
        // HandleTurnChanged will manage its visibility based on whose turn it is.
        if (turnChangePanel != null) turnChangePanel.SetActive(false); // Keep turn change panel hidden initially

        SetupButtonListeners();
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
                Debug.Log("Tech button clicked");
                if (UIManager.Instance != null && currentCiv != null)
                {
                    UIManager.Instance.ShowTechPanel(currentCiv);
                }
                else
                {
                    Debug.LogError("PlayerUI: UIManager or currentCiv is null, cannot show tech panel.");
                }
            });
        }

        if (cultureButton != null)
        {
            cultureButton.onClick.RemoveAllListeners();
            cultureButton.onClick.AddListener(() => 
            {
                Debug.Log("Culture button clicked");
                if (UIManager.Instance != null && currentCiv != null)
                {
                    UIManager.Instance.ShowCulturePanel(currentCiv);
                }
                else
                {
                    Debug.LogError("PlayerUI: UIManager or currentCiv is null, cannot show culture panel.");
                }
            });
        }

        // Assuming you have a policyButton and want to toggle a policy panel (ReligionPanel used as placeholder)
        if (policyButton != null) 
        {
            policyButton.onClick.RemoveAllListeners();
            policyButton.onClick.AddListener(() => 
            {
                if (UIManager.Instance != null)
                    UIManager.Instance.ShowPanel("ReligionPanel"); // Placeholder - replace with actual policy panel logic
                else
                    Debug.LogError("PlayerUI: UIManager.Instance is null when trying to show policy/religion panel!");
            });
        }
        

        if (diplomacyButton != null) 
        {
            diplomacyButton.onClick.RemoveAllListeners();
            diplomacyButton.onClick.AddListener(() => 
            {
                if (UIManager.Instance != null)
                {
                    UIManager.Instance.ShowPanel("DiplomacyPanel");
                }
                else
                {
                    Debug.LogError("PlayerUI: UIManager.Instance is null when trying to show diplomacy panel!");
                }
            });
        }

        Debug.Log("PlayerUI: Button listeners set up.");
    }

    /// <summary>
    /// Initializes the PlayerUI display with the player's data. 
    /// Called by GameManager after PlayerUI is instantiated.
    /// </summary>
    public void InitializePlayerDisplay(Civilization civ, int round)
    {
        Debug.Log($"PlayerUI: InitializePlayerDisplay called for {civ?.civData?.civName ?? "NULL"}, Round: {round}");
        if (playerPanel != null) 
        {
            playerPanel.SetActive(true);
            Debug.Log("PlayerUI: playerPanel activated by InitializePlayerDisplay.");
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
        UpdatePlayerPanel(civ, round);
    }

    void OnEnable()
    {
        // Subscribe to turn manager events with null checks
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.OnTurnChanged += HandleTurnChanged;
            Debug.Log("PlayerUI: Subscribed to TurnManager events");
        }
        else
        {
            Debug.LogWarning("PlayerUI: TurnManager.Instance is null during OnEnable");
        }
        if (currentCiv != null)
        {
            currentCiv.OnTechStarted += OnTechOrCultureStarted;
            currentCiv.OnCultureStarted += OnTechOrCultureStarted;
            currentCiv.OnTechResearched += OnTechOrCultureStarted;
            currentCiv.OnCultureCompleted += OnTechOrCultureStarted;
        }
    }

    void OnDisable()
    {
        if (TurnManager.Instance != null)
            TurnManager.Instance.OnTurnChanged -= HandleTurnChanged;
        if (currentCiv != null)
        {
            currentCiv.OnTechStarted -= OnTechOrCultureStarted;
            currentCiv.OnCultureStarted -= OnTechOrCultureStarted;
            currentCiv.OnTechResearched -= OnTechOrCultureStarted;
            currentCiv.OnCultureCompleted -= OnTechOrCultureStarted;
        }
    }

    private void HandleTurnChanged(Civilization civ, int round)
    {
        Debug.Log($"[PlayerUI] HandleTurnChanged called for civ: {civ?.civData.civName ?? "NULL"}, round: {round}");
        if (civ == null)
        {
            Debug.LogError("[PlayerUI] HandleTurnChanged received a null civilization. Aborting.");
            return;
        }

        bool isPlayer = civ.isPlayerControlled;
        Debug.Log($"[PlayerUI] Is player turn? {isPlayer}.");
        
        // Show/hide turn change panel
        if (turnChangePanel != null)
        {
            Debug.Log($"[PlayerUI] Setting turnChangePanel active state to: {!isPlayer}");
            turnChangePanel.SetActive(!isPlayer);
        } else {
            Debug.LogWarning("[PlayerUI] turnChangePanel reference is NOT assigned in the inspector!");
        }

        // Update main player panel only if it's the player's turn
        if (isPlayer)
        {
            Debug.Log("[PlayerUI] Updating main player panel.");
            UpdatePlayerPanel(civ, round);
        }

        // Update the content of the turn change panel regardless of its state
        Debug.Log("[PlayerUI] Updating turn change panel content.");
            UpdateTurnChangePanel(civ, round);
    }

    private void UpdatePlayerPanel(Civilization civ, int round)
    {
        Debug.Log($"PlayerUI: Updating player panel for {civ.civData.civName}");
        
        currentCiv = civ;
        
        // Top info
        if (civNameText != null) civNameText.text = civ.civData.civName;
        // Ensure round is at least 1
        int displayRound = (round <= 0) ? 1 : round;
        if (roundText != null) roundText.text = $"Round {displayRound}";
        if (ageText != null)
        {
            var age = civ.GetCurrentAge();
            ageText.text = age.ToString().Replace("Age", " Age");
            if (ageIconImage != null)
            {
                string iconName = age.ToString(); // e.g., "PrehistoricAge"
                Sprite icon = Resources.Load<Sprite>($"Icons/Ages/{iconName}");
                if (icon != null)
                    ageIconImage.sprite = icon;
                else
                    Debug.LogWarning($"PlayerUI: Could not find age icon for {iconName} in Icons/Ages/");
            }
        }

        // Yields - Calculate from cities
        int totalFood = SumCityYield(civ, city => city.GetFoodPerTurn());
        int totalGold = SumCityYield(civ, city => city.GetGoldPerTurn());
        int totalScience = SumCityYield(civ, city => city.GetSciencePerTurn());
        int totalCulture = SumCityYield(civ, city => city.GetCulturePerTurn());
        int totalPolicyPoints = SumCityYield(civ, city => city.GetPolicyPointPerTurn());

        if (foodYieldText != null) foodYieldText.text = $"+{totalFood}";
        if (goldYieldText != null) goldYieldText.text = $"+{totalGold}";
        if (scienceYieldText != null) scienceYieldText.text = $"+{totalScience}";
        if (cultureYieldText != null) cultureYieldText.text = $"+{totalCulture}";
        if (policyPointYieldText != null) policyPointYieldText.text = $"+{totalPolicyPoints}";

        // Inventory - Use the existing ResourceManager to get the civilization's resource inventory
        PopulateResourceList(civ);
        
        // Research progress
        if (techNameText != null && techTurnsLeftText != null)
        {
            if (civ.currentTech != null)
            {
                techNameText.text = civ.currentTech.techName;
                int turnsLeft = Mathf.CeilToInt((civ.currentTech.scienceCost - civ.currentTechProgress) 
                                  / Mathf.Max(1, totalScience));
                techTurnsLeftText.text = $"{turnsLeft} turns";
            }
            else
            {
                techNameText.text = "No Research";
                techTurnsLeftText.text = "";
            }
        }

        // Culture progress
        if (cultureNameText != null && cultureTurnsLeftText != null)
        {
            if (civ.currentCulture != null)
            {
                cultureNameText.text = civ.currentCulture.cultureName;
                int turnsLeft = Mathf.CeilToInt((civ.currentCulture.cultureCost - civ.currentCultureProgress) 
                                  / Mathf.Max(1, totalCulture));
                cultureTurnsLeftText.text = $"{turnsLeft} turns";
            }
            else
            {
                cultureNameText.text = "No Culture";
                cultureTurnsLeftText.text = "";
            }
        }
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

    private void OnTechOrCultureStarted(TechData tech) { UpdatePlayerPanel(currentCiv, -1); }
    private void OnTechOrCultureStarted(CultureData cult) { UpdatePlayerPanel(currentCiv, -1); }
}
