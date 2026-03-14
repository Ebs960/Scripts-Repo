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
    [SerializeField] private TextMeshProUGUI improvementYieldsText;
    [Header("Stored Units UI")]
    [SerializeField] private Transform storedUnitsContainer;
    [SerializeField] private GameObject storedUnitButtonPrefab;
    [SerializeField] private TextMeshProUGUI capacityText;
        
    [SerializeField] private Transform upgradeButtonContainer;
    [SerializeField] private GameObject upgradeButtonPrefab;
    // close button removed: panel will close on click-away like UnitInfoPanel

    private ImprovementData currentImprovement;
    private int currentTileIndex = -1;
    private int currentPlanetIndex = -1;
    private Civilization currentCiv;
    private List<GameObject> upgradeButtons = new List<GameObject>();
    private List<GameObject> storedUnitButtons = new List<GameObject>();

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

        PopulateUpgradeOptions();

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

        // Prefer UpgradeButton component on prefab for prefab-driven wiring
        bool canBuild = upgrade.CanBuild(currentCiv);
        Component prefabComp = null;
        // Try to find a component named "UpgradeButton" without requiring a compile-time reference
        foreach (var mb in buttonObj.GetComponents<MonoBehaviour>())
        {
            if (mb == null) continue;
            if (mb.GetType().Name == "UpgradeButton")
            {
                prefabComp = mb;
                break;
            }
        }

        if (prefabComp != null)
        {
            var setupMethod = prefabComp.GetType().GetMethod("Setup", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (setupMethod != null)
            {
                setupMethod.Invoke(prefabComp, new object[] { upgrade, (System.Action)(() => OnUpgradeSelected(upgrade)), canBuild });
            }
        }
        else
        {
            // Fallback: manual wiring if prefab doesn't have UpgradeButton
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

            if (button != null)
            {
                button.interactable = canBuild;
                button.onClick.AddListener(() => OnUpgradeSelected(upgrade));
            }

            var buttonImage = buttonObj.GetComponent<Image>();
            if (buttonImage != null)
            {
                buttonImage.color = canBuild ? Color.white : Color.gray;
            }
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

        // Apply immediate yield bonuses to the civilization so UI and civ pools update instantly.
        var impMgr = ImprovementManager.Instance;
        if (impMgr != null && tileData != null)
        {
            var owner = tileData.improvementOwner;
            if (owner != null)
                impMgr.ApplyImprovementYieldsForTile(currentTileIndex, owner, currentPlanetIndex);
        }

        // Refresh the upgrade panel so the available upgrade buttons and stored-unit UI reflect the new state.
        PopulateUpgradeOptions();
        if (currentImprovement != null && currentImprovement.isShelter)
        {
            var ts2 = TileSystem.GetForPlanet(currentPlanetIndex) ?? TileSystem.Instance;
            var td2 = ts2 != null ? ts2.GetTileData(currentTileIndex) : null;
            GameObject instObj2 = td2?.improvementInstanceObject ?? instanceObj;
            if (instObj2 == null)
            {
                ClearStoredUnitButtons();
                if (capacityText != null) capacityText.text = "Capacity: 0/0";
            }
            else
            {
                var ii = instObj2.GetComponent<ImprovementInstance>();
                if (ii == null || ii.storedUnits == null || ii.storedUnits.Count == 0)
                {
                    ClearStoredUnitButtons();
                    if (capacityText != null) capacityText.text = $"Capacity: 0/{(ii!=null?ii.GetShelterCapacity():0)}";
                }
                else
                {
                    PopulateStoredUnitButtons(ii);
                }
            }
        }
        else
        {
            ClearStoredUnitButtons();
            if (capacityText != null) capacityText.text = "";
        }
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
