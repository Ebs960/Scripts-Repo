using UnityEngine;

/// <summary>
/// Optional singleton to centrally control verbose logging for gas-giant components.
/// Add this to a root GasGiant GameObject (or anywhere in the scene) and toggle VerboseLogs.
/// Components will respect the global flag in addition to their local `verboseLogs` setting.
/// </summary>
public class GasGiantDebug : MonoBehaviour
{
    public static GasGiantDebug Instance { get; private set; }

    [Tooltip("Enable verbose logs for all gas-giant components when present")]
    public bool verboseLogs = false;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[GasGiantDebug] Multiple instances detected; using the first one.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
