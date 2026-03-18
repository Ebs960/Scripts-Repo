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
    [SerializeField] private TextAsset layoutJson; // Assign this in the inspector
    [SerializeField] private Vector2 techNodeSize = new Vector2(180, 90); // Size of each tech node
    [SerializeField] private Vector2 gridSpacing = new Vector2(200, 100); // Spacing between nodes

    [Header("Info Panel")]
    [SerializeField] private TextMeshProUGUI selectedTechNameText;
    [SerializeField] private TextMeshProUGUI selectedTechDescriptionText;
    [SerializeField] private TextMeshProUGUI selectedTechCostText;
    [SerializeField] private TextMeshProUGUI selectedTechTurnsRemainingText;
    [SerializeField] private TextMeshProUGUI selectedTechPrerequisitesText;
    [SerializeField] private TextMeshProUGUI selectedTechUnlocksText;
    [SerializeField] private TextMeshProUGUI selectedTechBuildingsText;
    [SerializeField] private TextMeshProUGUI selectedTechImprovementsText;
    [SerializeField] private UnityEngine.UI.Image selectedTechIconImage;
    [SerializeField] private Button closeButton;

    private Civilization playerCiv;
    private TechData currentlySelectedTech;
    private List<TechButtonUI> techButtons = new List<TechButtonUI>(); // To manage button states
    
    // Build-once caching to avoid Destroy/Recreate churn every time the panel opens.
    private bool _treeBuilt = false;
    private int _builtTechCount = -1;
    private bool _builtUsedCustomLayout = false;

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
        // Do not auto-select any technology when opening the UI; start with a blank info panel
        currentlySelectedTech = null;
        ClearInfoPanel();
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
        if (TechManager.Instance == null || TechManager.Instance.allTechs == null)
        {
            Debug.LogError("TechManager or its techs not available.");
            return;
        }

        int currentTechCount = TechManager.Instance.allTechs.Count;
        bool needsRebuild =
            !_treeBuilt ||
            techButtons == null || techButtons.Count == 0 ||
            _builtTechCount != currentTechCount ||
            _builtUsedCustomLayout != useCustomLayout;

        if (!needsRebuild)
        {
            // Tree already exists: only refresh state (locked/available/researched) and info panel.
            RefreshTechButtonStates();
            return;
        }

        // Rebuild path (rare): clear old nodes/lines/background.
        if (techContent != null)
        {
            foreach (Transform child in techContent)
            {
                if (child != null) Destroy(child.gameObject);
            }
        }
        techButtons.Clear();

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

        _treeBuilt = true;
        _builtTechCount = currentTechCount;
        _builtUsedCustomLayout = useCustomLayout;
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
        Debug.Log("[TechUI] Attempting to load custom tech tree layout from JSON...");
        TechTreeLayout layout = LoadLayoutFromFile();
        if (layout == null)
        {
            Debug.LogWarning("[TechUI] No tech tree layout found, falling back to grid layout");
            CreateTechNodesWithGridLayout();
            return;
        }

        Debug.Log($"[TechUI] Loaded layout with {layout.techPositions?.Count ?? 0} tech positions.");

        var layoutPositions = new Dictionary<string, Vector2>(System.StringComparer.OrdinalIgnoreCase);
        if (layout.techPositions != null)
        {
            foreach (var pos in layout.techPositions)
            {
                if (pos == null || string.IsNullOrWhiteSpace(pos.techName))
                    continue;

                layoutPositions[pos.techName] = pos.position;
            }
        }

        var resolvedPositions = new Dictionary<TechData, Vector2>();
        var fallbackRowsByAge = new Dictionary<TechAge, int>();

        foreach (TechData tech in TechManager.Instance.allTechs)
        {
            if (tech == null) continue;

            Vector2 position;
            bool foundPosition =
                layoutPositions.TryGetValue(tech.name, out position) ||
                (!string.IsNullOrWhiteSpace(tech.techName) && layoutPositions.TryGetValue(tech.techName, out position));

            if (foundPosition)
            {
                Debug.Log($"[TechUI] Found position for tech '{tech.name}': {position}");
                resolvedPositions[tech] = position;
                continue;
            }

            bool placedFromPrereqs = false;
            if (tech.requiredTechnologies != null && tech.requiredTechnologies.Length > 0)
            {
                float maxPrereqX = float.NegativeInfinity;
                float avgPrereqY = 0f;
                int prereqCount = 0;

                foreach (var prereq in tech.requiredTechnologies)
                {
                    if (prereq == null || !resolvedPositions.TryGetValue(prereq, out Vector2 prereqPos))
                        continue;

                    maxPrereqX = Mathf.Max(maxPrereqX, prereqPos.x);
                    avgPrereqY += prereqPos.y;
                    prereqCount++;
                }

                if (prereqCount > 0)
                {
                    position = new Vector2(maxPrereqX + gridSpacing.x, avgPrereqY / prereqCount);
                    placedFromPrereqs = true;
                }
            }

            if (!placedFromPrereqs)
            {
                int row = fallbackRowsByAge.TryGetValue(tech.techAge, out int existingRow) ? existingRow : 0;
                float ageStartX = backgroundData != null
                    ? backgroundData.GetAgeStartPosition(tech.techAge) + 100f
                    : 100f + ((int)tech.techAge * 300f);
                position = new Vector2(ageStartX, -(row * gridSpacing.y + 50f));
                fallbackRowsByAge[tech.techAge] = row + 1;
            }

            Debug.LogWarning($"[TechUI] No saved position found for tech '{tech.name}'. Using fallback position {position}.");
            resolvedPositions[tech] = position;
        }

        if (resolvedPositions.Count == 0)
        {
            Debug.LogWarning("[TechUI] Custom layout resolved no tech positions, falling back to grid layout");
            CreateTechNodesWithGridLayout();
            return;
        }

        float maxX = 0f;
        float minY = 0f;
        foreach (var kvp in resolvedPositions)
        {
            Vector2 pos = kvp.Value;
            maxX = Mathf.Max(maxX, pos.x + techNodeSize.x);
            minY = Mathf.Min(minY, pos.y - techNodeSize.y);
        }
        techContent.sizeDelta = new Vector2(maxX + 100f, Mathf.Abs(minY) + 100f);
        Debug.Log($"[TechUI] Set techContent size to {techContent.sizeDelta}");

        foreach (var kvp in resolvedPositions)
            CreateTechNode(kvp.Key, kvp.Value, techNodeSize);
    }

    private void CreateTechNode(TechData tech, Vector2 position, Vector2 nodeSize = default)
    {
        if (nodeSize == default)
            nodeSize = techNodeSize;

        // Instantiate the assigned prefab

        Debug.Log($"[TechUI] Creating tech node for '{tech.name}' at position {position}");
        GameObject techNode = Instantiate(techButtonPrefab, techContent);
        techNode.name = $"TechNode_{tech.techName}";
        RectTransform rect = techNode.GetComponent<RectTransform>();
        if (rect == null) rect = techNode.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;

        // Initialize the TechButtonUI component
        TechButtonUI techButtonUI = techNode.GetComponent<TechButtonUI>();
        if (techButtonUI == null) techButtonUI = techNode.AddComponent<TechButtonUI>();
        techButtonUI.Initialize(tech, this);
        techButtons.Add(techButtonUI);

        // Wire UI interactions for dynamically created tech node
        if (UIManager.Instance != null)
            UIManager.Instance.WireUIInteractions(techNode);
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

    /// <summary>
    /// Update the info panel and highlight the button without starting research.
    /// </summary>
    private void SelectTechInfoOnly(TechData tech)
    {
        currentlySelectedTech = tech;
        UpdateInfoPanel(tech);
        foreach (var btnUI in techButtons)
            btnUI.SetSelected(tech == btnUI.RepresentedTech);
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
        // Set icon if assigned
        if (selectedTechIconImage != null)
        {
            selectedTechIconImage.gameObject.SetActive(tech.techIcon != null);
            selectedTechIconImage.sprite = tech.techIcon;
        }

        selectedTechNameText.text = tech.techName;
        selectedTechDescriptionText.text = tech.description;
        // Calculate turns remaining
        int sciPerTurn = GetTotalSciencePerTurn(playerCiv);
        float remaining = tech.scienceCost;
        if (playerCiv != null && playerCiv.currentTech == tech)
            remaining = tech.scienceCost - playerCiv.currentTechProgress;
        selectedTechCostText.text = $"Cost: {tech.scienceCost} Science";
        if (selectedTechTurnsRemainingText != null)
            selectedTechTurnsRemainingText.text = sciPerTurn > 0 ? $"~{Mathf.CeilToInt(remaining / sciPerTurn)} turns" : "";

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

        List<string> buildingUnlocks = new List<string>();
        List<string> improvementUnlocks = new List<string>();
        List<string> unlockItems = new List<string>();

        // Reverse-lookup: find all data assets that require this tech
        var buildings = ResourceCache.GetAllBuildings();
        if (buildings != null)
            foreach (var b in buildings)
                if (b != null && b.requiredTechs != null)
                    foreach (var rt in b.requiredTechs)
                        if (rt == tech)
                        {
                            AddUniqueUnlock(buildingUnlocks, b.buildingName);
                            AddUniqueUnlock(unlockItems, b.buildingName);
                            break;
                        }

        var combatUnits = ResourceCache.GetAllCombatUnits();
        if (combatUnits != null)
            foreach (var u in combatUnits)
                if (u != null && u.requiredTechs != null)
                    foreach (var rt in u.requiredTechs)
                        if (rt == tech) { AddUniqueUnlock(unlockItems, u.unitName); break; }

        var workerUnits = ResourceCache.GetAllWorkerUnits();
        if (workerUnits != null)
            foreach (var w in workerUnits)
                if (w != null && w.requiredTechs != null)
                    foreach (var rt in w.requiredTechs)
                        if (rt == tech) { AddUniqueUnlock(unlockItems, w.unitName); break; }

        var improvements = ResourceCache.GetAllImprovements();
        if (improvements != null)
            foreach (var imp in improvements)
                if (imp != null)
                {
                    if (imp.requiredTechs != null)
                    {
                        foreach (var rt in imp.requiredTechs)
                        {
                            if (rt == tech)
                            {
                                AddUniqueUnlock(improvementUnlocks, imp.improvementName);
                                AddUniqueUnlock(unlockItems, imp.improvementName);
                                break;
                            }
                        }
                    }

                    if (imp.availableUpgrades != null)
                    {
                        foreach (var upgrade in imp.availableUpgrades)
                        {
                            if (upgrade == null || upgrade.requiredTech != tech)
                                continue;

                            string upgradeLabel = string.IsNullOrWhiteSpace(upgrade.upgradeName)
                                ? $"{imp.improvementName} Upgrade"
                                : $"{imp.improvementName}: {upgrade.upgradeName}";
                            AddUniqueUnlock(improvementUnlocks, upgradeLabel);
                            AddUniqueUnlock(unlockItems, upgradeLabel);
                        }
                    }
                }

        var equipment = ResourceCache.GetAllEquipment();
        if (equipment != null)
            foreach (var eq in equipment)
                if (eq != null && eq.requiredTechs != null)
                    foreach (var rt in eq.requiredTechs)
                        if (rt == tech) { AddUniqueUnlock(unlockItems, eq.equipmentName); break; }

        // Also show directly-referenced unlocks on TechData itself
        if (tech.unlockedGovernments != null)
            foreach (var g in tech.unlockedGovernments)
                if (g != null) AddUniqueUnlock(unlockItems, g.governmentName);
        if (tech.unlockedReligions != null)
            foreach (var r in tech.unlockedReligions)
                if (r != null) AddUniqueUnlock(unlockItems, r.religionName);
        if (tech.unlocksReligion)
            AddUniqueUnlock(unlockItems, "Religion Mechanics");

        if (selectedTechBuildingsText != null)
            selectedTechBuildingsText.text = FormatUnlockField("Buildings", buildingUnlocks);
        if (selectedTechImprovementsText != null)
            selectedTechImprovementsText.text = FormatUnlockField("Improvements", improvementUnlocks);

        selectedTechUnlocksText.text = FormatUnlockField("Unlocks", unlockItems);
    }

    private int GetTotalSciencePerTurn(Civilization civ)
    {
        if (civ == null) return 0;
        int total = 0;
        if (civ.cities != null)
            foreach (var city in civ.cities)
                if (city != null) total += city.GetSciencePerTurn();
        if (civ.combatUnits != null)
            foreach (var u in civ.combatUnits)
                if (u != null && u.data != null) total += civ.ComputeUnitPerTurnYield(u.data, u.Weapon, u.Shield, u.Armor, u.Miscellaneous).science;
        if (civ.workerUnits != null)
            foreach (var w in civ.workerUnits)
                if (w != null && w.data != null) total += civ.ComputeWorkerPerTurnYield(w.data).science;
        return total;
    }
    
    void ClearInfoPanel()
    {
        selectedTechNameText.text = "Select a Technology";
        selectedTechDescriptionText.text = "";
        selectedTechCostText.text = "";
        if (selectedTechTurnsRemainingText != null) selectedTechTurnsRemainingText.text = "";
        selectedTechPrerequisitesText.text = "";
        selectedTechUnlocksText.text = "";
        if (selectedTechBuildingsText != null) selectedTechBuildingsText.text = "";
        if (selectedTechImprovementsText != null) selectedTechImprovementsText.text = "";
    }

    private static void AddUniqueUnlock(List<string> list, string value)
    {
        if (list == null || string.IsNullOrWhiteSpace(value) || list.Contains(value))
            return;

        list.Add(value);
    }

    private static string FormatUnlockField(string label, List<string> items)
    {
        if (items == null || items.Count == 0)
            return $"{label}: None";

        return $"{label}: {string.Join(", ", items)}";
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
        if (layoutJson == null)
        {
            Debug.LogWarning("[TechUI] Tech tree layout TextAsset not assigned!");
            return null;
        }
        Debug.Log("[TechUI] Loaded JSON: " + layoutJson.text);
        try
        {
            TechTreeLayout layout = JsonUtility.FromJson<TechTreeLayout>(layoutJson.text);
            Debug.Log("[TechUI] Deserialized layout: " + (layout == null ? "NULL" : "OK"));
            if (layout != null && layout.techPositions != null)
            {
                Debug.Log("[TechUI] JSON tech names: " + string.Join(", ", layout.techPositions.Select(p => p.techName)));
            }
            if (TechManager.Instance != null && TechManager.Instance.allTechs != null)
            {
                Debug.Log("[TechUI] Asset tech names: " + string.Join(", ", TechManager.Instance.allTechs.Select(t => t.name)));
            }
            return layout;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[TechUI] Failed to load tech tree layout: {e.Message}");
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