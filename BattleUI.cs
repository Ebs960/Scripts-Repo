using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// UI system for real-time battle management - simplified with formation buttons
/// </summary>
public class BattleUI : MonoBehaviour
{
    [Header("Battle HUD")]
    [SerializeField] private GameObject battleHUDPanel;
    [SerializeField] private HorizontalLayoutGroup formationButtonLayout;
    [SerializeField] private GameObject formationButtonPrefab;
    
    [Header("Battle Info")]
    [SerializeField] private Slider battleProgressSlider;

    private BattleManager battleManager;
    private BattleTestSimple battleTestSimple; // For BattleTestSimple integration
    private List<CombatUnit> selectedUnits = new List<CombatUnit>();
    private bool isPaused = false;
    
    // Formation button tracking
    private List<GameObject> formationButtons = new List<GameObject>();
    private List<FormationUnit> trackedFormations = new List<FormationUnit>();

    void Start()
    {
        // Ensure panel exists but keep it deactivated until battle starts
        SetupUI();
        
        // Deactivate panel initially - it will be activated when battle starts
        if (battleHUDPanel != null)
        {
            battleHUDPanel.SetActive(false);
        }
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
        
        // Ensure UI panel exists and activate it (battle is starting)
        SetupUI();
        
        // Activate the panel when battle starts
        if (battleHUDPanel != null)
        {
            battleHUDPanel.SetActive(true);
        }
        
        CreateFormationButtons();
    }

    /// <summary>
    /// Initialize with BattleTestSimple (for formation-based battles)
    /// </summary>
    public void InitializeWithBattleTest(BattleTestSimple battleTest)
    {
        battleTestSimple = battleTest;
        
        // Ensure UI panel exists and activate it (battle is starting)
        SetupUI();
        
        // Activate the panel when battle starts
        if (battleHUDPanel != null)
        {
            battleHUDPanel.SetActive(true);
        }
        
        CreateFormationButtonsFromBattleTest();
    }

    private void SetupUI()
    {
        // Create battle HUD panel if it doesn't exist
        if (battleHUDPanel == null)
        {
            CreateBattleHUDPanel();
        }
    }

    private void CreateBattleHUDPanel()
    {
        // Create panel at top of screen
        battleHUDPanel = new GameObject("BattleHUDPanel");
        battleHUDPanel.transform.SetParent(transform, false);
        
        var canvas = GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            gameObject.AddComponent<CanvasScaler>();
            gameObject.AddComponent<GraphicRaycaster>();
        }
        
        var panelRect = battleHUDPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(1f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(0, 60);
        
        // Add horizontal layout group
        var layout = battleHUDPanel.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childForceExpandWidth = false;
        layout.childControlWidth = true;
        layout.spacing = 8f;
        layout.padding = new RectOffset(10, 10, 5, 5);
        
        formationButtonLayout = layout;
    }

    private void CreateFormationButtons()
    {
        // Clear existing buttons
        ClearFormationButtons();
        
        // Try to get formations from BattleTestSimple if available
        if (battleTestSimple != null)
        {
            CreateFormationButtonsFromBattleTest();
            return;
        }
        
        // Otherwise, create buttons from BattleManager units (grouped by formation if possible)
        if (battleManager != null)
        {
            // For now, create unit-based buttons if no formations available
            CreateUnitButtons();
        }
    }

    private void CreateFormationButtonsFromBattleTest()
    {
        if (battleTestSimple == null) return;
        
        // Directly access public allFormations list
        trackedFormations = battleTestSimple.allFormations;
        foreach (var formation in trackedFormations)
        {
            if (formation != null)
            {
                CreateFormationButton(formation);
            }
        }
    }

    private void CreateUnitButtons()
    {
        // Fallback: create buttons for individual units if no formations
        if (battleManager != null)
        {
            var attackerUnits = battleManager.GetUnits(battleManager.attacker);
            var defenderUnits = battleManager.GetUnits(battleManager.defender);
            
            // Group units into virtual formations or create per-unit buttons
            // For simplicity, create one button per unit for now
            foreach (var unit in attackerUnits)
            {
                if (unit != null)
                {
                    CreateUnitButton(unit);
                }
            }
            foreach (var unit in defenderUnits)
            {
                if (unit != null)
                {
                    CreateUnitButton(unit);
                }
            }
        }
    }

    private void CreateFormationButton(FormationUnit formation)
    {
        if (formationButtonLayout == null) return;
        
        GameObject buttonGO = new GameObject($"{formation.formationName}_Button");
        buttonGO.transform.SetParent(formationButtonLayout.transform, false);
        
        var buttonRect = buttonGO.AddComponent<RectTransform>();
        buttonRect.sizeDelta = new Vector2(240, 44);
        
        var button = buttonGO.AddComponent<Button>();
        var image = buttonGO.AddComponent<Image>();
        image.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        
        var textGO = new GameObject("Text");
        textGO.transform.SetParent(buttonGO.transform, false);
        var text = textGO.AddComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Midline;
        text.fontSize = 16f;
        text.color = Color.white;
        
        var textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        textRect.offsetMin = new Vector2(10, 5);
        textRect.offsetMax = new Vector2(-10, -5);
        
        button.onClick.AddListener(() => OnFormationButtonClicked(formation));
        
        formationButtons.Add(buttonGO);
        UpdateFormationButton(formation, text);
    }

    private void CreateUnitButton(CombatUnit unit)
    {
        if (formationButtonLayout == null) return;
        
        GameObject buttonGO = new GameObject($"{unit.data.unitName}_Button");
        buttonGO.transform.SetParent(formationButtonLayout.transform, false);
        
        var buttonRect = buttonGO.AddComponent<RectTransform>();
        buttonRect.sizeDelta = new Vector2(200, 44);
        
        var button = buttonGO.AddComponent<Button>();
        var image = buttonGO.AddComponent<Image>();
        image.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        
        var textGO = new GameObject("Text");
        textGO.transform.SetParent(buttonGO.transform, false);
        var text = textGO.AddComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Midline;
        text.fontSize = 16f;
        text.color = Color.white;
        
        var textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10, 5);
        textRect.offsetMax = new Vector2(-10, -5);
        
        button.onClick.AddListener(() => SelectUnit(unit));
        
        UpdateUnitButton(unit, text);
    }

    private void UpdateFormationButton(FormationUnit formation, TextMeshProUGUI text)
    {
        if (formation == null || text == null) return;
        
        int alive = 0;
        foreach (var soldier in formation.soldiers)
        {
            if (soldier != null) alive++;
        }
        
        text.text = $"{formation.formationName}  |  {alive} soldiers  |  Morale {formation.currentMorale}%";
    }

    private void UpdateUnitButton(CombatUnit unit, TextMeshProUGUI text)
    {
        if (unit == null || text == null) return;
        text.text = $"{unit.data.unitName}  |  HP {unit.currentHealth}/{unit.MaxHealth}";
    }

    private void OnFormationButtonClicked(FormationUnit formation)
    {
        if (battleTestSimple != null)
        {
            battleTestSimple.SelectFormation(formation);
        }
    }

    private void UpdateUI()
    {
        // Update formation button texts
        if (trackedFormations != null && trackedFormations.Count > 0)
        {
            int buttonIndex = 0;
            foreach (var formation in trackedFormations)
            {
                if (formation == null) continue;
                if (buttonIndex >= formationButtons.Count) break;
                
                var buttonGO = formationButtons[buttonIndex++];
                var text = buttonGO.GetComponentInChildren<TextMeshProUGUI>();
                if (text != null)
                {
                    UpdateFormationButton(formation, text);
                }
            }
        }
    }

    private void HandleInput()
    {
        // Handle keyboard shortcuts
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            OnPauseClicked();
        }
    }

    private void SelectUnit(CombatUnit unit)
    {
        if (battleManager == null) return;
        
        selectedUnits.Clear();
        selectedUnits.Add(unit);
        battleManager.SelectUnits(selectedUnits);
    }

    private void ClearFormationButtons()
    {
        foreach (var button in formationButtons)
        {
            if (button != null)
            {
                Destroy(button);
            }
        }
        formationButtons.Clear();
        trackedFormations.Clear();
    }

    // Button event handlers

    private void OnPauseClicked()
    {
        isPaused = !isPaused;
        Time.timeScale = isPaused ? 0f : 1f;
    }

    /// <summary>
    /// Public method to update formations list (called from BattleTestSimple)
    /// </summary>
    public void UpdateFormationsList(List<FormationUnit> formations)
    {
        trackedFormations = formations;
        ClearFormationButtons();
        
        foreach (var formation in formations)
        {
            if (formation != null)
            {
                CreateFormationButton(formation);
            }
        }
    }
}



