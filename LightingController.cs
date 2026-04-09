using UnityEngine;

// Simple scene-level lighting controller for terrain shaders.
[ExecuteAlways]
public class LightingController : MonoBehaviour
{
    public static LightingController Instance { get; private set; }

    [Tooltip("Normalized sun direction (pointing FROM sun towards scene).")]
    public Vector3 sunDirection = new Vector3(0.3f, -0.8f, 0.5f);

    public Color sunColor = new Color(1f, 0.95f, 0.85f, 1f);

    [Range(0f, 10f)]
    public float sunIntensity = 1.5f;

    [Header("Auto Sync")]
    [Tooltip("When true, automatically copy Directional Light (or RenderSettings.sun) into this controller.")]
    public bool autoSyncToSceneSun = true;

    [Tooltip("How often (seconds) to poll the scene light for changes when auto-sync is enabled.")]
    public float syncInterval = 0.25f;

    private Light _cachedSceneSun;
    private float _lastSyncTime = -999f;
    private Vector3 _lastSyncedDir;
    private Color _lastSyncedColor;
    private float _lastSyncedIntensity;

    private void OnEnable()
    {
        Instance = this;
        NormalizeDirection();
    }

    private void OnDisable()
    {
        if (Instance == this) Instance = null;
    }

    private void OnValidate()
    {
        NormalizeDirection();
    }

    private void Update()
    {
        if (!autoSyncToSceneSun) return;

        if (Time.realtimeSinceStartup - _lastSyncTime < syncInterval) return;
        _lastSyncTime = Time.realtimeSinceStartup;

        // Prefer explicit RenderSettings.sun if assigned, otherwise find any active directional light
        Light sceneSun = RenderSettings.sun;
        if (sceneSun == null || !sceneSun.isActiveAndEnabled)
        {
            // Find first active directional light in scene
            var lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (var l in lights)
            {
                if (l == null) continue;
                if (l.type == LightType.Directional && l.enabled && l.gameObject.activeInHierarchy)
                {
                    sceneSun = l;
                    break;
                }
            }
        }

        if (sceneSun == null)
            return;

        // Direction: convert light forward into "sun -> scene" convention used by shader/controller
        Vector3 dir = -sceneSun.transform.forward;
        Color col = sceneSun.color;
        float inten = sceneSun.intensity;

        bool changed = false;
        if ((dir - _lastSyncedDir).sqrMagnitude > 1e-4f)
        {
            sunDirection = dir.normalized;
            _lastSyncedDir = sunDirection;
            changed = true;
        }
        if ((col - _lastSyncedColor).maxColorComponent > 1e-3f)
        {
            sunColor = col;
            _lastSyncedColor = col;
            changed = true;
        }
        if (Mathf.Abs(inten - _lastSyncedIntensity) > 1e-3f)
        {
            sunIntensity = inten;
            _lastSyncedIntensity = inten;
            changed = true;
        }

        if (changed)
        {
            NormalizeDirection();
            // Refresh terrain materials so they pick up the new sun values
            var mgr = FindAnyObjectByType<HexMapChunkManager>();
            if (mgr != null) mgr.RefreshAllChunks();
        }
    }

    private void NormalizeDirection()
    {
        if (sunDirection.sqrMagnitude > 0.0001f)
            sunDirection = sunDirection.normalized;
    }
}
