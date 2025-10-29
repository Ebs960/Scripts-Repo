using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// Debug version of QuickBattleStart with extensive logging
/// This will help us figure out what's going wrong
/// </summary>
public class QuickBattleStartDebug : MonoBehaviour
{
    [Header("Quick Setup")]
    public Button startButton;
    public Text statusText;
    
    [Header("Debug Settings")]
    public bool enableDebugLogs = true;
    public bool createUI = true;
    
    void Start()
    {
        DebugLog("QuickBattleStartDebug started");
        
        // Create UI if needed
        if (createUI && (startButton == null || statusText == null))
        {
            DebugLog("Creating UI elements...");
            CreateQuickUI();
        }
        
        // Set up button
        if (startButton != null)
        {
            startButton.onClick.AddListener(StartQuickBattle);
            DebugLog("Button connected successfully");
        }
        else
        {
            DebugLog("ERROR: No start button found!");
        }
        
        UpdateStatus("Ready! Click the button to start a battle.");
        DebugLog("QuickBattleStartDebug initialization complete");
    }
    
    void CreateQuickUI()
    {
        DebugLog("Creating UI elements...");
        
        // Find or create canvas
        var canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            DebugLog("No canvas found, creating one...");
            var canvasGO = new GameObject("Canvas");
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            DebugLog("Canvas created");
        }
        else
        {
            DebugLog("Found existing canvas");
        }
        
        // Create button if needed
        if (startButton == null)
        {
            DebugLog("Creating start button...");
            var buttonGO = new GameObject("StartBattleButton");
            buttonGO.transform.SetParent(canvas.transform, false);
            startButton = buttonGO.AddComponent<Button>();
            
            // Add button image
            var image = buttonGO.AddComponent<Image>();
            image.color = Color.blue;
            
            // Add button text
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(buttonGO.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = "Start Battle";
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            
            // Position button
            var rectTransform = buttonGO.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = new Vector2(200, 50);
            
            DebugLog("Start button created");
        }
        
        // Create status text if needed
        if (statusText == null)
        {
            DebugLog("Creating status text...");
            var statusGO = new GameObject("StatusText");
            statusGO.transform.SetParent(canvas.transform, false);
            statusText = statusGO.AddComponent<Text>();
            statusText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            statusText.color = Color.white;
            statusText.alignment = TextAnchor.UpperCenter;
            
            var statusRect = statusGO.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0.5f, 0.5f);
            statusRect.anchorMax = new Vector2(0.5f, 0.5f);
            statusRect.anchoredPosition = new Vector2(0, 100);
            statusRect.sizeDelta = new Vector2(400, 100);
            
            DebugLog("Status text created");
        }
    }
    
    public void StartQuickBattle()
    {
        DebugLog("StartQuickBattle called!");
        UpdateStatus("Starting quick battle...");
        
        try
        {
            // Check for GameManager first
            DebugLog("Looking for GameManager...");
            var gameManager = FindFirstObjectByType<GameManager>();
            if (gameManager != null)
            {
                DebugLog("GameManager found! Calling StartBattleTest()");
                gameManager.StartBattleTest();
                UpdateStatus("Battle started via GameManager!");
                return;
            }
            else
            {
                DebugLog("No GameManager found, trying fallback method...");
            }
            
            // Fallback: Create a simple battle manually
            CreateSimpleBattle();
        }
        catch (System.Exception e)
        {
            string errorMsg = $"Error: {e.Message}";
            UpdateStatus(errorMsg);
            DebugLog($"EXCEPTION: {e}");
            DebugLog($"Stack trace: {e.StackTrace}");
        }
    }
    
    void CreateSimpleBattle()
    {
        DebugLog("Creating simple battle...");
        UpdateStatus("Creating simple battle...");
        
        try
        {
            // Create a simple ground plane
            DebugLog("Creating ground plane...");
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(10, 1, 10);
            ground.name = "BattleGround";
            ground.transform.position = Vector3.zero;
            DebugLog("Ground plane created");
            
            // Create some simple units
            DebugLog("Creating units...");
            CreateSimpleUnit("Attacker", new Vector3(-5, 0, 0), Color.red);
            CreateSimpleUnit("Defender", new Vector3(5, 0, 0), Color.blue);
            
            UpdateStatus("Simple battle created! Units are ready to fight.");
            DebugLog("Simple battle creation complete");
        }
        catch (System.Exception e)
        {
            string errorMsg = $"Error creating battle: {e.Message}";
            UpdateStatus(errorMsg);
            DebugLog($"EXCEPTION in CreateSimpleBattle: {e}");
        }
    }
    
    void CreateSimpleUnit(string name, Vector3 position, Color color)
    {
        DebugLog($"Creating unit: {name} at {position}");
        
        try
        {
            var unitGO = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            unitGO.name = name;
            unitGO.transform.position = position;
            
            // Color the unit
            var renderer = unitGO.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = color;
                DebugLog($"Unit {name} colored {color}");
            }
            else
            {
                DebugLog($"WARNING: No renderer found on {name}");
            }
            
            // Add a simple AI script
            var ai = unitGO.AddComponent<SimpleBattleAI>();
            ai.targetTag = name == "Attacker" ? "Defender" : "Attacker";
            
            DebugLog($"Unit {name} created successfully");
        }
        catch (System.Exception e)
        {
            DebugLog($"ERROR creating unit {name}: {e.Message}");
        }
    }
    
    void UpdateStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
            DebugLog($"Status updated: {message}");
        }
        else
        {
            DebugLog($"WARNING: No status text component! Message: {message}");
        }
    }
    
    void DebugLog(string message)
    {
        if (enableDebugLogs)
        {
            Debug.Log($"[QuickBattleStartDebug] {message}");
        }
    }
}

// SimpleBattleAI class is already defined in QuickBattleStart.cs
// This debug version will use the existing SimpleBattleAI class
