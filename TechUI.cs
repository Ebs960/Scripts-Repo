using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.InputSystem;

public class TechUI : MonoBehaviour
{
    [Header("Panel References")]
    [SerializeField] private GameObject techPanel;
    [SerializeField] private ScrollRect techScrollRect; // The ScrollRect for the tech tree
    [SerializeField] private RectTransform techContent; // The content area of the ScrollRect
    [SerializeField] private GameObject techButtonPrefab; // Prefab for tech buttons
    [Header("Shared Presentation")]
    [SerializeField] private TreePanelView panelView;
    [SerializeField] private TreeNavigation navigation;
    [SerializeField] private bool enableNavigation = true;
    [SerializeField, Min(0)] private float edgePadding = 80f;
    [SerializeField] private bool useFloatingPopups = true;
    [Tooltip("Optional shared scene popup or popup prefab. A runtime popup is created when unassigned.")]
    [SerializeField] private ResearchHoverPopup hoverPopup;
    [Header("Custom Tree Artwork")]
    [Tooltip("Tech/Culture Main Shell: transparent central opening with blue header and gold frame.")]
    [SerializeField] private Sprite mainShellSprite;
    [Tooltip("Tech/Culture Node Button: shared blue/gold background without text or icons.")]
    [SerializeField] private Sprite nodeButtonSprite;
    [Tooltip("Research Progress Bar Background: new blue/gold artwork, not the parchment HUD bar.")]
    [SerializeField] private Sprite researchProgressSprite;
    [Tooltip("Technology/Culture Hover Popup Background: tall blue/gold panel with title divider.")]
    [SerializeField] private Sprite hoverPopupSprite;
    [SerializeField] private GameObject[] legacyInformationPanels;
    public Sprite NodeButtonSprite => nodeButtonSprite;

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
    [Tooltip("Icon displayed when no technology is currently being researched. Assign a question mark or hourglass sprite.")]
    [SerializeField] private Sprite noResearchIcon;
    [SerializeField] private Button closeButton;

    [Header("Economic Impact Preview")]
    [SerializeField] private TextMeshProUGUI selectedTechEconomicImpactText;

    private Civilization playerCiv;
    private TechData currentlySelectedTech;
    private TechData displayedTech;
    private List<TechButtonUI> techButtons = new List<TechButtonUI>(); // To manage button states
    
    // Build-once caching to avoid Destroy/Recreate churn every time the panel opens.
    private bool _treeBuilt = false;
    private int _builtTechCount = -1;
    private bool _builtUsedCustomLayout = false;
    private TechData observedTech;
    private float observedProgress;
    private int observedCompletedCount;
    private float nextHeaderRefresh;
    private bool followsPlayerCivilization;

    void Start()
    {
        if (closeButton != null) 
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(() => 
            {
                ClosePopup();
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
        if (playerCiv != civ) currentlySelectedTech = null;
        playerCiv = civ;
        followsPlayerCivilization = CivilizationManager.Instance != null && CivilizationManager.Instance.playerCiv == civ;
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
        if (UIManager.Instance != null) UIManager.Instance.ShowPanel("techPanel");
        else if (techPanel != null) techPanel.SetActive(true);
        ConfigurePresentation();
        PopulateTechTree();
        // Preserve any prior explicit selection when reopening the UI.
        // RefreshUI will reapply selection for `playerCiv.currentTech` or `currentlySelectedTech` if set.
        RefreshUI();
    }

    public void Hide()
    {
        ClosePopup();
        if (UIManager.Instance != null) UIManager.Instance.HidePanel("techPanel");
        else if (techPanel != null) techPanel.SetActive(false);
        // Restore other panels (unit info, city, etc) when Tech UI is closed
        if (UIManager.Instance != null) {
            if (UIManager.Instance.unitInfoPanel != null)
                UIManager.Instance.unitInfoPanel.SetActive(true);
            // Do not restore city panel unless it was open before, but for now, leave it hidden
        }
    }

    void PopulateTechTree()
    {
        if (techContent == null || techButtonPrefab == null) return;
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
                if (child != null) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            }
        }
        techButtons.Clear();
        TreeContentBounds.ResetOrigin(techContent);

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
        TreeContentBounds.Fit(techContent, new Vector2(3000, 1200), edgePadding);

        RefreshTechButtonStates();

        _treeBuilt = true;
        _builtTechCount = currentTechCount;
        _builtUsedCustomLayout = useCustomLayout;
    }

    private void CreateTechTreeBackground()
    {
        if (backgroundData != null) TreePanorama.Render(techContent, backgroundData.GetPlacements(), backgroundData.showEraMarkers);
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

        foreach (var kvp in resolvedPositions)
            CreateTechNode(kvp.Key, kvp.Value, techNodeSize);
    }

    private void CreateTechNode(TechData tech, Vector2 position, Vector2 nodeSize = default)
    {
        if (tech == null || techButtonPrefab == null || techContent == null) return;
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
        rect.sizeDelta = nodeSize;
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
        lineImage.color = new Color(0.55f, 0.49f, 0.33f, 0.75f);
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

        if (playerCiv.researchedTechs != null && playerCiv.researchedTechs.Contains(tech))
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

    public void PreviewTech(TechData tech)
    {
        SelectTechInfoOnly(tech);
        var button = techButtons.FirstOrDefault(item => item != null && item.RepresentedTech == tech);
        if (button != null) PinPopup(tech, button.transform as RectTransform);
    }

    public bool ShowHoverPopup(TechData tech, RectTransform anchor)
    {
        if (!useFloatingPopups || tech == null || anchor == null) return false;
        hoverPopup = ResearchHoverPopup.GetOrCreate(this, hoverPopup);
        if (hoverPopup == null) return false;
        hoverPopup.Request(this, anchor, () => BuildPopupInfo(tech));
        return true;
    }

    public void PinPopup(TechData tech, RectTransform anchor, bool fromNavigation = false)
    {
        if (!useFloatingPopups || tech == null || anchor == null) return;
        hoverPopup = ResearchHoverPopup.GetOrCreate(this, hoverPopup);
        if (hoverPopup != null && (!fromNavigation || hoverPopup.pinOnNavigationSelection))
            hoverPopup.Request(this, anchor, () => BuildPopupInfo(tech), true);
    }

    public void LeaveHoverPopup(RectTransform anchor)
    {
        if (hoverPopup != null) hoverPopup.Leave(this, anchor);
    }

    public void ClosePopup() { if (hoverPopup != null) hoverPopup.HideForOwner(this); }
    private void OnDisable() { ClosePopup(); }

    private void LateUpdate()
    {
        if (techPanel != null && !techPanel.activeInHierarchy) return;
        var currentPlayer = CivilizationManager.Instance != null ? CivilizationManager.Instance.playerCiv : null;
        if (followsPlayerCivilization && currentPlayer != null && currentPlayer != playerCiv) { Show(currentPlayer); return; }
        if (playerCiv == null || panelView == null) return;
        if (observedTech != playerCiv.currentTech || observedProgress != playerCiv.currentTechProgress ||
            observedCompletedCount != (playerCiv.researchedTechs != null ? playerCiv.researchedTechs.Count : 0))
            RefreshUI();
        else if (Time.unscaledTime >= nextHeaderRefresh) UpdateHeader();
    }

    private void UpdateHeader()
    {
        if (panelView == null) return;
        panelView.UpdateFocus(playerCiv != null && playerCiv.currentTech != null ? playerCiv.currentTech.techName : "No Active Research",
            playerCiv != null ? playerCiv.currentTechProgress : 0, playerCiv != null && playerCiv.currentTech != null ? playerCiv.currentTech.scienceCost : 0,
            GetTotalSciencePerTurn(playerCiv), playerCiv != null && playerCiv.currentTech != null);
        nextHeaderRefresh = Time.unscaledTime + 0.25f;
    }

    private void ConfigurePresentation()
    {
        if (enableNavigation && techScrollRect != null)
        {
            if (navigation == null) navigation = techScrollRect.GetComponent<TreeNavigation>();
            if (navigation != null) navigation.Configure(techScrollRect);
        }
        if (panelView == null) panelView = TreePanelView.GetOrCreateShell(this, techPanel, techScrollRect, closeButton);
        else panelView.PrepareShell(this, techPanel, techScrollRect, closeButton);
        if (panelView != null)
        {
            if (mainShellSprite != null) panelView.mainShellSprite = mainShellSprite;
            if (researchProgressSprite != null) panelView.researchProgressSprite = researchProgressSprite;
            if (legacyInformationPanels != null && legacyInformationPanels.Length > 0) panelView.legacyInformationPanels = legacyInformationPanels;
            panelView.Configure("TECHNOLOGY TREE", navigation, ResearchSelectedTech, FocusCurrentResearch, Hide);
            if (useFloatingPopups) panelView.HideLegacyDetails(selectedTechNameText, selectedTechDescriptionText,
                selectedTechCostText, selectedTechTurnsRemainingText, selectedTechPrerequisitesText, selectedTechUnlocksText,
                selectedTechBuildingsText, selectedTechImprovementsText, selectedTechIconImage, selectedTechEconomicImpactText);
        }
            hoverPopup = ResearchHoverPopup.GetOrCreate(this, hoverPopup);
            if (hoverPopup != null && hoverPopupSprite != null) hoverPopup.backgroundSprite = hoverPopupSprite;
    }

    public void ResearchSelectedTech()
    {
        if (displayedTech != null && playerCiv != null && playerCiv.currentTech != displayedTech && playerCiv.CanResearch(displayedTech))
            SelectTech(displayedTech);
    }

    public void FocusCurrentResearch()
    {
        if (playerCiv == null || playerCiv.currentTech == null) return;
        var button = techButtons.FirstOrDefault(item => item != null && item.RepresentedTech == playerCiv?.currentTech);
        if (navigation != null && button != null) navigation.Focus(button.transform as RectTransform);
    }

    public void ResetView() { if (navigation != null) navigation.ResetView(); }

    [ContextMenu("Rebuild Tree")]
    public void RebuildTree()
    {
        ClosePopup();
        _treeBuilt = false;
        if (playerCiv != null) { PopulateTechTree(); RefreshUI(); }
    }

    public void FocusAge(int ageIndex)
    {
        if (navigation == null || backgroundData == null || techContent == null) return;
        Vector2 point = TreeContentBounds.ToContentPosition(techContent,
            new Vector2(backgroundData.GetAgeStartPosition((TechAge)ageIndex), -backgroundData.displayHeight * backgroundData.backgroundScale * 0.5f));
        navigation.FocusPoint(techContent.TransformPoint(point));
    }

    public void SelectTech(TechData tech)
    {
        if (tech == null) return;
        currentlySelectedTech = tech;
        UpdateInfoPanel(tech);
        if (playerCiv != null)
        {
            bool queueRequested = IsQueueModifierPressed();
            if (queueRequested)
            {
                if (!playerCiv.QueueResearch(tech))
                    playerCiv.StartResearchWithDependencies(tech, true);
            }
            else if (!playerCiv.StartResearchWithDependencies(tech, false))
            {
                playerCiv.StartResearch(tech);
            }
            RefreshUI();
        }

        foreach (var btnUI in techButtons)
        {
            btnUI.SetSelected(tech == btnUI.RepresentedTech);
        }
    }

    private bool IsQueueModifierPressed()
    {
        return Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
    }

    void UpdateInfoPanel(TechData tech)
    {
        displayedTech = tech;
        if (useFloatingPopups) return;
        if (tech == null)
        {
            ClearInfoPanel();
            return;
        }
        ResearchPopupInfo info = BuildPopupInfo(tech);
        if (selectedTechIconImage != null)
        {
            selectedTechIconImage.gameObject.SetActive(tech.techIcon != null);
            selectedTechIconImage.sprite = tech.techIcon;
        }
        TreePanelView.SetText(selectedTechNameText, info.name);
        TreePanelView.SetText(selectedTechDescriptionText, info.description);
        TreePanelView.SetText(selectedTechCostText, $"Cost: {info.cost:0} {info.currency}");
        TreePanelView.SetText(selectedTechTurnsRemainingText, info.turnsRemaining.HasValue ? $"~{info.turnsRemaining} turns" : "");
        TreePanelView.SetText(selectedTechPrerequisitesText, info.prerequisites);
        TreePanelView.SetText(selectedTechBuildingsText, info.buildings);
        TreePanelView.SetText(selectedTechImprovementsText, info.improvements);
        TreePanelView.SetText(selectedTechUnlocksText, info.units + (string.IsNullOrEmpty(info.otherUnlocks) ? "" : "\n" + info.otherUnlocks) + (string.IsNullOrEmpty(info.yields) ? "" : "\n" + info.yields));
        TreePanelView.SetText(selectedTechEconomicImpactText, info.economicEffects);
        if (panelView != null) panelView.UpdateSelection(info.progress, info.cost, info.canStart, info.actionLabel);
    }

    public ResearchPopupInfo BuildPopupInfo(TechData tech)
    {
        if (tech == null) return null;
        bool completed = playerCiv?.researchedTechs != null && playerCiv.researchedTechs.Contains(tech);
        bool active = playerCiv != null && playerCiv.currentTech == tech;
        bool available = !completed && playerCiv != null && playerCiv.CanResearch(tech);
        var info = ResearchPopupInfo.Create(tech.techName, tech.techIcon, tech.techAge,
            completed ? ResearchPopupStatus.Completed : active ? ResearchPopupStatus.Active : available ? ResearchPopupStatus.Available : ResearchPopupStatus.Locked,
            tech.scienceCost, active ? playerCiv.currentTechProgress : 0, GetTotalSciencePerTurn(playerCiv), "Science");
        info.description = tech.description;
        info.canStart = available && !active;
        info.actionLabel = completed ? "Completed" : active ? "Researching" : "Research";
        var prereqItems = new List<string>();
        if (tech.requiredTechnologies != null && tech.requiredTechnologies.Length > 0)
            foreach (var prerequisite in tech.requiredTechnologies)
                if (prerequisite != null) AddUniqueUnlock(prereqItems, prerequisite.techName);
        if (tech.requiredCityCount > 0) prereqItems.Add($"{tech.requiredCityCount} cities");
        if (tech.requiredControlledBiomes != null)
            foreach (var biome in tech.requiredControlledBiomes) AddUniqueUnlock(prereqItems, "Control " + biome);
        info.prerequisites = ResearchPopupInfo.JoinRelevant(prereqItems);

        List<string> buildingUnlocks = new List<string>();
        List<string> improvementUnlocks = new List<string>();
        List<string> unitUnlocks = new List<string>();
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
                        if (rt == tech) { AddUniqueUnlock(unitUnlocks, u.unitName); break; }

        var workerUnits = ResourceCache.GetAllWorkerUnits();
        if (workerUnits != null)
            foreach (var w in workerUnits)
                if (w != null && w.requiredTechs != null)
                    foreach (var rt in w.requiredTechs)
                        if (rt == tech) { AddUniqueUnlock(unitUnlocks, w.unitName); break; }

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

        var governments = ResourceCache.GetAllGovernmentData();
        if (governments != null)
            foreach (var g in governments)
                if (g != null && g.requiredTechs != null && System.Array.IndexOf(g.requiredTechs, tech) >= 0)
                    AddUniqueUnlock(unlockItems, g.governmentName);

        // Also show directly-referenced unlocks on TechData itself
        if (tech.unlockedReligions != null)
            foreach (var r in tech.unlockedReligions)
                if (r != null) AddUniqueUnlock(unlockItems, r.religionName);
        if (tech.unlocksReligion)
            AddUniqueUnlock(unlockItems, "Religion Mechanics");

        info.buildings = ResearchPopupInfo.JoinRelevant(buildingUnlocks);
        info.improvements = ResearchPopupInfo.JoinRelevant(improvementUnlocks);
        info.units = ResearchPopupInfo.JoinRelevant(unitUnlocks);
        info.otherUnlocks = ResearchPopupInfo.JoinRelevant(unlockItems.Except(buildingUnlocks).Except(improvementUnlocks).ToList());
        info.yields = FormatYieldInfo(tech);
        info.bonuses = ResearchPopupInfo.FormatBonuses(tech.attackBonus, tech.defenseBonus, tech.movementBonus);
        info.economicEffects = BuildEconomicImpact(tech);
        return info;
    }

    private string FormatYieldInfo(TechData tech)
    {
        if (tech == null) return string.Empty;
        var parts = new System.Collections.Generic.List<string>();
        void AddPct(float val, string label)
        {
            if (Mathf.Approximately(val, 0f)) return;
            string sign = val > 0 ? "+" : "";
            parts.Add($"{sign}{(val * 100f):0.#}% {label}");
        }
        void AddFlat(int val, string label)
        {
            if (val == 0) return;
            string sign = val > 0 ? "+" : "";
            parts.Add($"{sign}{val} {label}");
        }

        AddPct(tech.foodModifier, "Food");
        AddFlat(tech.flatFoodBonus, "Food");
        AddPct(tech.productionModifier, "Production");
        AddFlat(tech.flatProductionBonus, "Production");
        AddPct(tech.goldModifier, "Gold");
        AddFlat(tech.flatGoldBonus, "Gold");
        AddPct(tech.scienceModifier, "Science");
        AddFlat(tech.flatScienceBonus, "Science");
        AddPct(tech.cultureModifier, "Culture");
        AddFlat(tech.flatCultureBonus, "Culture");
        AddPct(tech.faithModifier, "Faith");
        AddFlat(tech.flatFaithBonus, "Faith");
        AddPct(tech.policyPointsModifier, "Policy Points");
        AddFlat(tech.flatPolicyPointsBonus, "Policy Points");

        if (parts.Count == 0) return string.Empty;
        return "Yields: " + string.Join(", ", parts);
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
        displayedTech = null;
        if (useFloatingPopups) return;
        TreePanelView.SetText(selectedTechNameText, "No Research");
        TreePanelView.SetText(selectedTechDescriptionText, "You are not researching any technology. Select one from the tech tree to begin earning progress toward it.");
        TreePanelView.SetText(selectedTechCostText, "");
        if (panelView != null) panelView.UpdateSelection(0, 0, false, "Research");
        if (selectedTechTurnsRemainingText != null) selectedTechTurnsRemainingText.text = "";
        TreePanelView.SetText(selectedTechPrerequisitesText, "");
        TreePanelView.SetText(selectedTechUnlocksText, "");
        if (selectedTechBuildingsText != null) selectedTechBuildingsText.text = "";
        if (selectedTechImprovementsText != null) selectedTechImprovementsText.text = "";
        if (selectedTechIconImage != null)
        {
            if (noResearchIcon != null)
            {
                selectedTechIconImage.gameObject.SetActive(true);
                selectedTechIconImage.sprite = noResearchIcon;
            }
            else
            {
                selectedTechIconImage.gameObject.SetActive(false);
            }
        }
        if (selectedTechEconomicImpactText != null) selectedTechEconomicImpactText.text = "";
    }

    private string BuildEconomicImpact(TechData tech)
    {
        if (playerCiv == null || tech == null)
            return string.Empty;

        int currentGold = GetTotalGoldPerTurn(playerCiv);
        int currentScience = GetTotalSciencePerTurn(playerCiv);
        int currentCulture = GetTotalCulturePerTurn(playerCiv);
        int currentFaith = GetTotalFaithPerTurn(playerCiv);
        int currentFood = GetTotalFoodPerTurn(playerCiv);
        int currentPolicy = GetTotalPolicyPerTurn(playerCiv);

        int projectedGold = Mathf.RoundToInt(currentGold * (1f + tech.goldModifier)) + tech.flatGoldBonus;
        int projectedScience = Mathf.RoundToInt(currentScience * (1f + tech.scienceModifier)) + tech.flatScienceBonus;
        int projectedCulture = Mathf.RoundToInt(currentCulture * (1f + tech.cultureModifier)) + tech.flatCultureBonus;
        int projectedFaith = Mathf.RoundToInt(currentFaith * (1f + tech.faithModifier)) + tech.flatFaithBonus;
        int projectedFood = Mathf.RoundToInt(currentFood * (1f + tech.foodModifier)) + tech.flatFoodBonus;
        int projectedPolicy = Mathf.RoundToInt(currentPolicy * (1f + tech.policyPointsModifier)) + tech.flatPolicyPointsBonus;

        int goldDiff = projectedGold - currentGold;
        int scienceDiff = projectedScience - currentScience;
        int cultureDiff = projectedCulture - currentCulture;
        int faithDiff = projectedFaith - currentFaith;
        int foodDiff = projectedFood - currentFood;
        int policyDiff = projectedPolicy - currentPolicy;

        {
            string impactSummary = "";
            if (goldDiff != 0) impactSummary += $"\nGold: {currentGold} -> {projectedGold} ({(goldDiff > 0 ? "+" : "")}{goldDiff})";
            if (scienceDiff != 0) impactSummary += $"\nScience: {currentScience} -> {projectedScience} ({(scienceDiff > 0 ? "+" : "")}{scienceDiff})";
            if (cultureDiff != 0) impactSummary += $"\nCulture: {currentCulture} -> {projectedCulture} ({(cultureDiff > 0 ? "+" : "")}{cultureDiff})";
            if (faithDiff != 0) impactSummary += $"\nFaith: {currentFaith} -> {projectedFaith} ({(faithDiff > 0 ? "+" : "")}{faithDiff})";
            if (foodDiff != 0) impactSummary += $"\nFood: {currentFood} -> {projectedFood} ({(foodDiff > 0 ? "+" : "")}{foodDiff})";
            if (policyDiff != 0) impactSummary += $"\nPolicy: {currentPolicy} -> {projectedPolicy} ({(policyDiff > 0 ? "+" : "")}{policyDiff})";
            
            return impactSummary.TrimStart();
        }


    }

    private int GetTotalGoldPerTurn(Civilization civ)
    {
        if (civ == null) return 0;
        int gold = 0;
        if (civ.cities != null)
            foreach (var city in civ.cities)
                if (city != null) gold += city.GetGoldPerTurn();
        if (civ.combatUnits != null)
            foreach (var u in civ.combatUnits)
                if (u != null && u.data != null) gold += civ.ComputeUnitPerTurnYield(u.data, u.Weapon, u.Shield, u.Armor, u.Miscellaneous).gold;
        if (civ.workerUnits != null)
            foreach (var w in civ.workerUnits)
                if (w != null && w.data != null) gold += civ.ComputeWorkerPerTurnYield(w.data).gold;
        if (civ.herds != null)
            foreach (var h in civ.herds)
                if (h != null) gold += h.GetAnimalYields().Gold;
        return gold;
    }

    private int GetTotalCulturePerTurn(Civilization civ)
    {
        if (civ == null) return 0;
        int culture = 0;
        if (civ.cities != null)
            foreach (var city in civ.cities)
                if (city != null) culture += city.GetCulturePerTurn();
        if (civ.combatUnits != null)
            foreach (var u in civ.combatUnits)
                if (u != null && u.data != null) culture += civ.ComputeUnitPerTurnYield(u.data, u.Weapon, u.Shield, u.Armor, u.Miscellaneous).culture;
        if (civ.workerUnits != null)
            foreach (var w in civ.workerUnits)
                if (w != null && w.data != null) culture += civ.ComputeWorkerPerTurnYield(w.data).culture;
        if (civ.herds != null)
            foreach (var h in civ.herds)
                if (h != null) culture += h.GetAnimalYields().Culture;
        return culture;
    }

    private int GetTotalFaithPerTurn(Civilization civ)
    {
        if (civ == null) return 0;
        int faith = 0;
        if (civ.cities != null)
            foreach (var city in civ.cities)
                if (city != null) faith += city.GetFaithPerTurn();
        if (civ.combatUnits != null)
            foreach (var u in civ.combatUnits)
                if (u != null && u.data != null) faith += civ.ComputeUnitPerTurnYield(u.data, u.Weapon, u.Shield, u.Armor, u.Miscellaneous).faith;
        if (civ.workerUnits != null)
            foreach (var w in civ.workerUnits)
                if (w != null && w.data != null) faith += civ.ComputeWorkerPerTurnYield(w.data).faith;
        if (civ.herds != null)
            foreach (var h in civ.herds)
                if (h != null) faith += h.GetAnimalYields().Faith;
        return faith;
    }

    private int GetTotalFoodPerTurn(Civilization civ)
    {
        if (civ == null) return 0;
        int food = 0;
        if (civ.cities != null)
            foreach (var city in civ.cities)
                if (city != null) food += city.GetFoodPerTurn();
        if (civ.combatUnits != null)
            foreach (var u in civ.combatUnits)
                if (u != null && u.data != null) food += civ.ComputeUnitPerTurnYield(u.data, u.Weapon, u.Shield, u.Armor, u.Miscellaneous).food;
        if (civ.workerUnits != null)
            foreach (var w in civ.workerUnits)
                if (w != null && w.data != null) food += civ.ComputeWorkerPerTurnYield(w.data).food;
        if (civ.herds != null)
            foreach (var h in civ.herds)
                if (h != null) food += h.GetAnimalYields().Food;
        return food;
    }

    private int GetTotalPolicyPerTurn(Civilization civ)
    {
        if (civ == null) return 0;
        int policy = 0;
        if (civ.cities != null)
            foreach (var city in civ.cities)
                if (city != null) policy += city.GetPolicyPointPerTurn();
        if (civ.combatUnits != null)
            foreach (var u in civ.combatUnits)
                if (u != null && u.data != null) policy += civ.ComputeUnitPerTurnYield(u.data, u.Weapon, u.Shield, u.Armor, u.Miscellaneous).policy;
        if (civ.workerUnits != null)
            foreach (var w in civ.workerUnits)
                if (w != null && w.data != null) policy += civ.ComputeWorkerPerTurnYield(w.data).policy;
        if (civ.herds != null)
            foreach (var h in civ.herds)
                if (h != null) policy += h.GetAnimalYields().Policy;
        return policy;
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
        UpdateHeader();
        if (playerCiv == null) return;
        observedTech = playerCiv.currentTech;
        observedProgress = playerCiv.currentTechProgress;
        observedCompletedCount = playerCiv.researchedTechs != null ? playerCiv.researchedTechs.Count : 0;
        if (hoverPopup != null) hoverPopup.RefreshForOwner(this);
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
        Image background = null;
        // Prefer the explicit background image exposed by the button UI component
        try { background = buttonUI.BackgroundImage; } catch { background = null; }
        if (background == null) background = buttonUI.GetComponent<Image>();
        if (background != null)
            background.color = GetTechStateColor(tech);

        if (playerCiv.researchedTechs != null && playerCiv.researchedTechs.Contains(tech))
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
            int queueIndex = playerCiv?.queuedTechs != null ? playerCiv.queuedTechs.IndexOf(btnUI.RepresentedTech) : -1;
            btnUI.SetQueueOrder(queueIndex >= 0 ? queueIndex + 1 : 0);
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
