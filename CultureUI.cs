using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class CultureUI : MonoBehaviour
{
    [Header("Panel References")]
    [SerializeField] private GameObject culturePanel;
    [SerializeField] private ScrollRect cultureScrollRect; // The ScrollRect for the culture tree
    [SerializeField] private RectTransform cultureContent; // The content area of the ScrollRect
    [SerializeField] private Transform cultureButtonContainer; // ScrollRect's content (fallback for list view)
    [SerializeField] private GameObject cultureButtonPrefab; // Prefab for culture buttons
    
    [Header("Culture Tree Integration")]
    [SerializeField] private CultureTreeBackgroundData backgroundData; // Background system
    [SerializeField] private bool useCustomLayout = true; // Use saved layout vs list layout
    [SerializeField] private string layoutFileName = "CultureTreeLayout.json"; // JSON file from CultureTreeBuilder
    [SerializeField] private Vector2 cultureNodeSize = new Vector2(200, 100); // Size of each culture node

    [Header("Info Panel")]
    [SerializeField] private TextMeshProUGUI selectedCultureNameText;
    [SerializeField] private TextMeshProUGUI selectedCultureDescriptionText;
    [SerializeField] private TextMeshProUGUI selectedCultureCostText;
    [SerializeField] private TextMeshProUGUI selectedCulturePrerequisitesText;
    [SerializeField] private TextMeshProUGUI selectedCultureUnlocksText;
    [SerializeField] private Button closeButton;

    private Civilization playerCiv;
    private CultureData currentlySelectedCulture;
    private List<CultureButtonUI> cultureButtons = new List<CultureButtonUI>();

    void Start()
    {
        if (closeButton != null) 
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(() => 
            {
                if (UIManager.Instance != null)
                {
                    UIManager.Instance.HidePanel("culturePanel");
                }
                else
                {
                    Debug.LogError("CultureUI: UIManager.Instance is null. Cannot hide panel.");
                    if (culturePanel != null) culturePanel.SetActive(false); // Fallback
                }
            });
        }
    }

    public void Show(Civilization civ)
    {
        playerCiv = civ;
        if (playerCiv == null)
        {
            Debug.LogError("CultureUI Show called with null civ");
            return;
        }
        UIManager.Instance.ShowPanel("culturePanel");
        
        if (useCustomLayout)
        {
            CreateCultureTreeWithCustomLayout();
        }
        else
        {
            PopulateCultureOptions();
        }
        
        ClearInfoPanel(); // Do not auto-select any culture
    }

    public void Hide()
    {
        // This method is called by the close button's new listener directly calling GameManager.HideCulturePanel().
        // If the root is being hidden, the internal panel will also be hidden.
        if (culturePanel != null && !this.gameObject.activeInHierarchy) 
        {
            // culturePanel.SetActive(false); // Let its state be tied to the root's state
        }
    }

    void PopulateCultureOptions()
    {
        foreach (Transform child in cultureButtonContainer)
        {
            Destroy(child.gameObject);
        }
        cultureButtons.Clear();

        if (CultureManager.Instance == null || CultureManager.Instance.allCultures == null)
        {
            Debug.LogError("CultureManager or its cultures not available.");
            return;
        }

        foreach (CultureData culture in CultureManager.Instance.allCultures.OrderBy(c => c.cultureCost))
        {
            GameObject buttonGO = Instantiate(cultureButtonPrefab, cultureButtonContainer);
            CultureButtonUI cultureButtonUI = buttonGO.GetComponent<CultureButtonUI>();
            if (cultureButtonUI != null)
            {
                cultureButtonUI.Initialize(culture, this);
                cultureButtons.Add(cultureButtonUI);
                UpdateCultureButtonState(cultureButtonUI, culture);
            }
            else
            {
                Button button = buttonGO.GetComponent<Button>();
                TextMeshProUGUI buttonText = buttonGO.GetComponentInChildren<TextMeshProUGUI>();
                if (buttonText != null) buttonText.text = culture.cultureName;
                if (button != null) button.onClick.AddListener(() => SelectCulture(culture));
            }
        }
        RefreshCultureButtonStates();
    }
    
    private void CreateCultureTreeWithCustomLayout()
    {
        // Clear existing buttons
        foreach (Transform child in cultureContent != null ? cultureContent : cultureButtonContainer)
        {
            Destroy(child.gameObject);
        }
        cultureButtons.Clear();
        
        // Load layout from JSON file
        CultureTreeLayout layout = LoadLayoutFromFile();
        if (layout == null)
        {
            Debug.LogWarning("No culture tree layout found, falling back to list view");
            PopulateCultureOptions();
            return;
        }
        
        // Create background images if we have background data
        if (backgroundData != null && cultureContent != null)
        {
            CreateBackgroundImages();
        }
        
        // Set content size from layout
        if (cultureContent != null && layout.culturePositions != null && layout.culturePositions.Count > 0)
        {
            // Calculate content size based on culture positions
            float maxX = 0f, minY = 0f;
            foreach (var pos in layout.culturePositions)
            {
                maxX = Mathf.Max(maxX, pos.position.x + cultureNodeSize.x);
                minY = Mathf.Min(minY, pos.position.y - cultureNodeSize.y);
            }
            cultureContent.sizeDelta = new Vector2(maxX + 100f, Mathf.Abs(minY) + 100f);
        }
        
        // Create culture nodes at their saved positions
        if (CultureManager.Instance?.allCultures != null)
        {
            foreach (CultureData culture in CultureManager.Instance.allCultures)
            {
                // Find saved position for this culture
                Vector2 position = Vector2.zero;
                bool foundPosition = false;
                
                if (layout.culturePositions != null)
                {
                    foreach (var pos in layout.culturePositions)
                    {
                        if (pos.cultureName == culture.cultureName)
                        {
                            position = pos.position;
                            foundPosition = true;
                            break;
                        }
                    }
                }
                
                if (!foundPosition)
                {
                    Debug.LogWarning($"No saved position found for culture: {culture.cultureName}");
                    continue; // Skip cultures not in the saved layout
                }
                
                CreateCultureNode(culture, position);
            }
        }
        
        RefreshCultureButtonStates();
    }
    
    private void CreateBackgroundImages()
    {
        if (backgroundData == null || cultureContent == null) return;
        
        // Get all backgrounds in age order
        Sprite[] backgroundImages = backgroundData.GetAllBackgroundsInOrder();
        
        if (backgroundImages.Length == 0) return;
        
        // Calculate total background width
        float totalBackgroundWidth = backgroundData.GetTotalWidth();
        float imageHeight = 1024f * backgroundData.backgroundScale;
        
        // Create background container
        GameObject backgroundContainer = new GameObject("BackgroundContainer");
        backgroundContainer.transform.SetParent(cultureContent, false);
        
        RectTransform bgRect = backgroundContainer.AddComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0, 1); // Top-left origin
        bgRect.anchorMax = new Vector2(0, 1);
        bgRect.pivot = new Vector2(0, 1);
        bgRect.sizeDelta = new Vector2(totalBackgroundWidth, imageHeight);
        bgRect.anchoredPosition = Vector2.zero;
        
        // Set background to render behind everything else
        backgroundContainer.transform.SetAsFirstSibling();
        
        // Create individual background images for each age
        var allAges = System.Enum.GetValues(typeof(TechAge));
        float currentX = 0f;
        int imageIndex = 0;
        
        foreach (TechAge age in allAges)
        {
            Sprite ageBackground = backgroundData.GetBackgroundForAge(age);
            if (ageBackground == null) continue;
            
            GameObject ageImageObj = new GameObject($"Background_{age}");
            ageImageObj.transform.SetParent(backgroundContainer.transform, false);
            
            Image ageImage = ageImageObj.AddComponent<Image>();
            ageImage.sprite = ageBackground;
            ageImage.type = Image.Type.Sliced;
            
            float imageWidth = ageBackground.texture.width * backgroundData.backgroundScale;
            
            RectTransform ageRect = ageImage.rectTransform;
            ageRect.anchorMin = new Vector2(0, 0);
            ageRect.anchorMax = new Vector2(0, 1);
            ageRect.pivot = new Vector2(0, 0.5f);
            ageRect.sizeDelta = new Vector2(imageWidth, imageHeight);
            ageRect.anchoredPosition = new Vector2(currentX, 0);
            
            currentX += imageWidth + backgroundData.imageSpacing;
            imageIndex++;
        }
        
        Debug.Log($"Created {imageIndex} age-based background images for culture tree");
    }
    
    private void CreateCultureNode(CultureData culture, Vector2 position)
    {
        GameObject buttonGO = Instantiate(cultureButtonPrefab, cultureContent != null ? cultureContent : cultureButtonContainer);
        
        // Set position for custom layout
        if (cultureContent != null)
        {
            RectTransform rectTransform = buttonGO.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.anchorMin = new Vector2(0, 1); // Top-left anchor
                rectTransform.anchorMax = new Vector2(0, 1);
                rectTransform.pivot = new Vector2(0.5f, 0.5f);
                rectTransform.sizeDelta = cultureNodeSize;
                rectTransform.anchoredPosition = position;
            }
        }
        
        // Initialize the button
        CultureButtonUI cultureButtonUI = buttonGO.GetComponent<CultureButtonUI>();
        if (cultureButtonUI != null)
        {
            cultureButtonUI.Initialize(culture, this);
            cultureButtons.Add(cultureButtonUI);
            UpdateCultureButtonState(cultureButtonUI, culture);
        }
        else
        {
            // Fallback for buttons without CultureButtonUI component
            Button button = buttonGO.GetComponent<Button>();
            TextMeshProUGUI buttonText = buttonGO.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null) buttonText.text = culture.cultureName;
            if (button != null) button.onClick.AddListener(() => SelectCulture(culture));
        }
    }
    
    private CultureTreeLayout LoadLayoutFromFile()
    {
        string filePath = "";
        
#if UNITY_EDITOR
        // In editor, look in Assets folder
        filePath = System.IO.Path.Combine(Application.dataPath, layoutFileName);
#else
        // In build, look in persistent data path
        filePath = System.IO.Path.Combine(Application.persistentDataPath, layoutFileName);
#endif
        
        if (!System.IO.File.Exists(filePath))
        {
            Debug.LogWarning($"Culture tree layout file not found at: {filePath}");
            return null;
        }
        
        try
        {
            string json = System.IO.File.ReadAllText(filePath);
            CultureTreeLayout layout = JsonUtility.FromJson<CultureTreeLayout>(json);
            Debug.Log($"Loaded culture tree layout with {layout?.culturePositions?.Count ?? 0} culture positions");
            return layout;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to load culture tree layout: {e.Message}");
            return null;
        }
    }

    public void SelectCulture(CultureData culture)
    {
        currentlySelectedCulture = culture;
        UpdateInfoPanel(culture);

        // Immediately start adoption if possible
        if (playerCiv != null && playerCiv.CanCultivate(culture))
        {
            playerCiv.StartCulture(culture);
            RefreshUI();
        }

        foreach (var btnUI in cultureButtons)
        {
            btnUI.SetSelected(culture == btnUI.RepresentedCulture);
        }
    }

    void UpdateInfoPanel(CultureData culture)
    {
        if (culture == null)
        {
            ClearInfoPanel();
            return;
        }

        selectedCultureNameText.text = culture.cultureName;
        selectedCultureDescriptionText.text = culture.description;
        selectedCultureCostText.text = $"Cost: {culture.cultureCost} Culture";

        string prereqs = "Prerequisites: ";
        if (culture.requiredCultures != null && culture.requiredCultures.Length > 0)
        {
            prereqs += string.Join(", ", culture.requiredCultures.Select(c => c.cultureName));
        }
        else
        {
            prereqs += "None";
        }
        selectedCulturePrerequisitesText.text = prereqs;

        string unlocks = "Unlocks: ";
        List<string> unlockItems = new List<string>();
        if (culture.unlockedUnits != null) unlockItems.AddRange(culture.unlockedUnits.Select(u => u.unitName + " (Unit)"));
        if (culture.unlockedWorkerUnits != null) unlockItems.AddRange(culture.unlockedWorkerUnits.Select(w => w.unitName + " (Worker)"));
        if (culture.unlockedBuildings != null) unlockItems.AddRange(culture.unlockedBuildings.Select(b => b.buildingName + " (Building)"));
        if (culture.unlockedAbilities != null) unlockItems.AddRange(culture.unlockedAbilities.Select(a => a.abilityName + " (Ability)"));
        if (culture.unlocksPolicies != null) unlockItems.AddRange(culture.unlocksPolicies.Select(p => p.policyName + " (Policy)"));
        if (unlockItems.Count > 0)
        {
            unlocks += string.Join(", ", unlockItems);
        }
        else
        {
            unlocks += "Nothing yet";
        }
        selectedCultureUnlocksText.text = unlocks;
    }
    
    void ClearInfoPanel()
    {
        selectedCultureNameText.text = "Select a Culture";
        selectedCultureDescriptionText.text = "";
        selectedCultureCostText.text = "";
        selectedCulturePrerequisitesText.text = "";
        selectedCultureUnlocksText.text = "";
    }

    public void RefreshUI()
    {
        if (playerCiv == null) return;
        RefreshCultureButtonStates();
        if (playerCiv.currentCulture != null)
        {
            UpdateInfoPanel(playerCiv.currentCulture);
             foreach (var btnUI in cultureButtons)
            {
                btnUI.SetSelected(playerCiv.currentCulture == btnUI.RepresentedCulture);
            }
        }
        else if (currentlySelectedCulture != null)
        {
            UpdateInfoPanel(currentlySelectedCulture);
        }
        else
        {
            ClearInfoPanel();
        }
    }

    private void UpdateCultureButtonState(CultureButtonUI buttonUI, CultureData culture)
    {
        if (playerCiv.researchedCultures.Contains(culture))
        {
            buttonUI.SetState(CultureButtonUI.CultureState.Researched);
        }
        else if (playerCiv.currentCulture == culture)
        {
            buttonUI.SetState(CultureButtonUI.CultureState.Researching);
        }
        else if (playerCiv.CanCultivate(culture))
        {
            buttonUI.SetState(CultureButtonUI.CultureState.Available);
        }
        else
        {
            buttonUI.SetState(CultureButtonUI.CultureState.Locked);
        }
    }
    public void RefreshCultureButtonStates()
    {
        foreach (var btnUI in cultureButtons)
        {
            UpdateCultureButtonState(btnUI, btnUI.RepresentedCulture);
        }
    }
}

// Layout data structure for loading from JSON
[System.Serializable]
public class CultureTreeLayout
{
    public List<CulturePosition> culturePositions;
}

[System.Serializable]
public class CulturePosition
{
    public string cultureName;
    public Vector2 position;
}


// Helper script for the CultureButton prefab (CultureButtonUI.cs)
// You would create this script and attach it to your cultureButtonPrefab
/*
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CultureButtonUI : MonoBehaviour
{
    public CultureData RepresentedCulture { get; private set; }
    private CultureUI cultureUI;

    [SerializeField] private TextMeshProUGUI cultureNameText;
    [SerializeField] private Image iconImage;
    [SerializeField] private Image backgroundImage;

    [SerializeField] private Color researchedColor = Color.green;
    [SerializeField] private Color researchingColor = Color.yellow;
    [SerializeField] private Color availableColor = Color.white;
    [SerializeField] private Color lockedColor = Color.gray;
    [SerializeField] private Color selectedColor = Color.cyan;
    
    private Button button;
    private bool isSelected = false;

    public enum CultureState { Available, Researched, Researching, Locked }
    private CultureState currentState;

    public void Initialize(CultureData culture, CultureUI ownerUI)
    {
        RepresentedCulture = culture;
        cultureUI = ownerUI;
        cultureNameText.text = culture.cultureName;
        // if (iconImage != null && culture.icon != null) iconImage.sprite = culture.icon;

        button = GetComponent<Button>();
        button.onClick.AddListener(() => cultureUI.SelectCulture(RepresentedCulture));
    }

    public void SetState(CultureState state)
    {
        currentState = state;
        if (isSelected)
        {
            backgroundImage.color = selectedColor;
        }
        else
        {
            switch (state)
            {
                case CultureState.Researched:
                    backgroundImage.color = researchedColor;
                    break;
                case CultureState.Researching:
                    backgroundImage.color = researchingColor;
                    break;
                case CultureState.Available:
                    backgroundImage.color = availableColor;
                    break;
                case CultureState.Locked:
                    backgroundImage.color = lockedColor;
                    break;
            }
        }
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;
        SetState(currentState); // Re-apply color based on new selection state
    }
}
*/ 