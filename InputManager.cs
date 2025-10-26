using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Centralized input manager with priority system.
/// Handles all input to prevent conflicts between systems.
/// </summary>
public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }

    // Priority levels (higher number = higher priority)
    public enum InputPriority
    {
        Background = 0,        // TileSystem, camera
        Gameplay = 1,          // Unit selection, unit movement
        UI = 2,                // UI panels, context menus
        Modal = 3              // Dialogs, pause menus
    }

    private InputPriority currentPriority = InputPriority.Background;
    private bool isInputEnabled = true;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Update()
    {
        // MIGRATED: InputManager no longer processes input directly
        // Systems now handle their own input with priority checks
        // This manager only provides priority coordination
    }

    /// <summary>
    /// Check if mouse is over UI element
    /// </summary>
    public bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    /// <summary>
    /// Check if input is enabled (not blocked by modal dialogs)
    /// </summary>
    public bool IsInputEnabled()
    {
        return isInputEnabled;
    }

    /// <summary>
    /// Set input enabled state (for pause menus, dialogs)
    /// </summary>
    public void SetInputEnabled(bool enabled)
    {
        isInputEnabled = enabled;
    }

    /// <summary>
    /// Set priority level (called by systems when they take control)
    /// </summary>
    public void SetPriority(InputPriority priority)
    {
        currentPriority = priority;
    }

    /// <summary>
    /// Check if current system can process input at its priority level
    /// </summary>
    public bool CanProcessInput(InputPriority priority)
    {
        return priority >= currentPriority;
    }
}

