using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Example decoration script that adds some basic functionality to decoration objects
/// Attach this to decoration prefabs for additional features
/// </summary>
public class DecorationObject : MonoBehaviour
{
    [Header("Decoration Settings")]
    [Tooltip("Should this decoration sway in the wind?")]
    public bool enableSwaying = false;
    
    [Range(0.1f, 2.0f)]
    [Tooltip("How much the decoration sways")]
    public float swayAmount = 0.5f;
    
    [Range(0.1f, 3.0f)]
    [Tooltip("How fast the decoration sways")]
    public float swaySpeed = 1.0f;
    
    [Tooltip("Should this decoration have a random scale variation?")]
    public bool randomizeScale = true;
    
    [Range(0.1f, 0.5f)]
    [Tooltip("How much to vary the scale randomly")]
    public float scaleVariation = 0.2f;
    
    [Tooltip("Should this decoration randomly rotate around its up axis?")]
    public bool randomizeRotation = true;
    
    [Tooltip("Biomes where this decoration is common (for reference)")]
    public Biome[] preferredBiomes;

    // --- Static batch manager: one Update() for ALL swaying decorations ---
    private static readonly List<DecorationObject> _swayingInstances = new List<DecorationObject>(512);
    private static bool _managerRegistered;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _swayingInstances.Clear();
        _managerRegistered = false;
    }

    internal Vector3 originalPosition;
    internal float swayOffset;
    private Vector3 originalScale;
    private bool _registeredForSway;

    void Start()
    {
        originalPosition = transform.localPosition;
        originalScale = transform.localScale;
        
        swayOffset = Random.Range(0f, Mathf.PI * 2f);
        
        if (randomizeScale)
        {
            float scaleModifier = Random.Range(1f - scaleVariation, 1f + scaleVariation);
            transform.localScale = originalScale * scaleModifier;
        }
        
        if (randomizeRotation)
        {
            float randomRotation = Random.Range(0f, 360f);
            transform.Rotate(transform.up, randomRotation, Space.World);
        }

        if (enableSwaying)
            RegisterForSway();
    }

    void OnEnable()
    {
        if (enableSwaying && !_registeredForSway)
            RegisterForSway();
    }

    void OnDisable()
    {
        UnregisterForSway();
    }

    void OnDestroy()
    {
        UnregisterForSway();
    }

    private void RegisterForSway()
    {
        if (_registeredForSway) return;
        _registeredForSway = true;
        _swayingInstances.Add(this);

        if (!_managerRegistered)
        {
            _managerRegistered = true;
            DecorationSwayManager.EnsureExists();
        }
    }

    private void UnregisterForSway()
    {
        if (!_registeredForSway) return;
        _registeredForSway = false;
        _swayingInstances.Remove(this);
    }

    // No Update() — all sway work is done by DecorationSwayManager in a single batch.

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
        Gizmos.color = Color.blue;
        Gizmos.DrawRay(transform.position, transform.up * 1f);
    }

    /// <summary>
    /// Single MonoBehaviour that batch-updates all swaying decorations.
    /// Only ONE Update() call per frame regardless of decoration count.
    /// </summary>
    internal class DecorationSwayManager : MonoBehaviour
    {
        private static DecorationSwayManager _instance;

        internal static void EnsureExists()
        {
            if (_instance != null) return;
            var go = new GameObject("[DecorationSwayManager]");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<DecorationSwayManager>();
        }

        void Update()
        {
            float t = Time.time;
            int count = _swayingInstances.Count;
            // Process a quarter of the list each frame (staggered batch)
            int bucket = Time.frameCount & 3; // 0,1,2,3
            for (int i = bucket; i < count; i += 4)
            {
                var deco = _swayingInstances[i];
                if (deco == null) continue;
                float off = deco.swayOffset;
                float spd = deco.swaySpeed;
                float amt = deco.swayAmount * 0.01f;
                float swayX = Mathf.Sin(t * spd + off) * amt;
                float swayZ = Mathf.Cos(t * spd * 0.7f + off) * amt;
                deco.transform.localPosition = deco.originalPosition + new Vector3(swayX, 0, swayZ);
            }
        }
    }
}
