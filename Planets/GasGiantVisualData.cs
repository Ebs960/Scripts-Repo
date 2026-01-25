using UnityEngine;

[CreateAssetMenu(menuName = "Planets/Gas Giant Visual Data")]
public class GasGiantVisualData : ScriptableObject
{
    public Texture2D baseGradient;
    // Preferred volumetric inputs for HDRP Volumetric Clouds
    // Use Texture3D noise for shape/detail when available (higher quality for raymarching volume)
    public Texture3D shapeNoise3D;
    public Texture3D detailNoise3D;
    // Optional flow map (equirectangular) to drive band advection in shaders/controllers
    public Texture2D flowMap;

    public Color tint = Color.white;
    public float bandSharpness = 1f;
    public float stormStrength = 0.5f;
    public float rotationSpeed = 1f;
}
