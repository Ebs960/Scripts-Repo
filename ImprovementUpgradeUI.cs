// Assets/Scripts/UI/ImprovementUpgradeUI.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

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
    private Civilization currentCiv;
    private List<GameObject> upgradeButtons = new List<GameObject>();

    private void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(HidePanel);
        
        if (upgradePanel != null)
            upgradePanel.SetActive(false);
    }

    public void ShowUpgradePanel(ImprovementData improvement, int tileIndex, Civilization civ)
    {
        if (improvement == null || civ == null) return;

        currentImprovement = improvement;
        currentTileIndex = tileIndex;
        currentCiv = civ;

        if (improvementNameText != null)
            improvementNameText.text = improvement.improvementName;

        PopulateUpgradeOptions();

        if (upgradePanel != null)
            upgradePanel.SetActive(true);
    }

    public void HidePanel()
    {
        if (upgradePanel != null)
            upgradePanel.SetActive(false);
        
        ClearUpgradeButtons();
        currentImprovement = null;
        currentTileIndex = -1;
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
            Debug.Log($"Cannot build {upgrade.upgradeName}: requirements not met");
            return;
        }

        // Consume requirements
        if (upgrade.ConsumeRequirements(currentCiv))
        {
            // Build the upgrade
            BuildUpgrade(upgrade);
            Debug.Log($"Built {upgrade.upgradeName} on {currentImprovement.improvementName}");
            
            // Refresh the panel
            PopulateUpgradeOptions();
        }
    }

    private void BuildUpgrade(ImprovementUpgradeData upgrade)
    {
        // Apply visual changes on the instantiated improvement when requested
        var (tileData, isMoonTile) = TileDataHelper.Instance.GetTileData(currentTileIndex);
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

                    // Ensure click handler
                    var ch = newObj.GetComponent<ImprovementClickHandler>();
                    if (ch == null) ch = newObj.AddComponent<ImprovementClickHandler>();
                    ch.Initialize(currentTileIndex, tileData.improvement);

                    // Replace reference on tile data
                    tileData.improvementInstanceObject = newObj;
                    TileDataHelper.Instance.SetTileData(currentTileIndex, tileData);

                    // Destroy old instance
                    Destroy(instanceObj);
                    instanceObj = newObj;
                    impInstance = newInst;
                }
                else if (upgrade.attachPrefabs != null && upgrade.attachPrefabs.Length > 0)
                {
                    foreach (var prefab in upgrade.attachPrefabs)
                    {
                        if (prefab == null) continue;
                        // Avoid duplicating identical attachment by name
                        bool already = false;
                        foreach (var child in impInstance.attachedParts)
                        {
                            if (child != null && child.name.Contains(prefab.name)) { already = true; break; }
                        }
                        if (already) continue;

                        var go = Instantiate(prefab, instanceObj.transform);
                        go.transform.localPosition = Vector3.zero;
                        go.transform.localRotation = Quaternion.identity;
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
            // Recompute aggregated defense modifiers and persist
            tileData.RecomputeImprovementDefenseAggregates();
            TileDataHelper.Instance.SetTileData(currentTileIndex, tileData);
            Debug.Log($"Upgrade {upgrade.upgradeName} built on tile {currentTileIndex}");
        }

        // Apply immediate yield bonuses to the civilization
        // Note: For per-turn yields, you'd want to track this in the tile data
        // and apply during yield calculation
    }

    private bool HasUpgrade(ImprovementUpgradeData upgrade)
    {
        // Check if this upgrade has already been built on this tile
        var (tileData, _) = TileDataHelper.Instance.GetTileData(currentTileIndex);
        if (tileData?.builtUpgrades == null) return false;
        
        return tileData.builtUpgrades.Contains(upgrade.upgradeName);
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
