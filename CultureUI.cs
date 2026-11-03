using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.InputSystem;

public class CultureUI : MonoBehaviour
{
    [Header("Panel References")]
    [SerializeField] private GameObject culturePanel;
    [SerializeField] private ScrollRect cultureScrollRect;
    [SerializeField] private RectTransform cultureContent;
    [SerializeField] private Transform cultureButtonContainer;
    [SerializeField] private GameObject cultureButtonPrefab;
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

    [Header("Culture Tree Integration")]
    [SerializeField] private CultureTreeBackgroundData backgroundData;
    [SerializeField] private bool useCustomLayout = true;
    [SerializeField] private TextAsset layoutJson;
    [SerializeField] private Vector2 cultureNodeSize = new Vector2(180, 90);
    [SerializeField] private Vector2 gridSpacing = new Vector2(200, 100);

    [Header("Info Panel")]
    [SerializeField] private TextMeshProUGUI selectedCultureNameText;
    [SerializeField] private TextMeshProUGUI selectedCultureDescriptionText;
    [SerializeField] private TextMeshProUGUI selectedCultureCostText;
    [SerializeField] private TextMeshProUGUI selectedCultureTurnsRemainingText;
    [SerializeField] private TextMeshProUGUI selectedCulturePrerequisitesText;
    [SerializeField] private TextMeshProUGUI selectedCultureUnlocksText;
    [SerializeField] private TextMeshProUGUI selectedCultureBuildingsText;
    [SerializeField] private TextMeshProUGUI selectedCultureImprovementsText;
    [SerializeField] private UnityEngine.UI.Image selectedCultureIconImage;
    [Tooltip("Icon displayed when no culture is currently being adopted. Assign a question mark or hourglass sprite.")]
    [SerializeField] private Sprite noResearchIcon;
    [SerializeField] private Button closeButton;

    [Header("Economic Impact Preview")]
    [SerializeField] private TextMeshProUGUI selectedCultureEconomicImpactText;

    private Civilization playerCiv;
    private CultureData currentlySelectedCulture;
    private CultureData displayedCulture;
    private readonly List<CultureButtonUI> cultureButtons = new();

    private bool _treeBuilt = false;
    private int _builtCultureCount = -1;
    private bool _builtUsedCustomLayout = false;
    private CultureData observedCulture;
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
                    UIManager.Instance.HidePanel("culturePanel");
                }
                else
                {
                    Debug.LogError("CultureUI: UIManager.Instance is null. Cannot hide panel.");
                    if (culturePanel != null) culturePanel.SetActive(false);
                }
            });

            if (UIManager.Instance != null)
                UIManager.Instance.WireUIInteractions(closeButton.gameObject);
        }
    }

    public void Show(Civilization civ)
    {
        if (playerCiv != civ) currentlySelectedCulture = null;
        playerCiv = civ;
        followsPlayerCivilization = CivilizationManager.Instance != null && CivilizationManager.Instance.playerCiv == civ;
        if (playerCiv == null)
        {
            Debug.LogError("CultureUI Show called with null civ");
            return;
        }

        if (UIManager.Instance != null)
        {
            if (UIManager.Instance.unitInfoPanel != null)
                UIManager.Instance.unitInfoPanel.SetActive(false);
            if (UIManager.Instance.cityPanel != null)
                UIManager.Instance.cityPanel.SetActive(false);
        }

        if (UIManager.Instance != null) UIManager.Instance.ShowPanel("culturePanel");
        else if (culturePanel != null) culturePanel.SetActive(true);
        ConfigurePresentation();
        PopulateCultureTree();
        // Preserve any prior explicit selection when reopening the UI.
        RefreshUI();
    }

    public void Hide()
    {
        ClosePopup();
        if (UIManager.Instance != null) UIManager.Instance.HidePanel("culturePanel");
        else if (culturePanel != null) culturePanel.SetActive(false);
        if (UIManager.Instance != null && UIManager.Instance.unitInfoPanel != null)
            UIManager.Instance.unitInfoPanel.SetActive(true);
    }

    void PopulateCultureTree()
    {
        if (cultureButtonPrefab == null || (cultureContent == null && cultureButtonContainer == null)) return;
        if (CultureManager.Instance == null || CultureManager.Instance.allCultures == null)
        {
            Debug.LogError("CultureManager or its cultures not available.");
            return;
        }

        int currentCultureCount = CultureManager.Instance.allCultures.Count;
        bool needsRebuild =
            !_treeBuilt ||
            cultureButtons == null || cultureButtons.Count == 0 ||
            _builtCultureCount != currentCultureCount ||
            _builtUsedCustomLayout != useCustomLayout;

        if (!needsRebuild)
        {
            RefreshCultureButtonStates();
            return;
        }

        Transform container = cultureContent != null ? cultureContent : cultureButtonContainer;
        if (container != null)
        {
            foreach (Transform child in container)
            {
                if (child != null) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            }
        }
        cultureButtons.Clear();
        TreeContentBounds.ResetOrigin(cultureContent);

        CreateCultureTreeBackground();

        if (useCustomLayout)
            CreateCultureNodesWithCustomLayout();
        else
            CreateCultureNodesWithGridLayout();

        CreateConnectionLines();
        TreeContentBounds.Fit(cultureContent, new Vector2(3000, 1200), edgePadding);
        RefreshCultureButtonStates();

        _treeBuilt = true;
        _builtCultureCount = currentCultureCount;
        _builtUsedCustomLayout = useCustomLayout;
    }

    private void CreateCultureTreeBackground()
    {
        if (backgroundData != null) TreePanorama.Render(cultureContent, backgroundData.GetPlacements(), backgroundData.showEraMarkers);
    }

    private void CreateCultureNodesWithGridLayout()
    {
        var culturesByAge = CultureManager.Instance.allCultures
            .Where(c => c != null)
            .GroupBy(c => c.cultureAge)
            .OrderBy(g => (int)g.Key);

        float currentAgeX = 0f;

        foreach (var ageGroup in culturesByAge)
        {
            TechAge age = ageGroup.Key;
            var ageCultures = ageGroup.OrderBy(c => c.cultureCost).ToList();

            if (backgroundData != null)
                currentAgeX = backgroundData.GetAgeStartPosition(age);

            for (int i = 0; i < ageCultures.Count; i++)
            {
                var culture = ageCultures[i];
                float yPos = -(i * gridSpacing.y + 50f);
                Vector2 position = new Vector2(currentAgeX + 100f, yPos);
                CreateCultureNode(culture, position, cultureNodeSize);
            }

            if (backgroundData != null)
                currentAgeX += backgroundData.GetWidthForAge(age) + backgroundData.imageSpacing;
            else
                currentAgeX += 300f;
        }
    }

    private void CreateCultureNodesWithCustomLayout()
    {
        CultureTreeLayout layout = LoadLayoutFromFile();
        if (layout == null)
        {
            Debug.LogWarning("[CultureUI] No culture tree layout found, falling back to grid layout");
            CreateCultureNodesWithGridLayout();
            return;
        }

        var layoutPositions = new Dictionary<string, Vector2>(System.StringComparer.OrdinalIgnoreCase);
        if (layout.culturePositions != null)
        {
            foreach (var pos in layout.culturePositions)
            {
                if (pos == null || string.IsNullOrWhiteSpace(pos.cultureName))
                    continue;

                layoutPositions[pos.cultureName] = pos.position;
            }
        }

        var resolvedPositions = new Dictionary<CultureData, Vector2>();
        var fallbackRowsByAge = new Dictionary<TechAge, int>();

        foreach (CultureData culture in CultureManager.Instance.allCultures)
        {
            if (culture == null) continue;

            Vector2 position;
            bool foundPosition =
                layoutPositions.TryGetValue(culture.name, out position) ||
                (!string.IsNullOrWhiteSpace(culture.cultureName) && layoutPositions.TryGetValue(culture.cultureName, out position));

            if (foundPosition)
            {
                resolvedPositions[culture] = position;
                continue;
            }

            bool placedFromPrereqs = false;
            if (culture.requiredCultures != null && culture.requiredCultures.Length > 0)
            {
                float maxPrereqX = float.NegativeInfinity;
                float avgPrereqY = 0f;
                int prereqCount = 0;

                foreach (var prereq in culture.requiredCultures)
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
                int row = fallbackRowsByAge.TryGetValue(culture.cultureAge, out int existingRow) ? existingRow : 0;
                float ageStartX = backgroundData != null
                    ? backgroundData.GetAgeStartPosition(culture.cultureAge) + 100f
                    : 100f + ((int)culture.cultureAge * 300f);
                position = new Vector2(ageStartX, -(row * gridSpacing.y + 50f));
                fallbackRowsByAge[culture.cultureAge] = row + 1;
            }

            Debug.LogWarning($"[CultureUI] No saved position found for culture '{culture.name}'. Using fallback position {position}.");
            resolvedPositions[culture] = position;
        }

        if (resolvedPositions.Count == 0)
        {
            Debug.LogWarning("[CultureUI] Custom layout resolved no culture positions, falling back to grid layout");
            CreateCultureNodesWithGridLayout();
            return;
        }

        foreach (var kvp in resolvedPositions)
            CreateCultureNode(kvp.Key, kvp.Value, cultureNodeSize);
    }

    private void CreateCultureNode(CultureData culture, Vector2 position, Vector2 nodeSize = default)
    {
        if (culture == null || cultureButtonPrefab == null) return;
        if (nodeSize == default)
            nodeSize = cultureNodeSize;

        Transform parent = cultureContent != null ? cultureContent : cultureButtonContainer;
        GameObject cultureNode = Instantiate(cultureButtonPrefab, parent);
        cultureNode.name = $"CultureNode_{culture.cultureName}";

        RectTransform rect = cultureNode.GetComponent<RectTransform>();
        if (rect == null) rect = cultureNode.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = nodeSize;
        rect.anchoredPosition = position;

        CultureButtonUI cultureButtonUI = cultureNode.GetComponent<CultureButtonUI>();
        if (cultureButtonUI == null) cultureButtonUI = cultureNode.AddComponent<CultureButtonUI>();
        cultureButtonUI.Initialize(culture, this);
        cultureButtons.Add(cultureButtonUI);

        if (UIManager.Instance != null)
            UIManager.Instance.WireUIInteractions(cultureNode);
    }

    private void CreateConnectionLines()
    {
        if (cultureContent == null) return;

        foreach (var cultureButton in cultureButtons)
        {
            var culture = cultureButton.RepresentedCulture;
            if (culture.requiredCultures == null) continue;

            foreach (var prereq in culture.requiredCultures)
            {
                var prereqButton = cultureButtons.FirstOrDefault(cb => cb.RepresentedCulture == prereq);
                if (prereqButton != null)
                    CreateConnectionLine(prereqButton.transform, cultureButton.transform);
            }
        }
    }

    private void CreateConnectionLine(Transform from, Transform to)
    {
        GameObject lineObj = new GameObject("ConnectionLine");
        lineObj.transform.SetParent(cultureContent, false);
        lineObj.transform.SetSiblingIndex(1);

        RectTransform lineRect = lineObj.AddComponent<RectTransform>();
        Image lineImage = lineObj.AddComponent<Image>();
        lineImage.color = new Color(0.55f, 0.49f, 0.33f, 0.75f);
        lineImage.raycastTarget = false;

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

    private void SelectCultureInfoOnly(CultureData culture)
    {
        currentlySelectedCulture = culture;
        UpdateInfoPanel(culture);
        foreach (var btnUI in cultureButtons)
            btnUI.SetSelected(culture == btnUI.RepresentedCulture);
    }

    public void PreviewCulture(CultureData culture)
    {
        SelectCultureInfoOnly(culture);
        var button = cultureButtons.FirstOrDefault(item => item != null && item.RepresentedCulture == culture);
        if (button != null) PinPopup(culture, button.transform as RectTransform);
    }

    public bool ShowHoverPopup(CultureData culture, RectTransform anchor)
    {
        if (!useFloatingPopups || culture == null || anchor == null) return false;
        hoverPopup = ResearchHoverPopup.GetOrCreate(this, hoverPopup);
        if (hoverPopup == null) return false;
        hoverPopup.Request(this, anchor, () => BuildPopupInfo(culture));
        return true;
    }

    public void PinPopup(CultureData culture, RectTransform anchor, bool fromNavigation = false)
    {
        if (!useFloatingPopups || culture == null || anchor == null) return;
        hoverPopup = ResearchHoverPopup.GetOrCreate(this, hoverPopup);
        if (hoverPopup != null && (!fromNavigation || hoverPopup.pinOnNavigationSelection))
            hoverPopup.Request(this, anchor, () => BuildPopupInfo(culture), true);
    }

    public void LeaveHoverPopup(RectTransform anchor)
    {
        if (hoverPopup != null) hoverPopup.Leave(this, anchor);
    }

    public void ClosePopup() { if (hoverPopup != null) hoverPopup.HideForOwner(this); }
    private void OnDisable() { ClosePopup(); }

    private void LateUpdate()
    {
        if (culturePanel != null && !culturePanel.activeInHierarchy) return;
        var currentPlayer = CivilizationManager.Instance != null ? CivilizationManager.Instance.playerCiv : null;
        if (followsPlayerCivilization && currentPlayer != null && currentPlayer != playerCiv) { Show(currentPlayer); return; }
        if (playerCiv == null || panelView == null) return;
        if (observedCulture != playerCiv.currentCulture || observedProgress != playerCiv.currentCultureProgress ||
            observedCompletedCount != (playerCiv.researchedCultures != null ? playerCiv.researchedCultures.Count : 0))
            RefreshUI();
        else if (Time.unscaledTime >= nextHeaderRefresh) UpdateHeader();
    }

    private void UpdateHeader()
    {
        if (panelView == null) return;
        panelView.UpdateFocus(playerCiv != null && playerCiv.currentCulture != null ? playerCiv.currentCulture.cultureName : "No Active Culture",
            playerCiv != null ? playerCiv.currentCultureProgress : 0, playerCiv != null && playerCiv.currentCulture != null ? playerCiv.currentCulture.cultureCost : 0,
            GetTotalCulturePerTurn(playerCiv), playerCiv != null && playerCiv.currentCulture != null);
        nextHeaderRefresh = Time.unscaledTime + 0.25f;
    }

    private void ConfigurePresentation()
    {
        if (enableNavigation && cultureScrollRect != null)
        {
            if (navigation == null) navigation = cultureScrollRect.GetComponent<TreeNavigation>();
            if (navigation != null) navigation.Configure(cultureScrollRect);
        }
        if (panelView == null) panelView = TreePanelView.GetOrCreateShell(this, culturePanel, cultureScrollRect, closeButton);
        else panelView.PrepareShell(this, culturePanel, cultureScrollRect, closeButton);
        if (panelView != null)
        {
            if (mainShellSprite != null) panelView.mainShellSprite = mainShellSprite;
            if (researchProgressSprite != null) panelView.researchProgressSprite = researchProgressSprite;
            if (legacyInformationPanels != null && legacyInformationPanels.Length > 0) panelView.legacyInformationPanels = legacyInformationPanels;
            panelView.Configure("CULTURE TREE", navigation, AdoptSelectedCulture, FocusCurrentResearch, Hide);
            if (useFloatingPopups) panelView.HideLegacyDetails(selectedCultureNameText, selectedCultureDescriptionText,
                selectedCultureCostText, selectedCultureTurnsRemainingText, selectedCulturePrerequisitesText, selectedCultureUnlocksText,
                selectedCultureBuildingsText, selectedCultureImprovementsText, selectedCultureIconImage, selectedCultureEconomicImpactText);
        }
            hoverPopup = ResearchHoverPopup.GetOrCreate(this, hoverPopup);
            if (hoverPopup != null && hoverPopupSprite != null) hoverPopup.backgroundSprite = hoverPopupSprite;
    }

    public void AdoptSelectedCulture()
    {
        if (displayedCulture != null && playerCiv != null && playerCiv.currentCulture != displayedCulture && playerCiv.CanCultivate(displayedCulture))
            SelectCulture(displayedCulture);
    }

    public void FocusCurrentResearch()
    {
        if (playerCiv == null || playerCiv.currentCulture == null) return;
        var button = cultureButtons.FirstOrDefault(item => item != null && item.RepresentedCulture == playerCiv?.currentCulture);
        if (navigation != null && button != null) navigation.Focus(button.transform as RectTransform);
    }

    public void ResetView() { if (navigation != null) navigation.ResetView(); }

    [ContextMenu("Rebuild Tree")]
    public void RebuildTree()
    {
        ClosePopup();
        _treeBuilt = false;
        if (playerCiv != null) { PopulateCultureTree(); RefreshUI(); }
    }

    public void FocusAge(int ageIndex)
    {
        if (navigation == null || backgroundData == null || cultureContent == null) return;
        Vector2 point = TreeContentBounds.ToContentPosition(cultureContent,
            new Vector2(backgroundData.GetAgeStartPosition((TechAge)ageIndex), -backgroundData.displayHeight * backgroundData.backgroundScale * 0.5f));
        navigation.FocusPoint(cultureContent.TransformPoint(point));
    }

    public void SelectCulture(CultureData culture)
    {
        if (culture == null) return;
        currentlySelectedCulture = culture;
        UpdateInfoPanel(culture);

        if (playerCiv != null)
        {
            bool queueRequested = IsQueueModifierPressed();
            if (queueRequested)
            {
                playerCiv.QueueCulture(culture);
            }
            else
            {
                playerCiv.StartCulture(culture);
            }
            RefreshUI();
        }

        foreach (var btnUI in cultureButtons)
            btnUI.SetSelected(culture == btnUI.RepresentedCulture);
    }

    private bool IsQueueModifierPressed()
    {
        return Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
    }

    void UpdateInfoPanel(CultureData culture)
    {
        displayedCulture = culture;
        if (useFloatingPopups) return;
        if (culture == null)
        {
            ClearInfoPanel();
            return;
        }
        ResearchPopupInfo info = BuildPopupInfo(culture);
        if (selectedCultureIconImage != null)
        {
            selectedCultureIconImage.gameObject.SetActive(culture.cultureIcon != null);
            selectedCultureIconImage.sprite = culture.cultureIcon;
        }

        TreePanelView.SetText(selectedCultureNameText, info.name);
        TreePanelView.SetText(selectedCultureDescriptionText, info.description);
        TreePanelView.SetText(selectedCultureCostText, $"Cost: {info.cost:0} {info.currency}");
        TreePanelView.SetText(selectedCultureTurnsRemainingText, info.turnsRemaining.HasValue ? $"~{info.turnsRemaining} turns" : "");
        TreePanelView.SetText(selectedCulturePrerequisitesText, info.prerequisites);
        TreePanelView.SetText(selectedCultureBuildingsText, info.buildings);
        TreePanelView.SetText(selectedCultureImprovementsText, info.improvements);
        TreePanelView.SetText(selectedCultureUnlocksText, info.units + (string.IsNullOrEmpty(info.otherUnlocks) ? "" : "\n" + info.otherUnlocks) + (string.IsNullOrEmpty(info.yields) ? "" : "\n" + info.yields));
        TreePanelView.SetText(selectedCultureEconomicImpactText, info.economicEffects);
        if (panelView != null) panelView.UpdateSelection(info.progress, info.cost, info.canStart, info.actionLabel);
    }

    public ResearchPopupInfo BuildPopupInfo(CultureData culture)
    {
        if (culture == null) return null;
        bool completed = playerCiv?.researchedCultures != null && playerCiv.researchedCultures.Contains(culture);
        bool active = playerCiv != null && playerCiv.currentCulture == culture;
        bool available = !completed && playerCiv != null && playerCiv.CanCultivate(culture);
        var info = ResearchPopupInfo.Create(culture.cultureName, culture.cultureIcon, culture.cultureAge,
            completed ? ResearchPopupStatus.Completed : active ? ResearchPopupStatus.Active : available ? ResearchPopupStatus.Available : ResearchPopupStatus.Locked,
            culture.cultureCost, active ? playerCiv.currentCultureProgress : 0, GetTotalCulturePerTurn(playerCiv), "Culture");
        info.description = culture.description;
        info.canStart = available && !active;
        info.actionLabel = completed ? "Completed" : active ? "Adopting" : "Adopt";
        List<string> prereqItems = new List<string>();
        if (culture.requiredTechnologies != null)
            foreach (var tech in culture.requiredTechnologies)
                if (tech != null) AddUniqueUnlock(prereqItems, tech.techName);
        if (culture.requiredCultures != null)
            foreach (var reqCulture in culture.requiredCultures)
                if (reqCulture != null) AddUniqueUnlock(prereqItems, reqCulture.cultureName);
        if (culture.requiredCityCount > 0) prereqItems.Add($"{culture.requiredCityCount} cities");
        if (culture.requiredControlledBiomes != null)
            foreach (var biome in culture.requiredControlledBiomes) AddUniqueUnlock(prereqItems, "Control " + biome);
        prereqItems.Add("Age reached: " + culture.cultureAge.ToString().Replace("Age", " Age"));
        info.prerequisites = ResearchPopupInfo.JoinRelevant(prereqItems);

        List<string> buildingUnlocks = new List<string>();
        List<string> improvementUnlocks = new List<string>();
        List<string> unitUnlocks = new List<string>();
        List<string> unlockItems = new List<string>();

        var buildings = ResourceCache.GetAllBuildings();
        if (buildings != null)
            foreach (var b in buildings)
                if (b != null && b.requiredCultures != null)
                    foreach (var rc in b.requiredCultures)
                        if (rc == culture)
                        {
                            AddUniqueUnlock(buildingUnlocks, b.buildingName);
                            AddUniqueUnlock(unlockItems, b.buildingName);
                            break;
                        }

        var combatUnits = ResourceCache.GetAllCombatUnits();
        if (combatUnits != null)
            foreach (var u in combatUnits)
                if (u != null && u.requiredCultures != null)
                    foreach (var rc in u.requiredCultures)
                        if (rc == culture) { AddUniqueUnlock(unitUnlocks, u.unitName); break; }

        var workerUnits = ResourceCache.GetAllWorkerUnits();
        if (workerUnits != null)
            foreach (var w in workerUnits)
                if (w != null && w.requiredCultures != null)
                    foreach (var rc in w.requiredCultures)
                        if (rc == culture) { AddUniqueUnlock(unitUnlocks, w.unitName); break; }

        var improvements = ResourceCache.GetAllImprovements();
        if (improvements != null)
            foreach (var imp in improvements)
                if (imp != null)
                {
                    if (imp.requiredCultures != null)
                    {
                        foreach (var rc in imp.requiredCultures)
                        {
                            if (rc == culture)
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
                            if (upgrade == null || upgrade.requiredCulture != culture)
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
                if (eq != null && eq.requiredCultures != null)
                    foreach (var rc in eq.requiredCultures)
                        if (rc == culture) { AddUniqueUnlock(unlockItems, eq.equipmentName); break; }

        var governments = ResourceCache.GetAllGovernmentData();
        if (governments != null)
            foreach (var g in governments)
                if (g != null && g.requiredCultures != null && System.Array.IndexOf(g.requiredCultures, culture) >= 0)
                    AddUniqueUnlock(unlockItems, g.governmentName);
        if (culture.unlockedReligions != null)
            foreach (var r in culture.unlockedReligions)
                if (r != null) AddUniqueUnlock(unlockItems, r.religionName);
        if (culture.unlockedLeaders != null)
            foreach (var leader in culture.unlockedLeaders)
                if (leader != null) AddUniqueUnlock(unlockItems, leader.leaderName);
        if (culture.unlocksReligion)
            AddUniqueUnlock(unlockItems, "Religion Mechanics");
        if (culture.unlocksPantheon)
            AddUniqueUnlock(unlockItems, "Pantheon Founding");
        if (culture.enablesTradeSystem)
            AddUniqueUnlock(unlockItems, "Trade System");
        if (culture.enablesGovernors)
            AddUniqueUnlock(unlockItems, "Governors");
        if (culture.unlocksPantheons != null)
            foreach (var pantheon in culture.unlocksPantheons)
                if (pantheon != null) AddUniqueUnlock(unlockItems, pantheon.pantheonName);
        if (culture.unlocksBeliefs != null)
            foreach (var belief in culture.unlocksBeliefs)
                if (belief != null) AddUniqueUnlock(unlockItems, belief.beliefName);

        info.buildings = ResearchPopupInfo.JoinRelevant(buildingUnlocks);
        info.improvements = ResearchPopupInfo.JoinRelevant(improvementUnlocks);
        info.units = ResearchPopupInfo.JoinRelevant(unitUnlocks);
        info.otherUnlocks = ResearchPopupInfo.JoinRelevant(unlockItems.Except(buildingUnlocks).Except(improvementUnlocks).ToList());
        info.yields = FormatYieldInfo(culture);
        info.bonuses = ResearchPopupInfo.FormatBonuses(culture.attackBonus, culture.defenseBonus, culture.movementBonus);
        info.economicEffects = BuildEconomicImpact(culture);
        return info;
    }

    private string FormatYieldInfo(CultureData culture)
    {
        if (culture == null) return string.Empty;
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

        AddPct(culture.foodModifier, "Food");
        AddFlat(culture.flatFoodBonus, "Food");
        AddPct(culture.productionModifier, "Production");
        AddFlat(culture.flatProductionBonus, "Production");
        AddPct(culture.goldModifier, "Gold");
        AddFlat(culture.flatGoldBonus, "Gold");
        AddPct(culture.scienceModifier, "Science");
        AddFlat(culture.flatScienceBonus, "Science");
        AddPct(culture.cultureModifier, "Culture");
        AddFlat(culture.flatCultureBonus, "Culture");
        AddPct(culture.faithModifier, "Faith");
        AddFlat(culture.flatFaithBonus, "Faith");
        AddPct(culture.policyPointsModifier, "Policy Points");
        AddFlat(culture.flatPolicyPointsBonus, "Policy Points");

        if (parts.Count == 0) return string.Empty;
        return "Yields: " + string.Join(", ", parts);
    }

    private int GetTotalCulturePerTurn(Civilization civ)
    {
        if (civ == null) return 0;
        int total = 0;
        if (civ.cities != null)
            foreach (var city in civ.cities)
                if (city != null) total += city.GetCulturePerTurn();
        if (civ.combatUnits != null)
            foreach (var u in civ.combatUnits)
                if (u != null && u.data != null) total += civ.ComputeUnitPerTurnYield(u.data, u.Weapon, u.Shield, u.Armor, u.Miscellaneous).culture;
        if (civ.workerUnits != null)
            foreach (var w in civ.workerUnits)
                if (w != null && w.data != null) total += civ.ComputeWorkerPerTurnYield(w.data).culture;
        if (civ.herds != null)
            foreach (var h in civ.herds)
                if (h != null) total += h.GetAnimalYields().Culture;
        return total;
    }

    void ClearInfoPanel()
    {
        displayedCulture = null;
        if (useFloatingPopups) return;
        TreePanelView.SetText(selectedCultureNameText, "No Culture");
        TreePanelView.SetText(selectedCultureDescriptionText, "You are not adopting any culture. Select one from the culture tree to begin earning progress toward it.");
        TreePanelView.SetText(selectedCultureCostText, "");
        if (panelView != null) panelView.UpdateSelection(0, 0, false, "Adopt");
        if (selectedCultureTurnsRemainingText != null) selectedCultureTurnsRemainingText.text = "";
        TreePanelView.SetText(selectedCulturePrerequisitesText, "");
        TreePanelView.SetText(selectedCultureUnlocksText, "");
        if (selectedCultureBuildingsText != null) selectedCultureBuildingsText.text = "";
        if (selectedCultureImprovementsText != null) selectedCultureImprovementsText.text = "";
        if (selectedCultureIconImage != null)
        {
            if (noResearchIcon != null)
            {
                selectedCultureIconImage.gameObject.SetActive(true);
                selectedCultureIconImage.sprite = noResearchIcon;
            }
            else
            {
                selectedCultureIconImage.gameObject.SetActive(false);
            }
        }
        if (selectedCultureEconomicImpactText != null) selectedCultureEconomicImpactText.text = "";
    }

    private string BuildEconomicImpact(CultureData culture)
    {
        if (playerCiv == null || culture == null)
            return string.Empty;

        int currentGold = GetTotalGoldPerTurn(playerCiv);
        int currentScience = GetTotalSciencePerTurn(playerCiv);
        int currentCulture = GetTotalCulturePerTurn(playerCiv);
        int currentFaith = GetTotalFaithPerTurn(playerCiv);
        int currentFood = GetTotalFoodPerTurn(playerCiv);
        int currentPolicy = GetTotalPolicyPerTurn(playerCiv);

        int projectedGold = Mathf.RoundToInt(currentGold * (1f + culture.goldModifier)) + culture.flatGoldBonus;
        int projectedScience = Mathf.RoundToInt(currentScience * (1f + culture.scienceModifier)) + culture.flatScienceBonus;
        int projectedCulture = Mathf.RoundToInt(currentCulture * (1f + culture.cultureModifier)) + culture.flatCultureBonus;
        int projectedFaith = Mathf.RoundToInt(currentFaith * (1f + culture.faithModifier)) + culture.flatFaithBonus;
        int projectedFood = Mathf.RoundToInt(currentFood * (1f + culture.foodModifier)) + culture.flatFoodBonus;
        int projectedPolicy = Mathf.RoundToInt(currentPolicy * (1f + culture.policyPointsModifier)) + culture.flatPolicyPointsBonus;

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

    private int GetTotalSciencePerTurn(Civilization civ)
    {
        if (civ == null) return 0;
        int science = 0;
        if (civ.cities != null)
            foreach (var city in civ.cities)
                if (city != null) science += city.GetSciencePerTurn();
        if (civ.combatUnits != null)
            foreach (var u in civ.combatUnits)
                if (u != null && u.data != null) science += civ.ComputeUnitPerTurnYield(u.data, u.Weapon, u.Shield, u.Armor, u.Miscellaneous).science;
        if (civ.workerUnits != null)
            foreach (var w in civ.workerUnits)
                if (w != null && w.data != null) science += civ.ComputeWorkerPerTurnYield(w.data).science;
        if (civ.herds != null)
            foreach (var h in civ.herds)
                if (h != null) science += h.GetAnimalYields().Science;
        return science;
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

    // NOTE: RefreshUI is implemented further below; duplicate removed.

    private void UpdateCultureButtonState(CultureButtonUI buttonUI, CultureData culture)
    {
        if (playerCiv == null) return;

        if (playerCiv.researchedCultures != null && playerCiv.researchedCultures.Contains(culture))
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
            int queueIndex = playerCiv?.queuedCultures != null ? playerCiv.queuedCultures.IndexOf(btnUI.RepresentedCulture) : -1;
            btnUI.SetQueueOrder(queueIndex >= 0 ? queueIndex + 1 : 0);
        }
    }

    /// <summary>
    /// Refresh the whole UI: button states and info panel (preserves selection/current research).
    /// </summary>
    public void RefreshUI()
    {
        UpdateHeader();
        if (playerCiv == null) return;
        observedCulture = playerCiv.currentCulture;
        observedProgress = playerCiv.currentCultureProgress;
        observedCompletedCount = playerCiv.researchedCultures != null ? playerCiv.researchedCultures.Count : 0;
        if (hoverPopup != null) hoverPopup.RefreshForOwner(this);
        RefreshCultureButtonStates();

        if (playerCiv.currentCulture != null)
        {
            UpdateInfoPanel(playerCiv.currentCulture);
            foreach (var btnUI in cultureButtons)
                btnUI.SetSelected(playerCiv.currentCulture == btnUI.RepresentedCulture);
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

    private CultureTreeLayout LoadLayoutFromFile()
    {
        if (layoutJson == null)
        {
            Debug.LogWarning("[CultureUI] Culture tree layout TextAsset not assigned!");
            return null;
        }

        try
        {
            CultureTreeLayout layout = JsonUtility.FromJson<CultureTreeLayout>(layoutJson.text);
            return layout;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[CultureUI] Failed to load culture tree layout: {e.Message}");
            return null;
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
