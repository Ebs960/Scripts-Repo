using UnityEngine;

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

    private Vector3 originalPosition;
    private Vector3 originalScale;
    private float swayOffset;

    void Start()
    {
        originalPosition = transform.localPosition;
        originalScale = transform.localScale;
        
        // Add random offset to sway timing so decorations don't all sway in sync
        swayOffset = Random.Range(0f, Mathf.PI * 2f);
        
        // Apply random scale variation
        if (randomizeScale)
        {
            float scaleModifier = Random.Range(1f - scaleVariation, 1f + scaleVariation);
            transform.localScale = originalScale * scaleModifier;
        }
        
        // Apply random rotation around up axis
        if (randomizeRotation)
        {
            float randomRotation = Random.Range(0f, 360f);
            transform.Rotate(transform.up, randomRotation, Space.World);
        }
    }

    void Update()
    {
        // Simple swaying animation
        if (enableSwaying)
        {
            float swayX = Mathf.Sin(Time.time * swaySpeed + swayOffset) * swayAmount * 0.01f;
            float swayZ = Mathf.Cos(Time.time * swaySpeed * 0.7f + swayOffset) * swayAmount * 0.01f;
            
            transform.localPosition = originalPosition + new Vector3(swayX, 0, swayZ);
        }
    }

    void OnDrawGizmosSelected()
    {
        // Draw a sphere to show the decoration's influence area
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
        
        // Draw the up direction
        Gizmos.color = Color.blue;
        Gizmos.DrawRay(transform.position, transform.up * 1f);
    }
}
