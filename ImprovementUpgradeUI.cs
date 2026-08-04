// Assets/Scripts/UI/ImprovementUpgradeUI.cs
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class ImprovementUpgradeUI : MonoBehaviour
{
    [Header("Panel References")]
    [SerializeField] private GameObject upgradePanel;
    [SerializeField] private TextMeshProUGUI improvementNameText;
    [SerializeField] private TextMeshProUGUI improvementYieldsText;
    [SerializeField] private Image improvementIconImage;
    [Header("Stored Units UI")]
    [SerializeField] private Transform storedUnitsContainer;
    [SerializeField] private GameObject storedUnitButtonPrefab;
    [SerializeField] private TextMeshProUGUI capacityText;
    [Header("Dismantle UI")]
    [SerializeField] private Button dismantleButton;
    [SerializeField] private TextMeshProUGUI dismantleRefundText;
        
    [SerializeField] private Transform upgradeButtonContainer;
    [SerializeField] private GameObject upgradeButtonPrefab;
    [Tooltip("Optional section prefab used to group upgrade options into visible slot-based rows.")]
    [SerializeField] private GameObject upgradeSlotSectionPrefab;
    [SerializeField] private Button closeButton;

    [Header("Context Actions")]
    [SerializeField] private Button manageSpecialistsButton;
    [SerializeField] private Button openCityStorageButton;
    [SerializeField] private TextMeshProUGUI emptyStateText;

    [Header("Replacement Confirmation")]
    [SerializeField] private GameObject replacementConfirmationPanel;
    [SerializeField] private TextMeshProUGUI replacementConfirmationText;
    [SerializeField] private Button confirmReplacementButton;
    [SerializeField] private Button cancelReplacementButton;

    private ImprovementData currentImprovement;
    private int currentTileIndex = -1;
    private int currentPlanetIndex = -1;
    private Civilization currentCiv;
    private List<GameObject> upgradeButtons = new List<GameObject>();
    private List<GameObject> upgradeSlotSections = new List<GameObject>();
    private List<GameObject> storedUnitButtons = new List<GameObject>();
    private ImprovementUpgradeData pendingUpgrade;

    // Slide animation fields
    [Header("Slide Settings")]
    [SerializeField] private float slideDuration = 0.18f;
    [SerializeField] private float offscreenPadding = 12f;
    private RectTransform panelRect;
    private Vector2 targetAnchoredPos;
    private Vector2 hiddenAnchoredPos;
    private Coroutine slideCoroutine;
    // TileSystem subscription for click-away behavior
    private TileSystem eventTileSystem;
    private int eventPlanetIndex = int.MinValue;

    private void Awake()
    {
        EnsureSupplementalUI();
        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(HidePanel);
            closeButton.onClick.AddListener(HidePanel);
        }
        WireContextActions();
        WireReplacementConfirmation();
        if (upgradePanel != null)
            upgradePanel.SetActive(false);
    }

    private void Start()
    {
        if (upgradePanel != null)
            panelRect = upgradePanel.GetComponent<RectTransform>();
        if (panelRect != null)
        {
            targetAnchoredPos = panelRect.anchoredPosition;
            float width = panelRect.rect.width;
            hiddenAnchoredPos = targetAnchoredPos + new Vector2(width + offscreenPadding, 0f);
            panelRect.anchoredPosition = hiddenAnchoredPos;
            if (upgradePanel != null) upgradePanel.SetActive(false);
        }

        // Initial subscription left minimal here; subscriptions are ensured when showing the panel
    }

    private void Update()
    {
        if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;
        if (replacementConfirmationPanel != null && replacementConfirmationPanel.activeSelf)
            CloseReplacementConfirmation();
        else if (upgradePanel != null && upgradePanel.activeSelf)
            HidePanel();
    }

    private void OnDisable()
    {
        if (eventTileSystem != null)
            eventTileSystem.OnTileClicked -= HandleAnyTileClicked;
        eventTileSystem = null;
    }

    private bool HandleAnyTileClicked(int clickedTileIndex, Vector3 worldPos)
    {
        // Ignore if panel not visible
        if (upgradePanel == null || !upgradePanel.activeSelf) return false;
        // Ignore clicks over UI
        if (InputManager.Instance != null && InputManager.Instance.IsPointerOverUI()) return false;
        // If clicked tile is different than current, hide the panel
        if (clickedTileIndex != currentTileIndex)
        {
            HidePanel();
        }
        return false; // do not consume - allow other handlers
    }

    public void ShowUpgradePanel(ImprovementData improvement, int tileIndex, Civilization civ, int planetIndex = -1)
    {
        if (improvement == null || civ == null)
        {
            Debug.LogWarning("ImprovementUpgradeUI.ShowUpgradePanel called with null improvement or civ");
            return;
        }

        currentImprovement = improvement;
        currentTileIndex = tileIndex;
        currentPlanetIndex = planetIndex;
        currentCiv = civ;

        // Ensure we subscribe to the correct TileSystem for click-away behavior
        int subscribePlanet = currentPlanetIndex >= 0 ? currentPlanetIndex : (GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0);
        SubscribeToTileSystemForPlanet(subscribePlanet);

        if (improvementNameText != null)
            improvementNameText.text = improvement.improvementName;
        if (improvementIconImage != null)
        {
            improvementIconImage.sprite = improvement.GetIcon(civ);
            improvementIconImage.enabled = improvementIconImage.sprite != null;
        }

        PopulateUpgradeOptions();
        PopulateLaborTypeOptions();
        RefreshDismantleUI();
        RefreshContextActions();

        // Populate yields summary
        if (improvementYieldsText != null)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            if (improvement.foodPerTurn != 0) sb.AppendLine($"Food: {improvement.foodPerTurn}/turn");
            if (improvement.productionPerTurn != 0) sb.AppendLine($"Production: {improvement.productionPerTurn}/turn");
            if (improvement.goldPerTurn != 0) sb.AppendLine($"Gold: {improvement.goldPerTurn}/turn");
            if (improvement.sciencePerTurn != 0) sb.AppendLine($"Science: {improvement.sciencePerTurn}/turn");
            if (improvement.culturePerTurn != 0) sb.AppendLine($"Culture: {improvement.culturePerTurn}/turn");
            if (improvement.policyPointsPerTurn != 0) sb.AppendLine($"Policy: {improvement.policyPointsPerTurn}/turn");
            if (improvement.faithPerTurn != 0) sb.AppendLine($"Faith: {improvement.faithPerTurn}/turn");
            var yieldsStr = sb.Length > 0 ? sb.ToString().TrimEnd('\n','\r') : "No direct yields";
            improvementYieldsText.text = yieldsStr;
        }

        // If this improvement is a shelter, populate stored-unit buttons (if configured)
        if (improvement.isShelter)
        {
            var ts = TileSystem.GetForPlanet(currentPlanetIndex) ?? TileSystem.Instance;
            var tileData = ts != null ? ts.GetTileData(tileIndex) : null;
            GameObject instanceObj = tileData?.improvementInstanceObject;
            if (instanceObj == null)
            {
                ClearStoredUnitButtons();
                if (capacityText != null) capacityText.text = "Capacity: 0/0";
            }
            else
            {
                var impInstance = instanceObj.GetComponent<ImprovementInstance>();
                if (impInstance == null || impInstance.storedUnits == null || impInstance.storedUnits.Count == 0)
                {
                    ClearStoredUnitButtons();
                    if (capacityText != null) capacityText.text = $"Capacity: 0/{(impInstance!=null?impInstance.GetShelterCapacity():0)}";
                }
                else
                {
                    PopulateStoredUnitButtons(impInstance);
                }
            }
        }
        else
        {
            // Not a shelter: clear stored unit UI
            ClearStoredUnitButtons();
            if (capacityText != null) capacityText.text = "";
        }

        // If stored unit buttons are configured, show capacity text
        bool useButtons = (storedUnitsContainer != null && storedUnitButtonPrefab != null);
        if (capacityText != null)
            capacityText.gameObject.SetActive(useButtons);

        if (upgradePanel == null)
        {
            Debug.LogWarning("ImprovementUpgradeUI: upgradePanel reference is null. Cannot show panel.");
            return;
        }

        // Ensure panel RectTransform is known (Start may not have run yet)
        if (panelRect == null)
        {
            panelRect = upgradePanel.GetComponent<RectTransform>();
            if (panelRect != null)
            {
                targetAnchoredPos = panelRect.anchoredPosition;
                float width = panelRect.rect.width;
                hiddenAnchoredPos = targetAnchoredPos + new Vector2(width + offscreenPadding, 0f);
            }
        }

        // Activate and force-to-target so it's visible immediately; then animate in for polish
        upgradePanel.SetActive(true);
        if (panelRect != null)
        {
            panelRect.anchoredPosition = targetAnchoredPos; // snap into view to avoid offscreen layout issues
        }
        Debug.Log($"ImprovementUpgradeUI: Showing panel for '{improvement.improvementName}' tile={tileIndex} civ={(civ!=null?civ.civData.civName:"null")} planet={planetIndex}");
        StartSlideIn();
    }

    public void HidePanel()
    {
        // Start slide out and clear after it finishes
        StartSlideOut();
    }

    private void StartSlideIn()
    {
        if (panelRect == null) return;
        if (slideCoroutine != null) StopCoroutine(slideCoroutine);
        slideCoroutine = StartCoroutine(Slide(panelRect.anchoredPosition, targetAnchoredPos, slideDuration));
    }

    private void StartSlideOut()
    {
        if (panelRect == null)
        {
            // Fallback: immediate hide
            DoClearAndHide();
            return;
        }
        if (slideCoroutine != null) StopCoroutine(slideCoroutine);
        slideCoroutine = StartCoroutine(Slide(panelRect.anchoredPosition, hiddenAnchoredPos, slideDuration, DoClearAndHide));
    }

    private IEnumerator Slide(Vector2 from, Vector2 to, float duration, System.Action onComplete = null)
    {
        float t = 0f;
        panelRect.anchoredPosition = from;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float f = Mathf.Clamp01(t / duration);
            float ease = f * f * (3f - 2f * f);
            panelRect.anchoredPosition = Vector2.LerpUnclamped(from, to, ease);
            yield return null;
        }
        panelRect.anchoredPosition = to;
        slideCoroutine = null;
        onComplete?.Invoke();
    }

    private void DoClearAndHide()
    {
        CloseReplacementConfirmation();
        if (upgradePanel != null)
            upgradePanel.SetActive(false);
        // Unsubscribe from tile clicks when panel is hidden
        UnsubscribeFromTileSystem();
        ClearUpgradeButtons();
        currentImprovement = null;
        currentTileIndex = -1;
        currentPlanetIndex = -1;
        currentCiv = null;
    }

    private void SubscribeToTileSystemForPlanet(int planetIndex)
    {
        // If already subscribed to the correct planet, nothing to do
        if (eventTileSystem != null && eventPlanetIndex == planetIndex) return;
        UnsubscribeFromTileSystem();
        eventPlanetIndex = planetIndex;
        eventTileSystem = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (eventTileSystem != null)
            eventTileSystem.OnTileClicked += HandleAnyTileClicked;
    }

    private void UnsubscribeFromTileSystem()
    {
        if (eventTileSystem != null)
        {
            try { eventTileSystem.OnTileClicked -= HandleAnyTileClicked; } catch { }
            eventTileSystem = null;
            eventPlanetIndex = int.MinValue;
        }
    }

    private void PopulateUpgradeOptions()
    {
        ClearUpgradeButtons();

        if (currentImprovement == null || currentImprovement.availableUpgrades == null || currentImprovement.availableUpgrades.Length == 0)
        {
            if (emptyStateText != null)
            {
                emptyStateText.gameObject.SetActive(true);
                emptyStateText.text = "No options are available for this improvement.";
            }
            return;
        }
        if (emptyStateText != null) emptyStateText.gameObject.SetActive(false);

        var tileData = GetCurrentTileData();

        var orderedUpgrades = new List<ImprovementUpgradeData>(currentImprovement.availableUpgrades);
        orderedUpgrades.Sort((left, right) =>
        {
            int slotComparison = string.Compare(
                ImprovementUpgradeRules.GetDisplaySlot(left),
                ImprovementUpgradeRules.GetDisplaySlot(right),
                System.StringComparison.OrdinalIgnoreCase);
            return slotComparison != 0
                ? slotComparison
                : string.Compare(left?.upgradeName, right?.upgradeName, System.StringComparison.OrdinalIgnoreCase);
        });

        foreach (var slotGroup in orderedUpgrades.Where(upgrade => upgrade != null)
                     .GroupBy(ImprovementUpgradeRules.GetDisplaySlot))
        {
            Transform optionParent = upgradeButtonContainer;
            if (upgradeSlotSectionPrefab != null)
            {
                var sectionObject = Instantiate(upgradeSlotSectionPrefab, upgradeButtonContainer);
                upgradeSlotSections.Add(sectionObject);
                var section = sectionObject.GetComponent<ImprovementUpgradeSlotSection>();
                if (section != null)
                {
                    int installed = slotGroup.Count(upgrade =>
                        ImprovementUpgradeRules.Evaluate(currentImprovement, tileData, upgrade, currentCiv).Availability
                        == ImprovementUpgradeAvailability.Installed);
                    section.Bind(slotGroup.Key, installed, slotGroup.Count());
                    optionParent = section.OptionContainer;
                }
            }

            foreach (var upgrade in slotGroup)
            {
                var evaluation = ImprovementUpgradeRules.Evaluate(currentImprovement, tileData, upgrade, currentCiv);
                CreateUpgradeButton(upgrade, evaluation, optionParent);
            }
        }
    }

    private void PopulateLaborTypeOptions()
    {
        var mgr = ImprovementManager.Instance;
        if (currentImprovement == null || !currentImprovement.usesLaborTypes || mgr == null || mgr.allLaborTypes == null || mgr.allLaborTypes.Length == 0)
            return;

        var tileData = GetCurrentTileData();
        var instance = tileData?.improvementInstanceObject != null ? tileData.improvementInstanceObject.GetComponent<ImprovementInstance>() : null;

        Transform optionParent = upgradeButtonContainer;
        if (upgradeSlotSectionPrefab != null)
        {
            var sectionObject = Instantiate(upgradeSlotSectionPrefab, upgradeButtonContainer);
            upgradeSlotSections.Add(sectionObject);
            var section = sectionObject.GetComponent<ImprovementUpgradeSlotSection>();
            if (section != null)
            {
                int installed = instance != null && instance.currentLaborType != null ? 1 : 0;
                section.Bind("Labor", installed, mgr.allLaborTypes.Length);
                optionParent = section.OptionContainer;
            }
        }

        foreach (var laborType in mgr.allLaborTypes)
        {
            if (laborType == null) continue;
            var evaluation = ImprovementLaborRules.Evaluate(currentImprovement, instance, laborType, currentCiv);
            CreateLaborTypeButton(laborType, evaluation, optionParent);
        }
    }

    private void CreateLaborTypeButton(LaborTypeData laborType, ImprovementUpgradeEvaluation evaluation, Transform parent)
    {
        if (upgradeButtonPrefab == null || upgradeButtonContainer == null) return;

        var buttonObj = Instantiate(upgradeButtonPrefab, parent);
        upgradeButtons.Add(buttonObj);

        var button = buttonObj.GetComponent<Button>();
        var nameText = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
        var icon = buttonObj.GetComponentInChildren<Image>();

        if (nameText != null)
        {
            int cost = ImprovementLaborRules.GetSwitchCost(laborType);
            string costText = cost > 0 ? $"Gold: {cost}" : "Free";
            string status = string.IsNullOrEmpty(evaluation.Reason) ? string.Empty : $"\n{evaluation.Reason}";
            nameText.text = $"Labor — {laborType.laborName}\n{costText}{status}";
        }

        if (icon != null && laborType.icon != null)
            icon.sprite = laborType.icon;

        if (button != null)
        {
            button.interactable = evaluation.IsInteractable;
            button.onClick.AddListener(() => OnLaborTypeSelected(laborType));
        }

        var buttonImage = buttonObj.GetComponent<Image>();
        if (buttonImage != null)
            buttonImage.color = evaluation.IsInteractable ? Color.white : Color.gray;
    }

    private void OnLaborTypeSelected(LaborTypeData laborType)
    {
        if (laborType == null || currentCiv == null || currentTileIndex < 0) return;
        if (ImprovementManager.Instance == null) return;

        if (!ImprovementManager.Instance.TryAssignLaborType(currentTileIndex, currentPlanetIndex, currentCiv, laborType, out string reason))
        {
            if (!string.IsNullOrEmpty(reason) && UIManager.Instance != null)
                UIManager.Instance.ShowNotification(reason);
            return;
        }

        PopulateUpgradeOptions();
        PopulateLaborTypeOptions();
    }

    private void RefreshDismantleUI()
    {
        bool showDismantle = currentImprovement != null
            && currentImprovement.canBeDismantled
            && currentCiv != null
            && ImprovementManager.Instance != null;
        bool canDismantle = showDismantle;
        string blockedReason = string.Empty;
        if (canDismantle)
            canDismantle = ImprovementManager.Instance.CanDismantleImprovement(currentTileIndex, currentCiv, currentPlanetIndex, out blockedReason);

        if (dismantleButton != null)
        {
            dismantleButton.gameObject.SetActive(showDismantle);
            dismantleButton.interactable = canDismantle;
            dismantleButton.onClick.RemoveAllListeners();
            if (canDismantle)
                dismantleButton.onClick.AddListener(OnDismantleClicked);
        }

        if (dismantleRefundText != null)
        {
            dismantleRefundText.gameObject.SetActive(showDismantle);
            dismantleRefundText.text = !showDismantle
                ? string.Empty
                : canDismantle ? BuildDismantleRefundText(currentImprovement) : blockedReason;
        }
    }

    private string BuildDismantleRefundText(ImprovementData improvement)
    {
        if (improvement == null) return string.Empty;
        List<string> parts = new List<string>();
        if (improvement.dismantleGoldRefund > 0)
            parts.Add($"Gold: {improvement.dismantleGoldRefund}");
        if (improvement.dismantleResourceRefunds != null)
        {
            foreach (var cost in improvement.dismantleResourceRefunds)
            {
                if (cost == null || cost.resource == null || cost.amount <= 0) continue;
                parts.Add($"{cost.resource.resourceName}: {cost.amount}");
            }
        }
        return parts.Count > 0 ? "Dismantle Refund\n" + string.Join("\n", parts) : "Dismantle Refund\nNone";
    }

    private void OnDismantleClicked()
    {
        if (currentImprovement == null || currentCiv == null || currentTileIndex < 0) return;
        if (ImprovementManager.Instance == null) return;

        if (ImprovementManager.Instance.DismantleImprovement(currentTileIndex, currentCiv, currentPlanetIndex))
            HidePanel();
    }

    private void CreateUpgradeButton(ImprovementUpgradeData upgrade, ImprovementUpgradeEvaluation evaluation, Transform parent)
    {
        if (upgradeButtonPrefab == null || upgradeButtonContainer == null) return;

        var buttonObj = Instantiate(upgradeButtonPrefab, parent);
        upgradeButtons.Add(buttonObj);

        var optionRow = buttonObj.GetComponent<ImprovementUpgradeOptionRow>();
        if (optionRow != null)
        {
            optionRow.Bind(upgrade, evaluation, () => OnUpgradeSelected(upgrade));
            return;
        }

        var legacyButton = buttonObj.GetComponent<UpgradeButton>();
        if (legacyButton != null)
        {
            legacyButton.Setup(upgrade, () => OnUpgradeSelected(upgrade), evaluation);
            return;
        }

        // Compatibility fallback for the existing button prefab until the new row asset is wired.
        {
            // Fallback: manual wiring if prefab doesn't have UpgradeButton
            var button = buttonObj.GetComponent<Button>();
            var nameText = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            var icon = buttonObj.GetComponentInChildren<Image>();

            if (nameText != null)
            {
                string costText = $"Gold: {upgrade.goldCost}";
                string resourceText = ResourceCost.FormatCosts(upgrade.resourceCosts, upgrade.hasSubstituteCosts);
                if (!string.IsNullOrEmpty(resourceText))
                    costText += $"\n{resourceText}";
                string status = string.IsNullOrEmpty(evaluation.Reason) ? string.Empty : $"\n{evaluation.Reason}";
                nameText.text = $"{ImprovementUpgradeRules.GetDisplaySlot(upgrade)} — {upgrade.upgradeName}\n{costText}{status}";
            }

            if (icon != null && upgrade.icon != null)
                icon.sprite = upgrade.icon;

            if (button != null)
            {
                button.interactable = evaluation.IsInteractable;
                button.onClick.AddListener(() => OnUpgradeSelected(upgrade));
            }

            var buttonImage = buttonObj.GetComponent<Image>();
            if (buttonImage != null)
            {
                buttonImage.color = evaluation.IsInteractable ? Color.white : Color.gray;
            }
        }
    }

    private void OnUpgradeSelected(ImprovementUpgradeData upgrade)
    {
        if (upgrade == null || currentCiv == null || currentTileIndex < 0) return;

        string reason;
        if (!ImprovementUpgradeRules.CanApplyUpgrade(currentImprovement, GetCurrentTileData(), upgrade, currentCiv, out reason))
        {
            if (!string.IsNullOrEmpty(reason) && UIManager.Instance != null)
                UIManager.Instance.ShowNotification(reason);
            return;
        }

        var replacements = ImprovementUpgradeRules.GetSupersededUpgradeKeys(currentImprovement, GetCurrentTileData(), upgrade);
        if (replacements.Count > 0 && replacementConfirmationPanel != null)
        {
            pendingUpgrade = upgrade;
            replacementConfirmationPanel.SetActive(true);
            if (replacementConfirmationText != null)
            {
                var replacedNames = currentImprovement.availableUpgrades
                    .Where(candidate => candidate != null && replacements.Contains(ImprovementUpgradeRules.GetKey(candidate)))
                    .Select(candidate => candidate.upgradeName);
                replacementConfirmationText.text =
                    $"Replace {string.Join(", ", replacedNames)} with {upgrade.upgradeName}?\nThe full listed cost will be charged.";
            }
            if (cancelReplacementButton != null) EventSystem.current?.SetSelectedGameObject(cancelReplacementButton.gameObject);
            return;
        }

        PurchaseUpgrade(upgrade);
    }

    private void PurchaseUpgrade(ImprovementUpgradeData upgrade)
    {
        string reason;

        if (ImprovementManager.Instance == null)
        {
            UIManager.Instance?.ShowNotification("The improvement system is unavailable.");
            return;
        }
        if (ImprovementManager.Instance.TryPurchaseAndApplyUpgrade(
                currentTileIndex, currentPlanetIndex, currentCiv, upgrade, out reason))
        {
            ShowUpgradePanel(currentImprovement, currentTileIndex, currentCiv, currentPlanetIndex);
        }
        else if (!string.IsNullOrEmpty(reason))
        {
            UIManager.Instance?.ShowNotification(reason);
        }
    }

    private void WireContextActions()
    {
        if (manageSpecialistsButton != null)
        {
            manageSpecialistsButton.onClick.RemoveAllListeners();
            manageSpecialistsButton.onClick.AddListener(OpenCitizenAssignment);
        }
        if (openCityStorageButton != null)
        {
            openCityStorageButton.onClick.RemoveAllListeners();
            openCityStorageButton.onClick.AddListener(OpenCityStorage);
        }
    }

    private void WireReplacementConfirmation()
    {
        if (replacementConfirmationPanel != null) replacementConfirmationPanel.SetActive(false);
        if (confirmReplacementButton != null)
        {
            confirmReplacementButton.onClick.RemoveAllListeners();
            confirmReplacementButton.onClick.AddListener(() =>
            {
                var upgrade = pendingUpgrade;
                CloseReplacementConfirmation();
                if (upgrade != null) PurchaseUpgrade(upgrade);
            });
        }
        if (cancelReplacementButton != null)
        {
            cancelReplacementButton.onClick.RemoveAllListeners();
            cancelReplacementButton.onClick.AddListener(CloseReplacementConfirmation);
        }
    }

    private void CloseReplacementConfirmation()
    {
        pendingUpgrade = null;
        if (replacementConfirmationPanel != null) replacementConfirmationPanel.SetActive(false);
    }

    private void RefreshContextActions()
    {
        City city = FindOwningCity();
        bool hasSpecialistSlots = false;
        var instance = GetCurrentTileData()?.improvementInstanceObject?.GetComponent<ImprovementInstance>();
        if (instance != null) hasSpecialistSlots = instance.GetActiveRuralSpecialistSlots().Any(slot => slot != null);
        if (manageSpecialistsButton != null)
        {
            manageSpecialistsButton.gameObject.SetActive(currentImprovement != null);
            manageSpecialistsButton.interactable = city != null && hasSpecialistSlots;
            BindTooltip(manageSpecialistsButton, "Manage Specialists", city == null
                ? "No city administers this improvement."
                : hasSpecialistSlots ? "Open citizen assignment focused on this improvement." : "This improvement has no rural specialist slots.");
        }
        if (openCityStorageButton != null)
        {
            bool hasStorage = currentImprovement != null && (currentImprovement.storageTypes != ImprovementStorageType.None ||
                currentImprovement.isShelter || currentImprovement.isMissileSilo);
            openCityStorageButton.gameObject.SetActive(currentImprovement != null);
            openCityStorageButton.interactable = city != null && hasStorage;
            BindTooltip(openCityStorageButton, "Open City Storage", city == null
                ? "No city administers this improvement."
                : hasStorage ? "Open the administering city's Unit Storage tab." : "This improvement has no supported storage capability.");
        }
    }

    private City FindOwningCity()
    {
        return ImprovementManager.Instance?.FindAdministeringCity(currentCiv, currentTileIndex, currentPlanetIndex);
    }

    private void OpenCitizenAssignment()
    {
        City city = FindOwningCity();
        if (city == null) return;
        int tileIndex = currentTileIndex;
        HidePanel();
        CityTileOverlayController.Instance?.EnterCityAssignmentMode(city, tileIndex);
    }

    private void BindTooltip(Button button, string title, string description)
    {
        if (button == null) return;
        var tooltip = button.GetComponent<SimpleTooltipTarget>() ?? button.gameObject.AddComponent<SimpleTooltipTarget>();
        tooltip.Bind(title, description);
    }

    private void OpenCityStorage()
    {
        City city = FindOwningCity();
        if (city == null) return;
        HidePanel();
        var cityUI = FindAnyObjectByType<CityUI>(FindObjectsInactive.Include);
        cityUI?.ShowForCityTab(city, CityUITab.UnitStorage);
    }

    private void EnsureSupplementalUI()
    {
        if (upgradePanel == null) return;
        var parent = upgradePanel.transform;
        improvementIconImage ??= CreatePanelImage(parent, "Improvement Icon", new Vector2(0f, 1f), new Vector2(18f, -18f), new Vector2(72f, 72f));
        emptyStateText ??= CreatePanelText(parent, "Empty State", "No options are available for this improvement.",
            new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(420f, 48f), 16f, TextAlignmentOptions.Center);
        emptyStateText.gameObject.SetActive(false);

        manageSpecialistsButton ??= CreatePanelButton(parent, "Manage Specialists", new Vector2(1f, 0f), new Vector2(-390f, 18f), new Vector2(170f, 34f));
        openCityStorageButton ??= CreatePanelButton(parent, "Open City Storage", new Vector2(1f, 0f), new Vector2(-210f, 18f), new Vector2(170f, 34f));
        dismantleButton ??= CreatePanelButton(parent, "Dismantle", new Vector2(0f, 0f), new Vector2(18f, 18f), new Vector2(130f, 34f));
        dismantleRefundText ??= CreatePanelText(parent, "Dismantle Status", string.Empty,
            new Vector2(0f, 0f), new Vector2(155f, 18f), new Vector2(250f, 42f), 12f, TextAlignmentOptions.Left);

        if (replacementConfirmationPanel == null)
        {
            replacementConfirmationPanel = new GameObject("Replacement Confirmation", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            replacementConfirmationPanel.transform.SetParent(parent, false);
            var rect = (RectTransform)replacementConfirmationPanel.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            replacementConfirmationPanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);
            var dialog = new GameObject("Dialog", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            dialog.transform.SetParent(rect, false);
            var dialogRect = (RectTransform)dialog.transform;
            dialogRect.anchorMin = dialogRect.anchorMax = new Vector2(0.5f, 0.5f);
            dialogRect.sizeDelta = new Vector2(460f, 170f);
            dialog.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.08f, 0.98f);
            replacementConfirmationText = CreatePanelText(dialogRect, "Message", "Confirm replacement?",
                new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(420f, 80f), 17f, TextAlignmentOptions.Center);
            confirmReplacementButton = CreatePanelButton(dialogRect, "Confirm", new Vector2(0.5f, 0f), new Vector2(-80f, 18f), new Vector2(140f, 36f));
            cancelReplacementButton = CreatePanelButton(dialogRect, "Cancel", new Vector2(0.5f, 0f), new Vector2(80f, 18f), new Vector2(140f, 36f));
        }
    }

    private Button CreatePanelButton(Transform parent, string label, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var child = new GameObject(label + " Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        child.transform.SetParent(parent, false);
        var rect = (RectTransform)child.transform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var image = child.GetComponent<Image>();
        image.color = new Color(0.72f, 0.67f, 0.52f, 0.96f);
        var button = child.GetComponent<Button>();
        button.targetGraphic = image;
        var text = CreatePanelText(rect, "Label", label, new Vector2(0.5f, 0.5f), Vector2.zero, size, 14f, TextAlignmentOptions.Center);
        text.color = Color.black;
        return button;
    }

    private Image CreatePanelImage(Transform parent, string objectName, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var child = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        child.transform.SetParent(parent, false);
        var rect = (RectTransform)child.transform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var image = child.GetComponent<Image>();
        image.preserveAspect = true;
        return image;
    }

    private TextMeshProUGUI CreatePanelText(Transform parent, string objectName, string value, Vector2 anchor,
        Vector2 position, Vector2 size, float fontSize, TextAlignmentOptions alignment)
    {
        var child = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        child.transform.SetParent(parent, false);
        var rect = (RectTransform)child.transform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var text = child.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        return text;
    }

    private HexTileData GetCurrentTileData()
    {
        if (currentPlanetIndex < 0) currentPlanetIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        var ts = TileSystem.GetForPlanet(currentPlanetIndex) ?? TileSystem.Instance;
        return ts != null ? ts.GetTileData(currentTileIndex) : null;
    }

    private void ClearUpgradeButtons()
    {
        foreach (var button in upgradeButtons)
        {
            if (button != null)
                Destroy(button);
        }
        upgradeButtons.Clear();
        foreach (var section in upgradeSlotSections)
        {
            if (section != null) Destroy(section);
        }
        upgradeSlotSections.Clear();
    }

    private void ClearStoredUnitButtons()
    {
        foreach (var b in storedUnitButtons)
        {
            if (b != null) Destroy(b);
        }
        storedUnitButtons.Clear();
    }

    private void PopulateStoredUnitButtons(ImprovementInstance impInstance)
    {
        ClearStoredUnitButtons();
        if (storedUnitsContainer == null || storedUnitButtonPrefab == null || impInstance == null || impInstance.storedUnits == null) return;
        foreach (var unit in impInstance.storedUnits)
        {
            if (unit == null) continue;
            var go = Instantiate(storedUnitButtonPrefab, storedUnitsContainer);
            storedUnitButtons.Add(go);
            var storedBtn = go.GetComponent<StoredUnitButton>();
            if (storedBtn != null)
            {
                storedBtn.Setup(unit, impInstance);
            }
            else
            {
                // Fallback: wire a simple button if the prefab doesn't have the StoredUnitButton script
                var btn = go.GetComponent<UnityEngine.UI.Button>();
                var txt = go.GetComponentInChildren<TextMeshProUGUI>();
                if (txt != null)
                {
                    string name = "Unit";
                    var cu = unit as CombatUnit;
                    var wu = unit as WorkerUnit;
                    if (cu != null && cu.data != null) name = cu.data.unitName;
                    else if (wu != null && wu.data != null) name = wu.data.unitName;
                    txt.text = name;
                }
                if (btn != null)
                {
                    btn.onClick.AddListener(() => { impInstance.TryUnstoreUnit(unit); PopulateStoredUnitButtons(impInstance); });
                }
            }
        }

        // Update capacity text
        if (capacityText != null)
        {
            int current = impInstance.storedUnits != null ? impInstance.storedUnits.Count : 0;
            int cap = impInstance.GetShelterCapacity();
            capacityText.text = $"Capacity: {current}/{cap}";
        }
    }

    // Public helper for StoredUnitButton to request a refresh after unstoring
    public void RefreshStoredUnits(ImprovementInstance impInstance)
    {
        PopulateStoredUnitButtons(impInstance);
        // Also update shelteredUnitsText visibility/capacity label
        if (impInstance != null)
        {
            if (capacityText != null)
            {
                int current = impInstance.storedUnits != null ? impInstance.storedUnits.Count : 0;
                int cap = impInstance.GetShelterCapacity();
                capacityText.text = $"Capacity: {current}/{cap}";
            }
        }
        else
        {
            ClearStoredUnitButtons();
            if (capacityText != null) capacityText.text = "Capacity: 0/0";
        }
    }

    private void OnDestroy()
    {
        // Ensure we clean up any tile subscriptions
        UnsubscribeFromTileSystem();
    }
}
