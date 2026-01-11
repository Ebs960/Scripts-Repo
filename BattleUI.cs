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
        // Validate that panel is assigned in Inspector
        if (battleHUDPanel == null)
        {
            Debug.LogError("[BattleUI] battleHUDPanel is not assigned! Please assign it in the Inspector.");
        }
        else
        {
// Deactivate panel initially - it will be activated when battle starts
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
        else
        {
            Debug.LogError("[BattleUI] battleHUDPanel is null after SetupUI!");
        }
        
        // Don't create buttons here - formations aren't created yet!
        // Buttons will be created by UpdateFormationsList() after formations are ready
}

    private void SetupUI()
    {
        // CRITICAL: Use manually assigned panel from Inspector - do NOT create one!
        if (battleHUDPanel == null)
        {
            Debug.LogError("[BattleUI] battleHUDPanel is not assigned in Inspector! Please assign it manually in the scene.");
            return;
        }
// Check if panel has ScrollRect (common UI pattern: Panel -> Viewport -> Content)
        var scrollRect = battleHUDPanel.GetComponent<UnityEngine.UI.ScrollRect>();
        Transform targetParent = battleHUDPanel.transform;
        
        if (scrollRect != null)
        {
// ScrollRect structure: Panel -> Viewport -> Content
            // We need to find the Content GameObject (child of Viewport)
            if (scrollRect.content != null)
            {
                targetParent = scrollRect.content.transform;
}
            else
            {
                // Fallback: look for a child named "Content"
                Transform viewport = battleHUDPanel.transform.Find("Viewport");
                if (viewport != null)
                {
                    Transform content = viewport.Find("Content");
                    if (content != null)
                    {
                        targetParent = content;
}
                }
            }
        }
        
        // Get or ensure layout group exists
        // First check if it's assigned in Inspector
        if (formationButtonLayout == null)
        {
            // Try to find it on the target parent or its children
            formationButtonLayout = targetParent.GetComponentInChildren<HorizontalLayoutGroup>();
            if (formationButtonLayout != null)
            {
}
        }
        
        // If still not found, create one as a child of the target parent
        if (formationButtonLayout == null)
        {
            Debug.LogWarning("[BattleUI] No HorizontalLayoutGroup found, creating one...");
            GameObject layoutGO = new GameObject("FormationButtonLayout");
            layoutGO.transform.SetParent(targetParent, false);
            
            // Add RectTransform
            var layoutRect = layoutGO.AddComponent<RectTransform>();
            layoutRect.anchorMin = Vector2.zero;
            layoutRect.anchorMax = Vector2.one;
            layoutRect.offsetMin = Vector2.zero;
            layoutRect.offsetMax = Vector2.zero;
            
            // Add HorizontalLayoutGroup
            formationButtonLayout = layoutGO.AddComponent<HorizontalLayoutGroup>();
            formationButtonLayout.childAlignment = TextAnchor.MiddleLeft;
            // Don't modify childForceExpand - let user set it in Inspector
            formationButtonLayout.childControlWidth = true;
            formationButtonLayout.childControlHeight = true;
            formationButtonLayout.spacing = 8f;
            formationButtonLayout.padding = new RectOffset(10, 10, 5, 5);
}
        else
        {
            // Ensure layout group is properly configured (but don't modify childForceExpand - let user set it)
            formationButtonLayout.childAlignment = TextAnchor.MiddleLeft;
            formationButtonLayout.childControlWidth = true;
            formationButtonLayout.childControlHeight = true;
            formationButtonLayout.spacing = 8f;
            if (formationButtonLayout.padding == null || formationButtonLayout.padding.left == 0)
            {
                formationButtonLayout.padding = new RectOffset(10, 10, 5, 5);
            }
}
    }

    // CreateBattleHUDPanel() removed - we use manually assigned panels from the Inspector only!

    private void CreateFormationButtonsFromBattleTest()
    {
        if (battleTestSimple == null)
        {
            Debug.LogWarning("[BattleUI] battleTestSimple is null! Cannot create formation buttons.");
            return;
        }
        
        // Directly access public allFormations list
        trackedFormations = battleTestSimple.allFormations;
if (trackedFormations == null || trackedFormations.Count == 0)
        {
            Debug.LogWarning("[BattleUI] No formations found! Buttons will not be created yet.");
            return;
        }
        
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
        if (formationButtonLayout == null)
        {
            Debug.LogError("[BattleUI] formationButtonLayout is null! Cannot create formation button.");
            // Try to get it again
            SetupUI();
            if (formationButtonLayout == null)
            {
                Debug.LogError("[BattleUI] formationButtonLayout is still null after SetupUI! Cannot create button.");
                return;
            }
        }
        
        if (formation == null)
        {
            Debug.LogError("[BattleUI] Formation is null! Cannot create button.");
            return;
        }
        
        GameObject buttonGO = new GameObject($"{formation.formationName}_Button");
        
        // CRITICAL: Parent to the layout group's transform, not the panel
        // The layout group should be a direct child of the panel
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
if (formations == null || formations.Count == 0)
        {
            Debug.LogWarning("[BattleUI] UpdateFormationsList received null or empty list!");
            return;
        }
        
        // Ensure UI is set up before creating buttons
        if (formationButtonLayout == null)
        {
            Debug.LogWarning("[BattleUI] formationButtonLayout is null, calling SetupUI...");
            SetupUI();
        }
        
        if (formationButtonLayout == null)
        {
            Debug.LogError("[BattleUI] formationButtonLayout is still null after SetupUI! Cannot create buttons.");
            return;
        }
        
        // Ensure panel is active
        if (battleHUDPanel != null && !battleHUDPanel.activeSelf)
        {
battleHUDPanel.SetActive(true);
        }
        
        // CRITICAL FIX: Clear buttons BEFORE assigning formations list
        // Otherwise ClearFormationButtons() will clear the formations list we just received!
        ClearFormationButtons();
        
        // Now assign the formations list (create a copy to avoid reference issues)
        trackedFormations = new List<FormationUnit>(formations);
        
        // Debug: Check formations list after clearing buttons
int buttonsCreated = 0;
        foreach (var formation in formations)
        {
            if (formation != null)
            {
                CreateFormationButton(formation);
                buttonsCreated++;
            }
        }
        
        // Force layout rebuild
        if (formationButtonLayout != null)
        {
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(formationButtonLayout.GetComponent<RectTransform>());
        }
    }
}



