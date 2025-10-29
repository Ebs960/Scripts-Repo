using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Quick and dirty battle system starter
/// Just drag this onto any GameObject in your scene and hit Play
/// </summary>
public class QuickBattleStart : MonoBehaviour
{
    [Header("Quick Setup")]
    [Tooltip("Just hit the button to start a battle!")]
    public Button startButton;
    
    [Tooltip("Status text to show what's happening")]
    public Text statusText;
    
    void Start()
    {
        // Create a simple button if none exists
        if (startButton == null)
        {
            CreateQuickUI();
        }
        
        if (startButton != null)
        {
            startButton.onClick.AddListener(StartQuickBattle);
        }
        
        UpdateStatus("Ready! Click the button to start a battle.");
    }
    
    void CreateQuickUI()
    {
        // Create a simple UI if none exists
        var canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            var canvasGO = new GameObject("Canvas");
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
        }
        
        // Create button
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
        
        // Create status text
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
    }
    
    public void StartQuickBattle()
    {
        UpdateStatus("Starting quick battle...");
        
        try
        {
            // Try to use GameManager if available
            var gameManager = FindFirstObjectByType<GameManager>();
            if (gameManager != null)
            {
                gameManager.StartBattleTest();
                UpdateStatus("Battle started via GameManager!");
                return;
            }
            
            // Fallback: Create a simple battle manually
            CreateSimpleBattle();
        }
        catch (System.Exception e)
        {
            UpdateStatus($"Error: {e.Message}");
            Debug.LogError($"QuickBattleStart error: {e}");
        }
    }
    
    void CreateSimpleBattle()
    {
        UpdateStatus("Creating simple battle...");
        
        // Create a simple ground plane
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.localScale = new Vector3(10, 1, 10);
        ground.name = "BattleGround";
        
        // Create some simple units
        CreateSimpleUnit("Attacker", new Vector3(-5, 0, 0), Color.red);
        CreateSimpleUnit("Defender", new Vector3(5, 0, 0), Color.blue);
        
        UpdateStatus("Simple battle created! Units are ready to fight.");
    }
    
    void CreateSimpleUnit(string name, Vector3 position, Color color)
    {
        var unitGO = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        unitGO.name = name;
        unitGO.transform.position = position;
        
        // Color the unit
        var renderer = unitGO.GetComponent<Renderer>();
        renderer.material.color = color;
        
        // Add a simple AI script
        var ai = unitGO.AddComponent<SimpleBattleAI>();
        ai.targetTag = name == "Attacker" ? "Defender" : "Attacker";
        
        UpdateStatus($"Created {name} at {position}");
    }
    
    void UpdateStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
        Debug.Log($"[QuickBattleStart] {message}");
    }
}

/// <summary>
/// Simple AI for testing - just moves toward the target
/// </summary>
public class SimpleBattleAI : MonoBehaviour
{
    public string targetTag = "Player";
    public float moveSpeed = 2f;
    public float attackRange = 2f;
    
    private GameObject target;
    private bool isMoving = false;
    
    void Start()
    {
        // Find target
        target = GameObject.FindGameObjectWithTag(targetTag);
        if (target == null)
        {
            // Fallback: find by name
            target = GameObject.Find(targetTag);
        }
    }
    
    void Update()
    {
        if (target == null) return;
        
        float distance = Vector3.Distance(transform.position, target.transform.position);
        
        if (distance > attackRange)
        {
            // Move toward target
            Vector3 direction = (target.transform.position - transform.position).normalized;
            transform.position += direction * moveSpeed * Time.deltaTime;
            isMoving = true;
        }
        else
        {
            // Attack target
            if (isMoving)
            {
                Debug.Log($"{gameObject.name} attacks {target.name}!");
                isMoving = false;
            }
        }
    }
}
