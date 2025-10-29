using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// UI system for real-time battle management
/// </summary>
public class BattleUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI selectedUnitsText;
    [SerializeField] private TextMeshProUGUI formationText;
    [SerializeField] private ScrollRect unitListScrollRect;
    [SerializeField] private GameObject unitListItemPrefab;
    [SerializeField] private TextMeshProUGUI battleStatusText;
    [SerializeField] private TextMeshProUGUI instructionsText;

    [Header("Formation Controls")]
    [SerializeField] private Dropdown formationDropdown;
    [SerializeField] private Button changeFormationButton;

    [Header("Battle Info")]
    [SerializeField] private TextMeshProUGUI attackerNameText;
    [SerializeField] private TextMeshProUGUI defenderNameText;
    [SerializeField] private Slider battleProgressSlider;

    private BattleManager battleManager;
    private List<CombatUnit> selectedUnits = new List<CombatUnit>();
    private FormationType currentFormation = FormationType.Line;
    private bool isPaused = false;

    void Start()
    {
        SetupUI();
    }

    void Update()
    {
        UpdateUI();
        HandleInput();
    }

    /// <summary>
    /// Initialize the battle UI
    /// </summary>
    public void Initialize(BattleManager manager)
    {
        battleManager = manager;
        SetupFormationDropdown();
        UpdateBattleInfo();
    }

    private void SetupUI()
    {
        // Setup formation button listener
        if (changeFormationButton != null)
            changeFormationButton.onClick.AddListener(OnChangeFormationClicked);
        
        // Setup instructions text
        if (instructionsText != null)
        {
            instructionsText.text = "Left Click: Select Units\nRight Click: Move/Attack\nEscape: Pause/Resume";
        }
    }

    private void SetupFormationDropdown()
    {
        if (formationDropdown != null)
        {
            formationDropdown.ClearOptions();
            
            List<string> formationNames = new List<string>();
            foreach (FormationType formation in System.Enum.GetValues(typeof(FormationType)))
            {
                formationNames.Add(formation.ToString());
            }
            
            formationDropdown.AddOptions(formationNames);
            formationDropdown.value = 0;
        }
    }

    private void UpdateUI()
    {
        if (battleManager == null) return;

        // Update selected units display
        UpdateSelectedUnitsDisplay();

        // Update formation display
        UpdateFormationDisplay();

        // Update unit list
        UpdateUnitList();

        // Update battle status
        UpdateBattleStatus();
    }

    private void UpdateSelectedUnitsDisplay()
    {
        if (selectedUnitsText != null)
        {
            selectedUnitsText.text = $"Selected Units: {selectedUnits.Count}";
        }
    }

    private void UpdateFormationDisplay()
    {
        if (formationText != null)
        {
            formationText.text = $"Formation: {currentFormation}";
        }
    }

    private void UpdateUnitList()
    {
        if (unitListScrollRect == null || unitListItemPrefab == null) return;

        // Clear existing items
        foreach (Transform child in unitListScrollRect.content)
        {
            Destroy(child.gameObject);
        }

        // Add units to list
        var allUnits = battleManager.GetUnits(battleManager.attacker);
        allUnits.AddRange(battleManager.GetUnits(battleManager.defender));

        foreach (var unit in allUnits)
        {
            if (unit != null)
            {
                CreateUnitListItem(unit);
            }
        }
    }

    private void CreateUnitListItem(CombatUnit unit)
    {
        GameObject item = Instantiate(unitListItemPrefab, unitListScrollRect.content);
        
        // Setup unit info
        var nameText = item.transform.Find("UnitName")?.GetComponent<TextMeshProUGUI>();
        var healthText = item.transform.Find("Health")?.GetComponent<TextMeshProUGUI>();
        var healthBar = item.transform.Find("HealthBar")?.GetComponent<Slider>();
        var selectButton = item.GetComponent<Button>();

        if (nameText != null)
            nameText.text = unit.data.unitName;
        
        if (healthText != null)
            healthText.text = $"{unit.currentHealth}/{unit.MaxHealth}";
        
        if (healthBar != null)
        {
            healthBar.value = (float)unit.currentHealth / unit.MaxHealth;
        }

        if (selectButton != null)
        {
            selectButton.onClick.AddListener(() => SelectUnit(unit));
        }
    }

    private void UpdateBattleStatus()
    {
        if (battleStatusText != null)
        {
            string status = isPaused ? "PAUSED" : "BATTLE IN PROGRESS";
            battleStatusText.text = status;
        }
    }

    private void UpdateBattleInfo()
    {
        if (battleManager == null) return;

        if (attackerNameText != null && battleManager.attacker != null)
            attackerNameText.text = battleManager.attacker.civData.civName;
        
        if (defenderNameText != null && battleManager.defender != null)
            defenderNameText.text = battleManager.defender.civData.civName;
    }

    private void HandleInput()
    {
        if (battleManager == null) return;

        // Handle mouse input
        if (Input.GetMouseButtonDown(0)) // Left click
        {
            HandleLeftClick();
        }
        else if (Input.GetMouseButtonDown(1)) // Right click
        {
            HandleRightClick();
        }

        // Handle keyboard shortcuts
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            OnPauseClicked();
        }
    }

    private void HandleLeftClick()
    {
        // Raycast to find units
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit))
        {
            CombatUnit unit = hit.collider.GetComponent<CombatUnit>();
            if (unit != null)
            {
                if (Input.GetKey(KeyCode.LeftControl))
                {
                    // Add to selection
                    AddToSelection(unit);
                }
                else
                {
                    // Replace selection
                    SelectUnit(unit);
                }
            }
        }
    }

    private void HandleRightClick()
    {
        if (selectedUnits.Count == 0) return;

        // Raycast to find target
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit))
        {
            CombatUnit targetUnit = hit.collider.GetComponent<CombatUnit>();
            if (targetUnit != null)
            {
                // Check if target is an enemy
                bool isEnemy = false;
                foreach (var selectedUnit in selectedUnits)
                {
                    if (selectedUnit != null && selectedUnit.isAttacker != targetUnit.isAttacker)
                    {
                        isEnemy = true;
                        break;
                    }
                }

                if (isEnemy)
                {
                    // Attack enemy target
                    battleManager.AttackTarget(targetUnit);
                }
                else
                {
                    // Move to position (friendly unit or same side)
                    battleManager.MoveSelectedUnits(hit.point);
                }
            }
            else
            {
                // Move to position (ground/terrain)
                battleManager.MoveSelectedUnits(hit.point);
            }
        }
    }

    private void SelectUnit(CombatUnit unit)
    {
        selectedUnits.Clear();
        selectedUnits.Add(unit);
        battleManager.SelectUnits(selectedUnits);
    }

    private void AddToSelection(CombatUnit unit)
    {
        if (!selectedUnits.Contains(unit))
        {
            selectedUnits.Add(unit);
            battleManager.SelectUnits(selectedUnits);
        }
    }

    private void ClearSelection()
    {
        selectedUnits.Clear();
        battleManager.ClearSelection();
    }

    // Button event handlers

    private void OnPauseClicked()
    {
        isPaused = !isPaused;
        Time.timeScale = isPaused ? 0f : 1f;
    }

    private void OnChangeFormationClicked()
    {
        if (formationDropdown != null && selectedUnits.Count > 0)
        {
            FormationType newFormation = (FormationType)formationDropdown.value;
            ChangeFormation(newFormation);
        }
    }

    private void ChangeFormation(FormationType newFormation)
    {
        currentFormation = newFormation;
        
        if (selectedUnits.Count > 0)
        {
            // Apply new formation to selected units
            // This would call a formation manager to rearrange units
            Debug.Log($"[BattleUI] Changed formation to {newFormation} for {selectedUnits.Count} units");
        }
    }
}
