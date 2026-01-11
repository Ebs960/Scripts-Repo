using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class TechUI : MonoBehaviour
{
    [Header("Panel References")]
    [SerializeField] private GameObject techPanel;
    [SerializeField] private ScrollRect techScrollRect; // The ScrollRect for the tech tree
    [SerializeField] private RectTransform techContent; // The content area of the ScrollRect
    [SerializeField] private GameObject techButtonPrefab; // Prefab for tech buttons

    [Header("Tech Tree Integration")]
    [SerializeField] private TechTreeBackgroundData backgroundData; // Background system
    [SerializeField] private bool useCustomLayout = true; // Use saved layout vs grid layout
    [SerializeField] private string layoutFileName = "TechTreeLayout.json"; // JSON file from TechTreeBuilder
    [SerializeField] private Vector2 techNodeSize = new Vector2(180, 90); // Size of each tech node
    [SerializeField] private Vector2 gridSpacing = new Vector2(200, 100); // Spacing between nodes

    [Header("Info Panel")]
    [SerializeField] private TextMeshProUGUI selectedTechNameText;
    [SerializeField] private TextMeshProUGUI selectedTechDescriptionText;
    [SerializeField] private TextMeshProUGUI selectedTechCostText;
    [SerializeField] private TextMeshProUGUI selectedTechPrerequisitesText;
    [SerializeField] private TextMeshProUGUI selectedTechUnlocksText;
    [SerializeField] private Button closeButton;

    private Civilization playerCiv;
    private TechData currentlySelectedTech;
    private List<TechButtonUI> techButtons = new List<TechButtonUI>(); // To manage button states

    void Start()
    {
        if (closeButton != null) 
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(() => 
            {
                if (UIManager.Instance != null)
                {
                    UIManager.Instance.HidePanel("techPanel");
                }
                else
                {
                    Debug.LogError("TechUI: UIManager.Instance is null. Cannot hide panel.");
                    if (techPanel != null) techPanel.SetActive(false); 
                }
            });
            // Ensure inspector-assigned close button is wired for UI interactions (click sound, etc.)
            if (UIManager.Instance != null)
                UIManager.Instance.WireUIInteractions(closeButton.gameObject);
        }
    }

    public void Show(Civilization civ)
    {
        playerCiv = civ;
        if (playerCiv == null)
        {
            Debug.LogError("TechUI Show called with null civ");
            return;
        }
        // Hide other panels (unit info, city, etc) when Tech UI is shown
        if (UIManager.Instance != null) {
            if (UIManager.Instance.unitInfoPanel != null)
                UIManager.Instance.unitInfoPanel.SetActive(false);
            if (UIManager.Instance.cityPanel != null)
                UIManager.Instance.cityPanel.SetActive(false);
            // Add more panels here if needed
        }
        UIManager.Instance.ShowPanel("techPanel");
        PopulateTechTree();
        ClearInfoPanel(); // Do not auto-select any tech
    }

    public void Hide()
    {
        UIManager.Instance.HidePanel("techPanel");
        // Restore other panels (unit info, city, etc) when Tech UI is closed
        if (UIManager.Instance != null) {
            if (UIManager.Instance.unitInfoPanel != null)
                UIManager.Instance.unitInfoPanel.SetActive(true);
            // Do not restore city panel unless it was open before, but for now, leave it hidden
        }
    }

    void PopulateTechTree()
    {
        // Clear existing tech nodes
        foreach (Transform child in techContent)
        {
            if (child.name.StartsWith("TechNode_") || child.name == "BackgroundContainer")
                Destroy(child.gameObject);
        }
        techButtons.Clear();

        if (TechManager.Instance == null || TechManager.Instance.allTechs == null)
        {
            Debug.LogError("TechManager or its techs not available.");
            return;
        }

        // Create background first
        CreateTechTreeBackground();

        // Create tech nodes with proper positioning
        if (useCustomLayout)
        {
            CreateTechNodesWithCustomLayout();
        }
        else
        {
            CreateTechNodesWithGridLayout();
        }

        // Create connection lines between prerequisites
        CreateConnectionLines();

        RefreshTechButtonStates();
    }

    private void CreateTechTreeBackground()
    {
        if (backgroundData == null) return;

        // Calculate total background width
        float totalWidth = backgroundData.GetTotalWidth();
        float imageHeight = 1024f * backgroundData.backgroundScale;

        // Adjust content size
        float contentWidth = Mathf.Max(totalWidth, 3000f); // Minimum width for grid
        float contentHeight = Mathf.Max(imageHeight, 1200f); // Minimum height for grid
        techContent.sizeDelta = new Vector2(contentWidth, contentHeight);

        // Create background container
        GameObject backgroundContainer = new GameObject("BackgroundContainer");
        backgroundContainer.transform.SetParent(techContent, false);

        RectTransform bgRect = backgroundContainer.AddComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0, 1);
        bgRect.anchorMax = new Vector2(0, 1);
        bgRect.pivot = new Vector2(0, 1);
        bgRect.sizeDelta = new Vector2(totalWidth, imageHeight);
        bgRect.anchoredPosition = Vector2.zero;
        bgRect.transform.SetAsFirstSibling(); // Behind everything

        // Create age backgrounds
        var allAges = System.Enum.GetValues(typeof(TechAge));
        float currentX = 0f;

        foreach (TechAge age in allAges)
        {
            Sprite ageBackground = backgroundData.GetBackgroundForAge(age);
            if (ageBackground == null) continue;

            GameObject bgImageObj = new GameObject($"Background_{age}");
            bgImageObj.transform.SetParent(backgroundContainer.transform, false);

            RectTransform imageRect = bgImageObj.AddComponent<RectTransform>();
            imageRect.anchorMin = new Vector2(0, 1);
            imageRect.anchorMax = new Vector2(0, 1);
            imageRect.pivot = new Vector2(0, 1);

            float ageWidth = backgroundData.GetWidthForAge(age);
            imageRect.sizeDelta = new Vector2(ageWidth, imageHeight);
            imageRect.anchoredPosition = new Vector2(currentX, 0);

            Image bgImage = bgImageObj.AddComponent<Image>();
            bgImage.sprite = ageBackground;
            bgImage.type = Image.Type.Simple;
            bgImage.raycastTarget = false;
            bgImage.preserveAspect = true;

            currentX += ageWidth + backgroundData.imageSpacing;
        }
    }

    private void CreateTechNodesWithGridLayout()
    {
        // Group techs by age and arrange in grid
        var techsByAge = TechManager.Instance.allTechs
            .Where(t => t != null)
            .GroupBy(t => t.techAge)
            .OrderBy(g => (int)g.Key);

        float currentAgeX = 0f;

        foreach (var ageGroup in techsByAge)
        {
            TechAge age = ageGroup.Key;
            var ageTechs = ageGroup.OrderBy(t => t.scienceCost).ToList();

            // Get the X position for this age based on background
            if (backgroundData != null)
            {
                currentAgeX = backgroundData.GetAgeStartPosition(age);
            }

            // Arrange techs in a vertical column for this age
            for (int i = 0; i < ageTechs.Count; i++)
            {
                var tech = ageTechs[i];
                float yPos = -(i * gridSpacing.y + 50f); // Start from top, go down

                Vector2 position = new Vector2(currentAgeX + 100f, yPos); // Offset from background start
                CreateTechNode(tech, position, techNodeSize);
            }

            // Move to next age position
            if (backgroundData != null)
            {
                currentAgeX += backgroundData.GetWidthForAge(age) + backgroundData.imageSpacing;
            }
            else
            {
                currentAgeX += 300f; // Default spacing
            }
        }
    }

    private void CreateTechNodesWithCustomLayout()
    {
        // Load layout from JSON file
        TechTreeLayout layout = LoadLayoutFromFile();
        if (layout == null)
        {
            Debug.LogWarning("No tech tree layout found, falling back to grid layout");
            CreateTechNodesWithGridLayout();
            return;
        }
        
        // Set content size from layout
        if (layout.techPositions != null && layout.techPositions.Count > 0)
        {
            // Calculate content size based on tech positions
            float maxX = 0f, minY = 0f;
            foreach (var pos in layout.techPositions)
            {
                maxX = Mathf.Max(maxX, pos.position.x + techNodeSize.x);
                minY = Mathf.Min(minY, pos.position.y - techNodeSize.y);
            }
            techContent.sizeDelta = new Vector2(maxX + 100f, Mathf.Abs(minY) + 100f);
        }
        
        // Create tech nodes using saved positions
        foreach (TechData tech in TechManager.Instance.allTechs)
        {
            if (tech == null) continue;
            
            // Find saved position for this tech
            Vector2 position = Vector2.zero;
            bool foundPosition = false;
            
            if (layout.techPositions != null)
            {
                foreach (var pos in layout.techPositions)
                {
                    if (pos.techName == tech.techName)
                    {
                        position = pos.position;
                        foundPosition = true;
                        break;
                    }
                }
            }
            
            if (!foundPosition)
            {
                Debug.LogWarning($"No saved position found for tech: {tech.techName}");
                continue; // Skip techs not in the saved layout
            }
            
            CreateTechNode(tech, position, techNodeSize);
        }
}

    private void CreateTechNode(TechData tech, Vector2 position, Vector2 nodeSize = default)
    {
        if (nodeSize == default)
            nodeSize = techNodeSize;
            
        GameObject techNode = new GameObject($"TechNode_{tech.techName}");
        techNode.transform.SetParent(techContent, false);

        RectTransform rect = techNode.AddComponent<RectTransform>();
        rect.sizeDelta = nodeSize;
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;

        // Add background
        Image background = techNode.AddComponent<Image>();
        background.color = GetTechStateColor(tech);

        // Add button component
        Button button = techNode.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(() => SelectTech(tech));

        // Wire UI interactions for dynamically created tech node
        if (UIManager.Instance != null)
            UIManager.Instance.WireUIInteractions(techNode);

        // Create icon
        if (tech.techIcon != null)
        {
            GameObject iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(techNode.transform, false);
            
            RectTransform iconRect = iconObj.AddComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0, 0.3f);
            iconRect.anchorMax = new Vector2(0.4f, 1f);
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;

            Image iconImage = iconObj.AddComponent<Image>();
            iconImage.sprite = tech.techIcon;
            iconImage.preserveAspect = true;
        }

        // Create text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(techNode.transform, false);
        
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.4f, 0);
        textRect.anchorMax = new Vector2(1, 1);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = tech.techName;
        text.fontSize = 10;
        text.fontSizeMin = 8;
        text.fontSizeMax = 12;
        text.enableAutoSizing = true;
        text.color = Color.white;
        text.alignment = TMPro.TextAlignmentOptions.Center;

        // Create a simple state management component
        TechButtonUI techButtonUI = techNode.AddComponent<TechButtonUI>();
        techButtonUI.Initialize(tech, this);
        techButtons.Add(techButtonUI);
    }

    private void CreateConnectionLines()
    {
        // Create lines between techs and their prerequisites
        foreach (var techButton in techButtons)
        {
            var tech = techButton.RepresentedTech;
            if (tech.requiredTechnologies != null)
            {
                foreach (var prereq in tech.requiredTechnologies)
                {
                    var prereqButton = techButtons.FirstOrDefault(tb => tb.RepresentedTech == prereq);
                    if (prereqButton != null)
                    {
                        CreateConnectionLine(prereqButton.transform, techButton.transform);
                    }
                }
            }
        }
    }

    private void CreateConnectionLine(Transform from, Transform to)
    {
        GameObject lineObj = new GameObject("ConnectionLine");
        lineObj.transform.SetParent(techContent, false);
        lineObj.transform.SetSiblingIndex(1); // Above background, below tech nodes

        RectTransform lineRect = lineObj.AddComponent<RectTransform>();
        Image lineImage = lineObj.AddComponent<Image>();
        lineImage.color = new Color(0.5f, 0.8f, 0.5f, 0.7f); // Semi-transparent green
        lineImage.raycastTarget = false;

        // Position line between the two nodes
        Vector2 fromPos = from.GetComponent<RectTransform>().anchoredPosition;
        Vector2 toPos = to.GetComponent<RectTransform>().anchoredPosition;
        
        Vector2 direction = (toPos - fromPos).normalized;
        float distance = Vector2.Distance(fromPos, toPos);
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        lineRect.sizeDelta = new Vector2(distance, 3f);
        lineRect.anchorMin = new Vector2(0, 1);
        lineRect.anchorMax = new Vector2(0, 1);
        lineRect.pivot = new Vector2(0, 0.5f);
        lineRect.anchoredPosition = fromPos;
        lineRect.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
    }

    private Color GetTechStateColor(TechData tech)
    {
        if (playerCiv == null) return Color.gray;

        if (playerCiv.researchedTechs.Contains(tech))
            return Color.green;
        else if (playerCiv.currentTech == tech)
            return Color.yellow;
        else if (playerCiv.CanResearch(tech))
            return Color.white;
        else
            return Color.gray;
    }

    public void SelectTech(TechData tech)
    {
        currentlySelectedTech = tech;
        UpdateInfoPanel(tech);
if (playerCiv != null && playerCiv.CanResearch(tech))
        {
playerCiv.StartResearch(tech);
            RefreshUI();
        }
        else
        {
}

        foreach (var btnUI in techButtons)
        {
            btnUI.SetSelected(tech == btnUI.RepresentedTech);
        }
    }

    void UpdateInfoPanel(TechData tech)
    {
        if (tech == null)
        {
            ClearInfoPanel();
            return;
        }

        selectedTechNameText.text = tech.techName;
        selectedTechDescriptionText.text = tech.description;
        selectedTechCostText.text = $"Cost: {tech.scienceCost} Science";

        string prereqs = "Prerequisites: ";
        if (tech.requiredTechnologies != null && tech.requiredTechnologies.Length > 0)
        {
            prereqs += string.Join(", ", tech.requiredTechnologies.Select(t => t.techName));
        }
        else
        {
            prereqs += "None";
        }
        selectedTechPrerequisitesText.text = prereqs;

        string unlocks = "Unlocks: ";
        List<string> unlockItems = new List<string>();
        // REMOVED: TechData no longer directly unlocks units/buildings
        // Availability is now controlled solely by requiredTechs in the respective data classes
        // Add other unlock types here (policies, governments, etc.)
        if (unlockItems.Count > 0)
        {
            unlocks += string.Join(", ", unlockItems);
        }
        else
        {
            unlocks += "Nothing yet";
        }
        selectedTechUnlocksText.text = unlocks;
    }
    
    void ClearInfoPanel()
    {
        selectedTechNameText.text = "Select a Technology";
        selectedTechDescriptionText.text = "";
        selectedTechCostText.text = "";
        selectedTechPrerequisitesText.text = "";
        selectedTechUnlocksText.text = "";
    }

    public void RefreshUI()
    {
if (playerCiv == null) return;
        // Update button states
        RefreshTechButtonStates();
        // Update info panel for the currently selected tech or current research
        if (playerCiv.currentTech != null)
        {
            UpdateInfoPanel(playerCiv.currentTech);
             foreach (var btnUI in techButtons)
            {
                btnUI.SetSelected(playerCiv.currentTech == btnUI.RepresentedTech);
            }
        }
        else if (currentlySelectedTech != null)
        {
            UpdateInfoPanel(currentlySelectedTech);
        } else {
            ClearInfoPanel();
        }
    }

    private void UpdateTechButtonState(TechButtonUI buttonUI, TechData tech)
    {
        if (playerCiv == null) return;
        
        // Update the visual state based on research status
        Image background = buttonUI.GetComponent<Image>();
        if (background != null)
        {
            background.color = GetTechStateColor(tech);
        }

        if (playerCiv.researchedTechs.Contains(tech))
        {
            buttonUI.SetState(TechButtonUI.TechState.Researched);
        }
        else if (playerCiv.currentTech == tech)
        {
            buttonUI.SetState(TechButtonUI.TechState.Researching);
        }
        else if (playerCiv.CanResearch(tech))
        {
            buttonUI.SetState(TechButtonUI.TechState.Available);
        }
        else
        {
            buttonUI.SetState(TechButtonUI.TechState.Locked);
        }
    }
    
    public void RefreshTechButtonStates()
    {
        foreach (var btnUI in techButtons)
        {
            UpdateTechButtonState(btnUI, btnUI.RepresentedTech);
        }
    }
    
    private TechTreeLayout LoadLayoutFromFile()
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
            Debug.LogWarning($"Tech tree layout file not found at: {filePath}");
            return null;
        }
        
        try
        {
            string json = System.IO.File.ReadAllText(filePath);
            TechTreeLayout layout = JsonUtility.FromJson<TechTreeLayout>(json);
return layout;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to load tech tree layout: {e.Message}");
            return null;
        }
    }
}

// Helper script for the TechButton prefab (TechButtonUI.cs)
// You would create this script and attach it to your techButtonPrefab
/*
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TechButtonUI : MonoBehaviour
{
    public TechData RepresentedTech { get; private set; }
    private TechUI techUI; // Reference to the main TechUI

    [SerializeField] private TextMeshProUGUI techNameText;
    [SerializeField] private Image iconImage; // Optional: if techs have icons
    [SerializeField] private Image backgroundImage; // To change color based on state

    [SerializeField] private Color researchedColor = Color.green;
    [SerializeField] private Color researchingColor = Color.yellow;
    [SerializeField] private Color availableColor = Color.white;
    [SerializeField] private Color lockedColor = Color.gray;
    [SerializeField] private Color selectedColor = Color.cyan; // For when this tech is selected in the info panel

    private Button button;
    private bool isSelected = false;


    public enum TechState { Available, Researched, Researching, Locked }
    private TechState currentState;

    public void Initialize(TechData tech, TechUI ownerUI)
    {
        RepresentedTech = tech;
        techUI = ownerUI;
        techNameText.text = tech.techName;
        // if (iconImage != null && tech.icon != null) iconImage.sprite = tech.icon;

        button = GetComponent<Button>();
        button.onClick.AddListener(() => techUI.SelectTech(RepresentedTech));
    }

    public void SetState(TechState state)
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
                case TechState.Researched:
                    backgroundImage.color = researchedColor;
                    break;
                case TechState.Researching:
                    backgroundImage.color = researchingColor;
                    break;
                case TechState.Available:
                    backgroundImage.color = availableColor;
                    break;
                case TechState.Locked:
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

/*
*/ 