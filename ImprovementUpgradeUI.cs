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
        // Spawn upgrade prefab if available
        if (upgrade.upgradePrefab != null)
        {
            Vector3 position = TileDataHelper.Instance.GetTileCenter(currentTileIndex);
            // Offset slightly to avoid z-fighting
            position.y += 0.1f;
            Instantiate(upgrade.upgradePrefab, position, Quaternion.identity);
        }

        // Store upgrade in tile data for persistence
        var (tileData, isMoonTile) = TileDataHelper.Instance.GetTileData(currentTileIndex);
        if (tileData != null)
        {
            // Add to the list of built upgrades
            if (tileData.builtUpgrades == null)
                tileData.builtUpgrades = new System.Collections.Generic.List<string>();
            
            if (!tileData.builtUpgrades.Contains(upgrade.upgradeName))
                tileData.builtUpgrades.Add(upgrade.upgradeName);
            
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
