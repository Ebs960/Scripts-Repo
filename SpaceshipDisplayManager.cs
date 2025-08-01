using UnityEngine;
using System.Collections.Generic;
using System.Collections;

/// <summary>
/// Manages the display of spaceships during space travel loading screens
/// This is a stub implementation for future spaceship visualization features
/// </summary>
public class SpaceshipDisplayManager : MonoBehaviour
{
    [Header("Display Configuration")]
    [Tooltip("Positions where spaceships will be displayed")]
    public Transform[] spaceshipSlots;
    [Tooltip("Default spaceship prefab for when no specific ship is provided")]
    public GameObject defaultSpaceshipPrefab;
    [Tooltip("Scale factor for displayed spaceships")]
    public float spaceshipScale = 1.0f;

    [Header("Animation Settings")]
    [Tooltip("Should spaceships gently float/rotate during display?")]
    public bool enableFloatingAnimation = true;
    [Tooltip("Speed of floating animation")]
    public float floatSpeed = 1.0f;
    [Tooltip("Amplitude of floating movement")]
    public float floatAmplitude = 0.1f;

    [Header("Future Features (Stubs)")]
    [Tooltip("Enable formation flying animation (not yet implemented)")]
    public bool enableFormationFlying = false;
    [Tooltip("Enable spaceship trails/effects (not yet implemented)")]
    public bool enableSpaceshipEffects = false;
    [Tooltip("Enable spaceship information display (not yet implemented)")]
    public bool enableShipInfoDisplay = false;

    // Private fields
    private List<GameObject> displayedSpaceships = new List<GameObject>();
    private List<Vector3> originalPositions = new List<Vector3>();
    private bool isDisplayActive = false;

    void Awake()
    {
        // Initialize spaceship slots if not assigned
        if (spaceshipSlots == null || spaceshipSlots.Length == 0)
        {
            CreateDefaultSlots();
        }

        // Store original positions for animation
        foreach (Transform slot in spaceshipSlots)
        {
            originalPositions.Add(slot.localPosition);
        }
    }

    /// <summary>
    /// Display the provided spaceships in the loading screen
    /// </summary>
    public void DisplaySpaceships(GameObject[] spaceships)
    {
        // Clear any existing display
        ClearDisplay();

        if (spaceships == null || spaceships.Length == 0)
        {
            Debug.Log("[SpaceshipDisplay] No spaceships provided, showing default fleet");
            DisplayDefaultFleet();
            return;
        }

        // Display provided spaceships
        for (int i = 0; i < spaceships.Length && i < spaceshipSlots.Length; i++)
        {
            if (spaceships[i] != null)
            {
                DisplaySpaceship(spaceships[i], i);
            }
        }

        isDisplayActive = true;

        // Start animation if enabled
        if (enableFloatingAnimation)
        {
            StartCoroutine(FloatingAnimationCoroutine());
        }

        Debug.Log($"[SpaceshipDisplay] Displaying {displayedSpaceships.Count} spaceships");
    }

    /// <summary>
    /// Display a default fleet when no specific spaceships are provided
    /// </summary>
    private void DisplayDefaultFleet()
    {
        if (defaultSpaceshipPrefab == null)
        {
            Debug.LogWarning("[SpaceshipDisplay] No default spaceship prefab assigned");
            return;
        }

        // Display default ships in available slots
        for (int i = 0; i < spaceshipSlots.Length; i++)
        {
            DisplaySpaceship(defaultSpaceshipPrefab, i);
        }

        isDisplayActive = true;

        if (enableFloatingAnimation)
        {
            StartCoroutine(FloatingAnimationCoroutine());
        }
    }

    /// <summary>
    /// Display a single spaceship in the specified slot
    /// </summary>
    private void DisplaySpaceship(GameObject spaceshipPrefab, int slotIndex)
    {
        if (slotIndex >= spaceshipSlots.Length) return;

        Transform slot = spaceshipSlots[slotIndex];
        GameObject spaceshipInstance = Instantiate(spaceshipPrefab, slot);
        
        // Configure the spaceship instance
        spaceshipInstance.transform.localPosition = Vector3.zero;
        spaceshipInstance.transform.localRotation = Quaternion.identity;
        spaceshipInstance.transform.localScale = Vector3.one * spaceshipScale;

        // Disable any gameplay components (this is just for display)
        DisableGameplayComponents(spaceshipInstance);

        displayedSpaceships.Add(spaceshipInstance);
    }

    /// <summary>
    /// Clear all displayed spaceships
    /// </summary>
    public void ClearDisplay()
    {
        foreach (GameObject ship in displayedSpaceships)
        {
            if (ship != null)
            {
                DestroyImmediate(ship);
            }
        }

        displayedSpaceships.Clear();
        isDisplayActive = false;
        StopAllCoroutines();

        Debug.Log("[SpaceshipDisplay] Display cleared");
    }

    /// <summary>
    /// Create default spaceship display slots
    /// </summary>
    private void CreateDefaultSlots()
    {
        List<Transform> slots = new List<Transform>();

        // Create 3 default slots in a formation
        for (int i = 0; i < 3; i++)
        {
            GameObject slot = new GameObject($"SpaceshipSlot_{i}");
            slot.transform.SetParent(transform);
            
            // Position slots in a triangular formation
            float angle = i * 120f * Mathf.Deg2Rad;
            float radius = 2.0f;
            slot.transform.localPosition = new Vector3(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius * 0.5f,
                0f
            );
            
            slots.Add(slot.transform);
        }

        spaceshipSlots = slots.ToArray();
        Debug.Log("[SpaceshipDisplay] Created 3 default spaceship slots");
    }

    /// <summary>
    /// Disable gameplay components on display spaceships
    /// </summary>
    private void DisableGameplayComponents(GameObject spaceship)
    {
        // Disable common gameplay components that shouldn't be active during display
        var colliders = spaceship.GetComponentsInChildren<Collider>();
        foreach (var col in colliders)
        {
            col.enabled = false;
        }

        var rigidbodies = spaceship.GetComponentsInChildren<Rigidbody>();
        foreach (var rb in rigidbodies)
        {
            rb.isKinematic = true;
        }

        // Disable any unit controllers or AI components
        var unitControllers = spaceship.GetComponentsInChildren<MonoBehaviour>();
        foreach (var controller in unitControllers)
        {
            // Only disable known gameplay components to avoid breaking display components
            if (controller.GetType().Name.Contains("Unit") || 
                controller.GetType().Name.Contains("AI") ||
                controller.GetType().Name.Contains("Movement"))
            {
                controller.enabled = false;
            }
        }
    }

    /// <summary>
    /// Floating animation coroutine for displayed spaceships
    /// </summary>
    private IEnumerator FloatingAnimationCoroutine()
    {
        float time = 0f;

        while (isDisplayActive)
        {
            time += Time.deltaTime * floatSpeed;

            for (int i = 0; i < spaceshipSlots.Length && i < originalPositions.Count; i++)
            {
                if (spaceshipSlots[i] != null)
                {
                    // Calculate floating offset
                    float phaseOffset = i * 0.5f; // Offset each ship slightly
                    Vector3 floatOffset = new Vector3(
                        Mathf.Sin(time + phaseOffset) * floatAmplitude * 0.5f,
                        Mathf.Cos(time + phaseOffset) * floatAmplitude,
                        Mathf.Sin(time * 0.7f + phaseOffset) * floatAmplitude * 0.3f
                    );

                    spaceshipSlots[i].localPosition = originalPositions[i] + floatOffset;

                    // Gentle rotation
                    spaceshipSlots[i].Rotate(Vector3.up, Time.deltaTime * 10f, Space.Self);
                }
            }

            yield return null;
        }
    }

    #region Future Feature Stubs

    /// <summary>
    /// STUB: Future feature for formation flying animation
    /// </summary>
    public void StartFormationFlying()
    {
        if (!enableFormationFlying) return;
        
        Debug.Log("[SpaceshipDisplay] STUB: Formation flying not yet implemented");
        // TODO: Implement formation flying patterns
        // - Line formation
        // - Delta formation  
        // - Diamond formation
        // - Custom formations based on fleet composition
    }

    /// <summary>
    /// STUB: Future feature for spaceship visual effects
    /// </summary>
    public void EnableSpaceshipEffects(bool enable)
    {
        if (!enableSpaceshipEffects) return;
        
        Debug.Log("[SpaceshipDisplay] STUB: Spaceship effects not yet implemented");
        // TODO: Implement spaceship effects
        // - Engine trails
        // - Shield effects
        // - Weapon glow
        // - Hull materials
        // - Damage states
    }

    /// <summary>
    /// STUB: Future feature for displaying ship information
    /// </summary>
    public void ShowShipInformation(GameObject spaceship)
    {
        if (!enableShipInfoDisplay) return;
        
        Debug.Log("[SpaceshipDisplay] STUB: Ship information display not yet implemented");
        // TODO: Implement ship info display
        // - Ship name and class
        // - Crew count
        // - Weapon loadout
        // - Special capabilities
        // - Ship history/achievements
    }

    /// <summary>
    /// STUB: Future feature for interactive spaceship selection
    /// </summary>
    public void OnSpaceshipClicked(GameObject spaceship)
    {
        Debug.Log("[SpaceshipDisplay] STUB: Interactive spaceship selection not yet implemented");
        // TODO: Implement spaceship interaction
        // - Click to select ship
        // - Show detailed information
        // - Allow ship customization
        // - Ship comparison features
    }

    #endregion
}
