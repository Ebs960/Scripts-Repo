using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class CultureUI : MonoBehaviour
{
    [Header("Panel References")]
    [SerializeField] private GameObject culturePanel;
    [SerializeField] private ScrollRect cultureScrollRect;
    [SerializeField] private RectTransform cultureContent;
    [SerializeField] private Transform cultureButtonContainer;
    [SerializeField] private GameObject cultureButtonPrefab;

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
    [SerializeField] private Button closeButton;

    private Civilization playerCiv;
    private CultureData currentlySelectedCulture;
    private readonly List<CultureButtonUI> cultureButtons = new();

    private bool _treeBuilt = false;
    private int _builtCultureCount = -1;
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
        playerCiv = civ;
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

        UIManager.Instance.ShowPanel("culturePanel");
        PopulateCultureTree();
        // Do not auto-select any culture when opening the UI; start blank
        currentlySelectedCulture = null;
        ClearInfoPanel();
    }

    public void Hide()
    {
        UIManager.Instance.HidePanel("culturePanel");
        if (UIManager.Instance != null && UIManager.Instance.unitInfoPanel != null)
            UIManager.Instance.unitInfoPanel.SetActive(true);
    }

    void PopulateCultureTree()
    {
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
                if (child != null) Destroy(child.gameObject);
            }
        }
        cultureButtons.Clear();

        CreateCultureTreeBackground();

        if (useCustomLayout)
            CreateCultureNodesWithCustomLayout();
        else
            CreateCultureNodesWithGridLayout();

        CreateConnectionLines();
        RefreshCultureButtonStates();

        _treeBuilt = true;
        _builtCultureCount = currentCultureCount;
        _builtUsedCustomLayout = useCustomLayout;
    }

    private void CreateCultureTreeBackground()
    {
        if (backgroundData == null || cultureContent == null) return;

        float totalWidth = backgroundData.GetTotalWidth();
        float imageHeight = 1024f * backgroundData.backgroundScale;

        float contentWidth = Mathf.Max(totalWidth, 3000f);
        float contentHeight = Mathf.Max(imageHeight, 1200f);
        cultureContent.sizeDelta = new Vector2(contentWidth, contentHeight);

        GameObject backgroundContainer = new GameObject("BackgroundContainer");
        backgroundContainer.transform.SetParent(cultureContent, false);

        RectTransform bgRect = backgroundContainer.AddComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0, 1);
        bgRect.anchorMax = new Vector2(0, 1);
        bgRect.pivot = new Vector2(0, 1);
        bgRect.sizeDelta = new Vector2(totalWidth, imageHeight);
        bgRect.anchoredPosition = Vector2.zero;
        bgRect.transform.SetAsFirstSibling();

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

        float maxX = 0f;
        float minY = 0f;
        foreach (var kvp in resolvedPositions)
        {
            Vector2 pos = kvp.Value;
            maxX = Mathf.Max(maxX, pos.x + cultureNodeSize.x);
            minY = Mathf.Min(minY, pos.y - cultureNodeSize.y);
        }

        if (cultureContent != null)
            cultureContent.sizeDelta = new Vector2(maxX + 100f, Mathf.Abs(minY) + 100f);

        foreach (var kvp in resolvedPositions)
            CreateCultureNode(kvp.Key, kvp.Value, cultureNodeSize);
    }

    private void CreateCultureNode(CultureData culture, Vector2 position, Vector2 nodeSize = default)
    {
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
        lineImage.color = new Color(0.5f, 0.8f, 0.5f, 0.7f);
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

    public void SelectCulture(CultureData culture)
    {
        currentlySelectedCulture = culture;
        UpdateInfoPanel(culture);

        if (playerCiv != null && playerCiv.CanCultivate(culture))
        {
            playerCiv.StartCulture(culture);
            RefreshUI();
        }

        foreach (var btnUI in cultureButtons)
            btnUI.SetSelected(culture == btnUI.RepresentedCulture);
    }

    void UpdateInfoPanel(CultureData culture)
    {
        if (culture == null)
        {
            ClearInfoPanel();
            return;
        }

        if (selectedCultureIconImage != null)
        {
            selectedCultureIconImage.gameObject.SetActive(culture.cultureIcon != null);
            selectedCultureIconImage.sprite = culture.cultureIcon;
        }

        selectedCultureNameText.text = culture.cultureName;
        selectedCultureDescriptionText.text = culture.description;

        int culturePerTurn = GetTotalCulturePerTurn(playerCiv);
        float remaining = culture.cultureCost;
        if (playerCiv != null && playerCiv.currentCulture == culture)
            remaining = culture.cultureCost - playerCiv.currentCultureProgress;
        selectedCultureCostText.text = $"Cost: {culture.cultureCost} Culture";
        if (selectedCultureTurnsRemainingText != null)
            selectedCultureTurnsRemainingText.text = culturePerTurn > 0 ? $"~{Mathf.CeilToInt(remaining / culturePerTurn)} turns" : "";

        string prereqs = "Prerequisites: ";
        List<string> prereqItems = new List<string>();
        if (culture.requiredTechnologies != null)
            foreach (var tech in culture.requiredTechnologies)
                if (tech != null) AddUniqueUnlock(prereqItems, tech.techName);
        if (culture.requiredCultures != null)
            foreach (var reqCulture in culture.requiredCultures)
                if (reqCulture != null) AddUniqueUnlock(prereqItems, reqCulture.cultureName);
        prereqs += prereqItems.Count > 0 ? string.Join(", ", prereqItems) : "None";
        selectedCulturePrerequisitesText.text = prereqs;

        List<string> buildingUnlocks = new List<string>();
        List<string> improvementUnlocks = new List<string>();
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
                        if (rc == culture) { AddUniqueUnlock(unlockItems, u.unitName); break; }

        var workerUnits = ResourceCache.GetAllWorkerUnits();
        if (workerUnits != null)
            foreach (var w in workerUnits)
                if (w != null && w.requiredCultures != null)
                    foreach (var rc in w.requiredCultures)
                        if (rc == culture) { AddUniqueUnlock(unlockItems, w.unitName); break; }

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

        if (culture.unlockedGovernments != null)
            foreach (var g in culture.unlockedGovernments)
                if (g != null) AddUniqueUnlock(unlockItems, g.governmentName);
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

        if (selectedCultureBuildingsText != null)
            selectedCultureBuildingsText.text = FormatUnlockField("Buildings", buildingUnlocks);
        if (selectedCultureImprovementsText != null)
            selectedCultureImprovementsText.text = FormatUnlockField("Improvements", improvementUnlocks);

        selectedCultureUnlocksText.text = FormatUnlockField("Unlocks", unlockItems);
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
        return total;
    }

    void ClearInfoPanel()
    {
        selectedCultureNameText.text = "Select a Culture";
        selectedCultureDescriptionText.text = "";
        selectedCultureCostText.text = "";
        if (selectedCultureTurnsRemainingText != null) selectedCultureTurnsRemainingText.text = "";
        selectedCulturePrerequisitesText.text = "";
        selectedCultureUnlocksText.text = "";
        if (selectedCultureBuildingsText != null) selectedCultureBuildingsText.text = "";
        if (selectedCultureImprovementsText != null) selectedCultureImprovementsText.text = "";
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

    private void UpdateCultureButtonState(CultureButtonUI buttonUI, CultureData culture)
    {
        if (playerCiv == null) return;

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
            UpdateCultureButtonState(btnUI, btnUI.RepresentedCulture);
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