using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

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
    [Header("Military Garrison")]
    public Transform garrisonContainer;
    public GameObject garrisonEntryPrefab;
    public TextMeshProUGUI garrisonHeaderText;
    public Button selectAllGarrisonButton;
    public Button formArmyButton;
    public Button garrisonArmyButton;
    public TMP_Dropdown nearbyArmyDropdown;
    public TextMeshProUGUI garrisonMessageText;
    [Header("Stored Civilians")]
    public Transform civilianContainer;
    public GameObject civilianEntryPrefab;
    public TextMeshProUGUI civilianHeaderText;
    private readonly HashSet<CombatUnit> selectedGarrison = new HashSet<CombatUnit>();
    private readonly List<CombatUnit> nearbyArmies = new List<CombatUnit>();
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
        RefreshGarrisonControls();

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
                        WarnMissingPrefab(nameof(buildEntryPrefab));
                        break;
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
                        WarnMissingPrefab(nameof(queueEntryPrefab));
                        break;
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

    private void RefreshGarrisonControls()
    {
        selectedGarrison.RemoveWhere(x => x == null || !currentHerd.MilitaryGarrison.Contains(x));
        bool controlled = currentHerd.owner == CivilizationManager.Instance?.playerCiv;
        if (garrisonHeaderText != null) garrisonHeaderText.text = $"MILITARY GARRISON  {currentHerd.MilitaryGarrison.Count} / {currentHerd.GarrisonCapacity}";
        ClearChildren(garrisonContainer);
        foreach (var unit in currentHerd.MilitaryGarrison)
        {
            if (unit == null) continue;
            if (garrisonEntryPrefab == null) { WarnMissingPrefab(nameof(garrisonEntryPrefab)); break; }
            var row = Instantiate(garrisonEntryPrefab, garrisonContainer);
            var label = row.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = $"{unit.UnitName}  {unit.currentHealth} / {unit.MaxHealth} HP  Lv {unit.level} XP {unit.experience}";
            var toggle = row.GetComponentInChildren<Toggle>();
            if (toggle != null) { toggle.SetIsOnWithoutNotify(selectedGarrison.Contains(unit)); toggle.interactable = controlled; toggle.onValueChanged.AddListener(on => { if (on) selectedGarrison.Add(unit); else selectedGarrison.Remove(unit); UpdateFormArmyButton(); }); }
        }
        if (selectAllGarrisonButton != null) { selectAllGarrisonButton.gameObject.SetActive(controlled); selectAllGarrisonButton.onClick.RemoveAllListeners(); selectAllGarrisonButton.onClick.AddListener(() => { selectedGarrison.Clear(); foreach (var unit in currentHerd.MilitaryGarrison) if (unit != null) selectedGarrison.Add(unit); Refresh(); }); }
        if (formArmyButton != null) { formArmyButton.gameObject.SetActive(controlled); formArmyButton.onClick.RemoveAllListeners(); formArmyButton.onClick.AddListener(FormSelectedArmy); }
        RefreshNearbyArmies(controlled);

        if (civilianHeaderText != null) civilianHeaderText.text = $"STORED CIVILIANS  {currentHerd.StoredCivilians.Count}";
        ClearChildren(civilianContainer);
        foreach (var civilian in currentHerd.StoredCivilians)
        {
            if (civilian == null) continue;
            if (civilianEntryPrefab == null) { WarnMissingPrefab(nameof(civilianEntryPrefab)); break; }
            var row = Instantiate(civilianEntryPrefab, civilianContainer);
            var label = row.GetComponentInChildren<TextMeshProUGUI>(); if (label != null) label.text = civilian.name;
            var button = row.GetComponentInChildren<Button>();
            if (button != null) { button.interactable = controlled; button.onClick.AddListener(() => { if (!currentHerd.TryUnstoreUnit(civilian)) ShowGarrisonMessage("No safe placement is available."); Refresh(); }); }
        }
        UpdateFormArmyButton();
    }

    private void RefreshNearbyArmies(bool controlled)
    {
        nearbyArmies.Clear();
        if (controlled && currentHerd.owner?.combatUnits != null)
            nearbyArmies.AddRange(currentHerd.owner.combatUnits.Where(x => x != null && !x.isStored && CampaignArmyService.IsRepresentative(x) && x.planetIndex == currentHerd.planetIndex && x.currentTileIndex == currentHerd.currentTileIndex));
        if (nearbyArmyDropdown != null) { nearbyArmyDropdown.ClearOptions(); nearbyArmyDropdown.AddOptions(nearbyArmies.Select(x => x.MilitaryFormationName ?? x.UnitName).ToList()); nearbyArmyDropdown.interactable = nearbyArmies.Count > 1; }
        if (garrisonArmyButton != null) { garrisonArmyButton.gameObject.SetActive(controlled); garrisonArmyButton.interactable = nearbyArmies.Count > 0; garrisonArmyButton.onClick.RemoveAllListeners(); garrisonArmyButton.onClick.AddListener(() => { int index = nearbyArmyDropdown != null ? nearbyArmyDropdown.value : 0; if (index < 0 || index >= nearbyArmies.Count) return; if (!currentHerd.TryGarrisonArmy(nearbyArmies[index], out string reason)) ShowGarrisonMessage(reason); else ShowGarrisonMessage(string.Empty); Refresh(); }); }
    }

    private void FormSelectedArmy() { if (currentHerd.FormArmy(selectedGarrison.ToList(), out _)) { selectedGarrison.Clear(); ShowGarrisonMessage(string.Empty); } else ShowGarrisonMessage("Selected units cannot be safely formed here."); Refresh(); }
    private void UpdateFormArmyButton() { if (formArmyButton != null) formArmyButton.interactable = selectedGarrison.Count > 0 && selectedGarrison.All(x => x != null && currentHerd.MilitaryGarrison.Contains(x)); }
    private void ShowGarrisonMessage(string message) { if (garrisonMessageText != null) garrisonMessageText.text = message; if (!string.IsNullOrEmpty(message)) UIManager.Instance?.ShowNotification(message); }
    private static void ClearChildren(Transform root) { if (root == null) return; for (int i = root.childCount - 1; i >= 0; i--) { var child = root.GetChild(i).gameObject; if (Application.isPlaying) Destroy(child); else DestroyImmediate(child); } }
    private static readonly HashSet<string> warnedPrefabs = new HashSet<string>();
    private static void WarnMissingPrefab(string field) { if (warnedPrefabs.Add(field)) Debug.LogWarning($"[HerdPanel] Required authored prefab '{field}' is not assigned. Configure the HerdPanel prefab; runtime fallback UI is not used."); }

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
