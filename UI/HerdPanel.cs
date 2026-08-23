using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class HerdPanel : MonoBehaviour
{
    private void Awake()
    {
        // Ensure the herd panel starts hidden like other panels
        gameObject.SetActive(false);
        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(HidePanel);
        }
        if (packButton != null)
        {
            packButton.onClick.RemoveAllListeners();
        }
    }
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI animalsText;
    public TextMeshProUGUI foodText;
    public TextMeshProUGUI populationText;
    public TextMeshProUGUI movePointsText;
    public TextMeshProUGUI storageText;
    public TextMeshProUGUI structuresText;
    public TextMeshProUGUI productionText;
    [Header("Yields")]
    public TextMeshProUGUI goldText;
    public TextMeshProUGUI faithText;
    public TextMeshProUGUI scienceText;
    public TextMeshProUGUI cultureText;
    public TextMeshProUGUI policyText;
    
    [Header("Mode")]
    public TextMeshProUGUI modeText;
    public Button packButton;
    
    [Header("Governor UI")]
    public TextMeshProUGUI governorNameText;
    public TextMeshProUGUI governorLevelText;
    public TextMeshProUGUI governorExperienceText;
    public TMP_Dropdown governorDropdown;
    private System.Collections.Generic.List<Governor> dropdownGovernors = new System.Collections.Generic.List<Governor>();
    [Header("Queue UI")]
    public Transform queueContainer; // container to populate queue entries
    public GameObject queueEntryPrefab; // prefab for queue entries (should have TextMeshProUGUI and HerdQueueEntry)

    private Herd currentHerd;

    [Header("Build UI")]
    public Transform buildListContainer; // container to populate build entries
    public GameObject buildEntryPrefab; // prefab: should contain a TextMeshProUGUI and a Button
    [Header("Move Herd")]
    public Button closeButton;

    public void ShowPanel(Herd herd)
    {
        if (herd == null) return;
        currentHerd = herd;
        gameObject.SetActive(true);
        Refresh();
    }

    public void HidePanel()
    {
        currentHerd = null;
        gameObject.SetActive(false);
    }

    public void Refresh()
    {
        if (currentHerd == null) return;
        if (titleText != null)
        {
            var display = string.IsNullOrEmpty(currentHerd.herdName) ? currentHerd.gameObject.name : currentHerd.herdName;
            titleText.text = display;
        }

            if (animalsText != null)
            {
                string s = "";
                foreach (var a in currentHerd.animals)
                {
                    if (a == null) continue;
                    s += $"{a.species.ToString()}: {a.count}\n";
                }
                animalsText.text = (string.IsNullOrEmpty(s) ? "(no animals)\n" : s) + $"Total livestock: {currentHerd.GetTotalAnimalCount()}";
            }

            // Governor display and dropdown
            UpdateGovernorDisplay();

        if (foodText != null) { int need = currentHerd.FoodRequiredPerTurn; int net = currentHerd.lastGrazedAmount - need; foodText.text = $"Reserve: {currentHerd.foodReserve}/{currentHerd.storageCapacity} | Grazing +{currentHerd.lastGrazedAmount} | Upkeep -{need} | Net {net:+#;-#;0}" + (currentHerd.IsStarving ? $"\nSTARVATION: projected livestock losses (last {currentHerd.lastStarvationLoss})" : ""); }
        if (populationText != null) populationText.text = $"Military Garrison: {currentHerd.MilitaryGarrison.Count}/{currentHerd.GarrisonCapacity}\nStored Civilians: {currentHerd.StoredCivilians.Count}";
        if (movePointsText != null) movePointsText.text = $"Move: {currentHerd.movementPoints}/{currentHerd.maxMovementPoints}";
        if (storageText != null) storageText.text = $"Storage: {currentHerd.storageCapacity}";

        if (structuresText != null)
        {
            string st = "";
            foreach (var b in currentHerd.builtStructures)
            {
                if (b == null) continue;
                st += $"{b.buildingName}\n";
            }
            structuresText.text = string.IsNullOrEmpty(st) ? "(none)" : st;
        }

        // Populate buildable buildings list
        if (buildListContainer != null)
        {
            // Clear existing entries
            for (int i = buildListContainer.childCount - 1; i >= 0; i--)
            {
                var c = buildListContainer.GetChild(i);
                if (Application.isPlaying) Destroy(c.gameObject); else DestroyImmediate(c.gameObject);
            }

            if (currentHerd.owner != null && currentHerd.owner.unlockedBuildings != null)
            {
                foreach (var b in currentHerd.owner.unlockedBuildings)
                {
                    if (b == null) continue;
                    if (!b.buildableByHerd) continue;
                    if (!b.AreRequirementsMet(currentHerd.owner)) continue;

                    // Create entry
                    // If there's an active herd production entry for this herd and building, show progress instead
                    var isBuildingInProgress = currentHerd.productionQueue != null && currentHerd.productionQueue.Count > 0 && currentHerd.productionQueue[0].data == b;

                    if (buildEntryPrefab != null)
                    {
                        var go = Instantiate(buildEntryPrefab, buildListContainer);
                        go.name = "BuildEntry_" + b.buildingName;
                        // Find label and button in prefab
                        var label = go.GetComponentInChildren<TextMeshProUGUI>();
                        if (label != null) label.text = b.buildingName + (b.herdStorageBonus != 0 ? $" (+{b.herdStorageBonus})" : "");
                        var btn = go.GetComponentInChildren<UnityEngine.UI.Button>();
                        if (btn != null)
                        {
                            btn.onClick.RemoveAllListeners();
                            var captured = b;
                            if (isBuildingInProgress)
                            {
                                btn.interactable = false;
                                var txt = btn.GetComponentInChildren<TextMeshProUGUI>(); if (txt != null) txt.text = "In Progress";
                            }
                            else
                            {
                                btn.onClick.AddListener(() => OnBuildClicked(captured));
                            }
                        }
                    }
                    else
                    {
                        // Fallback: create a simple button entry
                        var go = new GameObject("BuildEntry_" + b.buildingName, typeof(RectTransform));
                        go.transform.SetParent(buildListContainer, false);
                        var txtGO = new GameObject("Label", typeof(RectTransform));
                        txtGO.transform.SetParent(go.transform, false);
                        var tmp = txtGO.AddComponent<TextMeshProUGUI>();
                        tmp.text = b.buildingName + (b.herdStorageBonus != 0 ? $" (+{b.herdStorageBonus})" : "");
                        tmp.fontSize = 18;

                        var btnGO = new GameObject("AttachBtn", typeof(RectTransform));
                        btnGO.transform.SetParent(go.transform, false);
                        var btn = btnGO.AddComponent<UnityEngine.UI.Button>();
                        var img = btnGO.AddComponent<UnityEngine.UI.Image>();
                        img.color = new Color(0.2f, 0.6f, 0.2f, 1f);
                        var btnTextGO = new GameObject("Text", typeof(RectTransform));
                        btnTextGO.transform.SetParent(btnGO.transform, false);
                        var btxt = btnTextGO.AddComponent<TextMeshProUGUI>();
                        btxt.text = "Attach";
                        btxt.fontSize = 16;
                        var captured = b;
                        if (isBuildingInProgress)
                        {
                            var txt = btn.GetComponentInChildren<TextMeshProUGUI>(); if (txt != null) txt.text = "In Progress";
                            btn.interactable = false;
                        }
                        else
                        {
                            btn.onClick.AddListener(() => OnBuildClicked(captured));
                        }
                    }
                }
            }
        }
        // Populate current production queue entries
        if (queueContainer != null)
        {
            for (int i = queueContainer.childCount - 1; i >= 0; i--)
            {
                var c = queueContainer.GetChild(i);
                if (Application.isPlaying) Destroy(c.gameObject); else DestroyImmediate(c.gameObject);
            }

            if (currentHerd.productionQueue != null)
            {
                for (int i = 0; i < currentHerd.productionQueue.Count; i++)
                {
                    var entry = currentHerd.productionQueue[i];
                    if (entry == null) continue;
                    string label = "(unknown)";
                    if (entry.data is BuildingData bd) label = bd.buildingName + $" ({Mathf.Max(0, entry.remainingPts)}/{bd.productionCost})";

                    if (queueEntryPrefab != null)
                    {
                        var go = Instantiate(queueEntryPrefab, queueContainer);
                        go.name = "QueueEntry_" + i;
                        var txt = go.GetComponentInChildren<TextMeshProUGUI>(); if (txt != null) txt.text = label;
                        var q = go.GetComponent<HerdQueueEntry>(); if (q == null) q = go.AddComponent<HerdQueueEntry>();
                        q.owner = this; q.queueIndex = i;
                    }
                    else
                    {
                        var go = new GameObject("QueueEntry_" + i, typeof(RectTransform));
                        go.transform.SetParent(queueContainer, false);
                        var tmpGO = new GameObject("Text", typeof(RectTransform)); tmpGO.transform.SetParent(go.transform, false);
                        var tmp = tmpGO.AddComponent<TextMeshProUGUI>(); tmp.text = label; tmp.fontSize = 18;
                        var cg = go.AddComponent<UnityEngine.CanvasGroup>();
                        var q = go.AddComponent<HerdQueueEntry>(); q.owner = this; q.queueIndex = i;
                    }
                }
            }
        }
        // If there is an active herd production entry, show its progress
        if (productionText != null) productionText.text = $"Herd Prod: {currentHerd.GetProductionPerTurn()}";

        // Show other yields (from animals / herd yields) and potential tile yields when settled
        var yields = currentHerd.GetAnimalYields();
        var tileYields = currentHerd.GetNeighborhoodTileYields();
        if (goldText != null) goldText.text = $"Gold: {yields.Gold:+#;-#;0}/turn";
        if (faithText != null) faithText.text = $"Faith: {yields.Faith:+#;-#;0}/turn";
        if (scienceText != null) scienceText.text = $"Science: {yields.Science:+#;-#;0}/turn";
        if (cultureText != null) cultureText.text = $"Culture: {yields.Culture:+#;-#;0}/turn";
        if (policyText != null) policyText.text = $"Policy: {yields.Policy:+#;-#;0}/turn";

        // Show potential tile yields (what settling here would access)
        if (goldText != null)
        {
            // append tile yield info on a second line
            goldText.text += $"  (Tiles: {tileYields.Gold:+#;-#;0}/turn)";
        }
        if (foodText != null)
        {
            foodText.text += $"  (Tiles: {tileYields.Food:+#;-#;0}/turn)";
        }

        // Mode display and Pack/Settle button
        if (modeText != null) modeText.text = currentHerd.isPacked ? "Mode: Packed (mobile)" : "Mode: Settled (camp)";
        if (packButton != null)
        {
            packButton.onClick.RemoveAllListeners();
            packButton.onClick.AddListener(() => {
                if (currentHerd == null) return;
                if (currentHerd.isPacked) currentHerd.Settle(); else currentHerd.Pack();
                Refresh();
            });
            var btTxt = packButton.GetComponentInChildren<TextMeshProUGUI>();
            if (btTxt != null) btTxt.text = currentHerd.isPacked ? "Settle" : "Pack Up";
        }

        // Toggle build/production UI when packed (packed = mobile => limited yields and no production/build)
        if (buildListContainer != null) buildListContainer.gameObject.SetActive(!currentHerd.isPacked);
        if (queueContainer != null) queueContainer.gameObject.SetActive(!currentHerd.isPacked);
        if (productionText != null) productionText.gameObject.SetActive(!currentHerd.isPacked);
        if (structuresText != null) structuresText.gameObject.SetActive(!currentHerd.isPacked);

        

        if (currentHerd.productionQueue != null && currentHerd.productionQueue.Count > 0)
        {
            var entry = currentHerd.productionQueue[0];
            if (entry != null && entry.data is BuildingData bd)
            {
                if (structuresText != null)
                {
                    structuresText.text += $"\nBuilding: {bd.buildingName} ({Mathf.Max(0, entry.remainingPts)}/{bd.productionCost})";
                }
            }
        }
        else
        {
            if (structuresText != null && (currentHerd.builtStructures == null || currentHerd.builtStructures.Count == 0))
                structuresText.text = "(none)";
        }
        
        // (Move-target UI removed - new movement system replaces per-panel neighbor buttons)
    }

    private void UpdateGovernorDisplay()
    {
        if (currentHerd == null) return;
        var civ = currentHerd.owner;
        // Name/level/xp text
        var gov = currentHerd.governor;
        if (gov == null)
        {
            if (governorNameText != null) governorNameText.text = "(No Governor)";
            if (governorLevelText != null) governorLevelText.text = "";
            if (governorExperienceText != null) governorExperienceText.text = "";
        }
        else
        {
            if (governorNameText != null) governorNameText.text = gov.Name;
            if (governorLevelText != null) governorLevelText.text = $"Level {gov.Level}";
            if (governorExperienceText != null) governorExperienceText.text = $"XP: {gov.Experience}";
        }

        PopulateGovernorDropdown();
    }

    private void PopulateGovernorDropdown()
    {
        dropdownGovernors.Clear();
        if (governorDropdown == null) return;
        governorDropdown.onValueChanged.RemoveAllListeners();
        governorDropdown.ClearOptions();

        var options = new System.Collections.Generic.List<string>();
        // Option 0 is none
        options.Add("(None)");

        var civ = currentHerd?.owner;
        if (civ == null || !civ.governorsEnabled)
        {
            governorDropdown.AddOptions(options);
            governorDropdown.value = 0;
            governorDropdown.interactable = false;
            return;
        }

        // Add all civ governors
        if (civ.governors != null)
        {
            foreach (var g in civ.governors)
            {
                if (g == null) continue;
                dropdownGovernors.Add(g);
                options.Add(g.Name);
            }
        }

        governorDropdown.AddOptions(options);

        // Set selected index to current herd's governor
        if (currentHerd.governor == null)
            governorDropdown.value = 0;
        else
        {
            int idx = dropdownGovernors.IndexOf(currentHerd.governor);
            governorDropdown.value = (idx >= 0) ? idx + 1 : 0;
        }

        governorDropdown.interactable = true;
        governorDropdown.onValueChanged.AddListener(OnGovernorDropdownChanged);
    }

    private void OnGovernorDropdownChanged(int idx)
    {
        if (currentHerd == null) return;
        var civ = currentHerd.owner;
        if (civ == null) return;

        // Remove current governor if idx == 0
        if (idx == 0)
        {
            if (currentHerd.governor != null) civ.RemoveGovernorFromHerd(currentHerd.governor, currentHerd);
            Refresh();
            return;
        }

        int govIndex = idx - 1;
        if (govIndex >= 0 && govIndex < dropdownGovernors.Count)
        {
            var selected = dropdownGovernors[govIndex];
            civ.AssignGovernorToHerd(selected, currentHerd);
        }
        Refresh();
    }

    private void OnBuildClicked(BuildingData building)
    {
        if (building == null || currentHerd == null) return;
        var civ = currentHerd.owner;
        if (civ == null) return;
        bool ok = currentHerd.QueueProduction(building);
        if (!ok)
        {
            // Could show notification via UIManager
            UIManager.Instance?.ShowNotification("Cannot start herd build: requirements or resources missing.");
            return;
        }
        Refresh();
    }

    // Called by HerdQueueEntry when clicked
    public void OnQueueEntryClicked(int index)
    {
        if (currentHerd == null) return;
        if (index < 0 || index >= currentHerd.productionQueue.Count) return;
        currentHerd.CancelProduction(index);
        Refresh();
    }

    // Called by HerdQueueEntry when drag ends
    public void OnQueueEntryReordered(int fromIndex, int toIndex)
    {
        if (currentHerd == null) return;
        if (fromIndex < 0 || fromIndex >= currentHerd.productionQueue.Count) return;
        toIndex = Mathf.Clamp(toIndex, 0, Mathf.Max(0, currentHerd.productionQueue.Count - 1));
        if (fromIndex == toIndex) { Refresh(); return; }
        currentHerd.ReorderProduction(fromIndex, toIndex);
        Refresh();
    }

    public void OnMoveTargetClicked(int tileIndex)
    {
        // Deprecated: move-target button handler removed. Movement is handled by the new movement system.
    }
}
