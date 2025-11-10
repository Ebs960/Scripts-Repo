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

    private BattleTestSimple battleTestSimple; // Main battle system (merged from BattleManager)
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
    /// Initialize the battle UI (for BattleManager compatibility - now uses BattleTestSimple)
    /// </summary>
    public void Initialize(BattleTestSimple manager)
    {
        InitializeWithBattleTest(manager);
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
        // Use manually assigned panel if available, otherwise auto-generate
        if (battleHUDPanel == null)
        {
            CreateBattleHUDPanel();
        }
        else
        {
            // Ensure assigned panel has necessary components
            if (formationButtonLayout == null)
            {
                formationButtonLayout = battleHUDPanel.GetComponent<HorizontalLayoutGroup>();
                if (formationButtonLayout == null)
                {
                    // Create layout group if none exists
                    formationButtonLayout = battleHUDPanel.AddComponent<HorizontalLayoutGroup>();
                    formationButtonLayout.childAlignment = TextAnchor.MiddleLeft;
                    formationButtonLayout.childForceExpandWidth = false;
                    formationButtonLayout.childControlWidth = true;
                    formationButtonLayout.spacing = 8f;
                    formationButtonLayout.padding = new RectOffset(10, 10, 5, 5);
                }
            }
        }
    }

    private void CreateBattleHUDPanel()
    {
        // Create panel at top of screen
        battleHUDPanel = new GameObject("BattleHUDPanel");
        battleHUDPanel.transform.SetParent(transform, false);
        
        // Put UI on UI layer so it doesn't block unit raycasts
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer != -1)
        {
            battleHUDPanel.layer = uiLayer;
            gameObject.layer = uiLayer;
        }
        
        var canvas = GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            gameObject.AddComponent<CanvasScaler>();
            var raycaster = gameObject.AddComponent<GraphicRaycaster>();
            // Raycaster will still work for UI interaction, but won't block physics raycasts
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

    private void CreateFormationButton(FormationUnit formation)
    {
        if (formationButtonLayout == null) return;
        
        GameObject buttonGO = new GameObject($"{formation.formationName}_Button");
        buttonGO.transform.SetParent(formationButtonLayout.transform, false);
        
        // Put button on UI layer
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer != -1)
        {
            buttonGO.layer = uiLayer;
        }
        
        var buttonRect = buttonGO.AddComponent<RectTransform>();
        buttonRect.sizeDelta = new Vector2(240, 44);
        
        var button = buttonGO.AddComponent<Button>();
        var image = buttonGO.AddComponent<Image>();
        image.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        
        var textGO = new GameObject("Text");
        textGO.transform.SetParent(buttonGO.transform, false);
        
        // Put text on UI layer
        if (uiLayer != -1)
        {
            textGO.layer = uiLayer;
        }
        
        var text = textGO.AddComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Midline;
        text.fontSize = 16f;
        text.color = Color.white;
        text.raycastTarget = true; // Allow button clicks but physics raycasts will ignore UI layer
        
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
        if (battleTestSimple == null) return;
        
        // Find the formation this unit belongs to and select that instead
        // (BattleTestSimple uses formation-based selection, not individual units)
        var formation = battleTestSimple.GetFormationFromUnit(unit);
        if (formation != null)
        {
            battleTestSimple.SelectFormation(formation);
        }
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



