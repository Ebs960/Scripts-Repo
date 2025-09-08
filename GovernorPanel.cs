using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class GovernorPanel : MonoBehaviour
{
    [Header("Panel Root")]
    [SerializeField] private GameObject panelRoot;
    [Header("Close")]
    [SerializeField] private Button closeButton;

    [Header("Display")]
    [SerializeField] private TextMeshProUGUI governorNameText;
    [SerializeField] private TextMeshProUGUI governorLevelText;
    [SerializeField] private TextMeshProUGUI governorExperienceText;
    [SerializeField] private TextMeshProUGUI governorTraitsText;
    [SerializeField] private TextMeshProUGUI governorBonusesText;


    [Header("Assignment UI")]
    [SerializeField] private GameObject assignmentPanel;
    [SerializeField] private TMP_InputField governorNameInput;
    [SerializeField] private TMP_Dropdown specializationDropdown;
    [SerializeField] private Button createGovernorButton;
    [SerializeField] private Button removeGovernorButton;
    [SerializeField] private Transform existingGovernorsContainer;
    [SerializeField] private GameObject governorEntryPrefab;
    [Header("Trait Assignment")]
    [SerializeField] private GameObject traitPanel;
    [SerializeField] private Transform traitListContainer;
    [SerializeField] private GameObject traitEntryPrefab;

    private City currentCity;

    private void Awake()
    {
        if (createGovernorButton != null)
        {
            createGovernorButton.onClick.RemoveAllListeners();
            createGovernorButton.onClick.AddListener(OnCreateGovernorClicked);
        }

        if (removeGovernorButton != null)
        {
            removeGovernorButton.onClick.RemoveAllListeners();
            removeGovernorButton.onClick.AddListener(OnRemoveGovernorClicked);
        }

        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(Hide);
        }

    if (assignmentPanel != null) assignmentPanel.SetActive(false);
    if (traitPanel != null) traitPanel.SetActive(false);
    }

    private void Update()
    {
        if (panelRoot != null && panelRoot.activeSelf && Input.GetKeyDown(KeyCode.Escape))
        {
            Hide();
        }
    }

    public void ShowForCity(City city)
    {
        currentCity = city;
        if (panelRoot != null) panelRoot.SetActive(true);
        RefreshDisplay(city);
    }


    public void Hide()
    {
        currentCity = null;
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    public void RefreshDisplay(City city = null)
    {
        if (city != null) currentCity = city;
        if (currentCity == null)
        {
            Hide();
            return;
        }

        var civ = currentCity.owner;
        if (civ == null)
        {
            Hide();
            return;
        }

        // If this civilization hasn't unlocked governors, show locked state and disable UI
        if (!civ.governorsEnabled)
        {
            if (assignmentPanel != null) assignmentPanel.SetActive(false);
            if (traitPanel != null) traitPanel.SetActive(false);
            if (governorNameText != null) governorNameText.text = "(Governors Locked)";
            if (governorLevelText != null) governorLevelText.text = "";
            if (governorExperienceText != null) governorExperienceText.text = "";
            if (governorTraitsText != null) governorTraitsText.text = "This civilization has not unlocked governors.";
            if (governorBonusesText != null) governorBonusesText.text = "";
            // Clear existing governors list
            if (existingGovernorsContainer != null)
            {
                foreach (Transform t in existingGovernorsContainer) Destroy(t.gameObject);
            }
            return;
        }

        var gov = currentCity.governor;
        if (gov == null)
        {
            // No governor — show assignment panel
            if (assignmentPanel != null) assignmentPanel.SetActive(true);
            if (governorNameText != null) governorNameText.text = "(No Governor)";
            if (governorLevelText != null) governorLevelText.text = "";
            if (governorExperienceText != null) governorExperienceText.text = "";
            if (governorTraitsText != null) governorTraitsText.text = "";
            if (governorBonusesText != null) governorBonusesText.text = "";
            // Populate existing governors list for assignment
            PopulateExistingGovernors();
            return;
        }

        // Hide assignment UI when governor present
        if (assignmentPanel != null) assignmentPanel.SetActive(false);

        if (governorNameText != null) governorNameText.text = gov.Name;
        if (governorLevelText != null) governorLevelText.text = $"Level {gov.Level}";
        if (governorExperienceText != null) governorExperienceText.text = $"XP: {gov.Experience}";

        if (governorTraitsText != null)
        {
            if (gov.Traits != null && gov.Traits.Count > 0)
            {
                governorTraitsText.text = string.Join("\n", gov.Traits.ConvertAll(t => t.traitName));
            }
            else
            {
                governorTraitsText.text = "(No traits)";
            }
        }
        // Show numeric bonus breakdown
        if (governorBonusesText != null)
        {
            var b = gov.GetTotalBonuses();
            governorBonusesText.text = $"Gold: {b.gold}\nProduction: {b.production}\nFood: {b.food}\nScience: {b.science}\nCulture: {b.culture}\nFaith: {b.faith}\nCombat: {b.combat}\nCity Defense: {b.cityDefense}";
        }
        // Hide trait panel when viewing governor summary
        if (traitPanel != null) traitPanel.SetActive(false);
    }

    public void ShowTraitPanelForCurrentGovernor()
    {
    if (currentCity == null || currentCity.governor == null) return;
    if (currentCity.owner == null || !currentCity.owner.governorsEnabled) return;
        if (traitPanel != null) traitPanel.SetActive(true);
        PopulateTraitList();
    }

    private void PopulateTraitList()
    {
        if (traitListContainer == null || traitEntryPrefab == null) return;
        if (currentCity == null || currentCity.owner == null) return;

        foreach (Transform t in traitListContainer) Destroy(t.gameObject);

        var civ = currentCity.owner;
        var governor = currentCity.governor;
        if (civ.unlockedGovernorTraits == null) return;

        foreach (var trait in civ.unlockedGovernorTraits)
        {
            // Skip traits the governor already has
            if (governor.Traits.Contains(trait)) continue;

            var entry = Instantiate(traitEntryPrefab, traitListContainer);
            var nameText = entry.transform.Find("Name")?.GetComponent<TextMeshProUGUI>() ?? entry.GetComponentInChildren<TextMeshProUGUI>();
            var descText = entry.transform.Find("Description")?.GetComponent<TextMeshProUGUI>();
            var costText = entry.transform.Find("Cost")?.GetComponent<TextMeshProUGUI>();
            var assignButton = entry.transform.Find("AssignButton")?.GetComponent<Button>() ?? entry.GetComponentInChildren<Button>();

            if (nameText != null) nameText.text = trait.traitName;
            if (descText != null) descText.text = trait.description;
            if (costText != null) costText.text = "Cost: 1 Policy Point";

            if (assignButton != null)
            {
                assignButton.onClick.RemoveAllListeners();
                assignButton.onClick.AddListener(() => AssignTraitToGovernor(trait));
                assignButton.interactable = civ.policyPoints > 0;
            }
        }
    }

    private void AssignTraitToGovernor(GovernorTrait trait)
    {
        if (trait == null || currentCity == null || currentCity.owner == null || currentCity.governor == null) return;
        var civ = currentCity.owner;
        var governor = currentCity.governor;
        if (civ.policyPoints <= 0) return;
        if (governor.Traits.Contains(trait)) return;

        governor.Traits.Add(trait);
        civ.policyPoints -= 1;

        // Refresh UI and apply bonuses
        currentCity.RefreshGovernorBonuses();
        RefreshDisplay();
        if (traitPanel != null) PopulateTraitList();
    }

    private void OnCreateGovernorClicked()
    {
        if (currentCity == null || currentCity.owner == null) return;

        if (!currentCity.owner.governorsEnabled)
        {
            Debug.LogWarning($"{currentCity.owner.civData.civName} has not unlocked governors.");
            return;
        }

        string name = governorNameInput != null ? governorNameInput.text.Trim() : "Governor";
        if (string.IsNullOrEmpty(name)) return;

        Governor.Specialization spec = Governor.Specialization.Military;
        if (specializationDropdown != null)
        {
            int idx = Mathf.Clamp(specializationDropdown.value, 0, Enum.GetNames(typeof(Governor.Specialization)).Length - 1);
            spec = (Governor.Specialization)idx;
        }

        // Use civilization API to create and assign (honors governor limits)
        var civ = currentCity.owner;
        var gov = civ.CreateGovernor(name, spec);
        if (gov != null)
        {
            // Use centralized helper to assign so logic is consistent
            TryAssignGovernor(gov);
            if (assignmentPanel != null) assignmentPanel.SetActive(false);
            RefreshDisplay();
        }
        else
        {
            Debug.LogWarning($"{civ.civData.civName} cannot create a new governor (limit reached).");
        }
    }

    private void OnRemoveGovernorClicked()
    {
    // Centralized removal helper
    TryRemoveGovernor();
    }

    private void PopulateExistingGovernors()
    {
        if (existingGovernorsContainer == null || governorEntryPrefab == null) return;
        if (currentCity == null || currentCity.owner == null) return;

        foreach (Transform t in existingGovernorsContainer) Destroy(t.gameObject);

        var civ = currentCity.owner;
        if (!civ.governorsEnabled) return;

        foreach (var governor in civ.governors)
        {
            var entry = Instantiate(governorEntryPrefab, existingGovernorsContainer);
            var nameText = entry.GetComponentInChildren<TextMeshProUGUI>();
            var assignButton = entry.GetComponentInChildren<Button>();
            if (nameText != null) nameText.text = $"{governor.Name} ({governor.specialization})";
            if (assignButton != null)
            {
                assignButton.onClick.RemoveAllListeners();
                // Use centralized helper to assign
                assignButton.onClick.AddListener(() => { TryAssignGovernor(governor); });
                assignButton.interactable = civ.governorsEnabled && !governor.Cities.Contains(currentCity);
            }
        }
    }

    /// <summary>
    /// Try to assign the given governor to the current city. Returns true on success.
    /// Centralizes checks and refresh logic so multiple UI callsites behave the same.
    /// </summary>
    private bool TryAssignGovernor(Governor governor)
    {
        if (governor == null || currentCity == null || currentCity.owner == null) return false;
        // Already assigned
        if (currentCity.governor == governor) return false;

        currentCity.owner.AssignGovernorToCity(governor, currentCity);
        RefreshDisplay();
        return true;
    }

    /// <summary>
    /// Try to remove the governor from the current city. Returns true on success.
    /// </summary>
    private bool TryRemoveGovernor()
    {
        if (currentCity == null || currentCity.owner == null) return false;
        var gov = currentCity.governor;
        if (gov == null) return false;
        currentCity.owner.RemoveGovernorFromCity(gov, currentCity);
        RefreshDisplay();
        return true;
    }
}
