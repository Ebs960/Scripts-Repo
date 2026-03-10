// Assets/Scripts/UI/ImprovementUpgradeUI.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ImprovementUpgradeUI : MonoBehaviour
{
    [Header("Panel References")]
    [SerializeField] private GameObject upgradePanel;
    [SerializeField] private TextMeshProUGUI improvementNameText;
    [SerializeField] private Transform upgradeButtonContainer;
    [SerializeField] private GameObject upgradeButtonPrefab;
    [SerializeField] private Button closeButton;

    private ImprovementData currentImprovement;
    private int currentTileIndex = -1;
    private int currentPlanetIndex = -1;
    private Civilization currentCiv;
    private List<GameObject> upgradeButtons = new List<GameObject>();

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
        if (closeButton != null)
            closeButton.onClick.AddListener(HidePanel);
        
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

        // Subscribe to TileSystem clicks so clicking away will hide the panel
        int desiredPlanet = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        eventPlanetIndex = desiredPlanet;
        eventTileSystem = TileSystem.GetForPlanet(desiredPlanet) ?? TileSystem.Instance;
        if (eventTileSystem != null)
            eventTileSystem.OnTileClicked += HandleAnyTileClicked;
    }

    private void OnDisable()
    {
        if (eventTileSystem != null)
            eventTileSystem.OnTileClicked -= HandleAnyTileClicked;
        eventTileSystem = null;
    }

    private void HandleAnyTileClicked(int clickedTileIndex, Vector3 worldPos)
    {
        // Ignore if panel not visible
        if (upgradePanel == null || !upgradePanel.activeSelf) return;
        // Ignore clicks over UI
        if (InputManager.Instance != null && InputManager.Instance.IsPointerOverUI()) return;
        // If clicked tile is different than current, hide the panel
        if (clickedTileIndex != currentTileIndex)
        {
            HidePanel();
        }
    }

    public void ShowUpgradePanel(ImprovementData improvement, int tileIndex, Civilization civ, int planetIndex = -1)
    {
        if (improvement == null || civ == null) return;

        currentImprovement = improvement;
        currentTileIndex = tileIndex;
        currentPlanetIndex = planetIndex;
        currentCiv = civ;

        if (improvementNameText != null)
            improvementNameText.text = improvement.improvementName;

        PopulateUpgradeOptions();

        if (upgradePanel != null)
        {
            upgradePanel.SetActive(true);
            StartSlideIn();
        }
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
        if (upgradePanel != null)
            upgradePanel.SetActive(false);

        ClearUpgradeButtons();
        currentImprovement = null;
        currentTileIndex = -1;
        currentPlanetIndex = -1;
        currentCiv = null;
    }

    private void PopulateUpgradeOptions()
    {
        ClearUpgradeButtons();

        if (currentImprovement == null || currentImprovement.availableUpgrades == null)
            return;

        foreach (var upgrade in currentImprovement.availableUpgrades)
        {
            if (upgrade == null) continue;

            // Check if already built (if unique)
            if (upgrade.uniqueUpgrade && HasUpgrade(upgrade))
                continue;

            CreateUpgradeButton(upgrade);
        }
    }

    private void CreateUpgradeButton(ImprovementUpgradeData upgrade)
    {
        if (upgradeButtonPrefab == null || upgradeButtonContainer == null) return;

        var buttonObj = Instantiate(upgradeButtonPrefab, upgradeButtonContainer);
        upgradeButtons.Add(buttonObj);

        // Setup button components
        var button = buttonObj.GetComponent<Button>();
        var nameText = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
        var icon = buttonObj.GetComponentInChildren<Image>();

        if (nameText != null)
        {
            string costText = $"Gold: {upgrade.goldCost}";
            if (upgrade.resourceCosts != null)
            {
                foreach (var cost in upgrade.resourceCosts)
                {
                    if (cost.resource != null)
                        costText += $"\n{cost.resource.resourceName}: {cost.amount}";
                }
            }
            nameText.text = $"{upgrade.upgradeName}\n{costText}";
        }

        if (icon != null && upgrade.icon != null)
            icon.sprite = upgrade.icon;

        // Check if can build and set button state
        bool canBuild = upgrade.CanBuild(currentCiv);
        if (button != null)
        {
            button.interactable = canBuild;
            button.onClick.AddListener(() => OnUpgradeSelected(upgrade));
        }

        // Visual feedback for buildable state
        var buttonImage = buttonObj.GetComponent<Image>();
        if (buttonImage != null)
        {
            buttonImage.color = canBuild ? Color.white : Color.gray;
        }
    }

    private void OnUpgradeSelected(ImprovementUpgradeData upgrade)
    {
        if (upgrade == null || currentCiv == null || currentTileIndex < 0) return;

        if (!upgrade.CanBuild(currentCiv))
        {
return;
        }

        // Consume requirements
        if (upgrade.ConsumeRequirements(currentCiv))
        {
            // Build the upgrade
            BuildUpgrade(upgrade);
// Refresh the panel
            PopulateUpgradeOptions();
        }
    }

    private void BuildUpgrade(ImprovementUpgradeData upgrade)
    {
        // Apply visual changes on the instantiated improvement when requested
        if (currentPlanetIndex < 0) currentPlanetIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        var ts = TileSystem.GetForPlanet(currentPlanetIndex) ?? TileSystem.Instance;
        var tileData = ts != null ? ts.GetTileData(currentTileIndex) : null;
        GameObject instanceObj = tileData?.improvementInstanceObject;

        if (upgrade.makesVisualChange && instanceObj != null)
        {
            var impInstance = instanceObj.GetComponent<ImprovementInstance>();
            if (impInstance == null)
                impInstance = instanceObj.AddComponent<ImprovementInstance>();

            // Use upgradeId if provided, otherwise fallback to upgradeName
            string upgradeKey = !string.IsNullOrEmpty(upgrade.upgradeId) ? upgrade.upgradeId : upgrade.upgradeName;

            // If already applied on this runtime instance, skip
            if (!impInstance.HasApplied(upgradeKey))
            {
                // Replace the whole improvement object if a replacePrefab is defined
                if (upgrade.replacePrefab != null)
                {
                    Vector3 pos = instanceObj.transform.position;
                    Quaternion rot = instanceObj.transform.rotation;
                    // Instantiate replacement
                    var newObj = Instantiate(upgrade.replacePrefab, pos, rot);
                    // Transfer ImprovementInstance state
                    var newInst = newObj.GetComponent<ImprovementInstance>();
                    if (newInst == null) newInst = newObj.AddComponent<ImprovementInstance>();
                    newInst.tileIndex = impInstance.tileIndex;
                    newInst.data = impInstance.data;
                    newInst.appliedUpgrades = new System.Collections.Generic.HashSet<string>(impInstance.appliedUpgrades);

                    // Initialize the ImprovementInstance on the replacement object
                    newInst.Initialize(currentTileIndex, tileData.improvement, currentPlanetIndex);
                    // Preserve runtime ownership and transfer attached parts
                    newInst.owner = impInstance.owner;
                    if (impInstance.attachedParts != null && impInstance.attachedParts.Count > 0)
                    {
                        newInst.attachedParts = new System.Collections.Generic.List<GameObject>();
                        foreach (var child in impInstance.attachedParts)
                        {
                            if (child == null) continue;
                            // Reparent child into the new instance so it survives the destroy
                            child.transform.SetParent(newObj.transform, true);
                            newInst.attachedParts.Add(child);
                        }
                    }

                    // Replace reference on tile data
                    tileData.improvementInstanceObject = newObj;
                    ts?.SetTileData(currentTileIndex, tileData);

                    // Destroy old instance (attached parts already reparented)
                    Destroy(instanceObj);
                    instanceObj = newObj;
                    impInstance = newInst;
                }
                else if (upgrade.attachPrefabs != null && upgrade.attachPrefabs.Length > 0)
                {
                    for (int i = 0; i < upgrade.attachPrefabs.Length; i++)
                    {
                        var prefab = upgrade.attachPrefabs[i];
                        if (prefab == null) continue;
                        // Avoid duplicating identical attachment by name
                        bool already = false;
                        if (impInstance.attachedParts != null)
                        {
                            foreach (var child in impInstance.attachedParts)
                            {
                                if (child != null && child.name.Contains(prefab.name)) { already = true; break; }
                            }
                        }
                        if (already) continue;

                        var go = Instantiate(prefab, instanceObj.transform);

                        // Apply configured local position/rotation if provided
                        Vector3 localPos = Vector3.zero;
                        Quaternion localRot = Quaternion.identity;
                        if (upgrade.attachLocalPositions != null && i < upgrade.attachLocalPositions.Length)
                            localPos = upgrade.attachLocalPositions[i];
                        if (upgrade.attachLocalEulerAngles != null && i < upgrade.attachLocalEulerAngles.Length)
                            localRot = Quaternion.Euler(upgrade.attachLocalEulerAngles[i]);

                        go.transform.localPosition = localPos;
                        go.transform.localRotation = localRot;
                        if (impInstance.attachedParts == null) impInstance.attachedParts = new System.Collections.Generic.List<GameObject>();
                        impInstance.attachedParts.Add(go);
                    }
                }

                // Mark upgrade applied on runtime instance
                impInstance.MarkApplied(upgradeKey);
            }
        }
        else
        {
            // No runtime improvement instance available to apply visuals to.
            // We no longer support spawning standalone upgrade prefabs; log and return.
            Debug.LogWarning($"Upgrade {upgrade.upgradeName} requires an instantiated improvement on tile {currentTileIndex} to apply visuals. No action taken.");
            return;
        }

        // Store upgrade in tile data for persistence
        if (tileData != null)
        {
            if (tileData.builtUpgrades == null)
                tileData.builtUpgrades = new System.Collections.Generic.List<string>();

            string keyToPersist = !string.IsNullOrEmpty(upgrade.upgradeId) ? upgrade.upgradeId : upgrade.upgradeName;
            if (!tileData.builtUpgrades.Contains(keyToPersist))
                tileData.builtUpgrades.Add(keyToPersist);
            Debug.Log($"Applied improvement upgrade '{keyToPersist}' to tile {currentTileIndex}");
            // Recompute aggregated defense modifiers and persist
            tileData.RecomputeImprovementDefenseAggregates();
            ts?.SetTileData(currentTileIndex, tileData);
}

        // Apply immediate yield bonuses to the civilization
        // Note: For per-turn yields, you'd want to track this in the tile data
        // and apply during yield calculation
    }

    private bool HasUpgrade(ImprovementUpgradeData upgrade)
    {
        // Check if this upgrade has already been built on this tile using the same key logic used when persisting
        if (upgrade == null) return false;
        if (currentPlanetIndex < 0) currentPlanetIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        var ts = TileSystem.GetForPlanet(currentPlanetIndex) ?? TileSystem.Instance;
        var tileData = ts != null ? ts.GetTileData(currentTileIndex) : null;
        if (tileData?.builtUpgrades == null) return false;

        string key = !string.IsNullOrEmpty(upgrade.upgradeId) ? upgrade.upgradeId : upgrade.upgradeName;
        return tileData.builtUpgrades.Contains(key);
    }

    private void ClearUpgradeButtons()
    {
        foreach (var button in upgradeButtons)
        {
            if (button != null)
                Destroy(button);
        }
        upgradeButtons.Clear();
    }

    private void OnDestroy()
    {
        if (closeButton != null)
            closeButton.onClick.RemoveListener(HidePanel);
    }
}
